# Windows API 设计

仅使用官方文档化 API,封装集中在 `BPM.Infrastructure.Win32/NativeMethods.cs`。

## 进程启动

| API | 用途 |
| --- | --- |
| `LogonUserW` | 预校验凭据(快速失败、友好报错)并获取用户令牌 |
| `CreateEnvironmentBlock` / `DestroyEnvironmentBlock` | 由令牌生成目标用户环境变量块 |
| `CreateProcessWithLogonW` | 在目标用户上下文启动进程 |
| `CloseHandle` | 释放进程 / 线程 / 令牌句柄 |

关键参数:

| 参数 | 值 | 说明 |
| --- | --- | --- |
| `dwLogonFlags` | `LOGON_WITH_PROFILE (0x1)` | 加载目标用户 Profile,Battle.net 才能读到自己的 AppData |
| `dwCreationFlags` | `CREATE_UNICODE_ENVIRONMENT (0x400)` | 环境块为 Unicode |
| `lpApplicationName` | `null` | 路径经引号包裹后放入 `lpCommandLine` |
| `lpCommandLine` | `StringBuilder` | API 要求可写缓冲区 |
| `lpCurrentDirectory` | Battle.net 安装目录 | |

注意事项:

- 依赖 **Secondary Logon(seclogon)** 服务;禁用时报 `1058/1062`,已映射为友好提示。
- 常见错误映射:`1326` 凭据错误、`1327` 账户受限(空密码)、`1385` 无交互式登录权限、`2/3` 路径无效。
- 所有句柄与环境块在 `finally` 中释放;失败路径记录 `Win32Error`。

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
