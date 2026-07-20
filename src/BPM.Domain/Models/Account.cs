namespace BPM.Domain.Models;

/// <summary>一个 Battle.net 配置档:绑定独立 Windows 用户,数据完全隔离。</summary>
public class Account
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>UI 显示名,例如“主号”。</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>绑定的 Windows 用户名,支持 "user" 或 "DOMAIN\user" 形式。</summary>
    public string WindowsUser { get; set; } = string.Empty;

    /// <summary>Windows Credential Manager 中的凭据键,配置文件不保存任何密码。</summary>
    public string CredentialKey { get; set; } = string.Empty;

    /// <summary>Battle.net 启动器 EXE 完整路径(所有配置档可共享同一安装)。</summary>
    public string BattleNetPath { get; set; } = string.Empty;

    /// <summary>《魔兽世界》安装目录(共享同一份游戏文件)。</summary>
    public string WowPath { get; set; } = string.Empty;

    /// <summary>启动 Battle.net 后是否自动拉起 WoW(通过启动器官方命令行参数)。</summary>
    public bool AutoLaunchWow { get; set; }
}
