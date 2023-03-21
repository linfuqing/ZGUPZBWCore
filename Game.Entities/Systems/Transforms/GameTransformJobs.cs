using Unity.Jobs;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;
using ZG;

public interface IGameTransformEntityHandler<T> where T : IGameTransform<T>
{
    T Get(int index, in Entity entity);
}

public interface IGameTransformHandler<T> where T : IGameTransform<T>
{
    T Get(int index);
}

public interface IGameTransformCalculator<T> where T : IGameTransform<T>
{
    T Execute(int index, in T value);
}

public interface IGameTransformFactory<TTransform, THandler>
    where TTransform : IGameTransform<TTransform>
    where THandler : IGameTransformHandler<TTransform>
{
    THandler Get(in ArchetypeChunk chunk);
}

public interface IGameTransformJob<TTransform, TCalculator>
    where TTransform : IGameTransform<TTransform>
    where TCalculator : IGameTransformCalculator<TTransform>
{
    void Execute(in TCalculator calculator, in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask);
}

public struct GameTransformCalculator<TTransform, TVelocity> : IGameTransformCalculator<TTransform>
    where TTransform : unmanaged, IGameTransform<TTransform>
    where TVelocity : struct, IGameTransformVelocity<TTransform>
{
    public struct Comparer : System.Collections.Generic.IComparer<double>
    {
        public int Compare(double x, double y)
        {
            return x.CompareTo(y);
        }
    }

    public struct ReadOnlyListWrapper : IReadOnlyListWrapper<double, DynamicBuffer<GameTransformKeyframe<TTransform>>>
    {
        public int GetCount(DynamicBuffer<GameTransformKeyframe<TTransform>> list) => list.Length;

        public double Get(DynamicBuffer<GameTransformKeyframe<TTransform>> list, int index) => list[index].time;
    }

    public float smoothTime;
    public float deltaTime;

    public double time;

    /*[ReadOnly, DeallocateOnJobCompletion]
    public NativeArray<GameTransformSource<TTransform>> sources;
    [ReadOnly, DeallocateOnJobCompletion]
    public NativeArray<GameTransformDestination<TTransform>> destinations;*/

    public BufferAccessor<GameTransformKeyframe<TTransform>> keyframes;

    public NativeArray<GameTransformVelocity<TTransform, TVelocity>> velocities;

    public TTransform Execute(int index, in TTransform transform)
    {
        var keyframes = this.keyframes[index];
        int numKeyframes = keyframes.Length;
        if (numKeyframes < 1)
            return default;

        int keyframeIndex = keyframes.BinarySearch(time, new Comparer(), new ReadOnlyListWrapper()), maxFrameIndex = numKeyframes - 1;
        GameTransformKeyframe<TTransform> source = keyframes[math.clamp(keyframeIndex, 0, maxFrameIndex)], 
            destination = keyframes[math.min(keyframeIndex + 1, maxFrameIndex)];

        //float deltaTime = time > source.time ? (float)(time - source.time) : 0.0f;
        float deltaTime = this.deltaTime;
        double sourceTime = source.time + deltaTime;
        TTransform origin;
        if (sourceTime >= time && (keyframeIndex < 1 || source.time == keyframes[keyframeIndex - 1].time))
        {
            deltaTime = (float)(sourceTime - time);
            origin = source.value;
        }
        else
            origin = transform;

        if (keyframeIndex > 1)
            keyframes.RemoveRange(0, keyframeIndex - 1);

        var velocity = velocities[index];
        var result = velocity.value.SmoothDamp(
            origin,
            source.LerpTo(destination, time),
            smoothTime,
            deltaTime);

        velocities[index] = velocity;

        return result;
    }
}

[BurstCompile]
public struct GameTransformFactoryCopy<TTransform, TVelocity, THandler> : IJobParallelFor
    where TTransform : unmanaged, IGameTransform<TTransform>
    where TVelocity : struct, IGameTransformVelocity<TTransform>
    where THandler : struct, IGameTransformEntityHandler<TTransform>
{
    public GameTime time;

    [ReadOnly, DeallocateOnJobCompletion]
    public NativeArray<Entity> entityArray;

    /*[WriteOnly, NativeDisableParallelForRestriction]
    public ComponentLookup<GameTransformSource<TTransform>> sources;
    [WriteOnly, NativeDisableParallelForRestriction]
    public ComponentLookup<GameTransformDestination<TTransform>> destinations;*/

    [NativeDisableParallelForRestriction]
    public BufferLookup<GameTransformKeyframe<TTransform>> keyframes;

    public THandler handler;

    public void Execute(int index)
    {
        Entity entity = entityArray[index];

        GameTransformKeyframe<TTransform> keyframe;
        keyframe.time = time;
        keyframe.value = handler.Get(index, entity);

        var keyframes = this.keyframes[entity];

        GameTransformKeyframe<TTransform>.Insert(ref keyframes, keyframe);

        /*GameTransform<TTransform> transform;
        transform.time = time;
        transform.value = handler.Get(index, entity);

        GameTransformSource<TTransform> source;
        source.value = transform;
        source.oldValue = transform;
        sources[entity] = source;

        GameTransformDestination<TTransform> destination;
        destination.value = transform;
        destinations[entity] = destination;*/
    }
}

/*[BurstCompile]
public struct GameTransformUpdateDestinations<TTransform, THandler, TFactory> : IJobChunk
    where TTransform : struct, IGameTransform<TTransform>
    where THandler : struct, IGameTransformHandler<TTransform>
    where TFactory : struct, IGameTransformFactory<TTransform, THandler>
{
    private struct Executor
    {
        public double time;

        public NativeArray<GameTransformDestination<TTransform>> destinations;

        public THandler handler;

        public void Execute(int index)
        {
            GameTransformDestination<TTransform> destination;
            destination.value.time = time;
            destination.value.value = handler.Get(index);
            destinations[index] = destination;
        }
    }

    public double time;

    public ComponentTypeHandle<GameTransformDestination<TTransform>> destinationType;

    public TFactory factory;

    public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask, int _)
    {
        Executor executor;
        executor.time = time;
        executor.destinations = batchInChunk.GetNativeArray(destinationType);
        executor.handler = factory.Get(batchInChunk);

        int count = batchInChunk.Count;
        for (int i = 0; i < count; ++i)
            executor.Execute(i);
    }
}*/

[BurstCompile]
public struct GameTransformUpdateAll<TTransform, THandler, TFactory> : IJobChunk
    where TTransform : unmanaged, IGameTransform<TTransform>
    where THandler : struct, IGameTransformHandler<TTransform>
    where TFactory : struct, IGameTransformFactory<TTransform, THandler>
{
    private struct Executor
    {
        public GameTime time;

        public BufferAccessor<GameTransformKeyframe<TTransform>> keyframes;

        public THandler handler;

        public void Execute(int index)
        {
            GameTransformKeyframe<TTransform> keyframe;
            keyframe.time = time;
            keyframe.value = handler.Get(index);

            var keyframes = this.keyframes[index];
            GameTransformKeyframe<TTransform>.Insert(ref keyframes, keyframe);
        }
    }

    public GameTime time;

    public BufferTypeHandle<GameTransformKeyframe<TTransform>> keyframeType;

    public TFactory factory;

    public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
    {
        Executor executor;
        executor.time = time;
        executor.keyframes = chunk.GetBufferAccessor(ref keyframeType);
        executor.handler = factory.Get(chunk);

        var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
        while (iterator.NextEntityIndex(out int i))
            executor.Execute(i);
    }
}

[BurstCompile]
public struct GameTransformSmoothDamp<TTransform, TVelocity, TJob> : IJobChunk
    where TTransform : unmanaged, IGameTransform<TTransform>
    where TVelocity : unmanaged, IGameTransformVelocity<TTransform>
    where TJob : IGameTransformJob<TTransform, GameTransformCalculator<TTransform, TVelocity>>
{
    public float smoothTime;
    public float deltaTime;

    public double time;

    public BufferTypeHandle<GameTransformKeyframe<TTransform>> keyframeType;

    public ComponentTypeHandle<GameTransformVelocity<TTransform, TVelocity>> velocityType;

    public TJob job;

    public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
    {
        GameTransformCalculator<TTransform, TVelocity> calculator;
        calculator.smoothTime = smoothTime;
        calculator.deltaTime = deltaTime;
        calculator.time = time;
        calculator.keyframes = chunk.GetBufferAccessor(ref keyframeType);
        calculator.velocities = chunk.GetNativeArray(ref velocityType);
        //smoothDamp.transforms = transforms.Slice(firstEntityIndex, count);

        job.Execute(calculator, chunk, unfilteredChunkIndex, useEnabledMask, chunkEnabledMask);
    }
}
