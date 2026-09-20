using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

namespace NFC_System;

public sealed class AttendanceQueueSnapshot
{
    [JsonRequired] public string DeviceId { get; set; } = Guid.NewGuid().ToString("N");
    [JsonRequired] public long LastSequence { get; set; }
    [JsonRequired] public bool LegacyImported { get; set; }
    public bool CacheRefreshRequired { get; set; } = true;
    public DateTime? LastCacheRefreshUtc { get; set; }
    [JsonRequired] public List<PendingGateLog> GateLogs { get; set; } = new();
    [JsonRequired] public List<PendingEventAttendance> EventLogs { get; set; } = new();
}

// A single atomic file owns device identity, sequence allocation, and unacknowledged work.
public sealed class DurableAttendanceQueue
{
    private readonly string _directory;
    private readonly string _path;
    public DurableAttendanceQueue(string directory)
    {
        _directory = directory;
        _path = Path.Combine(directory, "attendance_outbox.json");
    }

    public AttendanceQueueSnapshot Snapshot() => Access(state => state);

    public PendingGateLog Enqueue(PendingGateLog log) => Access(state =>
    {
        log.TransactionId = Guid.NewGuid().ToString("N");
        log.DeviceId = state.DeviceId;
        log.DeviceSequence = ++state.LastSequence;
        state.GateLogs.Add(log);
        if (!log.OnlineAttempt) state.CacheRefreshRequired = true;
        return log;
    }, true);

    public PendingEventAttendance Enqueue(PendingEventAttendance log) => Access(state =>
    {
        log.TransactionId = Guid.NewGuid().ToString("N");
        log.DeviceId = state.DeviceId;
        log.DeviceSequence = ++state.LastSequence;
        state.EventLogs.Add(log);
        state.CacheRefreshRequired = true;
        return log;
    }, true);

    public void Acknowledge(string transactionId) => Access(state =>
    {
        state.GateLogs.RemoveAll(x => x.TransactionId == transactionId);
        state.EventLogs.RemoveAll(x => x.TransactionId == transactionId);
        return true;
    }, true);

    public void RequireCacheRefresh() => Access(state => { state.CacheRefreshRequired = true; return true; }, true);

    public bool MarkCacheRefreshed() => Access(state =>
    {
        if (state.GateLogs.Count != 0 || state.EventLogs.Count != 0) return false;
        state.CacheRefreshRequired = false;
        state.LastCacheRefreshUtc = DateTime.UtcNow;
        return true;
    }, true);

    private T Access<T>(Func<AttendanceQueueSnapshot, T> action, bool write = false)
    {
        Directory.CreateDirectory(_directory);
        using var lease = AcquireLease();
        bool exists = File.Exists(_path);
        // Corruption is an error, never an empty queue. Preserve the file for recovery.
        var state = exists ? JsonSerializer.Deserialize<AttendanceQueueSnapshot>(File.ReadAllText(_path))
            ?? throw new InvalidDataException("Attendance outbox is empty or invalid.") : new AttendanceQueueSnapshot();
        if (string.IsNullOrWhiteSpace(state.DeviceId) || state.LastSequence < 0 || state.GateLogs == null || state.EventLogs == null)
            throw new InvalidDataException("Invalid attendance device state.");
        bool migrated = !state.LegacyImported;
        if (migrated)
        {
            var gates = ReadLegacy<PendingGateLog>("offline_gate_logs.json");
            var events = ReadLegacy<PendingEventAttendance>("offline_event_logs.json");
            foreach (var log in gates)
            {
                log.TransactionId = Guid.NewGuid().ToString("N");
                log.DeviceId = state.DeviceId;
                log.DeviceSequence = ++state.LastSequence;
                log.IsLegacy = true;
                log.WasOffline = true;
                state.GateLogs.Add(log);
            }
            foreach (var log in events)
            {
                log.TransactionId = Guid.NewGuid().ToString("N");
                log.DeviceId = state.DeviceId;
                log.DeviceSequence = ++state.LastSequence;
                state.EventLogs.Add(log);
            }
            state.LegacyImported = true;
        }
        var identities = state.GateLogs.Select(x => (x.TransactionId, x.DeviceId, x.DeviceSequence))
            .Concat(state.EventLogs.Select(x => (x.TransactionId, x.DeviceId, x.DeviceSequence))).ToList();
        if (identities.Any(x => !Guid.TryParseExact(x.TransactionId, "N", out _) || x.DeviceId != state.DeviceId ||
            x.DeviceSequence <= 0 || x.DeviceSequence > state.LastSequence) ||
            identities.Select(x => x.TransactionId).Distinct().Count() != identities.Count ||
            identities.Select(x => x.DeviceSequence).Distinct().Count() != identities.Count)
            throw new InvalidDataException("Attendance outbox contains invalid transaction identities.");
        T result = action(state);
        if (write || !exists || migrated) WriteAtomically(state);
        return result;
    }

    private List<T> ReadLegacy<T>(string name)
    {
        string path = Path.Combine(_directory, name);
        return File.Exists(path) ? JsonSerializer.Deserialize<List<T>>(File.ReadAllText(path))
            ?? throw new InvalidDataException($"Invalid legacy queue: {name}") : new();
    }

    private FileStream AcquireLease()
    {
        for (int attempt = 0; ; attempt++)
        {
            try { return new FileStream(_path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (attempt < 100) { Thread.Sleep(20); }
        }
    }

    private void WriteAtomically(AttendanceQueueSnapshot state)
    {
        string temporary = _path + ".tmp";
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, state);
            stream.Flush(true);
        }
        if (File.Exists(_path)) File.Replace(temporary, _path, null);
        else File.Move(temporary, _path);
    }
}
