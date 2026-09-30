using System;
using System.Collections.Generic;

namespace NFC_System
{
    public static class KioskStateController
    {
        // 1. Globally store the current active states
        public static VerificationMode CurrentMode { get; private set; } = VerificationMode.Standard;
        public static TransactionType CurrentType { get; private set; } = TransactionType.Entry;

        // NEW: Store the Hardware ID of the selected webcam
        private static string _selectedCameraId = "";
        public static string SelectedCameraId
        {
            get => _selectedCameraId;
            set
            {
                string cameraId = value ?? "";
                if (_selectedCameraId == cameraId) return;
                _selectedCameraId = cameraId;
                CameraChanged?.Invoke(cameraId);
            }
        }
        public static event Action<string>? CameraChanged;

        // 2. The live event that the Kiosk screen listens to
        public static event Action<VerificationMode, TransactionType>? ModeChanged;
        public static event Action<string, VerificationMode, TransactionType>? EventStateChanged;
        public static event Action<string, VerificationMode>? EventModeChanged;
        private static readonly Dictionary<string, (VerificationMode Mode, TransactionType Type)> EventStates = new(StringComparer.Ordinal);

        public static (VerificationMode Mode, TransactionType Type) GetEventState(string eventId)
        {
            lock (EventStates)
                return EventStates.TryGetValue(eventId, out var state) ? state : (VerificationMode.Standard, TransactionType.EventAttendance);
        }

        public static void BroadcastEventStateChange(string eventId, VerificationMode mode, TransactionType direction)
        {
            if (string.IsNullOrWhiteSpace(eventId)) return;
            lock (EventStates) EventStates[eventId] = (mode, direction);
            EventStateChanged?.Invoke(eventId, mode, direction);
        }

        public static void BroadcastEventModeChange(string eventId, VerificationMode mode)
        {
            if (string.IsNullOrWhiteSpace(eventId)) return;
            lock (EventStates)
            {
                var current = GetEventState(eventId);
                EventStates[eventId] = (mode, current.Type);
            }
            EventModeChanged?.Invoke(eventId, mode);
        }

        // 3. The method the Guard Window calls to change the state and notify the Kiosk
        public static void BroadcastModeChange(VerificationMode mode, TransactionType type)
        {
            CurrentMode = mode;
            CurrentType = type;
            ModeChanged?.Invoke(mode, type);
        }
    }
}
