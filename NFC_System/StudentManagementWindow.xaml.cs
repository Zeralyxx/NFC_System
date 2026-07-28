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
                // Fetch the full list dynamically
                var result = await _database.SearchStudentsAsync("", "All Students", "All Courses", "All Years", 1, 99999);
                _allStudents = result.Students.ToList();

                // Dynamically populate the Course Filters based on existing data
                var courses = _allStudents.Select(s => s.Course).Where(c => !string.IsNullOrWhiteSpace(c)).Distinct().OrderBy(c => c).ToList();

                CourseFilterComboBox.Items.Clear();
                CourseFilterComboBox.Items.Add("All Courses");
                foreach (var course in courses) CourseFilterComboBox.Items.Add(course);
                CourseFilterComboBox.SelectedIndex = 0;

                // Populate the Batch Dialog course dropdown as well
                BatchCourseComboBox.Items.Clear();
                BatchCourseComboBox.Items.Add("All Courses");
                foreach (var course in courses) BatchCourseComboBox.Items.Add(course);
                BatchCourseComboBox.SelectedIndex = 0;

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
                // Maps exactly to: Active (0), Inactive (1), Graduated (2), Expelled (3)
                EditStatusComboBox.SelectedIndex = selectedStudent.Status switch
                {
                    "Active" => 0,
                    "Inactive" => 1,
                    "Graduated" => 2,
                    _ => 3
                };

                if (selectedStudent.PinLocked)
                {
                    LockoutStatusText.Text = $"LOCKED: {selectedStudent.FullName}";
                    LockoutStatusText.Foreground = new SolidColorBrush(Colors.DarkOrange);
                    UnlockAccountButton.IsEnabled = true;
                }
                else
                {
                    LockoutStatusText.Text = $"Secure: {selectedStudent.FullName}";
                    LockoutStatusText.Foreground = new SolidColorBrush(Colors.ForestGreen);
                    UnlockAccountButton.IsEnabled = false;
                }

                EditStatusComboBox.IsEnabled = true;
                SaveChangesButton.IsEnabled = true;
            }
            else
            {
                LockoutStatusText.Text = "Select a student";
                LockoutStatusText.Foreground = new SolidColorBrush(Colors.Gray);
                EditStatusComboBox.IsEnabled = false;
                UnlockAccountButton.IsEnabled = false;
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

                try
                {
                    // Update only standard properties. The PIN remains unchanged by passing null.
                    await _database.SaveStudentAsync(selected, null);

                    SaveChangesButton.Content = "Saved Successfully!";
                    RefreshDataGrid(); // Sync list view
                    await Task.Delay(2000);
                    SaveChangesButton.Content = "Save Security Changes";
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error Saving: {ex.Message}");
                }
            }
        }

        // --- BATCH OPERATIONS ---

        private async void OpenBatchDialogButton_Click(object sender, RoutedEventArgs e)
        {
            BatchUpdateDialog.XamlRoot = this.Content.XamlRoot;
            BatchDialogStatusText.Visibility = Visibility.Collapsed;
            ConfirmBatchButton.IsEnabled = true;
            await BatchUpdateDialog.ShowAsync();
        }

        private async void ConfirmBatchButton_Click(object sender, RoutedEventArgs e)
        {
            string course = BatchCourseComboBox.SelectedItem?.ToString() ?? "All Courses";
            string year = (BatchYearComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All Years";
            string status = (BatchNewStatusComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Active";

            ConfirmBatchButton.IsEnabled = false;
            BatchDialogStatusText.Text = "Applying updates...";
            BatchDialogStatusText.Foreground = new SolidColorBrush(Colors.White);
            BatchDialogStatusText.Visibility = Visibility.Visible;

            try
            {
                int affectedRows = await _database.BatchUpdateStudentStatusAsync(course, year, status);

                BatchDialogStatusText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 52, 211, 153)); // Green
                BatchDialogStatusText.Text = $"Success! {affectedRows} students updated to {status}.";

                // Refresh the master list so the changes appear immediately
                await LoadDataAsync();

                await Task.Delay(2000);
                BatchUpdateDialog.Hide();
            }
            catch (Exception ex)
            {
                BatchDialogStatusText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 248, 113, 113)); // Red
                BatchDialogStatusText.Text = ex.Message;
                ConfirmBatchButton.IsEnabled = true;
            }
        }

        // --- WINDOW HELPERS ---

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