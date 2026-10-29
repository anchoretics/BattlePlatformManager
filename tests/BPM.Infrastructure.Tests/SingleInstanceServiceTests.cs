using System.Runtime.Versioning;
using System.IO.Pipes;
using BPM.Infrastructure.Win32;
using FluentAssertions;
using Xunit;

namespace BPM.Infrastructure.Tests;

[SupportedOSPlatform("windows")]
public class SingleInstanceServiceTests
{
    [Fact]
    public async Task NoOwnerAllowsNormalStartup()
    {
        using var instance = new SingleInstanceService(Scope(), false);
        (await instance.TryForwardAsync(new())).Should().Be(InstanceForwardResult.NotRunning);
    }

    [Fact]
    public async Task OnlyOneOwnerAndRepeatedRequestsReachItsReceiver()
    {
        var scope = Scope();
        using var owner = new SingleInstanceService(scope, false);
        using var other = new SingleInstanceService(scope, false);
        var received = new List<SingleInstanceCommand>();
        owner.TryStartPrimary(command => { lock (received) received.Add(command); return true; }).Should().BeTrue();
        other.TryStartPrimary(_ => true).Should().BeFalse();
        var id = Guid.NewGuid();

        (await other.TryForwardAsync(new())).Should().Be(InstanceForwardResult.Forwarded);
        (await other.TryForwardAsync(new(id))).Should().Be(InstanceForwardResult.Forwarded);
        (await other.TryForwardAsync(new())).Should().Be(InstanceForwardResult.Forwarded);

        lock (received) received.Should().Equal(new SingleInstanceCommand(), new SingleInstanceCommand(id), new SingleInstanceCommand());
    }

    [Fact]
    public async Task RejectedRequestIsNotReportedAsDeliveredAndNextRequestStillWorks()
    {
        var scope = Scope();
        using var owner = new SingleInstanceService(scope, false);
        using var client = new SingleInstanceService(scope, false);
        var calls = 0;
        owner.TryStartPrimary(_ => ++calls > 1).Should().BeTrue();

        (await client.TryForwardAsync(new())).Should().Be(InstanceForwardResult.Unavailable);
        (await client.TryForwardAsync(new())).Should().Be(InstanceForwardResult.Forwarded);
    }

    [Fact]
    public async Task OwnerExitReleasesLeaseForNextStartup()
    {
        var scope = Scope();
        using var owner = new SingleInstanceService(scope, false);
        using var next = new SingleInstanceService(scope, false);
        owner.TryStartPrimary(_ => true).Should().BeTrue();
        owner.Dispose();

        (await next.TryForwardAsync(new())).Should().Be(InstanceForwardResult.NotRunning);
        next.TryStartPrimary(_ => true).Should().BeTrue();
    }

    [Fact]
    public void ShortcutsTransferOnlyTheParsedAccountId()
    {
        var id = Guid.NewGuid();
        SingleInstanceCommand.FromArguments(["--launch", id.ToString(), "--ignored", "other text"])
            .Should().Be(new SingleInstanceCommand(id));
        SingleInstanceCommand.FromArguments(["--launch", "invalid"]).AccountId.Should().BeNull();
        SingleInstanceCommand.FromArguments([]).AccountId.Should().BeNull();
    }

    [Fact]
    public async Task ReceiverExceptionDoesNotKillTheListener()
    {
        var scope = Scope();
        using var owner = new SingleInstanceService(scope, false);
        using var client = new SingleInstanceService(scope, false);
        var calls = 0;
        owner.TryStartPrimary(_ => ++calls == 1 ? throw new InvalidOperationException() : true).Should().BeTrue();

        (await client.TryForwardAsync(new())).Should().Be(InstanceForwardResult.Unavailable);
        (await client.TryForwardAsync(new())).Should().Be(InstanceForwardResult.Forwarded);
    }

    [Fact]
    public async Task SlowClientStillReceivesAcknowledgementBeforeServerDisconnects()
    {
        using var owner = new SingleInstanceService(Scope(), false);
        owner.TryStartPrimary(_ => true).Should().BeTrue();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        using var client = new NamedPipeClientStream(".", owner.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(timeout.Token);
        await client.WriteAsync(new byte[17], timeout.Token);
        await client.FlushAsync(timeout.Token);
        await Task.Delay(100, timeout.Token);

        var response = new byte[1];
        await client.ReadExactlyAsync(response, timeout.Token);
        response[0].Should().Be(1);
        await client.WriteAsync(new byte[] { 1 }, timeout.Token);
    }

    private static string Scope() => "BPM-Test-" + Guid.NewGuid().ToString("N");
}
