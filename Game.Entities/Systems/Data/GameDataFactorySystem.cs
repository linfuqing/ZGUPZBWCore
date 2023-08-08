using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using ZG;

#region GameFormulaFactoryStatusManager
[assembly: Unity.Jobs.RegisterGenericJobType(typeof(EntityDataIndexComponentInit<GameFormulaFactoryStatus, GameFormulaFactoryStatusWrapper>))]

//[assembly: Unity.Jobs.RegisterGenericJobType(typeof(EntityDataContainerSerialize<GameFormulaFactoryStatusContainerSerializationSystem.Serializer>))]
//[assembly: Unity.Jobs.RegisterGenericJobType(typeof(EntityDataContainerDeserialize<GameFormulaFactoryStatusContainerDeserializationSystem.Deserializer>))]
//[assembly: EntityDataSerialize(typeof(GameFactoryManager), typeof(GameFormulaFactoryStatusContainerSerializationSystem))]
//[assembly: EntityDataDeserialize(typeof(GameFactoryManager), typeof(GameFormulaFactoryStatusContainerDeserializationSystem), (int)GameDataConstans.Version)]
#endregion

#region GameFormulaFactoryStatus
[assembly: Unity.Jobs.RegisterGenericJobType(typeof(EntityDataComponentSerialize<EntityDataSerializationIndexComponentDataSystemCore<GameFormulaFactoryStatus, GameFormulaFactoryStatusWrapper>.Serializer, EntityDataSerializationIndexComponentDataSystemCore<GameFormulaFactoryStatus, GameFormulaFactoryStatusWrapper>.SerializerFactory>))]
[assembly: Unity.Jobs.RegisterGenericJobType(typeof(EntityDataComponentDeserialize<EntityDataDeserializationIndexComponentDataSystemCore<GameFormulaFactoryStatus, GameFormulaFactoryStatusWrapper>.Deserializer, EntityDataDeserializationIndexComponentDataSystemCore<GameFormulaFactoryStatus, GameFormulaFactoryStatusWrapper>.DeserializerFactory>))]
//[assembly: EntityDataSerialize(typeof(GameFormulaFactoryStatus), typeof(GameDataFactorySerializationSystem))]
//[assembly: EntityDataDeserialize(typeof(GameFormulaFactoryStatus), typeof(GameDataFactoryDeserializationSystem), (int)GameDataConstans.Version)]
#endregion

#region GameFormulaFactoryTime
//[assembly: Unity.Jobs.RegisterGenericJobType(typeof(EntityDataComponentSerialize<ComponentDataSerializationSystem<GameFormulaFactoryTime>.Serializer, ComponentDataSerializationSystem<GameFormulaFactoryTime>.SerializerFactory>))]
//[assembly: Unity.Jobs.RegisterGenericJobType(typeof(EntityDataComponentDeserialize<ComponentDataDeserializationSystem<GameFormulaFactoryTime>.Deserializer, ComponentDataDeserializationSystem<GameFormulaFactoryTime>.DeserializerFactory>))]
//[assembly: EntityDataSerialize(typeof(GameFormulaFactoryTime))]
//[assembly: EntityDataDeserialize(typeof(GameFormulaFactoryTime), (int)GameDataConstans.Version)]
#endregion

/*[Serializable]
public struct GameFactory : IComponentData
{
    public int status;

    public int formulaIndex;

    public int level;
}*/

public struct GameFormulaFactoryStatusWrapper : IEntityDataIndexReadWriteWrapper<GameFormulaFactoryStatus>
{
    public bool TryGet(in GameFormulaFactoryStatus data, out int index)
    {
        index = data.formulaIndex;

        return data.formulaIndex != -1;
    }

    public void Invail(ref GameFormulaFactoryStatus data)
    {
        data.formulaIndex = -1;
    }

    public void Set(ref GameFormulaFactoryStatus data, int index)
    {
        data.formulaIndex = index;
    }

    public void Serialize(ref EntityDataWriter writer, in GameFormulaFactoryStatus data, in SharedHashMap<int, int>.Reader guidIndices)
    {
        EntityDataIndexReadWriteWrapperUtility.Serialize(ref this, ref writer, data, guidIndices);
    }

    public GameFormulaFactoryStatus Deserialize(in Entity entity, in NativeArray<int>.ReadOnly guidIndices, ref EntityDataReader reader)
    {
        return EntityDataIndexReadWriteWrapperUtility.Deserialize<GameFormulaFactoryStatus, GameFormulaFactoryStatusWrapper>(ref this, ref reader, guidIndices);
    }

    /*public void Invail(ref GameFormulaFactoryStatus data)
    {
        data.formulaIndex = -1;
    }

    public void Set(ref GameFormulaFactoryStatus data, int index)
    {
        data.formulaIndex = index;
    }*/
}

public struct GameFactoryManager
{

}

[BurstCompile,
    EntityDataSerializationSystem(typeof(GameFactoryManager)),
    CreateAfter(typeof(EntityDataSerializationInitializationSystem)),
    UpdateInGroup(typeof(EntityDataSerializationSystemGroup)), AutoCreateIn("Server")]
public partial struct GameFormulaFactoryStatusContainerSerializationSystem : ISystem, IEntityDataSerializationIndexContainerSystem
{
    private EntityDataSerializationIndexContainerComponentDataSystemCore<GameFormulaFactoryStatus> __core;

    public SharedHashMap<int, int> guidIndices => __core.guidIndices;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __core = new EntityDataSerializationIndexContainerComponentDataSystemCore<GameFormulaFactoryStatus>(ref state);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        __core.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var guids = SystemAPI.GetSingleton<GameDataFormulaContainer>().guids;

        GameFormulaFactoryStatusWrapper wrapper;
        __core.Update(guids, ref wrapper, ref state);
    }
}

[BurstCompile,
    EntityDataSerializationSystem(typeof(GameFormulaFactoryStatus)),
    CreateAfter(typeof(GameFormulaFactoryStatusContainerSerializationSystem)),
    UpdateInGroup(typeof(EntityDataSerializationSystemGroup)), AutoCreateIn("Server"), 
    UpdateAfter(typeof(GameFormulaFactoryStatusContainerSerializationSystem))]
public partial struct GameDataFormulaFactoryStatusSerializationSystem : ISystem
{
    private EntityDataSerializationIndexComponentDataSystemCore<GameFormulaFactoryStatus, GameFormulaFactoryStatusWrapper> __core;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __core = EntityDataSerializationIndexComponentDataSystemCore<GameFormulaFactoryStatus, GameFormulaFactoryStatusWrapper>.Create<GameFormulaFactoryStatusContainerSerializationSystem>(ref state);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        //__core.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        GameFormulaFactoryStatusWrapper wrapper;
        __core.Update(ref wrapper, ref state);
    }
}

[BurstCompile,
    EntityDataSerializationSystem(typeof(GameFormulaFactoryTime)),
    CreateAfter(typeof(EntityDataSerializationInitializationSystem)),
    UpdateInGroup(typeof(EntityDataSerializationSystemGroup)), AutoCreateIn("Server")]
public partial struct GameDataFormulaFactoryTimeSerializationSystem : ISystem
{
    private EntityDataSerializationSystemCoreEx __core;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __core = EntityDataSerializationSystemCoreEx.Create<GameFormulaFactoryTime>(ref state);
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
    EntityDataDeserializationSystem(typeof(GameFactoryManager), (int)GameDataConstans.Version),
    CreateAfter(typeof(EntityDataDeserializationContainerSystem)),
    UpdateInGroup(typeof(EntityDataDeserializationSystemGroup)), AutoCreateIn("Server")]
public partial struct GameFormulaFactoryStatusContainerDeserializationSystem : ISystem, IEntityDataDeserializationIndexContainerSystem
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
        __core.Update(SystemAPI.GetSingleton<GameDataFormulaContainer>().guids, ref state);
    }
}

[BurstCompile,
    EntityDataDeserializationSystem(typeof(GameFormulaFactoryStatus), (int)GameDataConstans.Version),
    CreateAfter(typeof(EntityDataDeserializationComponentSystem)),
    CreateAfter(typeof(GameFormulaFactoryStatusContainerDeserializationSystem)),
    UpdateInGroup(typeof(EntityDataDeserializationSystemGroup)),
    UpdateAfter(typeof(GameFormulaFactoryStatusContainerDeserializationSystem)), AutoCreateIn("Server")]
public partial struct GameDataFactoryStatusDeserializationSystem : ISystem
{
    private EntityDataDeserializationIndexComponentDataSystemCore<GameFormulaFactoryStatus, GameFormulaFactoryStatusWrapper> __core;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __core = EntityDataDeserializationIndexComponentDataSystemCore<GameFormulaFactoryStatus, GameFormulaFactoryStatusWrapper>.Create<GameFormulaFactoryStatusContainerDeserializationSystem>(ref state);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        __core.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        GameFormulaFactoryStatusWrapper wrapper;
        __core.Update(ref wrapper, ref state, true);
    }
}

[BurstCompile,
    EntityDataDeserializationSystem(typeof(GameFormulaFactoryTime), (int)GameDataConstans.Version),
    CreateAfter(typeof(EntityDataDeserializationComponentSystem)),
    UpdateInGroup(typeof(EntityDataDeserializationSystemGroup)), AutoCreateIn("Server")]
public partial struct GameDataFactoryTimeDeserializationSystem : ISystem
{
    private EntityDataDeserializationSystemCoreEx __core;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __core = EntityDataDeserializationSystemCoreEx.Create<GameFormulaFactoryTime>(ref state);
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
