using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace MVI
{
    public enum MviTimelineEventKind
    {
        Intent = 0,
        Result = 1,
        State = 2,
        Effect = 3,
        Error = 4,
        Replay = 5,
        Undo = 6,
        Redo = 7,
        TimeTravel = 8
    }

    public sealed class MviTimelineEvent
    {
        public MviTimelineEvent(long sequence, DateTime timestampUtc, MviTimelineEventKind kind, object payload, string note)
        {
            Sequence = sequence;
            TimestampUtc = timestampUtc;
            Kind = kind;
            Payload = payload;
            Note = note;
        }

        public long Sequence { get; }

        public DateTime TimestampUtc { get; }

        public MviTimelineEventKind Kind { get; }

        public object Payload { get; }

        public string Note { get; }
    }

    public sealed class MviStoreTimeline
    {
        private readonly List<MviTimelineEvent> _events = new();
        private readonly object _syncRoot = new();
        private long _sequence;

        internal void Add(MviTimelineEventKind kind, object payload, string note, int maxEvents)
        {
            lock (_syncRoot)
            {
                _sequence++;
                _events.Add(new MviTimelineEvent(_sequence, DateTime.UtcNow, kind, payload, note));
                if (maxEvents > 0 && _events.Count > maxEvents)
                {
                    _events.RemoveRange(0, _events.Count - maxEvents);
                }
            }
        }

        public IReadOnlyList<MviTimelineEvent> Snapshot()
        {
            lock (_syncRoot)
            {
                return _events.ToArray();
            }
        }

        public void Clear()
        {
            lock (_syncRoot)
            {
                _events.Clear();
            }
        }
    }

    public static class MviDevTools
    {
        private static readonly ConditionalWeakTable<Store, MviStoreTimeline> Timelines = new();
        private static readonly object SyncRoot = new();

        // 启用后记录 Store 时间线（Intent/Result/State/Effect/Error）。
        public static bool Enabled { get; set; }

        // 单个 Store 最多保留多少条事件（<=0 表示不限制）。
        public static int MaxEventsPerStore { get; set; } = 1000;

        internal static void Track(Store store, MviTimelineEventKind kind, object payload = null, string note = null)
        {
            if (!Enabled || store == null)
            {
                return;
            }

            var timeline = GetOrCreateTimeline(store);
            timeline.Add(kind, payload, note, MaxEventsPerStore);
        }

        internal static void Detach(Store store)
        {
            if (store == null)
            {
                return;
            }

            lock (SyncRoot)
            {
                Timelines.Remove(store);
            }
        }

        public static bool TryGetTimeline(Store store, out MviStoreTimeline timeline)
        {
            if (store == null)
            {
                timeline = null;
                return false;
            }

            lock (SyncRoot)
            {
                return Timelines.TryGetValue(store, out timeline);
            }
        }

        public static IReadOnlyList<MviTimelineEvent> GetTimelineSnapshot(Store store)
        {
            if (store == null)
            {
                return Array.Empty<MviTimelineEvent>();
            }

            return TryGetTimeline(store, out var timeline)
                ? timeline.Snapshot()
                : Array.Empty<MviTimelineEvent>();
        }

        public static void Clear(Store store)
        {
            if (TryGetTimeline(store, out var timeline))
            {
                timeline.Clear();
            }
        }

        private static MviStoreTimeline GetOrCreateTimeline(Store store)
        {
            lock (SyncRoot)
            {
                return Timelines.GetValue(store, _ => new MviStoreTimeline());
            }
        }
    }
}
