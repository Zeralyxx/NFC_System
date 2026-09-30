using MySqlConnector;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NFC_System;

public sealed record EventRosterWriteResult(int Changed, int AlreadyIncluded, int Ineligible, int Unknown);

public sealed partial class DatabaseService
{
    private static void RequireEventManagement(long sessionVersion)
    {
        if (!DatabaseMonitor.IsOnline) throw new InvalidOperationException("An online database connection is required.");
        if (sessionVersion != AppSession.LoginVersion ||
            !EventEditPolicy.CanManage(AppSession.IsLoggedIn, AppSession.IsAdmin, AppSession.IsEventOrganizer))
            throw new UnauthorizedAccessException("Sign in as an Administrator or Event Organizer to manage events.");
    }

    private static async Task RequireTransactionalEventTablesAsync(MySqlConnection connection)
    {
        using var command = new MySqlCommand(@"SELECT COUNT(*) FROM information_schema.tables
            WHERE table_schema = DATABASE() AND table_name IN ('events','event_approved_students','event_roster_sync','students','alerts')
            AND engine = 'InnoDB'", connection);
        if (Convert.ToInt32(await command.ExecuteScalarAsync()) != 5)
            throw new InvalidOperationException("Event changes require events, event_approved_students, event_roster_sync, students and alerts to use InnoDB.");
    }

    public async Task<IReadOnlyList<EventRosterStudent>> GetEventRosterDirectoryAsync(string eventId)
    {
        RequireEventManagement(AppSession.LoginVersion);
        using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();
        using var command = new MySqlCommand(@"SELECT s.student_id,s.full_name,s.course,s.year_level,s.section_name,s.status,
            EXISTS(SELECT 1 FROM event_approved_students a WHERE a.event_id=@event AND a.student_id=s.student_id) AS included,
            EXISTS(SELECT 1 FROM event_roster_sync r WHERE r.event_id=@event AND r.student_id=s.student_id AND r.conflict=TRUE) AS sync_conflict
            FROM students s ORDER BY s.full_name,s.student_id", connection);
        command.Parameters.AddWithValue("@event", eventId);
        var result = new List<EventRosterStudent>();
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) result.Add(new EventRosterStudent
        {
            StudentId = Value(reader["student_id"]), FullName = Value(reader["full_name"]),
            Course = Value(reader["course"]), YearLevel = Value(reader["year_level"]),
            SectionName = Value(reader["section_name"]), Status = Value(reader["status"]),
            IsIncluded = Convert.ToBoolean(reader["included"]), HasSyncConflict = Convert.ToBoolean(reader["sync_conflict"])
        });
        return result;
    }

    public async Task<EventRecord?> GetEventConfigurationAsync(string eventId)
    {
        using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();
        return await ReadEventConfigurationAsync(connection, null, eventId, false);
    }

    private static async Task<EventRecord?> ReadEventConfigurationAsync(MySqlConnection connection,
        MySqlTransaction? transaction, string eventId, bool forUpdate)
    {
        using var command = new MySqlCommand(@"SELECT event_id,event_name,event_date,verification_mode,is_restricted,is_active
            FROM events WHERE event_id=@id" + (forUpdate ? " FOR UPDATE" : ""), connection, transaction);
        command.Parameters.AddWithValue("@id", eventId);
        using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        if (!Enum.TryParse<VerificationMode>(Value(reader["verification_mode"]), out var mode) || !Enum.IsDefined(mode))
            throw new InvalidOperationException("The event has an invalid verification mode.");
        return new EventRecord
        {
            EventId = Value(reader["event_id"]), EventName = Value(reader["event_name"]), VerificationMode = mode,
            EventDate = reader["event_date"] is DBNull ? null : Convert.ToDateTime(reader["event_date"]),
            IsRestricted = Convert.ToBoolean(reader["is_restricted"]), Status = Convert.ToBoolean(reader["is_active"]) ? "Active" : "Closed"
        };
    }

    private static async Task AuditEventChangeAsync(MySqlConnection connection, MySqlTransaction transaction, string message, DeviceAttribution device)
    {
        using var command = new MySqlCommand("INSERT INTO alerts (student_id,alert_type,message,device_id,device_name) VALUES (NULL,'ADMIN_ACTION',@message,@device,@name)", connection, transaction);
        command.Parameters.AddWithValue("@device", device.DeviceId);
        command.Parameters.AddWithValue("@name", device.DeviceName);
        command.Parameters.AddWithValue("@message", message);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<EventRecord> UpdateEventDetailsAsync(string eventId, string expectedName, VerificationMode expectedMode,
        string newName, VerificationMode newMode, long sessionVersion, string? authorizingUid = null, string? authorizingPin = null)
    {
        RequireEventManagement(sessionVersion);
        bool modeChanged = newMode != expectedMode;
        var device = DeviceIdentity.CaptureCurrent();
        string actor = AppSession.CurrentStaffName;
        string authorizedBy = actor;
        if (modeChanged)
        {
            if (!EventEditPolicy.CanChangeMode(AppSession.IsLoggedIn, AppSession.IsAdmin, AppSession.CurrentStaffRoleLabel))
                throw new UnauthorizedAccessException("Only an Administrator or Master Admin may change verification mode.");
            if (AppSession.CurrentStaffRoleLabel != "Master Admin")
            {
                if (string.IsNullOrWhiteSpace(authorizingUid) || string.IsNullOrWhiteSpace(authorizingPin))
                    throw new UnauthorizedAccessException("Administrator NFC and PIN authorization is required.");
                var staff = await GetStaffDetailsAsync(authorizingUid);
                if (staff.Role is not ("Administrator" or "Master Administrator") ||
                    string.IsNullOrEmpty(staff.PinSalt) || string.IsNullOrEmpty(staff.PinHash) ||
                    !PinHasher.VerifyPin(authorizingPin, staff.PinSalt, staff.PinHash))
                    throw new UnauthorizedAccessException("Administrator NFC or PIN is invalid.");
                authorizedBy = staff.FullName ?? staff.Role;
            }
        }
        using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();
        await RequireTransactionalEventTablesAsync(connection);
        using var transaction = await connection.BeginTransactionAsync();
        var current = await ReadEventConfigurationAsync(connection, transaction, eventId, true)
            ?? throw new InvalidOperationException("The event no longer exists.");
        EventEditPolicy.ValidateEdit(current, expectedName, expectedMode, newName, newMode);
        using var command = new MySqlCommand(@"UPDATE events SET event_name=@name,verification_mode=@mode WHERE event_id=@id", connection, transaction);
        command.Parameters.AddWithValue("@id", eventId);
        command.Parameters.AddWithValue("@name", newName.Trim());
        command.Parameters.AddWithValue("@mode", ToStorageValue(newMode));
        RequireEventManagement(sessionVersion);
        await command.ExecuteNonQueryAsync();
        await AuditEventChangeAsync(connection, transaction,
            $"Event '{eventId}' updated by {actor}; authorized by {authorizedBy}; name '{current.EventName}' -> '{newName.Trim()}'; mode {ToStorageValue(current.VerificationMode)} -> {ToStorageValue(newMode)}.", device);
        RequireEventManagement(sessionVersion);
        await transaction.CommitAsync();
        current.EventName = newName.Trim();
        current.VerificationMode = newMode;
        return current;
    }

    public async Task<EventRosterWriteResult> ApplyEventRosterSelectionAsync(string eventId, IEnumerable<string> studentIds,
        bool remove, long sessionVersion)
    {
        RequireEventManagement(sessionVersion);
        var ids = EventRosterRules.NormalizeIds(studentIds).OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToArray();
        var device = DeviceIdentity.CaptureCurrent();
        if (ids.Length == 0) throw new ArgumentException("Select at least one student.");
        using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();
        await RequireTransactionalEventTablesAsync(connection);
        using var transaction = await connection.BeginTransactionAsync();
        var current = await ReadEventConfigurationAsync(connection, transaction, eventId, true);
        if (current == null || current.Status != "Active" || !current.IsRestricted)
            throw new InvalidOperationException("Roster changes require an active restricted event. Refresh the event list.");
        int changed = 0, included = 0, ineligible = 0, unknown = 0;
        foreach (var id in ids)
        {
            RequireEventManagement(sessionVersion);
            if (remove)
            {
                var previousSync = await LockRosterSyncAsync(connection, transaction, current.EventId, id);
                using var deletion = new MySqlCommand("DELETE FROM event_approved_students WHERE event_id=@event AND student_id=@id", connection, transaction);
                deletion.Parameters.AddWithValue("@event", eventId);
                deletion.Parameters.AddWithValue("@id", id);
                int deleted = await deletion.ExecuteNonQueryAsync();
                changed += deleted == 0 && previousSync?.Conflict == true ? 1 : deleted;
                await RecordRosterEditAsync(connection, transaction, current.EventId, id, false);
                continue;
            }
            using var student = new MySqlCommand("SELECT status FROM students WHERE student_id=@id FOR UPDATE", connection, transaction);
            student.Parameters.AddWithValue("@id", id);
            object? status = await student.ExecuteScalarAsync();
            if (status == null || status is DBNull) { unknown++; continue; }
            using var existing = new MySqlCommand("SELECT COUNT(*) FROM event_approved_students WHERE event_id=@event AND student_id=@id", connection, transaction);
            existing.Parameters.AddWithValue("@event", eventId);
            existing.Parameters.AddWithValue("@id", id);
            if (Convert.ToInt32(await existing.ExecuteScalarAsync()) > 0) { included++; continue; }
            if (!string.Equals(status.ToString(), "Active", StringComparison.OrdinalIgnoreCase)) { ineligible++; continue; }
            using var insertion = new MySqlCommand("INSERT INTO event_approved_students (event_id,student_id) VALUES (@event,@id)", connection, transaction);
            insertion.Parameters.AddWithValue("@event", eventId);
            insertion.Parameters.AddWithValue("@id", id);
            changed += await insertion.ExecuteNonQueryAsync();
            await RecordRosterEditAsync(connection, transaction, current.EventId, id, true);
        }
        await AuditEventChangeAsync(connection, transaction,
            $"{AppSession.CurrentStaffName} {(remove ? "removed" : "added")} {changed} roster entries for event '{eventId}'. Selected {ids.Length}; already included {included}; inactive {ineligible}; unknown {unknown}.", device);
        RequireEventManagement(sessionVersion);
        await transaction.CommitAsync();
        return new EventRosterWriteResult(changed, included, ineligible, unknown);
    }

    public async Task CreateNewEventAsync(string eventId, string name, VerificationMode mode, bool restricted, long sessionVersion)
    {
        var device = DeviceIdentity.CaptureCurrent();
        RequireEventManagement(sessionVersion);
        if (string.IsNullOrWhiteSpace(eventId) || eventId.Length > 50 || string.IsNullOrWhiteSpace(name) || name.Length > 150 || !Enum.IsDefined(mode))
            throw new ArgumentException("Provide a valid event ID, name and verification mode.");
        using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();
        await RequireTransactionalEventTablesAsync(connection);
        using var transaction = await connection.BeginTransactionAsync();
        using var command = new MySqlCommand(@"INSERT INTO events (event_id,event_name,event_date,verification_mode,is_restricted,is_active)
            VALUES (@id,@name,NOW(),@mode,@restricted,TRUE)", connection, transaction);
        command.Parameters.AddWithValue("@id", eventId.Trim());
        command.Parameters.AddWithValue("@name", name.Trim());
        command.Parameters.AddWithValue("@mode", ToStorageValue(mode));
        command.Parameters.AddWithValue("@restricted", restricted);
        try { await command.ExecuteNonQueryAsync(); }
        catch (MySqlException ex) when (ex.Number == 1062)
        { throw new InvalidOperationException("That event ID already exists. Open the existing event to edit it.", ex); }
        await AuditEventChangeAsync(connection, transaction, $"{AppSession.CurrentStaffName} created event '{eventId}' with mode {ToStorageValue(mode)}.", device);
        RequireEventManagement(sessionVersion);
        await transaction.CommitAsync();
    }
}
