using BPM.Domain.Models;
using BPM.Shared;

namespace BPM.Application.Abstractions;

/// <summary>桌面快捷方式生成:指向 "BPM.exe --launch &lt;accountId&gt;"。</summary>
public interface IShortcutService
{
    /// <summary>成功时返回快捷方式完整路径。</summary>
    Result<string> CreateLaunchShortcut(Account account);
}
