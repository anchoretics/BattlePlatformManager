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

主程序启动先执行 `StartupElevationService`：普通权限自动请求 UAC，保留原参数重启；已提升时继续。取消授权退出，身份变化或重复提升失败明确报错。此检查发生在读取配置/凭据之前。浏览器管道助手入口先于此检查，以保持目标用户普通身份；全局 EXE 清单仍采用默认权限。

单实例入口先于提权尝试转交：以操作者 SID + Windows SessionId 命名租约和管道，普通重复启动只发送激活/配置 GUID 给现有管理员进程。首个管理员进程原子取得租约并建立管道，初始化期间的请求进入队列；提权子进程先校验原账号身份再参与竞争。浏览器助手在此流程之前分流，不占管理器租约。

管道 DACL 只允许该用户和 SYSTEM，双方核对进程 SID/SessionId，客户端额外核对服务端提升状态。固定 17 字节协议只包含操作类型和配置 GUID，不接收密码、任意命令行或程序路径。接收确认表示已进入队列；确认丢失不自动重复执行启动。主界面最小化时激活会恢复；首次快捷方式仍可后台运行，多个快捷方式共用同一辅助服务，全部辅助结束且无待处理请求时才退出。用户打开主界面后不再自动退出。

请求后的确认使用双向握手：服务端发送接收结果后，等待客户端确认已读取，再复用管道。`PipeStream.Flush` 不能保证对端已读取，直接 `Disconnect` 可能丢弃未读确认；慢速客户端回归检查覆盖该时序。

`LaunchService` 按配置 ID 合并尚未完成的启动请求，主界面和后台快捷方式共享一次启动及其结果；完成或失败后可重新启动，不阻塞其他配置。主界面刷新复用同 ID 的行对象，异步回调继续更新当前行的忙碌与结果状态。删除确认固定配置 ID，并在确认后复查该行是否正在启动。

`JsonConfigService` 在读取、保存及导入时校验账户列表、条目、必填字段及 ID 唯一性。无效结构返回 `ConfigCorrupted`；导入验证成功后才替换配置，避免将损坏内容写成下次启动使用的文件。合法旧配置仍保留新增辅助字段的默认值。

```
用户点击“启动”(或快捷方式 BPM.exe --launch <id>)
  → LaunchService.Launch(accountId)
    → IConfigService.Load()                读取配置
    → IGamePlatformAdapter.ValidateInstallation()  校验路径
    → ICredentialService.Get()             从 Credential Manager 取凭据
    → IGamePlatformAdapter.BuildLaunchRequest()    构造启动请求
    → IBrowserLoginAssist.Start()          可选浏览器辅助，失败不阻止客户端启动
    → IProcessLauncher.Launch()
        → CreateProcessWithLogonW(LOGON_WITH_PROFILE, lpEnvironment = NULL)
            校验凭据、加载目标用户配置文件、由系统生成目标用户环境
  → Battle.net 在目标用户上下文启动,读写该用户自己的 AppData
```

## 创建系统账号流程

新增配置勾选自动创建后,`LocalAccountProvisioningService` 先校验配置和本地用户名,再通过 `IWindowsUserService` 创建普通本地用户,将密码写入凭据管理器,初始化用户组及配置文件,最后保存 BPM 配置。

`WindowsUserService` 使用 `NetUserAdd`、`NetUserGetInfo`、`NetLocalGroupAddMembers`、`CreateProfile`。内置 Users 组通过 SID 解析本地化名称,不授予管理员权限。`CreateProfile` 无需登录用户桌面;运行时仍由 `CreateProcessWithLogonW` 加载配置及环境。

单个编辑窗口持有 `LocalUserSetupProgress`,仅保存创建阶段及用户标识,不含密码。失败后在原窗口重试已完成的阶段不会重复执行。系统用户独立保留,不会随取消编辑或删除 BPM 配置自动删除。

重启后可显式选择“仅初始化已有本地用户”,通过 `InitializeAndSave` 初始化既有用户的配置文件并保存 BPM 配置,不执行用户创建或 Windows 密码重置。

## 扩展新平台

### 浏览器登录交接

`IBrowserLoginAssist` 提供独立状态、重试及生命周期。Win32 实现只启用 Shell-Core 实时会话的 Battle.net 进程过滤，只处理事件 19501 的 Name；同时核对事件新鲜度、桌面会话、同一进程句柄的创建时间/路径/属主 SID，并限定到配置安装目录。只转交已验证的国服完整 HTTPS 入口，URL 不解码、不重建。

`BrowserLoginAssistService` 在启动客户端前注册目标用户，持续 15 分钟，可同时服务不同用户。进程交接和重启不结束辅助；同操作者/目标 SID 的命名事件租约避免多个 BPM 重复转交。只在内存保留短期请求指纹，后台串行交接，过期、停止或退出取消任务并释放会话；下次启用会清理此操作者已退出 BPM 进程的遗留会话。

认证地址经受限 ACL 命名管道交给 `BPM.exe --browser-login-pipe <随机名> --server-pid <pid>`。父子双方核对管道对端 PID，子进程在 DI、日志和界面初始化前执行，只接受受限长度/站点的地址，再用参数列表直接启动机器级 Edge/Chrome。CPWL 命令行仅含短管道参数；不受长认证链接的 1024 字符限制。收到助手应答只代表浏览器进程交接完成，不代表网页登录成功。

`BrowserLoginHelperCache` 处理操作者私人目录不能被目标用户读取的情况。按程序文件内容哈希在 ProgramData 专用缓存中存放 EXE、顶层 DLL 和依赖/运行时清单，不复制配置、日志或 PDB；按操作者 SID/目标 SID/版本分层，验证目录属主、拒绝重解析点，目标 SID 只有读取/执行权限，原目录 ACL 不变。助手以系统目录为工作目录。保留两版，旧版仍被占用时延后清理；清理只处理本目标的扁平哈希目录，不递归删除或跟随链接。

已有缓存文件通过同一个只读句柄检查链接数、ACL 和内容哈希，符合要求则直接复用。需要更新时，以 `CreateNew` 和受限 ACL 创建随机临时文件，验证内容并刷盘后替换目录项，避免写入或改权操作跟随旧硬链接。已有缓存目录仅校验 ACL，不重复写入可继承权限；目录权限异常时停止准备并交给手动登录兜底。

普通权限或不兼容环境以 `ManualLoginRequired` 降级，主界面展示目标账号的 Windows 桌面登录指引。快捷方式无主界面时等待辅助到期；若需人工处理，显示主界面并保留重试入口。退出 BPM 会停止本实例辅助。平台适配器扩展时应单独评估认证辅助，不能把国服入口白名单直接用于其他平台。

实现 `IGamePlatformAdapter` 并在 DI 中替换/并列注册即可;启动管线(凭据、进程、快捷方式)完全复用。
