using System;
using Unity.Entities;
using UnityEngine;
using ZG;

public struct GameEntityRage : IComponentData, IEquatable<GameEntityRage>
{
    public float value;

    public bool Equals(GameEntityRage other)
    {
        return value == other.value;
    }
}

public struct GameEntityRageMax : IComponentData
{
    public float value;
}

public struct GameEntityRageHitScale : IComponentData
{
    public float value;
}

[EntityComponent(typeof(GameEntityRage))]
[EntityComponent(typeof(GameEntityRageMax))]
[EntityComponent(typeof(GameEntityRageHitScale))]
public class GameEntityActorRageComponent : EntityProxyComponent, IEntityComponent
{
    [SerializeField]
    internal float _rage;
    [SerializeField]
    internal float _rageMax;
    [SerializeField]
    internal float _rageHitScale;

    public float rage
    {
        get => this.GetComponentData<GameEntityRage>().value;
    }

    void IEntityComponent.Init(in Entity entity, EntityComponentAssigner assigner)
    {
        GameEntityRage rage;
        rage.value = _rage;
        assigner.SetComponentData(entity, rage);

        GameEntityRageMax rageMax;
        rageMax.value = _rageMax;
        assigner.SetComponentData(entity, rageMax);

        GameEntityRageHitScale rageHitScale;
        rageHitScale.value = _rageHitScale;
        assigner.SetComponentData(entity, rageHitScale);
    }
}
