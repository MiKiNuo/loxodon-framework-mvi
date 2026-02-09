# 快速开始

## 1）通过 Git URL 安装

在 `Packages/manifest.json` 中添加依赖：

```json
{
  "dependencies": {
    "com.mikinuo.loxodon-framework-mvi": "https://github.com/<your-org>/<your-repo>.git?path=/MVI/Assets/Scripts/MVI"
  }
}
```

## 2）推荐最小初始化

1. 仅在开发环境开启诊断与 DevTools。
2. 通过 `MviStoreOptions.DefaultProfile` 统一注册全局默认配置。
3. 在同一入口集中配置中间件与错误策略。

示例：

```csharp
using System;
using MVI;

public static class MviBootstrap
{
    public static void Install()
    {
        MviStoreOptions.DefaultProfile = new StoreProfile
        {
            ProcessingMode = AwaitOperation.Switch,
            StateHistoryCapacity = 64,
            DevToolsEnabled = false,
            DevToolsMaxEventsPerStore = 1000
        }
        .UseRateLimit(limit: 20, window: TimeSpan.FromSeconds(1))
        .UseCircuitBreaker(failureThreshold: 3, openDuration: TimeSpan.FromSeconds(5));

        MviStoreOptions.DefaultErrorStrategy = new TemplateMviErrorStrategyBuilder()
            .ForException<TimeoutException>(_ => MviErrorDecision.Retry(2, TimeSpan.FromMilliseconds(200)))
            .Build();
    }
}
```

## 3）说明

- 本包不包含 Demo/Sample。
- 如果需要完整示例场景与业务接入示例，请直接克隆仓库查看。
