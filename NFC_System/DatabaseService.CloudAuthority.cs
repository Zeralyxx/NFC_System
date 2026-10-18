using System;

namespace NFC_System;

public sealed partial class DatabaseService
{
    private static void RequireLegacyProfileRestorePermission()
    {
        if (ActiveConnection.Role != ServerConnectionRole.NotConfigured)
            throw new InvalidOperationException("Cloud snapshots cannot replace authoritative operational records. Use Remote Administration for approved student changes or a separately reviewed database restore.");
    }
}
