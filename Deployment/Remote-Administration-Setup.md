# Remote Administration Across Separate Networks

## Important Before First Use

The application code is implemented, but live cloud access is not enabled by a code build alone. A Firebase project owner must complete the authentication, account bootstrap and security-rule setup below. No live Firebase rules or records were changed during development.

Do not enable remote administration while Firestore has public/test-mode rules. App-side role checks do not replace deployed security rules. Update all devices before applying the restrictive rules: older builds without cloud authentication will no longer be able to upload/download.

Back up the authoritative MySQL database and existing configuration first. Do not expose MySQL port 3306 to the internet. The existing MySQL credential configuration is unchanged by this feature; production network/credential hardening is still required.

## Device Roles

| Device | Role | Operational storage |
| --- | --- | --- |
| Main device | Main Server | Authoritative MySQL database |
| Gate devices on the campus LAN | Connect to Main Server | The same main-server MySQL database |
| Administrators on other Wi-Fi/internet connections | Remote Administration (Cloud) | Cloud read model and encrypted pending-change queue; no local MySQL access |

Remote mode does not delete, merge or use a pre-existing local database. Before switching an old device's role, synchronize any existing attendance/PIN/device-audit queues to their original database. Those queues block a server change until resolved.

## 1. Get the Main Server ID

On the main device, complete the existing Main Server setup and sign in as Master Admin.

Open the main menu gear, then **Remote Administration**. Copy the **Main Server ID**. It is a 32-character identifier, not an IP address or password.

Keep this device's operational MySQL database as the source of truth. Each remote administrator must use this same Server ID.

## 2. Enable Cloud Accounts

In the existing Firebase project, enable **Authentication > Sign-in method > Email/Password**.

Create one dedicated server account and separate accounts for each remote administrator. Do not share the server password with remote administrators. The app does not provide public account registration or store cloud passwords/refresh tokens on disk.

Each account must verify its email. A signed-in account can use **Verify Email**, open the email link, then sign out and sign in again. Firebase's existing password-management tools can be used for forgotten passwords.

The Firebase Authentication user list shows each account's UID. The app also displays the signed-in account UID, including when approval has not yet been granted.

## 3. Bootstrap the Server Account

This is performed by the Firebase project owner in the Firebase console, not by a remote administrator.

Create this Firestore document, substituting the real Server ID and the dedicated server account's Firebase UID:

```text
remote_sites/{SERVER_ID}/members/{SERVER_ACCOUNT_UID}
```

| Field | Firestore type | Value |
| --- | --- | --- |
| `email` | string | Server account email |
| `name` | string | Main Server |
| `role` | string | server |
| `active` | boolean | true |

If the console requires a parent `remote_sites/{SERVER_ID}` document, create it with only an `updatedAt` string set to `pending`. Do not invent an encryption key. The app publishes its actual public key when the server connection starts.

Create this separate document to authorize the existing audit archive collections for the designated main-server account:

```text
cloud_configuration/primary
```

| Field | Firestore type | Value |
| --- | --- | --- |
| `siteId` | string | Main Server ID |
| `serverUid` | string | Dedicated server account UID |

The application cannot grant itself the `server` role or change this bootstrap document. Server identities are managed by the Firebase project owner.

## 4. Publish the Security Rules

Use the complete policy in `Deployment/firestore.remote-administration.rules` in the Firestore Rules editor, review it, then publish it.

This policy covers both the new site-scoped collections and the existing archive collections. It is intentionally restrictive. If this Firebase project also serves other applications, its owner must merge the policies carefully rather than replacing their access rules without review.

Do not retain an overlapping public rule such as `allow read, write: if true`. Firestore combines overlapping allow rules, so a broad public rule defeats the restrictions.

The policy permits approved remote administrators to read published profiles and submit immutable requests under their own UID. Only the approved server account can publish profiles, acknowledge requests and approve/revoke administrator accounts. Cloud archive deletion is denied.

## 5. Start the Main Server Cloud Connection

On the main device, as the on-site Master Admin, open **Remote Administration** and sign in using the dedicated server cloud account.

The app checks the approved server membership, verifies that the required SQL tables are transactional, and starts a background synchronization loop. It creates only the new coordination tables, `remote_profile_versions` and `remote_change_receipts`, plus the existing PIN-security schema if necessary.

The main app must remain open. Synchronization runs approximately every 15 seconds when the previous cycle has completed; this is not a guaranteed delivery-time bound. Internet loss, an unavailable database, a large initial publication or archive backlog can delay confirmation.

The worker also uploads unsynchronized gate/alert and event audit logs through the existing archive uploader. Closing just the Remote Administration window does not stop the worker. Use **Stop Server Cloud Connection** to stop it deliberately. Restarting the application requires cloud sign-in again.

Only the main server performs cloud archive transfers. Gate devices continue using the shared MySQL database. The old cloud download action now downloads audit logs only; it does not overwrite student/staff profiles, events, rosters or live entry/exit state.

## 6. Approve Remote Administrators

In the main server's Remote Administration window, enter the administrator's Firebase UID, email and name. Set **Access** to **Approved**, then select **Save Account Access**.

Use **Refresh Accounts** to view approved/revoked accounts. Select an administrator, change Access to **Revoked**, then save to remove cloud access. An account with no approval can sign in to obtain its UID, but cannot read profiles or submit changes.

Revocation prevents subsequent cloud requests; the server rechecks membership before processing queued changes. A transaction already being processed is not retroactively undone.

## 7. Configure Each Remote Device

During device setup, select **Remote Administration (Cloud)** and enter the Main Server ID. On an already configured device, stage the role change in its connection settings, then restart.

Sign in using that administrator's own verified, approved cloud account. Select **Synchronize** if account approval was granted while the account was already signed in.

The remote workspace supports:

- Creating Pending Enrollment profiles without a card, PIN or signing key.
- Editing student demographics, enrollment status, temporary-badge flag and photo.
- Assigning/replacing an NFC UID manually or by reading a connected NFC reader.
- Requesting a four-digit PIN reset.
- Requesting QR issuance/replacement and viewing/exporting the server-issued QR after confirmation.
- Tracking queued changes and server results.

Student ID changes, hard deletion, bulk import and graduation-clearance review remain on-site operations. Remote enrollment changes still obey the current graduation hold/clearance and activation rules. Mark a student Inactive when access should be disabled; do not treat deletion of an old local copy as a server deletion.

Choose an available NFC reader port before selecting **Read NFC Card**. The administrator's computer needs its own compatible reader to capture a physical card remotely. Manual UID entry does not prove physical card ownership.

Photos selected remotely must be under 250 KB. An existing larger server photo is omitted from the cloud read model and retained unchanged unless the administrator explicitly selects a replacement.

## Confirmation States

| State | Meaning |
| --- | --- |
| Queued | Encrypted request stored on this device; not yet uploaded |
| Pending | Cloud received the request; main server has not confirmed application |
| Applied | Main server committed the operation and published confirmation |
| Conflict | Current server profile differs from the revision being edited; refresh and review |
| Rejected | Validation, approval, credential issuance or another rule prevented the requested operation |

An upload is not the same as an applied profile change. An unavailable main server leaves requests pending. The workspace shows the most recent server publication and warns when there has been no recent publication.

Review a conflicting/rejected request, select the refreshed profile, make the intended changes again and submit a new request. PINs are not recovered from old requests for automatic resubmission.

## Credential and Data Safety

PIN reset requests use a fresh AES-256-GCM key, wrapped with the server's RSA-3072 public key using OAEP-SHA256. Request identity, owner, site, key ID and creation time are authenticated with the encrypted payload. Plaintext PINs and PIN hashes/salts are not placed in the new Firestore profile/read-model documents.

The server alone decrypts the command, creates the PIN hash, rotates the existing PIN reset token, validates NFC uniqueness, and issues a signed QR when needed. Its existing QR signing certificate/private key must be installed for the Windows account running the main server, and the current public verification certificate must already be trusted on gate devices. No signing private key is distributed to remote administrators.

The SQL transaction preserves server-owned occupancy, PIN failure counters and lock state, except that an explicit authorized PIN reset clears the PIN lock/counter. Existing identity, enrollment and clearance restrictions are checked against the live locked row, not just the cloud copy.

Applied, conflicting and rejected valid profile requests receive durable SQL receipts. Re-delivery of the same request cannot repeat a PIN reset, issue another QR, or duplicate its transactional audit record. A SQL commit followed by a cloud outage is retried using the stored receipt. Deletion of an on-site profile publishes a tombstone and does not allow an old request to resurrect it.

The profile read model still contains personal information and signed QR credentials. It is restricted to approved administrators, not public. The existing student archive uploader no longer includes PIN hashes/salts in newly uploaded student documents; maintain a separately protected local database backup for full disaster recovery.

## Local Files and Recovery

Per-Windows-user files are under:

```text
%LOCALAPPDATA%\NFC_System\RemoteAdministration\{SERVER_ID}\
```

The server's `server-key.dpapi` and administrator queue files are protected using Windows DPAPI. Queues are isolated by server and account, use cross-process file locking, and are retained after sign-out. A queue is not silently deleted when the network is unavailable.

Do not delete the server key to troubleshoot a connection. The server refuses to replace an established published key silently. Use the original Windows account and an appropriate Windows/profile backup for recovery. Key migration to a different Windows account is not implemented. Losing that Windows profile/key can make previously encrypted requests unrecoverable; those actions must be reviewed and submitted again through an explicitly re-provisioned server.

Stop cloud synchronization before changing the main device's role/server configuration. Only one main-server coordinator operates on the authoritative database at a time.

## Local Verification

All automated checks use fake transports, a demo-project Firestore emulator, or a disposable database on localhost port 23306. They do not touch the production MySQL database or live Firestore records.

```powershell
dotnet run --project NFC_System.OperationsTests -- --mysql
dotnet run --project NFC_System.NetworkTests -- --mysql
dotnet run --project NFC_System.Tests
firebase emulators:exec --only firestore --project demo-nfc-remote-tests --config Deployment/firebase.remote-admin.tests.json "node Deployment/test-remote-administration-rules.mjs"
```

The SQL commands require the test fixture, not a normal MySQL installation/database. On Windows with Java 25, the emulator may need a nonexistent `jdk.net.unixdomain.tmpdir` to force TCP loopback if Unix-domain pipe startup fails. This is a per-process test-runtime workaround, not a production application setting.

Live acceptance testing is still required after Firebase setup: submit a remote change using a separate internet connection, confirm it on the main database/gate, test an unavailable server, two competing edits, PIN replacement, old/new QR behavior, account revocation, and server restart/retry.
