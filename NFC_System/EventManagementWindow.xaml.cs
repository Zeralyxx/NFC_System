using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.Collections.Generic;
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

        public EventManagementWindow()
        {
            this.InitializeComponent();
            // Subscribe to the live monitor
            DatabaseMonitor.ConnectionStatusChanged += UpdateOfflineBanner;
            UpdateOfflineBanner(DatabaseMonitor.IsOnline); // Set initial state on load
            MaximizeWindow();
            _ = InitializeAsync();
        }

        private async Task InitializeAsync()
        {
            try
            {
                await _database.EnsureSchemaAsync();
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
            // DispatcherQueue safely pushes the update to the UI thread
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

            // Sync the filtered data to BOTH the List view and the Grid view
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

        // Overrides ContentDialog boundaries to enable horizontal scrolling grid
        private void ExpandDialogToggle_Click(object sender, RoutedEventArgs e)
        {
            if (ExpandDialogToggle.IsChecked == true)
            {
                // Force the inner container to expand massively. 
                // The ContentDialogMaxWidth resource we added in XAML allows this to work!
                DialogContentContainer.Width = 1100;

                // Swap UI elements
                AttendeesListView.Visibility = Visibility.Collapsed;
                AttendeesGridView.Visibility = Visibility.Visible;

                ExpandDialogToggle.Content = "⮌ Collapse View";
            }
            else
            {
                // Return to default normal width
                DialogContentContainer.Width = 600;

                // Swap UI elements
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

                // Load Event Name into Editor
                EditEventNameTextBox.Text = clickedEvent.EventName ?? "";
                // Show or Hide Attendee Management based on Restriction
                if (!clickedEvent.IsRestricted)
                {
                    AttendeeManagementSection.Visibility = Visibility.Collapsed;
                    LogMessage($"[INFO] '{clickedEvent.EventId}' is an open event. Attendee management hidden.");
                }
                else
                {
                    AttendeeManagementSection.Visibility = Visibility.Visible;

                    // Reset all states and filters
                    CourseComboBox.SelectedIndex = -1;
                    YearComboBox.SelectedIndex = -1;
                    IndividualIdTextBox.Text = "";
                    SearchAttendeeTextBox.Text = "";

                    await RefreshAttendeesListAsync();
                }

                // Force collapse the dialog width on open
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
                // Reusing SaveEventAsync acts as an upsert (ON DUPLICATE KEY UPDATE) to overwrite the existing event parameters.
                await _database.SaveEventAsync(_selectedEvent.EventId, newName, _selectedEvent.VerificationMode, _selectedEvent.IsRestricted);
                LogMessage($"[SUCCESS] Event '{_selectedEvent.EventId}' renamed to '{newName}'.");

                // Refresh to reflect the new name in the background UI
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
                // GUARDRAIL: Verify the student actually exists in the database first!
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
            var dashboard = new MainWindow();
            dashboard.Activate();
            this.Close();
        }

        private void BackToAttendanceButton_Click(object sender, RoutedEventArgs e)
        {
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