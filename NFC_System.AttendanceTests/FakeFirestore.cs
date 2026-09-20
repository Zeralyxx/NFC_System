using System.Net;
using System.Text;
using System.Text.Json;

namespace NFC_System;

public sealed class FakeFirestore : HttpMessageHandler
{
    public Dictionary<string, JsonElement> Documents { get; } = new();
    public bool FailManifest { get; set; }
    public bool FailAll { get; set; }
    public bool LoseChunk { get; set; }
    public bool CorruptChunk { get; set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => HandleAsync(request);

    private async Task<HttpResponseMessage> HandleAsync(HttpRequestMessage request)
    {
        if (FailAll) return Reply(HttpStatusCode.Forbidden, new { error = "denied" });
        string path = request.RequestUri!.AbsolutePath.Split("/documents/")[1];
        if (request.Method == HttpMethod.Post)
        {
            if (FailManifest && path == "attendance_backups") return Reply(HttpStatusCode.ServiceUnavailable, new { error = "injected manifest failure" });
            string id = request.RequestUri.Query.TrimStart('?').Split('&').Select(x => x.Split('=')).Single(x => x[0] == "documentId")[1];
            string name = path + "/" + id;
            if (Documents.ContainsKey(name)) return Reply(HttpStatusCode.Conflict, new { error = "exists" });
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Documents[name] = json.RootElement.GetProperty("fields").Clone();
            return Reply(HttpStatusCode.OK, new { name = "projects/test/databases/(default)/documents/" + name, fields = Documents[name] });
        }
        if (path == "attendance_backups")
        {
            var manifests = Documents.Where(x => x.Key.Split('/').Length == 2)
                .OrderByDescending(x => x.Value.GetProperty("captured_at").GetProperty("timestampValue").GetString())
                .Take(1).Select(x => new { name = "projects/test/databases/(default)/documents/" + x.Key, fields = x.Value });
            return Reply(HttpStatusCode.OK, new { documents = manifests });
        }
        if (LoseChunk || !Documents.TryGetValue(path, out var fields)) return Reply(HttpStatusCode.NotFound, new { error = "missing" });
        if (CorruptChunk) return Reply(HttpStatusCode.OK, new { fields = new { data = new { stringValue = "AAAA" } } });
        return Reply(HttpStatusCode.OK, new { fields });
    }

    private static HttpResponseMessage Reply(HttpStatusCode status, object payload) => new(status)
    {
        Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
    };
}
