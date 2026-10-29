using System.Runtime.Versioning;
using BPM.Application.Abstractions;
using BPM.Application.Models;
using BPM.Domain.Models;
using BPM.Infrastructure.Win32;
using BPM.Shared;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BPM.Infrastructure.Tests;

[SupportedOSPlatform("windows")]
public class BrowserLoginAssistServiceTests
{
    [Fact]
    public async Task OptOutDoesNotReadCredentialsOrStartProcessesAndBecomesIdle()
    {
        using var service = new BrowserLoginAssistService(new RejectCredentials(), new RejectLauncher(),
            NullLogger<BrowserLoginAssistService>.Instance, Environment.ProcessPath!);
        var account = new Account { WindowsUser = "does-not-exist", BrowserLoginAssistEnabled = false };
        LoginAssistStatus? observed = null;
        service.StatusChanged += status => observed = status;

        var status = service.Start(account);

        status.State.Should().Be(LoginAssistState.Disabled);
        observed.Should().Be(status);
        service.GetStatus(account.Id).Should().Be(status);
        await service.WaitForIdleAsync().WaitAsync(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task MissingTargetOrPermissionsProvidesFallbackWithoutLaunchingAnything()
    {
        using var service = new BrowserLoginAssistService(new RejectCredentials(), new RejectLauncher(),
            NullLogger<BrowserLoginAssistService>.Instance, Environment.ProcessPath!);
        var account = new Account { WindowsUser = "BPMMissing" + Guid.NewGuid().ToString("N") };

        var status = service.Start(account);

        status.State.Should().Be(LoginAssistState.ManualLoginRequired);
        status.NeedsAttention.Should().BeTrue();
        await service.WaitForIdleAsync().WaitAsync(TimeSpan.FromSeconds(1));
    }

    private sealed class RejectCredentials : ICredentialService
    {
        public Result Save(string key, string name, string password) => throw new InvalidOperationException("Unexpected credential access");
        public Result<StoredCredential> Get(string key) => throw new InvalidOperationException("Unexpected credential access");
        public Result Delete(string key) => throw new InvalidOperationException("Unexpected credential access");
        public bool Exists(string key) => throw new InvalidOperationException("Unexpected credential access");
    }

    private sealed class RejectLauncher : IProcessLauncher
    {
        public Result<int> Launch(LaunchRequest request) => throw new InvalidOperationException("Unexpected process creation");
    }
}
