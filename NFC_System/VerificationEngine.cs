using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace NFC_System;

public sealed class VerificationEngine
{
    private readonly DatabaseService _database;

    public VerificationEngine(DatabaseService database)
    {
        _database = database;
    }

    // ====================================================================
    // EVALUATION MODULE: DATABASE QUERY PERFORMANCE MONITOR
    // ====================================================================
    private void LogPerformanceMetric(string operation, double elapsedMs, string result)
    {
        Task.Run(() =>
        {
            try
            {
                // Save to the app's local directory safely
                string logDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
                Directory.CreateDirectory(logDirectory);
                string logFile = Path.Combine(logDirectory, "System_Performance_Metrics.csv");

                bool isNewFile = !File.Exists(logFile);
                using var writer = new StreamWriter(logFile, true);

                if (isNewFile)
                {
                    writer.WriteLine("Timestamp,Operation,Elapsed Time (ms),Result");
                }

                writer.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff},\"{operation}\",{elapsedMs},\"{result}\"");
            }
            catch { /* Failsafe: Ignore IO errors so the verification flow never crashes */ }
        });
    }

    public async Task<VerificationOutcome> BeginNfcVerificationAsync(string uid, VerificationMode mode, TransactionType transactionType, string? eventId = null, bool isQrFallback = false)
    {
        // ---------------------------------------------------------
        // START DB TIMER: Retrieve Student Profile & PIN Hash
        // ---------------------------------------------------------
        Stopwatch profileTimer = Stopwatch.StartNew();

        StudentRecord? student = await _database.GetStudentByUidAsync(uid);

        profileTimer.Stop();
        LogPerformanceMetric("DB Query: Retrieve Profile & PIN Hash (NFC)", profileTimer.ElapsedMilliseconds, student != null ? "Found" : "Not Found");
        // ---------------------------------------------------------

        string scanTime = DateTime.Now.ToString("yyyy-MM-dd hh:mm:ss tt");

        if (student == null)
        {
            string error = "NOT_REGISTERED";
            await _database.LogVerificationAsync(null, uid, transactionType, mode, false, "NFC_NOT_REGISTERED", error, "NFC UID is not linked to a student record.");
            await _database.AddAlertAsync(null, error, $"Unregistered NFC UID attempted verification: {uid}.");
            return Denied(uid, null, "UNAUTHORIZED", "NFC credential is not registered", "NOT_REGISTERED", $"{scanTime} | UID {uid} | DENIED | NOT REGISTERED");
        }

        if (!student.Status.Equals("Active", StringComparison.OrdinalIgnoreCase))
        {
            string error = "INACTIVE_STUDENT";
            await _database.LogVerificationAsync(student, uid, transactionType, mode, false, error, error, $"Student status is {student.Status}.");
            await _database.AddAlertAsync(student.StudentId, error, $"{student.FullName} attempted access with status {student.Status}.");
            return Denied(uid, student, "ACCESS DENIED", $"Student status is {student.Status}", error, $"{scanTime} | {student.StudentId} | {student.FullName} | DENIED | {student.Status.ToUpper()}");
        }

        if (transactionType == TransactionType.Entry && student.EntryState.Equals("INSIDE", StringComparison.OrdinalIgnoreCase))
        {
            string error = "ANTI_TAILGATING_VIOLATION";
            await _database.LogVerificationAsync(student, uid, transactionType, mode, false, error, error, "Consecutive entry attempt detected before an exit transaction.");
            await _database.AddAlertAsync(student.StudentId, error, $"{student.FullName} attempted a second entry while already marked INSIDE.");
            return Denied(uid, student, "ACCESS DENIED", "Anti-tailgating rule blocked repeated entry", error, $"{scanTime} | {student.StudentId} | {student.FullName} | DENIED | TAILGATING");
        }

        if (transactionType == TransactionType.Exit && string.IsNullOrWhiteSpace(eventId) && student.EntryState.Equals("OUTSIDE", StringComparison.OrdinalIgnoreCase))
        {
            string error = "IRREGULAR_EXIT_SEQUENCE";
            await _database.LogVerificationAsync(student, uid, transactionType, mode, false, error, error, "Exit attempted while student is already marked OUTSIDE.");
            await _database.AddAlertAsync(student.StudentId, error, $"{student.FullName} attempted exit while already marked OUTSIDE.");
            return Denied(uid, student, "ACCESS DENIED", "Exit blocked because student is already outside", error, $"{scanTime} | {student.StudentId} | {student.FullName} | DENIED | IRREGULAR EXIT");
        }

        if (transactionType == TransactionType.EventAttendance)
        {
            // ---------------------------------------------------------
            // START DB TIMER: Event Registration Check
            // ---------------------------------------------------------
            Stopwatch eventTimer = Stopwatch.StartNew();

            bool allowed = await _database.IsStudentAllowedForEventAsync(eventId, student.StudentId);

            eventTimer.Stop();
            LogPerformanceMetric("DB Query: Validate Event Roster", eventTimer.ElapsedMilliseconds, allowed ? "Approved" : "Denied");
            // ---------------------------------------------------------

            if (!allowed)
            {
                string error = "UNAUTHORIZED_EVENT_ACCESS";
                await _database.LogVerificationAsync(student, uid, transactionType, mode, false, error, error, $"Student is not approved for event {eventId}.");
                await _database.AddAlertAsync(student.StudentId, error, $"{student.FullName} attempted to attend restricted or unknown event {eventId}.");
                return Denied(uid, student, "ATTENDANCE DENIED", "Student is not on the approved event list", error, $"{scanTime} | {student.StudentId} | {student.FullName} | EVENT DENIED | UNAUTHORIZED");
            }
        }

        var session = new VerificationSession
        {
            Student = student,
            Uid = uid,
            Mode = mode,
            TransactionType = transactionType,
            EventId = eventId,
            IsQrFallback = isQrFallback
        };

        if (isQrFallback)
        {
            return new VerificationOutcome
            {
                Step = VerificationStep.RequiresPin,
                IsGranted = false,
                ResultTitle = "QR ACCEPTED",
                ResultMessage = "Please enter your PIN to verify identity",
                Student = student,
                Session = session,
                LogLine = $"{scanTime} | {student.StudentId} | {student.FullName} | QR OK | PIN REQUIRED"
            };
        }

        if (mode == VerificationMode.Fast || transactionType == TransactionType.Exit)
        {
            string bypassRemarks = transactionType == TransactionType.Exit
                ? "NFC validation passed."
                : "NFC validation passed in Fast Mode.";

            return await GrantAsync(session, bypassRemarks);
        }

        if (student.PinLocked)
        {
            string error = "PIN_LOCKED";
            await _database.LogVerificationAsync(student, uid, transactionType, mode, false, error, error, "PIN verification is locked after repeated failed attempts.");
            await _database.AddAlertAsync(student.StudentId, error, $"{student.FullName} has a locked PIN and requires admin reset.");
            return Denied(uid, student, "ACCESS DENIED", "PIN is locked after repeated failed attempts", error, $"{scanTime} | {student.StudentId} | {student.FullName} | DENIED | PIN LOCKED");
        }

        return new VerificationOutcome
        {
            Step = VerificationStep.RequiresPin,
            IsGranted = false,
            ResultTitle = "PIN REQUIRED",
            ResultMessage = mode == VerificationMode.HighSecurity
                ? "Enter PIN before QR credential validation"
                : "Enter student PIN to complete Standard Mode",
            Student = student,
            Session = session,
            LogLine = $"{scanTime} | {student.StudentId} | {student.FullName} | NFC OK | PIN REQUIRED"
        };
    }

    public async Task<VerificationOutcome> SubmitPinAsync(VerificationSession session, string pin)
    {
        StudentRecord student = session.Student;
        string scanTime = DateTime.Now.ToString("yyyy-MM-dd hh:mm:ss tt");

        if (!PinHasher.VerifyPin(pin, student.PinSalt, student.PinHash))
        {
            int failedAttempts = student.FailedPinAttempts + 1;
            bool locked = failedAttempts >= 3;

            student.FailedPinAttempts = failedAttempts;
            student.PinLocked = locked;

            await _database.UpdatePinFailureAsync(student.StudentId, failedAttempts, locked);

            string error = locked ? "PIN_LOCKED" : "PIN_FAILURE";
            await _database.LogVerificationAsync(student, session.Uid, session.TransactionType, session.Mode, false, error, error, $"Failed PIN attempt {failedAttempts}/3.");

            string alertMessage = locked
                ? $"{student.FullName} reached three failed PIN attempts and has been locked."
                : $"{student.FullName} entered an incorrect PIN ({failedAttempts}/3).";

            await _database.AddAlertAsync(student.StudentId, error, alertMessage);

            return Denied(session.Uid, student, "ACCESS DENIED", locked ? "PIN locked after three failed attempts" : $"Incorrect PIN ({failedAttempts}/3)", error, $"{scanTime} | {student.StudentId} | {student.FullName} | DENIED | {error}");
        }

        await _database.UpdatePinFailureAsync(student.StudentId, 0, false);
        student.FailedPinAttempts = 0;
        student.PinLocked = false;

        if (session.Mode == VerificationMode.HighSecurity && !session.IsQrFallback)
        {
            return new VerificationOutcome
            {
                Step = VerificationStep.RequiresQr,
                IsGranted = false,
                ResultTitle = "QR REQUIRED",
                ResultMessage = "PIN accepted. Validate printed QR credential.",
                Student = student,
                Session = session,
                LogLine = $"{scanTime} | {student.StudentId} | {student.FullName} | PIN OK | QR REQUIRED"
            };
        }

        string logRemarks = session.IsQrFallback ? "QR and PIN authentication passed." : "NFC and PIN authentication passed.";
        return await GrantAsync(session, logRemarks);
    }

    public async Task<VerificationOutcome> SubmitQrAsync(VerificationSession session, string qrCredential)
    {
        StudentRecord student = session.Student;
        string normalizedInput = qrCredential.Trim();
        string normalizedStored = student.QrCredential.Trim();
        string scanTime = DateTime.Now.ToString("yyyy-MM-dd hh:mm:ss tt");

        if (string.IsNullOrWhiteSpace(normalizedStored) ||
            !normalizedStored.Equals(normalizedInput, StringComparison.OrdinalIgnoreCase))
        {
            string error = "CREDENTIAL_MISMATCH";
            await _database.LogVerificationAsync(student, session.Uid, session.TransactionType, session.Mode, false, error, error, "QR credential did not match the NFC-linked student record.");
            await _database.AddAlertAsync(student.StudentId, error, $"{student.FullName} produced a QR credential that does not match the NFC-linked record.");
            return Denied(session.Uid, student, "ACCESS DENIED", "QR credential mismatch detected", error, $"{scanTime} | {student.StudentId} | {student.FullName} | DENIED | QR MISMATCH");
        }

        return await GrantAsync(session, "NFC, PIN, and QR credential validation passed.");
    }

    private async Task<VerificationOutcome> GrantAsync(VerificationSession session, string remarks)
    {
        StudentRecord student = session.Student;
        string scanTime = DateTime.Now.ToString("yyyy-MM-dd hh:mm:ss tt");

        if (session.TransactionType == TransactionType.Entry)
        {
            await _database.UpdateEntryStateAsync(student.StudentId, "INSIDE");
            student.EntryState = "INSIDE";
        }
        else if (session.TransactionType == TransactionType.Exit)
        {
            if (!string.IsNullOrWhiteSpace(session.EventId))
            {
                await _database.RecordAttendanceAsync(session.EventId, student.StudentId, session.Mode, "DEPARTED", "Event check-out recorded.");
            }
            else
            {
                await _database.UpdateEntryStateAsync(student.StudentId, "OUTSIDE");
                student.EntryState = "OUTSIDE";
            }
        }
        else if (session.TransactionType == TransactionType.EventAttendance)
        {
            await _database.RecordAttendanceAsync(session.EventId, student.StudentId, session.Mode, "PRESENT", remarks);

            await _database.UpdateEntryStateAsync(student.StudentId, "INSIDE");
            student.EntryState = "INSIDE";
        }

        await _database.LogVerificationAsync(student, session.Uid, session.TransactionType, session.Mode, true, "VERIFIED", "", remarks);

        return new VerificationOutcome
        {
            Step = VerificationStep.Completed,
            IsGranted = true,
            ResultTitle = session.TransactionType == TransactionType.EventAttendance ? "ATTENDANCE RECORDED" : "ACCESS GRANTED",
            ResultMessage = session.TransactionType == TransactionType.Exit && !string.IsNullOrWhiteSpace(session.EventId) ? "Event Check-Out Recorded" : remarks,
            Student = student,
            Session = session,
            LogLine = $"{scanTime} | {student.StudentId} | {student.FullName} | GRANTED | {DatabaseService.ToStorageValue(session.TransactionType)} | {DatabaseService.ToStorageValue(session.Mode)}"
        };
    }

    public async Task<VerificationOutcome> BeginQrFallbackVerificationAsync(string qrPayload, VerificationMode mode, TransactionType transactionType, string? eventId)
    {
        string extractedStudentId = qrPayload.Trim();

        if (string.IsNullOrWhiteSpace(extractedStudentId))
        {
            return new VerificationOutcome { IsGranted = false, Step = VerificationStep.Completed, ResultTitle = "INVALID CREDENTIAL", ResultMessage = "QR code payload is empty or unreadable." };
        }

        // ---------------------------------------------------------
        // START DB TIMER: Retrieve Student Profile & PIN Hash (Via QR)
        // ---------------------------------------------------------
        Stopwatch profileTimer = Stopwatch.StartNew();

        StudentRecord? student = await _database.GetStudentByIdAsync(extractedStudentId);

        profileTimer.Stop();
        LogPerformanceMetric("DB Query: Retrieve Profile & PIN Hash (QR)", profileTimer.ElapsedMilliseconds, student != null ? "Found" : "Not Found");
        // ---------------------------------------------------------

        if (student == null)
        {
            return new VerificationOutcome { IsGranted = false, Step = VerificationStep.Completed, ResultTitle = "INVALID CREDENTIAL", ResultMessage = "Student ID not found in the database." };
        }

        return await BeginNfcVerificationAsync(student.NfcUid, mode, transactionType, eventId, isQrFallback: true);
    }

    private static VerificationOutcome Denied(string uid, StudentRecord? student, string title, string message, string errorCategory, string logLine)
    {
        return new VerificationOutcome { Step = VerificationStep.Completed, IsGranted = false, ResultTitle = title, ResultMessage = message, ErrorCategory = errorCategory, Student = student, LogLine = logLine };
    }
}