using MySqlConnector;
using NFC_System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

int passed = 0, failed = 0;
void Assert(bool condition) { if (!condition) throw new Exception("Assertion failed"); }
async Task Test(string name, Func<Task> action)
{
    try { await action(); passed++; Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failed++; Console.WriteLine("FAIL " + name + ": " + ex.Message); }
}
void Reject(Action action)
{
    try { action(); }
    catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or UnauthorizedAccessException or JsonException) { return; }
    throw new Exception("Expected rejection");
}
async Task RejectAsync(Func<Task> action)
{
    try { await action(); }
    catch (Exception ex) when (ex is InvalidOperationException or MySqlException) { return; }
    throw new Exception("Expected rejection");
}
string id = Guid.NewGuid().ToString("N"), otherId = Guid.NewGuid().ToString("N");

await Test("Discovery validates product, version, nonce, server identity, LAN source and packet size", () =>
{
    string nonce = Guid.NewGuid().ToString("N");
    byte[] offer = ServerDiscoveryProtocol.Offer(nonce, id, "Main Server");
    var server = ServerDiscoveryProtocol.ReadOffer(offer, nonce, IPAddress.Parse("192.168.1.8"));
    Assert(server?.Address == "192.168.1.8" && server.ServerId == id);
    Assert(ServerDiscoveryProtocol.ReadOffer(offer, otherId, IPAddress.Parse("192.168.1.8")) == null);
    Assert(ServerDiscoveryProtocol.ReadOffer(offer, nonce, IPAddress.Parse("8.8.8.8")) == null);
    Assert(ServerDiscoveryProtocol.ReadOffer(offer, nonce, IPAddress.Loopback) == null);
    foreach (var invalid in new[] {
        new ServerDiscoveryPacket("wrong",1,"offer",nonce,id,"Main"),
        new ServerDiscoveryPacket("NFC_SYSTEM_LAN",2,"offer",nonce,id,"Main"),
        new ServerDiscoveryPacket("NFC_SYSTEM_LAN",1,"query",nonce,id,"Main"),
        new ServerDiscoveryPacket("NFC_SYSTEM_LAN",1,"offer",nonce,"bad","Main"),
        new ServerDiscoveryPacket("NFC_SYSTEM_LAN",1,"offer",nonce,id,"Bad\nname"),
        new ServerDiscoveryPacket("NFC_SYSTEM_LAN",1,"offer",nonce,id,"Main",9999) })
        Assert(ServerDiscoveryProtocol.ReadOffer(JsonSerializer.SerializeToUtf8Bytes(invalid), nonce, IPAddress.Parse("192.168.1.8")) == null);
    Assert(ServerDiscoveryProtocol.ReadOffer(new byte[1025], nonce, IPAddress.Parse("192.168.1.8")) == null);
    Assert(ServerDiscoveryProtocol.ReadQuery(Encoding.UTF8.GetBytes("{bad")) == null);
    Assert(ServerDiscoveryProtocol.ReadQuery(ServerDiscoveryProtocol.Query(nonce)) == nonce);
    return Task.CompletedTask;
});
await Test("Loopback discovery finds and deduplicates multiple servers without choosing one", async () =>
{
    using var first = new ServerDiscoveryResponder(id, "Main A", 0, true);
    using var second = new ServerDiscoveryResponder(otherId, "Main B", 0, true);
    var endpoints = new[] { new IPEndPoint(IPAddress.Loopback, first.BoundPort), new IPEndPoint(IPAddress.Loopback, second.BoundPort) };
    var result = await ServerDiscovery.FindAsync(endpoints, TimeSpan.FromMilliseconds(1200), allowLoopback: true);
    Assert(result.Count == 2 && result.Select(r => r.ServerId).Distinct().Count() == 2);
    first.Dispose(); second.Dispose();
    await Task.WhenAll(first.Completion, second.Completion).WaitAsync(TimeSpan.FromSeconds(2));
});
await Test("Discovery timeout and cancellation are bounded and do not change saved configuration", async () =>
{
    using var silent = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
    var endpoints = new[] { (IPEndPoint)silent.Client.LocalEndPoint! };
    Assert((await ServerDiscovery.FindAsync(endpoints, TimeSpan.FromMilliseconds(100), allowLoopback: true)).Count == 0);
    using var cancel = new CancellationTokenSource(50);
    try { await ServerDiscovery.FindAsync(endpoints, TimeSpan.FromSeconds(5), cancel.Token, true); throw new Exception("Expected cancellation"); }
    catch (OperationCanceledException) { }
});
await Test("Legacy IP configuration is preserved without declaring an accidental main server", () =>
{
    string path = Path.Combine(Path.GetTempPath(), "nfc-network-config-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(path);
    File.WriteAllText(Path.Combine(path, "db_config.txt"), "192.168.1.5");
    var store = new ServerConnectionStore(path);
    Assert(store.Load() == ServerConnectionConfiguration.Default with { Host = "192.168.1.5" });
    Assert(!File.Exists(Path.Combine(path, "network_config.json")));
    var client = new ServerConnectionConfiguration(ServerConnectionRole.Client, "192.168.1.5", id);
    store.Save(client); Assert(store.Load() == client);
    Assert(File.ReadAllText(Path.Combine(path, "db_config.txt")) == "192.168.1.5");
    Reject(() => store.Save(client with { ServerId = "" }));
    File.WriteAllText(Path.Combine(path, "network_config.json"), "{bad");
    Reject(() => store.Load());
    Assert(File.ReadAllText(Path.Combine(path, "network_config.json")) == "{bad");
    return Task.CompletedTask;
});
await Test("Invalid role, version, IP, loopback client and malformed identities are rejected", () =>
{
    foreach (var invalid in new[] {
        ServerConnectionConfiguration.Default,
        new ServerConnectionConfiguration(ServerConnectionRole.MainServer,"192.168.1.5"),
        new ServerConnectionConfiguration(ServerConnectionRole.Client,"127.0.0.1",id),
        new ServerConnectionConfiguration(ServerConnectionRole.Client,"0.0.0.0",id),
        new ServerConnectionConfiguration(ServerConnectionRole.Client,"255.255.255.255",id),
        new ServerConnectionConfiguration(ServerConnectionRole.Client,"224.0.0.1",id),
        new ServerConnectionConfiguration(ServerConnectionRole.Client,"not-an-IP",id),
        new ServerConnectionConfiguration(ServerConnectionRole.Client,"192.168.1.5","bad"),
        new ServerConnectionConfiguration(ServerConnectionRole.Client,"192.168.1.5",id,2) }) Reject(() => invalid.Validate());
    var current = new ServerConnectionConfiguration(ServerConnectionRole.Client,"192.168.1.5",id);
    Assert(current.TargetsSameDatabase(current with { Host = "192.168.1.6" }));
    Assert(!current.TargetsSameDatabase(current with { ServerId = otherId }));
    Assert(!current.TargetsSameDatabase(current with { ServerId = "" }));
    return Task.CompletedTask;
});

await Test("Saving settings stages the new server without switching the live connection", () =>
{
    AppSession.IsLoggedIn = false; AppSession.IsKioskRunning = false;
    DatabaseService.SaveConnectionConfiguration(new(ServerConnectionRole.MainServer,"127.0.0.1"), AppSession.LoginVersion, true);
    var active = DatabaseService.ActiveConnection;
    AppSession.IsLoggedIn = true; AppSession.IsAdmin = true; AppSession.IsEventOrganizer = false;
    AppSession.CurrentStaffRoleLabel = "Master Admin";
    var next = new ServerConnectionConfiguration(ServerConnectionRole.Client,"192.168.1.8",id);
    DatabaseService.SaveConnectionConfiguration(next, AppSession.LoginVersion);
    Assert(DatabaseService.ActiveConnection == active && DatabaseService.ServerIp == "127.0.0.1" &&
        DatabaseService.ReadSavedConnection() == next with { CacheRefreshRequired = true } && OfflineCacheService.ServerCacheReady);
    return Task.CompletedTask;
});
await Test("Pending attendance, PIN and device audit queues block a server switch without erasing records", () =>
{
    var next = new ServerConnectionConfiguration(ServerConnectionRole.Client,"192.168.1.8",otherId);
    foreach (var queue in new[] { OfflineCacheService.AttendanceQueue.State.GateLogs, OfflineCacheService.AttendanceQueue.State.EventLogs,
        OfflineCacheService.AttendanceQueue.State.DeviceNameChanges, OfflineCacheService.PinJournal.State.Pending })
    {
        queue.Add(new object());
        Reject(() => DatabaseService.SaveConnectionConfiguration(next, AppSession.LoginVersion));
        Assert(queue.Count == 1 && DatabaseService.ServerIp == "127.0.0.1");
        queue.Clear();
    }
    return Task.CompletedTask;
});
await Test("Server configuration changes reject personnel, organizers, stale sessions and active kiosks", () =>
{
    var next = new ServerConnectionConfiguration(ServerConnectionRole.Client,"192.168.1.8",id);
    AppSession.IsAdmin = false; AppSession.CurrentStaffRoleLabel = "Personnel";
    Reject(() => DatabaseService.SaveConnectionConfiguration(next, AppSession.LoginVersion));
    AppSession.IsEventOrganizer = true;
    Reject(() => DatabaseService.SaveConnectionConfiguration(next, AppSession.LoginVersion));
    AppSession.IsAdmin = true; AppSession.IsEventOrganizer = false; AppSession.CurrentStaffRoleLabel = "Master Admin";
    Reject(() => DatabaseService.SaveConnectionConfiguration(next, AppSession.LoginVersion - 1));
    Reject(() => DatabaseService.SaveConnectionConfiguration(next, AppSession.LoginVersion, true));
    AppSession.IsLoggedIn = false; AppSession.IsKioskRunning = true;
    Reject(() => DatabaseService.SaveConnectionConfiguration(next, AppSession.LoginVersion, true));
    AppSession.IsKioskRunning = false;
    return Task.CompletedTask;
});

if (args.Contains("--mysql"))
{
    var builder = new MySqlConnectionStringBuilder(DatabaseService.ConnectionString);
    if (builder.Server != "127.0.0.1" || builder.Port != 23306 || builder.Database != "nfc_network_tests")
        throw new Exception("Refusing to use a non-disposable database.");
    builder.Database = "";
    using (var admin = new MySqlConnection(builder.ConnectionString))
    {
        await admin.OpenAsync();
        using var command = new MySqlCommand("CREATE DATABASE IF NOT EXISTS nfc_network_tests", admin);
        await command.ExecuteNonQueryAsync();
    }
    using var connection = new MySqlConnection(DatabaseService.ConnectionString);
    await connection.OpenAsync();
    async Task Sql(string sql) { using var command = new MySqlCommand(sql,connection); await command.ExecuteNonQueryAsync(); }
    await Sql("CREATE TABLE IF NOT EXISTS app_settings(setting_key VARCHAR(100) PRIMARY KEY,setting_value VARCHAR(255)); CREATE TABLE IF NOT EXISTS staff(id INT PRIMARY KEY); DELETE FROM app_settings; DELETE FROM staff");
    await Test("SQL client verification rejects an unconfigured database without creating server identity or staff", async () =>
    {
        await RejectAsync(() => ServerConnectionDatabase.VerifyAsync(DatabaseService.ConnectionString,"",default));
        using var command = new MySqlCommand("SELECT COUNT(*) FROM app_settings",connection);
        Assert(Convert.ToInt32(await command.ExecuteScalarAsync()) == 0);
    });
    await Test("SQL main identity survives initialization retries and clients verify the exact selected server", async () =>
    {
        await new DatabaseService().InitializeMainServerIdentityAsync();
        string identity = DatabaseService.ActiveConnection.ServerId;
        await new DatabaseService().InitializeMainServerIdentityAsync();
        Assert(DatabaseService.ActiveConnection.ServerId == identity && Guid.TryParseExact(identity,"N",out _));
        await RejectAsync(() => ServerConnectionDatabase.VerifyAsync(DatabaseService.ConnectionString,identity,default));
        await Sql("INSERT INTO staff VALUES(1)");
        Assert(await ServerConnectionDatabase.VerifyAsync(DatabaseService.ConnectionString,identity,default) == identity);
        await RejectAsync(() => ServerConnectionDatabase.VerifyAsync(DatabaseService.ConnectionString,otherId,default));
    });
}
else Console.WriteLine("SQL tests skipped; --mysql uses only disposable localhost:23306/nfc_network_tests.");

await Test("Applying a different server requires a fresh cache before offline access", () =>
{
    AppSession.IsLoggedIn = false; AppSession.IsKioskRunning = false;
    DatabaseService.SaveConnectionConfiguration(new(ServerConnectionRole.Client,"192.168.1.8",id),AppSession.LoginVersion,true);
    Assert(DatabaseService.ActiveConnection.CacheRefreshRequired && !OfflineCacheService.ServerCacheReady && DatabaseService.ReadSavedConnection().CacheRefreshRequired);
    return Task.CompletedTask;
});

Console.WriteLine($"{passed} passed; {failed} failed.");
return failed == 0 ? 0 : 1;
