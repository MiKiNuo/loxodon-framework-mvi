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

**新扩展架构（UI 适配层）**
1. 组合核心：`MVI/Assets/Scripts/MVI/Core/Composition`
2. UGUI 适配器：`MVI/Assets/Scripts/MVI/Loxodon/UIAdapters/UguiViewHost.cs`
3. FairyGUI 适配器：`MVI/Assets/Scripts/MVI/FairyGUI/UIAdapters/FairyViewHost.cs`
4. 组合页面基类（UGUI/FairyGUI）已统一复用同一套组合运行时（组件注册、Props diff、事件路由、清理）。

## Demo使用方法
**入口场景**
1. 打开 `MVI/Assets/Samples/Loxodon Framework/2.0.0/Examples/Launcher.unity`
2. 运行即可

**UGUI / FairyGUI 切换**
在 `Launcher` 组件上设置：
1. `demoUiKind = UGUI`：运行 UGUI 组合式 Demo
2. `demoUiKind = FairyGUI`：运行 FairyGUI 组合式 Demo

可选开关：
1. `enableRuntimeToggle` + `toggleKey`：运行时按键切换
2. `usePlayerPrefsOverride`：通过 `PlayerPrefs` 覆盖默认值

**UGUI Demo 位置**
1. 组合式 Demo 入口：`MVI/Assets/Samples/Loxodon Framework/2.0.0/Examples/Scripts/Views/UI/Composed/Views/ComposedDashboardWindow.cs`
2. 组合式页 Prefab：`MVI/Assets/Samples/Loxodon Framework/2.0.0/Examples/Resources/UI/Composed/ComposedDashboard.prefab`
3. 组件 Prefab：`MVI/Assets/Samples/Loxodon Framework/2.0.0/Examples/Resources/UI/Components`

**FairyGUI Demo 位置**
1. 组合式 Demo 入口：`MVI/Assets/Samples/Loxodon Framework/2.0.0/Examples/Scripts/Views/UI/FairyGUI/Composed/Views/FairyComposedDashboardView.cs`
2. 组合式子组件：`MVI/Assets/Samples/Loxodon Framework/2.0.0/Examples/Scripts/Views/UI/FairyGUI/Composed/Components`
3. 计数器 Demo：`MVI/Assets/Samples/Loxodon Framework/2.0.0/Examples/Scripts/Views/UI/FairyGUI/Counter/Views/FairyCounterView.cs`
4. FairyGUI 自动生成代码：`MVI/Assets/Samples/Loxodon Framework/2.0.0/Examples/Scripts/Views/UI/FairyGUI/ComposedDashboardWindow`

**FairyGUI 初始化（必需）**
```
BindingServiceBundle bundle = new BindingServiceBundle(context.GetContainer());
bundle.Start();
FairyGUIBindingServiceBundle fairyBundle = new FairyGUIBindingServiceBundle(context.GetContainer());
fairyBundle.Start();
```

**FairyGUI 资源加载（Editor）**
```
UIPackage.AddPackage("Assets/Res/ComposedDashboardWindow");
ComposedDashboardWindowBinder.BindAll();
```

## 业务侧接入示例（中间件/错误策略/DevTools）
**示例代码位置**
1. 集成安装器：`MVI/Assets/Samples/Loxodon Framework/2.0.0/Examples/Scripts/MviIntegration/MviBusinessIntegrationInstaller.cs`
2. 业务中间件：`MVI/Assets/Samples/Loxodon Framework/2.0.0/Examples/Scripts/MviIntegration/LoginIntentAuditMiddleware.cs`
3. 接入入口：`MVI/Assets/Samples/Loxodon Framework/2.0.0/Examples/Launcher.cs`
4. Store 挂中间件示例：`MVI/Assets/Samples/Loxodon Framework/2.0.0/Examples/Scripts/Views/UI/Logins/ViewModels/LoginViewModel.cs`

**入口开关（Launcher Inspector）**
1. `enableMviDiagnostics`：开启 `MviDiagnostics` 链路日志
2. `enableMviDevTools` + `mviDevToolsMaxEvents`：开启 DevTools 时间线并设置容量
3. `enableBusinessErrorStrategy` + `businessErrorRetryCount` + `businessErrorRetryDelayMs`：开启全局错误策略与重试参数
4. `enableLoginAuditMiddleware`：是否给 `LoginStore` 注入登录审计中间件
5. `autoDumpLoginStoreTimeline`：登录完成后自动打印时间线快照
6. `enableBuiltinLoginMiddlewares`：是否启用内置中间件链（日志/防抖/超时）
7. `enableLoginLoggingMiddleware`：是否启用 `LoggingStoreMiddleware`
8. `loginDebounceMs`：登录意图防抖窗口（毫秒）
9. `loginTimeoutMs`：登录意图超时阈值（毫秒）
10. `enableLoginMetricsMiddleware`：是否启用登录链路指标中间件
11. `autoDumpLoginMiddlewareMetrics`：登录完成后自动打印中间件指标快照

**业务接入要点**
1. 在入口统一安装：`MviBusinessIntegrationInstaller.Install(...)`
2. 在具体业务 Store 创建处调用 `store.UseMiddleware(new LoginIntentAuditMiddleware())`
3. 通过 `BusinessMviIntegrationRuntime.DumpTimeline(store, tag)` 输出时间线
4. 全局错误策略走 `IMviErrorStrategy`，可按阶段返回 `Retry/Ignore/Emit/Fallback`
5. 内置中间件示例在 `LoginViewModel.ConfigureStoreMiddlewares`：`LoggingStoreMiddleware` + `DebounceIntentMiddleware` + `TimeoutIntentMiddleware`
6. 指标中间件示例：`MetricsStoreMiddleware` + `StoreMiddlewareMetricsCollector`

## 扩展能力
1. 泛型 Store / MviResult：`Store<TState, TIntent, TResult>` / `MviResult<T>`
2. Effects 与错误通道：`Store.Effects` / `Store.Errors`
3. Intent 并发与取消：`ProcessingMode` / `EmitIntent(intent, cancellationToken)`
4. 映射规则：`[MviIgnore]` / `[MviMap("OtherName")]`
5. 诊断：`MviDiagnostics`

## 组合式页面布局（FairyGUI）
布局策略目录：`MVI/Assets/Samples/Loxodon Framework/2.0.0/Examples/Scripts/Views/UI/FairyGUI/Composed/Layouts`

内置策略：
1. `VerticalCenter`
2. `LeftRightSplit`
3. `VariableSpacingVertical`
4. `Grid`
5. `AbsolutePosition`
6. `PlaceholderAlign`

`FairyComposedDashboardView` 支持 `layoutKind` 切换策略，并提供 `SwitchLayout` / `RebuildLayout` 运行时接口。
