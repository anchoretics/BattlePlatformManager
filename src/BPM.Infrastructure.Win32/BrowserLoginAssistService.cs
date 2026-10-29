using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text;
using BPM.Application.Abstractions;
using BPM.Application.Models;
using BPM.Domain.Models;
using BPM.Domain.Validation;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;
using Microsoft.Extensions.Logging;

namespace BPM.Infrastructure.Win32;

/// <summary>
/// 实时捕获战网交给 Windows Shell 的完整认证地址，并交给同一用户的浏览器。
/// 不保存 ETL/URL，不修改默认应用；Shell 事件不是稳定公开契约，失败时降级为人工指引。
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class BrowserLoginAssistService : IBrowserLoginAssist, IDisposable
{
    private static readonly Guid ShellProvider = new("30336ed4-e327-447c-9de0-51b652c86108");
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);
    private readonly object _gate = new();
    private readonly Dictionary<Guid, Context> _contexts = new();
    private readonly Dictionary<Guid, LoginAssistStatus> _statuses = new();
    private readonly ICredentialService _credentials;
    private readonly IProcessLauncher _launcher;
    private readonly ILogger<BrowserLoginAssistService> _logger;
    private readonly string _helperPath;
    private readonly int _desktopSession = Process.GetCurrentProcess().SessionId;
    private readonly Timer _timer;
    private TraceEventSession? _session;
    private bool _disposed;

    public BrowserLoginAssistService(ICredentialService credentials, IProcessLauncher launcher,
        ILogger<BrowserLoginAssistService> logger, string helperPath)
    {
        _credentials = credentials;
        _launcher = launcher;
        _logger = logger;
        _helperPath = helperPath;
        _timer = new Timer(_ => Expire(), null, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3));
    }

    public event Action<LoginAssistStatus>? StatusChanged;

    public LoginAssistStatus? GetStatus(Guid accountId)
    {
        lock (_gate) return _statuses.GetValueOrDefault(accountId);
    }

    public LoginAssistStatus Start(Account account)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!account.BrowserLoginAssistEnabled)
            {
                StopCore(account.Id);
                return Publish(account.Id, LoginAssistState.Disabled, "已关闭辅助，使用系统浏览器登录流程");
            }
            if (_contexts.TryGetValue(account.Id, out var current))
            {
                if (current.WindowsUser.Equals(account.WindowsUser, StringComparison.OrdinalIgnoreCase) &&
                    current.BattleNetPath.Equals(account.BattleNetPath, StringComparison.OrdinalIgnoreCase) && current.CredentialKey == account.CredentialKey)
                {
                    // 支持取消登录、重试和客户端重启；请求指纹保留以避免重放。
                    current.ExpiresUtc = DateTime.UtcNow + Lifetime;
                    return Publish(account.Id, LoginAssistState.Ready, "登录辅助已就绪（15 分钟），请在战网重新点击登录");
                }
                StopCore(account.Id);
            }
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                if (identity.User is null) return Manual(account.Id, "无法识别当前 Windows 用户，请查看手动登录指引");
                var (domain, user) = AccountValidator.SplitWindowsUser(account.WindowsUser);
                var sid = ResolveSid(domain, user);
                if (sid.Equals(identity.User))
                    return Publish(account.Id, LoginAssistState.Disabled, "此配置绑定当前桌面用户，使用战网原生登录即可");
                if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
                    return Manual(account.Id, "登录辅助需要以管理员身份运行 BPM；也可手动登录一次对应 Windows 桌面");
                if (BrowserLoginHelper.FindBrowser() is null || !File.Exists(_helperPath))
                    return Manual(account.Id, "未找到机器级 Edge/Chrome 或 BPM 助手；请查看手动登录指引");

                var root = Path.GetDirectoryName(Path.GetFullPath(account.BattleNetPath));
                if (string.IsNullOrEmpty(root)) return Manual(account.Id, "战网安装路径无效，无法安全启用登录辅助");

                // 同一操作者、同一目标用户只允许一个 BPM 实例转交，避免重复网页。
                var lease = new EventWaitHandle(false, EventResetMode.ManualReset,
                    $"Local\\BPM-Login-{identity.User!.Value}-{sid.Value}", out var created);
                if (!created)
                {
                    lease.Dispose();
                    return Publish(account.Id, LoginAssistState.AlreadyActive,
                        "另一个 BPM 实例正在辅助此 Windows 用户；若无响应，请关闭旧实例后重试");
                }
                var context = new Context(account, domain, user, sid, root, lease);
                try
                {
                    context.HelperPath = new BrowserLoginHelperCache().Prepare(_helperPath, identity.User, sid);
                    _contexts.Add(account.Id, context);
                    EnsureSession();
                }
                catch { lease.Dispose(); StopCore(account.Id); throw; }
                return Publish(account.Id, LoginAssistState.Ready,
                    "登录辅助已就绪（15 分钟）；需要认证时会打开该用户的浏览器");
            }
            catch (Exception ex)
            {
                LogFailure("启动", ex);
                return Manual(account.Id, "当前系统无法启用登录辅助；战网仍可启动，请查看手动登录指引");
            }
        }
    }

    public void Stop(Guid accountId)
    {
        lock (_gate)
        {
            StopCore(accountId);
            Publish(accountId, LoginAssistState.Disabled, "登录辅助已停止");
        }
    }

    public async Task WaitForIdleAsync()
    {
        while (true)
        {
            lock (_gate) { if (_disposed || _contexts.Count == 0) return; }
            await Task.Delay(500).ConfigureAwait(false);
        }
    }

    private void EnsureSession()
    {
        if (_session is not null) return;
        var prefix = $"BPM-Login-{WindowsIdentity.GetCurrent().User!.Value}-";
        CleanupStaleSessions(prefix);
        var session = new TraceEventSession($"{prefix}{Environment.ProcessId}-{Guid.NewGuid():N}") { StopOnDispose = true };
        try
        {
            session.Source.Dynamic.All += Observe;
            // EnableProvider 的 bool 表示“是否重启已有会话”，false 是新会话的正常结果。
            // 启用失败会抛异常；额外核对 IsActive，而不是把返回 false 视为失败。
            session.EnableProvider(ShellProvider, TraceEventLevel.Verbose, ulong.MaxValue,
                new TraceEventProviderOptions { ProcessNameFilter = ["Battle.net.exe"] });
            if (!session.IsActive)
                throw new InvalidOperationException("Shell provider unavailable.");
            _session = session;
            _ = Task.Run(() => ReadEvents(session));
        }
        catch { session.Dispose(); throw; }
    }

    private void CleanupStaleSessions(string prefix)
    {
        foreach (var name in TraceEventSession.GetActiveSessionNames().Where(n => n.StartsWith(prefix, StringComparison.Ordinal)))
        {
            var parts = name[prefix.Length..].Split('-');
            if (parts.Length != 2 || !int.TryParse(parts[0], out var pid) || !Guid.TryParseExact(parts[1], "N", out _)) continue;
            try { using var process = Process.GetProcessById(pid); continue; }
            catch (ArgumentException) { /* 仅回收此操作者、确定已退出进程的 BPM 会话。 */ }
            catch { continue; }
            try { using var stale = new TraceEventSession(name, TraceEventSessionOptions.Attach); stale.Stop(); }
            catch (Exception ex) { LogFailure("清理旧会话", ex); }
        }
    }

    private void ReadEvents(TraceEventSession session)
    {
        try { session.Source.Process(); }
        catch (Exception ex) { LogFailure("监听", ex); }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_session, session))
                {
                    foreach (var id in _contexts.Keys.ToArray())
                    {
                        StopCore(id);
                        Manual(id, "登录辅助已中断；请重试辅助或手动登录一次对应 Windows 桌面");
                    }
                }
            }
        }
    }

    private void Observe(TraceEvent data)
    {
        // 不遍历、记录其他进程或事件的载荷。
        if (data.ProviderGuid != ShellProvider || (int)data.ID != 19501) return;
        var at = data.TimeStamp.ToUniversalTime();
        if (!BrowserAuthenticationPolicy.IsFresh(at, DateTime.UtcNow)) return;
        try
        {
            lock (_gate)
            {
                if (_disposed || _contexts.Count == 0) return;
                var context = FindTarget(data.ProcessID, at);
                if (context is null) return;
                if (!data.PayloadNames.Contains("Name", StringComparer.Ordinal))
                {
                    Manual(context.AccountId, "系统浏览器交接事件格式不兼容；请查看手动登录指引");
                    return;
                }
                if (data.PayloadByName("Name") is not string name)
                {
                    Manual(context.AccountId, "系统浏览器交接地址格式不兼容；请查看手动登录指引");
                    return;
                }
                if (BrowserAuthenticationPolicy.Extract(name) is not { } url)
                {
                    if (name.Contains("https://", StringComparison.OrdinalIgnoreCase))
                        Manual(context.AccountId, "系统浏览器交接地址格式不兼容；请查看手动登录指引");
                    return;
                }
                if (!BrowserAuthenticationPolicy.IsAllowed(url))
                {
                    // 不打开猜测或陌生入口；国服之外的认证流程保留系统原有行为。
                    if (Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
                        (uri.Host.Equals("account.battlenet.com.cn", StringComparison.OrdinalIgnoreCase) ||
                         uri.Host.Equals("account.battle.net", StringComparison.OrdinalIgnoreCase)))
                        Manual(context.AccountId, "此认证入口暂不支持自动辅助；请使用战网原生流程或查看手动登录指引");
                    return;
                }
                foreach (var key in context.Seen.Where(p => DateTime.UtcNow - p.Value > TimeSpan.FromSeconds(10)).Select(p => p.Key).ToArray())
                    context.Seen.Remove(key);
                if (context.Seen.Count >= 64)
                {
                    Manual(context.AccountId, "短时间收到过多登录请求，请稍后取消旧请求并重试");
                    return;
                }
                if (!context.Seen.TryAdd(BrowserAuthenticationPolicy.Fingerprint(url), DateTime.UtcNow)) return;
                var request = context.Requests.Begin(context.Cancellation.Token);
                Publish(context.AccountId, LoginAssistState.OpeningBrowser, "正在向该 Windows 用户的浏览器交接认证…");
                _ = Task.Run(() => RelayAsync(context, request, url, at));
            }
        }
        catch (Exception ex) { LogFailure("解析", ex); }
    }

    private async Task RelayAsync(Context context, BrowserLoginRequest request, string url, DateTime at)
    {
        try
        {
            await context.Serial.WaitAsync(request.Token).ConfigureAwait(false);
            try
            {
                if (!BrowserAuthenticationPolicy.IsFresh(at, DateTime.UtcNow))
                {
                    Update(context, request, LoginAssistState.ManualLoginRequired, "认证请求已过期，请取消战网旧请求后重新登录");
                    return;
                }
                var credential = _credentials.Get(context.CredentialKey);
                if (!credential.IsSuccess || !CredentialMatches(context, credential.Value.UserName))
                {
                    Update(context, request, LoginAssistState.ManualLoginRequired, "保存的凭据与 Windows 用户不匹配，请重新编辑配置或查看手动登录指引");
                    return;
                }
                var opened = await BrowserLoginPipe.OpenAsync(context.HelperPath, context.Sid, context.User, context.Domain,
                    credential.Value.Password, url, at, _launcher, request.Token).ConfigureAwait(false);
                Update(context, request, opened ? LoginAssistState.BrowserOpened : LoginAssistState.ManualLoginRequired,
                    opened ? "已交给浏览器，请完成认证；若未显示网页，请查看手动登录指引"
                           : "浏览器未能接收认证请求，请重试辅助或手动登录一次对应 Windows 桌面");
            }
            finally { context.Serial.Release(); }
        }
        catch (OperationCanceledException)
        {
            if (!request.Token.IsCancellationRequested)
                Update(context, request, LoginAssistState.ManualLoginRequired, "浏览器交接超时，请取消旧请求后重试；仍失败请查看手动登录指引");
        }
        catch (Exception ex)
        {
            LogFailure("转交", ex);
            Update(context, request, LoginAssistState.ManualLoginRequired, "浏览器交接失败，请重试辅助或手动登录一次对应 Windows 桌面");
        }
    }

    private Context? FindTarget(int pid, DateTime eventUtc)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            if (process.SessionId != _desktopSession || process.ProcessName != "Battle.net") return null;
            var handle = OpenProcess(0x1000, false, pid);
            if (handle == IntPtr.Zero) return null;
            IntPtr token = IntPtr.Zero;
            try
            {
                // 同一个句柄检查创建时间、路径和属主，拒绝退出后 PID 被重用的进程。
                if (!GetProcessTimes(handle, out var created, out _, out _, out _) ||
                    DateTime.FromFileTimeUtc(created) > eventUtc || !OpenProcessToken(handle, 8, out token)) return null;
                using var owner = new WindowsIdentity(token);
                if (owner.User is null) return null;
                var path = new StringBuilder(32768);
                var size = path.Capacity;
                if (!QueryFullProcessImageName(handle, 0, path, ref size)) return null;
                var image = path.ToString();
                if (!Path.GetFileName(image).Equals("Battle.net.exe", StringComparison.OrdinalIgnoreCase)) return null;
                return _contexts.Values.FirstOrDefault(c => c.Sid.Equals(owner.User) && eventUtc >= c.StartedUtc &&
                    DateTime.UtcNow < c.ExpiresUtc && image.StartsWith(c.Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
            }
            finally { if (token != IntPtr.Zero) NativeMethods.CloseHandle(token); NativeMethods.CloseHandle(handle); }
        }
        catch { return null; }
    }

    private static SecurityIdentifier ResolveSid(string domain, string user) =>
        (SecurityIdentifier)new NTAccount(domain == "." ? Environment.MachineName : domain, user).Translate(typeof(SecurityIdentifier));

    private static bool CredentialMatches(Context context, string credentialUser)
    {
        var (domain, user) = AccountValidator.SplitWindowsUser(credentialUser);
        // 现有凭据库按短用户名保存；域来自所绑定配置，不把域用户误当成本机用户。
        if (!credentialUser.Contains('\\')) domain = context.Domain;
        return context.Sid.Equals(ResolveSid(domain, user));
    }

    private void Expire()
    {
        lock (_gate)
        {
            if (_disposed) return;
            try { _session?.Flush(); }
            catch (Exception ex) { LogFailure("刷新", ex); }
            foreach (var context in _contexts.Values.Where(c => c.ExpiresUtc <= DateTime.UtcNow).ToArray())
            {
                StopCore(context.AccountId);
                Publish(context.AccountId, LoginAssistState.Expired, "本轮登录辅助已结束；如仍需网页登录，请重试辅助或查看手动登录指引");
            }
        }
    }

    private void StopCore(Guid accountId)
    {
        if (_contexts.Remove(accountId, out var context))
        {
            context.Cancellation.Cancel();
            context.Requests.Dispose();
            context.Lease.Dispose();
        }
        if (_contexts.Count == 0 && _session is not null)
        {
            var session = _session;
            _session = null;
            try { session.Source.StopProcessing(); }
            catch (Exception ex) { LogFailure("停止读取", ex); }
            finally
            {
                try { session.Dispose(); }
                catch (Exception ex) { LogFailure("停止会话", ex); }
            }
        }
    }

    private LoginAssistStatus Manual(Guid id, string message) => Publish(id, LoginAssistState.ManualLoginRequired, message);

    private LoginAssistStatus Publish(Guid id, LoginAssistState state, string message)
    {
        var status = new LoginAssistStatus(id, state, message);
        _statuses[id] = status;
        // 订阅者应投递到 UI 线程，不得在回调中阻塞或反向等待后台任务。
        foreach (Action<LoginAssistStatus> subscriber in StatusChanged?.GetInvocationList() ?? [])
        {
            try { subscriber(status); }
            catch (Exception ex) { LogFailure("更新界面", ex); }
        }
        return status;
    }

    private void Update(Context context, BrowserLoginRequest request, LoginAssistState state, string message)
    {
        lock (_gate)
        {
            if (!_disposed && _contexts.TryGetValue(context.AccountId, out var current) && ReferenceEquals(current, context) && context.Requests.IsCurrent(request))
                Publish(context.AccountId, state, message);
        }
    }

    private void LogFailure(string stage, Exception ex) =>
        _logger.LogWarning("登录辅助{Stage}失败，类型 {Type}，HRESULT {HResult}", stage, ex.GetType().Name, ex.HResult);

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _timer.Dispose();
            foreach (var id in _contexts.Keys.ToArray()) StopCore(id);
        }
    }

    private sealed class Context(Account account, string domain, string user, SecurityIdentifier sid, string root, EventWaitHandle lease)
    {
        public Guid AccountId { get; } = account.Id;
        public string CredentialKey { get; } = account.CredentialKey;
        public string HelperPath { get; set; } = string.Empty;
        public string WindowsUser { get; } = account.WindowsUser;
        public string BattleNetPath { get; } = account.BattleNetPath;
        public string Domain { get; } = domain;
        public string User { get; } = user;
        public SecurityIdentifier Sid { get; } = sid;
        public string Root { get; } = root.TrimEnd(Path.DirectorySeparatorChar);
        public EventWaitHandle Lease { get; } = lease;
        public DateTime StartedUtc { get; } = DateTime.UtcNow;
        public DateTime ExpiresUtc { get; set; } = DateTime.UtcNow + Lifetime;
        public Dictionary<string, DateTime> Seen { get; } = new(StringComparer.Ordinal);
        public CancellationTokenSource Cancellation { get; } = new();
        public BrowserLoginRequestSlot Requests { get; } = new();
        public SemaphoreSlim Serial { get; } = new(1, 1);
    }

    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetProcessTimes(IntPtr process, out long created, out long exited, out long kernel, out long user);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder path, ref int size);
}
