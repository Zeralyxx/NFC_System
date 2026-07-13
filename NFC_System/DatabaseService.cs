using MySqlConnector;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NFC_System;

public sealed class DatabaseService
{
    public const string ConnectionString = "Server=127.0.0.1;Port=3306;Database=nfc_system;User ID=root;Password=;";

    public async Task EnsureSchemaAsync()
    {
        using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();

        await ExecuteAsync(connection, @"
            CREATE TABLE IF NOT EXISTS students (
                student_id VARCHAR(50) PRIMARY KEY,
                full_name VARCHAR(150) NOT NULL,
                course VARCHAR(100) NULL,
                year_level VARCHAR(20) NULL,
                section_name VARCHAR(50) NULL,
                status VARCHAR(30) NOT NULL DEFAULT 'Active',
                nfc_uid VARCHAR(80) NOT NULL,
                pin_salt VARCHAR(128) NULL,
                pin_hash VARCHAR(256) NULL,
                qr_credential VARCHAR(255) NULL,
                entry_state VARCHAR(20) NOT NULL DEFAULT 'OUTSIDE',
                failed_pin_attempts INT NOT NULL DEFAULT 0,
                pin_locked BOOLEAN NOT NULL DEFAULT FALSE,
                created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
            )");

        await EnsureColumnAsync(connection, "students", "pin_salt", "VARCHAR(128) NULL");
        await EnsureColumnAsync(connection, "students", "pin_hash", "VARCHAR(256) NULL");
        await EnsureColumnAsync(connection, "students", "qr_credential", "VARCHAR(255) NULL");
        await EnsureColumnAsync(connection, "students", "entry_state", "VARCHAR(20) NOT NULL DEFAULT 'OUTSIDE'");
        await EnsureColumnAsync(connection, "students", "failed_pin_attempts", "INT NOT NULL DEFAULT 0");
        await EnsureColumnAsync(connection, "students", "pin_locked", "BOOLEAN NOT NULL DEFAULT FALSE");

        await ExecuteAsync(connection, @"
            CREATE TABLE IF NOT EXISTS app_settings (
                setting_key VARCHAR(80) PRIMARY KEY,
                setting_value VARCHAR(255) NOT NULL
            )");

        await ExecuteAsync(connection, @"
            INSERT INTO app_settings (setting_key, setting_value)
            VALUES ('verification_mode', 'Standard')
            ON DUPLICATE KEY UPDATE setting_value = setting_value");

        await ExecuteAsync(connection, @"
            CREATE TABLE IF NOT EXISTS verification_logs (
                id INT AUTO_INCREMENT PRIMARY KEY,
                student_id VARCHAR(50) NULL,
                nfc_uid VARCHAR(80) NULL,
                transaction_type VARCHAR(40) NOT NULL,
                verification_mode VARCHAR(40) NOT NULL,
                access_result VARCHAR(40) NOT NULL,
                verification_status VARCHAR(60) NOT NULL,
                error_category VARCHAR(80) NULL,
                remarks TEXT NULL,
                created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
            )");

        await ExecuteAsync(connection, @"
            CREATE TABLE IF NOT EXISTS security_alerts (
                id INT AUTO_INCREMENT PRIMARY KEY,
                student_id VARCHAR(50) NULL,
                alert_type VARCHAR(80) NOT NULL,
                message TEXT NOT NULL,
                is_resolved BOOLEAN NOT NULL DEFAULT FALSE,
                created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
            )");

        await ExecuteAsync(connection, @"
            CREATE TABLE IF NOT EXISTS events (
                event_id VARCHAR(50) PRIMARY KEY,
                event_name VARCHAR(150) NOT NULL,
                verification_mode VARCHAR(40) NOT NULL DEFAULT 'Standard',
                is_restricted BOOLEAN NOT NULL DEFAULT FALSE,
                status VARCHAR(30) NOT NULL DEFAULT 'Active',
                event_date DATETIME NULL,
                created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
            )");

        await ExecuteAsync(connection, @"
            CREATE TABLE IF NOT EXISTS event_attendee_list (
                event_id VARCHAR(50) NOT NULL,
                student_id VARCHAR(50) NOT NULL,
                created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                PRIMARY KEY (event_id, student_id)
            )");

        await ExecuteAsync(connection, @"
            CREATE TABLE IF NOT EXISTS attendance (
                id INT AUTO_INCREMENT PRIMARY KEY,
                event_id VARCHAR(50) NOT NULL,
                student_id VARCHAR(50) NOT NULL,
                verification_mode VARCHAR(40) NOT NULL,
                status VARCHAR(40) NOT NULL,
                remarks TEXT NULL,
                created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
            )");
    }

    public async Task SaveStudentAsync(StudentRecord student, string pin)
    {
        await EnsureSchemaAsync();

        using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();

        if (await NfcUidBelongsToAnotherStudentAsync(connection, student.NfcUid, student.StudentId))
        {
            throw new InvalidOperationException("NFC UID is already linked to another student.");
        }

        var (salt, hash) = PinHasher.HashPin(pin);
        string existsSql = "SELECT COUNT(*) FROM students WHERE student_id = @student_id";
        using var existsCommand = new MySqlCommand(existsSql, connection);
        existsCommand.Parameters.AddWithValue("@student_id", student.StudentId);
        bool exists = Convert.ToInt32(await existsCommand.ExecuteScalarAsync()) > 0;

        string sql = exists
            ? @"
                UPDATE students
                SET full_name = @full_name,
                    course = @course,
                    year_level = @year_level,
                    section_name = @section_name,
                    status = @status,
                    nfc_uid = @nfc_uid,
                    pin_salt = @pin_salt,
                    pin_hash = @pin_hash,
                    qr_credential = @qr_credential,
                    pin_locked = FALSE,
                    failed_pin_attempts = 0
                WHERE student_id = @student_id"
            : @"
                INSERT INTO students
                (student_id, full_name, course, year_level, section_name, status, nfc_uid, pin_salt, pin_hash, qr_credential, entry_state, failed_pin_attempts, pin_locked)
                VALUES
                (@student_id, @full_name, @course, @year_level, @section_name, @status, @nfc_uid, @pin_salt, @pin_hash, @qr_credential, 'OUTSIDE', 0, FALSE)";

        using var command = new MySqlCommand(sql, connection);
        AddStudentParameters(command, student, salt, hash);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<StudentRecord?> GetStudentByUidAsync(string uid)
    {
        await EnsureSchemaAsync();

        using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();

        string sql = @"
            SELECT student_id, full_name, course, year_level, section_name, status, nfc_uid,
                   pin_salt, pin_hash, qr_credential, entry_state, failed_pin_attempts, pin_locked
            FROM students
            WHERE nfc_uid = @uid
            LIMIT 1";

        using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@uid", uid);

        using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        return ReadStudent(reader);
    }

    public async Task<string> GetSettingAsync(string key, string fallback)
    {
        await EnsureSchemaAsync();

        using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();

        using var command = new MySqlCommand("SELECT setting_value FROM app_settings WHERE setting_key = @key LIMIT 1", connection);
        command.Parameters.AddWithValue("@key", key);

        object? value = await command.ExecuteScalarAsync();
        return value?.ToString() ?? fallback;
    }

    public async Task SetSettingAsync(string key, string value)
    {
        await EnsureSchemaAsync();

        using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();

        using var command = new MySqlCommand(@"
            INSERT INTO app_settings (setting_key, setting_value)
            VALUES (@key, @value)
            ON DUPLICATE KEY UPDATE setting_value = @value", connection);
        command.Parameters.AddWithValue("@key", key);
        command.Parameters.AddWithValue("@value", value);
        await command.ExecuteNonQueryAsync();
    }

    public async Task UpdateEntryStateAsync(string studentId, string state)
    {
        await EnsureSchemaAsync();

        using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();

        using var command = new MySqlCommand("UPDATE students SET entry_state = @state WHERE student_id = @student_id", connection);
        command.Parameters.AddWithValue("@state", state);
        command.Parameters.AddWithValue("@student_id", studentId);
        await command.ExecuteNonQueryAsync();
    }

    public async Task UpdatePinFailureAsync(string studentId, int failedAttempts, bool locked)
    {
        await EnsureSchemaAsync();

        using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();

        using var command = new MySqlCommand(@"
            UPDATE students
            SET failed_pin_attempts = @attempts,
                pin_locked = @locked
            WHERE student_id = @student_id", connection);
        command.Parameters.AddWithValue("@attempts", failedAttempts);
        command.Parameters.AddWithValue("@locked", locked);
        command.Parameters.AddWithValue("@student_id", studentId);
        await command.ExecuteNonQueryAsync();
    }

    public async Task ResetPinAsync(string studentId, string pin)
    {
        await EnsureSchemaAsync();
        var (salt, hash) = PinHasher.HashPin(pin);

        using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();

        using var command = new MySqlCommand(@"
            UPDATE students
            SET pin_salt = @salt,
                pin_hash = @hash,
                failed_pin_attempts = 0,
                pin_locked = FALSE
            WHERE student_id = @student_id", connection);
        command.Parameters.AddWithValue("@salt", salt);
        command.Parameters.AddWithValue("@hash", hash);
        command.Parameters.AddWithValue("@student_id", studentId);
        await command.ExecuteNonQueryAsync();
    }

    public async Task SaveEventAsync(string eventId, string eventName, VerificationMode mode, bool isRestricted)
    {
        await EnsureSchemaAsync();

        using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();

        using var command = new MySqlCommand(@"
            INSERT INTO events (event_id, event_name, verification_mode, is_restricted, status)
            VALUES (@event_id, @event_name, @mode, @restricted, 'Active')
            ON DUPLICATE KEY UPDATE
                event_name = @event_name,
                verification_mode = @mode,
                is_restricted = @restricted,
                status = 'Active'", connection);
        command.Parameters.AddWithValue("@event_id", eventId);
        command.Parameters.AddWithValue("@event_name", eventName);
        command.Parameters.AddWithValue("@mode", ToStorageValue(mode));
        command.Parameters.AddWithValue("@restricted", isRestricted);
        await command.ExecuteNonQueryAsync();
    }

    public async Task AddEventAttendeeAsync(string eventId, string studentId)
    {
        await EnsureSchemaAsync();

        using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();

        using var command = new MySqlCommand(@"
            INSERT INTO event_attendee_list (event_id, student_id)
            VALUES (@event_id, @student_id)
            ON DUPLICATE KEY UPDATE student_id = student_id", connection);
        command.Parameters.AddWithValue("@event_id", eventId);
        command.Parameters.AddWithValue("@student_id", studentId);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<bool> IsStudentAllowedForEventAsync(string eventId, string studentId)
    {
        if (string.IsNullOrWhiteSpace(eventId))
        {
            return true;
        }

        await EnsureSchemaAsync();

        using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();

        using var eventCommand = new MySqlCommand("SELECT is_restricted FROM events WHERE event_id = @event_id LIMIT 1", connection);
        eventCommand.Parameters.AddWithValue("@event_id", eventId);
        object? restrictedValue = await eventCommand.ExecuteScalarAsync();
        if (restrictedValue == null)
        {
            return false;
        }

        bool isRestricted = Convert.ToBoolean(restrictedValue);
        if (!isRestricted)
        {
            return true;
        }

        using var attendeeCommand = new MySqlCommand(@"
            SELECT COUNT(*)
            FROM event_attendee_list
            WHERE event_id = @event_id AND student_id = @student_id", connection);
        attendeeCommand.Parameters.AddWithValue("@event_id", eventId);
        attendeeCommand.Parameters.AddWithValue("@student_id", studentId);
        return Convert.ToInt32(await attendeeCommand.ExecuteScalarAsync()) > 0;
    }

    public async Task RecordAttendanceAsync(string eventId, string studentId, VerificationMode mode, string status, string remarks)
    {
        if (string.IsNullOrWhiteSpace(eventId))
        {
            return;
        }

        await EnsureSchemaAsync();

        using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();

        using var command = new MySqlCommand(@"
            INSERT INTO attendance (event_id, student_id, verification_mode, status, remarks)
            VALUES (@event_id, @student_id, @mode, @status, @remarks)", connection);
        command.Parameters.AddWithValue("@event_id", eventId);
        command.Parameters.AddWithValue("@student_id", studentId);
        command.Parameters.AddWithValue("@mode", ToStorageValue(mode));
        command.Parameters.AddWithValue("@status", status);
        command.Parameters.AddWithValue("@remarks", remarks);
        await command.ExecuteNonQueryAsync();
    }

    public async Task LogVerificationAsync(StudentRecord? student, string uid, TransactionType transactionType, VerificationMode mode, bool granted, string status, string errorCategory, string remarks)
    {
        await EnsureSchemaAsync();

        using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();

        using var command = new MySqlCommand(@"
            INSERT INTO verification_logs
            (student_id, nfc_uid, transaction_type, verification_mode, access_result, verification_status, error_category, remarks)
            VALUES
            (@student_id, @nfc_uid, @transaction_type, @verification_mode, @access_result, @verification_status, @error_category, @remarks)", connection);
        command.Parameters.AddWithValue("@student_id", NullIfEmpty(student?.StudentId));
        command.Parameters.AddWithValue("@nfc_uid", NullIfEmpty(uid));
        command.Parameters.AddWithValue("@transaction_type", ToStorageValue(transactionType));
        command.Parameters.AddWithValue("@verification_mode", ToStorageValue(mode));
        command.Parameters.AddWithValue("@access_result", granted ? "GRANTED" : "DENIED");
        command.Parameters.AddWithValue("@verification_status", status);
        command.Parameters.AddWithValue("@error_category", NullIfEmpty(errorCategory));
        command.Parameters.AddWithValue("@remarks", NullIfEmpty(remarks));
        await command.ExecuteNonQueryAsync();
    }

    public async Task AddAlertAsync(string? studentId, string alertType, string message)
    {
        await EnsureSchemaAsync();

        using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();

        using var command = new MySqlCommand(@"
            INSERT INTO security_alerts (student_id, alert_type, message)
            VALUES (@student_id, @alert_type, @message)", connection);
        command.Parameters.AddWithValue("@student_id", NullIfEmpty(studentId));
        command.Parameters.AddWithValue("@alert_type", alertType);
        command.Parameters.AddWithValue("@message", message);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyList<string>> GetRecentLogsAsync(int limit = 25)
    {
        await EnsureSchemaAsync();

        using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();

        using var command = new MySqlCommand(@"
            SELECT created_at, student_id, nfc_uid, transaction_type, verification_mode, access_result, error_category, remarks
            FROM verification_logs
            ORDER BY created_at DESC
            LIMIT @limit", connection);
        command.Parameters.AddWithValue("@limit", limit);

        var logs = new List<string>();
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            string time = Convert.ToDateTime(reader["created_at"]).ToString("yyyy-MM-dd hh:mm:ss tt");
            string subject = Value(reader["student_id"]);
            if (string.IsNullOrWhiteSpace(subject))
            {
                subject = $"UID {Value(reader["nfc_uid"])}";
            }

            logs.Add($"{time} | {subject} | {Value(reader["transaction_type"])} | {Value(reader["verification_mode"])} | {Value(reader["access_result"])} | {Value(reader["error_category"])} {Value(reader["remarks"])}".Trim());
        }

        return logs;
    }

    public async Task<IReadOnlyList<string>> GetRecentAlertsAsync(int limit = 25)
    {
        await EnsureSchemaAsync();

        using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();

        using var command = new MySqlCommand(@"
            SELECT created_at, student_id, alert_type, message
            FROM security_alerts
            ORDER BY created_at DESC
            LIMIT @limit", connection);
        command.Parameters.AddWithValue("@limit", limit);

        var alerts = new List<string>();
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            string time = Convert.ToDateTime(reader["created_at"]).ToString("yyyy-MM-dd hh:mm:ss tt");
            alerts.Add($"{time} | {Value(reader["alert_type"])} | {Value(reader["student_id"])} | {Value(reader["message"])}");
        }

        return alerts;
    }

    private static async Task ExecuteAsync(MySqlConnection connection, string sql)
    {
        using var command = new MySqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task EnsureColumnAsync(MySqlConnection connection, string tableName, string columnName, string definition)
    {
        using var checkCommand = new MySqlCommand(@"
            SELECT COUNT(*)
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = @table
              AND COLUMN_NAME = @column", connection);
        checkCommand.Parameters.AddWithValue("@table", tableName);
        checkCommand.Parameters.AddWithValue("@column", columnName);

        bool exists = Convert.ToInt32(await checkCommand.ExecuteScalarAsync()) > 0;
        if (!exists)
        {
            await ExecuteAsync(connection, $"ALTER TABLE {tableName} ADD COLUMN {columnName} {definition}");
        }
    }

    private static async Task<bool> NfcUidBelongsToAnotherStudentAsync(MySqlConnection connection, string uid, string studentId)
    {
        using var command = new MySqlCommand(@"
            SELECT COUNT(*)
            FROM students
            WHERE nfc_uid = @uid AND student_id <> @student_id", connection);
        command.Parameters.AddWithValue("@uid", uid);
        command.Parameters.AddWithValue("@student_id", studentId);
        return Convert.ToInt32(await command.ExecuteScalarAsync()) > 0;
    }

    private static void AddStudentParameters(MySqlCommand command, StudentRecord student, string salt, string hash)
    {
        command.Parameters.AddWithValue("@student_id", student.StudentId);
        command.Parameters.AddWithValue("@full_name", student.FullName);
        command.Parameters.AddWithValue("@course", student.Course);
        command.Parameters.AddWithValue("@year_level", student.YearLevel);
        command.Parameters.AddWithValue("@section_name", student.SectionName);
        command.Parameters.AddWithValue("@status", student.Status);
        command.Parameters.AddWithValue("@nfc_uid", student.NfcUid);
        command.Parameters.AddWithValue("@pin_salt", salt);
        command.Parameters.AddWithValue("@pin_hash", hash);
        command.Parameters.AddWithValue("@qr_credential", student.QrCredential);
    }

    private static StudentRecord ReadStudent(MySqlDataReader reader)
    {
        return new StudentRecord
        {
            StudentId = Value(reader["student_id"]),
            FullName = Value(reader["full_name"]),
            Course = Value(reader["course"]),
            YearLevel = Value(reader["year_level"]),
            SectionName = Value(reader["section_name"]),
            Status = Value(reader["status"]),
            NfcUid = Value(reader["nfc_uid"]),
            PinSalt = Value(reader["pin_salt"]),
            PinHash = Value(reader["pin_hash"]),
            QrCredential = Value(reader["qr_credential"]),
            EntryState = string.IsNullOrWhiteSpace(Value(reader["entry_state"])) ? "OUTSIDE" : Value(reader["entry_state"]),
            FailedPinAttempts = int.TryParse(Value(reader["failed_pin_attempts"]), out int attempts) ? attempts : 0,
            PinLocked = bool.TryParse(Value(reader["pin_locked"]), out bool locked) && locked || Value(reader["pin_locked"]) == "1"
        };
    }

    private static object NullIfEmpty(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;
    }

    private static string Value(object? value)
    {
        return value == null || value == DBNull.Value ? "" : value.ToString() ?? "";
    }

    public static string ToStorageValue(VerificationMode mode)
    {
        return mode switch
        {
            VerificationMode.Fast => "Fast",
            VerificationMode.Standard => "Standard",
            VerificationMode.HighSecurity => "High-Security",
            _ => "Standard"
        };
    }

    public static string ToStorageValue(TransactionType transactionType)
    {
        return transactionType switch
        {
            TransactionType.Entry => "ENTRY",
            TransactionType.Exit => "EXIT",
            TransactionType.EventAttendance => "EVENT_ATTENDANCE",
            _ => "ENTRY"
        };
    }
}
