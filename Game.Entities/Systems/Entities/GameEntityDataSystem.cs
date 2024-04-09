using System;
using System.Collections.Generic;
using Unity.Jobs;
using Unity.Burst.Intrinsics;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Collections;
using ZG;
using Unity.Collections.LowLevel.Unsafe;
using Random = Unity.Mathematics.Random;

//[assembly: RegisterGenericJobType(typeof(GameEntityActionSystemCore.PerformEx<GameEntityActionDataSystem.Handler, GameEntityActionDataSystem.Factory>))]

/*public struct GameEntityActionDataDefinition
{
    public struct Action
    {
        public GameBuff buff;

        public BlobArray<GameActionAttack> attacks;

        public BlobArray<GameActionSpawn> spawns;
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

}*/

[Serializable]
public struct GameActionPickableData
{
    [Serializable]
    public struct Range
    {
        public float min;
        public float max;
        public float chance;

        public float Compute(float value, float entityMax)
        {
            if (chance > math.FLT_MIN_NORMAL)
            {
                float result = max > min ? math.smoothstep(max, min, value) : 0.0f;
                if (entityMax > math.FLT_MIN_NORMAL)
                    result *= math.clamp(max / entityMax, 0.0f, 1.0f);

                return result * chance;
            }

            return 0.0f;
        }
    }
    
    public Range health;
    public Range torpidity;
    public Range animal;

    public float Compute( 
        float entityHealthMax, 
        float entityHealthValue, 
        float entityTorpidityMax, 
        float entityTorpidityValue, 
        float animalMax, 
        float animalValue)
    {
        float chance = health.Compute(entityHealthValue, entityHealthMax);
        chance += torpidity.Compute(entityTorpidityValue, entityTorpidityMax);
        chance += animal.Compute(animalValue, animalMax);

        return chance;
    }
}

[BurstCompile,
    //CreateAfter(typeof(GameEntityActionLocationSystem)),
    //CreateAfter(typeof(GamePhysicsWorldBuildSystem)),
    CreateAfter(typeof(GameItemSystem)),
    CreateAfter(typeof(GameItemStructChangeSystem)), 
    CreateAfter(typeof(GameRandomSpawnerSystem)), 
    CreateAfter(typeof(GameEntityActionSystem)), 
    CreateAfter(typeof(GameEntityActionDataPickSystem)), 
    UpdateInGroup(typeof(GameEntityActionSystemGroup), OrderLast = true)]
public partial struct GameEntityActionDataSystem : ISystem//, IEntityCommandProducerJob //GameEntityActionSystem<GameEntityActionDataSystem.Handler, GameEntityActionDataSystem.Factory>, IEntityCommandProducerJob
{
    public struct Action
    {
        public GameActionSpawnFlag spawnFlag;

        public GameBuff buff;

        public GameActionPickableData pickable;

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
        public GameActionSpawnFlag spawnFlag;

        public int spawnStartIndex;
        public int spawnCount;

        public GameActionPickableData pickable;

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

    private struct Count
    {
        public NativeArray<int> counter;

        [ReadOnly]
        public NativeArray<ActionData> actions;

        [ReadOnly]
        public NativeArray<GameActionData> instances;

        public void Execute(int index)
        {
            counter[0] += actions[instances[index].actionIndex].spawnCount;
        }
    }

    [BurstCompile]
    private struct CountEx : IJobChunk
    {
        public NativeArray<int> counter;

        [ReadOnly]
        public NativeArray<ActionData> actions;

        [ReadOnly]
        public ComponentTypeHandle<GameActionData> instanceType;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Count count;
            count.counter = counter;
            count.actions = actions;
            count.instances = chunk.GetNativeArray(ref instanceType);

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                count.Execute(i);
        }
    }

    [BurstCompile]
    private struct Recapcity : IJob
    {
        [ReadOnly, DeallocateOnJobCompletion]
        public NativeArray<int> counter;

        public NativeList<GameSpawnData> results;

        public void Execute()
        {
            results.Capacity = math.max(results.Capacity, results.Length + counter[0]);
        }
    }
    
    private struct PropertyCalculator
    {
        [ReadOnly]
        public NativeArray<Level> levels;

        [ReadOnly]
        public NativeArray<Property> properties;

        [ReadOnly]
        public ComponentLookup<GameItemVariant> entityVariants;

        [ReadOnly]
        public ComponentLookup<GameItemExp> entityExps;

        [ReadOnly]
        public ComponentLookup<GameItemPower> entityPowers;

        [ReadOnly]
        public ComponentLookup<GameItemLevel> entityLevels;

        public PropertyCalculator(
            in NativeArray<Level> levels,
            in NativeArray<Property> properties,
            in ComponentLookup<GameItemVariant> entityVariants,
            in ComponentLookup<GameItemExp> entityExps,
            in ComponentLookup<GameItemPower> entityPowers,
            in ComponentLookup<GameItemLevel> entityLevels)
        {
            this.levels = levels;
            this.properties = properties;
            this.entityVariants = entityVariants;
            this.entityExps = entityExps;
            this.entityPowers = entityPowers;
            this.entityLevels = entityLevels;
        }

        public void Calculate(bool isAttack, in Entity entity, ref NativeArray<float> properties)
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
            var propertyIndices = new NativeList<int>(Allocator.Temp);
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
    }

    private struct BuffCalculator
    {
        public int propertyCount;

        public int hitCount;

        [ReadOnly]
        public GameItemManager.ReadOnly itemManager;

        [ReadOnly]
        public NativeArray<ItemData> items;

        [ReadOnly]
        public NativeHashMap<int, int> itemTypeIndices;

        public BuffCalculator(
            int propertyCount, 
            int hitCount,
            in GameItemManager.ReadOnly itemManager, 
            in NativeArray<ItemData> items, 
            in NativeHashMap<int, int> itemTypeIndices)
        {
            this.propertyCount = propertyCount;
            this.hitCount = hitCount;
            this.itemManager = itemManager;
            this.items = items;
            this.itemTypeIndices = itemTypeIndices;
        }

        public bool Calculate(
            in GameItemHandle handle,
            in NativeArray<float> inputs,
            ref NativeArray<float> outputs)
        {
            return __Calculate(
                false,
                handle,
                inputs,
                ref outputs,
                out _);
        }

        public bool Calculate(
            in GameItemHandle handle,
            ref GameBuff buff)
        {
            return __Calculate(false, handle, ref buff, out _);
        }

        public bool __Calculate(
            bool isSibling,
            in GameItemHandle handle,
            in NativeArray<float> inputs,
            ref NativeArray<float> outputs,
            out GameItemInfo item)
        {
            if (itemManager.hierarchy.GetChildren(handle, out var enumerator, out item))
            {
                __Calculate(
                    true,
                    item.siblingHandle,
                    inputs,
                    ref outputs,
                    out _);

                if (isSibling)
                {
                    GameItemInfo childItem;
                    while (enumerator.MoveNext())
                    {
                        if (__Calculate(
                            false,
                            enumerator.Current.handle,
                            inputs,
                            ref outputs,
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
                        __Calculate(
                            false,
                            enumerator.Current.handle,
                            inputs,
                            ref outputs,
                            out _);
                }

                return true;
            }

            return false;
        }

        public bool __Calculate(
            bool isSibling,
            in GameItemHandle handle,
            ref GameBuff buff,
            out GameItemInfo item)
        {
            if (itemManager.hierarchy.GetChildren(handle, out var enumerator, out item))
            {
                __Calculate(
                    true,
                    item.siblingHandle,
                    ref buff,
                    out _);

                if (isSibling)
                {
                    GameItemInfo childItem;
                    while (enumerator.MoveNext())
                    {
                        if (__Calculate(
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
                        __Calculate(
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

    private struct Spawner
    {
        [ReadOnly]
        public NativeArray<GameActionSpawn> spawns;

        [NativeDisableContainerSafetyRestriction]
        public SharedList<GameSpawnData>.ParallelWriter results;

        public Spawner(
            in NativeArray<GameActionSpawn> spawns, 
            SharedList<GameSpawnData>.ParallelWriter results)
        {
            this.spawns = spawns;
            this.results = results;
        }

        public void Spawn(
            int startIndex,
            int count,
            GameActionSpawnType type,
            //double time,
            in Entity entity,
            in RigidTransform transform, 
            ref GameItemHandle handle)
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
                        destination.velocity = float3.zero;
                        destination.transform = transform;
                        /*if(handle.Equals(GameItemHandle.Empty))
                            destination.transform = transform;
                        else
                        {
                            var forward = math.forward(transform.rot);
                            forward = ZG.Mathematics.Math.ProjectOnPlane(forward, math.up());
                            destination.transform = math.RigidTransform(
                                ZG.Mathematics.Math.FromToRotation(math.float3(0.0f, 0.0f, 1.0f), forward), 
                                transform.pos);
                        }*/
                        
                        destination.itemHandle = handle;
                        
                        handle = GameItemHandle.Empty;

                        results.AddNoResize(destination);
                    }
                }
            }
        }
    }

    [BurstCompile]
    private struct ApplyInitializers : IJobParallelForDefer
    {
        public PropertyCalculator propertyCalculator;
        public BuffCalculator buffCalculator;
        public Spawner spawner;

        [ReadOnly]
        public NativeArray<ActionData> actions;

        [ReadOnly]
        public SharedList<GameEntityActionInitializer>.Reader initializers;

        [ReadOnly]
        public SharedHashMap<Entity, Entity>.Reader itemHandleEntities;

        [ReadOnly]
        public ComponentLookup<GameActionData> instances;

        [ReadOnly]
        public ComponentLookup<GameItemRoot> itemRoots;

        [ReadOnly]
        public NativeArray<GameActionAttack> actionAttacks;

        [ReadOnly]
        public NativeArray<GameActionAttack> itemAttacks;

        [NativeDisableParallelForRestriction]
        public BufferLookup<GameActionAttack> attacks;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<GameActionBuff> buffs;

        public void Execute(int index)
        {
            var initializer = initializers[index];

            var instance = instances[initializer.entity];

            var action = actions[instance.actionIndex];
            if (this.attacks.HasBuffer(initializer.entity))
            {
                GameActionBuff buff;
                buff.value = action.buff;

                var attacks = this.attacks[initializer.entity].Reinterpret<float>();
                attacks.ResizeUninitialized(buffCalculator.propertyCount);

                int temp = instance.actionIndex * buffCalculator.propertyCount;
                for (int i = 0; i < buffCalculator.propertyCount; ++i)
                    attacks[i] = actionAttacks[temp++].value;

                if (itemRoots.HasComponent(instance.entity))
                {
                    var handle = itemRoots[instance.entity].handle;
                    if (itemHandleEntities.TryGetValue(GameItemStructChangeFactory.Convert(handle), out var handleEntity))
                    {
                        var attackArray = attacks.AsNativeArray();
                        propertyCalculator.Calculate(
                            true, 
                            handleEntity,
                            ref attackArray);

                        buffCalculator.Calculate(handle, itemAttacks.Reinterpret<float>(), ref attackArray);

                        buffCalculator.Calculate(handle, ref buff.value);
                    }
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

                if (buffs.HasComponent(initializer.entity))
                    buffs[initializer.entity] = buff;
            }

            var emptyItem  = GameItemHandle.Empty;
            spawner.Spawn(
                action.spawnStartIndex,
                action.spawnCount,
                GameActionSpawnType.Init,
                //instance.time + initializer.elapsedTime,
                instance.entity,
                initializer.transform, 
                ref emptyItem);
        }
    }

    [BurstCompile]
    private struct ApplyHiters : IJob, IEntityCommandProducerJob
    {
        public Spawner spawner;

        [ReadOnly]
        public NativeArray<ActionData> actions;

        [ReadOnly]
        public NativeFactory<GameEntityActionHiter> hiters;

        [ReadOnly]
        public ComponentLookup<GameActionData> instances;

        public void Execute(in GameEntityActionHiter hiter)
        {
            var instance = instances[hiter.entity];

            var action = actions[instance.actionIndex];

            var emptyItem = GameItemHandle.Empty;
            spawner.Spawn(
                action.spawnStartIndex,
                action.spawnCount,
                GameActionSpawnType.Hit,
                //instance.time + elapsedTime,
                instance.entity,
                hiter.transform,
                ref emptyItem);
        }

        public void Execute()
        {
            foreach (var hiter in hiters)
                Execute(hiter);
        }
    }

    [BurstCompile]
    private struct ApplyDamagers : IJob
    {
        public PropertyCalculator propertyCalculator;
        public BuffCalculator buffCalculator;
        public Spawner spawner;

        [ReadOnly]
        public NativeArray<ActionData> actions;

        [ReadOnly]
        public NativeFactory<GameEntityActionDamager> damagers;

        [ReadOnly]
        public SharedHashMap<Entity, Entity>.Reader itemHandleEntities;

        [ReadOnly]
        public ComponentLookup<GameActionData> instances;

        [ReadOnly]
        public ComponentLookup<GameItemRoot> itemRoots;

        [ReadOnly]
        public ComponentLookup<GameOwner> owners;

        [ReadOnly]
        public ComponentLookup<GameNodeStatus> nodeStates;

        [ReadOnly]
        public NativeArray<GameEntityDefence> itemDefences;

        //[ReadOnly]
        //public BufferLookup<GameEntityItem> entityItems;

        [ReadOnly]
        public BufferLookup<GameEntityDefence> defences;

        [ReadOnly]
        public BufferLookup<GameActionAttack> attacks;

        [ReadOnly]
        public ComponentLookup<GameActionBuff> buffs;

        [ReadOnly]
        public ComponentLookup<GameAnimalData> animals;

        [ReadOnly]
        public ComponentLookup<GameAnimalInfo> animalInfos;

        [ReadOnly]
        public ComponentLookup<GameEntityTorpidityData> torpidityMaxes;
        
        [ReadOnly]
        public ComponentLookup<GameEntityTorpidity> torpidities;

        [ReadOnly]
        public ComponentLookup<GameEntityHealthData> healthMaxes;

        [ReadOnly]
        public ComponentLookup<GameEntityHealth> healthes;

        public NativeQueue<BufferElementData<GameEntityHealthDamage>>.ParallelWriter healthDamages;

        public NativeQueue<BufferElementData<GameEntityHealthBuff>>.ParallelWriter healthBuffs;

        public NativeQueue<BufferElementData<GameEntityTorpidityBuff>>.ParallelWriter torpidityBuffs;

        public NativeList<Entity> entitiesToPick;

        public EntityAddDataQueue.Writer entityManager;

        public void Execute(in GameEntityActionDamager damager, ref NativeArray<float> defences)
        {
            var instance = instances[damager.entity];

            float torpor = 0.0f;
            var attacks = this.attacks.HasBuffer(damager.entity) ? this.attacks[damager.entity] : default;
            int numAttacks = math.min(attacks.IsCreated ? attacks.Length : 0, buffCalculator.propertyCount);
            if (numAttacks > 0)
            {
                if (this.defences.HasBuffer(damager.target))
                {
                    var temp = this.defences[damager.target].Reinterpret<float>().AsNativeArray();
                    NativeArray<float>.Copy(temp, defences, math.min(temp.Length, defences.Length));
                }

                if (itemRoots.HasComponent(damager.target))
                {
                    var handle = itemRoots[damager.target].handle;
                    if (itemHandleEntities.TryGetValue(GameItemStructChangeFactory.Convert(handle),
                            out Entity handleEntity))
                    {
                        buffCalculator.Calculate(
                            handle,
                            itemDefences.Reinterpret<float>(),
                            ref defences);

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

                        propertyCalculator.Calculate(
                            false,
                            handleEntity,
                            ref defences);
                    }
                }

                float attack, defence, value, hit = 0.0f;
                for (int i = 0; i < numAttacks; ++i)
                {
                    attack = attacks[i];
                    defence = defences[i];

                    if (defence < 0)
                        value = attack > 0 ? attack - defence : 0;
                    else
                        value = math.max(attack, defence) - defence;

                    if (i < buffCalculator.hitCount)
                        hit += value;
                    else
                        torpor += value;
                }

                //defences.Dispose();

                hit *= damager.count;
                torpor *= damager.count;

                double now = instance.time + damager.elapsedTime;

                if (math.abs(hit) > math.FLT_MIN_NORMAL)
                {
                    BufferElementData<GameEntityHealthDamage> healthDamage;
                    healthDamage.entity = damager.target;
                    healthDamage.value.value = hit;
                    healthDamage.value.time = now;
                    healthDamage.value.entity = instance.entity;

                    healthDamages.Enqueue(healthDamage);
                }
            }

            if (buffs.HasComponent(damager.entity))
            {
                var buff = buffs[damager.entity];
                if (buff.value.healthTime > math.FLT_MIN_NORMAL)
                {
                    BufferElementData<GameEntityHealthBuff> healthBuff;

                    healthBuff.entity = damager.target;
                    healthBuff.value.value = buff.value.healthPerTime;
                    healthBuff.value.duration = buff.value.healthTime;
                    healthBuffs.Enqueue(healthBuff);
                }

                if (math.abs(torpor) > math.FLT_MIN_NORMAL &&
                    buff.value.torpidityTime > math.FLT_MIN_NORMAL &&
                    nodeStates.HasComponent(damager.target) &&
                    (nodeStates[damager.target].value & (int)GameEntityStatus.KnockedOut) != (int)GameEntityStatus.KnockedOut)
                {
                    BufferElementData<GameEntityTorpidityBuff> torpidityBuff;

                    torpidityBuff.entity = damager.target;
                    torpidityBuff.value.value = -torpor / buff.value.torpidityTime;
                    torpidityBuff.value.duration = buff.value.torpidityTime;
                    torpidityBuffs.Enqueue(torpidityBuff);
                }
            }

            var forward = math.forward(damager.transform.rot);
            forward = ZG.Mathematics.Math.ProjectOnPlane(forward, math.up());
            var transform = math.RigidTransform(
                ZG.Mathematics.Math.FromToRotation(math.float3(0.0f, 0.0f, 1.0f), forward),
                damager.transform.pos);

            var action = actions[instance.actionIndex];
            if ((action.spawnFlag & GameActionSpawnFlag.DamageToPicked) == GameActionSpawnFlag.DamageToPicked)
            {
                var sourceHandle = GameItemHandle.Empty;
                if ((!owners.HasComponent(damager.target) || owners[damager.target].entity == Entity.Null) &&
                    itemRoots.HasComponent(damager.target) &&
                    itemRoots.HasComponent(instance.entity))
                {
                    var handle = itemRoots[damager.target].handle;
                    if (buffCalculator.itemManager.TryGetValue(handle, out var item) &&
                        buffCalculator.itemManager.Find(
                            itemRoots[instance.entity].handle,
                            item.type,
                            item.count,
                            out _,
                            out _))
                        sourceHandle = handle;
                }

                if (!sourceHandle.Equals(GameItemHandle.Empty))
                {
                    var destinationHandle = sourceHandle;

                    spawner.Spawn(
                        action.spawnStartIndex,
                        action.spawnCount,
                        GameActionSpawnType.Damage,
                        //instance.time + elapsedTime,
                        instance.entity,
                        transform,
                        //math.RigidTransform(Math.FromToRotation(math.up(), damager.normal), damager.position), 
                        ref destinationHandle);

                    if ((action.spawnFlag & GameActionSpawnFlag.DamageToPicked) == GameActionSpawnFlag.DamageToPicked &&
                        destinationHandle.Equals(GameItemHandle.Empty) &&
                        itemHandleEntities.TryGetValue(GameItemStructChangeFactory.Convert(sourceHandle),
                            out Entity entity))
                    {
                        GameItemSpawnStatus status;
                        status.nodeStatus = nodeStates.HasComponent(damager.target)
                            ? nodeStates[damager.target].value
                            : 0;
                        status.entityHealth = healthes.HasComponent(damager.target)
                            ? healthes[damager.target].value
                            : 0.0f;
                        status.entityTorpidity = torpidities.HasComponent(damager.target)
                            ? torpidities[damager.target].value
                            : 0.0f;
                        status.animalValue = animalInfos.HasComponent(damager.target)
                            ? animalInfos[damager.target].value
                            : 0.0f;
                        status.chance = action.pickable.Compute(
                            healthMaxes.HasComponent(damager.target) ? healthMaxes[damager.target].max : 0,
                            status.entityHealth,
                            torpidityMaxes.HasComponent(damager.target) ? torpidityMaxes[damager.target].max : 0,
                            status.entityTorpidity,
                            animals.HasComponent(damager.target) ? animals[damager.target].max : 0,
                            status.animalValue);
                        status.handle = itemRoots.HasComponent(instance.entity)
                            ? itemRoots[instance.entity].handle
                            : GameItemHandle.Empty;

                        entityManager.AddComponentData(entity, status);

                        entitiesToPick.Add(damager.target);
                    }
                }
            }
            else
            {
                var handle = GameItemHandle.Empty;
                
                spawner.Spawn(
                    action.spawnStartIndex,
                    action.spawnCount,
                    GameActionSpawnType.Damage,
                    //instance.time + elapsedTime,
                    instance.entity,
                    transform,
                    //math.RigidTransform(Math.FromToRotation(math.up(), damager.normal), damager.position), 
                    ref handle);
            }
        }

        public void Execute()
        {
            var defences = new NativeArray<float>(buffCalculator.propertyCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);

            foreach (var damager in damagers)
                Execute(damager, ref defences);

            defences.Dispose();
        }
    }

    [BurstCompile]
    private struct Convert : IJob
    {
        public NativeQueue<BufferElementData<GameEntityHealthDamage>> damageInputs;
        public NativeQueue<BufferElementData<GameEntityHealthBuff>> healthInputs;
        public NativeQueue<BufferElementData<GameEntityTorpidityBuff>> torpidityInputs;
        public BufferLookup<GameEntityHealthDamage> damageOutputs;
        public BufferLookup<GameEntityHealthBuff> healthOutputs;
        public BufferLookup<GameEntityTorpidityBuff> torpidityOutputs;

        public void Execute()
        {
            while(damageInputs.TryDequeue(out var damage))
            {
                if (!damageOutputs.HasBuffer(damage.entity))
                    continue;

                damageOutputs[damage.entity].Add(damage.value);
            }

            while(healthInputs.TryDequeue(out var healthBuff))
            {
                if (!healthOutputs.HasBuffer(healthBuff.entity))
                    continue;

                healthOutputs[healthBuff.entity].Add(healthBuff.value);
            }

            DynamicBuffer<GameEntityTorpidityBuff> torpidityBuffs;
            while(torpidityInputs.TryDequeue(out var torpidityBuff))
            {
                if (!torpidityOutputs.HasBuffer(torpidityBuff.entity))
                    continue;

                torpidityOutputs[torpidityBuff.entity].Add(torpidityBuff.value);
            }
        }
    }

    private int __hitCount;

    private int __propertyCount;

    private EntityQuery __group;

    private ComponentTypeHandle<GameActionData> __instanceType;

    private ComponentLookup<GameActionData> __instances;

    private ComponentLookup<GameItemRoot> __itemRoots;

    private ComponentLookup<GameItemVariant> __entityVariants;

    private ComponentLookup<GameItemExp> __entityExps;

    private ComponentLookup<GameItemPower> __entityPowers;

    private ComponentLookup<GameItemLevel> __entityLevels;

    private ComponentLookup<GameAnimalData> __animals;

    private ComponentLookup<GameAnimalInfo> __animalInfos;

    private ComponentLookup<GameEntityTorpidityData> __torpidityMaxes;

    private ComponentLookup<GameEntityTorpidity> __torpidites;

    private ComponentLookup<GameEntityHealthData> __healthMaxes;

    private ComponentLookup<GameEntityHealth> __healthes;

    private BufferLookup<GameEntityDefence> __defences;

    private BufferLookup<GameActionAttack> __attacks;

    private ComponentLookup<GameActionBuff> __buffs;

    private ComponentLookup<GameOwner> __owners;

    private ComponentLookup<GameNodeStatus> __nodeStates;

    private BufferLookup<GameEntityHealthDamage> __damageOuputs;

    private BufferLookup<GameEntityHealthBuff> __healthOutputs;
    private BufferLookup<GameEntityTorpidityBuff> __torpidityOutputs;

    private GameEntityActionManager __actionManager;

    private GameItemManagerShared __itemManager;
    private SharedHashMap<Entity, Entity> __handleEntities;
    private SharedList<GameSpawnData> __spawnCommands;
    private SharedList<Entity> __entitiesToPick;

    private EntityAddDataPool __entityManager;

    private NativeList<GameActionSpawn> __spawns;
    private NativeList<ActionData> __actions;
    private NativeList<ItemData> __items;
    private NativeList<Level> __levels;
    private NativeList<Property> __properties;
    private NativeList<GameActionAttack> __actionAttacks;
    private NativeList<GameActionAttack> __itemAttacks;
    private NativeList<GameEntityDefence> __itemDefences;
    private NativeHashMap<int, int> __itemTypeIndices;

    private NativeQueue<BufferElementData<GameEntityHealthDamage>> __damages;
    private NativeQueue<BufferElementData<GameEntityHealthBuff>> __healthBuffs;
    private NativeQueue<BufferElementData<GameEntityTorpidityBuff>> __torpidityBuffs;

    public static readonly int InnerloopBatchCount = 4;

    public void Create(
        //World world, 
        int hitCount, 
        IEnumerable<Action> actions, 
        IEnumerable<Item> items,
        LevelData[] levels)
    {
        __hitCount = hitCount;

        __propertyCount = 0;

        int numSpawns = 0, numActions = 0;
        foreach (Action action in actions)
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

        __spawns.Resize(numSpawns, NativeArrayOptions.UninitializedMemory);
        __actions.Resize(numActions, NativeArrayOptions.UninitializedMemory);
        __actionAttacks.Resize(numActions * __propertyCount, NativeArrayOptions.UninitializedMemory);
        foreach (Action actionSource in actions)
        {
            actionDestination.spawnFlag = actionSource.spawnFlag;
            
            actionDestination.spawnCount = actionSource.spawns == null ? 0 : actionSource.spawns.Length;
            for (i = 0; i < actionDestination.spawnCount; ++i)
                __spawns[actionDestination.spawnStartIndex + i] = actionSource.spawns[i];

            actionDestination.pickable = actionSource.pickable;
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
        __items.Resize(numItems, NativeArrayOptions.UninitializedMemory);

        count = numItems * __propertyCount;
        __itemAttacks.Resize(count, NativeArrayOptions.UninitializedMemory);
        __itemDefences.Resize(count, NativeArrayOptions.UninitializedMemory);

        __itemTypeIndices.Clear();// = new NativeHashMap<int, int>(numItems, Allocator.Persistent);

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
        __levels.Resize(numLevels, NativeArrayOptions.UninitializedMemory);// = new NativeArrayLite<Level>(numLevels, Allocator.Persistent);
        for (i = 0; i < numLevels; ++i)
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
        __properties.Resize(numProperties, NativeArrayOptions.UninitializedMemory); //= new NativeArrayLite<Property>(numProperties, Allocator.Persistent);
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

    }

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __hitCount = 0;
        __propertyCount = 0;

        __instanceType = state.GetComponentTypeHandle<GameActionData>(true);

        __instances = state.GetComponentLookup<GameActionData>(true);

        __itemRoots = state.GetComponentLookup<GameItemRoot>(true);
        __entityVariants = state.GetComponentLookup<GameItemVariant>(true);
        __entityExps = state.GetComponentLookup<GameItemExp>(true);
        __entityPowers = state.GetComponentLookup<GameItemPower>(true);
        __entityLevels = state.GetComponentLookup<GameItemLevel>(true);
        
        __owners = state.GetComponentLookup<GameOwner>(true);

        __nodeStates = state.GetComponentLookup<GameNodeStatus>(true);

        __animals = state.GetComponentLookup<GameAnimalData>(true);
        __animalInfos = state.GetComponentLookup<GameAnimalInfo>(true);
        __healthMaxes = state.GetComponentLookup<GameEntityHealthData>(true);
        __healthes = state.GetComponentLookup<GameEntityHealth>(true);
        __torpidityMaxes = state.GetComponentLookup<GameEntityTorpidityData>(true);
        __torpidites = state.GetComponentLookup<GameEntityTorpidity>(true);

        __defences = state.GetBufferLookup<GameEntityDefence>(true);
        __attacks = state.GetBufferLookup<GameActionAttack>();
        __buffs = state.GetComponentLookup<GameActionBuff>();
        
        __damageOuputs = state.GetBufferLookup<GameEntityHealthDamage>();
        __healthOutputs = state.GetBufferLookup<GameEntityHealthBuff>();
        __torpidityOutputs = state.GetBufferLookup<GameEntityTorpidityBuff>();

        var world = state.WorldUnmanaged;
        ref var actionSystem = ref world.GetExistingSystemUnmanaged<GameEntityActionSystem>();
        __group = actionSystem.group;

        state.RequireForUpdate(__group);

        __actionManager = actionSystem.actionManager;
        __itemManager = world.GetExistingSystemUnmanaged<GameItemSystem>().manager;
        __handleEntities = world.GetExistingSystemUnmanaged<GameItemStructChangeSystem>().manager.handleEntities;
        __spawnCommands = world.GetExistingSystemUnmanaged<GameRandomSpawnerSystem>().commands;
        __entitiesToPick = world.GetExistingSystemUnmanaged<GameEntityActionDataPickSystem>().entities;
        __entityManager = world.GetExistingSystemUnmanaged<BeginFrameStructChangeSystem>().addDataPool;

        __spawns = new NativeList<GameActionSpawn>(Allocator.Persistent);
        __actions = new NativeList<ActionData>(Allocator.Persistent);
        __items = new NativeList<ItemData>(Allocator.Persistent);
        __levels = new NativeList<Level>(Allocator.Persistent);
        __properties = new NativeList<Property>(Allocator.Persistent);
        __actionAttacks = new NativeList<GameActionAttack>(Allocator.Persistent);
        __itemAttacks = new NativeList<GameActionAttack>(Allocator.Persistent);
        __itemDefences = new NativeList<GameEntityDefence>(Allocator.Persistent);
        __itemTypeIndices = new NativeHashMap<int, int>(1, Allocator.Persistent);

        __damages = new NativeQueue<BufferElementData<GameEntityHealthDamage>>(Allocator.Persistent);
        __healthBuffs = new NativeQueue<BufferElementData<GameEntityHealthBuff>>(Allocator.Persistent);
        __torpidityBuffs = new NativeQueue<BufferElementData<GameEntityTorpidityBuff>>(Allocator.Persistent);
    }

    public void OnDestroy(ref SystemState state)
    {
        __spawns.Dispose();

        __actions.Dispose();

        __items.Dispose();

        __levels.Dispose();

        __properties.Dispose();

        __actionAttacks.Dispose();

        __itemAttacks.Dispose();

        __itemDefences.Dispose();

        __itemTypeIndices.Dispose();

        __damages.Dispose();
        __healthBuffs.Dispose();
        __torpidityBuffs.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (__hitCount < 1)
            return;

        var counter = new NativeArray<int>(1, Allocator.TempJob, NativeArrayOptions.ClearMemory);

        var actions = __actions.AsArray();

        CountEx count;
        count.counter = counter;
        count.actions = actions;
        count.instanceType = __instanceType.UpdateAsRef(ref state);
        var inputDeps = count.ScheduleByRef(__group, state.Dependency);

        ref var spawnCommandsJobManager = ref __spawnCommands.lookupJobManager;

        Recapcity recapcity;
        recapcity.counter = counter;
        recapcity.results = __spawnCommands.writer;
        inputDeps = recapcity.ScheduleByRef(JobHandle.CombineDependencies(inputDeps, spawnCommandsJobManager.readWriteJobHandle));

        ref var handleEntitiesJobManager = ref __handleEntities.lookupJobManager;
        ref var itemManagerJobManager = ref __itemManager.lookupJobManager;
        var jobHandle = JobHandle.CombineDependencies(inputDeps, handleEntitiesJobManager.readOnlyJobHandle, itemManagerJobManager.readOnlyJobHandle);

        var propertyCalculator = new PropertyCalculator(
            __levels.AsArray(),
            __properties.AsArray(),
            __entityVariants.UpdateAsRef(ref state),
            __entityExps.UpdateAsRef(ref state),
            __entityPowers.UpdateAsRef(ref state),
            __entityLevels.UpdateAsRef(ref state));

        var buffCalculator = new BuffCalculator(
            __propertyCount,
            __hitCount,
            __itemManager.value.readOnly,
            __items.AsArray(),
            __itemTypeIndices);

        var spawner = new Spawner(__spawns.AsArray(), __spawnCommands.parallelWriter);

        var instances = __instances.UpdateAsRef(ref state);
        var itemRoots = __itemRoots.UpdateAsRef(ref state);
        var attacks = __attacks.UpdateAsRef(ref state);
        var buffs = __buffs.UpdateAsRef(ref state);

        var initializers = __actionManager.initializers;
        var itemHandleEntities = __handleEntities.reader;

        ApplyInitializers applyInitializers;
        applyInitializers.propertyCalculator = propertyCalculator;
        applyInitializers.buffCalculator = buffCalculator;
        applyInitializers.spawner = spawner;
        applyInitializers.actions = actions;
        applyInitializers.initializers = initializers.reader;
        applyInitializers.itemHandleEntities = itemHandleEntities;
        applyInitializers.instances = instances;
        applyInitializers.itemRoots = itemRoots;
        applyInitializers.actionAttacks = __actionAttacks.AsArray();
        applyInitializers.itemAttacks = __itemAttacks.AsArray();
        applyInitializers.attacks = attacks;
        applyInitializers.buffs = buffs;

        ref var initializersJobManager = ref initializers.lookupJobManager;
        var applyInitializersJobHandle = JobHandle.CombineDependencies(initializersJobManager.readOnlyJobHandle, jobHandle);
        applyInitializersJobHandle = applyInitializers.ScheduleByRef(initializers.AsList(), InnerloopBatchCount, applyInitializersJobHandle);

        initializersJobManager.AddReadOnlyDependency(applyInitializersJobHandle);

        var hiters = __actionManager.hiters;
        var nodeStates = __nodeStates.UpdateAsRef(ref state);

        ApplyHiters applyHiters;
        applyHiters.spawner = spawner;
        applyHiters.actions = actions;
        applyHiters.hiters = hiters.value;
        applyHiters.instances = instances;
        
        ref var hitersJobManager = ref hiters.lookupJobManager;
        var applyHitersJobHandle = JobHandle.CombineDependencies(hitersJobManager.readOnlyJobHandle, inputDeps);
        applyHitersJobHandle = applyHiters.ScheduleByRef(applyHitersJobHandle);

        hitersJobManager.AddReadOnlyDependency(applyHitersJobHandle);

        var entityManager = __entityManager.Create();

        var damagers = __actionManager.damagers;

        ApplyDamagers applyDamagers;
        applyDamagers.propertyCalculator = propertyCalculator;
        applyDamagers.buffCalculator = buffCalculator;
        applyDamagers.spawner = spawner;
        applyDamagers.actions = actions;
        applyDamagers.damagers = damagers.value;
        applyDamagers.itemHandleEntities = __handleEntities.reader;
        applyDamagers.instances = instances;
        applyDamagers.itemRoots = itemRoots;
        applyDamagers.owners = __owners.UpdateAsRef(ref state);
        applyDamagers.nodeStates = nodeStates;
        applyDamagers.itemDefences = __itemDefences.AsArray();
        applyDamagers.defences = __defences.UpdateAsRef(ref state);
        applyDamagers.attacks = attacks;
        applyDamagers.buffs = buffs;
        applyDamagers.animals = __animals.UpdateAsRef(ref state);
        applyDamagers.animalInfos = __animalInfos.UpdateAsRef(ref state);
        applyDamagers.torpidityMaxes = __torpidityMaxes.UpdateAsRef(ref state);
        applyDamagers.torpidities = __torpidites.UpdateAsRef(ref state);
        applyDamagers.healthMaxes = __healthMaxes.UpdateAsRef(ref state);
        applyDamagers.healthes = __healthes.UpdateAsRef(ref state);
        applyDamagers.healthDamages = __damages.AsParallelWriter();
        applyDamagers.healthBuffs = __healthBuffs.AsParallelWriter();
        applyDamagers.torpidityBuffs = __torpidityBuffs.AsParallelWriter();
        applyDamagers.entitiesToPick = __entitiesToPick.writer;
        applyDamagers.entityManager = entityManager.writer;

        ref var damagersJobManager = ref damagers.lookupJobManager;
        ref var entitiesToPickJobManager = ref __entitiesToPick.lookupJobManager;
        
        var applyDamagersJobHandle = JobHandle.CombineDependencies(damagersJobManager.readOnlyJobHandle, entitiesToPickJobManager.readWriteJobHandle, applyInitializersJobHandle);
        applyDamagersJobHandle = applyDamagers.ScheduleByRef(applyDamagersJobHandle);

        entitiesToPickJobManager.readWriteJobHandle = applyDamagersJobHandle;
        
        damagersJobManager.AddReadOnlyDependency(applyDamagersJobHandle);

        entityManager.AddJobHandleForProducer<ApplyHiters>(applyDamagersJobHandle);

        jobHandle = applyDamagersJobHandle;// JobHandle.CombineDependencies(applyInitializersJobHandle, applyDamagersJobHandle);

        //spawnCommandQueue.AddJobHandleForProducer<GameEntityActionDataSystem>(jobHandle);
        handleEntitiesJobManager.AddReadOnlyDependency(jobHandle);

        itemManagerJobManager.AddReadOnlyDependency(jobHandle);

        jobHandle = JobHandle.CombineDependencies(jobHandle, applyHitersJobHandle);

        spawnCommandsJobManager.readWriteJobHandle = jobHandle;

        Convert convert;
        convert.damageInputs = __damages;
        convert.healthInputs = __healthBuffs;
        convert.torpidityInputs = __torpidityBuffs;
        convert.damageOutputs = __damageOuputs.UpdateAsRef(ref state);
        convert.healthOutputs = __healthOutputs.UpdateAsRef(ref state);
        convert.torpidityOutputs = __torpidityOutputs.UpdateAsRef(ref state);

        state.Dependency = JobHandle.CombineDependencies(convert.ScheduleByRef(applyDamagersJobHandle), jobHandle);
    }
}

[BurstCompile, UpdateInGroup(typeof(TimeSystemGroup)), UpdateAfter(typeof(GameSyncSystemGroup))]
public partial struct GameEntityActionDataPickSystem : ISystem
{
    [BurstCompile]
    private struct Pick : IJobParallelForDefer
    {
        [ReadOnly]
        public NativeArray<Entity> entities;

        [NativeDisableParallelForRestriction] 
        public ComponentLookup<GameItemRoot> itemRoots;

        [NativeDisableParallelForRestriction] 
        public ComponentLookup<GameNodeStatus> nodeStates;

        public void Execute(int index)
        {
            Entity entity = entities[index];
            if (itemRoots.HasComponent(entity))
            {
                GameItemRoot itemRoot;
                itemRoot.handle = GameItemHandle.Empty;
                itemRoots[entity] = itemRoot;
                itemRoots.SetComponentEnabled(entity, false);
            }
            
            if (nodeStates.HasComponent(entity))
            {
                GameNodeStatus status;
                status.value = (int)GameItemStatus.Picked;
                nodeStates[entity] = status;
            }
        }
    }

    [BurstCompile]
    private struct Clear : IJob
    {
        public NativeList<Entity> entities;

        public void Execute()
        {
            entities.Clear();
        }
    }

    public static readonly int InnerloopBatchCount = 4;
    
    public SharedList<Entity> entities;

    private ComponentLookup<GameItemRoot> __itemRoots;
    private ComponentLookup<GameNodeStatus> __nodeStates;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        entities = new SharedList<Entity>(Allocator.Persistent);

        __itemRoots = state.GetComponentLookup<GameItemRoot>();
        __nodeStates = state.GetComponentLookup<GameNodeStatus>();
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        entities.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        NativeList<Entity> entities = this.entities.writer;
        Pick pick;
        pick.entities = entities.AsDeferredJobArray();
        pick.itemRoots = __itemRoots.UpdateAsRef(ref state);
        pick.nodeStates = __nodeStates.UpdateAsRef(ref state);

        ref var entitiesJobManager = ref this.entities.lookupJobManager;
        var jobHandle = pick.ScheduleByRef(
            entities, 
            InnerloopBatchCount, 
            JobHandle.CombineDependencies(entitiesJobManager.readWriteJobHandle, state.Dependency));

        Clear clear;
        clear.entities = entities;
        jobHandle = clear.ScheduleByRef(jobHandle);
        entitiesJobManager.readWriteJobHandle = jobHandle;

        state.Dependency = jobHandle;
    }
}