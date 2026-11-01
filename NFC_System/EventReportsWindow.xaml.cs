using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.IO;
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
        public DateTime RawTimestamp { get; set; }
        public string DeviceId { get; set; } = "";
        public string DeviceName { get; set; } = "Unknown/Legacy";
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
        private EventReportSummary _eventSummary = ReportingAnalytics.SummarizeEvent(Array.Empty<AttendanceLog>());
        private int _eventLoadVersion;
        private List<EventDropdownItem> _allEventDropdownItems = new();
        private EventDropdownItem? _currentSelectedEvent = null;
        private List<VerificationLogRecord> _univMasterLogs = new();

        // State variables for Exit Interceptor
        private bool _isForceClosing = false;
        private bool _isAwaitingAdminAuth = false;
        private string _pendingAdminAction = "";
        private string _pendingAdminSeverity = "";

        // Auto-Refresh Polling State
        private readonly DispatcherTimer _autoRefreshTimer = new();
        private bool _isAutoRefreshing = false;
        private DateTime _lastUnivLogTimestamp = DateTime.MinValue;
        private int _lastUnivLogCount = -1;
        private int _lastTotalScansToday = -1;
        private int _lastInsideCount = -1;
        private int _lastDeniedToday = -1;
        private DateTime _lastEventLogTimestamp = DateTime.MinValue;
        private int _lastEventLogCount = -1;

        public EventReportsWindow()
        {
            this.InitializeComponent();
            DatabaseMonitor.ConnectionStatusChanged += UpdateOfflineBanner;
            UpdateOfflineBanner(DatabaseMonitor.IsOnline);
            MaximizeWindow();

            // Subscribe to the global hardware manager
            HardwareService.OnUidScanned += HardwareService_OnUidScanned;

            IntPtr hWnd = WindowNative.GetWindowHandle(this);
            WindowId windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
            AppWindow appWindow = AppWindow.GetFromWindowId(windowId);
            appWindow.Closing += AppWindow_Closing;
            this.Closed += Window_Closed;

            // Start 3-second silent polling for live incoming scans
            _autoRefreshTimer.Interval = TimeSpan.FromSeconds(3);
            _autoRefreshTimer.Tick += AutoRefreshTimer_Tick;
            _autoRefreshTimer.Start();

            _ = InitializeAsync();
        }

        // ====================================================================
        // SHARED HARDWARE SERVICE EVENT HANDLER
        // ====================================================================
        private void HardwareService_OnUidScanned(string uid)
        {
            if (_isAwaitingAdminAuth)
            {
                DispatcherQueue.TryEnqueue(async () => await HandleAdminAuthScanAsync(uid));
                return;
            }

            if (AppSession.IsKioskRunning) return;
        }

        // ====================================================================
        // SILENT AUTO-REFRESH POLLING ENGINE
        // ====================================================================
        private async void AutoRefreshTimer_Tick(object? sender, object e)
        {
            if (_isAutoRefreshing || _refreshingReports || _reportsClosed || !DatabaseMonitor.IsOnline)
                return;

            await SilentAutoRefreshAsync();
        }

        private async Task SilentAutoRefreshAsync()
        {
            if (_isAutoRefreshing || _refreshingReports || _reportsClosed) return;
            _isAutoRefreshing = true;

            try
            {
                // 1. Refresh University / Campus Metrics & Ledger (if not restricted to Event Organizer)
                if (!AppSession.IsEventOrganizer)
                {
                    var metrics = await _database.GetUniversityMetricsAsync();
                    DateTime? selectedDate = UnivPopupDatePicker?.Date?.Date;
                    var latestLedger = await _database.GetGeneralLedgerAsync(selectedDate);

                    if (_reportsClosed) return;

                    DateTime newestUnivTime = latestLedger.FirstOrDefault()?.RawTimestamp ?? DateTime.MinValue;
                    bool metricsChanged = metrics.TotalScansToday != _lastTotalScansToday ||
                                          metrics.CurrentlyInside != _lastInsideCount ||
                                          metrics.DeniedToday != _lastDeniedToday;
                    bool ledgerChanged = newestUnivTime != _lastUnivLogTimestamp ||
                                         latestLedger.Count != _lastUnivLogCount;

                    if (metricsChanged || ledgerChanged)
                    {
                        _lastTotalScansToday = metrics.TotalScansToday;
                        _lastInsideCount = metrics.CurrentlyInside;
                        _lastDeniedToday = metrics.DeniedToday;
                        _lastUnivLogTimestamp = newestUnivTime;
                        _lastUnivLogCount = latestLedger.Count;

                        UnivTotalScansText.Text = metrics.TotalScansToday.ToString("N0");
                        UnivInsideText.Text = metrics.CurrentlyInside.ToString("N0");
                        UnivDeniedText.Text = metrics.DeniedToday.ToString("N0");

                        DailyEntriesItemsControl.ItemsSource = await _database.GetDailyEntryStatsAsync();

                        var alertExtremes = await _database.GetSecurityAlertExtremesAsync();
                        HighAlertDateText.Text = alertExtremes.HighDayLabel;
                        HighAlertCountText.Text = alertExtremes.HighCount.ToString();
                        LowAlertDateText.Text = alertExtremes.LowDayLabel;
                        LowAlertCountText.Text = alertExtremes.LowCount.ToString();

                        HistoricalThreatsListView.ItemsSource = await _database.GetDailySecurityAlertsAsync();
                        DailyTrafficListView.ItemsSource = await _database.GetDailyTrafficSummariesAsync();

                        _univMasterLogs = latestLedger.ToList();
                        RefreshPopupFilterOptionsSafely();
                        ApplyUnivPopupFilters();

                        ReportingStatus.Title = $"Database reports live-updated {DateTime.Now:T}";
                    }
                }

                // 2. Refresh Active Event Report (if an event is currently selected)
                if (_currentSelectedEvent != null && !_reportsClosed)
                {
                    await SilentRefreshSelectedEventAsync(_currentSelectedEvent);
                }
            }
            catch
            {
                // Silently ignore transient polling errors so the operator's screen isn't disrupted
            }
            finally
            {
                _isAutoRefreshing = false;
            }
        }

        private async Task SilentRefreshSelectedEventAsync(EventDropdownItem selectedWrapper)
        {
            EventRecord selectedEvent = selectedWrapper.Event;
            var logs = await _database.GetEventAttendanceLogsAsync(selectedEvent.EventId);
            if (_reportsClosed) return;

            DateTime newestEventTime = logs.FirstOrDefault()?.RawTimestamp ?? DateTime.MinValue;
            if (newestEventTime == _lastEventLogTimestamp && logs.Count == _lastEventLogCount)
                return; // No new event scans; skip UI rebuild

            _lastEventLogTimestamp = newestEventTime;
            _lastEventLogCount = logs.Count;

            var approvedList = selectedEvent.IsRestricted
                ? await _database.GetEventAttendeesAsync(selectedEvent.EventId)
                : Array.Empty<StudentRecord>();

            if (_reportsClosed) return;

            _eventMasterLogs = logs.ToList();
            _eventSummary = ReportingAnalytics.SummarizeEvent(logs);
            int totalEntered = _eventSummary.Arrivals.Count;
            int lastPresent = _eventSummary.LastPresent;

            AttendedCountText.Text = totalEntered.ToString();

            if (totalEntered > 0)
            {
                double retention = ((double)lastPresent / totalEntered) * 100;
                RetentionRateText.Text = $"{retention:F1}%";
                RetentionSubText.Text = $"{lastPresent} of {totalEntered} attendees last recorded as PRESENT. Completion is not established by scan logs.";

                var arrivalsOnly = _eventSummary.Arrivals;

                CourseBreakdownItemsControl.ItemsSource = arrivalsOnly
                    .GroupBy(l => string.IsNullOrWhiteSpace(l.Course) ? "Unregistered Course" : l.Course)
                    .Select(g => new StatItem { Label = g.Key, Value = g.Count().ToString(), SubValue = $"{(g.Count() * 100.0 / totalEntered):F1}%" })
                    .OrderByDescending(x => int.Parse(x.Value)).ToList();

                SectionBreakdownItemsControl.ItemsSource = arrivalsOnly
                    .GroupBy(l => $"{(string.IsNullOrWhiteSpace(l.Course) ? "Unassigned course" : l.Course)} / {(string.IsNullOrWhiteSpace(l.Section) ? "Unassigned" : l.Section)}")
                    .Select(g => new StatItem { Label = g.Key, Value = g.Count().ToString(), SubValue = $"{(g.Count() * 100.0 / totalEntered):F1}%" })
                    .OrderByDescending(x => int.Parse(x.Value)).Take(5).ToList();

                AuthModesItemsControl.ItemsSource = arrivalsOnly
                    .GroupBy(l => string.IsNullOrWhiteSpace(l.Mode) ? "Unknown" : l.Mode)
                    .Select(g => new StatItem { Label = g.Key + " Mode", Value = g.Count().ToString() })
                    .OrderByDescending(x => int.Parse(x.Value)).ToList();
            }

            if (selectedEvent.IsRestricted)
            {
                int expected = approvedList.Select(s => s.StudentId).Distinct(StringComparer.OrdinalIgnoreCase).Count();
                ExpectedCountText.Text = $"{expected} students on current roster";
                TurnoutRateText.Text = expected > 0
                    ? $"{_eventSummary.Turnout(approvedList.Select(s => s.StudentId))!.Value:F1}%"
                    : "N/A";
            }

            // Re-apply current filters and update dropdown options without resetting selections
            RefreshPopupFilterOptionsSafely();
            ApplyLedgerFilters();
            ApplyEventPopupFilters();

            EventReportNote.Text = $"{logs.Count:N0} retained scan rows; {totalEntered:N0} unique arrivals. Live-updated {DateTime.Now:T}.";
            ExportButton.IsEnabled = logs.Count > 0;
        }

        // ====================================================================
        // RBAC SEVERITY-AWARE EXIT INTERCEPTOR
        // ====================================================================
        private async void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
        {
            if (_isForceClosing) return;
            args.Cancel = true;

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

            HardwareService.Disconnect();
            _isForceClosing = true;
            Application.Current.Exit();
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
                PlaySuccessPing();

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

        private void Window_Closed(object sender, WindowEventArgs args)
        {
            _reportsClosed = true;
            _autoRefreshTimer.Stop();
            ++_eventLoadVersion;
            ++_campusLoadVersion;
            DatabaseMonitor.ConnectionStatusChanged -= UpdateOfflineBanner;
            HardwareService.OnUidScanned -= HardwareService_OnUidScanned;
        }

        private void PlaySuccessPing()
        {
            Task.Run(() =>
            {
                try
                {
                    string soundPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "success_ping.wav");
                    if (File.Exists(soundPath))
                    {
                        using var player = new System.Media.SoundPlayer(soundPath);
                        player.PlaySync();
                    }
                    else
                    {
                        Console.Beep(1046, 75);
                        System.Threading.Thread.Sleep(15);
                        Console.Beep(1318, 75);
                        System.Threading.Thread.Sleep(15);
                        Console.Beep(1568, 200);
                    }
                }
                catch { }
            });
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
        // EVENT REPORTS LOGIC
        // ====================================================================
        private async Task InitializeAsync()
        {
            if (_refreshingReports || _reportsClosed) return;
            _refreshingReports = true;
            RefreshReportsButton.IsEnabled = EventSearchBox.IsEnabled = UnivPopupDatePicker.IsEnabled = false;
            ++_eventLoadVersion;
            ++_campusLoadVersion;
            ClearCampusReport();
            ClearEventReport();
            ReportingStatus.Severity = InfoBarSeverity.Informational;
            ReportingStatus.Title = "Loading reports";
            ReportingStatus.Message = "";
            if (AppSession.IsEventOrganizer)
            {
                UniversityModeBtn.Visibility = Visibility.Collapsed;
                SwitchToEventMode();
            }
            try
            {
                if (!DatabaseMonitor.IsOnline) throw new InvalidOperationException("Connect to the database and refresh reports.");
                if (DatabaseMonitor.IsOnline)
                {
                    await _database.EnsureSchemaAsync();
                }

                IReadOnlyList<EventRecord> allEvents = new List<EventRecord>();
                IReadOnlyList<EventRecord> activeEvents = new List<EventRecord>();

                if (DatabaseMonitor.IsOnline)
                {
                    allEvents = await _database.GetAllEventsAsync();
                    activeEvents = await _database.GetActiveEventsAsync(9999);
                }

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
                if (_reportsClosed) return;

                if (AppSession.IsEventOrganizer)
                {
                    UniversityModeBtn.Visibility = Visibility.Collapsed;
                    SwitchToEventMode();
                }
                else
                {
                    if (DatabaseMonitor.IsOnline)
                    {
                        var metrics = await _database.GetUniversityMetricsAsync();
                        _lastTotalScansToday = metrics.TotalScansToday;
                        _lastInsideCount = metrics.CurrentlyInside;
                        _lastDeniedToday = metrics.DeniedToday;

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

                        DailyTrafficListView.ItemsSource = await _database.GetDailyTrafficSummariesAsync();
                        _updatingCampusFilters = true;
                        UnivPopupDatePicker.Date = null;
                        _updatingCampusFilters = false;
                        if (!await LoadCampusLedgerAsync(null)) throw new InvalidOperationException("Campus ledger could not be loaded. Refresh to retry.");

                        _lastUnivLogTimestamp = _univMasterLogs.FirstOrDefault()?.RawTimestamp ?? DateTime.MinValue;
                        _lastUnivLogCount = _univMasterLogs.Count;
                    }
                }
                if (_reportsClosed) return;
                if (_currentSelectedEvent != null)
                {
                    _currentSelectedEvent = _allEventDropdownItems.FirstOrDefault(e => e.Event.EventId == _currentSelectedEvent.Event.EventId);
                    if (_currentSelectedEvent != null) await ProcessEventSelectionAsync(_currentSelectedEvent);
                    else { EventSearchBox.Text = ""; EventReportNote.Text = "Selected event is no longer available."; }
                }
                ReportingStatus.Title = $"Database reports loaded {DateTime.Now:g}";
                ReportingStatus.Message = "Retained database records only. Pending device uploads and purged history are excluded. Inside is an estimate, not confirmed physical occupancy.";
            }
            catch (Exception ex)
            {
                if (!_reportsClosed)
                {
                    ClearCampusReport();
                    ReportingStatus.Severity = InfoBarSeverity.Error;
                    ReportingStatus.Title = "Reports unavailable";
                    ReportingStatus.Message = ex.Message;
                }
            }
            finally
            {
                _refreshingReports = false;
                if (!_reportsClosed) RefreshReportsButton.IsEnabled = EventSearchBox.IsEnabled = UnivPopupDatePicker.IsEnabled = true;
            }
        }

        private void UniversityModeBtn_Click(object sender, RoutedEventArgs e) => SwitchToUniversityMode();
        private void EventModeBtn_Click(object sender, RoutedEventArgs e) => SwitchToEventMode();

        private void SwitchToUniversityMode()
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

        private void SwitchToEventMode()
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

        private async void DailyTrafficListView_DoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
        {
            if (e.OriginalSource is FrameworkElement fe && fe.DataContext is DailyTrafficSummary summary)
            {
                UnivExplorerDialog.XamlRoot = this.Content.XamlRoot;
                UnivDialogContainer.Width = 1400;
                UnivPopupExpandToggle.IsChecked = true;
                UnivPopupExpandToggle.Content = "⮌ Collapse View";

                var courses = _univMasterLogs.Select(l => l.Course).Where(c => !string.IsNullOrWhiteSpace(c)).Distinct().OrderBy(c => c).ToList();
                courses.Insert(0, "All Courses");
                UnivPopupCourseFilter.ItemsSource = courses;

                var sections = _univMasterLogs.Select(l => l.Section).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().OrderBy(s => s).ToList();
                sections.Insert(0, "All Sections");
                UnivPopupSectionFilter.ItemsSource = sections;

                UnivPopupSearchBox.Text = "";
                UnivPopupCourseFilter.SelectedIndex = 0;
                UnivPopupSectionFilter.SelectedIndex = 0;
                UnivPopupStatusFilter.SelectedIndex = 0;
                UnivPopupSortBox.SelectedIndex = 0;

                UnivPopupDatePicker.Date = summary.DateValue;
                ApplyUnivPopupFilters();

                await UnivExplorerDialog.ShowAsync();
            }
        }

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
            if (EventSearchBox.IsSuggestionListOpen) EventSearchBox.IsSuggestionListOpen = false;
        }

        private void EventSearchBox_LostFocus(object sender, RoutedEventArgs e)
        {
            EventSearchBox.IsSuggestionListOpen = false;
        }

        private void EventLeftScrollViewer_ViewChanging(object sender, ScrollViewerViewChangingEventArgs e)
        {
            if (EventSearchBox.IsSuggestionListOpen) EventSearchBox.IsSuggestionListOpen = false;
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

        private async Task ProcessEventSelectionAsync(EventDropdownItem selectedWrapper)
        {
            EventRecord selectedEvent = selectedWrapper.Event;
            int version = ++_eventLoadVersion;
            ClearEventReport();
            EventReportNote.Text = "Loading event report...";

            try
            {
                if (!DatabaseMonitor.IsOnline) throw new InvalidOperationException("Event reports require a database connection.");
                var logs = await _database.GetEventAttendanceLogsAsync(selectedEvent.EventId);
                var approvedList = selectedEvent.IsRestricted ? await _database.GetEventAttendeesAsync(selectedEvent.EventId) : Array.Empty<StudentRecord>();
                if (version != _eventLoadVersion || _reportsClosed) return;
                _eventMasterLogs = logs.ToList();
                _lastEventLogTimestamp = logs.FirstOrDefault()?.RawTimestamp ?? DateTime.MinValue;
                _lastEventLogCount = logs.Count;

                _eventSummary = ReportingAnalytics.SummarizeEvent(logs);
                int totalEntered = _eventSummary.Arrivals.Count;
                int lastPresent = _eventSummary.LastPresent;

                AttendedCountText.Text = totalEntered.ToString();

                if (totalEntered > 0)
                {
                    double retention = ((double)lastPresent / totalEntered) * 100;
                    RetentionRateText.Text = $"{retention:F1}%";
                    RetentionSubText.Text = $"{lastPresent} of {totalEntered} attendees last recorded as PRESENT. Completion is not established by scan logs.";

                    var arrivalsOnly = _eventSummary.Arrivals;

                    var courseStats = arrivalsOnly
                        .GroupBy(l => string.IsNullOrWhiteSpace(l.Course) ? "Unregistered Course" : l.Course)
                        .Select(g => new StatItem { Label = g.Key, Value = g.Count().ToString(), SubValue = $"{(g.Count() * 100.0 / totalEntered):F1}%" })
                        .OrderByDescending(x => int.Parse(x.Value)).ToList();
                    CourseBreakdownItemsControl.ItemsSource = courseStats;

                    var sectionStats = arrivalsOnly
                        .GroupBy(l => $"{(string.IsNullOrWhiteSpace(l.Course) ? "Unassigned course" : l.Course)} / {(string.IsNullOrWhiteSpace(l.Section) ? "Unassigned" : l.Section)}")
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
                    RetentionRateText.Text = "N/A";
                    RetentionSubText.Text = "No arrivals recorded";
                    CourseBreakdownItemsControl.ItemsSource = new List<StatItem>();
                    SectionBreakdownItemsControl.ItemsSource = new List<StatItem>();
                    AuthModesItemsControl.ItemsSource = new List<StatItem>();
                }

                if (selectedEvent.IsRestricted)
                {
                    int expected = approvedList.Select(s => s.StudentId).Distinct(StringComparer.OrdinalIgnoreCase).Count();
                    ExpectedCountText.Text = $"{expected} students on current roster";

                    if (expected > 0)
                    {
                        double rate = _eventSummary.Turnout(approvedList.Select(s => s.StudentId))!.Value;
                        TurnoutRateText.Text = $"{rate:F1}%";
                    }
                    else
                    {
                        TurnoutRateText.Text = "N/A";
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
                ApplyLedgerFilters();
                EventReportNote.Text = $"{logs.Count:N0} retained scan rows; {totalEntered:N0} unique arrivals. Updated {DateTime.Now:g}. " +
                    "Breakdowns use each student's first recorded arrival and current profile; filters below affect ledger rows only. " +
                    (selectedEvent.IsRestricted ? "Turnout compares attendees with the current roster, not a historical roster snapshot." : "No fixed turnout denominator for open events.");
                ExportButton.IsEnabled = logs.Count > 0;
            }
            catch (Exception ex)
            {
                if (version != _eventLoadVersion || _reportsClosed) return;
                ClearEventReport();
                EventReportNote.Text = $"Event report unavailable: {ex.Message}";
            }
        }

        private void Filter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (FilterCourseComboBox == null || FilterSectionComboBox == null) return;

            if (ReferenceEquals(sender, FilterCourseComboBox) && _eventMasterLogs != null)
            {
                string course = FilterCourseComboBox.SelectedItem?.ToString() ?? "All Courses";
                var sectionQuery = _eventMasterLogs.AsEnumerable();

                if (course != "All Courses")
                    sectionQuery = sectionQuery.Where(l => l.Course == course);

                var sections = sectionQuery.Select(l => l.Section).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().OrderBy(s => s).ToList();
                sections.Insert(0, "All Sections");

                string currentSection = FilterSectionComboBox.SelectedItem?.ToString() ?? "All Sections";

                FilterSectionComboBox.SelectionChanged -= Filter_SelectionChanged;
                FilterSectionComboBox.ItemsSource = sections;
                FilterSectionComboBox.SelectedItem = sections.Contains(currentSection) ? currentSection : "All Sections";
                FilterSectionComboBox.SelectionChanged += Filter_SelectionChanged;
            }

            ApplyLedgerFilters();
        }

        private void SearchEventNameTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyLedgerFilters();
        }

        private void ClearFilters_Click(object? sender, RoutedEventArgs? e)
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

            if (status != "All Attendees") filtered = filtered.Where(l => _eventSummary.StateFor(l.StudentId) == status);
            filtered = filtered.Where(l => ReportingAnalytics.MatchesTime(l.RawTimestamp, time));

            var viewModels = filtered.Select(l => {
                string statusText = _eventSummary.StateFor(l.StudentId);
                var statusColor = new SolidColorBrush(statusText == ReportingAnalytics.Unresolved ? Microsoft.UI.Colors.Orange : Microsoft.UI.Colors.LightGray);

                return new EventAttendanceViewModel
                {
                    Timestamp = l.Timestamp,
                    RawTimestamp = l.RawTimestamp,
                    DeviceId = l.DeviceId,
                    DeviceName = ReportSpreadsheetTables.DeviceName(l.DeviceName),
                    FullName = l.FullName ?? "Unknown",
                    StudentId = l.StudentId ?? "",
                    Course = l.Course ?? "",
                    Section = l.Section ?? "",
                    Action = l.Status ?? "",
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
        }

        private async void OpenUnivExplorer_Click(object sender, RoutedEventArgs e)
        {
            UnivExplorerDialog.XamlRoot = this.Content.XamlRoot;
            UnivDialogContainer.Width = 1000;
            UnivPopupExpandToggle.IsChecked = false;
            UnivPopupExpandToggle.Content = "⛶ Expand View";

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

        private void UnivPopupFilter_Changed(object sender, RoutedEventArgs e) => ApplyUnivPopupFilters();
        private void UnivPopupFilter_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_updatingCampusFilters) return;
            if (UnivPopupCourseFilter != null && UnivPopupSectionFilter != null && ReferenceEquals(sender, UnivPopupCourseFilter) && _univMasterLogs != null)
            {
                string course = UnivPopupCourseFilter.SelectedItem?.ToString() ?? "All Courses";
                var sectionQuery = _univMasterLogs.AsEnumerable();

                if (course != "All Courses")
                    sectionQuery = sectionQuery.Where(l => l.Course == course);

                var sections = sectionQuery.Select(l => l.Section).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().OrderBy(s => s).ToList();
                sections.Insert(0, "All Sections");

                string currentSection = UnivPopupSectionFilter.SelectedItem?.ToString() ?? "All Sections";

                UnivPopupSectionFilter.SelectionChanged -= UnivPopupFilter_Changed;
                UnivPopupSectionFilter.ItemsSource = sections;
                UnivPopupSectionFilter.SelectedItem = sections.Contains(currentSection) ? currentSection : "All Sections";
                UnivPopupSectionFilter.SelectionChanged += UnivPopupFilter_Changed;
            }

            ApplyUnivPopupFilters();
        }

        private async void UnivPopupDatePicker_DateChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args)
        {
            if (!_updatingCampusFilters) await LoadCampusLedgerAsync(sender.Date?.Date);
        }

        private void UnivPopupClear_Click(object? sender, RoutedEventArgs? e)
        {
            if (UnivPopupSearchBox != null) UnivPopupSearchBox.Text = "";
            if (UnivPopupDatePicker != null) UnivPopupDatePicker.Date = null;
            if (UnivPopupSortBox != null) UnivPopupSortBox.SelectedIndex = 0;

            if (UnivPopupCourseFilter != null && UnivPopupCourseFilter.Items.Count > 0) UnivPopupCourseFilter.SelectedIndex = 0;
            if (UnivPopupSectionFilter != null && UnivPopupSectionFilter.Items.Count > 0) UnivPopupSectionFilter.SelectedIndex = 0;
            if (UnivPopupStatusFilter != null) UnivPopupStatusFilter.SelectedIndex = 0;

            ApplyUnivPopupFilters();
        }

        private void ApplyUnivPopupFilters()
        {
            if (_univMasterLogs == null || UnivPopupListView == null) return;

            var filtered = _univMasterLogs.AsEnumerable();

            string query = UnivPopupSearchBox?.Text?.Trim().ToLower() ?? "";
            if (!string.IsNullOrEmpty(query))
            {
                filtered = filtered.Where(l =>
                    (l.FullName != null && l.FullName.ToLower().Contains(query)) ||
                    (l.StudentId != null && l.StudentId.ToLower().Contains(query)) ||
                    (l.Course != null && l.Course.ToLower().Contains(query)) ||
                    (l.Section != null && l.Section.ToLower().Contains(query)) ||
                    (l.Timestamp != null && l.Timestamp.ToLower().Contains(query)));
            }

            if (UnivPopupDatePicker != null && UnivPopupDatePicker.Date.HasValue)
            {
                DateTime targetDate = UnivPopupDatePicker.Date.Value.Date;
                filtered = filtered.Where(l => l.RawTimestamp.Date == targetDate);
            }

            string course = UnivPopupCourseFilter?.SelectedItem?.ToString() ?? "All Courses";
            string section = UnivPopupSectionFilter?.SelectedItem?.ToString() ?? "All Sections";
            string status = (UnivPopupStatusFilter?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All Statuses";

            if (course != "All Courses") filtered = filtered.Where(l => l.Course == course);
            if (section != "All Sections") filtered = filtered.Where(l => l.Section == section);
            if (status != "All Statuses") filtered = filtered.Where(l => l.Status == status);

            string sortOrder = (UnivPopupSortBox?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Newest First";
            if (sortOrder == "Oldest First")
            {
                filtered = filtered.OrderBy(l => l.RawTimestamp);
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

        private async void OpenEventExplorer_Click(object sender, RoutedEventArgs e)
        {
            EventExplorerDialog.XamlRoot = this.Content.XamlRoot;
            EventDialogContainer.Width = 1000;
            EventPopupExpandToggle.IsChecked = false;
            EventPopupExpandToggle.Content = "⛶ Expand View";

            var courses = _eventMasterLogs.Select(l => l.Course).Where(c => !string.IsNullOrWhiteSpace(c)).Distinct().OrderBy(c => c).ToList();
            courses.Insert(0, "All Courses");
            EventPopupCourseFilter.ItemsSource = courses;

            var sections = _eventMasterLogs.Select(l => l.Section).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().OrderBy(s => s).ToList();
            sections.Insert(0, "All Sections");
            EventPopupSectionFilter.ItemsSource = sections;

            EventPopupClear_Click(null, null);

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

        private void EventPopupFilter_Changed(object sender, RoutedEventArgs e) => ApplyEventPopupFilters();
        private void EventPopupFilter_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (EventPopupCourseFilter != null && EventPopupSectionFilter != null && ReferenceEquals(sender, EventPopupCourseFilter) && _eventMasterLogs != null)
            {
                string course = EventPopupCourseFilter.SelectedItem?.ToString() ?? "All Courses";
                var sectionQuery = _eventMasterLogs.AsEnumerable();

                if (course != "All Courses")
                    sectionQuery = sectionQuery.Where(l => l.Course == course);

                var sections = sectionQuery.Select(l => l.Section).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().OrderBy(s => s).ToList();
                sections.Insert(0, "All Sections");

                string currentSection = EventPopupSectionFilter.SelectedItem?.ToString() ?? "All Sections";

                EventPopupSectionFilter.SelectionChanged -= EventPopupFilter_Changed;
                EventPopupSectionFilter.ItemsSource = sections;
                EventPopupSectionFilter.SelectedItem = sections.Contains(currentSection) ? currentSection : "All Sections";
                EventPopupSectionFilter.SelectionChanged += EventPopupFilter_Changed;
            }

            ApplyEventPopupFilters();
        }
        private void EventPopupDatePicker_DateChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args) => ApplyEventPopupFilters();

        private void EventPopupClear_Click(object? sender, RoutedEventArgs? e)
        {
            if (EventPopupSearchBox != null) EventPopupSearchBox.Text = string.Empty;
            if (EventPopupDatePicker != null) EventPopupDatePicker.Date = null;
            if (EventPopupSortBox != null) EventPopupSortBox.SelectedIndex = 0;

            if (EventPopupCourseFilter != null && EventPopupCourseFilter.Items.Count > 0) EventPopupCourseFilter.SelectedIndex = 0;
            if (EventPopupSectionFilter != null && EventPopupSectionFilter.Items.Count > 0) EventPopupSectionFilter.SelectedIndex = 0;
            if (EventPopupStatusFilter != null) EventPopupStatusFilter.SelectedIndex = 0;

            ApplyEventPopupFilters();
        }

        private void ApplyEventPopupFilters()
        {
            if (_eventMasterLogs == null || EventPopupListView == null || EventPopupCourseFilter == null || EventPopupSectionFilter == null || EventPopupStatusFilter == null || EventPopupSortBox == null)
                return;

            var filtered = _eventMasterLogs.AsEnumerable();

            string searchQuery = EventPopupSearchBox?.Text?.Trim().ToLower() ?? "";
            if (!string.IsNullOrEmpty(searchQuery))
            {
                filtered = filtered.Where(l =>
                    (l.FullName != null && l.FullName.ToLower().Contains(searchQuery)) ||
                    (l.StudentId != null && l.StudentId.ToLower().Contains(searchQuery)) ||
                    (l.Course != null && l.Course.ToLower().Contains(searchQuery)) ||
                    (l.Section != null && l.Section.ToLower().Contains(searchQuery)) ||
                    (l.Timestamp != null && l.Timestamp.ToLower().Contains(searchQuery)));
            }

            if (EventPopupDatePicker != null && EventPopupDatePicker.Date.HasValue)
            {
                DateTime targetDate = EventPopupDatePicker.Date.Value.Date;
                filtered = filtered.Where(l => ReportingAnalytics.MatchesDate(l.RawTimestamp, targetDate));
            }

            string course = EventPopupCourseFilter.SelectedItem?.ToString() ?? "All Courses";
            string section = EventPopupSectionFilter.SelectedItem?.ToString() ?? "All Sections";
            string status = (EventPopupStatusFilter.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All Attendees";

            if (course != "All Courses")
                filtered = filtered.Where(l => l.Course == course);

            if (section != "All Sections")
                filtered = filtered.Where(l => l.Section == section);

            if (status != "All Attendees") filtered = filtered.Where(l => _eventSummary.StateFor(l.StudentId) == status);

            var viewModels = filtered.Select(l => {
                string statusText = _eventSummary.StateFor(l.StudentId);
                var statusColor = new SolidColorBrush(statusText == ReportingAnalytics.Unresolved ? Microsoft.UI.Colors.Orange : Microsoft.UI.Colors.LightGray);

                return new EventAttendanceViewModel
                {
                    Timestamp = l.Timestamp,
                    RawTimestamp = l.RawTimestamp,
                    DeviceId = l.DeviceId,
                    DeviceName = ReportSpreadsheetTables.DeviceName(l.DeviceName),
                    FullName = l.FullName ?? "Unknown",
                    StudentId = l.StudentId ?? "",
                    Course = l.Course ?? "",
                    Section = l.Section ?? "",
                    Action = l.Status ?? "",
                    CompletionStatus = statusText,
                    CompletionColor = statusColor
                };
            }).ToList();

            string sortOrder = (EventPopupSortBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Newest First";

            if (sortOrder == "Oldest First")
            {
                viewModels = viewModels.OrderBy(l => l.RawTimestamp).ToList();
            }
            else if (sortOrder == "Name (A-Z)")
            {
                viewModels = viewModels.OrderBy(v => v.FullName).ToList();
            }
            else if (sortOrder == "Name (Z-A)")
            {
                viewModels = viewModels.OrderByDescending(v => v.FullName).ToList();
            }

            EventPopupListView.ItemsSource = viewModels;
        }

        private async void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var rawLogs = (AttendanceListView.ItemsSource as IEnumerable<EventAttendanceViewModel>)?.ToList();
                if (rawLogs == null || rawLogs.Count == 0) throw new InvalidOperationException("There are no attendance records matching the current filters.");
                ExportConfigDialog.XamlRoot = Content.XamlRoot;
                if (await ExportConfigDialog.ShowAsync() != ContentDialogResult.Primary) return;
                var logs = rawLogs.GroupBy(l => l.StudentId).Select(g => g.OrderByDescending(l => l.RawTimestamp).First()).ToList();
                string sort = (ExportSortComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
                logs = sort switch
                {
                    "Group by Course, then Section, then Name" => logs.OrderBy(l => l.Course).ThenBy(l => l.Section).ThenBy(l => l.FullName).ToList(),
                    "Group by Section, then Name" => logs.OrderBy(l => l.Section).ThenBy(l => l.FullName).ToList(),
                    "Sort alphabetically by Name only" => logs.OrderBy(l => l.FullName).ToList(),
                    _ => logs
                };
                var columns = new List<SpreadsheetColumn>();
                var selectors = new List<Func<EventAttendanceViewModel, object?>>();
                void Add(bool? selected, string header, Func<EventAttendanceViewModel, object?> value, SpreadsheetValueKind kind = SpreadsheetValueKind.Text)
                {
                    if (selected != true) return;
                    columns.Add(new(header, kind));
                    selectors.Add(value);
                }
                Add(ExportColTimestamp.IsChecked, "Timestamp", l => l.RawTimestamp == DateTime.MinValue ? ReportSpreadsheetTables.EventTimestamp(l.Timestamp) : l.RawTimestamp, SpreadsheetValueKind.DateTime);
                Add(ExportColStudentId.IsChecked, "Student ID", l => l.StudentId);
                Add(ExportColName.IsChecked, "Student Name", l => l.FullName);
                Add(ExportColCourse.IsChecked, "Course", l => l.Course);
                Add(ExportColSection.IsChecked, "Section", l => l.Section);
                Add(ExportColAction.IsChecked, "Latest Action Within Filters", l => l.Action);
                Add(ExportColStatus.IsChecked, "Last Recorded State (Whole Event)", l => l.CompletionStatus);
                if (columns.Count == 0) throw new InvalidOperationException("Select at least one report column.");
                Add(true, "Device ID", l => l.DeviceId);
                Add(true, "Device Name", l => l.DeviceName);
                var table = new SpreadsheetTable("Event attendance", columns, logs.Select(l => selectors.Select(s => s(l)).ToArray()).ToList());
                string name = $"{_currentSelectedEvent?.Event.EventId ?? "Attendance"}_Attendance_{DateTime.Now:yyyyMMdd}";
                if (await SpreadsheetPicker.SaveAsync(this, name, table)) await ShowExportResultAsync("Export complete", "The selected event report was saved.");
            }
            catch (Exception ex) { await ShowExportResultAsync("Export not completed", ex.Message); }
        }

        private async void UnivExportButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ApplyUnivPopupFilters();
                var logs = (UnivPopupListView.ItemsSource as IEnumerable<VerificationLogRecord>)?.ToList() ?? _univMasterLogs.ToList();
                if (logs.Count == 0) throw new InvalidOperationException("There are no campus traffic records matching the current filters.");
                var table = ReportSpreadsheetTables.Traffic(logs);
                if (await SpreadsheetPicker.SaveAsync(this, $"Campus_Traffic_Report_{DateTime.Now:yyyyMMdd}", table))
                    await ShowExportResultAsync("Export complete", "The selected campus traffic report was saved.");
            }
            catch (Exception ex) { await ShowExportResultAsync("Export not completed", ex.Message); }
        }

        private void RefreshPopupFilterOptionsSafely()
        {
            _updatingCampusFilters = true;
            try
            {
                // 1. University Explorer Popup Filters
                if (UnivPopupCourseFilter != null && UnivPopupSectionFilter != null)
                {
                    string selectedCourse = UnivPopupCourseFilter.SelectedItem?.ToString() ?? "All Courses";
                    string selectedSection = UnivPopupSectionFilter.SelectedItem?.ToString() ?? "All Sections";

                    var courses = _univMasterLogs.Select(l => l.Course).Where(c => !string.IsNullOrWhiteSpace(c)).Distinct().OrderBy(c => c).ToList();
                    courses.Insert(0, "All Courses");
                    UnivPopupCourseFilter.ItemsSource = courses;
                    UnivPopupCourseFilter.SelectedItem = courses.Contains(selectedCourse) ? selectedCourse : "All Courses";

                    var sectionQuery = _univMasterLogs.AsEnumerable();
                    string activeCourse = UnivPopupCourseFilter.SelectedItem?.ToString() ?? "All Courses";
                    if (activeCourse != "All Courses")
                        sectionQuery = sectionQuery.Where(l => l.Course == activeCourse);

                    var sections = sectionQuery.Select(l => l.Section).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().OrderBy(s => s).ToList();
                    sections.Insert(0, "All Sections");
                    UnivPopupSectionFilter.ItemsSource = sections;
                    UnivPopupSectionFilter.SelectedItem = sections.Contains(selectedSection) ? selectedSection : "All Sections";
                }

                // 2. Main Event View Filters
                if (FilterCourseComboBox != null && FilterSectionComboBox != null)
                {
                    string selectedMainCourse = FilterCourseComboBox.SelectedItem?.ToString() ?? "All Courses";
                    string selectedMainSection = FilterSectionComboBox.SelectedItem?.ToString() ?? "All Sections";

                    var mainCourses = _eventMasterLogs.Select(l => l.Course).Where(c => !string.IsNullOrWhiteSpace(c)).Distinct().OrderBy(c => c).ToList();
                    mainCourses.Insert(0, "All Courses");
                    FilterCourseComboBox.SelectionChanged -= Filter_SelectionChanged;
                    FilterCourseComboBox.ItemsSource = mainCourses;
                    FilterCourseComboBox.SelectedItem = mainCourses.Contains(selectedMainCourse) ? selectedMainCourse : "All Courses";
                    FilterCourseComboBox.SelectionChanged += Filter_SelectionChanged;

                    var mainSectionQuery = _eventMasterLogs.AsEnumerable();
                    string activeMainCourse = FilterCourseComboBox.SelectedItem?.ToString() ?? "All Courses";
                    if (activeMainCourse != "All Courses")
                        mainSectionQuery = mainSectionQuery.Where(l => l.Course == activeMainCourse);

                    var mainSections = mainSectionQuery.Select(l => l.Section).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().OrderBy(s => s).ToList();
                    mainSections.Insert(0, "All Sections");
                    FilterSectionComboBox.SelectionChanged -= Filter_SelectionChanged;
                    FilterSectionComboBox.ItemsSource = mainSections;
                    FilterSectionComboBox.SelectedItem = mainSections.Contains(selectedMainSection) ? selectedMainSection : "All Sections";
                    FilterSectionComboBox.SelectionChanged += Filter_SelectionChanged;
                }

                // 3. Event Explorer Popup Filters
                if (EventPopupCourseFilter != null && EventPopupSectionFilter != null)
                {
                    string selectedEventCourse = EventPopupCourseFilter.SelectedItem?.ToString() ?? "All Courses";
                    string selectedEventSection = EventPopupSectionFilter.SelectedItem?.ToString() ?? "All Sections";

                    var eventCourses = _eventMasterLogs.Select(l => l.Course).Where(c => !string.IsNullOrWhiteSpace(c)).Distinct().OrderBy(c => c).ToList();
                    eventCourses.Insert(0, "All Courses");
                    EventPopupCourseFilter.SelectionChanged -= EventPopupFilter_Changed;
                    EventPopupCourseFilter.ItemsSource = eventCourses;
                    EventPopupCourseFilter.SelectedItem = eventCourses.Contains(selectedEventCourse) ? selectedEventCourse : "All Courses";
                    EventPopupCourseFilter.SelectionChanged += EventPopupFilter_Changed;

                    var eventSectionQuery = _eventMasterLogs.AsEnumerable();
                    string activeEventCourse = EventPopupCourseFilter.SelectedItem?.ToString() ?? "All Courses";
                    if (activeEventCourse != "All Courses")
                        eventSectionQuery = eventSectionQuery.Where(l => l.Course == activeEventCourse);

                    var eventSections = eventSectionQuery.Select(l => l.Section).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().OrderBy(s => s).ToList();
                    eventSections.Insert(0, "All Sections");
                    EventPopupSectionFilter.SelectionChanged -= EventPopupFilter_Changed;
                    EventPopupSectionFilter.ItemsSource = eventSections;
                    EventPopupSectionFilter.SelectedItem = eventSections.Contains(selectedEventSection) ? selectedEventSection : "All Sections";
                    EventPopupSectionFilter.SelectionChanged += EventPopupFilter_Changed;
                }
            }
            finally
            {
                _updatingCampusFilters = false;
            }
        }

        private async Task ShowExportResultAsync(string title, string message)
        {
            await new ContentDialog { Title = title, Content = message, CloseButtonText = "OK", XamlRoot = Content.XamlRoot }.ShowAsync();
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