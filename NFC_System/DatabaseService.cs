using MySqlConnector;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
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
    public string Section { get; set; } = "";
    public string Mode { get; set; } = "";
    public string Status { get; set; } = "";
}

public sealed class DatabaseService
{
    public const string ConnectionString = "Server=192.168.1.7;Port=3306;Database=nfc_system;User ID=root;Password=;";

    // CLOUD FIRESTORE CONFIGURATION
    private const string FIREBASE_PROJECT_ID = "nfc-system-d6ec2";
    private static readonly HttpClient _httpClient = new HttpClient();

    public async Task EnsureSchemaAsync()
    {
        string baseConnection = "Server=192.168.1.7;Port=3306;User ID=root;Password=;";
        using var connection = new MySqlConnection(baseConnection);
        await connection.OpenAsync();

        using (var createDbCmd = new MySqlCommand("CREATE DATABASE IF NOT EXISTS nfc_system;", connection))
        {
            await createDbCmd.ExecuteNonQueryAsync();
        }

        await connection.ChangeDatabaseAsync("nfc_system");

        string schemaSql = @"
            CREATE TABLE IF NOT EXISTS students (
                student_id VARCHAR(50) PRIMARY KEY,
                full_name VARCHAR(100) NOT NULL,
                course VARCHAR(100),
                year_level VARCHAR(20),
                section_name VARCHAR(50),
                status VARCHAR(20) DEFAULT 'Active',
                nfc_uid VARCHAR(50) UNIQUE,
                pin_salt VARCHAR(255),
                pin_hash VARCHAR(255),
                qr_credential VARCHAR(255),
                entry_state VARCHAR(20) DEFAULT 'OUTSIDE',
                failed_pin_attempts INT DEFAULT 0,
                pin_locked BOOLEAN DEFAULT FALSE,
                last_scan_timestamp DATETIME NULL
            );

            CREATE TABLE IF NOT EXISTS courses (
                course_name VARCHAR(100) PRIMARY KEY
            );

            CREATE TABLE IF NOT EXISTS verification_logs (
                id INT AUTO_INCREMENT PRIMARY KEY,
                timestamp DATETIME DEFAULT CURRENT_TIMESTAMP,
                student_id VARCHAR(50),
                nfc_uid VARCHAR(50),
                transaction_type VARCHAR(50),
                verification_mode VARCHAR(50),
                is_granted BOOLEAN,
                error_code VARCHAR(100),
                error_message TEXT,
                remarks TEXT
            );

            CREATE TABLE IF NOT EXISTS alerts (
                id INT AUTO_INCREMENT PRIMARY KEY,
                timestamp DATETIME DEFAULT CURRENT_TIMESTAMP,
                student_id VARCHAR(50) NULL,
                alert_type VARCHAR(100),
                message TEXT
            );

            CREATE TABLE IF NOT EXISTS app_settings (
                setting_key VARCHAR(100) PRIMARY KEY,
                setting_value VARCHAR(255)
            );

            CREATE TABLE IF NOT EXISTS events (
                event_id VARCHAR(50) PRIMARY KEY,
                event_name VARCHAR(150),
                event_date DATETIME,
                verification_mode VARCHAR(50),
                is_restricted BOOLEAN DEFAULT FALSE,
                is_active BOOLEAN DEFAULT TRUE
            );

            CREATE TABLE IF NOT EXISTS event_approved_students (
                event_id VARCHAR(50),
                student_id VARCHAR(50),
                PRIMARY KEY (event_id, student_id)
            );

            CREATE TABLE IF NOT EXISTS event_attendance (
                id INT AUTO_INCREMENT PRIMARY KEY,
                timestamp DATETIME DEFAULT CURRENT_TIMESTAMP,
                event_id VARCHAR(50),
                student_id VARCHAR(50),
                verification_mode VARCHAR(50),
                status VARCHAR(50),
                remarks TEXT
            );

            CREATE TABLE IF NOT EXISTS staff (
                nfc_uid VARCHAR(50) PRIMARY KEY,
                full_name VARCHAR(100),
                role VARCHAR(50)
            );
        ";

        using (var schemaCmd = new MySqlCommand(schemaSql, connection))
        {
            await schemaCmd.ExecuteNonQueryAsync();
        }
    }

    // =========================================================================
    // CLOUD SYNCHRONIZATION METHODS (FIRESTORE REST API)
    // =========================================================================

    public async Task<int> PullStudentsFromCloudAsync()
    {
        string url = $"https://firestore.googleapis.com/v1/projects/{FIREBASE_PROJECT_ID}/databases/(default)/documents/students?pageSize=1000";
        int updatedCount = 0;

        try
        {
            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode) throw new Exception(await response.Content.ReadAsStringAsync());

            var json = await response.Content.ReadAsStringAsync();
            using JsonDocument doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("documents", out var documents)) return 0;

            using var connection = new MySqlConnection(ConnectionString);
            await connection.OpenAsync();

            foreach (var document in documents.EnumerateArray())
            {
                if (!document.TryGetProperty("fields", out var fields)) continue;

                string studentId = ExtractString(fields, "student_id");
                if (string.IsNullOrWhiteSpace(studentId))
                {
                    string docName = document.GetProperty("name").GetString() ?? "";
                    studentId = docName.Split('/').LastOrDefault() ?? "";
                }
                if (string.IsNullOrWhiteSpace(studentId)) continue;

                string fullName = ExtractString(fields, "full_name");
                string course = ExtractString(fields, "course");
                string yearLvl = ExtractString(fields, "year_level");
                string section = ExtractString(fields, "section_name");
                string status = ExtractString(fields, "status");
                string nfcUid = ExtractString(fields, "nfc_uid");
                string qr = ExtractString(fields, "qr_credential");
                string pinHash = ExtractString(fields, "pin_hash");
                string pinSalt = ExtractString(fields, "pin_salt");
                bool pinLocked = ExtractBool(fields, "pin_locked");
                int failedAttempts = ExtractInt(fields, "failed_pin_attempts");

                string sql = @"
                    INSERT INTO students 
                    (student_id, full_name, course, year_level, section_name, status, nfc_uid, qr_credential, pin_hash, pin_salt, pin_locked, failed_pin_attempts)
                    VALUES 
                    (@id, @name, @course, @year, @section, @status, @nfc, @qr, @hash, @salt, @locked, @failed)
                    ON DUPLICATE KEY UPDATE 
                    full_name=@name, course=@course, year_level=@year, section_name=@section, status=@status, nfc_uid=@nfc, 
                    qr_credential=@qr, pin_hash=@hash, pin_salt=@salt, pin_locked=@locked, failed_pin_attempts=@failed";

                using var cmd = new MySqlCommand(sql, connection);
                cmd.Parameters.AddWithValue("@id", studentId);
                cmd.Parameters.AddWithValue("@name", fullName);
                cmd.Parameters.AddWithValue("@course", NullIfEmpty(course));
                cmd.Parameters.AddWithValue("@year", NullIfEmpty(yearLvl));
                cmd.Parameters.AddWithValue("@section", NullIfEmpty(section));
                cmd.Parameters.AddWithValue("@status", string.IsNullOrWhiteSpace(status) ? "Active" : status);
                cmd.Parameters.AddWithValue("@nfc", NullIfEmpty(nfcUid));
                cmd.Parameters.AddWithValue("@qr", NullIfEmpty(qr));
                cmd.Parameters.AddWithValue("@hash", NullIfEmpty(pinHash));
                cmd.Parameters.AddWithValue("@salt", NullIfEmpty(pinSalt));
                cmd.Parameters.AddWithValue("@locked", pinLocked);
                cmd.Parameters.AddWithValue("@failed", failedAttempts);

                int affected = await cmd.ExecuteNonQueryAsync();
                if (affected > 0) updatedCount++;
            }
        }
        catch (Exception ex) { throw new Exception($"Student Sync Error: {ex.Message}"); }

        return updatedCount;
    }

    public async Task<int> PushStudentsToCloudAsync()
    {
        int pushedCount = 0;
        try
        {
            using var connection = new MySqlConnection(ConnectionString);
            await connection.OpenAsync();

            using var cmd = new MySqlCommand("SELECT * FROM students", connection);
            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                string studentId = Value(reader["student_id"]);
                if (string.IsNullOrWhiteSpace(studentId)) continue;

                var firestorePayload = new
                {
                    fields = new
                    {
                        student_id = new { stringValue = studentId },
                        full_name = new { stringValue = Value(reader["full_name"]) },
                        course = new { stringValue = Value(reader["course"]) },
                        year_level = new { stringValue = Value(reader["year_level"]) },
                        section_name = new { stringValue = Value(reader["section_name"]) },
                        status = new { stringValue = Value(reader["status"]) },
                        nfc_uid = new { stringValue = Value(reader["nfc_uid"]) },
                        qr_credential = new { stringValue = Value(reader["qr_credential"]) },
                        pin_hash = new { stringValue = Value(reader["pin_hash"]) },
                        pin_salt = new { stringValue = Value(reader["pin_salt"]) },
                        pin_locked = new { booleanValue = reader["pin_locked"].ToString() == "1" || reader["pin_locked"].ToString()?.ToLower() == "true" },
                        failed_pin_attempts = new { integerValue = Value(reader["failed_pin_attempts"]) }
                    }
                };

                string jsonPayload = JsonSerializer.Serialize(firestorePayload);
                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                string docId = Uri.EscapeDataString(studentId);
                string url = $"https://firestore.googleapis.com/v1/projects/{FIREBASE_PROJECT_ID}/databases/(default)/documents/students/{docId}";

                var response = await _httpClient.PatchAsync(url, content);
                if (response.IsSuccessStatusCode) pushedCount++;
                else throw new Exception(await response.Content.ReadAsStringAsync());
            }
        }
        catch (Exception ex) { throw new Exception($"Student Upload Error: {ex.Message}"); }

        return pushedCount;
    }

    public async Task<int> PullStaffFromCloudAsync()
    {
        string url = $"https://firestore.googleapis.com/v1/projects/{FIREBASE_PROJECT_ID}/databases/(default)/documents/staff?pageSize=1000";
        int updatedCount = 0;

        try
        {
            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode) throw new Exception(await response.Content.ReadAsStringAsync());

            var json = await response.Content.ReadAsStringAsync();
            using JsonDocument doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("documents", out var documents)) return 0;

            using var connection = new MySqlConnection(ConnectionString);
            await connection.OpenAsync();

            foreach (var document in documents.EnumerateArray())
            {
                if (!document.TryGetProperty("fields", out var fields)) continue;

                string nfcUid = ExtractString(fields, "nfc_uid");
                if (string.IsNullOrWhiteSpace(nfcUid))
                {
                    string docName = document.GetProperty("name").GetString() ?? "";
                    nfcUid = docName.Split('/').LastOrDefault() ?? "";
                }
                if (string.IsNullOrWhiteSpace(nfcUid)) continue;

                string fullName = ExtractString(fields, "full_name");
                string role = ExtractString(fields, "role");

                string sql = @"
                    INSERT INTO staff (nfc_uid, full_name, role) 
                    VALUES (@uid, @name, @role) 
                    ON DUPLICATE KEY UPDATE full_name=@name, role=@role";

                using var cmd = new MySqlCommand(sql, connection);
                cmd.Parameters.AddWithValue("@uid", nfcUid);
                cmd.Parameters.AddWithValue("@name", fullName);
                cmd.Parameters.AddWithValue("@role", role);

                int affected = await cmd.ExecuteNonQueryAsync();
                if (affected > 0) updatedCount++;
            }
        }
        catch (Exception ex) { throw new Exception($"Staff Sync Error: {ex.Message}"); }

        return updatedCount;
    }

    public async Task<int> PushStaffToCloudAsync()
    {
        int pushedCount = 0;
        try
        {
            using var connection = new MySqlConnection(ConnectionString);
            await connection.OpenAsync();

            using var cmd = new MySqlCommand("SELECT * FROM staff", connection);
            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                string nfcUid = Value(reader["nfc_uid"]);
                if (string.IsNullOrWhiteSpace(nfcUid)) continue;

                var firestorePayload = new
                {
                    fields = new
                    {
                        nfc_uid = new { stringValue = nfcUid },
                        full_name = new { stringValue = Value(reader["full_name"]) },
                        role = new { stringValue = Value(reader["role"]) }
                    }
                };

                string jsonPayload = JsonSerializer.Serialize(firestorePayload);
                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                string docId = Uri.EscapeDataString(nfcUid);
                string url = $"https://firestore.googleapis.com/v1/projects/{FIREBASE_PROJECT_ID}/databases/(default)/documents/staff/{docId}";

                var response = await _httpClient.PatchAsync(url, content);
                if (response.IsSuccessStatusCode) pushedCount++;
                else throw new Exception(await response.Content.ReadAsStringAsync());
            }
        }
        catch (Exception ex) { throw new Exception($"Staff Upload Error: {ex.Message}"); }

        return pushedCount;
    }

    public async Task<int> PullCoursesFromCloudAsync()
    {
        string url = $"https://firestore.googleapis.com/v1/projects/{FIREBASE_PROJECT_ID}/databases/(default)/documents/courses?pageSize=1000";
        int updatedCount = 0;

        try
        {
            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode) throw new Exception(await response.Content.ReadAsStringAsync());

            var json = await response.Content.ReadAsStringAsync();
            using JsonDocument doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("documents", out var documents)) return 0;

            using var connection = new MySqlConnection(ConnectionString);
            await connection.OpenAsync();

            foreach (var document in documents.EnumerateArray())
            {
                if (!document.TryGetProperty("fields", out var fields)) continue;

                string courseName = ExtractString(fields, "course_name");
                if (string.IsNullOrWhiteSpace(courseName))
                {
                    string docName = document.GetProperty("name").GetString() ?? "";
                    courseName = docName.Split('/').LastOrDefault() ?? "";
                }
                if (string.IsNullOrWhiteSpace(courseName)) continue;

                string sql = "INSERT IGNORE INTO courses (course_name) VALUES (@name)";
                using var cmd = new MySqlCommand(sql, connection);
                cmd.Parameters.AddWithValue("@name", courseName);

                int affected = await cmd.ExecuteNonQueryAsync();
                if (affected > 0) updatedCount++;
            }
        }
        catch (Exception ex) { throw new Exception($"Course Sync Error: {ex.Message}"); }

        return updatedCount;
    }

    public async Task<int> PushCoursesToCloudAsync()
    {
        int pushedCount = 0;
        try
        {
            using var connection = new MySqlConnection(ConnectionString);
            await connection.OpenAsync();

            using var cmd = new MySqlCommand("SELECT * FROM courses", connection);
            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                string courseName = Value(reader["course_name"]);
                if (string.IsNullOrWhiteSpace(courseName)) continue;

                var firestorePayload = new
                {
                    fields = new
                    {
                        course_name = new { stringValue = courseName }
                    }
                };

                string jsonPayload = JsonSerializer.Serialize(firestorePayload);
                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                string docId = Uri.EscapeDataString(courseName);
                string url = $"https://firestore.googleapis.com/v1/projects/{FIREBASE_PROJECT_ID}/databases/(default)/documents/courses/{docId}";

                var response = await _httpClient.PatchAsync(url, content);
                if (response.IsSuccessStatusCode) pushedCount++;
                else throw new Exception(await response.Content.ReadAsStringAsync());
            }
        }
        catch (Exception ex) { throw new Exception($"Course Upload Error: {ex.Message}"); }

        return pushedCount;
    }

    public async Task<int> PullEventsFromCloudAsync()
    {
        string url = $"https://firestore.googleapis.com/v1/projects/{FIREBASE_PROJECT_ID}/databases/(default)/documents/events?pageSize=1000";
        int updatedCount = 0;

        try
        {
            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode) throw new Exception(await response.Content.ReadAsStringAsync());

            var json = await response.Content.ReadAsStringAsync();
            using JsonDocument doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("documents", out var documents)) return 0;

            using var connection = new MySqlConnection(ConnectionString);
            await connection.OpenAsync();

            foreach (var document in documents.EnumerateArray())
            {
                if (!document.TryGetProperty("fields", out var fields)) continue;

                string eventId = ExtractString(fields, "event_id");
                if (string.IsNullOrWhiteSpace(eventId))
                {
                    string docName = document.GetProperty("name").GetString() ?? "";
                    eventId = docName.Split('/').LastOrDefault() ?? "";
                }
                if (string.IsNullOrWhiteSpace(eventId)) continue;

                string eventName = ExtractString(fields, "event_name");
                string verificationMode = ExtractString(fields, "verification_mode");
                bool isRestricted = ExtractBool(fields, "is_restricted");
                bool isActive = ExtractBool(fields, "is_active");

                DateTime? eventDate = ExtractTimestamp(fields, "event_date");

                string sql = @"
                    INSERT INTO events (event_id, event_name, event_date, verification_mode, is_restricted, is_active) 
                    VALUES (@id, @name, @date, @mode, @restricted, @active) 
                    ON DUPLICATE KEY UPDATE 
                    event_name=@name, event_date=@date, verification_mode=@mode, is_restricted=@restricted, is_active=@active";

                using var cmd = new MySqlCommand(sql, connection);
                cmd.Parameters.AddWithValue("@id", eventId);
                cmd.Parameters.AddWithValue("@name", eventName);
                cmd.Parameters.AddWithValue("@date", eventDate.HasValue ? (object)eventDate.Value : DBNull.Value);
                cmd.Parameters.AddWithValue("@mode", verificationMode);
                cmd.Parameters.AddWithValue("@restricted", isRestricted);
                cmd.Parameters.AddWithValue("@active", isActive);

                int affected = await cmd.ExecuteNonQueryAsync();
                if (affected > 0) updatedCount++;
            }
        }
        catch (Exception ex) { throw new Exception($"Events Sync Error: {ex.Message}"); }

        return updatedCount;
    }

    public async Task<int> PushEventsToCloudAsync()
    {
        int pushedCount = 0;
        try
        {
            using var connection = new MySqlConnection(ConnectionString);
            await connection.OpenAsync();

            using var cmd = new MySqlCommand("SELECT * FROM events", connection);
            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                string eventId = Value(reader["event_id"]);
                if (string.IsNullOrWhiteSpace(eventId)) continue;

                var firestorePayload = new
                {
                    fields = new
                    {
                        event_id = new { stringValue = eventId },
                        event_name = new { stringValue = Value(reader["event_name"]) },
                        verification_mode = new { stringValue = Value(reader["verification_mode"]) },
                        is_restricted = new { booleanValue = Convert.ToBoolean(reader["is_restricted"]) },
                        is_active = new { booleanValue = Convert.ToBoolean(reader["is_active"]) },
                        event_date = reader["event_date"] != DBNull.Value
                            ? new { timestampValue = Convert.ToDateTime(reader["event_date"]).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ") }
                            : null
                    }
                };

                string jsonPayload = JsonSerializer.Serialize(firestorePayload, new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });
                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                string docId = Uri.EscapeDataString(eventId);
                string url = $"https://firestore.googleapis.com/v1/projects/{FIREBASE_PROJECT_ID}/databases/(default)/documents/events/{docId}";

                var response = await _httpClient.PatchAsync(url, content);
                if (response.IsSuccessStatusCode) pushedCount++;
                else throw new Exception(await response.Content.ReadAsStringAsync());
            }
        }
        catch (Exception ex) { throw new Exception($"Events Upload Error: {ex.Message}"); }

        return pushedCount;
    }

    public async Task<int> PullEventApprovedStudentsFromCloudAsync()
    {
        string url = $"https://firestore.googleapis.com/v1/projects/{FIREBASE_PROJECT_ID}/databases/(default)/documents/event_approved_students?pageSize=2000";
        int updatedCount = 0;

        try
        {
            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode) throw new Exception(await response.Content.ReadAsStringAsync());

            var json = await response.Content.ReadAsStringAsync();
            using JsonDocument doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("documents", out var documents)) return 0;

            using var connection = new MySqlConnection(ConnectionString);
            await connection.OpenAsync();

            foreach (var document in documents.EnumerateArray())
            {
                if (!document.TryGetProperty("fields", out var fields)) continue;

                string eventId = ExtractString(fields, "event_id");
                string studentId = ExtractString(fields, "student_id");

                if (string.IsNullOrWhiteSpace(eventId) || string.IsNullOrWhiteSpace(studentId)) continue;

                string sql = "INSERT IGNORE INTO event_approved_students (event_id, student_id) VALUES (@event_id, @student_id)";
                using var cmd = new MySqlCommand(sql, connection);
                cmd.Parameters.AddWithValue("@event_id", eventId);
                cmd.Parameters.AddWithValue("@student_id", studentId);

                int affected = await cmd.ExecuteNonQueryAsync();
                if (affected > 0) updatedCount++;
            }
        }
        catch (Exception ex) { throw new Exception($"Approved Roster Sync Error: {ex.Message}"); }

        return updatedCount;
    }

    public async Task<int> PushEventApprovedStudentsToCloudAsync()
    {
        int pushedCount = 0;
        try
        {
            using var connection = new MySqlConnection(ConnectionString);
            await connection.OpenAsync();

            using var cmd = new MySqlCommand("SELECT * FROM event_approved_students", connection);
            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                string eventId = Value(reader["event_id"]);
                string studentId = Value(reader["student_id"]);

                if (string.IsNullOrWhiteSpace(eventId) || string.IsNullOrWhiteSpace(studentId)) continue;

                var firestorePayload = new
                {
                    fields = new
                    {
                        event_id = new { stringValue = eventId },
                        student_id = new { stringValue = studentId }
                    }
                };

                string jsonPayload = JsonSerializer.Serialize(firestorePayload);
                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                string docId = Uri.EscapeDataString($"{eventId}_{studentId}");
                string url = $"https://firestore.googleapis.com/v1/projects/{FIREBASE_PROJECT_ID}/databases/(default)/documents/event_approved_students/{docId}";

                var response = await _httpClient.PatchAsync(url, content);
                if (response.IsSuccessStatusCode) pushedCount++;
            }
        }
        catch (Exception ex) { throw new Exception($"Approved Roster Upload Error: {ex.Message}"); }

        return pushedCount;
    }

    public async Task<int> PushLogsToCloudAsync()
    {
        int pushedCount = 0;
        try
        {
            using var connection = new MySqlConnection(ConnectionString);
            await connection.OpenAsync();

            using var cmd = new MySqlCommand(@"
                SELECT id, timestamp, student_id, nfc_uid, transaction_type, verification_mode, is_granted, error_code, error_message, remarks 
                FROM verification_logs 
                ORDER BY timestamp DESC LIMIT 100", connection);

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                string id = reader["id"].ToString() ?? Guid.NewGuid().ToString();
                string firestoreTimestamp = Convert.ToDateTime(reader["timestamp"]).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

                var firestorePayload = new
                {
                    fields = new
                    {
                        student_id = new { stringValue = Value(reader["student_id"]) },
                        nfc_uid = new { stringValue = Value(reader["nfc_uid"]) },
                        transaction_type = new { stringValue = Value(reader["transaction_type"]) },
                        verification_mode = new { stringValue = Value(reader["verification_mode"]) },
                        is_granted = new { booleanValue = reader["is_granted"].ToString() == "1" || reader["is_granted"].ToString()?.ToLower() == "true" },
                        error_code = new { stringValue = Value(reader["error_code"]) },
                        error_message = new { stringValue = Value(reader["error_message"]) },
                        remarks = new { stringValue = Value(reader["remarks"]) },
                        timestamp = new { timestampValue = firestoreTimestamp }
                    }
                };

                string jsonPayload = JsonSerializer.Serialize(firestorePayload);
                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                string url = $"https://firestore.googleapis.com/v1/projects/{FIREBASE_PROJECT_ID}/databases/(default)/documents/verification_logs/log_{id}";

                var response = await _httpClient.PatchAsync(url, content);
                if (response.IsSuccessStatusCode) pushedCount++;
                else throw new Exception(await response.Content.ReadAsStringAsync());
            }
        }
        catch (Exception ex) { throw new Exception($"Log Upload Error: {ex.Message}"); }

        return pushedCount;
    }

    public async Task<int> PushEventAttendanceToCloudAsync()
    {
        int pushedCount = 0;
        try
        {
            using var connection = new MySqlConnection(ConnectionString);
            await connection.OpenAsync();

            using var cmd = new MySqlCommand(@"
                SELECT id, timestamp, event_id, student_id, verification_mode, status, remarks 
                FROM event_attendance 
                ORDER BY timestamp DESC LIMIT 200", connection);

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                string id = reader["id"].ToString() ?? Guid.NewGuid().ToString();
                string firestoreTimestamp = Convert.ToDateTime(reader["timestamp"]).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

                var firestorePayload = new
                {
                    fields = new
                    {
                        event_id = new { stringValue = Value(reader["event_id"]) },
                        student_id = new { stringValue = Value(reader["student_id"]) },
                        verification_mode = new { stringValue = Value(reader["verification_mode"]) },
                        status = new { stringValue = Value(reader["status"]) },
                        remarks = new { stringValue = Value(reader["remarks"]) },
                        timestamp = new { timestampValue = firestoreTimestamp }
                    }
                };

                string jsonPayload = JsonSerializer.Serialize(firestorePayload);
                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                string url = $"https://firestore.googleapis.com/v1/projects/{FIREBASE_PROJECT_ID}/databases/(default)/documents/event_attendance/att_{id}";

                var response = await _httpClient.PatchAsync(url, content);
                if (response.IsSuccessStatusCode) pushedCount++;
                else throw new Exception(await response.Content.ReadAsStringAsync());
            }
        }
        catch (Exception ex) { throw new Exception($"Event Attendance Upload Error: {ex.Message}"); }

        return pushedCount;
    }

    // JSON Extractor Helpers
    private string ExtractString(JsonElement fields, string key)
    {
        if (fields.TryGetProperty(key, out var prop) && prop.TryGetProperty("stringValue", out var val))
            return val.GetString() ?? "";
        return "";
    }

    private bool ExtractBool(JsonElement fields, string key)
    {
        if (fields.TryGetProperty(key, out var prop) && prop.TryGetProperty("booleanValue", out var val))
            return val.GetBoolean();
        return false;
    }

    private int ExtractInt(JsonElement fields, string key)
    {
        if (fields.TryGetProperty(key, out var prop) && prop.TryGetProperty("integerValue", out var val))
            return int.TryParse(val.GetString(), out int result) ? result : 0;
        return 0;
    }

    private DateTime? ExtractTimestamp(JsonElement fields, string key)
    {
        if (fields.TryGetProperty(key, out var prop) && prop.TryGetProperty("timestampValue", out var val))
        {
            if (DateTime.TryParse(val.GetString(), out DateTime dt)) return dt.ToLocalTime();
        }
        else if (fields.TryGetProperty(key, out var propStr) && propStr.TryGetProperty("stringValue", out var valStr))
        {
            if (DateTime.TryParse(valStr.GetString(), out DateTime dt2)) return dt2.ToLocalTime();
        }
        return null;
    }

    // =========================================================================
    // OFFLINE SHADOW CACHE INTEGRATION (NEW METHODS)
    // =========================================================================

    public async Task<bool> TestConnectionAsync()
    {
        try
        {
            using var connection = new MySqlConnection(ConnectionString);
            await connection.OpenAsync();
            return true; // XAMPP server is reachable
        }
        catch
        {
            return false; // Connection failed
        }
    }

    public async Task UpdateShadowCacheAsync()
    {
        try
        {
            using var connection = new MySqlConnection(ConnectionString);
            await connection.OpenAsync();

            // 1. Export Active Students to Shadow Cache
            var students = new List<CachedStudent>();
            using (var cmd = new MySqlCommand("SELECT student_id, full_name, nfc_uid, pin_hash, pin_salt, status, pin_locked FROM students WHERE nfc_uid IS NOT NULL AND nfc_uid != ''", connection))
            using (var reader = await cmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    students.Add(new CachedStudent
                    {
                        StudentId = Value(reader["student_id"]),
                        FullName = Value(reader["full_name"]),
                        NfcUid = Value(reader["nfc_uid"]),
                        PinHash = Value(reader["pin_hash"]),
                        PinSalt = Value(reader["pin_salt"]),
                        Status = Value(reader["status"]),
                        PinLocked = reader["pin_locked"].ToString() == "1" || reader["pin_locked"].ToString()?.ToLower() == "true"
                    });
                }
            }
            OfflineCacheService.UpdateStudentCache(students);

            // 2. Export Active Events to Shadow Cache
            var events = new List<CachedEvent>();
            using (var cmd = new MySqlCommand("SELECT event_id, event_name, verification_mode, is_restricted, is_active FROM events WHERE is_active = 1", connection))
            using (var reader = await cmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    events.Add(new CachedEvent
                    {
                        EventId = Value(reader["event_id"]),
                        EventName = Value(reader["event_name"]),
                        VerificationMode = Value(reader["verification_mode"]),
                        IsRestricted = Convert.ToBoolean(reader["is_restricted"]),
                        IsActive = Convert.ToBoolean(reader["is_active"])
                    });
                }
            }

            // 3. Export Event Approved Rosters
            var rostersDict = new Dictionary<string, List<string>>();
            using (var cmd = new MySqlCommand("SELECT event_id, student_id FROM event_approved_students", connection))
            using (var reader = await cmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    string eId = Value(reader["event_id"]);
                    string sId = Value(reader["student_id"]);

                    if (!rostersDict.ContainsKey(eId))
                        rostersDict[eId] = new List<string>();

                    rostersDict[eId].Add(sId);
                }
            }

            var rosters = rostersDict.Select(kvp => new CachedEventRoster
            {
                EventId = kvp.Key,
                ApprovedStudentIds = kvp.Value
            }).ToList();

            OfflineCacheService.UpdateEventCache(events, rosters);
        }
        catch { /* Silently fail background updates if XAMPP drops mid-pull */ }
    }

    public async Task SyncOfflineLogsToServerAsync()
    {
        if (!OfflineCacheService.HasPendingLogs()) return;

        try
        {
            using var connection = new MySqlConnection(ConnectionString);
            await connection.OpenAsync();

            // Push pending gate logs
            var gateLogs = OfflineCacheService.GetPendingGateLogs();
            foreach (var log in gateLogs)
            {
                using var cmd = new MySqlCommand(@"
                    INSERT INTO verification_logs (timestamp, student_id, nfc_uid, transaction_type, verification_mode, is_granted, error_code, remarks) 
                    VALUES (@ts, @sid, @nfc, @ttype, @vmode, @granted, @err, @rem)", connection);

                cmd.Parameters.AddWithValue("@ts", DateTime.Parse(log.Timestamp));
                cmd.Parameters.AddWithValue("@sid", NullIfEmpty(log.StudentId));
                cmd.Parameters.AddWithValue("@nfc", NullIfEmpty(log.NfcUid));
                cmd.Parameters.AddWithValue("@ttype", log.TransactionType);
                cmd.Parameters.AddWithValue("@vmode", log.VerificationMode);
                cmd.Parameters.AddWithValue("@granted", log.IsGranted ? 1 : 0);
                cmd.Parameters.AddWithValue("@err", NullIfEmpty(log.ErrorCode));
                cmd.Parameters.AddWithValue("@rem", NullIfEmpty(log.Remarks));

                await cmd.ExecuteNonQueryAsync();
            }

            // Push pending event logs
            var eventLogs = OfflineCacheService.GetPendingEventLogs();
            foreach (var log in eventLogs)
            {
                using var cmd = new MySqlCommand(@"
                    INSERT INTO event_attendance (timestamp, event_id, student_id, verification_mode, status, remarks) 
                    VALUES (@ts, @eid, @sid, @vmode, @status, @rem)", connection);

                cmd.Parameters.AddWithValue("@ts", DateTime.Parse(log.Timestamp));
                cmd.Parameters.AddWithValue("@eid", log.EventId);
                cmd.Parameters.AddWithValue("@sid", log.StudentId);
                cmd.Parameters.AddWithValue("@vmode", log.VerificationMode);
                cmd.Parameters.AddWithValue("@status", log.Status);
                cmd.Parameters.AddWithValue("@rem", NullIfEmpty(log.Remarks));

                await cmd.ExecuteNonQueryAsync();
            }

            // Successfully pushed, clear local files
            OfflineCacheService.ClearPendingLogs();
        }
        catch { /* Let it stay in cache and try again next tick */ }
    }

    // =========================================================================

    public async Task<IReadOnlyList<SystemAuditLog>> GetMasterAuditLogsAsync(int limit = 1000)
    {
        var masterLogs = new List<SystemAuditLog>();
        using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();

        using (var cmd1 = new MySqlCommand("SELECT timestamp, student_id, nfc_uid, transaction_type, is_granted, error_code, remarks FROM verification_logs WHERE transaction_type != 'EventAttendance' ORDER BY timestamp DESC LIMIT @limit", connection))
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

        using (var cmd2 = new MySqlCommand("SELECT timestamp, student_id, alert_type, message FROM alerts ORDER BY timestamp DESC LIMIT @limit", connection))
        {
            cmd2.Parameters.AddWithValue("@limit", limit);
            using var reader = await cmd2.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                string alertType = Value(reader["alert_type"]);

                bool isAdminAction = alertType.StartsWith("ADMIN") || alertType.StartsWith("STAFF");

                string status = isAdminAction ? "RESOLVED" : "FLAGGED";
                string logType = isAdminAction ? "ADMIN ACTION" : "SECURITY ALERT";

                masterLogs.Add(new SystemAuditLog
                {
                    Timestamp = Convert.ToDateTime(reader["timestamp"]),
                    LogType = logType,
                    Subject = Value(reader["student_id"]),
                    Action = alertType,
                    Status = status,
                    Details = Value(reader["message"])
                });
            }
        }

        var sorted = masterLogs.OrderByDescending(l => l.Timestamp).Take(limit).ToList();
        foreach (var log in sorted)
        {
            log.DisplayTime = log.Timestamp.ToString("MMM dd, yyyy - hh:mm:ss tt");

            if (log.Status == "GRANTED" || log.Status == "RESOLVED")
                log.StatusColor = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 52, 211, 153));
            else if (log.Status == "DENIED" || log.Status == "FLAGGED")
                log.StatusColor = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 248, 113, 113));
            else
                log.StatusColor = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 160, 160, 160));
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

        using (var cmd = new MySqlCommand("SELECT COUNT(*) FROM verification_logs WHERE transaction_type != 'EventAttendance' AND DATE(timestamp) = CURDATE()", connection))
            totalScans = Convert.ToInt32(await cmd.ExecuteScalarAsync());

        using (var cmd = new MySqlCommand("SELECT COUNT(*) FROM students WHERE entry_state = 'INSIDE'", connection))
            inside = Convert.ToInt32(await cmd.ExecuteScalarAsync());

        using (var cmd = new MySqlCommand("SELECT COUNT(*) FROM verification_logs WHERE transaction_type != 'EventAttendance' AND is_granted = 0 AND DATE(timestamp) = CURDATE()", connection))
            denied = Convert.ToInt32(await cmd.ExecuteScalarAsync());

        return (totalScans, inside, denied);
    }

    public async Task<IReadOnlyList<StatItem>> GetDailySecurityAlertsAsync()
    {
        using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();

        using var command = new MySqlCommand(@"
            SELECT DATE_FORMAT(timestamp, '%b %d, %Y') as DateLbl, COUNT(*) as Total 
            FROM verification_logs 
            WHERE transaction_type != 'EventAttendance' AND is_granted = 0 
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

        using var command = new MySqlCommand(@"
            SELECT DATE_FORMAT(timestamp, '%b %d, %Y') as DateLbl, COUNT(*) as Total 
            FROM verification_logs 
            WHERE transaction_type != 'EventAttendance' AND is_granted = 0 AND timestamp >= DATE_SUB(CURDATE(), INTERVAL 30 DAY)
            GROUP BY DATE(timestamp), DateLbl
            ORDER BY Total DESC", connection);

        var list = new List<(string DateLbl, int Count)>();
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add((Value(reader["DateLbl"]), Convert.ToInt32(reader["Total"])));
        }

        if (list.Count == 0) return ("No Data", 0, "No Data", 0);

        var high = list.First();
        var low = list.Last();

        return (high.DateLbl, high.Count, low.DateLbl, low.Count);
    }

    public async Task<IReadOnlyList<StatItem>> GetDailyEntryStatsAsync()
    {
        using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();

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
            WHERE vl.transaction_type != 'EventAttendance'
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

    public async Task SaveStudentAsync(StudentRecord student, string? pin)
    {
        using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();

        if (await NfcUidBelongsToAnotherStudentAsync(connection, student.NfcUid, student.StudentId))
            throw new InvalidOperationException("NFC UID is already linked to another student.");

        string? salt = null;
        string? hash = null;

        if (!string.IsNullOrWhiteSpace(pin))
        {
            var hashedResult = PinHasher.HashPin(pin);
            salt = hashedResult.Salt;
            hash = hashedResult.Hash;
        }

        bool exists = false;
        using (var checkCmd = new MySqlCommand("SELECT COUNT(*) FROM students WHERE student_id = @id", connection))
        {
            checkCmd.Parameters.AddWithValue("@id", student.StudentId);
            exists = Convert.ToInt32(await checkCmd.ExecuteScalarAsync()) > 0;
        }

        string sql;

        if (exists)
        {
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
            sql = @"
            INSERT INTO students
            (student_id, full_name, course, year_level, section_name, status, nfc_uid, pin_salt, pin_hash, qr_credential, entry_state, failed_pin_attempts, pin_locked)
            VALUES
            (@student_id, @full_name, @course, @year_level, @section_name, @status, @nfc_uid, @pin_salt, @pin_hash, @qr_credential, 'OUTSIDE', 0, FALSE);";
        }

        using var command = new MySqlCommand(sql, connection);
        AddStudentParameters(command, student, salt, hash);
        await command.ExecuteNonQueryAsync();

        await AddAlertAsync(student.StudentId, "ADMIN_ACTION", $"Registered or updated student profile for {student.FullName}.");
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

        using var command = new MySqlCommand("INSERT IGNORE INTO courses (course_name) VALUES (@name)", connection);
        command.Parameters.AddWithValue("@name", courseName.Trim());
        await command.ExecuteNonQueryAsync();

        await AddAlertAsync(null, "ADMIN_ACTION", $"Added new academic course to database: {courseName}");
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

        await AddAlertAsync(studentId, "ADMIN_ACTION", $"Updated student status to '{status}'.");
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
            WHERE transaction_type != 'EventAttendance'
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

    public async Task SaveEventAsync(string eventId, string eventName, VerificationMode mode, bool isRestricted)
    {
        using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();

        using var command = new MySqlCommand(@"
            INSERT INTO events (event_id, event_name, event_date, verification_mode, is_restricted, is_active)
            VALUES (@event_id, @event_name, NOW(), @mode, @is_restricted, TRUE)
            ON DUPLICATE KEY UPDATE event_name = @event_name, verification_mode = @mode, is_restricted = @is_restricted, event_date = NOW(), is_active = TRUE", connection);
        command.Parameters.AddWithValue("@event_id", eventId);
        command.Parameters.AddWithValue("@event_name", eventName);
        command.Parameters.AddWithValue("@mode", ToStorageValue(mode));
        command.Parameters.AddWithValue("@is_restricted", isRestricted ? 1 : 0);
        await command.ExecuteNonQueryAsync();

        await AddAlertAsync(null, "ADMIN_ACTION", $"Created or updated Event Profile '{eventName}' ({eventId}).");
    }



    public async Task RemoveEventAttendeeAsync(string eventId, string studentId)
    {
        using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();

        using var command = new MySqlCommand("DELETE FROM event_approved_students WHERE event_id = @event_id AND student_id = @student_id", connection);
        command.Parameters.AddWithValue("@event_id", eventId);
        command.Parameters.AddWithValue("@student_id", studentId);
        await command.ExecuteNonQueryAsync();

        await AddAlertAsync(studentId, "ADMIN_ACTION", $"Manually removed student from event roster for '{eventId}'.");
    }

    public async Task AddBatchToEventAsync(string eventId, string? courseName, string? yearLevel)
    {
        using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();

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
            command.Parameters.AddWithValue("@year", yearLevel.Replace("Year ", "").Trim());

        await command.ExecuteNonQueryAsync();

        await AddAlertAsync(null, "ADMIN_ACTION", $"Executed batch approval for Event '{eventId}'. Filter constraints applied.");
    }

    public async Task AddEventAttendeeAsync(string eventId, string studentId)
    {
        using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();

        using var command = new MySqlCommand(@"
            INSERT IGNORE INTO event_approved_students (event_id, student_id)
            VALUES (@event_id, @student_id)", connection);
        command.Parameters.AddWithValue("@event_id", eventId);
        command.Parameters.AddWithValue("@student_id", studentId);
        await command.ExecuteNonQueryAsync();

        await AddAlertAsync(studentId, "ADMIN_ACTION", $"Manually approved student for Event '{eventId}'.");
    }

    public async Task<IReadOnlyList<StudentRecord>> GetEventAttendeesAsync(string eventId)
    {
        using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();

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

        using var checkEventCmd = new MySqlCommand("SELECT is_restricted FROM events WHERE event_id = @event_id", connection);
        checkEventCmd.Parameters.AddWithValue("@event_id", eventId);
        var isRestrictedObj = await checkEventCmd.ExecuteScalarAsync();

        bool isRestricted = isRestrictedObj != DBNull.Value && Convert.ToBoolean(isRestrictedObj);

        if (!isRestricted) return true;

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

        using var closeEventCmd = new MySqlCommand("UPDATE events SET is_active = FALSE WHERE event_id = @event_id;", connection);
        closeEventCmd.Parameters.AddWithValue("@event_id", eventId);
        await closeEventCmd.ExecuteNonQueryAsync();

        await AddAlertAsync(null, "ADMIN_ACTION", $"Closed Event Profile '{eventId}'. It was removed from active scanning.");
    }

    public async Task<IReadOnlyList<EventRecord>> GetAllEventsAsync()
    {
        using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();

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
                Status = "Active",
                EventDate = reader["event_date"] != DBNull.Value ? Convert.ToDateTime(reader["event_date"]) : null
            });
        }
        return events;
    }

    public async Task RegisterStaffAsync(string uid, string fullName, string role)
    {
        using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();

        using var command = new MySqlCommand(@"
            INSERT INTO staff (nfc_uid, full_name, role)
            VALUES (@uid, @name, @role)
            ON DUPLICATE KEY UPDATE full_name = @name, role = @role", connection);

        command.Parameters.AddWithValue("@uid", uid);
        command.Parameters.AddWithValue("@name", fullName);
        command.Parameters.AddWithValue("@role", role);

        await command.ExecuteNonQueryAsync();
    }

    public async Task<int> BatchUpdateStudentStatusAsync(string? course, string? yearLevel, string newStatus)
    {
        using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();

        var whereClauses = new List<string>();

        if (!string.IsNullOrWhiteSpace(course) && course != "All Courses")
            whereClauses.Add("course = @course");

        if (!string.IsNullOrWhiteSpace(yearLevel) && yearLevel != "All Years")
        {
            if (yearLevel == "5+")
            {
                whereClauses.Add("year_level >= 5");
            }
            else
            {
                whereClauses.Add("year_level = @year");
            }
        }

        string whereSql = whereClauses.Count > 0 ? "WHERE " + string.Join(" AND ", whereClauses) : "";

        if (string.IsNullOrEmpty(whereSql))
            throw new InvalidOperationException("You must select at least one filter (Course or Year Level) to perform a batch update.");

        string sql = $"UPDATE students SET status = @status {whereSql}";
        using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@status", newStatus);

        if (!string.IsNullOrWhiteSpace(course) && course != "All Courses")
            command.Parameters.AddWithValue("@course", course);

        if (!string.IsNullOrWhiteSpace(yearLevel) && yearLevel != "All Years" && yearLevel != "5+")
            command.Parameters.AddWithValue("@year", yearLevel.Replace("Year ", "").Trim());

        int rowsAffected = await command.ExecuteNonQueryAsync();

        if (rowsAffected > 0)
        {
            await AddAlertAsync(null, "ADMIN_ACTION", $"Batch updated {rowsAffected} students to '{newStatus}' (Course: {course ?? "All"}, Year: {yearLevel ?? "All"}).");
        }

        return rowsAffected;
    }

    public async Task<string?> GetStaffRoleAsync(string uid)
    {
        using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();

        using var command = new MySqlCommand("SELECT role FROM staff WHERE nfc_uid = @uid LIMIT 1", connection);
        command.Parameters.AddWithValue("@uid", uid);

        var result = await command.ExecuteScalarAsync();
        return result?.ToString();
    }

    public async Task<(string? Role, string? FullName)> GetStaffDetailsAsync(string uid)
    {
        using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();

        using var command = new MySqlCommand("SELECT role, full_name FROM staff WHERE nfc_uid = @uid LIMIT 1", connection);
        command.Parameters.AddWithValue("@uid", uid);

        using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return (reader["role"]?.ToString(), reader["full_name"]?.ToString());
        }
        return (null, null);
    }

    public async Task<IReadOnlyList<AttendanceLog>> GetEventAttendanceLogsAsync(string eventId)
    {
        using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();

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
                Status = Value(reader["status"])
            });
        }
        return list;
    }

    private static object NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;
    private static string Value(object? value) => value == null || value == DBNull.Value ? "" : value.ToString() ?? "";
    public static string ToStorageValue(VerificationMode mode) => mode switch { VerificationMode.Fast => "Fast", VerificationMode.Standard => "Standard", VerificationMode.HighSecurity => "HighSecurity", _ => "Standard" };
    public static string ToStorageValue(TransactionType type) => type switch { TransactionType.Entry => "Entry", TransactionType.Exit => "Exit", TransactionType.EventAttendance => "EventAttendance", _ => "Entry" };
}