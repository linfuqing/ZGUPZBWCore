using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using ZG;

#region GameFormulaFactoryStatusManager
[assembly: RegisterGenericJobType(typeof(EntityDataIndexComponentInit<GameFormulaFactoryStatus, GameFormulaFactoryStatusContainerWrapper>))]

//[assembly: Unity.Jobs.RegisterGenericJobType(typeof(EntityDataContainerSerialize<GameFormulaFactoryStatusContainerSerializationSystem.Serializer>))]
//[assembly: Unity.Jobs.RegisterGenericJobType(typeof(EntityDataContainerDeserialize<GameFormulaFactoryStatusContainerDeserializationSystem.Deserializer>))]
//[assembly: EntityDataSerialize(typeof(GameFactoryManager), typeof(GameFormulaFactoryStatusContainerSerializationSystem))]
//[assembly: EntityDataDeserialize(typeof(GameFactoryManager), typeof(GameFormulaFactoryStatusContainerDeserializationSystem), (int)GameDataConstans.Version)]
#endregion

#region GameFormulaFactoryStatus
[assembly: RegisterGenericJobType(typeof(EntityDataComponentSerialize<EntityDataSerializationIndexComponentDataSystemCore<GameFormulaFactoryStatus, GameFormulaFactoryStatusSerializationWrapper>.Serializer, EntityDataSerializationIndexComponentDataSystemCore<GameFormulaFactoryStatus, GameFormulaFactoryStatusSerializationWrapper>.SerializerFactory>))]
[assembly: RegisterGenericJobType(typeof(EntityDataComponentDeserialize<GameDataEntityComponentDataDeserializer<GameFormulaFactoryStatus, GameDataFactoryStatusDeserializationSystem.Deserializer>, GameDataEntityComponentDataDeserializerFactory<GameFormulaFactoryStatus, GameDataFactoryStatusDeserializationSystem.Deserializer>>))]
[assembly: RegisterGenericJobType(typeof(GameDataEntityComponentDataBuild<GameFormulaFactoryStatus, GameDataFactoryStatusDeserializationSystem.Builder>))]
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

public struct GameFormulaFactoryStatusData
{
    public GameFormulaFactoryStatus.Status value;

    public int formulaIndex;

    public int level;

    public int count;

    public int usedCount;

    public int entityIndex;
}

public struct GameFormulaFactoryStatusContainerWrapper : 
    IEntityDataIndexReadOnlyWrapper<GameFormulaFactoryStatus>
{
    public bool TryGet(in GameFormulaFactoryStatus data, out int index)
    {
        index = data.formulaIndex;

        return data.formulaIndex != -1;
    }
}

public struct GameFormulaFactoryStatusSerializationWrapper : 
    IEntityDataSerializationIndexWrapper<GameFormulaFactoryStatus>, 
    IEntityDataIndexReadWriteWrapper<GameFormulaFactoryStatusData>
{
    [ReadOnly]
    public ComponentLookup<EntityDataIdentity> identities;

    public bool TryGet(in GameFormulaFactoryStatusData data, out int index)
    {
        index = data.formulaIndex;

        return data.formulaIndex != -1;
    }

    /*public void Invail(ref GameFormulaFactoryStatus data)
    {
        data.formulaIndex = -1;
    }

    public void Set(ref GameFormulaFactoryStatus data, int index)
    {
        data.formulaIndex = index;
    }*/

    public void Invail(ref GameFormulaFactoryStatusData data)
    {
        data.formulaIndex = -1;
    }

    public void Set(ref GameFormulaFactoryStatusData data, int index)
    {
        data.formulaIndex = index;
    }
    
    public void Serialize(
        ref EntityDataWriter writer, 
        in GameFormulaFactoryStatus data, 
        in SharedHashMap<int, int>.Reader guidIndices, 
        in SharedHashMap<Hash128, int>.Reader entityIndices)
    {
        GameFormulaFactoryStatusData instance;
        instance.value = data.value;
        instance.formulaIndex = data.formulaIndex;
        instance.level = data.level;
        instance.count = data.count;
        instance.usedCount = data.usedCount;
        instance.entityIndex = identities.HasComponent(data.entity) && entityIndices.TryGetValue(identities[data.entity].guid, out int entityIndex)
            ? entityIndex
            : -1;
        
        EntityDataIndexReadWriteWrapperUtility.Serialize(ref this, ref writer, instance, guidIndices);
    }
}

public struct GameFormulaFactoryStatusDeserializationWrapper : 
    IEntityDataIndexReadWriteWrapper<GameFormulaFactoryStatusData>
{
    public bool TryGet(in GameFormulaFactoryStatusData data, out int index)
    {
        index = data.formulaIndex;

        return data.formulaIndex != -1;
    }

    public void Invail(ref GameFormulaFactoryStatusData data)
    {
        data.formulaIndex = -1;
    }

    public void Set(ref GameFormulaFactoryStatusData data, int index)
    {
        data.formulaIndex = index;
    }
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

        GameFormulaFactoryStatusContainerWrapper wrapper;
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
    private ComponentLookup<EntityDataIdentity> __identities;

    private EntityDataSerializationIndexComponentDataSystemCore<GameFormulaFactoryStatus, GameFormulaFactoryStatusSerializationWrapper> __core;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __identities = state.GetComponentLookup<EntityDataIdentity>(true);
        
        __core = EntityDataSerializationIndexComponentDataSystemCore<GameFormulaFactoryStatus, GameFormulaFactoryStatusSerializationWrapper>.Create<GameFormulaFactoryStatusContainerSerializationSystem>(ref state);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        __core.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        GameFormulaFactoryStatusSerializationWrapper wrapper;
        wrapper.identities = __identities.UpdateAsRef(ref state);
        
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
    public struct Deserializer : IGameDataEntityCompoentDeserializer<GameFormulaFactoryStatus>
    {
        [ReadOnly]
        public SharedList<int>.Reader guidIndices;

        public bool Fallback(in Entity entity, in GameFormulaFactoryStatus value)
        {
            return false;
        }

        public int Deserialize(in Entity entity, ref GameFormulaFactoryStatus value, ref EntityDataReader reader)
        {
            GameFormulaFactoryStatusDeserializationWrapper wrapper;
            var instance = EntityDataIndexReadWriteWrapperUtility.Deserialize<GameFormulaFactoryStatusData, GameFormulaFactoryStatusDeserializationWrapper>(
                ref wrapper, 
                ref reader, 
                guidIndices.AsArray().AsReadOnly());

            value.value = instance.value;
            value.formulaIndex = instance.formulaIndex;
            value.level = instance.level;
            value.count = instance.count;
            value.usedCount = instance.usedCount;
            value.entity = Entity.Null;

            return instance.entityIndex;
        }
    }
    
    public struct Builder : IGameDataEntityCompoentBuilder<GameFormulaFactoryStatus>
    {
        public void Set(ref GameFormulaFactoryStatus value, in Entity entity, in Entity instance)
        {
            value.entity = entity;
        }
    }
    
    private GameDataEntityComponentDataDeserializationSystemCore __core;

    private SharedList<int> __guidIndices;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __core = new GameDataEntityComponentDataDeserializationSystemCore(TypeManager.GetTypeIndex<GameFormulaFactoryStatus>(), ref state);
        
        __guidIndices = state.WorldUnmanaged.GetExistingSystemUnmanaged<GameFormulaFactoryStatusContainerDeserializationSystem>().guidIndices;
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        __core.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        ref var guidIndicesJobManager = ref __guidIndices.lookupJobManager;
        
        state.Dependency = JobHandle.CombineDependencies(state.Dependency, guidIndicesJobManager.readOnlyJobHandle);
        
        Deserializer deserializer;
        deserializer.guidIndices = __guidIndices.reader;
        
        Builder builder;
        __core.Update<GameFormulaFactoryStatus, Deserializer, Builder>(
            ref deserializer, 
            ref builder, 
            ref state, 
            out var deserializeJobHandle);
        
        guidIndicesJobManager.AddReadOnlyDependency(deserializeJobHandle);
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
