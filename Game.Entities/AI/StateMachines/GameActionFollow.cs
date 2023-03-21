using System;
using System.Collections.Generic;
using Unity.Jobs;
using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using ZG;

[assembly: RegisterGenericComponentType(typeof(GameActionFollowSchedulerSystem.StateMachine))]
[assembly: RegisterGenericJobType(typeof(StateMachineExit<GameActionFollowSchedulerSystem.StateMachine, StateMachineScheduler, StateMachineFactory<StateMachineScheduler>>))]
[assembly: RegisterGenericJobType(typeof(StateMachineEntry<GameActionFollowSchedulerSystem.SchedulerEntry, GameActionFollowSchedulerSystem.FactoryEntry>))]
[assembly: RegisterGenericJobType(typeof(StateMachineEscaper<GameActionFollowSchedulerSystem.StateMachine, StateMachineEscaper, StateMachineFactory<StateMachineEscaper>>))]
[assembly: RegisterGenericJobType(typeof(StateMachineExecutor<GameActionFollowSchedulerSystem.StateMachine, GameActionFollowExecutorSystem.Executor, GameActionFollowExecutorSystem.ExecutorFactory>))]

public class GameActionFollow : StateMachineNode
{
    public int priority = 3;
    public float radius = 8;
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

[Serializable]
public struct GameActionFollowData : IComponentData
{
    [Tooltip("状态机优先级")]
    public int priority;

    public float radiusSq;
    public float distanceSq;
}

[UpdateAfter(typeof(GameActionActiveSchedulerSystem))]
public partial class GameActionFollowSchedulerSystem : StateMachineSchedulerSystem<
    GameActionFollowSchedulerSystem.SchedulerEntry, 
    GameActionFollowSchedulerSystem.FactoryEntry, 
    GameActionFollowSchedulerSystem>
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
            int runningSystemIndex,
            int nextSystemIndex,
            int currentSystemIndex,
            int index,
            in Entity entity)
        {
            if (currentSystemIndex == runningSystemIndex)
                return false;

            GameActionFollowData instance = instances[index];
            if (runningStatus >= instance.priority)
                return false;

            GameActorMaster actorMaster = actorMasters[index];
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
        
        public bool Create(
            int index, 
            in ArchetypeChunk chunk,
            out SchedulerEntry schedulerEntry)
        {
            schedulerEntry.translationMap = translations;
            schedulerEntry.translations = chunk.GetNativeArray(ref translationType);
            schedulerEntry.instances = chunk.GetNativeArray(ref instanceType);
            schedulerEntry.actorMasters = chunk.GetNativeArray(ref actorMasterType);

            return true;
        }
    }

    public override IEnumerable<EntityQueryDesc> entryEntityArchetypeQueries => __entryEntityArchetypeQueries;

    protected override FactoryEntry _GetEntry(ref JobHandle inputDeps)
    {
        FactoryEntry factoryEntry;
        factoryEntry.translations = GetComponentLookup<Translation>(true);
        factoryEntry.translationType = GetComponentTypeHandle<Translation>(true);
        factoryEntry.instanceType = GetComponentTypeHandle<GameActionFollowData>(true);
        factoryEntry.actorMasterType = GetComponentTypeHandle<GameActorMaster>(true);
        return factoryEntry;
    }

    private readonly EntityQueryDesc[] __entryEntityArchetypeQueries = new EntityQueryDesc[]
    {
        new EntityQueryDesc()
        {
            All = new ComponentType[]
            {
                ComponentType.ReadOnly<Translation>(),
                ComponentType.ReadOnly<GameActionFollowData>(),
                ComponentType.ReadOnly<GameActorMaster>()
            }, 
            None = new ComponentType[]
            {
                typeof(Disabled)
            }
        }
    };
}

//[UpdateInGroup(typeof(StateMachineExecutorGroup))]
public partial class GameActionFollowExecutorSystem : GameActionFollowSchedulerSystem.StateMachineExecutorSystem<
    GameActionFollowExecutorSystem.Executor, 
    GameActionFollowExecutorSystem.ExecutorFactory>, IEntityCommandProducerJob
{
    public struct Executor : IStateMachineExecutor
    {
        public bool isHasPositions;

        public double time;

        [ReadOnly]
        public ComponentLookup<Translation> translationMap;

        [ReadOnly]
        public NativeArray<Entity> entityArray;

        [ReadOnly]
        public NativeArray<Translation> translations;
        
        [ReadOnly]
        public NativeArray<GameActorMaster> actorMasters;

        [ReadOnly]
        public NativeArray<GameActionFollowData> instances;
        
        public NativeArray<GameNavMeshAgentTarget> targets;

        public EntityAddDataQueue.ParallelWriter entityManager;

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
                    targets[index] = target;
                else
                    entityManager.AddComponentData(entityArray[index], target);
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
        public EntityTypeHandle entityType;
        [ReadOnly]
        public ComponentTypeHandle<Translation> translationType;
        [ReadOnly]
        public ComponentTypeHandle<GameActionFollowData> instanceType;
        [ReadOnly]
        public ComponentTypeHandle<GameActorMaster> actorMasterType;

        public ComponentTypeHandle<GameNavMeshAgentTarget> targetType;

        public EntityAddDataQueue.ParallelWriter entityManager;

        public bool Create(
            int index, 
            in ArchetypeChunk chunk,
            out Executor executor)
        {
            executor.isHasPositions = chunk.Has(ref positionType);
            executor.time = time;
            executor.translationMap = translations;
            executor.entityArray = chunk.GetNativeArray(entityType);
            executor.translations = chunk.GetNativeArray(ref translationType);
            executor.instances = chunk.GetNativeArray(ref instanceType);
            executor.actorMasters = chunk.GetNativeArray(ref actorMasterType);
            executor.targets = chunk.GetNativeArray(ref targetType);
            executor.entityManager = entityManager;

            return true;
        }
    }

    private GameSyncTime __time;
    private EntityAddDataPool __endFrameBarrier;
    private EntityAddDataQueue __entityManager;

    public override IEnumerable<EntityQueryDesc> runEntityArchetypeQueries => __runEntityArchetypeQueries;

    protected override void OnCreate()
    {
        base.OnCreate();

        __time = new GameSyncTime(ref this.GetState());

        var world = World;
        __endFrameBarrier = world.GetOrCreateSystemUnmanaged<GameActionStructChangeSystem>().addDataCommander;
    }

    protected override void OnUpdate()
    {
        __entityManager = __endFrameBarrier.Create();

        base.OnUpdate();

        __entityManager.AddJobHandleForProducer<GameActionFollowExecutorSystem>(Dependency);
    }

    protected override ExecutorFactory _GetRun(ref JobHandle inputDeps)
    {
        ExecutorFactory executorFactory;
        executorFactory.time = __time.nextTime;
        executorFactory.translations = GetComponentLookup<Translation>(true);
        executorFactory.positionType = GetBufferTypeHandle<GameNodePosition>(true);
        executorFactory.entityType = GetEntityTypeHandle();
        executorFactory.translationType = GetComponentTypeHandle<Translation>(true);
        executorFactory.instanceType = GetComponentTypeHandle<GameActionFollowData>(true);
        executorFactory.actorMasterType = GetComponentTypeHandle<GameActorMaster>(true);
        executorFactory.targetType = GetComponentTypeHandle<GameNavMeshAgentTarget>();
        executorFactory.entityManager = __entityManager.AsComponentParallelWriter<GameNavMeshAgentTarget>(runGroup.CalculateEntityCount());

        return executorFactory;
    }
    
    private readonly EntityQueryDesc[] __runEntityArchetypeQueries = new EntityQueryDesc[]
    {
        new EntityQueryDesc()
        {
            All = new ComponentType[]
            {
                ComponentType.ReadOnly<GameActionFollowData>(),
                ComponentType.ReadOnly<GameActorMaster>(),
            }, 
            None = new ComponentType[]
            {
                typeof(Disabled)
            }
        }
    };
}