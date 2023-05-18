using System;
using Unity.Jobs;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Transforms;
using ZG;

public struct GameLocationCallbackData
{
    public int id;
    public Entity location;
    public Entity locator;
}

public struct GameLocator : IComponentData
{
}

public struct GameLocation : IBufferElementData
{
    public int id;
    public Entity locator;
    public CallbackHandle<GameLocationCallbackData> exit;
    public double time;
}

public struct GameLocationData : IBufferElementData
{
    public int id;
    public float radiusSq;
    public float3 position;
    public CallbackHandle<GameLocationCallbackData> enter;
    public CallbackHandle<GameLocationCallbackData> exit;
}

public partial class GameLocationSystem : SystemBase
{
    private struct Result
    {
        public int id;

        public Entity location;
        public Entity locator;

        public CallbackHandle<GameLocationCallbackData> callback;
    }

    private struct Locate
    {
        public double time;

        [ReadOnly]
        public NativeList<ArchetypeChunk> locators;

        [ReadOnly]
        public EntityTypeHandle entityType;

        [ReadOnly]
        public ComponentTypeHandle<Translation> translationType;

        [ReadOnly]
        public NativeArray<Entity> entityArray;

        [ReadOnly]
        public BufferAccessor<GameLocationData> instances;

        public BufferAccessor<GameLocation> locations;

        public NativeQueue<Result>.ParallelWriter results;

        public void Execute(int index)
        {
            Result result;
            result.location = entityArray[index];

            GameLocation location;
            location.time = time;

            NativeArray<Translation> locatorTranslations;
            NativeArray<Entity> locatorEntities;
            var instances = this.instances[index];
            var locations = this.locations[index];
            int numLocations = locations.Length, numLocators, locationIndex, i;
            foreach (var instance in instances)
            {
                result.id = instance.id;

                location.id = instance.id;

                foreach (var locatorChunk in locators)
                {
                    locatorTranslations = locatorChunk.GetNativeArray(ref translationType);

                    locatorEntities = locatorChunk.GetNativeArray(entityType);

                    numLocators = locatorChunk.Count;
                    for (i = 0; i < numLocators; ++i)
                    {
                        result.locator = locatorEntities[i];

                        if (instance.radiusSq < math.distancesq(instance.position, locatorTranslations[i].Value))
                            continue;

                        locationIndex = FindIndex(instance.id, locatorEntities[i], ref locations);
                        if (locationIndex == -1)
                        {
                            location.locator = result.locator;
                            location.exit = instance.exit;

                            locations.Add(location);

                            result.callback = instance.enter;
                            results.Enqueue(result);
                        }
                        else
                            locations.ElementAt(locationIndex).time = time;
                    }
                }
            }

            for(i = 0; i < numLocations; ++i)
            {
                ref var oldLocation = ref locations.ElementAt(i);
                if (oldLocation.time < time)
                {
                    result.locator = oldLocation.locator;
                    result.id = oldLocation.id;
                    result.callback = oldLocation.exit;
                    results.Enqueue(result);

                    locations.RemoveAtSwapBack(i--);

                    --numLocations;
                }
            }
        }

        public static int FindIndex(int id, in Entity locator, ref DynamicBuffer<GameLocation> locations)
        {
            int numLocations = locations.Length;
            for(int i = 0; i < numLocations; ++i)
            {
                ref var location = ref locations.ElementAt(i);

                if (location.id == id && location.locator == locator)
                    return i;
            }

            return -1;
        }
    }

    [BurstCompile]
    private struct LocateEx : IJobChunk
    {
        public double time;

        [ReadOnly]
        public NativeList<ArchetypeChunk> locators;

        [ReadOnly]
        public EntityTypeHandle entityType;

        [ReadOnly]
        public ComponentTypeHandle<Translation> translationType;

        [ReadOnly]
        public BufferTypeHandle<GameLocationData> instanceType;

        public BufferTypeHandle<GameLocation> locationType;

        public NativeQueue<Result>.ParallelWriter results;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Locate locate;
            locate.time = time;
            locate.locators = locators;
            locate.translationType = translationType;
            locate.entityType = entityType;
            locate.entityArray = chunk.GetNativeArray(entityType);
            locate.instances = chunk.GetBufferAccessor(ref instanceType);
            locate.locations = chunk.GetBufferAccessor(ref locationType);
            locate.results = results;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                locate.Execute(i);
        }
    }

    private EntityQuery __locatorGroup;
    private EntityQuery __locationGroup;

    private EntityTypeHandle __entityType;
    private ComponentTypeHandle<Translation> __translationType;
    private BufferTypeHandle<GameLocationData> __instanceType;
    private BufferTypeHandle<GameLocation> __locationType;

    private NativeQueue<Result> __results;

    protected override void OnCreate()
    {
        base.OnCreate();

        __locatorGroup = GetEntityQuery(
            ComponentType.ReadOnly<Translation>(),
            ComponentType.ReadOnly<GameLocator>());

        __locationGroup = GetEntityQuery(ComponentType.ReadOnly<Translation>(), ComponentType.ReadOnly<GameLocationData>(), ComponentType.ReadWrite<GameLocation>());

        __entityType = GetEntityTypeHandle();
        __translationType = GetComponentTypeHandle<Translation>(true);
        __instanceType = GetBufferTypeHandle<GameLocationData>(true);
        __locationType = GetBufferTypeHandle<GameLocation>();

        __results = new NativeQueue<Result>(Allocator.Persistent);
    }

    protected override void OnDestroy()
    {
        __results.Dispose();

        base.OnDestroy();
    }

    protected override void OnUpdate()
    {
        GameLocationCallbackData callbackData;
        while (__results.TryDequeue(out var result))
        {
            callbackData.id = result.id;
            callbackData.location = result.location;
            callbackData.locator = result.locator;
            result.callback.Invoke(callbackData);
        }

        if (!__locatorGroup.IsEmptyIgnoreFilter && !__locationGroup.IsEmptyIgnoreFilter)
        {
            ref var state = ref this.GetState();

            LocateEx locate;
            locate.time = SystemAPI.Time.ElapsedTime;
            locate.locators = __locatorGroup.ToArchetypeChunkListAsync(WorldUpdateAllocator, out var jobHandle);
            locate.entityType = __entityType.UpdateAsRef(ref state);
            locate.translationType = __translationType.UpdateAsRef(ref state);
            locate.instanceType = __instanceType.UpdateAsRef(ref state);
            locate.locationType = __locationType.UpdateAsRef(ref state);
            locate.results = __results.AsParallelWriter();

            Dependency = locate.ScheduleParallel(__locationGroup, JobHandle.CombineDependencies(jobHandle, Dependency));
        }
    }
}
