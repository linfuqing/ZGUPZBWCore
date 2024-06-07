using System;
using Unity.Entities;
using UnityEngine;
using ZG;

[Serializable]
public struct GameSpeakerData : IComponentData
{
    //[Mask]
    //public GameActionTargetType type;
    public LayerMask layerMask;
    public float radius;
}

[Serializable]
public struct GameSpeakerInfo : IComponentData
{
    public double time;
    public Entity target;
}

//[EntityComponent(typeof(Unity.Physics.CollisionWorldProxy))]
[EntityComponent(typeof(GameSpeakerInfo))]
public class GameSpeakerComponent : ComponentDataProxy<GameSpeakerData>
{
    public Entity target
    {
        get
        {
            return this.GetComponentData<GameSpeakerInfo>().target;
        }

        set
        {
            GameSpeakerInfo speakerInfo;
            speakerInfo.time = 0.0;
            speakerInfo.target = value;

            this.SetComponentData(speakerInfo);
        }
    }

    public override void Init(in Entity entity, EntityComponentAssigner assigner)
    {
        base.Init(entity, assigner);

        GameSpeakerInfo speakerInfo;
        speakerInfo.time = 0.0;
        speakerInfo.target = Entity.Null;

        assigner.SetComponentData(entity, speakerInfo);
    }
}