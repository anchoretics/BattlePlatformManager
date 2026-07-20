using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using BPM.Application.Abstractions;
using BPM.Application.Models;
using BPM.Shared;
using Microsoft.Extensions.Logging;

namespace BPM.Infrastructure.Win32;

/// <summary>
/// CreateProcessWithLogonW + LOGON_WITH_PROFILE 封装:
/// 先用 LogonUserW 校验凭据并生成目标用户环境块,再在目标用户上下文中启动进程。
/// </summary>
[SupportedOSPlatform("windows")]
public class ProcessLauncherService : IProcessLauncher
{
    private readonly ILogger<ProcessLauncherService> _logger;

    public ProcessLauncherService(ILogger<ProcessLauncherService> logger)
    {
        _logger = logger;
    }

    public Result<int> Launch(LaunchRequest request)
    {
        if (!File.Exists(request.ExecutablePath))
        {
            return Result<int>.Fail(
                ErrorCode.ExecutableNotFound,
                $"找不到可执行文件:{request.ExecutablePath},请重新配置路径。");
        }

        var token = IntPtr.Zero;
        var environmentBlock = IntPtr.Zero;
        var processInfo = default(NativeMethods.PROCESS_INFORMATION);

        try
        {
            // 1. 校验凭据并获取用户令牌(用于生成目标用户的环境变量块)。
            if (!NativeMethods.LogonUserW(
                    request.User, request.Domain, request.Password,
                    NativeMethods.LOGON32_LOGON_INTERACTIVE,
                    NativeMethods.LOGON32_PROVIDER_DEFAULT,
                    out token))
            {
                var logonError = Marshal.GetLastWin32Error();
                return Result<int>.Fail(MapLogonError(logonError), LogonErrorMessage(logonError), logonError);
            }

            // 2. 基于用户令牌创建环境块;失败时降级为 NULL(由系统按用户配置文件生成)。
            if (!NativeMethods.CreateEnvironmentBlock(out environmentBlock, token, bInherit: false))
            {
                _logger.LogWarning(
                    "CreateEnvironmentBlock 失败,Win32Error {Win32Error},将使用默认环境",
                    Marshal.GetLastWin32Error());
                environmentBlock = IntPtr.Zero;
            }

            // 3. 在目标用户上下文中启动进程并加载其用户配置文件。
            var startupInfo = new NativeMethods.STARTUPINFO
            {
                cb = Marshal.SizeOf<NativeMethods.STARTUPINFO>(),
            };

            // CreateProcessWithLogonW 要求 lpCommandLine 为可写缓冲区,故使用 StringBuilder。
            var commandLine = new StringBuilder($"\"{request.ExecutablePath}\"");
            if (!string.IsNullOrWhiteSpace(request.Arguments))
            {
                commandLine.Append(' ').Append(request.Arguments);
            }

            var workingDirectory = string.IsNullOrWhiteSpace(request.WorkingDirectory)
                ? Path.GetDirectoryName(request.ExecutablePath)
                : request.WorkingDirectory;

            if (!NativeMethods.CreateProcessWithLogonW(
                    request.User,
                    request.Domain,
                    request.Password,
                    NativeMethods.LOGON_WITH_PROFILE,
                    null,
                    commandLine,
                    NativeMethods.CREATE_UNICODE_ENVIRONMENT,
                    environmentBlock,
                    workingDirectory,
                    ref startupInfo,
                    out processInfo))
            {
                var error = Marshal.GetLastWin32Error();
                _logger.LogError(
                    "CreateProcessWithLogonW 失败,用户 {Domain}\\{User},Win32Error {Win32Error}",
                    request.Domain, request.User, error);
                return Result<int>.Fail(MapLaunchError(error), LaunchErrorMessage(error), error);
            }

            return Result<int>.Ok(processInfo.dwProcessId);
        }
        finally
        {
            if (processInfo.hProcess != IntPtr.Zero)
            {
                NativeMethods.CloseHandle(processInfo.hProcess);
            }

            if (processInfo.hThread != IntPtr.Zero)
            {
                NativeMethods.CloseHandle(processInfo.hThread);
            }

            if (environmentBlock != IntPtr.Zero)
            {
                NativeMethods.DestroyEnvironmentBlock(environmentBlock);
            }

            if (token != IntPtr.Zero)
            {
                NativeMethods.CloseHandle(token);
            }
        }
    }

    private static ErrorCode MapLogonError(int error) => error switch
    {
        NativeMethods.ERROR_LOGON_FAILURE => ErrorCode.InvalidCredentials,
        NativeMethods.ERROR_ACCOUNT_RESTRICTION => ErrorCode.InvalidCredentials,
        NativeMethods.ERROR_LOGON_TYPE_NOT_GRANTED => ErrorCode.LogonNotGranted,
        _ => ErrorCode.LaunchFailed,
    };

    private static string LogonErrorMessage(int error) => error switch
    {
        NativeMethods.ERROR_LOGON_FAILURE => "Windows 用户名或密码错误,请重新编辑账户并输入正确密码。",
        NativeMethods.ERROR_ACCOUNT_RESTRICTION =>
            "账户受限(常见原因:该 Windows 用户密码为空,本地安全策略默认禁止空密码网络登录,请为其设置密码)。",
        NativeMethods.ERROR_LOGON_TYPE_NOT_GRANTED => "该用户没有交互式登录权限,请检查本地安全策略。",
        _ => $"Windows 登录校验失败(错误码 {error})。",
    };

    private static ErrorCode MapLaunchError(int error) => error switch
    {
        NativeMethods.ERROR_FILE_NOT_FOUND or NativeMethods.ERROR_PATH_NOT_FOUND => ErrorCode.ExecutableNotFound,
        NativeMethods.ERROR_LOGON_FAILURE => ErrorCode.InvalidCredentials,
        NativeMethods.ERROR_SERVICE_NOT_ACTIVE or NativeMethods.ERROR_SERVICE_DISABLED => ErrorCode.LogonServiceUnavailable,
        _ => ErrorCode.LaunchFailed,
    };

    private static string LaunchErrorMessage(int error) => error switch
    {
        NativeMethods.ERROR_FILE_NOT_FOUND or NativeMethods.ERROR_PATH_NOT_FOUND =>
            "启动器路径无效,请重新配置 Battle.net 路径。",
        NativeMethods.ERROR_LOGON_FAILURE => "Windows 用户名或密码错误。",
        NativeMethods.ERROR_SERVICE_NOT_ACTIVE or NativeMethods.ERROR_SERVICE_DISABLED =>
            "Secondary Logon(seclogon)服务未运行,请在服务管理器中启用它。",
        _ => $"启动进程失败(错误码 {error})。",
    };
}
