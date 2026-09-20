namespace NFC_System;

// Only the connection, student cache, and unrelated service methods are substituted.
// Queue persistence, SQL transactions, reconciliation and readiness are production code.
public sealed partial class DatabaseService
{
    private static readonly System.Net.Http.HttpClient _httpClient = new();
    private const string FIREBASE_PROJECT_ID = "test-only";
    private const string FIREBASE_API_KEY = "test-only";
    public static bool TestConnectionUnavailable { get; set; }
    public static string ConnectionString => $"Server=127.0.0.1;Port={(TestConnectionUnavailable ? 23307 : 23306)};Database=attendance_tests;User ID=root;Password=attendance-test-only;ConnectionTimeout=2";
    public static DateTime Clock { get; set; } = new(2026, 9, 19, 9, 0, 0);
    public static DateTime GetNetworkAdjustedTime() => Clock;
    public static string ToStorageValue<T>(T value) => value?.ToString() ?? "";
    private static object NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;
    public bool FailCacheRefresh { get; set; }
    private Task<bool> UpdateShadowCacheCoreAsync() { CacheRefreshes++; if (FailCacheRefresh) throw new IOException("Injected refresh failure"); return Task.FromResult(true); }
    public int CacheRefreshes { get; private set; }
    public static Task CreateAttendanceSchemaAsync(MySqlConnector.MySqlConnection connection) => EnsureAttendanceSchemaAsync(connection);
    public static Task CommitForTestAsync(MySqlConnector.MySqlConnection connection, PendingGateLog log) => CommitQueuedRecordAsync(connection, log, null, true);
    public static Task CommitLiveForTestAsync(MySqlConnector.MySqlConnection connection, PendingGateLog log) => CommitQueuedRecordAsync(connection, log, null, false);
    public static Task<AttendanceVisit?> ReadVisitForTestAsync(string student, string transaction) => ReadConfirmedVisitAsync(student, transaction);
    public Task HealForTestAsync(string student, string state) => SetAttendanceStateManuallyAsync(student, state);
}

public static class OfflineCacheService
{
    public static DurableAttendanceQueue AttendanceQueue { get; set; } = null!;
    public static string CachedState { get; set; } = "OUTSIDE";
    public static void UpdateCachedStudentStateLocally(string student, string state) => CachedState = state;
    public static bool FailCacheFlush { get; set; }
    public static int FlushCount { get; set; }
    public static int FailFlushNumber { get; set; }
    public static void FlushStudentCache() { FlushCount++; if (FailCacheFlush || FlushCount == FailFlushNumber) throw new IOException("Injected cache flush failure"); }
    public static bool HasPendingLogs()
    {
        try { var s = AttendanceQueue.Snapshot(); return s.GateLogs.Count + s.EventLogs.Count > 0; }
        catch { return true; }
    }
}
