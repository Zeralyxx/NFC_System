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
    public sealed partial class VerificationWindow : Window
    {
        private SerialPort? _serialPort;

        private readonly string _connectionString =
            "Server=127.0.0.1;Port=3306;Database=nfc_system;User ID=root;Password=;";

        // ─── Session context (optional) ────────────────────────────────
        // When opened from VerificationManagementWindow a session is active;
        // scans are tagged with the session id in scan_logs.
        // When opened standalone (from MainWindow) sessionId = -1.

        private readonly int _sessionId;
        private readonly string _sessionLabel;
        private readonly bool _hasSession;

        // ─── Pending-decision state ────────────────────────────────────

        private string? _pendingStudentId = null;
        private string? _pendingFullName = null;
        private string? _pendingStatus = null;
        private string? _pendingUid = null;
        private string? _pendingScanTime = null;

        // ─── Constructors ──────────────────────────────────────────────

        /// <summary>Standalone — opened directly from the dashboard.</summary>
        public VerificationWindow()
        {
            InitializeComponent();
            _sessionId = -1;
            _sessionLabel = "";
            _hasSession = false;

            SharedInit();
        }

        /// <summary>Session-aware — opened from VerificationManagementWindow.</summary>
        public VerificationWindow(int sessionId, string sessionLabel)
        {
            InitializeComponent();
            _sessionId = sessionId;
            _sessionLabel = sessionLabel;
            _hasSession = true;

            SharedInit();
        }

        private void SharedInit()
        {
            MaximizeWindow();
            this.Closed += Window_Closed;
            SetDecisionButtonsEnabled(false);

            // Show session context in header if available
            if (_hasSession)
            {
                // SubHeaderTextBlock is defined in the XAML; update it if the session is set
                try { SubHeaderTextBlock.Text = $"Session: {_sessionLabel}"; } catch { }
            }

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

            if (!_hasSession)
            {
                // Opened standalone from dashboard — go back to MainWindow
                var dashboard = new MainWindow();
                dashboard.Activate();
            }

            // If session-aware, the VerificationManagementWindow is already open behind us
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
                VerificationLogListView.Items.Insert(0, $"[INFO] Connected to {portName}");
            }
            catch (Exception ex)
            {
                VerificationLogListView.Items.Insert(0, $"[ERROR] Could not connect: {ex.Message}");
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
                        string scanTime = DateTime.Now.ToString("yyyy-MM-dd hh:mm:ss tt");
                        ClearStudentDisplay();
                        ClearPendingState();
                        UidTextBlock.Text = uid;
                        ScanTimeTextBlock.Text = scanTime;
                        StatusTextBlock.Text = "INVALID UID";

                        StatusBadge.Background = new SolidColorBrush(Colors.DarkOrange);
                        StatusTextBlock.Foreground = new SolidColorBrush(Colors.White);
                        ResultTextBlock.Text = "SCAN AGAIN";
                        ResultSubTextBlock.Text = "Invalid NFC read detected";
                        ResultBorder.Background = new SolidColorBrush(Colors.DarkOrange);

                        VerificationLogListView.Items.Insert(0,
                            $"{scanTime}  |  UID {uid}  |  INVALID READ");
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
                    VerificationLogListView.Items.Insert(0, $"[ERROR] {ex.Message}"));
            }
        }

        // ─── Load student ──────────────────────────────────────────────

        private async System.Threading.Tasks.Task LoadStudentByUid(string uid)
        {
            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                string query = @"
                    SELECT student_id, full_name, course, year_level,
                           section_name, status, photo_path
                    FROM   students
                    WHERE  nfc_uid = @uid
                    LIMIT  1";

                using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@uid", uid);
                using var reader = await command.ExecuteReaderAsync();

                string scanTime = DateTime.Now.ToString("yyyy-MM-dd hh:mm:ss tt");

                if (await reader.ReadAsync())
                {
                    string studentId = reader["student_id"]?.ToString() ?? "-";
                    string fullName = reader["full_name"]?.ToString() ?? "-";
                    string course = reader["course"]?.ToString() ?? "-";
                    string yearLevel = reader["year_level"]?.ToString() ?? "-";
                    string section = reader["section_name"]?.ToString() ?? "-";
                    string status = reader["status"]?.ToString() ?? "-";
                    string photoPath = reader["photo_path"]?.ToString() ?? "";

                    _pendingStudentId = studentId;
                    _pendingFullName = fullName;
                    _pendingStatus = status;
                    _pendingUid = uid;
                    _pendingScanTime = scanTime;

                    StudentNameTextBlock.Text = fullName;
                    StudentIdTextBlock.Text = studentId;
                    CourseTextBlock.Text = course;
                    YearLevelTextBlock.Text = yearLevel;
                    SectionTextBlock.Text = section;
                    StatusTextBlock.Text = status.ToUpper();
                    UidTextBlock.Text = uid;
                    ScanTimeTextBlock.Text = scanTime;

                    if (!string.IsNullOrWhiteSpace(photoPath) && File.Exists(photoPath))
                        StudentPhotoImage.Source = new BitmapImage(new Uri(photoPath));
                    else
                        StudentPhotoImage.Source = null;

                    if (status.Equals("Active", StringComparison.OrdinalIgnoreCase))
                    {
                        StatusBadge.Background = new SolidColorBrush(Colors.ForestGreen);
                        StatusTextBlock.Foreground = new SolidColorBrush(Colors.White);
                        ResultTextBlock.Text = "AWAITING DECISION";
                        ResultSubTextBlock.Text = "Student is active — press GRANT or DENY";
                        ResultBorder.Background = new SolidColorBrush(Colors.SteelBlue);
                    }
                    else if (status.Equals("Inactive", StringComparison.OrdinalIgnoreCase))
                    {
                        StatusBadge.Background = new SolidColorBrush(Colors.DarkOrange);
                        StatusTextBlock.Foreground = new SolidColorBrush(Colors.White);
                        ResultTextBlock.Text = "AWAITING DECISION";
                        ResultSubTextBlock.Text = "Student is inactive — press GRANT or DENY";
                        ResultBorder.Background = new SolidColorBrush(Colors.SteelBlue);
                    }
                    else
                    {
                        StatusBadge.Background = new SolidColorBrush(Colors.Firebrick);
                        StatusTextBlock.Foreground = new SolidColorBrush(Colors.White);
                        ResultTextBlock.Text = "AWAITING DECISION";
                        ResultSubTextBlock.Text = $"Student status: {status} — press GRANT or DENY";
                        ResultBorder.Background = new SolidColorBrush(Colors.SteelBlue);
                    }

                    SetDecisionButtonsEnabled(true);
                }
                else
                {
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

                    VerificationLogListView.Items.Insert(0,
                        $"{scanTime}  |  UID {uid}  |  NOT REGISTERED");

                    await WriteScanLogAsync(null, scanTime, "Denied");
                }
            }
            catch (Exception ex)
            {
                VerificationLogListView.Items.Insert(0, $"[DB ERROR] {ex.Message}");
            }
        }

        // ─── Grant ─────────────────────────────────────────────────────

        private async void GrantButton_Click(object sender, RoutedEventArgs e)
        {
            if (_pendingStudentId == null) return;

            string studentId = _pendingStudentId;
            string fullName = _pendingFullName ?? "-";
            string scanTime = _pendingScanTime ?? DateTime.Now.ToString("yyyy-MM-dd hh:mm:ss tt");

            SetDecisionButtonsEnabled(false);
            ResultTextBlock.Text = "ACCESS GRANTED";
            ResultSubTextBlock.Text = "Student granted entry by security personnel";
            ResultBorder.Background = new SolidColorBrush(Colors.ForestGreen);

            VerificationLogListView.Items.Insert(0,
                $"{scanTime}  |  {studentId}  |  {fullName}  |  GRANTED (MANUAL)");

            await WriteScanLogAsync(studentId, scanTime, "Granted");
            ClearPendingState();
        }

        // ─── Deny ──────────────────────────────────────────────────────

        private async void DenyButton_Click(object sender, RoutedEventArgs e)
        {
            if (_pendingStudentId == null) return;

            string studentId = _pendingStudentId;
            string fullName = _pendingFullName ?? "-";
            string status = _pendingStatus ?? "-";
            string scanTime = _pendingScanTime ?? DateTime.Now.ToString("yyyy-MM-dd hh:mm:ss tt");

            SetDecisionButtonsEnabled(false);
            ResultTextBlock.Text = "ACCESS DENIED";
            ResultSubTextBlock.Text = "Student denied entry by security personnel";
            ResultBorder.Background = new SolidColorBrush(Colors.Firebrick);

            VerificationLogListView.Items.Insert(0,
                $"{scanTime}  |  {studentId}  |  {fullName}  |  DENIED (MANUAL)");

            await WriteScanLogAsync(studentId, scanTime, "Denied");
            ClearPendingState();
        }

        // ─── Write scan_log (session-aware) ───────────────────────────

        private async System.Threading.Tasks.Task WriteScanLogAsync(
            string? studentId, string scanTime, string result)
        {
            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                string sql = _hasSession
                    ? @"INSERT INTO scan_logs
                            (student_id, scan_timestamp, scan_type, result, verification_session_id)
                        VALUES
                            (@student_id, @ts, 'Verification', @result, @session_id)"
                    : @"INSERT INTO scan_logs
                            (student_id, scan_timestamp, scan_type, result)
                        VALUES
                            (@student_id, @ts, 'Verification', @result)";

                using var cmd = new MySqlCommand(sql, connection);
                cmd.Parameters.AddWithValue("@student_id",
                    studentId != null ? (object)studentId : DBNull.Value);
                cmd.Parameters.AddWithValue("@ts", DateTime.Now);
                cmd.Parameters.AddWithValue("@result", result);

                if (_hasSession)
                    cmd.Parameters.AddWithValue("@session_id", _sessionId);

                await cmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                VerificationLogListView.Items.Insert(0, $"[DB ERROR] scan_logs: {ex.Message}");
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
        }

        private void ClearStudentDisplay()
        {
            StudentNameTextBlock.Text = "-";
            StudentIdTextBlock.Text = "-";
            CourseTextBlock.Text = "-";
            YearLevelTextBlock.Text = "-";
            SectionTextBlock.Text = "-";
            StatusTextBlock.Text = "-";
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
            catch { }
        }
    }
}