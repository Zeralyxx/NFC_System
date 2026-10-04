using MySqlConnector;
using NFC_System;
using System.Text.RegularExpressions;

internal static class PinSecurityTests
{
    private static void Assert(bool value, string message = "PIN assertion failed") { if (!value) throw new Exception(message); }
    private static async Task Reject(Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or IOException or System.Text.Json.JsonException) { return; }
        throw new Exception("Expected rejection");
    }
    private static string JournalDirectory() => Path.Combine(Path.GetTempPath(), "nfc-pin-test-" + Guid.NewGuid().ToString("N"));
    private static PinState State() => new() { StudentId = "A", ResetToken = Guid.NewGuid().ToString("N"), CredentialTag = "tag" };
    private static PinAttempt Attempt(PinState state, bool correct = false, bool offline = false) => new()
    {
        StudentId = state.StudentId, ResetToken = state.ResetToken, CredentialTag = state.CredentialTag,
        BaseRevision = state.Revision, Correct = correct, Offline = offline
    };

    public static async Task RunAsync(Func<string, Func<Task>, Task> test)
    {
        await test("PIN journal restart preserves three failures and blocks a fourth submission", async () =>
        {
            string path = JournalDirectory();
            var journal = new DurablePinJournal(path);
            journal.Confirm(new[] { State() });
            for (int i = 1; i <= 3; i++)
            {
                journal.Append(journal.Read("A"), false, true);
                journal = new DurablePinJournal(path);
                Assert(journal.Read("A").Attempts == i && journal.Read("A").Locked == (i == 3));
            }
            await Reject(() => { journal.Append(journal.Read("A"), true, true); return Task.CompletedTask; });
        });
        await test("PIN journal acknowledgment cannot replay a local failure after restart", () =>
        {
            string path = JournalDirectory(); var journal = new DurablePinJournal(path); var state = State();
            journal.Confirm(new[] { state }); var operation = journal.Append(state, false, true);
            journal.Acknowledge(operation.Id, PinRules.Apply(state, operation).State);
            journal = new DurablePinJournal(path);
            Assert(journal.Snapshot().Pending.Count == 0 && journal.Read("A").Attempts == 1);
            return Task.CompletedTask;
        });
        await test("Uncertain online PIN outcomes cannot become offline permissions", async () =>
        {
            var journal = new DurablePinJournal(JournalDirectory()); var state = State(); journal.Confirm(new[] { state });
            journal.Append(state, true, false);
            await Reject(() => { journal.Read("A"); return Task.CompletedTask; });
        });
        await test("Missing and corrupt PIN journals fail closed without erasing evidence", async () =>
        {
            string path = JournalDirectory(); var journal = new DurablePinJournal(path);
            await Reject(() => { journal.Read("A"); return Task.CompletedTask; });
            Directory.CreateDirectory(path); File.WriteAllText(Path.Combine(path, "pin_security.json"), "{broken");
            await Reject(() => { journal.Confirm(new[] { State() }); return Task.CompletedTask; });
            Assert(File.ReadAllText(Path.Combine(path, "pin_security.json")) == "{broken");
        });
        await test("Offline success resets observed failures but cannot erase unseen peer failures", () =>
        {
            var state = State(); state.Attempts = 2; state.Revision = 2;
            var success = Attempt(state, true, true);
            Assert(PinRules.Apply(state, success).State.Attempts == 0);
            state.Revision++;
            Assert(PinRules.Apply(state, success).State.Attempts == 2);
            state.Locked = true;
            Assert(!PinRules.Apply(state, success).Accepted && PinRules.Apply(state, success).State.Locked);
            return Task.CompletedTask;
        });
        await test("Cloud profile refresh cannot overwrite existing PIN hashes or lock counters", () =>
        {
            string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../..", "NFC_System", "DatabaseService.cs"));
            int start = source.IndexOf("qr_credential=@qr, photo_data=@photo, is_temporary=@temp", StringComparison.Ordinal);
            Assert(start >= 0);
            Assert(!source.Contains("qr_credential=@qr, pin_hash=@hash"));
            return Task.CompletedTask;
        });
    }

    public static async Task RunDatabaseAsync(Func<string, Func<Task>, Task> test)
    {
        var builder = new MySqlConnectionStringBuilder(DatabaseService.ConnectionString);
        if (builder.Server != "127.0.0.1" || builder.Port != 23306 || builder.Database != "nfc_operations_tests") throw new Exception("Unsafe PIN test database");
        using var connection = new MySqlConnection(builder.ConnectionString); await connection.OpenAsync();
        async Task Sql(string sql) { using var cmd = new MySqlCommand(sql, connection); await cmd.ExecuteNonQueryAsync(); }
        await DatabaseService.EnsurePinSchemaAsync(connection);
        await Sql("DELETE FROM pin_attempt_receipts WHERE student_id LIKE 'PINTEST-%'; DELETE FROM pin_security_versions WHERE student_id LIKE 'PINTEST-%'; DELETE FROM students WHERE student_id LIKE 'PINTEST-%'");
        var hash = PinHasher.HashPin("1234");
        int next = 0;
        async Task<StudentRecord> Student()
        {
            string id = "PINTEST-" + ++next;
            using var cmd = new MySqlCommand(@"INSERT INTO students (student_id,full_name,nfc_uid,pin_salt,pin_hash,failed_pin_attempts,pin_locked,status)
                VALUES (@id,'PIN test',@id,@salt,@hash,0,0,'Active')", connection);
            cmd.Parameters.AddWithValue("@id", id); cmd.Parameters.AddWithValue("@salt", hash.Salt); cmd.Parameters.AddWithValue("@hash", hash.Hash);
            await cmd.ExecuteNonQueryAsync();
            OfflineCacheService.PinJournal = new DurablePinJournal(JournalDirectory());
            return new StudentRecord { StudentId = id, PinSalt = hash.Salt, PinHash = hash.Hash };
        }
        async Task<PinState> Read(string id)
        {
            using var tx = await connection.BeginTransactionAsync();
            var state = await DatabaseService.ReadPinStateAsync(connection, tx, id); await tx.CommitAsync(); return state;
        }
        async Task<PinAttemptResult> Apply(PinAttempt attempt)
        {
            using var peer = new MySqlConnection(builder.ConnectionString); await peer.OpenAsync();
            return await DatabaseService.ApplyPinAttemptAsync(peer, attempt);
        }
        void Admin()
        {
            AppSession.IsLoggedIn = true; AppSession.IsAdmin = true; AppSession.IsEventOrganizer = false;
            AppSession.CurrentStaffRoleLabel = "Admin"; DatabaseMonitor.IsOnline = true;
        }
        Admin(); var db = new DatabaseService();
        await test("SQL concurrent kiosk failures serialize to exactly three attempts", async () =>
        {
            var student = await Student(); var state = await Read(student.StudentId);
            var results = await Task.WhenAll(Apply(Attempt(state)), Apply(Attempt(state)), Apply(Attempt(state)));
            Assert(results.Select(x => x.State.Attempts).OrderBy(x => x).SequenceEqual(new[] { 1, 2, 3 }));
            var current = await Read(student.StudentId); Assert(current.Locked && current.Attempts == 3);
            Assert(!(await Apply(Attempt(state, true))).Accepted);
        });
        await test("SQL concurrent retries of one PIN attempt count once", async () =>
        {
            var student = await Student(); var state = await Read(student.StudentId); var attempt = Attempt(state);
            var results = await Task.WhenAll(Apply(attempt), Apply(attempt), Apply(attempt));
            Assert(results.All(x => x.State.Attempts == 1)); Assert((await Read(student.StudentId)).Attempts == 1);
        });
        await test("SQL lock and counter update roll back when receipt persistence fails", async () =>
        {
            var student = await Student(); var state = await Read(student.StudentId); var attempt = Attempt(state);
            attempt.Id = new string('a', 64);
            bool failed = false;
            try { await Apply(attempt); } catch (MySqlException) { failed = true; }
            Assert(failed && (await Read(student.StudentId)).Attempts == 0);
        });
        await test("SQL online state overrides both stale-unlocked and stale-locked profiles", async () =>
        {
            var student = await Student(); var state = await Read(student.StudentId);
            await Apply(Attempt(state)); await Apply(Attempt(state)); await Apply(Attempt(state));
            Assert((await db.PreparePinStateAsync(student, false)).Locked);
            await db.UnlockAccountAsync(student.StudentId);
            student.PinLocked = true; student.FailedPinAttempts = 3;
            state = await db.PreparePinStateAsync(student, false); Assert(!state.Locked && state.Attempts == 0);
        });
        await test("SQL stale session cannot accept a correct PIN after another kiosk locks it", async () =>
        {
            var student = await Student(); var state = await db.PreparePinStateAsync(student, false);
            var session = new VerificationSession { Student = student, PinState = state };
            await Apply(Attempt(state)); await Apply(Attempt(state)); await Apply(Attempt(state));
            var result = await db.SubmitSecurePinAsync(session, "1234");
            Assert(!result.Accepted && result.Error == "PIN_LOCKED" && (await Read(student.StudentId)).Locked);
        });
        await test("SQL correct PIN after two failures resets the counter without locking", async () =>
        {
            var student = await Student(); var state = await db.PreparePinStateAsync(student, false);
            var session = new VerificationSession { Student = student, PinState = state };
            Assert((await db.SubmitSecurePinAsync(session, "0000")).State.Attempts == 1);
            Assert((await db.SubmitSecurePinAsync(session, "0000")).State.Attempts == 2);
            var result = await db.SubmitSecurePinAsync(session, "1234");
            Assert(result.Accepted && !result.State.Locked && (await Read(student.StudentId)).Attempts == 0);
        });
        await test("SQL offline lock survives journal restart and recovery retries", async () =>
        {
            var student = await Student(); var state = await db.PreparePinStateAsync(student, false);
            string directory = JournalDirectory(); OfflineCacheService.PinJournal = new DurablePinJournal(directory);
            OfflineCacheService.PinJournal.Confirm(new[] { state });
            var session = new VerificationSession { Student = student, PinState = state, IsOffline = true };
            for (int i = 1; i <= 3; i++)
            {
                Assert((await db.SubmitSecurePinAsync(session, "0000")).State.Attempts == i);
                OfflineCacheService.PinJournal = new DurablePinJournal(directory);
            }
            Assert((await db.PreparePinStateAsync(student, true)).Locked);
            await db.RecoverPinAttemptsAsync(); await db.RecoverPinAttemptsAsync();
            Assert((await Read(student.StudentId)).Attempts == 3 && OfflineCacheService.PinJournal.Snapshot().Pending.Count == 0);
        });
        await test("SQL administrator unlock invalidates pre-reset queued failures and active sessions", async () =>
        {
            var student = await Student(); var state = await db.PreparePinStateAsync(student, false);
            var session = new VerificationSession { Student = student, PinState = state, IsOffline = true };
            await db.SubmitSecurePinAsync(session, "0000"); await db.SubmitSecurePinAsync(session, "0000");
            await db.UnlockAccountAsync(student.StudentId);
            await db.RecoverPinAttemptsAsync(); var current = await Read(student.StudentId);
            Assert(current.Attempts == 0 && current.ResetToken != state.ResetToken);
            session.IsOffline = false;
            Assert((await db.SubmitSecurePinAsync(session, "1234")).Error == "PIN_STATE_CHANGED");
        });
        await test("SQL lost acknowledgment after admin unlock cannot restore an old locked receipt", async () =>
        {
            var student = await Student(); var state = await db.PreparePinStateAsync(student, false);
            await Apply(Attempt(state)); await Apply(Attempt(state));
            state = await db.PreparePinStateAsync(student, false);
            var attempt = OfflineCacheService.PinJournal.Append(state, false, false);
            await Apply(attempt); // Commit succeeded, but local acknowledgment was lost.
            await db.UnlockAccountAsync(student.StudentId);
            await db.RecoverPinAttemptsAsync();
            Assert(!(await db.PreparePinStateAsync(student, true)).Locked && (await Read(student.StudentId)).Attempts == 0);
        });
        await test("SQL PIN replacement invalidates cached hashes and old queued successes", async () =>
        {
            var student = await Student(); var state = await db.PreparePinStateAsync(student, false);
            OfflineCacheService.PinJournal.Append(state, true, true);
            await db.ResetPinAsync(student.StudentId, "5678"); await db.RecoverPinAttemptsAsync();
            await Reject(() => db.PreparePinStateAsync(student, true));
            Assert((await Read(student.StudentId)).ResetToken != state.ResetToken);
        });
        await test("SQL profile-editor PIN replacement rotates the reset token in the same transaction", async () =>
        {
            var student = await Student(); var state = await Read(student.StudentId);
            student.FullName = "PIN test"; student.NfcUid = student.StudentId;
            await db.UpdateStudentWithClearanceAsync(student.StudentId, student, "5678");
            var current = await Read(student.StudentId);
            Assert(current.ResetToken != state.ResetToken && current.CredentialTag != state.CredentialTag);
            Assert(!current.Locked && current.Attempts == 0);
        });
        await test("SQL session ending during an unlock rolls back the reset", async () =>
        {
            var student = await Student(); var state = await Read(student.StudentId);
            using var blocker = await connection.BeginTransactionAsync();
            using (var locked = new MySqlCommand("SELECT student_id FROM students WHERE student_id=@id FOR UPDATE", connection, blocker))
            {
                locked.Parameters.AddWithValue("@id", student.StudentId); await locked.ExecuteScalarAsync();
            }
            var reset = db.UnlockAccountAsync(student.StudentId);
            await Task.Delay(100); AppSession.IsLoggedIn = false; await blocker.CommitAsync();
            await Reject(() => reset);
            Assert((await Read(student.StudentId)).ResetToken == state.ResetToken); Admin();
        });
        await test("SQL refresh preserves and synchronizes offline failures before caching database state", async () =>
        {
            var student = await Student(); var state = await db.PreparePinStateAsync(student, false);
            OfflineCacheService.PinJournal.Append(state, false, true);
            await db.RefreshPinSecurityAsync();
            Assert(OfflineCacheService.PinJournal.Snapshot().Pending.Count == 0);
            Assert((await db.PreparePinStateAsync(student, true)).Attempts == 1 && (await Read(student.StudentId)).Attempts == 1);
        });
        await test("SQL deleted student's pending PIN attempt does not block other students", async () =>
        {
            var student = await Student(); var state = await db.PreparePinStateAsync(student, false);
            OfflineCacheService.PinJournal.Append(state, false, true);
            await Sql($"DELETE FROM students WHERE student_id='{student.StudentId}'");
            await db.RecoverPinAttemptsAsync(); Assert(OfflineCacheService.PinJournal.Snapshot().Pending.Count == 0);
        });
        await test("SQL PIN override preserves personnel unlock but denies personnel PIN replacement", async () =>
        {
            var student = await Student(); AppSession.IsAdmin = false; AppSession.CurrentStaffRoleLabel = "Personnel";
            await db.UnlockAccountAsync(student.StudentId);
            await Reject(() => db.ResetPinAsync(student.StudentId, "5678"));
            AppSession.IsLoggedIn = false;
            await Reject(() => db.UnlockAccountAsync(student.StudentId)); Admin();
        });
        await test("SQL all four historical report joins preserve one row and original ownership", async () =>
        {
            string root = Path.Combine(AppContext.BaseDirectory, "../../../..", "NFC_System");
            var joins = new List<(string Alias, string Rule)>();
            foreach (string file in new[] { "DatabaseService.cs", "DatabaseService.SpreadsheetExport.cs" })
            foreach (Match match in Regex.Matches(File.ReadAllText(Path.Combine(root, file)), @"(?:LEFT JOIN students s\s+ON) ([^\r\n]+)"))
                if (match.Groups[1].Value.Contains("cl.student_id") || match.Groups[1].Value.Contains("vl.student_id"))
                    joins.Add((match.Groups[1].Value.Contains("cl.") ? "cl" : "vl", match.Groups[1].Value));
            Assert(joins.Count == 4, "Expected audit, CSV, ledger and spreadsheet SQL joins");
            await Sql(@"CREATE TEMPORARY TABLE pin_report_students(student_id VARCHAR(50) PRIMARY KEY,full_name VARCHAR(100),nfc_uid VARCHAR(50) UNIQUE);
                CREATE TEMPORARY TABLE pin_report_logs(id INT PRIMARY KEY,student_id VARCHAR(50),student_name VARCHAR(100),nfc_uid VARCHAR(50));
                INSERT INTO pin_report_students VALUES ('A','A current','NEW'),('B','B current','OLD');
                INSERT INTO pin_report_logs VALUES (1,'A','A recorded','OLD'),(2,'A','A recorded','OLD'),(3,'DELETED','Deleted recorded','OLD'),(4,NULL,'Legacy recorded','OLD'),(5,'B','','OLD');");
            foreach (var (alias, rule) in joins)
            {
                using var cmd = new MySqlCommand($@"SELECT {alias}.id,s.student_id,COALESCE(NULLIF({alias}.student_name,''),s.full_name)
                    FROM pin_report_logs {alias} LEFT JOIN pin_report_students s ON {rule} ORDER BY {alias}.id", connection);
                using var reader = await cmd.ExecuteReaderAsync(); var rows = new List<(int, string?, string)>();
                while (await reader.ReadAsync()) rows.Add((reader.GetInt32(0), reader.IsDBNull(1) ? null : reader.GetString(1), reader.GetString(2)));
                Assert(rows.SequenceEqual(new[] { (1, (string?)"A", "A recorded"), (2, "A", "A recorded"),
                    (3, (string?)null, "Deleted recorded"), (4, (string?)null, "Legacy recorded"), (5, "B", "B current") }));
            }
        });
    }
}
