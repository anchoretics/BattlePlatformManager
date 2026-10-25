using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text;

namespace BPM.Infrastructure.Win32;

[SupportedOSPlatform("windows")]
internal interface IWindowsUserApi
{
    bool IsAdministrator { get; }
    int AddUser(string userName, string password);
    int GetUserSid(string userName, out string sid);
    int AddToUsersGroup(string userName);
    int CreateProfile(string sid, string userName);
}

[SupportedOSPlatform("windows")]
internal sealed class WindowsUserApi : IWindowsUserApi
{
    public bool IsAdministrator
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    public int AddUser(string userName, string password)
    {
        var info = new NativeMethods.USER_INFO_1
        {
            usri1_name = userName,
            usri1_password = password,
            usri1_priv = NativeMethods.USER_PRIV_USER,
            usri1_comment = "BPM 创建的战网独立用户",
            usri1_flags = NativeMethods.UF_SCRIPT | NativeMethods.UF_NORMAL_ACCOUNT,
        };
        return NativeMethods.NetUserAdd(null, 1, ref info, out _);
    }

    public int GetUserSid(string userName, out string sid)
    {
        sid = string.Empty;
        var buffer = IntPtr.Zero;
        try
        {
            var error = NativeMethods.NetUserGetInfo(null, userName, 23, out buffer);
            if (error != 0)
            {
                return error;
            }

            var info = Marshal.PtrToStructure<NativeMethods.USER_INFO_23>(buffer);
            sid = new SecurityIdentifier(info.usri23_user_sid).Value;
            return 0;
        }
        finally
        {
            if (buffer != IntPtr.Zero)
            {
                NativeMethods.NetApiBufferFree(buffer);
            }
        }
    }

    public int AddToUsersGroup(string userName)
    {
        // 通过内置 SID 查找本机组名,兼容中文版和其他语言的 Windows。
        var usersSid = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
        var usersAccount = (NTAccount)usersSid.Translate(typeof(NTAccount));
        var groupName = usersAccount.Value[(usersAccount.Value.LastIndexOf('\\') + 1)..];
        var member = new NativeMethods.LOCALGROUP_MEMBERS_INFO_3
        {
            lgrmi3_domainandname = $"{Environment.MachineName}\\{userName}",
        };
        return NativeMethods.NetLocalGroupAddMembers(null, groupName, 3, ref member, 1);
    }

    public int CreateProfile(string sid, string userName)
    {
        var profilePath = new StringBuilder(1024);
        return NativeMethods.CreateProfile(sid, userName, profilePath, (uint)profilePath.Capacity);
    }
}
