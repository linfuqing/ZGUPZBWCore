using Unity.Jobs;
using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using ZG;
using Unity.Burst;

[assembly: RegisterGenericJobType(typeof(StateMachineSchedulerJob<
    StateMachineScheduler, 
    StateMachineFactory<StateMachineScheduler>, 
    GameActionFollowSchedulerSystem.SchedulerEntry, 
    GameActionFollowSchedulerSystem.FactoryEntry>))]
[assembly: RegisterGenericJobType(typeof(StateMachineEscaperJob<StateMachineEscaper, StateMachineFactory<StateMachineEscaper>>))]
[assembly: RegisterGenericJobType(typeof(StateMachineExecutorJob<GameActionFollowExecutorSystem.Executor, GameActionFollowExecutorSystem.ExecutorFactory>))]

public class GameActionFollow : StateMachineNode
{
    [Tooltip("跟随的优先级")]
    public int priority = 3;
    [Tooltip("超过半径开始跟随")]
    public float radius = 8;
    [Tooltip("跟随之后小于该距离不跟随")]
    public float distance = 5;
    
    public override void Enable(StateMachineComponentEx instance)
    {
        GameActionFollowData data;
        data.priority = priority;
        data.radiusSq = radius * radius;
        data.distanceSq = distance * distance;
        instance.AddComponentData(data);
    }

    public override void Disable(StateMachineComponentEx instance)
    {
        instance.RemoveComponent<GameActionFollowData>();
    }
}

public struct GameActionFollowData : IComponentData
{
    [Tooltip("状态机优先级")]
    public int priority;

    public float radiusSq;
    public float distanceSq;
}

[BurstCompile, UpdateInGroup(typeof(StateMachineGroup), OrderLast = true), UpdateAfter(typeof(GameActionActiveSchedulerSystem))]
public partial struct GameActionFollowSchedulerSystem : ISystem
{
    public struct SchedulerEntry : IStateMachineScheduler
    {
        [ReadOnly]
        public ComponentLookup<Translation> translationMap;

        [ReadOnly]
        public NativeArray<Translation> translations;

        [ReadOnly]
        public NativeArray<GameActionFollowData> instances;

        [ReadOnly]
        public NativeArray<GameActorMaster> actorMasters;
        
        public bool Execute(
            int runningStatus,
            in SystemHandle runningSystemHandle,
            in SystemHandle nextSystemHandle,
            in SystemHandle currentSystemHandle,
            int index)
        {
            if (currentSystemHandle == runningSystemHandle)
                return false;

            var instance = instances[index];
            if (runningStatus >= instance.priority)
                return false;

            var actorMaster = actorMasters[index];
            if (!translationMap.HasComponent(actorMaster.entity))
                return false;

            if (math.distancesq(translationMap[actorMaster.entity].Value, translations[index].Value) < instance.radiusSq)
                return false;
            
            return true;
        }
    }

    public struct FactoryEntry : IStateMachineFactory<SchedulerEntry>
    {
        [ReadOnly]
        public ComponentLookup<Translation> translations;

        [ReadOnly]
        public ComponentTypeHandle<Translation> translationType;

        [ReadOnly]
        public ComponentTypeHandle<GameActionFollowData> instanceType;

        [ReadOnly]
        public ComponentTypeHandle<GameActorMaster> actorMasterType;
        
        public SchedulerEntry Create(
            int index, 
            in ArchetypeChunk chunk)
        {
            SchedulerEntry schedulerEntry;
            schedulerEntry.translationMap = translations;
            schedulerEntry.translations = chunk.GetNativeArray(ref translationType);
            schedulerEntry.instances = chunk.GetNativeArray(ref instanceType);
            schedulerEntry.actorMasters = chunk.GetNativeArray(ref actorMasterType);

            return schedulerEntry;
        }
    }

    private ComponentLookup<Translation> __translations;

    private ComponentTypeHandle<Translation> __translationType;

    private ComponentTypeHandle<GameActionFollowData> __instanceType;

    private ComponentTypeHandle<GameActorMaster> __actorMasterType;

    private StateMachineSchedulerSystemCore __core;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __translations = state.GetComponentLookup<Translation>(true);
        __translationType = state.GetComponentTypeHandle<Translation>(true);
        __instanceType = state.GetComponentTypeHandle<GameActionFollowData>(true);
        __actorMasterType = state.GetComponentTypeHandle<GameActorMaster>(true);

        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __core = new StateMachineSchedulerSystemCore(
                ref state,
                builder
                .WithAll<Translation, GameActionFollowData, GameActorMaster>());
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {

    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        FactoryEntry factoryEntry;
        factoryEntry.translations = __translations.UpdateAsRef(ref state);
        factoryEntry.translationType = __translationType.UpdateAsRef(ref state);
        factoryEntry.instanceType = __instanceType.UpdateAsRef(ref state);
        factoryEntry.actorMasterType = __actorMasterType.UpdateAsRef(ref state);

        StateMachineFactory<StateMachineScheduler> factoryExit;
        __core.Update<StateMachineScheduler, SchedulerEntry, StateMachineFactory<StateMachineScheduler>, FactoryEntry>(ref state, ref factoryEntry, ref factoryExit);
    }
}

[BurstCompile, CreateAfter(typeof(GameActionFollowSchedulerSystem)), UpdateInGroup(typeof(StateMachineGroup))]
public partial struct GameActionFollowExecutorSystem : ISystem
{
    public struct Executor : IStateMachineExecutor
    {
        public bool isHasPositions;

        public double time;

        public ArchetypeChunk chunk;

        public ComponentTypeHandle<GameNavMeshAgentTarget> targetType;

        [ReadOnly]
        public ComponentLookup<Translation> translationMap;

        //[ReadOnly]
        //public NativeArray<Entity> entityArray;

        [ReadOnly]
        public NativeArray<Translation> translations;
        
        [ReadOnly]
        public NativeArray<GameActorMaster> actorMasters;

        [ReadOnly]
        public NativeArray<GameActionFollowData> instances;
        
        public NativeArray<GameNavMeshAgentTarget> targets;

        //public EntityAddDataQueue.ParallelWriter entityManager;

        public int Execute(bool isEntry, int index)
        {
            GameActorMaster actorMaster = actorMasters[index];
            if (!translationMap.HasComponent(actorMaster.entity))
                return 0;
            
            float3 temp = translationMap[actorMaster.entity].Value;
            GameActionFollowData instance = instances[index];
            if (math.distancesq(translations[index].Value, temp) < instance.distanceSq)
                return 0;

            if (isHasPositions)
            {
                GameNavMeshAgentTarget target;
                target.sourceAreaMask = -1;
                target.destinationAreaMask = -1;
                target.position = temp;
                if (index < targets.Length)
                {
                    targets[index] = target;

                    chunk.SetComponentEnabled(ref targetType, index, true);
                }
                /*else
                    entityManager.AddComponentData(entityArray[index], target);*/
            }

            /*if (index < this.positions.Length)
            {
                DynamicBuffer<GameNodePosition> positions = this.positions[index];
                if (isEntry)
                    positions.Clear();
                else
                    isEntry = positions.Length < 1;

                if (isEntry)
                {
                    Entity entity = entityArray[index];
                    GameNodeVersion version = versions[entity];
                    version.type = GameNodeVersion.Type.Position;
                    ++version.value;
                    version.time = time;

                    if (index < velocities.Length)
                    {
                        var velocity = velocities[index];
                        if (!velocity.value.Equals(float3.zero))
                        {
                            velocity.mode = GameNodeVelocityDirect.Mode.None;
                            velocity.version = version.value;
                            velocity.value = float3.zero;

                            velocities[index] = velocity;

                            version.type |= GameNodeVersion.Type.Direction;
                        }
                    }

                    GameNodePosition position;
                    position.mode = GameNodePosition.Mode.Normal;
                    position.version = version.value;
                    //position.distance = float.MaxValue;
                    position.value = temp;
                    positions.Add(position);

                    versions[entity] = version;
                }
            }*/

            return instance.priority;
        }
    }

    public struct ExecutorFactory : IStateMachineFactory<Executor>
    {
        public double time;

        [ReadOnly]
        public ComponentLookup<Translation> translations;
        [ReadOnly]
        public BufferTypeHandle<GameNodePosition> positionType;
        [ReadOnly]
        public ComponentTypeHandle<Translation> translationType;
        [ReadOnly]
        public ComponentTypeHandle<GameActionFollowData> instanceType;
        [ReadOnly]
        public ComponentTypeHandle<GameActorMaster> actorMasterType;

        public ComponentTypeHandle<GameNavMeshAgentTarget> targetType;

        public Executor Create(
            int index, 
            in ArchetypeChunk chunk)
        {
            Executor executor;
            executor.isHasPositions = chunk.Has(ref positionType);
            executor.time = time;
            executor.chunk = chunk;
            executor.targetType = targetType;
            executor.translationMap = translations;
            //executor.entityArray = chunk.GetNativeArray(entityType);
            executor.translations = chunk.GetNativeArray(ref translationType);
            executor.instances = chunk.GetNativeArray(ref instanceType);
            executor.actorMasters = chunk.GetNativeArray(ref actorMasterType);
            executor.targets = chunk.GetNativeArray(ref targetType);
            //executor.entityManager = entityManager;

            return executor;
        }
    }

    private GameSyncTime __time;

    private ComponentLookup<Translation> __translations;
    private BufferTypeHandle<GameNodePosition> __positionType;
    private ComponentTypeHandle<Translation> __translationType;
    private ComponentTypeHandle<GameActionFollowData> __instanceType;
    private ComponentTypeHandle<GameActorMaster> __actorMasterType;

    private ComponentTypeHandle<GameNavMeshAgentTarget> __targetType;

    private StateMachineExecutorSystemCore __core;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __time = new GameSyncTime(ref state);

        __translations = state.GetComponentLookup<Translation>(true);
        __positionType = state.GetBufferTypeHandle<GameNodePosition>(true);
        __translationType = state.GetComponentTypeHandle<Translation>(true);
        __instanceType = state.GetComponentTypeHandle<GameActionFollowData>(true);
        __actorMasterType = state.GetComponentTypeHandle<GameActorMaster>(true);
        __targetType = state.GetComponentTypeHandle<GameNavMeshAgentTarget>();

        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __core = new StateMachineExecutorSystemCore(
                ref state,
                builder
                .WithAll<GameActionFollowData, GameActorMaster>(),
                state.WorldUnmanaged.GetExistingUnmanagedSystem<GameActionFollowSchedulerSystem>());
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {

    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        ExecutorFactory executorFactory;
        executorFactory.time = __time.nextTime;
        executorFactory.translations = __translations.UpdateAsRef(ref state);
        executorFactory.positionType = __positionType.UpdateAsRef(ref state);
        executorFactory.translationType = __translationType.UpdateAsRef(ref state);
        executorFactory.instanceType = __instanceType.UpdateAsRef(ref state);
        executorFactory.actorMasterType = __actorMasterType.UpdateAsRef(ref state);
        executorFactory.targetType = __targetType.UpdateAsRef(ref state);

        StateMachineFactory<StateMachineEscaper> escaperFactory;
        __core.Update<StateMachineEscaper, Executor, StateMachineFactory<StateMachineEscaper>, ExecutorFactory>(ref state, ref executorFactory, ref escaperFactory);
    }
}