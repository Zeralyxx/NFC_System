using MySqlConnector;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NFC_System;

public sealed partial class DatabaseService
{
    private static readonly SemaphoreSlim DeviceSchemaLock = new(1, 1);
    public static string? DeviceAuditSyncError { get; private set; }

    // Also call after base schema creation: attendance initialization can run before these tables exist.
    private static async Task EnsureDeviceAttributionSchemaAsync(MySqlConnection connection)
    {
        await DeviceSchemaLock.WaitAsync();
        try
        {
            var tables = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            using (var query = new MySqlCommand(@"SELECT table_name, column_name FROM information_schema.columns
                WHERE table_schema=DATABASE() AND table_name IN
                ('attendance_devices','fast_mode_logs','standard_mode_logs','high_security_mode_logs','event_attendance','alerts')", connection))
            using (var reader = await query.ExecuteReaderAsync())
                while (await reader.ReadAsync())
                {
                    string table = reader.GetString(0);
                    if (!tables.TryGetValue(table, out var columns)) tables[table] = columns = new(StringComparer.OrdinalIgnoreCase);
                    columns.Add(reader.GetString(1));
                }

            foreach (var table in tables)
            {
                if (!table.Value.Contains("device_id"))
                    await AddColumnAsync(table.Key, "device_id", "VARCHAR(32) NOT NULL DEFAULT ''");
                if (!table.Value.Contains("device_name"))
                    await AddColumnAsync(table.Key, "device_name", "VARCHAR(100) NOT NULL DEFAULT 'Unknown/Legacy'");
            }
            if (tables.TryGetValue("alerts", out var alertColumns))
            {
                if (!alertColumns.Contains("device_name_change_id"))
                    await AddColumnAsync("alerts", "device_name_change_id", "VARCHAR(32) NULL");
                using var index = new MySqlCommand(@"SELECT COUNT(*) FROM information_schema.statistics
                    WHERE table_schema=DATABASE() AND table_name='alerts' AND index_name='unique_device_name_change'", connection);
                if (Convert.ToInt32(await index.ExecuteScalarAsync()) == 0)
                {
                    try
                    {
                        using var create = new MySqlCommand("ALTER TABLE alerts ADD UNIQUE INDEX unique_device_name_change (device_name_change_id)", connection);
                        await create.ExecuteNonQueryAsync();
                    }
                    catch (MySqlException ex) when (ex.Number == 1061) { }
                }
            }

            async Task AddColumnAsync(string table, string column, string definition)
            {
                try
                {
                    using var alter = new MySqlCommand($"ALTER TABLE `{table}` ADD COLUMN `{column}` {definition}", connection);
                    await alter.ExecuteNonQueryAsync();
                }
                catch (MySqlException ex) when (ex.Number == 1060) { }
            }
        }
        finally { DeviceSchemaLock.Release(); }
    }

    public async Task<DeviceRenameResult> RenameCurrentDeviceAsync(string name, long? expectedLoginVersion = null)
    {
        long version = expectedLoginVersion ?? AppSession.LoginVersion;
        DeviceIdentity.RequireRenamePermission(version);
        string staffName = AppSession.CurrentStaffName;
        string staffRole = AppSession.CurrentStaffRoleLabel;
        name = DeviceIdentity.ValidateName(name);
        await AttendanceLock.WaitAsync();
        try
        {
            bool changed = OfflineCacheService.AttendanceQueue.RenameDevice(name, staffName, staffRole,
                GetNetworkAdjustedTime(), () => DeviceIdentity.RequireRenamePermission(version));
            try
            {
                using var connection = new MySqlConnection(ConnectionString);
                await connection.OpenAsync();
                await EnsureAttendanceSchemaAsync(connection);
                await FlushDeviceNameChangesAsync(connection);
                DeviceAuditSyncError = null;
            }
            catch (Exception ex)
            {
                // The local name and audit are already durable; a heartbeat will retry the audit upload.
                System.Diagnostics.Debug.WriteLine($"Device rename audit pending: {ex.Message}");
                DeviceAuditSyncError = ex.Message;
            }
            var snapshot = OfflineCacheService.AttendanceQueue.Snapshot();
            return new(new(snapshot.DeviceId, snapshot.DeviceName), changed, snapshot.DeviceNameChanges.Count != 0);
        }
        finally { AttendanceLock.Release(); }
    }

    private static async Task FlushDeviceNameChangesAsync(MySqlConnection connection)
    {
        var snapshot = OfflineCacheService.AttendanceQueue.Snapshot();
        foreach (var change in snapshot.DeviceNameChanges.Take(200))
        {
            using var insert = new MySqlCommand(@"INSERT INTO alerts
                (timestamp,student_id,alert_type,message,device_id,device_name,device_name_change_id)
                VALUES (@ts,NULL,'ADMIN_ACTION',@message,@device,@name,@change)
                ON DUPLICATE KEY UPDATE device_name_change_id=device_name_change_id", connection);
            insert.Parameters.AddWithValue("@ts", ParseAttendanceTimestamp(change.Timestamp));
            insert.Parameters.AddWithValue("@message", $"{change.StaffRole} '{change.StaffName}' renamed device '{change.PreviousName}' to '{change.DeviceName}' ({change.DeviceId}).");
            insert.Parameters.AddWithValue("@device", change.DeviceId);
            insert.Parameters.AddWithValue("@name", change.DeviceName);
            insert.Parameters.AddWithValue("@change", change.ChangeId);
            await insert.ExecuteNonQueryAsync();
            OfflineCacheService.AttendanceQueue.AcknowledgeDeviceNameChange(change.ChangeId);
        }
        // Renaming an admin-only computer must not register it as an active attendance kiosk.
        snapshot = OfflineCacheService.AttendanceQueue.Snapshot();
        using var update = new MySqlCommand("UPDATE attendance_devices SET device_name=@name WHERE device_id=@id", connection);
        update.Parameters.AddWithValue("@id", snapshot.DeviceId);
        update.Parameters.AddWithValue("@name", snapshot.DeviceName);
        await update.ExecuteNonQueryAsync();
    }
}
