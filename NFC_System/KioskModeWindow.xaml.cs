using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using System;
using WinRT.Interop;

namespace NFC_System
{
    public sealed partial class KioskModeWindow : Window
    {
        private string _originatingMode; // Remembers "Gate" or "Event" for navigation on exit
        private string _contextDetails;

        // Dynamic Constructor
        public KioskModeWindow(string originatingMode, string contextDetails)
        {
            this.InitializeComponent();
            _originatingMode = originatingMode;
            _contextDetails = contextDetails;

            EnforceFullScreenMode();
            ApplyDynamicHeader();
        }

        private void EnforceFullScreenMode()
        {
            IntPtr hWnd = WindowNative.GetWindowHandle(this);
            WindowId windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
            AppWindow appWindow = AppWindow.GetFromWindowId(windowId);

            if (appWindow != null)
            {
                appWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
            }
        }

        private void ApplyDynamicHeader()
        {
            // Changes header text dynamically depending on how it was launched
            KioskHeaderSubtitle.Text = $"{_originatingMode.ToUpper()} TERMINAL  •  {_contextDetails.ToUpper()}";
        }

        private void ExitKiosk_Click(object sender, RoutedEventArgs e)
        {
            // Return the guard back to the screen they launched from
            if (_originatingMode == "Event")
            {
                var eventWindow = new EventAttendanceWindow();
                eventWindow.Activate();
            }
            else
            {
                var gateWindow = new VerificationWindow();
                gateWindow.Activate();
            }
            this.Close();
        }

        // ==========================================
        // UI STATE MACHINE (Simulations)
        // ==========================================
        private void SimulateIdle_Click(object sender, RoutedEventArgs e)
        {
            StatusBackgroundBorder.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(26, 59, 130, 246));
            StatusBackgroundBorder.BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(48, 59, 130, 246));
            StatusHeadlineText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 96, 165, 250));
            StatusHeadlineText.Text = "READY TO SCAN";
            StatusSubheadlineText.Text = "Please tap NFC ID on the reader";
            StatusIcon.Glyph = "\uE72A";
            StatusIcon.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 59, 130, 246));
            ResetProfileData();
        }

        private void SimulatePass_Click(object sender, RoutedEventArgs e)
        {
            StatusBackgroundBorder.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(26, 16, 185, 129));
            StatusBackgroundBorder.BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(48, 16, 185, 129));
            StatusHeadlineText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 52, 211, 153));
            StatusHeadlineText.Text = "ACCESS GRANTED";
            StatusSubheadlineText.Text = "Proceed through the gate";
            StatusIcon.Glyph = "\uE73E";
            StatusIcon.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 16, 185, 129));
            LoadProfileData("Justin Mason", "26-00001");
        }

        private void SimulateFail_Click(object sender, RoutedEventArgs e)
        {
            StatusBackgroundBorder.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(26, 248, 113, 113));
            StatusBackgroundBorder.BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(48, 248, 113, 113));
            StatusHeadlineText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 248, 113, 113));
            StatusHeadlineText.Text = "ACCESS DENIED";
            StatusSubheadlineText.Text = "Unregistered Credential / See Guard";
            StatusIcon.Glyph = "\uEA39";
            StatusIcon.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 248, 113, 113));
            LoadProfileData("UNKNOWN USER", "04:A1:B2:C3");
            StudentIdText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 248, 113, 113));
        }

        private void LoadProfileData(string name, string id)
        {
            ProfileBorder.Opacity = 1.0;
            StudentNameText.Text = name;
            StudentIdText.Text = id;
            StudentNameText.Foreground = new SolidColorBrush(Microsoft.UI.Colors.White);
            StudentIdText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 160, 160, 160));
        }

        private void ResetProfileData()
        {
            ProfileBorder.Opacity = 0.3;
            StudentNameText.Text = "---";
            StudentIdText.Text = "---";
        }
    }
}