using System;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using ZG;

//[assembly: RegisterEntityObject(typeof(GameEntityHealthComponent))]

public struct GameEntityHealth : IComponentData
{
    public float value;
}

public struct GameEntityHealthDamageCount : IComponentData
{
    public int value;
}

public struct GameEntityHealthDamage : IBufferElementData
{
    public float value;
    public double time;
    public Entity entity;
}

[InternalBufferCapacity(1)]
public struct GameEntityHealthBuff : IBufferElementData
{
    public float value;
    public float duration;
}

[Serializable]
public struct GameEntityHealthData : IComponentData
{
    public int max;
}

//[EntityComponent]
[EntityComponent(typeof(GameEntityHealth))]
[EntityComponent(typeof(GameEntityHealthDamageCount))]
[EntityComponent(typeof(GameEntityHealthDamage))]
[EntityComponent(typeof(GameEntityHealthBuff))]
public class GameEntityHealthComponent : ComponentDataProxy<GameEntityHealthData>
{
#if UNITY_EDITOR
    [ZG.CSVField]
    public int 生命值
    {
        set
        {
            if (value < 1)
            {
                value = 1;

                Debug.Log(name + ": lost the hp.");
            }

            GameEntityHealthData data = base.value;
            data.max = value;
            base.value = data;

            _health = value;
        }
    }
#endif

    [SerializeField]
    internal int _health;

    [SerializeField]
    [Tooltip("基本血量恢复")]
    internal float _buff = 0.0f;

    public float health
    {
        get
        {
            return this.GetComponentData<GameEntityHealth>().value;
        }

        set
        {
            GameEntityHealth health;
            health.value = value;
            this.SetComponentData(health);
        }
    }
    
    public void SetBuff(float value, float duration)
    {
        GameEntityHealthBuff buff;
        buff.value = value;
        buff.duration = duration;

        this.AppendBuffer(buff);
    }

    public override void Init(in Entity entity, EntityComponentAssigner assigner)
    {
        base.Init(entity, assigner);

        GameEntityHealth health;
        health.value = _health;
        assigner.SetComponentData(entity, health);

        GameEntityHealthBuff buff;
        buff.duration = 0.0f;
        buff.value = _buff;
        assigner.SetBuffer(EntityComponentAssigner.BufferOption.Override, entity, new GameEntityHealthBuff[] { buff });
    }
}