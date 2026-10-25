# Windows API 设计

仅使用官方文档化 API,封装集中在 `BPM.Infrastructure.Win32/NativeMethods.cs`。

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
- 账户已存在时不覆盖密码。系统用户创建后独立保留,取消配置或删除 BPM 配置不会删除系统用户。
- 原生 API 替身测试覆盖权限、同名用户、密码策略、初始化及重试。自动化测试不会创建真实系统用户。

依据:[NetUserAdd](https://learn.microsoft.com/en-us/windows/win32/api/lmaccess/nf-lmaccess-netuseradd)、[CreateProfile](https://learn.microsoft.com/en-us/windows/win32/api/userenv/nf-userenv-createprofile)。

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
