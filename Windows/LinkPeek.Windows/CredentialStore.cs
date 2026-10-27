using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace LinkPeek.Windows;

internal static class CredentialStore
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public uint Flags, Type;
        public string TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist, AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Write(ref Credential credential, uint flags);
    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Read(string target, uint type, uint flags, out IntPtr credential);
    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Delete(string target, uint type, uint flags);
    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr credential);

    private static string Target(string organization)
    {
        if (!PullRequestLink.ValidOrganization(organization)) throw new UserError("Enter an organization name, not a URL.");
        return "LinkPeek:AzureDevOps:" + organization.ToLowerInvariant();
    }

    public static string? Load(string organization)
    {
        if (!Read(Target(organization), 1, 0, out var pointer))
        {
            if (Marshal.GetLastWin32Error() == 1168) return null;
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        try
        {
            var data = Marshal.PtrToStructure<Credential>(pointer);
            return Marshal.PtrToStringUni(data.CredentialBlob, checked((int)data.CredentialBlobSize / 2));
        }
        finally { CredFree(pointer); }
    }

    public static bool Contains(string organization)
    {
        if (!Read(Target(organization), 1, 0, out var pointer))
        {
            if (Marshal.GetLastWin32Error() == 1168) return false;
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        CredFree(pointer);
        return true;
    }

    public static void Save(string organization, string token)
    {
        var target = Target(organization);
        if (string.IsNullOrWhiteSpace(token) || Encoding.Unicode.GetByteCount(token) > 2560)
            throw new UserError("Enter a valid PAT.");
        var pointer = Marshal.StringToCoTaskMemUni(token);
        try
        {
            var data = new Credential
            {
                Type = 1, TargetName = target, UserName = organization.ToLowerInvariant(),
                CredentialBlobSize = (uint)Encoding.Unicode.GetByteCount(token), CredentialBlob = pointer,
                Persist = 2 // Local machine, current Windows user. No roaming.
            };
            if (!Write(ref data, 0)) throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        finally { Marshal.ZeroFreeCoTaskMemUnicode(pointer); }
    }

    public static void Remove(string organization)
    {
        if (!Delete(Target(organization), 1, 0) && Marshal.GetLastWin32Error() != 1168)
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }
}
