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
using Windows.Devices.Enumeration;
using System.Linq;

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

                var cameras = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);
                CameraComboBox.ItemsSource = cameras;

                string savedCamId = await _database.GetSettingAsync("selected_camera", "");
                if (!string.IsNullOrEmpty(savedCamId))
                    CameraComboBox.SelectedItem = cameras.FirstOrDefault(c => c.Id == savedCamId) ?? cameras.FirstOrDefault();
                else
                    CameraComboBox.SelectedItem = cameras.FirstOrDefault();

                DirectionComboBox.SelectedIndex = KioskStateController.CurrentType == TransactionType.Entry ? 0 : 1;

                await LoadActiveEventsAsync();
                AttendanceLogListView.Items.Insert(0, "[INFO] Event attendance monitor ready.");
            }
            catch (Exception ex)
            {
                AttendanceLogListView.Items.Insert(0, $"[DB ERROR] {ex.Message}");
            }
        }

        private async void CameraComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CameraComboBox.SelectedItem is DeviceInformation selectedCam)
            {
                KioskStateController.SelectedCameraId = selectedCam.Id;
                try { await _database.SetSettingAsync("selected_camera", selectedCam.Id); } catch { }
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

        private void ManageEventsButton_Click(object sender, RoutedEventArgs e)
        {
            CloseSerialPort();
            var eventManager = new EventManagementWindow();
            eventManager.Activate();
            this.Close();
        }

        // 1. UPDATE THIS METHOD
        private void LaunchKioskButton_Click(object sender, RoutedEventArgs e)
        {
            CloseSerialPort();

            EventRecord? selectedEvent = ActiveEventComboBox.SelectedItem as EventRecord;
            string activeEventName = selectedEvent != null ? selectedEvent.DisplayName : "No Event Selected";
            string mode = selectedEvent != null ? selectedEvent.VerificationMode.ToString() : "Standard";
            string type = (DirectionComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Entry";

            var kiosk = new KioskModeWindow("Event", $"{activeEventName} | {type} ({mode})");

            // Subscribe to the Kiosk's live event bridge
            KioskModeWindow.OnKioskOutcome -= ApplyOutcomeFromKiosk;
            KioskModeWindow.OnKioskOutcome += ApplyOutcomeFromKiosk;

            KioskModeWindow.OnKioskLog -= AddKioskLog;
            KioskModeWindow.OnKioskLog += AddKioskLog;

            kiosk.Closed += (s, args) =>
            {
                TryConnectSerial("COM3");

                // Unsubscribe when Kiosk closes to prevent memory leaks
                KioskModeWindow.OnKioskOutcome -= ApplyOutcomeFromKiosk;
                KioskModeWindow.OnKioskLog -= AddKioskLog;
            };

            kiosk.Activate();
        }

        // NEW: Handles live logs directly from the active Kiosk
        private void ApplyOutcomeFromKiosk(VerificationOutcome outcome)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                ApplyOutcome(outcome);
            });
        }

        // NEW: Handles manual bad read texts from the Kiosk
        private void AddKioskLog(string msg)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                AttendanceLogListView.Items.Insert(0, msg);
            });
        }

        // 2. UPDATE THIS METHOD
        private void PlaySecurityAlert()
        {
            // Bypasses the Windows Volume Mixer and forces a loud hardware beep.
            // Runs on a background thread so it doesn't freeze your UI.
            System.Threading.Tasks.Task.Run(() =>
            {
                for (int i = 0; i < 3; i++)
                {
                    Console.Beep(2500, 300); // 2500hz frequency (high pitch), 300ms duration
                    System.Threading.Thread.Sleep(100);
                }
            });
        }

        private async void UnlockAccountButton_Click(object sender, RoutedEventArgs e)
        {
            string studentId = OverrideStudentIdBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(studentId)) return;

            try
            {
                // 1. Unlock the account
                await _database.UpdatePinFailureAsync(studentId, 0, false);

                // 2. PERMANENTLY LOG IT TO THE DATABASE FOR THE ADMIN DASHBOARD
                try
                {
                    await _database.AddAlertAsync(null, "ADMIN_OVERRIDE", $"Event Guard manually cleared 2FA lockout for {studentId}.");
                }
                catch { }

                // 3. Update the local UI
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

            // Trigger loud siren for severe security violations
            string[] severeErrors = { "PIN_LOCKED", "ANTI_TAILGATING_VIOLATION", "UNAUTHORIZED_EVENT_ACCESS", "NOT_REGISTERED", "CREDENTIAL_MISMATCH", "INACTIVE_STUDENT" };
            if (severeErrors.Contains(outcome.ErrorCategory))
            {
                PlaySecurityAlert();
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

            TransactionType tType = DirectionComboBox.SelectedIndex == 1
                ? TransactionType.Exit
                : TransactionType.EventAttendance;

            if (IsInvalidUid(uid))
            {
                PlaySecurityAlert(); // Trigger siren on bad/corrupted read
                await LogInvalidUidAsync(uid, tType, selectedEvent.VerificationMode);
                DisplayInvalidUid(uid);
                return;
            }

            try
            {
                VerificationOutcome outcome = await _engine.BeginNfcVerificationAsync(
                    uid,
                    selectedEvent.VerificationMode,
                    tType,
                    selectedEvent.EventId);

                ApplyOutcome(outcome);
            }
            catch (Exception ex)
            {
                AttendanceLogListView.Items.Insert(0, $"[DB ERROR] {ex.Message}");
            }
        }

        private void DisplayInvalidUid(string uid)
        {
            OverrideStudentIdBox.Text = "";
            string logTime = DateTime.Now.ToString("yyyy-MM-dd hh:mm:ss tt");
            AttendanceLogListView.Items.Insert(0, $"{logTime} | UID {uid} | BAD READ: Please tap again");
        }

        private async System.Threading.Tasks.Task LogInvalidUidAsync(string uid, TransactionType transactionType, VerificationMode mode)
        {
            try
            {
                await _database.LogVerificationAsync(null, uid, transactionType, mode, false, "BAD_NFC_READ", "BAD_READ", "Card couldn't be read properly. User prompted to tap again.");
            }
            catch (Exception ex)
            {
                AttendanceLogListView.Items.Insert(0, $"[DB ERROR] Could not log bad read: {ex.Message}");
            }
        }

        private static bool IsInvalidUid(string uid)
        {
            if (string.IsNullOrWhiteSpace(uid)) return true;

            string[] parts = uid.Split(':');
            if (parts.Length != 4 && parts.Length != 7) return true;

            bool allZero = true;
            foreach (string part in parts)
            {
                if (part != "00")
                {
                    allZero = false;
                    break;
                }
            }
            if (allZero) return true;

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
                if (trailingZeros) return true;
            }
            return false;
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