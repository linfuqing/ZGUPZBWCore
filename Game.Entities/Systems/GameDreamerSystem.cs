using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Jobs;
using Unity.Entities;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using ZG;

[BurstCompile, 
 CreateAfter(typeof(EndFrameSyncSystemGroupStructChangeSystem)), 
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

        public FixedString32Bytes statusName;
        public FixedString32Bytes dreamerName;
        public FixedString32Bytes dreamerTimeName;
        public FixedString32Bytes delayTimeName;

        public ComparisonStream<uint> stream;
        [ReadOnly]
        public NativeArray<GameEntityIndex> entityIndices;
#endif

        public void Execute(int index)
        {
            Entity entity = entityArray[index];
            var status = statusInputs[index];
            var dreamer = dreamers[index];
            GameDreamerEvent result;

            //UnityEngine.Debug.Log($"TDream: {entityIndices[index].value} : {entityArray[index].Index} : {status.value} : {(int)dreamer.status} : {(double)dreamer.time} : {(double)delay[index].time} : {frameIndex}");

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
                                
                                //UnityEngine.Debug.Log($"Dream: {entityIndices[index].value} : {entityArray[index].Index} : {statusOutputs[entity].value} : {(int)dreamer.status} : {(double)dreamer.time} : {frameIndex}");
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
                                
                                //UnityEngine.Debug.Log($"Dream: {entityIndices[index].value} : {entityArray[index].Index} : {statusOutputs[entity].value} : {(int)dreamer.status} : {(double)dreamer.time} : {frameIndex}");
                                
                                status.value = 0;
                                statusOutputs[entity] = status;

                                break;
                        }

                        if(isAwake)
                        {
#if GAME_DEBUG_COMPARSION
                            //UnityEngine.Debug.Log($"Dream: {entityIndices[index].value} : {entityArray[index].Index} : {status.value} : {(int)dreamer.status} : {(double)dreamer.time} : {(double)delay[index].time} : {frameIndex}");

                            stream.Begin(entityIndices[index].value);
                            stream.Assert(statusName, status.value);
                            stream.Assert(dreamerName, dreamer.status);
                            stream.Assert(dreamerTimeName, (double)dreamer.time);
                            stream.Assert(delayTimeName, (float)(GameDeadline.Max(delay[index].time, time) - dreamer.time));

                            stream.End();
#endif

                            var dreams = this.dreams[index];
                            var dreamerInfo = dreamerInfos[index];
                            if (dreamerInfo.currentIndex >= 0 && dreamerInfo.currentIndex < dreams.Length)
                            {
                                var dream = dreams[dreamerInfo.currentIndex];

                                var time = GameDeadline.Max(dreamer.time, (GameDeadline)this.time - dream.awakeTime);

                                half awakeTime = (half)dream.awakeTime;
                                dreamer.time = time + awakeTime;
                                dreamers[index] = dreamer;

                                GameNodeDelay delay;
                                delay.time = time;
                                delay.startTime = half.zero;
                                delay.endTime = awakeTime;
                                this.delay[index] = delay;
                            }

                            status.value = (int)GameDreamerStatus.Awake;
                            statusOutputs[entity] = status;

                            var version = versions[entity];
                            version.status = GameDreamerStatus.Awake;
                            version.index = dreamerInfo.currentIndex;
                            ++version.value;
                            versions[entity] = version;

                            result.status = version.status;
                            result.version = version.value;
                            result.index = version.index;
                            result.time = time;
                            result.dreamTime = time;

                            events[index].Add(result);

                            return;
                        }

                        //dreamer.time = time;

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
                                //UnityEngine.Debug.Log($"Dream: {entityIndices[index].value} : {entityArray[index].Index} : {statusOutputs[entity].value} : {(int)dreamer.status} : {(double)dreamer.time} : {frameIndex}");
                                isSleep = !this.delay[index].Check(time);
                                if(isSleep)
                                    dreamer.time = time;
                                break;
                            default:
                                isSleep = (status.value & (GameNodeStatus.STOP | GameNodeStatus.OVER)) == 0;
                                break;
                        }
                        
                        if (isSleep)
                        {
                            var dreams = this.dreams[index];
                            var dreamerInfo = dreamerInfos[index];
                            if (dreamerInfo.nextIndex >= 0 && dreamerInfo.nextIndex < dreams.Length)
                            {
#if GAME_DEBUG_COMPARSION
                                //UnityEngine.Debug.Log($"Dream: {entityIndices[index].value} : {entityArray[index].Index} : {status.value} : {(int)dreamer.status} : {(double)dreamer.time} : {frameIndex}");

                                stream.Begin(entityIndices[index].value);
                                stream.Assert(statusName, status.value);
                                stream.Assert(dreamerName, dreamer.status);
                                stream.Assert(dreamerTimeName, (double)dreamer.time);
                                stream.Assert(delayTimeName, (float)(GameDeadline.Max(this.delay[index].time, this.time) - dreamer.time));

                                stream.End();
#endif

                                //UnityEngine.Debug.Log($"Dream: {entityIndices[index].value} : {entityArray[index].Index} : {statusOutputs[entity].value} : {(int)dreamer.status} : {(double)dreamer.time} : {(double)this.delay[index].time} : {frameIndex}");

                                var dream = dreams[dreamerInfo.nextIndex];

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
                                version.status = GameDreamerStatus.Sleep;
                                version.index = dreamerInfo.currentIndex;
                                ++version.value;
                                versions[entity] = version;

                                result.status = version.status;
                                result.version = version.value;
                                result.index = version.index;
                                result.time = this.time;
                                result.dreamTime = time;

                                events[index].Add(result);

                                return;
                            }
                        }
                        
                        //UnityEngine.Debug.Log($"Dream: {entityIndices[index].value} : {entityArray[index].Index} : {statusOutputs[entity].value} : {(int)dreamer.status} : {(double)dreamer.time} : {frameIndex}");
                        
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
                                    //UnityEngine.Debug.Log($"Dream: {entityIndices[index].value} : {entityArray[index].Index} : {status.value} : {(int)dreamer.status} : {(double)dreamer.time} : {(double)delay[index].time} : {frameIndex}");

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
                                    version.status = GameDreamerStatus.Dream;
                                    version.index = dreamerInfos[index].currentIndex;
                                    ++version.value;
                                    versions[entity] = version;

                                    result.status = version.status;
                                    result.version = version.value;
                                    result.index = version.index;
                                    result.time = time;
                                    result.dreamTime = dreamer.time;

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
                                //UnityEngine.Debug.Log($"Dream: {entityIndices[index].value} : {entityArray[index].Index} : {statusOutputs[entity].value} : {(int)dreamer.status} : {(double)dreamer.time} : {frameIndex}");
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
                                    //UnityEngine.Debug.Log($"Dream: {entityIndices[index].value} : {entityArray[index].Index} : {status.value} : {(int)dreamer.status} : {(double)dreamer.time} : {(double)delay[index].time} : {frameIndex}");

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
                                    version.status = GameDreamerStatus.Awake;
                                    version.index = dreamerInfos[index].currentIndex;
                                    ++version.value;
                                    versions[entity] = version;

                                    result.status = version.status;
                                    result.version = version.value;
                                    result.index = version.index;
                                    result.time = time;
                                    result.dreamTime = dreamer.time;

                                    events[index].Add(result);

                                    return;
                                }
                                else
                                {
                                    //UnityEngine.Debug.Log($"Dream: {entityIndices[index].value} : {entityArray[index].Index} : {status.value} : {(int)dreamer.status} : {(double)dreamer.time} : {delay[index]} : {frameIndex}");
                                    status.value = (int)GameDreamerStatus.Dream;
                                }

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
                                        
                                        //UnityEngine.Debug.Log($"Dream: {entityIndices[index].value} : {entityArray[index].Index} : {statusOutputs[entity].value} : {(int)dreamer.status} : {(double)dreamer.time} : {frameIndex}");
                                    }
                                }
                                break;
                                //多次调用醒来
                            case GameDreamerStatus.Awake:
                                {
                                    //UnityEngine.Debug.Log($"Dream: {entityIndices[index].value} : {entityArray[index].Index} : {statusOutputs[entity].value} : {(int)dreamer.status} : {(double)dreamer.time} : {frameIndex}");
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
                    //UnityEngine.Debug.Log($"Dream: {entityIndices[index].value} : {entityArray[index].Index} : {status.value} : {(int)dreamer.status} : {(double)dreamer.time} : {(double)delay[index].time} : {frameIndex}");

                    stream.Begin(entityIndices[index].value);
                    stream.Assert(statusName, status.value);
                    stream.Assert(dreamerName, dreamer.status);
                    stream.Assert(dreamerTimeName, (double)dreamer.time);
                    stream.Assert(delayTimeName, (float)(GameDeadline.Max(delay[index].time, time) - dreamer.time));

                    stream.End();
#endif
                    var version = versions[entity];
                    version.status = dreamer.status;
                    version.index = dreamerInfos[index].currentIndex;
                    ++version.value;
                    versions[entity] = version;

                    result.status = version.status;// GameDreamerStatus.Normal;
                    result.version = version.value;
                    result.index = version.index;
                    result.time = time;
                    result.dreamTime = dreamer.time;

                    events[index].Add(result);

                    EntityCommandStructChange command;
                    command.componentType = ComponentType.ReadWrite<GameDreamer>();
                    command.entity = entity;
                    entityManager.Enqueue(command);
                }

                break;
            }

            //UnityEngine.Debug.Log($"Dream: {entityIndices[index].value} : {entityArray[index].Index} : {statusOutputs[entity].value} : {(int)dreamer.status} : {(double)dreamer.time} : {(double)delay[index].time} : {frameIndex}");
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

        public FixedString32Bytes statusName;
        public FixedString32Bytes dreamerName;

        public FixedString32Bytes dreamerTimeName;
        public FixedString32Bytes delayTimeName;

        public ComparisonStream<uint> stream;
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
            dreaming.entityIndices = chunk.GetNativeArray(ref entityIndexType);
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

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __group = builder
                .WithAll<GameDream>()
                .WithAllRW<GameDreamer, GameDreamerInfo>()
                .WithAllRW<GameDreamerVersion, GameNodeDelay>()
                .WithAllRW<GameNodeStatus>()
                .Build(ref state);
        
        //__syncDataGroup = state.GetEntityQuery(ComponentType.ReadOnly<GameSyncData>());
        __time = new GameRollbackTime(ref state);

        __endFrameBarrier = state.WorldUnmanaged.GetExistingSystemUnmanaged<EndFrameSyncSystemGroupStructChangeSystem>().manager.removeComponentPool;
    }

    [BurstCompile]
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
        uint frameIndex = __time.frameIndex;
        var streamScheduler = GameComparsionSystem.instance.Create(false, frameIndex, typeof(GameDreamerSystem).Name, state.World.Name);

        dreaming.frameIndex = frameIndex;
        dreaming.statusName = "status";
        dreaming.dreamerName = "dreamer";
        dreaming.dreamerTimeName = "dreamerTime";
        dreaming.delayTimeName = "delayTime";
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

[BurstCompile, CreateAfter(typeof(CallbackSystem)), UpdateInGroup(typeof(TimeSystemGroup)), UpdateAfter(typeof(GameSyncSystemGroup))]
public partial struct GameDreamEventSystem : ISystem
{
    [BurstCompile]
    private struct Resize : IJobChunk
    {
        [ReadOnly]
        public BufferTypeHandle<GameDreamerEvent> eventType;

        [NativeDisableParallelForRestriction]
        public NativeArray<int> functionCountAndSize;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            int functionCount = 0;
            var events = chunk.GetBufferAccessor(ref eventType);
            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                functionCount += events[i].Length;

            if (functionCount > 0)
            {
                functionCountAndSize.Add(0, functionCount);
                functionCountAndSize.Add(1, functionCount * UnsafeUtility.SizeOf<GameDreamerFunctionWrapper>());
            }
        }
    }
    
    private struct Invoke
    {
        public bool hasDream;
        public double animationTime;
        public GameTime time;
        
        [ReadOnly] 
        public NativeArray<EntityObject<GameDreamerComponent>> targets;
        public NativeArray<GameDreamerVersion> versions;
        public BufferAccessor<GameDreamerEvent> events;
        public SharedFunctionFactory.ParallelWriter functionFactory;

        public void Execute(int index)
        {
            GameDreamerFunctionWrapper functionWrapper;
            var version = versions[index];
            var events = this.events[index];
            int numEvents = events.Length;
            if (numEvents > 0)
            {
                functionWrapper.target = targets[index];
                
                int i;
                for (i = 0; i < numEvents; ++i)
                {
                    functionWrapper.result = events[i];
                    if (functionWrapper.result.version > version.value)
                        continue;

                    if (functionWrapper.result.time > animationTime)
                        break;

                    functionFactory.Invoke(ref functionWrapper);
                }

                events.RemoveRange(0, i);
            }

            switch (version.status)
            {
                case GameDreamerStatus.Dream:
                case GameDreamerStatus.Sleep:
                case GameDreamerStatus.Awake:
                    if (!hasDream && events.Length < 1)
                    {
                        functionWrapper.result.status = GameDreamerStatus.Normal;
                        functionWrapper.result.version = ++version.value;
                        functionWrapper.result.index = version.index;
                        functionWrapper.result.time = time;
                        functionWrapper.result.dreamTime = time;

                        events.Add(functionWrapper.result);

                        version.status = GameDreamerStatus.Normal;
                        versions[index] = version;
                    }
                    
                    break;
            }
        }
    }

    [BurstCompile]
    private struct InvokeEx : IJobChunk
    {
        public double animationTime;
        public GameTime time;

        [ReadOnly] 
        public ComponentTypeHandle<GameDreamer> dreamerType;

        [ReadOnly] 
        public ComponentTypeHandle<EntityObject<GameDreamerComponent>> targetType;
        
        public ComponentTypeHandle<GameDreamerVersion> versionType;

        public BufferTypeHandle<GameDreamerEvent> eventType;

        public SharedFunctionFactory.ParallelWriter functionFactory;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Invoke invoke;
            invoke.hasDream = chunk.Has(ref dreamerType);
            invoke.animationTime = animationTime;
            invoke.time = time;
            invoke.targets = chunk.GetNativeArray(ref targetType);
            invoke.versions = chunk.GetNativeArray(ref versionType);
            invoke.events = chunk.GetBufferAccessor(ref eventType);
            invoke.functionFactory = functionFactory;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                invoke.Execute(i);
        }
    }

    private EntityQuery __group;
    private EntityQuery __animationElapsedTimeGroup;
    private GameRollbackTime __time;
    
    private ComponentTypeHandle<GameDreamer> __dreamerType;

    private ComponentTypeHandle<EntityObject<GameDreamerComponent>> __targetType;
        
    private ComponentTypeHandle<GameDreamerVersion> __versionType;

    private BufferTypeHandle<GameDreamerEvent> __eventType;

    private SharedFunctionFactory __functionFactory;

    private NativeArray<int> __functionCountAndSize;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __animationElapsedTimeGroup = GameAnimationElapsedTime.GetEntityQuery(ref state);

        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __group = builder
                .WithAll<EntityObject<GameDreamerComponent>>()
                .WithAllRW<GameDreamerVersion, GameDreamerEvent>()
                .Build(ref state);
        
        __time = new GameRollbackTime(ref state);
        
        __dreamerType = state.GetComponentTypeHandle<GameDreamer>(true);
        __targetType = state.GetComponentTypeHandle<EntityObject<GameDreamerComponent>>(true);
        __versionType = state.GetComponentTypeHandle<GameDreamerVersion>();
        __eventType = state.GetBufferTypeHandle<GameDreamerEvent>();
        
        __functionFactory = state.WorldUnmanaged.GetExistingSystemUnmanaged<CallbackSystem>().functionFactory;

        __functionCountAndSize = new NativeArray<int>(2, Allocator.Persistent, NativeArrayOptions.ClearMemory);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        __functionCountAndSize.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var jobHandle = state.Dependency;
        var eventType = __eventType.UpdateAsRef(ref state);
        
        Resize resize;
        resize.eventType = eventType;
        resize.functionCountAndSize = __functionCountAndSize;
        jobHandle = resize.ScheduleParallelByRef(__group, jobHandle);

        ref var functionFactoryJobManager = ref __functionFactory.lookupJobManager;
        jobHandle = JobHandle.CombineDependencies(jobHandle, functionFactoryJobManager.readWriteJobHandle);
        
        InvokeEx invoke;
        invoke.time = __time.now;
        invoke.animationTime = __animationElapsedTimeGroup.GetSingleton<GameAnimationElapsedTime>().value;
        invoke.dreamerType = __dreamerType.UpdateAsRef(ref state);
        invoke.targetType = __targetType.UpdateAsRef(ref state);
        invoke.versionType = __versionType.UpdateAsRef(ref state);
        invoke.eventType = eventType;
        invoke.functionFactory = __functionFactory.AsParallelWriter(__functionCountAndSize, ref jobHandle);

        jobHandle = invoke.ScheduleParallelByRef(__group, jobHandle);
        
        functionFactoryJobManager.readWriteJobHandle = jobHandle;

        state.Dependency = jobHandle;
    }
}