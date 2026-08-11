using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.IO;
using System.IO.Ports;
using System.Threading.Tasks;
using WinRT.Interop;
using MySqlConnector;
using System.Net.Http;
using System.Text.Json;
using System.Text;

namespace NFC_System
{
    public static class AppSession
    {
        public static bool IsLoggedIn { get; set; } = false;
        public static bool IsAdmin { get; set; } = false;
        // THE FIX: Added the SecurityAdmin boolean flag
        public static bool IsSecurityAdmin { get; set; } = false;
        public static bool IsEventOrganizer { get; set; } = false;
        public static string CurrentStaffName { get; set; } = "";
        public static string CurrentStaffRoleLabel { get; set; } = "";
    }

    public sealed partial class MainWindow : Window
    {
        private SerialPort? _serialPort;
        private readonly DatabaseService _database = new();
        private string _currentPort = "COM3";
        private bool _isAuthenticating = false;

        private bool _isFirstTimeSetup = false;
        private string _pendingMasterUid = "";

        private bool _isForceClosing = false;
        private bool _isAwaitingAdminAuth = false;
        private string _pendingAdminAction = "";
        private string _pendingAdminSeverity = "";

        private const string FIREBASE_PROJECT_ID = "nfc-system-d6ec2";
        private const string FIREBASE_API_KEY = "AIzaSyCRz3BVZaLO7lA5nlKDlj187su5piFhdRo";
        private const string FIRESTORE_URL = $"https://firestore.googleapis.com/v1/projects/{FIREBASE_PROJECT_ID}/databases/(default)/documents/MasterCard/master_admin?key={FIREBASE_API_KEY}";
        private static readonly HttpClient _httpClient = new HttpClient();

        public MainWindow()
        {
            this.InitializeComponent();
            DatabaseMonitor.ConnectionStatusChanged += UpdateOfflineBanner;
            UpdateOfflineBanner(DatabaseMonitor.IsOnline);
            MaximizeWindow();

            IntPtr hWnd = WindowNative.GetWindowHandle(this);
            WindowId windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
            AppWindow appWindow = AppWindow.GetFromWindowId(windowId);
            appWindow.Closing += AppWindow_Closing;

            this.Closed += MainWindow_Closed;

            DatabaseService.LoadConfig();

            if (this.Content is FrameworkElement rootElement)
            {
                rootElement.Loaded += MainWindow_Loaded;
            }
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

                    TryConnectSerial(_currentPort);

                    AdminAuthDialog.XamlRoot = this.Content.XamlRoot;
                    var authResult = await AdminAuthDialog.ShowAsync();

                    if (authResult == ContentDialogResult.None && _isAwaitingAdminAuth)
                    {
                        _isAwaitingAdminAuth = false;
                        _pendingAdminAction = "";

                        CloseSerialPort();
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
            DashboardContent.Visibility = Visibility.Collapsed;
            SetupOverlay.Visibility = Visibility.Collapsed;
            LoginOverlay.Visibility = Visibility.Visible;

            LoginStatusText.Text = "Pushing data to cloud. Please do not force close...";
            LoginStatusText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White);
            LoginLoadingRing.IsActive = true;
            LoginLoadingRing.Visibility = Visibility.Visible;

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
                }
            }
            catch { }

            _isForceClosing = true;
            Application.Current.Exit();
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (AppSession.IsLoggedIn)
            {
                ApplyRoleBasedAccess();
            }
            else
            {
                await InitializeSystemAsync();
            }
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

        private async Task InitializeSystemAsync()
        {
            LoginStatusText.Text = "Synchronizing with Cloud & Local Databases...";
            LoginStatusText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 160, 160, 160));
            LoginLoadingRing.IsActive = true;
            LoginLoadingRing.Visibility = Visibility.Visible;

            try
            {
                if (DatabaseMonitor.IsOnline)
                {
                    try { await _database.EnsureSchemaAsync(); } catch { }
                }

                int staffCount = 0;
                if (DatabaseMonitor.IsOnline)
                {
                    using (var connection = new MySqlConnection(DatabaseService.ConnectionString))
                    {
                        await connection.OpenAsync();
                        using var cmd = new MySqlCommand("SELECT COUNT(*) FROM staff", connection);
                        staffCount = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                    }
                }

                if (staffCount == 0 && DatabaseMonitor.IsOnline)
                {
                    try
                    {
                        var response = await _httpClient.GetAsync(FIRESTORE_URL);
                        if (response.IsSuccessStatusCode)
                        {
                            var json = await response.Content.ReadAsStringAsync();
                            if (!string.IsNullOrWhiteSpace(json))
                            {
                                using JsonDocument doc = JsonDocument.Parse(json);
                                if (doc.RootElement.TryGetProperty("fields", out var fields))
                                {
                                    string globalMasterUid = "";
                                    string globalMasterName = "Global Master Admin";

                                    if (fields.TryGetProperty("uid", out var uidField) && uidField.TryGetProperty("stringValue", out var uidVal))
                                        globalMasterUid = uidVal.GetString() ?? "";

                                    if (fields.TryGetProperty("name", out var nameField) && nameField.TryGetProperty("stringValue", out var nameVal))
                                        globalMasterName = nameVal.GetString() ?? "Global Master Admin";

                                    if (!string.IsNullOrWhiteSpace(globalMasterUid))
                                    {
                                        await _database.RegisterStaffAsync(globalMasterUid, globalMasterName, "Master Administrator");
                                        await _database.AddAlertAsync(null, "ADMIN_ACTION", $"Global Master Card synced from Firestore: '{globalMasterName}'.");
                                        staffCount = 1;
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Cloud Master Sync Failed: {ex.Message}");
                    }
                }

                if (DatabaseMonitor.IsOnline)
                {
                    try { _currentPort = await _database.GetSettingAsync("nfc_com_port", "COM3"); } catch { _currentPort = "COM3"; }
                }
                else
                {
                    _currentPort = "COM3";
                }

                bool isConnected = TryConnectSerial(_currentPort);

                LoginLoadingRing.IsActive = false;
                LoginLoadingRing.Visibility = Visibility.Collapsed;

                if (staffCount == 0 && DatabaseMonitor.IsOnline)
                {
                    _isFirstTimeSetup = true;
                    LoginOverlay.Visibility = Visibility.Collapsed;
                    SetupOverlay.Visibility = Visibility.Visible;
                    SetupInstructionText.Text = "Please tap an NFC card to register as the Master Administrator.";
                    SetupFormPanel.Visibility = Visibility.Collapsed;
                }
                else
                {
                    LoginStatusText.Text = "Please tap your Staff or Admin NFC ID to log in.";
                }

                if (isConnected)
                {
                    ContentDialog successDialog = new ContentDialog
                    {
                        Title = "Hardware Linked",
                        Content = $"The NFC Terminal was successfully detected on port {_currentPort}.",
                        CloseButtonText = "OK",
                        XamlRoot = this.Content.XamlRoot
                    };
                    await successDialog.ShowAsync();
                }
                else
                {
                    ContentDialog warningDialog = new ContentDialog
                    {
                        Title = "Hardware Warning",
                        Content = $"Could not find the NFC Terminal on {_currentPort}. Please check the USB cable or update the port in Settings.",
                        CloseButtonText = "Continue",
                        XamlRoot = this.Content.XamlRoot
                    };
                    await warningDialog.ShowAsync();
                }
            }
            catch (Exception ex)
            {
                LoginLoadingRing.IsActive = false;
                LoginLoadingRing.Visibility = Visibility.Collapsed;
                LoginStatusText.Text = "Database connection failed.";

                DbErrorTextBlock.Text = $"Connection Error: {ex.Message}";
                ServerIpTextBox.Text = DatabaseService.ServerIp;
                DbConfigDialog.XamlRoot = this.Content.XamlRoot;

                var result = await DbConfigDialog.ShowAsync();

                if (result == ContentDialogResult.Primary)
                {
                    DatabaseService.SaveConfig(ServerIpTextBox.Text);
                    _ = InitializeSystemAsync();
                }
                else
                {
                    LoginStatusText.Text = "System Offline. Please tap an authorized offline key.";
                    LoginStatusText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Orange);

                    _currentPort = "COM3";
                    TryConnectSerial(_currentPort);
                }
            }
        }

        private async void CompleteSetupButton_Click(object sender, RoutedEventArgs e)
        {
            string firstName = SetupFirstNameBox.Text.Trim();
            string lastName = SetupLastNameBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName))
            {
                SetupInstructionText.Text = "Please provide both First and Last names.";
                SetupInstructionText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 248, 113, 113));
                return;
            }

            string fullName = $"{firstName} {lastName}";
            CompleteSetupButton.IsEnabled = false;

            try
            {
                if (DatabaseMonitor.IsOnline)
                {
                    await _database.RegisterStaffAsync(_pendingMasterUid, fullName, "Master Administrator");
                    await _database.AddAlertAsync(null, "ADMIN_ACTION", $"System initialized. Master Administrator '{fullName}' registered.");
                }

                if (DatabaseMonitor.IsOnline)
                {
                    try
                    {
                        var firestorePayload = new
                        {
                            fields = new
                            {
                                uid = new { stringValue = _pendingMasterUid },
                                name = new { stringValue = fullName },
                                role = new { stringValue = "Master Administrator" },
                                created_at = new { stringValue = DateTime.UtcNow.ToString("O") }
                            }
                        };

                        string jsonPayload = JsonSerializer.Serialize(firestorePayload);
                        var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
                        await _httpClient.PatchAsync(FIRESTORE_URL, content);
                    }
                    catch { }
                }

                _isFirstTimeSetup = false;
                SetupOverlay.Visibility = Visibility.Collapsed;

                AppSession.IsAdmin = true;
                AppSession.IsSecurityAdmin = false;
                AppSession.IsEventOrganizer = false;
                AppSession.IsLoggedIn = true;
                AppSession.CurrentStaffName = fullName;
                AppSession.CurrentStaffRoleLabel = "Master Admin";

                PlaySuccessPing();
                ApplyRoleBasedAccess();
            }
            catch (Exception ex)
            {
                SetupInstructionText.Text = $"Database Error: {ex.Message}";
                CompleteSetupButton.IsEnabled = true;
            }
        }

        private bool TryConnectSerial(string portName)
        {
            try
            {
                _serialPort = new SerialPort(portName, 115200);
                _serialPort.NewLine = "\n";
                _serialPort.DataReceived += SerialPort_DataReceived;
                _serialPort.Open();
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                if (_serialPort == null || !_serialPort.IsOpen) return;
                string line = _serialPort.ReadLine().Trim();

                if (line.StartsWith("UID="))
                {
                    string uid = line.Substring(4).Trim();

                    if (_isAwaitingAdminAuth)
                    {
                        DispatcherQueue.TryEnqueue(async () => await HandleAdminAuthScanAsync(uid));
                        return;
                    }

                    if (_isAuthenticating || AppSession.IsLoggedIn) return;

                    _isAuthenticating = true;
                    DispatcherQueue.TryEnqueue(() => _ = ProcessLoginScanAsync(uid));
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
                PlaySuccessPing();

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
                PlayErrorAlert();
            }
        }

        private async Task ProcessLoginScanAsync(string uid)
        {
            if (_isFirstTimeSetup)
            {
                if (SetupFormPanel.Visibility == Visibility.Visible)
                {
                    _isAuthenticating = false;
                    return;
                }

                _pendingMasterUid = uid;
                SetupInstructionText.Text = $"Card Detected (UID: {uid}).\nPlease enter your name to finalize registration.";
                SetupInstructionText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White);
                SimulateSetupButton.Visibility = Visibility.Collapsed;
                SetupFormPanel.Visibility = Visibility.Visible;

                _isAuthenticating = false;
                return;
            }

            LoginStatusText.Text = "Authenticating...";
            LoginStatusText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White);
            LoginLoadingRing.IsActive = true;
            LoginLoadingRing.Visibility = Visibility.Visible;

            string? role = null;
            string? fullName = null;

            try
            {
                if (DatabaseMonitor.IsOnline)
                {
                    var details = await _database.GetStaffDetailsAsync(uid);
                    role = details.Role;
                    fullName = details.FullName;
                }
            }
            catch { }

            if (role == null)
            {
                if (uid == "04:A1:B2:C3")
                {
                    role = "Master Administrator";
                    fullName = "Master Admin";
                }
                else if (uid == "VALID_SEC_ADMIN") // Setup for our simulator button
                {
                    role = "Security Admin";
                    fullName = "Simulated Chief Guard";
                }
                else if (uid == "VALID_STAFF_CARD")
                {
                    role = "Security Personnel";
                    fullName = "Simulated Guard";
                }
                else if (uid == "VALID_EVENT_CARD")
                {
                    role = "Event Organizer";
                    fullName = "Simulated Organizer";
                }
            }

            if (role == "Administrator" || role == "Master Administrator")
            {
                AppSession.IsAdmin = true;
                AppSession.IsSecurityAdmin = false;
                AppSession.IsEventOrganizer = false;
                AppSession.IsLoggedIn = true;
                AppSession.CurrentStaffName = fullName ?? "Administrator";
                AppSession.CurrentStaffRoleLabel = role == "Master Administrator" ? "Master Admin" : "Admin";

                if (DatabaseMonitor.IsOnline)
                {
                    try { await _database.AddAlertAsync(AppSession.CurrentStaffName, "ADMIN_LOGIN", $"{role} logged in: {AppSession.CurrentStaffName} (NFC UID: {uid})"); } catch { }
                }

                PlaySuccessPing();
                ApplyRoleBasedAccess();
            }
            // THE FIX: Intercept the new Security Admin role
            else if (role == "Security Admin")
            {
                AppSession.IsAdmin = false;
                AppSession.IsSecurityAdmin = true;
                AppSession.IsEventOrganizer = false;
                AppSession.IsLoggedIn = true;
                AppSession.CurrentStaffName = fullName ?? "Security Admin";
                AppSession.CurrentStaffRoleLabel = "Security Admin";

                if (DatabaseMonitor.IsOnline)
                {
                    try { await _database.AddAlertAsync(AppSession.CurrentStaffName, "STAFF_LOGIN", $"{role} logged in: {AppSession.CurrentStaffName} (NFC UID: {uid})"); } catch { }
                }

                PlaySuccessPing();
                ApplyRoleBasedAccess();
            }
            else if (role == "Event Organizer")
            {
                AppSession.IsAdmin = false;
                AppSession.IsSecurityAdmin = false;
                AppSession.IsEventOrganizer = true;
                AppSession.IsLoggedIn = true;
                AppSession.CurrentStaffName = fullName ?? "Organizer";
                AppSession.CurrentStaffRoleLabel = "Event Organizer";

                if (DatabaseMonitor.IsOnline)
                {
                    try { await _database.AddAlertAsync(AppSession.CurrentStaffName, "STAFF_LOGIN", $"{role} logged in: {AppSession.CurrentStaffName} (NFC UID: {uid})"); } catch { }
                }

                PlaySuccessPing();
                ApplyRoleBasedAccess();
            }
            else if (role == "Security Personnel")
            {
                AppSession.IsAdmin = false;
                AppSession.IsSecurityAdmin = false;
                AppSession.IsEventOrganizer = false;
                AppSession.IsLoggedIn = true;
                AppSession.CurrentStaffName = fullName ?? "Guard";
                AppSession.CurrentStaffRoleLabel = "Personnel";

                if (DatabaseMonitor.IsOnline)
                {
                    try { await _database.AddAlertAsync(AppSession.CurrentStaffName, "STAFF_LOGIN", $"{role} logged in: {AppSession.CurrentStaffName} (NFC UID: {uid})"); } catch { }
                }

                PlaySuccessPing();
                ApplyRoleBasedAccess();
            }
            else
            {
                LoginStatusText.Text = "Access Denied. Card not registered for Staff Access.";
                LoginStatusText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 248, 113, 113));
                LoginLoadingRing.IsActive = false;
                LoginLoadingRing.Visibility = Visibility.Collapsed;
            }

            _isAuthenticating = false;
        }

        private void ProcessLoginScan(string uid)
        {
            if (_isAuthenticating) return;
            _isAuthenticating = true;
            _ = ProcessLoginScanAsync(uid);
        }

        private void ApplyRoleBasedAccess()
        {
            CloseSerialPort();

            LoginLoadingRing.IsActive = false;
            LoginLoadingRing.Visibility = Visibility.Collapsed;
            LoginStatusText.Text = "Please tap your Staff or Admin NFC ID to log in.";
            LoginStatusText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 160, 160, 160));

            LoginOverlay.Visibility = Visibility.Collapsed;
            DashboardContent.Visibility = Visibility.Visible;

            ActiveRoleText.Text = $"{AppSession.CurrentStaffName} ({AppSession.CurrentStaffRoleLabel})";

            if (AppSession.IsAdmin)
            {
                RegistrationCard.SetValue(Grid.RowProperty, 0);
                RegistrationCard.SetValue(Grid.ColumnProperty, 0);

                EventAttendanceCard.SetValue(Grid.RowProperty, 0);
                EventAttendanceCard.SetValue(Grid.ColumnProperty, 1);

                VerificationCard.SetValue(Grid.RowProperty, 0);
                VerificationCard.SetValue(Grid.ColumnProperty, 2);

                StudentDirectoryCard.SetValue(Grid.RowProperty, 1);
                StudentDirectoryCard.SetValue(Grid.ColumnProperty, 0);

                SecurityAdminCard.SetValue(Grid.RowProperty, 1);
                SecurityAdminCard.SetValue(Grid.ColumnProperty, 1);

                EventReportsCard.SetValue(Grid.RowProperty, 1);
                EventReportsCard.SetValue(Grid.ColumnProperty, 2);

                RegistrationCard.Visibility = Visibility.Visible;
                EventAttendanceCard.Visibility = Visibility.Visible;
                VerificationCard.Visibility = Visibility.Visible;
                StudentDirectoryCard.Visibility = Visibility.Visible;
                SecurityAdminCard.Visibility = Visibility.Visible;
                EventReportsCard.Visibility = Visibility.Visible;
                DashboardSettingsButton.Visibility = Visibility.Visible;
            }
            // THE FIX: Display the 3 specific cards for the Security Admin
            else if (AppSession.IsSecurityAdmin)
            {
                RegistrationCard.Visibility = Visibility.Collapsed;
                StudentDirectoryCard.Visibility = Visibility.Collapsed;
                EventReportsCard.Visibility = Visibility.Collapsed;
                DashboardSettingsButton.Visibility = Visibility.Collapsed;

                EventAttendanceCard.SetValue(Grid.RowProperty, 0);
                EventAttendanceCard.SetValue(Grid.ColumnProperty, 0);

                VerificationCard.SetValue(Grid.RowProperty, 0);
                VerificationCard.SetValue(Grid.ColumnProperty, 1);

                SecurityAdminCard.SetValue(Grid.RowProperty, 0);
                SecurityAdminCard.SetValue(Grid.ColumnProperty, 2);

                EventAttendanceCard.Visibility = Visibility.Visible;
                VerificationCard.Visibility = Visibility.Visible;
                SecurityAdminCard.Visibility = Visibility.Visible;
            }
            else if (AppSession.IsEventOrganizer)
            {
                RegistrationCard.Visibility = Visibility.Collapsed;
                VerificationCard.Visibility = Visibility.Collapsed;
                SecurityAdminCard.Visibility = Visibility.Collapsed;
                DashboardSettingsButton.Visibility = Visibility.Collapsed;

                EventAttendanceCard.SetValue(Grid.RowProperty, 0);
                EventAttendanceCard.SetValue(Grid.ColumnProperty, 0);

                EventReportsCard.SetValue(Grid.RowProperty, 0);
                EventReportsCard.SetValue(Grid.ColumnProperty, 1);

                StudentDirectoryCard.SetValue(Grid.RowProperty, 0);
                StudentDirectoryCard.SetValue(Grid.ColumnProperty, 2);

                EventAttendanceCard.Visibility = Visibility.Visible;
                EventReportsCard.Visibility = Visibility.Visible;
                StudentDirectoryCard.Visibility = Visibility.Visible;
            }
            else // Security Personnel
            {
                RegistrationCard.Visibility = Visibility.Collapsed;
                StudentDirectoryCard.Visibility = Visibility.Collapsed;
                SecurityAdminCard.Visibility = Visibility.Collapsed;
                EventReportsCard.Visibility = Visibility.Collapsed;
                DashboardSettingsButton.Visibility = Visibility.Collapsed;

                EventAttendanceCard.SetValue(Grid.RowProperty, 0);
                EventAttendanceCard.SetValue(Grid.ColumnProperty, 0);

                VerificationCard.SetValue(Grid.RowProperty, 0);
                VerificationCard.SetValue(Grid.ColumnProperty, 1);

                EventAttendanceCard.Visibility = Visibility.Visible;
                VerificationCard.Visibility = Visibility.Visible;
            }
        }

        private void SignOut_Click(object sender, RoutedEventArgs e)
        {
            if (DatabaseMonitor.IsOnline)
            {
                try { _ = _database.AddAlertAsync(AppSession.CurrentStaffName, "STAFF_LOGOUT", $"{AppSession.CurrentStaffName} signed out of the system."); } catch { }
            }

            AppSession.IsLoggedIn = false;
            AppSession.IsAdmin = false;
            AppSession.IsSecurityAdmin = false;
            AppSession.IsEventOrganizer = false;
            AppSession.CurrentStaffName = "";
            AppSession.CurrentStaffRoleLabel = "";
            _isAuthenticating = false;

            DashboardContent.Visibility = Visibility.Collapsed;
            LoginStatusText.Text = "Please tap your Staff or Admin NFC ID to log in.";
            LoginStatusText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 160, 160, 160));
            LoginLoadingRing.IsActive = false;
            LoginLoadingRing.Visibility = Visibility.Collapsed;
            LoginOverlay.Visibility = Visibility.Visible;

            TryConnectSerial(_currentPort);
        }

        private void SimulateAdminLogin_Click(object sender, RoutedEventArgs e) => ProcessLoginScan("04:A1:B2:C3");
        private void SimulateSecAdminLogin_Click(object sender, RoutedEventArgs e) => ProcessLoginScan("VALID_SEC_ADMIN");
        private void SimulatePersonnelLogin_Click(object sender, RoutedEventArgs e) => ProcessLoginScan("VALID_STAFF_CARD");

        private void Registration_Click(object sender, RoutedEventArgs e)
        {
            new RegistrationWindow().Activate();
            this.Close();
        }

        private void Verification_Click(object sender, RoutedEventArgs e)
        {
            new VerificationWindow().Activate();
            this.Close();
        }

        private void EventAttendance_Click(object sender, RoutedEventArgs e)
        {
            new EventAttendanceWindow().Activate();
            this.Close();
        }

        private void StudentDirectory_Click(object sender, RoutedEventArgs e)
        {
            new StudentManagementWindow().Activate();
            this.Close();
        }

        private void EventManagement_Click(object sender, RoutedEventArgs e)
        {
            new EventReportsWindow().Activate();
            this.Close();
        }

        private void DashboardSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            new SettingsWindow().Activate();
        }

        private void Settings_Click(object sender, RoutedEventArgs e)
        {
            new SecurityDashboardWindow().Activate();
            this.Close();
        }

        private void MainWindow_Closed(object sender, WindowEventArgs args)
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
            });
        }

        private void CloseSerialPort()
        {
            SerialPort? portToClose = _serialPort;
            _serialPort = null;

            if (portToClose != null)
            {
                Task.Run(() =>
                {
                    try
                    {
                        portToClose.DataReceived -= SerialPort_DataReceived;
                        if (portToClose.IsOpen) portToClose.Close();
                        portToClose.Dispose();
                    }
                    catch { }
                });
            }
        }

        private async void RefreshComPort_Click(object sender, RoutedEventArgs e)
        {
            LoginStatusText.Text = "Reconnecting NFC Terminal...";
            LoginStatusText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White);
            LoginLoadingRing.IsActive = true;
            LoginLoadingRing.Visibility = Visibility.Visible;

            if (_serialPort != null)
            {
                try
                {
                    _serialPort.DataReceived -= SerialPort_DataReceived;
                    if (_serialPort.IsOpen) _serialPort.Close();
                    _serialPort.Dispose();
                }
                catch { }
                finally
                {
                    _serialPort = null;
                }
            }

            await Task.Delay(500);

            if (DatabaseMonitor.IsOnline)
            {
                try { _currentPort = await _database.GetSettingAsync("nfc_com_port", "COM3"); } catch { }
            }

            bool isConnected = TryConnectSerial(_currentPort);

            LoginLoadingRing.IsActive = false;
            LoginLoadingRing.Visibility = Visibility.Collapsed;

            if (isConnected)
            {
                LoginStatusText.Text = "Please tap your Staff or Admin NFC ID to log in.";
                LoginStatusText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 160, 160, 160));

                ContentDialog successDialog = new ContentDialog
                {
                    Title = "Hardware Linked",
                    Content = $"The NFC Terminal was successfully reconnected on port {_currentPort}.",
                    CloseButtonText = "OK",
                    XamlRoot = this.Content.XamlRoot
                };
                await successDialog.ShowAsync();
            }
            else
            {
                LoginStatusText.Text = "Connection failed. Please check the USB cable.";
                LoginStatusText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 248, 113, 113));

                ContentDialog warningDialog = new ContentDialog
                {
                    Title = "Hardware Warning",
                    Content = $"Could not find the NFC Terminal on {_currentPort}. Please check the USB cable or update the port in Settings.",
                    CloseButtonText = "Continue",
                    XamlRoot = this.Content.XamlRoot
                };
                await warningDialog.ShowAsync();
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
    }
}