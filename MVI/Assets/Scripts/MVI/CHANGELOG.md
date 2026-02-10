# 更新日志

## [0.1.1] - 2026-02-10

### 调整

- 将 `FairyGUI` 第三方源码迁入包内：`ThirdParty/FairyGUI/`。
- 通过 UPM 路径 `?path=/MVI/Assets/Scripts/MVI` 安装时会自动包含 FairyGUI，无需再单独导入。

## [0.1.0] - 2026-02-10

### 新增

- `StoreProfile` 全局默认配置与中间件/策略组合能力。
- Middleware V2 生命周期支持（`BeforeIntent`、`AfterResult`、`OnError`），并加入上下文池化与关联 ID。
- 错误策略决策链路追踪（`ruleId`、`priority`、`phase`、`attempt`）。
- 命名空间持久化存储 API 与辅助 key 生成方法。
- DevTools 采样配置与导出元数据。
- 内置弹性中间件：
  - `RateLimitIntentMiddleware`
  - `CircuitBreakerIntentMiddleware`
- DevTools 时间线统计与中间件链路导出能力。
- 包内包含 `SourceGenerator.dll`（`RoslynAnalyzer` 标签）用于映射代码生成。

### 调整

- Store 错误处理保持对旧版 `OnProcessError(Exception)` 覆写方式的兼容。
- DevTools 编辑器窗口支持采样控制与统计摘要展示。
