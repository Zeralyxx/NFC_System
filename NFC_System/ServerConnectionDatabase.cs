using MySqlConnector;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace NFC_System;

internal static class ServerConnectionDatabase
{
    internal static async Task<string> VerifyAsync(string connectionString, string expectedId, CancellationToken token)
    {
        using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync(token);
        using var identity = new MySqlCommand("SELECT setting_value FROM app_settings WHERE setting_key='lan_server_id'", connection) { CommandTimeout = 3 };
        string id = Convert.ToString(await identity.ExecuteScalarAsync(token)) ?? "";
        if (!Guid.TryParseExact(id, "N", out _) || (expectedId.Length > 0 && expectedId != id))
            throw new InvalidOperationException("The database does not match the selected main server. Open the updated app on the main device and detect it again.");
        using var staff = new MySqlCommand("SELECT COUNT(*) FROM staff", connection) { CommandTimeout = 3 };
        if (Convert.ToInt32(await staff.ExecuteScalarAsync(token)) == 0)
            throw new InvalidOperationException("Complete Master Administrator setup on the main server first.");
        return id;
    }
}
