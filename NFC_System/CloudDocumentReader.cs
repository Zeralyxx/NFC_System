using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace NFC_System;

internal static class CloudDocumentReader
{
    // Finish every page before allowing the caller to apply this collection locally.
    internal static async Task<JsonDocument> ReadCollectionAsync(HttpClient http, string collectionUrl)
    {
        var documents = new List<JsonElement>();
        var tokens = new HashSet<string>(StringComparer.Ordinal);
        string nextUrl = collectionUrl;
        while (true)
        {
            using var response = await http.GetAsync(nextUrl);
            response.EnsureSuccessStatusCode();
            using var page = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (page.RootElement.TryGetProperty("documents", out var rows))
            {
                if (rows.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Invalid cloud collection response.");
                foreach (var row in rows.EnumerateArray()) documents.Add(row.Clone());
            }
            if (!page.RootElement.TryGetProperty("nextPageToken", out var tokenElement)) break;
            string? token = tokenElement.GetString();
            if (string.IsNullOrEmpty(token)) break;
            if (!tokens.Add(token)) throw new InvalidDataException("Cloud download repeated a page token; no collection changes were applied.");
            nextUrl = collectionUrl + (collectionUrl.Contains('?') ? "&" : "?") + "pageToken=" + Uri.EscapeDataString(token);
        }
        return JsonSerializer.SerializeToDocument(new { documents });
    }
}
