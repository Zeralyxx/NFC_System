using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.IO.Ports;
using WinRT.Interop;
using System.Media;

namespace NFC_System
{
    public sealed partial class VerificationWindow : Window
    {
        private readonly DatabaseService _database = new();
        private readonly VerificationEngine _engine;
        private SerialPort? _serialPort;

        public VerificationWindow()
        {
            this.InitializeComponent();
            _engine = new VerificationEngine(_database);

            MaximizeWindow();
            this.Closed += Window_Closed;

            _ = InitializeAsync();
            TryConnectSerial("COM3"); // CHANGE this to your actual Arduino COM port
        }

        private async System.Threading.Tasks.Task InitializeAsync()
        {
            try
            {
                await _database.EnsureSchemaAsync();

                // 1. Sync the dropdown with the global memory to prevent it reverting to Entry
                DirectionComboBox.SelectedIndex = KioskStateController.CurrentType == TransactionType.Entry ? 0 : 1;

                // 2. Sync the Security Mode
                string savedMode = await _database.GetSettingAsync("verification_mode", "Standard");
                SecurityModeComboBox.SelectedIndex = savedMode switch
                {
                    "Fast" => 0,
                    "High-Security" => 2,
                    _ => 1
                };

                VerificationLogListView.Items.Insert(0, "[INFO] Risk-based verification engine ready.");
            }
            catch (Exception ex)
            {
                VerificationLogListView.Items.Insert(0, $"[DB ERROR] {ex.Message}");
            }
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            CloseSerialPort();

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

        private void Window_Closed(object sender, WindowEventArgs args)
        {
            CloseSerialPort();
        }

        private void LaunchKioskButton_Click(object sender, RoutedEventArgs e)
        {
            // 1. RELEASE THE HARDWARE PORT FIRST
            CloseSerialPort();

            // 2. Retrieve currently selected security rules to pass to the Kiosk
            var mode = (SecurityModeComboBox.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "Standard";
            var type = (DirectionComboBox.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "Entry";

            // 3. Launch the fullscreen kiosk, passing current configurations
            var kiosk = new KioskModeWindow("Gate", $"{type} ({mode})");
            kiosk.Activate();

            // 4. Close the Guard Window so they don't fight over the COM port
            this.Close();
        }

        // Resets the attempts of the student
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
                // Resets their failed attempts to 0 and unlocks the account
                await _database.UpdatePinFailureAsync(studentId, 0, false);

                VerificationLogListView.Items.Insert(0, $"[SECURITY OVERRIDE] Guard cleared 2FA lockout for {studentId}.");
                OverrideStudentIdBox.Text = ""; // Clear box
            }
            catch (Exception ex)
            {
                VerificationLogListView.Items.Insert(0, $"[DB ERROR] Could not unlock account: {ex.Message}");
            }
        }

        private void TryConnectSerial(string portName)
        {
            try
            {
                _serialPort = new SerialPort(portName, 115200);
                _serialPort.NewLine = "\n";
                _serialPort.DataReceived += SerialPort_DataReceived;
                _serialPort.Open();

                VerificationLogListView.Items.Insert(0, $"[INFO] Connected to {portName}");
            }
            catch (Exception ex)
            {
                VerificationLogListView.Items.Insert(0, $"[ERROR] Could not connect: {ex.Message}");
            }
        }

        private async void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                if (_serialPort == null || !_serialPort.IsOpen)
                {
                    return;
                }

                string line = _serialPort.ReadLine().Trim();
                if (line.StartsWith("UID="))
                {
                    string uid = line.Substring(4).Trim();
                    await DispatcherQueue.TryEnqueueAsync(() => ProcessUidAsync(uid));
                }
            }
            catch (Exception ex)
            {
                await DispatcherQueue.TryEnqueueAsync(() =>
                {
                    VerificationLogListView.Items.Insert(0, $"[ERROR] {ex.Message}");
                });
            }
        }

        private async System.Threading.Tasks.Task ProcessUidAsync(string uid)
        {
            if (string.IsNullOrWhiteSpace(uid))
            {
                VerificationLogListView.Items.Insert(0, "[ERROR] NFC UID is required.");
                return;
            }

            if (IsInvalidUid(uid))
            {
                await LogInvalidUidAsync(uid, GetSelectedTransactionType(), GetSelectedMode());
                DisplayInvalidUid(uid);
                return;
            }

            TransactionType transactionType = GetSelectedTransactionType();

            try
            {
                VerificationOutcome outcome = await _engine.BeginNfcVerificationAsync(
                    uid,
                    GetSelectedMode(),
                    transactionType,
                    "");

                ApplyOutcome(outcome);
            }
            catch (Exception ex)
            {
                VerificationLogListView.Items.Insert(0, $"[DB ERROR] {ex.Message}");
            }
        }

        private void ApplyOutcome(VerificationOutcome outcome)
        {
            if (outcome.Student != null)
            {
                // Auto-fill the Guard Override box so they don't have to type it!
                OverrideStudentIdBox.Text = outcome.Student.StudentId;
            }
            else
            {
                OverrideStudentIdBox.Text = ""; // Clear it if no valid student was found
            }

            if (!string.IsNullOrWhiteSpace(outcome.LogLine))
            {
                VerificationLogListView.Items.Insert(0, outcome.LogLine);
            }

            // Play a loud system alarm if the student locks themselves out
            if (outcome.ErrorCategory == "PIN_LOCKED")
            {
                SystemSounds.Exclamation.Play();
            }
        }

        private void DisplayInvalidUid(string uid)
        {
            OverrideStudentIdBox.Text = "";
            string logTime = DateTime.Now.ToString("yyyy-MM-dd hh:mm:ss tt");
            VerificationLogListView.Items.Insert(0, $"{logTime} | UID {uid} | INVALID READ");
        }

        private async System.Threading.Tasks.Task LogInvalidUidAsync(string uid, TransactionType transactionType, VerificationMode mode)
        {
            try
            {
                await _database.LogVerificationAsync(null, uid, transactionType, mode, false, "INVALID_NFC_READ", "INVALID_UID", "Invalid or corrupted NFC UID read was rejected before verification.");
                await _database.AddAlertAsync(null, "INVALID_UID", $"Invalid NFC UID read rejected: {uid}.");
            }
            catch (Exception ex)
            {
                VerificationLogListView.Items.Insert(0, $"[DB ERROR] Could not log invalid UID: {ex.Message}");
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

            try { await _database.SetSettingAsync("verification_mode", modeString); } catch { }
        }

        private void DirectionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SecurityModeComboBox == null || DirectionComboBox == null) return;
            KioskStateController.BroadcastModeChange(GetSelectedMode(), GetSelectedTransactionType());
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

        private static bool IsInvalidUid(string uid)
        {
            if (string.IsNullOrWhiteSpace(uid))
            {
                return true;
            }

            string[] parts = uid.Split(':');
            if (parts.Length != 4 && parts.Length != 7)
            {
                return true;
            }

            bool allZero = true;
            foreach (string part in parts)
            {
                if (part != "00")
                {
                    allZero = false;
                    break;
                }
            }

            if (allZero)
            {
                return true;
            }

            if (parts.Length >= 4)
            {
                int start = parts.Length - 4;
                bool trailingZeros = true;
                for (int i = start; i < parts.Length; i++)
                {
                    if (parts[i] != "00")
                    {
                        trailingZeros = false;
                        break;
                    }
                }

                if (trailingZeros)
                {
                    return true;
                }
            }

            return false;
        }

        private void CloseSerialPort()
        {
            try
            {
                if (_serialPort != null)
                {
                    if (_serialPort.IsOpen)
                    {
                        _serialPort.DataReceived -= SerialPort_DataReceived;
                        _serialPort.Close();
                    }

                    _serialPort.Dispose();
                    _serialPort = null;
                }
            }
            catch
            {
                // Closing the app should not be blocked by serial cleanup.
            }
        }
    }
}