using BPM.Application.Abstractions;
using BPM.Application.Models;
using BPM.Application.Services;
using BPM.Domain.Models;
using BPM.Domain.Validation;
using BPM.Shared;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BPM.Application.Tests;

public class LaunchServiceTests
{
    private sealed class FakeAdapter : IGamePlatformAdapter
    {
        public Result NextValidation { get; set; } = Result.Ok();

        public string PlatformName => "FakePlatform";

        public Result ValidateInstallation(Account account) => NextValidation;

        public Result<LaunchRequest> BuildLaunchRequest(Account account, StoredCredential credential)
        {
            var (domain, user) = AccountValidator.SplitWindowsUser(account.WindowsUser);
            return Result<LaunchRequest>.Ok(new LaunchRequest
            {
                User = user,
                Domain = domain,
                Password = credential.Password,
                ExecutablePath = account.BattleNetPath,
            });
        }
    }

    private readonly FakeConfigService _configService = new();
    private readonly FakeCredentialService _credentialService = new();
    private readonly FakeAdapter _adapter = new();
    private readonly FakeProcessLauncher _launcher = new();
    private readonly LaunchService _service;
    private readonly Account _account;

    public LaunchServiceTests()
    {
        _service = new LaunchService(
            _configService, _credentialService, _adapter, _launcher, NullLogger<LaunchService>.Instance);

        _account = new Account
        {
            DisplayName = "主号",
            WindowsUser = @"PC-01\WoW1",
            CredentialKey = "BPM_WoW1",
            BattleNetPath = @"D:\Games\Battle.net\Battle.net Launcher.exe",
        };
        _configService.Config.Accounts.Add(_account);
        _credentialService.Save("BPM_WoW1", "WoW1", "secret");
    }

    [Fact]
    public void Launch_HappyPath_ReturnsInstanceAndBuildsCorrectRequest()
    {
        var result = _service.Launch(_account.Id);

        result.IsSuccess.Should().BeTrue();
        result.Value.AccountId.Should().Be(_account.Id);
        result.Value.ProcessId.Should().Be(4242);
        _launcher.LastRequest.Should().NotBeNull();
        _launcher.LastRequest!.User.Should().Be("WoW1");
        _launcher.LastRequest.Domain.Should().Be("PC-01");
        _launcher.LastRequest.Password.Should().Be("secret");
        _launcher.LastRequest.ExecutablePath.Should().Be(_account.BattleNetPath);
    }

    [Fact]
    public void Launch_UnknownAccount_Fails()
    {
        var result = _service.Launch(Guid.NewGuid());

        result.IsSuccess.Should().BeFalse();
        result.Code.Should().Be(ErrorCode.AccountNotFound);
        _launcher.LastRequest.Should().BeNull();
    }

    [Fact]
    public void Launch_InvalidInstallation_FailsWithoutLaunching()
    {
        _adapter.NextValidation = Result.Fail(ErrorCode.ExecutableNotFound, "路径无效");

        var result = _service.Launch(_account.Id);

        result.IsSuccess.Should().BeFalse();
        result.Code.Should().Be(ErrorCode.ExecutableNotFound);
        _launcher.LastRequest.Should().BeNull();
    }

    [Fact]
    public void Launch_MissingCredential_Fails()
    {
        _credentialService.Delete("BPM_WoW1");

        var result = _service.Launch(_account.Id);

        result.IsSuccess.Should().BeFalse();
        result.Code.Should().Be(ErrorCode.CredentialNotFound);
        _launcher.LastRequest.Should().BeNull();
    }

    [Fact]
    public void Launch_LauncherFailure_PropagatesErrorAndWin32Code()
    {
        _launcher.NextResult = Result<int>.Fail(ErrorCode.InvalidCredentials, "密码错误", 1326);

        var result = _service.Launch(_account.Id);

        result.IsSuccess.Should().BeFalse();
        result.Code.Should().Be(ErrorCode.InvalidCredentials);
        result.Win32Error.Should().Be(1326);
    }

    [Fact]
    public void Launch_ConfigLoadFailure_Propagates()
    {
        _configService.NextLoadFailure = Result.Fail(ErrorCode.ConfigCorrupted, "配置损坏");

        var result = _service.Launch(_account.Id);

        result.IsSuccess.Should().BeFalse();
        result.Code.Should().Be(ErrorCode.ConfigCorrupted);
    }
}
