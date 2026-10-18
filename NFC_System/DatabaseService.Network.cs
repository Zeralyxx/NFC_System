using MySqlConnector;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace NFC_System;

public sealed partial class DatabaseService
{
    private static readonly ServerConnectionStore ConnectionStore = new(NetworkConfigurationDirectory, AppContext.BaseDirectory);
    private static ServerDiscoveryResponder? _discoveryResponder;
    public static ServerConnectionConfiguration ActiveConnection { get; private set; } = ServerConnectionConfiguration.Default;
    public static string? ConnectionConfigurationError { get; private set; }
    public static string? DiscoveryError { get; private set; }

    public static ServerConnectionConfiguration ReadSavedConnection() => ConnectionStore.Load();

    public static void RequireConnectionConfigurationPermission(long sessionVersion)
    {
        if (!DeviceIdentity.CanRename || AppSession.LoginVersion != sessionVersion)
            throw new UnauthorizedAccessException("Sign in as an Administrator or Master Admin to change this device's server connection.");
    }

    public static void SaveConnectionConfiguration(ServerConnectionConfiguration configuration, long sessionVersion, bool beforeLogin = false)
    {
        var validated = configuration.Validate();
        if (beforeLogin)
        {
            if (AppSession.IsLoggedIn || AppSession.IsKioskRunning || AppSession.LoginVersion != sessionVersion)
                throw new UnauthorizedAccessException("Restart before changing the initial server connection.");
        }
        else RequireConnectionConfigurationPermission(sessionVersion);

        if (!ActiveConnection.TargetsSameDatabase(validated))
        {
            var queue = OfflineCacheService.AttendanceQueue.Snapshot();
            if (queue.GateLogs.Count > 0 || queue.EventLogs.Count > 0 || queue.DeviceNameChanges.Count > 0 ||
                OfflineCacheService.PinJournal.Snapshot().Pending.Count > 0)
                throw new InvalidOperationException("This device has pending attendance, PIN or audit records. Reconnect to its current database and synchronize them before changing servers. Nothing was deleted or moved.");
        }
        validated = validated with { CacheRefreshRequired = ActiveConnection.CacheRefreshRequired || !ActiveConnection.TargetsSameDatabase(validated) ||
            (ActiveConnection.Role == ServerConnectionRole.NotConfigured && validated.Role == ServerConnectionRole.Client) };
        ConnectionStore.Save(validated);
        if (beforeLogin)
        {
            ActiveConnection = validated;
            ServerIp = validated.Host;
            ConnectionConfigurationError = null;
            OfflineCacheService.SetServerCacheReady(!validated.CacheRefreshRequired);
        }
    }

    public static async Task<ServerConnectionConfiguration> ValidateClientConnectionAsync(ServerConnectionConfiguration configuration)
    {
        var validated = configuration.Validate();
        if (validated.Role != ServerConnectionRole.Client) return validated;
        var builder = new MySqlConnectionStringBuilder(ConnectionString) { Server = validated.Host, Pooling = false };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(6));
        string id = await ServerConnectionDatabase.VerifyAsync(builder.ConnectionString, validated.ServerId, timeout.Token);
        return validated with { ServerId = id };
    }

    private static async Task<string> ReadServerIdentityAsync(MySqlConnection connection, CancellationToken token = default)
    {
        using var command = new MySqlCommand("SELECT setting_value FROM app_settings WHERE setting_key='lan_server_id'", connection) { CommandTimeout = 3 };
        return Convert.ToString(await command.ExecuteScalarAsync(token)) ?? "";
    }

    public async Task InitializeMainServerIdentityAsync()
    {
        if (ActiveConnection.Role != ServerConnectionRole.MainServer) return;
        using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();
        using var command = new MySqlCommand("INSERT IGNORE INTO app_settings(setting_key,setting_value) VALUES('lan_server_id',@id)", connection);
        command.Parameters.AddWithValue("@id", Guid.NewGuid().ToString("N"));
        await command.ExecuteNonQueryAsync();
        string id = await ReadServerIdentityAsync(connection);
        if (!Guid.TryParseExact(id, "N", out _)) throw new InvalidDataException("The main database has an invalid server identity.");
        if (ActiveConnection.ServerId.Length > 0 && ActiveConnection.ServerId != id)
            throw new InvalidOperationException("The main database identity changed. Review its configuration before resuming operation.");
        ActiveConnection = ActiveConnection with { ServerId = id };
        ConnectionStore.Save(ActiveConnection);
    }

    public async Task StartServerDiscoveryAsync()
    {
        if (ActiveConnection.Role != ServerConnectionRole.MainServer || _discoveryResponder != null) return;
        try
        {
            using var connection = new MySqlConnection(ConnectionString);
            await connection.OpenAsync();
            using var command = new MySqlCommand("SELECT COUNT(*) FROM staff", connection);
            if (Convert.ToInt32(await command.ExecuteScalarAsync()) == 0) return;
            string id = await ReadServerIdentityAsync(connection);
            if (id != ActiveConnection.ServerId) throw new InvalidOperationException("The server identity does not match this configuration.");
            _discoveryResponder = new ServerDiscoveryResponder(id, Environment.MachineName.Length > 100 ? Environment.MachineName[..100] : Environment.MachineName);
            DiscoveryError = null;
        }
        catch (Exception ex) { DiscoveryError = ex.Message; }
    }

    public static void StopServerDiscovery() => _discoveryResponder?.Dispose();

    private static void CompleteServerCacheRefresh()
    {
        if (!ActiveConnection.CacheRefreshRequired) return;
        var previous = ActiveConnection;
        var completed = previous with { CacheRefreshRequired = false };
        // Do not overwrite a different connection staged in Settings for the next restart.
        if (ConnectionStore.Load() == previous) ConnectionStore.Save(completed);
        ActiveConnection = completed;
        OfflineCacheService.SetServerCacheReady(true);
    }
}
