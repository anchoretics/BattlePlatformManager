using BPM.Domain.Validation;
using FluentAssertions;
using Xunit;

namespace BPM.Domain.Tests;

public class LocalWindowsUserValidatorTests
{
    [Theory]
    [InlineData("WoW2")]
    [InlineData("战网用户")]
    [InlineData("Player Two")]
    [InlineData("12345678901234567890")]
    public void Validate_LocalUserNames_Succeed(string userName)
    {
        LocalWindowsUserValidator.Validate(userName).IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" WoW2")]
    [InlineData("WoW2 ")]
    [InlineData("WoW2.")]
    [InlineData("123456789012345678901")]
    [InlineData(@"DOMAIN\WoW2")]
    [InlineData(@".\WoW2")]
    [InlineData("user@example.com")]
    [InlineData("user/name")]
    [InlineData("user:name")]
    [InlineData("user\nname")]
    [InlineData("user\0name")]
    public void Validate_InvalidOrNonLocalUserNames_Fail(string userName)
    {
        LocalWindowsUserValidator.Validate(userName).IsSuccess.Should().BeFalse();
    }
}
