using System;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Jobs;
using Unity.Entities;
using Unity.Collections;
using ZG;
using Unity.Collections.LowLevel.Unsafe;
using System.Diagnostics;
using Unity.Transforms;

#region GameCampManager
[assembly: RegisterGenericJobType(typeof(EntityDataContainerSerialize<GameCampManagerShared.Serializer>))]
[assembly: RegisterGenericJobType(typeof(EntityDataContainerDeserialize<GameCampManagerShared.Deserializer>))]
//[assembly: EntityDataSerialize(typeof(GameCampManager), typeof(GameDataCampSerializationSystem))]
//[assembly: EntityDataDeserialize(typeof(GameCampManager), typeof(GameDataCampDeserializationSystem), (int)GameDataConstans.Version)]
#endregion

#region GameEntityCamp
//[assembly: RegisterGenericJobType(typeof(EntityDataComponentSerialize<ComponentDataSerializationSystem<GameEntityCamp>.Serializer, ComponentDataSerializationSystem<GameEntityCamp>.SerializerFactory>))]
//[assembly: RegisterGenericJobType(typeof(EntityDataComponentDeserialize<ComponentDataDeserializationSystem<GameEntityCamp>.Deserializer, ComponentDataDeserializationSystem<GameEntityCamp>.DeserializerFactory>))]
//[assembly: EntityDataSerialize(typeof(GameEntityCamp))]
//[assembly: EntityDataDeserialize(typeof(GameEntityCamp), (int)GameDataConstans.Version)]
#endregion

public struct GameCampManager
{
    private struct Group
    {
        public int id;
        public int count;
    }

    private UnsafePool<Group> __groups;
    private UnsafeHashMap<int, int> __groupCamps;

    public AllocatorManager.AllocatorHandle allocator => __groups.allocator;

    public GameCampManager(in AllocatorManager.AllocatorHandle allocator)
    {
        __groups = new UnsafePool<Group>(1, allocator);
        __groupCamps = new UnsafeHashMap<int, int>(1, allocator);
    }

    public void Dispose()
    {
        __groups.Dispose();
        __groupCamps.Dispose();
    }

    public int GetCamp(int groupID, int originCamp)
    {
        int source = originCamp;// - builtInCamps;
        if (groupID == 0)
        {
            Free(source);

            return Alloc();
        }

        if (!__groups.TryGetValue(source, out var group))
        {
            group.id = 0;
            group.count = 1;
        }

        if (!__groupCamps.TryGetValue(groupID, out int destination))
        {
            destination = source < 0 || group.count > 1 ? __groups.nextIndex : source;

            __groupCamps[groupID] = destination;
        }

        if (destination != source)
        {
            Free(source);

            if (__groups.TryGetValue(destination, out group))
                ++group.count;
            else
                group.count = 1;
        }

        group.id = groupID;
        __groups.Insert(destination, group);

        return destination;// + builtInCamps;
    }

    public int Alloc(int groupID)
    {
        Group group;
        group.id = groupID;
        group.count = 1;

        int nextIndex = __groups.nextIndex;
        __groups.Insert(nextIndex, group);

        return nextIndex;// + BuiltInCamps;
    }

    public int Alloc()
    {
        return Alloc(0);
    }

    /*public bool Free(int camp)
    {
        return __Free(camp - BuiltInCamps);
    }*/

    public bool Free(int camp)
    {
        Group group;
        if (__groups.TryGetValue(camp, out group))
        {
            if (--group.count > 0)
                __groups[camp] = group;
            else
                __groups.RemoveAt(camp);

            return true;
        }

        return false;
    }

    public void Serialize(ref NativeBuffer.Writer writer)
    {
        writer.Serialize(__groups);
        writer.Serialize(__groupCamps);
    }

    public void Deserialize(in UnsafeBlock block)
    {
        var reader = block.reader;
        reader.Deserialize(ref __groups);
        reader.Deserialize(ref __groupCamps);

        Group group, temp;
        int camp, length = __groups.length;
        for (int i = 0; i < length; ++i)
        {
            if (!__groups.TryGetValue(i, out group) || group.id == 0)
                continue;

            if (!__groupCamps.TryGetValue(group.id, out camp))
            {
                UnityEngine.Debug.LogError($"WTF Group ID: {group.id}");

                continue;
            }

            if (camp == i)
                continue;

            UnityEngine.Debug.LogError($"WTF Group ID: {group.id}, Source Camp: {i}, Destination Camp {camp}");

            if (__groups.TryGetValue(camp, out temp))
                temp.count += group.count;
            else
                temp.count = group.count;

            temp.id = group.id;

            __groups[camp] = temp;

            __groups.RemoveAt(i);
        }
    }
}

public struct GameCampManagerShared
{
    private struct Data
    {
        public GameCampManager value;

        public LookupJobManager lookupJobManager;
    }

    [NativeContainer]
    [NativeContainerIsReadOnly]
    public unsafe struct Serializer : IEntityDataContainerSerializer
    {
        [NativeDisableUnsafePtrRestriction]
        private GameCampManager* __manager;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        internal AtomicSafetyHandle m_Safety;

        internal static readonly SharedStatic<int> StaticSafetyID = SharedStatic<int>.GetOrCreate<Serializer>();
#endif

        public Serializer(ref GameCampManagerShared manager)
        {
            __manager = (GameCampManager*)UnsafeUtility.AddressOf(ref manager.__data->value);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            m_Safety = manager.m_Safety;

            CollectionHelper.SetStaticSafetyId<Serializer>(ref m_Safety, ref StaticSafetyID.Data);
#endif
        }

        public void Serialize(ref NativeBuffer.Writer writer)
        {
            __CheckRead();

            __manager->Serialize(ref writer);
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
        [NativeDisableUnsafePtrRestriction]
        private GameCampManager* __manager;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        internal AtomicSafetyHandle m_Safety;

        internal static readonly SharedStatic<int> StaticSafetyID = SharedStatic<int>.GetOrCreate<Deserializer>();
#endif

        public Deserializer(ref GameCampManagerShared manager)
        {
            __manager = (GameCampManager*)UnsafeUtility.AddressOf(ref manager.__data->value);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            m_Safety = manager.m_Safety;

            CollectionHelper.SetStaticSafetyId<Deserializer>(ref m_Safety, ref StaticSafetyID.Data);
#endif
        }

        public void Deserialize(in UnsafeBlock block)
        {
            __CheckWrite();

            __manager->Deserialize(block);
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private void __CheckWrite()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndBumpSecondaryVersion(m_Safety);
#endif
        }
    }

    public readonly int BuiltInCamps;

    private unsafe Data* __data;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
    internal AtomicSafetyHandle m_Safety;
#endif

    public unsafe ref LookupJobManager lookupJobManager => ref __data->lookupJobManager;

    public Serializer serializer => new Serializer(ref this);

    public Deserializer deserializer => new Deserializer(ref this);

    public unsafe GameCampManagerShared(in AllocatorManager.AllocatorHandle allocator, int builtInCamps = 32)
    {
        BuiltInCamps = builtInCamps;

        __data = AllocatorManager.Allocate<Data>(allocator);

        __data->value = new GameCampManager(allocator);
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

        var allocator = __data->value.allocator;

        __data->value.Dispose();

        AllocatorManager.Free(allocator, __data);

        __data = null;
    }

    public void CompleteReadWriteDependency()
    {
        lookupJobManager.CompleteReadWriteDependency();

        __CheckWrite();
    }

    public unsafe int GetCamp(int groupID, int originCamp)
    {
        CompleteReadWriteDependency();

        return __data->value.GetCamp(groupID, originCamp - BuiltInCamps) + BuiltInCamps;
    }

    public unsafe int Alloc(int groupID)
    {
        CompleteReadWriteDependency();

        return __data->value.Alloc(groupID) + BuiltInCamps;
    }

    public int Alloc()
    {
        return Alloc(0);
    }

    public unsafe bool Free(int camp)
    {
        CompleteReadWriteDependency();

        return __data->value.Free(camp - BuiltInCamps);
    }

    [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
    private void __CheckWrite()
    {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        AtomicSafetyHandle.CheckWriteAndBumpSecondaryVersion(m_Safety);
#endif
    }

}

[AutoCreateIn("Server"), BurstCompile, CreateAfter(typeof(GameDataStructChangeSystem)), UpdateInGroup(typeof(GameDataSystemGroup))]
public partial struct GameDataCampSystem : ISystem
{
    private struct Serialize
    {
        //public int minSerializableCamp;

        [ReadOnly]
        public NativeArray<Entity> entityArray;

        /*[ReadOnly]
        public NativeArray<GameEntityCamp> camps;*/

        [ReadOnly] 
        public NativeArray<GameOwner> owners;

        [ReadOnly] 
        public ComponentLookup<EntityDataSerializable> serializables;

        public EntityCommandQueue<EntityCommandStructChange>.ParallelWriter entityManager;

        public void Execute(int index)
        {
            /*if (camps[index].value < minSerializableCamp)
                return;*/
            
            //只往上追溯一阶
            if (!serializables.HasComponent(owners[index].entity))
                return;
            
            entityManager.Enqueue(EntityCommandStructChange.Create<EntityDataSerializable>(entityArray[index]));
        }
    }

    [BurstCompile]
    private struct SerializeEx : IJobChunk, IEntityCommandProducerJob
    {
        //public int minSerializableCamp;

        [ReadOnly]
        public EntityTypeHandle entityType;

        //[ReadOnly]
        //public ComponentTypeHandle<GameEntityCamp> campType;

        [ReadOnly] 
        public ComponentTypeHandle<GameOwner> ownerType;

        [ReadOnly] 
        public ComponentLookup<EntityDataSerializable> serializables;

        public EntityCommandQueue<EntityCommandStructChange>.ParallelWriter entityManager;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Serialize serialize;
            //serialize.minSerializableCamp = minSerializableCamp;
            serialize.entityArray = chunk.GetNativeArray(entityType);
            //serialize.camps = chunk.GetNativeArray(ref campType);
            serialize.owners = chunk.GetNativeArray(ref ownerType);
            serialize.serializables = serializables;
            serialize.entityManager = entityManager;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                serialize.Execute(i);
        }
    }

    private EntityQuery __group;

    private EntityTypeHandle __entityType;

    //private ComponentTypeHandle<GameEntityCamp> __campType;

    private ComponentTypeHandle<GameOwner> __ownerType;

    private ComponentLookup<EntityDataSerializable> __serializables;

    private EntityCommandPool<EntityCommandStructChange> __entityManager;

    public GameCampManagerShared manager
    {
        get;

        private set;
    }

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __group = builder
                .WithAll<GameOwner>()
                .WithNone<GameNonSerialized, EntityDataSerializable>()
                .WithOptions(EntityQueryOptions.IncludeDisabledEntities)
                .Build(ref state);

        __group.SetChangedVersionFilter(ComponentType.ReadOnly<GameOwner>());

        __entityType = state.GetEntityTypeHandle();

        __ownerType = state.GetComponentTypeHandle<GameOwner>(true);

        __serializables = state.GetComponentLookup<EntityDataSerializable>(true);

        __entityManager = state.WorldUnmanaged.GetExistingSystemUnmanaged<GameDataStructChangeSystem>().manager.addComponentPool;

        manager = new GameCampManagerShared(Allocator.Persistent, (int)GameDataConstans.BuiltInCamps);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        manager.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var entityManager = __entityManager.Create();

        SerializeEx serialize;
        //serialize.minSerializableCamp = manager.BuiltInCamps;
        serialize.entityType = __entityType.UpdateAsRef(ref state);
        serialize.ownerType = __ownerType.UpdateAsRef(ref state);
        serialize.serializables = __serializables.UpdateAsRef(ref state);
        //serialize.campType = __campType.UpdateAsRef(ref state);
        serialize.entityManager = entityManager.parallelWriter;

        var jobHandle = serialize.ScheduleParallelByRef(__group, state.Dependency);

        entityManager.AddJobHandleForProducer<SerializeEx>(jobHandle);

        state.Dependency = jobHandle;
    }
}

[BurstCompile,
    EntityDataSerializationSystem(typeof(GameCampManager)),
    CreateAfter(typeof(GameDataCampSystem)),
    CreateAfter(typeof(EntityDataSerializationInitializationSystem)),
    UpdateInGroup(typeof(EntityDataSerializationSystemGroup)), AutoCreateIn("Server")]
public partial struct GameDataCampSerializationContainerSystem : ISystem
{
    private EntityDataSerializationTypeHandle __typeHandle;
    private GameCampManagerShared __manager;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __typeHandle = new EntityDataSerializationTypeHandle(ref state);

        __manager = state.WorldUnmanaged.GetExistingSystemUnmanaged<GameDataCampSystem>().manager;
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        ref var lookupJobManager = ref __manager.lookupJobManager;
        state.Dependency = JobHandle.CombineDependencies(state.Dependency, lookupJobManager.readOnlyJobHandle);

        var serializer = __manager.serializer;

        __typeHandle.Update(ref serializer, ref state);

        lookupJobManager.AddReadOnlyDependency(state.Dependency);
    }
}

/*[BurstCompile,
    EntityDataSerializationSystem(typeof(GameEntityCamp)),
    CreateAfter(typeof(EntityDataSerializationInitializationSystem)),
    UpdateInGroup(typeof(EntityDataSerializationSystemGroup)), AutoCreateIn("Server")]
public partial struct GameDataEntityCampSerializationSystem : ISystem
{
    private EntityDataSerializationSystemCoreEx __core;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __core = EntityDataSerializationSystemCoreEx.Create<GameEntityCamp>(ref state);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        __core.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        __core.Update(ref state);
    }
}*/

[BurstCompile,
    EntityDataDeserializationSystem(typeof(GameCampManager), (int)GameDataConstans.Version),
    CreateAfter(typeof(EntityDataDeserializationContainerSystem)),
    CreateAfter(typeof(GameDataCampSystem)),
    UpdateInGroup(typeof(EntityDataDeserializationSystemGroup)), AutoCreateIn("Server")]
public partial struct GameDataCampDeserializationContainerSystem : ISystem
{
    private GameCampManagerShared __manager;
    private EntityDataDeserializationContainerSystemCore __core;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __manager = state.WorldUnmanaged.GetExistingSystemUnmanaged<GameDataCampSystem>().manager;

        __core = new EntityDataDeserializationContainerSystemCore(ref state);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        __core.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        state.Dependency = JobHandle.CombineDependencies(__manager.lookupJobManager.readWriteJobHandle, state.Dependency);

        var deserializer = __manager.deserializer;
        __core.Update(ref deserializer, ref state);

        __manager.lookupJobManager.readWriteJobHandle = state.Dependency;
    }
}

/*[BurstCompile,
    EntityDataDeserializationSystem(typeof(GameEntityCamp), (int)GameDataConstans.Version),
    CreateAfter(typeof(EntityDataDeserializationComponentSystem)),
    UpdateInGroup(typeof(EntityDataDeserializationSystemGroup)), AutoCreateIn("Server")]
public partial struct GameDataCampDeserializationSystem : ISystem
{
    private EntityDataDeserializationSystemCoreEx __core;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __core = EntityDataDeserializationSystemCoreEx.Create<GameEntityCamp>(ref state);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        __core.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        __core.Update(ref state);
    }
}*/
