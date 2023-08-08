using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using ZG;

#region GameLevelManager
[assembly: RegisterGenericJobType(typeof(EntityDataIndexComponentInit<GameLevel, GameLevelWrapper>))]
[assembly: RegisterGenericJobType(typeof(EntityDataIndexBufferInit<GameSoul, GameSoulLevelWrapper>))]

//[assembly: RegisterGenericJobType(typeof(EntityDataContainerSerialize<GameDataLevelContainerSerializationSystem.Serializer>))]
//[assembly: RegisterGenericJobType(typeof(EntityDataContainerDeserialize<GameDataLevelContainerDeserializationSystem.Deserializer>))]

//[assembly: EntityDataSerialize(typeof(GameLevelManager), typeof(GameDataLevelContainerSerializationSystem))]
//[assembly: EntityDataDeserialize(typeof(GameLevelManager), typeof(GameDataLevelContainerDeserializationSystem), (int)GameDataConstans.Version)]
#endregion

#region GameLevel
[assembly: RegisterGenericJobType(typeof(EntityDataComponentSerialize<EntityDataSerializationIndexComponentDataSystemCore<GameLevel, GameLevelWrapper>.Serializer, EntityDataSerializationIndexComponentDataSystemCore<GameLevel, GameLevelWrapper>.SerializerFactory>))]
[assembly: RegisterGenericJobType(typeof(EntityDataComponentDeserialize<EntityDataDeserializationIndexComponentDataSystemCore<GameLevel, GameLevelWrapper>.Deserializer, EntityDataDeserializationIndexComponentDataSystemCore<GameLevel, GameLevelWrapper>.DeserializerFactory>))]
//[assembly: EntityDataSerialize(typeof(GameLevel), typeof(GameDataLevelSerializationSystem))]
//[assembly: EntityDataDeserialize(typeof(GameLevel), typeof(GameDataLevelDeserializationSystem), (int)GameDataConstans.Version)]
#endregion

#region GameExp
//[assembly: RegisterGenericJobType(typeof(EntityDataComponentSerialize<ComponentDataSerializationSystem<GameExp>.Serializer, ComponentDataSerializationSystem<GameExp>.SerializerFactory>))]
//[assembly: RegisterGenericJobType(typeof(EntityDataComponentDeserialize<ComponentDataDeserializationSystem<GameExp>.Deserializer, ComponentDataDeserializationSystem<GameExp>.DeserializerFactory>))]
//[assembly: EntityDataSerialize(typeof(GameExp))]
//[assembly: EntityDataDeserialize(typeof(GameExp), (int)GameDataConstans.Version)]
#endregion

#region GamePower
//[assembly: RegisterGenericJobType(typeof(EntityDataComponentSerialize<ComponentDataSerializationSystem<GamePower>.Serializer, ComponentDataSerializationSystem<GamePower>.SerializerFactory>))]
//[assembly: RegisterGenericJobType(typeof(EntityDataComponentDeserialize<ComponentDataDeserializationSystem<GamePower>.Deserializer, ComponentDataDeserializationSystem<GamePower>.DeserializerFactory>))]
//[assembly: EntityDataSerialize(typeof(GamePower))]
//[assembly: EntityDataDeserialize(typeof(GamePower), (int)GameDataConstans.Version)]
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

    public GameLevel Deserialize(in Entity entity, in NativeArray<int>.ReadOnly guidIndices, ref EntityDataReader reader)
    {
        return EntityDataIndexReadWriteWrapperUtility.Deserialize<GameLevel, GameLevelWrapper>(ref this, ref reader, guidIndices);
    }
}

public struct GameItemLevelWrapper : IEntityDataIndexReadWriteWrapper<GameItemLevel>
{
    public bool TryGet(in GameItemLevel data, out int index)
    {
        index = data.handle - 1;

        return data.handle > 0;
    }

    public void Invail(ref GameItemLevel data)
    {
        data.handle = 0;
    }

    public void Set(ref GameItemLevel data, int index)
    {
        data.handle = index + 1;
    }

    public void Serialize(ref EntityDataWriter writer, in GameItemLevel data, in SharedHashMap<int, int>.Reader guidIndices)
    {
        EntityDataIndexReadWriteWrapperUtility.Serialize(ref this, ref writer, data, guidIndices);
    }

    public GameItemLevel Deserialize(in Entity entity, in NativeArray<int>.ReadOnly guidIndices, ref EntityDataReader reader)
    {
        return EntityDataIndexReadWriteWrapperUtility.Deserialize<GameItemLevel, GameItemLevelWrapper>(ref this, ref reader, guidIndices);
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

    public GameSoul Deserialize(in Entity entity, in NativeArray<int>.ReadOnly guidIndices, ref EntityDataReader reader)
    {
        return EntityDataIndexReadWriteWrapperUtility.Deserialize<GameSoul, GameSoulLevelWrapper>(ref this, ref reader, guidIndices);
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
    private EntityQuery __itemGroup;
    private EntityQuery __soulGroup;

    private ComponentTypeHandle<GameLevel> __instanceType;
    private ComponentTypeHandle<GameItemLevel> __itemType;
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
            __itemGroup = builder
                    .WithAll<GameItemLevel, EntityDataIdentity, EntityDataSerializable>()
                    .WithOptions(EntityQueryOptions.IncludeDisabledEntities)
                    .Build(ref state);

        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __soulGroup = builder
                    .WithAll<GameSoul, EntityDataIdentity, EntityDataSerializable>()
                    .WithOptions(EntityQueryOptions.IncludeDisabledEntities)
                    .Build(ref state);

        __instanceType = state.GetComponentTypeHandle<GameLevel>(true);
        __itemType = state.GetComponentTypeHandle<GameItemLevel>(true);
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

        GameItemLevelWrapper itemWrapper;
        jobHandle = __core.Update(__itemGroup, guids, __itemType.UpdateAsRef(ref state), ref itemWrapper, jobHandle);

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

[BurstCompile,
    EntityDataDeserializationSystem(typeof(GameLevelManager), (int)GameDataConstans.Version),
    CreateAfter(typeof(EntityDataDeserializationContainerSystem)),
    UpdateInGroup(typeof(EntityDataDeserializationSystemGroup)), AutoCreateIn("Server")]
public partial struct GameDataLevelContainerDeserializationSystem : ISystem, IEntityDataDeserializationIndexContainerSystem
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
        __core.Update(SystemAPI.GetSingleton<GameDataSoulContainer>().guids, ref state);
    }
}

[BurstCompile,
    EntityDataDeserializationSystem(typeof(GameLevel), (int)GameDataConstans.Version),
    CreateAfter(typeof(EntityDataDeserializationComponentSystem)),
    CreateAfter(typeof(GameDataLevelContainerDeserializationSystem)),
    UpdateInGroup(typeof(EntityDataDeserializationSystemGroup)),
    UpdateAfter(typeof(GameDataLevelContainerDeserializationSystem)), AutoCreateIn("Server")]
public partial struct GameDataLevelDeserializationSystem : ISystem
{
    private EntityDataDeserializationIndexComponentDataSystemCore<GameLevel, GameLevelWrapper> __core;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __core = EntityDataDeserializationIndexComponentDataSystemCore<GameLevel, GameLevelWrapper>.Create<GameDataLevelContainerDeserializationSystem>(ref state);
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
        __core.Update(ref wrapper, ref state, true);
    }
}

[BurstCompile,
    EntityDataDeserializationSystem(typeof(GameExp), (int)GameDataConstans.Version),
    CreateAfter(typeof(EntityDataDeserializationComponentSystem)),
    UpdateInGroup(typeof(EntityDataDeserializationSystemGroup)), AutoCreateIn("Server")]
public partial struct GameDataExpDeserializationSystem : ISystem
{
    private EntityDataDeserializationSystemCoreEx __core;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __core = EntityDataDeserializationSystemCoreEx.Create<GameExp>(ref state);
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
    EntityDataDeserializationSystem(typeof(GamePower), (int)GameDataConstans.Version),
    CreateAfter(typeof(EntityDataDeserializationComponentSystem)),
    UpdateInGroup(typeof(EntityDataDeserializationSystemGroup)), AutoCreateIn("Server")]
public partial struct GameDataPowerDeserializationSystem : ISystem
{
    private EntityDataDeserializationSystemCoreEx __core;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __core = EntityDataDeserializationSystemCoreEx.Create<GamePower>(ref state);
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