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
    private const string SyncColumns = "student_id,full_name,email,course,year_level,section_name,status,nfc_uid,qr_credential,pin_salt,pin_hash,photo_data,is_temporary";

    internal static async Task EnsureStudentSyncSchemaAsync(MySqlConnection connection)
    {
        using var command = new MySqlCommand(@"
            CREATE TABLE IF NOT EXISTS student_sync_baselines (
                server_id CHAR(32) NOT NULL, student_id VARCHAR(50) NOT NULL,
                main_fingerprint VARCHAR(64) NOT NULL, local_fingerprint VARCHAR(64) NOT NULL,
                profile_json MEDIUMTEXT NOT NULL, request_id VARCHAR(32) NOT NULL DEFAULT '', request_json MEDIUMTEXT NOT NULL,
                PRIMARY KEY(server_id,student_id)) ENGINE=InnoDB;
            CREATE TABLE IF NOT EXISTS student_sync_receipts (
                request_id CHAR(32) PRIMARY KEY, author_uid VARCHAR(128) NOT NULL, request_json MEDIUMTEXT NOT NULL,
                result_json TEXT NOT NULL, applied_at_utc DATETIME(6) NOT NULL) ENGINE=InnoDB;
            CREATE TABLE IF NOT EXISTS student_sync_created (student_id VARCHAR(50) PRIMARY KEY) ENGINE=InnoDB;", connection);
        await command.ExecuteNonQueryAsync();
    }

    internal static async Task MarkNewSyncProfileAsync(MySqlConnection connection, MySqlTransaction transaction, string id)
    {
        using var command = new MySqlCommand("INSERT IGNORE INTO student_sync_created VALUES (@id)", connection, transaction);
        command.Parameters.AddWithValue("@id", id);
        await command.ExecuteNonQueryAsync();
    }

    internal async Task<HashSet<string>> ReadNewSyncProfileIdsAsync()
    {
        using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();
        await EnsureStudentSyncSchemaAsync(connection);
        using var command = new MySqlCommand("SELECT student_id FROM student_sync_created", connection);
        using var reader = await command.ExecuteReaderAsync();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync()) ids.Add(reader.GetString(0));
        return ids;
    }

    internal async Task ClearFinishedSyncRequestsAsync(string serverId)
    {
        using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();
        using var command = new MySqlCommand("UPDATE student_sync_baselines SET request_id='',request_json='' WHERE server_id=@server", connection);
        command.Parameters.AddWithValue("@server", serverId);
        await command.ExecuteNonQueryAsync();
    }

    private static StudentSyncProfile ReadSyncProfile(MySqlDataReader reader) => new(
        Value(reader["student_id"]), Value(reader["full_name"]), Value(reader["email"]), Value(reader["course"]),
        Value(reader["year_level"]), Value(reader["section_name"]), Value(reader["status"]), Value(reader["nfc_uid"]),
        Value(reader["qr_credential"]), Value(reader["pin_salt"]), Value(reader["pin_hash"]),
        reader["photo_data"] is byte[] photo ? photo : null, !reader.IsDBNull(reader.GetOrdinal("is_temporary")) && Convert.ToBoolean(reader["is_temporary"]));

    internal async Task<List<StudentSyncProfile>> ReadSyncProfilesAsync()
    {
        using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();
        await EnsureStudentSyncSchemaAsync(connection);
        using var command = new MySqlCommand("SELECT " + SyncColumns + " FROM students ORDER BY student_id", connection);
        using var reader = await command.ExecuteReaderAsync();
        var profiles = new List<StudentSyncProfile>();
        while (await reader.ReadAsync()) profiles.Add(ReadSyncProfile(reader));
        return profiles;
    }

    private static async Task<StudentSyncProfile?> LockSyncProfileAsync(MySqlConnection connection, MySqlTransaction transaction, string id)
    {
        using var command = new MySqlCommand("SELECT " + SyncColumns + " FROM students WHERE student_id=@id FOR UPDATE", connection, transaction);
        command.Parameters.AddWithValue("@id", id);
        using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? ReadSyncProfile(reader) : null;
    }

    internal async Task<Dictionary<string, StudentSyncBaseline>> ReadSyncBaselinesAsync(string serverId)
    {
        using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();
        await EnsureStudentSyncSchemaAsync(connection);
        using var command = new MySqlCommand("SELECT * FROM student_sync_baselines WHERE server_id=@server", connection);
        command.Parameters.AddWithValue("@server", serverId);
        using var reader = await command.ExecuteReaderAsync();
        var rows = new Dictionary<string, StudentSyncBaseline>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync()) rows.Add(reader.GetString("student_id"), new(reader.GetString("main_fingerprint"),
            reader.GetString("local_fingerprint"), reader.GetString("profile_json"), reader.GetString("request_id"), reader.GetString("request_json")));
        return rows;
    }

    internal async Task SaveSyncBaselineAsync(string serverId, string id, StudentSyncBaseline baseline)
    {
        using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();
        await EnsureStudentSyncSchemaAsync(connection);
        await WriteSyncBaselineAsync(connection, null, serverId, id, baseline);
    }

    private static async Task WriteSyncBaselineAsync(MySqlConnection connection, MySqlTransaction? transaction,
        string serverId, string id, StudentSyncBaseline baseline)
    {
        using var command = new MySqlCommand(@"INSERT INTO student_sync_baselines
            (server_id,student_id,main_fingerprint,local_fingerprint,profile_json,request_id,request_json)
            VALUES (@server,@id,@main,@local,@profile,@request,@json)
            ON DUPLICATE KEY UPDATE main_fingerprint=@main,local_fingerprint=@local,profile_json=@profile,request_id=@request,request_json=@json", connection, transaction);
        command.Parameters.AddWithValue("@server", serverId);
        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@main", baseline.MainFingerprint);
        command.Parameters.AddWithValue("@local", baseline.LocalFingerprint);
        command.Parameters.AddWithValue("@profile", baseline.ProfileJson);
        command.Parameters.AddWithValue("@request", baseline.RequestId);
        command.Parameters.AddWithValue("@json", baseline.RequestJson);
        await command.ExecuteNonQueryAsync();
    }

    internal async Task<StudentSyncResult> ApplyStudentSyncAsync(StudentSyncRequest request, bool approved,
        Func<StudentSyncProfile, bool>? verifyQr = null)
    {
        if (!Guid.TryParseExact(request.Id, "N", out _)) throw new InvalidOperationException("Invalid change ID.");
        using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();
        await EnsureStudentSyncSchemaAsync(connection);
        await EnsurePinSchemaAsync(connection);
        using (var engines = new MySqlCommand("SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE() AND table_name IN ('students','alerts','student_sync_receipts','student_graduation_audit','pin_security_versions') AND engine='InnoDB'", connection))
            if (Convert.ToInt32(await engines.ExecuteScalarAsync()) != 5) throw new InvalidOperationException("Student synchronization requires transactional InnoDB tables.");
        using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable);
        string requestJson = JsonSerializer.Serialize(request with { Status = "Pending", Detail = "", ResultFingerprint = "" });
        using (var prior = new MySqlCommand("SELECT author_uid,request_json,result_json FROM student_sync_receipts WHERE request_id=@id FOR UPDATE", connection, transaction))
        {
            prior.Parameters.AddWithValue("@id", request.Id);
            using var reader = await prior.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                if (reader.GetString(0) != request.AuthorUid || reader.GetString(1) != requestJson)
                    throw new InvalidOperationException("Change ID was reused with different content.");
                return JsonSerializer.Deserialize<StudentSyncResult>(reader.GetString(2))!;
            }
        }
        var profile = request.Profile;
        StudentSyncProfile? current = null;
        StudentSyncResult result;
        using (var savepoint = new MySqlCommand("SAVEPOINT profile_change", connection, transaction)) await savepoint.ExecuteNonQueryAsync();
        try
        {
            if (!approved) throw new UnauthorizedAccessException("Uploader is no longer an approved student administrator.");
            profile.Validate();
            current = await LockSyncProfileAsync(connection, transaction, profile.StudentId);
            if (request.ExpectedFingerprint != (current?.Fingerprint() ?? ""))
                result = new("Needs Review", "The main-server profile changed. Download the latest profile, review it, then edit and upload again.");
            else
            {
                if (current == null) GraduationRules.RequireNewStudentStatus(profile.Status, profile.NfcUid, profile.PinHash.Length > 0);
                else
                {
                    StudentProfileRules.ValidateEdit(profile.StudentId, profile.FullName, profile.Status, profile.NfcUid, current.NfcUid.Length > 0);
                    var graduation = await LockGraduationStudentAsync(connection, transaction, profile.StudentId);
                    GraduationRules.RequireGraduationClearance(graduation with { HasNfc = profile.NfcUid.Length > 0, HasPin = profile.PinHash.Length > 0 }, profile.Status);
                }
                bool pinChanged = current == null ? profile.PinHash.Length > 0 : current.PinHash != profile.PinHash || current.PinSalt != profile.PinSalt;
                if (pinChanged != request.ResetPin || request.ResetPin && profile.PinHash.Length == 0)
                    throw new InvalidOperationException("PIN changes require an explicit nonempty PIN reset.");
                bool qrChanged = current == null ? profile.QrCredential.Length > 0 || profile.NfcUid.Length > 0
                    : current.QrCredential != profile.QrCredential || current.NfcUid != profile.NfcUid;
                if (qrChanged && !(verifyQr ?? VerifySyncQr)(profile))
                    throw new InvalidOperationException("The QR credential is not signed by a trusted enrollment key for this student.");
                await WriteSyncProfileAsync(connection, transaction, profile, current == null, request.ResetPin);
                if (request.ResetPin) await RotatePinResetAsync(connection, transaction, profile.StudentId);
                var device = DeviceIdentity.CaptureCurrent();
                using var audit = new MySqlCommand(@"INSERT INTO alerts (student_id,alert_type,message,device_id,device_name)
                    VALUES (@id,'ADMIN_ACTION',@message,@device,@name)", connection, transaction);
                audit.Parameters.AddWithValue("@id", profile.StudentId);
                audit.Parameters.AddWithValue("@message", $"Student profile cloud change {request.Id} applied from approved account {request.AuthorUid}. PIN reset: {request.ResetPin}.");
                audit.Parameters.AddWithValue("@device", device.DeviceId);
                audit.Parameters.AddWithValue("@name", device.DeviceName);
                await audit.ExecuteNonQueryAsync();
                if (current != null && current.Status != profile.Status)
                    await AddGraduationAuditAsync(connection, transaction,
                        await LockGraduationStudentAsync(connection, transaction, profile.StudentId), "EnrollmentStatus", current.Status,
                        profile.Status, "Approved cloud student-profile change " + request.Id, request.AuthorUid, device);
                var saved = await LockSyncProfileAsync(connection, transaction, profile.StudentId);
                result = new("Applied", "Applied to the main database.", saved!.Fingerprint());
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or System.IO.InvalidDataException or FormatException or ArgumentException)
        {
            using var rollback = new MySqlCommand("ROLLBACK TO SAVEPOINT profile_change", connection, transaction);
            await rollback.ExecuteNonQueryAsync();
            result = new("Rejected", ex.Message);
        }
        catch (MySqlException ex) when (ex.Number == 1062)
        {
            using var rollback = new MySqlCommand("ROLLBACK TO SAVEPOINT profile_change", connection, transaction);
            await rollback.ExecuteNonQueryAsync();
            result = new("Rejected", "Student ID or NFC card is already assigned to another profile.");
        }
        // SQL failures roll back the entire change and are retried; validation outcomes get durable receipts.
        using var receipt = new MySqlCommand("INSERT INTO student_sync_receipts VALUES (@id,@author,@request,@result,UTC_TIMESTAMP(6))", connection, transaction);
        receipt.Parameters.AddWithValue("@id", request.Id);
        receipt.Parameters.AddWithValue("@author", request.AuthorUid);
        receipt.Parameters.AddWithValue("@request", requestJson);
        receipt.Parameters.AddWithValue("@result", JsonSerializer.Serialize(result));
        await receipt.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
        return result;
    }

    private static bool VerifySyncQr(StudentSyncProfile profile)
    {
        var qr = new QrCredentialService();
        return qr.TryVerify(profile.QrCredential, out var payload, out _) && payload!.StudentId == profile.StudentId;
    }

    private static async Task WriteSyncProfileAsync(MySqlConnection connection, MySqlTransaction transaction,
        StudentSyncProfile profile, bool create, bool resetPin)
    {
        string sql = create ? @"INSERT INTO students (student_id,full_name,email,course,year_level,section_name,status,nfc_uid,
            qr_credential,pin_salt,pin_hash,photo_data,is_temporary,entry_state,failed_pin_attempts,pin_locked)
            VALUES (@student_id,@full_name,@email,@course,@year_level,@section_name,@status,@nfc_uid,
            @qr_credential,@pin_salt,@pin_hash,@photo_data,@is_temporary,'OUTSIDE',0,FALSE)" :
            @"UPDATE students SET full_name=@full_name,email=@email,course=@course,year_level=@year_level,section_name=@section_name,
            status=@status,nfc_uid=@nfc_uid,qr_credential=@qr_credential,photo_data=@photo_data,is_temporary=@is_temporary" +
            (resetPin ? ",pin_salt=@pin_salt,pin_hash=@pin_hash,pin_locked=FALSE,failed_pin_attempts=0" : "") + " WHERE student_id=@student_id";
        using var command = new MySqlCommand(sql, connection, transaction);
        AddStudentParameters(command, profile.ToStudent(), profile.PinSalt, profile.PinHash);
        await command.ExecuteNonQueryAsync();
    }

    internal async Task<int> DownloadSyncProfilesAsync(string serverId,
        IReadOnlyList<(StudentSyncProfile Profile, string Fingerprint)> profiles, bool discardLocalChanges)
    {
        long session = GraduationRules.RequireAdministrator(DatabaseMonitor.IsOnline);
        using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();
        await EnsureStudentSyncSchemaAsync(connection);
        using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable);
        foreach (var (profile, fingerprint) in profiles)
        {
            profile.Validate();
            if (profile.Fingerprint() != fingerprint) throw new InvalidOperationException("Invalid main-server profile fingerprint.");
            var local = await LockSyncProfileAsync(connection, transaction, profile.StudentId);
            using (var baseline = new MySqlCommand("SELECT local_fingerprint,request_id FROM student_sync_baselines WHERE server_id=@server AND student_id=@id FOR UPDATE", connection, transaction))
            {
                baseline.Parameters.AddWithValue("@server", serverId);
                baseline.Parameters.AddWithValue("@id", profile.StudentId);
                using var reader = await baseline.ExecuteReaderAsync();
                bool known = await reader.ReadAsync();
                bool dirty = local != null && (!known || local.Fingerprint() != reader.GetString(0));
                bool pending = known && reader.GetString(1).Length > 0;
                if (pending || dirty && !discardLocalChanges)
                    throw new InvalidOperationException($"{profile.StudentId} has local changes or an upload awaiting confirmation. Use Student Profile Sync in Settings to check the result or explicitly replace local profiles.");
            }
            await WriteSyncProfileAsync(connection, transaction, profile, local == null, true);
            var saved = await LockSyncProfileAsync(connection, transaction, profile.StudentId);
            await WriteSyncBaselineAsync(connection, transaction, serverId, profile.StudentId,
                new(fingerprint, saved!.Fingerprint(), saved.ToJson(), "", ""));
        }
        GraduationRules.RequireAdministrator(DatabaseMonitor.IsOnline, session);
        await transaction.CommitAsync();
        return profiles.Count;
    }
}
