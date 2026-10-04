using System;
using System.Collections.Generic;
using System.Linq;

namespace NFC_System;

public static class StudentDirectoryQuery
{
    public static List<StudentRecord> Apply(IEnumerable<StudentRecord> students, string search, string course,
        string status, string year, string section, string sortColumn, bool ascending, IReadOnlySet<string>? importIds = null)
    {
        string query = search.Trim();
        var filtered = students.Where(s =>
            (query.Length == 0 || new[] { s.StudentId, s.FullName, s.NfcUid, s.Course, s.YearLevel, s.SectionName, s.Status }
                .Any(value => value?.Contains(query, StringComparison.OrdinalIgnoreCase) == true)) &&
            (course == "All Courses" || s.Course == course) &&
            (year == "All Years" || s.YearLevel == year) &&
            (section == "All Sections" || s.SectionName == section) &&
            (status == "All Students" || (status == "Locked Out" ? s.PinLocked : s.Status == (status == "Active Only" ? "Active" : status))) &&
            (importIds == null || importIds.Contains(s.StudentId)));

        Func<StudentRecord, string> key = sortColumn switch
        {
            "StudentId" => s => s.StudentId,
            "Course" => s => s.Course,
            "Year" => s => s.YearLevel,
            "Section" => s => s.SectionName,
            "Status" => s => s.Status,
            _ => s => s.FullName
        };
        IOrderedEnumerable<StudentRecord> ordered;
        if (sortColumn == "Year")
        {
            // Numeric years sort as 1, 2, 10; unknown years stay after numeric values in either direction.
            ordered = filtered.OrderBy(s => !int.TryParse(s.YearLevel, out _));
            ordered = ascending ? ordered.ThenBy(s => Year(s.YearLevel)) : ordered.ThenByDescending(s => Year(s.YearLevel));
            ordered = ascending ? ordered.ThenBy(key, StringComparer.OrdinalIgnoreCase) : ordered.ThenByDescending(key, StringComparer.OrdinalIgnoreCase);
        }
        else
            ordered = ascending ? filtered.OrderBy(key, StringComparer.OrdinalIgnoreCase) : filtered.OrderByDescending(key, StringComparer.OrdinalIgnoreCase);
        return ordered.ThenBy(s => s.StudentId, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static int Year(string value) => int.TryParse(value, out int year) ? year : 0;
}
