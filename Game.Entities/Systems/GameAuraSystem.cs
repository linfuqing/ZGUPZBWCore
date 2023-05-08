using Unity.Jobs;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Entities;
using Unity.Collections;
using Unity.Transforms;
using Unity.Physics;
using ZG;
using System.Collections.Generic;

[BurstCompile, UpdateInGroup(typeof(GameUpdateSystemGroup)), UpdateAfter(typeof(GameNodeCharacterSystemGroup))]
public partial struct GameAuraSystem : ISystem
{
    public struct Item
    {
        public GameActionTargetType targetType;

        public int flag;

        public uint layerMask;

        public float radius;

        public float time;
    }

    private struct Collector : ICollector<DistanceHit>
    {
        private int __index;
        private int __flag;
        private GameActionTargetType __targetType;
        private double __time;
        private GameEntityNode __node;
        private ComponentLookup<GameEntityCamp> __camps;
        private BufferLookup<GameAuraOrigin> __inputs;
        private NativeFactory<EntityData<GameAuraOrigin>>.ParallelWriter __outputs;

        public bool EarlyOutOnFirstHit => false;

        public float MaxFraction
        {
            get;
        }

        public int NumHits
        {
            get;

            private set;
        }

        public Collector(
            int index, 
            int flag, 
            GameActionTargetType targetType, 
            float maxFraction,
            double time, 
            in GameEntityNode node, 
            in ComponentLookup<GameEntityCamp> camps,
            in BufferLookup<GameAuraOrigin> inputs, 
            ref NativeFactory<EntityData<GameAuraOrigin>>.ParallelWriter outputs)
        {
            __index = index;
            __flag = flag;
            __targetType = targetType;

            MaxFraction = maxFraction;

            __time = time;

            __node = node;

            __camps = camps;

            __inputs = inputs;

            __outputs = outputs;

            NumHits = 0;
        }

        public bool AddHit(DistanceHit hit)
        {
            var entity = hit.Entity;
            if (!__inputs.HasBuffer(entity) || !__camps.HasComponent(entity))
                return false;

            GameEntityNode node;
            node.camp = __camps[entity].value;
            node.entity = entity;
            if (!__node.Predicate(__targetType, node))
                return false;

            var inputs = __inputs[entity];
            GameAuraOrigin input;
            int length = inputs.Length;
            for(int i = 0; i < length; ++i)
            {
                input = inputs[i];
                if (input.itemIndex == __index)
                    return false;
            }

            EntityData<GameAuraOrigin> output;
            output.entity = entity;
            output.value.itemIndex = __index;
            output.value.flag = __flag;
            output.value.time = __time;
            output.value.entity = __node.entity;

            __outputs.Create().value = output;

            ++NumHits;

            return true;
        }
    }

    private struct Clear
    {
        public double time;
        public BufferAccessor<GameAuraOrigin> origins;

        public void Execute(int index)
        {
            var origins = this.origins[index];

            int numOrigins = origins.Length;
            for (int i = 0; i < numOrigins; ++i)
            {
                if (origins[i].time > time)
                    continue;

                origins.RemoveAt(i--);

                --numOrigins;
            }
        }
    }

    [BurstCompile]
    private struct ClearEx : IJobChunk
    {
        public double time;
        public BufferTypeHandle<GameAuraOrigin> originType;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Clear clear;
            clear.time = time;
            clear.origins = chunk.GetBufferAccessor(ref originType);

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                clear.Execute(i);
        }
    }

    private struct Apply
    {
        public double time;

        [ReadOnly]
        public CollisionWorld collisionWorld;

        [ReadOnly]
        public NativeHashMap<int, Item> items;

        [ReadOnly]
        public NativeArray<Entity> entityArray;

        [ReadOnly]
        public NativeArray<Translation> translations;

        [ReadOnly]
        public NativeArray<GameEntityCamp> camps;

        [ReadOnly]
        public BufferAccessor<GameEntityItem> entityItems;

        [ReadOnly]
        public ComponentLookup<GameEntityCamp> campMap;

        [ReadOnly]
        public BufferLookup<GameAuraOrigin> inputs;

        public NativeFactory<EntityData<GameAuraOrigin>>.ParallelWriter outputs;

        public void Execute(int index)
        {
            GameEntityNode node;
            node.camp = camps[index].value;
            node.entity = entityArray[index];

            PointDistanceInput pointDistanceInput = default;
            pointDistanceInput.Filter = CollisionFilter.Default;
            pointDistanceInput.Position = translations[index].Value;

            Collector collector;

            var entityItems = this.entityItems[index];
            GameEntityItem entityItem;
            Item item;
            int numEntityItems = entityItems.Length;
            for(int i = 0; i < numEntityItems; ++i)
            {
                entityItem = entityItems[i];
                if(items.TryGetValue(entityItem.index, out item))
                {
                    pointDistanceInput.MaxDistance = item.radius;
                    pointDistanceInput.Filter.CollidesWith = item.layerMask;

                    collector = new Collector(entityItem.index, item.flag, item.targetType, item.radius, time + item.time, node, campMap, inputs, ref outputs);
                    collisionWorld.CalculateDistance(pointDistanceInput, ref collector);
                }
            }
        }
    }

    [BurstCompile]
    private struct ApplyEx : IJobChunk
    {
        public double time;

        [ReadOnly]
        public CollisionWorldContainer collisionWorld;

        [ReadOnly]
        public NativeHashMap<int, Item> items;

        [ReadOnly]
        public EntityTypeHandle entityType;

        [ReadOnly]
        public ComponentTypeHandle<Translation> translationType;

        [ReadOnly]
        public ComponentTypeHandle<GameEntityCamp> campType;

        [ReadOnly]
        public BufferTypeHandle<GameEntityItem> entityItemType;

        [ReadOnly]
        public ComponentLookup<GameEntityCamp> camps;

        [ReadOnly]
        public BufferLookup<GameAuraOrigin> inputs;

        public NativeFactory<EntityData<GameAuraOrigin>>.ParallelWriter outputs;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Apply apply;
            apply.time = time;
            apply.collisionWorld = collisionWorld;
            apply.items = items;
            apply.entityArray = chunk.GetNativeArray(entityType);
            apply.translations = chunk.GetNativeArray(ref translationType);
            apply.camps = chunk.GetNativeArray(ref campType);
            apply.entityItems = chunk.GetBufferAccessor(ref entityItemType);
            apply.campMap = camps;
            apply.inputs = inputs;
            apply.outputs = outputs;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                apply.Execute(i);
        }
    }

    [BurstCompile]
    private struct MoveTo : IJob
    {
        public NativeFactory<EntityData<GameAuraOrigin>> inputs;

        public BufferLookup<GameAuraOrigin> outputs;

        public void Execute()
        {
            foreach(var origin in inputs)
            {
                if (outputs.HasBuffer(origin.entity))
                    outputs[origin.entity].Add(origin.value);
            }

            inputs.Clear();
        }
    }

    private EntityQuery __originGroup;
    private EntityQuery __itemGroup;
    private SharedPhysicsWorld __physicsWorld;
    private NativeHashMap<int, Item> __items;
    private NativeFactory<EntityData<GameAuraOrigin>> __origins;

    public void Create(IEnumerable<KeyValuePair<int, Item>> items)
    {
        __items = new NativeHashMap<int, Item>(1, Allocator.Persistent);
        foreach(var item in items)
            __items.Add(item.Key, item.Value);
    }

    public void OnCreate(ref SystemState state)
    {
        BurstUtility.InitializeJob<MoveTo>();

        __originGroup = state.GetEntityQuery(
            ComponentType.ReadWrite<GameAuraOrigin>());

        __itemGroup = state.GetEntityQuery(
            ComponentType.ReadOnly<Translation>(), 
            ComponentType.ReadOnly<GameEntityCamp>(), 
            ComponentType.ReadOnly<GameEntityItem>());

        __physicsWorld = state.World.GetOrCreateSystemUnmanaged<GamePhysicsWorldBuildSystem>().physicsWorld;

        __origins = new NativeFactory<EntityData<GameAuraOrigin>>(Allocator.Persistent, true);
    }

    public void OnDestroy(ref SystemState state)
    {
        if (__items.IsCreated)
            __items.Dispose();

        __origins.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!__items.IsCreated)
            return;

        double time = state.WorldUnmanaged.Time.ElapsedTime;
        NativeFactory<EntityData<GameAuraOrigin>> origins = __origins;

        ClearEx clear;
        clear.time = time;
        clear.originType = state.GetBufferTypeHandle<GameAuraOrigin>();

        var jobHandle = clear.ScheduleParallel(__originGroup, state.Dependency);

        ApplyEx apply;
        apply.time = time;
        apply.collisionWorld = __physicsWorld.collisionWorld;
        apply.items = __items;
        apply.entityType = state.GetEntityTypeHandle();
        apply.translationType = state.GetComponentTypeHandle<Translation>(true);
        apply.campType = state.GetComponentTypeHandle<GameEntityCamp>(true);
        apply.entityItemType = state.GetBufferTypeHandle<GameEntityItem>(true);
        apply.camps = state.GetComponentLookup<GameEntityCamp>(true);
        apply.inputs = state.GetBufferLookup<GameAuraOrigin>(true);
        apply.outputs = origins.parallelWriter;

        ref var lookupJobManager = ref __physicsWorld.lookupJobManager;

        jobHandle = apply.ScheduleParallel(__itemGroup, JobHandle.CombineDependencies(lookupJobManager.readOnlyJobHandle, jobHandle));

        lookupJobManager.AddReadOnlyDependency(jobHandle);

        MoveTo moveTo;
        moveTo.inputs = origins;
        moveTo.outputs = state.GetBufferLookup<GameAuraOrigin>();

        state.Dependency = moveTo.Schedule(jobHandle);
    }
}
