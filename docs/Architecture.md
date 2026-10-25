# 架构设计

Clean Architecture + MVVM,依赖方向自上而下单向:

```
BPM.App(WPF / MVVM / DI 装配)
    ↓
BPM.Application(用例:AccountService、LaunchService;全部接口抽象)
    ↓
BPM.Domain(Account、Config、AccountValidator —— 零外部依赖)
    ↑ 实现接口
BPM.Infrastructure(JsonConfigService:配置持久化)
BPM.Infrastructure.Win32(NativeMethods、CredentialManagerService、ProcessLauncherService、ShortcutService)
BPM.Platform.BattleNet(BattleNetAdapter:平台细节)
BPM.Shared(Result / ErrorCode)
```

## 各层职责

| 项目 | 职责 |
| --- | --- |
| BPM.App | WPF 界面、ViewModel、Command、DI 容器与 Serilog 装配、`--launch` 无界面模式 |
| BPM.Application | 业务用例与全部服务接口(`IConfigService`、`ICredentialService`、`IProcessLauncher`、`IGamePlatformAdapter`、`IShortcutService`、`IWindowsUserService`);`LocalAccountProvisioningService` 编排新系统用户创建 |
| BPM.Domain | 领域模型与校验规则,零 IO |
| BPM.Infrastructure | System.Text.Json 配置读写(原子写入) |
| BPM.Infrastructure.Win32 | Win32 P/Invoke 封装与实现,全部标注 `[SupportedOSPlatform("windows")]` |
| BPM.Platform.BattleNet | Battle.net 启动参数、安装校验、默认路径检测 |
| BPM.Shared | `Result`/`Result<T>`/`ErrorCode` 统一错误模型 |

## 启动流程

```
用户点击“启动”(或快捷方式 BPM.exe --launch <id>)
  → LaunchService.Launch(accountId)
    → IConfigService.Load()                读取配置
    → IGamePlatformAdapter.ValidateInstallation()  校验路径
    → ICredentialService.Get()             从 Credential Manager 取凭据
    → IGamePlatformAdapter.BuildLaunchRequest()    构造启动请求
    → IProcessLauncher.Launch()
        → CreateProcessWithLogonW(LOGON_WITH_PROFILE, lpEnvironment = NULL)
            校验凭据、加载目标用户配置文件、由系统生成目标用户环境
  → Battle.net 在目标用户上下文启动,读写该用户自己的 AppData
```

## 创建系统账号流程

新增配置勾选自动创建后,`LocalAccountProvisioningService` 先校验配置和本地用户名,再通过 `IWindowsUserService` 创建普通本地用户,将密码写入凭据管理器,初始化用户组及配置文件,最后保存 BPM 配置。

`WindowsUserService` 使用 `NetUserAdd`、`NetUserGetInfo`、`NetLocalGroupAddMembers`、`CreateProfile`。内置 Users 组通过 SID 解析本地化名称,不授予管理员权限。`CreateProfile` 无需登录用户桌面;运行时仍由 `CreateProcessWithLogonW` 加载配置及环境。

单个编辑窗口持有 `LocalUserSetupProgress`,仅保存创建阶段及用户标识,不含密码。失败后在原窗口重试已完成的阶段不会重复执行。系统用户独立保留,不会随取消编辑或删除 BPM 配置自动删除。

## 扩展新平台

实现 `IGamePlatformAdapter` 并在 DI 中替换/并列注册即可;启动管线(凭据、进程、快捷方式)完全复用。
