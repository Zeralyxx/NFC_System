using System;

namespace NFC_System
{
    public static class KioskStateController
    {
        // 1. Globally store the current active states
        public static VerificationMode CurrentMode { get; private set; } = VerificationMode.Standard;
        public static TransactionType CurrentType { get; private set; } = TransactionType.Entry;

        // 2. The live event that the Kiosk screen listens to
        public static event Action<VerificationMode, TransactionType>? ModeChanged;

        // 3. The method the Guard Window calls to change the state and notify the Kiosk
        public static void BroadcastModeChange(VerificationMode mode, TransactionType type)
        {
            // Save the new choices into memory
            CurrentMode = mode;
            CurrentType = type;

            // Fire the alert to instantly update the Kiosk UI
            ModeChanged?.Invoke(mode, type);
        }
    }
}