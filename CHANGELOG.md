# Changelog

本项目遵循 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/) 与 [语义化版本](https://semver.org/lang/zh-CN/)。

## [Unreleased]

### Added

- 首个版本:Battle.net 多 Windows 用户配置档管理与一键启动。
- 账户管理:添加、编辑、删除、导入、导出配置档。
- 凭据管理:密码仅保存于 Windows Credential Manager,配置文件零明文。
- 通过 `CreateProcessWithLogonW` + `LOGON_WITH_PROFILE` 在独立 Windows 用户上下文中启动 Battle.net。
- 桌面快捷方式生成(`BPM.exe --launch <accountId>` 一键启动)。
- Serilog 日志、Clean Architecture 分层、xUnit 测试、GitHub Actions CI。
