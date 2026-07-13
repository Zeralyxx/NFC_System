using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.IO.Ports;
using System.Linq;
using WinRT.Interop;

namespace NFC_System
{
    public sealed partial class RegistrationWindow : Window
    {
        private SerialPort? _serialPort;
        private bool _isScanning = false;
        private readonly DatabaseService _database = new();

        public RegistrationWindow()
        {
            this.InitializeComponent();
            MaximizeWindow();
            this.Closed += Window_Closed;

            ScanUidButton.Click += ScanUidButton_Click;
            ClearButton.Click += ClearButton_Click;
            SaveButton.Click += SaveButton_Click;

            _ = InitializeDatabaseAsync();
            TryConnectSerial("COM3"); // CHANGE to your actual port
        }

        private async System.Threading.Tasks.Task InitializeDatabaseAsync()
        {
            try
            {
                await _database.EnsureSchemaAsync();
                UidLogListView.Items.Insert(0, "[INFO] Risk-based verification schema ready.");
            }
            catch (Exception ex)
            {
                UidLogListView.Items.Insert(0, $"[DB ERROR] {ex.Message}");
            }
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            CloseSerialPort();

            var dashboard = new MainWindow();
            dashboard.Activate();
            this.Close();
        }

        private void MaximizeWindow()
        {
            IntPtr hWnd = WindowNative.GetWindowHandle(this);
            WindowId windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
            AppWindow appWindow = AppWindow.GetFromWindowId(windowId);

            if (appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.Maximize();
            }
        }

        private void Window_Closed(object sender, WindowEventArgs args)
        {
            CloseSerialPort();
        }

        private bool IsInvalidUid(string uid)
        {
            if (string.IsNullOrWhiteSpace(uid))
                return true;

            string[] parts = uid.Split(':');

            // Accept only 4-byte or 7-byte style UIDs
            if (parts.Length != 4 && parts.Length != 7)
                return true;

            // All-zero UID
            bool allZero = true;
            foreach (string part in parts)
            {
                if (part != "00")
                {
                    allZero = false;
                    break;
                }
            }

            if (allZero)
                return true;

            // If the last 4 parts are all zero, treat as corrupted read
            if (parts.Length >= 4)
            {
                int start = parts.Length - 4;
                bool trailingZeros = true;

                for (int i = start; i < parts.Length; i++)
                {
                    if (parts[i] != "00")
                    {
                        trailingZeros = false;
                        break;
                    }
                }

                if (trailingZeros)
                    return true;
            }

            return false;
        }

        private void TryConnectSerial(string portName)
        {
            try
            {
                _serialPort = new SerialPort(portName, 115200);
                _serialPort.NewLine = "\n";
                _serialPort.DataReceived += SerialPort_DataReceived;
                _serialPort.Open();

                UidLogListView.Items.Add($"[INFO] Connected to {portName}");
            }
            catch (Exception ex)
            {
                UidLogListView.Items.Add($"[ERROR] Could not connect: {ex.Message}");
            }
        }

        private async void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                if (_serialPort == null || !_serialPort.IsOpen) return;

                string line = _serialPort.ReadLine().Trim();

                if (_isScanning && line.StartsWith("UID="))
                {
                    string uid = line.Substring(4).Trim();
                    bool invalidUid = IsInvalidUid(uid);

                    await DispatcherQueue.TryEnqueueAsync(() =>
                    {
                        if (invalidUid)
                        {
                            NfcUidTextBox.Text = "";
                            UidLogListView.Items.Insert(0, "[WARNING] Invalid UID detected. Please scan again.");

                            PreviewTextBlock.Text =
                                $"Student ID: {StudentIdTextBox.Text}\n" +
                                $"Full Name: {FullNameTextBox.Text}\n" +
                                $"Course: {CourseTextBox.Text}\n" +
                                $"Year Level: {YearLevelTextBox.Text}\n" +
                                $"Section: {SectionTextBox.Text}\n" +
                                $"NFC UID: Invalid read - please tap again";
                        }
                        else
                        {
                            NfcUidTextBox.Text = uid;
                            UidLogListView.Items.Insert(0, $"Scanned UID: {uid}");

                            PreviewTextBlock.Text =
                                $"Student ID: {StudentIdTextBox.Text}\n" +
                                $"Full Name: {FullNameTextBox.Text}\n" +
                                $"Course: {CourseTextBox.Text}\n" +
                                $"Year Level: {YearLevelTextBox.Text}\n" +
                                $"Section: {SectionTextBox.Text}\n" +
                                $"NFC UID: {uid}\n" +
                                $"QR Credential: {BuildQrCredential(StudentIdTextBox.Text.Trim(), uid)}";
                            QrCredentialTextBox.Text = BuildQrCredential(StudentIdTextBox.Text.Trim(), uid);
                        }
                    });

                    _isScanning = false;
                }
            }
            catch (Exception ex)
            {
                await DispatcherQueue.TryEnqueueAsync(() =>
                {
                    UidLogListView.Items.Insert(0, $"[ERROR] {ex.Message}");
                });
            }
        }



        private void ScanUidButton_Click(object sender, RoutedEventArgs e)
        {
            _isScanning = true;
            UidLogListView.Items.Insert(0, "[INFO] Waiting for NFC tap...");
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            ClearForm();
            PreviewTextBlock.Text = "Student information will appear here.";
        }

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            string studentId = StudentIdTextBox.Text.Trim();
            string fullName = FullNameTextBox.Text.Trim();
            string course = CourseTextBox.Text.Trim();
            string yearLevel = YearLevelTextBox.Text.Trim();
            string section = SectionTextBox.Text.Trim();
            string nfcUid = NfcUidTextBox.Text.Trim();
            string pin = PinPasswordBox.Password.Trim();
            string qrCredential = QrCredentialTextBox.Text.Trim();

            string status = "Active";
            if (StatusComboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                status = selectedItem.Content?.ToString() ?? "Active";
            }

            if (string.IsNullOrWhiteSpace(studentId) ||
                string.IsNullOrWhiteSpace(fullName) ||
                string.IsNullOrWhiteSpace(nfcUid))
            {
                UidLogListView.Items.Insert(0, "[ERROR] Student ID, Full Name, and NFC UID are required.");
                return;
            }

            if (pin.Length != 4 || !pin.All(char.IsDigit))
            {
                UidLogListView.Items.Insert(0, "[ERROR] A 4-digit PIN is required for two-factor authentication.");
                return;
            }

            if (IsInvalidUid(nfcUid))
            {
                UidLogListView.Items.Insert(0, "[ERROR] Invalid NFC UID. Please scan again.");
                return;
            }

            if (string.IsNullOrWhiteSpace(qrCredential))
            {
                qrCredential = BuildQrCredential(studentId, nfcUid);
                QrCredentialTextBox.Text = qrCredential;
            }

            try
            {
                var student = new StudentRecord
                {
                    StudentId = studentId,
                    FullName = fullName,
                    Course = course,
                    YearLevel = yearLevel,
                    SectionName = section,
                    Status = status,
                    NfcUid = nfcUid,
                    QrCredential = qrCredential
                };

                await _database.SaveStudentAsync(student, pin);
                UidLogListView.Items.Insert(0, $"[SUCCESS] Student saved with PIN + QR credential: {fullName}");

                PreviewTextBlock.Text =
                    $"Student ID: {studentId}\n" +
                    $"Full Name: {fullName}\n" +
                    $"Course: {course}\n" +
                    $"Year Level: {yearLevel}\n" +
                    $"Section: {section}\n" +
                    $"Status: {status}\n" +
                    $"NFC UID: {nfcUid}\n" +
                    $"QR Credential: {qrCredential}\n" +
                    $"PIN: Stored as secure hash";

                ClearForm();
            }
            catch (Exception ex)
            {
                UidLogListView.Items.Insert(0, $"[ERROR] {ex.Message}");
            }
        }

        private void ClearForm()
        {
            StudentIdTextBox.Text = "";
            FullNameTextBox.Text = "";
            CourseTextBox.Text = "";
            YearLevelTextBox.Text = "";
            SectionTextBox.Text = "";
            NfcUidTextBox.Text = "";
            PinPasswordBox.Password = "";
            QrCredentialTextBox.Text = "";
            StatusComboBox.SelectedIndex = 0;
        }

        private static string BuildQrCredential(string studentId, string nfcUid)
        {
            if (string.IsNullOrWhiteSpace(studentId) || string.IsNullOrWhiteSpace(nfcUid))
            {
                return "";
            }

            return $"TCU|{studentId}|{nfcUid}";
        }

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
            catch
            {
                // Serial cleanup should not block closing the window.
            }
        }
    }

    
}
