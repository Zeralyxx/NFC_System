using Microsoft.UI.Xaml;
using System;
using System.Threading.Tasks;

namespace NFC_System
{
    public static class DatabaseMonitor
    {
        public static event Action<bool>? ConnectionStatusChanged;

        private static bool _isOnline = true;
        private static DispatcherTimer? _pollTimer;
        private static readonly DatabaseService _db = new();

        public static bool IsOnline => _isOnline;

        public static void StartMonitoring()
        {
            if (_pollTimer != null) return;

            _pollTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3)
            };
            _pollTimer.Tick += async (s, e) => await CheckConnectionAsync();
            _pollTimer.Start();
        }

        private static async Task CheckConnectionAsync()
        {
            bool currentlyOnline = await _db.TestConnectionAsync();

            if (currentlyOnline != _isOnline)
            {
                _isOnline = currentlyOnline;
                ConnectionStatusChanged?.Invoke(_isOnline);
            }
        }
    }
}