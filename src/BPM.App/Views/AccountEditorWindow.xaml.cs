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
        var initializeExistingUser = InitializeExistingUserCheckBox.IsChecked == true;
        var password = PasswordInput.Password;
        if ((createUser || initializeExistingUser) && string.IsNullOrEmpty(password))
        {
            MessageBox.Show(this, initializeExistingUser
                    ? "请填写已有系统用户原来的 Windows 密码。"
                    : "创建系统账号必须填写 Windows 密码。", "提示",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _isBusy = true;
        EditorForm.IsEnabled = false;
        Cursor = Cursors.Wait;
        SaveButton.Content = createUser ? "正在创建…" : initializeExistingUser ? "正在初始化…" : "正在保存…";
        var saved = false;
        try
        {
            var result = await Task.Run(() => createUser
                ? _provisioningService.CreateAndSave(_account, password, _setupProgress)
                : initializeExistingUser
                    ? _provisioningService.InitializeAndSave(_account, password)
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
            InitializeExistingUserCheckBox.IsEnabled = !_setupProgress.UserCreated;
            SaveButton.Content = _setupProgress.UserCreated ? "重试并保存"
                : createUser ? "创建并保存" : initializeExistingUser ? "初始化并保存" : "保存";
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
        if (SaveButton is null || CreateUserHint is null || InitializeExistingUserCheckBox is null)
        {
            return;
        }

        if (ReferenceEquals(sender, CreateUserCheckBox) && CreateUserCheckBox.IsChecked == true)
        {
            InitializeExistingUserCheckBox.IsChecked = false;
        }
        else if (ReferenceEquals(sender, InitializeExistingUserCheckBox) && InitializeExistingUserCheckBox.IsChecked == true)
        {
            CreateUserCheckBox.IsChecked = false;
        }

        var createUser = CreateUserCheckBox.IsChecked == true;
        var initializeUser = InitializeExistingUserCheckBox.IsChecked == true;
        CreateUserHint.Visibility = createUser || initializeUser ? Visibility.Visible : Visibility.Collapsed;
        CreateUserHint.Text = initializeUser
            ? "需要管理员权限。填写已有用户的原 Windows 密码,初始化用户配置并保存 BPM 配置;不会重新创建用户或重置 Windows 密码。"
            : "需要管理员权限。创建普通用户并初始化配置,密码保存到凭据管理器。系统用户创建后会保留,删除 BPM 配置不会删除系统用户。";
        SaveButton.Content = createUser ? "创建并保存" : initializeUser ? "初始化并保存" : "保存";
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
