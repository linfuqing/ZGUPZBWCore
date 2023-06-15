using System.Collections.Generic;
using Unity.Jobs;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Physics;
using ZG;

[assembly: RegisterGenericJobType(typeof(GameEntityActionSystemCore.PerformEx<GameEntityActionDataSystem.Handler, GameEntityActionDataSystem.Factory>))]

[BurstCompile, UpdateInGroup(typeof(GameEntityActionSystemGroup))]
public partial struct GameEntityActionDataSystem : ISystem, IEntityCommandProducerJob //GameEntityActionSystem<GameEntityActionDataSystem.Handler, GameEntityActionDataSystem.Factory>, IEntityCommandProducerJob
{
    public struct Action
    {
        public GameBuff buff;

        public GameActionAttack[] attacks;

        public GameActionSpawn[] spawns;
    }

    public struct Item
    {
        public int type;

        public GameBuff buff;

        public GameActionAttack[] attacks;
        public GameEntityDefence[] defences;
    }

    public struct ActionData
    {
        public int spawnStartIndex;
        public int spawnCount;

        public GameBuff buff;
    }
    
    public struct ItemData
    {
        public GameBuff buff;
    }

    public struct LevelData
    {
        public float stageExpFactor;

        public Property[] attacks;
        public Property[] defences;
    }

    public struct Level
    {
        public float stageExpFactor;

        public int attackStartIndex;
        public int attackCount;

        public int defenceStartIndex;
        public int defenceCount;
    }

    public struct Property
    {
        public float variantMin;
        public float variantMax;
        public float power;
        public float max;
        public float chance;
    }

    public struct Handler : IGameEntityActionHandler
    {
        public int propertyCount;

        public int hitCount;

        [ReadOnly]
        public GameItemManager.Hierarchy hierarchy;

        [ReadOnly]
        public ComponentLookup<GameNodeStatus> nodeStates;

        [ReadOnly]
        public ComponentLookup<GameItemRoot> itemRoots;

        [ReadOnly]
        public ComponentLookup<GameVariant> entityVariants;

        [ReadOnly]
        public ComponentLookup<GameExp> entityExps;

        [ReadOnly]
        public ComponentLookup<GamePower> entityPowers;

        [ReadOnly]
        public ComponentLookup<GameLevel> entityLevels;

        [ReadOnly]
        public NativeHashMap<int, int> itemTypeIndices;

        [ReadOnly]
        public NativeArray<Level> levels;

        [ReadOnly]
        public NativeArray<Property> properties;

        [ReadOnly]
        public NativeArray<GameActionSpawn> spawns;

        [ReadOnly]
        public NativeArray<ItemData> items;

        [ReadOnly]
        public NativeArray<ActionData> actions;

        [ReadOnly]
        public NativeArray<GameActionAttack> actionAttacks;

        [ReadOnly]
        public NativeArray<GameActionAttack> itemAttacks;

        [ReadOnly]
        public NativeArray<GameEntityDefence> itemDefences;

        //[ReadOnly]
        //public BufferLookup<GameEntityItem> entityItems;

        [ReadOnly]
        public BufferLookup<GameEntityDefence> defences;
        
        public BufferAccessor<GameActionAttack> attacks;
        
        public NativeArray<GameActionBuff> buffs;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<GameEntityHealthDamage> healthDamages;

        public NativeFactory<BufferElementData<GameEntityHealthBuff>>.ParallelWriter healthBuffs;

        public NativeFactory<BufferElementData<GameEntityTorpidityBuff>>.ParallelWriter torpidityBuffs;

        public EntityCommandQueue<GameSpawnData>.ParallelWriter entityMananger;

        public void CalculateProperties(bool isAttack, in Entity entity, NativeArray<float> properties)
        {
            if (!entityLevels.HasComponent(entity))
                return;

            int levelHandle = entityLevels[entity].handle;
            if (levelHandle <= 0)
                return;

            int propertyCount = 0;
            Property property;
            var level = levels[levelHandle - 1];
            var random = new Random((uint)entityVariants[entity].value);
            float power = entityPowers[entity].value,
                stage = GameSoulManager.GetStage(entityExps[entity].value, level.stageExpFactor),
                propertyValue;
            var propertyIndices = new NativeList<int>(propertyCount, Allocator.Temp);
            for (int i = 0; i < level.attackCount; ++i)
            {
                property = this.properties[level.attackStartIndex + i];
                if (property.chance > math.FLT_MIN_NORMAL)
                    ++propertyCount;

                if (property.chance <= random.NextFloat())
                    continue;

                if (isAttack)
                {
                    propertyValue = random.NextFloat(property.variantMin, property.variantMax);
                    propertyValue += GameSoulManager.GetStagePower(power + property.power, stage);
                    propertyValue = math.min(propertyValue, property.max);

                    properties[i] += propertyValue;
                }
                else
                    random.NextFloat();

                propertyIndices.Add(i);
            }

            for (int i = 0; i < level.defenceCount; ++i)
            {
                property = this.properties[level.defenceStartIndex + i];
                if (property.chance > math.FLT_MIN_NORMAL)
                    ++propertyCount;

                if (property.chance <= random.NextFloat())
                    continue;

                if (isAttack)
                    random.NextFloat();
                else
                {
                    propertyValue = random.NextFloat(property.variantMin, property.variantMax);
                    propertyValue += GameSoulManager.GetStagePower(power + property.power, stage);
                    propertyValue = math.min(propertyValue, property.max);

                    properties[i] += propertyValue;
                }

                propertyIndices.Add(level.attackCount + i);
            }

            if (propertyCount >= 3)
            {
                propertyCount = level.attackCount + level.defenceCount;
                int propertyIndex, offset;
                while (propertyIndices.Length < 3)
                {
                    propertyIndex = random.NextInt(0, propertyCount);
                    if (propertyIndex > level.attackCount)
                    {
                        for (int i = 0; i < level.defenceCount; ++i)
                        {
                            propertyIndex += i;
                            if (propertyIndex >= propertyCount)
                                propertyIndex -= level.defenceCount;

                            offset = propertyIndex - level.attackCount;
                            property = this.properties[level.defenceStartIndex + offset];
                            if (property.chance > math.FLT_MIN_NORMAL && !propertyIndices.Contains(propertyIndex))
                            {
                                propertyIndices.Add(propertyIndex);

                                if (isAttack)
                                    random.NextFloat();
                                else
                                {
                                    propertyValue = random.NextFloat(property.variantMin, property.variantMax) + GameSoulManager.GetStagePower(property.power + power, stage);
                                    propertyValue = math.min(propertyValue, property.max);

                                    properties[offset] += propertyValue;
                                }

                                break;
                            }
                        }
                    }
                    else
                    {
                        for (int i = 0; i < level.attackCount; ++i)
                        {
                            propertyIndex += i;
                            if (propertyIndex >= level.attackCount)
                                propertyIndex -= level.attackCount;

                            property = this.properties[level.attackStartIndex + propertyIndex];
                            if (property.chance > math.FLT_MIN_NORMAL && !propertyIndices.Contains(propertyIndex))
                            {
                                propertyIndices.Add(propertyIndex);

                                if (isAttack)
                                {
                                    propertyValue = random.NextFloat(property.variantMin, property.variantMax) + GameSoulManager.GetStagePower(property.power + power, stage);
                                    propertyValue = math.min(propertyValue, property.max);

                                    properties[propertyIndex] += propertyValue;
                                }
                                else
                                    random.NextFloat();

                                break;
                            }
                        }
                    }
                }
            }

            propertyIndices.Dispose();
        }

        public bool CalculateProperties(
            in GameItemHandle handle,
            in NativeArray<float> inputs,
            NativeArray<float> outputs)
        {
            return __CalculateProperties(
                false,
                handle,
                inputs,
                outputs, 
                out _);
        }

        public bool CalculateProperties(
            in GameItemHandle handle,
            ref GameBuff buff)
        {
            return __CalculateProperties(false, handle, ref buff, out _);
        }

        public void Spawn(
            int startIndex, 
            int count, 
            GameActionSpawnType type, 
            double time, 
            Entity entity, 
            RigidTransform transform)
        {
            if (count > 0)
            {
                bool isChosed = false;
                float chance = 0.0f, randomValue = 0.0f;
                Random random = default;
                GameActionSpawn source;
                GameSpawnData destination;
                for (int i = 0; i < count; ++i)
                {
                    source = spawns[startIndex + i];
                    if (source.type == type)
                    {
                        if (source.chance > math.FLT_MIN_NORMAL)
                        {
                            chance += source.chance;
                            if (chance > 1.0f)
                            {
                                isChosed = false;

                                chance -= 1.0f;

                                randomValue = random.NextFloat();
                            }
                            else if (random.state == 0)
                            {
                                random.InitState(math.hash(transform));

                                randomValue = random.NextFloat();
                            }

                            if (isChosed || chance < randomValue)
                                continue;

                            isChosed = true;
                        }

                        destination.assetIndex = source.assetIndex;
                        //destination.time = time;
                        destination.entity = entity;
                        destination.transform = transform;

                        entityMananger.Enqueue(destination);
                    }
                }
            }
        }

        public bool Create(
            int index,
            double time,
            in float3 targetPosition, 
            in Entity entity,
            in GameActionData data)
        {
            return false;
        }

        public bool Init(
            int index,
            float elapsedTime,
            double time,
            in Entity entity,
            in RigidTransform transform,
            in GameActionData data)
        {
            ActionData action = actions[data.actionIndex];
            GameActionBuff buff;
            buff.value = action.buff;

            var attacks = this.attacks[index].Reinterpret<float>();
            attacks.ResizeUninitialized(propertyCount);

            int temp = data.actionIndex * propertyCount;
            for (int i = 0; i < propertyCount; ++i)
                attacks[i] = actionAttacks[temp++].value;

            CalculateProperties(true, data.entity, attacks.Reinterpret<float>().AsNativeArray());

            if (itemRoots.HasComponent(data.entity))
            {
                var handle = itemRoots[data.entity].handle;
                CalculateProperties(handle, itemAttacks.Reinterpret<float>(), attacks.AsNativeArray());

                CalculateProperties(handle, ref buff.value);
            }

            /*if (this.entityItems.HasComponent(data.entity))
            {
                DynamicBuffer<GameEntityItem> entityItems = this.entityItems[data.entity];
                GameEntityItem entityItem;
                int numItems = entityItems.Length, length = items.Length;
                for (int i = 0; i < numItems; ++i)
                {
                    entityItem = entityItems[i];

                    if (entityItem.index >= 0 && entityItem.index < length)
                    {
                        buff.value += items[entityItem.index].buff;

                        temp = entityItem.index * count;
                        for (int j = 0; j < count; ++j)
                            attacks[j] += itemAttacks[temp++];
                    }
                }
            }*/

            buffs[index] = buff;

            Spawn(
                action.spawnStartIndex, 
                action.spawnCount, 
                GameActionSpawnType.Init, 
                data.time + elapsedTime, 
                data.entity, 
                transform);

            return false;
        }

        public void Hit(
            int index,
            float elapsedTime,
            double time,
            in Entity entity,
            in Entity target,
            in RigidTransform transform,
            in GameActionData data)
        {
            ActionData action = actions[data.actionIndex];
            Spawn(action.spawnStartIndex,
                action.spawnCount,
                GameActionSpawnType.Hit,
                data.time + elapsedTime,
                data.entity,
                transform);
        }

        public void Damage(
            int index,
            int count,
            float elapsedTime, 
            double time,
            in Entity entity,
            in Entity target,
            in float3 position,
            in float3 normal,
            in GameActionData data)
        {
            var attacks = this.attacks[index];
            int numAttacks = math.min(attacks.Length, propertyCount);
            if (numAttacks < 1)
                return;

            var defences = new NativeArray<float>(propertyCount, Allocator.Temp);
            if (this.defences.HasBuffer(target))
            {
                var temp = this.defences[target].Reinterpret<float>().AsNativeArray();
                NativeArray<float>.Copy(temp, defences, math.min(temp.Length, defences.Length));
            }

            if(itemRoots.HasComponent(target))
                CalculateProperties(itemRoots[target].handle, itemDefences.Reinterpret<float>(), defences);

            /*if(this.entityItems.HasComponent(target))
            {
                var entityItems = this.entityItems[target];
                GameEntityItem entityItem;
                int numEntityItems = entityItems.Length, offset, length = items.Length, i, j;
                for (i = 0; i < numEntityItems; ++i)
                {
                    entityItem = entityItems[i];
                    if (entityItem.index >= 0 && entityItem.index < length)
                    {
                        offset = entityItem.index * this.count;
                        for (j = 0; j < this.count; ++j)
                            defences[j] += itemDefences[offset + j];
                    }
                }
            }*/

            CalculateProperties(false, target, defences);

            float attack, defence, value, hit = 0.0f, torpor = 0.0f;
            for (int i = 0; i < numAttacks; ++i)
            {
                attack = attacks[i];
                defence = defences[i];

                if (defence < 0)
                    value = attack > 0 ? attack - defence : 0;
                else
                    value = math.max(attack, defence) - defence;

                if (i < hitCount)
                    hit += value;
                else
                    torpor += value;
            }

            defences.Dispose();

            hit *= count;
            torpor *= count;

            double now = data.time + elapsedTime;

            if (healthDamages.HasComponent(target))
            {
                GameEntityHealthDamage healthDamage = healthDamages[target];
                healthDamage.value += hit;
                healthDamage.time = now;
                healthDamage.entity = data.entity;

                healthDamages[target] = healthDamage;
            }

            GameActionBuff buff = buffs[index];
            if (buff.value.healthTime > math.FLT_MIN_NORMAL)
            {
                BufferElementData<GameEntityHealthBuff> healthBuff;

                healthBuff.entity = target;
                healthBuff.value.value = buff.value.healthPerTime;
                healthBuff.value.duration = buff.value.healthTime;
                healthBuffs.Create().value = healthBuff;
            }

            if (torpor != 0 && 
                buff.value.torpidityTime > math.FLT_MIN_NORMAL && 
                nodeStates.HasComponent(target) && 
                (nodeStates[target].value & (int)GameEntityStatus.KnockedOut) != (int)GameEntityStatus.KnockedOut)
            {
                BufferElementData<GameEntityTorpidityBuff> torpidityBuff;

                torpidityBuff.entity = target;
                torpidityBuff.value.value = -torpor;
                torpidityBuff.value.duration = buff.value.torpidityTime;
                torpidityBuffs.Create().value = torpidityBuff;
            }
            
            ActionData action = actions[data.actionIndex];
            Spawn(action.spawnStartIndex,
                action.spawnCount,
                GameActionSpawnType.Damage,
                data.time + elapsedTime,
                data.entity,
                math.RigidTransform(Math.FromToRotation(math.up(), normal), position));
        }

        public void Destroy(
            int index,
            float elapsedTime,
            double time,
            in Entity entity,
            in RigidTransform transform,
            in GameActionData data)
        {
        }

        public bool __CalculateProperties(
            bool isSibling, 
            in GameItemHandle handle,
            in NativeArray<float> inputs,
            NativeArray<float> outputs, 
            out GameItemInfo item)
        {
            if (hierarchy.GetChildren(handle, out var enumerator, out item))
            {
                __CalculateProperties(
                    true,
                    item.siblingHandle,
                    inputs,
                    outputs, 
                    out _);

                if (isSibling)
                {
                    GameItemInfo childItem;
                    while (enumerator.MoveNext())
                    {
                        if (__CalculateProperties(
                            false,
                            enumerator.Current.handle,
                            inputs,
                            outputs,
                            out childItem) &&
                            itemTypeIndices.TryGetValue(childItem.type, out int itemIndex))
                        {
                            int temp = itemIndex * propertyCount;
                            for (int j = 0; j < propertyCount; ++j)
                                outputs[j] += inputs[temp++];
                        }
                    }
                }
                else
                {
                    while (enumerator.MoveNext())
                        __CalculateProperties(
                            false,
                            enumerator.Current.handle,
                            inputs,
                            outputs,
                            out _);
                }

                return true;
            }

            return false;
        }

        public bool __CalculateProperties(
            bool isSibling,
            in GameItemHandle handle,
            ref GameBuff buff, 
            out GameItemInfo item)
        {
            if (hierarchy.GetChildren(handle, out var enumerator, out item))
            {
                __CalculateProperties(
                    true,
                    item.siblingHandle,
                    ref buff, 
                    out _);

                if (isSibling)
                {
                    GameItemInfo childItem;
                    while (enumerator.MoveNext())
                    {
                        if (__CalculateProperties(
                            false,
                            enumerator.Current.handle,
                            ref buff, 
                            out childItem) &&
                            itemTypeIndices.TryGetValue(childItem.type, out int itemIndex))
                            buff += items[itemIndex].buff;
                    }
                }
                else
                {
                    while (enumerator.MoveNext())
                        __CalculateProperties(
                            false,
                            enumerator.Current.handle,
                            ref buff, 
                            out _);
                }

                return true;
            }

            return false;
        }
    }
    
    public struct Factory : IGameEntityActionFactory<Handler>
    {
        public int propertyCount;

        public int hitCount;

        [ReadOnly]
        public GameItemManager.Hierarchy hierarchy;

        [ReadOnly]
        public ComponentLookup<GameNodeStatus> nodeStates;

        [ReadOnly]
        public ComponentLookup<GameItemRoot> itemRoots;

        [ReadOnly]
        public ComponentLookup<GameVariant> entityVariants;

        [ReadOnly]
        public ComponentLookup<GameExp> entityExps;

        [ReadOnly]
        public ComponentLookup<GamePower> entityPowers;

        [ReadOnly]
        public ComponentLookup<GameLevel> entityLevels;

        [ReadOnly]
        public NativeArray<Level> levels;

        [ReadOnly]
        public NativeArray<Property> properties;

        [ReadOnly]
        public NativeArray<GameActionSpawn> spawns;

        [ReadOnly]
        public NativeArray<ItemData> items;

        [ReadOnly]
        public NativeArray<ActionData> actions;

        [ReadOnly]
        public NativeArray<GameActionAttack> actionAttacks;

        [ReadOnly]
        public NativeArray<GameActionAttack> itemAttacks;

        [ReadOnly]
        public NativeArray<GameEntityDefence> itemDefences;

        [ReadOnly]
        public NativeHashMap<int, int> itemTypeIndices;

        //[ReadOnly]
        //public BufferLookup<GameEntityItem> entityItems;

        [ReadOnly]
        public BufferLookup<GameEntityDefence> defences;
        
        public BufferTypeHandle<GameActionAttack> attackType;

        public ComponentTypeHandle<GameActionBuff> buffType;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<GameEntityHealthDamage> healthDamages;

        public NativeFactory<BufferElementData<GameEntityHealthBuff>>.ParallelWriter healthBuffs;

        public NativeFactory<BufferElementData<GameEntityTorpidityBuff>>.ParallelWriter torpidityBuffs;

        public EntityCommandQueue<GameSpawnData>.ParallelWriter entityMananger;

        public Handler Create(in ArchetypeChunk chunk)
        {
            Handler handler;
            handler.propertyCount = propertyCount;
            handler.hitCount = hitCount;
            handler.hierarchy = hierarchy;
            handler.nodeStates = nodeStates;
            handler.itemRoots = itemRoots;
            handler.entityVariants = entityVariants;
            handler.entityExps = entityExps;
            handler.entityPowers = entityPowers;
            handler.entityLevels = entityLevels;
            handler.levels = levels;
            handler.properties = properties;
            handler.spawns = spawns;
            handler.items = items;
            handler.actions = actions;
            handler.actionAttacks = actionAttacks;
            handler.itemAttacks = itemAttacks;
            handler.itemDefences = itemDefences;
            //handler.entityItems = entityItems;
            handler.itemTypeIndices = itemTypeIndices;
            handler.defences = defences;
            handler.attacks = chunk.GetBufferAccessor(ref attackType);
            handler.buffs = chunk.GetNativeArray(ref buffType);
            handler.healthDamages = healthDamages;
            handler.healthBuffs = healthBuffs;
            handler.torpidityBuffs = torpidityBuffs;
            handler.entityMananger = entityMananger;

            return handler;
        }
    }
    
    [BurstCompile]
    private struct Convert : IJob
    {
        public NativeFactory<BufferElementData<GameEntityHealthBuff>> healthInputs;
        public NativeFactory<BufferElementData<GameEntityTorpidityBuff>> torpidityInputs;
        public BufferLookup<GameEntityHealthBuff> healthOutputs;
        public BufferLookup<GameEntityTorpidityBuff> torpidityOutputs;

        public void Execute()
        {
            DynamicBuffer<GameEntityHealthBuff> healthBuffs;
            foreach(var healthBuff in healthInputs)
            {
                if (!healthOutputs.HasBuffer(healthBuff.entity))
                    continue;

                healthBuffs = healthOutputs[healthBuff.entity];
                healthBuffs.Add(healthBuff.value);
            }

            healthInputs.Clear();

            DynamicBuffer<GameEntityTorpidityBuff> torpidityBuffs;
            foreach(var torpidityBuff in torpidityInputs)
            {
                if (!torpidityOutputs.HasBuffer(torpidityBuff.entity))
                    continue;

                torpidityBuffs = torpidityOutputs[torpidityBuff.entity];
                torpidityBuffs.Add(torpidityBuff.value);
            }

            torpidityInputs.Clear();
        }
    }
    
    private int __hitCount;

    private int __propertyCount;
    
    private EntityCommandPool<GameSpawnData> __spawnCommander;

    private NativeArray<GameActionSpawn> __spawns;
    private NativeArray<ActionData> __actions;
    private NativeArray<ItemData> __items;
    private NativeArray<Level> __levels;
    private NativeArray<Property> __properties;
    private NativeArray<GameActionAttack> __actionAttacks;
    private NativeArray<GameActionAttack> __itemAttacks;
    private NativeArray<GameEntityDefence> __itemDefences;
    private NativeHashMap<int, int> __itemTypeIndices;

    private NativeFactory<BufferElementData<GameEntityHealthBuff>> __healthBuffs;
    private NativeFactory<BufferElementData<GameEntityTorpidityBuff>> __torpidityBuffs;

    private GameEntityActionSystemCore __core;

    private GameItemManagerShared __itemManager;

    public IEnumerable<EntityQueryDesc> queries => new EntityQueryDesc[]
    {
        new EntityQueryDesc()
        {
            All = new ComponentType[]
            {
                ComponentType.ReadWrite<GameActionBuff>(),
                ComponentType.ReadWrite<GameActionAttack>()
            }
        }
    };

    public void Create<T>(
        World world, 
        int hitCount, 
        IEnumerable<Action> actions, 
        IEnumerable<Item> items,
        LevelData[] levels, 
        T spawnCommander = default) where T : GameSpawnCommander
    {
        __hitCount = hitCount;

        __propertyCount = 0;

        int numSpawns = 0, numActions = 0;
        foreach(Action action in actions)
        {
            numSpawns += action.spawns == null ? 0 : action.spawns.Length;

            ++numActions;

            __propertyCount = math.max(__propertyCount, action.attacks == null ? 0 : action.attacks.Length);
        }

        int numItems = 0;
        foreach (Item item in items)
        {
            ++numItems;

            __propertyCount = math.max(__propertyCount, item.attacks == null ? 0 : item.attacks.Length);
            __propertyCount = math.max(__propertyCount, item.defences == null ? 0 : item.defences.Length);
        }

        int i, count, num = 0, index = 0;
        ActionData actionDestination;
        actionDestination.spawnStartIndex = 0;

        __spawns = new NativeArray<GameActionSpawn>(numSpawns, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        __actions = new NativeArray<ActionData>(numActions, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        __actionAttacks = new NativeArray<GameActionAttack>(numActions * __propertyCount, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        foreach (Action actionSource in actions)
        {
            actionDestination.spawnCount = actionSource.spawns == null ? 0 : actionSource.spawns.Length;
            for (i = 0; i < actionDestination.spawnCount; ++i)
                __spawns[actionDestination.spawnStartIndex + i] = actionSource.spawns[i];

            actionDestination.buff = actionSource.buff;
            __actions[num++] = actionDestination;

            actionDestination.spawnStartIndex += actionDestination.spawnCount;

            count = actionSource.attacks == null ? 0 : actionSource.attacks.Length;
            for (i = 0; i < count; ++i)
                __actionAttacks[index++] = actionSource.attacks[i];

            for (i = count; i < __propertyCount; ++i)
                __actionAttacks[index++] = 0;
        }

        num = 0;
        index = 0;
        __items = new NativeArray<ItemData>(numItems, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);

        count = numItems * __propertyCount;
        __itemAttacks = new NativeArray<GameActionAttack>(count, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        __itemDefences = new NativeArray<GameEntityDefence>(count, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);

        __itemTypeIndices = new NativeHashMap<int, int>(numItems, Allocator.Persistent);

        int temp;
        ItemData itemDestination;
        foreach (Item itemSource in items)
        {
            __itemTypeIndices[itemSource.type] = num;

            itemDestination.buff = itemSource.buff;
            __items[num++] = itemDestination;

            temp = index;
            count = itemSource.attacks == null ? 0 : itemSource.attacks.Length;
            for (i = 0; i < count; ++i)
                __itemAttacks[temp++] = itemSource.attacks[i];

            for (i = count; i < __propertyCount; ++i)
                __itemAttacks[temp++] = 0;

            temp = index;
            count = itemSource.defences == null ? 0 : itemSource.defences.Length;
            for (i = 0; i < count; ++i)
                __itemDefences[temp++] = itemSource.defences[i];

            for (i = count; i < __propertyCount; ++i)
                __itemDefences[temp++] = 0;

            index += __propertyCount;
        }

        int numLevels = levels.Length, numProperties = 0;
        LevelData sourceLevel;
        Level destinationLevel;
        __levels = new NativeArrayLite<Level>(numLevels, Allocator.Persistent);
        for(i = 0; i < numLevels; ++i)
        {
            sourceLevel = levels[i];
            destinationLevel.stageExpFactor = sourceLevel.stageExpFactor;

            destinationLevel.attackStartIndex = numProperties;
            destinationLevel.attackCount = sourceLevel.attacks == null ? 0 : sourceLevel.attacks.Length;

            numProperties += destinationLevel.attackCount;

            destinationLevel.defenceStartIndex = numProperties;
            destinationLevel.defenceCount = sourceLevel.defences == null ? 0 : sourceLevel.defences.Length;

            numProperties += destinationLevel.defenceCount;

            __levels[i] = destinationLevel;
        }

        int propertyIndex = 0, j;
        __properties = new NativeArrayLite<Property>(numProperties, Allocator.Persistent);
        for (i = 0; i < numLevels; ++i)
        {
            sourceLevel = levels[i];

            numProperties = sourceLevel.attacks == null ? 0 : sourceLevel.attacks.Length;
            for (j = 0; j < numProperties; ++j)
                __properties[propertyIndex++] = sourceLevel.attacks[j];

            numProperties = sourceLevel.defences == null ? 0 : sourceLevel.defences.Length;
            for (j = 0; j < numProperties; ++j)
                __properties[propertyIndex++] = sourceLevel.defences[j];
        }

        __spawnCommander = world.GetExistingSystemManaged<EndTimeSystemGroupEntityCommandSystem>().Create<GameSpawnData, T>(EntityCommandManager.QUEUE_PRESENT, spawnCommander);
    }

    public void OnCreate(ref SystemState state)
    {
        BurstUtility.InitializeJob<Convert>();

        __healthBuffs = new NativeFactory<BufferElementData<GameEntityHealthBuff>>(Allocator.Persistent, true);
        __torpidityBuffs = new NativeFactory<BufferElementData<GameEntityTorpidityBuff>>(Allocator.Persistent, true);

        __core = new GameEntityActionSystemCore(queries, ref state);

        __itemManager = state.World.GetOrCreateSystemUnmanaged<GameItemSystem>().manager;
    }

    public void OnDestroy(ref SystemState state)
    {
        if (__spawns.IsCreated)
            __spawns.Dispose();

        if (__actions.IsCreated)
            __actions.Dispose();

        if (__items.IsCreated)
            __items.Dispose();

        if (__levels.IsCreated)
            __levels.Dispose();

        if (__properties.IsCreated)
            __properties.Dispose();

        if (__actionAttacks.IsCreated)
            __actionAttacks.Dispose();

        if (__itemAttacks.IsCreated)
            __itemAttacks.Dispose();

        if (__itemDefences.IsCreated)
            __itemDefences.Dispose();

        if(__itemTypeIndices.IsCreated)
            __itemTypeIndices.Dispose();

        __healthBuffs.Dispose();
        __torpidityBuffs.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (__hitCount < 1)
            return;

        ref var lookupJobManager = ref __itemManager.lookupJobManager;

        state.Dependency = JobHandle.CombineDependencies(state.Dependency, lookupJobManager.readOnlyJobHandle);

        var spawnCommandQueue = __spawnCommander.Create();

        Factory factory;
        factory.propertyCount = __propertyCount;
        factory.hitCount = __hitCount;
        factory.hierarchy = __itemManager.hierarchy;
        factory.nodeStates = state.GetComponentLookup<GameNodeStatus>(true);
        factory.itemRoots = state.GetComponentLookup<GameItemRoot>(true);
        factory.entityVariants = state.GetComponentLookup<GameVariant>(true);
        factory.entityExps = state.GetComponentLookup<GameExp>(true);
        factory.entityPowers = state.GetComponentLookup<GamePower>(true);
        factory.entityLevels = state.GetComponentLookup<GameLevel>(true);
        factory.levels = __levels;
        factory.properties = __properties;
        factory.spawns = __spawns;
        factory.items = __items;
        factory.actions = __actions;
        factory.actionAttacks = __actionAttacks;
        factory.itemAttacks = __itemAttacks;
        factory.itemDefences = __itemDefences;
        //factory.entityItems = GetBufferLookup<GameEntityItem>(true);
        factory.itemTypeIndices = __itemTypeIndices;
        factory.defences = state.GetBufferLookup<GameEntityDefence>(true);
        factory.attackType = state.GetBufferTypeHandle<GameActionAttack>();
        factory.buffType = state.GetComponentTypeHandle<GameActionBuff>();
        factory.healthDamages = state.GetComponentLookup<GameEntityHealthDamage>();
        factory.healthBuffs = ((NativeFactory<BufferElementData<GameEntityHealthBuff>>)__healthBuffs).parallelWriter;
        factory.torpidityBuffs = ((NativeFactory<BufferElementData<GameEntityTorpidityBuff>>)__torpidityBuffs).parallelWriter;
        factory.entityMananger = spawnCommandQueue.parallelWriter;

        if (__core.Update<Handler, Factory>(factory, ref state))
        {
            JobHandle jobHandle = state.Dependency, performJob = __core.performJob;

            spawnCommandQueue.AddJobHandleForProducer<GameEntityActionDataSystem>(jobHandle);

            lookupJobManager.AddReadOnlyDependency(performJob);

            Convert convert;
            convert.healthInputs = __healthBuffs;
            convert.torpidityInputs = __torpidityBuffs;
            convert.healthOutputs = state.GetBufferLookup<GameEntityHealthBuff>();
            convert.torpidityOutputs = state.GetBufferLookup<GameEntityTorpidityBuff>();

            state.Dependency = JobHandle.CombineDependencies(jobHandle, convert.Schedule(performJob));
        }
    }
}
