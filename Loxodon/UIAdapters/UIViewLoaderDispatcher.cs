using System;
using System.Collections.Concurrent;
using System.Reflection;
using Loxodon.Framework.Binding;
using Loxodon.Framework.Views;
using MVI.Composition;

namespace MVI.UIAdapters.UGUI
{
    /// <summary>
    /// Loxodon <see cref="IUIViewLocator"/> 的类型擦除桥。
    /// </summary>
    /// <remarks>
    /// 背景：<see cref="IUIViewLocator.LoadView{T}"/> 带有 <c>where T : UIView</c> 约束，
    /// 而 <see cref="IViewLoader.Load{TView}"/> 的约束是 <c>where TView : class</c>。
    /// C# 泛型约束不向下传递，无法在 <see cref="IViewLoader.Load{TView}"/> 内直接调用
    /// <see cref="IUIViewLocator.LoadView{T}"/> 的强类型版本。
    /// <para>
    /// 本类采用"静态委托缓存"模式解决该问题：每个 <see cref="Type"/> 仅在首次访问时
    /// 通过 <see cref="Delegate.CreateDelegate(Type, MethodInfo)"/> 建立一次桥接委托，
    /// 之后通过 <see cref="Func{IUIViewLocator, String, Object}"/> 直接调用。
    /// </para>
    /// <para>
    /// 关于"反射"边界的说明：这里仅在初始化阶段读取已知方法的元数据以建立委托，
    /// 不涉及 <c>Type.GetType(string)</c>、<c>Activator.CreateInstance</c> 等运行时反射实现功能的模式。
    /// 实际加载逻辑完全走静态泛型 <see cref="LoadAsUIView{TView}"/>，无运行时反射。
    /// </para>
    /// </remarks>
    internal static class UIViewLoaderDispatcher
    {
        // 类型 → 已绑定的强类型加载委托。
        private static readonly ConcurrentDictionary<Type, Func<IUIViewLocator, string, object>> LoaderCache =
            new();

        // 桥接方法的 MethodInfo 缓存：避免每次 GetMethod 反射查找。
        private static readonly MethodInfo LoadAsUIViewMethod = typeof(UIViewLoaderDispatcher)
            .GetMethod(nameof(LoadAsUIView), BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("无法定位桥接方法 LoadAsUIView，请保留该方法的签名。");

        /// <summary>
        /// 类型擦除加载入口：在已知 Type 的前提下调用 IUIViewLocator.LoadView&lt;T&gt;。
        /// </summary>
        /// <param name="locator">底层 IUIViewLocator 实例；为 null 时返回 null。</param>
        /// <param name="viewType">要加载的 View 类型；必须派生自 <see cref="UIView"/>，否则返回 null。</param>
        /// <param name="resourcePath">资源路径；为空时返回 null。</param>
        /// <returns>加载得到的 View 实例；类型不匹配或加载失败时返回 null。</returns>
        public static object Load(IUIViewLocator locator, Type viewType, string resourcePath)
        {
            if (locator == null || viewType == null || string.IsNullOrWhiteSpace(resourcePath))
            {
                return null;
            }

            if (!typeof(UIView).IsAssignableFrom(viewType))
            {
                return null;
            }

            var loader = LoaderCache.GetOrAdd(viewType, CreateLoader);
            return loader(locator, resourcePath);
        }

        // 为指定 viewType 构造一个类型擦除的加载委托。该委托内部走静态泛型 LoadAsUIView<TView>。
        private static Func<IUIViewLocator, string, object> CreateLoader(Type viewType)
        {
            // 通过 MakeGenericMethod 在已知 viewType 上实例化 LoadAsUIView<TView>。
            // 这是初始化阶段的元数据绑定，运行时不再发生任何反射。
            var boundMethod = LoadAsUIViewMethod.MakeGenericMethod(viewType);
            var del = Delegate.CreateDelegate(typeof(Func<IUIViewLocator, string, object>), boundMethod);
            return (Func<IUIViewLocator, string, object>)del;
        }

        // 强类型加载方法：每次调用都通过 JIT 编译为具体 viewType 的特化代码，无任何反射。
        private static TView LoadAsUIView<TView>(IUIViewLocator locator, string resourcePath)
            where TView : UIView
        {
            return locator.LoadView<TView>(resourcePath);
        }
    }
}
