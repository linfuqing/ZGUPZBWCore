using Unity.Collections;
using Unity.Entities;
using ZG;

#region GameFormulaManager
[assembly: Unity.Jobs.RegisterGenericJobType(typeof(EntityDataIndexBufferInit<GameFormula, GameFormulaWrapper>))]

[assembly: Unity.Jobs.RegisterGenericJobType(typeof(EntityDataContainerSerialize<GameDataFormulaContainerSerializationSystem.Serializer>))]
[assembly: Unity.Jobs.RegisterGenericJobType(typeof(EntityDataContainerDeserialize<GameDataFormulaContainerDeserializationSystem.Deserializer>))]
[assembly: EntityDataSerialize(typeof(GameFormulaManager), typeof(GameDataFormulaContainerSerializationSystem))]
[assembly: EntityDataDeserialize(typeof(GameFormulaManager), typeof(GameDataFormulaContainerDeserializationSystem), (int)GameDataConstans.Version)]
#endregion

#region GameFormula
[assembly: Unity.Jobs.RegisterGenericJobType(typeof(EntityDataComponentSerialize<GameDataFormulaSerializationSystem.Serializer, GameDataFormulaSerializationSystem.SerializerFactory>))]
[assembly: Unity.Jobs.RegisterGenericJobType(typeof(EntityDataComponentDeserialize<GameDataFormulaDeserializationSystem.Deserializer, GameDataFormulaDeserializationSystem.DeserializerFactory>))]
[assembly: EntityDataSerialize(typeof(GameFormula), typeof(GameDataFormulaSerializationSystem))]
[assembly: EntityDataDeserialize(typeof(GameFormula), typeof(GameDataFormulaDeserializationSystem), (int)GameDataConstans.Version)]
#endregion

public struct GameFormulaWrapper : IEntityDataIndexReadWriteWrapper<GameFormula>
{
    public bool TryGet(in GameFormula data, out int index)
    {
        index = data.index;

        return data.index != -1;
    }

    public void Invail(ref GameFormula data)
    {
        data.index = -1;
    }

    public void Set(ref GameFormula data, int index)
    {
        data.index = index;
    }
}

[DisableAutoCreation]
public partial class GameDataFormulaSystem : SystemBase
{
    private NativeArray<Hash128> __guids;

    public NativeArray<Hash128> guids => __guids;

    public void Create(Hash128[] guids)
    {
        __guids = new NativeArray<Hash128>(guids, Allocator.Persistent);
    }

    protected override void OnDestroy()
    {
        if(__guids.IsCreated)
            __guids.Dispose();

        base.OnDestroy();
    }

    protected override void OnUpdate()
    {
        throw new System.NotImplementedException();
    }
}

[DisableAutoCreation]
public partial class GameDataFormulaContainerSerializationSystem : EntityDataIndexBufferContainerSerializationSystem<GameFormula, GameFormulaWrapper>
{
    private GameDataFormulaSystem __formulaSystem;
    private GameFormulaWrapper __wrapper;

    protected override void OnCreate()
    {
        base.OnCreate();

        __formulaSystem = World.GetOrCreateSystemManaged<GameDataFormulaSystem>();
    }

    protected override NativeArray<Hash128> _GetGuids() => __formulaSystem.guids;

    protected override ref GameFormulaWrapper _GetWrapper() => ref __wrapper;
}

[DisableAutoCreation, UpdateAfter(typeof(GameDataFormulaContainerSerializationSystem))]
public partial class GameDataFormulaSerializationSystem : EntityDataIndexBufferSerializationSystem<GameFormula, GameFormulaWrapper>
{
    protected override GameFormulaWrapper _GetWrapper() => default;

    protected override EntityDataIndexContainerSerializationSystem _GetOrCreateContainerSystem() => World.GetOrCreateSystemManaged<GameDataFormulaContainerSerializationSystem>();
}


[DisableAutoCreation]
public partial class GameDataFormulaContainerDeserializationSystem : EntityDataIndexContainerDeserializationSystem
{
    private GameDataFormulaSystem __formulaSystem;

    protected override void OnCreate()
    {
        base.OnCreate();

        __formulaSystem = World.GetOrCreateSystemManaged<GameDataFormulaSystem>();
    }

    protected override NativeArray<Hash128> _GetGuids() => __formulaSystem.guids;
}

[DisableAutoCreation, UpdateAfter(typeof(GameDataFormulaContainerDeserializationSystem))]
public partial class GameDataFormulaDeserializationSystem : EntityDataIndexBufferDeserializationSystem<GameFormula, GameFormulaWrapper>
{
    protected override GameFormulaWrapper _GetWrapper() => default;

    protected override EntityDataIndexContainerDeserializationSystem _GetOrCreateContainerSystem() => World.GetOrCreateSystemManaged<GameDataFormulaContainerDeserializationSystem>();
}
