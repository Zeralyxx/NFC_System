using System;
using System.Linq;

namespace NFC_System;

public sealed record DeviceAttribution(string DeviceId, string DeviceName);

public sealed record DeviceRenameResult(DeviceAttribution Identity, bool Changed, bool AuditPending);

public static class DeviceIdentity
{
    public const string UnknownName = "Unknown/Legacy";
    public const int MaximumNameLength = 100;

    public static DeviceAttribution CaptureCurrent()
    {
        var snapshot = OfflineCacheService.AttendanceQueue.Snapshot();
        return new(snapshot.DeviceId, DisplayName(snapshot.DeviceName));
    }

    public static string DisplayName(string? name) => string.IsNullOrWhiteSpace(name) ? UnknownName : name;

    public static string ValidateName(string? name)
    {
        if (name == null || name.Any(char.IsControl))
            throw new ArgumentException("Enter a device name without control characters.", nameof(name));
        string value = name.Trim();
        if (value.Length == 0 || value.Length > MaximumNameLength)
            throw new ArgumentException($"Enter a device name between 1 and {MaximumNameLength} characters.", nameof(name));
        return value;
    }

    public static bool CanRename => AppSession.IsLoggedIn && AppSession.IsAdmin && !AppSession.IsEventOrganizer &&
        AppSession.CurrentStaffRoleLabel is "Admin" or "Master Admin";

    internal static void RequireRenamePermission(long loginVersion)
    {
        if (!CanRename || AppSession.LoginVersion != loginVersion)
            throw new UnauthorizedAccessException("Sign in as an Administrator or Master Admin to rename this device.");
    }
}
