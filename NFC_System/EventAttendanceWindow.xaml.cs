using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.IO.Ports;
using WinRT.Interop;

namespace NFC_System
{
    public sealed partial class EventAttendanceWindow : Window
    {
        private readonly DatabaseService _database = new();
        private readonly VerificationEngine _engine;
        private SerialPort? _serialPort;
        private VerificationSession? _pendingSession;

        public EventAttendanceWindow()
        {
            this.InitializeComponent();
            _engine = new VerificationEngine(_database);

            MaximizeWindow();
            this.Closed += Window_Closed;

            ProcessManualUidButton.Click += ProcessManualUidButton_Click;
            RefreshEventsButton.Click += RefreshEventsButton_Click;
            ActiveEventComboBox.SelectionChanged += ActiveEventComboBox_SelectionChanged;
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
                await LoadActiveEventsAsync();
                AttendanceLogListView.Items.Insert(0, "[INFO] Event attendance workflow ready.");
            }
            catch (Exception ex)
            {
                AttendanceLogListView.Items.Insert(0, $"[DB ERROR] {ex.Message}");
            }
        }

        private async System.Threading.Tasks.Task LoadActiveEventsAsync()
        {
            IReadOnlyList<EventRecord> events = await _database.GetActiveEventsAsync();
            ActiveEventComboBox.ItemsSource = events;

            if (events.Count == 0)
            {
                SelectedEventTextBlock.Text = "No active event selected";
                ResultTextBlock.Text = "NO ACTIVE EVENTS";
                ResultSubTextBlock.Text = "Create or activate an event in Security Administration before scanning.";
                ResultBorder.Background = new SolidColorBrush(Colors.DarkOrange);
                AttendanceLogListView.Items.Insert(0, "[INFO] No active events found.");
                return;
            }

            ActiveEventComboBox.SelectedIndex = 0;
            AttendanceLogListView.Items.Insert(0, $"[INFO] Loaded {events.Count} active event(s).");
        }

        private async void RefreshEventsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await LoadActiveEventsAsync();
            }
            catch (Exception ex)
            {
                AttendanceLogListView.Items.Insert(0, $"[DB ERROR] Could not load events: {ex.Message}");
            }
        }

        private void ActiveEventComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            EventRecord? selectedEvent = GetSelectedEvent();
            if (selectedEvent == null)
            {
                SelectedEventTextBlock.Text = "-";
                EventModeTextBox.Text = "-";
                EventRiskModeTextBlock.Text = "-";
                EventRequiredStepsTextBlock.Text = "-";
                return;
            }

            SelectedEventTextBlock.Text = selectedEvent.DisplayName;
            EventModeTextBox.Text = ModeDisplayName(selectedEvent.VerificationMode);
            EventRiskModeTextBlock.Text = ModeDisplayName(selectedEvent.VerificationMode);
            EventRequiredStepsTextBlock.Text = RequiredStepsDisplay(selectedEvent.VerificationMode);
            ResultTextBlock.Text = "READY TO SCAN";
            ResultSubTextBlock.Text = $"Active event: {selectedEvent.EventName} - {ModeDisplayName(selectedEvent.VerificationMode)}";
            ResultBorder.Background = new SolidColorBrush(Colors.DimGray);
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

                AttendanceLogListView.Items.Insert(0, $"[INFO] Connected to {portName}");
            }
            catch (Exception ex)
            {
                AttendanceLogListView.Items.Insert(0, $"[ERROR] Could not connect: {ex.Message}");
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
                    AttendanceLogListView.Items.Insert(0, $"[ERROR] {ex.Message}");
                });
            }
        }

        private void LaunchKioskButton_Click(object sender, RoutedEventArgs e)
        {
            // Retrieve dynamic active event name
            string activeEvent = "No Event Selected";
            if (ActiveEventComboBox.SelectedItem != null)
            {
                // Adjust this if your model differs, falls back to raw ComboBox selection
                activeEvent = ActiveEventComboBox.SelectedItem.ToString();
            }

            // Launch the fullscreen kiosk configured for this event
            var kiosk = new KioskModeWindow("Event", activeEvent);
            kiosk.Activate();
            this.Close(); // Safely teardown the configuration window
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
            EventRecord? selectedEvent = GetSelectedEvent();
            if (selectedEvent == null)
            {
                ResultTextBlock.Text = "EVENT REQUIRED";
                ResultSubTextBlock.Text = "Select an active event before attendance scanning.";
                ResultBorder.Background = new SolidColorBrush(Colors.DarkOrange);
                AttendanceLogListView.Items.Insert(0, "[ERROR] Event Attendance requires an active event selection.");
                return;
            }

            string eventId = selectedEvent.EventId;
            if (string.IsNullOrWhiteSpace(uid))
            {
                AttendanceLogListView.Items.Insert(0, "[ERROR] NFC UID is required.");
                return;
            }

            ClearPendingInputs();
            SelectedEventTextBlock.Text = selectedEvent.DisplayName;

            if (IsInvalidUid(uid))
            {
                await LogInvalidUidAsync(uid, selectedEvent);
                DisplayInvalidUid(uid);
                return;
            }

            try
            {
                VerificationOutcome outcome = await _engine.BeginNfcVerificationAsync(
                    uid,
                    selectedEvent.VerificationMode,
                    TransactionType.EventAttendance,
                    eventId);

                ApplyOutcome(outcome);
            }
            catch (Exception ex)
            {
                AttendanceLogListView.Items.Insert(0, $"[DB ERROR] {ex.Message}");
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
                AttendanceLogListView.Items.Insert(0, outcome.LogLine);
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
            YearSectionTextBlock.Text = $"{student.YearLevel} / {student.SectionName}";
            StatusTextBlock.Text = student.Status.ToUpper();
            SelectedEventTextBlock.Text = GetSelectedEvent()?.DisplayName ?? "-";
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
            YearSectionTextBlock.Text = "-";
            StatusTextBlock.Text = "-";
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

            EventRecord? selectedEvent = GetSelectedEvent();
            string eventLabel = selectedEvent?.DisplayName ?? outcome.Session.EventId;
            SuccessSummaryTextBlock.Text =
                $"Attendance recorded for {eventLabel}\n" +
                $"Entry state updated: INSIDE\n" +
                $"Attendance/access log recorded at {outcome.Timestamp:yyyy-MM-dd hh:mm:ss tt}\n" +
                $"Mode used: {RequiredStepsDisplay(outcome.Session.Mode)}";
            SuccessSummaryBorder.Visibility = Visibility.Visible;
        }

        private void DisplayInvalidUid(string uid)
        {
            ClearStudentDetails();
            SuccessSummaryBorder.Visibility = Visibility.Collapsed;
            SuccessSummaryTextBlock.Text = "-";
            UidTextBlock.Text = uid;
            SelectedEventTextBlock.Text = GetSelectedEvent()?.DisplayName ?? "-";
            ScanTimeTextBlock.Text = DateTime.Now.ToString("yyyy-MM-dd hh:mm:ss tt");
            StatusTextBlock.Text = "INVALID UID";
            StatusBadge.Background = new SolidColorBrush(Colors.DarkOrange);
            ResultTextBlock.Text = "SCAN AGAIN";
            ResultSubTextBlock.Text = "Invalid NFC read detected";
            ResultBorder.Background = new SolidColorBrush(Colors.DarkOrange);
            AttendanceLogListView.Items.Insert(0, $"{ScanTimeTextBlock.Text} | UID {uid} | INVALID READ");
        }

        private async System.Threading.Tasks.Task LogInvalidUidAsync(string uid, EventRecord selectedEvent)
        {
            try
            {
                await _database.LogVerificationAsync(null, uid, TransactionType.EventAttendance, selectedEvent.VerificationMode, false, "INVALID_NFC_READ", "INVALID_UID", $"Invalid or corrupted NFC UID read was rejected for event {selectedEvent.EventId}.");
                await _database.AddAlertAsync(null, "INVALID_UID", $"Invalid NFC UID read rejected for event {selectedEvent.EventId}: {uid}.");
            }
            catch (Exception ex)
            {
                AttendanceLogListView.Items.Insert(0, $"[DB ERROR] Could not log invalid UID: {ex.Message}");
            }
        }

        private EventRecord? GetSelectedEvent()
        {
            return ActiveEventComboBox.SelectedItem as EventRecord;
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

        private void TriggerQrScannerButton_Click(object sender, RoutedEventArgs e)
        {
            // Launch the QR Camera Scanner modal
            var qrScannerWin = new QrScannerWindow();
            qrScannerWin.Activate();
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

        private static Brush OutcomeBrush(VerificationOutcome outcome)
        {
            if (outcome.Step == VerificationStep.RequiresPin || outcome.Step == VerificationStep.RequiresQr)
            {
                return new SolidColorBrush(Colors.SteelBlue);
            }

            if (outcome.ErrorCategory.Contains("PIN", StringComparison.OrdinalIgnoreCase) ||
                outcome.ErrorCategory.Contains("EVENT", StringComparison.OrdinalIgnoreCase))
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
