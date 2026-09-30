using System.Text.Json;
using System.Text.Json.Nodes;
using MySqlConnector;
using NFC_System;

internal static class DeviceAttributionTests
{
    private static void Assert(bool condition, string message = "Assertion failed")
    {
        if (!condition) throw new Exception(message);
    }

    private static string NewDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "nfc-device-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static PendingGateLog Gate(string mode = "Fast") => new()
    {
        StudentId = "A", StudentName = "Student A", TransactionType = "Entry", VerificationMode = mode,
        WasOffline = true, IsGranted = true, Timestamp = "2026-09-29 09:00:00.000"
    };

    private static PendingEventAttendance Event() => new()
    {
        StudentId = "A", EventId = "event-a", Status = "PRESENT", VerificationMode = "Fast", Timestamp = "2026-09-29 09:01:00.000"
    };

    private static bool Rename(DurableAttendanceQueue queue, string name) =>
        queue.RenameDevice(name, "Test Admin", "Admin", DateTime.UtcNow, () => { });

    private static void SignIn(string role = "Administrator")
    {
        AppSession.IsLoggedIn = false;
        Assert(AppSession.TrySignIn(role, "Test Admin", AppSession.LoginVersion));
    }

    public static async Task RunAsync(Func<string, Func<Task>, Task> test)
    {
        await test("Device identity is permanent and defaults to Unknown/Legacy", () =>
        {
            string path = NewDirectory(); var queue = new DurableAttendanceQueue(path);
            OfflineCacheService.AttendanceQueue = queue;
            var before = DeviceIdentity.CaptureCurrent();
            Assert(Guid.TryParseExact(before.DeviceId, "N", out _) && before.DeviceName == DeviceIdentity.UnknownName);
            Rename(queue, "Main Gate");
            OfflineCacheService.AttendanceQueue = new DurableAttendanceQueue(path);
            var after = DeviceIdentity.CaptureCurrent();
            Assert(after.DeviceId == before.DeviceId && after.DeviceName == "Main Gate");
            Assert(before.DeviceName == DeviceIdentity.UnknownName, "Captured attribution must be immutable");
            return Task.CompletedTask;
        });
        await test("Rename never changes queued gate/event names, sequence or transaction identity", () =>
        {
            string path = NewDirectory(); var queue = new DurableAttendanceQueue(path); Rename(queue, "Entry Gate");
            var gates = new[] { "Fast", "Standard", "HighSecurity" }.Select(mode => queue.Enqueue(Gate(mode))).ToArray();
            var evt = queue.Enqueue(Event()); var before = queue.Snapshot();
            Rename(queue, "Exit Gate");
            var reloaded = new DurableAttendanceQueue(path).Snapshot();
            Assert(reloaded.DeviceId == before.DeviceId && reloaded.LastSequence == before.LastSequence);
            Assert(reloaded.GateLogs.All(x => x.DeviceName == "Entry Gate") && reloaded.EventLogs.Single().DeviceName == "Entry Gate");
            Assert(reloaded.GateLogs.Select(x => x.TransactionId).SequenceEqual(gates.Select(x => x.TransactionId)));
            Assert(reloaded.EventLogs.Single().TransactionId == evt.TransactionId);
            var next = queue.Enqueue(Gate()); Assert(next.DeviceName == "Exit Gate" && next.DeviceSequence == before.LastSequence + 1);
            return Task.CompletedTask;
        });
        await test("Queue ignores caller supplied device attribution when enqueueing", () =>
        {
            var queue = new DurableAttendanceQueue(NewDirectory()); Rename(queue, "East Gate");
            var gate = Gate(); gate.DeviceId = "forged"; gate.DeviceName = "Wrong device";
            var evt = Event(); evt.DeviceId = "forged"; evt.DeviceName = "Wrong device";
            queue.Enqueue(gate); queue.Enqueue(evt);
            Assert(gate.DeviceId == queue.Snapshot().DeviceId && evt.DeviceId == gate.DeviceId);
            Assert(gate.DeviceName == "East Gate" && evt.DeviceName == "East Gate");
            return Task.CompletedTask;
        });
        await test("Old outbox schema loads without inventing names for historical records", () =>
        {
            string path = NewDirectory(); var queue = new DurableAttendanceQueue(path); var gate = queue.Enqueue(Gate()); queue.Enqueue(Event());
            string file = Path.Combine(path, "attendance_outbox.json"); var json = JsonNode.Parse(File.ReadAllText(file))!.AsObject();
            json.Remove("DeviceName"); json.Remove("DeviceNameChanges");
            foreach (string collection in new[] { "GateLogs", "EventLogs" })
                foreach (var row in json[collection]!.AsArray()) row!.AsObject().Remove("DeviceName");
            File.WriteAllText(file, json.ToJsonString());
            Rename(new DurableAttendanceQueue(path), "New label");
            var loaded = queue.Snapshot();
            Assert(loaded.DeviceId == gate.DeviceId && loaded.GateLogs.Single().DeviceName == DeviceIdentity.UnknownName);
            Assert(loaded.EventLogs.Single().DeviceName == DeviceIdentity.UnknownName && loaded.LastSequence == 2);
            return Task.CompletedTask;
        });
        await test("Legacy imports retain Unknown/Legacy even when a label was supplied", () =>
        {
            string path = NewDirectory(); var gate = Gate(); gate.DeviceName = "Not verifiable";
            var evt = Event(); evt.DeviceName = "Not verifiable";
            File.WriteAllText(Path.Combine(path, "offline_gate_logs.json"), JsonSerializer.Serialize(new[] { gate }));
            File.WriteAllText(Path.Combine(path, "offline_event_logs.json"), JsonSerializer.Serialize(new[] { evt }));
            var state = new DurableAttendanceQueue(path).Snapshot();
            Assert(state.GateLogs.Single().IsLegacy && state.GateLogs.Single().DeviceName == DeviceIdentity.UnknownName);
            Assert(state.EventLogs.Single().IsLegacy && state.EventLogs.Single().DeviceName == DeviceIdentity.UnknownName);
            return Task.CompletedTask;
        });
        await test("Rename audit persists atomically, unchanged saves create no duplicate audit", () =>
        {
            string path = NewDirectory(); var queue = new DurableAttendanceQueue(path);
            Assert(Rename(queue, " Gate A ")); Assert(!Rename(queue, "Gate A"));
            var snapshot = new DurableAttendanceQueue(path).Snapshot(); var change = snapshot.DeviceNameChanges.Single();
            Assert(change.PreviousName == DeviceIdentity.UnknownName && change.DeviceName == snapshot.DeviceName && change.DeviceName == "Gate A");
            Assert(change.StaffName == "Test Admin" && change.StaffRole == "Admin" && change.DeviceId == snapshot.DeviceId);
            var gate = queue.Enqueue(Gate()); queue.Acknowledge(gate.TransactionId);
            Assert(queue.Snapshot().DeviceNameChanges.Count == 1);
            queue.AcknowledgeDeviceNameChange(change.ChangeId);
            Assert(queue.Snapshot().DeviceNameChanges.Count == 0 && queue.Snapshot().DeviceName == "Gate A");
            return Task.CompletedTask;
        });
        await test("Failed local rename leaves previous name, audit and pending scans intact", () =>
        {
            string path = NewDirectory(); var queue = new DurableAttendanceQueue(path); Rename(queue, "Gate A"); queue.Enqueue(Gate());
            string file = Path.Combine(path, "attendance_outbox.json"); string before = File.ReadAllText(file);
            Directory.CreateDirectory(file + ".tmp"); bool threw = false;
            try { Rename(queue, "Gate B"); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { threw = true; }
            Assert(threw && File.ReadAllText(file) == before);
            return Task.CompletedTask;
        });
        await test("Invalid and multiline device names are rejected without modifying the outbox", () =>
        {
            string path = NewDirectory(); var queue = new DurableAttendanceQueue(path); Rename(queue, "Gate A");
            string file = Path.Combine(path, "attendance_outbox.json"); string before = File.ReadAllText(file);
            foreach (string invalid in new[] { "", "   ", new string('x', 101), "Gate\nB", "Gate\tB", "Gate\0B" })
            {
                bool threw = false; try { Rename(queue, invalid); } catch (ArgumentException) { threw = true; }
                Assert(threw && File.ReadAllText(file) == before);
            }
            return Task.CompletedTask;
        });
        await test("Concurrent instances serialize renames and enqueue with consistent attribution", async () =>
        {
            string path = NewDirectory(); var queue = new DurableAttendanceQueue(path); Rename(queue, "Gate A");
            await Task.WhenAll(Enumerable.Range(0, 24).Select(i => Task.Run(() =>
            {
                var q = new DurableAttendanceQueue(path);
                if (i % 3 == 0) Rename(q, "Gate " + i); else q.Enqueue(Gate());
            })));
            var snapshot = queue.Snapshot();
            var names = snapshot.DeviceNameChanges.Select(x => x.DeviceName).ToHashSet();
            Assert(snapshot.GateLogs.Count == 16 && snapshot.LastSequence == 16);
            Assert(snapshot.GateLogs.All(x => x.DeviceId == snapshot.DeviceId && names.Contains(x.DeviceName)));
        });
        await test("Personnel, organizer, signed-out and stale admin sessions cannot rename", async () =>
        {
            var queue = new DurableAttendanceQueue(NewDirectory()); OfflineCacheService.AttendanceQueue = queue; Rename(queue, "Gate A");
            foreach (string role in new[] { "Security Personnel", "Event Organizer" })
            {
                SignIn(role); bool threw = false;
                try { await new DatabaseService().RenameCurrentDeviceAsync("Gate B"); } catch (UnauthorizedAccessException) { threw = true; }
                Assert(threw && queue.Snapshot().DeviceName == "Gate A");
            }
            SignIn(); long oldVersion = AppSession.LoginVersion; SignIn();
            bool stale = false; try { await new DatabaseService().RenameCurrentDeviceAsync("Gate B", oldVersion); } catch (UnauthorizedAccessException) { stale = true; }
            Assert(stale); AppSession.IsLoggedIn = false;
            bool signedOut = false; try { await new DatabaseService().RenameCurrentDeviceAsync("Gate B"); } catch (UnauthorizedAccessException) { signedOut = true; }
            Assert(signedOut && queue.Snapshot().DeviceNameChanges.Count == 1);
        });
        await test("Offline admin rename survives restart with its audit pending upload", async () =>
        {
            string path = NewDirectory(); OfflineCacheService.AttendanceQueue = new DurableAttendanceQueue(path); SignIn("Master Administrator");
            DatabaseService.TestConnectionUnavailable = true;
            try
            {
                var result = await new DatabaseService().RenameCurrentDeviceAsync("Library Gate");
                Assert(result.Changed && result.AuditPending && result.Identity.DeviceName == "Library Gate");
                var snapshot = new DurableAttendanceQueue(path).Snapshot();
                Assert(snapshot.DeviceName == "Library Gate" && snapshot.DeviceNameChanges.Single().StaffRole == "Master Admin");
            }
            finally { DatabaseService.TestConnectionUnavailable = false; AppSession.IsLoggedIn = false; }
        });
    }

    public static async Task RunSqlAsync(Func<string, Func<Task>, Task> test, MySqlConnection connection, Func<Task> reset)
    {
        async Task Sql(string sql) { using var command = new MySqlCommand(sql, connection); await command.ExecuteNonQueryAsync(); }
        async Task<string> Scalar(string sql) { using var command = new MySqlCommand(sql, connection); return (await command.ExecuteScalarAsync())?.ToString() ?? ""; }
        var db = new DatabaseService();

        await test("Schema migration skips missing base tables and handles late-created tables", async () =>
        {
            string database = "attendance_device_schema_" + Guid.NewGuid().ToString("N");
            await Sql($"CREATE DATABASE `{database}`");
            try
            {
                using var fresh = new MySqlConnection(new MySqlConnectionStringBuilder(DatabaseService.ConnectionString) { Database = database }.ConnectionString);
                await fresh.OpenAsync();
                await DatabaseService.CreateAttendanceSchemaAsync(fresh);
                await DatabaseService.CreateAttendanceSchemaAsync(fresh);
                using (var create = new MySqlCommand("CREATE TABLE fast_mode_logs (id INT PRIMARY KEY) ENGINE=InnoDB; INSERT INTO fast_mode_logs VALUES (1)", fresh)) await create.ExecuteNonQueryAsync();
                await DatabaseService.CreateDeviceSchemaAsync(fresh); await DatabaseService.CreateDeviceSchemaAsync(fresh);
                using var query = new MySqlCommand("SELECT CONCAT(device_id,'|',device_name) FROM fast_mode_logs WHERE id=1", fresh);
                Assert((string)(await query.ExecuteScalarAsync())! == "|Unknown/Legacy");
            }
            finally { await Sql($"DROP DATABASE `{database}`"); }
        });
        await test("Online admin rename is audited once without registering an admin-only attendance peer", async () =>
        {
            await reset(); DatabaseService.DeactivateAttendanceForTest(); SignIn();
            var result = await db.RenameCurrentDeviceAsync("Admin Desk");
            Assert(result.Changed && !result.AuditPending);
            Assert(await Scalar("SELECT COUNT(*) FROM alerts WHERE device_name='Admin Desk' AND device_name_change_id IS NOT NULL") == "1");
            await db.PulseAttendanceAsync();
            Assert(await Scalar("SELECT COUNT(*) FROM attendance_devices") == "0");
            await db.RenameCurrentDeviceAsync("Admin Desk");
            Assert(await Scalar("SELECT COUNT(*) FROM alerts") == "1"); db.ActivateAttendanceDevice();
        });
        await test("Offline/restart recovery keeps original labels for all three gate modes and events", async () =>
        {
            await reset(); string path = NewDirectory(); var queue = new DurableAttendanceQueue(path);
            OfflineCacheService.AttendanceQueue = queue; Rename(queue, "Main Gate");
            foreach (string mode in new[] { "Fast", "Standard", "HighSecurity" }) { var gate = Gate(mode); gate.IsGranted = false; queue.Enqueue(gate); }
            queue.Enqueue(Event()); Rename(queue, "Renamed Gate"); queue.Enqueue(Event());
            OfflineCacheService.AttendanceQueue = new DurableAttendanceQueue(path);
            await db.PulseAttendanceAsync();
            foreach (string table in new[] { "fast_mode_logs", "standard_mode_logs", "high_security_mode_logs" })
                Assert(await Scalar($"SELECT COUNT(*) FROM {table} WHERE device_name='Main Gate' AND device_id='{queue.Snapshot().DeviceId}'") == "1", table);
            Assert(await Scalar("SELECT COUNT(*) FROM event_attendance WHERE device_name='Main Gate'") == "1");
            Assert(await Scalar("SELECT COUNT(*) FROM event_attendance WHERE device_name='Renamed Gate'") == "1");
            Assert(await Scalar("SELECT device_name FROM attendance_devices") == "Renamed Gate");
        });
        await test("Receipt retry after rename never rewrites the committed log attribution", async () =>
        {
            await reset(); var queue = OfflineCacheService.AttendanceQueue; Rename(queue, "Gate A"); var log = queue.Enqueue(Gate());
            await DatabaseService.CommitForTestAsync(connection, log);
            Rename(queue, "Gate B"); await db.PulseAttendanceAsync();
            Assert(await Scalar("SELECT COUNT(*) FROM fast_mode_logs") == "1");
            Assert(await Scalar("SELECT device_name FROM fast_mode_logs") == "Gate A");
        });
        await test("Event rows derived from a gate transaction preserve its captured attribution", async () =>
        {
            await reset(); var queue = OfflineCacheService.AttendanceQueue; Rename(queue, "Event Entrance");
            var gate = Gate(); gate.EventId = "event-a"; gate.TransactionType = "EventAttendance"; queue.Enqueue(gate);
            Rename(queue, "Event Exit"); await db.PulseAttendanceAsync();
            Assert(await Scalar("SELECT device_name FROM event_attendance") == "Event Entrance");
            Assert(await Scalar("SELECT device_id FROM event_attendance") == queue.Snapshot().DeviceId);
        });
        await test("Imported legacy gate and event rows do not claim the migrating device as their source", async () =>
        {
            await reset(); string path = NewDirectory();
            File.WriteAllText(Path.Combine(path, "offline_gate_logs.json"), JsonSerializer.Serialize(new[] { Gate() }));
            File.WriteAllText(Path.Combine(path, "offline_event_logs.json"), JsonSerializer.Serialize(new[] { Event() }));
            var queue = new DurableAttendanceQueue(path); OfflineCacheService.AttendanceQueue = queue; Rename(queue, "Migration station");
            await db.PulseAttendanceAsync();
            Assert(await Scalar("SELECT CONCAT(device_id,'|',device_name) FROM fast_mode_logs") == "|Unknown/Legacy");
            Assert(await Scalar("SELECT CONCAT(device_id,'|',device_name) FROM event_attendance") == "|Unknown/Legacy");
        });
        await test("A rename audit insert failure retains the audit without blocking attendance recovery", async () =>
        {
            await reset(); var queue = OfflineCacheService.AttendanceQueue; Rename(queue, "Gate A"); queue.Enqueue(Gate());
            await Sql("CREATE TRIGGER fail_device_audit BEFORE INSERT ON alerts FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='audit failure'");
            try
            {
                await db.PulseAttendanceAsync();
                Assert(queue.Snapshot().DeviceNameChanges.Count == 1 && queue.Snapshot().GateLogs.Count == 0);
                Assert(await Scalar("SELECT COUNT(*) FROM fast_mode_logs") == "1" && DatabaseService.DeviceAuditSyncError != null);
            }
            finally { await Sql("DROP TRIGGER fail_device_audit"); }
            await db.PulseAttendanceAsync();
            Assert(queue.Snapshot().DeviceNameChanges.Count == 0 && await Scalar("SELECT COUNT(*) FROM alerts") == "1");
        });
        await test("Committed rename audit retries exactly once when the local acknowledgment fails", async () =>
        {
            await reset(); string path = NewDirectory(); var queue = new DurableAttendanceQueue(path); OfflineCacheService.AttendanceQueue = queue; Rename(queue, "Gate A");
            string temporary = Path.Combine(path, "attendance_outbox.json.tmp"); Directory.CreateDirectory(temporary);
            bool threw = false;
            try { await DatabaseService.FlushDeviceNamesForTestAsync(connection); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { threw = true; }
            finally { Directory.Delete(temporary); }
            Assert(threw && await Scalar("SELECT COUNT(*) FROM alerts") == "1" && queue.Snapshot().DeviceNameChanges.Count == 1);
            await DatabaseService.FlushDeviceNamesForTestAsync(connection);
            Assert(queue.Snapshot().DeviceNameChanges.Count == 0 && await Scalar("SELECT COUNT(*) FROM alerts") == "1");
        });
        await test("Snapshots preserve registry names and historical attribution; old snapshots use legacy defaults", async () =>
        {
            await reset(); var queue = OfflineCacheService.AttendanceQueue; Rename(queue, "Gate A"); queue.Enqueue(Gate());
            Rename(queue, "Gate B"); await db.PulseAttendanceAsync();
            var snapshot = await db.CaptureAttendanceBackupAsync(); await reset(); await db.RestoreAttendanceSnapshotAsync(snapshot);
            Assert(await Scalar("SELECT device_name FROM fast_mode_logs") == "Gate A");
            Assert(await Scalar("SELECT device_name FROM attendance_devices") == "Gate B");
            foreach (var table in snapshot.Tables)
                foreach (var row in table.Value)
                {
                    row.Remove("device_name");
                    if (table.Key.EndsWith("_logs") || table.Key == "event_attendance") row.Remove("device_id");
                }
            await reset(); await db.RestoreAttendanceSnapshotAsync(snapshot);
            Assert(await Scalar("SELECT CONCAT(device_id,'|',device_name) FROM fast_mode_logs") == "|Unknown/Legacy");
            Assert(await Scalar("SELECT device_name FROM attendance_devices") == DeviceIdentity.UnknownName);
        });
        AppSession.IsLoggedIn = false;
    }
}
