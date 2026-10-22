#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace MVI.Editor
{
    /// <summary>
    /// MVI DevTools 编辑器窗口：查看时间线并执行 Replay / Time-travel。
    /// </summary>
    public sealed class MviDevToolsWindow : EditorWindow
    {
        private const string MenuPath = "MVI/DevTools/Timeline Window";

        private readonly Dictionary<MviTimelineEventKind, bool> _filters = new();
        private readonly List<Store> _stores = new();
        private readonly List<string> _storeLabels = new();
        private Vector2 _timelineScroll;
        private double _lastRefreshTime;
        private int _selectedStoreIndex;
        private bool _autoRefresh = true;
        private float _refreshIntervalSeconds = 1f;
        private bool _showPayloadDetails = false;
        private long _timelineSequenceInput;

        [MenuItem(MenuPath)]
        private static void Open()
        {
            var window = GetWindow<MviDevToolsWindow>("MVI DevTools");
            window.minSize = new Vector2(780, 460);
            window.RefreshStoreList();
            window.Show();
        }

        private void OnEnable()
        {
            var kinds = (MviTimelineEventKind[])Enum.GetValues(typeof(MviTimelineEventKind));
            for (var i = 0; i < kinds.Length; i++)
            {
                _filters[kinds[i]] = true;
            }

            RefreshStoreList();
            EditorApplication.update += OnEditorUpdate;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
        }

        private void OnEditorUpdate()
        {
            if (!_autoRefresh || EditorApplication.isPlaying == false)
            {
                return;
            }

            var now = EditorApplication.timeSinceStartup;
            if (now - _lastRefreshTime < Math.Max(0.2f, _refreshIntervalSeconds))
            {
                return;
            }

            _lastRefreshTime = now;
            RefreshStoreList();
            Repaint();
        }

        private void OnGUI()
        {
            DrawToolbar();
            EditorGUILayout.Space(6f);
            DrawStoreSelector();
            EditorGUILayout.Space(6f);
            DrawFilterPanel();
            EditorGUILayout.Space(6f);
            DrawTimelinePanel();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("MVI Timeline", EditorStyles.boldLabel, GUILayout.Width(100));

            var devToolsEnabled = GUILayout.Toggle(MviDevTools.Enabled, "DevTools Enabled", EditorStyles.toolbarButton, GUILayout.Width(120));
            if (devToolsEnabled != MviDevTools.Enabled)
            {
                MviDevTools.Enabled = devToolsEnabled;
            }

            GUILayout.Space(8);
            GUILayout.Label("Max Events", GUILayout.Width(70));
            var maxEvents = EditorGUILayout.IntField(MviDevTools.MaxEventsPerStore, GUILayout.Width(70));
            if (maxEvents != MviDevTools.MaxEventsPerStore)
            {
                MviDevTools.MaxEventsPerStore = Math.Max(1, maxEvents);
            }

            GUILayout.Space(8);
            var sampling = MviDevTools.SamplingOptions;
            GUILayout.Label("Sample", GUILayout.Width(50));
            var sampleRate = EditorGUILayout.Slider((float)sampling.SampleRate, 0f, 1f, GUILayout.Width(120));
            if (Math.Abs(sampleRate - (float)sampling.SampleRate) > 0.0001f)
            {
                sampling.SampleRate = sampleRate;
                MviDevTools.SamplingOptions = sampling;
            }

            GUILayout.FlexibleSpace();
            _autoRefresh = GUILayout.Toggle(_autoRefresh, "Auto", EditorStyles.toolbarButton, GUILayout.Width(45));
            _refreshIntervalSeconds = EditorGUILayout.Slider(_refreshIntervalSeconds, 0.2f, 5f, GUILayout.Width(140));

            if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(70)))
            {
                RefreshStoreList();
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawStoreSelector()
        {
            EditorGUILayout.BeginVertical("box");
            GUILayout.Label("Stores", EditorStyles.boldLabel);
            if (_storeLabels.Count == 0)
            {
                EditorGUILayout.HelpBox("暂无被追踪的 Store。请先在运行时触发意图并确保 MviDevTools.Enabled = true。", MessageType.Info);
                EditorGUILayout.EndVertical();
                return;
            }

            _selectedStoreIndex = EditorGUILayout.Popup("Current", Mathf.Clamp(_selectedStoreIndex, 0, _storeLabels.Count - 1), _storeLabels.ToArray());
            var store = GetSelectedStore();
            if (store == null)
            {
                EditorGUILayout.EndVertical();
                return;
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Replay Intents", GUILayout.Width(120)))
            {
                ReplaySelectedStoreAsync(store);
            }

            using (new EditorGUI.DisabledScope(!store.CanUndo))
            {
                if (GUILayout.Button("Undo", GUILayout.Width(80)))
                {
                    store.UndoState();
                }
            }

            using (new EditorGUI.DisabledScope(!store.CanRedo))
            {
                if (GUILayout.Button("Redo", GUILayout.Width(80)))
                {
                    store.RedoState();
                }
            }

            if (GUILayout.Button("Clear Timeline", GUILayout.Width(110)))
            {
                store.ClearTimeline();
            }

            if (GUILayout.Button("Copy Timeline", GUILayout.Width(110)))
            {
                CopySelectedTimelineToClipboard(store);
            }

            if (GUILayout.Button("Save Timeline", GUILayout.Width(110)))
            {
                SaveSelectedTimelineToFile(store);
            }

            if (GUILayout.Button("Copy JSON", GUILayout.Width(95)))
            {
                CopySelectedTimelineJsonToClipboard(store);
            }

            if (GUILayout.Button("Save JSON", GUILayout.Width(95)))
            {
                SaveSelectedTimelineJsonToFile(store);
            }

            if (GUILayout.Button("Copy Trace", GUILayout.Width(95)))
            {
                CopySelectedMiddlewareTraceToClipboard(store);
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            _timelineSequenceInput = EditorGUILayout.LongField("Sequence", _timelineSequenceInput);
            if (GUILayout.Button("Time-travel", GUILayout.Width(100)))
            {
                store.TryTimeTravelToTimelineSequence(_timelineSequenceInput);
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private void DrawFilterPanel()
        {
            EditorGUILayout.BeginVertical("box");
            GUILayout.Label("Filters", EditorStyles.boldLabel);
            _showPayloadDetails = EditorGUILayout.ToggleLeft("Show Payload Details", _showPayloadDetails);
            EditorGUILayout.BeginHorizontal();
            var kinds = (MviTimelineEventKind[])Enum.GetValues(typeof(MviTimelineEventKind));
            for (var i = 0; i < kinds.Length; i++)
            {
                var kind = kinds[i];
                var current = _filters.TryGetValue(kind, out var enabled) && enabled;
                _filters[kind] = GUILayout.Toggle(current, kind.ToString(), "Button", GUILayout.Height(20));
            }

            EditorGUILayout.EndHorizontal();

            var sampling = MviDevTools.SamplingOptions;
            var includedKindsText = sampling.IncludedKinds == null || sampling.IncludedKinds.Count == 0
                ? "*"
                : string.Join(",", sampling.GetIncludedKindsDisplay());
            EditorGUILayout.LabelField(
                $"Sampling: rate={sampling.SampleRate:0.###}, included={includedKindsText}, excludedStores={sampling.GetExcludedStoreTypeDisplay().Length}",
                EditorStyles.miniLabel);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Use Filters As Sampling", GUILayout.Width(180)))
            {
                ApplyFiltersToSamplingKinds();
            }

            if (GUILayout.Button("Clear Sampling Kinds", GUILayout.Width(160)))
            {
                var options = MviDevTools.SamplingOptions;
                options.IncludedKinds.Clear();
                MviDevTools.SamplingOptions = options;
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private void DrawTimelinePanel()
        {
            EditorGUILayout.BeginVertical("box");
            GUILayout.Label("Timeline", EditorStyles.boldLabel);
            var store = GetSelectedStore();
            if (store == null)
            {
                EditorGUILayout.EndVertical();
                return;
            }

            var timeline = store.GetTimelineSnapshot();
            if (timeline == null || timeline.Count == 0)
            {
                EditorGUILayout.HelpBox("当前 Store 时间线为空。", MessageType.None);
                EditorGUILayout.EndVertical();
                return;
            }

            DrawTimelineStatsPanel(store);

            _timelineScroll = EditorGUILayout.BeginScrollView(_timelineScroll);
            for (var i = 0; i < timeline.Count; i++)
            {
                var entry = timeline[i];
                if (!IsEventEnabled(entry))
                {
                    continue;
                }

                DrawTimelineEntry(store, entry);
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawTimelineEntry(Store store, MviTimelineEvent entry)
        {
            EditorGUILayout.BeginVertical("HelpBox");
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label($"#{entry.Sequence}", GUILayout.Width(64));
            GUILayout.Label(entry.TimestampUtc.ToLocalTime().ToString("HH:mm:ss.fff"), GUILayout.Width(92));
            GUILayout.Label(entry.Kind.ToString(), GUILayout.Width(90));
            GUILayout.Label(entry.Payload?.GetType().Name ?? "null", GUILayout.Width(180));
            GUILayout.Label(entry.Note ?? string.Empty);

            if (entry.Kind == MviTimelineEventKind.State)
            {
                if (GUILayout.Button("Jump", GUILayout.Width(64)))
                {
                    store.TryTimeTravelToTimelineSequence(entry.Sequence);
                }
            }

            EditorGUILayout.EndHorizontal();

            if (_showPayloadDetails && entry.Payload != null)
            {
                EditorGUILayout.TextArea(entry.Payload.ToString(), GUILayout.MinHeight(28));
            }

            EditorGUILayout.EndVertical();
        }

        private Store GetSelectedStore()
        {
            if (_stores.Count == 0)
            {
                return null;
            }

            var index = Mathf.Clamp(_selectedStoreIndex, 0, _stores.Count - 1);
            _selectedStoreIndex = index;
            return _stores[index];
        }

        private void RefreshStoreList()
        {
            _stores.Clear();
            _storeLabels.Clear();

            var stores = MviDevTools.GetTrackedStoresSnapshot();
            if (stores == null || stores.Count == 0)
            {
                _selectedStoreIndex = 0;
                return;
            }

            for (var i = 0; i < stores.Count; i++)
            {
                var store = stores[i];
                if (store == null)
                {
                    continue;
                }

                _stores.Add(store);
                _storeLabels.Add(BuildStoreLabel(store));
            }

            _selectedStoreIndex = Mathf.Clamp(_selectedStoreIndex, 0, Math.Max(0, _stores.Count - 1));
        }

        private static string BuildStoreLabel(Store store)
        {
            return $"{store.GetType().Name}  (#{store.GetHashCode():X8})";
        }

        private async void ReplaySelectedStoreAsync(Store store)
        {
            if (store == null)
            {
                return;
            }

            try
            {
                var count = await store.ReplayIntentsAsync();
                Debug.Log($"[MVI-DevTools] Replay finished. count={count}, store={store.GetType().Name}");
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
            finally
            {
                RefreshStoreList();
                Repaint();
            }
        }

        private void CopySelectedTimelineToClipboard(Store store)
        {
            var content = BuildFilteredTimelineText(store);
            if (string.IsNullOrEmpty(content))
            {
                Debug.Log("[MVI-DevTools] Timeline is empty.");
                return;
            }

            EditorGUIUtility.systemCopyBuffer = content;
            Debug.Log($"[MVI-DevTools] Timeline copied. chars={content.Length}");
        }

        private void SaveSelectedTimelineToFile(Store store)
        {
            var content = BuildFilteredTimelineText(store);
            if (string.IsNullOrEmpty(content))
            {
                Debug.Log("[MVI-DevTools] Timeline is empty.");
                return;
            }

            var path = EditorUtility.SaveFilePanel(
                "Save MVI Timeline",
                Application.dataPath,
                $"{store.GetType().Name}.timeline.txt",
                "txt");

            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            try
            {
                File.WriteAllText(path, content);
                Debug.Log($"[MVI-DevTools] Timeline saved: {path}");
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        private string BuildFilteredTimelineText(Store store)
        {
            return MviDevTools.ExportTimeline(
                store,
                includePayloadDetails: _showPayloadDetails,
                filter: IsEventEnabled);
        }

        private string BuildFilteredTimelineJson(Store store)
        {
            return MviDevTools.ExportTimelineJson(
                store,
                includePayloadDetails: _showPayloadDetails,
                filter: IsEventEnabled);
        }

        private void ApplyFiltersToSamplingKinds()
        {
            var options = MviDevTools.SamplingOptions;
            options.IncludedKinds.Clear();

            foreach (var filter in _filters)
            {
                if (!filter.Value)
                {
                    continue;
                }

                options.IncludedKinds.Add(filter.Key);
            }

            MviDevTools.SamplingOptions = options;
        }

        private bool IsEventEnabled(MviTimelineEvent entry)
        {
            return entry != null
                   && _filters.TryGetValue(entry.Kind, out var enabled)
                   && enabled;
        }

        private void DrawTimelineStatsPanel(Store store)
        {
            var stats = MviDevTools.GetTimelineStats(store, IsEventEnabled);
            var summary = MviDevTools.ExportTimelineSummary(store, IsEventEnabled);

            EditorGUILayout.BeginVertical("HelpBox");
            EditorGUILayout.LabelField("Summary", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(summary, EditorStyles.miniLabel);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Copy Summary", GUILayout.Width(120)))
            {
                EditorGUIUtility.systemCopyBuffer = summary;
                Debug.Log($"[MVI-DevTools] Timeline summary copied. chars={summary.Length}");
            }

            if (GUILayout.Button("Copy Middleware Trace", GUILayout.Width(180)))
            {
                CopySelectedMiddlewareTraceToClipboard(store);
            }

            EditorGUILayout.EndHorizontal();

            if (stats.TotalCount > 0)
            {
                var kinds = (MviTimelineEventKind[])Enum.GetValues(typeof(MviTimelineEventKind));
                for (var i = 0; i < kinds.Length; i++)
                {
                    var kind = kinds[i];
                    var count = stats.GetCount(kind);
                    if (count <= 0)
                    {
                        continue;
                    }

                    var percentage = (double)count * 100d / stats.TotalCount;
                    EditorGUILayout.LabelField($"{kind}: {count} ({percentage:0.##}%)", EditorStyles.miniLabel);
                }
            }

            EditorGUILayout.EndVertical();
        }

        private void CopySelectedTimelineJsonToClipboard(Store store)
        {
            var content = BuildFilteredTimelineJson(store);
            if (string.IsNullOrEmpty(content))
            {
                Debug.Log("[MVI-DevTools] Timeline JSON is empty.");
                return;
            }

            EditorGUIUtility.systemCopyBuffer = content;
            Debug.Log($"[MVI-DevTools] Timeline JSON copied. chars={content.Length}");
        }

        private void SaveSelectedTimelineJsonToFile(Store store)
        {
            var content = BuildFilteredTimelineJson(store);
            if (string.IsNullOrEmpty(content))
            {
                Debug.Log("[MVI-DevTools] Timeline JSON is empty.");
                return;
            }

            var path = EditorUtility.SaveFilePanel(
                "Save MVI Timeline JSON",
                Application.dataPath,
                $"{store.GetType().Name}.timeline.json",
                "json");

            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            try
            {
                File.WriteAllText(path, content);
                Debug.Log($"[MVI-DevTools] Timeline JSON saved: {path}");
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        private void CopySelectedMiddlewareTraceToClipboard(Store store)
        {
            var content = MviDevTools.ExportMiddlewareTrace(store);
            if (string.IsNullOrEmpty(content))
            {
                Debug.Log("[MVI-DevTools] Middleware trace is empty.");
                return;
            }

            EditorGUIUtility.systemCopyBuffer = content;
            Debug.Log($"[MVI-DevTools] Middleware trace copied. chars={content.Length}");
        }
    }
}
#endif
