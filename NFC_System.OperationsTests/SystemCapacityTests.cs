using System.Diagnostics;
using MySqlConnector;
using NFC_System;

internal static class SystemCapacityTests
{
    private const int Size = 10000;
    private static void Assert(bool value) { if (!value) throw new Exception("Capacity assertion failed"); }
    private static SpreadsheetTable Profiles() => new("Students",
        new[] { "StudentId", "FullName", "Email", "Course", "YearLevel", "Section" }.Select(h => new SpreadsheetColumn(h)).ToArray(),
        Enumerable.Range(0, Size).Select(i => new object?[] { $"CAPACITY-{i:D5}", $"Student {i:D5}", "", "BSIT", (i % 5 + 1).ToString(), "A" }).ToArray());

    public static async Task RunAsync(Func<string, Func<Task>, Task> test)
    {
        await test("10,000 student CSV/XLSX rows round-trip and preview without ID loss", () =>
        {
            var timer = Stopwatch.StartNew();
            foreach (string extension in new[] { ".csv", ".xlsx" })
            {
                using var stream = new MemoryStream();
                SpreadsheetService.Write(stream, extension, Profiles());
                stream.Position = 0;
                var document = SpreadsheetService.ReadImport(stream, extension, StudentImportKind.Profiles);
                var preview = SpreadsheetService.PreviewProfiles(document, Array.Empty<string>(), new[] { "BSIT" });
                Assert(preview.Count == Size && preview.All(r => r.Status == ImportRowStatus.Valid));
                Assert(preview[0].StudentId == "CAPACITY-00000" && preview[^1].StudentId == "CAPACITY-09999");
            }
            Console.WriteLine($"CAPACITY spreadsheet round-trip: {timer.ElapsedMilliseconds} ms (synthetic data, no UI).");
            return Task.CompletedTask;
        });
        await test("10,000 student directory filters/sorts and roster previews keep complete selections", () =>
        {
            var students = Enumerable.Range(0, Size).Select(i => new StudentRecord { StudentId = $"CAPACITY-{i:D5}", FullName = $"Student {i:D5}",
                Course = "BSIT", YearLevel = (i % 5 + 1).ToString(), SectionName = "A", Status = "Active" }).ToArray();
            var timer = Stopwatch.StartNew();
            var filtered = StudentDirectoryQuery.Apply(students, "", "All Courses", "Active Only", "3", "All Sections", "StudentId", false);
            Assert(filtered.Count == 2000 && filtered[0].StudentId == "CAPACITY-09997" && filtered[^1].StudentId == "CAPACITY-00002");
            var roster = students.Select(s => new EventRosterStudent { StudentId = s.StudentId, FullName = s.FullName, Status = s.Status }).ToArray();
            Assert(EventRosterRules.Preview(students.Select(s => s.StudentId), roster).Additions == Size);
            Console.WriteLine($"CAPACITY directory/roster rules: {timer.ElapsedMilliseconds} ms (synthetic data, no UI).");
            return Task.CompletedTask;
        });
    }

    public static async Task RunDatabaseAsync(Func<string, Func<Task>, Task> test)
    {
        var builder = new MySqlConnectionStringBuilder(DatabaseService.ConnectionString);
        if (builder.Server != "127.0.0.1" || builder.Port != 23306 || builder.Database != "nfc_operations_tests")
            throw new InvalidOperationException("Refusing capacity writes outside the disposable operations database.");
        await test("SQL imports 10,000 Pending Enrollment profiles with no credentials", async () =>
        {
            AppSession.IsLoggedIn = true; AppSession.IsAdmin = true; AppSession.IsEventOrganizer = false;
            AppSession.CurrentStaffRoleLabel = "Master Admin"; DatabaseMonitor.IsOnline = true;
            using var stream = new MemoryStream();
            SpreadsheetService.Write(stream, ".csv", Profiles());
            stream.Position = 0;
            var document = SpreadsheetService.ReadImport(stream, ".csv", StudentImportKind.Profiles);
            var timer = Stopwatch.StartNew();
            int imported = await new DatabaseService().ImportStudentsAsync(document);
            Assert(imported == Size);
            using var connection = new MySqlConnection(builder.ConnectionString);
            await connection.OpenAsync();
            using var count = new MySqlCommand(@"SELECT COUNT(*) FROM students WHERE student_id LIKE 'CAPACITY-%' AND status='Pending Enrollment'
                AND nfc_uid IS NULL AND pin_hash IS NULL AND qr_credential IS NULL AND graduation_clearance='Not Reviewed'", connection);
            Assert(Convert.ToInt32(await count.ExecuteScalarAsync()) == Size);
            Console.WriteLine($"CAPACITY SQL import: {imported} rows in {timer.ElapsedMilliseconds} ms (disposable localhost database).");
        });
    }
}
