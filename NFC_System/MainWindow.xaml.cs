using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using System;
using WinRT.Interop;

namespace NFC_System
{
    public sealed partial class MainWindow : Window
    {
        public MainWindow()
        {
            this.InitializeComponent();
            MaximizeWindow();
        }

        private void Registration_Click(object sender, RoutedEventArgs e)
        {
            var window = new RegistrationWindow();
            window.Activate();
            this.Close();
        }

        private void Verification_Click(object sender, RoutedEventArgs e)
        {
            var window = new VerificationWindow();
            window.Activate();
            this.Close();
        }

        private void EventAttendance_Click(object sender, RoutedEventArgs e)
        {
            var window = new EventAttendanceWindow();
            window.Activate();
            this.Close();
        }

        private void StudentDirectory_Click(object sender, RoutedEventArgs e)
        {
            var directoryWin = new StudentManagementWindow();
            directoryWin.Activate();
            this.Close();
        }

        private void EventManagement_Click(object sender, RoutedEventArgs e)
        {
            var reportsWin = new EventReportsWindow();
            reportsWin.Activate();
            this.Close();
        }

        private void DashboardSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            var settingsWin = new SettingsWindow();
            settingsWin.Activate();
        }

        private void Settings_Click(object sender, RoutedEventArgs e)
        {
            var window = new SecurityDashboardWindow();
            window.Activate();
            this.Close();
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
