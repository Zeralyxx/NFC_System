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

    public class EventAttendanceViewModel
    {
        public string Timestamp { get; set; } = "";
        public string FullName { get; set; } = "";
        public string StudentId { get; set; } = "";
        public string Course { get; set; } = "";
        public string Section { get; set; } = "";
        public string Action { get; set; } = "";
        public string CompletionStatus { get; set; } = "";
        public SolidColorBrush? CompletionColor { get; set; }
    }

    public class EventDropdownItem
    {
        public EventRecord Event { get; set; } = null!;
        public string DisplayText { get; set; } = "";
    }

    public sealed partial class EventReportsWindow : Window
    {
        private readonly DatabaseService _database = new();

        private List<AttendanceLog> _eventMasterLogs = new();
        private List<string> _completedStudentIds = new();
        private List<string> _incompleteStudentIds = new();
        private List<EventDropdownItem> _allEventDropdownItems = new();

        private EventDropdownItem? _currentSelectedEvent = null;

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

                IReadOnlyList<EventRecord> allEvents = await _database.GetAllEventsAsync();
                IReadOnlyList<EventRecord> activeEvents = await _database.GetActiveEventsAsync(9999);

                _allEventDropdownItems = allEvents.Select(e => new EventDropdownItem
                {
                    Event = e,
                    DisplayText = activeEvents.Any(a => a.EventId == e.EventId)
                        ? $"🟢 LIVE  -  {e.DisplayName}"
                        : $"🔴 CLOSED  -  {e.DisplayName}"
                })
                .OrderByDescending(x => x.DisplayText.StartsWith("🟢"))
                .ToList();

                EventSearchBox.ItemsSource = _allEventDropdownItems;

                var metrics = await _database.GetUniversityMetricsAsync();
                UnivTotalScansText.Text = metrics.TotalScansToday.ToString("N0");
                UnivInsideText.Text = metrics.CurrentlyInside.ToString("N0");
                UnivDeniedText.Text = metrics.DeniedToday.ToString("N0");

                var dailyStats = await _database.GetDailyEntryStatsAsync();
                DailyEntriesItemsControl.ItemsSource = dailyStats;

                var alertExtremes = await _database.GetSecurityAlertExtremesAsync();
                HighAlertDateText.Text = alertExtremes.HighDayLabel;
                HighAlertCountText.Text = alertExtremes.HighCount.ToString();
                LowAlertDateText.Text = alertExtremes.LowDayLabel;
                LowAlertCountText.Text = alertExtremes.LowCount.ToString();

                var allTimeThreats = await _database.GetDailySecurityAlertsAsync();
                HistoricalThreatsListView.ItemsSource = allTimeThreats;

                var univLogs = await _database.GetGeneralLedgerAsync();
                _univMasterLogs = univLogs.ToList();

                var courses = _univMasterLogs.Select(l => l.Course).Where(c => !string.IsNullOrWhiteSpace(c)).Distinct().OrderBy(c => c).ToList();
                courses.Insert(0, "All Courses");
                UnivFilterCourse.ItemsSource = courses;

                var sections = _univMasterLogs.Select(l => l.Section).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().OrderBy(s => s).ToList();
                sections.Insert(0, "All Sections");
                UnivFilterSection.ItemsSource = sections;

                ClearUnivFilters_Click(null, null);
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

            var finalData = filtered.ToList();
            UniversityAuditListView.ItemsSource = finalData;
        }

        // --- SMART SEARCH DROPDOWN LOGIC ---

        private void EventSearchBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(EventSearchBox.Text))
            {
                EventSearchBox.ItemsSource = _allEventDropdownItems.Take(50).ToList();
                EventSearchBox.IsSuggestionListOpen = true;
            }
        }

        private void Background_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            if (EventSearchBox.IsSuggestionListOpen)
            {
                EventSearchBox.IsSuggestionListOpen = false;
            }
        }

        private void EventSearchBox_LostFocus(object sender, RoutedEventArgs e)
        {
            EventSearchBox.IsSuggestionListOpen = false;
        }

        private void EventLeftScrollViewer_ViewChanging(object sender, ScrollViewerViewChangingEventArgs e)
        {
            if (EventSearchBox.IsSuggestionListOpen)
            {
                EventSearchBox.IsSuggestionListOpen = false;
            }
        }

        private void EventSearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            {
                string query = sender.Text.ToLower().Trim();

                if (string.IsNullOrWhiteSpace(query))
                {
                    sender.ItemsSource = _allEventDropdownItems.Take(50).ToList();
                }
                else
                {
                    sender.ItemsSource = _allEventDropdownItems
                        .Where(x => x.DisplayText.ToLower().Contains(query))
                        .ToList();
                }
            }
        }

        private async void EventSearchBox_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
        {
            if (args.SelectedItem is EventDropdownItem selectedWrapper)
            {
                sender.Text = selectedWrapper.DisplayText;
                _currentSelectedEvent = selectedWrapper;
                await ProcessEventSelectionAsync(selectedWrapper);
            }
        }

        // --- EVENT DATA LOADING LOGIC ---

        private async Task ProcessEventSelectionAsync(EventDropdownItem selectedWrapper)
        {
            EventRecord selectedEvent = selectedWrapper.Event;
            ExportButton.IsEnabled = true;

            try
            {
                var logs = await _database.GetEventAttendanceLogsAsync(selectedEvent.EventId);
                _eventMasterLogs = logs.ToList();

                var allAttendees = logs.Where(l => l.Status == "PRESENT").Select(l => l.StudentId).Distinct().ToList();

                var latestStudentLogs = logs.GroupBy(l => l.StudentId)
                                            .Select(g => g.First())
                                            .ToList();

                bool isEventLive = selectedWrapper.DisplayText.Contains("🟢 LIVE");

                if (isEventLive)
                {
                    _completedStudentIds = latestStudentLogs.Where(l => l.Status == "PRESENT").Select(l => l.StudentId).ToList();
                    _incompleteStudentIds = latestStudentLogs.Where(l => l.Status == "DEPARTED").Select(l => l.StudentId).ToList();
                }
                else
                {
                    _completedStudentIds = allAttendees.ToList();
                    _incompleteStudentIds = new List<string>();
                }

                int totalEntered = allAttendees.Count;
                int totalCompleted = _completedStudentIds.Count;

                AttendedCountText.Text = totalEntered.ToString();

                if (totalEntered > 0)
                {
                    double retention = ((double)totalCompleted / totalEntered) * 100;
                    RetentionRateText.Text = $"{retention:F1}%";
                    RetentionSubText.Text = $"{totalCompleted} out of {totalEntered} attendees completed the event";

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
                    CourseBreakdownItemsControl.ItemsSource = new List<StatItem>();
                    SectionBreakdownItemsControl.ItemsSource = new List<StatItem>();
                    AuthModesItemsControl.ItemsSource = new List<StatItem>();
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

                if (FilterStatusComboBox.Items.Count > 1 && FilterStatusComboBox.Items[1] is ComboBoxItem statusItem)
                {
                    statusItem.Content = isEventLive ? "Ongoing" : "Completed Event";
                }

                ClearFilters_Click(null, null);
            }
            catch { }
        }

        // --- EVENT LEDGER FILTERING ---

        private void Filter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplyLedgerFilters();
        }

        private void SearchEventNameTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyLedgerFilters();
        }

        private void ClearFilters_Click(object sender, RoutedEventArgs e)
        {
            if (FilterTimeComboBox == null || FilterStatusComboBox == null) return;

            SearchEventNameTextBox.Text = "";
            FilterSortNameComboBox.SelectedIndex = 0;

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

            bool isEventLive = false;
            if (_currentSelectedEvent != null)
            {
                isEventLive = _currentSelectedEvent.DisplayText.Contains("🟢 LIVE");
            }

            var filtered = _eventMasterLogs.AsEnumerable();

            string searchQuery = SearchEventNameTextBox.Text?.Trim().ToLower() ?? "";
            if (!string.IsNullOrEmpty(searchQuery))
            {
                filtered = filtered.Where(l =>
                    (l.FullName != null && l.FullName.ToLower().Contains(searchQuery)) ||
                    (l.StudentId != null && l.StudentId.ToLower().Contains(searchQuery)) ||
                    (l.Course != null && l.Course.ToLower().Contains(searchQuery)) ||
                    (l.Section != null && l.Section.ToLower().Contains(searchQuery)) ||
                    (l.Timestamp != null && l.Timestamp.ToLower().Contains(searchQuery)));
            }

            string course = FilterCourseComboBox.SelectedItem?.ToString() ?? "All Courses";
            string section = FilterSectionComboBox.SelectedItem?.ToString() ?? "All Sections";
            string status = (FilterStatusComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All Attendees";
            string time = (FilterTimeComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All Times";

            if (course != "All Courses")
                filtered = filtered.Where(l => l.Course == course);

            if (section != "All Sections")
                filtered = filtered.Where(l => l.Section == section);

            if (status == "Completed Event" || status == "Ongoing")
                filtered = filtered.Where(l => _completedStudentIds.Contains(l.StudentId));
            else if (status == "Incomplete / Left Early")
                filtered = filtered.Where(l => _incompleteStudentIds.Contains(l.StudentId));

            if (time == "Morning (AM)")
                filtered = filtered.Where(l => l.Timestamp.Contains("AM"));
            else if (time == "Afternoon (PM)")
                filtered = filtered.Where(l => l.Timestamp.Contains("PM"));

            var viewModels = filtered.Select(l => {
                bool isCompleted = _completedStudentIds.Contains(l.StudentId);

                string statusText = isCompleted ? (isEventLive ? "Ongoing" : "Completed") : "Incomplete";
                SolidColorBrush statusColor = isCompleted
                    ? (isEventLive
                        ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 96, 165, 250)) // Blue for Ongoing
                        : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 52, 211, 153))) // Green for Completed
                    : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 248, 113, 113)); // Red for Incomplete

                return new EventAttendanceViewModel
                {
                    Timestamp = l.Timestamp,
                    FullName = l.FullName ?? "Unknown",
                    StudentId = l.StudentId ?? "",
                    Course = l.Course ?? "",
                    Section = l.Section ?? "",
                    Action = l.Status ?? "", // Maps the "PRESENT/DEPARTED" state
                    CompletionStatus = statusText,
                    CompletionColor = statusColor
                };
            }).ToList();

            string sortOrder = (FilterSortNameComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Default (Time)";

            if (sortOrder == "Name (A-Z)")
                viewModels = viewModels.OrderBy(v => v.FullName).ToList();
            else if (sortOrder == "Name (Z-A)")
                viewModels = viewModels.OrderByDescending(v => v.FullName).ToList();

            var finalData = viewModels;
            AttendanceListView.ItemsSource = finalData;

            // Automatically sync the popup if it is open
            if (EventPopupListView != null)
                EventPopupListView.ItemsSource = finalData;
        }

        // --- MASTER EXPLORER POPUPS ---

        private async void OpenUnivExplorer_Click(object sender, RoutedEventArgs e)
        {
            UnivExplorerDialog.XamlRoot = this.Content.XamlRoot;
            UnivDialogContainer.Width = 1000;
            UnivPopupExpandToggle.IsChecked = false;
            UnivPopupExpandToggle.Content = "⛶ Expand View";

            // Populate the dropdown filters dynamically based on the current data pool
            var courses = _univMasterLogs.Select(l => l.Course).Where(c => !string.IsNullOrWhiteSpace(c)).Distinct().OrderBy(c => c).ToList();
            courses.Insert(0, "All Courses");
            UnivPopupCourseFilter.ItemsSource = courses;

            var sections = _univMasterLogs.Select(l => l.Section).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().OrderBy(s => s).ToList();
            sections.Insert(0, "All Sections");
            UnivPopupSectionFilter.ItemsSource = sections;

            UnivPopupClear_Click(null, null);

            await UnivExplorerDialog.ShowAsync();
        }

        private void UnivPopupExpandToggle_Click(object sender, RoutedEventArgs e)
        {
            if (UnivPopupExpandToggle.IsChecked == true)
            {
                UnivDialogContainer.Width = 1400;
                UnivPopupExpandToggle.Content = "⮌ Collapse View";
            }
            else
            {
                UnivDialogContainer.Width = 1000;
                UnivPopupExpandToggle.Content = "⛶ Expand View";
            }
        }

        // --- NEW: DEDICATED UNIVERSITY POPUP FILTERING LOGIC ---

        private void UnivPopupFilter_Changed(object sender, RoutedEventArgs e) => ApplyUnivPopupFilters();

        private void UnivPopupDatePicker_DateChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args) => ApplyUnivPopupFilters();

        private void UnivPopupClear_Click(object sender, RoutedEventArgs e)
        {
            UnivPopupSearchBox.Text = "";
            UnivPopupDatePicker.Date = null;
            UnivPopupSortBox.SelectedIndex = 0;

            if (UnivPopupCourseFilter.Items.Count > 0) UnivPopupCourseFilter.SelectedIndex = 0;
            if (UnivPopupSectionFilter.Items.Count > 0) UnivPopupSectionFilter.SelectedIndex = 0;
            UnivPopupStatusFilter.SelectedIndex = 0;

            ApplyUnivPopupFilters();
        }

        private void ApplyUnivPopupFilters()
        {
            if (_univMasterLogs == null || UnivPopupListView == null) return;

            var filtered = _univMasterLogs.AsEnumerable();

            string query = UnivPopupSearchBox.Text?.Trim().ToLower() ?? "";
            if (!string.IsNullOrEmpty(query))
            {
                filtered = filtered.Where(l =>
                    (l.FullName != null && l.FullName.ToLower().Contains(query)) ||
                    (l.StudentId != null && l.StudentId.ToLower().Contains(query)) ||
                    (l.Course != null && l.Course.ToLower().Contains(query)) ||
                    (l.Section != null && l.Section.ToLower().Contains(query)) ||
                    (l.Timestamp != null && l.Timestamp.ToLower().Contains(query)));
            }

            if (UnivPopupDatePicker.Date.HasValue)
            {
                string targetDateStr = UnivPopupDatePicker.Date.Value.ToString("MMM dd"); // Log format is "MMM dd - hh:mm tt"
                filtered = filtered.Where(l => l.Timestamp != null && l.Timestamp.StartsWith(targetDateStr));
            }

            string course = UnivPopupCourseFilter.SelectedItem?.ToString() ?? "All Courses";
            string section = UnivPopupSectionFilter.SelectedItem?.ToString() ?? "All Sections";
            string status = (UnivPopupStatusFilter.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All Statuses";

            if (course != "All Courses") filtered = filtered.Where(l => l.Course == course);
            if (section != "All Sections") filtered = filtered.Where(l => l.Section == section);
            if (status != "All Statuses") filtered = filtered.Where(l => l.Status == status);

            string sortOrder = (UnivPopupSortBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Newest First";
            if (sortOrder == "Oldest First")
            {
                filtered = filtered.Reverse(); // Original db pull is strictly DESC, so reverse gives exact ASC order
            }
            else if (sortOrder == "Name (A-Z)")
            {
                filtered = filtered.OrderBy(l => l.FullName);
            }
            else if (sortOrder == "Name (Z-A)")
            {
                filtered = filtered.OrderByDescending(l => l.FullName);
            }

            UnivPopupListView.ItemsSource = filtered.ToList();
        }

        // --- EVENT POPUP EXPLORER ---

        private async void OpenEventExplorer_Click(object sender, RoutedEventArgs e)
        {
            EventExplorerDialog.XamlRoot = this.Content.XamlRoot;
            EventDialogContainer.Width = 1000;
            EventPopupExpandToggle.IsChecked = false;
            EventPopupExpandToggle.Content = "⛶ Expand View";

            EventPopupListView.ItemsSource = AttendanceListView.ItemsSource;

            await EventExplorerDialog.ShowAsync();
        }

        private void EventPopupExpandToggle_Click(object sender, RoutedEventArgs e)
        {
            if (EventPopupExpandToggle.IsChecked == true)
            {
                EventDialogContainer.Width = 1400;
                EventPopupExpandToggle.Content = "⮌ Collapse View";
            }
            else
            {
                EventDialogContainer.Width = 1000;
                EventPopupExpandToggle.Content = "⛶ Expand View";
            }
        }

        // --- EXPORT LOGIC ---

        private async void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            var rawLogs = AttendanceListView.ItemsSource as IEnumerable<EventAttendanceViewModel>;

            if (rawLogs == null || !rawLogs.Any())
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

            // THE FIX (ITEM 3): Deduplicate by StudentId so each student appears exactly once
            var logsToExport = rawLogs
                .GroupBy(l => l.StudentId)
                .Select(g => g.First())
                .ToList();

            var picker = new Windows.Storage.Pickers.FileSavePicker();

            IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
            picker.FileTypeChoices.Add("Excel CSV Document", new List<string>() { ".csv" });

            string stateTag = "_Report";
            string eventPrefix = "Attendance";

            if (_currentSelectedEvent != null)
            {
                eventPrefix = _currentSelectedEvent.Event.EventId;
                stateTag = _currentSelectedEvent.DisplayText.Contains("🟢 LIVE") ? "_LIVE" : "_CLOSED";
            }

            picker.SuggestedFileName = $"{eventPrefix}_Attendance{stateTag}_{DateTime.Now:yyyyMMdd}";

            Windows.Storage.StorageFile file = await picker.PickSaveFileAsync();

            if (file != null)
            {
                var csvData = new System.Text.StringBuilder();

                csvData.AppendLine("Timestamp,Student ID,Student Name,Course,Section,Latest Action,Event Completion Status");

                foreach (var log in logsToExport)
                {
                    csvData.AppendLine($"\"{log.Timestamp}\",\"{log.StudentId}\",\"{log.FullName}\",\"{log.Course}\",\"{log.Section}\",\"{log.Action}\",\"{log.CompletionStatus}\"");
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

        private async void UnivExportButton_Click(object sender, RoutedEventArgs e)
        {
            var logsToExport = UniversityAuditListView.ItemsSource as IEnumerable<VerificationLogRecord>;

            if (logsToExport == null || !logsToExport.Any())
            {
                ContentDialog emptyDialog = new ContentDialog
                {
                    Title = "Nothing to Export",
                    Content = "There are no campus traffic records to export based on your current filters.",
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

            picker.SuggestedFileName = $"Campus_Traffic_Report_{DateTime.Now:yyyyMMdd}";

            Windows.Storage.StorageFile file = await picker.PickSaveFileAsync();

            if (file != null)
            {
                var csvData = new System.Text.StringBuilder();

                // Build header
                csvData.AppendLine("Timestamp,Student ID,Student Name,Course,Section,Action,Status");

                // Append rows
                foreach (var log in logsToExport)
                {
                    csvData.AppendLine($"\"{log.Timestamp}\",\"{log.StudentId}\",\"{log.FullName}\",\"{log.Course}\",\"{log.Section}\",\"{log.Action}\",\"{log.Status}\"");
                }

                Windows.Storage.CachedFileManager.DeferUpdates(file);
                await Windows.Storage.FileIO.WriteTextAsync(file, csvData.ToString(), Windows.Storage.Streams.UnicodeEncoding.Utf8);
                Windows.Storage.Provider.FileUpdateStatus status = await Windows.Storage.CachedFileManager.CompleteUpdatesAsync(file);

                if (status == Windows.Storage.Provider.FileUpdateStatus.Complete)
                {
                    ContentDialog successDialog = new ContentDialog
                    {
                        Title = "Export Complete",
                        Content = $"Your campus traffic report was successfully exported and saved to:\n\n{file.Path}",
                        CloseButtonText = "OK",
                        XamlRoot = this.Content.XamlRoot
                    };
                    await successDialog.ShowAsync();
                }
            }
        }

        // --- WINDOW MANAGEMENT ---

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