# Loxodon Framework MVI（UPM 包）

## 通过 Git URL 安装

在 Unity 项目的 `Packages/manifest.json` 中添加依赖：

```json
{
  "dependencies": {
    "com.mikinuo.loxodon-framework-mvi": "https://github.com/MiKiNuo/loxodon-framework-mvi.git?path=/MVI/Assets/Scripts/MVI"
  }
}
```

## 包内包含内容

- MVI 核心运行时：`Core/`
- Loxodon 集成与 UGUI 适配：`Loxodon/`
- FairyGUI 集成与适配：`FairyGUI/`
- 编辑器工具（DevTools 窗口）：`Editor/`

## 包内不包含内容

- 本 UPM 包不包含 Demo 与业务示例。
- 如需查看示例场景与完整接入示例，请克隆仓库后查看：
  - `MVI/Assets/Samples/`

## 说明

- 本包默认你已在项目中安装相关依赖，例如：
  - `R3.Unity`
  - `Loxodon.Framework`
  - `FairyGUI`（仅在使用 FairyGUI 集成时需要）
- 更完整的架构与示例说明，请查看仓库根目录 `README.md`。
