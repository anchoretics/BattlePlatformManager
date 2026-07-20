using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using BPM.App.Views;
using BPM.Application.Abstractions;
using BPM.Application.Services;
using BPM.Domain.Models;
using Microsoft.Extensions.Logging;

namespace BPM.App.ViewModels;

/// <summary>主界面视图模型:账户列表、启动、快捷方式、导入导出。</summary>
public class MainViewModel : ObservableObject
{
    private readonly AccountService _accountService;
    private readonly LaunchService _launchService;
    private readonly IShortcutService _shortcutService;
    private readonly ILogger<MainViewModel> _logger;
    private AccountItemViewModel? _selectedAccount;

    public MainViewModel(
        AccountService accountService,
        LaunchService launchService,
        IShortcutService shortcutService,
        ILogger<MainViewModel> logger)
    {
        _accountService = accountService;
        _launchService = launchService;
        _shortcutService = shortcutService;
        _logger = logger;

        AddCommand = new RelayCommand(_ => AddAccount());
        EditCommand = new RelayCommand(_ => EditAccount(), _ => SelectedAccount is not null);
        DeleteCommand = new RelayCommand(_ => DeleteAccount(), _ => SelectedAccount is not null);
        LaunchCommand = new RelayCommand(p => LaunchAccount(p as AccountItemViewModel ?? SelectedAccount),
            p => (p as AccountItemViewModel ?? SelectedAccount) is not null);
        ShortcutCommand = new RelayCommand(_ => CreateShortcut(), _ => SelectedAccount is not null);
        ImportCommand = new RelayCommand(_ => ImportConfig());
        ExportCommand = new RelayCommand(_ => ExportConfig());

        Reload();
    }

    public ObservableCollection<AccountItemViewModel> Accounts { get; } = new();

    public AccountItemViewModel? SelectedAccount
    {
        get => _selectedAccount;
        set => SetProperty(ref _selectedAccount, value);
    }

    public RelayCommand AddCommand { get; }
    public RelayCommand EditCommand { get; }
    public RelayCommand DeleteCommand { get; }
    public RelayCommand LaunchCommand { get; }
    public RelayCommand ShortcutCommand { get; }
    public RelayCommand ImportCommand { get; }
    public RelayCommand ExportCommand { get; }

    private void Reload()
    {
        Accounts.Clear();
        var accounts = _accountService.GetAccounts();
        if (!accounts.IsSuccess)
        {
            ShowError(accounts.Message);
            return;
        }

        foreach (var account in accounts.Value)
        {
            Accounts.Add(new AccountItemViewModel(account));
        }
    }

    private void AddAccount()
    {
        var config = _accountService.GetConfig();
        var account = new Account
        {
            BattleNetPath = config.IsSuccess ? config.Value.BattleNetPath : string.Empty,
            WowPath = config.IsSuccess ? config.Value.WowPath : string.Empty,
        };
        OpenEditor(account, isNew: true);
    }

    private void EditAccount()
    {
        if (SelectedAccount is null)
        {
            return;
        }

        // 编辑副本,保存成功后再刷新列表,避免取消时污染原对象。
        var source = SelectedAccount.Account;
        var clone = new Account
        {
            Id = source.Id,
            DisplayName = source.DisplayName,
            WindowsUser = source.WindowsUser,
            CredentialKey = source.CredentialKey,
            BattleNetPath = source.BattleNetPath,
            WowPath = source.WowPath,
            AutoLaunchWow = source.AutoLaunchWow,
        };
        OpenEditor(clone, isNew: false);
    }

    private void OpenEditor(Account account, bool isNew)
    {
        var editor = new AccountEditorWindow(account, isNew)
        {
            Owner = System.Windows.Application.Current.MainWindow,
        };

        if (editor.ShowDialog() != true)
        {
            return;
        }

        var result = _accountService.SaveAccount(account, editor.EnteredPassword);
        if (!result.IsSuccess)
        {
            ShowError(result.Message);
            return;
        }

        Reload();
    }

    private void DeleteAccount()
    {
        if (SelectedAccount is null)
        {
            return;
        }

        var confirm = MessageBox.Show(
            $"确定删除配置档“{SelectedAccount.DisplayName}”吗?\n(若无其他配置档共用,其保存的 Windows 凭据也会一并删除。)",
            "删除确认",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        var result = _accountService.DeleteAccount(SelectedAccount.Id);
        if (!result.IsSuccess)
        {
            ShowError(result.Message);
            return;
        }

        Reload();
    }

    private async void LaunchAccount(AccountItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        item.Status = "启动中…";
        var result = await Task.Run(() => _launchService.Launch(item.Id));
        if (!result.IsSuccess)
        {
            item.Status = "启动失败";
            ShowError(result.Message);
            return;
        }

        item.Status = $"运行中(PID {result.Value.ProcessId})";
        WatchProcessExit(item, result.Value.ProcessId);
    }

    private void WatchProcessExit(AccountItemViewModel item, int processId)
    {
        try
        {
            var process = Process.GetProcessById(processId);
            process.EnableRaisingEvents = true;
            process.Exited += (_, _) =>
            {
                System.Windows.Application.Current?.Dispatcher.Invoke(() => item.Status = "未运行");
                process.Dispose();
            };
        }
        catch (ArgumentException)
        {
            // Battle.net 启动器常在交接给常驻进程后退出,这里不视为错误。
            item.Status = "已启动";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "监视进程 {ProcessId} 退出状态失败", processId);
        }
    }

    private void CreateShortcut()
    {
        if (SelectedAccount is null)
        {
            return;
        }

        var result = _shortcutService.CreateLaunchShortcut(SelectedAccount.Account);
        if (!result.IsSuccess)
        {
            ShowError(result.Message);
            return;
        }

        MessageBox.Show($"已创建桌面快捷方式:\n{result.Value}", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ImportConfig()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "配置文件 (*.json)|*.json",
            Title = "导入配置",
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var result = _accountService.Import(dialog.FileName);
        if (!result.IsSuccess)
        {
            ShowError(result.Message);
            return;
        }

        Reload();
        MessageBox.Show("导入完成。注意:密码不随配置导出/导入,请为每个账户重新输入密码。",
            "导入配置", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ExportConfig()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "配置文件 (*.json)|*.json",
            FileName = "bpm-config.json",
            Title = "导出配置",
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var result = _accountService.Export(dialog.FileName);
        if (!result.IsSuccess)
        {
            ShowError(result.Message);
            return;
        }

        MessageBox.Show("导出完成(不包含任何密码)。", "导出配置", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private static void ShowError(string message) =>
        MessageBox.Show(message, "出错了", MessageBoxButton.OK, MessageBoxImage.Error);
}
