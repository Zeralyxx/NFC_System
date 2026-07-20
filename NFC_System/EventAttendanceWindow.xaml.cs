using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.IO.Ports;
using WinRT.Interop;
using System.Media;

namespace NFC_System
{
    public sealed partial class EventAttendanceWindow : Window
    {
        private readonly DatabaseService _database = new();
        private readonly VerificationEngine _engine;
        private SerialPort? _serialPort;

        public EventAttendanceWindow()
        {
            this.InitializeComponent();
            _engine = new VerificationEngine(_database);

            MaximizeWindow();
            this.Closed += Window_Closed;

            _ = InitializeAsync();
            TryConnectSerial("COM3");
        }

        private async System.Threading.Tasks.Task InitializeAsync()
        {
            try
            {
                await _database.EnsureSchemaAsync();

                // Sync the dropdown with the global Kiosk State memory
                DirectionComboBox.SelectedIndex = KioskStateController.CurrentType == TransactionType.Entry ? 0 : 1;

                await LoadActiveEventsAsync();
                AttendanceLogListView.Items.Insert(0, "[INFO] Event attendance monitor ready.");
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

            if (events.Count > 0) ActiveEventComboBox.SelectedIndex = 0;

            AttendanceLogListView.Items.Insert(0, $"[INFO] Loaded {events.Count} active event(s).");
        }

        private async void RefreshEventsButton_Click(object sender, RoutedEventArgs e)
        {
            await LoadActiveEventsAsync();
        }

        private void ActiveEventComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            BroadcastStateToKiosk();
        }

        private void DirectionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            BroadcastStateToKiosk();
        }

        private void BroadcastStateToKiosk()
        {
            if (DirectionComboBox == null || ActiveEventComboBox == null) return;

            TransactionType type = DirectionComboBox.SelectedIndex == 1 ? TransactionType.Exit : TransactionType.Entry;
            EventRecord? selectedEvent = ActiveEventComboBox.SelectedItem as EventRecord;
            VerificationMode mode = selectedEvent?.VerificationMode ?? VerificationMode.Standard;

            KioskStateController.BroadcastModeChange(mode, type);
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            CloseSerialPort();
            var dashboard = new MainWindow();
            dashboard.Activate();
            this.Close();
        }

        // Redirects the user to the (soon to be built) Event Management Window!
        private void ManageEventsButton_Click(object sender, RoutedEventArgs e)
        {
            CloseSerialPort();

            // NOTE: This will show a red squiggly line in Visual Studio until you 
            // actually create the blank 'EventManagementWindow.xaml' file!
            var eventManager = new EventManagementWindow();
            eventManager.Activate();
            this.Close();
        }

        private void LaunchKioskButton_Click(object sender, RoutedEventArgs e)
        {
            CloseSerialPort();
            string activeEvent = ActiveEventComboBox.SelectedItem?.ToString() ?? "No Event Selected";

            var kiosk = new KioskModeWindow("Event", activeEvent);
            kiosk.Activate();
            this.Close();
        }

        private async void UnlockAccountButton_Click(object sender, RoutedEventArgs e)
        {
            string studentId = OverrideStudentIdBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(studentId)) return;

            try
            {
                await _database.UpdatePinFailureAsync(studentId, 0, false);
                AttendanceLogListView.Items.Insert(0, $"[SECURITY OVERRIDE] Guard cleared lockout for {studentId}.");
                OverrideStudentIdBox.Text = "";
            }
            catch (Exception ex)
            {
                AttendanceLogListView.Items.Insert(0, $"[DB ERROR] {ex.Message}");
            }
        }

        private void ApplyOutcome(VerificationOutcome outcome)
        {
            if (!string.IsNullOrWhiteSpace(outcome.LogLine))
            {
                AttendanceLogListView.Items.Insert(0, outcome.LogLine);
            }

            if (outcome.ErrorCategory == "PIN_LOCKED")
            {
                SystemSounds.Exclamation.Play();
            }

            if (outcome.Student != null)
                OverrideStudentIdBox.Text = outcome.Student.StudentId;
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
                if (_serialPort == null || !_serialPort.IsOpen) return;
                string line = _serialPort.ReadLine().Trim();
                if (line.StartsWith("UID="))
                {
                    string uid = line.Substring(4).Trim();
                    await DispatcherQueue.TryEnqueueAsync(() => ProcessUidAsync(uid));
                }
            }
            catch { }
        }

        private async System.Threading.Tasks.Task ProcessUidAsync(string uid)
        {
            EventRecord? selectedEvent = ActiveEventComboBox.SelectedItem as EventRecord;
            if (selectedEvent == null) return;

            try
            {
                VerificationOutcome outcome = await _engine.BeginNfcVerificationAsync(
                    uid,
                    selectedEvent.VerificationMode,
                    TransactionType.EventAttendance, // We will update the engine logic for this next!
                    selectedEvent.EventId);

                ApplyOutcome(outcome);
            }
            catch (Exception ex)
            {
                AttendanceLogListView.Items.Insert(0, $"[DB ERROR] {ex.Message}");
            }
        }

        private void MaximizeWindow()
        {
            IntPtr hWnd = WindowNative.GetWindowHandle(this);
            WindowId windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
            AppWindow appWindow = AppWindow.GetFromWindowId(windowId);
            if (appWindow.Presenter is OverlappedPresenter presenter) presenter.Maximize();
        }

        private void Window_Closed(object sender, WindowEventArgs args) => CloseSerialPort();

        private void CloseSerialPort()
        {
            try
            {
                if (_serialPort != null)
                {
                    _serialPort.DataReceived -= SerialPort_DataReceived;
                    _serialPort.Close();
                    _serialPort.Dispose();
                    _serialPort = null;
                }
            }
            catch { }
        }
    }
}