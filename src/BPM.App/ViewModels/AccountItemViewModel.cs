using BPM.Domain.Models;

namespace BPM.App.ViewModels;

/// <summary>账户列表行:领域模型 + 运行状态展示。</summary>
public class AccountItemViewModel : ObservableObject
{
    private string _status = "待启动";
    private string _loginStatus;
    private bool _needsAttention;
    private bool _isBusy;

    public AccountItemViewModel(Account account)
    {
        Account = account;
        _loginStatus = account.BrowserLoginAssistEnabled ? "启动时自动准备登录辅助" : "已关闭辅助，使用系统原生登录";
    }

    public Account Account { get; private set; }

    public void UpdateAccount(Account account)
    {
        if (account.Id != Id) throw new ArgumentException("Cannot change a row's account ID.", nameof(account));
        var userChanged = !string.Equals(WindowsUser, account.WindowsUser, StringComparison.OrdinalIgnoreCase);
        var assistChanged = Account.BrowserLoginAssistEnabled != account.BrowserLoginAssistEnabled;
        Account = account;
        OnPropertyChanged(nameof(Account));
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(WindowsUser));
        OnPropertyChanged(nameof(BattleNetPath));
        if (userChanged && !IsBusy) Status = "待启动";
        if (userChanged || assistChanged)
        {
            LoginStatus = account.BrowserLoginAssistEnabled ? "启动时自动准备登录辅助" : "已关闭辅助，使用系统原生登录";
            NeedsAttention = false;
        }
    }

    public Guid Id => Account.Id;
    public string DisplayName => Account.DisplayName;
    public string WindowsUser => Account.WindowsUser;
    public string BattleNetPath => Account.BattleNetPath;
    public string LoginStatus { get => _loginStatus; set => SetProperty(ref _loginStatus, value); }
    public bool NeedsAttention { get => _needsAttention; set => SetProperty(ref _needsAttention, value); }
    public bool IsBusy { get => _isBusy; set => SetProperty(ref _isBusy, value); }

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }
}
