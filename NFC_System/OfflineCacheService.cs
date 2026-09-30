using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace NFC_System;

// --- DTOs for JSON Caching ---
public class CachedStudent
{
    public string StudentId { get; set; } = "";
    public string FullName { get; set; } = "";
    public string NfcUid { get; set; } = "";
    public string PinHash { get; set; } = "";
    public string PinSalt { get; set; } = "";
    public string Status { get; set; } = "";
    public bool PinLocked { get; set; }
    public string EntryState { get; set; } = "OUTSIDE";
    public int FailedPinAttempts { get; set; }
    public string QrCredential { get; set; } = "";
}

public class CachedEvent
{
    public string EventId { get; set; } = "";
    public string EventName { get; set; } = "";
    public string VerificationMode { get; set; } = "";
    public bool IsRestricted { get; set; }
    public bool IsActive { get; set; }
}

public class CachedEventRoster
{
    public string EventId { get; set; } = "";
    public List<string> ApprovedStudentIds { get; set; } = new();
}


public static class OfflineCacheService
{
    private static readonly string CacheDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "NFC_System",
        "Cache"
    );

    private static readonly string StudentsCacheFile = Path.Combine(CacheDirectory, "local_students.json");
    private static readonly string EventsCacheFile = Path.Combine(CacheDirectory, "local_events.json");
    private static readonly string RostersCacheFile = Path.Combine(CacheDirectory, "local_event_rosters.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly object FileLock = new();
    public static DurableAttendanceQueue AttendanceQueue { get; } = new(CacheDirectory);

    private static List<CachedStudent> _inMemoryStudents = new();
    private static List<CachedEvent> _inMemoryEvents = new();
    private static List<CachedEventRoster> _inMemoryRosters = new();

    private static bool _isStudentMemoryLoaded = false;
    private static bool _isEventMemoryLoaded = false;

    static OfflineCacheService()
    {
        EnsureDirectoryExists();
    }

    private static void EnsureDirectoryExists()
    {
        if (!Directory.Exists(CacheDirectory))
        {
            Directory.CreateDirectory(CacheDirectory);
        }
    }

    private static void LoadStudentMemoryCache()
    {
        if (_isStudentMemoryLoaded) return;
        lock (FileLock)
        {
            if (_isStudentMemoryLoaded) return;
            if (File.Exists(StudentsCacheFile))
            {
                try { _inMemoryStudents = JsonSerializer.Deserialize<List<CachedStudent>>(File.ReadAllText(StudentsCacheFile)) ?? new(); }
                catch { }
            }
            // Replay durable, not-yet-acknowledged admissions after a crash before cache persistence.
            var pending = AttendanceQueue.Snapshot();
            foreach (var change in PendingStudentStates(pending).OrderBy(x => x.Sequence))
            {
                var student = _inMemoryStudents.FirstOrDefault(x => x.StudentId == change.StudentId);
                if (student != null) student.EntryState = change.State;
            }
            _isStudentMemoryLoaded = true;
        }
    }

    private static void LoadEventMemoryCache()
    {
        if (_isEventMemoryLoaded) return;
        lock (FileLock)
        {
            if (_isEventMemoryLoaded) return;
            try
            {
                if (File.Exists(EventsCacheFile))
                    _inMemoryEvents = JsonSerializer.Deserialize<List<CachedEvent>>(File.ReadAllText(EventsCacheFile)) ?? new();
                if (File.Exists(RostersCacheFile))
                    _inMemoryRosters = JsonSerializer.Deserialize<List<CachedEventRoster>>(File.ReadAllText(RostersCacheFile)) ?? new();
            }
            catch { }
            _isEventMemoryLoaded = true;
        }
    }

    public static bool UpdateStudentCache(List<CachedStudent> students)
    {
        lock (FileLock)
        {
            if (HasPendingLogs()) return false;
            EnsureDirectoryExists();
            WriteCacheFile(StudentsCacheFile, students);
            _inMemoryStudents = students;
            _isStudentMemoryLoaded = true;
            return true;
        }
    }

    public static void UpdateCachedCredential(string originalStudentId, StudentRecord updated, bool pinReplaced = false)
    {
        LoadStudentMemoryCache();
        lock (FileLock)
        {
            var cached = _inMemoryStudents.FirstOrDefault(s => s.StudentId == originalStudentId);
            if (cached == null) return;
            // Keep pending entry/PIN progress while replacing the cached credential.
            cached.StudentId = updated.StudentId;
            cached.NfcUid = updated.NfcUid;
            cached.QrCredential = updated.QrCredential;
            cached.Status = updated.Status;
            cached.FullName = updated.FullName;
            if (pinReplaced)
            {
                cached.PinSalt = updated.PinSalt;
                cached.PinHash = updated.PinHash;
                cached.PinLocked = false;
                cached.FailedPinAttempts = 0;
            }
            WriteCacheFile(StudentsCacheFile, _inMemoryStudents);
        }
    }

    public static void UpdateEventCache(List<CachedEvent> events, List<CachedEventRoster> rosters)
    {
        lock (FileLock)
        {
            EnsureDirectoryExists();
            WriteCacheFile(EventsCacheFile, events);
            WriteCacheFile(RostersCacheFile, rosters);
            _inMemoryEvents = events;
            _inMemoryRosters = rosters;
            _isEventMemoryLoaded = true;
        }
    }

    public static List<CachedStudent> GetCachedStudents()
    {
        LoadStudentMemoryCache();
        return _inMemoryStudents;
    }

    public static List<CachedEvent> GetCachedEvents()
    {
        LoadEventMemoryCache();
        return _inMemoryEvents;
    }

    public static (bool IsGranted, CachedStudent? Student, string ErrorCode, string Remarks) VerifyStudentOffline(string uid, string transactionType, string? enteredPin = null)
    {
        LoadStudentMemoryCache();
        var student = _inMemoryStudents.FirstOrDefault(s => s.NfcUid.Equals(uid, StringComparison.OrdinalIgnoreCase));

        if (student == null)
            return (false, null, "UNREGISTERED", "Card UID not found in offline shadow cache.");

        if (!student.Status.Equals("Active", StringComparison.OrdinalIgnoreCase))
            return (false, student, "INACTIVE_STATUS", $"Student status is currently '{student.Status}'.");

        if (student.PinLocked)
            return (false, student, "ACCOUNT_LOCKED", "Student account is locked due to PIN failures.");

        if (transactionType == "Entry" && student.EntryState.Equals("INSIDE", StringComparison.OrdinalIgnoreCase))
            return (false, student, "ANTI_TAILGATING_VIOLATION", "Consecutive entry attempt detected in offline mode.");

        if (transactionType == "Exit" && student.EntryState.Equals("OUTSIDE", StringComparison.OrdinalIgnoreCase))
            return (false, student, "IRREGULAR_EXIT_SEQUENCE", "Exit attempted while student is already marked OUTSIDE.");

        if (!string.IsNullOrEmpty(student.PinHash) && !string.IsNullOrEmpty(enteredPin))
        {
            bool isPinValid = PinHasher.VerifyPin(enteredPin, student.PinSalt, student.PinHash);
            if (!isPinValid)
                return (false, student, "INVALID_PIN", "Incorrect PIN entered in offline mode.");
        }

        return (true, student, "VERIFIED", "Verified via Offline Shadow Cache.");
    }

    public static (bool IsAllowed, string Status, string Remarks) VerifyEventAttendeeOffline(string eventId, string studentId)
    {
        LoadEventMemoryCache();

        var evt = _inMemoryEvents.FirstOrDefault(e => e.EventId.Equals(eventId, StringComparison.OrdinalIgnoreCase));
        if (evt == null || !evt.IsRestricted)
            return (true, "GRANTED", "Unrestricted event in offline mode.");

        var roster = _inMemoryRosters.FirstOrDefault(r => r.EventId.Equals(eventId, StringComparison.OrdinalIgnoreCase));
        if (roster != null && roster.ApprovedStudentIds.Contains(studentId, StringComparer.OrdinalIgnoreCase))
            return (true, "GRANTED", "Verified against offline event roster.");

        return (false, "DENIED", "Student ID is not on the approved offline roster for this event.");
    }

    public static void UpdateCachedStudentPinProgress(string studentId, int failedAttempts, bool isLocked)
    {
        LoadStudentMemoryCache();
        var student = _inMemoryStudents.FirstOrDefault(s => s.StudentId == studentId);
        if (student != null)
        {
            student.FailedPinAttempts = failedAttempts;
            student.PinLocked = isLocked;
            SaveStudentsToDiskBackground();
        }
    }

    // THE FIX: Changed to Public so the VerificationEngine can unconditionally sync online/offline physical states
    public static void UpdateCachedStudentStateLocally(string studentId, string newState)
    {
        LoadStudentMemoryCache();
        var student = _inMemoryStudents.FirstOrDefault(s => s.StudentId == studentId);
        if (student != null)
        {
            student.EntryState = newState;
            SaveStudentsToDiskBackground();
        }
    }

    private static void SaveStudentsToDiskBackground()
    {
        Task.Run(() =>
        {
            lock (FileLock)
            {
                try { WriteCacheFile(StudentsCacheFile, _inMemoryStudents); }
                catch { }
            }
        });
    }

    public static void SaveOfflineGateLog(string studentId, string studentName, string nfcUid, string transactionType, string mode, bool isGranted, string errorCode, string remarks, double nfcSys, double pinWf, double pinSys, double qrWf, double qrSys, double totWf, double totSys, double dbSpeed)
    {
        AttendanceQueue.Enqueue(new PendingGateLog
        {
            Timestamp = DatabaseService.GetNetworkAdjustedTime().ToString("yyyy-MM-dd HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture),
            StudentId = studentId, StudentName = studentName, NfcUid = nfcUid,
            TransactionType = transactionType, VerificationMode = mode, IsGranted = isGranted,
            ErrorCode = isGranted ? "OFFLINE_MODE" : errorCode, Remarks = remarks, WasOffline = true,
            NfcSystemMs = nfcSys, PinWorkflowMs = pinWf, PinSystemMs = pinSys,
            QrWorkflowMs = qrWf, QrSystemMs = qrSys, TotalWorkflowMs = totWf,
            TotalSystemMs = totSys, DbQuerySpeedMs = dbSpeed
        });
    }

    public static void FlushStudentCache()
    {
        LoadStudentMemoryCache();
        lock (FileLock)
        {
            WriteCacheFile(StudentsCacheFile, _inMemoryStudents);
        }
    }

    private static void WriteCacheFile<T>(string path, T value)
    {
        string temporary = path + ".tmp";
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, value, JsonOptions);
            stream.Flush(true);
        }
        if (File.Exists(path)) File.Replace(temporary, path, null);
        else File.Move(temporary, path);
    }

    public static void SaveOfflineEventLog(string eventId, string studentId, string mode, string status, string remarks)
    {
        AttendanceQueue.Enqueue(new PendingEventAttendance
        {
            Timestamp = DatabaseService.GetNetworkAdjustedTime().ToString("yyyy-MM-dd HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture),
            EventId = eventId, StudentId = studentId, VerificationMode = mode,
            Status = status, Remarks = remarks
        });
    }

    public static bool HasPendingLogs()
    {
        try
        {
            var snapshot = AttendanceQueue.Snapshot();
            return snapshot.GateLogs.Count != 0 || snapshot.EventLogs.Count != 0;
        }
        catch { return true; }
    }

    public static bool IsAttendanceStorageAvailable()
    {
        try { AttendanceQueue.Snapshot(); return true; }
        catch { return false; }
    }

    public static string? GetPendingStudentEntryState(string studentId)
    {
        return PendingStudentStates(AttendanceQueue.Snapshot()).Where(x => x.StudentId == studentId)
            .OrderByDescending(x => x.Sequence).Select(x => x.State).FirstOrDefault();
    }

    private static IEnumerable<(string StudentId, long Sequence, string State)> PendingStudentStates(AttendanceQueueSnapshot pending)
    {
        var gate = pending.GateLogs.Where(x => x.IsGranted && !x.OnlineAttempt &&
            (x.TransactionType == "Entry" || x.TransactionType == "EventAttendance" ||
             (x.TransactionType == "Exit" && string.IsNullOrWhiteSpace(x.EventId))))
            .Select(x => (x.StudentId, Sequence: x.DeviceSequence, State: x.TransactionType == "Exit" ? "OUTSIDE" : "INSIDE"));
        var events = pending.EventLogs.Where(x => x.Status == "PRESENT")
            .Select(x => (x.StudentId, Sequence: x.DeviceSequence, State: "INSIDE"));
        return gate.Concat(events);
    }
}
