using System;
using Loxodon.Framework.Views;
using MVI.Composed;
using NUnit.Framework;
using UnityEngine;

namespace MVI.Tests
{
    public class ComposedWindowTests
    {
        private sealed class TestWindow : ComposedWindowBase
        {
            public void AddRoute(Action<int> handler)
            {
                AddEventRoute("Counter", "CountChanged", typeof(int), payload => handler((int)payload));
            }

            public void Emit(int value)
            {
                EmitComponentEvent("Counter", "CountChanged", value);
            }

            protected override void OnCompose(IBundle bundle)
            {
            }
        }

        [Test]
        public void EventRoute_ShouldDispatchExactlyOnceToHandler()
        {
            var gameObject = new GameObject("TestComposedWindow");
            try
            {
                var window = gameObject.AddComponent<TestWindow>();
                var callCount = 0;
                var notificationCount = 0;
                window.ComponentEventRaised += _ => notificationCount++;
                window.AddRoute(payload =>
                {
                    Assert.AreEqual(3, payload);
                    callCount++;
                });

                window.Emit(3);

                Assert.AreEqual(1, notificationCount);
                Assert.AreEqual(1, callCount);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }
    }
}
