using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;

namespace NFC_System;

public sealed partial class MainWindow
{
    private long _deviceSettingsSessionVersion;
    private bool _savingDeviceName;

    private void LoadSettingsDeviceIdentity()
    {
        _deviceSettingsSessionVersion = AppSession.LoginVersion;
        SettingsDeviceNameStatus.IsOpen = false;
        SettingsDeviceNameBox.IsReadOnly = !DeviceIdentity.CanRename;
        SettingsSaveDeviceNameButton.IsEnabled = DeviceIdentity.CanRename;
        SettingsDeviceIdBox.Text = "";
        SettingsDeviceNameBox.Text = "";
        try
        {
            var identity = DeviceIdentity.CaptureCurrent();
            SettingsDeviceIdBox.Text = identity.DeviceId;
            SettingsDeviceNameBox.Text = identity.DeviceName;
        }
        catch (Exception ex)
        {
            SettingsSaveDeviceNameButton.IsEnabled = false;
            SettingsDeviceNameBox.IsReadOnly = true;
            SettingsDeviceNameStatus.Severity = InfoBarSeverity.Error;
            SettingsDeviceNameStatus.Title = "Device identity unavailable";
            SettingsDeviceNameStatus.Message = ex.Message;
            SettingsDeviceNameStatus.IsOpen = true;
        }
    }

    private async void SettingsSaveDeviceNameButton_Click(object sender, RoutedEventArgs e)
    {
        if (_savingDeviceName) return;
        _savingDeviceName = true;
        SettingsSaveDeviceNameButton.IsEnabled = false;
        SettingsDeviceNameBox.IsEnabled = false;
        SettingsDialog.IsPrimaryButtonEnabled = false;
        try
        {
            var result = await _database.RenameCurrentDeviceAsync(SettingsDeviceNameBox.Text, _deviceSettingsSessionVersion);
            if (_isClosed) return;
            SettingsDeviceNameBox.Text = result.Identity.DeviceName;
            SettingsDeviceIdBox.Text = result.Identity.DeviceId;
            SettingsDeviceNameStatus.Severity = result.AuditPending ? InfoBarSeverity.Warning : InfoBarSeverity.Success;
            SettingsDeviceNameStatus.Title = result.Changed ? "Device name saved" : "Device name unchanged";
            SettingsDeviceNameStatus.Message = result.AuditPending ? "Audit upload pending database connection." : "";
        }
        catch (Exception ex)
        {
            if (_isClosed) return;
            SettingsDeviceNameStatus.Severity = InfoBarSeverity.Error;
            SettingsDeviceNameStatus.Title = "Device name not saved";
            SettingsDeviceNameStatus.Message = ex.Message;
        }
        finally
        {
            _savingDeviceName = false;
            if (!_isClosed)
            {
                SettingsDeviceNameStatus.IsOpen = true;
                SettingsDeviceNameBox.IsEnabled = true;
                SettingsSaveDeviceNameButton.IsEnabled = DeviceIdentity.CanRename && AppSession.LoginVersion == _deviceSettingsSessionVersion;
                SettingsDialog.IsPrimaryButtonEnabled = true;
            }
        }
    }

    private void SettingsDialog_Closing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        // Keep this dialog alive until the durable rename and its audit attempt finish.
        if (_savingDeviceName || _savingNetwork || SettingsStudentSyncControl.IsBusy) args.Cancel = true;
        if (!args.Cancel) SettingsConnectionControl.CancelDiscovery();
    }
}
