using System;
using MVI;
using NUnit.Framework;

namespace MVI.Tests
{
    public sealed class MviViewModelLifecycleTests
    {
        private sealed class TestState : IState
        {
            public bool IsUpdateNewState { get; set; } = true;
        }

        private sealed class TestResult : IMviResult
        {
        }

        private sealed class TestEffect : IMviEffect
        {
        }

        private interface IOtherIntent : IIntent
        {
        }

        private sealed class PlainStore : Store<TestState, IIntent, TestResult>
        {
            protected override TestState InitialState => new TestState();
            protected override TestState Reduce(TestResult result) => CurrentState;
        }

        private sealed class EffectStore : Store<TestState, IIntent, TestResult, TestEffect>
        {
            protected override TestState InitialState => new TestState();
            protected override TestState Reduce(TestResult result) => CurrentState;
        }

        private sealed class OtherStore : Store<TestState, IOtherIntent, TestResult>
        {
            protected override TestState InitialState => new TestState();
            protected override TestState Reduce(TestResult result) => CurrentState;
        }

        private sealed class PlainViewModel : MviViewModel<TestState, IIntent, TestResult>
        {
            public Store<TestState, IIntent, TestResult> ExposedStore => Store;
        }

        private sealed class EffectViewModel : MviViewModel<TestState, IIntent, TestResult, TestEffect>
        {
            public Store<TestState, IIntent, TestResult, TestEffect> ExposedStore => Store;
        }

        private static MviViewModel CreateViewModel(bool effects) => effects ? new EffectViewModel() : new PlainViewModel();
        private static Store CreateStore(bool effects) => effects ? new EffectStore() : new PlainStore();

        private static Store TypedStore(MviViewModel viewModel) => viewModel is EffectViewModel effect
            ? effect.ExposedStore
            : ((PlainViewModel)viewModel).ExposedStore;

        private static IState TypedState(MviViewModel viewModel) => viewModel is EffectViewModel effect
            ? effect.CurrentState
            : ((PlainViewModel)viewModel).CurrentState;

        private static void BindTyped(MviViewModel viewModel, Store store, bool disposeStore)
        {
            if (viewModel is EffectViewModel effect)
            {
                effect.BindStore((Store<TestState, IIntent, TestResult, TestEffect>)store, disposeStore);
            }
            else
            {
                ((PlainViewModel)viewModel).BindStore((Store<TestState, IIntent, TestResult>)store, disposeStore);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void FirstBind_ShouldExposeTheSameStoreAndState(bool effects)
        {
            using var store = CreateStore(effects);
            using var viewModel = CreateViewModel(effects);
            BindTyped(viewModel, store, disposeStore: false);

            Assert.AreSame(store, TypedStore(viewModel));
            Assert.AreSame(store.CurrentState, TypedState(viewModel));
            Assert.AreSame(TypedState(viewModel), viewModel.CurrentState);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void BaseReferenceRebind_ShouldUpdateTheTypedViewAndDisposeTheOwnedStore(bool effects)
        {
            using var oldStore = CreateStore(effects);
            using var newStore = CreateStore(effects);
            using var viewModel = CreateViewModel(effects);
            BindTyped(viewModel, oldStore, disposeStore: true);

            viewModel.BindStore(newStore, disposeStore: false);

            Assert.IsTrue(oldStore.IsDisposed);
            Assert.AreSame(newStore, TypedStore(viewModel));
            Assert.AreSame(newStore.CurrentState, TypedState(viewModel));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void NullRebind_ShouldPreserveTheExistingBinding(bool effects)
        {
            using var store = CreateStore(effects);
            using var viewModel = CreateViewModel(effects);
            BindTyped(viewModel, store, disposeStore: true);

            Assert.Throws<ArgumentNullException>(() => BindTyped(viewModel, null, disposeStore: false));

            Assert.IsFalse(store.IsDisposed);
            Assert.AreSame(store, TypedStore(viewModel));
            Assert.AreSame(store.CurrentState, viewModel.CurrentState);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void WrongStoreType_ShouldBeRejectedBeforeDetachingTheExistingStore(bool effects)
        {
            using var store = CreateStore(effects);
            using var wrongStore = new OtherStore();
            using var viewModel = CreateViewModel(effects);
            BindTyped(viewModel, store, disposeStore: true);

            Assert.Throws<ArgumentException>(() => viewModel.BindStore(wrongStore, disposeStore: false));

            Assert.IsFalse(store.IsDisposed);
            Assert.IsFalse(wrongStore.IsDisposed);
            Assert.AreSame(store, TypedStore(viewModel));
            Assert.AreSame(store.CurrentState, TypedState(viewModel));
        }

        [Test]
        public void EffectViewModel_ShouldRejectAPlainStoreThroughTheThreeTypeParameterOverload()
        {
            using var store = new EffectStore();
            using var wrongStore = new PlainStore();
            using var viewModel = new EffectViewModel();
            viewModel.BindStore(store, disposeStore: true);
            MviViewModel<TestState, IIntent, TestResult> weakerView = viewModel;

            Assert.Throws<ArgumentException>(() => weakerView.BindStore(wrongStore, disposeStore: false));

            Assert.IsFalse(store.IsDisposed);
            Assert.AreSame(store, viewModel.ExposedStore);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Dispose_ShouldClearAllTypedViewsAndRespectStoreOwnership(bool effects)
        {
            using var store = CreateStore(effects);
            var viewModel = CreateViewModel(effects);
            BindTyped(viewModel, store, disposeStore: false);

            viewModel.Dispose();

            Assert.IsNull(TypedStore(viewModel));
            Assert.IsNull(TypedState(viewModel));
            Assert.IsNull(viewModel.CurrentState);
            Assert.IsFalse(store.IsDisposed);
        }
    }
}
