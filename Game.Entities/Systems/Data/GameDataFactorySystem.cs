using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using ZG;

#region GameFormulaFactoryStatusManager
[assembly: Unity.Jobs.RegisterGenericJobType(typeof(EntityDataIndexComponentInit<GameFormulaFactoryStatus, GameFormulaFactoryStatusWrapper>))]

//[assembly: Unity.Jobs.RegisterGenericJobType(typeof(EntityDataContainerSerialize<GameFormulaFactoryStatusContainerSerializationSystem.Serializer>))]
[assembly: Unity.Jobs.RegisterGenericJobType(typeof(EntityDataContainerDeserialize<GameFormulaFactoryStatusContainerDeserializationSystem.Deserializer>))]
//[assembly: EntityDataSerialize(typeof(GameFactoryManager), typeof(GameFormulaFactoryStatusContainerSerializationSystem))]
[assembly: EntityDataDeserialize(typeof(GameFactoryManager), typeof(GameFormulaFactoryStatusContainerDeserializationSystem), (int)GameDataConstans.Version)]
#endregion

#region GameFormulaFactoryStatus
[assembly: Unity.Jobs.RegisterGenericJobType(typeof(EntityDataComponentSerialize<EntityDataSerializationIndexComponentDataSystemCore<GameFormulaFactoryStatus, GameFormulaFactoryStatusWrapper>.Serializer, EntityDataSerializationIndexComponentDataSystemCore<GameFormulaFactoryStatus, GameFormulaFactoryStatusWrapper>.SerializerFactory>))]
[assembly: Unity.Jobs.RegisterGenericJobType(typeof(EntityDataComponentDeserialize<GameDataFactoryDeserializationSystem.Deserializer, GameDataFactoryDeserializationSystem.DeserializerFactory>))]
//[assembly: EntityDataSerialize(typeof(GameFormulaFactoryStatus), typeof(GameDataFactorySerializationSystem))]
[assembly: EntityDataDeserialize(typeof(GameFormulaFactoryStatus), typeof(GameDataFactoryDeserializationSystem), (int)GameDataConstans.Version)]
#endregion

#region GameFormulaFactoryTime
//[assembly: Unity.Jobs.RegisterGenericJobType(typeof(EntityDataComponentSerialize<ComponentDataSerializationSystem<GameFormulaFactoryTime>.Serializer, ComponentDataSerializationSystem<GameFormulaFactoryTime>.SerializerFactory>))]
[assembly: Unity.Jobs.RegisterGenericJobType(typeof(EntityDataComponentDeserialize<ComponentDataDeserializationSystem<GameFormulaFactoryTime>.Deserializer, ComponentDataDeserializationSystem<GameFormulaFactoryTime>.DeserializerFactory>))]
//[assembly: EntityDataSerialize(typeof(GameFormulaFactoryTime))]
[assembly: EntityDataDeserialize(typeof(GameFormulaFactoryTime), (int)GameDataConstans.Version)]
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

    public void Serialize(ref EntityDataWriter writer, in GameFormulaFactoryStatus data, int guidIndex)
    {
        EntityDataIndexReadWriteWrapperUtility.Serialize(ref this, ref writer, data, guidIndex);
    }

    public GameFormulaFactoryStatus Deserialize(ref EntityDataReader reader, in NativeArray<int>.ReadOnly indices)
    {
        return EntityDataIndexReadWriteWrapperUtility.Deserialize<GameFormulaFactoryStatus, GameFormulaFactoryStatusWrapper>(ref this, ref reader, indices);
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
public partial struct GameDataFormulaFactorySerializationSystem : ISystem
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

[DisableAutoCreation]
public partial class GameFormulaFactoryStatusContainerDeserializationSystem : EntityDataIndexContainerDeserializationSystem
{
    protected override NativeArray<Hash128>.ReadOnly _GetGuids() => SystemAPI.GetSingleton<GameDataFormulaContainer>().guids;
}

[DisableAutoCreation, UpdateAfter(typeof(GameFormulaFactoryStatusContainerDeserializationSystem))]
public partial class GameDataFactoryDeserializationSystem : EntityDataIndexComponentDeserializationSystem<GameFormulaFactoryStatus, GameFormulaFactoryStatusWrapper>
{
    protected override GameFormulaFactoryStatusWrapper _GetWrapper() => default;

    protected override EntityDataIndexContainerDeserializationSystem _GetOrCreateContainerSystem() => World.GetOrCreateSystemManaged<GameFormulaFactoryStatusContainerDeserializationSystem>();
}
