using NFC_System;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

internal static class AuditRegressionTests
{
    private static void Assert(bool condition) { if (!condition) throw new Exception("Assertion failed"); }
    private static void Reject(Action action)
    {
        try { action(); } catch (InvalidOperationException) { return; }
        throw new Exception("Expected rejection");
    }

    public static async Task RunAsync(Func<string, Func<Task>, Task> test)
    {
        await test("Alphanumeric and leading-zero IDs survive shared import/edit validation", () =>
        {
            foreach (string id in new[] { "STU-001", "000123" })
            {
                Assert(StudentProfileRules.ValidateStudentId(id) == "");
                StudentProfileRules.ValidateEdit(id, "Corrected Name", "Pending Enrollment", "", false);
                Assert(SpreadsheetService.ValidateProfile(new(id, "Corrected Name", "", "BSIT", "1", "X")) == "");
            }
            foreach (string id in new[] { "", "A B", "=FORMULA", "+ID", "@ID", new string('A', 51) })
                Reject(() => StudentProfileRules.ValidateEdit(id, "Name", "Pending Enrollment", "", false));
            return Task.CompletedTask;
        });
        await test("Pending demographic corrections and ID corrections do not request signing", () =>
        {
            Assert(!StudentProfileRules.ShouldOfferQrReplacement("", ""));
            Assert(!StudentProfileRules.ShouldIssueQr("STU-001", "", "", "STU-002", "", false));
            Assert(StudentProfileRules.ShouldIssueQr("STU-001", "", "", "STU-001", "CARD", false));
            Assert(StudentProfileRules.ShouldIssueQr("STU-001", "OLD", "NFC1.current", "STU-001", "NEW", false));
            Assert(StudentProfileRules.ShouldOfferQrReplacement("CARD", "legacy"));
            Assert(!StudentProfileRules.ShouldIssueQr("STU-001", "CARD", "NFC1.current", "STU-001", "CARD", false));
            Reject(() => StudentProfileRules.ValidateEdit("STU-001", "Name", "Active", "", false));
            Reject(() => StudentProfileRules.ValidateEdit("STU-001", "Name", "Pending Enrollment", "", true));
            return Task.CompletedTask;
        });
        await test("Registration and edit XAML leave student IDs as text; year fields retain numeric filtering", () =>
        {
            string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../..", "NFC_System"));
            XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
            foreach (var (file, idName, yearName) in new[] {
                ("RegistrationWindow.xaml", "StudentIdTextBox", "YearLevelTextBox"),
                ("StudentManagementWindow.xaml", "EditDialogStudentIdBox", "EditDialogYearLevelBox") })
            {
                var document = XDocument.Load(Path.Combine(root, file));
                var id = document.Descendants().Single(e => (string?)e.Attribute(x + "Name") == idName);
                var year = document.Descendants().Single(e => (string?)e.Attribute(x + "Name") == yearName);
                Assert(id.Attribute("TextChanging") == null && (string?)year.Attribute("TextChanging") == "NumberOnly_TextChanging");
            }
            return Task.CompletedTask;
        });
        await test("Roster merge keeps unsent removal over stale approval and blocks concurrent revisions", () =>
        {
            var approved = Remote(true, "a", 1);
            var local = EventRosterSyncRules.Merge(null, approved) with { ChangeId = Id("b"), Approved = false, Pending = true };
            Assert(EventRosterSyncRules.Merge(local, approved) == local);
            var conflict = EventRosterSyncRules.Merge(local, Remote(true, "c", 2));
            Assert(conflict.Conflict && !conflict.Approved && conflict.Pending);
            Assert(EventRosterSyncRules.Merge(conflict, Remote(true, "c", 2)).Conflict);
            var removed = EventRosterSyncRules.Merge(null, Remote(false, "d", 3));
            Assert(EventRosterSyncRules.Merge(removed, approved) == removed);
            return Task.CompletedTask;
        });
        await test("Roster acknowledgment retains edits made while upload was in flight", () =>
        {
            var sent = new RosterSyncState("event", "student", Id("a"), true, "", "", true, false);
            var next = sent with { ChangeId = Id("b"), Approved = false };
            var result = EventRosterSyncRules.Acknowledge(next, sent, Remote(true, "a", 1));
            Assert(result.ChangeId == next.ChangeId && !result.Approved && result.Pending && !result.Conflict && result.CloudChangeId == sent.ChangeId);
            return Task.CompletedTask;
        });
        await test("Firestore roster writes use create/update preconditions and retain revocation payload", async () =>
        {
            var sent = new RosterSyncState("event", "student", Id("a"), false, "", "", true, false);
            var handler = new RosterHttpHandler(_ => new(HttpStatusCode.OK) { Content = new StringContent(Document(Remote(false, "a", 2))) });
            using var http = new HttpClient(handler);
            var store = new FirestoreEventRosterStore(http, "https://test.invalid/documents", "test");
            await store.TryWriteAsync(sent, null);
            Assert(handler.Urls.Last().Contains("currentDocument.exists=false"));
            using var payload = JsonDocument.Parse(handler.Bodies.Last());
            Assert(!payload.RootElement.GetProperty("fields").GetProperty("approved").GetProperty("booleanValue").GetBoolean());
            await store.TryWriteAsync(sent, Remote(true, "b", 1).UpdateTime);
            Assert(handler.Urls.Last().Contains("currentDocument.updateTime="));
        });
        await test("Firestore rejects stale conditional writes without unconditional retries", async () =>
        {
            var handler = new RosterHttpHandler(_ => new(HttpStatusCode.BadRequest) {
                Content = new StringContent("{\"error\":{\"status\":\"FAILED_PRECONDITION\"}}") });
            using var http = new HttpClient(handler);
            var store = new FirestoreEventRosterStore(http, "https://test.invalid/documents", "test");
            Assert(await store.TryWriteAsync(new("event", "student", Id("a"), true, "", "", true, false), "2026-01-01T00:00:00Z") == null);
            Assert(handler.Urls.Count == 1);
        });
        await test("Malformed versioned roster does not fall back to approval-only archive", async () =>
        {
            using var http = new HttpClient(new RosterHttpHandler(_ => new(HttpStatusCode.OK) {
                Content = new StringContent("{\"documents\":[{\"fields\":{}}]}") }));
            var store = new FirestoreEventRosterStore(http, "https://test.invalid/documents", "test");
            bool rejected = false;
            try { await store.LoadAsync(); } catch (Exception ex) when (ex is KeyNotFoundException or InvalidDataException) { rejected = true; }
            Assert(rejected);
        });
    }

    private static string Id(string letter) => new(letter[0], 32);
    private static RosterCloudRevision Remote(bool approved, string letter, int second) =>
        new("event", "student", Id(letter), approved, $"2026-01-01T00:00:{second:00}Z");
    private static string Document(RosterCloudRevision row) => JsonSerializer.Serialize(new
    {
        name = "projects/test/databases/(default)/documents/event_roster_state/" + EventRosterSyncRules.DocumentId(row.EventId, row.StudentId),
        updateTime = row.UpdateTime,
        fields = new Dictionary<string, object> { ["event_id"] = new { stringValue = row.EventId },
            ["student_id"] = new { stringValue = row.StudentId }, ["change_id"] = new { stringValue = row.ChangeId },
            ["approved"] = new { booleanValue = row.Approved }, ["schema_version"] = new { integerValue = "1" } }
    });

    private sealed class RosterHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<string> Urls { get; } = new();
        public List<string> Bodies { get; } = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Urls.Add(request.RequestUri!.AbsoluteUri);
            Bodies.Add(request.Content == null ? "" : await request.Content.ReadAsStringAsync(cancellationToken));
            return respond(request);
        }
    }
}
