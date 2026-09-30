using MySqlConnector;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;

namespace NFC_System;

public sealed partial class DatabaseService
{
    // Call after creating students. Do not run DDL inside a student-write transaction.
    public static async Task EnsureGraduationSchemaAsync(MySqlConnection connection)
    {
        var columns = new Dictionary<string, string>
        {
            ["graduation_clearance"] = "VARCHAR(20) NOT NULL DEFAULT 'Not Reviewed'",
            ["graduation_reason"] = "VARCHAR(1000) NOT NULL DEFAULT ''",
            ["graduation_changed_by"] = "VARCHAR(150) NOT NULL DEFAULT ''",
            ["graduation_changed_at_utc"] = "DATETIME(6) NULL",
            ["graduation_revision"] = "BIGINT NOT NULL DEFAULT 0"
        };
        using (var existing = new MySqlCommand("SELECT column_name FROM information_schema.columns WHERE table_schema=DATABASE() AND table_name='students'", connection))
        {
            using var reader = await existing.ExecuteReaderAsync();
            while (await reader.ReadAsync()) columns.Remove(reader.GetString(0));
        }
        if (columns.Count > 0)
        {
            using var alter = new MySqlCommand("ALTER TABLE students " + string.Join(", ", columns.Select(c => $"ADD COLUMN `{c.Key}` {c.Value}")), connection);
            try { await alter.ExecuteNonQueryAsync(); }
            catch (MySqlException ex) when (ex.Number == 1060)
            {
                await EnsureGraduationSchemaAsync(connection);
                return;
            }
        }
        using var command = new MySqlCommand(@"
            CREATE TABLE IF NOT EXISTS student_graduation_audit (
                audit_id CHAR(32) PRIMARY KEY,
                student_id VARCHAR(50) NOT NULL,
                student_name VARCHAR(100) NOT NULL,
                action VARCHAR(30) NOT NULL,
                old_value VARCHAR(20) NOT NULL,
                new_value VARCHAR(20) NOT NULL,
                reason VARCHAR(1000) NOT NULL,
                actor VARCHAR(150) NOT NULL,
                changed_at_utc DATETIME(6) NOT NULL,
                INDEX ix_graduation_audit_student (student_id, changed_at_utc)
            ) ENGINE=InnoDB;", connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task RequireGraduationTransactionsAsync(MySqlConnection connection)
    {
        using var command = new MySqlCommand(@"SELECT COUNT(*) FROM information_schema.tables
            WHERE table_schema = DATABASE() AND table_name IN
            ('students','student_graduation_audit','alerts') AND engine = 'InnoDB'", connection);
        if (Convert.ToInt32(await command.ExecuteScalarAsync()) != 3)
            throw new InvalidOperationException("Graduation setup is incomplete. Students and the graduation audit table must use InnoDB.");
    }

    private static long RequireGraduationAdmin(long? expectedSession = null) =>
        GraduationRules.RequireAdministrator(DatabaseMonitor.IsOnline, expectedSession);

    private static string GraduationActor(string? authorizedBy)
    {
        string actor = string.IsNullOrWhiteSpace(authorizedBy) ? AppSession.CurrentStaffName : authorizedBy.Trim();
        if (actor.Length == 0 || actor.Length > 150) throw new InvalidOperationException("A named administrator is required.");
        return actor;
    }

    public async Task<IReadOnlyList<GraduationStudent>> GetGraduationDirectoryAsync()
    {
        long session = RequireGraduationAdmin();
        using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();
        using var command = new MySqlCommand(GraduationSelect + " ORDER BY full_name, student_id", connection);
        using var reader = await command.ExecuteReaderAsync();
        var rows = new List<GraduationStudent>();
        while (await reader.ReadAsync()) rows.Add(ReadGraduationStudent(reader));
        RequireGraduationAdmin(session);
        return rows.AsReadOnly();
    }

    private const string GraduationSelect = @"SELECT student_id, full_name, course, year_level, section_name, status,
        graduation_clearance AS clearance_status, graduation_reason AS reason, graduation_changed_by AS changed_by,
        graduation_changed_at_utc AS changed_at_utc, graduation_revision AS revision,
        (nfc_uid IS NOT NULL AND TRIM(nfc_uid)<>'') AS has_nfc,
        (pin_hash IS NOT NULL AND TRIM(pin_hash)<>'' AND pin_salt IS NOT NULL AND TRIM(pin_salt)<>'') AS has_pin FROM students";

    private static GraduationStudent ReadGraduationStudent(MySqlDataReader reader) => new()
    {
        StudentId = Value(reader["student_id"]), FullName = Value(reader["full_name"]),
        Course = Value(reader["course"]), YearLevel = Value(reader["year_level"]),
        Section = Value(reader["section_name"]), EnrollmentStatus = Value(reader["status"]),
        Clearance = GraduationRules.Parse(Value(reader["clearance_status"])), Reason = Value(reader["reason"]),
        ChangedBy = Value(reader["changed_by"]),
        ChangedAtUtc = reader["changed_at_utc"] == DBNull.Value ? null : DateTime.SpecifyKind(Convert.ToDateTime(reader["changed_at_utc"]), DateTimeKind.Utc),
        Revision = reader["revision"] == DBNull.Value ? 0 : Convert.ToInt64(reader["revision"]),
        HasNfc = Convert.ToBoolean(reader["has_nfc"]), HasPin = Convert.ToBoolean(reader["has_pin"])
    };

    // Cloud merges must take the same student-row lock before comparing or updating clearance revisions.
    public static async Task<GraduationStudent> LockGraduationStudentAsync(MySqlConnection connection,
        MySqlTransaction transaction, string studentId)
    {
        using var command = new MySqlCommand(GraduationSelect + " WHERE student_id=@id FOR UPDATE", connection, transaction);
        command.Parameters.AddWithValue("@id", studentId);
        using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) throw new InvalidOperationException($"Student {studentId} no longer exists. Review the selection again.");
        return ReadGraduationStudent(reader);
    }

    public async Task SaveGraduationClearanceAsync(string studentId, GraduationClearanceStatus status,
        string reason, long expectedRevision, string authorizedBy)
    {
        long session = RequireGraduationAdmin();
        reason = GraduationRules.ValidateReason(status, reason);
        string actor = GraduationActor(authorizedBy);
        var device = DeviceIdentity.CaptureCurrent();
        using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();
        await RequireGraduationTransactionsAsync(connection);
        using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        GraduationStudent current = await LockGraduationStudentAsync(connection, transaction, studentId);
        if (current.Revision != expectedRevision)
            throw new InvalidOperationException("This clearance was changed by another administrator. Reload it before saving.");
        using var command = new MySqlCommand(@"UPDATE students SET graduation_clearance=@status,
            graduation_reason=@reason, graduation_changed_by=@actor, graduation_changed_at_utc=UTC_TIMESTAMP(6),
            graduation_revision=@revision WHERE student_id=@id", connection, transaction);
        command.Parameters.AddWithValue("@id", current.StudentId);
        command.Parameters.AddWithValue("@status", GraduationRules.Label(status));
        command.Parameters.AddWithValue("@reason", reason);
        command.Parameters.AddWithValue("@actor", actor);
        command.Parameters.AddWithValue("@revision", checked(current.Revision + 1));
        await command.ExecuteNonQueryAsync();
        await AddGraduationAuditAsync(connection, transaction, current, "Clearance", current.ClearanceLabel,
            GraduationRules.Label(status), reason, actor, device);
        RequireGraduationAdmin(session);
        await transaction.CommitAsync();
    }

    public async Task<StudentStatusPreview> PreviewStudentStatusUpdateAsync(IEnumerable<string> studentIds, string newStatus)
    {
        long session = RequireGraduationAdmin();
        var ids = studentIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var students = (await GetGraduationDirectoryAsync()).Where(s => ids.Contains(s.StudentId)).ToArray();
        if (students.Length != ids.Count) throw new InvalidOperationException("Some selected students no longer exist. Reload the directory.");
        return GraduationRules.Preview(students, newStatus, session, DateTime.UtcNow);
    }

    public async Task<StudentStatusPreview> PreviewStudentProfileStatusUpdateAsync(string studentId, string newStatus, string nfcUid, bool hasNewPin)
    {
        long session = RequireGraduationAdmin();
        var student = (await GetGraduationDirectoryAsync()).Single(s => s.StudentId == studentId);
        return GraduationRules.Preview(new[] { student with { HasNfc = !string.IsNullOrWhiteSpace(nfcUid), HasPin = student.HasPin || hasNewPin } },
            newStatus, session, DateTime.UtcNow);
    }

    public async Task<StudentStatusPreview> PreviewBatchStudentStatusUpdateAsync(string course, string year, string newStatus)
    {
        long session = RequireGraduationAdmin();
        if (course == "All Courses" && year == "All Years")
            throw new InvalidOperationException("Select at least one course or year filter.");
        var students = (await GetGraduationDirectoryAsync()).Where(s => GraduationRules.MatchesGroup(s, course, year));
        return GraduationRules.Preview(students, newStatus, session, DateTime.UtcNow);
    }

    public async Task<int> ApplyStudentStatusUpdateAsync(StudentStatusPreview preview, string authorizedBy)
    {
        long session = RequireGraduationAdmin(preview.SessionVersion);
        GraduationRules.ValidatePreviewAge(preview, DateTime.UtcNow);
        string actor = GraduationActor(authorizedBy);
        var device = DeviceIdentity.CaptureCurrent();
        using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();
        await RequireGraduationTransactionsAsync(connection);
        using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        var targets = preview.Rows.Where(r => r.Eligible).OrderBy(r => r.Student.StudentId, StringComparer.Ordinal).ToArray();
        foreach (var target in targets)
        {
            GraduationStudent current = await LockGraduationStudentAsync(connection, transaction, target.Student.StudentId);
            GraduationRules.ValidateRecheck(target.Student, current, preview.NewStatus);
            using var command = new MySqlCommand("UPDATE students SET status=@status WHERE student_id=@id", connection, transaction);
            command.Parameters.AddWithValue("@id", current.StudentId);
            command.Parameters.AddWithValue("@status", preview.NewStatus);
            await command.ExecuteNonQueryAsync();
            await AddGraduationAuditAsync(connection, transaction, current, "EnrollmentStatus", current.EnrollmentStatus,
                preview.NewStatus, "Confirmed explicit student selection", actor, device);
        }
        RequireGraduationAdmin(session);
        GraduationRules.ValidatePreviewAge(preview, DateTime.UtcNow);
        await transaction.CommitAsync();
        return targets.Length;
    }

    // The legacy UpdateStudentAsync should delegate here. Every SQL operation uses the same transaction.
    public async Task UpdateStudentWithClearanceAsync(string originalStudentId, StudentRecord student, string? newPin,
        string? authorizedBy = null, StudentStatusPreview? preview = null)
    {
        long session = RequireGraduationAdmin(preview?.SessionVersion);
        string actor = GraduationActor(authorizedBy);
        var device = DeviceIdentity.CaptureCurrent();
        using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();
        await RequireGraduationTransactionsAsync(connection);
        using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        var current = await LockGraduationStudentAsync(connection, transaction, originalStudentId);
        StudentProfileRules.ValidateEdit(student.StudentId, student.FullName, student.Status, student.NfcUid, current.HasNfc);
        var effective = current with
        {
            HasNfc = !string.IsNullOrWhiteSpace(student.NfcUid),
            HasPin = !string.IsNullOrWhiteSpace(newPin) || current.HasPin
        };
        GraduationRules.RequireGraduationClearance(effective, student.Status);
        if (preview != null)
        {
            GraduationRules.ValidatePreviewAge(preview, DateTime.UtcNow);
            if (preview.NewStatus != student.Status || preview.Rows.Count != 1 || !preview.Rows[0].Eligible)
                throw new InvalidOperationException("The profile no longer matches the confirmed status preview.");
            GraduationRules.ValidateRecheck(preview.Rows[0].Student, effective, student.Status);
        }
        using (var qr = new MySqlCommand("SELECT qr_credential FROM students WHERE student_id=@id", connection, transaction))
        {
            qr.Parameters.AddWithValue("@id", originalStudentId);
            string previousQr = Convert.ToString(await qr.ExecuteScalarAsync()) ?? "";
            if ((originalStudentId != student.StudentId && !string.IsNullOrWhiteSpace(previousQr)) ||
                !string.Equals(previousQr, student.QrCredential, StringComparison.Ordinal) ||
                (!current.HasNfc && !string.IsNullOrWhiteSpace(student.NfcUid)))
                ValidateIssuedQr(student);
        }
        if (!string.IsNullOrWhiteSpace(student.NfcUid))
        {
            using var uid = new MySqlCommand("SELECT COUNT(*) FROM students WHERE nfc_uid=@uid AND student_id<>@id", connection, transaction);
            uid.Parameters.AddWithValue("@uid", student.NfcUid);
            uid.Parameters.AddWithValue("@id", originalStudentId);
            if (Convert.ToInt32(await uid.ExecuteScalarAsync()) != 0) throw new InvalidOperationException("This NFC card is already linked to another student.");
        }
        string? salt = null, hash = null;
        if (!string.IsNullOrWhiteSpace(newPin))
        {
            if (newPin.Length != 4 || !newPin.All(char.IsDigit)) throw new ArgumentException("PIN must be four digits.");
            var hashed = PinHasher.HashPin(newPin);
            salt = hashed.Salt;
            hash = hashed.Hash;
        }
        string pinSql = salt == null ? "" : ", pin_salt=@pin_salt, pin_hash=@pin_hash, pin_locked=FALSE, failed_pin_attempts=0";
        using var update = new MySqlCommand(@"UPDATE students SET student_id=@student_id, full_name=@full_name,
            email=@email, course=@course, year_level=@year_level, section_name=@section_name, status=@status,
            nfc_uid=@nfc_uid, qr_credential=@qr_credential, photo_data=@photo_data, is_temporary=@is_temporary" +
            pinSql + " WHERE student_id=@original_id", connection, transaction);
        AddStudentParameters(update, student, salt, hash);
        update.Parameters.AddWithValue("@original_id", originalStudentId);
        await update.ExecuteNonQueryAsync();
        if (current.EnrollmentStatus != student.Status)
            await AddGraduationAuditAsync(connection, transaction, current, "EnrollmentStatus", current.EnrollmentStatus,
                student.Status, "Individual profile update", actor, device);
        if (originalStudentId != student.StudentId)
        {
            using var renameAudit = new MySqlCommand("UPDATE student_graduation_audit SET student_id=@newId WHERE student_id=@oldId", connection, transaction);
            renameAudit.Parameters.AddWithValue("@newId", student.StudentId);
            renameAudit.Parameters.AddWithValue("@oldId", originalStudentId);
            await renameAudit.ExecuteNonQueryAsync();
        }
        RequireGraduationAdmin(session);
        await transaction.CommitAsync();
        if (salt != null && hash != null)
        {
            student.PinSalt = salt;
            student.PinHash = hash;
        }
        OfflineCacheService.UpdateCachedCredential(originalStudentId, student, salt != null);
    }

    private static async Task AddGraduationAuditAsync(MySqlConnection connection, MySqlTransaction transaction,
        GraduationStudent student, string action, string oldValue, string newValue, string reason, string actor, DeviceAttribution device)
    {
        using var command = new MySqlCommand(@"INSERT INTO student_graduation_audit
            (audit_id, student_id, student_name, action, old_value, new_value, reason, actor, changed_at_utc)
            VALUES (@audit, @id, @name, @action, @old, @new, @reason, @actor, UTC_TIMESTAMP(6))", connection, transaction);
        command.Parameters.AddWithValue("@audit", Guid.NewGuid().ToString("N"));
        command.Parameters.AddWithValue("@id", student.StudentId);
        command.Parameters.AddWithValue("@name", student.FullName);
        command.Parameters.AddWithValue("@action", action);
        command.Parameters.AddWithValue("@old", oldValue);
        command.Parameters.AddWithValue("@new", newValue);
        command.Parameters.AddWithValue("@reason", reason);
        command.Parameters.AddWithValue("@actor", actor);
        await command.ExecuteNonQueryAsync();
        using var alert = new MySqlCommand(@"INSERT INTO alerts (student_id,alert_type,message,device_id,device_name)
            VALUES (@id,'ADMIN_ACTION',@message,@device,@name)", connection, transaction);
        alert.Parameters.AddWithValue("@id", student.StudentId);
        alert.Parameters.AddWithValue("@message", $"{actor}: {action} for {student.StudentId}: {oldValue} -> {newValue}. Reason: {reason}");
        alert.Parameters.AddWithValue("@device", device.DeviceId);
        alert.Parameters.AddWithValue("@name", device.DeviceName);
        await alert.ExecuteNonQueryAsync();
    }
}
