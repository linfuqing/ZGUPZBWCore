using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Collections.LowLevel.Unsafe;
using ZG;
using Math = ZG.Mathematics.Math;
using Unity.Collections;
using Unity.Jobs;

public struct GameEntityActionCreator
{
    //public double time;
    public float3 targetPosition;
    public Entity entity;
    //public RigidTransform transform;
}

public struct GameEntityActionInitializer
{
    public float elapsedTime;
    //public double time;
    public Entity entity;
    public RigidTransform transform;
}

public struct GameEntityActionHiter
{
    public float elapsedTime;
    //public double time;
    public Entity entity;
    public Entity target;
    public RigidTransform transform;
}

public struct GameEntityActionDamager
{
    public int count;
    public float elapsedTime;
    //public double time;
    public Entity entity;
    public Entity target;
    public float3 position;
    public float3 normal;
    public RigidTransform transform;
}

public struct GameEntityActionManager : IDisposable
{
    [BurstCompile]
    private struct ResizeJob : IJob
    {
        [ReadOnly, DeallocateOnJobCompletion]
        public NativeArray<int> counter;

        public NativeList<GameEntityActionCreator> creators;
        public NativeList<GameEntityActionInitializer> initializers;

        public NativeFactory<GameEntityActionHiter> hiters;
        public NativeFactory<GameEntityActionDamager> damagers;

        public void Execute()
        {
            int count = counter[0];

            creators.Clear();
            creators.Capacity = math.max(creators.Capacity, count);

            initializers.Clear();
            initializers.Capacity = math.max(initializers.Capacity, count);

            hiters.Clear();
            damagers.Clear();
        }
    }

    public struct ParallelWriter
    {
        public SharedList<GameEntityActionCreator>.ParallelWriter creators;
        public SharedList<GameEntityActionInitializer>.ParallelWriter initializers;
        public NativeFactory<GameEntityActionHiter>.ParallelWriter hiters;
        public NativeFactory<GameEntityActionDamager>.ParallelWriter damagers;

        public ParallelWriter(ref GameEntityActionManager manager)
        {
            creators = manager.creators.parallelWriter;
            initializers = manager.initializers.parallelWriter;
            hiters = manager.hiters.value.parallelWriter;
            damagers = manager.damagers.value.parallelWriter;
        }
    }

    public JobHandle jobHandle
    {
        get
        {
            return JobHandle.CombineDependencies(
                JobHandle.CombineDependencies(
                    creators.lookupJobManager.readWriteJobHandle,
                    initializers.lookupJobManager.readWriteJobHandle,
                    hiters.lookupJobManager.readWriteJobHandle),
                damagers.lookupJobManager.readWriteJobHandle);
        }

        set
        {
            creators.lookupJobManager.readWriteJobHandle = value;
            initializers.lookupJobManager.readWriteJobHandle = value;
            hiters.lookupJobManager.readWriteJobHandle = value;
            damagers.lookupJobManager.readWriteJobHandle = value;
        }
    }

    public SharedList<GameEntityActionCreator> creators
    {
        readonly get;

        private set;
    }

    public SharedList<GameEntityActionInitializer> initializers
    {
        readonly get;

        private set;
    }

    public SharedFactory<GameEntityActionHiter> hiters
    {
        readonly get;

        private set;
    }

    public SharedFactory<GameEntityActionDamager> damagers
    {
        readonly get;

        private set;
    }

    public ParallelWriter parallelWriter
    {
        get => new ParallelWriter(ref this);
    }

    public GameEntityActionManager(in AllocatorManager.AllocatorHandle allocator)
    {
        creators = new SharedList<GameEntityActionCreator>(allocator);
        initializers = new SharedList<GameEntityActionInitializer>(allocator);
        hiters = new SharedFactory<GameEntityActionHiter>(allocator, true);
        damagers = new SharedFactory<GameEntityActionDamager>(allocator, true);
    }

    public void Dispose()
    {
        creators.Dispose();
        initializers.Dispose();
        hiters.Dispose();
        damagers.Dispose();
    }

    public JobHandle Resize(in EntityQuery group, in JobHandle inputDeps)
    {
        var counter = new NativeArray<int>(1, Allocator.TempJob, NativeArrayOptions.ClearMemory);
        var jobHandle = group.CalculateEntityCountAsync(counter, inputDeps);

        ResizeJob resizeJob;
        resizeJob.counter = counter;
        resizeJob.creators = creators.writer;
        resizeJob.initializers = initializers.writer;
        resizeJob.hiters = hiters.value;
        resizeJob.damagers = damagers.value;

        return resizeJob.ScheduleByRef(JobHandle.CombineDependencies(this.jobHandle, jobHandle));
    }
}

public struct GameEntityTransform
{
    public float elapsedTime;
    public RigidTransform value;

    public GameEntityTransform LerpTo(in GameEntityTransform transform, float fraction)
    {
        GameEntityTransform result;
        result.elapsedTime = math.lerp(elapsedTime, transform.elapsedTime, fraction);
        result.value = Math.Lerp(value, transform.value, fraction);

        return result;
    }
}

public static class GameEntityUtility
{
    public static int Hit(
        this ref DynamicBuffer<GameActionEntity> actionEntities,
        in Entity entity,
        float3 normal, 
        float elaspedTime,
        float interval,
        float value)
    {
        int i, numActionEntities = actionEntities.Length;
        GameActionEntity actionEntity = default;
        for (i = 0; i < numActionEntities; ++i)
        {
            actionEntity = actionEntities[i];
            if (actionEntity.target == entity)
                break;
        }

        if (i < numActionEntities)
        {
            if (interval > math.FLT_MIN_NORMAL && actionEntity.elaspedTime < elaspedTime)
            {
                int count = 0;
                float nextTime = actionEntity.elaspedTime + interval;
                while (nextTime <= elaspedTime)
                {
                    ++count;

                    actionEntity.elaspedTime = nextTime;

                    nextTime += interval;
                }

                if (count > 0)
                {
                    actionEntity.delta = value * count;
                    actionEntity.hit += actionEntity.delta;
                    actionEntity.normal += normal * count;
                    actionEntities[i] = actionEntity;

                    return count;
                }
            }

            return 0;
        }

        actionEntity.hit = value;
        actionEntity.delta = value;
        actionEntity.elaspedTime = elaspedTime;
        actionEntity.normal = normal;
        actionEntity.target = entity;

        actionEntities.Add(actionEntity);

        return 1;
    }
}