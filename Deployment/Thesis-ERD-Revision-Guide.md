# Thesis ERD Revision Guide

Scope: revise Figure 16 on page 87 and the ERD explanation on pages 88-90 of `Final Draft Chapter 1-5 Thesis.pdf`. This guide reflects the current application's schema creation and migration code, not a query of the installed database. No application or database changes are required by this document.

## 1. Diagram Scope and Layout

Keep all 11 existing tables and add the 12 tables below, for 23 tables in total.

Use a landscape overview or split the detailed diagram into linked panels rather than shrinking 23 tables into the current portrait-page space. Suggested panels:

- Core records and verification: COURSES, STUDENTS, STAFF, EVENTS, EVENT_APPROVED_STUDENTS, EVENT_ATTENDANCE, the three mode-log tables, ALERTS, APP_SETTINGS.
- Campus attendance and synchronization: ATTENDANCE_DEVICES, ATTENDANCE_RECEIPTS, ATTENDANCE_DECISIONS, ATTENDANCE_CURRENT, ATTENDANCE_VISITS. Repeat STUDENTS as a reference entity.
- Event completion, roster synchronization, and security audits: EVENT_ROSTER_SYNC, EVENT_COMPLETION_POLICIES, EVENT_COMPLETION_SCANS, EVENT_COMPLETION_AUDIT, PIN_SECURITY_VERSIONS, PIN_ATTEMPT_RECEIPTS, STUDENT_GRADUATION_AUDIT. Repeat STUDENTS, EVENTS, ATTENDANCE_DEVICES, and EVENT_ATTENDANCE as reference entities where needed.

Repeated reference boxes represent the same table, not additional tables. Keep Figure 16 if it remains one multi-panel figure; otherwise update subsequent figure numbers and cross-references.

## 2. Changes to Existing Tables

### STUDENTS

Change `qr_credential` from `VARCHAR(255)` to `TEXT`, nullable. The field stores the issued QR credential; do not create a separate QR table unless the implementation changes.

Add:

| Column | Type | Nullability/default |
| --- | --- | --- |
| graduation_clearance | VARCHAR(20) | NOT NULL, DEFAULT 'Not Reviewed' |
| graduation_reason | VARCHAR(1000) | NOT NULL, DEFAULT '' |
| graduation_changed_by | VARCHAR(150) | NOT NULL, DEFAULT '' |
| graduation_changed_at_utc | DATETIME(6) | NULL |
| graduation_revision | BIGINT | NOT NULL, DEFAULT 0 |

Keep `student_id` as PK and `nfc_uid` as a unique nullable value. Keep the existing PIN, occupancy, photo, and temporary-profile fields. `graduation_changed_by` is stored text, not a foreign key to STAFF.

### Device Attribution

Add the following to FAST_MODE_LOGS, STANDARD_MODE_LOGS, HIGH_SECURITY_MODE_LOGS, EVENT_ATTENDANCE, and ALERTS:

| Column | Type | Nullability/default |
| --- | --- | --- |
| device_id | VARCHAR(32) | NOT NULL, DEFAULT '' |
| device_name | VARCHAR(100) | NOT NULL, DEFAULT 'Unknown/Legacy' |

`device_id` is the logical device reference. `device_name` preserves the recorded name, so do not replace it with only a relationship to the current device name. Empty device IDs are possible for legacy records.

### ALERTS

Also add `device_name_change_id VARCHAR(32) NULL`, with the unique index `unique_device_name_change`. This value identifies a device-rename audit operation; it does not reference another SQL table.

### Existing Fields That Stay

- Keep the three separate verification-mode log tables and their current timing fields.
- Keep EVENT_ATTENDANCE as individual event scan records. Do not rename its `timestamp` to `time_in` or add fictional duration/completion columns.
- Keep EVENTS with its existing fields. Event start/end times and completion thresholds belong in EVENT_COMPLETION_POLICIES.
- Keep STAFF and APP_SETTINGS. No new staff ID, event owner ID, course ID, or credential table exists in the current schema.

## 3. Add These 12 Tables

The definitions below reproduce the current columns and primary/unique keys. They intentionally contain no `FOREIGN KEY` clauses, matching the application code. Logical links are listed separately in section 4.

### ATTENDANCE_DEVICES

```sql
device_id                 VARCHAR(32)  PRIMARY KEY
device_name               VARCHAR(100) NOT NULL DEFAULT 'Unknown/Legacy'
last_seen                 DATETIME(3)  NOT NULL
reported_sequence         BIGINT       NOT NULL DEFAULT 0
acknowledged_sequence     BIGINT       NOT NULL DEFAULT 0
ready                     BOOLEAN      NOT NULL DEFAULT FALSE
enabled                   BOOLEAN      NOT NULL DEFAULT TRUE
```

### ATTENDANCE_RECEIPTS

```sql
transaction_id            VARCHAR(32) PRIMARY KEY
device_id                 VARCHAR(32) NOT NULL
device_sequence           BIGINT      NOT NULL
UNIQUE KEY device_order (device_id, device_sequence)
```

### ATTENDANCE_DECISIONS

```sql
transaction_id            VARCHAR(32) PRIMARY KEY
is_granted                BOOLEAN     NOT NULL
```

### ATTENDANCE_CURRENT

```sql
student_id                VARCHAR(50) PRIMARY KEY
state_json                LONGTEXT    NOT NULL
```

`state_json` is serialized attendance state. Its internal properties are not separate SQL columns.

### ATTENDANCE_VISITS

```sql
visit_id                  VARCHAR(32) PRIMARY KEY
student_id                VARCHAR(50) NOT NULL
entry_transaction_id      VARCHAR(32) NOT NULL
exit_transaction_id       VARCHAR(32) NULL
time_in                   DATETIME(3) NOT NULL
time_out                  DATETIME(3) NULL
confirmed                 BOOLEAN     NOT NULL
INDEX student_visits (student_id, time_in)
```

These are campus entry/exit visits, not event completion sessions.

### EVENT_ROSTER_SYNC

```sql
event_id                  VARCHAR(50) NOT NULL
student_id                VARCHAR(50) NOT NULL
change_id                 CHAR(32)    NOT NULL
approved                  BOOLEAN     NOT NULL
cloud_change_id           VARCHAR(32) NOT NULL DEFAULT ''
cloud_update_time         VARCHAR(40) NOT NULL DEFAULT ''
pending                   BOOLEAN     NOT NULL DEFAULT TRUE
conflict                  BOOLEAN     NOT NULL DEFAULT FALSE
PRIMARY KEY (event_id, student_id)
```

### EVENT_COMPLETION_POLICIES

```sql
event_id                  VARCHAR(50) PRIMARY KEY
start_time                DATETIME(3) NOT NULL
end_time                  DATETIME(3) NOT NULL
minimum_minutes           INT         NOT NULL
late_minutes              INT         NOT NULL
early_minutes             INT         NOT NULL
revision                  BIGINT      NOT NULL
coverage_since            DATETIME(3) NOT NULL
```

### EVENT_COMPLETION_SCANS

```sql
transaction_id            VARCHAR(32) PRIMARY KEY
event_id                  VARCHAR(50) NOT NULL
student_id                VARCHAR(50) NOT NULL
event_log_id              BIGINT      NOT NULL
scan_time                 DATETIME(3) NOT NULL
status                    VARCHAR(50) NOT NULL
device_id                 VARCHAR(32) NOT NULL
device_sequence           BIGINT      NOT NULL
recovered                 BOOLEAN     NOT NULL
legacy                    BOOLEAN     NOT NULL
INDEX completion_event_student (event_id, student_id)
INDEX completion_log (event_log_id)
```

Keep `event_log_id` as BIGINT even though EVENT_ATTENDANCE.id is INT: that is the current implementation. The `completion_log` index is not unique.

### EVENT_COMPLETION_AUDIT

```sql
audit_id                  BIGINT       AUTO_INCREMENT PRIMARY KEY
event_id                  VARCHAR(50)  NOT NULL
student_id                VARCHAR(50)  NOT NULL
action                    VARCHAR(20)  NOT NULL
previous_status           VARCHAR(30)  NOT NULL
new_status                VARCHAR(30)  NOT NULL
reason                    VARCHAR(1000) NOT NULL
actor                     VARCHAR(100) NOT NULL
actor_role                VARCHAR(50)  NOT NULL
device_id                 VARCHAR(32)  NOT NULL
device_name               VARCHAR(100) NOT NULL
reviewed_at_utc           DATETIME(3)  NOT NULL
evidence_stamp            VARCHAR(64)  NOT NULL
evidence_json             LONGTEXT     NOT NULL
INDEX completion_reviews (event_id, student_id, audit_id)
```

### PIN_SECURITY_VERSIONS

```sql
student_id                VARCHAR(50) PRIMARY KEY
reset_token               CHAR(32)    NOT NULL
revision                  BIGINT      NOT NULL DEFAULT 0
```

### PIN_ATTEMPT_RECEIPTS

```sql
attempt_id                CHAR(32)    PRIMARY KEY
student_id                VARCHAR(50) NOT NULL
result_json               LONGTEXT    NOT NULL
```

### STUDENT_GRADUATION_AUDIT

```sql
audit_id                  CHAR(32)     PRIMARY KEY
student_id                VARCHAR(50)  NOT NULL
student_name              VARCHAR(100) NOT NULL
action                    VARCHAR(30)  NOT NULL
old_value                 VARCHAR(20)  NOT NULL
new_value                 VARCHAR(20)  NOT NULL
reason                    VARCHAR(1000) NOT NULL
actor                     VARCHAR(150) NOT NULL
changed_at_utc            DATETIME(6)  NOT NULL
INDEX ix_graduation_audit_student (student_id, changed_at_utc)
```

## 4. Relationship Changes

### Legend

Keep PK and UQ. Replace the current FK labels with `LR` (logical reference), or explicitly state that FK labels denote logical references rather than declared SQL foreign-key constraints. The application creates no database-enforced foreign keys in these schema definitions.

Recommended note beneath the diagram:

> PK indicates a primary key and UQ indicates a unique key. LR indicates a logical reference used by the application. Relationship lines describe application-level associations; the current schema does not declare SQL foreign-key constraints. Historical and legacy records may remain without a matching current student, event, or device record.

Do not use dashed lines to mean logical/non-enforced links while also using the same style to mean non-identifying relationships without explaining the distinction.

### Core Links

| Parent/reference | Child column | Normal application association |
| --- | --- | --- |
| COURSES.course_name | STUDENTS.course | A course can have many students; course is nullable and not database-enforced. |
| STUDENTS.student_id | EVENT_APPROVED_STUDENTS.student_id | A student can have zero or many approved event memberships. |
| EVENTS.event_id | EVENT_APPROVED_STUDENTS.event_id | An event can have zero or many approved students. |
| STUDENTS.student_id | EVENT_ATTENDANCE.student_id | A student can have zero or many event scan records. |
| EVENTS.event_id | EVENT_ATTENDANCE.event_id | An event can have zero or many event scan records. |
| STUDENTS.student_id | student_id in each mode-log table | A student can have zero or many logs; unauthorized or historical logs need not match a current profile. |
| STUDENTS.student_id | ALERTS.student_id | A student can have zero or many alerts; alerts can also be system/staff actions with no student. |

Ensure the three mode-log tables link to STUDENTS by `student_id`, rather than claiming they link to EVENTS. These tables do not contain `event_id`. A UID can be reassigned, so do not make the historical log's NFC UID an identifying student relationship.

### New Student Links

| Parent/reference | Child column | Normal application association |
| --- | --- | --- |
| STUDENTS.student_id | ATTENDANCE_CURRENT.student_id | Zero or one current-state row per student. |
| STUDENTS.student_id | ATTENDANCE_VISITS.student_id | Zero or many campus visits per student. |
| STUDENTS.student_id | PIN_SECURITY_VERSIONS.student_id | Zero or one PIN version row per student. |
| STUDENTS.student_id | PIN_ATTEMPT_RECEIPTS.student_id | Zero or many recorded PIN attempts per student. |
| STUDENTS.student_id | STUDENT_GRADUATION_AUDIT.student_id | Zero or many graduation audit records per student. |
| STUDENTS.student_id | EVENT_ROSTER_SYNC.student_id | Zero or many roster synchronization records per student. |
| STUDENTS.student_id | EVENT_COMPLETION_SCANS.student_id | Zero or many event completion scan records per student. |
| STUDENTS.student_id | EVENT_COMPLETION_AUDIT.student_id | Zero or many completion reviews per student. |

### New Event Links

| Parent/reference | Child column | Normal application association |
| --- | --- | --- |
| EVENTS.event_id | EVENT_ROSTER_SYNC.event_id | Zero or many student roster synchronization records per event. |
| EVENTS.event_id | EVENT_COMPLETION_POLICIES.event_id | Zero or one completion policy per event. |
| EVENTS.event_id | EVENT_COMPLETION_SCANS.event_id | Zero or many completion scan records per event. |
| EVENTS.event_id | EVENT_COMPLETION_AUDIT.event_id | Zero or many completion review records per event. |

EVENT_ROSTER_SYNC also tracks exclusions and conflicts, so do not require each row to have a matching EVENT_APPROVED_STUDENTS row. Both tables logically reference the event/student pair.

### Device and Transaction Links

| Parent/reference | Child column | Normal application association |
| --- | --- | --- |
| ATTENDANCE_DEVICES.device_id | ATTENDANCE_RECEIPTS.device_id | A device can produce zero or many transaction receipts. |
| ATTENDANCE_DEVICES.device_id | device_id in mode logs, EVENT_ATTENDANCE, ALERTS, EVENT_COMPLETION_SCANS, EVENT_COMPLETION_AUDIT | A device can be associated with many records; legacy records may not identify a known device. |
| ATTENDANCE_RECEIPTS.transaction_id | ATTENDANCE_DECISIONS.transaction_id | Zero or one decision per receipt; event-only receipts do not necessarily have a decision. |
| ATTENDANCE_RECEIPTS.transaction_id | ATTENDANCE_VISITS.entry_transaction_id / exit_transaction_id | Separate logical entry and exit references; exit is nullable. Legacy/recovered data needs the historical-record qualification. Neither visit reference is unique. |
| ATTENDANCE_RECEIPTS.transaction_id | EVENT_COMPLETION_SCANS.transaction_id | At most one completion scan per transaction ID; use the legacy/recovery qualification. |
| EVENT_ATTENDANCE.id | EVENT_COMPLETION_SCANS.event_log_id | Logical source-record reference; normally one evidence row is written with a new event scan, but event_log_id is not unique and historical raw logs may be removed. Do not label this as an enforced one-to-one constraint. |

Do not connect ATTENDANCE_VISITS transaction IDs directly to the three mode-log `id` columns. They are different identifiers, and the mode logs do not store `transaction_id`.

### Remove Unsupported Links

- Remove any EVENTS-to-mode-logs relationship presented as a stored key relationship: the mode-log tables have no event_id.
- Remove any mode-logs-to-ALERTS relationship presented as a stored key relationship: ALERTS has no log_id or transaction_id. Explain alert generation in prose or a process diagram instead.
- Do not add STAFF-to-EVENT_ATTENDANCE or STAFF-to-audit foreign keys. Audit actor fields store text, not STAFF.nfc_uid.
- Leave APP_SETTINGS independent. Its string keys/values are not foreign-key columns.
- Do not connect device_name_change_id to ATTENDANCE_DEVICES.device_id; they identify different things.

## 5. Replacement ERD Explanation

Replace the existing ERD explanation on pages 87-90 with the following text, adjusting figure references if the diagram is split into separately numbered figures:

The Entity-Relationship Diagram (Figure 16) presents the database structure implemented by the Risk-Based Two-Factor Student Verification and Event Attendance Management System. The local MySQL schema consists of 23 tables supporting student and staff records, event participation, verification logs, security alerts, campus attendance, synchronization, PIN security, event completion, and graduation clearance. The diagram identifies primary and unique keys and shows the logical references used by the application. These associations are maintained through application logic and transaction processing; the current schema does not declare SQL foreign-key constraints.

COURSES stores available academic course names, while STUDENTS stores student identifiers, personal and academic information, enrollment status, NFC credentials, PIN hashes and salts, and issued QR credentials. The QR credential is stored as TEXT. Student records also contain occupancy and PIN-lockout fields, identification photos, temporary-profile status, and graduation clearance information. STAFF stores authorized personnel records and their NFC, role, and PIN authentication data. APP_SETTINGS stores application configuration as key-value pairs.

EVENTS stores each event's identifier, name, date, verification mode, restriction status, and active status. EVENT_APPROVED_STUDENTS associates students with restricted events through a composite primary key. EVENT_ROSTER_SYNC separately records approval or exclusion changes, pending synchronization, and conflicts for each event-student pair. An exclusion record may remain in EVENT_ROSTER_SYNC even when no approved membership exists.

EVENT_ATTENDANCE records individual verified event scans and their status, verification mode, remarks, device attribution, and cloud synchronization status. The FAST_MODE_LOGS, STANDARD_MODE_LOGS, and HIGH_SECURITY_MODE_LOGS tables retain verification results, credential identifiers, error information, remarks, and processing-time measurements. ALERTS stores security and administrative audit messages. Recorded device names are retained alongside device identifiers to preserve the name associated with each historical operation.

ATTENDANCE_DEVICES tracks kiosk identity, availability, readiness, and synchronization sequence progress. ATTENDANCE_RECEIPTS identifies transactions already processed and prevents repeated processing of the same device sequence. ATTENDANCE_DECISIONS records the granted or denied result for applicable transactions. ATTENDANCE_CURRENT stores serialized current attendance state for each student, while ATTENDANCE_VISITS stores paired campus entry and exit transactions, timestamps, and confirmation status. Campus visits are distinct from event attendance records.

EVENT_COMPLETION_POLICIES stores event start and end times, required attendance duration, late and early allowances, policy revision, and evidence coverage time. EVENT_COMPLETION_SCANS preserves transaction and scan evidence used to evaluate participation duration. EVENT_COMPLETION_AUDIT records completion reviews, previous and new statuses, reasons, actor information, device attribution, and the evidence used during review. Completion is evaluated from policies and scan evidence rather than represented by an additional completion column in EVENT_ATTENDANCE.

PIN_SECURITY_VERSIONS records student PIN reset tokens and revisions used in security-state reconciliation. PIN_ATTEMPT_RECEIPTS stores processed attempt identifiers and their serialized results to support repeat-safe processing. STUDENT_GRADUATION_AUDIT preserves graduation-related changes with the student's recorded name, previous and new values, reason, actor, and timestamp.

Together, these entities support verification, attendance recording, controlled event participation, administrative review, and reliable transaction processing. Historical records may remain after a current profile or raw log is removed, and legacy records may lack a known device identifier. The ERD therefore distinguishes logical associations from database-enforced referential constraints and preserves the separation between campus visits, event scan records, synchronization metadata, and audit evidence.

## 6. Implementation Sources

- `NFC_System/DatabaseService.cs`: original 11 tables and QR credential migration.
- `NFC_System/DatabaseService.Attendance.cs`: five attendance tables; receipt/decision writes; campus visit pairing; event evidence insertion.
- `NFC_System/DatabaseService.DeviceAttribution.cs`: device fields and device-rename unique index.
- `NFC_System/DatabaseService.Graduation.cs`: graduation fields and STUDENT_GRADUATION_AUDIT.
- `NFC_System/DatabaseService.PinSecurity.cs`: PIN_SECURITY_VERSIONS and PIN_ATTEMPT_RECEIPTS.
- `NFC_System/DatabaseService.EventRosterSync.cs`: EVENT_ROSTER_SYNC.
- `NFC_System/DatabaseService.EventCompletionSchema.cs`: three event completion tables.

These sources define the expected schema after initialization and relevant migrations. Confirm the installed database's structure separately if the thesis needs to document a specific deployment rather than the current code.
