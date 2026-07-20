using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.IO.Ports;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using WinRT.Interop;
using ZXing;
using ZXing.Common;

namespace NFC_System
{
    public sealed partial class RegistrationWindow : Window
    {
        private SerialPort? _serialPort;
        private bool _isScanning = false;
        private bool _isExistingProfile = false;
        private readonly DatabaseService _database = new();

        public RegistrationWindow()
        {
            this.InitializeComponent();
            MaximizeWindow();
            this.Closed += Window_Closed;

            ScanUidButton.Click += ScanUidButton_Click;
            ClearButton.Click += ClearButton_Click;
            SaveButton.Click += SaveButton_Click;

            // NEW: Kick off the async loader
            _ = InitializeAsync();
        }

        private async Task InitializeAsync()
        {
            // Fetch the port dynamically from the database, fallback to COM3
            string nfcPort = await _database.GetSettingAsync("nfc_com_port", "COM3");

            TryConnectSerial(nfcPort);
            UidLogListView.Items.Insert(0, $"[INFO] Ready for enrollment. Port: {nfcPort}");
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
            if (string.IsNullOrWhiteSpace(uid)) return true;
            string[] parts = uid.Split(':');
            if (parts.Length != 4 && parts.Length != 7) return true;

            bool allZero = true;
            foreach (string part in parts) { if (part != "00") { allZero = false; break; } }
            if (allZero) return true;

            if (parts.Length >= 4)
            {
                int start = parts.Length - 4;
                bool trailingZeros = true;
                for (int i = start; i < parts.Length; i++) { if (parts[i] != "00") { trailingZeros = false; break; } }
                if (trailingZeros) return true;
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
                UidLogListView.Items.Add($"[INFO] Serial stream link active on {portName}");
            }
            catch (Exception ex)
            {
                UidLogListView.Items.Add($"[ERROR] Serial terminal connection failed: {ex.Message}");
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

                    // NEW: Query the database on the background thread BEFORE updating the UI
                    StudentRecord? existingStudent = null;
                    if (!invalidUid)
                    {
                        existingStudent = await _database.GetStudentByUidAsync(uid);
                    }

                    await DispatcherQueue.TryEnqueueAsync(() =>
                    {
                        if (invalidUid)
                        {
                            NfcUidTextBox.Text = "";
                            UidLogListView.Items.Insert(0, "[WARNING] Invalid hardware read. Please scan again.");
                            PreviewTextBlock.Text = "NFC UID: Corrupted transmission layout - re-tap card";
                        }
                        else
                        {
                            NfcUidTextBox.Text = uid;

                            // NEW: If the student exists in the database, auto-fill the form!
                            if (existingStudent != null)
                            {
                                _isExistingProfile = true; // Tell the system this is an update
                                PinPasswordBox.PlaceholderText = "(Leave blank to keep PIN)"; // Helpful UI hint

                                StudentIdTextBox.Text = existingStudent.StudentId;
                                FullNameTextBox.Text = existingStudent.FullName;
                                CourseTextBox.Text = existingStudent.Course;
                                YearLevelTextBox.Text = existingStudent.YearLevel;
                                SectionTextBox.Text = existingStudent.SectionName;

                                // Match the ComboBox selection to their existing status
                                foreach (ComboBoxItem item in StatusComboBox.Items)
                                {
                                    if (item.Content?.ToString() == existingStudent.Status)
                                    {
                                        StatusComboBox.SelectedItem = item;
                                        break;
                                    }
                                }

                                UidLogListView.Items.Insert(0, $"[INFO] Existing profile loaded: {existingStudent.FullName}");
                            }
                            else
                            {
                                _isExistingProfile = false; // It's a new card
                                PinPasswordBox.PlaceholderText = "****"; // Reset placeholder
                                UidLogListView.Items.Insert(0, $"[INFO] New unassigned card scanned: {uid}");
                            }

                            // Generate the QR based on whatever the Student ID currently is
                            string currentId = StudentIdTextBox.Text.Trim();
                            string generatedQr = BuildQrCredential(currentId, uid);
                            QrCredentialTextBox.Text = generatedQr;

                            if (!string.IsNullOrEmpty(generatedQr))
                            {
                                QrCodeImage.Source = GenerateQrBitmap(generatedQr);
                                QrCodeImage.Visibility = Visibility.Visible;
                                QrPlaceholderPanel.Visibility = Visibility.Collapsed;
                            }
                            else
                            {
                                // If it's a new card and they haven't typed an ID yet, hide the QR image
                                QrCodeImage.Visibility = Visibility.Collapsed;
                                QrPlaceholderPanel.Visibility = Visibility.Visible;
                                UidLogListView.Items.Insert(0, "[INFO] Type a Student ID to generate the QR code.");
                            }

                            PreviewTextBlock.Text = $"Student ID: {currentId}\nFull Name: {FullNameTextBox.Text}\nCourse: {CourseTextBox.Text}\nYear Level: {YearLevelTextBox.Text}\nSection: {SectionTextBox.Text}\nNFC UID: {uid}\nQR Credential: {generatedQr}";
                        }
                    });
                    _isScanning = false;
                }
            }
            catch (Exception ex)
            {
                await DispatcherQueue.TryEnqueueAsync(() => { UidLogListView.Items.Insert(0, $"[ERROR] Buffer extraction breakdown: {ex.Message}"); });
            }
        }

        private void ScanUidButton_Click(object sender, RoutedEventArgs e)
        {
            _isScanning = true;
            UidLogListView.Items.Insert(0, "[INFO] Awaiting physical target tap on reader...");
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
            string nfcUid = NfcUidTextBox.Text.Trim();
            string pin = PinPasswordBox.Password.Trim();
            string qrCredential = QrCredentialTextBox.Text.Trim();
            string status = StatusComboBox.SelectedItem is ComboBoxItem item ? item.Content?.ToString() ?? "Active" : "Active";

            if (string.IsNullOrWhiteSpace(studentId) || string.IsNullOrWhiteSpace(fullName) || string.IsNullOrWhiteSpace(nfcUid))
            {
                UidLogListView.Items.Insert(0, "[ERROR] Critical structural criteria missing (ID, Name, or NFC).");
                return;
            }

            // With this:
            if (string.IsNullOrWhiteSpace(pin))
            {
                // If it's a NEW student, they MUST enter a PIN
                if (!_isExistingProfile)
                {
                    UidLogListView.Items.Insert(0, "[ERROR] A 4-digit PIN is strictly required for new enrollments.");
                    return;
                }
                // If it is an EXISTING student, we allow it to pass through blank!
            }
            else if (pin.Length != 4 || !pin.All(char.IsDigit))
            {
                UidLogListView.Items.Insert(0, "[ERROR] If setting a PIN, it must be exactly 4 numeric digits.");
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
                    Course = CourseTextBox.Text.Trim(),
                    YearLevel = YearLevelTextBox.Text.Trim(),
                    SectionName = SectionTextBox.Text.Trim(),
                    Status = status,
                    NfcUid = nfcUid,
                    QrCredential = qrCredential
                };

                await _database.SaveStudentAsync(student, pin);

                UidLogListView.Items.Insert(0, $"[SUCCESS] Access profile committed: {fullName}");
                PreviewTextBlock.Text = $"Student ID: {studentId}\nFull Name: {fullName}\nStatus: {status}\nNFC UID: {nfcUid}\nQR Credential: {qrCredential}\nPIN Status: Encrypted & Salted (PBKDF2)";
                ClearForm();
            }
            catch (Exception ex)
            {
                UidLogListView.Items.Insert(0, $"[ERROR] Transaction breakdown: {ex.Message}");
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
            QrCodeImage.Source = null;
            _isExistingProfile = false;
            PinPasswordBox.PlaceholderText = "****";
            QrCodeImage.Visibility = Visibility.Collapsed;
            QrPlaceholderPanel.Visibility = Visibility.Visible;
            StatusComboBox.SelectedIndex = 0;
        }

        private WriteableBitmap GenerateQrBitmap(string text)
        {
            var writer = new BarcodeWriterPixelData
            {
                Format = BarcodeFormat.QR_CODE,
                Options = new EncodingOptions
                {
                    Height = 400,
                    Width = 400,
                    Margin = 1 // Keeps the white border minimal
                }
            };

            var pixelData = writer.Write(text);
            var bitmap = new WriteableBitmap(pixelData.Width, pixelData.Height);

            using (var stream = bitmap.PixelBuffer.AsStream())
            {
                stream.Write(pixelData.Pixels, 0, pixelData.Pixels.Length);
            }

            return bitmap;
        }

        private static string BuildQrCredential(string studentId, string nfcUid)
        {
            if (string.IsNullOrWhiteSpace(studentId) || string.IsNullOrWhiteSpace(nfcUid)) return "";
            return $"TCU|{studentId}|{nfcUid}";
        }

        private void CloseSerialPort()
        {
            try
            {
                if (_serialPort != null && _serialPort.IsOpen)
                {
                    _serialPort.DataReceived -= SerialPort_DataReceived;
                    _serialPort.Close();
                }
                _serialPort?.Dispose();
                _serialPort = null;
            }
            catch { }
        }
    }


}

