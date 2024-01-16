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
        ref T destination,
        in T source,
        in DynamicBuffer<PhysicsTriggerEvent> physicsTriggerEvents, 
        in ComponentLookup<PhysicsShapeParent> physicsShapeParents);
}

public interface IGameEffectFactory<TEffect, THandler>
    where TEffect : struct, IGameEffect<TEffect>
    where THandler : struct, IGameEffectHandler<TEffect>
{
    THandler Create(in ArchetypeChunk chunk);
}

/*[Serializable]
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
}*/

public struct GameEffectLandscapeDefinition
{
    public struct Surface
    {
        public int octaveCount;
        public float frequency;
        public float persistence;

        public float2 offset;
        public float2 scale;

        public float GetHeight(float x, float y)
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

    public struct Condition
    {
        public int mapIndex;

        public float min;
        public float max;
    }

    public struct Layer
    {
        public float scale;

        public BlobArray<int> conditionIndices;

        public float GetHeight(
            float x,
            float y,
            ref BlobArray<Surface> surfaces,
            ref BlobArray<Condition> conditions)
        {
            float result = 0.0f, temp;
            int conditionCount = conditionIndices.Length;
            for (int i = 0; i < conditionCount; ++i)
            {
                ref var condition = ref conditions[conditionIndices[i]];

                temp = surfaces[condition.mapIndex].GetHeight(x, y);

                if (temp < condition.min || temp > condition.max)
                    continue;

                result += temp * scale;
            }

            return result;
        }
    }

    public struct Area
    {
        public struct Layers
        {
            public float offset;
            public BlobArray<int> indices;
        }

        public BlobArray<int> conditionIndices;
        public Layers fromLayers;
        public Layers toLayers;

        public bool Check(
            in float3 position,
            ref BlobArray<Surface> surfaces,
            ref BlobArray<Condition> conditions,
            ref BlobArray<Layer> layers)
        {
            int i, numConditions = conditionIndices.Length;
            float temp;
            for (i = 0; i < numConditions; ++i)
            {
                ref var condition = ref conditions[conditionIndices[i]];

                temp = surfaces[condition.mapIndex].GetHeight(position.x, position.z);
                if (temp < condition.min || temp > condition.max)
                    break;
            }

            if (i < numConditions)
                return false;

            int numFromLayers = fromLayers.indices.Length;
            if (numFromLayers > 0 || fromLayers.offset > math.FLT_MIN_NORMAL)
            {
                temp = fromLayers.offset;
                for (i = 0; i < numFromLayers; ++i)
                    temp += layers[fromLayers.indices[i]].GetHeight(position.x, position.z, ref surfaces, ref conditions);

                if (temp > position.y)
                    return false;
            }

            int numToLayers = toLayers.indices.Length;
            if (numToLayers > 0 || toLayers.offset > math.FLT_MIN_NORMAL)
            {
                temp = toLayers.offset;
                for (i = 0; i < numToLayers; ++i)
                    temp += layers[toLayers.indices[i]].GetHeight(position.x, position.z, ref surfaces, ref conditions);

                if (temp < position.y)
                    return false;
            }

            return true;
        }
    }

    public BlobArray<Surface> surfaces;
    public BlobArray<Condition> conditions;
    public BlobArray<Layer> layers;
    public BlobArray<Area> areas;

    public int areaCount => areas.Length;

    public bool IsOnArea(int areaIndex, in float3 position)
    {
        return areas[areaIndex].Check(position, ref surfaces, ref conditions, ref layers);
    }
}

public struct GameEffectLandscapeData : IComponentData
{
    public BlobAssetReference<GameEffectLandscapeDefinition> definition;
}

[BurstCompile]
public struct GameEffectApply<TEffect, THandler, TFactory> : IJobChunk
        where TEffect : unmanaged, IGameEffect<TEffect>
        where THandler : struct, IGameEffectHandler<TEffect>
        where TFactory : struct, IGameEffectFactory<TEffect, THandler>
{
    private struct Executor
    {
        public BlobAssetReference<GameEffectLandscapeDefinition> definition;

        [ReadOnly]
        public NativeArray<TEffect>.ReadOnly values;

        [ReadOnly]
        public ComponentLookup<GameEffectAreaOverride> areasOverride;
        [ReadOnly]
        public ComponentLookup<PhysicsShapeParent> physicsShapeParents;
        [ReadOnly]
        public BufferAccessor<PhysicsTriggerEvent> physicsTriggerEvents;
        /*[ReadOnly]
        public NativeArray<GameEffectInternalSurface> surfaces;
        [ReadOnly]
        public NativeArray<GameEffectInternalCondition> conditions;
        [ReadOnly]
        public NativeArray<GameEffectInternalHeight> heights;
        [ReadOnly]
        public NativeArray<GameEffectInternalValue> effects;*/
        [ReadOnly]
        public NativeArray<Translation> translations;
        [ReadOnly]
        public NativeArray<GameEffectData<TEffect>> instances;

        public NativeArray<GameEffectResult<TEffect>> results;

        public NativeArray<GameEffectArea> areas;

        public THandler handler;

        public void Execute(int index)
        {
            var physicsTriggerEvents = this.physicsTriggerEvents[index];
            Entity effector;
            int areaIndex = -1, i;
            foreach(var physicsTriggerEvent in physicsTriggerEvents)
            {
                effector = physicsShapeParents.HasComponent(physicsTriggerEvent.entity) ? physicsShapeParents[physicsTriggerEvent.entity].entity : physicsTriggerEvent.entity;
                if (!areasOverride.HasComponent(effector))
                    continue;

                areaIndex = math.max(areaIndex, areasOverride[effector].index);
            }

            if (areaIndex == -1)
            {
                ref var definition = ref this.definition.Value;

                int numEffects = math.min(definition.areaCount, values.Length);
                float3 position = translations[index].Value;
                for (i = 0; i < numEffects; ++i)
                {
                    /*if (i == numEffects - 1)
                        numEffects = i + 1;
                    */
                    if (!definition.IsOnArea(i, position))
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

            if (handler.Change(
                index, 
                areaIndex, 
                ref value,
                results[index].value,
                physicsTriggerEvents, 
                physicsShapeParents))
            {
                GameEffectResult<TEffect> result;
                result.value = value;
                results[index] = result;
            }
        }
    }

    /*[ReadOnly]
    public NativeArray<GameEffectInternalSurface> surfaces;
    [ReadOnly]
    public NativeArray<GameEffectInternalCondition> conditions;
    [ReadOnly]
    public NativeArray<GameEffectInternalHeight> heights;
    [ReadOnly]
    public NativeArray<GameEffectInternalValue> effects;*/

    public BlobAssetReference<GameEffectLandscapeDefinition> definition;

    [ReadOnly]
    public NativeArray<TEffect>.ReadOnly values;

    [ReadOnly]
    public ComponentLookup<GameEffectAreaOverride> areasOverride;
    [ReadOnly]
    public ComponentLookup<PhysicsShapeParent> physicsShapeParents;
    [ReadOnly]
    public BufferTypeHandle<PhysicsTriggerEvent> physicsTriggerEventType;
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
        /*executor.surfaces = surfaces;
        executor.conditions = conditions;
        executor.heights = heights;
        executor.effects = effects;*/
        executor.definition = definition;
        executor.values = values;
        executor.areasOverride = areasOverride;
        executor.physicsShapeParents = physicsShapeParents;
        executor.physicsTriggerEvents = chunk.GetBufferAccessor(ref physicsTriggerEventType);
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
