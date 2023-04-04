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

[AutoCreateIn("Server"), UpdateInGroup(typeof(TimeSystemGroup)), UpdateBefore(typeof(StateMachineSchedulerGroup))/*, UpdateAfter(typeof(GameNodeEventSystem))*/]
public partial class GameTimeActorSystem : SystemBase
{
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

        public EntityCommandQueue<GameSpawnData>.ParallelWriter entityManager;
        
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
                                spawnData.transform = math.RigidTransform(transform.rot, math.transform(transform, action.spawnOffset));

                                entityManager.Enqueue(spawnData);
                            }
                            else
                            {
                                int version = versions[index].value;
                                if (version != commands[entity].version)
                                {
                                    command.version = version;
                                    command.index = action.index;
                                    command.time = time;
                                    command.entity = Entity.Null;
                                    command.forward = math.forward(transform.rot);
                                    command.distance = float3.zero;
                                    command.offset = float3.zero;

                                    commands[entity] = command;
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
    private struct ActEx : IJobChunk, IEntityCommandProducerJob
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

        public EntityCommandQueue<GameSpawnData>.ParallelWriter entityManager;

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
            act.entityManager = entityManager;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                act.Execute(i);
        }
    }

    private EntityQuery __group;
    private GameSyncTime __time;
    private EntityCommandPool<GameSpawnData> __entityManager;

    public void Create<T>(T instance) where T : GameSpawnCommander
    {
        ///Why EndFrameSyncSystemGroupEntityCommandSystem ???
        __entityManager = World.GetExistingSystemManaged<EndTimeSystemGroupEntityCommandSystem>().Create<GameSpawnData, T>(EntityCommandManager.QUEUE_PRESENT, instance);
    }

    public void Create<T>() where T : GameSpawnCommander, new()
    {
        ///Why EndFrameSyncSystemGroupEntityCommandSystem ???
        __entityManager = World.GetExistingSystemManaged<EndTimeSystemGroupEntityCommandSystem>().GetOrCreate<GameSpawnData, T>(EntityCommandManager.QUEUE_PRESENT);
    }

    protected override void OnCreate()
    {
        base.OnCreate();

        __group = GetEntityQuery(
            ComponentType.ReadOnly<GameNodeStatus>(),
            ComponentType.ReadOnly<GameTimeAction>(),
            ComponentType.ReadWrite<GameTimeActionElapsedTime>(),
            ComponentType.Exclude<Disabled>());

        __time = new GameSyncTime(ref this.GetState());
    }

    protected override void OnUpdate()
    {
        if (!__entityManager.isCreated)
            return;

        var entityMananger = __entityManager.Create();

        ActEx act;
        act.elapsedTime = __time.frameDelta;
        act.time = __time.nextTime;
        act.entityType = GetEntityTypeHandle();
        act.translationType = GetComponentTypeHandle<Translation>(true);
        act.rotationType = GetComponentTypeHandle<Rotation>(true);
        act.statusType = GetComponentTypeHandle<GameNodeStatus>(true);
        act.delayType = GetComponentTypeHandle<GameNodeDelay>(true);
        act.versionType = GetComponentTypeHandle<GameEntityCommandVersion>(true);
        act.factorType = GetComponentTypeHandle<GameTimeActionFactor>(true);
        act.actionType = GetBufferTypeHandle<GameTimeAction>(true);
        act.counterType = GetBufferTypeHandle<GameSpawnerAssetCounter>(true);
        act.elpasedTimeType = GetBufferTypeHandle<GameTimeActionElapsedTime>();
        act.commands = GetComponentLookup<GameEntityActionCommand>();
        act.entityManager = entityMananger.parallelWriter;

        var jobHandle = act.ScheduleParallel(__group, Dependency);

        entityMananger.AddJobHandleForProducer<ActEx>(jobHandle);

        Dependency = jobHandle;
    }
}