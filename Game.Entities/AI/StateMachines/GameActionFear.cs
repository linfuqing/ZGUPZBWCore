using System;
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
    GameActionFearSchedulerSystem.SchedulerEntry, 
    GameActionFearSchedulerSystem.FactoryEntry>))]
[assembly: RegisterGenericJobType(typeof(StateMachineEscaperJob<GameActionFearExecutorSystem.Escaper, GameActionFearExecutorSystem.EscaperFactory>))]
[assembly: RegisterGenericJobType(typeof(StateMachineExecutorJob<GameActionFearExecutorSystem.Executor, GameActionFearExecutorSystem.ExecutorFactory>))]

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

public struct GameActionFearData : IComponentData
{
    public int auraFlag; 
    public int priority;
    public float distance;
}

public struct GameActionFearInfo : IComponentData
{
    public float3 position;
}

[BurstCompile, UpdateInGroup(typeof(StateMachineGroup), OrderLast = true), UpdateBefore(typeof(GameActionActiveSchedulerSystem)), UpdateAfter(typeof(GameActionNormalSchedulerSystem))]
public partial struct GameActionFearSchedulerSystem : ISystem
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
            in SystemHandle runningSystemHandle,
            in SystemHandle nextSystemHandle,
            in SystemHandle currentSystemHandle,
            int index)
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
            int unfilteredChunkIndex, 
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

    private BufferTypeHandle<GameAuraOrigin> __auraOriginType;

    private ComponentTypeHandle<Translation> __translationType;

    private ComponentTypeHandle<GameActionFearData> __instanceType;

    private ComponentTypeHandle<GameActionFearInfo> __infoType;

    private StateMachineSchedulerSystemCore __core;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __auraOriginType = state.GetBufferTypeHandle<GameAuraOrigin>(true);
        __translationType = state.GetComponentTypeHandle<Translation>(true);
        __instanceType = state.GetComponentTypeHandle<GameActionFearData>(true);
        __infoType = state.GetComponentTypeHandle<GameActionFearInfo>();

        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __core = new StateMachineSchedulerSystemCore(
                ref state,
                builder
                .WithAll<GameActionFearData, GameAuraOrigin>());
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {

    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        FactoryEntry factoryEntry;
        factoryEntry.auraOriginType = __auraOriginType.UpdateAsRef(ref state);
        factoryEntry.translationType = __translationType.UpdateAsRef(ref state);
        factoryEntry.instanceType = __instanceType.UpdateAsRef(ref state);
        factoryEntry.infoType = __infoType.UpdateAsRef(ref state);

        StateMachineFactory<StateMachineScheduler> factoryExit;
        __core.Update<StateMachineScheduler, SchedulerEntry, StateMachineFactory<StateMachineScheduler>, FactoryEntry>(ref state, ref factoryEntry, ref factoryExit);
    }
}

[BurstCompile, CreateAfter(typeof(GameActionFearSchedulerSystem)), UpdateInGroup(typeof(StateMachineGroup))]
public partial struct GameActionFearExecutorSystem : ISystem
{
    public struct Escaper : IStateMachineEscaper
    {
        public ArchetypeChunk chunk;

        public ComponentTypeHandle<GameNavMeshAgentTarget> targetType;

        [ReadOnly]
        public NativeArray<GameActionFearInfo> infos;

        public NativeArray<GameNavMeshAgentTarget> targets;

        public bool Execute(
            int runningStatus,
            in SystemHandle runningSystemHandle,
            in SystemHandle nextSystemHandle,
            in SystemHandle currentSystemHandle,
            int index)
        {
            if (nextSystemHandle != SystemHandle.Null && index < infos.Length)
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
        [ReadOnly]
        public ComponentTypeHandle<GameActionFearInfo> infoType;

        public ComponentTypeHandle<GameNavMeshAgentTarget> targetType;

        public bool Create(
            int unfilteredChunkIndex, 
            in ArchetypeChunk chunk, 
            out Escaper escaper)
        {
            escaper.chunk = chunk;
            escaper.targetType = targetType;
            escaper.infos = chunk.GetNativeArray(ref infoType);
            escaper.targets = chunk.GetNativeArray(ref targetType);

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

        [ReadOnly]
        public NativeArray<GameActionFearData> instances;

        public NativeArray<GameNavMeshAgentTarget> targets;

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
        [ReadOnly]
        public ComponentTypeHandle<Translation> translationType;
        [ReadOnly]
        public ComponentTypeHandle<GameActionFearData> instanceType;
        public ComponentTypeHandle<GameNavMeshAgentTarget> targetType;

        public bool Create(
            int unfilteredChunkIndex, 
            in ArchetypeChunk chunk, 
            out Executor executor)
        {
            executor.chunk = chunk;
            executor.targetType = targetType;
            executor.translationMap = translations;
            executor.auraOrigins = chunk.GetBufferAccessor(ref auraOriginType);
            executor.translations = chunk.GetNativeArray(ref translationType);
            executor.instances = chunk.GetNativeArray(ref instanceType);
            executor.targets = chunk.GetNativeArray(ref targetType);

            return true;
        }
    }

    private ComponentLookup<Translation> __translations;
    private BufferTypeHandle<GameAuraOrigin> __auraOriginType;
    private ComponentTypeHandle<Translation> __translationType;
    private ComponentTypeHandle<GameActionFearData> __instanceType;
    private ComponentTypeHandle<GameActionFearInfo> __infoType;
    private ComponentTypeHandle<GameNavMeshAgentTarget> __targetType;

    private StateMachineExecutorSystemCore __core;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __translations = state.GetComponentLookup<Translation>(true);
        __auraOriginType = state.GetBufferTypeHandle<GameAuraOrigin>(true);
        __translationType = state.GetComponentTypeHandle<Translation>(true);
        __instanceType = state.GetComponentTypeHandle<GameActionFearData>(true);
        __infoType = state.GetComponentTypeHandle<GameActionFearInfo>(true);
        __targetType = state.GetComponentTypeHandle<GameNavMeshAgentTarget>();

        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __core = new StateMachineExecutorSystemCore(
                ref state,
                builder
                .WithAll<GameAuraOrigin, GameActionFearData>(), 
                state.WorldUnmanaged.GetExistingUnmanagedSystem<GameActionFearSchedulerSystem>());
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {

    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var targetType = __targetType.UpdateAsRef(ref state);

        EscaperFactory escaperFactory;
        escaperFactory.infoType = __infoType.UpdateAsRef(ref state);
        escaperFactory.targetType = targetType;

        ExecutorFactory executorFactory;
        executorFactory.translations = __translations.UpdateAsRef(ref state);
        executorFactory.auraOriginType = __auraOriginType.UpdateAsRef(ref state);
        executorFactory.translationType = __translationType.UpdateAsRef(ref state);
        executorFactory.instanceType = __instanceType.UpdateAsRef(ref state);
        executorFactory.targetType = targetType;

        __core.Update<Escaper, Executor, EscaperFactory, ExecutorFactory>(ref state, ref executorFactory, ref escaperFactory);
    }
}