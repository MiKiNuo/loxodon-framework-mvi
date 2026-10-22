using System;
using System.Collections.Generic;

namespace MVI
{
    // Selector 工具：支持记忆化，减少重复计算与无效 UI 刷新。
    public static class MviSelector
    {
        public static Func<TState, TSelected> Memoize<TState, TSelected>(
            Func<TState, TSelected> selector,
            IEqualityComparer<TState> stateComparer = null,
            IEqualityComparer<TSelected> selectedComparer = null)
        {
            if (selector == null)
            {
                throw new ArgumentNullException(nameof(selector));
            }

            stateComparer ??= EqualityComparer<TState>.Default;
            selectedComparer ??= EqualityComparer<TSelected>.Default;

            var initialized = false;
            var lastState = default(TState);
            var lastSelected = default(TSelected);

            return state =>
            {
                if (initialized && stateComparer.Equals(lastState, state))
                {
                    return lastSelected;
                }

                var selected = selector(state);
                if (initialized && selectedComparer.Equals(lastSelected, selected))
                {
                    lastState = state;
                    return lastSelected;
                }

                initialized = true;
                lastState = state;
                lastSelected = selected;
                return selected;
            };
        }
    }
}
