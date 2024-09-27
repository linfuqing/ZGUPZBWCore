using System;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using ZG;

public struct GameCreatureTemperature : IComponentData
{
    public sbyte value;
}

public struct GameCreatureFood : IComponentData
{
    public float value;
}

public struct GameCreatureWater : IComponentData
{
    public float value;
}

public struct GameCreatureFoodBuffFromTemperature : IComponentData
{
    public float scale;
}

public struct GameCreatureWaterBuffFromTemperature : IComponentData
{
    public float scale;
}

public struct GameCreatureFoodBuff : IComponentData, IBuff<float>
{
    public float value;

    public void Add(float x)
    {
        value += x;
    }

    public void Subtract(float x)
    {
        value -= x;
    }
}

public struct GameCreatureWaterBuff : IComponentData, IBuff<float>
{
    public float value;

    public void Add(float x)
    {
        value += x;
    }

    public void Subtract(float x)
    {
        value -= x;
    }
}

public struct GameCreatureDisabled : IComponentData
{

}

[Serializable]
public struct GameCreatureData : IComponentData
{
    public sbyte temperatureMin;
    public sbyte temperatureMax;

    public int foodMax;
    public int waterMax;

    public float healthBuffOnStarving;
    public float torpidityBuffOnDehydrated;

    public float foodBuffFromTemperature;
    public float waterBuffFromTemperature;

    public float foodBuffFromTemperatureOnKnockedOut;
    public float waterBuffFromTemperatureOnKnockedOut;

    public float foodBuffOnKnockedOut;
    public float waterBuffOnKnockedOut;

    public float foodBuffOnMove;
    public float waterBuffOnMove;
}

//[EntityComponent(typeof(GameCreatureFoodBuffFromTemperature))]
//[EntityComponent(typeof(GameCreatureWaterBuffFromTemperature))]
[EntityComponent(typeof(GameCreatureTemperature))]
[EntityComponent(typeof(GameCreatureFood))]
[EntityComponent(typeof(GameCreatureWater))]
[EntityComponent(typeof(GameCreatureFoodBuff))]
[EntityComponent(typeof(GameCreatureWaterBuff))]
public class GameCreatureComponent : ZG.ComponentDataProxy<GameCreatureData>
{
#if UNITY_EDITOR
    [CSVField]
    public int 饱食度
    {
        set
        {
            GameCreatureData data = base.value;
            data.foodMax = value;
            base.value = data;

            _food = value;
        }
    }

    [CSVField]
    public int 饥渴度
    {
        set
        {
            GameCreatureData data = base.value;
            data.waterMax = value;
            base.value = data;

            _water = value;
        }
    }

    [CSVField]
    public sbyte 温度最小值
    {
        set
        {
            GameCreatureData data = base.value;
            data.temperatureMin = value;
            base.value = data;
        }
    }

    [CSVField]
    public sbyte 温度最大值
    {
        set
        {
            GameCreatureData data = base.value;
            data.temperatureMax = value;
            base.value = data;
        }
    }

    [CSVField]
    public float 寒冷导致饥饿
    {
        set
        {
            GameCreatureData data = base.value;
            data.foodBuffFromTemperature = -value;
            data.foodBuffFromTemperatureOnKnockedOut = -value;
            base.value = data;
        }
    }

    [CSVField]
    public float 酷热导致缺水
    {
        set
        {
            GameCreatureData data = base.value;
            data.waterBuffFromTemperature = -value;
            base.value = data;
        }
    }

    [CSVField]
    public float 饥饿流失生命
    {
        set
        {
            GameCreatureData data = base.value;
            data.healthBuffOnStarving = -value;
            base.value = data;
        }
    }

    [CSVField]
    public float 缺水丧失理智
    {
        set
        {
            GameCreatureData data = base.value;
            data.torpidityBuffOnDehydrated = -value;
            base.value = data;
        }
    }

    [CSVField]
    public float 晕眩食物消耗
    {
        set
        {
            GameCreatureData data = base.value;
            data.foodBuffOnKnockedOut = -value;
            base.value = data;
        }
    }

    [CSVField]
    public float 行走食物消耗
    {
        set
        {
            GameCreatureData data = base.value;
            data.foodBuffOnMove = -value;
            base.value = data;
        }
    }

    [CSVField]
    public float 晕眩水分消耗
    {
        set
        {
            GameCreatureData data = base.value;
            data.waterBuffOnKnockedOut = -value;
            base.value = data;
        }
    }

    [CSVField]
    public float 行走水分消耗
    {
        set
        {
            GameCreatureData data = base.value;
            data.waterBuffOnMove = -value;
            base.value = data;
        }
    }
#endif

    private bool __isActive = true;
    
    [SerializeField]
    internal sbyte _temperature = 0;
    [SerializeField]
    internal int _food;
    [SerializeField]
    internal int _water;
    [SerializeField]
    internal float _foodBuff = 0.0f;
    [SerializeField]
    internal float _waterBuff = 0.0f;
    
    private SharedBuffManager<float, GameCreatureFoodBuff> __foodBuffManager;
    private SharedBuffManager<float, GameCreatureWaterBuff> __waterBuffManager;
    
    public bool isActive
    {
        get
        {
            return __isActive;
        }

        set
        {
            if (__isActive == value)
                return;

            if (gameObjectEntity.isCreated)
            {
                if (value)
                    this.RemoveComponent<GameCreatureDisabled>();
                else
                    this.AddComponent<GameCreatureDisabled>();
            }

            __isActive = value;
        }
    }
    
    public int food
    {
        get
        {
            if (gameObjectEntity.isCreated)
                _food = (int)math.round(this.GetComponentData<GameCreatureFood>().value);

            return _food;
        }
        
        set
        {
            if (gameObjectEntity.isCreated)
            {
                GameCreatureFood food;
                food.value = value;
                this.SetComponentData(food);
            }

            _food = value;
        }
    }

    public int water
    {
        get
        {
            if (gameObjectEntity.isCreated)
                _water = (int)math.round(this.GetComponentData<GameCreatureWater>().value);

            return _water;
        }

        set
        {
            if (gameObjectEntity.isCreated)
            {
                GameCreatureWater water;
                water.value = value;
                this.SetComponentData(water);
            }

            _water = value;
        }
    }
    
    public SharedBuffManager<float, GameCreatureFoodBuff> foodBuffManager
    {
        get
        {
            if (!__foodBuffManager.isCreated)
                __foodBuffManager = world.GetExistingSystemUnmanaged<GameCreatureBuffSystem>().foodManager;

            return __foodBuffManager;
        }
    }

    public SharedBuffManager<float, GameCreatureWaterBuff> waterBuffManager
    {
        get
        {
            if (!__waterBuffManager.isCreated)
                __waterBuffManager = world.GetExistingSystemUnmanaged<GameCreatureBuffSystem>().waterManager;

            return __waterBuffManager;
        }
    }

    public void SetFoodBuff(float value, float time)
    {
        var foodBuffManager = this.foodBuffManager;
        if (!foodBuffManager.isCreated)
            return;

        EntityData<float> buff;
        buff.entity = entity;
        buff.value = value;
        foodBuffManager.Set(buff, time);
    }

    public void SetWaterBuff(float value, float time)
    {
        var waterBuffManager = this.waterBuffManager;
        if (!waterBuffManager.isCreated)
            return;

        EntityData<float> buff;
        buff.entity = entity;
        buff.value = value;
        waterBuffManager.Set(buff, time);
    }

    public override void Init(in Entity entity, EntityComponentAssigner assigner)
    {
        base.Init(entity, assigner);

        GameCreatureTemperature temperature;
        temperature.value = _temperature;
        assigner.SetComponentData(entity, temperature);
        
        GameCreatureFood food;
        food.value = _food;
        assigner.SetComponentData(entity, food);
        
        GameCreatureWater water;
        water.value = _water;
        assigner.SetComponentData(entity, water);

        GameCreatureFoodBuff foodBuff;
        foodBuff.value = _foodBuff;
        assigner.SetComponentData(entity, foodBuff);

        GameCreatureWaterBuff waterBuff;
        waterBuff.value = _waterBuff;
        assigner.SetComponentData(entity, waterBuff);

        /*if(!__isActive)
            this.AddComponent<GameCreatureDisabled>();*/
    }

    /*protected override void OnDisable()
    {
        GameObjectEntity gameObjectEntity = this.gameObjectEntity;
        EntityManager entityManager = gameObjectEntity == null ? null : gameObjectEntity.EntityManager;
        if (entityManager != null)
        {
            entityManager.RemoveComponentIfExists<GameCreatureTemperature>(__entity);
            entityManager.RemoveComponentIfExists<GameCreatureFood>(__entity);
            entityManager.RemoveComponentIfExists<GameCreatureWater>(__entity);
            entityManager.RemoveComponentIfExists<GameCreatureFoodBuff>(__entity);
            entityManager.RemoveComponentIfExists<GameCreatureWaterBuff>(__entity);
            entityManager.RemoveComponentIfExists<GameCreatureTime>(__entity);
        }

        base.OnDisable();
    }*/
}