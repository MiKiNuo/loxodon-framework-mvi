using System;
using System.Collections.Concurrent;
using System.Reflection;

namespace MVI
{
    /// <summary>
    /// State → ViewModel 映射器：优先使用源生成器生成的映射函数，缺失时返回 false。
    /// 不再提供反射回退实现，源生成器需保证所有 MVI 场景都有匹配的映射代码。
    /// </summary>
    internal static class MviStateMapper
    {
        // 由源生成器在编译期注入：MVI.Generated.GeneratedStateMapper.TryMap(state, viewModel)。
        // 若源生成器未启用或未覆盖目标类型，GeneratedMapper 为 null，TryMap 直接返回 false。
        private static readonly Func<IState, MviViewModel, bool> GeneratedMapper = FindGeneratedMapper();

        // 已注册的源生成器映射函数（同进程内允许热替换，例如 Editor 重载）。
        private static readonly ConcurrentDictionary<string, Func<IState, MviViewModel, bool>> RegisteredMappers =
            new(StringComparer.Ordinal);

        public static bool TryMap(IState state, MviViewModel viewModel)
        {
            if (state is null || viewModel is null)
            {
                return false;
            }

            var mapper = GeneratedMapper;
            return mapper != null && mapper(state, viewModel);
        }

        /// <summary>
        /// 由源生成器在模块初始化时调用，避免运行时反射扫描所有程序集。
        /// </summary>
        public static void RegisterMapper(Func<IState, MviViewModel, bool> mapper)
        {
            if (mapper == null)
            {
                return;
            }

            RegisteredMappers[mapper.Method.DeclaringType?.FullName ?? mapper.Method.Name] = mapper;
        }

        private static Func<IState, MviViewModel, bool> FindGeneratedMapper()
        {
            const string mapperTypeName = "MVI.Generated.GeneratedStateMapper";
            const string methodName = "TryMap";

            // 优先查找已注册的映射函数（源生成器通过 RegisterMapper 注入，避免 AppDomain 反射扫描）。
            foreach (var pair in RegisteredMappers)
            {
                if (pair.Value != null)
                {
                    return pair.Value;
                }
            }

            // 兜底：通过反射仅在 MVI.Generated 程序集内定位源生成器产物。
            // 这是为了兼容老版本源生成器（未调用 RegisterMapper），新源生成器应直接走 RegisterMapper 路径。
            try
            {
                var generatedType = Type.GetType($"{mapperTypeName}, MVI.Generated");
                if (generatedType == null)
                {
                    return null;
                }

                var method = generatedType.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
                if (method == null)
                {
                    return null;
                }

                return (Func<IState, MviViewModel, bool>)Delegate.CreateDelegate(
                    typeof(Func<IState, MviViewModel, bool>), method);
            }
            catch
            {
                return null;
            }
        }
    }
}
