using System;
using System.Collections.Concurrent;

namespace MVI
{
    /// <summary>
    /// State → ViewModel 映射器：仅使用源生成器通过 <see cref="RegisterMapper"/> 注入的映射函数，缺失时返回 false。
    /// 运行时直接调用已登记的委托，不扫描程序集。
    /// </summary>
    public static class MviStateMapper
    {
        // 已注册的源生成器映射函数（同进程内允许热替换，例如 Editor 重载）。
        private static readonly ConcurrentDictionary<string, Func<IState, MviViewModel, bool>> RegisteredMappers =
            new(StringComparer.Ordinal);

        public static bool TryMap(IState state, MviViewModel viewModel)
        {
            if (state is null || viewModel is null)
            {
                return false;
            }

            foreach (var mapper in RegisteredMappers.Values)
            {
                if (mapper(state, viewModel))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 由源生成器在 Unity 启动或 Editor 加载时调用，避免运行时扫描所有程序集。
        /// </summary>
        public static void RegisterMapper(Func<IState, MviViewModel, bool> mapper)
        {
            if (mapper == null)
            {
                return;
            }

            RegisteredMappers[mapper.Method.DeclaringType?.AssemblyQualifiedName ?? mapper.Method.Name] = mapper;
        }
    }
}
