# Battle Platform Manager (BPM)

Windows 平台的多用户配置文件启动管理器。首个适配器面向 **Battle.net**:让不同 Windows 用户拥有完全独立的 Battle.net 登录状态与用户数据,同时**共用同一份《魔兽世界》安装目录**——多个实例可同时运行,无需切换 Windows 登录账户。

> BPM is a Windows multi-user profile launcher manager. Each Battle.net instance runs under its own Windows user (via the documented `CreateProcessWithLogonW` API), giving fully isolated login state while sharing a single game installation.

[![Build](https://github.com/anchoretics/BattlePlatformManager/actions/workflows/build.yml/badge.svg)](https://github.com/anchoretics/BattlePlatformManager/actions/workflows/build.yml)

## 功能特性

- **一键启动**:桌面快捷方式或主界面按钮直接启动指定 Battle.net 实例。
- **管理器单实例**:同一 Windows 用户和登录会话只保留一个 BPM 主进程；再次打开恢复并激活原窗口，快捷方式启动请求转交给原进程，认证助手独立运行。
- **完全隔离**:每个配置档绑定独立 Windows 用户,Battle.net 登录状态、缓存、配置互不影响。
- **共享游戏目录**:所有实例共用同一份 Battle.net 安装与《魔兽世界》游戏文件,不浪费磁盘。
- **多开并行**:多个实例同时运行,无需注销 / 切换 Windows 账户。
- **凭据安全**:Windows 用户密码仅保存于系统凭据管理器(Credential Manager),配置文件零明文。
- **配置管理**:配置档的添加、编辑、删除、导入、导出。
- **一键创建系统账号**:添加配置时可创建普通 Windows 本地用户并初始化用户配置,无需先登录一次 Windows 桌面。
- **浏览器登录辅助**:捕获战网真实的国服认证交接地址，用配置绑定的 Windows 用户打开 Edge，失败时尝试机器级 Chrome；无需先设置默认浏览器。
- **明确的登录兜底**:主界面区分启动状态和浏览器交接状态，支持重试；无法自动处理时提供登录一次目标 Windows 桌面的指引。

## 工作原理

BPM 不修改任何游戏文件。它调用 Windows 官方 API `CreateProcessWithLogonW`(`LOGON_WITH_PROFILE`),在指定 Windows 用户上下文中启动 Battle.net——效果等同于 `runas`,但免去每次输入密码与命令行的麻烦。用户可以预先创建,也可以由 BPM 创建并初始化。Battle.net 随后自然地读写该用户自己的 `AppData`,隔离由 Windows 用户机制本身保证:

| 隔离的数据 | 路径 |
| --- | --- |
| Battle.net 用户配置 | `C:\Users\<User>\AppData\Roaming\Battle.net` |
| Battle.net 本地缓存 | `C:\Users\<User>\AppData\Local\Battle.net` |
| Blizzard 本地数据 | `C:\Users\<User>\AppData\Local\Blizzard Entertainment` |
| 部分游戏配置 | `C:\Users\<User>\Documents` |

## 安装与使用

1. 从 [Releases](../../releases) 下载最新的 `BPM-*-win-x64.zip`,解压到任意目录(单文件 EXE,无需安装)。
2. 双击 `BPM.exe`，启动时会自动请求 Windows 管理员授权，点击“是”即可；已有管理员权限时直接进入。取消授权会退出，不读取配置或启动战网。请使用平时运行 BPM 的同一个 Windows 账号提升权限。
3. 点击 **添加配置**:填写显示名、新的本地用户名(例如 `WoW2`)、Windows 密码和 Battle.net 启动器路径,勾选 **保存时创建 Windows 本地用户**,点击 **创建并保存**。BPM 会创建普通用户、初始化用户配置并保存密码。也可以不勾选,直接填写已有 Windows 用户及其密码。
4. 保留 **启用浏览器登录兼容辅助**，点击 **启动战网**；或先创建桌面快捷方式，以后双击启动。需要认证时，在打开的网页完成登录/短信验证，再启动游戏。
5. 首次在新用户下启动 Battle.net 时登录一次战网账号并勾选“保持登录”,之后即保持独立登录状态。

创建用户与浏览器交接均可在不登录目标 Windows 桌面的情况下进行。登录辅助每轮持续 15 分钟，期间支持取消/重试认证和重启战网；请保留 BPM 窗口。快捷方式启动时 BPM 在后台保持同样的辅助窗口，到期自动退出；辅助失败时会打开主界面显示指引。

辅助默认开启，旧配置缺少新字段时也可直接使用。原生登录正常或出现重复网页时，可在 **编辑配置** 中关闭辅助。辅助只承诺已交给浏览器，不能据此判断网页显示或战网认证成功；仍需用户在网页操作并确认战网进入主界面。

BPM 尚未运行时自动请求管理员权限；已有实例时直接转交激活或 `--launch` 请求，避免重复 UAC 和重复窗口。新建快捷方式不再强制“以管理员运行”；若旧快捷方式已带该标记，可重新创建，以免 Windows 在转交前仍要求授权。浏览器登录助手始终按配置用户的普通身份独立运行。窗口标题显示“管理员”表示已通过检查。

### 常见问题

- **提示“用户名或密码错误”**:请确认输入的是 *Windows 用户* 的密码(不是战网密码)。
- **提示 Secondary Logon 服务未运行**:在“服务”中将 `Secondary Logon (seclogon)` 设为自动并启动。
- **该 Windows 用户密码为空**:本地安全策略默认限制空密码账户,请为该用户设置密码。
- **自动创建提示用户已存在**:选择其他用户名,或取消自动创建后填写已有用户的密码;BPM 不会覆盖已有系统用户的密码。
- **自动创建中途失败**:保留编辑窗口,排除错误后点击“重试并保存”。创建进度会保留,不会重复创建同一用户。系统用户创建后独立保留,取消编辑或删除 BPM 配置不会删除该系统用户。
- **重启后继续未完成的创建**:点击“添加账户”,填写已创建的用户名和原 Windows 密码,选择 **仅初始化已有本地用户**,点击 **初始化并保存**。此入口不重新创建用户或重置系统密码,也无需登录 Windows 桌面。
- **提示使用浏览器完成登录，但没有浏览器窗口**:选中配置，点击 **重试登录辅助**，在战网取消旧请求再重新登录。检查 BPM 是否以管理员身份运行、机器上是否安装 Edge 或 Chrome。仍失败时点击 **手动登录一次 · 操作指引**：先完全退出此配置的战网及浏览器，按 Win+L 切换到配置绑定的 Windows 用户，完成首次桌面及浏览器设置、在该用户的战网完成一次认证，完全退出战网与浏览器并注销该用户，再回到原用户通过 BPM 启动。必须切换 Windows 桌面；仅以其他用户打开 Edge 设置，可能改到当前桌面用户的默认浏览器。
- **系统或认证入口不兼容**:自动辅助目前只接受已验证的国服 `account.battlenet.com.cn` 完整交接入口。国际服、未来的入口或 Windows Shell 事件格式变化会保留原生流程，并在可识别的不兼容、失败或本轮到期时提示重试/手动处理。辅助不写 UserChoice、不修改系统策略、组权限或驱动。一次手动桌面初始化也不能保证解决所有第三方客户端问题。
- **BPM 放在私人目录**:辅助程序会缓存到 `%ProgramData%\BattlePlatformManager.LoginHelpers`，按操作者、目标用户和版本隔离，只复制 EXE、DLL 及运行依赖清单。目标用户仅获读取/执行权限，缓存不含配置、密码或认证链接；不修改原程序目录的权限，保留最近两版缓存供正在运行的助手使用。
- **游戏目录共享**:所有配置档直接指向同一 Battle.net / WoW 安装目录即可;请确保目标用户对游戏目录有读写权限(必要时在目录属性 → 安全中为 Users 组授予修改权限)。
- **日志位置**:`%LOCALAPPDATA%\BPM\logs`,日志不含任何密码。

## 安全声明

- ✅ 使用 Windows 进程、凭据和实时 ETW API；浏览器辅助依赖的 Shell 事件布局有版本差异，失败时提供人工降级。
- ✅ 认证地址仅在内存和限定用户访问的短期命名管道中传递，不写入 BPM 配置、日志或临时文件；不修改默认浏览器。
- ✅ 密码只写入 Windows Credential Manager;配置文件、日志中绝无明文密码。
- ❌ 不修改 Battle.net、WoW.exe 或任何游戏文件。
- ❌ 无 DLL 注入、Hook、Patch、协议逆向,不绕过 Battle.net 登录、授权或安全机制。
- ❌ 不提供任何游戏自动化、脚本或外挂功能。

多账号使用请遵守暴雪娱乐的最终用户许可协议;本工具与暴雪娱乐无任何关联。

## 开发

```bash
dotnet build --configuration Release   # 要求 0 警告 0 错误
dotnet test  --configuration Release
```

技术栈:.NET 8 · WPF · Clean Architecture + MVVM · Serilog · xUnit + FluentAssertions。
架构与设计文档见 [docs/](docs/),贡献前请阅读 [PR 模板](.github/PULL_REQUEST_TEMPLATE.md) 中的检查清单。

## 许可证

[MIT](LICENSE)
