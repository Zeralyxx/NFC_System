// In-memory boundaries; production verification and cryptography are linked unchanged.
namespace NFC_System;

public record TestLog(string StudentId, bool Granted, string Error, string Remarks);

public sealed class DatabaseService
{
    public void ActivateAttendanceDevice() { }
    public bool FailAttendanceStorage { get; set; }
    public bool AttendanceConflict { get; set; }
    public bool AttendancePending { get; set; }
    public Task<AttendanceCommitResult> RecordAttendanceTransactionAsync(VerificationSession session, string remarks)
    {
        if (FailAttendanceStorage) throw new IOException("Test outbox unavailable");
        if (AttendancePending) return Task.FromResult(new AttendanceCommitResult { Timestamp = DateTime.Now, IsGranted = false, ConfirmationPending = true });
        if (AttendanceConflict) return Task.FromResult(new AttendanceCommitResult { Timestamp = DateTime.Now, Committed = true, IsGranted = false });
        bool offline = session.IsOffline || FailReads;
        var log = new TestLog(session.Student.StudentId, true, "VERIFIED", remarks);
        if (offline) OfflineCacheService.Logs.Add(log); else Logs.Add(log);
        string state = session.TransactionType == TransactionType.Exit ? "OUTSIDE" : "INSIDE";
        if (session.TransactionType != TransactionType.Exit || string.IsNullOrWhiteSpace(session.EventId))
        {
            OfflineCacheService.UpdateCachedStudentStateLocally(session.Student.StudentId, state);
            session.Student.EntryState = state;
            if (!offline) Students.Single(s => s.StudentId == session.Student.StudentId).EntryState = state;
        }
        return Task.FromResult(new AttendanceCommitResult { Timestamp = DateTime.Now, Committed = !offline });
    }
    public List<StudentRecord> Students { get; } = new();
    public List<TestLog> Logs { get; } = new();
    public bool FailReads { get; set; }
    public bool YieldReads { get; set; }
    public bool EventAllowed { get; set; } = true;
    public bool EventEntered { get; set; }
    public int ReadCount { get; private set; }
    public Task<StudentRecord?> GetStudentByIdAsync(string id) => Read(s => s.StudentId == id);
    public Task<StudentRecord?> GetStudentByUidAsync(string uid) => Read(s => s.NfcUid == uid);
    private async Task<StudentRecord?> Read(Func<StudentRecord, bool> predicate)
    {
        ReadCount++;
        if (YieldReads) await Task.Yield();
        if (FailReads) throw new IOException("Test database disconnected");
        var student = Students.FirstOrDefault(predicate);
        return student == null ? null : System.Text.Json.JsonSerializer.Deserialize<StudentRecord>(System.Text.Json.JsonSerializer.Serialize(student));
    }
    public Task<bool> HasStudentEnteredEventAsync(string? eventId, string id) => Task.FromResult(EventEntered);
    public Task<bool> IsStudentAllowedForEventAsync(string? eventId, string id) => Task.FromResult(EventAllowed);
    public Task UpdatePinFailureAsync(string id, int failed, bool locked)
    {
        var student = Students.Single(s => s.StudentId == id);
        student.FailedPinAttempts = failed;
        student.PinLocked = locked;
        return Task.CompletedTask;
    }
    public Task UpdateEntryStateAsync(string id, string state)
    {
        Students.Single(s => s.StudentId == id).EntryState = state;
        return Task.CompletedTask;
    }
    public Task AddAlertAsync(string id, string type, string message) => Task.CompletedTask;
    public Task RecordAttendanceAsync(string? eventId, string id, string mode, string status, string remarks) => Task.CompletedTask;
    public Task RecordAttendanceAsync(string? eventId, string id, VerificationMode mode, string status, string remarks) => Task.CompletedTask;
    public static string ToStorageValue<T>(T value) => value?.ToString() ?? "";
    public Task LogVerificationAsync(StudentRecord? student, string? name, string uid, TransactionType type, VerificationMode mode,
        bool granted, string status, string error, string remarks, double nfc, double pinW, double pinS, double qrW, double qrS, double db)
    {
        Logs.Add(new(student?.StudentId ?? "", granted, error, remarks));
        return Task.CompletedTask;
    }
}

public static class DatabaseMonitor
{
    public static bool IsOnline { get; set; } = true;
}

public static class OfflineCacheService
{
    public static bool AttendanceStorageAvailable { get; set; } = true;
    public static bool IsAttendanceStorageAvailable() => AttendanceStorageAvailable;
    public static string? GetPendingStudentEntryState(string id) =>
        Logs.Any(l => l.StudentId == id && l.Granted) ? Students.FirstOrDefault(s => s.StudentId == id)?.EntryState : null;
    public static List<StudentRecord> Students { get; } = new();
    public static List<TestLog> Logs { get; } = new();
    public static bool EventAllowed { get; set; } = true;
    public static List<StudentRecord> GetCachedStudents() => Students;
    public static (bool IsGranted, StudentRecord? Student, string ErrorCode, string Remarks) VerifyStudentOffline(string uid, string type)
    {
        var student = Students.FirstOrDefault(s => s.NfcUid == uid);
        if (student == null) return (false, null, "UNREGISTERED", "Unknown cached student");
        if (student.Status != "Active") return (false, student, "INACTIVE_STATUS", "Inactive student");
        if (type == "Entry" && student.EntryState == "INSIDE") return (false, student, "ANTI_TAILGATING_VIOLATION", "Already inside");
        return (true, student, "VERIFIED", "Cached student");
    }
    public static (bool IsAllowed, string Remarks) VerifyEventAttendeeOffline(string id, string studentId) => (EventAllowed, "Test event roster");
    public static void UpdateCachedStudentPinProgress(string id, int failed, bool locked)
    {
        var student = Students.FirstOrDefault(s => s.StudentId == id);
        if (student == null) return;
        student.FailedPinAttempts = failed;
        student.PinLocked = locked;
    }
    public static void UpdateCachedStudentStateLocally(string id, string state)
    {
        var student = Students.FirstOrDefault(s => s.StudentId == id);
        if (student != null) student.EntryState = state;
    }
    public static void SaveOfflineEventLog(string? eventId, string id, string mode, string status, string remarks) =>
        Logs.Add(new(id, status == "PRESENT", status, remarks));
    public static void SaveOfflineGateLog(string id, string name, string uid, string type, string mode, bool granted, string error,
        string remarks, double nfc, double pinW, double pinS, double qrW, double qrS, double totalW, double totalS, double db) =>
        Logs.Add(new(id, granted, error, remarks));
}
