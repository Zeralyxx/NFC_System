using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace NFC_System;

public sealed partial class EventReportsWindow
{
    private bool _refreshingReports;
    private bool _reportsClosed;
    private bool _updatingCampusFilters;
    private int _campusLoadVersion;

    private async void RefreshReports_Click(object sender, RoutedEventArgs e) => await InitializeAsync();

    private void ClearEventReport()
    {
        ExportButton.IsEnabled = false;
        _eventMasterLogs.Clear();
        _eventSummary = ReportingAnalytics.SummarizeEvent(Array.Empty<AttendanceLog>());
        AttendanceListView.ItemsSource = EventPopupListView.ItemsSource = null;
        AttendedCountText.Text = TurnoutRateText.Text = RetentionRateText.Text = ExpectedCountText.Text = "--";
        RetentionSubText.Text = "No report loaded";
        EventReportNote.Text = "No event report loaded";
        CourseBreakdownItemsControl.ItemsSource = SectionBreakdownItemsControl.ItemsSource = AuthModesItemsControl.ItemsSource = null;
    }

    private void ClearCampusReport()
    {
        _univMasterLogs.Clear();
        DailyTrafficListView.ItemsSource = UnivPopupListView.ItemsSource = HistoricalThreatsListView.ItemsSource = null;
        DailyEntriesItemsControl.ItemsSource = null;
        UnivTotalScansText.Text = UnivInsideText.Text = UnivDeniedText.Text = "--";
        HighAlertDateText.Text = HighAlertCountText.Text = LowAlertDateText.Text = LowAlertCountText.Text = "--";
    }

    private async Task<bool> LoadCampusLedgerAsync(DateTime? day)
    {
        int version = ++_campusLoadVersion;
        _univMasterLogs.Clear();
        UnivPopupListView.ItemsSource = null;
        CampusLedgerNote.Text = "Loading campus ledger...";
        UnivExplorerDialog.Title = "Loading campus ledger...";
        try
        {
            if (!DatabaseMonitor.IsOnline) throw new InvalidOperationException("Database is offline.");
            var logs = await _database.GetGeneralLedgerAsync(day);
            if (version != _campusLoadVersion || _reportsClosed) return false;
            _univMasterLogs = logs.ToList();
            _updatingCampusFilters = true;
            try
            {
                string course = UnivPopupCourseFilter.SelectedItem?.ToString() ?? "All Courses";
                string section = UnivPopupSectionFilter.SelectedItem?.ToString() ?? "All Sections";
                var courses = new[] { "All Courses" }.Concat(logs.Select(l => l.Course).Where(c => !string.IsNullOrWhiteSpace(c)).Distinct().OrderBy(c => c)).ToList();
                var sections = new[] { "All Sections" }.Concat(logs.Select(l => l.Section).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().OrderBy(s => s)).ToList();
                UnivPopupCourseFilter.ItemsSource = courses;
                UnivPopupSectionFilter.ItemsSource = sections;
                UnivPopupCourseFilter.SelectedItem = courses.Contains(course) ? course : "All Courses";
                UnivPopupSectionFilter.SelectedItem = sections.Contains(section) ? section : "All Sections";
            }
            finally { _updatingCampusFilters = false; }
            int limit = day.HasValue ? 10000 : 500;
            string scope = day.HasValue ? day.Value.ToString("yyyy-MM-dd") : "all dates";
            CampusLedgerNote.Text = $"Daily totals cover all retained gate logs. Explorer/export: {logs.Count:N0} rows for {scope}" +
                (logs.Count == limit ? $" (limit {limit:N0} reached; may be incomplete)." : ".") +
                " Select a date in the explorer to load its records.";
            UnivExplorerDialog.Title = $"Campus ledger: {scope} | {logs.Count:N0} rows | limit {limit:N0}";
            ApplyUnivPopupFilters();
            return true;
        }
        catch (Exception ex)
        {
            if (version == _campusLoadVersion && !_reportsClosed)
            {
                _univMasterLogs.Clear();
                UnivPopupListView.ItemsSource = null;
                CampusLedgerNote.Text = $"Campus ledger unavailable: {ex.Message}";
                UnivExplorerDialog.Title = "Campus ledger unavailable";
            }
            return false;
        }
    }
}
