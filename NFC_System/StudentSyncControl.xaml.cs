using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Threading.Tasks;

namespace NFC_System;

public sealed partial class StudentSyncControl : UserControl
{
    public bool IsBusy { get; private set; }
    public StudentSyncControl()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            StudentSyncService.StatusChanged += RefreshStatus;
            bool adminDevice = StudentSyncService.IsAdministration;
            Visibility = DeviceIdentity.CanRename && DatabaseService.ActiveConnection.Role is
                (ServerConnectionRole.MainServer or ServerConnectionRole.Administration) ? Visibility.Visible : Visibility.Collapsed;
            AdministrationActions.Visibility = adminDevice ? Visibility.Visible : Visibility.Collapsed;
            ServerIdBox.IsReadOnly = !adminDevice;
            try
            {
                var options = StudentSyncOptions.Load();
                ServerIdBox.Text = adminDevice ? options?.ServerId ?? "" : DatabaseService.ActiveConnection.ServerId;
                EmailBox.Text = options?.Email ?? "";
                RefreshStatus();
            }
            catch (Exception ex) { SyncStatus.Text = ex.Message; }
        };
        Unloaded += (_, _) => { StudentSyncService.StatusChanged -= RefreshStatus; PasswordBox.Password = ""; };
    }

    private void RefreshStatus() => DispatcherQueue.TryEnqueue(() => SyncStatus.Text = StudentSyncService.Status);
    private async Task RunAsync(Func<Task> action)
    {
        if (IsBusy) return;
        IsBusy = true;
        ConnectButton.IsEnabled = DisconnectButton.IsEnabled = CheckButton.IsEnabled = UploadButton.IsEnabled = DownloadButton.IsEnabled = false;
        ServerIdBox.IsEnabled = EmailBox.IsEnabled = PasswordBox.IsEnabled = ReplaceLocalBox.IsEnabled = false;
        BusyRing.Visibility = Visibility.Visible;
        BusyRing.IsActive = true;
        try { await action(); SyncStatus.Text = StudentSyncService.Status; }
        catch (Exception ex) { SyncStatus.Text = ex.Message; }
        finally
        {
            PasswordBox.Password = "";
            IsBusy = false;
            ConnectButton.IsEnabled = DisconnectButton.IsEnabled = CheckButton.IsEnabled = UploadButton.IsEnabled = DownloadButton.IsEnabled = DeviceIdentity.CanRename;
            ServerIdBox.IsEnabled = EmailBox.IsEnabled = PasswordBox.IsEnabled = ReplaceLocalBox.IsEnabled = true;
            BusyRing.IsActive = false;
            BusyRing.Visibility = Visibility.Collapsed;
        }
    }
    private async void Connect_Click(object sender, RoutedEventArgs e) => await RunAsync(() => StudentSyncService.SignInAsync(ServerIdBox.Text, EmailBox.Text, PasswordBox.Password));
    private async void Disconnect_Click(object sender, RoutedEventArgs e) => await RunAsync(StudentSyncService.SignOutAsync);
    private async void Check_Click(object sender, RoutedEventArgs e) => await RunAsync(StudentSyncService.CheckNowAsync);
    private async void Upload_Click(object sender, RoutedEventArgs e) => await RunAsync(async () => { await StudentSyncService.UploadAsync(); });
    private async void Download_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        await StudentSyncService.DownloadAsync(ReplaceLocalBox.IsChecked == true);
        ReplaceLocalBox.IsChecked = false;
    });
}
