using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Jobs;
using Unity.Entities;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using ZG;

[BurstCompile, 
    UpdateInGroup(typeof(GameRollbackSystemGroup)),
    UpdateAfter(typeof(GameStatusSystemGroup)), 
    UpdateBefore(typeof(GameNodeInitSystemGroup))]
public partial struct GameDreamerSystem : ISystem
{
    private struct Dreaming
    {
        public GameTime time;

        [ReadOnly]
        public NativeArray<Entity> entityArray;
        [ReadOnly]
        public NativeArray<GameNodeStatus> statusInputs;
        [ReadOnly]
        public BufferAccessor<GameDream> dreams;
        public BufferAccessor<GameDreamerEvent> events;

        public NativeArray<GameDreamerInfo> dreamerInfos;
        public NativeArray<GameDreamer> dreamers;
        public NativeArray<GameNodeDelay> delay;

        [NativeDisableContainerSafetyRestriction]
        public ComponentLookup<GameNodeStatus> statusOutputs;

        [NativeDisableContainerSafetyRestriction]
        public ComponentLookup<GameDreamerVersion> versions;

        public EntityCommandQueue<EntityCommandStructChange>.ParallelWriter entityManager;

#if GAME_DEBUG_COMPARSION
        //public NativeQueue<LogInfo>.ParallelWriter logInfos;

        public uint frameIndex;

        public Words statusName;
        public Words dreamerName;
        public Words dreamerTimeName;
        public Words delayTimeName;

        public ComparisonStream<int> stream;
        [ReadOnly]
        public NativeArray<GameEntityIndex> entityIndices;
#endif

        public void Execute(int index)
        {
            Entity entity = entityArray[index];
            var status = statusInputs[index];
            var dreamer = dreamers[index];
            GameDreamerEvent result;

/*#if GAME_DEBUG_COMPARSION
            //UnityEngine.Debug.Log($"Dream: {entityIndices[index].value} : {entityArray[index].Index} : {status.value} : {(int)dreamer.status} : {(double)dreamer.time} : {dreamer.currentIndex} : {(double)delay[index].time} : {frameIndex}");

            stream.Begin(entityIndices[index].value);
            stream.Assert(statusName, status.value);
            if(dreamer.status != GameDreamerStatus.Awake)
            {
                stream.Assert(dreamerName, dreamer.status);
                stream.Assert(dreamerTimeName, (double)dreamer.time);
                stream.Assert(delayTimeName, (float)(GameDeadline.Max(delay[index].time, time) - dreamer.time));
            }

            stream.End();
#endif*/

            while (true)
            {
                switch (dreamer.status)
                {
                    case GameDreamerStatus.Normal:
                        bool isAwake = false;
                        switch ((GameDreamerStatus)status.value)
                        {
                            /*case GameDreamerStatus.Normal:
                                isAwake = delay[index].time < dreamer.time;
                                break;*/
                            case GameDreamerStatus.Dream:
                                //UnityEngine.Debug.Log($"Dream {entity.Index} : {(double)dreamer.time} : {(double)time}");

                                if (dreamer.time > time)
                                    return;

                                isAwake = true;
                                break;
                            case GameDreamerStatus.Sleep:
                                var delay = this.delay[index];
                                if (delay.Check(time))
                                    return;

                                dreamer.time = GameDeadline.Max(delay.time + delay.endTime, dreamer.time);

                                isAwake = true;
                                break;
                            /*case GameDreamerStatus.Sleep:
                                {
                                    GameNodeDelay delay = this.delay[index];
                                    delay.time = dreamer.time;
                                    this.delay[index] = delay;
                                    UnityEngine.Debug.Log("Changed: " + entityArray[index] + delay.time);

                                }

                                status.value = 0;
                                statusOutputs[entity] = status;
                                break;*/
                            case GameDreamerStatus.Awake:
                                if (dreamer.time > time)
                                {
                                    //UnityEngine.Debug.LogError($"{entity} Awaking {time} : {dreamer.time}");

                                    return;
                                }

                                status.value = 0;
                                statusOutputs[entity] = status;

                                break;
                        }

                        if(isAwake)
                        {
#if GAME_DEBUG_COMPARSION
                            //UnityEngine.Debug.Log($"Dream: {entityIndices[index].value} : {entityArray[index].Index} : {status.value} : {(int)dreamer.status} : {(double)dreamer.time} : {dreamer.currentIndex} : {(double)delay[index].time} : {frameIndex}");

                            stream.Begin(entityIndices[index].value);
                            stream.Assert(statusName, status.value);
                            stream.Assert(dreamerName, dreamer.status);
                            stream.Assert(dreamerTimeName, (double)dreamer.time);
                            stream.Assert(delayTimeName, (float)(GameDeadline.Max(delay[index].time, time) - dreamer.time));

                            stream.End();
#endif

                            DynamicBuffer<GameDream> dreams = this.dreams[index];
                            var dreamerInfo = dreamerInfos[index];
                            if (dreamerInfo.currentIndex >= 0 && dreamerInfo.currentIndex < dreams.Length)
                            {
                                GameDream dream = dreams[dreamerInfo.currentIndex];

                                var time = GameDeadline.Max(dreamer.time, (GameDeadline)this.time - dream.awakeTime);

                                dreamer.time = time + dream.awakeTime;
                                dreamers[index] = dreamer;

                                GameNodeDelay delay;
                                delay.time = time;
                                delay.startTime = half.zero;
                                delay.endTime = (half)dream.awakeTime;
                                this.delay[index] = delay;
                            }

                            status.value = (int)GameDreamerStatus.Awake;
                            statusOutputs[entity] = status;

                            var version = versions[entity];
                            ++version.value;
                            versions[entity] = version;

                            result.status = GameDreamerStatus.Awake;
                            result.version = version.value;
                            result.index = dreamerInfo.currentIndex;
                            result.time = time;

                            events[index].Add(result);

                            return;
                        }

                        dreamer.time = time;

                        break;
                    case GameDreamerStatus.Sleep:
                        bool isSleep;
                        switch ((GameDreamerStatus)status.value)
                        {
                            case GameDreamerStatus.Normal:
                                var delay = this.delay[index];
                                isSleep = !delay.Check(dreamer.time) || !delay.Check(time);
                                break;
                            case GameDreamerStatus.Dream:
                                isSleep = !this.delay[index].Check(time);
                                if(isSleep)
                                    dreamer.time = time;
                                break;
                            default:
                                isSleep = true;
                                break;
                        }
                        
                        if (isSleep)
                        {
                            var dreams = this.dreams[index];
                            var dreamerInfo = dreamerInfos[index];
                            if (dreamerInfo.nextIndex >= 0 && dreamerInfo.nextIndex < dreams.Length)
                            {
#if GAME_DEBUG_COMPARSION
                                //UnityEngine.Debug.Log($"Dream: {entityIndices[index].value} : {entityArray[index].Index} : {status.value} : {(int)dreamer.status} : {(double)dreamer.time} : {dreamer.currentIndex} : {(double)delay[index].time} : {frameIndex}");

                                stream.Begin(entityIndices[index].value);
                                stream.Assert(statusName, status.value);
                                stream.Assert(dreamerName, dreamer.status);
                                stream.Assert(dreamerTimeName, (double)dreamer.time);
                                stream.Assert(delayTimeName, (float)(GameDeadline.Max(this.delay[index].time, this.time) - dreamer.time));

                                stream.End();
#endif

                                //UnityEngine.Debug.Log($"Dream: {entityIndices[index].value} : {entityArray[index].Index} : {statusOutputs[entity].value} : {(int)dreamer.status} : {(double)dreamer.time} : {dreamer.currentIndex} : {(double)this.delay[index].time} : {frameIndex}");

                                GameDream dream = dreams[dreamerInfo.nextIndex];

                                var time = GameDeadline.Max(dreamer.time, (GameDeadline)this.time - dream.sleepTime);

                                dreamer.status = GameDreamerStatus.Dream;
                                dreamer.time = time + dream.sleepTime;
                                dreamers[index] = dreamer;

                                dreamerInfo.currentIndex = dreamerInfo.nextIndex;
                                dreamerInfo.nextIndex = dream.nextIndex;

                                ++dreamerInfo.level;
                                dreamerInfos[index] = dreamerInfo;

                                status.value = (int)GameDreamerStatus.Sleep;
                                statusOutputs[entity] = status;

                                GameNodeDelay delay;
                                delay.time = time;
                                delay.startTime = half.zero;
                                delay.endTime = (half)dream.sleepTime;
                                this.delay[index] = delay;

                                var version = versions[entity];
                                ++version.value;
                                versions[entity] = version;

                                result.status = GameDreamerStatus.Sleep;
                                result.version = version.value;
                                result.index = dreamerInfo.currentIndex;
                                result.time = time;

                                events[index].Add(result);

                                return;
                            }
                        }

                        dreamer.status = GameDreamerStatus.Unknown;
                        dreamer.time = time;

                        break;
                    case GameDreamerStatus.Dream:
                        switch ((GameDreamerStatus)status.value)
                        {
                            //打断则为Normal，故注释掉
                            /*case GameDreamerStatus.Normal:
                                double delayTime = delay[index].time;
                                if (delayTime < dreamer.time || delayTime < time)
                                {
                                    status.value = (int)GameDreamerStatus.Dream;
                                    statusOutputs[entity] = status;

                                    var version = versions[entity];
                                    ++version.value;
                                    versions[entity] = version;

                                    result.status = GameDreamerStatus.Dream;
                                    result.version = version.value;
                                    result.index = dreamer.currentIndex;
                                    result.time = dreamer.time;

                                    events[index].Add(result);

                                    continue;
                                }

                                break;*/
                            case GameDreamerStatus.Sleep:
                                if (dreamer.time <= time)
                                {
#if GAME_DEBUG_COMPARSION
                                    //UnityEngine.Debug.Log($"Dream: {entityIndices[index].value} : {entityArray[index].Index} : {status.value} : {(int)dreamer.status} : {(double)dreamer.time} : {dreamer.currentIndex} : {(double)delay[index].time} : {frameIndex}");

                                    stream.Begin(entityIndices[index].value);
                                    stream.Assert(statusName, status.value);
                                    stream.Assert(dreamerName, dreamer.status);
                                    stream.Assert(dreamerTimeName, (double)dreamer.time);
                                    stream.Assert(delayTimeName, (float)(GameDeadline.Max(delay[index].time, time) - dreamer.time));

                                    stream.End();
#endif
                                    status.value = (int)GameDreamerStatus.Dream;
                                    statusOutputs[entity] = status;

                                    var version = versions[entity];
                                    ++version.value;
                                    versions[entity] = version;

                                    result.status = GameDreamerStatus.Dream;
                                    result.version = version.value;
                                    result.index = dreamerInfos[index].currentIndex;
                                    result.time = time;

                                    events[index].Add(result);

                                    //continue;
                                }
                                return;
                            case GameDreamerStatus.Dream:
                                return;
                            ///???
                            /*case GameDreamerStatus.Awake:
                                status.value = (int)GameDreamerStatus.Normal;

                                continue;*/
                            default:
                                dreamer.status = GameDreamerStatus.Unknown;
                                dreamer.time = time;
                                break;
                        }
                        break;
                    case GameDreamerStatus.Awake:
                        //UnityEngine.Debug.Log($"A {entity} : {entityIndices[index].value} : {status.value} : {(double)this.delay[index].time} : {(double)dreamer.time}");
                        switch ((GameDreamerStatus)status.value)
                        {
                            case GameDreamerStatus.Normal:
                                if (this.delay[index].Check(time))
                                {
#if GAME_DEBUG_COMPARSION
                                    //UnityEngine.Debug.Log($"Dream: {entityIndices[index].value} : {entityArray[index].Index} : {status.value} : {(int)dreamer.status} : {(double)dreamer.time} : {dreamer.currentIndex} : {(double)delay[index].time} : {frameIndex}");

                                    stream.Begin(entityIndices[index].value);
                                    stream.Assert(statusName, status.value);
                                    stream.Assert(dreamerName, dreamer.status);
                                    stream.Assert(dreamerTimeName, (double)dreamer.time);
                                    stream.Assert(delayTimeName, (float)(GameDeadline.Max(this.delay[index].time, this.time) - dreamer.time));

                                    stream.End();
#endif
                                    dreamer.time = time;
                                    dreamer.status = GameDreamerStatus.Normal;
                                    dreamers[index] = dreamer;

                                    var version = versions[entity];
                                    ++version.value;
                                    versions[entity] = version;

                                    result.status = GameDreamerStatus.Awake;
                                    result.version = version.value;
                                    result.index = dreamerInfos[index].currentIndex;
                                    result.time = dreamer.time;

                                    events[index].Add(result);

                                    return;
                                }
                                else
                                    status.value = (int)GameDreamerStatus.Dream;
                                break;
                            case GameDreamerStatus.Sleep:
                                {
                                    var delay = this.delay[index];
                                    if (delay.Check(dreamer.time))
                                    {
                                        delay.Clear(dreamer.time);
                                        this.delay[index] = delay;

                                        status.value = 0;
                                        statusOutputs[entity] = status;
                                    }
                                }
                                break;
                                //多次调用醒来
                            case GameDreamerStatus.Awake:
                                {
                                    var delay = this.delay[index];
                                    dreamer.time = GameDeadline.Max(dreamer.time, delay.time + delay.endTime);
                                }
                                break;
                            default:
                                break;
                        }

                        dreamer.status = GameDreamerStatus.Normal;

                        continue;
                }

                {
#if GAME_DEBUG_COMPARSION
                    //UnityEngine.Debug.Log($"Dream: {entityIndices[index].value} : {entityArray[index].Index} : {status.value} : {(int)dreamer.status} : {(double)dreamer.time} : {dreamer.currentIndex} : {(double)delay[index].time} : {frameIndex}");

                    stream.Begin(entityIndices[index].value);
                    stream.Assert(statusName, status.value);
                    stream.Assert(dreamerName, dreamer.status);
                    stream.Assert(dreamerTimeName, (double)dreamer.time);
                    stream.Assert(delayTimeName, (float)(GameDeadline.Max(delay[index].time, time) - dreamer.time));

                    stream.End();
#endif

                    GameDreamerVersion version = versions[entity];
                    ++version.value;
                    versions[entity] = version;

                    result.status = dreamer.status;// GameDreamerStatus.Normal;
                    result.version = version.value;
                    result.index = dreamerInfos[index].currentIndex;
                    result.time = dreamer.time;

                    events[index].Add(result);

                    EntityCommandStructChange command;
                    command.componentType = ComponentType.ReadWrite<GameDreamer>();
                    command.entity = entity;
                    entityManager.Enqueue(command);
                }

                break;
            }

            //UnityEngine.Debug.Log($"Dream: {entityIndices[index].value} : {entityArray[index].Index} : {statusOutputs[entity].value} : {(int)dreamer.status} : {(double)dreamer.time} : {dreamer.currentIndex} : {(double)delay[index].time} : {frameIndex}");
        }
    }

    [BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Deterministic)]
    private struct DreamingEx : IJobChunk, IEntityCommandProducerJob
    {
        public GameTime time;

        [ReadOnly]
        public EntityTypeHandle entityType;
        [ReadOnly]
        public ComponentTypeHandle<GameNodeStatus> statusType;
        [ReadOnly]
        public BufferTypeHandle<GameDream> dreamType;
        public BufferTypeHandle<GameDreamerEvent> eventType;
        public ComponentTypeHandle<GameDreamerInfo> dreamerInfoType;
        public ComponentTypeHandle<GameDreamer> dreamerType;
        public ComponentTypeHandle<GameNodeDelay> delayType;

        [NativeDisableContainerSafetyRestriction]
        public ComponentLookup<GameNodeStatus> states;
        [NativeDisableParallelForRestriction]
        public ComponentLookup<GameDreamerVersion> versions;

        public EntityCommandQueue<EntityCommandStructChange>.ParallelWriter entityManager;

#if GAME_DEBUG_COMPARSION
        public uint frameIndex;

        public Words statusName;
        public Words dreamerName;

        public Words dreamerTimeName;
        public Words delayTimeName;

        public ComparisonStream<int> stream;
        [ReadOnly]
        public ComponentTypeHandle<GameEntityIndex> entityIndexType;
#endif

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Dreaming dreaming;
            dreaming.time = time;
            dreaming.entityArray = chunk.GetNativeArray(entityType);
            dreaming.statusInputs = chunk.GetNativeArray(ref statusType);
            dreaming.dreams = chunk.GetBufferAccessor(ref dreamType);
            dreaming.events = chunk.GetBufferAccessor(ref eventType);
            dreaming.dreamerInfos = chunk.GetNativeArray(ref dreamerInfoType);
            dreaming.dreamers = chunk.GetNativeArray(ref dreamerType);
            dreaming.delay = chunk.GetNativeArray(ref delayType);
            dreaming.statusOutputs = states;
            dreaming.versions = versions;
            dreaming.entityManager = entityManager;

#if GAME_DEBUG_COMPARSION
            dreaming.frameIndex = frameIndex;

            dreaming.statusName = statusName;
            dreaming.dreamerName = dreamerName;
            dreaming.dreamerTimeName = dreamerTimeName;
            dreaming.delayTimeName = delayTimeName;
            dreaming.stream = stream;
            dreaming.entityIndices = chunk.GetNativeArray(entityIndexType);
#endif

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                dreaming.Execute(i);
        }
    }

    private EntityQuery __group;
    //private EntityQuery __syncDataGroup;
    private GameRollbackTime __time;
    private EntityCommandPool<EntityCommandStructChange> __endFrameBarrier;

    public void OnCreate(ref SystemState state)
    {
        __group = state.GetEntityQuery(
            ComponentType.ReadOnly<GameNodeStatus>(),
            ComponentType.ReadOnly<GameDream>(),
            ComponentType.ReadWrite<GameDreamer>(),
            ComponentType.ReadWrite<GameDreamerInfo>(),
            ComponentType.ReadWrite<GameDreamerVersion>(),
            ComponentType.ReadWrite<GameNodeDelay>(), 
            ComponentType.Exclude<Disabled>());

        //__syncDataGroup = state.GetEntityQuery(ComponentType.ReadOnly<GameSyncData>());
        __time = new GameRollbackTime(ref state);

        __endFrameBarrier = state.World.GetOrCreateSystemUnmanaged<EndFrameSyncSystemGroupStructChangeSystem>().manager.removeComponentPool;
    }

    public void OnDestroy(ref SystemState state)
    {

    }

#if !GAME_DEBUG_COMPARSION
    [BurstCompile]
#endif
    public void OnUpdate(ref SystemState state)
    {
        var entityManager = __endFrameBarrier.Create();

        DreamingEx dreaming;
        dreaming.time = __time.now;// __syncDataGroup.GetSingleton<GameSyncData>().now;
        dreaming.entityType = state.GetEntityTypeHandle();
        dreaming.statusType = state.GetComponentTypeHandle<GameNodeStatus>(true);
        dreaming.dreamType = state.GetBufferTypeHandle<GameDream>(true);
        dreaming.eventType = state.GetBufferTypeHandle<GameDreamerEvent>();
        dreaming.dreamerInfoType = state.GetComponentTypeHandle<GameDreamerInfo>();
        dreaming.dreamerType = state.GetComponentTypeHandle<GameDreamer>();
        dreaming.delayType = state.GetComponentTypeHandle<GameNodeDelay>();
        dreaming.states = state.GetComponentLookup<GameNodeStatus>();
        dreaming.versions = state.GetComponentLookup<GameDreamerVersion>();
        dreaming.entityManager = entityManager.parallelWriter;

#if GAME_DEBUG_COMPARSION
        uint frameIndex = __time.frame.index;
        var streamScheduler = GameComparsionSystem.instance.Create(false, frameIndex, typeof(GameDreamerSystem).Name, state.World.Name);

        dreaming.frameIndex = frameIndex;
        dreaming.statusName = WordsUtility.Create("status");
        dreaming.dreamerName = WordsUtility.Create("dreamer");
        dreaming.dreamerTimeName = WordsUtility.Create("dreamerTime");
        dreaming.delayTimeName = WordsUtility.Create("delayTime");
        dreaming.stream = streamScheduler.Begin(__group.CalculateEntityCount());
        dreaming.entityIndexType = state.GetComponentTypeHandle<GameEntityIndex>(true);
#endif

        var jobHandle = dreaming.ScheduleParallel(__group, state.Dependency);

        entityManager.AddJobHandleForProducer<DreamingEx>(jobHandle);

#if GAME_DEBUG_COMPARSION
        streamScheduler.End(jobHandle);
#endif

        state.Dependency = jobHandle;
    }
}

[UpdateInGroup(typeof(TimeSystemGroup)), UpdateAfter(typeof(GameSyncSystemGroup))]
public partial class GameDreamEventSystem : SystemBase
{
    private struct Invoke
    {
        [ReadOnly]
        public NativeArray<Entity> entityArray;
        public BufferAccessor<GameDreamerEvent> events;
        public NativeQueue<EntityData<GameDreamerEvent>>.ParallelWriter results;

        public void Execute(int index)
        {
            EntityData<GameDreamerEvent> result;
            result.entity = entityArray[index];

            var events = this.events[index];
            int numEvents = events.Length;
            for(int i = 0; i < numEvents; ++i)
            {
                result.value = events[i];

                results.Enqueue(result);
            }    
            events.Clear();
        }
    }

    [BurstCompile]
    private struct InvokeEx : IJobChunk
    {
        [ReadOnly]
        public EntityTypeHandle entityType;

        public BufferTypeHandle<GameDreamerEvent> eventType;

        public NativeQueue<EntityData<GameDreamerEvent>>.ParallelWriter results;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Invoke invoke;
            invoke.entityArray = chunk.GetNativeArray(entityType);
            invoke.events = chunk.GetBufferAccessor(ref eventType);
            invoke.results = results;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                invoke.Execute(i);
        }
    }

    private JobHandle __jobHandle;
    private EntityQuery __group;
    private NativeQueue<EntityData<GameDreamerEvent>> __results;

    public NativeQueue<EntityData<GameDreamerEvent>> results
    {
        get
        {
            __jobHandle.Complete();
            __jobHandle = default;

            return __results;
        }
    }

    protected override void OnCreate()
    {
        base.OnCreate();

        __group = GetEntityQuery(
            new EntityQueryDesc()
            {
                All = new ComponentType[]
                {
                    ComponentType.ReadWrite<GameDreamerEvent>(), 
                    ComponentType.ReadOnly<GameDreamerVersion>()
                }, 
                Options = EntityQueryOptions.IncludeDisabledEntities
            });
        __group.SetChangedVersionFilter(typeof(GameDreamerVersion));

        __results = new NativeQueue<EntityData<GameDreamerEvent>>(Allocator.Persistent);
    }

    protected override void OnDestroy()
    {
        __results.Dispose();

        base.OnDestroy();
    }

    protected override void OnUpdate()
    {
        InvokeEx invoke;
        invoke.entityType = GetEntityTypeHandle();
        invoke.eventType = GetBufferTypeHandle<GameDreamerEvent>();
        invoke.results = __results.AsParallelWriter();
        __jobHandle = invoke.ScheduleParallel(__group, Dependency);

        Dependency = __jobHandle;
    }
}

public partial class GameDreamEventCallbackSystem : SystemBase
{
    private GameDreamEventSystem __eventSystem;

    protected override void OnCreate()
    {
        base.OnCreate();

        __eventSystem = World.GetOrCreateSystemManaged<GameDreamEventSystem>();
    }

    protected override void OnUpdate()
    {
        GameDreamerComponent instance = null;
        var enttiyManager = EntityManager;
        Entity entity = Entity.Null;
        var results = __eventSystem.results;
        while(results.TryDequeue(out var result))
        {
            if(result.entity != entity)
                instance = enttiyManager.GetComponentData<EntityObject<GameDreamerComponent>>(result.entity).value;

            instance._Changed(result.value);
        }
    }
}