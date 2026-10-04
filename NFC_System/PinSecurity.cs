using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

namespace NFC_System;

public sealed class PinState
{
    [JsonRequired] public string StudentId { get; set; } = "";
    [JsonRequired] public string ResetToken { get; set; } = "";
    [JsonRequired] public string CredentialTag { get; set; } = "";
    [JsonRequired] public long Revision { get; set; }
    [JsonRequired] public int Attempts { get; set; }
    [JsonRequired] public bool Locked { get; set; }
    public PinState Copy() => (PinState)MemberwiseClone();
    public static string Tag(string salt, string hash) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(salt + ":" + hash)));
}

public sealed class PinAttempt
{
    [JsonRequired] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [JsonRequired] public string StudentId { get; set; } = "";
    [JsonRequired] public string ResetToken { get; set; } = "";
    [JsonRequired] public string CredentialTag { get; set; } = "";
    [JsonRequired] public long BaseRevision { get; set; }
    [JsonRequired] public bool Correct { get; set; }
    [JsonRequired] public bool Offline { get; set; }
}

public sealed class PinAttemptResult
{
    public PinState State { get; set; } = new();
    public bool Accepted { get; set; }
    public string Error { get; set; } = "";
}

public static class PinRules
{
    public static PinAttemptResult Apply(PinState current, PinAttempt attempt)
    {
        var state = current.Copy();
        var result = new PinAttemptResult { State = state };
        if (attempt.StudentId != state.StudentId || attempt.ResetToken != state.ResetToken || attempt.CredentialTag != state.CredentialTag)
            result.Error = "PIN_STATE_CHANGED";
        else if (state.Locked) result.Error = "PIN_LOCKED";
        else
        {
            if (!attempt.Correct)
            {
                state.Attempts = Math.Min(3, state.Attempts + 1);
                state.Locked = state.Attempts >= 3;
                result.Error = state.Locked ? "PIN_LOCKED" : "PIN_FAILURE";
            }
            else
            {
                // Recovered success cannot erase failures it never observed on another device.
                if (!attempt.Offline || attempt.BaseRevision == state.Revision) state.Attempts = 0;
                result.Accepted = true;
            }
            state.Revision++;
        }
        return result;
    }
}

public sealed class PinJournalSnapshot
{
    [JsonRequired] public Dictionary<string, PinState> States { get; set; } = new();
    [JsonRequired] public List<PinAttempt> Pending { get; set; } = new();
}

// Confirmed states and pending attempts move together in one flushed, atomically replaced file.
public sealed class DurablePinJournal
{
    private readonly string _path;
    public DurablePinJournal(string directory) => _path = Path.Combine(directory, "pin_security.json");
    public PinJournalSnapshot Snapshot() => Access(s => s);
    public PinState Read(string studentId) => Access(s =>
    {
        if (s.Pending.Any(x => x.StudentId == studentId && !x.Offline))
            throw new InvalidOperationException("An online PIN attempt has an uncertain outcome. Reconnect before verification.");
        return Project(s, studentId);
    });
    public void Confirm(IEnumerable<PinState> states) => Access(s =>
    {
        foreach (var state in states) s.States[state.StudentId] = state.Copy();
        return true;
    }, true);
    public void Acknowledge(string id, PinState state) => Access(s =>
    {
        s.States[state.StudentId] = state.Copy();
        s.Pending.RemoveAll(x => x.Id == id);
        return true;
    }, true);
    public PinAttempt Append(PinState expected, bool correct, bool offline) => Access(s =>
    {
        var current = Project(s, expected.StudentId);
        if (current.Locked || current.ResetToken != expected.ResetToken || current.CredentialTag != expected.CredentialTag)
            throw new InvalidOperationException("PIN state changed. Start a new verification attempt.");
        var attempt = new PinAttempt { StudentId = current.StudentId, ResetToken = current.ResetToken,
            CredentialTag = current.CredentialTag, BaseRevision = current.Revision, Correct = correct, Offline = offline };
        s.Pending.Add(attempt);
        return attempt;
    }, true);
    private static PinState Project(PinJournalSnapshot snapshot, string id)
    {
        if (!snapshot.States.TryGetValue(id, out var state))
            throw new InvalidOperationException("PIN security state needs an online refresh before offline verification.");
        state = state.Copy();
        foreach (var pending in snapshot.Pending.Where(x => x.StudentId == id)) state = PinRules.Apply(state, pending).State;
        return state;
    }
    private T Access<T>(Func<PinJournalSnapshot, T> action, bool write = false)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        using var lease = AcquireLease();
        bool exists = File.Exists(_path);
        var state = exists ? JsonSerializer.Deserialize<PinJournalSnapshot>(File.ReadAllText(_path))
            ?? throw new InvalidDataException("Invalid PIN journal.") : new PinJournalSnapshot();
        if (state.States == null || state.Pending == null || state.States.Any(x => x.Value == null || x.Key != x.Value.StudentId ||
            !Guid.TryParseExact(x.Value.ResetToken, "N", out _) || x.Value.Revision < 0 || x.Value.Attempts < 0 || x.Value.Attempts > 3) ||
            state.Pending.Any(x => x == null || !Guid.TryParseExact(x.Id, "N", out _) || !state.States.ContainsKey(x.StudentId) ||
                !Guid.TryParseExact(x.ResetToken, "N", out _) || string.IsNullOrWhiteSpace(x.CredentialTag) || x.BaseRevision < 0) ||
            state.Pending.Select(x => x.Id).Distinct().Count() != state.Pending.Count)
            throw new InvalidDataException("Invalid PIN journal. Preserve it for administrator recovery.");
        T result = action(state);
        if (write)
        {
            string temporary = _path + ".tmp";
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, state);
                stream.Flush(true);
            }
            if (exists) File.Replace(temporary, _path, null); else File.Move(temporary, _path);
        }
        return result;
    }
    private FileStream AcquireLease()
    {
        for (int attempt = 0; ; attempt++)
        {
            try { return new FileStream(_path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (attempt < 100) { Thread.Sleep(20); }
        }
    }
}
