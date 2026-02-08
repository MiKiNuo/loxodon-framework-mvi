using FairyGUI;
using Loxodon.Framework.Binding;
using MVI.FairyGUI;
using UnityEngine;

namespace MVI.Examples.FairyGUI.Counter.Views
{
    // FairyGUI 计数器示例 View（绑定 CounterViewModel）。
    internal sealed class FairyCounterView : MviFairyView<CounterState, IFairyCounterIntent, CounterResultBase, CounterEffect>
    {
        // 示例资源路径（Resources/UI/FairyCounter）。
        private static readonly string[] PackagePathList = { "UI/FairyCounter" };

        private GTextField valueText;
        private GTextInput inputText;
        private GButton incButton;
        private GButton decButton;
        private GButton submitButton;

        // 指定 FairyGUI 资源与组件名（与编辑器中发布的包一致）。
        protected override string[] PackagePaths => FairyCounterView.PackagePathList;
        protected override string PackageName => "FairyCounter";
        protected override string ComponentName => "Main";

        protected override void Awake()
        {
            base.Awake();
            // 示例里 View 创建 ViewModel，实际项目可由外部注入。
            BindViewModel(new CounterViewModel(), disposeViewModel: true);
        }

        protected override void OnViewReady(GComponent root)
        {
            // 绑定控件引用。
            valueText = root.GetChild("txtValue") as GTextField;
            // 输入框命名可能不同，提供兜底名称。
            inputText = root.GetChild("txtInput") as GTextInput
                ?? root.GetChild("inputValue") as GTextInput;
            incButton = root.GetChild("btnInc") as GButton;
            decButton = root.GetChild("btnDec") as GButton;
            submitButton = root.GetChild("btnSubmit") as GButton;
        }

        // 绑定 View 与 ViewModel 的数据关系（Loxodon Binding）。
        public override void Bind()
        {
            var bindingSet = this.CreateBindingSet<FairyCounterView, CounterViewModel>();
            if (valueText != null)
            {
                // 计数值绑定到文本。
                bindingSet.Bind(valueText)
                    .For(v => v.text)
                    .ToExpression(vm => vm.Value.ToString())
                    .OneWay();
            }

            if (inputText != null)
            {
                // 输入框双向绑定（官方示例写法）。
                bindingSet.Bind(inputText)
                    .For(v => v.text, v => v.onChanged)
                    .To(vm => vm.InputText)
                    .OneWay();
            }

            if (incButton != null)
            {
                // 点击事件绑定命令。
                bindingSet.Bind(incButton)
                    .For(v => v.onClick)
                    .To(vm => vm.IncrementCommand);
            }

            if (decButton != null)
            {
                // 点击事件绑定命令。
                bindingSet.Bind(decButton)
                    .For(v => v.onClick)
                    .To(vm => vm.DecrementCommand);
            }

            if (submitButton != null)
            {
                // 提交按钮绑定命令（演示校验效果）。
                bindingSet.Bind(submitButton)
                    .For(v => v.onClick)
                    .To(vm => vm.SubmitCommand);
            }

            bindingSet.Build();
        }

        protected override void OnEffect(CounterEffect effect)
        {
            // 处理一次性事件（示例里用日志输出）。
            if (effect is CounterMessageEffect message)
            {
                Debug.Log(message.Message);
            }

            if (effect is CounterValidationEffect validation)
            {
                Debug.LogWarning(validation.Message);
            }
        }
    }
}
