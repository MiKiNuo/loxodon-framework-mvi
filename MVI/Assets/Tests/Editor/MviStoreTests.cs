using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MVI;
using NUnit.Framework;
using R3;
using UnityEngine.TestTools;

namespace MVI.Tests
{
    public class MviStoreTests
    {
        internal sealed class TestState : IState
        {
            public int Value { get; set; }
            public bool IsUpdateNewState { get; set; } = true;
        }

        private sealed class TestResult : IMviResult
        {
            public TestResult(int value)
            {
                Value = value;
            }

            public int Value { get; }
        }

        private interface ITestIntent : IIntent
        {
        }

        private sealed class SetValueIntent : ITestIntent
        {
            private readonly int value;

            public SetValueIntent(int value)
            {
                this.value = value;
            }

            public ValueTask<IMviResult> HandleIntentAsync(CancellationToken ct = default)
            {
                IMviResult result = new TestResult(value);
                return new ValueTask<IMviResult>(result);
            }
        }

        private sealed class ThrowIntent : ITestIntent
        {
            public ValueTask<IMviResult> HandleIntentAsync(CancellationToken ct = default)
            {
                throw new InvalidOperationException("boom");
            }
        }

        private sealed class TestEffect : IMviEffect
        {
            public TestEffect(string message)
            {
                Message = message;
            }

            public string Message { get; }
        }

        private sealed class TestStore : Store<TestState, ITestIntent, TestResult, TestEffect>
        {
            protected override TestState InitialState => new TestState { Value = 0, IsUpdateNewState = true };

            protected override TestState Reduce(TestResult result)
            {
                EmitEffect(new TestEffect($"v:{result.Value}"));
                return new TestState { Value = result.Value, IsUpdateNewState = true };
            }
        }

        internal sealed class MapState : IState
        {
            [MviMap("UserName")] public string Name { get; set; }
            [MviIgnore] public string Hidden { get; set; }
            public bool IsUpdateNewState { get; set; } = true;
        }

        internal sealed class MapViewModel : MviViewModel
        {
            public string UserName { get; set; }
            public string Hidden { get; set; }
        }

        private sealed class MapStore : Store<MapState, ITestIntent, TestResult>
        {
            protected override MapState Reduce(TestResult result)
            {
                return null;
            }

            public void Push(MapState state)
            {
                UpdateState(state);
            }
        }

        [UnityTest]
        public IEnumerator InitialState_IsEmitted()
        {
            var store = new TestStore();
            var states = new List<TestState>();
            store.State.Subscribe(states.Add);

            yield return null;

            Assert.IsNotEmpty(states);
            Assert.AreEqual(0, states[0].Value);
        }

        [UnityTest]
        public IEnumerator EmitIntent_UpdatesState_AndEmitsEffect()
        {
            var store = new TestStore();
            var states = new List<TestState>();
            var effects = new List<TestEffect>();

            store.State.Subscribe(states.Add);
            store.Effects.Subscribe(effect =>
            {
                if (effect is TestEffect typed)
                {
                    effects.Add(typed);
                }
            });

            store.EmitIntent(new SetValueIntent(5));

            yield return null;

            Assert.IsNotEmpty(states);
            Assert.AreEqual(5, states[states.Count - 1].Value);
            Assert.IsNotEmpty(effects);
            Assert.AreEqual("v:5", effects[effects.Count - 1].Message);
        }

        [UnityTest]
        public IEnumerator EmitIntent_ErrorEmitsErrorEffect()
        {
            var store = new TestStore();
            var errors = new List<MviErrorEffect>();

            store.Errors.Subscribe(errors.Add);
            store.EmitIntent(new ThrowIntent());

            yield return null;

            Assert.IsNotEmpty(errors);
            Assert.AreEqual("boom", errors[errors.Count - 1].Message);
        }

        [UnityTest]
        public IEnumerator MappingAttributes_Work()
        {
            var store = new MapStore();
            var viewModel = new MapViewModel();
            viewModel.BindStore(store, disposeStore: true);

            store.Push(new MapState { Name = "Alice", Hidden = "secret", IsUpdateNewState = true });

            yield return null;

            Assert.AreEqual("Alice", viewModel.UserName);
            Assert.IsNull(viewModel.Hidden);
        }
    }
}
