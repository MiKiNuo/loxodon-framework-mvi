using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace MVI.Tests
{
    public class MviPersistenceRegressionTests
    {
        private const string Key = "persistence.regression";

        [Serializable]
        public sealed class PersistedState : IState
        {
            public int value;
            public bool forceUpdate;

            bool IState.IsUpdateNewState
            {
                get => forceUpdate;
                set => forceUpdate = value;
            }
        }

        private sealed class SnapshotSerializer : IStoreStateSerializer
        {
            private readonly StoreStateSnapshot _snapshot;

            public SnapshotSerializer(StoreStateSnapshot snapshot) => _snapshot = snapshot;
            public string SerializerId => _snapshot.SerializerId;
            public StoreStateSnapshot Serialize(IState state) => _snapshot;
            public IState Deserialize(StoreStateSnapshot snapshot) => null;
        }

        private sealed class ConfigurableMigrator : IStoreStateMigrator
        {
            public bool Prepared { get; set; }

            public bool TryMigrate(string key, StoreStateSnapshot source, out StoreStateSnapshot migrated)
            {
                migrated = Prepared ? source : null;
                return Prepared;
            }
        }

        private sealed class UnusedIntent : IIntent
        {
            public ValueTask<IMviResult> HandleIntentAsync(CancellationToken cancellationToken = default) => default;
        }

        private sealed class UnusedResult : IMviResult
        {
        }

        private sealed class RestoreStore : Store<PersistedState, UnusedIntent, UnusedResult>
        {
            public static IStoreStatePersistence ActivePersistence { get; set; }

            protected override IStoreStatePersistence Persistence => ActivePersistence;
            protected override string PersistenceKey => Key;
            protected override StoreProfile Profile => null;
            protected override PersistedState InitialState => new PersistedState();
            protected override PersistedState Reduce(UnusedResult result) => null;
        }

        [Test]
        public void FailedLoad_ShouldPreventDefaultStateSave()
        {
            var storage = SaveValidState();
            storage.TryRead(Key, out var originalBytes);
            var registry = new StateTypeRegistry();
            var reader = CreateJsonPersistence(storage, registry);
            Assert.IsFalse(reader.TryLoad(Key, out _));

            reader.Save(Key, new PersistedState());

            AssertPreserved(storage, originalBytes);
            Assert.AreEqual(0, registry.Count);
        }

        [Test]
        public void SuccessfulRetry_ShouldEnableSubsequentSave()
        {
            var storage = SaveValidState();
            var registry = new StateTypeRegistry();
            var reader = CreateJsonPersistence(storage, registry);
            Assert.IsFalse(reader.TryLoad(Key, out _));
            registry.Register(typeof(PersistedState));
            Assert.IsTrue(reader.TryLoad(Key, out var restored));
            Assert.AreEqual(7, ((PersistedState)restored).value);

            reader.Save(Key, new PersistedState { value = 9 });

            Assert.IsTrue(reader.TryLoad(Key, out var saved));
            Assert.AreEqual(9, ((PersistedState)saved).value);
        }

        [Test]
        public void ExplicitClear_ShouldEnableNewSave()
        {
            var storage = SaveValidState();
            var reader = CreateJsonPersistence(storage, new StateTypeRegistry());
            Assert.IsFalse(reader.TryLoad(Key, out _));

            reader.Clear(Key);
            Assert.IsFalse(storage.TryRead(Key, out _));
            reader.Save(Key, new PersistedState { value = 3 });

            Assert.IsTrue(reader.TryLoad(Key, out var saved));
            Assert.AreEqual(3, ((PersistedState)saved).value);
        }

        [Test]
        public void ConfigurationFailure_ShouldNotBlockAnotherKey()
        {
            var storage = SaveValidState();
            storage.TryRead(Key, out var originalBytes);
            var reader = CreateJsonPersistence(storage, new StateTypeRegistry());
            Assert.IsFalse(reader.TryLoad(Key, out _));

            reader.Save("another.key", new PersistedState { value = 11 });

            Assert.IsTrue(reader.TryLoad("another.key", out var saved));
            Assert.AreEqual(11, ((PersistedState)saved).value);
            reader.Save(Key, new PersistedState());
            AssertPreserved(storage, originalBytes);
        }

        [Test]
        public void StoreInitialization_ShouldNotOverwriteCustomRegistrySnapshot()
        {
            var storage = SaveValidState();
            storage.TryRead(Key, out var originalBytes);
            var registry = new StateTypeRegistry();
            var reader = CreateJsonPersistence(storage, registry);
            var previousPersistence = RestoreStore.ActivePersistence;
            RestoreStore.ActivePersistence = reader;
            try
            {
                using (var initialized = new RestoreStore())
                {
                    Assert.AreEqual(0, initialized.CurrentState.value);
                    AssertPreserved(storage, originalBytes);
                    Assert.AreEqual(0, registry.Count);
                }

                registry.Register(typeof(PersistedState));
                using (var restored = new RestoreStore())
                {
                    Assert.AreEqual(7, restored.CurrentState.value);
                    restored.UpdateState(new PersistedState { value = 9 });
                }

                Assert.IsTrue(reader.TryLoad(Key, out var saved));
                Assert.AreEqual(9, ((PersistedState)saved).value);
            }
            finally
            {
                RestoreStore.ActivePersistence = previousPersistence;
            }
        }

        [Test]
        public void FreshRegistry_ShouldPreserveSnapshotAndReportUnregisteredType()
        {
            var storage = SaveValidState();
            Assert.IsTrue(storage.TryRead(Key, out var originalBytes));
            var registry = new StateTypeRegistry();
            var reason = string.Empty;
            var reader = CreateJsonPersistence(storage, registry, new SerializedStoreStatePersistenceOptions
            {
                OnLoadFailed = (_, failure) => reason = failure
            });

            Assert.IsFalse(reader.TryLoad(Key, out var state));

            Assert.IsNull(state);
            Assert.AreEqual(0, registry.Count);
            StringAssert.Contains("not registered", reason);
            StringAssert.Contains(typeof(PersistedState).AssemblyQualifiedName, reason);
            AssertPreserved(storage, originalBytes);
        }

        [Test]
        public void RegisteringTypeAfterFailedLoad_ShouldRestoreOriginalSnapshot()
        {
            var storage = SaveValidState();
            storage.TryRead(Key, out var originalBytes);
            var registry = new StateTypeRegistry();
            var reader = CreateJsonPersistence(storage, registry);
            Assert.IsFalse(reader.TryLoad(Key, out _));

            registry.Register(typeof(PersistedState));

            Assert.IsTrue(reader.TryLoad(Key, out var restored));
            Assert.AreEqual(7, ((PersistedState)restored).value);
            AssertPreserved(storage, originalBytes);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void CorruptedJsonPayload_ShouldRespectCleanupOption(bool clearCorruptedData)
        {
            var registry = new StateTypeRegistry();
            var serializer = new JsonStoreStateSerializer(registry: registry);
            var snapshot = serializer.Serialize(new PersistedState { value = 7 })
                .With(payload: Encoding.UTF8.GetBytes("{invalid-json"));
            var storage = SaveSnapshot(snapshot);
            storage.TryRead(Key, out var originalBytes);
            var reader = CreateJsonPersistence(storage, registry, new SerializedStoreStatePersistenceOptions
            {
                ClearCorruptedDataOnLoadFailure = clearCorruptedData
            });

            Assert.IsFalse(reader.TryLoad(Key, out _));

            Assert.AreEqual(!clearCorruptedData, storage.TryRead(Key, out _));
            if (!clearCorruptedData)
            {
                AssertPreserved(storage, originalBytes);
            }
        }

        [Test]
        public void MissingSerializer_ShouldPreserveSnapshotUntilConfigured()
        {
            var storage = new InMemoryStoreStateStorage();
            var registry = new StateTypeRegistry();
            var legacySerializer = new JsonStoreStateSerializer(serializerId: "legacy-json", registry: registry);
            var writer = new SerializedStoreStatePersistence(storage, new[] { legacySerializer }, "legacy-json");
            writer.Save(Key, new PersistedState { value = 7 });
            storage.TryRead(Key, out var originalBytes);
            var reason = string.Empty;
            var reader = CreateJsonPersistence(storage, registry, new SerializedStoreStatePersistenceOptions
            {
                OnLoadFailed = (_, failure) => reason = failure
            });

            Assert.IsFalse(reader.TryLoad(Key, out _));

            StringAssert.Contains("Serializer not found: legacy-json", reason);
            AssertPreserved(storage, originalBytes);
            var configuredReader = new SerializedStoreStatePersistence(storage, new[] { legacySerializer }, "legacy-json");
            Assert.IsTrue(configuredReader.TryLoad(Key, out var restored));
            Assert.AreEqual(7, ((PersistedState)restored).value);
        }

        [Test]
        public void UnpreparedMigration_ShouldPreserveSnapshotAndAllowRetry()
        {
            var storage = SaveValidState();
            storage.TryRead(Key, out var originalBytes);
            var registry = RegisteredTypes();
            var migrator = new ConfigurableMigrator();
            var reason = string.Empty;
            var reader = CreateJsonPersistence(storage, registry, new SerializedStoreStatePersistenceOptions
            {
                OnLoadFailed = (_, failure) => reason = failure
            }, migrator);

            Assert.IsFalse(reader.TryLoad(Key, out _));

            StringAssert.Contains("Migration returned no snapshot", reason);
            AssertPreserved(storage, originalBytes);
            migrator.Prepared = true;
            Assert.IsTrue(reader.TryLoad(Key, out var restored));
            Assert.AreEqual(7, ((PersistedState)restored).value);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void UnpreparedVersionedMigrationStep_ShouldPreserveSnapshotAndAllowRetry(bool throwUntilPrepared)
        {
            var storage = SaveValidState();
            storage.TryRead(Key, out var originalBytes);
            var prepared = false;
            var migrator = new VersionedStoreStateMigrator().AddStep("json", 1, snapshot =>
            {
                if (!prepared)
                {
                    if (throwUntilPrepared)
                    {
                        throw new InvalidOperationException("Migration configuration is not prepared.");
                    }

                    return null;
                }

                return snapshot.With(schemaVersion: 2);
            });
            var reader = CreateJsonPersistence(storage, RegisteredTypes(), migrator: migrator);

            Assert.IsFalse(reader.TryLoad(Key, out _));

            AssertPreserved(storage, originalBytes);
            prepared = true;
            Assert.IsTrue(reader.TryLoad(Key, out var restored));
            Assert.AreEqual(7, ((PersistedState)restored).value);
        }

        [Test]
        public void MigrationWithoutApplicableStep_ShouldKeepExistingPassthroughBehavior()
        {
            var storage = SaveValidState();
            var reader = CreateJsonPersistence(storage, RegisteredTypes(), migrator: new VersionedStoreStateMigrator());

            Assert.IsTrue(reader.TryLoad(Key, out var restored));
            Assert.AreEqual(7, ((PersistedState)restored).value);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void BinaryUnknownType_ShouldPreserveSnapshot(bool compress)
        {
            var serializer = new BinaryStoreStateSerializer(compress: compress);
            var snapshot = serializer.Serialize(new PersistedState { value = 7 })
                .With(stateType: "Unregistered.State.FromAnotherModule");
            var storage = SaveSnapshot(snapshot);
            storage.TryRead(Key, out var originalBytes);
            var reader = new SerializedStoreStatePersistence(storage, new[] { serializer }, serializer.SerializerId);

            Assert.IsFalse(reader.TryLoad(Key, out _));

            AssertPreserved(storage, originalBytes);
        }

        [Test]
        public void FailureCallback_ShouldAllowAnotherThreadToClearBeforeReturning()
        {
            var storage = SaveValidState();
            var callbackCount = 0;
            SerializedStoreStatePersistence persistence = null;
            persistence = CreateJsonPersistence(storage, new StateTypeRegistry(), new SerializedStoreStatePersistenceOptions
            {
                OnLoadFailed = (key, _) =>
                {
                    callbackCount++;
                    var clearTask = Task.Run(() => persistence.Clear(key));
                    Assert.IsTrue(clearTask.Wait(TimeSpan.FromSeconds(5)), "Failure callback held the persistence lock while waiting for Clear.");
                }
            });

            Assert.IsFalse(persistence.TryLoad(Key, out _));

            Assert.AreEqual(1, callbackCount);
            Assert.IsFalse(storage.TryRead(Key, out _));
        }

        [Test]
        public void ThrowingDiagnostics_ShouldNotClearAnUnregisteredSnapshot()
        {
            var storage = SaveValidState();
            storage.TryRead(Key, out var originalBytes);
            var reader = CreateJsonPersistence(storage, new StateTypeRegistry(), new SerializedStoreStatePersistenceOptions
            {
                OnLoadFailed = (_, _) => throw new InvalidOperationException("diagnostic-failure")
            });

            Assert.Throws<InvalidOperationException>(() => reader.TryLoad(Key, out _));

            AssertPreserved(storage, originalBytes);
        }

        private static StateTypeRegistry RegisteredTypes()
        {
            var registry = new StateTypeRegistry();
            registry.Register(typeof(PersistedState));
            return registry;
        }

        private static InMemoryStoreStateStorage SaveValidState()
        {
            var storage = new InMemoryStoreStateStorage();
            CreateJsonPersistence(storage, new StateTypeRegistry()).Save(Key, new PersistedState { value = 7 });
            return storage;
        }

        private static InMemoryStoreStateStorage SaveSnapshot(StoreStateSnapshot snapshot)
        {
            var storage = new InMemoryStoreStateStorage();
            new SerializedStoreStatePersistence(storage, new[] { new SnapshotSerializer(snapshot) }, snapshot.SerializerId)
                .Save(Key, new PersistedState { value = 7 });
            return storage;
        }

        private static SerializedStoreStatePersistence CreateJsonPersistence(
            IStoreStateStorage storage,
            IStateTypeRegistry registry,
            SerializedStoreStatePersistenceOptions options = null,
            IStoreStateMigrator migrator = null)
        {
            return new SerializedStoreStatePersistence(storage, new[] { new JsonStoreStateSerializer(registry: registry) }, "json",
                migrator: migrator, options: options);
        }

        private static void AssertPreserved(IStoreStateStorage storage, byte[] originalBytes)
        {
            Assert.IsTrue(storage.TryRead(Key, out var storedBytes));
            CollectionAssert.AreEqual(originalBytes, storedBytes);
        }
    }
}
