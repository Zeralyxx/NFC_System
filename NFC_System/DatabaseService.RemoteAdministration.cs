using MySqlConnector;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NFC_System;

public sealed partial class DatabaseService
{
    internal static async Task EnsureRemoteAdministrationSchemaAsync(MySqlConnection connection)
    {
        await EnsurePinSchemaAsync(connection);
        using var schema=new MySqlCommand(@"
            CREATE TABLE IF NOT EXISTS remote_profile_versions (
                student_id VARCHAR(50) PRIMARY KEY, revision BIGINT NOT NULL, fingerprint CHAR(64) NOT NULL,
                payload_json LONGTEXT NOT NULL, published_revision BIGINT NOT NULL DEFAULT 0
            ) ENGINE=InnoDB;
            CREATE TABLE IF NOT EXISTS remote_change_receipts (
                request_id CHAR(32) PRIMARY KEY, owner_uid VARCHAR(128) NOT NULL, fingerprint CHAR(64) NOT NULL,
                result_json LONGTEXT NOT NULL, published BOOLEAN NOT NULL DEFAULT FALSE
            ) ENGINE=InnoDB;",connection);
        await schema.ExecuteNonQueryAsync();
        using var engines=new MySqlCommand(@"SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE()
            AND table_name IN ('students','alerts','student_graduation_audit','pin_security_versions','remote_profile_versions','remote_change_receipts')
            AND engine='InnoDB'",connection);
        if(Convert.ToInt32(await engines.ExecuteScalarAsync())!=6)
            throw new InvalidOperationException("Remote administration requires all student, PIN and audit tables to use InnoDB.");
    }

    private static async Task<(RemoteStudent? Profile,string SecretTag)> LockRemoteProfileAsync(MySqlConnection connection,
        MySqlTransaction transaction,string id,CancellationToken token)
    {
        using var command=new MySqlCommand(@"SELECT student_id,full_name,email,course,year_level,section_name,status,nfc_uid,
            qr_credential,pin_hash,pin_salt,is_temporary,photo_data,graduation_clearance FROM students WHERE student_id=@id FOR UPDATE",connection,transaction);
        command.Parameters.AddWithValue("@id",id);
        using var reader=await command.ExecuteReaderAsync(token);
        if(!await reader.ReadAsync(token)) return (null,"");
        string Text(string name)=>reader[name]==DBNull.Value?"":Convert.ToString(reader[name])??"";
        byte[] photo=reader["photo_data"] is byte[] bytes?bytes:[];
        var profile=new RemoteStudent { StudentId=Text("student_id"),FullName=Text("full_name"),Email=Text("email"),Course=Text("course"),
            YearLevel=Text("year_level"),Section=Text("section_name"),Status=Text("status"),NfcUid=Text("nfc_uid"),
            QrCredential=Text("qr_credential"),HasPin=Text("pin_hash").Length>0&&Text("pin_salt").Length>0,
            IsTemporary=reader["is_temporary"]!=DBNull.Value&&Convert.ToBoolean(reader["is_temporary"]),Clearance=Text("graduation_clearance"),
            PhotoBase64=photo.Length<=250000?Convert.ToBase64String(photo):"",PhotoOmitted=photo.Length>250000 };
        // Sensitive credentials affect conflict detection but never enter the published profile.
        string secret=Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Text("pin_hash")+"|"+Text("pin_salt")+"|"+Convert.ToHexString(SHA256.HashData(photo)))));
        return (profile,secret);
    }

    private static async Task<RemoteStudent> VersionRemoteProfileAsync(MySqlConnection connection,MySqlTransaction transaction,
        string id,(RemoteStudent? Profile,string SecretTag) current,CancellationToken token)
    {
        var profile=current.Profile??new RemoteStudent { StudentId=id,Deleted=true };
        string fingerprint=Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { Profile=profile,Secret=current.SecretTag })));
        long revision=0; string previous="";
        using(var query=new MySqlCommand("SELECT revision,fingerprint FROM remote_profile_versions WHERE student_id=@id FOR UPDATE",connection,transaction))
        {
            query.Parameters.AddWithValue("@id",id);
            using var reader=await query.ExecuteReaderAsync(token);
            if(await reader.ReadAsync(token)){revision=reader.GetInt64(0);previous=reader.GetString(1);}
        }
        if(fingerprint!=previous)
        {
            profile=profile with { Revision=++revision };
            using var update=new MySqlCommand(@"INSERT INTO remote_profile_versions(student_id,revision,fingerprint,payload_json)
                VALUES(@id,@revision,@fingerprint,@json) ON DUPLICATE KEY UPDATE revision=@revision,fingerprint=@fingerprint,payload_json=@json",connection,transaction);
            update.Parameters.AddWithValue("@id",id);update.Parameters.AddWithValue("@revision",revision);
            update.Parameters.AddWithValue("@fingerprint",fingerprint);update.Parameters.AddWithValue("@json",JsonSerializer.Serialize(profile));
            await update.ExecuteNonQueryAsync(token);
        }
        return profile with { Revision=revision };
    }

    internal static async Task RefreshRemoteProfileVersionsAsync(MySqlConnection connection,CancellationToken token=default)
    {
        var ids=new List<string>();
        using(var query=new MySqlCommand("SELECT student_id FROM students UNION SELECT student_id FROM remote_profile_versions",connection))
        using(var reader=await query.ExecuteReaderAsync(token)) while(await reader.ReadAsync(token)) ids.Add(reader.GetString(0));
        foreach(string id in ids)
        {
            token.ThrowIfCancellationRequested();
            using var transaction=await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted,token);
            await VersionRemoteProfileAsync(connection,transaction,id,await LockRemoteProfileAsync(connection,transaction,id,token),token);
            await transaction.CommitAsync(token);
        }
    }

    internal static async Task<IReadOnlyList<RemoteStudent>> UnpublishedRemoteProfilesAsync(MySqlConnection connection,CancellationToken token=default)
    {
        var rows=new List<RemoteStudent>();
        using var query=new MySqlCommand("SELECT payload_json FROM remote_profile_versions WHERE revision>published_revision",connection);
        using var reader=await query.ExecuteReaderAsync(token);
        while(await reader.ReadAsync(token)) rows.Add(JsonSerializer.Deserialize<RemoteStudent>(reader.GetString(0))!);
        return rows;
    }

    internal static async Task MarkRemoteProfilePublishedAsync(MySqlConnection connection,RemoteStudent profile,CancellationToken token=default)
    {
        using var update=new MySqlCommand("UPDATE remote_profile_versions SET published_revision=GREATEST(published_revision,@revision) WHERE student_id=@id",connection);
        update.Parameters.AddWithValue("@revision",profile.Revision);update.Parameters.AddWithValue("@id",profile.StudentId);
        await update.ExecuteNonQueryAsync(token);
    }

    internal static async Task<RemoteChangeResult> ApplyRemoteChangeAsync(MySqlConnection connection,RemoteEncryptedChange envelope,
        RemoteProfileChange change,RemoteMember member,Func<string,string> issueQr,DeviceAttribution serverDevice,CancellationToken token=default)
    {
        envelope.Validate();change.Validate();
        if(!member.Active || member.Role!="administrator" || member.Uid!=envelope.OwnerUid || string.IsNullOrWhiteSpace(member.Name))
            throw new UnauthorizedAccessException("The remote administrator is no longer approved.");
        using var transaction=await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted,token);
        using(var seed=new MySqlCommand(@"INSERT IGNORE INTO remote_change_receipts(request_id,owner_uid,fingerprint,result_json)
            VALUES(@id,@uid,@fingerprint,'')",connection,transaction))
        {
            seed.Parameters.AddWithValue("@id",envelope.Id);seed.Parameters.AddWithValue("@uid",member.Uid);seed.Parameters.AddWithValue("@fingerprint",envelope.Fingerprint);
            await seed.ExecuteNonQueryAsync(token);
        }
        using(var receipt=new MySqlCommand("SELECT owner_uid,fingerprint,result_json FROM remote_change_receipts WHERE request_id=@id FOR UPDATE",connection,transaction))
        {
            receipt.Parameters.AddWithValue("@id",envelope.Id);
            using var reader=await receipt.ExecuteReaderAsync(token);await reader.ReadAsync(token);
            if(reader.GetString(0)!=member.Uid||reader.GetString(1)!=envelope.Fingerprint) throw new InvalidDataException("Request receipt identity mismatch.");
            if(reader.GetString(2).Length>0)
            {
                var prior=JsonSerializer.Deserialize<RemoteChangeResult>(reader.GetString(2))!;
                await reader.DisposeAsync();await transaction.CommitAsync(token);return prior;
            }
        }
        await transaction.SaveAsync("profile_change",token);
        RemoteChangeResult result;
        try
        {
            var locked=await LockRemoteProfileAsync(connection,transaction,change.StudentId,token);
            var version=await VersionRemoteProfileAsync(connection,transaction,change.StudentId,locked,token);
            if(change.Create ? locked.Profile!=null || version.Revision>1 : locked.Profile==null || version.Revision!=change.ExpectedRevision)
                result=new(envelope.Id,"Conflict","Profile changed or is no longer available. Refresh and review before submitting again.",version.Revision);
            else
            {
                var current=locked.Profile;
                StudentProfileRules.ValidateEdit(change.StudentId,change.FullName,change.Status,change.NfcUid,current?.NfcUid.Length>0);
                if(change.Create) GraduationRules.RequireNewStudentStatus(change.Status,change.NfcUid,change.NewPin!=null);
                else GraduationRules.RequireGraduationClearance(new GraduationStudent { StudentId=change.StudentId,EnrollmentStatus=current!.Status,
                    HasNfc=change.NfcUid.Length>0,HasPin=current.HasPin||change.NewPin!=null,Clearance=GraduationRules.Parse(current.Clearance) },change.Status);
                using(var duplicate=new MySqlCommand("SELECT COUNT(*) FROM students WHERE nfc_uid=@uid AND student_id<>@id",connection,transaction))
                {
                    duplicate.Parameters.AddWithValue("@uid",change.NfcUid);duplicate.Parameters.AddWithValue("@id",change.StudentId);
                    if(change.NfcUid.Length>0&&Convert.ToInt32(await duplicate.ExecuteScalarAsync(token))>0)
                        throw new InvalidOperationException("This NFC card is already assigned to another student.");
                }
                string qr=current?.QrCredential??"";
                if(StudentProfileRules.ShouldIssueQr(change.StudentId,current?.NfcUid??"",qr,change.StudentId,change.NfcUid,change.ReissueQr))
                    qr=issueQr(change.StudentId);
                var student=new StudentRecord { StudentId=change.StudentId,FullName=change.FullName,Email=change.Email,Course=change.Course,
                    YearLevel=change.YearLevel,SectionName=change.Section,Status=change.Status,NfcUid=change.NfcUid,QrCredential=qr,
                    PhotoData=change.PhotoBase64.Length==0?null:Convert.FromBase64String(change.PhotoBase64),IsTemporary=change.IsTemporary };
                string? salt=null,hash=null;
                if(change.NewPin!=null){var pin=PinHasher.HashPin(change.NewPin);salt=pin.Salt;hash=pin.Hash;}
                string sql=change.Create?@"INSERT INTO students(student_id,full_name,email,course,year_level,section_name,status,nfc_uid,qr_credential,
                    photo_data,is_temporary,pin_salt,pin_hash,entry_state,failed_pin_attempts,pin_locked) VALUES
                    (@student_id,@full_name,@email,@course,@year_level,@section_name,@status,@nfc_uid,@qr_credential,@photo_data,@is_temporary,@pin_salt,@pin_hash,'OUTSIDE',0,FALSE)"
                    :@"UPDATE students SET full_name=@full_name,email=@email,course=@course,year_level=@year_level,section_name=@section_name,status=@status,
                    nfc_uid=@nfc_uid,qr_credential=@qr_credential,is_temporary=@is_temporary"+(change.ReplacePhoto?",photo_data=@photo_data":"")+
                    (salt!=null?",pin_salt=@pin_salt,pin_hash=@pin_hash,failed_pin_attempts=0,pin_locked=FALSE":"")+" WHERE student_id=@student_id";
                using(var write=new MySqlCommand(sql,connection,transaction))
                {AddStudentParameters(write,student,salt,hash);await write.ExecuteNonQueryAsync(token);}
                if(change.NewPin!=null) await RotatePinResetAsync(connection,transaction,change.StudentId);
                if(current!=null&&current.Status!=change.Status)
                    await AddGraduationAuditAsync(connection,transaction,new GraduationStudent { StudentId=change.StudentId,FullName=change.FullName },
                        "EnrollmentStatus",current.Status,change.Status,change.Reason,member.Name,serverDevice);
                using(var audit=new MySqlCommand(@"INSERT INTO alerts(student_id,alert_type,message,device_id,device_name)
                    VALUES(@student,'REMOTE_ADMIN',@message,@device,@name)",connection,transaction))
                {
                    audit.Parameters.AddWithValue("@student",change.StudentId);
                    audit.Parameters.AddWithValue("@message",$"Remote account {member.Uid} ({member.Name}): {(change.Create?"created":"updated")} profile. Request {envelope.Id}. NFC changed: {current?.NfcUid!=change.NfcUid}; PIN reset: {change.NewPin!=null}; QR changed: {qr!=current?.QrCredential}. Reason: {change.Reason}");
                    audit.Parameters.AddWithValue("@device",serverDevice.DeviceId);audit.Parameters.AddWithValue("@name",serverDevice.DeviceName);
                    await audit.ExecuteNonQueryAsync(token);
                }
                var applied=await VersionRemoteProfileAsync(connection,transaction,change.StudentId,await LockRemoteProfileAsync(connection,transaction,change.StudentId,token),token);
                result=new(envelope.Id,"Applied",$"{change.StudentId}: applied by the main server.",applied.Revision);
            }
        }
        catch(Exception ex) when(ex is InvalidOperationException or ArgumentException || ex is MySqlException { Number:1062 })
        {
            await transaction.RollbackAsync("profile_change",token);
            result=new(envelope.Id,"Rejected",ex is MySqlException?"Student ID or NFC card is already assigned.":ex.Message);
        }
        if(result.Status!="Applied")
        {
            using var audit=new MySqlCommand(@"INSERT INTO alerts(student_id,alert_type,message,device_id,device_name)
                VALUES(@student,@type,@message,@device,@name)",connection,transaction);
            audit.Parameters.AddWithValue("@student",change.StudentId);audit.Parameters.AddWithValue("@type","REMOTE_ADMIN_"+result.Status.ToUpperInvariant());
            audit.Parameters.AddWithValue("@message",$"Remote account {member.Uid} ({member.Name}), request {envelope.Id}: {result.Status}. {result.Message}");
            audit.Parameters.AddWithValue("@device",serverDevice.DeviceId);audit.Parameters.AddWithValue("@name",serverDevice.DeviceName);
            await audit.ExecuteNonQueryAsync(token);
        }
        using(var receipt=new MySqlCommand("UPDATE remote_change_receipts SET result_json=@json WHERE request_id=@id",connection,transaction))
        {receipt.Parameters.AddWithValue("@json",JsonSerializer.Serialize(result));receipt.Parameters.AddWithValue("@id",envelope.Id);await receipt.ExecuteNonQueryAsync(token);}
        await transaction.CommitAsync(token);
        return result;
    }

    internal static async Task<IReadOnlyList<RemoteChangeResult>> UnpublishedRemoteResultsAsync(MySqlConnection connection,CancellationToken token=default)
    {
        var rows=new List<RemoteChangeResult>();
        using var query=new MySqlCommand("SELECT result_json FROM remote_change_receipts WHERE published=FALSE AND result_json<>''",connection);
        using var reader=await query.ExecuteReaderAsync(token);
        while(await reader.ReadAsync(token))rows.Add(JsonSerializer.Deserialize<RemoteChangeResult>(reader.GetString(0))!);
        return rows;
    }

    internal static async Task MarkRemoteResultPublishedAsync(MySqlConnection connection,string id,CancellationToken token=default)
    {
        using var command=new MySqlCommand("UPDATE remote_change_receipts SET published=TRUE WHERE request_id=@id",connection);
        command.Parameters.AddWithValue("@id",id);await command.ExecuteNonQueryAsync(token);
    }
}
