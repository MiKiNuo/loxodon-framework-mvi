# MVI 验证

运行时逻辑和源码生成器使用同一份生产源码进行验证，语言版本固定为 C# 9。

```powershell
dotnet test tools/Runtime.Tests/Runtime.Tests.csproj -c Release
dotnet test tools/SourceGenerator.Tests/SourceGenerator.Tests.csproj -c Release
dotnet build MVI/SourceGenerator/SourceGenerator.csproj -c Release
```

生成器构建会把产物复制到 `MVI/Assets/Scripts/MVI/Analyzers/SourceGenerator.dll`，保留已有 RoslynAnalyzer 导入设置。不要再把生成器或其依赖放到 `Assets/Plugins` 作为运行时程序集。

`Runtime.Tests` 只验证状态流、错误决策、绑定生命周期、持久化和组合路由的逻辑。它使用轻量的 Unity/Loxodon 存根及字段 JSON，**不执行** `[UnityTest]` 协程，不提供 Unity 主线程、FairyGUI 渲染或原生资源释放保证。`SourceGenerator.Tests` 会实际以 C# 9 编译生成代码并验证跨程序集登记，不依赖旧 analyzer DLL。

完整 Unity 验证使用项目对应的 Unity 6000.3.7f1：

```powershell
& '<Unity Editor 目录>/Unity.exe' -batchmode -nographics -projectPath '<仓库绝对路径>/MVI' -runTests -testPlatform EditMode -testResults '<结果目录>/mvi-editmode.xml' -logFile '<结果目录>/mvi-editmode.log'
```

必须同时确认 Unity 编译日志无错误、结果 XML 包含实际测试数量且失败数为零。返回码为零或只有构建摘要不视作测试通过。UGUI、FairyGUI 默认事件派发与根视图所有权的新增测试位于 `MVI/Assets/Tests/Editor`，应由真实 Unity Test Framework 运行。

序列化持久化的默认 Json/Binary adapter 支持 Unity 可序列化字段对象；只读属性和 record 不属于该默认契约。泛型 Store 在恢复前登记声明的 State 类型到默认注册器；使用自定义注册器时需显式准备类型。配置未就绪导致的读档失败会保留快照，并阻止初始化保存覆盖该 key，直到恢复成功或显式清理。
