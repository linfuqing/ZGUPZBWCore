using System;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

[Serializable]
public struct GameEffect : IGameEffect<GameEffect>
{
    public sbyte force;
    public sbyte power;
    public sbyte temperature;
    [UnityEngine.Serialization.FormerlySerializedAs("hp")]
    public float health;
    [UnityEngine.Serialization.FormerlySerializedAs("hunger")]
    public float food;
    [UnityEngine.Serialization.FormerlySerializedAs("moisture")]
    public float water;
    public float itemTimeScale;
    public float layTimeScale;
    public float foodBuffFromTemperatureScale;
    public float waterBuffFromTemperatureScale;

    public void Add(in GameEffect value)
    {
        this += value;
    }

    public override int GetHashCode()
    {
        return base.GetHashCode();
    }

    public override bool Equals(object obj)
    {
        return this == (GameEffect)obj;
    }

    public static bool operator !=(GameEffect x, GameEffect y)
    {
        return !(x == y);
    }

    public static bool operator ==(GameEffect x, GameEffect y)
    {
        return x.force == y.force &&
            x.power == y.power &&
            x.temperature == y.temperature &&
            Mathf.Approximately(x.health, y.health) &&
            Mathf.Approximately(x.food, y.food) &&
            Mathf.Approximately(x.water, y.water) &&
            Mathf.Approximately(x.itemTimeScale, y.itemTimeScale) &&
            Mathf.Approximately(x.layTimeScale, y.layTimeScale) &&
            Mathf.Approximately(x.foodBuffFromTemperatureScale, y.foodBuffFromTemperatureScale) &&
            Mathf.Approximately(x.waterBuffFromTemperatureScale, y.waterBuffFromTemperatureScale);
    }

    /*public static bool operator <=(GameEffect x, GameEffect y)
    {
        return x.force <= y.force &&
            x.power <= y.power &&
            x.temperature <= y.temperature &&
            x.health <= y.health &&
            x.food <= y.food &&
            x.water <= y.water &&
            x.itemTimeScale <= y.itemTimeScale &&
            x.layTimeScale <= y.layTimeScale;
    }

    public static bool operator >=(GameEffect x, GameEffect y)
    {
        return x.force >= y.force &&
            x.power >= y.power &&
            x.temperature >= y.temperature &&
            x.health >= y.health &&
            x.food >= y.food &&
            x.water >= y.water &&
            x.itemTimeScale >= y.itemTimeScale &&
            x.layTimeScale >= y.layTimeScale;
    }*/

    public static GameEffect operator +(GameEffect x, GameEffect y)
    {
        x.force += y.force;
        x.power += y.power;
        x.temperature += y.temperature;
        x.health += y.health;
        x.food += y.food;
        x.water += y.water;
        x.itemTimeScale += y.itemTimeScale;
        x.layTimeScale += y.layTimeScale;
        x.foodBuffFromTemperatureScale += y.foodBuffFromTemperatureScale;
        x.waterBuffFromTemperatureScale += y.waterBuffFromTemperatureScale;

        return x;
    }

    public static GameEffect operator -(GameEffect x)
    {
        x.force = (sbyte)-x.force;
        x.power = (sbyte)-x.power;
        x.temperature = (sbyte)-x.temperature;
        x.health = -x.health;
        x.food = -x.food;
        x.water = -x.water;
        x.itemTimeScale = -x.itemTimeScale;
        x.layTimeScale = -x.layTimeScale;
        x.foodBuffFromTemperatureScale = -x.foodBuffFromTemperatureScale;
        x.waterBuffFromTemperatureScale = -x.waterBuffFromTemperatureScale;

        return x;
    }

    public static GameEffect operator -(GameEffect x, GameEffect y)
    {
        x.force -= y.force;
        x.power -= y.power;
        x.temperature -= y.temperature;
        x.health -= y.health;
        x.food -= y.food;
        x.water -= y.water;
        x.itemTimeScale -= y.itemTimeScale;
        x.layTimeScale -= y.layTimeScale;
        x.foodBuffFromTemperatureScale -= y.foodBuffFromTemperatureScale;
        x.waterBuffFromTemperatureScale -= y.waterBuffFromTemperatureScale;

        return x;
    }

    public static GameEffect operator *(GameEffect x, float y)
    {
        x.force = (sbyte)Mathf.RoundToInt(x.force * y);
        x.power = (sbyte)Mathf.RoundToInt(x.power * y);
        x.temperature = (sbyte)Mathf.RoundToInt(x.temperature * y);
        x.health = Mathf.RoundToInt(x.health * y);
        x.food *= y;
        x.water *= y;
        x.itemTimeScale *= y;
        x.layTimeScale *= y;
        x.foodBuffFromTemperatureScale *= y;
        x.waterBuffFromTemperatureScale *= y;

        return x;
    }
}

public class GameMapDatabase : ScriptableObject
{
    [Serializable]
    public struct Surface
    {
#if UNITY_EDITOR
        public string name;
#endif

        public int octaveCount;
        public float frequency;
        public float persistence;

        public Vector2 offset;
        public Vector2 scale;
    }

    [Serializable]
    public struct Condition
    {
        [ZG.Index("surfaces", pathLevel = -1)]
        public int mapIndex;

        public float min;
        public float max;
    }

    [Serializable]
    public struct Height
    {
        public float scale;

        public Condition[] conditions;
    }

    [Serializable]
    public struct Effect
    {
#if UNITY_EDITOR
        public string name;
#endif

        public float fromHeight;

        public float toHeight;

        public GameEffect value;

        public Condition[] conditions;

        public Height[] fromHeights;

        public Height[] toHeights;
    }

    public Surface[] surfaces;
    public Effect[] effects;

    public void CreateEffectLandscapeDefinition(out BlobAssetReference<GameEffectLandscapeDefinition> defintion, ref NativeList<GameEffect> values)
    {
        using (var builder = new BlobBuilder(Allocator.Temp))
        {
            ref var root = ref builder.ConstructRoot<GameEffectLandscapeDefinition>();

            int i, numSurfaces = this.surfaces.Length;
            var surfaces = builder.Allocate(ref root.surfaces, numSurfaces);
            for (i = 0; i < numSurfaces; ++i)
            {
                ref var sourceSurface = ref this.surfaces[i];
                ref var destinationSurface = ref surfaces[i];

                destinationSurface.octaveCount = sourceSurface.octaveCount;
                destinationSurface.frequency = sourceSurface.frequency;
                destinationSurface.persistence = sourceSurface.persistence;

                destinationSurface.offset = sourceSurface.offset;
                destinationSurface.scale = sourceSurface.scale;
            }

            int effectIndex = values.Length, numEffects = effects == null ? 0 : effects.Length;
            values.ResizeUninitialized(effectIndex + numEffects);

            var areas = builder.Allocate(ref root.areas, numEffects);

            int j, k, numConditions, numLayers, conditionCount = 0, layerCount = 0;
            for (i = 0; i < numEffects; ++i)
            {
                ref var effect = ref this.effects[i];

                conditionCount += effect.conditions == null ? 0 : effect.conditions.Length;

                numLayers = effect.fromHeights == null ? 0 : effect.fromHeights.Length;
                for(j = 0; j < numLayers; ++j)
                {
                    ref var height = ref effect.fromHeights[j];

                    conditionCount += height.conditions == null ? 0 : height.conditions.Length;
                }

                layerCount += numLayers;

                numLayers = effect.toHeights == null ? 0 : effect.toHeights.Length;
                for (j = 0; j < numLayers; ++j)
                {
                    ref var height = ref effect.toHeights[j];

                    conditionCount += height.conditions == null ? 0 : height.conditions.Length;
                }

                layerCount += numLayers;
            }

            int conditionIndex = 0, layerIndex = 0;
            var layers = builder.Allocate(ref root.layers, layerCount);
            var conditions = builder.Allocate(ref root.conditions, conditionCount);
            BlobBuilderArray<int> conditionIndices, layerIndices;
            for (i = 0; i < numEffects; ++i)
            {
                ref var effect = ref this.effects[i];
                ref var area = ref areas[i];

                numConditions = effect.conditions == null ? 0 : effect.conditions.Length;
                conditionIndices = builder.Allocate(ref area.conditionIndices, numConditions);
                for (j = 0; j < numConditions; ++j)
                {
                    conditionIndices[j] = conditionIndex;

                    ref var sourceCondition = ref effect.conditions[j];
                    ref var destinationCondition = ref conditions[conditionIndex++];

                    destinationCondition.mapIndex = sourceCondition.mapIndex;
                    destinationCondition.min = sourceCondition.min;
                    destinationCondition.max = sourceCondition.max;
                }

                area.fromLayers.offset = effect.fromHeight;

                numLayers = effect.fromHeights == null ? 0 : effect.fromHeights.Length;
                layerIndices = builder.Allocate(ref area.fromLayers.indices, numLayers);
                for (j = 0; j < numLayers; ++j)
                {
                    layerIndices[j] = layerIndex;

                    ref var height = ref effect.fromHeights[j];

                    ref var layer = ref layers[layerIndex++];
                    
                    layer.scale = height.scale;

                    numConditions = height.conditions == null ? 0 : height.conditions.Length;

                    conditionIndices = builder.Allocate(ref layer.conditionIndices, numConditions);

                    for (k = 0; k < numConditions; ++k)
                    {
                        conditionIndices[k] = conditionIndex;

                        ref var sourceCondition = ref height.conditions[k];
                        ref var destinationCondition = ref conditions[conditionIndex++];

                        destinationCondition.mapIndex = sourceCondition.mapIndex;
                        destinationCondition.min = sourceCondition.min;
                        destinationCondition.max = sourceCondition.max;
                    }
                }

                area.toLayers.offset = effect.toHeight;

                numLayers = effect.toHeights == null ? 0 : effect.toHeights.Length;
                layerIndices = builder.Allocate(ref area.fromLayers.indices, numLayers);
                for (j = 0; j < numLayers; ++j)
                {
                    layerIndices[j] = layerIndex;

                    ref var height = ref effect.toHeights[j];

                    ref var layer = ref layers[layerIndex++];

                    layer.scale = height.scale;

                    numConditions = height.conditions == null ? 0 : height.conditions.Length;

                    conditionIndices = builder.Allocate(ref layer.conditionIndices, numConditions);

                    for (k = 0; k < numConditions; ++k)
                    {
                        conditionIndices[k] = conditionIndex;

                        ref var sourceCondition = ref height.conditions[k];
                        ref var destinationCondition = ref conditions[conditionIndex++];

                        destinationCondition.mapIndex = sourceCondition.mapIndex;
                        destinationCondition.min = sourceCondition.min;
                        destinationCondition.max = sourceCondition.max;
                    }
                }

                values[effectIndex + i] = effect.value;
            }

            defintion = builder.CreateBlobAssetReference<GameEffectLandscapeDefinition>(Allocator.Persistent);
        }
    }

}
