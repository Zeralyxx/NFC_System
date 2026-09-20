using System;

namespace NFC_System;

public static class AppSession
{
    public static bool IsLoggedIn { get; set; }
    public static bool IsAdmin { get; set; }
    public static bool IsEventOrganizer { get; set; }
    public static string CurrentStaffName { get; set; } = "";
    public static string CurrentStaffRoleLabel { get; set; } = "";
    public static bool IsKioskRunning { get; set; }

    public static bool CanIssueQrCredentials => IsLoggedIn && IsAdmin && !IsEventOrganizer &&
        CurrentStaffRoleLabel is "Admin" or "Master Admin";

    public const string QrIssuanceDeniedMessage = "Sign in as an Administrator or Master Admin to issue or replace QR credentials.";

    public static void RequireQrIssuancePermission()
    {
        if (!CanIssueQrCredentials) throw new UnauthorizedAccessException(QrIssuanceDeniedMessage);
    }
}
