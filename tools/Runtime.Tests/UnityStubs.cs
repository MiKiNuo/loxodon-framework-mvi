// 仅用于 .NET 状态流与生命周期契约测试，不参与 Unity 程序集编译。
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace UnityEngine
{
    public static class Debug
    {
        public static void LogException(Exception exception) => Console.Error.WriteLine(exception);
    }

    public class Object
    {
        public string name { get; set; } = string.Empty;
        public static void Destroy(Object value) { }
    }

    public class Component : Object
    {
        public Transform transform { get; } = new Transform();
        public GameObject gameObject { get; } = new GameObject();
    }
    public class Behaviour : Component { }
    public class MonoBehaviour : Behaviour { }
    public class ScriptableObject : Object { }

    public class GameObject : Object
    {
        public Transform transform { get; } = new Transform();
        public bool activeSelf { get; set; }
        public void SetActive(bool active) => activeSelf = active;
    }
    public class Transform : Object
    {
        public void SetParent(Transform parent, bool worldPositionStays) { }
    }

    public static class JsonUtility
    {
        private static readonly JsonSerializerOptions Options = CreateOptions();

        // 只处理字段，避免把普通属性的 .NET JSON 行为误当成 Unity 的序列化保证。
        private static JsonSerializerOptions CreateOptions()
        {
            var resolver = new DefaultJsonTypeInfoResolver();
            resolver.Modifiers.Add(info =>
            {
                for (var index = info.Properties.Count - 1; index >= 0; index--)
                {
                    if (info.Properties[index].AttributeProvider is PropertyInfo)
                    {
                        info.Properties.RemoveAt(index);
                    }
                }
            });
            return new JsonSerializerOptions { IncludeFields = true, TypeInfoResolver = resolver };
        }

        public static string ToJson(object value, bool prettyPrint = false)
        {
            if (value == null) return "null";
            var options = prettyPrint ? new JsonSerializerOptions(Options) { WriteIndented = true } : Options;
            return JsonSerializer.Serialize(value, value.GetType(), options);
        }
        public static T FromJson<T>(string json) => (T)FromJson(json, typeof(T));
        public static object FromJson(string json, Type type) => JsonSerializer.Deserialize(json, type, Options);
    }

    public static class Application
    {
        public static string persistentDataPath { get; } = Path.Combine(Path.GetTempPath(), "mvi-tests");
    }

    public static class PlayerPrefs
    {
        private static readonly Dictionary<string, string> Values = new();
        public static bool HasKey(string key) => Values.ContainsKey(key);
        public static string GetString(string key, string defaultValue) => Values.TryGetValue(key, out var value) ? value : defaultValue;
        public static void SetString(string key, string value) => Values[key] = value ?? string.Empty;
        public static void DeleteKey(string key) => Values.Remove(key);
        public static void Save() { }
    }
}

namespace Loxodon.Framework.ViewModels
{
    public abstract class ViewModelBase : IDisposable
    {
        protected virtual void Dispose(bool disposing) { }
        public void Dispose() => Dispose(disposing: true);
    }
}

namespace Loxodon.Framework.Binding
{
    public interface IUIViewLocator
    {
        T LoadView<T>(string name) where T : Loxodon.Framework.Views.UIView;
    }
}

namespace Loxodon.Framework.Views
{
    public abstract class UIView : UnityEngine.MonoBehaviour
    {
        public virtual void SetDataContext(object dataContext) { }
    }
}

namespace R3
{
    public static class ReactivePropertyUnityExtensions
    {
        public static ReadOnlyReactiveProperty<T> ObserveOnMainThread<T>(this ReadOnlyReactiveProperty<T> source) => source;
        public static Observable<T> ObserveOnMainThread<T>(this Observable<T> source) => source;
    }
}

namespace UnityEngine.TestTools
{
    // .NET runner 不发现该特性；协程与真实 Unity 调度由 Unity Test Framework 执行。
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class UnityTestAttribute : Attribute { }
}
