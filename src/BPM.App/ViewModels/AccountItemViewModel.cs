using BPM.Domain.Models;

namespace BPM.App.ViewModels;

/// <summary>账户列表行:领域模型 + 运行状态展示。</summary>
public class AccountItemViewModel : ObservableObject
{
    private string _status = "未运行";

    public AccountItemViewModel(Account account)
    {
        Account = account;
    }

    public Account Account { get; }

    public Guid Id => Account.Id;
    public string DisplayName => Account.DisplayName;
    public string WindowsUser => Account.WindowsUser;
    public string BattleNetPath => Account.BattleNetPath;

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }
}
