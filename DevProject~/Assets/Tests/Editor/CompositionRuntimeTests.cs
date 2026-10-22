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
        public void EventRoute_ShouldDispatchExactlyOnce()
        {
            var runtime = new CompositionRuntime();
            var notificationCount = 0;
            runtime.ComponentEventRaised += _ => notificationCount++;

            var callCount = 0;
            runtime.AddEventRoute("Counter", "CountChanged", typeof(int), payload =>
            {
                if (payload is int value && value == 7)
                {
                    callCount++;
                }
            });

            runtime.EmitComponentEvent("Counter", "CountChanged", 7);

            Assert.AreEqual(1, notificationCount);
            Assert.AreEqual(1, callCount);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CustomPropsComparer_ShouldUseTheSameRulesForBothConfigurationPaths(bool setAfterRegistration)
        {
            using var runtime = new CompositionRuntime();
            var viewModel = new TestViewModel();
            Func<TestProps, TestProps, bool> comparer = (previous, next) => previous.Value % 10 == next.Value % 10;

            runtime.TryRegisterComponent("Counter", new object(), viewModel,
                setAfterRegistration ? null : ComposableComponentHub.WrapPropsComparer(comparer));
            if (setAfterRegistration)
            {
                runtime.SetPropsComparer("Counter", comparer);
            }

            runtime.ApplyProps("Counter", new TestProps(1));
            runtime.ApplyProps("Counter", new TestProps(11));
            runtime.ApplyProps("Counter", new TestProps(2));

            Assert.AreEqual(2, viewModel.ApplyCount);
        }

        [Test]
        public void PropsComparerWrapper_ShouldPreserveReferenceNullAndFallbackRules()
        {
            var wrapped = ComposableComponentHub.WrapPropsComparer<TestProps>((previous, next) => previous.Value == next.Value);
            var props = new TestProps(1);

            Assert.IsTrue(wrapped(props, props));
            Assert.IsTrue(wrapped(null, null));
            Assert.IsFalse(wrapped(null, props));
            Assert.IsFalse(wrapped(props, null));
            Assert.IsTrue(wrapped(props, new TestProps(1)));
            Assert.IsFalse(wrapped(props, new TestProps(2)));
            Assert.IsTrue(wrapped(1, 1));
            Assert.IsFalse(wrapped(1, 2));
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
