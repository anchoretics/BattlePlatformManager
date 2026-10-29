using BPM.Domain.Models;
using BPM.Infrastructure.Configuration;
using BPM.Shared;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BPM.Infrastructure.Tests;

public class JsonConfigServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _configPath;
    private readonly JsonConfigService _service;

    public JsonConfigServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"bpm-config-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _configPath = Path.Combine(_tempDir, "config.json");
        _service = new JsonConfigService(NullLogger<JsonConfigService>.Instance, _configPath);
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private static Config CreateConfig()
    {
        var config = new Config
        {
            BattleNetPath = @"D:\Games\Battle.net\Battle.net Launcher.exe",
            WowPath = @"D:\Games\World of Warcraft",
        };
        config.Accounts.Add(new Account
        {
            DisplayName = "主号",
            WindowsUser = "WoW1",
            CredentialKey = "BPM_WoW1",
            BattleNetPath = config.BattleNetPath,
            WowPath = config.WowPath,
            AutoLaunchWow = true,
        });
        return config;
    }

    [Fact]
    public void Load_MissingFile_ReturnsEmptyDefaultConfig()
    {
        var result = _service.Load();

        result.IsSuccess.Should().BeTrue();
        result.Value.Accounts.Should().BeEmpty();
    }

    [Fact]
    public void SaveThenLoad_RoundTripsAllFields()
    {
        var config = CreateConfig();

        _service.Save(config).IsSuccess.Should().BeTrue();
        var loaded = _service.Load();

        loaded.IsSuccess.Should().BeTrue();
        loaded.Value.Should().BeEquivalentTo(config);
    }

    [Fact]
    public void Load_LegacyConfigEnablesAssistAndExplicitOptOutSurvivesSave()
    {
        // 旧配置没有新字段，应直接可用，无需迁移或重新录入密码。
        File.WriteAllText(_configPath, "{\"Version\":1,\"Accounts\":[{\"DisplayName\":\"主号\",\"WindowsUser\":\"WoW1\",\"CredentialKey\":\"BPM_WoW1\",\"BattleNetPath\":\"Battle.net.exe\"}]}");
        _service.Load().Value.Accounts[0].BrowserLoginAssistEnabled.Should().BeTrue();

        var config = CreateConfig();
        config.Accounts[0].BrowserLoginAssistEnabled = false;
        _service.Save(config).IsSuccess.Should().BeTrue();
        _service.Load().Value.Accounts[0].BrowserLoginAssistEnabled.Should().BeFalse();
    }

    [Fact]
    public void Save_NeverWritesPasswordLikeContent()
    {
        _service.Save(CreateConfig());

        var json = File.ReadAllText(_configPath);
        json.Should().NotContainAny("password", "Password", "密码");
        json.Should().Contain("CredentialKey");
    }

    [Fact]
    public void Load_CorruptedJson_ReturnsConfigCorrupted()
    {
        File.WriteAllText(_configPath, "{ not valid json !!");

        var result = _service.Load();

        result.IsSuccess.Should().BeFalse();
        result.Code.Should().Be(ErrorCode.ConfigCorrupted);
    }

    [Fact]
    public void ExportThenImport_RoundTrips()
    {
        var config = CreateConfig();
        _service.Save(config);
        var exportPath = Path.Combine(_tempDir, "export.json");

        _service.Export(exportPath).IsSuccess.Should().BeTrue();

        File.Delete(_configPath);
        var imported = _service.Import(exportPath);

        imported.IsSuccess.Should().BeTrue();
        imported.Value.Should().BeEquivalentTo(config);
        File.Exists(_configPath).Should().BeTrue();
    }

    [Fact]
    public void Import_MissingFile_Fails()
    {
        var result = _service.Import(Path.Combine(_tempDir, "missing.json"));

        result.IsSuccess.Should().BeFalse();
        result.Code.Should().Be(ErrorCode.ConfigNotFound);
    }

    [Fact]
    public void Import_InvalidJson_Fails()
    {
        var badPath = Path.Combine(_tempDir, "bad.json");
        File.WriteAllText(badPath, "not json");

        var result = _service.Import(badPath);

        result.IsSuccess.Should().BeFalse();
        result.Code.Should().Be(ErrorCode.ConfigCorrupted);
    }

    public static IEnumerable<object[]> InvalidStructures()
    {
        yield return ["null"];
        yield return ["{\"Accounts\":null}"];
        yield return ["{\"Accounts\":[null]}"];
        yield return ["{\"Accounts\":[{}]}"];
        yield return ["{\"BattleNetPath\":null}"];
        foreach (var property in new[] { "Id", "DisplayName", "WindowsUser", "CredentialKey", "BattleNetPath", "WowPath" })
        {
            var config = CreateConfig();
            var account = config.Accounts[0];
            if (property == "Id") account.Id = Guid.Empty;
            else typeof(Account).GetProperty(property)!.SetValue(account, null);
            yield return [System.Text.Json.JsonSerializer.Serialize(config)];
        }
        var duplicate = CreateConfig();
        duplicate.Accounts.Add(duplicate.Accounts[0]);
        yield return [System.Text.Json.JsonSerializer.Serialize(duplicate)];
    }

    [Theory]
    [MemberData(nameof(InvalidStructures))]
    public void Import_InvalidStructurePreservesExistingConfigAndLoadReturnsFriendlyFailure(string json)
    {
        _service.Save(CreateConfig()).IsSuccess.Should().BeTrue();
        var original = File.ReadAllBytes(_configPath);
        var invalidPath = Path.Combine(_tempDir, "invalid-structure.json");
        File.WriteAllText(invalidPath, json);

        var imported = _service.Import(invalidPath);

        imported.Code.Should().Be(ErrorCode.ConfigCorrupted);
        File.ReadAllBytes(_configPath).Should().Equal(original);
        _service.Load().IsSuccess.Should().BeTrue();

        File.WriteAllText(_configPath, json);
        _service.Load().Code.Should().Be(ErrorCode.ConfigCorrupted);
    }

    [Fact]
    public void Save_InvalidStructurePreservesExistingConfig()
    {
        _service.Save(CreateConfig()).IsSuccess.Should().BeTrue();
        var original = File.ReadAllBytes(_configPath);
        _service.Save(new Config { Accounts = null! }).Code.Should().Be(ErrorCode.ConfigCorrupted);
        File.ReadAllBytes(_configPath).Should().Equal(original);
    }
}
