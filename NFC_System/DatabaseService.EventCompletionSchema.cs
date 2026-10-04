using MySqlConnector;
using System;
using System.Threading.Tasks;

namespace NFC_System;

public sealed partial class DatabaseService
{
    internal static async Task EnsureEventCompletionSchemaAsync(MySqlConnection connection)
    {
        using var command = new MySqlCommand(@"
            CREATE TABLE IF NOT EXISTS event_completion_policies (
                event_id VARCHAR(50) PRIMARY KEY, start_time DATETIME(3) NOT NULL, end_time DATETIME(3) NOT NULL,
                minimum_minutes INT NOT NULL, late_minutes INT NOT NULL, early_minutes INT NOT NULL,
                revision BIGINT NOT NULL, coverage_since DATETIME(3) NOT NULL
            ) ENGINE=InnoDB;
            CREATE TABLE IF NOT EXISTS event_completion_scans (
                transaction_id VARCHAR(32) PRIMARY KEY, event_id VARCHAR(50) NOT NULL, student_id VARCHAR(50) NOT NULL,
                event_log_id BIGINT NOT NULL, scan_time DATETIME(3) NOT NULL, status VARCHAR(50) NOT NULL,
                device_id VARCHAR(32) NOT NULL, device_sequence BIGINT NOT NULL, recovered BOOLEAN NOT NULL, legacy BOOLEAN NOT NULL,
                INDEX completion_event_student(event_id,student_id), INDEX completion_log(event_log_id)
            ) ENGINE=InnoDB;
            CREATE TABLE IF NOT EXISTS event_completion_audit (
                audit_id BIGINT AUTO_INCREMENT PRIMARY KEY, event_id VARCHAR(50) NOT NULL, student_id VARCHAR(50) NOT NULL,
                action VARCHAR(20) NOT NULL, previous_status VARCHAR(30) NOT NULL, new_status VARCHAR(30) NOT NULL,
                reason VARCHAR(1000) NOT NULL, actor VARCHAR(100) NOT NULL, actor_role VARCHAR(50) NOT NULL,
                device_id VARCHAR(32) NOT NULL, device_name VARCHAR(100) NOT NULL, reviewed_at_utc DATETIME(3) NOT NULL,
                evidence_stamp VARCHAR(64) NOT NULL, evidence_json LONGTEXT NOT NULL,
                INDEX completion_reviews(event_id,student_id,audit_id)
            ) ENGINE=InnoDB;", connection);
        await command.ExecuteNonQueryAsync();
        using var engines = new MySqlCommand(@"SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE()
            AND table_name IN ('event_completion_policies','event_completion_scans','event_completion_audit') AND engine='InnoDB'", connection);
        if (Convert.ToInt32(await engines.ExecuteScalarAsync()) != 3)
            throw new InvalidOperationException("Event completion metadata requires InnoDB.");
    }

    private static async Task InsertEventCompletionEvidenceAsync(MySqlConnection connection, MySqlTransaction transaction,
        string transactionId, string eventId, string studentId, long eventLogId, DateTime time, string status,
        string deviceId, long sequence, bool recovered, bool legacy)
    {
        using var command = new MySqlCommand(@"INSERT INTO event_completion_scans
            VALUES(@txn,@event,@student,@log,@time,@status,@device,@seq,@recovered,@legacy)", connection, transaction);
        command.Parameters.AddWithValue("@txn", transactionId);
        command.Parameters.AddWithValue("@event", eventId);
        command.Parameters.AddWithValue("@student", studentId);
        command.Parameters.AddWithValue("@log", eventLogId);
        command.Parameters.AddWithValue("@time", time);
        command.Parameters.AddWithValue("@status", status);
        command.Parameters.AddWithValue("@device", deviceId);
        command.Parameters.AddWithValue("@seq", sequence);
        command.Parameters.AddWithValue("@recovered", recovered);
        command.Parameters.AddWithValue("@legacy", legacy);
        await command.ExecuteNonQueryAsync();
    }
}
