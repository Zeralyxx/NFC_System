using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NFC_System;

public sealed record RemoteMember(string Uid, string Email, string Name, string Role, bool Active)
{
    [JsonIgnore] public string DisplayLabel => $"{Name} | {(Active?"Approved":"Revoked")}\n{Email} | {Uid}";
    public void Require(string uid, string email, string role)
    {
        if (!Active || Uid != uid || !string.Equals(Email, email, StringComparison.OrdinalIgnoreCase) || Role != role ||
            string.IsNullOrWhiteSpace(Name) || Name.Length > 100)
            throw new UnauthorizedAccessException("This cloud account is not approved for this server and role.");
    }
}

public sealed record RemoteStudent
{
    public string StudentId { get; init; } = "";
    public string FullName { get; init; } = "";
    public string Email { get; init; } = "";
    public string Course { get; init; } = "";
    public string YearLevel { get; init; } = "";
    public string Section { get; init; } = "";
    public string Status { get; init; } = "Pending Enrollment";
    public string NfcUid { get; init; } = "";
    public bool IsTemporary { get; init; }
    public string PhotoBase64 { get; init; } = "";
    public bool PhotoOmitted { get; init; }
    public string QrCredential { get; init; } = "";
    public bool HasPin { get; init; }
    public string Clearance { get; init; } = "Not Reviewed";
    public long Revision { get; init; }
    public bool Deleted { get; init; }
    [JsonIgnore] public string DisplayLabel => $"{StudentId} | {FullName}\n{Course} / {YearLevel} / {Section} | {Status}";
}

public sealed record RemoteProfileChange(string StudentId, long ExpectedRevision, bool Create,
    string FullName, string Email, string Course, string YearLevel, string Section, string Status,
    string NfcUid, bool IsTemporary, string PhotoBase64, string? NewPin, bool ReissueQr, string Reason, bool ReplacePhoto = false)
{
    public void Validate()
    {
        string idError = StudentProfileRules.ValidateStudentId(StudentId);
        if (idError.Length > 0) throw new InvalidOperationException(idError);
        GraduationRules.ValidateStatus(Status);
        if (ExpectedRevision < 0 || (Create && ExpectedRevision != 0)) throw new InvalidOperationException("Invalid profile revision.");
        foreach (var (value, max, name) in new[] { (FullName,100,"Name"), (Email,150,"Email"),
            (Course,100,"Course"), (YearLevel,20,"Year"), (Section,50,"Section"), (NfcUid,50,"NFC UID"), (Reason,1000,"Reason") })
            if (value == null || value.Length > max || value.Any(char.IsControl))
                throw new InvalidOperationException($"{name} is invalid or exceeds {max} characters.");
        if (string.IsNullOrWhiteSpace(FullName) || string.IsNullOrWhiteSpace(Reason))
            throw new InvalidOperationException("Enter a name and a reason for this change.");
        if (NewPin != null && (NewPin.Length != 4 || NewPin.Any(c => c is < '0' or > '9')))
            throw new InvalidOperationException("PIN must contain exactly four digits.");
        if (YearLevel.Length>0 && (!int.TryParse(YearLevel,out int year) || year<1))
            throw new InvalidOperationException("Enter the year level as a positive whole number.");
        if (PhotoBase64 == null || PhotoBase64.Length > 350000)
            throw new InvalidOperationException("Use a student photo smaller than 250 KB.");
        try { if (Convert.FromBase64String(PhotoBase64).Length > 250000) throw new FormatException(); }
        catch (FormatException) { throw new InvalidOperationException("Use a valid student photo smaller than 250 KB."); }
    }
}

public sealed record RemoteServerKey(string KeyId, string PublicKey);
public sealed record RemoteEncryptedChange(string Id, string SiteId, string OwnerUid, string KeyId,
    string WrappedKey, string Nonce, string Ciphertext, string Tag, string CreatedAt)
{
    [JsonIgnore] public string Fingerprint => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(this)));
    [JsonIgnore] public string AssociatedData => $"NFC_REMOTE_V1|{SiteId}|{Id}|{OwnerUid}|{KeyId}|{CreatedAt}";
    public void Validate()
    {
        if (new[] {Id,SiteId,OwnerUid,KeyId,WrappedKey,Nonce,Ciphertext,Tag,CreatedAt}.Any(value=>value==null) ||
            !Guid.TryParseExact(Id,"N",out _) || !Guid.TryParseExact(SiteId,"N",out _) || OwnerUid.Length is < 1 or > 128 ||
            OwnerUid.Any(char.IsControl) || KeyId.Length != 64 || KeyId.Any(c => !Uri.IsHexDigit(c)) ||
            !DateTimeOffset.TryParse(CreatedAt, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind, out _) || Ciphertext.Length > 600000 ||
            Convert.FromBase64String(Nonce).Length != 12 || Convert.FromBase64String(Tag).Length != 16 ||
            Convert.FromBase64String(WrappedKey).Length != 384)
            throw new InvalidDataException("Invalid encrypted change request.");
    }
}

public sealed record RemoteChangeResult(string Id, string Status, string Message, long Revision = 0)
{
    public string DisplayLabel => $"{Id[..Math.Min(Id.Length,8)]} | {Status}\n{Message}";
}
public sealed record RemoteQueuedChange(RemoteEncryptedChange Change, string Status, string Message, string StudentId = "")
{
    public string DisplayLabel => $"{StudentId} | {Status}\n{Message}\n{Change.CreatedAt}";
}

public static class RemoteChangeCrypto
{
    public static RemoteServerKey PublicKey(RSA key)
    {
        byte[] bytes = key.ExportSubjectPublicKeyInfo();
        return new(Convert.ToHexString(SHA256.HashData(bytes)), Convert.ToBase64String(bytes));
    }

    public static RemoteEncryptedChange Encrypt(RemoteProfileChange change, string site, string owner, RemoteServerKey server)
    {
        change.Validate();
        using var rsa = RSA.Create();
        rsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(server.PublicKey), out _);
        if (rsa.KeySize != 3072 || PublicKey(rsa) != server) throw new InvalidDataException("Invalid server encryption key.");
        byte[] key = RandomNumberGenerator.GetBytes(32), nonce = RandomNumberGenerator.GetBytes(12);
        byte[] plain = JsonSerializer.SerializeToUtf8Bytes(change), encrypted = new byte[plain.Length], tag = new byte[16];
        var envelope = new RemoteEncryptedChange(Guid.NewGuid().ToString("N"), site, owner, server.KeyId,
            Convert.ToBase64String(rsa.Encrypt(key,RSAEncryptionPadding.OaepSHA256)), Convert.ToBase64String(nonce), "", "",
            DateTimeOffset.UtcNow.ToString("O"));
        try
        {
            using var aes = new AesGcm(key,16);
            aes.Encrypt(nonce,plain,encrypted,tag,Encoding.UTF8.GetBytes(envelope.AssociatedData));
            return envelope with { Ciphertext = Convert.ToBase64String(encrypted), Tag = Convert.ToBase64String(tag) };
        }
        finally { CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(plain); }
    }

    public static RemoteProfileChange Decrypt(RemoteEncryptedChange envelope, RSA key, string site)
    {
        envelope.Validate();
        if (envelope.SiteId != site || envelope.KeyId != PublicKey(key).KeyId)
            throw new InvalidDataException("This request targets a different server or encryption key.");
        byte[] secret = key.Decrypt(Convert.FromBase64String(envelope.WrappedKey),RSAEncryptionPadding.OaepSHA256);
        byte[] cipher = Convert.FromBase64String(envelope.Ciphertext), plain = new byte[cipher.Length];
        try
        {
            using var aes = new AesGcm(secret,16);
            aes.Decrypt(Convert.FromBase64String(envelope.Nonce),cipher,Convert.FromBase64String(envelope.Tag),plain,
                Encoding.UTF8.GetBytes(envelope.AssociatedData));
            var change = JsonSerializer.Deserialize<RemoteProfileChange>(plain) ?? throw new InvalidDataException("Empty change request.");
            change.Validate();
            return change;
        }
        finally { CryptographicOperations.ZeroMemory(secret); CryptographicOperations.ZeroMemory(plain); }
    }
}
