using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using WinRT.Interop;
using Windows.Devices.Enumeration;
using System.Linq;
using System.Threading.Tasks;

namespace NFC_System
{
    public sealed partial class VerificationWindow : Window
    {
        private readonly DatabaseService _database = new();
        private readonly VerificationEngine _engine;
        private bool _isInitializing = true;

        // THE FIX: Timer to auto-clear the live feed
        private readonly DispatcherTimer _liveFeedTimer = new();

        public VerificationWindow()
        {
            this.InitializeComponent();
            // Subscribe to the live monitor
            DatabaseMonitor.ConnectionStatusChanged += UpdateOfflineBanner;
            UpdateOfflineBanner(DatabaseMonitor.IsOnline); // Set initial state on load
            _engine = new VerificationEngine(_database);

            MaximizeWindow();

            // Set up 10-second auto-clear timer
            _liveFeedTimer.Interval = TimeSpan.FromSeconds(10);
            _liveFeedTimer.Tick += (s, e) =>
            {
                _liveFeedTimer.Stop();
                LiveFeedActivePanel.Visibility = Visibility.Collapsed;
                LiveFeedIdlePanel.Visibility = Visibility.Visible;
            };

            _ = InitializeAsync();
        }

        private async Task InitializeAsync()
        {
            try
            {
                await _database.EnsureSchemaAsync();

                var cameras = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);
                CameraComboBox.ItemsSource = cameras;

                string savedCamId = await _database.GetSettingAsync("selected_camera", "");
                if (!string.IsNullOrEmpty(savedCamId))
                    CameraComboBox.SelectedItem = cameras.FirstOrDefault(c => c.Id == savedCamId) ?? cameras.FirstOrDefault();
                else
                    CameraComboBox.SelectedItem = cameras.FirstOrDefault();

                DirectionComboBox.SelectedIndex = KioskStateController.CurrentType == TransactionType.Entry ? 0 : 1;

                string savedMode = await _database.GetSettingAsync("verification_mode", "Standard");
                SecurityModeComboBox.SelectedIndex = savedMode switch
                {
                    "Fast" => 0,
                    "High-Security" => 2,
                    _ => 1
                };

                VerificationLogListView.Items.Insert(0, "[INFO] Risk-based verification engine ready.");

                await Task.Delay(500);
                _isInitializing = false;
            }
            catch (Exception ex)
            {
                VerificationLogListView.Items.Insert(0, $"[DB ERROR] {ex.Message}");
            }
        }

        private async void CameraComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CameraComboBox.SelectedItem is DeviceInformation selectedCam)
            {
                KioskStateController.SelectedCameraId = selectedCam.Id;
                try { await _database.SetSettingAsync("selected_camera", selectedCam.Id); } catch { }
            }
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            var dashboard = new MainWindow();
            dashboard.Activate();
            this.Close();
        }
        private void UpdateOfflineBanner(bool isOnline)
        {
            // DispatcherQueue safely pushes the update to the UI thread
            DispatcherQueue.TryEnqueue(() =>
            {
                if (GlobalOfflineBanner != null)
                {
                    GlobalOfflineBanner.Visibility = isOnline ? Visibility.Collapsed : Visibility.Visible;
                }
            });
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

        private void LaunchKioskButton_Click(object sender, RoutedEventArgs e)
        {
            var mode = (SecurityModeComboBox.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "Standard";
            var type = (DirectionComboBox.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "Entry";

            var kiosk = new KioskModeWindow("Gate", $"{type} ({mode})");

            KioskModeWindow.OnKioskOutcome -= ApplyOutcomeFromKiosk;
            KioskModeWindow.OnKioskOutcome += ApplyOutcomeFromKiosk;

            KioskModeWindow.OnKioskLog -= AddKioskLog;
            KioskModeWindow.OnKioskLog += AddKioskLog;

            kiosk.Closed += (s, args) =>
            {
                KioskModeWindow.OnKioskOutcome -= ApplyOutcomeFromKiosk;
                KioskModeWindow.OnKioskLog -= AddKioskLog;
            };

            kiosk.Activate();
        }

        private void ApplyOutcomeFromKiosk(VerificationOutcome outcome)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                ApplyOutcome(outcome);
            });
        }

        private void AddKioskLog(string msg)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                VerificationLogListView.Items.Insert(0, msg);
            });
        }

        private void PlaySecurityAlert()
        {
            System.Threading.Tasks.Task.Run(() =>
            {
                for (int i = 0; i < 3; i++)
                {
                    Console.Beep(2500, 300);
                    System.Threading.Tasks.Task.Delay(100).Wait();
                }
            });
        }

        private async void UnlockAccountButton_Click(object sender, RoutedEventArgs e)
        {
            string studentId = OverrideStudentIdBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(studentId))
            {
                VerificationLogListView.Items.Insert(0, "[ERROR] Valid Student ID required to unlock.");
                return;
            }

            try
            {
                var student = await _database.GetStudentByIdAsync(studentId);
                if (student == null)
                {
                    VerificationLogListView.Items.Insert(0, $"[ERROR] Unlock Aborted: Student ID '{studentId}' does not exist in the database.");
                    PlaySecurityAlert();
                    return;
                }

                await _database.UpdatePinFailureAsync(studentId, 0, false);

                try
                {
                    await _database.AddAlertAsync(AppSession.CurrentStaffName, "ADMIN_OVERRIDE", $"Manually cleared 2FA lockout for {student.FullName} ({studentId}).");
                }
                catch { }

                VerificationLogListView.Items.Insert(0, $"[SECURITY OVERRIDE] Guard cleared 2FA lockout for {student.FullName} ({studentId}).");
                OverrideStudentIdBox.Text = "";
            }
            catch (Exception ex)
            {
                VerificationLogListView.Items.Insert(0, $"[DB ERROR] Could not unlock account: {ex.Message}");
            }
        }

        // THE FIX: Populate the Live Authentication Feed
        private async void ApplyOutcome(VerificationOutcome outcome)
        {
            if (!string.IsNullOrWhiteSpace(outcome.LogLine))
            {
                VerificationLogListView.Items.Insert(0, outcome.LogLine);
            }

            string[] severeErrors = { "PIN_LOCKED", "ANTI_TAILGATING_VIOLATION", "UNAUTHORIZED_EVENT_ACCESS", "NOT_REGISTERED", "CREDENTIAL_MISMATCH", "INACTIVE_STUDENT" };
            if (severeErrors.Contains(outcome.ErrorCategory))
            {
                PlaySecurityAlert();
            }

            // Don't show partial steps (like waiting for PIN), only final outcomes
            if (outcome.Step == VerificationStep.Completed && outcome.Student != null)
            {
                OverrideStudentIdBox.Text = outcome.Student.StudentId;

                // Load the image bytes via our helper
                LiveStudentPhoto.ProfilePicture = await ImageHelper.GetBitmapAsync(outcome.Student.PhotoData);

                LiveStudentName.Text = outcome.Student.FullName;
                LiveStudentDetails.Text = $"{outcome.Student.Course} • Year {outcome.Student.YearLevel} • {outcome.Student.SectionName}";

                if (outcome.IsGranted)
                {
                    LiveResultBorder.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(26, 52, 211, 153));
                    LiveResultBorder.BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(48, 52, 211, 153));
                    LiveResultText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 52, 211, 153));
                    LiveResultText.Text = "ACCESS GRANTED";
                }
                else
                {
                    LiveResultBorder.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(26, 248, 113, 113));
                    LiveResultBorder.BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(48, 248, 113, 113));
                    LiveResultText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 248, 113, 113));
                    LiveResultText.Text = $"DENIED: {outcome.ErrorCategory.Replace("_", " ")}";
                }

                LiveFeedIdlePanel.Visibility = Visibility.Collapsed;
                LiveFeedActivePanel.Visibility = Visibility.Visible;

                _liveFeedTimer.Stop();
                _liveFeedTimer.Start();
            }
            else
            {
                OverrideStudentIdBox.Text = "";
            }
        }

        private async void SecurityModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SecurityModeComboBox == null || DirectionComboBox == null) return;

            KioskStateController.BroadcastModeChange(GetSelectedMode(), GetSelectedTransactionType());

            string modeString = GetSelectedMode() switch
            {
                VerificationMode.Fast => "Fast",
                VerificationMode.HighSecurity => "High-Security",
                _ => "Standard"
            };

            try
            {
                await _database.SetSettingAsync("verification_mode", modeString);

                if (!_isInitializing)
                {
                    string staff = AppSession.CurrentStaffName;
                    await _database.AddAlertAsync(staff, "ADMIN_ACTION", $"Changed global gate security mode to {modeString}.");
                    VerificationLogListView.Items.Insert(0, $"[AUDIT] Security Mode changed to {modeString} by {staff}");
                }
            }
            catch { }
        }

        private async void DirectionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SecurityModeComboBox == null || DirectionComboBox == null) return;
            KioskStateController.BroadcastModeChange(GetSelectedMode(), GetSelectedTransactionType());

            if (!_isInitializing)
            {
                string direction = DirectionComboBox.SelectedIndex == 1 ? "Exit" : "Entry";
                string staff = AppSession.CurrentStaffName;

                try
                {
                    await _database.AddAlertAsync(staff, "ADMIN_ACTION", $"Changed Gate Direction to {direction}.");
                    VerificationLogListView.Items.Insert(0, $"[AUDIT] Gate Direction changed to {direction} by {staff}");
                }
                catch { }
            }
        }

        private VerificationMode GetSelectedMode()
        {
            if (SecurityModeComboBox == null) return VerificationMode.Standard;

            return SecurityModeComboBox.SelectedIndex switch
            {
                0 => VerificationMode.Fast,
                2 => VerificationMode.HighSecurity,
                _ => VerificationMode.Standard
            };
        }

        private TransactionType GetSelectedTransactionType()
        {
            if (DirectionComboBox == null) return TransactionType.Entry;

            return DirectionComboBox.SelectedIndex switch
            {
                1 => TransactionType.Exit,
                _ => TransactionType.Entry
            };
        }
    }
}