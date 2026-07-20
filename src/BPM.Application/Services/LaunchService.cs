using BPM.Application.Abstractions;
using BPM.Application.Models;
using BPM.Shared;
using Microsoft.Extensions.Logging;

namespace BPM.Application.Services;

/// <summary>核心启动用例:配置 → 凭据 → 平台适配器 → CreateProcessWithLogonW。</summary>
public class LaunchService
{
    private readonly IConfigService _configService;
    private readonly ICredentialService _credentialService;
    private readonly IGamePlatformAdapter _platformAdapter;
    private readonly IProcessLauncher _processLauncher;
    private readonly ILogger<LaunchService> _logger;

    public LaunchService(
        IConfigService configService,
        ICredentialService credentialService,
        IGamePlatformAdapter platformAdapter,
        IProcessLauncher processLauncher,
        ILogger<LaunchService> logger)
    {
        _configService = configService;
        _credentialService = credentialService;
        _platformAdapter = platformAdapter;
        _processLauncher = processLauncher;
        _logger = logger;
    }

    public Result<LaunchedInstance> Launch(Guid accountId)
    {
        var configResult = _configService.Load();
        if (!configResult.IsSuccess)
        {
            return Result<LaunchedInstance>.Fail(configResult);
        }

        var account = configResult.Value.Accounts.FirstOrDefault(a => a.Id == accountId);
        if (account is null)
        {
            return Result<LaunchedInstance>.Fail(ErrorCode.AccountNotFound, $"找不到账户 {accountId}。");
        }

        var installation = _platformAdapter.ValidateInstallation(account);
        if (!installation.IsSuccess)
        {
            _logger.LogWarning("账户 {DisplayName} 安装校验失败:{Message}", account.DisplayName, installation.Message);
            return Result<LaunchedInstance>.Fail(installation);
        }

        var credential = _credentialService.Get(account.CredentialKey);
        if (!credential.IsSuccess)
        {
            _logger.LogWarning("账户 {DisplayName} 凭据缺失(键 {CredentialKey})", account.DisplayName, account.CredentialKey);
            return Result<LaunchedInstance>.Fail(credential);
        }

        var request = _platformAdapter.BuildLaunchRequest(account, credential.Value);
        if (!request.IsSuccess)
        {
            return Result<LaunchedInstance>.Fail(request);
        }

        var launch = _processLauncher.Launch(request.Value);
        if (!launch.IsSuccess)
        {
            _logger.LogError(
                "启动失败:账户 {DisplayName},错误 {Code},Win32Error {Win32Error}",
                account.DisplayName, launch.Code, launch.Win32Error);
            return Result<LaunchedInstance>.Fail(launch);
        }

        _logger.LogInformation(
            "已为账户 {DisplayName} 启动 {Platform},PID {ProcessId},Windows 用户 {WindowsUser}",
            account.DisplayName, _platformAdapter.PlatformName, launch.Value, account.WindowsUser);

        return Result<LaunchedInstance>.Ok(new LaunchedInstance(account.Id, launch.Value));
    }
}
