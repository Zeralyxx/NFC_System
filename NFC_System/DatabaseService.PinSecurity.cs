using MySqlConnector;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NFC_System;

public sealed partial class DatabaseService
{
    private static readonly SemaphoreSlim PinWorkLock = new(1, 1);

    internal static async Task EnsurePinSchemaAsync(MySqlConnection connection)
    {
        using var schema = new MySqlCommand(@"CREATE TABLE IF NOT EXISTS pin_security_versions (
            student_id VARCHAR(50) PRIMARY KEY, reset_token CHAR(32) NOT NULL, revision BIGINT NOT NULL DEFAULT 0
            ) ENGINE=InnoDB;
            CREATE TABLE IF NOT EXISTS pin_attempt_receipts (
            attempt_id CHAR(32) PRIMARY KEY, student_id VARCHAR(50) NOT NULL, result_json LONGTEXT NOT NULL
            ) ENGINE=InnoDB;", connection);
        await schema.ExecuteNonQueryAsync();
        using var engines = new MySqlCommand(@"SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE()
            AND table_name IN ('students','pin_security_versions','pin_attempt_receipts') AND engine<>'InnoDB'", connection);
        if (Convert.ToInt32(await engines.ExecuteScalarAsync()) != 0)
            throw new InvalidOperationException("PIN security requires InnoDB tables.");
    }

    internal static async Task<PinState> ReadPinStateAsync(MySqlConnection connection, MySqlTransaction transaction, string id, bool allowDeleted = false)
    {
        PinState state;
        using (var command = new MySqlCommand("SELECT pin_salt,pin_hash,failed_pin_attempts,pin_locked FROM students WHERE student_id=@id FOR UPDATE", connection, transaction))
        {
            command.Parameters.AddWithValue("@id", id);
            using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
            {
                if (!allowDeleted) throw new InvalidOperationException("Student no longer exists.");
                return new PinState { StudentId = id, ResetToken = Guid.NewGuid().ToString("N"), Locked = true, Attempts = 3 };
            }
            state = new PinState { StudentId = id, Attempts = reader["failed_pin_attempts"] == DBNull.Value ? 3 : Convert.ToInt32(reader["failed_pin_attempts"]),
                Locked = reader["pin_locked"] == DBNull.Value || Convert.ToBoolean(reader["pin_locked"]),
                CredentialTag = PinState.Tag(Convert.ToString(reader["pin_salt"]) ?? "", Convert.ToString(reader["pin_hash"]) ?? "") };
            state.Locked |= state.Attempts >= 3;
        }
        using (var seed = new MySqlCommand("INSERT IGNORE INTO pin_security_versions (student_id,reset_token) VALUES (@id,@token)", connection, transaction))
        {
            seed.Parameters.AddWithValue("@id", id);
            seed.Parameters.AddWithValue("@token", Guid.NewGuid().ToString("N"));
            await seed.ExecuteNonQueryAsync();
        }
        using var version = new MySqlCommand("SELECT reset_token,revision FROM pin_security_versions WHERE student_id=@id FOR UPDATE", connection, transaction);
        version.Parameters.AddWithValue("@id", id);
        using var row = await version.ExecuteReaderAsync();
        await row.ReadAsync();
        state.ResetToken = row.GetString(0);
        state.Revision = row.GetInt64(1);
        return state;
    }

    internal static async Task RotatePinResetAsync(MySqlConnection connection, MySqlTransaction transaction, string id)
    {
        using var command = new MySqlCommand(@"INSERT INTO pin_security_versions (student_id,reset_token,revision) VALUES (@id,@token,1)
            ON DUPLICATE KEY UPDATE reset_token=@token,revision=revision+1", connection, transaction);
        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@token", Guid.NewGuid().ToString("N"));
        await command.ExecuteNonQueryAsync();
    }

    internal static async Task<PinAttemptResult> ApplyPinAttemptAsync(MySqlConnection connection, PinAttempt attempt)
    {
        using var transaction = await connection.BeginTransactionAsync();
        var state = await ReadPinStateAsync(connection, transaction, attempt.StudentId, allowDeleted: true);
        // Student row locking serializes attempts, administrative resets, and receipt retries.
        using (var receipt = new MySqlCommand("SELECT result_json FROM pin_attempt_receipts WHERE attempt_id=@id", connection, transaction))
        {
            receipt.Parameters.AddWithValue("@id", attempt.Id);
            if (await receipt.ExecuteScalarAsync() is string json)
            {
                var prior = JsonSerializer.Deserialize<PinAttemptResult>(json)!;
                if (prior.State.StudentId != attempt.StudentId) throw new InvalidOperationException("PIN receipt identity mismatch.");
                await transaction.CommitAsync();
                return prior;
            }
        }
        var result = PinRules.Apply(state, attempt);
        using (var update = new MySqlCommand(@"UPDATE students SET failed_pin_attempts=@attempts,pin_locked=@locked WHERE student_id=@id;
            UPDATE pin_security_versions SET revision=@revision WHERE student_id=@id;
            INSERT INTO pin_attempt_receipts (attempt_id,student_id,result_json) VALUES (@operation,@id,@json)", connection, transaction))
        {
            update.Parameters.AddWithValue("@id", state.StudentId);
            update.Parameters.AddWithValue("@attempts", result.State.Attempts);
            update.Parameters.AddWithValue("@locked", result.State.Locked);
            update.Parameters.AddWithValue("@revision", result.State.Revision);
            update.Parameters.AddWithValue("@operation", attempt.Id);
            update.Parameters.AddWithValue("@json", JsonSerializer.Serialize(result));
            await update.ExecuteNonQueryAsync();
        }
        await transaction.CommitAsync();
        return result;
    }

    private static async Task FlushPinAttemptsAsync(MySqlConnection connection)
    {
        foreach (var attempt in OfflineCacheService.PinJournal.Snapshot().Pending)
        {
            await ApplyPinAttemptAsync(connection, attempt);
            // A receipt describes its original decision, which may predate a later administrative reset.
            using var transaction = await connection.BeginTransactionAsync();
            var current = await ReadPinStateAsync(connection, transaction, attempt.StudentId, allowDeleted: true);
            await transaction.CommitAsync();
            OfflineCacheService.PinJournal.Acknowledge(attempt.Id, current);
        }
    }

    public async Task<PinState> PreparePinStateAsync(StudentRecord student, bool offline)
    {
        await PinWorkLock.WaitAsync();
        try
        {
            PinState state;
            if (offline) state = OfflineCacheService.PinJournal.Read(student.StudentId);
            else
            {
                using var connection = new MySqlConnection(ConnectionString);
                await connection.OpenAsync();
                await EnsurePinSchemaAsync(connection);
                await FlushPinAttemptsAsync(connection);
                using var transaction = await connection.BeginTransactionAsync();
                state = await ReadPinStateAsync(connection, transaction, student.StudentId);
                await transaction.CommitAsync();
                OfflineCacheService.PinJournal.Confirm(new[] { state });
            }
            if (state.CredentialTag != PinState.Tag(student.PinSalt, student.PinHash))
                throw new InvalidOperationException("PIN credential changed. Refresh the profile and scan again.");
            return state;
        }
        finally { PinWorkLock.Release(); }
    }

    public async Task<PinAttemptResult> SubmitSecurePinAsync(VerificationSession session, string pin)
    {
        await PinWorkLock.WaitAsync();
        try
        {
            var student = session.Student;
            var expected = session.PinState ?? throw new InvalidOperationException("PIN session is missing security state.");
            using var connection = new MySqlConnection(ConnectionString);
            PinState current;
            if (session.IsOffline) current = OfflineCacheService.PinJournal.Read(student.StudentId);
            else
            {
                await connection.OpenAsync();
                await EnsurePinSchemaAsync(connection);
                await FlushPinAttemptsAsync(connection);
                using var transaction = await connection.BeginTransactionAsync();
                current = await ReadPinStateAsync(connection, transaction, student.StudentId);
                await transaction.CommitAsync();
                OfflineCacheService.PinJournal.Confirm(new[] { current });
            }
            if (expected.ResetToken != current.ResetToken || expected.CredentialTag != current.CredentialTag)
                return new PinAttemptResult { State = current, Error = "PIN_STATE_CHANGED" };
            if (current.Locked) return new PinAttemptResult { State = current, Error = "PIN_LOCKED" };
            var attempt = OfflineCacheService.PinJournal.Append(current, PinHasher.VerifyPin(pin, student.PinSalt, student.PinHash), session.IsOffline);
            var result = session.IsOffline ? PinRules.Apply(current, attempt) : await ApplyPinAttemptAsync(connection, attempt);
            if (!session.IsOffline) OfflineCacheService.PinJournal.Acknowledge(attempt.Id, result.State);
            return result;
        }
        finally { PinWorkLock.Release(); }
    }

    public async Task RefreshPinSecurityAsync()
    {
        await PinWorkLock.WaitAsync();
        try
        {
            using var connection = new MySqlConnection(ConnectionString);
            await connection.OpenAsync();
            await EnsurePinSchemaAsync(connection);
            await FlushPinAttemptsAsync(connection);
            using var seed = new MySqlCommand(@"INSERT IGNORE INTO pin_security_versions (student_id,reset_token)
                SELECT student_id,REPLACE(UUID(),'-','') FROM students", connection);
            await seed.ExecuteNonQueryAsync();
            using var command = new MySqlCommand(@"SELECT s.student_id,s.pin_salt,s.pin_hash,s.failed_pin_attempts,s.pin_locked,v.reset_token,v.revision
                FROM students s JOIN pin_security_versions v ON v.student_id=s.student_id", connection);
            using var reader = await command.ExecuteReaderAsync();
            var states = new List<PinState>();
            while (await reader.ReadAsync())
            {
                int attempts = reader.IsDBNull(3) ? 3 : Convert.ToInt32(reader[3]);
                states.Add(new PinState { StudentId = reader.GetString(0),
                    CredentialTag = PinState.Tag(Convert.ToString(reader[1]) ?? "", Convert.ToString(reader[2]) ?? ""),
                    Attempts = attempts, Locked = attempts >= 3 || reader.IsDBNull(4) || Convert.ToBoolean(reader[4]),
                    ResetToken = reader.GetString(5), Revision = reader.GetInt64(6) });
            }
            OfflineCacheService.PinJournal.Confirm(states);
        }
        finally { PinWorkLock.Release(); }
    }

    public async Task RecoverPinAttemptsAsync()
    {
        await PinWorkLock.WaitAsync();
        try
        {
            if (OfflineCacheService.PinJournal.Snapshot().Pending.Count == 0) return;
            using var connection = new MySqlConnection(ConnectionString);
            await connection.OpenAsync();
            await EnsurePinSchemaAsync(connection);
            await FlushPinAttemptsAsync(connection);
        }
        finally { PinWorkLock.Release(); }
    }

    public Task UnlockAccountAsync(string studentId) => ResetPinSecurityAsync(studentId, null);
    public Task ResetPinAsync(string studentId, string pin) => ResetPinSecurityAsync(studentId, pin);
    private static long RequirePinResetPermission(bool changingPin, long? expected = null)
    {
        long version = AppSession.LoginVersion;
        bool admin = AppSession.CanIssueQrCredentials;
        bool operatorAllowed = AppSession.IsLoggedIn && !AppSession.IsAdmin &&
            ((AppSession.CurrentStaffRoleLabel == "Personnel" && !AppSession.IsEventOrganizer) ||
             (AppSession.CurrentStaffRoleLabel == "Event Organizer" && AppSession.IsEventOrganizer));
        if (!DatabaseMonitor.IsOnline || (!admin && (changingPin || !operatorAllowed)) || (expected.HasValue && expected != version))
            throw new UnauthorizedAccessException("A current authorized online staff session is required to reset PIN security.");
        return version;
    }
    private async Task ResetPinSecurityAsync(string id, string? pin)
    {
        long sessionVersion = RequirePinResetPermission(pin != null);
        if (pin != null && (pin.Length != 4 || !System.Linq.Enumerable.All(pin, char.IsDigit))) throw new ArgumentException("PIN must be four digits.");
        using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();
        await EnsurePinSchemaAsync(connection);
        using var transaction = await connection.BeginTransactionAsync();
        await ReadPinStateAsync(connection, transaction, id);
        var hashed = pin == null ? default : PinHasher.HashPin(pin);
        using var update = new MySqlCommand("UPDATE students SET pin_locked=FALSE,failed_pin_attempts=0" +
            (pin == null ? "" : ",pin_salt=@salt,pin_hash=@hash") + " WHERE student_id=@id", connection, transaction);
        update.Parameters.AddWithValue("@id", id);
        if (pin != null) { update.Parameters.AddWithValue("@salt", hashed.Salt); update.Parameters.AddWithValue("@hash", hashed.Hash); }
        await update.ExecuteNonQueryAsync();
        await RotatePinResetAsync(connection, transaction, id);
        RequirePinResetPermission(pin != null, sessionVersion);
        await transaction.CommitAsync();
    }
}
