using System;

namespace MVI
{
    /// <summary>
    /// 状态持久化职责协作器：把 Store 中关于 "何时 Save / 何时 Restore / 如何迁移" 的逻辑剥离出来。
    /// 通过委托注入依赖，便于在测试或独立模块中替换为内存实现，且不破坏 Store 公开 API。
    /// </summary>
    /// <remarks>
    /// 该协作器只负责"按当前 key 调底层 persistence"以及"调用迁移钩子"，不感知 State 历史与回放，
    /// 也不会主动修改 Store 内部状态；Store 仍然负责把读到的 <see cref="IState"/> 装入自己的历史与流。
    /// </remarks>
    public sealed class StorePersistenceCoordinator
    {
        private readonly Func<IStoreStatePersistence> _persistenceProvider;
        private readonly Func<string> _keyProvider;
        private readonly Func<IState, IState> _migrator;
        private readonly Action<Exception, MviErrorPhase> _errorHandler;

        /// <summary>
        /// 构造持久化协作器：所有依赖通过委托懒加载，避免 Store 字段读取顺序耦合。
        /// </summary>
        public StorePersistenceCoordinator(
            Func<IStoreStatePersistence> persistenceProvider,
            Func<string> keyProvider,
            Func<IState, IState> migrator,
            Action<Exception, MviErrorPhase> errorHandler)
        {
            _persistenceProvider = persistenceProvider ?? throw new ArgumentNullException(nameof(persistenceProvider));
            _keyProvider = keyProvider ?? throw new ArgumentNullException(nameof(keyProvider));
            _migrator = migrator ?? (_ => _);
            _errorHandler = errorHandler;
        }

        /// <summary>
        /// 尝试从持久化层加载状态并应用迁移钩子。
        /// </summary>
        /// <returns>成功加载且迁移后非空则返回 <c>true</c>，否则 <c>false</c>。</returns>
        public bool TryRestore(out IState state)
        {
            state = null;
            IStoreStatePersistence persistence;
            string key;
            try
            {
                persistence = _persistenceProvider();
                key = _keyProvider();
            }
            catch (Exception ex)
            {
                _errorHandler?.Invoke(ex, MviErrorPhase.PersistenceLoad);
                return false;
            }

            if (persistence == null || string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            try
            {
                if (!persistence.TryLoad(key, out var persisted) || persisted == null)
                {
                    return false;
                }

                var migrated = _migrator(persisted);
                if (migrated == null)
                {
                    return false;
                }

                state = migrated;
                return true;
            }
            catch (Exception ex)
            {
                _errorHandler?.Invoke(ex, MviErrorPhase.PersistenceLoad);
                return false;
            }
        }

        /// <summary>
        /// 将状态写入持久化层；写入失败会通过 <see cref="Action{Exception, MviErrorPhase}"/> 上报。
        /// </summary>
        public void Save(IState state)
        {
            if (state == null)
            {
                return;
            }

            IStoreStatePersistence persistence;
            string key;
            try
            {
                persistence = _persistenceProvider();
                key = _keyProvider();
            }
            catch (Exception ex)
            {
                _errorHandler?.Invoke(ex, MviErrorPhase.PersistenceSave);
                return;
            }

            if (persistence == null || string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            try
            {
                persistence.Save(key, state);
            }
            catch (Exception ex)
            {
                _errorHandler?.Invoke(ex, MviErrorPhase.PersistenceSave);
            }
        }
    }
}
