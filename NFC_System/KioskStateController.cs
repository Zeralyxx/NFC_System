using System;

namespace NFC_System
{
    public static class KioskStateController
    {
        // 1. Globally store the current active states
        public static VerificationMode CurrentMode { get; private set; } = VerificationMode.Standard;
        public static TransactionType CurrentType { get; private set; } = TransactionType.Entry;

        // NEW: Store the Hardware ID of the selected webcam
        public static string SelectedCameraId { get; set; } = "";

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