using System.Runtime.Versioning;
using BPM.Infrastructure.Win32;
using BPM.Shared;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BPM.Infrastructure.Tests;

/// <summary>仅使用原生 API 替身,不会在开发机或 CI 中创建真实 Windows 用户。</summary>
[SupportedOSPlatform("windows")]
public class WindowsUserServiceTests
{
    private readonly FakeWindowsUserApi _api = new();
    private readonly WindowsUserService _service;

    public WindowsUserServiceTests()
    {
        _service = new WindowsUserService(NullLogger<WindowsUserService>.Instance, _api);
    }

    [Fact]
    public void NonAdministrator_CannotCreateOrInitializeUser()
    {
        _api.IsAdministrator = false;

        _service.CreateLocalUser("WoW2", "synthetic-password").Code.Should().Be(ErrorCode.AdministratorRequired);
        _service.InitializeProfile("WoW2").Code.Should().Be(ErrorCode.AdministratorRequired);

        _api.Operations.Should().BeEmpty();
    }

    [Theory]
    [InlineData(5, ErrorCode.AdministratorRequired)]
    [InlineData(2224, ErrorCode.WindowsUserAlreadyExists)]
    [InlineData(2245, ErrorCode.ValidationFailed)]
    [InlineData(2202, ErrorCode.ValidationFailed)]
    [InlineData(87, ErrorCode.ValidationFailed)]
    [InlineData(1234, ErrorCode.WindowsUserCreationFailed)]
    public void CreateLocalUser_WindowsError_IsReportedWithoutPassword(int error, ErrorCode expected)
    {
        _api.AddError = error;

        var result = _service.CreateLocalUser("WoW2", "synthetic-password");

        result.Code.Should().Be(expected);
        result.Win32Error.Should().Be(error);
        result.Message.Should().NotContain("synthetic-password");
        _api.Operations.Should().Equal("add");
    }

    [Fact]
    public void CreateLocalUser_ValidInput_AddsUser()
    {
        _service.CreateLocalUser("WoW2", "synthetic-password").IsSuccess.Should().BeTrue();

        _api.UserName.Should().Be("WoW2");
        _api.Password.Should().Be("synthetic-password");
        _api.Operations.Should().Equal("add");
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid\0password")]
    public void CreateLocalUser_InvalidPassword_DoesNotCallWindows(string password)
    {
        _service.CreateLocalUser("WoW2", password).Code.Should().Be(ErrorCode.ValidationFailed);

        _api.Operations.Should().BeEmpty();
    }

    [Fact]
    public void InitializeProfile_ResolvesLocalSidAndAddsUsersMembershipBeforeCreatingProfile()
    {
        _service.InitializeProfile("WoW2").IsSuccess.Should().BeTrue();

        _api.Operations.Should().Equal("sid", "users-group", "profile");
        _api.ProfileSid.Should().Be("S-1-5-21-100-200-300-1002");
        _api.UserName.Should().Be("WoW2");
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1378, 0)]
    [InlineData(0, unchecked((int)0x800700B7))]
    [InlineData(1378, unchecked((int)0x800700B7))]
    public void InitializeProfile_AlreadyInitialized_IsIdempotent(int groupStatus, int profileStatus)
    {
        _api.GroupError = groupStatus;
        _api.ProfileHResult = profileStatus;

        _service.InitializeProfile("WoW2").IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void InitializeProfile_UnknownUser_DoesNotChangeGroupsOrCreateProfile()
    {
        _api.SidError = 2221;

        var result = _service.InitializeProfile("MissingUser");

        result.Code.Should().Be(ErrorCode.UserProfileInitializationFailed);
        result.Win32Error.Should().Be(2221);
        _api.Operations.Should().Equal("sid");
    }

    [Fact]
    public void InitializeProfile_GroupFailure_DoesNotCreateProfile()
    {
        _api.GroupError = 5;

        _service.InitializeProfile("WoW2").Code.Should().Be(ErrorCode.UserProfileInitializationFailed);

        _api.Operations.Should().Equal("sid", "users-group");
    }

    [Fact]
    public void InitializeProfile_ProfileFailure_PreservesHResult()
    {
        _api.ProfileHResult = unchecked((int)0x80070005);

        var result = _service.InitializeProfile("WoW2");

        result.Code.Should().Be(ErrorCode.UserProfileInitializationFailed);
        result.Win32Error.Should().Be(_api.ProfileHResult);
        result.Message.Should().Contain("80070005");
    }

    private sealed class FakeWindowsUserApi : IWindowsUserApi
    {
        public bool IsAdministrator { get; set; } = true;
        public int AddError { get; set; }
        public int SidError { get; set; }
        public int GroupError { get; set; }
        public int ProfileHResult { get; set; }
        public string? UserName { get; private set; }
        public string? Password { get; private set; }
        public string? ProfileSid { get; private set; }
        public List<string> Operations { get; } = new();

        public int AddUser(string userName, string password)
        {
            Operations.Add("add");
            UserName = userName;
            Password = password;
            return AddError;
        }

        public int GetUserSid(string userName, out string sid)
        {
            Operations.Add("sid");
            UserName = userName;
            sid = "S-1-5-21-100-200-300-1002";
            return SidError;
        }

        public int AddToUsersGroup(string userName)
        {
            Operations.Add("users-group");
            return GroupError;
        }

        public int CreateProfile(string sid, string userName)
        {
            Operations.Add("profile");
            ProfileSid = sid;
            return ProfileHResult;
        }
    }
}
