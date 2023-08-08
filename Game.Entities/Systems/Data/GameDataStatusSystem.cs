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

public struct GameStatus : IComponentData
{
    public int value;
}

[AutoCreateIn("Server"), 
    BurstCompile, 
    CreateAfter(typeof(EndFrameStructChangeSystem)), 
    UpdateInGroup(typeof(GameDataSystemGroup))]
public partial struct GameDataStatusSystem : ISystem
{
    private struct Serialize
    {
        [ReadOnly]
        public NativeArray<Entity> entityArray;

        [ReadOnly]
        public NativeArray<GameNodeStatus> states;

        public EntityCommandQueue<EntityCommandStructChange>.ParallelWriter entityManager;

        public void Execute(int index)
        {
            if ((states[index].value & (int)GameEntityStatus.Mask) != (int)GameEntityStatus.KnockedOut)
                return;

            entityManager.Enqueue(EntityCommandStructChange.Create<EntityDataSerializable>(entityArray[index]));
        }
    }

    [BurstCompile]
    private struct SerializeEx : IJobChunk, IEntityCommandProducerJob
    {
        [ReadOnly]
        public EntityTypeHandle entityType;

        [ReadOnly]
        public ComponentTypeHandle<GameNodeStatus> statusType;

        public EntityCommandQueue<EntityCommandStructChange>.ParallelWriter entityManager;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Serialize serialize;
            serialize.entityArray = chunk.GetNativeArray(entityType);
            serialize.states = chunk.GetNativeArray(ref statusType);
            serialize.entityManager = entityManager;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                serialize.Execute(i);
        }
    }

    private EntityQuery __group;

    private EntityTypeHandle __entityType;

    private ComponentTypeHandle<GameNodeStatus> __statusType;

    private EntityCommandPool<EntityCommandStructChange> __entityManager;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __group = builder
                    .WithAll<GameNodeStatus>()
                    .WithNone<EntityDataSerializable, GameNonSerialized>()
                    .WithOptions(EntityQueryOptions.IncludeDisabledEntities)
                    .Build(ref state);
        __group.SetChangedVersionFilter(ComponentType.ReadOnly<GameNodeStatus>());

        __entityType = state.GetEntityTypeHandle();
        __statusType = state.GetComponentTypeHandle<GameNodeStatus>(true);

        __entityManager = state.WorldUnmanaged.GetExistingSystemUnmanaged<EndFrameStructChangeSystem>().manager.addComponentPool;
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {

    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var entityManager = __entityManager.Create();

        SerializeEx serialize;
        serialize.entityType = __entityType.UpdateAsRef(ref state);
        serialize.statusType = __statusType.UpdateAsRef(ref state);
        serialize.entityManager = entityManager.parallelWriter;

        var jobHandle = serialize.ScheduleParallelByRef(__group, state.Dependency);

        entityManager.AddJobHandleForProducer<SerializeEx>(jobHandle);

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