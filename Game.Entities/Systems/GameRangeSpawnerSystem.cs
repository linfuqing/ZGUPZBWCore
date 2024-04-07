using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using ZG;

public struct GameRnageSpawnerNode : IBufferElementData
{
    public int sliceIndex;
}

public struct GameRangeSpawnerStatus : IComponentData
{
    public enum Value
    {
        Start, 
        End
    }

    public Value value;

    public int count;
    //public Entity entity;
}

[BurstCompile, CreateAfter(typeof(GameRandomSpawnerSystem))]
public partial struct GameRangeSpawnerSystem : ISystem
{
    private struct Spawn
    {
        [ReadOnly]
        public GameRandomSpawner.Reader spawner;

        [ReadOnly]
        public ComponentLookup<PhysicsShapeParent> physicsShapeParents;
        [ReadOnly]
        public ComponentLookup<GameNodeStatus> nodeStates;
        [ReadOnly]
        public ComponentLookup<GameEntityCamp> campMap;
        [ReadOnly]
        public NativeArray<GameEntityCamp> camps;
        [ReadOnly] 
        public NativeArray<Entity> entityArray;
        [ReadOnly]
        public BufferAccessor<PhysicsTriggerEvent> physicsTriggerEvents;
        [ReadOnly]
        public BufferAccessor<GameFollower> followers;

        public BufferAccessor<GameRandomSpawnerNode> randomNodes;

        public BufferAccessor<GameRnageSpawnerNode> nodes;
        
        public NativeArray<GameRangeSpawnerStatus> states;

        public NativeQueue<Entity>.ParallelWriter entities;

        public void Execute(int index)
        {
            var physicsTriggerEvents = this.physicsTriggerEvents[index];
            Entity entity;
            bool isContains;
            isContains = false;
            foreach (var physicsTriggerEvent in physicsTriggerEvents)
            {
                entity = physicsShapeParents.HasComponent(physicsTriggerEvent.entity)
                    ? physicsShapeParents[physicsTriggerEvent.entity].entity
                    : physicsTriggerEvent.entity;
                if (nodeStates.HasComponent(entity) && ((GameEntityStatus)nodeStates[entity].value & GameEntityStatus.Mask) != GameEntityStatus.Dead)
                {
                    isContains = true;

                    break;
                }
            }

            var state = states[index];
            if (isContains)
            {
                if (state.value == GameRangeSpawnerStatus.Value.End)
                {
                    state.value = GameRangeSpawnerStatus.Value.Start;
                    state.count = 0;
                }
                else
                {
                    if (followers[index].Length > 0 || !spawner.IsEmpty(entityArray[index]))
                        return;
                }
                
                var nodes = this.nodes[index];
                if (state.count < nodes.Length)
                {
                    GameRandomSpawnerNode randomNode;
                    randomNode.sliceIndex = nodes[state.count++].sliceIndex;
                    randomNodes[index].Add(randomNode);
                }
                else
                    state.value = GameRangeSpawnerStatus.Value.End;
                    
                states[index] = state;
            }
            else if (state.value != GameRangeSpawnerStatus.Value.End)
            {
                state.value = GameRangeSpawnerStatus.Value.End;
                states[index] = state;
                
                entities.Enqueue(entityArray[index]);
            }
        }
    }

    [BurstCompile]
    private struct SpawnEx : IJobChunk
    {
        [ReadOnly]
        public GameRandomSpawner.Reader spawner;
        [ReadOnly]
        public ComponentLookup<PhysicsShapeParent> physicsShapeParents;
        [ReadOnly]
        public ComponentLookup<GameNodeStatus> nodeStates;
        [ReadOnly]
        public ComponentLookup<GameEntityCamp> camps;
        [ReadOnly]
        public ComponentTypeHandle<GameEntityCamp> campType;
        [ReadOnly] 
        public EntityTypeHandle entityType;
        [ReadOnly]
        public BufferTypeHandle<PhysicsTriggerEvent> physicsTriggerEventType;
        [ReadOnly]
        public BufferTypeHandle<GameFollower> followerType;

        public BufferTypeHandle<GameRandomSpawnerNode> randomNodeType;

        public BufferTypeHandle<GameRnageSpawnerNode> nodeType;
        
        public ComponentTypeHandle<GameRangeSpawnerStatus> statusType;

        public NativeQueue<Entity>.ParallelWriter entities;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask,
            in v128 chunkEnabledMask)
        {
            Spawn spawn;
            spawn.spawner = spawner;
            spawn.physicsShapeParents = physicsShapeParents;
            spawn.nodeStates = nodeStates;
            spawn.campMap = camps;
            spawn.camps = chunk.GetNativeArray(ref campType);
            spawn.entityArray = chunk.GetNativeArray(entityType);
            spawn.physicsTriggerEvents = chunk.GetBufferAccessor(ref physicsTriggerEventType);
            spawn.followers = chunk.GetBufferAccessor(ref followerType);
            spawn.randomNodes = chunk.GetBufferAccessor(ref randomNodeType);
            spawn.nodes = chunk.GetBufferAccessor(ref nodeType);
            spawn.states = chunk.GetNativeArray(ref statusType);
            spawn.entities = entities;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                spawn.Execute(i);
        }
    }

    [BurstCompile]
    private struct Stop : IJob
    {
        [ReadOnly] 
        public BufferLookup<GameFollower> followers;

        public ComponentLookup<GameNodeStatus> nodeStates;
        
        public NativeQueue<Entity> entities;

        public GameRandomSpawner.Writer spawner;
        
        public void Execute()
        {
            GameNodeStatus nodeStatus;
            nodeStatus.value = (int)GameItemStatus.Lose;
            
            DynamicBuffer<GameFollower> followers;
            while (entities.TryDequeue(out Entity entity))
            {
                spawner.Cancel(entity);

                if (this.followers.HasBuffer(entity))
                {
                    followers = this.followers[entity];

                    foreach (var follower in followers)
                    {
                        if (nodeStates.HasComponent(follower.entity))
                            nodeStates[follower.entity] = nodeStatus;
                    }
                }
            }
        }
    }

    private EntityQuery __group;

    private BufferLookup<GameFollower> __followers;
    private ComponentLookup<PhysicsShapeParent> __physicsShapeParents;
    private ComponentLookup<GameEntityCamp> __camps;
    private ComponentTypeHandle<GameEntityCamp> __campType;

    private EntityTypeHandle __entityType;
    private BufferTypeHandle<PhysicsTriggerEvent> __physicsTriggerEventType;
    private BufferTypeHandle<GameFollower> __followerType;

    private BufferTypeHandle<GameRandomSpawnerNode> __randomNodeType;

    private BufferTypeHandle<GameRnageSpawnerNode> __nodeType;
        
    private ComponentTypeHandle<GameRangeSpawnerStatus> __statusType;

    private ComponentLookup<GameNodeStatus> __nodeStates;
    
    private GameRandomSpawner __spawner;
    
    private NativeQueue<Entity> __entities;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __group = builder
                .WithAll<PhysicsTriggerEvent, GameFollower, GameEntityCamp>()
                .WithAllRW<GameRandomSpawnerNode>()
                .WithAllRW<GameRnageSpawnerNode>()
                .WithAllRW<GameRangeSpawnerStatus>()
                .Build(ref state);
                
        __followers = state.GetBufferLookup<GameFollower>(true);
        __physicsShapeParents = state.GetComponentLookup<PhysicsShapeParent>(true);
        __camps = state.GetComponentLookup<GameEntityCamp>(true);
        __campType = state.GetComponentTypeHandle<GameEntityCamp>(true);
        __entityType = state.GetEntityTypeHandle();
        __physicsTriggerEventType = state.GetBufferTypeHandle<PhysicsTriggerEvent>(true);
        __followerType = state.GetBufferTypeHandle<GameFollower>(true);

        __randomNodeType = state.GetBufferTypeHandle<GameRandomSpawnerNode>();

        __nodeType = state.GetBufferTypeHandle<GameRnageSpawnerNode>();

        __statusType = state.GetComponentTypeHandle<GameRangeSpawnerStatus>();

        __nodeStates = state.GetComponentLookup<GameNodeStatus>();

        __spawner = state.WorldUnmanaged.GetExistingSystemUnmanaged<GameRandomSpawnerSystem>().spawner;

        __entities = new NativeQueue<Entity>(Allocator.Persistent);
    }

    public void OnDestroy(ref SystemState state)
    {
        __entities.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var nodeStates = __nodeStates.UpdateAsRef(ref state);
        
        SpawnEx spawn;
        spawn.spawner = __spawner.reader;
        spawn.physicsShapeParents = __physicsShapeParents.UpdateAsRef(ref state);
        spawn.nodeStates = nodeStates;
        spawn.camps = __camps.UpdateAsRef(ref state);
        spawn.campType = __campType.UpdateAsRef(ref state);
        spawn.entityType = __entityType.UpdateAsRef(ref state);
        spawn.physicsTriggerEventType = __physicsTriggerEventType.UpdateAsRef(ref state);
        spawn.followerType = __followerType.UpdateAsRef(ref state);
        spawn.randomNodeType = __randomNodeType.UpdateAsRef(ref state);
        spawn.nodeType = __nodeType.UpdateAsRef(ref state);
        spawn.statusType = __statusType.UpdateAsRef(ref state);
        spawn.entities = __entities.AsParallelWriter();

        ref var spawnerJobManager = ref __spawner.lookupJobManager;

        var jobHandle = spawn.ScheduleParallelByRef(__group,
            JobHandle.CombineDependencies(spawnerJobManager.readOnlyJobHandle, state.Dependency));

        Stop stop;
        stop.followers = __followers.UpdateAsRef(ref state);
        stop.nodeStates = nodeStates;
        stop.entities = __entities;
        stop.spawner = __spawner.writer;

        jobHandle = stop.ScheduleByRef(JobHandle.CombineDependencies(jobHandle, spawnerJobManager.readWriteJobHandle));

        spawnerJobManager.readWriteJobHandle = jobHandle;

        state.Dependency = jobHandle;
    }
}
