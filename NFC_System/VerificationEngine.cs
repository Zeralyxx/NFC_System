using System;
using System.Threading.Tasks;

namespace NFC_System;

public sealed class VerificationEngine
{
    private readonly DatabaseService _database;

    public VerificationEngine(DatabaseService database)
    {
        _database = database;
    }

    public async Task<VerificationOutcome> BeginNfcVerificationAsync(string uid, VerificationMode mode, TransactionType transactionType, string eventId)
    {
        StudentRecord? student = await _database.GetStudentByUidAsync(uid);
        string scanTime = DateTime.Now.ToString("yyyy-MM-dd hh:mm:ss tt");

        if (student == null)
        {
            await _database.LogVerificationAsync(null, uid, transactionType, mode, false, "NFC_NOT_REGISTERED", "NOT_REGISTERED", "NFC UID is not linked to a student record.");
            return Denied(uid, null, "ACCESS DENIED", "NFC UID is not registered", "NOT_REGISTERED", $"{scanTime} | UID {uid} | DENIED | NOT REGISTERED");
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

        if (transactionType == TransactionType.Exit && student.EntryState.Equals("OUTSIDE", StringComparison.OrdinalIgnoreCase))
        {
            string error = "IRREGULAR_EXIT_SEQUENCE";
            await _database.LogVerificationAsync(student, uid, transactionType, mode, false, error, error, "Exit attempted while student is already marked OUTSIDE.");
            await _database.AddAlertAsync(student.StudentId, error, $"{student.FullName} attempted exit while already marked OUTSIDE.");
            return Denied(uid, student, "ACCESS DENIED", "Exit blocked because student is already outside", error, $"{scanTime} | {student.StudentId} | {student.FullName} | DENIED | IRREGULAR EXIT");
        }

        if (transactionType == TransactionType.EventAttendance)
        {
            bool allowed = await _database.IsStudentAllowedForEventAsync(eventId, student.StudentId);
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
            EventId = eventId
        };

        if (mode == VerificationMode.Fast)
        {
            return await GrantAsync(session, "NFC validation passed in Fast Mode.");
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
            await _database.UpdatePinFailureAsync(student.StudentId, failedAttempts, locked);

            string error = locked ? "PIN_LOCKED" : "PIN_FAILURE";
            await _database.LogVerificationAsync(student, session.Uid, session.TransactionType, session.Mode, false, error, error, $"Failed PIN attempt {failedAttempts}/3.");

            if (locked)
            {
                await _database.AddAlertAsync(student.StudentId, error, $"{student.FullName} reached three failed PIN attempts and has been locked.");
            }

            return Denied(session.Uid, student, "ACCESS DENIED", locked ? "PIN locked after three failed attempts" : $"Incorrect PIN ({failedAttempts}/3)", error, $"{scanTime} | {student.StudentId} | {student.FullName} | DENIED | {error}");
        }

        await _database.UpdatePinFailureAsync(student.StudentId, 0, false);
        student.FailedPinAttempts = 0;
        student.PinLocked = false;

        if (session.Mode == VerificationMode.HighSecurity)
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

        return await GrantAsync(session, "NFC and PIN authentication passed.");
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
            await _database.UpdateEntryStateAsync(student.StudentId, "OUTSIDE");
            student.EntryState = "OUTSIDE";
        }
        else if (session.TransactionType == TransactionType.EventAttendance)
        {
            await _database.RecordAttendanceAsync(session.EventId, student.StudentId, session.Mode, "PRESENT", remarks);
        }

        await _database.LogVerificationAsync(student, session.Uid, session.TransactionType, session.Mode, true, "VERIFIED", "", remarks);

        return new VerificationOutcome
        {
            Step = VerificationStep.Completed,
            IsGranted = true,
            ResultTitle = session.TransactionType == TransactionType.EventAttendance ? "ATTENDANCE RECORDED" : "ACCESS GRANTED",
            ResultMessage = remarks,
            Student = student,
            Session = session,
            LogLine = $"{scanTime} | {student.StudentId} | {student.FullName} | GRANTED | {DatabaseService.ToStorageValue(session.TransactionType)} | {DatabaseService.ToStorageValue(session.Mode)}"
        };
    }

    private static VerificationOutcome Denied(string uid, StudentRecord? student, string title, string message, string errorCategory, string logLine)
    {
        return new VerificationOutcome
        {
            Step = VerificationStep.Completed,
            IsGranted = false,
            ResultTitle = title,
            ResultMessage = message,
            ErrorCategory = errorCategory,
            Student = student,
            LogLine = logLine
        };
    }
}
