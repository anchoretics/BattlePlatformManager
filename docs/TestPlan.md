# 测试计划

## 单元测试(全平台运行)

| 项目 | 覆盖 |
| --- | --- |
| BPM.Domain.Tests | 账户校验规则、用户名域拆分、凭据键派生、模型默认值 |
| BPM.Application.Tests | AccountService 增删改(凭据联动、共享凭据保护)、LaunchService 编排(凭据缺失、路径无效、启动失败传播)、BattleNetAdapter 路径校验与启动参数 |
| BPM.Infrastructure.Tests | config.json 读写往返、损坏文件、导入导出、**配置文件不含密码字样断言** |

## 集成测试(`[WindowsOnlyFact]`,CI windows-latest 真实执行)

- Credential Manager 真实读写删往返、覆盖写入、幂等删除。

## 手工验收(需真实多用户环境)

| 测试项 | 预期结果 |
| --- | --- |
| 启动单个 Battle.net | 成功启动并加载对应 Windows 用户数据 |
| 启动多个 Battle.net | ≥5 个实例同时运行 |
| 登录状态隔离 | 不同实例保持不同 Battle.net 登录状态 |
| 游戏目录共享 | 所有实例识别同一 WoW 安装目录 |
| 错误凭据 | 提示“用户名或密码错误”并记录日志 |
| 缺失 Battle.net 路径 | 提示重新配置路径 |
| seclogon 禁用 | 提示启用 Secondary Logon 服务 |
| 快捷方式 | 双击 `.lnk` 直接启动对应实例,无主界面 |

## 执行

```bash
dotnet test --configuration Release
```
