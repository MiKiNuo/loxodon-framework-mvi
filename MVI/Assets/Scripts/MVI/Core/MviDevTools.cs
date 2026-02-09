using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using UnityEngine;

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
        [Serializable]
        private sealed class TimelineExportEnvelope
        {
            public TimelineExportEntry[] events;
        }

        [Serializable]
        private sealed class TimelineExportEntry
        {
            public long sequence;
            public string timestampLocal;
            public string kind;
            public string payloadType;
            public string note;
            public string payload;
        }

        private static readonly ConditionalWeakTable<Store, MviStoreTimeline> Timelines = new();
        private static readonly List<WeakReference<Store>> TrackedStores = new();
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
            RegisterStore(store);
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
                RemoveTrackedStore(store);
                SweepDeadStores();
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

        /// <summary>
        /// 导出指定 Store 的时间线文本，便于问题复现与离线分析。
        /// </summary>
        public static string ExportTimeline(Store store, bool includePayloadDetails = false, Func<MviTimelineEvent, bool> filter = null)
        {
            var snapshot = GetTimelineSnapshot(store);
            if (snapshot == null || snapshot.Count == 0)
            {
                return string.Empty;
            }

            var sb = new StringBuilder();
            for (var i = 0; i < snapshot.Count; i++)
            {
                var entry = snapshot[i];
                if (entry == null)
                {
                    continue;
                }

                if (filter != null && !filter(entry))
                {
                    continue;
                }

                sb.Append('#')
                    .Append(entry.Sequence)
                    .Append(' ')
                    .Append(entry.TimestampUtc.ToLocalTime().ToString("HH:mm:ss.fff"))
                    .Append(' ')
                    .Append(entry.Kind)
                    .Append(" payload=")
                    .Append(entry.Payload?.GetType().Name ?? "null")
                    .Append(" note=")
                    .Append(entry.Note ?? string.Empty)
                    .AppendLine();

                if (includePayloadDetails && entry.Payload != null)
                {
                    sb.AppendLine(entry.Payload.ToString());
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// 导出指定 Store 的时间线 JSON 文本，便于外部分析脚本处理。
        /// </summary>
        public static string ExportTimelineJson(Store store, bool includePayloadDetails = false, Func<MviTimelineEvent, bool> filter = null)
        {
            var snapshot = GetTimelineSnapshot(store);
            if (snapshot == null || snapshot.Count == 0)
            {
                return string.Empty;
            }

            var exportEntries = new List<TimelineExportEntry>(snapshot.Count);
            for (var i = 0; i < snapshot.Count; i++)
            {
                var entry = snapshot[i];
                if (entry == null)
                {
                    continue;
                }

                if (filter != null && !filter(entry))
                {
                    continue;
                }

                exportEntries.Add(new TimelineExportEntry
                {
                    sequence = entry.Sequence,
                    timestampLocal = entry.TimestampUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff"),
                    kind = entry.Kind.ToString(),
                    payloadType = entry.Payload?.GetType().FullName ?? "null",
                    note = entry.Note ?? string.Empty,
                    payload = includePayloadDetails && entry.Payload != null
                        ? entry.Payload.ToString()
                        : null
                });
            }

            var envelope = new TimelineExportEnvelope
            {
                events = exportEntries.ToArray()
            };

            return JsonUtility.ToJson(envelope, prettyPrint: true);
        }

        /// <summary>
        /// 获取当前被 DevTools 追踪到的 Store 列表（用于 Editor 可视化）。
        /// </summary>
        public static IReadOnlyList<Store> GetTrackedStoresSnapshot()
        {
            lock (SyncRoot)
            {
                SweepDeadStores();
                var stores = new List<Store>(TrackedStores.Count);
                for (var i = 0; i < TrackedStores.Count; i++)
                {
                    if (TrackedStores[i].TryGetTarget(out var store) && store != null)
                    {
                        stores.Add(store);
                    }
                }

                return stores;
            }
        }

        private static MviStoreTimeline GetOrCreateTimeline(Store store)
        {
            lock (SyncRoot)
            {
                return Timelines.GetValue(store, _ => new MviStoreTimeline());
            }
        }

        private static void RegisterStore(Store store)
        {
            lock (SyncRoot)
            {
                for (var i = 0; i < TrackedStores.Count; i++)
                {
                    if (TrackedStores[i].TryGetTarget(out var existing) && ReferenceEquals(existing, store))
                    {
                        return;
                    }
                }

                TrackedStores.Add(new WeakReference<Store>(store));
                SweepDeadStores();
            }
        }

        private static void RemoveTrackedStore(Store store)
        {
            for (var i = TrackedStores.Count - 1; i >= 0; i--)
            {
                if (!TrackedStores[i].TryGetTarget(out var existing) || ReferenceEquals(existing, store))
                {
                    TrackedStores.RemoveAt(i);
                }
            }
        }

        private static void SweepDeadStores()
        {
            for (var i = TrackedStores.Count - 1; i >= 0; i--)
            {
                if (!TrackedStores[i].TryGetTarget(out _))
                {
                    TrackedStores.RemoveAt(i);
                }
            }
        }
    }
}
