namespace NFC_System;

// Database tests inject QR decisions; real signature verification is covered by NFC_System.Tests.
public sealed record SyncQrPayload(string StudentId);
public sealed class QrCredentialService
{
    public bool TryVerify(string? credential, out SyncQrPayload? payload, out string error)
    { payload = null; error = "INVALID_QR"; return false; }
}
