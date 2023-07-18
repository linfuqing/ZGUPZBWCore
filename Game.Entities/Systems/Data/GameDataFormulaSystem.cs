using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using ZG;

#region GameFormulaManager
[assembly: Unity.Jobs.RegisterGenericJobType(typeof(EntityDataIndexBufferInit<GameFormula, GameFormulaWrapper>))]

[assembly: Unity.Jobs.RegisterGenericJobType(typeof(EntityDataContainerDeserialize<GameDataFormulaContainerDeserializationSystem.Deserializer>))]
//[assembly: EntityDataSerialize(typeof(GameFormulaManager), typeof(GameDataFormulaContainerSerializationSystem))]
[assembly: EntityDataDeserialize(typeof(GameFormulaManager), typeof(GameDataFormulaContainerDeserializationSystem), (int)GameDataConstans.Version)]
#endregion

#region GameFormula
[assembly: Unity.Jobs.RegisterGenericJobType(typeof(EntityDataComponentSerialize<EntityDataSerializationIndexBufferSystemCore<GameFormula, GameFormulaWrapper>.Serializer, EntityDataSerializationIndexBufferSystemCore<GameFormula, GameFormulaWrapper>.SerializerFactory>))]
[assembly: Unity.Jobs.RegisterGenericJobType(typeof(EntityDataComponentDeserialize<GameDataFormulaDeserializationSystem.Deserializer, GameDataFormulaDeserializationSystem.DeserializerFactory>))]
//[assembly: EntityDataSerialize(typeof(GameFormula), typeof(GameDataFormulaSerializationSystem))]
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

    public void Serialize(ref EntityDataWriter writer, in GameFormula data, int guidIndex)
    {
        EntityDataIndexReadWriteWrapperUtility.Serialize(ref this, ref writer, data, guidIndex);
    }

    public GameFormula Deserialize(ref EntityDataReader reader, in NativeArray<int>.ReadOnly indices)
    {
        return EntityDataIndexReadWriteWrapperUtility.Deserialize<GameFormula, GameFormulaWrapper>(ref this, ref reader, indices);
    }
}

public struct GameDataFormulaContainer : IComponentData
{
    public NativeArray<Hash128>.ReadOnly guids;
}

/*[DisableAutoCreation]
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
}*/

[BurstCompile,
    EntityDataSerializationSystem(typeof(GameFormulaManager)),
    CreateAfter(typeof(EntityDataSerializationInitializationSystem)),
    UpdateInGroup(typeof(EntityDataSerializationSystemGroup)), AutoCreateIn("Server")]
public partial struct GameDataFormulaContainerSerializationSystem : ISystem, IEntityDataSerializationIndexContainerSystem
{
    private EntityDataSerializationIndexContainerBufferSystemCore<GameFormula> __core;

    public SharedHashMap<int, int> guidIndices => __core.guidIndices;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __core = new EntityDataSerializationIndexContainerBufferSystemCore<GameFormula>(ref state);
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

        GameFormulaWrapper wrapper;
        __core.Update(guids, ref wrapper, ref state);
    }
}

[BurstCompile,
    EntityDataSerializationSystem(typeof(GameFormula)),
    CreateAfter(typeof(GameDataFormulaContainerSerializationSystem)),
    UpdateInGroup(typeof(EntityDataSerializationSystemGroup)), AutoCreateIn("Server"), 
    UpdateAfter(typeof(GameDataFormulaContainerSerializationSystem))]
public partial struct GameDataFormulaSerializationSystem : ISystem
{
    private EntityDataSerializationIndexBufferSystemCore<GameFormula, GameFormulaWrapper> __core;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __core = EntityDataSerializationIndexBufferSystemCore<GameFormula, GameFormulaWrapper>.Create<GameDataFormulaContainerSerializationSystem>(ref state);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        __core.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        GameFormulaWrapper wrapper;
        __core.Update(ref wrapper, ref state);
    }
}

[DisableAutoCreation]
public partial class GameDataFormulaContainerDeserializationSystem : EntityDataIndexContainerDeserializationSystem
{
    protected override NativeArray<Hash128>.ReadOnly _GetGuids() => SystemAPI.GetSingleton<GameDataFormulaContainer>().guids;
}

[DisableAutoCreation, UpdateAfter(typeof(GameDataFormulaContainerDeserializationSystem))]
public partial class GameDataFormulaDeserializationSystem : EntityDataIndexBufferDeserializationSystem<GameFormula, GameFormulaWrapper>
{
    protected override GameFormulaWrapper _GetWrapper() => default;

    protected override EntityDataIndexContainerDeserializationSystem _GetOrCreateContainerSystem() => World.GetOrCreateSystemManaged<GameDataFormulaContainerDeserializationSystem>();
}
