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

[BurstCompile, 
    CreateAfter(typeof(GameAnimalEventSystem)), 
    CreateAfter(typeof(GameItemSystem)), 
    UpdateInGroup(typeof(TimeSystemGroup)), 
    UpdateAfter(typeof(GameEntityHealthSystem)), 
    UpdateAfter(typeof(GameWeaponSystem))]
public partial struct GameAnimalSystem : ISystem
{
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

        public EntityCommandQueue<Result>.ParallelWriter results;

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
            int numFoodIndices = foodIndices.Length, foodIndex, count, i;
            for (i = 0; i < numFoodIndices; ++i)
            {
                foodIndex = foodIndices[i].value;
                if (foodIndex < 0 || foodIndex >= definition.foods.Length)
                {
                    UnityEngine.Debug.LogError($"{entityArray[i]} food index {i} == {foodIndex}");
                    
                    continue;
                }

                ref var food = ref definition.foods[foodIndex];
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
    private struct UpdateNodesEx : IJobChunk, IEntityCommandProducerJob
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

        public EntityCommandQueue<Result>.ParallelWriter results;

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

    private EntityQuery __nodeGroup;
    private EntityQuery __infoGroup;

    private EntityTypeHandle __entityType;

    private ComponentTypeHandle<GameItemRoot> __itemRootType;
    private ComponentTypeHandle<GameNodeStatus> __statusType;
    private ComponentTypeHandle<GameNodeDelay> __delayType;
    private ComponentTypeHandle<GameCreatureData> __creatureType;
    private ComponentTypeHandle<GameCreatureFood> __foodType;

    private BufferTypeHandle<GameAnimalFoodIndex> __foodIndexType;

    private ComponentTypeHandle<GameAnimalFoodTime> __foodTimeType;

    private ComponentTypeHandle<GameAnimalData> __instanceType;

    private ComponentTypeHandle<GameAnimalBuff> __buffType;

    private ComponentTypeHandle<GameAnimalInfo> __infoType;

    private NativeQueue<Item> __items;
    private GameItemManagerShared __itemManager;
    private EntityCommandPool<Result> __results;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __nodeGroup = builder
                    .WithAll<GameAnimalFoodIndex, GameItemRoot, GameNodeStatus, GameNodeDelay>()
                    .WithAll<GameCreatureData, GameCreatureFood>()
                    .WithAllRW<GameAnimalFoodTime>()
                    .Build(ref state);

        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __infoGroup = builder
                .WithAll<GameNodeStatus, GameAnimalData, GameAnimalBuff>()
                .WithAllRW<GameAnimalInfo>()
                .Build(ref state);

        __items = new NativeQueue<Item>(Allocator.Persistent);

        __entityType = state.GetEntityTypeHandle();
        __itemRootType = state.GetComponentTypeHandle<GameItemRoot>(true);
        __statusType = state.GetComponentTypeHandle<GameNodeStatus>(true);
        __delayType = state.GetComponentTypeHandle<GameNodeDelay>(true);
        __creatureType = state.GetComponentTypeHandle<GameCreatureData>(true);
        __foodType = state.GetComponentTypeHandle<GameCreatureFood>(true);
        __foodIndexType = state.GetBufferTypeHandle<GameAnimalFoodIndex>(true);
        //updateNodes.versionType = GetComponentTypeHandle<GameEntityCommandVersion>(true);
        //updateNodes.entityVersionType = GetComponentTypeHandle<GameAnimalEntityCommandVersion>();
        __foodTimeType = state.GetComponentTypeHandle<GameAnimalFoodTime>();

        __instanceType = state.GetComponentTypeHandle<GameAnimalData>(true);
        __buffType = state.GetComponentTypeHandle<GameAnimalBuff>(true);
        __infoType = state.GetComponentTypeHandle<GameAnimalInfo>();

        var world = state.WorldUnmanaged;

        __itemManager = world.GetExistingSystemUnmanaged<GameItemSystem>().manager;
        __results = world.GetExistingSystemUnmanaged<GameAnimalEventSystem>().pool;
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        __items.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var statusType = __statusType.UpdateAsRef(ref state);

        var inputDeps = state.Dependency;
        JobHandle? result;
        if (__itemManager.isCreated && SystemAPI.HasSingleton<GameAnimalFoodsData>())
        {
            var itemManager = __itemManager.value;

            var results = __results.Create();

            UpdateNodesEx updateNodes;
            updateNodes.time = state.WorldUnmanaged.Time.ElapsedTime;
            updateNodes.definition = SystemAPI.GetSingleton<GameAnimalFoodsData>().definition;
            updateNodes.itemManager = itemManager.readOnly;
            updateNodes.entityType = __entityType.UpdateAsRef(ref state);
            updateNodes.itemRootType = __itemRootType.UpdateAsRef(ref state);
            updateNodes.statusType = statusType;
            updateNodes.delayType = __delayType.UpdateAsRef(ref state);
            updateNodes.creatureType = __creatureType.UpdateAsRef(ref state);
            updateNodes.foodType = __foodType.UpdateAsRef(ref state);
            updateNodes.foodIndexType = __foodIndexType.UpdateAsRef(ref state);
            //updateNodes.versionType = GetComponentTypeHandle<GameEntityCommandVersion>(true);
            //updateNodes.entityVersionType = GetComponentTypeHandle<GameAnimalEntityCommandVersion>();
            updateNodes.foodTimeType = __foodTimeType.UpdateAsRef(ref state);
            updateNodes.items = __items.AsParallelWriter();
            updateNodes.results = results.parallelWriter;

            ref var lookupJobManager = ref __itemManager.lookupJobManager;

            var jobHandle = updateNodes.ScheduleParallelByRef(__nodeGroup, JobHandle.CombineDependencies(lookupJobManager.readOnlyJobHandle, inputDeps));

            results.AddJobHandleForProducer<UpdateNodesEx>(jobHandle);

            ApplyItems applyItems;
            applyItems.items = __items;
            applyItems.itemManager = itemManager;

            jobHandle = applyItems.ScheduleByRef(JobHandle.CombineDependencies(jobHandle, lookupJobManager.readWriteJobHandle));

            lookupJobManager.readWriteJobHandle = jobHandle;

            result = jobHandle;
        }
        else
            result = null;

        UpdateInfosEx updateInfos;
        updateInfos.deltaTime = state.WorldUnmanaged.Time.DeltaTime;
        updateInfos.statusType = statusType;
        updateInfos.instanceType = __instanceType.UpdateAsRef(ref state);
        updateInfos.buffType = __buffType.UpdateAsRef(ref state);
        updateInfos.infoType = __infoType.UpdateAsRef(ref state);

        var temp = updateInfos.ScheduleParallelByRef(__infoGroup, inputDeps);
        if (result != null)
            temp = JobHandle.CombineDependencies(temp, result.Value);

        state.Dependency = temp;
    }
}

//TODO: Delete
//[UpdateInGroup(typeof(TimeSystemGroup), OrderFirst = true), UpdateBefore(typeof(BeginTimeSystemGroupEntityCommandSystem))]
[BurstCompile, UpdateInGroup(typeof(CallbackSystemGroup), OrderFirst = true)]
public partial struct GameAnimalEventSystem : ISystem
{
    private EntityCommandPool<GameAnimalSystem.Result>.Context __context;

    public EntityCommandPool<GameAnimalSystem.Result> pool => __context.pool;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __context = new EntityCommandPool<GameAnimalSystem.Result>.Context(Allocator.Persistent);
    }

    public void OnDestroy(ref SystemState state)
    {
        __context.Dispose();
    }

    public void OnUpdate(ref SystemState state)
    {
        var entityManager = state.EntityManager;
        while (__context.TryDequeue(out var result))
        {
            if (!entityManager.HasComponent<EntityObject<GameAnimalComponent>>(result.entity))
                continue;

            entityManager.GetComponentData<EntityObject<GameAnimalComponent>>(result.entity).value._OnEat(result.value);
        }
    }
}