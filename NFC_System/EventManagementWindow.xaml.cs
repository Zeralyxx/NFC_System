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
    public sealed partial class EventManagementWindow : Window
    {
        private readonly string _connectionString =
            "Server=127.0.0.1;Port=3306;Database=nfc_system;User ID=root;Password=;";

        // ─── Win32 ─────────────────────────────────────────────────────
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(int dwProcessId);

        private const int SW_SHOWMAXIMIZED = 3;

        private void OpenAttendanceWindow(int eventId, string eventName)
        {
            var win = new EventAttendanceWindow(eventId, eventName);
            win.Activate();

            win.DispatcherQueue.TryEnqueue(
                Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                () =>
                {
                    IntPtr hWnd = WindowNative.GetWindowHandle(win);
                    WindowId winId = Win32Interop.GetWindowIdFromWindow(hWnd);
                    AppWindow appWin = AppWindow.GetFromWindowId(winId);
                    if (appWin.Presenter is OverlappedPresenter presenter)
                        presenter.Maximize();

                    AllowSetForegroundWindow(-1);
                    ShowWindow(hWnd, SW_SHOWMAXIMIZED);
                    SetForegroundWindow(hWnd);
                });
        }

        public EventManagementWindow()
        {
            this.InitializeComponent();
            MaximizeWindow();

            EventsListView.SelectionChanged += async (s, e) => await RefreshAttendanceLogAsync();

            _ = LoadEventsAsync();
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

        // ─── Load events ───────────────────────────────────────────────

        private async Task LoadEventsAsync()
        {
            int previouslySelectedId = (EventsListView.SelectedItem as EventListItem)?.EventId ?? -1;

            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                string query = @"
                    SELECT event_id, event_name, location,
                           start_datetime, end_datetime, event_status
                    FROM events
                    ORDER BY start_datetime DESC";

                using var command = new MySqlCommand(query, connection);
                using var reader = await command.ExecuteReaderAsync();

                EventsListView.Items.Clear();

                while (await reader.ReadAsync())
                {
                    int eventId = reader.GetInt32("event_id");
                    string eventName = reader["event_name"]?.ToString() ?? "-";
                    string location = reader["location"]?.ToString() ?? "-";
                    string status = reader["event_status"]?.ToString() ?? "-";
                    string start = Convert.ToDateTime(reader["start_datetime"]).ToString("yyyy-MM-dd hh:mm tt");
                    string end = Convert.ToDateTime(reader["end_datetime"]).ToString("yyyy-MM-dd hh:mm tt");

                    var item = new EventListItem
                    {
                        EventId = eventId,
                        EventName = eventName,
                        Location = location,
                        Status = status,
                        Start = start,
                        End = end,
                        Display = $"#{eventId}  [{status.ToUpper()}]  {eventName}  |  {location}  |  {start} – {end}"
                    };

                    EventsListView.Items.Add(item);

                    if (eventId == previouslySelectedId)
                        EventsListView.SelectedItem = item;
                }

                if (EventsListView.Items.Count == 0)
                    EventsListView.Items.Add("No events found.");
            }
            catch (Exception ex)
            {
                EventsListView.Items.Add($"[DB ERROR] {ex.Message}");
            }
        }

        // ─── Create event ──────────────────────────────────────────────

        private async void CreateEventButton_Click(object sender, RoutedEventArgs e)
        {
            string name = EventNameTextBox.Text.Trim();
            string location = EventLocationTextBox.Text.Trim();
            string startStr = StartDateTimeTextBox.Text.Trim();
            string endStr = EndDateTimeTextBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(name) ||
                string.IsNullOrWhiteSpace(startStr) ||
                string.IsNullOrWhiteSpace(endStr))
            {
                LogMessage("[ERROR] Event name, start, and end date-time are required.");
                return;
            }

            if (!DateTime.TryParse(startStr, out DateTime startDt) ||
                !DateTime.TryParse(endStr, out DateTime endDt))
            {
                LogMessage("[ERROR] Invalid date-time format. Use: yyyy-MM-dd HH:mm");
                return;
            }

            if (endDt <= startDt)
            {
                LogMessage("[ERROR] End date-time must be after start date-time.");
                return;
            }

            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                string query = @"
                    INSERT INTO events (event_name, location, start_datetime, end_datetime, event_status)
                    VALUES (@event_name, @location, @start, @end, 'Upcoming')";

                using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@event_name", name);
                command.Parameters.AddWithValue("@location", location);
                command.Parameters.AddWithValue("@start", startDt);
                command.Parameters.AddWithValue("@end", endDt);

                int rows = await command.ExecuteNonQueryAsync();

                if (rows > 0)
                {
                    LogMessage($"[SUCCESS] Event '{name}' created.");
                    ClearEventForm();
                    await LoadEventsAsync();
                }
            }
            catch (Exception ex)
            {
                LogMessage($"[DB ERROR] {ex.Message}");
            }
        }

        // ─── Start attendance ──────────────────────────────────────────

        private async void StartAttendanceButton_Click(object sender, RoutedEventArgs e)
        {
            if (EventsListView.SelectedItem is not EventListItem item)
            {
                LogMessage("[ERROR] Please select an event first.");
                return;
            }

            if (item.Status.Equals("Ongoing", StringComparison.OrdinalIgnoreCase))
            {
                LogMessage("[INFO] Event is already ongoing.");
                return;
            }

            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                string query = "UPDATE events SET event_status = 'Ongoing' WHERE event_id = @id";
                using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@id", item.EventId);
                await command.ExecuteNonQueryAsync();

                LogMessage($"[INFO] Event '{item.EventName}' (#{item.EventId}) is now ONGOING.");
                await LoadEventsAsync();

                OpenAttendanceWindow(item.EventId, item.EventName);
            }
            catch (Exception ex)
            {
                LogMessage($"[DB ERROR] {ex.Message}");
            }
        }

        // ─── Double-click: reopen ongoing event ────────────────────────

        private void EventsListView_DoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
        {
            EventListItem? item = EventsListView.SelectedItem as EventListItem;

            if (item == null)
            {
                DependencyObject? obj = e.OriginalSource as DependencyObject;
                while (obj != null)
                {
                    if (obj is FrameworkElement fe && fe.DataContext is EventListItem ctx)
                    {
                        item = ctx;
                        EventsListView.SelectedItem = item;
                        break;
                    }
                    obj = VisualTreeHelper.GetParent(obj);
                }
            }

            if (item == null) return;

            if (!item.Status.Equals("Ongoing", StringComparison.OrdinalIgnoreCase))
            {
                LogMessage($"[INFO] '{item.EventName}' is not ongoing — double-click only reopens an active session.");
                return;
            }

            LogMessage($"[INFO] Reopening attendance window for '{item.EventName}' (#{item.EventId}).");
            OpenAttendanceWindow(item.EventId, item.EventName);
        }

        // ─── End attendance ────────────────────────────────────────────

        private async void EndAttendanceButton_Click(object sender, RoutedEventArgs e)
        {
            if (EventsListView.SelectedItem is not EventListItem item)
            {
                LogMessage("[ERROR] Please select an event first.");
                return;
            }

            if (!item.Status.Equals("Ongoing", StringComparison.OrdinalIgnoreCase))
            {
                LogMessage("[ERROR] Only ongoing events can be ended.");
                return;
            }

            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                string query = "UPDATE events SET event_status = 'Completed' WHERE event_id = @id";
                using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@id", item.EventId);
                await command.ExecuteNonQueryAsync();

                LogMessage($"[INFO] Event '{item.EventName}' (#{item.EventId}) marked as COMPLETED.");
                await LoadEventsAsync();
            }
            catch (Exception ex)
            {
                LogMessage($"[DB ERROR] {ex.Message}");
            }
        }

        // ─── View Attendance — opens universal AttendanceLogsWindow ────

        private void ViewAttendanceButton_Click(object sender, RoutedEventArgs e)
        {
            if (EventsListView.SelectedItem is not EventListItem item)
            {
                LogMessage("[ERROR] Please select an event first.");
                return;
            }

            var logsWin = new AttendanceLogsWindow(item.EventId, $"Event #{item.EventId} — {item.EventName}");
            logsWin.Activate();
            LogMessage($"[INFO] Opened attendance logs for '{item.EventName}'.");
        }

        // ─── Attendance log preview (in-window) ───────────────────────

        private async void ViewAttendancePreviewButton_Click(object sender, RoutedEventArgs e)
        {
            await RefreshAttendanceLogAsync();
        }

        private async Task RefreshAttendanceLogAsync()
        {
            if (EventsListView.SelectedItem is not EventListItem item)
            {
                AttendanceLogListView.Items.Clear();
                AttendanceLogListView.Items.Add("Select an event above to view its attendance.");
                return;
            }

            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                string query = @"
                    SELECT s.student_id, s.full_name,
                           s.course, s.year_level, s.section_name,
                           a.tap_time, a.attendance_status, a.remarks
                    FROM attendance a
                    JOIN students s ON a.student_id = s.student_id
                    WHERE a.event_id = @event_id
                    ORDER BY a.tap_time DESC";

                using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@event_id", item.EventId);
                using var reader = await command.ExecuteReaderAsync();

                AttendanceLogListView.Items.Clear();
                AttendanceLogListView.Items.Add(
                    $"=== {item.EventName}  (Event #{item.EventId}) ===");

                int count = 0;
                while (await reader.ReadAsync())
                {
                    string sid = reader["student_id"]?.ToString() ?? "-";
                    string name = reader["full_name"]?.ToString() ?? "-";
                    string course = reader["course"]?.ToString() ?? "-";
                    string yr = reader["year_level"]?.ToString() ?? "-";
                    string sec = reader["section_name"]?.ToString() ?? "-";
                    string tapTime = Convert.ToDateTime(reader["tap_time"]).ToString("yyyy-MM-dd hh:mm:ss tt");
                    string status = reader["attendance_status"]?.ToString() ?? "-";
                    string remarks = reader["remarks"]?.ToString() ?? "";

                    AttendanceLogListView.Items.Add(
                        $"{tapTime}  |  {sid}  |  {name}  |  {course} {yr}-{sec}  |  {status.ToUpper()}"
                        + (string.IsNullOrWhiteSpace(remarks) ? "" : $"  |  {remarks}"));
                    count++;
                }

                if (count == 0)
                    AttendanceLogListView.Items.Add("No attendance records for this event.");
                else
                    AttendanceLogListView.Items.Add($"--- Total: {count} record(s) ---");
            }
            catch (Exception ex)
            {
                LogMessage($"[DB ERROR] {ex.Message}");
            }
        }

        // ─── Refresh ───────────────────────────────────────────────────

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            await LoadEventsAsync();
            LogMessage("[INFO] Event list refreshed.");
        }

        // ─── Helpers ───────────────────────────────────────────────────

        private void LogMessage(string message)
        {
            EventLogListView.Items.Insert(0, $"[{DateTime.Now:hh:mm:ss tt}] {message}");
        }

        private void ClearEventForm()
        {
            EventNameTextBox.Text = "";
            EventLocationTextBox.Text = "";
            StartDateTimeTextBox.Text = "";
            EndDateTimeTextBox.Text = "";
        }
    }

    // ─── Helper model ──────────────────────────────────────────────────────

    public class EventListItem
    {
        public int EventId { get; set; }
        public string EventName { get; set; } = "";
        public string Location { get; set; } = "";
        public string Status { get; set; } = "";
        public string Start { get; set; } = "";
        public string End { get; set; } = "";
        public string Display { get; set; } = "";

        public override string ToString() => Display;
    }
}