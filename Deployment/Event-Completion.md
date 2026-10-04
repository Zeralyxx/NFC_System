# Duration-Based Event Completion

## Workflow

1. Deploy the same updated client to every attendance device. Drain existing queues and keep all participating devices online with synchronized clocks. Back up MySQL and every device cache before rollout.
2. Before the scheduled start, select the event in Event Management and open **Attendance Requirements**. Alternatively use Analytics & Reporting Center > Event Analytics > select event > Attendance Requirements.
3. Set start/end, required whole minutes, late-arrival allowance and early-departure allowance. Supply a reason. Requirements are stored separately from the event creation date and verification mode.
4. Students record event check-in AND check-out. Re-entry is supported; time between a departure and the next arrival does not count. Only time inside the scheduled interval counts. This does not change gate access, QR/PIN verification, roster eligibility or graduation clearance.
5. After the scheduled end and after the final student check-out, close the event. Closing does not create departures. Keep the attendance devices running and online until their queues and caches reconcile.
6. In Event Analytics, select the event and open **Completion Review**, or use **Export Completion** for CSV/XLSX. Each opening/export evaluates a fresh database snapshot. Existing scan-state reports remain separate.
7. An Admin/Master Admin or Event Organizer may select a student and record Completed, Incomplete or Needs Review with a mandatory reason. The backend checks permissions, login session, synchronization and concurrent changes again before saving. The original scans are not edited.

There is no check-in-only policy. Required duration cannot be zero. A single check-in never automatically qualifies as Completed.

## Status Rules

| Status | Meaning |
| --- | --- |
| In Progress | Sufficiently trustworthy evidence, but event is still active or scheduled end has not arrived. Minutes include completed pairs only. |
| Completed | Closed event after its scheduled end, synchronized devices, valid pairs, required duration reached, arrival/departure allowances met; or a current authorized manual decision. |
| Incomplete | Trustworthy paired evidence fails required duration or arrival/departure allowance; or a current authorized manual decision. |
| Needs Review | Missing requirements, missing check-out, orphan check-out, repeated entry, uncertain timestamps, legacy/untracked evidence, unavailable synchronization, no paired evidence, or a stale manual decision. |

Automatic evaluation uses the first recorded arrival and last recorded departure for allowance checks, and sums every paired interval clipped to the schedule. Allowance boundaries are inclusive.

Requirements first configured after the scheduled start cannot establish complete tracking coverage retrospectively. Such events require manual review, including old events created before this feature. No attendance means Needs Review, not an inferred absence.

## Synchronization and Review

- Every enabled attendance device and every device contributing evidence must have a heartbeat within 15 seconds, be ready, and have matching reported/acknowledged counters. At least one enabled device must exist. Missing device registrations also withhold completion.
- Disabled contributing devices are still checked; disabling one is not a way to bypass unfinished uploads. A permanently unavailable device currently blocks final review until its data/readiness can be reconciled. Do not delete device registrations to force a result.
- Single-device offline pairs can qualify after recovery. Recovered evidence spanning devices requires review because clock order is uncertain.
- Manual decisions are bound to the exact policy revision and attendance evidence. Later scans or requirement changes invalidate them instead of silently preserving a potentially wrong Completed status. A stale review form cannot overwrite another review.
- Reports are point-in-time results, not permanent certificates. A fresh report can become Needs Review when devices go stale or new evidence arrives. Exported files contain the evaluation time and synchronization status. Heartbeats are a conservative freshness check, not proof that a device cannot disconnect immediately afterward.
- All devices must run the new build. Old clients do not write durable completion evidence. Keep server and device time zones consistent; no timezone conversion or automatic clock synchronization is added here.

## Storage and Recovery

New InnoDB tables:

- `event_completion_policies`: schedule, minimum duration, allowances, revision and tracking coverage start.
- `event_completion_scans`: durable transaction/device/sequence evidence, saved in the same transaction as the attendance receipt and event log. Retry does not duplicate evidence.
- `event_completion_audit`: append-only policy changes and manual decisions, with actor, role, device, UTC review time, reason and evidence fingerprint.

Normal old-log cleanup does not delete these tables. Completion evidence remains usable when its raw event log has been purged. Policy and review operations fail closed if required event/roster/device tables are not InnoDB.

Attendance cloud snapshots now use version 2 and include all three tables alongside the existing nine tables. Version 1 snapshots can still be restored, but do not supply completion evidence or requirements, so legacy attendance must be reviewed. Restore still refuses populated attendance/completion tables. Older app versions cannot read version 2 snapshots; deploy updated clients together.

These tables are included in the attendance snapshot, not separate Firestore live collections. A full recovery also needs the ordinary student/event/roster data. Independent MySQL installations are not synchronized by these completion tables. No metadata retention job or permanent completion freeze is introduced.

## Tester Scenarios

1. Configure a future event with 10 required minutes, one minute late/early allowance. Record valid entry/exit, wait until end, close and synchronize: Completed.
2. Repeat with too little paired time, late arrival or early departure: Incomplete.
3. Leave and return mid-event: time outside must not count. Time before start or after end must not increase attendance minutes.
4. Scan entry only, close the event: Needs Review, never an invented check-out or automatic Completed.
5. Exercise repeated entries, orphan exits, tied/backward/future timestamps and old untracked logs: Needs Review.
6. Leave an event active or close it before the scheduled end: no final automatic decision. Missing or inconsistent evidence may already be Needs Review.
7. Disconnect a participating device or leave queued scans: automatic final decisions and manual review are withheld. Drain and refresh devices; reopen the report.
8. Recover paired offline scans from one device, then try recovered scans spanning devices. Only the former can automatically qualify once synchronized.
9. Save a manual review with a reason. Verify actor/device/time/reason in the audit. Empty reasons, personnel accounts and stale login sessions must fail.
10. Open two review forms: saving one makes the other stale. Upload a delayed scan or change requirements: previous manual decision must become Needs Review.
11. Purge raw logs on a disposable database: durable paired minutes remain. Take and restore a version 2 snapshot into an empty disposable database: policies, evidence and audit survive. Restore a version 1 snapshot: no completion is fabricated.
12. Check the three dialogs at small and large window sizes, filtering/search, completion CSV/XLSX export and ordinary scan-state exports. Test actual multi-device reader workflows separately from automated fixtures.

## Automated Checks

`NFC_System.OperationsTests` covers pure rules, policy revisions, role/session checks, readiness, retained evidence and audited review conflicts against an isolated MySQL database.

`NFC_System.AttendanceTests` covers atomic evidence rollback/retry and version 1/2 snapshot recovery, plus existing attendance tests. Its temporary cache requires permission to create, replace and flush files.

`NFC_System.WinUiSmoke` renders production completion dialogs at 640x480 and 1000x800, checks bounded/nonblank bodies and disabled reviews when synchronization is uncertain. It does not connect to the live application database or operate physical readers.
