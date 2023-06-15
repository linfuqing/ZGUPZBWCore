using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Jobs;
using Unity.Entities;
using Unity.Collections;
using ZG;

#region GameStatus
[assembly: RegisterGenericJobType(typeof(EntityDataComponentSerialize<ComponentDataSerializationSystem<GameStatus>.Serializer, ComponentDataSerializationSystem<GameStatus>.SerializerFactory>))]
[assembly: RegisterGenericJobType(typeof(EntityDataComponentDeserialize<ComponentDataDeserializationSystem<GameStatus>.Deserializer, ComponentDataDeserializationSystem<GameStatus>.DeserializerFactory>))]
[assembly: EntityDataSerialize(typeof(GameStatus))]
[assembly: EntityDataDeserialize(typeof(GameStatus), (int)GameDataConstans.Version)]
#endregion

public struct GameStatus : IComponentData
{
    public int value;
}

[AutoCreateIn("Server"), UpdateInGroup(typeof(GameDataSystemGroup))]
public partial class GameDataStatusSystem : SystemBase
{
    private struct Serialize
    {
        [ReadOnly]
        public NativeArray<Entity> entityArray;

        [ReadOnly]
        public NativeArray<GameNodeStatus> states;

        public EntityCommandQueue<Entity>.ParallelWriter entityManager;

        public void Execute(int index)
        {
            if ((states[index].value & (int)GameEntityStatus.Mask) != (int)GameEntityStatus.KnockedOut)
                return;

            entityManager.Enqueue(entityArray[index]);
        }
    }

    [BurstCompile]
    private struct SerializeEx : IJobChunk, IEntityCommandProducerJob
    {
        [ReadOnly]
        public EntityTypeHandle entityType;

        [ReadOnly]
        public ComponentTypeHandle<GameNodeStatus> statusType;

        public EntityCommandQueue<Entity>.ParallelWriter entityManager;

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

    private EntityCommandPool<Entity> __entityManager;

    protected override void OnCreate()
    {
        base.OnCreate();

        __group = GetEntityQuery(
            new EntityQueryDesc()
            {
                All = new ComponentType[]
                {
                    ComponentType.ReadOnly<GameNodeStatus>()
                },
                None = new ComponentType[]
                {
                    typeof(EntityDataSerializable), 
                    typeof(GameNonSerialized)
                }, 
                Options = EntityQueryOptions.IncludeDisabledEntities
            });
        __group.SetChangedVersionFilter(typeof(GameNodeStatus));

        __entityManager = World.GetOrCreateSystemManaged<EndFrameEntityCommandSystem>().CreateAddComponentCommander<EntityDataSerializable>();
    }

    protected override void OnUpdate()
    {
        var entityManager = __entityManager.Create();

        SerializeEx serialize;
        serialize.entityType = GetEntityTypeHandle();
        serialize.statusType = GetComponentTypeHandle<GameNodeStatus>(true);
        serialize.entityManager = entityManager.parallelWriter;

        JobHandle jobHandle = serialize.ScheduleParallel(__group, Dependency);

        entityManager.AddJobHandleForProducer<SerializeEx>(jobHandle);

        Dependency = jobHandle;
    }
}
