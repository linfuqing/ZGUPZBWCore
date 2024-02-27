using System;
using System.Collections.Generic;
using Unity.Jobs;
using Unity.Burst;
using Unity.Entities;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using ZG;
using Random = Unity.Mathematics.Random;

[assembly: RegisterGenericJobType(typeof(StateMachineSchedulerJob<
    GameActionNormalSchedulerSystem.SchedulerExit, 
    GameActionNormalSchedulerSystem.FactoryExit, 
    GameActionNormalSchedulerSystem.SchedulerEntry, 
    GameActionNormalSchedulerSystem.FactoryEntry>))]
[assembly: RegisterGenericJobType(typeof(StateMachineEscaperJob<GameActionNormalExecutorSystem.Escaper, GameActionNormalExecutorSystem.EscaperFactory>))]
[assembly: RegisterGenericJobType(typeof(StateMachineExecutorJob<GameActionNormalExecutorSystem.Executor, GameActionNormalExecutorSystem.ExecutorFactory>))]

[Flags]
public enum GameActionNormalFlag
{
    DelayInWater = 0x01,
    DreamInWater = 0x02
}

public class GameActionNormal : StateMachineNode
{
    public GameActionNormalFlag flag = 0;

    public int waterAreaMask = 1 << 4 | 1 << 6;

    [Tooltip("休闲索引")]
    public int delayIndex = 0;

    [Tooltip("散步速度缩放。")]
    public float speedScale = 0.2f;

    [Tooltip("散步时期待速度和实际速度的差距超过该值则掉头。")]
    public float backwardThreshold = 0.5f;

    [Tooltip("发呆概率，决定行走还是发呆。")]
    public float delayChance = 0.5f;
    [Tooltip("醒来概率，决定进行下一段休息、回到上一段休息或是醒来。")]
    public float awakeChance = 0.5f;

    [Tooltip("散步下次路点范围。")]
    public float range = 5.0f;

    [Tooltip("小于该高度则回头")]
    [Range(0.0f, 1.0f)]
    public float slope = 0.5f;

    [Tooltip("发呆超过该时间则休息。")]
    public float sleepTime = 20.0f;
    [Tooltip("休息后多久醒来时间范围。该时间结束后由概率决定进行下一段休息或者醒来。")]
    public GameActionNormalDelayTime awakeTime = new GameActionNormalDelayTime() { min = 10.0f, max = 30.0f };
    [Tooltip("发呆时间范围。")]
    public GameActionNormalDelayTime delayTime = new GameActionNormalDelayTime() { min = 5.0f, max = 10.0f };
    [Tooltip("每段休息的时间范围。")]
    public GameActionNormalDelayTime[] delayTimes = new GameActionNormalDelayTime[]
        {
            new GameActionNormalDelayTime() { min = 20.0f, max = 50.0f }, 
            new GameActionNormalDelayTime() { min = 30.0f, max = 80.0f }
        };
    
    public override void Enable(StateMachineComponentEx instance)
    {
        GameActionNormalData data;
        data.flag = flag;
        data.waterAreaMask = waterAreaMask;
        data.speedScale = (half)speedScale;
        data.backwardThreshold = backwardThreshold;
        data.delayIndex = delayIndex;
        data.delayChance = delayChance;
        data.awakeChance = awakeChance;
        data.range = range;
        data.slope = slope;
        data.sleepTime = sleepTime;
        data.awakeTime = awakeTime;
        data.delayTime = delayTime;
        instance.AddComponentData(data);
        
        instance.AddComponent<GameActionNormalInfo>();

        if (this.delayTimes != null && this.delayTimes.Length > 1)
            instance.AddBuffer(delayTimes);
    }

    public override void Disable(StateMachineComponentEx instance)
    {
        instance.RemoveComponent<GameActionNormalData>();
        //instance.RemoveComponentIfExists<GameActionNormalInfo>();
        instance.RemoveComponent<GameActionNormalDelayTime>();
    }
}

[Serializable]
public struct GameActionNormalDelayTime : IBufferElementData
{
    public float min;
    public float max;

    public bool isVail => min < max;

    public float Get(Random random)
    {
        return random.NextFloat(min, max);
    }
}

public struct GameActionNormalData : IComponentData
{
    public GameActionNormalFlag flag;

    public int waterAreaMask;

    [Tooltip("休闲索引")]
    public int delayIndex;

    [Tooltip("散步速度缩放。")]
    public half speedScale;

    [Tooltip("散步时期待速度和实际速度的差距超过该值则掉头。")]
    public float backwardThreshold;

    [Tooltip("发呆概率，决定行走还是发呆。")]
    public float delayChance;
    [Tooltip("醒来概率，决定进行下一段休息、回到上一段休息或是醒来。")]
    public float awakeChance;

    [Tooltip("散步下次路点范围。")]
    public float range;

    [Tooltip("小于该高度则回头")]
    public float slope;

    [Tooltip("发呆超过该时间则休息。")]
    public float sleepTime;
    [Tooltip("休息后多久醒来时间范围。该时间结束后由概率决定进行下一段休息或者醒来。")]
    public GameActionNormalDelayTime awakeTime;
    [Tooltip("发呆时间范围。")]
    public GameActionNormalDelayTime delayTime;
}

public struct GameActionNormalInfo : IComponentData
{
    [Flags]
    public enum Flag
    {
        MuteDelay = 0x01, 
        MuteDream = 0x02
    }

    public Flag flag;
    public half speedScale;
    public float duration;
    public float elapsedTime;
    public double time;
}

[BurstCompile, UpdateInGroup(typeof(StateMachineGroup), OrderLast = true)]
public partial struct GameActionNormalSchedulerSystem : ISystem
{
    public struct SchedulerExit : IStateMachineScheduler
    {
        [ReadOnly]
        public NativeArray<GameActionNormalInfo> infos;
        
        public BufferAccessor<GameNodeSpeedScaleComponent> speedScaleComponents;
        
        public bool Execute(
            int runningStatus,
            in SystemHandle runningSystemHandle,
            in SystemHandle nextSystemHandle,
            in SystemHandle currentSystemHandle,
            int index)
        {
            if(index < infos.Length && index < this.speedScaleComponents.Length)
            {
                var speedScaleComponents = this.speedScaleComponents[index];
                var speedScale = infos[index].speedScale;
                int length = speedScaleComponents.Length;
                for(int i = 0; i < length; ++i)
                {
                    if(speedScaleComponents[i].value == speedScale)
                    {
                        speedScaleComponents.RemoveAt(i);

                        break;
                    }
                }
            }

            return true;
        }
    }
    
    public struct FactoryExit : IStateMachineFactory<SchedulerExit>
    {
        [ReadOnly]
        public ComponentTypeHandle<GameActionNormalInfo> infoType;

        public BufferTypeHandle<GameNodeSpeedScaleComponent> speedScaleComponentType;

        public bool Create(int unfilteredChunkIndex, in ArchetypeChunk chunk, out SchedulerExit schedulerExit)
        {
            schedulerExit.infos = chunk.GetNativeArray(ref infoType);
            schedulerExit.speedScaleComponents = chunk.GetBufferAccessor(ref speedScaleComponentType);

            return true;
        }
    }
    
    public struct SchedulerEntry : IStateMachineScheduler
    {
        [WriteOnly]
        public NativeArray<GameActionNormalInfo> infos;

        public bool Execute(
            int runningStatus,
            in SystemHandle runningSystemHandle,
            in SystemHandle nextSystemHandle,
            in SystemHandle currentSystemHandle,
            int index)
        {
            if (runningStatus > 0)
                return false;

            if (currentSystemHandle == runningSystemHandle || runningSystemHandle != nextSystemHandle && nextSystemHandle != SystemHandle.Null)
                return false;

            GameActionNormalInfo info;
            info.flag = 0;
            info.speedScale = (half)1.0f;
            info.duration = 0.0f;
            info.elapsedTime = 0.0f;
            info.time = 0.0;
            infos[index] = info;

            return true;
        }
    }
    
    public struct FactoryEntry : IStateMachineFactory<SchedulerEntry>
    {
        [ReadOnly]
        public ComponentTypeHandle<GameActionNormalData> instanceType;

        [NativeDisableContainerSafetyRestriction]
        public ComponentTypeHandle<GameActionNormalInfo> infoType;

        public bool Create(int unfilteredChunkIndex, in ArchetypeChunk chunk, out SchedulerEntry schedulerEntry)
        {
            //schedulerEntry.instances = chunk.GetNativeArray(ref instanceType);
            schedulerEntry.infos = chunk.GetNativeArray(ref infoType);

            return chunk.Has(ref instanceType);
        }
    }

    private ComponentTypeHandle<GameActionNormalData> __instanceType;

    private ComponentTypeHandle<GameActionNormalInfo> __infoType;

    private BufferTypeHandle<GameNodeSpeedScaleComponent> __speedScaleComponentType;

    private StateMachineSchedulerSystemCore __core;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __instanceType = state.GetComponentTypeHandle<GameActionNormalData>(true);
        __infoType = state.GetComponentTypeHandle<GameActionNormalInfo>();
        __speedScaleComponentType = state.GetBufferTypeHandle<GameNodeSpeedScaleComponent>();

        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __core = new StateMachineSchedulerSystemCore(
                ref state,
                builder
                //.WithAll<GameActionNormalData>()
                .WithAllRW<GameActionNormalInfo>());
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {

    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var infoType = __infoType.UpdateAsRef(ref state);

        FactoryExit factoryExit;
        factoryExit.infoType = infoType;
        factoryExit.speedScaleComponentType = __speedScaleComponentType.UpdateAsRef(ref state);

        FactoryEntry factoryEntry;
        factoryEntry.instanceType = __instanceType.UpdateAsRef(ref state);
        factoryEntry.infoType = infoType;

        __core.Update<SchedulerExit, SchedulerEntry, FactoryExit, FactoryEntry>(ref state, ref factoryEntry, ref factoryExit);
    }
}

[BurstCompile, 
    CreateAfter(typeof(GameActionNormalSchedulerSystem)), 
    CreateAfter(typeof(GameActionStructChangeSystem)), 
    UpdateInGroup(typeof(StateMachineGroup))]
public partial struct GameActionNormalExecutorSystem : ISystem, IEntityCommandProducerJob
{
    public struct Escaper : IStateMachineEscaper
    {
        public GameTime time;
        
        public NativeArray<GameDreamer> dreamers;
        
        public bool Execute(
            int runningStatus,
            in SystemHandle runningSystemHandle,
            in SystemHandle nextSystemHandle,
            in SystemHandle currentSystemHandle,
            int index)
        {
            if (dreamers.Length > index)
            {
                var dreamer = dreamers[index];
                switch(dreamer.status)
                {
                    case GameDreamerStatus.Sleep:
                    case GameDreamerStatus.Dream:
                        //Debug.Log($"S Awake {time}");

                        dreamer.time = time;
                        dreamer.status = GameDreamerStatus.Awake;

                        dreamers[index] = dreamer;
                        break;
                }
            }

            return true;
        }
    }
    
    public struct EscaperFactory : IStateMachineFactory<Escaper>
    {
        public GameTime time;

        public ComponentTypeHandle<GameDreamer> dreamerType;
        
        public bool Create(int unfilteredChunkIndex, in ArchetypeChunk chunk, out Escaper escaper)
        {
            escaper.time = time;
            escaper.dreamers = chunk.GetNativeArray(ref dreamerType);

            return true;
        }
    }
    
    public struct Executor : IStateMachineExecutor
    {
        public GameTime time;

        public Random random;

        public ArchetypeChunk chunk;

        public ComponentTypeHandle<GameNavMeshAgentTarget> targetType;

        [ReadOnly]
        public NativeArray<Entity> entityArray;

        [ReadOnly]
        public NativeArray<Translation> translations;

        [ReadOnly]
        public NativeArray<Rotation> rotations;

        [ReadOnly]
        public NativeArray<GameOwner> owners;

        [ReadOnly]
        public NativeArray<GameNodeSpeed> speeds;

        [ReadOnly]
        public NativeArray<GameNodeVelocity> velocities;

        [ReadOnly]
        public NativeArray<GameNodeDelay> delay;

        [ReadOnly]
        public NativeArray<GameNodeStatus> states;

        [ReadOnly]
        public NativeArray<GameNodeActorStatus> actorStates;

        [ReadOnly]
        public NativeArray<GameNavMeshAgentData> agents;

        [ReadOnly]
        public NativeArray<GameNavMeshAgentQuery> queries;

        [ReadOnly]
        public BufferAccessor<GameNavMeshAgentExtends> extends;

        [ReadOnly]
        public BufferAccessor<GameActionNormalDelayTime> delayTimes;

        [ReadOnly]
        public BufferAccessor<GameNodeSpeedSection> speedSections;

        [ReadOnly]
        public NativeArray<GameActionNormalData> instances;

        public NativeArray<GameActionNormalInfo> infos;

        public NativeArray<GameDreamerInfo> dreamerInfos;

        public NativeArray<GameDreamer> dreamers;

        public NativeArray<GameNavMeshAgentTarget> targets;

        public NativeArray<GameNodeDirection> directions;
        public BufferAccessor<GameNodePosition> positions;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<GameNodeVersion> versions;

        [NativeDisableParallelForRestriction]
        public BufferLookup<GameNodeSpeedScaleComponent> speedScaleComponents;

        public EntityAddDataQueue.ParallelWriter entityManager;

        public bool Owned(int index)
        {
              return index < owners.Length && owners[index].entity != Entity.Null;
        }

        public bool SetSpeedScale(half value, in Entity entity, ref GameActionNormalInfo info)
        {
            if (info.speedScale == value)
                return false;

            var speedScaleComponents = this.speedScaleComponents[entity];
            GameNodeSpeedScale.Set(value, info.speedScale, ref speedScaleComponents);

            info.speedScale = value;

            return true;
        }

        public int Execute(bool isEntry, int index)
        {
            if ((states[index].value & GameNodeStatus.STOP) == GameNodeStatus.STOP ||
                index < delay.Length && delay[index].Check(time))
                return 0;

            var instance = instances[index];
            var info = infos[index];
            bool isDrowning = false;
            if ((instance.flag & GameActionNormalFlag.DelayInWater) != GameActionNormalFlag.DelayInWater && 
                index < actorStates.Length)
            {
                switch (actorStates[index].value)
                {
                    case GameNodeActorStatus.Status.Swim:
                    case GameNodeActorStatus.Status.Dive:
                        isDrowning = true;
                        break;
                }
            }

            if (index < targets.Length)
            {
                var target = targets[index];
                if (isDrowning)
                {
                    if (SetSpeedScale(GameNodeSpeedScale.Normal, entityArray[index], ref info))
                        infos[index] = info;

                    if (target.sourceAreaMask != instance.waterAreaMask)
                    {
                        target.sourceAreaMask = instance.waterAreaMask;

                        targets[index] = target;
                    }
                }
                else
                {
                    if (SetSpeedScale(instance.speedScale, entityArray[index], ref info))
                        infos[index] = info;

                    if (target.sourceAreaMask != -1)
                    {
                        target.sourceAreaMask = -1;

                        targets[index] = target;
                    }
                }

                return 0;
            }

            bool isHasPositions = index < this.positions.Length;
            var positions = isHasPositions ? this.positions[index] : default;
            if (!isDrowning && isHasPositions && positions.Length > 0)
                return 0;

            //bool isExists = index < this.positions.Length, isBackward = false;
            //float3 translation = translations[index].Value, forward = math.forward(rotations[index].Value);
            /*DynamicBuffer<GameNodePosition> positions = isExists ? this.positions[index] : default;
            if (isExists && positions.Length > 0)
            {
                if (math.distancesq(realVelocities[index].value.xz, desiredVelocities[index].linear.xz) < instance.backwardThreshold)
                    return 0;

                if (math.dot(positions[0].value - translation, forward) < 0.0f)
                    return 0;

                isBackward = true;

                info.flag |= GameActionNormalInfo.Flag.MuteDelay;
            }*/
            
            if (info.duration > math.FLT_MIN_NORMAL || !(instance.delayTime.isVail || instance.range > math.FLT_MIN_NORMAL))
            {
                if (index < actorStates.Length)
                {
                    switch (actorStates[index].value)
                    {
                        case GameNodeActorStatus.Status.Swim:
                        case GameNodeActorStatus.Status.Dive:
                            if ((instance.flag & GameActionNormalFlag.DreamInWater) != GameActionNormalFlag.DreamInWater)
                                info.flag |= GameActionNormalInfo.Flag.MuteDream;
                            break;
                    }
                }

                if ((info.flag & GameActionNormalInfo.Flag.MuteDream) == GameActionNormalInfo.Flag.MuteDream)
                {
                    info.elapsedTime = (float)(time - info.time);
                    if(info.elapsedTime >= info.duration)
                        info.duration = 0.0f;
                }
                else
                {
                    bool isExists = index < dreamers.Length, isLess = info.elapsedTime < instance.sleepTime, isDelay;
                    if (info.duration > math.FLT_MIN_NORMAL)
                    {
                        info.elapsedTime = (float)(time - info.time);

                        isDelay = info.elapsedTime >= info.duration;
                    }
                    else
                        isDelay = false;

                    if (isDelay)
                    {
                        bool isDreaming = isExists && !isLess, result = isDreaming;
                        var dreamer = isExists ? dreamers[index] : default;
                        if (result)
                        {
                            if (dreamer.status == GameDreamerStatus.Dream)
                            {
                                var dreamerInfo = dreamerInfos[index];
                                result = dreamerInfo.nextIndex >= 0 && dreamerInfo.level > 0 && this.delayTimes.Length > index;
                                if (result)
                                {
                                    bool isOwned = Owned(index);
                                    var delayTimes = this.delayTimes[index];
                                    if (delayTimes.Length >= dreamerInfo.level)
                                    {
                                        result = isOwned || instance.awakeChance < random.NextFloat();

                                        if (result)
                                        {
                                            dreamer.status = GameDreamerStatus.Sleep;
                                            dreamer.time = time;
                                            dreamers[index] = dreamer;

                                            info.duration = info.elapsedTime + delayTimes[dreamerInfo.level - 1].Get(random);
                                        }
                                    }
                                    else
                                        result = isOwned;
                                }
                            }
                            else
                                result = false;
                        }

                        if (!result)
                        {
                            if (isDreaming && dreamer.status == GameDreamerStatus.Dream)
                            {
                                dreamer.status = GameDreamerStatus.Awake;
                                dreamer.time = time;
                                dreamers[index] = dreamer;

                                info.flag |= GameActionNormalInfo.Flag.MuteDream;
                            }

                            info.duration = 0.0f;
                        }
                    }
                    else if (!isExists && (instance.sleepTime > math.FLT_MIN_NORMAL ? isLess && info.elapsedTime >= instance.sleepTime : true))
                    {
                        if (index < dreamerInfos.Length)
                        {
                            GameDreamer dreamer;
                            dreamer.status = GameDreamerStatus.Sleep;
                            dreamer.time = time;
                            entityManager.AddComponentData(entityArray[index], dreamer);

                            GameDreamerInfo dreamerInfo;
                            dreamerInfo.currentIndex = -1;
                            dreamerInfo.nextIndex = instance.delayIndex;
                            dreamerInfo.level = 0;
                            dreamerInfos[index] = dreamerInfo;
                        }

                        if (instance.awakeTime.isVail)
                            info.duration += instance.awakeTime.Get(random);
                        else
                            info.duration = float.MaxValue;
                    }
                }
            }
            else
            {
                if (/*isExists && */((info.flag & GameActionNormalInfo.Flag.MuteDelay) == GameActionNormalInfo.Flag.MuteDelay ||
                    !instance.delayTime.isVail || 
                    instance.delayChance < random.NextFloat()))
                {
                    bool canMove = isHasPositions && !Owned(index);
                    if (canMove)
                    {
                        int sourceAreaMask = -1, destinationAreaMask = -1;
                        if (isDrowning)
                        {
                            info.flag |= GameActionNormalInfo.Flag.MuteDelay;

                            sourceAreaMask = instance.waterAreaMask;
                            destinationAreaMask = ~instance.waterAreaMask;
                        }

                        float velocity = velocities[index].value, speed = speeds[index].value;
                        velocity = velocity > math.FLT_MIN_NORMAL ? velocity : speed;
                        var speedSection = GameNodeSpeedSection.Get(velocity, speeds[index].value, this.speedSections[index]);

                        float radius = speedSection.angularSpeed > math.FLT_MIN_NORMAL ? velocity / speedSection.angularSpeed : 0.0f,
                            length = radius < instance.range ? random.NextFloat(radius, instance.range) : radius,
                            sign = /*isBackward ? -1.0f : */math.sign(random.NextFloat() - 0.5f);
                        float3 translation = translations[index].Value,
                            forward = math.forward(rotations[index].Value), right = math.normalize(math.cross(forward, math.up())),
                            point = translation +
                                math.normalizesafe(math.float3(forward.x, 0.0f, forward.z)) * (length * sign) +
                                math.normalizesafe(math.float3(right.x, 0.0f, right.z)) *
                                (instance.range > length ? (random.NextFloat() *
                                math.sqrt(instance.range * instance.range - length * length) *
                                math.sign(random.NextFloat() - 0.5f)) : 0.0f);
                        //GameNodePosition position;
                        //position.mode = GameNodePosition.Mode.Normal;

                        if (index < queries.Length)
                        {
                            var agent = agents[index];
                            ref var query = ref queries[index].value.value;
                            var location = query.MapLocation(translation, extends[index][0].value, agent.agentTypeID, sourceAreaMask);
                            if (query.Raycast(out var hit, location, point, destinationAreaMask) == UnityEngine.Experimental.AI.PathQueryStatus.Success &&
                                hit.mask != 0)
                                point = hit.position;
                            else
                                return 0;
                        }
                        else
                            isDrowning = false;

                        Entity entity = entityArray[index];
                        if (isDrowning)
                        {
                            GameNavMeshAgentTarget target;
                            target.sourceAreaMask = sourceAreaMask;
                            target.destinationAreaMask = destinationAreaMask;
                            target.position = point;
                            targets[index] = target;

                            chunk.SetComponentEnabled(ref targetType, index, true);
                            //entityManager.AddComponentData(entity, target);
                        }
                        else
                            GameNavMeshSystem.SetPosition(
                                index,
                                entity,
                                point,
                                ref positions,
                                ref directions,
                                ref versions);

                        SetSpeedScale((info.flag & GameActionNormalInfo.Flag.MuteDelay) == GameActionNormalInfo.Flag.MuteDelay ? GameNodeSpeedScale.Normal : instance.speedScale, entity, ref info);
                    }

                    info.flag &= ~(GameActionNormalInfo.Flag.MuteDelay | GameActionNormalInfo.Flag.MuteDream);

                    /*GameNodeVersion version = versions[entity];
                    version.type = GameNodeVersion.Type.Position;
                    ++version.value;

                    if(index < velocities.Length)
                    {
                        var velocity = velocities[index];
                        if(!velocity.value.Equals(float3.zero))
                        {
                            velocity.mode = GameNodeVelocityDirect.Mode.None;
                            velocity.version = version.value;
                            velocity.value = float3.zero;

                            velocities[index] = velocity;

                            version.type |= GameNodeVersion.Type.Direction;
                        }
                    }

                    position.version = version.value;

                    positions.Clear();

                    positions.Add(position);
                    
                    versions[entity] = version;*/
                }
                else
                {
                    info.time = time;
                    info.elapsedTime = 0.0f;
                    info.duration = instance.delayTime.Get(random);
                }
            }
            
            infos[index] = info;

            return 0;
        }
    }
    
    public struct ExecutorFactory : IStateMachineFactory<Executor>
    {
        public GameTime time;
        
        [ReadOnly]
        public EntityTypeHandle entityType;

        [ReadOnly]
        public ComponentTypeHandle<Translation> translationType;

        [ReadOnly]
        public ComponentTypeHandle<Rotation> rotationType;

        [ReadOnly]
        public ComponentTypeHandle<GameOwner> ownerType;

        [ReadOnly]
        public ComponentTypeHandle<GameNodeSpeed> speedType;

        [ReadOnly]
        public ComponentTypeHandle<GameNodeVelocity> velocityType;

        [ReadOnly]
        public ComponentTypeHandle<GameNodeDelay> delayType;
        [ReadOnly]
        public ComponentTypeHandle<GameNodeStatus> statusType;
        [ReadOnly]
        public ComponentTypeHandle<GameNodeActorStatus> actorStatusType;

        [ReadOnly]
        public ComponentTypeHandle<GameNavMeshAgentData> agentType;

        [ReadOnly]
        public ComponentTypeHandle<GameNavMeshAgentQuery> queryType;

        [ReadOnly]
        public BufferTypeHandle<GameNavMeshAgentExtends> extendType;

        [ReadOnly]
        public BufferTypeHandle<GameActionNormalDelayTime> delayTimeType;

        [ReadOnly]
        public BufferTypeHandle<GameNodeSpeedSection> speedSectionType;

        [ReadOnly]
        public ComponentTypeHandle<GameActionNormalData> instanceType;

        public ComponentTypeHandle<GameActionNormalInfo> infoType;

        public ComponentTypeHandle<GameDreamerInfo> dreamerInfoType;

        public ComponentTypeHandle<GameDreamer> dreamerType;

        public ComponentTypeHandle<GameNavMeshAgentTarget> targetType;

        public ComponentTypeHandle<GameNodeDirection> directionType;
        public BufferTypeHandle<GameNodePosition> positionType;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<GameNodeVersion> versions;

        [NativeDisableParallelForRestriction]
        public BufferLookup<GameNodeSpeedScaleComponent> speedScaleComponents;

        public EntityAddDataQueue.ParallelWriter entityManager;

        public bool Create(int index, in ArchetypeChunk chunk, out Executor executor)
        {
            executor.time = time;
            long hash = math.aslong(time);
            executor.random = new Random((uint)((int)(hash >> 32) ^ ((int)hash) ^ index));
            executor.chunk = chunk;
            executor.targetType = targetType;
            executor.entityArray = chunk.GetNativeArray(entityType);
            executor.translations = chunk.GetNativeArray(ref translationType);
            executor.rotations = chunk.GetNativeArray(ref rotationType);
            executor.owners = chunk.GetNativeArray(ref ownerType);
            executor.speeds = chunk.GetNativeArray(ref speedType);
            executor.velocities = chunk.GetNativeArray(ref velocityType);
            executor.delay = chunk.GetNativeArray(ref delayType);
            executor.states = chunk.GetNativeArray(ref statusType);
            executor.actorStates = chunk.GetNativeArray(ref actorStatusType);
            executor.agents = chunk.GetNativeArray(ref agentType);
            executor.queries = chunk.GetNativeArray(ref queryType);
            executor.extends = chunk.GetBufferAccessor(ref extendType);
            executor.delayTimes = chunk.GetBufferAccessor(ref delayTimeType);
            executor.speedSections = chunk.GetBufferAccessor(ref speedSectionType);
            executor.instances = chunk.GetNativeArray(ref instanceType);
            executor.infos = chunk.GetNativeArray(ref infoType);
            executor.dreamerInfos = chunk.GetNativeArray(ref dreamerInfoType);
            executor.dreamers = chunk.GetNativeArray(ref dreamerType);
            executor.targets = chunk.GetNativeArray(ref targetType);
            executor.directions = chunk.GetNativeArray(ref directionType);
            executor.positions = chunk.GetBufferAccessor(ref positionType);
            executor.versions = versions;
            executor.speedScaleComponents = speedScaleComponents;
            executor.entityManager = entityManager;

            return true;
        }
    }
    
    private GameSyncTime __time;

    private EntityTypeHandle __entityType;

    private ComponentTypeHandle<Translation> __translationType;

    private ComponentTypeHandle<Rotation> __rotationType;

    private ComponentTypeHandle<GameOwner> __ownerType;

    private ComponentTypeHandle<GameNodeSpeed> __speedType;

    private ComponentTypeHandle<GameNodeVelocity> __velocityType;

    private ComponentTypeHandle<GameNodeDelay> __delayType;
    private ComponentTypeHandle<GameNodeStatus> __statusType;
    private ComponentTypeHandle<GameNodeActorStatus> __actorStatusType;

    private ComponentTypeHandle<GameNavMeshAgentData> __agentType;

    private ComponentTypeHandle<GameNavMeshAgentQuery> __queryType;

    private BufferTypeHandle<GameNavMeshAgentExtends> __extendType;

    private BufferTypeHandle<GameActionNormalDelayTime> __delayTimeType;

    private BufferTypeHandle<GameNodeSpeedSection> __speedSectionType;

    private ComponentTypeHandle<GameActionNormalData> __instanceType;

    private ComponentTypeHandle<GameActionNormalInfo> __infoType;

    private ComponentTypeHandle<GameDreamerInfo> __dreamerInfoType;

    private ComponentTypeHandle<GameDreamer> __dreamerType;

    private ComponentTypeHandle<GameNavMeshAgentTarget> __targetType;

    private ComponentTypeHandle<GameNodeDirection> __directionType;
    private BufferTypeHandle<GameNodePosition> __positionType;

    private ComponentLookup<GameNodeVersion> __versions;

    private BufferLookup<GameNodeSpeedScaleComponent> __speedScaleComponents;

    private EntityAddDataPool __endFrameBarrier;

    private StateMachineExecutorSystemCore __core;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __time = new GameSyncTime(ref state);

        __entityType = state.GetEntityTypeHandle();
        __translationType = state.GetComponentTypeHandle<Translation>(true);
        __rotationType = state.GetComponentTypeHandle<Rotation>(true);
        __ownerType = state.GetComponentTypeHandle<GameOwner>(true);
        __speedType = state.GetComponentTypeHandle<GameNodeSpeed>(true);
        __velocityType = state.GetComponentTypeHandle<GameNodeVelocity>(true);
        __delayType = state.GetComponentTypeHandle<GameNodeDelay>(true);
        __statusType = state.GetComponentTypeHandle<GameNodeStatus>(true);
        __actorStatusType = state.GetComponentTypeHandle<GameNodeActorStatus>(true);
        __agentType = state.GetComponentTypeHandle<GameNavMeshAgentData>(true);
        __queryType = state.GetComponentTypeHandle<GameNavMeshAgentQuery>(true);
        __extendType = state.GetBufferTypeHandle<GameNavMeshAgentExtends>(true);
        __delayTimeType = state.GetBufferTypeHandle<GameActionNormalDelayTime>(true);
        __speedSectionType = state.GetBufferTypeHandle<GameNodeSpeedSection>(true);
        __instanceType = state.GetComponentTypeHandle<GameActionNormalData>(true);
        __infoType = state.GetComponentTypeHandle<GameActionNormalInfo>();
        __dreamerInfoType = state.GetComponentTypeHandle<GameDreamerInfo>();
        __dreamerType = state.GetComponentTypeHandle<GameDreamer>();
        __targetType = state.GetComponentTypeHandle<GameNavMeshAgentTarget>();
        __directionType = state.GetComponentTypeHandle<GameNodeDirection>();
        __positionType = state.GetBufferTypeHandle<GameNodePosition>();
        __versions = state.GetComponentLookup<GameNodeVersion>();
        __speedScaleComponents = state.GetBufferLookup<GameNodeSpeedScaleComponent>();

        __endFrameBarrier = state.WorldUnmanaged.GetExistingSystemUnmanaged<GameActionStructChangeSystem>().addDataCommander;

        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __core = new StateMachineExecutorSystemCore(
                ref state,
                builder
                .WithAll<Translation, Rotation, GameNodeStatus, GameActionNormalData, GameActionNormalInfo>(),
                state.WorldUnmanaged.GetExistingUnmanagedSystem<GameActionNormalSchedulerSystem>());
    }


    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {

    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var dreamerType = __dreamerType.UpdateAsRef(ref state);

        EscaperFactory escaperFactory;
        escaperFactory.time = __time.nextTime;
        escaperFactory.dreamerType = dreamerType;

        var entityManager = __endFrameBarrier.Create();

        int entityCount = __core.runGroup.CalculateEntityCount();

        ExecutorFactory executorFactory;
        executorFactory.time = __time.nextTime;
        executorFactory.entityType = __entityType.UpdateAsRef(ref state);
        executorFactory.translationType = __translationType.UpdateAsRef(ref state);
        executorFactory.rotationType = __rotationType.UpdateAsRef(ref state);
        executorFactory.ownerType = __ownerType.UpdateAsRef(ref state);
        executorFactory.speedType = __speedType.UpdateAsRef(ref state);
        executorFactory.velocityType = __velocityType.UpdateAsRef(ref state);
        executorFactory.delayType = __delayType.UpdateAsRef(ref state);
        executorFactory.statusType = __statusType.UpdateAsRef(ref state);
        executorFactory.actorStatusType = __actorStatusType.UpdateAsRef(ref state);
        executorFactory.agentType = __agentType.UpdateAsRef(ref state);
        executorFactory.queryType = __queryType.UpdateAsRef(ref state);
        executorFactory.extendType = __extendType.UpdateAsRef(ref state);
        executorFactory.delayTimeType = __delayTimeType.UpdateAsRef(ref state);
        executorFactory.speedSectionType = __speedSectionType.UpdateAsRef(ref state);
        executorFactory.instanceType = __instanceType.UpdateAsRef(ref state);
        executorFactory.infoType = __infoType.UpdateAsRef(ref state);
        executorFactory.dreamerInfoType = __dreamerInfoType.UpdateAsRef(ref state);
        executorFactory.dreamerType = dreamerType;
        executorFactory.targetType = __targetType.UpdateAsRef(ref state);
        executorFactory.directionType = __directionType.UpdateAsRef(ref state);
        executorFactory.positionType = __positionType.UpdateAsRef(ref state);
        executorFactory.versions = __versions.UpdateAsRef(ref state);
        executorFactory.speedScaleComponents = __speedScaleComponents.UpdateAsRef(ref state);
        executorFactory.entityManager = entityManager.AsParallelWriter((UnsafeUtility.SizeOf<GameDreamer>()/* + UnsafeUtility.SizeOf<GameNavMeshAgentTarget>()*/) * entityCount, entityCount/* << 1*/);

        __core.Update<Escaper, Executor, EscaperFactory, ExecutorFactory>(ref state, ref executorFactory, ref escaperFactory);

        entityManager.AddJobHandleForProducer<GameActionNormalExecutorSystem>(state.Dependency);
    }
}