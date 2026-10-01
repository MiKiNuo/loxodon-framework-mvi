/*
 * MVI 框架的 FairyGUI 集成入口。
 *
 * 重要说明：
 *  - Loxodon.Framework.Binding.Reflection.IProxyType 抽象在 Loxodon 内部同时支持
 *    "已编译表达式"（无反射）与"反射回退"两条路径。
 *  - Loxodon 提供了 IProxyTypeRegistry 用于在应用启动时为已知目标类型注册"已编译表达式"实现，
 *    注册后走零反射路径；未注册时才走反射回退。
 *  - 本类 (FairyGUIBindingServiceBundle) 仅负责把 FairyGUI 工厂挂到 Loxodon 框架的工厂链上，
 *    不参与反射实现。反射的取舍由 Loxodon 自身的 IProxyTypeRegistry 决定。
 *  - 若要彻底消除反射，请在使用本 Bundle 之前注册全部 FairyGUI 目标类型的
 *    IProxyType/CompiledProxyType，对应反射回退永远不会被走到。
 */

using Loxodon.Framework.Binding.Proxy.Targets;
using Loxodon.Framework.Services;
using System;

namespace Loxodon.Framework.Binding
{
    public class FairyGUIBindingServiceBundle : AbstractServiceBundle
    {
        public FairyGUIBindingServiceBundle(IServiceContainer container) : base(container)
        {
        }

        protected override void OnStart(IServiceContainer container)
        {
            var targetFactory = container.Resolve<ITargetProxyFactoryRegister>();
            if (targetFactory == null)
                throw new Exception("Data binding service is not initialized,please create a BindingServiceBundle service before using it.");

            targetFactory.Register(new FairyTargetProxyFactory(), 20);
        }

        protected override void OnStop(IServiceContainer container)
        {
        }
    }
}
