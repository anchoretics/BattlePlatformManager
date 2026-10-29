using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace BPM.Infrastructure.Win32;

/// <summary>advapi32 / kernel32 / netapi32 / userenv P/Invoke 声明。仅封装官方文档化 API。</summary>
[SupportedOSPlatform("windows")]
internal static class NativeMethods
{
    internal const int LOGON_WITH_PROFILE = 0x00000001;
    internal const int CREATE_UNICODE_ENVIRONMENT = 0x00000400;

    internal const uint CRED_TYPE_GENERIC = 1;
    internal const uint CRED_PERSIST_LOCAL_MACHINE = 2;
    internal const uint USER_PRIV_USER = 1;
    internal const uint UF_SCRIPT = 0x0001;
    internal const uint UF_NORMAL_ACCOUNT = 0x0200;

    internal const int ERROR_FILE_NOT_FOUND = 2;
    internal const int ERROR_PATH_NOT_FOUND = 3;
    internal const int ERROR_LOGON_FAILURE = 1326;
    internal const int ERROR_ACCOUNT_RESTRICTION = 1327;
    internal const int ERROR_LOGON_TYPE_NOT_GRANTED = 1385;
    internal const int ERROR_SERVICE_NOT_ACTIVE = 1062;
    internal const int ERROR_SERVICE_DISABLED = 1058;
    internal const int ERROR_NOT_FOUND = 1168;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct STARTUPINFO
    {
        public int cb;
        public IntPtr lpReserved;
        public IntPtr lpDesktop;
        public IntPtr lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct CREDENTIALW
    {
        public uint Flags;
        public uint Type;
        [MarshalAs(UnmanagedType.LPWStr)] public string TargetName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        [MarshalAs(UnmanagedType.LPWStr)] public string? TargetAlias;
        [MarshalAs(UnmanagedType.LPWStr)] public string? UserName;
    }

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern bool CreateProcessWithLogonW(
        string lpUsername,
        string? lpDomain,
        string lpPassword,
        int dwLogonFlags,
        string? lpApplicationName,
        StringBuilder lpCommandLine,
        int dwCreationFlags,
        IntPtr lpEnvironment,
        string? lpCurrentDirectory,
        ref STARTUPINFO lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool CloseHandle(IntPtr hObject);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern bool CredWriteW(ref CREDENTIALW credential, uint flags);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern bool CredReadW(string targetName, uint type, uint flags, out IntPtr credentialPtr);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern bool CredDeleteW(string targetName, uint type, uint flags);

    [DllImport("advapi32.dll")]
    internal static extern void CredFree(IntPtr buffer);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct USER_INFO_1
    {
        [MarshalAs(UnmanagedType.LPWStr)] public string usri1_name;
        [MarshalAs(UnmanagedType.LPWStr)] public string usri1_password;
        public uint usri1_password_age;
        public uint usri1_priv;
        [MarshalAs(UnmanagedType.LPWStr)] public string? usri1_home_dir;
        [MarshalAs(UnmanagedType.LPWStr)] public string? usri1_comment;
        public uint usri1_flags;
        [MarshalAs(UnmanagedType.LPWStr)] public string? usri1_script_path;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct USER_INFO_23
    {
        public IntPtr usri23_name;
        public IntPtr usri23_full_name;
        public IntPtr usri23_comment;
        public uint usri23_flags;
        public IntPtr usri23_user_sid;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct LOCALGROUP_MEMBERS_INFO_3
    {
        [MarshalAs(UnmanagedType.LPWStr)] public string lgrmi3_domainandname;
    }

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    internal static extern int NetUserAdd(string? serverName, uint level, ref USER_INFO_1 info, out uint parameterError);

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    internal static extern int NetUserGetInfo(string? serverName, string userName, uint level, out IntPtr buffer);

    [DllImport("netapi32.dll", ExactSpelling = true)]
    internal static extern int NetApiBufferFree(IntPtr buffer);

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    internal static extern int NetLocalGroupAddMembers(
        string? serverName, string groupName, uint level, ref LOCALGROUP_MEMBERS_INFO_3 member, uint totalEntries);

    [DllImport("userenv.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    internal static extern int CreateProfile(string userSid, string userName, [Out] StringBuilder profilePath, uint profilePathLength);
}
