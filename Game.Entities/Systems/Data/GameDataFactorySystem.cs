using System;
using Unity.Collections;
using Unity.Entities;
using ZG;

#region GameFormulaFactoryStatusManager
[assembly: Unity.Jobs.RegisterGenericJobType(typeof(EntityDataIndexComponentInit<GameFormulaFactoryStatus, GameFormulaFactoryStatusWrapper>))]

[assembly: Unity.Jobs.RegisterGenericJobType(typeof(EntityDataContainerSerialize<GameFormulaFactoryStatusContainerSerializationSystem.Serializer>))]
[assembly: Unity.Jobs.RegisterGenericJobType(typeof(EntityDataContainerDeserialize<GameFormulaFactoryStatusContainerDeserializationSystem.Deserializer>))]
[assembly: EntityDataSerialize(typeof(GameFactoryManager), typeof(GameFormulaFactoryStatusContainerSerializationSystem))]
[assembly: EntityDataDeserialize(typeof(GameFactoryManager), typeof(GameFormulaFactoryStatusContainerDeserializationSystem), (int)GameDataConstans.Version)]
#endregion

#region GameFormulaFactoryStatus
[assembly: Unity.Jobs.RegisterGenericJobType(typeof(EntityDataComponentSerialize<GameDataFactorySerializationSystem.Serializer, GameDataFactorySerializationSystem.SerializerFactory>))]
[assembly: Unity.Jobs.RegisterGenericJobType(typeof(EntityDataComponentDeserialize<GameDataFactoryDeserializationSystem.Deserializer, GameDataFactoryDeserializationSystem.DeserializerFactory>))]
[assembly: EntityDataSerialize(typeof(GameFormulaFactoryStatus), typeof(GameDataFactorySerializationSystem))]
[assembly: EntityDataDeserialize(typeof(GameFormulaFactoryStatus), typeof(GameDataFactoryDeserializationSystem), (int)GameDataConstans.Version)]
#endregion

#region GameFormulaFactoryTime
[assembly: Unity.Jobs.RegisterGenericJobType(typeof(EntityDataComponentSerialize<ComponentDataSerializationSystem<GameFormulaFactoryTime>.Serializer, ComponentDataSerializationSystem<GameFormulaFactoryTime>.SerializerFactory>))]
[assembly: Unity.Jobs.RegisterGenericJobType(typeof(EntityDataComponentDeserialize<ComponentDataDeserializationSystem<GameFormulaFactoryTime>.Deserializer, ComponentDataDeserializationSystem<GameFormulaFactoryTime>.DeserializerFactory>))]
[assembly: EntityDataSerialize(typeof(GameFormulaFactoryTime))]
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
}

public struct GameFactoryManager
{

}

[DisableAutoCreation]
public partial class GameFormulaFactoryStatusContainerSerializationSystem : EntityDataIndexComponentContainerSerializationSystem<GameFormulaFactoryStatus, GameFormulaFactoryStatusWrapper>
{
    private GameDataFormulaSystem __formulaSystem;
    private GameFormulaFactoryStatusWrapper __wrapper;

    protected override void OnCreate()
    {
        base.OnCreate();

        __formulaSystem = World.GetOrCreateSystemManaged<GameDataFormulaSystem>();
    }

    protected override ref GameFormulaFactoryStatusWrapper _GetWrapper() => ref __wrapper;

    protected override NativeArray<Hash128> _GetGuids() => __formulaSystem.guids;
}

[DisableAutoCreation, UpdateAfter(typeof(GameFormulaFactoryStatusContainerSerializationSystem))]
public partial class GameDataFactorySerializationSystem : EntityDataIndexComponentSerializationSystem<GameFormulaFactoryStatus, GameFormulaFactoryStatusWrapper>
{
    protected override GameFormulaFactoryStatusWrapper _GetWrapper() => default;

    protected override EntityDataIndexContainerSerializationSystem _GetOrCreateContainerSystem() => World.GetOrCreateSystemManaged<GameFormulaFactoryStatusContainerSerializationSystem>();
}

[DisableAutoCreation]
public partial class GameFormulaFactoryStatusContainerDeserializationSystem : EntityDataIndexContainerDeserializationSystem
{
    private GameDataFormulaSystem __formulaSystem;

    protected override void OnCreate()
    {
        base.OnCreate();

        __formulaSystem = World.GetOrCreateSystemManaged<GameDataFormulaSystem>();
    }

    protected override NativeArray<Hash128> _GetGuids() => __formulaSystem.guids;
}

[DisableAutoCreation, UpdateAfter(typeof(GameFormulaFactoryStatusContainerDeserializationSystem))]
public partial class GameDataFactoryDeserializationSystem : EntityDataIndexComponentDeserializationSystem<GameFormulaFactoryStatus, GameFormulaFactoryStatusWrapper>
{
    protected override GameFormulaFactoryStatusWrapper _GetWrapper() => default;

    protected override EntityDataIndexContainerDeserializationSystem _GetOrCreateContainerSystem() => World.GetOrCreateSystemManaged<GameFormulaFactoryStatusContainerDeserializationSystem>();
}
