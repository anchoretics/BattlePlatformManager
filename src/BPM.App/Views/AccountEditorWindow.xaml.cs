using System.Windows;
using BPM.Domain.Models;

namespace BPM.App.Views;

/// <summary>账户编辑对话框。密码只经由 PasswordBox 短暂持有,随保存写入凭据管理器。</summary>
public partial class AccountEditorWindow : Window
{
    private readonly bool _isNew;

    public AccountEditorWindow(Account account, bool isNew)
    {
        InitializeComponent();
        _isNew = isNew;
        DataContext = account;
    }

    /// <summary>用户输入的密码;null 表示未输入(编辑时保留原凭据)。</summary>
    public string? EnteredPassword { get; private set; }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var password = PasswordInput.Password;
        if (_isNew && string.IsNullOrEmpty(password))
        {
            MessageBox.Show("新建配置档必须输入该 Windows 用户的密码。", "提示",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        EnteredPassword = string.IsNullOrEmpty(password) ? null : password;
        DialogResult = true;
    }

    private void BrowseBattleNet_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Battle.net 启动器 (*.exe)|*.exe",
            Title = "选择 Battle.net Launcher.exe",
        };
        if (dialog.ShowDialog(this) == true)
        {
            BattleNetPathInput.Text = dialog.FileName;
        }
    }

    private void BrowseWow_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "选择《魔兽世界》安装目录",
        };
        if (dialog.ShowDialog(this) == true)
        {
            WowPathInput.Text = dialog.FolderName;
        }
    }
}
