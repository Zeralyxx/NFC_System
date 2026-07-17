using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Threading.Tasks;
using WinRT.Interop;

namespace NFC_System
{
    public enum SecurityLevel { Fast, Standard, High }

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

        // State Machine Properties
        private SecurityLevel _currentSecurityLevel = SecurityLevel.High;
        private AuthenticationStage _currentStage = AuthenticationStage.Idle;

        // PIN Tracking
        private string _currentPinBuffer = "";
        private int _pinAttemptsRemaining = 3;

        // Temporarily stored data between stages
        private string _tempStudentName = "";
        private string _tempStudentId = "";

        public KioskModeWindow(string originatingMode, string contextDetails)
        {
            this.InitializeComponent();
            _originatingMode = originatingMode;
            _contextDetails = contextDetails;

            EnforceFullScreenMode();
            ApplyDynamicHeader();

            // Kickoff state machine
            SetState(AuthenticationStage.Idle);
        }

        /* =========================================================================
         * STATE MACHINE ENGINE
         * ========================================================================= */

        private void SetState(AuthenticationStage newState)
        {
            // Only marshal to UI thread if necessary, otherwise execute immediately
            if (DispatcherQueue.HasThreadAccess)
            {
                ExecuteStateChange(newState);
            }
            else
            {
                DispatcherQueue.TryEnqueue(() => ExecuteStateChange(newState));
            }
        }

        private async void ExecuteStateChange(AuthenticationStage newState)
        {
            _currentStage = newState;
            UpdateUiForState(newState);

            // Handle automatic transitions and delays
            switch (newState)
            {
                case AuthenticationStage.NFCVerified:
                    await Task.Delay(500);
                    if (_currentSecurityLevel == SecurityLevel.Fast)
                        SetState(AuthenticationStage.AccessGranted);
                    else
                        SetState(AuthenticationStage.WaitingForPIN);
                    break;

                case AuthenticationStage.PINVerified:
                    await Task.Delay(500);
                    if (_currentSecurityLevel == SecurityLevel.Standard)
                        SetState(AuthenticationStage.AccessGranted);
                    else
                        SetState(AuthenticationStage.WaitingForQR);
                    break;

                case AuthenticationStage.AccessGranted:
                case AuthenticationStage.AccessDenied:
                    // Auto-reset back to idle after a few seconds of showing the result
                    await Task.Delay(3500);
                    if (_currentStage == AuthenticationStage.AccessGranted || _currentStage == AuthenticationStage.AccessDenied)
                    {
                        SetState(AuthenticationStage.Idle);
                    }
                    break;
            }
        }

        private void UpdateUiForState(AuthenticationStage state)
        {
            UpdateProgressIndicator();

            switch (state)
            {
                case AuthenticationStage.Idle:
                    _currentPinBuffer = "";
                    _pinAttemptsRemaining = 3;
                    _tempStudentName = "";
                    _tempStudentId = "";

                    TogglePanels(showStatus: true, showPin: false, showQr: false);
                    ResetProfileData();
                    ApplyStatusStyle(
                        Colors.Blue, "\uE72A",
                        "READY TO SCAN", "Please tap NFC ID on the reader"
                    );
                    break;

                case AuthenticationStage.NFCVerified:
                    TogglePanels(showStatus: true, showPin: false, showQr: false);
                    LoadProfileData(_tempStudentName, _tempStudentId);

                    if (_currentSecurityLevel == SecurityLevel.Fast)
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
                    // TODO: Hook up your MediaFrameReader from QrScannerWindow here
                    break;

                case AuthenticationStage.AccessGranted:
                    TogglePanels(showStatus: true, showPin: false, showQr: false);
                    ApplyStatusStyle(Colors.Green, "\uE73E", "ACCESS GRANTED", "Proceed through the gate");
                    break;

                case AuthenticationStage.AccessDenied:
                    TogglePanels(showStatus: true, showPin: false, showQr: false);
                    ApplyStatusStyle(Colors.Red, "\uEA39", "ACCESS DENIED", "Authentication Failed / See Guard");
                    StudentIdText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 248, 113, 113));
                    break;
            }
        }

        /* =========================================================================
         * HARDWARE INTERFACE HOOKS (Public methods for backend to call)
         * ========================================================================= */

        public void ProcessNfcScan(string studentName, string studentId, bool isValid)
        {
            if (_currentStage != AuthenticationStage.Idle) return;

            if (isValid)
            {
                _tempStudentName = studentName;
                _tempStudentId = studentId;
                SetState(AuthenticationStage.NFCVerified);
            }
            else
            {
                _tempStudentName = studentName;
                _tempStudentId = studentId;
                LoadProfileData(studentName, studentId);
                SetState(AuthenticationStage.AccessDenied);
            }
        }

        public void ProcessHardwareKeypadStroke(string digit, bool isEnter, bool isClear, bool isCancel)
        {
            if (_currentStage != AuthenticationStage.WaitingForPIN) return;

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
                if (_currentPinBuffer.Length < 4) return; // Prevent short submissions

                // Validate PIN (Replace with real backend validation)
                if (_currentPinBuffer == "1234")
                {
                    SetState(AuthenticationStage.PINVerified);
                }
                else
                {
                    _pinAttemptsRemaining--;
                    _currentPinBuffer = "";
                    UpdatePinDots();

                    if (_pinAttemptsRemaining <= 0)
                    {
                        SetState(AuthenticationStage.AccessDenied);
                    }
                    else
                    {
                        PinErrorText.Text = $"Incorrect PIN. Attempts remaining: {_pinAttemptsRemaining}";
                        PinErrorText.Visibility = Visibility.Visible;
                    }
                }
                return;
            }

            // Append digit
            if (_currentPinBuffer.Length < 4 && !string.IsNullOrEmpty(digit))
            {
                _currentPinBuffer += digit;
                UpdatePinDots();
                PinErrorText.Visibility = Visibility.Collapsed; // Hide error on new input
            }
        }

        public void ProcessQrScan(string payload, bool isValid)
        {
            if (_currentStage != AuthenticationStage.WaitingForQR) return;

            if (isValid)
            {
                SetState(AuthenticationStage.AccessGranted);
            }
            else
            {
                SetState(AuthenticationStage.AccessDenied);
            }
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
            // Filled circle: \uEA3B | Empty circle: \uEA3A
            PinDot1.Glyph = _currentPinBuffer.Length >= 1 ? "\uEA3B" : "\uEA3A";
            PinDot2.Glyph = _currentPinBuffer.Length >= 2 ? "\uEA3B" : "\uEA3A";
            PinDot3.Glyph = _currentPinBuffer.Length >= 3 ? "\uEA3B" : "\uEA3A";
            PinDot4.Glyph = _currentPinBuffer.Length >= 4 ? "\uEA3B" : "\uEA3A";
        }

        private void UpdateProgressIndicator()
        {
            if (_currentSecurityLevel == SecurityLevel.Fast)
            {
                ProgressIndicatorPanel.Visibility = Visibility.Collapsed;
                return;
            }

            ProgressIndicatorPanel.Visibility = Visibility.Visible;

            // Hide QR dot if only Standard security
            ProgQrIcon.Visibility = _currentSecurityLevel == SecurityLevel.High ? Visibility.Visible : Visibility.Collapsed;
            ProgQrText.Visibility = _currentSecurityLevel == SecurityLevel.High ? Visibility.Visible : Visibility.Collapsed;
            ProgLine2.Visibility = _currentSecurityLevel == SecurityLevel.High ? Visibility.Visible : Visibility.Collapsed;

            // Reset all to Grey
            var grey = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 80, 80, 80));
            var blue = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 96, 165, 250));
            var green = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 52, 211, 153));

            ProgNfcIcon.Foreground = grey; ProgNfcText.Foreground = grey;
            ProgPinIcon.Foreground = grey; ProgPinText.Foreground = grey;
            ProgQrIcon.Foreground = grey; ProgQrText.Foreground = grey;
            ProgNfcIcon.Glyph = "\uECCA"; ProgPinIcon.Glyph = "\uECA7"; ProgQrIcon.Glyph = "\uED14";

            // Light up based on stage
            if (_currentStage >= AuthenticationStage.WaitingForPIN)
            {
                ProgNfcIcon.Foreground = green; ProgNfcText.Foreground = green; ProgNfcIcon.Glyph = "\uE73E"; // Check
                ProgPinIcon.Foreground = blue; ProgPinText.Foreground = blue;
            }
            else
            {
                ProgNfcIcon.Foreground = blue; ProgNfcText.Foreground = blue;
            }

            if (_currentStage >= AuthenticationStage.WaitingForQR)
            {
                ProgPinIcon.Foreground = green; ProgPinText.Foreground = green; ProgPinIcon.Glyph = "\uE73E"; // Check
                ProgQrIcon.Foreground = blue; ProgQrText.Foreground = blue;
            }

            if (_currentStage == AuthenticationStage.AccessGranted)
            {
                ProgQrIcon.Foreground = green; ProgQrText.Foreground = green; ProgQrIcon.Glyph = "\uE73E"; // Check
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
                // Forces the WinUI 3 window into borderless kiosk mode
                appWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
            }
        }

        private void ApplyDynamicHeader()
        {
            // Restores the dynamic text at the top left of the screen
            KioskHeaderSubtitle.Text = $"{_originatingMode.ToUpper()} TERMINAL  •  {_contextDetails.ToUpper()}";
        }

        private void ExitKiosk_Click(object sender, RoutedEventArgs e)
        {
            // Restores the routing logic to go back to the correct previous screen
            if (_originatingMode == "Event")
            {
                var eventWindow = new EventAttendanceWindow();
                eventWindow.Activate();
            }
            else
            {
                var gateWindow = new VerificationWindow();
                gateWindow.Activate();
            }

            this.Close();
        }

        /* =========================================================================
         * DEBUG SIMULATION HOOKS
         * ========================================================================= */

        private void DebugSecurityLevel_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DebugSecurityLevel.SelectedIndex == 0) _currentSecurityLevel = SecurityLevel.Fast;
            if (DebugSecurityLevel.SelectedIndex == 1) _currentSecurityLevel = SecurityLevel.Standard;
            if (DebugSecurityLevel.SelectedIndex == 2) _currentSecurityLevel = SecurityLevel.High;
            SetState(AuthenticationStage.Idle);
        }

        private void SimulateNfc_Click(object sender, RoutedEventArgs e) => ProcessNfcScan("Justin Mason", "26-00001", true);

        private void SimulatePin_Click(object sender, RoutedEventArgs e)
        {
            ProcessHardwareKeypadStroke("1", false, false, false);
            ProcessHardwareKeypadStroke("2", false, false, false);
            ProcessHardwareKeypadStroke("3", false, false, false);
            ProcessHardwareKeypadStroke("4", false, false, false);
            ProcessHardwareKeypadStroke("", true, false, false); // Enter
        }

        private void SimulateQr_Click(object sender, RoutedEventArgs e) => ProcessQrScan("VALID_PAYLOAD", true);

        private void SimulateFail_Click(object sender, RoutedEventArgs e)
        {
            if (_currentStage == AuthenticationStage.Idle) ProcessNfcScan("UNKNOWN USER", "04:A1:B2:C3", false);
            else if (_currentStage == AuthenticationStage.WaitingForPIN) { ProcessHardwareKeypadStroke("9", false, false, false); ProcessHardwareKeypadStroke("", true, false, false); }
            else if (_currentStage == AuthenticationStage.WaitingForQR) ProcessQrScan("INVALID", false);
        }
    }
}