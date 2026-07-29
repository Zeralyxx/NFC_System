using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.IO.Ports;
using System.Threading.Tasks;
using WinRT.Interop;
using MySqlConnector;

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
        private string _currentPort = "COM3"; // Default
        private bool _isAuthenticating = false;

        // FIRST-TIME SETUP VARIABLES
        private bool _isFirstTimeSetup = false;
        private string _pendingMasterUid = "";

        public MainWindow()
        {
            this.InitializeComponent();
            MaximizeWindow();

            this.Closed += MainWindow_Closed;

            if (this.Content is FrameworkElement rootElement)
            {
                rootElement.Loaded += MainWindow_Loaded;
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

        /* =========================================================================
         * SYSTEM INITIALIZATION & HARDWARE POPUP
         * ========================================================================= */

        private async Task InitializeSystemAsync()
        {
            LoginStatusText.Text = "Initializing system and databases...";
            LoginStatusText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 160, 160, 160));
            LoginLoadingRing.IsActive = true;
            LoginLoadingRing.Visibility = Visibility.Visible;

            try
            {
                await _database.EnsureSchemaAsync();

                // CHECK IF FIRST-TIME RUN
                int staffCount = 0;
                using (var connection = new MySqlConnection(DatabaseService.ConnectionString))
                {
                    await connection.OpenAsync();
                    using var cmd = new MySqlCommand("SELECT COUNT(*) FROM staff", connection);
                    staffCount = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                }

                _currentPort = await _database.GetSettingAsync("nfc_com_port", "COM3");
                bool isConnected = TryConnectSerial(_currentPort);

                LoginLoadingRing.IsActive = false;
                LoginLoadingRing.Visibility = Visibility.Collapsed;

                if (staffCount == 0)
                {
                    // TRIGGER FIRST-TIME SETUP
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

                ContentDialog errorDialog = new ContentDialog
                {
                    Title = "Database Error",
                    Content = $"Failed to reach the MySQL Database. Ensure XAMPP is running.\n\nDetails: {ex.Message}",
                    CloseButtonText = "OK",
                    XamlRoot = this.Content.XamlRoot
                };
                await errorDialog.ShowAsync();
            }
        }

        /* =========================================================================
         * FIRST-TIME SETUP REGISTRATION
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
                await _database.RegisterStaffAsync(_pendingMasterUid, fullName, "Administrator");
                await _database.AddAlertAsync(null, "ADMIN_ACTION", $"System initialized. Master Administrator '{fullName}' registered.");

                _isFirstTimeSetup = false;
                SetupOverlay.Visibility = Visibility.Collapsed;

                // Log them in immediately
                AppSession.IsAdmin = true;
                AppSession.IsLoggedIn = true;
                AppSession.CurrentStaffName = fullName;
                AppSession.CurrentStaffRoleLabel = "Admin";

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
            // IF FIRST TIME SETUP IS ACTIVE, INTERCEPT THE TAP
            if (_isFirstTimeSetup)
            {
                if (SetupFormPanel.Visibility == Visibility.Visible)
                {
                    _isAuthenticating = false; // Ignore taps while typing name
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

            // REGULAR LOGIN FLOW
            LoginStatusText.Text = "Authenticating...";
            LoginStatusText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White);
            LoginLoadingRing.IsActive = true;
            LoginLoadingRing.Visibility = Visibility.Visible;

            string? role = null;
            string? fullName = null;

            try
            {
                var details = await _database.GetStaffDetailsAsync(uid);
                role = details.Role;
                fullName = details.FullName;
            }
            catch
            {
                LoginStatusText.Text = "Database connection error.";
                LoginStatusText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 248, 113, 113));
                LoginLoadingRing.IsActive = false;
                LoginLoadingRing.Visibility = Visibility.Collapsed;
                _isAuthenticating = false;
                return;
            }

            // Fallbacks for prototypes if database is empty
            if (role == null)
            {
                if (uid == "VALID_STAFF_CARD")
                {
                    role = "Security Personnel";
                    fullName = "Simulated Guard";
                }
            }

            if (role == "Administrator")
            {
                AppSession.IsAdmin = true;
                AppSession.IsLoggedIn = true;
                AppSession.CurrentStaffName = fullName ?? "Administrator";
                AppSession.CurrentStaffRoleLabel = "Admin";

                await _database.AddAlertAsync(null, "ADMIN_LOGIN", $"Administrator logged in: {AppSession.CurrentStaffName} (NFC UID: {uid})");

                ApplyRoleBasedAccess();
            }
            else if (role == "Security Personnel")
            {
                AppSession.IsAdmin = false;
                AppSession.IsLoggedIn = true;
                AppSession.CurrentStaffName = fullName ?? "Guard";
                AppSession.CurrentStaffRoleLabel = "Personnel";

                await _database.AddAlertAsync(null, "STAFF_LOGIN", $"Security Personnel logged in: {AppSession.CurrentStaffName} (NFC UID: {uid})");

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
            _ = _database.AddAlertAsync(null, "STAFF_LOGOUT", $"{AppSession.CurrentStaffName} signed out of the system.");

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


        /* =========================================================================
         * WINDOW LIFECYCLE HELPERS
         * ========================================================================= */

        private void MainWindow_Closed(object sender, WindowEventArgs args)
        {
            CloseSerialPort();
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