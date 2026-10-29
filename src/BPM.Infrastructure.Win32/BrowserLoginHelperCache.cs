using System.Runtime.Versioning;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace BPM.Infrastructure.Win32;

/// <summary>私有安装目录可能不能被目标用户读取；仅缓存程序文件，并显式授予目标 SID 读取/执行。</summary>
[SupportedOSPlatform("windows")]
internal sealed class BrowserLoginHelperCache(string? root = null)
{
    private static readonly object Gate = new();
    private static readonly SecurityIdentifier Administrators = new(WellKnownSidType.BuiltinAdministratorsSid, null);
    private static readonly SecurityIdentifier System = new(WellKnownSidType.LocalSystemSid, null);
    private readonly string _root = root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "BattlePlatformManager.LoginHelpers");

    internal string Prepare(string executable, SecurityIdentifier operatorSid, SecurityIdentifier targetSid)
    {
        lock (Gate)
        {
            executable = Path.GetFullPath(executable);
            var sourceDirectory = Path.GetDirectoryName(executable)!;
            var sources = Directory.EnumerateFiles(sourceDirectory).Where(p =>
                    p.Equals(executable, StringComparison.OrdinalIgnoreCase) ||
                    p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
                    p.EndsWith(".runtimeconfig.json", StringComparison.OrdinalIgnoreCase) ||
                    p.EndsWith(".deps.json", StringComparison.OrdinalIgnoreCase))
                .OrderBy(Path.GetFileName, StringComparer.Ordinal).Select(p => (Path: p, Hash: Hash(p))).ToArray();
            if (!sources.Any(s => s.Path.Equals(executable, StringComparison.OrdinalIgnoreCase))) throw new FileNotFoundException();
            var manifest = string.Join('\n', sources.Select(s => Path.GetFileName(s.Path) + ":" + s.Hash));
            var version = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(manifest)))[..32];

            SecureDirectory(_root, operatorSid, null);
            var operatorRoot = Path.Combine(_root, operatorSid.Value);
            SecureDirectory(operatorRoot, operatorSid, null);
            var targetRoot = Path.Combine(operatorRoot, targetSid.Value);
            SecureDirectory(targetRoot, operatorSid, targetSid);
            var versionRoot = Path.Combine(targetRoot, version);
            SecureDirectory(versionRoot, operatorSid, targetSid);
            foreach (var source in sources)
            {
                var destination = Path.Combine(versionRoot, Path.GetFileName(source.Path));
                RejectReparsePoint(destination);
                var security = new FileSecurity();
                security.SetAccessRuleProtection(true, false);
                foreach (var sid in new[] { operatorSid, Administrators, System }.Distinct())
                    security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl, AccessControlType.Allow));
                security.AddAccessRule(new FileSystemAccessRule(targetSid, FileSystemRights.ReadAndExecute, AccessControlType.Allow));
                if (!CanReuse(destination, source.Hash, security))
                    Publish(source.Path, source.Hash, destination, security);
            }
            CleanupOldVersions(targetRoot, versionRoot);
            return Path.Combine(versionRoot, Path.GetFileName(executable));
        }
    }

    private static bool CanReuse(string destination, string expectedHash, FileSecurity security)
    {
        if (!File.Exists(destination)) return false;
        // 在同一个只读句柄上检查链接数、ACL 和内容，绝不修改已有文件的内容或权限。
        using var stream = new FileStream(destination, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (!GetFileInformationByHandle(stream.SafeFileHandle, out var information))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        if (information.NumberOfLinks != 1 || (information.Attributes & FileAttributes.ReparsePoint) != 0)
            return false;
        return AccessRulesMatch(stream.GetAccessControl(), security) &&
               Convert.ToHexString(SHA256.HashData(stream)) == expectedHash;
    }

    private static void Publish(string source, string expectedHash, string destination, FileSecurity security)
    {
        var temporary = Path.Combine(Path.GetDirectoryName(destination)!, Guid.NewGuid().ToString("N") + ".tmp");
        var created = false;
        try
        {
            using (var output = new FileInfo(temporary).Create(FileMode.CreateNew, FileSystemRights.FullControl,
                       FileShare.None, 4096, FileOptions.None, security))
            {
                created = true;
                using (var input = File.OpenRead(source)) input.CopyTo(output);
                output.Position = 0;
                if (Convert.ToHexString(SHA256.HashData(output)) != expectedHash)
                    throw new IOException("Helper changed during preparation.");
                output.Flush(flushToDisk: true);
            }
            // 替换目录项，不跟随旧文件的硬链接；新文件从创建起便使用受限 ACL。
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            if (created && File.Exists(temporary)) File.Delete(temporary);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileInformation
    {
        public FileAttributes Attributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime, LastAccessTime, LastWriteTime;
        public uint VolumeSerialNumber, FileSizeHigh, FileSizeLow, NumberOfLinks, FileIndexHigh, FileIndexLow;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle file, out FileInformation information);

    private static string Hash(string file)
    {
        RejectReparsePoint(file);
        using var stream = File.OpenRead(file);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static void SecureDirectory(string path, SecurityIdentifier operatorSid, SecurityIdentifier? targetSid)
    {
        RejectReparsePoint(path);
        var existed = Directory.Exists(path);
        var directory = Directory.CreateDirectory(path);
        var existingSecurity = directory.GetAccessControl();
        var owner = existingSecurity.GetOwner(typeof(SecurityIdentifier));
        if (!operatorSid.Equals(owner) && !Administrators.Equals(owner) && !System.Equals(owner))
            throw new UnauthorizedAccessException("Untrusted helper cache owner.");
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(true, false);
        foreach (var sid in new[] { operatorSid, Administrators, System }.Distinct())
            security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        if (targetSid is not null)
            security.AddAccessRule(new FileSystemAccessRule(targetSid, FileSystemRights.ReadAndExecute,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        else
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
                FileSystemRights.Traverse, AccessControlType.Allow));
        // 反复设置可继承 ACL 会由 Windows 传播至子文件，也可能碰到被替换的硬链接。
        // 已存在的缓存目录必须保持预期权限；异常时交给辅助的手动登录兜底。
        if (existed)
        {
            if (!AccessRulesMatch(existingSecurity, security))
                throw new UnauthorizedAccessException("Unexpected helper cache directory permissions.");
        }
        else directory.SetAccessControl(security);
    }

    private static bool AccessRulesMatch(FileSystemSecurity actual, FileSystemSecurity expected)
    {
        static IEnumerable<(string, AccessControlType, FileSystemRights, InheritanceFlags, PropagationFlags, bool)>
            Rules(FileSystemSecurity security) => security.GetAccessRules(true, true, typeof(SecurityIdentifier))
                .Cast<FileSystemAccessRule>().Select(rule => (rule.IdentityReference.Value, rule.AccessControlType,
                    rule.FileSystemRights, rule.InheritanceFlags, rule.PropagationFlags, rule.IsInherited)).OrderBy(rule => rule);
        return actual.AreAccessRulesProtected == expected.AreAccessRulesProtected && Rules(actual).SequenceEqual(Rules(expected));
    }

    private static void RejectReparsePoint(string path)
    {
        if ((File.Exists(path) || Directory.Exists(path)) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new UnauthorizedAccessException("Helper cache cannot contain reparse points.");
    }

    private static void CleanupOldVersions(string targetRoot, string current)
    {
        // 仅清理此目标 SID 的扁平程序缓存，保留两版；绝不递归删除或跟随链接。
        foreach (var directory in new DirectoryInfo(targetRoot).EnumerateDirectories()
                     .Where(d => d.Name.Length == 32 && d.Name.All(Uri.IsHexDigit))
                     .OrderByDescending(d => d.LastWriteTimeUtc).Skip(2))
        {
            var path = Path.GetFullPath(directory.FullName);
            if (path.Equals(current, StringComparison.OrdinalIgnoreCase) ||
                !path.StartsWith(Path.GetFullPath(targetRoot) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                RejectReparsePoint(path);
                if (directory.EnumerateDirectories().Any()) continue;
                var files = directory.EnumerateFiles().ToArray();
                if (files.Any(f => (f.Attributes & FileAttributes.ReparsePoint) != 0)) continue;
                foreach (var file in files) file.Delete();
                directory.Delete();
            }
            catch (IOException) { /* 旧助手仍在使用，留给下一轮清理。 */ }
            catch (UnauthorizedAccessException) { /* 不放宽缓存权限。 */ }
        }
    }
}
