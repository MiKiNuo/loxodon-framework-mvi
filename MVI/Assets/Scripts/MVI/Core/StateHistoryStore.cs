using System.Collections.Generic;

namespace MVI
{
    /// <summary>
    /// 状态历史职责协作器：管理状态栈、撤销/重做/时间旅行游标。
    /// 该类把 Store 中的状态历史相关字段与逻辑剥离出来，使 Store 主体专注于 Intent/State 流。
    /// </summary>
    /// <remarks>
    /// 容量 &lt;= 0 表示关闭历史：所有状态变更会同步清空历史并把游标重置为 -1。
    /// 时间旅行 / 撤销 / 重做均会同步 <see cref="CurrentIndex"/>，使外部观察者保持一致视图。
    /// </remarks>
    public sealed class StateHistoryStore
    {
        private readonly List<IState> _entries = new();
        private int _cursor = -1;

        /// <summary>
        /// 历史栈容量；&lt;=0 表示关闭历史。修改容量会立刻截断超出部分。
        /// </summary>
        public int Capacity { get; set; }

        /// <summary>
        /// 已记录状态数量。
        /// </summary>
        public int Count => _entries.Count;

        /// <summary>
        /// 当前游标（-1 表示无历史）。
        /// </summary>
        public int CurrentIndex => _cursor;

        /// <summary>
        /// 是否可以撤销。
        /// </summary>
        public bool CanUndo => _cursor > 0;

        /// <summary>
        /// 是否可以重做。
        /// </summary>
        public bool CanRedo => _cursor >= 0 && _cursor < _entries.Count - 1;

        /// <summary>
        /// 记录一条状态：超过容量时丢弃最旧的；处于历史中间位置时先裁掉后续分支。
        /// </summary>
        public void Record(IState state)
        {
            if (Capacity <= 0)
            {
                _entries.Clear();
                _cursor = -1;
                return;
            }

            if (state == null)
            {
                return;
            }

            if (_cursor >= 0 && _cursor < _entries.Count - 1)
            {
                _entries.RemoveRange(_cursor + 1, _entries.Count - _cursor - 1);
            }

            _entries.Add(state);
            if (_entries.Count > Capacity)
            {
                _entries.RemoveRange(0, _entries.Count - Capacity);
            }

            _cursor = _entries.Count - 1;
        }

        /// <summary>
        /// 移动游标到指定索引，返回该位置的状态；索引越界返回 false。
        /// </summary>
        public bool TryGetAt(int index, out IState state)
        {
            state = null;
            if (index < 0 || index >= _entries.Count)
            {
                return false;
            }

            _cursor = index;
            state = _entries[index];
            return true;
        }

        /// <summary>
        /// 撤销：将游标前移一位并返回当前状态。
        /// </summary>
        public bool Undo(out IState state)
        {
            if (!CanUndo)
            {
                state = null;
                return false;
            }

            return TryGetAt(_cursor - 1, out state);
        }

        /// <summary>
        /// 重做：将游标后移一位并返回当前状态。
        /// </summary>
        public bool Redo(out IState state)
        {
            if (!CanRedo)
            {
                state = null;
                return false;
            }

            return TryGetAt(_cursor + 1, out state);
        }

        /// <summary>
        /// 根据值匹配定位状态（从最新往旧找）。找不到时保持游标不变。
        /// </summary>
        public bool TryLocate(IState state)
        {
            if (state == null || _entries.Count == 0)
            {
                return false;
            }

            for (var i = _entries.Count - 1; i >= 0; i--)
            {
                var candidate = _entries[i];
                if (ReferenceEquals(candidate, state) || candidate.Equals(state))
                {
                    _cursor = i;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 清空历史与游标。
        /// </summary>
        public void Clear()
        {
            _entries.Clear();
            _cursor = -1;
        }
    }
}
