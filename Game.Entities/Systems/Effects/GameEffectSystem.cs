using Unity.Jobs;
using Unity.Entities;
using Unity.Collections;
using ZG;
using Unity.Burst;
using Unity.Mathematics;

[assembly: RegisterGenericJobType(typeof(GameEffectApply<GameEffect, GameEffectSystem.Handler, GameEffectSystem.Factory>))]
//[assembly: RegisterGenericJobType(typeof(ClearMultiHashMap<int, Entity>))]

[BurstCompile, UpdateInGroup(typeof(TimeSystemGroup)), UpdateBefore(typeof(GameCreatureSystem)), /*UpdateBefore(typeof(GameItemSystem)), */UpdateAfter(typeof(GameSyncSystemGroup))]
public partial struct GameEffectSystem : ISystem//GameEffectSystem<GameEffect, GameEffectSystem.Handler, GameEffectSystem.Factory>
{
    [BurstCompile]
    public struct Clear : IJob
    {
        [ReadOnly, DeallocateOnJobCompletion]
        public NativeArray<int> entityCount;
        public SharedMultiHashMap<int, Entity>.Writer entities;

        public void Execute()
        {
            entities.Clear();

            entities.capacity = math.max(entities.capacity, this.entityCount[0]);
        }
    }

    public struct Handler : IGameEffectHandler<GameEffect>
    {
        [ReadOnly]
        public ComponentLookup<GameEffectorData> effectors;

        [ReadOnly]
        public NativeArray<Entity> entityArray;

        public BufferAccessor<GameEntityHealthBuff> healthBuffs;
        public NativeArray<GameCreatureFoodBuff> foodBuffs;
        public NativeArray<GameCreatureWaterBuff> waterBuffs;
        public NativeArray<GameCreatureTemperature> temperatures;
        public NativeArray<GameCreatureFoodBuffFromTemperature> foodBuffFromtemperatures;
        public NativeArray<GameCreatureWaterBuffFromTemperature> waterBuffFromTemperatures;
        public NativeArray<GameItemTimeScale> itemTimeScales;
        public NativeArray<GameTimeActionFactor> timeActionFactors;

        public SharedMultiHashMap<int, Entity>.ParallelWriter entities;

        public bool Change(
            int index,
            int areaIndex,
            ref GameEffect destination,
            in GameEffect source,
            in DynamicBuffer<PhysicsTriggerEvent> physicsTriggerEvents,
            in ComponentLookup<PhysicsShapeParent> physicsShapeParents)
        {
            if (areaIndex != -1)
                entities.Add(areaIndex, entityArray[index]);

            Entity effector;
            foreach(var physicsTriggerEvent in physicsTriggerEvents)
            {
                effector = physicsShapeParents.HasComponent(physicsTriggerEvent.entity) ? physicsShapeParents[physicsTriggerEvent.entity].entity : physicsTriggerEvent.entity;
                if (!effectors.HasComponent(effector))
                    continue;

                destination += effectors[effector].value;
            }

            GameEffect value = destination - source;
            bool result = value.force != 0 || value.power != 0;

            if (value.health != 0.0f)
            {
                if (healthBuffs.Length > index)
                {
                    DynamicBuffer<GameEntityHealthBuff> healthBuffs = this.healthBuffs[index];
                    GameEntityHealthBuff healthBuff = healthBuffs[0];
                    healthBuff.value += value.health;
                    healthBuffs[0] = healthBuff;
                }

                result = true;
            }

            if (value.food != 0.0f)
            {
                if (foodBuffs.Length > index)
                {
                    GameCreatureFoodBuff foodBuff = foodBuffs[index];
                    foodBuff.value += value.food;
                    foodBuffs[index] = foodBuff;
                }

                result = true;
            }

            if (value.water != 0.0f)
            {
                if (waterBuffs.Length > index)
                {
                    GameCreatureWaterBuff waterBuff = waterBuffs[index];
                    waterBuff.value += value.water;
                    waterBuffs[index] = waterBuff;
                }

                result = true;
            }

            if (value.temperature != 0)
            {
                if (temperatures.Length > index)
                {
                    GameCreatureTemperature temperature = temperatures[index];
                    temperature.value += value.temperature;
                    temperatures[index] = temperature;
                }

                result = true;
            }

            if (value.foodBuffFromTemperatureScale != 0)
            {
                if (foodBuffFromtemperatures.Length > index)
                {
                    var foodBuffFromtemperature = foodBuffFromtemperatures[index];
                    foodBuffFromtemperature.scale += value.foodBuffFromTemperatureScale;
                    foodBuffFromtemperatures[index] = foodBuffFromtemperature;
                }

                result = true;
            }

            if (value.waterBuffFromTemperatureScale != 0)
            {
                if (waterBuffFromTemperatures.Length > index)
                {
                    var waterBuffFromTemperature = waterBuffFromTemperatures[index];
                    waterBuffFromTemperature.scale += value.waterBuffFromTemperatureScale;
                    waterBuffFromTemperatures[index] = waterBuffFromTemperature;
                }

                result = true;
            }

            if (value.itemTimeScale != 0.0f)
            {
                if (itemTimeScales.Length > index)
                {
                    GameItemTimeScale itemTimeScale = itemTimeScales[index];
                    itemTimeScale.value += value.itemTimeScale;
                    itemTimeScales[index] = itemTimeScale;
                }

                result = true;
            }

            if (value.layTimeScale != 0.0f)
            {
                if (timeActionFactors.Length > index)
                {
                    GameTimeActionFactor timeActionFactor = timeActionFactors[index];
                    timeActionFactor.value += value.layTimeScale;
                    timeActionFactors[index] = timeActionFactor;
                }

                result = true;
            }

            return result;
        }
    }

    public struct Factory : IGameEffectFactory<GameEffect, Handler>
    {
        [ReadOnly]
        public ComponentLookup<GameEffectorData> effectors;

        [ReadOnly]
        public EntityTypeHandle entityType;

        public BufferTypeHandle<GameEntityHealthBuff> healthBuffType;
        public ComponentTypeHandle<GameCreatureFoodBuff> foodBuffType;
        public ComponentTypeHandle<GameCreatureWaterBuff> waterBuffType;
        public ComponentTypeHandle<GameCreatureTemperature> temperatureType;
        public ComponentTypeHandle<GameCreatureFoodBuffFromTemperature> foodBuffFromtemperatureType;
        public ComponentTypeHandle<GameCreatureWaterBuffFromTemperature> waterBuffFromtemperatureType;
        public ComponentTypeHandle<GameItemTimeScale> itemTimeScaleType;
        public ComponentTypeHandle<GameTimeActionFactor> timeActionFactorType;

        public SharedMultiHashMap<int, Entity>.ParallelWriter entities;

        public Handler Create(in ArchetypeChunk chunk)
        {
            Handler handler;
            handler.effectors = effectors;
            handler.entityArray = chunk.GetNativeArray(entityType);
            handler.healthBuffs = chunk.GetBufferAccessor(ref healthBuffType);
            handler.foodBuffs = chunk.GetNativeArray(ref foodBuffType);
            handler.waterBuffs = chunk.GetNativeArray(ref waterBuffType);
            handler.temperatures = chunk.GetNativeArray(ref temperatureType);
            handler.foodBuffFromtemperatures = chunk.GetNativeArray(ref foodBuffFromtemperatureType);
            handler.waterBuffFromTemperatures = chunk.GetNativeArray(ref waterBuffFromtemperatureType);
            handler.itemTimeScales = chunk.GetNativeArray(ref itemTimeScaleType);
            handler.timeActionFactors = chunk.GetNativeArray(ref timeActionFactorType);
            handler.entities = entities;

            return handler;
        }
    }

    private ComponentLookup<GameEffectorData> __effectors;

    private EntityTypeHandle __entityType;

    private BufferTypeHandle<GameEntityHealthBuff> __healthBuffType;
    private ComponentTypeHandle<GameCreatureFoodBuff> __foodBuffType;
    private ComponentTypeHandle<GameCreatureWaterBuff> __waterBuffType;
    private ComponentTypeHandle<GameCreatureTemperature> __temperatureType;
    private ComponentTypeHandle<GameCreatureFoodBuffFromTemperature> __foodBuffFromtemperatureType;
    private ComponentTypeHandle<GameCreatureWaterBuffFromTemperature> __waterBuffFromtemperatureType;
    private ComponentTypeHandle<GameItemTimeScale> __itemTimeScaleType;
    private ComponentTypeHandle<GameTimeActionFactor> __timeActionFactorType;

    private GameEffectSystemCore<GameEffect> __core;

    public SharedList<GameEffect> values
    {
        get;

        private set;
    }

    public SharedMultiHashMap<int, Entity> entities
    {
        get;

        private set;
    }

    /*public NativeMultiHashMapEnumerable<int, Entity, NativeMultiHashMapEnumeratorObject> Get(int areaIndex)
    {
        __jobHandle.Complete();
        __jobHandle = default;

        if (__core.isEmpty)
            __entities.Clear();

        return __entities.GetEnumerable(areaIndex);
    }

    public void Set(int areaIndex, GameEffect value)
    {
        __jobHandle.Complete();
        __jobHandle = default;

        _values[areaIndex] += value;
    }*/

    /*public bool Create(GameMapDatabase.Surface[] surfaces, GameMapDatabase.Effect[] effects)
    {
        if (_surfaces.IsCreated)
            return false;

        int numEffects = effects == null ? 0 : effects.Length;
        if (numEffects < 1)
            return false;

        int numSurfaces = surfaces == null ? 0 : surfaces.Length;
        if (numSurfaces < 1)
            return false;

        int i;
        _surfaces = new NativeArray<GameEffectInternalSurface>(numSurfaces, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        for (i = 0; i < numSurfaces; ++i)
            _surfaces[i] = surfaces[i];

        _effects = new NativeArray<GameEffectInternalValue>(numEffects, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        _values = new NativeArray<GameEffect>(numEffects, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);

        int j, k;
        GameEffectInternalValue effectTemp;
        GameEffectInternalHeight heightTemp;
        GameMapDatabase.Effect effect;
        GameMapDatabase.Height height;
        var conditions = new NativeList<GameEffectInternalCondition>(Allocator.Temp);
        var heights = new NativeList<GameEffectInternalHeight>(Allocator.Temp);
        for (i = 0; i < numEffects; ++i)
        {
            effect = effects[i];

            effectTemp.conditionIndex = conditions.Length;
            effectTemp.conditionCount = effect.conditions == null ? 0 : effect.conditions.Length;

            for (j = 0; j < effectTemp.conditionCount; ++j)
                conditions.Add(effect.conditions[j]);

            effectTemp.fromHeightIndex = heights.Length;
            effectTemp.fromHeightCount = effect.fromHeights == null ? 0 : effect.fromHeights.Length;
            for (j = 0; j < effectTemp.fromHeightCount; ++j)
            {
                height = effect.fromHeights[j];

                heightTemp.conditionIndex = conditions.Length;
                heightTemp.conditionCount = height.conditions == null ? 0 : height.conditions.Length;

                for (k = 0; k < heightTemp.conditionCount; ++k)
                    conditions.Add(height.conditions[k]);

                heightTemp.scale = height.scale;

                heights.Add(heightTemp);
            }

            effectTemp.toHeightIndex = heights.Length;
            effectTemp.toHeightCount = effect.toHeights == null ? 0 : effect.toHeights.Length;
            for (j = 0; j < effectTemp.toHeightCount; ++j)
            {
                height = effect.toHeights[j];

                heightTemp.conditionIndex = conditions.Length;
                heightTemp.conditionCount = height.conditions == null ? 0 : height.conditions.Length;

                for (k = 0; k < heightTemp.conditionCount; ++k)
                    conditions.Add(height.conditions[k]);

                heightTemp.scale = height.scale;

                heights.Add(heightTemp);
            }

            effectTemp.fromHeight = effect.fromHeight;
            effectTemp.toHeight = effect.toHeight;

            _effects[i] = effectTemp;
            _values[i] = effect.value;
        }

        _conditions = new NativeArray<GameEffectInternalCondition>(conditions.AsArray(), Allocator.Persistent);

        conditions.Dispose();

        _heights = new NativeArray<GameEffectInternalHeight>(heights.AsArray(), Allocator.Persistent);

        heights.Dispose();

        return true;
    }*/

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __core = new GameEffectSystemCore<GameEffect>(ref state);

        values = new SharedList<GameEffect>(Allocator.Persistent);

        entities = new SharedMultiHashMap<int, Entity>(Allocator.Persistent);

        __effectors = state.GetComponentLookup<GameEffectorData>(true);
        __entityType = state.GetEntityTypeHandle();
        __healthBuffType = state.GetBufferTypeHandle<GameEntityHealthBuff>();
        __foodBuffType = state.GetComponentTypeHandle<GameCreatureFoodBuff>();
        __waterBuffType = state.GetComponentTypeHandle<GameCreatureWaterBuff>();
        __temperatureType = state.GetComponentTypeHandle<GameCreatureTemperature>();
        __foodBuffFromtemperatureType = state.GetComponentTypeHandle<GameCreatureFoodBuffFromTemperature>();
        __waterBuffFromtemperatureType = state.GetComponentTypeHandle<GameCreatureWaterBuffFromTemperature>();
        __itemTimeScaleType = state.GetComponentTypeHandle<GameItemTimeScale>();
        __timeActionFactorType = state.GetComponentTypeHandle<GameTimeActionFactor>();
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        values.Dispose();

        entities.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var entityCount = new NativeArray<int>(1, Allocator.TempJob, NativeArrayOptions.ClearMemory);
        var jobHandle = __core.Group.CalculateEntityCountAsync(entityCount, state.Dependency);

        var entities = this.entities;
        ref var entitiesJobManager = ref entities.lookupJobManager;

        Clear clear;
        clear.entityCount = entityCount;
        clear.entities = entities.writer;
        jobHandle = clear.ScheduleByRef(JobHandle.CombineDependencies(entitiesJobManager.readWriteJobHandle, jobHandle));

        Factory factory;
        factory.effectors = __effectors.UpdateAsRef(ref state);
        factory.entityType = __entityType.UpdateAsRef(ref state);
        factory.healthBuffType = __healthBuffType.UpdateAsRef(ref state);
        factory.foodBuffType = __foodBuffType.UpdateAsRef(ref state);
        factory.waterBuffType = __waterBuffType.UpdateAsRef(ref state);
        factory.temperatureType = __temperatureType.UpdateAsRef(ref state);
        factory.foodBuffFromtemperatureType = __foodBuffFromtemperatureType.UpdateAsRef(ref state);
        factory.waterBuffFromtemperatureType = __waterBuffFromtemperatureType.UpdateAsRef(ref state);
        factory.itemTimeScaleType = __itemTimeScaleType.UpdateAsRef(ref state);
        factory.timeActionFactorType = __timeActionFactorType.UpdateAsRef(ref state);
        factory.entities = entities.parallelWriter;

        var values = this.values;
        ref var valuesJobManager = ref values.lookupJobManager;
        state.Dependency = JobHandle.CombineDependencies(jobHandle, valuesJobManager.readOnlyJobHandle);

        __core.Update<Handler, Factory>(values.AsArray().AsReadOnly(), ref factory, ref state);

        jobHandle = state.Dependency;

        valuesJobManager.AddReadOnlyDependency(jobHandle);

        entitiesJobManager.readWriteJobHandle = jobHandle;
    }
}