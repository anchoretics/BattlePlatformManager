using System.Runtime.Versioning;
using BPM.Infrastructure.Win32;
using BPM.Shared;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BPM.Infrastructure.Tests;

/// <summary>真实 Windows Credential Manager 集成测试(CI 在 windows-latest 上执行)。</summary>
[SupportedOSPlatform("windows")]
public class CredentialManagerServiceTests
{
    private static CredentialManagerService CreateService() =>
        new(NullLogger<CredentialManagerService>.Instance);

    private static string UniqueKey() => $"BPM_TEST_{Guid.NewGuid():N}";

    [WindowsOnlyFact]
    public void SaveGetDelete_RoundTrips()
    {
        var service = CreateService();
        var key = UniqueKey();

        try
        {
            service.Save(key, "TestUser", "测试密码P@ss").IsSuccess.Should().BeTrue();
            service.Exists(key).Should().BeTrue();

            var credential = service.Get(key);
            credential.IsSuccess.Should().BeTrue();
            credential.Value.UserName.Should().Be("TestUser");
            credential.Value.Password.Should().Be("测试密码P@ss");
        }
        finally
        {
            service.Delete(key);
        }

        service.Exists(key).Should().BeFalse();
    }

    [WindowsOnlyFact]
    public void Get_MissingKey_ReturnsCredentialNotFound()
    {
        var result = CreateService().Get(UniqueKey());

        result.IsSuccess.Should().BeFalse();
        result.Code.Should().Be(ErrorCode.CredentialNotFound);
    }

    [WindowsOnlyFact]
    public void Delete_MissingKey_IsIdempotent()
    {
        CreateService().Delete(UniqueKey()).IsSuccess.Should().BeTrue();
    }

    [WindowsOnlyFact]
    public void Save_OverwritesExistingCredential()
    {
        var service = CreateService();
        var key = UniqueKey();

        try
        {
            service.Save(key, "User", "旧密码");
            service.Save(key, "User", "新密码");

            service.Get(key).Value.Password.Should().Be("新密码");
        }
        finally
        {
            service.Delete(key);
        }
    }
}
