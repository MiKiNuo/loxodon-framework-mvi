using System.Collections;
using MVI;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace MVI.Tests
{
#if !UNITY_5_3_OR_NEWER
    [NonParallelizable]
#endif
    public sealed class MviMappingTests
    {
        private sealed class TestState : IState
        {
            public int Value { get; set; }
            public bool IsUpdateNewState { get; set; }
        }

        private sealed class TestViewModel : MviViewModel
        {
            public int Value { get; set; }
        }

        private static class NonMatchingMapper
        {
            public static bool TryMap(IState state, MviViewModel viewModel) => false;
        }

        private static bool DoNotMap(IState state, MviViewModel viewModel) => false;

        private static bool MapValue(IState state, MviViewModel viewModel)
        {
            if (state is not TestState source || viewModel is not TestViewModel target)
            {
                return false;
            }

            target.Value = source.Value;
            return true;
        }

        private static bool MapDoubleValue(IState state, MviViewModel viewModel)
        {
            if (!MapValue(state, viewModel))
            {
                return false;
            }

            ((TestViewModel)viewModel).Value *= 2;
            return true;
        }

        [SetUp]
        [TearDown]
        public void ResetOwnMapper() => MviStateMapper.RegisterMapper(DoNotMap);

        [Test]
        public void LateRegistration_ShouldMapAfterAnEarlierMiss()
        {
            var state = new TestState { Value = 7 };
            using var viewModel = new TestViewModel();
            Assert.IsFalse(MviStateMapper.TryMap(state, viewModel));

            MviStateMapper.RegisterMapper(MapValue);

            Assert.IsTrue(MviStateMapper.TryMap(state, viewModel));
            Assert.AreEqual(7, viewModel.Value);
        }

        [Test]
        public void Replacement_ShouldUseTheCurrentDelegate()
        {
            var state = new TestState { Value = 7 };
            using var viewModel = new TestViewModel();
            MviStateMapper.RegisterMapper(MapValue);
            Assert.IsTrue(MviStateMapper.TryMap(state, viewModel));
            Assert.AreEqual(7, viewModel.Value);

            MviStateMapper.RegisterMapper(MapDoubleValue);

            Assert.IsTrue(MviStateMapper.TryMap(state, viewModel));
            Assert.AreEqual(14, viewModel.Value);
        }

        [Test]
        public void UnmatchedMapper_ShouldNotPreventAnotherMapperFromHandlingThePair()
        {
            MviStateMapper.RegisterMapper(NonMatchingMapper.TryMap);
            MviStateMapper.RegisterMapper(MapValue);
            using var viewModel = new TestViewModel();

            Assert.IsTrue(MviStateMapper.TryMap(new TestState { Value = 9 }, viewModel));
            Assert.AreEqual(9, viewModel.Value);
        }

        [Test]
        public void NullArguments_ShouldNotInvokeAMapper()
        {
            MviStateMapper.RegisterMapper(MapValue);
            MviStateMapper.RegisterMapper(null);
            using var viewModel = new TestViewModel();

            Assert.IsFalse(MviStateMapper.TryMap(null, viewModel));
            Assert.IsFalse(MviStateMapper.TryMap(new TestState(), null));
        }

        [UnityTest]
        public IEnumerator GeneratedMapper_ShouldSyncTheFirstBoundStateAndLaterUpdates()
        {
            using var store = new MappingIntegrationStore();
            using var viewModel = new MappingIntegrationViewModel();
            viewModel.BindStore(store, disposeStore: false);
            yield return null;

            Assert.AreEqual(7, viewModel.Count);
            Assert.IsNull(viewModel.Hidden);

            store.UpdateState(new MappingIntegrationState { Value = 12, Hidden = "secret" });
            yield return null;

            Assert.AreEqual(12, viewModel.Count);
            Assert.IsNull(viewModel.Hidden);
        }
    }

    public sealed class MappingIntegrationState : IState
    {
        [MviMap("Count")]
        public int Value { get; set; }

        [MviIgnore]
        public string Hidden { get; set; }

        public bool IsUpdateNewState { get; set; } = true;
    }

    public sealed class MappingIntegrationViewModel : MviViewModel<MappingIntegrationState, IIntent, MappingIntegrationResult>
    {
        public int Count { get; set; }
        public string Hidden { get; set; }
    }

    public sealed class MappingIntegrationResult : IMviResult
    {
    }

    public sealed class MappingIntegrationStore : Store<MappingIntegrationState, IIntent, MappingIntegrationResult>
    {
        protected override MappingIntegrationState InitialState => new MappingIntegrationState { Value = 7, Hidden = "initial secret" };
        protected override MappingIntegrationState Reduce(MappingIntegrationResult result) => CurrentState;
    }
}
