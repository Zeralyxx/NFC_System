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
        private readonly long _loginVersion = AppSession.LoginVersion;

        public SettingsWindow()
        {
            this.InitializeComponent();
            ServerIpConfigTextBox.Text = DatabaseService.ServerIp;
            LoadDeviceIdentity();

            // Fetch the saved database values when window opens
            _ = LoadSettingsAsync();
        }

        private void LoadDeviceIdentity()
        {
            SaveDeviceNameButton.IsEnabled = DeviceIdentity.CanRename;
            DeviceNameTextBox.IsReadOnly = !DeviceIdentity.CanRename;
            try
            {
                var identity = DeviceIdentity.CaptureCurrent();
                DeviceIdTextBox.Text = identity.DeviceId;
                DeviceNameTextBox.Text = identity.DeviceName;
            }
            catch (Exception ex)
            {
                SaveDeviceNameButton.IsEnabled = false;
                DeviceNameStatus.Severity = InfoBarSeverity.Error;
                DeviceNameStatus.Title = "Device identity unavailable";
                DeviceNameStatus.Message = ex.Message;
                DeviceNameStatus.IsOpen = true;
            }
        }

        private async void SaveDeviceNameButton_Click(object sender, RoutedEventArgs e)
        {
            SaveDeviceNameButton.IsEnabled = false;
            DeviceNameTextBox.IsEnabled = false;
            try
            {
                var result = await _database.RenameCurrentDeviceAsync(DeviceNameTextBox.Text, _loginVersion);
                DeviceNameTextBox.Text = result.Identity.DeviceName;
                DeviceIdTextBox.Text = result.Identity.DeviceId;
                DeviceNameStatus.Severity = result.AuditPending ? InfoBarSeverity.Warning : InfoBarSeverity.Success;
                DeviceNameStatus.Title = result.Changed ? "Device name saved" : "Device name unchanged";
                DeviceNameStatus.Message = result.AuditPending ? "Audit upload pending database connection." : "";
            }
            catch (Exception ex)
            {
                DeviceNameStatus.Severity = InfoBarSeverity.Error;
                DeviceNameStatus.Title = "Device name not saved";
                DeviceNameStatus.Message = ex.Message;
            }
            finally
            {
                DeviceNameStatus.IsOpen = true;
                DeviceNameTextBox.IsEnabled = true;
                SaveDeviceNameButton.IsEnabled = DeviceIdentity.CanRename && AppSession.LoginVersion == _loginVersion;
            }
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
