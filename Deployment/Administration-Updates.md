# Administration Updates

## Before rollout

1. Back up MySQL and every device's cache. Keep the existing QR certificates/private keys; this release does not require new signing certificates or PowerShell provisioning.
2. Deploy the same application version to all administrators and kiosks. Older clients do not enforce the new graduation checks or roster revocations. Do not mix versions or roll back to an approval-only client.
3. Start online with an application database account allowed to create tables and add columns/indexes. Startup adds graduation-clearance columns, `student_graduation_audit`, `event_roster_sync`, and device attribution. Existing clearances default to **Not Reviewed**; existing enrollment statuses are preserved.
4. Student imports require InnoDB `students`, `courses`, and `alerts`. Graduation changes require InnoDB `students`, `student_graduation_audit`, and `alerts`. Event changes require InnoDB `events`, `event_approved_students`, `event_roster_sync`, `students`, and `alerts`.
5. In Settings, assign a recognizable device name, such as `Main Gate - Entry`. The permanent device ID must not be reset. Never copy one device's attendance outbox onto another active device.
6. Complete the checks below on a test database before production rollout. Hardware, file-picker interaction, and actual cloud permissions still need operator testing.

## What changed

| Area | Workflow |
| --- | --- |
| Student profiles | Student Directory > Import Students > Template or Choose CSV / Excel. Templates support UTF-8 CSV and real `.xlsx`; no Office installation is required. `.xls` is not supported. |
| Import validation | Preview every row. Existing IDs, repeated IDs, invalid fields, unrecognized courses, numeric Excel IDs, and Excel formulas block the entire profile import. Existing records are never overwritten. |
| Enrollment | Imported profiles are **Pending Enrollment**, without NFC, PIN or QR credentials. Demographics and IDs can be corrected while pending without a card or signing certificate. IDs remain text, including letters and leading zeros. Assign the first NFC card and PIN, issue its signed QR, and activate it. Activation is checked again when saving; first card assignment needs no replacement reason. Ordinary edits preserve existing credentials. |
| Event roster | Open an event's editor. Filter the existing directory by course, year, section, status and search; select students; preview additions/removals; apply the explicit selection. An ID-only CSV/XLSX roster template is also available. Unknown/inactive/duplicate entries are shown and excluded from additions. |
| Graduation | Student Directory > Graduation Clearance. Review a student, choose Not Reviewed / Cleared / On Hold, provide a reason and authorize the change. Clearance does not itself change campus-access status. |
| Graduation batch | Preview the exact matching students. Only Cleared students can newly become Graduated. Held/unreviewed students are excluded. A changed eligible student invalidates the save and rolls back the batch. Reopen the preview and confirm again. |
| Exports | Audit, traffic, event reports and performance metrics offer Excel Workbook or UTF-8 CSV in the save picker. Excel preserves text IDs and typed dates/numbers. Device ID and the captured device name are included. Exports retain the report's existing query/filter limits; they are not full database backups. |
| Device names | Settings > device identity. Renames require an Admin/Master Admin session. New logs capture the new name; old and queued logs retain their original name. Unattributed historical records display Unknown/Legacy. Offline renames retain an audit entry for retry. |
| Event modes | An authorized Admin/Master Admin can edit an active event's mode without resetting its date, restricted status, or active state. Event Organizer sessions cannot change the mode. Admin authorization follows the event editor's NFC/PIN prompt. |
| Kiosk mode updates | Updates are event-specific. A mode/direction change does not interrupt an active PIN/QR attempt. A new online event attempt reloads the event configuration; closed/unavailable events cannot start a new attempt. Offline kiosks retain the last cached active event configuration. |

## Cloud behavior and limits

- Downloads follow every Firestore page. A failure while reading a collection is reported instead of treating a partial collection as complete. Previously completed collections may already have been applied; the entire multi-collection download is not one transaction.
- Failed archive uploads are reported and unsuccessful records remain unsynced for retry.
- Uploads do not delete cloud documents merely because they are absent locally. **Student/profile deletions still have no cloud tombstones** and pulling an older profile archive can reintroduce absent profiles. Reconcile those deletions before pulling archives.
- Roster edits now keep durable approvals/revocations in `event_roster_sync`, committed atomically with the effective `event_approved_students` membership. Cloud Push retries pending revisions into `event_roster_state`, using Firestore server preconditions, not device-clock ordering. A lost response can be retried without inventing another revision. Legacy `event_approved_students` cloud documents are read only for pairs with no versioned/local state; they never override a known revocation.
- Before the first cloud pull, reconcile roster removals made **before this upgrade**. Their deletion history was not recorded and cannot be reconstructed. Ensure staging/production Firestore access rules permit the new `event_roster_state` collection under your existing authorization policy; do not make the database public to enable it. Keep these cloud revocations and the SQL sync table in backups; do not purge them as ordinary logs.
- Competing roster revisions are excluded and reported as sync conflicts. The event directory labels affected rows `Sync conflict: excluded`. For an active restricted event, select the student and explicitly add them again, or use Remove to confirm exclusion, then push. Event Administration > Roster sync conflicts also lets an authorized operator confirm exclusion for any event, including closed events. Review rechecks the selected revision and rejects stale confirmations. Ordinary retries do not clear conflicts. Shared authoritative MySQL remains the supported multi-admin arrangement.
- Roster state is a separate reference-data collection, not an extra attendance snapshot table. A full MySQL backup includes its pending changes; uploading an attendance snapshot alone does not upload pending roster revocations. Do not clear local state to dismiss a conflict.
- Student uploads include graduation clearance, reason, reviewer, time and revision. When pulling, missing profiles receive those archived values; existing profiles retain their local enrollment status and clearance. A pull cannot clear a local hold or graduate an existing student. Separate independent databases do not automatically merge competing clearance decisions; administrators should share the authoritative MySQL database.
- Graduation actions are also written transactionally to the normal device-attributed audit log, which participates in ordinary log upload. The dedicated `student_graduation_audit` table is local SQL history, not an additional attendance-snapshot table. Keep full MySQL backups.
- Device attribution travels with gate/event/audit archives and attendance snapshots. New archive document IDs include the source device ID; old unattributed IDs retain their existing format.
- Offline caches are still subject to refresh timing. QR replacement, event changes and enrollment changes cannot be instantly known by a disconnected kiosk.

## Tester checklist

1. Download CSV and XLSX profile templates. Import a valid file containing leading-zero IDs and names with commas. Check Pending Enrollment, empty credentials and unchanged existing records.
2. Try repeated/existing IDs, missing columns, credential columns, unknown course, invalid email/year, formulas and numeric Excel IDs. Confirm no profiles from the rejected file were saved.
3. Activate an imported profile with both a newly entered NFC and PIN; verify the preview allows it, signed QR issuance succeeds, and the saved credentials verify. Omit either credential and confirm activation is blocked. A batch must not activate unenrolled profiles.
4. Preview a restricted-event group using course/year/section/status/search, then add the selected students. Confirm only the explicit eligible selection was added. Repeat to confirm no duplicates. Import an ID-only roster; inspect unknown/inactive/duplicate rows. Bulk-remove selected attendees without affecting other events.
5. Mark one student Cleared, another On Hold, and leave another Not Reviewed. Preview graduation: only Cleared is included. Change that clearance from a second admin window after preview; saving the original batch must fail without partially graduating anyone. Repeat through individual status/profile edits.
6. Confirm Personnel/Event Organizer/logged-out sessions cannot change clearance, issue profile imports, or rename devices. Confirm a dismissed admin authorization dialog leaves no pending tap action.
7. Export filtered audit, traffic, event and metric reports as CSV and XLSX. Open them in Excel/LibreOffice; check IDs, timestamps, numeric measurements, filtering, column selection and device fields. Cancel the picker and confirm no success notification appears.
8. Rename a device, scan online, disconnect, scan offline, rename again, restart and reconnect. Verify queued scans keep their original device ID/name, new scans use the new name, and recovery does not duplicate logs or rename audits.
9. Change event A from Standard to High Security while a student is entering a PIN. The in-progress attempt must retain its original requirements; the next online attempt must require the new mode. Event B and gate verification must be unaffected. Repeat while a kiosk is offline and after reconnecting. Close the event and confirm no new online attempt starts.
10. Regression-test staff-login separation, reader unplug/replug, independent database/reader colors, 10-second result replacement, three PIN attempts, signed QR rejection/replacement, and offline attendance recovery on the extended-monitor kiosk.
11. On a staging Firestore project, test a collection exceeding one page, a failed later-page read, denied write permission and interrupted upload. Verify visible failures and successful retries. Do not use production records for failure injection.
12. Import `STU-001` and `000123`. Without an NFC card or signing key, correct both profiles' names/email and save. IDs must not change, both NFC/QR fields must remain empty, and status must remain Pending Enrollment. Check that setting Active without enrolled NFC/PIN is rejected. Assigning the first NFC card should not ask for a replacement reason; replacing an existing card still must.
13. In staging, add an attendee, push, remove, push and pull. Keep the old approval-only cloud document present: it must not restore access. Repeat with cloud writes denied during removal, restart and retry. Re-add explicitly and confirm a new approval revision. Simulate a competing cloud revision; the attendee must be excluded, visibly flagged and remain excluded on retry until reviewed. Refresh each kiosk's cache before testing offline; an already disconnected kiosk cannot learn a new revocation instantly.

## Automated checks

```powershell
dotnet build NFC_System.slnx -p:Platform=x64 --no-restore
dotnet build NFC_System.slnx -p:Platform=x64 -c Release --no-restore
dotnet run --project NFC_System.Tests/NFC_System.Tests.csproj --no-restore
dotnet run --project NFC_System.OperationsTests/NFC_System.OperationsTests.csproj --no-restore
dotnet run --project NFC_System.AttendanceTests/NFC_System.AttendanceTests.csproj --no-restore
dotnet run --project NFC_System.WinUiSmoke/NFC_System.WinUiSmoke.csproj -p:Platform=x64 --no-restore
```

SQL tests use only the disposable localhost:23306 server with test password `attendance-test-only`. Attendance tests clear `attendance_tests`; operations tests clear `nfc_operations_tests`. Never redirect them to production:

```powershell
dotnet run --project NFC_System.OperationsTests/NFC_System.OperationsTests.csproj --no-restore -- --mysql
dotnet run --project NFC_System.AttendanceTests/NFC_System.AttendanceTests.csproj --no-restore -- --mysql
```

Operations SQL tests exercise production import/clearance/batch/event methods, credential-free profile saves, roster revocation/recovery/rollback and concurrent upload cases, with test-only device/session boundaries. Firestore requests are checked with a fake HTTP handler and synchronization uses a fake cloud store against the disposable MySQL database. This is not a live Firestore test. First credential enrollment still needs end-to-end UI testing. WinUI smoke checks render clearance dialogs and existing QR/branding controls; interactive file-picking and complete window navigation remain manual checks.

Conditional cloud writes follow the [Firestore PATCH precondition](https://firebase.google.com/docs/firestore/reference/rest/v1/projects.databases.documents/patch) and [server update-time precondition](https://firebase.google.com/docs/firestore/reference/rest/v1/Precondition) contracts.
