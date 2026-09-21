# Attendance synchronization and kiosk results

## Behavior

- A successful gate scan displays TIME IN or TIME OUT with its transaction timestamp. When the matching visit is confirmed and device synchronization checks pass, the kiosk displays both times. An open, confirmed visit shows "Not yet recorded" for time-out.
- Offline, recovering, ambiguous, legacy, and unavailable history displays only the current transaction. Missing records are never treated as evidence of a missing exit.
- The result timeout remains ten seconds. A new student replaces the previous result. The attendance result travels with that verification outcome; there is no delayed history request that can overwrite another student's card. Photo loading also uses a replacement guard.
- Event check-in/check-out is recorded separately and cannot manufacture a gate time-in/time-out pair.
- Signed QR, NFC, PIN, staff roles, and reader/database status indicators keep their existing purposes.

## Persistence and recovery

1. Before granting admission, write a unique transaction ID, persistent device ID, device sequence, and timestamp to `%ProgramData%\NFC_System\Cache\attendance_outbox.json`. Writes use a flushed temporary file and atomic replacement, with a file lease to serialize multiple processes.
2. For a scan that began offline, update the local student state. For an online scan, wait for its committed database decision first. Pending offline admissions are replayed into the student cache after restart; unconfirmed online requests are not treated as admissions.
3. While the application is running and an attendance window has activated its device, the existing database monitor attempts recovery every three seconds. The kiosk also has a five-second retry trigger. A shared semaphore prevents overlapping recovery, admissions, and cache refresh within the process.
4. Recover at most 200 records per background pass, ordered by device sequence. Explicit sync-before-exit drains all remaining batches. A failure stops the batch, preserving the failed record and all later records.
5. Commit the database receipt, log, decision, visit, and student-state changes in one InnoDB transaction. A repeated transaction ID finds its receipt instead of inserting duplicate records.
6. Remove a queued record only after its database transaction is acknowledged. If acknowledgment persistence fails, replay remains safe.
7. Refresh the student/event/roster cache immediately after background recovery drains the queue. A durable `CacheRefreshRequired` flag remains set until all refresh writes succeed. Failure keeps the device unready even when its queue is empty; the next heartbeat retries, including after restart. `LastCacheRefreshUtc` records the last successful refresh. Periodic cache refresh is serialized with recovery and cannot replace pending local attendance state.

An interrupted online scan with no readable committed decision displays `CONFIRMATION PENDING` and does not grant access. A known denial stays denied even if subsequent cache I/O or history lookup fails. An online request replayed without an existing receipt is recorded as `ATTENDANCE_CONFIRMATION_EXPIRED`, not retroactively admitted. Scans that genuinely began offline retain offline operation. If the database committed a grant but its reply was lost, personnel must investigate that transaction before allowing a retry or correcting attendance; a committed database decision is not proof that somebody physically passed the gate.

Recovery does not run with the application closed. Database reachability and attendance readiness are deliberately different checks. A malformed queue is preserved and blocks recovery; it is not silently replaced with an empty list. `DatabaseService.AttendanceSyncError` contains the most recent recovery failure for diagnostics.

Existing `offline_gate_logs.json` and `offline_event_logs.json` are imported once into the new outbox. Original files remain as recovery evidence and are no longer live queues. Do not run an older application build against this cache after migration. Legacy gate records are marked uncertain, because they lack original transaction identities and ordering guarantees. Pre-upgrade duplicates or missing records cannot be repaired automatically.

## Visits, conflicts, and purging

New tables: `attendance_devices`, `attendance_receipts`, `attendance_decisions`, `attendance_current`, and `attendance_visits`.

The existing log-purge routine does not delete these tables. Open visits, the current per-student state, and retry receipts survive log cleanup. This release does not automatically purge closed visit metadata or receipts; include them in capacity and retention planning.

- Ordered entry/exit on one device can be reconciled after offline recovery.
- A late record or backward clock never blindly overwrites a newer state. Its history becomes unresolved.
- Cross-device offline ordering is treated conservatively; timestamps alone do not establish a valid pair.
- Simultaneous online gate scans recheck student state under a database row lock. A conflicting admission is recorded as denied, not converted into an offline grant.
- Manual attendance correction invalidates the old pair. A fresh online entry after a known outside state can establish a new visit; it does not rewrite old history.
- An existing INSIDE profile without a visit can exit, but no historical time-in is invented.
- Importing cloud log archives no longer changes live entry state. Updating an existing profile from cloud also preserves its local entry state.

Unresolved cases can be inspected through `attendance_current.state_json` and `attendance_visits.confirmed`. There is no new conflict-review screen in this release; personnel can use the existing attendance correction workflow after investigating the logs.

## Multiple kiosks and rollout

Every participating kiosk must run the updated build, connect to the same operational MySQL database, and open its verification or kiosk workflow online at least once to register its device. An older or never-enrolled kiosk cannot report a backlog; do not mix versions or treat an unenrolled deployment as complete.

A device reports its sequence and the database tracks the acknowledged sequence. Paired results are withheld if any enabled registered device is unready, has a sequence gap, or has not reported within fifteen seconds. A returning device must reconcile its backlog before its ready flag is restored.

This is a conservative, last-reported view, not a global real-time guarantee. A device can disconnect immediately after reporting ready, leaving up to the freshness timeout before another kiosk detects its absence. Offline cross-device scans can remain ambiguous even after upload. Guaranteed globally current attendance requires online-only coordination or restricting offline access.

Closing a kiosk application leaves its device registered; once stale, it suppresses paired results elsewhere. For a permanently retired device, an administrator must first recover/verify its queue and then set its `attendance_devices.enabled` to FALSE. Never retire a device merely to hide a pending backlog. A returning installation automatically re-enables its device record. Do not clone the outbox/device identity to another kiosk or delete it to clear a sync warning.

Before deployment:

1. Take a full MySQL backup and back up every device's cache. Use a coordinated rollout, not mixed old/new clients.
2. Confirm the application identity can create, replace, and flush files in the cache directory.
3. Confirm `students`, all three gate log tables, `event_attendance`, and all five attendance metadata tables use InnoDB. Nontransactional tables block recovery/restore until an administrator migrates them.
4. Start each attendance device online, allow pending queues to drain, and verify the device rows and sequence counters.
5. Keep device clocks synchronized. A backward or ambiguous clock produces unresolved history rather than a guessed pair.

## Cloud attendance snapshots

The existing log-upload workflow now also captures a consistent, point-in-time MySQL snapshot of all five attendance metadata tables, the three gate log tables, and `event_attendance`. Including related logs prevents restored receipts from suppressing logs that were never restored. Original log identity counters are included so purged IDs are not reused after restoration.

- Compressed snapshots are stored in immutable chunks under `attendance_backups/{generation}/chunks/{index}`. A completed manifest is created only after every chunk uploads. Download chooses the newest capture time and verifies the checksum, format, required tables and identity counters before restoring. Interrupted uploads do not replace the last completed snapshot.
- Firestore access rules must authorize the application's existing connection for reading/creating manifests and nested chunks. Do not make these collections public to bypass a permission failure. API keys alone are not authorization. Production rules and network access have not been validated by the local tests.
- The administrator-authorized download workflow restores attendance only when all eight attendance/log tables are empty (bootstrap device rows are allowed). It never merges snapshot metadata into live attendance or replaces existing records. Missing student profiles, local pending queues, or a recently active peer block restoration.
- Stop every attendance client for disaster recovery and use one recovery workstation. Back up each cache before proceeding. Download profiles first, restore attendance, then import older archive logs. Restoration is one InnoDB transaction; a failure rolls back all its rows. Restored devices are marked stale and unready until they reconnect and reconcile.
- This is a point-in-time attendance backup, not a continuously replicated full database or an atomic backup of profiles, settings, photos and attendance together. Keep full MySQL and per-device cache backups. Already-acknowledged transactions newer than the snapshot cannot be recreated from a drained device queue; reconcile that recovery interval before trusting current attendance or paired results.
- A full snapshot is made on each log upload, with limits of 512 MiB uncompressed and 1,024 chunks of 200,000 encoded characters. Oversized snapshots fail explicitly. There is no automatic snapshot retention or orphan-chunk cleanup yet; plan storage capacity and an administrator-managed retention policy.

The existing archive-import pagination and deduplication behavior has not been redesigned by this change. In particular, old gate-log imports match timestamp and transaction type, which is not a globally unique identity. Do not treat archive-only import as a guaranteed complete database restore.

## Remaining Operational Work

There is still no dedicated device-retirement/conflict-review screen, persisted synchronization-error history, automatic clock synchronization/health monitor, or automatic metadata/receipt retention. The changes above do not replace those operational tasks. Deployment backups, cache permissions and enrollment of every physical kiosk must still be confirmed on-site.

## Automated checks

Run the existing verification/QR tests:

```powershell
dotnet run --project NFC_System.Tests/NFC_System.Tests.csproj
```

Run durable queue checks without a database:

```powershell
dotnet run --project NFC_System.AttendanceTests/NFC_System.AttendanceTests.csproj
```

SQL integration tests require a DISPOSABLE MySQL/MariaDB instance on localhost port 23306, root password `attendance-test-only`. The test creates and clears only the `attendance_tests` database on that instance. Never point these tests at production.

```powershell
dotnet run --project NFC_System.AttendanceTests/NFC_System.AttendanceTests.csproj -- --mysql
dotnet build NFC_System.slnx -p:Platform=x64 --no-restore
dotnet build NFC_System.slnx -p:Platform=x64 -c Release --no-restore
```

The SQL suite links production queue, transaction, reconciliation, readiness, snapshot capture and restore code. It substitutes the connection and student-cache boundary. Cloud transport tests use an in-memory HTTP handler for chunking, publication, selection, checksums and failures; they never contact Firestore. Hardware, actual kiosk layout, real cache refresh integration and live Firestore permissions/recovery still require deployment testing. The randomized dense signed-QR image roundtrip has shown an intermittent decode failure; a passing rerun does not establish scanner reliability for every printed QR.

## Tester checklist

### Staff-session, camera and export regression checks (2026-09-21)

1. Sign in as Personnel. With the kiosk open, tap a registered Event Organizer card, then verify a valid student. The organizer tap must not replace the operator or become an unknown student result; the student must still follow the selected verification mode. Return to the main menu and confirm the original Personnel name/role remains. Repeat with Admin and another Personnel card, both with and without a kiosk open.
2. Explicitly sign out, then tap an organizer or personnel card. Normal staff login and its audit record must still work. Authorization taps in administrator confirmation dialogs must authorize only the requested action, not replace the signed-in operator.
3. With two cameras connected, open the kiosk QR stage and change the camera in the controller. The preview and QR input must switch without reopening the kiosk. Switch back, change rapidly, and close/reopen while switching; confirm no crash or old-camera scan is processed. Repeat from both Gate Verification and Event Attendance controllers. Physical switching remains a hardware acceptance test.
4. Export student `00-00010` from Student Management and from the registration preview. The default filename must be `00-00010.png`, not `Student-QR.png`. Cancel and retry export; the saved credential must remain unchanged.

### Attendance and recovery checks

1. Online entry: confirm the student's time-in and an unrecorded time-out. Exit: confirm the same visit now contains both timestamps. Re-enter: confirm a new visit, not the previous exit paired with the new entry.
2. Repeat using Fast, Standard, High Security, and signed-QR fallback. Confirm PIN/QR requirements are unchanged.
3. At second nine, scan another student. Confirm replacement is immediate, the old timeout does not clear the new card, and the new card remains for its own ten seconds. Repeat with large photos and long names at the deployed screen resolution/scaling.
4. Disconnect MySQL, admit and exit a student, then reconnect. Offline results must show only the current transaction. Verify one database row per queued scan, preserved original timestamps, and the final correct entry state.
5. Interrupt recovery after a database commit but before local acknowledgment. Restart; verify no duplicate log, visit, or event record.
6. Fail one record in a batch. Earlier commits must remain, failed/later records must remain queued, and history must stay withheld until recovery succeeds.
7. With two registered kiosks, disconnect one for over fifteen seconds. Paired history on the other must be withheld. Reconnect with a backlog; it must remain withheld until that backlog is acknowledged.
8. Create conflicting offline scans on different kiosks, and separately test a backward device clock. Confirm unresolved history is not presented as a confirmed pair or used to overwrite newer state blindly.
9. Attempt simultaneous online entry on two kiosks. Confirm one grant and one attendance conflict, with no second confirmed visit.
10. Purge eligible cloud-backed log rows while retaining an open visit in a test database. A later exit must still pair with the preserved time-in.
11. Exit an existing INSIDE profile with no visit history. Confirm there is no invented time-in. Investigate/correct an unresolved state, then verify a fresh visit works.
12. Record offline event check-in and check-out. Confirm exactly one event row per action and no fabricated gate visit.
13. Deny cache write/replace permissions in a test installation. An admission that cannot be durably queued must not be granted. A malformed existing queue must remain intact for investigation.
14. Import an older cloud archive into a test database with newer live attendance. Confirm the import does not change the current student entry state. Separately validate the full-database backup/restore procedure.
15. Interrupt MySQL after online verification but before the attendance decision can be confirmed. Confirm no green grant, no offline fallback grant, and no local INSIDE transition from that unconfirmed request. Reconnect and inspect its receipt/decision before retrying.
16. Fail the cache refresh after the queue drains. Confirm `ready=0` and `CacheRefreshRequired=true`, including after restart. Restore permissions/connectivity; the next successful refresh must clear the flag and restore readiness.
17. In a disposable cloud/database deployment, upload a snapshot, then interrupt a second upload before manifest creation. Restore must use the previous complete generation. Missing/corrupt chunks or denied permissions must fail without partial attendance restoration.
18. Restore into empty attendance tables and verify every table, student state and log identity counter. Verify nonempty tables, a live peer, pending local scans, missing profiles or a non-InnoDB metadata table prevent restore. Confirm restored devices cannot immediately advertise ready.
