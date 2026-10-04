using MySqlConnector;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace NFC_System;

public sealed partial class DatabaseService
{
    public async Task<EventCompletionReport> GetEventCompletionReportAsync(string eventId)
    {
        if (!AppSession.IsLoggedIn || !DatabaseMonitor.IsOnline)
            throw new InvalidOperationException("Sign in and connect to the database to evaluate event completion.");
        using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();
        await EnsureEventCompletionSchemaAsync(connection);
        await RequireCompletionTransactionsAsync(connection);
        using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead);
        var report = await ReadCompletionReportAsync(connection, transaction, eventId);
        await transaction.CommitAsync();
        return report;
    }

    public async Task SaveEventCompletionPolicyAsync(string eventId, EventCompletionPolicy policy, string reason, long sessionVersion)
    {
        RequireEventManagement(sessionVersion);
        EventCompletionRules.Validate(policy);
        reason = EventCompletionRules.ValidateReason(reason);
        var device = DeviceIdentity.CaptureCurrent();
        using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();
        await EnsureEventCompletionSchemaAsync(connection);
        await RequireCompletionTransactionsAsync(connection);
        using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable);
        _ = await ReadEventConfigurationAsync(connection, transaction, eventId, true)
            ?? throw new InvalidOperationException("The event no longer exists.");
        var current = await ReadCompletionPolicyAsync(connection, transaction, eventId);
        if ((current?.Revision ?? 0) != policy.Revision) throw new InvalidOperationException("Requirements changed. Reload before saving.");
        using var clock = new MySqlCommand("SELECT NOW(3)", connection, transaction);
        DateTime now = Convert.ToDateTime(await clock.ExecuteScalarAsync());
        var saved = policy with { Revision = policy.Revision + 1, CoverageSince = current?.CoverageSince ?? now };
        using var command = new MySqlCommand(@"INSERT INTO event_completion_policies
            VALUES(@event,@start,@end,@min,@late,@early,@revision,@coverage)
            ON DUPLICATE KEY UPDATE start_time=@start,end_time=@end,minimum_minutes=@min,late_minutes=@late,
                early_minutes=@early,revision=@revision", connection, transaction);
        command.Parameters.AddWithValue("@event", eventId);
        command.Parameters.AddWithValue("@start", saved.Start);
        command.Parameters.AddWithValue("@end", saved.End);
        command.Parameters.AddWithValue("@min", saved.MinimumMinutes);
        command.Parameters.AddWithValue("@late", saved.LateAllowanceMinutes);
        command.Parameters.AddWithValue("@early", saved.EarlyAllowanceMinutes);
        command.Parameters.AddWithValue("@revision", saved.Revision);
        command.Parameters.AddWithValue("@coverage", saved.CoverageSince);
        await command.ExecuteNonQueryAsync();
        await AuditCompletionAsync(connection, transaction, eventId, "", "Policy", current?.Revision.ToString() ?? "None",
            saved.Revision.ToString(), reason, device, "", JsonSerializer.Serialize(new { Previous = current, Saved = saved }));
        RequireEventManagement(sessionVersion);
        await transaction.CommitAsync();
    }

    public async Task ReviewEventCompletionAsync(string eventId, string studentId, string status, string reason,
        string expectedStamp, long expectedReviewId, long sessionVersion)
    {
        RequireEventManagement(sessionVersion);
        if (status is not (EventCompletionRules.Completed or EventCompletionRules.Incomplete or EventCompletionRules.NeedsReview))
            throw new ArgumentException("Choose Completed, Incomplete or Needs Review.");
        reason = EventCompletionRules.ValidateReason(reason);
        var device = DeviceIdentity.CaptureCurrent();
        using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();
        await EnsureEventCompletionSchemaAsync(connection);
        await RequireCompletionTransactionsAsync(connection);
        using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable);
        _ = await ReadEventConfigurationAsync(connection, transaction, eventId, true)
            ?? throw new InvalidOperationException("The event no longer exists.");
        var report = await ReadCompletionReportAsync(connection, transaction, eventId);
        var row = report.Rows.SingleOrDefault(r => r.StudentId == studentId)
            ?? throw new InvalidOperationException("The student is no longer in this report.");
        if (!row.CanReview) throw new InvalidOperationException("Close the event after its scheduled end and synchronize all attendance devices before reviewing completion.");
        if (row.EvidenceStamp != expectedStamp || (row.Review?.Id ?? 0) != expectedReviewId)
            throw new InvalidOperationException("Attendance, requirements or another review changed. Reload and review again.");
        await AuditCompletionAsync(connection, transaction, eventId, studentId, "Review", row.Effective.Status, status,
            reason, device, row.EvidenceStamp, JsonSerializer.Serialize(new { report.Policy, row.Automatic, PreviousReview = row.Review }));
        RequireEventManagement(sessionVersion);
        await transaction.CommitAsync();
    }

    private static async Task RequireCompletionTransactionsAsync(MySqlConnection connection)
    {
        using var command = new MySqlCommand(@"SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE()
            AND table_name IN ('events','event_attendance','event_approved_students','attendance_devices',
            'event_completion_policies','event_completion_scans','event_completion_audit') AND engine='InnoDB'", connection);
        if (Convert.ToInt32(await command.ExecuteScalarAsync()) != 7)
            throw new InvalidOperationException("Event completion requires transactional event, roster, device and completion tables.");
    }

    private static async Task<EventCompletionPolicy?> ReadCompletionPolicyAsync(MySqlConnection connection, MySqlTransaction transaction, string eventId)
    {
        using var command = new MySqlCommand("SELECT * FROM event_completion_policies WHERE event_id=@event", connection, transaction);
        command.Parameters.AddWithValue("@event", eventId);
        using var reader = await command.ExecuteReaderAsync();
        return !await reader.ReadAsync() ? null : new EventCompletionPolicy(reader.GetDateTime("start_time"), reader.GetDateTime("end_time"),
            reader.GetInt32("minimum_minutes"), reader.GetInt32("late_minutes"), reader.GetInt32("early_minutes"),
            reader.GetInt64("revision"), reader.GetDateTime("coverage_since"));
    }

    private static async Task<EventCompletionReport> ReadCompletionReportAsync(MySqlConnection connection, MySqlTransaction transaction, string eventId)
    {
        var evt = await ReadEventConfigurationAsync(connection, transaction, eventId, false)
            ?? throw new InvalidOperationException("The event no longer exists.");
        var policy = await ReadCompletionPolicyAsync(connection, transaction, eventId);
        DateTime now, utcNow;
        using (var clock = new MySqlCommand("SELECT NOW(3),UTC_TIMESTAMP(3)", connection, transaction))
        using (var reader = await clock.ExecuteReaderAsync())
        { await reader.ReadAsync(); now = reader.GetDateTime(0); utcNow = reader.GetDateTime(1); }

        MySqlCommand Query(string sql)
        {
            var command = new MySqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("@event", eventId);
            return command;
        }
        var scans = new List<EventCompletionScan>();
        using (var command = Query("SELECT * FROM event_completion_scans WHERE event_id=@event ORDER BY transaction_id"))
        using (var reader = await command.ExecuteReaderAsync())
            while (await reader.ReadAsync()) scans.Add(new(reader.GetString("transaction_id"), reader.GetString("student_id"),
                reader.GetDateTime("scan_time"), reader.GetString("status"), reader.GetString("device_id"), reader.GetInt64("device_sequence"),
                reader.GetBoolean("recovered"), reader.GetBoolean("legacy")));

        var untracked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var command = Query(@"SELECT a.id,a.student_id,a.timestamp,a.status FROM event_attendance a LEFT JOIN event_completion_scans e
            ON e.event_log_id=a.id AND e.event_id=a.event_id AND e.student_id=a.student_id AND e.scan_time=a.timestamp AND e.status=a.status
            WHERE a.event_id=@event AND e.transaction_id IS NULL"))
        using (var reader = await command.ExecuteReaderAsync())
            while (await reader.ReadAsync())
            {
                string student = Value(reader["student_id"]);
                untracked.Add(student);
                scans.Add(new("untracked-" + Value(reader["id"]), student,
                    reader.IsDBNull(reader.GetOrdinal("timestamp")) ? default : reader.GetDateTime("timestamp"),
                    Value(reader["status"]), "", 0, false, true));
            }

        var requiredDevices = scans.Select(s => s.DeviceId).Where(d => !string.IsNullOrWhiteSpace(d)).ToHashSet(StringComparer.Ordinal);
        bool synchronized = true;
        int enabledDevices = 0;
        var seenDevices = new HashSet<string>(StringComparer.Ordinal);
        using (var command = Query("SELECT * FROM attendance_devices"))
        using (var reader = await command.ExecuteReaderAsync())
            while (await reader.ReadAsync())
            {
                string id = reader.GetString("device_id");
                seenDevices.Add(id);
                bool enabled = reader.GetBoolean("enabled");
                if (enabled) enabledDevices++;
                if (!enabled && !requiredDevices.Contains(id)) continue;
                DateTime seen = reader.GetDateTime("last_seen");
                synchronized &= reader.GetBoolean("ready") && reader.GetInt64("reported_sequence") == reader.GetInt64("acknowledged_sequence") &&
                    seen >= utcNow.AddSeconds(-15) && seen <= utcNow.AddSeconds(15);
            }
        synchronized &= enabledDevices > 0 && requiredDevices.IsSubsetOf(seenDevices);

        var reviews = new Dictionary<string, EventCompletionReview>(StringComparer.OrdinalIgnoreCase);
        using (var command = Query("SELECT * FROM event_completion_audit WHERE event_id=@event AND action='Review' ORDER BY audit_id DESC"))
        using (var reader = await command.ExecuteReaderAsync())
            while (await reader.ReadAsync())
            {
                string id = reader.GetString("student_id");
                reviews.TryAdd(id, new(reader.GetInt64("audit_id"), id, reader.GetString("new_status"), reader.GetString("reason"),
                    reader.GetString("actor"), DateTime.SpecifyKind(reader.GetDateTime("reviewed_at_utc"), DateTimeKind.Utc), reader.GetString("evidence_stamp")));
            }
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using (var command = Query(@"SELECT s.student_id,s.full_name FROM students s WHERE
            EXISTS(SELECT 1 FROM event_completion_scans e WHERE e.event_id=@event AND e.student_id=s.student_id) OR
            EXISTS(SELECT 1 FROM event_attendance e WHERE e.event_id=@event AND e.student_id=s.student_id) OR
            EXISTS(SELECT 1 FROM event_approved_students e WHERE e.event_id=@event AND e.student_id=s.student_id)"))
        using (var reader = await command.ExecuteReaderAsync())
            while (await reader.ReadAsync()) names[Value(reader[0])] = Value(reader[1]);
        var grouped = scans.ToLookup(s => s.StudentId, StringComparer.OrdinalIgnoreCase);
        var ids = names.Keys.Concat(scans.Select(s => s.StudentId)).Concat(untracked).Concat(reviews.Keys)
            .Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase);
        bool active = evt.Status == "Active";
        bool canReview = synchronized && !active && policy != null && now >= policy.End;
        var rows = new List<EventCompletionRow>();
        foreach (string id in ids)
        {
            var evidence = grouped[id].ToArray();
            string stamp = EventCompletionRules.Stamp(policy, evidence, active, untracked.Contains(id));
            var decision = EventCompletionRules.Evaluate(policy, evidence, synchronized, active, now, untracked.Contains(id));
            reviews.TryGetValue(id, out var review);
            rows.Add(new(id, names.GetValueOrDefault(id, "Unknown student"), decision,
                EventCompletionRules.ApplyReview(decision, review, stamp, canReview), stamp, review, canReview));
        }
        return new(eventId, policy, synchronized, active, now, rows.OrderBy(r => r.Name).ThenBy(r => r.StudentId).ToArray());
    }

    private static async Task AuditCompletionAsync(MySqlConnection connection, MySqlTransaction transaction, string eventId,
        string studentId, string action, string previous, string status, string reason, DeviceAttribution device, string stamp, string evidence)
    {
        using var command = new MySqlCommand(@"INSERT INTO event_completion_audit
            (event_id,student_id,action,previous_status,new_status,reason,actor,actor_role,device_id,device_name,reviewed_at_utc,evidence_stamp,evidence_json)
            VALUES(@event,@student,@action,@previous,@status,@reason,@actor,@role,@device,@name,UTC_TIMESTAMP(3),@stamp,@evidence)", connection, transaction);
        command.Parameters.AddWithValue("@event", eventId);
        command.Parameters.AddWithValue("@student", studentId);
        command.Parameters.AddWithValue("@action", action);
        command.Parameters.AddWithValue("@previous", previous);
        command.Parameters.AddWithValue("@status", status);
        command.Parameters.AddWithValue("@reason", reason);
        command.Parameters.AddWithValue("@actor", AppSession.CurrentStaffName);
        command.Parameters.AddWithValue("@role", AppSession.CurrentStaffRoleLabel);
        command.Parameters.AddWithValue("@device", device.DeviceId);
        command.Parameters.AddWithValue("@name", device.DeviceName);
        command.Parameters.AddWithValue("@stamp", stamp);
        command.Parameters.AddWithValue("@evidence", evidence);
        await command.ExecuteNonQueryAsync();
    }
}
