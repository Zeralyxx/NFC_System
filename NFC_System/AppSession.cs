using System;

namespace NFC_System;

public static class AppSession
{
    private static readonly object SessionLock = new();
    private static bool _isLoggedIn;
    private static long _loginVersion;
    public static bool IsLoggedIn
    {
        get { lock (SessionLock) return _isLoggedIn; }
        set { lock (SessionLock) { _isLoggedIn = value; _loginVersion++; } }
    }
    public static long LoginVersion { get { lock (SessionLock) return _loginVersion; } }
    public static bool CanAcceptStaffLogin => !IsLoggedIn;
    public static bool IsAdmin { get; set; }
    public static bool IsEventOrganizer { get; set; }
    public static string CurrentStaffName { get; set; } = "";
    public static string CurrentStaffRoleLabel { get; set; } = "";
    public static bool IsKioskRunning { get; set; }

    internal static bool TrySignIn(string? role, string? fullName, long expectedVersion)
    {
        lock (SessionLock)
        {
            // A delayed lookup or another window's tap must never replace the active operator.
            if (_isLoggedIn || _loginVersion != expectedVersion) return false;
            if (role is not ("Administrator" or "Master Administrator" or "Event Organizer" or "Security Personnel")) return false;
            IsAdmin = role is "Administrator" or "Master Administrator";
            IsEventOrganizer = role == "Event Organizer";
            CurrentStaffRoleLabel = role switch
            {
                "Master Administrator" => "Master Admin",
                "Administrator" => "Admin",
                "Event Organizer" => "Event Organizer",
                _ => "Personnel"
            };
            CurrentStaffName = fullName ?? CurrentStaffRoleLabel;
            _isLoggedIn = true;
            _loginVersion++;
            return true;
        }
    }

    public static bool CanIssueQrCredentials => IsLoggedIn && IsAdmin && !IsEventOrganizer &&
        CurrentStaffRoleLabel is "Admin" or "Master Admin";

    public const string QrIssuanceDeniedMessage = "Sign in as an Administrator or Master Admin to issue or replace QR credentials.";

    public static void RequireQrIssuancePermission()
    {
        if (!CanIssueQrCredentials) throw new UnauthorizedAccessException(QrIssuanceDeniedMessage);
    }
}
