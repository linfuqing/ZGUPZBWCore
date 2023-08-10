using Unity.Jobs;
using Unity.Entities;
using Unity.Collections;
using ZG;
using Unity.Burst;

#region GameItemTime
[assembly: RegisterGenericJobType(typeof(EntityDataComponentDeserialize<
    GameDataItemDeserializationSystemCore<GameItemTime, GameItemTimeInitSystem.Initializer>.Deserializer,
    GameDataItemDeserializationSystemCore<GameItemTime, GameItemTimeInitSystem.Initializer>.DeserializerFactory>))]

[assembly: RegisterEntityCommandProducerJob(typeof(GameDataItemDeserializationSystemCore<GameItemTime, GameItemTimeInitSystem.Initializer>))]

//[assembly: EntityDataSerialize(typeof(GameItemTime))]
//[assembly: EntityDataDeserialize(typeof(GameItemTime), typeof(GameDataItemDeserializationSystem<GameItemTime, GameItemTimeInitSystem.Initializer, GameItemTimeInitSystem>), (int)GameDataConstans.Version)]
#endregion

#region GameItemDurability
[assembly: RegisterGenericJobType(typeof(EntityDataComponentDeserialize<
    GameDataItemDeserializationSystemCore<GameItemDurability, GameItemDurabilityInitSystem.Initializer>.Deserializer,
    GameDataItemDeserializationSystemCore<GameItemDurability, GameItemDurabilityInitSystem.Initializer>.DeserializerFactory>))]

[assembly: RegisterEntityCommandProducerJob(typeof(GameDataItemDeserializationSystemCore<GameItemDurability, GameItemDurabilityInitSystem.Initializer>))]

//[assembly: EntityDataSerialize(typeof(GameItemDurability))]
//[assembly: EntityDataDeserialize(typeof(GameItemDurability), typeof(GameDataItemDeserializationSystem<GameItemDurability, GameItemDurabilityInitSystem.Initializer, GameItemDurabilityInitSystem>), (int)GameDataConstans.Version)]
#endregion

/*#region GameItemName
[assembly: RegisterGenericJobType(typeof(GameDataItemDeserialize<
    GameItemName,
    GameDataItemDeserializationSystem<GameItemName, GameItemNameInitSystem.Initializer, GameItemNameInitSystem>.Deserializer,
    GameItemNameInitSystem.Initializer>))]

[assembly: RegisterGenericJobType(typeof(EntityDataComponentSerialize<ComponentDataSerializationSystem<GameItemName>.Serializer, ComponentDataSerializationSystem<GameItemName>.SerializerFactory>))]
[assembly: RegisterGenericJobType(typeof(EntityDataComponentDeserialize<
    GameDataItemDeserializer<GameItemName, GameDataItemDeserializationSystem<GameItemName, GameItemNameInitSystem.Initializer, GameItemNameInitSystem>.Deserializer>,
    GameDataItemDeserializerFactory<GameItemName, GameDataItemDeserializationSystem<GameItemName, GameItemNameInitSystem.Initializer, GameItemNameInitSystem>.Deserializer>>))]

[assembly: EntityDataSerialize(typeof(GameItemName))]
[assembly: EntityDataDeserialize(typeof(GameItemName), typeof(GameDataItemDeserializationSystem<GameItemName, GameItemNameInitSystem.Initializer, GameItemNameInitSystem>), (int)GameDataConstans.Version)]
#endregion

#region GameItemVariant
[assembly: RegisterGenericJobType(typeof(GameDataItemDeserialize<
    GameItemVariant,
    GameDataItemDeserializationSystem<GameItemVariant, GameItemVariantInitSystem.Initializer, GameItemVariantInitSystem>.Deserializer,
    GameItemVariantInitSystem.Initializer>))]

[assembly: RegisterGenericJobType(typeof(EntityDataComponentSerialize<ComponentDataSerializationSystem<GameItemVariant>.Serializer, ComponentDataSerializationSystem<GameItemVariant>.SerializerFactory>))]
[assembly: RegisterGenericJobType(typeof(EntityDataComponentDeserialize<
    GameDataItemDeserializer<GameItemVariant, GameDataItemDeserializationSystem<GameItemVariant, GameItemVariantInitSystem.Initializer, GameItemVariantInitSystem>.Deserializer>,
    GameDataItemDeserializerFactory<GameItemVariant, GameDataItemDeserializationSystem<GameItemVariant, GameItemVariantInitSystem.Initializer, GameItemVariantInitSystem>.Deserializer>>))]

[assembly: EntityDataSerialize(typeof(GameItemVariant))]
[assembly: EntityDataDeserialize(typeof(GameItemVariant), typeof(GameDataItemDeserializationSystem<GameItemVariant, GameItemVariantInitSystem.Initializer, GameItemVariantInitSystem>), (int)GameDataConstans.Version)]
#endregion*/

#region GameItemExp
[assembly: RegisterGenericJobType(typeof(EntityDataComponentDeserialize<
    GameDataItemDeserializationSystemCore<GameItemExp, GameItemExpInitSystem.Initializer>.Deserializer,
    GameDataItemDeserializationSystemCore<GameItemExp, GameItemExpInitSystem.Initializer>.DeserializerFactory>))]

[assembly: RegisterEntityCommandProducerJob(typeof(GameDataItemDeserializationSystemCore<GameItemExp, GameItemExpInitSystem.Initializer>))]

//[assembly: EntityDataSerialize(typeof(GameItemExp))]
//[assembly: EntityDataDeserialize(typeof(GameItemExp), typeof(GameDataItemDeserializationSystem<GameItemExp, GameItemExpInitSystem.Initializer, GameItemExpInitSystem>), (int)GameDataConstans.Version)]
#endregion

#region GameItemPower
[assembly: RegisterGenericJobType(typeof(EntityDataComponentDeserialize<
    GameDataItemDeserializationSystemCore<GameItemPower, GameItemPowerInitSystem.Initializer>.Deserializer,
    GameDataItemDeserializationSystemCore<GameItemPower, GameItemPowerInitSystem.Initializer>.DeserializerFactory>))]

[assembly: RegisterEntityCommandProducerJob(typeof(GameDataItemDeserializationSystemCore<GameItemPower, GameItemPowerInitSystem.Initializer>))]

//[assembly: EntityDataSerialize(typeof(GameItemPower))]
//[assembly: EntityDataDeserialize(typeof(GameItemPower), typeof(GameDataItemDeserializationSystem<GameItemPower, GameItemPowerInitSystem.Initializer, GameItemPowerInitSystem>), (int)GameDataConstans.Version)]
#endregion

#region GameItemLevel
[assembly: RegisterGenericJobType(typeof(EntityDataComponentSerialize<
    EntityDataSerializationIndexComponentDataSystemCore<GameItemLevel, GameItemLevelWrapper>.Serializer,
    EntityDataSerializationIndexComponentDataSystemCore<GameItemLevel, GameItemLevelWrapper>.SerializerFactory>))]

[assembly: RegisterGenericJobType(typeof(EntityDataComponentDeserialize<
    EntityDataDeserializationIndexComponentDataSystemCore<GameItemLevel, GameItemLevelWrapper>.Deserializer,
    EntityDataDeserializationIndexComponentDataSystemCore<GameItemLevel, GameItemLevelWrapper>.DeserializerFactory>))]

[assembly: RegisterGenericJobType(typeof(EntityDataComponentDeserialize<
    GameDataItemLevelDeserializationSystem.Deserializer,
    GameDataItemLevelDeserializationSystem.DeserializerFactory>))]

//[assembly: EntityDataSerialize(typeof(GameItemLevel))]
//[assembly: EntityDataDeserialize(typeof(GameItemLevel), typeof(GameDataItemDeserializationSystem<GameItemLevel, GameItemLevelInitSystem.Initializer, GameItemLevelInitSystem>), (int)GameDataConstans.Version)]
#endregion

[BurstCompile,
    EntityDataSerializationSystem(typeof(GameItemTime)),
    CreateAfter(typeof(EntityDataSerializationInitializationSystem)),
    UpdateInGroup(typeof(EntityDataSerializationSystemGroup)), AutoCreateIn("Server")]
public partial struct GameDataItemTimeSerializationSystem : ISystem
{
    private EntityDataSerializationSystemCoreEx __core;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __core = EntityDataSerializationSystemCoreEx.Create<GameItemTime>(ref state);
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
}

[BurstCompile,
    EntityDataSerializationSystem(typeof(GameItemDurability)),
    CreateAfter(typeof(EntityDataSerializationInitializationSystem)),
    UpdateInGroup(typeof(EntityDataSerializationSystemGroup)), AutoCreateIn("Server")]
public partial struct GameDataItemDurabilitySerializationSystem : ISystem
{
    private EntityDataSerializationSystemCoreEx __core;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __core = EntityDataSerializationSystemCoreEx.Create<GameItemDurability>(ref state);
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
}

[BurstCompile,
    EntityDataSerializationSystem(typeof(GameItemExp)),
    CreateAfter(typeof(EntityDataSerializationInitializationSystem)),
    UpdateInGroup(typeof(EntityDataSerializationSystemGroup)), AutoCreateIn("Server")]
public partial struct GameDataItemExpSerializationSystem : ISystem
{
    private EntityDataSerializationSystemCoreEx __core;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __core = EntityDataSerializationSystemCoreEx.Create<GameItemExp>(ref state);
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
}

[BurstCompile,
    EntityDataSerializationSystem(typeof(GameItemPower)),
    CreateAfter(typeof(EntityDataSerializationInitializationSystem)),
    UpdateInGroup(typeof(EntityDataSerializationSystemGroup)), AutoCreateIn("Server")]
public partial struct GameDataItemPowerSerializationSystem : ISystem
{
    private EntityDataSerializationSystemCoreEx __core;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __core = EntityDataSerializationSystemCoreEx.Create<GameItemPower>(ref state);
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
}

[BurstCompile,
    EntityDataSerializationSystem(typeof(GameItemLevel)),
    CreateAfter(typeof(GameDataLevelContainerSerializationSystem)),
    CreateAfter(typeof(EntityDataSerializationInitializationSystem)),
    UpdateInGroup(typeof(EntityDataSerializationSystemGroup)),
    UpdateAfter(typeof(GameDataLevelContainerSerializationSystem)), AutoCreateIn("Server")]
public partial struct GameDataItemLevelSerializationSystem : ISystem
{
    private EntityDataSerializationIndexComponentDataSystemCore<GameItemLevel, GameItemLevelWrapper> __core;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __core = EntityDataSerializationIndexComponentDataSystemCore<GameItemLevel, GameItemLevelWrapper>.Create<GameDataLevelContainerSerializationSystem>(ref state);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        __core.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        GameItemLevelWrapper wrapper;
        __core.Update(ref wrapper, ref state);
    }
}

//[UpdateAfter(typeof(GameDataItemRootDeserializationSystem))]
public struct GameDataItemDeserializationSystemCore<TData, TInitializer> : IEntityCommandProducerJob
    where TData : unmanaged, IComponentData
    where TInitializer : struct, IGameItemInitializer<TData>
{
    public struct Deserializer : IEntityDataDeserializer
    {
        public GameItemManager.ReadOnlyInfos infos;

        [ReadOnly]
        public NativeArray<Entity> entityArray;

        [ReadOnly]
        public NativeArray<GameItemData> instances;

        public EntityAddDataQueue.ParallelWriter entityManager;

        public TInitializer initializer;

        public bool Fallback(int index)
        {
            if (infos.TryGetValue(instances[index].handle, out var item) && initializer.IsVail(item.type))
                entityManager.AddComponentData(entityArray[index], initializer.GetValue(item.type, item.count));

            return true;
        }

        public void Deserialize(int index, ref EntityDataReader reader)
        {
            entityManager.AddComponentData(entityArray[index], reader.Read<TData>());
        }
    }

    public struct DeserializerFactory : IEntityDataFactory<Deserializer>
    {
        public GameItemManager.ReadOnlyInfos infos;

        [ReadOnly]
        public EntityTypeHandle entityType;

        [ReadOnly]
        public ComponentTypeHandle<GameItemData> instanceType;

        public EntityAddDataQueue.ParallelWriter entityManager;

        public TInitializer initializer;

        public Deserializer Create(in ArchetypeChunk chunk, int unfilteredChunkIndex)
        {
            Deserializer deserializer;
            deserializer.infos = infos;
            deserializer.entityArray = chunk.GetNativeArray(entityType);
            deserializer.instances = chunk.GetNativeArray(ref instanceType);
            deserializer.entityManager = entityManager;
            deserializer.initializer = initializer;

            return deserializer;
        }
    }

    private EntityQuery __group;
    private EntityTypeHandle __entityType;
    private ComponentTypeHandle<GameItemData> __instanceType;
    private TInitializer __initializer;
    private EntityAddDataPool __entityManager;
    private GameItemManagerShared __itemManager;
    private EntityDataDeserializationSystemCoreEx __core;

    public static GameDataItemDeserializationSystemCore<TData, TInitializer> Create<T>(ref SystemState state)
        where T : unmanaged, IGameItemInitializationSystem<TData, TInitializer>
    {
        GameDataItemDeserializationSystemCore<TData, TInitializer> result;
        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            result.__group = builder
                    .WithAll<GameItemData, EntityDataIdentity, EntityDataDeserializable>()
                    .WithNone<TData>()
                    .Build(ref state);

        result.__entityType = state.GetEntityTypeHandle();

        result.__instanceType = state.GetComponentTypeHandle<GameItemData>(true);

        var world = state.WorldUnmanaged;

        result.__initializer = world.GetExistingSystemUnmanaged<T>().initializer;

        result.__entityManager = world.GetExistingSystemUnmanaged<EntityDataDeserializationStructChangeSystem>().addDataCommander;

        result.__itemManager = world.GetExistingSystemUnmanaged<GameItemSystem>().manager;

        result.__core = EntityDataDeserializationSystemCoreEx.Create<TData>(ref state);

        return result;
    }

    public void Dispose()
    {
        __core.Dispose();
    }

    public void Update(ref SystemState state)
    {
        var entityCount = CollectionHelper.CreateNativeArray<int>(1, state.WorldUpdateAllocator, NativeArrayOptions.ClearMemory);

        var jobHandle = __group.CalculateEntityCountAsync(entityCount, state.Dependency);

        var entityManager = __entityManager.Create();

        DeserializerFactory factory;
        factory.infos = __itemManager.value.readOnlyInfos;
        factory.entityType = __entityType.UpdateAsRef(ref state);
        factory.instanceType = __instanceType.UpdateAsRef(ref state);
        factory.entityManager = entityManager.AsComponentParallelWriter<TData>(entityCount, ref jobHandle);
        factory.initializer = __initializer;

        ref var itemManagerJobManager = ref __itemManager.lookupJobManager;

        state.Dependency = JobHandle.CombineDependencies(itemManagerJobManager.readOnlyJobHandle, jobHandle);

        __core.value.Update<Deserializer, DeserializerFactory>(__group, ref factory, ref state, true);

        jobHandle = state.Dependency;

        entityManager.AddJobHandleForProducer<GameDataItemDeserializationSystemCore<TData, TInitializer>>(jobHandle);

        itemManagerJobManager.AddReadOnlyDependency(jobHandle);

        __core.Update(ref state);
    }
}

[BurstCompile,
    EntityDataDeserializationSystem(typeof(GameItemTime), (int)GameDataConstans.Version),
    CreateAfter(typeof(GameItemTimeInitSystem)),
    CreateAfter(typeof(GameDataItemRootDeserializationSystem)),
    CreateAfter(typeof(EntityDataDeserializationStructChangeSystem)),
    UpdateInGroup(typeof(EntityDataDeserializationSystemGroup)),
    UpdateAfter(typeof(GameDataItemRootDeserializationSystem)), AutoCreateIn("Server")]
public partial struct GameDataItemTimeDeserializationSystem : ISystem
{
    private GameDataItemDeserializationSystemCore<GameItemTime, GameItemTimeInitSystem.Initializer> __core;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __core = GameDataItemDeserializationSystemCore<GameItemTime, GameItemTimeInitSystem.Initializer>.Create<GameItemTimeInitSystem>(ref state);
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
}

[BurstCompile,
    EntityDataDeserializationSystem(typeof(GameItemDurability), (int)GameDataConstans.Version),
    CreateAfter(typeof(GameItemDurabilityInitSystem)),
    CreateAfter(typeof(GameDataItemRootDeserializationSystem)),
    CreateAfter(typeof(EntityDataDeserializationStructChangeSystem)),
    UpdateInGroup(typeof(EntityDataDeserializationSystemGroup)),
    UpdateAfter(typeof(GameDataItemRootDeserializationSystem)), AutoCreateIn("Server")]
public partial struct GameDataItemDurabilityDeserializationSystem : ISystem
{
    private GameDataItemDeserializationSystemCore<GameItemDurability, GameItemDurabilityInitSystem.Initializer> __core;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __core = GameDataItemDeserializationSystemCore<GameItemDurability, GameItemDurabilityInitSystem.Initializer>.Create<GameItemDurabilityInitSystem>(ref state);
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
}

[BurstCompile,
    EntityDataDeserializationSystem(typeof(GameItemExp), (int)GameDataConstans.Version),
    CreateAfter(typeof(GameItemExpInitSystem)),
    CreateAfter(typeof(GameDataItemRootDeserializationSystem)),
    CreateAfter(typeof(EntityDataDeserializationStructChangeSystem)),
    UpdateInGroup(typeof(EntityDataDeserializationSystemGroup)),
    UpdateAfter(typeof(GameDataItemRootDeserializationSystem)), AutoCreateIn("Server")]
public partial struct GameDataItemExpDeserializationSystem : ISystem
{
    private GameDataItemDeserializationSystemCore<GameItemExp, GameItemExpInitSystem.Initializer> __core;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __core = GameDataItemDeserializationSystemCore<GameItemExp, GameItemExpInitSystem.Initializer>.Create<GameItemExpInitSystem>(ref state);
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
}

[BurstCompile,
    EntityDataDeserializationSystem(typeof(GameItemPower), (int)GameDataConstans.Version),
    CreateAfter(typeof(GameItemPowerInitSystem)),
    CreateAfter(typeof(GameDataItemRootDeserializationSystem)),
    CreateAfter(typeof(EntityDataDeserializationStructChangeSystem)),
    UpdateInGroup(typeof(EntityDataDeserializationSystemGroup)),
    UpdateAfter(typeof(GameDataItemRootDeserializationSystem)), AutoCreateIn("Server")]
public partial struct GameDataItemPowerDeserializationSystem : ISystem
{
    private GameDataItemDeserializationSystemCore<GameItemPower, GameItemPowerInitSystem.Initializer> __core;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __core = GameDataItemDeserializationSystemCore<GameItemPower, GameItemPowerInitSystem.Initializer>.Create<GameItemPowerInitSystem>(ref state);
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
}

[BurstCompile,
    EntityDataDeserializationSystem(typeof(GameItemLevel), (int)GameDataConstans.Version),
    CreateAfter(typeof(GameItemLevelInitSystem)),
    CreateAfter(typeof(GameDataItemRootDeserializationSystem)),
    CreateAfter(typeof(GameDataLevelContainerDeserializationSystem)),
    CreateAfter(typeof(EntityDataDeserializationStructChangeSystem)),
    UpdateInGroup(typeof(EntityDataDeserializationSystemGroup)),
    UpdateAfter(typeof(GameDataItemRootDeserializationSystem)),
    UpdateAfter(typeof(GameDataLevelContainerDeserializationSystem)), AutoCreateIn("Server")]
public partial struct GameDataItemLevelDeserializationSystem : ISystem
{
    public struct Deserializer : IEntityDataDeserializer, IEntityCommandProducerJob
    {
        public GameItemManager.ReadOnlyInfos infos;

        [ReadOnly]
        public SharedList<int>.Reader guidIndices;

        [ReadOnly]
        public NativeArray<Entity> entityArray;

        [ReadOnly]
        public NativeArray<GameItemData> instances;

        public EntityAddDataQueue.ParallelWriter entityManager;

        public GameItemLevelInitSystem.Initializer initializer;

        public GameItemLevelWrapper wrapper;

        public bool Fallback(int index)
        {
            if (infos.TryGetValue(instances[index].handle, out var item) && initializer.IsVail(item.type))
                entityManager.AddComponentData(entityArray[index], initializer.GetValue(item.type, item.count));

            return true;
        }

        public void Deserialize(int index, ref EntityDataReader reader)
        {
            Entity entity = entityArray[index];

            var value = wrapper.Deserialize(entity, guidIndices.AsArray().AsReadOnly(), ref reader);
            entityManager.AddComponentData(entity, value);
        }
    }

    public struct DeserializerFactory : IEntityDataFactory<Deserializer>
    {
        public GameItemManager.ReadOnlyInfos infos;

        [ReadOnly]
        public SharedList<int>.Reader guidIndices;

        [ReadOnly]
        public EntityTypeHandle entityType;

        [ReadOnly]
        public ComponentTypeHandle<GameItemData> instanceType;

        public EntityAddDataQueue.ParallelWriter entityManager;

        public GameItemLevelInitSystem.Initializer initializer;

        public GameItemLevelWrapper wrapper;

        public Deserializer Create(in ArchetypeChunk chunk, int unfilteredChunkIndex)
        {
            Deserializer deserializer;
            deserializer.infos = infos;
            deserializer.guidIndices = guidIndices;
            deserializer.entityArray = chunk.GetNativeArray(entityType);
            deserializer.instances = chunk.GetNativeArray(ref instanceType);
            deserializer.entityManager = entityManager;
            deserializer.initializer = initializer;
            deserializer.wrapper = wrapper;

            return deserializer;
        }
    }

    private EntityQuery __group;
    private EntityTypeHandle __entityType;
    private ComponentTypeHandle<GameItemData> __instanceType;
    private GameItemLevelInitSystem.Initializer __initializer;
    private EntityAddDataPool __entityManager;
    private GameItemManagerShared __itemManager;
    private EntityDataDeserializationIndexComponentDataSystemCore<GameItemLevel, GameItemLevelWrapper> __core;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __group = builder
                    .WithAll<GameItemData, EntityDataIdentity, EntityDataDeserializable>()
                    .WithNone<GameItemLevel>()
                    .Build(ref state);

        __entityType = state.GetEntityTypeHandle();

        __instanceType = state.GetComponentTypeHandle<GameItemData>(true);

        var world = state.WorldUnmanaged;

        __initializer = world.GetExistingSystemUnmanaged<GameItemLevelInitSystem>().initializer;

        __entityManager = world.GetExistingSystemUnmanaged<EntityDataDeserializationStructChangeSystem>().addDataCommander;

        __itemManager = world.GetExistingSystemUnmanaged<GameItemSystem>().manager;

        __core = EntityDataDeserializationIndexComponentDataSystemCore<GameItemLevel, GameItemLevelWrapper>.Create<GameDataLevelContainerDeserializationSystem>(ref state);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        __core.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var entityCount = CollectionHelper.CreateNativeArray<int>(1, state.WorldUpdateAllocator, NativeArrayOptions.ClearMemory);

        var jobHandle = __group.CalculateEntityCountAsync(entityCount, state.Dependency);

        var entityManager = __entityManager.Create();

        var guidIndices = __core.guidIndices;

        GameItemLevelWrapper wrapper;

        DeserializerFactory factory;
        factory.infos = __itemManager.value.readOnlyInfos;
        factory.guidIndices = guidIndices.reader;
        factory.entityType = __entityType.UpdateAsRef(ref state);
        factory.instanceType = __instanceType.UpdateAsRef(ref state);
        factory.entityManager = entityManager.AsComponentParallelWriter<GameItemLevel>(entityCount, ref jobHandle);
        factory.initializer = __initializer;
        factory.wrapper = wrapper;

        ref var guidIndicesJobManager = ref guidIndices.lookupJobManager;
        ref var itemManagerJobManager = ref __itemManager.lookupJobManager;

        state.Dependency = JobHandle.CombineDependencies(guidIndicesJobManager.readOnlyJobHandle, itemManagerJobManager.readOnlyJobHandle, jobHandle);

        __core.value.Update<Deserializer, DeserializerFactory>(ref factory, ref state, true);

        jobHandle = state.Dependency;

        guidIndicesJobManager.AddReadOnlyDependency(jobHandle);

        itemManagerJobManager.AddReadOnlyDependency(jobHandle);

        entityManager.AddJobHandleForProducer<Deserializer>(jobHandle);

        __core.Update(ref wrapper, ref state, true);
    }
}
