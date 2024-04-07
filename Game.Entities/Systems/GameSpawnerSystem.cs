using Unity.Jobs;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;
using ZG;

[assembly: RegisterGenericJobType(typeof(TimeManager<Entity>.UpdateEvents))]

[BurstCompile, /*CreateAfter(typeof(BeginFrameStructChangeSystem)), */UpdateInGroup(typeof(TimeSystemGroup)), UpdateAfter(typeof(GameSyncSystemGroup))]
public partial struct GameSpawnerTimeSystem : ISystem
{
    private struct Init
    {
        public double time;

        //[ReadOnly]
        //public BufferLookup<GameSpawnerAssetCounter> counters;

        [ReadOnly]
        public NativeArray<GameSpawnerAsset> assets;

        [ReadOnly]
        public NativeArray<Entity> entityArray;

        //[ReadOnly]
        //public NativeArray<GameOwner> owners;

        [ReadOnly]
        public NativeArray<GameSpawnedInstanceData> instances;

        //public NativeArray<GameActorMaster> masters;

        public NativeList<TimeEvent<Entity>>.ParallelWriter timeEvents;

        //public EntityAddDataQueue.ParallelWriter entityManager;

        public void Execute(int index)
        {
            /*var owner = owners[index].entity;
            if (owner == Entity.Null)
                return;

            if (!counters.HasBuffer(owner))
                return;*/

            //Entity entity = entityArray[index];
            //var instance = instances[index];

            var asset = assets[instances[index].assetIndex];
            if (asset.deadline > math.FLT_MIN_NORMAL)
            {
                TimeEvent<Entity> timeEvent;
                timeEvent.time = time + asset.deadline;
                timeEvent.value = entityArray[index];

                timeEvents.AddNoResize(timeEvent);
            }

            /*if (index < masters.Length)
            {
                GameActorMaster master = masters[index];
                if (master.entity != owner)
                {
                    master.entity = owner;
                    masters[index] = master;
                }
            }*/

            /*GameSpawnedInstanceInfo result;
            result.time = time;
            entityManager.AddComponentData(entity, result);*/
        }
    }

    [BurstCompile]
    private struct InitEx : IJobChunk, IEntityCommandProducerJob
    {
        //public uint lastSystemVersion;

        public double time;

        //[ReadOnly]
        //public BufferLookup<GameSpawnerAssetCounter> counters;

        [ReadOnly]
        public NativeArray<GameSpawnerAsset> assets;

        [ReadOnly]
        public EntityTypeHandle entityType;

        [ReadOnly]
        public ComponentTypeHandle<Disabled> disabledType;

        //[ReadOnly]
        //public ComponentTypeHandle<GameOwner> ownerType;

        //[ReadOnly]
        public ComponentTypeHandle<GameSpawnedInstanceData> instanceType;

        //public ComponentTypeHandle<GameActorMaster> masterType;

        public NativeList<Entity>.ParallelWriter entitiesToCannel;

        public NativeList<TimeEvent<Entity>>.ParallelWriter entitiesToInvoke;

        //public EntityAddDataQueue.ParallelWriter entityManager;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            var entityArray = chunk.GetNativeArray(entityType);
            if (chunk.Has(ref disabledType))
            {
                //UnityEngine.Assertions.Assert.AreNotEqual(default, chunkEnabledMask);

                var negateIterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);

                while (negateIterator.NextEntityIndex(out int i))
                {
                    chunk.SetComponentEnabled(ref instanceType, i, true);

                    entitiesToCannel.AddNoResize(entityArray[i]);
                }
            }
            else
            {
                Init init;
                init.time = time;
                //init.counters = counters;
                init.assets = assets;
                init.entityArray = entityArray;
                //init.owners = chunk.GetNativeArray(ref ownerType);
                init.instances = chunk.GetNativeArray(ref instanceType);
                //init.masters = chunk.GetNativeArray(ref masterType);
                init.timeEvents = entitiesToInvoke;
                //init.entityManager = entityManager;

                var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (iterator.NextEntityIndex(out int i))
                {
                    init.Execute(i);

                    chunk.SetComponentEnabled(ref instanceType, i, false);
                }
            }
        }
    }

    private struct Count
    {
        [ReadOnly]
        public NativeArray<GameSpawnerAsset> assets;

        [ReadOnly]
        public ComponentLookup<GameSpawnedInstanceData> instances;

        [ReadOnly]
        public BufferAccessor<GameFollower> followers;

        public BufferAccessor<GameSpawnerAssetCounter> counters;

        public void Execute(int index)
        {
            var counters = this.counters[index];
            counters.Clear();

            var followers = this.followers[index];
            GameFollower follower;
            GameSpawnerAssetCounter counter;
            int numFollowers = followers.Length, numCounters = counters.Length, assetIndex, i, j;
            for(i = 0; i < numFollowers; ++i)
            {
                follower = followers[i];
                if (!instances.HasComponent(follower.entity))
                    continue;

                assetIndex = instances[follower.entity].assetIndex;
                for(j = 0; j < numCounters; ++j)
                {
                    counter = counters[j]; 
                    if (counter.assetIndex == assetIndex)
                    {
                        --counter.value;

                        counters[j] = counter;

                        break;
                    }
                }

                if (j == numCounters)
                {
                    counter.assetIndex = assetIndex;
                    counter.value = assets[assetIndex].capacity - 1;

                    counters.Add(counter);

                    ++numCounters;
                }
            }
        }
    }

    [BurstCompile]
    private struct CountEx : IJobChunk
    {
        [ReadOnly]
        public NativeArray<GameSpawnerAsset> assets;

        [ReadOnly]
        public ComponentLookup<GameSpawnedInstanceData> instances;

        [ReadOnly]
        public BufferTypeHandle<GameFollower> followerType;

        public BufferTypeHandle<GameSpawnerAssetCounter> counterType;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Count count;
            count.assets = assets;
            count.instances = instances;
            count.followers = chunk.GetBufferAccessor(ref followerType);
            count.counters = chunk.GetBufferAccessor(ref counterType);

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                count.Execute(i);
        }
    }

    [BurstCompile]
    private struct Move : IJob
    {
        [ReadOnly]
        public NativeArray<Entity> entitiesToCannel;

        [ReadOnly]
        public NativeArray<TimeEvent<Entity>> entitiesToInvoke;

        public TimeManager<Entity>.Writer results;

        public ComponentLookup<GameSpawnedInstanceDeadline> deadlines;

        public void Execute()
        {
            foreach (var result in entitiesToCannel)
            {
                if (results.Cancel(deadlines[result].handle))
                    deadlines[result] = default;
            }

            GameSpawnedInstanceDeadline deadline;
            foreach (var result in entitiesToInvoke)
            {
                deadline.handle = results.Invoke(result.time, result.value);
                deadlines[result.value] = deadline;
            }

            //inputs.Clear();
        }
    }

    [BurstCompile]
    private struct Die : IJobParallelForDefer
    {
        [ReadOnly]
        public NativeArray<Entity> entities;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<GameNodeStatus> states;

        public void Execute(int index)
        {
            Entity entity = entities[index];
            if (!states.HasComponent(entity))
                return;

            if ((states[entity].value & (int)GameEntityStatus.Mask) == (int)GameEntityStatus.Dead)
                return;

            GameNodeStatus status;
            status.value = (int)GameEntityStatus.Dead;
            states[entity] = status;
        }
    }

    public static readonly int InnerloopBatchCount = 32;

    private EntityQuery __groupToInit;
    private EntityQuery __groupToCount;

    private EntityTypeHandle __entityType;

    private ComponentTypeHandle<Disabled> __disabledType;

    //private BufferLookup<GameSpawnerAssetCounter> __counters;

    //private ComponentTypeHandle<GameOwner> __ownerType;

    private ComponentTypeHandle<GameSpawnedInstanceData> __instanceType;

    //private ComponentTypeHandle<GameActorMaster> __masterType;

    private ComponentLookup<GameNodeStatus> __states;

    private ComponentLookup<GameSpawnedInstanceDeadline> __deadlines;

    private ComponentLookup<GameSpawnedInstanceData> __instances;

    private BufferTypeHandle<GameFollower> __followerType;

    private BufferTypeHandle<GameSpawnerAssetCounter> __counterType;

    //private EntityAddDataPool __entityManager;

    private TimeManager<Entity> __timeManager;

    private NativeList<Entity> __commands;

    private NativeList<Entity> __entitiesToCannel;
    private NativeList<TimeEvent<Entity>> __entitiesToInvoke;

    private NativeArray<GameSpawnerAsset> __assets;

    public void Create(NativeArray<GameSpawnerAsset> assets)
    {
        __assets = new NativeArray<GameSpawnerAsset>(assets, Allocator.Persistent);
    }

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __groupToInit = builder
                    .WithAll<GameSpawnedInstanceDeadline, GameSpawnedInstanceData/*, GameOwner*/>()
                    //.WithNone<GameSpawnedInstanceInfo>()
                    //.WithOptions(EntityQueryOptions.IncludeDisabledEntities)
                    .AddAdditionalQuery()
                    .WithAll<GameSpawnedInstanceDeadline, Disabled>()
                    .WithNone<GameSpawnedInstanceData>()
                    .WithOptions(EntityQueryOptions.IncludeDisabledEntities)
                    .Build(ref state);

        //__groupToInit.SetChangedVersionFilter(ComponentType.ReadOnly<GameOwner>());

        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __groupToCount = builder
                    .WithAll<GameFollower>()
                    .WithAllRW<GameSpawnerAssetCounter>()
                    .WithOptions(EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IgnoreComponentEnabledState)
                    .Build(ref state);
        __groupToCount.SetChangedVersionFilter(ComponentType.ReadOnly<GameFollower>());

        __entityType = state.GetEntityTypeHandle();
        __disabledType = state.GetComponentTypeHandle<Disabled>(true);
        //__counters = state.GetBufferLookup<GameSpawnerAssetCounter>(true);
        //__ownerType = state.GetComponentTypeHandle<GameOwner>(true);
        __instanceType = state.GetComponentTypeHandle<GameSpawnedInstanceData>();
        __deadlines = state.GetComponentLookup<GameSpawnedInstanceDeadline>();
        //__masterType = state.GetComponentTypeHandle<GameActorMaster>();
        __states = state.GetComponentLookup<GameNodeStatus>();

        __instances = state.GetComponentLookup<GameSpawnedInstanceData>(true);
        __followerType = state.GetBufferTypeHandle<GameFollower>(true);
        __counterType = state.GetBufferTypeHandle<GameSpawnerAssetCounter>();

        //__entityManager = state.WorldUnmanaged.GetExistingSystemUnmanaged<BeginFrameStructChangeSystem>().addDataPool;//GetOrCreateSystemManaged<EndTimeSystemGroupEntityCommandSystem>().CreateAddComponentDataCommander<GameSpawnedInstanceInfo>();

        __timeManager = new TimeManager<Entity>(Allocator.Persistent);

        __commands = new NativeList<Entity>(Allocator.Persistent);

        __entitiesToCannel = new NativeList<Entity>(Allocator.Persistent);
        __entitiesToInvoke = new NativeList<TimeEvent<Entity>>(Allocator.Persistent);
    }

    //[BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        if (__assets.IsCreated)
            __assets.Dispose();

        __timeManager.Dispose();

        __entitiesToCannel.Dispose();
        __entitiesToInvoke.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!__assets.IsCreated)
            return;

        //var entityManager = __entityManager.Create();

        double time = state.WorldUnmanaged.Time.ElapsedTime;

        //var entityCount = CollectionHelper.CreateNativeArray<int>(1, state.WorldUpdateAllocator, NativeArrayOptions.ClearMemory);
        var inputDeps = state.Dependency;// __groupToInit.CalculateEntityCountAsync(entityCount, state.Dependency);

        int entityCount = __groupToInit.CalculateEntityCountWithoutFiltering();

        __entitiesToCannel.Clear();
        __entitiesToCannel.Capacity = math.max(__entitiesToCannel.Capacity, entityCount);

        __entitiesToInvoke.Clear();
        __entitiesToInvoke.Capacity = math.max(__entitiesToInvoke.Capacity, entityCount);

        InitEx init;
        init.time = time;
        init.assets = __assets;
        init.entityType = __entityType.UpdateAsRef(ref state);
        init.disabledType = __disabledType.UpdateAsRef(ref state);
        //init.ownerType = __ownerType.UpdateAsRef(ref state);
        //init.counters = __counters.UpdateAsRef(ref state);
        init.instanceType = __instanceType.UpdateAsRef(ref state);
        //init.masterType = __masterType.UpdateAsRef(ref state);
        init.entitiesToCannel = __entitiesToCannel.AsParallelWriter();
        init.entitiesToInvoke = __entitiesToInvoke.AsParallelWriter();
        //init.entityManager = entityManager.AsComponentParallelWriter<GameSpawnedInstanceInfo>(entityCount, ref inputDeps);
        inputDeps = init.ScheduleParallelByRef(__groupToInit, inputDeps);

        //entityManager.AddJobHandleForProducer<InitEx>(inputDeps);

        Move move;
        move.entitiesToCannel = __entitiesToCannel.AsDeferredJobArray();
        move.entitiesToInvoke = __entitiesToInvoke.AsDeferredJobArray();
        move.results = __timeManager.writer;
        move.deadlines = __deadlines.UpdateAsRef(ref state);

        var jobHandle = move.ScheduleByRef(inputDeps);

        __commands.Clear();

        jobHandle = __timeManager.Schedule(time, ref __commands, jobHandle);

        Die die;
        die.entities = __commands.AsDeferredJobArray();
        die.states = __states.UpdateAsRef(ref state);
        jobHandle = die.ScheduleByRef(__commands, InnerloopBatchCount, jobHandle);

        CountEx count;
        count.assets = __assets;
        count.instances = __instances.UpdateAsRef(ref state);
        count.followerType = __followerType.UpdateAsRef(ref state);
        count.counterType = __counterType.UpdateAsRef(ref state);

        state.Dependency = JobHandle.CombineDependencies(
            count.ScheduleParallelByRef(__groupToCount, inputDeps),
            jobHandle);
    }
}