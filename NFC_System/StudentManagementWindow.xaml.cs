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
using MySqlConnector;

namespace NFC_System
{
    public sealed partial class StudentManagementWindow : Window
    {
        private readonly DatabaseService _database = new();
        private List<StudentRecord> _allStudents = new();
        private int _currentPage = 1;
        private const int PageSize = 10;

        public StudentManagementWindow()
        {
            this.InitializeComponent();
            MaximizeWindow();
            _ = LoadDataAsync();
        }

        private async Task LoadDataAsync()
        {
            try
            {
                using var connection = new MySqlConnection(DatabaseService.ConnectionString);
                await connection.OpenAsync();

                string sql = "SELECT * FROM students";
                using var command = new MySqlCommand(sql, connection);
                using var reader = await command.ExecuteReaderAsync();

                var students = new List<StudentRecord>();
                while (await reader.ReadAsync())
                {
                    students.Add(new StudentRecord
                    {
                        StudentId = reader["student_id"].ToString() ?? "",
                        FullName = reader["full_name"].ToString() ?? "",
                        Course = reader["course"].ToString() ?? "",
                        Status = reader["status"].ToString() ?? "Active",
                        NfcUid = reader["nfc_uid"].ToString() ?? "",
                        PinLocked = reader["pin_locked"] != DBNull.Value && Convert.ToBoolean(reader["pin_locked"]),
                        FailedPinAttempts = reader["failed_pin_attempts"] != DBNull.Value ? Convert.ToInt32(reader["failed_pin_attempts"]) : 0
                    });
                }

                _allStudents = students;

                // Dynamically populate the Course Filter based on existing data
                var courses = _allStudents.Select(s => s.Course).Where(c => !string.IsNullOrWhiteSpace(c)).Distinct().OrderBy(c => c).ToList();
                CourseFilterComboBox.Items.Clear();
                CourseFilterComboBox.Items.Add("All Courses");
                foreach (var course in courses)
                {
                    CourseFilterComboBox.Items.Add(course);
                }
                CourseFilterComboBox.SelectedIndex = 0;

                RefreshDataGrid();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DB ERROR] {ex.Message}");
            }
        }

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

            // Paging Math
            int totalItems = filteredData.Count;
            int totalPages = (int)Math.Ceiling(totalItems / (double)PageSize);

            _currentPage = Math.Clamp(_currentPage, 1, totalPages == 0 ? 1 : totalPages);

            var pagedData = filteredData.Skip((_currentPage - 1) * PageSize).Take(PageSize).ToList();

            StudentListView.ItemsSource = pagedData;

            // Update UI Pagination Controls
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
                EditNameText.Text = selectedStudent.FullName;
                EditIdText.Text = selectedStudent.StudentId;
                EditUidText.Text = selectedStudent.NfcUid;

                EditStatusComboBox.SelectedIndex = selectedStudent.Status switch
                {
                    "Active" => 0,
                    "Inactive" => 1,
                    _ => 2
                };

                if (selectedStudent.PinLocked)
                {
                    LockoutStatusText.Text = "ACCOUNT LOCKED (Too many PIN failures)";
                    LockoutStatusText.Foreground = new SolidColorBrush(Colors.DarkOrange);
                    UnlockAccountButton.IsEnabled = true;
                }
                else
                {
                    LockoutStatusText.Text = "Account is secure (No active flags)";
                    LockoutStatusText.Foreground = new SolidColorBrush(Colors.ForestGreen);
                    UnlockAccountButton.IsEnabled = false;
                }

                EditStatusComboBox.IsEnabled = true;
                ResetPinBox.IsEnabled = true;
                ResetPinBox.Password = "";
                SaveChangesButton.IsEnabled = true;
            }
            else
            {
                EditNameText.Text = "-";
                EditIdText.Text = "-";
                EditUidText.Text = "-";
                LockoutStatusText.Text = "Select a student";
                LockoutStatusText.Foreground = new SolidColorBrush(Colors.Gray);
                EditStatusComboBox.IsEnabled = false;
                UnlockAccountButton.IsEnabled = false;
                ResetPinBox.IsEnabled = false;
                SaveChangesButton.IsEnabled = false;
            }
        }

        private async void UnlockAccountButton_Click(object sender, RoutedEventArgs e)
        {
            if (StudentListView.SelectedItem is StudentRecord selected)
            {
                await _database.UpdatePinFailureAsync(selected.StudentId, 0, false);
                selected.PinLocked = false;
                selected.FailedPinAttempts = 0;
                StudentListView_SelectionChanged(null, null); // Refresh right panel
                RefreshDataGrid(); // Refresh list visual
            }
        }

        private async void SaveChangesButton_Click(object sender, RoutedEventArgs e)
        {
            if (StudentListView.SelectedItem is StudentRecord selected)
            {
                selected.Status = ((ComboBoxItem)EditStatusComboBox.SelectedItem).Content.ToString();
                string newPin = ResetPinBox.Password;

                try
                {
                    if (!string.IsNullOrWhiteSpace(newPin))
                    {
                        await _database.ResetPinAsync(selected.StudentId, newPin);
                    }

                    // We pass null for PIN here because SaveStudentAsync will ignore the pin if it's null,
                    // and we already updated the PIN directly above if needed.
                    await _database.SaveStudentAsync(selected, null);

                    SaveChangesButton.Content = "Saved Successfully!";
                    RefreshDataGrid(); // Sync list view
                    await Task.Delay(2000);
                    SaveChangesButton.Content = "Save Security Changes";
                    ResetPinBox.Password = "";
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error Saving: {ex.Message}");
                }
            }
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
            new MainWindow().Activate();
            this.Close();
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _currentPage = 1; // Reset to page 1 on new search
            RefreshDataGrid();
        }

        private void Filter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (StudentListView == null) return;
            _currentPage = 1; // Reset to page 1 on new filter
            RefreshDataGrid();
        }
    }
}