using ClosedXML.Excel;
using NFC_System;
using System.Text;

internal static class FeatureTests
{
    private static void Assert(bool condition) { if (!condition) throw new Exception("Assertion failed"); }
    private static void Throws(Action action)
    {
        try { action(); } catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or InvalidDataException or UnauthorizedAccessException) { return; }
        throw new Exception("Expected rejection");
    }
    public static async Task RunAsync(Func<string, Func<Task>, Task> test)
    {
        Task Check(Action action) { action(); return Task.CompletedTask; }
        SpreadsheetImportDocument Read(string csv, StudentImportKind kind = StudentImportKind.Profiles)
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
            return SpreadsheetService.ReadImport(stream, ".csv", kind);
        }
        const string header = "StudentId,FullName,Email,Course,YearLevel,Section\r\n";
        await test("CSV preserves leading-zero IDs, quoted commas, Unicode and escaped quotes", () => Check(() =>
        {
            var document = Read(header + "00-001,\"Dela Cruz, Ana \"\"A\"\"\",ana@example.com,BSIT,1,A\r\n");
            var row = SpreadsheetService.PreviewProfiles(document, Array.Empty<string>(), new[] { "BSIT" }).Single();
            Assert(row.Status == ImportRowStatus.Valid && row.StudentId == "00-001" && row.FullName == "Dela Cruz, Ana \"A\"");
        }));
        await test("Import blocks duplicate IDs, existing IDs, unknown courses and invalid profiles", () => Check(() =>
        {
            var doc = Read(header + "a,A,,BSIT,1,A\na,B,,BSIT,1,A\nb,B,,BSIT,1,A\nc,C,,Unknown,1,A\nd,D,broken,BSIT,1,A");
            var rows = SpreadsheetService.PreviewProfiles(doc, new[] { "b" }, new[] { "BSIT" });
            Assert(rows.Count(r => r.Status == ImportRowStatus.Duplicate) == 3);
            Assert(rows.Count(r => r.Status == ImportRowStatus.Unknown) == 1 && rows.Count(r => r.Status == ImportRowStatus.Error) == 1);
        }));
        await test("Credential columns, malformed headers and XLS are rejected", () => Check(() =>
        {
            Throws(() => Read(header.Replace("Section", "PIN") + "a,A,,BSIT,1,1234"));
            Throws(() => Read("StudentId,StudentId\na,a"));
            using var stream = new MemoryStream();
            Throws(() => SpreadsheetService.ReadImport(stream, ".xls", StudentImportKind.Profiles));
        }));
        foreach (var kind in new[] { StudentImportKind.Profiles, StudentImportKind.Roster })
        await test($"XLSX {kind} template is a real workbook with text IDs and round-trips", () => Check(() =>
        {
            using var output = new MemoryStream();
            SpreadsheetService.WriteTemplate(output, ".xlsx", kind);
            output.Position = 0;
            using var workbook = new XLWorkbook(output);
            var sheet = workbook.Worksheet(1);
            Assert(sheet.Cell(1, 1).GetString() == "StudentId" && sheet.Cell(2, 1).Style.NumberFormat.Format == "@");
            sheet.Cell(2, 1).Value = "00-001";
            if (kind == StudentImportKind.Profiles)
            {
                sheet.Cell(2, 2).Value = "Student A"; sheet.Cell(2, 4).Value = "BSIT";
                sheet.Cell(2, 5).Value = "1"; sheet.Cell(2, 6).Value = "A";
            }
            using var populated = new MemoryStream(); workbook.SaveAs(populated); populated.Position = 0;
            Assert(SpreadsheetService.ReadImport(populated, ".xlsx", kind).Rows.Single().Values["StudentId"] == "00-001");
        }));
        await test("XLSX import rejects formulas and numeric student IDs", () => Check(() =>
        {
            using var workbook = new XLWorkbook(); var sheet = workbook.AddWorksheet("Roster");
            sheet.Cell(1, 1).Value = "StudentId"; sheet.Cell(2, 1).Value = 123;
            sheet.Cell(3, 1).FormulaA1 = "1+1";
            using var stream = new MemoryStream(); workbook.SaveAs(stream); stream.Position = 0;
            Assert(SpreadsheetService.ReadImport(stream, ".xlsx", StudentImportKind.Roster).Rows.All(r => r.Error.Length > 0));
        }));
        await test("Exports preserve typed dates/numbers, IDs, device metadata and literal formula-looking text", () => Check(() =>
        {
            var table = new SpreadsheetTable("Logs", new SpreadsheetColumn[] { new("Student ID"), new("Timestamp", SpreadsheetValueKind.DateTime), new("Latency", SpreadsheetValueKind.Number), new("Device Name") },
                new[] { new object?[] { "00-001", new DateTime(2026, 9, 29, 10, 20, 0), 12.5, "=test" } });
            using var xlsx = new MemoryStream(); SpreadsheetService.Write(xlsx, ".xlsx", table); xlsx.Position = 0;
            using var workbook = new XLWorkbook(xlsx); var sheet = workbook.Worksheet(1);
            Assert(sheet.Cell(2, 1).GetString() == "00-001" && sheet.Cell(2, 2).DataType == XLDataType.DateTime);
            Assert(sheet.Cell(2, 3).GetDouble() == 12.5 && !sheet.Cell(2, 4).HasFormula && sheet.Cell(2, 4).GetString() == "=test");
            using var csv = new MemoryStream(); SpreadsheetService.Write(csv, ".csv", table);
            Assert(Encoding.UTF8.GetString(csv.ToArray()).Contains("\"'=test\""));
        }));
        var cleared = new GraduationStudent { StudentId = "a", FullName = "A", Course = "BSIT", YearLevel = "5", EnrollmentStatus = "Active", Clearance = GraduationClearanceStatus.Cleared, Revision = 2, HasNfc = true, HasPin = true };
        await test("Graduation preview excludes holds and unreviewed students", () => Check(() =>
        {
            var preview = GraduationRules.Preview(new[] { cleared, cleared with { StudentId = "b", Clearance = GraduationClearanceStatus.OnHold }, cleared with { StudentId = "c", Clearance = GraduationClearanceStatus.NotReviewed } }, "Graduated", 1, DateTime.UtcNow);
            Assert(preview.EligibleCount == 1 && preview.ExcludedCount == 2);
        }));
        await test("Graduation recheck rejects stale hold, revision, group and status changes", () => Check(() =>
        {
            foreach (var changed in new[] { cleared with { Clearance = GraduationClearanceStatus.OnHold }, cleared with { Revision = 3 }, cleared with { Course = "Other" }, cleared with { EnrollmentStatus = "Inactive" } })
                Throws(() => GraduationRules.ValidateRecheck(cleared, changed, "Graduated"));
            GraduationRules.ValidateRecheck(cleared, cleared, "Graduated");
        }));
        await test("Pending imports require enrolled NFC and PIN to activate; holds do not prohibit Active status", () => Check(() =>
        {
            Throws(() => GraduationRules.RequireActivationCredentials("Active", true, false));
            Throws(() => GraduationRules.RequireActivationCredentials("Active", false, true));
            Throws(() => GraduationRules.RequireNewStudentStatus("Graduated", "id", true));
            GraduationRules.RequireGraduationClearance(cleared with { Clearance = GraduationClearanceStatus.OnHold }, "Active");
        }));
        await test("Expired and future previews rejected; clearance reasons required", () => Check(() =>
        {
            var now = DateTime.UtcNow; var p = GraduationRules.Preview(new[] { cleared }, "Graduated", 1, now);
            Throws(() => GraduationRules.ValidatePreviewAge(p, now.AddMinutes(16)));
            Throws(() => GraduationRules.ValidatePreviewAge(p, now.AddMinutes(-1)));
            Throws(() => GraduationRules.ValidateReason(GraduationClearanceStatus.OnHold, " "));
        }));
        await test("Graduation changes require the same online administrator session", () => Check(() =>
        {
            AppSession.IsLoggedIn = true; AppSession.IsAdmin = true; AppSession.CurrentStaffRoleLabel = "Admin";
            long version = GraduationRules.RequireAdministrator(true);
            Throws(() => GraduationRules.RequireAdministrator(false));
            AppSession.IsLoggedIn = false;
            Throws(() => GraduationRules.RequireAdministrator(true, version));
        }));
        await test("Roster filters cover section/status/year groups and previews classify every selected ID", () => Check(() =>
        {
            var rows = new[] { new EventRosterStudent { StudentId = "a", FullName = "A", Course = "BSIT", YearLevel = "6", SectionName = "X", Status = "Active" },
                new EventRosterStudent { StudentId = "b", Status = "Inactive" }, new EventRosterStudent { StudentId = "c", Status = "Active", IsIncluded = true } };
            Assert(EventRosterRules.Filter(rows, "a", "BSIT", "5+", "X", "Active").Count == 1);
            var p = EventRosterRules.Preview(new[] { "a", "a", "b", "c", "d" }, rows);
            Assert(p.Additions == 1 && p.DuplicateCount == 1 && p.Ineligible == 1 && p.AlreadyIncluded == 1 && p.Unknown == 1);
        }));
        await test("Mode edits reject closed/stale events and preserve date and restriction", () => Check(() =>
        {
            var ev = new EventRecord { EventId = "e", EventName = "Event", EventDate = DateTime.Today, IsRestricted = true, VerificationMode = VerificationMode.Standard };
            EventEditPolicy.ValidateEdit(ev, "Event", VerificationMode.Standard, "New", VerificationMode.Fast);
            Assert(ev.IsRestricted && ev.EventDate == DateTime.Today && ev.Status == "Active");
            Throws(() => EventEditPolicy.ValidateEdit(ev, "Old", VerificationMode.Standard, "New", VerificationMode.Fast));
            ev.Status = "Closed"; Throws(() => EventEditPolicy.ValidateEdit(ev, "Event", VerificationMode.Standard, "New", VerificationMode.Fast));
            Assert(!EventEditPolicy.CanChangeMode(true, false, "Event Organizer"));
        }));
        await test("Event mode notifications stay scoped and preserve independent gate direction", () => Check(() =>
        {
            KioskStateController.BroadcastModeChange(VerificationMode.Standard, TransactionType.Exit);
            KioskStateController.BroadcastEventStateChange("one", VerificationMode.Fast, TransactionType.Entry);
            KioskStateController.BroadcastEventStateChange("two", VerificationMode.Standard, TransactionType.Exit);
            KioskStateController.BroadcastEventModeChange("one", VerificationMode.HighSecurity);
            Assert(KioskStateController.GetEventState("one") == (VerificationMode.HighSecurity, TransactionType.Entry));
            Assert(KioskStateController.GetEventState("two") == (VerificationMode.Standard, TransactionType.Exit));
            Assert(KioskStateController.CurrentMode == VerificationMode.Standard && KioskStateController.CurrentType == TransactionType.Exit);
        }));
    }
}
