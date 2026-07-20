using BPM.Domain.Models;
using BPM.Domain.Validation;
using BPM.Shared;
using FluentAssertions;
using Xunit;

namespace BPM.Domain.Tests;

public class AccountValidatorTests
{
    private static Account CreateValidAccount() => new()
    {
        DisplayName = "主号",
        WindowsUser = "WoW1",
        CredentialKey = "BPM_WoW1",
        BattleNetPath = @"D:\Games\Battle.net\Battle.net Launcher.exe",
        WowPath = @"D:\Games\World of Warcraft",
    };

    [Fact]
    public void Validate_ValidAccount_Succeeds()
    {
        AccountValidator.Validate(CreateValidAccount()).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Validate_EmptyId_Fails()
    {
        var account = CreateValidAccount();
        account.Id = Guid.Empty;

        var result = AccountValidator.Validate(account);

        result.IsSuccess.Should().BeFalse();
        result.Code.Should().Be(ErrorCode.ValidationFailed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_EmptyDisplayName_Fails(string displayName)
    {
        var account = CreateValidAccount();
        account.DisplayName = displayName;

        AccountValidator.Validate(account).IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void Validate_EmptyWindowsUser_Fails()
    {
        var account = CreateValidAccount();
        account.WindowsUser = string.Empty;

        AccountValidator.Validate(account).IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void Validate_WindowsUserWithMultipleBackslashes_Fails()
    {
        var account = CreateValidAccount();
        account.WindowsUser = @"a\b\c";

        AccountValidator.Validate(account).IsSuccess.Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData(@"D:\Games\Battle.net")]
    [InlineData(@"D:\Games\Battle.net\readme.txt")]
    public void Validate_InvalidBattleNetPath_Fails(string path)
    {
        var account = CreateValidAccount();
        account.BattleNetPath = path;

        AccountValidator.Validate(account).IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void Validate_EmptyCredentialKey_Fails()
    {
        var account = CreateValidAccount();
        account.CredentialKey = string.Empty;

        AccountValidator.Validate(account).IsSuccess.Should().BeFalse();
    }

    [Theory]
    [InlineData("WoW1", ".", "WoW1")]
    [InlineData(@"PC-01\WoW2", "PC-01", "WoW2")]
    public void SplitWindowsUser_SplitsDomainAndUser(string input, string expectedDomain, string expectedUser)
    {
        var (domain, user) = AccountValidator.SplitWindowsUser(input);

        domain.Should().Be(expectedDomain);
        user.Should().Be(expectedUser);
    }

    [Theory]
    [InlineData("WoW1", "BPM_WoW1")]
    [InlineData(@"PC-01\WoW2", "BPM_WoW2")]
    public void DeriveCredentialKey_UsesBareUserName(string windowsUser, string expected)
    {
        AccountValidator.DeriveCredentialKey(windowsUser).Should().Be(expected);
    }
}
