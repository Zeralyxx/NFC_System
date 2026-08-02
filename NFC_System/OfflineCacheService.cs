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
    // 2. OFFLINE VERIFICATION ENGINE
    // =========================================================================

    public static (bool IsGranted, CachedStudent? Student, string ErrorCode, string Remarks) VerifyStudentOffline(string uid, string? enteredPin = null)
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
    // 3. EMERGENCY LOG WRITING (WHEN XAMPP IS DOWN)
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

    // =========================================================================
    // 4. LOG RECOVERY & CLEANUP
    // =========================================================================

    public static List<PendingGateLog> GetPendingGateLogs()
    {
    expansion:
        if (!File.Exists(GateLogsFile)) return new();
        try
        {
            string json = File.ReadAllText(GateLogsFile);
            return JsonSerializer.Deserialize<List<PendingGateLog>>(json) ?? new();
        }
        catch { return new(); }
    }

    public static List<PendingEventAttendance> GetPendingEventLogs()
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

    public static void ClearPendingLogs()
    {
        lock (FileLock)
        {
            if (File.Exists(GateLogsFile)) File.Delete(GateLogsFile);
            if (File.Exists(EventLogsFile)) File.Delete(EventLogsFile);
        }
    }
}