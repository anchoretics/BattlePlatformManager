using BPM.Application.Models;
using BPM.Shared;

namespace BPM.Application.Abstractions;

/// <summary>以其他 Windows 用户身份启动进程(CreateProcessWithLogonW 封装)。</summary>
public interface IProcessLauncher
{
    /// <summary>启动成功返回新进程 PID。</summary>
    Result<int> Launch(LaunchRequest request);
}
