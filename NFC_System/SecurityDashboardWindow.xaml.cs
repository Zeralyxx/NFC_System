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
using MySqlConnector;

namespace NFC_System
{
    public sealed partial class SecurityDashboardWindow : Window
    {
        private readonly DatabaseService _database = new();
        private List<SystemAuditLog> _masterLogsCache = new();
        private IReadOnlyList<StaffRecord> _allStaffCache = new List<StaffRecord>();
        private SerialPort? _serialPort;

        private bool _isAwaitingAdminAuth = false;
        private string _pendingStaffName = "";
        private string _pendingStaffUid = "";
        private string _pendingStaffRole = "";
        private string _pendingStaffPin = "";

        private string _pendingAdminAction = "";
        private string _pendingAdminSeverity = "";
        private string _pendingStaffAction = "";

        private StaffRecord? _editingStaff = null;
        private string _origStaffUid = "";
        private string _origStaffName = "";
        private string _origStaffRole = "";
        private bool _isAwaitingStaffNfcReplacementScan = false;
        private string _pendingStaffNfcReason = "";

        private string _pendingHealStudentId = "";
        private string _pendingHealState = "";

        private bool _isForceClosing = false;
        private readonly DispatcherTimer _searchDebounceTimer = new();

        // THE FIX: Asynchronous Dialog Queuing Engine
        // This mathematically prevents WinUI 3 from crashing due to overlapping ContentDialogs
        private bool _isDialogOpen = false;

        private async Task<ContentDialogResult> EnqueueDialogAsync(ContentDialog dialog)
        {
            // Pause execution safely in the background until the active dialog finishes closing
            while (_isDialogOpen)
            {
                await Task.Delay(50);
            }

            _isDialogOpen = true;
            try
            {
                if (this.Content?.XamlRoot != null)
                {
                    dialog.XamlRoot = this.Content.XamlRoot;
                }
                return await dialog.ShowAsync();
            }
            catch
            {
                return ContentDialogResult.None;
            }
            finally
            {
                _isDialogOpen = false;
                await Task.Delay(250); // Generous buffer to clear the pop-out visual animation
            }
        }

        public SecurityDashboardWindow()
        {
            this.InitializeComponent();
            DatabaseMonitor.ConnectionStatusChanged += UpdateOfflineBanner;
            UpdateOfflineBanner(DatabaseMonitor.IsOnline);
            MaximizeWindow();

            IntPtr hWnd = WindowNative.GetWindowHandle(this);
            WindowId windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
            AppWindow appWindow = AppWindow.GetFromWindowId(windowId);
            appWindow.Closing += AppWindow_Closing;

            this.Closed += Window_Closed;
            this.Activated += Window_Activated;

            _searchDebounceTimer.Interval = TimeSpan.FromMilliseconds(500);
            _searchDebounceTimer.Tick += SearchDebounceTimer_Tick;

            _ = InitializeAsync();
        }

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
                    CloseButtonText = "Cancel"
                };

                var result = await EnqueueDialogAsync(masterDialog);
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
                    CloseButtonText = "Cancel"
                };

                var result = await EnqueueDialogAsync(adminDialog);
                if (result == ContentDialogResult.Primary)
                {
                    _pendingAdminAction = "EXIT_SYNC";
                    _pendingAdminSeverity = "HIGH";

                    AdminPinBox.Visibility = Visibility.Visible;
                    AdminPinBox.Password = "";
                    AuthStatusText.Visibility = Visibility.Collapsed;
                    AdminAuthDescriptionText.Text = "To confirm this cloud upload, an Administrator must enter their 4-digit PIN and tap their NFC card.";
                    AdminAuthTapPromptText.Text = "Awaiting Administrator NFC tap & PIN...";

                    _isAwaitingAdminAuth = true;
                    var authResult = await EnqueueDialogAsync(AdminAuthDialog);

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
                    CloseButtonText = "Cancel"
                };

                restrictedDialog.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Orange);

                var result = await EnqueueDialogAsync(restrictedDialog);
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

            _isForceClosing = true;
            Application.Current.Exit();
        }

        private void Window_Activated(object sender, WindowActivatedEventArgs args)
        {
            if (args.WindowActivationState != WindowActivationState.Deactivated)
            {
                _ = RefreshDashboardAsync();
            }
        }

        private async Task InitializeAsync()
        {
            if (AppSession.CurrentStaffRoleLabel == "Personnel")
            {
                UploadDataButton.Visibility = Visibility.Collapsed;
                UploadDataColumn.Width = new GridLength(0);
                CourseManagementPanel.Visibility = Visibility.Collapsed;
                StaffManagementPanel.Visibility = Visibility.Collapsed;

                PopupStatusFilter.SelectedIndex = 2;
                PopupStatusFilter.IsEnabled = false;
            }

            try
            {
                await _database.EnsureSchemaAsync();
                await RefreshDashboardAsync();

                string nfcPort = await _database.GetSettingAsync("nfc_com_port", "COM3");
                TryConnectSerial(nfcPort);
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"Database setup failed: {ex.Message}";
                StatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 248, 113, 113));
                PlayErrorAlert();
            }
        }

        private void PlaySuccessPing()
        {
            Task.Run(() =>
            {
                try
                {
                    string soundPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "success_ping.wav");
                    if (File.Exists(soundPath))
                    {
                        using var player = new System.Media.SoundPlayer(soundPath);
                        player.PlaySync();
                    }
                    else
                    {
                        Console.Beep(1046, 75);
                        System.Threading.Thread.Sleep(15);
                        Console.Beep(1318, 75);
                        System.Threading.Thread.Sleep(15);
                        Console.Beep(1568, 200);
                    }
                }
                catch { }
            });
        }

        private void PlayErrorAlert()
        {
            Task.Run(() =>
            {
                try
                {
                    Console.Beep(2000, 300);
                    System.Threading.Thread.Sleep(100);
                    Console.Beep(2000, 300);
                }
                catch { }
            });
        }

        private void TryConnectSerial(string portName)
        {
            if (_serialPort != null && _serialPort.IsOpen) return;

            try
            {
                _serialPort = new SerialPort(portName, 115200);
                _serialPort.NewLine = "\n";
                _serialPort.DataReceived += SerialPort_DataReceived;
                _serialPort.Open();
                StatusTextBlock.Text = $"Ready. NFC connected on {portName}";
                StatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White);
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"NFC disconnected: {ex.Message}";
                StatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 248, 113, 113));
                PlayErrorAlert();
            }
        }

        private void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                if (_serialPort == null || !_serialPort.IsOpen) return;
                string line = _serialPort.ReadLine().Trim();
                if (!line.StartsWith("UID=")) return;

                string uid = line.Substring(4).Trim();

                DispatcherQueue.TryEnqueue(async () =>
                {
                    if (_isAwaitingStaffNfcReplacementScan)
                    {
                        await HandleStaffNfcReplacementScanAsync(uid);
                        return;
                    }

                    if (_isAwaitingAdminAuth)
                    {
                        var details = await _database.GetStaffDetailsAsync(uid);

                        bool isAuthorized = false;
                        string failReason = "";

                        if (_pendingAdminSeverity == "CRITICAL")
                        {
                            if (details.Role == "Master Administrator")
                            {
                                isAuthorized = true;
                            }
                            else
                            {
                                failReason = "Authorization Denied: This action strictly requires a Master Administrator.";
                            }
                        }
                        else if (_pendingAdminSeverity == "HIGH")
                        {
                            if (details.Role == "Administrator" || details.Role == "Master Administrator")
                            {
                                string enteredPin = AdminPinBox.Password.Trim();

                                if (string.IsNullOrEmpty(enteredPin))
                                {
                                    failReason = "Authorization Denied: A 4-digit Staff PIN is required.";
                                }
                                else if (string.IsNullOrEmpty(details.PinHash))
                                {
                                    failReason = "Authorization Denied: Tapped account does not have a PIN configured.";
                                }
                                else if (!PinHasher.VerifyPin(enteredPin, details.PinSalt, details.PinHash))
                                {
                                    failReason = "Authorization Denied: Invalid PIN.";
                                }
                                else
                                {
                                    isAuthorized = true;
                                }
                            }
                            else
                            {
                                failReason = "Authorization Denied: Tapped card is not an Administrator.";
                            }
                        }
                        else if (_pendingAdminSeverity == "MODERATE")
                        {
                            if (details.Role == "Security Personnel" || details.Role == "Administrator" || details.Role == "Master Administrator")
                            {
                                string enteredPin = AdminPinBox.Password.Trim();

                                if (string.IsNullOrEmpty(enteredPin))
                                {
                                    failReason = "Authorization Denied: A 4-digit Staff PIN is required.";
                                }
                                else if (string.IsNullOrEmpty(details.PinHash))
                                {
                                    failReason = "Authorization Denied: Tapped account does not have a PIN configured.";
                                }
                                else if (!PinHasher.VerifyPin(enteredPin, details.PinSalt, details.PinHash))
                                {
                                    failReason = "Authorization Denied: Invalid PIN.";
                                }
                                else
                                {
                                    isAuthorized = true;
                                }
                            }
                            else
                            {
                                failReason = "Authorization Denied: Tapped card must belong to a valid staff member.";
                            }
                        }

                        if (isAuthorized)
                        {
                            _isAwaitingAdminAuth = false;
                            AdminAuthDialog.Hide();

                            PlaySuccessPing();

                            string authorizedByName = details.FullName ?? "Staff";
                            string actionToRun = _pendingAdminAction;
                            _pendingAdminAction = "";

                            switch (actionToRun)
                            {
                                case "REGISTER_STAFF":
                                    await ExecuteStaffRegistration(_pendingStaffUid, _pendingStaffName, _pendingStaffRole, authorizedByName, _pendingStaffPin);
                                    break;
                                case "UPLOAD_DATA":
                                    await ExecuteUploadDataAsync(authorizedByName);
                                    break;
                                case "DOWNLOAD_DATA":
                                    await ExecuteDownloadDataAsync(authorizedByName);
                                    break;
                                case "EXIT_SYNC":
                                    await PerformCloudPushAndExit();
                                    break;
                                case "HEAL_STATE":
                                    await ExecuteHealStateAsync(authorizedByName);
                                    break;
                            }
                        }
                        else
                        {
                            AuthStatusText.Text = failReason;
                            AuthStatusText.Visibility = Visibility.Visible;
                            PlayErrorAlert();
                        }
                    }
                    else
                    {
                        StaffNfcUidTextBox.Text = uid;
                        StatusTextBlock.Text = "Card scanned. Ready to register staff.";
                        StatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White);
                        PlaySuccessPing();
                    }
                });
            }
            catch { }
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

        private void UpdateOfflineBanner(bool isOnline)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (GlobalOfflineBanner != null)
                {
                    GlobalOfflineBanner.Visibility = isOnline ? Visibility.Collapsed : Visibility.Visible;
                }

                if (UploadDataButton != null) UploadDataButton.IsEnabled = isOnline;
                if (GetNewDataButton != null) GetNewDataButton.IsEnabled = isOnline;
            });
        }

        private async Task RefreshDashboardAsync()
        {
            try
            {
                string? statusFilter = AppSession.CurrentStaffRoleLabel == "Personnel" ? "Denied / Flagged" : null;

                var recentLogs = await _database.GetMasterAuditLogsAsync(30, null, null, statusFilter, null);
                RecentActivityListView.ItemsSource = recentLogs;
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"Could not refresh dashboard: {ex.Message}";
                StatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 248, 113, 113));
                PlayErrorAlert();
            }
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            _ = RefreshDashboardAsync();
        }

        // ====================================================================
        // MANUAL STATE CORRECTION (ANTI-TAILGATING HEALER) LOGIC
        // ====================================================================
        private async void HealerApplyButton_Click(object sender, RoutedEventArgs e)
        {
            string studentId = HealerStudentIdBox.Text.Trim();
            string state = (HealerStateComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";

            if (string.IsNullOrWhiteSpace(studentId) || string.IsNullOrWhiteSpace(state))
            {
                StatusTextBlock.Text = "Please provide a valid Student ID and select a physical State.";
                StatusTextBlock.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 248, 113, 113));
                PlayErrorAlert();
                return;
            }

            if (AppSession.CurrentStaffRoleLabel == "Master Admin")
            {
                _pendingHealStudentId = studentId;
                _pendingHealState = state;
                await ExecuteHealStateAsync(AppSession.CurrentStaffName);
            }
            else
            {
                _pendingHealStudentId = studentId;
                _pendingHealState = state;

                _pendingAdminAction = "HEAL_STATE";
                _pendingAdminSeverity = "MODERATE";

                AdminPinBox.Visibility = Visibility.Visible;
                AdminPinBox.Password = "";
                AuthStatusText.Visibility = Visibility.Collapsed;

                AdminAuthDescriptionText.Text = "To manually override a student's state, please verify your identity.";
                AdminAuthTapPromptText.Text = "Please tap your NFC identification card & enter PIN...";

                _isAwaitingAdminAuth = true;
                var result = await EnqueueDialogAsync(AdminAuthDialog);

                if (result == ContentDialogResult.None && _isAwaitingAdminAuth)
                {
                    _isAwaitingAdminAuth = false;
                    _pendingAdminAction = "";
                    StatusTextBlock.Text = "State override cancelled.";
                    StatusTextBlock.Foreground = new SolidColorBrush(Microsoft.UI.Colors.White);
                }
            }
        }

        private async Task ExecuteHealStateAsync(string authorizedByName)
        {
            try
            {
                var student = await _database.GetStudentByIdAsync(_pendingHealStudentId);
                if (student == null)
                {
                    StatusTextBlock.Text = $"Override failed: Student ID '{_pendingHealStudentId}' not found.";
                    StatusTextBlock.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 248, 113, 113));
                    PlayErrorAlert();
                    return;
                }

                await _database.UpdateEntryStateAsync(_pendingHealStudentId, _pendingHealState);

                string logMessage = $"Manually overridden physical state for {student.FullName} ({student.StudentId}) to {_pendingHealState}.";
                await _database.AddAlertAsync(authorizedByName, "ADMIN_OVERRIDE", logMessage);

                HealerStudentIdBox.Text = "";
                HealerStateComboBox.SelectedIndex = -1;

                StatusTextBlock.Text = $"Successfully updated {student.FullName} to {_pendingHealState}.";
                StatusTextBlock.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 52, 211, 153));
                PlaySuccessPing();

                await RefreshDashboardAsync();
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"State correction failed: {ex.Message}";
                StatusTextBlock.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 248, 113, 113));
                PlayErrorAlert();
            }
        }

        private async void OpenPopupLogsButton_Click(object sender, RoutedEventArgs e)
        {
            DialogLogContainer.Width = 900;
            PopupExpandToggle.IsChecked = false;
            PopupExpandToggle.Content = "⛶ Expand View";

            await ApplyServerSidePopupFiltersAsync();
            await EnqueueDialogAsync(MasterLogsDialog);
        }

        private void PopupExpandToggle_Click(object sender, RoutedEventArgs e)
        {
            if (PopupExpandToggle.IsChecked == true)
            {
                DialogLogContainer.Width = 1400;
                PopupExpandToggle.Content = "⮌ Collapse View";
            }
            else
            {
                DialogLogContainer.Width = 900;
                PopupExpandToggle.Content = "⛶ Expand View";
            }
        }

        private void PopupFilter_Changed(object sender, RoutedEventArgs e)
        {
            if (PopupLogsListView == null) return;
            _searchDebounceTimer.Stop();
            _searchDebounceTimer.Start();
        }

        private void PopupDatePicker_DateChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args)
        {
            if (PopupLogsListView == null) return;
            _searchDebounceTimer.Stop();
            _searchDebounceTimer.Start();
        }

        private async void SearchDebounceTimer_Tick(object? sender, object e)
        {
            _searchDebounceTimer.Stop();
            await ApplyServerSidePopupFiltersAsync();
        }

        private async void PopupClear_Click(object sender, RoutedEventArgs e)
        {
            PopupSearchBox.Text = "";
            PopupTypeFilter.SelectedIndex = 0;
            PopupDatePicker.Date = null;

            if (AppSession.CurrentStaffRoleLabel != "Personnel")
            {
                PopupStatusFilter.SelectedIndex = 0;
            }

            _searchDebounceTimer.Stop();
            await ApplyServerSidePopupFiltersAsync();
        }

        private async Task ApplyServerSidePopupFiltersAsync()
        {
            string searchTerm = PopupSearchBox.Text?.Trim() ?? "";
            string type = (PopupTypeFilter.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All Types";
            string status = (PopupStatusFilter.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All Statuses";

            if (AppSession.CurrentStaffRoleLabel == "Personnel")
            {
                status = "Denied / Flagged";
            }

            DateTime? searchDate = null;
            if (PopupDatePicker.Date.HasValue)
            {
                searchDate = PopupDatePicker.Date.Value.DateTime;
            }

            try
            {
                var searchResults = await _database.GetMasterAuditLogsAsync(2000, searchTerm, type, status, searchDate);
                PopupLogsListView.ItemsSource = searchResults;
            }
            catch { }
        }

        private async void RegisterStaffButton_Click(object sender, RoutedEventArgs e)
        {
            string fullName = StaffNameTextBox.Text.Trim();
            string uid = StaffNfcUidTextBox.Text.Trim();
            string role = (StaffRoleComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Security Personnel";
            string rawPin = StaffPinBox.Password.Trim();

            if (string.IsNullOrWhiteSpace(fullName) || string.IsNullOrWhiteSpace(uid))
            {
                StatusTextBlock.Text = "Staff Name and NFC UID are strictly required.";
                StatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 248, 113, 113));
                PlayErrorAlert();
                return;
            }

            if (!string.IsNullOrWhiteSpace(rawPin) && (rawPin.Length != 4 || !rawPin.All(char.IsDigit)))
            {
                StatusTextBlock.Text = "If provided, the Staff PIN must be exactly 4 numeric digits.";
                StatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 248, 113, 113));
                PlayErrorAlert();
                return;
            }

            if ((role == "Administrator" || role == "Master Administrator") && string.IsNullOrWhiteSpace(rawPin))
            {
                StatusTextBlock.Text = "Administrators must have a 4-digit PIN assigned for High-Severity actions.";
                StatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 248, 113, 113));
                PlayErrorAlert();
                return;
            }

            var existingStaff = await _database.GetStaffDetailsAsync(uid);
            if (existingStaff.Role != null)
            {
                ContentDialog overwriteDialog = new ContentDialog
                {
                    Title = "NFC Card Already in Use",
                    Content = $"This NFC card is currently registered to:\n\nName: {existingStaff.FullName}\nRole: {existingStaff.Role}\n\nDo you want to overwrite this assignment and register the card to {fullName}?",
                    PrimaryButtonText = "Yes, Overwrite",
                    CloseButtonText = "Cancel",
                    DefaultButton = ContentDialogButton.Close
                };

                PlayErrorAlert();
                var dialogResult = await EnqueueDialogAsync(overwriteDialog);

                if (dialogResult != ContentDialogResult.Primary)
                {
                    StatusTextBlock.Text = "Staff registration cancelled.";
                    StatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White);
                    return;
                }
            }

            if (AppSession.CurrentStaffRoleLabel == "Master Admin")
            {
                await ExecuteStaffRegistration(uid, fullName, role, AppSession.CurrentStaffName, rawPin);
            }
            else
            {
                _pendingStaffName = fullName;
                _pendingStaffUid = uid;
                _pendingStaffRole = role;
                _pendingStaffPin = rawPin;

                _pendingAdminAction = "REGISTER_STAFF";
                _pendingAdminSeverity = "CRITICAL";

                AdminPinBox.Visibility = Visibility.Collapsed;
                AdminPinBox.Password = "";
                AuthStatusText.Visibility = Visibility.Collapsed;
                AdminAuthDescriptionText.Text = "To prevent unauthorized account creation, a Master Administrator must verify this action.";
                AdminAuthTapPromptText.Text = "Awaiting Master Administrator NFC identification card...";

                _isAwaitingAdminAuth = true;
                var result = await EnqueueDialogAsync(AdminAuthDialog);

                if (result == ContentDialogResult.None && _isAwaitingAdminAuth)
                {
                    _isAwaitingAdminAuth = false;
                    _pendingAdminAction = "";
                    StatusTextBlock.Text = "Registration cancelled.";
                    StatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White);
                }
            }
        }

        private async Task ExecuteStaffRegistration(string uid, string fullName, string role, string authorizedBy, string? rawPin)
        {
            try
            {
                await _database.RegisterStaffAsync(uid, fullName, role, rawPin);
                await _database.AddAlertAsync(authorizedBy, "ADMIN_OVERRIDE", $"Authorized registration of new {role}: {fullName}");

                StaffNameTextBox.Text = "";
                StaffNfcUidTextBox.Text = "";
                StaffPinBox.Password = "";
                StaffRoleComboBox.SelectedIndex = 0;

                StatusTextBlock.Text = $"Successfully registered {role}: {fullName}";
                StatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 52, 211, 153));

                await RefreshDashboardAsync();
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"Registration failed: {ex.Message}";
                StatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 248, 113, 113));
                PlayErrorAlert();
            }
        }

        private async void OpenStaffDirectoryButton_Click(object sender, RoutedEventArgs e)
        {
            StaffDirectoryFilter.SelectedIndex = 0;
            await ReopenStaffDirectory();
        }

        private void StaffDirectoryFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplyStaffFilter();
        }

        private void ApplyStaffFilter()
        {
            if (StaffListView == null || StaffDirectoryFilter == null) return;
            string selectedRole = (StaffDirectoryFilter.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All Roles";

            var sortedStaff = _allStaffCache
                .OrderBy(s => s.Role)
                .ThenBy(s => s.FullName)
                .ToList();

            if (selectedRole == "All Roles")
            {
                StaffListView.ItemsSource = sortedStaff;
            }
            else
            {
                StaffListView.ItemsSource = sortedStaff.Where(s => s.Role == selectedRole).ToList();
            }
        }

        private async Task ReopenStaffDirectory()
        {
            try
            {
                _allStaffCache = (await _database.GetAllStaffAsync()).ToList();
                ApplyStaffFilter();

                await EnqueueDialogAsync(StaffDirectoryDialog);
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"Could not load staff directory: {ex.Message}";
                StatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 248, 113, 113));
                PlayErrorAlert();
            }
        }

        private async Task ShowSyncResultDialog(string title, string message)
        {
            ContentDialog resultDialog = new ContentDialog
            {
                Title = title,
                Content = message,
                CloseButtonText = "OK"
            };
            await EnqueueDialogAsync(resultDialog);
        }

        private async void UploadDataButton_Click(object sender, RoutedEventArgs e)
        {
            ContentDialog confirmDialog = new ContentDialog
            {
                Title = "Confirm Upload",
                Content = "This will push new local students, staff, courses, events, and logs to the cloud database. Continue?",
                PrimaryButtonText = "Yes, Upload",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close
            };

            var confirmResult = await EnqueueDialogAsync(confirmDialog);
            if (confirmResult != ContentDialogResult.Primary) return;

            if (AppSession.CurrentStaffRoleLabel == "Master Admin")
            {
                await ExecuteUploadDataAsync(AppSession.CurrentStaffName);
            }
            else
            {
                _pendingAdminAction = "UPLOAD_DATA";
                _pendingAdminSeverity = "HIGH";

                AdminPinBox.Visibility = Visibility.Visible;
                AdminPinBox.Password = "";
                AuthStatusText.Visibility = Visibility.Collapsed;
                AdminAuthDescriptionText.Text = "To confirm this data upload, an Administrator must enter their 4-digit PIN and tap their NFC card.";
                AdminAuthTapPromptText.Text = "Awaiting Administrator NFC identification card & PIN...";

                _isAwaitingAdminAuth = true;
                var authResult = await EnqueueDialogAsync(AdminAuthDialog);

                if (authResult == ContentDialogResult.None && _isAwaitingAdminAuth)
                {
                    _isAwaitingAdminAuth = false;
                    _pendingAdminAction = "";
                    StatusTextBlock.Text = "Upload cancelled.";
                    StatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White);
                }
            }
        }

        private async Task ExecuteUploadDataAsync(string authorizedBy)
        {
            UploadDataButton.IsEnabled = false;
            GetNewDataButton.IsEnabled = false;
            SyncProgressBar.Visibility = Visibility.Visible;
            StatusTextBlock.Text = "Uploading local records to cloud database...";
            StatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White);

            try
            {
                int pushedStudents = await _database.PushStudentsToCloudAsync();
                int pushedStaff = await _database.PushStaffToCloudAsync();
                int pushedCourses = await _database.PushCoursesToCloudAsync();
                int pushedEvents = await _database.PushEventsToCloudAsync();
                int pushedApproved = await _database.PushEventApprovedStudentsToCloudAsync();
                int pushedLogs = await _database.PushLogsToCloudAsync();
                int pushedEventLogs = await _database.PushEventAttendanceToCloudAsync();

                int totalPushed = pushedStudents + pushedStaff + pushedCourses + pushedEvents + pushedApproved + pushedLogs + pushedEventLogs;

                if (totalPushed > 0)
                {
                    var additions = new List<string>();
                    if (pushedStudents > 0) additions.Add($"{pushedStudents} Student(s)");
                    if (pushedStaff > 0) additions.Add($"{pushedStaff} Staff member(s)");
                    if (pushedCourses > 0) additions.Add($"{pushedCourses} Course(s)");
                    if (pushedEvents > 0) additions.Add($"{pushedEvents} Event(s)");
                    if (pushedApproved > 0) additions.Add($"{pushedApproved} Roster Entry(ies)");
                    if (pushedLogs > 0) additions.Add($"{pushedLogs} Gate Log(s)");
                    if (pushedEventLogs > 0) additions.Add($"{pushedEventLogs} Event Attendance Log(s)");

                    string formattedList = "• " + string.Join("\n• ", additions);
                    string message = $"Upload complete. The following new records were synced to the cloud:\n\n{formattedList}";

                    StatusTextBlock.Text = $"Upload complete. {totalPushed} records pushed.";
                    StatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 52, 211, 153));
                    PlaySuccessPing();

                    try
                    {
                        await _database.AddAlertAsync(authorizedBy, "ADMIN_ACTION", $"Authorized cloud data upload. {totalPushed} record(s) pushed.");
                    }
                    catch { }

                    await ShowSyncResultDialog("Upload Successful", message);
                }
                else
                {
                    StatusTextBlock.Text = "System is already up to date. No new local records to upload.";
                    StatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White);
                }
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"Upload failed: {ex.Message}";
                StatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 248, 113, 113));
                PlayErrorAlert();

                await ShowSyncResultDialog("Upload Failed", $"An error occurred while syncing to the cloud:\n\n{ex.Message}");
            }
            finally
            {
                UploadDataButton.IsEnabled = true;
                GetNewDataButton.IsEnabled = true;
                SyncProgressBar.Visibility = Visibility.Collapsed;
            }
        }

        private async void GetNewDataButton_Click(object sender, RoutedEventArgs e)
        {
            ContentDialog confirmDialog = new ContentDialog
            {
                Title = "Confirm Download",
                Content = "This will pull the latest students, staff, courses, events, and rosters from the cloud database into this local terminal. Continue?",
                PrimaryButtonText = "Yes, Download",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close
            };

            var confirmResult = await EnqueueDialogAsync(confirmDialog);
            if (confirmResult != ContentDialogResult.Primary) return;

            if (AppSession.CurrentStaffRoleLabel == "Master Admin")
            {
                await ExecuteDownloadDataAsync(AppSession.CurrentStaffName);
            }
            else
            {
                _pendingAdminAction = "DOWNLOAD_DATA";
                _pendingAdminSeverity = "HIGH";

                AdminPinBox.Visibility = Visibility.Visible;
                AdminPinBox.Password = "";
                AuthStatusText.Visibility = Visibility.Collapsed;
                AdminAuthDescriptionText.Text = "To confirm this data download, an Administrator must enter their 4-digit PIN and tap their NFC card.";
                AdminAuthTapPromptText.Text = "Awaiting Administrator NFC identification card & PIN...";

                _isAwaitingAdminAuth = true;
                var authResult = await EnqueueDialogAsync(AdminAuthDialog);

                if (authResult == ContentDialogResult.None && _isAwaitingAdminAuth)
                {
                    _isAwaitingAdminAuth = false;
                    _pendingAdminAction = "";
                    StatusTextBlock.Text = "Download cancelled.";
                    StatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White);
                }
            }
        }

        private async Task ExecuteDownloadDataAsync(string authorizedBy)
        {
            GetNewDataButton.IsEnabled = false;
            UploadDataButton.IsEnabled = false;
            SyncProgressBar.Visibility = Visibility.Visible;
            StatusTextBlock.Text = "Downloading latest records and logs from cloud database...";
            StatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White);

            try
            {
                int pulledStudents = await _database.PullStudentsFromCloudAsync();
                int pulledStaff = await _database.PullStaffFromCloudAsync();
                int pulledCourses = await _database.PullCoursesFromCloudAsync();
                int pulledEvents = await _database.PullEventsFromCloudAsync();
                int pulledApproved = await _database.PullEventApprovedStudentsFromCloudAsync();
                int pulledLogs = await _database.PullLogsFromCloudAsync();
                int pulledEventLogs = await _database.PullEventAttendanceFromCloudAsync();

                int totalPulled = pulledStudents + pulledStaff + pulledCourses + pulledEvents + pulledApproved + pulledLogs + pulledEventLogs;

                if (totalPulled > 0)
                {
                    var additions = new List<string>();
                    if (pulledStudents > 0) additions.Add($"{pulledStudents} Student(s)");
                    if (pulledStaff > 0) additions.Add($"{pulledStaff} Staff member(s)");
                    if (pulledCourses > 0) additions.Add($"{pulledCourses} Course(s)");
                    if (pulledEvents > 0) additions.Add($"{pulledEvents} Event(s)");
                    if (pulledApproved > 0) additions.Add($"{pulledApproved} Roster Entry(ies)");
                    if (pulledLogs > 0) additions.Add($"{pulledLogs} Gate Log(s)");
                    if (pulledEventLogs > 0) additions.Add($"{pulledEventLogs} Event Log(s)");

                    string formattedList = "• " + string.Join("\n• ", additions);
                    string message = $"Download complete. The following new updates were synced locally:\n\n{formattedList}";

                    StatusTextBlock.Text = $"Download complete. {totalPulled} records pulled.";
                    StatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 52, 211, 153));
                    PlaySuccessPing();
                    await RefreshDashboardAsync();

                    try
                    {
                        await _database.AddAlertAsync(authorizedBy, "ADMIN_ACTION", $"Authorized cloud data download. {totalPulled} record(s) pulled.");
                    }
                    catch { }

                    await ShowSyncResultDialog("Download Successful", message);
                }
                else
                {
                    StatusTextBlock.Text = "System is already up to date. No new cloud records found.";
                    StatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White);
                }
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"Download failed: {ex.Message}";
                StatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 248, 113, 113));
                PlayErrorAlert();

                await ShowSyncResultDialog("Download Failed", $"An error occurred while pulling from the cloud:\n\n{ex.Message}");
            }
            finally
            {
                GetNewDataButton.IsEnabled = true;
                UploadDataButton.IsEnabled = true;
                SyncProgressBar.Visibility = Visibility.Collapsed;
            }
        }

        private async void OpenManageCoursesDialog_Click(object sender, RoutedEventArgs e)
        {
            CourseDialogStatusText.Text = "Select an action below.";
            CourseDialogStatusText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 160, 160, 160));
            DialogNewCourseTextBox.Text = "";

            await LoadCoursesIntoDialogAsync();
            await EnqueueDialogAsync(ManageCoursesDialog);
        }

        private async Task LoadCoursesIntoDialogAsync()
        {
            try
            {
                var courses = await _database.GetDistinctCoursesAsync();
                DialogDeleteCourseComboBox.ItemsSource = courses;
                DialogDeleteCourseComboBox.SelectedIndex = -1;
            }
            catch { }
        }

        private async void DialogAddCourseButton_Click(object sender, RoutedEventArgs e)
        {
            string courseName = DialogNewCourseTextBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(courseName))
            {
                CourseDialogStatusText.Text = "Please enter a valid course name.";
                CourseDialogStatusText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 248, 113, 113));
                PlayErrorAlert();
                return;
            }

            try
            {
                await _database.AddCourseAsync(courseName);

                CourseDialogStatusText.Text = $"Course '{courseName}' added successfully.";
                CourseDialogStatusText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 52, 211, 153));
                PlaySuccessPing();
                DialogNewCourseTextBox.Text = "";

                await LoadCoursesIntoDialogAsync();
                await RefreshDashboardAsync();
            }
            catch (Exception ex)
            {
                CourseDialogStatusText.Text = $"Could not add course: {ex.Message}";
                CourseDialogStatusText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 248, 113, 113));
                PlayErrorAlert();
            }
        }

        private async void DialogDeleteCourseButton_Click(object sender, RoutedEventArgs e)
        {
            string courseName = (DialogDeleteCourseComboBox.SelectedItem as string) ?? "";

            if (string.IsNullOrWhiteSpace(courseName))
            {
                CourseDialogStatusText.Text = "Please select a course to delete.";
                CourseDialogStatusText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 248, 113, 113));
                PlayErrorAlert();
                return;
            }

            ManageCoursesDialog.Hide();

            try
            {
                int enrolledStudents = await _database.GetStudentCountByCourseAsync(courseName);

                if (enrolledStudents > 0)
                {
                    ContentDialog warningDialog = new ContentDialog
                    {
                        Title = "Action Blocked: Course in Use",
                        Content = $"You cannot delete '{courseName}' because there are currently {enrolledStudents} student(s) enrolled in it.\n\nPlease reassign these students to a different course before deleting.",
                        CloseButtonText = "Understood"
                    };
                    PlayErrorAlert();
                    await EnqueueDialogAsync(warningDialog);

                    await EnqueueDialogAsync(ManageCoursesDialog);
                    return;
                }

                ContentDialog confirmDialog = new ContentDialog
                {
                    Title = "Confirm Deletion",
                    Content = $"Are you absolutely sure you want to delete '{courseName}'? This action cannot be undone.",
                    PrimaryButtonText = "Delete Course",
                    CloseButtonText = "Cancel",
                    DefaultButton = ContentDialogButton.Close
                };

                var result = await EnqueueDialogAsync(confirmDialog);

                if (result == ContentDialogResult.Primary)
                {
                    await _database.DeleteCourseAsync(courseName);

                    CourseDialogStatusText.Text = $"Course '{courseName}' was successfully deleted.";
                    CourseDialogStatusText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 52, 211, 153));
                    PlaySuccessPing();

                    await LoadCoursesIntoDialogAsync();
                    await RefreshDashboardAsync();
                }

                await EnqueueDialogAsync(ManageCoursesDialog);
            }
            catch (Exception ex)
            {
                CourseDialogStatusText.Text = $"Could not delete course: {ex.Message}";
                CourseDialogStatusText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 248, 113, 113));
                PlayErrorAlert();

                await EnqueueDialogAsync(ManageCoursesDialog);
            }
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            CloseSerialPort();
            var dashboard = new MainWindow();
            dashboard.Activate();
            this.Close();
        }

        private async void ExportLogsButton_Click(object sender, RoutedEventArgs e)
        {
            StatusTextBlock.Text = "Gathering audit records for export...";
            StatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White);

            string? statusFilter = AppSession.CurrentStaffRoleLabel == "Personnel" ? "Denied / Flagged" : null;
            var rawLogs = await _database.GetMasterAuditLogsAsync(10000, null, null, statusFilter, null);

            if (rawLogs == null || !rawLogs.Any())
            {
                ContentDialog emptyDialog = new ContentDialog
                {
                    Title = "Nothing to Export",
                    Content = "There are no audit records in the database to export.",
                    CloseButtonText = "OK"
                };
                await EnqueueDialogAsync(emptyDialog);
                StatusTextBlock.Text = "System Ready: Waiting for input...";
                return;
            }

            var dialogResult = await EnqueueDialogAsync(ExportConfigDialog);

            if (dialogResult != ContentDialogResult.Primary)
            {
                StatusTextBlock.Text = "Export cancelled.";
                return;
            }

            var logsToExport = rawLogs.ToList();
            string sortOption = (ExportSortComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";

            if (sortOption == "Group by Log Type")
            {
                logsToExport = logsToExport
                    .OrderBy(l => l.LogType)
                    .ThenByDescending(l => l.Timestamp)
                    .ToList();
            }
            else if (sortOption == "Sort alphabetically by Subject")
            {
                logsToExport = logsToExport
                    .OrderBy(l => l.Subject)
                    .ToList();
            }

            var picker = new Windows.Storage.Pickers.FileSavePicker();
            IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
            picker.FileTypeChoices.Add("Excel CSV Document", new List<string>() { ".csv" });
            picker.SuggestedFileName = $"Security_Audit_Log_{DateTime.Now:yyyyMMdd}";

            Windows.Storage.StorageFile file = await picker.PickSaveFileAsync();

            if (file != null)
            {
                StatusTextBlock.Text = "Writing custom audit log to CSV...";

                try
                {
                    var csvData = new System.Text.StringBuilder();

                    var headers = new List<string>();
                    if (ExportColTimestamp.IsChecked == true) headers.Add("Date & Time");
                    if (ExportColLogType.IsChecked == true) headers.Add("Log Type");
                    if (ExportColSubject.IsChecked == true) headers.Add("Subject");
                    if (ExportColAction.IsChecked == true) headers.Add("Action Taken");
                    if (ExportColStatus.IsChecked == true) headers.Add("Status");
                    if (ExportColDetails.IsChecked == true) headers.Add("Extended Details");

                    csvData.AppendLine(string.Join(",", headers));

                    foreach (var log in logsToExport)
                    {
                        var row = new List<string>();

                        if (ExportColTimestamp.IsChecked == true) row.Add($"\"{log.DisplayTime}\"");
                        if (ExportColLogType.IsChecked == true) row.Add($"\"{log.LogType}\"");
                        if (ExportColSubject.IsChecked == true) row.Add($"\"{log.Subject}\"");
                        if (ExportColAction.IsChecked == true) row.Add($"\"{log.Action}\"");
                        if (ExportColStatus.IsChecked == true) row.Add($"\"{log.Status}\"");
                        if (ExportColDetails.IsChecked == true) row.Add($"\"{log.Details.Replace("\"", "\"\"")}\"");

                        csvData.AppendLine(string.Join(",", row));
                    }

                    Windows.Storage.CachedFileManager.DeferUpdates(file);
                    await Windows.Storage.FileIO.WriteTextAsync(file, csvData.ToString(), Windows.Storage.Streams.UnicodeEncoding.Utf8);
                    Windows.Storage.Provider.FileUpdateStatus status = await Windows.Storage.CachedFileManager.CompleteUpdatesAsync(file);

                    if (status == Windows.Storage.Provider.FileUpdateStatus.Complete)
                    {
                        StatusTextBlock.Text = $"Audit log successfully exported.";
                        StatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 52, 211, 153));
                        PlaySuccessPing();

                        ContentDialog successDialog = new ContentDialog
                        {
                            Title = "Export Complete",
                            Content = $"Your custom security audit was successfully exported and saved to:\n\n{file.Path}",
                            CloseButtonText = "OK"
                        };
                        await EnqueueDialogAsync(successDialog);
                    }
                }
                catch (Exception ex)
                {
                    StatusTextBlock.Text = $"Export failed: {ex.Message}";
                    StatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 248, 113, 113));
                    PlayErrorAlert();
                }
            }
            else
            {
                StatusTextBlock.Text = "Export cancelled.";
                StatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White);
            }
        }

        private async void ExportMetricsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var folderPicker = new Windows.Storage.Pickers.FolderPicker();
                folderPicker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Desktop;
                folderPicker.FileTypeFilter.Add("*");

                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                WinRT.Interop.InitializeWithWindow.Initialize(folderPicker, hwnd);

                var folder = await folderPicker.PickSingleFolderAsync();
                if (folder != null)
                {
                    StatusTextBlock.Text = "Exporting system performance metrics to CSV...";
                    StatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White);

                    await _database.ExportCleanLogsToCsvAsync(folder.Path);

                    StatusTextBlock.Text = $"Performance metrics successfully exported to {folder.Path}";
                    StatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 52, 211, 153));
                    PlaySuccessPing();
                }
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"Metrics export failed: {ex.Message}";
                StatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 248, 113, 113));
                PlayErrorAlert();
            }
        }

        private void MaximizeWindow()
        {
            IntPtr hWnd = WindowNative.GetWindowHandle(this);
            WindowId windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
            AppWindow appWindow = AppWindow.GetFromWindowId(windowId);
            if (appWindow.Presenter is OverlappedPresenter presenter) presenter.Maximize();
        }

        private async void StaffListView_DoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
        {
            if (e.OriginalSource is FrameworkElement fe && fe.DataContext is StaffRecord staff)
            {
                StaffDirectoryDialog.Hide();

                _editingStaff = staff;
                _origStaffUid = staff.NfcUid;
                _origStaffName = staff.FullName;
                _origStaffRole = staff.Role;

                EditStaffNameBox.Text = staff.FullName;
                EditStaffNfcUidBox.Text = staff.NfcUid;
                EditStaffPinBox.Password = "";
                EditStaffNfcReasonBox.Text = "";
                EditStaffNfcReasonBox.Visibility = Visibility.Collapsed;
                EditStaffNfcScanStatusText.Visibility = Visibility.Collapsed;
                EditStaffChangeNfcButton.IsEnabled = true;
                EditStaffStatusText.Visibility = Visibility.Collapsed;

                EditStaffDeleteButton.Visibility = AppSession.CurrentStaffRoleLabel == "Master Admin" ? Visibility.Visible : Visibility.Collapsed;

                foreach (ComboBoxItem item in EditStaffRoleComboBox.Items)
                {
                    if (item.Content.ToString() == staff.Role)
                    {
                        EditStaffRoleComboBox.SelectedItem = item;
                        break;
                    }
                }

                _isAwaitingStaffNfcReplacementScan = false;
                EditStaffDialog.IsPrimaryButtonEnabled = false;
                _pendingStaffAction = "";

                var result = await EnqueueDialogAsync(EditStaffDialog);

                if (_pendingStaffAction == "DELETE")
                {
                    ContentDialog confirmDialog = new ContentDialog
                    {
                        Title = "Confirm Staff Deletion",
                        Content = $"Are you sure you want to permanently delete {_origStaffName} ({_origStaffRole}) from the system?",
                        PrimaryButtonText = "Delete Profile",
                        CloseButtonText = "Cancel",
                        DefaultButton = ContentDialogButton.Close
                    };

                    var confirmResult = await EnqueueDialogAsync(confirmDialog);

                    if (confirmResult == ContentDialogResult.Primary)
                    {
                        await ExecuteStaffDeletionAsync(AppSession.CurrentStaffName);
                    }
                    else
                    {
                        await ReopenStaffDirectory();
                    }
                }
                else if (_pendingStaffAction == "EDIT_STAFF_EXECUTE")
                {
                    await ExecuteStaffEditAsync(AppSession.CurrentStaffName);
                }
                else if (_pendingStaffAction == "EDIT_STAFF_AUTH")
                {
                    AdminPinBox.Visibility = Visibility.Collapsed;
                    AdminPinBox.Password = "";
                    AuthStatusText.Visibility = Visibility.Collapsed;
                    AdminAuthDescriptionText.Text = "To modify staff credentials, a Master Administrator must verify this action.";
                    AdminAuthTapPromptText.Text = "Awaiting Master Administrator NFC identification card...";

                    _pendingAdminAction = "EDIT_STAFF";
                    _pendingAdminSeverity = "CRITICAL";
                    _isAwaitingAdminAuth = true;

                    var authResult = await EnqueueDialogAsync(AdminAuthDialog);

                    if (authResult == ContentDialogResult.None && _isAwaitingAdminAuth)
                    {
                        _isAwaitingAdminAuth = false;
                        _pendingAdminAction = "";
                        await ReopenStaffDirectory();
                    }
                }
                else
                {
                    await ReopenStaffDirectory();
                }
            }
        }

        private void EditStaffDialog_FieldChanged(object sender, object e)
        {
            if (_editingStaff == null) return;

            string name = EditStaffNameBox.Text.Trim();
            string uid = EditStaffNfcUidBox.Text.Trim();
            string role = (EditStaffRoleComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
            bool hasPinChange = !string.IsNullOrWhiteSpace(EditStaffPinBox.Password);

            bool nfcChanged = uid != _origStaffUid;

            if (EditStaffNfcReasonBox != null)
            {
                EditStaffNfcReasonBox.Visibility = nfcChanged ? Visibility.Visible : Visibility.Collapsed;
            }

            bool isDirty = name != _origStaffName || role != _origStaffRole || nfcChanged || hasPinChange;
            EditStaffDialog.IsPrimaryButtonEnabled = isDirty;
        }

        private void EditStaffChangeNfcButton_Click(object sender, RoutedEventArgs e)
        {
            _isAwaitingStaffNfcReplacementScan = true;
            EditStaffChangeNfcButton.IsEnabled = false;
            EditStaffNfcScanStatusText.Text = "Waiting for NFC tap... present the new staff card.";
            EditStaffNfcScanStatusText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 96, 165, 250));
            EditStaffNfcScanStatusText.Visibility = Visibility.Visible;
        }

        private async Task HandleStaffNfcReplacementScanAsync(string uid)
        {
            _isAwaitingStaffNfcReplacementScan = false;
            EditStaffChangeNfcButton.IsEnabled = true;

            if (IsInvalidUid(uid))
            {
                EditStaffNfcScanStatusText.Text = "Bad read. Try again.";
                EditStaffNfcScanStatusText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 248, 113, 113));
                return;
            }

            if (DatabaseMonitor.IsOnline)
            {
                var existing = await _database.GetStaffDetailsAsync(uid);
                if (!string.IsNullOrEmpty(existing.Role) && uid != _origStaffUid)
                {
                    EditStaffNfcScanStatusText.Text = $"Card belongs to {existing.FullName}. Tap a different card.";
                    EditStaffNfcScanStatusText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 248, 113, 113));
                    return;
                }
            }

            EditStaffNfcUidBox.Text = uid;
            EditStaffNfcScanStatusText.Text = "New card captured. Note the reason below.";
            EditStaffNfcScanStatusText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 52, 211, 153));
        }

        private void EditStaffDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            string name = EditStaffNameBox.Text.Trim();
            string uid = EditStaffNfcUidBox.Text.Trim();
            string pin = EditStaffPinBox.Password.Trim();
            bool nfcChanged = uid != _origStaffUid;

            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(uid))
            {
                EditStaffStatusText.Text = "Name and NFC UID are strictly required.";
                EditStaffStatusText.Visibility = Visibility.Visible;
                PlayErrorAlert();
                args.Cancel = true;
                return;
            }

            if (!string.IsNullOrWhiteSpace(pin) && (pin.Length != 4 || !pin.All(char.IsDigit)))
            {
                EditStaffStatusText.Text = "New PIN must be exactly 4 numeric digits.";
                EditStaffStatusText.Visibility = Visibility.Visible;
                PlayErrorAlert();
                args.Cancel = true;
                return;
            }

            if (nfcChanged && string.IsNullOrWhiteSpace(EditStaffNfcReasonBox.Text))
            {
                EditStaffStatusText.Text = "Please provide a reason for the NFC card replacement.";
                EditStaffStatusText.Visibility = Visibility.Visible;
                PlayErrorAlert();
                args.Cancel = true;
                return;
            }

            string role = (EditStaffRoleComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Security Personnel";

            _pendingStaffName = name;
            _pendingStaffUid = uid;
            _pendingStaffPin = pin;
            _pendingStaffRole = role;
            _pendingStaffNfcReason = EditStaffNfcReasonBox.Text.Trim();

            if (AppSession.CurrentStaffRoleLabel == "Master Admin")
            {
                _pendingStaffAction = "EDIT_STAFF_EXECUTE";
            }
            else
            {
                _pendingStaffAction = "EDIT_STAFF_AUTH";
            }
        }

        private void EditStaffDeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (AppSession.CurrentStaffRoleLabel != "Master Admin")
            {
                EditStaffStatusText.Text = "Only Master Administrators can delete staff profiles.";
                EditStaffStatusText.Visibility = Visibility.Visible;
                PlayErrorAlert();
                return;
            }

            if (_origStaffName == AppSession.CurrentStaffName)
            {
                EditStaffStatusText.Text = "You cannot delete your currently logged-in account.";
                EditStaffStatusText.Visibility = Visibility.Visible;
                PlayErrorAlert();
                return;
            }

            _pendingStaffAction = "DELETE";
            EditStaffDialog.Hide();
        }

        private async Task ExecuteStaffEditAsync(string authorizedBy)
        {
            try
            {
                await _database.UpdateStaffAsync(_origStaffUid, _pendingStaffUid, _pendingStaffName, _pendingStaffRole, _pendingStaffPin);

                List<string> changes = new List<string>();
                if (_origStaffName != _pendingStaffName) changes.Add("Name");
                if (_origStaffRole != _pendingStaffRole) changes.Add($"Role (→ {_pendingStaffRole})");
                if (!string.IsNullOrWhiteSpace(_pendingStaffPin)) changes.Add("Reset PIN");
                if (_origStaffUid != _pendingStaffUid) changes.Add($"Replaced NFC Card (Reason: {_pendingStaffNfcReason})");

                string changesString = changes.Count > 0 ? string.Join(", ", changes) : "Forced save";
                string logMessage = $"Edited staff profile for {_pendingStaffName}. Changes: {changesString}.";

                await _database.AddAlertAsync(authorizedBy, "ADMIN_OVERRIDE", logMessage);

                StatusTextBlock.Text = $"Successfully updated staff: {_pendingStaffName}";
                StatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 52, 211, 153));
                PlaySuccessPing();

                _pendingStaffNfcReason = "";

                await ReopenStaffDirectory();
                await RefreshDashboardAsync();
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"Staff update failed: {ex.Message}";
                StatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 248, 113, 113));
                PlayErrorAlert();
            }
        }

        private async Task ExecuteStaffDeletionAsync(string authorizedBy)
        {
            try
            {
                using var connection = new MySqlConnection(DatabaseService.ConnectionString);
                await connection.OpenAsync();

                using var cmd = new MySqlCommand("DELETE FROM staff WHERE nfc_uid = @uid", connection);
                cmd.Parameters.AddWithValue("@uid", _origStaffUid);
                await cmd.ExecuteNonQueryAsync();

                await _database.AddAlertAsync(authorizedBy, "ADMIN_OVERRIDE", $"Permanently deleted staff profile for {_origStaffName} ({_origStaffRole}).");

                StatusTextBlock.Text = $"Successfully deleted staff profile: {_origStaffName}";
                StatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 52, 211, 153));
                PlaySuccessPing();

                await ReopenStaffDirectory();
                await RefreshDashboardAsync();
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"Staff deletion failed: {ex.Message}";
                StatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 248, 113, 113));
                PlayErrorAlert();
            }
        }

        private static bool IsInvalidUid(string uid)
        {
            if (string.IsNullOrWhiteSpace(uid)) return true;
            string[] parts = uid.Split(':');
            if (parts.Length != 4 && parts.Length != 7) return true;

            bool allZero = true;
            foreach (string part in parts) { if (part != "00") { allZero = false; break; } }
            if (allZero) return true;

            if (parts.Length >= 4)
            {
                int start = parts.Length - 4;
                bool trailingZeros = true;
                for (int i = start; i < parts.Length; i++) { if (parts[i] != "00") { trailingZeros = false; break; } }
                if (trailingZeros) return true;
            }
            return false;
        }
    }
}