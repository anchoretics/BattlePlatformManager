using BPM.Domain.Models;
using BPM.Shared;

namespace BPM.Domain.Validation;

/// <summary>账户配置档的领域规则校验(纯规则,不做文件存在性等 IO 检查)。</summary>
public static class AccountValidator
{
    public static Result Validate(Account account)
    {
        if (account.Id == Guid.Empty)
        {
            return Result.Fail(ErrorCode.ValidationFailed, "账户 Id 不能为空。");
        }

        if (string.IsNullOrWhiteSpace(account.DisplayName))
        {
            return Result.Fail(ErrorCode.ValidationFailed, "显示名不能为空。");
        }

        if (string.IsNullOrWhiteSpace(account.WindowsUser))
        {
            return Result.Fail(ErrorCode.ValidationFailed, "必须绑定一个 Windows 用户名。");
        }

        if (account.WindowsUser.Count(c => c == '\\') > 1)
        {
            return Result.Fail(ErrorCode.ValidationFailed, "Windows 用户名格式无效,应为 \"user\" 或 \"DOMAIN\\user\"。");
        }

        if (string.IsNullOrWhiteSpace(account.BattleNetPath))
        {
            return Result.Fail(ErrorCode.ValidationFailed, "必须配置 Battle.net 启动器路径。");
        }

        if (!account.BattleNetPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return Result.Fail(ErrorCode.ValidationFailed, "Battle.net 路径必须指向一个 .exe 文件。");
        }

        if (string.IsNullOrWhiteSpace(account.CredentialKey))
        {
            return Result.Fail(ErrorCode.ValidationFailed, "凭据键不能为空。");
        }

        return Result.Ok();
    }

    /// <summary>把 WindowsUser 拆分为 (domain, user)。无域名时返回本机域 "."。</summary>
    public static (string Domain, string User) SplitWindowsUser(string windowsUser)
    {
        var separator = windowsUser.IndexOf('\\');
        return separator < 0
            ? (".", windowsUser)
            : (windowsUser[..separator], windowsUser[(separator + 1)..]);
    }

    /// <summary>由 Windows 用户名派生默认凭据键,例如 WoW1 → BPM_WoW1。</summary>
    public static string DeriveCredentialKey(string windowsUser)
    {
        var (_, user) = SplitWindowsUser(windowsUser);
        return $"BPM_{user}";
    }
}
