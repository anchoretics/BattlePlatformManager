namespace BPM.Application.Models;

/// <summary>单个账户编辑窗口中的创建进度。不包含密码,失败后可继续同一次创建。</summary>
public sealed class LocalUserSetupProgress
{
    public Guid AccountId { get; internal set; }
    public string? UserName { get; internal set; }
    public string? CredentialKey { get; internal set; }
    public bool UserCreated { get; internal set; }
    public bool CredentialsSaved { get; internal set; }
    public bool ProfileInitialized { get; internal set; }
}
