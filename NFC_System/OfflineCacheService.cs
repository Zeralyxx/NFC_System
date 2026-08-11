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

// --- Pending Offline Log Models ---
public class PendingGateLog
{
    public string Timestamp { get; set; } = "";
    public string StudentId { get; set; } = "";
    public string StudentName { get; set; } = "";
    public string NfcUid { get; set; } = "";
    public string TransactionType { get; set; } = "";
    public string VerificationMode { get; set; } = "";
    public bool IsGranted { get; set; }
    public string ErrorCode { get; set; } = "";
    public string Remarks { get; set; } = "";

    public double NfcSystemMs { get; set; }
    public double PinWorkflowMs { get; set; }
    public double PinSystemMs { get; set; }
    public double QrWorkflowMs { get; set; }
    public double QrSystemMs { get; set; }
    public double TotalWorkflowMs { get; set; }
    public double TotalSystemMs { get; set; }
    public double DbQuerySpeedMs { get; set; }
}

public class PendingEventAttendance
{
    public string Timestamp { get; set; } = "";
    public string EventId { get; set; } = "";
    public string StudentId { get; set; } = "";
    public string VerificationMode { get; set; } = "";
    public string Status { get; set; } = "";
    public string Remarks { get; set; } = "";
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

    private static readonly string GateLogsFile = Path.Combine(CacheDirectory, "offline_gate_logs.json");
    private static readonly string EventLogsFile = Path.Combine(CacheDirectory, "offline_event_logs.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly object FileLock = new();

    // =========================================================================
    // IN-MEMORY RAM CACHE (TO PREVENT UI FREEZING)
    // =========================================================================
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

    // =========================================================================
    // 1. SAVE SHADOW CACHE (EXECUTED REGULARLY WHEN ONLINE)
    // =========================================================================

    public static void UpdateStudentCache(List<CachedStudent> students)
    {
        lock (FileLock)
        {
            EnsureDirectoryExists();
            _inMemoryStudents = students;
            _isStudentMemoryLoaded = true;
            string json = JsonSerializer.Serialize(students, JsonOptions);
            File.WriteAllText(StudentsCacheFile, json);
        }
    }

    public static void UpdateEventCache(List<CachedEvent> events, List<CachedEventRoster> rosters)
    {
        lock (FileLock)
        {
            EnsureDirectoryExists();
            _inMemoryEvents = events;
            _inMemoryRosters = rosters;
            _isEventMemoryLoaded = true;
            File.WriteAllText(EventsCacheFile, JsonSerializer.Serialize(events, JsonOptions));
            File.WriteAllText(RostersCacheFile, JsonSerializer.Serialize(rosters, JsonOptions));
        }
    }

    // =========================================================================
    // 2. READ SHADOW CACHE (FOR UI DROPDOWNS)
    // =========================================================================

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

    // =========================================================================
    // 3. OFFLINE VERIFICATION ENGINE (LIGHTNING FAST)
    // =========================================================================

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

    // =========================================================================
    // 4. FAST BACKGROUND FILE WRITERS (NO UI FREEZES)
    // =========================================================================

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

    private static void UpdateCachedStudentStateLocally(string studentId, string newState)
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
            string json;
            lock (FileLock) { json = JsonSerializer.Serialize(_inMemoryStudents, JsonOptions); }
            try { File.WriteAllText(StudentsCacheFile, json); } catch { }
        });
    }

    // =========================================================================
    // 5. EMERGENCY LOG WRITING & ATOMIC EXTRACTION
    // =========================================================================

    public static void SaveOfflineGateLog(string studentId, string studentName, string nfcUid, string transactionType, string mode, bool isGranted, string errorCode, string remarks, double nfcSys, double pinWf, double pinSys, double qrWf, double qrSys, double totWf, double totSys, double dbSpeed)
    {
        lock (FileLock)
        {
            EnsureDirectoryExists();
            var logs = GetPendingGateLogs();
            logs.Add(new PendingGateLog
            {
                Timestamp = DatabaseService.GetNetworkAdjustedTime().ToString("yyyy-MM-dd HH:mm:ss"),
                StudentId = studentId,
                StudentName = studentName,
                NfcUid = nfcUid,
                TransactionType = transactionType,
                VerificationMode = mode,
                IsGranted = isGranted,

                // THE FIX: Enforce OFFLINE_MODE flag natively into the JSON to bypass DatabaseService stripping logic
                ErrorCode = isGranted ? "OFFLINE_MODE" : errorCode,
                Remarks = $"[OFFLINE MODE] {remarks}",

                NfcSystemMs = nfcSys,
                PinWorkflowMs = pinWf,
                PinSystemMs = pinSys,
                QrWorkflowMs = qrWf,
                QrSystemMs = qrSys,
                TotalWorkflowMs = totWf,
                TotalSystemMs = totSys,
                DbQuerySpeedMs = dbSpeed
            });
            File.WriteAllText(GateLogsFile, JsonSerializer.Serialize(logs, JsonOptions));

            if (isGranted && !string.IsNullOrEmpty(studentId))
            {
                UpdateCachedStudentStateLocally(studentId, transactionType == "Entry" ? "INSIDE" : "OUTSIDE");
            }
        }
    }

    public static void SaveOfflineEventLog(string eventId, string studentId, string mode, string status, string remarks)
    {
        lock (FileLock)
        {
            EnsureDirectoryExists();
            var logs = GetPendingEventLogs();
            logs.Add(new PendingEventAttendance
            {
                Timestamp = DatabaseService.GetNetworkAdjustedTime().ToString("yyyy-MM-dd HH:mm:ss"),
                EventId = eventId,
                StudentId = studentId,
                VerificationMode = mode,
                Status = status,
                Remarks = $"[OFFLINE MODE] {remarks}"
            });
            File.WriteAllText(EventLogsFile, JsonSerializer.Serialize(logs, JsonOptions));
        }
    }

    private static List<PendingGateLog> GetPendingGateLogs()
    {
        if (!File.Exists(GateLogsFile)) return new();
        try { return JsonSerializer.Deserialize<List<PendingGateLog>>(File.ReadAllText(GateLogsFile)) ?? new(); }
        catch { return new(); }
    }

    private static List<PendingEventAttendance> GetPendingEventLogs()
    {
        if (!File.Exists(EventLogsFile)) return new();
        try { return JsonSerializer.Deserialize<List<PendingEventAttendance>>(File.ReadAllText(EventLogsFile)) ?? new(); }
        catch { return new(); }
    }

    public static bool HasPendingLogs()
    {
        return (File.Exists(GateLogsFile) && new FileInfo(GateLogsFile).Length > 10) ||
               (File.Exists(EventLogsFile) && new FileInfo(EventLogsFile).Length > 10);
    }

    public static List<PendingGateLog> ExtractPendingGateLogs()
    {
        lock (FileLock)
        {
            if (!File.Exists(GateLogsFile)) return new();
            try
            {
                string json = File.ReadAllText(GateLogsFile);
                var logs = JsonSerializer.Deserialize<List<PendingGateLog>>(json) ?? new();
                File.Delete(GateLogsFile);
                return logs;
            }
            catch { return new(); }
        }
    }

    public static List<PendingEventAttendance> ExtractPendingEventLogs()
    {
        lock (FileLock)
        {
            if (!File.Exists(EventLogsFile)) return new();
            try
            {
                string json = File.ReadAllText(EventLogsFile);
                var logs = JsonSerializer.Deserialize<List<PendingEventAttendance>>(json) ?? new();
                File.Delete(EventLogsFile);
                return logs;
            }
            catch { return new(); }
        }
    }

    public static void RestoreFailedGateLogs(List<PendingGateLog> failedLogs)
    {
        if (failedLogs.Count == 0) return;
        lock (FileLock)
        {
            var existing = GetPendingGateLogs();
            existing.InsertRange(0, failedLogs);
            File.WriteAllText(GateLogsFile, JsonSerializer.Serialize(existing, JsonOptions));
        }
    }

    public static void RestoreFailedEventLogs(List<PendingEventAttendance> failedLogs)
    {
        if (failedLogs.Count == 0) return;
        lock (FileLock)
        {
            var existing = GetPendingEventLogs();
            existing.InsertRange(0, failedLogs);
            File.WriteAllText(EventLogsFile, JsonSerializer.Serialize(existing, JsonOptions));
        }
    }
}