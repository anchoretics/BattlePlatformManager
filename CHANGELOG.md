# Changelog

本项目遵循 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/) 与 [语义化版本](https://semver.org/lang/zh-CN/)。

## [Unreleased]

### Fixed

- 修正目标用户环境的生成时机:由 `CreateProcessWithLogonW(LOGON_WITH_PROFILE, lpEnvironment = NULL)` 加载用户配置并生成环境,解决开机首次启动战网时登录状态异常、需要重新定位游戏目录及游戏启动失败的问题。2026-10-25 用户实机验证通过。
- 保留直接创建进程时的凭据错误、账户受限和登录权限错误提示,增加无需真实用户密码的启动回归测试。

### Added

- 添加账户时可一键创建普通 Windows 本地用户,通过系统 API 初始化用户配置,无需手动登录 Windows 桌面。创建需要管理员权限,并遵循本机密码策略。
- 创建进度支持凭据写入、配置初始化及 BPM 配置保存失败后的原窗口重试;已有系统用户不会被重置密码,删除 BPM 配置不会删除系统用户。
- 首个版本:Battle.net 多 Windows 用户配置档管理与一键启动。
- 账户管理:添加、编辑、删除、导入、导出配置档。
- 凭据管理:密码仅保存于 Windows Credential Manager,配置文件零明文。
- 通过 `CreateProcessWithLogonW` + `LOGON_WITH_PROFILE` 在独立 Windows 用户上下文中启动 Battle.net。
- 桌面快捷方式生成(`BPM.exe --launch <accountId>` 一键启动)。
- Serilog 日志、Clean Architecture 分层、xUnit 测试、GitHub Actions CI。
