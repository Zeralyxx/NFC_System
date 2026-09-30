using System;
using System.Linq;

namespace NFC_System;

public static class StudentProfileRules
{
    public static string ValidateStudentId(string id) => string.IsNullOrWhiteSpace(id) || id.Length > 50 ||
        id.Any(char.IsWhiteSpace) || id.Any(char.IsControl) || "=+-@".Contains(id[0])
        ? "Student ID is required, at most 50 characters, without whitespace or a leading =, +, -, or @." : "";

    public static void ValidateEdit(string id, string name, string status, string nfcUid, bool previouslyEnrolled)
    {
        string error = ValidateStudentId(id);
        if (error.Length != 0) throw new InvalidOperationException(error);
        if (string.IsNullOrWhiteSpace(name) || name.Length > 100)
            throw new InvalidOperationException("Full Name is required and must be at most 100 characters.");
        if (string.IsNullOrWhiteSpace(nfcUid) && (previouslyEnrolled || status != "Pending Enrollment"))
            throw new InvalidOperationException("Assign an NFC card before leaving Pending Enrollment. Existing cards cannot be cleared here.");
    }

    public static bool ShouldOfferQrReplacement(string nfcUid, string qr) =>
        (!string.IsNullOrWhiteSpace(nfcUid) || !string.IsNullOrWhiteSpace(qr)) &&
        !qr.StartsWith("NFC1.", StringComparison.Ordinal);

    public static bool ShouldIssueQr(string oldId, string oldNfc, string oldQr, string newId, string newNfc, bool requested) =>
        requested || (!string.IsNullOrWhiteSpace(newNfc) &&
            (oldId != newId || oldNfc != newNfc || string.IsNullOrWhiteSpace(oldQr))) ||
        (!string.IsNullOrWhiteSpace(oldQr) && oldId != newId);
}
