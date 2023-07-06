using System;
using System.Collections.Generic;
using Unity.Jobs;
using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Transforms;
using ZG;
using Random = Unity.Mathematics.Random;

[assembly: RegisterGenericComponentType(typeof(GameActionFixedSchedulerSystem.StateMachine))]
[assembly: RegisterGenericJobType(typeof(StateMachineExit<GameActionFixedSchedulerSystem.StateMachine, StateMachineScheduler, StateMachineFactory<StateMachineScheduler>>))]
[assembly: RegisterGenericJobType(typeof(StateMachineEntry<GameActionFixedSchedulerSystem.SchedulerEntry, GameActionFixedSchedulerSystem.FactoryEntry>))]
[assembly: RegisterGenericJobType(typeof(StateMachineEscaper<GameActionFixedSchedulerSystem.StateMachine, StateMachineEscaper, StateMachineFactory<StateMachineEscaper>>))]
[assembly: RegisterGenericJobType(typeof(StateMachineExecutor<GameActionFixedSchedulerSystem.StateMachine, GameActionFixedExecutorSystem.Executor, GameActionFixedExecutorSystem.ExecutorFactory>))]

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
    public int actionIndex;
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

[Serializable]
public struct GameActionFixedData : IComponentData
{
    public int priority;
}

[Serializable]
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
        info.time = 0.0;
        instance.AddComponentData(info);
    }

    public override void Disable(StateMachineComponentEx instance)
    {
        instance.RemoveComponent<GameActionFixedData>();
        instance.RemoveComponent<GameActionFixedInfo>();
    }
}


[UpdateAfter(typeof(GameActionActiveSchedulerSystem))]
public partial class GameActionFixedSchedulerSystem : StateMachineSchedulerSystem<GameActionFixedSchedulerSystem.SchedulerEntry, GameActionFixedSchedulerSystem.FactoryEntry, GameActionFixedSchedulerSystem>
{
    public struct SchedulerEntry : IStateMachineScheduler
    {
        [ReadOnly]
        public NativeArray<GameActionFixedData> instances;

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
            int index, 
            in ArchetypeChunk chunk,
            out SchedulerEntry schedulerEntry)
        {
            schedulerEntry.instances = chunk.GetNativeArray(ref instanceType);

            return true;
        }
    }

    public override IEnumerable<EntityQueryDesc> entryEntityArchetypeQueries => __entryEntityArchetypeQueries;

    protected override FactoryEntry _GetEntry(ref JobHandle inputDeps)
    {
        FactoryEntry factory;
        factory.instanceType = GetComponentTypeHandle<GameActionFixedData>(true);
        return factory;
    }

    private readonly EntityQueryDesc[] __entryEntityArchetypeQueries = new EntityQueryDesc[]
    {
        new EntityQueryDesc()
        {
            All = new ComponentType[]
            {
                ComponentType.ReadOnly<GameActionFixedData>(),
            },
            None = new ComponentType[]
            {
                typeof(Disabled)
            }
        }
    };
}

//[UpdateInGroup(typeof(StateMachineExecutorGroup))]
public partial class GameActionFixedExecutorSystem : 
    GameActionFixedSchedulerSystem.StateMachineExecutorSystem<GameActionFixedExecutorSystem.Executor, GameActionFixedExecutorSystem.ExecutorFactory>, IEntityCommandProducerJob
{
    public struct Executor : IStateMachineExecutor
    {
        public double time;

        public Random random;

        public ArchetypeChunk chunk;

        public ComponentTypeHandle<GameNavMeshAgentTarget> targetType;

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

        public NativeArray<Rotation> rotations;

        public NativeArray<GameNavMeshAgentTarget> targets;

        //public EntityAddDataQueue.ParallelWriter entityManager;

        public int Execute(bool isEntry, int index)
        {
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
                    Rotation rotation;
                    rotation.Value = frame.rotation;
                    rotations[index] = rotation;
                }

                isMove = false;
            }

            if(isMove)
            {
                GameNavMeshAgentTarget target;
                target.sourceAreaMask = -1;
                target.destinationAreaMask = -1;
                target.position = frame.position;

                if (index < targets.Length)
                {
                    targets[index] = target;

                    chunk.SetComponentEnabled(ref targetType, index, true);
                }
                /*else
                    entityManager.AddComponentData(entityArray[index], target);*/

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

        public ComponentTypeHandle<Rotation> rotationType;

        public ComponentTypeHandle<GameNavMeshAgentTarget> targetType;

        //public EntityAddDataQueue.ParallelWriter entityManager;

        public bool Create(
            int index, 
            in ArchetypeChunk chunk,
            out Executor executor)
        {
            executor.time = time;
            long hash = math.aslong(time);
            executor.random = new Random((uint)hash ^ (uint)(hash >> 32) ^ (uint)index);
            executor.chunk = chunk;
            executor.targetType = targetType;
            //executor.entityArray = chunk.GetNativeArray(entityType);
            executor.frames = chunk.GetBufferAccessor(ref frameType);
            executor.nextFrames = chunk.GetBufferAccessor(ref nextFrameType);
            executor.stages = chunk.GetBufferAccessor(ref stageType);
            executor.stageIndices = chunk.GetNativeArray(ref stageIndexType);
            executor.translations = chunk.GetNativeArray(ref translationType);
            executor.instances = chunk.GetNativeArray(ref instanceType);
            executor.infos = chunk.GetNativeArray(ref infoType);
            executor.rotations = chunk.GetNativeArray(ref rotationType);
            executor.targets = chunk.GetNativeArray(ref targetType);
            //executor.entityManager = entityManager;

            return true;
        }
    }

    private GameSyncTime __time;
    //private EntityAddDataPool __endFrameBarrier;
    //private EntityAddDataQueue __entityManager;

    public override IEnumerable<EntityQueryDesc> runEntityArchetypeQueries => __runEntityArchetypeQueries;

    protected override void OnCreate()
    {
        base.OnCreate();

        __time = new GameSyncTime(ref this.GetState());

       // var world = World;
        //__endFrameBarrier = world.GetOrCreateSystemUnmanaged<GameActionStructChangeSystem>().addDataCommander;
    }

    protected override void OnUpdate()
    {
        //__entityManager = __endFrameBarrier.Create();

        base.OnUpdate();

        //__entityManager.AddJobHandleForProducer<GameActionFixedExecutorSystem>(Dependency);
    }

    protected override ExecutorFactory _GetRun(ref JobHandle inputDeps)
    {
        ExecutorFactory executorFactory;
        executorFactory.time = __time.nextTime;
        //executorFactory.entityType = GetEntityTypeHandle();
        executorFactory.frameType = GetBufferTypeHandle<GameActionFixedFrame>(true);
        executorFactory.nextFrameType = GetBufferTypeHandle<GameActionFixedNextFrame>(true);
        executorFactory.stageType = GetBufferTypeHandle<GameActionFixedStage>(true);
        executorFactory.stageIndexType = GetComponentTypeHandle<GameActionFixedStageIndex>(true);
        executorFactory.translationType = GetComponentTypeHandle<Translation>(true);
        executorFactory.instanceType = GetComponentTypeHandle<GameActionFixedData>(true);
        executorFactory.infoType = GetComponentTypeHandle<GameActionFixedInfo>();
        executorFactory.rotationType = GetComponentTypeHandle<Rotation>();
        executorFactory.targetType = GetComponentTypeHandle<GameNavMeshAgentTarget>();
        //executorFactory.entityManager = __entityManager.AsComponentParallelWriter<GameNavMeshAgentTarget>(runGroup.CalculateEntityCount());

        return executorFactory;
    }

    private readonly EntityQueryDesc[] __runEntityArchetypeQueries = new EntityQueryDesc[]
    {
        new EntityQueryDesc()
        {
            All = new ComponentType[]
            {
                ComponentType.ReadWrite<GameActionFixedInfo>(),
            },
            None = new ComponentType[]
            {
                typeof(GameNavMeshAgentTarget), 
                typeof(Disabled)
            }
        }
    };
}
