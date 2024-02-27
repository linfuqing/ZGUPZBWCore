using System;
using System.Collections.Generic;
using Unity.Animation;
using Unity.Jobs;
using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Transforms;
using ZG;
using Random = Unity.Mathematics.Random;
using Unity.Burst;

[assembly: RegisterGenericJobType(typeof(StateMachineSchedulerJob<
    GameActionFixedSchedulerSystem.SchedulerExit, 
    GameActionFixedSchedulerSystem.FactoryExit, 
    GameActionFixedSchedulerSystem.SchedulerEntry, 
    GameActionFixedSchedulerSystem.FactoryEntry>))]
[assembly: RegisterGenericJobType(typeof(StateMachineEscaperJob<StateMachineEscaper, StateMachineFactory<StateMachineEscaper>>))]
[assembly: RegisterGenericJobType(typeof(StateMachineExecutorJob<GameActionFixedExecutorSystem.Executor, GameActionFixedExecutorSystem.ExecutorFactory>))]

[Serializable]
public struct GameActionFixedNextFrame : IBufferElementData
{
    public int frameIndex;

    public float chance;

    public static int FindFrameIndex(
        int startIndex,
        int count,
        float randomValue,
        in DynamicBuffer<GameActionFixedNextFrame> nextFrames)
    {
        GameActionFixedNextFrame nextFrame;
        for (int i = 0; i < count; ++i)
        {
            nextFrame = nextFrames[i + startIndex];
            if (nextFrame.chance < randomValue)
                randomValue -= nextFrame.chance;
            else
                return nextFrame.frameIndex;
        }

        return -1;
    }
}

[Serializable]
public struct GameActionFixedFrame : IBufferElementData
{
    //public int actionIndex;
    public StringHash animationTrigger;
    public half speedScale;
    public float rangeSq;
    public float minTime;
    public float maxTime;
    public float3 position;
    public quaternion rotation;

    public int nextFrameStartIndex;
    public int nextFrameCount;
}

[Serializable]
public struct GameActionFixedStage : IBufferElementData
{
    public int nextFrameStartIndex;
    public int nextFrameCount;
}

[Serializable]
public struct GameActionFixedStageIndex : IComponentData
{
    public int value;
}

public struct GameActionFixedData : IComponentData
{
    public int priority;
}

public struct GameActionFixedInfo : IComponentData
{
    public enum Status
    {
        None, 
        Moving, 
        Acting
    }

    public Status status;
    public int frameIndex;
    public int stageIndex;
    public half speedScale;
    public double time;
}

public class GameActionFixed : StateMachineNode
{
    public int priority;

    public override void Enable(StateMachineComponentEx instance)
    {
        GameActionFixedData data;
        data.priority = priority;
        instance.AddComponentData(data);

        GameActionFixedInfo info;
        info.status = GameActionFixedInfo.Status.None;
        info.frameIndex = -1;
        info.stageIndex = -1;
        info.speedScale = (half)1.0f;
        info.time = 0.0;
        instance.AddComponentData(info);
    }

    public override void Disable(StateMachineComponentEx instance)
    {
        instance.RemoveComponent<GameActionFixedData>();
        //instance.RemoveComponent<GameActionFixedInfo>();
    }
}


[BurstCompile, UpdateInGroup(typeof(StateMachineGroup), OrderLast = true), UpdateAfter(typeof(GameActionActiveSchedulerSystem))]
public partial struct GameActionFixedSchedulerSystem : ISystem
{
    public struct SchedulerExit : IStateMachineScheduler
    {
        [ReadOnly]
        public NativeArray<GameActionFixedInfo> infos;
        
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
        public ComponentTypeHandle<GameActionFixedInfo> infoType;

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
        [ReadOnly]
        public NativeArray<GameActionFixedData> instances;

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

            return true;
        }
    }

    public struct FactoryEntry : IStateMachineFactory<SchedulerEntry>
    {
        [ReadOnly]
        public ComponentTypeHandle<GameActionFixedData> instanceType;

        public bool Create(
            int unfilteredChunkIndex, 
            in ArchetypeChunk chunk, 
            out SchedulerEntry schedulerEntry)
        {
            schedulerEntry.instances = chunk.GetNativeArray(ref instanceType);

            return chunk.Has(ref instanceType);
        }
    }

    private StateMachineSchedulerSystemCore __core;

    private ComponentTypeHandle<GameActionFixedData> __instanceType;
    
    private ComponentTypeHandle<GameActionFixedInfo> __infoType;

    private BufferTypeHandle<GameNodeSpeedScaleComponent> __speedScaleComponentType;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __core = new StateMachineSchedulerSystemCore(
                ref state,
                builder
                .WithAll<GameActionFixedInfo>());
        
        __instanceType = state.GetComponentTypeHandle<GameActionFixedData>(true);

        __speedScaleComponentType = state.GetBufferTypeHandle<GameNodeSpeedScaleComponent>();
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {

    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        FactoryEntry factoryEntry;
        factoryEntry.instanceType = __instanceType.UpdateAsRef(ref state);

        FactoryExit factoryExit;
        factoryExit.infoType = __infoType.UpdateAsRef(ref state);
        factoryExit.speedScaleComponentType = __speedScaleComponentType.UpdateAsRef(ref state);
        __core.Update<SchedulerExit, SchedulerEntry, FactoryExit, FactoryEntry>(ref state, ref factoryEntry, ref factoryExit);
    }
}

[BurstCompile, CreateAfter(typeof(GameActionFixedSchedulerSystem)), UpdateInGroup(typeof(StateMachineGroup))]
public partial struct GameActionFixedExecutorSystem : ISystem
{
    public struct Executor : IStateMachineExecutor
    {
        public double time;

        public Random random;

        public ArchetypeChunk chunk;

        [ReadOnly]
        public BufferAccessor<GameActionFixedFrame> frames;

        [ReadOnly]
        public BufferAccessor<GameActionFixedNextFrame> nextFrames;

        [ReadOnly]
        public BufferAccessor<GameActionFixedStage> stages;

        [ReadOnly]
        public NativeArray<GameActionFixedStageIndex> stageIndices;

        //[ReadOnly]
        //public NativeArray<Entity> entityArray;

        [ReadOnly]
        public NativeArray<Translation> translations;

        [ReadOnly]
        public NativeArray<GameActionFixedData> instances;

        public NativeArray<GameActionFixedInfo> infos;

        public NativeArray<GameNodeCharacterAngle> characterAngles;

        public NativeArray<GameNodeAngle> angles;

        public NativeArray<GameNodeSurface> surfaces;

        public NativeArray<Rotation> rotations;

        //public NativeArray<LocalToWorld> localToWorlds;

        public NativeArray<GameNavMeshAgentTarget> targets;

        public BufferAccessor<GameNodeSpeedScaleComponent> speedScaleComponents;

        public BufferAccessor<GameNodePosition> positions;

        public BufferAccessor<GameTransformKeyframe<GameTransform>> transforms;

        public BufferAccessor<MeshInstanceAnimatorParameterCommand> animatorParameterCommands;

        public BufferTypeHandle<MeshInstanceAnimatorParameterCommand> animatorParameterCommandType;

        public ComponentTypeHandle<GameNavMeshAgentTarget> targetType;

        //public EntityAddDataQueue.ParallelWriter entityManager;

        public int Execute(bool isEntry, int index)
        {
            if (index >= instances.Length)
                return 0;
            
            if (chunk.IsComponentEnabled(ref targetType, index))
                return 0;

            bool isMove;
            var info = infos[index];
            int stageIndex = stageIndices[index].value;
            if(stageIndex != info.stageIndex)
            {
                info.stageIndex = stageIndex;
                info.frameIndex = -1;
                info.status = GameActionFixedInfo.Status.None;
            }

            var nextFrames = this.nextFrames[index];
            var stages = this.stages[index];
            if(info.frameIndex == -1)
            {
                if (stages.Length <= stageIndex)
                    return 0;

                var stage = stages[stageIndex];

                info.frameIndex = GameActionFixedNextFrame.FindFrameIndex(stage.nextFrameStartIndex, stage.nextFrameCount, random.NextFloat(), nextFrames);
                if (info.frameIndex == -1)
                    return 0;
            }

            var frames = this.frames[index];
            var frame = frames[info.frameIndex];
            if(info.status == GameActionFixedInfo.Status.Acting)
            {
                if (info.time > time)
                    return instances[index].priority;

                info.frameIndex = GameActionFixedNextFrame.FindFrameIndex(frame.nextFrameStartIndex, frame.nextFrameCount, random.NextFloat(), nextFrames);
                if (info.frameIndex == -1)
                    return 0;

                isMove = true;
            }
            else if (math.distancesq(translations[index].Value, frame.position) > frame.rangeSq)
            {
                /*if (info.status == GameActionFixedInfo.Status.Moving)
                    return 0;*/

                isMove = true;
            }
            else
            {
                info.status = GameActionFixedInfo.Status.Acting;
                info.time = time + random.NextFloat(frame.minTime, frame.maxTime);
                //if(frame.minTime < frame.maxTime)
                {
                }

                if (!StringHash.IsNullOrEmpty(frame.animationTrigger))
                {
                    MeshInstanceAnimatorParameterCommand animatorParameterCommand;
                    animatorParameterCommand.value = 1;
                    animatorParameterCommand.name = frame.animationTrigger;
                    animatorParameterCommands[index].Add(animatorParameterCommand);
                    
                    chunk.SetComponentEnabled(ref animatorParameterCommandType, index, true);
                }

                if(index < characterAngles.Length || index < angles.Length)
                {
                    var surfaceRotation = index < surfaces.Length ? math.mul(math.inverse(surfaces[index].rotation), frame.rotation) : frame.rotation;
                    half eulerY = (half)ZG.Mathematics.Math.GetEulerY(surfaceRotation);

                    if(index < characterAngles.Length)
                    {
                        GameNodeCharacterAngle angle;
                        angle.value = eulerY;

                        characterAngles[index] = angle;
                    }

                    if (index < angles.Length)
                    {
                        GameNodeAngle angle;
                        angle.value = eulerY;

                        angles[index] = angle;
                    }
                }

                Rotation rotation;
                rotation.Value = frame.rotation;
                rotations[index] = rotation;
                
                if(index < positions.Length)
                    positions[index].Clear();

                if (index < transforms.Length)
                    transforms[index].Clear();
                
                isMove = false;
            }

            if(isMove)
            {
                if (index < targets.Length)
                {
                    GameNavMeshAgentTarget target;
                    target.sourceAreaMask = -1;
                    target.destinationAreaMask = -1;
                    target.position = frame.position;

                    targets[index] = target;

                    chunk.SetComponentEnabled(ref targetType, index, true);
                }
                /*else
                    entityManager.AddComponentData(entityArray[index], target);*/

                if (index < this.speedScaleComponents.Length)
                {
                    var speedScaleComponents = this.speedScaleComponents[index];
                    
                    int length = speedScaleComponents.Length;
                    for(int i = 0; i < length; ++i)
                    {
                        if(speedScaleComponents[i].value == info.speedScale)
                        {
                            speedScaleComponents.RemoveAt(i);

                            break;
                        }
                    }

                    info.speedScale = math.abs(frame.speedScale) > math.FLT_MIN_NORMAL ? frame.speedScale : (half)1.0f;

                    GameNodeSpeedScaleComponent speedScaleComponent;
                    speedScaleComponent.value = info.speedScale;
                    speedScaleComponents.Add(speedScaleComponent);
                }

                info.status = GameActionFixedInfo.Status.Moving;
            }

            infos[index] = info;

            return instances[index].priority;
        }
    }

    public struct ExecutorFactory : IStateMachineFactory<Executor>
    {
        public double time;

        //[ReadOnly]
        //public EntityTypeHandle entityType;

        [ReadOnly]
        public BufferTypeHandle<GameActionFixedFrame> frameType;

        [ReadOnly]
        public BufferTypeHandle<GameActionFixedNextFrame> nextFrameType;

        [ReadOnly]
        public BufferTypeHandle<GameActionFixedStage> stageType;

        [ReadOnly]
        public ComponentTypeHandle<GameActionFixedStageIndex> stageIndexType;

        [ReadOnly]
        public ComponentTypeHandle<Translation> translationType;
        [ReadOnly]
        public ComponentTypeHandle<GameActionFixedData> instanceType;
        public ComponentTypeHandle<GameActionFixedInfo> infoType;

        public ComponentTypeHandle<GameNodeCharacterAngle> characterAngleType;

        public ComponentTypeHandle<GameNodeAngle> angleType;

        public ComponentTypeHandle<GameNodeSurface> surfaceType;

        public ComponentTypeHandle<Rotation> rotationType;

        public ComponentTypeHandle<GameNavMeshAgentTarget> targetType;

        public BufferTypeHandle<GameNodeSpeedScaleComponent> speedScaleComponentType;

        public BufferTypeHandle<GameNodePosition> positionType;

        public BufferTypeHandle<GameTransformKeyframe<GameTransform>> transformType;

        public BufferTypeHandle<MeshInstanceAnimatorParameterCommand> animatorParameterCommandType;

        //public EntityAddDataQueue.ParallelWriter entityManager;

        public bool Create(
            int unfilteredChunkIndex, 
            in ArchetypeChunk chunk, 
            out Executor executor)
        {
            executor.time = time;
            executor.random = new Random(RandomUtility.Hash(time) ^ (uint)unfilteredChunkIndex);
            executor.chunk = chunk;
            //executor.entityArray = chunk.GetNativeArray(entityType);
            executor.frames = chunk.GetBufferAccessor(ref frameType);
            executor.nextFrames = chunk.GetBufferAccessor(ref nextFrameType);
            executor.stages = chunk.GetBufferAccessor(ref stageType);
            executor.stageIndices = chunk.GetNativeArray(ref stageIndexType);
            executor.translations = chunk.GetNativeArray(ref translationType);
            executor.instances = chunk.GetNativeArray(ref instanceType);
            executor.infos = chunk.GetNativeArray(ref infoType);
            executor.characterAngles = chunk.GetNativeArray(ref characterAngleType);
            executor.angles = chunk.GetNativeArray(ref angleType);
            executor.surfaces = chunk.GetNativeArray(ref surfaceType);
            executor.rotations = chunk.GetNativeArray(ref rotationType);
            executor.targets = chunk.GetNativeArray(ref targetType);
            executor.speedScaleComponents = chunk.GetBufferAccessor(ref speedScaleComponentType);
            executor.positions = chunk.GetBufferAccessor(ref positionType);
            executor.transforms = chunk.GetBufferAccessor(ref transformType);
            executor.animatorParameterCommands = chunk.GetBufferAccessor(ref animatorParameterCommandType);
            executor.animatorParameterCommandType = animatorParameterCommandType;
            executor.targetType = targetType;
            //executor.entityManager = entityManager;

            return true;
        }
    }

    private GameSyncTime __time;

    private BufferTypeHandle<GameActionFixedFrame> __frameType;

    private BufferTypeHandle<GameActionFixedNextFrame> __nextFrameType;

    private BufferTypeHandle<GameActionFixedStage> __stageType;

    private ComponentTypeHandle<GameActionFixedStageIndex> __stageIndexType;

    private ComponentTypeHandle<Translation> __translationType;
    private ComponentTypeHandle<GameActionFixedData> __instanceType;
    private ComponentTypeHandle<GameActionFixedInfo> __infoType;

    private ComponentTypeHandle<GameNodeCharacterAngle> __characterAngleType;

    private ComponentTypeHandle<GameNodeAngle> __angleType;

    private ComponentTypeHandle<GameNodeSurface> __surfaceType;

    private ComponentTypeHandle<Rotation> __rotationType;

    private ComponentTypeHandle<GameNavMeshAgentTarget> __targetType;

    private BufferTypeHandle<GameNodeSpeedScaleComponent> __speedScaleComponentType;

    private BufferTypeHandle<GameNodePosition> __positionType;

    private BufferTypeHandle<GameTransformKeyframe<GameTransform>> __transformType;

    private BufferTypeHandle<MeshInstanceAnimatorParameterCommand> __animatorParameterCommandType;

    private StateMachineExecutorSystemCore __core;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __time = new GameSyncTime(ref state);

        __frameType = state.GetBufferTypeHandle<GameActionFixedFrame>(true);
        __nextFrameType = state.GetBufferTypeHandle<GameActionFixedNextFrame>(true);
        __stageType = state.GetBufferTypeHandle<GameActionFixedStage>(true);
        __stageIndexType = state.GetComponentTypeHandle<GameActionFixedStageIndex>(true);
        __translationType = state.GetComponentTypeHandle<Translation>(true);
        __instanceType = state.GetComponentTypeHandle<GameActionFixedData>(true);
        __infoType = state.GetComponentTypeHandle<GameActionFixedInfo>();
        __characterAngleType = state.GetComponentTypeHandle<GameNodeCharacterAngle>();
        __angleType = state.GetComponentTypeHandle<GameNodeAngle>();
        __surfaceType = state.GetComponentTypeHandle<GameNodeSurface>();
        __rotationType = state.GetComponentTypeHandle<Rotation>();
        __targetType = state.GetComponentTypeHandle<GameNavMeshAgentTarget>();
        __speedScaleComponentType = state.GetBufferTypeHandle<GameNodeSpeedScaleComponent>();
        __positionType = state.GetBufferTypeHandle<GameNodePosition>();
        __transformType = state.GetBufferTypeHandle<GameTransformKeyframe<GameTransform>>();
        __animatorParameterCommandType = state.GetBufferTypeHandle<MeshInstanceAnimatorParameterCommand>();

        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __core = new StateMachineExecutorSystemCore(
                ref state,
                builder
                .WithAllRW<GameActionFixedInfo>(),
                state.WorldUnmanaged.GetExistingUnmanagedSystem<GameActionFixedSchedulerSystem>());
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
        executorFactory.frameType = __frameType.UpdateAsRef(ref state);
        executorFactory.nextFrameType = __nextFrameType.UpdateAsRef(ref state);
        executorFactory.stageType = __stageType.UpdateAsRef(ref state);
        executorFactory.stageIndexType = __stageIndexType.UpdateAsRef(ref state);
        executorFactory.translationType = __translationType.UpdateAsRef(ref state);
        executorFactory.instanceType = __instanceType.UpdateAsRef(ref state);
        executorFactory.infoType = __infoType.UpdateAsRef(ref state);
        executorFactory.characterAngleType = __characterAngleType.UpdateAsRef(ref state);
        executorFactory.angleType = __angleType.UpdateAsRef(ref state);
        executorFactory.surfaceType = __surfaceType.UpdateAsRef(ref state);
        executorFactory.rotationType = __rotationType.UpdateAsRef(ref state);
        executorFactory.targetType = __targetType.UpdateAsRef(ref state);
        executorFactory.speedScaleComponentType = __speedScaleComponentType.UpdateAsRef(ref state);
        executorFactory.positionType = __positionType.UpdateAsRef(ref state);
        executorFactory.transformType = __transformType.UpdateAsRef(ref state);
        executorFactory.animatorParameterCommandType = __animatorParameterCommandType.UpdateAsRef(ref state);

        StateMachineFactory<StateMachineEscaper> escaperFactory;
        __core.Update<StateMachineEscaper, Executor, StateMachineFactory<StateMachineEscaper>, ExecutorFactory>(ref state, ref executorFactory, ref escaperFactory);
    }
}
