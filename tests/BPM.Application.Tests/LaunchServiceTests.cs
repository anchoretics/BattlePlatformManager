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

    [Fact]
    public void Launch_PreparesAssistBeforeCreatingBattleNetProcess()
    {
        var order = new List<string>();
        var assist = new FakeAssist { OnStart = () => order.Add("assist") };
        var launcher = new OrderedLauncher(() => order.Add("launch"));
        var service = new LaunchService(_configService, _credentialService, _adapter, launcher,
            NullLogger<LaunchService>.Instance, assist);

        service.Launch(_account.Id).IsSuccess.Should().BeTrue();

        order.Should().Equal("assist", "launch");
    }

    [Fact]
    public void Launch_AssistUnavailableStillLaunchesClient()
    {
        var assist = new FakeAssist { OnStart = () => throw new UnauthorizedAccessException() };
        var service = new LaunchService(_configService, _credentialService, _adapter, _launcher,
            NullLogger<LaunchService>.Instance, assist);

        service.Launch(_account.Id).IsSuccess.Should().BeTrue();
        _launcher.LastRequest.Should().NotBeNull();
    }

    [Fact]
    public void Launch_ClientFailsStopsPreparedAssist()
    {
        var assist = new FakeAssist();
        _launcher.NextResult = Result<int>.Fail(ErrorCode.LaunchFailed, "failed", 5);
        var service = new LaunchService(_configService, _credentialService, _adapter, _launcher,
            NullLogger<LaunchService>.Instance, assist);

        service.Launch(_account.Id).IsSuccess.Should().BeFalse();

        assist.Stopped.Should().Be(_account.Id);
    }

    [Fact]
    public void Launch_InvalidConfigurationDoesNotStartAssist()
    {
        var started = false;
        var assist = new FakeAssist { OnStart = () => started = true };
        _adapter.NextValidation = Result.Fail(ErrorCode.ExecutableNotFound, "missing");
        var service = new LaunchService(_configService, _credentialService, _adapter, _launcher,
            NullLogger<LaunchService>.Instance, assist);

        service.Launch(_account.Id).IsSuccess.Should().BeFalse();

        started.Should().BeFalse();
    }

    [Fact]
    public void Launch_UnexpectedProcessFailureStillStopsAssistAndReturnsSafeFailure()
    {
        var assist = new FakeAssist();
        var launcher = new OrderedLauncher(() => throw new InvalidOperationException("sensitive launch arguments"));
        var service = new LaunchService(_configService, _credentialService, _adapter, launcher,
            NullLogger<LaunchService>.Instance, assist);

        var result = service.Launch(_account.Id);

        result.Code.Should().Be(ErrorCode.LaunchFailed);
        result.Message.Should().NotContain("sensitive");
        assist.Stopped.Should().Be(_account.Id);
    }

    private sealed class OrderedLauncher(Action onLaunch) : IProcessLauncher
    {
        public Result<int> Launch(LaunchRequest request) { onLaunch(); return Result<int>.Ok(4242); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Launch_ConcurrentRequestsShareOneProcessAndPermitRetryAfterCompletion(bool failFirst)
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var secondStarted = new ManualResetEventSlim();
        var calls = 0;
        var launcher = new OrderedLauncher(() =>
        {
            Interlocked.Increment(ref calls);
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException();
            if (failFirst) throw new InvalidOperationException("test failure");
        });
        var service = new LaunchService(_configService, _credentialService, _adapter, launcher,
            NullLogger<LaunchService>.Instance);
        var first = Task.Factory.StartNew(() => service.Launch(_account.Id), TaskCreationOptions.LongRunning);
        Task<Result<LaunchedInstance>>? second = null;
        try
        {
            entered.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
            second = Task.Factory.StartNew(() =>
            {
                secondStarted.Set();
                return service.Launch(_account.Id);
            }, TaskCreationOptions.LongRunning);
            secondStarted.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
            (await Task.WhenAny(second, Task.Delay(200))).Should().NotBeSameAs(second);
            Volatile.Read(ref calls).Should().Be(1);
        }
        finally
        {
            release.Set();
            await first.WaitAsync(TimeSpan.FromSeconds(3));
            if (second is not null) await second.WaitAsync(TimeSpan.FromSeconds(3));
        }

        var firstResult = await first;
        (await second!).Should().BeSameAs(firstResult);
        firstResult.IsSuccess.Should().Be(!failFirst);
        service.Launch(_account.Id).IsSuccess.Should().Be(!failFirst);
        calls.Should().Be(2);
    }

    [Fact]
    public async Task Launch_DifferentAccountsCanStartIndependently()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var other = new Account { CredentialKey = _account.CredentialKey, WindowsUser = "other", BattleNetPath = "other.exe" };
        _configService.Config.Accounts.Add(other);
        var launcher = new RequestLauncher(request =>
        {
            if (request.User != "other")
            {
                entered.Set();
                if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException();
            }
            return Result<int>.Ok(4242);
        });
        var service = new LaunchService(_configService, _credentialService, _adapter, launcher,
            NullLogger<LaunchService>.Instance);
        var first = Task.Factory.StartNew(() => service.Launch(_account.Id), TaskCreationOptions.LongRunning);
        try
        {
            entered.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
            service.Launch(other.Id).IsSuccess.Should().BeTrue();
            first.IsCompleted.Should().BeFalse();
        }
        finally { release.Set(); await first.WaitAsync(TimeSpan.FromSeconds(3)); }
    }

    private sealed class RequestLauncher(Func<LaunchRequest, Result<int>> launch) : IProcessLauncher
    {
        public Result<int> Launch(LaunchRequest request) => launch(request);
    }

    private sealed class FakeAssist : IBrowserLoginAssist
    {
        public Action? OnStart { get; init; }
        public Guid? Stopped { get; private set; }
        public event Action<LoginAssistStatus>? StatusChanged { add { } remove { } }
        public LoginAssistStatus Start(Account account)
        {
            OnStart?.Invoke();
            return new(account.Id, LoginAssistState.Ready, "ready");
        }
        public LoginAssistStatus? GetStatus(Guid id) => null;
        public void Stop(Guid id) => Stopped = id;
        public Task WaitForIdleAsync() => Task.CompletedTask;
    }
}
