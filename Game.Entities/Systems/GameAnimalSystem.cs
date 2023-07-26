using System;
using Unity.Jobs;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Collections;
using ZG;
using Random = Unity.Mathematics.Random;

/*[assembly: RegisterGenericJobType(typeof(ZG.TimeManager<EntityData<GameAnimalBuff>>.Clear))]
[assembly: RegisterGenericJobType(typeof(ZG.TimeManager<EntityData<GameAnimalBuff>>.UpdateEvents))]*/

[assembly: RegisterGenericJobType(typeof(BuffAdd<float, GameAnimalBuff>))]
[assembly: RegisterGenericJobType(typeof(BuffSubtract<float, GameAnimalBuff>))]

[BurstCompile, UpdateInGroup(typeof(TimeSystemGroup)), UpdateBefore(typeof(GameAnimalSystem))]
public partial struct GameAnimalBuffSystem : ISystem
{
    public SharedBuffManager<float, GameAnimalBuff> manager
    {
        get;

        private set;
    }

    public void OnCreate(ref SystemState state)
    {
        manager = new SharedBuffManager<float, GameAnimalBuff>(Allocator.Persistent);
    }

    public void OnDestroy(ref SystemState state)
    {
        manager.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        manager.Update(state.WorldUnmanaged.Time.ElapsedTime, ref state);
    }
}

[UpdateInGroup(typeof(TimeSystemGroup)), UpdateAfter(typeof(GameEntityHealthSystem)), UpdateAfter(typeof(GameWeaponSystem))]
public partial class GameAnimalSystem : SystemBase
{
    [Serializable]
    public struct Result
    {
        public int value;
        public Entity entity;
    }

    private struct Item
    {
        public int type;
        public int count;
        public GameItemHandle handle;
    }

    private struct RandomItemHandler : IRandomItemHandler
    {
        public GameItemHandle handle;
        public Random random;
        public BlobAssetReference<GameAnimalFoodsDefinition> definition;
        public NativeQueue<Item>.ParallelWriter items;

        public RandomResult Set(int startIndex, int count)
        {
            Item item;
            item.handle = handle;

            ref var items = ref definition.Value.items;
            for (int i = 0; i < count; ++i)
            {
                ref var origin = ref items[startIndex + i];
                if (origin.type == -1)
                    continue;

                item.type = origin.type;
                item.count = random.NextInt(origin.min, origin.max);
                this.items.Enqueue(item);
            }

            return RandomResult.Success;
        }
    }

    private struct UpdateNodes
    {
        public double time;

        public Random random;

        public BlobAssetReference<GameAnimalFoodsDefinition> definition;

        public GameItemManager.ReadOnly itemManager;

        [ReadOnly]
        public BufferAccessor<GameAnimalFoodIndex> foodIndices;
        [ReadOnly]
        public NativeArray<Entity> entityArray;
        [ReadOnly]
        public NativeArray<GameItemRoot> itemRoots;
        [ReadOnly]
        public NativeArray<GameNodeStatus> states;
        [ReadOnly]
        public NativeArray<GameNodeDelay> delay;
        [ReadOnly]
        public NativeArray<GameCreatureData> creatures;
        [ReadOnly]
        public NativeArray<GameCreatureFood> foods;
        /*[ReadOnly]
        public NativeArray<GameEntityCommandVersion> versions;

        public NativeArray<GameAnimalEntityCommandVersion> entityVersions;*/

        public NativeArray<GameAnimalFoodTime> foodTimes;

        public NativeQueue<Item>.ParallelWriter items;

        public NativeQueue<Result>.ParallelWriter results;

        public void Execute(int index)
        {
            var foodTime = foodTimes[index];
            if (foodTime.value > time)
                return;

            if (delay[index].Check(time))
                return;

            switch (states[index].value & (int)GameEntityStatus.Mask)
            {
                case 0:
                case (int)GameEntityStatus.KnockedOut:
                    break;
                default:
                    return;
            }

            /*var entityVersion = entityVersions[index];
            int version = versions[index].value;
            if (version == entityVersion.value)
                return;*/

            var itemRoot = itemRoots[index];

            RandomItemHandler randomItemHandler;
            randomItemHandler.handle = itemRoot.handle;
            randomItemHandler.random = random;
            randomItemHandler.definition = this.definition;
            randomItemHandler.items = items;

            float value = creatures[index].foodMax - foods[index].value;

            Result result;
            var foodIndices = this.foodIndices[index];
            ref var definition = ref this.definition.Value;
            int numFoodIndices = foodIndices.Length, count, i;
            for (i = 0; i < numFoodIndices; ++i)
            {
                ref var food = ref definition.foods[foodIndices[i].value];
                if (food.threshold > value)
                    continue;

                count = 1;
                if (!itemManager.Contains(itemRoot.handle, food.itemType, ref count))
                    continue;

                foodTime.value = time + food.time;

                foodTimes[index] = foodTime;

                if(food.randomGroupCount > 0)
                    random.Next(ref randomItemHandler, definition.randomGroups.AsArray().Slice(food.startRandomGroupIndex, food.randomGroupCount));

                result.value = i;
                result.entity = entityArray[index];
                results.Enqueue(result);

                break;
            }

            /*if (i < numFoodIndices)
            {
                entityVersion.value = version;

                entityVersions[index] = entityVersion;
            }*/
        }
    }

    [BurstCompile]
    private struct UpdateNodesEx : IJobChunk
    {
        public double time;

        public BlobAssetReference<GameAnimalFoodsDefinition> definition;

        public GameItemManager.ReadOnly itemManager;

        [ReadOnly]
        public EntityTypeHandle entityType;

        [ReadOnly]
        public ComponentTypeHandle<GameItemRoot> itemRootType;
        [ReadOnly]
        public ComponentTypeHandle<GameNodeStatus> statusType;
        [ReadOnly]
        public ComponentTypeHandle<GameNodeDelay> delayType;
        [ReadOnly]
        public ComponentTypeHandle<GameCreatureData> creatureType;
        [ReadOnly]
        public ComponentTypeHandle<GameCreatureFood> foodType;
        [ReadOnly]
        public BufferTypeHandle<GameAnimalFoodIndex> foodIndexType;
        /*[ReadOnly]
        public ComponentTypeHandle<GameEntityCommandVersion> versionType;

        public ComponentTypeHandle<GameAnimalEntityCommandVersion> entityVersionType;*/

        public ComponentTypeHandle<GameAnimalFoodTime> foodTimeType;

        public NativeQueue<Item>.ParallelWriter items;

        public NativeQueue<Result>.ParallelWriter results;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            UpdateNodes updateNodes;
            updateNodes.time = time;

            long hash = math.aslong(time);
            updateNodes.random = new Random((uint)hash ^ (uint)(hash >> 32) ^ (uint)unfilteredChunkIndex);
            updateNodes.definition = definition;
            updateNodes.itemManager = itemManager;
            updateNodes.foodIndices = chunk.GetBufferAccessor(ref foodIndexType);
            updateNodes.entityArray = chunk.GetNativeArray(entityType);
            updateNodes.itemRoots = chunk.GetNativeArray(ref itemRootType);
            updateNodes.states = chunk.GetNativeArray(ref statusType);
            updateNodes.delay = chunk.GetNativeArray(ref delayType);
            updateNodes.creatures = chunk.GetNativeArray(ref creatureType);
            updateNodes.foods = chunk.GetNativeArray(ref foodType);
            /*updateNodes.versions = chunk.GetNativeArray(versionType);
            updateNodes.entityVersions = chunk.GetNativeArray(entityVersionType);*/
            updateNodes.foodTimes = chunk.GetNativeArray(ref foodTimeType);
            updateNodes.items = items;
            updateNodes.results = results;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                updateNodes.Execute(i);
        }
    }

    [BurstCompile]
    private struct ApplyItems : IJob
    {
        public NativeQueue<Item> items;

        public GameItemManager itemManager;

        public void Execute()
        {
            int count, parentChildIndex;
            GameItemHandle parentHandle;
            while (items.TryDequeue(out var item))
            {
                if (itemManager.Find(item.handle, item.type, item.count, out parentChildIndex, out parentHandle))
                {
                    count = item.count;

                    itemManager.Add(parentHandle, parentChildIndex, item.type, ref count);
                }
            }
        }
    }

    [BurstCompile]
    private struct UpdateInfos
    {
        public float deltaTime;
        [ReadOnly]
        public NativeArray<GameNodeStatus> states;
        [ReadOnly]
        public NativeArray<GameAnimalData> instances;
        [ReadOnly]
        public NativeArray<GameAnimalBuff> buffs;
        public NativeArray<GameAnimalInfo> infos;

        public void Execute(int index)
        {
            var instance = instances[index];
            bool isOnKnockedOut = (states[index].value & (int)GameEntityStatus.Mask) == (int)GameEntityStatus.KnockedOut;
            float buffValue = buffs[index].value + (isOnKnockedOut ? instance.valueOnKnockedOut : instance.valueOnNormal);

            var info = infos[index];
            //int source = (int)math.round(info.value);
            info.value = math.clamp((float)(info.value + buffValue * deltaTime), 0.0f, instance.max);
            //int destination = (int)math.round(info.value);

            infos[index] = info;
            //if (source != destination)
        }
    }

    [BurstCompile]
    private struct UpdateInfosEx : IJobChunk
    {
        public float deltaTime;

        [ReadOnly]
        public ComponentTypeHandle<GameNodeStatus> statusType;

        [ReadOnly]
        public ComponentTypeHandle<GameAnimalData> instanceType;

        [ReadOnly]
        public ComponentTypeHandle<GameAnimalBuff> buffType;

        public ComponentTypeHandle<GameAnimalInfo> infoType;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            UpdateInfos updateInfos;
            updateInfos.deltaTime = deltaTime;
            updateInfos.states = chunk.GetNativeArray(ref statusType);
            updateInfos.instances = chunk.GetNativeArray(ref instanceType);
            updateInfos.buffs = chunk.GetNativeArray(ref buffType);
            updateInfos.infos = chunk.GetNativeArray(ref infoType);

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                updateInfos.Execute(i);
        }
    }

    private JobHandle __jobHandle;

    private EntityQuery __nodeGroup;
    private EntityQuery __infoGroup;
    private NativeQueue<Item> __items;
    private NativeQueue<Result> __results;
    private GameItemManagerShared __itemManager;

    public NativeQueue<Result> results
    {
        get
        {
            __jobHandle.Complete();
            __jobHandle = default;

            return __results;
        }
    }

    protected override void OnCreate()
    {
        __nodeGroup = GetEntityQuery(
            ComponentType.ReadOnly<GameAnimalFoodIndex>(),
            ComponentType.ReadOnly<GameItemRoot>(),
            ComponentType.ReadOnly<GameNodeStatus>(),
            ComponentType.ReadOnly<GameNodeDelay>(),
            ComponentType.ReadOnly<GameCreatureData>(),
            ComponentType.ReadOnly<GameCreatureFood>(),
            ComponentType.ReadWrite<GameAnimalFoodTime>(),
            ComponentType.Exclude<Disabled>());

        __infoGroup = GetEntityQuery(
            ComponentType.ReadOnly<GameNodeStatus>(),
            ComponentType.ReadOnly<GameAnimalData>(),
            ComponentType.ReadOnly<GameAnimalBuff>(),
            ComponentType.ReadWrite<GameAnimalInfo>(),
            ComponentType.Exclude<Disabled>());

        __items = new NativeQueue<Item>(Allocator.Persistent);

        __results = new NativeQueue<Result>(Allocator.Persistent);

        World world = World;

        __itemManager = world.GetOrCreateSystemUnmanaged<GameItemSystem>().manager;
    }

    protected override void OnDestroy()
    {
        __items.Dispose();

        __results.Dispose();
    }

    protected override void OnUpdate()
    {
        var inputDeps = Dependency;
        JobHandle? result;
        if (__itemManager.isCreated && SystemAPI.HasSingleton<GameAnimalFoodsData>())
        {
            var itemManager = __itemManager.value;

            UpdateNodesEx updateNodes;
            updateNodes.time = World.Time.ElapsedTime;
            updateNodes.definition = SystemAPI.GetSingleton<GameAnimalFoodsData>().definition;
            updateNodes.itemManager = itemManager.readOnly;
            updateNodes.entityType = GetEntityTypeHandle();
            updateNodes.itemRootType = GetComponentTypeHandle<GameItemRoot>(true);
            updateNodes.statusType = GetComponentTypeHandle<GameNodeStatus>(true);
            updateNodes.delayType = GetComponentTypeHandle<GameNodeDelay>(true);
            updateNodes.creatureType = GetComponentTypeHandle<GameCreatureData>(true);
            updateNodes.foodType = GetComponentTypeHandle<GameCreatureFood>(true);
            updateNodes.foodIndexType = GetBufferTypeHandle<GameAnimalFoodIndex>(true);
            //updateNodes.versionType = GetComponentTypeHandle<GameEntityCommandVersion>(true);
            //updateNodes.entityVersionType = GetComponentTypeHandle<GameAnimalEntityCommandVersion>();
            updateNodes.foodTimeType = GetComponentTypeHandle<GameAnimalFoodTime>();
            updateNodes.items = __items.AsParallelWriter();
            updateNodes.results = __results.AsParallelWriter();

            ref var lookupJobManager = ref __itemManager.lookupJobManager;

            __jobHandle = updateNodes.ScheduleParallel(__nodeGroup, JobHandle.CombineDependencies(lookupJobManager.readOnlyJobHandle, inputDeps));

            ApplyItems applyItems;
            applyItems.items = __items;
            applyItems.itemManager = itemManager;

            var jobHandle = applyItems.Schedule(JobHandle.CombineDependencies(__jobHandle, lookupJobManager.readWriteJobHandle));

            lookupJobManager.readWriteJobHandle = jobHandle;

            result = jobHandle;
        }
        else
            result = null;

        UpdateInfosEx updateInfos;
        updateInfos.deltaTime = World.Time.DeltaTime;
        updateInfos.statusType = GetComponentTypeHandle<GameNodeStatus>(true);
        updateInfos.instanceType = GetComponentTypeHandle<GameAnimalData>(true);
        updateInfos.buffType = GetComponentTypeHandle<GameAnimalBuff>(true);
        updateInfos.infoType = GetComponentTypeHandle<GameAnimalInfo>();

        var temp = updateInfos.ScheduleParallel(__infoGroup, inputDeps);
        if (result != null)
            temp = JobHandle.CombineDependencies(temp, result.Value);

        Dependency = temp;
    }
}

//[UpdateInGroup(typeof(TimeSystemGroup), OrderFirst = true), UpdateBefore(typeof(BeginTimeSystemGroupEntityCommandSystem))]
[UpdateInGroup(typeof(PresentationSystemGroup)), UpdateBefore(typeof(CallbackSystem))]
public partial class GameAnimalEventSystem : SystemBase
{
    private GameAnimalSystem __animalSystem;

    protected override void OnCreate()
    {
        base.OnCreate();

        __animalSystem = World.GetOrCreateSystemManaged<GameAnimalSystem>();
    }

    protected override void OnUpdate()
    {
        var entityManager = EntityManager;
        var results = __animalSystem.results;
        while (results.TryDequeue(out var result))
        {
            if (!entityManager.HasComponent<EntityObject<GameAnimalComponent>>(result.entity))
                continue;

            entityManager.GetComponentData<EntityObject<GameAnimalComponent>>(result.entity).value._OnEat(result.value);
        }
    }
}