using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using WinRT.Interop;
using System.Linq;
using Windows.Devices.Enumeration;

namespace NFC_System
{
    public sealed partial class EventAttendanceWindow : Window
    {
        private readonly DatabaseService _database = new();
        private readonly VerificationEngine _engine;
        private bool _isInitializing = true;

        public EventAttendanceWindow()
        {
            this.InitializeComponent();
            _engine = new VerificationEngine(_database);

            MaximizeWindow();

            _ = InitializeAsync();
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

                if (!AppSession.IsAdmin)
                {
                    ManageEventsButton.Visibility = Visibility.Collapsed;
                }

                _isInitializing = false;
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

        private async void ActiveEventComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            BroadcastStateToKiosk();

            if (!_isInitializing && ActiveEventComboBox.SelectedItem is EventRecord selectedEvent)
            {
                string staff = AppSession.CurrentStaffName;
                try
                {
                    // Inside ActiveEventComboBox_SelectionChanged
                    await _database.AddAlertAsync(staff, "ADMIN_ACTION", $"Set Event Terminal to monitor '{selectedEvent.EventName}'.");
                }
                catch { }
                AttendanceLogListView.Items.Insert(0, $"[AUDIT] Terminal set to {selectedEvent.EventName} by {staff}");
            }
        }

        private async void DirectionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            BroadcastStateToKiosk();

            if (!_isInitializing)
            {
                string direction = DirectionComboBox.SelectedIndex == 1 ? "Exit" : "Entry";
                string staff = AppSession.CurrentStaffName;

                try
                {
                    // Inside DirectionComboBox_SelectionChanged
                    await _database.AddAlertAsync(staff, "ADMIN_ACTION", $"Changed Event Terminal Direction to {direction}.");
                }
                catch { }
                AttendanceLogListView.Items.Insert(0, $"[AUDIT] Direction changed to {direction} by {staff}");
            }
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
            var dashboard = new MainWindow();
            dashboard.Activate();
            this.Close();
        }

        private void ManageEventsButton_Click(object sender, RoutedEventArgs e)
        {
            var eventManager = new EventManagementWindow();
            eventManager.Activate();
            this.Close();
        }

        private void LaunchKioskButton_Click(object sender, RoutedEventArgs e)
        {
            BroadcastStateToKiosk();

            EventRecord? selectedEvent = ActiveEventComboBox.SelectedItem as EventRecord;
            string activeEventName = selectedEvent != null ? selectedEvent.DisplayName : "No Event Selected";
            string mode = selectedEvent != null ? selectedEvent.VerificationMode.ToString() : "Standard";
            string type = (DirectionComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Entry";

            var kiosk = new KioskModeWindow("Event", $"{activeEventName} | {type} ({mode})", selectedEvent?.EventId);

            KioskModeWindow.OnKioskOutcome -= ApplyOutcomeFromKiosk;
            KioskModeWindow.OnKioskOutcome += ApplyOutcomeFromKiosk;

            KioskModeWindow.OnKioskLog -= AddKioskLog;
            KioskModeWindow.OnKioskLog += AddKioskLog;

            kiosk.Closed += (s, args) =>
            {
                KioskModeWindow.OnKioskOutcome -= ApplyOutcomeFromKiosk;
                KioskModeWindow.OnKioskLog -= AddKioskLog;
            };

            kiosk.Activate();
        }

        private void ApplyOutcomeFromKiosk(VerificationOutcome outcome)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                ApplyOutcome(outcome);
            });
        }

        private void AddKioskLog(string msg)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                AttendanceLogListView.Items.Insert(0, msg);
            });
        }

        private void PlaySecurityAlert()
        {
            System.Threading.Tasks.Task.Run(() =>
            {
                for (int i = 0; i < 3; i++)
                {
                    Console.Beep(2500, 300);
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
                // GUARDRAIL: Verify the student actually exists in the database
                var student = await _database.GetStudentByIdAsync(studentId);
                if (student == null)
                {
                    AttendanceLogListView.Items.Insert(0, $"[ERROR] Unlock Aborted: Student ID '{studentId}' does not exist in the database.");
                    PlaySecurityAlert();
                    return;
                }

                await _database.UpdatePinFailureAsync(studentId, 0, false);

                try
                {
                    await _database.AddAlertAsync(AppSession.CurrentStaffName, "ADMIN_OVERRIDE", $"Manually cleared 2FA lockout for {student.FullName} ({studentId}).");
                }
                catch { }

                AttendanceLogListView.Items.Insert(0, $"[SECURITY OVERRIDE] Guard cleared lockout for {student.FullName} ({studentId}).");
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

            string[] severeErrors = { "PIN_LOCKED", "ANTI_TAILGATING_VIOLATION", "UNAUTHORIZED_EVENT_ACCESS", "NOT_REGISTERED", "CREDENTIAL_MISMATCH", "INACTIVE_STUDENT" };
            if (severeErrors.Contains(outcome.ErrorCategory))
            {
                PlaySecurityAlert();
            }

            if (outcome.Student != null)
                OverrideStudentIdBox.Text = outcome.Student.StudentId;
        }

        private void MaximizeWindow()
        {
            IntPtr hWnd = WindowNative.GetWindowHandle(this);
            WindowId windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
            AppWindow appWindow = AppWindow.GetFromWindowId(windowId);
            if (appWindow.Presenter is OverlappedPresenter presenter) presenter.Maximize();
        }
    }
}