using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using MySqlConnector;
using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using WinRT.Interop;

namespace NFC_System
{
    public sealed partial class VerificationManagementWindow : Window
    {
        private readonly string _connectionString =
            "Server=127.0.0.1;Port=3306;Database=nfc_system;User ID=root;Password=;";

        // ─── Win32 ─────────────────────────────────────────────────────
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(int dwProcessId);
        private const int SW_SHOWMAXIMIZED = 3;

        public VerificationManagementWindow()
        {
            this.InitializeComponent();
            MaximizeWindow();

            SessionsListView.SelectionChanged += async (s, e) => await RefreshScanLogPreviewAsync();

            _ = EnsureSchemaAsync();
            _ = LoadSessionsAsync();
        }

        // ─── DB schema bootstrap ───────────────────────────────────────
        // Creates the verification_sessions table if it doesn't exist yet.

        private async Task EnsureSchemaAsync()
        {
            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                string sql = @"
                    CREATE TABLE IF NOT EXISTS verification_sessions (
                        session_id      INT AUTO_INCREMENT PRIMARY KEY,
                        label           VARCHAR(200) NOT NULL,
                        location        VARCHAR(200) NOT NULL DEFAULT '',
                        session_date    DATE         NOT NULL,
                        session_status  VARCHAR(20)  NOT NULL DEFAULT 'Upcoming',
                        created_at      DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP
                    );

                    -- Add verification_session_id column to scan_logs if absent
                    ALTER TABLE scan_logs
                        ADD COLUMN IF NOT EXISTS verification_session_id INT NULL;";

                using var cmd = new MySqlCommand(sql, connection);
                await cmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                LogMessage($"[SCHEMA] {ex.Message}");
            }
        }

        // ─── Navigation ────────────────────────────────────────────────

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            var dashboard = new MainWindow();
            dashboard.Activate();
            this.Close();
        }

        // ─── Window helpers ────────────────────────────────────────────

        private void MaximizeWindow()
        {
            IntPtr hWnd = WindowNative.GetWindowHandle(this);
            WindowId windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
            AppWindow appWindow = AppWindow.GetFromWindowId(windowId);

            if (appWindow.Presenter is OverlappedPresenter presenter)
                presenter.Maximize();
        }

        // ─── Load sessions ─────────────────────────────────────────────

        private async Task LoadSessionsAsync()
        {
            int previouslySelectedId = (SessionsListView.SelectedItem as VerificationSessionItem)?.SessionId ?? -1;

            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                string query = @"
                    SELECT session_id, label, location, session_date, session_status
                    FROM verification_sessions
                    ORDER BY session_date DESC, session_id DESC";

                using var command = new MySqlCommand(query, connection);
                using var reader  = await command.ExecuteReaderAsync();

                SessionsListView.Items.Clear();

                while (await reader.ReadAsync())
                {
                    int    id       = reader.GetInt32("session_id");
                    string label    = reader["label"]?.ToString()          ?? "-";
                    string location = reader["location"]?.ToString()       ?? "-";
                    string status   = reader["session_status"]?.ToString() ?? "-";
                    string date     = Convert.ToDateTime(reader["session_date"]).ToString("yyyy-MM-dd");

                    var item = new VerificationSessionItem
                    {
                        SessionId = id,
                        Label     = label,
                        Location  = location,
                        Status    = status,
                        Date      = date,
                        Display   = $"#{id}  [{status.ToUpper()}]  {label}  |  {location}  |  {date}"
                    };

                    SessionsListView.Items.Add(item);

                    if (id == previouslySelectedId)
                        SessionsListView.SelectedItem = item;
                }

                if (SessionsListView.Items.Count == 0)
                    SessionsListView.Items.Add("No sessions found.");
            }
            catch (Exception ex)
            {
                SessionsListView.Items.Add($"[DB ERROR] {ex.Message}");
            }
        }

        // ─── Create session ────────────────────────────────────────────

        private async void CreateSessionButton_Click(object sender, RoutedEventArgs e)
        {
            string label    = SessionLabelTextBox.Text.Trim();
            string location = SessionLocationTextBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(label))
            {
                LogMessage("[ERROR] Session label is required.");
                return;
            }

            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                string query = @"
                    INSERT INTO verification_sessions (label, location, session_date, session_status)
                    VALUES (@label, @location, @date, 'Upcoming')";

                using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@label",    label);
                command.Parameters.AddWithValue("@location", string.IsNullOrWhiteSpace(location) ? "Main Gate" : location);
                command.Parameters.AddWithValue("@date",     DateTime.Today);

                int rows = await command.ExecuteNonQueryAsync();

                if (rows > 0)
                {
                    LogMessage($"[SUCCESS] Session '{label}' created.");
                    SessionLabelTextBox.Text    = "";
                    SessionLocationTextBox.Text = "";
                    await LoadSessionsAsync();
                }
            }
            catch (Exception ex)
            {
                LogMessage($"[DB ERROR] {ex.Message}");
            }
        }

        // ─── Start session ─────────────────────────────────────────────

        private async void StartSessionButton_Click(object sender, RoutedEventArgs e)
        {
            if (SessionsListView.SelectedItem is not VerificationSessionItem item)
            {
                LogMessage("[ERROR] Please select a session first.");
                return;
            }

            if (item.Status.Equals("Ongoing", StringComparison.OrdinalIgnoreCase))
            {
                LogMessage("[INFO] Session is already ongoing.");
                return;
            }

            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                string query = "UPDATE verification_sessions SET session_status = 'Ongoing' WHERE session_id = @id";
                using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@id", item.SessionId);
                await command.ExecuteNonQueryAsync();

                LogMessage($"[INFO] Session '{item.Label}' (#{item.SessionId}) is now ONGOING.");
                await LoadSessionsAsync();

                OpenVerificationWindow(item.SessionId, item.Label);
            }
            catch (Exception ex)
            {
                LogMessage($"[DB ERROR] {ex.Message}");
            }
        }

        // ─── End session ───────────────────────────────────────────────

        private async void EndSessionButton_Click(object sender, RoutedEventArgs e)
        {
            if (SessionsListView.SelectedItem is not VerificationSessionItem item)
            {
                LogMessage("[ERROR] Please select a session first.");
                return;
            }

            if (!item.Status.Equals("Ongoing", StringComparison.OrdinalIgnoreCase))
            {
                LogMessage("[ERROR] Only ongoing sessions can be ended.");
                return;
            }

            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                string query = "UPDATE verification_sessions SET session_status = 'Completed' WHERE session_id = @id";
                using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@id", item.SessionId);
                await command.ExecuteNonQueryAsync();

                LogMessage($"[INFO] Session '{item.Label}' (#{item.SessionId}) marked as COMPLETED.");
                await LoadSessionsAsync();
            }
            catch (Exception ex)
            {
                LogMessage($"[DB ERROR] {ex.Message}");
            }
        }

        // ─── View logs (opens universal window) ───────────────────────

        private void ViewLogsButton_Click(object sender, RoutedEventArgs e)
        {
            if (SessionsListView.SelectedItem is not VerificationSessionItem item)
            {
                LogMessage("[ERROR] Please select a session first.");
                return;
            }

            var logsWin = new AttendanceLogsWindow(item.SessionId, $"Session #{item.SessionId} — {item.Label}", isVerification: true);
            logsWin.Activate();
            LogMessage($"[INFO] Opened logs for session '{item.Label}'.");
        }

        // ─── Double-tap: reopen ongoing session ────────────────────────

        private void SessionsListView_DoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
        {
            VerificationSessionItem? item = SessionsListView.SelectedItem as VerificationSessionItem;

            if (item == null)
            {
                DependencyObject? obj = e.OriginalSource as DependencyObject;
                while (obj != null)
                {
                    if (obj is FrameworkElement fe && fe.DataContext is VerificationSessionItem ctx)
                    {
                        item = ctx;
                        SessionsListView.SelectedItem = item;
                        break;
                    }
                    obj = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(obj);
                }
            }

            if (item == null) return;

            if (!item.Status.Equals("Ongoing", StringComparison.OrdinalIgnoreCase))
            {
                LogMessage($"[INFO] '{item.Label}' is not ongoing — double-click only reopens an active session.");
                return;
            }

            LogMessage($"[INFO] Reopening verification window for '{item.Label}' (#{item.SessionId}).");
            OpenVerificationWindow(item.SessionId, item.Label);
        }

        // ─── Scan log preview ──────────────────────────────────────────

        private async Task RefreshScanLogPreviewAsync()
        {
            if (SessionsListView.SelectedItem is not VerificationSessionItem item)
            {
                ScanLogListView.Items.Clear();
                ScanLogListView.Items.Add("Select a session above to preview scans.");
                return;
            }

            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                string query = @"
                    SELECT s.student_id, s.full_name,
                           sl.scan_timestamp, sl.result
                    FROM scan_logs sl
                    LEFT JOIN students s ON sl.student_id = s.student_id
                    WHERE sl.verification_session_id = @id
                    ORDER BY sl.scan_timestamp DESC
                    LIMIT 50";

                using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@id", item.SessionId);
                using var reader = await command.ExecuteReaderAsync();

                ScanLogListView.Items.Clear();
                ScanLogListView.Items.Add($"=== Scan Preview: {item.Label} (#{item.SessionId}) ===");

                int count = 0;
                while (await reader.ReadAsync())
                {
                    string sid  = reader["student_id"]?.ToString() ?? "—";
                    string name = reader["full_name"]?.ToString()   ?? "Unknown / Not Registered";
                    string ts   = Convert.ToDateTime(reader["scan_timestamp"]).ToString("yyyy-MM-dd hh:mm:ss tt");
                    string res  = reader["result"]?.ToString()      ?? "-";

                    ScanLogListView.Items.Add($"{ts}  |  {sid}  |  {name}  |  {res.ToUpper()}");
                    count++;
                }

                if (count == 0)
                    ScanLogListView.Items.Add("No scan records for this session.");
                else
                    ScanLogListView.Items.Add($"--- Showing {count} record(s) (max 50) ---");
            }
            catch (Exception ex)
            {
                LogMessage($"[DB ERROR] {ex.Message}");
            }
        }

        // ─── Refresh ───────────────────────────────────────────────────

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            await LoadSessionsAsync();
            LogMessage("[INFO] Session list refreshed.");
        }

        // ─── Open verification window (maximized) ─────────────────────

        private void OpenVerificationWindow(int sessionId, string label)
        {
            var win = new VerificationWindow(sessionId, label);
            win.Activate();

            win.DispatcherQueue.TryEnqueue(
                Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                () =>
                {
                    IntPtr hWnd = WindowNative.GetWindowHandle(win);
                    WindowId winId   = Win32Interop.GetWindowIdFromWindow(hWnd);
                    AppWindow appWin = AppWindow.GetFromWindowId(winId);
                    if (appWin.Presenter is OverlappedPresenter presenter)
                        presenter.Maximize();

                    AllowSetForegroundWindow(-1);
                    ShowWindow(hWnd, SW_SHOWMAXIMIZED);
                    SetForegroundWindow(hWnd);
                });
        }

        // ─── Helpers ───────────────────────────────────────────────────

        private void LogMessage(string message)
        {
            ActivityLogListView.Items.Insert(0, $"[{DateTime.Now:hh:mm:ss tt}] {message}");
        }
    }

    // ── Model ──────────────────────────────────────────────────────────────

    public class VerificationSessionItem
    {
        public int    SessionId { get; set; }
        public string Label     { get; set; } = "";
        public string Location  { get; set; } = "";
        public string Status    { get; set; } = "";
        public string Date      { get; set; } = "";
        public string Display   { get; set; } = "";

        public override string ToString() => Display;
    }
}
