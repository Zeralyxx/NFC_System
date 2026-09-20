using MySqlConnector;
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace NFC_System;

public sealed partial class DatabaseService
{
    private static AttendanceBackupStore CreateAttendanceBackupStore() => new(_httpClient,
        $"https://firestore.googleapis.com/v1/projects/{FIREBASE_PROJECT_ID}/databases/(default)/documents", FIREBASE_API_KEY);

    public async Task<string> PushAttendanceBackupToCloudAsync()
    {
        var snapshot = await CaptureAttendanceBackupAsync();
        return await CreateAttendanceBackupStore().UploadAsync(snapshot);
    }

    internal async Task<AttendanceBackupSnapshot> CaptureAttendanceBackupAsync()
    {
        await AttendanceLock.WaitAsync();
        try
        {
            using var connection = new MySqlConnection(ConnectionString);
            await connection.OpenAsync();
            await EnsureAttendanceSchemaAsync(connection);
            using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead);
            var snapshot = new AttendanceBackupSnapshot();
            using (var clock = new MySqlCommand("SELECT UTC_TIMESTAMP(3), COUNT(*) FROM attendance_devices", connection, transaction))
                snapshot.CapturedAtUtc = DateTime.SpecifyKind(Convert.ToDateTime(await clock.ExecuteScalarAsync()), DateTimeKind.Utc);
            foreach (string table in AttendanceBackupSnapshot.TableNames)
            {
                using var command = new MySqlCommand($"SELECT * FROM {table}", connection, transaction);
                using var reader = await command.ExecuteReaderAsync();
                var rows = new List<Dictionary<string, JsonElement>>();
                while (await reader.ReadAsync())
                {
                    var row = new Dictionary<string, JsonElement>();
                    for (int i = 0; i < reader.FieldCount; i++)
                        row[reader.GetName(i)] = JsonSerializer.SerializeToElement(reader.IsDBNull(i) ? null : reader.GetValue(i));
                    rows.Add(row);
                }
                snapshot.Tables.Add(table, rows);
            }
            foreach (string table in AttendanceBackupSnapshot.TableNames.Skip(5))
            {
                using var counter = new MySqlCommand("SELECT AUTO_INCREMENT FROM information_schema.tables WHERE table_schema=DATABASE() AND table_name=@table", connection, transaction);
                counter.Parameters.AddWithValue("@table", table);
                snapshot.NextLogIds[table] = Math.Max(1, Convert.ToInt64(await counter.ExecuteScalarAsync()));
            }
            await transaction.CommitAsync();
            return snapshot;
        }
        finally { AttendanceLock.Release(); }
    }

    // Called by the existing administrator-authorized download workflow, never by heartbeat recovery.
    public async Task<int> RestoreAttendanceBackupIfEmptyAsync()
    {
        using (var connection = new MySqlConnection(ConnectionString))
        {
            await connection.OpenAsync();
            await EnsureAttendanceSchemaAsync(connection);
            foreach (string table in AttendanceBackupSnapshot.TableNames.Where(t => t != "attendance_devices"))
            {
                using var count = new MySqlCommand($"SELECT COUNT(*) FROM {table}", connection);
                if (Convert.ToInt64(await count.ExecuteScalarAsync()) != 0) return 0;
            }
        }
        var snapshot = await CreateAttendanceBackupStore().DownloadLatestAsync();
        return snapshot == null ? 0 : await RestoreAttendanceSnapshotAsync(snapshot);
    }

    internal async Task<int> RestoreAttendanceSnapshotAsync(AttendanceBackupSnapshot snapshot)
    {
        snapshot.Validate();
        await AttendanceLock.WaitAsync();
        try
        {
            var local = OfflineCacheService.AttendanceQueue.Snapshot();
            if (local.GateLogs.Count != 0 || local.EventLogs.Count != 0)
                throw new InvalidOperationException("Restore requires an empty local queue. Preserve pending records and reconcile them with the administrator first.");
            OfflineCacheService.AttendanceQueue.RequireCacheRefresh();
            using var connection = new MySqlConnection(ConnectionString);
            await connection.OpenAsync();
            await EnsureAttendanceSchemaAsync(connection);
            using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable);

            // Lock empty ranges too: another kiosk cannot insert a live transaction during restore.
            foreach (string table in AttendanceBackupSnapshot.TableNames.Where(t => t != "attendance_devices"))
            {
                using var check = new MySqlCommand($"SELECT * FROM {table} FOR UPDATE", connection, transaction);
                using var reader = await check.ExecuteReaderAsync();
                if (await reader.ReadAsync()) throw new InvalidOperationException("Attendance restore is only allowed into an empty attendance database. Existing attendance was not replaced.");
            }
            using (var check = new MySqlCommand("SELECT device_id FROM attendance_devices WHERE device_id<>@local AND enabled=TRUE AND last_seen>UTC_TIMESTAMP(3)-INTERVAL 15 SECOND FOR UPDATE", connection, transaction))
            {
                check.Parameters.AddWithValue("@local", local.DeviceId);
                if (await check.ExecuteScalarAsync() != null) throw new InvalidOperationException("Stop other attendance devices before restoring this database.");
            }
            using (var clearBootstrap = new MySqlCommand("DELETE FROM attendance_devices", connection, transaction))
                await clearBootstrap.ExecuteNonQueryAsync();

            int restored = 0;
            foreach (string table in AttendanceBackupSnapshot.TableNames)
            {
                var columns = new HashSet<string>(StringComparer.Ordinal);
                using (var schema = new MySqlCommand($"SELECT * FROM {table} LIMIT 0", connection, transaction))
                using (var reader = await schema.ExecuteReaderAsync())
                    for (int i = 0; i < reader.FieldCount; i++) columns.Add(reader.GetName(i));
                foreach (var row in snapshot.Tables[table])
                {
                    if (row.Count == 0 || row.Keys.Any(c => !columns.Contains(c))) throw new InvalidDataException($"Backup columns do not match {table}.");
                    var names = row.Keys.ToArray();
                    using var insert = new MySqlCommand($"INSERT INTO {table} ({string.Join(",", names.Select(n => "`" + n + "`"))}) VALUES ({string.Join(",", names.Select((_, i) => "@p" + i))})", connection, transaction);
                    for (int i = 0; i < names.Length; i++)
                    {
                        object value = BackupParameter(names[i], row[names[i]]);
                        if (table == "attendance_devices" && names[i] == "ready") value = false;
                        if (table == "attendance_devices" && names[i] == "last_seen") value = new DateTime(1970, 1, 1);
                        insert.Parameters.AddWithValue("@p" + i, value);
                    }
                    await insert.ExecuteNonQueryAsync();
                    restored++;
                }
            }
            foreach (var row in snapshot.Tables["attendance_current"])
            {
                using (var profile = new MySqlCommand("SELECT student_id FROM students WHERE student_id=@sid FOR UPDATE", connection, transaction))
                {
                    profile.Parameters.AddWithValue("@sid", row["student_id"].GetString());
                    if (await profile.ExecuteScalarAsync() == null)
                        throw new InvalidOperationException("Restore the missing student profiles before restoring attendance. No attendance was restored.");
                }
                var state = JsonSerializer.Deserialize<AttendanceState>(row["state_json"].GetString()!)
                    ?? throw new InvalidDataException("Invalid student attendance state in backup.");
                if (state.EntryState != "INSIDE" && state.EntryState != "OUTSIDE") throw new InvalidDataException("Invalid entry state in backup.");
                using var update = new MySqlCommand("UPDATE students SET entry_state=@state WHERE student_id=@sid", connection, transaction);
                update.Parameters.AddWithValue("@state", state.EntryState);
                update.Parameters.AddWithValue("@sid", row["student_id"].GetString());
                await update.ExecuteNonQueryAsync();
            }
            // Reserve historical IDs even when their old log rows were purged. Do not reuse cloud document IDs.
            // InnoDB keeps the allocated counter after this transaction-local reservation row is deleted.
            foreach (string table in AttendanceBackupSnapshot.TableNames.Skip(5))
            {
                long reserved = snapshot.NextLogIds[table] - 1;
                if (reserved <= 0 || snapshot.Tables[table].Any(row => row["id"].GetInt64() == reserved)) continue;
                using var reserve = new MySqlCommand($"INSERT INTO {table} (id) VALUES (@id); DELETE FROM {table} WHERE id=@id", connection, transaction);
                reserve.Parameters.AddWithValue("@id", reserved);
                await reserve.ExecuteNonQueryAsync();
            }
            await transaction.CommitAsync();
            // Restored devices remain unready until they reconnect and refresh their own caches.
            return restored;
        }
        finally { AttendanceLock.Release(); }
    }

    private static object BackupParameter(string column, JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Null) return DBNull.Value;
        if (value.ValueKind is JsonValueKind.True or JsonValueKind.False) return value.GetBoolean();
        if (value.ValueKind == JsonValueKind.Number) return value.TryGetInt64(out long integer) ? (object)integer : value.GetDouble();
        if (value.ValueKind != JsonValueKind.String) throw new InvalidDataException("Unsupported backup field type.");
        string text = value.GetString()!;
        return column is "timestamp" or "last_seen" or "time_in" or "time_out"
            ? DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind) : text;
    }
}
