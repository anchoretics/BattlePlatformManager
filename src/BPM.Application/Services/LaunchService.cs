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
    private readonly IBrowserLoginAssist? _loginAssist;
    private readonly object _launchGate = new();
    private readonly Dictionary<Guid, Lazy<Result<LaunchedInstance>>> _launches = new();

    public LaunchService(
        IConfigService configService,
        ICredentialService credentialService,
        IGamePlatformAdapter platformAdapter,
        IProcessLauncher processLauncher,
        ILogger<LaunchService> logger,
        IBrowserLoginAssist? loginAssist = null)
    {
        _configService = configService;
        _credentialService = credentialService;
        _platformAdapter = platformAdapter;
        _processLauncher = processLauncher;
        _logger = logger;
        _loginAssist = loginAssist;
    }

    public Result<LaunchedInstance> Launch(Guid accountId)
    {
        Lazy<Result<LaunchedInstance>> pending;
        lock (_launchGate)
        {
            if (!_launches.TryGetValue(accountId, out pending!))
            {
                pending = new Lazy<Result<LaunchedInstance>>(() => LaunchCore(accountId),
                    LazyThreadSafetyMode.ExecutionAndPublication);
                _launches.Add(accountId, pending);
            }
        }
        // UI 和快捷方式共享同一用例实例；并发请求共用结果，完成后允许再次启动。
        try { return pending.Value; }
        finally
        {
            lock (_launchGate)
            {
                if (_launches.TryGetValue(accountId, out var current) && ReferenceEquals(current, pending))
                    _launches.Remove(accountId);
            }
        }
    }

    private Result<LaunchedInstance> LaunchCore(Guid accountId)
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

        // 必须先监听，再启动客户端，以免丢失全新用户的第一次认证请求。
        try
        {
            _loginAssist?.Start(account);
        }
        catch (Exception ex)
        {
            // 不记录异常消息：第三方浏览器组件可能在消息中包含认证参数。
            _logger.LogWarning("登录辅助启动失败，类型 {Type}，HRESULT {HResult}", ex.GetType().Name, ex.HResult);
        }

        Result<int> launch;
        try
        {
            launch = _processLauncher.Launch(request.Value);
        }
        catch (Exception ex)
        {
            StopAssist(account.Id);
            _logger.LogError("启动进程异常，类型 {Type}，HRESULT {HResult}", ex.GetType().Name, ex.HResult);
            return Result<LaunchedInstance>.Fail(ErrorCode.LaunchFailed, "启动进程未完成，请检查 Windows 用户、程序路径和 BPM 日志后重试。");
        }
        if (!launch.IsSuccess)
        {
            StopAssist(account.Id);
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

    private void StopAssist(Guid id)
    {
        try { _loginAssist?.Stop(id); }
        catch (Exception ex) { _logger.LogWarning("停止登录辅助异常，类型 {Type}，HRESULT {HResult}", ex.GetType().Name, ex.HResult); }
    }
}
