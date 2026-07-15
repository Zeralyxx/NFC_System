using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using WinRT.Interop;

namespace NFC_System
{
    // Dummy Data Model
    public class StudentRecordModel
    {
        public string StudentId { get; set; }
        public string FullName { get; set; }
        public string Course { get; set; }
        public string Status { get; set; }
        public string NfcUid { get; set; }
        public bool IsLockedOut { get; set; }
    }

    public sealed partial class StudentManagementWindow : Window
    {
        private List<StudentRecordModel> _allStudents = new();

        public StudentManagementWindow()
        {
            this.InitializeComponent();
            MaximizeWindow();
            LoadDummyData();
        }

        private void MaximizeWindow()
        {
            IntPtr hWnd = WindowNative.GetWindowHandle(this);
            WindowId windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
            AppWindow appWindow = AppWindow.GetFromWindowId(windowId);

            if (appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.Maximize();
            }
        }

        private void LoadDummyData()
        {
            // Injecting temporary UI test data
            _allStudents = new List<StudentRecordModel>
            {
                new StudentRecordModel { StudentId = "26-00001", FullName = "Justin Mason", Course = "BS Computer Science", Status = "Active", NfcUid = "04:A1:B2:C3", IsLockedOut = false },
                new StudentRecordModel { StudentId = "26-00045", FullName = "Alyssa Rivera", Course = "BS Information Tech", Status = "Active", NfcUid = "11:F2:E3:D4", IsLockedOut = true },
                new StudentRecordModel { StudentId = "26-00102", FullName = "Marcus Cruz", Course = "BS Engineering", Status = "Inactive", NfcUid = "99:A8:B7:C6", IsLockedOut = false },
                new StudentRecordModel { StudentId = "26-00214", FullName = "Elena Santos", Course = "BS Computer Science", Status = "Expelled", NfcUid = "00:00:00:00", IsLockedOut = true }
            };

            RefreshDataGrid();
        }

        private void RefreshDataGrid()
        {
            string searchTerm = SearchBox.Text.ToLower();
            string filter = FilterComboBox.SelectedItem is ComboBoxItem item ? item.Content.ToString() : "All Students";

            var filteredData = _allStudents.Where(s =>
                (string.IsNullOrEmpty(searchTerm) || s.FullName.ToLower().Contains(searchTerm) || s.StudentId.Contains(searchTerm)) &&
                (filter == "All Students" ||
                (filter == "Active Only" && s.Status == "Active") ||
                (filter == "Locked Out" && s.IsLockedOut) ||
                (filter == "Inactive" && s.Status == "Inactive"))
            ).ToList();

            StudentListView.ItemsSource = filteredData;
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            var dashboard = new MainWindow();
            dashboard.Activate();
            this.Close();
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            RefreshDataGrid();
        }

        private void FilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (StudentListView != null) // Prevent null ref on initialization
            {
                RefreshDataGrid();
            }
        }

        private void StudentListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (StudentListView.SelectedItem is StudentRecordModel selectedStudent)
            {
                // Populate text fields
                EditNameText.Text = selectedStudent.FullName;
                EditIdText.Text = selectedStudent.StudentId;
                EditUidText.Text = selectedStudent.NfcUid;

                // Populate combo box
                EditStatusComboBox.SelectedIndex = selectedStudent.Status switch
                {
                    "Active" => 0,
                    "Inactive" => 1,
                    _ => 2
                };

                // Populate Lockout UI
                if (selectedStudent.IsLockedOut)
                {
                    LockoutStatusText.Text = "ACCOUNT LOCKED (Too many PIN failures)";
                    LockoutStatusText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.DarkOrange);
                    UnlockAccountButton.IsEnabled = true;
                }
                else
                {
                    LockoutStatusText.Text = "Account is secure (No active flags)";
                    LockoutStatusText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.ForestGreen);
                    UnlockAccountButton.IsEnabled = false;
                }

                // Enable forms
                EditStatusComboBox.IsEnabled = true;
                ResetPinBox.IsEnabled = true;
                ResetPinBox.Password = ""; // Clear old typing
                SaveChangesButton.IsEnabled = true;
            }
            else
            {
                // Reset/Disable panel if nothing is selected
                EditNameText.Text = "-";
                EditIdText.Text = "-";
                EditUidText.Text = "-";
                LockoutStatusText.Text = "Select a student";
                LockoutStatusText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray);
                EditStatusComboBox.IsEnabled = false;
                UnlockAccountButton.IsEnabled = false;
                ResetPinBox.IsEnabled = false;
                SaveChangesButton.IsEnabled = false;
            }
        }

        private void UnlockAccountButton_Click(object sender, RoutedEventArgs e)
        {
            // Temporary UI feedback logic
            LockoutStatusText.Text = "Account successfully unlocked.";
            LockoutStatusText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.ForestGreen);
            UnlockAccountButton.IsEnabled = false;
        }

        private void SaveChangesButton_Click(object sender, RoutedEventArgs e)
        {
            // Placeholder for Database UPDATE logic
            EditNameText.Text = "Saved Successfully!";
        }
    }
}