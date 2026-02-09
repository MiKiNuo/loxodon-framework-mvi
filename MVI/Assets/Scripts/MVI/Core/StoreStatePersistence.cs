using System;
using System.Collections.Generic;

namespace MVI
{
    // Store 状态持久化接口（可接入本地存档、加密存储、云端同步等）。
    public interface IStoreStatePersistence
    {
        bool TryLoad(string key, out IState state);

        void Save(string key, IState state);

        void Clear(string key);
    }

    // 全局 Store 运行时选项。
    public static class MviStoreOptions
    {
        public static IStoreStatePersistence DefaultStatePersistence { get; set; }

        public static IMviErrorStrategy DefaultErrorStrategy { get; set; }

        public static StoreProfile DefaultProfile { get; set; }
    }

    // 默认的内存持久化实现（适用于测试与轻量场景）。
    public sealed class InMemoryStoreStatePersistence : IStoreStatePersistence
    {
        private readonly Dictionary<string, IState> _states = new(StringComparer.Ordinal);

        public bool TryLoad(string key, out IState state)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                state = null;
                return false;
            }

            return _states.TryGetValue(key, out state);
        }

        public void Save(string key, IState state)
        {
            if (string.IsNullOrWhiteSpace(key) || state == null)
            {
                return;
            }

            _states[key] = state;
        }

        public void Clear(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            _states.Remove(key);
        }
    }
}
