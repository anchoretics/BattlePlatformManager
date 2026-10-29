using BPM.Infrastructure.Win32;
using FluentAssertions;
using Xunit;

namespace BPM.Infrastructure.Tests;

public class BrowserLoginRequestSlotTests
{
    [Fact]
    public void NewRequestCancelsOldAndInvalidatesOldStatusWithoutAffectingOtherAccounts()
    {
        using var firstAccount = new BrowserLoginRequestSlot();
        using var secondAccount = new BrowserLoginRequestSlot();
        var old = firstAccount.Begin(CancellationToken.None);
        var other = secondAccount.Begin(CancellationToken.None);

        var current = firstAccount.Begin(CancellationToken.None);

        old.Token.IsCancellationRequested.Should().BeTrue();
        firstAccount.IsCurrent(old).Should().BeFalse();
        firstAccount.IsCurrent(current).Should().BeTrue();
        current.Token.IsCancellationRequested.Should().BeFalse();
        other.Token.IsCancellationRequested.Should().BeFalse();
        secondAccount.IsCurrent(other).Should().BeTrue();
    }

    [Fact]
    public void LifetimeCancellationAndDisposeStopPendingHandoffs()
    {
        using var lifetime = new CancellationTokenSource();
        using var slot = new BrowserLoginRequestSlot();
        var request = slot.Begin(lifetime.Token);

        lifetime.Cancel();
        request.Token.IsCancellationRequested.Should().BeTrue();
        slot.Dispose();
        slot.IsCurrent(request).Should().BeFalse();
    }
}
