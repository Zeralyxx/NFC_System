using MySqlConnector;
using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace NFC_System;

internal static class RemoteAdministrationHost
{
    internal static readonly HttpClient Http=new(new HttpClientHandler { AllowAutoRedirect=false }) { Timeout=TimeSpan.FromSeconds(30) };
    private static CancellationTokenSource? _stop;
    private static Task? _worker;
    private static FirebaseCloudSession? _session;
    private static RemoteCloudStore? _cloud;
    private static readonly SemaphoreSlim Lifecycle=new(1,1);
    internal static string Status { get; private set; }="Stopped";
    internal static bool Running=>_worker is { IsCompleted:false };
    internal static CancellationToken StopToken=>_stop?.Token??new CancellationToken(true);
    internal static string StorageDirectory=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"NFC_System","RemoteAdministration");

    internal static FirebaseCloudSession NewSession()=>new(Http,DatabaseService.CloudApiKey);
    internal static RemoteCloudStore Store(FirebaseCloudSession session,string site)=>new(Http,session,DatabaseService.CloudProjectId,site);
    internal static void RequireServerOperator()
    {
        if(DatabaseService.ActiveConnection.Role!=ServerConnectionRole.MainServer || !AppSession.IsLoggedIn ||
            !AppSession.IsAdmin || AppSession.IsEventOrganizer || AppSession.CurrentStaffRoleLabel!="Master Admin")
            throw new UnauthorizedAccessException("Sign in as Master Admin on the main server to configure remote administration.");
    }

    internal static async Task StartAsync(FirebaseCloudSession session,CancellationToken token)
    {
        RequireServerOperator();
        await Lifecycle.WaitAsync(token);
        try
        {
            if(Running)throw new InvalidOperationException("Remote synchronization is already running.");
            string site=DatabaseService.ActiveConnection.ServerId;
            if(!Guid.TryParseExact(site,"N",out _))throw new InvalidOperationException("Initialize the main server identity first.");
            var cloud=Store(session,site);
            await cloud.RequireMemberAsync("server",token);
            using(var connection=new MySqlConnection(DatabaseService.ConnectionString))
            {await connection.OpenAsync(token);await DatabaseService.EnsureRemoteAdministrationSchemaAsync(connection);}
            token.ThrowIfCancellationRequested();
            RequireServerOperator();
            if(_session!=null&&!ReferenceEquals(_session,session))_session.Dispose();
            _stop?.Dispose();
            _session=session;_cloud=cloud;_stop=new CancellationTokenSource();
            _worker=Task.Run(()=>RunAsync(site,cloud,_stop.Token));
            DatabaseService.CloudAdministrationConnectionActive=true;
        }
        finally { Lifecycle.Release(); }
    }

    internal static async Task StopAsync()
    {
        await Lifecycle.WaitAsync();
        try
        {
            _stop?.Cancel();
            if(_worker!=null)try{await _worker;}catch(OperationCanceledException){}
            _session?.Dispose();_session=null;_cloud=null;_stop?.Dispose();_stop=null;_worker=null;Status="Stopped";
            DatabaseService.CloudAdministrationConnectionActive=false;
        }
        finally{Lifecycle.Release();}
    }

    internal static void CancelOnExit()=>_stop?.Cancel();

    internal static async Task ApproveAsync(RemoteMember member,CancellationToken token)
    {
        RequireServerOperator();
        if(member.Uid.Length is <1 or >128 || member.Uid.Contains('/') || string.IsNullOrWhiteSpace(member.Name)||member.Name.Length>100 ||
            !System.Net.Mail.MailAddress.TryCreate(member.Email,out _) || member.Role!="administrator")
            throw new InvalidOperationException("Enter the account UID, email address and administrator name.");
        var cloud=_cloud??throw new InvalidOperationException("Start the server cloud connection first.");
        await cloud.RequireMemberAsync("server",token);
        RequireServerOperator();
        await cloud.SaveMemberAsync(member,token);
    }

    internal static async Task<System.Collections.Generic.IReadOnlyList<RemoteMember>> MembersAsync(CancellationToken token)
    {
        RequireServerOperator();
        var cloud=_cloud??throw new InvalidOperationException("Start the server cloud connection first.");
        await cloud.RequireMemberAsync("server",token);
        return await cloud.MembersAsync(token);
    }

    internal static async Task<string> ArchiveTokenAsync(CancellationToken token)
    {
        if(DatabaseService.ActiveConnection.Role!=ServerConnectionRole.MainServer || _session==null || !Running)
            throw new InvalidOperationException("Cloud archive transfers run on the main server. Start its approved cloud connection in Remote Administration first.");
        return await _session.TokenAsync(token);
    }

    private static RSA LoadKey(string site)
    {
        string path=Path.Combine(StorageDirectory,site,"server-key.dpapi");
        byte[]? bytes=UserProtectedFile.Read<byte[]>(path);
        var rsa=RSA.Create(3072);
        try
        {
            if(bytes==null)
            {
                bytes=rsa.ExportPkcs8PrivateKey();
                try{UserProtectedFile.Save(path,bytes,overwrite:false);}
                catch(IOException)when(File.Exists(path))
                {CryptographicOperations.ZeroMemory(bytes);bytes=UserProtectedFile.Read<byte[]>(path)??throw new InvalidDataException("Server key is empty.");}
            }
            rsa.ImportPkcs8PrivateKey(bytes,out _);
            return rsa;
        }
        catch{rsa.Dispose();throw;}
        finally{if(bytes!=null)CryptographicOperations.ZeroMemory(bytes);}
    }

    private static async Task RunAsync(string site,RemoteCloudStore cloud,CancellationToken token)
    {
        RSA rsa;
        try{rsa=LoadKey(site);}
        catch(Exception ex){Status="Server encryption key unavailable: "+ex.Message;return;}
        using var keyLifetime=rsa;
        while(!token.IsCancellationRequested)
        {
            try
            {
                await cloud.RequireMemberAsync("server",token);
                using var connection=new MySqlConnection(DatabaseService.ConnectionString);
                await connection.OpenAsync(token);
                using(var identity=new MySqlCommand("SELECT setting_value FROM app_settings WHERE setting_key='lan_server_id'",connection))
                    if(!string.Equals(Convert.ToString(await identity.ExecuteScalarAsync(token)),site,StringComparison.Ordinal))
                        throw new InvalidOperationException("The authoritative database identity changed. Stop synchronization and review the server configuration.");
                // One coordinator per authoritative database avoids out-of-order cloud publication by two app processes.
                using var gate=new MySqlCommand("SELECT GET_LOCK(@name,0)",connection);
                gate.Parameters.AddWithValue("@name","nfc-remote-"+site);
                if(Convert.ToInt32(await gate.ExecuteScalarAsync(token))!=1) throw new InvalidOperationException("Another main-server process is already synchronizing.");
                try
                {
                    await cloud.PublishKeyAsync(RemoteChangeCrypto.PublicKey(rsa),token);
                    await DatabaseService.RefreshRemoteProfileVersionsAsync(connection,token);
                    foreach(var envelope in await cloud.PendingAsync(token))
                    {
                        try
                        {
                            var member=await cloud.MemberAsync(envelope.OwnerUid,token);
                            if(member is not { Active:true,Role:"administrator" })
                            {await cloud.PublishResultAsync(new(envelope.Id,"Rejected","This administrator is no longer approved."),token);continue;}
                            var change=RemoteChangeCrypto.Decrypt(envelope,rsa,site);
                            await DatabaseService.ApplyRemoteChangeAsync(connection,envelope,change,member,
                                id=>new QrCredentialService().IssueForRemoteServer(id),DeviceIdentity.CaptureCurrent(),token);
                        }
                        catch(Exception ex) when(ex is CryptographicException or System.Text.Json.JsonException or InvalidDataException or ArgumentException or FormatException or InvalidOperationException)
                        {await cloud.PublishResultAsync(new(envelope.Id,"Rejected","Invalid encrypted request or obsolete server key. Refresh, review and submit again."),token);}
                    }
                    foreach(var profile in await DatabaseService.UnpublishedRemoteProfilesAsync(connection,token))
                    {await cloud.PublishProfileAsync(profile,token);await DatabaseService.MarkRemoteProfilePublishedAsync(connection,profile,token);}
                    var receipts=await DatabaseService.UnpublishedRemoteResultsAsync(connection,token);
                    if(System.Linq.Enumerable.Any(receipts,result=>result.Status=="Applied"))await new DatabaseService().UpdateShadowCacheAsync();
                    foreach(var result in receipts)
                    {await cloud.PublishResultAsync(result,token);await DatabaseService.MarkRemoteResultPublishedAsync(connection,result.Id,token);}
                    Status="Connected | Last synchronization: "+DateTime.Now.ToString("g");
                    try
                    {
                        var database=new DatabaseService();
                        await database.PushLogsToCloudAsync();
                        await database.PushEventAttendanceToCloudAsync();
                    }
                    catch(Exception ex){Status+=" | Audit archive upload pending: "+ex.Message;}
                }
                finally
                {
                    using var release=new MySqlCommand("SELECT RELEASE_LOCK(@name)",connection);
                    release.Parameters.AddWithValue("@name","nfc-remote-"+site);
                    await release.ExecuteScalarAsync(CancellationToken.None);
                }
            }
            catch(OperationCanceledException) when(token.IsCancellationRequested){break;}
            catch(Exception ex){Status="Synchronization paused: "+ex.Message;}
            try{await Task.Delay(TimeSpan.FromSeconds(15),token);}catch(OperationCanceledException){break;}
        }
    }
}

internal sealed class CloudArchiveAuthorizationHandler : DelegatingHandler
{
    internal CloudArchiveAuthorizationHandler():base(new HttpClientHandler { AllowAutoRedirect=false }){}
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)
    {
        if(request.RequestUri?.Scheme!="https" || request.RequestUri.Host!="firestore.googleapis.com" ||
            !request.RequestUri.AbsolutePath.StartsWith("/v1/projects/"+DatabaseService.CloudProjectId+"/",StringComparison.Ordinal))
            throw new UnauthorizedAccessException("Unexpected cloud destination.");
        request.Headers.Authorization=new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",await RemoteAdministrationHost.ArchiveTokenAsync(cancellationToken));
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken,RemoteAdministrationHost.StopToken);
        return await base.SendAsync(request,linked.Token);
    }
}
