using System;

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

        // 3. The method the Guard Window calls to change the state and notify the Kiosk
        public static void BroadcastModeChange(VerificationMode mode, TransactionType type)
        {
            CurrentMode = mode;
            CurrentType = type;
            ModeChanged?.Invoke(mode, type);
        }
    }
}
