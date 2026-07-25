using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WinRT.Interop;

namespace NFC_System
{
    public class StatItem
    {
        public string Label { get; set; } = "";
        public string Value { get; set; } = "";
        public string SubValue { get; set; } = "";
    }

    public sealed partial class EventReportsWindow : Window
    {
        private readonly DatabaseService _database = new();

        // Caching for rapid UI filtering
        private List<AttendanceLog> _masterLogs = new();
        private List<string> _completedStudentIds = new();
        private List<string> _incompleteStudentIds = new();

        public EventReportsWindow()
        {
            this.InitializeComponent();
            MaximizeWindow();
            _ = InitializeAsync();
        }

        private async Task InitializeAsync()
        {
            try
            {
                await _database.EnsureSchemaAsync();
                IReadOnlyList<EventRecord> allEvents = await _database.GetAllEventsAsync();
                EventComboBox.ItemsSource = allEvents;
            }
            catch { }
        }

        private async void EventComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (EventComboBox.SelectedItem is EventRecord selectedEvent)
            {
                ExportButton.IsEnabled = true;

                try
                {
                    // 1. Fetch the raw ledger and store it in memory
                    var logs = await _database.GetEventAttendanceLogsAsync(selectedEvent.EventId);
                    _masterLogs = logs.ToList();

                    // 2. RETENTION MATH: Find unique entries vs unique exits
                    var enteredStudents = logs.Where(l => l.Status == "PRESENT").Select(l => l.StudentId).Distinct().ToList();
                    var exitedStudents = logs.Where(l => l.Status == "DEPARTED").Select(l => l.StudentId).Distinct().ToList();

                    _completedStudentIds = enteredStudents.Intersect(exitedStudents).ToList();
                    _incompleteStudentIds = enteredStudents.Except(exitedStudents).ToList();

                    int totalEntered = enteredStudents.Count;
                    int totalCompleted = _completedStudentIds.Count;

                    AttendedCountText.Text = totalEntered.ToString();

                    if (totalEntered > 0)
                    {
                        double retention = ((double)totalCompleted / totalEntered) * 100;
                        RetentionRateText.Text = $"{retention:F1}%";
                        RetentionSubText.Text = $"{totalCompleted} out of {totalEntered} attendees checked out";

                        var arrivalsOnly = logs.Where(l => l.Status == "PRESENT").ToList();

                        var courseStats = arrivalsOnly
                            .GroupBy(l => string.IsNullOrWhiteSpace(l.Course) ? "Unregistered Course" : l.Course)
                            .Select(g => new StatItem { Label = g.Key, Value = g.Count().ToString(), SubValue = $"{(g.Count() * 100.0 / totalEntered):F1}%" })
                            .OrderByDescending(x => int.Parse(x.Value)).ToList();
                        CourseBreakdownItemsControl.ItemsSource = courseStats;

                        var sectionStats = arrivalsOnly
                            .GroupBy(l => string.IsNullOrWhiteSpace(l.Section) ? "Unassigned" : l.Section)
                            .Select(g => new StatItem { Label = g.Key, Value = g.Count().ToString(), SubValue = $"{(g.Count() * 100.0 / totalEntered):F1}%" })
                            .OrderByDescending(x => int.Parse(x.Value)).Take(5).ToList();
                        SectionBreakdownItemsControl.ItemsSource = sectionStats;

                        var modeStats = arrivalsOnly
                            .GroupBy(l => string.IsNullOrWhiteSpace(l.Mode) ? "Unknown" : l.Mode)
                            .Select(g => new StatItem { Label = g.Key + " Mode", Value = g.Count().ToString() })
                            .OrderByDescending(x => int.Parse(x.Value)).ToList();
                        AuthModesItemsControl.ItemsSource = modeStats;
                    }
                    else
                    {
                        RetentionRateText.Text = "0%";
                        RetentionSubText.Text = "No arrivals recorded";
                        CourseBreakdownItemsControl.ItemsSource = null;
                        SectionBreakdownItemsControl.ItemsSource = null;
                        AuthModesItemsControl.ItemsSource = null;
                    }

                    if (selectedEvent.IsRestricted)
                    {
                        var approvedList = await _database.GetEventAttendeesAsync(selectedEvent.EventId);
                        int expected = approvedList.Count;
                        ExpectedCountText.Text = $"{expected} Registered Students";

                        if (expected > 0)
                        {
                            double rate = ((double)totalEntered / expected) * 100;
                            TurnoutRateText.Text = $"{rate:F1}%";
                            TurnoutRateText.Foreground = rate > 75
                                ? new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 52, 211, 153))
                                : new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 248, 113, 113));
                        }
                        else
                        {
                            TurnoutRateText.Text = "0%";
                        }
                    }
                    else
                    {
                        ExpectedCountText.Text = "Open Event (No Restrictions)";
                        TurnoutRateText.Text = "N/A";
                        TurnoutRateText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255));
                    }

                    // --- Configure Filter Dropdowns ---
                    var courses = _masterLogs.Select(l => l.Course).Where(c => !string.IsNullOrWhiteSpace(c)).Distinct().OrderBy(c => c).ToList();
                    courses.Insert(0, "All Courses");
                    FilterCourseComboBox.ItemsSource = courses;

                    var sections = _masterLogs.Select(l => l.Section).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().OrderBy(s => s).ToList();
                    sections.Insert(0, "All Sections");
                    FilterSectionComboBox.ItemsSource = sections;

                    // Automatically applies the default state to the ledger
                    ClearFilters_Click(null, null);
                }
                catch { }
            }
        }

        // --- FILTERING LOGIC ---

        private void Filter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplyLedgerFilters();
        }

        private void ClearFilters_Click(object sender, RoutedEventArgs e)
        {
            // SAFETY CHECK: Ensure UI is fully built before resetting
            if (FilterTimeComboBox == null || FilterStatusComboBox == null) return;

            if (FilterCourseComboBox.Items.Count > 0) FilterCourseComboBox.SelectedIndex = 0;
            if (FilterSectionComboBox.Items.Count > 0) FilterSectionComboBox.SelectedIndex = 0;

            FilterStatusComboBox.SelectedIndex = 0;
            FilterTimeComboBox.SelectedIndex = 0;
        }

        private void ApplyLedgerFilters()
        {
            // SAFETY CHECK: Prevents the NullReferenceException during XAML initialization
            if (_masterLogs == null ||
                FilterCourseComboBox == null ||
                FilterSectionComboBox == null ||
                FilterStatusComboBox == null ||
                FilterTimeComboBox == null ||
                AttendanceListView == null) // <-- Added this check right here!
                return;

            var filtered = _masterLogs.AsEnumerable();

            string course = FilterCourseComboBox.SelectedItem?.ToString() ?? "All Courses";
            string section = FilterSectionComboBox.SelectedItem?.ToString() ?? "All Sections";
            string status = (FilterStatusComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All Attendees";
            string time = (FilterTimeComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All Times";

            // Apply Filters
            if (course != "All Courses")
                filtered = filtered.Where(l => l.Course == course);

            if (section != "All Sections")
                filtered = filtered.Where(l => l.Section == section);

            if (status == "Completed Event")
                filtered = filtered.Where(l => _completedStudentIds.Contains(l.StudentId));
            else if (status == "Incomplete / Left Early")
                filtered = filtered.Where(l => _incompleteStudentIds.Contains(l.StudentId));

            if (time == "Morning (AM)")
                filtered = filtered.Where(l => l.Timestamp.Contains("AM"));
            else if (time == "Afternoon (PM)")
                filtered = filtered.Where(l => l.Timestamp.Contains("PM"));

            AttendanceListView.ItemsSource = filtered.ToList();
        }

        // --- GENERAL MANAGEMENT ---

        private async void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            // 1. Grab whatever is currently filtered and showing on the screen
            var logsToExport = AttendanceListView.ItemsSource as IEnumerable<AttendanceLog>;

            if (logsToExport == null || !logsToExport.Any())
            {
                ContentDialog emptyDialog = new ContentDialog
                {
                    Title = "Nothing to Export",
                    Content = "There are no attendance records to export based on your current filters.",
                    CloseButtonText = "OK",
                    XamlRoot = this.Content.XamlRoot
                };
                await emptyDialog.ShowAsync();
                return;
            }

            // 2. Create the native Windows Save Dialog
            var picker = new Windows.Storage.Pickers.FileSavePicker();

            // 3. WinUI 3 requires us to bind the picker to the window's Handle (HWND)
            IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            // 4. Configure it for Excel (CSV format opens natively in Excel with perfect columns)
            picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
            picker.FileTypeChoices.Add("Excel CSV Document", new List<string>() { ".csv" });
            picker.SuggestedFileName = $"Attendance_Report_{DateTime.Now:yyyyMMdd}";

            // 5. Open the dialog and wait for the user to pick a folder and click "Save"
            Windows.Storage.StorageFile file = await picker.PickSaveFileAsync();

            if (file != null)
            {
                // 6. Build the Excel Data
                var csvData = new System.Text.StringBuilder();

                // Add the Column Headers
                csvData.AppendLine("Timestamp,Student ID,Student Name,Course,Section,Action,Auth Mode");

                // Add the Data Rows
                foreach (var log in logsToExport)
                {
                    // We wrap everything in quotes ("") so that if a course name contains a comma, 
                    // it doesn't accidentally break the Excel columns!
                    csvData.AppendLine($"\"{log.Timestamp}\",\"{log.StudentId}\",\"{log.FullName}\",\"{log.Course}\",\"{log.Section}\",\"{log.Status}\",\"{log.Mode}\"");
                }

                // 7. Write the data to the hard drive
                Windows.Storage.CachedFileManager.DeferUpdates(file);
                await Windows.Storage.FileIO.WriteTextAsync(file, csvData.ToString(), Windows.Storage.Streams.UnicodeEncoding.Utf8);
                Windows.Storage.Provider.FileUpdateStatus status = await Windows.Storage.CachedFileManager.CompleteUpdatesAsync(file);

                // 8. Confirm success with the exact file path
                if (status == Windows.Storage.Provider.FileUpdateStatus.Complete)
                {
                    ContentDialog successDialog = new ContentDialog
                    {
                        Title = "Export Complete",
                        Content = $"Your report was successfully exported and saved to:\n\n{file.Path}",
                        CloseButtonText = "OK",
                        XamlRoot = this.Content.XamlRoot
                    };
                    await successDialog.ShowAsync();
                }
            }
        }

        private void DashboardButton_Click(object sender, RoutedEventArgs e)
        {
            var dashboard = new MainWindow();
            dashboard.Activate();
            this.Close();
        }

        private void ManageEventsButton_Click(object sender, RoutedEventArgs e)
        {
            var adminWin = new EventManagementWindow();
            adminWin.Activate();
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