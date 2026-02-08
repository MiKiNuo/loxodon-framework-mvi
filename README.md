# loxodon-framework-mvi介绍
## 首先感谢loxodon-framework框架作者， loxodon-framework是一个MVVM双向数据绑定的用于Unity开发的框架，在代码质量、可读性、性能等方面非常优秀。
https://github.com/vovgou/loxodon-framework
### MVI架构图如下
![alt text](mad-arch-ui-udf.png)

### 本项目的 MVI 落地架构图（便于快速上手）
#### 流程图（纯文本）
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

#### 类关系图（纯文本）
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
MVI架构是谷歌最新的UI架构，是在MVVM基础上解决一些生产环境的痛点而产生单项数据、响应式、不可变状态的新型框架，目前主要是在Android原生上使用的比较多，Unity以及其他方面基本没有，所以才创建了loxodon-framework-mvi库。
###
loxodon-framework-mvi在loxodon-framework框架上进行扩展实现MVI架构，没有修改loxodon-framework任何代码，通过Nuget进行包管理引用loxodon-framework，实现了响应式编程、单数据流、不可变状态，主要依赖如下开源库实现：
### R3 https://github.com/Cysharp/R3
### loxodon-framework-mvi 类介绍
#### IIntent意图类，用于执行一系列的意图
#### IMviResult结果类，用于生成意图的结果
#### IState状态类，表示UI进行显式的状态信息
#### IMviEffect一次性事件类（Toast/弹窗/导航等）
#### MviViewModel类是继承loxodon-framework框架的ViewModelBase类，ViewModelBase类是用来处理业务逻辑的，MVVM所有的业务逻辑基本都写在ViewModel中
#### Store类是管理状态的更新，用于生成新的状态

## 使用教程
### 1、每一个意图都需于要实现Intent接口
### 2、UI界面需要刷新的状态，每个状态都要实现IState接口,状态类最好使用Record类型使其不可变，
### 3、每个模块或者说每个界面都要有一个Store进行对状态的管理，所以需要继承Store类，具体可以参考Demo
### 4、View和ViewModel绑定具体教程参考loxodon-framework框架，ViewModel只用绑定相关UI属性和对应点击事件即可,在ViewModel的构造函数中执行绑定BindStore方法，具体看LoginViewModel的构造函数，绑定按钮事件需要执行EmitIntent方法触发意图，具体看Login()方法
具体实现代码可以看Demo中的登录实例的代码，其中加载进度的代码是loxodon-framework框架Demo的并没有进行修改，登录Demo分别定义了Intent、State、Store、ViewModel、View、Const文件夹
### 5、组合式组件化（新增）
新增组合式组件化基础能力，位于：
- `MVI/Assets/Scripts/MVI/Core/Components`（命名空间：`MVI.Components`）：`IViewBinder`、`IPropsReceiver<T>`、`IForceUpdateProps`、`ComponentEvent`
- `MVI/Assets/Scripts/MVI/Loxodon/Composed`（命名空间：`MVI.Composed`）：`ComposedWindowBase`（组件注册表、事件路由表、props diff、生命周期管理、声明式组合 DSL）

示例 Demo 代码位于：
- `MVI/Assets/Samples/Loxodon Framework/2.0.0/Examples/Scripts/Views/UI/Composed/Views/ComposedDashboardWindow.cs`
- 组件示例：`MVI/Assets/Samples/Loxodon Framework/2.0.0/Examples/Scripts/Views/UI/Components/*`
- 组件 Prefab：`MVI/Assets/Samples/Loxodon Framework/2.0.0/Examples/Resources/UI/Components/*`
- 组合页 Prefab：`MVI/Assets/Samples/Loxodon Framework/2.0.0/Examples/Resources/UI/Composed/ComposedDashboard.prefab`

#### 如何切换原来的登录/注册 Demo
启动入口在 `MVI/Assets/Samples/Loxodon Framework/2.0.0/Examples/Launcher.cs`：
- 组合式 Demo（当前）：加载 `ComposedDashboardWindow` 与资源 `UI/Composed/ComposedDashboard`
- 原登录/注册 Demo：改回 `StartupWindow` 与资源 `UI/Startup/Startup`

示例切换代码：
```
// 组合式 Demo
ComposedDashboardWindow window = locator.LoadWindow<ComposedDashboardWindow>(winContainer, "UI/Composed/ComposedDashboard");

// 原登录/注册 Demo
StartupWindow window = locator.LoadWindow<StartupWindow>(winContainer, "UI/Startup/Startup");
```

## 新增能力与扩展
### 泛型 Store / MviResult
使用 `Store<TState, TIntent, TResult>` 与 `MviResult<T>`，减少强制转换与运行期错误。

### Effects 与错误通道
一次性事件通过 `IMviEffect` 发送，避免污染 State。错误统一通过 `MviErrorEffect` 暴露：
- `Store.Effects`：业务 Effect（Toast/导航等）
- `Store.Errors`：标准化错误流

### Intent 并发与取消
Store 支持并发策略与取消：
- `ProcessingMode`：`Switch/Sequential/Parallel/SequentialParallel` 等
- `MaxConcurrent`：并发上限
- `EmitIntent(intent, cancellationToken)`：可取消意图

### 初始状态
Store 可覆写 `CreateInitialState()` 或 `InitialState`（泛型 Store）来提供初始状态。

### 可定制映射规则
通过特性控制映射：
- `[MviIgnore]`：忽略该属性
- `[MviMap("OtherName")]`：自定义映射名（可用在 State 或 ViewModel）

### 诊断与可观测性
使用 `MviDiagnostics` 统一记录 Intent/Result/State/Effect 流程：
```
MviDiagnostics.Enabled = true;
MviDiagnostics.Log = msg => UnityEngine.Debug.Log(msg);
```

### 程序集拆分（asmdef）
已将核心与 UI 框架适配拆分为独立程序集：
- `MVI.Core`：核心运行时（依赖 `R3.Unity`）
- `MVI.Loxodon`：Loxodon 适配（依赖 `MVI.Core`、`R3.Unity`、`Loxodon.Framework`）
- `MVI.Examples`：示例代码（依赖 `MVI.Core`、`MVI.Loxodon`、`Loxodon.Framework`、`Loxodon.Log`）
- `MVI.Tests`：编辑器测试（依赖 `MVI.Core`、`MVI.Loxodon`、`R3.Unity`、Unity TestRunner）
- `MVI.FairyGUI` / `MVI.NGUI`：可选 UI 适配（Define 约束：`MVI_FAIRYGUI` / `MVI_NGUI`）

### 测试示例
编辑器下测试示例位于：
- `MVI/Assets/Tests/Editor/MviStoreTests.cs`

### FairyGUI 适配示例
示例代码位于：
- `MVI/Assets/Samples/Loxodon Framework/2.0.0/Examples/Scripts/Views/UI/FairyGUI/FairyCounterExample.cs`
- `MVI/Assets/Samples/Loxodon Framework/2.0.0/Examples/Scripts/Views/UI/FairyGUI/FairyComposedDashboardView.cs`
启用条件：
- 在 Unity 的 `Scripting Define Symbols` 中添加 `MVI_FAIRYGUI`
说明：
- `MviFairyView` 会先加载 `PackagePaths`（如 `UI/xxx`），再通过 `PackageName/ComponentName` 创建 UI
- 支持两种模式：`UIPanel`（Inspector 配置）或 `GRoot`（代码创建并挂载）
- 若要对接 AssetBundle / YooAsset，可实现 `IFairyPackageLoader` 并覆写 `PackageLoader`
 - FairyGUI 包示例路径：`MVI/Assets/Res/ComposedDashboardWindow`（Editor 下可直接 `Assets/...` 路径加载）

## Demo演示
打开Unity工程找到Samples\Loxodon Framework\2.0.0\Examples\Launcher场景，直接运行即可，该项目工程是在官方Demo基础上进行修改，具体可以进行对比，使用MVI架构后ViewModel和View之间只存在绑定关系不存在业务逻辑关系，所有的业务逻辑都分发到Intent中
