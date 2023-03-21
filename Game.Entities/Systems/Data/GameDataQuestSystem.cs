using Unity.Collections;
using Unity.Entities;
using ZG;

#region GameQuestManager
[assembly: Unity.Jobs.RegisterGenericJobType(typeof(EntityDataIndexBufferInit<GameQuest, GameQuestWrapper>))]

[assembly: Unity.Jobs.RegisterGenericJobType(typeof(EntityDataContainerSerialize<GameDataQuestContainerSerializationSystem.Serializer>))]
[assembly: Unity.Jobs.RegisterGenericJobType(typeof(EntityDataContainerDeserialize<GameDataQuestContainerDeserializationSystem.Deserializer>))]

[assembly: EntityDataSerialize(typeof(GameQuestManager), typeof(GameDataQuestContainerSerializationSystem))]
[assembly: EntityDataDeserialize(typeof(GameQuestManager), typeof(GameDataQuestContainerDeserializationSystem), (int)GameDataConstans.Version)]
#endregion

#region GameNPC
[assembly: Unity.Jobs.RegisterGenericJobType(typeof(EntityDataComponentSerialize<GameDataQuestSerializationSystem.Serializer, GameDataQuestSerializationSystem.SerializerFactory>))]
[assembly: Unity.Jobs.RegisterGenericJobType(typeof(EntityDataComponentDeserialize<GameDataQuestDeserializationSystem.Deserializer, GameDataQuestDeserializationSystem.DeserializerFactory>))]

[assembly: EntityDataSerialize(typeof(GameQuest), typeof(GameDataQuestSerializationSystem))]
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
}

[DisableAutoCreation]
public partial class GameDataQuestSystem : SystemBase
{
    private NativeArray<Hash128> __guids;

    public NativeArray<Hash128> guids => __guids;

    public void Create(Hash128[] guids)
    {
        __guids = new NativeArray<Hash128>(guids, Allocator.Persistent);
    }

    protected override void OnDestroy()
    {
        if (__guids.IsCreated)
            __guids.Dispose();

        base.OnDestroy();
    }

    protected override void OnUpdate()
    {
        throw new System.NotImplementedException();
    }
}

[DisableAutoCreation]
public partial class GameDataQuestContainerSerializationSystem : EntityDataIndexBufferContainerSerializationSystem<GameQuest, GameQuestWrapper>
{
    private GameDataQuestSystem __questsSystem;
    private GameQuestWrapper __wrapper;

    protected override void OnCreate()
    {
        base.OnCreate();

        __questsSystem = World.GetOrCreateSystemManaged<GameDataQuestSystem>();
    }

    protected override NativeArray<Hash128> _GetGuids() => __questsSystem.guids;

    protected override ref GameQuestWrapper _GetWrapper() => ref __wrapper;
}

[DisableAutoCreation, UpdateAfter(typeof(GameDataQuestContainerSerializationSystem))]
public partial class GameDataQuestSerializationSystem : EntityDataIndexBufferSerializationSystem<GameQuest, GameQuestWrapper>
{
    protected override GameQuestWrapper _GetWrapper() => default;

    protected override EntityDataIndexContainerSerializationSystem _GetOrCreateContainerSystem() => World.GetOrCreateSystemManaged<GameDataQuestContainerSerializationSystem>();
}

[DisableAutoCreation]
public partial class GameDataQuestContainerDeserializationSystem : EntityDataIndexContainerDeserializationSystem
{
    private GameDataQuestSystem __questsSystem;

    protected override void OnCreate()
    {
        base.OnCreate();

        __questsSystem = World.GetOrCreateSystemManaged<GameDataQuestSystem>();
    }

    protected override NativeArray<Hash128> _GetGuids() => __questsSystem.guids;
}

[DisableAutoCreation, UpdateAfter(typeof(GameDataQuestContainerDeserializationSystem))]
public partial class GameDataQuestDeserializationSystem : EntityDataIndexBufferDeserializationSystem<GameQuest, GameQuestWrapper>
{
    protected override GameQuestWrapper _GetWrapper() => default;

    protected override EntityDataIndexContainerDeserializationSystem _GetOrCreateContainerSystem() => World.GetOrCreateSystemManaged<GameDataQuestContainerDeserializationSystem>();
}
