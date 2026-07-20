namespace BPM.Application.Models;

/// <summary>一次“以指定 Windows 用户启动进程”的完整描述。密码仅在内存中短暂存在。</summary>
public sealed record LaunchRequest
{
    public required string User { get; init; }
    public required string Domain { get; init; }
    public required string Password { get; init; }
    public required string ExecutablePath { get; init; }
    public string Arguments { get; init; } = string.Empty;
    public string WorkingDirectory { get; init; } = string.Empty;
}

/// <summary>启动成功后返回的实例信息。</summary>
public sealed record LaunchedInstance(Guid AccountId, int ProcessId);

/// <summary>从 Credential Manager 读出的凭据(用户名 + 密码)。</summary>
public sealed record StoredCredential(string UserName, string Password);
