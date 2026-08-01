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

        public SecurityDashboardWindow()
        {
            this.InitializeComponent();
            MaximizeWindow();

            this.Closed += Window_Closed;
            ResetPinButton.Click += ResetPinButton_Click;

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

        private void TryConnectSerial(string portName)
        {
            try
            {
                _serialPort = new SerialPort(portName, 115200);
                _serialPort.NewLine = "\n";
                _serialPort.DataReceived += SerialPort_DataReceived;
                _serialPort.Open();
                StatusTextBlock.Text = $"Ready. NFC connected on {portName}";
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"NFC disconnected: {ex.Message}";
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
                                await ExecuteStaffRegistration(_pendingStaffUid, _pendingStaffName, _pendingStaffRole, details.FullName ?? "Admin");
                            }
                            else
                            {
                                _isAwaitingAdminAuth = false;
                                AdminAuthDialog.Hide();
                                StatusTextBlock.Text = "Authorization Denied: Tapped card is not an Administrator.";
                                StatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 248, 113, 113));
                            }
                        }
                        else
                        {
                            StaffNfcUidTextBox.Text = uid;
                            StatusTextBlock.Text = "Card scanned. Ready to register staff.";
                            StatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White);
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

            _masterLogsCache = (await _database.GetMasterAuditLogsAsync(2000)).ToList();

            ApplyPopupFilters();
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

        private void PopupFilter_Changed(object sender, RoutedEventArgs e) => ApplyPopupFilters();

        private void PopupClear_Click(object sender, RoutedEventArgs e)
        {
            PopupSearchBox.Text = "";
            PopupTypeFilter.SelectedIndex = 0;
            PopupStatusFilter.SelectedIndex = 0;
            ApplyPopupFilters();
        }

        private void ApplyPopupFilters()
        {
            if (_masterLogsCache == null || PopupLogsListView == null) return;

            var filtered = _masterLogsCache.AsEnumerable();

            string query = PopupSearchBox.Text?.Trim().ToLower() ?? "";
            if (!string.IsNullOrEmpty(query))
            {
                filtered = filtered.Where(l =>
                    (l.Subject != null && l.Subject.ToLower().Contains(query)) ||
                    (l.Details != null && l.Details.ToLower().Contains(query)));
            }

            string type = (PopupTypeFilter.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All Types";
            if (type != "All Types") filtered = filtered.Where(l => l.LogType == type);

            string status = (PopupStatusFilter.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All Statuses";
            if (status == "Granted / Resolved") filtered = filtered.Where(l => l.Status == "GRANTED" || l.Status == "RESOLVED");
            else if (status == "Denied / Flagged") filtered = filtered.Where(l => l.Status == "DENIED" || l.Status == "FLAGGED");

            PopupLogsListView.ItemsSource = filtered.ToList();
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
                return;
            }

            if (AppSession.CurrentStaffRoleLabel == "Master Admin")
            {
                await ExecuteStaffRegistration(uid, fullName, role, AppSession.CurrentStaffName);
            }
            else
            {
                _pendingStaffName = fullName;
                _pendingStaffUid = uid;
                _pendingStaffRole = role;
                _isAwaitingAdminAuth = true;

                AdminAuthDialog.XamlRoot = this.Content.XamlRoot;
                var result = await AdminAuthDialog.ShowAsync();

                if (result == ContentDialogResult.None && _isAwaitingAdminAuth)
                {
                    _isAwaitingAdminAuth = false;
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
            }
        }

        // ====================================================================
        // SYNC DIALOG HELPER
        // ====================================================================
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

        // ====================================================================
        // UPLOAD DATA (NOTIFIES ONLY IF NEW ADDITIONS/UPDATES EXIST)
        // ====================================================================
        private async void UploadDataButton_Click(object sender, RoutedEventArgs e)
        {
            UploadDataButton.IsEnabled = false;
            GetNewDataButton.IsEnabled = false;
            SyncProgressBar.Visibility = Visibility.Visible;
            StatusTextBlock.Text = "Uploading local records to cloud database...";

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

                await ShowSyncResultDialog("Upload Failed", $"An error occurred while syncing to the cloud:\n\n{ex.Message}");
            }
            finally
            {
                UploadDataButton.IsEnabled = true;
                GetNewDataButton.IsEnabled = true;
                SyncProgressBar.Visibility = Visibility.Collapsed;
            }
        }

        // ====================================================================
        // DOWNLOAD DATA (NOTIFIES ONLY IF NEW ADDITIONS EXIST)
        // ====================================================================
        private async void GetNewDataButton_Click(object sender, RoutedEventArgs e)
        {
            GetNewDataButton.IsEnabled = false;
            UploadDataButton.IsEnabled = false;
            SyncProgressBar.Visibility = Visibility.Visible;
            StatusTextBlock.Text = "Downloading latest records from cloud database...";

            try
            {
                int pulledStudents = await _database.PullStudentsFromCloudAsync();
                int pulledStaff = await _database.PullStaffFromCloudAsync();
                int pulledCourses = await _database.PullCoursesFromCloudAsync();
                int pulledEvents = await _database.PullEventsFromCloudAsync();
                int pulledApproved = await _database.PullEventApprovedStudentsFromCloudAsync();

                int totalPulled = pulledStudents + pulledStaff + pulledCourses + pulledEvents + pulledApproved;

                if (totalPulled > 0)
                {
                    var additions = new List<string>();
                    if (pulledStudents > 0) additions.Add($"{pulledStudents} Student(s)");
                    if (pulledStaff > 0) additions.Add($"{pulledStaff} Staff member(s)");
                    if (pulledCourses > 0) additions.Add($"{pulledCourses} Course(s)");
                    if (pulledEvents > 0) additions.Add($"{pulledEvents} Event(s)");
                    if (pulledApproved > 0) additions.Add($"{pulledApproved} Roster Entry(ies)");

                    string formattedList = "• " + string.Join("\n• ", additions);
                    string message = $"Download complete. The following new updates were synced locally:\n\n{formattedList}";

                    StatusTextBlock.Text = $"Download complete. {totalPulled} records pulled.";
                    StatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 52, 211, 153));
                    PlaySuccessPing();
                    await RefreshDashboardAsync();

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
                return;
            }

            try
            {
                await _database.ResetPinAsync(studentId, pin);

                NewPinPasswordBox.Password = "";
                ResetStudentIdTextBox.Text = "";
                StatusTextBlock.Text = $"New PIN set and lockout cleared for {studentId}.";
                StatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 52, 211, 153));

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
            }
        }

        private async void AddCourseButton_Click(object sender, RoutedEventArgs e)
        {
            string courseName = NewCourseTextBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(courseName))
            {
                StatusTextBlock.Text = "Please enter a valid course name.";
                StatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 248, 113, 113));
                return;
            }

            try
            {
                await _database.AddCourseAsync(courseName);

                NewCourseTextBox.Text = "";
                StatusTextBlock.Text = $"Course '{courseName}' added successfully.";
                StatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 52, 211, 153));

                ContentDialog successDialog = new ContentDialog
                {
                    Title = "Course Created Successfully",
                    Content = $"The academic course '{courseName}' has been added to the database.\n\nIt will now appear in the dropdown menu on the Student Registration window.",
                    CloseButtonText = "OK",
                    XamlRoot = this.Content.XamlRoot
                };

                await _database.AddAlertAsync(AppSession.CurrentStaffName, "ADMIN_ACTION", $"Added new academic course to database: {courseName}");
                await successDialog.ShowAsync();
                await RefreshDashboardAsync();
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"Could not add course: {ex.Message}";
                StatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 248, 113, 113));

                ContentDialog errorDialog = new ContentDialog { Title = "Database Error", Content = $"Failed to add the course.\n\nDetails: {ex.Message}", CloseButtonText = "OK", XamlRoot = this.Content.XamlRoot };
                await errorDialog.ShowAsync();
            }
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            CloseSerialPort();
            var dashboard = new MainWindow();
            dashboard.Activate();
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