using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using ZG;

public struct GameRangeSpawnerNode : IBufferElementData
{
    public int sliceIndex;
}

public struct GameRangeSpawnerEntity : IBufferElementData
{
    public Entity value;
}

public struct GameRangeSpawnerStatus : IComponentData
{
    public int count;
}

[BurstCompile, CreateAfter(typeof(GameRandomSpawnerSystem))]
public partial struct GameRangeSpawnerSystem : ISystem
{
    private struct Spawn
    {
        [ReadOnly]
        public GameRandomSpawner.Reader spawner;
        [ReadOnly]
        public BufferLookup<PhysicsTriggerEvent> physicsTriggerEvents;
        [ReadOnly]
        public ComponentLookup<PhysicsShapeParent> physicsShapeParents;
        [ReadOnly] 
        public ComponentLookup<GameAreaNode> areaNodes;
        [ReadOnly]
        public ComponentLookup<GameNodeStatus> nodeStates;
        [ReadOnly]
        public ComponentLookup<GameEntityCamp> campMap;
        [ReadOnly]
        public NativeArray<GameEntityCamp> camps;
        [ReadOnly] 
        public NativeArray<Entity> entityArray;
        [ReadOnly]
        public BufferAccessor<PhysicsShapeChildEntity> physicsShapeChildEntities;
        [ReadOnly]
        public BufferAccessor<GameFollower> followers;
        [ReadOnly]
        public BufferAccessor<GameRangeSpawnerNode> nodes;

        public BufferAccessor<GameRandomSpawnerNode> randomNodes;

        public BufferAccessor<GameRangeSpawnerEntity> rangeEntities;

        public NativeArray<GameRangeSpawnerStatus> states;

        public NativeQueue<Entity>.ParallelWriter results;

        public bool Execute(int index)
        {
            var physicsShapeChildEntities = this.physicsShapeChildEntities[index];
            DynamicBuffer<PhysicsTriggerEvent> physicsTriggerEvents;
            Entity entity;
            bool isContains = false;
            foreach (var physicsShapeChildEntity in physicsShapeChildEntities)
            {
                if(!this.physicsTriggerEvents.HasBuffer(physicsShapeChildEntity.value))
                    continue;

                physicsTriggerEvents = this.physicsTriggerEvents[physicsShapeChildEntity.value];
                foreach (var physicsTriggerEvent in physicsTriggerEvents)
                {
                    entity = physicsShapeParents.HasComponent(physicsTriggerEvent.entity)
                        ? physicsShapeParents[physicsTriggerEvent.entity].entity
                        : physicsTriggerEvent.entity;
                    if (__IsVail(entity))
                    {
                        isContains = true;

                        break;
                    }
                }
                        
                if(isContains)
                    break;
            }
            
            bool result = false;
            if (isContains)
            {
                if ((followers.Length <= index || followers[index].Length < 1) &&
                    spawner.IsEmpty(entityArray[index]))
                {
                    var nodes = this.nodes[index];
                    int numNodes = nodes.Length;
                    if (numNodes > 0)
                    {
                        var entities = rangeEntities[index].Reinterpret<Entity>();
                        int numEntities = entities.Length;
                        for (int i = 0; i < numEntities; ++i)
                        {
                            entity = entities[i];
                            if (!__IsVail(entity))
                            {
                                entities.RemoveAtSwapBack(i--);

                                --numEntities;
                            }
                        }
                        
                        var state = states[index];
                        if (state.count >= numNodes)
                        {
                            foreach (var physicsShapeChildEntity in physicsShapeChildEntities)
                            {
                                if(!this.physicsTriggerEvents.HasBuffer(physicsShapeChildEntity.value))
                                    continue;

                                physicsTriggerEvents = this.physicsTriggerEvents[physicsShapeChildEntity.value];
                                foreach (var physicsTriggerEvent in physicsTriggerEvents)
                                {
                                    entity = physicsShapeParents.HasComponent(physicsTriggerEvent.entity)
                                        ? physicsShapeParents[physicsTriggerEvent.entity].entity
                                        : physicsTriggerEvent.entity;
                                    if (entities.AsNativeArray().Contains(entity))
                                        return false;
                                }
                            }
                            
                            state.count = 0;
                        }

                        foreach (var physicsShapeChildEntity in physicsShapeChildEntities)
                        {
                            if(!this.physicsTriggerEvents.HasBuffer(physicsShapeChildEntity.value))
                                continue;

                            physicsTriggerEvents = this.physicsTriggerEvents[physicsShapeChildEntity.value];
                            foreach (var physicsTriggerEvent in physicsTriggerEvents)
                            {
                                entity = physicsShapeParents.HasComponent(physicsTriggerEvent.entity)
                                    ? physicsShapeParents[physicsTriggerEvent.entity].entity
                                    : physicsTriggerEvent.entity;
                                if (__IsVail(entity) && 
                                    !entities.AsNativeArray().Contains(entity))
                                    entities.Add(entity);
                            }
                        }

                        if (randomNodes.Length > index)
                        {
                            GameRandomSpawnerNode randomNode;
                            randomNode.sliceIndex = nodes[state.count++].sliceIndex;
                            randomNodes[index].Add(randomNode);

                            result = true;
                        }
                        else
                            ++state.count;

                        states[index] = state;
                    }
                }
            }
            else
            {
                if(index < randomNodes.Length)
                    randomNodes[index].Clear();
                
                rangeEntities[index].Clear();
                
                states[index] = default;
                
                results.Enqueue(entityArray[index]);
            }

            return result;
        }

        private bool __IsVail(in Entity entity)
        {
            return nodeStates.HasComponent(entity) &&
                   ((GameEntityStatus)nodeStates[entity].value & GameEntityStatus.Mask) !=
                   GameEntityStatus.Dead && 
                   areaNodes.HasComponent(entity) && 
                   areaNodes[entity].areaIndex != -1;
        }
    }

    [BurstCompile]
    private struct SpawnEx : IJobChunk
    {
        [ReadOnly]
        public GameRandomSpawner.Reader spawner;
        [ReadOnly]
        public BufferLookup<PhysicsTriggerEvent> physicsTriggerEvents;
        [ReadOnly]
        public ComponentLookup<PhysicsShapeParent> physicsShapeParents;
        [ReadOnly] 
        public ComponentLookup<GameAreaNode> areaNodes;
        [ReadOnly]
        public ComponentLookup<GameNodeStatus> nodeStates;
        [ReadOnly]
        public ComponentLookup<GameEntityCamp> camps;
        [ReadOnly]
        public ComponentTypeHandle<GameEntityCamp> campType;
        [ReadOnly] 
        public EntityTypeHandle entityType;
        [ReadOnly]
        public BufferTypeHandle<PhysicsShapeChildEntity> physicsShapeChildEntityType;
        [ReadOnly]
        public BufferTypeHandle<GameFollower> followerType;
        [ReadOnly]
        public BufferTypeHandle<GameRangeSpawnerNode> nodeType;

        public BufferTypeHandle<GameRandomSpawnerNode> randomNodeType;

        public BufferTypeHandle<GameRangeSpawnerEntity> rangeEntityType;

        public ComponentTypeHandle<GameRangeSpawnerStatus> statusType;

        public NativeQueue<Entity>.ParallelWriter results;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask,
            in v128 chunkEnabledMask)
        {
            Spawn spawn;
            spawn.spawner = spawner;
            spawn.physicsTriggerEvents = physicsTriggerEvents;
            spawn.physicsShapeParents = physicsShapeParents;
            spawn.areaNodes = areaNodes;
            spawn.nodeStates = nodeStates;
            spawn.campMap = camps;
            spawn.camps = chunk.GetNativeArray(ref campType);
            spawn.entityArray = chunk.GetNativeArray(entityType);
            spawn.physicsShapeChildEntities = chunk.GetBufferAccessor(ref physicsShapeChildEntityType);
            spawn.followers = chunk.GetBufferAccessor(ref followerType);
            spawn.nodes = chunk.GetBufferAccessor(ref nodeType);
            spawn.randomNodes = chunk.GetBufferAccessor(ref randomNodeType);
            spawn.rangeEntities = chunk.GetBufferAccessor(ref rangeEntityType);
            spawn.states = chunk.GetNativeArray(ref statusType);
            spawn.results = results;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
            {
                if(spawn.Execute(i))
                    chunk.SetComponentEnabled(ref randomNodeType, i, true);
            }
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
    private BufferLookup<PhysicsTriggerEvent> __physicsTriggerEvents;
    private ComponentLookup<PhysicsShapeParent> __physicsShapeParents;
    private ComponentLookup<GameAreaNode> __areaNodes;
    private ComponentLookup<GameEntityCamp> __camps;
    private ComponentTypeHandle<GameEntityCamp> __campType;

    private EntityTypeHandle __entityType;
    private BufferTypeHandle<PhysicsShapeChildEntity> __physicsShapeChildEntityType;
    private BufferTypeHandle<GameFollower> __followerType;

    private BufferTypeHandle<GameRangeSpawnerNode> __nodeType;

    private BufferTypeHandle<GameRandomSpawnerNode> __randomNodeType;

    private BufferTypeHandle<GameRangeSpawnerEntity> __rangeEntityType;

    private ComponentTypeHandle<GameRangeSpawnerStatus> __statusType;

    private ComponentLookup<GameNodeStatus> __nodeStates;
    
    private GameRandomSpawner __spawner;
    
    private NativeQueue<Entity> __entities;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __group = builder
                .WithAll<PhysicsShapeChildEntity, GameRangeSpawnerNode>()
                //.WithAllRW<GameRandomSpawnerNode>()
                .WithAllRW<GameRangeSpawnerEntity>()
                .WithAllRW<GameRangeSpawnerStatus>()
                .Build(ref state);
                
        __followers = state.GetBufferLookup<GameFollower>(true);
        __physicsTriggerEvents = state.GetBufferLookup<PhysicsTriggerEvent>(true);
        __physicsShapeParents = state.GetComponentLookup<PhysicsShapeParent>(true);
        __areaNodes = state.GetComponentLookup<GameAreaNode>(true);
        __camps = state.GetComponentLookup<GameEntityCamp>(true);
        __campType = state.GetComponentTypeHandle<GameEntityCamp>(true);
        __entityType = state.GetEntityTypeHandle();
        __physicsShapeChildEntityType = state.GetBufferTypeHandle<PhysicsShapeChildEntity>(true);
        __followerType = state.GetBufferTypeHandle<GameFollower>(true);

        __nodeType = state.GetBufferTypeHandle<GameRangeSpawnerNode>(true);

        __randomNodeType = state.GetBufferTypeHandle<GameRandomSpawnerNode>();

        __rangeEntityType = state.GetBufferTypeHandle<GameRangeSpawnerEntity>();
        
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
        spawn.physicsTriggerEvents = __physicsTriggerEvents.UpdateAsRef(ref state);
        spawn.physicsShapeParents = __physicsShapeParents.UpdateAsRef(ref state);
        spawn.areaNodes = __areaNodes.UpdateAsRef(ref state);
        spawn.nodeStates = nodeStates;
        spawn.camps = __camps.UpdateAsRef(ref state);
        spawn.campType = __campType.UpdateAsRef(ref state);
        spawn.entityType = __entityType.UpdateAsRef(ref state);
        spawn.physicsShapeChildEntityType = __physicsShapeChildEntityType.UpdateAsRef(ref state);
        spawn.followerType = __followerType.UpdateAsRef(ref state);
        spawn.nodeType = __nodeType.UpdateAsRef(ref state);
        spawn.randomNodeType = __randomNodeType.UpdateAsRef(ref state);
        spawn.rangeEntityType = __rangeEntityType.UpdateAsRef(ref state);
        spawn.statusType = __statusType.UpdateAsRef(ref state);
        spawn.results = __entities.AsParallelWriter();

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
