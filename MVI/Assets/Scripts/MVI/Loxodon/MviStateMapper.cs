using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace MVI
{
    internal static class MviStateMapper
    {
        private static readonly Func<IState, MviViewModel, bool> GeneratedMapper = FindGeneratedMapper();
        private static readonly ConcurrentDictionary<(Type StateType, Type ViewModelType), PropertyPair[]> PairCache = new();

        public static bool TryMap(IState state, MviViewModel viewModel)
        {
            if (state is null || viewModel is null)
            {
                return false;
            }

            if (GeneratedMapper != null && GeneratedMapper(state, viewModel))
            {
                return true;
            }

            return ReflectionMap(state, viewModel);
        }

        private static Func<IState, MviViewModel, bool> FindGeneratedMapper()
        {
            const string mapperTypeName = "MVI.Generated.GeneratedStateMapper";
            const string methodName = "TryMap";

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type;
                try
                {
                    type = assembly.GetType(mapperTypeName, false);
                }
                catch
                {
                    continue;
                }

                if (type == null)
                {
                    continue;
                }

                var method = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
                if (method == null)
                {
                    continue;
                }

                try
                {
                    return (Func<IState, MviViewModel, bool>)Delegate.CreateDelegate(
                        typeof(Func<IState, MviViewModel, bool>), method);
                }
                catch
                {
                    return null;
                }
            }

            return null;
        }

        private static bool ReflectionMap(IState state, MviViewModel viewModel)
        {
            var pairs = PairCache.GetOrAdd((state.GetType(), viewModel.GetType()), key => BuildPairs(key.StateType, key.ViewModelType));
            if (pairs.Length == 0)
            {
                return false;
            }

            var onlyIfChanged = !state.IsUpdateNewState;
            foreach (var pair in pairs)
            {
                var newValue = pair.StateProperty.GetValue(state);
                if (newValue == null && pair.ViewModelProperty.PropertyType.IsValueType && Nullable.GetUnderlyingType(pair.ViewModelProperty.PropertyType) == null)
                {
                    continue;
                }

                if (onlyIfChanged)
                {
                    var currentValue = pair.ViewModelProperty.GetValue(viewModel);
                    if (Equals(currentValue, newValue))
                    {
                        continue;
                    }
                }

                pair.ViewModelProperty.SetValue(viewModel, newValue);
            }

            return true;
        }

        private static PropertyPair[] BuildPairs(Type stateType, Type viewModelType)
        {
            var stateProps = GetStateProperties(stateType);
            var viewModelProps = GetViewModelProperties(viewModelType);
            var pairs = new List<PropertyPair>();

            foreach (var kvp in stateProps)
            {
                if (!viewModelProps.TryGetValue(kvp.Key, out var vmProp))
                {
                    continue;
                }

                if (!vmProp.PropertyType.IsAssignableFrom(kvp.Value.PropertyType))
                {
                    continue;
                }

                pairs.Add(new PropertyPair(kvp.Value, vmProp));
            }

            return pairs.ToArray();
        }

        private static Dictionary<string, PropertyInfo> GetStateProperties(Type stateType)
        {
            var props = new Dictionary<string, PropertyInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in GetAllPublicInstanceProperties(stateType))
            {
                if (property.Name == "IsUpdateNewState")
                {
                    continue;
                }

                if (HasIgnore(property))
                {
                    continue;
                }

                var name = GetMappedName(property) ?? property.Name;
                if (!props.ContainsKey(name))
                {
                    props.Add(name, property);
                }
            }

            return props;
        }

        private static Dictionary<string, PropertyInfo> GetViewModelProperties(Type viewModelType)
        {
            var props = new Dictionary<string, PropertyInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in GetAllPublicInstanceProperties(viewModelType))
            {
                if (!property.CanWrite || property.SetMethod == null || !property.SetMethod.IsPublic)
                {
                    continue;
                }

                if (HasIgnore(property))
                {
                    continue;
                }

                if (!props.ContainsKey(property.Name))
                {
                    props.Add(property.Name, property);
                }

                var alias = GetMappedName(property);
                if (!string.IsNullOrWhiteSpace(alias) && !props.ContainsKey(alias))
                {
                    props.Add(alias, property);
                }
            }

            return props;
        }

        private static IEnumerable<PropertyInfo> GetAllPublicInstanceProperties(Type type)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy;
            return type.GetProperties(flags).Where(p => p.GetIndexParameters().Length == 0);
        }

        private static bool HasIgnore(PropertyInfo property)
        {
            return property.GetCustomAttributes(typeof(MviIgnoreAttribute), true).Length > 0;
        }

        private static string GetMappedName(PropertyInfo property)
        {
            var attribute = property.GetCustomAttributes(typeof(MviMapAttribute), true).FirstOrDefault() as MviMapAttribute;
            return attribute?.Name;
        }

        private readonly struct PropertyPair
        {
            public PropertyPair(PropertyInfo stateProperty, PropertyInfo viewModelProperty)
            {
                StateProperty = stateProperty;
                ViewModelProperty = viewModelProperty;
            }

            public PropertyInfo StateProperty { get; }
            public PropertyInfo ViewModelProperty { get; }
        }
    }
}
