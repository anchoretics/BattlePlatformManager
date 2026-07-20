using BPM.Domain.Models;
using BPM.Shared;

namespace BPM.Application.Abstractions;

/// <summary>配置持久化(config.json)。实现必须保证不写入任何密码。</summary>
public interface IConfigService
{
    /// <summary>加载配置;文件不存在时返回默认空配置。</summary>
    Result<Config> Load();

    Result Save(Config config);

    /// <summary>导出配置到指定路径(不含凭据)。</summary>
    Result Export(string targetPath);

    /// <summary>从指定路径导入配置并覆盖当前配置。</summary>
    Result<Config> Import(string sourcePath);
}
