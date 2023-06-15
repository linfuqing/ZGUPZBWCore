using Unity.Jobs;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Entities;
using Unity.Collections;
using ZG;

[BurstCompile, AutoCreateIn("Server"), UpdateInGroup(typeof(GameStatusSystemGroup))/*, UpdateBefore(typeof(GameRandomActorSystem))*/]
public partial struct GameStatusActorSystem : ISystem
{
    private struct Act
    {
        [ReadOnly]
        public NativeArray<GameNodeStatus> states;

        [ReadOnly]
        public NativeArray<GameNodeOldStatus> oldStates;

        [ReadOnly]
        public BufferAccessor<GameStatusActorLevel> levels;

        public BufferAccessor<GameRandomActorNode> actors;

        public BufferAccessor<GameRandomSpawnerNode> spawners;
        
        public GameStatusActorFlag Execute(int index)
        {
            var status = states[index].value;
            if (status == oldStates[index].value)
                return 0;

            GameStatusActorFlag flag = 0;
            var spawners = index < this.spawners.Length ? this.spawners[index] : default;
            var actors = index < this.actors.Length ? this.actors[index] :default;
            var levels = this.levels[index];
            GameStatusActorLevel level;
            GameRandomActorNode actor;
            GameRandomSpawnerNode spawner;
            int length = levels.Length;
            for (int i = 0; i < length; ++i)
            {
                level = levels[i];
                if (level.status != status)
                    continue;

                if ((level.flag & GameStatusActorFlag.Action) == GameStatusActorFlag.Action)
                {
                    if (actors.IsCreated)
                    {
                        flag |= GameStatusActorFlag.Action;

                        actor.sliceIndex = level.sliceIndex;
                        actors.Add(actor);
                    }

                    continue;
                }

                if (spawners.IsCreated)
                {
                    flag |= GameStatusActorFlag.Normal;

                    spawner.sliceIndex = level.sliceIndex;
                    spawners.Add(spawner);
                }
            }

            return flag;
        }
    }

    [BurstCompile]
    private struct ActEx : IJobChunk
    {
        [ReadOnly]
        public ComponentTypeHandle<GameNodeStatus> statusType;

        [ReadOnly]
        public ComponentTypeHandle<GameNodeOldStatus> oldStatusType;

        [ReadOnly]
        public BufferTypeHandle<GameStatusActorLevel> levelType;
        
        public BufferTypeHandle<GameRandomActorNode> actorType;

        public BufferTypeHandle<GameRandomSpawnerNode> spawnerType;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Act act;
            act.states = chunk.GetNativeArray(ref statusType);
            act.oldStates = chunk.GetNativeArray(ref oldStatusType);
            act.levels = chunk.GetBufferAccessor(ref levelType);
            act.actors = chunk.GetBufferAccessor(ref actorType);
            act.spawners = chunk.GetBufferAccessor(ref spawnerType);

            GameStatusActorFlag flag;
            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
            {
                flag = act.Execute(i);

                if((flag & GameStatusActorFlag.Normal) == GameStatusActorFlag.Normal)
                    chunk.SetComponentEnabled(ref spawnerType, i, true);

                if ((flag & GameStatusActorFlag.Action) == GameStatusActorFlag.Action)
                    chunk.SetComponentEnabled(ref actorType, i, true);
            }
        }
    }

    private EntityQuery __group;

    private ComponentTypeHandle<GameNodeStatus> __statusType;

    private ComponentTypeHandle<GameNodeOldStatus> __oldStatusType;

    private BufferTypeHandle<GameStatusActorLevel> __levelType;

    private BufferTypeHandle<GameRandomActorNode> __actorType;

    private BufferTypeHandle<GameRandomSpawnerNode> __spawnerType;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __group = builder
                    .WithAll<GameNodeStatus, GameNodeOldStatus, GameStatusActorLevel>()
                    .Build(ref state);

        __group.AddChangedVersionFilter(ComponentType.ReadOnly<GameNodeStatus>());
        __group.AddChangedVersionFilter(ComponentType.ReadOnly<GameNodeOldStatus>());

        __statusType = state.GetComponentTypeHandle<GameNodeStatus>(true);
        __oldStatusType = state.GetComponentTypeHandle<GameNodeOldStatus>(true);
        __levelType = state.GetBufferTypeHandle<GameStatusActorLevel>(true);
        __actorType = state.GetBufferTypeHandle<GameRandomActorNode>();
        __spawnerType = state.GetBufferTypeHandle<GameRandomSpawnerNode>();
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {

    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        ActEx act;
        act.statusType = __statusType.UpdateAsRef(ref state);
        act.oldStatusType = __oldStatusType.UpdateAsRef(ref state);
        act.levelType = __levelType.UpdateAsRef(ref state);
        act.actorType = __actorType.UpdateAsRef(ref state);
        act.spawnerType = __spawnerType.UpdateAsRef(ref state);

        state.Dependency = act.ScheduleParallelByRef(__group, state.Dependency);
    }
}
