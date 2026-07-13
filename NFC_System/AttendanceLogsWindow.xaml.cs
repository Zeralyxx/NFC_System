using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using MySqlConnector;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace NFC_System
{
    // ── Data model for each log row ────────────────────────────────────────
    public class LogRow
    {
        public string TapTime          { get; set; } = "";
        public string StudentId        { get; set; } = "";
        public string FullName         { get; set; } = "";
        public string CourseYearSec    { get; set; } = "";
        public string AttendanceStatus { get; set; } = "";
        public string Remarks          { get; set; } = "";
        public string StatusColor      { get; set; } = "#555555";
    }

    /// <summary>
    /// Universal log viewer.
    /// Pass either (eventId, eventName) for event attendance logs,
    /// or (sessionId, sessionLabel, isVerification:true) for verification-day logs.
    /// </summary>
    public sealed partial class AttendanceLogsWindow : Window
    {
        private readonly string _connectionString =
            "Server=127.0.0.1;Port=3306;Database=nfc_system;User ID=root;Password=;";

        private readonly int    _id;
        private readonly string _label;
        private readonly bool   _isVerification;   // false = event, true = verification session

        private readonly List<LogRow> _rows = new();

        // ── Event attendance logs constructor ──────────────────────────
        public AttendanceLogsWindow(int eventId, string eventName)
        {
            InitializeComponent();
            _id             = eventId;
            _label          = eventName;
            _isVerification = false;

            SetupWindow();
            _ = LoadAsync();
        }

        // ── Verification session logs constructor ──────────────────────
        public AttendanceLogsWindow(int sessionId, string sessionLabel, bool isVerification)
        {
            InitializeComponent();
            _id             = sessionId;
            _label          = sessionLabel;
            _isVerification = isVerification;

            SetupWindow();
            _ = LoadAsync();
        }

        private void SetupWindow()
        {
            MaximizeWindow();
            TitleTextBlock.Text    = _isVerification ? "Verification Session Logs" : "Event Attendance Logs";
            SubtitleTextBlock.Text = _label;
        }

        // ── Load data ──────────────────────────────────────────────────

        private async System.Threading.Tasks.Task LoadAsync()
        {
            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                string sql = _isVerification
                    ? @"SELECT s.student_id, s.full_name, s.course, s.year_level, s.section_name,
                               sl.scan_timestamp AS tap_time,
                               sl.result         AS attendance_status,
                               ''                AS remarks
                        FROM scan_logs sl
                        LEFT JOIN students s ON sl.student_id = s.student_id
                        WHERE sl.verification_session_id = @id
                        ORDER BY sl.scan_timestamp DESC"
                    : @"SELECT s.student_id, s.full_name, s.course, s.year_level, s.section_name,
                               a.tap_time, a.attendance_status, a.remarks
                        FROM attendance a
                        JOIN students s ON a.student_id = s.student_id
                        WHERE a.event_id = @id
                        ORDER BY a.tap_time DESC";

                using var cmd = new MySqlCommand(sql, connection);
                cmd.Parameters.AddWithValue("@id", _id);
                using var reader = await cmd.ExecuteReaderAsync();

                _rows.Clear();
                int granted = 0, denied = 0;
                var uniqueIds = new System.Collections.Generic.HashSet<string>();

                while (await reader.ReadAsync())
                {
                    string sid    = reader["student_id"]?.ToString() ?? "—";
                    string name   = reader["full_name"]?.ToString()  ?? "Unknown";
                    string course = reader["course"]?.ToString()     ?? "";
                    string yr     = reader["year_level"]?.ToString() ?? "";
                    string sec    = reader["section_name"]?.ToString() ?? "";
                    string status = reader["attendance_status"]?.ToString() ?? "";
                    string rem    = reader["remarks"]?.ToString()    ?? "";

                    DateTime tapDt = Convert.ToDateTime(reader["tap_time"]);
                    string tap     = tapDt.ToString("yyyy-MM-dd hh:mm:ss tt");

                    string color = status.Equals("Present", StringComparison.OrdinalIgnoreCase)
                                || status.Equals("Granted", StringComparison.OrdinalIgnoreCase)
                        ? "#1E7B34" : "#9B1C1C";

                    if (color == "#1E7B34") granted++;
                    else                    denied++;
                    if (sid != "—") uniqueIds.Add(sid);

                    _rows.Add(new LogRow
                    {
                        TapTime          = tap,
                        StudentId        = sid,
                        FullName         = name,
                        CourseYearSec    = $"{course} {yr}-{sec}".Trim(),
                        AttendanceStatus = status.ToUpper(),
                        Remarks          = rem,
                        StatusColor      = color
                    });
                }

                // Push to UI thread
                DispatcherQueue.TryEnqueue(() =>
                {
                    LogsListView.ItemsSource   = _rows;
                    StatTotalTextBlock.Text     = _rows.Count.ToString();
                    StatGrantedTextBlock.Text   = granted.ToString();
                    StatDeniedTextBlock.Text    = denied.ToString();
                    StatUniqueTextBlock.Text    = uniqueIds.Count.ToString();
                    FooterTextBlock.Text        = $"{_rows.Count} record(s) loaded — {DateTime.Now:hh:mm:ss tt}";
                });
            }
            catch (Exception ex)
            {
                DispatcherQueue.TryEnqueue(() =>
                    SubtitleTextBlock.Text = $"Error: {ex.Message}");
            }
        }

        // ── CSV export ─────────────────────────────────────────────────

        private async void ExportCsvButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var picker = new FileSavePicker();
                picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
                picker.FileTypeChoices.Add("CSV File", new List<string> { ".csv" });
                picker.SuggestedFileName = $"logs_{_label.Replace(" ", "_")}_{DateTime.Now:yyyyMMdd_HHmm}";
                InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));

                StorageFile? file = await picker.PickSaveFileAsync();
                if (file == null) return;

                var sb = new StringBuilder();
                sb.AppendLine("Tap Time,Student ID,Full Name,Course/Year/Section,Status,Remarks");
                foreach (var r in _rows)
                    sb.AppendLine($"\"{r.TapTime}\",\"{r.StudentId}\",\"{r.FullName}\",\"{r.CourseYearSec}\",\"{r.AttendanceStatus}\",\"{r.Remarks}\"");

                await FileIO.WriteTextAsync(file, sb.ToString());
                FooterTextBlock.Text = $"Exported {_rows.Count} record(s) to {file.Name}";
            }
            catch (Exception ex)
            {
                FooterTextBlock.Text = $"Export failed: {ex.Message}";
            }
        }

        // ── Close ──────────────────────────────────────────────────────

        private void CloseButton_Click(object sender, RoutedEventArgs e) => this.Close();

        // ── Window maximize ────────────────────────────────────────────

        private void MaximizeWindow()
        {
            IntPtr hWnd = WindowNative.GetWindowHandle(this);
            WindowId windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
            AppWindow appWindow = AppWindow.GetFromWindowId(windowId);
            if (appWindow.Presenter is OverlappedPresenter presenter)
                presenter.Maximize();
        }
    }
}
