# Loxodon Framework MVI（UPM 包）

MVI 架构框架，包含 Store 中间件、状态持久化、DevTools、组合式组件运行时，以及 UGUI 与 FairyGUI 适配。

- 包名：`com.mikinuo.loxodon-framework-mvi`
- 最低 Unity 版本：2021.3
- 依赖形态：**自包含**，无需额外安装任何包或 NuGet 组件

## 安装

在 `Packages/manifest.json` 中加入：

```json
{
  "dependencies": {
    "com.mikinuo.loxodon-framework-mvi": "https://github.com/MiKiNuo/loxodon-framework-mvi.git?path=/MVI/Assets/Scripts/MVI"
  }
}
```

或在 Package Manager 中 `Add package from git URL` 填入：

```
https://github.com/MiKiNuo/loxodon-framework-mvi.git?path=/MVI/Assets/Scripts/MVI
```

`?path=` 是必需参数：本包的 `package.json` 位于仓库子目录，省略后会报 `Repository does not contain a package manifest`。生产环境建议在 URL 末尾追加 `#<tag>` 或 `#<commit>` 锁定版本。

安装后无需任何手动步骤，Unity 自动完成导入与编译。

## 包内已内置的依赖

| 依赖 | 版本 | 位置 | 说明 |
|---|---|---|---|
| R3 运行时 | NuGet 1.3.0 | `ThirdParty/R3/Plugins/` | `R3.dll` 及 5 个 NuGet 依赖程序集 |
| R3 Unity 适配层 | 1.3.1 | `ThirdParty/R3/Unity/` | 提供 `ObserveOnMainThread`、`UnityFrameProvider`、Triggers 等 |
| Loxodon Framework 运行时 | 2.6.7 | `ThirdParty/Loxodon/` | 仅 `Framework` 与 `Log` 两个程序集 |
| FairyGUI | 上游 master | `ThirdParty/FairyGUI/` | 运行时、编辑器与 Resources |

## 不要重复安装

包内已包含上述依赖，**请勿同时安装**以下任意一项，否则会出现同名程序集或重复类型冲突：

- `com.cysharp.r3`（R3 的 UPM 集成层）
- NuGet 版 `R3` 及其依赖（通过 NuGetForUnity 安装）
- `com.vovgou.loxodon-framework`（Loxodon Framework 官方包）
- FairyGUI 官方包或 `.unitypackage`

## 验证安装

1. Console 无 `CS0246`、`CS0433` 或 `Multiple precompiled assemblies` 报错。
2. `Library/ScriptAssemblies` 下生成 `MVI.Core.dll`、`MVI.Loxodon.dll`、`MVI.FairyGUI.dll`、`Loxodon.Framework.dll`。

## 包内结构

- `Core/`：MVI 核心运行时
- `Loxodon/`：Loxodon 集成与 UGUI 适配
- `FairyGUI/`：FairyGUI 集成与适配
- `Editor/`：编辑器工具（DevTools 窗口）
- `Analyzers/`：源生成器（`MviMap` / `MviIgnore` 映射优化）
- `ThirdParty/`：内置的 R3、Loxodon、FairyGUI

不包含 Demo 与业务示例，示例场景请查看仓库的 `MVI/Assets/Samples/`。

## 第三方同步

内置的第三方库通过仓库根目录的 `tools/sync-thirdparty.py` 与上游保持一致：

```bash
python tools/sync-thirdparty.py --check                  # 检查上游可达性
python tools/sync-thirdparty.py fairygui                 # 同步 FairyGUI（master）
python tools/sync-thirdparty.py loxodon --ref v2.6.7     # 同步 Loxodon 指定 tag
python tools/sync-thirdparty.py r3 --r3-version 1.3.0    # 同步 R3 NuGet 程序集
python tools/sync-thirdparty.py all
```

同步采用覆盖写入，上游已删除的文件会列在输出中，需人工确认后清理。FairyGUI 上游仓库不提供 `package.json`，无法作为 UPM 依赖引用，只能以此方式同步。

## 已知约束

- **版本锁定**：包内第三方库版本随包版本冻结。需要不同版本时，请修改 `tools/sync-thirdparty.py` 中的版本常量并重新同步。
- **FairyGUI 补丁**：若对 `ThirdParty/FairyGUI` 有本地修改，同步时会被上游覆盖，请改为在 `FairyGUI/` 适配层中扩展。
