using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NFC_System;

public sealed record DiscoveredServer(string ServerId, string Name, string Address)
{
    public string DisplayLabel => $"{Name} | {Address}";
}

internal sealed record ServerDiscoveryPacket(string Product, int Version, string Kind, string Nonce,
    string ServerId = "", string Name = "", int DatabasePort = 3306);

internal static class ServerDiscoveryProtocol
{
    internal const int Port = 43852;
    private const string Product = "NFC_SYSTEM_LAN";

    internal static bool IsLanAddress(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork) return false;
        var b = address.GetAddressBytes();
        return b[0] == 10 || (b[0] == 172 && b[1] is >= 16 and <= 31) ||
            (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254);
    }

    internal static byte[] Query(string nonce) => JsonSerializer.SerializeToUtf8Bytes(new ServerDiscoveryPacket(Product, 1, "query", nonce));

    internal static byte[] Offer(string nonce, string id, string name) =>
        JsonSerializer.SerializeToUtf8Bytes(new ServerDiscoveryPacket(Product, 1, "offer", nonce, id, name));

    private static ServerDiscoveryPacket? Parse(byte[] bytes)
    {
        if (bytes.Length is 0 or > 1024) return null;
        try
        {
            var packet = JsonSerializer.Deserialize<ServerDiscoveryPacket>(bytes);
            return packet is { Product: Product, Version: 1, DatabasePort: 3306 } &&
                Guid.TryParseExact(packet.Nonce, "N", out _) ? packet : null;
        }
        catch (JsonException) { return null; }
    }

    internal static string? ReadQuery(byte[] bytes) => Parse(bytes) is { Kind: "query" } query ? query.Nonce : null;

    internal static DiscoveredServer? ReadOffer(byte[] bytes, string nonce, IPAddress sender, bool allowLoopback = false)
    {
        var packet = Parse(bytes);
        if (packet is not { Kind: "offer" } || packet.Nonce != nonce ||
            !Guid.TryParseExact(packet.ServerId, "N", out _) || string.IsNullOrWhiteSpace(packet.Name) ||
            packet.Name.Length > 100 || packet.Name.Any(char.IsControl) ||
            (!IsLanAddress(sender) && !(allowLoopback && IPAddress.IsLoopback(sender)))) return null;
        // The datagram source, never a payload-supplied address, is the connection candidate.
        return new(packet.ServerId, packet.Name, sender.ToString());
    }
}

internal sealed class ServerDiscoveryResponder : IDisposable
{
    private readonly UdpClient _socket;
    private readonly CancellationTokenSource _stop = new();
    private int _disposed;
    internal Task Completion { get; }
    internal int BoundPort => ((IPEndPoint)_socket.Client.LocalEndPoint!).Port;

    internal ServerDiscoveryResponder(string serverId, string name, int port = ServerDiscoveryProtocol.Port, bool allowLoopback = false)
    {
        if (!Guid.TryParseExact(serverId, "N", out _) || string.IsNullOrWhiteSpace(name) || name.Length > 100 || name.Any(char.IsControl))
            throw new ArgumentException("Invalid discovery server identity.");
        _socket = new UdpClient(AddressFamily.InterNetwork);
        try
        {
            _socket.Client.ExclusiveAddressUse = true;
            _socket.Client.Bind(new IPEndPoint(allowLoopback ? IPAddress.Loopback : IPAddress.Any, port));
        }
        catch { _socket.Dispose(); _stop.Dispose(); throw; }
        Completion = RunAsync(serverId, name, allowLoopback);
    }

    private async Task RunAsync(string id, string name, bool allowLoopback)
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var received = await _socket.ReceiveAsync(_stop.Token);
                if (!ServerDiscoveryProtocol.IsLanAddress(received.RemoteEndPoint.Address) &&
                    !(allowLoopback && IPAddress.IsLoopback(received.RemoteEndPoint.Address))) continue;
                string? nonce = ServerDiscoveryProtocol.ReadQuery(received.Buffer);
                if (nonce == null) continue;
                await _socket.SendAsync(ServerDiscoveryProtocol.Offer(nonce, id, name), received.RemoteEndPoint, _stop.Token);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException) { }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _stop.Cancel();
        _socket.Dispose();
        _ = Completion.ContinueWith(_ => _stop.Dispose(), TaskScheduler.Default);
    }
}

public static class ServerDiscovery
{
    public static Task<IReadOnlyList<DiscoveredServer>> FindAsync(CancellationToken cancellationToken = default) =>
        FindAsync(GetBroadcastEndpoints(), TimeSpan.FromSeconds(4), cancellationToken);

    internal static IReadOnlyList<IPEndPoint> GetBroadcastEndpoints()
    {
        var addresses = new HashSet<IPAddress> { IPAddress.Broadcast };
        foreach (var network in NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up &&
            n.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211))
        {
            foreach (var unicast in network.GetIPProperties().UnicastAddresses.Where(u => ServerDiscoveryProtocol.IsLanAddress(u.Address)))
            {
                byte[] address = unicast.Address.GetAddressBytes(), mask = unicast.IPv4Mask.GetAddressBytes();
                addresses.Add(new IPAddress(address.Select((value, i) => (byte)(value | ~mask[i])).ToArray()));
            }
        }
        return addresses.Select(a => new IPEndPoint(a, ServerDiscoveryProtocol.Port)).ToArray();
    }

    internal static async Task<IReadOnlyList<DiscoveredServer>> FindAsync(IReadOnlyList<IPEndPoint> endpoints,
        TimeSpan timeout, CancellationToken cancellationToken = default, bool allowLoopback = false)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        stop.CancelAfter(timeout);
        using var socket = new UdpClient(new IPEndPoint(IPAddress.Any, 0)) { EnableBroadcast = true };
        string nonce = Guid.NewGuid().ToString("N");
        byte[] query = ServerDiscoveryProtocol.Query(nonce);
        var found = new Dictionary<string, DiscoveredServer>(StringComparer.Ordinal);
        async Task SendAsync()
        {
            for (int round = 0; round < 3; round++)
            {
                foreach (var endpoint in endpoints)
                {
                    try { await socket.SendAsync(query, endpoint, stop.Token); }
                    catch (SocketException) { }
                }
                await Task.Delay(500, stop.Token);
            }
        }
        var sending = SendAsync();
        try
        {
            while (found.Count < 64)
            {
                var response = await socket.ReceiveAsync(stop.Token);
                var server = ServerDiscoveryProtocol.ReadOffer(response.Buffer, nonce, response.RemoteEndPoint.Address, allowLoopback);
                if (server != null) found.TryAdd(server.ServerId + ":" + server.Address, server);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
        finally
        {
            stop.Cancel();
            try { await sending; } catch (OperationCanceledException) { }
        }
        cancellationToken.ThrowIfCancellationRequested();
        return found.Values.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ThenBy(s => s.Address, StringComparer.Ordinal).ToArray();
    }
}
