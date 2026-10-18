using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NFC_System;

internal sealed class FirebaseCloudSession(HttpClient http, string apiKey) : IDisposable
{
    private readonly SemaphoreSlim _tokenLock = new(1,1);
    private string _idToken = "", _refreshToken = "";
    private DateTimeOffset _expires;
    private bool _disposed;
    internal string Uid { get; private set; } = "";
    internal string Email { get; private set; } = "";
    internal bool EmailVerified { get; private set; }

    internal async Task SignInAsync(string email, string password, CancellationToken token = default)
    {
        if (Uid.Length > 0) throw new InvalidOperationException("Sign out before changing cloud accounts.");
        using var result = await PostAsync("https://identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key="+apiKey,
            new { email=email.Trim(), password, returnSecureToken=true },token);
        var root = result.RootElement;
        _idToken=root.GetProperty("idToken").GetString()!;
        _refreshToken=root.GetProperty("refreshToken").GetString()!;
        Uid=root.GetProperty("localId").GetString()!;
        Email=root.GetProperty("email").GetString()!;
        _expires=DateTimeOffset.UtcNow.AddSeconds(int.Parse(root.GetProperty("expiresIn").GetString()!,CultureInfo.InvariantCulture));
        using var lookup = await PostAsync("https://identitytoolkit.googleapis.com/v1/accounts:lookup?key="+apiKey,new { idToken=_idToken },token);
        EmailVerified=lookup.RootElement.GetProperty("users")[0].GetProperty("emailVerified").GetBoolean();
    }

    internal async Task SendVerificationAsync(CancellationToken token = default)
    {
        using var response = await PostAsync("https://identitytoolkit.googleapis.com/v1/accounts:sendOobCode?key="+apiKey,
            new { requestType="VERIFY_EMAIL", idToken=await TokenAsync(token) },token);
    }

    internal async Task<string> TokenAsync(CancellationToken token = default)
    {
        await _tokenLock.WaitAsync(token);
        try
        {
            if (_disposed || Uid.Length == 0) throw new UnauthorizedAccessException("Sign in to the cloud again.");
            if (_expires > DateTimeOffset.UtcNow.AddMinutes(2)) return _idToken;
            using var content = new FormUrlEncodedContent(new Dictionary<string,string> { ["grant_type"]="refresh_token",["refresh_token"]=_refreshToken });
            using var response = await http.PostAsync("https://securetoken.googleapis.com/v1/token?key="+apiKey,content,token);
            if (!response.IsSuccessStatusCode) throw new UnauthorizedAccessException("Cloud sign-in expired or was revoked. Sign in again.");
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
            var root=json.RootElement;
            if (root.GetProperty("user_id").GetString()!=Uid) throw new UnauthorizedAccessException("Cloud account identity changed.");
            _idToken=root.GetProperty("id_token").GetString()!;
            _refreshToken=root.GetProperty("refresh_token").GetString()!;
            _expires=DateTimeOffset.UtcNow.AddSeconds(int.Parse(root.GetProperty("expires_in").GetString()!,CultureInfo.InvariantCulture));
            return _idToken;
        }
        finally { _tokenLock.Release(); }
    }

    private async Task<JsonDocument> PostAsync(string url, object data, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(_disposed,this);
        using var content = new StringContent(JsonSerializer.Serialize(data),Encoding.UTF8,"application/json");
        using var response = await http.PostAsync(url,content,token);
        if (!response.IsSuccessStatusCode)
            throw new UnauthorizedAccessException("Cloud sign-in failed. Check the email/password, account status, and Firebase email/password setup.");
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
    }

    public void Dispose() { _disposed=true; _idToken=""; _refreshToken=""; }
}

internal sealed class RemoteCloudStore(HttpClient http, FirebaseCloudSession session, string project, string site)
{
    private string Root => $"https://firestore.googleapis.com/v1/projects/{project}/databases/(default)/documents";
    private string Site => "remote_sites/"+site;
    internal string OwnerUid => session.Uid;
    internal DateTimeOffset? LastServerPublication { get; private set; }

    internal async Task<RemoteMember> RequireMemberAsync(string role, CancellationToken token=default)
    {
        if (!session.EmailVerified) throw new UnauthorizedAccessException("Verify your cloud email address, then sign out and sign in again.");
        var member=await MemberAsync(session.Uid,token) ?? throw new UnauthorizedAccessException("This cloud account has not been approved for this server.");
        member.Require(session.Uid,session.Email,role);
        return member;
    }

    internal async Task<RemoteMember?> MemberAsync(string uid, CancellationToken token=default)
    {
        var fields=await ReadAsync(Site+"/members/"+Segment(uid),token);
        return fields is null ? null : new RemoteMember(uid,Field(fields.Value,"email"),Field(fields.Value,"name"),Field(fields.Value,"role"),
            fields.Value.TryGetProperty("active",out var active) && active.GetProperty("booleanValue").GetBoolean());
    }

    internal Task SaveMemberAsync(RemoteMember member,CancellationToken token=default) => WriteAsync(Site+"/members/"+Segment(member.Uid),new()
    {
        ["email"]=Text(member.Email),["name"]=Text(member.Name),["role"]=Text("administrator"),["active"]=new { booleanValue=member.Active }
    },token);

    internal async Task<RemoteServerKey> ServerKeyAsync(CancellationToken token=default)
    {
        var fields=await ReadAsync(Site,token) ?? throw new InvalidOperationException("The main server has not published its remote-administration setup.");
        LastServerPublication=DateTimeOffset.TryParse(Field(fields,"updatedAt"),CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out var date)?date:null;
        return new(Field(fields,"keyId"),Field(fields,"publicKey"));
    }

    internal async Task PublishKeyAsync(RemoteServerKey key,CancellationToken token=default)
    {
        using var read=await SendAsync(HttpMethod.Get,Site,null,token);
        string condition="?currentDocument.exists=false";
        if(read.StatusCode!=HttpStatusCode.NotFound)
        {
            RequireSuccess(read);using var json=JsonDocument.Parse(await read.Content.ReadAsStringAsync(token));
            var fields=json.RootElement.TryGetProperty("fields",out var value)?value:default;
            if(fields.ValueKind==JsonValueKind.Object && Field(fields,"keyId").Length>0 &&
                (Field(fields,"keyId")!=key.KeyId || Field(fields,"publicKey")!=key.PublicKey))
                throw new InvalidOperationException("The published server key belongs to another Windows account or an earlier installation. Restore the original server key before processing remote changes.");
            condition="?currentDocument.updateTime="+Uri.EscapeDataString(json.RootElement.GetProperty("updateTime").GetString()!);
        }
        var payload=new { fields=new Dictionary<string,object> {
            ["keyId"]=Text(key.KeyId),["publicKey"]=Text(key.PublicKey),["updatedAt"]=Text(DateTimeOffset.UtcNow.ToString("O")) } };
        using var response=await SendAsync(HttpMethod.Patch,Site+condition,payload,token);
        RequireSuccess(response);
    }

    internal async Task SubmitAsync(RemoteEncryptedChange change,CancellationToken token=default)
    {
        change.Validate();
        if (change.SiteId!=site || change.OwnerUid!=session.Uid) throw new UnauthorizedAccessException("Request account or server mismatch.");
        var fields=new Dictionary<string,object> { ["ownerUid"]=Text(change.OwnerUid),["status"]=Text("Pending"),
            ["envelope"]=Text(JsonSerializer.Serialize(change)),["createdAt"]=Text(change.CreatedAt) };
        using var response=await SendAsync(HttpMethod.Post,Site+"/requests?documentId="+change.Id,new { fields },token);
        if (response.StatusCode==HttpStatusCode.Conflict)
        {
            var prior=await ReadAsync(Site+"/requests/"+change.Id,token);
            if (prior==null || Field(prior.Value,"envelope")!=JsonSerializer.Serialize(change) || Field(prior.Value,"ownerUid")!=change.OwnerUid)
                throw new InvalidOperationException("The cloud request ID belongs to a different change.");
            return;
        }
        RequireSuccess(response);
    }

    internal async Task<IReadOnlyList<RemoteEncryptedChange>> PendingAsync(CancellationToken token=default)
    {
        var rows=await QueryAsync("requests","status","Pending",100,token);
        var results=new List<RemoteEncryptedChange>();
        foreach (var row in rows)
        {
            try
            {
                var change=JsonSerializer.Deserialize<RemoteEncryptedChange>(Field(row.Fields,"envelope"));
                if (change==null || change.Id!=row.Id || change.SiteId!=site || change.OwnerUid!=Field(row.Fields,"ownerUid"))
                    throw new JsonException();
                results.Add(change);
            }
            catch(JsonException) { await PublishResultAsync(new(row.Id,"Rejected","Invalid request envelope."),token); }
        }
        return results;
    }

    internal async Task<IReadOnlyList<RemoteChangeResult>> MyResultsAsync(CancellationToken token=default)
    {
        var rows=new List<(string Id,JsonElement Fields)>();
        string after="";
        while(true)
        {
            var page=await QueryAsync("requests","ownerUid",session.Uid,500,token,after);
            rows.AddRange(page);
            if(page.Count<500)break;
            string next=page[^1].Id;
            if(next==after)throw new System.IO.InvalidDataException("Cloud request pagination did not advance.");
            after=next;
        }
        return rows.Select(row=>new RemoteChangeResult(row.Id,Field(row.Fields,"status"),Field(row.Fields,"message"),
            long.TryParse(Field(row.Fields,"revision"),out long revision)?revision:0)).ToArray();
    }

    internal Task PublishResultAsync(RemoteChangeResult result,CancellationToken token=default) => WriteAsync(Site+"/requests/"+Segment(result.Id),new()
    {
        ["status"]=Text(result.Status),["message"]=Text(result.Message),["revision"]=Text(result.Revision.ToString(CultureInfo.InvariantCulture)),
        ["processedAt"]=Text(DateTimeOffset.UtcNow.ToString("O"))
    },token,merge:true);

    internal Task PublishProfileAsync(RemoteStudent profile,CancellationToken token=default) => WriteAsync(Site+"/profiles/"+ProfileId(profile.StudentId),
        new() { ["profile"]=Text(JsonSerializer.Serialize(profile)) },token);

    internal async Task<IReadOnlyList<RemoteMember>> MembersAsync(CancellationToken token=default)
    {
        var results=new List<RemoteMember>();
        string page="";var seen=new HashSet<string>(StringComparer.Ordinal);
        do
        {
            using var response=await SendAsync(HttpMethod.Get,Site+"/members?pageSize=500"+(page.Length>0?"&pageToken="+Uri.EscapeDataString(page):""),null,token);
            RequireSuccess(response);using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
            if(json.RootElement.TryGetProperty("documents",out var documents))foreach(var document in documents.EnumerateArray())
            {
                var fields=document.GetProperty("fields");string uid=document.GetProperty("name").GetString()!.Split('/')[^1];
                results.Add(new(uid,Field(fields,"email"),Field(fields,"name"),Field(fields,"role"),fields.GetProperty("active").GetProperty("booleanValue").GetBoolean()));
            }
            page=json.RootElement.TryGetProperty("nextPageToken",out var next)?next.GetString()??"":"";
            if(page.Length>0&&!seen.Add(page))throw new System.IO.InvalidDataException("Cloud member pagination did not advance.");
        }while(page.Length>0);
        return results;
    }

    internal async Task<IReadOnlyList<RemoteStudent>> ProfilesAsync(CancellationToken token=default)
    {
        var results=new List<RemoteStudent>();
        var seen=new HashSet<string>(StringComparer.Ordinal);
        string page="";
        do
        {
            using var response=await SendAsync(HttpMethod.Get,Site+"/profiles?pageSize=500"+(page.Length>0?"&pageToken="+Uri.EscapeDataString(page):""),null,token);
            RequireSuccess(response);
            using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
            if(json.RootElement.TryGetProperty("documents",out var documents)) foreach(var document in documents.EnumerateArray())
            {
                var profile=JsonSerializer.Deserialize<RemoteStudent>(Field(document.GetProperty("fields"),"profile"));
                if(profile!=null && !profile.Deleted) results.Add(profile);
            }
            page=json.RootElement.TryGetProperty("nextPageToken",out var next)?next.GetString()??"":"";
            if(page.Length>0&&!seen.Add(page))throw new System.IO.InvalidDataException("Cloud profile pagination did not advance.");
        } while(page.Length>0);
        return results;
    }

    private async Task<List<(string Id,JsonElement Fields)>> QueryAsync(string collection,string field,string value,int limit,CancellationToken token,string after="")
    {
        var query=new Dictionary<string,object> { ["from"]=new[] { new { collectionId=collection } },
            ["where"]=new { fieldFilter=new { field=new { fieldPath=field },op="EQUAL",value=Text(value) } },["limit"]=limit,
            ["orderBy"]=new[] {new {field=new {fieldPath="__name__"},direction="ASCENDING"}} };
        if(after.Length>0)query["startAt"]=new {values=new[] {new {referenceValue=$"projects/{project}/databases/(default)/documents/{Site}/{collection}/{after}"}},before=false};
        using var response=await SendAsync(HttpMethod.Post,Site+":runQuery",new { structuredQuery=query },token);
        RequireSuccess(response);
        using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        var rows=new List<(string,JsonElement)>();
        foreach(var result in json.RootElement.EnumerateArray()) if(result.TryGetProperty("document",out var document))
            rows.Add((document.GetProperty("name").GetString()!.Split('/')[^1],document.GetProperty("fields").Clone()));
        return rows;
    }

    private async Task<JsonElement?> ReadAsync(string path,CancellationToken token)
    {
        using var response=await SendAsync(HttpMethod.Get,path,null,token);
        if(response.StatusCode==HttpStatusCode.NotFound)return null;
        RequireSuccess(response);
        using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        return json.RootElement.GetProperty("fields").Clone();
    }

    private async Task WriteAsync(string path,Dictionary<string,object> fields,CancellationToken token,bool merge=false)
    {
        if(merge) path+="?"+string.Join("&",fields.Keys.Select(key=>"updateMask.fieldPaths="+Uri.EscapeDataString(key)));
        using var response=await SendAsync(HttpMethod.Patch,path,new { fields },token);
        RequireSuccess(response);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method,string path,object? payload,CancellationToken token)
    {
        using var request=new HttpRequestMessage(method,Root+"/"+path);
        request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",await session.TokenAsync(token));
        if(payload!=null) request.Content=new StringContent(JsonSerializer.Serialize(payload),Encoding.UTF8,"application/json");
        return await http.SendAsync(request,token);
    }

    private static object Text(string value)=>new { stringValue=value };
    private static string Field(JsonElement fields,string name)=>fields.TryGetProperty(name,out var value)&&value.TryGetProperty("stringValue",out var text)?text.GetString()??"":"";
    private static string Segment(string value)=>value.Length is >0 and <=128 && !value.Contains('/') && value is not ("." or "..")
        ?Uri.EscapeDataString(value):throw new InvalidOperationException("Invalid cloud record ID.");
    internal static string ProfileId(string id)=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(id)));
    private static void RequireSuccess(HttpResponseMessage response)
    {
        if(response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new UnauthorizedAccessException("Cloud access denied. Check account approval and the deployed Firebase security rules.");
        if(!response.IsSuccessStatusCode) throw new HttpRequestException($"Cloud synchronization failed ({(int)response.StatusCode}). Retry when connected.");
    }
}
