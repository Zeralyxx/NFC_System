# PIN security and historical reporting

Implemented October 2, 2026. This change does not alter QR signing keys, reader ports, or hardware configuration.

## Behavior

- Online verification uses current database PIN state, not the cached profile's lock flag or attempt counter.
- Each submission rechecks the student under a database row lock. Concurrent failures increment the shared counter; the third failure locks it. A correct PIN cannot clear an existing lock.
- An explicit unlock, PIN replacement, or profile-editor PIN replacement rotates a reset token. Older sessions must start again; older queued attempts cannot reverse that reset.
- Existing personnel/event-window unlock controls use the same reset operation. Personnel and event organizers may unlock through the existing controls; replacing the PIN remains an administrator action. Authorization and session identity are rechecked before committing.
- Gate and event admission recheck the lock, reset token and credential fingerprint in the attendance transaction, including a change after PIN acceptance while QR verification is pending.
- Failed confirmation or local persistence does not turn an online PIN attempt into an offline grant. Reconnect and start a new verification attempt.
- Missing/corrupt PIN journals block verification; they are not silently replaced. Preserve the file for investigation. Null legacy PIN counters/lock fields are treated conservatively as locked.

## Offline and synchronization

`C:\ProgramData\NFC_System\Cache\pin_security.json` contains confirmed PIN security states and pending attempts. It is flushed and atomically replaced under a file lock. It stores no entered PINs. Keep the existing cache directory restricted to the application identity and authorized administrators.

Two InnoDB tables are created automatically as needed:

- `pin_security_versions`: student reset tokens and revisions.
- `pin_attempt_receipts`: committed attempt decisions, keyed by unique attempt ID.

Offline attempts update the durable journal before a result is accepted. Recovery applies them in journal order. Counter changes and receipts commit in one transaction. A retry after a lost acknowledgment finds the original receipt instead of counting again, then refreshes current state so an old receipt cannot restore an old lock after an unlock.

Recovered successful PINs reset only the failure state they actually observed. They do not erase intervening failures from another device. Queued work for a deleted student is rejected and acknowledged without blocking everyone else.

Recovery runs while the application is running. Disconnected devices cannot immediately know about locks, resets, or attempts on other devices. A globally enforced three-attempt limit across disconnected kiosks is not possible with this offline model; synchronization may conservatively lock an account when devices reconnect. Cross-device online coordination assumes the devices use the same authoritative MySQL database.

Cloud profile pulls no longer overwrite existing local PIN hashes, salts, counters or locks with backup values. A genuinely new restored profile still receives its backed-up fields. The attendance-only cloud snapshot format is unchanged; it is not a full PIN-security backup. Use a full MySQL backup (including both new tables) plus every device's cache for coordinated recovery. A fresh database with newly created reset tokens rejects old-token queued attempts. Never delete just the PIN metadata or journal as routine cleanup. PIN receipt retention is not automated.

## Historical reports

Audit, campus ledger, CSV metrics and spreadsheet metrics join historical logs only by the recorded student ID. They prefer the name stored in the log. Reassigning an NFC card cannot add a second joined row or make the new card owner appear as the old log's student.

Deleted students' logs remain visible. Logs without a recorded student ID retain their recorded details or the view's unknown/empty fallback; their owner is not guessed from the current card assignment. Real repeated scans are preserved. No historical records are deleted or rewritten by this fix.

## Rollout

1. Back up the full MySQL database and each device's entire cache directory. Drain the old clients' attendance queues and review any unresolved offline PIN lockouts before upgrading: older caches did not record reset tokens, so their lock history cannot be reconciled reliably after the fact.
2. Deploy the same build to every admin/kiosk computer. Do not leave old clients writing unversioned counters. This work updated source/builds, not the installed deployment.
3. Start every device online and allow the shadow-cache refresh to finish before testing offline. The application identity needs permission to create the two tables and to create, flush and replace files in the cache directory.
4. Confirm `pin_security.json` exists and both new tables use InnoDB. Do not edit that JSON manually. A legacy cache alone is insufficient for offline verification after this upgrade.
5. Test the scenarios below on staging hardware before production rollout. QR certificates do not need reinstalling for this change.

## Tester scenarios

1. Enter two wrong PINs without rescanning: see 1/3 then 2/3. The third locks. A correct PIN after the first or second failure resets the counter.
2. Begin verification on kiosk A. Lock the same student on kiosk B. A must not grant with the correct PIN, including if A was already waiting for a QR.
3. Unlock through the admin or existing staff override. A fresh online scan succeeds; an already-open pre-reset session must restart.
4. Offline: enter wrong PINs, restart the application, and confirm progress/lock survives. Reconnect; retries must not increment the same attempt again.
5. Queue failures offline, then unlock online from another device. Reconnect the first device; its old-token attempts must not undo the unlock.
6. Replace the PIN through both supported admin paths. Old sessions and cached old PIN credentials must not authenticate after synchronization.
7. Simulate lost connectivity during online PIN submission: expect confirmation pending/no grant. Reconnect and begin a fresh attempt.
8. Record scans for A, assign A's old card to B, and check audit, ledger, CSV and XLSX reports. Each original log appears once and remains A's. Deleted-profile and legacy/no-ID logs remain present without attribution to B.

## Verification scope

Final automated results on October 2, 2026:

- Verification suite: 114 passed, 0 failed.
- Operations suite, including production PIN SQL/journal and historical report join tests: 116 passed, 0 failed.
- Attendance suite, including six final gate/event admission security-change scenarios: 75 passed, 0 failed.
- WinUI smoke: passed.
- Debug and Release x64 builds: passed with existing warnings. NuGet vulnerability-index retrieval was unavailable in the verification suite, so these results do not certify dependency vulnerability status.
- Live database, installed application, real cache, signing certificates and reader configuration were not modified. The disposable database was shut down after testing.

Automated tests exercise the production PIN journal, rules, database transactions, retries, profile edits, session checks and final attendance guard against a disposable MariaDB 10.4.32 instance on port 23306. Reporting tests execute the join predicates extracted from all four production query sites with reassigned-card, deleted-owner, legacy and genuine-repeat fixtures. They do not replace interactive report/export acceptance testing.

WinUI smoke checks and Debug/Release builds are run separately. Physical two-kiosk/card-reader tests, real deployment cache permissions and live cloud recovery remain staging acceptance tasks. The previously reported QR image-decoding and registration error-boundary findings are outside this change.
