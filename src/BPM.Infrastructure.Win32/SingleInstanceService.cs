using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace BPM.Infrastructure.Win32;

public sealed record SingleInstanceCommand(Guid? AccountId = null)
{
    public static SingleInstanceCommand FromArguments(IReadOnlyList<string> arguments)
    {
        for (var index = 0; index + 1 < arguments.Count; index++)
            if (arguments[index] == "--launch" && Guid.TryParse(arguments[index + 1], out var id) && id != Guid.Empty)
                return new(id);
        return new();
    }
}

public enum InstanceForwardResult { NotRunning, Forwarded, Unavailable }

/// <summary>同一 Windows 用户/登录会话共用一个管理员主进程。IPC 仅传激活或配置 GUID。</summary>
[SupportedOSPlatform("windows")]
public sealed class SingleInstanceService : IDisposable
{
    private readonly object _gate = new();
    private readonly SecurityIdentifier _sid;
    private readonly int _sessionId;
    private readonly string _leaseName;
    private readonly string _pipeName;
    private readonly bool _requireElevatedServer;
    private readonly CancellationTokenSource _stop = new();
    private EventWaitHandle? _lease;
    private NamedPipeServerStream? _listener;
    private bool _disposed;
    internal string PipeName => _pipeName;

    public SingleInstanceService() : this("BPM-Main-v1", true) { }

    internal SingleInstanceService(string scope, bool requireElevatedServer)
    {
        using var identity = WindowsIdentity.GetCurrent();
        _sid = identity.User ?? throw new InvalidOperationException("No Windows identity.");
        using var process = Process.GetCurrentProcess();
        _sessionId = process.SessionId;
        var name = $"{scope}-{_sid.Value}-{_sessionId}";
        _leaseName = "Local\\" + name;
        _pipeName = name;
        _requireElevatedServer = requireElevatedServer;
    }

    public async Task<InstanceForwardResult> TryForwardAsync(SingleInstanceCommand command)
    {
        if (!OwnerExists()) return InstanceForwardResult.NotRunning;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            using var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false);
            if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var pid) || !TrustedPeer(pid, _requireElevatedServer))
                return InstanceForwardResult.Unavailable;
            if (command.AccountId is null) AllowSetForegroundWindow(pid);
            var packet = new byte[17];
            if (command.AccountId is { } accountId)
            {
                if (accountId == Guid.Empty) return InstanceForwardResult.Unavailable;
                packet[0] = 1;
                accountId.TryWriteBytes(packet.AsSpan(1));
            }
            await pipe.WriteAsync(packet, timeout.Token).ConfigureAwait(false);
            await pipe.FlushAsync(timeout.Token).ConfigureAwait(false);
            var acknowledgement = new byte[1];
            await pipe.ReadExactlyAsync(acknowledgement, timeout.Token).ConfigureAwait(false);
            // 告知服务端已读到响应，避免 Disconnect 丢弃尚未读取的确认字节。
            try
            {
                await pipe.WriteAsync(new byte[] { 1 }, timeout.Token).ConfigureAwait(false);
                await pipe.FlushAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException) { }
            return acknowledgement[0] == 1 ? InstanceForwardResult.Forwarded : InstanceForwardResult.Unavailable;
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or UnauthorizedAccessException)
        {
            // 发送后确认丢失也不能自行再次执行 --launch，以免重复启动游戏。
            return InstanceForwardResult.Unavailable;
        }
    }

    public bool TryStartPrimary(Func<SingleInstanceCommand, bool> accept)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_lease is not null) throw new InvalidOperationException("Already the primary instance.");
            var lease = new EventWaitHandle(false, EventResetMode.ManualReset, _leaseName, out var created);
            if (!created) { lease.Dispose(); return false; }
            try
            {
                _listener = CreateListener();
                _lease = lease;
                _ = ListenAsync(accept);
                return true;
            }
            catch { lease.Dispose(); throw; }
        }
    }

    private NamedPipeServerStream CreateListener(bool first = true)
    {
        var security = new PipeSecurity();
        security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(new PipeAccessRule(_sid, PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl, AccessControlType.Allow));
        return NamedPipeServerStreamAcl.Create(_pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | (first ? PipeOptions.FirstPipeInstance : PipeOptions.None), 256, 256, security);
    }

    private async Task ListenAsync(Func<SingleInstanceCommand, bool> accept)
    {
        while (!_stop.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe;
            lock (_gate) pipe = _listener;
            if (pipe is null) return;
            try
            {
                await pipe.WaitForConnectionAsync(_stop.Token).ConfigureAwait(false);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(2));
                var accepted = false;
                if (GetNamedPipeClientProcessId(pipe.SafePipeHandle, out var pid) && TrustedPeer(pid, false))
                {
                    var packet = new byte[17];
                    await pipe.ReadExactlyAsync(packet, timeout.Token).ConfigureAwait(false);
                    var id = new Guid(packet.AsSpan(1));
                    try
                    {
                        if (packet[0] == 0 && id == Guid.Empty) accepted = accept(new());
                        else if (packet[0] == 1 && id != Guid.Empty) accepted = accept(new(id));
                    }
                    catch { accepted = false; }
                }
                await pipe.WriteAsync(new byte[] { accepted ? (byte)1 : (byte)0 }, timeout.Token).ConfigureAwait(false);
                await pipe.FlushAsync(timeout.Token).ConfigureAwait(false);
                // Flush 不保证对端已读取。完成握手/等对端关闭后，才复用此管道实例。
                var receipt = new byte[1];
                await pipe.ReadExactlyAsync(receipt, timeout.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or UnauthorizedAccessException or ObjectDisposedException)
            {
                // 单个断开的客户端不结束主实例监听。
            }
            finally
            {
                lock (_gate)
                {
                    if (!_disposed)
                    {
                        try { pipe.Disconnect(); }
                        catch (Exception ex) when (ex is IOException or InvalidOperationException)
                        {
                            pipe.Dispose();
                            _listener = CreateListener(first: false);
                        }
                    }
                }
            }
        }
    }

    private bool OwnerExists()
    {
        try
        {
            using var lease = EventWaitHandle.OpenExisting(_leaseName);
            return true;
        }
        catch (WaitHandleCannotBeOpenedException) { return false; }
        catch (UnauthorizedAccessException) { return true; }
    }

    private bool TrustedPeer(uint pid, bool requireElevated)
    {
        try
        {
            using var process = Process.GetProcessById(checked((int)pid));
            if (process.SessionId != _sessionId) return false;
            using var handle = OpenProcess(0x1000, false, pid);
            if (handle.IsInvalid || !OpenProcessToken(handle, 8, out var token)) return false;
            using (token)
            using (var identity = new WindowsIdentity(token.DangerousGetHandle()))
            {
                if (identity.User is null || !_sid.Equals(identity.User)) return false;
                return !requireElevated || (GetTokenInformation(token, 20, out var elevated, sizeof(int), out _) && elevated != 0);
            }
        }
        catch { return false; }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _stop.Cancel();
            _listener?.Dispose();
            _listener = null;
            _lease?.Dispose();
            _lease = null;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint pid);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint pid);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeProcessHandle OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(SafeProcessHandle process, uint access, out SafeAccessTokenHandle token);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool GetTokenInformation(SafeAccessTokenHandle token, int type, out int value, int length, out int returned);
    [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(uint pid);
}
