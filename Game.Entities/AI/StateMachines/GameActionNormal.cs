using System;
using System.Collections.Generic;
using Unity.Jobs;
using Unity.Entities;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using ZG;
using Random = Unity.Mathematics.Random;

[assembly: RegisterGenericComponentType(typeof(GameActionNormalSchedulerSystem.StateMachine))]
[assembly: RegisterGenericJobType(typeof(StateMachineExit<GameActionNormalSchedulerSystem.StateMachine, GameActionNormalSchedulerSystem.SchedulerExit, GameActionNormalSchedulerSystem.FactoryExit>))]
[assembly: RegisterGenericJobType(typeof(StateMachineEntry<GameActionNormalSchedulerSystem.SchedulerEntry, GameActionNormalSchedulerSystem.FactoryEntry>))]
[assembly: RegisterGenericJobType(typeof(StateMachineEscaper<GameActionNormalSchedulerSystem.StateMachine, GameActionNormalExecutorSystem.Escaper, GameActionNormalExecutorSystem.EscaperFactory>))]
[assembly: RegisterGenericJobType(typeof(StateMachineExecutor<GameActionNormalSchedulerSystem.StateMachine, GameActionNormalExecutorSystem.Executor, GameActionNormalExecutorSystem.ExecutorFactory>))]

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

[Serializable]
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

[Serializable]
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

public partial class GameActionNormalSchedulerSystem : StateMachineSchedulerSystem<
    GameActionNormalSchedulerSystem.SchedulerExit, 
    GameActionNormalSchedulerSystem.SchedulerEntry,
    GameActionNormalSchedulerSystem.FactoryExit,
    GameActionNormalSchedulerSystem.FactoryEntry, 
    GameActionNormalSchedulerSystem>
{
    public struct SchedulerExit : IStateMachineScheduler
    {
        [ReadOnly]
        public NativeArray<GameActionNormalInfo> infos;
        
        [NativeDisableParallelForRestriction]
        public BufferLookup<GameNodeSpeedScaleComponent> speedScaleComponents;
        
        public bool Execute(
               int runningStatus,
               int runningSystemIndex,
               int nextSystemIndex,
               int currentSystemIndex,
               int index,
               in Entity entity)
        {
            if(index < infos.Length && this.speedScaleComponents.HasBuffer(entity))
            {
                var speedScaleComponents = this.speedScaleComponents[entity];
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

        [NativeDisableParallelForRestriction]
        public BufferLookup<GameNodeSpeedScaleComponent> speedScaleComponents;

        public bool Create(int index, in ArchetypeChunk chunk, out SchedulerExit schedulerExit)
        {
            schedulerExit.infos = chunk.GetNativeArray(ref infoType);
            schedulerExit.speedScaleComponents = speedScaleComponents;

            return true;
        }
    }
    
    public struct SchedulerEntry : IStateMachineScheduler
    {
        [ReadOnly]
        public NativeArray<GameActionNormalData> instances;

        [WriteOnly]
        public NativeArray<GameActionNormalInfo> infos;

        public bool Execute(
            int runningStatus,
            int runningSystemIndex,
            int nextSystemIndex,
            int currentSystemIndex, 
            int index,
            in Entity entity)
        {
            if (runningStatus > 0)
                return false;

            if (currentSystemIndex == runningSystemIndex || runningSystemIndex != nextSystemIndex && nextSystemIndex >= 0)
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
        
        public ComponentTypeHandle<GameActionNormalInfo> infoType;

        public bool Create(int index, in ArchetypeChunk chunk, out SchedulerEntry schedulerEntry)
        {
            schedulerEntry.instances = chunk.GetNativeArray(ref instanceType);
            schedulerEntry.infos = chunk.GetNativeArray(ref infoType);

            return true;
        }
    }
    
    public override IEnumerable<EntityQueryDesc> entryEntityArchetypeQueries => __entryEntityArchetypeQueries;

    protected override FactoryExit _GetExit(ref JobHandle inputDeps)
    {
        FactoryExit factoryExit;
        factoryExit.infoType = GetComponentTypeHandle<GameActionNormalInfo>(true);
        factoryExit.speedScaleComponents = GetBufferLookup<GameNodeSpeedScaleComponent>();

        return factoryExit;
    }

    protected override FactoryEntry _GetEntry(ref JobHandle inputDeps)
    {
        FactoryEntry factoryEntry;
        factoryEntry.instanceType = GetComponentTypeHandle<GameActionNormalData>(true);
        factoryEntry.infoType = GetComponentTypeHandle<GameActionNormalInfo>();
        return factoryEntry;
    }
    
    private readonly EntityQueryDesc[] __entryEntityArchetypeQueries = new EntityQueryDesc[]
    {
        new EntityQueryDesc()
        {
            All = new ComponentType[]
            {
                ComponentType.ReadOnly<GameActionNormalData>(), 
                ComponentType.ReadOnly<GameActionNormalInfo>()
            }, 
            None = new ComponentType[]
            {
                typeof(Disabled)
            }
        }
    };
}

//[UpdateInGroup(typeof(StateMachineExecutorGroup))]
public partial class GameActionNormalExecutorSystem : GameActionNormalSchedulerSystem.StateMachineExecutorSystem<
    GameActionNormalExecutorSystem.Escaper, 
    GameActionNormalExecutorSystem.Executor, 
    GameActionNormalExecutorSystem.EscaperFactory,
    GameActionNormalExecutorSystem.ExecutorFactory>, IEntityCommandProducerJob
{
    public struct Escaper : IStateMachineEscaper
    {
        public GameTime time;
        
        public NativeArray<GameDreamer> dreamers;
        
        public bool Execute(
            int runningStatus,
            int runningSystemIndex,
            int nextSystemIndex,
            int currentSystemIndex,
            int index)
        {
            if (dreamers.Length > index)
            {
                GameDreamer dreamer = dreamers[index];
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
        
        public bool Create(int index, in ArchetypeChunk chunk, out Escaper escaper)
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
    
    public override IEnumerable<EntityQueryDesc> runEntityArchetypeQueries => __runEntityArchetypeQueries;

    private GameSyncTime __time;
    private EntityAddDataPool __endFrameBarrier;
    private EntityAddDataQueue __entityManager;

    protected override void OnCreate()
    {
        base.OnCreate();

        __time = new GameSyncTime(ref this.GetState());

        World world = World;
        __endFrameBarrier = world.GetOrCreateSystemUnmanaged<GameActionStructChangeSystem>().addDataCommander;
    }

    protected override EscaperFactory _GetExit(ref JobHandle inputDeps)
    {
        EscaperFactory escaperFactory;
        escaperFactory.time = __time.nextTime;
        escaperFactory.dreamerType = GetComponentTypeHandle<GameDreamer>();

        return escaperFactory;
    }

    protected override ExecutorFactory _GetRun(ref JobHandle inputDeps)
    {
        int entityCount = runGroup.CalculateEntityCount();

        ExecutorFactory executorFactory;
        executorFactory.time = __time.nextTime;
        executorFactory.entityType = GetEntityTypeHandle();
        executorFactory.translationType = GetComponentTypeHandle<Translation>(true);
        executorFactory.rotationType = GetComponentTypeHandle<Rotation>(true);
        executorFactory.ownerType = GetComponentTypeHandle<GameOwner>(true);
        executorFactory.speedType = GetComponentTypeHandle<GameNodeSpeed>(true);
        executorFactory.velocityType = GetComponentTypeHandle<GameNodeVelocity>(true);
        executorFactory.delayType = GetComponentTypeHandle<GameNodeDelay>(true);
        executorFactory.statusType = GetComponentTypeHandle<GameNodeStatus>(true);
        executorFactory.actorStatusType = GetComponentTypeHandle<GameNodeActorStatus>(true);
        executorFactory.agentType = GetComponentTypeHandle<GameNavMeshAgentData>(true);
        executorFactory.queryType = GetComponentTypeHandle<GameNavMeshAgentQuery>(true);
        executorFactory.extendType = GetBufferTypeHandle<GameNavMeshAgentExtends>(true);
        executorFactory.delayTimeType = GetBufferTypeHandle<GameActionNormalDelayTime>(true);
        executorFactory.speedSectionType = GetBufferTypeHandle<GameNodeSpeedSection>(true);
        executorFactory.instanceType = GetComponentTypeHandle<GameActionNormalData>(true);
        executorFactory.infoType = GetComponentTypeHandle<GameActionNormalInfo>();
        executorFactory.dreamerInfoType = GetComponentTypeHandle<GameDreamerInfo>();
        executorFactory.dreamerType = GetComponentTypeHandle<GameDreamer>();
        executorFactory.targetType = GetComponentTypeHandle<GameNavMeshAgentTarget>();
        executorFactory.directionType = GetComponentTypeHandle<GameNodeDirection>();
        executorFactory.positionType = GetBufferTypeHandle<GameNodePosition>();
        executorFactory.versions = GetComponentLookup<GameNodeVersion>();
        executorFactory.speedScaleComponents = GetBufferLookup<GameNodeSpeedScaleComponent>();
        executorFactory.entityManager = __entityManager.AsParallelWriter((UnsafeUtility.SizeOf<GameDreamer>()/* + UnsafeUtility.SizeOf<GameNavMeshAgentTarget>()*/) * entityCount, entityCount/* << 1*/);

        return executorFactory;
    }

    protected override void OnUpdate()
    {
        __entityManager = __endFrameBarrier.Create();

        base.OnUpdate();

        __entityManager.AddJobHandleForProducer<GameActionNormalExecutorSystem>(Dependency);
    }
    
    private readonly EntityQueryDesc[] __runEntityArchetypeQueries = new EntityQueryDesc[]
    {
        new EntityQueryDesc()
        {
            All = new ComponentType[]
            {
                ComponentType.ReadOnly<Translation>(),
                ComponentType.ReadOnly<Rotation>(),
                ComponentType.ReadOnly<GameNodeStatus>(),
                ComponentType.ReadOnly<GameActionNormalData>(),
                ComponentType.ReadWrite<GameActionNormalInfo>(),
            }, 
            None = new ComponentType[]
            {
                typeof(Disabled)
            }
        }
    };
}