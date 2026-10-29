using System.Collections.ObjectModel;
using System.Windows;
using BPM.App.Views;
using BPM.Application.Abstractions;
using BPM.Application.Services;
using BPM.Application.Models;
using BPM.Domain.Models;
using Microsoft.Extensions.Logging;

namespace BPM.App.ViewModels;

/// <summary>主界面视图模型:账户列表、启动、快捷方式、导入导出。</summary>
public class MainViewModel : ObservableObject
{
    private readonly AccountService _accountService;
    private readonly LaunchService _launchService;
    private readonly LocalAccountProvisioningService _provisioningService;
    private readonly IShortcutService _shortcutService;
    private readonly ILogger<MainViewModel> _logger;
    private readonly IBrowserLoginAssist _loginAssist;
    private readonly IAccountDeletionConfirmation _deletionConfirmation;
    private AccountItemViewModel? _selectedAccount;

    public MainViewModel(
        AccountService accountService,
        LaunchService launchService,
        LocalAccountProvisioningService provisioningService,
        IShortcutService shortcutService,
        ILogger<MainViewModel> logger,
        IBrowserLoginAssist loginAssist,
        IAccountDeletionConfirmation? deletionConfirmation = null)
    {
        _accountService = accountService;
        _launchService = launchService;
        _provisioningService = provisioningService;
        _shortcutService = shortcutService;
        _logger = logger;
        _loginAssist = loginAssist;
        _deletionConfirmation = deletionConfirmation ?? new AccountDeletionConfirmation();
        _loginAssist.StatusChanged += status => System.Windows.Application.Current?.Dispatcher.BeginInvoke(
            new Action(() => ApplyLoginStatus(status)));

        AddCommand = new RelayCommand(_ => AddAccount());
        EditCommand = new RelayCommand(_ => EditAccount(), _ => SelectedAccount is { IsBusy: false });
        DeleteCommand = new RelayCommand(_ => DeleteAccount(), _ => SelectedAccount is { IsBusy: false });
        LaunchCommand = new RelayCommand(p => LaunchAccount(p as AccountItemViewModel ?? SelectedAccount),
            p => (p as AccountItemViewModel ?? SelectedAccount) is { IsBusy: false });
        RetryLoginCommand = new RelayCommand(_ => RetryLogin(), _ => SelectedAccount is { IsBusy: false });
        LoginHelpCommand = new RelayCommand(_ => ShowLoginHelp(), _ => SelectedAccount is not null);
        ShortcutCommand = new RelayCommand(_ => CreateShortcut(), _ => SelectedAccount is not null);
        ImportCommand = new RelayCommand(_ => ImportConfig(), _ => Accounts.All(a => !a.IsBusy));
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
    public RelayCommand RetryLoginCommand { get; }
    public RelayCommand LoginHelpCommand { get; }

    private void Reload()
    {
        var oldItems = Accounts.DistinctBy(a => a.Id).ToDictionary(a => a.Id);
        var selectedId = SelectedAccount?.Id;
        var accounts = _accountService.GetAccounts();
        if (!accounts.IsSuccess)
        {
            ShowError(accounts.Message);
            return;
        }

        Accounts.Clear();
        foreach (var account in accounts.Value)
        {
            // 保留行对象，异步启动的完成回调仍能更新当前列表中的忙碌和结果状态。
            var item = oldItems.TryGetValue(account.Id, out var old) ? old : new AccountItemViewModel(account);
            item.UpdateAccount(account);
            Accounts.Add(item);
            if (_loginAssist.GetStatus(account.Id) is { } status) ApplyLoginStatus(status);
        }
        SelectedAccount = Accounts.FirstOrDefault(a => a.Id == selectedId) ?? Accounts.FirstOrDefault();
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
            BrowserLoginAssistEnabled = source.BrowserLoginAssistEnabled,
        };
        OpenEditor(clone, isNew: false);
    }

    private void OpenEditor(Account account, bool isNew)
    {
        var editor = new AccountEditorWindow(account, isNew, _accountService, _provisioningService)
        {
            Owner = System.Windows.Application.Current.MainWindow,
        };

        if (editor.ShowDialog() != true)
        {
            return;
        }

        _loginAssist.Stop(account.Id);
        Reload();
    }

    private void DeleteAccount()
    {
        var item = SelectedAccount;
        if (item is null || item.IsBusy)
        {
            return;
        }

        // 模态窗口会继续处理单实例转交等消息；选中项可能在确认期间改变。
        var accountId = item.Id;
        if (!_deletionConfirmation.Confirm(item.DisplayName) || item.IsBusy)
        {
            return;
        }

        var result = _accountService.DeleteAccount(accountId);
        if (!result.IsSuccess)
        {
            ShowError(result.Message);
            return;
        }

        _loginAssist.Stop(accountId);
        Reload();
    }

    private async void LaunchAccount(AccountItemViewModel? item)
    {
        if (item is null || item.IsBusy)
        {
            return;
        }

        SelectedAccount = item;
        item.IsBusy = true;
        System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        try
        {
            item.Status = "启动中…";
            var result = await Task.Run(() => _launchService.Launch(item.Id));
            if (!result.IsSuccess)
            {
                item.Status = "启动失败";
                ShowError(result.Message);
                return;
            }
            item.Status = "已启动";
        }
        catch (Exception ex)
        {
            _logger.LogWarning("启动异常，类型 {Type}，HRESULT {HResult}", ex.GetType().Name, ex.HResult);
            item.Status = "启动失败";
            ShowError("启动未完成，请检查配置和 BPM 日志后重试。");
        }
        finally
        {
            item.IsBusy = false;
            System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }
    }

    private void ApplyLoginStatus(LoginAssistStatus status)
    {
        var item = Accounts.FirstOrDefault(a => a.Id == status.AccountId);
        if (item is null) return;
        item.LoginStatus = status.Message;
        item.NeedsAttention = status.NeedsAttention;
    }

    private async void RetryLogin()
    {
        var item = SelectedAccount;
        if (item is null || item.IsBusy) return;
        if (!item.Account.BrowserLoginAssistEnabled)
        {
            MessageBox.Show("请先在“编辑配置”中启用“浏览器登录兼容辅助”，保存后重试。", "登录辅助");
            return;
        }
        item.IsBusy = true;
        try
        {
            var status = await Task.Run(() => _loginAssist.Start(item.Account));
            ApplyLoginStatus(status);
            if (status.NeedsAttention) ShowLoginHelp(item);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("重试辅助失败，类型 {Type}，HRESULT {HResult}", ex.GetType().Name, ex.HResult);
            item.LoginStatus = "登录辅助不可用，请查看手动登录指引";
            item.NeedsAttention = true;
            ShowLoginHelp(item);
        }
        finally { item.IsBusy = false; System.Windows.Input.CommandManager.InvalidateRequerySuggested(); }
    }

    private void ShowLoginHelp() { if (SelectedAccount is { } item) ShowLoginHelp(item); }

    private static void ShowLoginHelp(AccountItemViewModel item) => MessageBox.Show(
        $"配置：{item.DisplayName}  ·  Windows 用户：{item.WindowsUser}\n当前状态：{item.LoginStatus}\n\n{ManualLoginGuide.ForUser(item.WindowsUser)}",
        "浏览器未打开？手动登录指引", MessageBoxButton.OK, MessageBoxImage.Information);

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

        var elevationHint = "\n\nBPM 尚未运行时会自动请求管理员权限；已运行时会把配置启动请求交给现有实例。";
        MessageBox.Show($"已创建桌面快捷方式:\n{result.Value}{elevationHint}", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
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

        foreach (var item in Accounts) _loginAssist.Stop(item.Id);
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
