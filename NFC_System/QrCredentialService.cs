using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

namespace NFC_System;

public sealed record QrCredentialPayload(string Issuer, int Version, string StudentId, string CredentialId, long IssuedAt);

public sealed class QrTrustConfiguration
{
    public string? ActiveSigningKeyId { get; set; }
    public Dictionary<string, string> PublicCertificates { get; set; } = new(StringComparer.Ordinal);
}

public sealed class QrCredentialService
{
    private const string Prefix = "NFC1";
    private const string Issuer = "NFC_SYSTEM";
    private readonly Func<QrTrustConfiguration> _loadTrust;
    public static string ConfigurationPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "NFC_System", "QrKeys", "trust.json");

    public QrCredentialService(Func<QrTrustConfiguration>? loadTrust = null)
    {
        _loadTrust = loadTrust ?? (() => JsonSerializer.Deserialize<QrTrustConfiguration>(File.ReadAllText(ConfigurationPath))
            ?? throw new InvalidOperationException("QR verification keys are not configured."));
    }

    public string Issue(string studentId)
    {
        AppSession.RequireQrIssuancePermission();
        try
        {
            var trust = _loadTrust();
            string keyId = trust.ActiveSigningKeyId ?? throw new InvalidOperationException("This account is not configured to issue QR credentials.");
            using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadOnly);
            var certificates = store.Certificates.Find(X509FindType.FindByThumbprint, keyId, false);
            try
            {
                var certificate = certificates.OfType<X509Certificate2>().FirstOrDefault(c => c.HasPrivateKey)
                    ?? throw new InvalidOperationException("The enrollment signing certificate is unavailable for this Windows account.");
                if (DateTime.Now < certificate.NotBefore || DateTime.Now > certificate.NotAfter)
                    throw new InvalidOperationException("The enrollment signing certificate has expired or is not yet valid.");
                using var key = certificate.GetECDsaPrivateKey()
                    ?? throw new InvalidOperationException("An ECDSA enrollment certificate is required.");
                string credential = Sign(studentId, keyId, key);
                if (!TryVerify(credential, out _, out _))
                    throw new InvalidOperationException("The enrollment signing key is not in the trusted public key list.");
                return credential;
            }
            finally { foreach (var certificate in certificates) certificate.Dispose(); }
        }
        catch (UnauthorizedAccessException) when (!AppSession.CanIssueQrCredentials)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or JsonException)
        {
            throw new InvalidOperationException("Cannot issue QR credentials. Check the enrollment certificate and QR key configuration.", ex);
        }
    }

    internal static string Sign(string studentId, string keyId, ECDsa privateKey)
    {
        AppSession.RequireQrIssuancePermission();
        if (string.IsNullOrWhiteSpace(studentId) || studentId.Length > 50 || studentId != studentId.Trim() || studentId.Any(char.IsControl))
            throw new ArgumentException("A valid student ID is required.", nameof(studentId));
        if (!ValidKeyId(keyId) || privateKey.KeySize != 256 || privateKey.ExportParameters(false).Curve.Oid.Value != "1.2.840.10045.3.1.7")
            throw new ArgumentException("A P-256 signing key and certificate thumbprint are required.");
        var payload = new QrCredentialPayload(Issuer, 1, studentId, Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        string message = $"{Prefix}.{keyId}.{Encode(JsonSerializer.SerializeToUtf8Bytes(payload))}";
        byte[] signature = privateKey.SignData(Encoding.UTF8.GetBytes(message), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return $"{message}.{Encode(signature)}";
    }

    public bool TryVerify(string? credential, out QrCredentialPayload? payload, out string error)
    {
        payload = null;
        error = "INVALID_QR";
        if (string.IsNullOrWhiteSpace(credential) || credential.Length > 2048) return false;
        var parts = credential.Split('.');
        if (parts.Length != 4 || parts[0] != Prefix || !ValidKeyId(parts[1])) return false;
        try
        {
            byte[] payloadBytes = Decode(parts[2]);
            byte[] signature = Decode(parts[3]);
            if (signature.Length != 64) return false;
            var trust = _loadTrust();
            if (trust.PublicCertificates == null || !trust.PublicCertificates.TryGetValue(parts[1], out string? encodedCertificate))
            {
                error = "QR_UNKNOWN_KEY";
                return false;
            }
            using var certificate = new X509Certificate2(Convert.FromBase64String(encodedCertificate));
            using var key = certificate.GetECDsaPublicKey();
            if (certificate.Thumbprint != parts[1] || key == null || key.KeySize != 256 ||
                key.ExportParameters(false).Curve.Oid.Value != "1.2.840.10045.3.1.7" ||
                !key.VerifyData(Encoding.UTF8.GetBytes(string.Join(".", parts.Take(3))), signature,
                    HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation)) return false;

            var candidate = JsonSerializer.Deserialize<QrCredentialPayload>(payloadBytes);
            if (candidate == null || candidate.Issuer != Issuer || candidate.Version != 1 ||
                string.IsNullOrWhiteSpace(candidate.StudentId) || candidate.StudentId.Length > 50 ||
                candidate.StudentId != candidate.StudentId.Trim() || candidate.StudentId.Any(char.IsControl) ||
                !Guid.TryParseExact(candidate.CredentialId, "N", out _) || candidate.IssuedAt <= 0) return false;
            payload = candidate;
            error = "";
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            error = "QR_KEYS_UNAVAILABLE";
            return false;
        }
        catch (Exception ex) when (ex is FormatException or JsonException or CryptographicException or ArgumentException)
        {
            return false;
        }
    }

    public static bool MatchesCurrent(string credential, QrCredentialPayload payload, StudentRecord student) =>
        string.Equals(payload.StudentId, student.StudentId, StringComparison.Ordinal) &&
        string.Equals(credential, student.QrCredential, StringComparison.Ordinal);

    private static bool ValidKeyId(string value) => value.Length == 40 && value.All(c => c is >= '0' and <= '9' or >= 'A' and <= 'F');
    private static string Encode(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static byte[] Decode(string value)
    {
        if (value.Length == 0 || value.Any(c => !(char.IsAsciiLetterOrDigit(c) || c == '-' || c == '_')))
            throw new FormatException("Invalid QR encoding.");
        string padded = value.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight((padded.Length + 3) / 4 * 4, '=');
        byte[] decoded = Convert.FromBase64String(padded);
        if (Encode(decoded) != value) throw new FormatException("Noncanonical QR encoding.");
        return decoded;
    }
}
