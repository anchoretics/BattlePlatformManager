using System.Windows;

namespace BPM.App.ViewModels;

public interface IAccountDeletionConfirmation
{
    bool Confirm(string displayName);
}

internal sealed class AccountDeletionConfirmation : IAccountDeletionConfirmation
{
    public bool Confirm(string displayName) => MessageBox.Show(
        $"确定删除配置档“{displayName}”吗?\n(若无其他配置档共用,其保存的 Windows 凭据也会一并删除。)",
        "删除确认", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
}
