using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.IO.Ports;
using System.Threading.Tasks;
using WinRT.Interop;

namespace NFC_System
{
    // GLOBAL SESSION STATE
    public static class AppSession
    {
        public static bool IsLoggedIn { get; set; } = false;
        public static bool IsAdmin { get; set; } = false;
    }

    public sealed partial class MainWindow : Window
    {
        private SerialPort? _serialPort;

        public MainWindow()
        {
            this.InitializeComponent();
            MaximizeWindow();

            this.Closed += MainWindow_Closed;

            // If already logged in (e.g., coming back from another window), skip login
            if (AppSession.IsLoggedIn)
            {
                ApplyRoleBasedAccess();
            }
            else
            {
                TryConnectSerial("COM3"); // Listen for login tap
            }
        }

        /* =========================================================================
         * ROLE-BASED ACCESS CONTROL & LOGIN LOGIC
         * ========================================================================= */

        private void TryConnectSerial(string portName)
        {
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

                if (line.StartsWith("UID="))
                {
                    string uid = line.Substring(4).Trim();

                    // Route back to the main UI thread to process the login
                    DispatcherQueue.TryEnqueue(() => ProcessLoginScan(uid));
                }
            }
            catch { }
        }

        private void ProcessLoginScan(string uid)
        {
            LoginStatusText.Text = "Authenticating...";
            LoginLoadingRing.IsActive = true;
            LoginLoadingRing.Visibility = Visibility.Visible;

            // MOCK DATABASE AUTHENTICATION: 
            // In a real setup, you would query "SELECT Role FROM Staff WHERE Uid = @uid"
            // For the prototype, we assume UID "04:A1:B2:C3" is the Admin, and anything else valid is Personnel.

            if (uid == "04:A1:B2:C3")
            {
                AppSession.IsAdmin = true;
                AppSession.IsLoggedIn = true;
                ApplyRoleBasedAccess();
            }
            else if (!string.IsNullOrWhiteSpace(uid) && uid != "00:00:00:00") // Assuming any valid card is normal personnel
            {
                AppSession.IsAdmin = false;
                AppSession.IsLoggedIn = true;
                ApplyRoleBasedAccess();
            }
            else
            {
                LoginStatusText.Text = "Invalid ID. Please try again.";
                LoginStatusText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 248, 113, 113));
                LoginLoadingRing.IsActive = false;
                LoginLoadingRing.Visibility = Visibility.Collapsed;
            }
        }

        private void ApplyRoleBasedAccess()
        {
            // Close the COM port so other windows (like Verification) can use it
            CloseSerialPort();

            // Hide the login screen, show the dashboard
            LoginOverlay.Visibility = Visibility.Collapsed;
            DashboardContent.Visibility = Visibility.Visible;

            if (AppSession.IsAdmin)
            {
                ActiveRoleText.Text = "Administrator";

                // Show all cards
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
                ActiveRoleText.Text = "Security Personnel";

                // Hide restricted cards
                RegistrationCard.Visibility = Visibility.Collapsed;
                StudentDirectoryCard.Visibility = Visibility.Collapsed;
                SecurityAdminCard.Visibility = Visibility.Collapsed;
                EventReportsCard.Visibility = Visibility.Collapsed;
                DashboardSettingsButton.Visibility = Visibility.Collapsed;

                // Center the two remaining cards dynamically
                EventAttendanceCard.SetValue(Grid.ColumnProperty, 0);
                VerificationCard.SetValue(Grid.ColumnProperty, 1);
            }
        }

        private void SignOut_Click(object sender, RoutedEventArgs e)
        {
            AppSession.IsLoggedIn = false;
            AppSession.IsAdmin = false;

            DashboardContent.Visibility = Visibility.Collapsed;
            LoginStatusText.Text = "Please tap your Staff or Admin NFC ID to log in.";
            LoginStatusText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 160, 160, 160));
            LoginOverlay.Visibility = Visibility.Visible;

            TryConnectSerial("COM3"); // Re-open the listener for the next person
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
            try
            {
                if (_serialPort != null && _serialPort.IsOpen)
                {
                    _serialPort.DataReceived -= SerialPort_DataReceived;
                    _serialPort.Close();
                }
                _serialPort?.Dispose();
                _serialPort = null;
            }
            catch { }
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