using System;
using System.IO.Ports;
using System.Threading.Tasks;

namespace NFC_System
{
    public static class HardwareService
    {
        private static SerialPort? _serialPort;

        // Global events that any window can listen to
        public static event Action<string>? OnUidScanned;
        public static event Action<string>? OnKeypadInput;

        public static bool Connect(string portName)
        {
            // If already connected to the same port, do nothing
            if (_serialPort != null && _serialPort.IsOpen)
            {
                if (_serialPort.PortName == portName) return true;
                Disconnect();
            }

            try
            {
                _serialPort = new SerialPort(portName, 115200);
                _serialPort.NewLine = "\n";
                _serialPort.DataReceived += SerialPort_DataReceived;
                _serialPort.Open();
                return true;
            }
            catch
            {
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
                        if (_serialPort.IsOpen) _serialPort.Close();
                        _serialPort.Dispose();
                    }
                    catch { }
                    finally { _serialPort = null; }
                });
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
            catch { }
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
            catch { }
        }
    }
}