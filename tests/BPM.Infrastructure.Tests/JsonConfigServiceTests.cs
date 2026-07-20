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
}
