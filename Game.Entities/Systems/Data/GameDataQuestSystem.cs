using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using ZG;

#region GameQuestManager
[assembly: Unity.Jobs.RegisterGenericJobType(typeof(EntityDataIndexBufferInit<GameQuest, GameQuestWrapper>))]

//[assembly: Unity.Jobs.RegisterGenericJobType(typeof(EntityDataContainerSerialize<GameDataQuestContainerSerializationSystem.Serializer>))]
[assembly: Unity.Jobs.RegisterGenericJobType(typeof(EntityDataContainerDeserialize<GameDataQuestContainerDeserializationSystem.Deserializer>))]

//[assembly: EntityDataSerialize(typeof(GameQuestManager), typeof(GameDataQuestContainerSerializationSystem))]
[assembly: EntityDataDeserialize(typeof(GameQuestManager), typeof(GameDataQuestContainerDeserializationSystem), (int)GameDataConstans.Version)]
#endregion

#region GameNPC
[assembly: Unity.Jobs.RegisterGenericJobType(typeof(EntityDataComponentSerialize<EntityDataSerializationIndexBufferSystemCore<GameQuest, GameQuestWrapper>.Serializer, EntityDataSerializationIndexBufferSystemCore<GameQuest, GameQuestWrapper>.SerializerFactory>))]
[assembly: Unity.Jobs.RegisterGenericJobType(typeof(EntityDataComponentDeserialize<GameDataQuestDeserializationSystem.Deserializer, GameDataQuestDeserializationSystem.DeserializerFactory>))]

//[assembly: EntityDataSerialize(typeof(GameQuest), typeof(GameDataQuestSerializationSystem))]
[assembly: EntityDataDeserialize(typeof(GameQuest), typeof(GameDataQuestDeserializationSystem), (int)GameDataConstans.Version)]
#endregion


public struct GameQuestWrapper : IEntityDataIndexReadWriteWrapper<GameQuest>
{
    public bool TryGet(in GameQuest data, out int index)
    {
        index = data.index;

        return data.index != -1;
    }

    public void Invail(ref GameQuest data)
    {
        data.index = -1;
    }

    public void Set(ref GameQuest data, int index)
    {
        data.index = index;
    }

    public void Serialize(ref EntityDataWriter writer, in GameQuest data, in SharedHashMap<int, int>.Reader guidIndices)
    {
        EntityDataIndexReadWriteWrapperUtility.Serialize(ref this, ref writer, data, guidIndices);
    }

    public GameQuest Deserialize(ref EntityDataReader reader, in NativeArray<int>.ReadOnly indices)
    {
        return EntityDataIndexReadWriteWrapperUtility.Deserialize<GameQuest, GameQuestWrapper>(ref this, ref reader, indices);
    }
}

public struct GameDataQuestContainer : IComponentData
{
    public NativeArray<Hash128>.ReadOnly guids;
}

[BurstCompile,
    EntityDataSerializationSystem(typeof(GameQuestManager)),
    CreateAfter(typeof(EntityDataSerializationInitializationSystem)),
    UpdateInGroup(typeof(EntityDataSerializationSystemGroup)), AutoCreateIn("Server")]
public partial struct GameDataQuestContainerSerializationSystem : ISystem, IEntityDataSerializationIndexContainerSystem
{
    private EntityDataSerializationIndexContainerBufferSystemCore<GameQuest> __core;

    public SharedHashMap<int, int> guidIndices => __core.guidIndices;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __core = new EntityDataSerializationIndexContainerBufferSystemCore<GameQuest>(ref state);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        __core.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var guids = SystemAPI.GetSingleton<GameDataQuestContainer>().guids;

        GameQuestWrapper wrapper;
        __core.Update(guids, ref wrapper, ref state);
    }
}

[BurstCompile,
    EntityDataSerializationSystem(typeof(GameQuest)),
    CreateAfter(typeof(GameDataQuestContainerSerializationSystem)),
    UpdateInGroup(typeof(EntityDataSerializationSystemGroup)), AutoCreateIn("Server"),
    UpdateAfter(typeof(GameDataQuestContainerSerializationSystem))]
public partial struct GameDataQuestSerializationSystem : ISystem
{
    private EntityDataSerializationIndexBufferSystemCore<GameQuest, GameQuestWrapper> __core;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __core = EntityDataSerializationIndexBufferSystemCore<GameQuest, GameQuestWrapper>.Create<GameDataQuestContainerSerializationSystem>(ref state);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        __core.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        GameQuestWrapper wrapper;
        __core.Update(ref wrapper, ref state);
    }
}

[DisableAutoCreation]
public partial class GameDataQuestContainerDeserializationSystem : EntityDataIndexContainerDeserializationSystem
{
    protected override NativeArray<Hash128>.ReadOnly _GetGuids() => SystemAPI.GetSingleton<GameDataQuestContainer>().guids;
}

[DisableAutoCreation, UpdateAfter(typeof(GameDataQuestContainerDeserializationSystem))]
public partial class GameDataQuestDeserializationSystem : EntityDataIndexBufferDeserializationSystem<GameQuest, GameQuestWrapper>
{
    protected override GameQuestWrapper _GetWrapper() => default;

    protected override EntityDataIndexContainerDeserializationSystem _GetOrCreateContainerSystem() => World.GetOrCreateSystemManaged<GameDataQuestContainerDeserializationSystem>();
}
