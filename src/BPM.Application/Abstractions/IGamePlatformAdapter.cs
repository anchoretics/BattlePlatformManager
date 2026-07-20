using BPM.Application.Models;
using BPM.Domain.Models;
using BPM.Shared;

namespace BPM.Application.Abstractions;

/// <summary>游戏平台适配器。首个实现为 Battle.net,后续可扩展其他平台。</summary>
public interface IGamePlatformAdapter
{
    string PlatformName { get; }

    /// <summary>检查该账户的平台安装是否有效(路径存在等)。</summary>
    Result ValidateInstallation(Account account);

    /// <summary>根据账户与凭据构造启动请求(命令行参数、工作目录等平台细节在此决定)。</summary>
    Result<LaunchRequest> BuildLaunchRequest(Account account, StoredCredential credential);
}
