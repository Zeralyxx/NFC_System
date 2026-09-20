using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace NFC_System;

public sealed class AttendanceBackupSnapshot
{
    public int Version { get; set; } = 1;
    public DateTime CapturedAtUtc { get; set; }
    public Dictionary<string, List<Dictionary<string, JsonElement>>> Tables { get; set; } = new();
    public Dictionary<string, long> NextLogIds { get; set; } = new();

    public static readonly string[] TableNames =
    {
        "attendance_devices", "attendance_receipts", "attendance_decisions", "attendance_current", "attendance_visits",
        "fast_mode_logs", "standard_mode_logs", "high_security_mode_logs", "event_attendance"
    };

    public void Validate()
    {
        if (Version != 1 || CapturedAtUtc == default || Tables == null || NextLogIds == null ||
            Tables.Count != TableNames.Length || TableNames.Any(t => !Tables.ContainsKey(t) || Tables[t] == null))
            throw new InvalidDataException("The attendance backup is incomplete or uses an unsupported format.");
        foreach (string table in TableNames.Skip(5))
        {
            if (!NextLogIds.TryGetValue(table, out long next) || next < 1 || next > int.MaxValue ||
                Tables[table].Any(row => !row.TryGetValue("id", out var id) || id.GetInt64() >= next))
                throw new InvalidDataException("The attendance backup is missing valid log identity counters.");
        }
    }
}

// Immutable chunks are published first. Only a completed manifest makes a generation restorable.
public sealed class AttendanceBackupStore
{
    private readonly HttpClient _http;
    private readonly string _documents;
    private readonly string _key;
    private const int ChunkSize = 200000;
    private const int MaximumChunks = 1024;

    public AttendanceBackupStore(HttpClient http, string documentsUrl, string apiKey)
    {
        _http = http;
        _documents = documentsUrl.TrimEnd('/');
        _key = Uri.EscapeDataString(apiKey);
    }

    public async Task<string> UploadAsync(AttendanceBackupSnapshot snapshot)
    {
        snapshot.Validate();
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(snapshot);
        if (json.LongLength > 512L * 1024 * 1024) throw new InvalidOperationException("Attendance backup exceeds the supported snapshot size.");
        using var compressed = new MemoryStream();
        using (var gzip = new GZipStream(compressed, CompressionLevel.Optimal, true)) gzip.Write(json);
        byte[] bytes = compressed.ToArray();
        string encoded = Convert.ToBase64String(bytes);
        int count = (encoded.Length + ChunkSize - 1) / ChunkSize;
        if (count > MaximumChunks) throw new InvalidOperationException("Attendance backup exceeds the supported snapshot size.");
        string generation = Guid.NewGuid().ToString("N");
        for (int i = 0; i < count; i++)
        {
            string chunk = encoded.Substring(i * ChunkSize, Math.Min(ChunkSize, encoded.Length - i * ChunkSize));
            await CreateAsync($"attendance_backups/{generation}/chunks", i.ToString("D6", CultureInfo.InvariantCulture),
                new { fields = new { data = new { stringValue = chunk } } });
        }
        await CreateAsync("attendance_backups", generation, new
        {
            fields = new
            {
                version = new { integerValue = "1" },
                captured_at = new { timestampValue = snapshot.CapturedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) },
                chunk_count = new { integerValue = count.ToString(CultureInfo.InvariantCulture) },
                sha256 = new { stringValue = Convert.ToHexString(SHA256.HashData(bytes)) },
                complete = new { booleanValue = true }
            }
        });
        return generation;
    }

    public async Task<AttendanceBackupSnapshot?> DownloadLatestAsync()
    {
        using var response = await _http.GetAsync($"{_documents}/attendance_backups?pageSize=1&orderBy=captured_at%20desc&key={_key}");
        response.EnsureSuccessStatusCode();
        using var list = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        if (!list.RootElement.TryGetProperty("documents", out var documents) || documents.GetArrayLength() == 0) return null;
        var manifest = documents[0];
        var fields = manifest.GetProperty("fields");
        int count = int.Parse(fields.GetProperty("chunk_count").GetProperty("integerValue").GetString()!, CultureInfo.InvariantCulture);
        if (!fields.GetProperty("complete").GetProperty("booleanValue").GetBoolean() ||
            fields.GetProperty("version").GetProperty("integerValue").GetString() != "1" || count <= 0 || count > MaximumChunks)
            throw new InvalidDataException("Attendance backup manifest is incomplete or invalid.");
        string generation = manifest.GetProperty("name").GetString()!.Split('/').Last();
        if (!Guid.TryParseExact(generation, "N", out _)) throw new InvalidDataException("Invalid backup generation identifier.");
        var encoded = new StringBuilder();
        for (int i = 0; i < count; i++)
        {
            using var part = await _http.GetAsync($"{_documents}/attendance_backups/{generation}/chunks/{i:D6}?key={_key}");
            part.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await part.Content.ReadAsStringAsync());
            string data = document.RootElement.GetProperty("fields").GetProperty("data").GetProperty("stringValue").GetString()!;
            if (data.Length > ChunkSize) throw new InvalidDataException("Attendance backup chunk is oversized.");
            encoded.Append(data);
        }
        byte[] bytes = Convert.FromBase64String(encoded.ToString());
        string expected = fields.GetProperty("sha256").GetProperty("stringValue").GetString()!;
        if (!Convert.ToHexString(SHA256.HashData(bytes)).Equals(expected, StringComparison.Ordinal))
            throw new InvalidDataException("Attendance backup checksum did not match. Nothing was restored.");
        using var input = new MemoryStream(bytes);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        byte[] buffer = new byte[81920];
        int read;
        while ((read = gzip.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (output.Length + read > 512L * 1024 * 1024) throw new InvalidDataException("Attendance backup expands beyond the supported size.");
            output.Write(buffer, 0, read);
        }
        var snapshot = JsonSerializer.Deserialize<AttendanceBackupSnapshot>(output.ToArray())
            ?? throw new InvalidDataException("Attendance snapshot is empty.");
        snapshot.Validate();
        return snapshot;
    }

    private async Task CreateAsync(string collection, string id, object payload)
    {
        using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var response = await _http.PostAsync($"{_documents}/{collection}?documentId={id}&key={_key}", content);
        response.EnsureSuccessStatusCode();
    }
}
