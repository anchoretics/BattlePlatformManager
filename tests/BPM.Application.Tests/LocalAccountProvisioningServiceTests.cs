using System.Text.Json;
using BPM.Application.Abstractions;
using BPM.Application.Models;
using BPM.Application.Services;
using BPM.Domain.Models;
using BPM.Shared;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BPM.Application.Tests;

public class LocalAccountProvisioningServiceTests
{
    private const string Password = "Synthetic-P@ssword-123";
    private readonly SnapshotConfigService _config = new();
    private readonly FaultingCredentialService _credentials = new();
    private readonly FakeWindowsUserService _windowsUsers = new();
    private readonly LocalUserSetupProgress _progress = new();
    private readonly LocalAccountProvisioningService _service;
    private readonly Account _account = new()
    {
        DisplayName = "新配置档",
        WindowsUser = "WoW2",
        BattleNetPath = @"C:\Games\Battle.net\Battle.net.exe",
    };

    public LocalAccountProvisioningServiceTests()
    {
        var accounts = new AccountService(_config, _credentials, NullLogger<AccountService>.Instance);
        _service = new LocalAccountProvisioningService(_windowsUsers, _credentials, accounts);
    }

    [Fact]
    public void CreateAndSave_CreatesUserInitializesProfileAndPersistsOnlyCredentialKey()
    {
        var result = _service.CreateAndSave(_account, Password, _progress);

        result.IsSuccess.Should().BeTrue();
        _windowsUsers.UserName.Should().Be("WoW2");
        _windowsUsers.Password.Should().Be(Password);
        _windowsUsers.CreateCalls.Should().Be(1);
        _windowsUsers.InitializeCalls.Should().Be(1);
        _credentials.Store[_account.CredentialKey].Password.Should().Be(Password);
        _account.CredentialKey.Should().Be($"BPM_Local_{_account.Id:N}");
        _config.Config.Accounts.Should().ContainSingle().Which.Should().BeEquivalentTo(_account);
        JsonSerializer.Serialize(_config.Config).Should().NotContain(Password);
        _progress.UserCreated.Should().BeTrue();
        _progress.CredentialsSaved.Should().BeTrue();
        _progress.ProfileInitialized.Should().BeTrue();
    }

    [Fact]
    public void CreateAndSave_InvalidConfig_DoesNotCreateSystemUser()
    {
        _account.DisplayName = "";

        _service.CreateAndSave(_account, Password, _progress).Code.Should().Be(ErrorCode.ValidationFailed);

        _windowsUsers.CreateCalls.Should().Be(0);
        _credentials.Store.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid\0password")]
    public void CreateAndSave_InvalidPassword_DoesNotCreateUser(string password)
    {
        _service.CreateAndSave(_account, password, _progress).Code.Should().Be(ErrorCode.ValidationFailed);

        _windowsUsers.CreateCalls.Should().Be(0);
    }

    [Fact]
    public void CreateAndSave_UnreadableConfig_DoesNotCreateUser()
    {
        _config.LoadFailure = Result.Fail(ErrorCode.ConfigCorrupted, "配置损坏");

        _service.CreateAndSave(_account, Password, _progress).Code.Should().Be(ErrorCode.ConfigCorrupted);

        _windowsUsers.CreateCalls.Should().Be(0);
    }

    [Fact]
    public void CreateAndSave_ExistingSystemUser_DoesNotOverwriteCredentialsOrInitializeProfile()
    {
        _windowsUsers.CreateResult = Result.Fail(ErrorCode.WindowsUserAlreadyExists, "用户已存在", 2224);
        _credentials.Save("BPM_WoW2", "WoW2", "existing-password");

        var result = _service.CreateAndSave(_account, Password, _progress);

        result.Code.Should().Be(ErrorCode.WindowsUserAlreadyExists);
        _windowsUsers.InitializeCalls.Should().Be(0);
        _credentials.Store.Should().ContainSingle().Which.Value.Password.Should().Be("existing-password");
        _config.Config.Accounts.Should().BeEmpty();
        _progress.UserCreated.Should().BeFalse();
    }

    [Fact]
    public void CreateAndSave_CredentialFailure_RetryContinuesWithoutRecreatingUser()
    {
        _credentials.SaveFailure = Result.Fail(ErrorCode.CredentialWriteFailed, "凭据写入失败");

        var failed = _service.CreateAndSave(_account, Password, _progress);

        failed.Code.Should().Be(ErrorCode.CredentialWriteFailed);
        _progress.UserCreated.Should().BeTrue();
        _progress.CredentialsSaved.Should().BeFalse();
        _windowsUsers.InitializeCalls.Should().Be(0);
        _config.Config.Accounts.Should().BeEmpty();
        _credentials.SaveFailure = null;

        _service.CreateAndSave(_account, Password, _progress).IsSuccess.Should().BeTrue();

        _windowsUsers.CreateCalls.Should().Be(1);
        _credentials.SaveCalls.Should().Be(2);
        _credentials.Store[_account.CredentialKey].Password.Should().Be(Password);
    }

    [Fact]
    public void CreateAndSave_ProfileFailure_RetryReusesUserAndSavedPassword()
    {
        _windowsUsers.InitializeResult = Result.Fail(ErrorCode.UserProfileInitializationFailed, "配置初始化失败", 5);

        var failed = _service.CreateAndSave(_account, Password, _progress);

        failed.Code.Should().Be(ErrorCode.UserProfileInitializationFailed);
        failed.Win32Error.Should().Be(5);
        failed.Message.Should().Contain("系统账号已创建").And.NotContain(Password);
        _progress.CredentialsSaved.Should().BeTrue();
        _config.Config.Accounts.Should().BeEmpty();
        _windowsUsers.InitializeResult = Result.Ok();

        _service.CreateAndSave(_account, Password, _progress).IsSuccess.Should().BeTrue();

        _windowsUsers.CreateCalls.Should().Be(1);
        _windowsUsers.InitializeCalls.Should().Be(2);
        _credentials.SaveCalls.Should().Be(1);
    }

    [Fact]
    public void CreateAndSave_SaveFailure_RetryOnlySavesConfig()
    {
        _config.SaveFailure = Result.Fail(ErrorCode.Unexpected, "磁盘写入失败");

        _service.CreateAndSave(_account, Password, _progress).IsSuccess.Should().BeFalse();
        _config.Config.Accounts.Should().BeEmpty();
        _progress.ProfileInitialized.Should().BeTrue();
        _config.SaveFailure = null;

        _service.CreateAndSave(_account, Password, _progress).IsSuccess.Should().BeTrue();

        _windowsUsers.CreateCalls.Should().Be(1);
        _windowsUsers.InitializeCalls.Should().Be(1);
        _credentials.SaveCalls.Should().Be(1);
        _config.Config.Accounts.Should().ContainSingle();
    }

    [Fact]
    public void CreateAndSave_RetryWithDifferentUser_DoesNotTouchAnotherUser()
    {
        _windowsUsers.InitializeResult = Result.Fail(ErrorCode.UserProfileInitializationFailed, "失败");
        _service.CreateAndSave(_account, Password, _progress);
        _account.WindowsUser = "OtherUser";

        _service.CreateAndSave(_account, Password, _progress).Code.Should().Be(ErrorCode.ValidationFailed);

        _windowsUsers.CreateCalls.Should().Be(1);
        _windowsUsers.InitializeCalls.Should().Be(1);
        _credentials.SaveCalls.Should().Be(1);
    }

    [Fact]
    public void CreateAndSave_ExistingConfigId_DoesNotCreateUser()
    {
        _config.Config.Accounts.Add(_account);

        _service.CreateAndSave(_account, Password, _progress).Code.Should().Be(ErrorCode.ValidationFailed);

        _windowsUsers.CreateCalls.Should().Be(0);
    }

    [Fact]
    public void InitializeAndSave_ExistingUser_InitializesAndSavesWithoutCreatingUser()
    {
        var result = _service.InitializeAndSave(_account, Password);

        result.IsSuccess.Should().BeTrue();
        _windowsUsers.CreateCalls.Should().Be(0);
        _windowsUsers.InitializeCalls.Should().Be(1);
        _credentials.Store[_account.CredentialKey].Password.Should().Be(Password);
        _config.Config.Accounts.Should().ContainSingle().Which.WindowsUser.Should().Be("WoW2");
    }

    [Fact]
    public void InitializeAndSave_ProfileFailure_DoesNotPersistConfigOrOverwritePassword()
    {
        _windowsUsers.InitializeResult = Result.Fail(ErrorCode.UserProfileInitializationFailed, "失败");

        _service.InitializeAndSave(_account, Password).Code.Should().Be(ErrorCode.UserProfileInitializationFailed);

        _windowsUsers.CreateCalls.Should().Be(0);
        _credentials.Store.Should().BeEmpty();
        _config.Config.Accounts.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("invalid\0password")]
    public void InitializeAndSave_RequiresOriginalPassword(string? password)
    {
        _service.InitializeAndSave(_account, password).Code.Should().Be(ErrorCode.ValidationFailed);

        _windowsUsers.InitializeCalls.Should().Be(0);
        _credentials.Store.Should().BeEmpty();
    }

    [Fact]
    public void InitializeAndSave_DomainUser_DoesNotInitializeProfile()
    {
        _account.WindowsUser = @"DOMAIN\WoW2";

        _service.InitializeAndSave(_account, Password).Code.Should().Be(ErrorCode.ValidationFailed);

        _windowsUsers.InitializeCalls.Should().Be(0);
    }

    [Fact]
    public void InitializeAndSave_UnreadableConfig_DoesNotInitializeProfile()
    {
        _config.LoadFailure = Result.Fail(ErrorCode.ConfigCorrupted, "配置损坏");

        _service.InitializeAndSave(_account, Password).Code.Should().Be(ErrorCode.ConfigCorrupted);

        _windowsUsers.InitializeCalls.Should().Be(0);
    }

    private sealed class FakeWindowsUserService : IWindowsUserService
    {
        public Result CreateResult { get; set; } = Result.Ok();
        public Result InitializeResult { get; set; } = Result.Ok();
        public int CreateCalls { get; private set; }
        public int InitializeCalls { get; private set; }
        public string? UserName { get; private set; }
        public string? Password { get; private set; }
        public Result CreateLocalUser(string userName, string password)
        {
            CreateCalls++;
            UserName = userName;
            Password = password;
            return CreateResult;
        }

        public Result InitializeProfile(string userName)
        {
            InitializeCalls++;
            return InitializeResult;
        }
    }

    private sealed class FaultingCredentialService : ICredentialService
    {
        private readonly FakeCredentialService _inner = new();
        public Dictionary<string, StoredCredential> Store => _inner.Store;
        public Result? SaveFailure { get; set; }
        public int SaveCalls { get; private set; }
        public Result Save(string key, string user, string password)
        {
            SaveCalls++;
            return SaveFailure ?? _inner.Save(key, user, password);
        }

        public Result<StoredCredential> Get(string key) => _inner.Get(key);
        public bool Exists(string key) => _inner.Exists(key);
        public Result Delete(string key) => _inner.Delete(key);
    }

    private sealed class SnapshotConfigService : IConfigService
    {
        public Config Config { get; private set; } = new();
        public Result? LoadFailure { get; set; }
        public Result? SaveFailure { get; set; }
        public Result<Config> Load() => LoadFailure is null
            ? Result<Config>.Ok(JsonSerializer.Deserialize<Config>(JsonSerializer.Serialize(Config))!)
            : Result<Config>.Fail(LoadFailure);

        public Result Save(Config config)
        {
            if (SaveFailure is not null)
            {
                return SaveFailure;
            }

            Config = JsonSerializer.Deserialize<Config>(JsonSerializer.Serialize(config))!;
            return Result.Ok();
        }

        public Result Export(string path) => Result.Ok();
        public Result<Config> Import(string path) => Load();
    }
}
