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
/// 由系统加载目标用户配置文件并生成环境,避免首次启动时使用尚未加载的用户环境。
/// </summary>
[SupportedOSPlatform("windows")]
public class ProcessLauncherService : IProcessLauncher
{
    private readonly ILogger<ProcessLauncherService> _logger;
    private readonly IProcessCreationApi _processApi;

    public ProcessLauncherService(ILogger<ProcessLauncherService> logger)
        : this(logger, new WindowsProcessCreationApi())
    {
    }

    internal ProcessLauncherService(ILogger<ProcessLauncherService> logger, IProcessCreationApi processApi)
    {
        _logger = logger;
        _processApi = processApi;
    }

    public Result<int> Launch(LaunchRequest request)
    {
        if (!File.Exists(request.ExecutablePath))
        {
            return Result<int>.Fail(
                ErrorCode.ExecutableNotFound,
                $"找不到可执行文件:{request.ExecutablePath},请重新配置路径。");
        }

        var processInfo = default(NativeMethods.PROCESS_INFORMATION);

        try
        {
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

            // 不在加载用户配置文件前调用 CreateEnvironmentBlock。开机后目标用户的
            // 注册表配置可能尚未加载,提前生成的环境会缺少用户专属变量。
            // LOGON_WITH_PROFILE + NULL 环境让 Windows 从目标用户配置生成完整环境,
            // 同时完成凭据校验;也不会继承以管理员身份运行的 BPM 的环境。
            if (!_processApi.CreateProcessWithLogon(
                    request.User,
                    request.Domain,
                    request.Password,
                    NativeMethods.LOGON_WITH_PROFILE,
                    request.ExecutablePath,
                    commandLine,
                    NativeMethods.CREATE_UNICODE_ENVIRONMENT,
                    IntPtr.Zero,
                    workingDirectory,
                    ref startupInfo,
                    out processInfo))
            {
                var error = _processApi.GetLastError();
                _logger.LogError(
                    "CreateProcessWithLogonW 失败,用户 {Domain}\\{User},Win32Error {Win32Error}",
                    request.Domain, request.User, error);
                return Result<int>.Fail(MapLaunchError(error), LaunchErrorMessage(error), error);
            }

            _logger.LogInformation(
                "已使用系统生成的目标用户配置环境启动进程,用户 {Domain}\\{User},PID {ProcessId}",
                request.Domain, request.User, processInfo.dwProcessId);
            return Result<int>.Ok(processInfo.dwProcessId);
        }
        finally
        {
            if (processInfo.hProcess != IntPtr.Zero)
            {
                _processApi.CloseHandle(processInfo.hProcess);
            }

            if (processInfo.hThread != IntPtr.Zero)
            {
                _processApi.CloseHandle(processInfo.hThread);
            }
        }
    }

    private static ErrorCode MapLaunchError(int error) => error switch
    {
        NativeMethods.ERROR_FILE_NOT_FOUND or NativeMethods.ERROR_PATH_NOT_FOUND => ErrorCode.ExecutableNotFound,
        NativeMethods.ERROR_LOGON_FAILURE => ErrorCode.InvalidCredentials,
        NativeMethods.ERROR_ACCOUNT_RESTRICTION => ErrorCode.InvalidCredentials,
        NativeMethods.ERROR_LOGON_TYPE_NOT_GRANTED => ErrorCode.LogonNotGranted,
        NativeMethods.ERROR_SERVICE_NOT_ACTIVE or NativeMethods.ERROR_SERVICE_DISABLED => ErrorCode.LogonServiceUnavailable,
        _ => ErrorCode.LaunchFailed,
    };

    private static string LaunchErrorMessage(int error) => error switch
    {
        NativeMethods.ERROR_FILE_NOT_FOUND or NativeMethods.ERROR_PATH_NOT_FOUND =>
            "启动器路径无效,请重新配置 Battle.net 路径。",
        NativeMethods.ERROR_LOGON_FAILURE => "Windows 用户名或密码错误,请重新编辑账户并输入正确密码。",
        NativeMethods.ERROR_ACCOUNT_RESTRICTION =>
            "账户受限(常见原因:该 Windows 用户密码为空,请为其设置密码并检查本地安全策略)。",
        NativeMethods.ERROR_LOGON_TYPE_NOT_GRANTED => "该用户没有交互式登录权限,请检查本地安全策略。",
        NativeMethods.ERROR_SERVICE_NOT_ACTIVE or NativeMethods.ERROR_SERVICE_DISABLED =>
            "Secondary Logon(seclogon)服务未运行,请在服务管理器中启用它。",
        _ => $"启动进程失败(错误码 {error})。",
    };
}
