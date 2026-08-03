using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.Devices;
using Windows.Media.MediaProperties;
using Windows.Storage.Streams;
using WinRT.Interop;
using ZXing;
using ZXing.Common;

namespace NFC_System
{
    public enum AuthenticationStage
    {
        Idle,
        NFCVerified,
        WaitingForPIN,
        PINVerified,
        WaitingForQR,
        AccessGranted,
        AccessDenied
    }

    public sealed partial class KioskModeWindow : Window
    {
        public static event Action<VerificationOutcome>? OnKioskOutcome;
        public static event Action<string>? OnKioskLog;

        private readonly string _originatingMode;
        private readonly string _contextDetails;
        private readonly string? _eventId;

        private SerialPort? _serialPort;
        private readonly DatabaseService _database = new();
        private readonly VerificationEngine _engine;
        private VerificationSession? _activeSession;

        private VerificationMode _currentMode = VerificationMode.HighSecurity;
        private AuthenticationStage _currentStage = AuthenticationStage.Idle;
        private readonly DispatcherTimer _inactivityTimer = new();

        private MediaFrameReader? _frameReader;
        private readonly SoftwareBitmapSource _previewSource = new();
        private MediaCapture? _mediaCapture;
        private bool _isDecoding;
        private bool _isDisposingCamera;
        private bool _isClosing;

        private DateTime _lastFrameProcessTime = DateTime.MinValue;
        private DateTime _lastPreviewTime = DateTime.MinValue;
        private bool _isUpdatingPreview = false;

        private string _lastScannedQr = string.Empty;
        // OFFLINE CACHE TIMERS
        private readonly DispatcherTimer _shadowCacheTimer = new();
        private readonly DispatcherTimer _syncRecoveryTimer = new();
        private DateTime _lastQrScanTime = DateTime.MinValue;

        private static readonly HashSet<string> _eventAttendanceCache = new();
        private static string? _lastTrackedEventId = null;

        // 1. FAST ZXING CONFIGURATION (Optimized for upright QR codes on phone screens)
        private readonly BarcodeReaderGeneric _barcodeReader = new()
        {
            AutoRotate = false, // Set to false to save 70% CPU processing time
            Options = new DecodingOptions
            {
                PossibleFormats = new List<BarcodeFormat> { BarcodeFormat.QR_CODE },
                TryHarder = false, // Set to false for instant reading of clear screen QR codes
                PureBarcode = false
            }
        };

        private string _currentPinBuffer = "";
        private bool _isVerifyingPin = false;

        private string _tempStudentName = "";
        private string _tempStudentId = "";
        private string _outcomeTitle = "";
        private string _outcomeMessage = "";

        public KioskModeWindow(string originatingMode, string contextDetails, string? eventId = null)
        {
            this.InitializeComponent();
            _originatingMode = originatingMode;
            _contextDetails = contextDetails;
            _eventId = eventId;

            if (_eventId != _lastTrackedEventId)
            {
                _eventAttendanceCache.Clear();
                _lastTrackedEventId = _eventId;
            }

            _engine = new VerificationEngine(_database);

            EnforceFullScreenMode();
            ApplyDynamicHeader();

            _inactivityTimer.Interval = TimeSpan.FromSeconds(30);
            _inactivityTimer.Tick += InactivityTimer_Tick;

            Closed += KioskModeWindow_Closed;
            _ = InitializeCameraAsync();
            _ = SyncOperationalModeAsync();

            // --------------------------------------------------------
            // OFFLINE CACHE CONFIGURATION
            // --------------------------------------------------------

            // 1. Silent Background Pull (Every 5 Minutes)
            _shadowCacheTimer.Interval = TimeSpan.FromMinutes(5);
            _shadowCacheTimer.Tick += async (s, e) => await _database.UpdateShadowCacheAsync();
            _shadowCacheTimer.Start();

            // 2. Silent Recovery Push (Checks Every 5 Seconds)
            _syncRecoveryTimer.Interval = TimeSpan.FromSeconds(5);
            _syncRecoveryTimer.Tick += async (s, e) =>
            {
                if (OfflineCacheService.HasPendingLogs())
                {
                    if (await _database.TestConnectionAsync())
                    {
                        await _database.SyncOfflineLogsToServerAsync();
                    }
                }
            };
            _syncRecoveryTimer.Start();

            KioskStateController.ModeChanged += KioskStateController_ModeChanged;
        }

        // ====================================================================
        // NATIVE HARDWARE AUDIO FEEDBACK
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
                        Console.Beep(1046, 75);  // C6
                        System.Threading.Thread.Sleep(15);
                        Console.Beep(1318, 75);  // E6
                        System.Threading.Thread.Sleep(15);
                        Console.Beep(1568, 200); // G6 (Held longer for the "ping" resolution)
                    }
                }
                catch { }
            });
        }

        private void PlaySecurityAlert()
        {
            Task.Run(() =>
            {
                for (int i = 0; i < 3; i++)
                {
                    Console.Beep(2500, 300);
                    System.Threading.Thread.Sleep(100);
                }
            });
        }

        // NEW: Fast, responsive beep for hardware keypad input
        private void PlayKeypadBeep()
        {
            Task.Run(() =>
            {
                try
                {
                    Console.Beep(800, 60);
                }
                catch { }
            });
        }

        private void LogPerformanceMetric(string operation, double elapsedMs, string result)
        {
            Task.Run(() =>
            {
                try
                {
                    string logDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
                    Directory.CreateDirectory(logDirectory);
                    string logFile = Path.Combine(logDirectory, "System_Performance_Metrics.csv");

                    bool isNewFile = !File.Exists(logFile);
                    using var writer = new StreamWriter(logFile, true);

                    if (isNewFile)
                    {
                        writer.WriteLine("Timestamp,Operation,Elapsed Time (ms),Result");
                    }

                    writer.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff},\"{operation}\",{elapsedMs},\"{result}\"");
                }
                catch { }
            });
        }

        private void InactivityTimer_Tick(object? sender, object e)
        {
            _inactivityTimer.Stop();
            SetState(AuthenticationStage.Idle);
        }

        private void KioskStateController_ModeChanged(VerificationMode newMode, TransactionType newType)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                _currentMode = newMode;
                string modeText = newMode == VerificationMode.Fast ? "Fast" :
                                  newMode == VerificationMode.Standard ? "Standard" : "High-Security";

                KioskHeaderSubtitle.Text = $"GATE TERMINAL  •  {newType.ToString().ToUpper()} ({modeText})";
                SetState(AuthenticationStage.Idle);
            });
        }

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

                if (line.StartsWith("UID="))
                {
                    string uid = line.Substring(4).Trim();
                    if (_currentStage == AuthenticationStage.Idle)
                    {
                        DispatcherQueue.TryEnqueue(() => ProcessNfcScan(uid));
                    }
                }
                else if (line.StartsWith("KEY="))
                {
                    string key = line.Substring(4).Trim().ToUpper();

                    DispatcherQueue.TryEnqueue(() =>
                    {
                        if (key.Contains("D"))
                        {
                            if (_currentStage == AuthenticationStage.Idle || _currentStage == AuthenticationStage.WaitingForPIN)
                            {
                                ForgotIdButton_Click(this, new RoutedEventArgs());
                            }
                            return;
                        }

                        if (_currentStage == AuthenticationStage.WaitingForPIN && !string.IsNullOrEmpty(key))
                        {
                            bool isEnter = key.Contains("A");
                            bool isClear = key.Contains("B");
                            bool isCancel = key.Contains("C");

                            string digit = "";
                            foreach (char c in key)
                            {
                                if (char.IsDigit(c))
                                {
                                    digit = c.ToString();
                                    break;
                                }
                            }

                            ProcessHardwareKeypadStroke(digit, isEnter, isClear, isCancel);
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

        private async Task SyncOperationalModeAsync()
        {
            try
            {
                if (_originatingMode == "Event")
                {
                    _currentMode = KioskStateController.CurrentMode;

                    if (_currentMode == VerificationMode.Fast) DebugSecurityLevel.SelectedIndex = 0;
                    else if (_currentMode == VerificationMode.HighSecurity) DebugSecurityLevel.SelectedIndex = 2;
                    else DebugSecurityLevel.SelectedIndex = 1;
                }
                else
                {
                    string savedMode = await _database.GetSettingAsync("verification_mode", "Standard");

                    if (savedMode == "Fast") { _currentMode = VerificationMode.Fast; DebugSecurityLevel.SelectedIndex = 0; }
                    else if (savedMode == "High-Security") { _currentMode = VerificationMode.HighSecurity; DebugSecurityLevel.SelectedIndex = 2; }
                    else { _currentMode = VerificationMode.Standard; DebugSecurityLevel.SelectedIndex = 1; }
                }

                string nfcPort = await _database.GetSettingAsync("nfc_com_port", "COM3");
                TryConnectSerial(nfcPort);
            }
            catch
            {
                _currentMode = VerificationMode.Standard;
                DebugSecurityLevel.SelectedIndex = 1;
                TryConnectSerial("COM3");
            }
            SetState(AuthenticationStage.Idle);
        }

        private void KioskModeWindow_Closed(object sender, WindowEventArgs args)
        {
            _isClosing = true;
            _inactivityTimer.Stop();
            _shadowCacheTimer.Stop();
            _syncRecoveryTimer.Stop();

            // 1. Force LEDs OFF before tearing down serial connection
            SetHardwareLeds(false);

            // 2. Small delay (50ms) to ensure the serial buffer pushes "LED=OFF\n" to the ESP32
            System.Threading.Thread.Sleep(50);

            CloseSerialPort();
            _ = DisposeCameraAsync();
            KioskStateController.ModeChanged -= KioskStateController_ModeChanged;
        }

        private void ExitKiosk_Click(object sender, RoutedEventArgs e)
        {
            _isClosing = true;
            _inactivityTimer.Stop();
            _shadowCacheTimer.Stop();
            _syncRecoveryTimer.Stop();

            // 1. Force LEDs OFF before tearing down serial connection
            SetHardwareLeds(false);

            // 2. Small delay (50ms) to ensure the serial buffer pushes "LED=OFF\n" to the ESP32
            System.Threading.Thread.Sleep(50);

            CloseSerialPort();
            _ = DisposeCameraAsync();
            this.Close();
        }

        private void ForgotIdButton_Click(object sender, RoutedEventArgs e)
        {
            _tempStudentName = "";
            _tempStudentId = "";
            _activeSession = null;
            SetState(AuthenticationStage.WaitingForQR);
        }

        private void RegisterSuccessfulEntry()
        {
            if (_activeSession?.TransactionType == TransactionType.EventAttendance)
            {
                if (!string.IsNullOrEmpty(_activeSession.Uid)) _eventAttendanceCache.Add(_activeSession.Uid);
                if (_activeSession.Student != null && !string.IsNullOrEmpty(_activeSession.Student.StudentId))
                    _eventAttendanceCache.Add(_activeSession.Student.StudentId);
            }
            else if (_activeSession?.TransactionType == TransactionType.Exit)
            {
                if (!string.IsNullOrEmpty(_activeSession.Uid)) _eventAttendanceCache.Remove(_activeSession.Uid);
                if (_activeSession.Student != null && !string.IsNullOrEmpty(_activeSession.Student.StudentId))
                    _eventAttendanceCache.Remove(_activeSession.Student.StudentId);
            }
        }

        private void SetState(AuthenticationStage newState)
        {
            if (DispatcherQueue.HasThreadAccess) ExecuteStateChange(newState);
            else DispatcherQueue.TryEnqueue(() => ExecuteStateChange(newState));
        }

        private async void ExecuteStateChange(AuthenticationStage newState)
        {
            _currentStage = newState;
            UpdateUiForState(newState);

            if (newState == AuthenticationStage.WaitingForPIN || newState == AuthenticationStage.WaitingForQR)
                _inactivityTimer.Start();
            else
                _inactivityTimer.Stop();

            if (newState == AuthenticationStage.AccessGranted)
            {
                await Task.Delay(1000);
                if (_currentStage == AuthenticationStage.AccessGranted)
                    SetState(AuthenticationStage.Idle);
            }
            else if (newState == AuthenticationStage.AccessDenied)
            {
                await Task.Delay(3500);
                if (_currentStage == AuthenticationStage.AccessDenied)
                    SetState(AuthenticationStage.Idle);
            }
        }

        private void UpdateUiForState(AuthenticationStage state)
        {
            UpdateProgressIndicator();

            // The Fix: By default, turn LEDs off unless explicitly waiting for a QR scan
            SetHardwareLeds(state == AuthenticationStage.WaitingForQR);

            switch (state)
            {
                case AuthenticationStage.Idle:
                    _currentPinBuffer = "";
                    _isVerifyingPin = false;
                    _tempStudentName = "";
                    _tempStudentId = "";
                    _activeSession = null;

                    TogglePanels(showStatus: true, showPin: false, showQr: false);
                    ResetProfileData();
                    ApplyStatusStyle(Colors.Blue, "\uE72A", "READY TO SCAN", "Please tap NFC ID on the reader");
                    break;

                case AuthenticationStage.NFCVerified:
                    TogglePanels(showStatus: true, showPin: false, showQr: false);
                    LoadProfileData(_tempStudentName, _tempStudentId);

                    if (_currentMode == VerificationMode.Fast)
                        ApplyStatusStyle(Colors.Green, "\uE73E", "NFC VERIFIED", "Authenticating...");
                    else
                        ApplyStatusStyle(Colors.Green, "\uE73E", "✓ NFC VERIFIED", "Preparing PIN Verification...");
                    break;

                case AuthenticationStage.WaitingForPIN:
                    _isVerifyingPin = false;
                    TogglePanels(showStatus: false, showPin: true, showQr: false);
                    UpdatePinDots();
                    PinErrorText.Visibility = Visibility.Collapsed;
                    break;

                case AuthenticationStage.PINVerified:
                    TogglePanels(showStatus: true, showPin: false, showQr: false);
                    ApplyStatusStyle(Colors.Green, "\uE73E", "✓ PIN VERIFIED", "Opening QR Verification...");
                    break;

                case AuthenticationStage.WaitingForQR:
                    TogglePanels(showStatus: false, showPin: false, showQr: true);
                    break;

                case AuthenticationStage.AccessGranted:
                    TogglePanels(showStatus: true, showPin: false, showQr: false);
                    ApplyStatusStyle(Colors.Green, "\uE73E", _outcomeTitle, _outcomeMessage);
                    break;

                case AuthenticationStage.AccessDenied:
                    TogglePanels(showStatus: true, showPin: false, showQr: false);
                    ApplyStatusStyle(Colors.Red, "\uEA39", _outcomeTitle, _outcomeMessage);
                    StudentIdText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 248, 113, 113));
                    break;
            }
        }

        public async void ProcessNfcScan(string uid)
        {
            if (_currentStage != AuthenticationStage.Idle) return;

            Stopwatch nfcTimer = Stopwatch.StartNew();

            var transType = KioskStateController.CurrentType;
            if (_originatingMode == "Event" && transType == TransactionType.Entry)
            {
                transType = TransactionType.EventAttendance;
            }

            if (transType == TransactionType.EventAttendance && _eventAttendanceCache.Contains(uid))
            {
                PlaySecurityAlert();
                _outcomeTitle = "ANTI-PROXY TRIGGERED";
                _outcomeMessage = "This credential has already checked into this event.";
                ExecuteStateChange(AuthenticationStage.AccessDenied);

                string logTime = DateTime.Now.ToString("MMM dd, yyyy - hh:mm:ss tt");
                OnKioskLog?.Invoke($"{logTime} | UID {uid} | DENIED | DOUBLE ENTRY");

                nfcTimer.Stop();
                _ = Task.Run(() => _database.LogVerificationAsync(null, null, uid, transType, _currentMode, false, "DOUBLE_ENTRY", "ANTI_PROXY_VIOLATION", "Blocked attempt to scan into the same event multiple times.", nfcTimer.Elapsed.TotalMilliseconds, 0));
                return;
            }

            // --- FRONT-LINE RESTRICTION CHECK ---
            // Blocks uninvited attendees immediately before engines or sessions are invoked
            if (transType == TransactionType.EventAttendance && !string.IsNullOrEmpty(_eventId))
            {
                try
                {
                    var student = await _database.GetStudentByUidAsync(uid);
                    if (student != null)
                    {
                        bool isAllowed = await _database.IsStudentAllowedForEventAsync(_eventId, student.StudentId);
                        if (!isAllowed)
                        {
                            PlaySecurityAlert();
                            _outcomeTitle = "RESTRICTED EVENT";
                            _outcomeMessage = "You are not on the approved attendee list for this event.";
                            LoadProfileData(student.FullName, student.StudentId);
                            ExecuteStateChange(AuthenticationStage.AccessDenied);

                            string logTime = DateTime.Now.ToString("MMM dd, yyyy - hh:mm:ss tt");
                            OnKioskLog?.Invoke($"{logTime} | UID {uid} | DENIED | UNINVITED");

                            nfcTimer.Stop();
                            _ = Task.Run(() => _database.LogVerificationAsync(student, student.FullName, uid, transType, _currentMode, false, "UNAUTHORIZED_EVENT_ACCESS", "RESTRICTED_EVENT", "Student not on the restricted event roster.", nfcTimer.Elapsed.TotalMilliseconds, 0));
                            return;
                        }
                    }
                }
                catch { /* Ignore exceptions to allow the VerificationEngine to handle offline cache logic safely */ }
            }
            // ------------------------------------

            if (IsInvalidUid(uid))
            {
                PlaySecurityAlert();
                _outcomeTitle = "BAD READ";
                _outcomeMessage = "Card couldn't be read properly. Please tap again.";
                ExecuteStateChange(AuthenticationStage.AccessDenied);

                string logTime = DateTime.Now.ToString("MMM dd, yyyy - hh:mm:ss tt");
                OnKioskLog?.Invoke($"{logTime} | UID {uid} | BAD READ: Please tap again");

                nfcTimer.Stop();
                _ = Task.Run(() => _database.LogVerificationAsync(null, null, uid, transType, _currentMode, false, "BAD_READ", "BAD_NFC_READ", "Card couldn't be read properly. User prompted to tap again.", nfcTimer.Elapsed.TotalMilliseconds, 0));
                return;
            }

            VerificationOutcome outcome = await _engine.BeginNfcVerificationAsync(uid, _currentMode, transType, _eventId);

            if (outcome.Session != null) _activeSession = outcome.Session;

            OnKioskOutcome?.Invoke(outcome);

            DispatcherQueue.TryEnqueue(async () =>
            {
                _tempStudentName = outcome.Student != null ? outcome.Student.FullName : "UNKNOWN USER";
                _tempStudentId = outcome.Student != null ? outcome.Student.StudentId : "---";

                nfcTimer.Stop();
                string nfcResult = outcome.IsGranted ? "MATCH (Access Granted)" :
                                   (outcome.Step == VerificationStep.RequiresPin ? "MATCH (Proceeding to PIN)" : "DENIED");
                LogPerformanceMetric("NFC Reader Response Time", nfcTimer.ElapsedMilliseconds, nfcResult);

                if (!outcome.IsGranted && outcome.Step == VerificationStep.Completed)
                {
                    string[] severeErrors = { "PIN_LOCKED", "ANTI_TAILGATING_VIOLATION", "UNAUTHORIZED_EVENT_ACCESS", "NOT_REGISTERED", "CREDENTIAL_MISMATCH", "INACTIVE_STUDENT" };
                    if (severeErrors.Contains(outcome.ErrorCategory))
                        PlaySecurityAlert();

                    _outcomeTitle = outcome.ResultTitle;
                    _outcomeMessage = outcome.ResultMessage;
                    LoadProfileData(_tempStudentName, _tempStudentId);
                    ExecuteStateChange(AuthenticationStage.AccessDenied);
                }
                else
                {
                    ExecuteStateChange(AuthenticationStage.NFCVerified);
                    await Task.Delay(800);

                    _outcomeTitle = outcome.ResultTitle;
                    _outcomeMessage = outcome.ResultMessage;

                    if (outcome.IsGranted)
                    {
                        PlaySuccessPing();
                        RegisterSuccessfulEntry();
                        ExecuteStateChange(AuthenticationStage.AccessGranted);
                    }
                    else if (outcome.Step == VerificationStep.RequiresPin)
                    {
                        ExecuteStateChange(AuthenticationStage.WaitingForPIN);
                    }
                }
            });
        }

        public async void ProcessHardwareKeypadStroke(string digit, bool isEnter, bool isClear, bool isCancel)
        {
            if (_currentStage != AuthenticationStage.WaitingForPIN || _activeSession == null || _isVerifyingPin) return;

            _inactivityTimer.Stop();
            _inactivityTimer.Start();

            PlayKeypadBeep();

            if (isCancel) { SetState(AuthenticationStage.Idle); return; }

            if (isClear)
            {
                if (_currentPinBuffer.Length > 0)
                {
                    _currentPinBuffer = _currentPinBuffer.Substring(0, _currentPinBuffer.Length - 1);
                    UpdatePinDots();
                }
                return;
            }

            if (isEnter)
            {
                if (_currentPinBuffer.Length < 4) return;

                _isVerifyingPin = true;

                PinErrorText.Text = "Verifying...";
                PinErrorText.Foreground = new SolidColorBrush(Microsoft.UI.Colors.White);
                PinErrorText.Visibility = Visibility.Visible;

                try
                {
                    await Task.Delay(400);

                    Stopwatch pinTimer = Stopwatch.StartNew();
                    VerificationOutcome outcome = await _engine.SubmitPinAsync(_activeSession, _currentPinBuffer);
                    pinTimer.Stop();

                    string pinResult = "MISMATCH";
                    if (outcome.ErrorCategory == "PIN_LOCKED") pinResult = "LOCKED";
                    else if (outcome.IsGranted) pinResult = "MATCH (Access Granted)";
                    else if (outcome.Step == VerificationStep.RequiresQr) pinResult = "MATCH (Proceeding to QR)";

                    LogPerformanceMetric("PIN Authentication", pinTimer.ElapsedMilliseconds, pinResult);

                    if (outcome.Session != null)
                        _activeSession = outcome.Session;

                    OnKioskOutcome?.Invoke(outcome);

                    if (!outcome.IsGranted && outcome.Step == VerificationStep.Completed)
                    {
                        _currentPinBuffer = "";
                        UpdatePinDots();

                        if (outcome.ErrorCategory == "PIN_LOCKED")
                        {
                            PlaySecurityAlert();
                            _outcomeTitle = outcome.ResultTitle;
                            _outcomeMessage = outcome.ResultMessage;
                            SetState(AuthenticationStage.AccessDenied);
                        }
                        else
                        {
                            System.Threading.Tasks.Task.Run(() => { Console.Beep(1500, 200); });
                            PinErrorText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 248, 113, 113));
                            PinErrorText.Text = outcome.ResultMessage;
                        }
                    }
                    else
                    {
                        SetState(AuthenticationStage.PINVerified);
                        await Task.Delay(800);

                        if (outcome.IsGranted)
                        {
                            PlaySuccessPing();
                            _outcomeTitle = outcome.ResultTitle;
                            _outcomeMessage = outcome.ResultMessage;
                            RegisterSuccessfulEntry();
                            SetState(AuthenticationStage.AccessGranted);
                        }
                        else if (outcome.Step == VerificationStep.RequiresQr)
                        {
                            SetState(AuthenticationStage.WaitingForQR);
                        }
                    }
                }
                finally
                {
                    _isVerifyingPin = false;
                }
                return;
            }

            if (_currentPinBuffer.Length < 4 && !string.IsNullOrEmpty(digit))
            {
                _currentPinBuffer += digit;
                UpdatePinDots();
                PinErrorText.Visibility = Visibility.Collapsed;
            }
        }

        public async void ProcessQrScan(string payload)
        {
            if (_currentStage != AuthenticationStage.WaitingForQR) return;

            Stopwatch qrTimer = Stopwatch.StartNew();

            _inactivityTimer.Stop();
            _inactivityTimer.Start();

            VerificationOutcome outcome;

            var transType = KioskStateController.CurrentType;
            if (_originatingMode == "Event" && transType == TransactionType.Entry)
            {
                transType = TransactionType.EventAttendance;
            }

            if (transType == TransactionType.EventAttendance && _eventAttendanceCache.Contains(payload.Trim()))
            {
                PlaySecurityAlert();
                _outcomeTitle = "ANTI-PROXY TRIGGERED";
                _outcomeMessage = "This credential has already checked into this event.";
                LoadProfileData("UNKNOWN USER", payload.Trim());
                ExecuteStateChange(AuthenticationStage.AccessDenied);

                string logTime = DateTime.Now.ToString("MMM dd, yyyy - hh:mm:ss tt");
                OnKioskLog?.Invoke($"{logTime} | ID {payload.Trim()} | DENIED | DOUBLE ENTRY");

                qrTimer.Stop();
                _ = Task.Run(() => _database.LogVerificationAsync(null, null, payload.Trim(), transType, _currentMode, false, "DOUBLE_ENTRY", "ANTI_PROXY_VIOLATION", "Blocked attempt to scan into the same event multiple times.", qrTimer.Elapsed.TotalMilliseconds, 0));

                LogPerformanceMetric("QR Validation (Fallback Flow)", qrTimer.ElapsedMilliseconds, "DENIED / DOUBLE ENTRY");
                return;
            }

            // --- FRONT-LINE RESTRICTION CHECK (QR) ---
            if (transType == TransactionType.EventAttendance && !string.IsNullOrEmpty(_eventId))
            {
                try
                {
                    var student = await _database.GetStudentByIdAsync(payload.Trim());
                    if (student != null)
                    {
                        bool isAllowed = await _database.IsStudentAllowedForEventAsync(_eventId, student.StudentId);
                        if (!isAllowed)
                        {
                            PlaySecurityAlert();
                            _outcomeTitle = "RESTRICTED EVENT";
                            _outcomeMessage = "You are not on the approved attendee list for this event.";
                            LoadProfileData(student.FullName, student.StudentId);
                            ExecuteStateChange(AuthenticationStage.AccessDenied);

                            string logTime = DateTime.Now.ToString("MMM dd, yyyy - hh:mm:ss tt");
                            OnKioskLog?.Invoke($"{logTime} | ID {payload.Trim()} | DENIED | UNINVITED");

                            qrTimer.Stop();
                            _ = Task.Run(() => _database.LogVerificationAsync(student, student.FullName, payload.Trim(), transType, _currentMode, false, "UNAUTHORIZED_EVENT_ACCESS", "RESTRICTED_EVENT", "Student not on the restricted event roster.", qrTimer.Elapsed.TotalMilliseconds, 0));

                            LogPerformanceMetric("QR Validation (Restriction Check)", qrTimer.ElapsedMilliseconds, "DENIED / UNINVITED");
                            return;
                        }
                    }
                }
                catch { /* Ignore exceptions to allow the VerificationEngine to handle offline cache logic safely */ }
            }
            // -----------------------------------------

            if (_activeSession == null)
            {
                outcome = await _engine.BeginQrFallbackVerificationAsync(payload, _currentMode, transType, _eventId);
                if (outcome.Session != null) _activeSession = outcome.Session;

                OnKioskOutcome?.Invoke(outcome);

                _tempStudentName = outcome.Student != null ? outcome.Student.FullName : "UNKNOWN USER";
                _tempStudentId = outcome.Student != null ? outcome.Student.StudentId : "---";

                if (!outcome.IsGranted && outcome.Step == VerificationStep.Completed)
                {
                    string[] severeErrors = { "PIN_LOCKED", "ANTI_TAILGATING_VIOLATION", "UNAUTHORIZED_EVENT_ACCESS", "NOT_REGISTERED", "CREDENTIAL_MISMATCH", "INACTIVE_STUDENT" };
                    if (severeErrors.Contains(outcome.ErrorCategory))
                        PlaySecurityAlert();

                    _outcomeTitle = outcome.ResultTitle;
                    _outcomeMessage = outcome.ResultMessage;
                    LoadProfileData(_tempStudentName, _tempStudentId);

                    qrTimer.Stop();
                    LogPerformanceMetric("QR Validation (Fallback Flow)", qrTimer.ElapsedMilliseconds, "DENIED / MISMATCH");

                    SetState(AuthenticationStage.AccessDenied);
                }
                else if (outcome.Step == VerificationStep.RequiresPin)
                {
                    LoadProfileData(_tempStudentName, _tempStudentId);

                    qrTimer.Stop();
                    LogPerformanceMetric("QR Validation (Fallback Flow)", qrTimer.ElapsedMilliseconds, "MATCH (Proceeding to PIN)");

                    SetState(AuthenticationStage.WaitingForPIN);
                }
                return;
            }

            outcome = await _engine.SubmitQrAsync(_activeSession, payload);
            if (outcome.Session != null) _activeSession = outcome.Session;

            OnKioskOutcome?.Invoke(outcome);

            _outcomeTitle = outcome.ResultTitle;
            _outcomeMessage = outcome.ResultMessage;

            if (outcome.IsGranted)
            {
                PlaySuccessPing();
                qrTimer.Stop();
                LogPerformanceMetric("QR Validation (High Security Match)", qrTimer.ElapsedMilliseconds, "MATCH (Access Granted)");

                RegisterSuccessfulEntry();
                SetState(AuthenticationStage.AccessGranted);
            }
            else
            {
                string[] severeErrors = { "PIN_LOCKED", "ANTI_TAILGATING_VIOLATION", "UNAUTHORIZED_EVENT_ACCESS", "NOT_REGISTERED", "CREDENTIAL_MISMATCH", "INACTIVE_STUDENT" };
                if (severeErrors.Contains(outcome.ErrorCategory))
                    PlaySecurityAlert();

                qrTimer.Stop();
                LogPerformanceMetric("QR Validation (High Security Match)", qrTimer.ElapsedMilliseconds, "MISMATCH");

                SetState(AuthenticationStage.AccessDenied);
            }
        }

        private void TogglePanels(bool showStatus, bool showPin, bool showQr)
        {
            StatusPanel.Visibility = showStatus ? Visibility.Visible : Visibility.Collapsed;
            PinPanel.Visibility = showPin ? Visibility.Visible : Visibility.Collapsed;
            QrPanel.Visibility = showQr ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ApplyStatusStyle(Windows.UI.Color baseColor, string iconGlyph, string headline, string subheadline)
        {
            StatusBackgroundBorder.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(26, baseColor.R, baseColor.G, baseColor.B));
            StatusBackgroundBorder.BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(48, baseColor.R, baseColor.G, baseColor.B));
            StatusHeadlineText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, baseColor.R, baseColor.G, baseColor.B));
            StatusHeadlineText.Text = headline;
            StatusSubheadlineText.Text = subheadline;
            StatusIcon.Glyph = iconGlyph;
            StatusIcon.Foreground = new SolidColorBrush(baseColor);
        }

        private void UpdatePinDots()
        {
            PinDot1.Glyph = _currentPinBuffer.Length >= 1 ? "\uEA3B" : "\uEA3A";
            PinDot2.Glyph = _currentPinBuffer.Length >= 2 ? "\uEA3B" : "\uEA3A";
            PinDot3.Glyph = _currentPinBuffer.Length >= 3 ? "\uEA3B" : "\uEA3A";
            PinDot4.Glyph = _currentPinBuffer.Length >= 4 ? "\uEA3B" : "\uEA3A";
        }

        private void UpdateProgressIndicator()
        {
            if (_currentMode == VerificationMode.Fast)
            {
                ProgressIndicatorPanel.Visibility = Visibility.Collapsed;
                return;
            }

            ProgressIndicatorPanel.Visibility = Visibility.Visible;

            ProgQrIcon.Visibility = _currentMode == VerificationMode.HighSecurity ? Visibility.Visible : Visibility.Collapsed;
            ProgQrText.Visibility = _currentMode == VerificationMode.HighSecurity ? Visibility.Visible : Visibility.Collapsed;
            ProgLine2.Visibility = _currentMode == VerificationMode.HighSecurity ? Visibility.Visible : Visibility.Collapsed;

            var grey = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 80, 80, 80));
            var blue = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 96, 165, 250));
            var green = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 52, 211, 153));

            ProgNfcIcon.Foreground = grey; ProgNfcText.Foreground = grey;
            ProgPinIcon.Foreground = grey; ProgPinText.Foreground = grey;
            ProgQrIcon.Foreground = grey; ProgQrText.Foreground = grey;
            ProgNfcIcon.Glyph = "\uECCA"; ProgPinIcon.Glyph = "\uECA7"; ProgQrIcon.Glyph = "\uED14";

            if (_currentStage == AuthenticationStage.AccessDenied) return;

            if (_currentStage >= AuthenticationStage.WaitingForPIN)
            {
                ProgNfcIcon.Foreground = green; ProgNfcText.Foreground = green; ProgNfcIcon.Glyph = "\uE73E";
                ProgPinIcon.Foreground = blue; ProgPinText.Foreground = blue;
            }
            else
            {
                ProgNfcIcon.Foreground = blue; ProgNfcText.Foreground = blue;
            }

            if (_currentStage >= AuthenticationStage.WaitingForQR)
            {
                ProgPinIcon.Foreground = green; ProgPinText.Foreground = green; ProgPinIcon.Glyph = "\uE73E";
                ProgQrIcon.Foreground = blue; ProgQrText.Foreground = blue;
            }

            if (_currentStage == AuthenticationStage.AccessGranted)
            {
                ProgQrIcon.Foreground = green; ProgQrText.Foreground = green; ProgQrIcon.Glyph = "\uE73E";
            }
        }

        private void LoadProfileData(string name, string id)
        {
            ProfileBorder.Opacity = 1.0;
            StudentNameText.Text = name;
            StudentIdText.Text = id;
            StudentNameText.Foreground = new SolidColorBrush(Microsoft.UI.Colors.White);
            StudentIdText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 160, 160, 160));
        }

        private void ResetProfileData()
        {
            ProfileBorder.Opacity = 0.3;
            StudentNameText.Text = "---";
            StudentIdText.Text = "---";
        }

        private void EnforceFullScreenMode()
        {
            IntPtr hWnd = WindowNative.GetWindowHandle(this);
            WindowId windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
            AppWindow appWindow = AppWindow.GetFromWindowId(windowId);

            if (appWindow != null)
            {
                var displayAreas = DisplayArea.FindAll();
                if (displayAreas.Count > 1)
                {
                    var secondScreen = displayAreas[1];
                    appWindow.MoveAndResize(secondScreen.WorkArea);
                }
                appWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
            }
        }

        private void ApplyDynamicHeader()
        {
            KioskHeaderSubtitle.Text = $"{_originatingMode.ToUpper()} TERMINAL  •  {_contextDetails.ToUpper()}";
        }

        private static bool IsInvalidUid(string uid)
        {
            if (string.IsNullOrWhiteSpace(uid)) return true;

            string[] parts = uid.Split(':');
            if (parts.Length != 4 && parts.Length != 7) return true;

            bool allZero = true;
            foreach (string part in parts)
            {
                if (part != "00")
                {
                    allZero = false;
                    break;
                }
            }

            if (allZero) return true;

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
                if (trailingZeros) return true;
            }

            return false;
        }

        // 2. CAMERA INITIALIZATION WITH MACRO FOCUS TUNING
        private async Task InitializeCameraAsync()
        {
            try
            {
                var devices = await Windows.Devices.Enumeration.DeviceInformation.FindAllAsync(Windows.Devices.Enumeration.DeviceClass.VideoCapture);
                if (devices.Count == 0) return;

                string targetCameraId = string.IsNullOrEmpty(KioskStateController.SelectedCameraId) ? devices[0].Id : KioskStateController.SelectedCameraId;
                var selectedDevice = devices.FirstOrDefault(d => d.Id == targetCameraId) ?? devices[0];

                _mediaCapture = new MediaCapture();
                await _mediaCapture.InitializeAsync(new MediaCaptureInitializationSettings
                {
                    VideoDeviceId = selectedDevice.Id,
                    StreamingCaptureMode = StreamingCaptureMode.Video,
                    MemoryPreference = MediaCaptureMemoryPreference.Cpu
                });

                // ENHANCED AUTO-FOCUS: Set focus range to Macro for phone screens held close
                var focusControl = _mediaCapture.VideoDeviceController.FocusControl;
                if (focusControl.Supported)
                {
                    var settings = new FocusSettings
                    {
                        Mode = FocusMode.Continuous,
                        AutoFocusRange = focusControl.SupportedFocusRanges.Contains(AutoFocusRange.Macro)
                            ? AutoFocusRange.Macro
                            : AutoFocusRange.FullRange
                    };
                    focusControl.Configure(settings);
                }

                var frameSource = _mediaCapture.FrameSources.Values.FirstOrDefault(fs => fs.Info.MediaStreamType == MediaStreamType.VideoPreview) ?? _mediaCapture.FrameSources.Values.FirstOrDefault();
                if (frameSource == null) return;

                _frameReader = await _mediaCapture.CreateFrameReaderAsync(frameSource, MediaEncodingSubtypes.Bgra8);
                _frameReader.AcquisitionMode = MediaFrameReaderAcquisitionMode.Realtime;
                _frameReader.FrameArrived += FrameReader_FrameArrived;
                await _frameReader.StartAsync();

                KioskCameraPreview.Source = _previewSource;
            }
            catch { }
        }

        // 3. FAST FRAME ARRIVED (Scans every 120ms instead of 500ms)
        private void FrameReader_FrameArrived(MediaFrameReader sender, MediaFrameArrivedEventArgs args)
        {
            if (_isClosing || _currentStage != AuthenticationStage.WaitingForQR) return;

            bool processPreview = !_isUpdatingPreview && (DateTime.Now - _lastPreviewTime).TotalMilliseconds >= 33; // ~30 FPS preview
            bool processDecode = !_isDecoding && (DateTime.Now - _lastFrameProcessTime).TotalMilliseconds >= 120;  // ~8 scans/sec

            if (!processPreview && !processDecode) return;

            using var frame = sender.TryAcquireLatestFrame();
            var videoFrame = frame?.VideoMediaFrame;
            using var rawBitmap = videoFrame?.SoftwareBitmap;

            if (rawBitmap == null) return;

            if (processPreview)
            {
                _isUpdatingPreview = true;
                _lastPreviewTime = DateTime.Now;

                SoftwareBitmap previewBitmap;
                if (rawBitmap.BitmapPixelFormat != BitmapPixelFormat.Bgra8 || rawBitmap.BitmapAlphaMode == BitmapAlphaMode.Straight)
                    previewBitmap = SoftwareBitmap.Convert(rawBitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
                else
                    previewBitmap = SoftwareBitmap.Copy(rawBitmap);

                DispatcherQueue.TryEnqueue(async () =>
                {
                    try
                    {
                        if (!_isClosing && _currentStage == AuthenticationStage.WaitingForQR)
                            await _previewSource.SetBitmapAsync(previewBitmap);
                    }
                    catch { }
                    finally
                    {
                        previewBitmap.Dispose();
                        _isUpdatingPreview = false;
                    }
                });
            }

            if (processDecode)
            {
                _isDecoding = true;
                _lastFrameProcessTime = DateTime.Now;

                SoftwareBitmap decodeBitmap;
                if (rawBitmap.BitmapPixelFormat != BitmapPixelFormat.Bgra8)
                    decodeBitmap = SoftwareBitmap.Convert(rawBitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
                else
                    decodeBitmap = SoftwareBitmap.Copy(rawBitmap);

                Task.Run(() =>
                {
                    try
                    {
                        string? payload = DecodeQrPayloadDirect(decodeBitmap);

                        if (!string.IsNullOrWhiteSpace(payload))
                        {
                            if (payload == _lastScannedQr && (DateTime.Now - _lastQrScanTime).TotalSeconds < 3) return;

                            _lastScannedQr = payload;
                            _lastQrScanTime = DateTime.Now;

                            DispatcherQueue.TryEnqueue(() => ProcessQrScan(payload));
                        }
                    }
                    catch { }
                    finally
                    {
                        decodeBitmap.Dispose();
                        _isDecoding = false;
                    }
                });
            }
        }

        // 4. ZERO-LATENCY PIXEL EXTRACTION (Replaces Jpeg Encoder/Decoder)
        private string? DecodeQrPayloadDirect(SoftwareBitmap bitmap)
        {
            try
            {
                int width = bitmap.PixelWidth;
                int height = bitmap.PixelHeight;

                // Extract raw pixel buffer straight into memory
                byte[] bytes = new byte[4 * width * height];
                bitmap.CopyToBuffer(bytes.AsBuffer());

                // Hand raw BGRA bytes directly to ZXing
                var source = new RGBLuminanceSource(bytes, width, height, RGBLuminanceSource.BitmapFormat.BGRA32);
                Result? result = _barcodeReader.Decode(source);

                return result?.Text;
            }
            catch
            {
                return null;
            }
        }

        private async Task DisposeCameraAsync()
        {
            if (_isDisposingCamera) return;
            _isDisposingCamera = true;

            SetHardwareLeds(false);

            try
            {
                if (_frameReader != null)
                {
                    _frameReader.FrameArrived -= FrameReader_FrameArrived;
                    await _frameReader.StopAsync();
                    _frameReader.Dispose();
                    _frameReader = null;
                }
                if (_mediaCapture != null)
                {
                    _mediaCapture.Dispose();
                    _mediaCapture = null;
                }
                KioskCameraPreview.Source = null;
            }
            finally
            {
                _isDisposingCamera = false;
            }
        }

        private void SetHardwareLeds(bool turnOn)
        {
            try
            {
                if (_serialPort != null && _serialPort.IsOpen)
                {
                    // If the window is closing, write immediately on the calling thread
                    if (_isClosing)
                    {
                        _serialPort.WriteLine(turnOn ? "LED=ON" : "LED=OFF");
                    }
                    else
                    {
                        // Otherwise, run asynchronously off the UI thread
                        Task.Run(() =>
                        {
                            try
                            {
                                if (_serialPort != null && _serialPort.IsOpen)
                                {
                                    _serialPort.WriteLine(turnOn ? "LED=ON" : "LED=OFF");
                                }
                            }
                            catch { }
                        });
                    }
                }
            }
            catch { }
        }

        private void DebugSecurityLevel_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DebugSecurityLevel.SelectedIndex == 0) _currentMode = VerificationMode.Fast;
            else if (DebugSecurityLevel.SelectedIndex == 1) _currentMode = VerificationMode.Standard;
            else if (DebugSecurityLevel.SelectedIndex == 2) _currentMode = VerificationMode.HighSecurity;

            SetState(AuthenticationStage.Idle);
        }

        private void SimulateNfc_Click(object sender, RoutedEventArgs e) => ProcessNfcScan("04:A1:B2:C3");

        private async void SimulatePin_Click(object sender, RoutedEventArgs e)
        {
            if (_currentStage != AuthenticationStage.WaitingForPIN) return;

            string[] strokes = { "1", "2", "3", "4", "ENTER" };
            foreach (var stroke in strokes)
            {
                if (stroke == "ENTER") ProcessHardwareKeypadStroke("", true, false, false);
                else ProcessHardwareKeypadStroke(stroke, false, false, false);

                await Task.Delay(150);
            }
        }

        private void SimulateQr_Click(object sender, RoutedEventArgs e) => ProcessQrScan("26-00001");

        private async void SimulateFail_Click(object sender, RoutedEventArgs e)
        {
            if (_currentStage == AuthenticationStage.Idle)
            {
                ProcessNfcScan("00:00:00:00");
            }
            else if (_currentStage == AuthenticationStage.WaitingForPIN)
            {
                string[] strokes = { "9", "9", "9", "9", "ENTER" };
                foreach (var stroke in strokes)
                {
                    if (stroke == "ENTER") ProcessHardwareKeypadStroke("", true, false, false);
                    else ProcessHardwareKeypadStroke(stroke, false, false, false);

                    await Task.Delay(150);
                }
            }
            else if (_currentStage == AuthenticationStage.WaitingForQR)
            {
                ProcessQrScan("INVALID");
            }
        }
    }
}