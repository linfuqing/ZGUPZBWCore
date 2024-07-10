using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using ZG;

public struct GameRangeSpawnerOrigin : IComponentData
{
    public float3 value;
}

public struct GameRangeSpawnerTarget : IComponentData, IEnableableComponent
{
    public float3 value;
}

public struct GameRangeSpawner : IBufferElementData, IEnableableComponent
{
    public Entity entity;
}

public struct GameRangeSpawnerNode : IBufferElementData
{
    public int sliceIndex;
    public float inTime;
    public float outTime;
}

public struct GameRangeSpawnerEntity : IBufferElementData, IEnableableComponent
{
    public Entity value;
}

public struct GameRangeSpawnerStatus : IComponentData
{
    public int count;
}

public struct GameRangeSpawnerCoolDownTime : IComponentData
{
    public double value;
}

[BurstCompile, CreateAfter(typeof(GameRandomSpawnerSystem)), CreateAfter(typeof(GameItemSystem))]
public partial struct GameRangeSpawnerSystem : ISystem
{
    private struct MoveItem
    {
        public GameItemHandle source;
        public GameItemHandle destination;
    }

    [BurstCompile]
    private struct Enable : IJobChunk
    {
        public BufferTypeHandle<GameRangeSpawnerEntity> rangeEntityType;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                chunk.SetComponentEnabled(ref rangeEntityType, i, true);
        }
    }
    
    private struct Spawn
    {
        public double time;
        
        [ReadOnly]
        public GameRandomSpawner.Reader spawner;
        [ReadOnly]
        public BufferLookup<PhysicsTriggerEvent> physicsTriggerEvents;
        [ReadOnly]
        public ComponentLookup<PhysicsShapeParent> physicsShapeParents;
        [ReadOnly] 
        public ComponentLookup<GameAreaNodePresentation> areaNodePresentations;
        [ReadOnly] 
        public ComponentLookup<GameAreaNode> areaNodes;
        [ReadOnly]
        public ComponentLookup<GameNodeStatus> nodeStates;
        [ReadOnly]
        public ComponentLookup<GameItemRoot> itemRoots;
        [ReadOnly]
        public ComponentLookup<GameOwner> owners;
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

        public NativeArray<GameRangeSpawnerCoolDownTime> coolDownTimes;

        public NativeQueue<EntityData<Entity>>.ParallelWriter inputs;

        public NativeQueue<EntityData<Entity>>.ParallelWriter outputs;

        public NativeQueue<Entity>.ParallelWriter results;

        public NativeQueue<MoveItem>.ParallelWriter moveItems;

        public bool Execute(int index, ref bool isRangeEntityEnabled)
        {
            var coolDownTime = coolDownTimes[index];
            if (coolDownTime.value > time)
                return false;
            
            var physicsShapeChildEntities = index < this.physicsShapeChildEntities.Length ? this.physicsShapeChildEntities[index] : default;
            DynamicBuffer<PhysicsTriggerEvent> physicsTriggerEvents;
            Entity entity;
            bool isContains = false;
            if (physicsShapeChildEntities.IsCreated)
            {
                foreach (var physicsShapeChildEntity in physicsShapeChildEntities)
                {
                    if (!this.physicsTriggerEvents.HasBuffer(physicsShapeChildEntity.value))
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

                    if (isContains)
                        break;
                }
            }

            var state = states[index];
            bool result = false;
            if (isContains)
            {
                if ((followers.Length <= index || followers[index].Length < 1) &&
                    spawner.IsEmpty(entityArray[index]))
                {
                    EntityData<Entity> temp;
                    temp.entity = entityArray[index];

                    var rangeEntities = this.rangeEntities[index].Reinterpret<Entity>();
                    int numRangeEntities = rangeEntities.Length;
                    for (int i = 0; i < numRangeEntities; ++i)
                    {
                        entity = rangeEntities[i];
                        if (!__IsVail(entity))
                        {
                            temp.value = entity;
                            outputs.Enqueue(temp);

                            rangeEntities.RemoveAtSwapBack(i--);

                            --numRangeEntities;
                        }
                    }

                    var rangeEntityArray = rangeEntities.AsNativeArray();
                    var nodes = this.nodes[index];
                    int numNodes = nodes.Length;
                    if (numNodes <= state.count)
                    {
                        foreach (var physicsShapeChildEntity in physicsShapeChildEntities)
                        {
                            if (!this.physicsTriggerEvents.HasBuffer(physicsShapeChildEntity.value))
                                continue;

                            physicsTriggerEvents = this.physicsTriggerEvents[physicsShapeChildEntity.value];
                            foreach (var physicsTriggerEvent in physicsTriggerEvents)
                            {
                                entity = physicsShapeParents.HasComponent(physicsTriggerEvent.entity)
                                    ? physicsShapeParents[physicsTriggerEvent.entity].entity
                                    : physicsTriggerEvent.entity;
                                if (rangeEntityArray.Contains(entity))
                                    return false;
                            }
                        }

                        state.count = 0;
                    }

                    foreach (var physicsShapeChildEntity in physicsShapeChildEntities)
                    {
                        if (!this.physicsTriggerEvents.HasBuffer(physicsShapeChildEntity.value))
                            continue;

                        physicsTriggerEvents = this.physicsTriggerEvents[physicsShapeChildEntity.value];
                        foreach (var physicsTriggerEvent in physicsTriggerEvents)
                        {
                            entity = physicsShapeParents.HasComponent(physicsTriggerEvent.entity)
                                ? physicsShapeParents[physicsTriggerEvent.entity].entity
                                : physicsTriggerEvent.entity;
                            if (__IsVail(entity) &&
                                !rangeEntityArray.Contains(entity))
                            {
                                ++numRangeEntities;
                                
                                rangeEntities.Add(entity);
                                rangeEntityArray = rangeEntities.AsNativeArray();

                                temp.value = entity;
                                inputs.Enqueue(temp);
                            }
                        }
                    }

                    isRangeEntityEnabled = true;

                    if (state.count < numNodes)
                    {
                        var node = nodes[state.count];

                        coolDownTime.value = nodes[state.count].inTime + time;
                        coolDownTimes[index] = coolDownTime;

                        if (randomNodes.Length > index)
                        {
                            GameRandomSpawnerNode randomNode;
                            randomNode.sliceIndex = node.sliceIndex;
                            randomNodes[index].Add(randomNode);

                            result = true;
                        }
                    }
                    
                    ++state.count;

                    states[index] = state;
                }
            }
            else
            {
                if(index < randomNodes.Length)
                    randomNodes[index].Clear();
                
                EntityData<Entity> temp;
                temp.entity = entityArray[index];
                var rangeEntities = this.rangeEntities[index];
                foreach (var rangeEntity in rangeEntities)
                {
                    temp.value = rangeEntity.value;
                    outputs.Enqueue(temp);
                }
                
                rangeEntities.Clear();

                isRangeEntityEnabled = false;
                
                if (state.count > 0)
                {
                    var nodes = this.nodes[index];
                    if (nodes.Length >= state.count)
                    {
                        coolDownTime.value = nodes[state.count - 1].outTime + time;
                        coolDownTimes[index] = coolDownTime;
                    }
                }
                
                state.count = 0;
                states[index] = state;
                
                results.Enqueue(temp.entity);

                if (physicsShapeChildEntities.IsCreated)
                {
                    MoveItem moveItem;
                    foreach (var physicsShapeChildEntity in physicsShapeChildEntities)
                    {
                        if (!this.physicsTriggerEvents.HasBuffer(physicsShapeChildEntity.value))
                            continue;

                        physicsTriggerEvents = this.physicsTriggerEvents[physicsShapeChildEntity.value];
                        foreach (var physicsTriggerEvent in physicsTriggerEvents)
                        {
                            entity = physicsShapeParents.HasComponent(physicsTriggerEvent.entity)
                                ? physicsShapeParents[physicsTriggerEvent.entity].entity
                                : physicsTriggerEvent.entity;
                            if (!itemRoots.HasComponent(entity))
                                continue;

                            moveItem.source = itemRoots[entity].handle;
                            moveItem.destination = __GetItemRoot(entity);

                            if (moveItem.source.Equals(moveItem.destination))
                                continue;

                            moveItems.Enqueue(moveItem);
                        }
                    }
                }
            }

            return result;
        }

        private GameItemHandle __GetItemRoot(in Entity entity)
        {
            Entity owner = owners.HasComponent(entity) ? owners[entity].entity : Entity.Null;
            if (owner == Entity.Null)
                return itemRoots.HasComponent(entity) ? itemRoots[entity].handle : GameItemHandle.Empty;

            return __GetItemRoot(owner);
        }

        private bool __IsVail(in Entity entity)
        {
            return nodeStates.HasComponent(entity) &&
                   ((GameEntityStatus)nodeStates[entity].value & GameEntityStatus.Mask) !=
                   GameEntityStatus.Dead && 
                   areaNodePresentations.HasComponent(entity) && 
                   areaNodes.HasComponent(entity) && 
                   areaNodes[entity].areaIndex != -1;
        }
    }

    [BurstCompile]
    private struct SpawnEx : IJobChunk
    {
        public double time;
        [ReadOnly]
        public GameRandomSpawner.Reader spawner;
        [ReadOnly]
        public BufferLookup<PhysicsTriggerEvent> physicsTriggerEvents;
        [ReadOnly]
        public ComponentLookup<PhysicsShapeParent> physicsShapeParents;
        [ReadOnly] 
        public ComponentLookup<GameAreaNodePresentation> areaNodePresentations;
        [ReadOnly] 
        public ComponentLookup<GameAreaNode> areaNodes;
        [ReadOnly]
        public ComponentLookup<GameNodeStatus> nodeStates;
        [ReadOnly]
        public ComponentLookup<GameItemRoot> itemRoots;
        [ReadOnly]
        public ComponentLookup<GameOwner> owners;
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

        public ComponentTypeHandle<GameRangeSpawnerCoolDownTime> coolDownTimeType;

        public NativeQueue<EntityData<Entity>>.ParallelWriter inputs;

        public NativeQueue<EntityData<Entity>>.ParallelWriter outputs;

        public NativeQueue<Entity>.ParallelWriter results;

        public NativeQueue<MoveItem>.ParallelWriter moveItems;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask,
            in v128 chunkEnabledMask)
        {
            Spawn spawn;
            spawn.time = time;
            spawn.spawner = spawner;
            spawn.physicsTriggerEvents = physicsTriggerEvents;
            spawn.physicsShapeParents = physicsShapeParents;
            spawn.areaNodePresentations = areaNodePresentations;
            spawn.areaNodes = areaNodes;
            spawn.nodeStates = nodeStates;
            spawn.itemRoots = itemRoots;
            spawn.owners = owners;
            spawn.entityArray = chunk.GetNativeArray(entityType);
            spawn.physicsShapeChildEntities = chunk.GetBufferAccessor(ref physicsShapeChildEntityType);
            spawn.followers = chunk.GetBufferAccessor(ref followerType);
            spawn.nodes = chunk.GetBufferAccessor(ref nodeType);
            spawn.randomNodes = chunk.GetBufferAccessor(ref randomNodeType);
            spawn.rangeEntities = chunk.GetBufferAccessor(ref rangeEntityType);
            spawn.states = chunk.GetNativeArray(ref statusType);
            spawn.coolDownTimes = chunk.GetNativeArray(ref coolDownTimeType);
            spawn.inputs = inputs;
            spawn.outputs = outputs;
            spawn.results = results;
            spawn.moveItems = moveItems;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
            {
                bool isRangeEntityEnabledSource = chunk.IsComponentEnabled(ref rangeEntityType, i), isRangeEntityEnabledDestination = isRangeEntityEnabledSource;
                if(spawn.Execute(i, ref isRangeEntityEnabledDestination))
                    chunk.SetComponentEnabled(ref randomNodeType, i, true);
                
                if(isRangeEntityEnabledDestination != isRangeEntityEnabledSource)
                    chunk.SetComponentEnabled(ref rangeEntityType, i, isRangeEntityEnabledDestination);
            }
        }
    }

    [BurstCompile]
    private struct Apply : IJob
    {
        public GameItemManager itemManager;
        
        [ReadOnly] 
        public BufferLookup<GameFollower> followers;

        public BufferLookup<GameRangeSpawner> rangeSpawners;

        public ComponentLookup<GameNodeStatus> nodeStates;
        
        public NativeQueue<EntityData<Entity>> inputs;

        public NativeQueue<EntityData<Entity>> outputs;

        public NativeQueue<Entity> entities;

        public NativeQueue<MoveItem> moveItems;

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

            int index;
            DynamicBuffer<Entity> rangeSpawners;
            while (outputs.TryDequeue(out var temp))
            {
                if (this.rangeSpawners.HasBuffer(temp.value))
                {
                    rangeSpawners = this.rangeSpawners[temp.value].Reinterpret<Entity>();
                    index = rangeSpawners.AsNativeArray().IndexOf(temp.entity);
                    if (index != -1)
                    {
                        rangeSpawners.RemoveAtSwapBack(index);
                        if(rangeSpawners.Length < 1)
                            this.rangeSpawners.SetBufferEnabled(temp.value, false);
                    }
                }
            }
            
            while (inputs.TryDequeue(out var temp))
            {
                if (this.rangeSpawners.HasBuffer(temp.value))
                {
                    this.rangeSpawners[temp.value].Reinterpret<Entity>().Add(temp.entity);
                    
                    this.rangeSpawners.SetBufferEnabled(temp.value, true);
                }
            }

            int parentChildIndex;
            GameItemHandle parentHandle;
            GameItemInfo item;
            while (moveItems.TryDequeue(out var moveItem))
            {
                if (itemManager.TryGetValue(moveItem.source, out item) &&
                    itemManager.Find(moveItem.destination, item.type, item.count, out parentChildIndex,
                        out parentHandle))
                    itemManager.Move(moveItem.source, parentHandle, parentChildIndex);
            }
        }
    }

    private struct Filter
    {
        [ReadOnly]
        public ComponentLookup<GameRangeSpawnerOrigin> origins;
        [ReadOnly]
        public ComponentLookup<PhysicsShapeParent> physicsShapeParents;
        [ReadOnly]
        public BufferAccessor<PhysicsTriggerEvent> physicsTriggerEvents;
        [ReadOnly]
        public BufferAccessor<GameRangeSpawner> spawners;
        
        public NativeArray<GameRangeSpawnerTarget> targets;

        public bool Execute(int index)
        {
            Entity entity;
            GameRangeSpawnerTarget target;
            var physicsTriggerEvents = this.physicsTriggerEvents[index];
            var spawners = this.spawners[index];
            foreach (var spawner in spawners)
            {
                if(!origins.HasComponent(spawner.entity))
                    continue;

                foreach (var physicsTriggerEvent in physicsTriggerEvents)
                {
                    entity = physicsShapeParents.HasComponent(physicsTriggerEvent.entity)
                        ? physicsShapeParents[physicsTriggerEvent.entity].entity
                        : physicsTriggerEvent.entity;
                    if (entity == spawner.entity)
                    {
                        target.value = origins[entity].value;
                        targets[index] = target;

                        return true;
                    }
                }
            }

            return false;
        }
    }

    [BurstCompile]
    private struct FilterEx : IJobChunk
    {
        [ReadOnly]
        public ComponentLookup<GameRangeSpawnerOrigin> origins;
        [ReadOnly]
        public ComponentLookup<PhysicsShapeParent> physicsShapeParents;
        [ReadOnly]
        public BufferTypeHandle<PhysicsTriggerEvent> physicsTriggerEventType;
        [ReadOnly]
        public BufferTypeHandle<GameRangeSpawner> spawnerType;

        public ComponentTypeHandle<GameRangeSpawnerTarget> targetType;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask,
            in v128 chunkEnabledMask)
        {
            Filter filter;
            filter.origins = origins;
            filter.physicsShapeParents = physicsShapeParents;
            filter.physicsTriggerEvents = chunk.GetBufferAccessor(ref physicsTriggerEventType);
            filter.spawners = chunk.GetBufferAccessor(ref spawnerType);
            filter.targets = chunk.GetNativeArray(ref targetType);
            
            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                chunk.SetComponentEnabled(ref targetType, i, filter.Execute(i));
        }
    }

    private EntityQuery __groupToEnable;
    private EntityQuery __groupToUpdate;
    private EntityQuery __groupToFilter;

    private BufferLookup<GameFollower> __followers;
    private BufferLookup<PhysicsTriggerEvent> __physicsTriggerEvents;
    private ComponentLookup<PhysicsShapeParent> __physicsShapeParents;
    private ComponentLookup<GameAreaNodePresentation> __areaNodePresentations;
    private ComponentLookup<GameAreaNode> __areaNodes;
    private ComponentLookup<GameItemRoot> __itemRoots;
    private ComponentLookup<GameOwner> __owners;
    private ComponentLookup<GameRangeSpawnerOrigin> __origins;

    private EntityTypeHandle __entityType;
    
    private BufferTypeHandle<PhysicsShapeChildEntity> __physicsShapeChildEntityType;
    private BufferTypeHandle<PhysicsTriggerEvent> __physicsTriggerEventType;
    private BufferTypeHandle<GameFollower> __followerType;

    private BufferTypeHandle<GameRangeSpawnerNode> __nodeType;

    private BufferTypeHandle<GameRandomSpawnerNode> __randomNodeType;

    private BufferTypeHandle<GameRangeSpawnerEntity> __rangeEntityType;

    private BufferTypeHandle<GameRangeSpawner> __rangeSpawnerType;

    private ComponentTypeHandle<GameRangeSpawnerTarget> __targetType;

    private ComponentTypeHandle<GameRangeSpawnerCoolDownTime> __coolDownTimeType;

    private ComponentTypeHandle<GameRangeSpawnerStatus> __statusType;

    private ComponentLookup<GameNodeStatus> __nodeStates;
    
    private BufferLookup<GameRangeSpawner> __rangeSpawners;

    private GameRandomSpawner __spawner;
    
    private NativeQueue<EntityData<Entity>> __inputs;
    private NativeQueue<EntityData<Entity>> __outputs;
    private NativeQueue<Entity> __entities;
    private NativeQueue<MoveItem> __moveItems;

    private GameItemManagerShared __itemManager;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __groupToEnable = builder
                .WithAll<GameRangeSpawnerNode>()
                .WithNone<GameRangeSpawnerEntity>()
                .Build(ref state);

        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __groupToUpdate = builder
                .WithAll<GameRangeSpawnerNode>()
                .WithAllRW<GameRangeSpawnerEntity, GameRangeSpawnerCoolDownTime>()
                .WithAllRW<GameRangeSpawnerStatus>()
                .WithOptions(EntityQueryOptions.IncludeDisabledEntities)
                .Build(ref state);
                
        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __groupToFilter = builder
                .WithAll<PhysicsTriggerEvent>()
                .WithAny<GameRangeSpawner>()
                .WithAnyRW<GameRangeSpawnerTarget>()
                .WithOptions(EntityQueryOptions.IncludeDisabledEntities)
                .Build(ref state);

        __followers = state.GetBufferLookup<GameFollower>(true);
        __physicsTriggerEvents = state.GetBufferLookup<PhysicsTriggerEvent>(true);
        __physicsShapeParents = state.GetComponentLookup<PhysicsShapeParent>(true);
        __areaNodePresentations = state.GetComponentLookup<GameAreaNodePresentation>(true);
        __areaNodes = state.GetComponentLookup<GameAreaNode>(true);
        __itemRoots = state.GetComponentLookup<GameItemRoot>(true);
        __owners = state.GetComponentLookup<GameOwner>(true);
        __origins = state.GetComponentLookup<GameRangeSpawnerOrigin>(true);
        
        __entityType = state.GetEntityTypeHandle();
        __physicsShapeChildEntityType = state.GetBufferTypeHandle<PhysicsShapeChildEntity>(true);
        __physicsTriggerEventType = state.GetBufferTypeHandle<PhysicsTriggerEvent>(true);
        __followerType = state.GetBufferTypeHandle<GameFollower>(true);

        __nodeType = state.GetBufferTypeHandle<GameRangeSpawnerNode>(true);

        __randomNodeType = state.GetBufferTypeHandle<GameRandomSpawnerNode>();

        __rangeEntityType = state.GetBufferTypeHandle<GameRangeSpawnerEntity>();
        
        __rangeSpawnerType = state.GetBufferTypeHandle<GameRangeSpawner>();
        
        __targetType = state.GetComponentTypeHandle<GameRangeSpawnerTarget>();

        __coolDownTimeType = state.GetComponentTypeHandle<GameRangeSpawnerCoolDownTime>();
        
        __statusType = state.GetComponentTypeHandle<GameRangeSpawnerStatus>();

        __nodeStates = state.GetComponentLookup<GameNodeStatus>();
        
        __rangeSpawners = state.GetBufferLookup<GameRangeSpawner>();

        var world = state.WorldUnmanaged;

        __spawner = world.GetExistingSystemUnmanaged<GameRandomSpawnerSystem>().spawner;
        
        __itemManager = world.GetExistingSystemUnmanaged<GameItemSystem>().manager;

        __inputs = new NativeQueue<EntityData<Entity>>(Allocator.Persistent);
        __outputs = new NativeQueue<EntityData<Entity>>(Allocator.Persistent);
        __entities = new NativeQueue<Entity>(Allocator.Persistent);
        __moveItems = new NativeQueue<MoveItem>(Allocator.Persistent);
    }

    public void OnDestroy(ref SystemState state)
    {
        __entities.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var rangeEntityType = __rangeEntityType.UpdateAsRef(ref state);
        
        Enable enable;
        enable.rangeEntityType = rangeEntityType;
        var jobHandle = enable.ScheduleParallelByRef(__groupToEnable, state.Dependency);
        
        var physicsShapeParents = __physicsShapeParents.UpdateAsRef(ref state);
        var nodeStates = __nodeStates.UpdateAsRef(ref state);
        
        SpawnEx spawn;
        spawn.time = state.WorldUnmanaged.Time.ElapsedTime;
        spawn.spawner = __spawner.reader;
        spawn.physicsTriggerEvents = __physicsTriggerEvents.UpdateAsRef(ref state);
        spawn.physicsShapeParents = physicsShapeParents;
        spawn.areaNodePresentations = __areaNodePresentations.UpdateAsRef(ref state);
        spawn.areaNodes = __areaNodes.UpdateAsRef(ref state);
        spawn.nodeStates = nodeStates;
        spawn.itemRoots = __itemRoots.UpdateAsRef(ref state);
        spawn.owners = __owners.UpdateAsRef(ref state);
        spawn.entityType = __entityType.UpdateAsRef(ref state);
        spawn.physicsShapeChildEntityType = __physicsShapeChildEntityType.UpdateAsRef(ref state);
        spawn.followerType = __followerType.UpdateAsRef(ref state);
        spawn.nodeType = __nodeType.UpdateAsRef(ref state);
        spawn.randomNodeType = __randomNodeType.UpdateAsRef(ref state);
        spawn.rangeEntityType = rangeEntityType;
        spawn.statusType = __statusType.UpdateAsRef(ref state);
        spawn.coolDownTimeType = __coolDownTimeType.UpdateAsRef(ref state);
        spawn.inputs = __inputs.AsParallelWriter();
        spawn.outputs = __outputs.AsParallelWriter();
        spawn.results = __entities.AsParallelWriter();
        spawn.moveItems = __moveItems.AsParallelWriter();

        ref var spawnerJobManager = ref __spawner.lookupJobManager;

        jobHandle = spawn.ScheduleParallelByRef(__groupToUpdate,
            JobHandle.CombineDependencies(spawnerJobManager.readWriteJobHandle, jobHandle));

        Apply apply;
        apply.itemManager = __itemManager.value;
        apply.followers = __followers.UpdateAsRef(ref state);
        apply.rangeSpawners = __rangeSpawners.UpdateAsRef(ref state);
        apply.nodeStates = nodeStates;
        apply.inputs = __inputs;
        apply.outputs = __outputs;
        apply.entities = __entities;
        apply.moveItems = __moveItems;
        apply.spawner = __spawner.writer;

        ref var itemManagerJobManager = ref __itemManager.lookupJobManager;

        jobHandle = apply.ScheduleByRef(JobHandle.CombineDependencies(jobHandle, itemManagerJobManager.readWriteJobHandle));

        itemManagerJobManager.readWriteJobHandle = jobHandle;

        spawnerJobManager.readWriteJobHandle = jobHandle;

        FilterEx filter;
        filter.origins = __origins.UpdateAsRef(ref state);
        filter.physicsShapeParents = physicsShapeParents;
        filter.physicsTriggerEventType = __physicsTriggerEventType.UpdateAsRef(ref state);
        filter.spawnerType = __rangeSpawnerType.UpdateAsRef(ref state);
        filter.targetType = __targetType.UpdateAsRef(ref state);
        jobHandle = filter.ScheduleParallelByRef(__groupToFilter, jobHandle);

        state.Dependency = jobHandle;
    }
}
