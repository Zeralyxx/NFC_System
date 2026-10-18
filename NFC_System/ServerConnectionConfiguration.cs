using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace NFC_System;

public enum ServerConnectionRole { NotConfigured, MainServer, Client }

public sealed record ServerConnectionConfiguration(ServerConnectionRole Role, string Host, string ServerId = "", int Version = 1,
    bool CacheRefreshRequired = false)
{
    public static ServerConnectionConfiguration Default => new(ServerConnectionRole.NotConfigured, "127.0.0.1");

    public ServerConnectionConfiguration Validate()
    {
        if (Version != 1 || !Enum.IsDefined(Role) || Role == ServerConnectionRole.NotConfigured)
            throw new InvalidDataException("Choose Main Server or Connect to Main Server.");
        if (!IPAddress.TryParse(Host.Trim(), out var address) || address.AddressFamily != AddressFamily.InterNetwork ||
            address.Equals(IPAddress.Any) || address.Equals(IPAddress.Broadcast) || address.GetAddressBytes()[0] >= 224)
            throw new InvalidDataException("Enter a valid IPv4 server address.");
        if (Role == ServerConnectionRole.MainServer && !IPAddress.IsLoopback(address))
            throw new InvalidDataException("The Main Server role must use its own local database (127.0.0.1).");
        if (Role == ServerConnectionRole.Client && IPAddress.IsLoopback(address))
            throw new InvalidDataException("A connecting device must use the main server's network address, not 127.0.0.1.");
        if (ServerId.Length > 0 && !Guid.TryParseExact(ServerId, "N", out _))
            throw new InvalidDataException("The saved server identity is invalid. Detect the server again.");
        return this with { Host = address.ToString() };
    }

    public bool TargetsSameDatabase(ServerConnectionConfiguration other) =>
        ServerId.Length > 0
            ? other.ServerId.Length > 0 && string.Equals(ServerId, other.ServerId, StringComparison.Ordinal)
            : string.Equals(Host, other.Host, StringComparison.OrdinalIgnoreCase);
}

internal sealed class ServerConnectionStore(string directory, string? legacyDirectory = null)
{
    private string PathName => Path.Combine(directory, "network_config.json");

    internal ServerConnectionConfiguration Load()
    {
        string savedPath = PathName;
        if (!File.Exists(savedPath) && legacyDirectory != null)
            savedPath = Path.Combine(legacyDirectory, "network_config.json");
        if (File.Exists(savedPath))
        {
            var saved = (JsonSerializer.Deserialize<ServerConnectionConfiguration>(File.ReadAllText(savedPath))
                ?? throw new InvalidDataException("Connection settings are empty.")).Validate();
            if (saved.Role == ServerConnectionRole.Client && saved.ServerId.Length == 0)
                throw new InvalidDataException("The saved client connection has no verified server identity.");
            return saved;
        }
        string legacy = Path.Combine(legacyDirectory ?? directory, "db_config.txt");
        return ServerConnectionConfiguration.Default with { Host = File.Exists(legacy) ? File.ReadAllText(legacy).Trim() : "127.0.0.1" };
    }

    internal void Save(ServerConnectionConfiguration configuration)
    {
        var validated = configuration.Validate();
        if (validated.Role == ServerConnectionRole.Client && validated.ServerId.Length == 0)
            throw new InvalidDataException("Verify the main database before saving its connection.");
        Directory.CreateDirectory(directory);
        string temporary = PathName + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(validated));
            File.Move(temporary, PathName, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
