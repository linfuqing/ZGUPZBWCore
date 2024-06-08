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
public class GameActorComponent : EntityProxyComponent
{
}
