# 产品规格

## 目标

- 管理员账户下通过桌面快捷方式一键启动指定 Battle.net 实例。
- 每个 Battle.net 实例绑定独立 Windows 用户;登录状态、缓存、配置完全隔离。
- 多实例同时运行,无需切换 Windows 登录账户。
- 所有实例共享同一份 Battle.net 安装目录与《魔兽世界》游戏目录。
- 开源发布(GitHub,MIT License)。

## 非目标(安全边界)

- 不修改 Battle.net、WoW.exe 或任何游戏文件。
- 不进行 DLL 注入、Hook、Patch 或逆向协议行为。
- 不提供游戏自动化、脚本操作或外挂相关功能。
- 不绕过 Battle.net 登录、授权或暴雪安全机制。

## 核心功能

| 功能 | 说明 |
| --- | --- |
| 账户管理 | 配置档增删改、导入导出;绑定 Windows 用户与游戏路径 |
| 凭据管理 | 密码仅存 Windows Credential Manager,配置文件零明文 |
| 启动 | `CreateProcessWithLogonW` + `LOGON_WITH_PROFILE`,加载目标用户 Profile |
| 快捷方式 | 桌面 `.lnk` 指向 `BPM.exe --launch <accountId>` |
| 状态展示 | 主界面显示各配置档运行状态(PID) |

## 验收标准

- Windows 10 / 11 可运行;至少 5 个配置档同时启动。
- 不同配置档 Battle.net 登录状态互不影响;共享同一 WoW 目录。
- 无明文密码存储;日志不含敏感凭据。
- `dotnet build` 0 错误 0 警告;`dotnet test` 全部通过;GitHub Actions 构建成功并产出 Release。
