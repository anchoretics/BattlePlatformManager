using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using BPM.Application.Abstractions;
using BPM.Application.Models;

namespace BPM.Infrastructure.Win32;

/// <summary>跨用户短期内存管道。URL 不进入 CPWL 的 1024 字符命令行，也不写临时文件。</summary>
[SupportedOSPlatform("windows")]
internal static class BrowserLoginPipe
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeClientProcessId(Microsoft.Win32.SafeHandles.SafePipeHandle pipe, out uint pid);

    internal static async Task<bool> OpenAsync(string helperPath, SecurityIdentifier targetSid,
        string user, string domain, string password, string url, DateTime eventUtc,
        IProcessLauncher launcher, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var operatorSid = WindowsIdentity.GetCurrent().User!;
        var security = new PipeSecurity();
        security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(new PipeAccessRule(operatorSid, PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(targetSid, PipeAccessRights.ReadWrite, AccessControlType.Allow));
        var name = "BPM-Login-" + Guid.NewGuid().ToString("N");
        using var pipe = NamedPipeServerStreamAcl.Create(name, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 4096, 4096, security);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(12));
        // 建立监听后再创建目标用户助手，只有短管道名和操作者 PID 出现在命令行。
        var connected = pipe.WaitForConnectionAsync(timeout.Token);
        timeout.Token.ThrowIfCancellationRequested();
        var launched = launcher.Launch(new LaunchRequest
        {
            User = user, Domain = domain, Password = password, ExecutablePath = helperPath,
            Arguments = $"--browser-login-pipe {name} --server-pid {Environment.ProcessId}",
            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System),
        });
        if (!launched.IsSuccess) return false;
        await connected.ConfigureAwait(false);
        timeout.Token.ThrowIfCancellationRequested();
        if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle, out var pid) || pid != launched.Value) return false;
        if (!BrowserAuthenticationPolicy.IsFresh(eventUtc, DateTime.UtcNow)) return false;
        var bytes = Encoding.UTF8.GetBytes(url);
        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, bytes.Length);
        await pipe.WriteAsync(header, timeout.Token).ConfigureAwait(false);
        await pipe.WriteAsync(bytes, timeout.Token).ConfigureAwait(false);
        await pipe.FlushAsync(timeout.Token).ConfigureAwait(false);
        var response = new byte[1];
        await pipe.ReadExactlyAsync(response, timeout.Token).ConfigureAwait(false);
        return response[0] == 1;
    }
}

/// <summary>BPM 自身以配置用户运行的最小浏览器助手。入口必须在 DI/日志/主窗口初始化前调用。</summary>
[SupportedOSPlatform("windows")]
public static class BrowserLoginHelper
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeServerProcessId(Microsoft.Win32.SafeHandles.SafePipeHandle pipe, out uint pid);

    public static Task<int> RunAsync(string pipeName, int serverPid) => RunCoreAsync(pipeName, serverPid, OpenBrowser);

    internal static async Task<int> RunCoreAsync(string pipeName, int serverPid, Func<string, bool> openBrowser)
    {
        if (!pipeName.StartsWith("BPM-Login-", StringComparison.Ordinal) ||
            !Guid.TryParseExact(pipeName[10..], "N", out _) || serverPid <= 0) return 1;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false);
            if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var pid) || pid != serverPid) return 1;
            var header = new byte[4];
            await pipe.ReadExactlyAsync(header, timeout.Token).ConfigureAwait(false);
            var length = BinaryPrimitives.ReadInt32LittleEndian(header);
            if (length is <= 0 or > BrowserAuthenticationPolicy.MaxUrlBytes) return 1;
            var bytes = new byte[length];
            await pipe.ReadExactlyAsync(bytes, timeout.Token).ConfigureAwait(false);
            var url = new UTF8Encoding(false, true).GetString(bytes);
            var opened = BrowserAuthenticationPolicy.IsAllowed(url) && openBrowser(url);
            await pipe.WriteAsync(new byte[] { opened ? (byte)1 : (byte)0 }, timeout.Token).ConfigureAwait(false);
            await pipe.FlushAsync(timeout.Token).ConfigureAwait(false);
            return opened ? 0 : 1;
        }
        catch
        {
            // 不记录异常消息或命令行，避免地址/认证参数泄漏。
            return 1;
        }
    }

    internal static string? FindBrowser() => BrowserCandidates().FirstOrDefault(File.Exists);

    private static IEnumerable<string> BrowserCandidates()
    {
        // 不调用 ShellExecute 或修改默认浏览器；只使用机器级可信安装路径。
        foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) }.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrEmpty(root)) yield return Path.Combine(root, "Microsoft", "Edge", "Application", "msedge.exe");
        }
        foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) }.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrEmpty(root)) yield return Path.Combine(root, "Google", "Chrome", "Application", "chrome.exe");
        }
    }

    private static bool OpenBrowser(string url)
    {
        foreach (var browser in BrowserCandidates().Distinct(StringComparer.OrdinalIgnoreCase).Where(File.Exists))
        {
            try
            {
                var start = new ProcessStartInfo(browser) { UseShellExecute = false };
                start.ArgumentList.Add("--no-first-run");
                start.ArgumentList.Add("--no-default-browser-check");
                start.ArgumentList.Add("--new-window");
                start.ArgumentList.Add(url);
                using var process = Process.Start(start);
                if (process is not null) return true;
            }
            catch { /* 当前浏览器无法创建进程时继续尝试机器级 Chrome，不记录含 URL 的异常。 */ }
        }
        return false;
    }
}
