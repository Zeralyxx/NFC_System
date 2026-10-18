# Automatic Server Discovery

## Rollout

1. Back up the main MySQL database and each device's offline files. Update the main server first, then both connecting devices, using the same app version.
2. On the main device, choose **Main Server** in the first connection dialog. This role uses its own database at `127.0.0.1`. Complete its existing Master Administrator setup, if needed, and leave the main app open.
3. On each other device, choose **Connect to Main Server**. This immediately searches the local network. A single result fills the address automatically; multiple results require an explicit selection. Confirm the displayed computer/address and click **Connect**. The app verifies the selected database identity and completed staff setup before saving.
4. On first use after this upgrade, an existing `db_config.txt` address is retained but the operator must explicitly choose a role. A local database is never automatically declared authoritative just because it exists.
5. Once connected, all devices use the same `nfc_system` MySQL database. Existing databases on the other computers are neither deleted nor automatically copied into the server. Resolve any historical local-only records separately before retiring them.

## Main-menu settings

Open the dashboard gear > **System Settings > Network Configuration**. The role selector, manual IP field and **Automatically Detect Server** button are also available in Application Settings. Only a current Admin/Master Admin session may save changes. Saved changes apply after restarting; they do not redirect an active scan, policy save or recovery operation to another database.

Detection is available for the client role. A known client searches for its saved server identity rather than silently switching to an unrelated main server. Manual IP entry remains available. If the saved IP stops working at startup, the connection dialog automatically searches again; confirm the rediscovered server to update the address.

Network settings are stored per device in `%ProgramData%\NFC_System\Configuration\network_config.json`. The old installation-folder settings are read for migration without erasing or overwriting the old files. The server's stable identity is stored under `app_settings.lan_server_id` in MySQL; retain it in database backups. Do not run two independent copies of the main database as competing main servers or copy a device's cache/configuration onto another active device.

## Pending records and offline operation

Changing to a different server is blocked while local attendance, PIN or device-audit records await synchronization. Reconnect to the previous database and synchronize them first. The app does not delete queues to make a server change succeed.

A different server requires a fresh student/event/roster/PIN cache before scanning. The refresh-required marker survives restart; old offline credentials cannot be used against a newly selected server while refresh is incomplete. A change of IP for the same verified server identity can retain its existing pending work. Ordinary outages continue using the existing offline workflow; there is no fallback to another local MySQL database.

## Network requirements and limits

- All devices must be on a trusted local network with IPv4 broadcast communication. Ethernet and Wi-Fi adapters are supported. Guest Wi-Fi isolation or separate routed subnets can prevent detection; manual IP entry remains available if MySQL is reachable.
- The main app must remain open for discovery. The MySQL service can continue serving already configured devices when the app is closed, but new discovery requests cannot find it until the app is reopened.
- Discovery uses UDP port `43852`; database access remains TCP `3306`. Allow the relevant app/service only on the intended private network, under your administrator's policy. This feature does not change firewall rules, database listeners, credentials or account permissions, and does not install a Windows service. Finding a computer does not grant MySQL access.
- Replies contain only a product/version marker, request nonce, server identifier, computer name and fixed database port. No student data, PINs or database passwords are broadcast. The received packet's source address is used, not a payload-provided address.
- Database identity checks help avoid accidental connections to another installation. Discovery is not cryptographic server authentication and must not be treated as a security boundary against a malicious network. Existing database credentials remain unchanged; review them before deployment beyond the trusted test LAN. Do not expose MySQL to the public internet.
- Cloud push/pull behavior is separate. When all devices point to the shared server, a cloud pull from a client applies to the shared database too. It is not a restore into the client's unused local database. Server-only cloud publishing is still an operator policy, not a device-role authorization restriction.

## Verification

```powershell
dotnet run --project NFC_System.NetworkTests/NFC_System.NetworkTests.csproj --no-restore
dotnet run --project NFC_System.WinUiSmoke/NFC_System.WinUiSmoke.csproj -p:Platform=x64 --no-restore
```

The network suite uses loopback-only sockets, isolated configuration files and mock local queue boundaries. Optional SQL checks refuse any database other than disposable `127.0.0.1:23306/nfc_network_tests` (test password `attendance-test-only`):

```powershell
dotnet run --project NFC_System.NetworkTests/NFC_System.NetworkTests.csproj --no-restore -- --mysql
```

Before rollout, test the actual three computers: main role only on the main device, both clients discovering it, multiple-candidate selection, wrong/missing database identity, blocked MySQL access, cancelled/failed detection, restart persistence, pending-work protection, fresh-cache requirements and reconnect after an address change. Windows firewall/router behavior and real cross-device discovery require that LAN acceptance test; the automated tests do not prove those deployment conditions.
