using System;

namespace NFC_System;

public sealed partial class QrCredentialService
{
    internal string IssueForRemoteServer(string studentId)
    {
        if (DatabaseService.ActiveConnection.Role != ServerConnectionRole.MainServer)
            throw new UnauthorizedAccessException("Only the main server can issue remote QR credentials.");
        return IssueCore(studentId, true);
    }
}
