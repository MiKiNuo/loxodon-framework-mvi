# 发布指南

## 目标

通过 Git + UPM 发布核心包，示例代码仅保留在仓库中，不随包分发。

## 发布前检查清单

1. 确认包元数据已更新：
   - `package.json` 版本号
   - `CHANGELOG.md` 发布记录
2. 在本地 Unity 环境完成编译与测试验证。
3. 确认安装路径参数正确：
   - `?path=/MVI/Assets/Scripts/MVI`

## 版本规范

建议使用语义化版本：

- `patch`：缺陷修复与不破坏兼容的优化
- `minor`：向后兼容的新功能
- `major`：不兼容变更

## 打标签与发布

如果当前包目录改动都需要提交，可直接：

```bash
git add -A MVI/Assets/Scripts/MVI
```

如果工作区中有无关改动，请按文件精确暂存：

```bash
git add \
  MVI/Assets/Scripts/MVI/package.json \
  MVI/Assets/Scripts/MVI/package.json.meta \
  MVI/Assets/Scripts/MVI/README.md \
  MVI/Assets/Scripts/MVI/README.md.meta \
  MVI/Assets/Scripts/MVI/CHANGELOG.md \
  MVI/Assets/Scripts/MVI/CHANGELOG.md.meta \
  MVI/Assets/Scripts/MVI/Documentation~.meta \
  MVI/Assets/Scripts/MVI/Documentation~/GettingStarted.md \
  MVI/Assets/Scripts/MVI/Documentation~/GettingStarted.md.meta \
  MVI/Assets/Scripts/MVI/Documentation~/ReleaseGuide.md \
  MVI/Assets/Scripts/MVI/Documentation~/ReleaseGuide.md.meta \
  MVI/Assets/Scripts/MVI/Editor/MVI.Editor.asmdef \
  MVI/Assets/Scripts/MVI/Editor/MVI.Editor.asmdef.meta
```

然后执行发布：

```bash
git commit -m "release: v0.1.0"
git tag v0.1.0
git push origin main
git push origin v0.1.0
```

## 用户安装示例

```json
{
  "dependencies": {
    "com.mikinuo.loxodon-framework-mvi": "https://github.com/<your-org>/<your-repo>.git?path=/MVI/Assets/Scripts/MVI#v0.1.0"
  }
}
```

## 示例策略

- 示例不随本 UPM 包分发。
- 需要示例时，请克隆仓库并查看 `MVI/Assets/Samples/`。
