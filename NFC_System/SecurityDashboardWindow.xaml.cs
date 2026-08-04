using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Threading.Tasks;
using WinRT.Interop;

namespace NFC_System
{
    public sealed partial class SecurityDashboardWindow : Window
    {
        private readonly DatabaseService _database = new();
        private List<SystemAuditLog> _masterLogsCache = new();
        private SerialPort? _serialPort;

        // State variables for RBAC authorization
        private bool _isAwaitingAdminAuth = false;
        private string _pendingStaffName = "";
        private string _pendingStaffUid = "";
        private string _pendingStaffRole = "";
        private string _pendingAdminAction = ""; // "REGISTER_STAFF", "UPLOAD_DATA", "DOWNLOAD_DATA"
        // DEBOUNCE TIMER FOR SEARCH
        private readonly DispatcherTimer _searchDebounceTimer = new();

        public SecurityDashboardWindow()
        {
            this.InitializeComponent();
            MaximizeWindow();

            this.Closed += Window_Closed;
            ResetPinButton.Click += ResetPinButton_Click;

            // Setup the 500ms delay timer for database searching
            _searchDebounceTimer.Interval = TimeSpan.FromMilliseconds(500);
            _searchDebounceTimer.Tick += SearchDebounceTimer_Tick;

            _ = InitializeAsync();
        }

        private async Task InitializeAsync()
        {
            try
            {
                await _database.EnsureSchemaAsync();
                await RefreshDashboardAsync();

                string nfcPort = await _database.GetSettingAsync("nfc_com_port", "COM3");
                TryConnectSerial(nfcPort);
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"Database setup failed: {ex.Message}";
                StatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 248, 113, 113));
                PlayErrorAlert();
            }
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

        // THE FIX (ITEM 7): Native Error SFX Beep for failures
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

        private void TryConnectSerial(string portName)
        {
            try
            {
                _serialPort = new SerialPort(portName, 115200);
                _serialPort.NewLine = "\n";
                _serialPort.DataReceived += SerialPort_DataReceived;
                _serialPort.Open();
                StatusTextBlock.Text = $"Ready. NFC connected on {portName}";
                StatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White);
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"NFC disconnected: {ex.Message}";
                StatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 248, 113, 113));
                PlayErrorAlert();
            }
        }

        private void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                if (_serialPort == null || !_serialPort.IsOpen) return;
                string line = _serialPort.ReadLine().Trim();

                if (line.StartsWith("UID="))
                {
                    string uid = line.Substring(4).Trim();

                    DispatcherQueue.TryEnqueue(async () =>
                    {
                        if (_isAwaitingAdminAuth)
                        {
                            var details = await _database.GetStaffDetailsAsync(uid);

                            if (details.Role == "Administrator" || details.Role == "Master Administrator")
                            {
                                _isAwaitingAdminAuth = false;
                                AdminAuthDialog.Hide();
                                PlaySuccessPing();

                                string authorizedByName = details.FullName ?? "Admin";
                                string actionToRun = _pendingAdminAction;
                                _pendingAdminAction = "";

                                switch (actionToRun)
                                {
                                    case "REGISTER_STAFF":
                                        await ExecuteStaffRegistration(_pendingStaffUid, _pendingStaffName, _pendingStaffRole, authorizedByName);
                                        break;
                                    case "UPLOAD_DATA":
                                        await ExecuteUploadDataAsync(authorizedByName);
                                        break;
                                    case "DOWNLOAD_DATA":
                                        await ExecuteDownloadDataAsync(authorizedByName);
                                        break;
                                }
                            }
                            else
                            {
                                _isAwaitingAdminAuth = false;
                                _pendingAdminAction = "";
                                AdminAuthDialog.Hide();
                                StatusTextBlock.Text = "Authorization Denied: Tapped card is not an Administrator.";
                                StatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 248, 113, 113));
                                PlayErrorAlert();
                            }
                        }
                        else
                        {
                            StaffNfcUidTextBox.Text = uid;
                            StatusTextBlock.Text = "Card scanned. Ready to register staff.";
                            StatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White);
                            PlaySuccessPing();
                        }
                    });
                }
            }
            catch { }
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
            CloseSerialPort();
        }

        private async Task RefreshDashboardAsync()
        {
            try
            {
                var recentLogs = await _database.GetMasterAuditLogsAsync(30);
                RecentActivityListView.ItemsSource = recentLogs;
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"Could not refresh dashboard: {ex.Message}";
                StatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 248, 113, 113));
                PlayErrorAlert();
            }
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            _ = RefreshDashboardAsync();
        }

        private async void OpenPopupLogsButton_Click(object sender, RoutedEventArgs e)
        {
            MasterLogsDialog.XamlRoot = this.Content.XamlRoot;
            DialogLogContainer.Width = 900;
            PopupExpandToggle.IsChecked = false;
            PopupExpandToggle.Content = "⛶ Expand View";

            // Load initial top 2000 with no filters
            await ApplyServerSidePopupFiltersAsync();

            await MasterLogsDialog.ShowAsync();
        }

        private void PopupExpandToggle_Click(object sender, RoutedEventArgs e)
        {
            if (PopupExpandToggle.IsChecked == true)
            {
                DialogLogContainer.Width = 1400;
                PopupExpandToggle.Content = "⮌ Collapse View";
            }
            else
            {
                DialogLogContainer.Width = 900;
                PopupExpandToggle.Content = "⛶ Expand View";
            }
        }

        // Whenever the user types or changes a dropdown, restart the 500ms timer
        private void PopupFilter_Changed(object sender, RoutedEventArgs e)
        {
            if (PopupLogsListView == null) return; // Prevent firing during window init

            _searchDebounceTimer.Stop();
            _searchDebounceTimer.Start();
        }

        private void PopupDatePicker_DateChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args)
        {
            if (PopupLogsListView == null) return;

            _searchDebounceTimer.Stop();
            _searchDebounceTimer.Start();
        }

        // When the timer finishes (meaning the user stopped typing), execute the DB query
        private async void SearchDebounceTimer_Tick(object? sender, object e)
        {
            _searchDebounceTimer.Stop();
            await ApplyServerSidePopupFiltersAsync();
        }

        private async void PopupClear_Click(object sender, RoutedEventArgs e)
        {
            PopupSearchBox.Text = "";
            PopupTypeFilter.SelectedIndex = 0;
            PopupStatusFilter.SelectedIndex = 0;
            PopupDatePicker.Date = null; // Clear the date

            _searchDebounceTimer.Stop();
            await ApplyServerSidePopupFiltersAsync();
        }

        // Passes the UI inputs directly to the database for deep historical searching
        private async Task ApplyServerSidePopupFiltersAsync()
        {
            string searchTerm = PopupSearchBox.Text?.Trim() ?? "";
            string type = (PopupTypeFilter.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All Types";
            string status = (PopupStatusFilter.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All Statuses";

            // Extract the optional date filter
            DateTime? searchDate = null;
            if (PopupDatePicker.Date.HasValue)
            {
                searchDate = PopupDatePicker.Date.Value.DateTime;
            }

            try
            {
                // Queries the actual database, pulling up to 2,000 matches from ANY date
                var searchResults = await _database.GetMasterAuditLogsAsync(2000, searchTerm, type, status, searchDate);
                PopupLogsListView.ItemsSource = searchResults;
            }
            catch { /* Ignore brief DB locks during rapid typing */ }
        }

        private async void RegisterStaffButton_Click(object sender, RoutedEventArgs e)
        {
            string fullName = StaffNameTextBox.Text.Trim();
            string uid = StaffNfcUidTextBox.Text.Trim();
            string role = (StaffRoleComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Security Personnel";

            if (string.IsNullOrWhiteSpace(fullName) || string.IsNullOrWhiteSpace(uid))
            {
                StatusTextBlock.Text = "Staff Name and NFC UID are strictly required.";
                StatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 248, 113, 113));
                PlayErrorAlert();
                return;
            }

            // THE FIX (ITEM 9): Check for existing staff assignment to prevent accidental overwrites
            var existingStaff = await _database.GetStaffDetailsAsync(uid);
            if (existingStaff.Role != null)
            {
                ContentDialog overwriteDialog = new ContentDialog
                {
                    Title = "NFC Card Already in Use",
                    Content = $"This NFC card is currently registered to:\n\nName: {existingStaff.FullName}\nRole: {existingStaff.Role}\n\nDo you want to overwrite this assignment and register the card to {fullName}?",
                    PrimaryButtonText = "Yes, Overwrite",
                    CloseButtonText = "Cancel",
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = this.Content.XamlRoot
                };

                PlayErrorAlert(); // Alert the user to the conflict
                var dialogResult = await overwriteDialog.ShowAsync();

                if (dialogResult != ContentDialogResult.Primary)
                {
                    StatusTextBlock.Text = "Staff registration cancelled.";
                    StatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White);
                    return;
                }
            }

            // Proceed with registration / Master Admin check
            if (AppSession.CurrentStaffRoleLabel == "Master Admin")
            {
                await ExecuteStaffRegistration(uid, fullName, role, AppSession.CurrentStaffName);
            }
            else
            {
                _pendingStaffName = fullName;
                _pendingStaffUid = uid;
                _pendingStaffRole = role;
                _pendingAdminAction = "REGISTER_STAFF";
                _isAwaitingAdminAuth = true;

                AdminAuthDescriptionText.Text = "To prevent unauthorized account creation, an Administrator must verify this action.";
                AdminAuthDialog.XamlRoot = this.Content.XamlRoot;
                var result = await AdminAuthDialog.ShowAsync();

                if (result == ContentDialogResult.None && _isAwaitingAdminAuth)
                {
                    _isAwaitingAdminAuth = false;
                    _pendingAdminAction = "";
                    StatusTextBlock.Text = "Registration cancelled.";
                    StatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White);
                }
            }
        }

        private async Task ExecuteStaffRegistration(string uid, string fullName, string role, string authorizedBy)
        {
            try
            {
                await _database.RegisterStaffAsync(uid, fullName, role);
                await _database.AddAlertAsync(authorizedBy, "ADMIN_OVERRIDE", $"Authorized registration of new {role}: {fullName}");

                StaffNameTextBox.Text = "";
                StaffNfcUidTextBox.Text = "";
                StaffRoleComboBox.SelectedIndex = 0;

                StatusTextBlock.Text = $"Successfully registered {role}: {fullName}";
                StatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 52, 211, 153));

                await RefreshDashboardAsync();
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"Registration failed: {ex.Message}";
                StatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 248, 113, 113));
                PlayErrorAlert();
            }
        }

        private async Task ShowSyncResultDialog(string title, string message)
        {
            ContentDialog resultDialog = new ContentDialog
            {
                Title = title,
                Content = message,
                CloseButtonText = "OK",
                XamlRoot = this.Content.XamlRoot
            };
            await resultDialog.ShowAsync();
        }

        private async void UploadDataButton_Click(object sender, RoutedEventArgs e)
        {
            ContentDialog confirmDialog = new ContentDialog
            {
                Title = "Confirm Upload",
                Content = "This will push new local students, staff, courses, events, and logs to the cloud database. Continue?",
                PrimaryButtonText = "Yes, Upload",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.Content.XamlRoot
            };

            var confirmResult = await confirmDialog.ShowAsync();
            if (confirmResult != ContentDialogResult.Primary) return;

            if (AppSession.CurrentStaffRoleLabel == "Master Admin")
            {
                await ExecuteUploadDataAsync(AppSession.CurrentStaffName);
            }
            else
            {
                _pendingAdminAction = "UPLOAD_DATA";
                _isAwaitingAdminAuth = true;

                AdminAuthDescriptionText.Text = "To confirm this upload, an Administrator must verify by tapping their NFC card.";
                AdminAuthDialog.XamlRoot = this.Content.XamlRoot;
                var authResult = await AdminAuthDialog.ShowAsync();

                if (authResult == ContentDialogResult.None && _isAwaitingAdminAuth)
                {
                    _isAwaitingAdminAuth = false;
                    _pendingAdminAction = "";
                    StatusTextBlock.Text = "Upload cancelled.";
                    StatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White);
                }
            }
        }

        private async Task ExecuteUploadDataAsync(string authorizedBy)
        {
            UploadDataButton.IsEnabled = false;
            GetNewDataButton.IsEnabled = false;
            SyncProgressBar.Visibility = Visibility.Visible;
            StatusTextBlock.Text = "Uploading local records to cloud database...";
            StatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White);

            try
            {
                int pushedStudents = await _database.PushStudentsToCloudAsync();
                int pushedStaff = await _database.PushStaffToCloudAsync();
                int pushedCourses = await _database.PushCoursesToCloudAsync();
                int pushedEvents = await _database.PushEventsToCloudAsync();
                int pushedApproved = await _database.PushEventApprovedStudentsToCloudAsync();
                int pushedLogs = await _database.PushLogsToCloudAsync();
                int pushedEventLogs = await _database.PushEventAttendanceToCloudAsync();

                int totalPushed = pushedStudents + pushedStaff + pushedCourses + pushedEvents + pushedApproved + pushedLogs + pushedEventLogs;

                if (totalPushed > 0)
                {
                    var additions = new List<string>();
                    if (pushedStudents > 0) additions.Add($"{pushedStudents} Student(s)");
                    if (pushedStaff > 0) additions.Add($"{pushedStaff} Staff member(s)");
                    if (pushedCourses > 0) additions.Add($"{pushedCourses} Course(s)");
                    if (pushedEvents > 0) additions.Add($"{pushedEvents} Event(s)");
                    if (pushedApproved > 0) additions.Add($"{pushedApproved} Roster Entry(ies)");
                    if (pushedLogs > 0) additions.Add($"{pushedLogs} Gate Log(s)");
                    if (pushedEventLogs > 0) additions.Add($"{pushedEventLogs} Event Attendance Log(s)");

                    string formattedList = "• " + string.Join("\n• ", additions);
                    string message = $"Upload complete. The following new records were synced to the cloud:\n\n{formattedList}";

                    StatusTextBlock.Text = $"Upload complete. {totalPushed} records pushed.";
                    StatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 52, 211, 153));
                    PlaySuccessPing();

                    try
                    {
                        await _database.AddAlertAsync(authorizedBy, "ADMIN_ACTION", $"Authorized cloud data upload. {totalPushed} record(s) pushed.");
                    }
                    catch { }

                    await ShowSyncResultDialog("Upload Successful", message);
                }
                else
                {
                    StatusTextBlock.Text = "System is already up to date. No new local records to upload.";
                    StatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White);
                }
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"Upload failed: {ex.Message}";
                StatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 248, 113, 113));
                PlayErrorAlert();

                await ShowSyncResultDialog("Upload Failed", $"An error occurred while syncing to the cloud:\n\n{ex.Message}");
            }
            finally
            {
                UploadDataButton.IsEnabled = true;
                GetNewDataButton.IsEnabled = true;
                SyncProgressBar.Visibility = Visibility.Collapsed;
            }
        }

        private async void GetNewDataButton_Click(object sender, RoutedEventArgs e)
        {
            ContentDialog confirmDialog = new ContentDialog
            {
                Title = "Confirm Download",
                Content = "This will pull the latest students, staff, courses, events, and rosters from the cloud database into this local terminal. Continue?",
                PrimaryButtonText = "Yes, Download",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.Content.XamlRoot
            };

            var confirmResult = await confirmDialog.ShowAsync();
            if (confirmResult != ContentDialogResult.Primary) return;

            if (AppSession.CurrentStaffRoleLabel == "Master Admin")
            {
                await ExecuteDownloadDataAsync(AppSession.CurrentStaffName);
            }
            else
            {
                _pendingAdminAction = "DOWNLOAD_DATA";
                _isAwaitingAdminAuth = true;

                AdminAuthDescriptionText.Text = "To confirm this download, an Administrator must verify by tapping their NFC card.";
                AdminAuthDialog.XamlRoot = this.Content.XamlRoot;
                var authResult = await AdminAuthDialog.ShowAsync();

                if (authResult == ContentDialogResult.None && _isAwaitingAdminAuth)
                {
                    _isAwaitingAdminAuth = false;
                    _pendingAdminAction = "";
                    StatusTextBlock.Text = "Download cancelled.";
                    StatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White);
                }
            }
        }

        private async Task ExecuteDownloadDataAsync(string authorizedBy)
        {
            GetNewDataButton.IsEnabled = false;
            UploadDataButton.IsEnabled = false;
            SyncProgressBar.Visibility = Visibility.Visible;
            StatusTextBlock.Text = "Downloading latest records and logs from cloud database...";
            StatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White);

            try
            {
                int pulledStudents = await _database.PullStudentsFromCloudAsync();
                int pulledStaff = await _database.PullStaffFromCloudAsync();
                int pulledCourses = await _database.PullCoursesFromCloudAsync();
                int pulledEvents = await _database.PullEventsFromCloudAsync();
                int pulledApproved = await _database.PullEventApprovedStudentsFromCloudAsync();
                int pulledLogs = await _database.PullLogsFromCloudAsync();
                int pulledEventLogs = await _database.PullEventAttendanceFromCloudAsync();

                int totalPulled = pulledStudents + pulledStaff + pulledCourses + pulledEvents + pulledApproved + pulledLogs + pulledEventLogs;

                if (totalPulled > 0)
                {
                    var additions = new List<string>();
                    if (pulledStudents > 0) additions.Add($"{pulledStudents} Student(s)");
                    if (pulledStaff > 0) additions.Add($"{pulledStaff} Staff member(s)");
                    if (pulledCourses > 0) additions.Add($"{pulledCourses} Course(s)");
                    if (pulledEvents > 0) additions.Add($"{pulledEvents} Event(s)");
                    if (pulledApproved > 0) additions.Add($"{pulledApproved} Roster Entry(ies)");
                    if (pulledLogs > 0) additions.Add($"{pulledLogs} Gate Log(s)");
                    if (pulledEventLogs > 0) additions.Add($"{pulledEventLogs} Event Log(s)");

                    string formattedList = "• " + string.Join("\n• ", additions);
                    string message = $"Download complete. The following new updates were synced locally:\n\n{formattedList}";

                    StatusTextBlock.Text = $"Download complete. {totalPulled} records pulled.";
                    StatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 52, 211, 153));
                    PlaySuccessPing();
                    await RefreshDashboardAsync();

                    try
                    {
                        await _database.AddAlertAsync(authorizedBy, "ADMIN_ACTION", $"Authorized cloud data download. {totalPulled} record(s) pulled.");
                    }
                    catch { }

                    await ShowSyncResultDialog("Download Successful", message);
                }
                else
                {
                    StatusTextBlock.Text = "System is already up to date. No new cloud records found.";
                    StatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White);
                }
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"Download failed: {ex.Message}";
                StatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 248, 113, 113));
                PlayErrorAlert();

                await ShowSyncResultDialog("Download Failed", $"An error occurred while pulling from the cloud:\n\n{ex.Message}");
            }
            finally
            {
                GetNewDataButton.IsEnabled = true;
                UploadDataButton.IsEnabled = true;
                SyncProgressBar.Visibility = Visibility.Collapsed;
            }
        }



        private async void ResetPinButton_Click(object sender, RoutedEventArgs e)
        {
            string studentId = ResetStudentIdTextBox.Text.Trim();
            string pin = NewPinPasswordBox.Password.Trim();

            if (string.IsNullOrWhiteSpace(studentId) || pin.Length != 4 || !pin.All(char.IsDigit))
            {
                StatusTextBlock.Text = "Enter a student ID and a 4-digit PIN.";
                StatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 248, 113, 113));
                PlayErrorAlert();
                return;
            }

            try
            {
                await _database.ResetPinAsync(studentId, pin);

                NewPinPasswordBox.Password = "";
                ResetStudentIdTextBox.Text = "";
                StatusTextBlock.Text = $"New PIN set and lockout cleared for {studentId}.";
                StatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 52, 211, 153));
                PlaySuccessPing();

                try
                {
                    await _database.AddAlertAsync(AppSession.CurrentStaffName, "ADMIN_OVERRIDE", $"Manually unlocked account and reset PIN for {studentId}.");
                }
                catch { }

                await RefreshDashboardAsync();
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"Could not reset PIN: {ex.Message}";
                StatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 248, 113, 113));
                PlayErrorAlert();
            }
        }

        // ====================================================================
        // COURSE MANAGEMENT DIALOG LOGIC
        // ====================================================================
        private async void OpenManageCoursesDialog_Click(object sender, RoutedEventArgs e)
        {
            ManageCoursesDialog.XamlRoot = this.Content.XamlRoot;
            CourseDialogStatusText.Text = "Select an action below.";
            CourseDialogStatusText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 160, 160, 160));
            DialogNewCourseTextBox.Text = "";

            await LoadCoursesIntoDialogAsync();
            await ManageCoursesDialog.ShowAsync();
        }

        private async Task LoadCoursesIntoDialogAsync()
        {
            try
            {
                var courses = await _database.GetDistinctCoursesAsync();
                DialogDeleteCourseComboBox.ItemsSource = courses;
                DialogDeleteCourseComboBox.SelectedIndex = -1;
            }
            catch { }
        }

        private async void DialogAddCourseButton_Click(object sender, RoutedEventArgs e)
        {
            string courseName = DialogNewCourseTextBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(courseName))
            {
                CourseDialogStatusText.Text = "Please enter a valid course name.";
                CourseDialogStatusText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 248, 113, 113));
                PlayErrorAlert();
                return;
            }

            try
            {
                await _database.AddCourseAsync(courseName);

                CourseDialogStatusText.Text = $"Course '{courseName}' added successfully.";
                CourseDialogStatusText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 52, 211, 153));
                PlaySuccessPing();
                DialogNewCourseTextBox.Text = "";

                await LoadCoursesIntoDialogAsync();
                await RefreshDashboardAsync();
            }
            catch (Exception ex)
            {
                CourseDialogStatusText.Text = $"Could not add course: {ex.Message}";
                CourseDialogStatusText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 248, 113, 113));
                PlayErrorAlert();
            }
        }

        private async void DialogDeleteCourseButton_Click(object sender, RoutedEventArgs e)
        {
            string courseName = (DialogDeleteCourseComboBox.SelectedItem as string) ?? "";

            if (string.IsNullOrWhiteSpace(courseName))
            {
                CourseDialogStatusText.Text = "Please select a course to delete.";
                CourseDialogStatusText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 248, 113, 113));
                PlayErrorAlert();
                return;
            }

            ManageCoursesDialog.Hide();

            try
            {
                // 1. Safety Check: Are students using this course?
                int enrolledStudents = await _database.GetStudentCountByCourseAsync(courseName);

                if (enrolledStudents > 0)
                {
                    ContentDialog warningDialog = new ContentDialog
                    {
                        Title = "Action Blocked: Course in Use",
                        Content = $"You cannot delete '{courseName}' because there are currently {enrolledStudents} student(s) enrolled in it.\n\nPlease reassign these students to a different course before deleting.",
                        CloseButtonText = "Understood",
                        XamlRoot = this.Content.XamlRoot
                    };
                    PlayErrorAlert();
                    await warningDialog.ShowAsync();

                    await ManageCoursesDialog.ShowAsync();
                    return;
                }

                // 2. Confirmation Dialog
                ContentDialog confirmDialog = new ContentDialog
                {
                    Title = "Confirm Deletion",
                    Content = $"Are you absolutely sure you want to delete '{courseName}'? This action cannot be undone.",
                    PrimaryButtonText = "Delete Course",
                    CloseButtonText = "Cancel",
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = this.Content.XamlRoot
                };

                var result = await confirmDialog.ShowAsync();

                if (result == ContentDialogResult.Primary)
                {
                    // 3. Execute Deletion
                    await _database.DeleteCourseAsync(courseName);

                    CourseDialogStatusText.Text = $"Course '{courseName}' was successfully deleted.";
                    CourseDialogStatusText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 52, 211, 153));
                    PlaySuccessPing();

                    await LoadCoursesIntoDialogAsync();
                    await RefreshDashboardAsync();
                }

                await ManageCoursesDialog.ShowAsync();
            }
            catch (Exception ex)
            {
                CourseDialogStatusText.Text = $"Could not delete course: {ex.Message}";
                CourseDialogStatusText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 248, 113, 113));
                PlayErrorAlert();

                await ManageCoursesDialog.ShowAsync();
            }
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            CloseSerialPort();
            var dashboard = new MainWindow();
            dashboard.Activate();
            this.Close();
        }

        private async void ExportLogsButton_Click(object sender, RoutedEventArgs e)
        {
            StatusTextBlock.Text = "Gathering audit records for export...";
            StatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White);

            // THE FIX: Directly fetch a comprehensive batch of logs from the database 
            // rather than trying to read the UI lists.
            var rawLogs = await _database.GetMasterAuditLogsAsync(10000);

            if (rawLogs == null || !rawLogs.Any())
            {
                ContentDialog emptyDialog = new ContentDialog
                {
                    Title = "Nothing to Export",
                    Content = "There are no audit records in the database to export.",
                    CloseButtonText = "OK",
                    XamlRoot = this.Content.XamlRoot
                };
                await emptyDialog.ShowAsync();
                StatusTextBlock.Text = "System Ready: Waiting for input...";
                return;
            }

            // 1. Show the Export Configuration Dialog
            ExportConfigDialog.XamlRoot = this.Content.XamlRoot;
            var dialogResult = await ExportConfigDialog.ShowAsync();

            // If they click cancel, abort the export.
            if (dialogResult != ContentDialogResult.Primary)
            {
                StatusTextBlock.Text = "Export cancelled.";
                return;
            }

            // 2. Apply the selected Grouping / Sorting
            var logsToExport = rawLogs.ToList();
            string sortOption = (ExportSortComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";

            if (sortOption == "Group by Log Type")
            {
                logsToExport = logsToExport
                    .OrderBy(l => l.LogType)
                    .ThenByDescending(l => l.Timestamp)
                    .ToList();
            }
            else if (sortOption == "Sort alphabetically by Subject")
            {
                logsToExport = logsToExport
                    .OrderBy(l => l.Subject)
                    .ToList();
            }
            // If "Default (Time of Entry)", leave it in the default descending timestamp order.

            var picker = new Windows.Storage.Pickers.FileSavePicker();
            IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
            picker.FileTypeChoices.Add("Excel CSV Document", new List<string>() { ".csv" });
            picker.SuggestedFileName = $"Security_Audit_Log_{DateTime.Now:yyyyMMdd}";

            Windows.Storage.StorageFile file = await picker.PickSaveFileAsync();

            if (file != null)
            {
                StatusTextBlock.Text = "Writing custom audit log to CSV...";

                try
                {
                    var csvData = new System.Text.StringBuilder();

                    // 3. Build dynamic headers based on CheckBox selection
                    var headers = new List<string>();
                    if (ExportColTimestamp.IsChecked == true) headers.Add("Date & Time");
                    if (ExportColLogType.IsChecked == true) headers.Add("Log Type");
                    if (ExportColSubject.IsChecked == true) headers.Add("Subject");
                    if (ExportColAction.IsChecked == true) headers.Add("Action Taken");
                    if (ExportColStatus.IsChecked == true) headers.Add("Status");
                    if (ExportColDetails.IsChecked == true) headers.Add("Extended Details");

                    csvData.AppendLine(string.Join(",", headers));

                    // 4. Build dynamic rows based on CheckBox selection
                    foreach (var log in logsToExport)
                    {
                        var row = new List<string>();

                        if (ExportColTimestamp.IsChecked == true) row.Add($"\"{log.DisplayTime}\"");
                        if (ExportColLogType.IsChecked == true) row.Add($"\"{log.LogType}\"");
                        if (ExportColSubject.IsChecked == true) row.Add($"\"{log.Subject}\"");
                        if (ExportColAction.IsChecked == true) row.Add($"\"{log.Action}\"");
                        if (ExportColStatus.IsChecked == true) row.Add($"\"{log.Status}\"");
                        if (ExportColDetails.IsChecked == true) row.Add($"\"{log.Details.Replace("\"", "\"\"")}\""); // Escape quotes in details

                        csvData.AppendLine(string.Join(",", row));
                    }

                    Windows.Storage.CachedFileManager.DeferUpdates(file);
                    await Windows.Storage.FileIO.WriteTextAsync(file, csvData.ToString(), Windows.Storage.Streams.UnicodeEncoding.Utf8);
                    Windows.Storage.Provider.FileUpdateStatus status = await Windows.Storage.CachedFileManager.CompleteUpdatesAsync(file);

                    if (status == Windows.Storage.Provider.FileUpdateStatus.Complete)
                    {
                        StatusTextBlock.Text = $"Audit log successfully exported.";
                        StatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 52, 211, 153));
                        PlaySuccessPing();

                        ContentDialog successDialog = new ContentDialog
                        {
                            Title = "Export Complete",
                            Content = $"Your custom security audit was successfully exported and saved to:\n\n{file.Path}",
                            CloseButtonText = "OK",
                            XamlRoot = this.Content.XamlRoot
                        };
                        await successDialog.ShowAsync();
                    }
                }
                catch (Exception ex)
                {
                    StatusTextBlock.Text = $"Export failed: {ex.Message}";
                    StatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 248, 113, 113));
                    PlayErrorAlert();
                }
            }
            else
            {
                StatusTextBlock.Text = "Export cancelled.";
                StatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White);
            }
        }

        private async void ExportMetricsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var folderPicker = new Windows.Storage.Pickers.FolderPicker();
                folderPicker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Desktop;
                folderPicker.FileTypeFilter.Add("*");

                // Required WinUI 3 initialization for Pickers
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                WinRT.Interop.InitializeWithWindow.Initialize(folderPicker, hwnd);

                var folder = await folderPicker.PickSingleFolderAsync();
                if (folder != null)
                {
                    StatusTextBlock.Text = "Exporting system performance metrics to CSV...";
                    StatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White);

                    // This calls your original method that generates the 3 separate CSV files 
                    // containing the raw auth_speed_ms and db_query_speed_ms
                    await _database.ExportCleanLogsToCsvAsync(folder.Path);

                    StatusTextBlock.Text = $"Performance metrics successfully exported to {folder.Path}";
                    StatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 52, 211, 153));
                    PlaySuccessPing();
                }
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"Metrics export failed: {ex.Message}";
                StatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 248, 113, 113));
                PlayErrorAlert();
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