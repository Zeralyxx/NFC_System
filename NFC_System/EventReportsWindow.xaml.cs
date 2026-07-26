using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
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

        // Caching for Event filtering
        private List<AttendanceLog> _eventMasterLogs = new();
        private List<string> _completedStudentIds = new();
        private List<string> _incompleteStudentIds = new();

        // Caching for University filtering
        private List<VerificationLogRecord> _univMasterLogs = new();

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

                // 1. Load Event Dropdown
                IReadOnlyList<EventRecord> allEvents = await _database.GetAllEventsAsync();
                EventComboBox.ItemsSource = allEvents;

                // 2. Load General University Analytics
                var metrics = await _database.GetUniversityMetricsAsync();
                UnivTotalScansText.Text = metrics.TotalScansToday.ToString("N0");
                UnivInsideText.Text = metrics.CurrentlyInside.ToString("N0");
                UnivDeniedText.Text = metrics.DeniedToday.ToString("N0");

                var dailyStats = await _database.GetDailyEntryStatsAsync();
                DailyEntriesItemsControl.ItemsSource = dailyStats;

                // Load Historical Security Extremes
                var alertExtremes = await _database.GetSecurityAlertExtremesAsync();
                HighAlertDateText.Text = alertExtremes.HighDayLabel;
                HighAlertCountText.Text = alertExtremes.HighCount.ToString();
                LowAlertDateText.Text = alertExtremes.LowDayLabel;
                LowAlertCountText.Text = alertExtremes.LowCount.ToString();

                // ---> NEW: Load Full Historical Threat Log <---
                var allTimeThreats = await _database.GetDailySecurityAlertsAsync();
                HistoricalThreatsListView.ItemsSource = allTimeThreats;

                // 3. Load General University Ledger & Dropdowns
                var univLogs = await _database.GetGeneralLedgerAsync();
                _univMasterLogs = univLogs.ToList();

                var courses = _univMasterLogs.Select(l => l.Course).Where(c => !string.IsNullOrWhiteSpace(c)).Distinct().OrderBy(c => c).ToList();
                courses.Insert(0, "All Courses");
                UnivFilterCourse.ItemsSource = courses;

                var sections = _univMasterLogs.Select(l => l.Section).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().OrderBy(s => s).ToList();
                sections.Insert(0, "All Sections");
                UnivFilterSection.ItemsSource = sections;

                ClearUnivFilters_Click(null, null); // Applies default full list
            }
            catch { }
        }

        // --- TAB TOGGLE LOGIC ---

        private void UniversityModeBtn_Click(object sender, RoutedEventArgs e)
        {
            UniversityViewGrid.Visibility = Visibility.Visible;
            EventViewGrid.Visibility = Visibility.Collapsed;

            UniversityModeBtn.Background = (SolidColorBrush)Application.Current.Resources["PrimaryAccentBrush"];
            UniversityModeBtn.Foreground = new SolidColorBrush(Microsoft.UI.Colors.White);
            UniversityModeBtn.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
            UniversityModeBtn.BorderThickness = new Thickness(0);

            EventModeBtn.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(26, 255, 255, 255));
            EventModeBtn.BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(48, 255, 255, 255));
            EventModeBtn.BorderThickness = new Thickness(1);
            EventModeBtn.Foreground = new SolidColorBrush(Microsoft.UI.Colors.White);
            EventModeBtn.FontWeight = Microsoft.UI.Text.FontWeights.Normal;
        }

        private void EventModeBtn_Click(object sender, RoutedEventArgs e)
        {
            UniversityViewGrid.Visibility = Visibility.Collapsed;
            EventViewGrid.Visibility = Visibility.Visible;

            EventModeBtn.Background = (SolidColorBrush)Application.Current.Resources["PrimaryAccentBrush"];
            EventModeBtn.Foreground = new SolidColorBrush(Microsoft.UI.Colors.White);
            EventModeBtn.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
            EventModeBtn.BorderThickness = new Thickness(0);

            UniversityModeBtn.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(26, 255, 255, 255));
            UniversityModeBtn.BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(48, 255, 255, 255));
            UniversityModeBtn.BorderThickness = new Thickness(1);
            UniversityModeBtn.Foreground = new SolidColorBrush(Microsoft.UI.Colors.White);
            UniversityModeBtn.FontWeight = Microsoft.UI.Text.FontWeights.Normal;
        }

        // --- UNIVERSITY LEDGER FILTERING ---

        private void UnivFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplyUnivLedgerFilters();
        }

        private void ClearUnivFilters_Click(object sender, RoutedEventArgs e)
        {
            if (UnivFilterCourse == null || UnivFilterSection == null || UnivFilterStatus == null) return;

            if (UnivFilterCourse.Items.Count > 0) UnivFilterCourse.SelectedIndex = 0;
            if (UnivFilterSection.Items.Count > 0) UnivFilterSection.SelectedIndex = 0;
            UnivFilterStatus.SelectedIndex = 0;
        }

        private void ApplyUnivLedgerFilters()
        {
            if (_univMasterLogs == null || UnivFilterCourse == null || UnivFilterSection == null || UnivFilterStatus == null || UniversityAuditListView == null)
                return;

            var filtered = _univMasterLogs.AsEnumerable();

            string course = UnivFilterCourse.SelectedItem?.ToString() ?? "All Courses";
            string section = UnivFilterSection.SelectedItem?.ToString() ?? "All Sections";
            string status = (UnivFilterStatus.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All Statuses";

            if (course != "All Courses")
                filtered = filtered.Where(l => l.Course == course);

            if (section != "All Sections")
                filtered = filtered.Where(l => l.Section == section);

            if (status != "All Statuses")
                filtered = filtered.Where(l => l.Status == status);

            UniversityAuditListView.ItemsSource = filtered.ToList();
        }


        // --- EVENT LOGIC ---

        private async void EventComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (EventComboBox.SelectedItem is EventRecord selectedEvent)
            {
                ExportButton.IsEnabled = true;

                try
                {
                    var logs = await _database.GetEventAttendanceLogsAsync(selectedEvent.EventId);
                    _eventMasterLogs = logs.ToList();

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
                                ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 52, 211, 153))
                                : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 248, 113, 113));
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
                        TurnoutRateText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255));
                    }

                    var courses = _eventMasterLogs.Select(l => l.Course).Where(c => !string.IsNullOrWhiteSpace(c)).Distinct().OrderBy(c => c).ToList();
                    courses.Insert(0, "All Courses");
                    FilterCourseComboBox.ItemsSource = courses;

                    var sections = _eventMasterLogs.Select(l => l.Section).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().OrderBy(s => s).ToList();
                    sections.Insert(0, "All Sections");
                    FilterSectionComboBox.ItemsSource = sections;

                    ClearFilters_Click(null, null);
                }
                catch { }
            }
        }

        private void Filter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplyLedgerFilters();
        }

        private void ClearFilters_Click(object sender, RoutedEventArgs e)
        {
            if (FilterTimeComboBox == null || FilterStatusComboBox == null) return;

            if (FilterCourseComboBox.Items.Count > 0) FilterCourseComboBox.SelectedIndex = 0;
            if (FilterSectionComboBox.Items.Count > 0) FilterSectionComboBox.SelectedIndex = 0;

            FilterStatusComboBox.SelectedIndex = 0;
            FilterTimeComboBox.SelectedIndex = 0;
        }

        private void ApplyLedgerFilters()
        {
            if (_eventMasterLogs == null ||
                FilterCourseComboBox == null || FilterSectionComboBox == null ||
                FilterStatusComboBox == null || FilterTimeComboBox == null ||
                AttendanceListView == null)
                return;

            var filtered = _eventMasterLogs.AsEnumerable();

            string course = FilterCourseComboBox.SelectedItem?.ToString() ?? "All Courses";
            string section = FilterSectionComboBox.SelectedItem?.ToString() ?? "All Sections";
            string status = (FilterStatusComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All Attendees";
            string time = (FilterTimeComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All Times";

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

        private async void ExportButton_Click(object sender, RoutedEventArgs e)
        {
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

            var picker = new Windows.Storage.Pickers.FileSavePicker();

            IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
            picker.FileTypeChoices.Add("Excel CSV Document", new List<string>() { ".csv" });
            picker.SuggestedFileName = $"Attendance_Report_{DateTime.Now:yyyyMMdd}";

            Windows.Storage.StorageFile file = await picker.PickSaveFileAsync();

            if (file != null)
            {
                var csvData = new System.Text.StringBuilder();

                csvData.AppendLine("Timestamp,Student ID,Student Name,Course,Section,Action,Auth Mode");

                foreach (var log in logsToExport)
                {
                    csvData.AppendLine($"\"{log.Timestamp}\",\"{log.StudentId}\",\"{log.FullName}\",\"{log.Course}\",\"{log.Section}\",\"{log.Status}\",\"{log.Mode}\"");
                }

                Windows.Storage.CachedFileManager.DeferUpdates(file);
                await Windows.Storage.FileIO.WriteTextAsync(file, csvData.ToString(), Windows.Storage.Streams.UnicodeEncoding.Utf8);
                Windows.Storage.Provider.FileUpdateStatus status = await Windows.Storage.CachedFileManager.CompleteUpdatesAsync(file);

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