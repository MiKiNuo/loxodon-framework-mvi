# loxodon-framework-mvi介绍
## 首先感谢loxodon-framework框架作者， loxodon-framework是一个MVVM双向数据绑定的用于Unity开发的框架，在代码质量、可读性、性能等方面非常优秀。
https://github.com/vovgou/loxodon-framework
### MVI架构图如下
![alt text](mad-arch-ui-udf.png)

**架构概览（流程图）**
```
用户交互
   │
   ▼
View(UI)
   │  绑定/事件
   ▼
MviViewModel
   │  EmitIntent
   ▼
Intent
   │  处理意图
   ▼
Store
   │  产生新状态
   ▼
State
   │  TryMap/绑定
   ▼
MviViewModel
   │  数据驱动
   ▼
View(UI)

（组合式组件化，可选）
ComposedWindowBase
   ├─ 组件注册表 → 组件A(View+ViewModel+Store)
   ├─ 组件注册表 → 组件B(View+ViewModel+Store)
   └─ 事件路由表 ↔ 组件A / 组件B（Props/Events）
```

**类关系图**
```
IIntent
IMviResult
IState
IMviEffect

MviViewModel / MviViewModel<TState, TIntent, TResult, TEffect>
  └─ 依赖：GeneratedStateMapper（由 SourceGenerator 生成）

Store<TState, TIntent, TResult>
  ├─ 输入：TIntent : IIntent
  ├─ 输出：TResult : IMviResult
  └─ 管理：TState : IState

Store<TState, TIntent, TResult, TEffect>
  ├─ Effects：TEffect : IMviEffect
  └─ Errors：MviErrorEffect

（组合式组件化）
ComposedWindowBase : Window
  ├─ 组件注册：IViewBinder / IPropsReceiver<T> / IForceUpdateProps
  ├─ 事件输出：ComponentEvent
  └─ 组合 DSL：Compose / Component / WithProps / On / CompareProps
```

## 架构与模块
**核心接口**
1. `IIntent`：意图
2. `IMviResult`：结果
3. `IState`：状态（建议使用 Record）
4. `IMviEffect`：一次性事件（Toast/弹窗/导航）

**核心类**
1. `MviViewModel`：继承 `ViewModelBase`，负责绑定 Store 与发射 Intent
2. `Store`：状态管理与结果处理
3. `StoreMiddlewareContext / IStoreMiddleware`：Store 中间件扩展点（日志、鉴权、重试、埋点）
4. `IntentProcessingPolicy`：按 Intent 类型配置并发策略（Queue/Drop/Switch/Parallel）
5. `MviSelector`：Selector 记忆化工具，降低重复计算与无效刷新
6. `IStoreStatePersistence`：状态持久化插件接口（支持恢复与迁移）
7. `MviDevTools`：时间线调试工具（Intent/Result/State/Effect/Error、Replay、Time-travel）
8. `IMviErrorStrategy / MviErrorDecision`：全局错误策略（重试、忽略、回退、抛出）
9. `Store.UndoState / RedoState / TryTimeTravelToHistoryIndex`：状态历史回退能力
10. `StoreTestKit`：Store 测试辅助 DSL（采集状态、等待条件、快速发射 Intent）

**组合式组件化**
1. 基础组件：`MVI/Assets/Scripts/MVI/Core/Components`
2. 组合式基类：`MVI/Assets/Scripts/MVI/Loxodon/Composed`
3. 示例组件：`MVI/Assets/Samples/Loxodon Framework/2.0.0/Examples/Scripts/Views/UI/Components`

## Demo使用方法
**入口场景**
1. 打开 `MVI/Assets/Samples/Loxodon Framework/2.0.0/Examples/Launcher.unity`
2. 运行即可

**UGUI / FairyGUI 切换**
在 `Launcher` 上设置：
1. `demoUiKind = UGUI`：运行 UGUI 组合式 Demo
2. `demoUiKind = FairyGUI`：运行 FairyGUI 组合式 Demo

**UGUI Demo 位置**
1. 组合式 Demo 入口：`MVI/Assets/Samples/Loxodon Framework/2.0.0/Examples/Scripts/Views/UI/Composed/Views/ComposedDashboardWindow.cs`
2. 组合式页 Prefab：`MVI/Assets/Samples/Loxodon Framework/2.0.0/Examples/Resources/UI/Composed/ComposedDashboard.prefab`
3. 组件 Prefab：`MVI/Assets/Samples/Loxodon Framework/2.0.0/Examples/Resources/UI/Components`

**FairyGUI Demo 位置**
1. 组合式 Demo 入口：`MVI/Assets/Samples/Loxodon Framework/2.0.0/Examples/Scripts/Views/UI/FairyGUI/Composed/Views/FairyComposedDashboardView.cs`
2. 组合式子组件：`MVI/Assets/Samples/Loxodon Framework/2.0.0/Examples/Scripts/Views/UI/FairyGUI/Composed/Components`
3. 计数器 Demo：`MVI/Assets/Samples/Loxodon Framework/2.0.0/Examples/Scripts/Views/UI/FairyGUI/Counter/Views/FairyCounterView.cs`
4. FairyGUI 自动生成代码：`MVI/Assets/Samples/Loxodon Framework/2.0.0/Examples/Scripts/Views/UI/FairyGUI/ComposedDashboardWindow`



