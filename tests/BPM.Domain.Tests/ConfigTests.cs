using BPM.Domain.Models;
using FluentAssertions;
using Xunit;

namespace BPM.Domain.Tests;

public class ConfigTests
{
    [Fact]
    public void NewConfig_HasEmptyDefaults()
    {
        var config = new Config();

        config.BattleNetPath.Should().BeEmpty();
        config.WowPath.Should().BeEmpty();
        config.Accounts.Should().BeEmpty();
    }

    [Fact]
    public void NewAccount_GetsNonEmptyId()
    {
        new Account().Id.Should().NotBe(Guid.Empty);
    }
}
