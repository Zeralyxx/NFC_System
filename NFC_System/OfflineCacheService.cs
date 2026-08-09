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
    // THE FIX: Added missing properties required for offline PIN and QR logic
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
    public string NfcUid { get; set; } = "";
    public string TransactionType { get; set; } = "";
    public string VerificationMode { get; set; } = "";
    public bool IsGranted { get; set; }
    public string ErrorCode { get; set; } = "";
    public string Remarks { get; set; } = "";
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

    static OfflineCacheService()
    {
        EnsureDirectoryExists();
    }

    // THE FIX: Tracks failed PIN attempts in the local JSON so students actually get locked out offline
    public static void UpdateCachedStudentPinProgress(string studentId, int failedAttempts, bool isLocked)
    {
        lock (FileLock)
        {
            if (!File.Exists(StudentsCacheFile)) return;
            try
            {
                var students = JsonSerializer.Deserialize<List<CachedStudent>>(File.ReadAllText(StudentsCacheFile)) ?? new();
                var student = students.FirstOrDefault(s => s.StudentId == studentId);
                if (student != null)
                {
                    student.FailedPinAttempts = failedAttempts;
                    student.PinLocked = isLocked;
                    File.WriteAllText(StudentsCacheFile, JsonSerializer.Serialize(students, JsonOptions));
                }
            }
            catch { }
        }
    }

    private static void EnsureDirectoryExists()
    {
        if (!Directory.Exists(CacheDirectory))
        {
            Directory.CreateDirectory(CacheDirectory);
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
            string json = JsonSerializer.Serialize(students, JsonOptions);
            File.WriteAllText(StudentsCacheFile, json);
        }
    }

    public static void UpdateEventCache(List<CachedEvent> events, List<CachedEventRoster> rosters)
    {
        lock (FileLock)
        {
            EnsureDirectoryExists();
            File.WriteAllText(EventsCacheFile, JsonSerializer.Serialize(events, JsonOptions));
            File.WriteAllText(RostersCacheFile, JsonSerializer.Serialize(rosters, JsonOptions));
        }
    }

    // =========================================================================
    // 2. READ SHADOW CACHE (FOR UI OFFLINE FALLBACK)
    // =========================================================================

    public static List<CachedStudent> GetCachedStudents()
    {
        lock (FileLock)
        {
            if (!File.Exists(StudentsCacheFile)) return new List<CachedStudent>();
            try
            {
                string json = File.ReadAllText(StudentsCacheFile);
                return JsonSerializer.Deserialize<List<CachedStudent>>(json) ?? new List<CachedStudent>();
            }
            catch { return new List<CachedStudent>(); }
        }
    }

    public static List<CachedEvent> GetCachedEvents()
    {
        lock (FileLock)
        {
            if (!File.Exists(EventsCacheFile)) return new List<CachedEvent>();
            try
            {
                string json = File.ReadAllText(EventsCacheFile);
                return JsonSerializer.Deserialize<List<CachedEvent>>(json) ?? new List<CachedEvent>();
            }
            catch { return new List<CachedEvent>(); }
        }
    }

    // =========================================================================
    // 3. OFFLINE VERIFICATION ENGINE
    // =========================================================================

    // THE FIX: Added transactionType to strictly enforce entry/exit states while offline
    public static (bool IsGranted, CachedStudent? Student, string ErrorCode, string Remarks) VerifyStudentOffline(string uid, string transactionType, string? enteredPin = null)
    {
        lock (FileLock)
        {
            if (!File.Exists(StudentsCacheFile))
                return (false, null, "NO_CACHE", "Offline roster not initialized on terminal.");

            var students = JsonSerializer.Deserialize<List<CachedStudent>>(File.ReadAllText(StudentsCacheFile)) ?? new();
            var student = students.FirstOrDefault(s => s.NfcUid.Equals(uid, StringComparison.OrdinalIgnoreCase));

            if (student == null)
                return (false, null, "UNREGISTERED", "Card UID not found in offline shadow cache.");

            if (!student.Status.Equals("Active", StringComparison.OrdinalIgnoreCase))
                return (false, student, "INACTIVE_STATUS", $"Student status is currently '{student.Status}'.");

            if (student.PinLocked)
                return (false, student, "ACCOUNT_LOCKED", "Student account is locked due to PIN failures.");

            // THE FIX: Strict offline anti-tailgating and sequence tracking
            if (transactionType == "Entry" && student.EntryState.Equals("INSIDE", StringComparison.OrdinalIgnoreCase))
                return (false, student, "ANTI_TAILGATING_VIOLATION", "Consecutive entry attempt detected in offline mode.");

            if (transactionType == "Exit" && student.EntryState.Equals("OUTSIDE", StringComparison.OrdinalIgnoreCase))
                return (false, student, "IRREGULAR_EXIT_SEQUENCE", "Exit attempted while student is already marked OUTSIDE.");

            // Verify PIN if required
            if (!string.IsNullOrEmpty(student.PinHash) && !string.IsNullOrEmpty(enteredPin))
            {
                bool isPinValid = PinHasher.VerifyPin(enteredPin, student.PinSalt, student.PinHash);
                if (!isPinValid)
                    return (false, student, "INVALID_PIN", "Incorrect PIN entered in offline mode.");
            }

            return (true, student, "VERIFIED", "Verified via Offline Shadow Cache.");
        }
    }

    public static (bool IsAllowed, string Status, string Remarks) VerifyEventAttendeeOffline(string eventId, string studentId)
    {
        lock (FileLock)
        {
            if (!File.Exists(EventsCacheFile) || !File.Exists(RostersCacheFile))
                return (true, "GRANTED", "Event roster cache missing; allowing entry.");

            var events = JsonSerializer.Deserialize<List<CachedEvent>>(File.ReadAllText(EventsCacheFile)) ?? new();
            var evt = events.FirstOrDefault(e => e.EventId.Equals(eventId, StringComparison.OrdinalIgnoreCase));

            if (evt == null || !evt.IsRestricted)
                return (true, "GRANTED", "Unrestricted event in offline mode.");

            var rosters = JsonSerializer.Deserialize<List<CachedEventRoster>>(File.ReadAllText(RostersCacheFile)) ?? new();
            var roster = rosters.FirstOrDefault(r => r.EventId.Equals(eventId, StringComparison.OrdinalIgnoreCase));

            if (roster != null && roster.ApprovedStudentIds.Contains(studentId, StringComparer.OrdinalIgnoreCase))
            {
                return (true, "GRANTED", "Verified against offline event roster.");
            }

            return (false, "DENIED", "Student ID is not on the approved offline roster for this event.");
        }
    }

    // =========================================================================
    // 4. EMERGENCY LOG WRITING (WHEN MYSQL IS DOWN)
    // =========================================================================

    public static void SaveOfflineGateLog(string studentId, string nfcUid, string transactionType, string mode, bool isGranted, string errorCode, string remarks)
    {
        lock (FileLock)
        {
            EnsureDirectoryExists();
            var logs = GetPendingGateLogs();

            logs.Add(new PendingGateLog
            {
                Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                StudentId = studentId,
                NfcUid = nfcUid,
                TransactionType = transactionType,
                VerificationMode = mode,
                IsGranted = isGranted,
                ErrorCode = errorCode,
                Remarks = $"[OFFLINE MODE] {remarks}"
            });

            File.WriteAllText(GateLogsFile, JsonSerializer.Serialize(logs, JsonOptions));

            // THE FIX: Automatically toggle the cached Entry State to simulate a successful check-in
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
                Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                EventId = eventId,
                StudentId = studentId,
                VerificationMode = mode,
                Status = status,
                Remarks = $"[OFFLINE MODE] {remarks}"
            });

            File.WriteAllText(EventLogsFile, JsonSerializer.Serialize(logs, JsonOptions));
        }
    }

    private static void UpdateCachedStudentStateLocally(string studentId, string newState)
    {
        if (!File.Exists(StudentsCacheFile)) return;
        try
        {
            var students = JsonSerializer.Deserialize<List<CachedStudent>>(File.ReadAllText(StudentsCacheFile)) ?? new();
            var student = students.FirstOrDefault(s => s.StudentId == studentId);
            if (student != null)
            {
                student.EntryState = newState;
                File.WriteAllText(StudentsCacheFile, JsonSerializer.Serialize(students, JsonOptions));
            }
        }
        catch { }
    }

    // =========================================================================
    // 5. ATOMIC LOG EXTRACTION (PREVENTS CONCURRENCY DELETION BUGS)
    // =========================================================================

    private static List<PendingGateLog> GetPendingGateLogs()
    {
        if (!File.Exists(GateLogsFile)) return new();
        try
        {
            string json = File.ReadAllText(GateLogsFile);
            return JsonSerializer.Deserialize<List<PendingGateLog>>(json) ?? new();
        }
        catch { return new(); }
    }

    private static List<PendingEventAttendance> GetPendingEventLogs()
    {
        if (!File.Exists(EventLogsFile)) return new();
        try
        {
            string json = File.ReadAllText(EventLogsFile);
            return JsonSerializer.Deserialize<List<PendingEventAttendance>>(json) ?? new();
        }
        catch { return new(); }
    }

    public static bool HasPendingLogs()
    {
        return (File.Exists(GateLogsFile) && new FileInfo(GateLogsFile).Length > 10) ||
               (File.Exists(EventLogsFile) && new FileInfo(EventLogsFile).Length > 10);
    }

    // THE FIX: We no longer randomly clear files. We atomic-extract them, wiping the file at the exact millisecond we read it.
    public static List<PendingGateLog> ExtractPendingGateLogs()
    {
        lock (FileLock)
        {
            if (!File.Exists(GateLogsFile)) return new();
            try
            {
                string json = File.ReadAllText(GateLogsFile);
                File.Delete(GateLogsFile); // Instantly delete so new offline taps aren't lost
                return JsonSerializer.Deserialize<List<PendingGateLog>>(json) ?? new();
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
                File.Delete(EventLogsFile);
                return JsonSerializer.Deserialize<List<PendingEventAttendance>>(json) ?? new();
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