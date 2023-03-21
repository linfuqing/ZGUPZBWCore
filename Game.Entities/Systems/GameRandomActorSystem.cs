using Unity.Jobs;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Transforms;
using ZG;

[AutoCreateIn("Server"), UpdateInGroup(typeof(TimeSystemGroup)), UpdateBefore(typeof(StateMachineSchedulerGroup))/*, UpdateAfter(typeof(GameNodeEventSystem))*/]
public partial class GameRandomActorSystem : SystemBase
{
    private struct Act
    {
        private struct ActionHandler : IRandomItemHandler
        {
            public int version;

            public GameTime time;

            public Entity entity;

            public Random random;

            public quaternion rotation;

            [ReadOnly]
            public DynamicBuffer<GameRandomActorAction> actions;

            [NativeDisableParallelForRestriction]
            public ComponentLookup<GameEntityActionCommand> commands;

            public RandomItemType Set(int startIndex, int count)
            {
                GameEntityActionCommand command;
                command.version = version;
                command.index = actions[startIndex + random.NextInt(count)].index;
                command.time = time;
                command.entity = Entity.Null;
                command.forward = math.forward(rotation);
                command.distance = float3.zero;

                commands[entity] = command;

                return RandomItemType.Success;
            }
        }

        public GameTime time;

        public Random random;

        [ReadOnly]
        public NativeArray<Entity> entityArray;
        
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
        
        [ReadOnly]
        public BufferAccessor<GameRandomActorNode> nodes;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<GameEntityActionCommand> commands;

        public NativeQueue<Entity>.ParallelWriter nodesToClear;

        public void Execute(int index)
        {
            var nodes = this.nodes[index];
            int length = nodes.Length;
            if (length < 1)
                return;

            int version = versions[index].value;
            Entity entity = entityArray[index];
            if (version == commands[entity].version)
                return;

            ActionHandler actionHandler;
            actionHandler.version = version;
            actionHandler.time = time;
            actionHandler.entity = entity;
            actionHandler.random = random;
            actionHandler.rotation = rotations[index].Value;
            actionHandler.actions = actions[index];
            actionHandler.commands = commands;

            var groups = this.groups[index];
            var slices = this.slices[index];
            GameRandomActorSlice slice;
            for(int i = 0; i < length; ++i)
            {
                slice = slices[nodes[i].sliceIndex];

                random.Next(ref actionHandler, groups.Reinterpret<RandomGroup>().AsNativeArray().Slice(slice.groupStartIndex, slice.groupCount));
            }

            nodesToClear.Enqueue(entity);
        }
    }

    [BurstCompile]
    private struct ActEx : IJobChunk
    {
        public GameTime time;
        
        [ReadOnly]
        public EntityTypeHandle entityType;
        
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

        [ReadOnly]
        public BufferTypeHandle<GameRandomActorNode> nodeType;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<GameEntityActionCommand> commands;

        public NativeQueue<Entity>.ParallelWriter nodesToClear;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Act act;
            act.time = time;
            long hash = math.aslong(time);
            act.random = new Random((uint)((int)(hash >> 32) ^ ((int)hash) ^ unfilteredChunkIndex));
            act.entityArray = chunk.GetNativeArray(entityType);
            act.rotations = chunk.GetNativeArray(ref rotationType);
            act.versions = chunk.GetNativeArray(ref versionType);
            act.slices = chunk.GetBufferAccessor(ref sliceType);
            act.groups = chunk.GetBufferAccessor(ref groupType);
            act.actions = chunk.GetBufferAccessor(ref actionType);
            act.nodes = chunk.GetBufferAccessor(ref nodeType);
            act.commands = commands;
            act.nodesToClear = nodesToClear;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                act.Execute(i);
        }
    }

    [BurstCompile]
    private struct Clear : IJob
    {
        public NativeQueue<Entity> entities;
        public BufferLookup<GameRandomActorNode> nodes;

        public void Execute()
        {
            while (entities.TryDequeue(out Entity entity))
                nodes[entity].Clear();
        }
    }

    private EntityQuery __group;
    private GameSyncTime __time;
    private NativeQueue<Entity> __nodesToClear;
    
    protected override void OnCreate()
    {
        base.OnCreate();

        __group = GetEntityQuery(
            ComponentType.ReadOnly<Rotation>(),
            ComponentType.ReadOnly<GameEntityCommandVersion>(),
            ComponentType.ReadOnly<GameRandomActorSlice>(),
            ComponentType.ReadOnly<GameRandomActorGroup>(),
            ComponentType.ReadOnly<GameRandomActorAction>(),
            ComponentType.ReadOnly<GameRandomActorNode>());

        __time = new GameSyncTime(ref this.GetState());

        __nodesToClear = new NativeQueue<Entity>(Allocator.Persistent);
    }

    protected override void OnDestroy()
    {
        __nodesToClear.Dispose();

        base.OnDestroy();
    }

    protected override void OnUpdate()
    {
        ActEx act;
        act.time = __time.nextTime;
        act.entityType = GetEntityTypeHandle();
        act.rotationType = GetComponentTypeHandle<Rotation>(true);
        act.versionType = GetComponentTypeHandle<GameEntityCommandVersion>(true);
        act.sliceType = GetBufferTypeHandle<GameRandomActorSlice>(true);
        act.groupType = GetBufferTypeHandle<GameRandomActorGroup>(true);
        act.actionType = GetBufferTypeHandle<GameRandomActorAction>(true);
        act.nodeType = GetBufferTypeHandle<GameRandomActorNode>(true);
        act.commands = GetComponentLookup<GameEntityActionCommand>();
        act.nodesToClear = __nodesToClear.AsParallelWriter();

        var jobHandle = act.ScheduleParallel(__group, Dependency);

        Clear clear;
        clear.entities = __nodesToClear;
        clear.nodes = GetBufferLookup<GameRandomActorNode>();

        Dependency = clear.Schedule(jobHandle);
    }
}
