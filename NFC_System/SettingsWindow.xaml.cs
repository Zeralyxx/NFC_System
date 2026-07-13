using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.IO.Ports;
using WinRT.Interop;

namespace NFC_System
{
    public sealed partial class SettingsWindow : Window
    {
        private AppSettings _settings;

        public SettingsWindow()
        {
            this.InitializeComponent();
            MaximizeWindow();

            _settings = AppSettings.Load();
            PopulatePortLists();
        }

        // ─── Populate dropdowns with available COM ports ───────────────

        private void PopulatePortLists()
        {
            string[] ports = SerialPort.GetPortNames();

            if (ports.Length == 0)
            {
                Log("[WARNING] No COM ports detected. Connect your Arduino devices and reopen Settings.");
                VerificationPortComboBox.Items.Add("No ports found");
                EventPortComboBox.Items.Add("No ports found");
                return;
            }

            foreach (string port in ports)
            {
                VerificationPortComboBox.Items.Add(port);
                EventPortComboBox.Items.Add(port);
            }

            // Restore saved selections
            VerificationPortComboBox.SelectedItem = _settings.VerificationComPort;
            EventPortComboBox.SelectedItem        = _settings.EventComPort;

            Log($"[INFO] Found {ports.Length} COM port(s): {string.Join(", ", ports)}");
            Log($"[INFO] Current settings — Reader 1: {_settings.VerificationComPort}  |  Reader 2: {_settings.EventComPort}");
        }

        // ─── Test connections ──────────────────────────────────────────

        private void TestPortsButton_Click(object sender, RoutedEventArgs e)
        {
            TestPort(
                VerificationPortComboBox.SelectedItem?.ToString(),
                VerificationStatusDot,
                "Reader 1 (Verification)");

            TestPort(
                EventPortComboBox.SelectedItem?.ToString(),
                EventStatusDot,
                "Reader 2 (Event)");
        }

        private void TestPort(string? portName, Microsoft.UI.Xaml.Shapes.Ellipse? dot, string label)
        {
            // Note: SettingsWindow XAML uses Border for the dot, not Ellipse
            // We update it via the helper below
        }

        private void TestPort(string? portName, Border dot, string label)
        {
            if (string.IsNullOrWhiteSpace(portName) || portName == "No ports found")
            {
                dot.Background = new SolidColorBrush(Colors.Gray);
                Log($"[{label}] No port selected.");
                return;
            }

            try
            {
                using var port = new SerialPort(portName, 115200);
                port.Open();
                port.Close();

                dot.Background = new SolidColorBrush(Colors.LimeGreen);
                Log($"[{label}] ✓ {portName} — connected successfully.");
            }
            catch (Exception ex)
            {
                dot.Background = new SolidColorBrush(Colors.Firebrick);
                Log($"[{label}] ✗ {portName} — {ex.Message}");
            }
        }

        // ─── Save ──────────────────────────────────────────────────────

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            string? verPort   = VerificationPortComboBox.SelectedItem?.ToString();
            string? eventPort = EventPortComboBox.SelectedItem?.ToString();

            if (string.IsNullOrWhiteSpace(verPort) || verPort == "No ports found" ||
                string.IsNullOrWhiteSpace(eventPort) || eventPort == "No ports found")
            {
                Log("[ERROR] Please select a valid port for both readers before saving.");
                return;
            }

            if (verPort == eventPort)
            {
                Log("[ERROR] Both readers cannot share the same COM port. Each reader needs its own port.");
                return;
            }

            _settings.VerificationComPort = verPort;
            _settings.EventComPort        = eventPort;
            _settings.Save();

            Log($"[SUCCESS] Settings saved — Reader 1: {verPort}  |  Reader 2: {eventPort}");
            Log("[INFO] Changes take effect the next time Verification or Event Attendance windows are opened.");
        }

        // ─── Navigation ────────────────────────────────────────────────

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            var dashboard = new MainWindow();
            dashboard.Activate();
            this.Close();
        }

        // ─── Helpers ───────────────────────────────────────────────────

        private void Log(string message)
        {
            StatusLogListView.Items.Insert(0, message);
        }

        private void MaximizeWindow()
        {
            IntPtr hWnd = WindowNative.GetWindowHandle(this);
            WindowId windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
            AppWindow appWindow = AppWindow.GetFromWindowId(windowId);

            if (appWindow.Presenter is OverlappedPresenter presenter)
                presenter.Maximize();
        }
    }
}
