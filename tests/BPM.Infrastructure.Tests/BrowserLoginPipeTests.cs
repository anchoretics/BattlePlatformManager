using System.Runtime.Versioning;
using System.Security.Principal;
using BPM.Application.Abstractions;
using BPM.Application.Models;
using BPM.Infrastructure.Win32;
using BPM.Shared;
using FluentAssertions;
using Xunit;

namespace BPM.Infrastructure.Tests;

[SupportedOSPlatform("windows")]
public class BrowserLoginPipeTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TransfersLongSignedUrlExactlyAndReturnsBrowserAcknowledgement(bool browserAccepts)
    {
        var url = BrowserAuthenticationPolicyTests.Url + "%2B%2F%3D" + new string('a', 3000);
        string? received = null;
        var launcher = new PipeLauncher(value => { received = value; return browserAccepts; });
        using var identity = WindowsIdentity.GetCurrent();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));

        var result = await BrowserLoginPipe.OpenAsync(Environment.ProcessPath!, identity.User!, "unused", ".", "unused",
            url, DateTime.UtcNow, launcher, timeout.Token);

        result.Should().Be(browserAccepts);
        received.Should().Be(url);
        launcher.Arguments.Should().NotContain("https:").And.NotContain("state=");
        launcher.Arguments.Length.Should().BeLessThan(150);
        (await launcher.Helper!).Should().Be(browserAccepts ? 0 : 1);
    }

    [Fact]
    public async Task RejectsHelperWhosePidDiffersFromLaunchedProcess()
    {
        var launchedBrowser = false;
        var launcher = new PipeLauncher(_ => { launchedBrowser = true; return true; }) { ReportedPid = int.MaxValue };
        using var identity = WindowsIdentity.GetCurrent();
        var result = await BrowserLoginPipe.OpenAsync(Environment.ProcessPath!, identity.User!, "unused", ".", "unused",
            BrowserAuthenticationPolicyTests.Url, DateTime.UtcNow, launcher, CancellationToken.None);

        result.Should().BeFalse();
        (await launcher.Helper!).Should().Be(1);
        launchedBrowser.Should().BeFalse();
    }

    [Fact]
    public async Task SupersededRequestDoesNotLaunchHelper()
    {
        var launcher = new PipeLauncher(_ => true);
        using var identity = WindowsIdentity.GetCurrent();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var handoff = () => BrowserLoginPipe.OpenAsync(Environment.ProcessPath!, identity.User!, "unused", ".", "unused",
            BrowserAuthenticationPolicyTests.Url, DateTime.UtcNow, launcher, cancellation.Token);

        await handoff.Should().ThrowAsync<OperationCanceledException>();
        launcher.Helper.Should().BeNull();
    }

    private sealed class PipeLauncher(Func<string, bool> openBrowser) : IProcessLauncher
    {
        public string Arguments { get; private set; } = "";
        public Task<int>? Helper { get; private set; }
        public int ReportedPid { get; init; } = Environment.ProcessId;
        public Result<int> Launch(LaunchRequest request)
        {
            Arguments = request.Arguments;
            var args = Arguments.Split(' ');
            Helper = Task.Run(() => BrowserLoginHelper.RunCoreAsync(args[1], int.Parse(args[3]), openBrowser));
            return Result<int>.Ok(ReportedPid);
        }
    }
}
