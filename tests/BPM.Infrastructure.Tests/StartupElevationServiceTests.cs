using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;
using BPM.Infrastructure.Win32;
using FluentAssertions;
using Xunit;

namespace BPM.Infrastructure.Tests;

[SupportedOSPlatform("windows")]
public class StartupElevationServiceTests
{
    private const string Sid = "S-1-5-21-11-22-33-1001";

    [Fact]
    public void OrdinaryStartupRequestsRunAsAndPreservesOriginalArgumentVector()
    {
        var api = new FakeApi();
        string[] args = ["--launch", Guid.NewGuid().ToString(), "中文 空格\"参数\\"];
        var service = new StartupElevationService(api);

        service.EnsureAdministrator(@"C:\Program Files\BPM\BPM.exe", args).State.Should().Be(StartupElevationState.Relaunched);

        api.Start!.UseShellExecute.Should().BeTrue();
        api.Start.Verb.Should().Be("runas");
        api.Start.FileName.Should().Be(@"C:\Program Files\BPM\BPM.exe");
        api.Start.Arguments.Should().BeEmpty();
        api.Start.ArgumentList.Should().Equal(args.Concat([StartupElevationService.OriginArgument, Sid]));
    }

    [Fact]
    public void AlreadyElevatedStartupDoesNotRelaunch()
    {
        var api = new FakeApi { IsAdministrator = true };
        new StartupElevationService(api).EnsureAdministrator("BPM.exe", []).State.Should().Be(StartupElevationState.Continue);
        api.Start.Should().BeNull();
    }

    [Fact]
    public void BrowserHelperNeverRequestsElevation()
    {
        var api = new FakeApi();
        new StartupElevationService(api).EnsureAdministrator("BPM.exe", ["--browser-login-pipe", "BPM-Login-test", "--server-pid", "123"])
            .State.Should().Be(StartupElevationState.Continue);
        api.Start.Should().BeNull();
    }

    [Fact]
    public void CancelledUacDoesNotRetry()
    {
        var api = new FakeApi { Error = new Win32Exception(1223) };
        new StartupElevationService(api).EnsureAdministrator("BPM.exe", []).State.Should().Be(StartupElevationState.Cancelled);
        api.Calls.Should().Be(1);
    }

    [Fact]
    public void FailedElevationDoesNotEnterRelaunchLoop()
    {
        var api = new FakeApi();
        new StartupElevationService(api).EnsureAdministrator("BPM.exe", [StartupElevationService.OriginArgument, Sid])
            .State.Should().Be(StartupElevationState.Failed);
        api.Start.Should().BeNull();
    }

    [Fact]
    public void DifferentElevatedIdentityCannotSilentlyLoadAnotherUsersConfiguration()
    {
        var api = new FakeApi { IsAdministrator = true, CurrentUserSid = "S-1-5-21-11-22-33-1002" };
        new StartupElevationService(api).EnsureAdministrator("BPM.exe", [StartupElevationService.OriginArgument, Sid])
            .State.Should().Be(StartupElevationState.Failed);
        api.Start.Should().BeNull();
    }

    [Fact]
    public void SuccessfulElevatedChildContinuesWithItsOriginalUser()
    {
        var api = new FakeApi { IsAdministrator = true };
        new StartupElevationService(api).EnsureAdministrator("BPM.exe", ["--launch", "id", StartupElevationService.OriginArgument, Sid])
            .State.Should().Be(StartupElevationState.Continue);
        api.Start.Should().BeNull();
    }

    private sealed class FakeApi : IStartupElevationApi
    {
        public bool IsAdministrator { get; init; }
        public string CurrentUserSid { get; init; } = Sid;
        public Win32Exception? Error { get; init; }
        public ProcessStartInfo? Start { get; private set; }
        public int Calls { get; private set; }
        public bool StartElevated(ProcessStartInfo start)
        {
            Calls++;
            Start = start;
            if (Error is not null) throw Error;
            return true;
        }
    }
}
