using MySqlConnector;
using System;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NFC_System;

public sealed partial class DatabaseService
{
    private static readonly SemaphoreSlim AttendanceLock = new(1, 1);
    private static bool _attendanceActive;
    public static string? AttendanceSyncError { get; private set; }
    public void ActivateAttendanceDevice() => _attendanceActive = true;

    private async Task SetAttendanceStateManuallyAsync(string studentId, string entryState)
    {
        await AttendanceLock.WaitAsync();
        try
        {
            using var connection = new MySqlConnection(ConnectionString);
            await connection.OpenAsync();
            await EnsureAttendanceSchemaAsync(connection);
            using var transaction = await connection.BeginTransactionAsync();
            using var update = new MySqlCommand("UPDATE students SET entry_state=@state WHERE student_id=@sid", connection, transaction);
            update.Parameters.AddWithValue("@state", entryState);
            update.Parameters.AddWithValue("@sid", studentId);
            await update.ExecuteNonQueryAsync();
            var state = await ReadAttendanceStateAsync(connection, transaction, studentId) ?? new AttendanceState();
            state.EntryState = entryState;
            state.LastTimestamp = GetNetworkAdjustedTime();
            state.LastDeviceId = "";
            state.LastSequence = 0;
            state.Unresolved = true;
            if (state.Visit != null)
            {
                state.Visit.Confirmed = false;
                await SaveVisitAsync(connection, transaction, studentId, state.Visit);
            }
            await SaveAttendanceStateAsync(connection, transaction, studentId, state);
            await transaction.CommitAsync();
            OfflineCacheService.UpdateCachedStudentStateLocally(studentId, entryState);
        }
        finally { AttendanceLock.Release(); }
    }

    private static async Task EnsureAttendanceSchemaAsync(MySqlConnection connection)
    {
        using (var engines = new MySqlCommand(@"SELECT COUNT(*) FROM information_schema.tables
            WHERE table_schema=DATABASE() AND table_name IN
            ('students','fast_mode_logs','standard_mode_logs','high_security_mode_logs','event_attendance') AND engine<>'InnoDB'", connection))
        {
            if (Convert.ToInt32(await engines.ExecuteScalarAsync()) != 0)
                throw new InvalidOperationException("Attendance synchronization requires InnoDB tables. Ask the database administrator to migrate nontransactional tables before recovery.");
        }
        using var command = new MySqlCommand(@"
            CREATE TABLE IF NOT EXISTS attendance_devices (
                device_id VARCHAR(32) PRIMARY KEY,
                device_name VARCHAR(100) NOT NULL DEFAULT 'Unknown/Legacy',
                last_seen DATETIME(3) NOT NULL,
                reported_sequence BIGINT NOT NULL DEFAULT 0,
                acknowledged_sequence BIGINT NOT NULL DEFAULT 0,
                ready BOOLEAN NOT NULL DEFAULT FALSE,
                enabled BOOLEAN NOT NULL DEFAULT TRUE
            ) ENGINE=InnoDB;
            CREATE TABLE IF NOT EXISTS attendance_receipts (
                transaction_id VARCHAR(32) PRIMARY KEY,
                device_id VARCHAR(32) NOT NULL,
                device_sequence BIGINT NOT NULL,
                UNIQUE KEY device_order (device_id, device_sequence)
            ) ENGINE=InnoDB;
            CREATE TABLE IF NOT EXISTS attendance_current (
                student_id VARCHAR(50) PRIMARY KEY,
                state_json LONGTEXT NOT NULL
            ) ENGINE=InnoDB;
            CREATE TABLE IF NOT EXISTS attendance_decisions (
                transaction_id VARCHAR(32) PRIMARY KEY,
                is_granted BOOLEAN NOT NULL
            ) ENGINE=InnoDB;
            CREATE TABLE IF NOT EXISTS attendance_visits (
                visit_id VARCHAR(32) PRIMARY KEY,
                student_id VARCHAR(50) NOT NULL,
                entry_transaction_id VARCHAR(32) NOT NULL,
                exit_transaction_id VARCHAR(32) NULL,
                time_in DATETIME(3) NOT NULL,
                time_out DATETIME(3) NULL,
                confirmed BOOLEAN NOT NULL,
                INDEX student_visits (student_id, time_in)
            ) ENGINE=InnoDB;", connection);
        await command.ExecuteNonQueryAsync();
        using var metadataEngines = new MySqlCommand(@"SELECT COUNT(*) FROM information_schema.tables
            WHERE table_schema=DATABASE() AND table_name IN
            ('attendance_devices','attendance_receipts','attendance_decisions','attendance_current','attendance_visits') AND engine<>'InnoDB'", connection);
        if (Convert.ToInt32(await metadataEngines.ExecuteScalarAsync()) != 0)
            throw new InvalidOperationException("Attendance metadata must use InnoDB before synchronization or restore can proceed.");
        await EnsureDeviceAttributionSchemaAsync(connection);
    }

    public async Task<AttendanceCommitResult> RecordAttendanceTransactionAsync(VerificationSession session, string remarks)
    {
        await AttendanceLock.WaitAsync();
        try
        {
            _attendanceActive = true;
            DateTime now = GetNetworkAdjustedTime();
            var student = session.Student;
            var log = OfflineCacheService.AttendanceQueue.Enqueue(new PendingGateLog
            {
                Timestamp = now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture),
                StudentId = student.StudentId,
                StudentName = student.IsTemporary ? $"[TEMP] {student.FullName}" : student.FullName,
                NfcUid = session.Uid, TransactionType = session.TransactionType.ToString(),
                VerificationMode = session.Mode.ToString(), IsGranted = true,
                ErrorCode = session.IsOffline ? "OFFLINE_MODE" : "VERIFIED", Remarks = remarks,
                WasOffline = session.IsOffline, OnlineAttempt = !session.IsOffline, EventId = session.EventId ?? "",
                NfcSystemMs = session.NfcSystemMs, PinWorkflowMs = session.PinWorkflowMs,
                PinSystemMs = session.PinSystemMs, QrWorkflowMs = session.QrWorkflowMs,
                QrSystemMs = session.QrSystemMs, DbQuerySpeedMs = session.TotalDbQueryMs,
                TotalWorkflowMs = session.PinWorkflowMs + session.QrWorkflowMs,
                TotalSystemMs = session.NfcSystemMs + session.PinSystemMs + session.QrSystemMs
            });

            void ApplyLocalGrant()
            {
                if (session.TransactionType != TransactionType.Exit || string.IsNullOrWhiteSpace(session.EventId))
                {
                    student.EntryState = session.TransactionType == TransactionType.Exit ? "OUTSIDE" : "INSIDE";
                    OfflineCacheService.UpdateCachedStudentStateLocally(student.StudentId, student.EntryState);
                }
            }

            bool? decision = null;
            AttendanceVisit? visit = null;
            if (session.IsOffline) ApplyLocalGrant();
            else
            {
                try
                {
                    await RecoverAttendanceCoreAsync(log.TransactionId, granted =>
                    {
                        // Capture the committed decision before local acknowledgment or cache I/O can fail.
                        decision = granted;
                        if (granted) ApplyLocalGrant();
                    });
                }
                catch (Exception ex) { AttendanceSyncError = ex.Message; }

                if (!decision.HasValue)
                {
                    try { decision = await ReadAttendanceDecisionAsync(log.TransactionId); }
                    catch (Exception ex) { AttendanceSyncError = ex.Message; }
                    if (decision == true) ApplyLocalGrant();
                }
                if (decision == true)
                {
                    try { visit = await ReadConfirmedVisitAsync(student.StudentId, log.TransactionId); }
                    catch (Exception ex) { AttendanceSyncError = ex.Message; }
                }
            }
            return new AttendanceCommitResult
            {
                TransactionId = log.TransactionId, Timestamp = now, Committed = decision.HasValue,
                IsGranted = session.IsOffline || decision == true,
                ConfirmationPending = !session.IsOffline && !decision.HasValue, Visit = visit
            };
        }
        finally { AttendanceLock.Release(); }
    }

    public async Task PulseAttendanceAsync()
    {
        if (!await AttendanceLock.WaitAsync(0)) return;
        try
        {
            if (!_attendanceActive)
            {
                if (OfflineCacheService.AttendanceQueue.Snapshot().DeviceNameChanges.Count == 0) return;
                using var connection = new MySqlConnection(ConnectionString);
                await connection.OpenAsync();
                await EnsureAttendanceSchemaAsync(connection);
                await FlushDeviceNameChangesAsync(connection);
                DeviceAuditSyncError = null;
                return;
            }
            await RecoverAttendanceCoreAsync();
        }
        catch (Exception ex) { AttendanceSyncError = ex.Message; }
        finally { AttendanceLock.Release(); }
    }

    private async Task<bool?> ReadAttendanceDecisionAsync(string transactionId)
    {
        using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();
        using var command = new MySqlCommand("SELECT is_granted FROM attendance_decisions WHERE transaction_id=@id", connection);
        command.Parameters.AddWithValue("@id", transactionId);
        var value = await command.ExecuteScalarAsync();
        return value == null ? null : Convert.ToBoolean(value);
    }

    private async Task RecoverAttendanceCoreAsync(string? liveTransactionId = null, Action<bool>? onLiveDecision = null)
    {
        var snapshot = OfflineCacheService.AttendanceQueue.Snapshot();
        bool recoveringBacklog = snapshot.EventLogs.Count != 0 || snapshot.GateLogs.Any(x => x.TransactionId != liveTransactionId);
        if (recoveringBacklog && !snapshot.CacheRefreshRequired)
        {
            OfflineCacheService.AttendanceQueue.RequireCacheRefresh();
            snapshot.CacheRefreshRequired = true;
        }
        if (snapshot.GateLogs.Count != 0 || snapshot.EventLogs.Count != 0)
            OfflineCacheService.FlushStudentCache();
        using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();
        await EnsureAttendanceSchemaAsync(connection);
        try
        {
            await FlushDeviceNameChangesAsync(connection);
            DeviceAuditSyncError = null;
        }
        catch (Exception ex) { DeviceAuditSyncError = ex.Message; }
        await ReportDeviceAsync(connection, snapshot, !snapshot.CacheRefreshRequired && snapshot.GateLogs.Count == 0 && snapshot.EventLogs.Count == 0);

        // Preserve allocation order across gate and event queues; stop on the first failure.
        var work = snapshot.GateLogs.Select(x => (x.DeviceSequence, Gate: (PendingGateLog?)x, Event: (PendingEventAttendance?)null))
            .Concat(snapshot.EventLogs.Select(x => (x.DeviceSequence, Gate: (PendingGateLog?)null, Event: (PendingEventAttendance?)x)))
            .OrderBy(x => x.DeviceSequence).Take(200);
        foreach (var item in work)
        {
            string id = item.Gate?.TransactionId ?? item.Event!.TransactionId;
            bool? decision = await CommitQueuedRecordAsync(connection, item.Gate, item.Event, id != liveTransactionId);
            if (id == liveTransactionId && decision.HasValue)
            {
                onLiveDecision?.Invoke(decision.Value);
                OfflineCacheService.FlushStudentCache();
            }
            // If this write fails or the process stops, a retry finds the existing DB receipt.
            OfflineCacheService.AttendanceQueue.Acknowledge(id);
        }
        snapshot = OfflineCacheService.AttendanceQueue.Snapshot();
        if (snapshot.CacheRefreshRequired && snapshot.GateLogs.Count == 0 && snapshot.EventLogs.Count == 0)
        {
            if (!await UpdateShadowCacheCoreAsync()) throw new InvalidOperationException("Attendance cache refresh did not complete.");
            OfflineCacheService.AttendanceQueue.MarkCacheRefreshed();
        }
        snapshot = OfflineCacheService.AttendanceQueue.Snapshot();
        await ReportDeviceAsync(connection, snapshot, !snapshot.CacheRefreshRequired && snapshot.GateLogs.Count == 0 && snapshot.EventLogs.Count == 0);
        AttendanceSyncError = null;
    }

    private static async Task ReportDeviceAsync(MySqlConnection connection, AttendanceQueueSnapshot snapshot, bool ready)
    {
        using var command = new MySqlCommand(@"
            INSERT INTO attendance_devices (device_id,device_name,last_seen,reported_sequence,ready)
            VALUES (@id,@name,UTC_TIMESTAMP(3),@seq,@ready)
            ON DUPLICATE KEY UPDATE device_name=@name,last_seen=UTC_TIMESTAMP(3), reported_sequence=@seq, ready=@ready, enabled=TRUE", connection);
        command.Parameters.AddWithValue("@id", snapshot.DeviceId);
        command.Parameters.AddWithValue("@name", DeviceIdentity.DisplayName(snapshot.DeviceName));
        command.Parameters.AddWithValue("@seq", snapshot.LastSequence);
        command.Parameters.AddWithValue("@ready", ready);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<bool> DevicesReadyAsync(MySqlConnection connection, MySqlTransaction? transaction = null)
    {
        using var command = new MySqlCommand(@"
            SELECT COUNT(*) FROM attendance_devices WHERE enabled=TRUE AND
            (ready=FALSE OR reported_sequence<>acknowledged_sequence OR last_seen < UTC_TIMESTAMP(3)-INTERVAL 15 SECOND)", connection, transaction);
        return Convert.ToInt32(await command.ExecuteScalarAsync()) == 0;
    }

    private static async Task<bool?> CommitQueuedRecordAsync(MySqlConnection connection, PendingGateLog? gate, PendingEventAttendance? evt, bool recovered)
    {
        string id = gate?.TransactionId ?? evt!.TransactionId;
        string device = gate?.DeviceId ?? evt!.DeviceId;
        long sequence = gate?.DeviceSequence ?? evt!.DeviceSequence;
        using var transaction = await connection.BeginTransactionAsync();
        using (var receipt = new MySqlCommand("SELECT COUNT(*) FROM attendance_receipts WHERE transaction_id=@id", connection, transaction))
        {
            receipt.Parameters.AddWithValue("@id", id);
            if (Convert.ToInt32(await receipt.ExecuteScalarAsync()) != 0)
            {
                using var prior = new MySqlCommand("SELECT is_granted FROM attendance_decisions WHERE transaction_id=@id", connection, transaction);
                prior.Parameters.AddWithValue("@id", id);
                var value = await prior.ExecuteScalarAsync();
                await transaction.CommitAsync();
                return value == null ? null : Convert.ToBoolean(value);
            }
        }
        using (var receipt = new MySqlCommand("INSERT INTO attendance_receipts VALUES (@id,@device,@seq)", connection, transaction))
        {
            receipt.Parameters.AddWithValue("@id", id);
            receipt.Parameters.AddWithValue("@device", device);
            receipt.Parameters.AddWithValue("@seq", sequence);
            await receipt.ExecuteNonQueryAsync();
        }
        if (gate != null)
        {
            if (recovered && gate.OnlineAttempt)
            {
                // An abandoned online request is not evidence that the person was admitted.
                gate.IsGranted = false;
                gate.ErrorCode = "ATTENDANCE_CONFIRMATION_EXPIRED";
                gate.Remarks = "Unconfirmed online attempt recovered without granting access; a new scan is required.";
            }
            if (!recovered && !gate.WasOffline && gate.IsGranted && string.IsNullOrWhiteSpace(gate.EventId) &&
                (gate.TransactionType == "Entry" || gate.TransactionType == "Exit"))
            {
                using var current = new MySqlCommand("SELECT entry_state FROM students WHERE student_id=@sid FOR UPDATE", connection, transaction);
                current.Parameters.AddWithValue("@sid", gate.StudentId);
                string? state = (await current.ExecuteScalarAsync())?.ToString();
                if (state == null || (gate.TransactionType == "Entry" ? state != "OUTSIDE" : state != "INSIDE"))
                {
                    gate.IsGranted = false;
                    gate.ErrorCode = "ATTENDANCE_SEQUENCE_CONFLICT";
                    gate.Remarks = "Attendance changed during verification; a fresh scan is required.";
                }
            }
            await InsertQueuedGateLogAsync(connection, transaction, gate);
            using (var decision = new MySqlCommand("INSERT INTO attendance_decisions VALUES (@id,@granted)", connection, transaction))
            {
                decision.Parameters.AddWithValue("@id", id);
                decision.Parameters.AddWithValue("@granted", gate.IsGranted);
                await decision.ExecuteNonQueryAsync();
            }
            if (gate.IsGranted && !string.IsNullOrWhiteSpace(gate.StudentId))
            {
                if (!string.IsNullOrWhiteSpace(gate.EventId))
                    await InsertQueuedEventAsync(connection, transaction, gate.EventId, gate.StudentId, gate.VerificationMode,
                        gate.TransactionType == "Exit" ? "DEPARTED" : "PRESENT", gate.Remarks, gate.Timestamp,
                        gate.IsLegacy ? "" : gate.DeviceId, gate.DeviceName);
                if (string.IsNullOrWhiteSpace(gate.EventId) && (gate.TransactionType == "Entry" || gate.TransactionType == "Exit"))
                    await ApplyGateVisitAsync(connection, transaction, gate, recovered || gate.WasOffline);
                else if (gate.TransactionType == "EventAttendance")
                    await InvalidateEventGateStateAsync(connection, transaction, gate.StudentId, gate.Timestamp, gate.DeviceId, gate.DeviceSequence);
            }
        }
        else
        {
            await InsertQueuedEventAsync(connection, transaction, evt!.EventId, evt.StudentId, evt.VerificationMode, evt.Status, evt.Remarks, evt.Timestamp,
                evt.IsLegacy ? "" : evt.DeviceId, evt.DeviceName);
            if (evt.Status == "PRESENT") await InvalidateEventGateStateAsync(connection, transaction, evt.StudentId, evt.Timestamp, evt.DeviceId, evt.DeviceSequence);
        }
        using (var ack = new MySqlCommand("UPDATE attendance_devices SET acknowledged_sequence=GREATEST(acknowledged_sequence,@seq) WHERE device_id=@device", connection, transaction))
        {
            ack.Parameters.AddWithValue("@device", device);
            ack.Parameters.AddWithValue("@seq", sequence);
            await ack.ExecuteNonQueryAsync();
        }
        await transaction.CommitAsync();
        return gate?.IsGranted;
    }

    private static async Task InsertQueuedGateLogAsync(MySqlConnection connection, MySqlTransaction transaction, PendingGateLog log)
    {
        string table = log.VerificationMode switch { "Fast" => "fast_mode_logs", "HighSecurity" => "high_security_mode_logs", _ => "standard_mode_logs" };
        using var command = new MySqlCommand($@"INSERT INTO {table}
            (timestamp,student_id,student_name,nfc_uid,transaction_type,verification_mode,is_granted,error_code,remarks,
             nfc_system_ms,pin_workflow_ms,pin_system_ms,qr_workflow_ms,qr_system_ms,total_workflow_ms,total_system_ms,db_query_speed_ms,device_id,device_name)
            VALUES (@ts,@sid,@name,@uid,@type,@mode,@granted,@error,@remarks,@nfc,@pw,@ps,@qw,@qs,@tw,@total,@db,@device,@device_name)", connection, transaction);
        command.Parameters.AddWithValue("@ts", ParseAttendanceTimestamp(log.Timestamp));
        command.Parameters.AddWithValue("@sid", NullIfEmpty(log.StudentId));
        command.Parameters.AddWithValue("@name", NullIfEmpty(log.StudentName));
        command.Parameters.AddWithValue("@uid", NullIfEmpty(log.NfcUid));
        command.Parameters.AddWithValue("@type", log.TransactionType);
        command.Parameters.AddWithValue("@mode", log.VerificationMode);
        command.Parameters.AddWithValue("@granted", log.IsGranted);
        command.Parameters.AddWithValue("@error", log.ErrorCode);
        command.Parameters.AddWithValue("@remarks", log.Remarks);
        command.Parameters.AddWithValue("@nfc", log.NfcSystemMs);
        command.Parameters.AddWithValue("@pw", log.PinWorkflowMs);
        command.Parameters.AddWithValue("@ps", log.PinSystemMs);
        command.Parameters.AddWithValue("@qw", log.QrWorkflowMs);
        command.Parameters.AddWithValue("@qs", log.QrSystemMs);
        command.Parameters.AddWithValue("@tw", log.TotalWorkflowMs);
        command.Parameters.AddWithValue("@total", log.TotalSystemMs);
        command.Parameters.AddWithValue("@db", log.DbQuerySpeedMs);
        command.Parameters.AddWithValue("@device", log.IsLegacy ? "" : log.DeviceId);
        command.Parameters.AddWithValue("@device_name", DeviceIdentity.DisplayName(log.DeviceName));
        await command.ExecuteNonQueryAsync();
    }

    private static DateTime ParseAttendanceTimestamp(string value) => DateTime.ParseExact(value, "yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);

    private static async Task InsertQueuedEventAsync(MySqlConnection connection, MySqlTransaction transaction, string eventId,
        string studentId, string mode, string status, string remarks, string timestamp, string deviceId, string deviceName)
    {
        using var command = new MySqlCommand(@"INSERT INTO event_attendance
            (timestamp,event_id,student_id,verification_mode,status,remarks,device_id,device_name) VALUES (@ts,@event,@sid,@mode,@status,@remarks,@device,@device_name)", connection, transaction);
        command.Parameters.AddWithValue("@ts", ParseAttendanceTimestamp(timestamp));
        command.Parameters.AddWithValue("@event", eventId);
        command.Parameters.AddWithValue("@sid", studentId);
        command.Parameters.AddWithValue("@mode", mode);
        command.Parameters.AddWithValue("@status", status);
        command.Parameters.AddWithValue("@remarks", remarks);
        command.Parameters.AddWithValue("@device", deviceId);
        command.Parameters.AddWithValue("@device_name", DeviceIdentity.DisplayName(deviceName));
        await command.ExecuteNonQueryAsync();
    }

    private static async Task ApplyGateVisitAsync(MySqlConnection connection, MySqlTransaction transaction, PendingGateLog log, bool recovered)
    {
        string? entryState;
        using (var student = new MySqlCommand("SELECT entry_state FROM students WHERE student_id=@sid FOR UPDATE", connection, transaction))
        {
            student.Parameters.AddWithValue("@sid", log.StudentId);
            entryState = (await student.ExecuteScalarAsync())?.ToString();
        }
        if (entryState == null) return; // Preserve the log even when a profile was removed.
        var state = await ReadAttendanceStateAsync(connection, transaction, log.StudentId)
            ?? new AttendanceState { EntryState = entryState };
        if (state.EntryState != entryState)
        {
            state.Unresolved = true;
            state.EntryState = entryState;
            if (state.Visit != null) state.Visit.Confirmed = false;
        }
        var previousVisit = state.Visit;
        bool updateState = state.Apply(log.TransactionId, log.DeviceId, log.DeviceSequence,
            ParseAttendanceTimestamp(log.Timestamp), log.TransactionType == "Entry", recovered, log.IsLegacy);
        if (previousVisit != null) await SaveVisitAsync(connection, transaction, log.StudentId, previousVisit);
        if (state.Visit != null) await SaveVisitAsync(connection, transaction, log.StudentId, state.Visit);
        await SaveAttendanceStateAsync(connection, transaction, log.StudentId, state);
        if (updateState)
        {
            using var command = new MySqlCommand("UPDATE students SET entry_state=@state WHERE student_id=@sid", connection, transaction);
            command.Parameters.AddWithValue("@state", state.EntryState);
            command.Parameters.AddWithValue("@sid", log.StudentId);
            await command.ExecuteNonQueryAsync();
        }
    }

    private static async Task InvalidateEventGateStateAsync(MySqlConnection connection, MySqlTransaction transaction, string studentId,
        string timestamp, string deviceId, long sequence)
    {
        // Event attendance is not a gate visit and must never manufacture a time-in.
        using var command = new MySqlCommand("SELECT entry_state FROM students WHERE student_id=@sid FOR UPDATE", connection, transaction);
        command.Parameters.AddWithValue("@sid", studentId);
        if (await command.ExecuteScalarAsync() == null) return;
        var state = await ReadAttendanceStateAsync(connection, transaction, studentId) ?? new AttendanceState();
        state.Unresolved = true;
        if (state.Visit != null) { state.Visit.Confirmed = false; await SaveVisitAsync(connection, transaction, studentId, state.Visit); }
        DateTime eventTime = ParseAttendanceTimestamp(timestamp);
        if (!state.LastTimestamp.HasValue || state.LastTimestamp < eventTime)
        {
            state.EntryState = "INSIDE";
            state.LastTimestamp = eventTime;
            state.LastDeviceId = deviceId;
            state.LastSequence = sequence;
            using var update = new MySqlCommand("UPDATE students SET entry_state='INSIDE' WHERE student_id=@sid", connection, transaction);
            update.Parameters.AddWithValue("@sid", studentId);
            await update.ExecuteNonQueryAsync();
        }
        await SaveAttendanceStateAsync(connection, transaction, studentId, state);
    }

    private static async Task<AttendanceState?> ReadAttendanceStateAsync(MySqlConnection connection, MySqlTransaction? transaction, string studentId)
    {
        using var command = new MySqlCommand("SELECT state_json FROM attendance_current WHERE student_id=@sid" +
            (transaction == null ? "" : " FOR UPDATE"), connection, transaction);
        command.Parameters.AddWithValue("@sid", studentId);
        var json = await command.ExecuteScalarAsync();
        return json == null ? null : JsonSerializer.Deserialize<AttendanceState>((string)json);
    }

    private static async Task SaveAttendanceStateAsync(MySqlConnection connection, MySqlTransaction transaction, string studentId, AttendanceState state)
    {
        using var command = new MySqlCommand(@"INSERT INTO attendance_current VALUES (@sid,@json)
            ON DUPLICATE KEY UPDATE state_json=@json", connection, transaction);
        command.Parameters.AddWithValue("@sid", studentId);
        command.Parameters.AddWithValue("@json", JsonSerializer.Serialize(state));
        await command.ExecuteNonQueryAsync();
    }

    private static async Task SaveVisitAsync(MySqlConnection connection, MySqlTransaction transaction, string studentId, AttendanceVisit visit)
    {
        using var command = new MySqlCommand(@"INSERT INTO attendance_visits
            (visit_id,student_id,entry_transaction_id,exit_transaction_id,time_in,time_out,confirmed)
            VALUES (@id,@sid,@entry,@exit,@tin,@tout,@confirmed)
            ON DUPLICATE KEY UPDATE exit_transaction_id=@exit,time_out=@tout,confirmed=@confirmed", connection, transaction);
        command.Parameters.AddWithValue("@id", visit.VisitId);
        command.Parameters.AddWithValue("@sid", studentId);
        command.Parameters.AddWithValue("@entry", visit.EntryTransactionId);
        command.Parameters.AddWithValue("@exit", visit.ExitTransactionId);
        command.Parameters.AddWithValue("@tin", visit.TimeIn);
        command.Parameters.AddWithValue("@tout", visit.TimeOut);
        command.Parameters.AddWithValue("@confirmed", visit.Confirmed);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<AttendanceVisit?> ReadConfirmedVisitAsync(string studentId, string transactionId)
    {
        if (OfflineCacheService.HasPendingLogs() || OfflineCacheService.AttendanceQueue.Snapshot().CacheRefreshRequired) return null;
        using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();
        if (!await DevicesReadyAsync(connection)) return null;
        var state = await ReadAttendanceStateAsync(connection, null, studentId);
        var visit = state?.Visit;
        return state?.Unresolved == false && visit?.Confirmed == true &&
            (visit.ExitTransactionId ?? visit.EntryTransactionId) == transactionId ? visit : null;
    }
}
