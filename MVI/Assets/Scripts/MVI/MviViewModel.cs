using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using Loxodon.Framework.ViewModels;
using Mapper;
using R3;

namespace MVI
{
    public abstract class MviViewModel : ViewModelBase
    {
        protected Store Store { get; private set; }
        private readonly CompositeDisposable _disposables = new();

        private static readonly Dictionary<(Type vmType, Type stateType), List<PropertyAccessor>>
            _accessorCache = new();

        public void BindStore(Store store)
        {
            Store = store;
            // 使用 ObserveOnMainThread 确保 UI 线程更新
            Store.State
                .ObserveOnMainThread()
                .Subscribe(OnStateChanged)
                .AddTo(_disposables);
        }

        protected virtual void OnStateChanged(IState? state)
        {
            if (state is null)
            {
                return;
            }

            var vmType = GetType();
            var stateType = state.GetType();
            var key = (vmType, stateType);

            // 获取或创建属性访问器
            if (!_accessorCache.TryGetValue(key, out var accessors))
            {
                accessors = CreateAccessors(vmType, stateType);
                _accessorCache[key] = accessors;
            }

            // 根据更新模式执行
            if (!state.IsUpdateNewState)
            {
                ConditionalUpdate(accessors, state);
            }
            else
            {
                UnconditionalUpdate(accessors, state);
            }
            
        }

        protected override void Dispose(bool disposing)
        {
            _disposables.Dispose();
            base.Dispose(disposing);
        }

        protected void EmitIntent(IIntent intent)
        {
            Store.EmitIntent(intent);
        }

        private class PropertyAccessor
        {
            public Func<object, object> GetStateValue { get; set; }
            public Func<object, object> GetVmValue { get; set; }
            public Action<object, object> SetVmValue { get; set; }
        }

        private List<PropertyAccessor> CreateAccessors(Type vmType, Type stateType)
        {
            var accessors = new List<PropertyAccessor>();
            var stateProps = stateType.GetProperties();

            foreach (var stateProp in stateProps)
            {
                if (!stateProp.CanRead) continue;

                var vmProp = vmType.GetProperty(stateProp.Name);
                if (vmProp == null || !vmProp.CanWrite) continue;

                // 创建状态属性getter
                var stateGetter = CreateGetterDelegate(stateType, stateProp);

                // 创建VM属性getter
                var vmGetter = CreateGetterDelegate(vmType, vmProp);

                // 创建VM属性setter
                var vmSetter = CreateSetterDelegate(vmType, vmProp);

                accessors.Add(new PropertyAccessor
                {
                    GetStateValue = stateGetter,
                    GetVmValue = vmGetter,
                    SetVmValue = vmSetter
                });
            }

            return accessors;
        }

        private Func<object, object> CreateGetterDelegate(Type type, PropertyInfo prop)
        {
            // 参数：实例对象
            var instance = Expression.Parameter(typeof(object), "instance");

            // 转换类型并访问属性
            var converted = Expression.Convert(instance, type);
            var property = Expression.Property(converted, prop);
            var convertResult = Expression.Convert(property, typeof(object));

            // 编译Lambda表达式
            return Expression.Lambda<Func<object, object>>(convertResult, instance).Compile();
        }

        private Action<object, object> CreateSetterDelegate(Type type, PropertyInfo prop)
        {
            // 参数：实例对象和值
            var instance = Expression.Parameter(typeof(object), "instance");
            var value = Expression.Parameter(typeof(object), "value");

            // 转换类型
            var convertedInstance = Expression.Convert(instance, type);
            var convertedValue = Expression.Convert(value, prop.PropertyType);

            // 属性赋值
            var setCall = Expression.Call(convertedInstance, prop.GetSetMethod(), convertedValue);

            // 编译Lambda表达式
            return Expression.Lambda<Action<object, object>>(setCall, instance, value).Compile();
        }

        private void ConditionalUpdate(List<PropertyAccessor> accessors, IState state)
        {
            foreach (var accessor in accessors)
            {
                var newValue = accessor.GetStateValue(state);
                var currentValue = accessor.GetVmValue(this);

                if (!Equals(currentValue, newValue))
                {
                    accessor.SetVmValue(this, newValue);
                }
            }
        }

        private void UnconditionalUpdate(List<PropertyAccessor> accessors, IState state)
        {
            foreach (var accessor in accessors)
            {
                var newValue = accessor.GetStateValue(state);
                accessor.SetVmValue(this, newValue);
            }
        }
    }
}