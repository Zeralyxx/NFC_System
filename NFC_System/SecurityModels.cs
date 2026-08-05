using System;

namespace NFC_System;

public enum VerificationMode { Fast, Standard, HighSecurity }
public enum TransactionType { Entry, Exit, EventAttendance }
public enum VerificationStep { Completed, RequiresPin, RequiresQr }

public sealed class StudentRecord
{
    public string StudentId { get; set; } = "";
    public string FullName { get; set; } = "";
    public string Course { get; set; } = "";
    public string Email { get; set; } = "";
    public string YearLevel { get; set; } = "";
    public string SectionName { get; set; } = "";
    public string Status { get; set; } = "Active";
    public string NfcUid { get; set; } = "";
    public string PinSalt { get; set; } = "";
    public string PinHash { get; set; } = "";
    public string QrCredential { get; set; } = "";
    public string EntryState { get; set; } = "OUTSIDE";
    public int FailedPinAttempts { get; set; }
    public bool PinLocked { get; set; }
    public DateTime? LastScanTimestamp { get; set; }
    public byte[]? PhotoData { get; set; }
    public bool IsTemporary { get; set; }
}

public sealed class EventRecord
{
    public string EventId { get; set; } = "";
    public string EventName { get; set; } = "";
    public VerificationMode VerificationMode { get; set; } = VerificationMode.Standard;
    public bool IsRestricted { get; set; }
    public string Status { get; set; } = "Active";
    public DateTime? EventDate { get; set; }

    public string DisplayName
    {
        get
        {
            string restriction = IsRestricted ? "Restricted" : "Open";
            return $"{EventId} - {EventName} ({DatabaseService.ToStorageValue(VerificationMode)}, {restriction})";
        }
    }

    public string IsRestrictedText => IsRestricted ? "Restricted" : "Open";
}

public sealed class VerificationSession
{
    public StudentRecord Student { get; init; } = new();
    public string Uid { get; init; } = "";
    public VerificationMode Mode { get; init; }
    public TransactionType TransactionType { get; init; }
    public string? EventId { get; init; }
    public bool IsQrFallback { get; init; } = false;

    // THE FIX: Explicit Separation of Human vs System Performance
    public double NfcSystemMs { get; set; }
    public double PinWorkflowMs { get; set; }
    public double PinSystemMs { get; set; }
    public double QrWorkflowMs { get; set; }
    public double QrSystemMs { get; set; }
    public double TotalDbQueryMs { get; set; }
}

public sealed class VerificationOutcome
{
    public VerificationStep Step { get; init; } = VerificationStep.Completed;
    public bool IsGranted { get; init; }
    public string ResultTitle { get; init; } = "";
    public string ResultMessage { get; init; } = "";
    public string ErrorCategory { get; init; } = "";
    public string LogLine { get; init; } = "";
    public StudentRecord? Student { get; init; }
    public VerificationSession? Session { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.now;
}