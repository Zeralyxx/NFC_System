using MySqlConnector;
using NFC_System;

internal static class EventCompletionTests
{
    private static void Assert(bool value) { if (!value) throw new Exception("Completion assertion failed"); }
    private static async Task Reject(Func<Task> action)
    {
        try { await action(); } catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or UnauthorizedAccessException) { return; }
        throw new Exception("Expected completion rejection");
    }

    public static async Task RunAsync(Func<string, Func<Task>, Task> test)
    {
        DateTime start = new(2026, 9, 1, 9, 0, 0);
        var policy = new EventCompletionPolicy(start, start.AddHours(1), 50, 5, 5, 1, start.AddDays(-1));
        EventCompletionScan Scan(int minute, string status, int seq, string device = "device") =>
            new(seq + "-" + device, "A", start.AddMinutes(minute), status, device, seq, false, false);
        var pair = new[] { Scan(0, "PRESENT", 1), Scan(60, "DEPARTED", 2) };
        EventCompletionDecision Evaluate(IEnumerable<EventCompletionScan> scans, EventCompletionPolicy? p = null, bool sync = true, bool active = false) =>
            EventCompletionRules.Evaluate(p ?? policy, scans, sync, active, start.AddHours(2));
        async Task Check(string name, Action action) => await test(name, () => { action(); return Task.CompletedTask; });
        await Check("Completion requires paired duration and final event state", () =>
        {
            Assert(Evaluate(pair).Status == "Completed" && Evaluate(pair).Minutes == 60);
            Assert(Evaluate(pair, active: true).Status == "In Progress");
            Assert(EventCompletionRules.Evaluate(policy, pair[..1], true, false, start.AddMinutes(30)).Status == "In Progress");
            Assert(Evaluate(pair[..1]).Status == "Needs Review");
        });
        await Check("Completion excludes time outside and clips to schedule", () =>
        {
            var gaps = new[] { Scan(-10, "PRESENT", 1), Scan(20, "DEPARTED", 2), Scan(40, "PRESENT", 3), Scan(70, "DEPARTED", 4) };
            Assert(Evaluate(gaps).Minutes == 40 && Evaluate(gaps).Status == "Incomplete");
        });
        await Check("Arrival and departure allowance boundaries are inclusive", () =>
        {
            Assert(Evaluate(new[] { Scan(5, "PRESENT", 1), Scan(55, "DEPARTED", 2) }).Status == "Completed");
            Assert(Evaluate(new[] { Scan(6, "PRESENT", 1), Scan(60, "DEPARTED", 2) }).Status == "Incomplete");
            Assert(Evaluate(new[] { Scan(0, "PRESENT", 1), Scan(54, "DEPARTED", 2) }).Status == "Incomplete");
        });
        foreach (var item in new[]
        {
            ("orphan checkout", new[] { pair[1] }), ("duplicate entry", new[] { pair[0], Scan(1, "PRESENT", 2), Scan(60, "DEPARTED", 3) }),
            ("equal timestamps", new[] { pair[0], Scan(0, "DEPARTED", 2) }),
            ("backward clock", new[] { pair[0] with { Sequence = 2 }, pair[1] with { Sequence = 1 } }),
            ("unknown action", new[] { pair[0], pair[1] with { Status = "COMPLETED" } }),
            ("future timestamp", new[] { pair[0], Scan(180, "DEPARTED", 2) }),
            ("legacy scan", new[] { pair[0] with { Legacy = true }, pair[1] }),
            ("missing device", new[] { pair[0] with { DeviceId = "" }, pair[1] }),
            ("cross-device recovery", new[] { pair[0] with { Recovered = true }, pair[1] with { DeviceId = "peer" } }),
            ("no scans", Array.Empty<EventCompletionScan>())
        }) await Check("Completion withholds " + item.Item1, () => Assert(Evaluate(item.Item2).Status == "Needs Review"));
        await Check("Unready queues and retrospective requirements never finalize automatically", () =>
        {
            Assert(Evaluate(pair, sync: false).Status == "Needs Review");
            Assert(Evaluate(pair, policy with { CoverageSince = start.AddSeconds(1) }).Status == "Needs Review");
            Assert(EventCompletionRules.Evaluate(null, pair, true, false, start.AddHours(2)).Status == "Needs Review");
            Assert(Evaluate(pair.Select(s => s with { Recovered = true })).Status == "Completed");
        });
        await test("Zero-minute check-in-only and invalid requirements are rejected", async () =>
        {
            foreach (var bad in new[] { policy with { MinimumMinutes = 0 }, policy with { MinimumMinutes = 61 }, policy with { End = start },
                policy with { LateAllowanceMinutes = -1 }, policy with { EarlyAllowanceMinutes = 60 }, policy with { End = start.AddDays(8) } })
                await Reject(() => { EventCompletionRules.Validate(bad); return Task.CompletedTask; });
            await Reject(() => { EventCompletionRules.ValidateReason(" "); return Task.CompletedTask; });
        });
        await Check("Manual decisions are bound to evidence and withheld during uncertainty", () =>
        {
            string stamp = EventCompletionRules.Stamp(policy, pair, false, false);
            Assert(stamp == EventCompletionRules.Stamp(policy, pair.Reverse(), false, false));
            var automatic = Evaluate(pair);
            var review = new EventCompletionReview(1, "A", "Incomplete", "Approved correction", "Admin", DateTime.UtcNow, stamp);
            Assert(EventCompletionRules.ApplyReview(automatic, review, stamp, true).Status == "Incomplete");
            string changed = EventCompletionRules.Stamp(policy with { Revision = 2 }, pair, false, false);
            Assert(EventCompletionRules.ApplyReview(automatic, review, changed, true).Status == "Needs Review");
            Assert(EventCompletionRules.ApplyReview(Evaluate(pair, sync: false), review, stamp, false).Status == "Needs Review");
        });
    }

    public static async Task RunDatabaseAsync(Func<string, Func<Task>, Task> test)
    {
        var builder = new MySqlConnectionStringBuilder(DatabaseService.ConnectionString);
        if (builder.Server != "127.0.0.1" || builder.Port != 23306 || builder.Database != "nfc_operations_tests") throw new Exception("Unsafe completion test database");
        using var connection = new MySqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        async Task Sql(string sql) { using var command = new MySqlCommand(sql, connection); await command.ExecuteNonQueryAsync(); }
        async Task<long> Count(string sql) { using var command = new MySqlCommand(sql, connection); return Convert.ToInt64(await command.ExecuteScalarAsync()); }
        await Sql(@"CREATE TABLE IF NOT EXISTS event_attendance(id INT AUTO_INCREMENT PRIMARY KEY,event_id VARCHAR(50),student_id VARCHAR(50),timestamp DATETIME(3),status VARCHAR(50)) ENGINE=InnoDB;
            CREATE TABLE IF NOT EXISTS attendance_devices(device_id VARCHAR(32) PRIMARY KEY,last_seen DATETIME(3),reported_sequence BIGINT,acknowledged_sequence BIGINT,ready BOOLEAN,enabled BOOLEAN) ENGINE=InnoDB;");
        await DatabaseService.EnsureEventCompletionSchemaAsync(connection);
        await DatabaseService.EnsureEventCompletionSchemaAsync(connection);
        await Sql(@"DELETE FROM event_completion_scans; DELETE FROM event_completion_policies; DELETE FROM event_completion_audit;
            DELETE FROM event_attendance; DELETE FROM attendance_devices;
            INSERT INTO events VALUES('completion','Completion',NOW(),'Standard',1,0) ON DUPLICATE KEY UPDATE is_active=0;
            INSERT IGNORE INTO students(student_id,full_name) VALUES('completion-A','Completion Student');
            INSERT IGNORE INTO event_approved_students VALUES('completion','completion-A');
            INSERT INTO attendance_devices VALUES('completion-device',UTC_TIMESTAMP(3),2,2,1,1);");
        AppSession.IsLoggedIn = true; AppSession.IsAdmin = true; AppSession.IsEventOrganizer = false;
        AppSession.CurrentStaffName = "Completion admin"; AppSession.CurrentStaffRoleLabel = "Admin"; DatabaseMonitor.IsOnline = true;
        var db = new DatabaseService();
        var initial = await db.GetEventCompletionReportAsync("completion");
        DateTime start = initial.DatabaseTime.AddHours(-2);
        var policy = new EventCompletionPolicy(start, start.AddHours(1), 50, 5, 5);
        await test("SQL requirements are audited and reject stale revisions", async () =>
        {
            await db.SaveEventCompletionPolicyAsync("completion", policy, "Initial requirements", AppSession.LoginVersion);
            await Reject(() => db.SaveEventCompletionPolicyAsync("completion", policy, "Stale", AppSession.LoginVersion));
            Assert(await Count("SELECT COUNT(*) FROM event_completion_audit WHERE action='Policy'") == 1);
            Assert((await db.GetEventCompletionReportAsync("completion")).Rows.Single().Automatic.Status == "Needs Review");
        });
        await Sql("UPDATE event_completion_policies SET coverage_since=start_time-INTERVAL 1 DAY WHERE event_id='completion'");
        for (int i = 0; i < 2; i++)
        {
            using var insert = new MySqlCommand(@"INSERT INTO event_completion_scans VALUES(@id,'completion','completion-A',@log,@time,@status,'completion-device',@seq,0,0)", connection);
            insert.Parameters.AddWithValue("@id", "completion-" + i); insert.Parameters.AddWithValue("@log", i + 1);
            insert.Parameters.AddWithValue("@time", start.AddMinutes(i * 60)); insert.Parameters.AddWithValue("@status", i == 0 ? "PRESENT" : "DEPARTED"); insert.Parameters.AddWithValue("@seq", i + 1);
            await insert.ExecuteNonQueryAsync();
        }
        async Task Ready() => await Sql("UPDATE attendance_devices SET last_seen=UTC_TIMESTAMP(3),ready=1,reported_sequence=2,acknowledged_sequence=2");
        await test("SQL retained evidence completes even after raw logs were purged", async () =>
        {
            await Ready(); var report = await db.GetEventCompletionReportAsync("completion");
            Assert(report.Rows.Single().Effective.Status == "Completed" && report.Rows.Single().Effective.Minutes == 60);
        });
        await test("SQL review is audited, requires reason, rejects concurrent review", async () =>
        {
            await Ready(); var row = (await db.GetEventCompletionReportAsync("completion")).Rows.Single();
            await Reject(() => db.ReviewEventCompletionAsync("completion", row.StudentId, "Incomplete", " ", row.EvidenceStamp, 0, AppSession.LoginVersion));
            await db.ReviewEventCompletionAsync("completion", row.StudentId, "Incomplete", "Verified exception", row.EvidenceStamp, 0, AppSession.LoginVersion);
            await Reject(() => db.ReviewEventCompletionAsync("completion", row.StudentId, "Completed", "Stale review", row.EvidenceStamp, 0, AppSession.LoginVersion));
            Assert((await db.GetEventCompletionReportAsync("completion")).Rows.Single().Effective.Status == "Incomplete");
            Assert(await Count("SELECT COUNT(*) FROM event_completion_audit WHERE action='Review' AND actor='Completion admin' AND device_name='Operations test device'") == 1);
        });
        await test("SQL changed evidence invalidates manual decisions", async () =>
        {
            await Ready(); await Sql("UPDATE event_completion_scans SET scan_time=scan_time+INTERVAL 1 SECOND WHERE transaction_id='completion-1'");
            Assert((await db.GetEventCompletionReportAsync("completion")).Rows.Single().Effective.Status == "Needs Review");
        });
        await test("SQL stale, unready, missing and disabled contributing devices withhold completion", async () =>
        {
            foreach (string change in new[] { "ready=0", "acknowledged_sequence=1", "last_seen=UTC_TIMESTAMP()-INTERVAL 1 MINUTE", "enabled=0,ready=0" })
            {
                await Ready(); await Sql("UPDATE attendance_devices SET enabled=1," + change);
                var report = await db.GetEventCompletionReportAsync("completion");
                Assert(!report.Synchronized && !report.Rows.Single().CanReview && report.Rows.Single().Effective.Status == "Needs Review");
            }
            await Ready(); await Sql("UPDATE attendance_devices SET device_id='unknown',enabled=1");
            Assert(!(await db.GetEventCompletionReportAsync("completion")).Synchronized);
            await Sql("UPDATE attendance_devices SET device_id='completion-device'");
        });
        await test("SQL delayed untracked scans change review stamp and cannot auto-complete", async () =>
        {
            await Ready(); var before = (await db.GetEventCompletionReportAsync("completion")).Rows.Single();
            await Sql("INSERT INTO event_attendance(event_id,student_id,timestamp,status) VALUES('completion','completion-A',NOW(),'PRESENT')");
            var after = (await db.GetEventCompletionReportAsync("completion")).Rows.Single();
            Assert(before.EvidenceStamp != after.EvidenceStamp && after.Automatic.Status == "Needs Review");
            await Reject(() => db.ReviewEventCompletionAsync("completion", after.StudentId, "Completed", "Stale evidence", before.EvidenceStamp, before.Review!.Id, AppSession.LoginVersion));
        });
        await test("SQL roles, session version, event close and sync gate reviews", async () =>
        {
            await Ready(); var row = (await db.GetEventCompletionReportAsync("completion")).Rows.Single();
            Task Review(long session) => db.ReviewEventCompletionAsync("completion", row.StudentId, "Completed", "Audit reason", row.EvidenceStamp, row.Review!.Id, session);
            await Reject(() => Review(AppSession.LoginVersion - 1));
            AppSession.IsAdmin = false;
            try { await Reject(() => Review(AppSession.LoginVersion)); } finally { AppSession.IsAdmin = true; }
            await Sql("UPDATE events SET is_active=1 WHERE event_id='completion'");
            await Reject(() => Review(AppSession.LoginVersion));
            await Sql("UPDATE events SET is_active=0 WHERE event_id='completion'; UPDATE attendance_devices SET ready=0");
            await Reject(() => Review(AppSession.LoginVersion));
            await Ready();
            AppSession.IsAdmin = false; AppSession.IsEventOrganizer = true;
            try { await Review(AppSession.LoginVersion); } finally { AppSession.IsAdmin = true; AppSession.IsEventOrganizer = false; }
            Assert((await db.GetEventCompletionReportAsync("completion")).Rows.Single().Effective.Status == "Completed");
        });
    }
}
