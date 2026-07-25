using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Linq;
using WinRT.Interop;

namespace NFC_System
{
    public sealed partial class SecurityDashboardWindow : Window
    {
        private readonly DatabaseService _database = new();

        public SecurityDashboardWindow()
        {
            this.InitializeComponent();
            MaximizeWindow();

            ResetPinButton.Click += ResetPinButton_Click;

            _ = InitializeAsync();
        }

        private async System.Threading.Tasks.Task InitializeAsync()
        {
            try
            {
                await _database.EnsureSchemaAsync();
                await RefreshDashboardAsync();
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"Database setup failed: {ex.Message}";
            }
        }

        // For testing purposes, this method opens the alert details window when the Refresh button is clicked. 
        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            _ = RefreshDashboardAsync();
        }

        // Only works if there are items in the AlertsListView. 
        private void AlertsListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (AlertsListView.SelectedItem != null)
            {
                var alertWindow = new AlertDetailsWindow();
                // Here you would normally pass the selected alert data into the window:
                // alertWindow.LoadAlertData(selectedItem);

                alertWindow.Activate();

                // Deselect the item so it can be clicked again later
                AlertsListView.SelectedItem = null;
            }
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            var dashboard = new MainWindow();
            dashboard.Activate();
            this.Close();
        }

        private async void ResetPinButton_Click(object sender, RoutedEventArgs e)
        {
            string studentId = ResetStudentIdTextBox.Text.Trim();
            string pin = NewPinPasswordBox.Password.Trim();

            if (string.IsNullOrWhiteSpace(studentId) || pin.Length != 4 || !pin.All(char.IsDigit))
            {
                StatusTextBlock.Text = "Enter a student ID and a 4-digit PIN.";
                return;
            }

            try
            {
                await _database.ResetPinAsync(studentId, pin);
                StatusTextBlock.Text = $"New PIN set and lockout cleared for {studentId}.";
                NewPinPasswordBox.Password = "";
                ResetStudentIdTextBox.Text = "";
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"Could not reset PIN: {ex.Message}";
            }
        }

        private async void AddCourseButton_Click(object sender, RoutedEventArgs e)
        {
            string courseName = NewCourseTextBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(courseName))
            {
                StatusTextBlock.Text = "Please enter a valid course name.";
                return;
            }

            try
            {
                // 1. Save to database
                await _database.AddCourseAsync(courseName);

                // 2. Clear the input box
                NewCourseTextBox.Text = "";
                StatusTextBlock.Text = $"Course '{courseName}' added successfully.";

                // 3. Show a clear, visible success prompt
                ContentDialog successDialog = new ContentDialog
                {
                    Title = "Course Created Successfully",
                    Content = $"The academic course '{courseName}' has been added to the database.\n\nIt will now appear in the dropdown menu on the Student Registration window.",
                    CloseButtonText = "OK",
                    XamlRoot = this.Content.XamlRoot
                };

                await successDialog.ShowAsync();
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"Could not add course: {ex.Message}";

                ContentDialog errorDialog = new ContentDialog
                {
                    Title = "Database Error",
                    Content = $"Failed to add the course.\n\nDetails: {ex.Message}",
                    CloseButtonText = "OK",
                    XamlRoot = this.Content.XamlRoot
                };
                await errorDialog.ShowAsync();
            }
        }

        private async System.Threading.Tasks.Task RefreshDashboardAsync()
        {
            try
            {
                AlertsListView.Items.Clear();
                foreach (string alert in await _database.GetRecentAlertsAsync())
                {
                    AlertsListView.Items.Add(alert);
                }

                AuditLogsListView.Items.Clear();
                foreach (string log in await _database.GetRecentLogsAsync())
                {
                    AuditLogsListView.Items.Add(log);
                }

                StatusTextBlock.Text = "Dashboard refreshed.";
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"Could not refresh dashboard: {ex.Message}";
            }
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
    }
}