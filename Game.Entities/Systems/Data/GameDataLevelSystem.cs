using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using ZG;

#region GameLevelManager
[assembly: RegisterGenericJobType(typeof(EntityDataIndexComponentInit<GameLevel, GameLevelWrapper>))]
[assembly: RegisterGenericJobType(typeof(EntityDataIndexBufferInit<GameSoul, GameSoulLevelWrapper>))]

//[assembly: RegisterGenericJobType(typeof(EntityDataContainerSerialize<GameDataLevelContainerSerializationSystem.Serializer>))]
[assembly: RegisterGenericJobType(typeof(EntityDataContainerDeserialize<GameDataLevelContainerDeserializationSystem.Deserializer>))]

//[assembly: EntityDataSerialize(typeof(GameLevelManager), typeof(GameDataLevelContainerSerializationSystem))]
[assembly: EntityDataDeserialize(typeof(GameLevelManager), typeof(GameDataLevelContainerDeserializationSystem), (int)GameDataConstans.Version)]
#endregion

#region GameLevel
[assembly: RegisterGenericJobType(typeof(EntityDataComponentSerialize<EntityDataSerializationIndexComponentDataSystemCore<GameLevel, GameLevelWrapper>.Serializer, EntityDataSerializationIndexComponentDataSystemCore<GameLevel, GameLevelWrapper>.SerializerFactory>))]
[assembly: RegisterGenericJobType(typeof(EntityDataComponentDeserialize<GameDataLevelDeserializationSystem.Deserializer, GameDataLevelDeserializationSystem.DeserializerFactory>))]
//[assembly: EntityDataSerialize(typeof(GameLevel), typeof(GameDataLevelSerializationSystem))]
[assembly: EntityDataDeserialize(typeof(GameLevel), typeof(GameDataLevelDeserializationSystem), (int)GameDataConstans.Version)]
#endregion

#region GameExp
//[assembly: RegisterGenericJobType(typeof(EntityDataComponentSerialize<ComponentDataSerializationSystem<GameExp>.Serializer, ComponentDataSerializationSystem<GameExp>.SerializerFactory>))]
[assembly: RegisterGenericJobType(typeof(EntityDataComponentDeserialize<ComponentDataDeserializationSystem<GameExp>.Deserializer, ComponentDataDeserializationSystem<GameExp>.DeserializerFactory>))]
//[assembly: EntityDataSerialize(typeof(GameExp))]
[assembly: EntityDataDeserialize(typeof(GameExp), (int)GameDataConstans.Version)]
#endregion

#region GamePower
//[assembly: RegisterGenericJobType(typeof(EntityDataComponentSerialize<ComponentDataSerializationSystem<GamePower>.Serializer, ComponentDataSerializationSystem<GamePower>.SerializerFactory>))]
[assembly: RegisterGenericJobType(typeof(EntityDataComponentDeserialize<ComponentDataDeserializationSystem<GamePower>.Deserializer, ComponentDataDeserializationSystem<GamePower>.DeserializerFactory>))]
//[assembly: EntityDataSerialize(typeof(GamePower))]
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

    public void Serialize(ref EntityDataWriter writer, in GameLevel data, in SharedHashMap<int, int>.Reader guidIndices)
    {
        EntityDataIndexReadWriteWrapperUtility.Serialize(ref this, ref writer, data, guidIndices);
    }

    public GameLevel Deserialize(ref EntityDataReader reader, in NativeArray<int>.ReadOnly indices)
    {
        return EntityDataIndexReadWriteWrapperUtility.Deserialize<GameLevel, GameLevelWrapper>(ref this, ref reader, indices);
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

    public void Serialize(ref EntityDataWriter writer, in GameSoul data, in SharedHashMap<int, int>.Reader guidIndices)
    {
        EntityDataIndexReadWriteWrapperUtility.Serialize(ref this, ref writer, data, guidIndices);
    }

    public GameSoul Deserialize(ref EntityDataReader reader, in NativeArray<int>.ReadOnly indices)
    {
        return EntityDataIndexReadWriteWrapperUtility.Deserialize<GameSoul, GameSoulLevelWrapper>(ref this, ref reader, indices);
    }
}

public struct GameDataSoulContainer : IComponentData
{
    public NativeArray<Hash128>.ReadOnly guids;
}

[BurstCompile,
    EntityDataSerializationSystem(typeof(GameLevelManager)),
    CreateAfter(typeof(EntityDataSerializationInitializationSystem)),
    UpdateInGroup(typeof(EntityDataSerializationSystemGroup)), AutoCreateIn("Server")]
public partial struct GameDataLevelContainerSerializationSystem : ISystem, IEntityDataSerializationIndexContainerSystem
{
    private EntityQuery __instanceGroup;
    private EntityQuery __soulGroup;

    private ComponentTypeHandle<GameLevel> __instanceType;
    private BufferTypeHandle<GameSoul> __soulType;
    private EntityDataSerializationIndexContainerSystemCore __core;

    public SharedHashMap<int, int> guidIndices => __core.guidIndices;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __instanceGroup = builder
                    .WithAll<GameLevel, EntityDataIdentity, EntityDataSerializable>()
                    .WithOptions(EntityQueryOptions.IncludeDisabledEntities)
                    .Build(ref state);

        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __soulGroup = builder
                    .WithAll<GameSoul, EntityDataIdentity, EntityDataSerializable>()
                    .WithOptions(EntityQueryOptions.IncludeDisabledEntities)
                    .Build(ref state);

        __instanceType = state.GetComponentTypeHandle<GameLevel>(true);
        __soulType = state.GetBufferTypeHandle<GameSoul>(true);

        __core = new EntityDataSerializationIndexContainerSystemCore(ref state);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        __core.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var jobHandle = __core.Clear(state.Dependency);

        var guids = SystemAPI.GetSingleton<GameDataSoulContainer>().guids;

        GameLevelWrapper instanceWrapper;
        jobHandle = __core.Update(__instanceGroup, guids, __instanceType.UpdateAsRef(ref state), ref instanceWrapper, jobHandle);

        GameSoulLevelWrapper soulWrapper;
        state.Dependency = __core.Update(__soulGroup, guids, __soulType.UpdateAsRef(ref state), ref soulWrapper, jobHandle);
    }
}

[BurstCompile,
    EntityDataSerializationSystem(typeof(GameLevel)),
    CreateAfter(typeof(GameDataLevelContainerSerializationSystem)),
    UpdateInGroup(typeof(EntityDataSerializationSystemGroup)), AutoCreateIn("Server"),
    UpdateAfter(typeof(GameDataLevelContainerSerializationSystem))]
public partial struct GameDataLevelSerializationSystem : ISystem
{
    private EntityDataSerializationIndexComponentDataSystemCore<GameLevel, GameLevelWrapper> __core;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __core = EntityDataSerializationIndexComponentDataSystemCore<GameLevel, GameLevelWrapper>.Create<GameDataLevelContainerSerializationSystem>(ref state);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        __core.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        GameLevelWrapper wrapper;
        __core.Update(ref wrapper, ref state);
    }
}

[BurstCompile,
    EntityDataSerializationSystem(typeof(GameExp)),
    CreateAfter(typeof(EntityDataSerializationInitializationSystem)),
    UpdateInGroup(typeof(EntityDataSerializationSystemGroup)), AutoCreateIn("Server")]
public partial struct GameDataExpSerializationSystem : ISystem
{
    private EntityDataSerializationSystemCoreEx __core;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __core = EntityDataSerializationSystemCoreEx.Create<GameExp>(ref state);
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
    EntityDataSerializationSystem(typeof(GamePower)),
    CreateAfter(typeof(EntityDataSerializationInitializationSystem)),
    UpdateInGroup(typeof(EntityDataSerializationSystemGroup)), AutoCreateIn("Server")]
public partial struct GameDataPowerSerializationSystem : ISystem
{
    private EntityDataSerializationSystemCoreEx __core;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __core = EntityDataSerializationSystemCoreEx.Create<GamePower>(ref state);
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

[DisableAutoCreation]
public partial class GameDataLevelContainerDeserializationSystem : EntityDataIndexContainerDeserializationSystem
{
    private GameSoulSystem __soulSystem;

    protected override void OnCreate()
    {
        base.OnCreate();

        __soulSystem = World.GetOrCreateSystemManaged<GameSoulSystem>();
    }

    protected override NativeArray<Hash128>.ReadOnly _GetGuids()
    {
        return SystemAPI.GetSingleton<GameDataSoulContainer>().guids;
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