using System.ComponentModel;
using System.Runtime.InteropServices;
using RDPManager.Core.Credentials;

namespace RDPManager.App.Services;

/// <summary>
/// Windows Credential Manager (generic credentials, persisted for this user on this computer), through
/// advapi32's CredWrite, CredRead and CredDelete (DESIGN: Credentials, Windows details).
/// </summary>
public sealed class WindowsCredentialStore : ICredentialStore
{
    private const uint CredTypeGeneric = 1;
    private const uint CredPersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;

    public string? Read(string target)
    {
        if (!CredReadW(target, CredTypeGeneric, 0, out var pointer))
        {
            var error = Marshal.GetLastWin32Error();
            return error == ErrorNotFound ? null : throw Failure("read", target, error);
        }

        try
        {
            var credential = Marshal.PtrToStructure<Credential>(pointer);
            return credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0
                ? string.Empty
                : Marshal.PtrToStringUni(credential.CredentialBlob, (int)credential.CredentialBlobSize / 2);
        }
        finally
        {
            CredFree(pointer);
        }
    }

    public void Write(string target, string secret, string comment)
    {
        ArgumentNullException.ThrowIfNull(secret);
        var blob = Marshal.StringToCoTaskMemUni(secret);
        try
        {
            var credential = new Credential
            {
                Type = CredTypeGeneric,
                TargetName = target,
                Comment = comment,
                CredentialBlobSize = (uint)(secret.Length * 2),
                CredentialBlob = blob,
                Persist = CredPersistLocalMachine,
                UserName = "RDPManager",
            };

            if (!CredWriteW(ref credential, 0))
            {
                throw Failure("store", target, Marshal.GetLastWin32Error());
            }
        }
        finally
        {
            Marshal.ZeroFreeCoTaskMemUnicode(blob);
        }
    }

    public bool Delete(string target)
    {
        if (CredDeleteW(target, CredTypeGeneric, 0))
        {
            return true;
        }

        var error = Marshal.GetLastWin32Error();
        return error == ErrorNotFound ? false : throw Failure("remove", target, error);
    }

    private static CredentialStoreException Failure(string action, string target, int error) =>
        new($"Windows Credential Manager could not {action} \"{target}\": {new Win32Exception(error).Message}", new Win32Exception(error));

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public uint Flags;
        public uint Type;
        public string? TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string? UserName;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWriteW(ref Credential credential, uint flags);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredReadW(string target, uint type, uint flags, out IntPtr credential);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDeleteW(string target, uint type, uint flags);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr buffer);
}
