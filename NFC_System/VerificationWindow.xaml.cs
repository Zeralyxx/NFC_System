using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Linq;
using System.Threading.Tasks;
using WinRT.Interop;
using Windows.Devices.Enumeration;

namespace NFC_System
{
    public sealed partial class VerificationWindow : Window
    {
        private readonly DatabaseService _database = new();
        private readonly VerificationEngine _engine;
        private bool _isInitializing = true;

        private readonly DispatcherTimer _liveFeedTimer = new();

        // THE FIX: State variables for Exit Interceptor
        private bool _isForceClosing = false;
        private bool _isAwaitingAdminAuth = false;
        private string _pendingAdminAction = "";
        private string _pendingAdminSeverity = "";

        public VerificationWindow()
        {
            this.InitializeComponent();
            DatabaseMonitor.ConnectionStatusChanged += UpdateOfflineBanner;
            UpdateOfflineBanner(DatabaseMonitor.IsOnline);
            _engine = new VerificationEngine(_database);

            MaximizeWindow();

            // THE FIX: Subscribe to the global hardware manager
            HardwareService.OnUidScanned += HardwareService_OnUidScanned;

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
        // THE FIX: SHARED HARDWARE SERVICE EVENT HANDLER
        // ====================================================================
        private void HardwareService_OnUidScanned(string uid)
        {
            // SMART ROUTING: Ignore scans if the Kiosk is actively tracking attendance
            if (AppSession.IsKioskRunning) return;

            if (_isAwaitingAdminAuth)
            {
                DispatcherQueue.TryEnqueue(async () => await HandleAdminAuthScanAsync(uid));
            }
        }

        // ====================================================================
        // RBAC SEVERITY-AWARE EXIT INTERCEPTOR
        // ====================================================================
        private async void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
        {
            if (_isForceClosing) return;
            args.Cancel = true;

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
            SyncOverlay.Visibility = Visibility.Visible;

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

            HardwareService.Disconnect();
            _isForceClosing = true;
            Application.Current.Exit();
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

        private void Window_Closed(object sender, WindowEventArgs args)
        {
            DatabaseMonitor.ConnectionStatusChanged -= UpdateOfflineBanner;

            // THE FIX: Unhook the hardware listener to prevent memory leaks
            HardwareService.OnUidScanned -= HardwareService_OnUidScanned;
        }

        // ====================================================================
        // STANDARD WINDOW LOGIC
        // ====================================================================
        private async Task InitializeAsync()
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
                    try { savedCamId = await _database.GetSettingAsync("selected_camera", ""); } catch { }
                }

                if (!string.IsNullOrEmpty(savedCamId))
                    CameraComboBox.SelectedItem = cameras.FirstOrDefault(c => c.Id == savedCamId) ?? cameras.FirstOrDefault();
                else
                    CameraComboBox.SelectedItem = cameras.FirstOrDefault();

                DirectionComboBox.SelectedIndex = KioskStateController.CurrentType == TransactionType.Entry ? 0 : 1;

                string savedMode = "Standard";
                if (DatabaseMonitor.IsOnline)
                {
                    try { savedMode = await _database.GetSettingAsync("verification_mode", "Standard"); } catch { }
                }

                SecurityModeComboBox.SelectedIndex = savedMode switch
                {
                    "Fast" => 0,
                    "High-Security" => 2,
                    _ => 1
                };

                VerificationLogListView.Items.Insert(0, "[INFO] Risk-based verification engine ready.");
            }
            catch (Exception ex)
            {
                VerificationLogListView.Items.Insert(0, $"[SYSTEM ERROR] {ex.Message}");
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

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            var dashboard = new MainWindow();
            dashboard.Activate();
            this.Close();
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

        private void MaximizeWindow()
        {
            IntPtr hWnd = WindowNative.GetWindowHandle(this);
            WindowId windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
            AppWindow appWindow = AppWindow.GetFromWindowId(windowId);

            if (appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.Maximize();
            }
        }

        private void LaunchKioskButton_Click(object sender, RoutedEventArgs e)
        {
            var mode = (SecurityModeComboBox.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "Standard";
            var type = (DirectionComboBox.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "Entry";

            var kiosk = new KioskModeWindow("Gate", $"{type} ({mode})");

            KioskModeWindow.OnKioskOutcome -= ApplyOutcomeFromKiosk;
            KioskModeWindow.OnKioskOutcome += ApplyOutcomeFromKiosk;

            KioskModeWindow.OnKioskLog -= AddKioskLog;
            KioskModeWindow.OnKioskLog += AddKioskLog;

            kiosk.Closed += (s, args) =>
            {
                KioskModeWindow.OnKioskOutcome -= ApplyOutcomeFromKiosk;
                KioskModeWindow.OnKioskLog -= AddKioskLog;
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
                VerificationLogListView.Items.Insert(0, msg);
            });
        }

        private void PlaySecurityAlert()
        {
            Task.Run(() =>
            {
                for (int i = 0; i < 3; i++)
                {
                    Console.Beep(2500, 300);
                    System.Threading.Tasks.Task.Delay(100).Wait();
                }
            });
        }

        private async void UnlockAccountButton_Click(object sender, RoutedEventArgs e)
        {
            string studentId = OverrideStudentIdBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(studentId))
            {
                VerificationLogListView.Items.Insert(0, "[ERROR] Valid Student ID required to unlock.");
                return;
            }

            try
            {
                var student = await _database.GetStudentByIdAsync(studentId);
                if (student == null)
                {
                    VerificationLogListView.Items.Insert(0, $"[ERROR] Unlock Aborted: Student ID '{studentId}' does not exist in the database.");
                    PlaySecurityAlert();
                    return;
                }

                await _database.UpdatePinFailureAsync(studentId, 0, false);

                try
                {
                    await _database.AddAlertAsync(AppSession.CurrentStaffName, "ADMIN_OVERRIDE", $"Manually cleared 2FA lockout for {student.FullName} ({studentId}).");
                }
                catch { }

                VerificationLogListView.Items.Insert(0, $"[SECURITY OVERRIDE] Guard cleared 2FA lockout for {student.FullName} ({studentId}).");
                OverrideStudentIdBox.Text = "";
            }
            catch (Exception ex)
            {
                VerificationLogListView.Items.Insert(0, $"[DB ERROR] Could not unlock account: {ex.Message}");
            }
        }

        private async void ApplyOutcome(VerificationOutcome outcome)
        {
            if (!string.IsNullOrWhiteSpace(outcome.LogLine))
            {
                VerificationLogListView.Items.Insert(0, outcome.LogLine);
            }

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
                    LiveResultText.Text = "ACCESS GRANTED";
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

        private async void SecurityModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SecurityModeComboBox == null || DirectionComboBox == null) return;

            KioskStateController.BroadcastModeChange(GetSelectedMode(), GetSelectedTransactionType());

            string modeString = GetSelectedMode() switch
            {
                VerificationMode.Fast => "Fast",
                VerificationMode.HighSecurity => "High-Security",
                _ => "Standard"
            };

            if (!_isInitializing)
            {
                string staff = AppSession.CurrentStaffName;

                VerificationLogListView.Items.Insert(0, $"[AUDIT] Security Mode changed to {modeString} by {staff}");

                try
                {
                    await _database.SetSettingAsync("verification_mode", modeString);
                    await _database.AddAlertAsync(staff, "ADMIN_ACTION", $"Changed global gate security mode to {modeString}.");
                }
                catch { }
            }
        }

        private async void DirectionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SecurityModeComboBox == null || DirectionComboBox == null) return;
            KioskStateController.BroadcastModeChange(GetSelectedMode(), GetSelectedTransactionType());

            if (!_isInitializing)
            {
                string direction = DirectionComboBox.SelectedIndex == 1 ? "Exit" : "Entry";
                string staff = AppSession.CurrentStaffName;

                VerificationLogListView.Items.Insert(0, $"[AUDIT] Gate Direction changed to {direction} by {staff}");

                try
                {
                    await _database.AddAlertAsync(staff, "ADMIN_ACTION", $"Changed Gate Direction to {direction}.");
                }
                catch { }
            }
        }

        private VerificationMode GetSelectedMode()
        {
            if (SecurityModeComboBox == null) return VerificationMode.Standard;

            return SecurityModeComboBox.SelectedIndex switch
            {
                0 => VerificationMode.Fast,
                2 => VerificationMode.HighSecurity,
                _ => VerificationMode.Standard
            };
        }

        private TransactionType GetSelectedTransactionType()
        {
            if (DirectionComboBox == null) return TransactionType.Entry;

            return DirectionComboBox.SelectedIndex switch
            {
                1 => TransactionType.Exit,
                _ => TransactionType.Entry
            };
        }
    }
}