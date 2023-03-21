using System;
using Unity.Jobs;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;
using ZG;

public interface IGameItemComponentConverter<TInput, TOutput>
{
    bool IsVail(int index);

    TOutput Convert(in TInput value);
}

public interface IGameItemComponentConvertFactory<T>
{
    T Create(in ArchetypeChunk chunk, int unfilteredChunkIndex);
}

[BurstCompile]
public struct GameItemComponentDataSyncInit<TSource, TDestination, TConverter, TFactory> : IJobChunk
    where TSource : unmanaged, IComponentData
    where TDestination : unmanaged, IComponentData
    where TConverter : struct, IGameItemComponentConverter<TDestination, TSource>
    where TFactory : struct, IGameItemComponentConvertFactory<TConverter>
{
    private struct Executor
    {
        [ReadOnly]
        public SharedHashMap<Entity, Entity>.Reader entities;

        [ReadOnly]
        public ComponentLookup<TDestination> destinations;

        [ReadOnly]
        public NativeArray<GameItemRoot> roots;

        public NativeArray<TSource> sources;

        public TConverter converter;

        public void Execute(int index)
        {
            if (!converter.IsVail(index))
                return;

            var handle = roots[index].handle;
            if (!entities.TryGetValue(GameItemStructChangeFactory.Convert(handle), out Entity entity))
                return;

            if (!destinations.HasComponent(entity))
                return;

            sources[index] = converter.Convert(destinations[entity]);
        }
    }

    [ReadOnly]
    public SharedHashMap<Entity, Entity>.Reader entities;

    [ReadOnly]
    public ComponentLookup<TDestination> destinations;

    [ReadOnly]
    public ComponentTypeHandle<GameItemRoot> rootType;

    public ComponentTypeHandle<TSource> sourceType;

    public TFactory factory;

    public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
    {
        Executor executor;
        executor.entities = entities;
        executor.destinations = destinations;
        executor.roots = chunk.GetNativeArray(ref rootType);
        executor.sources = chunk.GetNativeArray(ref sourceType);
        executor.converter = factory.Create(chunk, unfilteredChunkIndex);

        var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
        while (iterator.NextEntityIndex(out int i))
            executor.Execute(i);
    }
}

[BurstCompile]
public struct GameItemComponentDataSyncApply<TSource, TDestination, TConverter, TFactory> : IJobChunk
    where TSource : unmanaged, IComponentData
    where TDestination : unmanaged, IComponentData 
    where TConverter : struct, IGameItemComponentConverter<TSource, TDestination>
    where TFactory : struct, IGameItemComponentConvertFactory<TConverter>
{
    private struct Executor
    {
        [ReadOnly]
        public SharedHashMap<Entity, Entity>.Reader entities;

        [ReadOnly]
        public NativeArray<GameItemRoot> roots;

        [ReadOnly]
        public NativeArray<TSource> sources;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<TDestination> destinations;

        public TConverter converter;

        public void Execute(int index)
        {
            if (!converter.IsVail(index))
                return;

            var handle = roots[index].handle;
            if (!entities.TryGetValue(GameItemStructChangeFactory.Convert(handle), out Entity entity))
                return;

            if (!destinations.HasComponent(entity))
                return;

            destinations[entity] = converter.Convert(sources[index]);
        }
    }

    [ReadOnly]
    public SharedHashMap<Entity, Entity>.Reader entities;

    [ReadOnly]
    public ComponentTypeHandle<GameItemRoot> rootType;

    [ReadOnly]
    public ComponentTypeHandle<TSource> sourceType;

    [NativeDisableParallelForRestriction]
    public ComponentLookup<TDestination> destinations;

    public TFactory factory;

    public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
    {
        Executor executor;
        executor.entities = entities;
        executor.roots = chunk.GetNativeArray(ref rootType);
        executor.sources = chunk.GetNativeArray(ref sourceType);
        executor.destinations = destinations;
        executor.converter = factory.Create(chunk, unfilteredChunkIndex);

        var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
        while (iterator.NextEntityIndex(out int i))
            executor.Execute(i);
    }
}

[BurstCompile]
public struct GameItemBufferSyncApply<TSource, TDestination, TConverter> : IJobChunk
    where TSource : unmanaged, IBufferElementData
    where TDestination : unmanaged, IBufferElementData
    where TConverter : struct, IGameItemComponentConverter<TSource, TDestination>
{
    private struct Executor
    {
        [ReadOnly]
        public SharedHashMap<Entity, Entity>.Reader entities;

        [ReadOnly]
        public NativeArray<GameItemRoot> roots;

        [ReadOnly]
        public BufferAccessor<TSource> sources;

        [NativeDisableParallelForRestriction]
        public BufferLookup<TDestination> destinations;

        public TConverter converter;

        public void Execute(int index)
        {
            if (!converter.IsVail(index))
                return;

            var handle = roots[index].handle;
            if (!entities.TryGetValue(GameItemStructChangeFactory.Convert(handle), out Entity entity))
                return;

            if (!this.destinations.HasBuffer(entity))
                return;

            var sources = this.sources[index];
            var destinations = this.destinations[entity];

            int length = sources.Length;
            destinations.ResizeUninitialized(length);
            for (int i = 0; i < length; ++i)
                destinations[i] = converter.Convert(sources[i]);
        }
    }

    [ReadOnly]
    public SharedHashMap<Entity, Entity>.Reader entities;

    [ReadOnly]
    public ComponentTypeHandle<GameItemRoot> rootType;

    [ReadOnly]
    public BufferTypeHandle<TSource> sourceType;

    [NativeDisableParallelForRestriction]
    public BufferLookup<TDestination> destinations;

    public TConverter converter;

    public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
    {
        Executor executor;
        executor.entities = entities;
        executor.roots = chunk.GetNativeArray(ref rootType);
        executor.sources = chunk.GetBufferAccessor(ref sourceType);
        executor.destinations = destinations;
        executor.converter = converter;

        var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
        while (iterator.NextEntityIndex(out int i))
            executor.Execute(i);
    }
}

[UpdateInGroup(typeof(GameItemComponentInitSystemGroup), OrderFirst = true)]
public struct GameItemSyncInitSystemCore<T> where T : struct
{
    private EntityQuery __structChangeManagerGroup;

    public EntityQuery group
    {
        get;
    }

    public SharedHashMap<Entity, Entity> handleEntities
    {
        get => __structChangeManagerGroup.GetSingleton<GameItemStructChangeManager>().handleEntities;
    }

    public GameItemSyncInitSystemCore(ref SystemState state)
    {
        __structChangeManagerGroup = GameItemStructChangeManager.GetEntityQuery(ref state);

        group = state.GetEntityQuery(
            new EntityQueryDesc()
            {
                All = new ComponentType[]
                {
                    ComponentType.ReadOnly<GameItemRoot>(),
                    ComponentType.ReadOnly<T>(),
                    ComponentType.ReadOnly<GameItemSync>(), 
                    ComponentType.ReadOnly<GameItemSyncDisabled>()
                }, 
                Options = EntityQueryOptions.IncludeDisabledEntities
            });
    }
}

[UpdateInGroup(typeof(GameItemInitSystemGroup), OrderLast = true)]
public struct GameItemSyncApplySystemCore<T> where T : struct
{
    private EntityQuery __structChangeManagerGroup;

    public EntityQuery group
    {
        get;
    }

    public SharedHashMap<Entity, Entity> handleEntities
    {
        get => __structChangeManagerGroup.GetSingleton<GameItemStructChangeManager>().handleEntities;
    }

    public GameItemSyncApplySystemCore(ref SystemState state)
    {
        __structChangeManagerGroup = GameItemStructChangeManager.GetEntityQuery(ref state);

        group = state.GetEntityQuery(new EntityQueryDesc()
        {
            All = new ComponentType[]
            {
                ComponentType.ReadOnly<GameItemRoot>(),
                ComponentType.ReadOnly<T>(),
                ComponentType.ReadOnly<GameItemSync>()
            },
            Options = EntityQueryOptions.IncludeDisabledEntities
        });
        group.SetChangedVersionFilter(new ComponentType[] { typeof(GameItemRoot), typeof(T) });
    }
}

[BurstCompile, UpdateInGroup(typeof(GameItemComponentInitSystemGroup))]
public partial struct GameItemSyncSystem : ISystem
{
    private EntityQuery __group;

    public void OnCreate(ref SystemState state)
    {
        __group = state.GetEntityQuery(
            new EntityQueryDesc()
            {
                All = new ComponentType[]
                {
                    ComponentType.ReadOnly<GameItemSyncDisabled>()
                },
                Options = EntityQueryOptions.IncludeDisabledEntities
            });
    }

    public void OnDestroy(ref SystemState state)
    {

    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        state.EntityManager.RemoveComponent<GameItemSyncDisabled>(__group);
    }
}

public static class GameItemSyncSystemUtility
{
    public static void UpdateComponentData<TSource, TDestination, TConverter, TFactory>(
        this ref GameItemSyncInitSystemCore<TSource> core,
        ref SystemState state,
        TFactory factory)
        where TSource : unmanaged, IComponentData
        where TDestination : unmanaged, IComponentData
        where TConverter : struct, IGameItemComponentConverter<TDestination, TSource>
        where TFactory : struct, IGameItemComponentConvertFactory<TConverter>
    {
        var handleEntities = core.handleEntities;

        GameItemComponentDataSyncInit<TSource, TDestination, TConverter, TFactory> sync;
        sync.entities = handleEntities.reader;
        sync.destinations = state.GetComponentLookup<TDestination>(true);
        sync.rootType = state.GetComponentTypeHandle<GameItemRoot>(true);
        sync.sourceType = state.GetComponentTypeHandle<TSource>();
        sync.factory = factory;

        ref var lookupJobManager = ref handleEntities.lookupJobManager;

        var jobHandle = sync.ScheduleParallel(core.group, JobHandle.CombineDependencies(lookupJobManager.readOnlyJobHandle, state.Dependency));

        lookupJobManager.AddReadOnlyDependency(jobHandle);

        state.Dependency = jobHandle;
    }

    public static void UpdateComponentData<TSource, TDestination, TConverter, TFactory>(
        this ref GameItemSyncApplySystemCore<TSource> core, 
        ref SystemState state,
        TFactory factory)
        where TSource : unmanaged, IComponentData
        where TDestination : unmanaged, IComponentData
        where TConverter : struct, IGameItemComponentConverter<TSource, TDestination>
        where TFactory : struct, IGameItemComponentConvertFactory<TConverter>
    {
        var handleEntities = core.handleEntities;

        GameItemComponentDataSyncApply<TSource, TDestination, TConverter, TFactory> sync;
        sync.entities = handleEntities.reader;
        sync.rootType = state.GetComponentTypeHandle<GameItemRoot>(true);
        sync.sourceType = state.GetComponentTypeHandle<TSource>(true);
        sync.destinations = state.GetComponentLookup<TDestination>();
        sync.factory = factory;

        ref var lookupJobManager = ref handleEntities.lookupJobManager;

        var jobHandle = sync.ScheduleParallel(core.group, JobHandle.CombineDependencies(lookupJobManager.readOnlyJobHandle, state.Dependency));

        lookupJobManager.AddReadOnlyDependency(jobHandle);

        state.Dependency = jobHandle;
    }

    public static void UpdateBuffer<TSource, TDestination, TConverter>(
        this ref GameItemSyncApplySystemCore<TSource> core,
        ref SystemState state,
        TConverter converter)
        where TSource : unmanaged, IBufferElementData
        where TDestination : unmanaged, IBufferElementData
        where TConverter : struct, IGameItemComponentConverter<TSource, TDestination>
    {
        var handleEntities = core.handleEntities;

        GameItemBufferSyncApply<TSource, TDestination, TConverter> sync;
        sync.entities = handleEntities.reader;
        sync.rootType = state.GetComponentTypeHandle<GameItemRoot>(true);
        sync.sourceType = state.GetBufferTypeHandle<TSource>(true);
        sync.destinations = state.GetBufferLookup<TDestination>();
        sync.converter = converter;

        ref var lookupJobManager = ref handleEntities.lookupJobManager;

        var jobHandle = sync.ScheduleParallel(core.group, JobHandle.CombineDependencies(lookupJobManager.readOnlyJobHandle, state.Dependency));

        lookupJobManager.AddReadOnlyDependency(jobHandle);

        state.Dependency = jobHandle;
    }
}
