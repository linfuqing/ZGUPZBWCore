using System;
using Unity.Jobs;
using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using ZG;
using Unity.Burst;

[assembly: RegisterGenericJobType(typeof(StateMachineExitJob<
    GameActionFearSystem.Exit, 
    GameActionFearSystem.FactoryExit>))]
[assembly: RegisterGenericJobType(typeof(StateMachineEntryJob<
    GameActionFearSystem.Entry, 
    GameActionFearSystem.FactoryEntry>))]
[assembly: RegisterGenericJobType(typeof(StateMachineRunJob<
    GameActionFearSystem.Run, 
    GameActionFearSystem.FactoryRun>))]

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
        //instance.RemoveComponent<GameActionFearInfo>();
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

[BurstCompile, UpdateInGroup(typeof(StateMachineGroup)), UpdateBefore(typeof(GameActionActiveSystem)), UpdateAfter(typeof(GameActionNormalSystem))]
public partial struct GameActionFearSystem : ISystem
{
    public struct Exit : IStateMachineCondition
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

    public struct FactoryExit : IStateMachineFactory<Exit>
    {
        [ReadOnly]
        public ComponentTypeHandle<GameActionFearInfo> infoType;

        public ComponentTypeHandle<GameNavMeshAgentTarget> targetType;

        public bool Create(
            int unfilteredChunkIndex, 
            in ArchetypeChunk chunk, 
            out Exit exit)
        {
            exit.chunk = chunk;
            exit.targetType = targetType;
            exit.infos = chunk.GetNativeArray(ref infoType);
            exit.targets = chunk.GetNativeArray(ref targetType);

            return true;
        }
    }

    public struct Entry : IStateMachineCondition
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

    public struct FactoryEntry : IStateMachineFactory<Entry>
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
            out Entry entry)
        {
            entry.auraOrigins = chunk.GetBufferAccessor(ref auraOriginType);
            entry.translations = chunk.GetNativeArray(ref translationType);
            entry.instances = chunk.GetNativeArray(ref instanceType);
            entry.infos = chunk.GetNativeArray(ref infoType);

            return true;
        }
    }

    public struct Run : IStateMachineExecutor
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

    public struct FactoryRun : IStateMachineFactory<Run>
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
            out Run run)
        {
            run.chunk = chunk;
            run.targetType = targetType;
            run.translationMap = translations;
            run.auraOrigins = chunk.GetBufferAccessor(ref auraOriginType);
            run.translations = chunk.GetNativeArray(ref translationType);
            run.instances = chunk.GetNativeArray(ref instanceType);
            run.targets = chunk.GetNativeArray(ref targetType);

            return true;
        }
    }

    private BufferTypeHandle<GameAuraOrigin> __auraOriginType;
    private ComponentLookup<Translation> __translations;
    private ComponentTypeHandle<Translation> __translationType;
    private ComponentTypeHandle<GameActionFearData> __instanceType;
    private ComponentTypeHandle<GameActionFearInfo> __infoType;
    private ComponentTypeHandle<GameNavMeshAgentTarget> __targetType;

    private StateMachineSystemCore __core;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __auraOriginType = state.GetBufferTypeHandle<GameAuraOrigin>(true);
        __translations = state.GetComponentLookup<Translation>(true);
        __translationType = state.GetComponentTypeHandle<Translation>(true);
        __instanceType = state.GetComponentTypeHandle<GameActionFearData>(true);
        __infoType = state.GetComponentTypeHandle<GameActionFearInfo>();
        __targetType = state.GetComponentTypeHandle<GameNavMeshAgentTarget>();

        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __core = new StateMachineSystemCore(
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
        var translationType = __translationType.UpdateAsRef(ref state);
        var targetType = __targetType.UpdateAsRef(ref state);
        var auraOriginType = __auraOriginType.UpdateAsRef(ref state);
        var instanceType = __instanceType.UpdateAsRef(ref state);
        var infoType = __infoType.UpdateAsRef(ref state);

        FactoryExit factoryExit;
        factoryExit.infoType = infoType;
        factoryExit.targetType = targetType;

        FactoryEntry factoryEntry;
        factoryEntry.auraOriginType = auraOriginType;
        factoryEntry.translationType = translationType;
        factoryEntry.instanceType = instanceType;
        factoryEntry.infoType = infoType;

        FactoryRun factoryRun;
        factoryRun.translations = __translations.UpdateAsRef(ref state);
        factoryRun.auraOriginType = auraOriginType;
        factoryRun.translationType = translationType;
        factoryRun.instanceType = instanceType;
        factoryRun.targetType = targetType;

        __core.Update<Exit, Entry, Run, FactoryExit, FactoryEntry, FactoryRun>(
            ref state, 
            ref factoryRun, 
            ref factoryEntry, 
            ref factoryExit);
    }
}