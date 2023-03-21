using Unity.Jobs;
using Unity.Entities;
using Unity.Collections;
using ZG;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections.LowLevel.Unsafe;

#region GameItemTime
[assembly: RegisterGenericJobType(typeof(GameDataItemDeserialize<
    GameItemTime,
    GameDataItemDeserializationSystem<GameItemTime, GameItemTimeInitSystem.Initializer, GameItemTimeInitSystem>.Deserializer,
    GameItemTimeInitSystem.Initializer>))]

[assembly: RegisterGenericJobType(typeof(EntityDataComponentSerialize<ComponentDataSerializationSystem<GameItemTime>.Serializer, ComponentDataSerializationSystem<GameItemTime>.SerializerFactory>))]
[assembly: RegisterGenericJobType(typeof(EntityDataComponentDeserialize<
    GameDataItemDeserializer<GameItemTime, GameDataItemDeserializationSystem<GameItemTime, GameItemTimeInitSystem.Initializer, GameItemTimeInitSystem>.Deserializer>,
    GameDataItemDeserializerFactory<GameItemTime, GameDataItemDeserializationSystem<GameItemTime, GameItemTimeInitSystem.Initializer, GameItemTimeInitSystem>.Deserializer>>))]

[assembly: EntityDataSerialize(typeof(GameItemTime))]
[assembly: EntityDataDeserialize(typeof(GameItemTime), typeof(GameDataItemDeserializationSystem<GameItemTime, GameItemTimeInitSystem.Initializer, GameItemTimeInitSystem>), (int)GameDataConstans.Version)]
#endregion

#region GameItemDurability
[assembly: RegisterGenericJobType(typeof(GameDataItemDeserialize<
    GameItemDurability,
    GameDataItemDeserializationSystem<GameItemDurability, GameItemDurabilityInitSystem.Initializer, GameItemDurabilityInitSystem>.Deserializer, 
    GameItemDurabilityInitSystem.Initializer>))]

[assembly: RegisterGenericJobType(typeof(EntityDataComponentSerialize<ComponentDataSerializationSystem<GameItemDurability>.Serializer, ComponentDataSerializationSystem<GameItemDurability>.SerializerFactory>))]
[assembly: RegisterGenericJobType(typeof(EntityDataComponentDeserialize<
    GameDataItemDeserializer<GameItemDurability, GameDataItemDeserializationSystem<GameItemDurability, GameItemDurabilityInitSystem.Initializer, GameItemDurabilityInitSystem>.Deserializer>,
    GameDataItemDeserializerFactory<GameItemDurability, GameDataItemDeserializationSystem<GameItemDurability, GameItemDurabilityInitSystem.Initializer, GameItemDurabilityInitSystem>.Deserializer>>))]

[assembly: EntityDataSerialize(typeof(GameItemDurability))]
[assembly: EntityDataDeserialize(typeof(GameItemDurability), typeof(GameDataItemDeserializationSystem<GameItemDurability, GameItemDurabilityInitSystem.Initializer, GameItemDurabilityInitSystem>), (int)GameDataConstans.Version)]
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

#region GameItemLevel
[assembly: RegisterGenericJobType(typeof(GameDataItemDeserialize<
    GameItemLevel,
    GameDataItemDeserializationSystem<GameItemLevel, GameItemLevelInitSystem.Initializer, GameItemLevelInitSystem>.Deserializer,
    GameItemLevelInitSystem.Initializer>))]

[assembly: RegisterGenericJobType(typeof(EntityDataComponentSerialize<ComponentDataSerializationSystem<GameItemLevel>.Serializer, ComponentDataSerializationSystem<GameItemLevel>.SerializerFactory>))]
[assembly: RegisterGenericJobType(typeof(EntityDataComponentDeserialize<
    GameDataItemDeserializer<GameItemLevel, GameDataItemDeserializationSystem<GameItemLevel, GameItemLevelInitSystem.Initializer, GameItemLevelInitSystem>.Deserializer>,
    GameDataItemDeserializerFactory<GameItemLevel, GameDataItemDeserializationSystem<GameItemLevel, GameItemLevelInitSystem.Initializer, GameItemLevelInitSystem>.Deserializer>>))]

[assembly: EntityDataSerialize(typeof(GameItemLevel))]
[assembly: EntityDataDeserialize(typeof(GameItemLevel), typeof(GameDataItemDeserializationSystem<GameItemLevel, GameItemLevelInitSystem.Initializer, GameItemLevelInitSystem>), (int)GameDataConstans.Version)]
#endregion

#region GameItemExp
[assembly: RegisterGenericJobType(typeof(GameDataItemDeserialize<
    GameItemExp,
    GameDataItemDeserializationSystem<GameItemExp, GameItemExpInitSystem.Initializer, GameItemExpInitSystem>.Deserializer,
    GameItemExpInitSystem.Initializer>))]

[assembly: RegisterGenericJobType(typeof(EntityDataComponentSerialize<ComponentDataSerializationSystem<GameItemExp>.Serializer, ComponentDataSerializationSystem<GameItemExp>.SerializerFactory>))]
[assembly: RegisterGenericJobType(typeof(EntityDataComponentDeserialize<
    GameDataItemDeserializer<GameItemExp, GameDataItemDeserializationSystem<GameItemExp, GameItemExpInitSystem.Initializer, GameItemExpInitSystem>.Deserializer>,
    GameDataItemDeserializerFactory<GameItemExp, GameDataItemDeserializationSystem<GameItemExp, GameItemExpInitSystem.Initializer, GameItemExpInitSystem>.Deserializer>>))]

[assembly: EntityDataSerialize(typeof(GameItemExp))]
[assembly: EntityDataDeserialize(typeof(GameItemExp), typeof(GameDataItemDeserializationSystem<GameItemExp, GameItemExpInitSystem.Initializer, GameItemExpInitSystem>), (int)GameDataConstans.Version)]
#endregion

#region GameItemPower
[assembly: RegisterGenericJobType(typeof(GameDataItemDeserialize<
    GameItemPower,
    GameDataItemDeserializationSystem<GameItemPower, GameItemPowerInitSystem.Initializer, GameItemPowerInitSystem>.Deserializer,
    GameItemPowerInitSystem.Initializer>))]

[assembly: RegisterGenericJobType(typeof(EntityDataComponentSerialize<ComponentDataSerializationSystem<GameItemPower>.Serializer, ComponentDataSerializationSystem<GameItemPower>.SerializerFactory>))]
[assembly: RegisterGenericJobType(typeof(EntityDataComponentDeserialize<
    GameDataItemDeserializer<GameItemPower, GameDataItemDeserializationSystem<GameItemPower, GameItemPowerInitSystem.Initializer, GameItemPowerInitSystem>.Deserializer>,
    GameDataItemDeserializerFactory<GameItemPower, GameDataItemDeserializationSystem<GameItemPower, GameItemPowerInitSystem.Initializer, GameItemPowerInitSystem>.Deserializer>>))]

[assembly: EntityDataSerialize(typeof(GameItemPower))]
[assembly: EntityDataDeserialize(typeof(GameItemPower), typeof(GameDataItemDeserializationSystem<GameItemPower, GameItemPowerInitSystem.Initializer, GameItemPowerInitSystem>), (int)GameDataConstans.Version)]
#endregion

public interface IGameDataItemDeserializer<T> where T : struct, IComponentData
{
    T Deserialize(in EntityDataReader reader);
}

public struct GameDataItemDeserializer<TData, TDeserializer> : IEntityDataDeserializer 
    where TData : struct, IComponentData 
    where TDeserializer : struct, IGameDataItemDeserializer<TData>
{
    public TDeserializer instance;
    public NativeArray<TData> values;

    public void Deserialize(int index, ref EntityDataReader reader)
    {
        values[index] = instance.Deserialize(reader);
    }
}

public struct GameDataItemDeserializerFactory<TData, TDeserializer> : IEntityDataFactory<GameDataItemDeserializer<TData, TDeserializer>>
    where TData : unmanaged, IComponentData
    where TDeserializer : struct, IGameDataItemDeserializer<TData>
{
    public TDeserializer deserializer;
    public ComponentTypeHandle<TData> valueType;

    public GameDataItemDeserializer<TData, TDeserializer> Create(in ArchetypeChunk chunk, int firstEntityIndex)
    {
        GameDataItemDeserializer<TData, TDeserializer> deserializer;
        deserializer.instance = this.deserializer;
        deserializer.values = chunk.GetNativeArray(ref valueType);

        return deserializer;
    }
}

[BurstCompile]
public struct GameDataItemDeserialize<TData, TDeserializer, TInitializer> : IJobChunk, IEntityCommandProducerJob
    where TData : struct, IComponentData
        where TDeserializer : struct, IGameDataItemDeserializer<TData>
        where TInitializer : struct, IGameItemInitializer<TData>
{
    private struct Executor
    {
        public GameItemManager.ReadOnlyInfos infos;

        [ReadOnly]
        public UnsafeParallelHashMap<Hash128, UnsafeBlock> blocks;

        [ReadOnly]
        public NativeArray<Entity> entityArray;

        [ReadOnly]
        public NativeArray<EntityDataIdentity> identities;

        [ReadOnly]
        public NativeArray<GameItemData> instances;

        public TDeserializer deserializer;

        public TInitializer initializer;

        public EntityCommandQueue<EntityData<TData>>.ParallelWriter entityManager;

        public void Execute(int index)
        {
            EntityData<TData> result;
            if (!blocks.TryGetValue(identities[index].guid, out var block))
            {
                /*if(entityArray[index].Index == 5078)
                    UnityEngine.Debug.LogError($"Error {entityArray[index]}");*/

                if (infos.TryGetValue(instances[index].handle, out var item) && initializer.IsVail(item.type))
                {
                    result.entity = entityArray[index];
                    result.value = initializer.GetValue(item.type, item.count);

                    entityManager.Enqueue(result);
                }
                //else
                //     UnityEngine.Debug.LogError($"Error {entityArray[index]}");

                return;
            }

            result.entity = entityArray[index];
            result.value = deserializer.Deserialize(new EntityDataReader(block));

            entityManager.Enqueue(result);
        }
    }

    public GameItemManager.ReadOnlyInfos infos;

    [ReadOnly]
    public NativeArray<UnsafeParallelHashMap<Hash128, UnsafeBlock>> blocks;

    [ReadOnly]
    public EntityTypeHandle entityType;

    [ReadOnly]
    public ComponentTypeHandle<EntityDataIdentity> identityType;

    [ReadOnly]
    public ComponentTypeHandle<GameItemData> instanceType;

    public TDeserializer deserializer;

    public TInitializer initializer;

    public EntityCommandQueue<EntityData<TData>>.ParallelWriter entityManager;

    public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
    {
        var blocks = this.blocks[0];
        if (!blocks.IsCreated)
            return;

        Executor executor;
        executor.infos = infos;
        executor.blocks = blocks;
        executor.entityArray = chunk.GetNativeArray(entityType);
        executor.identities = chunk.GetNativeArray(ref identityType);
        executor.instances = chunk.GetNativeArray(ref instanceType);
        executor.deserializer = deserializer;
        executor.initializer = initializer;
        executor.entityManager = entityManager;

        var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
        while (iterator.NextEntityIndex(out int i))
            executor.Execute(i);
    }
}

[UpdateAfter(typeof(GameDataItemRootDeserializationSystem))]
public abstract partial class GameDataItemDeserializationSystem<TData, TDeserializer, TInitializer, TInitSystem> : EntityDataDeserializationComponentSystem<
    TData, 
    GameDataItemDeserializer<TData, TDeserializer>, 
    GameDataItemDeserializerFactory<TData, TDeserializer>>
        where TData : unmanaged, IComponentData
        where TDeserializer : struct, IGameDataItemDeserializer<TData>
        where TInitializer : struct, IGameItemInitializer<TData>
        where TInitSystem : unmanaged, IGameItemInitializationSystem<TData, TInitializer>
{
    private EntityQuery __group;
    private EntityCommandPool<EntityData<TData>> __entityManager;
    private GameItemManagerShared __itemManager;
    private TInitializer __initializer;

    protected override void OnCreate()
    {
        base.OnCreate();

        __group = GetEntityQuery(
            ComponentType.ReadOnly<GameItemData>(),
            ComponentType.ReadOnly<EntityDataIdentity>(),
            ComponentType.ReadOnly<EntityDataDeserializable>(), 
            ComponentType.Exclude<TData>());

        var world = World;

        __itemManager = world.GetOrCreateSystemUnmanaged<GameItemSystem>().manager;

        __initializer = world.GetOrCreateSystemUnmanaged<TInitSystem>().initializer;

#if DEBUG
        EntityCommandUtility.RegisterProducerJobType<GameDataItemDeserialize<TData, TDeserializer, TInitializer>>();
#endif
    }

    protected override JobHandle _Update(in NativeArray<UnsafeParallelHashMap<Hash128, UnsafeBlock>> blocks, in JobHandle inputDeps)
    {
        if(!__entityManager.isCreated)
            __entityManager = systemGroup.clearSystem.CreateAddComponentDataCommander<TData>();

        var entityManager = __entityManager.Create();

        GameDataItemDeserialize<TData, TDeserializer, TInitializer> deserialize;
        deserialize.infos = __itemManager.value.readOnlyInfos;
        deserialize.blocks = blocks;
        deserialize.entityType = GetEntityTypeHandle();
        deserialize.identityType = GetComponentTypeHandle<EntityDataIdentity>(true);
        deserialize.instanceType = GetComponentTypeHandle<GameItemData>(true);
        deserialize.initializer = __initializer;
        deserialize.deserializer = _GetDeserializer();
        deserialize.entityManager = entityManager.parallelWriter;

        ref var lookupJobManager = ref __itemManager.lookupJobManager;

        JobHandle jobHandle = deserialize.ScheduleParallel(__group, JobHandle.CombineDependencies(lookupJobManager.readOnlyJobHandle, inputDeps));

        entityManager.AddJobHandleForProducer<GameDataItemDeserialize<TData, TDeserializer, TInitializer>>(jobHandle);

        lookupJobManager.AddReadOnlyDependency(jobHandle);

        return jobHandle;
    }

    protected override GameDataItemDeserializerFactory<TData, TDeserializer> _Get(ref JobHandle jobHandle)
    {
        GameDataItemDeserializerFactory<TData, TDeserializer> factory;
        factory.deserializer = _GetDeserializer();
        factory.valueType = GetComponentTypeHandle<TData>();

        return factory;
    }

    protected abstract TDeserializer _GetDeserializer();
}

public partial class GameDataItemDeserializationSystem<TData, TInitializer, TInitSystem> : GameDataItemDeserializationSystem<
    TData, 
    GameDataItemDeserializationSystem<TData, TInitializer, TInitSystem>.Deserializer,
    TInitializer,
    TInitSystem> 
    where TData : unmanaged, IComponentData
    where TInitializer : struct, IGameItemInitializer<TData>
    where TInitSystem : unmanaged, IGameItemInitializationSystem<TData, TInitializer>
{
    public struct Deserializer : IGameDataItemDeserializer<TData>
    {
        public TData Deserialize(in EntityDataReader reader) => reader.Read<TData>();
    }

    protected override Deserializer _GetDeserializer()
    {
        return default;
    }
}
