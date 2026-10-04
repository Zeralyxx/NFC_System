# System Verification - 2026-10-01

## Verdict

Not ready for an unconditional "every feature works" sign-off. The automated suites and source review found three reproducible issues, plus a registration error-handling risk. Hardware, deployed application navigation and live Firestore integration remain unverified.

Application behavior was not changed in this verification pass. Only tests/diagnostics and this report were added. Existing worktree changes were preserved. The production database, installed application, real signing keys and real device caches were not used or modified.

## Results

| Check | Result | Scope |
| --- | --- | --- |
| Verification engine, original suite | 110 passed, 1 failed | Intermittent dense-QR image decoding failed. In-memory database/cache boundaries; production engine and signing code. |
| Verification engine, expanded suite | 111 passed, 2 failed | Two new PIN-state reconciliation regressions fail consistently. The randomized image test passed on this later run, which does not clear its earlier failure. |
| Operations including SQL and capacity | 94 passed, 0 failed | Production import, graduation, roster, event and completion methods against disposable MySQL-compatible SQL storage. |
| Attendance including SQL | 69 passed, 0 failed | Durable queues, rollback/retry, concurrency, device naming and snapshot recovery. |
| WinUI smoke | Passed | Selected production dialogs/controls, QR preview/export filename, success notice, branding, settings and report labels. Not full application navigation. |
| Debug / Release | Both passed | Each incremental build reported 9 existing compiler warnings. |
| Package vulnerability freshness | Unavailable | Verification project reported NU1900 because the NuGet service index could not be reached. No claim of a current dependency-security audit. |
| Whitespace check | Passed | `git diff --check`; Git also reported normal LF/CRLF notices. |

The isolated SQL service was MariaDB 10.4.32 on localhost:23306, using only `attendance_tests` and `nfc_operations_tests`. This is not validation against every deployed MySQL version or configuration.

## Findings

### P1: An older PIN cache overrides the authoritative online lock state

`NFC_System/VerificationEngine.cs:83` reads the local cached profile after a successful database lookup and overwrites both `FailedPinAttempts` and `PinLocked` at lines 86-87.

Reproduced using the production engine:

1. Database profile locked after three failures; older local cache unlocked: a new Standard-mode attempt returns RequiresPin instead of PIN_LOCKED.
2. Database profile unlocked by an administrator; older local cache locked: a new attempt is denied with PIN_LOCKED instead of accepting PIN verification.

These are now failing regression tests in `NFC_System.Tests/Program.cs`. Single-session three-attempt behavior still passes; this finding concerns reconciliation between database and cached state. Correct PIN submission subsequently writes zero failures/unlocked, so stale local state must not bypass a server lock. A fix must also preserve genuine unsynchronized offline failures and support explicit administrator unlocks, rather than simply discarding local progress.

### P2: Some valid signed QR images are not detected with kiosk settings

`NFC_System/KioskModeWindow.xaml.cs:99` uses TryHarder=false, unlike `QrScannerWindow`, which uses TryHarder=true and AutoRotate=true.

The new `--qr-stress` diagnostic generates 80 synthetic signed credentials (40 eight-character IDs and 40 fifty-character IDs), each rendered at 400 and 800 pixels with the application's four-module margin. It compares settings matching the test, kiosk, and scanner window, plus a pure-image diagnostic mode.

Observed in this run:

| Decoder settings | Exact results out of 160 images |
| --- | --- |
| Test default | 154 |
| Kiosk | 154 |
| Scanner window | 160 |
| Pure-image diagnostic only | 160 |

Three of the short-ID credentials failed detection at both resolutions with kiosk/default settings. This is not evidence of bad signatures; the same images decoded with the other settings. Counts can vary with randomized payload/signature patterns. Do not interpret this as a measured field failure rate. Pure-image mode is not a recommended camera configuration.

Synthetic failing payloads are retained in `NFC_System.Tests/bin/Debug/net8.0/qr-decode-diagnostic.json`. No real student QR or installed signing key is involved. Camera performance and printed-card reliability still need physical testing after a decoder change.

### P2: Card reassignment can duplicate audit and metrics rows

The joins in `NFC_System/DatabaseService.SpreadsheetExport.cs:24` and `NFC_System/DatabaseService.cs:1382` match either the logged student ID OR the current owner of the logged NFC UID. The legacy CSV exporter has the same pattern at line 1485.

A read-only synthetic SQL reproduction returned two joined rows for one log: the original student A by ID and student B, who now owns A's old card. This can duplicate audit/metrics output and distort exported measurements. The campus ledger already has a guarded fallback at `DatabaseService.cs:1739`; the remaining queries do not.

Reproduction shape: log `(student_id=A, nfc_uid=CARD-OLD)`; current profiles `(A,CARD-NEW)` and `(B,CARD-OLD)`. The current join returns `(A,A)` and `(A,B)`.

This was verified at the SQL join level, not through an interactive export picker.

### P2, Source Review: Registration duplicate lookup can escape error handling

`NFC_System/RegistrationWindow.xaml.cs:512` awaits `FindPotentialDuplicatesByNameAsync` before the handler's try/catch begins at line 526. The lookup opens a database connection and can throw. A disconnect during that lookup escapes the async-void Save handler instead of reaching the normal registration error notice; `App` does not install an unhandled-exception recovery handler.

This is a source-confirmed missing error boundary, not a reproduced full-application crash. Failure injection into the live registration window was deliberately not attempted.

## Feature Coverage

"Passed" below means the listed automated layer passed, not that every physical or deployed scenario has been certified.

| Feature | Verified evidence | Remaining qualification |
| --- | --- | --- |
| Staff login and student separation | Session takeover rejection, concurrent/delayed login guards, explicit logout, student verification after organizer tap | Actual staff tap routing and login audit on the extended-monitor setup need operator testing. |
| Fast, Standard, High Security and QR fallback | Online/offline engine flows, wrong/mismatched credentials, step order, double-submission guards | PIN-cache reconciliation defect above; actual keypad/camera transport pending. |
| Three PIN attempts | Three retries without rescanning, correct PIN after one/two failures, overlapping submissions | Cross-device/admin lock reconciliation fails. |
| Signed QR validation | Public-certificate verification, tampering, malformed/unsigned values, untrusted keys, rotation, replacement during an attempt | Image decoding defect above; printed cards and installed certificate-store permissions pending. Exact copies remain usable with required credentials by design. |
| Registration and credential editing | Issuance-role checks, pending-profile edits, activation validation and stale-credential rejection | Full enrollment/save/export with actual certificate store pending; registration exception boundary needs repair. |
| QR preview/export and replacement notice | WinUI preview, selected student-ID filename, replacement-only success notice, editor reopening | Interactive file selection and saved PNG reopening not exercised by smoke tests. |
| Ten-second student result replacement | Source confirms replacement stages, state-version timer guard and photo-version guard | No automated physical back-to-back card taps; still needs kiosk testing. |
| Reader/database status | Source confirms separate labels/colors and one-second reader poll/reconnect | USB removal/reinsertion, COM reassignment and independent database outages need physical tests. |
| Camera selection | Camera-change notification logic passes; subscription/version guards reviewed | Real device switching, permission denial and camera contention pending. |
| Time-in/time-out display | SQL visit pairing, unsafe-history suppression, offline recovery, raw-log purge survival | Extended-monitor rendering and real multi-device scans pending. Separate previous-visit last time-out remains unimplemented. |
| Offline durability | Restart survival, atomic queue operations, rollback, ordered replay, receipts, corrupt storage denial | Actual deployed cache ACLs/disk failures and power-loss tests pending. |
| Multiple kiosks | Separate SQL connections, concurrent admission protection, stale/unready/sequence-gap checks | Real independent devices and time-zone/clock configuration pending. |
| CSV/XLSX import and templates | Format/validation tests, duplicate rejection, Pending Enrollment, no credential overwrite | UI picker interaction and actual operator enrollment flow pending. |
| Directory filters/sorting/latest import | Combined filters, numeric-year sorting, ascending/descending, source/UI wiring and control smoke tests | Latest-import selection is intentionally window-local; full window performance not measured. |
| Graduation clearance and batch updates | Eligibility, exclusions, revision conflicts, stale previews, role/session checks and SQL rollback | Operator authorization prompts/taps pending. |
| Group event rosters | Explicit selection, duplicates/inactive exclusion, removals, revision/conflict tests | Live Firestore preconditions/permissions and real offline roster refresh pending. |
| Event mode changes | Backend role rules, stale/closed-event checks, event-specific notifications and current-attempt preservation logic | Actual in-flight PIN/QR window interactions pending. |
| Duration completion, no check-in-only policy | Rules, paired duration, gap exclusion, allowances, synchronization, reviews/audits, stale evidence, SQL persistence and WinUI dialogs | Full staged event lifecycle with physical devices pending. Old/uncertain records intentionally require review. |
| Analytics | Unique arrivals, honest last-recorded states, uncapped aggregate totals and recorded-day alert extremes | Audit/metrics duplicate-join defect above. |
| CSV/Excel report export | Typed values, text IDs, formula-like text handling, event/date filtering logic | Metrics row-join defect; complete interactive exports pending. |
| Device naming | Stable IDs, role guards, rename audits, captured historical names, offline retries and backup round-trip | Actual administrator Save button interaction pending. |
| Cloud push/pull and snapshots | Pagination failures, conditional roster writes using fake HTTP/cloud boundary; SQL restore; snapshot v1/v2 recovery | No live Firebase access-rule, network interruption, credentials or quota tests. Profile deletion tombstones remain absent. |
| Local log cleanup | Source checks 1/5/10-year UI choices and cloud-synced-only deletion; SQL tests verify attendance metadata survives raw-log removal | Actual production purge method/button not invoked. No automatic retention scheduler; no cleanup runs while the app is closed. |
| App icon/logo | WinUI branding images and ICO assignment smoke passed | Installed package/Start menu/taskbar assets on another PC pending. |

## Capacity Check

10,000 synthetic profiles were imported through the production SQL import method into the disposable database: all remained Pending Enrollment with no NFC/PIN/QR and Not Reviewed clearance. The CSV/XLSX round-trip preserved every ID. Directory filtering/sorting and the 10,000-entry roster preview retained the correct selections.

Observed single-run timings: CSV/XLSX round-trip and validation 2,228 ms; in-memory directory/roster rules 5 ms; SQL import and count verification 2,276 ms. These are diagnostic observations on this machine, not guarantees of full UI responsiveness or production throughput. No 10,000-student data was added to the live system.

## Next Acceptance Steps

1. Fix the PIN database/cache precedence with explicit pending-state and unlock semantics; make both new regression tests pass.
2. Improve kiosk QR detection using the retained synthetic cases, then measure real camera/printed-card behavior.
3. Apply the guarded student-ID/NFC fallback consistently to audit and metrics queries and add reassignment regression tests.
4. Move the registration duplicate lookup inside a handled error boundary; inject database loss in a staging UI session.
5. Run physical card/keypad/camera/extended-monitor tests and staging Firestore push/pull/recovery tests. Verify deployment cache and certificate permissions. Only then sign off on end-to-end operation.

## Reproduction Commands

Run from the repository. `--mysql` requires the isolated test server on port 23306; never repoint these suites to production.

```powershell
dotnet run --project NFC_System.Tests/NFC_System.Tests.csproj --no-restore
dotnet run --project NFC_System.Tests/NFC_System.Tests.csproj --no-restore -- --qr-stress
dotnet run --project NFC_System.OperationsTests/NFC_System.OperationsTests.csproj --no-restore -- --mysql
dotnet run --project NFC_System.AttendanceTests/NFC_System.AttendanceTests.csproj --no-restore -- --mysql
dotnet run --project NFC_System.WinUiSmoke/NFC_System.WinUiSmoke.csproj -p:Platform=x64 --no-restore
dotnet build NFC_System.slnx -p:Platform=x64 --no-restore -v:q
dotnet build NFC_System.slnx -p:Platform=x64 -c Release --no-restore -v:q
```

The verification suite now deliberately reports the two newly exposed PIN regressions as failures. The QR diagnostic also returns nonzero when an ordinary reader configuration misses a generated image. A passing random rerun does not resolve the retained failures.
