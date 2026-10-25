# ADR-002:用 CreateProcessWithLogonW 实现实例隔离

日期:2026-07-20 · 状态:已接受

## 背景

需要让多个 Battle.net 实例拥有独立登录状态且共享同一安装目录,并排除一切修改游戏、注入、Hook 的方案(安全与合规红线)。

## 备选方案

1. **沙箱/虚拟化(Sandboxie 类)**:重量级、兼容性差、易被反作弊误伤。
2. **修改 Battle.net 数据目录指向**:需要 Hook 文件系统调用,越过红线。
3. **`runas` 命令行**:原理正确,但无法保存密码、体验差。
4. **`CreateProcessWithLogonW` + `LOGON_WITH_PROFILE`**:Windows 官方多用户机制,隔离由操作系统保证。

## 决策

选方案 4。使用 `CreateProcessWithLogonW(LOGON_WITH_PROFILE, lpEnvironment = NULL)` 校验凭据、加载目标用户配置文件并由系统生成环境,零游戏文件改动。

2026-10-24 修订:移除启动前的 `LogonUserW` 和 `CreateEnvironmentBlock`。开机后目标用户配置文件可能尚未加载,提前生成的环境无法保证包含用户专属变量;由系统按目标用户配置生成环境,避免首次与后续启动使用不同环境。

## 结果

- 依赖 seclogon 服务与目标用户的交互式登录权限(错误已映射为友好提示)。
- 用户需预先创建 Windows 本地账户并设置密码(空密码受本地安全策略限制)。
