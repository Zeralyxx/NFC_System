using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using MySqlConnector;
using System;
using System.IO;
using System.IO.Ports;
using WinRT.Interop;

namespace NFC_System
{
    public sealed partial class EventAttendanceWindow : Window
    {
        private SerialPort? _serialPort;

        private readonly int _eventId;
        private readonly string _eventName;

        private readonly string _connectionString =
            "Server=127.0.0.1;Port=3306;Database=nfc_system;User ID=root;Password=;";

        // ─── Pending-decision state ────────────────────────────────────
        // Populated after a successful NFC scan; cleared after Grant/Deny is pressed.

        private string? _pendingStudentId = null;
        private string? _pendingFullName = null;
        private string? _pendingStatus = null;   // raw DB status, e.g. "Active"
        private string? _pendingUid = null;
        private string? _pendingScanTime = null;
        private bool _pendingIsDuplicate = false;  // true = student already recorded

        // ──────────────────────────────────────────────────────────────

        public EventAttendanceWindow(int eventId, string eventName)
        {
            this.InitializeComponent();
            _eventId = eventId;
            _eventName = eventName;

            MaximizeWindow();
            this.Closed += Window_Closed;

            EventNameTextBlock.Text = $"Event: {_eventName}";

            // Buttons start disabled; enabled once a student is loaded
            SetDecisionButtonsEnabled(false);

            TryConnectSerial(DetectArduinoPort());
        }

        // ─── Arduino port detection ────────────────────────────────────

        private static string DetectArduinoPort()
        {
            string[] ports = SerialPort.GetPortNames();
            return ports.Length > 0 ? ports[0] : "COM3";
        }

        // ─── Navigation ────────────────────────────────────────────────

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            CloseSerialPort();
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

        private void Window_Closed(object sender, WindowEventArgs args)
        {
            CloseSerialPort();
        }

        // ─── Serial port ───────────────────────────────────────────────

        private void TryConnectSerial(string portName)
        {
            try
            {
                _serialPort = new SerialPort(portName, 115200);
                _serialPort.NewLine = "\n";
                _serialPort.DataReceived += SerialPort_DataReceived;
                _serialPort.Open();

                AttendanceLogListView.Items.Insert(0,
                    $"[INFO] Connected to {portName}. Waiting for NFC tap…");
            }
            catch (Exception ex)
            {
                AttendanceLogListView.Items.Insert(0,
                    $"[ERROR] Could not connect: {ex.Message}");
            }
        }

        private async void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                if (_serialPort == null || !_serialPort.IsOpen) return;

                string line = _serialPort.ReadLine().Trim();
                if (!line.StartsWith("UID=")) return;

                string uid = line.Substring(4).Trim();

                bool invalidUid =
                    uid == "00:00:00:00" ||
                    uid == "00:00:00:00:00:00:00" ||
                    uid.Contains(":00:00:00:00");

                await DispatcherQueue.TryEnqueueAsync(async () =>
                {
                    if (invalidUid)
                    {
                        string t = DateTime.Now.ToString("yyyy-MM-dd hh:mm:ss tt");

                        ClearStudentDisplay();
                        ClearPendingState();

                        UidTextBlock.Text = uid;
                        ScanTimeTextBlock.Text = t;
                        StatusTextBlock.Text = "INVALID UID";

                        StatusBadge.Background = new SolidColorBrush(Colors.DarkOrange);
                        StatusTextBlock.Foreground = new SolidColorBrush(Colors.White);

                        ResultTextBlock.Text = "SCAN AGAIN";
                        ResultSubTextBlock.Text = "Invalid NFC read detected";
                        ResultBorder.Background = new SolidColorBrush(Colors.DarkOrange);

                        // Invalid reads are not logged to DB — just show in UI list
                        AttendanceLogListView.Items.Insert(0,
                            $"{t}  |  UID {uid}  |  INVALID READ");
                    }
                    else
                    {
                        await LoadStudentByUid(uid);
                    }
                });
            }
            catch (Exception ex)
            {
                await DispatcherQueue.TryEnqueueAsync(() =>
                    AttendanceLogListView.Items.Insert(0, $"[ERROR] {ex.Message}"));
            }
        }

        // ─── Core: load student by UID (display only — no DB write yet) ─

        private async System.Threading.Tasks.Task LoadStudentByUid(string uid)
        {
            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                // 1. Look up student
                string selectQuery = @"
                    SELECT student_id, full_name, course, year_level,
                           section_name, status, photo_path
                    FROM   students
                    WHERE  nfc_uid = @uid
                    LIMIT  1";

                using var selectCmd = new MySqlCommand(selectQuery, connection);
                selectCmd.Parameters.AddWithValue("@uid", uid);
                using var reader = await selectCmd.ExecuteReaderAsync();

                string scanTime = DateTime.Now.ToString("yyyy-MM-dd hh:mm:ss tt");

                if (!await reader.ReadAsync())
                {
                    // ── Not registered — no decision needed, log immediately ──
                    ClearStudentDisplay();
                    ClearPendingState();

                    UidTextBlock.Text = uid;
                    ScanTimeTextBlock.Text = scanTime;
                    StatusTextBlock.Text = "NOT REGISTERED";

                    StudentPhotoImage.Source = null;

                    StatusBadge.Background = new SolidColorBrush(Colors.Gray);
                    StatusTextBlock.Foreground = new SolidColorBrush(Colors.White);

                    ResultTextBlock.Text = "ACCESS DENIED";
                    ResultSubTextBlock.Text = "Reason: NFC UID is not registered";
                    ResultBorder.Background = new SolidColorBrush(Colors.Firebrick);

                    AttendanceLogListView.Items.Insert(0,
                        $"{scanTime}  |  UID {uid}  |  NOT REGISTERED");

                    // Log unregistered scan immediately (no guard decision needed)
                    await WriteAttendanceAndScanLogAsync(null, scanTime, "Denied",
                        "Not registered", skipAttendance: true);
                    return;
                }

                string studentId = reader["student_id"]?.ToString() ?? "-";
                string fullName = reader["full_name"]?.ToString() ?? "-";
                string course = reader["course"]?.ToString() ?? "-";
                string yearLevel = reader["year_level"]?.ToString() ?? "-";
                string section = reader["section_name"]?.ToString() ?? "-";
                string status = reader["status"]?.ToString() ?? "-";
                string photoPath = reader["photo_path"]?.ToString() ?? "";

                reader.Close();

                // 2. Duplicate check
                string dupQuery = @"
                    SELECT COUNT(*) FROM attendance
                    WHERE event_id = @event_id AND student_id = @student_id";

                using var dupCmd = new MySqlCommand(dupQuery, connection);
                dupCmd.Parameters.AddWithValue("@event_id", _eventId);
                dupCmd.Parameters.AddWithValue("@student_id", studentId);
                long existing = (long)(await dupCmd.ExecuteScalarAsync() ?? 0L);

                // ── Store pending state ──
                _pendingStudentId = studentId;
                _pendingFullName = fullName;
                _pendingStatus = status;
                _pendingUid = uid;
                _pendingScanTime = scanTime;
                _pendingIsDuplicate = existing > 0;

                // ── Populate text fields ──
                StudentNameTextBlock.Text = fullName;
                StudentIdTextBlock.Text = studentId;
                CourseTextBlock.Text = course;
                YearLevelTextBlock.Text = yearLevel;
                SectionTextBlock.Text = section;
                StatusTextBlock.Text = status.ToUpper();
                UidTextBlock.Text = uid;
                ScanTimeTextBlock.Text = scanTime;

                // ── Load photo ──
                if (!string.IsNullOrWhiteSpace(photoPath) && File.Exists(photoPath))
                    StudentPhotoImage.Source = new BitmapImage(new Uri(photoPath));
                else
                    StudentPhotoImage.Source = null;

                // ── Show result banner (pending guard confirmation) ──
                if (existing > 0)
                {
                    // Already recorded — warn the guard but still let them decide
                    StatusBadge.Background = new SolidColorBrush(Colors.DarkOrange);
                    StatusTextBlock.Foreground = new SolidColorBrush(Colors.White);

                    ResultTextBlock.Text = "AWAITING DECISION";
                    ResultSubTextBlock.Text = "⚠ Already recorded — press GRANT to allow re-entry or DENY";
                    ResultBorder.Background = new SolidColorBrush(Colors.SteelBlue);
                }
                else if (status.Equals("Active", StringComparison.OrdinalIgnoreCase))
                {
                    StatusBadge.Background = new SolidColorBrush(Colors.ForestGreen);
                    StatusTextBlock.Foreground = new SolidColorBrush(Colors.White);

                    ResultTextBlock.Text = "AWAITING DECISION";
                    ResultSubTextBlock.Text = "Student is active — press GRANT or DENY";
                    ResultBorder.Background = new SolidColorBrush(Colors.SteelBlue);
                }
                else
                {
                    StatusBadge.Background = new SolidColorBrush(Colors.Firebrick);
                    StatusTextBlock.Foreground = new SolidColorBrush(Colors.White);

                    ResultTextBlock.Text = "AWAITING DECISION";
                    ResultSubTextBlock.Text = $"Student is {status} — press GRANT or DENY";
                    ResultBorder.Background = new SolidColorBrush(Colors.SteelBlue);
                }

                // Enable decision buttons
                SetDecisionButtonsEnabled(true);
            }
            catch (Exception ex)
            {
                AttendanceLogListView.Items.Insert(0, $"[DB ERROR] {ex.Message}");
            }
        }

        // ─── Grant button ──────────────────────────────────────────────

        private async void GrantButton_Click(object sender, RoutedEventArgs e)
        {
            if (_pendingStudentId == null) return;

            string studentId = _pendingStudentId;
            string fullName = _pendingFullName ?? "-";
            string scanTime = _pendingScanTime ?? DateTime.Now.ToString("yyyy-MM-dd hh:mm:ss tt");
            bool isDuplicate = _pendingIsDuplicate;

            SetDecisionButtonsEnabled(false);

            ResultTextBlock.Text = "ATTENDANCE RECORDED";
            ResultSubTextBlock.Text = isDuplicate
                ? "Guard granted re-entry (duplicate overridden)"
                : "Student admitted to event by guard";
            ResultBorder.Background = new SolidColorBrush(Colors.ForestGreen);

            AttendanceLogListView.Items.Insert(0,
                $"{scanTime}  |  {studentId}  |  {fullName}  |  GRANTED (MANUAL)"
                + (isDuplicate ? "  |  RE-ENTRY" : ""));

            string remarks = isDuplicate ? "Re-entry granted by guard" : "Admitted via guard decision";
            await WriteAttendanceAndScanLogAsync(studentId, scanTime, "Present", remarks,
                skipAttendance: isDuplicate);   // don't insert a second attendance row if duplicate

            ClearPendingState();
        }

        // ─── Deny button ───────────────────────────────────────────────

        private async void DenyButton_Click(object sender, RoutedEventArgs e)
        {
            if (_pendingStudentId == null) return;

            string studentId = _pendingStudentId;
            string fullName = _pendingFullName ?? "-";
            string status = _pendingStatus ?? "-";
            string scanTime = _pendingScanTime ?? DateTime.Now.ToString("yyyy-MM-dd hh:mm:ss tt");

            SetDecisionButtonsEnabled(false);

            ResultTextBlock.Text = "ACCESS DENIED";
            ResultSubTextBlock.Text = "Student denied entry by guard";
            ResultBorder.Background = new SolidColorBrush(Colors.Firebrick);

            AttendanceLogListView.Items.Insert(0,
                $"{scanTime}  |  {studentId}  |  {fullName}  |  DENIED (MANUAL)");

            string remarks = $"Denied by guard — student status: {status}";
            await WriteAttendanceAndScanLogAsync(studentId, scanTime, "Denied", remarks,
                skipAttendance: false);

            ClearPendingState();
        }

        // ─── Write attendance + scan_logs ──────────────────────────────

        /// <summary>
        /// Writes to scan_logs always.
        /// Writes to attendance only when skipAttendance is false
        /// (i.e. skip for duplicate rows and unregistered scans that have no student_id).
        /// </summary>
        private async System.Threading.Tasks.Task WriteAttendanceAndScanLogAsync(
            string? studentId,
            string scanTime,
            string attendanceStatus,   // "Present" | "Denied"
            string remarks,
            bool skipAttendance)
        {
            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                // ── attendance table ──────────────────────────────────
                if (!skipAttendance && studentId != null)
                {
                    string insertQuery = @"
                        INSERT INTO attendance
                            (student_id, event_id, tap_time, attendance_status, remarks)
                        VALUES
                            (@student_id, @event_id, @tap_time, @attendance_status, @remarks)";

                    using var insertCmd = new MySqlCommand(insertQuery, connection);
                    insertCmd.Parameters.AddWithValue("@student_id", studentId);
                    insertCmd.Parameters.AddWithValue("@event_id", _eventId);
                    insertCmd.Parameters.AddWithValue("@tap_time", DateTime.Now);
                    insertCmd.Parameters.AddWithValue("@attendance_status", attendanceStatus);
                    insertCmd.Parameters.AddWithValue("@remarks", remarks);
                    await insertCmd.ExecuteNonQueryAsync();
                }

                // ── scan_logs audit ───────────────────────────────────
                string scanLogQuery = @"
                    INSERT INTO scan_logs
                        (student_id, scan_timestamp, scan_type, result)
                    VALUES
                        (@student_id, @ts, 'Event', @result)";

                using var scanLogCmd = new MySqlCommand(scanLogQuery, connection);
                scanLogCmd.Parameters.AddWithValue("@student_id",
                    studentId != null ? (object)studentId : DBNull.Value);
                scanLogCmd.Parameters.AddWithValue("@ts", DateTime.Now);
                scanLogCmd.Parameters.AddWithValue("@result",
                    attendanceStatus == "Present" ? "Granted" : "Denied");
                await scanLogCmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                AttendanceLogListView.Items.Insert(0, $"[DB ERROR] {ex.Message}");
            }
        }

        // ─── UI helpers ────────────────────────────────────────────────

        private void SetDecisionButtonsEnabled(bool enabled)
        {
            GrantButton.IsEnabled = enabled;
            DenyButton.IsEnabled = enabled;
        }

        private void ClearPendingState()
        {
            _pendingStudentId = null;
            _pendingFullName = null;
            _pendingStatus = null;
            _pendingUid = null;
            _pendingScanTime = null;
            _pendingIsDuplicate = false;
        }

        private void ClearStudentDisplay()
        {
            StudentNameTextBlock.Text = "-";
            StudentIdTextBlock.Text = "-";
            CourseTextBlock.Text = "-";
            YearLevelTextBlock.Text = "-";
            SectionTextBlock.Text = "-";
            StatusTextBlock.Text = "-";
            StatusBadge.Background = new SolidColorBrush(Colors.Transparent);
            StudentPhotoImage.Source = null;
        }

        // ─── Serial port cleanup ───────────────────────────────────────

        private void CloseSerialPort()
        {
            try
            {
                if (_serialPort != null)
                {
                    if (_serialPort.IsOpen)
                    {
                        _serialPort.DataReceived -= SerialPort_DataReceived;
                        _serialPort.Close();
                    }
                    _serialPort.Dispose();
                    _serialPort = null;
                }
            }
            catch { /* suppress on shutdown */ }
        }
    }
}