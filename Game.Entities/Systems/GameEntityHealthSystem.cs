using Unity.Jobs;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using ZG;
using Unity.Transforms;

[BurstCompile, UpdateInGroup(typeof(GameNodeCharacterSystemGroup), OrderLast = true)/*, UpdateAfter(typeof(GameNodeActorSystem))*/]
public partial struct GameEntityHealthActorSystem : ISystem
{
    private struct UpdateHealthes
    {
        public float maxDelta;
        public double time;

        [ReadOnly]
        public NativeArray<Entity> entityArray;

        [ReadOnly]
        public NativeArray<Translation> translations;

        [ReadOnly]
        public NativeArray<GameNodeStatus> states;

        [ReadOnly]
        public NativeArray<GameNodeActorStatus> actorStates;

        [ReadOnly]
        public NativeArray<GameEntityHealthData> healthes;

        [ReadOnly]
        public NativeArray<GameEntityHealthActorData> instances;
        
        public NativeArray<GameEntityHealthActorInfo> infos;

        //[NativeDisableParallelForRestriction]
        public BufferAccessor<GameEntityHealthDamage> healthDamages;
        
        public void Execute(int index)
        {
            var info = infos[index];

            float height = translations[index].Value.y, velocity = 0.0f, deltaTime = (float)(time - info.time);
            if (deltaTime > math.FLT_MIN_NORMAL && deltaTime < maxDelta)
            {
                var instance = instances[index];
                if (instance.minSpeedToHit < instance.maxSpeedToHit)
                {
                    switch (actorStates[index].value)
                    {
                        case GameNodeActorStatus.Status.Normal:
                            float hit = info.oldVelocity - velocity;
                            hit = math.smoothstep(instance.minSpeedToHit, instance.maxSpeedToHit, hit);
                            if (hit > math.FLT_MIN_NORMAL)
                            {
                                GameEntityHealthDamage healthDamage;
                                healthDamage.value = math.pow(hit, instance.speedToHitPower) * instance.speedToHitScale * healthes[index].max;
                                healthDamage.time = time;
                                healthDamage.entity = entityArray[index];
                                healthDamages[index].Add(healthDamage);
                            }
                            break;
                        case GameNodeActorStatus.Status.Fall:
                            if((states[index].value & GameNodeActorStatus.NODE_STATUS_ACT) == GameNodeActorStatus.NODE_STATUS_ACT)
                                velocity = math.max((info.oldHeight - height) / deltaTime, 0.0f);
                            break;
                    }
                }
            }

            info.oldHeight = height;
            info.oldVelocity = velocity;
            info.time = time;
            infos[index] = info;
        }
    }

    [BurstCompile]
    private struct UpdateHealthesEx : IJobChunk
    {
        public float maxDelta;
        public double time;

        [ReadOnly]
        public EntityTypeHandle entityType;

        [ReadOnly]
        public ComponentTypeHandle<Translation> translationType;

        [ReadOnly]
        public ComponentTypeHandle<GameNodeStatus> statusType;

        [ReadOnly]
        public ComponentTypeHandle<GameNodeActorStatus> actorStatusType;

        [ReadOnly]
        public ComponentTypeHandle<GameEntityHealthData> healthType;

        [ReadOnly]
        public ComponentTypeHandle<GameEntityHealthActorData> instanceType;

        public ComponentTypeHandle<GameEntityHealthActorInfo> infoType;
        
        public BufferTypeHandle<GameEntityHealthDamage> healthDamageType;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            UpdateHealthes updateHealthes;
            updateHealthes.maxDelta = maxDelta;
            updateHealthes.time = time;
            updateHealthes.entityArray = chunk.GetNativeArray(entityType);
            updateHealthes.states = chunk.GetNativeArray(ref statusType);
            updateHealthes.actorStates = chunk.GetNativeArray(ref actorStatusType);
            updateHealthes.translations = chunk.GetNativeArray(ref translationType);
            updateHealthes.healthes = chunk.GetNativeArray(ref healthType);
            updateHealthes.instances = chunk.GetNativeArray(ref instanceType);
            updateHealthes.infos = chunk.GetNativeArray(ref infoType);
            updateHealthes.healthDamages = chunk.GetBufferAccessor(ref healthDamageType);

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                updateHealthes.Execute(i);
        }
    }

    private EntityQuery __group;

    public void OnCreate(ref SystemState state)
    {
        __group = state.GetEntityQuery(
                ComponentType.ReadOnly<Translation>(),
                ComponentType.ReadOnly<GameNodeStatus>(),
                ComponentType.ReadOnly<GameNodeActorStatus>(), 
                ComponentType.ReadOnly<GameEntityHealthActorData>(),
                ComponentType.ReadWrite<GameEntityHealthActorInfo>()/*,
                ComponentType.Exclude<GameNodeParent>()*/);

        state.RequireForUpdate(__group);
    }

    public void OnDestroy(ref SystemState state)
    {

    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        ref readonly var Time = ref state.WorldUnmanaged.Time;

        UpdateHealthesEx updateHealth;
        updateHealth.maxDelta = Time.DeltaTime * 2.0f;
        updateHealth.time = Time.ElapsedTime;
        updateHealth.entityType = state.GetEntityTypeHandle();
        updateHealth.translationType = state.GetComponentTypeHandle<Translation>(true);
        updateHealth.statusType = state.GetComponentTypeHandle<GameNodeStatus>(true);
        updateHealth.actorStatusType = state.GetComponentTypeHandle<GameNodeActorStatus>(true);
        updateHealth.healthType = state.GetComponentTypeHandle<GameEntityHealthData>(true);
        updateHealth.instanceType = state.GetComponentTypeHandle<GameEntityHealthActorData>(true);
        updateHealth.infoType = state.GetComponentTypeHandle<GameEntityHealthActorInfo>();
        updateHealth.healthDamageType = state.GetBufferTypeHandle<GameEntityHealthDamage>();

        state.Dependency = updateHealth.ScheduleParallel(__group, state.Dependency);
    }
}

[BurstCompile, UpdateInGroup(typeof(TimeSystemGroup)), UpdateBefore(typeof(GameEntityActionDataPickSystem)), UpdateAfter(typeof(GameSyncSystemGroup))]
public partial struct GameEntityHealthSystem : ISystem
{
    private struct UpdateHealthes
    {
        public float deltaTime;

        [ReadOnly]
        public NativeArray<Entity> entityArray;
        [ReadOnly]
        public NativeArray<GameNodeStatus> statusInputs;
        [ReadOnly]
        public NativeArray<GameEntityHealthData> instances;
        [ReadOnly]
        public NativeArray<GameEntityHealth> inputs;
        [ReadOnly]
        public BufferAccessor<GameEntityHealthDamage> damages;

        public BufferAccessor<GameEntityHealthBuff> buffs;

        public NativeArray<GameEntityHealthDamageCount> damageCounts;

        [NativeDisableContainerSafetyRestriction]
        public ComponentLookup<GameNodeStatus> statusOutputs;

        [NativeDisableContainerSafetyRestriction]
        public ComponentLookup<GameEntityHealth> outputs;

        public void Execute(int index)
        {
            GameNodeStatus status = statusInputs[index];
            if ((status.value & (int)GameEntityStatus.Mask) == (int)GameEntityStatus.Dead)
                return;

            float buffValue = 0.0f;
            if (index < this.buffs.Length)
            {
                DynamicBuffer<GameEntityHealthBuff> buffs = this.buffs[index];
                int length = buffs.Length;
                if (length > 0)
                {
                    float elpasedTime;
                    GameEntityHealthBuff buff;
                    for (int i = 0; i < length; ++i)
                    {
                        buff = buffs[i];
                        if (buff.duration > deltaTime)
                        {
                            buff.duration -= deltaTime;

                            buffs[i] = buff;

                            elpasedTime = deltaTime;
                        }
                        else if(buff.duration > math.FLT_MIN_NORMAL)
                        {
                            buffs.RemoveAt(i);

                            --i;

                            --length;

                            elpasedTime = buff.duration;
                        }
                        else
                            elpasedTime = deltaTime;

                        buffValue += buff.value * elpasedTime;
                    }
                }
            }

            Entity entity = entityArray[index];
            GameEntityHealthData instance = instances[index];
            GameEntityHealth result = inputs[index];
            float value = result.value;
            int source = (int)math.round(value);

            var damages = this.damages[index];
            var damageCount = damageCounts[index];
            int numDamages = damages.Length;
            while(numDamages > damageCount.value)
                value -= damages[damageCount.value++].value;

            damageCounts[index] = damageCount;

            value = math.clamp(value + buffValue, 0.0f, instance.max);

            int destination = (int)math.round(value);

            /*if (status.value == (int)GameEntityStatus.Dead)
            {
                if (result.value > 0.0f)
                {
                    status.value = 0;

                    states[index] = status;

                    damage.time = time;
                    damage.entity = Entity.Null;
                }
            }
            else */if (source < 1 || destination < 1)
            {
                //UnityEngine.Debug.LogError($"Heath {entity.Index} : {buffValue} : {damage.value}");

                status.value = (int)GameEntityStatus.Dead;

                statusOutputs[entity] = status;
            }

            if (math.abs(result.value - value) > math.FLT_MIN_NORMAL)
            {
                result.value = value;
                outputs[entity] = result;
            }

            /*if (math.abs(damage.value) > math.FLT_MIN_NORMAL)
            {
                damage.value = 0.0f;
                damages[index] = damage;
            }*/
        }
    }
    
    [BurstCompile]
    private struct UpdateHealthesEx : IJobChunk
    {
        public float deltaTime;

        [ReadOnly]
        public EntityTypeHandle entityType;
        [ReadOnly]
        public ComponentTypeHandle<GameNodeStatus> statusType;
        [ReadOnly]
        public ComponentTypeHandle<GameEntityHealthData> instanceType;
        [ReadOnly]
        public ComponentTypeHandle<GameEntityHealth> resultType;
        [ReadOnly]
        public BufferTypeHandle<GameEntityHealthDamage> damageType;

        public BufferTypeHandle<GameEntityHealthBuff> buffType;

        public ComponentTypeHandle<GameEntityHealthDamageCount> damageCountType;

        [NativeDisableContainerSafetyRestriction]
        public ComponentLookup<GameNodeStatus> states;

        [NativeDisableContainerSafetyRestriction]
        public ComponentLookup<GameEntityHealth> results;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            UpdateHealthes updateHealthes;
            updateHealthes.deltaTime = deltaTime;
            updateHealthes.entityArray = chunk.GetNativeArray(entityType);
            updateHealthes.statusInputs = chunk.GetNativeArray(ref statusType);
            updateHealthes.instances = chunk.GetNativeArray(ref instanceType);
            updateHealthes.inputs = chunk.GetNativeArray(ref resultType);
            updateHealthes.damages = chunk.GetBufferAccessor(ref damageType);
            updateHealthes.buffs = chunk.GetBufferAccessor(ref buffType);
            updateHealthes.damageCounts = chunk.GetNativeArray(ref damageCountType);
            updateHealthes.statusOutputs = states;
            updateHealthes.outputs = results;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                updateHealthes.Execute(i);
        }
    }
        
    private EntityQuery __group;

    private EntityTypeHandle __entityType;
    private ComponentTypeHandle<GameNodeStatus> __statusType;
    private ComponentTypeHandle<GameEntityHealthData> __instanceType;
    private ComponentTypeHandle<GameEntityHealth> __resultType;
    private BufferTypeHandle<GameEntityHealthDamage> __damageType;
    private BufferTypeHandle<GameEntityHealthBuff> __buffType;
    private ComponentTypeHandle<GameEntityHealthDamageCount> __damageCountType;
    private ComponentLookup<GameNodeStatus> __states;
    private ComponentLookup<GameEntityHealth> __results;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        using(var builder = new EntityQueryBuilder(Allocator.Temp))
            __group = builder
                .WithAll<GameNodeStatus, GameEntityHealthData, GameEntityHealth, GameEntityHealthDamage>()
                .WithAllRW<GameEntityHealthDamageCount>()
                .Build(ref state);

        __entityType = state.GetEntityTypeHandle();
        __statusType = state.GetComponentTypeHandle<GameNodeStatus>(true);
        __instanceType = state.GetComponentTypeHandle<GameEntityHealthData>(true);
        __resultType = state.GetComponentTypeHandle<GameEntityHealth>(true);
        __damageType = state.GetBufferTypeHandle<GameEntityHealthDamage>(true);
        __buffType = state.GetBufferTypeHandle<GameEntityHealthBuff>();
        __damageCountType = state.GetComponentTypeHandle<GameEntityHealthDamageCount>();
        __states = state.GetComponentLookup<GameNodeStatus>();
        __results = state.GetComponentLookup<GameEntityHealth>();
    }

    public void OnDestroy(ref SystemState state)
    {

    }
    
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        UpdateHealthesEx updateHealth;
        updateHealth.deltaTime = state.WorldUnmanaged.Time.DeltaTime;
        updateHealth.entityType = __entityType.UpdateAsRef(ref state);
        updateHealth.statusType = __statusType.UpdateAsRef(ref state);
        updateHealth.instanceType = __instanceType.UpdateAsRef(ref state);
        updateHealth.resultType = __resultType.UpdateAsRef(ref state);
        updateHealth.damageType = __damageType.UpdateAsRef(ref state);
        updateHealth.buffType = __buffType.UpdateAsRef(ref state);
        updateHealth.damageCountType = __damageCountType.UpdateAsRef(ref state);
        updateHealth.states = __states.UpdateAsRef(ref state);
        updateHealth.results = __results.UpdateAsRef(ref state);

        state.Dependency = updateHealth.ScheduleParallelByRef(__group, state.Dependency);
    }
}

[BurstCompile, UpdateInGroup(typeof(GameStatusSystemGroup))]
public partial struct GameEntityHealthStatusSystem : ISystem
{
    private struct UpdateStates
    {
        [ReadOnly]
        public NativeArray<GameNodeStatus> states;

        public NativeArray<GameEntityHealthDamageCount> damageCounts;
        public BufferAccessor<GameEntityHealthDamage> damages;
        
        public BufferAccessor<GameEntityHealthBuff> buffs;

        public void Execute(int index)
        {
            if ((states[index].value & GameNodeStatus.OVER) != GameNodeStatus.OVER)
                return;

            damages[index].Clear();
            damageCounts[index] = default;

            if (index < this.buffs.Length)
            {
                var buffs = this.buffs[index];
                int numBuffs = buffs.Length;
                for (int i = 0; i < numBuffs; ++i)
                {
                    if (buffs[i].duration > math.FLT_MIN_NORMAL)
                    {
                        buffs.RemoveAtSwapBack(i--);

                        --numBuffs;
                    }
                }
            }
        }
    }

    [BurstCompile]
    private struct UpdateStatesEx : IJobChunk
    {
        [ReadOnly]
        public ComponentTypeHandle<GameNodeStatus> statusType;

        public ComponentTypeHandle<GameEntityHealthDamageCount> damageCountType;
        public BufferTypeHandle<GameEntityHealthDamage> damageType;

        public BufferTypeHandle<GameEntityHealthBuff> buffType;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            UpdateStates updateStates;
            updateStates.states = chunk.GetNativeArray(ref statusType);
            updateStates.damageCounts = chunk.GetNativeArray(ref damageCountType);
            updateStates.damages = chunk.GetBufferAccessor(ref damageType);
            updateStates.buffs = chunk.GetBufferAccessor(ref buffType);

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                updateStates.Execute(i);
        }
    }

    
    private EntityQuery __group;
    private ComponentTypeHandle<GameNodeStatus> __statusType;
    private ComponentTypeHandle<GameEntityHealthDamageCount> __damageCountType;
    private BufferTypeHandle<GameEntityHealthDamage> __damageType;
    private BufferTypeHandle<GameEntityHealthBuff> __buffType;
    
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __group = builder
                .WithAllRW<GameEntityHealthDamage, GameEntityHealthDamageCount>()
                .BuildStatusSystemGroup(ref state);

        __statusType = state.GetComponentTypeHandle<GameNodeStatus>(true);
        __damageCountType = state.GetComponentTypeHandle<GameEntityHealthDamageCount>();
        __damageType = state.GetBufferTypeHandle<GameEntityHealthDamage>();
        __buffType = state.GetBufferTypeHandle<GameEntityHealthBuff>();
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        UpdateStatesEx updateStates;
        updateStates.statusType = __statusType.UpdateAsRef(ref state);
        updateStates.damageCountType = __damageCountType.UpdateAsRef(ref state);
        updateStates.damageType = __damageType.UpdateAsRef(ref state);
        updateStates.buffType = __buffType.UpdateAsRef(ref state);
        state.Dependency = updateStates.ScheduleParallelByRef(__group, state.Dependency);
    }
}