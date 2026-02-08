using System;
using FairyGUI;

namespace MVI.FairyGUI.Utils
{
    // FairyGUI 视图工具类：提供递归查找等通用能力。
    public static class FairyGuiViewHelper
    {
        // 递归按 URL 查找组件。
        public static T FindByUrl<T>(GComponent root, string url) where T : GComponent
        {
            return FindByPredicate(root, child => string.Equals(child.resourceURL, url, StringComparison.Ordinal)) as T;
        }

        // 递归按名称查找组件。
        public static T FindByName<T>(GComponent root, string name) where T : GComponent
        {
            return FindByPredicate(root, child => string.Equals(child.name, name, StringComparison.Ordinal)) as T;
        }

        // 递归按名称查找任意子对象（不限于 GComponent）。
        public static GObject FindByName(GComponent root, string name)
        {
            return FindByPredicate(root, child => string.Equals(child.name, name, StringComparison.Ordinal));
        }

        // 递归查找满足条件的子组件。
        private static GObject FindByPredicate(GComponent root, Func<GObject, bool> predicate)
        {
            if (root == null || predicate == null)
            {
                return null;
            }

            for (int i = 0; i < root.numChildren; i++)
            {
                var child = root.GetChildAt(i);
                if (child == null)
                {
                    continue;
                }

                if (predicate(child))
                {
                    return child;
                }

                if (child is GComponent childCom)
                {
                    var found = FindByPredicate(childCom, predicate);
                    if (found != null)
                    {
                        return found;
                    }
                }
            }

            return null;
        }
    }
}
