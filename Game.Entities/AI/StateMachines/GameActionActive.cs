using System;
using System.Collections.Generic;
using Unity.Jobs;
using Unity.Burst;
using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Physics;
using UnityEngine;
using ZG;
using Random = Unity.Mathematics.Random;
using Collider = Unity.Physics.Collider;

[assembly: RegisterGenericComponentType(typeof(GameActionActiveSchedulerSystem.StateMachine))]
[assembly: RegisterGenericJobType(typeof(StateMachineExit<GameActionActiveSchedulerSystem.StateMachine, StateMachineScheduler, StateMachineFactory<StateMachineScheduler>>))]
[assembly: RegisterGenericJobType(typeof(StateMachineEntry<GameActionActiveSchedulerSystem.SchedulerEntry, GameActionActiveSchedulerSystem.FactoryEntry>))]
[assembly: RegisterGenericJobType(typeof(StateMachineEscaper<GameActionActiveSchedulerSystem.StateMachine, GameActionActiveExecutorSystem.Escaper, GameActionActiveExecutorSystem.EscaperFactory>))]
[assembly: RegisterGenericJobType(typeof(StateMachineExecutor<GameActionActiveSchedulerSystem.StateMachine, GameActionActiveExecutorSystem.Executor, GameActionActiveExecutorSystem.ExecutorFactory>))]

[Flags]
public enum GameActionActiveFlag
{
    [Tooltip("攻击之后逃跑。")]
    Sly = 0x01,
    [Tooltip("仅攻击可视范围内的敌人。")]
    Timid = 0x02,
}

[Serializable]
public struct GameActionActiveProtectedTime : IComponentData
{
    public double value;
}

public class GameActionActive : StateMachineNode
{
    [Mask]
    public GameActionActiveFlag flag;

    [Tooltip("使用此标签算攻击距离。")]
    public LayerMask hitMask;

    [Tooltip("遇到该标签敌人则逃跑。")]
    public LayerMask layerMask;

    [Tooltip("状态机优先级")]
    public int watcherPriority = 1;

    [Tooltip("状态机优先级")]
    public int speakerPriority = 3;

    [Tooltip("警觉技能")]
    public int entryActionIndex = 0;

    [Tooltip("逃跑或进攻改变方向阈值")]
    public float dot = 0.5f;

    [Tooltip("生命小于该值则逃跑"), Range(0.0f, 1.0f)]
    public float health = 0.3f;

    [Tooltip("晕眩小于该值则逃跑"), Range(0.0f, 1.0f)]
    public float torpidity = 0.3f;
    
    [Tooltip("仇恨时间")]
    public float speakerTime = 60.0f;

    [Tooltip("追寻时间")]
    public float watcherTime = 5.0f;

    [Tooltip("不追寻时间")]
    public float restTime = 10.0f;

    [Tooltip("领地半径，离开该半径则丢失目标。")]
    public float radius = 30.0f;

    [Tooltip("搜寻距离，离开该范围则丢失目标。")]
    [UnityEngine.Serialization.FormerlySerializedAs("distance")]
    public float maxDistance = 10.0f;

    [Tooltip("最近距离")]
    public float minDistance = 0.0f;

    [Tooltip("最大环绕距离")]
    [NonSerialized]
    public float maxAlertDistance = 10.0f;

    [Tooltip("最小环绕距离")]
    [NonSerialized]
    public float minAlertDistance = 0.0f;

    [Tooltip("最大环绕时间")]
    [NonSerialized]
    public float maxAlertTime = 10.0f;

    [Tooltip("最小环绕时间")]
    [NonSerialized]
    public float minAlertTime = 3.0f;

    [Tooltip("环绕概率")]
    [NonSerialized]
    public float alertChance = 0.1f;

    [Tooltip("环绕速度")]
    public float alertSpeedScale = 0.3f;

    public override void Enable(StateMachineComponentEx instance)
    {
        GameActionActiveData data;
        data.flag = flag;
        data.hitMask = (uint)(int)hitMask;
        data.layerMask = (uint)(int)layerMask;
        data.watcherPriority = watcherPriority;
        data.speakerPriority = speakerPriority;
        data.entryActionIndex = entryActionIndex;
        data.dot = dot;
        data.health = health;
        data.torpidity = torpidity;
        data.speakerTime = speakerTime;
        data.watcherTime = watcherTime;
        data.restTime = restTime;
        data.radiusSq = radius * radius;
        data.minDistance = minDistance;
        data.maxDistance = maxDistance;
        data.minAlertDistance = minAlertDistance;
        data.maxAlertDistance = maxAlertDistance;
        data.maxAlertTime = maxAlertTime;
        data.minAlertTime = minAlertTime;
        data.alertChance = alertChance;
        data.alertSpeedScale = alertSpeedScale;
        instance.AddComponentData(data);

        instance.AddComponent<GameActionActiveInfo>();

        /*GameEntityActionCommand command;
        command.version = -1;
        command.index = -1;
        command.time = 0;
        command.entity = Entity.Null;
        command.forward = float3.zero;
        command.distance = float3.zero;
        instance.AddComponentDataIfNotExists(command);*/
    }

    public override void Disable(StateMachineComponentEx instance)
    {
        instance.RemoveComponent<GameActionActiveData>();
        instance.RemoveComponent<GameActionActiveInfo>();
    }
}

[Serializable]
public struct GameActionActiveData : IComponentData
{
    public GameActionActiveFlag flag;

    [Tooltip("使用此标签算攻击距离。")]
    public uint hitMask;

    [Tooltip("遇到该标签敌人则逃跑。")]
    public uint layerMask;

    [Tooltip("状态机优先级")]
    public int watcherPriority;

    [Tooltip("状态机优先级")]
    public int speakerPriority;

    [Tooltip("警觉技能")]
    public int entryActionIndex;

    [Tooltip("逃跑或进攻改变方向阈值")]
    public float dot;

    [Tooltip("生命小于该值则逃跑")]
    public float health;

    [Tooltip("晕眩小于该值则逃跑")]
    public float torpidity;
    
    [Tooltip("仇恨时间")]
    public float speakerTime;

    [Tooltip("追寻时间")]
    public float watcherTime;

    [Tooltip("不追寻时间")]
    public float restTime;

    [Tooltip("领地半径，离开该半径则丢失目标。")]
    public float radiusSq;
    
    [Tooltip("搜寻距离，离开该距离则不攻击。")]
    public float maxDistance;

    [Tooltip("最近搜寻距离，小于该距离则后退。")]
    public float minDistance;

    [Tooltip("最大环绕距离")]
    public float maxAlertDistance;

    [Tooltip("最小环绕距离")]
    public float minAlertDistance;

    [Tooltip("最大环绕时间")]
    public float maxAlertTime;

    [Tooltip("最小环绕时间")]
    public float minAlertTime;

    [Tooltip("环绕概率")]
    public float alertChance;

    [Tooltip("环绕速度")]
    public float alertSpeedScale;
}

[Serializable]
public struct GameActionActiveInfo : IComponentData
{
    public enum Status
    {
        Forward = 0,
        Backward = 2,
        AlertLeft = -1, 
        AlertRight = 1
    }

    public Status status;
    public int groupMask;
    public int conditionIndex;
    public double activeTime;
    public double alertTime;
    public float3 position;
    public Entity entity;
}

[UpdateAfter(typeof(GameActionNormalSchedulerSystem))]
public partial class GameActionActiveSchedulerSystem : StateMachineSchedulerSystem<
    GameActionActiveSchedulerSystem.SchedulerEntry,
    GameActionActiveSchedulerSystem.FactoryEntry, 
    GameActionActiveSchedulerSystem>
{
    public struct SchedulerEntry : IStateMachineScheduler
    {
        public double time;

        [ReadOnly]
        public ComponentLookup<Disabled> disabled;
        
        [ReadOnly]
        public ComponentLookup<Translation> translationMap;

        [ReadOnly]
        public NativeArray<Translation> translations;

        [ReadOnly]
        public NativeArray<GameWatcherInfo> watcherInfos;

        [ReadOnly]
        public NativeArray<GameSpeakerInfo> speakerInfos;

        [ReadOnly]
        public NativeArray<GameActionActiveData> instances;
        
        public NativeArray<GameActionActiveInfo> infos;

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

            var instance = instances[index];

            float distanceSq = instance.maxDistance * instance.maxDistance;
            float3 position = translations[index].Value;
            var info = infos[index];

            if (runningStatus < instance.speakerPriority && instance.speakerTime > math.FLT_MIN_NORMAL && speakerInfos.Length > index)
            {
                GameSpeakerInfo speakerInfo = speakerInfos[index];
                if (translationMap.HasComponent(speakerInfo.target) && 
                    !disabled.HasComponent(speakerInfo.target) &&
                    speakerInfo.time + instance.speakerTime > time &&
                    (info.entity != speakerInfo.target || 
                    info.activeTime + instance.restTime < math.min(time, speakerInfo.time) || 
                    math.distancesq(position, translationMap[speakerInfo.target].Value) < distanceSq))
                {
                    info.activeTime = time + instance.speakerTime;
                    info.position = position;
                    info.entity = speakerInfo.target;

                    infos[index] = info;
                    
                    return true;
                }
            }

            if (runningStatus < instance.watcherPriority && index < watcherInfos.Length)
            {
                GameWatcherInfo watcherInfo = watcherInfos[index];
                if (translationMap.HasComponent(watcherInfo.target) &&
                    !disabled.HasComponent(watcherInfo.target) && 
                    watcherInfo.time + instance.watcherTime > time && 
                    (info.entity != watcherInfo.target || 
                    info.activeTime + instance.restTime < math.min(time, watcherInfo.time) ||
                    math.distancesq(position, translationMap[watcherInfo.target].Value) < distanceSq))
                {
                    info.activeTime = time + instance.watcherTime;
                    info.position = position;
                    info.entity = watcherInfo.target;

                    infos[index] = info;

                    /*int one = info.entity != watcherInfo.target ? 1 : 0;
                    int two = info.time + instance.restTime < math.min(time, watcherInfo.time) ? 1 : 0;
                    int three = math.distancesq(position, translationMap[watcherInfo.target].Value) < instance.radiusSq ? 1 : 0;

                    Debug.Log($"{watcherInfo.target.Index} : {one} : {two} : {three}");

                    /*watcherInfo.time = double.MaxValue;
                    watcherInfos[index] = watcherInfo;*/

                    return true;
                }
            }

            return false;
        }
    }

    public struct FactoryEntry : IStateMachineFactory<SchedulerEntry>
    {
        public double time;

        [ReadOnly]
        public ComponentLookup<Disabled> disabled;
        
        [ReadOnly]
        public ComponentLookup<Translation> translations;
        
        [ReadOnly]
        public ComponentTypeHandle<Translation> translationType;

        [ReadOnly]
        public ComponentTypeHandle<GameWatcherInfo> watcherInfoType;
        [ReadOnly]
        public ComponentTypeHandle<GameSpeakerInfo> speakerInfoType;
        [ReadOnly]
        public ComponentTypeHandle<GameActionActiveData> instanceType;
        
        public ComponentTypeHandle<GameActionActiveInfo> infoType;

        public bool Create(
            int index, 
            in ArchetypeChunk chunk,
            out SchedulerEntry schedulerEntry)
        {
            schedulerEntry.time = time;
            schedulerEntry.disabled = disabled;
            schedulerEntry.translationMap = translations;
            schedulerEntry.translations = chunk.GetNativeArray(ref translationType);
            schedulerEntry.watcherInfos = chunk.GetNativeArray(ref watcherInfoType);
            schedulerEntry.speakerInfos = chunk.GetNativeArray(ref speakerInfoType);
            schedulerEntry.instances = chunk.GetNativeArray(ref instanceType);
            schedulerEntry.infos = chunk.GetNativeArray(ref infoType);

            return true;
        }
    }

    //public override IEnumerable<ComponentType> exitAnyComponentTypes =>  __exitAnyComponentTypes;

    public override IEnumerable<EntityQueryDesc> entryEntityArchetypeQueries => __entryEntityArchetypeQueries;

    private GameSyncTime __time;

    protected override void OnCreate()
    {
        base.OnCreate();

        __time = new GameSyncTime(ref this.GetState());
    }

    protected override FactoryEntry _GetEntry(ref JobHandle inputDeps)
    {
        FactoryEntry factoryEntry;
        factoryEntry.time = __time.nextTime;
        factoryEntry.disabled = GetComponentLookup<Disabled>(true);
        factoryEntry.translations = GetComponentLookup<Translation>(true);
        factoryEntry.translationType = GetComponentTypeHandle<Translation>(true);
        factoryEntry.watcherInfoType = GetComponentTypeHandle<GameWatcherInfo>(true);
        factoryEntry.speakerInfoType = GetComponentTypeHandle<GameSpeakerInfo>(true);
        factoryEntry.instanceType = GetComponentTypeHandle<GameActionActiveData>(true);
        factoryEntry.infoType = GetComponentTypeHandle<GameActionActiveInfo>();

        return factoryEntry;
    }

    /*private readonly ComponentType[] __exitAnyComponentTypes = new ComponentType[]
    {
        ComponentType.ReadOnly<GameActionActiveInfo>(),
        ComponentType.ReadWrite<GameWatcherInfo>()
    };*/

    private readonly EntityQueryDesc[] __entryEntityArchetypeQueries = new EntityQueryDesc[]
    {
        new EntityQueryDesc()
        {
            All = new ComponentType[]
            {
                ComponentType.ReadOnly<Translation>(),
                ComponentType.ReadOnly<GameActionActiveData>(),
                ComponentType.ReadWrite<GameActionActiveInfo>()
            },
            Any = new ComponentType[]
            {
                ComponentType.ReadOnly<GameSpeakerInfo>(),
                ComponentType.ReadOnly<GameWatcherInfo>()
            }, 
            None = new ComponentType[]
            {
                typeof(Disabled)
            }
        }
    };
}

//[UpdateInGroup(typeof(StateMachineExecutorGroup))]
public partial class GameActionActiveExecutorSystem : GameActionActiveSchedulerSystem.StateMachineExecutorSystem<
    GameActionActiveExecutorSystem.Escaper, 
    GameActionActiveExecutorSystem.Executor,
    GameActionActiveExecutorSystem.EscaperFactory,
    GameActionActiveExecutorSystem.ExecutorFactory>, IEntityCommandProducerJob
{
    public struct Escaper : IStateMachineEscaper
    {
        //public double time;

        public bool isHasPositions;

        [ReadOnly]
        public NativeArray<Entity> entityArray;

        [ReadOnly]
        public NativeArray<GameActionActiveInfo> infos;
        
        public NativeArray<GameNavMeshAgentTarget> targets;

        public EntityAddDataQueue.ParallelWriter entityManager;

        public bool Execute(
            int runningStatus,
            int runningSystemIndex,
            int nextSystemIndex,
            int currentSystemIndex,
            int index)
        {
            if (isHasPositions && nextSystemIndex != -1 && index < infos.Length)
            {
                GameNavMeshAgentTarget target;
                target.sourceAreaMask = -1;
                target.destinationAreaMask = -1;
                target.position = infos[index].position;
                if (index < targets.Length)
                    targets[index] = target;
                else
                    entityManager.AddComponentData(entityArray[index], target);
            }

            /*if (versions.Exists(entity))
            {
                bool isUpdate = false;
                GameNodeVersion version = versions[entity];
                if (index < velocities.Length)
                {
                    GameNodeVelocityDirect velocity = velocities[index];
                    if (!velocity.value.Equals(float3.zero))
                    {
                        isUpdate = true;

                        version.type = GameNodeVersion.Type.Direction;
                        ++version.value;

                        velocity.mode = GameNodeVelocityDirect.Mode.None;
                        velocity.version = version.value;
                        velocity.value = float3.zero;
                        velocities[index] = velocity;
                    }
                }

                if (index < this.positions.Length && index < infos.Length)
                {
                    var positions = this.positions[index];
                    positions.Clear();

                    if (isUpdate)
                        version.type |= GameNodeVersion.Type.Position;
                    else
                    {
                        isUpdate = true;

                        version.type = GameNodeVersion.Type.Position;
                        ++version.value;
                    }

                    GameNodePosition position;
                    position.mode = GameNodePosition.Mode.Normal;
                    position.version = version.value;
                    //position.distance = float.MaxValue;
                    position.value = infos[index].position;

                    positions.Add(position);
                }

                if (isUpdate)
                    versions[entity] = version;
            }*/

            return true;
        }
    }

    public struct EscaperFactory : IStateMachineFactory<Escaper>
    {
        //public double time;

        [ReadOnly]
        public EntityTypeHandle entityType;

        [ReadOnly]
        public BufferTypeHandle<GameNodePosition> positionType;

        [ReadOnly]
        public ComponentTypeHandle<GameActionActiveInfo> infoType;

        public ComponentTypeHandle<GameNavMeshAgentTarget> targetType;

        public EntityAddDataQueue.ParallelWriter entityManager;

        public bool Create(
            int index, 
            in ArchetypeChunk chunk,
            out Escaper escaper)
        {
            //escaper.time = time;
            escaper.isHasPositions = chunk.Has(ref positionType);
            escaper.entityArray = chunk.GetNativeArray(entityType);
            escaper.infos = chunk.GetNativeArray(ref infoType);
            escaper.targets = chunk.GetNativeArray(ref targetType);
            escaper.entityManager = entityManager;
            return true;
        }
    }

    public struct Executor : IStateMachineExecutor
    {
        public float collisionTolerance;
        public GameTime time;

        public Random random;

        [ReadOnly]
        public BlobAssetReference<GameActionSetDefinition> actions;

        [ReadOnly]
        public SingletonAssetContainer<BlobAssetReference<Collider>>.Reader actionColliders;

        [ReadOnly]
        public ComponentLookup<Disabled> disabled;

        [ReadOnly]
        public ComponentLookup<Translation> translations;

        [ReadOnly]
        public ComponentLookup<Rotation> rotations;

        [ReadOnly]
        public ComponentLookup<PhysicsMass> physicsMassMap;

        [ReadOnly]
        public ComponentLookup<PhysicsCollider> physicsColliders;

        [ReadOnly]
        public ComponentLookup<GameActionActiveProtectedTime> protectedTimes;

        [ReadOnly]
        public NativeArray<Entity> entityArray;

        [ReadOnly]
        public NativeArray<GameNodeStatus> states;

        [ReadOnly]
        public NativeArray<GameNodeDelay> delay;

        [ReadOnly]
        public NativeArray<GameNodeAngle> angles;

        [ReadOnly]
        public NativeArray<GameNodeActorStatus> actorStates;

        [ReadOnly]
        public NativeArray<GameNodeCharacterDesiredVelocity> desiredVelocities;

        [ReadOnly]
        public NativeArray<GameEntityHealthData> healthData;

        [ReadOnly]
        public NativeArray<GameEntityHealth> healthes;
        
        [ReadOnly]
        public NativeArray<GameEntityTorpidityData> torpidityData;

        [ReadOnly]
        public NativeArray<GameEntityTorpidity> torpidities;

        [ReadOnly]
        public NativeArray<GameEntityCommandVersion> commandVersions;

        [ReadOnly]
        public NativeArray<GameEntityActorTime> actorTimes;

        [ReadOnly]
        public NativeArray<GameEntityActorData> actors;

        [ReadOnly]
        public NativeArray<GameActionActiveData> instances;

        [ReadOnly]
        public NativeArray<GameSpeakerInfo> speakerInfos;

        [ReadOnly]
        public NativeArray<GameWatcherInfo> watcherInfos;

        [ReadOnly]
        public BufferAccessor<GameEntityActorActionData> actorActions;

        [ReadOnly]
        public BufferAccessor<GameEntityActorActionInfo> actorActionInfos;

        [ReadOnly]
        public BufferAccessor<GameActionConditionAction> conditionActions;

        [ReadOnly]
        public BufferAccessor<GameActionCondition> conditions;

        [ReadOnly]
        public BufferAccessor<GameActionGroup> groups;

        public BufferAccessor<GameNodePosition> positions;

        public NativeArray<GameNodeDirection> directions;

        public NativeArray<GameNavMeshAgentTarget> targets;

        public NativeArray<GameActionActiveInfo> infos;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<GameEntityActionCommand> commands;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<GameNodeVersion> versions;

        public EntityAddDataQueue.ParallelWriter addComponentCommander;
        public EntityCommandQueue<EntityCommandStructChange>.ParallelWriter removeComponentCommander;

        public static unsafe bool IsDo(
            float collisionTolerance,
            float distance,
            float sourceScale,
            in float3 offset,
            in RigidTransform transform,
            in BlobAssetReference<Collider> sourceCollider,
            in BlobAssetReference<Collider> destinationCollider)
        {
            float3 position = math.transform(transform, offset);

            if (sourceCollider.IsCreated && destinationCollider.IsCreated)
            {
                PointDistanceInput pointDistanceInput = default;
                pointDistanceInput.MaxDistance = float.MaxValue;
                pointDistanceInput.Position = position;
                pointDistanceInput.Filter = sourceCollider.Value.Filter;
                if (destinationCollider.Value.CalculateDistance(pointDistanceInput, out var closestHit))
                {
                    if (closestHit.Distance <= distance)
                        return true;

                    position = math.normalize(closestHit.Position - position) * (distance - collisionTolerance) + position;
                }

                var target = transform;
                target.pos = position;

                var collider = (Collider*)sourceCollider.GetUnsafePtr();
                if (math.abs(sourceScale) > math.FLT_MIN_NORMAL)
                {
                    int size = collider->MemorySize;
                    var bytes = stackalloc byte[size];
                    Unity.Collections.LowLevel.Unsafe.UnsafeUtility.MemCpy(bytes, collider, size);
                    collider = (Collider*)bytes;
                    (*collider).Scale(sourceScale);
                }

                ColliderDistanceInput colliderDistanceInput = default;
                colliderDistanceInput.Collider = collider;
                colliderDistanceInput.Transform = target;
                colliderDistanceInput.MaxDistance = collisionTolerance;

                return destinationCollider.Value.CalculateDistance(colliderDistanceInput);
            }

            return math.lengthsq(position) <= distance * distance;
        }

        public int Execute(bool isEntry, int index)
        {
            var instance = instances[index];
            if ((states[index].value & GameNodeStatus.STOP) == GameNodeStatus.STOP)
                return 0;

            Entity entity = entityArray[index];
            if (!translations.HasComponent(entity))
                return 0;

            float3 source = translations[entity].Value;

            var info = infos[index];

            bool isWatch = true;
            Entity speakerEntity = Entity.Null;
            if (speakerInfos.Length > index)
            {
                GameSpeakerInfo speakerInfo = speakerInfos[index];
                speakerEntity = speakerInfo.target;
                if (speakerEntity != Entity.Null)
                {
                    double speakerTime = speakerInfo.time + instance.speakerTime;
                    if (time < speakerTime)
                    {
                        isWatch = false;

                        if (info.activeTime < speakerTime)
                        {
                            info.activeTime = speakerTime;
                            info.position = source;
                            info.entity = speakerInfo.target;
                        }
                    }
                }
            }

            if (isWatch && watcherInfos.Length > index)
            {
                var watcherInfo = watcherInfos[index];
                if (watcherInfo.target != Entity.Null)
                {
                    if (watcherInfo.target != info.entity/* && (time - watcherInfo.time) < instance.watcherTime*/)
                    {
                        info.entity = watcherInfo.target;

                        isEntry = true;
                    }
                }
            }

            if (disabled.HasComponent(info.entity) || !translations.HasComponent(info.entity) || !rotations.HasComponent(info.entity))
            {
                info.activeTime = time;

                infos[index] = info;

                return 0;
            }

            quaternion targetRotation = rotations[info.entity].Value;
            float3 destination = translations[info.entity].Value,
                targetPosition = physicsMassMap.HasComponent(info.entity) ? 
                math.transform(math.RigidTransform(targetRotation, destination), physicsMassMap[info.entity].CenterOfMass) : 
                destination, 
                forward = targetPosition - source;
            float distance = math.length(forward);
            if (isWatch && isEntry)
            {
                infos[index] = info;

                if (distance > math.FLT_MIN_NORMAL)
                {
                    GameEntityActionCommand command;
                    command.version = commandVersions[index].value;
                    command.index = instance.entryActionIndex;
                    command.time = time;
                    command.entity = info.entity;
                    command.forward = forward / distance;
                    command.distance = float3.zero;

                    commands[entity] = command;
                }

                return instance.watcherPriority;
            }

            if (isWatch)
            {
                if (distance > instance.maxDistance)
                {
                    if (info.activeTime < time)
                    {
                        info.activeTime = time;

                        infos[index] = info;

                        return 0;
                    }
                }
                else if (math.distancesq(destination, info.position) < instance.radiusSq)
                {
                    info.activeTime = time + instance.watcherTime;

                    if (info.entity == speakerEntity)
                        isWatch = false;
                }
            }
            else if (info.activeTime < time)
            {
                if(distance > instance.maxDistance)
                {
                    info.activeTime = time;

                    infos[index] = info;

                    return 0;
                }

                return instance.speakerPriority;
            }

            if ((!isWatch || 
                instance.watcherTime > math.FLT_MIN_NORMAL) &&
                physicsColliders.HasComponent(info.entity))
            {
                /*var collider = physicsColliders[info.entity].Value;

                if (physicsColliders.HasComponent(info.entity))
                {
                    ChildCollider child;
                    var collider = physicsColliders[info.entity].ColliderPtr;
                    if (physicsColliders.HasComponent(entity))
                    {
                        var physicsCollider = physicsColliders[entity];
                        RigidTransform transform = math.RigidTransform(rotations[info.entity].Value, destination);

                        ColliderDistanceInput colliderDistanceInput = default;
                        colliderDistanceInput.Collider = physicsCollider.ColliderPtr;
                        colliderDistanceInput.Transform = math.mul(math.inverse(transform), math.RigidTransform(rotations[entity].Value, source));
                        colliderDistanceInput.MaxDistance = distance;

                        if (collider->CalculateDistance(colliderDistanceInput, out DistanceHit closestHit))
                        {
                            distance = closestHit.Distance;
                            destination = math.transform(transform, closestHit.Position);

                            if (collider->GetLeaf(closestHit.ColliderKey, out child))
                                collider = child.Collider;
                        }
                    }
                    else
                    {
                        RigidTransform transform = math.RigidTransform(rotations[info.entity].Value, destination);

                        PointDistanceInput pointDistanceInput = default;
                        pointDistanceInput.MaxDistance = distance;
                        pointDistanceInput.Position = math.transform(math.inverse(transform), source);
                        pointDistanceInput.Filter = default;
                        pointDistanceInput.Filter.BelongsTo = instance.hitMask;
                        pointDistanceInput.Filter.CollidesWith = ~0u;
                        if (collider->CalculateDistance(pointDistanceInput, out DistanceHit closestHit))
                        {
                            distance = closestHit.Distance;
                            destination = math.transform(transform, closestHit.Position);

                            if (collider->GetLeaf(closestHit.ColliderKey, out child))
                                collider = child.Collider;
                        }
                    }
                    categoryBits = physicsColliders[info.entity].Value.Value.Filter.BelongsTo;
                }*/

                forward = distance > math.FLT_MIN_NORMAL ? forward / distance : forward;

                bool isRunAway, isHasPosition = index < positions.Length, isDelay = delay[index].Check(time);
                uint categoryBits = physicsColliders[info.entity].Value.Value.Filter.BelongsTo;
                int maxHealth = index < healthes.Length && index < healthData.Length ? healthData[index].max : 0, 
                    maxTorpidity = index < torpidities.Length && index < torpidityData.Length ? torpidityData[index].max : 0;
                float health = maxHealth < 1 ? 1.0f : healthes[index].value / maxHealth, 
                    torpidity = maxTorpidity < 1 ? 1.0f : torpidities[index].value / maxTorpidity;
                if ((instance.layerMask == 0 || (instance.layerMask & categoryBits) == 0) &&
                    (!isHasPosition || 
                    health > instance.health &&
                    torpidity > instance.torpidity))
                {
                    if (!isWatch && (instance.flag & GameActionActiveFlag.Sly) == GameActionActiveFlag.Sly)
                        isRunAway = isHasPosition;
                    else if ((!isWatch || !protectedTimes.HasComponent(info.entity) || protectedTimes[info.entity].value < time))
                    {
                        if (actorTimes[index].value > time || commandVersions[index].value == commands[entity].version)
                            isRunAway = false;
                        else
                        {
                            bool isHasAngle = index < angles.Length;
                            float3 surfaceForward = isHasAngle ? math.forward(quaternion.RotateY(angles[index].value)) : float3.zero;

                            var actorActionInfos = this.actorActionInfos[index];
                            float dot = math.dot(forward, surfaceForward);
                            bool isDiffDirection = isHasAngle && dot < 0.0f;
                            GameEntityActionCommand command;
                            if (groups.Length > 0)
                            {
                                var conditions = this.conditions[index];
                                var conditionActions = this.conditionActions[index];
                                var groups = this.groups[index];

                                float actorVelocity = index < desiredVelocities.Length ? math.rotate(math.inverse(quaternion.LookRotationSafe(forward, math.up())), desiredVelocities[index].linear).z : 0.0f;
                                int actorStatus = index < actorStates.Length ? (int)actorStates[index].value : 0, groupMask = 0;
                                if (info.groupMask == 0 || !isDelay)
                                {
                                    var result = GameActionCondition.Did(
                                            time,
                                            actorVelocity,
                                            actorStatus,
                                            ref groupMask, 
                                            ref info.conditionIndex,
                                            conditions,
                                            actorActionInfos,
                                            conditionActions);

                                    info.groupMask = result == GameActionConditionResult.OK ? GameActionGroup.Did(groupMask, health, torpidity, dot, distance, random.NextFloat(), groups) : 0;

                                    info.conditionIndex = -1;
                                }

                                if (info.groupMask != 0)
                                {
                                    var result = GameActionCondition.Did(
                                         time,
                                         actorVelocity,
                                         actorStatus,
                                         ref info.groupMask, 
                                         ref info.conditionIndex,
                                         conditions,
                                         actorActionInfos,
                                         conditionActions);
                                    if (result == GameActionConditionResult.OK)
                                    {
                                        command.version = commandVersions[index].value;
                                        command.index = conditions[info.conditionIndex].actionIndex;
                                        command.time = time;
                                        command.forward = forward;
                                        command.distance = float3.zero;
                                        command.entity = info.entity;
                                        commands[entity] = command;

                                        isHasPosition = false;
                                    }
                                    else
                                        info.groupMask = 0;
                                }

                                if(info.groupMask == 0 && isHasPosition)
                                {
                                    GameActionGroup group;
                                    int numGroups = groups.Length;
                                    for (int i = 0; i < numGroups; ++i)
                                    {
                                        group = groups[i];
                                        if ((group.mask & groupMask) == 0)
                                            continue;

                                        if (group.minDistance < group.maxDistance && group.Did(health, torpidity, dot) != 0)
                                        {
                                            instance.maxAlertDistance = math.min(instance.maxAlertDistance, group.maxDistance);
                                            instance.minAlertDistance = math.max(instance.minAlertDistance, group.minDistance);
                                        }
                                    }
                                }
                            }
                            else
                            {
                                ref var actions = ref this.actions.Value;
                                SingletonAssetContainerHandle handle;
                                handle.instanceID = actions.instanceID;

                                int numActorActions = actorActionInfos.Length, i;
                                double coolDownTime;
                                RigidTransform sourceTransform = math.RigidTransform(quaternion.LookRotationSafe(forward, math.up()), source),
                                    destinationTransform = math.RigidTransform(targetRotation, destination),
                                    transform = math.mul(math.inverse(destinationTransform), sourceTransform);
                                var collider = physicsColliders[info.entity].Value;
                                var actor = actors[index];
                                var actorActions = this.actorActions[index];
                                for (i = 0; i < numActorActions; ++i)
                                {
                                    ref var action = ref actions.values[actorActions[i].actionIndex];
                                    if ((action.instance.damageType & GameActionTargetType.Enemy) == GameActionTargetType.Enemy &&
                                        (action.instance.damageMask & categoryBits) != 0 &&
                                        action.colliderIndex != -1)
                                    {
                                        handle.index = action.colliderIndex;

                                        coolDownTime = actorActionInfos[i].coolDownTime;
                                        if (coolDownTime < time &&
                                            isDiffDirection == action.info.distance < 0.0f &&
                                            IsDo(
                                                collisionTolerance,
                                                math.abs(action.info.distance) * (actor.distanceScale > math.FLT_MIN_NORMAL ? actor.distanceScale : 1.0f),
                                                (actor.rangeScale > math.FLT_MIN_NORMAL ? actor.rangeScale : 1.0f) * (action.info.scale > math.FLT_MIN_NORMAL ? action.info.scale : 1.0f),
                                                math.select(action.instance.offset, action.instance.offset * actor.offsetScale, actor.offsetScale > math.FLT_MIN_NORMAL),
                                                transform,
                                                actionColliders[handle],
                                                collider))
                                        {
                                            command.version = commandVersions[index].value;
                                            command.index = i;
                                            command.time = time;
                                            command.forward = forward;
                                            command.distance = float3.zero;
                                            command.entity = info.entity;
                                            commands[entity] = command;

                                            isHasPosition = false;

                                            break;
                                        }
                                    }
                                }
                            }

                            if (isHasPosition)
                            {
                                if (/*info.status == GameActionActiveInfo.Status.Forward || */distance > instance.maxAlertDistance || dot < 0.0f)
                                {
                                    isRunAway = (instance.flag & GameActionActiveFlag.Timid) == GameActionActiveFlag.Timid;
                                    if (!isRunAway)
                                    {
                                        GameNavMeshAgentTarget target;
                                        target.sourceAreaMask = -1;
                                        target.destinationAreaMask = -1;
                                        target.position = source + forward * distance;
                                        if (index < targets.Length)
                                            targets[index] = target;
                                        else// if (!isDelay)
                                            addComponentCommander.AddComponentData(entity, target);
                                    }
                                }
                                else
                                {
                                    if (info.alertTime < time || distance < instance.minAlertDistance)
                                    {
                                        info.alertTime = time + random.NextFloat(instance.minAlertTime, instance.maxAlertTime);

                                        info.status = instance.alertChance > random.NextFloat() ?
                                            (GameActionActiveInfo.Status)(int)math.sign(random.NextFloat() - 0.5f) :
                                            (distance < instance.minAlertDistance ? GameActionActiveInfo.Status.Backward : GameActionActiveInfo.Status.Forward);
                                    }

                                    float3 value;
                                    var direction = directions[index];
                                    switch (info.status)
                                    {
                                        case GameActionActiveInfo.Status.Forward:
                                            value = forward;

                                            direction.mode = GameNodeDirection.Mode.Forward;
                                            break;
                                        case GameActionActiveInfo.Status.Backward:
                                            value = -forward;

                                            direction.mode = GameNodeDirection.Mode.Backward;
                                            break;
                                        default:
                                            float3 right = math.cross(forward, math.up());

                                            int sign = math.dot(right, direction.value) < 0.0f ? -1 : 1;

                                            value = right/*math.rotate(
                                            quaternion.RotateY(math.acos(instance.dot) * (sign * (direction.mode == GameNodeDirection.Mode.Backward ? -1 : 1))), 
                                            right)*/ * sign;
                                            break;
                                    }

                                    if (math.dot(math.normalizesafe(direction.value), value) < instance.dot)
                                    {
                                        var version = versions[entity];
                                        version.type = GameNodeVersion.Type.Direction;
                                        ++version.value;
                                        versions[entity] = version;

                                        //direction.mode = isHasAngle && math.dot(value, surfaceForward) < 0.0f ? GameNodeDirection.Mode.Backward : GameNodeDirection.Mode.Forward;
                                        direction.version = version.value;
                                        direction.value = value * instance.alertSpeedScale;
                                        directions[index] = direction;

                                        positions[index].Clear();

                                        if (index < targets.Length)
                                        {
                                            EntityCommandStructChange targetCommand;
                                            targetCommand.entity = entity;
                                            targetCommand.componentType = ComponentType.ReadWrite<GameNavMeshAgentTarget>();
                                            removeComponentCommander.Enqueue(targetCommand);
                                        }
                                    }

                                    isRunAway = false;
                                }
                            }
                            else
                                isRunAway = false;
                        }
                    }
                    else
                        isRunAway = isHasPosition && (instance.flag & GameActionActiveFlag.Timid) == GameActionActiveFlag.Timid;
                }
                else
                    isRunAway = isHasPosition;

                if (isRunAway && index < directions.Length && !isDelay)
                {
                    float3 value = -forward;
                    var direction = directions[index];
                    if (math.dot(math.normalizesafe(direction.value), value) < instance.dot)
                    {
                        var version = versions[entity];
                        version.type = GameNodeVersion.Type.Direction;
                        ++version.value;
                        versions[entity] = version;

                        direction.mode = GameNodeDirection.Mode.Forward;
                        direction.version = version.value;
                        direction.value = value;
                        directions[index] = direction;

                        if (isHasPosition)
                            positions[index].Clear();

                        if (index < targets.Length)
                        {
                            EntityCommandStructChange targetCommand;
                            targetCommand.entity = entity;
                            targetCommand.componentType = ComponentType.ReadWrite<GameNavMeshAgentTarget>();
                            removeComponentCommander.Enqueue(targetCommand);
                        }
                    }

                    /*GameNavMeshAgentTarget target;
                    target.sourceAreaMask = -1;
                    target.destinationAreaMask = -1;
                    target.position = source + value * instance.maxDistance;
                    if (index < targets.Length)
                        targets[index] = target;
                    else if(delay[index].time < time)
                    {
                        EntityData<GameNavMeshAgentTarget> result;
                        result.entity = entity;
                        result.value = target;
                        addComponentCommander.Enqueue(result);
                    }*/
                }
            }

            infos[index] = info;

            return isWatch ? instance.watcherPriority : instance.speakerPriority;
        }
    }

    public struct ExecutorFactory : IStateMachineFactory<Executor>
    {
        public float collisionTolerance;

        public GameTime time;

        [ReadOnly]
        public BlobAssetReference<GameActionSetDefinition> actions;

        [ReadOnly]
        public SingletonAssetContainer<BlobAssetReference<Collider>>.Reader actionColliders;

        [ReadOnly]
        public ComponentLookup<Disabled> disabled;

        [ReadOnly]
        public ComponentLookup<Translation> translations;

        [ReadOnly]
        public ComponentLookup<Rotation> rotations;

        [ReadOnly]
        public ComponentLookup<PhysicsMass> physicsMasses;

        [ReadOnly]
        public ComponentLookup<PhysicsCollider> physicsColliders;

        [ReadOnly]
        public ComponentLookup<GameActionActiveProtectedTime> protectedTimes;

        [ReadOnly]
        public EntityTypeHandle entityType;
        [ReadOnly]
        public ComponentTypeHandle<GameNodeStatus> statusType;
        [ReadOnly]
        public ComponentTypeHandle<GameNodeDelay> delayType;
        [ReadOnly]
        public ComponentTypeHandle<GameNodeAngle> angleType;
        [ReadOnly]
        public ComponentTypeHandle<GameNodeActorStatus> actorStatusType;
        [ReadOnly]
        public ComponentTypeHandle<GameNodeCharacterDesiredVelocity> desiredVelocityType;
        [ReadOnly]
        public ComponentTypeHandle<GameEntityHealthData> healthDataType;
        [ReadOnly]
        public ComponentTypeHandle<GameEntityHealth> healthType;
        [ReadOnly]
        public ComponentTypeHandle<GameEntityTorpidityData> torpidityDataType;
        [ReadOnly]
        public ComponentTypeHandle<GameEntityTorpidity> torpidityType;
        [ReadOnly]
        public ComponentTypeHandle<GameEntityCommandVersion> commandVersionType;
        [ReadOnly]
        public ComponentTypeHandle<GameEntityActorTime> actorTimeType;
        [ReadOnly]
        public ComponentTypeHandle<GameEntityActorData> actorType;
        [ReadOnly]
        public ComponentTypeHandle<GameActionActiveData> instanceType;
        [ReadOnly]
        public ComponentTypeHandle<GameSpeakerInfo> speakerInfoType;
        [ReadOnly]
        public ComponentTypeHandle<GameWatcherInfo> watcherInfoType;

        [ReadOnly]
        public BufferTypeHandle<GameEntityActorActionData> actorActionType;

        [ReadOnly]
        public BufferTypeHandle<GameEntityActorActionInfo> actorActionInfoType;

        [ReadOnly]
        public BufferTypeHandle<GameActionConditionAction> conditionActionType;

        [ReadOnly]
        public BufferTypeHandle<GameActionCondition> conditionType;

        [ReadOnly]
        public BufferTypeHandle<GameActionGroup> groupType;

        public BufferTypeHandle<GameNodePosition> positionType;

        public ComponentTypeHandle<GameNodeDirection> directionType;

        public ComponentTypeHandle<GameActionActiveInfo> infoType;
        
        public ComponentTypeHandle<GameNavMeshAgentTarget> targetType;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<GameEntityActionCommand> commands;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<GameNodeVersion> versions;

        public EntityAddDataQueue.ParallelWriter addComponentCommander;
        public EntityCommandQueue<EntityCommandStructChange>.ParallelWriter removeComponentCommander;

        public bool Create(
            int index, 
            in ArchetypeChunk chunk,
            out Executor executor)
        {
            long hash = math.aslong((double)time);
            executor.collisionTolerance = collisionTolerance;
            executor.time = time;
            executor.random = new Random((uint)(hash ^ hash >> 32) ^ (uint)index);
            executor.actions = actions;
            executor.actionColliders = actionColliders;
            executor.disabled = disabled;
            executor.translations = translations;
            executor.rotations = rotations;
            executor.physicsMassMap = physicsMasses;
            executor.physicsColliders = physicsColliders;
            executor.protectedTimes = protectedTimes;
            executor.entityArray = chunk.GetNativeArray(entityType);
            executor.states = chunk.GetNativeArray(ref statusType);
            executor.delay = chunk.GetNativeArray(ref delayType);
            executor.angles = chunk.GetNativeArray(ref angleType);
            executor.actorStates = chunk.GetNativeArray(ref actorStatusType);
            executor.desiredVelocities = chunk.GetNativeArray(ref desiredVelocityType);
            executor.healthData = chunk.GetNativeArray(ref healthDataType);
            executor.healthes = chunk.GetNativeArray(ref healthType);
            executor.torpidityData = chunk.GetNativeArray(ref torpidityDataType);
            executor.torpidities = chunk.GetNativeArray(ref torpidityType);
            executor.commandVersions = chunk.GetNativeArray(ref commandVersionType);
            executor.actorTimes = chunk.GetNativeArray(ref actorTimeType);
            executor.actors = chunk.GetNativeArray(ref actorType);
            executor.instances = chunk.GetNativeArray(ref instanceType);
            executor.speakerInfos = chunk.GetNativeArray(ref speakerInfoType);
            executor.watcherInfos = chunk.GetNativeArray(ref watcherInfoType);
            executor.actorActions = chunk.GetBufferAccessor(ref actorActionType);
            executor.actorActionInfos = chunk.GetBufferAccessor(ref actorActionInfoType);
            executor.conditionActions = chunk.GetBufferAccessor(ref conditionActionType);
            executor.conditions = chunk.GetBufferAccessor(ref conditionType);
            executor.groups = chunk.GetBufferAccessor(ref groupType);
            executor.positions = chunk.GetBufferAccessor(ref positionType);
            executor.directions = chunk.GetNativeArray(ref directionType);
            executor.targets = chunk.GetNativeArray(ref targetType);
            executor.infos = chunk.GetNativeArray(ref infoType);
            executor.commands = commands;
            executor.versions = versions;
            executor.addComponentCommander = addComponentCommander;
            executor.removeComponentCommander = removeComponentCommander;

            return true;
        }
    }

    public float collisionTolerance = 0.1f;

    private GameSyncTime __time;
    private EntityAddDataPool __addComponentPool;
    private EntityAddDataQueue.ParallelWriter __addComponentCommander;
    private EntityCommandPool<EntityCommandStructChange> __removeComponentPool;
    private EntityCommandQueue<EntityCommandStructChange>.ParallelWriter __removeComponentCommander;
    private SingletonAssetContainer<BlobAssetReference<Collider>> __actionColliders;

    public override IEnumerable<EntityQueryDesc> runEntityArchetypeQueries => __runEntityArchetypeQueries;
    
    protected override void OnCreate()
    {
        base.OnCreate();

        __time = new GameSyncTime(ref this.GetState());

        World world = World;

        ref var endFrameBarrier = ref world.GetOrCreateSystemUnmanaged<GameActionStructChangeSystem>();

        __addComponentPool = endFrameBarrier.addDataCommander;
        __removeComponentPool = endFrameBarrier.manager.removeComponentPool;

        __actionColliders = SingletonAssetContainer<BlobAssetReference<Collider>>.instance;
    }

    protected override void OnUpdate()
    {
        if (!SystemAPI.HasSingleton<GameActionSetData>())
            return;

        var addComponentQueue = __addComponentPool.Create();
        var removeComponentQueue = __removeComponentPool.Create();

        __addComponentCommander = addComponentQueue.AsComponentParallelWriter<GameNavMeshAgentTarget>(exitGroup.CalculateEntityCount() + runGroup.CalculateEntityCount());
        __removeComponentCommander = removeComponentQueue.parallelWriter;

        base.OnUpdate();

        var jobHandle = Dependency;

        addComponentQueue.AddJobHandleForProducer<GameActionActiveExecutorSystem>(jobHandle);
        removeComponentQueue.AddJobHandleForProducer<GameActionActiveExecutorSystem>(jobHandle);

        __actionColliders.AddDependency(this.GetState().GetSystemID(), jobHandle);
    }

    protected override EscaperFactory _GetExit(ref JobHandle inputDeps)
    {
        EscaperFactory escaperFactory;
        //escaperFactory.time = __syncSystemGroup.nextTime;
        escaperFactory.entityType = GetEntityTypeHandle();
        escaperFactory.positionType = GetBufferTypeHandle<GameNodePosition>(true);
        escaperFactory.infoType = GetComponentTypeHandle<GameActionActiveInfo>(true);
        escaperFactory.targetType = GetComponentTypeHandle<GameNavMeshAgentTarget>();
        escaperFactory.entityManager = __addComponentCommander;
        return escaperFactory;
    }

    protected override ExecutorFactory _GetRun(ref JobHandle inputDeps)
    {
        ExecutorFactory executorFactory;
        executorFactory.collisionTolerance = collisionTolerance;
        executorFactory.time = __time.nextTime;
        executorFactory.actions = SystemAPI.GetSingleton<GameActionSetData>().definition;
        executorFactory.actionColliders = __actionColliders.reader;
        executorFactory.disabled = GetComponentLookup<Disabled>(true);
        executorFactory.translations = GetComponentLookup<Translation>(true);
        executorFactory.rotations = GetComponentLookup<Rotation>(true);
        executorFactory.physicsMasses = GetComponentLookup<PhysicsMass>(true);
        executorFactory.physicsColliders = GetComponentLookup<PhysicsCollider>(true);
        executorFactory.protectedTimes = GetComponentLookup<GameActionActiveProtectedTime>(true);
        executorFactory.entityType = GetEntityTypeHandle();
        executorFactory.statusType = GetComponentTypeHandle<GameNodeStatus>(true);
        executorFactory.delayType = GetComponentTypeHandle<GameNodeDelay>(true);
        executorFactory.angleType = GetComponentTypeHandle<GameNodeAngle>(true);
        executorFactory.actorStatusType = GetComponentTypeHandle<GameNodeActorStatus>(true);
        executorFactory.desiredVelocityType = GetComponentTypeHandle<GameNodeCharacterDesiredVelocity>(true);
        executorFactory.angleType = GetComponentTypeHandle<GameNodeAngle>(true);
        executorFactory.healthDataType = GetComponentTypeHandle<GameEntityHealthData>(true);
        executorFactory.healthType = GetComponentTypeHandle<GameEntityHealth>(true);
        executorFactory.torpidityDataType = GetComponentTypeHandle<GameEntityTorpidityData>(true);
        executorFactory.torpidityType = GetComponentTypeHandle<GameEntityTorpidity>(true);
        executorFactory.commandVersionType = GetComponentTypeHandle<GameEntityCommandVersion>(true);
        executorFactory.actorTimeType = GetComponentTypeHandle<GameEntityActorTime>(true);
        executorFactory.actorType = GetComponentTypeHandle<GameEntityActorData>(true);
        executorFactory.instanceType = GetComponentTypeHandle<GameActionActiveData>(true);
        executorFactory.speakerInfoType = GetComponentTypeHandle<GameSpeakerInfo>(true);
        executorFactory.watcherInfoType = GetComponentTypeHandle<GameWatcherInfo>(true);
        executorFactory.actorActionType = GetBufferTypeHandle<GameEntityActorActionData>(true);
        executorFactory.actorActionInfoType = GetBufferTypeHandle<GameEntityActorActionInfo>(true);
        executorFactory.conditionActionType = GetBufferTypeHandle<GameActionConditionAction>(true);
        executorFactory.conditionType = GetBufferTypeHandle<GameActionCondition>(true);
        executorFactory.groupType = GetBufferTypeHandle<GameActionGroup>(true);
        executorFactory.positionType = GetBufferTypeHandle<GameNodePosition>();
        executorFactory.directionType = GetComponentTypeHandle<GameNodeDirection>();
        executorFactory.targetType = GetComponentTypeHandle<GameNavMeshAgentTarget>();
        executorFactory.infoType = GetComponentTypeHandle<GameActionActiveInfo>();
        executorFactory.commands = GetComponentLookup<GameEntityActionCommand>();
        executorFactory.versions = GetComponentLookup<GameNodeVersion>();
        executorFactory.addComponentCommander = __addComponentCommander;
        executorFactory.removeComponentCommander = __removeComponentCommander;
        return executorFactory;
    }
    
    private readonly EntityQueryDesc[] __runEntityArchetypeQueries = new EntityQueryDesc[]
    {
        new EntityQueryDesc()
        {
            All = new ComponentType[]
            {
                ComponentType.ReadOnly<GameNodeStatus>(),
                ComponentType.ReadOnly<GameNodeDelay>(),
                /*ComponentType.ReadOnly<GameEntityHealthData>(),
                ComponentType.ReadOnly<GameEntityHealth>(),
                ComponentType.ReadOnly<GameEntityTorpidityData>(),
                ComponentType.ReadOnly<GameEntityTorpidity>(),*/
                ComponentType.ReadOnly<GameEntityCommandVersion>(),
                ComponentType.ReadOnly<GameEntityActorData>(),
                ComponentType.ReadOnly<GameActionActiveData>(),
                ComponentType.ReadOnly<GameEntityActorActionData>(),
                ComponentType.ReadOnly<GameEntityActorActionInfo>(),
                ComponentType.ReadWrite<GameActionActiveInfo>()
            },
            Any = new ComponentType[]
            {
                ComponentType.ReadOnly<GameSpeakerInfo>(),
                ComponentType.ReadWrite<GameWatcherInfo>()
            },
            None = new ComponentType[]
            {
                typeof(Disabled)
            }
        }
    };
}