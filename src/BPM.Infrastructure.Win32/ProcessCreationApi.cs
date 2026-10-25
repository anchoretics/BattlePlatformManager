using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace BPM.Infrastructure.Win32;

/// <summary>进程创建的原生边界,便于在无需真实用户密码的情况下验证启动契约。</summary>
[SupportedOSPlatform("windows")]
internal interface IProcessCreationApi
{
    bool CreateProcessWithLogon(
        string user,
        string domain,
        string password,
        int logonFlags,
        string applicationName,
        StringBuilder commandLine,
        int creationFlags,
        IntPtr environment,
        string? workingDirectory,
        ref NativeMethods.STARTUPINFO startupInfo,
        out NativeMethods.PROCESS_INFORMATION processInfo);

    int GetLastError();

    void CloseHandle(IntPtr handle);
}

[SupportedOSPlatform("windows")]
internal sealed class WindowsProcessCreationApi : IProcessCreationApi
{
    public bool CreateProcessWithLogon(
        string user,
        string domain,
        string password,
        int logonFlags,
        string applicationName,
        StringBuilder commandLine,
        int creationFlags,
        IntPtr environment,
        string? workingDirectory,
        ref NativeMethods.STARTUPINFO startupInfo,
        out NativeMethods.PROCESS_INFORMATION processInfo) =>
        NativeMethods.CreateProcessWithLogonW(
            user, domain, password, logonFlags, applicationName, commandLine,
            creationFlags, environment, workingDirectory, ref startupInfo, out processInfo);

    public int GetLastError() => Marshal.GetLastWin32Error();

    public void CloseHandle(IntPtr handle) => NativeMethods.CloseHandle(handle);
}
