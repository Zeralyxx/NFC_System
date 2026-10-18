using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace NFC_System;

// No production database, cloud identity, stored profile or hardware is used by layout smoke tests.
internal static class DatabaseService
{
    internal static ServerConnectionConfiguration ActiveConnection { get; } = new(ServerConnectionRole.RemoteAdministration,"","12345678901234567890123456789012");
}
internal static class HardwareService
{
    internal static event Action<string>? OnUidScanned;
    internal static bool Connect(string port)=>false;
    internal static void Disconnect(){}
    internal static void Simulate(string uid)=>OnUidScanned?.Invoke(uid);
}
internal static class RemoteAdministrationHost
{
    internal static bool Running=>false;
    internal static string Status=>"Layout test only";
    internal static string StorageDirectory=>Path.GetTempPath();
    internal static void RequireServerOperator()=>throw new NotSupportedException("No server access in smoke tests");
    internal static FirebaseCloudSession NewSession()=>throw new NotSupportedException("No cloud access in smoke tests");
    internal static RemoteCloudStore Store(FirebaseCloudSession session,string site)=>throw new NotSupportedException("No cloud access in smoke tests");
    internal static Task StartAsync(FirebaseCloudSession session,CancellationToken token)=>throw new NotSupportedException("No cloud access in smoke tests");
    internal static Task StopAsync()=>Task.CompletedTask;
    internal static Task ApproveAsync(RemoteMember member,CancellationToken token)=>throw new NotSupportedException("No cloud access in smoke tests");
    internal static Task<System.Collections.Generic.IReadOnlyList<RemoteMember>> MembersAsync(CancellationToken token)=>throw new NotSupportedException("No cloud access in smoke tests");
}
