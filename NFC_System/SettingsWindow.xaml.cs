using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Threading.Tasks;
using Windows.UI;
using WinRT.Interop;

namespace NFC_System
{
    public sealed partial class SettingsWindow : Window
    {
        private readonly DatabaseService _database = new();

        public SettingsWindow()
        {
            this.InitializeComponent();
            ServerIpConfigTextBox.Text = DatabaseService.ServerIp;

            // Fetch the saved database values when window opens
            _ = LoadSettingsAsync();
        }

        private async Task LoadSettingsAsync()
        {
            try
            {
                string strictEntry = await _database.GetSettingAsync("strict_entry_policy", "False");
                StrictEntryToggle.IsOn = (strictEntry == "True");
            }
            catch { }
        }

        private void ResizeWindow(int width, int height)
        {
            IntPtr hWnd = WindowNative.GetWindowHandle(this);
            WindowId windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
            AppWindow appWindow = AppWindow.GetFromWindowId(windowId);
            appWindow.Resize(new Windows.Graphics.SizeInt32(width, height));
        }

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
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
                // Save the local config text file
                DatabaseService.SaveConfig(newIp);

                // Save the exact policy setting to the app_settings MySQL table
                await _database.SetSettingAsync("strict_entry_policy", StrictEntryToggle.IsOn.ToString());

                ServerIpStatusText.Text = "Settings saved. Restart the app for network changes to take effect.";
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