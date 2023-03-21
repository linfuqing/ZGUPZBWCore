using System;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Transforms;
using ZG;

public interface IGameEffectHandler<T> where T : struct, IGameEffect<T>
{
    bool Change(
        int index,
        int areaIndex,
        in T source,
        ref T destination,
        in DynamicBuffer<PhysicsShapeTriggerEventRevicer> revicers);
}

public interface IGameEffectFactory<TEffect, THandler>
    where TEffect : struct, IGameEffect<TEffect>
    where THandler : struct, IGameEffectHandler<TEffect>
{
    THandler Create(in ArchetypeChunk chunk);
}

[Serializable]
public struct GameEffectInternalSurface
{
    public int octaveCount;
    public float frequency;
    public float persistence;

    public float2 offset;
    public float2 scale;

    public float Get(float x, float y)
    {
        x *= scale.x;
        y *= scale.y;

        x += offset.x;
        y += offset.y;

        if (octaveCount > 0)
        {
            float result = 0.0f, amplitude = 1.0f, frequency = 1.0f;
            for (int i = 0; i < octaveCount; ++i)
            {
                if (i > 0)
                {
                    frequency *= this.frequency;
                    amplitude *= persistence;
                }

                result += noise.cnoise(new float2(x * frequency, y * frequency)) * amplitude;
            }

            return ((result / octaveCount) + 1.0f) * 0.5f;
        }

        return noise.srnoise(new float2(x, y)) * 0.5f + 0.5f;
    }
}

[Serializable]
public struct GameEffectInternalCondition
{
    public int mapIndex;

    public float min;
    public float max;
}

[Serializable]
public struct GameEffectInternalHeight
{
    public int conditionIndex;
    public int conditionCount;

    public float scale;

    public float Get(
        float x,
        float y,
        in NativeArray<GameEffectInternalSurface> surfaces,
        in NativeArray<GameEffectInternalCondition> conditions)
    {
        float result = 0.0f, temp;
        GameEffectInternalCondition condition;
        for (int i = 0; i < conditionCount; ++i)
        {
            condition = conditions[i + conditionIndex];

            temp = surfaces[condition.mapIndex].Get(x, y);

            if (temp < condition.min || temp > condition.max)
                continue;

            result += temp * scale;
        }

        return result;
    }
}

[Serializable]
public struct GameEffectInternalValue
{
    public int conditionIndex;
    public int conditionCount;

    public int fromHeightIndex;
    public int fromHeightCount;

    public int toHeightIndex;
    public int toHeightCount;

    public float fromHeight;
    public float toHeight;

    public bool Check(
        in float3 position,
        in NativeArray<GameEffectInternalSurface> surfaces,
        in NativeArray<GameEffectInternalCondition> conditions,
        in NativeArray<GameEffectInternalHeight> heights)
    {
        int i;
        float temp;
        GameEffectInternalCondition condition;
        for (i = 0; i < conditionCount; ++i)
        {
            condition = conditions[i + conditionIndex];
            temp = surfaces[condition.mapIndex].Get(position.x, position.z);
            if (temp < condition.min || temp > condition.max)
                break;
        }

        if (i < conditionCount)
            return false;

        if (fromHeightCount > 0 || fromHeight > math.FLT_MIN_NORMAL)
        {
            temp = fromHeight;
            for (i = 0; i < fromHeightCount; ++i)
                temp += heights[i + fromHeightIndex].Get(position.x, position.z, surfaces, conditions);

            if (temp > position.y)
                return false;
        }

        if (toHeightCount > 0 || toHeight > math.FLT_MIN_NORMAL)
        {
            temp = toHeight;
            for (i = 0; i < toHeightCount; ++i)
                temp += heights[i + toHeightIndex].Get(position.x, position.z, surfaces, conditions);

            if (temp < position.y)
                return false;
        }

        return true;
    }
}

[BurstCompile]
public struct GameEffectApply<TEffect, THandler, TFactory> : IJobChunk
        where TEffect : unmanaged, IGameEffect<TEffect>
        where THandler : struct, IGameEffectHandler<TEffect>
        where TFactory : struct, IGameEffectFactory<TEffect, THandler>
{
    private struct Executor
    {
        [ReadOnly]
        public ComponentLookup<GameEffectAreaOverride> areasOverride;
        [ReadOnly]
        public BufferAccessor<PhysicsShapeTriggerEventRevicer> revicers;
        [ReadOnly]
        public NativeArray<GameEffectInternalSurface> surfaces;
        [ReadOnly]
        public NativeArray<GameEffectInternalCondition> conditions;
        [ReadOnly]
        public NativeArray<GameEffectInternalHeight> heights;
        [ReadOnly]
        public NativeArray<GameEffectInternalValue> effects;
        [ReadOnly]
        public NativeArray<TEffect> values;
        [ReadOnly]
        public NativeArray<Translation> translations;
        [ReadOnly]
        public NativeArray<GameEffectData<TEffect>> instances;

        public NativeArray<GameEffectResult<TEffect>> results;

        public NativeArray<GameEffectArea> areas;

        public THandler handler;

        public void Execute(int index)
        {
            var revicers = this.revicers[index];
            Entity revicer;
            int areaIndex = -1, length = revicers.Length, i;
            for (i = 0; i < length; ++i)
            {
                revicer = revicers[i].entity;
                if (!areasOverride.HasComponent(revicer))
                    continue;

                areaIndex = areasOverride[revicer].index;

                break;
            }

            if (areaIndex == -1)
            {
                int numEffects = math.min(effects.Length, values.Length);
                float3 position = translations[index].Value;
                for (i = 0; i < numEffects; ++i)
                {
                    /*if (i == numEffects - 1)
                        numEffects = i + 1;
                    */
                    if (!effects[i].Check(position, surfaces, conditions, heights))
                        continue;

                    areaIndex = i;

                    break;
                }
            }

            var value = instances[index].value;
            if (areaIndex != -1)
                value.Add(values[areaIndex]);

            GameEffectArea area;
            area.index = areaIndex;
            areas[index] = area;

            if (handler.Change(index, areaIndex, results[index].value, ref value, revicers))
            {
                GameEffectResult<TEffect> result;
                result.value = value;
                results[index] = result;
            }
        }
    }

    [ReadOnly]
    public ComponentLookup<GameEffectAreaOverride> areasOverride;
    [ReadOnly]
    public NativeArray<GameEffectInternalSurface> surfaces;
    [ReadOnly]
    public NativeArray<GameEffectInternalCondition> conditions;
    [ReadOnly]
    public NativeArray<GameEffectInternalHeight> heights;
    [ReadOnly]
    public NativeArray<GameEffectInternalValue> effects;
    [ReadOnly]
    public NativeArray<TEffect> values;
    [ReadOnly]
    public BufferTypeHandle<PhysicsShapeTriggerEventRevicer> revicerType;
    [ReadOnly]
    public ComponentTypeHandle<Translation> translationType;
    [ReadOnly]
    public ComponentTypeHandle<GameEffectData<TEffect>> instanceType;

    public ComponentTypeHandle<GameEffectResult<TEffect>> resultType;

    public ComponentTypeHandle<GameEffectArea> areaType;

    public TFactory factory;

    public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
    {
        Executor executor;
        executor.areasOverride = areasOverride;
        executor.surfaces = surfaces;
        executor.conditions = conditions;
        executor.heights = heights;
        executor.effects = effects;
        executor.values = values;
        executor.revicers = chunk.GetBufferAccessor(ref revicerType);
        executor.translations = chunk.GetNativeArray(ref translationType);
        executor.instances = chunk.GetNativeArray(ref instanceType);
        executor.results = chunk.GetNativeArray(ref resultType);
        executor.areas = chunk.GetNativeArray(ref areaType);
        executor.handler = factory.Create(chunk);

        var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
        while (iterator.NextEntityIndex(out int i))
            executor.Execute(i);
    }
}
