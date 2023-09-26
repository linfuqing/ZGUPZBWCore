using Unity.Entities;
using UnityEngine;
using ZG;

[System.Serializable]
public struct GameEntityCharacterHit : IBufferElementData
{
    public LayerMask layerMask;
    public float value;
}

[EntityComponent(typeof(GameEntityCharacterHit))]
public class GameEntityCharacterComponent : EntityProxyComponent, IEntityComponent
{
    [SerializeField]
    internal GameEntityCharacterHit[] _values = null;

    void IEntityComponent.Init(in Entity entity, EntityComponentAssigner assigner)
    {
        assigner.SetBuffer(EntityComponentAssigner.BufferOption.Override, entity, _values);
    }
}
