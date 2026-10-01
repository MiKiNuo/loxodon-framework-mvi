using System;
using System.Collections.Generic;

namespace MVI
{
    /// <summary>
    /// 状态类型解析器契约：通过显式注册建立 State 类型与 AssemblyQualifiedName 的映射，
    /// 序列化器反序列化时使用该契约查找具体类型，避免反射遍历 <see cref="AppDomain"/>。
    /// </summary>
    /// <remarks>
    /// 业务侧通常在启动阶段（模块装配、源生成器或 DI 引导）调用 <see cref="Register"/> 注册所有可被持久化的 <see cref="IState"/> 实现。
    /// 映射仅保存在当前进程，不随快照保存；新进程首次加载前应重新登记。
    /// 自定义注册器需显式注入序列化器，不能依赖全局 Shared 的登记。
    /// 注册器内部以线程安全字典保存映射，反序列化时只走字典查询，不再触发任何反射调用。
    /// </remarks>
    public interface IStateTypeRegistry
    {
        /// <summary>
        /// 注册一个 State 类型：同时按 <see cref="Type.AssemblyQualifiedName"/> 与 <see cref="Type.FullName"/> 建立别名。
        /// </summary>
        void Register(Type stateType);

        /// <summary>
        /// 尝试解析已注册的类型别名。
        /// </summary>
        bool TryResolve(string name, out Type stateType);

        /// <summary>
        /// 已注册类型数量（用于诊断与测试）。
        /// </summary>
        int Count { get; }
    }

    /// <summary>
    /// 默认 <see cref="IStateTypeRegistry"/> 实现：使用锁保护的字典存储映射。
    /// </summary>
    public sealed class StateTypeRegistry : IStateTypeRegistry
    {
        private readonly Dictionary<string, Type> _byQualifiedName = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Type> _bySimpleName = new(StringComparer.Ordinal);
        private readonly object _syncRoot = new();

        /// <summary>
        /// 全局共享注册器实例。框架默认使用此实例；测试或独立模块可自定义 <see cref="IStateTypeRegistry"/> 注入。
        /// </summary>
        public static StateTypeRegistry Shared { get; } = new();

        /// <summary>
        /// 创建一个空的注册器，调用方需自行注册类型。
        /// </summary>
        public StateTypeRegistry()
        {
        }

        /// <inheritdoc />
        public int Count
        {
            get
            {
                lock (_syncRoot)
                {
                    return _byQualifiedName.Count;
                }
            }
        }

        /// <inheritdoc />
        public void Register(Type stateType)
        {
            if (stateType == null)
            {
                throw new ArgumentNullException(nameof(stateType));
            }

            var qualifiedName = stateType.AssemblyQualifiedName;
            if (string.IsNullOrWhiteSpace(qualifiedName))
            {
                throw new ArgumentException(
                    $"Type '{stateType.FullName}' has no AssemblyQualifiedName and cannot be registered.",
                    nameof(stateType));
            }

            var simpleName = stateType.FullName;
            lock (_syncRoot)
            {
                _byQualifiedName[qualifiedName] = stateType;
                if (!string.IsNullOrWhiteSpace(simpleName))
                {
                    _bySimpleName[simpleName] = stateType;
                }
            }
        }

        /// <inheritdoc />
        public bool TryResolve(string name, out Type stateType)
        {
            stateType = null;
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            lock (_syncRoot)
            {
                if (_byQualifiedName.TryGetValue(name, out stateType))
                {
                    return stateType != null;
                }

                if (_bySimpleName.TryGetValue(name, out stateType))
                {
                    return stateType != null;
                }
            }

            return false;
        }

        /// <summary>
        /// 清空所有已注册映射（仅供测试或热重载场景使用）。
        /// </summary>
        public void Clear()
        {
            lock (_syncRoot)
            {
                _byQualifiedName.Clear();
                _bySimpleName.Clear();
            }
        }
    }
}
