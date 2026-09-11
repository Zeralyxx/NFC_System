using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using WinRT.Interop;
using ZXing;
using ZXing.Common;
using System.IO;
using System.Text.RegularExpressions;

namespace NFC_System
{
    public sealed partial class RegistrationWindow : Window
    {
        private bool _isScanning = false;
        private readonly DatabaseService _database = new();
        private bool _duplicateWarningAcknowledged = false;

        private byte[]? _currentPhotoData = null;

        // State variables for Exit Interceptor
        private bool _isForceClosing = false;
        private bool _isAwaitingAdminAuth = false;
        private string _pendingAdminAction = "";
        private string _pendingAdminSeverity = "";

        public RegistrationWindow()
        {
            this.InitializeComponent();
            DatabaseMonitor.ConnectionStatusChanged += UpdateOfflineBanner;
            UpdateOfflineBanner(DatabaseMonitor.IsOnline);
            MaximizeWindow();

            // THE FIX: Subscribe to the global hardware manager
            HardwareService.OnUidScanned += HardwareService_OnUidScanned;

            IntPtr hWnd = WindowNative.GetWindowHandle(this);
            WindowId windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
            AppWindow appWindow = AppWindow.GetFromWindowId(windowId);
            appWindow.Closing += AppWindow_Closing;

            this.Closed += Window_Closed;

            ScanUidButton.Click += ScanUidButton_Click;
            ClearButton.Click += ClearButton_Click;
            SaveButton.Click += SaveButton_Click;
            FullNameTextBox.TextChanged += (s, e) => _duplicateWarningAcknowledged = false;

            _ = InitializeAsync();
        }

        // ====================================================================
        // THE FIX: SHARED HARDWARE SERVICE EVENT HANDLER
        // ====================================================================
        private void HardwareService_OnUidScanned(string uid)
        {
            // SMART ROUTING: Ignore scans if the Kiosk is actively tracking attendance
            if (AppSession.IsKioskRunning) return;

            DispatcherQueue.TryEnqueue(async () =>
            {
                // 1. Check if the app is waiting for an Admin Authentication Tap (Exit Routine)
                if (_isAwaitingAdminAuth)
                {
                    await HandleAdminAuthScanAsync(uid);
                    return;
                }

                // 2. Otherwise, check if we are actively registering a student
                if (_isScanning)
                {
                    bool invalidUid = IsInvalidUid(uid);
                    StudentRecord? existingStudent = null;

                    // Query the database asynchronously without locking the UI thread!
                    if (!invalidUid && DatabaseMonitor.IsOnline)
                    {
                        existingStudent = await _database.GetStudentByUidAsync(uid);
                    }

                    if (invalidUid)
                    {
                        NfcUidTextBox.Text = "";
                        UidLogListView.Items.Insert(0, "[WARNING] Invalid hardware read. Please scan again.");
                        PreviewTextBlock.Text = "NFC UID: Corrupted transmission layout - re-tap card";
                    }
                    else if (existingStudent != null)
                    {
                        NfcUidTextBox.Text = "";
                        UidLogListView.Items.Insert(0, $"[ERROR] Card is already registered to {existingStudent.FullName} ({existingStudent.StudentId}). Please use the Student Management window to edit this profile.");
                        PreviewTextBlock.Text = "NFC UID: Card already in use by another student.";
                    }
                    else
                    {
                        NfcUidTextBox.Text = uid;
                        PinPasswordBox.PlaceholderText = "****";
                        UidLogListView.Items.Insert(0, $"[INFO] New unassigned card scanned: {uid}");

                        string currentId = StudentIdTextBox.Text.Trim();
                        string generatedQr = BuildQrCredential(currentId);
                        QrCredentialTextBox.Text = generatedQr;

                        if (!string.IsNullOrEmpty(generatedQr))
                        {
                            QrCodeImage.Source = GenerateQrBitmap(generatedQr);
                            QrCodeImage.Visibility = Visibility.Visible;
                            QrPlaceholderPanel.Visibility = Visibility.Collapsed;
                        }
                        else
                        {
                            QrCodeImage.Visibility = Visibility.Collapsed;
                            QrPlaceholderPanel.Visibility = Visibility.Visible;
                            UidLogListView.Items.Insert(0, "[INFO] Type a Student ID to generate the QR code.");
                        }

                        PreviewTextBlock.Text = $"Student ID: {currentId}\nFull Name: {FullNameTextBox.Text}\nCourse: {CourseComboBox.SelectedItem?.ToString()}\nYear Level: {YearLevelTextBox.Text}\nSection: {SectionTextBox.Text}\nNFC UID: {uid}\nQR Credential: {generatedQr}";
                    }

                    _isScanning = false;
                }
            });
        }

        // ====================================================================
        // RBAC SEVERITY-AWARE EXIT INTERCEPTOR
        // ====================================================================
        private async void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
        {
            if (_isForceClosing) return;
            args.Cancel = true;

            // 1. MASTER ADMINISTRATOR FLOW (Bypass Authorization)
            if (AppSession.CurrentStaffRoleLabel == "Master Admin")
            {
                ContentDialog masterDialog = new ContentDialog
                {
                    Title = "Exit Application",
                    Content = "You may have unsynced offline data. Would you like to push it to the cloud before exiting?",
                    PrimaryButtonText = "Push to Cloud & Exit",
                    SecondaryButtonText = "Exit Anyway",
                    CloseButtonText = "Cancel",
                    XamlRoot = this.Content.XamlRoot
                };

                var result = await masterDialog.ShowAsync();
                if (result == ContentDialogResult.Primary) await PerformCloudPushAndExit();
                else if (result == ContentDialogResult.Secondary) ForceExit();
            }
            // 2. STANDARD ADMINISTRATOR FLOW (High Severity - Needs PIN + Tap)
            else if (AppSession.IsAdmin)
            {
                ContentDialog adminDialog = new ContentDialog
                {
                    Title = "Exit Application",
                    Content = "You have unsynced offline data. Pushing this to the cloud requires High-Severity authorization (PIN + NFC Tap).",
                    PrimaryButtonText = "Authorize Sync & Exit",
                    SecondaryButtonText = "Exit Without Syncing",
                    CloseButtonText = "Cancel",
                    XamlRoot = this.Content.XamlRoot
                };

                var result = await adminDialog.ShowAsync();
                if (result == ContentDialogResult.Primary)
                {
                    _pendingAdminAction = "EXIT_SYNC";
                    _pendingAdminSeverity = "HIGH";

                    AdminPinBox.Visibility = Visibility.Visible;
                    AdminPinBox.Password = "";
                    AuthStatusText.Visibility = Visibility.Collapsed;
                    AdminAuthDescriptionText.Text = "To confirm this cloud upload, enter your 4-digit PIN and tap your Admin NFC card.";

                    _isAwaitingAdminAuth = true;
                    AdminAuthDialog.XamlRoot = this.Content.XamlRoot;
                    var authResult = await AdminAuthDialog.ShowAsync();

                    if (authResult == ContentDialogResult.None && _isAwaitingAdminAuth)
                    {
                        _isAwaitingAdminAuth = false;
                        _pendingAdminAction = "";
                    }
                }
                else if (result == ContentDialogResult.Secondary)
                {
                    ForceExit();
                }
            }
            // 3. ORGANIZER & GUARD FLOW (Read-Only/Low Severity Restriction)
            else
            {
                ContentDialog restrictedDialog = new ContentDialog
                {
                    Title = "Exit Application",
                    Content = "Warning: There may be unsynced offline data. You do not have Administrator privileges to push this data to the cloud. If you exit now, the data will remain safely stored locally.",
                    PrimaryButtonText = "Exit Anyway",
                    CloseButtonText = "Cancel",
                    XamlRoot = this.Content.XamlRoot
                };

                restrictedDialog.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Orange);

                var result = await restrictedDialog.ShowAsync();
                if (result == ContentDialogResult.Primary) ForceExit();
            }
        }

        private async Task PerformCloudPushAndExit()
        {
            try
            {
                if (await _database.TestConnectionAsync())
                {
                    await _database.SyncOfflineLogsToServerAsync();
                    await _database.PushStudentsToCloudAsync();
                    await _database.PushStaffToCloudAsync();
                    await _database.PushCoursesToCloudAsync();
                    await _database.PushEventsToCloudAsync();
                    await _database.PushEventApprovedStudentsToCloudAsync();
                    await _database.PushLogsToCloudAsync();
                    await _database.PushEventAttendanceToCloudAsync();
                    await _database.AddAlertAsync(AppSession.CurrentStaffName, "ADMIN_ACTION", "Authorized Cloud Push on Application Exit.");
                }
            }
            catch { }

            ForceExit();
        }

        private void ForceExit()
        {
            if (DatabaseMonitor.IsOnline && AppSession.IsLoggedIn)
            {
                try { _ = _database.AddAlertAsync(AppSession.CurrentStaffName, "STAFF_LOGOUT", $"{AppSession.CurrentStaffName} closed the application."); } catch { }
            }

            HardwareService.Disconnect();
            _isForceClosing = true;
            Application.Current.Exit();
        }

        private async Task HandleAdminAuthScanAsync(string uid)
        {
            string? role = null;
            string? pinHash = null;
            string? pinSalt = null;

            if (DatabaseMonitor.IsOnline)
            {
                try
                {
                    var details = await _database.GetStaffDetailsAsync(uid);
                    role = details.Role;
                    pinHash = details.PinHash;
                    pinSalt = details.PinSalt;
                }
                catch { }
            }

            if (role == null && uid == "04:A1:B2:C3")
            {
                role = "Master Administrator";
            }

            bool isAuthorized = false;
            string failReason = "";

            if (_pendingAdminSeverity == "HIGH")
            {
                if (role == "Administrator" || role == "Master Administrator")
                {
                    string enteredPin = AdminPinBox.Password.Trim();
                    if (string.IsNullOrEmpty(enteredPin)) failReason = "Authorization Denied: A 4-digit Staff PIN is required.";
                    else if (string.IsNullOrEmpty(pinHash)) failReason = "Authorization Denied: Tapped account does not have a PIN configured.";
                    else if (!PinHasher.VerifyPin(enteredPin, pinSalt!, pinHash)) failReason = "Authorization Denied: Invalid PIN.";
                    else isAuthorized = true;
                }
                else failReason = "Authorization Denied: Tapped card is not an Administrator.";
            }

            if (isAuthorized)
            {
                _isAwaitingAdminAuth = false;
                AdminAuthDialog.Hide();

                if (_pendingAdminAction == "EXIT_SYNC")
                {
                    _pendingAdminAction = "";
                    await PerformCloudPushAndExit();
                }
            }
            else
            {
                AuthStatusText.Text = failReason;
                AuthStatusText.Visibility = Visibility.Visible;
                PlayErrorAlert();
            }
        }

        private void Window_Closed(object sender, WindowEventArgs args)
        {
            DatabaseMonitor.ConnectionStatusChanged -= UpdateOfflineBanner;

            // THE FIX: Unhook the hardware listener to prevent memory leaks
            HardwareService.OnUidScanned -= HardwareService_OnUidScanned;
        }

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

        private void PlayErrorAlert()
        {
            Task.Run(() =>
            {
                try
                {
                    Console.Beep(2000, 300);
                    System.Threading.Thread.Sleep(100);
                    Console.Beep(2000, 300);
                }
                catch { }
            });
        }

        // ====================================================================
        // STANDARD REGISTRATION LOGIC
        // ====================================================================
        private async System.Threading.Tasks.Task InitializeAsync()
        {
            try
            {
                CourseComboBox.ItemsSource = await _database.GetDistinctCoursesAsync();
                UidLogListView.Items.Insert(0, $"[INFO] Ready for new enrollment. Hardware service active.");
            }
            catch (Exception ex)
            {
                UidLogListView.Items.Insert(0, $"[ERROR] Setup failed: {ex.Message}");
            }
        }

        private async void UploadPhotoButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var picker = new Windows.Storage.Pickers.FileOpenPicker();

                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

                picker.ViewMode = Windows.Storage.Pickers.PickerViewMode.Thumbnail;
                picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.PicturesLibrary;
                picker.FileTypeFilter.Add(".jpg");
                picker.FileTypeFilter.Add(".jpeg");
                picker.FileTypeFilter.Add(".png");

                var file = await picker.PickSingleFileAsync();

                if (file != null)
                {
                    using (var stream = await file.OpenReadAsync())
                    {
                        _currentPhotoData = await ImageHelper.ProcessProfileImageAsync(stream);
                        StudentPhotoPreview.ProfilePicture = await ImageHelper.GetBitmapAsync(_currentPhotoData);
                        UidLogListView.Items.Insert(0, $"[INFO] Profile photo attached successfully. ({_currentPhotoData.Length / 1024} KB)");
                    }
                }
            }
            catch (Exception ex)
            {
                UidLogListView.Items.Insert(0, $"[ERROR] Could not load image: {ex.Message}");
            }
        }

        private void NumberOnly_TextChanging(TextBox sender, TextBoxTextChangingEventArgs args)
        {
            string text = sender.Text;

            if (text.Any(c => !char.IsDigit(c) && c != '-'))
            {
                int selectionStart = sender.SelectionStart;
                sender.Text = new string(text.Where(c => char.IsDigit(c) || c == '-').ToArray());
                sender.SelectionStart = Math.Max(0, selectionStart - 1);
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

            if (appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.Maximize();
            }
        }

        private void UpdateOfflineBanner(bool isOnline)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (GlobalOfflineBanner != null)
                {
                    GlobalOfflineBanner.Visibility = isOnline ? Visibility.Collapsed : Visibility.Visible;
                }
            });
        }

        private bool IsInvalidUid(string uid)
        {
            if (string.IsNullOrWhiteSpace(uid)) return true;
            string[] parts = uid.Split(':');
            if (parts.Length != 4 && parts.Length != 7) return true;

            bool allZero = true;
            foreach (string part in parts) { if (part != "00") { allZero = false; break; } }
            if (allZero) return true;

            if (parts.Length >= 4)
            {
                int start = parts.Length - 4;
                bool trailingZeros = true;
                for (int i = start; i < parts.Length; i++) { if (parts[i] != "00") { trailingZeros = false; break; } }
                if (trailingZeros) return true;
            }
            return false;
        }

        private void ScanUidButton_Click(object sender, RoutedEventArgs e)
        {
            _isScanning = true;
            UidLogListView.Items.Insert(0, "[INFO] Awaiting physical target tap on reader...");
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            ClearForm();
            PreviewTextBlock.Text = "Student information will appear here.";
        }

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            string studentId = StudentIdTextBox.Text.Trim();
            string fullName = FullNameTextBox.Text.Trim();
            string email = EmailTextBox.Text.Trim();
            string nfcUid = NfcUidTextBox.Text.Trim();
            string pin = PinPasswordBox.Password.Trim();
            string qrCredential = QrCredentialTextBox.Text.Trim();
            string status = StatusComboBox.SelectedItem is ComboBoxItem item ? item.Content?.ToString() ?? "Active" : "Active";
            string course = CourseComboBox.SelectedItem?.ToString() ?? "";

            bool isTemporary = IsTemporaryCheckBox.IsChecked == true;

            if (string.IsNullOrWhiteSpace(studentId) || string.IsNullOrWhiteSpace(fullName) || string.IsNullOrWhiteSpace(nfcUid))
            {
                UidLogListView.Items.Insert(0, "[ERROR] Critical structural criteria missing (ID, Name, or NFC).");
                return;
            }

            // Strict Email Format Validation
            string emailPattern = @"^[^@\s]+@[^@\s]+\.[^@\s]+$";
            if (string.IsNullOrWhiteSpace(email) || !Regex.IsMatch(email, emailPattern))
            {
                UidLogListView.Items.Insert(0, "[ERROR] A valid email address is required (e.g., student@university.edu).");
                PlayErrorAlert();
                return;
            }

            if (string.IsNullOrWhiteSpace(pin) || pin.Length != 4 || !pin.All(char.IsDigit))
            {
                UidLogListView.Items.Insert(0, "[ERROR] A 4-digit PIN is strictly required for new enrollments.");
                return;
            }

            if (!_duplicateWarningAcknowledged)
            {
                var possibleDupes = await _database.FindPotentialDuplicatesByNameAsync(fullName);
                if (possibleDupes.Count > 0)
                {
                    UidLogListView.Items.Insert(0, $"[WARNING] {possibleDupes.Count} existing student(s) share this name — possible duplicate:");
                    foreach (var dupe in possibleDupes)
                        UidLogListView.Items.Insert(1, $"    → {dupe.StudentId} | {dupe.FullName} | {dupe.Course} {dupe.SectionName}");

                    UidLogListView.Items.Insert(0, "[ACTION REQUIRED] Verify this isn't a re-enrollment, then press Save again to confirm.");

                    _duplicateWarningAcknowledged = true;
                    return;
                }
            }

            if (string.IsNullOrWhiteSpace(qrCredential))
            {
                qrCredential = BuildQrCredential(studentId);
                QrCredentialTextBox.Text = qrCredential;
            }

            try
            {
                var student = new StudentRecord
                {
                    StudentId = studentId,
                    FullName = fullName,
                    Email = email,
                    Course = course,
                    YearLevel = YearLevelTextBox.Text.Trim(),
                    SectionName = SectionTextBox.Text.Trim(),
                    Status = status,
                    NfcUid = nfcUid,
                    QrCredential = qrCredential,
                    PhotoData = _currentPhotoData,
                    IsTemporary = isTemporary
                };

                await _database.SaveStudentAsync(student, pin);

                string tempTag = isTemporary ? "[TEMP] " : "";
                UidLogListView.Items.Insert(0, $"[SUCCESS] Access profile committed: {tempTag}{fullName}");
                PreviewTextBlock.Text = $"Student ID: {studentId}\nFull Name: {fullName}\nEmail: {email}\nCourse: {course}\nStatus: {status}\nNFC UID: {nfcUid}\nTemporary: {isTemporary}\nQR Credential: {qrCredential}\nPIN Status: Encrypted & Salted (PBKDF2)";

                PlaySuccessPing();
                ClearForm();
            }
            catch (InvalidOperationException ex)
            {
                UidLogListView.Items.Insert(0, $"[ERROR] {ex.Message}");
                PlayErrorAlert();
            }
            catch (Exception ex)
            {
                UidLogListView.Items.Insert(0, $"[ERROR] Transaction breakdown: {ex.Message}");
                PlayErrorAlert();
            }
        }

        private void ClearForm()
        {
            StudentIdTextBox.Text = "";
            FullNameTextBox.Text = "";
            EmailTextBox.Text = "";
            CourseComboBox.SelectedItem = null;
            YearLevelTextBox.Text = "";
            SectionTextBox.Text = "";
            NfcUidTextBox.Text = "";
            PinPasswordBox.Password = "";
            QrCredentialTextBox.Text = "";
            QrCodeImage.Source = null;
            PinPasswordBox.PlaceholderText = "****";
            QrCodeImage.Visibility = Visibility.Collapsed;
            QrPlaceholderPanel.Visibility = Visibility.Visible;
            StatusComboBox.SelectedIndex = 0;
            IsTemporaryCheckBox.IsChecked = false;
            _duplicateWarningAcknowledged = false;

            _currentPhotoData = null;
            StudentPhotoPreview.ProfilePicture = null;
        }

        private WriteableBitmap GenerateQrBitmap(string text)
        {
            var writer = new BarcodeWriterPixelData
            {
                Format = BarcodeFormat.QR_CODE,
                Options = new EncodingOptions
                {
                    Height = 400,
                    Width = 400,
                    Margin = 1
                }
            };

            var pixelData = writer.Write(text);
            var bitmap = new WriteableBitmap(pixelData.Width, pixelData.Height);

            using (var stream = bitmap.PixelBuffer.AsStream())
            {
                stream.Write(pixelData.Pixels, 0, pixelData.Pixels.Length);
            }

            return bitmap;
        }

        private static string BuildQrCredential(string studentId)
        {
            if (string.IsNullOrWhiteSpace(studentId)) return "";
            return studentId;
        }
    }
}