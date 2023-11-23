using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using ZG;

#region GameItemOwner
[assembly: RegisterGenericJobType(typeof(EntityDataComponentSerialize<GameDataEntityComponentDataSerializer<GameItemOwner, GameItemOwnerDataSerializer>, GameDataEntityComponentDataSerializerFactory<GameItemOwner, GameItemOwnerDataSerializer>>))]
[assembly: RegisterGenericJobType(typeof(EntityDataComponentDeserialize<GameDataEntityComponentDataDeserializer<GameItemData, GameItemOwnerDataDeserializer>, GameDataEntityComponentDataDeserializerFactory<GameItemData, GameItemOwnerDataDeserializer>>))]
[assembly: RegisterGenericJobType(typeof(GameDataEntityComponentDataBuild<GameItemData, GameItemOwnerDataBuilder>))]
#endregion

public struct GameItemOwnerDataSerializer : IGameDataEntityCompoentSerializer<GameItemOwner>
{
    public Entity Get(in GameItemOwner value)
    {
        return value.entity;
    }

    /*public void Set(ref GameItemOwner value, in Entity entity)
    {
        value.entity = entity;
    }*/

    public void Serialize(in GameItemOwner value, int entityIndex, ref EntityDataWriter writer)
    {
        writer.Write(entityIndex);
    }

    /*public int Deserialize(ref GameItemOwner value, ref EntityDataReader reader)
    {
        return reader.Read<int>();
    }*/
}

public struct GameItemOwnerDataDeserializer : IGameDataEntityCompoentDeserializer<GameItemData>
{
    public GameItemManager.ReadOnlyInfos infos;
    public GameItemObjectInitSystem.Initializer initializer;

    [ReadOnly]
    public ComponentLookup<GameItemOwner> values;

    public EntityAddDataQueue.ParallelWriter entityManager;

    public bool Fallback(in Entity entity, in GameItemData value)
    {
        if (!values.HasComponent(entity) && infos.TryGetValue(value.handle, out var item) && initializer.IsVail(item.type))
            entityManager.AddComponentData(entity, default(GameItemOwner));

        return true;
    }

    public int Deserialize(ref GameItemData value, ref EntityDataReader reader)
    {
        return reader.Read<int>();
    }
}

public struct GameItemOwnerDataBuilder : IGameDataEntityCompoentBuilder<GameItemData>
{
    public ComponentLookup<GameItemOwner> values;

    public EntityAddDataQueue.Writer entityManager;

    public void Set(ref GameItemData value, in Entity entity, in Entity instance)
    {
        GameItemOwner target;
        target.entity = entity;
        if (values.HasComponent(instance))
            values[instance] = target;
        else
            entityManager.AddComponentData(instance, target);
    }
}

[BurstCompile,
    EntityDataSerializationSystem(typeof(GameItemOwner)),
    CreateAfter(typeof(EntityDataSerializationInitializationSystem)),
    UpdateInGroup(typeof(EntityDataSerializationSystemGroup)), AutoCreateIn("Server")]
public partial struct GameDataItemOwnerSerializationSystem : ISystem
{
    private GameDataEntityComponentDataSerializationSystemCore __core;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __core = new GameDataEntityComponentDataSerializationSystemCore(TypeManager.GetTypeIndex<GameItemOwner>(), ref state);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        __core.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        GameItemOwnerDataSerializer serializer;
        __core.Update<GameItemOwner, GameItemOwnerDataSerializer>(ref serializer, ref state);
    }
}

[BurstCompile,
    EntityDataDeserializationSystem(typeof(GameItemOwner), (int)GameDataConstans.Version),
    CreateAfter(typeof(EntityDataDeserializationPresentationSystem)),
    CreateAfter(typeof(EntityDataDeserializationComponentSystem)),
    UpdateInGroup(typeof(EntityDataDeserializationSystemGroup)), AutoCreateIn("Server")]
public partial struct GameDataItemOwnerDeserializationSystem : ISystem, IEntityCommandProducerJob
{
    private ComponentLookup<GameItemOwner> __values;

    private GameItemObjectInitSystem.Initializer __initializer;
    private EntityAddDataPool __entityManager;
    private GameItemManagerShared __itemManager;

    private GameDataEntityComponentDataDeserializationSystemCore __core;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __values = state.GetComponentLookup<GameItemOwner>();

        var world = state.WorldUnmanaged;

        __initializer = world.GetExistingSystemUnmanaged<GameItemObjectInitSystem>().initializer;

        __entityManager = world.GetExistingSystemUnmanaged<EntityDataDeserializationStructChangeSystem>().addDataCommander;

        __itemManager = world.GetExistingSystemUnmanaged<GameItemSystem>().manager;

        __core = new GameDataEntityComponentDataDeserializationSystemCore(TypeManager.GetTypeIndex<GameItemData>(), ref state);
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

        var jobHandle = __core.group.CalculateEntityCountAsync(entityCount, state.Dependency);

        var values = __values.UpdateAsRef(ref state);

        var entityManager = __entityManager.Create();

        GameItemOwnerDataDeserializer deserializer;
        deserializer.infos = __itemManager.value.readOnlyInfos;
        deserializer.initializer = __initializer;
        deserializer.values = values;
        deserializer.entityManager = entityManager.AsComponentParallelWriter<GameItemOwner>(entityCount, ref jobHandle);

        GameItemOwnerDataBuilder builder;
        builder.values = values;
        builder.entityManager = entityManager.writer;

        ref var itemManagerJobManager = ref __itemManager.lookupJobManager;

        state.Dependency = JobHandle.CombineDependencies(itemManagerJobManager.readOnlyJobHandle, jobHandle);

        __core.Update<GameItemData, GameItemOwnerDataDeserializer, GameItemOwnerDataBuilder>(ref deserializer, ref builder, ref state, out var deserializeJobHandle);

        itemManagerJobManager.AddReadOnlyDependency(deserializeJobHandle);

        entityManager.AddJobHandleForProducer<GameDataItemOwnerDeserializationSystem>(state.Dependency);
    }
}
