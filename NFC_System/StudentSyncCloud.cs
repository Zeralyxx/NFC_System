using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Windows.Security.Credentials;

namespace NFC_System;

public sealed record StudentSyncOptions(string ServerId, string Email)
{
    private static string FileName => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NFC_System", "student-sync.json");
    public static StudentSyncOptions? Load() => File.Exists(FileName)
        ? JsonSerializer.Deserialize<StudentSyncOptions>(File.ReadAllText(FileName)) : null;
    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FileName)!);
        File.WriteAllText(FileName + ".tmp", JsonSerializer.Serialize(this));
        File.Move(FileName + ".tmp", FileName, true);
    }
    public static void Delete() { if (File.Exists(FileName)) File.Delete(FileName); }
}

internal sealed class StudentSyncCloud(StudentSyncOptions options, string projectId, string apiKey)
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(25) };
    private string _token = "", _uid = "", _refresh = "";
    private DateTime _expires;
    private string Resource => "NFC_System.StudentSync." + options.ServerId;
    private string Root => $"https://firestore.googleapis.com/v1/projects/{projectId}/databases/(default)/documents";
    private string ServerPath => "studentSyncServers/" + options.ServerId;
    public string Uid => _uid;
    public string OwnerUid { get; private set; } = "";
    public HashSet<string> AdminUids { get; private set; } = new(StringComparer.Ordinal);

    internal async Task SignInAsync(string password, CancellationToken ct)
    {
        using var content = JsonContent(new { email = options.Email, password, returnSecureToken = true });
        using var response = await Http.PostAsync($"https://identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key={apiKey}", content, ct);
        if (!response.IsSuccessStatusCode) throw new UnauthorizedAccessException("Cloud sign-in failed. Check the email, password, and Firebase Email/Password provider.");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var data = document.RootElement;
        _token = data.GetProperty("idToken").GetString()!;
        _refresh = data.GetProperty("refreshToken").GetString()!;
        _uid = data.GetProperty("localId").GetString()!;
        _expires = DateTime.UtcNow.AddMinutes(50);
        await AuthorizeAsync(ct);
        var vault = new PasswordVault();
        try { vault.Remove(vault.Retrieve(Resource, options.Email)); } catch (Exception ex) when ((uint)ex.HResult == 0x80070490) { }
        vault.Add(new PasswordCredential(Resource, options.Email, _refresh));
    }

    internal void Forget()
    {
        var vault = new PasswordVault();
        try { vault.Remove(vault.Retrieve(Resource, options.Email)); } catch (Exception ex) when ((uint)ex.HResult == 0x80070490) { }
        _token = _refresh = _uid = "";
    }

    private async Task TokenAsync(CancellationToken ct)
    {
        if (_token.Length > 0 && DateTime.UtcNow < _expires) return;
        if (_refresh.Length == 0)
        {
            try
            {
                var credential = new PasswordVault().Retrieve(Resource, options.Email);
                credential.RetrievePassword();
                _refresh = credential.Password;
            }
            catch (Exception ex) { throw new UnauthorizedAccessException("Sign in to Student Profile Sync again in Settings.", ex); }
        }
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        { ["grant_type"] = "refresh_token", ["refresh_token"] = _refresh });
        using var response = await Http.PostAsync($"https://securetoken.googleapis.com/v1/token?key={apiKey}", content, ct);
        if (!response.IsSuccessStatusCode) throw new UnauthorizedAccessException("Cloud session expired or was revoked. Sign in again in Settings.");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        _token = document.RootElement.GetProperty("id_token").GetString()!;
        _refresh = document.RootElement.GetProperty("refresh_token").GetString()!;
        _uid = document.RootElement.GetProperty("user_id").GetString()!;
        _expires = DateTime.UtcNow.AddMinutes(50);
        var vault = new PasswordVault();
        try { vault.Remove(vault.Retrieve(Resource, options.Email)); } catch { }
        vault.Add(new PasswordCredential(Resource, options.Email, _refresh));
    }

    internal async Task AuthorizeAsync(CancellationToken ct)
    {
        using var document = await SendAsync(HttpMethod.Get, ServerPath, null, ct);
        var fields = document!.RootElement.GetProperty("fields");
        OwnerUid = Text(fields, "ownerUid");
        AdminUids = fields.TryGetProperty("adminUids", out var array) && array.TryGetProperty("arrayValue", out var values) &&
            values.TryGetProperty("values", out var items)
            ? items.EnumerateArray().Select(x => x.GetProperty("stringValue").GetString()!).ToHashSet(StringComparer.Ordinal) : new();
        bool owner = DatabaseService.ActiveConnection.Role == ServerConnectionRole.MainServer;
        if (owner ? OwnerUid != _uid || DatabaseService.ActiveConnection.ServerId != options.ServerId
            : DatabaseService.ActiveConnection.Role != ServerConnectionRole.Administration || !AdminUids.Contains(_uid))
            throw new UnauthorizedAccessException("This cloud account is not approved for this device role and main server.");
    }

    internal async Task<IReadOnlyList<(StudentSyncProfile Profile, string Fingerprint)>> ProfilesAsync(CancellationToken ct)
    {
        var result = new List<(StudentSyncProfile, string)>();
        string page = "";
        var seen = new HashSet<string>();
        do
        {
            string listPath = ServerPath + "/profiles?pageSize=100" + (page.Length == 0 ? "" : "&pageToken=" + Uri.EscapeDataString(page));
            using var document = await SendAsync(HttpMethod.Get, listPath, null, ct);
            var root = document!.RootElement;
            if (root.TryGetProperty("documents", out var docs))
                foreach (var doc in docs.EnumerateArray())
                {
                    var fields = doc.GetProperty("fields");
                    var profile = StudentSyncProfile.Parse(Text(fields, "profileJson"));
                    string fingerprint = Text(fields, "fingerprint");
                    if (profile.Fingerprint() != fingerprint) throw new InvalidDataException("Cloud profile fingerprint does not match.");
                    result.Add((profile, fingerprint));
                }
            page = root.TryGetProperty("nextPageToken", out var token) ? token.GetString() ?? "" : "";
            if (page.Length > 0 && !seen.Add(page)) throw new InvalidDataException("Repeated cloud page token.");
        } while (page.Length > 0);
        return result;
    }

    internal async Task PublishAsync(StudentSyncProfile profile, CancellationToken ct)
    {
        string id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(profile.StudentId)));
        using var response = await SendAsync(HttpMethod.Patch, ServerPath + "/profiles/" + id,
            new { fields = new { profileJson = Str(profile.ToJson()), fingerprint = Str(profile.Fingerprint()) } }, ct);
    }

    internal async Task DeleteProfileAsync(string studentId, CancellationToken ct)
    {
        string id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(studentId)));
        using var response = await SendAsync(HttpMethod.Delete, ServerPath + "/profiles/" + id, null, ct, true);
    }

    internal async Task SubmitAsync(StudentSyncRequest request, CancellationToken ct)
    {
        using var existing = await SendAsync(HttpMethod.Get, ServerPath + "/requests/" + request.Id, null, ct, true);
        if (existing != null)
        {
            var saved = ParseRequest(existing.RootElement);
            if (saved.AuthorUid != _uid || saved.Profile.ToJson() != request.Profile.ToJson() ||
                saved.ExpectedFingerprint != request.ExpectedFingerprint || saved.ResetPin != request.ResetPin)
                throw new InvalidDataException("Upload ID already belongs to a different change.");
            return;
        }
        using var response = await SendAsync(HttpMethod.Patch, ServerPath + "/requests/" + request.Id + "?currentDocument.exists=false",
            new { fields = new {
                authorUid = Str(_uid), expectedFingerprint = Str(request.ExpectedFingerprint), profileJson = Str(request.Profile.ToJson()),
                resetPin = new { booleanValue = request.ResetPin }, status = Str("Pending"), detail = Str(""), resultFingerprint = Str("")
            } }, ct);
    }

    internal async Task<StudentSyncRequest?> RequestAsync(string id, CancellationToken ct)
    {
        using var document = await SendAsync(HttpMethod.Get, ServerPath + "/requests/" + id, null, ct, true);
        return document == null ? null : ParseRequest(document.RootElement);
    }

    internal async Task<IReadOnlyList<StudentSyncRequest>> PendingAsync(CancellationToken ct)
    {
        using var document = await SendAsync(HttpMethod.Post, ServerPath + ":runQuery", new { structuredQuery = new {
            from = new[] { new { collectionId = "requests" } }, limit = 100,
            where = new { fieldFilter = new { field = new { fieldPath = "status" }, op = "EQUAL", value = Str("Pending") } }
        } }, ct);
        var result = new List<StudentSyncRequest>();
        foreach (var item in document!.RootElement.EnumerateArray())
        {
            if (!item.TryGetProperty("document", out var doc)) continue;
            try { result.Add(ParseRequest(doc)); }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or InvalidDataException)
            {
                string id = doc.GetProperty("name").GetString()!.Split('/').Last();
                await AcknowledgeAsync(id, new("Rejected", "Malformed student change. Upload it again from the current app."), ct);
            }
        }
        return result;
    }

    internal async Task AcknowledgeAsync(string id, StudentSyncResult result, CancellationToken ct)
    {
        using var response = await SendAsync(HttpMethod.Patch, ServerPath + "/requests/" + id +
            "?updateMask.fieldPaths=status&updateMask.fieldPaths=detail&updateMask.fieldPaths=resultFingerprint&currentDocument.exists=true",
            new { fields = new { status = Str(result.Status), detail = Str(result.Detail), resultFingerprint = Str(result.Fingerprint) } }, ct);
    }

    private static StudentSyncRequest ParseRequest(JsonElement doc)
    {
        var f = doc.GetProperty("fields");
        string id = doc.GetProperty("name").GetString()!.Split('/').Last();
        return new(id, Text(f, "authorUid"), Text(f, "expectedFingerprint"), StudentSyncProfile.Parse(Text(f, "profileJson")),
            f.GetProperty("resetPin").GetProperty("booleanValue").GetBoolean(), Text(f, "status"), Text(f, "detail"), Text(f, "resultFingerprint"));
    }
    private static string Text(JsonElement fields, string key) => fields.GetProperty(key).GetProperty("stringValue").GetString() ?? "";
    private static object Str(string value) => new { stringValue = value };
    private static StringContent JsonContent(object value) => new(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");

    private async Task<JsonDocument?> SendAsync(HttpMethod method, string path, object? payload, CancellationToken ct, bool allowMissing = false)
    {
        await TokenAsync(ct);
        using var request = new HttpRequestMessage(method, Root + "/" + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        if (payload != null) request.Content = JsonContent(payload);
        using var response = await Http.SendAsync(request, ct);
        if (allowMissing && response.StatusCode == HttpStatusCode.NotFound) return null;
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Student sync cloud request failed ({(int)response.StatusCode}). Check account approval and Student Sync security rules.", null, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
    }
}
