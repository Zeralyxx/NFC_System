using MySqlConnector;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NFC_System;

public sealed class VerificationLogRecord
{
    public string Timestamp { get; set; } = "";
    public string StudentId { get; set; } = "";
    public string FullName { get; set; } = "";
    public string Course { get; set; } = "";
    public string Section { get; set; } = "";
    public string Action { get; set; } = "";
    public string Status { get; set; } = "";
    public string Mode { get; set; } = "";

    // NEW: Bypasses the XAML Converter bug by assigning the color directly in the data model!
    public Microsoft.UI.Xaml.Media.Brush StatusColor =>
        Status == "GRANTED"
            ? new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 52, 211, 153)) // Green
            : new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 248, 113, 113)); // Red
}

public sealed class SystemAuditLog
{
    public DateTime Timestamp { get; set; }
    public string DisplayTime { get; set; } = "";
    public string LogType { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Action { get; set; } = "";
    public string Status { get; set; } = "";
    public string Details { get; set; } = "";
    public Microsoft.UI.Xaml.Media.Brush StatusColor { get; set; } = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray);
}

public sealed class AttendanceLog
{
    public string Timestamp { get; set; } = "";
    public string StudentId { get; set; } = "";
    public string FullName { get; set; } = "";
    public string Course { get; set; } = "";
    public string Section { get; set; } = ""; // NEW
    public string Mode { get; set; } = "";

    public string Status { get; set; } = ""; // NEW: Catches "PRESENT" or "DEPARTED"
}

public sealed class DatabaseService
{
    public const string ConnectionString = "Server=127.0.0.1;Port=3306;Database=nfc_system;User ID=root;Password=;";

    public Task EnsureSchemaAsync()
    {
        // Table structures are safely maintained inside phpMyAdmin to prevent runtime structural lag.
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<SystemAuditLog>> GetMasterAuditLogsAsync(int limit = 1000)
    {
        var masterLogs = new List<SystemAuditLog>();
        using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();

        // 1. Get Verification Logs
        using (var cmd1 = new MySqlCommand("SELECT timestamp, student_id, nfc_uid, transaction_type, is_granted, error_code, remarks FROM verification_logs ORDER BY timestamp DESC LIMIT @limit", connection))
        {
            cmd1.Parameters.AddWithValue("@limit", limit);
            using var reader = await cmd1.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                bool isGranted = reader["is_granted"].ToString() == "1" || reader["is_granted"].ToString()?.ToLower() == "true";
                string status = isGranted ? "GRANTED" : "DENIED";
                string type = Value(reader["transaction_type"]);
                string error = Value(reader["error_code"]);
                string details = Value(reader["remarks"]);

                // Format detailed breakdown for failures
                if (!string.IsNullOrEmpty(error) && error != "VERIFIED" && error != "BAD_READ")
                    details = $"[{error}] {details}";

                string subject = Value(reader["student_id"]);
                if (string.IsNullOrWhiteSpace(subject)) subject = $"UID: {Value(reader["nfc_uid"])}";

                masterLogs.Add(new SystemAuditLog
                {
                    Timestamp = Convert.ToDateTime(reader["timestamp"]),
                    LogType = "GATE LOG",
                    Subject = subject,
                    Action = type,
                    Status = status,
                    Details = details
                });
            }
        }

        // 2. Get Security & System Alerts
        using (var cmd2 = new MySqlCommand("SELECT timestamp, student_id, alert_type, message FROM alerts ORDER BY timestamp DESC LIMIT @limit", connection))
        {
            cmd2.Parameters.AddWithValue("@limit", limit);
            using var reader = await cmd2.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                string alertType = Value(reader["alert_type"]);
                string status = alertType.Contains("OVERRIDE") ? "RESOLVED" : "FLAGGED";

                masterLogs.Add(new SystemAuditLog
                {
                    Timestamp = Convert.ToDateTime(reader["timestamp"]),
                    LogType = alertType == "ADMIN_OVERRIDE" ? "ADMIN ACTION" : "SECURITY ALERT",
                    Subject = Value(reader["student_id"]),
                    Action = alertType,
                    Status = status,
                    Details = Value(reader["message"])
                });
            }
        }

        // 3. Sort completely by Timestamp and dynamically apply UI colors
        var sorted = masterLogs.OrderByDescending(l => l.Timestamp).Take(limit).ToList();
        foreach (var log in sorted)
        {
            log.DisplayTime = log.Timestamp.ToString("MMM dd, yyyy - hh:mm:ss tt");

            if (log.Status == "GRANTED" || log.Status == "RESOLVED")
                log.StatusColor = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 52, 211, 153)); // Green
            else if (log.Status == "DENIED" || log.Status == "FLAGGED")
                log.StatusColor = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 248, 113, 113)); // Red
            else
                log.StatusColor = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 160, 160, 160)); // Gray
        }

        return sorted;
    }

    public async Task<(int TotalScansToday, int CurrentlyInside, int DeniedToday)> GetUniversityMetricsAsync()
    {
        using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();

        int totalScans = 0;
        int inside = 0;
        int denied = 0;

        using (var cmd = new MySqlCommand("SELECT COUNT(*) FROM verification_logs WHERE DATE(timestamp) = CURDATE()", connection))
            totalScans = Convert.ToInt32(await cmd.ExecuteScalarAsync());

        using (var cmd = new MySqlCommand("SELECT COUNT(*) FROM students WHERE entry_state = 'INSIDE'", connection))
            inside = Convert.ToInt32(await cmd.ExecuteScalarAsync());

        using (var cmd = new MySqlCommand("SELECT COUNT(*) FROM verification_logs WHERE is_granted = 0 AND DATE(timestamp) = CURDATE()", connection))
            denied = Convert.ToInt32(await cmd.ExecuteScalarAsync());

        return (totalScans, inside, denied);
    }

    public async Task<IReadOnlyList<StatItem>> GetDailySecurityAlertsAsync()
    {
        using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();

        // Gets the total blocked/denied entries for every day in system history
        using var command = new MySqlCommand(@"
            SELECT DATE_FORMAT(timestamp, '%b %d, %Y') as DateLbl, COUNT(*) as Total 
            FROM verification_logs 
            WHERE is_granted = 0 
            GROUP BY DATE(timestamp), DateLbl 
            ORDER BY DATE(timestamp) DESC", connection);

        var list = new List<StatItem>();
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new StatItem
            {
                Label = Value(reader["DateLbl"]),
                Value = Value(reader["Total"]) + " Blocks"
            });
        }
        return list;
    }
    public async Task<(string HighDayLabel, int HighCount, string LowDayLabel, int LowCount)> GetSecurityAlertExtremesAsync()
    {
        using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();

        // Fetches daily denied counts for the last 30 days, ordered highest to lowest
        using var command = new MySqlCommand(@"
            SELECT DATE_FORMAT(timestamp, '%b %d, %Y') as DateLbl, COUNT(*) as Total 
            FROM verification_logs 
            WHERE is_granted = 0 AND timestamp >= DATE_SUB(CURDATE(), INTERVAL 30 DAY)
            GROUP BY DATE(timestamp), DateLbl
            ORDER BY Total DESC", connection);

        var list = new List<(string DateLbl, int Count)>();
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add((Value(reader["DateLbl"]), Convert.ToInt32(reader["Total"])));
        }

        // Return placeholders if there are no logs at all
        if (list.Count == 0) return ("No Data", 0, "No Data", 0);

        var high = list.First(); // Highest count
        var low = list.Last();   // Lowest count

        return (high.DateLbl, high.Count, low.DateLbl, low.Count);
    }

    public async Task<IReadOnlyList<StatItem>> GetDailyEntryStatsAsync()
    {
        using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();

        // Gets the total successful entries per day for the last 7 active days
        using var command = new MySqlCommand(@"
            SELECT DATE_FORMAT(timestamp, '%b %d, %Y') as DateLbl, COUNT(*) as Total 
            FROM verification_logs 
            WHERE transaction_type = 'Entry' AND is_granted = 1 
            GROUP BY DATE(timestamp), DateLbl 
            ORDER BY DATE(timestamp) DESC 
            LIMIT 7", connection);

        var list = new List<StatItem>();
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new StatItem
            {
                Label = Value(reader["DateLbl"]),
                Value = Value(reader["Total"]) + " Entries"
            });
        }
        return list;
    }

    public async Task<IReadOnlyList<VerificationLogRecord>> GetGeneralLedgerAsync()
    {
        using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();

        using var command = new MySqlCommand(@"
            SELECT vl.timestamp, vl.student_id, s.full_name, s.course, s.section_name, 
                   vl.transaction_type, vl.is_granted, vl.verification_mode
            FROM verification_logs vl
            LEFT JOIN students s ON vl.student_id = s.student_id
            ORDER BY vl.timestamp DESC
            LIMIT 500", connection);

        var list = new List<VerificationLogRecord>();
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            bool isGranted = reader["is_granted"].ToString() == "1" || reader["is_granted"].ToString()?.ToLower() == "true";

            list.Add(new VerificationLogRecord
            {
                Timestamp = reader["timestamp"] != DBNull.Value ? Convert.ToDateTime(reader["timestamp"]).ToString("MMM dd - hh:mm tt") : "",
                StudentId = Value(reader["student_id"]),
                FullName = string.IsNullOrWhiteSpace(Value(reader["full_name"])) ? "Unknown / Unregistered" : Value(reader["full_name"]),
                Course = Value(reader["course"]),
                Section = Value(reader["section_name"]),
                Action = Value(reader["transaction_type"]),
                Status = isGranted ? "GRANTED" : "DENIED",
                Mode = Value(reader["verification_mode"])
            });
        }
        return list;
    }

    /* =========================================================================
     * ENROLLMENT & ACCOUNT MANAGEMENT OPERATIONS
     * ========================================================================= */

    public async Task SaveStudentAsync(StudentRecord student, string? pin)
    {
        using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();

        if (await NfcUidBelongsToAnotherStudentAsync(connection, student.NfcUid, student.StudentId))
            throw new InvalidOperationException("NFC UID is already linked to another student.");

        string? salt = null;
        string? hash = null;

        // Only generate a new secure hash if the guard actually typed a new PIN
        if (!string.IsNullOrWhiteSpace(pin))
        {
            var hashedResult = PinHasher.HashPin(pin);
            salt = hashedResult.Salt;
            hash = hashedResult.Hash;
        }

        // 1. Check if the student already exists in the database
        bool exists = false;
        using (var checkCmd = new MySqlCommand("SELECT COUNT(*) FROM students WHERE student_id = @id", connection))
        {
            checkCmd.Parameters.AddWithValue("@id", student.StudentId);
            exists = Convert.ToInt32(await checkCmd.ExecuteScalarAsync()) > 0;
        }

        string sql;

        // 2. Dynamically build the correct SQL query
        if (exists)
        {
            // If they exist AND provided a new PIN, update everything including the PIN
            if (salt != null && hash != null)
            {
                sql = @"
                UPDATE students 
                SET full_name = @full_name, course = @course, year_level = @year_level, 
                    section_name = @section_name, status = @status, nfc_uid = @nfc_uid, 
                    qr_credential = @qr_credential, pin_salt = @pin_salt, pin_hash = @pin_hash, 
                    pin_locked = FALSE, failed_pin_attempts = 0
                WHERE student_id = @student_id;";
            }
            else
            {
                // If they left the PIN blank, update everything EXCEPT the PIN
                sql = @"
                UPDATE students 
                SET full_name = @full_name, course = @course, year_level = @year_level, 
                    section_name = @section_name, status = @status, nfc_uid = @nfc_uid, 
                    qr_credential = @qr_credential
                WHERE student_id = @student_id;";
            }
        }
        else
        {
            // If it's a brand new student, run a standard INSERT
            sql = @"
            INSERT INTO students
            (student_id, full_name, course, year_level, section_name, status, nfc_uid, pin_salt, pin_hash, qr_credential, entry_state, failed_pin_attempts, pin_locked)
            VALUES
            (@student_id, @full_name, @course, @year_level, @section_name, @status, @nfc_uid, @pin_salt, @pin_hash, @qr_credential, 'OUTSIDE', 0, FALSE);";
        }

        using var command = new MySqlCommand(sql, connection);
        AddStudentParameters(command, student, salt, hash);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<StudentRecord?> GetStudentByUidAsync(string uid)
    {
        using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();

        string sql = @"
            SELECT student_id, full_name, course, year_level, section_name, status, nfc_uid,
                   pin_salt, pin_hash, qr_credential, entry_state, failed_pin_attempts, pin_locked, last_scan_timestamp
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

    public async Task<StudentRecord?> GetStudentByIdAsync(string studentId)
    {
        using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();

        string sql = @"
            SELECT student_id, full_name, course, year_level, section_name, status, nfc_uid,
                   pin_salt, pin_hash, qr_credential, entry_state, failed_pin_attempts, pin_locked, last_scan_timestamp
            FROM students
            WHERE student_id = @id
            LIMIT 1";

        using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@id", studentId);

        using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        return ReadStudent(reader);
    }

    /* =========================================================================
     * STUDENT DIRECTORY: SEARCH, FILTERING & PAGING
     * ========================================================================= */

    public async Task<(IReadOnlyList<StudentRecord> Students, int TotalCount)> SearchStudentsAsync(
        string? searchTerm,
        string? statusFilter,
        string? courseFilter,
        string? yearFilter,
        int pageNumber,
        int pageSize)
    {
        using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();

        var whereClauses = new List<string>();
        var parameterValues = new List<(string Name, object Value)>();

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            whereClauses.Add("(full_name LIKE @search OR student_id LIKE @search OR nfc_uid LIKE @search)");
            parameterValues.Add(("@search", $"%{searchTerm.Trim()}%"));
        }

        if (!string.IsNullOrWhiteSpace(statusFilter) && statusFilter != "All Students")
        {
            if (statusFilter == "Locked Out")
            {
                whereClauses.Add("pin_locked = 1");
            }
            else if (statusFilter == "Active Only")
            {
                whereClauses.Add("status = @status");
                parameterValues.Add(("@status", "Active"));
            }
            else
            {
                whereClauses.Add("status = @status");
                parameterValues.Add(("@status", statusFilter));
            }
        }

        if (!string.IsNullOrWhiteSpace(courseFilter) && courseFilter != "All Courses")
        {
            whereClauses.Add("course = @course");
            parameterValues.Add(("@course", courseFilter));
        }

        if (!string.IsNullOrWhiteSpace(yearFilter) && yearFilter != "All Years")
        {
            if (yearFilter == "Year 5+")
            {
                whereClauses.Add("year_level >= 5");
            }
            else
            {
                string yearNumber = yearFilter.Replace("Year ", "").Trim();
                whereClauses.Add("year_level = @year");
                parameterValues.Add(("@year", yearNumber));
            }
        }

        string whereSql = whereClauses.Count > 0 ? "WHERE " + string.Join(" AND ", whereClauses) : "";

        int totalCount;
        using (var countCommand = new MySqlCommand($"SELECT COUNT(*) FROM students {whereSql}", connection))
        {
            foreach (var (name, value) in parameterValues)
            {
                countCommand.Parameters.AddWithValue(name, value);
            }
            totalCount = Convert.ToInt32(await countCommand.ExecuteScalarAsync());
        }

        int safePageSize = pageSize <= 0 ? 12 : pageSize;
        int safePageNumber = pageNumber <= 0 ? 1 : pageNumber;
        int offset = (safePageNumber - 1) * safePageSize;

        string sql = $@"
            SELECT student_id, full_name, course, year_level, section_name, status, nfc_uid,
                   pin_salt, pin_hash, qr_credential, entry_state, failed_pin_attempts, pin_locked, last_scan_timestamp
            FROM students
            {whereSql}
            ORDER BY full_name ASC
            LIMIT @pageSize OFFSET @offset";

        using var command = new MySqlCommand(sql, connection);
        foreach (var (name, value) in parameterValues)
        {
            command.Parameters.AddWithValue(name, value);
        }
        command.Parameters.AddWithValue("@pageSize", safePageSize);
        command.Parameters.AddWithValue("@offset", offset);

        var students = new List<StudentRecord>();
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            students.Add(ReadStudent(reader));
        }

        return (students, totalCount);
    }

    public async Task<IReadOnlyList<string>> GetDistinctCoursesAsync()
    {
        using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();

        // Pulls the official list of courses you created in the Dashboard
        using var command = new MySqlCommand("SELECT course_name FROM courses ORDER BY course_name ASC", connection);

        var courses = new List<string>();
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            courses.Add(Value(reader["course_name"]));
        }
        return courses;
    }
    public async Task AddCourseAsync(string courseName)
    {
        using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();

        // INSERT IGNORE prevents a crash if you try to add the exact same course twice
        using var command = new MySqlCommand("INSERT IGNORE INTO courses (course_name) VALUES (@name)", connection);
        command.Parameters.AddWithValue("@name", courseName.Trim());
        await command.ExecuteNonQueryAsync();
    }

    public async Task UpdateStudentStatusAsync(string studentId, string status)
    {
        using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();

        using var command = new MySqlCommand(
            "UPDATE students SET status = @status WHERE student_id = @student_id", connection);
        command.Parameters.AddWithValue("@status", status);
        command.Parameters.AddWithValue("@student_id", studentId);
        await command.ExecuteNonQueryAsync();
    }

    public async Task UnlockAccountAsync(string studentId)
    {
        using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();

        using var command = new MySqlCommand(@"
            UPDATE students
            SET pin_locked = FALSE,
                failed_pin_attempts = 0
            WHERE student_id = @student_id", connection);
        command.Parameters.AddWithValue("@student_id", studentId);
        await command.ExecuteNonQueryAsync();
    }

    /* =========================================================================
     * AUTOMATED GATE & TRANSITION STATE OPERATIONS
     * ========================================================================= */

    public async Task UpdateEntryStateAsync(string studentId, string state)
    {
        using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();

        using var command = new MySqlCommand("UPDATE students SET entry_state = @state WHERE student_id = @student_id", connection);
        command.Parameters.AddWithValue("@state", state);
        command.Parameters.AddWithValue("@student_id", studentId);
        await command.ExecuteNonQueryAsync();
    }

    public async Task UpdateLastScanTimestampAsync(string studentId, DateTime timestamp)
    {
        using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();

        using var command = new MySqlCommand("UPDATE students SET last_scan_timestamp = @time WHERE student_id = @student_id", connection);
        command.Parameters.AddWithValue("@time", timestamp);
        command.Parameters.AddWithValue("@student_id", studentId);
        await command.ExecuteNonQueryAsync();
    }

    public async Task UpdatePinFailureAsync(string studentId, int failedAttempts, bool locked)
    {
        using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();

        using var command = new MySqlCommand(@"
            UPDATE students
            SET failed_pin_attempts = @attempts,
                pin_locked = @locked
            WHERE student_id = @student_id", connection);
        command.Parameters.AddWithValue("@attempts", failedAttempts);
        command.Parameters.AddWithValue("@locked", locked ? 1 : 0);
        command.Parameters.AddWithValue("@student_id", studentId);
        await command.ExecuteNonQueryAsync();
    }

    public async Task ResetPinAsync(string studentId, string pin)
    {
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

    /* =========================================================================
     * LIVE DASHBOARD LOGGING & SYSTEM AUDITING
     * ========================================================================= */

    public async Task<string> GetSettingAsync(string key, string fallback)
    {
        using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();

        using var command = new MySqlCommand("SELECT setting_value FROM app_settings WHERE setting_key = @key LIMIT 1", connection);
        command.Parameters.AddWithValue("@key", key);

        object? value = await command.ExecuteScalarAsync();
        return value?.ToString() ?? fallback;
    }

    public async Task SetSettingAsync(string key, string value)
    {
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

    public async Task<IReadOnlyList<string>> GetRecentLogsAsync(int limit = 25)
    {
        using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();

        using var command = new MySqlCommand(@"
            SELECT timestamp, student_id, nfc_uid, transaction_type, verification_mode, is_granted, error_code, remarks
            FROM verification_logs
            ORDER BY timestamp DESC
            LIMIT @limit", connection);
        command.Parameters.AddWithValue("@limit", limit);

        var logs = new List<string>();
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            string time = Convert.ToDateTime(reader["timestamp"]).ToString("yyyy-MM-dd hh:mm:ss tt");
            string subject = Value(reader["student_id"]);
            if (string.IsNullOrWhiteSpace(subject))
            {
                subject = $"UID {Value(reader["nfc_uid"])}";
            }
            string result = reader["is_granted"].ToString() == "1" || reader["is_granted"].ToString()?.ToLower() == "true" ? "GRANTED" : "DENIED";

            logs.Add($"{time} | {subject} | {Value(reader["transaction_type"])} | {Value(reader["verification_mode"])} | {result} | {Value(reader["error_code"])} {Value(reader["remarks"])}".Trim());
        }
        return logs;
    }

    public async Task<IReadOnlyList<string>> GetRecentAlertsAsync(int limit = 25)
    {
        using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();

        using var command = new MySqlCommand(@"
            SELECT timestamp, student_id, alert_type, message
            FROM alerts
            ORDER BY timestamp DESC
            LIMIT @limit", connection);
        command.Parameters.AddWithValue("@limit", limit);

        var alerts = new List<string>();
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            string time = Convert.ToDateTime(reader["timestamp"]).ToString("yyyy-MM-dd hh:mm:ss tt");
            alerts.Add($"{time} | {Value(reader["alert_type"])} | {Value(reader["student_id"])} | {Value(reader["message"])}");
        }
        return alerts;
    }

    public async Task LogVerificationAsync(StudentRecord? student, string uid, TransactionType transactionType, VerificationMode mode, bool granted, string status, string errorCategory, string remarks)
    {
        using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();

        using var command = new MySqlCommand(@"
            INSERT INTO verification_logs
            (student_id, nfc_uid, transaction_type, verification_mode, is_granted, error_code, error_message, remarks)
            VALUES
            (@student_id, @nfc_uid, @transaction_type, @verification_mode, @is_granted, @error_category, @status, @remarks)", connection);
        command.Parameters.AddWithValue("@student_id", NullIfEmpty(student?.StudentId));
        command.Parameters.AddWithValue("@nfc_uid", NullIfEmpty(uid));
        command.Parameters.AddWithValue("@transaction_type", ToStorageValue(transactionType));
        command.Parameters.AddWithValue("@verification_mode", ToStorageValue(mode));
        command.Parameters.AddWithValue("@is_granted", granted ? 1 : 0);
        command.Parameters.AddWithValue("@error_category", NullIfEmpty(errorCategory));
        command.Parameters.AddWithValue("@status", status);
        command.Parameters.AddWithValue("@remarks", NullIfEmpty(remarks));
        await command.ExecuteNonQueryAsync();
    }

    public async Task AddAlertAsync(string? studentId, string alertType, string message)
    {
        using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();

        using var command = new MySqlCommand(@"
            INSERT INTO alerts (student_id, alert_type, message)
            VALUES (@student_id, @alert_type, @message)", connection);
        command.Parameters.AddWithValue("@student_id", NullIfEmpty(studentId));
        command.Parameters.AddWithValue("@alert_type", alertType);
        command.Parameters.AddWithValue("@message", message);
        await command.ExecuteNonQueryAsync();
    }

    /* =========================================================================
     * ACADEMIC TRACKS & RESTRICTED EVENT CHECKPOINTS
     * ========================================================================= */

    public async Task SaveEventAsync(string eventId, string eventName, VerificationMode mode, bool isRestricted)
    {
        using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();

        // UPDATED: Now inserts is_active = TRUE, and reactivates it if updated
        using var command = new MySqlCommand(@"
            INSERT INTO events (event_id, event_name, event_date, verification_mode, is_restricted, is_active)
            VALUES (@event_id, @event_name, NOW(), @mode, @is_restricted, TRUE)
            ON DUPLICATE KEY UPDATE event_name = @event_name, verification_mode = @mode, is_restricted = @is_restricted, event_date = NOW(), is_active = TRUE", connection);
        command.Parameters.AddWithValue("@event_id", eventId);
        command.Parameters.AddWithValue("@event_name", eventName);
        command.Parameters.AddWithValue("@mode", ToStorageValue(mode));
        command.Parameters.AddWithValue("@is_restricted", isRestricted ? 1 : 0);
        await command.ExecuteNonQueryAsync();
    }



    public async Task RemoveEventAttendeeAsync(string eventId, string studentId)
    {
        using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();

        using var command = new MySqlCommand("DELETE FROM event_approved_students WHERE event_id = @event_id AND student_id = @student_id", connection);
        command.Parameters.AddWithValue("@event_id", eventId);
        command.Parameters.AddWithValue("@student_id", studentId);
        await command.ExecuteNonQueryAsync();
    }

    public async Task AddBatchToEventAsync(string eventId, string? courseName, string? yearLevel)
    {
        using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();

        // Dynamically build the WHERE clause based on what the user selected
        var whereClauses = new List<string>();
        if (!string.IsNullOrWhiteSpace(courseName)) whereClauses.Add("course = @course");
        if (!string.IsNullOrWhiteSpace(yearLevel)) whereClauses.Add("year_level = @year");

        string whereSql = whereClauses.Count > 0 ? "WHERE " + string.Join(" AND ", whereClauses) : "";

        string sql = $@"
        INSERT IGNORE INTO event_approved_students (event_id, student_id)
        SELECT @event_id, student_id FROM students {whereSql}";

        using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@event_id", eventId);

        if (!string.IsNullOrWhiteSpace(courseName))
            command.Parameters.AddWithValue("@course", courseName);

        if (!string.IsNullOrWhiteSpace(yearLevel))
            command.Parameters.AddWithValue("@year", yearLevel.Replace("Year ", "").Trim()); // Handles "Year 1" -> "1"

        await command.ExecuteNonQueryAsync();
    }

    public async Task AddEventAttendeeAsync(string eventId, string studentId)
    {
        using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();

        // FIX: Changed to INSERT IGNORE for consistency
        using var command = new MySqlCommand(@"
            INSERT IGNORE INTO event_approved_students (event_id, student_id)
            VALUES (@event_id, @student_id)", connection);
        command.Parameters.AddWithValue("@event_id", eventId);
        command.Parameters.AddWithValue("@student_id", studentId);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyList<StudentRecord>> GetEventAttendeesAsync(string eventId)
    {
        using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();

        // FIX: Switched to a LEFT JOIN and selected 'eas.student_id' directly. 
        // Now, even if a student isn't formally registered in the main system yet, 
        // their ID will still show up on the Event list!
        using var command = new MySqlCommand(@"
            SELECT eas.student_id, s.full_name, s.course, s.year_level, s.section_name, s.status, s.nfc_uid, 
                   s.pin_salt, s.pin_hash, s.qr_credential, s.entry_state, s.failed_pin_attempts, s.pin_locked, s.last_scan_timestamp
            FROM event_approved_students eas
            LEFT JOIN students s ON eas.student_id = s.student_id
            WHERE eas.event_id = @event_id
            ORDER BY s.full_name ASC", connection);
        command.Parameters.AddWithValue("@event_id", eventId);

        var attendees = new List<StudentRecord>();
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var student = ReadStudent(reader);

            // If the student ID was added but they have no name in the database yet, 
            // give them a placeholder so the UI doesn't look blank.
            if (string.IsNullOrWhiteSpace(student.FullName))
            {
                student.FullName = "Unregistered Student";
            }

            attendees.Add(student);
        }
        return attendees;
    }



    public async Task<IReadOnlyList<EventRecord>> GetActiveEventsAsync(int limit = 50)
    {
        using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();

        // UPDATED: Added WHERE is_active = TRUE so closed events disappear from the management window
        using var command = new MySqlCommand(@"
            SELECT event_id, event_name, event_date, verification_mode, is_restricted
            FROM events
            WHERE is_active = TRUE
            LIMIT @limit", connection);
        command.Parameters.AddWithValue("@limit", limit);

        var events = new List<EventRecord>();
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            events.Add(new EventRecord
            {
                EventId = Value(reader["event_id"]),
                EventName = Value(reader["event_name"]),
                VerificationMode = Enum.TryParse<VerificationMode>(Value(reader["verification_mode"]), out var vMode) ? vMode : VerificationMode.Standard,
                IsRestricted = reader["is_restricted"] != DBNull.Value && Convert.ToBoolean(reader["is_restricted"]),
                Status = "Active",
                EventDate = reader["event_date"] != DBNull.Value ? Convert.ToDateTime(reader["event_date"]) : null
            });
        }
        return events;
    }

    public async Task<bool> IsStudentAllowedForEventAsync(string eventId, string studentId)
    {
        if (string.IsNullOrWhiteSpace(eventId)) return true;

        using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();

        using var attendeeCommand = new MySqlCommand(@"
            SELECT COUNT(*)
            FROM event_approved_students
            WHERE event_id = @event_id AND student_id = @student_id", connection);
        attendeeCommand.Parameters.AddWithValue("@event_id", eventId);
        attendeeCommand.Parameters.AddWithValue("@student_id", studentId);
        return Convert.ToInt32(await attendeeCommand.ExecuteScalarAsync()) > 0;
    }

    public async Task RecordAttendanceAsync(string? eventId, string studentId, VerificationMode mode, string status, string remarks)
    {
        if (string.IsNullOrWhiteSpace(eventId)) return;

        using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();

        using var command = new MySqlCommand(@"
            INSERT INTO event_attendance (event_id, student_id, verification_mode, status, remarks)
            VALUES (@event_id, @student_id, @mode, @status, @remarks)", connection);
        command.Parameters.AddWithValue("@event_id", eventId);
        command.Parameters.AddWithValue("@student_id", studentId);
        command.Parameters.AddWithValue("@mode", ToStorageValue(mode));
        command.Parameters.AddWithValue("@status", status);
        command.Parameters.AddWithValue("@remarks", remarks);
        await command.ExecuteNonQueryAsync();
    }

    /* =========================================================================
     * STRUCTURAL MAPPING INTERNALS
     * ========================================================================= */

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

    private static void AddStudentParameters(MySqlCommand command, StudentRecord student, string? salt, string? hash)
    {
        command.Parameters.AddWithValue("@student_id", student.StudentId);
        command.Parameters.AddWithValue("@full_name", student.FullName);
        command.Parameters.AddWithValue("@course", NullIfEmpty(student.Course));

        if (int.TryParse(student.YearLevel, out int year))
            command.Parameters.AddWithValue("@year_level", year);
        else
            command.Parameters.AddWithValue("@year_level", DBNull.Value);

        command.Parameters.AddWithValue("@section_name", NullIfEmpty(student.SectionName));
        command.Parameters.AddWithValue("@status", student.Status);
        command.Parameters.AddWithValue("@nfc_uid", student.NfcUid);
        command.Parameters.AddWithValue("@qr_credential", student.QrCredential);

        command.Parameters.AddWithValue("@pin_salt", salt != null ? salt : DBNull.Value);
        command.Parameters.AddWithValue("@pin_hash", hash != null ? hash : DBNull.Value);
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
            PinLocked = bool.TryParse(Value(reader["pin_locked"]), out bool locked) && locked || Value(reader["pin_locked"]) == "1",
            LastScanTimestamp = reader["last_scan_timestamp"] != DBNull.Value ? Convert.ToDateTime(reader["last_scan_timestamp"]) : null
        };
    }

    public async Task CloseEventAsync(string eventId)
    {
        if (string.IsNullOrWhiteSpace(eventId)) return;

        using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();

        // UPDATED: We no longer DELETE anything. We preserve the approved attendees for historical turnout reporting, 
        // and simply toggle the event's active state to FALSE.
        using var closeEventCmd = new MySqlCommand("UPDATE events SET is_active = FALSE WHERE event_id = @event_id;", connection);
        closeEventCmd.Parameters.AddWithValue("@event_id", eventId);
        await closeEventCmd.ExecuteNonQueryAsync();
    }



    // Add these methods into the DatabaseService class:
    public async Task<IReadOnlyList<EventRecord>> GetAllEventsAsync()
    {
        using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();

        // Preserved: This deliberately grabs ALL events (Active and Closed) for the Reports dropdown
        using var command = new MySqlCommand(@"
            SELECT event_id, event_name, event_date, verification_mode, is_restricted
            FROM events
            ORDER BY event_date DESC", connection);

        var events = new List<EventRecord>();
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            events.Add(new EventRecord
            {
                EventId = Value(reader["event_id"]),
                EventName = Value(reader["event_name"]),
                VerificationMode = Enum.TryParse<VerificationMode>(Value(reader["verification_mode"]), out var vMode) ? vMode : VerificationMode.Standard,
                IsRestricted = reader["is_restricted"] != DBNull.Value && Convert.ToBoolean(reader["is_restricted"]),
                EventDate = reader["event_date"] != DBNull.Value ? Convert.ToDateTime(reader["event_date"]) : null
            });
        }
        return events;
    }

    public async Task<IReadOnlyList<AttendanceLog>> GetEventAttendanceLogsAsync(string eventId)
    {
        using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();

        // NEW: Added 'ea.status' to the SELECT statement
        using var command = new MySqlCommand(@"
            SELECT ea.timestamp, ea.student_id, s.full_name, s.course, s.section_name, ea.verification_mode, ea.status
            FROM event_attendance ea
            LEFT JOIN students s ON ea.student_id = s.student_id
            WHERE ea.event_id = @event_id
            ORDER BY ea.timestamp DESC", connection);

        command.Parameters.AddWithValue("@event_id", eventId);

        var list = new List<AttendanceLog>();
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new AttendanceLog
            {
                Timestamp = reader["timestamp"] != DBNull.Value ? Convert.ToDateTime(reader["timestamp"]).ToString("MMM dd, yyyy - hh:mm tt") : "",
                StudentId = Value(reader["student_id"]),
                FullName = Value(reader["full_name"]),
                Course = Value(reader["course"]),
                Section = Value(reader["section_name"]),
                Mode = Value(reader["verification_mode"]),
                Status = Value(reader["status"]) // NEW: Maps the Check-in/Check-out status
            });
        }
        return list;
    }

    private static object NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;
    private static string Value(object? value) => value == null || value == DBNull.Value ? "" : value.ToString() ?? "";
    public static string ToStorageValue(VerificationMode mode) => mode switch { VerificationMode.Fast => "Fast", VerificationMode.Standard => "Standard", VerificationMode.HighSecurity => "HighSecurity", _ => "Standard" };
    public static string ToStorageValue(TransactionType type) => type switch { TransactionType.Entry => "Entry", TransactionType.Exit => "Exit", TransactionType.EventAttendance => "EventAttendance", _ => "Entry" };
}