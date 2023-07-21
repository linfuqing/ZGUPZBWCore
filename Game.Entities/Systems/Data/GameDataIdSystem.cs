using System;
using Unity.Jobs;
using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;
using ZG;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Burst;
using System.Diagnostics;

#region GameIDManager
[assembly: RegisterGenericJobType(typeof(EntityDataContainerSerialize<GameIDManagerShared.Serializer>))]
[assembly: RegisterGenericJobType(typeof(EntityDataContainerDeserialize<GameIDManagerShared.Deserializer>))]
//[assembly: EntityDataSerialize(typeof(GameIDManager), typeof(GameDataIdSerializationSystem))]
[assembly: EntityDataDeserialize(typeof(GameIDManager), typeof(GameDataIDDeserializationSystem), (int)GameDataConstans.Version)]
#endregion

[EntityDataTypeName("GameIdManager")]
public struct GameIDManager
{
    public UnsafeHashMap<Hash128, int> __ids;
    public UnsafeHashMap<int, Hash128> __guids;
    
    //public Deserializer deserializer => new Deserializer(ref this);

    public GameIDManager(in AllocatorManager.AllocatorHandle allocator)
    {
        __ids = new UnsafeHashMap<Hash128, int>(1, allocator);
        __guids = new UnsafeHashMap<int, Hash128>(1, allocator);
    }

    public void Dispose()
    {
        __ids.Dispose();
        __guids.Dispose();
    }

    public Hash128 GetOrCreateGUID(int id)
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

    public bool TryGetGUID(int id, out Hash128 guid)
    {
        return __guids.TryGetValue(id, out guid);
    }

    public bool GetID(Hash128 guid, out int id)
    {
        return __ids.TryGetValue(guid, out id);
    }

    public unsafe void Serialize(ref NativeBuffer.Writer writer, in SharedHashMap<Hash128, int>.Reader entityIndices)
    {
        var keyValueArrays = __ids.GetKeyValueArrays(Allocator.Temp);

        int length = keyValueArrays.Keys.Length, entityIndex;
        var indices = new NativeList<int>(length, Allocator.Temp);
        for (int i = 0; i < length; ++i)
        {
            if (entityIndices.TryGetValue(keyValueArrays.Keys[i], out entityIndex))
            {
                indices.Add(entityIndex);

                continue;
            }

            keyValueArrays.Keys[i] = keyValueArrays.Keys[--length];
            keyValueArrays.Values[i--] = keyValueArrays.Values[length];
        }

        writer.Write(length);
        writer.Write(indices.AsArray().Slice());
        writer.Write(keyValueArrays.Values.Slice(0, length));

        indices.Dispose();

        keyValueArrays.Dispose();
    }

    public void Deserialize(in UnsafeBlock block, in NativeArray<Hash128>.ReadOnly guids)
    {
        __ids.Clear();
        __guids.Clear();

        var reader = block.reader;
        int length = reader.Read<int>();

        if (length > 0)
        {
            __ids.Capacity = math.max(__ids.Capacity, length);
            __guids.Capacity = math.max(__guids.Capacity, length);

            var keys = reader.ReadArray<int>(length);
            var values = reader.ReadArray<int>(length);

            int id;
            Hash128 guid;
            for (int i = 0; i < length; ++i)
            {
                guid = guids[keys[i]];
                id = values[i];

                if (__ids.ContainsKey(guid))
                {
                    UnityEngine.Debug.LogError($"Missing {id} : {guid}");

                    continue;
                }

                __ids[guid] = id;

                __guids.Add(id, guid);
            }
        }
    }
}

public struct GameIDManagerShared
{
    private struct Data
    {
        public GameIDManager value;

        public LookupJobManager lookupJobManager;
    }

    [NativeContainer]
    [NativeContainerIsReadOnly]
    public unsafe struct Serializer : IEntityDataContainerSerializer
    {
        public readonly SharedHashMap<Hash128, int>.Reader EntityIndices;

        [NativeDisableUnsafePtrRestriction]
        private GameIDManager* __manager;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        internal AtomicSafetyHandle m_Safety;

        internal static readonly SharedStatic<int> StaticSafetyID = SharedStatic<int>.GetOrCreate<Serializer>();
#endif


        public Serializer(ref GameIDManagerShared manager, SharedHashMap<Hash128, int>.Reader entityIndices)
        {
            EntityIndices = entityIndices;

            __manager = (GameIDManager*)UnsafeUtility.AddressOf(ref manager.__data->value);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            m_Safety = manager.m_Safety;

            CollectionHelper.SetStaticSafetyId<Serializer>(ref m_Safety, ref StaticSafetyID.Data);
#endif
        }

        public void Serialize(ref NativeBuffer.Writer writer)
        {
            __CheckRead();

            __manager->Serialize(ref writer, EntityIndices);
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private void __CheckRead()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif
        }
    }

    [NativeContainer]
    public unsafe struct Deserializer : IEntityDataContainerDeserializer
    {
        public readonly NativeArray<Hash128>.ReadOnly GUIDs;

        [NativeDisableUnsafePtrRestriction]
        private GameIDManager* __manager;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        internal AtomicSafetyHandle m_Safety;

        internal static readonly SharedStatic<int> StaticSafetyID = SharedStatic<int>.GetOrCreate<Deserializer>();
#endif

        public Deserializer(ref GameIDManagerShared manager, in NativeArray<Hash128>.ReadOnly guids)
        {
            GUIDs = guids;

            __manager = (GameIDManager*)UnsafeUtility.AddressOf(ref manager.__data->value);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            m_Safety = manager.m_Safety;

            CollectionHelper.SetStaticSafetyId<Deserializer>(ref m_Safety, ref StaticSafetyID.Data);
#endif
        }

        public void Deserialize(in UnsafeBlock block)
        {
            __CheckWrite();

            __manager->Deserialize(block, GUIDs);
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private void __CheckWrite()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndBumpSecondaryVersion(m_Safety);
#endif
        }
    }

    public readonly AllocatorManager.AllocatorHandle Allocator;

    private unsafe Data* __data;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
    internal AtomicSafetyHandle m_Safety;
#endif

    public unsafe ref LookupJobManager lookupJobManager => ref __data->lookupJobManager;

    public unsafe GameIDManagerShared(in AllocatorManager.AllocatorHandle allocator)
    {
        Allocator = allocator;

        __data = AllocatorManager.Allocate<Data>(allocator);

        __data->value = new GameIDManager(allocator);
        __data->lookupJobManager = default;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        m_Safety = CollectionHelper.CreateSafetyHandle(allocator);
#endif
    }

    public unsafe void Dispose()
    {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        CollectionHelper.DisposeSafetyHandle(ref m_Safety);
#endif

        __data->value.Dispose();

        AllocatorManager.Free(Allocator, __data);

        __data = null;
    }

    public void CompleteReadWriteDependency()
    {
        lookupJobManager.CompleteReadWriteDependency();

        __CheckWrite();
    }

    public unsafe Hash128 GetOrCreateGUID(int id)
    {
        CompleteReadWriteDependency();

        return __data->value.GetOrCreateGUID(id);
    }

    public unsafe bool TryGetGUID(int id, out Hash128 guid)
    {
        CompleteReadWriteDependency();

        return __data->value.TryGetGUID(id, out guid);
    }

    public unsafe bool GetID(Hash128 guid, out int id)
    {
        CompleteReadWriteDependency();

        return __data->value.GetID(guid, out id);
    }

    public Serializer AsSerializer(in SharedHashMap<Hash128, int>.Reader entityIndices)
    {
        return new Serializer(ref this, entityIndices);
    }

    public Deserializer AsDeserializer(in NativeArray<Hash128>.ReadOnly guids)
    {
        return new Deserializer(ref this, guids);
    }

    [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
    private void __CheckWrite()
    {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        AtomicSafetyHandle.CheckWriteAndBumpSecondaryVersion(m_Safety);
#endif
    }

}

[BurstCompile, AutoCreateIn("Server")]
public partial struct GameDataIDSystem : ISystem
{
    public GameIDManagerShared manager
    {
        get;

        private set;
    }

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        manager = new GameIDManagerShared(Allocator.Persistent);

        state.Enabled = false;
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        manager.Dispose();
    }

    public void OnUpdate(ref SystemState state)
    {
        throw new NotImplementedException();
    }
}

[BurstCompile,
    EntityDataSerializationSystem(typeof(GameIDManager)),
    CreateAfter(typeof(GameDataIDSystem)),
    CreateAfter(typeof(EntityDataSerializationInitializationSystem)),
    UpdateInGroup(typeof(EntityDataSerializationSystemGroup)), AutoCreateIn("Server")]
public partial struct GameDataIDSerializationContainerSystem : ISystem
{
    private EntityDataSerializationTypeHandle __typeHandle;
    private SharedHashMap<Hash128, int> __entityIndices;
    private GameIDManagerShared __manager;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __typeHandle = EntityDataSerializationUtility.GetTypeHandle(ref state);

        var world = state.WorldUnmanaged;

        __entityIndices = world.GetExistingSystemUnmanaged<EntityDataSerializationInitializationSystem>().entityIndices;

        __manager = world.GetExistingSystemUnmanaged<GameDataIDSystem>().manager;
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        ref var entityIndicesJobManager = ref __entityIndices.lookupJobManager;
        ref var managerJobManager = ref __manager.lookupJobManager;
        state.Dependency = JobHandle.CombineDependencies(state.Dependency, entityIndicesJobManager.readOnlyJobHandle, managerJobManager.readOnlyJobHandle);

        var serializer = __manager.AsSerializer(__entityIndices.reader);

        EntityDataSerializationUtility.Update(__typeHandle, ref serializer, ref state);

        var jobHandle = state.Dependency;

        entityIndicesJobManager.AddReadOnlyDependency(jobHandle);

        managerJobManager.AddReadOnlyDependency(jobHandle);
    }
}

[DisableAutoCreation]
public partial class GameDataIDDeserializationSystem : EntityDataDeserializationContainerSystem<GameIDManagerShared.Deserializer>
{
    private GameIDManagerShared __manager;

    protected override void OnCreate()
    {
        base.OnCreate();

        __manager = World.GetOrCreateSystemUnmanaged<GameDataIDSystem>().manager;
    }

    protected override void OnUpdate()
    {
        Dependency = JobHandle.CombineDependencies(Dependency, __manager.lookupJobManager.readWriteJobHandle);

        base.OnUpdate();

        __manager.lookupJobManager.readWriteJobHandle = Dependency;
    }

    protected override GameIDManagerShared.Deserializer _Create(ref JobHandle jobHandle) => __manager.AsDeserializer(systemGroup.initializationSystem.guids.AsReadOnly());
}
