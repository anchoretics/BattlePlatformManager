using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Runtime.Versioning;
using BPM.Application.Abstractions;
using BPM.Domain.Models;
using BPM.Shared;
using Microsoft.Extensions.Logging;

namespace BPM.Infrastructure.Win32;

/// <summary>通过 IShellLinkW COM 接口在桌面创建 "BPM --launch &lt;accountId&gt;" 快捷方式。</summary>
[SupportedOSPlatform("windows")]
public class ShortcutService : IShortcutService
{
    private readonly ILogger<ShortcutService> _logger;

    public ShortcutService(ILogger<ShortcutService> logger)
    {
        _logger = logger;
    }

    public Result<string> CreateLaunchShortcut(Account account)
    {
        try
        {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath))
            {
                return Result<string>.Fail(ErrorCode.ShortcutFailed, "无法确定 BPM 程序自身路径。");
            }

            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var safeName = string.Join("_", account.DisplayName.Split(Path.GetInvalidFileNameChars()));
            var shortcutPath = Path.Combine(desktop, $"Battle.net - {safeName}.lnk");

            var link = (IShellLinkW)new ShellLink();
            link.SetPath(exePath);
            link.SetArguments($"--launch {account.Id}");
            link.SetWorkingDirectory(Path.GetDirectoryName(exePath) ?? string.Empty);
            link.SetDescription($"以 Windows 用户 {account.WindowsUser} 启动 Battle.net({account.DisplayName})");
            if (File.Exists(account.BattleNetPath))
            {
                link.SetIconLocation(account.BattleNetPath, 0);
            }

            ((IPersistFile)link).Save(shortcutPath, fRemember: false);
            _logger.LogInformation("已创建快捷方式 {ShortcutPath}", shortcutPath);
            return Result<string>.Ok(shortcutPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "创建快捷方式失败,账户 {DisplayName}", account.DisplayName);
            return Result<string>.Fail(ErrorCode.ShortcutFailed, $"创建快捷方式失败:{ex.Message}");
        }
    }

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink
    {
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszFile, int cch, IntPtr pfd, int fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszName, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszDir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszArgs, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszIconPath, int cch, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, int dwReserved);
        void Resolve(IntPtr hwnd, int fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }
}
