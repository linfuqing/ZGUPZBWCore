using Unity.Jobs;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;
using ZG;

[assembly: RegisterGenericJobType(typeof(TimeManager<Entity>.Clear))]
[assembly: RegisterGenericJobType(typeof(TimeManager<Entity>.UpdateEvents))]

[BurstCompile, CreateAfter(typeof(EndFrameStructChangeSystem)), UpdateInGroup(typeof(TimeSystemGroup)), UpdateAfter(typeof(GameSyncSystemGroup))]
public partial struct GameSpawnerTimeSystem : ISystem
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

        public EntityAddDataQueue.ParallelWriter entityManager;

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

            GameSpawnedInstanceInfo result;
            result.time = time;
            entityManager.AddComponentData(entity, result);
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

        public EntityAddDataQueue.ParallelWriter entityManager;

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

    private BufferLookup<GameSpawnerAssetCounter> __counters;

    private ComponentTypeHandle<GameOwner> __ownerType;

    private ComponentTypeHandle<GameSpawnedInstanceData> __instanceType;

    private ComponentTypeHandle<GameActorMaster> __masterType;

    private ComponentLookup<GameNodeStatus> __states;

    private ComponentLookup<GameSpawnedInstanceData> __instances;

    private BufferTypeHandle<GameFollower> __followerType;

    private BufferTypeHandle<GameSpawnerAssetCounter> __counterType;

    private EntityAddDataPool __entityManager;

    private TimeManager<Entity> __timeManager;

    private NativeArray<GameSpawnerAsset> __assets;
    private NativeQueue<TimeEvent<Entity>> __timeEvents;

    public void Create(NativeArray<GameSpawnerAsset> assets)
    {
        __assets = new NativeArray<GameSpawnerAsset>(assets, Allocator.Persistent);
    }

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __groupToInit = builder
                    .WithAll<GameSpawnedInstanceData, GameOwner>()
                    .WithNone<GameSpawnedInstanceInfo>()
                    .WithOptions(EntityQueryOptions.IncludeDisabledEntities)
                    .Build(ref state);

        __groupToInit.SetChangedVersionFilter(ComponentType.ReadOnly<GameOwner>());

        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __groupToCount = builder
                    .WithAll<GameFollower>()
                    .WithAllRW<GameSpawnerAssetCounter>()
                    .WithOptions(EntityQueryOptions.IncludeDisabledEntities)
                    .Build(ref state);
        __groupToCount.SetChangedVersionFilter(ComponentType.ReadOnly<GameFollower>());

        __entityType = state.GetEntityTypeHandle();
        __counters = state.GetBufferLookup<GameSpawnerAssetCounter>(true);
        __ownerType = state.GetComponentTypeHandle<GameOwner>(true);
        __instanceType = state.GetComponentTypeHandle<GameSpawnedInstanceData>(true);
        __masterType = state.GetComponentTypeHandle<GameActorMaster>();
        __states = state.GetComponentLookup<GameNodeStatus>();

        __instances = state.GetComponentLookup<GameSpawnedInstanceData>(true);
        __followerType = state.GetBufferTypeHandle<GameFollower>(true);
        __counterType = state.GetBufferTypeHandle<GameSpawnerAssetCounter>();

        __entityManager = state.WorldUnmanaged.GetExistingSystemUnmanaged<EndFrameStructChangeSystem>().addDataPool;//GetOrCreateSystemManaged<EndTimeSystemGroupEntityCommandSystem>().CreateAddComponentDataCommander<GameSpawnedInstanceInfo>();

        __timeManager = new TimeManager<Entity>(Allocator.Persistent);

        __timeEvents = new NativeQueue<TimeEvent<Entity>>(Allocator.Persistent);
    }

    //[BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        if (__assets.IsCreated)
            __assets.Dispose();

        __timeManager.Dispose();

        __timeEvents.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!__assets.IsCreated)
            return;

        var entityManager = __entityManager.Create();

        double time = state.WorldUnmanaged.Time.ElapsedTime;

        var entityCount = new NativeArray<int>(1, Allocator.TempJob, NativeArrayOptions.ClearMemory);
        var inputDeps = __groupToInit.CalculateEntityCountAsync(entityCount, state.Dependency);

        InitEx init;
        init.time = time;
        init.assets = __assets;
        init.entityType = __entityType.UpdateAsRef(ref state);
        init.ownerType = __ownerType.UpdateAsRef(ref state);
        init.counters = __counters.UpdateAsRef(ref state);
        init.instanceType = __instanceType.UpdateAsRef(ref state);
        init.masterType = __masterType.UpdateAsRef(ref state);
        init.timeEvents = __timeEvents.AsParallelWriter();
        init.entityManager = entityManager.AsComponentParallelWriter<GameSpawnedInstanceInfo>(entityCount, ref inputDeps);
        inputDeps = init.ScheduleParallelByRef(__groupToInit, inputDeps);

        entityManager.AddJobHandleForProducer<InitEx>(inputDeps);

        Move move;
        move.inputs = __timeEvents;
        move.outputs = __timeManager.writer;

        var jobHandle = move.Schedule(inputDeps);

        jobHandle = __timeManager.Schedule(time, jobHandle);

        Die die;
        die.entities = __timeManager.values;
        die.states = __states.UpdateAsRef(ref state);
        jobHandle = __timeManager.ScheduleParallel(ref die, InnerloopBatchCount, jobHandle);

        jobHandle = __timeManager.Flush(jobHandle);

        CountEx count;
        count.assets = __assets;
        count.instances = __instances.UpdateAsRef(ref state);
        count.followerType = __followerType.UpdateAsRef(ref state);
        count.counterType = __counterType.UpdateAsRef(ref state);

        state.Dependency = JobHandle.CombineDependencies(
            count.ScheduleParallelByRef(__groupToCount, inputDeps),
            entityCount.Dispose(inputDeps), 
            jobHandle);
    }
}