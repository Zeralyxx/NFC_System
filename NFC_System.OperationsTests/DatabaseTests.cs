using MySqlConnector;
using NFC_System;
using System.Text;

internal static class DatabaseTests
{
    private static void Assert(bool condition) { if (!condition) throw new Exception("Assertion failed"); }
    private static async Task Reject(Func<Task> action)
    {
        try { await action(); } catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException) { return; }
        throw new Exception("Expected rejection");
    }
    public static async Task RunAsync(Func<string, Func<Task>, Task> test)
    {
        var builder = new MySqlConnectionStringBuilder(DatabaseService.ConnectionString);
        if (builder.Server != "127.0.0.1" || builder.Port != 23306 || builder.Database != "nfc_operations_tests")
            throw new InvalidOperationException("Refusing to run outside the disposable operations database.");
        builder.Database = "";
        using (var admin = new MySqlConnection(builder.ConnectionString))
        {
            await admin.OpenAsync(); using var create = new MySqlCommand("CREATE DATABASE IF NOT EXISTS nfc_operations_tests", admin);
            await create.ExecuteNonQueryAsync();
        }
        using var connection = new MySqlConnection(DatabaseService.ConnectionString); await connection.OpenAsync();
        async Task Sql(string sql) { using var cmd = new MySqlCommand(sql, connection); await cmd.ExecuteNonQueryAsync(); }
        async Task<string> Scalar(string sql) { using var cmd = new MySqlCommand(sql, connection); return Convert.ToString(await cmd.ExecuteScalarAsync()) ?? ""; }
        await Sql(@"CREATE TABLE IF NOT EXISTS students (
            student_id VARCHAR(50) PRIMARY KEY, full_name VARCHAR(100), email VARCHAR(150), course VARCHAR(100),
            year_level VARCHAR(20), section_name VARCHAR(50), status VARCHAR(20), nfc_uid VARCHAR(50) UNIQUE,
            pin_salt VARCHAR(255),pin_hash VARCHAR(255),qr_credential TEXT, entry_state VARCHAR(20),failed_pin_attempts INT,
            pin_locked BOOLEAN,is_temporary BOOLEAN) ENGINE=InnoDB;
            CREATE TABLE IF NOT EXISTS courses (course_name VARCHAR(100) PRIMARY KEY) ENGINE=InnoDB;
            CREATE TABLE IF NOT EXISTS alerts (id INT AUTO_INCREMENT PRIMARY KEY,student_id VARCHAR(50),alert_type VARCHAR(100),message TEXT,
                device_id VARCHAR(32),device_name VARCHAR(100)) ENGINE=InnoDB;
            CREATE TABLE IF NOT EXISTS events(event_id VARCHAR(50) PRIMARY KEY,event_name VARCHAR(150),event_date DATETIME,
                verification_mode VARCHAR(50),is_restricted BOOLEAN,is_active BOOLEAN) ENGINE=InnoDB;
            CREATE TABLE IF NOT EXISTS event_approved_students(event_id VARCHAR(50),student_id VARCHAR(50),PRIMARY KEY(event_id,student_id)) ENGINE=InnoDB;");
        await DatabaseService.EnsureGraduationSchemaAsync(connection);
        await DatabaseService.EnsureGraduationSchemaAsync(connection);
        await DatabaseService.EnsureEventRosterSyncSchemaAsync(connection);
        await DatabaseService.EnsureEventRosterSyncSchemaAsync(connection);
        await Sql("DELETE FROM event_roster_sync; ALTER TABLE students ADD COLUMN IF NOT EXISTS photo_data LONGTEXT");
        await Sql("DELETE FROM event_approved_students; DELETE FROM events; DELETE FROM student_graduation_audit; DELETE FROM alerts; DELETE FROM students; DELETE FROM courses; INSERT INTO courses VALUES('BSIT');");
        AppSession.IsLoggedIn = true; AppSession.IsAdmin = true; AppSession.IsEventOrganizer = false;
        AppSession.CurrentStaffRoleLabel = "Master Admin"; AppSession.CurrentStaffName = "Test administrator";
        DatabaseMonitor.IsOnline = true;
        var db = new DatabaseService();
        SpreadsheetImportDocument Document(string rows)
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes("StudentId,FullName,Email,Course,YearLevel,Section\n" + rows));
            return SpreadsheetService.ReadImport(stream, ".csv", StudentImportKind.Profiles);
        }
        await test("SQL imports are credential-free Pending Enrollment profiles with device-tagged audit", async () =>
        {
            Assert(await db.ImportStudentsAsync(Document("00-01,Student A,,BSIT,1,X\n00-02,Student B,,BSIT,1,X")) == 2);
            Assert(await Scalar("SELECT COUNT(*) FROM students WHERE status='Pending Enrollment' AND nfc_uid IS NULL AND pin_hash IS NULL AND qr_credential IS NULL AND graduation_clearance='Not Reviewed'") == "2");
            Assert(await Scalar("SELECT COUNT(*) FROM alerts WHERE device_name='Operations test device'") == "1");
        });
        await test("SQL mixed duplicate/new import saves nothing", async () =>
        {
            await Reject(() => db.ImportStudentsAsync(Document("00-01,Changed,,BSIT,1,X\n00-03,Student C,,BSIT,1,X")));
            Assert(await Scalar("SELECT COUNT(*) FROM students") == "2" && await Scalar("SELECT full_name FROM students WHERE student_id='00-01'") == "Student A");
        });
        await test("SQL profile activation preview includes newly entered credentials but batch activation does not", async () =>
        {
            var edit = await db.PreviewStudentProfileStatusUpdateAsync("00-01", "Active", "AA:BB:CC:DD", true);
            var batch = await db.PreviewStudentStatusUpdateAsync(new[] { "00-01" }, "Active");
            Assert(edit.EligibleCount == 1 && batch.EligibleCount == 0);
            Assert((await db.PreviewStudentProfileStatusUpdateAsync("00-01", "Active", "AA:BB:CC:DD", false)).EligibleCount == 0);
        });
        await test("SQL clearance revisions reject competing edits and preserve the recorded hold", async () =>
        {
            await db.SaveGraduationClearanceAsync("00-01", GraduationClearanceStatus.Cleared, "Complete", 0, "Test administrator");
            await db.SaveGraduationClearanceAsync("00-02", GraduationClearanceStatus.OnHold, "Pending requirement", 0, "Test administrator");
            await Reject(() => db.SaveGraduationClearanceAsync("00-02", GraduationClearanceStatus.Cleared, "Stale form", 0, "Test administrator"));
            Assert(await Scalar("SELECT graduation_clearance FROM students WHERE student_id='00-02'") == "On Hold");
            Assert(await Scalar("SELECT COUNT(*) FROM student_graduation_audit") == "2");
        });
        await test("SQL graduation saves only eligible preview rows", async () =>
        {
            var preview = await db.PreviewBatchStudentStatusUpdateAsync("BSIT", "All Years", "Graduated");
            Assert(preview.EligibleCount == 1 && preview.ExcludedCount == 1);
            Assert(await db.ApplyStudentStatusUpdateAsync(preview, "Test administrator") == 1);
            Assert(await Scalar("SELECT status FROM students WHERE student_id='00-02'") == "Pending Enrollment");
        });
        await test("SQL graduation rechecks after preview and rolls back earlier rows on conflict", async () =>
        {
            await Sql("UPDATE students SET status='Inactive',graduation_clearance='Cleared',graduation_revision=2");
            var preview = await db.PreviewBatchStudentStatusUpdateAsync("BSIT", "All Years", "Graduated");
            await db.SaveGraduationClearanceAsync("00-02", GraduationClearanceStatus.OnHold, "New hold", 2, "Test administrator");
            await Reject(() => db.ApplyStudentStatusUpdateAsync(preview, "Test administrator"));
            Assert(await Scalar("SELECT COUNT(*) FROM students WHERE status='Inactive'") == "2");
        });
        await test("SQL mode change preserves event date, active status and restriction; stale edit rejected", async () =>
        {
            await db.CreateNewEventAsync("event", "Original", VerificationMode.Standard, true, AppSession.LoginVersion);
            string before = await Scalar("SELECT event_date FROM events WHERE event_id='event'");
            var saved = await db.UpdateEventDetailsAsync("event", "Original", VerificationMode.Standard, "Updated", VerificationMode.HighSecurity, AppSession.LoginVersion);
            Assert(saved.IsRestricted && saved.Status == "Active" && await Scalar("SELECT event_date FROM events WHERE event_id='event'") == before);
            await Reject(() => db.UpdateEventDetailsAsync("event", "Original", VerificationMode.Standard, "Stale", VerificationMode.Fast, AppSession.LoginVersion));
            Assert((await db.GetEventConfigurationAsync("event"))!.VerificationMode == VerificationMode.HighSecurity);
        });
        await test("SQL roster batch rechecks active status, deduplicates and removes explicit selections", async () =>
        {
            await Sql("UPDATE students SET status='Active' WHERE student_id='00-01'");
            var result = await db.ApplyEventRosterSelectionAsync("event", new[] { "00-01", "00-01", "00-02", "missing" }, false, AppSession.LoginVersion);
            Assert(result.Changed == 1 && result.Ineligible == 1 && result.Unknown == 1);
            Assert((await db.ApplyEventRosterSelectionAsync("event", new[] { "00-01" }, false, AppSession.LoginVersion)).AlreadyIncluded == 1);
            Assert((await db.ApplyEventRosterSelectionAsync("event", new[] { "00-01" }, true, AppSession.LoginVersion)).Changed == 1);
        });
        await Sql("DELETE FROM event_roster_sync");
        await AuditDatabaseRegressionTests.RunAsync(db, Sql, Scalar, test);
        await test("SQL stale session rejects event mutations before write", async () =>
        {
            long old = AppSession.LoginVersion; AppSession.IsLoggedIn = false;
            await Reject(() => db.CreateNewEventAsync("denied", "Denied", VerificationMode.Fast, false, old));
            Assert(await Scalar("SELECT COUNT(*) FROM events WHERE event_id='denied'") == "0");
        });
    }
}
