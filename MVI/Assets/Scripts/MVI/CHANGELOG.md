# 更新日志

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

### 调整

- Store 错误处理保持对旧版 `OnProcessError(Exception)` 覆写方式的兼容。
- DevTools 编辑器窗口支持采样控制与统计摘要展示。
