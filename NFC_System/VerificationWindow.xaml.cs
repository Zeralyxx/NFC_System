using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using MySqlConnector;
using System;
using System.IO.Ports;
using Microsoft.UI.Windowing;
using WinRT.Interop;

namespace NFC_System
{
    public sealed partial class VerificationWindow : Window
    {
        private SerialPort? _serialPort;

        private readonly string _connectionString =
            "Server=127.0.0.1;Port=3306;Database=nfc_system;User ID=root;Password=;";

        public VerificationWindow()
        {
            this.InitializeComponent();
            MaximizeWindow();
            this.Closed += Window_Closed;
            TryConnectSerial("COM3"); // CHANGE this to your actual Arduino COM port
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

                if (line.StartsWith("UID="))
                {
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

                            StudentNameTextBlock.Text = "-";
                            StudentIdTextBlock.Text = "-";
                            CourseTextBlock.Text = "-";
                            YearLevelTextBlock.Text = "-";
                            SectionTextBlock.Text = "-";
                            StatusTextBlock.Text = "INVALID UID";
                            UidTextBlock.Text = uid;
                            ScanTimeTextBlock.Text = scanTime;

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
            }
            catch (Exception ex)
            {
                await DispatcherQueue.TryEnqueueAsync(() =>
                {
                    VerificationLogListView.Items.Insert(0, $"[ERROR] {ex.Message}");
                });
            }
        }

        private async System.Threading.Tasks.Task LoadStudentByUid(string uid)
        {
            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                string query = @"
                    SELECT student_id, full_name, course, year_level, section_name, status
                    FROM students
                    WHERE nfc_uid = @uid
                    LIMIT 1";

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

                    StudentNameTextBlock.Text = fullName;
                    StudentIdTextBlock.Text = studentId;
                    CourseTextBlock.Text = course;
                    YearLevelTextBlock.Text = yearLevel;
                    SectionTextBlock.Text = section;
                    StatusTextBlock.Text = status.ToUpper();
                    UidTextBlock.Text = uid;
                    ScanTimeTextBlock.Text = scanTime;

                    if (status.Equals("Active", StringComparison.OrdinalIgnoreCase))
                    {
                        StatusBadge.Background = new SolidColorBrush(Colors.ForestGreen);
                        StatusTextBlock.Foreground = new SolidColorBrush(Colors.White);

                        ResultTextBlock.Text = "ACCESS GRANTED";
                        ResultSubTextBlock.Text = "Student is active and authorized";
                        ResultBorder.Background = new SolidColorBrush(Colors.ForestGreen);

                        VerificationLogListView.Items.Insert(0,
                            $"{scanTime}  |  {studentId}  |  {fullName}  |  GRANTED");
                    }
                    else if (status.Equals("Inactive", StringComparison.OrdinalIgnoreCase))
                    {
                        StatusBadge.Background = new SolidColorBrush(Colors.DarkOrange);
                        StatusTextBlock.Foreground = new SolidColorBrush(Colors.White);

                        ResultTextBlock.Text = "ACCESS DENIED";
                        ResultSubTextBlock.Text = "Reason: student record is inactive";
                        ResultBorder.Background = new SolidColorBrush(Colors.DarkOrange);

                        VerificationLogListView.Items.Insert(0,
                            $"{scanTime}  |  {studentId}  |  {fullName}  |  DENIED (INACTIVE)");
                    }
                    else
                    {
                        StatusBadge.Background = new SolidColorBrush(Colors.Firebrick);
                        StatusTextBlock.Foreground = new SolidColorBrush(Colors.White);

                        ResultTextBlock.Text = "ACCESS DENIED";
                        ResultSubTextBlock.Text = $"Reason: {status}";
                        ResultBorder.Background = new SolidColorBrush(Colors.Firebrick);

                        VerificationLogListView.Items.Insert(0,
                            $"{scanTime}  |  {studentId}  |  {fullName}  |  DENIED ({status.ToUpper()})");
                    }
                }
                else
                {
                    StudentNameTextBlock.Text = "-";
                    StudentIdTextBlock.Text = "-";
                    CourseTextBlock.Text = "-";
                    YearLevelTextBlock.Text = "-";
                    SectionTextBlock.Text = "-";
                    StatusTextBlock.Text = "NOT REGISTERED";
                    UidTextBlock.Text = uid;
                    ScanTimeTextBlock.Text = scanTime;

                    StatusBadge.Background = new SolidColorBrush(Colors.Gray);
                    StatusTextBlock.Foreground = new SolidColorBrush(Colors.White);

                    ResultTextBlock.Text = "ACCESS DENIED";
                    ResultSubTextBlock.Text = "Reason: NFC UID is not registered";
                    ResultBorder.Background = new SolidColorBrush(Colors.Firebrick);

                    VerificationLogListView.Items.Insert(0,
                        $"{scanTime}  |  UID {uid}  |  NOT REGISTERED");
                }
            }
            catch (Exception ex)
            {
                VerificationLogListView.Items.Insert(0, $"[DB ERROR] Could not log invalid UID: {ex.Message}");
            }
        }

        private void SecurityModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateRiskSummary();
        }

        private VerificationMode GetSelectedMode()
        {
            return SecurityModeComboBox.SelectedIndex switch
            {
                0 => VerificationMode.Fast,
                2 => VerificationMode.HighSecurity,
                _ => VerificationMode.Standard
            };
        }

        private void UpdateRiskSummary()
        {
            VerificationMode mode = GetSelectedMode();
            
        }

        private static string ModeDisplayName(VerificationMode mode)
        {
            return mode switch
            {
                VerificationMode.Fast => "Fast Mode",
                VerificationMode.HighSecurity => "High-Security Mode",
                _ => "Standard Mode"
            };
        }

        private static string RequiredStepsDisplay(VerificationMode mode)
        {
            return mode switch
            {
                VerificationMode.Fast => "NFC only",
                VerificationMode.HighSecurity => "NFC + PIN + QR",
                _ => "NFC + PIN"
            };
        }

        private TransactionType GetSelectedTransactionType()
        {
            return DirectionComboBox.SelectedIndex switch
            {
                1 => TransactionType.Exit,
                _ => TransactionType.Entry
            };
        }

        private static Brush OutcomeBrush(VerificationOutcome outcome)
        {
            if (outcome.Step == VerificationStep.RequiresPin || outcome.Step == VerificationStep.RequiresQr)
            {
                return new SolidColorBrush(Colors.SteelBlue);
            }

            if (outcome.ErrorCategory.Contains("PIN", StringComparison.OrdinalIgnoreCase) ||
                outcome.ErrorCategory.Contains("TAILGATING", StringComparison.OrdinalIgnoreCase))
            {
                return new SolidColorBrush(Colors.DarkOrange);
            }

            return new SolidColorBrush(Colors.Firebrick);
        }

        private static bool IsInvalidUid(string uid)
        {
            if (string.IsNullOrWhiteSpace(uid))
            {
                return true;
            }

            string[] parts = uid.Split(':');
            if (parts.Length != 4 && parts.Length != 7)
            {
                return true;
            }

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
            {
                return true;
            }

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
                {
                    return true;
                }
            }
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
            catch (Exception ex)
            {
                // Optional: log if needed
                // UidLogListView.Items.Insert(0, $"[ERROR] {ex.Message}");
            }
        }
    }

}