using System;
using Unity.Entities;
using ZG;

//[assembly: RegisterEntityObject(typeof(GameNodeStatusComponent))]

public struct GameNodeStatus : IComponentData, IEquatable<GameNodeStatus>
{
    public const int DELAY = 0x01;
    public const int STOP = 0x02;
    public const int OVER = 0x0C;
    public const int MASK = DELAY | STOP | OVER;

    public int value;

    public bool Equals(GameNodeStatus other)
    {
        return value == other.value;
    }

    public override int GetHashCode()
    {
        return value;
    }
}

public struct GameNodeOldStatus : IComponentData, IEnableableComponent, IEquatable<GameNodeOldStatus>
{
    public int value;

    public bool Equals(GameNodeOldStatus other)
    {
        return value == other.value;
    }

    public override int GetHashCode()
    {
        return value;
    }
}

//[EntityComponent]
[EntityComponent(typeof(GameNodeStatus))]
[EntityComponent(typeof(GameNodeOldStatus))]
public class GameNodeStatusComponent : EntityProxyComponent//, IEntityComponent
{
    public int value
    {
        get => this.GetComponentData<GameNodeStatus>().value;

        set
        {
            GameNodeStatus status;
            status.value = value;
            this.SetComponentData(status);
        }
    }

    public int oldValue
    {
        set
        {
            GameNodeOldStatus status;
            status.value = value;
            this.SetComponentData(status);
        }
    }

    /*void IEntityComponent.Init(in Entity entity, EntityComponentAssigner assigner)
    {
        if (this.GetFactory().GetEntity(entity, true) != Entity.Null)
            return;

        assigner.SetComponentData(entity, default(GameNodeStatus));
        assigner.SetComponentData(entity, default(GameNodeOldStatus));
    }*/
}