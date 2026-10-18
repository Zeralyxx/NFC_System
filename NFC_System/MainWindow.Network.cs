using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Threading.Tasks;

namespace NFC_System;

public sealed partial class MainWindow
{
    private bool _networkConfigurationOpen;
    private bool _savingNetwork;

    private void DbConfigDialog_Closing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        if (_savingNetwork) args.Cancel = true;
        else SetupConnectionControl.CancelDiscovery();
    }

    private async Task<bool> ShowConnectionSetupAsync(bool firstSetup, string? error = null)
    {
        _networkConfigurationOpen = true;
        try
        {
            SetupConnectionControl.Load(DatabaseService.ActiveConnection);
            DbConfigDialog.Title = firstSetup ? "Device Connection Setup" : "Server Connection Unavailable";
            DbConfigDialog.PrimaryButtonText = "Connect";
            DbConfigDialog.CloseButtonText = firstSetup ? "Cancel" : "Continue Offline";
            DbErrorTextBlock.Text = error ?? DatabaseService.ConnectionConfigurationError ?? "";
            DbConfigDialog.XamlRoot = Content.XamlRoot;
            var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            DbConfigDialog.Closed += OnClosed;
            void OnClosed(ContentDialog sender, ContentDialogClosedEventArgs args) => closed.TrySetResult();
            try
            {
                var showing = DbConfigDialog.ShowAsync();
                if (DatabaseService.ActiveConnection.Role == ServerConnectionRole.Client)
                    _ = SetupConnectionControl.DetectAsync();
                var result = await showing;
                await closed.Task;
                return result == ContentDialogResult.Primary;
            }
            finally
            {
                SetupConnectionControl.CancelDiscovery();
                DbConfigDialog.Closed -= OnClosed;
            }
        }
        finally { _networkConfigurationOpen = false; }
    }

    private async void DbConfigDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true;
        if (_savingNetwork || SetupConnectionControl.IsSearching) return;
        var deferral = args.GetDeferral();
        _savingNetwork = true;
        sender.IsPrimaryButtonEnabled = false;
        SetupConnectionControl.IsEnabled = false;
        try
        {
            long session = AppSession.LoginVersion;
            var validated = await DatabaseService.ValidateClientConnectionAsync(SetupConnectionControl.Configuration);
            DatabaseService.SaveConnectionConfiguration(validated, session, beforeLogin: true);
            args.Cancel = false;
        }
        catch (Exception ex) { DbErrorTextBlock.Text = $"Connection not saved: {ex.Message}"; }
        finally
        {
            _savingNetwork = false;
            SetupConnectionControl.IsEnabled = true;
            sender.IsPrimaryButtonEnabled = true;
            deferral.Complete();
        }
    }
}
