# FairyGUI 示例目录说明

本目录结构与 UGUI 登录/组合式 Demo 对齐，便于维护与查找。

## 目录结构
- 公共工具（如 `FairyGuiViewHelper`）已移至 `MVI/Assets/Scripts/MVI/FairyGUI/Utils/`。
- `Counter/`：计数器 Demo，按 `Intent/State/Store/Effects/ViewModels/Views` 拆分。
- `Composed/`：组合式 Demo。
  - `Views/`：组合式页面入口（`FairyComposedDashboardView`）。
  - `Components/`：组合式组件 View（CounterCard/UserCard/StatusBadge）。
- `ComposedDashboardWindow/`：FairyGUI 自动生成代码（请勿移动或手动编辑）。

## 重要说明
- `ComposedDashboardWindow/` 为 FairyGUI 发布生成的代码与绑定器，路径应与发布设置保持一致。
- 组合式 Demo 复用 UGUI 组件 ViewModel（CounterCard/UserCard/StatusBadge），仅替换 View 层。
- FairyGUI 事件绑定通过 Loxodon FairyGUI 绑定插件完成（`FairyGUIBindingServiceBundle`）。
