using FairyGUI;
using MVI.FairyGUI;
using MVI.FairyGUI.Composed;
using NUnit.Framework;
using UnityEngine;

namespace MVI.Tests
{
    public class FairyRootOwnershipTests
    {
        private sealed class CountingRoot : GComponent
        {
            public int DisposeCount { get; private set; }

            public override void Dispose()
            {
                DisposeCount++;
                base.Dispose();
            }
        }

        private sealed class TestComposedView : ComposedFairyViewBase
        {
            public GComponent TestRoot { get; set; }
            public bool UsePanel { get; set; }

            protected override bool AutoCreateView => false;
            protected override bool PreferUIPanel => UsePanel;
            protected override bool AddToGRoot => false;
            protected override string PackageName => "Test";
            protected override string ComponentName => "Root";

            protected override GComponent GetPanelRoot() => TestRoot;
            protected override GComponent CreateRoot() => TestRoot;

            public GComponent GetRoot() => EnsureRoot();
            public void Close() => OnDestroy();

            protected override void OnCompose()
            {
            }
        }

        private sealed class TestMviView : MviFairyView
        {
            public GComponent TestRoot { get; set; }
            public bool UsePanel { get; set; }

            protected override bool AutoCreateView => false;
            protected override bool PreferUIPanel => UsePanel;
            protected override bool AddToGRoot => false;
            protected override string PackageName => "Test";
            protected override string ComponentName => "Root";

            protected override GComponent GetPanelRoot() => TestRoot;
            protected override GComponent CreateRoot() => TestRoot;

            public GComponent GetRoot() => EnsureRoot();
            public void Close() => OnDestroy();

            public override void Bind()
            {
            }
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void ComposedView_ShouldReleaseOnlyRootsItCreated(bool hasPanel, bool preferPanel)
        {
            var gameObject = new GameObject("TestComposedRootOwnership");
            var root = new CountingRoot();
            try
            {
                if (hasPanel)
                {
                    gameObject.AddComponent<UIPanel>();
                }

                var view = gameObject.AddComponent<TestComposedView>();
                view.TestRoot = root;
                view.UsePanel = preferPanel;
                Assert.AreSame(root, view.GetRoot());

                // Ownership follows the root acquisition, even if configuration changes later.
                view.UsePanel = !preferPanel;
                Assert.AreSame(root, view.GetRoot());
                view.Close();
                view.Close();

                Assert.AreEqual(hasPanel && preferPanel ? 0 : 1, root.DisposeCount);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
                if (!root.isDisposed)
                {
                    root.Dispose();
                }
            }
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void MviView_ShouldReleaseOnlyRootsItCreated(bool hasPanel, bool preferPanel)
        {
            var gameObject = new GameObject("TestMviRootOwnership");
            var root = new CountingRoot();
            try
            {
                if (hasPanel)
                {
                    gameObject.AddComponent<UIPanel>();
                }

                var view = gameObject.AddComponent<TestMviView>();
                view.TestRoot = root;
                view.UsePanel = preferPanel;
                Assert.AreSame(root, view.GetRoot());

                view.UsePanel = !preferPanel;
                Assert.AreSame(root, view.GetRoot());
                view.Close();
                view.Close();

                Assert.AreEqual(hasPanel && preferPanel ? 0 : 1, root.DisposeCount);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
                if (!root.isDisposed)
                {
                    root.Dispose();
                }
            }
        }
    }
}
