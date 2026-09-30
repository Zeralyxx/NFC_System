using MySqlConnector;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NFC_System;

public sealed record EventRosterConflict(string EventId, string StudentId, string ChangeId, string CloudChangeId)
{
    public string DisplayLabel => $"Event: {EventId}\nStudent: {StudentId}";
}

public sealed partial class DatabaseService
{
    public async Task<IReadOnlyList<EventRosterConflict>> GetEventRosterConflictsAsync()
    {
        RequireEventManagement(AppSession.LoginVersion);
        using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();
        using var command = new MySqlCommand("SELECT event_id,student_id,change_id,cloud_change_id FROM event_roster_sync WHERE conflict=TRUE ORDER BY event_id,student_id", connection);
        var conflicts = new List<EventRosterConflict>();
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) conflicts.Add(new(Value(reader["event_id"]), Value(reader["student_id"]), Value(reader["change_id"]), Value(reader["cloud_change_id"])));
        return conflicts;
    }

    public async Task ConfirmEventRosterExclusionAsync(EventRosterConflict expected, long sessionVersion)
    {
        RequireEventManagement(sessionVersion);
        var device = DeviceIdentity.CaptureCurrent();
        using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();
        await RequireTransactionalEventTablesAsync(connection);
        using var transaction = await connection.BeginTransactionAsync();
        await ReadEventConfigurationAsync(connection, transaction, expected.EventId, true);
        var current = await LockRosterSyncAsync(connection, transaction, expected.EventId, expected.StudentId);
        if (current == null || !current.Conflict || current.ChangeId != expected.ChangeId || current.CloudChangeId != expected.CloudChangeId)
            throw new InvalidOperationException("This roster conflict changed. Reload the conflict list before confirming.");
        var excluded = current with { ChangeId = Guid.NewGuid().ToString("N"), Approved = false, Pending = true, Conflict = false };
        await SaveRosterSyncAsync(connection, transaction, excluded);
        await ApplyRosterMembershipAsync(connection, transaction, excluded);
        await AuditEventChangeAsync(connection, transaction,
            $"{AppSession.CurrentStaffName} confirmed roster exclusion for event '{expected.EventId}', student '{expected.StudentId}' after synchronization conflict.", device);
        RequireEventManagement(sessionVersion);
        await transaction.CommitAsync();
    }

    internal static async Task EnsureEventRosterSyncSchemaAsync(MySqlConnection connection)
    {
        using var command = new MySqlCommand(@"CREATE TABLE IF NOT EXISTS event_roster_sync (
            event_id VARCHAR(50) NOT NULL, student_id VARCHAR(50) NOT NULL, change_id CHAR(32) NOT NULL,
            approved BOOLEAN NOT NULL, cloud_change_id VARCHAR(32) NOT NULL DEFAULT '',
            cloud_update_time VARCHAR(40) NOT NULL DEFAULT '', pending BOOLEAN NOT NULL DEFAULT TRUE,
            conflict BOOLEAN NOT NULL DEFAULT FALSE, PRIMARY KEY(event_id,student_id)) ENGINE=InnoDB;
            INSERT IGNORE INTO event_roster_sync(event_id,student_id,change_id,approved)
            SELECT event_id,student_id,REPLACE(UUID(),'-',''),TRUE FROM event_approved_students;", connection);
        await command.ExecuteNonQueryAsync();
    }

    private static RosterSyncState ReadRosterSync(MySqlDataReader reader) => new(Value(reader["event_id"]),
        Value(reader["student_id"]), Value(reader["change_id"]), Convert.ToBoolean(reader["approved"]),
        Value(reader["cloud_change_id"]), Value(reader["cloud_update_time"]), Convert.ToBoolean(reader["pending"]), Convert.ToBoolean(reader["conflict"]));

    private static async Task<RosterSyncState?> LockRosterSyncAsync(MySqlConnection connection, MySqlTransaction transaction, string eventId, string studentId)
    {
        using var command = new MySqlCommand("SELECT * FROM event_roster_sync WHERE event_id=@event AND student_id=@id FOR UPDATE", connection, transaction);
        command.Parameters.AddWithValue("@event", eventId);
        command.Parameters.AddWithValue("@id", studentId);
        using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? ReadRosterSync(reader) : null;
    }

    private static async Task SaveRosterSyncAsync(MySqlConnection connection, MySqlTransaction transaction, RosterSyncState state)
    {
        using var command = new MySqlCommand(@"INSERT INTO event_roster_sync
            (event_id,student_id,change_id,approved,cloud_change_id,cloud_update_time,pending,conflict)
            VALUES (@event,@id,@change,@approved,@cloud,@time,@pending,@conflict)
            ON DUPLICATE KEY UPDATE change_id=@change,approved=@approved,cloud_change_id=@cloud,
                cloud_update_time=@time,pending=@pending,conflict=@conflict", connection, transaction);
        command.Parameters.AddWithValue("@event", state.EventId);
        command.Parameters.AddWithValue("@id", state.StudentId);
        command.Parameters.AddWithValue("@change", state.ChangeId);
        command.Parameters.AddWithValue("@approved", state.Approved);
        command.Parameters.AddWithValue("@cloud", state.CloudChangeId);
        command.Parameters.AddWithValue("@time", state.CloudUpdateTime);
        command.Parameters.AddWithValue("@pending", state.Pending);
        command.Parameters.AddWithValue("@conflict", state.Conflict);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<int> ApplyRosterMembershipAsync(MySqlConnection connection, MySqlTransaction transaction, RosterSyncState state)
    {
        string sql = state.Approved && !state.Conflict
            ? @"INSERT IGNORE INTO event_approved_students(event_id,student_id)
                SELECT e.event_id,s.student_id FROM events e JOIN students s ON s.student_id=@id WHERE e.event_id=@event"
            : "DELETE FROM event_approved_students WHERE event_id=@event AND student_id=@id";
        using var command = new MySqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@event", state.EventId);
        command.Parameters.AddWithValue("@id", state.StudentId);
        return await command.ExecuteNonQueryAsync();
    }

    private static async Task RecordRosterEditAsync(MySqlConnection connection, MySqlTransaction transaction, string eventId, string studentId, bool approved)
    {
        var current = await LockRosterSyncAsync(connection, transaction, eventId, studentId);
        var updated = new RosterSyncState(current?.EventId ?? eventId, current?.StudentId ?? studentId,
            Guid.NewGuid().ToString("N"), approved, current?.CloudChangeId ?? "", current?.CloudUpdateTime ?? "", true, false);
        await SaveRosterSyncAsync(connection, transaction, updated);
    }

    // Serialize cloud operations across processes sharing this database, while permitting local roster edits.
    private static async Task AcquireRosterSyncLockAsync(MySqlConnection connection)
    {
        using var command = new MySqlCommand("SELECT GET_LOCK(CONCAT('nfc_roster_',LEFT(SHA2(DATABASE(),256),40)),0)", connection);
        if (Convert.ToInt32(await command.ExecuteScalarAsync()) != 1)
            throw new InvalidOperationException("Another roster synchronization is running. Retry after it finishes.");
    }

    private static async Task ReleaseRosterSyncLockAsync(MySqlConnection connection)
    {
        using var command = new MySqlCommand("SELECT RELEASE_LOCK(CONCAT('nfc_roster_',LEFT(SHA2(DATABASE(),256),40)))", connection);
        await command.ExecuteScalarAsync();
    }

    private static async Task<(bool Conflict, bool Changed)> ApplyCloudRosterRevisionAsync(MySqlConnection connection, RosterCloudRevision remote, RosterSyncState? sent = null)
    {
        using var transaction = await connection.BeginTransactionAsync();
        // Use the same event -> membership lock order as administrator batch edits.
        await ReadEventConfigurationAsync(connection, transaction, remote.EventId, true);
        var local = await LockRosterSyncAsync(connection, transaction, remote.EventId, remote.StudentId);
        var merged = sent != null && local != null ? EventRosterSyncRules.Acknowledge(local, sent, remote) : EventRosterSyncRules.Merge(local, remote);
        await SaveRosterSyncAsync(connection, transaction, merged);
        int membershipChanges = await ApplyRosterMembershipAsync(connection, transaction, merged);
        if (merged.Conflict && local?.Conflict != true)
            await AuditEventChangeAsync(connection, transaction,
                $"Roster synchronization conflict for event '{remote.EventId}', student '{remote.StudentId}'. Attendee excluded pending an explicit roster review.", DeviceIdentity.CaptureCurrent());
        await transaction.CommitAsync();
        return (merged.Conflict, local != merged || membershipChanges > 0);
    }

    internal async Task<int> PullEventRosterStateAsync(IEventRosterCloudStore store)
    {
        using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();
        await RequireTransactionalEventTablesAsync(connection);
        await AcquireRosterSyncLockAsync(connection);
        try
        {
            // Validate every versioned page before considering the approval-only legacy archive.
            var revisions = await store.LoadAsync();
            var legacy = await store.LoadLegacyAsync();
            int changed = 0, conflicts = 0;
            foreach (var remote in revisions)
            {
                var result = await ApplyCloudRosterRevisionAsync(connection, remote);
                if (result.Conflict) conflicts++;
                if (result.Changed) changed++;
            }
            foreach (var (eventId, studentId) in legacy)
            {
                using var transaction = await connection.BeginTransactionAsync();
                await ReadEventConfigurationAsync(connection, transaction, eventId, true);
                // Any local revision, including an unsent removal, takes precedence over legacy approvals.
                if (await LockRosterSyncAsync(connection, transaction, eventId, studentId) == null)
                {
                    var imported = new RosterSyncState(eventId, studentId, Guid.NewGuid().ToString("N"), true, "", "", true, false);
                    await SaveRosterSyncAsync(connection, transaction, imported);
                    await ApplyRosterMembershipAsync(connection, transaction, imported);
                    changed++;
                }
                await transaction.CommitAsync();
            }
            if (conflicts > 0) throw new InvalidOperationException($"{conflicts} roster conflict(s). Affected attendees remain excluded. Review their roster entries and explicitly add them or confirm exclusion.");
            return changed;
        }
        finally { await ReleaseRosterSyncLockAsync(connection); }
    }

    internal async Task<int> PushEventRosterStateAsync(IEventRosterCloudStore store)
    {
        using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();
        await RequireTransactionalEventTablesAsync(connection);
        await AcquireRosterSyncLockAsync(connection);
        try
        {
            var pending = new List<RosterSyncState>();
            using (var command = new MySqlCommand("SELECT * FROM event_roster_sync WHERE pending=TRUE OR conflict=TRUE ORDER BY event_id,student_id", connection))
            using (var reader = await command.ExecuteReaderAsync())
                while (await reader.ReadAsync()) pending.Add(ReadRosterSync(reader));
            int pushed = 0, conflicts = 0;
            foreach (var state in pending)
            {
                if (state.Conflict) { conflicts++; continue; }
                var remote = await store.ReadAsync(state.EventId, state.StudentId);
                if (remote?.ChangeId == state.ChangeId)
                {
                    if ((await ApplyCloudRosterRevisionAsync(connection, remote, state)).Conflict) conflicts++;
                    else pushed++;
                    continue;
                }
                if ((remote?.ChangeId ?? "") != state.CloudChangeId)
                {
                    if (remote != null) await ApplyCloudRosterRevisionAsync(connection, remote);
                    else await MarkMissingRosterConflictAsync(connection, state);
                    conflicts++;
                    continue;
                }
                var committed = await store.TryWriteAsync(state, remote?.UpdateTime);
                if (committed == null)
                {
                    // The server changed after the read. Never retry an unconditional overwrite.
                    var newer = await store.ReadAsync(state.EventId, state.StudentId);
                    if (newer != null) await ApplyCloudRosterRevisionAsync(connection, newer);
                    else await MarkMissingRosterConflictAsync(connection, state);
                    conflicts++;
                    continue;
                }
                if ((await ApplyCloudRosterRevisionAsync(connection, committed, state)).Conflict) conflicts++;
                else pushed++;
            }
            if (conflicts > 0) throw new InvalidOperationException($"{conflicts} roster conflict(s) require review. Affected attendees remain excluded; no conflicting cloud approvals were overwritten.");
            return pushed;
        }
        finally { await ReleaseRosterSyncLockAsync(connection); }
    }

    private static async Task MarkMissingRosterConflictAsync(MySqlConnection connection, RosterSyncState sent)
    {
        using var transaction = await connection.BeginTransactionAsync();
        await ReadEventConfigurationAsync(connection, transaction, sent.EventId, true);
        var current = await LockRosterSyncAsync(connection, transaction, sent.EventId, sent.StudentId);
        if (current != null)
        {
            var blocked = current with { Conflict = true, CloudChangeId = "", CloudUpdateTime = "" };
            await SaveRosterSyncAsync(connection, transaction, blocked);
            await ApplyRosterMembershipAsync(connection, transaction, blocked);
        }
        await transaction.CommitAsync();
    }
}
