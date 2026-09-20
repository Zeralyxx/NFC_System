using NFC_System;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using ZXing;
using ZXing.Common;

AppSession.IsLoggedIn = true;
AppSession.IsAdmin = true;
AppSession.CurrentStaffRoleLabel = "Admin";

using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
using var certificate = new CertificateRequest("CN=QR test", key, HashAlgorithmName.SHA256)
    .CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
using var otherKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
using var otherCertificate = new CertificateRequest("CN=QR rotation test", otherKey, HashAlgorithmName.SHA256)
    .CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
var trust = new QrTrustConfiguration();
trust.PublicCertificates[certificate.Thumbprint] = Convert.ToBase64String(certificate.Export(X509ContentType.Cert));
var qr = new QrCredentialService(() => trust);
string Sign(string id) => QrCredentialService.Sign(id, certificate.Thumbprint, key);
string qrA = Sign("2026-001"), qrB = Sign("2026-002");
var pin = PinHasher.HashPin("1234");
int passed = 0, failed = 0;

void Assert(bool condition, string message = "Assertion failed") { if (!condition) throw new Exception(message); }
async Task Test(string name, Func<Task> action)
{
    try { await action(); passed++; Console.WriteLine($"PASS {name}"); }
    catch (Exception ex) { failed++; Console.WriteLine($"FAIL {name}: {ex.Message}"); }
}
Task Check(Action action) { action(); return Task.CompletedTask; }
(DatabaseService Db, VerificationEngine Engine, StudentRecord A) Fixture(bool offline = false)
{
    DatabaseMonitor.IsOnline = !offline;
    OfflineCacheService.Students.Clear();
    OfflineCacheService.Logs.Clear();
    OfflineCacheService.EventAllowed = true;
    OfflineCacheService.AttendanceStorageAvailable = true;
    var db = new DatabaseService();
    var a = new StudentRecord { StudentId = "2026-001", FullName = "Student A", NfcUid = "AA:01", QrCredential = qrA, PinSalt = pin.Salt, PinHash = pin.Hash };
    db.Students.Add(a);
    db.Students.Add(new StudentRecord { StudentId = "2026-002", FullName = "Student B", NfcUid = "BB:02", QrCredential = qrB, PinSalt = pin.Salt, PinHash = pin.Hash });
    if (offline) OfflineCacheService.Students.Add(JsonSerializer.Deserialize<StudentRecord>(JsonSerializer.Serialize(a))!);
    return (db, new VerificationEngine(db, qr), a);
}
async Task<VerificationSession> HighSession(VerificationEngine engine)
{
    var begin = await engine.BeginNfcVerificationAsync("AA:01", VerificationMode.HighSecurity, TransactionType.Entry);
    Assert(begin.Step == VerificationStep.RequiresPin);
    var pinResult = await engine.SubmitPinAsync(begin.Session!, "1234");
    Assert(pinResult.Step == VerificationStep.RequiresQr && !pinResult.IsGranted);
    return pinResult.Session!;
}
string Encode(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
string SignedCustom(QrCredentialPayload payload)
{
    string message = $"NFC1.{certificate.Thumbprint}.{Encode(JsonSerializer.SerializeToUtf8Bytes(payload))}";
    return message + "." + Encode(key.SignData(Encoding.UTF8.GetBytes(message), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
}

await Test("Signature validates using public certificate only", () => Check(() =>
{
    Assert(qr.TryVerify(qrA, out var payload, out _));
    Assert(payload!.StudentId == "2026-001" && payload.Version == 1);
    using var publicCertificate = new X509Certificate2(Convert.FromBase64String(trust.PublicCertificates[certificate.Thumbprint]));
    Assert(!publicCertificate.HasPrivateKey);
}));
foreach (string invalid in new[] { "", "2026-001", "NFC1", "NFC2" + qrA[4..], qrA[..^10], qrA + ".extra", new string('A', 2049), qrA.Replace('.', '|') })
    await Test("Reject malformed/legacy/version input " + invalid[..Math.Min(18, invalid.Length)], () => Check(() => Assert(!qr.TryVerify(invalid, out _, out _))));
await Test("Reject payload tampering", () => Check(() =>
{
    var parts = qrA.Split('.');
    parts[2] = Encode(Encoding.UTF8.GetBytes("{\"StudentId\":\"2026-002\"}"));
    Assert(!qr.TryVerify(string.Join('.', parts), out _, out _));
}));
await Test("Reject signature tampering and noncanonical base64", () => Check(() =>
{
    var parts = qrA.Split('.');
    parts[3] = (parts[3][0] == 'A' ? "B" : "A") + parts[3][1..];
    Assert(!qr.TryVerify(string.Join('.', parts), out _, out _));
    Assert(!qr.TryVerify(qrA + "=", out _, out _));
}));
await Test("Reject forged QR and unknown signing key", () => Check(() =>
{
    Assert(!qr.TryVerify(QrCredentialService.Sign("2026-001", certificate.Thumbprint, otherKey), out _, out _));
    Assert(!qr.TryVerify(QrCredentialService.Sign("2026-001", otherCertificate.Thumbprint, otherKey), out _, out var error));
    Assert(error == "QR_UNKNOWN_KEY");
}));
await Test("Reject signed wrong issuer/version/empty identity", () => Check(() =>
{
    var payload = new QrCredentialPayload("NFC_SYSTEM", 1, "2026-001", Guid.NewGuid().ToString("N"), 1);
    foreach (var invalid in new[] { payload with { Issuer = "OTHER" }, payload with { Version = 2 }, payload with { StudentId = "" }, payload with { CredentialId = "bad" } })
        Assert(!qr.TryVerify(SignedCustom(invalid), out _, out _));
}));
await Test("Missing or corrupt key configuration fails closed", () => Check(() =>
{
    Assert(!new QrCredentialService(() => throw new FileNotFoundException()).TryVerify(qrA, out _, out var error));
    Assert(error == "QR_KEYS_UNAVAILABLE");
    Assert(!new QrCredentialService(() => throw new JsonException()).TryVerify(qrA, out _, out _));
    bool threw = false;
    try { qr.Issue("2026-001"); } catch (InvalidOperationException) { threw = true; }
    Assert(threw, "Public-only configuration must not issue credentials");
}));
await Test("Key rotation overlap and removal", () => Check(() =>
{
    string rotated = QrCredentialService.Sign("2026-001", otherCertificate.Thumbprint, otherKey);
    trust.PublicCertificates[otherCertificate.Thumbprint] = Convert.ToBase64String(otherCertificate.Export(X509ContentType.Cert));
    Assert(qr.TryVerify(rotated, out _, out _) && qr.TryVerify(qrA, out _, out _));
    trust.PublicCertificates.Remove(otherCertificate.Thumbprint);
    Assert(!qr.TryVerify(rotated, out _, out _));
}));
await Test("Dense signed QR encodes/decodes exactly", () => Check(() =>
{
    string longest = Sign(new string('9', 50));
    var pixels = new BarcodeWriterPixelData { Format = BarcodeFormat.QR_CODE, Options = new EncodingOptions { Width = 800, Height = 800, Margin = 4 } }.Write(longest);
    var decoded = new BarcodeReaderGeneric().Decode(pixels.Pixels, pixels.Width, pixels.Height, RGBLuminanceSource.BitmapFormat.BGRA32);
    Assert(decoded?.Text == longest);
    Assert(longest.Length > 255, "Exercises widened DB column");
}));
foreach (bool offline in new[] { false, true })
{
    string label = offline ? "Offline" : "Online";
    await Test(label + " High Security requires PIN then current signed QR", async () =>
    {
        var f = Fixture(offline);
        var session = await HighSession(f.Engine);
        Assert((await f.Engine.SubmitQrAsync(session, qrA)).IsGranted);
        var logs = offline ? OfflineCacheService.Logs : f.Db.Logs;
        Assert(logs.Count(l => l.Granted && l.StudentId == f.A.StudentId) == 1);
        Assert(!(await f.Engine.SubmitQrAsync(session, qrA)).IsGranted, "Completed session cannot grant twice");
    });
    await Test(label + " wrong student's signed QR denied", async () =>
    {
        var f = Fixture(offline);
        Assert(!(await f.Engine.SubmitQrAsync(await HighSession(f.Engine), qrB)).IsGranted);
    });
    await Test(label + " unsigned QR cannot reach fallback PIN", async () =>
    {
        var f = Fixture(offline);
        var result = await f.Engine.BeginQrFallbackVerificationAsync("2026-001", VerificationMode.HighSecurity, TransactionType.Entry, null);
        Assert(result.Step == VerificationStep.Completed && !result.IsGranted && result.Session == null);
        Assert(f.Db.ReadCount == 0, "Signature validation precedes student lookup");
    });
    foreach (VerificationMode mode in Enum.GetValues<VerificationMode>())
        await Test($"{label} {mode} fallback requires PIN", async () =>
        {
            var f = Fixture(offline);
            var result = await f.Engine.BeginQrFallbackVerificationAsync(qrA, mode, TransactionType.Entry, null);
            Assert(result.Step == VerificationStep.RequiresPin && !result.IsGranted && result.Session!.IsQrFallback);
            Assert((await f.Engine.SubmitPinAsync(result.Session!, "1234")).IsGranted);
            Assert((offline ? OfflineCacheService.Logs : f.Db.Logs).Last().Remarks.Contains("fallback"));
        });
    await Test(label + " replaced QR denied, new QR accepted", async () =>
    {
        var f = Fixture(offline);
        string replacement = Sign(f.A.StudentId);
        f.A.QrCredential = replacement;
        if (offline) OfflineCacheService.Students[0].QrCredential = replacement;
        Assert((await f.Engine.BeginQrFallbackVerificationAsync(qrA, VerificationMode.Standard, TransactionType.Entry, null)).Step == VerificationStep.Completed);
        Assert((await f.Engine.BeginQrFallbackVerificationAsync(replacement, VerificationMode.Standard, TransactionType.Entry, null)).Step == VerificationStep.RequiresPin);
    });
    await Test(label + " three wrong fallback PINs lock the student", async () =>
    {
        var f = Fixture(offline);
        for (int attempt = 0; attempt < 3; attempt++)
        {
            var begin = await f.Engine.BeginQrFallbackVerificationAsync(qrA, VerificationMode.HighSecurity, TransactionType.Entry, null);
            Assert(begin.Step == VerificationStep.RequiresPin);
            Assert(!(await f.Engine.SubmitPinAsync(begin.Session!, "0000")).IsGranted);
            Assert(!(await f.Engine.SubmitPinAsync(begin.Session!, "1234")).IsGranted);
        }
        Assert((await f.Engine.BeginQrFallbackVerificationAsync(qrA, VerificationMode.HighSecurity, TransactionType.Entry, null)).ErrorCategory == "PIN_LOCKED");
    });
    await Test(label + " inactive student denied", async () =>
    {
        var f = Fixture(offline);
        f.A.Status = "Inactive";
        if (offline) OfflineCacheService.Students[0].Status = "Inactive";
        Assert((await f.Engine.BeginQrFallbackVerificationAsync(qrA, VerificationMode.Standard, TransactionType.Entry, null)).Step == VerificationStep.Completed);
    });
    await Test(label + " restricted event rejects fallback", async () =>
    {
        var f = Fixture(offline);
        f.Db.EventAllowed = false;
        OfflineCacheService.EventAllowed = false;
        Assert((await f.Engine.BeginQrFallbackVerificationAsync(qrA, VerificationMode.Standard, TransactionType.EventAttendance, "event")).ErrorCategory == "UNAUTHORIZED_EVENT_ACCESS");
    });
}
await Test("QR cannot bypass PIN in High Security", async () =>
{
    var f = Fixture();
    var begin = await f.Engine.BeginNfcVerificationAsync("AA:01", VerificationMode.HighSecurity, TransactionType.Entry);
    Assert(!(await f.Engine.SubmitQrAsync(begin.Session!, qrA)).IsGranted);
});
await Test("Reissue during active NFC/PIN session invalidates old QR", async () =>
{
    var f = Fixture();
    var session = await HighSession(f.Engine);
    f.A.QrCredential = Sign(f.A.StudentId);
    Assert(!(await f.Engine.SubmitQrAsync(session, qrA)).IsGranted);
});
await Test("Reissue during fallback PIN session invalidates prior scan", async () =>
{
    var f = Fixture();
    var begin = await f.Engine.BeginQrFallbackVerificationAsync(qrA, VerificationMode.Standard, TransactionType.Entry, null);
    f.A.QrCredential = Sign(f.A.StudentId);
    Assert(!(await f.Engine.SubmitPinAsync(begin.Session!, "1234")).IsGranted);
});
await Test("Online deleted student not resurrected from stale cache", async () =>
{
    var f = Fixture();
    OfflineCacheService.Students.Add(f.A);
    f.Db.Students.Clear();
    Assert((await f.Engine.BeginQrFallbackVerificationAsync(qrA, VerificationMode.Standard, TransactionType.Entry, null)).ErrorCategory == "NOT_REGISTERED");
});
await Test("Offline missing cache denies", async () =>
{
    var f = Fixture(true);
    OfflineCacheService.Students.Clear();
    Assert((await f.Engine.BeginQrFallbackVerificationAsync(qrA, VerificationMode.Standard, TransactionType.Entry, null)).ErrorCategory == "NOT_REGISTERED");
});
await Test("Database failure between monitor polls uses offline cache", async () =>
{
    var f = Fixture(true);
    DatabaseMonitor.IsOnline = true;
    f.Db.FailReads = true;
    var begin = await f.Engine.BeginQrFallbackVerificationAsync(qrA, VerificationMode.Standard, TransactionType.Entry, null);
    Assert(begin.Step == VerificationStep.RequiresPin && begin.Session!.IsOffline);
    Assert((await f.Engine.SubmitPinAsync(begin.Session!, "1234")).IsGranted);
});
await Test("Fast, Standard, Exit keep existing requirements", async () =>
{
    var f = Fixture();
    Assert((await f.Engine.BeginNfcVerificationAsync("AA:01", VerificationMode.Fast, TransactionType.Entry)).IsGranted);
    Assert((await f.Engine.BeginNfcVerificationAsync("AA:01", VerificationMode.HighSecurity, TransactionType.Exit)).IsGranted);
    var standard = await f.Engine.BeginNfcVerificationAsync("AA:01", VerificationMode.Standard, TransactionType.Entry);
    Assert(standard.Step == VerificationStep.RequiresPin);
    Assert((await f.Engine.SubmitPinAsync(standard.Session!, "1234")).IsGranted);
});
await Test("Copied exact signed QR still works with required credentials", async () =>
{
    var f = Fixture();
    string copy = new(qrA.ToCharArray());
    Assert((await f.Engine.SubmitQrAsync(await HighSession(f.Engine), copy)).IsGranted);
});
await Test("Concurrent QR submissions cannot grant twice", async () =>
{
    var f = Fixture();
    var session = await HighSession(f.Engine);
    f.Db.YieldReads = true;
    var results = await Task.WhenAll(f.Engine.SubmitQrAsync(session, qrA), f.Engine.SubmitQrAsync(session, qrA));
    Assert(results.Count(r => r.IsGranted) == 1 && f.Db.Logs.Count(l => l.Granted) == 1);
});
await Test("Concurrent fallback PIN submissions cannot grant twice", async () =>
{
    var f = Fixture();
    var begin = await f.Engine.BeginQrFallbackVerificationAsync(qrA, VerificationMode.Standard, TransactionType.Entry, null);
    f.Db.YieldReads = true;
    var results = await Task.WhenAll(f.Engine.SubmitPinAsync(begin.Session!, "1234"), f.Engine.SubmitPinAsync(begin.Session!, "1234"));
    Assert(results.Count(r => r.IsGranted) == 1 && f.Db.Logs.Count(l => l.Granted) == 1);
});
foreach (var identity in new[]
{
    (Name: "logged out", LoggedIn: false, Admin: false, Organizer: false, Role: ""),
    (Name: "logged out with stale admin flags", LoggedIn: false, Admin: true, Organizer: false, Role: "Admin"),
    (Name: "personnel", LoggedIn: true, Admin: false, Organizer: false, Role: "Personnel"),
    (Name: "organizer", LoggedIn: true, Admin: false, Organizer: true, Role: "Event Organizer"),
    (Name: "unknown role with admin flag", LoggedIn: true, Admin: true, Organizer: false, Role: "Unknown"),
    (Name: "role label without admin flag", LoggedIn: true, Admin: false, Organizer: false, Role: "Master Admin"),
    (Name: "conflicting role flags", LoggedIn: true, Admin: true, Organizer: true, Role: "Admin")
})
    await Test($"Issuance denies {identity.Name} before key access", () => Check(() =>
    {
        AppSession.IsLoggedIn = identity.LoggedIn;
        AppSession.IsAdmin = identity.Admin;
        AppSession.IsEventOrganizer = identity.Organizer;
        AppSession.CurrentStaffRoleLabel = identity.Role;
        bool accessedKeys = false, denied = false, signingDenied = false;
        var issuer = new QrCredentialService(() => { accessedKeys = true; return trust; });
        try { issuer.Issue("2026-001"); } catch (UnauthorizedAccessException ex) { denied = ex.Message == AppSession.QrIssuanceDeniedMessage; }
        try { QrCredentialService.Sign("2026-001", certificate.Thumbprint, key); } catch (UnauthorizedAccessException) { signingDenied = true; }
        Assert(denied && signingDenied && !accessedKeys && !AppSession.CanIssueQrCredentials);
    }));
foreach (string role in new[] { "Admin", "Master Admin" })
    await Test($"{role} can sign and proceeds to configured-key checks", () => Check(() =>
    {
        AppSession.IsLoggedIn = true;
        AppSession.IsAdmin = true;
        AppSession.IsEventOrganizer = false;
        AppSession.CurrentStaffRoleLabel = role;
        Assert(AppSession.CanIssueQrCredentials);
        Assert(qr.TryVerify(Sign("2026-001"), out _, out _));
        bool accessedKeys = false, missingKeyDenied = false;
        var issuer = new QrCredentialService(() => { accessedKeys = true; return new QrTrustConfiguration(); });
        try { issuer.Issue("2026-001"); } catch (InvalidOperationException) { missingKeyDenied = true; }
        Assert(accessedKeys && missingKeyDenied, "Authorization does not bypass signing-key requirements");
    }));
await Test("Cached issuer rechecks permissions after logout", () => Check(() =>
{
    var issuer = new QrCredentialService(() => trust);
    AppSession.IsLoggedIn = false;
    bool denied = false;
    try { issuer.Issue("2026-001"); } catch (UnauthorizedAccessException) { denied = true; }
    Assert(denied);
}));
await Test("Role revoked during key lookup cannot reach signing", () => Check(() =>
{
    AppSession.IsLoggedIn = true;
    var issuer = new QrCredentialService(() => { AppSession.IsLoggedIn = false; throw new UnauthorizedAccessException(AppSession.QrIssuanceDeniedMessage); });
    bool denied = false;
    try { issuer.Issue("2026-001"); } catch (UnauthorizedAccessException ex) { denied = ex.Message == AppSession.QrIssuanceDeniedMessage; }
    Assert(denied);
}));
foreach (bool offline in new[] { false, true })
    await Test($"Personnel can verify signed QR {(offline ? "offline" : "online")} without issuance permission", async () =>
    {
        AppSession.IsLoggedIn = true;
        AppSession.IsAdmin = false;
        AppSession.IsEventOrganizer = false;
        AppSession.CurrentStaffRoleLabel = "Personnel";
        var f = Fixture(offline);
        Assert(!AppSession.CanIssueQrCredentials);
        Assert((await f.Engine.SubmitQrAsync(await HighSession(f.Engine), qrA)).IsGranted);
    });
await Test("Unavailable durable storage denies access without updating attendance", async () =>
{
    var f = Fixture(); f.Db.FailAttendanceStorage = true;
    var outcome = await f.Engine.BeginNfcVerificationAsync("AA:01", VerificationMode.Fast, TransactionType.Entry);
    Assert(!outcome.IsGranted && outcome.ErrorCategory == "ATTENDANCE_STORAGE_UNAVAILABLE");
    Assert(f.A.EntryState == "OUTSIDE" && f.Db.Logs.Count == 0);
});
await Test("Concurrent database attendance conflict cannot become an offline grant", async () =>
{
    var f = Fixture(); f.Db.AttendanceConflict = true;
    var outcome = await f.Engine.BeginNfcVerificationAsync("AA:01", VerificationMode.Fast, TransactionType.Entry);
    Assert(!outcome.IsGranted && outcome.ErrorCategory == "ATTENDANCE_SEQUENCE_CONFLICT");
    Assert(OfflineCacheService.Logs.Count == 0);
});
await Test("Unconfirmed online decision cannot become an offline grant", async () =>
{
    var f = Fixture(); f.Db.AttendancePending = true;
    var outcome = await f.Engine.BeginNfcVerificationAsync("AA:01", VerificationMode.Fast, TransactionType.Entry);
    Assert(!outcome.IsGranted && outcome.ErrorCategory == "ATTENDANCE_CONFIRMATION_PENDING");
    Assert(f.A.EntryState == "OUTSIDE" && OfflineCacheService.Logs.Count == 0);
});
await Test("Stale online cache cannot replace another kiosk's current database state", async () =>
{
    var f = Fixture();
    var cached = JsonSerializer.Deserialize<StudentRecord>(JsonSerializer.Serialize(f.A))!;
    cached.EntryState = "INSIDE"; OfflineCacheService.Students.Add(cached);
    Assert((await f.Engine.BeginNfcVerificationAsync("AA:01", VerificationMode.Fast, TransactionType.Entry)).IsGranted);
});
await Test("Pending local entry still blocks a second entry during recovery", async () =>
{
    var f = Fixture();
    var cached = JsonSerializer.Deserialize<StudentRecord>(JsonSerializer.Serialize(f.A))!;
    cached.EntryState = "INSIDE"; OfflineCacheService.Students.Add(cached);
    OfflineCacheService.Logs.Add(new(f.A.StudentId, true, "OFFLINE", "pending entry"));
    var outcome = await f.Engine.BeginNfcVerificationAsync("AA:01", VerificationMode.Fast, TransactionType.Entry);
    Assert(!outcome.IsGranted && outcome.ErrorCategory == "ANTI_TAILGATING_VIOLATION");
});
await Test("Unreadable outbox denies NFC and QR fallback without throwing", async () =>
{
    var f = Fixture(); OfflineCacheService.AttendanceStorageAvailable = false;
    var nfc = await f.Engine.BeginNfcVerificationAsync("AA:01", VerificationMode.Fast, TransactionType.Entry);
    var qrResult = await f.Engine.BeginQrFallbackVerificationAsync(qrA, VerificationMode.Fast, TransactionType.Entry, null);
    Assert(!nfc.IsGranted && !qrResult.IsGranted && nfc.ErrorCategory == "ATTENDANCE_STORAGE_UNAVAILABLE");
    Assert(f.Db.ReadCount == 0);
});
Console.WriteLine($"\n{passed} passed; {failed} failed.");
return failed == 0 ? 0 : 1;
