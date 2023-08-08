using Unity.Jobs;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Collections;
using ZG;

[assembly: RegisterGenericJobType(typeof(TimeManager<EntityData<float>>.UpdateEvents))]

/*[assembly: RegisterGenericJobType(typeof(TimeManager<EntityData<GameCreatureFoodBuff>>.Clear))]
[assembly: RegisterGenericJobType(typeof(TimeManager<EntityData<GameCreatureFoodBuff>>.UpdateEvents))]

[assembly: RegisterGenericJobType(typeof(TimeManager<EntityData<GameCreatureWaterBuff>>.Clear))]
[assembly: RegisterGenericJobType(typeof(TimeManager<EntityData<GameCreatureWaterBuff>>.UpdateEvents))]*/

[assembly: RegisterGenericJobType(typeof(BuffAdd<float, GameCreatureFoodBuff>))]
[assembly: RegisterGenericJobType(typeof(BuffSubtract<float, GameCreatureFoodBuff>))]

[assembly: RegisterGenericJobType(typeof(BuffAdd<float, GameCreatureWaterBuff>))]
[assembly: RegisterGenericJobType(typeof(BuffSubtract<float, GameCreatureWaterBuff>))]

public struct GameCreatureItemsDefinition
{
    public struct Item
    {
        public float waterToHealthScale;
        public float waterToHealthSpeed;

        public float waterToTorpidityScale;
        public float waterToTorpiditySpeed;

        public static Item operator+(in Item x, in Item y)
        {
            Item result;
            result.waterToHealthScale = x.waterToHealthScale + y.waterToHealthScale;
            result.waterToHealthSpeed = x.waterToHealthSpeed + y.waterToHealthSpeed;

            result.waterToTorpidityScale = x.waterToTorpidityScale + y.waterToTorpidityScale;
            result.waterToTorpiditySpeed = x.waterToTorpiditySpeed + y.waterToTorpiditySpeed;

            return result;
        }
    }

    public BlobArray<Item> values;

    public Item Get(in GameItemHandle handle, in GameItemManager.Hierarchy hierarchy)
    {
        Item result = default;

        if (hierarchy.TryGetValue(handle, out var item))
            result += __GetChildren(item.siblingHandle, hierarchy);

        return result;
    }

    private Item __GetChildren(in GameItemHandle handle, in GameItemManager.Hierarchy hierarchy)
    {
        Item result = default;

        if(hierarchy.GetChildren(handle, out var children, out var item))
        {
            result += __GetChildren(item.siblingHandle, hierarchy);

            GameItemChild child;
            while (children.MoveNext())
            {
                child = children.Current;

                if (hierarchy.TryGetValue(child.handle, out item))
                {
                    result += values[item.type];

                    result += __GetChildren(item.siblingHandle, hierarchy);
                }
            }
        }

        return result;
    }
}

/*public struct GameCreatureItemsSharedData : IComponentData
{
    public BlobAssetReference<GameCreatureItemsDefinition> definition;
}*/

public struct GameCreatureItemsData : IComponentData
{
    public BlobAssetReference<GameCreatureItemsDefinition> definition;
}

[BurstCompile, AutoCreateIn("Server"), UpdateInGroup(typeof(TimeSystemGroup)), UpdateBefore(typeof(GameCreatureSystem))]
public partial struct GameCreatureBuffSystem : ISystem
{
    public SharedBuffManager<float, GameCreatureFoodBuff> foodManager
    {
        get;

        private set;
    }

    public SharedBuffManager<float, GameCreatureWaterBuff> waterManager
    {
        get;

        private set;
    }

    public void OnCreate(ref SystemState state)
    {
        foodManager = new SharedBuffManager<float, GameCreatureFoodBuff>(Allocator.Persistent);
        waterManager = new SharedBuffManager<float, GameCreatureWaterBuff>(Allocator.Persistent);
    }

    public void OnDestroy(ref SystemState state)
    {
        foodManager.Dispose();
        waterManager.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        double time = state.WorldUnmanaged.Time.ElapsedTime;

        var inputDeps = state.Dependency;

        foodManager.Update(time, ref state);

        var jobHandle = state.Dependency;

        state.Dependency = inputDeps;

        waterManager.Update(time, ref state);

        state.Dependency = JobHandle.CombineDependencies(state.Dependency, jobHandle);
    }
}

/*[BurstCompile, UpdateInGroup(typeof(EndFrameEntityCommandSystemGroup))]
public partial struct GameCreatureStructChangeSystem : ISystem
{
    [BurstCompile]
    private struct Init : IJobParallelFor
    {
        public BlobAssetReference<GameCreatureItemsDefinition> definition;

        [ReadOnly, DeallocateOnJobCompletion]
        public NativeArray<Entity> entityArray;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<GameCreatureItemsData> instances;

        public void Execute(int index)
        {
            GameCreatureItemsData instance;
            instance.definition = definition;
            instances[entityArray[index]] = instance;
        }
    }

    public static readonly int InnerloopBatchCount = 1;

    private EntityQuery __definitionGroup;
    private EntityQuery __instanceGroup;

    public void OnCreate(ref SystemState state)
    {
        BurstUtility.InitializeJobParallelFor<Init>();

        __definitionGroup = state.GetEntityQuery(ComponentType.ReadOnly<GameCreatureItemsSharedData>());

        __instanceGroup = state.GetEntityQuery(
            new EntityQueryDesc()
            {
                All = new ComponentType[]
                {
                    ComponentType.ReadOnly<GameCreatureData>()
                },
                None = new ComponentType[]
                {
                    typeof(GameCreatureItemsData)
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
        NativeArray<Entity> instanceEntities;
        if (__instanceGroup.IsEmptyIgnoreFilter)
            instanceEntities = default;
        else
        {
            instanceEntities = __instanceGroup.ToEntityArrayBurstCompatible(state.GetEntityTypeHandle(), Allocator.TempJob);

            state.EntityManager.AddComponent<GameCreatureItemsData>(__instanceGroup);
        }

        if (instanceEntities.IsCreated)
        {
            Init init;
            init.definition = __definitionGroup.GetSingleton<GameCreatureItemsSharedData>().definition;
            init.entityArray = instanceEntities;
            init.instances = state.GetComponentLookup<GameCreatureItemsData>();

            state.Dependency = init.ScheduleByRef(instanceEntities.Length, InnerloopBatchCount, state.Dependency);
        }
    }
}*/

[BurstCompile, CreateAfter(typeof(GameItemSystem)), UpdateInGroup(typeof(TimeSystemGroup)), UpdateBefore(typeof(GameEntityTorpiditySystem))]
public partial struct GameCreatureSystem : ISystem
{ 
    private struct UpdateAttributes
    {
        public float deltaTime;

        public BlobAssetReference<GameCreatureItemsDefinition> definition;

        [ReadOnly]
        public GameItemManager.Hierarchy hierarchy;

        [ReadOnly]
        public ComponentLookup<GameNodeParent> parents;

        [ReadOnly]
        public NativeArray<Entity> entityArray;

        [ReadOnly]
        public NativeArray<GameNodeStatus> states;

        [ReadOnly]
        public NativeArray<GameNodeVelocity> velocities;

        [ReadOnly]
        public NativeArray<GameNodeStaticThreshold> staticThresholds;

        [ReadOnly]
        public NativeArray<GameEntityHealthData> maxHealthes;

        [ReadOnly]
        public NativeArray<GameEntityHealth> healthes;

        [ReadOnly]
        public NativeArray<GameEntityTorpidityData> maxTorpidities;

        [ReadOnly]
        public NativeArray<GameEntityTorpidity> torpidities;

        [ReadOnly]
        public NativeArray<GameItemRoot> itemRoots;

        [ReadOnly]
        public NativeArray<GameCreatureData> instances;

        [ReadOnly]
        public NativeArray<GameCreatureTemperature> temperatures;

        [ReadOnly]
        public NativeArray<GameCreatureFoodBuffFromTemperature> foodBuffFromTemperatures;

        [ReadOnly]
        public NativeArray<GameCreatureWaterBuffFromTemperature> waterBuffFromTemperatures;

        [ReadOnly]
        public NativeArray<GameCreatureFoodBuff> foodBuffs;

        [ReadOnly]
        public NativeArray<GameCreatureWaterBuff> waterBuffs;

        public NativeArray<GameCreatureFood> foods;

        public NativeArray<GameCreatureWater> waters;
        
        public BufferAccessor<GameEntityHealthBuff> healthBuffs;

        public BufferAccessor<GameEntityTorpidityBuff> torpidityBuffs;

        public void Execute(int index)
        {
            Entity entity = entityArray[index];
            var status = states[index];
            var instance = instances[index];
            var temperature = temperatures[index];

            if(index < foodBuffFromTemperatures.Length)
                instance.foodBuffFromTemperature *= foodBuffFromTemperatures[index].scale;

            if (index < waterBuffFromTemperatures.Length)
                instance.waterBuffFromTemperature *= waterBuffFromTemperatures[index].scale;

            float value = index < velocities.Length ? velocities[index].value : 0.0f, staticThreshold = index < staticThresholds.Length ? staticThresholds[index].value : 0.0f;
            bool isKnockedOut = (status.value & (int)GameEntityStatus.Mask) == (int)GameEntityStatus.KnockedOut, isDynamic = value * value > staticThreshold && !parents.HasComponent(entity);
            //int source, destination;

            var foodBuff = foodBuffs[index];
            if (isKnockedOut)
                foodBuff.value += instance.foodBuffOnKnockedOut;

            if (isDynamic)
                foodBuff.value += instance.foodBuffOnMove;

            var food = foods[index];
            //心之种
            foodBuff.value = math.max(foodBuff.value * deltaTime, -food.value) / deltaTime;

            if (temperature.value < instance.temperatureMin)
                foodBuff.value += (instance.temperatureMin - temperature.value) * (isKnockedOut ? instance.foodBuffFromTemperatureOnKnockedOut : instance.foodBuffFromTemperature);

            //source = (int)math.round(food.value);
            food.value = math.min(food.value + foodBuff.value * deltaTime, instance.foodMax);
            if (food.value < 0.0f)
            {
                GameEntityHealthBuff healthBuff;
                healthBuff.value = -food.value * instance.healthBuffOnStarving / deltaTime;
                healthBuff.duration = deltaTime;

                var healthBuffs = this.healthBuffs[index];
                healthBuffs.Add(healthBuff);

                food.value = 0.0f;
            }

            //destination = (int)math.round(food.value);
            //if(source != destination)

            foods[index] = food;

            var waterBuff = waterBuffs[index];

            if (isKnockedOut)
                waterBuff.value += instance.waterBuffOnKnockedOut;

            if (isDynamic)
                waterBuff.value += instance.waterBuffOnMove;

            var water = waters[index];
            //心之种
            waterBuff.value = math.max(waterBuff.value * deltaTime, -water.value) / deltaTime;

            if (temperature.value > instance.temperatureMax)
                waterBuff.value += (temperature.value - instance.temperatureMax) * (isKnockedOut ? instance.waterBuffFromTemperatureOnKnockedOut : instance.waterBuffFromTemperature);

            //source = (int)math.round(water.value);
            water.value = math.min(water.value + waterBuff.value * deltaTime, instance.waterMax);
            if (water.value < 0.0f)
            {
                if (!isKnockedOut)
                {
                    GameEntityTorpidityBuff torpidityBuff;
                    torpidityBuff.value = -water.value * instance.torpidityBuffOnDehydrated / deltaTime;
                    torpidityBuff.duration = deltaTime;

                    var torpidityBuffs = this.torpidityBuffs[index];
                    torpidityBuffs.Add(torpidityBuff);
                }

                water.value = 0.0f;
            }
            else if (!isKnockedOut)
            {
                var item = index < itemRoots.Length ? definition.Value.Get(itemRoots[index].handle, hierarchy) : default;

                float health = maxHealthes[index].max - healthes[index].value;
                if (water.value > 0.0f && health > 0.0f && item.waterToHealthScale > math.FLT_MIN_NORMAL)
                {
                    float waterValue = item.waterToHealthSpeed > math.FLT_MIN_NORMAL ? math.min(water.value, deltaTime * item.waterToHealthSpeed) : water.value,
                        healthValue = waterValue * item.waterToHealthScale,
                        healthDeltaTime = deltaTime;

                    if (healthValue > health)
                    {
                        float scale = health / healthValue;
                        healthDeltaTime *= scale;
                        healthValue = health;

                        waterValue *= scale;
                    }

                    GameEntityHealthBuff healthBuff;
                    healthBuff.value = healthValue / healthDeltaTime;
                    healthBuff.duration = healthDeltaTime;

                    var healthBuffs = this.healthBuffs[index];
                    healthBuffs.Add(healthBuff);

                    water.value -= waterValue;
                }

                float torpidity = maxTorpidities[index].max - torpidities[index].value;
                if (water.value > 0.0f && torpidity > 0.0f && item.waterToTorpidityScale > math.FLT_MIN_NORMAL)
                {
                    float waterValue = item.waterToTorpiditySpeed > math.FLT_MIN_NORMAL ? math.min(water.value, deltaTime * item.waterToTorpiditySpeed) : water.value,
                        torpidityValue = waterValue * item.waterToTorpidityScale,
                        torpidityDeltaTime = deltaTime;

                    if (torpidityValue > health)
                    {
                        float scale = torpidity / torpidityValue;
                        torpidityDeltaTime *= scale;
                        torpidityValue = health;

                        waterValue *= scale;
                    }

                    GameEntityTorpidityBuff torpidityBuff;
                    torpidityBuff.value = torpidityValue;
                    torpidityBuff.duration = torpidityDeltaTime;

                    var torpidityBuffs = this.torpidityBuffs[index];
                    torpidityBuffs.Add(torpidityBuff);

                    water.value -= waterValue;
                }
            }

            //destination = (int)math.round(water.value);
            //if (source != destination)

            waters[index] = water;
        }
    }

    [BurstCompile]
    private struct UpdateAttributesEx : IJobChunk
    {
        public float deltaTime;

        public BlobAssetReference<GameCreatureItemsDefinition> definition;

        [ReadOnly]
        public GameItemManager.Hierarchy hierarchy;

        [ReadOnly]
        public ComponentLookup<GameNodeParent> parents;

        [ReadOnly]
        public EntityTypeHandle entityType;
        [ReadOnly]
        public ComponentTypeHandle<GameNodeStatus> statusType;
        [ReadOnly]
        public ComponentTypeHandle<GameNodeVelocity> velocitieType;
        [ReadOnly]
        public ComponentTypeHandle<GameNodeStaticThreshold> staticThresholdType;
        [ReadOnly]
        public ComponentTypeHandle<GameEntityHealthData> maxHealthType;
        [ReadOnly]
        public ComponentTypeHandle<GameEntityHealth> healtheType;
        [ReadOnly]
        public ComponentTypeHandle<GameEntityTorpidityData> maxTorpidityType;
        [ReadOnly]
        public ComponentTypeHandle<GameEntityTorpidity> torpidityType;
        [ReadOnly]
        public ComponentTypeHandle<GameItemRoot> itemRootType;
        [ReadOnly]
        public ComponentTypeHandle<GameCreatureData> instanceType;
        [ReadOnly]
        public ComponentTypeHandle<GameCreatureTemperature> temperatureType;
        [ReadOnly]
        public ComponentTypeHandle<GameCreatureFoodBuffFromTemperature> foodBuffFromTemperatureType;
        [ReadOnly]
        public ComponentTypeHandle<GameCreatureWaterBuffFromTemperature> waterBuffFromTemperatureType;
        [ReadOnly]
        public ComponentTypeHandle<GameCreatureFoodBuff> foodBuffType;
        [ReadOnly]
        public ComponentTypeHandle<GameCreatureWaterBuff> waterBuffType;

        public ComponentTypeHandle<GameCreatureFood> foodType;
        public ComponentTypeHandle<GameCreatureWater> waterType;
        public BufferTypeHandle<GameEntityHealthBuff> healthBuffType;
        public BufferTypeHandle<GameEntityTorpidityBuff> torpidityBuffType;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            UpdateAttributes updateAttributes;
            updateAttributes.deltaTime = deltaTime;
            updateAttributes.definition = definition;
            updateAttributes.hierarchy = hierarchy;
            updateAttributes.parents = parents;
            updateAttributes.entityArray = chunk.GetNativeArray(entityType);
            updateAttributes.states = chunk.GetNativeArray(ref statusType);
            updateAttributes.velocities = chunk.GetNativeArray(ref velocitieType);
            updateAttributes.staticThresholds = chunk.GetNativeArray(ref staticThresholdType);
            updateAttributes.maxHealthes = chunk.GetNativeArray(ref maxHealthType);
            updateAttributes.healthes = chunk.GetNativeArray(ref healtheType);
            updateAttributes.maxTorpidities = chunk.GetNativeArray(ref maxTorpidityType);
            updateAttributes.torpidities = chunk.GetNativeArray(ref torpidityType);
            updateAttributes.itemRoots = chunk.GetNativeArray(ref itemRootType);
            updateAttributes.instances = chunk.GetNativeArray(ref instanceType);
            updateAttributes.temperatures = chunk.GetNativeArray(ref temperatureType);
            updateAttributes.foodBuffFromTemperatures = chunk.GetNativeArray(ref foodBuffFromTemperatureType);
            updateAttributes.waterBuffFromTemperatures = chunk.GetNativeArray(ref waterBuffFromTemperatureType);
            updateAttributes.foodBuffs = chunk.GetNativeArray(ref foodBuffType);
            updateAttributes.waterBuffs = chunk.GetNativeArray(ref waterBuffType);
            updateAttributes.foods = chunk.GetNativeArray(ref foodType);
            updateAttributes.waters = chunk.GetNativeArray(ref waterType);
            updateAttributes.healthBuffs = chunk.GetBufferAccessor(ref healthBuffType);
            updateAttributes.torpidityBuffs = chunk.GetBufferAccessor(ref torpidityBuffType);

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                updateAttributes.Execute(i);
        }
    }

    private EntityQuery __group;
    private GameItemManagerShared __itemManager;

    private ComponentLookup<GameNodeParent> __parents;

    private EntityTypeHandle __entityType;
    private ComponentTypeHandle<GameNodeStatus> __statusType;
    private ComponentTypeHandle<GameNodeVelocity> __velocitieType;
    private ComponentTypeHandle<GameNodeStaticThreshold> __staticThresholdType;
    private ComponentTypeHandle<GameEntityHealthData> __maxHealthType;
    private ComponentTypeHandle<GameEntityHealth> __healtheType;
    private ComponentTypeHandle<GameEntityTorpidityData> __maxTorpidityType;
    private ComponentTypeHandle<GameEntityTorpidity> __torpidityType;
    private ComponentTypeHandle<GameItemRoot> __itemRootType;
    private ComponentTypeHandle<GameCreatureData> __instanceType;
    private ComponentTypeHandle<GameCreatureTemperature> __temperatureType;
    private ComponentTypeHandle<GameCreatureFoodBuffFromTemperature> __foodBuffFromTemperatureType;
    private ComponentTypeHandle<GameCreatureWaterBuffFromTemperature> __waterBuffFromTemperatureType;
    private ComponentTypeHandle<GameCreatureFoodBuff> __foodBuffType;
    private ComponentTypeHandle<GameCreatureWaterBuff> __waterBuffType;

    private ComponentTypeHandle<GameCreatureFood> __foodType;
    private ComponentTypeHandle<GameCreatureWater> __waterType;
    private BufferTypeHandle<GameEntityHealthBuff> __healthBuffType;
    private BufferTypeHandle<GameEntityTorpidityBuff> __torpidityBuffType;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __group = builder
                .WithAll<GameNodeStatus, GameNodeVelocity, GameCreatureData, GameCreatureTemperature, GameCreatureFoodBuff, GameCreatureWaterBuff>()
                .WithAllRW<GameCreatureFood>()
                .WithAllRW<GameCreatureWater>()
                .WithAllRW<GameEntityHealthBuff>()
                .WithAllRW<GameEntityTorpidityBuff>()
                .WithNone<GameCreatureDisabled>()
                .Build(ref state);

        __parents = state.GetComponentLookup<GameNodeParent>(true);
        __entityType = state.GetEntityTypeHandle();
        __statusType = state.GetComponentTypeHandle<GameNodeStatus>(true);
        __velocitieType = state.GetComponentTypeHandle<GameNodeVelocity>(true);
        __staticThresholdType = state.GetComponentTypeHandle<GameNodeStaticThreshold>(true);
        __maxHealthType = state.GetComponentTypeHandle<GameEntityHealthData>(true);
        __healtheType = state.GetComponentTypeHandle<GameEntityHealth>(true);
        __maxTorpidityType = state.GetComponentTypeHandle<GameEntityTorpidityData>(true);
        __torpidityType = state.GetComponentTypeHandle<GameEntityTorpidity>(true);
        __itemRootType = state.GetComponentTypeHandle<GameItemRoot>(true);
        __instanceType = state.GetComponentTypeHandle<GameCreatureData>(true);
        __temperatureType = state.GetComponentTypeHandle<GameCreatureTemperature>(true);
        __foodBuffFromTemperatureType = state.GetComponentTypeHandle<GameCreatureFoodBuffFromTemperature>(true);
        __waterBuffFromTemperatureType = state.GetComponentTypeHandle<GameCreatureWaterBuffFromTemperature>(true);
        __foodBuffType = state.GetComponentTypeHandle<GameCreatureFoodBuff>(true);
        __waterBuffType = state.GetComponentTypeHandle<GameCreatureWaterBuff>(true);
        __foodType = state.GetComponentTypeHandle<GameCreatureFood>();
        __waterType = state.GetComponentTypeHandle<GameCreatureWater>();
        __healthBuffType = state.GetBufferTypeHandle<GameEntityHealthBuff>();
        __torpidityBuffType = state.GetBufferTypeHandle<GameEntityTorpidityBuff>();

        __itemManager = state.WorldUnmanaged.GetExistingSystemUnmanaged<GameItemSystem>().manager;
    }

    public void OnDestroy(ref SystemState state)
    {

    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.HasSingleton<GameCreatureItemsData>())
            return;

        UpdateAttributesEx updateAttributes;
        updateAttributes.deltaTime = state.WorldUnmanaged.Time.DeltaTime;
        updateAttributes.definition = SystemAPI.GetSingleton<GameCreatureItemsData>().definition;
        updateAttributes.hierarchy = __itemManager.value.hierarchy;
        updateAttributes.parents = __parents.UpdateAsRef(ref state);
        updateAttributes.entityType = __entityType.UpdateAsRef(ref state);
        updateAttributes.statusType = __statusType.UpdateAsRef(ref state);
        updateAttributes.velocitieType = __velocitieType.UpdateAsRef(ref state);
        updateAttributes.staticThresholdType = __staticThresholdType.UpdateAsRef(ref state);
        updateAttributes.maxHealthType = __maxHealthType.UpdateAsRef(ref state);
        updateAttributes.healtheType = __healtheType.UpdateAsRef(ref state);
        updateAttributes.maxTorpidityType = __maxTorpidityType.UpdateAsRef(ref state);
        updateAttributes.torpidityType = __torpidityType.UpdateAsRef(ref state);
        updateAttributes.itemRootType = __itemRootType.UpdateAsRef(ref state);
        updateAttributes.instanceType = __instanceType.UpdateAsRef(ref state);
        updateAttributes.temperatureType = __temperatureType.UpdateAsRef(ref state);
        updateAttributes.foodBuffFromTemperatureType = __foodBuffFromTemperatureType.UpdateAsRef(ref state);
        updateAttributes.waterBuffFromTemperatureType = __waterBuffFromTemperatureType.UpdateAsRef(ref state);
        updateAttributes.foodBuffType = __foodBuffType.UpdateAsRef(ref state);
        updateAttributes.waterBuffType = __waterBuffType.UpdateAsRef(ref state);
        updateAttributes.foodType = __foodType.UpdateAsRef(ref state);
        updateAttributes.waterType = __waterType.UpdateAsRef(ref state);
        updateAttributes.healthBuffType = __healthBuffType.UpdateAsRef(ref state);
        updateAttributes.torpidityBuffType = __torpidityBuffType.UpdateAsRef(ref state);

        ref var itemJobManager = ref __itemManager.lookupJobManager;

        var jobHandle = updateAttributes.ScheduleParallelByRef(__group, JobHandle.CombineDependencies(state.Dependency, itemJobManager.readOnlyJobHandle));

        itemJobManager.AddReadOnlyDependency(jobHandle);

        state.Dependency = jobHandle;
    }
}