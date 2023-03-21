using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using ZG;

#region GameLevelManager
[assembly: RegisterGenericJobType(typeof(EntityDataIndexComponentInit<GameLevel, GameLevelWrapper>))]
[assembly: RegisterGenericJobType(typeof(EntityDataIndexBufferInit<GameSoul, GameSoulLevelWrapper>))]

[assembly: RegisterGenericJobType(typeof(EntityDataContainerSerialize<GameDataLevelContainerSerializationSystem.Serializer>))]
[assembly: RegisterGenericJobType(typeof(EntityDataContainerDeserialize<GameDataLevelContainerDeserializationSystem.Deserializer>))]

[assembly: EntityDataSerialize(typeof(GameLevelManager), typeof(GameDataLevelContainerSerializationSystem))]
[assembly: EntityDataDeserialize(typeof(GameLevelManager), typeof(GameDataLevelContainerDeserializationSystem), (int)GameDataConstans.Version)]
#endregion

#region GameLevel
[assembly: RegisterGenericJobType(typeof(EntityDataComponentSerialize<GameDataLevelSerializationSystem.Serializer, GameDataLevelSerializationSystem.SerializerFactory>))]
[assembly: RegisterGenericJobType(typeof(EntityDataComponentDeserialize<GameDataLevelDeserializationSystem.Deserializer, GameDataLevelDeserializationSystem.DeserializerFactory>))]
[assembly: EntityDataSerialize(typeof(GameLevel), typeof(GameDataLevelSerializationSystem))]
[assembly: EntityDataDeserialize(typeof(GameLevel), typeof(GameDataLevelDeserializationSystem), (int)GameDataConstans.Version)]
#endregion

#region GameExp
[assembly: RegisterGenericJobType(typeof(EntityDataComponentSerialize<ComponentDataSerializationSystem<GameExp>.Serializer, ComponentDataSerializationSystem<GameExp>.SerializerFactory>))]
[assembly: RegisterGenericJobType(typeof(EntityDataComponentDeserialize<ComponentDataDeserializationSystem<GameExp>.Deserializer, ComponentDataDeserializationSystem<GameExp>.DeserializerFactory>))]
[assembly: EntityDataSerialize(typeof(GameExp))]
[assembly: EntityDataDeserialize(typeof(GameExp), (int)GameDataConstans.Version)]
#endregion

#region GamePower
[assembly: RegisterGenericJobType(typeof(EntityDataComponentSerialize<ComponentDataSerializationSystem<GamePower>.Serializer, ComponentDataSerializationSystem<GamePower>.SerializerFactory>))]
[assembly: RegisterGenericJobType(typeof(EntityDataComponentDeserialize<ComponentDataDeserializationSystem<GamePower>.Deserializer, ComponentDataDeserializationSystem<GamePower>.DeserializerFactory>))]
[assembly: EntityDataSerialize(typeof(GamePower))]
[assembly: EntityDataDeserialize(typeof(GamePower), (int)GameDataConstans.Version)]
#endregion

public struct GameLevelManager
{

}

public struct GameLevelWrapper : IEntityDataIndexReadWriteWrapper<GameLevel>
{
    public bool TryGet(in GameLevel data, out int index)
    {
        index = data.handle - 1;

        return data.handle > 0;
    }

    public void Invail(ref GameLevel data)
    {
        data.handle = 0;
    }

    public void Set(ref GameLevel data, int index)
    {
        data.handle = index + 1;
    }
}

public struct GameSoulLevelWrapper : IEntityDataIndexReadWriteWrapper<GameSoul>
{
    public bool TryGet(in GameSoul data, out int index)
    {
        index = data.data.levelIndex;

        return data.data.levelIndex > -1;
    }

    public void Invail(ref GameSoul data)
    {
        data.data.levelIndex = -1;
    }

    public void Set(ref GameSoul data, int index)
    {
        data.data.levelIndex = index;
    }
}

[DisableAutoCreation]
public partial class GameDataLevelContainerSerializationSystem : EntityDataIndexContainerSerializationSystem
{
    private GameSoulSystem __soulSystem;
    private EntityQuery __instanceGroup;
    private EntityQuery __soulGroup;

    protected override void OnCreate()
    {
        base.OnCreate();

        __soulSystem = World.GetOrCreateSystemManaged<GameSoulSystem>();

        __instanceGroup = GetEntityQuery(
                new EntityQueryDesc()
                {
                    All = new ComponentType[]
                    {
                        ComponentType.ReadOnly<GameLevel>(),
                        ComponentType.ReadOnly<EntityDataIdentity>(),
                        ComponentType.ReadOnly<EntityDataSerializable>()
                    },
                    Options = EntityQueryOptions.IncludeDisabledEntities
                });

        __soulGroup = GetEntityQuery(
                new EntityQueryDesc()
                {
                    All = new ComponentType[]
                    {
                        ComponentType.ReadOnly<GameSoul>(),
                        ComponentType.ReadOnly<EntityDataIdentity>(),
                        ComponentType.ReadOnly<EntityDataSerializable>()
                    },
                    Options = EntityQueryOptions.IncludeDisabledEntities
                });
    }

    protected override JobHandle _Update(in JobHandle inputDeps)
    {
        var guids = __soulSystem.guids;

        GameLevelWrapper instanceWrapper;
        var jobHandle = _ScheduleComponent<GameLevel, GameLevelWrapper>(__instanceGroup, guids, ref instanceWrapper, inputDeps);

        GameSoulLevelWrapper soulWrapper;
        return _ScheduleBuffer<GameSoul, GameSoulLevelWrapper>(__soulGroup, guids, ref soulWrapper, jobHandle);
    }
}

[DisableAutoCreation, UpdateAfter(typeof(GameDataLevelContainerSerializationSystem))]
public partial class GameDataLevelSerializationSystem : EntityDataIndexComponentSerializationSystem<GameLevel, GameLevelWrapper>
{
    protected override GameLevelWrapper _GetWrapper() => default;

    protected override EntityDataIndexContainerSerializationSystem _GetOrCreateContainerSystem() => World.GetOrCreateSystemManaged<GameDataLevelContainerSerializationSystem>();
}

[DisableAutoCreation]
public partial class GameDataLevelContainerDeserializationSystem : EntityDataIndexContainerDeserializationSystem
{
    private GameSoulSystem __soulSystem;

    protected override void OnCreate()
    {
        base.OnCreate();

        __soulSystem = World.GetOrCreateSystemManaged<GameSoulSystem>();
    }

    protected override NativeArray<Hash128> _GetGuids()
    {
        return __soulSystem.guids;
    }
}

[DisableAutoCreation, UpdateAfter(typeof(GameDataLevelContainerDeserializationSystem))]
public partial class GameDataLevelDeserializationSystem : EntityDataIndexComponentDeserializationSystem<GameLevel, GameLevelWrapper>
{
    protected override GameLevelWrapper _GetWrapper() => default;

    protected override EntityDataIndexContainerDeserializationSystem _GetOrCreateContainerSystem()
    {
        return World.GetOrCreateSystemManaged<GameDataLevelContainerDeserializationSystem>();
    }
}