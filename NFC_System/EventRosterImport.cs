using System;
using System.Collections.Generic;
using System.Linq;

namespace NFC_System;

public static class EventRosterImport
{
    public static IReadOnlyList<EventRosterPreviewRow> Preview(SpreadsheetImportDocument document, IEnumerable<EventRosterStudent> directory)
    {
        var students = directory.ToDictionary(s => s.StudentId, StringComparer.OrdinalIgnoreCase);
        return SpreadsheetService.PreviewRoster(document, students.Keys, students.Values.Where(s => s.IsIncluded).Select(s => s.StudentId))
            .Select(row =>
            {
                students.TryGetValue(row.StudentId, out var student);
                string result = row.Status switch
                {
                    ImportRowStatus.Valid => student?.IsEligible == true ? "Add" : "Ineligible: inactive",
                    ImportRowStatus.Duplicate when student?.IsIncluded == true => "Already included",
                    ImportRowStatus.Unknown => "Unknown student ID",
                    _ => $"Row {row.RowNumber}: {row.Message}"
                };
                return new EventRosterPreviewRow(row.StudentId, student?.FullName ?? "", result);
            }).ToList();
    }
}
