using System.IO;
using System.Windows;
using BPM.App.ViewModels;
using BPM.Application.Abstractions;
using BPM.Application.Services;
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

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BPM", "logs");
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                Path.Combine(logDirectory, "bpm-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14)
            .CreateLogger();

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddSerilog(dispose: true));
        services.AddSingleton<IConfigService, JsonConfigService>(sp =>
            new JsonConfigService(sp.GetRequiredService<ILogger<JsonConfigService>>()));
        services.AddSingleton<ICredentialService, CredentialManagerService>();
        services.AddSingleton<IProcessLauncher, ProcessLauncherService>();
        services.AddSingleton<IShortcutService, ShortcutService>();
        services.AddSingleton<IGamePlatformAdapter, BattleNetAdapter>();
        services.AddSingleton<AccountService>();
        services.AddSingleton<LaunchService>();
        services.AddSingleton<MainViewModel>();
        _serviceProvider = services.BuildServiceProvider();

        // 快捷方式模式:BPM.exe --launch <accountId>,不显示主界面,启动后立即退出。
        var launchIndex = Array.IndexOf(e.Args, "--launch");
        if (launchIndex >= 0 && launchIndex + 1 < e.Args.Length && Guid.TryParse(e.Args[launchIndex + 1], out var accountId))
        {
            RunHeadlessLaunch(accountId);
            return;
        }

        ShutdownMode = ShutdownMode.OnMainWindowClose;
        var mainWindow = new MainWindow
        {
            DataContext = _serviceProvider.GetRequiredService<MainViewModel>(),
        };
        MainWindow = mainWindow;
        mainWindow.Show();
    }

    private void RunHeadlessLaunch(Guid accountId)
    {
        var launchService = _serviceProvider!.GetRequiredService<LaunchService>();
        var result = launchService.Launch(accountId);
        if (!result.IsSuccess)
        {
            MessageBox.Show(
                result.Message,
                "BPM 启动失败",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        Shutdown(0);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _serviceProvider?.Dispose();
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}
