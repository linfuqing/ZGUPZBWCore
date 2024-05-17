using System;
using Unity.Jobs;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Transforms;
using ZG;
using Random = Unity.Mathematics.Random;

[BurstCompile, AutoCreateIn("Server"), UpdateInGroup(typeof(TimeSystemGroup)), UpdateBefore(typeof(StateMachineGroup))/*, UpdateAfter(typeof(GameNodeEventSystem))*/]
public partial struct GameTimeActorSystem : ISystem
{
    [BurstCompile]
    private struct CountEx : IJobChunk
    {
        [NativeDisableParallelForRestriction]
        public NativeArray<int> counter;

        [ReadOnly]
        public BufferTypeHandle<GameTimeAction> actionType;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            var actions = chunk.GetBufferAccessor(ref actionType);

            int count = 0;
            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                count += actions[i].Length;

            counter.Add(0, count);
        }
    }

    [BurstCompile]
    public struct Recapcity : IJob
    {
        [ReadOnly, DeallocateOnJobCompletion]
        public NativeArray<int> counter;

        public NativeList<GameSpawnData> results;

        public void Execute()
        {
            results.Capacity = math.max(results.Capacity, results.Length + counter[0]);
        }
    }

    private struct Act
    {
        public float elapsedTime;
        public GameTime time;

        public Random random;

        [ReadOnly]
        public NativeArray<Entity> entityArray;

        [ReadOnly]
        public NativeArray<Translation> translations;

        [ReadOnly]
        public NativeArray<Rotation> rotations;

        [ReadOnly]
        public NativeArray<GameNodeStatus> states;

        [ReadOnly]
        public NativeArray<GameNodeDelay> delay;

        [ReadOnly]
        public NativeArray<GameEntityCommandVersion> versions;

        [ReadOnly]
        public NativeArray<GameTimeActionFactor> factors;

        [ReadOnly]
        public BufferAccessor<GameTimeAction> actions;

        [ReadOnly]
        public BufferAccessor<GameSpawnerAssetCounter> counters;

        public BufferAccessor<GameTimeActionElapsedTime> elpasedTimes;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<GameEntityActionCommand> commands;

        public SharedList<GameSpawnData>.ParallelWriter results;
        
        public void Execute(int index)
        {
            DynamicBuffer<GameTimeAction> actions = this.actions[index];
            DynamicBuffer<GameTimeActionElapsedTime> elpasedTimes = this.elpasedTimes[index];
            int length = math.min(actions.Length, elpasedTimes.Length);
            if (length < 1)
                return;

            if (states[index].value != 0)
                return;
            
            bool isBusy = index < delay.Length && delay[index].Check(time);
            int i, j, numCounters, count;
            float deltaTime = elapsedTime * factors[index].value;
            Entity entity = entityArray[index];
            RigidTransform transform = math.RigidTransform(rotations[index].Value, translations[index].Value);
            GameTimeAction action;
            GameTimeActionElapsedTime elpasedTime;
            GameEntityActionCommand command;
            GameSpawnData spawnData;
            GameSpawnerAssetCounter counter;
            DynamicBuffer<GameSpawnerAssetCounter> counters;

            if (index < this.counters.Length)
            {
                counters = this.counters[index];

                numCounters = counters.Length;
            }
            else
            {
                counters = default;

                numCounters = 0;
            }

            for (i = 0; i < length; ++i)
            {
                action = actions[i];
                if (action.index != -1 && isBusy)
                    continue;

                elpasedTime = elpasedTimes[i];
                elpasedTime.value += deltaTime;
                if (elpasedTime.value > action.time && (action.index != -1 || !isBusy))
                {
                    if (action.assetIndex == -1)
                        count = 1;
                    else
                    {
                        count = 0;

                        for (j = 0; j < numCounters; ++j)
                        {
                            counter = counters[j];
                            if (counter.assetIndex == action.assetIndex)
                            {
                                count = counter.value;

                                break;
                            }
                        }

                        if (j == numCounters)
                            count = 1;
                    }

                    if (count > 0)
                    {
                        if (action.chance > random.NextFloat())
                        {
                            if (action.index == -1)
                            {
                                spawnData.assetIndex = action.assetIndex;
                                //spawnData.time = time;
                                spawnData.entity = entity;
                                spawnData.velocity = float3.zero;
                                spawnData.transform = math.RigidTransform(transform.rot, math.transform(transform, action.spawnOffset));
                                spawnData.itemHandle = GameItemHandle.Empty;

                                results.AddNoResize(spawnData);
                            }
                            else
                            {
                                int version = versions[index].value;
                                if (version != commands[entity].version)
                                {
                                    command.version = version;
                                    command.index = action.index;
                                    //command.time = time;
                                    command.entity = Entity.Null;
                                    command.forward = math.forward(transform.rot);
                                    command.distance = float3.zero;
                                    //command.offset = float3.zero;

                                    commands[entity] = command;
                                    commands.SetComponentEnabled(entity, true);
                                }
                            }
                        }

                        elpasedTime.value = 0.0f;
                    }
                }

                elpasedTimes[i] = elpasedTime;
            }
        }
    }

    [BurstCompile]
    private struct ActEx : IJobChunk//, IEntityCommandProducerJob
    {
        public float elapsedTime;
        public GameTime time;
        
        [ReadOnly]
        public EntityTypeHandle entityType;
        [ReadOnly]
        public ComponentTypeHandle<Translation> translationType;
        [ReadOnly]
        public ComponentTypeHandle<Rotation> rotationType;
        [ReadOnly]
        public ComponentTypeHandle<GameNodeStatus> statusType;
        [ReadOnly]
        public ComponentTypeHandle<GameNodeDelay> delayType;
        [ReadOnly]
        public ComponentTypeHandle<GameEntityCommandVersion> versionType;
        [ReadOnly]
        public ComponentTypeHandle<GameTimeActionFactor> factorType;
        [ReadOnly]
        public BufferTypeHandle<GameTimeAction> actionType;
        [ReadOnly]
        public BufferTypeHandle<GameSpawnerAssetCounter> counterType;

        public BufferTypeHandle<GameTimeActionElapsedTime> elpasedTimeType;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<GameEntityActionCommand> commands;

        public SharedList<GameSpawnData>.ParallelWriter results;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Act act;
            act.elapsedTime = elapsedTime;
            act.time = time;
            long hash = math.aslong(time);
            act.random = new Random((uint)((int)(hash >> 32) ^ ((int)hash) ^ unfilteredChunkIndex));
            act.entityArray = chunk.GetNativeArray(entityType);
            act.translations = chunk.GetNativeArray(ref translationType);
            act.rotations = chunk.GetNativeArray(ref rotationType);
            act.states = chunk.GetNativeArray(ref statusType);
            act.delay = chunk.GetNativeArray(ref delayType);
            act.versions = chunk.GetNativeArray(ref versionType);
            act.factors = chunk.GetNativeArray(ref factorType);
            act.actions = chunk.GetBufferAccessor(ref actionType);
            act.counters = chunk.GetBufferAccessor(ref counterType);
            act.elpasedTimes = chunk.GetBufferAccessor(ref elpasedTimeType);
            act.commands = commands;
            act.results = results;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                act.Execute(i);
        }
    }

    private EntityQuery __group;

    private EntityTypeHandle __entityType;
    private ComponentTypeHandle<Translation> __translationType;
    private ComponentTypeHandle<Rotation> __rotationType;
    private ComponentTypeHandle<GameNodeStatus> __statusType;
    private ComponentTypeHandle<GameNodeDelay> __delayType;
    private ComponentTypeHandle<GameEntityCommandVersion> __versionType;
    private ComponentTypeHandle<GameTimeActionFactor> __factorType;
    private BufferTypeHandle<GameTimeAction> __actionType;
    private BufferTypeHandle<GameSpawnerAssetCounter> __counterType;

    private BufferTypeHandle<GameTimeActionElapsedTime> __elpasedTimeType;
    private ComponentLookup<GameEntityActionCommand> __commands;

    private GameSyncTime __time;

    /*private EntityCommandPool<GameSpawnData> __entityManager;

    public void Create<T>(T instance) where T : GameSpawnCommander
    {
        ///Why EndFrameSyncSystemGroupEntityCommandSystem ???
        __entityManager = World.GetExistingSystemManaged<EndTimeSystemGroupEntityCommandSystem>().Create<GameSpawnData, T>(EntityCommandManager.QUEUE_PRESENT, instance);
    }

    public void Create<T>() where T : GameSpawnCommander, new()
    {
        ///Why EndFrameSyncSystemGroupEntityCommandSystem ???
        __entityManager = World.GetExistingSystemManaged<EndTimeSystemGroupEntityCommandSystem>().GetOrCreate<GameSpawnData, T>(EntityCommandManager.QUEUE_PRESENT);
    }*/

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __group = builder
                    .WithAll<GameNodeStatus, GameTimeAction>()
                    .WithAllRW<GameTimeActionElapsedTime>()
                    .Build(ref state);

        __entityType = state.GetEntityTypeHandle();
        __translationType = state.GetComponentTypeHandle<Translation>(true);
        __rotationType = state.GetComponentTypeHandle<Rotation>(true);
        __statusType = state.GetComponentTypeHandle<GameNodeStatus>(true);
        __delayType = state.GetComponentTypeHandle<GameNodeDelay>(true);
        __versionType = state.GetComponentTypeHandle<GameEntityCommandVersion>(true);
        __factorType = state.GetComponentTypeHandle<GameTimeActionFactor>(true);
        __actionType = state.GetBufferTypeHandle<GameTimeAction>(true);
        __counterType = state.GetBufferTypeHandle<GameSpawnerAssetCounter>(true);
        __elpasedTimeType = state.GetBufferTypeHandle<GameTimeActionElapsedTime>();
        __commands = state.GetComponentLookup<GameEntityActionCommand>();

        __time = new GameSyncTime(ref state);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {

    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.HasSingleton<GameRandomSpawnerFactory>())
            return;

        var actionType = __actionType.UpdateAsRef(ref state);

        var counter = new NativeArray<int>(1, Allocator.TempJob, NativeArrayOptions.ClearMemory);

        CountEx count;
        count.counter = counter;
        count.actionType = actionType;
        var jobHandle = count.ScheduleParallelByRef(__group, state.Dependency);

        var commands = SystemAPI.GetSingleton<GameRandomSpawnerFactory>().commands;
        ref var commandsJobManager = ref commands.lookupJobManager;

        Recapcity recapcity;
        recapcity.counter = counter;
        recapcity.results = commands.writer;
        jobHandle = recapcity.ScheduleByRef(JobHandle.CombineDependencies(jobHandle, commandsJobManager.readWriteJobHandle));

        ActEx act;
        act.elapsedTime = __time.frameDelta;
        act.time = __time.nextTime;
        act.entityType = __entityType.UpdateAsRef(ref state);
        act.translationType = __translationType.UpdateAsRef(ref state);
        act.rotationType = __rotationType.UpdateAsRef(ref state);
        act.statusType = __statusType.UpdateAsRef(ref state);
        act.delayType = __delayType.UpdateAsRef(ref state);
        act.versionType = __versionType.UpdateAsRef(ref state);
        act.factorType = __factorType.UpdateAsRef(ref state);
        act.actionType = actionType;
        act.counterType = __counterType.UpdateAsRef(ref state);
        act.elpasedTimeType = __elpasedTimeType.UpdateAsRef(ref state);
        act.commands = __commands.UpdateAsRef(ref state);
        act.results = commands.parallelWriter;

        jobHandle = act.ScheduleParallelByRef(__group, jobHandle);

        //entityMananger.AddJobHandleForProducer<ActEx>(jobHandle);

        commandsJobManager.readWriteJobHandle = jobHandle;

        state.Dependency = jobHandle;
    }
}