using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using ZG;

[/*AlwaysUpdateSystem, */BurstCompile, UpdateInGroup(typeof(GameRollbackSystemGroup)), UpdateAfter(typeof(GameStatusSystemGroup))]
public partial struct GameContainerChildSystem : ISystem
{
    private struct Dirty
    {
        [ReadOnly]
        public NativeArray<Entity> entityArray;

        [ReadOnly]
        public BufferAccessor<GameContainerChild> children;

        public SharedMultiHashMap<Entity, EntityData<int>>.Writer parentIndices;

        public SharedMultiHashMap<Entity, EntityData<int>>.Writer childIndices;

        public NativeList<Entity> results;

        public void Execute(int index)
        {
            bool isDirty = false;
            var children = this.children[index];
            Entity entity = entityArray[index];
            GameContainerChild child;
            EntityData<int> parentIndex;
            NativeParallelMultiHashMapIterator<Entity> parentIterator;
            int numChildren = children.Length, i;
            if (childIndices.TryGetFirstValue(entity, out var childIndex, out var iterator))
            {
                do
                {
                    for (i = 0; i < numChildren; ++i)
                    {
                        child = children[i];
                        if (child.index == childIndex.value && child.entity == childIndex.entity)
                            break;
                    }

                    if (i == numChildren)
                    {
                        isDirty = true;

                        if(results.IsCreated)
                            results.Add(childIndex.entity);

                        if (parentIndices.TryGetFirstValue(childIndex.entity, out parentIndex, out parentIterator))
                        {
                            do
                            {
                                if (parentIndex.value == childIndex.value && parentIndex.entity == entity)
                                {
                                    parentIndices.Remove(parentIterator);

                                    break;
                                }
                            } while (parentIndices.TryGetNextValue(out parentIndex, ref parentIterator));
                        }
                    }

                } while (childIndices.TryGetNextValue(out childIndex, ref iterator));
            }

            childIndices.Remove(entity);

            bool isContains;
            for (i = 0; i < numChildren; ++i)
            {
                child = children[i];
                if (child.entity == Entity.Null)
                    continue;

                childIndex.value = child.index;
                childIndex.entity = child.entity;
                childIndices.Add(entity, childIndex);

                isContains = false;
                if (parentIndices.TryGetFirstValue(child.entity, out parentIndex, out parentIterator))
                {
                    do
                    {
                        if (parentIndex.value == child.index && parentIndex.entity == entity)
                        {
                            isContains = true;

                            break;
                        }
                    } while (parentIndices.TryGetNextValue(out parentIndex, ref parentIterator));
                }

                if (!isContains)
                {
                    parentIndex.value = child.index;
                    parentIndex.entity = entity;
                    parentIndices.Add(child.entity, parentIndex);

                    //isDirty = true;
                }
            }

            if (isDirty && results.IsCreated)
                results.Add(entity);
        }
    }

    [BurstCompile]
    private struct DirtyEx : IJobChunk
    {
        [ReadOnly]
        public EntityTypeHandle entityType;

        [ReadOnly]
        public ComponentTypeHandle<GameContainerStatusDisabled> statusDisabled;

        [ReadOnly]
        public BufferTypeHandle<GameContainerChild> childType;

        public SharedMultiHashMap<Entity, EntityData<int>>.Writer parentIndices;

        public SharedMultiHashMap<Entity, EntityData<int>>.Writer childIndices;

        public NativeList<Entity> results;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Dirty dirty;
            dirty.entityArray = chunk.GetNativeArray(entityType);
            dirty.children = chunk.GetBufferAccessor(ref childType);
            dirty.parentIndices = parentIndices;
            dirty.childIndices = childIndices;
            dirty.results = chunk.Has(ref statusDisabled) ? default : results;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                dirty.Execute(i);
        }
    }

    [BurstCompile]
    private struct Change : IJob
    {
        [ReadOnly]
        public SharedList<Entity>.Reader entityArray;

        [ReadOnly]
        public ComponentLookup<GameContainerWeight> weights;

        [ReadOnly]
        public ComponentLookup<GameContainerBearing> bearings;

        [ReadOnly]
        public SharedMultiHashMap<Entity, EntityData<int>>.Writer childIndices;

        [ReadOnly]
        public SharedMultiHashMap<Entity, EntityData<int>>.Writer parentIndices;

        public NativeList<Entity> results;

        //public NativeParallelMultiHashMap<Entity, Entity> parents;

        //public NativeHashMap<Entity, int> results;

        public ComponentLookup<GameNodeStatus> states;

        public static bool Contains(
            in Entity entity,
            in Entity parent,
            in NativeParallelMultiHashMap<Entity, Entity> parents)
        {
            if (parents.TryGetFirstValue(entity, out Entity temp, out var tempIterator))
            {
                do
                {
                    if (temp == parent || Contains(temp, parent, parents))
                        return true;

                } while (parents.TryGetNextValue(out temp, ref tempIterator));
            }

            if (parents.TryGetFirstValue(parent, out temp, out tempIterator))
            {
                do
                {
                    if (temp == entity || Contains(entity, temp, parents))
                        return true;

                } while (parents.TryGetNextValue(out temp, ref tempIterator));
            }

            return false;
        }

        public void Execute()
        {
            int length = entityArray.length;
            if (length + this.results.Length < 1)
                return;

            int i;
            Entity entity;
            var results = new UnsafeHashMap<Entity, int>(length, Allocator.Temp);
            if (length > 0)
            {
                for (i = 0; i < length; ++i)
                {
                    entity = entityArray[i];

                    if (parentIndices.TryGetFirstValue(entity, out var index, out var iterator))
                    {
                        do
                        {
                            this.results.Add(index.entity);
                        } while (parentIndices.TryGetNextValue(out index, ref iterator));
                    }

                    if (childIndices.TryGetFirstValue(entity, out index, out iterator))
                    {
                        do
                        {
                            this.results.Add(index.entity);
                        } while (childIndices.TryGetNextValue(out index, ref iterator));
                    }

                    results[entity] = -1;//0;
                }
            }

            GameNodeStatus status;
            status.value = (int)GameEntityStatus.Dead;

            length = this.results.Length;
            //var parents = new NativeParallelMultiHashMap<Entity, Entity>(length, Allocator.Temp);
            for (i = 0; i < length; ++i)
            {
                entity = this.results[i];
                if (GetBearing(1, entity, /*Entity.Null, ref parents, */ref results) < 1 && states.HasComponent(entity))
                    states[entity] = status;
            }

            results.Dispose();

            this.results.Clear();
        }

        public int GetBearing(
            int minBearing,
            in Entity entity,
            //in Entity parent,
            //ref NativeParallelMultiHashMap<Entity, Entity> parents,
            ref UnsafeHashMap<Entity, int> results)
        {
            if (results.TryGetValue(entity, out int bearing))
            {
                if (bearing == -1)
                {
                    /*if (!Contains(entity, parent, parents))
                    {
                        parents.Add(entity, parent);
                        //parents.Add(parent, entity);
                    }*/

                    bearing = 0;
                }

                return bearing;
            }

            int maxBearing;
            if (bearings.HasComponent(entity))
            {
                maxBearing = bearings[entity].value;
                if (maxBearing > 0)
                    return maxBearing;
            }

            maxBearing = -1;

            results[entity] = maxBearing;

            /*if (parent != Entity.Null)
            {
                parents.Add(entity, parent);
            }*/

            if (parentIndices.TryGetFirstValue(entity, out var index, out var iterator))
            {
                do
                {
                    /*if (index.entity == parent)
                        continue;*/

                    maxBearing = math.max(maxBearing, GetBearing(minBearing, index.entity, /*entity, ref parents, */ref results));
                } while (parentIndices.TryGetNextValue(out index, ref iterator));
            }

            if (childIndices.TryGetFirstValue(entity, out index, out iterator))
            {
                do
                {
                    /*if (index.entity == parent)
                        continue;*/

                    maxBearing = math.max(maxBearing, GetBearing(minBearing, index.entity, /*entity, ref parents, */ref results));
                } while (childIndices.TryGetNextValue(out index, ref iterator));
            }

            //maxBearing = __UpdateParentBearings(minBearing, maxBearing, entity, Entity.Null, ref parents, ref results);
            return __UpdateChildBearings(math.max(0, maxBearing), minBearing, entity, ref results);

            //return maxBearing;
        }

        /*private int __UpdateParentBearings(
            int minBearing, 
            int bearing,
            in Entity entity,
            in Entity child, 
            ref NativeParallelMultiHashMap<Entity, Entity> parents,
            ref NativeHashMap<Entity, int> results)
        {
            int destination = bearing, source = results[entity];
            if (destination > source)
            {
                results[entity] = destination;

                if (destination > 0 && parents.TryGetFirstValue(entity, out Entity parent, out var iterator))
                {
                    bearing = math.clamp(bearing - (weights.HasComponent(entity) ? weights[entity].value : 0), minBearing, math.max(minBearing, bearing));
                    do
                    {
                        if (parent == child)
                            continue;

                        __UpdateParentBearings(minBearing, bearing, parent, entity, ref parents, ref results);
                    } while (parents.TryGetNextValue(out parent, ref iterator));
                }

                return results[entity];
            }

            destination = math.max(0, source);

            results[entity] = destination;

            return destination;
        }*/

        private int __UpdateChildBearings(int bearing, int minBearing, Entity entity, ref UnsafeHashMap<Entity, int> results)
        {
            int destination = bearing > 0 ? math.clamp(bearing - (weights.HasComponent(entity) ? weights[entity].value : 0), minBearing, math.max(minBearing, bearing)) : 0,
                source = results[entity];
            if (destination > source)
            {
                results[entity] = destination;

                if (destination > 0)
                {
                    if (parentIndices.TryGetFirstValue(entity, out var index, out var iterator))
                    {
                        do
                        {
                            if (results.TryGetValue(index.entity, out source) && source > -1)
                                __UpdateChildBearings(destination, minBearing, index.entity, ref results);
                        } while (parentIndices.TryGetNextValue(out index, ref iterator));
                    }

                    if (childIndices.TryGetFirstValue(entity, out index, out iterator))
                    {
                        do
                        {
                            if (results.TryGetValue(index.entity, out source) && source > -1)
                                __UpdateChildBearings(destination, minBearing, index.entity, ref results);
                        } while (childIndices.TryGetNextValue(out index, ref iterator));
                    }
                }

                return results[entity];
            }

            destination = math.max(0, source);

            results[entity] = destination;

            return destination;
        }
    }

    [BurstCompile]
    private struct Clear : IJob
    {
        //[ReadOnly]
        public SharedList<Entity>.Writer entities;

        public BufferLookup<GameContainerChild> children;

        public SharedMultiHashMap<Entity, EntityData<int>>.Writer childIndices;

        public SharedMultiHashMap<Entity, EntityData<int>>.Writer parentIndices;

        public void Execute()
        {
            int length = entities.length, numChildren, i, j;
            Entity entity;
            EntityData<int> childEntityIndex, parentEntityIndex;
            NativeParallelMultiHashMapIterator<Entity> childIterator, parentIterator;
            GameContainerChild child;
            DynamicBuffer<GameContainerChild> children;
            for (i = 0; i < length; ++i)
            {
                entity = entities[i];

                if (this.children.HasBuffer(entity))
                    this.children[entity].Clear();

                if (childIndices.TryGetFirstValue(entity, out childEntityIndex, out childIterator))
                {
                    do
                    {
                        if (parentIndices.TryGetFirstValue(childEntityIndex.entity, out parentEntityIndex, out parentIterator))
                        {
                            do
                            {
                                if (parentEntityIndex.value == childEntityIndex.value && parentEntityIndex.entity == entity)
                                {
                                    parentIndices.Remove(parentIterator);

                                    break;
                                }
                            } while (parentIndices.TryGetNextValue(out parentEntityIndex, ref parentIterator));
                        }
                    } while (childIndices.TryGetNextValue(out childEntityIndex, ref childIterator));

                    childIndices.Remove(entity);
                }

                if (parentIndices.TryGetFirstValue(entity, out parentEntityIndex, out parentIterator))
                {
                    do
                    {
                        if (childIndices.TryGetFirstValue(parentEntityIndex.entity, out childEntityIndex, out childIterator))
                        {
                            do
                            {
                                if (childEntityIndex.value == parentEntityIndex.value && childEntityIndex.entity == entity)
                                {
                                    childIndices.Remove(childIterator);

                                    break;
                                }
                            } while (childIndices.TryGetNextValue(out childEntityIndex, ref childIterator));
                        }

                        if (this.children.HasBuffer(parentEntityIndex.entity))
                        {
                            children = this.children[parentEntityIndex.entity];
                            numChildren = children.Length;
                            for (j = 0; j < numChildren; ++j)
                            {
                                child = children[j];
                                if (child.index == parentEntityIndex.value && child.entity == entity)
                                {
                                    children.RemoveAt(j);

                                    break;
                                }
                            }
                        }
                    } while (parentIndices.TryGetNextValue(out parentEntityIndex, ref parentIterator));

                    parentIndices.Remove(entity);
                }
            }

            entities.Clear();
        }
    }

    public SharedMultiHashMap<Entity, EntityData<int>> childIndices { get; private set; }

    public SharedMultiHashMap<Entity, EntityData<int>> parentIndices { get; private set; }

    public SharedList<Entity> statusResults
    {
        get;

        private set;
    }

    //private uint __lastSystemVersion;
    private EntityQuery __group;
    private ComponentTypeHandle<GameContainerStatusDisabled> __disableType;
    private BufferTypeHandle<GameContainerChild> __childrenType;
    private BufferLookup<GameContainerChild> __children;
    private ComponentLookup<GameNodeStatus> __states;
    private ComponentLookup<GameContainerWeight> __weights;
    private ComponentLookup<GameContainerBearing> __bearings;

    //private BlobAssetReference<uint> __statusLastSystemVersion;
    private NativeList<Entity> __results;

    public void OnCreate(ref SystemState state)
    {
        BurstUtility.InitializeJob<Change>();
        BurstUtility.InitializeJob<Clear>();

        state.SetAlwaysUpdateSystem(true);

        __group = state.GetEntityQuery(
            new EntityQueryDesc()
            {
                All = new ComponentType[]
                {
                    ComponentType.ReadOnly<GameContainerChild>()
                },
                Options = EntityQueryOptions.IncludeDisabledEntities
            });
        __group.SetChangedVersionFilter(typeof(GameContainerChild));

        __disableType = state.GetComponentTypeHandle<GameContainerStatusDisabled>(true);
        __childrenType = state.GetBufferTypeHandle<GameContainerChild>(true);
        __children = state.GetBufferLookup<GameContainerChild>();
        __states = state.GetComponentLookup<GameNodeStatus>();
        __weights = state.GetComponentLookup<GameContainerWeight>(true);
        __bearings = state.GetComponentLookup<GameContainerBearing>(true);

        //__statusLastSystemVersion = statusSystem.lastSystemVersion;
        __results = new NativeList<Entity>(Allocator.Persistent);

        statusResults = new SharedList<Entity>(Allocator.Persistent);

        childIndices = new SharedMultiHashMap<Entity, EntityData<int>>(Allocator.Persistent);
        parentIndices = new SharedMultiHashMap<Entity, EntityData<int>>(Allocator.Persistent);
    }

    public void OnDestroy(ref SystemState state)
    {
        __results.Dispose();

        childIndices.Dispose();
        parentIndices.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var childIndices = this.childIndices;
        var parentIndices = this.parentIndices;

        var childIndexWriter = childIndices.writer;
        var parentIndexWriter = parentIndices.writer;

        ref var childIndexJobManager = ref childIndices.lookupJobManager;
        ref var parentIndexJobManager = ref parentIndices.lookupJobManager;

        //childIndexJobManager.CompleteReadWriteDependency();
        //parentIndexJobManager.CompleteReadWriteDependency();

        JobHandle jobHandle = JobHandle.CombineDependencies(childIndexJobManager.readWriteJobHandle, parentIndexJobManager.readWriteJobHandle, state.Dependency);

        if (!__group.IsEmptyIgnoreFilter)
        {
            __results.Clear();

            DirtyEx dirty;
            dirty.entityType = state.GetEntityTypeHandle();
            dirty.statusDisabled = __disableType.UpdateAsRef(ref state);
            dirty.childType = __childrenType.UpdateAsRef(ref state);
            dirty.childIndices = childIndexWriter;
            dirty.parentIndices = parentIndexWriter;
            dirty.results = __results;
            jobHandle = dirty.Schedule(__group, jobHandle);
        }

        //uint lastSystemVersion = __statusLastSystemVersion.Value;
        //if (ChangeVersionUtility.DidChange(lastSystemVersion, __lastSystemVersion))
        {
            //__lastSystemVersion = lastSystemVersion;

            var statusResults = this.statusResults;

            ref var statusJobManager = ref statusResults.lookupJobManager;

            Change change;
            change.entityArray = statusResults.reader;
            change.weights = __weights.UpdateAsRef(ref state);
            change.bearings = __bearings.UpdateAsRef(ref state);
            change.childIndices = childIndexWriter;
            change.parentIndices = parentIndexWriter;
            change.states = __states.UpdateAsRef(ref state);
            change.results = __results;
            jobHandle = change.Schedule(JobHandle.CombineDependencies(jobHandle, statusJobManager.readOnlyJobHandle));

            Clear clear;
            clear.entities = statusResults.writer;
            clear.children = __children.UpdateAsRef(ref state);
            clear.childIndices = childIndexWriter;
            clear.parentIndices = parentIndexWriter;
            jobHandle = clear.Schedule(JobHandle.CombineDependencies(jobHandle, statusJobManager.readWriteJobHandle));

            statusJobManager.readWriteJobHandle = jobHandle;
        }

        childIndexJobManager.readWriteJobHandle = jobHandle;
        parentIndexJobManager.readWriteJobHandle = jobHandle;

        state.Dependency = jobHandle;
    }
}

[BurstCompile/*AlwaysUpdateSystem*/, UpdateInGroup(typeof(GameStatusSystemGroup))]
public partial struct GameContainerChildStatusSystem : ISystem
{
    [BurstCompile]
    private struct Resize : IJob
    {
        [ReadOnly, DeallocateOnJobCompletion]
        public NativeArray<int> counter;

        public SharedList<Entity>.Writer results;

        public void Execute()
        {
            results.Clear();

            results.capacity = math.max(results.capacity, results.length + counter[0]);
        }
    }

    private struct Init
    {
        [ReadOnly]
        public NativeArray<Entity> entityArray;

        [ReadOnly]
        public NativeArray<GameNodeStatus> states;

        [ReadOnly]
        public NativeArray<GameNodeOldStatus> oldStates;

        [ReadOnly]
        public SharedMultiHashMap<Entity, EntityData<int>>.Reader childIndices;

        [ReadOnly]
        public SharedMultiHashMap<Entity, EntityData<int>>.Reader parentIndices;

        public SharedList<Entity>.ParallelWriter results;

        public void Execute(int index)
        {
            var status = states[index].value & (int)GameEntityStatus.Mask;
            if (status != (int)GameEntityStatus.Dead)
                return;

            if (status == (oldStates[index].value & (int)GameEntityStatus.Mask))
                return;

            Entity entity = entityArray[index];
            if (!parentIndices.ContainsKey(entity) &&
                !childIndices.ContainsKey(entity))
                return;

            results.AddNoResize(entity);
        }
    }

    [BurstCompile]
    private struct InitEx : IJobChunk
    {
        [ReadOnly]
        public EntityTypeHandle entityType;

        [ReadOnly]
        public ComponentTypeHandle<GameNodeStatus> statusType;

        [ReadOnly]
        public ComponentTypeHandle<GameNodeOldStatus> oldStatusType;

        [ReadOnly]
        public SharedMultiHashMap<Entity, EntityData<int>>.Reader childIndices;

        [ReadOnly]
        public SharedMultiHashMap<Entity, EntityData<int>>.Reader parentIndices;

        public SharedList<Entity>.ParallelWriter results;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Init init;
            init.entityArray = chunk.GetNativeArray(entityType);
            init.states = chunk.GetNativeArray(ref statusType);
            init.oldStates = chunk.GetNativeArray(ref oldStatusType);
            init.childIndices = childIndices;
            init.parentIndices = parentIndices;
            init.results = results;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                init.Execute(i);
        }
    }

    //public BlobAssetReference<uint> lastSystemVersion => __lastSystemVersion;

    private EntityQuery __group;

    private SharedList<Entity> __results;

    //private BlobAssetReference<uint> __lastSystemVersion;
    private SharedMultiHashMap<Entity, EntityData<int>> __childIndices;
    private SharedMultiHashMap<Entity, EntityData<int>> __parentIndices;

    public void OnCreate(ref SystemState state)
    {
        BurstUtility.InitializeJob<Resize>();

        //state.SetAlwaysUpdateSystem(true);

        __group = state.GetEntityQuery(
            new EntityQueryDesc()
            {
                All = new ComponentType[]
                {
                    ComponentType.ReadOnly<GameNodeStatus>(),
                    ComponentType.ReadOnly<GameNodeOldStatus>(),
                },
                Any = new ComponentType[]
                {
                    ComponentType.ReadOnly<GameContainerChild>(),
                    ComponentType.ReadOnly<GameContainerWeight>(),
                    ComponentType.ReadOnly<GameContainerBearing>(),
                },

                None = new ComponentType[]
                {
                    typeof(GameContainerStatusDisabled)
                },

                Options = EntityQueryOptions.IncludeDisabledEntities
            });
        __group.SetChangedVersionFilter(new ComponentType[]
            {
                typeof(GameNodeStatus),
                typeof(GameNodeOldStatus)
            });

        /*using (var blobBuilder = new BlobBuilder(Allocator.Temp))
        {
            blobBuilder.ConstructRoot<uint>() = 0;

            __lastSystemVersion = blobBuilder.CreateBlobAssetReference<uint>(Allocator.Persistent);
        }*/

        ref var childSystem = ref state.World.GetOrCreateSystemUnmanaged<GameContainerChildSystem>();

        __childIndices = childSystem.childIndices;
        __parentIndices = childSystem.parentIndices;

        __results = childSystem.statusResults;
    }

    public void OnDestroy(ref SystemState state)
    {
        //results.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        //__lastSystemVersion.Value = state.LastSystemVersion;

        __results.lookupJobManager.CompleteReadWriteDependency();

        var counter = new NativeArray<int>(1, Allocator.TempJob, NativeArrayOptions.ClearMemory);
        var jobHandle = __group.CalculateEntityCountAsync(counter, state.Dependency);

        Resize resize;
        resize.counter = counter;
        resize.results = __results.writer;
        jobHandle = resize.Schedule(jobHandle);

        InitEx initEx;
        initEx.entityType = state.GetEntityTypeHandle();
        initEx.statusType = state.GetComponentTypeHandle<GameNodeStatus>(true);
        initEx.oldStatusType = state.GetComponentTypeHandle<GameNodeOldStatus>(true);
        initEx.childIndices = __childIndices.reader;
        initEx.parentIndices = __parentIndices.reader;
        initEx.results = __results.parallelWriter;

        ref var childIndicesJobManager = ref __childIndices.lookupJobManager;
        ref var parentIndicesJobManager = ref __parentIndices.lookupJobManager;

        jobHandle = initEx.ScheduleParallel(
            __group, 
            JobHandle.CombineDependencies(
                childIndicesJobManager.readOnlyJobHandle, 
                parentIndicesJobManager.readOnlyJobHandle,
                jobHandle));

        childIndicesJobManager.AddReadOnlyDependency(jobHandle);
        parentIndicesJobManager.AddReadOnlyDependency(jobHandle);

        state.Dependency = jobHandle;
    }
}