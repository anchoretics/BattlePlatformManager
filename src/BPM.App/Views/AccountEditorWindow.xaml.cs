using System.Windows;
using System.Windows.Input;
using BPM.Application.Models;
using BPM.Application.Services;
using BPM.Domain.Models;

namespace BPM.App.Views;

/// <summary>账户编辑对话框。密码只经由 PasswordBox 短暂持有,随保存写入凭据管理器。</summary>
public partial class AccountEditorWindow : Window
{
    private readonly Account _account;
    private readonly AccountService _accountService;
    private readonly LocalAccountProvisioningService _provisioningService;
    private readonly LocalUserSetupProgress _setupProgress = new();
    private bool _isBusy;

    public AccountEditorWindow(Account account, bool isNew,
        AccountService accountService, LocalAccountProvisioningService provisioningService)
    {
        InitializeComponent();
        _account = account;
        _accountService = accountService;
        _provisioningService = provisioningService;
        DataContext = account;
        CreateUserOptions.Visibility = isNew ? Visibility.Visible : Visibility.Collapsed;
        Closing += (_, args) => args.Cancel = _isBusy;
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy)
        {
            return;
        }

        var createUser = CreateUserCheckBox.IsChecked == true;
        var password = PasswordInput.Password;
        if (createUser && string.IsNullOrEmpty(password))
        {
            MessageBox.Show(this, "创建系统账号必须填写 Windows 密码。", "提示",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _isBusy = true;
        EditorForm.IsEnabled = false;
        Cursor = Cursors.Wait;
        SaveButton.Content = createUser ? "正在创建…" : "正在保存…";
        var saved = false;
        try
        {
            var result = await Task.Run(() => createUser
                ? _provisioningService.CreateAndSave(_account, password, _setupProgress)
                : _accountService.SaveAccount(_account, string.IsNullOrEmpty(password) ? null : password));
            if (!result.IsSuccess)
            {
                MessageBox.Show(this, result.Message, "保存失败", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            else
            {
                saved = true;
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "保存账户配置时发生异常");
            var message = _setupProgress.UserCreated
                ? "系统账号已创建,但设置未完成。请保留此窗口重试,详细信息见 BPM 日志。"
                : "保存未完成,请检查 BPM 日志后重试。";
            MessageBox.Show(this, message, "保存失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _isBusy = false;
            EditorForm.IsEnabled = true;
            Cursor = null;
            // 创建成功后用户名和密码与系统账户绑定;失败重试不能改成另一个身份。
            WindowsUserInput.IsReadOnly = _setupProgress.UserCreated;
            PasswordInput.IsEnabled = !_setupProgress.UserCreated;
            CreateUserCheckBox.IsEnabled = !_setupProgress.UserCreated;
            SaveButton.Content = _setupProgress.UserCreated ? "重试并保存" : createUser ? "创建并保存" : "保存";
        }

        if (saved)
        {
            PasswordInput.Clear();
            DialogResult = true;
        }
    }

    private void CreateUserMode_Changed(object sender, RoutedEventArgs e)
    {
        // XAML 初始化期间控件可能尚未全部创建。
        if (SaveButton is null || CreateUserHint is null)
        {
            return;
        }

        var createUser = CreateUserCheckBox.IsChecked == true;
        CreateUserHint.Visibility = createUser ? Visibility.Visible : Visibility.Collapsed;
        SaveButton.Content = createUser ? "创建并保存" : "保存";
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
