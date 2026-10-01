using System;
using System.Collections.Generic;

namespace MVI
{
    /// <summary>
    /// DevTools 接入契约：把 Store 与 <see cref="MviDevTools"/> 静态全局类解耦，
    /// 业务 Store 只需要向该契约发送事件、读取/写入配置，底层到底写全局表、写文件还是完全忽略都由适配器决定。
    /// </summary>
    /// <remarks>
    /// 设计动机：
    /// <list type="bullet">
    /// <item>Store 不再依赖 <c>MviDevTools.Track(this, ...)</c> 的静态调用，便于在测试中替换为 <see cref="NullMviDevToolsHost"/> 或内存记录器。</item>
    /// <item>允许业务以"局部"或"模块级"的方式关闭 DevTools 抓取，而不必切换全局 <c>MviDevTools.Enabled</c>。</item>
    /// <item>为多 Store 场景提供按实例隔离的埋点适配器提供 seam（如独立 DebugWindow 只想观察一个 Store）。</item>
    /// <item>配置（<c>Enabled</c> / <c>MaxEventsPerStore</c> / <c>SamplingOptions</c>）也走该契约，Store 不再直接写 <see cref="MviDevTools"/> 静态字段。</item>
    /// </list>
    /// </remarks>
    public interface IMviDevToolsHost
    {
        /// <summary>
        /// 当前是否启用 DevTools 抓取。
        /// </summary>
        bool Enabled { get; set; }

        /// <summary>
        /// 单个 Store 最多保留多少条事件（&lt;=0 表示不限制）。
        /// </summary>
        int MaxEventsPerStore { get; set; }

        /// <summary>
        /// 时间线采样配置。
        /// </summary>
        MviDevToolsSamplingOptions SamplingOptions { get; set; }

        /// <summary>
        /// 由 Store 在 Intent/Result/State/Effect/Error 等关键节点调用。
        /// 实现可以决定是否记录、是否采样、是否上报远端。
        /// </summary>
        void Track(Store store, MviTimelineEventKind kind, object payload, string note = null);

        /// <summary>
        /// 获取 Store 的时间线快照：默认实现透传到 <see cref="MviDevTools.GetTimelineSnapshot(Store)"/>，自定义实现可以走独立存储。
        /// </summary>
        IReadOnlyList<MviTimelineEvent> GetTimelineSnapshot(Store store);

        /// <summary>
        /// 清空 Store 的时间线：默认实现透传到 <see cref="MviDevTools.Clear(Store)"/>。
        /// </summary>
        void Clear(Store store);

        /// <summary>
        /// Store 被释放时调用，宿主负责清理与该 Store 关联的资源。
        /// </summary>
        void Detach(Store store);
    }

    /// <summary>
    /// 空实现：在不需要 DevTools 抓取时（如测试、性能基准）使用，调用零开销。
    /// </summary>
    public sealed class NullMviDevToolsHost : IMviDevToolsHost
    {
        /// <summary>
        /// 共享空实现实例，便于业务直接复用而无需重复创建。
        /// </summary>
        public static NullMviDevToolsHost Shared { get; } = new();

        public bool Enabled
        {
            get => false;
            set { /* 空实现：始终禁用。 */ }
        }

        public int MaxEventsPerStore
        {
            get => 0;
            set { /* 空实现：始终无限制。 */ }
        }

        public MviDevToolsSamplingOptions SamplingOptions
        {
            get => new();
            set { /* 空实现：丢弃配置。 */ }
        }

        public void Track(Store store, MviTimelineEventKind kind, object payload, string note = null)
        {
            // 故意空实现：让 Store 在禁用 DevTools 时走零开销路径。
        }

        public IReadOnlyList<MviTimelineEvent> GetTimelineSnapshot(Store store)
        {
            return Array.Empty<MviTimelineEvent>();
        }

        public void Clear(Store store)
        {
            // 故意空实现。
        }

        public void Detach(Store store)
        {
            // 故意空实现。
        }
    }

    /// <summary>
    /// 默认实现：把事件透传给静态 <see cref="MviDevTools"/>，保留原有全局行为与 Editor 视图联动。
    /// </summary>
    public sealed class MviDevToolsHost : IMviDevToolsHost
    {
        /// <summary>
        /// 共享默认实例：与原 <see cref="MviDevTools"/> 静态行为完全一致。
        /// </summary>
        public static MviDevToolsHost Shared { get; } = new();

        public bool Enabled
        {
            get => MviDevTools.Enabled;
            set => MviDevTools.Enabled = value;
        }

        public int MaxEventsPerStore
        {
            get => MviDevTools.MaxEventsPerStore;
            set => MviDevTools.MaxEventsPerStore = value;
        }

        public MviDevToolsSamplingOptions SamplingOptions
        {
            get => MviDevTools.SamplingOptions;
            set => MviDevTools.SamplingOptions = value;
        }

        public void Track(Store store, MviTimelineEventKind kind, object payload, string note = null)
        {
            MviDevTools.Track(store, kind, payload, note);
        }

        public IReadOnlyList<MviTimelineEvent> GetTimelineSnapshot(Store store)
        {
            return MviDevTools.GetTimelineSnapshot(store);
        }

        public void Clear(Store store)
        {
            MviDevTools.Clear(store);
        }

        public void Detach(Store store)
        {
            MviDevTools.Detach(store);
        }
    }
}
