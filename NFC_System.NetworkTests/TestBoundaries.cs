namespace NFC_System;

public sealed partial class DatabaseService
{
    public static string ServerIp { get; private set; } = "127.0.0.1";
    private static string NetworkConfigurationDirectory => AppContext.BaseDirectory;
    public static string ConnectionString => $"Server={ServerIp};Port=23306;Database=nfc_network_tests;User ID=root;Password=attendance-test-only;ConnectionTimeout=2";
}

public sealed class TestQueueSnapshot
{
    public string DeviceId { get; } = "00000000000000000000000000000001";
    public string DeviceName { get; } = "Network test device";
    public List<object> GateLogs { get; } = new();
    public List<object> EventLogs { get; } = new();
    public List<object> DeviceNameChanges { get; } = new();
    public List<object> Pending { get; } = new();
}

public sealed class TestQueue
{
    public TestQueueSnapshot State { get; } = new();
    public TestQueueSnapshot Snapshot() => State;
}

public static class OfflineCacheService
{
    public static bool ServerCacheReady { get; private set; } = true;
    public static void SetServerCacheReady(bool ready) => ServerCacheReady = ready;
    public static TestQueue AttendanceQueue { get; } = new();
    public static TestQueue PinJournal { get; } = new();
}
