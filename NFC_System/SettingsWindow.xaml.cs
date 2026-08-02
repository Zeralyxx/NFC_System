using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using Windows.UI;
using WinRT.Interop;

namespace NFC_System
{
    public sealed partial class SettingsWindow : Window
    {
        public SettingsWindow()
        {
            this.InitializeComponent();
            

            ServerIpConfigTextBox.Text = DatabaseService.ServerIp;
        }

        private void ResizeWindow(int width, int height)
        {
            IntPtr hWnd = WindowNative.GetWindowHandle(this);
            WindowId windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
            AppWindow appWindow = AppWindow.GetFromWindowId(windowId);
            appWindow.Resize(new Windows.Graphics.SizeInt32(width, height));
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            string newIp = ServerIpConfigTextBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(newIp))
            {
                ServerIpStatusText.Text = "Please enter a valid IP address.";
                ServerIpStatusText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Color.FromArgb(255, 248, 113, 113));
                return;
            }

            try
            {
                DatabaseService.SaveConfig(newIp);
                ServerIpStatusText.Text = "Server IP saved. Restart the app for changes to take effect.";
                ServerIpStatusText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Color.FromArgb(255, 52, 211, 153));
            }
            catch (System.Exception ex)
            {
                ServerIpStatusText.Text = $"Failed to save: {ex.Message}";
                ServerIpStatusText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Color.FromArgb(255, 248, 113, 113));
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}