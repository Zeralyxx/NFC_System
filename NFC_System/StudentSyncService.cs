using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NFC_System;

public static class StudentSyncService
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly CancellationTokenSource Lifetime = new();
    private static StudentSyncCloud? _cloud;
    private static Task? _worker;
    public static string Status { get; private set; } = "Not connected";
    public static event Action? StatusChanged;
    public static bool IsAdministration => DatabaseService.ActiveConnection.Role == ServerConnectionRole.Administration;
    private static void SetStatus(string value) { Status = value; StatusChanged?.Invoke(); }

    public static void Start()
    {
        if (_worker != null) return;
        _worker = Task.Run(async () =>
        {
            while (!Lifetime.IsCancellationRequested)
            {
                try
                {
                    if (DatabaseService.ActiveConnection.Role == ServerConnectionRole.MainServer && StudentSyncOptions.Load() != null)
                    {
                        await Gate.WaitAsync(Lifetime.Token);
                        try { await ReceiveAsync(Lifetime.Token); }
                        finally { Gate.Release(); }
                    }
                    await Task.Delay(TimeSpan.FromMinutes(3), Lifetime.Token);
                }
                catch (OperationCanceledException) when (Lifetime.IsCancellationRequested) { break; }
                catch (Exception ex)
                {
                    SetStatus("Check failed: " + ex.Message);
                    try { await Task.Delay(TimeSpan.FromMinutes(3), Lifetime.Token); }
                    catch (OperationCanceledException) { break; }
                }
            }
        });
    }
    public static void Stop() => Lifetime.Cancel();

    public static async Task SignInAsync(string serverId, string email, string password)
    {
        long session = GraduationRules.RequireAdministrator(DatabaseMonitor.IsOnline);
        if (!Guid.TryParseExact(serverId.Trim(), "N", out _)) throw new ArgumentException("Enter the main server's 32-character Server ID.");
        if (DatabaseService.ActiveConnection.Role is not (ServerConnectionRole.MainServer or ServerConnectionRole.Administration))
            throw new InvalidOperationException("Cloud profile sync is for the Main Server or an Administration Device. LAN clients use the main database directly.");
        await Gate.WaitAsync();
        try
        {
            var options = new StudentSyncOptions(serverId.Trim(), email.Trim());
            var cloud = DatabaseService.CreateStudentSyncCloud(options);
            await cloud.SignInAsync(password, Lifetime.Token);
            GraduationRules.RequireAdministrator(DatabaseMonitor.IsOnline, session);
            options.Save();
            _cloud = cloud;
            SetStatus("Connected. " + (IsAdministration ? "Download main-server profiles before editing existing students." : "Automatic checks run every three minutes while the app is open."));
        }
        finally { Gate.Release(); }
    }

    public static async Task SignOutAsync()
    {
        GraduationRules.RequireAdministrator(DatabaseMonitor.IsOnline);
        await Gate.WaitAsync();
        try
        {
            var options = StudentSyncOptions.Load();
            if (options != null) (_cloud ?? DatabaseService.CreateStudentSyncCloud(options)).Forget();
            _cloud = null;
            StudentSyncOptions.Delete();
            SetStatus("Disconnected. Pending uploads remain available after signing in again.");
        }
        finally { Gate.Release(); }
    }

    private static StudentSyncCloud Cloud()
    {
        var options = StudentSyncOptions.Load() ?? throw new InvalidOperationException("Connect Student Profile Sync in Settings first.");
        return _cloud ??= DatabaseService.CreateStudentSyncCloud(options);
    }

    public static async Task CheckNowAsync()
    {
        GraduationRules.RequireAdministrator(DatabaseMonitor.IsOnline);
        await Gate.WaitAsync();
        try
        {
            if (IsAdministration) await ReconcileAsync(Lifetime.Token);
            else await ReceiveAsync(Lifetime.Token);
        }
        catch (Exception ex) { SetStatus(ex.Message); throw; }
        finally { Gate.Release(); }
    }

    private static async Task ReceiveAsync(CancellationToken ct)
    {
        if (DatabaseService.ActiveConnection.Role != ServerConnectionRole.MainServer)
            throw new InvalidOperationException("Only the Main Server can apply cloud profile changes.");
        var cloud = Cloud();
        await cloud.AuthorizeAsync(ct);
        var database = new DatabaseService();
        int applied = 0, review = 0;
        var outcomes = new System.Collections.Generic.List<(string Id, StudentSyncResult Result)>();
        foreach (var request in await cloud.PendingAsync(ct))
        {
            ct.ThrowIfCancellationRequested();
            // Approval is re-read for every batch, rather than trusting the uploader's payload or local role.
            var result = await database.ApplyStudentSyncAsync(request, cloud.AdminUids.Contains(request.AuthorUid));
            if (result.Status == "Applied") applied++; else review++;
            // The SQL receipt survives a network failure here; retries never apply the same change twice.
            outcomes.Add((request.Id, result));
        }
        var published = (await cloud.ProfilesAsync(ct)).ToDictionary(x => x.Profile.StudentId, x => x.Fingerprint, StringComparer.OrdinalIgnoreCase);
        var currentProfiles = await database.ReadSyncProfilesAsync();
        var currentIds = currentProfiles.Select(x => x.StudentId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var profile in currentProfiles)
            if (!published.TryGetValue(profile.StudentId, out string? hash) || hash != profile.Fingerprint())
                await cloud.PublishAsync(profile, ct);
        foreach (string deletedId in published.Keys.Where(id => !currentIds.Contains(id)))
            await cloud.DeleteProfileAsync(deletedId, ct);
        foreach (var outcome in outcomes) await cloud.AcknowledgeAsync(outcome.Id, outcome.Result, ct);
        SetStatus($"Checked {DateTime.Now:t}. Applied: {applied}; rejected/needs review: {review}. Next check in three minutes.");
    }

    private static async Task ReconcileAsync(CancellationToken ct)
    {
        var cloud = Cloud();
        await cloud.AuthorizeAsync(ct);
        var options = StudentSyncOptions.Load()!;
        var database = new DatabaseService();
        int pending = 0, applied = 0;
        var issues = new System.Collections.Generic.List<string>();
        foreach (var row in await database.ReadSyncBaselinesAsync(options.ServerId))
        {
            if (row.Value.RequestId.Length == 0) continue;
            var result = await cloud.RequestAsync(row.Value.RequestId, ct);
            if (result == null || result.Status == "Pending") { pending++; continue; }
            if (result.AuthorUid != cloud.Uid) throw new UnauthorizedAccessException("A pending upload belongs to another cloud account. Sign in with its uploader account.");
            if (result.Status == "Applied")
            {
                var submitted = JsonSerializer.Deserialize<StudentSyncRequest>(row.Value.RequestJson)!;
                await database.SaveSyncBaselineAsync(options.ServerId, row.Key,
                    new(result.ResultFingerprint, submitted.Profile.Fingerprint(), submitted.Profile.ToJson(), "", ""));
                applied++;
            }
            else issues.Add(row.Key + ": " + result.Status + ". " + result.Detail);
        }
        SetStatus($"Applied: {applied}; pending: {pending}." + (issues.Count > 0 ? " " + string.Join(" ", issues.Take(5)) : ""));
    }

    public static async Task<int> UploadAsync()
    {
        long session = GraduationRules.RequireAdministrator(DatabaseMonitor.IsOnline);
        if (!IsAdministration) throw new InvalidOperationException("Student changes are uploaded from an Administration Device.");
        await Gate.WaitAsync();
        try
        {
            await ReconcileAsync(Lifetime.Token);
            var cloud = Cloud();
            var options = StudentSyncOptions.Load()!;
            var database = new DatabaseService();
            var baselines = await database.ReadSyncBaselinesAsync(options.ServerId);
            var newIds = await database.ReadNewSyncProfileIdsAsync();
            int count = 0;
            foreach (var profile in await database.ReadSyncProfilesAsync())
            {
                baselines.TryGetValue(profile.StudentId, out var baseline);
                if (baseline == null && !newIds.Contains(profile.StudentId)) continue;
                if (baseline != null && profile.Fingerprint() == baseline.LocalFingerprint && baseline.RequestId.Length == 0) continue;
                StudentSyncRequest request;
                if (baseline?.RequestId.Length > 0)
                {
                    request = JsonSerializer.Deserialize<StudentSyncRequest>(baseline.RequestJson)!;
                    if (request.AuthorUid != cloud.Uid) throw new UnauthorizedAccessException("Sign in as the original uploader to resume pending changes.");
                    var result = await cloud.RequestAsync(request.Id, Lifetime.Token);
                    if (result != null && result.Status != "Pending")
                        throw new InvalidOperationException(profile.StudentId + ": " + result.Status + ". " + result.Detail);
                }
                else
                {
                    profile.Validate();
                    var previous = baseline == null ? null : StudentSyncProfile.Parse(baseline.ProfileJson);
                    bool reset = previous == null ? profile.PinHash.Length > 0 : previous.PinHash != profile.PinHash || previous.PinSalt != profile.PinSalt;
                    request = new(Guid.NewGuid().ToString("N"), cloud.Uid, baseline?.MainFingerprint ?? "", profile, reset);
                    // Persist the request ID before the network call so a lost response can be retried safely.
                    await database.SaveSyncBaselineAsync(options.ServerId, profile.StudentId,
                        new(baseline?.MainFingerprint ?? "", baseline?.LocalFingerprint ?? "", baseline?.ProfileJson ?? profile.ToJson(), request.Id, JsonSerializer.Serialize(request)));
                }
                GraduationRules.RequireAdministrator(DatabaseMonitor.IsOnline, session);
                await cloud.SubmitAsync(request, Lifetime.Token);
                count++;
            }
            SetStatus($"{count} change(s) uploaded or awaiting confirmation. Not effective on campus until the main server reports Applied.");
            return count;
        }
        catch (Exception ex) { SetStatus("Upload incomplete: " + ex.Message); throw; }
        finally { Gate.Release(); }
    }

    public static async Task<int> DownloadAsync(bool discardLocalChanges = false)
    {
        GraduationRules.RequireAdministrator(DatabaseMonitor.IsOnline);
        if (!IsAdministration) throw new InvalidOperationException("Download student profiles on an Administration Device only.");
        await Gate.WaitAsync();
        try
        {
            await ReconcileAsync(Lifetime.Token);
            var database = new DatabaseService();
            var options = StudentSyncOptions.Load()!;
            if (discardLocalChanges)
            {
                // Never abandon an upload the server could still apply. Resolve its outcome first.
                foreach (var row in await database.ReadSyncBaselinesAsync(options.ServerId))
                    if (row.Value.RequestId.Length > 0)
                    {
                        var result = await Cloud().RequestAsync(row.Value.RequestId, Lifetime.Token);
                        if (result == null) continue; // A failed, never-submitted local upload can be discarded.
                        if (result.Status == "Pending") throw new InvalidOperationException("The main server must finish pending uploads before local profiles can be replaced.");
                    }
                await database.ClearFinishedSyncRequestsAsync(options.ServerId);
            }
            int count = await database.DownloadSyncProfilesAsync(options.ServerId, await Cloud().ProfilesAsync(Lifetime.Token), discardLocalChanges);
            SetStatus($"Downloaded {count} main-server student profiles. Existing local-only profiles were not uploaded or deleted.");
            return count;
        }
        catch (Exception ex) { SetStatus("Download stopped: " + ex.Message); throw; }
        finally { Gate.Release(); }
    }
}
