using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace NFC_System;

internal sealed record RosterCloudRevision(string EventId, string StudentId, string ChangeId, bool Approved, string UpdateTime);
internal sealed record RosterSyncState(string EventId, string StudentId, string ChangeId, bool Approved,
    string CloudChangeId, string CloudUpdateTime, bool Pending, bool Conflict);

internal static class EventRosterSyncRules
{
    internal static string DocumentId(string eventId, string studentId) => Convert.ToHexString(SHA256.HashData(
        JsonSerializer.SerializeToUtf8Bytes(new[] { eventId.ToUpperInvariant(), studentId.ToUpperInvariant() })));

    internal static DateTimeOffset ServerTime(string value) => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
        DateTimeStyles.AssumeUniversal, out var time) ? time : throw new InvalidDataException("Invalid roster cloud revision time.");

    internal static RosterSyncState Merge(RosterSyncState? local, RosterCloudRevision remote)
    {
        if (local == null) return new(remote.EventId, remote.StudentId, remote.ChangeId, remote.Approved,
            remote.ChangeId, remote.UpdateTime, false, false);
        if (local.CloudUpdateTime.Length > 0 && ServerTime(remote.UpdateTime) < ServerTime(local.CloudUpdateTime)) return local;
        if (remote.ChangeId == local.ChangeId)
        {
            if (remote.Approved != local.Approved) throw new InvalidDataException("A roster revision has conflicting contents.");
            return local with { CloudChangeId = remote.ChangeId, CloudUpdateTime = remote.UpdateTime, Pending = false };
        }
        if (local.Conflict) return local with { CloudChangeId = remote.ChangeId, CloudUpdateTime = remote.UpdateTime };
        if (local.Pending)
        {
            if (remote.ChangeId == local.CloudChangeId) return local;
            // A concurrent cloud edit must never silently turn a local revocation back into an approval.
            return local with { CloudChangeId = remote.ChangeId, CloudUpdateTime = remote.UpdateTime, Conflict = true };
        }
        return new(local.EventId, local.StudentId, remote.ChangeId, remote.Approved, remote.ChangeId, remote.UpdateTime, false, false);
    }

    internal static RosterSyncState Acknowledge(RosterSyncState current, RosterSyncState sent, RosterCloudRevision committed)
    {
        if (current.ChangeId == sent.ChangeId) return Merge(current, committed);
        // An edit made while HTTP was in flight remains pending, based on the write that just committed.
        if (!current.Conflict && current.Pending && current.CloudChangeId == sent.CloudChangeId)
            return current with { CloudChangeId = committed.ChangeId, CloudUpdateTime = committed.UpdateTime };
        return Merge(current, committed);
    }
}

internal interface IEventRosterCloudStore
{
    Task<IReadOnlyList<RosterCloudRevision>> LoadAsync();
    Task<IReadOnlyList<(string EventId, string StudentId)>> LoadLegacyAsync();
    Task<RosterCloudRevision?> ReadAsync(string eventId, string studentId);
    Task<RosterCloudRevision?> TryWriteAsync(RosterSyncState state, string? expectedUpdateTime);
}

internal sealed class FirestoreEventRosterStore(HttpClient http, string documentsUrl, string apiKey) : IEventRosterCloudStore
{
    private string CollectionUrl(string collection) => $"{documentsUrl}/{collection}?key={Uri.EscapeDataString(apiKey)}";
    private string DocumentUrl(string eventId, string studentId) =>
        $"{documentsUrl}/event_roster_state/{EventRosterSyncRules.DocumentId(eventId, studentId)}?key={Uri.EscapeDataString(apiKey)}";

    internal static RosterCloudRevision Parse(JsonElement document)
    {
        var fields = document.GetProperty("fields");
        string Text(string key) => fields.GetProperty(key).GetProperty("stringValue").GetString() ?? "";
        string eventId = Text("event_id"), studentId = Text("student_id"), change = Text("change_id");
        if (string.IsNullOrWhiteSpace(eventId) || eventId.Length > 50 || string.IsNullOrWhiteSpace(studentId) || studentId.Length > 50 ||
            !Guid.TryParseExact(change, "N", out _) || fields.GetProperty("schema_version").GetProperty("integerValue").GetString() != "1")
            throw new InvalidDataException("Invalid versioned roster record. No legacy roster fallback is allowed.");
        string time = document.GetProperty("updateTime").GetString() ?? "";
        _ = EventRosterSyncRules.ServerTime(time);
        if (!(document.GetProperty("name").GetString() ?? "").EndsWith("/event_roster_state/" + EventRosterSyncRules.DocumentId(eventId, studentId), StringComparison.Ordinal))
            throw new InvalidDataException("Roster document identity does not match its contents.");
        return new(eventId, studentId, change, fields.GetProperty("approved").GetProperty("booleanValue").GetBoolean(), time);
    }

    public async Task<IReadOnlyList<RosterCloudRevision>> LoadAsync()
    {
        using var document = await CloudDocumentReader.ReadCollectionAsync(http, CollectionUrl("event_roster_state") + "&pageSize=1000");
        var rows = document.RootElement.GetProperty("documents").EnumerateArray().Select(Parse).ToArray();
        if (rows.Select(r => EventRosterSyncRules.DocumentId(r.EventId, r.StudentId)).Distinct().Count() != rows.Length)
            throw new InvalidDataException("Duplicate roster cloud identities.");
        return rows;
    }

    public async Task<IReadOnlyList<(string EventId, string StudentId)>> LoadLegacyAsync()
    {
        using var document = await CloudDocumentReader.ReadCollectionAsync(http, CollectionUrl("event_approved_students") + "&pageSize=1000");
        var rows = new List<(string, string)>();
        foreach (var row in document.RootElement.GetProperty("documents").EnumerateArray())
        {
            var fields = row.GetProperty("fields");
            string eventId = fields.GetProperty("event_id").GetProperty("stringValue").GetString() ?? "";
            string studentId = fields.GetProperty("student_id").GetProperty("stringValue").GetString() ?? "";
            if (eventId.Length is > 0 and <= 50 && studentId.Length is > 0 and <= 50) rows.Add((eventId, studentId));
        }
        return rows;
    }

    public async Task<RosterCloudRevision?> ReadAsync(string eventId, string studentId)
    {
        using var response = await http.GetAsync(DocumentUrl(eventId, studentId));
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var row = Parse(document.RootElement);
        if (EventRosterSyncRules.DocumentId(row.EventId, row.StudentId) != EventRosterSyncRules.DocumentId(eventId, studentId))
            throw new InvalidDataException("Unexpected roster identity in cloud response.");
        return row;
    }

    public async Task<RosterCloudRevision?> TryWriteAsync(RosterSyncState state, string? expectedUpdateTime)
    {
        var fields = new Dictionary<string, object>
        {
            ["event_id"] = new { stringValue = state.EventId }, ["student_id"] = new { stringValue = state.StudentId },
            ["change_id"] = new { stringValue = state.ChangeId }, ["approved"] = new { booleanValue = state.Approved },
            ["schema_version"] = new { integerValue = "1" }
        };
        string precondition = expectedUpdateTime == null ? "&currentDocument.exists=false"
            : "&currentDocument.updateTime=" + Uri.EscapeDataString(expectedUpdateTime);
        using var content = new StringContent(JsonSerializer.Serialize(new { fields }), Encoding.UTF8, "application/json");
        using var response = await http.PatchAsync(DocumentUrl(state.EventId, state.StudentId) + precondition, content);
        // Firestore can report a failed create/update precondition as ALREADY_EXISTS or FAILED_PRECONDITION.
        if (response.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.PreconditionFailed) return null;
        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            using var error = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (error.RootElement.TryGetProperty("error", out var detail) && detail.TryGetProperty("status", out var status) &&
                status.GetString() is "FAILED_PRECONDITION" or "ALREADY_EXISTS" or "ABORTED") return null;
        }
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var committed = Parse(document.RootElement);
        if (committed.ChangeId != state.ChangeId || committed.Approved != state.Approved ||
            EventRosterSyncRules.DocumentId(committed.EventId, committed.StudentId) != EventRosterSyncRules.DocumentId(state.EventId, state.StudentId))
            throw new InvalidDataException("Unexpected roster acknowledgment.");
        return committed;
    }
}
