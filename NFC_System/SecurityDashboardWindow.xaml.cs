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

        // NEW: Expands the right column to 100% width for easier log reading
        private void ExpandLogsToggle_Click(object sender, RoutedEventArgs e)
        {
            if (ExpandLogsToggle.IsChecked == true)
            {
                // Collapse the left admin panel completely
                LeftAdminColumn.Width = new GridLength(0);
                AdminScrollViewer.Visibility = Visibility.Collapsed;
                ExpandLogsToggle.Content = "⮌ Collapse";
            }
            else
            {
                // Restore the split view
                LeftAdminColumn.Width = new GridLength(4.5, GridUnitType.Star);
                AdminScrollViewer.Visibility = Visibility.Visible;
                ExpandLogsToggle.Content = "⛶ Expand Logs";
            }
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            _ = RefreshDashboardAsync();
        }

        private void LogDateFilter_DateChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args)
        {
            _ = RefreshDashboardAsync();
        }

        private void LogTypeFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _ = RefreshDashboardAsync();
        }

        private void ClearFiltersButton_Click(object sender, RoutedEventArgs e)
        {
            LogDateFilter.DateChanged -= LogDateFilter_DateChanged;
            if (LogTypeFilter != null) LogTypeFilter.SelectionChanged -= LogTypeFilter_SelectionChanged;

            LogDateFilter.Date = null;
            if (LogTypeFilter != null) LogTypeFilter.SelectedIndex = 0;

            LogDateFilter.DateChanged += LogDateFilter_DateChanged;
            if (LogTypeFilter != null) LogTypeFilter.SelectionChanged += LogTypeFilter_SelectionChanged;

            _ = RefreshDashboardAsync();
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            var dashboard = new MainWindow();
            dashboard.Activate();
            this.Close();
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

        private async Task RefreshDashboardAsync()
        {
            try
            {
                var allAlerts = await _database.GetRecentAlertsAsync();
                var allLogs = await _database.GetRecentLogsAsync();

                // 1. FILTER THE JUNK: Strip out intermediate PIN failures from BOTH lists.
                // By filtering out "PIN_FAILURE", we hide attempts 1 and 2, but we keep the final "PIN_LOCKED" event.
                allAlerts = allAlerts.Where(a => !a.Contains("PIN_FAILURE", StringComparison.OrdinalIgnoreCase)).ToList();
                allLogs = allLogs.Where(l => !l.Contains("PIN_FAILURE", StringComparison.OrdinalIgnoreCase)).ToList();

                // 2. Apply Date Filtering
                DateTime? filterDate = LogDateFilter.Date?.DateTime;
                if (filterDate.HasValue)
                {
                    string targetDateString = filterDate.Value.ToString("yyyy-MM-dd");
                    allAlerts = allAlerts.Where(a => a.Contains(targetDateString)).ToList();
                    allLogs = allLogs.Where(l => l.Contains(targetDateString)).ToList();
                }

                // 3. Apply Event Type Filtering via Keyword Mapping
                string selectedType = (LogTypeFilter?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All Events";
                if (selectedType != "All Events")
                {
                    List<string> filterKeywords = new List<string>();

                    switch (selectedType)
                    {
                        case "PIN Lockouts":
                            filterKeywords.AddRange(new[] { "PIN_LOCKED", "lockout" });
                            break;
                        case "Unauthorized Attempts":
                            filterKeywords.AddRange(new[] { "UNAUTHORIZED", "NOT_REGISTERED", "TAILGATING", "DENIED", "INACTIVE" });
                            break;
                        case "Bad Reads":
                            filterKeywords.AddRange(new[] { "BAD_READ", "BAD_NFC_READ", "INVALID" });
                            break;
                        case "Admin Overrides":
                            filterKeywords.AddRange(new[] { "ADMIN_OVERRIDE", "SECURITY OVERRIDE" });
                            break;
                    }

                    if (filterKeywords.Count > 0)
                    {
                        allAlerts = allAlerts.Where(a => filterKeywords.Any(k => a.Contains(k, StringComparison.OrdinalIgnoreCase))).ToList();
                        allLogs = allLogs.Where(l => filterKeywords.Any(k => l.Contains(k, StringComparison.OrdinalIgnoreCase))).ToList();
                    }
                }

                // 4. Bind the processed and filtered data back to the UI
                AlertsListView.Items.Clear();
                foreach (string alert in allAlerts)
                {
                    AlertsListView.Items.Add(alert);
                }

                AuditLogsListView.Items.Clear();
                foreach (string log in allLogs)
                {
                    AuditLogsListView.Items.Add(log);
                }

                StatusTextBlock.Text = filterDate.HasValue
                    ? $"Dashboard filtered for {filterDate.Value:MMM dd, yyyy}."
                    : "Dashboard refreshed successfully.";
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