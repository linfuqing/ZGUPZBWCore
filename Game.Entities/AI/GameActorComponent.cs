using System;
using Unity.Entities;
using ZG;
using GameObjectEntity = ZG.GameObjectEntity;

public struct GameActorMaster : IComponentData
{
    public Entity entity;

    /*public void Serialize(int entityIndex, ref EntityDataWriter writer)
    {
        writer.Write(entityIndex);
    }

    public int Deserialize(ref EntityDataReader reader)
    {
        return reader.Read<int>();
    }

    Entity IGameDataEntityCompoent.entity
    {
        get => entity;

        set => entity = value;
    }*/
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
