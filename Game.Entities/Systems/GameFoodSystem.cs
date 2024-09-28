using System;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using ZG;
using Random = Unity.Mathematics.Random;

public struct GameFoodsDefinition
{
    public struct Food
    {
        public uint areaMask;

        public int min;
        public int max;

        public int itemGroupStartIndex;
        public int itemGroupCount;
        
        public float health;

        public float torpidity;

        public float food;

        public float water;

        public float time;

        public float durability;

        public float alpha;

        public float beta;

        public BlobArray<int> formulaIndices;
    }

    public struct Formula
    {
        public float ratio;

        public BlobArray<int> types;
        public BlobArray<int> levelCounts;
    }
    
    public struct Item
    {
        public int type;

        public int min;
        public int max;
    }
    
    public BlobArray<Item> items;
    public BlobArray<RandomGroup> itemGroups;
    public BlobArray<Food> foods;
    public BlobArray<Formula> formulas;
}

public struct GameFoodsData : IComponentData
{
    public BlobAssetReference<GameFoodsDefinition> definition;
}

public struct GameFoodCommand : IComponentData, IEnableableComponent
{
    public bool isActive;
    public int count;
    public GameItemHandle handle;
}

[BurstCompile, 
 CreateAfter(typeof(CallbackSystem)), 
 CreateAfter(typeof(GameItemSystem)), 
 CreateAfter(typeof(GameItemRootEntitySystem)),
 CreateAfter(typeof(GameItemDurabilityInitSystem)),
 CreateAfter(typeof(GameItemTimeInitSystem))]
public partial struct GameFoodSystem : ISystem
{
    [Flags]
    private enum EnabledComponents
    {
        QuestCommandCondition = 0x01, 
        FormulaCommand = 0x02
    }

    private struct RemovedItem
    {
        public GameItemHandle handle;
        public int count;
    }

    private struct ChangedItemDurability
    {
        //public Entity entity;
        public GameItemHandle handle;
        public float value;
    }
    
    private struct ChangedItemTime
    {
        public GameItemHandle handle;
        public float value;
    }
    
    private struct Eat
    {
        public Random random;

        public BlobAssetReference<GameFoodsDefinition> definition;
        
        [ReadOnly]
        public GameItemManager.Hierarchy itemManager;

        [ReadOnly] 
        public SharedHashMap<Entity, Entity>.Reader handleEntities;

        //[ReadOnly] 
        //public SharedHashMap<GameItemHandle, Entity>.Reader handleRootEntities;

        [ReadOnly]
        public NativeHashMap<int, float> durabilities;

        [ReadOnly]
        public NativeHashMap<int, float> times;

        [ReadOnly] 
        public ComponentLookup<GameItemDurability> itemDurabilities;

        [ReadOnly]
        public BufferAccessor<GameFormula> formulas;

        [ReadOnly] 
        public NativeArray<NetworkIdentityType> identityTypes;

        [ReadOnly] 
        public NativeArray<GameNodeStatus> nodeStates;

        [ReadOnly] 
        public NativeArray<GameNodeCharacterSurface> nodeCharacterSurfaces;

        [ReadOnly] 
        public NativeArray<GameCreatureData> creatures;

        [ReadOnly] 
        public NativeArray<GameAnimalData> animals;

        [ReadOnly] 
        public NativeArray<GameFoodCommand> foodCommands;

        public NativeArray<GameMoney> moneies;

        public NativeArray<GameAnimalInfo> animalInfos;

        public NativeArray<GameCreatureWater> waters;

        public NativeArray<GameCreatureFood> foods;

        public BufferAccessor<GameEntityHealthBuff> healthBuffs;

        public BufferAccessor<GameEntityTorpidityBuff> torpidityBuffs;
        
        public BufferAccessor<GameQuestCommandCondition> questCommandConditions;

        public BufferAccessor<GameFormulaCommand> formulaCommands;

        public NativeQueue<RemovedItem>.ParallelWriter removedItems;

        public NativeQueue<ChangedItemDurability>.ParallelWriter changedDurabilities;

        public NativeQueue<ChangedItemTime>.ParallelWriter changedTimes;

        public EnabledComponents Execute(int index)
        {
            ref var definition = ref this.definition.Value;
            var foodCommand = foodCommands[index];
            if(!itemManager.TryGetValue(foodCommand.handle, out var item) || 
               item.type < 0 || 
               item.type >= definition.foods.Length)
                return 0;

            ref var food = ref definition.foods[item.type];
            
            bool isSample = false;//, isChange = false;
            if (food.areaMask != 0 && 
                index < nodeCharacterSurfaces.Length && 
                (food.areaMask & nodeCharacterSurfaces[index].layerMask) != 0)
            {
                if (durabilities.TryGetValue(item.type, out float durability))
                {
                    ChangedItemDurability changedItemDurability;
                    changedItemDurability.handle = foodCommand.handle;
                    changedItemDurability.value = durability;
                    changedDurabilities.Enqueue(changedItemDurability);

                    //isChange = true;
                }

                if (times.TryGetValue(item.type, out float time))
                {
                    ChangedItemTime changedItemTime;
                    changedItemTime.handle = foodCommand.handle;
                    changedItemTime.value = time;
                    changedTimes.Enqueue(changedItemTime);

                    //isChange = true;
                }

                isSample = true;
            }

            float power = foodCommand.count;
            if (isSample)
            {
                //if (!isChange)
                    //actor.Break();
            }
            else
            {
                if (food.durability > math.FLT_MIN_NORMAL && 
                    handleEntities.TryGetValue(GameItemStructChangeFactory.Convert(foodCommand.handle), out Entity entity) && 
                    itemDurabilities.TryGetComponent(entity, out var itemDurability))
                {
                    if (food.durability > itemDurability.value)
                        power = itemDurability.value / food.durability;

                    ChangedItemDurability changedItemDurability;
                    changedItemDurability.handle = foodCommand.handle;
                    changedItemDurability.value = itemDurability.value - food.durability * power;
                    changedDurabilities.Enqueue(changedItemDurability);
                }
                else
                {
                    RemovedItem removedItem;
                    removedItem.handle = foodCommand.handle;
                    removedItem.count = foodCommand.count;
                    removedItems.Enqueue(removedItem);
                }
            }

            __Buff(foodCommand.isActive, index, power, ref food);

            return __Player(
                index, 
                item.type, 
                foodCommand.count, 
                power, 
                ref food, 
                ref definition.formulas);
        }

        private void __Buff(
            bool isActive, 
            int index, 
            float power, 
            ref GameFoodsDefinition.Food foodDefinition)
        {
            if (index < healthBuffs.Length)
            {
                GameEntityHealthBuff healthBuff;
                healthBuff.value = foodDefinition.health * power;
                healthBuff.duration = foodDefinition.time;
                healthBuffs[index].Add(healthBuff);
            }
            
            if (index < torpidityBuffs.Length)
            {
                GameEntityTorpidityBuff torpidityBuff;
                torpidityBuff.value = foodDefinition.torpidity * power;
                torpidityBuff.duration = foodDefinition.time;
                torpidityBuffs[index].Add(torpidityBuff);
            }

            if (index < creatures.Length)
            {
                var creature = creatures[index];

                if (index < foods.Length)
                {
                    var food = foods[index];
                    food.value = math.clamp(food.value + foodDefinition.food, 0.0f, creature.foodMax);
                    foods[index] = food;
                }
                
                if (index < waters.Length)
                {
                    var water = waters[index];
                    water.value = math.clamp(water.value + foodDefinition.water, 0.0f, creature.waterMax);
                    waters[index] = water;
                }
            }

            if (isActive && 
                index < animals.Length && 
                index < animalInfos.Length)
            {
                bool isKnockedOut = index < nodeStates.Length && nodeStates[index].value == (int)GameEntityStatus.KnockedOut;
                
                var animalInfo = animalInfos[index];
                animalInfo.value =
                    math.clamp(animalInfo.value + (isKnockedOut ? foodDefinition.beta : foodDefinition.alpha), 0.0f,
                        animals[index].max);
                animalInfos[index] = animalInfo;
            }
        }

        private EnabledComponents __Player(
            int index, 
            int itemType, 
            int count, 
            float power, 
            ref GameFoodsDefinition.Food food, 
            ref BlobArray<GameFoodsDefinition.Formula> foodFormulas)
        {
            EnabledComponents enabledComponents = 0;
            
            if (index < questCommandConditions.Length)
            {
                GameQuestCommandCondition questCommandCondition;
                questCommandCondition.type = GameQuestConditionType.Use;
                questCommandCondition.index = itemType;
                questCommandCondition.count = count;
                questCommandCondition.label = default;
                questCommandConditions[index].Add(questCommandCondition);

                enabledComponents |= EnabledComponents.QuestCommandCondition;
            }

            if (index < this.moneies.Length)
            {
                int value = (int)math.round(random.NextFloat(food.min, food.max) * power);
                if (value != 0)
                {
                    var money = moneies[index];
                    
                    money.value += value;

                    moneies[index] = money;
                }
            }

            if (index < formulas.Length && index < formulaCommands.Length)
            {
                int numFormulaIndices = food.formulaIndices.Length;
                if (numFormulaIndices > 0)
                {
                    var formulas = this.formulas[index];
                    
                    int foodFormulaIndex, 
                        formulaIndex = random.NextInt(0, numFormulaIndices),
                        numFormulas = formulas.Length,
                        numLevels,
                        i,
                        j,
                        k;
                    int type = identityTypes[index].value;
                    float formulaCount, max, ratio;
                    GameFormula formula;
                    GameFormulaCommand formulaCommand;
                    var formulaCommands = this.formulaCommands[index];
                    for (i = 0; i < numFormulaIndices; ++i)
                    {
                        foodFormulaIndex = food.formulaIndices[formulaIndex];
                        if (foodFormulaIndex >= 0 && foodFormulaIndex < foodFormulas.Length)
                        {
                            ref var foodFormula = ref foodFormulas[foodFormulaIndex];
                            if (foodFormula.types.AsArray().IndexOf(type) != -1)
                            {
                                ratio = foodFormula.ratio * power;
                                max = ratio * food.beta;

                                numLevels = foodFormula.levelCounts.Length;
                                k = -1;
                                formulaCount = 0;
                                for (j = 0; j < numFormulas; ++j)
                                {
                                    formula = formulas[j];
                                    if (formula.index == foodFormulaIndex)
                                    {
                                        k = formula.level;

                                        formulaCount = k < numLevels ? foodFormula.levelCounts[k] - formula.count : 0;

                                        break;
                                    }
                                }

                                while (++k < numLevels)
                                {
                                    formulaCount += foodFormula.levelCounts[k];
                                    if (formulaCount > max)
                                        break;
                                }

                                if (math.abs(formulaCount) > math.FLT_MIN_NORMAL)
                                {
                                    formulaCount = math.max(
                                        random.NextFloat(food.alpha * ratio, math.min(max, formulaCount)), 1.0f);

                                    formulaCommand.index = foodFormulaIndex;
                                    formulaCommand.count = (int)math.ceil(formulaCount);

                                    formulaCommands.Add(formulaCommand);

                                    enabledComponents |= EnabledComponents.FormulaCommand;

                                    break;
                                }
                            }
                        }

                        ++formulaIndex;
                        if (formulaIndex >= numFormulaIndices)
                            formulaIndex = 0;
                    }
                }
            }

            return enabledComponents;
        }
    }

    [BurstCompile]
    private struct EatEx : IJobChunk
    {
        public double time;

        public BlobAssetReference<GameFoodsDefinition> definition;
        
        [ReadOnly]
        public GameItemManager.Hierarchy itemManager;

        [ReadOnly] 
        public SharedHashMap<Entity, Entity>.Reader handleEntities;

        //[ReadOnly] 
        //public SharedHashMap<GameItemHandle, Entity>.Reader handleRootEntities;

        [ReadOnly]
        public NativeHashMap<int, float> durabilities;

        [ReadOnly]
        public NativeHashMap<int, float> times;

        [ReadOnly] 
        public ComponentLookup<GameItemDurability> itemDurabilities;

        [ReadOnly]
        public BufferTypeHandle<GameFormula> formulaType;

        [ReadOnly] 
        public ComponentTypeHandle<NetworkIdentityType> identityTypeType;

        [ReadOnly] 
        public ComponentTypeHandle<GameNodeStatus> nodeStatusType;

        [ReadOnly] 
        public ComponentTypeHandle<GameNodeCharacterSurface> nodeCharacterSurfaceType;

        [ReadOnly] 
        public ComponentTypeHandle<GameCreatureData> creatureType;

        [ReadOnly]
        public ComponentTypeHandle<GameAnimalData> animalType;

        public ComponentTypeHandle<GameFoodCommand> foodCommandType;

        public ComponentTypeHandle<GameMoney> moneyType;

        public ComponentTypeHandle<GameAnimalInfo> animalInfoType;

        public ComponentTypeHandle<GameCreatureWater> waterType;

        public ComponentTypeHandle<GameCreatureFood> foodType;

        public BufferTypeHandle<GameEntityHealthBuff> healthBuffType;

        public BufferTypeHandle<GameEntityTorpidityBuff> torpidityBuffType;
        
        //public BufferAccessor<GameCreatureWaterBuff> waterBuffs;

        //public BufferAccessor<GameCreatureFoodBuff> foodBuffs;

        public BufferTypeHandle<GameQuestCommandCondition> questCommandConditionType;

        public BufferTypeHandle<GameFormulaCommand> formulaCommandType;

        public NativeQueue<RemovedItem>.ParallelWriter removedItems;

        public NativeQueue<ChangedItemDurability>.ParallelWriter changedDurabilities;

        public NativeQueue<ChangedItemTime>.ParallelWriter changedTimes;

        public void Execute(
            in ArchetypeChunk chunk, 
            int unfilteredChunkIndex, 
            bool useEnabledMask, 
            in v128 chunkEnabledMask)
        {
            Eat eat;
            eat.random = new Random(RandomUtility.Hash(time) ^ (uint)unfilteredChunkIndex);
            eat.definition = definition;
            eat.itemManager = itemManager;
            eat.handleEntities = handleEntities;
            eat.durabilities = durabilities;
            eat.times = times;
            eat.itemDurabilities = itemDurabilities;
            eat.formulas = chunk.GetBufferAccessor(ref formulaType);
            eat.identityTypes = chunk.GetNativeArray(ref identityTypeType);
            eat.nodeStates = chunk.GetNativeArray(ref nodeStatusType);
            eat.nodeCharacterSurfaces = chunk.GetNativeArray(ref nodeCharacterSurfaceType);
            eat.creatures = chunk.GetNativeArray(ref creatureType);
            eat.animals = chunk.GetNativeArray(ref animalType);
            eat.foodCommands = chunk.GetNativeArray(ref foodCommandType);
            eat.moneies = chunk.GetNativeArray(ref moneyType);
            eat.foods = chunk.GetNativeArray(ref foodType);
            eat.waters = chunk.GetNativeArray(ref waterType);
            eat.animalInfos = chunk.GetNativeArray(ref animalInfoType);
            eat.healthBuffs = chunk.GetBufferAccessor(ref healthBuffType);
            eat.torpidityBuffs = chunk.GetBufferAccessor(ref torpidityBuffType);
            eat.questCommandConditions = chunk.GetBufferAccessor(ref questCommandConditionType);
            eat.formulaCommands = chunk.GetBufferAccessor(ref formulaCommandType);
            eat.removedItems = removedItems;
            eat.changedDurabilities = changedDurabilities;
            eat.changedTimes = changedTimes;

            EnabledComponents enabledComponents;
            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
            {
                enabledComponents = eat.Execute(i);
                if((enabledComponents & EnabledComponents.QuestCommandCondition) == EnabledComponents.QuestCommandCondition)
                    chunk.SetComponentEnabled(ref questCommandConditionType, i, true);
                
                if((enabledComponents & EnabledComponents.FormulaCommand) == EnabledComponents.FormulaCommand)
                    chunk.SetComponentEnabled(ref formulaCommandType, i, true);
                
                chunk.SetComponentEnabled(ref foodCommandType, i, false);
            }
        }
    }

    [BurstCompile]
    private struct Apply : IJob
    {
        public GameItemManager itemManager;

        public NativeQueue<RemovedItem> removedItems;

        public NativeQueue<ChangedItemDurability> changedDurabilities;

        public NativeQueue<ChangedItemTime> changedTimes;

        public SharedFunctionFactory.Writer functionFactory;

        [ReadOnly]
        public ComponentLookup<EntityObject<GameWeaponComponent>> components;

        [ReadOnly] 
        public SharedHashMap<GameItemHandle, Entity>.Reader handleRootEntities;

        public void Execute()
        {
            while (removedItems.TryDequeue(out var removedItem))
                itemManager.Remove(removedItem.handle, removedItem.count);

            Entity entity;
            GameWeaponFunctionWrapper functionWrapper;
            while (changedDurabilities.TryDequeue(out var changedDurability))
            {
                if(!handleRootEntities.TryGetValue(changedDurability.handle, out entity) || 
                   !components.TryGetComponent(entity, out functionWrapper.value))
                    continue;

                functionWrapper.result.handle = changedDurability.handle;
                functionWrapper.result.value = changedDurability.value;
                
                functionFactory.Invoke(ref functionWrapper);
            }

            changedTimes.Clear();
        }
    }

    private EntityQuery __group;
    
    private ComponentLookup<EntityObject<GameWeaponComponent>> __components;

    private ComponentLookup<GameItemDurability> __itemDurabilities;

    private BufferTypeHandle<GameFormula> __formulaType;
    
    private ComponentTypeHandle<NetworkIdentityType> __identityTypeType;

    private ComponentTypeHandle<GameNodeStatus> __nodeStatusType;

    private ComponentTypeHandle<GameNodeCharacterSurface> __nodeCharacterSurfaceType;

    private ComponentTypeHandle<GameCreatureData> __creatureType;

    private ComponentTypeHandle<GameAnimalData> __animalType;

    private ComponentTypeHandle<GameFoodCommand> __foodCommandType;

    private ComponentTypeHandle<GameMoney> __moneyType;

    private ComponentTypeHandle<GameAnimalInfo> __animalInfoType;

    private ComponentTypeHandle<GameCreatureWater> __waterType;

    private ComponentTypeHandle<GameCreatureFood> __foodType;

    private BufferTypeHandle<GameEntityHealthBuff> __healthBuffType;

    private BufferTypeHandle<GameEntityTorpidityBuff> __torpidityBuffType;
        
    //public BufferAccessor<GameCreatureWaterBuff> waterBuffs;

    //public BufferAccessor<GameCreatureFoodBuff> foodBuffs;

    private BufferTypeHandle<GameQuestCommandCondition> __questCommandConditionType;

    private BufferTypeHandle<GameFormulaCommand> __formulaCommandType;

    private NativeQueue<RemovedItem> __removedItems;

    private NativeQueue<ChangedItemDurability> __changedDurabilities;

    private NativeQueue<ChangedItemTime> __changedTimes;

    private NativeHashMap<int, float> __durabilities;

    private NativeHashMap<int, float> __times;

    //private SharedHashMap<Entity, Entity> __handleEntities;

    private SharedHashMap<GameItemHandle, Entity> __handleRootEntities;

    private GameItemManagerShared __itemManager;

    private SharedFunctionFactory __functionFactory;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __group = builder
                .WithAllRW<GameFoodCommand>()
                .Build(ref state);
        
        __components = state.GetComponentLookup<EntityObject<GameWeaponComponent>>(true);
        __itemDurabilities = state.GetComponentLookup<GameItemDurability>(true);
        __formulaType = state.GetBufferTypeHandle<GameFormula>(true);
        __identityTypeType = state.GetComponentTypeHandle<NetworkIdentityType>(true);
        __nodeStatusType = state.GetComponentTypeHandle<GameNodeStatus>(true);
        __nodeCharacterSurfaceType = state.GetComponentTypeHandle<GameNodeCharacterSurface>(true);
        __creatureType = state.GetComponentTypeHandle<GameCreatureData>(true);
        __animalType = state.GetComponentTypeHandle<GameAnimalData>(true);
        __foodCommandType = state.GetComponentTypeHandle<GameFoodCommand>();
        __moneyType = state.GetComponentTypeHandle<GameMoney>();
        __animalInfoType = state.GetComponentTypeHandle<GameAnimalInfo>();
        __foodType = state.GetComponentTypeHandle<GameCreatureFood>();
        __waterType = state.GetComponentTypeHandle<GameCreatureWater>();
        __healthBuffType = state.GetBufferTypeHandle<GameEntityHealthBuff>();
        __torpidityBuffType = state.GetBufferTypeHandle<GameEntityTorpidityBuff>();
        __questCommandConditionType = state.GetBufferTypeHandle<GameQuestCommandCondition>();
        __formulaCommandType = state.GetBufferTypeHandle<GameFormulaCommand>();
        
        __removedItems = new NativeQueue<RemovedItem>(Allocator.Persistent);
        __changedDurabilities = new NativeQueue<ChangedItemDurability>(Allocator.Persistent);
        __changedTimes = new NativeQueue<ChangedItemTime>(Allocator.Persistent);
        
        var world = state.WorldUnmanaged;

        __durabilities = world.GetExistingSystemUnmanaged<GameItemDurabilityInitSystem>().initializer.values;
        __times = world.GetExistingSystemUnmanaged<GameItemTimeInitSystem>().initializer.values;
        __handleRootEntities = world.GetExistingSystemUnmanaged<GameItemRootEntitySystem>().entities;
        __itemManager = world.GetExistingSystemUnmanaged<GameItemSystem>().manager;
        __functionFactory = world.GetExistingSystemUnmanaged<CallbackSystem>().functionFactory;
    }
    
    //[BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        __removedItems.Dispose();
        __changedDurabilities.Dispose();
        __changedTimes.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.HasSingleton<GameFoodsData>() ||
            !SystemAPI.HasSingleton<GameItemStructChangeManager>())
            return;

        var handleEntities = SystemAPI.GetSingleton<GameItemStructChangeManager>().handleEntities;
        ref var handleEntitiesJobManager = ref handleEntities.lookupJobManager;

        var itemManager = __itemManager.value;
        ref var itemManagerJobManager = ref __itemManager.lookupJobManager;
        var jobHandle = JobHandle.CombineDependencies(
            itemManagerJobManager.readWriteJobHandle,
            handleEntitiesJobManager.readOnlyJobHandle, 
            state.Dependency);

        EatEx eat;
        eat.time = state.WorldUnmanaged.Time.ElapsedTime;
        eat.definition = SystemAPI.GetSingleton<GameFoodsData>().definition;
        eat.itemManager = itemManager.hierarchy;
        eat.handleEntities = handleEntities.reader;
        eat.durabilities = __durabilities;
        eat.times = __times;
        eat.itemDurabilities = __itemDurabilities.UpdateAsRef(ref state);
        eat.formulaType = __formulaType.UpdateAsRef(ref state);
        eat.identityTypeType = __identityTypeType.UpdateAsRef(ref state);
        eat.nodeStatusType = __nodeStatusType.UpdateAsRef(ref state);
        eat.nodeCharacterSurfaceType = __nodeCharacterSurfaceType.UpdateAsRef(ref state);
        eat.creatureType = __creatureType.UpdateAsRef(ref state);
        eat.animalType = __animalType.UpdateAsRef(ref state);
        eat.foodCommandType = __foodCommandType.UpdateAsRef(ref state);
        eat.moneyType = __moneyType.UpdateAsRef(ref state);
        eat.animalInfoType = __animalInfoType.UpdateAsRef(ref state);
        eat.foodType = __foodType.UpdateAsRef(ref state);
        eat.waterType = __waterType.UpdateAsRef(ref state);
        eat.healthBuffType = __healthBuffType.UpdateAsRef(ref state);
        eat.torpidityBuffType = __torpidityBuffType.UpdateAsRef(ref state);
        eat.questCommandConditionType = __questCommandConditionType.UpdateAsRef(ref state);
        eat.formulaCommandType = __formulaCommandType.UpdateAsRef(ref state);
        eat.removedItems = __removedItems.AsParallelWriter();
        eat.changedDurabilities = __changedDurabilities.AsParallelWriter();
        eat.changedTimes = __changedTimes.AsParallelWriter();
        jobHandle = eat.ScheduleParallelByRef(__group, jobHandle);
        
        handleEntitiesJobManager.AddReadOnlyDependency(jobHandle);

        ref var functionFactoryJobManager = ref __functionFactory.lookupJobManager;

        ref var handleRootEntitiesJobManager = ref __handleRootEntities.lookupJobManager;

        jobHandle = JobHandle.CombineDependencies(
            jobHandle, 
            functionFactoryJobManager.readWriteJobHandle, 
            handleRootEntitiesJobManager.readOnlyJobHandle);

        Apply apply;
        apply.itemManager = itemManager;
        apply.removedItems = __removedItems;
        apply.changedDurabilities = __changedDurabilities;
        apply.changedTimes = __changedTimes;
        apply.functionFactory = __functionFactory.writer;
        apply.components = __components.UpdateAsRef(ref state);
        apply.handleRootEntities = __handleRootEntities.reader;
        jobHandle = apply.ScheduleByRef(jobHandle);

        handleRootEntitiesJobManager.AddReadOnlyDependency(jobHandle);

        functionFactoryJobManager.readWriteJobHandle = jobHandle;
        itemManagerJobManager.readWriteJobHandle = jobHandle;
        
        state.Dependency = jobHandle;
    }
}
