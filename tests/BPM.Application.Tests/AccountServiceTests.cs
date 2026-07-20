using BPM.Application.Services;
using BPM.Domain.Models;
using BPM.Shared;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BPM.Application.Tests;

public class AccountServiceTests
{
    private readonly FakeConfigService _configService = new();
    private readonly FakeCredentialService _credentialService = new();
    private readonly AccountService _service;

    public AccountServiceTests()
    {
        _service = new AccountService(_configService, _credentialService, NullLogger<AccountService>.Instance);
    }

    private static Account CreateAccount(string user = "WoW1") => new()
    {
        DisplayName = $"账户 {user}",
        WindowsUser = user,
        BattleNetPath = @"D:\Games\Battle.net\Battle.net Launcher.exe",
        WowPath = @"D:\Games\World of Warcraft",
    };

    [Fact]
    public void SaveAccount_NewAccountWithPassword_SavesCredentialAndConfig()
    {
        var account = CreateAccount();

        var result = _service.SaveAccount(account, "P@ssw0rd");

        result.IsSuccess.Should().BeTrue();
        account.CredentialKey.Should().Be("BPM_WoW1");
        _credentialService.Store.Should().ContainKey("BPM_WoW1");
        _credentialService.Store["BPM_WoW1"].Password.Should().Be("P@ssw0rd");
        _configService.Config.Accounts.Should().ContainSingle(a => a.Id == account.Id);
    }

    [Fact]
    public void SaveAccount_DomainUser_CredentialUsesBareUserName()
    {
        var account = CreateAccount(@"PC-01\WoW2");

        _service.SaveAccount(account, "pw").IsSuccess.Should().BeTrue();

        _credentialService.Store["BPM_WoW2"].UserName.Should().Be("WoW2");
    }

    [Fact]
    public void SaveAccount_NoPasswordAndNoStoredCredential_Fails()
    {
        var result = _service.SaveAccount(CreateAccount(), password: null);

        result.IsSuccess.Should().BeFalse();
        result.Code.Should().Be(ErrorCode.CredentialNotFound);
        _configService.Config.Accounts.Should().BeEmpty();
    }

    [Fact]
    public void SaveAccount_NoPasswordButCredentialExists_KeepsExistingCredential()
    {
        _credentialService.Save("BPM_WoW1", "WoW1", "old-password");
        var account = CreateAccount();

        var result = _service.SaveAccount(account, password: null);

        result.IsSuccess.Should().BeTrue();
        _credentialService.Store["BPM_WoW1"].Password.Should().Be("old-password");
    }

    [Fact]
    public void SaveAccount_InvalidAccount_FailsValidation()
    {
        var account = CreateAccount();
        account.DisplayName = string.Empty;

        var result = _service.SaveAccount(account, "pw");

        result.IsSuccess.Should().BeFalse();
        result.Code.Should().Be(ErrorCode.ValidationFailed);
    }

    [Fact]
    public void SaveAccount_ExistingId_UpdatesInsteadOfDuplicating()
    {
        var account = CreateAccount();
        _service.SaveAccount(account, "pw");
        account.DisplayName = "改名后";

        _service.SaveAccount(account, null).IsSuccess.Should().BeTrue();

        _configService.Config.Accounts.Should().HaveCount(1);
        _configService.Config.Accounts[0].DisplayName.Should().Be("改名后");
    }

    [Fact]
    public void DeleteAccount_RemovesAccountAndOrphanedCredential()
    {
        var account = CreateAccount();
        _service.SaveAccount(account, "pw");

        var result = _service.DeleteAccount(account.Id);

        result.IsSuccess.Should().BeTrue();
        _configService.Config.Accounts.Should().BeEmpty();
        _credentialService.Store.Should().NotContainKey("BPM_WoW1");
    }

    [Fact]
    public void DeleteAccount_CredentialSharedByAnotherAccount_KeepsCredential()
    {
        var first = CreateAccount();
        var second = CreateAccount();
        second.DisplayName = "同用户第二档";
        _service.SaveAccount(first, "pw");
        _service.SaveAccount(second, null);

        _service.DeleteAccount(first.Id).IsSuccess.Should().BeTrue();

        _configService.Config.Accounts.Should().ContainSingle(a => a.Id == second.Id);
        _credentialService.Store.Should().ContainKey("BPM_WoW1");
    }

    [Fact]
    public void DeleteAccount_UnknownId_Fails()
    {
        var result = _service.DeleteAccount(Guid.NewGuid());

        result.IsSuccess.Should().BeFalse();
        result.Code.Should().Be(ErrorCode.AccountNotFound);
    }
}
