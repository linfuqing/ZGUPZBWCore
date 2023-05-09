using System;
using Unity.Jobs;
using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;
using ZG;

#region GameIdManager
[assembly: RegisterGenericJobType(typeof(EntityDataContainerSerialize<GameIdManager.Serializer>))]
[assembly: RegisterGenericJobType(typeof(EntityDataContainerDeserialize<GameIdManager.Deserializer>))]
[assembly: EntityDataSerialize(typeof(GameIdManager), typeof(GameDataIdSerializationSystem))]
[assembly: EntityDataDeserialize(typeof(GameIdManager), typeof(GameDataIdDeserializationSystem), (int)GameDataConstans.Version)]
#endregion

public struct GameIdManager
{
    public struct Serializer : IEntityDataContainerSerializer
    {
        [ReadOnly]
        private NativeParallelHashMap<Hash128, int> __ids;
        [ReadOnly]
        private NativeParallelHashMap<Hash128, int> __entityIndices;

        public Serializer(in GameIdManager manager, in NativeParallelHashMap<Hash128, int> entityIndices)
        {
            __ids = manager.__ids;
            __entityIndices = entityIndices;
        }

        public void Serialize(ref NativeBuffer.Writer writer)
        {
            var keyValueArrays = __ids.GetKeyValueArrays(Allocator.Temp);

            int length = keyValueArrays.Keys.Length, entityIndex;
            var entityIndices = new NativeList<int>(length, Allocator.Temp);
            for (int i = 0; i < length; ++i)
            {
                if (__entityIndices.TryGetValue(keyValueArrays.Keys[i], out entityIndex))
                {
                    entityIndices.Add(entityIndex);

                    continue;
                }

                keyValueArrays.Keys[i] = keyValueArrays.Keys[--length];
                keyValueArrays.Values[i--] = keyValueArrays.Values[length];
            }

            writer.Write(length);
            writer.Write(entityIndices.AsArray().Slice());
            writer.Write(keyValueArrays.Values.Slice(0, length));

            entityIndices.Dispose();

            keyValueArrays.Dispose();
        }
    }

    public struct Deserializer : IEntityDataContainerDeserializer
    {
        [ReadOnly]
        private NativeArray<Hash128> __guids;
        private NativeParallelHashMap<int, Hash128> __idsToGuids;
        private NativeParallelHashMap<Hash128, int> __guidsToIds;

        public Deserializer(in GameIdManager manager, in NativeArray<Hash128> guids)
        {
            __guids = guids;
            __idsToGuids = manager.__guids;
            __guidsToIds = manager.__ids;
        }

        public void Deserialize(in UnsafeBlock block)
        {
            __idsToGuids.Clear();
            __guidsToIds.Clear();

            var reader = block.reader;
            int length = reader.Read<int>();

            if (length > 0)
            {
                __idsToGuids.Capacity = math.max(__idsToGuids.Capacity, length);
                __guidsToIds.Capacity = math.max(__guidsToIds.Capacity, length);

                var keys = reader.ReadArray<int>(length);
                var values = reader.ReadArray<int>(length);

                int id;
                Hash128 guid;
                for (int i = 0; i < length; ++i)
                {
                    guid = __guids[keys[i]];
                    id = values[i];

                    if (__guidsToIds.ContainsKey(guid))
                    {
                        UnityEngine.Debug.LogError($"Missing {id} : {guid}");

                        continue;
                    }

                    __guidsToIds[guid] = id;

                    __idsToGuids.Add(id, guid);
                }
            }
        }
    }

    public NativeParallelHashMap<Hash128, int> __ids;
    public NativeParallelHashMap<int, Hash128> __guids;

    public GameIdManager(Allocator allocator)
    {
        __ids = new NativeParallelHashMap<Hash128, int>(1, allocator);
        __guids = new NativeParallelHashMap<int, Hash128>(1, allocator);
    }

    public void Dispose()
    {
        __ids.Dispose();
        __guids.Dispose();
    }

    public Hash128 GetOrCreateGuid(int id)
    {
        Hash128 guid;
        if (__guids.TryGetValue(id, out guid))
            return guid;

        do
        {
            guid = Guid.NewGuid().ToHash128();
        } while (__ids.ContainsKey(guid));

        __ids[guid] = id;

        __guids[id] = guid;

        return guid;
    }

    public bool TryGetGuid(int id, out Hash128 guid)
    {
        return __guids.TryGetValue(id, out guid);
    }

    public bool GetId(Hash128 guid, out int id)
    {
        return __ids.TryGetValue(guid, out id);
    }

    public Serializer AsSerializer(in NativeParallelHashMap<Hash128, int> entityIndices)
    {
        return new Serializer(this, entityIndices);
    }

    public Deserializer AsDeserializer(in NativeArray<Hash128> guids)
    {
        return new Deserializer(this, guids);
    }
}

[AutoCreateIn("Server")]
public partial class GameIDSystem : LookupSystem
{
    public GameIdManager manager
    {
        get;

        private set;
    }

    public GameIdManager GetManagerReadOnly()
    {
        _lookupJobManager.CompleteReadOnlyDependency();

        return manager;
    }

    public GameIdManager GetManagerReadWrite()
    {
        _lookupJobManager.CompleteReadWriteDependency();

        return manager;
    }

    protected override void OnCreate()
    {
        base.OnCreate();

        manager = new GameIdManager(Allocator.Persistent);

        Enabled = false;
    }

    protected override void OnDestroy()
    {
        manager.Dispose();

        base.OnDestroy();
    }

    protected override void _Update()
    {
        throw new NotImplementedException();
    }
}

[DisableAutoCreation]
public partial class GameDataIdSerializationSystem : EntityDataSerializationContainerSystem<GameIdManager.Serializer>
{
    private GameIDSystem __idSystem;

    protected override void OnCreate()
    {
        base.OnCreate();

        __idSystem = World.GetOrCreateSystemManaged<GameIDSystem>();
    }

    protected override void OnUpdate()
    {
        var initializationSystem = systemGroup.initializationSystem;
        Dependency = JobHandle.CombineDependencies(Dependency, __idSystem.readOnlyJobHandle, initializationSystem.readOnlyJobHandle);

        base.OnUpdate();

        var jobHandle = Dependency;

        __idSystem.AddReadOnlyDependency(jobHandle);

        initializationSystem.AddReadOnlyDependency(jobHandle);
    }

    protected override GameIdManager.Serializer _Get() => __idSystem.manager.AsSerializer(systemGroup.initializationSystem.entityIndices);
}

[DisableAutoCreation]
public partial class GameDataIdDeserializationSystem : EntityDataDeserializationContainerSystem<GameIdManager.Deserializer>
{
    private GameIDSystem __idSystem;

    protected override void OnCreate()
    {
        base.OnCreate();

        __idSystem = World.GetOrCreateSystemManaged<GameIDSystem>();
    }

    protected override void OnUpdate()
    {
        Dependency = JobHandle.CombineDependencies(Dependency, __idSystem.readWriteJobHandle);

        base.OnUpdate();

        __idSystem.readWriteJobHandle = Dependency;
    }

    protected override GameIdManager.Deserializer _Create(ref JobHandle jobHandle) => __idSystem.manager.AsDeserializer(systemGroup.initializationSystem.guids);
}
