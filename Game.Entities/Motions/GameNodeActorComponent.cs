using System;
using Unity.Entities;
using ZG;

[Serializable]
public struct GameNodeActorData : IComponentData
{
    [UnityEngine.Tooltip("[][fall][jump]该时间段可为跳,也可为落")]
    public float fallTime;
    [UnityEngine.Tooltip("[][fall][jump]该时间段仅仅可为跳")]
    public float jumpTime;
    
    public float jumpToStepDelayTime;
    public float fallToStepDelayTime;
    public float stepToFallDelayTime;

    [UnityEngine.Tooltip("Fall之后的踏步时间，防止地基上踏步")]
    public float fallDelayTime;

    [UnityEngine.Tooltip("Fix之后的移动时间")]
    public float fallMoveTime;

    [UnityEngine.Tooltip("落下后的移动时间")]
    public float fallToStepTime;

    public float stepToFallSpeed;
    public float fallToStepSpeed;
    public float jumpToStepSpeed;
}

public struct GameNodeActorStatus : IComponentData
{
    public enum Status
    {
        Normal,
        Fall,
        Jump,
        Climb, 
        Swim, 
        Dive
    }
    
    public const int NODE_STATUS_ACT = 0x10;

    public Status value;
    public GameDeadline time;

    public override string ToString()
    {
        return "GameNodeActorStatus(Value" + value + ")";
    }
}

[EntityComponent(typeof(GameNodeActorStatus))]
public class GameNodeActorComponent : ComponentDataProxy<GameNodeActorData>
{
    public GameNodeActorStatus.Status status
    {
        get
        {
            return this.GetComponentData<GameNodeActorStatus>().value;
        }

        set
        {
            GameNodeActorStatus status;
            status.value = value;
            status.time = world.GetExistingSystemManaged<GameSyncSystemGroup>().rollbackManager.now;
            this.SetComponentData(status);
        }
    }

    public void SetStatus(EntityCommander commander, in GameNodeActorStatus value)
    {
        commander.SetComponentData(entity, value);
    }
}