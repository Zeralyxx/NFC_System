using NFC_System;
using System.Xml.Linq;

internal static class StudentDirectoryTests
{
    private static void Assert(bool condition) { if (!condition) throw new Exception("Directory assertion failed"); }
    public static async Task RunAsync(Func<string, Func<Task>, Task> test)
    {
        var students = new[]
        {
            new StudentRecord { StudentId = "001", FullName = "Zoe", Course = "BSIT", YearLevel = "10", SectionName = "A", Status = "Pending Enrollment" },
            new StudentRecord { StudentId = "002", FullName = "amy", Course = "BSIT", YearLevel = "2", SectionName = "B", Status = "Pending Enrollment" },
            new StudentRecord { StudentId = "A-003", FullName = "Ben", Course = "BSCS", YearLevel = "1", SectionName = "A", Status = "Active", PinLocked = true, NfcUid = "ABC123" },
            new StudentRecord { StudentId = "004", FullName = "Amy", Course = "BSIT", YearLevel = "N/A", SectionName = "B", Status = "Graduated" }
        };
        List<StudentRecord> Query(string search = "", string course = "All Courses", string status = "All Students",
            string year = "All Years", string section = "All Sections", string sort = "FullName", bool asc = true, IReadOnlySet<string>? ids = null) =>
            StudentDirectoryQuery.Apply(students, search, course, status, year, section, sort, asc, ids);

        await test("Directory combines search, pending status, course, year, section and import IDs", () =>
        {
            var result = Query(" AMY ", "BSIT", "Pending Enrollment", "2", "B", ids: new HashSet<string> { "002" });
            Assert(result.Count == 1 && result[0].StudentId == "002");
            Assert(Query(status: "Pending Enrollment").Count == 2);
            Assert(Query(search: "abc123").Single().StudentId == "A-003");
            return Task.CompletedTask;
        });
        await test("Directory latest batch excludes older pending profiles without dropping active batch members", () =>
        {
            var ids = new HashSet<string>(new[] { "002", "a-003" }, StringComparer.OrdinalIgnoreCase);
            Assert(Query(ids: ids).Select(s => s.StudentId).SequenceEqual(new[] { "002", "A-003" }));
            Assert(Query(ids: new HashSet<string>()).Count == 0);
            Assert(Query().Count == 4);
            return Task.CompletedTask;
        });
        await test("Directory exposes every enrollment status and independent PIN lock filter", () =>
        {
            foreach (string status in new[] { "Active", "Inactive", "Graduated", "Expelled", "Pending Enrollment" })
            {
                var rows = new[] { new StudentRecord { Status = status } };
                Assert(StudentDirectoryQuery.Apply(rows, "", "All Courses", status, "All Years", "All Sections", "FullName", true).Count == 1);
            }
            Assert(Query(status: "Locked Out").Single().StudentId == "A-003");
            Assert(Query(status: "Active Only").Single().StudentId == "A-003");
            Assert(Query(status: "Inactive").Count == 0);
            return Task.CompletedTask;
        });
        await test("Directory sorts numerically by year in both directions with unknown years last", () =>
        {
            Assert(Query(sort: "Year").Select(s => s.YearLevel).SequenceEqual(new[] { "1", "2", "10", "N/A" }));
            Assert(Query(sort: "Year", asc: false).Select(s => s.YearLevel).SequenceEqual(new[] { "10", "2", "1", "N/A" }));
            return Task.CompletedTask;
        });
        await test("Directory text sorting is case-insensitive, stable and preserves leading-zero IDs", () =>
        {
            Assert(Query().Select(s => s.StudentId).SequenceEqual(new[] { "002", "004", "A-003", "001" }));
            Assert(Query(asc: false).Select(s => s.StudentId).SequenceEqual(new[] { "001", "A-003", "002", "004" }));
            foreach (string column in new[] { "StudentId", "Course", "Section", "Status" })
            {
                Func<StudentRecord, string> key = column switch { "StudentId" => s => s.StudentId, "Course" => s => s.Course, "Section" => s => s.SectionName, _ => s => s.Status };
                Assert(Query(sort: column).Select(key).SequenceEqual(students.Select(key).OrderBy(s => s, StringComparer.OrdinalIgnoreCase)));
                Assert(Query(sort: column, asc: false).Select(key).SequenceEqual(students.Select(key).OrderByDescending(s => s, StringComparer.OrdinalIgnoreCase)));
            }
            return Task.CompletedTask;
        });
        await test("Directory sorting precedes pagination and empty combined filters return no rows", () =>
        {
            var rows = Enumerable.Range(1, 25).Select(n => new StudentRecord { StudentId = n.ToString("D3") }).Reverse();
            var sorted = StudentDirectoryQuery.Apply(rows, "", "All Courses", "All Students", "All Years", "All Sections", "StudentId", true);
            Assert(sorted.Skip(10).Take(10).First().StudentId == "011");
            Assert(sorted.Last().StudentId == "025");
            Assert(Query(course: "BSCS", year: "10").Count == 0);
            return Task.CompletedTask;
        });
        await test("Both directory views expose pending enrollment and the same refinement control", () =>
        {
            string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../..", "NFC_System"));
            XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
            var doc = XDocument.Load(Path.Combine(root, "StudentManagementWindow.xaml"));
            foreach (string name in new[] { "StatusFilterComboBox", "PopupStatusFilter" })
                Assert(doc.Descendants().Single(e => (string?)e.Attribute(x + "Name") == name).Elements().Any(e => (string?)e.Attribute("Content") == "Pending Enrollment"));
            Assert(doc.Descendants().Count(e => e.Name.LocalName == "DirectoryRefinementBar" && e.Attribute("Changed") != null) == 2);
            return Task.CompletedTask;
        });
    }
}
