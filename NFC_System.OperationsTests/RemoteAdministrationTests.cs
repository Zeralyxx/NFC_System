using MySqlConnector;
using NFC_System;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

internal static class RemoteAdministrationTests
{
    private static void Assert(bool condition){if(!condition)throw new Exception("Remote administration assertion failed");}
    private static async Task Reject(Func<Task> action)
    {
        try{await action();}
        catch(Exception ex)when(ex is InvalidOperationException or InvalidDataException or UnauthorizedAccessException or CryptographicException or ArgumentException or FormatException){return;}
        throw new Exception("Expected rejection");
    }
    private static RemoteProfileChange Change(string id="REMOTE-1")=>new(id,0,true,"Remote Student","s@example.test","BSIT","1","A","Active","AABBCCDD",false,"","4682",true,"Controlled remote test");
    private static string Site=>"12345678901234567890123456789012";
    private static RemoteMember Member=>new("remote-user","admin@example.test","Approved Administrator","administrator",true);

    internal static async Task RunAsync(Func<string,Func<Task>,Task> test)
    {
        await test("Remote encryption round-trips without publishing PIN or plaintext profile",()=>
        {
            using var rsa=RSA.Create(3072);var change=Change();var envelope=RemoteChangeCrypto.Encrypt(change,Site,Member.Uid,RemoteChangeCrypto.PublicKey(rsa));
            envelope.Validate();string json=JsonSerializer.Serialize(envelope);
            Assert(!json.Contains("Remote Student")&&!json.Contains("4682")&&envelope.Fingerprint.Length==64);
            Assert(RemoteChangeCrypto.Decrypt(JsonSerializer.Deserialize<RemoteEncryptedChange>(json)!,rsa,Site)==change);
            return Task.CompletedTask;
        });
        await test("Remote encryption rejects server, owner, request ID, key, timestamp and payload tampering",async()=>
        {
            using var rsa=RSA.Create(3072);var envelope=RemoteChangeCrypto.Encrypt(Change(),Site,Member.Uid,RemoteChangeCrypto.PublicKey(rsa));
            foreach(var altered in new[]{envelope with {SiteId=Guid.NewGuid().ToString("N")},envelope with {OwnerUid="other"},
                envelope with {Id=Guid.NewGuid().ToString("N")},envelope with {KeyId=new string('A',64)},
                envelope with {CreatedAt=DateTimeOffset.UtcNow.AddDays(1).ToString("O")},envelope with {Tag=Convert.ToBase64String(new byte[16])},
                envelope with {Ciphertext="AAAA"},envelope with {Ciphertext=null!}})
                await Reject(()=>{RemoteChangeCrypto.Decrypt(altered,rsa,Site);return Task.CompletedTask;});
        });
        await test("Remote profiles validate enrollment, field bounds, photos, revision and four-digit PIN",async()=>
        {
            foreach(var change in new[]{Change() with {NewPin="12"},Change() with {NewPin="１２３４"},Change() with {Reason=""},
                Change() with {FullName=new string('a',101)},Change() with {StudentId="=formula"},Change() with {PhotoBase64="bad"},
                Change() with {ExpectedRevision=2},Change() with {Status="anything"}})
                await Reject(()=>{change.Validate();return Task.CompletedTask;});
            (Change() with {NfcUid="AA:BB:CC:DD"}).Validate();
        });
        await test("Protected remote queue survives restart, isolates accounts and preserves immutable retry envelopes",async()=>
        {
            string path=Path.Combine(Path.GetTempPath(),"nfc-remote-tests-"+Guid.NewGuid().ToString("N"));
            try
            {
                using var rsa=RSA.Create(3072);var envelope=RemoteChangeCrypto.Encrypt(Change(),Site,Member.Uid,RemoteChangeCrypto.PublicKey(rsa));
                var queue=new RemoteChangeQueue(path,Site,Member.Uid);queue.Put(new(envelope,"Queued","Waiting"));
                var restarted=new RemoteChangeQueue(path,Site,Member.Uid);Assert(restarted.Snapshot().Single().Change==envelope);
                restarted.Put(new(envelope,"Pending","Uploaded"));Assert(restarted.Snapshot().Length==1);
                Assert(new RemoteChangeQueue(path,Site,"other-user").Snapshot().Length==0);
                await Reject(()=>{restarted.Put(new(envelope with {OwnerUid="other-user"},"Queued",""));return Task.CompletedTask;});
                await Reject(()=>{restarted.Put(new(envelope with {Tag="changed"},"Queued",""));return Task.CompletedTask;});
                string bytes=Encoding.UTF8.GetString(File.ReadAllBytes(Directory.GetFiles(path,"*.queue",SearchOption.AllDirectories).Single()));
                Assert(!bytes.Contains(envelope.Id)&&!bytes.Contains("4682")&&!bytes.Contains("Queued"));
            }
            finally{if(Directory.Exists(path))Directory.Delete(path,true);}
        });
        await test("Cloud auth refresh uses the refresh endpoint and disposed sessions cannot authorize requests",async()=>
        {
            var handler=new RemoteHttp{ExpiresIn="-1"};using var http=new HttpClient(handler);using var session=new FirebaseCloudSession(http,"test-api-key");
            await session.SignInAsync(Member.Email,"test-password");Assert(session.EmailVerified&&session.Uid==Member.Uid);
            Assert(await session.TokenAsync()=="refreshed-token"&&handler.Refreshes==1);
            Assert(await session.TokenAsync()=="refreshed-token"&&handler.Refreshes==1);
            session.Dispose();await Reject(()=>session.TokenAsync());
        });
        await test("Cloud requests carry a Bearer token and account approval is checked independently",async()=>
        {
            var handler=new RemoteHttp();using var http=new HttpClient(handler);using var session=new FirebaseCloudSession(http,"test-api-key");
            await session.SignInAsync(Member.Email,"test-password");var store=new RemoteCloudStore(http,session,"demo-test",Site);
            await store.RequireMemberAsync("administrator");Assert(handler.FirestoreCalls>0);
            await Reject(()=>store.RequireMemberAsync("server"));
            handler.Active=false;await Reject(()=>store.RequireMemberAsync("administrator"));
            handler.Active=true;handler.MemberEmail="wrong@example.test";await Reject(()=>store.RequireMemberAsync("administrator"));
        });
        await test("Unverified email cannot access approved remote administration",async()=>
        {
            var handler=new RemoteHttp{Verified=false};using var http=new HttpClient(handler);using var session=new FirebaseCloudSession(http,"test-api-key");
            await session.SignInAsync(Member.Email,"test-password");var store=new RemoteCloudStore(http,session,"demo-test",Site);
            await Reject(()=>store.RequireMemberAsync("administrator"));Assert(handler.FirestoreCalls==0);
        });
        await test("Cloud upload retries retain the same immutable request and reject ID collisions",async()=>
        {
            var handler=new RemoteHttp();using var http=new HttpClient(handler);using var session=new FirebaseCloudSession(http,"test-api-key");
            await session.SignInAsync(Member.Email,"test-password");var store=new RemoteCloudStore(http,session,"demo-test",Site);
            using var rsa=RSA.Create(3072);var envelope=RemoteChangeCrypto.Encrypt(Change(),Site,Member.Uid,RemoteChangeCrypto.PublicKey(rsa));
            await store.SubmitAsync(envelope);await store.SubmitAsync(envelope);Assert(handler.CreatedRequests==1);
            await Reject(()=>store.SubmitAsync(envelope with {CreatedAt=DateTimeOffset.UtcNow.AddDays(1).ToString("O")}));
            await Reject(()=>store.SubmitAsync(envelope with {OwnerUid="another-user"}));
        });
        await test("Remote request queries restrict the owner and profile pagination rejects loops",async()=>
        {
            var handler=new RemoteHttp();using var http=new HttpClient(handler);using var session=new FirebaseCloudSession(http,"test-api-key");
            await session.SignInAsync(Member.Email,"test-password");var store=new RemoteCloudStore(http,session,"demo-test",Site);
            await store.MyResultsAsync();Assert(handler.LastQuery.Contains("ownerUid")&&handler.LastQuery.Contains(Member.Uid));
            handler.RepeatedPage=true;await Reject(()=>store.ProfilesAsync());
        });
        await test("Server-key publication uses preconditions and cannot silently replace an established key",async()=>
        {
            var handler=new RemoteHttp();using var http=new HttpClient(handler);using var session=new FirebaseCloudSession(http,"test-api-key");
            await session.SignInAsync(Member.Email,"test-password");var store=new RemoteCloudStore(http,session,"demo-test",Site);
            using var first=RSA.Create(3072);using var second=RSA.Create(3072);var key=RemoteChangeCrypto.PublicKey(first);
            await store.PublishKeyAsync(key);Assert(handler.LastKeyCondition.Contains("currentDocument.exists=false"));
            await Reject(()=>store.PublishKeyAsync(RemoteChangeCrypto.PublicKey(second)));Assert(handler.KeyWrites==1);
            await store.PublishKeyAsync(key);Assert(handler.KeyWrites==2&&handler.LastKeyCondition.Contains("currentDocument.updateTime="));
        });
        await test("Independent queue instances retain concurrent submissions and terminal confirmations",async()=>
        {
            string path=Path.Combine(Path.GetTempPath(),"nfc-remote-tests-"+Guid.NewGuid().ToString("N"));
            try
            {
                using var rsa=RSA.Create(3072);var key=RemoteChangeCrypto.PublicKey(rsa);
                var rows=Enumerable.Range(0,20).Select(i=>new RemoteQueuedChange(RemoteChangeCrypto.Encrypt(Change("QUEUE-"+i),Site,Member.Uid,key),"Queued","Waiting")).ToArray();
                await Task.WhenAll(rows.Select(row=>Task.Run(()=>new RemoteChangeQueue(path,Site,Member.Uid).Put(row))));
                var queue=new RemoteChangeQueue(path,Site,Member.Uid);Assert(queue.Snapshot().Length==20);
                queue.Put(rows[0] with {Status="Applied",Message="Confirmed"});
                new RemoteChangeQueue(path,Site,Member.Uid).Put(rows[0] with {Status="Pending"});
                Assert(queue.Snapshot().Single(row=>row.Change.Id==rows[0].Change.Id).Status=="Applied");
            }
            finally{if(Directory.Exists(path))Directory.Delete(path,true);}
        });
    }

    internal static async Task RunDatabaseAsync(Func<string,Func<Task>,Task> test)
    {
        var builder=new MySqlConnectionStringBuilder(DatabaseService.ConnectionString);
        if(builder.Server!="127.0.0.1"||builder.Port!=23306||builder.Database!="nfc_operations_tests")throw new Exception("Not a disposable database");
        using var connection=new MySqlConnection(builder.ConnectionString);await connection.OpenAsync();
        async Task Sql(string sql){using var command=new MySqlCommand(sql,connection);await command.ExecuteNonQueryAsync();}
        async Task<string> Scalar(string sql){using var command=new MySqlCommand(sql,connection);return Convert.ToString(await command.ExecuteScalarAsync())??"";}
        await Sql("ALTER TABLE students MODIFY COLUMN photo_data MEDIUMBLOB; DELETE FROM students; DELETE FROM alerts; DELETE FROM student_graduation_audit;");
        await DatabaseService.EnsureRemoteAdministrationSchemaAsync(connection);await DatabaseService.EnsureRemoteAdministrationSchemaAsync(connection);
        await Sql("DELETE FROM remote_profile_versions; DELETE FROM remote_change_receipts;");
        using var rsa=RSA.Create(3072);
        async Task<RemoteChangeResult> Apply(RemoteProfileChange change,RemoteEncryptedChange? envelope=null,Func<string,string>? issuer=null,RemoteMember? member=null)=>
            await DatabaseService.ApplyRemoteChangeAsync(connection,envelope??RemoteChangeCrypto.Encrypt(change,Site,Member.Uid,RemoteChangeCrypto.PublicKey(rsa)),change,member??Member,
                issuer??(id=>"server-signed-"+id+"-"+Guid.NewGuid().ToString("N")),new("00000000000000000000000000000001","Test server"));
        async Task<RemoteStudent> Profile(string id)
        {
            await DatabaseService.RefreshRemoteProfileVersionsAsync(connection);
            return (await DatabaseService.UnpublishedRemoteProfilesAsync(connection)).Single(x=>x.StudentId==id);
        }
        await test("SQL remote creation stores a hashed PIN and initializes OUTSIDE without exposing secrets",async()=>
        {
            var result=await Apply(Change());Assert(result.Status=="Applied");
            Assert(await Scalar("SELECT entry_state FROM students WHERE student_id='REMOTE-1'")=="OUTSIDE");
            Assert(await Scalar("SELECT pin_hash FROM students WHERE student_id='REMOTE-1'")!="4682");
            Assert(PinHasher.VerifyPin("4682",await Scalar("SELECT pin_salt FROM students WHERE student_id='REMOTE-1'"),await Scalar("SELECT pin_hash FROM students WHERE student_id='REMOTE-1'")));
            string json=JsonSerializer.Serialize(await Profile("REMOTE-1"));Assert(!json.Contains("PinHash")&&!json.Contains("PinSalt")&&!json.Contains("EntryState")&&!json.Contains("4682"));
        });
        await test("SQL remote PIN reset and NFC replacement preserve server-owned occupancy and audit once on retry",async()=>
        {
            var profile=await Profile("REMOTE-1");
            await Sql("UPDATE students SET entry_state='INSIDE',failed_pin_attempts=3,pin_locked=TRUE WHERE student_id='REMOTE-1'");
            var change=Change() with {Create=false,ExpectedRevision=profile.Revision,NfcUid="11223344",NewPin="7391"};
            var envelope=RemoteChangeCrypto.Encrypt(change,Site,Member.Uid,RemoteChangeCrypto.PublicKey(rsa));
            var first=await Apply(change,envelope);string qr=await Scalar("SELECT qr_credential FROM students WHERE student_id='REMOTE-1'");
            var retry=await Apply(change,envelope);Assert(first==retry&&first.Status=="Applied");
            Assert(await Scalar("SELECT entry_state FROM students WHERE student_id='REMOTE-1'")=="INSIDE");
            Assert(await Scalar("SELECT CONCAT(failed_pin_attempts,':',pin_locked) FROM students WHERE student_id='REMOTE-1'")=="0:0");
            Assert(await Scalar("SELECT qr_credential FROM students WHERE student_id='REMOTE-1'")==qr);
            Assert(await Scalar("SELECT COUNT(*) FROM alerts WHERE alert_type='REMOTE_ADMIN'")=="2");
        });
        await test("SQL stale remote edit detects a new on-site change before any periodic publication",async()=>
        {
            var profile=await Profile("REMOTE-1");await Sql("UPDATE students SET full_name='Onsite Edit' WHERE student_id='REMOTE-1'");
            var result=await Apply(Change() with {Create=false,ExpectedRevision=profile.Revision,NewPin=null,FullName="Stale Edit"});
            Assert(result.Status=="Conflict"&&await Scalar("SELECT full_name FROM students WHERE student_id='REMOTE-1'")=="Onsite Edit");
        });
        await test("SQL duplicate NFC, missing activation credentials and graduation hold reject all profile changes",async()=>
        {
            Assert((await Apply(Change("REMOTE-2") with {NfcUid="11223344"})).Status=="Rejected");
            Assert((await Apply(Change("REMOTE-3") with {NfcUid="",NewPin=null})).Status=="Rejected");
            var profile=await Profile("REMOTE-1");
            Assert((await Apply(Change() with {Create=false,ExpectedRevision=profile.Revision,Status="Graduated",NewPin=null})).Status=="Rejected");
            Assert(await Scalar("SELECT COUNT(*) FROM students")=="1");
        });
        await test("SQL QR issuance failure rolls back profile, PIN, receipt outcome and audit consistently",async()=>
        {
            var result=await Apply(Change("REMOTE-4") with {NfcUid="55667788"},issuer:_=>throw new InvalidOperationException("Signing unavailable"));
            Assert(result.Status=="Rejected"&&await Scalar("SELECT COUNT(*) FROM students WHERE student_id='REMOTE-4'")=="0");
        });
        await test("SQL remote updates preserve oversized on-site photos unless explicitly replaced",async()=>
        {
            await Sql("UPDATE students SET photo_data=REPEAT('X',300000) WHERE student_id='REMOTE-1'");var profile=await Profile("REMOTE-1");
            Assert(profile.PhotoOmitted&&profile.PhotoBase64.Length==0);
            var result=await Apply(Change() with {Create=false,ExpectedRevision=profile.Revision,NfcUid=profile.NfcUid,NewPin=null,ReissueQr=false});
            Assert(result.Status=="Applied"&&await Scalar("SELECT LENGTH(photo_data) FROM students WHERE student_id='REMOTE-1'")=="300000");
        });
        await test("SQL outbox survives retry and deleted profiles publish a tombstone rather than resurrecting",async()=>
        {
            var profile=await Profile("REMOTE-1");await DatabaseService.MarkRemoteProfilePublishedAsync(connection,profile);
            Assert(!(await DatabaseService.UnpublishedRemoteProfilesAsync(connection)).Any(x=>x.StudentId=="REMOTE-1"));
            await Sql("DELETE FROM students WHERE student_id='REMOTE-1'");var tombstone=await Profile("REMOTE-1");Assert(tombstone.Deleted&&tombstone.Revision>profile.Revision);
            Assert((await Apply(Change())).Status=="Conflict");
            Assert((await DatabaseService.UnpublishedRemoteResultsAsync(connection)).Count>0);
        });
        await test("SQL revoked account cannot apply a queued remote request",async()=>
        {
            await Reject(()=>Apply(Change("REVOKED"),member:Member with {Active=false}));
            Assert(await Scalar("SELECT COUNT(*) FROM students WHERE student_id='REVOKED'")=="0");
        });
        await test("SQL remote pending profile needs no card, PIN or signing key",async()=>
        {
            var result=await Apply(Change("REMOTE-PENDING") with {Status="Pending Enrollment",NfcUid="",NewPin=null,ReissueQr=false},
                issuer:_=>throw new Exception("A signer must not be needed"));
            Assert(result.Status=="Applied"&&await Scalar("SELECT COUNT(*) FROM students WHERE student_id='REMOTE-PENDING' AND nfc_uid IS NULL AND pin_hash IS NULL AND qr_credential IS NULL")=="1");
        });
        await test("SQL simultaneous retries commit one remote profile and one audit receipt",async()=>
        {
            var change=Change("REMOTE-CONCURRENT") with {NfcUid="99887766"};var envelope=RemoteChangeCrypto.Encrypt(change,Site,Member.Uid,RemoteChangeCrypto.PublicKey(rsa));
            async Task<RemoteChangeResult> Concurrent()
            {
                using var peer=new MySqlConnection(DatabaseService.ConnectionString);await peer.OpenAsync();
                return await DatabaseService.ApplyRemoteChangeAsync(peer,envelope,change,Member,id=>"signed-"+id,
                    new("00000000000000000000000000000001","Test server"));
            }
            var results=await Task.WhenAll(Concurrent(),Concurrent());Assert(results[0]==results[1]&&results[0].Status=="Applied");
            Assert(await Scalar("SELECT COUNT(*) FROM alerts WHERE alert_type='REMOTE_ADMIN' AND student_id='REMOTE-CONCURRENT'")=="1");
            await Reject(()=>Apply(change,envelope with {CreatedAt=DateTimeOffset.UtcNow.AddDays(1).ToString("O")}));
        });
    }

    private sealed class RemoteHttp:HttpMessageHandler
    {
        internal string ExpiresIn="3600",MemberEmail=Member.Email,LastQuery="";
        internal bool Active=true,Verified=true,RepeatedPage;
        internal int Refreshes,FirestoreCalls,CreatedRequests,KeyWrites;
        internal string LastKeyCondition="";
        private JsonElement? _request;
        private JsonElement? _site;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            string url=request.RequestUri!.AbsoluteUri;
            if(url.Contains("accounts:signInWithPassword"))return Reply(HttpStatusCode.OK,new {idToken="test-token",refreshToken="test-refresh",localId=Member.Uid,email=Member.Email,expiresIn=ExpiresIn});
            if(url.Contains("accounts:lookup"))return Reply(HttpStatusCode.OK,new {users=new[]{new {emailVerified=Verified}}});
            if(url.Contains("securetoken.googleapis.com")){Refreshes++;return Reply(HttpStatusCode.OK,new {id_token="refreshed-token",refresh_token="next-refresh",user_id=Member.Uid,expires_in="3600"});}
            Assert(request.Headers.Authorization?.Scheme=="Bearer"&&request.Headers.Authorization.Parameter=="test-token");FirestoreCalls++;
            if(request.RequestUri.AbsolutePath.EndsWith("/remote_sites/"+Site,StringComparison.Ordinal))
            {
                if(request.Method==HttpMethod.Patch)
                {using var json=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));_site=json.RootElement.GetProperty("fields").Clone();KeyWrites++;LastKeyCondition=request.RequestUri.Query;return Reply(HttpStatusCode.OK,new{});}
                return _site==null?Reply(HttpStatusCode.NotFound,new{}):Reply(HttpStatusCode.OK,new{fields=_site,updateTime="2026-10-18T00:00:00Z"});
            }
            if(url.Contains("/members/"))return Reply(HttpStatusCode.OK,new {fields=new {email=new {stringValue=MemberEmail},name=new {stringValue=Member.Name},role=new {stringValue="administrator"},active=new {booleanValue=Active}}});
            if(url.Contains(":runQuery")){LastQuery=await request.Content!.ReadAsStringAsync(token);return Reply(HttpStatusCode.OK,Array.Empty<object>());}
            if(url.Contains("/profiles?"))return Reply(HttpStatusCode.OK,RepeatedPage?new {nextPageToken="repeat"}:new {nextPageToken=""});
            if(request.Method==HttpMethod.Post)
            {
                if(_request!=null)return Reply(HttpStatusCode.Conflict,new {});
                using var json=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));_request=json.RootElement.GetProperty("fields").Clone();CreatedRequests++;
                return Reply(HttpStatusCode.OK,new {});
            }
            if(_request!=null)return Reply(HttpStatusCode.OK,new {fields=_request});
            return Reply(HttpStatusCode.NotFound,new {});
        }
        private static HttpResponseMessage Reply(HttpStatusCode status,object payload)=>new(status){Content=new StringContent(JsonSerializer.Serialize(payload),Encoding.UTF8,"application/json")};
    }
}
