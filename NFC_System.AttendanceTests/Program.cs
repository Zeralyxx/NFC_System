using NFC_System;
using MySqlConnector;
using System.Globalization;
using System.Text.Json;

int passed = 0, failed = 0;
void Assert(bool value, string message = "Assertion failed") { if (!value) throw new Exception(message); }
async Task Test(string name, Func<Task> test)
{
    try { await test(); passed++; Console.WriteLine($"PASS {name}"); }
    catch (Exception ex) { failed++; Console.WriteLine($"FAIL {name}: {ex}"); }
}
string NewDirectory() { string path = Path.Combine(Path.GetTempPath(), "nfc-attendance-tests", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(path); return path; }
PendingGateLog Log(string type = "Entry", string student = "A", int minute = 0) => new()
{
    StudentId = student, StudentName = student, TransactionType = type, VerificationMode = "Fast",
    IsGranted = true, Timestamp = new DateTime(2026, 9, 19, 9, minute, 0).ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)
};

await Test("Outbox survives restart and snapshot never removes records", () =>
{
    string path = NewDirectory(); var q = new DurableAttendanceQueue(path); var a = q.Enqueue(Log());
    var reloaded = new DurableAttendanceQueue(path).Snapshot();
    Assert(reloaded.GateLogs.Single().TransactionId == a.TransactionId);
    Assert(q.Snapshot().GateLogs.Count == 1 && reloaded.DeviceId == a.DeviceId);
    return Task.CompletedTask;
});
await Test("Acknowledgment removes only its transaction, including concurrent arrivals", () =>
{
    var q = new DurableAttendanceQueue(NewDirectory()); var a = q.Enqueue(Log());
    var batch = q.Snapshot(); var b = q.Enqueue(Log("Exit", minute: 1));
    q.Acknowledge(batch.GateLogs.Single().TransactionId); q.Acknowledge(a.TransactionId);
    Assert(q.Snapshot().GateLogs.Single().TransactionId == b.TransactionId);
    return Task.CompletedTask;
});
await Test("Concurrent queue instances allocate unique durable device sequences", async () =>
{
    string path = NewDirectory();
    await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() => new DurableAttendanceQueue(path).Enqueue(Log()))));
    var s = new DurableAttendanceQueue(path).Snapshot();
    Assert(s.LastSequence == 20 && s.GateLogs.Select(x => x.DeviceSequence).Distinct().Count() == 20);
});
await Test("Legacy files migrate once without deleting originals", () =>
{
    string path = NewDirectory(); string legacy = Path.Combine(path, "offline_gate_logs.json");
    File.WriteAllText(legacy, JsonSerializer.Serialize(new[] { Log() }));
    var q = new DurableAttendanceQueue(path); var log = q.Snapshot().GateLogs.Single();
    Assert(log.IsLegacy && File.Exists(legacy)); q.Acknowledge(log.TransactionId);
    Assert(new DurableAttendanceQueue(path).Snapshot().GateLogs.Count == 0);
    return Task.CompletedTask;
});
foreach (string corrupt in new[] { "{bad", "{}", "null" })
await Test($"Corrupt outbox fails closed: {corrupt}", () =>
{
    string path = NewDirectory(); string file = Path.Combine(path, "attendance_outbox.json"); File.WriteAllText(file, corrupt);
    bool threw = false; try { new DurableAttendanceQueue(path).Enqueue(Log()); } catch { threw = true; }
    Assert(threw && File.ReadAllText(file) == corrupt); return Task.CompletedTask;
});
await Test("Malformed legacy queue is preserved rather than overwritten", () =>
{
    string path = NewDirectory(); File.WriteAllText(Path.Combine(path, "offline_gate_logs.json"), "broken");
    bool threw = false; try { new DurableAttendanceQueue(path).Snapshot(); } catch { threw = true; }
    Assert(threw && !File.Exists(Path.Combine(path, "attendance_outbox.json"))); return Task.CompletedTask;
});

AttendanceBackupSnapshot EmptyBackup(DateTime captured) => new()
{
    CapturedAtUtc = captured,
    Tables = AttendanceBackupSnapshot.TableNames.ToDictionary(t => t, _ => new List<Dictionary<string, JsonElement>>()),
    NextLogIds = AttendanceBackupSnapshot.TableNames.Skip(5).ToDictionary(t => t, _ => 1L)
};
await Test("Cloud snapshot roundtrip includes every table and multiple chunks", async () =>
{
    using var http = new HttpClient(new FakeFirestore());
    var store = new AttendanceBackupStore(http, "https://test.invalid/documents", "test");
    var snapshot = EmptyBackup(DateTime.UtcNow);
    snapshot.Tables["fast_mode_logs"].Add(new() { ["id"] = JsonSerializer.SerializeToElement(1), ["remarks"] = JsonSerializer.SerializeToElement(Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(300000))) });
    snapshot.NextLogIds["fast_mode_logs"] = 2;
    await store.UploadAsync(snapshot);
    var restored = await store.DownloadLatestAsync();
    Assert(restored!.Tables.Count == 9 && restored.Tables["fast_mode_logs"][0]["remarks"].GetString() == snapshot.Tables["fast_mode_logs"][0]["remarks"].GetString());
});
await Test("Incomplete upload never publishes a restorable generation", async () =>
{
    var handler = new FakeFirestore(); using var http = new HttpClient(handler);
    var store = new AttendanceBackupStore(http, "https://test.invalid/documents", "test");
    var original = EmptyBackup(DateTime.UtcNow.AddMinutes(-1)); await store.UploadAsync(original);
    handler.FailManifest = true; bool threw = false;
    try { await store.UploadAsync(EmptyBackup(DateTime.UtcNow)); } catch (HttpRequestException) { threw = true; }
    Assert(threw && (await store.DownloadLatestAsync())!.CapturedAtUtc == original.CapturedAtUtc);
});
await Test("Backup selection uses capture time, not upload completion order", async () =>
{
    using var http = new HttpClient(new FakeFirestore()); var store = new AttendanceBackupStore(http, "https://test.invalid/documents", "test");
    var latest = EmptyBackup(DateTime.UtcNow); await store.UploadAsync(latest);
    await store.UploadAsync(EmptyBackup(DateTime.UtcNow.AddHours(-1)));
    Assert((await store.DownloadLatestAsync())!.CapturedAtUtc == latest.CapturedAtUtc);
});
foreach (string fault in new[] { "missing", "checksum", "permission" })
await Test($"Cloud restore fails closed on {fault} failure", async () =>
{
    var handler = new FakeFirestore(); using var http = new HttpClient(handler);
    var store = new AttendanceBackupStore(http, "https://test.invalid/documents", "test");
    await store.UploadAsync(EmptyBackup(DateTime.UtcNow));
    handler.LoseChunk = fault == "missing"; handler.CorruptChunk = fault == "checksum"; handler.FailAll = fault == "permission";
    bool threw = false; try { await store.DownloadLatestAsync(); } catch { threw = true; }
    Assert(threw);
});

bool sqlEnabled = args.Contains("--mysql");
await DeviceAttributionTests.RunAsync(Test);
if (sqlEnabled)
{
    using var admin = new MySqlConnection(DatabaseService.ConnectionString.Replace("Database=attendance_tests;", ""));
    await admin.OpenAsync();
    using (var create = new MySqlCommand("CREATE DATABASE IF NOT EXISTS attendance_tests", admin)) await create.ExecuteNonQueryAsync();
    using var connection = new MySqlConnection(DatabaseService.ConnectionString); await connection.OpenAsync();
    async Task Sql(string sql) { using var cmd = new MySqlCommand(sql, connection); await cmd.ExecuteNonQueryAsync(); }
    async Task<string> Scalar(string sql) { using var cmd = new MySqlCommand(sql, connection); return (await cmd.ExecuteScalarAsync())?.ToString() ?? ""; }
    await Sql("CREATE TABLE IF NOT EXISTS students (student_id VARCHAR(50) PRIMARY KEY, entry_state VARCHAR(20)) ENGINE=InnoDB");
    foreach (string table in new[] { "fast_mode_logs", "standard_mode_logs", "high_security_mode_logs" })
        await Sql($@"CREATE TABLE IF NOT EXISTS {table} (id INT AUTO_INCREMENT PRIMARY KEY, timestamp DATETIME(3), student_id VARCHAR(50),
            student_name VARCHAR(100), nfc_uid VARCHAR(50), transaction_type VARCHAR(30), verification_mode VARCHAR(30), is_granted BOOLEAN,
            error_code VARCHAR(100), remarks TEXT, nfc_system_ms DOUBLE, pin_workflow_ms DOUBLE, pin_system_ms DOUBLE, qr_workflow_ms DOUBLE,
            qr_system_ms DOUBLE, total_workflow_ms DOUBLE, total_system_ms DOUBLE, db_query_speed_ms DOUBLE, synced_to_cloud BOOLEAN DEFAULT FALSE) ENGINE=InnoDB");
    await Sql(@"CREATE TABLE IF NOT EXISTS event_attendance (id INT AUTO_INCREMENT PRIMARY KEY, timestamp DATETIME(3), event_id VARCHAR(50),
        student_id VARCHAR(50), verification_mode VARCHAR(30), status VARCHAR(30), remarks TEXT) ENGINE=InnoDB");
    await Sql(@"CREATE TABLE IF NOT EXISTS alerts (id INT AUTO_INCREMENT PRIMARY KEY, timestamp DATETIME(3), student_id VARCHAR(50),
        alert_type VARCHAR(100), message TEXT, synced_to_cloud BOOLEAN DEFAULT FALSE) ENGINE=InnoDB");
    await DatabaseService.CreateAttendanceSchemaAsync(connection);
    var db = new DatabaseService(); db.ActivateAttendanceDevice();
    async Task Reset()
    {
        foreach (string table in new[] { "attendance_decisions", "attendance_receipts", "attendance_devices", "attendance_current", "attendance_visits", "fast_mode_logs", "standard_mode_logs", "high_security_mode_logs", "event_attendance", "students", "alerts" }) await Sql($"DELETE FROM {table}");
        await Sql("INSERT INTO students VALUES ('A','OUTSIDE'),('B','OUTSIDE')");
        OfflineCacheService.AttendanceQueue = new DurableAttendanceQueue(NewDirectory());
        OfflineCacheService.CachedState = "OUTSIDE";
        OfflineCacheService.FailCacheFlush = false;
        OfflineCacheService.FailFlushNumber = 0;
        OfflineCacheService.FlushCount = 0;
        db.FailCacheRefresh = false;
        DatabaseService.Clock = new(2026, 9, 19, 9, 0, 0);
    }
    Task<AttendanceCommitResult> Scan(string type, int minute, bool offline = false, string? eventId = null)
    {
        DatabaseService.Clock = new(2026, 9, 19, 9, minute, 0);
        return db.RecordAttendanceTransactionAsync(new VerificationSession
        {
            Student = new StudentRecord { StudentId = "A", FullName = "Student A" },
            TransactionType = Enum.Parse<TransactionType>(type), Mode = VerificationMode.Fast, IsOffline = offline, EventId = eventId
        }, "test");
    }
    await Test("Online entry and exit return a single confirmed visit", async () =>
    {
        await Reset(); var entry = await Scan("Entry", 0); var exit = await Scan("Exit", 1);
        Assert(entry.Committed && entry.Visit?.TimeOut == null && entry.Visit?.Confirmed == true);
        Assert(exit.Visit?.VisitId == entry.Visit!.VisitId && exit.Visit?.TimeOut == DatabaseService.Clock);
        Assert(await Scalar("SELECT entry_state FROM students WHERE student_id='A'") == "OUTSIDE");
    });
    await Test("Offline display has no history; ordered recovery pairs one-device visits", async () =>
    {
        await Reset(); var entry = await Scan("Entry", 0, true); var exit = await Scan("Exit", 1, true);
        Assert(!entry.Committed && entry.Visit == null && !exit.Committed);
        Assert(OfflineCacheService.AttendanceQueue.Snapshot().GateLogs.Count == 2);
        await db.PulseAttendanceAsync();
        Assert(!OfflineCacheService.HasPendingLogs() && await Scalar("SELECT COUNT(*) FROM fast_mode_logs") == "2");
        Assert(await DatabaseService.ReadVisitForTestAsync("A", exit.TransactionId) != null);
        Assert(db.CacheRefreshes > 0);
    });
    await Test("Crash after DB commit before acknowledgment does not duplicate on retry", async () =>
    {
        await Reset(); await Scan("Entry", 0, true); var log = OfflineCacheService.AttendanceQueue.Snapshot().GateLogs.Single();
        await Sql($"INSERT INTO attendance_devices (device_id,last_seen,reported_sequence,acknowledged_sequence,ready,enabled) VALUES ('{log.DeviceId}',UTC_TIMESTAMP(),1,0,FALSE,TRUE)");
        await DatabaseService.CommitForTestAsync(connection, log);
        Assert(OfflineCacheService.HasPendingLogs());
        await db.PulseAttendanceAsync();
        Assert(!OfflineCacheService.HasPendingLogs());
        Assert(await Scalar("SELECT COUNT(*) FROM fast_mode_logs") == "1");
        Assert(await Scalar("SELECT COUNT(*) FROM attendance_visits") == "1");
    });
    await Test("Failed SQL transaction retains the queue and rolls back receipt/log/state", async () =>
    {
        await Reset(); await Scan("Entry", 0, true);
        await Sql("CREATE TRIGGER fail_attendance BEFORE INSERT ON attendance_visits FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='injected failure'");
        try
        {
            await db.PulseAttendanceAsync();
            Assert(OfflineCacheService.HasPendingLogs());
            Assert(await Scalar("SELECT COUNT(*) FROM fast_mode_logs") == "0");
            Assert(await Scalar("SELECT COUNT(*) FROM attendance_receipts") == "0");
            Assert(await Scalar("SELECT entry_state FROM students WHERE student_id='A'") == "OUTSIDE");
        }
        finally { await Sql("DROP TRIGGER fail_attendance"); }
        await db.PulseAttendanceAsync(); Assert(!OfflineCacheService.HasPendingLogs());
    });
    await Test("A partial batch retains later records and resumes without duplicates", async () =>
    {
        await Reset(); await Scan("Entry", 0, true); await Scan("Exit", 1, true);
        await Sql("CREATE TRIGGER fail_exit BEFORE INSERT ON fast_mode_logs FOR EACH ROW BEGIN IF NEW.transaction_type='Exit' THEN SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='exit failure'; END IF; END");
        try { await db.PulseAttendanceAsync(); Assert(await Scalar("SELECT COUNT(*) FROM fast_mode_logs") == "1"); Assert(OfflineCacheService.AttendanceQueue.Snapshot().GateLogs.Count == 1); }
        finally { await Sql("DROP TRIGGER fail_exit"); }
        await db.PulseAttendanceAsync(); Assert(await Scalar("SELECT COUNT(*) FROM fast_mode_logs") == "2");
    });
    await Test("A late offline entry never overwrites a newer exit", async () =>
    {
        await Reset(); await Scan("Entry", 1); var exit = await Scan("Exit", 3);
        OfflineCacheService.AttendanceQueue.Enqueue(Log("Entry", minute: 2)); await db.PulseAttendanceAsync();
        Assert(await Scalar("SELECT entry_state FROM students WHERE student_id='A'") == "OUTSIDE");
        Assert(await DatabaseService.ReadVisitForTestAsync("A", exit.TransactionId) == null);
    });
    await Test("Purged logs do not erase an open visit or prevent pairing its exit", async () =>
    {
        await Reset(); var entry = await Scan("Entry", 0); await Sql("DELETE FROM fast_mode_logs");
        var exit = await Scan("Exit", 1); Assert(exit.Visit?.VisitId == entry.Visit?.VisitId && exit.Visit?.Confirmed == true);
    });
    await Test("Existing INSIDE profile without history cannot manufacture a time-in", async () =>
    {
        await Reset(); await Sql("UPDATE students SET entry_state='INSIDE' WHERE student_id='A'");
        var exit = await Scan("Exit", 1); Assert(exit.Committed && exit.Visit == null);
        Assert((await Scan("Entry", 2)).Visit?.Confirmed == true);
    });
    await Test("Stale peer hides history until the peer reports a reconciled queue", async () =>
    {
        await Reset(); await Sql("INSERT INTO attendance_devices (device_id,last_seen,reported_sequence,acknowledged_sequence,ready,enabled) VALUES ('peer',UTC_TIMESTAMP()-INTERVAL 1 MINUTE,2,1,FALSE,TRUE)");
        var entry = await Scan("Entry", 0); Assert(entry.Visit == null);
        await Sql("UPDATE attendance_devices SET last_seen=UTC_TIMESTAMP(),acknowledged_sequence=2,ready=TRUE WHERE device_id='peer'");
        Assert(await DatabaseService.ReadVisitForTestAsync("A", entry.TransactionId) != null);
    });
    await Test("Reported ready with an acknowledgment gap still hides history", async () =>
    {
        await Reset(); await Sql("INSERT INTO attendance_devices (device_id,last_seen,reported_sequence,acknowledged_sequence,ready,enabled) VALUES ('peer',UTC_TIMESTAMP(),2,1,TRUE,TRUE)");
        Assert((await Scan("Entry", 0)).Visit == null);
    });
    await Test("Cross-device offline exit is unresolved rather than guessed", async () =>
    {
        await Reset(); await Scan("Entry", 0);
        var other = new DurableAttendanceQueue(NewDirectory()); var log = other.Enqueue(Log("Exit", minute: 1));
        await Sql($"INSERT INTO attendance_devices (device_id,last_seen,reported_sequence,acknowledged_sequence,ready,enabled) VALUES ('{log.DeviceId}',UTC_TIMESTAMP(),1,0,FALSE,TRUE)");
        await DatabaseService.CommitForTestAsync(connection, log);
        Assert(await Scalar("SELECT confirmed FROM attendance_visits") == "False" || await Scalar("SELECT confirmed FROM attendance_visits") == "0");
    });
    await Test("Denied scans do not create visits or change entry state", async () =>
    {
        await Reset(); var log = Log(); log.IsGranted = false; OfflineCacheService.AttendanceQueue.Enqueue(log); await db.PulseAttendanceAsync();
        Assert(await Scalar("SELECT COUNT(*) FROM attendance_visits") == "0");
        Assert(await Scalar("SELECT entry_state FROM students WHERE student_id='A'") == "OUTSIDE");
    });
    await Test("Offline event entry and exit recover once without creating gate visits", async () =>
    {
        await Reset(); await Scan("EventAttendance", 0, true, "event-1"); await Scan("Exit", 1, true, "event-1");
        await db.PulseAttendanceAsync(); await db.PulseAttendanceAsync();
        Assert(await Scalar("SELECT COUNT(*) FROM event_attendance") == "2");
        Assert(await Scalar("SELECT COUNT(*) FROM attendance_visits") == "0");
        Assert(await Scalar("SELECT entry_state FROM students WHERE student_id='A'") == "INSIDE");
    });
    await Test("Concurrent recovery calls do not duplicate records", async () =>
    {
        await Reset(); await Scan("Entry", 0, true); await Scan("Exit", 1, true);
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => db.PulseAttendanceAsync()));
        Assert(await Scalar("SELECT COUNT(*) FROM fast_mode_logs") == "2");
    });
    await Test("Concurrent online admissions grant only one entry", async () =>
    {
        await Reset();
        var results = await Task.WhenAll(Scan("Entry", 0), Scan("Entry", 1));
        Assert(results.Count(x => x.IsGranted) == 1);
        Assert(await Scalar("SELECT COUNT(*) FROM attendance_visits WHERE confirmed=TRUE") == "1");
        Assert(await Scalar("SELECT COUNT(*) FROM fast_mode_logs WHERE is_granted=FALSE") == "1");
    });
    await Test("Manual attendance correction invalidates the old visit and survives late replay", async () =>
    {
        await Reset(); var entry = await Scan("Entry", 0);
        DatabaseService.Clock = new(2026, 9, 19, 9, 2, 0);
        await db.HealForTestAsync("A", "OUTSIDE");
        OfflineCacheService.AttendanceQueue.Enqueue(Log("Entry", minute: 1)); await db.PulseAttendanceAsync();
        Assert(await Scalar("SELECT entry_state FROM students WHERE student_id='A'") == "OUTSIDE");
        Assert(await DatabaseService.ReadVisitForTestAsync("A", entry.TransactionId) == null);
        Assert((await Scan("Entry", 3)).Visit?.Confirmed == true);
    });
    await Test("Legacy admissions never invent confirmed history", async () =>
    {
        await Reset(); var log = Log(); log.IsLegacy = true;
        OfflineCacheService.AttendanceQueue.Enqueue(log); await db.PulseAttendanceAsync();
        Assert(await DatabaseService.ReadVisitForTestAsync("A", log.TransactionId) == null);
    });
    await Test("Backward device clock marks history unresolved", async () =>
    {
        await Reset(); await Scan("Entry", 2, true); var exit = await Scan("Exit", 1, true);
        await db.PulseAttendanceAsync(); Assert(await DatabaseService.ReadVisitForTestAsync("A", exit.TransactionId) == null);
        Assert(await Scalar("SELECT entry_state FROM students WHERE student_id='A'") == "INSIDE");
    });
    await Test("Recovery preserves all verification-mode tables", async () =>
    {
        await Reset();
        foreach (string mode in new[] { "Fast", "Standard", "HighSecurity" })
        {
            var log = Log(); log.IsGranted = false; log.VerificationMode = mode;
            OfflineCacheService.AttendanceQueue.Enqueue(log);
        }
        await db.PulseAttendanceAsync();
        foreach (string table in new[] { "fast_mode_logs", "standard_mode_logs", "high_security_mode_logs" })
            Assert(await Scalar($"SELECT COUNT(*) FROM {table}") == "1");
    });
    await Test("Nontransactional log tables block recovery without losing queued records", async () =>
    {
        await Reset(); await Scan("Entry", 0, true); await Sql("ALTER TABLE fast_mode_logs ENGINE=MyISAM");
        try { await db.PulseAttendanceAsync(); Assert(OfflineCacheService.HasPendingLogs()); Assert(await Scalar("SELECT COUNT(*) FROM fast_mode_logs") == "0"); }
        finally { await Sql("ALTER TABLE fast_mode_logs ENGINE=InnoDB"); }
        await db.PulseAttendanceAsync(); Assert(!OfflineCacheService.HasPendingLogs());
    });
    await Test("Cache persistence failure retains the durable queue for retry", async () =>
    {
        await Reset(); await Scan("Entry", 0, true); OfflineCacheService.FailCacheFlush = true;
        await db.PulseAttendanceAsync();
        Assert(OfflineCacheService.HasPendingLogs() && await Scalar("SELECT COUNT(*) FROM fast_mode_logs") == "0");
        OfflineCacheService.FailCacheFlush = false; await db.PulseAttendanceAsync();
        Assert(!OfflineCacheService.HasPendingLogs());
    });
    await Test("Committed denial survives post-commit cache failure", async () =>
    {
        await Reset(); await Sql("UPDATE students SET entry_state='INSIDE' WHERE student_id='A'");
        OfflineCacheService.FailFlushNumber = 2;
        var result = await Scan("Entry", 0);
        Assert(result.Committed && !result.IsGranted && !result.ConfirmationPending);
        Assert(OfflineCacheService.CachedState == "OUTSIDE" && OfflineCacheService.HasPendingLogs());
        OfflineCacheService.FailFlushNumber = 0; await db.PulseAttendanceAsync();
        Assert(await Scalar("SELECT COUNT(*) FROM fast_mode_logs WHERE is_granted=FALSE") == "1");
        Assert(await Scalar("SELECT COUNT(*) FROM attendance_visits") == "0");
    });
    await Test("Interrupted online request stays pending and recovery cannot grant it", async () =>
    {
        await Reset(); DatabaseService.TestConnectionUnavailable = true;
        AttendanceCommitResult result;
        try { result = await Scan("Entry", 0); }
        finally { DatabaseService.TestConnectionUnavailable = false; }
        Assert(!result.IsGranted && !result.Committed && result.ConfirmationPending);
        Assert(OfflineCacheService.CachedState == "OUTSIDE");
        await db.PulseAttendanceAsync();
        Assert(!OfflineCacheService.HasPendingLogs());
        Assert(await Scalar("SELECT error_code FROM fast_mode_logs") == "ATTENDANCE_CONFIRMATION_EXPIRED");
        Assert(await Scalar("SELECT COUNT(*) FROM attendance_visits") == "0");
        Assert(await Scalar("SELECT entry_state FROM students WHERE student_id='A'") == "OUTSIDE");
    });
    await Test("Empty queue remains unready until failed cache refresh is retried after restart", async () =>
    {
        await Reset(); string path = NewDirectory();
        OfflineCacheService.AttendanceQueue = new DurableAttendanceQueue(path);
        await Scan("Entry", 0, true); db.FailCacheRefresh = true; await db.PulseAttendanceAsync();
        Assert(!OfflineCacheService.HasPendingLogs());
        Assert(await Scalar("SELECT COUNT(*) FROM attendance_devices WHERE ready=FALSE") == "1");
        OfflineCacheService.AttendanceQueue = new DurableAttendanceQueue(path);
        Assert(OfflineCacheService.AttendanceQueue.Snapshot().CacheRefreshRequired);
        Assert(DatabaseService.AttendanceSyncError != null);
        db.FailCacheRefresh = false; await db.PulseAttendanceAsync();
        Assert(!OfflineCacheService.AttendanceQueue.Snapshot().CacheRefreshRequired);
        Assert(OfflineCacheService.AttendanceQueue.Snapshot().LastCacheRefreshUtc.HasValue);
        Assert(await Scalar("SELECT COUNT(*) FROM attendance_devices WHERE ready=TRUE") == "1");
        Assert(DatabaseService.AttendanceSyncError == null);
    });
    await Test("Snapshot restores all attendance tables, state and safe device readiness", async () =>
    {
        await Reset(); await Scan("Entry", 0);
        foreach (string mode in new[] { "Standard", "HighSecurity" })
        {
            var denied = Log(); denied.VerificationMode = mode; denied.IsGranted = false;
            OfflineCacheService.AttendanceQueue.Enqueue(denied);
        }
        await Scan("EventAttendance", 1, true, "event-1"); await db.PulseAttendanceAsync();
        var snapshot = await db.CaptureAttendanceBackupAsync();
        Assert(snapshot.Tables.Values.All(rows => rows.Count > 0));
        await Reset(); int count = await db.RestoreAttendanceSnapshotAsync(snapshot);
        Assert(count == snapshot.Tables.Values.Sum(rows => rows.Count));
        foreach (string table in AttendanceBackupSnapshot.TableNames)
            Assert(await Scalar($"SELECT COUNT(*) FROM {table}") == snapshot.Tables[table].Count.ToString(), table);
        Assert(await Scalar("SELECT entry_state FROM students WHERE student_id='A'") == "INSIDE");
        Assert(await Scalar("SELECT COUNT(*) FROM attendance_devices WHERE ready=TRUE OR last_seen>'1970-01-02'") == "0");
        Assert(OfflineCacheService.AttendanceQueue.Snapshot().CacheRefreshRequired);
    });
    await Test("Restore refuses populated attendance without changing it", async () =>
    {
        await Reset(); await Scan("Entry", 0); var snapshot = await db.CaptureAttendanceBackupAsync();
        bool threw = false; try { await db.RestoreAttendanceSnapshotAsync(snapshot); } catch (InvalidOperationException) { threw = true; }
        Assert(threw && await Scalar("SELECT COUNT(*) FROM fast_mode_logs") == "1");
    });
    await Test("Restore refuses local pending records", async () =>
    {
        await Reset(); var snapshot = EmptyBackup(DateTime.UtcNow); await Scan("Entry", 0, true);
        bool threw = false; try { await db.RestoreAttendanceSnapshotAsync(snapshot); } catch (InvalidOperationException) { threw = true; }
        Assert(threw && OfflineCacheService.HasPendingLogs());
    });
    await Test("Restore refuses missing student profiles and rolls back metadata", async () =>
    {
        await Reset(); await Scan("Entry", 0); var snapshot = await db.CaptureAttendanceBackupAsync();
        await Reset(); await Sql("DELETE FROM students WHERE student_id='A'");
        bool threw = false; try { await db.RestoreAttendanceSnapshotAsync(snapshot); } catch (InvalidOperationException) { threw = true; }
        Assert(threw && await Scalar("SELECT COUNT(*) FROM attendance_receipts") == "0");
        Assert(await Scalar("SELECT COUNT(*) FROM fast_mode_logs") == "0");
    });
    await Test("Nontransactional metadata blocks recovery", async () =>
    {
        await Reset(); await Scan("Entry", 0, true); await Sql("ALTER TABLE attendance_decisions ENGINE=MyISAM");
        try
        {
            await db.PulseAttendanceAsync();
            Assert(OfflineCacheService.HasPendingLogs() && await Scalar("SELECT COUNT(*) FROM attendance_receipts") == "0");
        }
        finally { await Sql("ALTER TABLE attendance_decisions ENGINE=InnoDB"); }
        await db.PulseAttendanceAsync(); Assert(!OfflineCacheService.HasPendingLogs());
    });
    await Test("Restore refuses a live peer", async () =>
    {
        await Reset(); await Sql("INSERT INTO attendance_devices (device_id,last_seen,reported_sequence,acknowledged_sequence,ready,enabled) VALUES ('peer',UTC_TIMESTAMP(),0,0,TRUE,TRUE)");
        bool threw = false; try { await db.RestoreAttendanceSnapshotAsync(EmptyBackup(DateTime.UtcNow)); } catch (InvalidOperationException) { threw = true; }
        Assert(threw && await Scalar("SELECT device_id FROM attendance_devices") == "peer");
    });
    await Test("Failed restore rolls back every table", async () =>
    {
        await Reset(); await Scan("Entry", 0); var snapshot = await db.CaptureAttendanceBackupAsync(); await Reset();
        await Sql("CREATE TRIGGER fail_restore BEFORE INSERT ON fast_mode_logs FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='restore failure'");
        bool threw = false;
        try { await db.RestoreAttendanceSnapshotAsync(snapshot); } catch (MySqlException) { threw = true; }
        finally { await Sql("DROP TRIGGER fail_restore"); }
        Assert(threw);
        foreach (string table in AttendanceBackupSnapshot.TableNames) Assert(await Scalar($"SELECT COUNT(*) FROM {table}") == "0", table);
        Assert(await Scalar("SELECT entry_state FROM students WHERE student_id='A'") == "OUTSIDE");
    });
    await Test("Restore preserves purged log ID watermark", async () =>
    {
        await Reset(); await Scan("Entry", 0); await Sql("DELETE FROM fast_mode_logs");
        var snapshot = await db.CaptureAttendanceBackupAsync();
        long next = snapshot.NextLogIds["fast_mode_logs"] + 100;
        snapshot.NextLogIds["fast_mode_logs"] = next;
        await Reset(); await db.RestoreAttendanceSnapshotAsync(snapshot);
        Assert(await Scalar("SELECT COUNT(*) FROM fast_mode_logs") == "0");
        await Sql("INSERT INTO fast_mode_logs (remarks) VALUES ('watermark test')");
        Assert(long.Parse(await Scalar("SELECT id FROM fast_mode_logs")) >= next);
    });
    await Test("Separate device connections serialize conflicting online admissions in SQL", async () =>
    {
        await Reset();
        var first = new DurableAttendanceQueue(NewDirectory()).Enqueue(Log());
        var second = new DurableAttendanceQueue(NewDirectory()).Enqueue(Log(minute: 1));
        foreach (var log in new[] { first, second })
            await Sql($"INSERT INTO attendance_devices (device_id,last_seen,reported_sequence,acknowledged_sequence,ready,enabled) VALUES ('{log.DeviceId}',UTC_TIMESTAMP(),1,0,FALSE,TRUE)");
        using var firstConnection = new MySqlConnection(DatabaseService.ConnectionString);
        using var secondConnection = new MySqlConnection(DatabaseService.ConnectionString);
        await firstConnection.OpenAsync(); await secondConnection.OpenAsync();
        await Task.WhenAll(DatabaseService.CommitLiveForTestAsync(firstConnection, first), DatabaseService.CommitLiveForTestAsync(secondConnection, second));
        Assert(await Scalar("SELECT COUNT(*) FROM attendance_decisions WHERE is_granted=TRUE") == "1");
        Assert(await Scalar("SELECT COUNT(*) FROM attendance_visits") == "1");
    });
    await DeviceAttributionTests.RunSqlAsync(Test, connection, Reset);
}
else Console.WriteLine("SQL integration tests skipped; pass --mysql with the isolated test server on port 23306.");
Console.WriteLine($"{passed} passed; {failed} failed.");
return failed == 0 ? 0 : 1;
