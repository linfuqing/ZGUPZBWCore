using System;
using Unity.Jobs;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Transforms;
using ZG;
using Unity.Collections.LowLevel.Unsafe;

public struct GameLocator : IComponentData
{
}

public struct GameLocation : IBufferElementData
{
    public float radiusSq;
    public float3 position;
    public CallbackHandle<Entity> enter;
    public CallbackHandle<Entity> exit;
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
        public bool isNew;
        public CallbackHandle<Entity> enter;
        public CallbackKey key;
    }

    private struct Locate
    {
        [ReadOnly]
        public NativeList<ArchetypeChunk> locators;

        [ReadOnly]
        public EntityTypeHandle entityType;

        [ReadOnly]
        public ComponentTypeHandle<Translation> translationType;

        [ReadOnly]
        public NativeArray<Entity> entityArray;

        [ReadOnly]
        public BufferAccessor<GameLocation> locations;

        public NativeQueue<Result>.ParallelWriter results;

        public NativeParallelHashMap<CallbackKey, CallbackHandle<Entity>>.ParallelWriter exits;

        public void Execute(int index)
        {
            Result result;
            result.key.location = entityArray[index];

            NativeArray<Translation> locatorTranslations;
            NativeArray<Entity> locatorEntities;
            var locations = this.locations[index];
            float3 position;
            int numLocations = locations.Length, numLocators, i;
            foreach (var location in locations)
            {
                foreach (var locatorChunk in locators)
                {
                    locatorTranslations = locatorChunk.GetNativeArray(ref translationType);

                    locatorEntities = locatorChunk.GetNativeArray(entityType);

                    numLocators = locatorChunk.Count;
                    for (i = 0; i < numLocators; ++i)
                    {
                        result.key.locator = locatorEntities[i];

                        position = locatorTranslations[i].Value;

                        if (location.radiusSq < math.distancesq(location.position, position))
                            continue;

                        result.isNew = exits.TryAdd(result.key, location.exit);
                        result.enter = location.enter;
                        results.Enqueue(result);
                    }
                }
            }
        }
    }

    [BurstCompile]
    private struct LocateEx : IJobChunk
    {
        [ReadOnly]
        public NativeList<ArchetypeChunk> locators;

        [ReadOnly]
        public EntityTypeHandle entityType;

        [ReadOnly]
        public ComponentTypeHandle<Translation> translationType;

        [ReadOnly]
        public BufferTypeHandle<GameLocation> locationType;

        public NativeQueue<Result>.ParallelWriter results;

        public NativeParallelHashMap<CallbackKey, CallbackHandle<Entity>>.ParallelWriter exits;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Locate locate;
            locate.locators = locators;
            locate.translationType = translationType;
            locate.entityType = entityType;
            locate.entityArray = chunk.GetNativeArray(entityType);
            locate.locations = chunk.GetBufferAccessor(ref locationType);
            locate.results = results;
            locate.exits = exits;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                locate.Execute(i);
        }
    }

    [BurstCompile]
    private struct Apply : IJob
    {
        public NativeParallelHashMap<CallbackKey, CallbackHandle<Entity>> exits;
        public NativeQueue<Result> results;
        public NativeList<CallbackValue> callbacks;

        public void Execute()
        {
            callbacks.Clear();

            UnsafeHashSet<CallbackKey> enterKeys = default;
            CallbackValue callbackValue;
            while (results.TryDequeue(out var result))
            {
                if(!enterKeys.IsCreated)
                    enterKeys = new UnsafeHashSet<CallbackKey>(1, Allocator.Temp);

                enterKeys.Add(result.key);

                if (result.isNew)
                {
                    callbackValue.entity = result.key.locator;
                    callbackValue.handle = result.enter;
                    callbacks.Add(callbackValue);
                }
            }

            if(enterKeys.IsCreated)
            {
                using (var exitKeys = exits.GetKeyArray(Allocator.Temp))
                {
                    foreach(var exitKey in exitKeys)
                    {
                        if(!enterKeys.Contains(exitKey))
                        {
                            callbackValue.entity = exitKey.locator;
                            callbackValue.handle = exits[exitKey];
                            callbacks.Add(callbackValue);

                            exits.Remove(exitKey);
                        }
                    }
                }
            }
        }
    }

    private EntityQuery __locatorGroup;
    private EntityQuery __locationGroup;

    private EntityTypeHandle __entityType;
    private ComponentTypeHandle<Translation> __translationType;
    private BufferTypeHandle<GameLocation> __locationType;

    private NativeParallelHashMap<CallbackKey, CallbackHandle<Entity>> __exits;
    private NativeQueue<Result> __results;
    private NativeList<CallbackValue> __callbacks;

    protected override void OnCreate()
    {
        base.OnCreate();

        __locatorGroup = GetEntityQuery(
            ComponentType.ReadOnly<Translation>(),
            ComponentType.ReadOnly<GameLocator>());

        __locationGroup = GetEntityQuery(ComponentType.ReadOnly<GameLocation>());

        __entityType = GetEntityTypeHandle();
        __translationType = GetComponentTypeHandle<Translation>(true);
        __locationType = GetBufferTypeHandle<GameLocation>(true);

        __exits = new NativeParallelHashMap<CallbackKey, CallbackHandle<Entity>>(1, Allocator.Persistent);
        __results = new NativeQueue<Result>(Allocator.Persistent);
        __callbacks = new NativeList<CallbackValue>(Allocator.Persistent);
    }

    protected override void OnDestroy()
    {
        __exits.Dispose();
        __results.Dispose();
        __callbacks.Dispose();

        base.OnDestroy();
    }

    protected override void OnUpdate()
    {
        foreach(var callback in __callbacks.AsArray())
            callback.handle.Invoke(callback.entity);

        if (!__locatorGroup.IsEmptyIgnoreFilter && !__locationGroup.IsEmptyIgnoreFilter)
        {
            ref var state = ref this.GetState();

            LocateEx locate;
            locate.locators = __locatorGroup.ToArchetypeChunkListAsync(WorldUpdateAllocator, out var jobHandle);
            locate.entityType = __entityType.UpdateAsRef(ref state);
            locate.translationType = __translationType.UpdateAsRef(ref state);
            locate.locationType = __locationType.UpdateAsRef(ref state);
            locate.results = __results.AsParallelWriter();
            locate.exits = __exits.AsParallelWriter();

            jobHandle = locate.ScheduleParallel(__locationGroup, JobHandle.CombineDependencies(jobHandle, Dependency));

            Apply apply;
            apply.exits = __exits;
            apply.results = __results;
            apply.callbacks = __callbacks;
            Dependency = apply.Schedule(jobHandle);
        }
    }
}
