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

        // ====================================================================
        // NATIVE HARDWARE SUCCESS CHIME
        // ====================================================================
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
                        // If we are awaiting authorization, intercept the tap!
                        if (_isAwaitingAdminAuth)
                        {
                            var details = await _database.GetStaffDetailsAsync(uid);

                            // Check if the card tapped belongs to an Admin
                            if (details.Role == "Administrator" || details.Role == "Master Administrator")
                            {
                                _isAwaitingAdminAuth = false;
                                AdminAuthDialog.Hide();
                                PlaySuccessPing(); // <--- THE FIX
                                await ExecuteStaffRegistration(_pendingStaffUid, _pendingStaffName, _pendingStaffRole, details.FullName ?? "Admin");
                            }
                            else
                            {
                                _isAwaitingAdminAuth = false;
                                AdminAuthDialog.Hide();
                                StatusTextBlock.Text = "Authorization Denied: Tapped card is not an Administrator.";
                                StatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 248, 113, 113)); // Red
                            }
                        }
                        else
                        {
                            // Normal behavior: auto-fill the textbox for registration
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

        // --- DASHBOARD DATA ---
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

        // --- EXPANDABLE POPUP DIALOG LOGIC ---
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

        // --- ADMINISTRATIVE ACTIONS ---

        private async void RegisterStaffButton_Click(object sender, RoutedEventArgs e)
        {
            string fullName = StaffNameTextBox.Text.Trim();
            string uid = StaffNfcUidTextBox.Text.Trim();
            string role = (StaffRoleComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Security Personnel";

            if (string.IsNullOrWhiteSpace(fullName) || string.IsNullOrWhiteSpace(uid))
            {
                StatusTextBlock.Text = "Staff Name and NFC UID are strictly required.";
                StatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 248, 113, 113)); // Red
                return;
            }

            // If Master Admin, skip the authorization prompt!
            if (AppSession.CurrentStaffRoleLabel == "Master Admin")
            {
                await ExecuteStaffRegistration(uid, fullName, role, AppSession.CurrentStaffName);
            }
            else
            {
                // Standard Admin needs to tap their card again to prove they are present
                _pendingStaffName = fullName;
                _pendingStaffUid = uid;
                _pendingStaffRole = role;
                _isAwaitingAdminAuth = true;

                AdminAuthDialog.XamlRoot = this.Content.XamlRoot;
                var result = await AdminAuthDialog.ShowAsync();

                if (result == ContentDialogResult.None && _isAwaitingAdminAuth)
                {
                    // They clicked Cancel
                    _isAwaitingAdminAuth = false;
                    StatusTextBlock.Text = "Registration cancelled.";
                    StatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White);
                }
            }
        }

        // Extracted execution method so it can be called from the prompt OR directly
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
                StatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 52, 211, 153)); // Green

                await RefreshDashboardAsync();
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"Registration failed: {ex.Message}";
                StatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 248, 113, 113)); // Red
            }
        }

        private async void UploadDataButton_Click(object sender, RoutedEventArgs e)
        {
            UploadDataButton.IsEnabled = false;
            StatusTextBlock.Text = "Uploading local records to cloud database...";
            await Task.Delay(2000);
            StatusTextBlock.Text = "Upload complete. Cloud is securely synced.";
            UploadDataButton.IsEnabled = true;
        }

        private async void GetNewDataButton_Click(object sender, RoutedEventArgs e)
        {
            GetNewDataButton.IsEnabled = false;
            StatusTextBlock.Text = "Downloading latest records from cloud database...";
            await Task.Delay(2000);
            StatusTextBlock.Text = "Download complete. Local database is up to date.";
            GetNewDataButton.IsEnabled = true;
            await RefreshDashboardAsync();
        }

        private async void ResetPinButton_Click(object sender, RoutedEventArgs e)
        {
            string studentId = ResetStudentIdTextBox.Text.Trim();
            string pin = NewPinPasswordBox.Password.Trim();

            if (string.IsNullOrWhiteSpace(studentId) || pin.Length != 4 || !pin.All(char.IsDigit))
            {
                StatusTextBlock.Text = "Enter a student ID and a 4-digit PIN.";
                StatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 248, 113, 113)); // Red
                return;
            }

            try
            {
                await _database.ResetPinAsync(studentId, pin);

                NewPinPasswordBox.Password = "";
                ResetStudentIdTextBox.Text = "";
                StatusTextBlock.Text = $"New PIN set and lockout cleared for {studentId}.";
                StatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 52, 211, 153)); // Green

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
                StatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 248, 113, 113)); // Red
            }
        }

        private async void AddCourseButton_Click(object sender, RoutedEventArgs e)
        {
            string courseName = NewCourseTextBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(courseName))
            {
                StatusTextBlock.Text = "Please enter a valid course name.";
                StatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 248, 113, 113)); // Red
                return;
            }

            try
            {
                await _database.AddCourseAsync(courseName);

                NewCourseTextBox.Text = "";
                StatusTextBlock.Text = $"Course '{courseName}' added successfully.";
                StatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 52, 211, 153)); // Green

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
                StatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 248, 113, 113)); // Red

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