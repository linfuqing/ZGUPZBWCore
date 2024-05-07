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

[assembly: RegisterGenericJobType(typeof(StateMachineExitJob<
    GameActionFixedSystem.Exit, 
    GameActionFixedSystem.FactoryExit>))]
[assembly: RegisterGenericJobType(typeof(StateMachineEntryJob<
    GameActionFixedSystem.Entry, 
    GameActionFixedSystem.FactoryEntry>))]
[assembly: RegisterGenericJobType(typeof(StateMachineRunJob<
    GameActionFixedSystem.Run, 
    GameActionFixedSystem.FactoryRun>))]

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


[BurstCompile, UpdateInGroup(typeof(StateMachineGroup)), UpdateAfter(typeof(GameActionActiveSystem))]
public partial struct GameActionFixedSystem : ISystem
{
    public struct Exit : IStateMachineCondition
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
    
    public struct FactoryExit : IStateMachineFactory<Exit>
    {
        [ReadOnly]
        public ComponentTypeHandle<GameActionFixedInfo> infoType;

        public BufferTypeHandle<GameNodeSpeedScaleComponent> speedScaleComponentType;

        public bool Create(int unfilteredChunkIndex, in ArchetypeChunk chunk, out Exit exit)
        {
            exit.infos = chunk.GetNativeArray(ref infoType);
            exit.speedScaleComponents = chunk.GetBufferAccessor(ref speedScaleComponentType);

            return true;
        }
    }

    public struct Entry : IStateMachineCondition
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

    public struct FactoryEntry : IStateMachineFactory<Entry>
    {
        [ReadOnly]
        public ComponentTypeHandle<GameActionFixedData> instanceType;

        public bool Create(
            int unfilteredChunkIndex, 
            in ArchetypeChunk chunk, 
            out Entry entry)
        {
            entry.instances = chunk.GetNativeArray(ref instanceType);

            return chunk.Has(ref instanceType);
        }
    }

    public struct Run : IStateMachineExecutor
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

    public struct FactoryRun : IStateMachineFactory<Run>
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
            out Run run)
        {
            run.time = time;
            run.random = new Random(RandomUtility.Hash(time) ^ (uint)unfilteredChunkIndex);
            run.chunk = chunk;
            //executor.entityArray = chunk.GetNativeArray(entityType);
            run.frames = chunk.GetBufferAccessor(ref frameType);
            run.nextFrames = chunk.GetBufferAccessor(ref nextFrameType);
            run.stages = chunk.GetBufferAccessor(ref stageType);
            run.stageIndices = chunk.GetNativeArray(ref stageIndexType);
            run.translations = chunk.GetNativeArray(ref translationType);
            run.instances = chunk.GetNativeArray(ref instanceType);
            run.infos = chunk.GetNativeArray(ref infoType);
            run.characterAngles = chunk.GetNativeArray(ref characterAngleType);
            run.angles = chunk.GetNativeArray(ref angleType);
            run.surfaces = chunk.GetNativeArray(ref surfaceType);
            run.rotations = chunk.GetNativeArray(ref rotationType);
            run.targets = chunk.GetNativeArray(ref targetType);
            run.speedScaleComponents = chunk.GetBufferAccessor(ref speedScaleComponentType);
            run.positions = chunk.GetBufferAccessor(ref positionType);
            run.transforms = chunk.GetBufferAccessor(ref transformType);
            run.animatorParameterCommands = chunk.GetBufferAccessor(ref animatorParameterCommandType);
            run.animatorParameterCommandType = animatorParameterCommandType;
            run.targetType = targetType;
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

    private StateMachineSystemCore __core;
    
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
            __core = new StateMachineSystemCore(
                ref state,
                builder
                    .WithAllRW<GameActionFixedInfo>());
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {

    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var instanceType = __instanceType.UpdateAsRef(ref state);
        var infoType = __infoType.UpdateAsRef(ref state);
        
        FactoryEntry factoryEntry;
        factoryEntry.instanceType = instanceType;

        FactoryExit factoryExit;
        factoryExit.infoType = infoType;
        factoryExit.speedScaleComponentType = __speedScaleComponentType.UpdateAsRef(ref state);
        
        FactoryRun factoryRun;
        factoryRun.time = __time.nextTime;
        factoryRun.frameType = __frameType.UpdateAsRef(ref state);
        factoryRun.nextFrameType = __nextFrameType.UpdateAsRef(ref state);
        factoryRun.stageType = __stageType.UpdateAsRef(ref state);
        factoryRun.stageIndexType = __stageIndexType.UpdateAsRef(ref state);
        factoryRun.translationType = __translationType.UpdateAsRef(ref state);
        factoryRun.instanceType = instanceType;
        factoryRun.infoType = infoType;
        factoryRun.characterAngleType = __characterAngleType.UpdateAsRef(ref state);
        factoryRun.angleType = __angleType.UpdateAsRef(ref state);
        factoryRun.surfaceType = __surfaceType.UpdateAsRef(ref state);
        factoryRun.rotationType = __rotationType.UpdateAsRef(ref state);
        factoryRun.targetType = __targetType.UpdateAsRef(ref state);
        factoryRun.speedScaleComponentType = __speedScaleComponentType.UpdateAsRef(ref state);
        factoryRun.positionType = __positionType.UpdateAsRef(ref state);
        factoryRun.transformType = __transformType.UpdateAsRef(ref state);
        factoryRun.animatorParameterCommandType = __animatorParameterCommandType.UpdateAsRef(ref state);
        
        __core.Update<Exit, Entry, Run, FactoryExit, FactoryEntry, FactoryRun>(
            ref state, 
            ref factoryRun, 
            ref factoryEntry, 
            ref factoryExit);
    }
}
