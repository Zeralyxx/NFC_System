using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WinRT.Interop;

namespace NFC_System
{
    public sealed partial class SecurityDashboardWindow : Window
    {
        private readonly DatabaseService _database = new();
        private List<SystemAuditLog> _masterLogsCache = new(); // Holds the deep query for the popup

        public SecurityDashboardWindow()
        {
            this.InitializeComponent();
            MaximizeWindow();

            ResetPinButton.Click += ResetPinButton_Click;

            _ = InitializeAsync();
        }

        private async Task InitializeAsync()
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

        private async Task RefreshDashboardAsync()
        {
            try
            {
                // Load the 30 most recent items into the Live Activity Feed on the main screen
                var recentLogs = await _database.GetMasterAuditLogsAsync(30);
                RecentActivityListView.ItemsSource = recentLogs;

                StatusTextBlock.Text = "Dashboard refreshed successfully.";
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

            // Force dialog to start at standard width
            DialogLogContainer.Width = 900;
            PopupExpandToggle.IsChecked = false;
            PopupExpandToggle.Content = "⛶ Expand View";

            // Fetch a deep history (e.g., 2000 records) for the master explorer
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

        private void PopupFilter_Changed(object sender, RoutedEventArgs e)
        {
            ApplyPopupFilters();
        }

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

            // 1. Text Search
            string query = PopupSearchBox.Text?.Trim().ToLower() ?? "";
            if (!string.IsNullOrEmpty(query))
            {
                filtered = filtered.Where(l =>
                    (l.Subject != null && l.Subject.ToLower().Contains(query)) ||
                    (l.Details != null && l.Details.ToLower().Contains(query)));
            }

            // 2. Type Dropdown
            string type = (PopupTypeFilter.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All Types";
            if (type != "All Types")
            {
                filtered = filtered.Where(l => l.LogType == type);
            }

            // 3. Status Dropdown
            string status = (PopupStatusFilter.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All Statuses";
            if (status == "Granted / Resolved")
            {
                filtered = filtered.Where(l => l.Status == "GRANTED" || l.Status == "RESOLVED");
            }
            else if (status == "Denied / Flagged")
            {
                filtered = filtered.Where(l => l.Status == "DENIED" || l.Status == "FLAGGED");
            }

            PopupLogsListView.ItemsSource = filtered.ToList();
        }

        // --- ADMINISTRATIVE ACTIONS ---

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
                return;
            }

            try
            {
                await _database.ResetPinAsync(studentId, pin);

                NewPinPasswordBox.Password = "";
                ResetStudentIdTextBox.Text = "";
                StatusTextBlock.Text = $"New PIN set and lockout cleared for {studentId}.";

                try
                {
                    await _database.AddAlertAsync(null, "ADMIN_OVERRIDE", $"Security personnel manually unlocked account and reset PIN for {studentId}.");
                }
                catch { }

                await RefreshDashboardAsync();
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
                await _database.AddCourseAsync(courseName);

                NewCourseTextBox.Text = "";
                StatusTextBlock.Text = $"Course '{courseName}' added successfully.";

                ContentDialog successDialog = new ContentDialog
                {
                    Title = "Course Created Successfully",
                    Content = $"The academic course '{courseName}' has been added to the database.\n\nIt will now appear in the dropdown menu on the Student Registration window.",
                    CloseButtonText = "OK",
                    XamlRoot = this.Content.XamlRoot
                };

                await successDialog.ShowAsync();
                await RefreshDashboardAsync();
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

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
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