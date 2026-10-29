# Windows API 设计

使用 Windows 文档化进程、凭据与 ETW API；P/Invoke 封装位于 `BPM.Infrastructure.Win32`。登录辅助观察的 Shell 事件布局存在版本差异，不作为稳定公开接口承诺。

## 进程启动

| API | 用途 |
| --- | --- |
| `CreateProcessWithLogonW` | 校验凭据、加载目标用户配置文件、由系统生成用户环境并启动进程 |
| `CloseHandle` | 释放进程 / 线程句柄 |

关键参数:

| 参数 | 值 | 说明 |
| --- | --- | --- |
| `dwLogonFlags` | `LOGON_WITH_PROFILE (0x1)` | 加载目标用户 Profile,Battle.net 才能读到自己的 AppData |
| `dwCreationFlags` | `CREATE_UNICODE_ENVIRONMENT (0x400)` | Unicode 环境标志 |
| `lpEnvironment` | `IntPtr.Zero` (`NULL`) | 由系统从目标用户配置文件生成环境,不提前生成或继承 BPM 的环境 |
| `lpApplicationName` | 启动器完整路径 | 显式指定可执行文件 |
| `lpCommandLine` | `StringBuilder` | API 要求可写缓冲区 |
| `lpCurrentDirectory` | Battle.net 安装目录 | |

注意事项:

- 依赖 **Secondary Logon(seclogon)** 服务;禁用时报 `1058/1062`,已映射为友好提示。
- 常见错误映射:`1326` 凭据错误、`1327` 账户受限(空密码)、`1385` 无交互式登录权限、`2/3` 路径无效。
- 进程 / 线程句柄在 `finally` 中释放;失败路径记录 `Win32Error`。
- 不再预先调用 `LogonUserW` / `CreateEnvironmentBlock`:目标用户在开机后尚未加载配置文件时,提前生成的环境可能缺少用户专属变量,影响战网读取登录状态和安装位置。
- `LOGON_WITH_PROFILE` 与 `lpEnvironment = NULL` 的组合交由系统加载配置并生成目标用户环境。即使 BPM 以管理员身份运行,子进程也使用配置绑定的 Windows 用户身份和环境。

依据:[CreateEnvironmentBlock](https://learn.microsoft.com/en-us/windows/win32/api/userenv/nf-userenv-createenvironmentblock) 的用户配置加载要求,以及 [CreateProcessWithLogonW](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-createprocesswithlogonw) 的 `LOGON_WITH_PROFILE` / `lpEnvironment` 约定。

## 本地系统用户创建

| API | 用途 |
| --- | --- |
| `NetUserAdd` | 在本机创建普通用户,设置用户输入的 Windows 密码,遵循本机密码策略 |
| `NetUserGetInfo` / `NetApiBufferFree` | 查询本地用户 SID 并释放返回的缓冲区 |
| `NetLocalGroupAddMembers` | 将新用户加入本机 Users 组,组名通过内置组 SID 解析,兼容中文 Windows |
| `CreateProfile` | 为该 SID 创建用户配置文件,无需先登录一次用户桌面 |

- 以上创建 / 初始化流程需要管理员权限。新用户只加入 Users 组;不修改系统登录策略或共享游戏目录权限。
- `CreateProfile` 返回配置已存在、组成员已存在时视为该阶段已完成,支持失败重试。
- `CreateProfile` 的输出缓冲区使用 260 字符(`MAX_PATH`),不使用 1024 字符长缓冲区。已在本机管理员会话对已注册的同一配置核对:1024 返回 `0x800706F7`,260 返回 `0x800700B7`(配置已存在)。替身回归检查覆盖这一原生参数边界。
- 账户已存在时不覆盖密码。系统用户创建后独立保留,取消配置或删除 BPM 配置不会删除系统用户。
- 编辑窗口提供显式的“仅初始化已有本地用户”入口,用于重启 BPM 后继续失败的设置;不调用 `NetUserAdd`,也不重置 Windows 密码。
- 原生 API 替身测试覆盖权限、同名用户、密码策略、初始化及重试。自动化测试不会创建真实系统用户。

依据:[NetUserAdd](https://learn.microsoft.com/en-us/windows/win32/api/lmaccess/nf-lmaccess-netuseradd)、[CreateProfile](https://learn.microsoft.com/en-us/windows/win32/api/userenv/nf-userenv-createprofile)。

### 首次浏览器关联的限制

`CreateProfile` 负责用户配置文件创建,不保证新用户已完成默认应用选择。默认浏览器关联应针对目标用户验证,不能由跨用户 Edge 进程的属主推断系统设置界面的属主。目标用户尚无 HTTP/HTTPS 默认选择时,`AssocQueryString` 可能解析到 `OpenWith.exe`,而跨用户环境中的选择界面未必能显示。

浏览器登录辅助优先转交真实认证地址，不依赖 UserChoice。自动处理失败时的兜底是在目标用户的 Windows 交互会话中完成首次设置及一次战网认证，再返回主用户使用 BPM。BPM 不复制其他用户的 UserChoice、不生成保护哈希，也不通过注册表覆盖默认浏览器。

依据:[Windows 默认应用平台](https://learn.microsoft.com/en-us/windows/apps/develop/windows-integration/default-apps-platform) 要求通过 Windows 系统界面确认默认应用选择。

## 浏览器登录辅助

- 使用 Microsoft TraceEvent 3.1.21 启用 `Microsoft-Windows-Shell-Core`（`30336ed4-e327-447c-9de0-51b652c86108`）实时进程过滤，只处理战网事件 19501 的 `Name`。需要提升的管理员令牌，权限不足明确降级；不改变组成员或安全策略。
- `OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION)`、`GetProcessTimes`、`QueryFullProcessImageNameW`、`OpenProcessToken` 核对同一进程句柄的时间、安装目录与 SID；再核对桌面 SessionId。只接受 5 秒内的已验证国服 HTTPS 入口，不复用日志里的不完整地址。
- `CreateProcessWithLogonW` 以目标用户运行 BPM 最小助手；命令行只包含随机管道名和服务端 PID。`NamedPipeServerStreamAcl` 限制操作者/目标 SID 访问，双方分别用 `GetNamedPipeClientProcessId`、`GetNamedPipeServerProcessId` 验证对端 PID。
- 私人 Debug/安装目录可能导致 `267`（工作目录不可访问）或 `5`（程序不可读取）。助手从 ProgramData 的专用、按 SID/内容哈希隔离的程序缓存运行，目标 SID 仅获 ReadAndExecute；不放宽原目录、不缓存配置/密码/URL，工作目录使用系统目录。缓存目录验证属主并拒绝重解析点，清理不做递归删除。
- URL 最多 16 KiB，经长度前缀内存管道原样传递，助手再次白名单校验后用 `ProcessStartInfo.ArgumentList` 启动浏览器。既避免 CPWL 的 1024 字符命令行上限，也无需落盘中转或命令解释器。
- 15 分钟到期/显式退出取消任务，释放租约并停止所属会话。下次启动只清理同操作者、PID 已退出的 BPM 命名会话。进程突然终止仍可能短暂遗留会话；不扫描/停止其他应用的会话。
- 浏览器交接成功不等于认证成功；没有收到事件、系统事件布局变化或国际服入口，仍需要用户重试/使用人工兜底。辅助不写完整地址/参数值/ETL。

依据：[TraceEvent](https://github.com/microsoft/perfview/blob/main/documentation/TraceEvent/TraceEventLibrary.md)、[实时 ETW 权限](https://learn.microsoft.com/en-us/windows/win32/api/evntrace/nf-evntrace-opentracew)。

## 凭据管理

| API | 用途 |
| --- | --- |
| `CredWriteW` | 写入(`CRED_TYPE_GENERIC` + `CRED_PERSIST_LOCAL_MACHINE`) |
| `CredReadW` | 读取(密码 blob 为 UTF-16 字节) |
| `CredDeleteW` | 删除(键不存在视为成功,幂等) |
| `CredFree` | 释放 `CredReadW` 返回的缓冲区 |

凭据键格式:`BPM_<Windows用户名>`;密码明文仅在启动瞬间于内存中出现,不落盘、不入日志。

## 快捷方式

`IShellLinkW` + `IPersistFile` COM 接口生成桌面 `.lnk`,目标为 `BPM.exe --launch <accountId>`,图标取自 Battle.net 启动器。

新快捷方式不设置 `SLDF_RUNAS_USER (0x2000)`，由 BPM 首次启动时自动提权；重复打开在普通权限下转交给现有管理员进程。创建完成后释放 COM 对象。旧快捷方式由用户重新创建时更新，不自动改写桌面其他文件。

依据：[微软关于快捷方式提升标记的说明](https://devblogs.microsoft.com/oldnewthing/20071219-00/?p=24103)。主程序现另有启动时自动提权，旧快捷方式和未带标记的快捷方式也会请求 UAC。

## 单实例通信

`EventWaitHandle` 命名租约保证同 SID/SessionId 只产生一个主进程，进程退出后由 Windows 释放。受限 `NamedPipeServerStreamAcl` 接收激活或配置 GUID，使用 `GetNamedPipeServerProcessId` / `GetNamedPipeClientProcessId`、`OpenProcessToken` 核对双方身份及会话，客户端通过 `GetTokenInformation(TokenElevation)` 核对管理员服务端。普通激活方先调用 `AllowSetForegroundWindow`，再由现有进程恢复/激活自身窗口。

## 主程序自动提权

`StartupElevationService` 在初始化日志、配置、凭据和 DI 前检查管理员令牌；无提升权限时用 `ProcessStartInfo` 的 `UseShellExecute=true` / `Verb=runas` 重启同一 EXE，通过 `ArgumentList` 原样传递参数，并使用系统工作目录。原进程随后退出。取消授权（1223）直接退出；其他失败显示安全的错误码，不记录完整命令行。

管理员子进程核对原用户 SID 并防止重复提权：如果 UAC 使用另一账号，会拒绝读取另一份配置/凭据。不能在整个 EXE 清单上设置 `requireAdministrator`，因为浏览器助手复用同一 EXE，必须由 `--browser-login-pipe` 分支先处理并保持普通目标用户身份。
