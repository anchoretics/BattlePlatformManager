using BPM.Application.Models;
using BPM.Domain.Models;
using BPM.Platform.BattleNet;
using BPM.Shared;
using FluentAssertions;
using Xunit;

namespace BPM.Application.Tests;

public class BattleNetAdapterTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _launcherPath;
    private readonly BattleNetAdapter _adapter = new();

    public BattleNetAdapterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"bpm-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _launcherPath = Path.Combine(_tempDir, "Battle.net Launcher.exe");
        File.WriteAllText(_launcherPath, "stub");
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private Account CreateAccount() => new()
    {
        DisplayName = "主号",
        WindowsUser = "WoW1",
        CredentialKey = "BPM_WoW1",
        BattleNetPath = _launcherPath,
        WowPath = _tempDir,
    };

    [Fact]
    public void ValidateInstallation_ExistingPaths_Succeeds()
    {
        _adapter.ValidateInstallation(CreateAccount()).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void ValidateInstallation_MissingLauncher_Fails()
    {
        var account = CreateAccount();
        account.BattleNetPath = Path.Combine(_tempDir, "missing.exe");

        var result = _adapter.ValidateInstallation(account);

        result.IsSuccess.Should().BeFalse();
        result.Code.Should().Be(ErrorCode.ExecutableNotFound);
    }

    [Fact]
    public void ValidateInstallation_MissingWowDirectory_Fails()
    {
        var account = CreateAccount();
        account.WowPath = Path.Combine(_tempDir, "missing-wow");

        _adapter.ValidateInstallation(account).IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void ValidateInstallation_EmptyWowPath_IsAllowed()
    {
        var account = CreateAccount();
        account.WowPath = string.Empty;

        _adapter.ValidateInstallation(account).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void BuildLaunchRequest_MapsUserDomainAndWorkingDirectory()
    {
        var account = CreateAccount();
        account.WindowsUser = @"PC-01\WoW1";

        var result = _adapter.BuildLaunchRequest(account, new StoredCredential("WoW1", "secret"));

        result.IsSuccess.Should().BeTrue();
        result.Value.User.Should().Be("WoW1");
        result.Value.Domain.Should().Be("PC-01");
        result.Value.Password.Should().Be("secret");
        result.Value.ExecutablePath.Should().Be(_launcherPath);
        result.Value.WorkingDirectory.Should().Be(_tempDir);
        result.Value.Arguments.Should().BeEmpty();
    }

    [Fact]
    public void BuildLaunchRequest_AutoLaunchWow_AddsLauncherArgument()
    {
        var account = CreateAccount();
        account.AutoLaunchWow = true;

        var result = _adapter.BuildLaunchRequest(account, new StoredCredential("WoW1", "secret"));

        result.Value.Arguments.Should().Contain("launch WoW");
    }
}
