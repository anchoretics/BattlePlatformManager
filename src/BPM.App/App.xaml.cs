using System.IO;
using System.Windows;
using BPM.App.ViewModels;
using BPM.Application.Abstractions;
using BPM.Application.Services;
using BPM.Application.Models;
using BPM.Infrastructure.Configuration;
using BPM.Infrastructure.Win32;
using BPM.Platform.BattleNet;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

namespace BPM.App;

public partial class App : System.Windows.Application
{
    private ServiceProvider? _serviceProvider;
    private SingleInstanceService? _singleInstance;
    private readonly object _commandGate = new();
    private readonly Queue<SingleInstanceCommand> _pendingCommands = new();
    private readonly HashSet<Guid> _headlessLaunches = new();
    private readonly HashSet<Guid> _headlessAccounts = new();
    private bool _initialized;
    private bool _acceptingCommands = true;
    private bool _interactive;
    private bool _watchingIdle;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 必须先处理目标用户的短期助手：不打开主界面，也不初始化日志/凭据库。
        if (e.Args.Contains("--browser-login-pipe"))
        {
            var code = e.Args.Length == 4 && e.Args[0] == "--browser-login-pipe" && e.Args[2] == "--server-pid" &&
                       int.TryParse(e.Args[3], out var serverPid)
                ? await BrowserLoginHelper.RunAsync(e.Args[1], serverPid) : 1;
            Shutdown(code);
            return;
        }

        // 不使用全局 requireAdministrator 清单，否则同一 EXE 的普通用户浏览器助手也会被要求提升。
        var processPath = Environment.ProcessPath;
        var helperPath = !string.IsNullOrEmpty(processPath) && !Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
            ? processPath : Path.Combine(AppContext.BaseDirectory, "BPM.exe");
        var command = SingleInstanceCommand.FromArguments(e.Args);
        _singleInstance = new SingleInstanceService();
        // 已有管理员实例时，普通权限的重复启动直接转交，避免再次弹 UAC。
        // 提权子进程必须先通过原用户身份校验，不能转交给另一个 Windows 账号。
        if (!e.Args.Contains("--bpm-elevation-user") && await ForwardIfRunning(command)) return;
        var elevation = new StartupElevationService().EnsureAdministrator(helperPath, e.Args);
        if (elevation.State != StartupElevationState.Continue)
        {
            if (elevation.State == StartupElevationState.Failed)
                MessageBox.Show(elevation.Message, "BPM 管理员启动失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            Shutdown(elevation.State == StartupElevationState.Relaunched ? 0 : elevation.State == StartupElevationState.Cancelled ? 1223 : 1);
            return;
        }

        try
        {
            if (!_singleInstance.TryStartPrimary(EnqueueCommand))
            {
                if (await ForwardIfRunning(command)) return;
                if (!_singleInstance.TryStartPrimary(EnqueueCommand))
                {
                    MessageBox.Show("已有 BPM 正在启动或退出，请稍后再试。", "BPM 单实例");
                    Shutdown(1);
                    return;
                }
            }
            EnqueueCommand(command);
        }
        catch (Exception)
        {
            MessageBox.Show("无法建立 BPM 单实例通信，请关闭其他 BPM 窗口后重新打开。", "BPM 单实例", MessageBoxButton.OK, MessageBoxImage.Warning);
            Shutdown(1);
            return;
        }

        var logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BPM", "logs");
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                Path.Combine(logDirectory, "bpm-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14)
            .CreateLogger();
        Log.Information("BPM 已通过管理员启动检查，PID {ProcessId}", Environment.ProcessId);

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddSerilog(dispose: true));
        services.AddSingleton<IConfigService, JsonConfigService>(sp =>
            new JsonConfigService(sp.GetRequiredService<ILogger<JsonConfigService>>()));
        services.AddSingleton<ICredentialService, CredentialManagerService>();
        services.AddSingleton<IProcessLauncher, ProcessLauncherService>();
        services.AddSingleton<IShortcutService, ShortcutService>();
        services.AddSingleton<IWindowsUserService, WindowsUserService>();
        services.AddSingleton<IBrowserLoginAssist>(sp => new BrowserLoginAssistService(
            sp.GetRequiredService<ICredentialService>(), sp.GetRequiredService<IProcessLauncher>(),
            sp.GetRequiredService<ILogger<BrowserLoginAssistService>>(), helperPath));
        services.AddSingleton<IGamePlatformAdapter, BattleNetAdapter>();
        services.AddSingleton<AccountService>();
        services.AddSingleton<LocalAccountProvisioningService>();
        services.AddSingleton<LaunchService>();
        services.AddSingleton<MainViewModel>();
        _serviceProvider = services.BuildServiceProvider();
        _serviceProvider.GetRequiredService<IBrowserLoginAssist>().StatusChanged += status =>
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (status.State == LoginAssistState.ManualLoginRequired && _headlessAccounts.Contains(status.AccountId))
                    ShowLoginFallback(status.AccountId);
            }));
        _initialized = true;
        ProcessPendingCommands();
    }

    private async Task<bool> ForwardIfRunning(SingleInstanceCommand command)
    {
        var result = await _singleInstance!.TryForwardAsync(command);
        if (result == InstanceForwardResult.NotRunning) return false;
        if (result == InstanceForwardResult.Unavailable)
            MessageBox.Show("已有 BPM 正在启动、退出或未能响应。本次请求未确认，请检查已有窗口后重试。", "BPM 单实例", MessageBoxButton.OK, MessageBoxImage.Information);
        Shutdown(result == InstanceForwardResult.Forwarded ? 0 : 1);
        return true;
    }

    private bool EnqueueCommand(SingleInstanceCommand command)
    {
        lock (_commandGate)
        {
            if (!_acceptingCommands || Dispatcher.HasShutdownStarted) return false;
            _pendingCommands.Enqueue(command);
            Dispatcher.BeginInvoke(new Action(ProcessPendingCommands));
            return true;
        }
    }

    private void ProcessPendingCommands()
    {
        if (!_initialized) return;
        while (true)
        {
            SingleInstanceCommand command;
            lock (_commandGate)
            {
                if (_pendingCommands.Count == 0) return;
                command = _pendingCommands.Dequeue();
            }
            if (command.AccountId is not { } accountId) { ShowMainWindow(); continue; }
            if (MainWindow?.DataContext is MainViewModel viewModel && MainWindow.IsVisible &&
                viewModel.Accounts.FirstOrDefault(a => a.Id == accountId) is { } item)
            {
                if (viewModel.LaunchCommand.CanExecute(item)) viewModel.LaunchCommand.Execute(item);
            }
            else RunHeadlessLaunch(accountId);
        }
    }

    private void ShowMainWindow()
    {
        _interactive = true;
        if (MainWindow is { } existing)
        {
            if (!existing.IsVisible) existing.Show();
            if (existing.WindowState == WindowState.Minimized) existing.WindowState = WindowState.Normal;
            existing.Activate();
            return;
        }
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        var mainWindow = new MainWindow
        {
            Title = "BPM · 战网配置管理（管理员）",
            DataContext = _serviceProvider!.GetRequiredService<MainViewModel>(),
        };
        MainWindow = mainWindow;
        mainWindow.Show();
    }

    private async void RunHeadlessLaunch(Guid accountId)
    {
        if (!_headlessLaunches.Add(accountId)) return;
        _headlessAccounts.Remove(accountId);
        var assist = _serviceProvider!.GetRequiredService<IBrowserLoginAssist>();
        try
        {
            var launchService = _serviceProvider!.GetRequiredService<LaunchService>();
            var result = await Task.Run(() => launchService.Launch(accountId));
            if (!result.IsSuccess)
            {
                MessageBox.Show(result.Message, "BPM 启动失败", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            _headlessAccounts.Add(accountId);
            if (assist.GetStatus(accountId)?.State == LoginAssistState.ManualLoginRequired) ShowLoginFallback(accountId);
        }
        catch (Exception ex)
        {
            Log.Warning("单实例启动配置失败，类型 {Type}，HRESULT {HResult}", ex.GetType().Name, ex.HResult);
            MessageBox.Show("启动未完成，请检查配置和 BPM 日志后重试。", "BPM 启动失败");
        }
        finally { _headlessLaunches.Remove(accountId); WatchHeadlessIdle(); }
    }

    private void ShowLoginFallback(Guid accountId)
    {
        ShowMainWindow();
        var viewModel = _serviceProvider!.GetRequiredService<MainViewModel>();
        viewModel.SelectedAccount = viewModel.Accounts.FirstOrDefault(a => a.Id == accountId);
        if (viewModel.SelectedAccount is { } item) item.Status = "已启动";
    }

    private async void WatchHeadlessIdle()
    {
        if (_interactive || _watchingIdle) return;
        _watchingIdle = true;
        await _serviceProvider!.GetRequiredService<IBrowserLoginAssist>().WaitForIdleAsync();
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Background);
        _watchingIdle = false;
        lock (_commandGate)
        {
            if (_interactive || _headlessLaunches.Count != 0 || _pendingCommands.Count != 0) return;
            // 与后台接收请求互斥，关闭期间不确认尚未处理的新请求。
            _acceptingCommands = false;
        }
        Shutdown(0);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        lock (_commandGate) _acceptingCommands = false;
        _singleInstance?.Dispose();
        _serviceProvider?.Dispose();
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}
