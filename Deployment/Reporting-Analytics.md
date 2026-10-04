# Reporting Analytics

## Corrected calculations

- Campus daily totals aggregate all retained gate rows across the three modes in MySQL. They are no longer calculated from the latest 500 ledger rows. Event attendance is excluded.
- The campus explorer defaults to the latest 500 rows. Selecting a date loads up to 10,000 rows for that full calendar date. Its title and the campus ledger note disclose scope/limits. Reaching the cap warns that the list may be incomplete. Campus export uses the currently loaded, filtered explorer records, not all historical rows or the daily aggregate table.
- The low/high denied-attempt calculation includes zero-denial days that have gate records. Its 30-day window includes today and the previous 29 dates; future rows are excluded. Dates with no retained records are not asserted to be zero-activity days.
- Event counts/breakdowns use one first recorded arrival per nonempty student ID. Repeat arrivals no longer inflate percentages. Section groups include course so identically named sections in different courses are not merged.
- Restricted-event turnout is the intersection of unique arrivals and the current approved roster, divided by that roster's unique count. Empty rosters and open events display N/A. A current roster is not a historical enrollment snapshot.
- Event status is the last recorded PRESENT/DEPARTED state, not proof of completion, duration, or early departure. Closing an event does not rewrite its interpretation. Conflicting latest timestamps, missing timestamps and departure-only histories are unresolved.
- Event dates compare the full raw date including year; AM/PM filters use the raw hour rather than localized display text.
- Event export contains one latest matching row per student. Its action is within the selected filters; its state column refers to the whole loaded event. Main ledger and explorer filters stay independent.
- Refresh reloads reports and the selected event. Failures clear affected figures and show errors; stale event/date requests cannot overwrite newer requests. Reports display load time and offline/stale limitations.

## Remaining limits

Reports are based on the retained database, not a merged Firestore archive or a confirmed all-devices-synchronized snapshot. Pending device uploads, log cleanup, and changed student demographics/rosters can affect historical interpretation. Inside is explicitly a log-derived estimate, not verified physical occupancy. Separate queries can observe scans arriving during a refresh; the full dashboard is not a single transactional snapshot. Arbitrary date ranges, device comparison dashboards, and server-paged unlimited ledger exports remain future work.

## Operator checks

1. Use an isolated test database. Record more than 500 gate scans on one date across the modes, plus event scans. Confirm campus totals include all gate scans and exclude event scans.
2. Open that date and check the explorer/export scope. Select a different year with the same month/day and confirm it does not match the original year's event rows.
3. Record entry, departure, then re-entry for one event student. Confirm one unique arrival, one contribution to each breakdown, and the newest recorded state. Close the event; it must not become Completed.
4. Use a restricted event with attendees not on its current roster; verify only roster members count toward turnout. Empty roster must show N/A.
5. Change filters in each explorer and verify they do not overwrite the other ledger's filter results. Export and compare selected rows and scope.
6. Switch events/dates quickly on a slow connection. The final selection must win. Disconnect the database and refresh; do not retain old numbers as a successful new report.
7. Check long notes and status labels on the deployment display, including Windows scaling. Inspect reports in both university and event modes.

Automated coverage includes unique arrivals, re-entry, unresolved history, roster intersection, year/time filters, and real SQL aggregates above 500 rows on the disposable database at port 23306. No production records are seeded by these tests.
