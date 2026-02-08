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

## 扩展能力（摘要）
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
