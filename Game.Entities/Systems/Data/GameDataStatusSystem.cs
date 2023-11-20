using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Jobs;
using Unity.Entities;
using Unity.Collections;
using ZG;

#region GameStatus
//[assembly: RegisterGenericJobType(typeof(EntityDataComponentSerialize<ComponentDataSerializationSystem<GameStatus>.Serializer, ComponentDataSerializationSystem<GameStatus>.SerializerFactory>))]
//[assembly: RegisterGenericJobType(typeof(EntityDataComponentDeserialize<ComponentDataDeserializationSystem<GameStatus>.Deserializer, ComponentDataDeserializationSystem<GameStatus>.DeserializerFactory>))]
//[assembly: EntityDataSerialize(typeof(GameStatus))]
//[assembly: EntityDataDeserialize(typeof(GameStatus), (int)GameDataConstans.Version)]
#endregion

/*public struct GameDataPresentation : IComponentData
{
    public int value;
}*/

public struct GameStatus : IComponentData
{
    public int value;
}

[AutoCreateIn("Server"), 
    BurstCompile,
    CreateAfter(typeof(GameDataStructChangeSystem)), 
    UpdateInGroup(typeof(GameDataSystemGroup))]
public partial struct GameDataStatusSystem : ISystem
{
    private struct Serialize
    {
        public bool isDeadlineTrigger;
        public bool isSerialized;

        [ReadOnly]
        public NativeArray<Entity> entityArray;

        [ReadOnly]
        public NativeArray<GameNodeStatus> states;

        public EntityCommandQueue<EntityCommandStructChange>.ParallelWriter addComponentQueue;
        public EntityCommandQueue<EntityCommandStructChange>.ParallelWriter removeComponentQueue;

        public void Execute(int index)
        {
            int value = states[index].value & (int)GameEntityStatus.Mask;
            switch((GameEntityStatus)value)
            {
                case GameEntityStatus.KnockedOut:
                    if(!isSerialized)
                        addComponentQueue.Enqueue(EntityCommandStructChange.Create<EntityDataSerializable>(entityArray[index]));
                    break;
                case GameEntityStatus.Dead:
                    if(!isDeadlineTrigger && isSerialized)
                        removeComponentQueue.Enqueue(EntityCommandStructChange.Create<EntityDataSerializable>(entityArray[index]));
                    break;
            }
        }
    }

    [BurstCompile]
    private struct SerializeEx : IJobChunk, IEntityCommandProducerJob
    {
        [ReadOnly]
        public EntityTypeHandle entityType;

        [ReadOnly]
        public ComponentTypeHandle<EntityDataSerializable> entityDataSerializableType;

        [ReadOnly]
        public ComponentTypeHandle<GameDataDeadlineTrigger> deadlineTriggerType;

        [ReadOnly]
        public ComponentTypeHandle<GameNodeStatus> statusType;

        public EntityCommandQueue<EntityCommandStructChange>.ParallelWriter addComponentQueue;
        public EntityCommandQueue<EntityCommandStructChange>.ParallelWriter removeComponentQueue;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Serialize serialize;
            serialize.isDeadlineTrigger = chunk.Has(ref deadlineTriggerType);
            serialize.isSerialized = chunk.Has(ref entityDataSerializableType);
            serialize.entityArray = chunk.GetNativeArray(entityType);
            serialize.states = chunk.GetNativeArray(ref statusType);
            serialize.addComponentQueue = addComponentQueue;
            serialize.removeComponentQueue = removeComponentQueue;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                serialize.Execute(i);
        }
    }

    private EntityQuery __group;

    private EntityTypeHandle __entityType;

    private ComponentTypeHandle<EntityDataSerializable> __entityDataSerializableType;

    private ComponentTypeHandle<GameNodeStatus> __statusType;

    private ComponentTypeHandle<GameDataDeadlineTrigger> __deadlineTriggerType;

    private EntityCommandPool<EntityCommandStructChange> __addComponentPool;
    private EntityCommandPool<EntityCommandStructChange> __removeComponentPool;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __group = builder
                    .WithAll<GameNodeStatus>()
                    .WithNone<GameNonSerialized>()
                    .WithOptions(EntityQueryOptions.IncludeDisabledEntities)
                    .Build(ref state);
        __group.SetChangedVersionFilter(ComponentType.ReadOnly<GameNodeStatus>());

        __entityType = state.GetEntityTypeHandle();
        __entityDataSerializableType = state.GetComponentTypeHandle<EntityDataSerializable>(true);
        __statusType = state.GetComponentTypeHandle<GameNodeStatus>(true);
        __deadlineTriggerType = state.GetComponentTypeHandle<GameDataDeadlineTrigger>(true);

        var manager = state.WorldUnmanaged.GetExistingSystemUnmanaged<GameDataStructChangeSystem>().manager;
        __addComponentPool = manager.addComponentPool;
        __removeComponentPool = manager.removeComponentPool;
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {

    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var addComponentQueue = __addComponentPool.Create();
        var removeComponentQueue = __removeComponentPool.Create();

        SerializeEx serialize;
        serialize.entityType = __entityType.UpdateAsRef(ref state);
        serialize.statusType = __statusType.UpdateAsRef(ref state);
        serialize.entityDataSerializableType = __entityDataSerializableType.UpdateAsRef(ref state);
        serialize.deadlineTriggerType = __deadlineTriggerType.UpdateAsRef(ref state);
        serialize.addComponentQueue = addComponentQueue.parallelWriter;
        serialize.removeComponentQueue = removeComponentQueue.parallelWriter;

        var jobHandle = serialize.ScheduleParallelByRef(__group, state.Dependency);

        addComponentQueue.AddJobHandleForProducer<SerializeEx>(jobHandle);
        removeComponentQueue.AddJobHandleForProducer<SerializeEx>(jobHandle);

        state.Dependency = jobHandle;
    }
}

[BurstCompile,
    EntityDataSerializationSystem(typeof(GameStatus)),
    CreateAfter(typeof(EntityDataSerializationInitializationSystem)),
    UpdateInGroup(typeof(EntityDataSerializationSystemGroup)), AutoCreateIn("Server")]
public partial struct GameDataStatusSerializationSystem : ISystem
{
    private EntityDataSerializationSystemCoreEx __core;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __core = EntityDataSerializationSystemCoreEx.Create<GameStatus>(ref state);
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
    EntityDataDeserializationSystem(typeof(GameStatus), (int)GameDataConstans.Version),
    CreateAfter(typeof(EntityDataDeserializationComponentSystem)),
    UpdateInGroup(typeof(EntityDataDeserializationSystemGroup)), AutoCreateIn("Server")]
public partial struct GameDataStatusDeserializationSystem : ISystem
{
    private EntityDataDeserializationSystemCoreEx __core;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __core = EntityDataDeserializationSystemCoreEx.Create<GameStatus>(ref state);
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