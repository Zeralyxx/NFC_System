namespace NFC_System;

public sealed partial class DatabaseService
{
    public static string ConnectionString => "Server=127.0.0.1;Port=23306;Database=nfc_operations_tests;User ID=root;Password=attendance-test-only;ConnectionTimeout=2";
    public static string ToStorageValue(VerificationMode mode) => mode.ToString();
    private static string Value(object? value) => value == null || value == DBNull.Value ? "" : Convert.ToString(value) ?? "";
    private static void ValidateIssuedQr(StudentRecord student) => throw new NotSupportedException("Profile credential edits are tested in the verification/UI suites.");
    public Task<StaffDetails> GetStaffDetailsAsync(string uid) => Task.FromResult(new StaffDetails());
}
public sealed class StaffDetails { public string? Role { get; set; } public string? FullName { get; set; } public string? PinHash { get; set; } public string? PinSalt { get; set; } }
public static class DatabaseMonitor { public static bool IsOnline { get; set; } = true; }
public sealed record DeviceAttribution(string DeviceId, string DeviceName);
public static class DeviceIdentity { public static DeviceAttribution CaptureCurrent() => new("00000000000000000000000000000001", "Operations test device"); }
public static class OfflineCacheService { public static void UpdateCachedCredential(string id, StudentRecord student, bool pinReplaced = false) { } }
