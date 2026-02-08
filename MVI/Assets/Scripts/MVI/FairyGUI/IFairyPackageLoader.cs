using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MVI.FairyGUI
{
    // FairyGUI 资源包加载器接口（用于对接 AssetBundle / YooAsset 等资源系统）。
    public interface IFairyPackageLoader
    {
        // 异步加载 FairyGUI 资源包。
        ValueTask LoadAsync(IReadOnlyList<string> packagePaths, CancellationToken cancellationToken = default);
    }
}
