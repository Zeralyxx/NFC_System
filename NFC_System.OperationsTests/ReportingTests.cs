using MySqlConnector;
using NFC_System;

internal static class ReportingTests
{
    private static void Assert(bool condition) { if (!condition) throw new Exception("Reporting assertion failed"); }
    private static AttendanceLog Log(string id, string status, int minute) => new()
    {
        StudentId = id, Status = status, RawTimestamp = new DateTime(2026, 1, 1, 9, 0, 0).AddMinutes(minute),
        Course = "BSIT", Section = "A", Mode = "Standard"
    };

    public static async Task RunAsync(Func<string, Func<Task>, Task> test)
    {
        await test("Event analytics count unique arrivals, not repeated scans", () =>
        {
            var summary = ReportingAnalytics.SummarizeEvent(new[] { Log("A", "PRESENT", 0), Log("A", "DEPARTED", 1), Log("A", "PRESENT", 2), Log("B", "PRESENT", 0) });
            Assert(summary.Arrivals.Count == 2 && summary.LastPresent == 2);
            Assert(summary.Arrivals.Single(a => a.StudentId == "A").RawTimestamp.Minute == 0);
            return Task.CompletedTask;
        });
        await test("Event closing cannot manufacture completion; latest state is chronological", () =>
        {
            var summary = ReportingAnalytics.SummarizeEvent(new[] { Log("A", "DEPARTED", 2), Log("A", "PRESENT", 1) });
            Assert(summary.StateFor("a") == ReportingAnalytics.LastDeparted && summary.LastPresent == 0);
            Assert(!summary.StateFor("A").Contains("Completed"));
            return Task.CompletedTask;
        });
        await test("Event ambiguous timestamps and departure-only history remain unresolved", () =>
        {
            var summary = ReportingAnalytics.SummarizeEvent(new[] { Log("A", "PRESENT", 1), Log("A", "DEPARTED", 1), Log("B", "DEPARTED", 0), new AttendanceLog { StudentId = "C", Status = "PRESENT" } });
            Assert(new[] { "A", "B", "C", "missing" }.All(id => summary.StateFor(id) == ReportingAnalytics.Unresolved));
            Assert(summary.LastPresent == 0);
            return Task.CompletedTask;
        });
        await test("Event turnout uses unique current roster intersection, with no denominator reported as unavailable", () =>
        {
            var summary = ReportingAnalytics.SummarizeEvent(new[] { Log("A", "PRESENT", 0), Log("A", "PRESENT", 1), Log("B", "PRESENT", 0), Log("", "PRESENT", 0) });
            Assert(summary.Turnout(new[] { "a", "A", "C" }) == 50);
            Assert(summary.Turnout(Array.Empty<string>()) == null);
            Assert(summary.Arrivals.Count == 2);
            return Task.CompletedTask;
        });
        await test("Report date filter includes year and time-of-day filtering does not parse display text", () =>
        {
            Assert(!ReportingAnalytics.MatchesDate(new DateTime(2025, 9, 16), new DateTime(2026, 9, 16)));
            Assert(ReportingAnalytics.MatchesDate(new DateTime(2026, 9, 16, 23, 59, 59), new DateTime(2026, 9, 16)));
            Assert(ReportingAnalytics.MatchesTime(new DateTime(2026, 1, 1, 0, 0, 0), "Morning (AM)"));
            Assert(!ReportingAnalytics.MatchesTime(new DateTime(2026, 1, 1, 12, 0, 0), "Morning (AM)"));
            Assert(ReportingAnalytics.MatchesTime(new DateTime(2026, 1, 1, 12, 0, 0), "Afternoon (PM)"));
            Assert(!ReportingAnalytics.MatchesTime(DateTime.MinValue, "Afternoon (PM)"));
            return Task.CompletedTask;
        });
    }

    public static async Task RunDatabaseAsync(Func<string, Func<Task>, Task> test)
    {
        var builder = new MySqlConnectionStringBuilder(DatabaseService.ConnectionString);
        if (builder.Server != "127.0.0.1" || builder.Port != 23306 || builder.Database != "nfc_operations_tests")
            throw new InvalidOperationException("Refusing to seed outside the disposable operations database.");
        using var connection = new MySqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        async Task Sql(string sql) { using var cmd = new MySqlCommand(sql, connection); await cmd.ExecuteNonQueryAsync(); }
        foreach (string table in new[] { "fast_mode_logs", "standard_mode_logs", "high_security_mode_logs" })
        {
            await Sql($"CREATE TABLE IF NOT EXISTS {table}(timestamp DATETIME NOT NULL, is_granted BOOLEAN NOT NULL, transaction_type VARCHAR(30) NOT NULL) ENGINE=InnoDB");
            await Sql($"DELETE FROM {table}");
        }
        await test("SQL daily totals include more than 500 logs across all modes and exclude event scans", async () =>
        {
            string values = string.Join(",", Enumerable.Range(0, 750).Select(_ => "(CURDATE(), 1, 'Entry')"));
            await Sql("INSERT INTO fast_mode_logs VALUES " + values);
            await Sql("INSERT INTO standard_mode_logs VALUES (CURDATE(),0,'Entry'), (CURDATE(),1,'Exit')");
            await Sql("INSERT INTO high_security_mode_logs VALUES (CURDATE(),0,'Exit'), (CURDATE(),1,'EventAttendance')");
            var rows = await new DatabaseService().GetDailyTrafficSummariesAsync();
            Assert(rows.Count == 1 && rows[0].TotalScans == 753 && rows[0].Granted == 751 && rows[0].Denied == 2);
        });
        await test("SQL alert extremes include recorded zero-denial days but exclude dates outside 30-day window", async () =>
        {
            await Sql("INSERT INTO standard_mode_logs VALUES (DATE_SUB(CURDATE(),INTERVAL 1 DAY),1,'Entry'), (DATE_SUB(CURDATE(),INTERVAL 30 DAY),0,'Entry'), (DATE_ADD(CURDATE(),INTERVAL 1 DAY),0,'Entry')");
            using var cmd = new MySqlCommand(ReportingQueries.AlertExtremes, connection);
            using var reader = await cmd.ExecuteReaderAsync();
            var totals = new List<int>();
            while (await reader.ReadAsync()) totals.Add(reader.GetInt32(1));
            Assert(totals.SequenceEqual(new[] { 2, 0 }));
        });
    }
}
