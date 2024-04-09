using System;
using Unity.Entities;
using UnityEngine;
using ZG;

[Flags]
public enum GameDamageActorFlag
{
    Loop = 0x01,
    Action = 0x02
}

[Serializable]
public struct GameDamageActorLevel : IBufferElementData
{
    [Mask]
    public GameDamageActorFlag flag;
    public int sliceIndex;
    public float hit;
}

public struct GameDamageActorHit : IComponentData
{
    public float value;
}

[EntityComponent(typeof(GameDamageActorLevel))]
[EntityComponent(typeof(GameDamageActorHit))]
public class GameDamageActorComponent : EntityProxyComponent, IEntityComponent
{
    [SerializeField]
    internal GameDamageActorLevel[] _levels = null;
    
    void IEntityComponent.Init(in Entity entity, EntityComponentAssigner assigner)
    {
        assigner.SetBuffer(EntityComponentAssigner.BufferOption.Override, entity, _levels);
    }
}
