using BPM.Application.Abstractions;
using BPM.Domain.Models;
using BPM.Domain.Validation;
using BPM.Shared;
using Microsoft.Extensions.Logging;

namespace BPM.Application.Services;

/// <summary>账户配置档的增删改查用例。密码写入凭据库,配置文件只存凭据键。</summary>
public class AccountService
{
    private readonly IConfigService _configService;
    private readonly ICredentialService _credentialService;
    private readonly ILogger<AccountService> _logger;

    public AccountService(
        IConfigService configService,
        ICredentialService credentialService,
        ILogger<AccountService> logger)
    {
        _configService = configService;
        _credentialService = credentialService;
        _logger = logger;
    }

    public Result<Config> GetConfig() => _configService.Load();

    public Result<IReadOnlyList<Account>> GetAccounts()
    {
        var config = _configService.Load();
        return config.IsSuccess
            ? Result<IReadOnlyList<Account>>.Ok(config.Value.Accounts)
            : Result<IReadOnlyList<Account>>.Fail(config);
    }

    /// <summary>新增或更新配置档。password 为 null 表示保留已有凭据不变。</summary>
    public Result SaveAccount(Account account, string? password)
    {
        if (string.IsNullOrWhiteSpace(account.CredentialKey) && !string.IsNullOrWhiteSpace(account.WindowsUser))
        {
            account.CredentialKey = AccountValidator.DeriveCredentialKey(account.WindowsUser);
        }

        var validation = AccountValidator.Validate(account);
        if (!validation.IsSuccess)
        {
            return validation;
        }

        var configResult = _configService.Load();
        if (!configResult.IsSuccess)
        {
            return configResult;
        }

        var config = configResult.Value;
        var existingIndex = config.Accounts.FindIndex(a => a.Id == account.Id);

        if (password is not null)
        {
            var (_, user) = AccountValidator.SplitWindowsUser(account.WindowsUser);
            var credResult = _credentialService.Save(account.CredentialKey, user, password);
            if (!credResult.IsSuccess)
            {
                return credResult;
            }
        }
        else if (!_credentialService.Exists(account.CredentialKey))
        {
            return Result.Fail(ErrorCode.CredentialNotFound, "尚未为该 Windows 用户保存密码,请输入密码。");
        }

        if (existingIndex >= 0)
        {
            config.Accounts[existingIndex] = account;
        }
        else
        {
            config.Accounts.Add(account);
        }

        _logger.LogInformation("已保存账户 {DisplayName}({AccountId})", account.DisplayName, account.Id);
        return _configService.Save(config);
    }

    /// <summary>删除配置档;若无其他账户共用同一凭据键,则一并删除凭据。</summary>
    public Result DeleteAccount(Guid accountId)
    {
        var configResult = _configService.Load();
        if (!configResult.IsSuccess)
        {
            return configResult;
        }

        var config = configResult.Value;
        var account = config.Accounts.FirstOrDefault(a => a.Id == accountId);
        if (account is null)
        {
            return Result.Fail(ErrorCode.AccountNotFound, "找不到指定的账户配置档。");
        }

        config.Accounts.Remove(account);

        var credentialStillUsed = config.Accounts.Any(a => a.CredentialKey == account.CredentialKey);
        if (!credentialStillUsed && _credentialService.Exists(account.CredentialKey))
        {
            _credentialService.Delete(account.CredentialKey);
        }

        _logger.LogInformation("已删除账户 {DisplayName}({AccountId})", account.DisplayName, accountId);
        return _configService.Save(config);
    }

    public Result Export(string targetPath) => _configService.Export(targetPath);

    public Result<Config> Import(string sourcePath) => _configService.Import(sourcePath);
}
