using System;
using System.IO.Ports;
using System.Linq;

namespace NFC_System
{
    public static class HardwareService
    {
        private static SerialPort? _serialPort;
        private static bool _lastConnectionStatus;
        private static string? _configuredPortName;
        private static readonly object SyncRoot = new();

        public static bool IsConnected => GetConnectionStatus();

        // Global events that any window can listen to
        public static event Action<string>? OnUidScanned;
        public static event Action<string>? OnKeypadInput;
        public static event Action<bool>? ConnectionStatusChanged;

        public static bool RefreshConnectionStatus()
        {
            bool isConnected = GetConnectionStatus();
            PublishConnectionStatus(isConnected);
            return isConnected;
        }

        public static bool TryReconnect()
        {
            string? portName;
            lock (SyncRoot)
            {
                portName = _configuredPortName;
            }

            return string.IsNullOrWhiteSpace(portName) ? false : Connect(portName);
        }

        private static bool GetConnectionStatus()
        {
            try
            {
                string? portName = _serialPort?.PortName;
                return _serialPort?.IsOpen == true &&
                    !string.IsNullOrWhiteSpace(portName) &&
                    SerialPort.GetPortNames().Contains(portName, StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        public static bool Connect(string portName)
        {
            lock (SyncRoot)
            {
                _configuredPortName = portName;

                // Reuse the existing port only when it is still physically present.
                if (_serialPort != null && _serialPort.IsOpen &&
                    string.Equals(_serialPort.PortName, portName, StringComparison.OrdinalIgnoreCase) &&
                    IsConnected)
                {
                    RefreshConnectionStatus();
                    return IsConnected;
                }

                DisposeCurrentPort();

                try
                {
                    _serialPort = new SerialPort(portName, 115200)
                    {
                        NewLine = "\n"
                    };
                    _serialPort.DataReceived += SerialPort_DataReceived;
                    _serialPort.ErrorReceived += SerialPort_ErrorReceived;
                    _serialPort.Open();
                    RefreshConnectionStatus();
                    return IsConnected;
                }
                catch
                {
                    DisposeCurrentPort();
                    return false;
                }
            }
        }

        public static void Disconnect()
        {
            lock (SyncRoot)
            {
                _configuredPortName = null;
                DisposeCurrentPort();
            }
        }

        private static void DisposeCurrentPort()
        {
            SerialPort? port = _serialPort;
            _serialPort = null;

            if (port != null)
            {
                try
                {
                    port.DataReceived -= SerialPort_DataReceived;
                    port.ErrorReceived -= SerialPort_ErrorReceived;
                    if (port.IsOpen) port.Close();
                    port.Dispose();
                }
                catch { }
            }

            PublishConnectionStatus(false);
        }

        public static void SendCommand(string command)
        {
            try
            {
                if (_serialPort != null && _serialPort.IsOpen)
                {
                    _serialPort.WriteLine(command);
                }
            }
            catch { RefreshConnectionStatus(); }
        }

        private static void SerialPort_ErrorReceived(object sender, SerialErrorReceivedEventArgs e)
        {
            RefreshConnectionStatus();
        }

        private static void PublishConnectionStatus(bool isConnected)
        {
            if (_lastConnectionStatus == isConnected) return;

            _lastConnectionStatus = isConnected;
            ConnectionStatusChanged?.Invoke(isConnected);
        }

        private static void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                if (_serialPort == null || !_serialPort.IsOpen) return;
                string line = _serialPort.ReadLine().Trim();

                if (line.StartsWith("UID="))
                {
                    string uid = line.Substring(4).Trim();
                    OnUidScanned?.Invoke(uid);
                }
                else if (line.StartsWith("KEY="))
                {
                    string key = line.Substring(4).Trim().ToUpper();
                    OnKeypadInput?.Invoke(key);
                }
            }
            catch { RefreshConnectionStatus(); }
        }
    }
}
