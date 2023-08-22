using System;
using Unity.Entities;
using UnityEngine;
using ZG;

public struct GameClipTargetData : IComponentData
{
    public float weightSpeed;

    public float height;
}

public struct GameClipTargetWeight : IComponentData, IEnableableComponent
{
    public float value;
}

[Unity.Rendering.MaterialProperty("_ClipTargetWeight")]
public struct ClipTargetWeight : IComponentData
{
    public float value;
}

[EntityComponent(typeof(GameClipTargetData))]
[EntityComponent(typeof(GameClipTargetWeight))]
public class GameClipTargetComponent : EntityProxyComponent, IEntityComponent
{
    [SerializeField]
    internal float _weightSpeed = 1f;

    [SerializeField]
    internal float _height = 0.5f;

    void IEntityComponent.Init(in Entity entity, EntityComponentAssigner assigner)
    {
        GameClipTargetData instance;
        instance.weightSpeed = _weightSpeed;
        instance.height = _height;
        //instance.visibleCallback = new Action(__Visible).Register();
        //instance.invisibleCallback = new Action(__Invisible).Register();
        assigner.SetComponentData(entity, instance);
    }
}
