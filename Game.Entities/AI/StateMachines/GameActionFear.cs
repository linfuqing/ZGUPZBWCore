using System;
using Unity.Jobs;
using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using ZG;
using System.Collections.Generic;

[assembly: RegisterGenericComponentType(typeof(GameActionFearSchedulerSystem.StateMachine))]
[assembly: RegisterGenericJobType(typeof(StateMachineExit<GameActionFearSchedulerSystem.StateMachine, StateMachineScheduler, StateMachineFactory<StateMachineScheduler>>))]
[assembly: RegisterGenericJobType(typeof(StateMachineEntry<GameActionFearSchedulerSystem.SchedulerEntry, GameActionFearSchedulerSystem.FactoryEntry>))]
[assembly: RegisterGenericJobType(typeof(StateMachineEscaper<GameActionFearSchedulerSystem.StateMachine, GameActionFearExecutorSystem.Escaper, GameActionFearExecutorSystem.EscaperFactory>))]
[assembly: RegisterGenericJobType(typeof(StateMachineExecutor<GameActionFearSchedulerSystem.StateMachine, GameActionFearExecutorSystem.Executor, GameActionFearExecutorSystem.ExecutorFactory>))]

public class GameActionFear : StateMachineNode
{
    public int auraFlag = 1;

    [Tooltip("状态机优先级")]
    public int priority = 1;

    [Tooltip("逃跑距离")]
    public float distance = 5.0f;

    public override void Enable(StateMachineComponentEx instance)
    {
        GameActionFearData data;
        data.auraFlag = auraFlag;
        data.priority = priority;
        data.distance = distance;
        instance.AddComponentData(data);

        instance.AddComponent<GameActionFearInfo>();
    }

    public override void Disable(StateMachineComponentEx instance)
    {
        instance.RemoveComponent<GameActionFearData>();
        instance.RemoveComponent<GameActionFearInfo>();
    }
}

[Serializable]
public struct GameActionFearData : IComponentData
{
    public int auraFlag; 
    public int priority;
    public float distance;
}

[Serializable]
public struct GameActionFearInfo : IComponentData
{
    public float3 position;
}

[UpdateBefore(typeof(GameActionActiveSchedulerSystem)), UpdateAfter(typeof(GameActionNormalSchedulerSystem))]
public partial class GameActionFearSchedulerSystem : StateMachineSchedulerSystem<
    GameActionFearSchedulerSystem.SchedulerEntry,
    GameActionFearSchedulerSystem.FactoryEntry,
    GameActionFearSchedulerSystem>
{
    public struct SchedulerEntry : IStateMachineScheduler
    {
        [ReadOnly]
        public BufferAccessor<GameAuraOrigin> auraOrigins;

        [ReadOnly]
        public NativeArray<Translation> translations;

        [ReadOnly]
        public NativeArray<GameActionFearData> instances;

        public NativeArray<GameActionFearInfo> infos;

        public bool Execute(
            int runningStatus,
            int runningSystemIndex,
            int nextSystemIndex,
            int currentSystemIndex,
            int index,
            in Entity entity)
        {
            var instance = instances[index];
            if (runningStatus >= instance.priority)
                return false;

            var auraOrigins = this.auraOrigins[index];
            GameAuraOrigin auraOrigin;
            GameActionFearInfo info;
            int numAuraOrigins = auraOrigins.Length;
            for(int i = 0; i < numAuraOrigins; ++i)
            {
                auraOrigin = auraOrigins[i];
                if ((auraOrigin.flag & instance.auraFlag) == 0)
                    continue;

                info.position = translations[index].Value;
                infos[index] = info;

                return true;
            }

            return false;
        }
    }

    public struct FactoryEntry : IStateMachineFactory<SchedulerEntry>
    {
        [ReadOnly]
        public BufferTypeHandle<GameAuraOrigin> auraOriginType;

        [ReadOnly]
        public ComponentTypeHandle<Translation> translationType;

        [ReadOnly]
        public ComponentTypeHandle<GameActionFearData> instanceType;

        public ComponentTypeHandle<GameActionFearInfo> infoType;

        public bool Create(
            int index, 
            in ArchetypeChunk chunk,
            out SchedulerEntry schedulerEntry)
        {
            schedulerEntry.auraOrigins = chunk.GetBufferAccessor(ref auraOriginType);
            schedulerEntry.translations = chunk.GetNativeArray(ref translationType);
            schedulerEntry.instances = chunk.GetNativeArray(ref instanceType);
            schedulerEntry.infos = chunk.GetNativeArray(ref infoType);

            return true;
        }
    }

    public override IEnumerable<EntityQueryDesc> entryEntityArchetypeQueries => __entryEntityArchetypeQueries;

    protected override FactoryEntry _GetEntry(ref JobHandle inputDeps)
    {
        FactoryEntry factoryEntry;
        factoryEntry.auraOriginType = GetBufferTypeHandle<GameAuraOrigin>(true);
        factoryEntry.translationType = GetComponentTypeHandle<Translation>(true);
        factoryEntry.instanceType = GetComponentTypeHandle<GameActionFearData>(true);
        factoryEntry.infoType = GetComponentTypeHandle<GameActionFearInfo>();

        return factoryEntry;
    }

    private readonly EntityQueryDesc[] __entryEntityArchetypeQueries = new EntityQueryDesc[]
    {
        new EntityQueryDesc()
        {
            All = new ComponentType[]
            {
                ComponentType.ReadOnly<GameActionFearData>(),
                ComponentType.ReadOnly<GameAuraOrigin>(),
            },
            None = new ComponentType[]
            {
                typeof(Disabled)
            }
        }
    };
}

public partial class GameActionFearExecutorSystem : GameActionFearSchedulerSystem.StateMachineExecutorSystem<
    GameActionFearExecutorSystem.Escaper,
    GameActionFearExecutorSystem.Executor,
    GameActionFearExecutorSystem.EscaperFactory,
    GameActionFearExecutorSystem.ExecutorFactory>, IEntityCommandProducerJob
{
    public struct Escaper : IStateMachineEscaper
    {
        public ArchetypeChunk chunk;

        public ComponentTypeHandle<GameNavMeshAgentTarget> targetType;

        //[ReadOnly]
        //public NativeArray<Entity> entityArray;

        [ReadOnly]
        public NativeArray<GameActionFearInfo> infos;

        public NativeArray<GameNavMeshAgentTarget> targets;

        //public EntityAddDataQueue.ParallelWriter entityManager;

        public bool Execute(
            int runningStatus,
            int runningSystemIndex,
            int nextSystemIndex,
            int currentSystemIndex,
            int index)
        {
            if (nextSystemIndex != -1 && index < infos.Length)
            {
                GameNavMeshAgentTarget target;
                target.sourceAreaMask = -1;
                target.destinationAreaMask = -1;
                target.position = infos[index].position;
                if (index < targets.Length)
                {
                    targets[index] = target;

                    chunk.SetComponentEnabled(ref targetType, index, true);
                }
                /*else
                    entityManager.AddComponentData(entityArray[index], target);*/
            }

            return true;
        }
    }

    public struct EscaperFactory : IStateMachineFactory<Escaper>
    {
        //[ReadOnly]
        //public EntityTypeHandle entityType;

        [ReadOnly]
        public ComponentTypeHandle<GameActionFearInfo> infoType;

        public ComponentTypeHandle<GameNavMeshAgentTarget> targetType;

        //public EntityAddDataQueue.ParallelWriter entityManager;

        public bool Create(
            int index, 
            in ArchetypeChunk chunk,
            out Escaper escaper)
        {
            escaper.chunk = chunk;
            escaper.targetType = targetType;
            //escaper.entityArray = chunk.GetNativeArray(entityType);
            escaper.infos = chunk.GetNativeArray(ref infoType);
            escaper.targets = chunk.GetNativeArray(ref targetType);
            //escaper.entityManager = entityManager;

            return true;
        }
    }

    public struct Executor : IStateMachineExecutor
    {
        public ArchetypeChunk chunk;

        public ComponentTypeHandle<GameNavMeshAgentTarget> targetType;

        [ReadOnly]
        public BufferAccessor<GameAuraOrigin> auraOrigins;

        [ReadOnly]
        public ComponentLookup<Translation> translationMap;

        [ReadOnly]
        public NativeArray<Translation> translations;

        //[ReadOnly]
        //public NativeArray<Entity> entityArray;

        [ReadOnly]
        public NativeArray<GameActionFearData> instances;

        public NativeArray<GameNavMeshAgentTarget> targets;

        //public EntityAddDataQueue.ParallelWriter entityManager;

        public unsafe int Execute(bool isEntry, int index)
        {
            var instance = instances[index];
            var auraOrigins = this.auraOrigins[index];
            GameAuraOrigin auraOrigin;
            float3 position = translations[index].Value, direction = float3.zero;
            int numAuraOrigins = auraOrigins.Length;
            bool isFind = false;
            for (int i = 0; i < numAuraOrigins; ++i)
            {
                auraOrigin = auraOrigins[i];
                if ((auraOrigin.flag & instance.auraFlag) == 0)
                    continue;

                isFind = true;

                direction += position - translationMap[auraOrigin.entity].Value;
            }

            if (!isFind)
                return 0;

            GameNavMeshAgentTarget target;
            target.sourceAreaMask = -1;
            target.destinationAreaMask = -1;
            target.position = position + math.normalizesafe(direction) * instance.distance;
            if (index < targets.Length)
            {
                targets[index] = target;

                chunk.SetComponentEnabled(ref targetType, index, true);
            }
            /*else
                entityManager.AddComponentData(entityArray[index], target);*/

            return instance.priority;
        }
    }

    public struct ExecutorFactory : IStateMachineFactory<Executor>
    {
        [ReadOnly]
        public ComponentLookup<Translation> translations;
        [ReadOnly]
        public BufferTypeHandle<GameAuraOrigin> auraOriginType;
        //[ReadOnly]
        //public EntityTypeHandle entityType;
        [ReadOnly]
        public ComponentTypeHandle<Translation> translationType;
        [ReadOnly]
        public ComponentTypeHandle<GameActionFearData> instanceType;
        public ComponentTypeHandle<GameNavMeshAgentTarget> targetType;

        //public EntityAddDataQueue.ParallelWriter entityManager;

        public bool Create(
            int index, 
            in ArchetypeChunk chunk,
            out Executor executor)
        {
            executor.chunk = chunk;
            executor.targetType = targetType;
            executor.translationMap = translations;
            executor.auraOrigins = chunk.GetBufferAccessor(ref auraOriginType);
            //executor.entityArray = chunk.GetNativeArray(entityType);
            executor.translations = chunk.GetNativeArray(ref translationType);
            executor.instances = chunk.GetNativeArray(ref instanceType);
            executor.targets = chunk.GetNativeArray(ref targetType);
            //executor.entityManager = entityManager;

            return true;
        }
    }

    //private EntityAddDataPool __endFrameBarrier;
    //private EntityAddDataQueue.ParallelWriter __entityManager;

    public override IEnumerable<EntityQueryDesc> runEntityArchetypeQueries => __runEntityArchetypeQueries;

    protected override void OnCreate()
    {
        base.OnCreate();

        //__endFrameBarrier = World.GetOrCreateSystemUnmanaged<GameActionStructChangeSystem>().addDataCommander;
    }

    protected override void OnUpdate()
    {
        //var entityManager = __endFrameBarrier.Create();

        //__entityManager = entityManager.AsComponentParallelWriter<GameNavMeshAgentTarget>(exitGroup.CalculateEntityCount() + runGroup.CalculateEntityCount());

        base.OnUpdate();

        //entityManager.AddJobHandleForProducer<GameActionFearExecutorSystem>(Dependency);
    }

    protected override EscaperFactory _GetExit(ref JobHandle inputDeps)
    {
        EscaperFactory escaperFactory;
        //escaperFactory.entityType = GetEntityTypeHandle();
        escaperFactory.infoType = GetComponentTypeHandle<GameActionFearInfo>(true);
        escaperFactory.targetType = GetComponentTypeHandle<GameNavMeshAgentTarget>();
        //escaperFactory.entityManager = __entityManager;
        return escaperFactory;
    }

    protected override ExecutorFactory _GetRun(ref JobHandle inputDeps)
    {
        ExecutorFactory executorFactory;
        executorFactory.translations = GetComponentLookup<Translation>(true);
        executorFactory.auraOriginType = GetBufferTypeHandle<GameAuraOrigin>(true);
        //executorFactory.entityType = GetEntityTypeHandle();
        executorFactory.translationType = GetComponentTypeHandle<Translation>(true);
        executorFactory.instanceType = GetComponentTypeHandle<GameActionFearData>(true);
        executorFactory.targetType = GetComponentTypeHandle<GameNavMeshAgentTarget>();
        //executorFactory.entityManager = __entityManager;
        return executorFactory;
    }

    private readonly EntityQueryDesc[] __runEntityArchetypeQueries = new EntityQueryDesc[]
    {
        new EntityQueryDesc()
        {
            All = new ComponentType[]
            {
                ComponentType.ReadOnly<GameAuraOrigin>(),
                ComponentType.ReadOnly<GameActionFearData>()
            },
            None = new ComponentType[]
            {
                typeof(Disabled)
            }
        }
    };
}