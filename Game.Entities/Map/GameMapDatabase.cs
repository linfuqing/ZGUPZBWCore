using System;
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
        
        public static implicit operator GameEffectInternalSurface(GameMapDatabase.Surface surface)
        {
            GameEffectInternalSurface result;
            result.octaveCount = surface.octaveCount;
            result.frequency = surface.frequency;
            result.persistence = surface.persistence;

            result.offset = surface.offset;
            result.scale = surface.scale;

            return result;
        }
    }

    [Serializable]
    public struct Condition
    {
        [ZG.Index("surfaces", pathLevel = -1)]
        public int mapIndex;

        public float min;
        public float max;

        public static implicit operator GameEffectInternalCondition(Condition condition)
        {
            GameEffectInternalCondition result;
            result.mapIndex = condition.mapIndex;
            result.min = condition.min;
            result.max = condition.max;
            return result;
        }
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
}
