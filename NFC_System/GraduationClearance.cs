using System;
using System.Collections.Generic;
using System.Linq;

namespace NFC_System;

public enum GraduationClearanceStatus { NotReviewed, Cleared, OnHold }

public sealed record GraduationStudent
{
    public string StudentId { get; init; } = "";
    public string FullName { get; init; } = "";
    public string Course { get; init; } = "";
    public string YearLevel { get; init; } = "";
    public string Section { get; init; } = "";
    public string EnrollmentStatus { get; init; } = "";
    public GraduationClearanceStatus Clearance { get; init; }
    public string Reason { get; init; } = "";
    public string ChangedBy { get; init; } = "";
    public DateTime? ChangedAtUtc { get; init; }
    public long Revision { get; init; }
    public bool HasNfc { get; init; }
    public bool HasPin { get; init; }
    public string ClearanceLabel => GraduationRules.Label(Clearance);
    public string DisplayLabel => $"{StudentId}  |  {FullName}\n{Course} / {YearLevel} / {Section}  |  {EnrollmentStatus}  |  {ClearanceLabel}";
    public string ReviewLabel => ChangedAtUtc is DateTime date
        ? $"{ChangedBy} | {DateTime.SpecifyKind(date, DateTimeKind.Utc).ToLocalTime():g}"
        : "Not reviewed";
}

public sealed record StudentStatusPreviewRow(GraduationStudent Student, string Exclusion)
{
    public bool Eligible => Exclusion.Length == 0;
    public string DisplayLabel => $"{Student.StudentId} | {Student.FullName} | {Student.EnrollmentStatus}\n{(Eligible ? "Included" : Exclusion)}";
}

public sealed class StudentStatusPreview
{
    internal StudentStatusPreview(string status, IEnumerable<StudentStatusPreviewRow> rows, long sessionVersion, DateTime createdUtc)
    {
        NewStatus = status;
        Rows = Array.AsReadOnly(rows.ToArray());
        SessionVersion = sessionVersion;
        CreatedUtc = createdUtc;
    }

    public string NewStatus { get; }
    public IReadOnlyList<StudentStatusPreviewRow> Rows { get; }
    public long SessionVersion { get; }
    public DateTime CreatedUtc { get; }
    public int EligibleCount => Rows.Count(row => row.Eligible);
    public int ExcludedCount => Rows.Count - EligibleCount;
}

public static class GraduationRules
{
    public static string Label(GraduationClearanceStatus status) => status switch
    {
        GraduationClearanceStatus.NotReviewed => "Not Reviewed",
        GraduationClearanceStatus.Cleared => "Cleared",
        GraduationClearanceStatus.OnHold => "On Hold",
        _ => throw new ArgumentOutOfRangeException(nameof(status))
    };

    public static GraduationClearanceStatus Parse(string? status) => status switch
    {
        "Cleared" => GraduationClearanceStatus.Cleared,
        "On Hold" => GraduationClearanceStatus.OnHold,
        _ => GraduationClearanceStatus.NotReviewed
    };

    public static void ValidateStatus(string status)
    {
        if (status is not ("Active" or "Inactive" or "Graduated" or "Expelled" or "Pending Enrollment"))
            throw new ArgumentException("Choose a supported enrollment status.", nameof(status));
    }

    public static void RequireNewStudentStatus(string status, string nfcUid, bool hasPin)
    {
        ValidateStatus(status);
        if (status == "Graduated")
            throw new InvalidOperationException("Register the student first, review graduation clearance, then graduate them through a confirmed status update.");
        RequireActivationCredentials(status, !string.IsNullOrWhiteSpace(nfcUid), hasPin);
    }

    public static void RequireActivationCredentials(string status, bool hasNfc, bool hasPin)
    {
        if (status == "Active" && (!hasNfc || !hasPin))
            throw new InvalidOperationException("Activation requires both an enrolled NFC card and a configured PIN.");
    }

    public static string ValidateReason(GraduationClearanceStatus status, string reason)
    {
        _ = Label(status);
        string normalized = reason.Trim();
        if (normalized.Length == 0 || normalized.Length > 1000)
            throw new ArgumentException("Enter a clearance reason of 1 to 1,000 characters.", nameof(reason));
        return normalized;
    }

    public static long RequireAdministrator(bool online, long? expectedSession = null)
    {
        long version = AppSession.LoginVersion;
        if (!AppSession.IsLoggedIn || !AppSession.IsAdmin || AppSession.IsEventOrganizer ||
            AppSession.CurrentStaffRoleLabel is not ("Admin" or "Master Admin") ||
            (expectedSession.HasValue && expectedSession.Value != version))
            throw new UnauthorizedAccessException("Sign in as an Admin or Master Admin and start this action again.");
        if (!online) throw new InvalidOperationException("Student changes require an online database connection.");
        return version;
    }

    public static StudentStatusPreview Preview(IEnumerable<GraduationStudent> students, string newStatus,
        long sessionVersion, DateTime nowUtc)
    {
        ValidateStatus(newStatus);
        GraduationStudent[] targets = students.ToArray();
        if (targets.Length == 0) throw new InvalidOperationException("No students match this selection.");
        if (targets.Any(s => string.IsNullOrWhiteSpace(s.StudentId)) ||
            targets.Select(s => s.StudentId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != targets.Length)
            throw new InvalidOperationException("The selection contains missing or duplicate student IDs.");
        return new StudentStatusPreview(newStatus, targets.Select(s => new StudentStatusPreviewRow(s,
            s.EnrollmentStatus == newStatus ? "Already " + newStatus :
            newStatus == "Graduated" && s.Clearance != GraduationClearanceStatus.Cleared
                ? "Excluded: " + s.ClearanceLabel :
            newStatus == "Active" && (!s.HasNfc || !s.HasPin)
                ? "Excluded: NFC and PIN enrollment required" : "")), sessionVersion, nowUtc);
    }

    public static void ValidatePreviewAge(StudentStatusPreview preview, DateTime nowUtc)
    {
        if (nowUtc < preview.CreatedUtc || nowUtc - preview.CreatedUtc > TimeSpan.FromMinutes(15))
            throw new InvalidOperationException("This preview expired. Review the selection again.");
        if (preview.EligibleCount == 0) throw new InvalidOperationException("No eligible students are selected.");
    }

    public static void ValidateRecheck(GraduationStudent expected, GraduationStudent current, string newStatus)
    {
        if (expected.StudentId != current.StudentId || expected.FullName != current.FullName ||
            expected.EnrollmentStatus != current.EnrollmentStatus || expected.Course != current.Course ||
            expected.YearLevel != current.YearLevel || expected.Section != current.Section ||
            (newStatus == "Graduated" && (expected.Revision != current.Revision || expected.Clearance != current.Clearance)))
            throw new InvalidOperationException($"{expected.StudentId} changed after preview. No students were updated; review the selection again.");
        RequireGraduationClearance(current, newStatus);
    }

    public static void RequireGraduationClearance(GraduationStudent current, string newStatus)
    {
        ValidateStatus(newStatus);
        RequireActivationCredentials(newStatus, current.HasNfc, current.HasPin);
        if (newStatus == "Graduated" && current.EnrollmentStatus != "Graduated" &&
            current.Clearance != GraduationClearanceStatus.Cleared)
            throw new InvalidOperationException($"{current.StudentId} cannot graduate: clearance is {current.ClearanceLabel}.");
    }

    public static bool MatchesGroup(GraduationStudent student, string course, string year)
    {
        if (course != "All Courses" && student.Course != course) return false;
        return year == "All Years" || (year == "5+"
            ? int.TryParse(student.YearLevel, out int value) && value >= 5
            : student.YearLevel == year);
    }
}
