namespace NFC_System;

// --- Pending Offline Log Models ---
public class PendingGateLog
{
    public string TransactionId { get; set; } = "";
    public string DeviceId { get; set; } = "";
    public long DeviceSequence { get; set; }
    public bool WasOffline { get; set; }
    public bool OnlineAttempt { get; set; }
    public bool IsLegacy { get; set; }
    public string EventId { get; set; } = "";
    public string Timestamp { get; set; } = "";
    public string StudentId { get; set; } = "";
    public string StudentName { get; set; } = "";
    public string NfcUid { get; set; } = "";
    public string TransactionType { get; set; } = "";
    public string VerificationMode { get; set; } = "";
    public bool IsGranted { get; set; }
    public string ErrorCode { get; set; } = "";
    public string Remarks { get; set; } = "";

    public double NfcSystemMs { get; set; }
    public double PinWorkflowMs { get; set; }
    public double PinSystemMs { get; set; }
    public double QrWorkflowMs { get; set; }
    public double QrSystemMs { get; set; }
    public double TotalWorkflowMs { get; set; }
    public double TotalSystemMs { get; set; }
    public double DbQuerySpeedMs { get; set; }
}

public class PendingEventAttendance
{
    public string TransactionId { get; set; } = "";
    public string DeviceId { get; set; } = "";
    public long DeviceSequence { get; set; }
    public string Timestamp { get; set; } = "";
    public string EventId { get; set; } = "";
    public string StudentId { get; set; } = "";
    public string VerificationMode { get; set; } = "";
    public string Status { get; set; } = "";
    public string Remarks { get; set; } = "";
}
