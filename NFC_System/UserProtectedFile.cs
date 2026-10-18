using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace NFC_System;

// DPAPI binds queued records and the server decryption key to this Windows account.
internal static class UserProtectedFile
{
    [StructLayout(LayoutKind.Sequential)] private struct Blob { public int Length; public IntPtr Data; }
    [DllImport("crypt32.dll",SetLastError=true,CharSet=CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref Blob input, string? description, IntPtr entropy, IntPtr reserved,
        IntPtr prompt, uint flags, out Blob output);
    [DllImport("crypt32.dll",SetLastError=true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy, IntPtr reserved,
        IntPtr prompt, uint flags, out Blob output);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);

    internal static byte[] Transform(byte[] bytes, bool protect)
    {
        var input = new Blob { Length=bytes.Length, Data=Marshal.AllocHGlobal(bytes.Length) };
        Blob output = default;
        try
        {
            Marshal.Copy(bytes,0,input.Data,bytes.Length);
            bool success = protect ? CryptProtectData(ref input,null,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,1,out output)
                : CryptUnprotectData(ref input,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,1,out output);
            if (!success) throw new CryptographicException("Protected local storage is unavailable.",new Win32Exception(Marshal.GetLastWin32Error()));
            byte[] result = new byte[output.Length];
            Marshal.Copy(output.Data,result,0,result.Length);
            return result;
        }
        finally
        {
            for (int i=0;i<input.Length;i++) Marshal.WriteByte(input.Data,i,0);
            Marshal.FreeHGlobal(input.Data);
            if (output.Data != IntPtr.Zero)
            {
                for (int i=0;i<output.Length;i++) Marshal.WriteByte(output.Data,i,0);
                LocalFree(output.Data);
            }
        }
    }

    internal static T? Read<T>(string path)
    {
        if(!File.Exists(path))return default;
        byte[] plain=Transform(File.ReadAllBytes(path),false);
        try{return JsonSerializer.Deserialize<T>(plain);}
        finally{CryptographicOperations.ZeroMemory(plain);}
    }
    internal static void Save<T>(string path, T value, bool overwrite = true)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        byte[] plain = JsonSerializer.SerializeToUtf8Bytes(value);
        string temporary = path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try { File.WriteAllBytes(temporary,Transform(plain,true)); File.Move(temporary,path,overwrite); }
        finally { CryptographicOperations.ZeroMemory(plain); if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

internal sealed class RemoteChangeQueue(string directory, string site, string uid)
{
    private readonly object _lock = new();
    private string PathName => Path.Combine(directory,site,Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(uid)))+".queue");
    internal RemoteQueuedChange[] Snapshot()
    {
        lock(_lock){using var lease=AcquireLease();return UserProtectedFile.Read<RemoteQueuedChange[]>(PathName)??[];}
    }
    private FileStream AcquireLease()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PathName)!);
        for(int attempt=0;attempt<100;attempt++)
        {
            try{return new FileStream(PathName+".lock",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);}
            catch(IOException){System.Threading.Thread.Sleep(10);}
        }
        throw new InvalidOperationException("Another app instance is updating this account's queue. Try again.");
    }
    internal void Put(RemoteQueuedChange row)
    {
        if (row.Change.SiteId != site || row.Change.OwnerUid != uid) throw new InvalidOperationException("Queue account mismatch.");
        lock (_lock)
        {
            using var lease=AcquireLease();
            var rows = new System.Collections.Generic.List<RemoteQueuedChange>(UserProtectedFile.Read<RemoteQueuedChange[]>(PathName)??[]);
            int index = rows.FindIndex(r => r.Change.Id == row.Change.Id);
            if (index >= 0 && rows[index].Change != row.Change) throw new InvalidOperationException("A queued request cannot be replaced.");
            if(index>=0&&rows[index].Status is "Applied" or "Conflict" or "Rejected")return;
            if(index>=0&&rows[index].Status=="Pending"&&row.Status=="Queued")return;
            if (index < 0) rows.Add(row); else rows[index] = row;
            UserProtectedFile.Save(PathName,rows);
        }
    }
}
