using NFC_System;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using ZXing;
using ZXing.Common;

internal static class QrDecodeDiagnostics
{
    // Synthetic credentials only. Never reads the machine's real signing/trust configuration.
    public static int Run()
    {
        AppSession.IsLoggedIn = true;
        AppSession.IsAdmin = true;
        AppSession.CurrentStaffRoleLabel = "Admin";
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var cert = new CertificateRequest("CN=QR image diagnostic", key, HashAlgorithmName.SHA256)
            .CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
        var readers = new Dictionary<string, BarcodeReaderGeneric>
        {
            ["test-default"] = new(),
            ["kiosk"] = new() { AutoRotate = false, Options = new DecodingOptions
                { PossibleFormats = new[] { BarcodeFormat.QR_CODE }, TryHarder = false, PureBarcode = false } },
            ["scanner-window"] = new() { AutoRotate = true, Options = new DecodingOptions
                { PossibleFormats = new[] { BarcodeFormat.QR_CODE }, TryHarder = true } },
            ["pure-diagnostic-only"] = new() { Options = new DecodingOptions
                { PossibleFormats = new[] { BarcodeFormat.QR_CODE }, PureBarcode = true } }
        };
        var rows = new List<DecodeRow>();
        foreach (int length in new[] { 8, 50 })
        for (int sample = 0; sample < 40; sample++)
        {
            string payload = QrCredentialService.Sign(new string('9', length), cert.Thumbprint, key);
            foreach (int size in new[] { 400, 800 })
            {
                var image = new BarcodeWriterPixelData { Format = BarcodeFormat.QR_CODE,
                    Options = new EncodingOptions { Width = size, Height = size, Margin = 4 } }.Write(payload);
                foreach (var pair in readers)
                {
                    string? decoded = pair.Value.Decode(image.Pixels, image.Width, image.Height, RGBLuminanceSource.BitmapFormat.BGRA32)?.Text;
                    rows.Add(new(length, sample, size, pair.Key, decoded == payload, decoded == null,
                        decoded == payload ? null : payload));
                }
            }
        }
        foreach (var group in rows.GroupBy(r => new { r.IdLength, r.Size, r.Reader }))
            Console.WriteLine($"QR image diagnostic: idLength={group.Key.IdLength}, size={group.Key.Size}, reader={group.Key.Reader}, passed={group.Count(r => r.ExactMatch)}/{group.Count()}");
        string path = Path.Combine(AppContext.BaseDirectory, "qr-decode-diagnostic.json");
        File.WriteAllText(path, JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("Synthetic failure corpus: " + path);
        return rows.Any(r => !r.ExactMatch && r.Reader != "pure-diagnostic-only") ? 1 : 0;
    }

    private sealed record DecodeRow(int IdLength, int Sample, int Size, string Reader, bool ExactMatch, bool NoResult, string? SyntheticPayload);
}
