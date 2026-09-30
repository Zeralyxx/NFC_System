using System;
using System.Collections.Generic;
using System.Linq;

namespace NFC_System;

public sealed class EventRosterStudent
{
    public string StudentId { get; init; } = "";
    public string FullName { get; init; } = "";
    public string Course { get; init; } = "";
    public string YearLevel { get; init; } = "";
    public string SectionName { get; init; } = "";
    public string Status { get; init; } = "";
    public bool IsIncluded { get; init; }
    public bool HasSyncConflict { get; init; }
    public bool IsEligible => string.Equals(Status, "Active", StringComparison.OrdinalIgnoreCase);
    public string Detail => $"{StudentId} | {Course} | Year {YearLevel} | {SectionName} | {Status}";
    public string RosterStatus => HasSyncConflict ? "Sync conflict: excluded" : IsIncluded ? "Already included" : IsEligible ? "Eligible" : "Ineligible: inactive";
}

public sealed record EventRosterPreviewRow(string StudentId, string FullName, string Result);

public sealed class EventRosterPreview
{
    public IReadOnlyList<EventRosterPreviewRow> Rows { get; init; } = Array.Empty<EventRosterPreviewRow>();
    public int Additions => Rows.Count(r => r.Result == "Add");
    public int AlreadyIncluded => Rows.Count(r => r.Result == "Already included");
    public int Ineligible => Rows.Count(r => r.Result == "Ineligible: inactive");
    public int Unknown => Rows.Count(r => r.Result == "Unknown student ID");
    public int DuplicateCount { get; init; }
    public string Summary => $"{Additions} additions; {AlreadyIncluded} already included; {Ineligible} ineligible; {Unknown} unknown; {DuplicateCount} duplicate IDs.";
}

public static class EventRosterRules
{
    public const int MaximumSelection = 10000;

    public static string[] NormalizeIds(IEnumerable<string> ids)
    {
        var result = ids.Select(id => (id ?? "").Trim()).Where(id => id.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (result.Length > MaximumSelection)
            throw new ArgumentException($"Select no more than {MaximumSelection:N0} students per operation.");
        if (result.Any(id => id.Length > 50)) throw new ArgumentException("A student ID exceeds 50 characters.");
        return result;
    }

    public static IReadOnlyList<EventRosterStudent> Filter(IEnumerable<EventRosterStudent> students,
        string? search, string? course, string? year, string? section, string? status)
    {
        static bool Equal(string left, string? right) => string.IsNullOrWhiteSpace(right) ||
            string.Equals(left, right.Trim(), StringComparison.OrdinalIgnoreCase);
        search = search?.Trim();
        return students.Where(s =>
            (string.IsNullOrEmpty(search) || s.StudentId.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                s.FullName.Contains(search, StringComparison.OrdinalIgnoreCase)) &&
            Equal(s.Course, course) && Equal(s.SectionName, section) && Equal(s.Status, status) &&
            (year == "5+" ? int.TryParse(s.YearLevel, out int number) && number >= 5 : Equal(s.YearLevel, year)))
            .OrderBy(s => s.FullName, StringComparer.OrdinalIgnoreCase).ThenBy(s => s.StudentId).ToList();
    }

    public static EventRosterPreview Preview(IEnumerable<string> requested, IEnumerable<EventRosterStudent> directory)
    {
        var raw = requested.Where(id => !string.IsNullOrWhiteSpace(id)).ToArray();
        var ids = NormalizeIds(raw);
        var byId = directory.ToDictionary(s => s.StudentId, StringComparer.OrdinalIgnoreCase);
        return new EventRosterPreview
        {
            DuplicateCount = raw.Length - ids.Length,
            Rows = ids.Select(id => byId.TryGetValue(id, out var student)
                ? new EventRosterPreviewRow(student.StudentId, student.FullName,
                    student.IsIncluded ? "Already included" : student.IsEligible ? "Add" : "Ineligible: inactive")
                : new EventRosterPreviewRow(id, "", "Unknown student ID")).ToList()
        };
    }
}

public static class EventEditPolicy
{
    public static bool CanManage(bool loggedIn, bool admin, bool organizer) => loggedIn && (admin || organizer);
    public static bool CanChangeMode(bool loggedIn, bool admin, string role) =>
        loggedIn && admin && role is "Admin" or "Master Admin";

    public static void ValidateEdit(EventRecord current, string expectedName, VerificationMode expectedMode,
        string newName, VerificationMode newMode)
    {
        if (string.IsNullOrWhiteSpace(newName) || newName.Trim().Length > 150)
            throw new ArgumentException("Event name must contain 1 to 150 characters.");
        if (!Enum.IsDefined(newMode)) throw new ArgumentException("Invalid verification mode.");
        if (current.Status != "Active") throw new InvalidOperationException("This event is closed. Refresh the event list.");
        if (current.VerificationMode != expectedMode || current.EventName != expectedName)
            throw new InvalidOperationException("This event changed since it was opened. Reload it before saving.");
    }
}
