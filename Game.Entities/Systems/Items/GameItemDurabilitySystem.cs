using System;
using Unity.Jobs;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Entities;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using ZG;
using Unity.Mathematics;
using System.Collections.Generic;

[assembly: RegisterGenericJobType(typeof(GameItemComponentInit<GameItemDurability, GameItemDurabilityInitSystem.Initializer>))]
[assembly: RegisterGenericJobType(typeof(GameItemComponentChange<GameItemDurability, GameItemDurabilityInitSystem.Initializer>))]
[assembly: RegisterGenericJobType(typeof(GameItemComponentApply<GameItemDurability>))]

#if DEBUG
[assembly: RegisterEntityCommandProducerJob(typeof(GameItemComponentInit<GameItemDurability, GameItemDurabilityInitSystem.Initializer>))]
#endif

[Serializable]
public struct GameItemDurability : IGameItemComponentData<GameItemDurability>
{
    public float value;

    public void Mul(float value)
    {
        this.value *= value;
    }

    public void Add(in GameItemDurability value)
    {
        this.value += value.value;
    }

    public GameItemDurability Diff(in GameItemDurability value)
    {
        GameItemDurability result;
        result.value = math.max(this.value, value.value) - math.min(this.value, value.value);
        return result;
    }
}

[UpdateInGroup(typeof(TimeSystemGroup)), UpdateAfter(typeof(GameSyncSystemGroup))]
public partial class GameWeaponSystem : LookupSystem
{
    [Serializable]
    public struct Result
    {
        public Entity entity;
        public GameWeaponResult value;
    }

    [Serializable]
    public struct Value
    {
        public float value;
        public Entity entity;
    }

    [Serializable]
    public struct Weapon
    {
        public float damage;

        public float damageToBeUsed;

        public float damageToBeHurt;

        public float damageRate;

        public float max;
    }

    private struct UpdateDurability
    {
        public float deltaTime;

        public GameItemManager.Hierarchy hierarchy;

        [ReadOnly]
        public NativeParallelHashMap<int, Weapon> weapons;

        [ReadOnly]
        public SharedHashMap<Entity, Entity>.Reader handleEntities;

        [ReadOnly]
        public ComponentLookup<GameItemDurability> durabilities;

        [ReadOnly]
        public NativeArray<Entity> entityArray;

        [ReadOnly]
        public NativeArray<GameItemRoot> itemRoots;

        [ReadOnly]
        public NativeArray<GameEntityActorHit> actorHits;

        public NativeQueue<Result>.ParallelWriter results;

        public void Execute(int index)
        {
            GameItemRoot itemRoot = itemRoots[index];

            if (!hierarchy.TryGetValue(itemRoot.handle, out var item))
                return;

            //int numResults = results.Length, i;

            float value;//, durability;
            Entity entity = entityArray[index], itemEntity;
            Weapon weapon;
            GameItemHandle handle;
            GameItemInfo temp;
            Result result;
            GameItemChild child;
            NativeParallelMultiHashMap<int, GameItemChild>.Enumerator enumerator;
            var actorHit = actorHits[index];
            do
            {
                handle = item.siblingHandle;
                if (hierarchy.GetChildren(handle, out enumerator, out _))
                {
                    while(enumerator.MoveNext())
                    {
                        child = enumerator.Current;
                        if (hierarchy.TryGetValue(child.handle, out temp) && 
                            weapons.TryGetValue(temp.type, out weapon) &&
                            handleEntities.TryGetValue(GameItemStructChangeFactory.Convert(child.handle), out itemEntity) && 
                            durabilities.HasComponent(itemEntity))
                        {
                            value = weapon.damage * deltaTime;

                            value += actorHit.sourceTimes * weapon.damageToBeUsed;

                            value += actorHit.destinationHit * weapon.damageToBeHurt;

                            value += actorHit.sourceHit * weapon.damageRate;/*math.select(
                                0.0f,
                                math.select(
                                    0.0f,
                                    1.0f - math.saturate((math.min(weapon.fullHit, info.hit) - weapon.overHit) / weapon.fullHit), 
                                    weapon.fullHit > math.FLT_MIN_NORMAL) * weapon.damageRate, 
                                info.hit > 0.0f);*/

                            if (math.abs(value) > math.FLT_MIN_NORMAL)
                            {
                                /*durability = durabilities[entity].value;
                                for (i = 0; i < numResults; ++i)
                                {
                                    result = results[i];
                                    if (result.handle.Equals(child.handle))
                                        durability -= result.value;
                                }

                                value = durability - math.clamp(durability - value, 0.0f, weapon.max * temp.count);*/

                                //UnityEngine.Debug.Log($"{value} : {weapon.damageRate}");

                                result.entity = entity;
                                result.value.value = value;
                                result.value.handle = child.handle;

                                results.Enqueue(result);
                            }
                        }

                    }
                }
            } while (hierarchy.TryGetValue(handle, out item));
        }
    }

    [BurstCompile]
    private struct UpdateDurabilityEx : IJobChunk
    {
        public float deltaTime;

        public GameItemManager.Hierarchy hierarchy;

        [ReadOnly]
        public NativeParallelHashMap<int, Weapon> weapons;

        [ReadOnly]
        public SharedHashMap<Entity, Entity>.Reader handleEntities;

        [ReadOnly]
        public ComponentLookup<GameItemDurability> durabilities;

        [ReadOnly]
        public EntityTypeHandle entityType;

        [ReadOnly]
        public ComponentTypeHandle<GameItemRoot> itemRootType;
        [ReadOnly]
        public ComponentTypeHandle<GameEntityActorHit> actorHitType;

        public NativeQueue<Result>.ParallelWriter results;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            UpdateDurability updateDurability;
            updateDurability.deltaTime = deltaTime;
            updateDurability.hierarchy = hierarchy;
            updateDurability.weapons = weapons;
            updateDurability.handleEntities = handleEntities;
            updateDurability.durabilities = durabilities;

            updateDurability.entityArray = chunk.GetNativeArray(entityType);
            updateDurability.itemRoots = chunk.GetNativeArray(ref itemRootType);
            updateDurability.actorHits = chunk.GetNativeArray(ref actorHitType);

            updateDurability.results = results;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                updateDurability.Execute(i);
        }
    }

    [BurstCompile]
    private struct Collect : IJob
    {
        public GameItemManager.Hierarchy hierarchy;

        public NativeQueue<Result> inputs;

        public NativeParallelHashMap<GameItemHandle, Value> outputs;

        [ReadOnly]
        public SharedHashMap<Entity, Entity>.Reader handleEntities;

        [ReadOnly]
        public NativeParallelHashMap<int, Weapon> weapons;

        [ReadOnly]
        public ComponentLookup<GameItemDurability> durabilities;

        public void Execute()
        {
            float durability;
            Value value;
            Entity entity;
            Weapon weapon;
            GameItemInfo temp;
            while (inputs.TryDequeue(out var result))
            {
                if(hierarchy.TryGetValue(result.value.handle, out temp) &&
                            weapons.TryGetValue(temp.type, out weapon) &&
                            handleEntities.TryGetValue(GameItemStructChangeFactory.Convert(result.value.handle), out entity))
                {
                    durability = durabilities[entity].value;
                    if (!outputs.TryGetValue(result.value.handle, out value))
                        value.value = 0.0f;

                    value.value += result.value.value;

                    value.value = durability - math.clamp(durability - value.value, 0.0f, weapon.max * temp.count);

                    value.entity = result.entity;

                    outputs[result.value.handle] = value;
                }
            }
        }
    }

    private EntityQuery __structChangeManagerGroup;
    private EntityQuery __group;
    private GameUpdateTime __time;
    private GameItemManagerShared __itemManager;

    private NativeQueue<Result> __results;

    private NativeParallelHashMap<int, Weapon> __weapons;

    public NativeParallelHashMap<GameItemHandle, Value> values
    {
        get;

        private set;
    }

    public bool Create(IEnumerable<KeyValuePair<int, Weapon>> weapons)
    {
        if (__weapons.IsCreated)
            return false;

        __weapons = new NativeParallelHashMap<int, Weapon>(1, Allocator.Persistent);

        bool result;
        foreach (KeyValuePair<int, Weapon> pair in weapons)
        {
            result = __weapons.TryAdd(pair.Key, pair.Value);

            UnityEngine.Assertions.Assert.IsTrue(result);
        }

        return true;
    }

    protected override void OnCreate()
    {
        base.OnCreate();

        __structChangeManagerGroup = GameItemStructChangeManager.GetEntityQuery(ref this.GetState());

        __group = GetEntityQuery(
            ComponentType.ReadOnly<GameItemRoot>(),
            ComponentType.ReadOnly<GameEntityActorHit>(),
            ComponentType.Exclude<Disabled>());

        __time = new GameUpdateTime(ref this.GetState());

        World world = World;

        __itemManager = world.GetOrCreateSystemUnmanaged<GameItemSystem>().manager;

        __results = new NativeQueue<Result>(Allocator.Persistent);

        values = new NativeParallelHashMap<GameItemHandle, Value>(1, Allocator.Persistent);
    }

    protected override void OnDestroy()
    {
        if (__weapons.IsCreated)
            __weapons.Dispose();

        __results.Dispose();

        values.Dispose();

        base.OnDestroy();
    }

    protected override void _Update()
    {
        if (!__weapons.IsCreated)
            return;

        if (!__itemManager.isCreated)
            return;

        if (!__time.IsVail())
            return;

        if (__group.IsEmptyIgnoreFilter)
            return;

        var handleEntities = __structChangeManagerGroup.GetSingleton<GameItemStructChangeManager>().handleEntities;

        var hierarchy = __itemManager.value.hierarchy;
        var handleEntitiesReader = handleEntities.reader;
        var durabilities = GetComponentLookup<GameItemDurability>(true);

        UpdateDurabilityEx updateDurability;
        updateDurability.deltaTime = __time.delta;
        updateDurability.hierarchy = hierarchy;
        updateDurability.weapons = __weapons;
        updateDurability.handleEntities = handleEntitiesReader;
        updateDurability.durabilities = durabilities;
        updateDurability.entityType = GetEntityTypeHandle();
        updateDurability.itemRootType = GetComponentTypeHandle<GameItemRoot>(true);
        updateDurability.actorHitType = GetComponentTypeHandle<GameEntityActorHit>(true);
        updateDurability.results = __results.AsParallelWriter();

        ref var itemJobManager = ref __itemManager.lookupJobManager;
        ref var entityHandleJobManager = ref handleEntities.lookupJobManager;

        var jobHandle = JobHandle.CombineDependencies(itemJobManager.readOnlyJobHandle, entityHandleJobManager.readOnlyJobHandle, Dependency);
        jobHandle = updateDurability.ScheduleParallel(__group, jobHandle);

        Collect collect;
        collect.hierarchy = hierarchy;
        collect.inputs = __results;
        collect.outputs = values;
        collect.handleEntities = handleEntitiesReader;
        collect.weapons = __weapons;
        collect.durabilities = durabilities;
        jobHandle = collect.Schedule(jobHandle);

        itemJobManager.AddReadOnlyDependency(jobHandle);
        entityHandleJobManager.AddReadOnlyDependency(jobHandle);

        Dependency = jobHandle;
    }
}

[BurstCompile, UpdateInGroup(typeof(GameItemComponentInitSystemGroup), OrderFirst = true)]
public struct GameItemDurabilityInitSystem : IGameItemInitializationSystem<GameItemDurability, GameItemDurabilityInitSystem.Initializer>
{
    public struct Initializer : IGameItemInitializer<GameItemDurability>
    {
        [ReadOnly]
        public UnsafeParallelHashMap<int, float> values;

        public bool IsVail(int type) => values.ContainsKey(type);

        public GameItemDurability GetValue(int type, int count)
        {
            GameItemDurability value;
            value.value = values[type];
            value.value *= count;

            return value;
        }
    }

    private GameItemComponentInitSystemCore<GameItemDurability> __core;
    private UnsafeParallelHashMap<int, float> __values;

    public Initializer initializer
    {
        get
        {
            Initializer initializer;
            initializer.values = __values;
            return initializer;
        }
    }

    public void Create(Tuple<int, float>[] values)
    {
        foreach (var value in values)
            __values.Add(value.Item1, value.Item2);
    }

    public void OnCreate(ref SystemState state)
    {
        __core = new GameItemComponentInitSystemCore<GameItemDurability>(ref state);

        __values = new UnsafeParallelHashMap<int, float>(1, Allocator.Persistent);

#if DEBUG
        EntityCommandUtility.RegisterProducerJobType<GameItemComponentInit<GameItemDurability, Initializer>>();
#endif
    }

    public void OnDestroy(ref SystemState state)
    {
        __values.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        __core.Update(initializer, ref state);
    }
}

[BurstCompile, UpdateInGroup(typeof(GameItemSystemGroup), OrderLast = true)]
public partial struct GameItemDurabilityChangeSystem : ISystem
{
    private GameItemComponentDataChangeSystemCore<GameItemDurability, GameItemDurabilityInitSystem.Initializer> __core;
    private GameItemDurabilityInitSystem.Initializer __initializer;

    public SharedList<GameItemChangeResult<GameItemDurability>> results => __core.results;

    public void OnCreate(ref SystemState state)
    {
        __core = new GameItemComponentDataChangeSystemCore<GameItemDurability, GameItemDurabilityInitSystem.Initializer>(ref state);

        __initializer = state.World.GetOrCreateSystemUnmanaged<GameItemDurabilityInitSystem>().initializer;
    }

    public void OnDestroy(ref SystemState state)
    {
        __core.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        __core.Update(__initializer, ref state);
    }
}

[BurstCompile, UpdateInGroup(typeof(GameItemInitSystemGroup), OrderLast = true)]
public partial struct GameItemDurabilityApplySystem : ISystem
{
    private GameItemComponentDataApplySystemCore<GameItemDurability> __core;
    private SharedList<GameItemChangeResult<GameItemDurability>> __results;

    public void OnCreate(ref SystemState state)
    {
        __core = new GameItemComponentDataApplySystemCore<GameItemDurability>(ref state);

        __results = state.World.GetOrCreateSystemUnmanaged<GameItemDurabilityChangeSystem>().results;
    }

    public void OnDestroy(ref SystemState state)
    {
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        __core.Update(ref __results, ref state);
    }
}

[AlwaysUpdateSystem, UpdateInGroup(typeof(PresentationSystemGroup))]//UpdateInGroup(typeof(InitializationSystemGroup)), UpdateAfter(typeof(EntityObjectSystemGroup))]
public partial class GameWeaponCallbackSystem : SystemBase
{
    private EntityQuery __group;
    private GameWeaponSystem __system;

    protected override void OnCreate()
    {
        base.OnCreate();

        __group = GetEntityQuery(ComponentType.ReadOnly<GameWeaponCallback>(), ComponentType.Exclude<GameItemRoot>());

        __system = World.GetOrCreateSystemManaged<GameWeaponSystem>();
    }

    protected override void OnUpdate()
    {
        var entityManager = EntityManager;
        if (!__group.IsEmptyIgnoreFilter)
        {
            //TODO: 
            CompleteDependency();

            using(var callbacks = __group.ToComponentDataArray<GameWeaponCallback>(Allocator.Temp))
            {
                foreach (var callback in callbacks)
                    callback.value.Unregister();
            }

            entityManager.RemoveComponent<GameWeaponCallback>(__group);
        }

        __system.CompleteReadWriteDependency();

        var values = __system.values;
        using (var keyValueArrays = values.GetKeyValueArrays(Allocator.Temp))
        {
            int length = keyValueArrays.Length;
            GameWeaponSystem.Value value;
            GameWeaponResult result;
            for (int i = 0; i < length; ++i)
            {
                value = keyValueArrays.Values[i];
                if (!entityManager.HasComponent<GameWeaponCallback>(value.entity))
                       continue;

                result.value = value.value;
                result.handle = keyValueArrays.Keys[i];

                entityManager.GetComponentData<GameWeaponCallback>(value.entity).value.Invoke(result);
            }
        }
        values.Clear();
    }
}