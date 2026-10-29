using System.Runtime.Versioning;
using BPM.Infrastructure.Win32;
using FluentAssertions;
using Xunit;

namespace BPM.Infrastructure.Tests;

[SupportedOSPlatform("windows")]
public class WindowsUserApiTests
{
    [Fact]
    public void CreateProfile_RespectsProfileServiceRpcPathBufferLimit()
    {
        var called = false;
        var api = new WindowsUserApi((sid, userName, path, length) =>
        {
            called = true;
            sid.Should().Be("S-1-5-21-100-200-300-1002");
            userName.Should().Be("战网用户");
            // 模拟 Profile Service 对过长路径缓冲区的 RPC 错误,回归 0x800706F7。
            if (length > 260)
            {
                return unchecked((int)0x800706F7);
            }

            path.Capacity.Should().Be((int)length);
            path.Append(@"C:\Users\战网用户");
            return 0;
        });

        api.CreateProfile("S-1-5-21-100-200-300-1002", "战网用户").Should().Be(0);
        called.Should().BeTrue();
    }

    [Fact]
    public void CreateProfile_PreservesNativeHResult()
    {
        var api = new WindowsUserApi((_, _, _, _) => unchecked((int)0x80070005));

        api.CreateProfile("S-1-5-21-100-200-300-1002", "WoW2").Should().Be(unchecked((int)0x80070005));
    }
}
