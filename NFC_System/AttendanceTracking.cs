using System;

namespace NFC_System;

public sealed class AttendanceVisit
{
    public string VisitId { get; set; } = "";
    public string EntryTransactionId { get; set; } = "";
    public string? ExitTransactionId { get; set; }
    public DateTime TimeIn { get; set; }
    public DateTime? TimeOut { get; set; }
    public bool Confirmed { get; set; }
}

public sealed class AttendanceState
{
    public DateTime? LastTimestamp { get; set; }
    public string LastDeviceId { get; set; } = "";
    public long LastSequence { get; set; }
    public string EntryState { get; set; } = "OUTSIDE";
    public bool Unresolved { get; set; }
    public AttendanceVisit? Visit { get; set; }

    // Late or cross-device offline records cannot establish a trustworthy ordering.
    public bool Apply(string transactionId, string deviceId, long sequence, DateTime timestamp,
        bool entry, bool recovered, bool legacy)
    {
        if (LastTimestamp.HasValue && (timestamp <= LastTimestamp.Value ||
            (LastDeviceId == deviceId && sequence <= LastSequence)))
        {
            Unresolved = true;
            if (Visit != null) Visit.Confirmed = false;
            return false;
        }

        bool ambiguous = legacy || (recovered && LastTimestamp.HasValue && LastDeviceId != deviceId);
        if (entry)
        {
            bool valid = EntryState == "OUTSIDE" && !ambiguous;
            if (Visit != null && !Visit.TimeOut.HasValue) Visit.Confirmed = false;
            Visit = new AttendanceVisit
            {
                VisitId = transactionId, EntryTransactionId = transactionId,
                TimeIn = timestamp, Confirmed = valid && !Unresolved
            };
            // A fresh online entry after a known exit establishes a new baseline.
            if (valid && !recovered && !legacy) { Unresolved = false; Visit.Confirmed = true; }
            else if (!valid) Unresolved = true;
            EntryState = "INSIDE";
        }
        else
        {
            bool valid = EntryState == "INSIDE" && Visit != null && !Visit.TimeOut.HasValue && !ambiguous;
            if (valid)
            {
                Visit!.ExitTransactionId = transactionId;
                Visit.TimeOut = timestamp;
                Visit.Confirmed &= !Unresolved;
            }
            else
            {
                Unresolved = true;
                if (Visit != null) Visit.Confirmed = false;
            }
            EntryState = "OUTSIDE";
        }
        LastTimestamp = timestamp;
        LastDeviceId = deviceId;
        LastSequence = sequence;
        return true;
    }
}

public sealed class AttendanceCommitResult
{
    public bool IsGranted { get; init; } = true;
    public bool ConfirmationPending { get; init; }
    public string TransactionId { get; init; } = "";
    public DateTime Timestamp { get; init; }
    public bool Committed { get; init; }
    public AttendanceVisit? Visit { get; init; }
}
