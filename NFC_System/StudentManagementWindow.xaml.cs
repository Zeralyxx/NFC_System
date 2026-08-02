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
using Microsoft.UI.Xaml.Input; // for DoubleTappedRoutedEventArgs

namespace NFC_System
{
    public sealed partial class StudentManagementWindow : Window
    {
        private enum AdminActionType { None, SaveIndividual, DeleteIndividual, BatchUpdate, EditFullProfile }

        private readonly DatabaseService _database = new();
        private List<StudentRecord> _allStudents = new();
        private int _currentPage = 1;
        private const int PageSize = 10;

        // Serial Port objects to listen for the Admin Tap
        private SerialPort? _serialPort;
        private bool _isAwaitingAdminAuth = false;
        private bool _isAwaitingNfcReplacementScan = false;
        private StudentRecord? _editingStudent = null;

        // Snapshot of original values to detect unsaved changes
        private string _origStudentId = "";
        private string _origFullName = "";
        private string _origEmail = ""; // <-- Add this
        private string _origCourse = "";
        private string _origYearLevel = "";
        private string _origSection = "";
        private string _origStatus = "";
        private string _origNfcUid = "";
        private AdminActionType _pendingAction = AdminActionType.None;

        public StudentManagementWindow()
        {
            this.InitializeComponent();
            MaximizeWindow();
            this.Closed += Window_Closed;
            StudentListView.DoubleTapped += StudentListView_DoubleTapped;
            PopupStudentListView.DoubleTapped += PopupStudentListView_DoubleTapped;

            EditDialogStudentIdBox.TextChanged += EditDialog_FieldChanged;
            EditDialogFullNameBox.TextChanged += EditDialog_FieldChanged;
            EditDialogEmailBox.TextChanged += EditDialog_FieldChanged; // <-- Add this
            EditDialogCourseComboBox.SelectionChanged += EditDialog_FieldChanged;
            EditDialogYearLevelBox.TextChanged += EditDialog_FieldChanged;
            EditDialogSectionBox.TextChanged += EditDialog_FieldChanged;
            EditDialogStatusComboBox.SelectionChanged += EditDialog_FieldChanged;
            EditDialogNfcUidBox.TextChanged += EditDialog_FieldChanged;
            EditDialogNewPinBox.PasswordChanged += EditDialog_FieldChanged;

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
                var result = await _database.SearchStudentsAsync("", "All Students", "All Courses", "All Years", 1, 99999);
                _allStudents = result.Students.ToList();
                var courses = await _database.GetDistinctCoursesAsync();


                CourseFilterComboBox.Items.Clear();
                CourseFilterComboBox.Items.Add("All Courses");
                PopupCourseFilter.Items.Clear();
                PopupCourseFilter.Items.Add("All Courses");

                foreach (var course in courses)
                {
                    CourseFilterComboBox.Items.Add(course);
                    PopupCourseFilter.Items.Add(course);
                }

                CourseFilterComboBox.SelectedIndex = 0;
                PopupCourseFilter.SelectedIndex = 0;

                BatchCourseComboBox.Items.Clear();
                BatchCourseComboBox.Items.Add("All Courses");
                foreach (var course in courses) BatchCourseComboBox.Items.Add(course);
                BatchCourseComboBox.SelectedIndex = 0;

                // Hook up the hardware reader so we can intercept the auth tap
                string nfcPort = await _database.GetSettingAsync("nfc_com_port", "COM3");
                TryConnectSerial(nfcPort);

                RefreshDataGrid();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DB ERROR] {ex.Message}");
            }
        }

        // ====================================================================
        // THE FIX: NATIVE HARDWARE SUCCESS CHIME
        // ====================================================================
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
                        // A highly satisfying, rapid ascending major chord (C6 -> E6 -> G6)
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

        // ====================================================================
        // SERIAL PORT RBAC LISTENER LOGIC
        // ====================================================================
        private void TryConnectSerial(string portName)
        {
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
                        var details = await _database.GetStaffDetailsAsync(uid);

                        if (details.Role == "Administrator" || details.Role == "Master Administrator")
                        {
                            _isAwaitingAdminAuth = false;
                            AdminAuthDialog.Hide();
                            await ExecutePendingAdminAction(details.FullName ?? "Admin");
                        }
                        else
                        {
                            AuthStatusText.Text = "Authorization Denied: Tapped card is not an Administrator.";
                            AuthStatusText.Visibility = Visibility.Visible;
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
            if (e.OriginalSource is FrameworkElement fe && fe.DataContext is StudentRecord student)
            {
                await OpenEditDialogAsync(student);
            }
        }

        private async void PopupStudentListView_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
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

            EditDialogStudentIdBox.Text = student.StudentId;
            EditDialogFullNameBox.Text = student.FullName;
            EditDialogEmailBox.Text = student.Email ?? ""; // <-- Add this

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

            // Snapshot original state for dirty-checking
            _origStudentId = student.StudentId;
            _origFullName = student.FullName;
            _origEmail = student.Email ?? ""; // <-- Add this
            _origCourse = student.Course;
            _origYearLevel = student.YearLevel;
            _origSection = student.SectionName;
            _origStatus = student.Status;
            _origNfcUid = student.NfcUid;

            _isAwaitingNfcReplacementScan = false;
            ChangeNfcButton.IsEnabled = true;
            NfcScanStatusText.Visibility = Visibility.Collapsed;
            NfcReplacementReasonBox.Visibility = Visibility.Collapsed;
            NfcReplacementReasonBox.Text = "";
            EditDialogSaveButton.IsEnabled = false;

            EditStudentDialog.XamlRoot = this.Content.XamlRoot;
            await EditStudentDialog.ShowAsync();
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

            var existing = await _database.GetStudentByUidAsync(uid);
            if (existing != null && existing.StudentId != _origStudentId)
            {
                NfcScanStatusText.Text = $"This card already belongs to {existing.FullName} ({existing.StudentId}). Tap a different card.";
                NfcScanStatusText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 248, 113, 113));
                NfcScanStatusText.Visibility = Visibility.Visible;
                return;
            }

            EditDialogNfcUidBox.Text = uid; // fires EditDialog_FieldChanged, which reveals the reason box
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
            string curEmail = EditDialogEmailBox.Text.Trim(); // <-- Add this
            string curCourse = EditDialogCourseComboBox.SelectedItem?.ToString() ?? "";
            string curYearLevel = EditDialogYearLevelBox.Text.Trim();
            string curSection = EditDialogSectionBox.Text.Trim();
            string curStatus = (EditDialogStatusComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
            string curNfcUid = EditDialogNfcUidBox.Text.Trim();
            bool hasPinChange = !string.IsNullOrWhiteSpace(EditDialogNewPinBox.Password);

            bool nfcActuallyChanged = curNfcUid != _origNfcUid;

            // Reveal/hide the reason field based purely on whether the UID differs from original,
            // regardless of whether it came from a tap or manual typing.
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
                curEmail != _origEmail || // <-- Add this
                curCourse != _origCourse ||
                curYearLevel != _origYearLevel ||
                curSection != _origSection ||
                curStatus != _origStatus ||
                curNfcUid != _origNfcUid ||
                hasPinChange;

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

            EditStudentDialog.Hide();
            _pendingAction = AdminActionType.EditFullProfile;

            if (AppSession.CurrentStaffRoleLabel == "Master Admin")
            {
                await ExecutePendingAdminAction(AppSession.CurrentStaffName);
            }
            else
            {
                _isAwaitingAdminAuth = true;
                AuthStatusText.Visibility = Visibility.Collapsed;
                AdminAuthDialog.XamlRoot = this.Content.XamlRoot;
                await AdminAuthDialog.ShowAsync();
            }
        }

        private void Window_Closed(object sender, WindowEventArgs args)
        {
            CloseSerialPort();
        }

        // ====================================================================
        // EXECUTING THE LOCKED ADMIN ACTIONS
        // ====================================================================
        private async Task ExecutePendingAdminAction(string adminName)
        {
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

                    // Add the specific master admin name to the batch log
                    await _database.AddAlertAsync(adminName, "ADMIN_OVERRIDE", $"Batch updated {affectedRows} students to '{status}' (Course: {course}, Year: {year}).");
                }
                else if (_pendingAction == AdminActionType.EditFullProfile)
                {
                    if (_editingStudent != null)
                    {
                        string originalId = _editingStudent.StudentId;
                        string oldUid = _editingStudent.NfcUid;
                        string pin = EditDialogNewPinBox.Password.Trim();
                        string nfcReplacementReason = NfcReplacementReasonBox.Text.Trim();

                        var updated = new StudentRecord
                        {
                            StudentId = EditDialogStudentIdBox.Text.Trim(),
                            FullName = EditDialogFullNameBox.Text.Trim(),
                            Email = EditDialogEmailBox.Text.Trim(), // <-- Add this
                            Course = EditDialogCourseComboBox.SelectedItem?.ToString() ?? "",
                            YearLevel = EditDialogYearLevelBox.Text.Trim(),
                            SectionName = EditDialogSectionBox.Text.Trim(),
                            Status = ((ComboBoxItem)EditDialogStatusComboBox.SelectedItem).Content.ToString() ?? "Active",
                            NfcUid = EditDialogNfcUidBox.Text.Trim(),
                            QrCredential = EditDialogStudentIdBox.Text.Trim() // keep QR aligned to Student ID
                        };

                        await _database.UpdateStudentAsync(originalId, updated, string.IsNullOrWhiteSpace(pin) ? null : pin);

                        bool nfcChanged = oldUid != updated.NfcUid;
                        string logMessage = nfcChanged
                            ? $"Edited full profile for {updated.FullName} ({originalId} → {updated.StudentId}). NFC card replaced — reason: {nfcReplacementReason}."
                            : $"Edited full profile for {updated.FullName} ({originalId} → {updated.StudentId}).";

                        await _database.AddAlertAsync(adminName, "ADMIN_OVERRIDE", logMessage);

                        if (nfcChanged)
                        {
                            // Push the shadow cache refresh now instead of waiting up to 5 minutes
                            // for the background timer, so the old card stops working immediately
                            // even on kiosks currently running offline.
                            await _database.UpdateShadowCacheAsync();
                        }

                        _editingStudent = null;
                    }
                }

                // Play the success sound upon successful execution
                PlaySuccessPing();

                // Clean up and refresh
                _pendingAction = AdminActionType.None;
                await LoadDataAsync(); // Fetch latest DB state
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

        // ====================================================================
        // UI BINDINGS & CLICKS
        // ====================================================================

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

            // Master Admins bypass the Sudo prompt entirely
            if (AppSession.CurrentStaffRoleLabel == "Master Admin")
            {
                await ExecutePendingAdminAction(AppSession.CurrentStaffName);
            }
            else
            {
                _isAwaitingAdminAuth = true;
                AuthStatusText.Visibility = Visibility.Collapsed;
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

            // Master Admins bypass the Sudo prompt entirely
            if (AppSession.CurrentStaffRoleLabel == "Master Admin")
            {
                await ExecutePendingAdminAction(AppSession.CurrentStaffName);
            }
            else
            {
                _isAwaitingAdminAuth = true;
                AuthStatusText.Visibility = Visibility.Collapsed;
                AdminAuthDialog.XamlRoot = this.Content.XamlRoot;
                await AdminAuthDialog.ShowAsync();
            }
        }

        private async void ConfirmBatchButton_Click(object sender, RoutedEventArgs e)
        {
            BatchUpdateDialog.Hide(); // Hide the config menu before popping the Sudo menu

            _pendingAction = AdminActionType.BatchUpdate;

            // Master Admins bypass the Sudo prompt entirely
            if (AppSession.CurrentStaffRoleLabel == "Master Admin")
            {
                await ExecutePendingAdminAction(AppSession.CurrentStaffName);
            }
            else
            {
                _isAwaitingAdminAuth = true;
                AuthStatusText.Visibility = Visibility.Collapsed;
                AdminAuthDialog.XamlRoot = this.Content.XamlRoot;
                await AdminAuthDialog.ShowAsync();
            }
        }

        private async void OpenBatchDialogButton_Click(object sender, RoutedEventArgs e)
        {
            BatchUpdateDialog.XamlRoot = this.Content.XamlRoot;
            await BatchUpdateDialog.ShowAsync();
        }

        // --- ENLARGE POPUP DIRECTORY LOGIC ---
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
                    (s.NfcUid != null && s.NfcUid.ToLower().Contains(query)));
            }

            string course = PopupCourseFilter.SelectedItem?.ToString() ?? "All Courses";
            if (course != "All Courses") filtered = filtered.Where(s => s.Course == course);

            string status = (PopupStatusFilter.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All Students";
            if (status == "Active Only") filtered = filtered.Where(s => s.Status == "Active");
            else if (status == "Locked Out") filtered = filtered.Where(s => s.PinLocked);
            else if (status == "Inactive") filtered = filtered.Where(s => s.Status == "Inactive");

            filtered = filtered.OrderBy(s => s.FullName);
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