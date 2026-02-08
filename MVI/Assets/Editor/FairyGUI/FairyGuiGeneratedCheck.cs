#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace MVI.Editor
{
    // FairyGUI 生成代码检查：避免缺失导致运行时空引用。
    internal static class FairyGuiGeneratedCheck
    {
        private const string BinderScriptName = "ComposedDashboardWindowBinder";
        private const string BinderExpectedPath =
            "Assets/Samples/Loxodon Framework/2.0.0/Examples/Scripts/Views/UI/FairyGUI/ComposedDashboardWindow/ComposedDashboardWindowBinder.cs";

        [InitializeOnLoadMethod]
        private static void CheckGeneratedFiles()
        {
            var guid = AssetDatabase.AssetPathToGUID(BinderExpectedPath);
            if (!string.IsNullOrEmpty(guid))
            {
                return;
            }

            var guids = AssetDatabase.FindAssets($"{BinderScriptName} t:Script");
            if (guids == null || guids.Length == 0)
            {
                Debug.LogError($"未找到 FairyGUI 生成文件：{BinderExpectedPath}，请重新发布 FairyGUI 包。");
                return;
            }

            var path = AssetDatabase.GUIDToAssetPath(guids[0]);
            Debug.LogWarning($"FairyGUI 生成文件路径变更：{path}，请确认发布设置与路径一致。");
        }
    }
}
#endif
