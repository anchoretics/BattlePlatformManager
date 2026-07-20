using BPM.Application.Abstractions;
using BPM.Application.Models;
using BPM.Domain.Models;
using BPM.Domain.Validation;
using BPM.Shared;

namespace BPM.Platform.BattleNet;

/// <summary>
/// Battle.net 平台适配器:校验安装路径并构造启动请求。
/// 仅通过官方命令行参数与启动器交互,不修改任何游戏文件。
/// </summary>
public class BattleNetAdapter : IGamePlatformAdapter
{
    public string PlatformName => "Battle.net";

    /// <summary>常见默认安装位置,供 UI“自动检测”使用。</summary>
    public static IReadOnlyList<string> WellKnownInstallPaths { get; } = new[]
    {
        @"C:\Program Files (x86)\Battle.net\Battle.net Launcher.exe",
        @"C:\Program Files\Battle.net\Battle.net Launcher.exe",
    };

    public Result ValidateInstallation(Account account)
    {
        if (string.IsNullOrWhiteSpace(account.BattleNetPath) || !File.Exists(account.BattleNetPath))
        {
            return Result.Fail(
                ErrorCode.ExecutableNotFound,
                $"Battle.net 启动器不存在:{account.BattleNetPath},请重新配置路径。");
        }

        if (!string.IsNullOrWhiteSpace(account.WowPath) && !Directory.Exists(account.WowPath))
        {
            return Result.Fail(
                ErrorCode.ExecutableNotFound,
                $"《魔兽世界》目录不存在:{account.WowPath},请重新配置路径。");
        }

        return Result.Ok();
    }

    public Result<LaunchRequest> BuildLaunchRequest(Account account, StoredCredential credential)
    {
        var (domain, user) = AccountValidator.SplitWindowsUser(account.WindowsUser);

        if (string.IsNullOrEmpty(user))
        {
            return Result<LaunchRequest>.Fail(ErrorCode.ValidationFailed, "Windows 用户名无效。");
        }

        // "--exec=launch WoW" 是 Battle.net 启动器公开支持的命令行指令,
        // 用于在启动器就绪后自动启动指定游戏,不涉及任何游戏文件修改。
        var arguments = account.AutoLaunchWow ? "--exec=\"launch WoW\"" : string.Empty;

        return Result<LaunchRequest>.Ok(new LaunchRequest
        {
            User = user,
            Domain = domain,
            Password = credential.Password,
            ExecutablePath = account.BattleNetPath,
            Arguments = arguments,
            WorkingDirectory = Path.GetDirectoryName(account.BattleNetPath) ?? string.Empty,
        });
    }
}
