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
    // GLOBAL SESSION STATE
    public static class AppSession
    {
        public static bool IsLoggedIn { get; set; } = false;
        public static bool IsAdmin { get; set; } = false;
        public static string CurrentStaffName { get; set; } = "";
        public static string CurrentStaffRoleLabel { get; set; } = "";
    }

    public sealed partial class MainWindow : Window
    {
        private SerialPort? _serialPort;
        private readonly DatabaseService _database = new();
        private string _currentPort = "COM3";
        private bool _isAuthenticating = false;

        // FIRST-TIME SETUP VARIABLES
        private bool _isFirstTimeSetup = false;
        private string _pendingMasterUid = "";

        // APP LIFECYCLE
        private bool _isForceClosing = false;

        // ====================================================================
        // CLOUD FIRESTORE CONFIGURATION
        // ====================================================================
        private const string FIREBASE_PROJECT_ID = "nfc-system-d6ec2";
        private const string FIRESTORE_URL = $"https://firestore.googleapis.com/v1/projects/{FIREBASE_PROJECT_ID}/databases/(default)/documents/MasterCard/master_admin";
        private static readonly HttpClient _httpClient = new HttpClient();

        public MainWindow()
        {
            this.InitializeComponent();
            // Subscribe to the live monitor
            DatabaseMonitor.ConnectionStatusChanged += UpdateOfflineBanner;
            UpdateOfflineBanner(DatabaseMonitor.IsOnline); // Set initial state on load
            MaximizeWindow();

            // THE FIX (ITEM 10): Hook into native window closing event to intercept exit
            IntPtr hWnd = WindowNative.GetWindowHandle(this);
            WindowId windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
            AppWindow appWindow = AppWindow.GetFromWindowId(windowId);
            appWindow.Closing += AppWindow_Closing;

            this.Closed += MainWindow_Closed;

            // Load the dynamic IP configuration BEFORE anything else runs
            DatabaseService.LoadConfig();

            if (this.Content is FrameworkElement rootElement)
            {
                rootElement.Loaded += MainWindow_Loaded;
            }
        }

        private async void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
        {
            if (_isForceClosing) return;

            args.Cancel = true; // Always intercept the initial close command

            ContentDialog exitDialog = new ContentDialog
            {
                Title = "Exit Application",
                Content = "Wait! You may have unsynced data. Would you like to push it to the cloud before exiting?",
                PrimaryButtonText = "Push to Cloud & Exit",
                SecondaryButtonText = "Exit Anyway",
                CloseButtonText = "Cancel",
                XamlRoot = this.Content.XamlRoot
            };

            var result = await exitDialog.ShowAsync();

            if (result == ContentDialogResult.Primary)
            {
                // Mask the UI to show syncing status
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
            else if (result == ContentDialogResult.Secondary)
            {
                _isForceClosing = true;
                Application.Current.Exit();
            }
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

        /* =========================================================================
         * SYSTEM INITIALIZATION & FIREBASE CLOUD SYNC
         * ========================================================================= */

        private async Task InitializeSystemAsync()
        {
            LoginStatusText.Text = "Synchronizing with Cloud & Local Databases...";
            LoginStatusText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 160, 160, 160));
            LoginLoadingRing.IsActive = true;
            LoginLoadingRing.Visibility = Visibility.Visible;

            try
            {
                // 1. Ensure local schema exists
                await _database.EnsureSchemaAsync();

                // 2. Count local staff
                int staffCount = 0;
                using (var connection = new MySqlConnection(DatabaseService.ConnectionString))
                {
                    await connection.OpenAsync();
                    using var cmd = new MySqlCommand("SELECT COUNT(*) FROM staff", connection);
                    staffCount = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                }

                // 3. Check Firestore for Global Master Card if local MySQL has no staff
                if (staffCount == 0)
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
                                    string globalMasterUid = fields.GetProperty("uid").GetProperty("stringValue").GetString() ?? "";
                                    string globalMasterName = fields.GetProperty("name").GetProperty("stringValue").GetString() ?? "Global Master Admin";

                                    if (!string.IsNullOrWhiteSpace(globalMasterUid))
                                    {
                                        // Sync the global master card into local MySQL database
                                        await _database.RegisterStaffAsync(globalMasterUid, globalMasterName, "Master Administrator");
                                        await _database.AddAlertAsync(null, "ADMIN_ACTION", $"Global Master Card synced from Firestore: '{globalMasterName}'.");

                                        staffCount = 1; // Mark as initialized so setup screen is skipped
                                    }
                                }
                            }
                        }
                    }
                    catch
                    {
                        // Failsafe: Continue locally if offline
                    }
                }

                _currentPort = await _database.GetSettingAsync("nfc_com_port", "COM3");
                bool isConnected = TryConnectSerial(_currentPort);

                LoginLoadingRing.IsActive = false;
                LoginLoadingRing.Visibility = Visibility.Collapsed;

                if (staffCount == 0)
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
                // THE FIX: Graceful Offline Fallback instead of infinite crashing
                LoginLoadingRing.IsActive = false;
                LoginLoadingRing.Visibility = Visibility.Collapsed;
                LoginStatusText.Text = "Database connection failed.";

                DbErrorTextBlock.Text = $"Connection Error: {ex.Message}";
                ServerIpTextBox.Text = DatabaseService.ServerIp;
                DbConfigDialog.XamlRoot = this.Content.XamlRoot;

                var result = await DbConfigDialog.ShowAsync();

                if (result == ContentDialogResult.Primary)
                {
                    // User entered a new IP and hit Save & Retry
                    DatabaseService.SaveConfig(ServerIpTextBox.Text);
                    _ = InitializeSystemAsync(); // Restart the connection attempt
                }
                else
                {
                    // User hit "Continue Offline" 
                    LoginStatusText.Text = "System Offline. Please tap an authorized offline key.";
                    LoginStatusText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Orange);

                    try { _currentPort = await _database.GetSettingAsync("nfc_com_port", "COM3"); }
                    catch { _currentPort = "COM3"; } // Failsafe if DB is fully down

                    TryConnectSerial(_currentPort);
                }
            }
        }

        /* =========================================================================
         * FIRST-TIME SETUP REGISTRATION (UPLOADS TO FIREBASE)
         * ========================================================================= */
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
                // 1. Save locally to MySQL
                await _database.RegisterStaffAsync(_pendingMasterUid, fullName, "Master Administrator");
                await _database.AddAlertAsync(null, "ADMIN_ACTION", $"System initialized. Master Administrator '{fullName}' registered.");

                // 2. Upload to Cloud Firestore REST API
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

                    // PATCH creates or overwrites the document at /MasterCard/master_admin
                    await _httpClient.PatchAsync(FIRESTORE_URL, content);
                }
                catch
                {
                    // Failsafe: Local registration remains intact if offline
                }

                _isFirstTimeSetup = false;
                SetupOverlay.Visibility = Visibility.Collapsed;

                AppSession.IsAdmin = true;
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

        /* =========================================================================
         * ROLE-BASED ACCESS CONTROL & LOGIN LOGIC
         * ========================================================================= */

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
                if (_isAuthenticating || AppSession.IsLoggedIn) return;

                if (_serialPort == null || !_serialPort.IsOpen) return;
                string line = _serialPort.ReadLine().Trim();

                if (line.StartsWith("UID="))
                {
                    _isAuthenticating = true;
                    string uid = line.Substring(4).Trim();

                    DispatcherQueue.TryEnqueue(() => _ = ProcessLoginScanAsync(uid));
                }
            }
            catch { }
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
                // THE FIX: Skip the 3-second timeout wait if we already know we're offline
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
                // Hardcoded fallback keys for Offline Mode bypass
                if (uid == "04:A1:B2:C3")
                {
                    role = "Master Administrator";
                    fullName = "Master Admin";
                }
                else if (uid == "VALID_STAFF_CARD")
                {
                    role = "Security Personnel";
                    fullName = "Simulated Guard";
                }
            }

            if (role == "Administrator" || role == "Master Administrator")
            {
                AppSession.IsAdmin = true;
                AppSession.IsLoggedIn = true;
                AppSession.CurrentStaffName = fullName ?? "Administrator";
                AppSession.CurrentStaffRoleLabel = role == "Master Administrator" ? "Master Admin" : "Admin";

                try { await _database.AddAlertAsync(AppSession.CurrentStaffName, "ADMIN_LOGIN", $"{role} logged in: {AppSession.CurrentStaffName} (NFC UID: {uid})"); } catch { }

                PlaySuccessPing();
                ApplyRoleBasedAccess();
            }
            else if (role == "Security Personnel")
            {
                AppSession.IsAdmin = false;
                AppSession.IsLoggedIn = true;
                AppSession.CurrentStaffName = fullName ?? "Guard";
                AppSession.CurrentStaffRoleLabel = "Personnel";

                try { await _database.AddAlertAsync(AppSession.CurrentStaffName, "STAFF_LOGIN", $"{role} logged in: {AppSession.CurrentStaffName} (NFC UID: {uid})"); } catch { }

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

            ActiveRoleText.Text = $"{AppSession.CurrentStaffName}({AppSession.CurrentStaffRoleLabel})";

            if (AppSession.IsAdmin)
            {
                EventAttendanceCard.SetValue(Grid.ColumnProperty, 1);
                VerificationCard.SetValue(Grid.ColumnProperty, 2);

                RegistrationCard.Visibility = Visibility.Visible;
                EventAttendanceCard.Visibility = Visibility.Visible;
                VerificationCard.Visibility = Visibility.Visible;
                StudentDirectoryCard.Visibility = Visibility.Visible;
                SecurityAdminCard.Visibility = Visibility.Visible;
                EventReportsCard.Visibility = Visibility.Visible;
                DashboardSettingsButton.Visibility = Visibility.Visible;
            }
            else
            {
                RegistrationCard.Visibility = Visibility.Collapsed;
                StudentDirectoryCard.Visibility = Visibility.Collapsed;
                SecurityAdminCard.Visibility = Visibility.Collapsed;
                EventReportsCard.Visibility = Visibility.Collapsed;
                DashboardSettingsButton.Visibility = Visibility.Collapsed;

                EventAttendanceCard.SetValue(Grid.ColumnProperty, 0);
                VerificationCard.SetValue(Grid.ColumnProperty, 1);

                EventAttendanceCard.Visibility = Visibility.Visible;
                VerificationCard.Visibility = Visibility.Visible;
            }
        }

        private void SignOut_Click(object sender, RoutedEventArgs e)
        {
            string activeRole = AppSession.IsAdmin ? "Administrator" : "Security Personnel";
            try { _ = _database.AddAlertAsync(AppSession.CurrentStaffName, "STAFF_LOGOUT", $"{AppSession.CurrentStaffName} signed out of the system."); } catch { }

            AppSession.IsLoggedIn = false;
            AppSession.IsAdmin = false;
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

        // --- DEBUG SIMULATIONS ---
        private void SimulateAdminLogin_Click(object sender, RoutedEventArgs e) => ProcessLoginScan("04:A1:B2:C3");
        private void SimulatePersonnelLogin_Click(object sender, RoutedEventArgs e) => ProcessLoginScan("VALID_STAFF_CARD");


        /* =========================================================================
         * WINDOW NAVIGATION
         * ========================================================================= */

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
            // DispatcherQueue safely pushes the update to the UI thread
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
            // 1. Update UI to show activity
            LoginStatusText.Text = "Reconnecting NFC Terminal...";
            LoginStatusText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White);
            LoginLoadingRing.IsActive = true;
            LoginLoadingRing.Visibility = Visibility.Visible;

            // 2. Synchronously close existing port to prevent "Access Denied" exceptions
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

            // 3. Brief delay to allow the OS to fully release the COM port
            await Task.Delay(500);

            // 4. Fetch the latest port in case it was updated, and reconnect
            try { _currentPort = await _database.GetSettingAsync("nfc_com_port", "COM3"); } catch { }
            bool isConnected = TryConnectSerial(_currentPort);

            // 5. Restore UI
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