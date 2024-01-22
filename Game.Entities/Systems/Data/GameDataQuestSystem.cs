using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using ZG;

#region GameQuestManager
[assembly: Unity.Jobs.RegisterGenericJobType(typeof(EntityDataIndexBufferInit<GameQuest, GameQuestWrapper>))]

//[assembly: Unity.Jobs.RegisterGenericJobType(typeof(EntityDataContainerSerialize<GameDataQuestContainerSerializationSystem.Serializer>))]
//[assembly: Unity.Jobs.RegisterGenericJobType(typeof(EntityDataContainerDeserialize<GameDataQuestContainerDeserializationSystem.Deserializer>))]

//[assembly: EntityDataSerialize(typeof(GameQuestManager), typeof(GameDataQuestContainerSerializationSystem))]
//[assembly: EntityDataDeserialize(typeof(GameQuestManager), typeof(GameDataQuestContainerDeserializationSystem), (int)GameDataConstans.Version)]
#endregion

#region GameNPC
[assembly: Unity.Jobs.RegisterGenericJobType(typeof(EntityDataComponentSerialize<EntityDataSerializationIndexBufferSystemCore<GameQuest, GameQuestWrapper>.Serializer, EntityDataSerializationIndexBufferSystemCore<GameQuest, GameQuestWrapper>.SerializerFactory>))]
[assembly: Unity.Jobs.RegisterGenericJobType(typeof(EntityDataComponentDeserialize<EntityDataDeserializationIndexBufferSystemCore<GameQuest, GameQuestWrapper>.Deserializer, EntityDataDeserializationIndexBufferSystemCore<GameQuest, GameQuestWrapper>.DeserializerFactory>))]

//[assembly: EntityDataSerialize(typeof(GameQuest), typeof(GameDataQuestSerializationSystem))]
//[assembly: EntityDataDeserialize(typeof(GameQuest), typeof(GameDataQuestDeserializationSystem), (int)GameDataConstans.Version)]
#endregion


public struct GameQuestWrapper : IEntityDataIndexReadWriteWrapper<GameQuest>, 
    IEntityDataSerializationIndexWrapper<GameQuest>, 
    IEntityDataDeserializationIndexWrapper<GameQuest>
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

    public GameQuest Deserialize(in Entity entity, in NativeArray<int>.ReadOnly guidIndices, ref EntityDataReader reader)
    {
        return EntityDataIndexReadWriteWrapperUtility.Deserialize<GameQuest, GameQuestWrapper>(ref this, ref reader, guidIndices);
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

[BurstCompile,
    EntityDataDeserializationSystem(typeof(GameQuestManager), (int)GameDataConstans.Version),
    CreateAfter(typeof(EntityDataDeserializationContainerSystem)),
    UpdateInGroup(typeof(EntityDataDeserializationSystemGroup)), AutoCreateIn("Server")]
public partial struct GameDataQuestContainerDeserializationSystem : ISystem, IEntityDataDeserializationIndexContainerSystem
{
    private EntityDataDeserializationIndexContainerSystemCore __core;

    public SharedList<int> guidIndices => __core.guidIndices;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __core = new EntityDataDeserializationIndexContainerSystemCore(ref state);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        __core.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        __core.Update(SystemAPI.GetSingleton<GameDataQuestContainer>().guids, ref state);
    }
}

[BurstCompile,
    EntityDataDeserializationSystem(typeof(GameQuest), (int)GameDataConstans.Version),
    CreateAfter(typeof(EntityDataDeserializationComponentSystem)),
    CreateAfter(typeof(GameDataQuestContainerDeserializationSystem)),
    UpdateInGroup(typeof(EntityDataDeserializationSystemGroup)),
    UpdateAfter(typeof(GameDataQuestContainerDeserializationSystem)), AutoCreateIn("Server")]
public partial struct GameDataQuestDeserializationSystem : ISystem
{
    private EntityDataDeserializationIndexBufferSystemCore<GameQuest, GameQuestWrapper> __core;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __core = EntityDataDeserializationIndexBufferSystemCore<GameQuest, GameQuestWrapper>.Create<GameDataQuestContainerDeserializationSystem>(ref state);
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
        __core.Update(ref wrapper, ref state, true);
    }
}
