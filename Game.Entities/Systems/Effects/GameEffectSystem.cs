using Unity.Jobs;
using Unity.Entities;
using Unity.Collections;
using ZG;

[assembly: RegisterGenericJobType(typeof(GameEffectApply<GameEffect, GameEffectSystem.Handler, GameEffectSystem.Factory>))]

[UpdateInGroup(typeof(TimeSystemGroup)), UpdateBefore(typeof(GameCreatureSystem)), /*UpdateBefore(typeof(GameItemSystem)), */UpdateAfter(typeof(GameSyncSystemGroup))]
public partial class GameEffectSystem : GameEffectSystem<GameEffect, GameEffectSystem.Handler, GameEffectSystem.Factory>
{
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

        public NativeParallelMultiHashMap<int, Entity>.ParallelWriter entities;

        public bool Change(
            int index,
            int areaIndex,
            in GameEffect source,
            ref GameEffect destination,
            in DynamicBuffer<PhysicsShapeTriggerEventRevicer> revicers)
        {
            if (areaIndex != -1)
                entities.Add(areaIndex, entityArray[index]);

            Entity revicer;
            int length = revicers.Length;
            for (int i = 0; i < length; ++i)
            {
                revicer = revicers[i].entity;
                if (!effectors.HasComponent(revicer))
                    continue;

                destination += effectors[revicer].value;
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

        public NativeParallelMultiHashMap<int, Entity>.ParallelWriter entities;

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

    private JobHandle __jobHandle;
    private NativeParallelMultiHashMap<int, Entity> __entities;

    public NativeMultiHashMapEnumerable<int, Entity, NativeMultiHashMapEnumeratorObject> Get(int areaIndex)
    {
        __jobHandle.Complete();
        __jobHandle = default;

        if (isEmpty)
            __entities.Clear();

        return __entities.GetEnumerable(areaIndex);
    }

    public void Set(int areaIndex, GameEffect value)
    {
        __jobHandle.Complete();
        __jobHandle = default;

        _values[areaIndex] += value;
    }

    public bool Create(GameMapDatabase.Surface[] surfaces, GameMapDatabase.Effect[] effects)
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
    }

    protected override void OnCreate()
    {
        base.OnCreate();

        __entities = new NativeParallelMultiHashMap<int, Entity>(1, Allocator.Persistent);
    }

    protected override void OnDestroy()
    {
        if (_surfaces.IsCreated)
            _surfaces.Dispose();

        if (_conditions.IsCreated)
            _conditions.Dispose();

        if (_heights.IsCreated)
            _heights.Dispose();

        if (_effects.IsCreated)
            _effects.Dispose();

        if (_values.IsCreated)
            _values.Dispose();

        __entities.Dispose();

        base.OnDestroy();
    }

    protected override void OnUpdate()
    {
        base.OnUpdate();

        __jobHandle = Dependency;
    }

    protected override Factory _Get(ref JobHandle inputDeps)
    {
        inputDeps = __entities.Clear(entityCount, inputDeps);

        Factory factory;
        factory.effectors = GetComponentLookup<GameEffectorData>(true);
        factory.entityType = GetEntityTypeHandle();
        factory.healthBuffType = GetBufferTypeHandle<GameEntityHealthBuff>();
        factory.foodBuffType = GetComponentTypeHandle<GameCreatureFoodBuff>();
        factory.waterBuffType = GetComponentTypeHandle<GameCreatureWaterBuff>();
        factory.temperatureType = GetComponentTypeHandle<GameCreatureTemperature>();
        factory.foodBuffFromtemperatureType = GetComponentTypeHandle<GameCreatureFoodBuffFromTemperature>();
        factory.waterBuffFromtemperatureType = GetComponentTypeHandle<GameCreatureWaterBuffFromTemperature>();
        factory.itemTimeScaleType = GetComponentTypeHandle<GameItemTimeScale>();
        factory.timeActionFactorType = GetComponentTypeHandle<GameTimeActionFactor>();
        factory.entities = __entities.AsParallelWriter();

        return factory;
    }
}