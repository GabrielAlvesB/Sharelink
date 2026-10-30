using System.Runtime.InteropServices;
using System.Text;
using FILETIME = System.Runtime.InteropServices.ComTypes.FILETIME;

namespace Sharelink.Upload;

/// <summary>
/// Guarda as chaves dos destinos de upload no Gerenciador de Credenciais do Windows
/// (criptografado pelo próprio Windows, acessível só pelo seu usuário).
/// </summary>
internal static class CredentialStore
{
    public const string R2Target = "Sharelink/R2";
    public const string S3Target = "Sharelink/S3";
    public const string ImgurTarget = "Sharelink/Imgur";
    public const string WebDavTarget = "Sharelink/WebDAV";
    public const string SupabaseTarget = "Sharelink/Supabase";

    private const uint CRED_TYPE_GENERIC = 1;
    private const uint CRED_PERSIST_LOCAL_MACHINE = 2;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL
    {
        public uint Flags;
        public uint Type;
        public string TargetName;
        public string? Comment;
        public FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(ref CREDENTIAL credential, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string target, uint type, uint reservedFlag, out IntPtr credential);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(string target, uint type, uint flags);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr buffer);

    public static void Save(string target, string userName, string secret)
    {
        byte[] blob = Encoding.Unicode.GetBytes(secret);
        IntPtr buffer = Marshal.AllocHGlobal(blob.Length);
        try
        {
            Marshal.Copy(blob, 0, buffer, blob.Length);
            var credential = new CREDENTIAL
            {
                Type = CRED_TYPE_GENERIC,
                TargetName = target,
                UserName = userName,
                CredentialBlob = buffer,
                CredentialBlobSize = (uint)blob.Length,
                Persist = CRED_PERSIST_LOCAL_MACHINE,
            };
            if (!CredWrite(ref credential, 0))
                throw new InvalidOperationException($"Não foi possível salvar a credencial (erro {Marshal.GetLastWin32Error()}).");
        }
        finally
        {
            // Apaga a cópia do segredo da memória antes de liberar.
            Marshal.Copy(new byte[blob.Length], 0, buffer, blob.Length);
            Marshal.FreeHGlobal(buffer);
            Array.Clear(blob);
        }
    }

    /// <returns>(usuário, segredo) ou null se não existir.</returns>
    public static (string UserName, string Secret)? Load(string target)
    {
        if (!CredRead(target, CRED_TYPE_GENERIC, 0, out IntPtr pointer))
            return null;
        try
        {
            CREDENTIAL credential = Marshal.PtrToStructure<CREDENTIAL>(pointer);
            string secret = credential.CredentialBlobSize == 0
                ? ""
                : Marshal.PtrToStringUni(credential.CredentialBlob, (int)credential.CredentialBlobSize / 2);
            return (credential.UserName ?? "", secret);
        }
        finally
        {
            CredFree(pointer);
        }
    }

    public static void Delete(string target) => CredDelete(target, CRED_TYPE_GENERIC, 0);
}
