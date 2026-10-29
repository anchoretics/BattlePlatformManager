namespace BPM.Application.Models;

public enum LoginAssistState
{
    Disabled,
    Ready,
    OpeningBrowser,
    BrowserOpened,
    AlreadyActive,
    ManualLoginRequired,
    Expired,
}

/// <summary>仅携带可显示的状态，不携带认证地址、参数或凭据。</summary>
public sealed record LoginAssistStatus(Guid AccountId, LoginAssistState State, string Message)
{
    public bool NeedsAttention => State is LoginAssistState.ManualLoginRequired or LoginAssistState.Expired;
}

public static class ManualLoginGuide
{
    public static string ForUser(string windowsUser)
    {
        var loginName = windowsUser.Contains('\\') || windowsUser.Contains('@') ? windowsUser : $@".\{windowsUser}";
        return
        $"如果战网停在“使用浏览器完成登录”，请先在 BPM 点击“重试登录辅助”，再取消战网旧请求并重新登录。\n\n" +
        $"仍无法打开时，请手动登录一次对应的 Windows 账号：\n" +
        "1. 先完全退出此配置的战网和浏览器，保存当前工作，按 Win+L 选择“其他用户”或“切换用户”。\n" +
        $"2. 使用 {loginName} 及其 Windows 密码登录桌面，等待首次配置完成。\n" +
        "3. 在该用户桌面打开 Edge 或 Chrome，完成首次使用设置，并在 Windows 设置中设为默认浏览器；打开战网完成一次认证。\n" +
        "4. 完全退出战网和浏览器，注销这个 Windows 用户，再回到原来的用户通过 BPM 启动，避免残留窗口留在另一桌面。\n\n" +
        "请在配置绑定的 Windows 桌面操作。在当前桌面以其他用户打开浏览器设置，可能改到当前用户的默认浏览器。\n" +
        "若公司策略不允许切换或登录该账号，请联系系统管理员。不要删除账号或重新创建配置来重试。";
    }
}
