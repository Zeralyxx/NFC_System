using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NFC_System;

public sealed partial class ServerConnectionControl : UserControl
{
    private CancellationTokenSource? _search;
    private string _serverId = "";
    private string _identityHost = "";
    private bool _loading;
    private int _roleIndex = -1;
    public bool IsSearching => _search != null;
    public event Action? StateChanged;
    public Func<CancellationToken, Task<System.Collections.Generic.IReadOnlyList<DiscoveredServer>>> DiscoverServers { get; set; } = ServerDiscovery.FindAsync;

    public ServerConnectionControl()
    {
        InitializeComponent();
        Unloaded += (_, _) => CancelDiscovery();
    }

    public void Load(ServerConnectionConfiguration configuration)
    {
        CancelDiscovery();
        _loading = true;
        _roleIndex = configuration.Role == ServerConnectionRole.MainServer ? 0 : configuration.Role == ServerConnectionRole.Client ? 1 : -1;
        RoleBox.SelectedIndex = _roleIndex;
        _identityHost = configuration.Host;
        HostBox.Text = configuration.Host;
        _serverId = configuration.ServerId;
        ServersBox.ItemsSource = null;
        ServersBox.Visibility = Visibility.Collapsed;
        _loading = false;
        UpdateRole();
    }

    public ServerConnectionConfiguration Configuration => new(RoleBox.SelectedIndex switch
    {
        0 => ServerConnectionRole.MainServer,
        1 => ServerConnectionRole.Client,
        _ => ServerConnectionRole.NotConfigured
    }, HostBox.Text.Trim(), HostBox.Text.Trim() == _identityHost ? _serverId : "");

    public void CancelDiscovery() => _search?.Cancel();

    private async void RoleBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || HostBox == null) return;
        if (_roleIndex == RoleBox.SelectedIndex) return;
        _roleIndex = RoleBox.SelectedIndex;
        CancelDiscovery();
        _serverId = "";
        if (RoleBox.SelectedIndex == 0) HostBox.Text = "127.0.0.1";
        else if (HostBox.Text == "127.0.0.1") HostBox.Text = "";
        UpdateRole();
        if (RoleBox.SelectedIndex == 1) await DetectAsync();
    }

    private void UpdateRole()
    {
        bool client = RoleBox.SelectedIndex == 1;
        HostBox.IsReadOnly = !client;
        DetectButton.IsEnabled = client && !IsSearching;
        ConnectionNote.Text = RoleBox.SelectedIndex == 0
            ? "The main server app must remain open for automatic detection."
            : "";
        StateChanged?.Invoke();
    }

    private void HostBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_loading && HostBox.Text.Trim() != _identityHost) _serverId = "";
        StateChanged?.Invoke();
    }

    private void ServersBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ServersBox.SelectedItem is not DiscoveredServer server) return;
        _loading = true;
        _identityHost = server.Address;
        HostBox.Text = server.Address;
        _serverId = server.ServerId;
        _loading = false;
        ConnectionNote.Text = $"Selected: {server.DisplayLabel}. Confirm the main device before saving.";
        StateChanged?.Invoke();
    }

    private async void DetectButton_Click(object sender, RoutedEventArgs e) => await DetectAsync();
    private void CancelDetectionButton_Click(object sender, RoutedEventArgs e) => CancelDiscovery();

    public async Task DetectAsync()
    {
        if (IsSearching || RoleBox.SelectedIndex != 1) return;
        _serverId = Configuration.ServerId;
        using var search = new CancellationTokenSource();
        _search = search;
        DetectButton.IsEnabled = false;
        RoleBox.IsEnabled = false;
        HostBox.IsEnabled = false;
        CancelDetectionButton.Visibility = Visibility.Visible;
        SearchingRing.Visibility = Visibility.Visible;
        SearchingRing.IsActive = true;
        ConnectionNote.Text = "Searching the local network...";
        StateChanged?.Invoke();
        try
        {
            var discovered = await DiscoverServers(search.Token);
            var servers = (_serverId.Length == 0 ? discovered : discovered.Where(s => s.ServerId == _serverId).ToArray());
            search.Token.ThrowIfCancellationRequested();
            ServersBox.SelectedIndex = -1;
            ServersBox.ItemsSource = servers;
            ServersBox.Visibility = servers.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (servers.Count == 1) ServersBox.SelectedIndex = 0;
            else ConnectionNote.Text = servers.Count > 1 ? "Choose the main server from the detected devices."
                : "No main server found. Open the main server app, check the local network/firewall, then retry or enter its IP address.";
        }
        catch (OperationCanceledException) { ConnectionNote.Text = "Server detection cancelled. The saved connection was not changed."; }
        catch (Exception ex) { ConnectionNote.Text = $"Server detection failed: {ex.Message}"; }
        finally
        {
            _search = null;
            RoleBox.IsEnabled = true;
            HostBox.IsEnabled = true;
            DetectButton.IsEnabled = RoleBox.SelectedIndex == 1;
            CancelDetectionButton.Visibility = Visibility.Collapsed;
            SearchingRing.IsActive = false;
            SearchingRing.Visibility = Visibility.Collapsed;
            StateChanged?.Invoke();
        }
    }
}
