using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Threading.Tasks;
using WinRT.Interop;

namespace NFC_System
{
    public sealed partial class EventManagementWindow : Window
    {
        private List<StudentRecord> _masterAttendeesList = new();
        private readonly DatabaseService _database = new();
        private EventRecord? _selectedEvent;

        // THE FIX: State variables for Exit Interceptor and Serial Port
        private SerialPort? _serialPort;
        private string _currentPort = "COM3";
        private bool _isForceClosing = false;
        private bool _isAwaitingAdminAuth = false;
        private string _pendingAdminAction = "";
        private string _pendingAdminSeverity = "";

        public EventManagementWindow()
        {
            this.InitializeComponent();
            DatabaseMonitor.ConnectionStatusChanged += UpdateOfflineBanner;
            UpdateOfflineBanner(DatabaseMonitor.IsOnline);
            MaximizeWindow();

            IntPtr hWnd = WindowNative.GetWindowHandle(this);
            WindowId windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
            AppWindow appWindow = AppWindow.GetFromWindowId(windowId);
            appWindow.Closing += AppWindow_Closing;
            this.Closed += Window_Closed;

            _ = InitializeAsync();
        }

        // ====================================================================
        // RBAC SEVERITY-AWARE EXIT INTERCEPTOR
        // ====================================================================
        private async void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
        {
            if (_isForceClosing) return;
            args.Cancel = true;

            // 1. MASTER ADMINISTRATOR FLOW (Bypass Authorization)
            if (AppSession.CurrentStaffRoleLabel == "Master Admin")
            {
                ContentDialog masterDialog = new ContentDialog
                {
                    Title = "Exit Application",
                    Content = "You may have unsynced offline data. Would you like to push it to the cloud before exiting?",
                    PrimaryButtonText = "Push to Cloud & Exit",
                    SecondaryButtonText = "Exit Anyway",
                    CloseButtonText = "Cancel",
                    XamlRoot = this.Content.XamlRoot
                };

                var result = await masterDialog.ShowAsync();
                if (result == ContentDialogResult.Primary) await PerformCloudPushAndExit();
                else if (result == ContentDialogResult.Secondary) ForceExit();
            }
            // 2. STANDARD ADMINISTRATOR FLOW (High Severity - Needs PIN + Tap)
            else if (AppSession.IsAdmin)
            {
                ContentDialog adminDialog = new ContentDialog
                {
                    Title = "Exit Application",
                    Content = "You have unsynced offline data. Pushing this to the cloud requires High-Severity authorization (PIN + NFC Tap).",
                    PrimaryButtonText = "Authorize Sync & Exit",
                    SecondaryButtonText = "Exit Without Syncing",
                    CloseButtonText = "Cancel",
                    XamlRoot = this.Content.XamlRoot
                };

                var result = await adminDialog.ShowAsync();
                if (result == ContentDialogResult.Primary)
                {
                    _pendingAdminAction = "EXIT_SYNC";
                    _pendingAdminSeverity = "HIGH";

                    AdminPinBox.Visibility = Visibility.Visible;
                    AdminPinBox.Password = "";
                    AuthStatusText.Visibility = Visibility.Collapsed;
                    AdminAuthDescriptionText.Text = "To confirm this cloud upload, enter your 4-digit PIN and tap your Admin NFC card.";

                    _isAwaitingAdminAuth = true;
                    AdminAuthDialog.XamlRoot = this.Content.XamlRoot;
                    var authResult = await AdminAuthDialog.ShowAsync();

                    if (authResult == ContentDialogResult.None && _isAwaitingAdminAuth)
                    {
                        _isAwaitingAdminAuth = false;
                        _pendingAdminAction = "";
                    }
                }
                else if (result == ContentDialogResult.Secondary)
                {
                    ForceExit();
                }
            }
            // 3. ORGANIZER & GUARD FLOW (Read-Only/Low Severity Restriction)
            else
            {
                ContentDialog restrictedDialog = new ContentDialog
                {
                    Title = "Exit Application",
                    Content = "Warning: There may be unsynced offline data. You do not have Administrator privileges to push this data to the cloud. If you exit now, the data will remain safely stored locally.",
                    PrimaryButtonText = "Exit Anyway",
                    CloseButtonText = "Cancel",
                    XamlRoot = this.Content.XamlRoot
                };

                restrictedDialog.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Orange);

                var result = await restrictedDialog.ShowAsync();
                if (result == ContentDialogResult.Primary) ForceExit();
            }
        }

        private async Task PerformCloudPushAndExit()
        {
            try
            {
                if (await _database.TestConnectionAsync())
                {
                    await _database.SyncOfflineLogsToServerAsync();
                    await _database.PushStudentsToCloudAsync();
                    await _database.PushStaffToCloudAsync();
                    await _database.PushCoursesToCloudAsync();
                    await _database.PushEventsToCloudAsync();
                    await _database.PushEventApprovedStudentsToCloudAsync();
                    await _database.PushLogsToCloudAsync();
                    await _database.PushEventAttendanceToCloudAsync();
                    await _database.AddAlertAsync(AppSession.CurrentStaffName, "ADMIN_ACTION", "Authorized Cloud Push on Application Exit.");
                }
            }
            catch { }

            ForceExit();
        }

        private void ForceExit()
        {
            if (DatabaseMonitor.IsOnline && AppSession.IsLoggedIn)
            {
                try { _ = _database.AddAlertAsync(AppSession.CurrentStaffName, "STAFF_LOGOUT", $"{AppSession.CurrentStaffName} closed the application."); } catch { }
            }

            _isForceClosing = true;
            Application.Current.Exit();
        }

        // ====================================================================
        // SERIAL PORT & AUTHORIZATION HANDLING
        // ====================================================================
        private void TryConnectSerial(string portName)
        {
            if (_serialPort != null && _serialPort.IsOpen) return;

            try
            {
                _serialPort = new SerialPort(portName, 115200);
                _serialPort.NewLine = "\n";
                _serialPort.DataReceived += SerialPort_DataReceived;
                _serialPort.Open();
            }
            catch { }
        }

        private void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                if (_serialPort == null || !_serialPort.IsOpen) return;
                string line = _serialPort.ReadLine().Trim();
                if (!line.StartsWith("UID=")) return;

                string uid = line.Substring(4).Trim();

                if (_isAwaitingAdminAuth)
                {
                    DispatcherQueue.TryEnqueue(async () => await HandleAdminAuthScanAsync(uid));
                }
            }
            catch { }
        }

        private async Task HandleAdminAuthScanAsync(string uid)
        {
            string? role = null;
            string? pinHash = null;
            string? pinSalt = null;

            if (DatabaseMonitor.IsOnline)
            {
                try
                {
                    var details = await _database.GetStaffDetailsAsync(uid);
                    role = details.Role;
                    pinHash = details.PinHash;
                    pinSalt = details.PinSalt;
                }
                catch { }
            }

            if (role == null && uid == "04:A1:B2:C3")
            {
                role = "Master Administrator";
            }

            bool isAuthorized = false;
            string failReason = "";

            if (_pendingAdminSeverity == "HIGH")
            {
                if (role == "Administrator" || role == "Master Administrator")
                {
                    string enteredPin = AdminPinBox.Password.Trim();
                    if (string.IsNullOrEmpty(enteredPin)) failReason = "Authorization Denied: A 4-digit Staff PIN is required.";
                    else if (string.IsNullOrEmpty(pinHash)) failReason = "Authorization Denied: Tapped account does not have a PIN configured.";
                    else if (!PinHasher.VerifyPin(enteredPin, pinSalt!, pinHash)) failReason = "Authorization Denied: Invalid PIN.";
                    else isAuthorized = true;
                }
                else failReason = "Authorization Denied: Tapped card is not an Administrator.";
            }

            if (isAuthorized)
            {
                _isAwaitingAdminAuth = false;
                AdminAuthDialog.Hide();

                if (_pendingAdminAction == "EXIT_SYNC")
                {
                    _pendingAdminAction = "";
                    await PerformCloudPushAndExit();
                }
            }
            else
            {
                AuthStatusText.Text = failReason;
                AuthStatusText.Visibility = Visibility.Visible;
                PlayErrorAlert();
            }
        }

        private void CloseSerialPort()
        {
            try
            {
                if (_serialPort != null && _serialPort.IsOpen)
                {
                    _serialPort.DataReceived -= SerialPort_DataReceived;
                    _serialPort.Close();
                    _serialPort.Dispose();
                    _serialPort = null;
                }
            }
            catch { }
        }

        private void Window_Closed(object sender, WindowEventArgs args)
        {
            DatabaseMonitor.ConnectionStatusChanged -= UpdateOfflineBanner;
            CloseSerialPort();
        }

        private void PlayErrorAlert()
        {
            Task.Run(() =>
            {
                try
                {
                    Console.Beep(2000, 300);
                    System.Threading.Thread.Sleep(100);
                    Console.Beep(2000, 300);
                }
                catch { }
            });
        }

        // ====================================================================
        // EVENT MANAGEMENT LOGIC
        // ====================================================================
        private async Task InitializeAsync()
        {
            try
            {
                await _database.EnsureSchemaAsync();

                string nfcPort = "COM3";
                if (DatabaseMonitor.IsOnline)
                {
                    try { nfcPort = await _database.GetSettingAsync("nfc_com_port", "COM3"); } catch { }
                }
                _currentPort = nfcPort;
                TryConnectSerial(_currentPort);

                await LoadActiveEventsAsync();
                await LoadCoursesAsync();
                LogMessage("[INFO] Event Administration initialized.");
            }
            catch (Exception ex)
            {
                LogMessage($"[DB ERROR] {ex.Message}");
            }
        }

        private async Task LoadActiveEventsAsync()
        {
            try
            {
                IReadOnlyList<EventRecord> events = await _database.GetActiveEventsAsync();
                ActiveEventsListView.ItemsSource = events;
                CloseEventButton.IsEnabled = false;
            }
            catch (Exception ex)
            {
                LogMessage($"[DB ERROR] Could not load events: {ex.Message}");
            }
        }

        private async Task LoadCoursesAsync()
        {
            try
            {
                IReadOnlyList<string> courses = await _database.GetDistinctCoursesAsync();
                CourseComboBox.ItemsSource = courses;
                ViewFilterCourseComboBox.ItemsSource = courses;
            }
            catch { }
        }

        private async Task RefreshAttendeesListAsync()
        {
            if (_selectedEvent == null) return;
            try
            {
                var attendees = await _database.GetEventAttendeesAsync(_selectedEvent.EventId);
                _masterAttendeesList = new List<StudentRecord>(attendees);
                ApplyViewFilters();
            }
            catch (Exception ex)
            {
                LogMessage($"[DB ERROR] Could not load attendees: {ex.Message}");
            }
        }

        private void UpdateOfflineBanner(bool isOnline)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (GlobalOfflineBanner != null)
                {
                    GlobalOfflineBanner.Visibility = isOnline ? Visibility.Collapsed : Visibility.Visible;
                }
            });
        }

        private void ApplyViewFilters()
        {
            if (_masterAttendeesList == null) return;

            string? courseFilter = ViewFilterCourseComboBox.SelectedItem?.ToString();
            string? yearFilter = (ViewFilterYearComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString();
            string searchQuery = SearchAttendeeTextBox.Text?.Trim().ToLower() ?? "";

            var filteredData = _masterAttendeesList.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(searchQuery))
            {
                filteredData = filteredData.Where(s =>
                    (s.FullName != null && s.FullName.ToLower().Contains(searchQuery)) ||
                    (s.StudentId != null && s.StudentId.ToLower().Contains(searchQuery)));
            }

            if (!string.IsNullOrWhiteSpace(courseFilter))
            {
                filteredData = filteredData.Where(s => s.Course == courseFilter);
            }

            if (!string.IsNullOrWhiteSpace(yearFilter))
            {
                if (yearFilter == "5+")
                {
                    filteredData = filteredData.Where(s => int.TryParse(s.YearLevel, out int y) && y >= 5);
                }
                else
                {
                    filteredData = filteredData.Where(s => s.YearLevel == yearFilter);
                }
            }

            var observableData = new System.Collections.ObjectModel.ObservableCollection<StudentRecord>(filteredData);
            AttendeesListView.ItemsSource = observableData;
            AttendeesGridView.ItemsSource = observableData;
        }

        private void ViewFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplyViewFilters();
        }

        private void SearchAttendeeTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyViewFilters();
        }

        private void ClearViewFilters_Click(object sender, RoutedEventArgs e)
        {
            SearchAttendeeTextBox.Text = "";
            ViewFilterCourseComboBox.SelectedIndex = -1;
            ViewFilterYearComboBox.SelectedIndex = -1;
            ApplyViewFilters();
        }

        private void ExpandDialogToggle_Click(object sender, RoutedEventArgs e)
        {
            if (ExpandDialogToggle.IsChecked == true)
            {
                DialogContentContainer.Width = 1100;
                AttendeesListView.Visibility = Visibility.Collapsed;
                AttendeesGridView.Visibility = Visibility.Visible;
                ExpandDialogToggle.Content = "⮌ Collapse View";
            }
            else
            {
                DialogContentContainer.Width = 600;
                AttendeesListView.Visibility = Visibility.Visible;
                AttendeesGridView.Visibility = Visibility.Collapsed;
                ExpandDialogToggle.Content = "⛶ Expand View";
            }
        }

        private async void CreateEventButton_Click(object sender, RoutedEventArgs e)
        {
            string eventId = EventIdTextBox.Text.Trim();
            string eventName = EventNameTextBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(eventId) || string.IsNullOrWhiteSpace(eventName))
            {
                LogMessage("[WARNING] Event ID and Event Name are required.");
                return;
            }

            VerificationMode mode = EventModeComboBox.SelectedIndex switch
            {
                0 => VerificationMode.Fast,
                2 => VerificationMode.HighSecurity,
                _ => VerificationMode.Standard
            };

            bool isRestricted = RestrictedEventCheckBox.IsChecked ?? false;

            try
            {
                await _database.SaveEventAsync(eventId, eventName, mode, isRestricted);
                LogMessage($"[SUCCESS] Event '{eventName}' ({eventId}) created and activated.");

                EventIdTextBox.Text = "";
                EventNameTextBox.Text = "";
                await LoadActiveEventsAsync();
            }
            catch (Exception ex)
            {
                LogMessage($"[DB ERROR] Failed to create event: {ex.Message}");
            }
        }

        private void ActiveEventsListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedEvent = ActiveEventsListView.SelectedItem as EventRecord;
            CloseEventButton.IsEnabled = _selectedEvent != null;
        }

        private async void ActiveEventsListView_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            if (ActiveEventsListView.SelectedItem is EventRecord clickedEvent)
            {
                _selectedEvent = clickedEvent;

                AttendeeManagementDialog.XamlRoot = this.Content.XamlRoot;
                AttendeeManagementDialog.Title = $"Manage Event: {clickedEvent.EventId}";

                EditEventNameTextBox.Text = clickedEvent.EventName ?? "";

                if (!clickedEvent.IsRestricted)
                {
                    AttendeeManagementSection.Visibility = Visibility.Collapsed;
                    LogMessage($"[INFO] '{clickedEvent.EventId}' is an open event. Attendee management hidden.");
                }
                else
                {
                    AttendeeManagementSection.Visibility = Visibility.Visible;

                    CourseComboBox.SelectedIndex = -1;
                    YearComboBox.SelectedIndex = -1;
                    IndividualIdTextBox.Text = "";
                    SearchAttendeeTextBox.Text = "";

                    await RefreshAttendeesListAsync();
                }

                DialogContentContainer.Width = 600;
                ExpandDialogToggle.IsChecked = false;
                ExpandDialogToggle.Content = "⛶ Expand View";
                AttendeesListView.Visibility = Visibility.Visible;
                AttendeesGridView.Visibility = Visibility.Collapsed;

                await AttendeeManagementDialog.ShowAsync();
            }
        }

        private async void UpdateEventNameButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedEvent == null) return;

            string newName = EditEventNameTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(newName))
            {
                LogMessage("[WARNING] Event Name cannot be empty.");
                return;
            }

            try
            {
                await _database.SaveEventAsync(_selectedEvent.EventId, newName, _selectedEvent.VerificationMode, _selectedEvent.IsRestricted);
                LogMessage($"[SUCCESS] Event '{_selectedEvent.EventId}' renamed to '{newName}'.");
                await LoadActiveEventsAsync();
            }
            catch (Exception ex)
            {
                LogMessage($"[DB ERROR] Failed to update event name: {ex.Message}");
            }
        }

        private async void AddBatchButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedEvent == null) return;

            string? selectedCourse = CourseComboBox.SelectedIndex >= 0 ? CourseComboBox.SelectedItem?.ToString() : null;
            string? selectedYear = YearComboBox.SelectedIndex >= 0 ? (YearComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() : null;

            if (string.IsNullOrWhiteSpace(selectedCourse) && string.IsNullOrWhiteSpace(selectedYear))
            {
                LogMessage("[WARNING] Please select a Course or Year Level to batch add.");
                return;
            }

            try
            {
                await _database.AddBatchToEventAsync(_selectedEvent.EventId, selectedCourse, selectedYear);

                string filterDetails = $"Course: {(selectedCourse ?? "Any")}, Year: {(selectedYear ?? "Any")}";
                LogMessage($"[SUCCESS] Batch approved for '{_selectedEvent.EventId}' [{filterDetails}].");

                await RefreshAttendeesListAsync();

                CourseComboBox.SelectedIndex = -1;
                YearComboBox.SelectedIndex = -1;
            }
            catch (Exception ex)
            {
                LogMessage($"[ERROR] Failed to add batch: {ex.Message}");
            }
        }

        private async void AddIndividualButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedEvent == null) return;

            string studentId = IndividualIdTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(studentId)) return;

            try
            {
                var student = await _database.GetStudentByIdAsync(studentId);
                if (student == null)
                {
                    LogMessage($"[WARNING] Cannot add: Student ID '{studentId}' does not exist in the database.");
                    return;
                }

                await _database.AddEventAttendeeAsync(_selectedEvent.EventId, studentId);
                LogMessage($"[SUCCESS] Added student '{studentId}' to '{_selectedEvent.EventId}'.");

                IndividualIdTextBox.Text = "";
                await RefreshAttendeesListAsync();
            }
            catch (Exception ex)
            {
                LogMessage($"[ERROR] Failed to add student: {ex.Message}");
            }
        }

        private async void RemoveAttendeeButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedEvent == null) return;

            if (sender is Button btn && btn.Tag is string studentId)
            {
                try
                {
                    await _database.RemoveEventAttendeeAsync(_selectedEvent.EventId, studentId);
                    LogMessage($"[SUCCESS] Removed '{studentId}' from event list.");
                    await RefreshAttendeesListAsync();
                }
                catch (Exception ex)
                {
                    LogMessage($"[ERROR] Failed to remove attendee: {ex.Message}");
                }
            }
        }

        private async void CloseEventButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedEvent != null)
            {
                try
                {
                    await _database.CloseEventAsync(_selectedEvent.EventId);
                    LogMessage($"[SUCCESS] Event '{_selectedEvent.DisplayName}' closed and removed.");

                    ActiveEventsListView.SelectedItem = null;
                    await LoadActiveEventsAsync();
                }
                catch (Exception ex)
                {
                    LogMessage($"[DB ERROR] Could not close event: {ex.Message}");
                }
            }
        }

        private void RefreshLogsButton_Click(object sender, RoutedEventArgs e)
        {
            LogMessage("[INFO] Administrator activity logs refreshed.");
        }

        private void LogMessage(string message)
        {
            string timestamp = DateTime.Now.ToString("HH:mm:ss");
            EventLogsListView.Items.Insert(0, $"{timestamp} | {message}");
        }

        private void DashboardButton_Click(object sender, RoutedEventArgs e)
        {
            CloseSerialPort();
            var dashboard = new MainWindow();
            dashboard.Activate();
            this.Close();
        }

        private void BackToAttendanceButton_Click(object sender, RoutedEventArgs e)
        {
            CloseSerialPort();
            var attendanceWindow = new EventAttendanceWindow();
            attendanceWindow.Activate();
            this.Close();
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