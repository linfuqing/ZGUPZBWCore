using Unity.Jobs;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Entities;
using Unity.Collections;
using ZG;

[BurstCompile, AutoCreateIn("Server"), UpdateInGroup(typeof(TimeSystemGroup)), UpdateBefore(typeof(GameRandomActorSystem))]
public partial struct GameStatusActorSystem : ISystem
{
    private struct Act
    {
        [ReadOnly]
        public NativeArray<Entity> entityArray;

        [ReadOnly]
        public NativeArray<GameNodeStatus> states;

        [ReadOnly]
        public NativeArray<GameNodeOldStatus> oldStates;

        [ReadOnly]
        public BufferAccessor<GameStatusActorLevel> levels;

        [NativeDisableParallelForRestriction]
        public BufferLookup<GameRandomActorNode> actors;

        [NativeDisableParallelForRestriction]
        public BufferLookup<GameRandomSpawnerNode> spawners;
        
        public void Execute(int index)
        {
            var status = states[index].value;
            if (status == oldStates[index].value)
                return;

            var levels = this.levels[index];
            GameStatusActorLevel level;
            Entity entity = entityArray[index];
            int length = levels.Length;
            for (int i = 0; i < length; ++i)
            {
                level = levels[i];
                if (level.status != status)
                    continue;

                if ((level.flag & GameStatusActorFlag.Action) == GameStatusActorFlag.Action)
                {
                    if (actors.HasComponent(entity))
                        actors[entity].Reinterpret<int>().Add(level.sliceIndex);

                    continue;
                }

                if (spawners.HasComponent(entity))
                    spawners[entity].Reinterpret<int>().Add(level.sliceIndex);
            }
        }
    }

    [BurstCompile]
    private struct ActEx : IJobChunk
    {
        [ReadOnly]
        public EntityTypeHandle entityType;

        [ReadOnly]
        public ComponentTypeHandle<GameNodeStatus> statusType;

        [ReadOnly]
        public ComponentTypeHandle<GameNodeOldStatus> oldStatusType;

        [ReadOnly]
        public BufferTypeHandle<GameStatusActorLevel> levelType;
        
        [NativeDisableParallelForRestriction]
        public BufferLookup<GameRandomActorNode> actors;

        [NativeDisableParallelForRestriction]
        public BufferLookup<GameRandomSpawnerNode> spawners;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Act act;
            act.entityArray = chunk.GetNativeArray(entityType);
            act.states = chunk.GetNativeArray(ref statusType);
            act.oldStates = chunk.GetNativeArray(ref oldStatusType);
            act.levels = chunk.GetBufferAccessor(ref levelType);
            act.actors = actors;
            act.spawners = spawners;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                act.Execute(i);
        }
    }

    private EntityQuery __group;

    public void OnCreate(ref SystemState state)
    {
        __group = state.GetEntityQuery(
            ComponentType.ReadOnly<GameNodeStatus>(),
            ComponentType.ReadOnly<GameNodeOldStatus>(), 
            ComponentType.ReadOnly<GameStatusActorLevel>());

        __group.SetChangedVersionFilter(
            new ComponentType[]
            {
                typeof(GameNodeStatus),
                typeof(GameNodeOldStatus)
            });
    }

    public void OnDestroy(ref SystemState state)
    {

    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        ActEx act;
        act.entityType = state.GetEntityTypeHandle();
        act.statusType = state.GetComponentTypeHandle<GameNodeStatus>(true);
        act.oldStatusType = state.GetComponentTypeHandle<GameNodeOldStatus>(true);
        act.levelType = state.GetBufferTypeHandle<GameStatusActorLevel>(true);
        act.actors = state.GetBufferLookup<GameRandomActorNode>();
        act.spawners = state.GetBufferLookup<GameRandomSpawnerNode>();

        state.Dependency = act.ScheduleParallel(__group, state.Dependency);
    }
}
