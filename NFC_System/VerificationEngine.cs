using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace NFC_System;

public sealed class VerificationEngine
{
    private readonly DatabaseService _database;
    private readonly QrCredentialService _qrCredentials;

    public VerificationEngine(DatabaseService database, QrCredentialService? qrCredentials = null)
    {
        _database = database;
        _database.ActivateAttendanceDevice();
        _qrCredentials = qrCredentials ?? new QrCredentialService();
    }

    private void LogPerformanceMetric(string operation, double elapsedMs, string result)
    {
        Task.Run(() =>
        {
            try
            {
                string logDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
                Directory.CreateDirectory(logDirectory);
                string logFile = Path.Combine(logDirectory, "System_Performance_Metrics.csv");

                bool isNewFile = !File.Exists(logFile);
                using var writer = new StreamWriter(logFile, true);

                if (isNewFile)
                {
                    writer.WriteLine("Timestamp,Operation (Module),Elapsed Time,Result");
                }

                TimeSpan t = TimeSpan.FromMilliseconds(elapsedMs);
                var parts = new System.Collections.Generic.List<string>();
                if (t.Hours > 0) parts.Add($"{t.Hours}h");
                if (t.Minutes > 0) parts.Add($"{t.Minutes}m");
                if (t.Seconds > 0) parts.Add($"{t.Seconds}s");
                if (t.Milliseconds > 0 || parts.Count == 0) parts.Add($"{t.Milliseconds}ms");

                string formattedTime = string.Join(" ", parts);

                writer.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff},\"{operation}\",\"{formattedTime}\",\"{result}\"");
            }
            catch { }
        });
    }

    public Task<VerificationOutcome> BeginNfcVerificationAsync(string uid, VerificationMode mode, TransactionType transactionType, string? eventId = null) =>
        BeginStudentVerificationAsync(uid, mode, transactionType, eventId);

    private async Task<VerificationOutcome> BeginStudentVerificationAsync(string uid, VerificationMode mode, TransactionType transactionType,
        string? eventId, string? verifiedQr = null, QrCredentialPayload? qrPayload = null, bool forceOffline = false)
    {
        if (!OfflineCacheService.IsAttendanceStorageAvailable())
            return Denied(uid, null, "UNABLE TO RECORD ACCESS", "Please contact security personnel. Attendance storage needs attention.",
                "ATTENDANCE_STORAGE_UNAVAILABLE", "Attendance outbox could not be read; access denied.");
        bool isQrFallback = verifiedQr != null;
        Stopwatch authTimer = Stopwatch.StartNew();
        Stopwatch dbTimer = new Stopwatch();
        string scanTime = DateTime.Now.ToString("yyyy-MM-dd hh:mm:ss tt");

        StudentRecord? student = null;
        bool isOffline = forceOffline || !DatabaseMonitor.IsOnline;

        if (!isOffline)
        {
            dbTimer.Start();
            try { student = await _database.GetStudentByUidAsync(uid); }
            catch { isOffline = true; }
            dbTimer.Stop();

            // Pending local admissions take precedence while recovering, not a stale five-minute cache.
            if (student != null)
            {
                student.EntryState = OfflineCacheService.GetPendingStudentEntryState(student.StudentId) ?? student.EntryState;
                var localCache = OfflineCacheService.GetCachedStudents().FirstOrDefault(s => s.StudentId == student.StudentId);
                if (localCache != null)
                {
                    student.FailedPinAttempts = localCache.FailedPinAttempts;
                    student.PinLocked = localCache.PinLocked;
                }
            }
        }

        double dbQueryMs = dbTimer.Elapsed.TotalMilliseconds;
        LogPerformanceMetric("Database Query Performance Monitor", dbQueryMs, student != null ? $"Initial Profile Retrieval: Found ({(isOffline ? "OFFLINE CACHE" : "ONLINE")})" : "Initial Profile Retrieval: Not Found");

        if (isOffline)
        {
            var offlineResult = OfflineCacheService.VerifyStudentOffline(uid, transactionType.ToString());

            if (offlineResult.Student != null)
            {
                student = new StudentRecord
                {
                    StudentId = offlineResult.Student.StudentId,
                    FullName = offlineResult.Student.FullName,
                    NfcUid = offlineResult.Student.NfcUid,
                    PinHash = offlineResult.Student.PinHash,
                    PinSalt = offlineResult.Student.PinSalt,
                    Status = offlineResult.Student.Status,
                    PinLocked = offlineResult.Student.PinLocked,
                    EntryState = offlineResult.Student.EntryState,
                    FailedPinAttempts = offlineResult.Student.FailedPinAttempts,
                    QrCredential = offlineResult.Student.QrCredential
                };
            }

            if (!offlineResult.IsGranted && offlineResult.ErrorCode != "VERIFIED")
            {
                authTimer.Stop();

                OfflineCacheService.SaveOfflineGateLog(
                    student?.StudentId ?? "",
                    student?.FullName ?? "UNKNOWN USER",
                    uid,
                    transactionType.ToString(),
                    mode.ToString(),
                    false,
                    offlineResult.ErrorCode,
                    offlineResult.Remarks,
                    authTimer.Elapsed.TotalMilliseconds, 0, 0, 0, 0, 0, authTimer.Elapsed.TotalMilliseconds, 0
                );

                return Denied(uid, student, "OFFLINE: ACCESS DENIED", offlineResult.Remarks, offlineResult.ErrorCode, $"{scanTime} | UID {uid} | OFFLINE DENIED | {offlineResult.ErrorCode}");
            }

            if (transactionType == TransactionType.EventAttendance && !string.IsNullOrWhiteSpace(eventId))
            {
                var eventOffline = OfflineCacheService.VerifyEventAttendeeOffline(eventId, student!.StudentId);
                if (!eventOffline.IsAllowed)
                {
                    OfflineCacheService.SaveOfflineEventLog(eventId, student.StudentId, mode.ToString(), "DENIED", eventOffline.Remarks);
                    return Denied(uid, student, "OFFLINE: EVENT DENIED", eventOffline.Remarks, "UNAUTHORIZED_EVENT_ACCESS", $"{scanTime} | {student.StudentId} | OFFLINE EVENT DENIED | UNAUTHORIZED");
                }
            }
        }
        else
        {
            if (student == null)
            {
                string error = "NOT_REGISTERED";
                await SafeLogGateAsync(null, uid, transactionType, mode, false, error, "NFC UID is not linked to a student record.", authTimer.Elapsed.TotalMilliseconds, 0, 0, 0, 0, dbQueryMs, isOffline);
                return Denied(uid, null, "UNAUTHORIZED", "NFC credential is not registered", error, $"{scanTime} | UID {uid} | DENIED | NOT REGISTERED");
            }

            if (!student.Status.Equals("Active", StringComparison.OrdinalIgnoreCase))
            {
                string error = "INACTIVE_STUDENT";
                await SafeLogGateAsync(student, uid, transactionType, mode, false, error, $"Student status is {student.Status}.", authTimer.Elapsed.TotalMilliseconds, 0, 0, 0, 0, dbQueryMs, isOffline);
                return Denied(uid, student, "ACCESS DENIED", $"Student status is {student.Status}", error, $"{scanTime} | {student.StudentId} | {student.FullName} | DENIED | {student.Status.ToUpper()}");
            }

            if (transactionType == TransactionType.Entry && student.EntryState.Equals("INSIDE", StringComparison.OrdinalIgnoreCase))
            {
                string error = "ANTI_TAILGATING_VIOLATION";
                await SafeLogGateAsync(student, uid, transactionType, mode, false, error, "Consecutive entry attempt detected before an exit transaction.", authTimer.Elapsed.TotalMilliseconds, 0, 0, 0, 0, dbQueryMs, isOffline);
                return Denied(uid, student, "ACCESS DENIED", "Anti-tailgating rule blocked repeated entry", error, $"{scanTime} | {student.StudentId} | {student.FullName} | DENIED | TAILGATING");
            }

            if (transactionType == TransactionType.Exit && string.IsNullOrWhiteSpace(eventId) && student.EntryState.Equals("OUTSIDE", StringComparison.OrdinalIgnoreCase))
            {
                string error = "IRREGULAR_EXIT_SEQUENCE";
                await SafeLogGateAsync(student, uid, transactionType, mode, false, error, "Exit attempted while student is already marked OUTSIDE.", authTimer.Elapsed.TotalMilliseconds, 0, 0, 0, 0, dbQueryMs, isOffline);
                return Denied(uid, student, "ACCESS DENIED", "Exit blocked because student is already outside", error, $"{scanTime} | {student.StudentId} | {student.FullName} | DENIED | IRREGULAR EXIT");
            }

            if (transactionType == TransactionType.Exit && !string.IsNullOrWhiteSpace(eventId))
            {
                dbTimer.Restart();
                bool hasEntered = await _database.HasStudentEnteredEventAsync(eventId, student.StudentId);
                dbTimer.Stop();
                dbQueryMs += dbTimer.Elapsed.TotalMilliseconds;

                if (!hasEntered)
                {
                    string error = "IRREGULAR_EVENT_EXIT";
                    await SafeLogEventAsync(eventId, student.StudentId, mode, "DENIED", "Event exit attempted without prior check-in.", isOffline);
                    return Denied(uid, student, "ATTENDANCE DENIED", "Cannot check out without checking in first", error, $"{scanTime} | {student.StudentId} | {student.FullName} | EVENT DENIED | IRREGULAR EXIT");
                }
            }

            if (transactionType == TransactionType.EventAttendance && !string.IsNullOrWhiteSpace(eventId))
            {
                dbTimer.Restart();
                bool allowed = await _database.IsStudentAllowedForEventAsync(eventId, student.StudentId);
                dbTimer.Stop();
                dbQueryMs += dbTimer.Elapsed.TotalMilliseconds;

                if (!allowed)
                {
                    string error = "UNAUTHORIZED_EVENT_ACCESS";
                    await SafeLogEventAsync(eventId, student.StudentId, mode, "DENIED", $"Student is not approved for event {eventId}.", isOffline);
                    return Denied(uid, student, "ATTENDANCE DENIED", "Student is not on the approved event list", error, $"{scanTime} | {student.StudentId} | {student.FullName} | EVENT DENIED | UNAUTHORIZED");
                }
            }
        }

        if (isQrFallback && (student == null || qrPayload == null || !QrCredentialService.MatchesCurrent(verifiedQr!, qrPayload, student)))
        {
            await SafeLogGateAsync(student, uid, transactionType, mode, false, "QR_REPLACED_OR_MISMATCH",
                "QR fallback credential is not the student's current credential.", 0, 0, 0, 0, authTimer.Elapsed.TotalMilliseconds, dbQueryMs, isOffline);
            return Denied(uid, student, "INVALID CREDENTIAL", "QR credential has been replaced or does not match.", "QR_REPLACED_OR_MISMATCH", "QR fallback denied: credential mismatch.");
        }

        authTimer.Stop();
        var session = new VerificationSession
        {
            Student = student!,
            Uid = uid,
            Mode = mode,
            TransactionType = transactionType,
            EventId = eventId,
            IsQrFallback = isQrFallback,
            FallbackQrCredential = verifiedQr,
            NfcSystemMs = isQrFallback ? 0 : authTimer.Elapsed.TotalMilliseconds,
            TotalDbQueryMs = dbQueryMs,
            IsOffline = isOffline
        };

        if (student!.PinLocked)
        {
            string error = "PIN_LOCKED";
            await SafeLogGateAsync(student, uid, transactionType, mode, false, error, "PIN verification is locked after repeated failed attempts.", session.NfcSystemMs, 0, 0, 0, 0, session.TotalDbQueryMs, session.IsOffline);
            return Denied(uid, student, isOffline ? "OFFLINE: ACCESS DENIED" : "ACCESS DENIED", "PIN is locked after repeated failed attempts", error, $"{scanTime} | {student.StudentId} | {student.FullName} | DENIED | PIN LOCKED");
        }

        if (isQrFallback)
        {
            return new VerificationOutcome
            {
                Step = VerificationStep.RequiresPin,
                IsGranted = false,
                ResultTitle = isOffline ? "OFFLINE: QR ACCEPTED" : "QR ACCEPTED",
                ResultMessage = "Please enter your PIN to verify identity",
                Student = student,
                Session = session,
                LogLine = $"{scanTime} | {student.StudentId} | {student.FullName} | {(isOffline ? "OFFLINE " : "")}QR OK | PIN REQUIRED"
            };
        }

        // 1. EVALUATING FAST MODE (1FA: Tap-and-Go)
        if (mode == VerificationMode.Fast || transactionType == TransactionType.Exit)
        {
            return await GrantAsync(session, transactionType == TransactionType.Exit ? "NFC validation passed." : "NFC validation passed in Fast Mode.");
        }

        // 2. ROUTING TO STANDARD OR HIGH-SECURITY MODE (Requires PIN)
        return new VerificationOutcome
        {
            Step = VerificationStep.RequiresPin,
            IsGranted = false,
            ResultTitle = isOffline ? "OFFLINE: PIN REQUIRED" : "PIN REQUIRED",
            ResultMessage = mode == VerificationMode.HighSecurity ? "Enter PIN before QR credential validation" : "Enter student PIN to complete Standard Mode",
            Student = student,
            Session = session,
            LogLine = $"{scanTime} | {student.StudentId} | {student.FullName} | {(isOffline ? "OFFLINE " : "")}NFC OK | PIN REQUIRED"
        };
    }

    public async Task<VerificationOutcome> SubmitPinAsync(VerificationSession session, string pin, double uiPinTimeMs = 0)
    {
        if (session.NextStep != VerificationStep.RequiresPin || session.Student.PinLocked)
            return Denied(session.Uid, session.Student, "ACCESS DENIED", "Start a new verification attempt.", "INVALID_SEQUENCE", "PIN denied: inactive or locked session.");
        session.NextStep = VerificationStep.Completed;
        Stopwatch authTimer = Stopwatch.StartNew();
        Stopwatch dbTimer = new Stopwatch();
        double dbQueryMs = 0;

        StudentRecord student = session.Student;
        string scanTime = DateTime.Now.ToString("yyyy-MM-dd hh:mm:ss tt");

        if (!PinHasher.VerifyPin(pin, student.PinSalt, student.PinHash))
        {
            int failedAttempts = student.FailedPinAttempts + 1;
            bool locked = failedAttempts >= 3;

            student.FailedPinAttempts = failedAttempts;
            student.PinLocked = locked;

            string error = locked ? "PIN_LOCKED" : "PIN_FAILURE";

            if (!session.IsOffline)
            {
                try
                {
                    dbTimer.Start();
                    await _database.UpdatePinFailureAsync(student.StudentId, failedAttempts, locked);
                    dbTimer.Stop();
                    dbQueryMs = dbTimer.Elapsed.TotalMilliseconds;
                    if (locked) await _database.AddAlertAsync(student.StudentId, error, $"{student.FullName} reached three failed PIN attempts and has been locked.");
                }
                catch { session.IsOffline = true; }
            }

            // UNCONDITIONAL LOCAL STATE UPDATE: Always lock them out in RAM cache immediately
            OfflineCacheService.UpdateCachedStudentPinProgress(student.StudentId, failedAttempts, locked);

            authTimer.Stop();
            session.PinWorkflowMs = uiPinTimeMs;
            session.PinSystemMs = authTimer.Elapsed.TotalMilliseconds;
            session.TotalDbQueryMs += dbQueryMs;

            await SafeLogGateAsync(student, session.Uid, session.TransactionType, session.Mode, false, error, $"Failed PIN attempt {failedAttempts}/3.", session.NfcSystemMs, session.PinWorkflowMs, session.PinSystemMs, session.QrWorkflowMs, session.QrSystemMs, session.TotalDbQueryMs, session.IsOffline);

            // Reopen only after persistence/logging finishes so overlapping submissions cannot count twice.
            if (!locked) session.NextStep = VerificationStep.RequiresPin;
            return Denied(session.Uid, student, "ACCESS DENIED", locked ? "PIN locked after three failed attempts" : $"Incorrect PIN ({failedAttempts}/3)", error, $"{scanTime} | {student.StudentId} | {student.FullName} | DENIED | {error}", retrySession: locked ? null : session);
        }

        if (!session.IsOffline)
        {
            try
            {
                dbTimer.Restart();
                await _database.UpdatePinFailureAsync(student.StudentId, 0, false);
                dbTimer.Stop();
                dbQueryMs = dbTimer.Elapsed.TotalMilliseconds;
            }
            catch { session.IsOffline = true; }
        }

        // UNCONDITIONAL LOCAL STATE UPDATE: Clear lockouts
        OfflineCacheService.UpdateCachedStudentPinProgress(student.StudentId, 0, false);

        student.FailedPinAttempts = 0;
        student.PinLocked = false;

        authTimer.Stop();
        session.PinWorkflowMs = uiPinTimeMs;
        session.PinSystemMs = authTimer.Elapsed.TotalMilliseconds;
        session.TotalDbQueryMs += dbQueryMs;

        // 3. EVALUATING HIGH-SECURITY MODE (3FA: Demands QR Code after PIN)
        if (session.Mode == VerificationMode.HighSecurity && !session.IsQrFallback)
        {
            session.NextStep = VerificationStep.RequiresQr;
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

        if (session.IsQrFallback)
        {
            var current = await GetCurrentQrStudentAsync(session);
            if (!_qrCredentials.TryVerify(session.FallbackQrCredential, out var payload, out _) || current == null ||
                !current.Status.Equals("Active", StringComparison.OrdinalIgnoreCase) ||
                !QrCredentialService.MatchesCurrent(session.FallbackQrCredential!, payload!, current))
            {
                session.NextStep = VerificationStep.Completed;
                await SafeLogGateAsync(student, session.Uid, session.TransactionType, session.Mode, false, "QR_REPLACED_OR_MISMATCH",
                    "QR fallback credential is no longer current or trusted.", 0, session.PinWorkflowMs, session.PinSystemMs,
                    session.QrWorkflowMs, session.QrSystemMs, session.TotalDbQueryMs, session.IsOffline);
                return Denied(session.Uid, student, "ACCESS DENIED", "QR credential is no longer valid.", "QR_REPLACED_OR_MISMATCH", "QR fallback denied: credential changed.");
            }
        }
        return await GrantAsync(session, session.IsQrFallback ? "Signed QR fallback and PIN authentication passed." : "NFC and PIN authentication passed.");
    }

    public async Task<VerificationOutcome> SubmitQrAsync(VerificationSession session, string qrCredential, double uiQrTimeMs = 0)
    {
        if (session.NextStep != VerificationStep.RequiresQr)
            return Denied(session.Uid, session.Student, "ACCESS DENIED", "Complete PIN verification first.", "INVALID_SEQUENCE", "QR denied: unexpected verification step.");
        session.NextStep = VerificationStep.Completed;
        Stopwatch authTimer = Stopwatch.StartNew();

        StudentRecord student = session.Student;
        string normalizedInput = qrCredential.Trim();
        bool valid = _qrCredentials.TryVerify(normalizedInput, out var payload, out string qrError);
        var current = valid ? await GetCurrentQrStudentAsync(session) : null;
        bool matches = valid && current != null && current.Status.Equals("Active", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(current.NfcUid, session.Uid, StringComparison.OrdinalIgnoreCase) &&
            QrCredentialService.MatchesCurrent(normalizedInput, payload!, current);
        string scanTime = DateTime.Now.ToString("yyyy-MM-dd hh:mm:ss tt");

        authTimer.Stop();

        session.QrWorkflowMs = uiQrTimeMs;
        session.QrSystemMs = authTimer.Elapsed.TotalMilliseconds;

        if (!matches)
        {
            session.NextStep = VerificationStep.Completed;
            string error = valid ? "QR_REPLACED_OR_MISMATCH" : qrError;
            await SafeLogGateAsync(student, session.Uid, session.TransactionType, session.Mode, false, error, "QR credential did not match the NFC-linked student record.", session.NfcSystemMs, session.PinWorkflowMs, session.PinSystemMs, session.QrWorkflowMs, session.QrSystemMs, session.TotalDbQueryMs, session.IsOffline);
            return Denied(session.Uid, student, "ACCESS DENIED", "QR credential mismatch detected", error, $"{scanTime} | {student.StudentId} | {student.FullName} | DENIED | QR MISMATCH");
        }

        return await GrantAsync(session, "NFC, PIN, and QR credential validation passed.");
    }

    public async Task<VerificationOutcome> BeginQrFallbackVerificationAsync(string qrPayload, VerificationMode mode, TransactionType transactionType, string? eventId, double uiQrTimeMs = 0)
    {
        if (!OfflineCacheService.IsAttendanceStorageAvailable())
            return Denied("", null, "UNABLE TO RECORD ACCESS", "Please contact security personnel. Attendance storage needs attention.",
                "ATTENDANCE_STORAGE_UNAVAILABLE", "Attendance outbox could not be read; access denied.");
        qrPayload = qrPayload.Trim();
        if (!_qrCredentials.TryVerify(qrPayload, out var signedPayload, out string error))
        {
            await SafeLogGateAsync(null, "", transactionType, mode, false, error, "Signed QR fallback validation failed.",
                0, 0, 0, uiQrTimeMs, 0, 0, !DatabaseMonitor.IsOnline);
            return Denied("", null, "INVALID CREDENTIAL", "QR signature could not be verified.", error, "QR fallback denied: invalid signature or unavailable verification key.");
        }
        string extractedStudentId = signedPayload!.StudentId;
        bool isOffline = !DatabaseMonitor.IsOnline;

        StudentRecord? student = null;
        Stopwatch dbTimer = Stopwatch.StartNew();

        try
        {
            if (!isOffline)
            {
                student = await _database.GetStudentByIdAsync(extractedStudentId);
            }
        }
        catch { isOffline = true; }

        if (isOffline)
        {
            var cached = OfflineCacheService.GetCachedStudents().FirstOrDefault(s => s.StudentId.Equals(extractedStudentId, StringComparison.Ordinal));
            if (cached != null)
            {
                student = new StudentRecord
                {
                    StudentId = cached.StudentId,
                    FullName = cached.FullName,
                    NfcUid = cached.NfcUid,
                    PinHash = cached.PinHash,
                    PinSalt = cached.PinSalt,
                    Status = cached.Status,
                    PinLocked = cached.PinLocked,
                    EntryState = cached.EntryState,
                    FailedPinAttempts = cached.FailedPinAttempts,
                    QrCredential = cached.QrCredential
                };
            }
        }

        dbTimer.Stop();

        if (student == null)
        {
            await SafeLogGateAsync(null, "", transactionType, mode, false, "NOT_REGISTERED", "Signed QR student not found.",
                0, 0, 0, uiQrTimeMs, 0, dbTimer.Elapsed.TotalMilliseconds, isOffline);
            return Denied("", null, "INVALID CREDENTIAL", "Student ID not found in database or offline cache.", "NOT_REGISTERED", "QR fallback denied: student not found.");
        }

        var outcome = await BeginStudentVerificationAsync(student.NfcUid, mode, transactionType, eventId, qrPayload, signedPayload, isOffline);

        if (outcome.Session != null)
        {
            outcome.Session.QrWorkflowMs = uiQrTimeMs;
            outcome.Session.QrSystemMs = dbTimer.Elapsed.TotalMilliseconds;
            outcome.Session.TotalDbQueryMs += dbTimer.Elapsed.TotalMilliseconds;
        }

        return outcome;
    }

    private async Task<StudentRecord?> GetCurrentQrStudentAsync(VerificationSession session)
    {
        if (!session.IsOffline)
        {
            var timer = Stopwatch.StartNew();
            try { return await _database.GetStudentByIdAsync(session.Student.StudentId); }
            catch { session.IsOffline = true; }
            finally { session.TotalDbQueryMs += timer.Elapsed.TotalMilliseconds; }
        }
        var cached = OfflineCacheService.GetCachedStudents().FirstOrDefault(s => s.StudentId == session.Student.StudentId);
        return cached == null ? null : new StudentRecord
        {
            StudentId = cached.StudentId,
            NfcUid = cached.NfcUid,
            Status = cached.Status,
            QrCredential = cached.QrCredential
        };
    }

    private async Task<VerificationOutcome> GrantAsync(VerificationSession session, string remarks)
    {
        session.NextStep = VerificationStep.Completed;
        StudentRecord student = session.Student;
        AttendanceCommitResult result;
        try
        {
            result = await _database.RecordAttendanceTransactionAsync(session, remarks);
        }
        catch (Exception ex)
        {
            // Do not grant an unrecorded admission when durable local storage is unavailable.
            return new VerificationOutcome
            {
                Student = student, Session = session, IsGranted = false,
                ErrorCategory = "ATTENDANCE_STORAGE_UNAVAILABLE",
                ResultTitle = "UNABLE TO RECORD ACCESS",
                ResultMessage = "Please contact security personnel. Attendance storage needs attention.",
                LogLine = $"Attendance storage failed: {ex.Message}"
            };
        }
        session.IsOffline = !result.Committed;
        if (result.ConfirmationPending)
        {
            return new VerificationOutcome
            {
                Student = student, Session = session, Timestamp = result.Timestamp, IsGranted = false,
                ErrorCategory = "ATTENDANCE_CONFIRMATION_PENDING", ResultTitle = "CONFIRMATION PENDING",
                ResultMessage = "Please wait for security personnel to confirm this attempt before entering.",
                LogLine = $"{result.Timestamp:yyyy-MM-dd HH:mm:ss} | {student.StudentId} | UNCONFIRMED | {result.TransactionId}"
            };
        }
        if (!result.IsGranted)
        {
            return new VerificationOutcome
            {
                Student = student, Session = session, Timestamp = result.Timestamp, IsGranted = false,
                ErrorCategory = "ATTENDANCE_SEQUENCE_CONFLICT", ResultTitle = "PLEASE SCAN AGAIN",
                ResultMessage = "Attendance changed during verification. Please ask security personnel if this continues.",
                LogLine = $"{result.Timestamp:yyyy-MM-dd HH:mm:ss} | {student.StudentId} | DENIED | ATTENDANCE_SEQUENCE_CONFLICT"
            };
        }
        bool isOffline = session.IsOffline;
        string nameForLog = student.IsTemporary ? $"[TEMP] {student.FullName}" : student.FullName;
        return new VerificationOutcome
        {
            Step = VerificationStep.Completed, IsGranted = true,
            ResultTitle = session.TransactionType == TransactionType.EventAttendance ? (isOffline ? "OFFLINE ATTENDANCE" : "ATTENDANCE RECORDED") : (isOffline ? "OFFLINE: ACCESS GRANTED" : "ACCESS GRANTED"),
            ResultMessage = session.TransactionType == TransactionType.Exit && !string.IsNullOrWhiteSpace(session.EventId) ? "Event Check-Out Recorded" : remarks,
            Student = student, Session = session, Timestamp = result.Timestamp, Visit = result.Visit,
            LogLine = $"{result.Timestamp:yyyy-MM-dd hh:mm:ss tt} | {student.StudentId} | {nameForLog} | {(isOffline ? "OFFLINE GRANTED" : "GRANTED")} | {DatabaseService.ToStorageValue(session.TransactionType)} | {DatabaseService.ToStorageValue(session.Mode)}"
        };
    }

    private async Task SafeLogGateAsync(StudentRecord? student, string uid, TransactionType type, VerificationMode mode, bool granted, string errorCategory, string remarks, double nfcSystemMs, double pinWorkflowMs, double pinSystemMs, double qrWorkflowMs, double qrSystemMs, double dbQuerySpeedMs, bool isOffline)
    {
        if (dbQuerySpeedMs > 0)
        {
            LogPerformanceMetric("Database Query Performance Monitor", dbQuerySpeedMs, "Total Aggregated Transaction Queries");
        }

        string? loggedName = student?.FullName;
        if (student != null && student.IsTemporary) loggedName = $"[TEMP] {loggedName}";

        if (!isOffline)
        {
            try
            {
                await _database.LogVerificationAsync(student, loggedName, uid, type, mode, granted, errorCategory, errorCategory, remarks, nfcSystemMs, pinWorkflowMs, pinSystemMs, qrWorkflowMs, qrSystemMs, dbQuerySpeedMs);
                if (!granted && student != null) await _database.AddAlertAsync(student.StudentId, errorCategory, remarks);
                return;
            }
            catch { }
        }

        double totalWorkflowMs = pinWorkflowMs + qrWorkflowMs;
        double totalSystemMs = nfcSystemMs + pinSystemMs + qrSystemMs;

        OfflineCacheService.SaveOfflineGateLog(
            student?.StudentId ?? "",
            loggedName ?? "",
            uid,
            DatabaseService.ToStorageValue(type),
            DatabaseService.ToStorageValue(mode),
            granted,
            errorCategory,
            remarks,
            nfcSystemMs, pinWorkflowMs, pinSystemMs, qrWorkflowMs, qrSystemMs, totalWorkflowMs, totalSystemMs, dbQuerySpeedMs
        );
    }

    private async Task SafeLogEventAsync(string eventId, string studentId, VerificationMode mode, string status, string remarks, bool isOffline)
    {
        if (!isOffline)
        {
            try
            {
                await _database.RecordAttendanceAsync(eventId, studentId, mode, status, remarks);
                return;
            }
            catch { }
        }
        OfflineCacheService.SaveOfflineEventLog(eventId, studentId, DatabaseService.ToStorageValue(mode), status, remarks);
    }

    private static VerificationOutcome Denied(string uid, StudentRecord? student, string title, string message, string errorCategory, string logLine, VerificationSession? retrySession = null)
    {
        string finalLogLine = logLine;

        if (student != null && student.IsTemporary && !logLine.Contains("[TEMP]"))
        {
            finalLogLine = logLine.Replace(student.FullName, $"[TEMP] {student.FullName}");
        }

        return new VerificationOutcome
        {
            Step = retrySession == null ? VerificationStep.Completed : VerificationStep.RequiresPin,
            IsGranted = false, ResultTitle = title, ResultMessage = message, ErrorCategory = errorCategory,
            Student = student, Session = retrySession, LogLine = finalLogLine
        };
    }
}
