using NFC_System;
using System.Text;

internal static class AuditDatabaseRegressionTests
{
    private const string Event = "roster-regression";
    private const string Student = "roster-student";
    private static void Assert(bool condition) { if (!condition) throw new Exception("Assertion failed"); }
    private static async Task Reject(Func<Task> operation)
    {
        try { await operation(); } catch (InvalidOperationException) { return; }
        throw new Exception("Expected rejection");
    }

    internal static async Task RunAsync(DatabaseService db, Func<string, Task> sql, Func<string, Task<string>> scalar,
        Func<string, Func<Task>, Task> test)
    {
        StudentRecord Profile(string id, string name) => new() { StudentId = id, FullName = name,
            Email = "corrected@example.test", Course = "BSIT", YearLevel = "1", SectionName = "X", Status = "Pending Enrollment", NfcUid = "", QrCredential = "" };
        await test("SQL two pending imports can save demographics without NFC, PIN, QR or signing", async () =>
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes("StudentId,FullName,Email,Course,YearLevel,Section\nSTU-001,First,,BSIT,1,X\n000123,Second,,BSIT,1,X"));
            await db.ImportStudentsAsync(SpreadsheetService.ReadImport(stream, ".csv", StudentImportKind.Profiles));
            await db.UpdateStudentWithClearanceAsync("STU-001", Profile("STU-001", "First corrected"), null);
            await db.UpdateStudentWithClearanceAsync("000123", Profile("000123", "Second corrected"), null);
            Assert(await scalar("SELECT COUNT(*) FROM students WHERE student_id IN ('STU-001','000123') AND nfc_uid IS NULL AND qr_credential IS NULL AND pin_hash IS NULL AND status='Pending Enrollment'") == "2");
            Assert(await scalar("SELECT full_name FROM students WHERE student_id='STU-001'") == "First corrected");
        });
        await test("SQL pending ID correction needs no signer and incomplete activation saves nothing", async () =>
        {
            await db.UpdateStudentWithClearanceAsync("STU-001", Profile("STU-002", "First corrected"), null);
            Assert(await scalar("SELECT COUNT(*) FROM students WHERE student_id='STU-002' AND qr_credential IS NULL") == "1");
            var incomplete = Profile("STU-002", "Must not save"); incomplete.Status = "Active";
            await Reject(() => db.UpdateStudentWithClearanceAsync("STU-002", incomplete, null));
            Assert(await scalar("SELECT full_name FROM students WHERE student_id='STU-002'") == "First corrected");
        });

        await sql($"INSERT INTO students(student_id,full_name,status) VALUES('{Student}','Roster test','Active')");
        await db.CreateNewEventAsync(Event, "Roster regression", VerificationMode.Standard, true, AppSession.LoginVersion);
        Task Edit(bool remove) => db.ApplyEventRosterSelectionAsync(Event, new[] { Student }, remove, AppSession.LoginVersion);
        Task<string> Count() => scalar($"SELECT COUNT(*) FROM event_approved_students WHERE event_id='{Event}' AND student_id='{Student}'");
        Task<string> Pending() => scalar($"SELECT CAST(pending AS UNSIGNED) FROM event_roster_sync WHERE event_id='{Event}' AND student_id='{Student}'");
        async Task<FakeRosterStore> Reset()
        {
            await sql($"DELETE FROM event_approved_students WHERE event_id='{Event}'; DELETE FROM event_roster_sync WHERE event_id='{Event}'");
            return new FakeRosterStore(Event, Student);
        }

        await test("SQL add/push/remove/push/pull retains revocation despite legacy cloud approval", async () =>
        {
            var cloud = await Reset();
            await Edit(false); await db.PushEventRosterStateAsync(cloud);
            await Edit(true); await db.PushEventRosterStateAsync(cloud);
            await db.PullEventRosterStateAsync(cloud);
            Assert(await Count() == "0" && cloud.Current?.Approved == false && await Pending() == "0");
            Assert(await db.PullEventRosterStateAsync(cloud) == 0);
        });
        await test("SQL unsent removal survives stale pull, failed upload and service restart", async () =>
        {
            var cloud = await Reset(); await Edit(false); await db.PushEventRosterStateAsync(cloud);
            await Edit(true); await db.PullEventRosterStateAsync(cloud);
            Assert(await Count() == "0" && await Pending() == "1");
            cloud.FailWrites = true;
            try { await db.PushEventRosterStateAsync(cloud); throw new Exception("Expected network failure"); }
            catch (HttpRequestException) { }
            Assert(await Count() == "0" && await Pending() == "1");
            cloud.FailWrites = false;
            await new DatabaseService().PushEventRosterStateAsync(cloud);
            Assert(cloud.Current?.Approved == false && await Pending() == "0");
        });
        await test("SQL intentional re-add supersedes acknowledged removal", async () =>
        {
            var cloud = await Reset(); await Edit(true); await db.PushEventRosterStateAsync(cloud);
            string revoked = cloud.Current!.ChangeId;
            await Edit(false); await db.PushEventRosterStateAsync(cloud); await db.PullEventRosterStateAsync(cloud);
            Assert(cloud.Current.Approved && cloud.Current.ChangeId != revoked && await Count() == "1");
        });
        await test("SQL fresh recovery and repeated schema migration preserve cloud revocations", async () =>
        {
            var cloud = await Reset(); await Edit(true); await db.PushEventRosterStateAsync(cloud);
            await sql($"DELETE FROM event_roster_sync WHERE event_id='{Event}'");
            await db.PullEventRosterStateAsync(cloud);
            using var connection = new MySqlConnector.MySqlConnection(DatabaseService.ConnectionString);
            await connection.OpenAsync();
            await DatabaseService.EnsureEventRosterSyncSchemaAsync(connection);
            await DatabaseService.EnsureEventRosterSyncSchemaAsync(connection);
            Assert(await Count() == "0" && await Pending() == "0");
        });
        await test("SQL cloud conflicts exclude attendee until explicit exclusion review", async () =>
        {
            var cloud = await Reset(); await Edit(false); await db.PushEventRosterStateAsync(cloud);
            await Edit(true); cloud.Force(true);
            await Reject(() => db.PullEventRosterStateAsync(cloud));
            Assert(await Count() == "0" && (await db.GetEventRosterDirectoryAsync(Event)).Single(s => s.StudentId == Student).HasSyncConflict);
            await Reject(() => db.PushEventRosterStateAsync(cloud));
            Assert(await Count() == "0");
            await Edit(true); await db.PushEventRosterStateAsync(cloud);
            Assert(cloud.Current?.Approved == false && !(await db.GetEventRosterDirectoryAsync(Event)).Single(s => s.StudentId == Student).HasSyncConflict);
        });
        await test("SQL compare-and-swap race cannot overwrite concurrent revocation", async () =>
        {
            var cloud = await Reset(); await Edit(false);
            cloud.BeforeWrite = () => { cloud.Force(false); return Task.CompletedTask; };
            await Reject(() => db.PushEventRosterStateAsync(cloud));
            Assert(cloud.Current?.Approved == false && await Count() == "0");
        });
        await test("SQL closed-event conflict can be excluded through review; stale review is rejected", async () =>
        {
            var cloud = await Reset(); await Edit(false); await db.PushEventRosterStateAsync(cloud);
            await Edit(true); cloud.Force(true);
            await Reject(() => db.PullEventRosterStateAsync(cloud));
            var conflict = (await db.GetEventRosterConflictsAsync()).Single(r => r.EventId == Event);
            await sql($"UPDATE events SET is_active=FALSE WHERE event_id='{Event}'");
            try
            {
                await db.ConfirmEventRosterExclusionAsync(conflict, AppSession.LoginVersion);
                await Reject(() => db.ConfirmEventRosterExclusionAsync(conflict, AppSession.LoginVersion));
                await db.PushEventRosterStateAsync(cloud);
                Assert(await Count() == "0" && cloud.Current?.Approved == false);
            }
            finally { await sql($"UPDATE events SET is_active=TRUE WHERE event_id='{Event}'"); }
        });
        await test("SQL acknowledgment does not discard a removal made during upload", async () =>
        {
            var cloud = await Reset(); await Edit(false);
            cloud.BeforeWrite = () => Edit(true);
            await db.PushEventRosterStateAsync(cloud);
            Assert(await Count() == "0" && await Pending() == "1");
            await db.PushEventRosterStateAsync(cloud);
            Assert(cloud.Current?.Approved == false && await Pending() == "0");
        });
        await test("SQL lost cloud acknowledgment retries the same revision without duplicate overwrite", async () =>
        {
            var cloud = await Reset(); await Edit(true); cloud.LoseAcknowledgment = true;
            try { await db.PushEventRosterStateAsync(cloud); throw new Exception("Expected network failure"); }
            catch (HttpRequestException) { }
            string committed = cloud.Current!.ChangeId;
            Assert(await Pending() == "1");
            await db.PushEventRosterStateAsync(cloud);
            Assert(await Pending() == "0" && cloud.Writes == 1 && cloud.Current.ChangeId == committed);
        });
        await test("SQL cloud synchronization lock serializes separate service connections", async () =>
        {
            var cloud = await Reset(); await Edit(false);
            cloud.BeforeWrite = () => Reject(() => new DatabaseService().PushEventRosterStateAsync(cloud));
            await db.PushEventRosterStateAsync(cloud);
            Assert(await Count() == "1" && await Pending() == "0");
        });
        await test("SQL roster-state failure rolls back membership removal", async () =>
        {
            var cloud = await Reset(); await Edit(false); await db.PushEventRosterStateAsync(cloud);
            await sql("CREATE TRIGGER fail_roster_revision BEFORE INSERT ON event_roster_sync FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='forced roster failure'");
            try
            {
                bool failed = false;
                try { await Edit(true); } catch (MySqlConnector.MySqlException) { failed = true; }
                Assert(failed && await Count() == "1" && await Pending() == "0");
            }
            finally { await sql("DROP TRIGGER fail_roster_revision"); }
        });
        await test("SQL nontransactional roster-state table blocks edits", async () =>
        {
            await sql("ALTER TABLE event_roster_sync ENGINE=MyISAM");
            try { await Reject(() => Edit(true)); }
            finally { await sql("ALTER TABLE event_roster_sync ENGINE=InnoDB"); }
        });
        await test("SQL versioned roster read failure never imports legacy approval", async () =>
        {
            var cloud = await Reset(); cloud.FailReads = true;
            try { await db.PullEventRosterStateAsync(cloud); throw new Exception("Expected network failure"); }
            catch (HttpRequestException) { }
            Assert(await Count() == "0" && cloud.LegacyReads == 0);
        });
    }

    private sealed class FakeRosterStore(string eventId, string studentId) : IEventRosterCloudStore
    {
        private int sequence;
        public RosterCloudRevision? Current { get; private set; }
        public bool FailReads, FailWrites, LoseAcknowledgment;
        public int Writes, LegacyReads;
        public Func<Task>? BeforeWrite;
        public void Force(bool approved) => Set(Guid.NewGuid().ToString("N"), approved);
        private void Set(string change, bool approved) => Current = new(eventId, studentId, change, approved,
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddSeconds(++sequence).ToString("O"));
        public Task<IReadOnlyList<RosterCloudRevision>> LoadAsync()
        {
            if (FailReads) throw new HttpRequestException("Forced unavailable cloud");
            return Task.FromResult<IReadOnlyList<RosterCloudRevision>>(Current == null ? Array.Empty<RosterCloudRevision>() : new[] { Current });
        }
        public Task<IReadOnlyList<(string EventId, string StudentId)>> LoadLegacyAsync()
        {
            LegacyReads++;
            return Task.FromResult<IReadOnlyList<(string, string)>>(new[] { (eventId, studentId) });
        }
        public Task<RosterCloudRevision?> ReadAsync(string e, string s) => Task.FromResult(Current);
        public async Task<RosterCloudRevision?> TryWriteAsync(RosterSyncState state, string? expectedUpdateTime)
        {
            if (FailWrites) throw new HttpRequestException("Forced unavailable cloud");
            if (BeforeWrite != null) { var action = BeforeWrite; BeforeWrite = null; await action(); }
            if (Current?.UpdateTime != expectedUpdateTime) return null;
            Set(state.ChangeId, state.Approved); Writes++;
            if (LoseAcknowledgment) { LoseAcknowledgment = false; throw new HttpRequestException("Response lost after cloud commit"); }
            return Current;
        }
    }
}
