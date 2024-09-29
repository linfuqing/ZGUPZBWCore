using System;
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

[assembly: RegisterGenericJobType(typeof(StateMachineExitJob<
    GameActionActiveSystem.Exit, 
    GameActionActiveSystem.FactoryExit>))]
[assembly: RegisterGenericJobType(typeof(StateMachineEntryJob<
    GameActionActiveSystem.Entry, 
    GameActionActiveSystem.FactoryEntry>))]
[assembly: RegisterGenericJobType(typeof(StateMachineRunJob<
    GameActionActiveSystem.Run, 
    GameActionActiveSystem.FactoryRun>))]

[Flags]
public enum GameActionActiveFlag
{
    [Tooltip("攻击之后逃跑。")]
    Sly = 0x01,
    [Tooltip("仅攻击可视范围内的敌人。")]
    Timid = 0x02,
}

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

    [Tooltip("主动攻击的优先级（有GameWatcherComponent生效）")]
    public int watcherPriority = 1;

    [Tooltip("被打之后反击优先级（有GameSpeakerComponent生效）")]
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
    public float maxAlertDistance = 10.0f;

    [Tooltip("最小环绕距离")]
    public float minAlertDistance = 0.0f;

    [Tooltip("最大环绕时间")]
    public float maxAlertTime = 10.0f;

    [Tooltip("最小环绕时间")]
    public float minAlertTime = 3.0f;

    [Tooltip("环绕概率")]
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
        instance.RemoveComponent<GameActionActiveInfo>();
        //instance.RemoveComponent<GameActionActiveInfo>();
    }
}

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
    //public float watcherTime;
    public double activeTime;
    public double alertTime;
    public float3 position;
    public Entity entity;
}

[BurstCompile, UpdateInGroup(typeof(StateMachineGroup)), UpdateAfter(typeof(GameActionNormalSystem))]
public partial struct GameActionActiveSystem : ISystem
{
    public struct Entry : IStateMachineCondition
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
            in SystemHandle runningSystemHandle,
            in SystemHandle nextSystemHandle,
            in SystemHandle currentSystemHandle,
            int index)
        {
            if (currentSystemHandle == runningSystemHandle)
                return false;

            var instance = instances[index];

            float distanceSq = instance.maxDistance * instance.maxDistance;
            float3 position = translations[index].Value;
            var info = infos[index];

            if (runningStatus < instance.speakerPriority && instance.speakerTime > math.FLT_MIN_NORMAL && speakerInfos.Length > index)
            {
                var speakerInfo = speakerInfos[index];
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
                var watcherInfo = watcherInfos[index];
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

    public struct FactoryEntry : IStateMachineFactory<Entry>
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

        public bool Create(int unfilteredChunkIndex, in ArchetypeChunk chunk, out Entry entry)
        {
            entry.time = time;
            entry.disabled = disabled;
            entry.translationMap = translations;
            entry.translations = chunk.GetNativeArray(ref translationType);
            entry.watcherInfos = chunk.GetNativeArray(ref watcherInfoType);
            entry.speakerInfos = chunk.GetNativeArray(ref speakerInfoType);
            entry.instances = chunk.GetNativeArray(ref instanceType);
            entry.infos = chunk.GetNativeArray(ref infoType);

            return true;
        }
    }

    public struct Exit : IStateMachineCondition
    {
        public bool hasPositions;

        public ArchetypeChunk chunk;

        public ComponentTypeHandle<GameNavMeshAgentTarget> targetType;

        [ReadOnly]
        public NativeArray<GameActionActiveInfo> infos;
        
        public NativeArray<GameNavMeshAgentTarget> targets;

        public bool Execute(
            int runningStatus,
            in SystemHandle runningSystemHandle,
            in SystemHandle nextSystemHandle,
            in SystemHandle currentSystemHandle,
            int index)
        {
            if (hasPositions && nextSystemHandle != SystemHandle.Null && index < infos.Length)
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

    public struct FactoryExit : IStateMachineFactory<Exit>
    {
        [ReadOnly]
        public BufferTypeHandle<GameNodePosition> positionType;

        [ReadOnly]
        public ComponentTypeHandle<GameActionActiveInfo> infoType;

        public ComponentTypeHandle<GameNavMeshAgentTarget> targetType;

        public bool Create(
            int unfilteredChunkIndex, 
            in ArchetypeChunk chunk, 
            out Exit exit)
        {
            exit.hasPositions = chunk.Has(ref positionType);
            exit.chunk = chunk;
            exit.targetType = targetType;
            exit.infos = chunk.GetNativeArray(ref infoType);
            exit.targets = chunk.GetNativeArray(ref targetType);
            
            return true;
        }
    }

    public struct Run : IStateMachineExecutor
    {
        public float collisionTolerance;
        public GameTime time;

        public Random random;

        public ArchetypeChunk chunk;

        public ComponentTypeHandle<GameNavMeshAgentTarget> targetType;

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
        public NativeArray<GameOwner> owners;

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

        //public EntityAddDataQueue.ParallelWriter addComponentCommander;
        //public EntityCommandQueue<EntityCommandStructChange>.ParallelWriter removeComponentCommander;

        public static unsafe bool IsDo(
            float collisionTolerance,
            float distance,
            float sourceScale,
            in float3 offset,
            in RigidTransform transform,
            in BlobAssetReference<Collider> sourceCollider,
            Collider* destinationCollider)
        {
            float3 position = math.transform(transform, offset);

            if (sourceCollider.IsCreated && destinationCollider != null)
            {
                PointDistanceInput pointDistanceInput = default;
                pointDistanceInput.MaxDistance = float.MaxValue;
                pointDistanceInput.Position = position;
                pointDistanceInput.Filter = sourceCollider.Value.Filter;
                if (destinationCollider->CalculateDistance(pointDistanceInput, out var closestHit))
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

                return destinationCollider->CalculateDistance(colliderDistanceInput);
            }

            return math.lengthsq(position) <= distance * distance;
        }

        public bool IsVail(in Entity entity)
        {
            return !disabled.HasComponent(entity) && translations.HasComponent(entity) && rotations.HasComponent(entity);
        }

        public unsafe int Execute(bool isEntry, int index)
        {
            var instance = instances[index];
            if ((states[index].value & GameNodeStatus.STOP) == GameNodeStatus.STOP)
                return 0;

            Entity entity = entityArray[index];
            if (!translations.HasComponent(entity))
                return 0;

            float3 source = translations[entity].Value;

            var info = infos[index];

            bool isVail = false, isWatch = true;
            Entity speakerEntity = Entity.Null;
            if (speakerInfos.Length > index)
            {
                var speakerInfo = speakerInfos[index];
                speakerEntity = speakerInfo.target;
                double speakerTime = speakerInfo.time + instance.speakerTime;
                if (time < speakerTime && IsVail(speakerEntity))
                {
                    isVail = true;
                    isWatch = false;

                    if (info.activeTime < speakerTime)
                    {
                        info.activeTime = speakerTime;
                        info.position = source;
                        info.entity = speakerInfo.target;
                    }
                }
            }

            if (isWatch && watcherInfos.Length > index)
            {
                var watcherInfo = watcherInfos[index];
                if (watcherInfo.target != info.entity && IsVail(watcherInfo.target))
                {
                    isVail = true;
                    info.entity = watcherInfo.target;

                    isEntry = true;
                }
            }

            if (!isVail && !IsVail(info.entity))
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
                    //command.time = time;
                    command.entity = info.entity;
                    command.forward = forward / distance;
                    command.distance = float3.zero;
                    //command.offset = float3.zero;

                    commands[entity] = command;
                    commands.SetComponentEnabled(entity, true);
                }

                return instance.watcherPriority;
            }

            /*if (index < owners.Length && owners[index].entity != Entity.Null)
                instance.maxDistance = 15.0f;*/
            
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

                    if (info.entity == speakerEntity/* || 
                        index < owners.Length && 
                        owners[index].entity != Entity.Null*/)
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
                forward = distance > math.FLT_MIN_NORMAL ? forward / distance : forward;

                //var collider = physicsColliders[info.entity].Value;
                Collider* collider;
                if (physicsColliders.HasComponent(info.entity))
                {
                    ChildCollider child;
                    collider = physicsColliders[info.entity].ColliderPtr;
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
                    //categoryBits = physicsColliders[info.entity].Value.Value.Filter.BelongsTo;
                }
                else
                    collider = null;

                bool isRunAway, hasPosition = index < positions.Length;//, isDelay = delay[index].Check(time);
                uint categoryBits = physicsColliders[info.entity].Value.Value.Filter.BelongsTo;
                int maxHealth = index < healthes.Length && index < healthData.Length ? healthData[index].max : 0, 
                    maxTorpidity = index < torpidities.Length && index < torpidityData.Length ? torpidityData[index].max : 0;
                float health = maxHealth < 1 ? 1.0f : healthes[index].value / maxHealth, 
                    torpidity = maxTorpidity < 1 ? 1.0f : torpidities[index].value / maxTorpidity;
                if ((instance.layerMask == 0 || (instance.layerMask & categoryBits) == 0) &&
                    (!hasPosition || 
                    health > instance.health &&
                    torpidity > instance.torpidity))
                {
                    if (!isWatch && (instance.flag & GameActionActiveFlag.Sly) == GameActionActiveFlag.Sly)
                        isRunAway = hasPosition;
                    else if (!isWatch || !protectedTimes.HasComponent(info.entity) || protectedTimes[info.entity].value < time)
                    {
                        if (actorTimes[index].value >= time || commands.IsComponentEnabled(entity))
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
                                if (info.groupMask == 0/* || !isDelay*/)
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
                                        //command.time = time;
                                        command.entity = info.entity;
                                        command.forward = forward;
                                        command.distance = float3.zero;
                                        //command.offset = float3.zero;
                                        commands[entity] = command;

                                        commands.SetComponentEnabled(entity, true);

                                        hasPosition = false;
                                    }
                                    else
                                        info.groupMask = 0;
                                }

                                /*if(info.groupMask == 0 && isHasPosition)
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
                                }*/
                            }
                            else
                            {
                                ref var actions = ref this.actions.Value;
                                SingletonAssetContainerHandle handle;
                                handle.instanceID = actions.instanceID;

                                var actorActions = this.actorActions[index];
                                int numActorActions = actorActions.Length, numActorActionInfos = actorActionInfos.Length, i;
                                double coolDownTime;
                                RigidTransform sourceTransform = math.RigidTransform(quaternion.LookRotationSafe(forward, math.up()), source),
                                    destinationTransform = math.RigidTransform(targetRotation, destination),
                                    transform = math.mul(math.inverse(destinationTransform), sourceTransform);
                                //var collider = physicsColliders[info.entity].Value;
                                var actor = actors[index];
                                for (i = 0; i < numActorActions; ++i)
                                {
                                    ref var action = ref actions.values[actorActions[i].actionIndex];
                                    if ((action.instance.damageType & GameActionTargetType.Enemy) == GameActionTargetType.Enemy &&
                                        (action.instance.damageMask & categoryBits) != 0 &&
                                        action.colliderIndex != -1)
                                    {
                                        handle.index = action.colliderIndex;

                                        coolDownTime = i < numActorActionInfos
                                            ? actorActionInfos[i].coolDownTime
                                            : 0.0f;
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
                                            //command.time = time;
                                            command.entity = info.entity;
                                            command.forward = forward;
                                            command.distance = float3.zero;
                                            //command.offset = float3.zero;
                                            commands[entity] = command;

                                            commands.SetComponentEnabled(entity, true);

                                            hasPosition = false;

                                            break;
                                        }
                                    }
                                }
                            }

                            if (hasPosition)
                            {
                                if (/*info.status == GameActionActiveInfo.Status.Forward || */distance > instance.maxAlertDistance/* || dot < 0.0f*/)
                                {
                                    isRunAway = (instance.flag & GameActionActiveFlag.Timid) == GameActionActiveFlag.Timid;
                                    if (!isRunAway)
                                    {
                                        GameNavMeshAgentTarget target;
                                        target.sourceAreaMask = -1;
                                        target.destinationAreaMask = -1;
                                        target.position = source + forward * distance;
                                        if (index < targets.Length)
                                        {
                                            targets[index] = target;

                                            chunk.SetComponentEnabled(ref targetType, index, true);
                                        }
                                        /*else// if (!isDelay)
                                            addComponentCommander.AddComponentData(entity, target);*/
                                    }
                                }
                                else
                                {
                                    if (info.alertTime < time || distance < instance.minAlertDistance || distance > instance.maxAlertDistance)
                                    {
                                        info.alertTime = time + random.NextFloat(instance.minAlertTime, instance.maxAlertTime);

                                        info.status = distance < instance.minAlertDistance ? GameActionActiveInfo.Status.Backward : 
                                            (distance < instance.maxAlertDistance && instance.alertChance > random.NextFloat() ?
                                            (GameActionActiveInfo.Status)(int)math.sign(random.NextFloat() - 0.5f) : GameActionActiveInfo.Status.Forward);
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

                                            value = right * (int)info.status;
                                            break;
                                    }

                                    if (math.dot(math.normalizesafe(direction.value), value) < instance.dot)
                                    {
                                        var version = versions[entity];
                                        version.type = GameNodeVersion.Type.Direction;
                                        ++version.value;
                                        versions[entity] = version;
                                        versions.SetComponentEnabled(entity, true);

                                        //direction.mode = isHasAngle && math.dot(value, surfaceForward) < 0.0f ? GameNodeDirection.Mode.Backward : GameNodeDirection.Mode.Forward;
                                        direction.version = version.value;
                                        direction.value = value * instance.alertSpeedScale;
                                        directions[index] = direction;

                                        positions[index].Clear();

                                        if (index < targets.Length)
                                        {
                                            chunk.SetComponentEnabled(ref targetType, index, false);
                                            /*EntityCommandStructChange targetCommand;
                                            targetCommand.entity = entity;
                                            targetCommand.componentType = ComponentType.ReadWrite<GameNavMeshAgentTarget>();
                                            removeComponentCommander.Enqueue(targetCommand);*/
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
                        isRunAway = hasPosition && (instance.flag & GameActionActiveFlag.Timid) == GameActionActiveFlag.Timid;
                }
                else
                    isRunAway = hasPosition;

                if (isRunAway && index < directions.Length && !delay[index].Check(time)/*!isDelay*/)
                {
                    float3 value = -forward;
                    var direction = directions[index];
                    if (direction.mode != GameNodeDirection.Mode.Forward || 
                        math.dot(math.normalizesafe(direction.value), value) < instance.dot)
                    {
                        var version = versions[entity];
                        version.type = GameNodeVersion.Type.Direction;
                        ++version.value;
                        versions[entity] = version;
                        versions.SetComponentEnabled(entity, true);

                        direction.mode = GameNodeDirection.Mode.Forward;
                        direction.version = version.value;
                        direction.value = value;
                        directions[index] = direction;

                        if (hasPosition)
                            positions[index].Clear();

                        if (index < targets.Length)
                        {
                            chunk.SetComponentEnabled(ref targetType, index, false);
                            /*EntityCommandStructChange targetCommand;
                            targetCommand.entity = entity;
                            targetCommand.componentType = ComponentType.ReadWrite<GameNavMeshAgentTarget>();
                            removeComponentCommander.Enqueue(targetCommand);*/
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

    public struct FactoryRun : IStateMachineFactory<Run>
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
        public ComponentTypeHandle<GameOwner> ownerType;
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

        //public EntityAddDataQueue.ParallelWriter addComponentCommander;
        //public EntityCommandQueue<EntityCommandStructChange>.ParallelWriter removeComponentCommander;

        public bool Create(
            int unfilteredChunkIndex, 
            in ArchetypeChunk chunk, 
            out Run run)
        {
            run.collisionTolerance = collisionTolerance;
            run.time = time;
            run.random = new Random(RandomUtility.Hash(time) ^ (uint)unfilteredChunkIndex);
            run.chunk = chunk;
            run.targetType = targetType;
            run.actions = actions;
            run.actionColliders = actionColliders;
            run.disabled = disabled;
            run.translations = translations;
            run.rotations = rotations;
            run.physicsMassMap = physicsMasses;
            run.physicsColliders = physicsColliders;
            run.protectedTimes = protectedTimes;
            run.entityArray = chunk.GetNativeArray(entityType);
            run.owners = chunk.GetNativeArray(ref ownerType);
            run.states = chunk.GetNativeArray(ref statusType);
            run.delay = chunk.GetNativeArray(ref delayType);
            run.angles = chunk.GetNativeArray(ref angleType);
            run.actorStates = chunk.GetNativeArray(ref actorStatusType);
            run.desiredVelocities = chunk.GetNativeArray(ref desiredVelocityType);
            run.healthData = chunk.GetNativeArray(ref healthDataType);
            run.healthes = chunk.GetNativeArray(ref healthType);
            run.torpidityData = chunk.GetNativeArray(ref torpidityDataType);
            run.torpidities = chunk.GetNativeArray(ref torpidityType);
            run.commandVersions = chunk.GetNativeArray(ref commandVersionType);
            run.actorTimes = chunk.GetNativeArray(ref actorTimeType);
            run.actors = chunk.GetNativeArray(ref actorType);
            run.instances = chunk.GetNativeArray(ref instanceType);
            run.speakerInfos = chunk.GetNativeArray(ref speakerInfoType);
            run.watcherInfos = chunk.GetNativeArray(ref watcherInfoType);
            run.actorActions = chunk.GetBufferAccessor(ref actorActionType);
            run.actorActionInfos = chunk.GetBufferAccessor(ref actorActionInfoType);
            run.conditionActions = chunk.GetBufferAccessor(ref conditionActionType);
            run.conditions = chunk.GetBufferAccessor(ref conditionType);
            run.groups = chunk.GetBufferAccessor(ref groupType);
            run.positions = chunk.GetBufferAccessor(ref positionType);
            run.directions = chunk.GetNativeArray(ref directionType);
            run.targets = chunk.GetNativeArray(ref targetType);
            run.infos = chunk.GetNativeArray(ref infoType);
            run.commands = commands;
            run.versions = versions;
            //executor.addComponentCommander = addComponentCommander;
            //executor.removeComponentCommander = removeComponentCommander;

            return true;
        }
    }

    public static readonly float CollisionTolerance = 0.1f;

    private GameSyncTime __time;

    private ComponentLookup<Disabled> __disabled;

    private ComponentLookup<Translation> __translations;

    private ComponentLookup<Rotation> __rotations;

    private ComponentLookup<PhysicsMass> __physicsMasses;

    private ComponentLookup<PhysicsCollider> __physicsColliders;

    private ComponentLookup<GameActionActiveProtectedTime> __protectedTimes;

    private EntityTypeHandle __entityType;

    private ComponentTypeHandle<Translation> __translationType;

    private ComponentTypeHandle<GameOwner> __ownerType;

    private ComponentTypeHandle<GameNodeStatus> __statusType;
    private ComponentTypeHandle<GameNodeDelay> __delayType;
    private ComponentTypeHandle<GameNodeAngle> __angleType;
    private ComponentTypeHandle<GameNodeActorStatus> __actorStatusType;
    private ComponentTypeHandle<GameNodeCharacterDesiredVelocity> __desiredVelocityType;
    private ComponentTypeHandle<GameEntityHealthData> __healthDataType;
    private ComponentTypeHandle<GameEntityHealth> __healthType;
    private ComponentTypeHandle<GameEntityTorpidityData> __torpidityDataType;
    private ComponentTypeHandle<GameEntityTorpidity> __torpidityType;
    private ComponentTypeHandle<GameEntityCommandVersion> __commandVersionType;
    private ComponentTypeHandle<GameEntityActorTime> __actorTimeType;
    private ComponentTypeHandle<GameEntityActorData> __actorType;
    private ComponentTypeHandle<GameActionActiveData> __instanceType;
    private ComponentTypeHandle<GameSpeakerInfo> __speakerInfoType;
    private ComponentTypeHandle<GameWatcherInfo> __watcherInfoType;

    private BufferTypeHandle<GameEntityActorActionData> __actorActionType;

    private BufferTypeHandle<GameEntityActorActionInfo> __actorActionInfoType;

    private BufferTypeHandle<GameActionConditionAction> __conditionActionType;

    private BufferTypeHandle<GameActionCondition> __conditionType;

    private BufferTypeHandle<GameActionGroup> __groupType;

    private BufferTypeHandle<GameNodePosition> __positionType;

    private ComponentTypeHandle<GameNodeDirection> __directionType;

    private ComponentTypeHandle<GameActionActiveInfo> __infoType;

    private ComponentTypeHandle<GameNavMeshAgentTarget> __targetType;

    private ComponentLookup<GameEntityActionCommand> __commands;

    private ComponentLookup<GameNodeVersion> __versions;

    private SingletonAssetContainer<BlobAssetReference<Collider>> __actionColliders;

    private StateMachineSystemCore __core;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __time = new GameSyncTime(ref state);

        __disabled = state.GetComponentLookup<Disabled>(true);
        __translations = state.GetComponentLookup<Translation>(true);
        __rotations = state.GetComponentLookup<Rotation>(true);
        __physicsMasses = state.GetComponentLookup<PhysicsMass>(true);
        __physicsColliders = state.GetComponentLookup<PhysicsCollider>(true);
        __protectedTimes = state.GetComponentLookup<GameActionActiveProtectedTime>(true);
        __entityType = state.GetEntityTypeHandle();
        __translationType = state.GetComponentTypeHandle<Translation>(true);
        __ownerType = state.GetComponentTypeHandle<GameOwner>(true);
        __statusType = state.GetComponentTypeHandle<GameNodeStatus>(true);
        __delayType = state.GetComponentTypeHandle<GameNodeDelay>(true);
        __angleType = state.GetComponentTypeHandle<GameNodeAngle>(true);
        __actorStatusType = state.GetComponentTypeHandle<GameNodeActorStatus>(true);
        __desiredVelocityType = state.GetComponentTypeHandle<GameNodeCharacterDesiredVelocity>(true);
        __angleType = state.GetComponentTypeHandle<GameNodeAngle>(true);
        __healthDataType = state.GetComponentTypeHandle<GameEntityHealthData>(true);
        __healthType = state.GetComponentTypeHandle<GameEntityHealth>(true);
        __torpidityDataType = state.GetComponentTypeHandle<GameEntityTorpidityData>(true);
        __torpidityType = state.GetComponentTypeHandle<GameEntityTorpidity>(true);
        __commandVersionType = state.GetComponentTypeHandle<GameEntityCommandVersion>(true);
        __actorTimeType = state.GetComponentTypeHandle<GameEntityActorTime>(true);
        __actorType = state.GetComponentTypeHandle<GameEntityActorData>(true);
        __instanceType = state.GetComponentTypeHandle<GameActionActiveData>(true);
        __speakerInfoType = state.GetComponentTypeHandle<GameSpeakerInfo>(true);
        __watcherInfoType = state.GetComponentTypeHandle<GameWatcherInfo>(true);
        __actorActionType = state.GetBufferTypeHandle<GameEntityActorActionData>(true);
        __actorActionInfoType = state.GetBufferTypeHandle<GameEntityActorActionInfo>(true);
        __conditionActionType = state.GetBufferTypeHandle<GameActionConditionAction>(true);
        __conditionType = state.GetBufferTypeHandle<GameActionCondition>(true);
        __groupType = state.GetBufferTypeHandle<GameActionGroup>(true);
        __positionType = state.GetBufferTypeHandle<GameNodePosition>();
        __directionType = state.GetComponentTypeHandle<GameNodeDirection>();
        __targetType = state.GetComponentTypeHandle<GameNavMeshAgentTarget>();
        __infoType = state.GetComponentTypeHandle<GameActionActiveInfo>();
        __commands = state.GetComponentLookup<GameEntityActionCommand>();
        __versions = state.GetComponentLookup<GameNodeVersion>();

        __actionColliders = SingletonAssetContainer<BlobAssetReference<Collider>>.Retain();

        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __core = new StateMachineSystemCore(
                ref state, 
                builder
                    .WithAll<GameNodeStatus, GameNodeDelay, GameEntityCommandVersion, GameEntityActorData, GameActionActiveData, GameEntityActorActionData, GameEntityActorActionInfo>()
                    .WithAllRW<GameActionActiveInfo>()
                    .WithAny<GameSpeakerInfo, GameWatcherInfo>());
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        __actionColliders.Release();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.HasSingleton<GameActionSetData>())
            return;
        
        var nextTime = __time.nextTime;
        var disabled = __disabled.UpdateAsRef(ref state);
        var translations = __translations.UpdateAsRef(ref state);
        var positionType = __positionType.UpdateAsRef(ref state);
        var targetType = __targetType.UpdateAsRef(ref state);
        var watcherInfoType = __watcherInfoType.UpdateAsRef(ref state);
        var speakerInfoType = __speakerInfoType.UpdateAsRef(ref state);
        var instanceType = __instanceType.UpdateAsRef(ref state);
        var infoType = __infoType.UpdateAsRef(ref state);

        FactoryRun factoryRun;
        factoryRun.collisionTolerance = CollisionTolerance;
        factoryRun.time = nextTime;
        factoryRun.actions = SystemAPI.GetSingleton<GameActionSetData>().definition;
        factoryRun.actionColliders = __actionColliders.reader;
        factoryRun.disabled = disabled;
        factoryRun.translations = __translations.UpdateAsRef(ref state);
        factoryRun.rotations = __rotations.UpdateAsRef(ref state);
        factoryRun.physicsMasses = __physicsMasses.UpdateAsRef(ref state);
        factoryRun.physicsColliders = __physicsColliders.UpdateAsRef(ref state);
        factoryRun.protectedTimes = __protectedTimes.UpdateAsRef(ref state);
        factoryRun.entityType = __entityType.UpdateAsRef(ref state);
        factoryRun.ownerType = __ownerType.UpdateAsRef(ref state);
        factoryRun.statusType = __statusType.UpdateAsRef(ref state);
        factoryRun.delayType = __delayType.UpdateAsRef(ref state);
        factoryRun.angleType = __angleType.UpdateAsRef(ref state);
        factoryRun.actorStatusType = __actorStatusType.UpdateAsRef(ref state);
        factoryRun.desiredVelocityType = __desiredVelocityType.UpdateAsRef(ref state);
        factoryRun.angleType = __angleType.UpdateAsRef(ref state);
        factoryRun.healthDataType = __healthDataType.UpdateAsRef(ref state);
        factoryRun.healthType = __healthType.UpdateAsRef(ref state);
        factoryRun.torpidityDataType = __torpidityDataType.UpdateAsRef(ref state);
        factoryRun.torpidityType = __torpidityType.UpdateAsRef(ref state);
        factoryRun.commandVersionType = __commandVersionType.UpdateAsRef(ref state);
        factoryRun.actorTimeType = __actorTimeType.UpdateAsRef(ref state);
        factoryRun.actorType = __actorType.UpdateAsRef(ref state);
        factoryRun.instanceType = instanceType;
        factoryRun.speakerInfoType = speakerInfoType;
        factoryRun.watcherInfoType = watcherInfoType;
        factoryRun.actorActionType = __actorActionType.UpdateAsRef(ref state);
        factoryRun.actorActionInfoType = __actorActionInfoType.UpdateAsRef(ref state);
        factoryRun.conditionActionType = __conditionActionType.UpdateAsRef(ref state);
        factoryRun.conditionType = __conditionType.UpdateAsRef(ref state);
        factoryRun.groupType = __groupType.UpdateAsRef(ref state);
        factoryRun.positionType = positionType;
        factoryRun.directionType = __directionType.UpdateAsRef(ref state);
        factoryRun.targetType = targetType;
        factoryRun.infoType = infoType;
        factoryRun.commands = __commands.UpdateAsRef(ref state);
        factoryRun.versions = __versions.UpdateAsRef(ref state);

        FactoryExit factoryExit;
        factoryExit.positionType = positionType;
        factoryExit.infoType = infoType;
        factoryExit.targetType = targetType;

        FactoryEntry factoryEntry;
        factoryEntry.time = nextTime;
        factoryEntry.disabled = disabled;
        factoryEntry.translations = translations;
        factoryEntry.translationType = __translationType.UpdateAsRef(ref state);
        factoryEntry.watcherInfoType = watcherInfoType;
        factoryEntry.speakerInfoType = speakerInfoType;
        factoryEntry.instanceType = instanceType;
        factoryEntry.infoType = infoType;

        __core.Update<Exit, Entry, Run, FactoryExit, FactoryEntry, FactoryRun>(
            ref state, 
            ref factoryRun, 
            ref factoryEntry, 
            ref factoryExit);
        
        __actionColliders.AddDependency(state.GetSystemID(), state.Dependency);
    }
}