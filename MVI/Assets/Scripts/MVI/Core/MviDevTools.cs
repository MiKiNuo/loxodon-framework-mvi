using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using UnityEngine;
using Random = System.Random;

namespace MVI
{
    [Serializable]
    public sealed class MviDevToolsSamplingOptions
    {
        public double SampleRate { get; set; } = 1d;

        public HashSet<MviTimelineEventKind> IncludedKinds { get; set; } = new();

        public HashSet<string> ExcludedStoreTypeFullNames { get; set; } = new(StringComparer.Ordinal);

        public MviDevToolsSamplingOptions Clone()
        {
            return new MviDevToolsSamplingOptions
            {
                SampleRate = SampleRate,
                IncludedKinds = IncludedKinds != null
                    ? new HashSet<MviTimelineEventKind>(IncludedKinds)
                    : new HashSet<MviTimelineEventKind>(),
                ExcludedStoreTypeFullNames = ExcludedStoreTypeFullNames != null
                    ? new HashSet<string>(ExcludedStoreTypeFullNames, StringComparer.Ordinal)
                    : new HashSet<string>(StringComparer.Ordinal)
            };
        }

        public string[] GetIncludedKindsDisplay()
        {
            if (IncludedKinds == null || IncludedKinds.Count == 0)
            {
                return Array.Empty<string>();
            }

            var values = new List<string>(IncludedKinds.Count);
            foreach (var kind in IncludedKinds)
            {
                values.Add(kind.ToString());
            }

            values.Sort(StringComparer.Ordinal);
            return values.ToArray();
        }

        public string[] GetExcludedStoreTypeDisplay()
        {
            if (ExcludedStoreTypeFullNames == null || ExcludedStoreTypeFullNames.Count == 0)
            {
                return Array.Empty<string>();
            }

            var values = new List<string>(ExcludedStoreTypeFullNames);
            values.Sort(StringComparer.Ordinal);
            return values.ToArray();
        }
    }

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
        TimeTravel = 8,
        Middleware = 9
    }

    [Serializable]
    public sealed class MviMiddlewareTraceEvent
    {
        public MviMiddlewareTraceEvent(
            string correlationId,
            int attempt,
            StoreMiddlewareStage stage,
            string middlewareType,
            string message = null,
            string exceptionType = null,
            string exceptionMessage = null)
        {
            CorrelationId = correlationId ?? string.Empty;
            Attempt = attempt < 0 ? 0 : attempt;
            Stage = stage;
            MiddlewareType = middlewareType ?? string.Empty;
            Message = message ?? string.Empty;
            ExceptionType = exceptionType ?? string.Empty;
            ExceptionMessage = exceptionMessage ?? string.Empty;
        }

        public string CorrelationId { get; }

        public int Attempt { get; }

        public StoreMiddlewareStage Stage { get; }

        public string MiddlewareType { get; }

        public string Message { get; }

        public string ExceptionType { get; }

        public string ExceptionMessage { get; }
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

    /// <summary>
    /// 时间线统计快照。
    /// </summary>
    public sealed class MviTimelineStats
    {
        private readonly Dictionary<MviTimelineEventKind, int> _countsByKind;

        internal MviTimelineStats(
            int totalCount,
            DateTime? firstTimestampUtc,
            DateTime? lastTimestampUtc,
            Dictionary<MviTimelineEventKind, int> countsByKind)
        {
            TotalCount = totalCount < 0 ? 0 : totalCount;
            FirstTimestampUtc = firstTimestampUtc;
            LastTimestampUtc = lastTimestampUtc;
            _countsByKind = countsByKind ?? new Dictionary<MviTimelineEventKind, int>();
        }

        public int TotalCount { get; }

        public DateTime? FirstTimestampUtc { get; }

        public DateTime? LastTimestampUtc { get; }

        public IReadOnlyDictionary<MviTimelineEventKind, int> CountsByKind => _countsByKind;

        public int GetCount(MviTimelineEventKind kind)
        {
            return _countsByKind.TryGetValue(kind, out var count) ? count : 0;
        }
    }

    public static class MviDevTools
    {
        [Serializable]
        private sealed class TimelineExportEnvelope
        {
            public TimelineSamplingExport sampling;
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

        [Serializable]
        private sealed class TimelineSamplingExport
        {
            public bool enabled;
            public double sampleRate;
            public string[] includedKinds;
            public string[] excludedStoreTypes;
        }

        private static readonly ConditionalWeakTable<Store, MviStoreTimeline> Timelines = new();
        private static readonly List<WeakReference<Store>> TrackedStores = new();
        private static readonly object SyncRoot = new();
        private static readonly object SamplingRandomSyncRoot = new();
        private static readonly Random SamplingRandom = new();
        private static MviDevToolsSamplingOptions _samplingOptions = new();

        // 启用后记录 Store 时间线（Intent/Result/State/Effect/Error）。
        public static bool Enabled { get; set; }

        // 单个 Store 最多保留多少条事件（<=0 表示不限制）。
        public static int MaxEventsPerStore { get; set; } = 1000;

        // 时间线采样配置。
        public static MviDevToolsSamplingOptions SamplingOptions
        {
            get
            {
                lock (SyncRoot)
                {
                    return (_samplingOptions ?? new MviDevToolsSamplingOptions()).Clone();
                }
            }
            set
            {
                lock (SyncRoot)
                {
                    _samplingOptions = (value ?? new MviDevToolsSamplingOptions()).Clone();
                }
            }
        }

        internal static void Track(Store store, MviTimelineEventKind kind, object payload = null, string note = null)
        {
            if (!Enabled || store == null)
            {
                return;
            }

            if (!ShouldTrackWithSampling(store, kind))
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
        /// 获取指定 Store 某条中间件关联链路（CorrelationId）的时间线事件。
        /// </summary>
        /// <param name="store">目标 Store。</param>
        /// <param name="correlationId">链路 ID；为空时默认取最近一条链路。</param>
        public static IReadOnlyList<MviTimelineEvent> GetMiddlewareTraceTimeline(Store store, string correlationId = null)
        {
            var snapshot = GetTimelineSnapshot(store);
            if (snapshot == null || snapshot.Count == 0)
            {
                return Array.Empty<MviTimelineEvent>();
            }

            var targetCorrelationId = string.IsNullOrWhiteSpace(correlationId)
                ? ResolveLatestMiddlewareCorrelationId(snapshot)
                : correlationId.Trim();
            if (string.IsNullOrWhiteSpace(targetCorrelationId))
            {
                return Array.Empty<MviTimelineEvent>();
            }

            var result = new List<MviTimelineEvent>();
            for (var i = 0; i < snapshot.Count; i++)
            {
                var entry = snapshot[i];
                if (entry == null || entry.Kind != MviTimelineEventKind.Middleware)
                {
                    continue;
                }

                if (entry.Payload is not MviMiddlewareTraceEvent trace)
                {
                    continue;
                }

                if (!string.Equals(trace.CorrelationId, targetCorrelationId, StringComparison.Ordinal))
                {
                    continue;
                }

                result.Add(entry);
            }

            return result.ToArray();
        }

        /// <summary>
        /// 导出指定 Store 某条中间件链路的可读文本（默认导出最近一条链路）。
        /// </summary>
        public static string ExportMiddlewareTrace(Store store, string correlationId = null, bool includeExceptionDetails = true)
        {
            var traceTimeline = GetMiddlewareTraceTimeline(store, correlationId);
            if (traceTimeline == null || traceTimeline.Count == 0)
            {
                return string.Empty;
            }

            var resolvedCorrelationId = traceTimeline[0].Payload is MviMiddlewareTraceEvent first
                ? first.CorrelationId
                : correlationId ?? string.Empty;
            var sb = new StringBuilder();
            sb.Append("[middleware-trace] cid=")
                .Append(resolvedCorrelationId)
                .Append(", count=")
                .Append(traceTimeline.Count)
                .AppendLine();

            for (var i = 0; i < traceTimeline.Count; i++)
            {
                var entry = traceTimeline[i];
                if (entry?.Payload is not MviMiddlewareTraceEvent trace)
                {
                    continue;
                }

                sb.Append('#')
                    .Append(entry.Sequence)
                    .Append(' ')
                    .Append(entry.TimestampUtc.ToLocalTime().ToString("HH:mm:ss.fff"))
                    .Append(" stage=")
                    .Append(trace.Stage)
                    .Append(" attempt=")
                    .Append(trace.Attempt)
                    .Append(" middleware=")
                    .Append(string.IsNullOrWhiteSpace(trace.MiddlewareType) ? "-" : trace.MiddlewareType)
                    .Append(" message=")
                    .Append(trace.Message ?? string.Empty);

                if (includeExceptionDetails && !string.IsNullOrWhiteSpace(trace.ExceptionType))
                {
                    sb.Append(" exception=")
                        .Append(trace.ExceptionType)
                        .Append(':')
                        .Append(trace.ExceptionMessage ?? string.Empty);
                }

                sb.AppendLine();
            }

            return sb.ToString();
        }

        /// <summary>
        /// 获取指定 Store 的时间线统计快照（总量、分类型计数、首尾时间）。
        /// </summary>
        public static MviTimelineStats GetTimelineStats(Store store, Func<MviTimelineEvent, bool> filter = null)
        {
            var snapshot = GetTimelineSnapshot(store);
            if (snapshot == null || snapshot.Count == 0)
            {
                return new MviTimelineStats(0, null, null, new Dictionary<MviTimelineEventKind, int>());
            }

            var totalCount = 0;
            DateTime? firstTimestampUtc = null;
            DateTime? lastTimestampUtc = null;
            var countsByKind = new Dictionary<MviTimelineEventKind, int>();

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

                totalCount++;
                if (firstTimestampUtc == null || entry.TimestampUtc < firstTimestampUtc.Value)
                {
                    firstTimestampUtc = entry.TimestampUtc;
                }

                if (lastTimestampUtc == null || entry.TimestampUtc > lastTimestampUtc.Value)
                {
                    lastTimestampUtc = entry.TimestampUtc;
                }

                countsByKind.TryGetValue(entry.Kind, out var count);
                countsByKind[entry.Kind] = count + 1;
            }

            return new MviTimelineStats(totalCount, firstTimestampUtc, lastTimestampUtc, countsByKind);
        }

        /// <summary>
        /// 导出时间线统计摘要文本（可用于日志或问题单附带信息）。
        /// </summary>
        public static string ExportTimelineSummary(Store store, Func<MviTimelineEvent, bool> filter = null)
        {
            var stats = GetTimelineStats(store, filter);
            var sampling = GetSamplingSnapshot();
            var sb = new StringBuilder();
            sb.Append("total=").Append(stats.TotalCount);
            sb.Append(", first=").Append(stats.FirstTimestampUtc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff") ?? "-");
            sb.Append(", last=").Append(stats.LastTimestampUtc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff") ?? "-");
            sb.Append(", sampleRate=").Append(sampling.sampleRate.ToString("0.###"));

            var kinds = (MviTimelineEventKind[])Enum.GetValues(typeof(MviTimelineEventKind));
            for (var i = 0; i < kinds.Length; i++)
            {
                var kind = kinds[i];
                var count = stats.GetCount(kind);
                if (count <= 0)
                {
                    continue;
                }

                sb.Append(", ").Append(kind).Append('=').Append(count);
            }

            return sb.ToString();
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

            var sampling = GetSamplingSnapshot();
            var sb = new StringBuilder();
            sb.Append("[sampling] enabled=")
                .Append(Enabled)
                .Append(", rate=")
                .Append(sampling.sampleRate.ToString("0.###"))
                .Append(", included=")
                .Append(sampling.includedKinds == null || sampling.includedKinds.Length == 0
                    ? "*"
                    : string.Join("|", sampling.includedKinds))
                .Append(", excludedStores=")
                .Append(sampling.excludedStoreTypes == null || sampling.excludedStoreTypes.Length == 0
                    ? "-"
                    : string.Join("|", sampling.excludedStoreTypes))
                .AppendLine();

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
                sampling = GetSamplingSnapshot(),
                events = exportEntries.ToArray()
            };

            return JsonUtility.ToJson(envelope, prettyPrint: true);
        }

        private static bool ShouldTrackWithSampling(Store store, MviTimelineEventKind kind)
        {
            var options = GetSamplingOptionsSnapshot();
            if (options == null)
            {
                return true;
            }

            var normalizedRate = options.SampleRate;
            if (normalizedRate < 0d)
            {
                normalizedRate = 0d;
            }
            else if (normalizedRate > 1d)
            {
                normalizedRate = 1d;
            }

            if (normalizedRate <= 0d)
            {
                return false;
            }

            if (options.IncludedKinds != null && options.IncludedKinds.Count > 0 && !options.IncludedKinds.Contains(kind))
            {
                return false;
            }

            var storeTypeName = store.GetType().FullName;
            if (!string.IsNullOrWhiteSpace(storeTypeName)
                && options.ExcludedStoreTypeFullNames != null
                && options.ExcludedStoreTypeFullNames.Contains(storeTypeName))
            {
                return false;
            }

            if (normalizedRate >= 1d)
            {
                return true;
            }

            lock (SamplingRandomSyncRoot)
            {
                return SamplingRandom.NextDouble() <= normalizedRate;
            }
        }

        private static TimelineSamplingExport GetSamplingSnapshot()
        {
            var options = GetSamplingOptionsSnapshot() ?? new MviDevToolsSamplingOptions();
            var normalizedRate = options.SampleRate;
            if (normalizedRate < 0d)
            {
                normalizedRate = 0d;
            }
            else if (normalizedRate > 1d)
            {
                normalizedRate = 1d;
            }

            return new TimelineSamplingExport
            {
                enabled = Enabled,
                sampleRate = normalizedRate,
                includedKinds = options.GetIncludedKindsDisplay(),
                excludedStoreTypes = options.GetExcludedStoreTypeDisplay()
            };
        }

        private static MviDevToolsSamplingOptions GetSamplingOptionsSnapshot()
        {
            lock (SyncRoot)
            {
                return (_samplingOptions ?? new MviDevToolsSamplingOptions()).Clone();
            }
        }

        private static string ResolveLatestMiddlewareCorrelationId(IReadOnlyList<MviTimelineEvent> snapshot)
        {
            if (snapshot == null || snapshot.Count == 0)
            {
                return string.Empty;
            }

            for (var i = snapshot.Count - 1; i >= 0; i--)
            {
                var entry = snapshot[i];
                if (entry == null || entry.Kind != MviTimelineEventKind.Middleware)
                {
                    continue;
                }

                if (entry.Payload is not MviMiddlewareTraceEvent trace || string.IsNullOrWhiteSpace(trace.CorrelationId))
                {
                    continue;
                }

                return trace.CorrelationId;
            }

            return string.Empty;
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
