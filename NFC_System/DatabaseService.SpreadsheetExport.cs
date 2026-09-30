using MySqlConnector;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NFC_System;

public sealed partial class DatabaseService
{
    public async Task<IReadOnlyList<SpreadsheetTable>> GetMetricsSpreadsheetTablesAsync()
    {
        using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();
        var tables = new List<SpreadsheetTable>();
        foreach (string table in new[] { "fast_mode_logs", "standard_mode_logs", "high_security_mode_logs" })
        {
            using var command = new MySqlCommand($@"SELECT vl.timestamp,
                COALESCE(NULLIF(vl.student_name, ''), s.full_name) AS student_name,
                vl.transaction_type, vl.verification_mode, vl.is_granted, vl.error_code,
                vl.nfc_system_ms, vl.pin_workflow_ms, vl.pin_system_ms, vl.qr_workflow_ms,
                vl.qr_system_ms, vl.total_workflow_ms, vl.total_system_ms, vl.db_query_speed_ms,
                vl.device_id, vl.device_name
                FROM {table} vl LEFT JOIN students s
                ON (s.student_id = vl.student_id OR (vl.nfc_uid != '' AND s.nfc_uid = vl.nfc_uid))
                ORDER BY vl.timestamp DESC", connection);
            using var reader = await command.ExecuteReaderAsync();
            var rows = new List<object?[]>();
            while (await reader.ReadAsync())
            {
                string action = Value(reader["transaction_type"]);
                string mode = Value(reader["verification_mode"]);
                string error = Value(reader["error_code"]);
                bool granted = Convert.ToBoolean(reader["is_granted"] == DBNull.Value ? false : reader["is_granted"]);
                bool pin = !action.Equals("Exit", StringComparison.OrdinalIgnoreCase) && (mode is "Standard" or "HighSecurity");
                bool qr = !action.Equals("Exit", StringComparison.OrdinalIgnoreCase) && mode == "HighSecurity";
                if (!granted && error is "NOT_REGISTERED" or "INACTIVE_STUDENT" or "ANTI_TAILGATING_VIOLATION" or "IRREGULAR_EXIT_SEQUENCE" or "IRREGULAR_EVENT_EXIT" or "UNAUTHORIZED_EVENT_ACCESS" or "BAD_READ" or "DOUBLE_ENTRY" or "ANTI_PROXY_VIOLATION" or "PIN_LOCKED") pin = qr = false;
                if (!granted && error == "PIN_FAILURE") qr = false;
                double Number(string column) => reader[column] == DBNull.Value ? 0 : Convert.ToDouble(reader[column]);
                rows.Add(new object?[]
                {
                    reader["timestamp"] == DBNull.Value ? null : Convert.ToDateTime(reader["timestamp"]),
                    string.IsNullOrWhiteSpace(Value(reader["student_name"])) ? "Unknown" : Value(reader["student_name"]), action, mode,
                    granted ? "GRANTED" : string.IsNullOrWhiteSpace(error) ? "DENIED" : $"DENIED [{error}]",
                    Number("nfc_system_ms"), pin ? Number("pin_workflow_ms") : "N/A (Bypassed)", pin ? Number("pin_system_ms") : "N/A (Bypassed)",
                    qr ? Number("qr_workflow_ms") : "N/A (Bypassed)", qr ? Number("qr_system_ms") : "N/A (Bypassed)",
                    Number("total_workflow_ms") > 0 ? Number("total_workflow_ms") : "N/A", Number("total_system_ms"), Number("db_query_speed_ms"),
                    Value(reader["device_id"]), ReportSpreadsheetTables.DeviceName(Value(reader["device_name"]))
                });
            }
            tables.Add(new(table, new SpreadsheetColumn[]
            {
                new("Date & Time", SpreadsheetValueKind.DateTime), new("Student Name"), new("Action"), new("Verification Flow"), new("Verdict"),
                new("NFC System Latency (ms)", SpreadsheetValueKind.Number), new("PIN User Workflow (ms)", SpreadsheetValueKind.Number),
                new("PIN System Latency (ms)", SpreadsheetValueKind.Number), new("QR User Workflow (ms)", SpreadsheetValueKind.Number),
                new("QR System Latency (ms)", SpreadsheetValueKind.Number), new("Total User Workflow Time (ms)", SpreadsheetValueKind.Number),
                new("Total System Latency (ms)", SpreadsheetValueKind.Number), new("Total DB Query Time (ms)", SpreadsheetValueKind.Number),
                new("Device ID"), new("Device Name")
            }, rows));
        }
        return tables;
    }
}
