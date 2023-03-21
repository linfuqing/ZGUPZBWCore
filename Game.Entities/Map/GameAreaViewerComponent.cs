using System;
using Unity.Entities;
using ZG;

/*public struct GameViewerVersion : IComponentData
{
    public int value;
}*/

public struct GameAreaNodePresentation : IComponentData
{
    public int areaIndex;
}

/*[EntityComponent(typeof(ViewerActorHistoryEvent))]
[EntityComponent(typeof(ViewerActorHistory))]*/
//[EntityComponent(typeof(GameViewerVersion))]
[EntityComponent(typeof(GameAreaNodePresentation))]
[EntityComponent(typeof(GameAreaNode))]
public class GameAreaViewerComponent : EntityProxyComponent, IEntityComponent
{
    public bool isActive
    {
        set
        {
            if (value)
                this.AddComponent<GameAreaViewer>();
            else
                this.RemoveComponent<GameAreaViewer>();
        }
    }

    public int areaIndex
    {
        get
        {
            return this.GetComponentData<GameAreaNode>().areaIndex;
        }

        set
        {
            GameAreaNodePresentation presentation;
            presentation.areaIndex = value;
            this.SetComponentData(presentation);
        }
    }

    void IEntityComponent.Init(in Entity entity, EntityComponentAssigner assigner)
    {
        GameAreaNodePresentation presentation;
        presentation.areaIndex = -1;
        assigner.SetComponentData(entity, presentation);
    }
}
