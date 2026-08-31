using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Threading.Tasks;
using WinRT.Interop;
using Windows.Devices.Enumeration;

namespace NFC_System
{
    public sealed partial class EventAttendanceWindow : Window
    {
        private readonly DatabaseService _database = new();
        private readonly VerificationEngine _engine;
        private bool _isInitializing = true;

        // THE FIX: Required state variables for the Exit Interceptor
        private SerialPort? _serialPort;
        private string _currentPort = "COM3";
        private bool _isForceClosing = false;
        private bool _isAwaitingAdminAuth = false;
        private string _pendingAdminAction = "";
        private string _pendingAdminSeverity = "";

        private readonly DispatcherTimer _liveFeedTimer = new();

        public EventAttendanceWindow()
        {
            this.InitializeComponent();
            DatabaseMonitor.ConnectionStatusChanged += UpdateOfflineBanner;
            UpdateOfflineBanner(DatabaseMonitor.IsOnline);
            _engine = new VerificationEngine(_database);

            MaximizeWindow();

            // Hook into native window closing event to intercept exit
            IntPtr hWnd = WindowNative.GetWindowHandle(this);
            WindowId windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
            AppWindow appWindow = AppWindow.GetFromWindowId(windowId);
            appWindow.Closing += AppWindow_Closing;
            this.Closed += Window_Closed;

            _liveFeedTimer.Interval = TimeSpan.FromSeconds(10);
            _liveFeedTimer.Tick += (s, e) =>
            {
                _liveFeedTimer.Stop();
                LiveFeedActivePanel.Visibility = Visibility.Collapsed;
                LiveFeedIdlePanel.Visibility = Visibility.Visible;
            };

            _ = InitializeAsync();
        }

        // ====================================================================
        // RBAC SEVERITY-AWARE EXIT INTERCEPTOR
        // ====================================================================
        private async void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
        {
            if (_isForceClosing) return;
            args.Cancel = true;

            // 1. MASTER ADMINISTRATOR FLOW (Bypass Authorization)
            if (AppSession.CurrentStaffRoleLabel == "Master Admin")
            {
                ContentDialog masterDialog = new ContentDialog
                {
                    Title = "Exit Application",
                    Content = "You may have unsynced offline data. Would you like to push it to the cloud before exiting?",
                    PrimaryButtonText = "Push to Cloud & Exit",
                    SecondaryButtonText = "Exit Anyway",
                    CloseButtonText = "Cancel",
                    XamlRoot = this.Content.XamlRoot
                };

                var result = await masterDialog.ShowAsync();
                if (result == ContentDialogResult.Primary) await PerformCloudPushAndExit();
                else if (result == ContentDialogResult.Secondary) ForceExit();
            }
            // 2. STANDARD ADMINISTRATOR FLOW (High Severity - Needs PIN + Tap)
            else if (AppSession.IsAdmin)
            {
                ContentDialog adminDialog = new ContentDialog
                {
                    Title = "Exit Application",
                    Content = "You have unsynced offline data. Pushing this to the cloud requires High-Severity authorization (PIN + NFC Tap).",
                    PrimaryButtonText = "Authorize Sync & Exit",
                    SecondaryButtonText = "Exit Without Syncing",
                    CloseButtonText = "Cancel",
                    XamlRoot = this.Content.XamlRoot
                };

                var result = await adminDialog.ShowAsync();
                if (result == ContentDialogResult.Primary)
                {
                    _pendingAdminAction = "EXIT_SYNC";
                    _pendingAdminSeverity = "HIGH";

                    AdminPinBox.Visibility = Visibility.Visible;
                    AdminPinBox.Password = "";
                    AuthStatusText.Visibility = Visibility.Collapsed;
                    AdminAuthDescriptionText.Text = "To confirm this cloud upload, enter your 4-digit PIN and tap your Admin NFC card.";

                    _isAwaitingAdminAuth = true;
                    AdminAuthDialog.XamlRoot = this.Content.XamlRoot;
                    var authResult = await AdminAuthDialog.ShowAsync();

                    if (authResult == ContentDialogResult.None && _isAwaitingAdminAuth)
                    {
                        _isAwaitingAdminAuth = false;
                        _pendingAdminAction = "";
                    }
                }
                else if (result == ContentDialogResult.Secondary)
                {
                    ForceExit();
                }
            }
            // 3. ORGANIZER & GUARD FLOW (Read-Only/Low Severity Restriction)
            else
            {
                ContentDialog restrictedDialog = new ContentDialog
                {
                    Title = "Exit Application",
                    Content = "Warning: There may be unsynced offline data. You do not have Administrator privileges to push this data to the cloud. If you exit now, the data will remain safely stored locally.",
                    PrimaryButtonText = "Exit Anyway",
                    CloseButtonText = "Cancel",
                    XamlRoot = this.Content.XamlRoot
                };

                restrictedDialog.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Orange);

                var result = await restrictedDialog.ShowAsync();
                if (result == ContentDialogResult.Primary) ForceExit();
            }
        }

        private async Task PerformCloudPushAndExit()
        {
            try
            {
                if (await _database.TestConnectionAsync())
                {
                    await _database.SyncOfflineLogsToServerAsync();
                    await _database.PushStudentsToCloudAsync();
                    await _database.PushStaffToCloudAsync();
                    await _database.PushCoursesToCloudAsync();
                    await _database.PushEventsToCloudAsync();
                    await _database.PushEventApprovedStudentsToCloudAsync();
                    await _database.PushLogsToCloudAsync();
                    await _database.PushEventAttendanceToCloudAsync();
                    await _database.AddAlertAsync(AppSession.CurrentStaffName, "ADMIN_ACTION", "Authorized Cloud Push on Application Exit.");
                }
            }
            catch { }

            ForceExit();
        }

        private void ForceExit()
        {
            if (DatabaseMonitor.IsOnline && AppSession.IsLoggedIn)
            {
                try { _ = _database.AddAlertAsync(AppSession.CurrentStaffName, "STAFF_LOGOUT", $"{AppSession.CurrentStaffName} closed the application."); } catch { }
            }

            _isForceClosing = true;
            Application.Current.Exit();
        }

        // ====================================================================
        // SERIAL PORT & AUTHORIZATION HANDLING
        // ====================================================================
        private void TryConnectSerial(string portName)
        {
            if (_serialPort != null && _serialPort.IsOpen) return;

            try
            {
                _serialPort = new SerialPort(portName, 115200);
                _serialPort.NewLine = "\n";
                _serialPort.DataReceived += SerialPort_DataReceived;
                _serialPort.Open();
            }
            catch { }
        }

        private void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                if (_serialPort == null || !_serialPort.IsOpen) return;
                string line = _serialPort.ReadLine().Trim();
                if (!line.StartsWith("UID=")) return;

                string uid = line.Substring(4).Trim();

                if (_isAwaitingAdminAuth)
                {
                    DispatcherQueue.TryEnqueue(async () => await HandleAdminAuthScanAsync(uid));
                }
            }
            catch { }
        }

        private async Task HandleAdminAuthScanAsync(string uid)
        {
            string? role = null;
            string? pinHash = null;
            string? pinSalt = null;

            if (DatabaseMonitor.IsOnline)
            {
                try
                {
                    var details = await _database.GetStaffDetailsAsync(uid);
                    role = details.Role;
                    pinHash = details.PinHash;
                    pinSalt = details.PinSalt;
                }
                catch { }
            }

            if (role == null && uid == "04:A1:B2:C3")
            {
                role = "Master Administrator";
            }

            bool isAuthorized = false;
            string failReason = "";

            if (_pendingAdminSeverity == "HIGH")
            {
                if (role == "Administrator" || role == "Master Administrator")
                {
                    string enteredPin = AdminPinBox.Password.Trim();
                    if (string.IsNullOrEmpty(enteredPin)) failReason = "Authorization Denied: A 4-digit Staff PIN is required.";
                    else if (string.IsNullOrEmpty(pinHash)) failReason = "Authorization Denied: Tapped account does not have a PIN configured.";
                    else if (!PinHasher.VerifyPin(enteredPin, pinSalt!, pinHash)) failReason = "Authorization Denied: Invalid PIN.";
                    else isAuthorized = true;
                }
                else failReason = "Authorization Denied: Tapped card is not an Administrator.";
            }

            if (isAuthorized)
            {
                _isAwaitingAdminAuth = false;
                AdminAuthDialog.Hide();

                if (_pendingAdminAction == "EXIT_SYNC")
                {
                    _pendingAdminAction = "";
                    await PerformCloudPushAndExit();
                }
            }
            else
            {
                AuthStatusText.Text = failReason;
                AuthStatusText.Visibility = Visibility.Visible;
                PlaySecurityAlert();
            }
        }

        private void CloseSerialPort()
        {
            try
            {
                if (_serialPort != null && _serialPort.IsOpen)
                {
                    _serialPort.DataReceived -= SerialPort_DataReceived;
                    _serialPort.Close();
                    _serialPort.Dispose();
                    _serialPort = null;
                }
            }
            catch { }
        }

        private void Window_Closed(object sender, WindowEventArgs args)
        {
            DatabaseMonitor.ConnectionStatusChanged -= UpdateOfflineBanner;
            CloseSerialPort();
        }

        // ====================================================================
        // STANDARD WINDOW LOGIC
        // ====================================================================
        private async System.Threading.Tasks.Task InitializeAsync()
        {
            if (DatabaseMonitor.IsOnline)
            {
                try { await _database.EnsureSchemaAsync(); } catch { }
            }

            try
            {
                var cameras = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);
                CameraComboBox.ItemsSource = cameras;

                string savedCamId = "";
                if (DatabaseMonitor.IsOnline)
                {
                    try
                    {
                        savedCamId = await _database.GetSettingAsync("selected_camera", "");
                        _currentPort = await _database.GetSettingAsync("nfc_com_port", "COM3");
                    }
                    catch { }
                }

                TryConnectSerial(_currentPort);

                if (!string.IsNullOrEmpty(savedCamId))
                    CameraComboBox.SelectedItem = cameras.FirstOrDefault(c => c.Id == savedCamId) ?? cameras.FirstOrDefault();
                else
                    CameraComboBox.SelectedItem = cameras.FirstOrDefault();

                DirectionComboBox.SelectedIndex = KioskStateController.CurrentType == TransactionType.Entry ? 0 : 1;

                await LoadActiveEventsAsync();

                AttendanceLogListView.Items.Insert(0, "[INFO] Event attendance monitor ready.");

                if (!AppSession.IsAdmin && !AppSession.IsEventOrganizer)
                {
                    ManageEventsButton.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                AttendanceLogListView.Items.Insert(0, $"[SYSTEM ERROR] {ex.Message}");
            }
            finally
            {
                _isInitializing = false;
            }
        }

        private async void CameraComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CameraComboBox.SelectedItem is DeviceInformation selectedCam)
            {
                KioskStateController.SelectedCameraId = selectedCam.Id;
                try { await _database.SetSettingAsync("selected_camera", selectedCam.Id); } catch { }
            }
        }

        private async System.Threading.Tasks.Task LoadActiveEventsAsync()
        {
            IReadOnlyList<EventRecord> events = new List<EventRecord>();

            if (DatabaseMonitor.IsOnline)
            {
                try { events = await _database.GetActiveEventsAsync(); } catch { }
            }

            if (events == null || events.Count == 0)
            {
                try
                {
                    var cachedEvents = OfflineCacheService.GetCachedEvents();
                    if (cachedEvents != null && cachedEvents.Count > 0)
                    {
                        events = cachedEvents
                            .Where(e => e.IsActive)
                            .Select(e => new EventRecord
                            {
                                EventId = e.EventId,
                                EventName = e.EventName,
                                VerificationMode = Enum.TryParse<VerificationMode>(e.VerificationMode, out var vMode) ? vMode : VerificationMode.Standard,
                                IsRestricted = e.IsRestricted,
                                Status = "Active"
                            })
                            .ToList();
                    }
                }
                catch { }
            }

            ActiveEventComboBox.ItemsSource = events;

            if (events != null && events.Count > 0)
            {
                ActiveEventComboBox.SelectedIndex = 0;
            }

            string sourceTag = DatabaseMonitor.IsOnline && events != null && events.Count > 0 ? "Online Database" : "Offline Cache";
            AttendanceLogListView.Items.Insert(0, $"[INFO] Loaded {events?.Count ?? 0} active event(s) via {sourceTag}.");
        }

        private void UpdateOfflineBanner(bool isOnline)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (GlobalOfflineBanner != null)
                {
                    GlobalOfflineBanner.Visibility = isOnline ? Visibility.Collapsed : Visibility.Visible;
                }
            });
        }

        private async void RefreshEventsButton_Click(object sender, RoutedEventArgs e)
        {
            await LoadActiveEventsAsync();
        }

        private async void ActiveEventComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            BroadcastStateToKiosk();

            if (!_isInitializing && ActiveEventComboBox.SelectedItem is EventRecord selectedEvent)
            {
                string staff = AppSession.CurrentStaffName;

                AttendanceLogListView.Items.Insert(0, $"[AUDIT] Terminal set to {selectedEvent.EventName} by {staff}");

                if (DatabaseMonitor.IsOnline)
                {
                    try { await _database.AddAlertAsync(staff, "ADMIN_ACTION", $"Set Event Terminal to monitor '{selectedEvent.EventName}'."); } catch { }
                }
            }
        }

        private async void DirectionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            BroadcastStateToKiosk();

            if (!_isInitializing)
            {
                string direction = DirectionComboBox.SelectedIndex == 1 ? "Exit" : "Entry";
                string staff = AppSession.CurrentStaffName;

                AttendanceLogListView.Items.Insert(0, $"[AUDIT] Direction changed to {direction} by {staff}");

                if (DatabaseMonitor.IsOnline)
                {
                    try { await _database.AddAlertAsync(staff, "ADMIN_ACTION", $"Changed Event Terminal Direction to {direction}."); } catch { }
                }
            }
        }

        private void BroadcastStateToKiosk()
        {
            if (DirectionComboBox == null || ActiveEventComboBox == null) return;

            TransactionType type = DirectionComboBox.SelectedIndex == 1 ? TransactionType.Exit : TransactionType.Entry;
            EventRecord? selectedEvent = ActiveEventComboBox.SelectedItem as EventRecord;
            VerificationMode mode = selectedEvent?.VerificationMode ?? VerificationMode.Standard;

            KioskStateController.BroadcastModeChange(mode, type);
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            CloseSerialPort();
            var dashboard = new MainWindow();
            dashboard.Activate();
            this.Close();
        }

        private void ManageEventsButton_Click(object sender, RoutedEventArgs e)
        {
            CloseSerialPort();
            var eventManager = new EventManagementWindow();
            eventManager.Activate();
            this.Close();
        }

        private void LaunchKioskButton_Click(object sender, RoutedEventArgs e)
        {
            BroadcastStateToKiosk();

            // THE FIX: Explicitly yield the COM port to the new Kiosk Window
            CloseSerialPort();

            EventRecord? selectedEvent = ActiveEventComboBox.SelectedItem as EventRecord;
            string activeEventName = selectedEvent != null ? selectedEvent.DisplayName : "No Event Selected";
            string mode = selectedEvent != null ? selectedEvent.VerificationMode.ToString() : "Standard";
            string type = (DirectionComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Entry";

            var kiosk = new KioskModeWindow("Event", $"{activeEventName} | {type} ({mode})", selectedEvent?.EventId);

            KioskModeWindow.OnKioskOutcome -= ApplyOutcomeFromKiosk;
            KioskModeWindow.OnKioskOutcome += ApplyOutcomeFromKiosk;

            KioskModeWindow.OnKioskLog -= AddKioskLog;
            KioskModeWindow.OnKioskLog += AddKioskLog;

            kiosk.Closed += (s, args) =>
            {
                KioskModeWindow.OnKioskOutcome -= ApplyOutcomeFromKiosk;
                KioskModeWindow.OnKioskLog -= AddKioskLog;

                // Reclaim the COM port when the Kiosk is closed so the Exit Interceptor works again
                TryConnectSerial(_currentPort);
            };

            kiosk.Activate();
        }

        private void ApplyOutcomeFromKiosk(VerificationOutcome outcome)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                ApplyOutcome(outcome);
            });
        }

        private void AddKioskLog(string msg)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                AttendanceLogListView.Items.Insert(0, msg);
            });
        }

        private void PlaySecurityAlert()
        {
            Task.Run(() =>
            {
                for (int i = 0; i < 3; i++)
                {
                    Console.Beep(2500, 300);
                    System.Threading.Thread.Sleep(100);
                }
            });
        }

        private async void UnlockAccountButton_Click(object sender, RoutedEventArgs e)
        {
            string studentId = OverrideStudentIdBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(studentId)) return;

            try
            {
                var student = await _database.GetStudentByIdAsync(studentId);
                if (student == null)
                {
                    AttendanceLogListView.Items.Insert(0, $"[ERROR] Unlock Aborted: Student ID '{studentId}' does not exist in the database.");
                    PlaySecurityAlert();
                    return;
                }

                await _database.UpdatePinFailureAsync(studentId, 0, false);

                if (DatabaseMonitor.IsOnline)
                {
                    try { await _database.AddAlertAsync(AppSession.CurrentStaffName, "ADMIN_OVERRIDE", $"Manually cleared 2FA lockout for {student.FullName} ({studentId})."); } catch { }
                }

                AttendanceLogListView.Items.Insert(0, $"[SECURITY OVERRIDE] Guard cleared lockout for {student.FullName} ({studentId}).");
                OverrideStudentIdBox.Text = "";
            }
            catch (Exception ex)
            {
                AttendanceLogListView.Items.Insert(0, $"[DB ERROR] {ex.Message}");
            }
        }

        private async void ApplyOutcome(VerificationOutcome outcome)
        {
            if (!string.IsNullOrWhiteSpace(outcome.LogLine))
            {
                AttendanceLogListView.Items.Insert(0, outcome.LogLine);
            }

            // THE FIX: Added "ACCOUNT_LOCKED" and "IRREGULAR_EXIT_SEQUENCE" so the hardware siren triggers during offline violations
            string[] severeErrors = { "PIN_LOCKED", "ACCOUNT_LOCKED", "ANTI_TAILGATING_VIOLATION", "IRREGULAR_EXIT_SEQUENCE", "UNAUTHORIZED_EVENT_ACCESS", "NOT_REGISTERED", "CREDENTIAL_MISMATCH", "INACTIVE_STUDENT" };
            if (severeErrors.Contains(outcome.ErrorCategory))
            {
                PlaySecurityAlert();
            }

            if (outcome.Step == VerificationStep.Completed && outcome.Student != null)
            {
                OverrideStudentIdBox.Text = outcome.Student.StudentId;

                LiveStudentPhoto.ProfilePicture = await ImageHelper.GetBitmapAsync(outcome.Student.PhotoData);

                LiveStudentName.Text = outcome.Student.FullName;
                LiveStudentDetails.Text = $"{outcome.Student.Course} • Year {outcome.Student.YearLevel} • {outcome.Student.SectionName}";

                if (outcome.IsGranted)
                {
                    LiveResultBorder.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(26, 52, 211, 153));
                    LiveResultBorder.BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(48, 52, 211, 153));
                    LiveResultText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 52, 211, 153));
                    LiveResultText.Text = "ATTENDANCE GRANTED";
                }
                else
                {
                    LiveResultBorder.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(26, 248, 113, 113));
                    LiveResultBorder.BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(48, 248, 113, 113));
                    LiveResultText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 248, 113, 113));
                    LiveResultText.Text = $"DENIED: {outcome.ErrorCategory.Replace("_", " ")}";
                }

                LiveFeedIdlePanel.Visibility = Visibility.Collapsed;
                LiveFeedActivePanel.Visibility = Visibility.Visible;

                _liveFeedTimer.Stop();
                _liveFeedTimer.Start();
            }
            else
            {
                OverrideStudentIdBox.Text = "";
            }
        }

        private void MaximizeWindow()
        {
            IntPtr hWnd = WindowNative.GetWindowHandle(this);
            WindowId windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
            AppWindow appWindow = AppWindow.GetFromWindowId(windowId);
            if (appWindow.Presenter is OverlappedPresenter presenter) presenter.Maximize();
        }
    }
}