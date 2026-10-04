using MySqlConnector;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NFC_System;

public sealed partial class DatabaseService
{
    public async Task<IReadOnlyList<DailyTrafficSummary>> GetDailyTrafficSummariesAsync()
    {
        using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();
        using var command = new MySqlCommand(ReportingQueries.DailyTraffic, connection);
        var rows = new List<DailyTrafficSummary>();
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            rows.Add(new DailyTrafficSummary { DateValue = reader.GetDateTime(0), TotalScans = reader.GetInt32(1),
                Granted = reader.GetInt32(2), Denied = reader.GetInt32(3) });
        return rows;
    }
}

public static class ReportingQueries
{
    public const string AlertExtremes = @"
        SELECT DATE_FORMAT(timestamp, '%b %d, %Y') as DateLbl,
            SUM(CASE WHEN is_granted = 0 THEN 1 ELSE 0 END) as Total
        FROM (
            SELECT timestamp, is_granted, transaction_type FROM fast_mode_logs
            UNION ALL SELECT timestamp, is_granted, transaction_type FROM standard_mode_logs
            UNION ALL SELECT timestamp, is_granted, transaction_type FROM high_security_mode_logs
        ) logs
        WHERE transaction_type != 'EventAttendance' AND timestamp >= DATE_SUB(CURDATE(), INTERVAL 29 DAY)
            AND timestamp < DATE_ADD(CURDATE(), INTERVAL 1 DAY)
        GROUP BY DATE(timestamp), DateLbl ORDER BY Total DESC, DATE(timestamp) DESC";

    // Aggregate before displaying/paging ledger rows; no student join or row cap can change these totals.
    public const string DailyTraffic = @"
        SELECT DATE(timestamp), COUNT(*), SUM(CASE WHEN is_granted = 1 THEN 1 ELSE 0 END),
            SUM(CASE WHEN is_granted = 0 THEN 1 ELSE 0 END)
        FROM (
            SELECT timestamp, is_granted, transaction_type FROM fast_mode_logs
            UNION ALL SELECT timestamp, is_granted, transaction_type FROM standard_mode_logs
            UNION ALL SELECT timestamp, is_granted, transaction_type FROM high_security_mode_logs
        ) logs WHERE transaction_type != 'EventAttendance'
        GROUP BY DATE(timestamp) ORDER BY DATE(timestamp) DESC";
}
