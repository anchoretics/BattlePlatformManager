using System.Text.Json;
using System.Text.Json.Serialization;
using BPM.Application.Abstractions;
using BPM.Domain.Models;
using BPM.Shared;
using Microsoft.Extensions.Logging;

namespace BPM.Infrastructure.Configuration;

/// <summary>System.Text.Json 配置持久化。原子写入(临时文件 + 替换),绝不写入密码。</summary>
public class JsonConfigService : IConfigService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private readonly string _configPath;
    private readonly ILogger<JsonConfigService> _logger;

    public JsonConfigService(ILogger<JsonConfigService> logger, string? configPath = null)
    {
        _logger = logger;
        _configPath = configPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BPM", "config.json");
    }

    public string ConfigPath => _configPath;

    public Result<Config> Load()
    {
        try
        {
            if (!File.Exists(_configPath))
            {
                return Result<Config>.Ok(new Config());
            }

            var json = File.ReadAllText(_configPath);
            var config = JsonSerializer.Deserialize<Config>(json, SerializerOptions);
            return config is null
                ? Result<Config>.Fail(ErrorCode.ConfigCorrupted, "配置文件内容为空或无效。")
                : Result<Config>.Ok(config);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "配置文件解析失败:{ConfigPath}", _configPath);
            return Result<Config>.Fail(ErrorCode.ConfigCorrupted, $"配置文件已损坏:{ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "读取配置失败:{ConfigPath}", _configPath);
            return Result<Config>.Fail(ErrorCode.Unexpected, $"读取配置失败:{ex.Message}");
        }
    }

    public Result Save(Config config)
    {
        try
        {
            var directory = Path.GetDirectoryName(_configPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(config, SerializerOptions);
            var tempPath = _configPath + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, _configPath, overwrite: true);
            return Result.Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存配置失败:{ConfigPath}", _configPath);
            return Result.Fail(ErrorCode.Unexpected, $"保存配置失败:{ex.Message}");
        }
    }

    public Result Export(string targetPath)
    {
        var load = Load();
        if (!load.IsSuccess)
        {
            return load;
        }

        try
        {
            File.WriteAllText(targetPath, JsonSerializer.Serialize(load.Value, SerializerOptions));
            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail(ErrorCode.Unexpected, $"导出配置失败:{ex.Message}");
        }
    }

    public Result<Config> Import(string sourcePath)
    {
        try
        {
            if (!File.Exists(sourcePath))
            {
                return Result<Config>.Fail(ErrorCode.ConfigNotFound, "导入文件不存在。");
            }

            var json = File.ReadAllText(sourcePath);
            var config = JsonSerializer.Deserialize<Config>(json, SerializerOptions);
            if (config is null)
            {
                return Result<Config>.Fail(ErrorCode.ConfigCorrupted, "导入文件内容无效。");
            }

            var save = Save(config);
            return save.IsSuccess ? Result<Config>.Ok(config) : Result<Config>.Fail(save);
        }
        catch (JsonException ex)
        {
            return Result<Config>.Fail(ErrorCode.ConfigCorrupted, $"导入文件不是有效的配置 JSON:{ex.Message}");
        }
        catch (Exception ex)
        {
            return Result<Config>.Fail(ErrorCode.Unexpected, $"导入配置失败:{ex.Message}");
        }
    }
}
