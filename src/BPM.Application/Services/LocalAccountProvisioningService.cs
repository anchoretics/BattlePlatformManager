using BPM.Application.Abstractions;
using BPM.Application.Models;
using BPM.Domain.Models;
using BPM.Domain.Validation;
using BPM.Shared;

namespace BPM.Application.Services;

/// <summary>创建用户 → 保存凭据 → 初始化用户配置 → 保存 BPM 配置。保留进度以支持失败重试。</summary>
public class LocalAccountProvisioningService
{
    private readonly IWindowsUserService _windowsUserService;
    private readonly ICredentialService _credentialService;
    private readonly AccountService _accountService;

    public LocalAccountProvisioningService(
        IWindowsUserService windowsUserService,
        ICredentialService credentialService,
        AccountService accountService)
    {
        _windowsUserService = windowsUserService;
        _credentialService = credentialService;
        _accountService = accountService;
    }

    public Result CreateAndSave(Account account, string password, LocalUserSetupProgress progress)
    {
        var userValidation = LocalWindowsUserValidator.Validate(account.WindowsUser);
        if (!userValidation.IsSuccess)
        {
            return userValidation;
        }

        if (string.IsNullOrEmpty(password) || password.Contains('\0'))
        {
            return Result.Fail(ErrorCode.ValidationFailed, "创建系统账号必须填写有效的 Windows 密码。");
        }

        if (progress.UserCreated && (progress.AccountId != account.Id ||
            progress.UserName != account.WindowsUser || progress.CredentialKey != account.CredentialKey))
        {
            return Result.Fail(ErrorCode.ValidationFailed, "系统账号已创建,重试时不能更改其用户名或凭据键。");
        }

        if (!progress.UserCreated)
        {
            // 新用户使用配置档专属键,避免覆盖同名域用户或其他配置档的凭据。
            account.CredentialKey = $"BPM_Local_{account.Id:N}";
        }

        var validation = AccountValidator.Validate(account);
        if (!validation.IsSuccess)
        {
            return validation;
        }

        var config = _accountService.GetConfig();
        if (!config.IsSuccess)
        {
            return config;
        }

        if (!progress.UserCreated)
        {
            if (config.Value.Accounts.Any(a => a.Id == account.Id) || _credentialService.Exists(account.CredentialKey))
            {
                return Result.Fail(ErrorCode.ValidationFailed, "该配置档或凭据键已经存在,请使用新的配置档创建系统账号。");
            }

            var create = _windowsUserService.CreateLocalUser(account.WindowsUser, password);
            if (!create.IsSuccess)
            {
                return create;
            }

            progress.AccountId = account.Id;
            progress.UserName = account.WindowsUser;
            progress.CredentialKey = account.CredentialKey;
            progress.UserCreated = true;
        }

        if (!progress.CredentialsSaved)
        {
            var saveCredential = _credentialService.Save(account.CredentialKey, account.WindowsUser, password);
            if (!saveCredential.IsSuccess)
            {
                return Incomplete(saveCredential);
            }

            progress.CredentialsSaved = true;
        }

        if (!progress.ProfileInitialized)
        {
            var initialize = _windowsUserService.InitializeProfile(account.WindowsUser);
            if (!initialize.IsSuccess)
            {
                return Incomplete(initialize);
            }

            progress.ProfileInitialized = true;
        }

        var save = _accountService.SaveAccount(account, password: null);
        return save.IsSuccess ? save : Incomplete(save);
    }

    private static Result Incomplete(Result failure) => Result.Fail(failure.Code,
        $"系统账号已创建,但设置尚未完成:{failure.Message}\n请保留此窗口重试;退出窗口也不会删除已创建的 Windows 用户。",
        failure.Win32Error);
}
