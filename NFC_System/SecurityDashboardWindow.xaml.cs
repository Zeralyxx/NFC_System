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

            SaveDefaultModeButton.Click += SaveDefaultModeButton_Click;
            ResetPinButton.Click += ResetPinButton_Click;
            SaveEventButton.Click += SaveEventButton_Click;
            AddAttendeeButton.Click += AddAttendeeButton_Click;

            _ = InitializeAsync();
        }

        private async System.Threading.Tasks.Task InitializeAsync()
        {
            try
            {
                await _database.EnsureSchemaAsync();
                string savedMode = await _database.GetSettingAsync("verification_mode", "Standard");
                DefaultModeComboBox.SelectedIndex = savedMode switch
                {
                    "Fast" => 0,
                    "High-Security" => 2,
                    _ => 1
                };

                await RefreshDashboardAsync();
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"Database setup failed: {ex.Message}";
            }
        }

        // For testing purposes, this method opens the alert details window when the Refresh button is clicked. 
        private void RefreshButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            // Launch the details modal instantly for testing
            var alertWindow = new AlertDetailsWindow();
            alertWindow.Activate();
        }

        // Only works if there are items in the AlertsListView. This is a placeholder for actual alert selection logic. 
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

        private async void SaveDefaultModeButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                VerificationMode mode = GetSelectedMode(DefaultModeComboBox);
                await _database.SetSettingAsync("verification_mode", DatabaseService.ToStorageValue(mode));
                StatusTextBlock.Text = $"Default verification mode saved: {DatabaseService.ToStorageValue(mode)}";
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"Could not save mode: {ex.Message}";
            }
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
                StatusTextBlock.Text = $"PIN reset and lockout cleared for {studentId}.";
                NewPinPasswordBox.Password = "";
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"Could not reset PIN: {ex.Message}";
            }
        }

        private async void SaveEventButton_Click(object sender, RoutedEventArgs e)
        {
            string eventId = EventIdTextBox.Text.Trim();
            string eventName = EventNameTextBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(eventId) || string.IsNullOrWhiteSpace(eventName))
            {
                StatusTextBlock.Text = "Enter both event ID and event name.";
                return;
            }

            try
            {
                await _database.SaveEventAsync(
                    eventId,
                    eventName,
                    GetSelectedMode(EventModeComboBox),
                    RestrictedEventCheckBox.IsChecked == true);

                AttendeeEventIdTextBox.Text = eventId;
                StatusTextBlock.Text = $"Event saved: {eventId} - {eventName}.";
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"Could not save event: {ex.Message}";
            }
        }

        private async void AddAttendeeButton_Click(object sender, RoutedEventArgs e)
        {
            string eventId = AttendeeEventIdTextBox.Text.Trim();
            string studentId = AttendeeStudentIdTextBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(eventId) || string.IsNullOrWhiteSpace(studentId))
            {
                StatusTextBlock.Text = "Enter both event ID and student ID.";
                return;
            }

            try
            {
                await _database.AddEventAttendeeAsync(eventId, studentId);
                StatusTextBlock.Text = $"{studentId} approved for {eventId}.";
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"Could not approve attendee: {ex.Message}";
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

        private static VerificationMode GetSelectedMode(ComboBox comboBox)
        {
            return comboBox.SelectedIndex switch
            {
                0 => VerificationMode.Fast,
                2 => VerificationMode.HighSecurity,
                _ => VerificationMode.Standard
            };
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
