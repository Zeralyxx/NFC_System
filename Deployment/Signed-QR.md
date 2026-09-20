# Signed QR deployment and acceptance tests

## Behavior

Credentials use `NFC1.<certificate-thumbprint>.<base64url-json>.<base64url-signature>`.
The entire prefix, key ID and payload are signed with ECDSA P-256 / SHA-256 using
the .NET cryptography APIs and fixed-width IEEE P1363 signatures. Payloads contain
issuer `NFC_SYSTEM`, version `1`, student ID, a random credential ID and UTC issue time.
These are signed, not encrypted. An exact copy remains valid. The issue date does
not impose expiry; credential replacement and trusted-key removal control validity.

- Registration signs once on Save; the saved QR can be viewed and exported as PNG.
- Issuance/replacement requires a logged-in Admin or Master Admin, consistent with
  registration access. Personnel, organizers, unknown roles and logged-out sessions
  are rejected before key access. The service and database write paths enforce this;
  public-key verification does not require an administrator role.
- Student Management preserves QR on ordinary edits. Select Replace QR credential
  to reissue. Changing student ID or NFC card automatically reissues it. Legacy
  unsigned records default to replacement when opened for editing.
- High-Security entry: NFC -> PIN -> signed QR matching the current student record.
- Forgotten-card fallback: signed QR -> PIN, including when High Security is selected.
  This is the existing fallback exception; it is not proof of physical NFC possession.
- Fast, Standard and NFC-only exit requirements are unchanged.
- Signature verification requires only public keys and works without a database.
  Access still requires an active database/cache record and the existing access rules.
- Online lookup returning no student denies access; it does not resurrect a deleted
  student from stale cache. Connection failure can use offline cache.
- Replacement is checked against the exact stored credential with ordinal matching.
  The current credential is rechecked before final QR/fallback grant. Reissue updates
  this machine's credential cache without overwriting pending entry or PIN progress.
- An offline kiosk with an older cache can still accept an older credential until
  it receives updated student data. Public-key removal likewise requires deployment
  to each offline kiosk. No immediate remote revocation is claimed.

## Provisioning

Do this on the dedicated enrollment Windows account, not a public kiosk account:

```powershell
.\Deployment\New-QrSigningCertificate.ps1 -OutputDirectory C:\NfcQrProvisioning\initial
```

The script creates a non-exportable private signing key in CurrentUser/My and emits
two JSON files containing public certificates. It does not modify the application's
configuration or student database. On the enrollment machine, an administrator
installs `issuer-trust.json` as `%ProgramData%\NFC_System\QrKeys\trust.json`.
On kiosks, install `kiosk-trust.json` at that path. Do not install a private signing
certificate on kiosks. Both use the same application, so Windows account separation
is required if enrollment and kiosk operation share a physical computer.

Protect the QrKeys directory and trust.json against writes by kiosk users: SYSTEM
and Administrators need full access, the runtime account needs read access only.
Protect the parent directory against replacement as well. Trust-list integrity is
part of the security boundary. The app never automatically creates a signing key
or trusts a key supplied by a QR. Missing/broken trust configuration rejects QR;
missing signing configuration blocks issuance and leaves the database unchanged.

For rotation, use a fresh output directory and pass `-ExistingTrustFile` with the
current trust.json. Distribute the expanded public trust list to all kiosks before
issuing with the new key. Retain old public keys until old credentials are reissued.
Remove a compromised key from all trust lists. Losing the non-exportable enrollment
key requires a new key and reissuance; retaining its public certificate lets existing
credentials verify during a planned replacement. Certificate lifetime limits new
issuance; existing QR verification uses explicit trust membership.

References: [ECDsa.SignData](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.ecdsa.signdata?view=net-8.0),
[New-SelfSignedCertificate](https://learn.microsoft.com/en-us/powershell/module/pki/new-selfsignedcertificate).

## Migration

1. Provision keys and test an enrollment and a public-only kiosk installation.
2. Start the app against a backed-up test database first. EnsureSchemaAsync widens
   students.qr_credential from VARCHAR to TEXT; migration errors are not swallowed.
3. Open each existing student in Student Management, replace the QR, save through
   the existing administrator authorization flow, then export/print the new QR.
4. Push updated student records to cloud using the existing authorized sync flow;
   refresh each kiosk's student cache before testing offline acceptance.
5. Unsigned legacy QRs are rejected immediately by this application version. Coordinate
   deployment and reprinting; Fast/Standard NFC operation remains available.

No production credentials, database contents, keys or installed trust lists are
automatically provisioned by a source-code build.

## Automated tests

```powershell
dotnet run --project NFC_System.Tests/NFC_System.Tests.csproj
dotnet build NFC_System.slnx -p:Platform=x64
dotnet build NFC_System.slnx -p:Platform=x64 -c Release
dotnet run --project NFC_System.WinUiSmoke/NFC_System.WinUiSmoke.csproj -p:Platform=x64
```

The standalone test runner links the production signing, verification, model and
PIN code. Database/monitor/cache boundaries are replaced by in-memory test doubles;
tests never connect to the production database or touch its offline cache. The
runner exits nonzero on failure. Real persistence, devices and WinUI need the
acceptance checks below.

The Windows-only WinUiSmoke app uses the production QR display helper and exits
after checking live WinUI startup, XamlRoot attachment, dialog close/reopen sequencing,
QR bitmap creation, preservation of unsaved editor fields and file-picker owner
initialization. It does not connect to the database or provision signing keys. Its
result is written beside its executable as `winui-smoke-result.txt`, with a nonzero
process exit code on failure. Interactive file selection and physical camera/reader
operation remain manual acceptance checks.

Verification on 2026-09-18: 59 core/authorization checks passed; x64 Debug and Release
application builds passed with existing warnings; the live WinUI smoke test passed.
The core test restore reported that NuGet vulnerability data was unavailable from
the current network; package restore/build and the tests themselves completed.

## Hardware and persistence acceptance

Use students A and B, an active event with a restricted roster, enrollment and kiosk
accounts, and a disposable database. Check each denial writes one appropriate record
with the identified student (or unknown only when identity cannot be authenticated).
Do not record the full QR credential in access logs.

| Scenario | Expected result |
| --- | --- |
| Admin / Master Admin issuance | Allowed with a configured signing key; existing reissue authorization still applies |
| Personnel / organizer / logged-out issuance, including an already-open editor | Denied before signing or saving; verification remains available |
| Student editor -> QR preview -> editor; editor -> admin tap -> new QR | Dialogs close before the next opens; no overlapping-dialog exception; unsaved fields preserved |
| Register, export PNG, scan from print and phone | Saved credential decodes exactly; correct student; no truncation |
| Edit name/course only | QR stays identical and remains usable |
| Reissue / replace NFC / rename ID | New signed QR; old QR denied against updated DB/cache; export survives restart |
| Legacy plain ID, edited payload/signature, unknown key, malformed QR | Denied without reaching PIN in fallback |
| High Security: A NFC, correct PIN, A QR | Granted; expected timing and student in logs |
| High Security: A NFC, correct PIN, B QR | Denied; no state/attendance change |
| Fallback: A QR, wrong PIN three separate attempts | Denied; lock persists across restart and reconnect |
| Fallback in Fast/Standard/High Security, including exit | PIN always required; appropriate rules still apply |
| Copy A's QR and use all valid required credentials | Accepted, documenting the static-copy limitation |
| Inactive/deleted/replaced student; already-inside repeated entry | Denied according to current DB/cache rules |
| Restricted event, duplicate entry, event exit | Roster and entry rules maintained with signed payloads |
| Disconnect database, restart app, then verify | Public keys + persisted cache work; no record means denial |
| Offline grant/denial then authorized reconnect sync | Correct identity, fallback remarks, mode and timestamp; logs not lost/duplicated |
| Cloud push then pull to second test installation | Signed credential unchanged and verifiable |
| Rotate keys; retain both then remove old | Both verify during overlap; removed key denied |
| Missing key config/private key | QR/issuance fails closed; no unsigned QR or successful registration |
| Rapid scans of A then B while QR verification is awaiting database | One in-flight QR; no mixed identity or duplicate grant |
| Existing 10-second result replacement, staff login, reader/database indicators | Earlier kiosk behaviors still work |

Offline revocation test: disconnect kiosk B before reissue on A; B may accept its
cached old credential. After cache synchronization it must reject the old and accept
the new. This is an expected offline limitation, not an anti-copy guarantee.
