using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Threading.Tasks;
using WinRT.Interop;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Collections.Generic;
using System.Linq;
using Windows.Graphics.Imaging;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.MediaProperties;
using Windows.Storage.Streams;
using ZXing;
using ZXing.Common;
using System.IO.Ports;

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
        private readonly string _originatingMode;
        private readonly string _contextDetails;

        // Hardware Connection
        private SerialPort? _serialPort;

        // Backend Integration
        private readonly DatabaseService _database = new();
        private readonly VerificationEngine _engine;
        private VerificationSession? _activeSession;

        // State Machine Properties
        private VerificationMode _currentMode = VerificationMode.HighSecurity;
        private AuthenticationStage _currentStage = AuthenticationStage.Idle;

        // NEW: Inactivity Timer to prevent the kiosk from getting permanently stuck
        private readonly DispatcherTimer _inactivityTimer = new();

        // Camera & QR Tracking
        private MediaFrameReader? _frameReader;
        private readonly SoftwareBitmapSource _previewSource = new();
        private MediaCapture? _mediaCapture;
        private bool _isDecoding;
        private bool _isDisposingCamera;
        private bool _isClosing;

        // Strict Debounce: Prevents the same QR from being spammed
        private string _lastScannedQr = string.Empty;
        private DateTime _lastQrScanTime = DateTime.MinValue;

        private readonly BarcodeReaderGeneric _barcodeReader = new()
        {
            AutoRotate = true,
            Options = new DecodingOptions
            {
                PossibleFormats = new List<BarcodeFormat> { BarcodeFormat.QR_CODE },
                TryHarder = true
            }
        };

        // PIN Tracking
        private string _currentPinBuffer = "";

        // Dynamic UI Text Storage
        private string _tempStudentName = "";
        private string _tempStudentId = "";
        private string _outcomeTitle = "";
        private string _outcomeMessage = "";

        public KioskModeWindow(string originatingMode, string contextDetails)
        {
            this.InitializeComponent();
            _originatingMode = originatingMode;
            _contextDetails = contextDetails;

            _engine = new VerificationEngine(_database);

            EnforceFullScreenMode();
            ApplyDynamicHeader();

            // Setup the 15-second inactivity timeout
            _inactivityTimer.Interval = TimeSpan.FromSeconds(15);
            _inactivityTimer.Tick += InactivityTimer_Tick;

            Closed += KioskModeWindow_Closed;
            _ = InitializeCameraAsync();
            _ = SyncOperationalModeAsync();

            KioskStateController.ModeChanged += KioskStateController_ModeChanged;
        }

        private void InactivityTimer_Tick(object? sender, object e)
        {
            // If the user took too long, cancel the session and reset the terminal
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

        /* =========================================================================
         * PHYSICAL HARDWARE LISTENERS
         * ========================================================================= */

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
                    string key = line.Substring(4).Trim();
                    if (_currentStage == AuthenticationStage.WaitingForPIN && !string.IsNullOrEmpty(key))
                    {
                        DispatcherQueue.TryEnqueue(() =>
                        {
                            bool isEnter = (key == "A");
                            bool isClear = (key == "B");
                            bool isCancel = (key == "C");
                            string digit = char.IsDigit(key[0]) ? key : "";

                            ProcessHardwareKeypadStroke(digit, isEnter, isClear, isCancel);
                        });
                    }
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
                }
                _serialPort?.Dispose();
                _serialPort = null;
            }
            catch { }
        }

        private async Task SyncOperationalModeAsync()
        {
            try
            {
                string savedMode = await _database.GetSettingAsync("verification_mode", "Standard");

                if (savedMode == "Fast") { _currentMode = VerificationMode.Fast; DebugSecurityLevel.SelectedIndex = 0; }
                else if (savedMode == "High-Security") { _currentMode = VerificationMode.HighSecurity; DebugSecurityLevel.SelectedIndex = 2; }
                else { _currentMode = VerificationMode.Standard; DebugSecurityLevel.SelectedIndex = 1; }

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
            CloseSerialPort();
            _ = DisposeCameraAsync();
            KioskStateController.ModeChanged -= KioskStateController_ModeChanged;
        }

        private void ExitKiosk_Click(object sender, RoutedEventArgs e)
        {
            _isClosing = true;
            _inactivityTimer.Stop();
            CloseSerialPort();
            _ = DisposeCameraAsync();

            if (_originatingMode == "Event") { new EventAttendanceWindow().Activate(); }
            else { new VerificationWindow().Activate(); }
            this.Close();
        }

        private void ForgotIdButton_Click(object sender, RoutedEventArgs e)
        {
            _tempStudentName = "";
            _tempStudentId = "";
            _activeSession = null;
            SetState(AuthenticationStage.WaitingForQR);
        }

        /* =========================================================================
         * STATE MACHINE ENGINE
         * ========================================================================= */

        private void SetState(AuthenticationStage newState)
        {
            if (DispatcherQueue.HasThreadAccess) ExecuteStateChange(newState);
            else DispatcherQueue.TryEnqueue(() => ExecuteStateChange(newState));
        }

        private async void ExecuteStateChange(AuthenticationStage newState)
        {
            _currentStage = newState;
            UpdateUiForState(newState);

            // Inactivity Timeout Manager
            if (newState == AuthenticationStage.WaitingForPIN || newState == AuthenticationStage.WaitingForQR)
            {
                _inactivityTimer.Start(); // Start the 15-second clock
            }
            else
            {
                _inactivityTimer.Stop(); // Pause the clock on any other stage
            }

            // Auto-reset back to idle after a few seconds of showing the final result
            if (newState == AuthenticationStage.AccessGranted || newState == AuthenticationStage.AccessDenied)
            {
                await Task.Delay(3500);
                if (_currentStage == AuthenticationStage.AccessGranted || _currentStage == AuthenticationStage.AccessDenied)
                {
                    SetState(AuthenticationStage.Idle);
                }
            }
        }

        private void UpdateUiForState(AuthenticationStage state)
        {
            UpdateProgressIndicator();

            switch (state)
            {
                case AuthenticationStage.Idle:
                    _currentPinBuffer = "";
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

        /* =========================================================================
         * HARDWARE INTERFACE HOOKS
         * ========================================================================= */

        public async void ProcessNfcScan(string uid)
        {
            if (_currentStage != AuthenticationStage.Idle) return;

            var transType = _originatingMode == "Event" ? TransactionType.EventAttendance : KioskStateController.CurrentType;
            string? eventId = _originatingMode == "Event" ? _contextDetails : null;

            VerificationOutcome outcome = await _engine.BeginNfcVerificationAsync(uid, _currentMode, transType, eventId);

            DispatcherQueue.TryEnqueue(async () =>
            {
                _tempStudentName = outcome.Student != null ? outcome.Student.FullName : "UNKNOWN USER";
                _tempStudentId = outcome.Student != null ? outcome.Student.StudentId : uid;

                if (!outcome.IsGranted && outcome.Step == VerificationStep.Completed)
                {
                    _outcomeTitle = outcome.ResultTitle;
                    _outcomeMessage = outcome.ResultMessage;
                    LoadProfileData(_tempStudentName, _tempStudentId);
                    ExecuteStateChange(AuthenticationStage.AccessDenied);
                }
                else
                {
                    _activeSession = outcome.Session;
                    ExecuteStateChange(AuthenticationStage.NFCVerified);

                    await Task.Delay(800);

                    _outcomeTitle = outcome.ResultTitle;
                    _outcomeMessage = outcome.ResultMessage;

                    if (outcome.IsGranted) ExecuteStateChange(AuthenticationStage.AccessGranted);
                    else if (outcome.Step == VerificationStep.RequiresPin) ExecuteStateChange(AuthenticationStage.WaitingForPIN);
                }
            });
        }

        public async void ProcessHardwareKeypadStroke(string digit, bool isEnter, bool isClear, bool isCancel)
        {
            if (_currentStage != AuthenticationStage.WaitingForPIN || _activeSession == null) return;

            // Reset the inactivity timeout every time they touch a button
            _inactivityTimer.Stop();
            _inactivityTimer.Start();

            if (isCancel)
            {
                SetState(AuthenticationStage.Idle);
                return;
            }

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

                VerificationOutcome outcome = await _engine.SubmitPinAsync(_activeSession, _currentPinBuffer);

                if (!outcome.IsGranted && outcome.Step == VerificationStep.Completed)
                {
                    _currentPinBuffer = "";
                    UpdatePinDots();

                    if (_activeSession.Student.PinLocked)
                    {
                        _outcomeTitle = outcome.ResultTitle;
                        _outcomeMessage = outcome.ResultMessage;
                        SetState(AuthenticationStage.AccessDenied);
                    }
                    else
                    {
                        PinErrorText.Text = outcome.ResultMessage;
                        PinErrorText.Visibility = Visibility.Visible;
                    }
                }
                else
                {
                    SetState(AuthenticationStage.PINVerified);
                    await Task.Delay(800);

                    if (outcome.IsGranted)
                    {
                        _outcomeTitle = outcome.ResultTitle;
                        _outcomeMessage = outcome.ResultMessage;
                        SetState(AuthenticationStage.AccessGranted);
                    }
                    else if (outcome.Step == VerificationStep.RequiresQr)
                    {
                        SetState(AuthenticationStage.WaitingForQR);
                    }
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

            // Reset the timeout as they are successfully feeding data
            _inactivityTimer.Stop();
            _inactivityTimer.Start();

            VerificationOutcome outcome;

            if (_activeSession == null)
            {
                var transType = _originatingMode == "Event" ? TransactionType.EventAttendance : KioskStateController.CurrentType;
                string? eventId = _originatingMode == "Event" ? _contextDetails : null;

                outcome = await _engine.BeginQrFallbackVerificationAsync(payload, _currentMode, transType, eventId);

                _tempStudentName = outcome.Student != null ? outcome.Student.FullName : "UNKNOWN USER";
                _tempStudentId = outcome.Student != null ? outcome.Student.StudentId : "---";

                if (!outcome.IsGranted && outcome.Step == VerificationStep.Completed)
                {
                    _outcomeTitle = outcome.ResultTitle;
                    _outcomeMessage = outcome.ResultMessage;
                    LoadProfileData(_tempStudentName, _tempStudentId);
                    SetState(AuthenticationStage.AccessDenied);
                }
                else if (outcome.Step == VerificationStep.RequiresPin)
                {
                    _activeSession = outcome.Session;
                    LoadProfileData(_tempStudentName, _tempStudentId);
                    SetState(AuthenticationStage.WaitingForPIN);
                }
                return;
            }

            outcome = await _engine.SubmitQrAsync(_activeSession, payload);

            _outcomeTitle = outcome.ResultTitle;
            _outcomeMessage = outcome.ResultMessage;

            if (outcome.IsGranted) SetState(AuthenticationStage.AccessGranted);
            else SetState(AuthenticationStage.AccessDenied);
        }

        /* =========================================================================
         * UI HELPERS
         * ========================================================================= */

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
            if (appWindow != null) appWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
        }

        private void ApplyDynamicHeader()
        {
            KioskHeaderSubtitle.Text = $"{_originatingMode.ToUpper()} TERMINAL  •  {_contextDetails.ToUpper()}";
        }

        /* =========================================================================
         * KIOSK CAMERA ENGINE
         * ========================================================================= */

        private async Task InitializeCameraAsync()
        {
            try
            {
                var devices = await Windows.Devices.Enumeration.DeviceInformation.FindAllAsync(Windows.Devices.Enumeration.DeviceClass.VideoCapture);
                if (devices.Count == 0) return;

                _mediaCapture = new MediaCapture();
                await _mediaCapture.InitializeAsync(new MediaCaptureInitializationSettings
                {
                    VideoDeviceId = devices[0].Id,
                    StreamingCaptureMode = StreamingCaptureMode.Video,
                    MemoryPreference = MediaCaptureMemoryPreference.Cpu
                });

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

        private void FrameReader_FrameArrived(MediaFrameReader sender, MediaFrameArrivedEventArgs args)
        {
            if (_isClosing || _currentStage != AuthenticationStage.WaitingForQR) return;

            using var frame = sender.TryAcquireLatestFrame();
            var videoFrame = frame?.VideoMediaFrame;
            using var rawBitmap = videoFrame?.SoftwareBitmap;

            if (rawBitmap == null) return;

            using SoftwareBitmap normalizedBitmap = (rawBitmap.BitmapPixelFormat != BitmapPixelFormat.Bgra8 || rawBitmap.BitmapAlphaMode == BitmapAlphaMode.Straight)
                ? SoftwareBitmap.Convert(rawBitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied)
                : SoftwareBitmap.Copy(rawBitmap);

            var displayBitmap = SoftwareBitmap.Copy(normalizedBitmap);
            DispatcherQueue.TryEnqueue(async () =>
            {
                if (!_isClosing && _currentStage == AuthenticationStage.WaitingForQR)
                {
                    try { await _previewSource.SetBitmapAsync(displayBitmap); } catch { }
                }
                displayBitmap.Dispose();
            });

            if (!_isDecoding && (DateTime.Now - _lastQrScanTime).TotalMilliseconds >= 700)
            {
                _isDecoding = true;
                try
                {
                    string? payload = DecodeQrPayload(normalizedBitmap);
                    if (!string.IsNullOrWhiteSpace(payload))
                    {
                        if (payload == _lastScannedQr && (DateTime.Now - _lastQrScanTime).TotalSeconds < 3) return;

                        _lastScannedQr = payload;
                        _lastQrScanTime = DateTime.Now;

                        DispatcherQueue.TryEnqueue(() => ProcessQrScan(payload));
                    }
                }
                finally
                {
                    _isDecoding = false;
                }
            }
        }

        private string? DecodeQrPayload(SoftwareBitmap bitmap)
        {
            uint capacity = (uint)(bitmap.PixelWidth * bitmap.PixelHeight * 4);
            var buffer = new Windows.Storage.Streams.Buffer(capacity);
            bitmap.CopyToBuffer(buffer);

            byte[] pixels = new byte[capacity];
            using DataReader reader = DataReader.FromBuffer(buffer);
            reader.ReadBytes(pixels);

            var source = new RGBLuminanceSource(pixels, bitmap.PixelWidth, bitmap.PixelHeight, RGBLuminanceSource.BitmapFormat.BGRA32);
            Result? result = _barcodeReader.Decode(source);
            return result?.Text;
        }

        private async Task DisposeCameraAsync()
        {
            if (_isDisposingCamera) return;
            _isDisposingCamera = true;

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

        /* =========================================================================
         * DEBUG SIMULATION HOOKS
         * ========================================================================= */

        private void DebugSecurityLevel_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DebugSecurityLevel.SelectedIndex == 0) _currentMode = VerificationMode.Fast;
            else if (DebugSecurityLevel.SelectedIndex == 1) _currentMode = VerificationMode.Standard;
            else if (DebugSecurityLevel.SelectedIndex == 2) _currentMode = VerificationMode.HighSecurity;

            SetState(AuthenticationStage.Idle);
        }

        private void SimulateNfc_Click(object sender, RoutedEventArgs e) => ProcessNfcScan("04:A1:B2:C3");

        private void SimulatePin_Click(object sender, RoutedEventArgs e)
        {
            ProcessHardwareKeypadStroke("1", false, false, false);
            ProcessHardwareKeypadStroke("2", false, false, false);
            ProcessHardwareKeypadStroke("3", false, false, false);
            ProcessHardwareKeypadStroke("4", false, false, false);
            ProcessHardwareKeypadStroke("", true, false, false);
        }

        private void SimulateQr_Click(object sender, RoutedEventArgs e) => ProcessQrScan("TCU|26-00001|04:A1:B2:C3");

        private void SimulateFail_Click(object sender, RoutedEventArgs e)
        {
            if (_currentStage == AuthenticationStage.Idle)
            {
                ProcessNfcScan("00:00:00:00");
            }
            else if (_currentStage == AuthenticationStage.WaitingForPIN)
            {
                // FIX: Inject 4 bad digits so the simulated failure actually fires
                ProcessHardwareKeypadStroke("9", false, false, false);
                ProcessHardwareKeypadStroke("9", false, false, false);
                ProcessHardwareKeypadStroke("9", false, false, false);
                ProcessHardwareKeypadStroke("9", false, false, false);
                ProcessHardwareKeypadStroke("", true, false, false);
            }
            else if (_currentStage == AuthenticationStage.WaitingForQR)
            {
                ProcessQrScan("INVALID");
            }
        }
    }
}