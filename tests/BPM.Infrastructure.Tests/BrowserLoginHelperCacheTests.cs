using System.Runtime.Versioning;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using BPM.Infrastructure.Win32;
using FluentAssertions;
using Xunit;

namespace BPM.Infrastructure.Tests;

[SupportedOSPlatform("windows")]
public class BrowserLoginHelperCacheTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HardLinkIsReplacedWithoutChangingExternalContentOrPermissions(bool matchingContent)
    {
        var temporary = Path.Combine(Path.GetTempPath(), "bpm-helper-cache-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            var source = Path.Combine(temporary, "source");
            Directory.CreateDirectory(source);
            var executable = Path.Combine(source, "BPM.exe");
            File.WriteAllText(executable, "program");
            var unrelated = Path.Combine(temporary, "unrelated.txt");
            var originalContent = matchingContent ? "program" : "unrelated data";
            File.WriteAllText(unrelated, originalContent);
            using var identity = WindowsIdentity.GetCurrent();
            var target = new SecurityIdentifier("S-1-5-21-11-22-33-1042");
            var cache = new BrowserLoginHelperCache(Path.Combine(temporary, "cache"));
            var cached = cache.Prepare(executable, identity.User!, target);
            File.Delete(cached);
            CreateHardLink(cached, unrelated, IntPtr.Zero).Should().BeTrue();
            var originalAcl = new FileInfo(unrelated).GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.All);

            cache.Prepare(executable, identity.User!, target).Should().Be(cached);

            File.ReadAllText(unrelated).Should().Be(originalContent);
            new FileInfo(unrelated).GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.All).Should().Be(originalAcl);
            File.ReadAllText(cached).Should().Be("program");
            File.WriteAllText(unrelated, "external change");
            File.ReadAllText(cached).Should().Be("program");
            // 完整缓存可直接复用，即便助手文件被另一个进程只读打开。
            using var active = new FileStream(cached, FileMode.Open, FileAccess.Read, FileShare.Read);
            cache.Prepare(executable, identity.User!, target).Should().Be(cached);
        }
        finally
        {
            var checkedPath = Path.GetFullPath(temporary);
            if (Path.GetDirectoryName(checkedPath) == Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) &&
                Path.GetFileName(checkedPath).StartsWith("bpm-helper-cache-test-", StringComparison.Ordinal))
                Directory.Delete(checkedPath, recursive: true);
        }
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLink(string fileName, string existingFile, IntPtr attributes);

    [Fact]
    public void CopiesOnlyProgramFilesGrantsTargetReadExecuteAndChangesVersionAfterRebuild()
    {
        var temporary = Path.Combine(Path.GetTempPath(), "bpm-helper-cache-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            var source = Path.Combine(temporary, "source");
            Directory.CreateDirectory(source);
            var executable = Path.Combine(source, "BPM.exe");
            File.WriteAllText(executable, "app-v1");
            File.WriteAllText(Path.Combine(source, "BPM.dll"), "managed");
            File.WriteAllText(Path.Combine(source, "BPM.runtimeconfig.json"), "{}");
            File.WriteAllText(Path.Combine(source, "config.json"), "private config must not be copied");
            File.WriteAllText(Path.Combine(source, "bpm.log"), "private log must not be copied");
            using var identity = WindowsIdentity.GetCurrent();
            var target = new SecurityIdentifier("S-1-5-21-11-22-33-1042");
            var cache = new BrowserLoginHelperCache(Path.Combine(temporary, "cache"));

            var first = cache.Prepare(executable, identity.User!, target);

            File.ReadAllText(first).Should().Be("app-v1");
            Directory.GetFiles(Path.GetDirectoryName(first)!).Select(Path.GetFileName).Should()
                .BeEquivalentTo("BPM.exe", "BPM.dll", "BPM.runtimeconfig.json");
            var rule = new FileInfo(first).GetAccessControl().GetAccessRules(true, true, typeof(SecurityIdentifier))
                .Cast<FileSystemAccessRule>().Single(r => r.IdentityReference.Equals(target));
            rule.AccessControlType.Should().Be(AccessControlType.Allow);
            (rule.FileSystemRights & FileSystemRights.ReadAndExecute).Should().Be(FileSystemRights.ReadAndExecute);
            (rule.FileSystemRights & FileSystemRights.Write).Should().Be(0);
            new FileInfo(executable).GetAccessControl().GetAccessRules(true, true, typeof(SecurityIdentifier))
                .Cast<FileSystemAccessRule>().Should().NotContain(r => r.IdentityReference.Equals(target));

            cache.Prepare(executable, identity.User!, target).Should().Be(first);
            File.WriteAllText(executable, "app-v2");
            var second = cache.Prepare(executable, identity.User!, target);
            second.Should().NotBe(first);
            File.ReadAllText(second).Should().Be("app-v2");
            File.ReadAllText(first).Should().Be("app-v1");
        }
        finally
        {
            var checkedPath = Path.GetFullPath(temporary);
            if (Path.GetDirectoryName(checkedPath) == Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) &&
                Path.GetFileName(checkedPath).StartsWith("bpm-helper-cache-test-", StringComparison.Ordinal))
                Directory.Delete(checkedPath, recursive: true);
        }
    }
}
