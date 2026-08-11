using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace NFC_System;

public sealed class VerificationEngine
{
    private readonly DatabaseService _database;

    public VerificationEngine(DatabaseService database)
    {
        _database = database;
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

    public async Task<VerificationOutcome> BeginNfcVerificationAsync(string uid, VerificationMode mode, TransactionType transactionType, string? eventId = null, bool isQrFallback = false)
    {
        Stopwatch authTimer = Stopwatch.StartNew();
        Stopwatch dbTimer = new Stopwatch();
        string scanTime = DateTime.Now.ToString("yyyy-MM-dd hh:mm:ss tt");

        StudentRecord? student = null;

        // THE FIX: Instantly drop to offline mode without suffering the 3-second ADO.NET timeout
        bool isOffline = !DatabaseMonitor.IsOnline;

        if (!isOffline)
        {
            dbTimer.Start();
            try
            {
                student = await _database.GetStudentByUidAsync(uid);
            }
            catch
            {
                isOffline = true;
            }
            dbTimer.Stop();
        }

        double dbQueryMs = dbTimer.Elapsed.TotalMilliseconds;
        LogPerformanceMetric("Database Query Performance Monitor", dbQueryMs, student != null ? $"Initial Profile Retrieval: Found ({(isOffline ? "OFFLINE CACHE" : "ONLINE")})" : "Initial Profile Retrieval: Not Found");

        if (isOffline)
        {
            // THE FIX: Pass the transactionType to enforce strict offline anti-tailgating
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

                    // THE FIX: You missed these two mappings!
                    FailedPinAttempts = offlineResult.Student.FailedPinAttempts,
                    QrCredential = offlineResult.Student.QrCredential
                };
            }

            if (!offlineResult.IsGranted && offlineResult.ErrorCode != "VERIFIED")
            {
                authTimer.Stop(); // Stop the timer so we can log the hardware speed

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
                await SafeLogGateAsync(null, uid, transactionType, mode, false, error, "NFC UID is not linked to a student record.", authTimer.Elapsed.TotalMilliseconds, 0, 0, 0, 0, dbQueryMs);
                return Denied(uid, null, "UNAUTHORIZED", "NFC credential is not registered", error, $"{scanTime} | UID {uid} | DENIED | NOT REGISTERED");
            }

            if (!student.Status.Equals("Active", StringComparison.OrdinalIgnoreCase))
            {
                string error = "INACTIVE_STUDENT";
                await SafeLogGateAsync(student, uid, transactionType, mode, false, error, $"Student status is {student.Status}.", authTimer.Elapsed.TotalMilliseconds, 0, 0, 0, 0, dbQueryMs);
                return Denied(uid, student, "ACCESS DENIED", $"Student status is {student.Status}", error, $"{scanTime} | {student.StudentId} | {student.FullName} | DENIED | {student.Status.ToUpper()}");
            }

            if (transactionType == TransactionType.Entry && student.EntryState.Equals("INSIDE", StringComparison.OrdinalIgnoreCase))
            {
                string error = "ANTI_TAILGATING_VIOLATION";
                await SafeLogGateAsync(student, uid, transactionType, mode, false, error, "Consecutive entry attempt detected before an exit transaction.", authTimer.Elapsed.TotalMilliseconds, 0, 0, 0, 0, dbQueryMs);
                return Denied(uid, student, "ACCESS DENIED", "Anti-tailgating rule blocked repeated entry", error, $"{scanTime} | {student.StudentId} | {student.FullName} | DENIED | TAILGATING");
            }

            if (transactionType == TransactionType.Exit && string.IsNullOrWhiteSpace(eventId) && student.EntryState.Equals("OUTSIDE", StringComparison.OrdinalIgnoreCase))
            {
                string error = "IRREGULAR_EXIT_SEQUENCE";
                await SafeLogGateAsync(student, uid, transactionType, mode, false, error, "Exit attempted while student is already marked OUTSIDE.", authTimer.Elapsed.TotalMilliseconds, 0, 0, 0, 0, dbQueryMs);
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
                    await SafeLogEventAsync(eventId, student.StudentId, mode, "DENIED", "Event exit attempted without prior check-in.");
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
                    await SafeLogEventAsync(eventId, student.StudentId, mode, "DENIED", $"Student is not approved for event {eventId}.");
                    return Denied(uid, student, "ATTENDANCE DENIED", "Student is not on the approved event list", error, $"{scanTime} | {student.StudentId} | {student.FullName} | EVENT DENIED | UNAUTHORIZED");
                }
            }
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
            NfcSystemMs = isQrFallback ? 0 : authTimer.Elapsed.TotalMilliseconds,
            TotalDbQueryMs = dbQueryMs
        };

        if (student!.PinLocked)
        {
            string error = "PIN_LOCKED";
            await SafeLogGateAsync(student, uid, transactionType, mode, false, error, "PIN verification is locked after repeated failed attempts.", session.NfcSystemMs, 0, 0, 0, 0, session.TotalDbQueryMs);
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

        if (mode == VerificationMode.Fast || transactionType == TransactionType.Exit)
        {
            return await GrantAsync(session, transactionType == TransactionType.Exit ? "NFC validation passed." : "NFC validation passed in Fast Mode.");
        }

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

            if (DatabaseMonitor.IsOnline)
            {
                try
                {
                    dbTimer.Start();
                    await _database.UpdatePinFailureAsync(student.StudentId, failedAttempts, locked);
                    dbTimer.Stop();
                    dbQueryMs = dbTimer.Elapsed.TotalMilliseconds;
                    if (locked) await _database.AddAlertAsync(student.StudentId, error, $"{student.FullName} reached three failed PIN attempts and has been locked.");
                }
                catch { }
            }
            else
            {
                // THE FIX: Persist the offline failure locally so they actually get locked out!
                OfflineCacheService.UpdateCachedStudentPinProgress(student.StudentId, failedAttempts, locked);
            }

            authTimer.Stop();
            session.PinWorkflowMs = uiPinTimeMs;
            session.PinSystemMs = authTimer.Elapsed.TotalMilliseconds;
            session.TotalDbQueryMs += dbQueryMs;

            await SafeLogGateAsync(student, session.Uid, session.TransactionType, session.Mode, false, error, $"Failed PIN attempt {failedAttempts}/3.", session.NfcSystemMs, session.PinWorkflowMs, session.PinSystemMs, session.QrWorkflowMs, session.QrSystemMs, session.TotalDbQueryMs);

            return Denied(session.Uid, student, "ACCESS DENIED", locked ? "PIN locked after three failed attempts" : $"Incorrect PIN ({failedAttempts}/3)", error, $"{scanTime} | {student.StudentId} | {student.FullName} | DENIED | {error}");
        }

        if (DatabaseMonitor.IsOnline)
        {
            try
            {
                dbTimer.Restart();
                await _database.UpdatePinFailureAsync(student.StudentId, 0, false);
                dbTimer.Stop();
                dbQueryMs = dbTimer.Elapsed.TotalMilliseconds;
            }
            catch { }
        }
        else if (student.FailedPinAttempts > 0)
        {
            // THE FIX: Clear offline progress if they get it right
            OfflineCacheService.UpdateCachedStudentPinProgress(student.StudentId, 0, false);
        }

        student.FailedPinAttempts = 0;
        student.PinLocked = false;

        authTimer.Stop();
        session.PinWorkflowMs = uiPinTimeMs;
        session.PinSystemMs = authTimer.Elapsed.TotalMilliseconds;
        session.TotalDbQueryMs += dbQueryMs;

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

        return await GrantAsync(session, session.IsQrFallback ? "QR and PIN authentication passed." : "NFC and PIN authentication passed.");
    }

    public async Task<VerificationOutcome> SubmitQrAsync(VerificationSession session, string qrCredential, double uiQrTimeMs = 0)
    {
        Stopwatch authTimer = Stopwatch.StartNew();

        StudentRecord student = session.Student;
        string normalizedInput = qrCredential.Trim();
        // Safely handles null QR strings from offline cache
        string normalizedStored = student.QrCredential?.Trim() ?? "";
        string scanTime = DateTime.Now.ToString("yyyy-MM-dd hh:mm:ss tt");

        authTimer.Stop();

        session.QrWorkflowMs = uiQrTimeMs;
        session.QrSystemMs = authTimer.Elapsed.TotalMilliseconds;

        if (string.IsNullOrWhiteSpace(normalizedStored) || !normalizedStored.Equals(normalizedInput, StringComparison.OrdinalIgnoreCase))
        {
            string error = "CREDENTIAL_MISMATCH";
            await SafeLogGateAsync(student, session.Uid, session.TransactionType, session.Mode, false, error, "QR credential did not match the NFC-linked student record.", session.NfcSystemMs, session.PinWorkflowMs, session.PinSystemMs, session.QrWorkflowMs, session.QrSystemMs, session.TotalDbQueryMs);
            return Denied(session.Uid, student, "ACCESS DENIED", "QR credential mismatch detected", error, $"{scanTime} | {student.StudentId} | {student.FullName} | DENIED | QR MISMATCH");
        }

        return await GrantAsync(session, "NFC, PIN, and QR credential validation passed.");
    }

    public async Task<VerificationOutcome> BeginQrFallbackVerificationAsync(string qrPayload, VerificationMode mode, TransactionType transactionType, string? eventId, double uiQrTimeMs = 0)
    {
        string extractedStudentId = qrPayload.Trim();

        if (string.IsNullOrWhiteSpace(extractedStudentId))
            return new VerificationOutcome { IsGranted = false, Step = VerificationStep.Completed, ResultTitle = "INVALID CREDENTIAL", ResultMessage = "QR payload is empty or unreadable." };

        StudentRecord? student = null;
        Stopwatch dbTimer = Stopwatch.StartNew();

        try
        {
            if (DatabaseMonitor.IsOnline)
            {
                student = await _database.GetStudentByIdAsync(extractedStudentId);
            }
        }
        catch { }

        // Fallback to offline immediately
        if (student == null)
        {
            string cacheFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "NFC_System", "Cache", "local_students.json");
            if (File.Exists(cacheFile))
            {
                try
                {
                    var list = JsonSerializer.Deserialize<List<CachedStudent>>(File.ReadAllText(cacheFile));
                    var cached = list?.FirstOrDefault(s => s.StudentId.Equals(extractedStudentId, StringComparison.OrdinalIgnoreCase));
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
                            
                            // THE FIX: You missed these two mappings here too!
                            FailedPinAttempts = cached.FailedPinAttempts,
                            QrCredential = cached.QrCredential
                        };
                    }
                }
                catch { }
            }
        }

        dbTimer.Stop();

        if (student == null)
            return new VerificationOutcome { IsGranted = false, Step = VerificationStep.Completed, ResultTitle = "INVALID CREDENTIAL", ResultMessage = "Student ID not found in database or offline cache." };

        var outcome = await BeginNfcVerificationAsync(student.NfcUid, mode, transactionType, eventId, isQrFallback: true);

        if (outcome.Session != null)
        {
            outcome.Session.QrWorkflowMs = uiQrTimeMs;
            outcome.Session.QrSystemMs = dbTimer.Elapsed.TotalMilliseconds;
            outcome.Session.TotalDbQueryMs += dbTimer.Elapsed.TotalMilliseconds;
        }

        return outcome;
    }

    private async Task<VerificationOutcome> GrantAsync(VerificationSession session, string remarks)
    {
        StudentRecord student = session.Student;
        string scanTime = DateTime.Now.ToString("yyyy-MM-dd hh:mm:ss tt");

        bool isOffline = !DatabaseMonitor.IsOnline;
        Stopwatch updateTimer = new Stopwatch();

        if (!isOffline)
        {
            try
            {
                updateTimer.Start();
                if (session.TransactionType == TransactionType.Entry)
                {
                    await _database.UpdateEntryStateAsync(student.StudentId, "INSIDE");
                    student.EntryState = "INSIDE";
                }
                else if (session.TransactionType == TransactionType.Exit)
                {
                    if (!string.IsNullOrWhiteSpace(session.EventId))
                        await _database.RecordAttendanceAsync(session.EventId, student.StudentId, session.Mode, "DEPARTED", "Event check-out recorded.");
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
                updateTimer.Stop();
                session.TotalDbQueryMs += updateTimer.Elapsed.TotalMilliseconds;

                await SafeLogGateAsync(student, session.Uid, session.TransactionType, session.Mode, true, "VERIFIED", remarks, session.NfcSystemMs, session.PinWorkflowMs, session.PinSystemMs, session.QrWorkflowMs, session.QrSystemMs, session.TotalDbQueryMs);
            }
            catch
            {
                isOffline = true;
            }
        }

        // Catch the fallthrough gracefully
        if (isOffline)
        {
            if (session.TransactionType == TransactionType.EventAttendance && !string.IsNullOrWhiteSpace(session.EventId))
            {
                OfflineCacheService.SaveOfflineEventLog(session.EventId, student.StudentId, session.Mode.ToString(), "PRESENT", remarks);
            }
            else
            {
                double totalWorkflowMs = session.PinWorkflowMs + session.QrWorkflowMs;
                double totalSystemMs = session.NfcSystemMs + session.PinSystemMs + session.QrSystemMs;

                OfflineCacheService.SaveOfflineGateLog(
                    student.StudentId,
                    student.FullName,
                    session.Uid,
                    session.TransactionType.ToString(),
                    session.Mode.ToString(),
                    true,
                    "VERIFIED",
                    remarks,
                    session.NfcSystemMs,
                    session.PinWorkflowMs,
                    session.PinSystemMs,
                    session.QrWorkflowMs,
                    session.QrSystemMs,
                    totalWorkflowMs,
                    totalSystemMs,
                    session.TotalDbQueryMs
                );
            }
        }

        string nameForLog = student.IsTemporary ? $"[TEMP] {student.FullName}" : student.FullName;

        return new VerificationOutcome
        {
            Step = VerificationStep.Completed,
            IsGranted = true,
            ResultTitle = session.TransactionType == TransactionType.EventAttendance ? (isOffline ? "OFFLINE ATTENDANCE" : "ATTENDANCE RECORDED") : (isOffline ? "OFFLINE: ACCESS GRANTED" : "ACCESS GRANTED"),
            ResultMessage = session.TransactionType == TransactionType.Exit && !string.IsNullOrWhiteSpace(session.EventId) ? "Event Check-Out Recorded" : remarks,
            Student = student,
            Session = session,
            LogLine = $"{scanTime} | {student.StudentId} | {nameForLog} | {(isOffline ? "OFFLINE GRANTED" : "GRANTED")} | {DatabaseService.ToStorageValue(session.TransactionType)} | {DatabaseService.ToStorageValue(session.Mode)}"
        };
    }

    private async Task SafeLogGateAsync(StudentRecord? student, string uid, TransactionType type, VerificationMode mode, bool granted, string errorCategory, string remarks, double nfcSystemMs, double pinWorkflowMs, double pinSystemMs, double qrWorkflowMs, double qrSystemMs, double dbQuerySpeedMs)
    {
        if (dbQuerySpeedMs > 0)
        {
            LogPerformanceMetric("Database Query Performance Monitor", dbQuerySpeedMs, "Total Aggregated Transaction Queries");
        }

        string? loggedName = student?.FullName;
        if (student != null && student.IsTemporary) loggedName = $"[TEMP] {loggedName}";

        if (DatabaseMonitor.IsOnline)
        {
            try
            {
                await _database.LogVerificationAsync(student, loggedName, uid, type, mode, granted, errorCategory, errorCategory, remarks, nfcSystemMs, pinWorkflowMs, pinSystemMs, qrWorkflowMs, qrSystemMs, dbQuerySpeedMs);
                if (!granted && student != null) await _database.AddAlertAsync(student.StudentId, errorCategory, remarks);
                return;
            }
            catch { }
        }

        // THE FIX: Calculate totals and pass ALL metrics to the offline JSON queue
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

    private async Task SafeLogEventAsync(string eventId, string studentId, VerificationMode mode, string status, string remarks)
    {
        if (DatabaseMonitor.IsOnline)
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

    private static VerificationOutcome Denied(string uid, StudentRecord? student, string title, string message, string errorCategory, string logLine)
    {
        string finalLogLine = logLine;

        if (student != null && student.IsTemporary && !logLine.Contains("[TEMP]"))
        {
            finalLogLine = logLine.Replace(student.FullName, $"[TEMP] {student.FullName}");
        }

        return new VerificationOutcome { Step = VerificationStep.Completed, IsGranted = false, ResultTitle = title, ResultMessage = message, ErrorCategory = errorCategory, Student = student, LogLine = finalLogLine };
    }
}