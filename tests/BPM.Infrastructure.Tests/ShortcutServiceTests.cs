using System.Runtime.Versioning;
using BPM.Domain.Models;
using BPM.Infrastructure.Win32;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BPM.Infrastructure.Tests;

[SupportedOSPlatform("windows")]
public class ShortcutServiceTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SavedShortcutLetsProgramHandleElevationAndSingleInstanceForwarding(bool enabled)
    {
        var directory = Path.Combine(Path.GetTempPath(), "bpm-shortcut-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string? link = null;
        try
        {
            var service = new ShortcutService(NullLogger<ShortcutService>.Instance);
            var account = new Account { DisplayName = "验证", WindowsUser = "WoW1", BrowserLoginAssistEnabled = enabled };

            var result = service.CreateLaunchShortcut(account, Environment.ProcessPath!, directory);

            result.IsSuccess.Should().BeTrue();
            link = result.Value;
            var header = File.ReadAllBytes(link);
            BitConverter.ToUInt32(header, 0).Should().Be(0x4c); // SHELL_LINK_HEADER
            (BitConverter.ToUInt32(header, 0x14) & 0x2000).Should().Be(0);
        }
        finally
        {
            if (link is not null && Path.GetDirectoryName(Path.GetFullPath(link)) == Path.GetFullPath(directory)) File.Delete(link);
            Directory.Delete(directory);
        }
    }
}
