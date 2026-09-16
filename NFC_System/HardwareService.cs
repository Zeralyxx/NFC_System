using System;
using System.IO.Ports;
using System.Linq;
using System.Threading.Tasks;

namespace NFC_System
{
    public static class HardwareService
    {
        private static SerialPort? _serialPort;
        private static bool _lastConnectionStatus;

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
            // If already connected to the same port, do nothing
            if (_serialPort != null && _serialPort.IsOpen)
            {
                if (_serialPort.PortName == portName)
                {
                    RefreshConnectionStatus();
                    return IsConnected;
                }
                Disconnect();
            }

            try
            {
                _serialPort = new SerialPort(portName, 115200);
                _serialPort.NewLine = "\n";
                _serialPort.DataReceived += SerialPort_DataReceived;
                _serialPort.ErrorReceived += SerialPort_ErrorReceived;
                _serialPort.Open();
                RefreshConnectionStatus();
                return IsConnected;
            }
            catch
            {
                PublishConnectionStatus(false);
                return false;
            }
        }

        public static void Disconnect()
        {
            if (_serialPort != null)
            {
                Task.Run(() =>
                {
                    try
                    {
                        _serialPort.DataReceived -= SerialPort_DataReceived;
                        _serialPort.ErrorReceived -= SerialPort_ErrorReceived;
                        if (_serialPort.IsOpen) _serialPort.Close();
                        _serialPort.Dispose();
                    }
                    catch { }
                    finally
                    {
                        _serialPort = null;
                        PublishConnectionStatus(false);
                    }
                });
            }
            else
            {
                PublishConnectionStatus(false);
            }
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
