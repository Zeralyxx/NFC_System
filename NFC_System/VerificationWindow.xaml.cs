using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.IO.Ports;
using WinRT.Interop;

namespace NFC_System
{
    public sealed partial class VerificationWindow : Window
    {
        private readonly DatabaseService _database = new();
        private readonly VerificationEngine _engine;
        private SerialPort? _serialPort;
        private VerificationSession? _pendingSession;

        public VerificationWindow()
        {
            this.InitializeComponent();
            _engine = new VerificationEngine(_database);

            MaximizeWindow();
            this.Closed += Window_Closed;

            ProcessManualUidButton.Click += ProcessManualUidButton_Click;
            SecurityModeComboBox.SelectionChanged += SecurityModeComboBox_SelectionChanged;
            SubmitPinButton.Click += SubmitPinButton_Click;
            SubmitQrButton.Click += SubmitQrButton_Click;

            _ = InitializeAsync();
            TryConnectSerial("COM3"); // CHANGE this to your actual Arduino COM port
        }

        private async System.Threading.Tasks.Task InitializeAsync()
        {
            try
            {
                await _database.EnsureSchemaAsync();
                string savedMode = await _database.GetSettingAsync("verification_mode", "Standard");
                SecurityModeComboBox.SelectedIndex = savedMode switch
                {
                    "Fast" => 0,
                    "High-Security" => 2,
                    _ => 1
                };
                UpdateRiskSummary();

                VerificationLogListView.Items.Insert(0, "[INFO] Risk-based verification engine ready.");
            }
            catch (Exception ex)
            {
                VerificationLogListView.Items.Insert(0, $"[DB ERROR] {ex.Message}");
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
                if (_serialPort == null || !_serialPort.IsOpen)
                {
                    return;
                }

                string line = _serialPort.ReadLine().Trim();
                if (line.StartsWith("UID="))
                {
                    string uid = line.Substring(4).Trim();
                    await DispatcherQueue.TryEnqueueAsync(() => _ = ProcessUidAsync(uid));
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

        private async void ProcessManualUidButton_Click(object sender, RoutedEventArgs e)
        {
            await ProcessUidAsync(ManualUidTextBox.Text.Trim());
        }

        private async void SubmitPinButton_Click(object sender, RoutedEventArgs e)
        {
            if (_pendingSession == null)
            {
                return;
            }

            VerificationOutcome outcome = await _engine.SubmitPinAsync(_pendingSession, VerifyPinBox.Password.Trim());
            ApplyOutcome(outcome);
        }

        private async void SubmitQrButton_Click(object sender, RoutedEventArgs e)
        {
            if (_pendingSession == null)
            {
                return;
            }

            VerificationOutcome outcome = await _engine.SubmitQrAsync(_pendingSession, QrCredentialTextBox.Text.Trim());
            ApplyOutcome(outcome);
        }

        private async System.Threading.Tasks.Task ProcessUidAsync(string uid)
        {
            if (string.IsNullOrWhiteSpace(uid))
            {
                VerificationLogListView.Items.Insert(0, "[ERROR] NFC UID is required.");
                return;
            }

            ClearPendingInputs();

            if (IsInvalidUid(uid))
            {
                await LogInvalidUidAsync(uid, GetSelectedTransactionType(), GetSelectedMode());
                DisplayInvalidUid(uid);
                return;
            }

            TransactionType transactionType = GetSelectedTransactionType();

            try
            {
                VerificationOutcome outcome = await _engine.BeginNfcVerificationAsync(
                    uid,
                    GetSelectedMode(),
                    transactionType,
                    "");

                ApplyOutcome(outcome);
            }
            catch (Exception ex)
            {
                VerificationLogListView.Items.Insert(0, $"[DB ERROR] {ex.Message}");
            }
        }

        private void ApplyOutcome(VerificationOutcome outcome)
        {
            ResultTextBlock.Text = outcome.ResultTitle;
            ResultSubTextBlock.Text = outcome.ResultMessage;
            ResultBorder.Background = outcome.IsGranted
                ? new SolidColorBrush(Colors.ForestGreen)
                : OutcomeBrush(outcome);
            UpdateSuccessSummary(outcome);

            ScanTimeTextBlock.Text = outcome.Timestamp.ToString("yyyy-MM-dd hh:mm:ss tt");

            if (outcome.Student != null)
            {
                DisplayStudent(outcome.Student);
            }
            else
            {
                ClearStudentDetails();
            }

            if (!string.IsNullOrWhiteSpace(outcome.LogLine))
            {
                VerificationLogListView.Items.Insert(0, outcome.LogLine);
            }

            if (outcome.Step == VerificationStep.RequiresPin && outcome.Session != null)
            {
                _pendingSession = outcome.Session;
                VerifyPinBox.IsEnabled = true;
                SubmitPinButton.IsEnabled = true;
                VerifyPinBox.Password = "";
                VerifyPinBox.Focus(FocusState.Programmatic);
                QrCredentialTextBox.IsEnabled = false;
                SubmitQrButton.IsEnabled = false;
            }
            else if (outcome.Step == VerificationStep.RequiresQr && outcome.Session != null)
            {
                _pendingSession = outcome.Session;
                VerifyPinBox.IsEnabled = false;
                SubmitPinButton.IsEnabled = false;
                QrCredentialTextBox.IsEnabled = true;
                SubmitQrButton.IsEnabled = true;
                QrCredentialTextBox.Text = "";
                QrCredentialTextBox.Focus(FocusState.Programmatic);
            }
            else
            {
                ClearPendingInputs();
            }
        }

        private void DisplayStudent(StudentRecord student)
        {
            StudentNameTextBlock.Text = student.FullName;
            StudentIdTextBlock.Text = student.StudentId;
            CourseTextBlock.Text = student.Course;
            YearLevelTextBlock.Text = student.YearLevel;
            SectionTextBlock.Text = student.SectionName;
            StatusTextBlock.Text = student.Status.ToUpper();
            EntryStateTextBlock.Text = student.EntryState;
            UidTextBlock.Text = student.NfcUid;

            if (student.Status.Equals("Active", StringComparison.OrdinalIgnoreCase))
            {
                StatusBadge.Background = new SolidColorBrush(Colors.ForestGreen);
            }
            else if (student.Status.Equals("Inactive", StringComparison.OrdinalIgnoreCase))
            {
                StatusBadge.Background = new SolidColorBrush(Colors.DarkOrange);
            }
            else
            {
                StatusBadge.Background = new SolidColorBrush(Colors.Firebrick);
            }

            StatusTextBlock.Foreground = new SolidColorBrush(Colors.White);
        }

        private void ClearStudentDetails()
        {
            StudentNameTextBlock.Text = "-";
            StudentIdTextBlock.Text = "-";
            CourseTextBlock.Text = "-";
            YearLevelTextBlock.Text = "-";
            SectionTextBlock.Text = "-";
            StatusTextBlock.Text = "-";
            EntryStateTextBlock.Text = "Unknown";
            UidTextBlock.Text = "-";
            StatusBadge.Background = new SolidColorBrush(Colors.Gray);
            StatusTextBlock.Foreground = new SolidColorBrush(Colors.White);
        }

        private void ClearPendingInputs()
        {
            _pendingSession = null;
            VerifyPinBox.Password = "";
            VerifyPinBox.IsEnabled = false;
            SubmitPinButton.IsEnabled = false;
            QrCredentialTextBox.Text = "";
            QrCredentialTextBox.IsEnabled = false;
            SubmitQrButton.IsEnabled = false;
        }

        private void UpdateSuccessSummary(VerificationOutcome outcome)
        {
            if (!outcome.IsGranted || outcome.Session == null || outcome.Student == null)
            {
                SuccessSummaryBorder.Visibility = Visibility.Collapsed;
                SuccessSummaryTextBlock.Text = "-";
                return;
            }

            string action = outcome.Session.TransactionType == TransactionType.Exit ? "Exit state updated" : "Entry state updated";
            string state = outcome.Session.TransactionType == TransactionType.Exit ? "OUTSIDE" : "INSIDE";
            SuccessSummaryTextBlock.Text =
                $"{action}: {state}\n" +
                $"Access log recorded at {outcome.Timestamp:yyyy-MM-dd hh:mm:ss tt}\n" +
                $"Mode used: {RequiredStepsDisplay(outcome.Session.Mode)}";
            SuccessSummaryBorder.Visibility = Visibility.Visible;
        }

        private void DisplayInvalidUid(string uid)
        {
            ClearStudentDetails();
            SuccessSummaryBorder.Visibility = Visibility.Collapsed;
            SuccessSummaryTextBlock.Text = "-";
            UidTextBlock.Text = uid;
            ScanTimeTextBlock.Text = DateTime.Now.ToString("yyyy-MM-dd hh:mm:ss tt");
            StatusTextBlock.Text = "INVALID UID";
            StatusBadge.Background = new SolidColorBrush(Colors.DarkOrange);
            ResultTextBlock.Text = "SCAN AGAIN";
            ResultSubTextBlock.Text = "Invalid NFC read detected";
            ResultBorder.Background = new SolidColorBrush(Colors.DarkOrange);
            VerificationLogListView.Items.Insert(0, $"{ScanTimeTextBlock.Text} | UID {uid} | INVALID READ");
        }

        private async System.Threading.Tasks.Task LogInvalidUidAsync(string uid, TransactionType transactionType, VerificationMode mode)
        {
            try
            {
                await _database.LogVerificationAsync(null, uid, transactionType, mode, false, "INVALID_NFC_READ", "INVALID_UID", "Invalid or corrupted NFC UID read was rejected before verification.");
                await _database.AddAlertAsync(null, "INVALID_UID", $"Invalid NFC UID read rejected: {uid}.");
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

            return false;
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
                // Closing the app should not be blocked by serial cleanup.
            }
        }
    }
}
