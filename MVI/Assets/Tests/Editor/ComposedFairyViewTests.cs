using System;
using MVI.Components;
using MVI.FairyGUI.Composed;
using NUnit.Framework;
using UnityEngine;

namespace MVI.Tests
{
    public class ComposedFairyViewTests
    {
        private sealed class TestProps : IEquatable<TestProps>
        {
            public TestProps(int value)
            {
                Value = value;
            }

            public int Value { get; }

            public bool Equals(TestProps other)
            {
                if (ReferenceEquals(null, other))
                {
                    return false;
                }

                if (ReferenceEquals(this, other))
                {
                    return true;
                }

                return Value == other.Value;
            }

            public override bool Equals(object obj)
            {
                return Equals(obj as TestProps);
            }

            public override int GetHashCode()
            {
                return Value.GetHashCode();
            }
        }

        private sealed class TestViewModel : IPropsReceiver<TestProps>
        {
            public int ApplyCount { get; private set; }

            public void SetProps(TestProps props)
            {
                ApplyCount++;
            }
        }

        private sealed class TestFairyView : IFairyView
        {
            public object DataContext { get; private set; }

            public void SetDataContext(object viewModel)
            {
                DataContext = viewModel;
            }

            public void Bind()
            {
            }

            public void Dispose()
            {
            }
        }

        private sealed class TestComposedView : ComposedFairyViewBase
        {
            public void ExposeRegister(string componentId, TestFairyView view, TestViewModel viewModel)
            {
                RegisterComponent<TestFairyView, TestViewModel>(componentId, view, viewModel);
            }

            public void ExposeApplyProps(string componentId, TestProps props)
            {
                ApplyProps(componentId, props);
            }

            public void ExposeAddRoute<T>(string componentId, string eventName, Action<T> handler)
            {
                AddEventRoute(componentId, eventName, typeof(T), payload =>
                {
                    if (payload is T typed)
                    {
                        handler(typed);
                    }
                });
            }

            public void ExposeEmit(string componentId, string eventName, object payload)
            {
                EmitComponentEvent(componentId, eventName, payload);
            }

            protected override void OnCompose()
            {
            }
        }

        [Test]
        public void ApplyProps_ShouldDiffByEquals()
        {
            var go = new GameObject("TestComposedFairyView");
            try
            {
                var view = go.AddComponent<TestComposedView>();
                var vm = new TestViewModel();
                var componentView = new TestFairyView();

                view.ExposeRegister("Counter", componentView, vm);

                var props1 = new TestProps(1);
                var props2 = new TestProps(1);

                view.ExposeApplyProps("Counter", props1);
                view.ExposeApplyProps("Counter", props1);
                view.ExposeApplyProps("Counter", props2);

                Assert.AreEqual(1, vm.ApplyCount);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void EventRoute_ShouldDispatchToHandler()
        {
            var go = new GameObject("TestComposedFairyView");
            try
            {
                var view = go.AddComponent<TestComposedView>();
                var called = false;

                view.ExposeAddRoute<int>("Counter", "CountChanged", payload =>
                {
                    if (payload == 3)
                    {
                        called = true;
                    }
                });

                view.ExposeEmit("Counter", "CountChanged", 3);

                Assert.IsTrue(called);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }
    }
}
