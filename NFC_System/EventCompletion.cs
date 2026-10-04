using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NFC_System;

public sealed record EventCompletionPolicy(DateTime Start, DateTime End, int MinimumMinutes,
    int LateAllowanceMinutes, int EarlyAllowanceMinutes, long Revision = 0, DateTime CoverageSince = default);
public sealed record EventCompletionScan(string TransactionId, string StudentId, DateTime Time,
    string Status, string DeviceId, long Sequence, bool Recovered, bool Legacy);
public sealed record EventCompletionDecision(string Status, double Minutes, string Reason);
public sealed record EventCompletionReview(long Id, string StudentId, string Status, string Reason,
    string Actor, DateTime ReviewedAtUtc, string EvidenceStamp);
public sealed record EventCompletionRow(string StudentId, string Name, EventCompletionDecision Automatic,
    EventCompletionDecision Effective, string EvidenceStamp, EventCompletionReview? Review, bool CanReview)
{
    public string DisplayLabel => $"{Name} ({StudentId}) | {Effective.Status} | {Effective.Minutes:F1} min";
    public string ReviewLabel => Review == null ? "No manual decision" : $"Reviewed by {Review.Actor} at {Review.ReviewedAtUtc:u}: {Review.Status}. {Review.Reason}";
}
public sealed record EventCompletionReport(string EventId, EventCompletionPolicy? Policy, bool Synchronized,
    bool EventActive, DateTime DatabaseTime, IReadOnlyList<EventCompletionRow> Rows);

public static class EventCompletionRules
{
    public const string Completed = "Completed";
    public const string Incomplete = "Incomplete";
    public const string InProgress = "In Progress";
    public const string NeedsReview = "Needs Review";

    public static void Validate(EventCompletionPolicy policy)
    {
        double span = (policy.End - policy.Start).TotalMinutes;
        if (policy.Start == default || policy.End <= policy.Start || span > 7 * 24 * 60)
            throw new ArgumentException("Set a start and end time spanning no more than seven days.");
        if (policy.MinimumMinutes < 1 || policy.MinimumMinutes > span)
            throw new ArgumentException("Required attendance must be at least one minute and cannot exceed the event duration.");
        if (policy.LateAllowanceMinutes < 0 || policy.EarlyAllowanceMinutes < 0 ||
            policy.LateAllowanceMinutes >= span || policy.EarlyAllowanceMinutes >= span)
            throw new ArgumentException("Arrival/departure allowances must be nonnegative and shorter than the event.");
    }

    public static string ValidateReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 1000)
            throw new ArgumentException("Provide an audit reason of 1 to 1,000 characters.");
        return reason.Trim();
    }

    public static string Stamp(EventCompletionPolicy? policy, IEnumerable<EventCompletionScan> scans, bool active, bool untracked) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        { policy, scans = scans.OrderBy(s => s.TransactionId, StringComparer.Ordinal).ToArray(), active, untracked }))));

    public static EventCompletionDecision Evaluate(EventCompletionPolicy? policy, IEnumerable<EventCompletionScan> input,
        bool synchronized, bool active, DateTime now, bool untracked = false)
    {
        EventCompletionDecision Review(string reason, double minutes = 0) => new(NeedsReview, minutes, reason);
        if (policy == null) return Review("Attendance requirements have not been configured.");
        try { Validate(policy); } catch (ArgumentException ex) { return Review(ex.Message); }
        if (!synchronized) return Review("Device uploads/readiness are not confirmed. Result is provisional.");
        var scans = input.ToArray();
        if (untracked || policy.CoverageSince == default || policy.CoverageSince > policy.Start || scans.Any(s => s.Legacy))
            return Review("Legacy or untracked attendance cannot establish a complete duration history.");
        if (scans.Any(s => s.Time == default || s.Time > now || string.IsNullOrWhiteSpace(s.DeviceId) || s.Sequence < 1))
            return Review("Missing provenance or an invalid/future device timestamp.");
        foreach (var device in scans.GroupBy(s => s.DeviceId))
        {
            var ordered = device.OrderBy(s => s.Sequence).ToArray();
            if (ordered.Zip(ordered.Skip(1), (a, b) => b.Sequence <= a.Sequence || b.Time <= a.Time).Any(b => b))
                return Review("Device sequence and clock order are ambiguous.");
        }
        if (scans.GroupBy(s => s.Time).Any(g => g.Count() > 1)) return Review("Multiple scans share a timestamp; their order is uncertain.");
        if (scans.Any(s => s.Recovered) && scans.Select(s => s.DeviceId).Distinct().Count() > 1)
            return Review("Recovered scans span devices; clock ordering needs review.");

        DateTime? entry = null, firstEntry = null, lastExit = null;
        double minutes = 0;
        foreach (var scan in scans.OrderBy(s => s.Time))
        {
            if (scan.Status == "PRESENT")
            {
                if (entry.HasValue) return Review("A repeated check-in has no matching check-out.", minutes);
                entry = scan.Time;
                firstEntry ??= scan.Time;
            }
            else if (scan.Status == "DEPARTED")
            {
                if (!entry.HasValue) return Review("A check-out has no matching check-in.", minutes);
                DateTime from = entry.Value < policy.Start ? policy.Start : entry.Value;
                DateTime to = scan.Time > policy.End ? policy.End : scan.Time;
                if (to > from) minutes += (to - from).TotalMinutes;
                lastExit = scan.Time;
                entry = null;
            }
            else return Review("An unknown attendance action needs review.", minutes);
        }
        if (active || now < policy.End) return new(InProgress, minutes, "Final evaluation requires a closed event after its scheduled end.");
        if (entry.HasValue) return Review("The last check-in is missing a check-out.", minutes);
        if (!firstEntry.HasValue || !lastExit.HasValue) return Review("No paired attendance is available.", minutes);
        if (firstEntry > policy.Start.AddMinutes(policy.LateAllowanceMinutes)) return new(Incomplete, minutes, "Arrival exceeded the late-arrival allowance.");
        if (lastExit < policy.End.AddMinutes(-policy.EarlyAllowanceMinutes)) return new(Incomplete, minutes, "Departure preceded the allowed departure time.");
        return minutes >= policy.MinimumMinutes
            ? new(Completed, minutes, "Paired attendance meets duration, arrival and departure requirements.")
            : new(Incomplete, minutes, "Paired attendance is shorter than the required duration; time outside is excluded.");
    }

    public static EventCompletionDecision ApplyReview(EventCompletionDecision automatic, EventCompletionReview? review,
        string stamp, bool canReview)
    {
        if (review == null) return automatic;
        if (!canReview) return automatic;
        if (review.EvidenceStamp != stamp) return new(NeedsReview, automatic.Minutes, "Attendance or requirements changed after the review. Review again.");
        return new(review.Status, automatic.Minutes, $"Manual decision by {review.Actor}: {review.Reason}");
    }
}
