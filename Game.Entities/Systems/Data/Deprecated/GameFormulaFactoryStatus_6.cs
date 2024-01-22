using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using ZG;


#region GameFormulaFactoryStatus
//[assembly: Unity.Jobs.RegisterGenericJobType(typeof(EntityDataComponentSerialize<EntityDataSerializationIndexComponentDataSystemCore<GameFormulaFactoryStatus_5, GameFormulaFactoryStatusWrapper>.Serializer, EntityDataSerializationIndexComponentDataSystemCore<GameFormulaFactoryStatus, GameFormulaFactoryStatusWrapper>.SerializerFactory>))]
[assembly: Unity.Jobs.RegisterGenericJobType(typeof(EntityDataComponentDeserialize<EntityDataDeserializationIndexComponentDataSystemCore<GameFormulaFactoryStatus, GameFormulaFactoryStatusWrapper_6>.Deserializer, EntityDataDeserializationIndexComponentDataSystemCore<GameFormulaFactoryStatus, GameFormulaFactoryStatusWrapper_6>.DeserializerFactory>))]
//[assembly: EntityDataSerialize(typeof(GameFormulaFactoryStatus), typeof(GameDataFactorySerializationSystem))]
//[assembly: EntityDataDeserialize(typeof(GameFormulaFactoryStatus), typeof(GameDataFactoryDeserializationSystem), (int)GameDataConstans.Version)]
#endregion

public struct GameFormulaFactoryStatus_6 : IComponentData
{
    public GameFormulaFactoryStatus.Status value;

    public int formulaIndex;

    public int level;
    
    public int count;

    public Entity entity;

    public GameFormulaFactoryStatus As()
    {
        GameFormulaFactoryStatus result;
        result.value = value;
        result.formulaIndex = formulaIndex;
        result.level = level;
        result.count = count;
        result.usedCount = 0;
        result.entity = entity;
        return result;
    }
}

public struct GameFormulaFactoryStatusWrapper_6 : IEntityDataDeserializationIndexWrapper<GameFormulaFactoryStatus>
{
    public GameFormulaFactoryStatus Deserialize(in Entity entity, in NativeArray<int>.ReadOnly guidIndices, ref EntityDataReader reader)
    {
        var instance = reader.Read<GameFormulaFactoryStatus_6>();

        if (instance.formulaIndex != -1)
            instance.formulaIndex = guidIndices[instance.formulaIndex];

        return instance.As();
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


[BurstCompile,
    EntityDataDeserializationSystem(typeof(GameFormulaFactoryStatus), 6),
    CreateAfter(typeof(EntityDataDeserializationComponentSystem)),
    CreateAfter(typeof(GameFormulaFactoryStatusContainerDeserializationSystem)),
    UpdateInGroup(typeof(EntityDataDeserializationSystemGroup)),
    UpdateAfter(typeof(GameFormulaFactoryStatusContainerDeserializationSystem)), AutoCreateIn("Server")]
public partial struct GameDataFactoryStatusDeserializationSystem_6 : ISystem
{
    private EntityDataDeserializationIndexComponentDataSystemCore<GameFormulaFactoryStatus, GameFormulaFactoryStatusWrapper_6> __core;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __core = EntityDataDeserializationIndexComponentDataSystemCore<GameFormulaFactoryStatus, GameFormulaFactoryStatusWrapper_6>.Create<GameFormulaFactoryStatusContainerDeserializationSystem>(ref state);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        __core.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        GameFormulaFactoryStatusWrapper_6 wrapper;
        __core.Update(ref wrapper, ref state, true);
    }
}
