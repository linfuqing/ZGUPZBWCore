using System;
using Unity.Jobs;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;
using ZG;
using Handle = GameItemHandle;

[Serializable]
public struct GameItemType : IComponentData
{
    public int value;
}

[BurstCompile, UpdateInGroup(typeof(GameItemComponentInitSystemGroup), OrderLast = true)]
public partial struct GameItemTypeInitSystem : ISystem
{
    [BurstCompile]
    private struct Init : IJobParallelFor
    {
        public GameItemManager.ReadOnlyInfos inputs;

        [ReadOnly, DeallocateOnJobCompletion]
        public NativeArray<Entity> entities;
        [ReadOnly]
        public ComponentLookup<GameItemData> instances;
        [NativeDisableParallelForRestriction]
        public ComponentLookup<GameItemType> outputs;

        public void Execute(int index)
        {
            Entity entity = entities[index];

            if (!inputs.TryGetValue(instances[entity].handle, out var item))
                return;

            GameItemType type;
            type.value = item.type;
            outputs[entity] = type;
        }
    }

    public static readonly int InnerloopBatchCount = 1;

    private EntityQuery __group;
    private GameItemManagerShared __itemManager;

    public void OnCreate(ref SystemState state)
    {
        BurstUtility.InitializeJobParallelFor<Init>();

        __group = state.GetEntityQuery(
            ComponentType.ReadOnly<GameItemData>(),
            ComponentType.Exclude<GameItemType>());

        __itemManager = state.World.GetOrCreateSystemUnmanaged<GameItemSystem>().manager;
    }

    public void OnDestroy(ref SystemState state)
    {

    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var entities = __group.ToEntityArrayBurstCompatible(state.GetEntityTypeHandle(), Allocator.TempJob);
        state.EntityManager.AddComponent<GameItemType>(__group);

        Init init;
        init.inputs = __itemManager.value.readOnlyInfos;
        init.entities = entities;
        init.instances = state.GetComponentLookup<GameItemData>(true);
        init.outputs = state.GetComponentLookup<GameItemType>();

        ref var lookupJobManager = ref __itemManager.lookupJobManager;

        var jobHandle = init.Schedule(entities.Length, InnerloopBatchCount, JobHandle.CombineDependencies(lookupJobManager.readOnlyJobHandle, state.Dependency));

        lookupJobManager.AddReadOnlyDependency(jobHandle);

        state.Dependency = jobHandle;
    }
}

[BurstCompile, UpdateInGroup(typeof(GameItemSystemGroup), OrderFirst = true)]
public partial struct GameItemTypeChangeSystem : ISystem
{
    private struct Result
    {
        public Handle handle;
        public int type;
    }

    [BurstCompile]
    private struct Resize : IJob
    {
        [ReadOnly, DeallocateOnJobCompletion]
        public NativeArray<int> counter;

        public NativeList<Result> results;

        public void Execute()
        {
            results.Capacity = math.max(results.Capacity, counter[0]);
        }
    }

    private struct DidChangeTypes
    {
        public GameItemManager.ReadOnlyInfos infos;

        [ReadOnly]
        public NativeArray<GameItemData> instances;
        [ReadOnly]
        public NativeArray<GameItemType> types;

        public NativeList<Result>.ParallelWriter results;

        public void Execute(int index)
        {
            var handle = instances[index].handle;
            if (!infos.TryGetValue(handle, out var item))
                return;

            int type = types[index].value;
            if (type == item.type)
                return;

            Result result;
            result.handle = handle;
            result.type = type;
            results.AddNoResizeEx(result);
            //manager.CompareExchange(ref destinationHandle, ref sourceType, out _);
        }
    }

    [BurstCompile]
    private struct DidChangeTypesEx : IJobChunk
    {
        public GameItemManager.ReadOnlyInfos infos;

        [ReadOnly]
        public ComponentTypeHandle<GameItemData> instanceType;
        [ReadOnly]
        public ComponentTypeHandle<GameItemType> typeType;

        public NativeList<Result>.ParallelWriter results;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            DidChangeTypes didChangeTypes;
            didChangeTypes.infos = infos;
            didChangeTypes.instances = chunk.GetNativeArray(ref instanceType);
            didChangeTypes.types = chunk.GetNativeArray(ref typeType);
            didChangeTypes.results = results;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                didChangeTypes.Execute(i);
        }
    }

    [BurstCompile]
    private struct ApplyChangedTypes : IJob
    {
        public GameItemManager manager;

        public NativeList<Result> results;

        public void Execute()
        {
            int length = results.Length;
            for (int i = 0; i < length; ++i)
            {
                ref var result = ref results.ElementAt(i);

                manager.CompareExchange(ref result.handle, ref result.type, out _);
            }

            results.Clear();
        }
    }

    private EntityQuery __group;
    private GameItemManagerShared __itemManager;
    private NativeListLite<Result> __results;

    public void OnCreate(ref SystemState state)
    {
        BurstUtility.InitializeJob<Resize>();
        BurstUtility.InitializeJob<ApplyChangedTypes>();

        __group = state.GetEntityQuery(ComponentType.ReadOnly<GameItemData>(), ComponentType.ReadOnly<GameItemType>());
        __group.SetChangedVersionFilter(typeof(GameItemType));

        __itemManager = state.World.GetOrCreateSystemUnmanaged<GameItemSystem>().manager;

        __results = new NativeListLite<Result>(Allocator.Persistent);
    }

    public void OnDestroy(ref SystemState state)
    {
        __results.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!__itemManager.isCreated)
            return;

        var counter = new NativeArray<int>(1, Allocator.TempJob, NativeArrayOptions.ClearMemory);
        var jobHandle = __group.CalculateEntityCountAsync(counter, state.Dependency);

        NativeList<Result> results = __results;

        Resize resize;
        resize.counter = counter;
        resize.results = results;
        jobHandle = resize.Schedule(jobHandle);

        DidChangeTypesEx didChangeTypes;
        didChangeTypes.infos = __itemManager.value.readOnlyInfos;
        didChangeTypes.instanceType = state.GetComponentTypeHandle<GameItemData>(true);
        didChangeTypes.typeType = state.GetComponentTypeHandle<GameItemType>(true);
        didChangeTypes.results = results.AsParallelWriter();

        ref var lookupJobManager = ref __itemManager.lookupJobManager;

        jobHandle = didChangeTypes.ScheduleParallel(__group, JobHandle.CombineDependencies(lookupJobManager.readOnlyJobHandle, jobHandle));

        ApplyChangedTypes applyChangedTypes;
        applyChangedTypes.manager = __itemManager.value;
        applyChangedTypes.results = results;

        jobHandle = applyChangedTypes.Schedule(JobHandle.CombineDependencies(lookupJobManager.readWriteJobHandle, jobHandle));

        lookupJobManager.readWriteJobHandle = jobHandle;

        state.Dependency = jobHandle;
    }
}
