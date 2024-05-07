using Unity.Jobs;
using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using ZG;
using Unity.Burst;

[assembly: RegisterGenericJobType(typeof(StateMachineEntryJob<
    GameActionFollowSystem.Entry, 
    GameActionFollowSystem.FactoryEntry>))]
[assembly: RegisterGenericJobType(typeof(StateMachineRunJob<
    GameActionFollowSystem.Run, 
    GameActionFollowSystem.FactoryRun>))]

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

[BurstCompile, UpdateInGroup(typeof(StateMachineGroup)), UpdateAfter(typeof(GameActionActiveSystem))]
public partial struct GameActionFollowSystem : ISystem
{
    public struct Entry : IStateMachineCondition
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

    public struct FactoryEntry : IStateMachineFactory<Entry>
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
            int unfilteredChunkIndex, 
            in ArchetypeChunk chunk, 
            out Entry entry)
        {
            entry.translationMap = translations;
            entry.translations = chunk.GetNativeArray(ref translationType);
            entry.instances = chunk.GetNativeArray(ref instanceType);
            entry.actorMasters = chunk.GetNativeArray(ref actorMasterType);

            return true;
        }
    }

    public struct Run : IStateMachineExecutor
    {
        public bool hasPositions;

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

            if (hasPositions)
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

    public struct FactoryRun : IStateMachineFactory<Run>
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

        public bool Create(
            int unfilteredChunkIndex, 
            in ArchetypeChunk chunk, 
            out Run run)
        {
            run.hasPositions = chunk.Has(ref positionType);
            run.time = time;
            run.chunk = chunk;
            run.targetType = targetType;
            run.translationMap = translations;
            //executor.entityArray = chunk.GetNativeArray(entityType);
            run.translations = chunk.GetNativeArray(ref translationType);
            run.instances = chunk.GetNativeArray(ref instanceType);
            run.actorMasters = chunk.GetNativeArray(ref actorMasterType);
            run.targets = chunk.GetNativeArray(ref targetType);
            //executor.entityManager = entityManager;

            return true;
        }
    }

    private GameSyncTime __time;

    private ComponentLookup<Translation> __translations;
    private BufferTypeHandle<GameNodePosition> __positionType;
    private ComponentTypeHandle<Translation> __translationType;
    private ComponentTypeHandle<GameActionFollowData> __instanceType;
    private ComponentTypeHandle<GameActorMaster> __actorMasterType;

    private ComponentTypeHandle<GameNavMeshAgentTarget> __targetType;

    private StateMachineSystemCore __core;

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
            __core = new StateMachineSystemCore(
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
        StateMachineFactory<StateMachineCondition> factoryExit;
        
        var translations = __translations.UpdateAsRef(ref state);
        var translationType = __translationType.UpdateAsRef(ref state);
        var instanceType = __instanceType.UpdateAsRef(ref state);
        var actorMasterType = __actorMasterType.UpdateAsRef(ref state);
        
        FactoryEntry factoryEntry;
        factoryEntry.translations = translations;
        factoryEntry.translationType = translationType;
        factoryEntry.instanceType = instanceType;
        factoryEntry.actorMasterType = actorMasterType;

        FactoryRun factoryRun;
        factoryRun.time = __time.nextTime;
        factoryRun.translations = translations;
        factoryRun.positionType = __positionType.UpdateAsRef(ref state);
        factoryRun.translationType = translationType;
        factoryRun.instanceType = instanceType;
        factoryRun.actorMasterType = actorMasterType;
        factoryRun.targetType = __targetType.UpdateAsRef(ref state);

        __core.Update<StateMachineCondition, Entry, Run, StateMachineFactory<StateMachineCondition>, FactoryEntry, FactoryRun>(
            ref state, 
            ref factoryRun, 
            ref factoryEntry, 
            ref factoryExit);
    }
}