using System;
using Unity.Entities;
using ZG;

[Serializable]
public struct GameClipTargetData : ICleanupComponentData
{
    public float height;

    public CallbackHandle visibleCallback;
    public CallbackHandle invisibleCallback;
}

[Serializable]
public struct GameClipTargetWeight : ICleanupComponentData
{
    public float value;
}

[EntityComponent(typeof(GameClipTargetData))]
public class GameClipTargetComponent : EntityProxyComponent, IEntitySystemStateComponent
{
    public float height = 0.5f;

    public event Action onVisible;
    public event Action onInvisible;

    private void __Visible()
    {
        //print($"Visible {name}");
        if (onVisible != null)
            onVisible();
    }

    private void __Invisible()
    {
        //print($"Invisible {name}");
        if (onInvisible != null)
            onInvisible();
    }

    void IEntitySystemStateComponent.Init(in Entity entity, EntityComponentAssigner assigner)
    {
        GameClipTargetData instance;
        instance.height = height;
        instance.visibleCallback = new Action(__Visible).Register();
        instance.invisibleCallback = new Action(__Invisible).Register();
        assigner.SetComponentData(entity, instance);
    }
}
