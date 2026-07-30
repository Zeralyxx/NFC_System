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
using MySqlConnector;

namespace NFC_System
{
    public sealed partial class StudentManagementWindow : Window
    {
        private enum AdminActionType { None, SaveIndividual, DeleteIndividual, BatchUpdate }

        private readonly DatabaseService _database = new();
        private List<StudentRecord> _allStudents = new();
        private int _currentPage = 1;
        private const int PageSize = 10;

        // Serial Port objects to listen for the Admin Tap
        private SerialPort? _serialPort;
        private bool _isAwaitingAdminAuth = false;
        private AdminActionType _pendingAction = AdminActionType.None;

        public StudentManagementWindow()
        {
            this.InitializeComponent();
            MaximizeWindow();
            this.Closed += Window_Closed;
            _ = LoadDataAsync();
        }

        private async Task LoadDataAsync()
        {
            try
            {
                var result = await _database.SearchStudentsAsync("", "All Students", "All Courses", "All Years", 1, 99999);
                _allStudents = result.Students.ToList();

                var courses = _allStudents.Select(s => s.Course).Where(c => !string.IsNullOrWhiteSpace(c)).Distinct().OrderBy(c => c).ToList();

                CourseFilterComboBox.Items.Clear();
                CourseFilterComboBox.Items.Add("All Courses");
                PopupCourseFilter.Items.Clear();
                PopupCourseFilter.Items.Add("All Courses");

                foreach (var course in courses)
                {
                    CourseFilterComboBox.Items.Add(course);
                    PopupCourseFilter.Items.Add(course);
                }

                CourseFilterComboBox.SelectedIndex = 0;
                PopupCourseFilter.SelectedIndex = 0;

                BatchCourseComboBox.Items.Clear();
                BatchCourseComboBox.Items.Add("All Courses");
                foreach (var course in courses) BatchCourseComboBox.Items.Add(course);
                BatchCourseComboBox.SelectedIndex = 0;

                // Hook up the hardware reader so we can intercept the auth tap
                string nfcPort = await _database.GetSettingAsync("nfc_com_port", "COM3");
                TryConnectSerial(nfcPort);

                RefreshDataGrid();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DB ERROR] {ex.Message}");
            }
        }

        // ====================================================================
        // THE FIX: NATIVE HARDWARE SUCCESS CHIME
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
                        // A highly satisfying, rapid ascending major chord (C6 -> E6 -> G6)
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

        // ====================================================================
        // SERIAL PORT RBAC LISTENER LOGIC
        // ====================================================================
        private void TryConnectSerial(string portName)
        {
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

                if (line.StartsWith("UID=") && _isAwaitingAdminAuth)
                {
                    string uid = line.Substring(4).Trim();

                    DispatcherQueue.TryEnqueue(async () =>
                    {
                        var details = await _database.GetStaffDetailsAsync(uid);

                        if (details.Role == "Administrator" || details.Role == "Master Administrator")
                        {
                            _isAwaitingAdminAuth = false;
                            AdminAuthDialog.Hide();
                            await ExecutePendingAdminAction(details.FullName ?? "Admin");
                        }
                        else
                        {
                            AuthStatusText.Text = "Authorization Denied: Tapped card is not an Administrator.";
                            AuthStatusText.Visibility = Visibility.Visible;
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

        // ====================================================================
        // EXECUTING THE LOCKED ADMIN ACTIONS
        // ====================================================================
        private async Task ExecutePendingAdminAction(string adminName)
        {
            try
            {
                if (_pendingAction == AdminActionType.SaveIndividual)
                {
                    if (StudentListView.SelectedItem is StudentRecord selected)
                    {
                        string oldId = selected.StudentId;
                        string newId = EditStudentIdBox.Text.Trim();
                        string newStatus = ((ComboBoxItem)EditStatusComboBox.SelectedItem).Content.ToString() ?? "Active";

                        using var connection = new MySqlConnection(DatabaseService.ConnectionString);
                        await connection.OpenAsync();

                        using var cmd = new MySqlCommand("UPDATE students SET student_id = @newId, status = @status WHERE student_id = @oldId", connection);
                        cmd.Parameters.AddWithValue("@newId", newId);
                        cmd.Parameters.AddWithValue("@status", newStatus);
                        cmd.Parameters.AddWithValue("@oldId", oldId);
                        await cmd.ExecuteNonQueryAsync();

                        await _database.AddAlertAsync(adminName, "ADMIN_OVERRIDE", $"Updated profile for {selected.FullName}. ID changed to '{newId}', Status changed to '{newStatus}'.");
                    }
                }
                else if (_pendingAction == AdminActionType.DeleteIndividual)
                {
                    if (StudentListView.SelectedItem is StudentRecord selected)
                    {
                        using var connection = new MySqlConnection(DatabaseService.ConnectionString);
                        await connection.OpenAsync();

                        using var cmd = new MySqlCommand("DELETE FROM students WHERE student_id = @id", connection);
                        cmd.Parameters.AddWithValue("@id", selected.StudentId);
                        await cmd.ExecuteNonQueryAsync();

                        await _database.AddAlertAsync(adminName, "ADMIN_OVERRIDE", $"Deleted profile and purged credentials for {selected.FullName} ({selected.StudentId}).");
                    }
                }
                else if (_pendingAction == AdminActionType.BatchUpdate)
                {
                    string course = BatchCourseComboBox.SelectedItem?.ToString() ?? "All Courses";
                    string year = (BatchYearComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All Years";
                    string status = (BatchNewStatusComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Active";

                    int affectedRows = await _database.BatchUpdateStudentStatusAsync(course, year, status);

                    // Add the specific master admin name to the batch log
                    await _database.AddAlertAsync(adminName, "ADMIN_OVERRIDE", $"Batch updated {affectedRows} students to '{status}' (Course: {course}, Year: {year}).");
                }

                // Play the success sound upon successful execution
                PlaySuccessPing();

                // Clean up and refresh
                _pendingAction = AdminActionType.None;
                await LoadDataAsync(); // Fetch latest DB state
            }
            catch (Exception ex)
            {
                ContentDialog errorDialog = new ContentDialog
                {
                    Title = "Action Failed",
                    Content = $"The database rejected the change.\n\nDetails: {ex.Message}",
                    CloseButtonText = "OK",
                    XamlRoot = this.Content.XamlRoot
                };
                await errorDialog.ShowAsync();
            }
        }

        // ====================================================================
        // UI BINDINGS & CLICKS
        // ====================================================================

        private void RefreshDataGrid()
        {
            if (StudentListView == null) return;

            string searchTerm = SearchBox.Text.ToLower();
            string statusFilter = StatusFilterComboBox.SelectedItem is ComboBoxItem item ? item.Content.ToString() : "All Students";
            string courseFilter = CourseFilterComboBox.SelectedItem?.ToString() ?? "All Courses";

            var filteredData = _allStudents.Where(s =>
                (string.IsNullOrEmpty(searchTerm) || s.FullName.ToLower().Contains(searchTerm) || s.StudentId.Contains(searchTerm)) &&
                (statusFilter == "All Students" ||
                 (statusFilter == "Active Only" && s.Status == "Active") ||
                 (statusFilter == "Locked Out" && s.PinLocked) ||
                 (statusFilter == "Inactive" && s.Status == "Inactive")) &&
                (courseFilter == "All Courses" || s.Course == courseFilter)
            ).ToList();

            int totalItems = filteredData.Count;
            int totalPages = (int)Math.Ceiling(totalItems / (double)PageSize);
            _currentPage = Math.Clamp(_currentPage, 1, totalPages == 0 ? 1 : totalPages);

            var pagedData = filteredData.Skip((_currentPage - 1) * PageSize).Take(PageSize).ToList();

            StudentListView.ItemsSource = pagedData;
            PageInfoText.Text = $"Page {_currentPage} of {totalPages}";
            PrevBtn.IsEnabled = _currentPage > 1;
            NextBtn.IsEnabled = _currentPage < totalPages;
        }

        private void PageChange_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Name == "PrevBtn") _currentPage--;
            else _currentPage++;

            RefreshDataGrid();
        }

        private void StudentListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (StudentListView == null) return;

            if (StudentListView.SelectedItem is StudentRecord selectedStudent)
            {
                EditStudentIdBox.Text = selectedStudent.StudentId;

                EditStatusComboBox.SelectedIndex = selectedStudent.Status switch
                {
                    "Active" => 0,
                    "Inactive" => 1,
                    "Graduated" => 2,
                    _ => 3
                };

                EditStudentIdBox.IsEnabled = true;
                EditStatusComboBox.IsEnabled = true;
                SaveChangesButton.IsEnabled = true;
                DeleteStudentButton.IsEnabled = true;
            }
            else
            {
                EditStudentIdBox.Text = "";
                EditStudentIdBox.IsEnabled = false;
                EditStatusComboBox.IsEnabled = false;
                SaveChangesButton.IsEnabled = false;
                DeleteStudentButton.IsEnabled = false;
            }
        }

        private async void SaveChangesButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(EditStudentIdBox.Text)) return;

            _pendingAction = AdminActionType.SaveIndividual;

            // Master Admins bypass the Sudo prompt entirely
            if (AppSession.CurrentStaffRoleLabel == "Master Admin")
            {
                await ExecutePendingAdminAction(AppSession.CurrentStaffName);
            }
            else
            {
                _isAwaitingAdminAuth = true;
                AuthStatusText.Visibility = Visibility.Collapsed;
                AdminAuthDialog.XamlRoot = this.Content.XamlRoot;
                await AdminAuthDialog.ShowAsync();
            }
        }

        private async void DeleteStudentButton_Click(object sender, RoutedEventArgs e)
        {
            _pendingAction = AdminActionType.DeleteIndividual;

            // Master Admins bypass the Sudo prompt entirely
            if (AppSession.CurrentStaffRoleLabel == "Master Admin")
            {
                await ExecutePendingAdminAction(AppSession.CurrentStaffName);
            }
            else
            {
                _isAwaitingAdminAuth = true;
                AuthStatusText.Visibility = Visibility.Collapsed;
                AdminAuthDialog.XamlRoot = this.Content.XamlRoot;
                await AdminAuthDialog.ShowAsync();
            }
        }

        private async void ConfirmBatchButton_Click(object sender, RoutedEventArgs e)
        {
            BatchUpdateDialog.Hide(); // Hide the config menu before popping the Sudo menu

            _pendingAction = AdminActionType.BatchUpdate;

            // Master Admins bypass the Sudo prompt entirely
            if (AppSession.CurrentStaffRoleLabel == "Master Admin")
            {
                await ExecutePendingAdminAction(AppSession.CurrentStaffName);
            }
            else
            {
                _isAwaitingAdminAuth = true;
                AuthStatusText.Visibility = Visibility.Collapsed;
                AdminAuthDialog.XamlRoot = this.Content.XamlRoot;
                await AdminAuthDialog.ShowAsync();
            }
        }

        private async void OpenBatchDialogButton_Click(object sender, RoutedEventArgs e)
        {
            BatchUpdateDialog.XamlRoot = this.Content.XamlRoot;
            await BatchUpdateDialog.ShowAsync();
        }

        // --- ENLARGE POPUP DIRECTORY LOGIC ---
        private async void OpenPopupDirectoryButton_Click(object sender, RoutedEventArgs e)
        {
            MasterDirectoryDialog.XamlRoot = this.Content.XamlRoot;
            DialogDirectoryContainer.Width = 900;
            PopupExpandToggle.IsChecked = false;
            PopupExpandToggle.Content = "⛶ Expand View";

            ApplyPopupFilters();
            await MasterDirectoryDialog.ShowAsync();
        }

        private void PopupExpandToggle_Click(object sender, RoutedEventArgs e)
        {
            if (PopupExpandToggle.IsChecked == true)
            {
                DialogDirectoryContainer.Width = 1400;
                PopupExpandToggle.Content = "⮌ Collapse View";
            }
            else
            {
                DialogDirectoryContainer.Width = 900;
                PopupExpandToggle.Content = "⛶ Expand View";
            }
        }

        private void PopupFilter_Changed(object sender, RoutedEventArgs e) => ApplyPopupFilters();

        private void PopupClear_Click(object sender, RoutedEventArgs e)
        {
            PopupSearchBox.Text = "";
            PopupCourseFilter.SelectedIndex = 0;
            PopupStatusFilter.SelectedIndex = 0;
            ApplyPopupFilters();
        }

        private void ApplyPopupFilters()
        {
            if (_allStudents == null || PopupStudentListView == null) return;

            var filtered = _allStudents.AsEnumerable();

            string query = PopupSearchBox.Text?.Trim().ToLower() ?? "";
            if (!string.IsNullOrEmpty(query))
            {
                filtered = filtered.Where(s =>
                    (s.FullName != null && s.FullName.ToLower().Contains(query)) ||
                    (s.StudentId != null && s.StudentId.ToLower().Contains(query)) ||
                    (s.NfcUid != null && s.NfcUid.ToLower().Contains(query)));
            }

            string course = PopupCourseFilter.SelectedItem?.ToString() ?? "All Courses";
            if (course != "All Courses") filtered = filtered.Where(s => s.Course == course);

            string status = (PopupStatusFilter.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All Students";
            if (status == "Active Only") filtered = filtered.Where(s => s.Status == "Active");
            else if (status == "Locked Out") filtered = filtered.Where(s => s.PinLocked);
            else if (status == "Inactive") filtered = filtered.Where(s => s.Status == "Inactive");

            filtered = filtered.OrderBy(s => s.FullName);
            PopupStudentListView.ItemsSource = filtered.ToList();
        }

        private void MaximizeWindow()
        {
            IntPtr hWnd = WindowNative.GetWindowHandle(this);
            WindowId windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
            AppWindow appWindow = AppWindow.GetFromWindowId(windowId);
            if (appWindow.Presenter is OverlappedPresenter presenter) presenter.Maximize();
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            CloseSerialPort();
            new MainWindow().Activate();
            this.Close();
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _currentPage = 1;
            RefreshDataGrid();
        }

        private void Filter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (StudentListView == null) return;
            _currentPage = 1;
            RefreshDataGrid();
        }
    }
}