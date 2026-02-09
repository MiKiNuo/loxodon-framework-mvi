using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using UnityEngine;

namespace MVI
{
    /// <summary>
    /// 持久化快照：包含版本、序列化器标识、状态类型与原始负载。
    /// </summary>
    public sealed class StoreStateSnapshot
    {
        public StoreStateSnapshot(int schemaVersion, string serializerId, string stateType, byte[] payload, long savedAtUtcTicks = 0)
        {
            SchemaVersion = schemaVersion;
            SerializerId = serializerId;
            StateType = stateType;
            Payload = payload;
            SavedAtUtcTicks = savedAtUtcTicks <= 0 ? DateTime.UtcNow.Ticks : savedAtUtcTicks;
        }

        public int SchemaVersion { get; }

        public string SerializerId { get; }

        public string StateType { get; }

        public byte[] Payload { get; }

        public long SavedAtUtcTicks { get; }

        public StoreStateSnapshot With(int? schemaVersion = null, string serializerId = null, string stateType = null, byte[] payload = null, long? savedAtUtcTicks = null)
        {
            return new StoreStateSnapshot(
                schemaVersion ?? SchemaVersion,
                serializerId ?? SerializerId,
                stateType ?? StateType,
                payload ?? Payload,
                savedAtUtcTicks ?? SavedAtUtcTicks);
        }
    }

    /// <summary>
    /// 状态序列化器接口（JSON / 二进制等）。
    /// </summary>
    public interface IStoreStateSerializer
    {
        string SerializerId { get; }

        StoreStateSnapshot Serialize(IState state);

        IState Deserialize(StoreStateSnapshot snapshot);
    }

    /// <summary>
    /// 持久化底层存储接口（可接内存、PlayerPrefs、文件系统等）。
    /// </summary>
    public interface IStoreStateStorage
    {
        bool TryRead(string key, out byte[] bytes);

        void Write(string key, byte[] bytes);

        void Clear(string key);
    }

    /// <summary>
    /// 支持命名空间管理的存储接口。
    /// </summary>
    public interface INamespacedStoreStateStorage : IStoreStateStorage
    {
        IEnumerable<string> EnumerateKeys(string prefix = null);

        int ClearByPrefix(string prefix);
    }

    /// <summary>
    /// 可选加密接口。
    /// </summary>
    public interface IStoreStateEncryptor
    {
        byte[] Encrypt(string key, byte[] bytes);

        byte[] Decrypt(string key, byte[] bytes);
    }

    /// <summary>
    /// 快照版本迁移接口（例如 v1 -> v2）。
    /// </summary>
    public interface IStoreStateMigrator
    {
        bool TryMigrate(string key, StoreStateSnapshot source, out StoreStateSnapshot migrated);
    }

    /// <summary>
    /// 序列化持久化选项：用于控制损坏数据处理与诊断回调。
    /// </summary>
    public sealed class SerializedStoreStatePersistenceOptions
    {
        /// <summary>
        /// 加载失败时是否自动清理损坏快照，避免反复读取失败。
        /// </summary>
        public bool ClearCorruptedDataOnLoadFailure { get; set; } = true;

        /// <summary>
        /// 加载失败回调（key, reason）。
        /// </summary>
        public Action<string, string> OnLoadFailed { get; set; }
    }

    /// <summary>
    /// 基于快照存储/序列化/加密/迁移组合而成的持久化实现。
    /// </summary>
    public sealed class SerializedStoreStatePersistence : IStoreStatePersistence
    {
        [Serializable]
        private sealed class StoreStateEnvelope
        {
            public int schemaVersion;
            public string serializerId;
            public string stateType;
            public string payloadBase64;
            public long savedAtUtcTicks;
        }

        private readonly IStoreStateStorage _storage;
        private readonly Dictionary<string, IStoreStateSerializer> _serializers;
        private readonly string _defaultSerializerId;
        private readonly IStoreStateEncryptor _encryptor;
        private readonly IStoreStateMigrator _migrator;
        private readonly SerializedStoreStatePersistenceOptions _options;

        public SerializedStoreStatePersistence(
            IStoreStateStorage storage,
            IEnumerable<IStoreStateSerializer> serializers,
            string defaultSerializerId,
            IStoreStateEncryptor encryptor = null,
            IStoreStateMigrator migrator = null,
            SerializedStoreStatePersistenceOptions options = null)
        {
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _serializers = new Dictionary<string, IStoreStateSerializer>(StringComparer.Ordinal);

            if (serializers != null)
            {
                foreach (var serializer in serializers)
                {
                    if (serializer == null || string.IsNullOrWhiteSpace(serializer.SerializerId))
                    {
                        continue;
                    }

                    _serializers[serializer.SerializerId] = serializer;
                }
            }

            if (_serializers.Count == 0)
            {
                throw new ArgumentException("At least one serializer is required.", nameof(serializers));
            }

            _defaultSerializerId = string.IsNullOrWhiteSpace(defaultSerializerId)
                ? throw new ArgumentException("defaultSerializerId is required.", nameof(defaultSerializerId))
                : defaultSerializerId;

            if (!_serializers.ContainsKey(_defaultSerializerId))
            {
                throw new ArgumentException($"Serializer '{_defaultSerializerId}' was not found.", nameof(defaultSerializerId));
            }

            _encryptor = encryptor;
            _migrator = migrator;
            _options = options ?? new SerializedStoreStatePersistenceOptions();
        }

        public bool TryLoad(string key, out IState state)
        {
            state = null;
            if (string.IsNullOrWhiteSpace(key) || !_storage.TryRead(key, out var bytes) || bytes == null || bytes.Length == 0)
            {
                return false;
            }

            try
            {
                if (_encryptor != null)
                {
                    bytes = _encryptor.Decrypt(key, bytes);
                }

                var snapshot = DeserializeEnvelope(bytes);
                if (snapshot == null)
                {
                    return FailLoad(key, "Invalid envelope.");
                }

                if (_migrator != null && _migrator.TryMigrate(key, snapshot, out var migrated) && migrated != null)
                {
                    snapshot = migrated;
                }

                if (string.IsNullOrWhiteSpace(snapshot.SerializerId) || !_serializers.TryGetValue(snapshot.SerializerId, out var serializer))
                {
                    return FailLoad(key, $"Serializer not found: {snapshot.SerializerId}");
                }

                state = serializer.Deserialize(snapshot);
                if (state == null)
                {
                    return FailLoad(key, "Serializer returned null state.");
                }

                return true;
            }
            catch (Exception ex)
            {
                return FailLoad(key, ex.Message);
            }
        }

        public void Save(string key, IState state)
        {
            if (string.IsNullOrWhiteSpace(key) || state == null)
            {
                return;
            }

            var serializer = _serializers[_defaultSerializerId];
            var snapshot = serializer.Serialize(state);
            if (snapshot == null || snapshot.Payload == null || snapshot.Payload.Length == 0)
            {
                return;
            }

            var bytes = SerializeEnvelope(snapshot);
            if (_encryptor != null)
            {
                bytes = _encryptor.Encrypt(key, bytes);
            }

            _storage.Write(key, bytes);
        }

        public void Clear(string key)
        {
            _storage.Clear(key);
        }

        private bool FailLoad(string key, string reason)
        {
            _options.OnLoadFailed?.Invoke(key, reason ?? "unknown");
            if (_options.ClearCorruptedDataOnLoadFailure)
            {
                _storage.Clear(key);
            }

            return false;
        }

        private static byte[] SerializeEnvelope(StoreStateSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return Array.Empty<byte>();
            }

            var envelope = new StoreStateEnvelope
            {
                schemaVersion = snapshot.SchemaVersion,
                serializerId = snapshot.SerializerId,
                stateType = snapshot.StateType,
                payloadBase64 = Convert.ToBase64String(snapshot.Payload),
                savedAtUtcTicks = snapshot.SavedAtUtcTicks
            };

            var json = JsonUtility.ToJson(envelope);
            return Encoding.UTF8.GetBytes(json);
        }

        private static StoreStateSnapshot DeserializeEnvelope(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
            {
                return null;
            }

            var json = Encoding.UTF8.GetString(bytes);
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            var envelope = JsonUtility.FromJson<StoreStateEnvelope>(json);
            if (envelope == null || string.IsNullOrWhiteSpace(envelope.serializerId) || string.IsNullOrWhiteSpace(envelope.payloadBase64))
            {
                return null;
            }

            byte[] payload;
            try
            {
                payload = Convert.FromBase64String(envelope.payloadBase64);
            }
            catch
            {
                return null;
            }

            return new StoreStateSnapshot(
                envelope.schemaVersion,
                envelope.serializerId,
                envelope.stateType,
                payload,
                envelope.savedAtUtcTicks);
        }
    }

    /// <summary>
    /// JSON 序列化器（默认建议使用）。
    /// </summary>
    public sealed class JsonStoreStateSerializer : IStoreStateSerializer
    {
        private static readonly Dictionary<string, Type> StateTypeCache = new(StringComparer.Ordinal);
        private static readonly object StateTypeCacheSyncRoot = new();

        public JsonStoreStateSerializer(int schemaVersion = 1, string serializerId = "json")
        {
            SchemaVersion = schemaVersion <= 0 ? 1 : schemaVersion;
            SerializerId = string.IsNullOrWhiteSpace(serializerId) ? "json" : serializerId;
        }

        public int SchemaVersion { get; }

        public string SerializerId { get; }

        public StoreStateSnapshot Serialize(IState state)
        {
            if (state == null)
            {
                return null;
            }

            var stateType = state.GetType();
            var json = JsonUtility.ToJson(state);
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            return new StoreStateSnapshot(
                SchemaVersion,
                SerializerId,
                stateType.AssemblyQualifiedName,
                Encoding.UTF8.GetBytes(json));
        }

        public IState Deserialize(StoreStateSnapshot snapshot)
        {
            if (snapshot == null || snapshot.Payload == null || snapshot.Payload.Length == 0)
            {
                return null;
            }

            var stateType = ResolveStateType(snapshot.StateType);
            if (stateType == null)
            {
                return null;
            }

            var json = Encoding.UTF8.GetString(snapshot.Payload);
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                return JsonUtility.FromJson(json, stateType) as IState;
            }
            catch
            {
                return null;
            }
        }

        private static Type ResolveStateType(string assemblyQualifiedName)
        {
            if (string.IsNullOrWhiteSpace(assemblyQualifiedName))
            {
                return null;
            }

            lock (StateTypeCacheSyncRoot)
            {
                if (StateTypeCache.TryGetValue(assemblyQualifiedName, out var cached))
                {
                    return cached;
                }
            }

            var type = Type.GetType(assemblyQualifiedName);
            if (type != null)
            {
                CacheResolvedType(assemblyQualifiedName, type);
                return type;
            }

            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (var i = 0; i < assemblies.Length; i++)
            {
                type = assemblies[i].GetType(assemblyQualifiedName);
                if (type != null)
                {
                    CacheResolvedType(assemblyQualifiedName, type);
                    return type;
                }
            }

            CacheResolvedType(assemblyQualifiedName, null);
            return null;
        }

        private static void CacheResolvedType(string key, Type type)
        {
            lock (StateTypeCacheSyncRoot)
            {
                StateTypeCache[key] = type;
            }
        }
    }

    /// <summary>
    /// 二进制序列化器：内部将 JSON 编码为字节，并支持可选 GZip 压缩。
    /// </summary>
    public sealed class BinaryStoreStateSerializer : IStoreStateSerializer
    {
        private readonly JsonStoreStateSerializer _jsonSerializer;

        public BinaryStoreStateSerializer(int schemaVersion = 1, bool compress = true, string serializerId = "binary")
        {
            SchemaVersion = schemaVersion <= 0 ? 1 : schemaVersion;
            Compress = compress;
            SerializerId = string.IsNullOrWhiteSpace(serializerId) ? "binary" : serializerId;
            _jsonSerializer = new JsonStoreStateSerializer(SchemaVersion, serializerId: "json-inner");
        }

        public int SchemaVersion { get; }

        public bool Compress { get; }

        public string SerializerId { get; }

        public StoreStateSnapshot Serialize(IState state)
        {
            var jsonSnapshot = _jsonSerializer.Serialize(state);
            if (jsonSnapshot?.Payload == null || jsonSnapshot.Payload.Length == 0)
            {
                return null;
            }

            var body = Compress ? CompressBytes(jsonSnapshot.Payload) : jsonSnapshot.Payload;
            var payload = new byte[body.Length + 1];
            payload[0] = Compress ? (byte)1 : (byte)0;
            Buffer.BlockCopy(body, 0, payload, 1, body.Length);

            return new StoreStateSnapshot(
                SchemaVersion,
                SerializerId,
                jsonSnapshot.StateType,
                payload);
        }

        public IState Deserialize(StoreStateSnapshot snapshot)
        {
            if (snapshot?.Payload == null || snapshot.Payload.Length <= 1)
            {
                return null;
            }

            try
            {
                var compressed = snapshot.Payload[0] == 1;
                var body = new byte[snapshot.Payload.Length - 1];
                Buffer.BlockCopy(snapshot.Payload, 1, body, 0, body.Length);
                var jsonBytes = compressed ? DecompressBytes(body) : body;
                var jsonSnapshot = snapshot.With(serializerId: "json-inner", payload: jsonBytes);
                return _jsonSerializer.Deserialize(jsonSnapshot);
            }
            catch
            {
                return null;
            }
        }

        private static byte[] CompressBytes(byte[] bytes)
        {
            using var output = new MemoryStream();
            using (var gzip = new GZipStream(output, CompressionMode.Compress, leaveOpen: true))
            {
                gzip.Write(bytes, 0, bytes.Length);
            }

            return output.ToArray();
        }

        private static byte[] DecompressBytes(byte[] bytes)
        {
            using var input = new MemoryStream(bytes);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            gzip.CopyTo(output);
            return output.ToArray();
        }
    }

    /// <summary>
    /// 内存存储后端（用于测试或运行时临时缓存）。
    /// </summary>
    public sealed class InMemoryStoreStateStorage : INamespacedStoreStateStorage
    {
        private readonly Dictionary<string, byte[]> _store = new(StringComparer.Ordinal);

        public bool TryRead(string key, out byte[] bytes)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                bytes = null;
                return false;
            }

            if (!_store.TryGetValue(key, out var cached) || cached == null)
            {
                bytes = null;
                return false;
            }

            bytes = new byte[cached.Length];
            Buffer.BlockCopy(cached, 0, bytes, 0, cached.Length);
            return true;
        }

        public void Write(string key, byte[] bytes)
        {
            if (string.IsNullOrWhiteSpace(key) || bytes == null)
            {
                return;
            }

            var cloned = new byte[bytes.Length];
            Buffer.BlockCopy(bytes, 0, cloned, 0, bytes.Length);
            _store[key] = cloned;
        }

        public void Clear(string key)
        {
            if (!string.IsNullOrWhiteSpace(key))
            {
                _store.Remove(key);
            }
        }

        public IEnumerable<string> EnumerateKeys(string prefix = null)
        {
            if (string.IsNullOrWhiteSpace(prefix))
            {
                return _store.Keys.ToArray();
            }

            return _store.Keys.Where(key => key.StartsWith(prefix, StringComparison.Ordinal)).ToArray();
        }

        public int ClearByPrefix(string prefix)
        {
            var keys = EnumerateKeys(prefix).ToArray();
            for (var i = 0; i < keys.Length; i++)
            {
                _store.Remove(keys[i]);
            }

            return keys.Length;
        }
    }

    /// <summary>
    /// PlayerPrefs 存储后端（将快照写为 Base64 字符串）。
    /// </summary>
    public sealed class PlayerPrefsStoreStateStorage : INamespacedStoreStateStorage
    {
        [Serializable]
        private sealed class KeyIndexEnvelope
        {
            public string[] keys;
        }

        private const string IndexSuffix = ".__index__";
        private readonly string _keyPrefix;

        public PlayerPrefsStoreStateStorage(string keyPrefix = "MVI.STATE")
        {
            _keyPrefix = string.IsNullOrWhiteSpace(keyPrefix) ? "MVI.STATE" : keyPrefix;
        }

        public bool TryRead(string key, out byte[] bytes)
        {
            var storageKey = BuildStorageKey(key);
            if (string.IsNullOrWhiteSpace(storageKey) || !PlayerPrefs.HasKey(storageKey))
            {
                bytes = null;
                return false;
            }

            var base64 = PlayerPrefs.GetString(storageKey, string.Empty);
            if (string.IsNullOrWhiteSpace(base64))
            {
                bytes = null;
                return false;
            }

            try
            {
                bytes = Convert.FromBase64String(base64);
                return true;
            }
            catch
            {
                bytes = null;
                return false;
            }
        }

        public void Write(string key, byte[] bytes)
        {
            var storageKey = BuildStorageKey(key);
            if (string.IsNullOrWhiteSpace(storageKey) || bytes == null)
            {
                return;
            }

            PlayerPrefs.SetString(storageKey, Convert.ToBase64String(bytes));
            AddKeyToIndex(key);
            PlayerPrefs.Save();
        }

        public void Clear(string key)
        {
            var storageKey = BuildStorageKey(key);
            if (string.IsNullOrWhiteSpace(storageKey))
            {
                return;
            }

            PlayerPrefs.DeleteKey(storageKey);
            RemoveKeyFromIndex(key);
            PlayerPrefs.Save();
        }

        public IEnumerable<string> EnumerateKeys(string prefix = null)
        {
            var index = LoadIndex();
            if (index.Count == 0)
            {
                return Array.Empty<string>();
            }

            var result = new List<string>(index.Count);
            foreach (var key in index)
            {
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(prefix) && !key.StartsWith(prefix, StringComparison.Ordinal))
                {
                    continue;
                }

                var storageKey = BuildStorageKey(key);
                if (!string.IsNullOrWhiteSpace(storageKey) && PlayerPrefs.HasKey(storageKey))
                {
                    result.Add(key);
                }
            }

            return result;
        }

        public int ClearByPrefix(string prefix)
        {
            var keys = EnumerateKeys(prefix).ToArray();
            for (var i = 0; i < keys.Length; i++)
            {
                var storageKey = BuildStorageKey(keys[i]);
                if (!string.IsNullOrWhiteSpace(storageKey))
                {
                    PlayerPrefs.DeleteKey(storageKey);
                }
            }

            if (keys.Length > 0)
            {
                var index = LoadIndex();
                for (var i = 0; i < keys.Length; i++)
                {
                    index.Remove(keys[i]);
                }

                SaveIndex(index);
                PlayerPrefs.Save();
            }

            return keys.Length;
        }

        private string BuildStorageKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return null;
            }

            return $"{_keyPrefix}.{key}";
        }

        private string BuildIndexStorageKey()
        {
            return $"{_keyPrefix}{IndexSuffix}";
        }

        private HashSet<string> LoadIndex()
        {
            var indexStorageKey = BuildIndexStorageKey();
            if (string.IsNullOrWhiteSpace(indexStorageKey) || !PlayerPrefs.HasKey(indexStorageKey))
            {
                return new HashSet<string>(StringComparer.Ordinal);
            }

            var json = PlayerPrefs.GetString(indexStorageKey, string.Empty);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new HashSet<string>(StringComparer.Ordinal);
            }

            try
            {
                var envelope = JsonUtility.FromJson<KeyIndexEnvelope>(json);
                if (envelope?.keys == null || envelope.keys.Length == 0)
                {
                    return new HashSet<string>(StringComparer.Ordinal);
                }

                return new HashSet<string>(envelope.keys.Where(key => !string.IsNullOrWhiteSpace(key)), StringComparer.Ordinal);
            }
            catch
            {
                return new HashSet<string>(StringComparer.Ordinal);
            }
        }

        private void SaveIndex(HashSet<string> keys)
        {
            var indexStorageKey = BuildIndexStorageKey();
            if (string.IsNullOrWhiteSpace(indexStorageKey))
            {
                return;
            }

            var normalized = (keys ?? new HashSet<string>(StringComparer.Ordinal))
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(key => key, StringComparer.Ordinal)
                .ToArray();
            var envelope = new KeyIndexEnvelope { keys = normalized };
            var json = JsonUtility.ToJson(envelope);
            PlayerPrefs.SetString(indexStorageKey, json);
        }

        private void AddKeyToIndex(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            var index = LoadIndex();
            if (index.Add(key))
            {
                SaveIndex(index);
            }
        }

        private void RemoveKeyFromIndex(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            var index = LoadIndex();
            if (index.Remove(key))
            {
                SaveIndex(index);
            }
        }
    }

    /// <summary>
    /// 文件存储后端（按 key 落盘为独立文件，支持原子替换写入）。
    /// </summary>
    public sealed class FileStoreStateStorage : INamespacedStoreStateStorage
    {
        private readonly string _rootDirectory;
        private readonly string _fileExtension;

        public FileStoreStateStorage(string rootDirectory = null, string fileExtension = ".mvi")
        {
            var fallbackRoot = Path.Combine(Path.GetTempPath(), "mvi-store");
            _rootDirectory = string.IsNullOrWhiteSpace(rootDirectory)
                ? (string.IsNullOrWhiteSpace(Application.persistentDataPath)
                    ? fallbackRoot
                    : Path.Combine(Application.persistentDataPath, "mvi-store"))
                : rootDirectory;

            _fileExtension = string.IsNullOrWhiteSpace(fileExtension)
                ? ".mvi"
                : (fileExtension.StartsWith(".") ? fileExtension : "." + fileExtension);
        }

        public bool TryRead(string key, out byte[] bytes)
        {
            var path = BuildFilePath(key);
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                bytes = null;
                return false;
            }

            try
            {
                bytes = File.ReadAllBytes(path);
                return bytes != null && bytes.Length > 0;
            }
            catch
            {
                bytes = null;
                return false;
            }
        }

        public void Write(string key, byte[] bytes)
        {
            var path = BuildFilePath(key);
            if (string.IsNullOrWhiteSpace(path) || bytes == null)
            {
                return;
            }

            EnsureDirectory(path);
            var tempPath = path + ".tmp";
            File.WriteAllBytes(tempPath, bytes);

            if (!File.Exists(path))
            {
                File.Move(tempPath, path);
                return;
            }

            try
            {
                File.Replace(tempPath, path, null);
            }
            catch
            {
                File.Copy(tempPath, path, overwrite: true);
                File.Delete(tempPath);
            }
        }

        public void Clear(string key)
        {
            var path = BuildFilePath(key);
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return;
            }

            try
            {
                File.Delete(path);
            }
            catch
            {
                // ignore
            }
        }

        public IEnumerable<string> EnumerateKeys(string prefix = null)
        {
            if (string.IsNullOrWhiteSpace(_rootDirectory) || !Directory.Exists(_rootDirectory))
            {
                return Array.Empty<string>();
            }

            var result = new List<string>();
            foreach (var filePath in EnumerateFilePaths())
            {
                if (!TryDecodeKeyFromPath(filePath, out var key))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(prefix) && !key.StartsWith(prefix, StringComparison.Ordinal))
                {
                    continue;
                }

                result.Add(key);
            }

            return result;
        }

        public int ClearByPrefix(string prefix)
        {
            var keys = EnumerateKeys(prefix).ToArray();
            for (var i = 0; i < keys.Length; i++)
            {
                Clear(keys[i]);
            }

            return keys.Length;
        }

        private string BuildFilePath(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return null;
            }

            var fileName = EncodeKeyForFileName(key);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return null;
            }

            return Path.Combine(_rootDirectory, fileName + _fileExtension);
        }

        private IEnumerable<string> EnumerateFilePaths()
        {
            return Directory.EnumerateFiles(_rootDirectory, "*" + _fileExtension, SearchOption.TopDirectoryOnly);
        }

        private bool TryDecodeKeyFromPath(string filePath, out string key)
        {
            key = null;
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return false;
            }

            var fileName = Path.GetFileNameWithoutExtension(filePath);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return false;
            }

            return TryDecodeKeyFromFileName(fileName, out key);
        }

        private static string EncodeKeyForFileName(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return string.Empty;
            }

            var bytes = Encoding.UTF8.GetBytes(key);
            var base64 = Convert.ToBase64String(bytes);
            return base64
                .Replace('+', '-')
                .Replace('/', '_')
                .TrimEnd('=');
        }

        private static bool TryDecodeKeyFromFileName(string fileName, out string key)
        {
            key = null;
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return false;
            }

            try
            {
                var normalized = fileName
                    .Replace('-', '+')
                    .Replace('_', '/');
                var padding = normalized.Length % 4;
                if (padding > 0)
                {
                    normalized = normalized.PadRight(normalized.Length + (4 - padding), '=');
                }

                var bytes = Convert.FromBase64String(normalized);
                key = Encoding.UTF8.GetString(bytes);
                return !string.IsNullOrWhiteSpace(key);
            }
            catch
            {
                return false;
            }
        }

        private static void EnsureDirectory(string filePath)
        {
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }
    }

    /// <summary>
    /// 空加密器（不做加解密）。
    /// </summary>
    public sealed class PassthroughStoreStateEncryptor : IStoreStateEncryptor
    {
        public byte[] Encrypt(string key, byte[] bytes)
        {
            return bytes;
        }

        public byte[] Decrypt(string key, byte[] bytes)
        {
            return bytes;
        }
    }

    /// <summary>
    /// 简单 XOR 加密器（示例用途，不建议用于高强度安全场景）。
    /// </summary>
    public sealed class XorStoreStateEncryptor : IStoreStateEncryptor
    {
        private readonly byte[] _secret;

        public XorStoreStateEncryptor(string secret)
            : this(Encoding.UTF8.GetBytes(string.IsNullOrEmpty(secret) ? "mvi-default-secret" : secret))
        {
        }

        public XorStoreStateEncryptor(byte[] secret)
        {
            _secret = (secret == null || secret.Length == 0) ? Encoding.UTF8.GetBytes("mvi-default-secret") : secret;
        }

        public byte[] Encrypt(string key, byte[] bytes)
        {
            return ApplyXor(bytes, key);
        }

        public byte[] Decrypt(string key, byte[] bytes)
        {
            return ApplyXor(bytes, key);
        }

        private byte[] ApplyXor(byte[] bytes, string key)
        {
            if (bytes == null || bytes.Length == 0)
            {
                return Array.Empty<byte>();
            }

            var result = new byte[bytes.Length];
            var keyBytes = string.IsNullOrWhiteSpace(key) ? _secret : Encoding.UTF8.GetBytes(key);
            for (var i = 0; i < bytes.Length; i++)
            {
                var secretByte = _secret[i % _secret.Length];
                var keyByte = keyBytes[i % keyBytes.Length];
                result[i] = (byte)(bytes[i] ^ secretByte ^ keyByte);
            }

            return result;
        }
    }

    /// <summary>
    /// 版本迁移器：按 (serializerId, fromVersion) 注册迁移步骤并自动串联。
    /// </summary>
    public sealed class VersionedStoreStateMigrator : IStoreStateMigrator
    {
        private readonly Dictionary<string, Func<StoreStateSnapshot, StoreStateSnapshot>> _steps = new(StringComparer.Ordinal);

        public VersionedStoreStateMigrator AddStep(string serializerId, int fromVersion, Func<StoreStateSnapshot, StoreStateSnapshot> migration)
        {
            if (string.IsNullOrWhiteSpace(serializerId) || fromVersion <= 0 || migration == null)
            {
                return this;
            }

            _steps[BuildKey(serializerId, fromVersion)] = migration;
            return this;
        }

        public bool TryMigrate(string key, StoreStateSnapshot source, out StoreStateSnapshot migrated)
        {
            migrated = source;
            if (source == null)
            {
                return false;
            }

            var migratedAny = false;
            var guard = 0;
            while (guard < 64 && _steps.TryGetValue(BuildKey(migrated.SerializerId, migrated.SchemaVersion), out var step))
            {
                guard++;
                var next = step(migrated);
                if (next == null)
                {
                    break;
                }

                migrated = next;
                migratedAny = true;
            }

            return migratedAny;
        }

        private static string BuildKey(string serializerId, int version)
        {
            return $"{serializerId}::{version}";
        }
    }

    /// <summary>
    /// 持久化工厂：快速创建常见持久化组合。
    /// </summary>
    public static class StoreStatePersistenceFactory
    {
        public static string CreateNamespacedKey(string keyNamespace, string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return string.Empty;
            }

            if (string.IsNullOrWhiteSpace(keyNamespace))
            {
                return key;
            }

            return $"{keyNamespace.Trim()}.{key}";
        }

        public static IStoreStatePersistence CreateJsonPersistence(
            IStoreStateStorage storage,
            int schemaVersion = 1,
            IStoreStateEncryptor encryptor = null,
            IStoreStateMigrator migrator = null)
        {
            var serializer = new JsonStoreStateSerializer(schemaVersion);
            return new SerializedStoreStatePersistence(
                storage,
                new[] { serializer },
                defaultSerializerId: serializer.SerializerId,
                encryptor: encryptor,
                migrator: migrator);
        }

        public static IStoreStatePersistence CreateBinaryPersistence(
            IStoreStateStorage storage,
            int schemaVersion = 1,
            bool compress = true,
            IStoreStateEncryptor encryptor = null,
            IStoreStateMigrator migrator = null)
        {
            var serializer = new BinaryStoreStateSerializer(schemaVersion, compress);
            return new SerializedStoreStatePersistence(
                storage,
                new[] { serializer },
                defaultSerializerId: serializer.SerializerId,
                encryptor: encryptor,
                migrator: migrator);
        }
    }
}
