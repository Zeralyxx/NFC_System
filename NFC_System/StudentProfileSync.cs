using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NFC_System;

// This is deliberately not StudentRecord: operational state must never cross this channel.
public sealed record StudentSyncProfile(string StudentId, string FullName, string Email, string Course,
    string YearLevel, string SectionName, string Status, string NfcUid, string QrCredential,
    string PinSalt, string PinHash, byte[]? PhotoData, bool IsTemporary)
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);
    public string Fingerprint() => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ToJson())));
    public static StudentSyncProfile Parse(string json) =>
        JsonSerializer.Deserialize<StudentSyncProfile>(json, JsonOptions) ?? throw new InvalidDataException("Missing student profile.");

    public StudentRecord ToStudent() => new()
    {
        StudentId = StudentId, FullName = FullName, Email = Email, Course = Course, YearLevel = YearLevel,
        SectionName = SectionName, Status = Status, NfcUid = NfcUid, QrCredential = QrCredential,
        PinSalt = PinSalt, PinHash = PinHash, PhotoData = PhotoData, IsTemporary = IsTemporary
    };

    public void Validate()
    {
        if (StudentId == null || FullName == null || Email == null || Course == null || YearLevel == null ||
            SectionName == null || Status == null || NfcUid == null || QrCredential == null || PinSalt == null || PinHash == null)
            throw new InvalidDataException("Student fields cannot be null.");
        StudentProfileRules.ValidateEdit(StudentId, FullName, Status, NfcUid, false);
        GraduationRules.ValidateStatus(Status);
        GraduationRules.RequireActivationCredentials(Status, NfcUid.Length > 0, PinHash.Length > 0);
        if (Email.Length > 150 || Course.Length > 100 || YearLevel.Length > 20 || SectionName.Length > 50 ||
            NfcUid.Length > 50 || QrCredential.Length > 2048 || (PhotoData?.Length ?? 0) > 500_000 ||
            (YearLevel.Length > 0 && !int.TryParse(YearLevel, out _)))
            throw new InvalidDataException("A student field or photo exceeds the supported size.");
        if (PinSalt.Length > 0 || PinHash.Length > 0)
        {
            try
            {
                if (Convert.FromBase64String(PinSalt).Length != 16 || Convert.FromBase64String(PinHash).Length != 32)
                    throw new FormatException();
            }
            catch (FormatException) { throw new InvalidDataException("Invalid salted PIN hash. Reset the PIN before uploading."); }
        }
        if (Encoding.UTF8.GetByteCount(ToJson()) > 900_000) throw new InvalidDataException("Student profile is too large to upload.");
    }
}

public sealed record StudentSyncRequest(string Id, string AuthorUid, string ExpectedFingerprint,
    StudentSyncProfile Profile, bool ResetPin, string Status = "Pending", string Detail = "", string ResultFingerprint = "");

public sealed record StudentSyncResult(string Status, string Detail, string Fingerprint = "");

public sealed record StudentSyncBaseline(string MainFingerprint, string LocalFingerprint, string ProfileJson,
    string RequestId, string RequestJson);
