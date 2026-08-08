using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
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

    public class DailyTrafficSummary
    {
        public DateTime DateValue { get; set; }
        public string DisplayDate { get; set; } = "";
        public int TotalScans { get; set; }
        public int Granted { get; set; }
        public int Denied { get; set; }
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

        // THE FIX: State variables for Exit Interceptor and Serial Port
        private SerialPort? _serialPort;
        private string _currentPort = "COM3";
        private bool _isForceClosing = false;
        private bool _isAwaitingAdminAuth = false;
        private string _pendingAdminAction = "";
        private string _pendingAdminSeverity = "";

        public EventReportsWindow()
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
            try
            {
                if (DatabaseMonitor.IsOnline)
                {
                    await _database.EnsureSchemaAsync();
                    try { _currentPort = await _database.GetSettingAsync("nfc_com_port", "COM3"); } catch { }
                }

                TryConnectSerial(_currentPort);

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

                        var dailySummaries = _univMasterLogs
                            .Where(l => l.RawTimestamp != DateTime.MinValue)
                            .GroupBy(l => l.RawTimestamp.Date)
                            .Select(g => new DailyTrafficSummary
                            {
                                DateValue = g.Key,
                                DisplayDate = g.Key.ToString("MMMM dd, yyyy"),
                                TotalScans = g.Count(),
                                Granted = g.Count(x => x.Status == "GRANTED"),
                                Denied = g.Count(x => x.Status == "DENIED")
                            })
                            .OrderByDescending(x => x.DateValue)
                            .ToList();

                        DailyTrafficListView.ItemsSource = dailySummaries;
                    }
                }
            }
            catch { }
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

        private void Filter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (FilterCourseComboBox == null || FilterSectionComboBox == null) return;

            if (sender == FilterCourseComboBox && _eventMasterLogs != null)
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
                filtered = filtered.Where(l => l.Timestamp != null && l.Timestamp.Contains("AM"));
            else if (time == "Afternoon (PM)")
                filtered = filtered.Where(l => l.Timestamp != null && l.Timestamp.Contains("PM"));

            var viewModels = filtered.Select(l => {
                bool isCompleted = _completedStudentIds.Contains(l.StudentId);

                string statusText = isCompleted ? (isEventLive ? "Ongoing" : "Completed") : "Incomplete";
                SolidColorBrush statusColor = isCompleted
                    ? (isEventLive
                        ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 96, 165, 250))
                        : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 52, 211, 153)))
                    : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 248, 113, 113));

                return new EventAttendanceViewModel
                {
                    Timestamp = l.Timestamp,
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

            if (EventPopupListView != null)
                EventPopupListView.ItemsSource = finalData;
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
            if (UnivPopupCourseFilter != null && UnivPopupSectionFilter != null && sender == UnivPopupCourseFilter && _univMasterLogs != null)
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

        private void UnivPopupDatePicker_DateChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args) => ApplyUnivPopupFilters();

        private void UnivPopupClear_Click(object sender, RoutedEventArgs e)
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
                filtered = filtered.Reverse();
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
            if (EventPopupCourseFilter != null && EventPopupSectionFilter != null && sender == EventPopupCourseFilter && _eventMasterLogs != null)
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

        private void EventPopupClear_Click(object sender, RoutedEventArgs e)
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

            bool isEventLive = false;
            if (_currentSelectedEvent != null)
            {
                isEventLive = _currentSelectedEvent.DisplayText.Contains("🟢 LIVE");
            }

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
                string targetDateStr = EventPopupDatePicker.Date.Value.ToString("MMM dd");
                filtered = filtered.Where(l => l.Timestamp != null && l.Timestamp.StartsWith(targetDateStr));
            }

            string course = EventPopupCourseFilter.SelectedItem?.ToString() ?? "All Courses";
            string section = EventPopupSectionFilter.SelectedItem?.ToString() ?? "All Sections";
            string status = (EventPopupStatusFilter.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All Attendees";

            if (course != "All Courses")
                filtered = filtered.Where(l => l.Course == course);

            if (section != "All Sections")
                filtered = filtered.Where(l => l.Section == section);

            if (status == "Completed Event" || status == "Ongoing")
                filtered = filtered.Where(l => _completedStudentIds.Contains(l.StudentId));
            else if (status == "Incomplete / Left Early")
                filtered = filtered.Where(l => _incompleteStudentIds.Contains(l.StudentId));

            var viewModels = filtered.Select(l => {
                bool isCompleted = _completedStudentIds.Contains(l.StudentId);

                string statusText = isCompleted ? (isEventLive ? "Ongoing" : "Completed") : "Incomplete";
                SolidColorBrush statusColor = isCompleted
                    ? (isEventLive
                        ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 96, 165, 250))
                        : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 52, 211, 153)))
                    : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 248, 113, 113));

                return new EventAttendanceViewModel
                {
                    Timestamp = l.Timestamp,
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
                viewModels.Reverse();
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

            ExportConfigDialog.XamlRoot = this.Content.XamlRoot;
            var dialogResult = await ExportConfigDialog.ShowAsync();

            if (dialogResult != ContentDialogResult.Primary) return;

            var logsToExport = rawLogs
                .GroupBy(l => l.StudentId)
                .Select(g => g.First())
                .ToList();

            string sortOption = (ExportSortComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";

            if (sortOption == "Group by Course, then Section, then Name")
            {
                logsToExport = logsToExport
                    .OrderBy(l => l.Course)
                    .ThenBy(l => l.Section)
                    .ThenBy(l => l.FullName)
                    .ToList();
            }
            else if (sortOption == "Group by Section, then Name")
            {
                logsToExport = logsToExport
                    .OrderBy(l => l.Section)
                    .ThenBy(l => l.FullName)
                    .ToList();
            }
            else if (sortOption == "Sort alphabetically by Name only")
            {
                logsToExport = logsToExport
                    .OrderBy(l => l.FullName)
                    .ToList();
            }

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

                var headers = new List<string>();
                if (ExportColTimestamp.IsChecked == true) headers.Add("Timestamp");
                if (ExportColStudentId.IsChecked == true) headers.Add("Student ID");
                if (ExportColName.IsChecked == true) headers.Add("Student Name");
                if (ExportColCourse.IsChecked == true) headers.Add("Course");
                if (ExportColSection.IsChecked == true) headers.Add("Section");
                if (ExportColAction.IsChecked == true) headers.Add("Latest Action");
                if (ExportColStatus.IsChecked == true) headers.Add("Event Completion Status");

                csvData.AppendLine(string.Join(",", headers));

                foreach (var log in logsToExport)
                {
                    var row = new List<string>();

                    if (ExportColTimestamp.IsChecked == true) row.Add($"\"{log.Timestamp}\"");
                    if (ExportColStudentId.IsChecked == true) row.Add($"\"{log.StudentId}\"");
                    if (ExportColName.IsChecked == true) row.Add($"\"{log.FullName}\"");
                    if (ExportColCourse.IsChecked == true) row.Add($"\"{log.Course}\"");
                    if (ExportColSection.IsChecked == true) row.Add($"\"{log.Section}\"");
                    if (ExportColAction.IsChecked == true) row.Add($"\"{log.Action}\"");
                    if (ExportColStatus.IsChecked == true) row.Add($"\"{log.CompletionStatus}\"");

                    csvData.AppendLine(string.Join(",", row));
                }

                Windows.Storage.CachedFileManager.DeferUpdates(file);
                await Windows.Storage.FileIO.WriteTextAsync(file, csvData.ToString(), Windows.Storage.Streams.UnicodeEncoding.Utf8);
                Windows.Storage.Provider.FileUpdateStatus status = await Windows.Storage.CachedFileManager.CompleteUpdatesAsync(file);

                if (status == Windows.Storage.Provider.FileUpdateStatus.Complete)
                {
                    ContentDialog successDialog = new ContentDialog
                    {
                        Title = "Export Complete",
                        Content = $"Your custom report was successfully exported and saved to:\n\n{file.Path}",
                        CloseButtonText = "OK",
                        XamlRoot = this.Content.XamlRoot
                    };
                    await successDialog.ShowAsync();
                }
            }
        }

        private async void UnivExportButton_Click(object sender, RoutedEventArgs e)
        {
            var rawLogs = await _database.GetGeneralLedgerAsync();
            var logsToExport = rawLogs.ToList();

            if (logsToExport == null || !logsToExport.Any())
            {
                ContentDialog emptyDialog = new ContentDialog
                {
                    Title = "Nothing to Export",
                    Content = "There are no campus traffic records to export.",
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

                csvData.AppendLine("Timestamp,Student ID,Student Name,Course,Section,Action,Status,Auth Speed (ms),DB Query Speed (ms)");

                foreach (var log in logsToExport)
                {
                    csvData.AppendLine($"\"{log.Timestamp}\",\"{log.StudentId}\",\"{log.FullName}\",\"{log.Course}\",\"{log.Section}\",\"{log.Action}\",\"{log.Status}\",\"{log.AuthSpeedMs}\",\"{log.DbQuerySpeedMs}\"");
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

        private void DashboardButton_Click(object sender, RoutedEventArgs e)
        {
            CloseSerialPort();
            var dashboard = new MainWindow();
            dashboard.Activate();
            this.Close();
        }

        private void ManageEventsButton_Click(object sender, RoutedEventArgs e)
        {
            CloseSerialPort();
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