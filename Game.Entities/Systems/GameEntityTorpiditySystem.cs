using Unity.Jobs;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using ZG;

[BurstCompile, UpdateInGroup(typeof(TimeSystemGroup)), UpdateBefore(typeof(GameEntityHealthSystem))]
public partial struct GameEntityTorpiditySystem : ISystem
{
    private struct UpdateTorpidity
    {
        public float speedScaleInterval;
        public float deltaTime;

        [ReadOnly]
        public NativeArray<Entity> entityArray;
        [ReadOnly]
        public NativeArray<GameNodeStatus> statusInputs;
        [ReadOnly]
        public NativeArray<GameEntityTorpidityData> instances;
        [ReadOnly]
        public NativeArray<GameEntityTorpidity> inputs;

        public NativeArray<GameEntityTorpiditySpeedScale> speedScales;

        public BufferAccessor<GameEntityTorpidityBuff> buffs;

        [NativeDisableParallelForRestriction]
        public BufferLookup<GameNodeSpeedScaleComponent> speedScaleComponents;

        [NativeDisableContainerSafetyRestriction]
        public ComponentLookup<GameNodeStatus> statusOutputs;

        [NativeDisableContainerSafetyRestriction]
        public ComponentLookup<GameEntityTorpidity> outputs;

        public void Execute(int index)
        {
            var status = statusInputs[index];
            if ((status.value & (int)GameEntityStatus.Mask) == (int)GameEntityStatus.Dead)
                return;

            float buffValue = 0.0f;
            var buffs = this.buffs[index];
            var length = buffs.Length;
            if (length > 0)
            {
                float elpasedTime;
                GameEntityTorpidityBuff buff;
                for (int i = length - 1; i >= 0; --i)
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

            var instance = instances[index];
            //instance.buff *= deltaTime;

            var input = inputs[index];

            float value = input.value;
            int source = (int)math.round(value);

            buffValue += value;

            bool isKnockedOut = (status.value & (int)GameEntityStatus.Mask) == (int)GameEntityStatus.KnockedOut;
            value = buffValue + (isKnockedOut ? instance.buffOnKnockedOut : instance.buffOnNormal) * deltaTime;

            int destination = (int)math.clamp(math.round(value), 0, instance.max);

            Entity entity = entityArray[index];
            if (isKnockedOut)
            {
                if (source >= instance.min || destination >= instance.min)
                {
                    status.value = 0;

                    statusOutputs[entity] = status;

                    if (buffValue > instance.min)
                        value += (instance.buffOnNormal - instance.buffOnKnockedOut) * deltaTime;
                    else if (instance.buffOnKnockedOut > math.FLT_MIN_NORMAL)
                        value = (value - instance.min) / instance.buffOnKnockedOut * instance.buffOnNormal + instance.min;
                }
            }
            else if (instance.max > 0 && (/*source < 1 || */destination < 1))
            {
                status.value = (int)GameEntityStatus.KnockedOut;

                statusOutputs[entity] = status;

                if (buffValue < 0.0f)
                    value += (instance.buffOnKnockedOut - instance.buffOnNormal) * deltaTime;
                else if (instance.buffOnNormal > math.FLT_MIN_NORMAL)
                    value *= instance.buffOnKnockedOut / instance.buffOnNormal;
            }

            value = math.clamp(value, 0.0f, instance.max);

            bool isChanged = destination != source || math.abs(input.value - value) > math.FLT_MIN_NORMAL;
            if (instance.min > 0 && instance.min < instance.max && index < speedScales.Length && this.speedScaleComponents.HasBuffer(entity))
            {
                half newSpeedScale = (half)math.clamp(math.smoothstep(0.0f, instance.min, value), speedScaleInterval, 1.0f), 
                    oldSpeedScale = speedScales[index].value;
                if (math.abs(newSpeedScale - oldSpeedScale) > speedScaleInterval)
                {
                    GameEntityTorpiditySpeedScale torpiditySpeedScale;
                    torpiditySpeedScale.value = newSpeedScale;
                    speedScales[index] = torpiditySpeedScale;

                    var speedScaleComponents = this.speedScaleComponents[entity];
                    GameNodeSpeedScale.Set(newSpeedScale, oldSpeedScale, ref speedScaleComponents);

                    isChanged = true;
                }
            }

            if (isChanged)
            {
                input.value = value;

                outputs[entity] = input;
            }
        }
    }

    [BurstCompile]
    private struct UpdateTorpidityEx : IJobChunk
    {
        public float speedScaleInterval;
        public float deltaTime;

        [ReadOnly]
        public EntityTypeHandle entityArrayType;
        [ReadOnly]
        public ComponentTypeHandle<GameNodeStatus> statusType;
        [ReadOnly]
        public ComponentTypeHandle<GameEntityTorpidityData> instanceType;
        [ReadOnly]
        public ComponentTypeHandle<GameEntityTorpidity> resultType;

        public ComponentTypeHandle<GameEntityTorpiditySpeedScale> torpiditySpeedScaleType;

        public BufferTypeHandle<GameEntityTorpidityBuff> buffType;

        [NativeDisableContainerSafetyRestriction]
        public BufferLookup<GameNodeSpeedScaleComponent> speedScaleComponents;

        [NativeDisableContainerSafetyRestriction]
        public ComponentLookup<GameNodeStatus> states;

        [NativeDisableContainerSafetyRestriction]
        public ComponentLookup<GameEntityTorpidity> results;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            UpdateTorpidity updateTorpidity;
            updateTorpidity.speedScaleInterval = speedScaleInterval;
            updateTorpidity.deltaTime = deltaTime;
            updateTorpidity.entityArray = chunk.GetNativeArray(entityArrayType);
            updateTorpidity.statusInputs = chunk.GetNativeArray(ref statusType);
            updateTorpidity.instances = chunk.GetNativeArray(ref instanceType);
            updateTorpidity.inputs = chunk.GetNativeArray(ref resultType);
            updateTorpidity.speedScales = chunk.GetNativeArray(ref torpiditySpeedScaleType);
            updateTorpidity.buffs = chunk.GetBufferAccessor(ref buffType);
            updateTorpidity.speedScaleComponents = speedScaleComponents;
            updateTorpidity.statusOutputs = states;
            updateTorpidity.outputs = results;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                updateTorpidity.Execute(i);
        }
    }

    public static readonly float SpeedScaleInterval = 0.1f;

    private EntityQuery __group;

    public void OnCreate(ref SystemState state)
    {
        __group = state.GetEntityQuery(
            ComponentType.ReadOnly<GameNodeStatus>(),
            ComponentType.ReadOnly<GameEntityTorpidityData>(),
            ComponentType.ReadOnly<GameEntityTorpidity>(),
            ComponentType.ReadWrite<GameEntityTorpidityBuff>(),
            ComponentType.Exclude<Disabled>());
    }

    public void OnDestroy(ref SystemState state)
    {

    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        UpdateTorpidityEx updateTorpidity;
        updateTorpidity.speedScaleInterval = SpeedScaleInterval;
        updateTorpidity.deltaTime = state.WorldUnmanaged.Time.DeltaTime;
        updateTorpidity.entityArrayType = state.GetEntityTypeHandle();
        updateTorpidity.statusType = state.GetComponentTypeHandle<GameNodeStatus>(true);
        updateTorpidity.instanceType = state.GetComponentTypeHandle<GameEntityTorpidityData>(true);
        updateTorpidity.resultType = state.GetComponentTypeHandle<GameEntityTorpidity>(true);
        updateTorpidity.torpiditySpeedScaleType = state.GetComponentTypeHandle<GameEntityTorpiditySpeedScale>();
        updateTorpidity.buffType = state.GetBufferTypeHandle<GameEntityTorpidityBuff>();
        updateTorpidity.speedScaleComponents = state.GetBufferLookup<GameNodeSpeedScaleComponent>();
        updateTorpidity.states = state.GetComponentLookup<GameNodeStatus>();
        updateTorpidity.results = state.GetComponentLookup<GameEntityTorpidity>();

        state.Dependency = updateTorpidity.ScheduleParallel(__group, state.Dependency);
    }
}
