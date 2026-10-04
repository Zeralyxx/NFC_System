using System;
using System.Collections.Generic;
using System.Linq;

namespace NFC_System;

public sealed class AttendanceLog
{
    public string DeviceId { get; set; } = "";
    public string DeviceName { get; set; } = "Unknown/Legacy";
    public DateTime RawTimestamp { get; set; }
    public string Timestamp { get; set; } = "";
    public string StudentId { get; set; } = "";
    public string FullName { get; set; } = "";
    public string Course { get; set; } = "";
    public string Section { get; set; } = "";
    public string Mode { get; set; } = "";
    public string Status { get; set; } = "";
}

public class DailyTrafficSummary
{
    public DateTime DateValue { get; set; }
    public string DisplayDate => DateValue.ToString("MMMM dd, yyyy");
    public int TotalScans { get; set; }
    public int Granted { get; set; }
    public int Denied { get; set; }
}

public sealed record EventReportSummary(IReadOnlyList<AttendanceLog> Arrivals, IReadOnlyDictionary<string, string> LastStates)
{
    public int LastPresent => Arrivals.Count(a => StateFor(a.StudentId) == ReportingAnalytics.LastPresent);
    public string StateFor(string id) => LastStates.TryGetValue(id, out string? state) ? state : ReportingAnalytics.Unresolved;
    public double? Turnout(IEnumerable<string> roster)
    {
        var ids = roster.Where(id => !string.IsNullOrWhiteSpace(id)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return ids.Count == 0 ? null : Arrivals.Count(a => ids.Contains(a.StudentId)) * 100.0 / ids.Count;
    }
}

public static class ReportingAnalytics
{
    public const string LastPresent = "Last scan: PRESENT";
    public const string LastDeparted = "Last scan: DEPARTED";
    public const string Unresolved = "Unresolved";

    public static EventReportSummary SummarizeEvent(IEnumerable<AttendanceLog> logs)
    {
        var groups = logs.Where(l => !string.IsNullOrWhiteSpace(l.StudentId)).GroupBy(l => l.StudentId, StringComparer.OrdinalIgnoreCase);
        var arrivals = new List<AttendanceLog>();
        var states = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in groups)
        {
            var firstArrival = group.Where(l => l.Status == "PRESENT").OrderBy(l => l.RawTimestamp).FirstOrDefault();
            if (firstArrival != null) arrivals.Add(firstArrival);
            DateTime latest = group.Max(r => r.RawTimestamp);
            var last = group.Where(l => l.RawTimestamp == latest).Select(l => l.Status).Distinct().ToList();
            states[group.Key] = firstArrival == null || group.Any(l => l.RawTimestamp == DateTime.MinValue) || last.Count != 1
                ? Unresolved : last[0] switch { "PRESENT" => LastPresent, "DEPARTED" => LastDeparted, _ => Unresolved };
        }
        return new(arrivals, states);
    }

    public static bool MatchesDate(DateTime timestamp, DateTime date) => timestamp != DateTime.MinValue && timestamp.Date == date.Date;
    public static bool MatchesTime(DateTime timestamp, string time) => time == "All Times" ||
        timestamp != DateTime.MinValue && (time == "Morning (AM)" ? timestamp.Hour < 12 : timestamp.Hour >= 12);
}
