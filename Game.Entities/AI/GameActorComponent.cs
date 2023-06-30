using System;
using Unity.Entities;
using ZG;
using GameObjectEntity = ZG.GameObjectEntity;

public struct GameActorMaster : IGameDataEntityCompoentData
{
    public Entity entity;

    Entity IGameDataEntityCompoentData.entity
    {
        get => entity;

        set => entity = value;
    }
}

[EntityComponent(typeof(GameActorMaster))]
public class GameActorComponent : EntityProxyComponent, IEntityComponent
{
    [UnityEngine.SerializeField]
    internal GameObjectEntity _master;

    public GameObjectEntity master
    {
        get
        {
            return _master;
        }

        set
        {
            GameActorMaster actorMaster;
            actorMaster.entity = value == null ? Entity.Null : value.entity;
            this.SetComponentData(actorMaster);

            _master = value;
        }
    }

    void IEntityComponent.Init(in Entity entity, EntityComponentAssigner assigner)
    {
        GameActorMaster actorMaster;
        actorMaster.entity = _master == null ? Entity.Null : _master.entity;
        assigner.SetComponentData(entity, actorMaster);
    }
}
