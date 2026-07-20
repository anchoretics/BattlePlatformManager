# ADR-001:采用 Clean Architecture 分层

日期:2026-07-20 · 状态:已接受

## 背景

项目需要长期演进(未来接入更多游戏平台),且核心启动逻辑依赖大量 Windows 专有 API,必须保证业务规则可在任意平台上测试。

## 决策

采用 Clean Architecture:Domain 零依赖;Application 只定义接口与用例;Win32/文件系统等副作用全部下沉到 Infrastructure 实现;WPF 仅做展示与装配。

## 结果

- Domain / Application / 大部分 Infrastructure 测试可在非 Windows 环境运行(本项目即在 macOS 上开发构建,CI 在 Windows 上全量执行)。
- 新平台只需实现 `IGamePlatformAdapter`,启动管线零改动。
- 代价:项目数量较多,小功能也需跨层接线;对本项目规模可接受。
