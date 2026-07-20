# Battle Platform Manager (BPM)

Windows 平台的多用户配置文件启动管理器。首个适配器面向 **Battle.net**:让不同 Windows 用户拥有完全独立的 Battle.net 登录状态与用户数据,同时**共用同一份《魔兽世界》安装目录**——多个实例可同时运行,无需切换 Windows 登录账户。

> BPM is a Windows multi-user profile launcher manager. Each Battle.net instance runs under its own Windows user (via the documented `CreateProcessWithLogonW` API), giving fully isolated login state while sharing a single game installation.

[![Build](https://github.com/anchoretics/BattlePlatformManager/actions/workflows/build.yml/badge.svg)](https://github.com/anchoretics/BattlePlatformManager/actions/workflows/build.yml)

## 功能特性

- **一键启动**:桌面快捷方式或主界面按钮直接启动指定 Battle.net 实例。
- **完全隔离**:每个配置档绑定独立 Windows 用户,Battle.net 登录状态、缓存、配置互不影响。
- **共享游戏目录**:所有实例共用同一份 Battle.net 安装与《魔兽世界》游戏文件,不浪费磁盘。
- **多开并行**:多个实例同时运行,无需注销 / 切换 Windows 账户。
- **凭据安全**:Windows 用户密码仅保存于系统凭据管理器(Credential Manager),配置文件零明文。
- **配置管理**:配置档的添加、编辑、删除、导入、导出。

## 工作原理

BPM 不修改任何游戏文件。它只做一件事:调用 Windows 官方 API `CreateProcessWithLogonW`(`LOGON_WITH_PROFILE`),在你预先创建的其他 Windows 用户上下文中启动 Battle.net——效果等同于 `runas`,但免去每次输入密码与命令行的麻烦。Battle.net 随后自然地读写该用户自己的 `AppData`,隔离由 Windows 用户机制本身保证:

| 隔离的数据 | 路径 |
| --- | --- |
| Battle.net 用户配置 | `C:\Users\<User>\AppData\Roaming\Battle.net` |
| Battle.net 本地缓存 | `C:\Users\<User>\AppData\Local\Battle.net` |
| Blizzard 本地数据 | `C:\Users\<User>\AppData\Local\Blizzard Entertainment` |
| 部分游戏配置 | `C:\Users\<User>\Documents` |

## 安装与使用

1. 从 [Releases](../../releases) 下载最新的 `BPM-*-win-x64.zip`,解压到任意目录(单文件 EXE,无需安装)。
2. 在 Windows 中为每个游戏账号创建一个本地用户(例如 `WoW1`、`WoW2`)并设置密码
   (设置 → 账户 → 其他用户 → 添加用户,无需微软账号)。
3. 运行 `BPM.exe`,点击 **添加账户**:填写显示名、Windows 用户名、该用户的密码、Battle.net 启动器路径。
4. 点击 **启动**,或先 **创建桌面快捷方式**,以后双击快捷方式一键启动。
5. 首次在新用户下启动 Battle.net 时登录一次战网账号并勾选“保持登录”,之后即保持独立登录状态。

### 常见问题

- **提示“用户名或密码错误”**:请确认输入的是 *Windows 用户* 的密码(不是战网密码)。
- **提示 Secondary Logon 服务未运行**:在“服务”中将 `Secondary Logon (seclogon)` 设为自动并启动。
- **该 Windows 用户密码为空**:本地安全策略默认限制空密码账户,请为该用户设置密码。
- **游戏目录共享**:所有配置档直接指向同一 Battle.net / WoW 安装目录即可;请确保目标用户对游戏目录有读写权限(必要时在目录属性 → 安全中为 Users 组授予修改权限)。
- **日志位置**:`%LOCALAPPDATA%\BPM\logs`,日志不含任何密码。

## 安全声明

- ✅ 仅使用官方文档化的 Windows API(`CreateProcessWithLogonW`、Credential Manager 等)。
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
