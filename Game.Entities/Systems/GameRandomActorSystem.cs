using Unity.Jobs;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Transforms;
using ZG;

[BurstCompile, AutoCreateIn("Server"), 
    UpdateInGroup(typeof(GameEntityActorSystemGroup), OrderFirst = true), 
    UpdateBefore(typeof(GameEntityActorInitSystem))
    /*, UpdateBefore(typeof(StateMachineSchedulerGroup))*/]
public partial struct GameRandomActorSystem : ISystem
{
    private struct Act
    {
        private struct ActionHandler : IRandomItemHandler
        {
            public int index;
            public int version;

            public GameTime time;

            public Random random;

            public quaternion rotation;

            [ReadOnly]
            public DynamicBuffer<GameRandomActorAction> actions;

            [NativeDisableParallelForRestriction]
            public NativeArray<GameEntityActionCommand> commands;

            public RandomResult Set(int startIndex, int count)
            {
                GameEntityActionCommand command;
                command.version = version;
                command.index = actions[startIndex + random.NextInt(count)].index;
                command.time = time;
                command.entity = Entity.Null;
                command.forward = math.forward(rotation);
                command.distance = float3.zero;
                //command.offset = float3.zero;

                commands[index] = command;

                return RandomResult.Success;
            }
        }

        public GameTime time;

        public Random random;

        [ReadOnly]
        public NativeArray<Rotation> rotations;

        [ReadOnly]
        public NativeArray<GameEntityCommandVersion> versions;
        
        [ReadOnly]
        public BufferAccessor<GameRandomActorSlice> slices;

        [ReadOnly]
        public BufferAccessor<GameRandomActorGroup> groups;
        
        [ReadOnly]
        public BufferAccessor<GameRandomActorAction> actions;
        
        public BufferAccessor<GameRandomActorNode> nodes;

        public NativeArray<GameEntityActionCommand> commands;

        public bool Execute(int index)
        {
            var nodes = this.nodes[index];
            int length = nodes.Length;
            if (length < 1)
                return false;

            ActionHandler actionHandler;
            actionHandler.index = index;
            actionHandler.version = versions[index].value;
            actionHandler.time = time;
            actionHandler.random = random;
            actionHandler.rotation = rotations[index].Value;
            actionHandler.actions = actions[index];
            actionHandler.commands = commands;

            bool result = false;
            var groups = this.groups[index];
            var slices = this.slices[index];
            GameRandomActorSlice slice;
            for(int i = 0; i < length; ++i)
            {
                slice = slices[nodes[i].sliceIndex];

                if (RandomResult.Success == random.Next(ref actionHandler, groups.Reinterpret<RandomGroup>().AsNativeArray().Slice(slice.groupStartIndex, slice.groupCount)))
                {
                    result = true;

                    break;
                }
            }

            nodes.Clear();

            return result;
        }
    }

    [BurstCompile]
    private struct ActEx : IJobChunk
    {
        public GameTime time;
        
        [ReadOnly]
        public ComponentTypeHandle<Rotation> rotationType;

        [ReadOnly]
        public ComponentTypeHandle<GameEntityCommandVersion> versionType;

        [ReadOnly]
        public BufferTypeHandle<GameRandomActorSlice> sliceType;

        [ReadOnly]
        public BufferTypeHandle<GameRandomActorGroup> groupType;
        
        [ReadOnly]
        public BufferTypeHandle<GameRandomActorAction> actionType;

        public BufferTypeHandle<GameRandomActorNode> nodeType;

        public ComponentTypeHandle<GameEntityActionCommand> commandType;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Act act;
            act.time = time;
            long hash = math.aslong(time);
            act.random = new Random((uint)((int)(hash >> 32) ^ ((int)hash) ^ unfilteredChunkIndex));
            act.rotations = chunk.GetNativeArray(ref rotationType);
            act.versions = chunk.GetNativeArray(ref versionType);
            act.slices = chunk.GetBufferAccessor(ref sliceType);
            act.groups = chunk.GetBufferAccessor(ref groupType);
            act.actions = chunk.GetBufferAccessor(ref actionType);
            act.nodes = chunk.GetBufferAccessor(ref nodeType);
            act.commands = chunk.GetNativeArray(ref commandType);

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
            {
                if (act.Execute(i))
                {
                    chunk.SetComponentEnabled(ref nodeType, i, false);
                    chunk.SetComponentEnabled(ref commandType, i, true);
                }
            }
        }
    }

    private EntityQuery __group;
    private GameSyncTime __time;

    private ComponentTypeHandle<Rotation> __rotationType;

    private ComponentTypeHandle<GameEntityCommandVersion> __versionType;

    private BufferTypeHandle<GameRandomActorSlice> __sliceType;

    private BufferTypeHandle<GameRandomActorGroup> __groupType;

    private BufferTypeHandle<GameRandomActorAction> __actionType;

    private BufferTypeHandle<GameRandomActorNode> __nodeType;

    private ComponentTypeHandle<GameEntityActionCommand> __commandType;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __group = builder
                .WithAll<Rotation, GameEntityCommandVersion, GameRandomActorSlice, GameRandomActorGroup, GameRandomActorAction>()
                .WithAllRW<GameRandomActorNode>()
                .Build(ref state);

        __group.SetChangedVersionFilter(ComponentType.ReadOnly<GameRandomActorNode>());

        __time = new GameSyncTime(ref state);

        __rotationType = state.GetComponentTypeHandle<Rotation>(true);
        __versionType = state.GetComponentTypeHandle<GameEntityCommandVersion>(true);
        __sliceType = state.GetBufferTypeHandle<GameRandomActorSlice>(true);
        __groupType = state.GetBufferTypeHandle<GameRandomActorGroup>(true);
        __actionType = state.GetBufferTypeHandle<GameRandomActorAction>(true);
        __nodeType = state.GetBufferTypeHandle<GameRandomActorNode>();
        __commandType = state.GetComponentTypeHandle<GameEntityActionCommand>();
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        ActEx act;
        act.time = __time.time;// __time.nextTime;
        act.rotationType = __rotationType.UpdateAsRef(ref state);
        act.versionType = __versionType.UpdateAsRef(ref state);
        act.sliceType = __sliceType.UpdateAsRef(ref state);
        act.groupType = __groupType.UpdateAsRef(ref state);
        act.actionType = __actionType.UpdateAsRef(ref state);
        act.nodeType = __nodeType.UpdateAsRef(ref state);
        act.commandType = __commandType.UpdateAsRef(ref state);

        state.Dependency = act.ScheduleParallelByRef(__group, state.Dependency);
    }
}
