using System.Runtime.Versioning;
using BPM.Application.Abstractions;
using BPM.Domain.Validation;
using BPM.Shared;
using Microsoft.Extensions.Logging;

namespace BPM.Infrastructure.Win32;

[SupportedOSPlatform("windows")]
public class WindowsUserService : IWindowsUserService
{
    private readonly ILogger<WindowsUserService> _logger;
    private readonly IWindowsUserApi _api;

    public WindowsUserService(ILogger<WindowsUserService> logger) : this(logger, new WindowsUserApi())
    {
    }

    internal WindowsUserService(ILogger<WindowsUserService> logger, IWindowsUserApi api)
    {
        _logger = logger;
        _api = api;
    }

    public Result CreateLocalUser(string userName, string password)
    {
        var validation = LocalWindowsUserValidator.Validate(userName);
        if (!validation.IsSuccess)
        {
            return validation;
        }

        if (string.IsNullOrEmpty(password) || password.Contains('\0'))
        {
            return Result.Fail(ErrorCode.ValidationFailed, "创建系统账号必须填写有效的 Windows 密码。");
        }

        if (!_api.IsAdministrator)
        {
            return RequireAdministrator();
        }

        var error = _api.AddUser(userName, password);
        if (error == 0)
        {
            _logger.LogInformation("已创建 Windows 普通本地用户 {UserName}", userName);
            return Result.Ok();
        }

        _logger.LogWarning("创建 Windows 用户 {UserName} 失败,错误码 {Error}", userName, error);
        return error switch
        {
            5 => RequireAdministrator(),
            2224 => Result.Fail(ErrorCode.WindowsUserAlreadyExists,
                "该 Windows 用户已存在。请取消自动创建并使用其现有密码,或选择新的用户名。", error),
            2245 => Result.Fail(ErrorCode.ValidationFailed,
                "密码不符合本机密码策略,请增加长度并包含大小写字母、数字和符号。", error),
            2202 or 87 => Result.Fail(ErrorCode.ValidationFailed, "Windows 用户名或账户参数不符合系统要求。", error),
            _ => Result.Fail(ErrorCode.WindowsUserCreationFailed, $"创建 Windows 用户失败(错误码 {error})。", error),
        };
    }

    public Result InitializeProfile(string userName)
    {
        var validation = LocalWindowsUserValidator.Validate(userName);
        if (!validation.IsSuccess)
        {
            return validation;
        }

        if (!_api.IsAdministrator)
        {
            return RequireAdministrator();
        }

        var sidError = _api.GetUserSid(userName, out var sid);
        if (sidError != 0)
        {
            return ProfileFailure("读取本地用户 SID", sidError);
        }

        var groupError = _api.AddToUsersGroup(userName);
        if (groupError != 0 && groupError != 1378) // 已属于 Users 组可继续。
        {
            return ProfileFailure("加入本机 Users 组", groupError);
        }

        var hresult = _api.CreateProfile(sid, userName);
        if (hresult != 0 && hresult != unchecked((int)0x800700B7)) // 已存在的用户配置可直接使用。
        {
            _logger.LogWarning("初始化用户 {UserName} 配置失败,HRESULT {HResult:X8}", userName, hresult);
            return Result.Fail(ErrorCode.UserProfileInitializationFailed,
                $"初始化 Windows 用户配置失败(HRESULT 0x{hresult:X8})。", hresult);
        }

        _logger.LogInformation("Windows 用户 {UserName} 配置已就绪,无需先登录桌面", userName);
        return Result.Ok();
    }

    private static Result RequireAdministrator() => Result.Fail(ErrorCode.AdministratorRequired,
        "创建和初始化系统账号需要管理员权限。请保存其他工作后,以管理员身份重新运行 BPM。", 5);

    private static Result ProfileFailure(string operation, int error) => Result.Fail(
        ErrorCode.UserProfileInitializationFailed, $"{operation}失败(错误码 {error})。", error);
}
