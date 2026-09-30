using MySqlConnector;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;

namespace NFC_System;

public sealed partial class DatabaseService
{
    public async Task<IReadOnlyList<StudentImportPreviewRow>> PreviewStudentImportAsync(SpreadsheetImportDocument document)
    {
        RequireStudentImportPermission();
        long version = AppSession.LoginVersion;
        using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();
        var (ids, courses) = await ReadImportReferenceAsync(connection, null);
        RequireStudentImportPermission(version);
        return SpreadsheetService.PreviewProfiles(document, ids, courses);
    }

    public async Task<int> ImportStudentsAsync(SpreadsheetImportDocument document)
    {
        RequireStudentImportPermission();
        long version = AppSession.LoginVersion;
        string actor = AppSession.CurrentStaffName;
        var device = DeviceIdentity.CaptureCurrent();
        if (document.Kind != StudentImportKind.Profiles || document.Rows.Count is < 1 or > SpreadsheetService.MaxImportRows)
            throw new InvalidOperationException("Load a valid student profile import first.");
        using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();
        using (var engine = new MySqlCommand("SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = DATABASE() AND table_name IN ('students','courses','alerts') AND engine = 'InnoDB'", connection))
        {
            if (Convert.ToInt32(await engine.ExecuteScalarAsync()) != 3)
                throw new InvalidOperationException("Student import requires InnoDB students, courses, and alerts tables for an all-or-nothing save.");
        }
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable);
        var (ids, courses) = await ReadImportReferenceAsync(connection, transaction);
        var preview = SpreadsheetService.PreviewProfiles(document, ids, courses);
        var errors = preview.Where(r => r.Status != ImportRowStatus.Valid).ToList();
        if (errors.Count > 0)
            throw new InvalidOperationException($"Nothing was imported. {errors.Count} row(s) need review. Row {errors[0].RowNumber}: {errors[0].Message} Reload the preview.");
        RequireStudentImportPermission(version);
        foreach (var row in preview)
        {
            var profile = row.Profile!;
            using var insert = new MySqlCommand(@"
                INSERT INTO students (student_id, full_name, email, course, year_level, section_name, status,
                    nfc_uid, pin_salt, pin_hash, qr_credential, entry_state, failed_pin_attempts, pin_locked, is_temporary)
                VALUES (@id, @name, @email, @course, @year, @section, @status,
                    NULL, NULL, NULL, NULL, 'OUTSIDE', 0, FALSE, FALSE)", connection, transaction);
            insert.Parameters.AddWithValue("@id", profile.StudentId);
            insert.Parameters.AddWithValue("@name", profile.FullName);
            insert.Parameters.AddWithValue("@email", profile.Email);
            insert.Parameters.AddWithValue("@course", profile.Course);
            insert.Parameters.AddWithValue("@year", profile.YearLevel);
            insert.Parameters.AddWithValue("@section", profile.Section);
            insert.Parameters.AddWithValue("@status", SpreadsheetService.PendingEnrollmentStatus);
            await insert.ExecuteNonQueryAsync();
        }
        using (var audit = new MySqlCommand("INSERT INTO alerts (student_id, alert_type, message, device_id, device_name) VALUES (NULL, 'ADMIN_ACTION', @message, @device, @deviceName)", connection, transaction))
        {
            audit.Parameters.AddWithValue("@device", device.DeviceId);
            audit.Parameters.AddWithValue("@deviceName", device.DeviceName);
            audit.Parameters.AddWithValue("@message", $"{actor} imported {preview.Count} student profiles as Pending Enrollment. No NFC, PIN, or QR credentials were imported.");
            await audit.ExecuteNonQueryAsync();
        }
        RequireStudentImportPermission(version);
        await transaction.CommitAsync();
        return preview.Count;
    }

    private static async Task<(List<string> Ids, List<string> Courses)> ReadImportReferenceAsync(MySqlConnection connection, MySqlTransaction? transaction)
    {
        var ids = new List<string>();
        var courses = new List<string>();
        string locking = transaction == null ? "" : " FOR UPDATE";
        using (var command = new MySqlCommand("SELECT student_id FROM students" + locking, connection, transaction))
        using (var reader = await command.ExecuteReaderAsync())
            while (await reader.ReadAsync()) ids.Add(reader.GetString(0));
        using (var command = new MySqlCommand("SELECT course_name FROM courses" + locking, connection, transaction))
        using (var reader = await command.ExecuteReaderAsync())
            while (await reader.ReadAsync()) courses.Add(reader.GetString(0));
        return (ids, courses);
    }

    private static void RequireStudentImportPermission(long? expectedVersion = null)
    {
        if (!DatabaseMonitor.IsOnline) throw new InvalidOperationException("Student import requires an online database connection.");
        if (!AppSession.CanIssueQrCredentials || expectedVersion.HasValue && AppSession.LoginVersion != expectedVersion.Value)
            throw new UnauthorizedAccessException("Sign in as an Administrator or Master Admin to import students. The session must remain unchanged until the import finishes.");
    }
}
