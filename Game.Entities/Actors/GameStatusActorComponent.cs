using System;
using Unity.Entities;
using UnityEngine;
using ZG;

[Flags]
public enum GameStatusActorFlag
{
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
        assigner.SetBuffer(true, entity, _levels);
    }
}
