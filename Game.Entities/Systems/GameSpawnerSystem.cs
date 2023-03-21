using Unity.Jobs;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;
using ZG;

[assembly: RegisterGenericJobType(typeof(TimeManager<Entity>.Clear))]
[assembly: RegisterGenericJobType(typeof(TimeManager<Entity>.UpdateEvents))]

[/*AlwaysUpdateSystem, */UpdateInGroup(typeof(TimeSystemGroup)), UpdateAfter(typeof(GameSyncSystemGroup))]
public partial class GameSpawnerTimeSystem : SystemBase
{
    private struct Init
    {
        public double time;

        [ReadOnly]
        public BufferLookup<GameSpawnerAssetCounter> counters;

        [ReadOnly]
        public NativeArray<GameSpawnerAsset> assets;

        [ReadOnly]
        public NativeArray<Entity> entityArray;

        [ReadOnly]
        public NativeArray<GameOwner> owners;

        [ReadOnly]
        public NativeArray<GameSpawnedInstanceData> instances;

        public NativeArray<GameActorMaster> masters;

        public NativeQueue<TimeEvent<Entity>>.ParallelWriter timeEvents;

        public EntityCommandQueue<EntityData<GameSpawnedInstanceInfo>>.ParallelWriter entityManager;

        public void Execute(int index)
        {
            var owner = owners[index].entity;
            if (owner == Entity.Null)
                return;

            if (!counters.HasBuffer(owner))
                return;

            Entity entity = entityArray[index];
            var instance = instances[index];

            var asset = assets[instance.assetIndex];
            if (asset.deadline > math.FLT_MIN_NORMAL)
            {
                TimeEvent<Entity> timeEvent;
                timeEvent.time = time + asset.deadline;
                timeEvent.value = entity;

                timeEvents.Enqueue(timeEvent);
            }

            if (index < masters.Length)
            {
                GameActorMaster master = masters[index];
                if (master.entity != owner)
                {
                    master.entity = owner;
                    masters[index] = master;
                }
            }

            EntityData<GameSpawnedInstanceInfo> result;
            result.entity = entity;
            result.value.time = time;
            entityManager.Enqueue(result);
        }
    }

    [BurstCompile]
    private struct InitEx : IJobChunk, IEntityCommandProducerJob
    {
        public double time;

        [ReadOnly]
        public BufferLookup<GameSpawnerAssetCounter> counters;

        [ReadOnly]
        public NativeArray<GameSpawnerAsset> assets;

        [ReadOnly]
        public EntityTypeHandle entityType;

        [ReadOnly]
        public ComponentTypeHandle<GameOwner> ownerType;

        [ReadOnly]
        public ComponentTypeHandle<GameSpawnedInstanceData> instanceType;

        public ComponentTypeHandle<GameActorMaster> masterType;

        public NativeQueue<TimeEvent<Entity>>.ParallelWriter timeEvents;

        public EntityCommandQueue<EntityData<GameSpawnedInstanceInfo>>.ParallelWriter entityManager;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Init init;
            init.time = time;
            init.counters = counters;
            init.assets = assets;
            init.entityArray = chunk.GetNativeArray(entityType);
            init.owners = chunk.GetNativeArray(ref ownerType);
            init.instances = chunk.GetNativeArray(ref instanceType);
            init.masters = chunk.GetNativeArray(ref masterType);
            init.timeEvents = timeEvents;
            init.entityManager = entityManager;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                init.Execute(i);
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
        public NativeQueue<TimeEvent<Entity>> inputs;

        public TimeManager<Entity>.Writer outputs;

        public void Execute()
        {
            while (inputs.TryDequeue(out var result))
                outputs.Invoke(result.time, result.value);
        }
    }

    [BurstCompile]
    private struct Die : IJobParalledForDeferBurstSchedulable
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

    public int innerloopBatchCount = 32;

    private EntityQuery __groupToInit;
    private EntityQuery __groupToCount;
    private EntityCommandPool<EntityData<GameSpawnedInstanceInfo>> __entityManager;

    private TimeManager<Entity> __timeManager;

    private NativeArray<GameSpawnerAsset> __assets;
    private NativeQueue<TimeEvent<Entity>> __timeEvents;

    public void Create(NativeArray<GameSpawnerAsset> assets)
    {
        __assets = new NativeArray<GameSpawnerAsset>(assets, Allocator.Persistent);
    }

    protected override void OnCreate()
    {
        BurstUtility.InitializeJobParalledForDefer<Die>();

        base.OnCreate();

        __groupToInit = GetEntityQuery(
            new EntityQueryDesc()
            {
                All = new ComponentType[]
                {
                    ComponentType.ReadOnly<GameSpawnedInstanceData>(), ComponentType.ReadOnly<GameOwner>()
                },
                None = new ComponentType[]
                {
                    typeof(GameSpawnedInstanceInfo)
                }, 
                Options = EntityQueryOptions.IncludeDisabledEntities
            });
        __groupToInit.SetChangedVersionFilter(typeof(GameOwner));

        __groupToCount = GetEntityQuery(new EntityQueryDesc()
        {
            All = new ComponentType[]
            {
                ComponentType.ReadOnly<GameFollower>(), 
                ComponentType.ReadWrite<GameSpawnerAssetCounter>()
            },
            Options = EntityQueryOptions.IncludeDisabledEntities
        });
        __groupToCount.SetChangedVersionFilter(typeof(GameFollower));

        World world = World;
        __entityManager = world.GetOrCreateSystemManaged<EndTimeSystemGroupEntityCommandSystem>().CreateAddComponentDataCommander<GameSpawnedInstanceInfo>();

        __timeManager = new TimeManager<Entity>(Allocator.Persistent);

        __timeEvents = new NativeQueue<TimeEvent<Entity>>(Allocator.Persistent);
    }

    protected override void OnDestroy()
    {
        if (__assets.IsCreated)
            __assets.Dispose();

        __timeManager.Dispose();

        __timeEvents.Dispose();

        base.OnDestroy();
    }

    protected override void OnUpdate()
    {
        if (!__assets.IsCreated)
            return;

        var entityManager = __entityManager.Create();

        double time = World.Time.ElapsedTime;

        InitEx init;
        init.time = time;
        init.assets = __assets;
        init.entityType = GetEntityTypeHandle();
        init.ownerType = GetComponentTypeHandle<GameOwner>(true);
        init.counters = GetBufferLookup<GameSpawnerAssetCounter>(true);
        init.instanceType = GetComponentTypeHandle<GameSpawnedInstanceData>(true);
        init.masterType = GetComponentTypeHandle<GameActorMaster>();
        init.timeEvents = __timeEvents.AsParallelWriter();
        init.entityManager = entityManager.parallelWriter;
        var inputDeps = init.ScheduleParallel(__groupToInit, Dependency);

        entityManager.AddJobHandleForProducer<InitEx>(inputDeps);

        Move move;
        move.inputs = __timeEvents;
        move.outputs = __timeManager.writer;

        var jobHandle = move.Schedule(inputDeps);

        jobHandle = __timeManager.Schedule(time, jobHandle);

        Die die;
        die.entities = __timeManager.values;
        die.states = GetComponentLookup<GameNodeStatus>();
        jobHandle = __timeManager.ScheduleParallel(die, innerloopBatchCount, jobHandle);

        jobHandle = __timeManager.Flush(jobHandle);

        CountEx count;
        count.assets = __assets;
        count.instances = GetComponentLookup<GameSpawnedInstanceData>(true);
        count.followerType = GetBufferTypeHandle<GameFollower>(true);
        count.counterType = GetBufferTypeHandle<GameSpawnerAssetCounter>();
        inputDeps = count.ScheduleParallel(__groupToCount, inputDeps);

        Dependency = JobHandle.CombineDependencies(jobHandle, inputDeps);
    }
}