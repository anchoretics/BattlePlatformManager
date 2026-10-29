namespace BPM.Infrastructure.Win32;

internal readonly record struct BrowserLoginRequest(int Version, CancellationToken Token);

/// <summary>由辅助服务的锁保护；新认证请求取消旧交接，旧任务不能覆盖新请求的状态。</summary>
internal sealed class BrowserLoginRequestSlot : IDisposable
{
    private CancellationTokenSource? _active;
    private int _version;
    private bool _disposed;

    internal BrowserLoginRequest Begin(CancellationToken lifetime)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _active?.Cancel();
        _active?.Dispose();
        _active = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        return new BrowserLoginRequest(++_version, _active.Token);
    }

    internal bool IsCurrent(BrowserLoginRequest request) => !_disposed && _active is not null && request.Version == _version;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _active?.Cancel();
        _active?.Dispose();
        _active = null;
    }
}
