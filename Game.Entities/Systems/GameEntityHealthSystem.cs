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

        [NativeDisableParallelForRestriction]
        public ComponentLookup<GameEntityHealthDamage> healthDamages;
        
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
                                var entity = entityArray[index];
                                var healthDamage = healthDamages[entity];
                                healthDamage.value += math.pow(hit, instance.speedToHitPower) * instance.speedToHitScale * healthes[index].max;
                                healthDamage.time = time;
                                healthDamage.entity = entity;

                                healthDamages[entity] = healthDamage;
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
        
        [NativeDisableParallelForRestriction]
        public ComponentLookup<GameEntityHealthDamage> healthDamages;

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
            updateHealthes.healthDamages = healthDamages;

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
        updateHealth.healthDamages = state.GetComponentLookup<GameEntityHealthDamage>();

        state.Dependency = updateHealth.ScheduleParallel(__group, state.Dependency);
    }
}

[BurstCompile, UpdateInGroup(typeof(TimeSystemGroup)), UpdateAfter(typeof(GameSyncSystemGroup))]
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
        public NativeArray<GameEntityHealthDamage> damages;

        public BufferAccessor<GameEntityHealthBuff> buffs;

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

            var damage = damages[index];
            value -= damage.value;

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
        public EntityTypeHandle entityArrayType;
        [ReadOnly]
        public ComponentTypeHandle<GameNodeStatus> statusType;
        [ReadOnly]
        public ComponentTypeHandle<GameEntityHealthData> instanceType;
        [ReadOnly]
        public ComponentTypeHandle<GameEntityHealth> resultType;
        [ReadOnly]
        public ComponentTypeHandle<GameEntityHealthDamage> damageType;

        public BufferTypeHandle<GameEntityHealthBuff> buffType;

        [NativeDisableContainerSafetyRestriction]
        public ComponentLookup<GameNodeStatus> states;

        [NativeDisableContainerSafetyRestriction]
        public ComponentLookup<GameEntityHealth> results;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            UpdateHealthes updateHealthes;
            updateHealthes.deltaTime = deltaTime;
            updateHealthes.entityArray = chunk.GetNativeArray(entityArrayType);
            updateHealthes.statusInputs = chunk.GetNativeArray(ref statusType);
            updateHealthes.instances = chunk.GetNativeArray(ref instanceType);
            updateHealthes.inputs = chunk.GetNativeArray(ref resultType);
            updateHealthes.damages = chunk.GetNativeArray(ref damageType);
            updateHealthes.buffs = chunk.GetBufferAccessor(ref buffType);
            updateHealthes.statusOutputs = states;
            updateHealthes.outputs = results;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                updateHealthes.Execute(i);
        }
    }
        
    private EntityQuery __group;

    public void OnCreate(ref SystemState state)
    {
        __group = state.GetEntityQuery(
            ComponentType.ReadOnly<GameEntityHealthData>(),
            ComponentType.ReadOnly<GameNodeStatus>(),
            ComponentType.ReadOnly<GameEntityHealth>(),
            ComponentType.ReadOnly<GameEntityHealthDamage>(), 
            ComponentType.Exclude<Disabled>());
    }

    public void OnDestroy(ref SystemState state)
    {

    }
    
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        UpdateHealthesEx updateHealth;
        updateHealth.deltaTime = state.WorldUnmanaged.Time.DeltaTime;
        updateHealth.entityArrayType = state.GetEntityTypeHandle();
        updateHealth.statusType = state.GetComponentTypeHandle<GameNodeStatus>(true);
        updateHealth.instanceType = state.GetComponentTypeHandle<GameEntityHealthData>(true);
        updateHealth.resultType = state.GetComponentTypeHandle<GameEntityHealth>(true);
        updateHealth.damageType = state.GetComponentTypeHandle<GameEntityHealthDamage>(true);
        updateHealth.buffType = state.GetBufferTypeHandle<GameEntityHealthBuff>();
        updateHealth.states = state.GetComponentLookup<GameNodeStatus>();
        updateHealth.results = state.GetComponentLookup<GameEntityHealth>();

        state.Dependency = updateHealth.ScheduleParallel(__group, state.Dependency);
    }
}

[BurstCompile, UpdateInGroup(typeof(TimeSystemGroup)), UpdateBefore(typeof(GameSyncSystemGroup))]
public partial struct GameEntityHealthClearSystem : ISystem
{
    private struct Clear
    {
        [ReadOnly]
        public NativeArray<Entity> entityArray;

        [ReadOnly]
        public NativeArray<GameEntityHealthDamage> inputs;

        [NativeDisableContainerSafetyRestriction]
        public ComponentLookup<GameEntityHealthDamage> outputs;

        public void Execute(int index)
        {
            var instance = inputs[index];
            if (math.abs(instance.value) > math.FLT_MIN_NORMAL)
            {
                instance.value = 0.0f;

                outputs[entityArray[index]] = instance;
            }
        }
    }
    
    [BurstCompile]
    private struct ClearEx : IJobChunk
    {
        [ReadOnly]
        public EntityTypeHandle entityType;

        [ReadOnly]
        public ComponentTypeHandle<GameEntityHealthDamage> damageType;
        
        [NativeDisableContainerSafetyRestriction]
        public ComponentLookup<GameEntityHealthDamage> damages;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Clear clear;
            clear.entityArray = chunk.GetNativeArray(entityType);
            clear.inputs = chunk.GetNativeArray(ref damageType);
            clear.outputs = damages;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                clear.Execute(i);
        }
    }

    private EntityQuery __group;

    public void OnCreate(ref SystemState state)
    {
        __group = state.GetEntityQuery(ComponentType.ReadOnly<GameEntityHealthDamage>());
        __group.SetChangedVersionFilter(typeof(GameEntityHealthDamage));
    }

    public void OnDestroy(ref SystemState state)
    {

    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        ClearEx clear;
        clear.entityType = state.GetEntityTypeHandle();
        clear.damageType = state.GetComponentTypeHandle<GameEntityHealthDamage>(true);
        clear.damages = state.GetComponentLookup<GameEntityHealthDamage>();

        state.Dependency = clear.ScheduleParallel(__group, state.Dependency);
    }
}