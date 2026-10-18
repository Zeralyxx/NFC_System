using NFC_System;
using MySqlConnector;

internal static class StudentSyncTests
{
    private static void Assert(bool value) { if (!value) throw new Exception("Student sync assertion failed"); }
    private static StudentSyncProfile Profile(string id = "SYNC-1") => new(id, "Student", "", "BSIT", "1", "A",
        "Pending Enrollment", "", "", "", "", null, false);

    public static async Task RunAsync(Func<string, Func<Task>, Task> test)
    {
        await test("Student sync excludes entry state, counters, lock and timestamps", () =>
        {
            string json = Profile().ToJson();
            Assert(!json.Contains("EntryState") && !json.Contains("PinLocked") && !json.Contains("FailedPinAttempts") && !json.Contains("LastScan"));
            Assert(StudentSyncProfile.Parse(json).Fingerprint() == Profile().Fingerprint());
            return Task.CompletedTask;
        });
        await test("Student sync rejects injected operational fields", () =>
        {
            bool rejected = false;
            try { StudentSyncProfile.Parse(Profile().ToJson().Replace("\"FullName\":", "\"EntryState\":\"INSIDE\",\"FullName\":")); }
            catch (System.Text.Json.JsonException) { rejected = true; }
            Assert(rejected); return Task.CompletedTask;
        });
        foreach (var profile in new[] { Profile() with { PinSalt = "plaintext", PinHash = "1234" },
            Profile() with { PhotoData = new byte[500001] }, Profile() with { FullName = null! },
            Profile() with { Status = "Active" }, Profile() with { YearLevel = "1st year" } })
        await test("Student sync validates untrusted profile fields: " + profile.Fingerprint()[..8], () =>
        {
            bool rejected = false;
            try { profile.Validate(); } catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException) { rejected = true; }
            Assert(rejected); return Task.CompletedTask;
        });
    }

    public static async Task RunDatabaseAsync(Func<string, Func<Task>, Task> test)
    {
        var builder = new MySqlConnectionStringBuilder(DatabaseService.ConnectionString);
        if (builder.Server != "127.0.0.1" || builder.Port != 23306 || builder.Database != "nfc_operations_tests") throw new Exception("Unsafe sync test database");
        using var connection = new MySqlConnection(DatabaseService.ConnectionString);
        await connection.OpenAsync();
        async Task Sql(string sql) { using var command = new MySqlCommand(sql, connection); await command.ExecuteNonQueryAsync(); }
        async Task<string> Scalar(string sql) { using var command = new MySqlCommand(sql, connection); return Convert.ToString(await command.ExecuteScalarAsync()) ?? ""; }
        await DatabaseService.EnsureStudentSyncSchemaAsync(connection);
        await DatabaseService.EnsurePinSchemaAsync(connection);
        await Sql("DELETE FROM student_sync_receipts; DELETE FROM student_sync_baselines; DELETE FROM students WHERE student_id LIKE 'SYNC-%'; ALTER TABLE students MODIFY photo_data MEDIUMBLOB");
        var database = new DatabaseService();
        StudentSyncRequest Request(StudentSyncProfile profile, string fingerprint = "", bool reset = false) =>
            new(Guid.NewGuid().ToString("N"), "approved-admin", fingerprint, profile, reset);
        var original = Profile();
        await test("SQL student sync creates pending profiles without importing operational state", async () =>
        {
            var result = await database.ApplyStudentSyncAsync(Request(original), true);
            Assert(result.Status == "Applied" && result.Fingerprint == original.Fingerprint());
            Assert(await Scalar("SELECT entry_state FROM students WHERE student_id='SYNC-1'") == "OUTSIDE");
        });
        await test("SQL student sync profile edit preserves main lock, attempts and entry state", async () =>
        {
            await Sql("UPDATE students SET entry_state='INSIDE',pin_locked=1,failed_pin_attempts=3 WHERE student_id='SYNC-1'");
            var result = await database.ApplyStudentSyncAsync(Request(original with { FullName = "Changed" }, original.Fingerprint()), true);
            Assert(result.Status == "Applied");
            Assert(await Scalar("SELECT CONCAT(entry_state,':',pin_locked,':',failed_pin_attempts) FROM students WHERE student_id='SYNC-1'") == "INSIDE:1:3");
        });
        await test("SQL student sync stale edit becomes Needs Review and never overwrites main", async () =>
        {
            var result = await database.ApplyStudentSyncAsync(Request(original with { FullName = "Stale" }, original.Fingerprint()), true);
            Assert(result.Status == "Needs Review" && await Scalar("SELECT full_name FROM students WHERE student_id='SYNC-1'") == "Changed");
        });
        await test("SQL student sync unapproved sender is rejected with a receipt", async () =>
        {
            var request = Request(Profile("SYNC-UNAPPROVED"));
            Assert((await database.ApplyStudentSyncAsync(request, false)).Status == "Rejected");
            Assert(await Scalar("SELECT COUNT(*) FROM students WHERE student_id='SYNC-UNAPPROVED'") == "0");
        });
        var changed = original with { FullName = "Changed" };
        var pin = PinHasher.HashPin("5678");
        var resetProfile = changed with { PinHash = pin.Hash, PinSalt = pin.Salt };
        var resetRequest = Request(resetProfile, changed.Fingerprint(), true);
        await test("SQL student sync explicit PIN reset clears lock and rotates security token", async () =>
        {
            Assert((await database.ApplyStudentSyncAsync(resetRequest, true)).Status == "Applied");
            Assert(await Scalar("SELECT CONCAT(entry_state,':',pin_locked,':',failed_pin_attempts) FROM students WHERE student_id='SYNC-1'") == "INSIDE:0:0");
            Assert((await Scalar("SELECT reset_token FROM pin_security_versions WHERE student_id='SYNC-1'")).Length == 32);
        });
        await test("SQL student sync duplicate reset returns receipt without resetting a later lock", async () =>
        {
            string token = await Scalar("SELECT reset_token FROM pin_security_versions WHERE student_id='SYNC-1'");
            await Sql("UPDATE students SET pin_locked=1,failed_pin_attempts=3 WHERE student_id='SYNC-1'");
            Assert((await database.ApplyStudentSyncAsync(resetRequest, true)).Status == "Applied");
            Assert(await Scalar("SELECT pin_locked FROM students WHERE student_id='SYNC-1'") == "1");
            Assert(await Scalar("SELECT reset_token FROM pin_security_versions WHERE student_id='SYNC-1'") == token);
        });
        await test("SQL student sync changed PIN without reset flag is rejected", async () =>
        {
            var next = PinHasher.HashPin("1234");
            Assert((await database.ApplyStudentSyncAsync(Request(resetProfile with { PinHash = next.Hash, PinSalt = next.Salt }, resetProfile.Fingerprint()), true)).Status == "Rejected");
        });
        await test("SQL student sync cannot bypass graduation clearance", async () =>
        {
            Assert((await database.ApplyStudentSyncAsync(Request(resetProfile with { Status = "Graduated" }, resetProfile.Fingerprint()), true)).Status == "Rejected");
        });
        await test("SQL student sync rejects untrusted QR assignment", async () =>
        {
            Assert((await database.ApplyStudentSyncAsync(Request(resetProfile with { NfcUid = "SYNC-NFC", QrCredential = "fake", Status = "Active" }, resetProfile.Fingerprint()), true)).Status == "Rejected");
        });
        await test("SQL student sync unique NFC collision rolls back and returns Rejected", async () =>
        {
            var p1 = Profile("SYNC-NFC1") with { NfcUid = "SYNC-DUP", QrCredential = "test-qr" };
            Assert((await database.ApplyStudentSyncAsync(Request(p1), true, _ => true)).Status == "Applied");
            Assert((await database.ApplyStudentSyncAsync(Request(p1 with { StudentId = "SYNC-NFC2" }), true, _ => true)).Status == "Rejected");
            Assert(await Scalar("SELECT COUNT(*) FROM students WHERE student_id='SYNC-NFC2'") == "0");
        });
        await test("SQL student sync cannot reuse receipt ID for different content", async () =>
        {
            bool rejected = false;
            try { await database.ApplyStudentSyncAsync(resetRequest with { Profile = resetProfile with { FullName = "Tampered" } }, true); }
            catch (InvalidOperationException) { rejected = true; }
            Assert(rejected);
        });
        await test("SQL profile download protects unsaved local edits and supports explicit replacement", async () =>
        {
            AppSession.IsLoggedIn = true; AppSession.IsAdmin = true; AppSession.IsEventOrganizer = false;
            AppSession.CurrentStaffRoleLabel = "Admin"; DatabaseMonitor.IsOnline = true;
            string server = new string('a', 32);
            bool rejected = false;
            var published = new[] { (resetProfile, resetProfile.Fingerprint()) };
            try { await database.DownloadSyncProfilesAsync(server, published, false); }
            catch (InvalidOperationException) { rejected = true; }
            Assert(rejected);
            Assert(await database.DownloadSyncProfilesAsync(server, published, true) == 1);
            await Sql("UPDATE students SET full_name='Local edit' WHERE student_id='SYNC-1'");
            rejected = false;
            try { await database.DownloadSyncProfilesAsync(server, published, false); }
            catch (InvalidOperationException) { rejected = true; }
            Assert(rejected && await Scalar("SELECT full_name FROM students WHERE student_id='SYNC-1'") == "Local edit");
        });
    }
}
