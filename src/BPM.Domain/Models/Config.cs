namespace BPM.Domain.Models;

/// <summary>应用配置根对象,持久化为 config.json(禁止包含任何明文密码)。</summary>
public class Config
{
    /// <summary>全局默认 Battle.net 启动器路径,新建配置档时作为默认值。</summary>
    public string BattleNetPath { get; set; } = string.Empty;

    /// <summary>全局默认《魔兽世界》目录,新建配置档时作为默认值。</summary>
    public string WowPath { get; set; } = string.Empty;

    public List<Account> Accounts { get; set; } = new();
}
