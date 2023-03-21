using System;
using Unity.Jobs;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Transforms;
using ZG;

[Serializable]
public struct GameLocator : IComponentData
{
}

[Serializable]
public struct GameLocation : IBufferElementData
{
    public float radiusSq;
    public float3 position;
    public CallbackHandle<Entity> callbackHandle;
}

public partial class GameLocationSystem : SystemBase
{
    private struct CallbackKey : IEquatable<CallbackKey>
    {
        public Entity locator;
        public Entity location;

        public bool Equals(CallbackKey other)
        {
            return locator == other.locator && location == other.location;
        }

        public override int GetHashCode()
        {
            return locator.GetHashCode() ^ location.GetHashCode();
        }
    }

    private struct CallbackValue
    {
        public Entity entity;
        public CallbackHandle<Entity> handle;
    }

    private struct Result
    {
        public bool isKeep;
        public CallbackHandle<Entity> callbackHandle;
        public CallbackKey key;
    }

    private struct Locate
    {
        [ReadOnly]
        public NativeParallelHashMap<CallbackKey, CallbackHandle<Entity>> callbackHandles;

        [ReadOnly, DeallocateOnJobCompletion]
        public NativeArray<Entity> locators;

        [ReadOnly, DeallocateOnJobCompletion]
        public NativeArray<Translation> translations;

        [ReadOnly]
        public NativeArray<Entity> entityArray;

        [ReadOnly]
        public BufferAccessor<GameLocation> locations;

        public NativeQueue<Result>.ParallelWriter results;

        public void Execute(int index)
        {
            Result result;
            result.key.location = entityArray[index];

            var locations = this.locations[index];
            GameLocation location;
            float3 position;
            int numLocations = locations.Length, numLocators = locators.Length, i, j;
            for (i = 0; i < numLocators; ++i)
            {
                result.key.locator = locators[i];

                position = translations[i].Value;
                for(j = 0; j < numLocations; ++j)
                {
                    location = locations[j];

                    if (location.radiusSq < math.distancesq(location.position, position))
                        continue;

                    result.isKeep = callbackHandles.TryGetValue(result.key, out result.callbackHandle) && result.callbackHandle.Equals(location.callbackHandle);
                    result.callbackHandle = location.callbackHandle;
                    results.Enqueue(result);

                    break;
                }
            }
        }
    }

    [BurstCompile]
    private struct LocateEx : IJobChunk
    {
        [ReadOnly]
        public NativeParallelHashMap<CallbackKey, CallbackHandle<Entity>> callbackHandles;

        [ReadOnly]
        public NativeList<Entity> locators;

        [ReadOnly]
        public NativeList<Translation> translations;

        [ReadOnly]
        public EntityTypeHandle entityType;

        [ReadOnly]
        public BufferTypeHandle<GameLocation> locationType;

        public NativeQueue<Result>.ParallelWriter results;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Locate locate;
            locate.callbackHandles = callbackHandles;
            locate.locators = locators.AsArray();
            locate.translations = translations.AsArray();
            locate.entityArray = chunk.GetNativeArray(entityType);
            locate.locations = chunk.GetBufferAccessor(ref locationType);
            locate.results = results;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                locate.Execute(i);
        }
    }

    [BurstCompile]
    private struct Apply : IJob
    {
        public NativeParallelHashMap<CallbackKey, CallbackHandle<Entity>> callbackHandles;
        public NativeQueue<Result> results;
        public NativeList<CallbackValue> callbackValues;

        public void Execute()
        {
            callbackValues.Clear();
            callbackHandles.Clear();

            CallbackValue callbackValue;
            while (results.TryDequeue(out var result))
            {
                callbackHandles[result.key] = result.callbackHandle;

                if (!result.isKeep)
                {
                    callbackValue.entity = result.key.locator;
                    callbackValue.handle = result.callbackHandle;
                    callbackValues.Add(callbackValue);
                }
            }
        }
    }

    private EntityQuery __locatorGroup;
    private EntityQuery __locationGroup;

    private NativeParallelHashMap<CallbackKey, CallbackHandle<Entity>> __callbackHandles;
    private NativeQueue<Result> __results;
    private NativeList<CallbackValue> __callbackValues;

    protected override void OnCreate()
    {
        base.OnCreate();

        __locatorGroup = GetEntityQuery(
            ComponentType.ReadOnly<Translation>(),
            ComponentType.ReadOnly<GameLocator>());

        __locationGroup = GetEntityQuery(ComponentType.ReadOnly<GameLocation>());

        __callbackHandles = new NativeParallelHashMap<CallbackKey, CallbackHandle<Entity>>(1, Allocator.Persistent);
        __results = new NativeQueue<Result>(Allocator.Persistent);
        __callbackValues = new NativeList<CallbackValue>(Allocator.Persistent);
    }

    protected override void OnDestroy()
    {
        __callbackHandles.Dispose();
        __results.Dispose();
        __callbackValues.Dispose();

        base.OnDestroy();
    }

    protected override void OnUpdate()
    {
        foreach(var callbackValue in __callbackValues.AsArray())
            callbackValue.handle.Invoke(callbackValue.entity);

        if (!__locatorGroup.IsEmptyIgnoreFilter && !__locationGroup.IsEmptyIgnoreFilter)
        {
            LocateEx locate;
            locate.callbackHandles = __callbackHandles;
            locate.locators = __locatorGroup.ToEntityListAsync(WorldUpdateAllocator, out var entityJobHandle);
            locate.translations = __locatorGroup.ToComponentDataListAsync<Translation>(WorldUpdateAllocator, out var translationJobHandle);
            locate.entityType = GetEntityTypeHandle();
            locate.locationType = GetBufferTypeHandle<GameLocation>(true);
            locate.results = __results.AsParallelWriter();

            var jobHandle = locate.ScheduleParallel(__locationGroup, JobHandle.CombineDependencies(entityJobHandle, translationJobHandle, Dependency));

            Apply apply;
            apply.callbackHandles = __callbackHandles;
            apply.results = __results;
            apply.callbackValues = __callbackValues;
            Dependency = apply.Schedule(jobHandle);
        }
    }
}
