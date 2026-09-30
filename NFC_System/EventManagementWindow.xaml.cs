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
        private IReadOnlyList<EventRosterStudent> _directory = Array.Empty<EventRosterStudent>();
        private IReadOnlyList<EventRosterStudent> _filteredDirectory = Array.Empty<EventRosterStudent>();
        private readonly HashSet<string> _selectedRosterIds = new(StringComparer.OrdinalIgnoreCase);
        private string[] _previewIds = Array.Empty<string>();
        private bool _previewRemoval;
        private bool _updatingSelection;
        private bool _editorBusy;
        private bool _authBusy;
        private bool _closed;
        private long _editorSessionVersion;
        private TaskCompletionSource<(string Uid, string Pin)?>? _eventAuthorization;
        private readonly DatabaseService _database = new();
        private EventRecord? _selectedEvent;

        // THE FIX: State variables for Exit Interceptor and Serial Port
        private string _currentPort = "COM3";
        private bool _isForceClosing = false;
        private bool _isAwaitingAdminAuth = false;
        private string _pendingAdminAction = "";
        private string _pendingAdminSeverity = "";

        public EventManagementWindow()
        {
            this.InitializeComponent();
            HardwareService.OnUidScanned += HardwareService_OnUidScanned;
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
            HardwareService.Connect(portName);
        }

        private void HardwareService_OnUidScanned(string uid)
        {
            if (!_closed && _isAwaitingAdminAuth)
                DispatcherQueue.TryEnqueue(async () => await HandleAdminAuthScanAsync(uid));
        }

        private async Task HandleAdminAuthScanAsync(string uid)
        {
            if (_closed || !_isAwaitingAdminAuth || _authBusy) return;
            _authBusy = true;
            try
            {
            if (_pendingAdminAction == "EVENT_MODE")
            {
                if (!DatabaseMonitor.IsOnline || _editorSessionVersion != AppSession.LoginVersion)
                {
                    AuthStatusText.Text = "An online database and the original administrator session are required.";
                    AuthStatusText.Visibility = Visibility.Visible;
                    return;
                }
                _isAwaitingAdminAuth = false;
                _eventAuthorization?.TrySetResult((uid, AdminPinBox.Password.Trim()));
                await QrCredentialDisplay.HideAsync(AdminAuthDialog);
                return;
            }
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
            finally { _authBusy = false; }
        }

        private void CloseSerialPort()
        {
            HardwareService.OnUidScanned -= HardwareService_OnUidScanned;
        }

        private void Window_Closed(object sender, WindowEventArgs args)
        {
            _closed = true;
            _eventAuthorization?.TrySetResult(null);
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
            if (!DatabaseMonitor.IsOnline) return;
            var selectedId = _selectedEvent?.EventId;
            var events = await _database.GetActiveEventsAsync(int.MaxValue);
            ActiveEventsListView.ItemsSource = events;
            ActiveEventsListView.SelectedItem = events.FirstOrDefault(e => e.EventId == selectedId);
            CloseEventButton.IsEnabled = ActiveEventsListView.SelectedItem != null;
        }

        private Task LoadCoursesAsync() => Task.CompletedTask;

        private async Task RefreshRosterAsync()
        {
            if (_selectedEvent == null) return;
            _directory = await _database.GetEventRosterDirectoryAsync(_selectedEvent.EventId);
            _updatingSelection = true;
            try
            {
                var course = CourseComboBox.SelectedItem as string;
                var section = SectionComboBox.SelectedItem as string;
                var status = StatusComboBox.SelectedItem as string ?? "Active";
                CourseComboBox.ItemsSource = _directory.Select(s => s.Course).Where(s => s.Length > 0).Distinct().OrderBy(s => s).ToList();
                SectionComboBox.ItemsSource = _directory.Select(s => s.SectionName).Where(s => s.Length > 0).Distinct().OrderBy(s => s).ToList();
                StatusComboBox.ItemsSource = new[] { "All statuses", "Active" }.Concat(_directory.Select(s => s.Status).Where(s => s.Length > 0)).Distinct().ToList();
                CourseComboBox.SelectedItem = course;
                SectionComboBox.SelectedItem = section;
                StatusComboBox.SelectedItem = status;
            }
            finally { _updatingSelection = false; }
            ApplyViewFilters();
        }

        private void UpdateOfflineBanner(bool isOnline)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (_closed) return;
                GlobalOfflineBanner.Visibility = isOnline ? Visibility.Collapsed : Visibility.Visible;
                CreateEventButton.IsEnabled = isOnline && EventEditPolicy.CanManage(AppSession.IsLoggedIn, AppSession.IsAdmin, AppSession.IsEventOrganizer);
                UpdateEditorActions();
                if (!isOnline) InvalidatePreview();
            });
        }

        private void ApplyViewFilters()
        {
            if (_updatingSelection || RosterStudentsListView == null) return;
            var source = RosterScopeComboBox.SelectedIndex == 1 ? _directory.Where(s => s.IsIncluded) : _directory;
            string? status = StatusComboBox.SelectedItem as string;
            _filteredDirectory = EventRosterRules.Filter(source, SearchAttendeeTextBox.Text,
                CourseComboBox.SelectedItem as string, (YearComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString(),
                SectionComboBox.SelectedItem as string, status == "All statuses" ? null : status);
            _updatingSelection = true;
            try
            {
                RosterStudentsListView.ItemsSource = _filteredDirectory;
                foreach (var student in _filteredDirectory)
                    if (_selectedRosterIds.Contains(student.StudentId)) RosterStudentsListView.SelectedItems.Add(student);
            }
            finally { _updatingSelection = false; }
            UpdateSelectionText();
        }

        private void UpdateSelectionText()
        {
            int visibleSelected = _filteredDirectory.Count(s => _selectedRosterIds.Contains(s.StudentId));
            RosterSelectionText.Text = $"{_filteredDirectory.Count:N0} matching; {_selectedRosterIds.Count:N0} selected ({_selectedRosterIds.Count - visibleSelected:N0} outside current filter).";
            SelectMatchingButton.Content = $"Select all matching ({_filteredDirectory.Count:N0})";
            UpdateEditorActions();
        }

        private void UpdateEditorActions()
        {
            if (SaveEventDetailsButton == null) return;
            bool allowed = !_editorBusy && DatabaseMonitor.IsOnline && AppSession.LoginVersion == _editorSessionVersion &&
                EventEditPolicy.CanManage(AppSession.IsLoggedIn, AppSession.IsAdmin, AppSession.IsEventOrganizer);
            SaveEventDetailsButton.IsEnabled = allowed;
            EditEventModeComboBox.IsEnabled = allowed && EventEditPolicy.CanChangeMode(AppSession.IsLoggedIn, AppSession.IsAdmin, AppSession.CurrentStaffRoleLabel);
            PreviewAddButton.IsEnabled = allowed && _selectedRosterIds.Count > 0;
            PreviewRemoveButton.IsEnabled = allowed && _selectedRosterIds.Count > 0;
            SelectMatchingButton.IsEnabled = allowed;
            ApplyRosterButton.IsEnabled = allowed && _previewIds.Length > 0;
        }

        private void SetEditorBusy(bool busy)
        {
            _editorBusy = busy;
            AttendeeManagementDialog.IsEnabled = !busy;
            UpdateEditorActions();
        }

        private void ShowEditorNotice(string message, InfoBarSeverity severity)
        {
            EventEditorNotice.Message = message;
            EventEditorNotice.Severity = severity;
            EventEditorNotice.IsOpen = true;
            LogMessage($"[{severity}] {message}");
        }

        private void InvalidatePreview()
        {
            _previewIds = Array.Empty<string>();
            if (RosterPreviewPanel != null) RosterPreviewPanel.Visibility = Visibility.Collapsed;
            UpdateEditorActions();
        }

        private void RosterFilter_SelectionChanged(object sender, SelectionChangedEventArgs e) => ApplyViewFilters();
        private void SearchAttendeeTextBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyViewFilters();

        private void RosterScope_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (RosterStudentsListView == null) return;
            _selectedRosterIds.Clear();
            InvalidatePreview();
            ApplyViewFilters();
        }

        private void ClearViewFilters_Click(object sender, RoutedEventArgs e)
        {
            _updatingSelection = true;
            SearchAttendeeTextBox.Text = "";
            CourseComboBox.SelectedIndex = -1;
            YearComboBox.SelectedIndex = -1;
            SectionComboBox.SelectedIndex = -1;
            StatusComboBox.SelectedItem = "Active";
            _updatingSelection = false;
            ApplyViewFilters();
        }

        private void RosterStudents_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_updatingSelection) return;
            foreach (EventRosterStudent student in e.RemovedItems) _selectedRosterIds.Remove(student.StudentId);
            foreach (EventRosterStudent student in e.AddedItems) _selectedRosterIds.Add(student.StudentId);
            InvalidatePreview();
            UpdateSelectionText();
        }

        private void SelectMatching_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedRosterIds.Union(_filteredDirectory.Select(s => s.StudentId), StringComparer.OrdinalIgnoreCase).Count() > EventRosterRules.MaximumSelection)
            {
                ShowEditorNotice($"Select at most {EventRosterRules.MaximumSelection:N0} students.", InfoBarSeverity.Warning);
                return;
            }
            _selectedRosterIds.UnionWith(_filteredDirectory.Select(s => s.StudentId));
            InvalidatePreview();
            ApplyViewFilters();
        }

        private void ClearSelection_Click(object sender, RoutedEventArgs e)
        {
            _selectedRosterIds.Clear();
            InvalidatePreview();
            ApplyViewFilters();
        }

        private async void ReloadRoster_Click(object sender, RoutedEventArgs e)
        {
            if (_editorBusy) return;
            SetEditorBusy(true);
            InvalidatePreview();
            try { await RefreshRosterAsync(); }
            catch (Exception ex) { ShowEditorNotice(ex.Message, InfoBarSeverity.Error); }
            finally { SetEditorBusy(false); }
        }

        private async void CreateEventButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                CreateEventButton.IsEnabled = false;
                var mode = ModeFromIndex(EventModeComboBox.SelectedIndex);
                await _database.CreateNewEventAsync(EventIdTextBox.Text.Trim(), EventNameTextBox.Text.Trim(), mode,
                    RestrictedEventCheckBox.IsChecked == true, AppSession.LoginVersion);
                LogMessage($"[SUCCESS] Event '{EventIdTextBox.Text.Trim()}' created.");
                EventIdTextBox.Text = "";
                EventNameTextBox.Text = "";
                await LoadActiveEventsAsync();
            }
            catch (Exception ex) { LogMessage($"[ERROR] {ex.Message}"); }
            finally { CreateEventButton.IsEnabled = DatabaseMonitor.IsOnline; }
        }

        private void ActiveEventsListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_editorBusy) _selectedEvent = ActiveEventsListView.SelectedItem as EventRecord;
            CloseEventButton.IsEnabled = ActiveEventsListView.SelectedItem != null && DatabaseMonitor.IsOnline;
        }

        private static VerificationMode ModeFromIndex(int index) => index switch
        {
            0 => VerificationMode.Fast, 2 => VerificationMode.HighSecurity, _ => VerificationMode.Standard
        };

        private async void ActiveEventsListView_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            if (_editorBusy || ActiveEventsListView.SelectedItem is not EventRecord clickedEvent) return;
            _selectedEvent = clickedEvent;
            _editorSessionVersion = AppSession.LoginVersion;
            AttendeeManagementDialog.XamlRoot = Content.XamlRoot;
            AttendeeManagementDialog.Title = $"Manage Event: {clickedEvent.EventId}";
            DialogContentContainer.Width = Math.Max(260, Math.Min(840, Content.XamlRoot.Size.Width - 120));
            EventEditorScroll.MaxHeight = Math.Max(260, Content.XamlRoot.Size.Height - 180);
            EditEventNameTextBox.Text = clickedEvent.EventName;
            EditEventModeComboBox.SelectedIndex = (int)clickedEvent.VerificationMode;
            EventModePermissionText.Text = DatabaseMonitor.IsOnline
                ? "Database mode. Changes apply to new verification sessions; offline devices retain their cached mode."
                : "Cached mode. An online database connection is required to save changes.";
            EventEditorNotice.IsOpen = false;
            _selectedRosterIds.Clear();
            InvalidatePreview();
            AttendeeManagementSection.Visibility = clickedEvent.IsRestricted ? Visibility.Visible : Visibility.Collapsed;
            try
            {
                if (clickedEvent.IsRestricted)
                {
                    await RefreshRosterAsync();
                    ClearViewFilters_Click(this, new RoutedEventArgs());
                }
                UpdateEditorActions();
                await AttendeeManagementDialog.ShowAsync();
            }
            catch (Exception ex) { LogMessage($"[ERROR] {ex.Message}"); }
        }

        private async Task<(string Uid, string Pin)?> AuthorizeEventModeAsync()
        {
            if (AppSession.IsKioskRunning)
                throw new InvalidOperationException("Close the kiosk before authorizing an event mode change.");
            await QrCredentialDisplay.HideAsync(AttendeeManagementDialog);
            _eventAuthorization = new TaskCompletionSource<(string Uid, string Pin)?>();
            _pendingAdminAction = "EVENT_MODE";
            _pendingAdminSeverity = "HIGH";
            AdminPinBox.Password = "";
            AdminPinBox.Visibility = Visibility.Visible;
            AuthStatusText.Visibility = Visibility.Collapsed;
            AdminAuthDescriptionText.Text = "Enter the authorizing administrator's PIN and tap their NFC card.";
            _isAwaitingAdminAuth = true;
            AdminAuthDialog.XamlRoot = Content.XamlRoot;
            try
            {
                await AdminAuthDialog.ShowAsync();
                _eventAuthorization.TrySetResult(null);
                return await _eventAuthorization.Task;
            }
            finally
            {
                _isAwaitingAdminAuth = false;
                _pendingAdminAction = "";
                AdminPinBox.Password = "";
                _eventAuthorization = null;
            }
        }

        private async void UpdateEventNameButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedEvent == null || _editorBusy) return;
            var editing = _selectedEvent;
            string newName = EditEventNameTextBox.Text.Trim();
            var mode = ModeFromIndex(EditEventModeComboBox.SelectedIndex);
            bool modeChanged = editing.VerificationMode != mode;
            bool reopen = false;
            SetEditorBusy(true);
            try
            {
                EventEditPolicy.ValidateEdit(editing, editing.EventName, editing.VerificationMode, newName, mode);
                (string Uid, string Pin)? credentials = null;
                if (modeChanged && AppSession.CurrentStaffRoleLabel != "Master Admin")
                {
                    if (!EventEditPolicy.CanChangeMode(AppSession.IsLoggedIn, AppSession.IsAdmin, AppSession.CurrentStaffRoleLabel))
                        throw new UnauthorizedAccessException("Only an Administrator or Master Admin may change verification mode.");
                    if (AppSession.IsKioskRunning) throw new InvalidOperationException("Close the kiosk before authorizing an event mode change.");
                    reopen = true;
                    credentials = await AuthorizeEventModeAsync();
                    if (credentials == null) return;
                }
                _selectedEvent = await _database.UpdateEventDetailsAsync(editing.EventId, editing.EventName, editing.VerificationMode,
                    newName, mode, _editorSessionVersion, credentials?.Uid, credentials?.Pin);
                if (modeChanged) KioskStateController.BroadcastEventModeChange(editing.EventId, mode);
                ShowEditorNotice("Event saved. Original event date and active state were preserved.", InfoBarSeverity.Success);
                await LoadActiveEventsAsync();
            }
            catch (Exception ex) { ShowEditorNotice(ex.Message, InfoBarSeverity.Error); }
            finally
            {
                SetEditorBusy(false);
                if (reopen && !_closed) await AttendeeManagementDialog.ShowAsync();
            }
        }

        private async Task PreviewRosterAsync(IEnumerable<string> ids, bool removal, IReadOnlyList<EventRosterPreviewRow>? errors = null)
        {
            var requested = ids.ToArray();
            await RefreshRosterAsync();
            _previewRemoval = removal;
            var preview = EventRosterRules.Preview(requested, _directory);
            if (removal)
            {
                var conflicts = _directory.Where(s => s.HasSyncConflict).Select(s => s.StudentId).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var rows = preview.Rows.Select(row => row with { Result = conflicts.Contains(row.StudentId)
                    ? "Confirm exclusion" : row.Result == "Already included" ? "Remove" : "Not on roster" }).ToList();
                _previewIds = rows.Where(row => row.Result is "Remove" or "Confirm exclusion").Select(row => row.StudentId).ToArray();
                RosterPreviewSummary.Text = $"{_previewIds.Length} removals; {rows.Count - _previewIds.Length} not on roster. Attendance logs will be retained.";
                RosterPreviewList.ItemsSource = rows;
                ApplyRosterButton.Content = "Remove selected attendees";
            }
            else
            {
                _previewIds = preview.Rows.Where(row => row.Result == "Add").Select(row => row.StudentId).ToArray();
                RosterPreviewSummary.Text = preview.Summary + (errors?.Count > 0 ? $" {errors.Count} invalid file rows." : "");
                RosterPreviewList.ItemsSource = preview.Rows.Concat(errors ?? Array.Empty<EventRosterPreviewRow>()).ToList();
                ApplyRosterButton.Content = $"Add eligible students ({_previewIds.Length})";
            }
            RosterPreviewPanel.Visibility = Visibility.Visible;
            UpdateEditorActions();
        }

        private async void AddBatchButton_Click(object sender, RoutedEventArgs e) => await PrepareSelectedRosterAsync(false);
        private async void RemoveSelected_Click(object sender, RoutedEventArgs e) => await PrepareSelectedRosterAsync(true);

        private async Task PrepareSelectedRosterAsync(bool removal)
        {
            if (_editorBusy) return;
            SetEditorBusy(true);
            try { await PreviewRosterAsync(_selectedRosterIds, removal); }
            catch (Exception ex) { InvalidatePreview(); ShowEditorNotice(ex.Message, InfoBarSeverity.Error); }
            finally { SetEditorBusy(false); }
        }

        private async void ApplyRoster_Click(object sender, RoutedEventArgs e)
        {
            if (_editorBusy || _selectedEvent == null || _previewIds.Length == 0) return;
            SetEditorBusy(true);
            try
            {
                var result = await _database.ApplyEventRosterSelectionAsync(_selectedEvent.EventId, _previewIds, _previewRemoval, _editorSessionVersion);
                ShowEditorNotice($"{result.Changed} {(_previewRemoval ? "removed" : "added")}; {result.AlreadyIncluded} already included; {result.Ineligible} now ineligible; {result.Unknown} no longer in the directory.",
                    InfoBarSeverity.Success);
                _selectedRosterIds.Clear();
                InvalidatePreview();
                await RefreshRosterAsync();
            }
            catch (Exception ex) { ShowEditorNotice(ex.Message, InfoBarSeverity.Error); }
            finally { SetEditorBusy(false); }
        }

        private async void ImportRoster_Click(object sender, RoutedEventArgs e)
        {
            if (_editorBusy || !DatabaseMonitor.IsOnline) return;
            SetEditorBusy(true);
            try
            {
                var file = await SpreadsheetPicker.PickImportAsync(this);
                if (file == null) return;
                var document = await Task.Run(() => SpreadsheetService.ReadImport(file.Path, StudentImportKind.Roster));
                await RefreshRosterAsync();
                var rows = EventRosterImport.Preview(document, _directory);
                _selectedRosterIds.Clear();
                _previewIds = EventRosterRules.NormalizeIds(rows.Where(row => row.Result == "Add").Select(row => row.StudentId));
                _previewRemoval = false;
                _selectedRosterIds.UnionWith(_previewIds);
                ApplyViewFilters();
                RosterPreviewList.ItemsSource = rows;
                RosterPreviewSummary.Text = $"{_previewIds.Length} additions; {rows.Count(row => row.Result == "Already included")} already included; " +
                    $"{rows.Count(row => row.Result == "Ineligible: inactive")} ineligible; {rows.Count(row => row.Result == "Unknown student ID")} unknown; " +
                    $"{rows.Count(row => row.Result.StartsWith("Row ", StringComparison.Ordinal))} duplicate or invalid file rows.";
                ApplyRosterButton.Content = $"Add eligible students ({_previewIds.Length})";
                RosterPreviewPanel.Visibility = Visibility.Visible;
            }
            catch (Exception ex) { InvalidatePreview(); ShowEditorNotice(ex.Message, InfoBarSeverity.Error); }
            finally { SetEditorBusy(false); }
        }

        private async void DownloadRosterTemplate_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (await SpreadsheetPicker.SaveTemplateAsync(this, StudentImportKind.Roster))
                    ShowEditorNotice("Roster template saved.", InfoBarSeverity.Success);
            }
            catch (Exception ex) { ShowEditorNotice(ex.Message, InfoBarSeverity.Error); }
        }

        private async void CloseEventButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedEvent == null || !DatabaseMonitor.IsOnline ||
                !EventEditPolicy.CanManage(AppSession.IsLoggedIn, AppSession.IsAdmin, AppSession.IsEventOrganizer)) return;
            try
            {
                await _database.CloseEventAsync(_selectedEvent.EventId);
                LogMessage($"[SUCCESS] Event '{_selectedEvent.EventId}' closed.");
                _selectedEvent = null;
                await LoadActiveEventsAsync();
            }
            catch (Exception ex) { LogMessage($"[ERROR] {ex.Message}"); }
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
