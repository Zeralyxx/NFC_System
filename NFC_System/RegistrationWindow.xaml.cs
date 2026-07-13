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
    public sealed partial class RegistrationWindow : Window
    {
        private SerialPort? _serialPort;
        private bool _isScanning = false;
        private string? _selectedPhotoPath = null;   // tracks photo chosen this session

        private readonly string _connectionString =
            "Server=127.0.0.1;Port=3306;Database=nfc_system;User ID=root;Password=;";

        public RegistrationWindow()
        {
            this.InitializeComponent();
            MaximizeWindow();
            this.Closed += Window_Closed;

            ScanUidButton.Click += ScanUidButton_Click;
            ClearButton.Click += ClearButton_Click;
            SaveButton.Click += SaveButton_Click;
            UploadPhotoButton.Click += UploadPhotoButton_Click;
            DeletePhotoButton.Click += DeletePhotoButton_Click;

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

        private void Window_Closed(object sender, WindowEventArgs args)
        {
            CloseSerialPort();
        }

        // ─── UID validation ────────────────────────────────────────────

        private bool IsInvalidUid(string uid)
        {
            if (string.IsNullOrWhiteSpace(uid)) return true;

            string[] parts = uid.Split(':');

            if (parts.Length != 4 && parts.Length != 7) return true;

            bool allZero = true;
            foreach (string part in parts)
                if (part != "00") { allZero = false; break; }

            if (allZero) return true;

            if (parts.Length >= 4)
            {
                int start = parts.Length - 4;
                bool trailingZeros = true;
                for (int i = start; i < parts.Length; i++)
                    if (parts[i] != "00") { trailingZeros = false; break; }
                if (trailingZeros) return true;
            }

            return false;
        }

        // ─── Status indicator helpers ──────────────────────────────────

        private void UpdateStatusIndicators(string nfcStatus, string photoStatus, string regStatus)
        {
            NfcStatusText.Text = nfcStatus;
            PhotoStatusText.Text = photoStatus;
            RegStatusText.Text = regStatus;

            // Update NFC status color
            NfcStatusText.Foreground = nfcStatus switch
            {
                "Ready" => new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0xA6, 0xE3, 0xA1)),    // Green
                "Scanned" => new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0xA6, 0xE3, 0xA1)),  // Green
                "Waiting" => new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0xF9, 0xE2, 0xAF)),  // Yellow
                "Error" => new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0xF3, 0x8B, 0xA8)),    // Red
                "Invalid" => new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0xF3, 0x8B, 0xA8)),  // Red
                _ => new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0x6C, 0x70, 0x86))           // Gray default
            };

            // Update Photo status color
            PhotoStatusText.Foreground = photoStatus switch
            {
                "Uploaded" => new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0xA6, 0xE3, 0xA1)),  // Green
                _ => new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0xF3, 0x8B, 0xA8))            // Red
            };

            // Update Registration status color
            RegStatusText.Foreground = regStatus switch
            {
                "Saved" => new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0xA6, 0xE3, 0xA1)),      // Green
                "New Student" => new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0x89, 0xB4, 0xFA)), // Blue
                "Editing" => new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0x89, 0xB4, 0xFA)),     // Blue
                "Pending" => new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0xF9, 0xE2, 0xAF)),     // Yellow
                _ => new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0x6C, 0x70, 0x86))              // Gray default
            };
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

                UidLogListView.Items.Add($"[INFO] Connected to {portName}");
                UpdateStatusIndicators("Ready", "None", "Pending");
            }
            catch (Exception ex)
            {
                UidLogListView.Items.Add($"[ERROR] Could not connect: {ex.Message}");
                UpdateStatusIndicators("Error", "None", "Pending");
            }
        }

        private async void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                if (_serialPort == null || !_serialPort.IsOpen) return;

                string line = _serialPort.ReadLine().Trim();

                if (!(_isScanning && line.StartsWith("UID="))) return;

                string uid = line.Substring(4).Trim();
                bool invalidUid = IsInvalidUid(uid);

                if (invalidUid)
                {
                    await DispatcherQueue.TryEnqueueAsync(() =>
                    {
                        NfcUidTextBox.Text = "";
                        UidLogListView.Items.Insert(0, "[WARNING] Invalid UID detected. Please scan again.");
                        UpdateStatusIndicators("Invalid", "None", "Pending");
                        UpdatePreviewCard(
                            StudentIdTextBox.Text, FullNameTextBox.Text,
                            CourseTextBox.Text, YearLevelTextBox.Text,
                            SectionTextBox.Text, "—", "Invalid read");
                    });
                    _isScanning = false;
                    return;
                }

                // ── Valid UID: query DB ────────────────────────────────
                string studentId = "", fullName = "", course = "",
                       yearLevel = "", section = "", status = "",
                       photoPath = "";
                bool found = false;

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

                    if (await reader.ReadAsync())
                    {
                        found = true;
                        studentId = reader["student_id"]?.ToString() ?? "";
                        fullName = reader["full_name"]?.ToString() ?? "";
                        course = reader["course"]?.ToString() ?? "";
                        yearLevel = reader["year_level"]?.ToString() ?? "";
                        section = reader["section_name"]?.ToString() ?? "";
                        status = reader["status"]?.ToString() ?? "Active";
                        photoPath = reader["photo_path"]?.ToString() ?? "";
                    }
                }
                catch (Exception ex)
                {
                    await DispatcherQueue.TryEnqueueAsync(() =>
                    {
                        UidLogListView.Items.Insert(0, $"[DB ERROR] {ex.Message}");
                        UpdateStatusIndicators("Error", "None", "Pending");
                    });
                    _isScanning = false;
                    return;
                }

                // ── Update UI ─────────────────────────────────────────
                await DispatcherQueue.TryEnqueueAsync(() =>
                {
                    NfcUidTextBox.Text = uid;

                    if (found)
                    {
                        StudentIdTextBox.Text = studentId;
                        FullNameTextBox.Text = fullName;
                        CourseTextBox.Text = course;
                        YearLevelTextBox.Text = yearLevel;
                        SectionTextBox.Text = section;

                        // Match status ComboBox
                        foreach (var item in StatusComboBox.Items)
                        {
                            if (item is ComboBoxItem cbi &&
                                cbi.Content?.ToString()
                                   .Equals(status, StringComparison.OrdinalIgnoreCase) == true)
                            {
                                StatusComboBox.SelectedItem = cbi;
                                break;
                            }
                        }

                        // Load photo
                        if (!string.IsNullOrWhiteSpace(photoPath) && File.Exists(photoPath))
                        {
                            StudentPhotoImage.Source = new BitmapImage(new Uri(photoPath));
                            _selectedPhotoPath = photoPath;
                            UpdateStatusIndicators("Scanned", "Uploaded", "Editing");
                        }
                        else
                        {
                            StudentPhotoImage.Source = null;
                            _selectedPhotoPath = null;
                            UpdateStatusIndicators("Scanned", "None", "Editing");
                        }

                        UidLogListView.Items.Insert(0,
                            $"[INFO] Existing student loaded: {fullName} ({studentId}) — edit fields and save to update.");

                        UpdatePreviewCard(studentId, fullName, course, yearLevel, section, status, uid);
                    }
                    else
                    {
                        StudentPhotoImage.Source = null;
                        _selectedPhotoPath = null;

                        UpdateStatusIndicators("Scanned", "None", "New Student");

                        UidLogListView.Items.Insert(0,
                            $"[INFO] New card scanned: {uid} — fill in student details.");

                        UpdatePreviewCard(
                            StudentIdTextBox.Text, FullNameTextBox.Text,
                            CourseTextBox.Text, YearLevelTextBox.Text,
                            SectionTextBox.Text, "—", uid);
                    }
                });

                _isScanning = false;
            }
            catch (Exception ex)
            {
                await DispatcherQueue.TryEnqueueAsync(() =>
                {
                    UidLogListView.Items.Insert(0, $"[ERROR] {ex.Message}");
                    UpdateStatusIndicators("Error", "None", "Pending");
                });
            }
        }

        // ─── Photo: Upload ─────────────────────────────────────────────

        private async void UploadPhotoButton_Click(object sender, RoutedEventArgs e)
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            picker.SuggestedStartLocation =
                Windows.Storage.Pickers.PickerLocationId.PicturesLibrary;
            picker.FileTypeFilter.Add(".jpg");
            picker.FileTypeFilter.Add(".jpeg");
            picker.FileTypeFilter.Add(".png");

            // WinUI 3 requires the window handle
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));

            var file = await picker.PickSingleFileAsync();
            if (file == null) return;

            try
            {
                string photosFolder = Path.Combine(AppContext.BaseDirectory, "student_photos");
                Directory.CreateDirectory(photosFolder);

                string ext = Path.GetExtension(file.Path).ToLowerInvariant();
                string sid = StudentIdTextBox.Text.Trim();
                string fileName = string.IsNullOrWhiteSpace(sid)
                                  ? $"temp_{Guid.NewGuid()}{ext}"
                                  : $"{sid}{ext}";

                string destination = Path.Combine(photosFolder, fileName);
                File.Copy(file.Path, destination, overwrite: true);

                _selectedPhotoPath = destination;
                StudentPhotoImage.Source = new BitmapImage(new Uri(destination));

                UidLogListView.Items.Insert(0, $"[INFO] Photo selected: {fileName}");
                UpdateStatusIndicators(NfcStatusText.Text, "Uploaded", RegStatusText.Text);
            }
            catch (Exception ex)
            {
                UidLogListView.Items.Insert(0, $"[ERROR] Could not load photo: {ex.Message}");
            }
        }

        // ─── Photo: Delete ─────────────────────────────────────────────

        private async void DeletePhotoButton_Click(object sender, RoutedEventArgs e)
        {
            string nfcUid = NfcUidTextBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(nfcUid))
            {
                UidLogListView.Items.Insert(0, "[ERROR] Scan the student's NFC card first.");
                return;
            }

            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                // Get current path from DB
                string selectSql = "SELECT photo_path FROM students WHERE nfc_uid = @uid LIMIT 1";
                using var selectCmd = new MySqlCommand(selectSql, connection);
                selectCmd.Parameters.AddWithValue("@uid", nfcUid);
                string? existingPath = (await selectCmd.ExecuteScalarAsync())?.ToString();

                // Delete file if it exists
                if (!string.IsNullOrWhiteSpace(existingPath) && File.Exists(existingPath))
                    File.Delete(existingPath);

                // Clear DB column
                string updateSql = "UPDATE students SET photo_path = NULL WHERE nfc_uid = @uid";
                using var updateCmd = new MySqlCommand(updateSql, connection);
                updateCmd.Parameters.AddWithValue("@uid", nfcUid);
                await updateCmd.ExecuteNonQueryAsync();

                // Clear UI
                StudentPhotoImage.Source = null;
                _selectedPhotoPath = null;

                UidLogListView.Items.Insert(0, "[INFO] Photo removed successfully.");
                UpdateStatusIndicators(NfcStatusText.Text, "None", RegStatusText.Text);
            }
            catch (Exception ex)
            {
                UidLogListView.Items.Insert(0, $"[ERROR] {ex.Message}");
            }
        }

        // ─── Scan button ───────────────────────────────────────────────

        private void ScanUidButton_Click(object sender, RoutedEventArgs e)
        {
            _isScanning = true;
            UidLogListView.Items.Insert(0, "[INFO] Waiting for NFC tap...");
            UpdateStatusIndicators("Waiting", PhotoStatusText.Text, RegStatusText.Text);
        }

        // ─── Clear ─────────────────────────────────────────────────────

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            ClearForm();
            ClearPreviewCard();
            UpdateStatusIndicators("Ready", "None", "Pending");
        }

        // ─── Save ──────────────────────────────────────────────────────

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            string studentId = StudentIdTextBox.Text.Trim();
            string fullName = FullNameTextBox.Text.Trim();
            string course = CourseTextBox.Text.Trim();
            string yearLevel = YearLevelTextBox.Text.Trim();
            string section = SectionTextBox.Text.Trim();
            string nfcUid = NfcUidTextBox.Text.Trim();

            string status = "Active";
            if (StatusComboBox.SelectedItem is ComboBoxItem selectedItem)
                status = selectedItem.Content?.ToString() ?? "Active";

            if (string.IsNullOrWhiteSpace(studentId) ||
                string.IsNullOrWhiteSpace(fullName) ||
                string.IsNullOrWhiteSpace(nfcUid))
            {
                UidLogListView.Items.Insert(0,
                    "[ERROR] Student ID, Full Name, and NFC UID are required.");
                return;
            }

            if (IsInvalidUid(nfcUid))
            {
                UidLogListView.Items.Insert(0, "[ERROR] Invalid NFC UID. Please scan again.");
                return;
            }

            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                string query = @"
                    INSERT INTO students
                        (student_id, full_name, course, year_level,
                         section_name, status, nfc_uid, photo_path)
                    VALUES
                        (@student_id, @full_name, @course, @year_level,
                         @section_name, @status, @nfc_uid, @photo_path)
                    ON DUPLICATE KEY UPDATE
                        full_name    = VALUES(full_name),
                        course       = VALUES(course),
                        year_level   = VALUES(year_level),
                        section_name = VALUES(section_name),
                        status       = VALUES(status),
                        nfc_uid      = VALUES(nfc_uid),
                        photo_path   = VALUES(photo_path)";

                using var command = new MySqlCommand(query, connection);
                command.Parameters.AddWithValue("@student_id", studentId);
                command.Parameters.AddWithValue("@full_name", fullName);
                command.Parameters.AddWithValue("@course", course);
                command.Parameters.AddWithValue("@year_level", yearLevel);
                command.Parameters.AddWithValue("@section_name", section);
                command.Parameters.AddWithValue("@status", status);
                command.Parameters.AddWithValue("@nfc_uid", nfcUid);
                command.Parameters.AddWithValue("@photo_path",
                    string.IsNullOrWhiteSpace(_selectedPhotoPath)
                        ? (object)DBNull.Value
                        : _selectedPhotoPath!);

                int rows = await command.ExecuteNonQueryAsync();

                if (rows > 0)
                {
                    UidLogListView.Items.Insert(0, $"[SUCCESS] Student saved: {fullName}");

                    UpdatePreviewCard(studentId, fullName, course, yearLevel, section, status, nfcUid);
                    UpdateStatusIndicators("Scanned", PhotoStatusText.Text, "Saved");

                    ClearForm();
                }
                else
                {
                    UidLogListView.Items.Insert(0, "[ERROR] No data was saved.");
                }
            }
            catch (MySqlException ex)
            {
                UidLogListView.Items.Insert(0, $"[DB ERROR] {ex.Message}");
            }
            catch (Exception ex)
            {
                UidLogListView.Items.Insert(0, $"[ERROR] {ex.Message}");
            }
        }

        // ─── Preview card helper ───────────────────────────────────────

        private void UpdatePreviewCard(string studentId, string fullName, string course,
                                       string yearLevel, string section, string status, string nfcUid)
        {
            PreviewFullNameTextBlock.Text = string.IsNullOrWhiteSpace(fullName) ? "—" : fullName;
            PreviewStudentIdTextBlock.Text = string.IsNullOrWhiteSpace(studentId) ? "—" : studentId;
            PreviewCourseTextBlock.Text = string.IsNullOrWhiteSpace(course) ? "—" : course;
            PreviewNfcUidTextBlock.Text = string.IsNullOrWhiteSpace(nfcUid) ? "—" : nfcUid;

            string yrSec = (string.IsNullOrWhiteSpace(yearLevel) && string.IsNullOrWhiteSpace(section))
                ? "—"
                : $"{yearLevel}th Year — {section}".Trim(' ', '—');
            PreviewYearSectionTextBlock.Text = yrSec;

            if (string.IsNullOrWhiteSpace(status) || status == "—")
            {
                PreviewStatusTextBlock.Text = "—";
                PreviewStatusBadge.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Microsoft.UI.ColorHelper.FromArgb(0xFF, 0x25, 0x25, 0x32));
                PreviewStatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Microsoft.UI.ColorHelper.FromArgb(0xFF, 0x90, 0x94, 0xA8));
            }
            else
            {
                PreviewStatusTextBlock.Text = status.ToUpper();
                var (bg, fg) = status.ToLower() switch
                {
                    "active" => (Microsoft.UI.ColorHelper.FromArgb(0xFF, 0x1A, 0x3A, 0x1A),
                                   Microsoft.UI.ColorHelper.FromArgb(0xFF, 0xA6, 0xE3, 0xA1)),
                    "inactive" => (Microsoft.UI.ColorHelper.FromArgb(0xFF, 0x2A, 0x2A, 0x1A),
                                   Microsoft.UI.ColorHelper.FromArgb(0xFF, 0xF9, 0xE2, 0xAF)),
                    _ => (Microsoft.UI.ColorHelper.FromArgb(0xFF, 0x3A, 0x1A, 0x1A),
                                   Microsoft.UI.ColorHelper.FromArgb(0xFF, 0xF3, 0x8B, 0xA8)),
                };
                PreviewStatusBadge.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(bg);
                PreviewStatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(fg);
            }
        }

        private void ClearPreviewCard()
        {
            PreviewFullNameTextBlock.Text = "—";
            PreviewStudentIdTextBlock.Text = "—";
            PreviewCourseTextBlock.Text = "—";
            PreviewYearSectionTextBlock.Text = "—";
            PreviewNfcUidTextBlock.Text = "—";
            PreviewStatusTextBlock.Text = "—";
            PreviewStatusBadge.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Microsoft.UI.ColorHelper.FromArgb(0xFF, 0x25, 0x25, 0x32));
            PreviewStatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Microsoft.UI.ColorHelper.FromArgb(0xFF, 0x90, 0x94, 0xA8));
        }

        // ─── Form helpers ──────────────────────────────────────────────

        private void ClearForm()
        {
            StudentIdTextBox.Text = "";
            FullNameTextBox.Text = "";
            CourseTextBox.Text = "";
            YearLevelTextBox.Text = "";
            SectionTextBox.Text = "";
            NfcUidTextBox.Text = "";
            StatusComboBox.SelectedIndex = 0;

            // Clear photo
            StudentPhotoImage.Source = null;
            _selectedPhotoPath = null;
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
            catch (Exception)
            {
                // suppress on shutdown
            }
        }
    }
}