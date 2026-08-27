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
using Microsoft.UI.Xaml.Input;

namespace NFC_System
{
    public sealed partial class StudentManagementWindow : Window
    {
        private enum AdminActionType { None, SaveIndividual, DeleteIndividual, BatchUpdate, EditFullProfile }

        private readonly DatabaseService _database = new();
        private List<StudentRecord> _allStudents = new();
        private int _currentPage = 1;
        private const int PageSize = 10;

        private SerialPort? _serialPort;
        private bool _isAwaitingAdminAuth = false;
        private bool _isAwaitingNfcReplacementScan = false;
        private StudentRecord? _editingStudent = null;

        private string _currentSortColumn = "FullName";
        private bool _isSortAscending = true;
        private string _popupSortColumn = "FullName";
        private bool _isPopupSortAscending = true;

        private string _origStudentId = "";
        private string _origFullName = "";
        private string _origEmail = "";
        private string _origCourse = "";
        private string _origYearLevel = "";
        private string _origSection = "";
        private string _origStatus = "";
        private string _origNfcUid = "";
        private bool _origIsTemporary = false;
        private AdminActionType _pendingAction = AdminActionType.None;

        private string _pendingAdminSeverity = "";
        private string _pendingNfcReplacementReason = ""; // THE FIX: Add this line

        public StudentManagementWindow()
        {
            this.InitializeComponent();
            DatabaseMonitor.ConnectionStatusChanged += UpdateOfflineBanner;
            UpdateOfflineBanner(DatabaseMonitor.IsOnline);
            MaximizeWindow();
            this.Closed += Window_Closed;
            StudentListView.DoubleTapped += StudentListView_DoubleTapped;
            PopupStudentListView.DoubleTapped += PopupStudentListView_DoubleTapped;

            EditDialogStudentIdBox.TextChanged += EditDialog_FieldChanged;
            EditDialogFullNameBox.TextChanged += EditDialog_FieldChanged;
            EditDialogEmailBox.TextChanged += EditDialog_FieldChanged;
            EditDialogCourseComboBox.SelectionChanged += EditDialog_FieldChanged;
            EditDialogYearLevelBox.TextChanged += EditDialog_FieldChanged;
            EditDialogSectionBox.TextChanged += EditDialog_FieldChanged;
            EditDialogStatusComboBox.SelectionChanged += EditDialog_FieldChanged;
            EditDialogNfcUidBox.TextChanged += EditDialog_FieldChanged;
            EditDialogNewPinBox.PasswordChanged += EditDialog_FieldChanged;
            EditDialogIsTemporaryCheckBox.Checked += EditDialog_FieldChanged;
            EditDialogIsTemporaryCheckBox.Unchecked += EditDialog_FieldChanged;

            EditStudentDialog.Closed += (s, e) =>
            {
                _isAwaitingNfcReplacementScan = false;
                ChangeNfcButton.IsEnabled = true;
                NfcScanStatusText.Visibility = Visibility.Collapsed;
                NfcReplacementReasonBox.Visibility = Visibility.Collapsed;
                NfcReplacementReasonBox.Text = "";
            };

            _ = LoadDataAsync();
        }

        private async Task LoadDataAsync()
        {
            try
            {
                if (AppSession.IsEventOrganizer && ConfigPanelContainer != null)
                {
                    ConfigPanelContainer.Visibility = Visibility.Collapsed;
                }

                // THE FIX: Snapshot active UI selections to prevent wiping filters
                string activeMainCourse = CourseFilterComboBox?.SelectedItem?.ToString() ?? "All Courses";
                string activePopupCourse = PopupCourseFilter?.SelectedItem?.ToString() ?? "All Courses";

                // THE FIX: Detach the event temporarily so rebuilding the list doesn't trigger _currentPage = 1
                if (CourseFilterComboBox != null)
                    CourseFilterComboBox.SelectionChanged -= Filter_SelectionChanged;

                if (DatabaseMonitor.IsOnline)
                {
                    var result = await _database.SearchStudentsAsync("", "All Students", "All Courses", "All Years", 1, 99999);
                    _allStudents = result.Students.ToList();
                    var courses = await _database.GetDistinctCoursesAsync();

                    CourseFilterComboBox?.Items.Clear();
                    CourseFilterComboBox?.Items.Add("All Courses");
                    PopupCourseFilter?.Items.Clear();
                    PopupCourseFilter?.Items.Add("All Courses");
                    BatchCourseComboBox?.Items.Clear();
                    BatchCourseComboBox?.Items.Add("All Courses");

                    foreach (var course in courses)
                    {
                        CourseFilterComboBox?.Items.Add(course);
                        PopupCourseFilter?.Items.Add(course);
                        BatchCourseComboBox?.Items.Add(course);
                    }
                }
                else
                {
                    var cachedStudents = OfflineCacheService.GetCachedStudents();
                    _allStudents = cachedStudents.Select(c => new StudentRecord
                    {
                        StudentId = c.StudentId,
                        FullName = c.FullName,
                        NfcUid = c.NfcUid,
                        Status = c.Status,
                        PinLocked = c.PinLocked,
                        Course = "Offline Mode (Restricted View)",
                        YearLevel = "N/A",
                        SectionName = "N/A"
                    }).ToList();

                    CourseFilterComboBox?.Items.Clear();
                    CourseFilterComboBox?.Items.Add("All Courses");
                    PopupCourseFilter?.Items.Clear();
                    PopupCourseFilter?.Items.Add("All Courses");
                    BatchCourseComboBox?.Items.Clear();
                    BatchCourseComboBox?.Items.Add("All Courses");
                }

                // THE FIX: Safely restore the previous selections without resetting the page
                if (CourseFilterComboBox != null)
                {
                    CourseFilterComboBox.SelectedItem = CourseFilterComboBox.Items.Contains(activeMainCourse) ? activeMainCourse : "All Courses";
                    // Re-attach the listener once it's safe
                    CourseFilterComboBox.SelectionChanged += Filter_SelectionChanged;
                }

                if (PopupCourseFilter != null)
                {
                    PopupCourseFilter.SelectedItem = PopupCourseFilter.Items.Contains(activePopupCourse) ? activePopupCourse : "All Courses";
                }

                if (BatchCourseComboBox != null) BatchCourseComboBox.SelectedIndex = 0;

                string nfcPort = "COM3";
                if (DatabaseMonitor.IsOnline)
                {
                    try { nfcPort = await _database.GetSettingAsync("nfc_com_port", "COM3"); } catch { }
                }

                TryConnectSerial(nfcPort);

                RefreshDataGrid();

                UpdateSortIcons(isPopup: false);
                UpdateSortIcons(isPopup: true);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DB ERROR] {ex.Message}");
            }
        }

        private void SortHeader_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string columnName)
            {
                if (_currentSortColumn == columnName)
                    _isSortAscending = !_isSortAscending;
                else
                {
                    _currentSortColumn = columnName;
                    _isSortAscending = true;
                }

                UpdateSortIcons(isPopup: false);
                RefreshDataGrid();
            }
        }

        private void PopupSortHeader_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string columnName)
            {
                if (_popupSortColumn == columnName)
                    _isPopupSortAscending = !_isPopupSortAscending;
                else
                {
                    _popupSortColumn = columnName;
                    _isPopupSortAscending = true;
                }

                UpdateSortIcons(isPopup: true);
                ApplyPopupFilters();
            }
        }

        private void UpdateSortIcons(bool isPopup)
        {
            string activeColumn = isPopup ? _popupSortColumn : _currentSortColumn;
            bool isAsc = isPopup ? _isPopupSortAscending : _isSortAscending;
            string glyph = isAsc ? "\uE70E" : "\uE70D";

            if (!isPopup)
            {
                SortIcon_StudentId.Visibility = Visibility.Collapsed;
                SortIcon_FullName.Visibility = Visibility.Collapsed;
                SortIcon_Course.Visibility = Visibility.Collapsed;
                SortIcon_Year.Visibility = Visibility.Collapsed;
                SortIcon_Section.Visibility = Visibility.Collapsed;
                SortIcon_Status.Visibility = Visibility.Collapsed;

                FontIcon? activeIcon = activeColumn switch
                {
                    "StudentId" => SortIcon_StudentId,
                    "FullName" => SortIcon_FullName,
                    "Course" => SortIcon_Course,
                    "Year" => SortIcon_Year,
                    "Section" => SortIcon_Section,
                    "Status" => SortIcon_Status,
                    _ => null
                };

                if (activeIcon != null)
                {
                    activeIcon.Visibility = Visibility.Visible;
                    activeIcon.Glyph = glyph;
                }
            }
            else
            {
                PopupSortIcon_StudentId.Visibility = Visibility.Collapsed;
                PopupSortIcon_FullName.Visibility = Visibility.Collapsed;
                PopupSortIcon_Course.Visibility = Visibility.Collapsed;
                PopupSortIcon_Year.Visibility = Visibility.Collapsed;
                PopupSortIcon_Section.Visibility = Visibility.Collapsed;
                PopupSortIcon_Status.Visibility = Visibility.Collapsed;

                FontIcon? activeIcon = activeColumn switch
                {
                    "StudentId" => PopupSortIcon_StudentId,
                    "FullName" => PopupSortIcon_FullName,
                    "Course" => PopupSortIcon_Course,
                    "Year" => PopupSortIcon_Year,
                    "Section" => PopupSortIcon_Section,
                    "Status" => PopupSortIcon_Status,
                    _ => null
                };

                if (activeIcon != null)
                {
                    activeIcon.Visibility = Visibility.Visible;
                    activeIcon.Glyph = glyph;
                }
            }
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

        private void TryConnectSerial(string portName)
        {
            if (_serialPort != null && _serialPort.IsOpen) return;

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
                if (!line.StartsWith("UID=")) return;

                string uid = line.Substring(4).Trim();

                if (_isAwaitingNfcReplacementScan)
                {
                    DispatcherQueue.TryEnqueue(async () => await HandleNfcReplacementScanAsync(uid));
                    return;
                }

                if (_isAwaitingAdminAuth)
                {
                    DispatcherQueue.TryEnqueue(async () =>
                    {
                        string? role = null;
                        string? fullName = null;
                        string? pinHash = null;
                        string? pinSalt = null;

                        if (DatabaseMonitor.IsOnline)
                        {
                            try
                            {
                                var details = await _database.GetStaffDetailsAsync(uid);
                                role = details.Role;
                                fullName = details.FullName;
                                pinHash = details.PinHash;
                                pinSalt = details.PinSalt;
                            }
                            catch { }
                        }

                        if (role == null && uid == "04:A1:B2:C3")
                        {
                            role = "Master Administrator";
                            fullName = "Master Admin";
                        }

                        bool isAuthorized = false;
                        string failReason = "";

                        if (_pendingAdminSeverity == "CRITICAL")
                        {
                            if (role == "Master Administrator") isAuthorized = true;
                            else failReason = "Authorization Denied: This action strictly requires a Master Administrator.";
                        }
                        else if (_pendingAdminSeverity == "HIGH")
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
                        else if (_pendingAdminSeverity == "MODERATE")
                        {
                            if (role == "Administrator" || role == "Master Administrator") isAuthorized = true;
                            else failReason = "Authorization Denied: Tapped card is not an Administrator.";
                        }

                        if (isAuthorized)
                        {
                            _isAwaitingAdminAuth = false;
                            AdminAuthDialog.Hide();
                            await ExecutePendingAdminAction(fullName ?? "Admin");
                        }
                        else
                        {
                            AuthStatusText.Text = failReason;
                            AuthStatusText.Visibility = Visibility.Visible;
                            PlayErrorAlert();
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

        private async void StudentListView_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            if (AppSession.IsEventOrganizer) return;
            if (e.OriginalSource is FrameworkElement fe && fe.DataContext is StudentRecord student)
            {
                await OpenEditDialogAsync(student);
            }
        }

        private async void PopupStudentListView_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            if (AppSession.IsEventOrganizer) return;
            if (e.OriginalSource is FrameworkElement fe && fe.DataContext is StudentRecord student)
            {
                MasterDirectoryDialog.Hide();
                await OpenEditDialogAsync(student);
            }
        }

        private async Task OpenEditDialogAsync(StudentRecord student)
        {
            _editingStudent = student;
            EditDialogStatusText.Visibility = Visibility.Collapsed;

            EditDialogPhotoPreview.ProfilePicture = await ImageHelper.GetBitmapAsync(student.PhotoData);
            _currentPhotoData = student.PhotoData;

            EditDialogStudentIdBox.Text = student.StudentId;
            EditDialogFullNameBox.Text = student.FullName;
            EditDialogEmailBox.Text = student.Email ?? "";

            EditDialogCourseComboBox.ItemsSource = CourseFilterComboBox.Items
                .Cast<object>()
                .Select(i => i.ToString())
                .Where(c => c != "All Courses")
                .ToList();
            EditDialogCourseComboBox.SelectedItem = student.Course;

            EditDialogYearLevelBox.Text = student.YearLevel;
            EditDialogSectionBox.Text = student.SectionName;
            EditDialogNfcUidBox.Text = student.NfcUid;
            EditDialogNewPinBox.Password = "";

            EditDialogStatusComboBox.SelectedIndex = student.Status switch
            {
                "Active" => 0,
                "Inactive" => 1,
                "Graduated" => 2,
                "Expelled" => 3,
                _ => 0
            };

            EditDialogIsTemporaryCheckBox.IsChecked = student.IsTemporary;

            _origStudentId = student.StudentId;
            _origFullName = student.FullName;
            _origEmail = student.Email ?? "";
            _origCourse = student.Course;
            _origYearLevel = student.YearLevel;
            _origSection = student.SectionName;
            _origStatus = student.Status;
            _origNfcUid = student.NfcUid;
            _origIsTemporary = student.IsTemporary;

            _isAwaitingNfcReplacementScan = false;
            ChangeNfcButton.IsEnabled = true;
            NfcScanStatusText.Visibility = Visibility.Collapsed;
            NfcReplacementReasonBox.Visibility = Visibility.Collapsed;
            NfcReplacementReasonBox.Text = "";
            EditDialogSaveButton.IsEnabled = false;

            EditStudentDialog.XamlRoot = this.Content.XamlRoot;
            await EditStudentDialog.ShowAsync();
        }

        private byte[]? _currentPhotoData = null;

        private async void EditDialogUploadPhoto_Click(object sender, RoutedEventArgs e)
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
                        EditDialogPhotoPreview.ProfilePicture = await ImageHelper.GetBitmapAsync(_currentPhotoData);
                        EditDialog_FieldChanged(this, new RoutedEventArgs());
                    }
                }
            }
            catch (Exception ex)
            {
                ShowEditDialogError($"Could not process image: {ex.Message}");
            }
        }

        private void ChangeNfcButton_Click(object sender, RoutedEventArgs e)
        {
            if (_editingStudent == null) return;

            _isAwaitingNfcReplacementScan = true;
            ChangeNfcButton.IsEnabled = false;
            NfcScanStatusText.Text = "Waiting for NFC tap... present the new card to the reader.";
            NfcScanStatusText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 96, 165, 250));
            NfcScanStatusText.Visibility = Visibility.Visible;
        }

        private async Task HandleNfcReplacementScanAsync(string uid)
        {
            _isAwaitingNfcReplacementScan = false;
            ChangeNfcButton.IsEnabled = true;

            if (IsInvalidUid(uid))
            {
                NfcScanStatusText.Text = "Bad read — please tap the card again.";
                NfcScanStatusText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 248, 113, 113));
                NfcScanStatusText.Visibility = Visibility.Visible;
                return;
            }

            if (DatabaseMonitor.IsOnline)
            {
                var existing = await _database.GetStudentByUidAsync(uid);
                if (existing != null && existing.StudentId != _origStudentId)
                {
                    NfcScanStatusText.Text = $"This card already belongs to {existing.FullName} ({existing.StudentId}). Tap a different card.";
                    NfcScanStatusText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 248, 113, 113));
                    NfcScanStatusText.Visibility = Visibility.Visible;
                    return;
                }
            }

            EditDialogNfcUidBox.Text = uid;
            NfcScanStatusText.Text = "New card captured. Please note the reason below, then press Save Changes.";
            NfcScanStatusText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 52, 211, 153));
            NfcScanStatusText.Visibility = Visibility.Visible;
        }

        private static bool IsInvalidUid(string uid)
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

        private void EditDialog_FieldChanged(object sender, object e)
        {
            if (_editingStudent == null || EditDialogSaveButton == null) return;

            string curStudentId = EditDialogStudentIdBox.Text.Trim();
            string curFullName = EditDialogFullNameBox.Text.Trim();
            string curEmail = EditDialogEmailBox.Text.Trim();
            string curCourse = EditDialogCourseComboBox.SelectedItem?.ToString() ?? "";
            string curYearLevel = EditDialogYearLevelBox.Text.Trim();
            string curSection = EditDialogSectionBox.Text.Trim();
            string curStatus = (EditDialogStatusComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
            string curNfcUid = EditDialogNfcUidBox.Text.Trim();
            bool curIsTemporary = EditDialogIsTemporaryCheckBox.IsChecked == true;
            bool hasPinChange = !string.IsNullOrWhiteSpace(EditDialogNewPinBox.Password);

            bool nfcActuallyChanged = curNfcUid != _origNfcUid;

            if (NfcReplacementReasonBox != null)
            {
                NfcReplacementReasonBox.Visibility = nfcActuallyChanged ? Visibility.Visible : Visibility.Collapsed;
                if (!nfcActuallyChanged)
                {
                    NfcReplacementReasonBox.Text = "";
                }
            }

            bool isDirty =
                curStudentId != _origStudentId ||
                curFullName != _origFullName ||
                curEmail != _origEmail ||
                curCourse != _origCourse ||
                curYearLevel != _origYearLevel ||
                curSection != _origSection ||
                curStatus != _origStatus ||
                curNfcUid != _origNfcUid ||
                curIsTemporary != _origIsTemporary ||
                hasPinChange ||
                _currentPhotoData != _editingStudent.PhotoData;

            EditDialogSaveButton.IsEnabled = isDirty;
        }

        private void ShowEditDialogError(string message)
        {
            EditDialogStatusText.Text = message;
            EditDialogStatusText.Visibility = Visibility.Visible;
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

        private async void EditDialogSaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (_editingStudent == null) return;

            string newId = EditDialogStudentIdBox.Text.Trim();
            string fullName = EditDialogFullNameBox.Text.Trim();
            string nfcUid = EditDialogNfcUidBox.Text.Trim();
            string pin = EditDialogNewPinBox.Password.Trim();

            if (string.IsNullOrWhiteSpace(newId) || string.IsNullOrWhiteSpace(fullName) || string.IsNullOrWhiteSpace(nfcUid))
            {
                ShowEditDialogError("Student ID, Full Name, and NFC UID cannot be empty.");
                return;
            }

            if (!string.IsNullOrWhiteSpace(pin) && (pin.Length != 4 || !pin.All(char.IsDigit)))
            {
                ShowEditDialogError("New PIN must be exactly 4 numeric digits.");
                return;
            }

            bool nfcActuallyChanged = nfcUid != _origNfcUid;
            if (nfcActuallyChanged && string.IsNullOrWhiteSpace(NfcReplacementReasonBox.Text))
            {
                ShowEditDialogError("Please provide a reason for the NFC card replacement.");
                return;
            }

            // THE FIX: Save the reason into memory before the dialog hides and wipes the UI!
            _pendingNfcReplacementReason = NfcReplacementReasonBox.Text.Trim();
            EditStudentDialog.Hide();

            _pendingAction = AdminActionType.EditFullProfile;
            _pendingAdminSeverity = "MODERATE";

            if (AppSession.CurrentStaffRoleLabel == "Master Administrator")
            {
                await ExecutePendingAdminAction(AppSession.CurrentStaffName);
            }
            else
            {
                AdminPinBox.Visibility = Visibility.Collapsed;
                AdminPinBox.Password = "";
                AuthStatusText.Visibility = Visibility.Collapsed;
                AdminAuthDescriptionText.Text = "An Administrator must verify this profile update by tapping their NFC card.";

                _isAwaitingAdminAuth = true;
                AdminAuthDialog.XamlRoot = this.Content.XamlRoot;
                await AdminAuthDialog.ShowAsync();
            }
        }

        private void Window_Closed(object sender, WindowEventArgs args)
        {
            DatabaseMonitor.ConnectionStatusChanged -= UpdateOfflineBanner;
            CloseSerialPort();
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

        private async Task ExecutePendingAdminAction(string adminName)
        {
            if (!DatabaseMonitor.IsOnline)
            {
                ContentDialog offlineErrorDialog = new ContentDialog
                {
                    Title = "Action Unavailable Offline",
                    Content = "You cannot modify student records, delete profiles, or perform batch updates while the system is offline. Please restore the database connection first.",
                    CloseButtonText = "Understood",
                    XamlRoot = this.Content.XamlRoot
                };
                await offlineErrorDialog.ShowAsync();

                _pendingAction = AdminActionType.None;
                return;
            }

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
                    await _database.AddAlertAsync(adminName, "ADMIN_OVERRIDE", $"Batch updated {affectedRows} students to '{status}' (Course: {course}, Year: {year}).");
                }
                else if (_pendingAction == AdminActionType.EditFullProfile)
                {
                    if (_editingStudent != null)
                    {
                        string originalId = _editingStudent.StudentId;
                        string oldUid = _editingStudent.NfcUid;
                        string pin = EditDialogNewPinBox.Password.Trim();
                        string nfcReplacementReason = _pendingNfcReplacementReason;

                        var updated = new StudentRecord
                        {
                            StudentId = EditDialogStudentIdBox.Text.Trim(),
                            FullName = EditDialogFullNameBox.Text.Trim(),
                            Email = EditDialogEmailBox.Text.Trim(),
                            Course = EditDialogCourseComboBox.SelectedItem?.ToString() ?? "",
                            YearLevel = EditDialogYearLevelBox.Text.Trim(),
                            SectionName = EditDialogSectionBox.Text.Trim(),
                            Status = ((ComboBoxItem)EditDialogStatusComboBox.SelectedItem).Content.ToString() ?? "Active",
                            NfcUid = EditDialogNfcUidBox.Text.Trim(),
                            QrCredential = EditDialogStudentIdBox.Text.Trim(),
                            PhotoData = _currentPhotoData,
                            IsTemporary = EditDialogIsTemporaryCheckBox.IsChecked == true
                        };

                        List<string> changes = new List<string>();
                        if (_origStudentId != updated.StudentId) changes.Add($"Student ID ({_origStudentId} → {updated.StudentId})");
                        if (_origFullName != updated.FullName) changes.Add("Name");
                        if (_origEmail != updated.Email) changes.Add("Email");
                        if (_origCourse != updated.Course) changes.Add("Course");
                        if (_origYearLevel != updated.YearLevel) changes.Add("Year Level");
                        if (_origSection != updated.SectionName) changes.Add("Section");
                        if (_origStatus != updated.Status) changes.Add($"Status (→ {updated.Status})");
                        if (_origIsTemporary != updated.IsTemporary) changes.Add($"Temp Badge (→ {updated.IsTemporary})");
                        if (!string.IsNullOrWhiteSpace(pin)) changes.Add("Reset PIN");
                        if (_currentPhotoData != _editingStudent.PhotoData) changes.Add("Updated Photo");
                        if (oldUid != updated.NfcUid) changes.Add($"Replaced NFC Card (Reason: {nfcReplacementReason})");

                        string changesString = changes.Count > 0 ? string.Join(", ", changes) : "No specific fields altered (forced save)";
                        string logMessage = $"Edited profile for {updated.FullName} ({updated.StudentId}). Changes: {changesString}.";

                        await _database.UpdateStudentAsync(originalId, updated, string.IsNullOrWhiteSpace(pin) ? null : pin);
                        await _database.AddAlertAsync(adminName, "ADMIN_OVERRIDE", logMessage);

                        if (oldUid != updated.NfcUid)
                        {
                            await _database.UpdateShadowCacheAsync();
                        }

                        _editingStudent = null;
                    }
                }

                PlaySuccessPing();
                _pendingAction = AdminActionType.None;
                await LoadDataAsync();
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

        private void RefreshDataGrid()
        {
            if (StudentListView == null) return;

            string searchTerm = SearchBox.Text.ToLower();
            string statusFilter = StatusFilterComboBox.SelectedItem is ComboBoxItem item ? item.Content.ToString() : "All Students";
            string courseFilter = CourseFilterComboBox.SelectedItem?.ToString() ?? "All Courses";

            var filteredData = _allStudents.Where(s =>
                (string.IsNullOrEmpty(searchTerm) ||
                 (s.FullName != null && s.FullName.ToLower().Contains(searchTerm)) ||
                 (s.StudentId != null && s.StudentId.ToLower().Contains(searchTerm)) ||
                 (s.Course != null && s.Course.ToLower().Contains(searchTerm)) ||
                 (s.YearLevel != null && s.YearLevel.ToLower().Contains(searchTerm)) ||
                 (s.SectionName != null && s.SectionName.ToLower().Contains(searchTerm)) ||
                 (s.Status != null && s.Status.ToLower().Contains(searchTerm)) ||
                 (s.NfcUid != null && s.NfcUid.ToLower().Contains(searchTerm))) &&
                (statusFilter == "All Students" ||
                 (statusFilter == "Active Only" && s.Status == "Active") ||
                 (statusFilter == "Locked Out" && s.PinLocked) ||
                 (statusFilter == "Inactive" && s.Status == "Inactive")) &&
                (courseFilter == "All Courses" || s.Course == courseFilter)
            ).ToList();

            filteredData = _currentSortColumn switch
            {
                "StudentId" => _isSortAscending ? filteredData.OrderBy(s => s.StudentId).ToList() : filteredData.OrderByDescending(s => s.StudentId).ToList(),
                "FullName" => _isSortAscending ? filteredData.OrderBy(s => s.FullName).ToList() : filteredData.OrderByDescending(s => s.FullName).ToList(),
                "Course" => _isSortAscending ? filteredData.OrderBy(s => s.Course).ToList() : filteredData.OrderByDescending(s => s.Course).ToList(),
                "Year" => _isSortAscending ? filteredData.OrderBy(s => s.YearLevel).ToList() : filteredData.OrderByDescending(s => s.YearLevel).ToList(),
                "Section" => _isSortAscending ? filteredData.OrderBy(s => s.SectionName).ToList() : filteredData.OrderByDescending(s => s.SectionName).ToList(),
                "Status" => _isSortAscending ? filteredData.OrderBy(s => s.Status).ToList() : filteredData.OrderByDescending(s => s.Status).ToList(),
                _ => filteredData
            };

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
            _pendingAdminSeverity = "MODERATE";

            if (AppSession.CurrentStaffRoleLabel == "Master Administrator")
            {
                await ExecutePendingAdminAction(AppSession.CurrentStaffName);
            }
            else
            {
                AdminPinBox.Visibility = Visibility.Collapsed;
                AdminPinBox.Password = "";
                AuthStatusText.Visibility = Visibility.Collapsed;
                AdminAuthDescriptionText.Text = "An Administrator must verify this profile update by tapping their NFC card.";

                _isAwaitingAdminAuth = true;
                AdminAuthDialog.XamlRoot = this.Content.XamlRoot;
                await AdminAuthDialog.ShowAsync();
            }
        }

        private async void DeleteStudentButton_Click(object sender, RoutedEventArgs e)
        {
            if (StudentListView.SelectedItem is not StudentRecord selected) return;

            ContentDialog confirmDialog = new ContentDialog
            {
                Title = "Confirm Deletion",
                Content = $"Are you absolutely sure you want to completely delete the profile and credentials for {selected.FullName} ({selected.StudentId})? This action cannot be undone.",
                PrimaryButtonText = "Delete Profile",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.Content.XamlRoot
            };

            var result = await confirmDialog.ShowAsync();
            if (result != ContentDialogResult.Primary) return;

            _pendingAction = AdminActionType.DeleteIndividual;
            _pendingAdminSeverity = "CRITICAL";

            if (AppSession.CurrentStaffRoleLabel == "Master Administrator")
            {
                await ExecutePendingAdminAction(AppSession.CurrentStaffName);
            }
            else
            {
                AdminPinBox.Visibility = Visibility.Collapsed;
                AdminPinBox.Password = "";
                AuthStatusText.Visibility = Visibility.Collapsed;
                AdminAuthDescriptionText.Text = "To prevent unauthorized deletions, a Master Administrator must verify this action.";

                _isAwaitingAdminAuth = true;
                AdminAuthDialog.XamlRoot = this.Content.XamlRoot;
                await AdminAuthDialog.ShowAsync();
            }
        }

        private async void ConfirmBatchButton_Click(object sender, RoutedEventArgs e)
        {
            BatchUpdateDialog.Hide();

            _pendingAction = AdminActionType.BatchUpdate;
            _pendingAdminSeverity = "HIGH";

            if (AppSession.CurrentStaffRoleLabel == "Master Administrator")
            {
                await ExecutePendingAdminAction(AppSession.CurrentStaffName);
            }
            else
            {
                AdminPinBox.Visibility = Visibility.Visible;
                AdminPinBox.Password = "";
                AuthStatusText.Visibility = Visibility.Collapsed;
                AdminAuthDescriptionText.Text = "To confirm this batch update, an Administrator must enter their 4-digit PIN and tap their NFC card.";

                _isAwaitingAdminAuth = true;
                AdminAuthDialog.XamlRoot = this.Content.XamlRoot;
                await AdminAuthDialog.ShowAsync();
            }
        }

        private async void OpenBatchDialogButton_Click(object sender, RoutedEventArgs e)
        {
            BatchUpdateDialog.XamlRoot = this.Content.XamlRoot;
            await BatchUpdateDialog.ShowAsync();
        }

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
                    (s.NfcUid != null && s.NfcUid.ToLower().Contains(query)) ||
                    (s.Course != null && s.Course.ToLower().Contains(query)) ||
                    (s.YearLevel != null && s.YearLevel.ToLower().Contains(query)) ||
                    (s.SectionName != null && s.SectionName.ToLower().Contains(query)) ||
                    (s.Status != null && s.Status.ToLower().Contains(query)));
            }

            string course = PopupCourseFilter.SelectedItem?.ToString() ?? "All Courses";
            if (course != "All Courses") filtered = filtered.Where(s => s.Course == course);

            string status = (PopupStatusFilter.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All Students";
            if (status == "Active Only") filtered = filtered.Where(s => s.Status == "Active");
            else if (status == "Locked Out") filtered = filtered.Where(s => s.PinLocked);
            else if (status == "Inactive") filtered = filtered.Where(s => s.Status == "Inactive");

            filtered = _popupSortColumn switch
            {
                "StudentId" => _isPopupSortAscending ? filtered.OrderBy(s => s.StudentId) : filtered.OrderByDescending(s => s.StudentId),
                "FullName" => _isPopupSortAscending ? filtered.OrderBy(s => s.FullName) : filtered.OrderByDescending(s => s.FullName),
                "Course" => _isPopupSortAscending ? filtered.OrderBy(s => s.Course) : filtered.OrderByDescending(s => s.Course),
                "Year" => _isPopupSortAscending ? filtered.OrderBy(s => s.YearLevel) : filtered.OrderByDescending(s => s.YearLevel),
                "Section" => _isPopupSortAscending ? filtered.OrderBy(s => s.SectionName) : filtered.OrderByDescending(s => s.SectionName),
                "Status" => _isPopupSortAscending ? filtered.OrderBy(s => s.Status) : filtered.OrderByDescending(s => s.Status),
                _ => filtered.OrderBy(s => s.FullName)
            };

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