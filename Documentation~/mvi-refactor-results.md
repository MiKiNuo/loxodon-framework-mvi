# Unity MVI 重构结果

2026-10-01。已按架构审核的 F0–F5、R1–R3 完成实现，保留开始时工作区已有修改。功能修复与结构整理的行为检查分别覆盖；没有引入新的生产依赖或通用生命周期框架。

| 审核项 | 最终实现 |
| --- | --- |
| F0：Unity 编译接线 | 共享 Hub 移入 Core/Composition；移除 C# 10 global using，生产源码保持 C# 9；修复生成器语法与诊断开关；生成器构建产物自动更新包内 Analyzers 目录。 |
| F1：状态映射 | 登记入口可跨程序集使用；取消空表的永久缓存；查询当前登记并按能处理的类型对命中；程序集身份进入 key；生成 Unity Runtime/Editor 登记入口。 |
| F2：读档与数据保留 | 未登记类型、缺 serializer、迁移未就绪和损坏数据分别处理；配置失败的 key 保留快照并拒绝初始化 Save 覆盖；成功恢复或显式 Clear 后解除保护；通知在锁外执行。 |
| F3：事件派发 | Runtime 统一派发一次；UGUI/FairyGUI 宿主默认钩子只负责通知扩展；测试从 bool 改为实际调用次数。 |
| F4：异步错误决策 | 同步入口不等待未完成 ValueTask；同步 Rethrow 保持原栈交付，异步 Rethrow 送到 R3 未处理错误通道；释放后停止交付；Replay 的直接 Reducer 错误可等待且随 Store 释放取消。 |
| F5：FairyGUI root | 根视图在取得时记录创建或借用关系；有 Panel 但未使用它的自建 root 仍释放；配置变化和重复关闭不改变取得时的所有权。 |
| R1：唯一 Store 引用 | 泛型 ViewModel 查询基类唯一 Store；重绑先校验兼容性，再变更订阅；删除派生层重复保存与释放字段。 |
| R2：执行职责集中 | 中间件 hooks、上下文池、错误重试和追踪集中到 Store.IntentExecution 的私有 IntentExecutor；保留现有 protected 扩展点与 R3 路由策略。 |
| R3：重复规则 | Hub 的 Props comparer 委托 Runtime 唯一实现；生成器 State 属性登记复用既有 TryAddProperty，保留派生优先及诊断顺序。 |

## 验证

| 检查 | 本轮结果 | 覆盖范围 |
| --- | --- | --- |
| `dotnet test tools/Runtime.Tests/Runtime.Tests.csproj -c Release` | **85/85 通过** | 当前生产 Core/绑定源码的 C# 9 编译、正常与异常绑定、错误决策上下文、释放取消、Replay、执行顺序、持久化保留与重试、组合路由。 |
| `dotnet test tools/SourceGenerator.Tests/SourceGenerator.Tests.csproj -c Release` | **7/7 通过** | 生成代码实际 C# 9 编译、Runtime/Editor 登记属性、跨程序集同名 mapper、alias/ignore、增量/强制 setter、派生优先与诊断顺序。 |
| `dotnet build MVI/SourceGenerator/SourceGenerator.csproj -c Release` | **成功，0 警告、0 错误** | 实际 netstandard2.0 / Roslyn 4.3.1 生成器产物。 |
| Release DLL 与包内 analyzer 的 SHA-256 | **一致** | Unity 使用的 Analyzers/SourceGenerator.dll 已由本轮源码更新，保留原 importer meta。 |
| `git diff --check` | **通过** | 最终变更的空白检查。 |

运行时最终结果 XML：`C:/Users/luoji/AppData/Local/Temp/mvi-refactor-20261001/runtime-results.trx`。测试输出中的 `handler-failed` 是刻意覆盖“R3 错误处理器自身抛错”分支的预期异常日志，该用例通过。

独立审查指出并已修复的两项集成问题：Replay 等待策略的 token 未连接 Store 生命周期；公开加载失败回调在持久化锁内执行。这两项均有可运行的针对回归，已包含在 85 项运行时结果内。

## 验证限制与类型契约

当前机器没有可执行的 Unity Editor 安装，项目原有引用指向的 `E:/Program Files/Unity/Editor/6000.3.7f1/Editor/Unity.exe` 也不存在。因此没有宣称实际 Unity 场景或全部 UnityTest 已通过；项目 Library 中旧 DLL 不作为本轮证明。UGUI/FairyGUI 默认路由及 root 所有权的真实宿主测试已编写，并保留在 Editor 测试程序集内。

运行时 .NET 测试使用明确的 Unity/Loxodon 存根；它不执行 `[UnityTest]` 协程，不能替代 Unity 主线程、原生资源或实际渲染。完整测试命令与验证范围见 [tools/README.md](../tools/README.md)。

默认 Json/Binary 持久化 adapter 使用 Unity 可序列化字段契约，不承诺只读属性与 record 的通用恢复。泛型 Store 自动预登记声明的 State 到默认 Shared；自定义注册器仍需显式准备类型。一次恢复失败后，受保护 key 的保存会保留原数据，直到恢复成功或显式 Clear。

架构决策见 [ADR 0001](adr/0001-unity-mvi-execution-and-ownership.md)，领域词汇见 [CONTEXT.md](../CONTEXT.md)。
