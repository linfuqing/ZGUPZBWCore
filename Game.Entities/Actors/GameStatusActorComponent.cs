using System;
using Unity.Entities;
using UnityEngine;
using ZG;

[Flags]
public enum GameStatusActorFlag
{
    Normal = 0x01, 
    Action = 0x02
}

[Serializable]
public struct GameStatusActorLevel : IBufferElementData
{
    [Mask]
    public GameStatusActorFlag flag;
    public int sliceIndex;
    public int status;
}

[EntityComponent(typeof(GameStatusActorLevel))]
public class GameStatusActorComponent : EntityProxyComponent, IEntityComponent
{
    [SerializeField]
    internal GameStatusActorLevel[] _levels = null;

    void IEntityComponent.Init(in Entity entity, EntityComponentAssigner assigner)
    {
        assigner.SetBuffer(EntityComponentAssigner.BufferOption.Override, entity, _levels);
    }
}
