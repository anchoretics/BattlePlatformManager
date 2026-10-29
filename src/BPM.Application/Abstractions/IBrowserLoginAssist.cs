using BPM.Application.Models;
using BPM.Domain.Models;

namespace BPM.Application.Abstractions;

/// <summary>浏览器登录交接的可选兼容辅助；失败不能阻止平台正常启动。</summary>
public interface IBrowserLoginAssist
{
    event Action<LoginAssistStatus>? StatusChanged;
    LoginAssistStatus Start(Account account);
    LoginAssistStatus? GetStatus(Guid accountId);
    void Stop(Guid accountId);
    Task WaitForIdleAsync();
}
