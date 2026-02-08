using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FairyGUI;
using Loxodon.Framework.Binding;
using MVI.Components;
using MVI.UIAdapters.FairyGUI;
using UnityEngine;

namespace MVI.FairyGUI
{
    // FairyGUI 的 MVI View 基类：强制要求绑定 MviViewModel，并接入 Loxodon 绑定上下文。
    public abstract class MviFairyView : MonoBehaviour, IViewBinder
    {
        [Header("FairyGUI 资源加载")]
        [SerializeField] private string[] packagePaths;
        [SerializeField] private string packageName;
        [SerializeField] private string componentName;
        [SerializeField] private bool preferUIPanel = true;
        [SerializeField] private bool addToGRoot = true;
        [SerializeField] private bool autoCreateView = true;

        // 当前绑定的 ViewModel（统一处理状态与副作用）。
        protected MviViewModel ViewModel { get; private set; }
        // FairyGUI 面板组件，负责承载 GComponent 根节点。
        protected UIPanel Panel { get; private set; }
        // FairyGUI 根组件（可能来自 UIPanel 或 GRoot）。
        protected GComponent Root => _root;

        // ViewModel 是否由 View 负责释放。
        private bool _disposeViewModel;
        // View 生命周期内的取消令牌（用于异步加载）。
        private readonly CancellationTokenSource _viewCts = new();
        // 运行时创建的根组件（非 UIPanel 模式）。
        private GComponent _root;
        // 视图是否已就绪。
        private bool _isViewReady;
        // 是否已经完成 Bind（防止重复绑定）。
        private bool _isBound;
        // 视图未就绪时缓存的状态。
        private IState _pendingState;
        // 资源包加载器（用于对接 AssetBundle / YooAsset）。
        private IFairyPackageLoader _packageLoader;
        // FairyGUI 适配器。
        private FairyViewHost _viewHost;

        // 资源路径（Resources 下的路径），可由子类覆写。
        protected virtual string[] PackagePaths => packagePaths;
        // 包名（用于 CreateObject），可由子类覆写。
        protected virtual string PackageName => packageName;
        // 组件名（用于 CreateObject），可由子类覆写。
        protected virtual string ComponentName => componentName;
        // 是否优先使用 UIPanel（Inspector 里配置的组件）。
        protected virtual bool PreferUIPanel => preferUIPanel;
        // 非 UIPanel 模式时是否自动加入到 GRoot。
        protected virtual bool AddToGRoot => addToGRoot;
        // 是否自动创建并初始化 View。
        protected virtual bool AutoCreateView => autoCreateView;
        // 自定义包加载器（默认使用 Resources 模式）。
        protected virtual IFairyPackageLoader PackageLoader => _packageLoader ??= new ResourcesPackageLoader();
        protected FairyViewHost ViewHost => _viewHost ??= CreateViewHost();

        protected virtual FairyViewHost CreateViewHost()
        {
            return new FairyViewHost(PackageLoader);
        }

        protected virtual void Awake()
        {
            // 缓存 UIPanel，避免重复 GetComponent。
            Panel = GetComponent<UIPanel>();
        }

        protected virtual void Start()
        {
            if (!AutoCreateView)
            {
                return;
            }

            // 异步加载资源包，再创建视图。
            StartCoroutine(LoadAndCreateView());
        }

        // 绑定 ViewModel（强制要求使用 MviViewModel）。
        public void BindViewModel(MviViewModel viewModel, bool disposeViewModel = true)
        {
            if (ReferenceEquals(ViewModel, viewModel))
            {
                return;
            }

            // 如果已绑定旧的 ViewModel，先解除订阅。
            DetachViewModel(disposeExisting: _disposeViewModel);

            ViewModel = viewModel;
            _disposeViewModel = disposeViewModel;

            if (ViewModel == null)
            {
                return;
            }

            // 将 Loxodon DataContext 绑定到 ViewModel，供 BindingSet 使用。
            this.SetDataContext(ViewModel);

            // ViewModel 统一发出状态/Effect/Error 事件。
            ViewModel.StateChanged += OnViewModelStateChanged;
            ViewModel.EffectEmitted += OnViewModelEffectEmitted;
            ViewModel.ErrorEmitted += OnViewModelErrorEmitted;

            // 如果 View 已就绪且尚未绑定，则执行 Bind。
            TryBind();

            // 立即同步一次当前状态，保证初始 UI 渲染。
            if (ViewModel.CurrentState != null)
            {
                OnViewModelStateChanged(ViewModel.CurrentState);
            }
        }

        // Loxodon 绑定入口（由子类实现具体绑定逻辑）。
        public abstract void Bind();

        protected virtual void OnViewReady(GComponent root)
        {
        }

        protected virtual void OnStateChanged(IState state)
        {
        }

        protected virtual void OnEffect(IMviEffect effect)
        {
        }

        protected virtual void OnError(MviErrorEffect error)
        {
        }

        // 设置自定义包加载器（例如接入 YooAsset 时注入）。
        protected void SetPackageLoader(IFairyPackageLoader loader)
        {
            _packageLoader = loader;
            _viewHost = null;
        }

        // 异步加载 FairyGUI 资源包（默认走 Resources）。
        protected virtual ValueTask LoadPackagesAsync(CancellationToken cancellationToken = default)
        {
            var paths = PackagePaths;
            if (paths == null || paths.Length == 0)
            {
                return default;
            }

            return ViewHost.LoadPackagesAsync(paths, cancellationToken);
        }

        private IEnumerator LoadAndCreateView()
        {
            var task = LoadPackagesAsync(_viewCts.Token).AsTask();
            while (!task.IsCompleted)
            {
                yield return null;
            }

            if (task.IsFaulted)
            {
                Debug.LogException(task.Exception);
                yield break;
            }

            if (_viewCts.IsCancellationRequested)
            {
                yield break;
            }

            // 创建或获取根组件（UIPanel 或 GRoot）。
            var root = EnsureRoot();
            if (root != null)
            {
                // View 已准备就绪，交由子类绑定控件与事件。
                OnViewReady(root);
                _isViewReady = true;
                // View 已准备好后尝试建立绑定。
                TryBind();
                if (_pendingState != null)
                {
                    OnStateChanged(_pendingState);
                    _pendingState = null;
                }
            }
        }

        // 创建或获取 FairyGUI 根组件。
        protected virtual GComponent EnsureRoot()
        {
            if (PreferUIPanel && Panel != null)
            {
                // UIPanel 模式：依赖 UIPanel 来创建 UI 组件树。
                ConfigurePanel();
                _root = Panel.ui;
                return _root;
            }

            if (_root != null)
            {
                return _root;
            }

            if (string.IsNullOrWhiteSpace(PackageName) || string.IsNullOrWhiteSpace(ComponentName))
            {
                Debug.LogWarning($"{nameof(MviFairyView)} 未配置 PackageName/ComponentName，无法创建视图。");
                return null;
            }

            // GRoot 模式：由 ViewHost 创建并按配置挂载到舞台。
            _root = ViewHost.Load(typeof(GComponent), $"{PackageName}/{ComponentName}") as GComponent;
            if (_root != null && AddToGRoot)
            {
                ViewHost.Attach(_root, null);
            }

            return _root;
        }

        // 同步 Inspector 配置到 UIPanel（如果已有配置则保持不动）。
        private void ConfigurePanel()
        {
            if (Panel == null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(PackageName))
            {
                Panel.packageName = PackageName;
            }

            if (!string.IsNullOrWhiteSpace(ComponentName))
            {
                Panel.componentName = ComponentName;
            }
        }

        protected virtual void OnDestroy()
        {
            // 解除 ViewModel 订阅，必要时释放 ViewModel。
            DetachViewModel(disposeExisting: _disposeViewModel);

            // 取消异步加载。
            if (!_viewCts.IsCancellationRequested)
            {
                _viewCts.Cancel();
            }
            _viewCts.Dispose();

            // 非 UIPanel 模式下，主动移除并销毁根组件。
            if (Panel == null && _root != null)
            {
                ViewHost.Destroy(_root);
                _root = null;
            }

            _isViewReady = false;
            _pendingState = null;
            _isBound = false;
        }

        // 尝试触发 Bind：View 就绪且 ViewModel 已绑定时才执行一次。
        private void TryBind()
        {
            if (_isBound || !_isViewReady || ViewModel == null)
            {
                return;
            }

            _isBound = true;
            Bind();
        }

        // 解除旧的 ViewModel 订阅并回收。
        private void DetachViewModel(bool disposeExisting)
        {
            if (ViewModel == null)
            {
                return;
            }

            ViewModel.StateChanged -= OnViewModelStateChanged;
            ViewModel.EffectEmitted -= OnViewModelEffectEmitted;
            ViewModel.ErrorEmitted -= OnViewModelErrorEmitted;

            // 清理 DataContext，避免悬挂引用。
            this.SetDataContext(null);

            if (disposeExisting)
            {
                ViewModel.Dispose();
            }

            ViewModel = null;
        }

        private void OnViewModelStateChanged(IState state)
        {
            if (state == null)
            {
                return;
            }
            if (!_isViewReady)
            {
                _pendingState = state;
                return;
            }

            OnStateChanged(state);
        }

        private void OnViewModelEffectEmitted(IMviEffect effect)
        {
            if (effect == null)
            {
                return;
            }

            OnEffect(effect);
        }

        private void OnViewModelErrorEmitted(MviErrorEffect error)
        {
            if (error == null)
            {
                return;
            }

            OnError(error);
        }

        // 默认的 Resources 包加载器（与 FairyGUI Demo 一致）。
        private sealed class ResourcesPackageLoader : IFairyPackageLoader
        {
            public ValueTask LoadAsync(IReadOnlyList<string> packagePaths, CancellationToken cancellationToken = default)
            {
                if (packagePaths == null)
                {
                    return default;
                }

                foreach (var path in packagePaths)
                {
                    if (string.IsNullOrWhiteSpace(path))
                    {
                        continue;
                    }

                    // FairyGUI 会内部处理重复加载的情况。
                    UIPackage.AddPackage(path);
                }

                return default;
            }
        }
    }

    public abstract class MviFairyView<TState, TIntent, TResult> : MviFairyView
        where TState : class, IState
        where TIntent : IIntent
        where TResult : IMviResult
    {
        // 强类型 ViewModel，便于子类直接使用。
        protected new MviViewModel<TState, TIntent, TResult> ViewModel { get; private set; }

        public void BindViewModel(MviViewModel<TState, TIntent, TResult> viewModel, bool disposeViewModel = true)
        {
            ViewModel = viewModel;
            base.BindViewModel(viewModel, disposeViewModel);
        }

        // 将基类的 IState 分发到强类型回调。
        protected sealed override void OnStateChanged(IState state)
        {
            if (state is TState typed)
            {
                OnStateChanged(typed);
            }
        }

        protected virtual void OnStateChanged(TState state)
        {
        }
    }

    public abstract class MviFairyView<TState, TIntent, TResult, TEffect> : MviFairyView<TState, TIntent, TResult>
        where TState : class, IState
        where TIntent : IIntent
        where TResult : IMviResult
        where TEffect : class, IMviEffect
    {
        // 强类型 Effect ViewModel。
        protected new MviViewModel<TState, TIntent, TResult, TEffect> ViewModel { get; private set; }

        public void BindViewModel(MviViewModel<TState, TIntent, TResult, TEffect> viewModel, bool disposeViewModel = true)
        {
            ViewModel = viewModel;
            base.BindViewModel(viewModel, disposeViewModel);
        }

        // 将基类的 IMviEffect 分发到强类型回调。
        protected sealed override void OnEffect(IMviEffect effect)
        {
            if (effect is TEffect typed)
            {
                OnEffect(typed);
            }
        }

        protected virtual void OnEffect(TEffect effect)
        {
        }
    }
}
