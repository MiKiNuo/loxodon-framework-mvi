using System;
using MVI.Components;
using MVI.Composition;
using NUnit.Framework;

namespace MVI.Tests
{
    public class CompositionRuntimeTests
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

        [Test]
        public void ApplyProps_ShouldDiffByEquals()
        {
            var runtime = new CompositionRuntime();
            var viewModel = new TestViewModel();
            runtime.TryRegisterComponent("Counter", new object(), viewModel);

            var props1 = new TestProps(1);
            var props2 = new TestProps(1);

            runtime.ApplyProps("Counter", props1);
            runtime.ApplyProps("Counter", props1);
            runtime.ApplyProps("Counter", props2);

            Assert.AreEqual(1, viewModel.ApplyCount);
        }

        [Test]
        public void EventRoute_ShouldDispatchByRuntimeEvent()
        {
            var runtime = new CompositionRuntime();
            runtime.ComponentEventRaised += runtime.DispatchEventRoutes;

            var called = false;
            runtime.AddEventRoute("Counter", "CountChanged", typeof(int), payload =>
            {
                if (payload is int value && value == 7)
                {
                    called = true;
                }
            });

            runtime.EmitComponentEvent("Counter", "CountChanged", 7);

            Assert.IsTrue(called);
        }

        [Test]
        public void Dispose_ShouldRunCleanupActions()
        {
            var runtime = new CompositionRuntime();
            var unsubscribed = false;

            runtime.TrackSubscription(
                subscribe: null,
                unsubscribe: () => unsubscribed = true);

            runtime.Dispose();

            Assert.IsTrue(unsubscribed);
        }
    }
}
