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

        // Timer to auto-clear the live feed
        private readonly DispatcherTimer _liveFeedTimer = new();

        public EventAttendanceWindow()
        {
            this.InitializeComponent();
            // Subscribe to the live monitor
            DatabaseMonitor.ConnectionStatusChanged += UpdateOfflineBanner;
            UpdateOfflineBanner(DatabaseMonitor.IsOnline); // Set initial state on load
            _engine = new VerificationEngine(_database);

            MaximizeWindow();

            // Set up 10-second auto-clear timer
            _liveFeedTimer.Interval = TimeSpan.FromSeconds(10);
            _liveFeedTimer.Tick += (s, e) =>
            {
                _liveFeedTimer.Stop();
                LiveFeedActivePanel.Visibility = Visibility.Collapsed;
                LiveFeedIdlePanel.Visibility = Visibility.Visible;
            };

            _ = InitializeAsync();
        }

        private async System.Threading.Tasks.Task InitializeAsync()
        {
            if (DatabaseMonitor.IsOnline)
            {
                try { await _database.EnsureSchemaAsync(); } catch { }
            }

            try
            {
                var cameras = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);
                CameraComboBox.ItemsSource = cameras;

                string savedCamId = "";
                if (DatabaseMonitor.IsOnline)
                {
                    try { savedCamId = await _database.GetSettingAsync("selected_camera", ""); } catch { }
                }

                if (!string.IsNullOrEmpty(savedCamId))
                    CameraComboBox.SelectedItem = cameras.FirstOrDefault(c => c.Id == savedCamId) ?? cameras.FirstOrDefault();
                else
                    CameraComboBox.SelectedItem = cameras.FirstOrDefault();

                DirectionComboBox.SelectedIndex = KioskStateController.CurrentType == TransactionType.Entry ? 0 : 1;

                if (DatabaseMonitor.IsOnline)
                {
                    await LoadActiveEventsAsync();
                }
                else
                {
                    AttendanceLogListView.Items.Insert(0, $"[OFFLINE CACHE] Cannot retrieve live events from database. Defaulting to local mode.");
                }

                AttendanceLogListView.Items.Insert(0, "[INFO] Event attendance monitor ready.");

                if (!AppSession.IsAdmin)
                {
                    ManageEventsButton.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                AttendanceLogListView.Items.Insert(0, $"[SYSTEM ERROR] {ex.Message}");
            }
            finally
            {
                _isInitializing = false;
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
            try
            {
                IReadOnlyList<EventRecord> events = await _database.GetActiveEventsAsync();
                ActiveEventComboBox.ItemsSource = events;

                if (events.Count > 0) ActiveEventComboBox.SelectedIndex = 0;

                AttendanceLogListView.Items.Insert(0, $"[INFO] Loaded {events.Count} active event(s).");
            }
            catch
            {
                // THE FIX: Let the UI know it couldn't retrieve events instead of crashing initialization
                AttendanceLogListView.Items.Insert(0, $"[OFFLINE CACHE] Cannot retrieve live events from database. Defaulting to local mode.");
            }
        }

        private void UpdateOfflineBanner(bool isOnline)
        {
            // DispatcherQueue safely pushes the update to the UI thread
            DispatcherQueue.TryEnqueue(() =>
            {
                if (GlobalOfflineBanner != null)
                {
                    GlobalOfflineBanner.Visibility = isOnline ? Visibility.Collapsed : Visibility.Visible;
                }
            });
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

                // THE FIX: Update the UI immediately
                AttendanceLogListView.Items.Insert(0, $"[AUDIT] Terminal set to {selectedEvent.EventName} by {staff}");

                try
                {
                    await _database.AddAlertAsync(staff, "ADMIN_ACTION", $"Set Event Terminal to monitor '{selectedEvent.EventName}'.");
                }
                catch { /* Fails gracefully in offline mode */ }
            }
        }

        private async void DirectionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            BroadcastStateToKiosk();

            if (!_isInitializing)
            {
                string direction = DirectionComboBox.SelectedIndex == 1 ? "Exit" : "Entry";
                string staff = AppSession.CurrentStaffName;

                // THE FIX: Update the UI immediately
                AttendanceLogListView.Items.Insert(0, $"[AUDIT] Direction changed to {direction} by {staff}");

                try
                {
                    await _database.AddAlertAsync(staff, "ADMIN_ACTION", $"Changed Event Terminal Direction to {direction}.");
                }
                catch { /* Fails gracefully in offline mode */ }
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
                    System.Threading.Tasks.Task.Delay(100).Wait();
                }
            });
        }

        private async void UnlockAccountButton_Click(object sender, RoutedEventArgs e)
        {
            string studentId = OverrideStudentIdBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(studentId)) return;

            try
            {
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

        private async void ApplyOutcome(VerificationOutcome outcome)
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

            // Don't show partial steps, only final outcomes
            if (outcome.Step == VerificationStep.Completed && outcome.Student != null)
            {
                OverrideStudentIdBox.Text = outcome.Student.StudentId;

                LiveStudentPhoto.ProfilePicture = await ImageHelper.GetBitmapAsync(outcome.Student.PhotoData);

                LiveStudentName.Text = outcome.Student.FullName;
                LiveStudentDetails.Text = $"{outcome.Student.Course} • Year {outcome.Student.YearLevel} • {outcome.Student.SectionName}";

                if (outcome.IsGranted)
                {
                    LiveResultBorder.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(26, 52, 211, 153));
                    LiveResultBorder.BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(48, 52, 211, 153));
                    LiveResultText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 52, 211, 153));
                    LiveResultText.Text = "ATTENDANCE GRANTED";
                }
                else
                {
                    LiveResultBorder.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(26, 248, 113, 113));
                    LiveResultBorder.BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(48, 248, 113, 113));
                    LiveResultText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 248, 113, 113));
                    LiveResultText.Text = $"DENIED: {outcome.ErrorCategory.Replace("_", " ")}";
                }

                LiveFeedIdlePanel.Visibility = Visibility.Collapsed;
                LiveFeedActivePanel.Visibility = Visibility.Visible;

                _liveFeedTimer.Stop();
                _liveFeedTimer.Start();
            }
            else
            {
                OverrideStudentIdBox.Text = "";
            }
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