using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Principal;

namespace BPM.Infrastructure.Win32;

public enum StartupElevationState { Continue, Relaunched, Cancelled, Failed }

public sealed record StartupElevationResult(StartupElevationState State, string Message = "");

/// <summary>在加载配置/凭据前提升 BPM 主进程；浏览器助手必须继续使用配置用户的普通令牌。</summary>
[SupportedOSPlatform("windows")]
public sealed class StartupElevationService
{
    internal const string OriginArgument = "--bpm-elevation-user";
    private readonly IStartupElevationApi _api;

    public StartupElevationService() : this(new WindowsStartupElevationApi()) { }
    internal StartupElevationService(IStartupElevationApi api) => _api = api;

    public StartupElevationResult EnsureAdministrator(string executable, IReadOnlyList<string> arguments)
    {
        // 双重保护：目标用户助手不能因主程序新增提升要求而失败/弹出 UAC。
        if (arguments.Contains("--browser-login-pipe", StringComparer.Ordinal)) return new(StartupElevationState.Continue);
        try
        {
            var originIndex = -1;
            for (var index = 0; index < arguments.Count; index++)
            {
                if (arguments[index] != OriginArgument) continue;
                if (originIndex >= 0) return Failed("管理员启动参数无效，请重新打开 BPM。");
                originIndex = index;
            }
            if (originIndex >= 0)
            {
                if (originIndex != arguments.Count - 2) return Failed("管理员启动参数无效，请重新打开 BPM。");
                if (!string.Equals(arguments[originIndex + 1], _api.CurrentUserSid, StringComparison.Ordinal))
                    return Failed("Windows 授权使用了另一个账号，无法访问原用户的配置和凭据。请使用原 Windows 账号提升权限后打开 BPM。");
                if (!_api.IsAdministrator) return Failed("提升后仍未获得管理员权限，请检查 Windows 授权或系统策略。BPM 已停止启动，避免重复请求授权。");
            }
            if (_api.IsAdministrator) return new(StartupElevationState.Continue);

            var start = new ProcessStartInfo(executable)
            {
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System),
            };
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            start.ArgumentList.Add(OriginArgument);
            start.ArgumentList.Add(_api.CurrentUserSid);
            return _api.StartElevated(start)
                ? new(StartupElevationState.Relaunched)
                : Failed("Windows 未能启动管理员进程，请重新打开 BPM。");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return new(StartupElevationState.Cancelled);
        }
        catch (Win32Exception ex)
        {
            return Failed($"请求管理员权限失败（Windows 错误 {ex.NativeErrorCode}）。请检查程序路径和系统策略。");
        }
        catch (Exception ex)
        {
            return Failed($"请求管理员权限失败（HRESULT 0x{ex.HResult:X8}）。请重新打开 BPM。");
        }
    }

    private static StartupElevationResult Failed(string message) => new(StartupElevationState.Failed, message);
}

[SupportedOSPlatform("windows")]
internal interface IStartupElevationApi
{
    bool IsAdministrator { get; }
    string CurrentUserSid { get; }
    bool StartElevated(ProcessStartInfo start);
}

[SupportedOSPlatform("windows")]
internal sealed class WindowsStartupElevationApi : IStartupElevationApi
{
    public bool IsAdministrator
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    public string CurrentUserSid
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return identity.User?.Value ?? throw new InvalidOperationException("No Windows identity.");
        }
    }

    public bool StartElevated(ProcessStartInfo start)
    {
        using var process = Process.Start(start);
        return process is not null;
    }
}
