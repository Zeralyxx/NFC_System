# Student Profile Sync

This uses the existing local student-management screens. There is no separate remote workspace. LAN gate devices still connect directly to the main MySQL database. Administration devices in other buildings use their own MySQL database and upload student-profile changes only.

## One-time cloud setup

1. In the existing Firebase project, enable Authentication > Email/Password. Create one account for the main server and a separate account for each remote administrator. Copy their Firebase user UIDs. Do not share accounts.
2. On the main device, use the Main Server role and restart. Sign in normally, then open main-menu Settings > Student Profile Sync. Copy its Main Server ID.
3. In Firestore, create `studentSyncServers/{Main Server ID}`. Add `ownerUid` (string: main-server account UID) and `adminUids` (array of approved administrator UIDs). Only a trusted Firebase project administrator may edit this approval document. Removing a UID revokes its next upload and unprocessed changes.
4. Merge the match block from `firestore.student-sync.rules` into the project's rules. Keep appropriate rules for the existing audit/archive collections. **Do not blindly replace all production rules with this file**, because this isolated file denies other collections. Remove any broad wildcard rule that also permits access to `studentSyncServers`; Firestore allow rules are additive, so a permissive catch-all would defeat these restrictions. Never grant public read access: profile snapshots contain sensitive data and salted PIN hashes.
5. Connect the main-server cloud account in Settings, then select Check Now to publish its student profiles. Keep the app open. It checks every three minutes; Check Now handles urgent updates. The Windows Credential Locker remembers the cloud refresh token for that Windows account. Passwords are not saved in configuration files.

## Administration device

1. Use Administration Device (Local) in Network Configuration, save, and restart. The device keeps its existing local MySQL database and NFC/PIN staff login. It does not advertise itself as a LAN main server. If newly installed, complete its normal local admin setup first.
2. In Student Profile Sync, enter the main device's Server ID and the approved administrator's cloud email/password. Select Connect Account.
3. Download Student Profiles **before editing existing students**. For the first download into a database with existing students, explicitly select Replace local changes when downloading after reviewing/backing up local edits. Existing local-only students are left alone, not silently submitted as new students.
4. Add or edit students and reset PINs through the existing screens. Use Upload Student Changes, or the existing Upload action. Only changed downloaded profiles and newly registered/imported profiles are submitted. Untouched existing local records are skipped. Edits to old local-only students must first be reconciled with the main database; they are not automatically treated as new.
5. Check Now reports Applied, Pending, Rejected, or Needs Review. An upload is not effective on campus until the main server confirms Applied. If the app/network stops after upload, the durable request ID allows a safe retry. To refresh the admin's view, download profiles after checking upload outcomes.
6. If a profile needs review, inspect the main-server information, select Replace local changes when downloading, download, then redo the desired change. This intentionally discards that local edit. A still-pending upload cannot be discarded because the main server may yet apply it. The server must finish it first.

## Boundaries

- Profile fields: name, email, course/year/section, enrollment status, NFC assignment, signed QR, photo, temporary flag, and explicit student PIN reset (salt/hash only).
- The receiver validates enrollment and graduation eligibility, unique NFC assignment, trusted QR signature, current profile fingerprint, and uploader approval. Every outcome has a durable SQL receipt; applied changes are audited. An interrupted acknowledgement does not apply the same reset twice.
- Attendance, events, rosters, staff accounts/passwords, entry/exit state, scan timestamps, graduation clearance decisions, and current failure/lock counters are not imported through this channel. A PIN reset explicitly clears that student's main-server PIN lock and rotates the PIN security reset token.
- Student ID changes, deletions, and purges remain main-server actions. Administration-device changes to unrelated local settings or staff are not synchronized.
- QR issuance still needs the existing enrollment certificate on each issuing Windows account, and its public certificate must be trusted by the main server. This feature does not bypass or replace QR trust setup.
- Remote photo uploads are limited to 500 KB. The channel is not for arbitrary database backup/restore.
- Cloud authorization and production end-to-end tests require your Firebase configuration above. No production rules or accounts are changed automatically.

## Acceptance check with two devices

1. Download a known student, change only the name, upload, and Check Now on the main device. Confirm the name changes but entry state and PIN lock remain unchanged.
2. Reset that student's PIN remotely. Confirm the main device accepts the new PIN only after Applied and the old PIN fails. Retry the upload; confirm there is only one apply audit/receipt.
3. Edit the same profile on the main device before applying a remote edit. Confirm Needs Review and no overwrite.
4. Remove an admin UID and verify new uploads are denied and queued unprocessed changes are rejected.
5. Interrupt the network during upload/acknowledgement, reconnect, and retry. Confirm Pending progresses to Applied without duplicate changes.
6. Confirm an Administration Device's existing Upload does not change cloud staff/events/attendance and that normal LAN clients continue using the main MySQL database.
