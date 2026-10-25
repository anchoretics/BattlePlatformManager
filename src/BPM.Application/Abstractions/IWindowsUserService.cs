using BPM.Shared;

namespace BPM.Application.Abstractions;

/// <summary>创建普通 Windows 本地用户,并在无需登录桌面的情况下初始化其配置文件。</summary>
public interface IWindowsUserService
{
    Result CreateLocalUser(string userName, string password);

    /// <summary>可重复调用;将用户加入本机 Users 组,并创建其用户配置文件。</summary>
    Result InitializeProfile(string userName);
}
