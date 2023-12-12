using JetBrains.Annotations;
using System;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using ZG;
using Math = ZG.Mathematics.Math;
using AnimatorControllerParameterType = UnityEngine.AnimatorControllerParameterType;
using AnimatorControllerParameter = UnityEngine.AnimatorControllerParameter;

[assembly: RegisterEntityObject(typeof(Animator))]
[assembly: RegisterGenericComponentType(typeof(GameTransformKeyframe<GameAnimatorTransform>))]
//[assembly: RegisterGenericComponentType(typeof(GameTransformDestination<GameAnimatorTransform>))]
[assembly: RegisterGenericComponentType(typeof(GameTransformVelocity<GameAnimatorTransform, GameAnimatorVelocity>))]

public struct GameAnimatorTransform : IGameTransform<GameAnimatorTransform>
{
    public float fraction;
    public float forwardAmount;
    public float turnAmount;

    public GameAnimatorTransform LerpTo(in GameAnimatorTransform value, float scale)
    {
        GameAnimatorTransform transform;
        transform.fraction = math.lerp(fraction, value.fraction, scale);
        transform.forwardAmount = math.lerp(forwardAmount, value.forwardAmount, scale);
        transform.turnAmount = math.lerp(turnAmount, value.turnAmount, scale);
        return transform;
    }
}

public struct GameAnimatorVelocity : IGameTransformVelocity<GameAnimatorTransform>
{
    public float heightVelocity;
    public float forwardVelocity;
    public float turnVelocity;

    public GameAnimatorTransform SmoothDamp(in GameAnimatorTransform source, in GameAnimatorTransform destination, float smoothTime, float deltaTime)
    {
        GameAnimatorTransform transform;
        transform.fraction = Math.SmoothDamp(source.fraction, destination.fraction, ref heightVelocity, smoothTime, float.MaxValue, deltaTime);// destination.fraction;
        transform.forwardAmount = Math.SmoothDamp(source.forwardAmount, destination.forwardAmount, ref forwardVelocity, smoothTime, float.MaxValue, deltaTime);
        transform.turnAmount = Math.SmoothDamp(source.turnAmount, destination.turnAmount, ref turnVelocity, smoothTime, float.MaxValue, deltaTime);
        return transform;
    }
}

public struct GameAnimatorDesiredVelocity : IComponentData
{
    public float deltaTime;
    public float sign;
    public float3 distance;
}

public struct GameAnimatorParameterData : IComponentData
{
    /*public const int ENABLE_BUSY = 0x01;
    public const int ENABLE_DESIRED_STATUS = 0x02;
    public const int ENABLE_MOVE_STATUS = 0x04;
    public const int ENABLE_TURN = 0x08;
    public const int ENABLE_HEIGHT = 0x10;

    public static readonly int triggerHashBusy = Animator.StringToHash("Busy");
    public static readonly int triggerHashDesiredStatus = Animator.StringToHash("DesiredStatus");
    public static readonly int triggerHashMoveStatus = Animator.StringToHash("MoveStatus");
    public static readonly int triggerHashMove = Animator.StringToHash("Move");
    public static readonly int triggerHashTurn = Animator.StringToHash("Turn");
    public static readonly int triggerHashHeight = Animator.StringToHash("Height");*/

    public int busyID;
    public int desiredStatusID;
    public int moveStatusID;
    public int moveID;
    public int turnID;
    public int heightID;
}

public struct GameAnimatorActorTimeToLive : IComponentData
{
    public float value;
}

public struct GameAnimatorActorStatus : IComponentData
{
    public int value;
}

public struct GameAnimatorDesiredStatus : IComponentData
{
    public int value;
}

public struct GameAnimatorDelay : IComponentData, IEquatable<GameNodeDelay>, IEquatable<GameAnimatorDelayInfo>
{
    public enum Status
    {
        Normal, 
        Busy
    }

    public Status status;
    public double startTime;
    public double endTime;

    public bool Equals(GameNodeDelay other)
    {
        return startTime == other.time + other.startTime && endTime == other.time + other.endTime;
    }

    public bool Equals(GameAnimatorDelayInfo other)
    {
        return startTime == other.startTime && endTime == other.endTime;
    }
}

public struct GameAnimatorTransformData : IComponentData
{
    public float turnInterpolationSpeed;
}

public struct GameAnimatorTransformInfo : IComponentData
{
    [Flags]
    public enum DirtyFlag
    {
        Height = 0x01, 
        Forward = 0x02, 
        Turn = 0x04
    }

    public DirtyFlag dirtyFlag;
    //public float maxFraction;
    public float heightAmount;
    public float forwardAmount;
    public float turnAmount;
    public GameAnimatorTransform value;
}

public struct GameAnimatorDelayInfo : IBufferElementData, IEquatable<GameNodeDelay>
{
    public double startTime;
    public double endTime;

    public bool Check(double time)
    {
        return startTime <= time && time < endTime;
    }

    public bool Equals(GameNodeDelay other)
    {
        return startTime == other.startTime && endTime == other.endTime;
    }
}

public struct GameAnimatorActorStatusInfo : IBufferElementData
{
    public double time;
    public int value;
}

public struct GameAnimatorDesiredStatusInfo : IBufferElementData
{
    public double time;
    public int value;
}

public struct GameAnimatorHitInfo : IBufferElementData
{
    public int version;
    public int actionIndex;
    public double time;
    public Entity entity;
}

public struct GameAnimatorBreakInfo : IBufferElementData
{
    public int version;
    public int delayIndex;
    public double time;
}

public struct GameAnimatorDoInfo : IBufferElementData
{
    public int version;
    public int index;
    public double time;
    public Entity entity;
}

[InternalBufferCapacity(5)]
public struct GameAnimatorForwardKeyframe : IBufferElementData
{
    public float value;
}

[InternalBufferCapacity(3)]
public struct GameAnimatorTurnKeyframe : IBufferElementData
{
    public float value;
}

[EntityComponent(typeof(Animator))]
[EntityComponent(typeof(GameNodeDesiredStatus))]
[EntityComponent(typeof(GameNodeDesiredVelocity))]
[EntityComponent(typeof(GameAnimatorDesiredVelocity))]
[EntityComponent(typeof(GameTransformKeyframe<GameAnimatorTransform>))]
//[EntityComponent(typeof(GameTransformDestination<GameAnimatorTransform>))]
[EntityComponent(typeof(GameTransformVelocity<GameAnimatorTransform, GameAnimatorVelocity>))]
[EntityComponent(typeof(GameAnimatorParameterData))]
[EntityComponent(typeof(GameAnimatorActorTimeToLive))]
[EntityComponent(typeof(GameAnimatorTransformData))]
[EntityComponent(typeof(GameAnimatorTransformInfo))]
[EntityComponent(typeof(GameAnimatorHitInfo))]
[EntityComponent(typeof(GameAnimatorDelayInfo))]
[EntityComponent(typeof(GameAnimatorDesiredStatusInfo))]
[EntityComponent(typeof(GameAnimatorActorStatusInfo))]
[EntityComponent(typeof(GameAnimatorBreakInfo))]
[EntityComponent(typeof(GameAnimatorDoInfo))]
[EntityComponent(typeof(GameAnimatorForwardKeyframe))]
[EntityComponent(typeof(GameAnimatorTurnKeyframe))]
public class GameAnimatorComponent : EntityProxyComponent, IEntityComponent
{
    internal float _actorTimeToLive = 0.15f;

    //[SerializeField]
    internal float _turnInterpolationSpeed = 3.0f;

    [SerializeField]
    internal float[] _forwardKeyframes =
    {
        -1.0f, 
        0.0f, 
        1.0f, 
        2.0f, 
        3.0f
    };

    //[SerializeField]
    internal float[] _turnKeyframes =
    {
        -Mathf.PI,
        //-Mathf.PI * 3.0f / 4.0f,
        0.0f,
        //Mathf.PI * 3.0f / 4.0f,
        Mathf.PI
    };

    void IEntityComponent.Init(in Entity entity, EntityComponentAssigner assigner)
    {
        GameAnimatorTransformData instance;
        instance.turnInterpolationSpeed = _turnInterpolationSpeed;
        assigner.SetComponentData(entity, instance);
        
        int length = _forwardKeyframes.Length;
        var forwardKeyframes = new GameAnimatorForwardKeyframe[length];
        for (int i = 0; i < length; ++i)
            forwardKeyframes[i].value = _forwardKeyframes[i];

        assigner.SetBuffer(EntityComponentAssigner.BufferOption.Override, entity, forwardKeyframes);

        length = _turnKeyframes.Length;
        var turnKeyframes = new GameAnimatorTurnKeyframe[length];
        for (int i = 0; i < length; ++i)
            turnKeyframes[i].value = _turnKeyframes[i];

        assigner.SetBuffer(EntityComponentAssigner.BufferOption.Override, entity, turnKeyframes);

        GameAnimatorActorTimeToLive actorTimeToLive;
        actorTimeToLive.value = _actorTimeToLive;
        assigner.SetComponentData(entity, actorTimeToLive);

        GameAnimatorParameterData parameters;
        var animatorController = GetComponentInChildren<IAnimatorController>(true);
        //flag.value = __GetFlag(animator == null ? null : animator.parameters);
        if (animatorController == null)
            parameters = default;
        else
        {
            parameters.busyID = animatorController.GetParameterID("Busy");
            parameters.desiredStatusID = animatorController.GetParameterID("DesiredStatus");
            parameters.moveStatusID = animatorController.GetParameterID("MoveStatus");
            parameters.moveID = animatorController.GetParameterID("Move");
            parameters.turnID = animatorController.GetParameterID("Turn");
            parameters.heightID = animatorController.GetParameterID("Height");
        }

        assigner.SetComponentData(entity, parameters);
    }
    
    /*private static bool __ContainsParameter(int id, AnimatorControllerParameterType type, AnimatorControllerParameter[] parameters)
    {
        foreach (var parameter in parameters)
        {
            if (parameter != null && parameter.nameHash == id)
                return parameter.type == type;
        }

        return false;
    }

    private static int __GetFlag(AnimatorControllerParameter[] parameters)
    {
        int result = 0;
        if (parameters != null)
        {
            if (__ContainsParameter(GameAnimatorFlag.triggerHashBusy, AnimatorControllerParameterType.Bool, parameters))
                result |= GameAnimatorFlag.ENABLE_BUSY;

            if (__ContainsParameter(GameAnimatorFlag.triggerHashDesiredStatus, AnimatorControllerParameterType.Int, parameters))
                result |= GameAnimatorFlag.ENABLE_DESIRED_STATUS;

            if (__ContainsParameter(GameAnimatorFlag.triggerHashMoveStatus, AnimatorControllerParameterType.Int, parameters))
                result |= GameAnimatorFlag.ENABLE_MOVE_STATUS;

            if (__ContainsParameter(GameAnimatorFlag.triggerHashTurn, AnimatorControllerParameterType.Float, parameters))
                result |= GameAnimatorFlag.ENABLE_TURN;

            if (__ContainsParameter(GameAnimatorFlag.triggerHashHeight, AnimatorControllerParameterType.Float, parameters))
                result |= GameAnimatorFlag.ENABLE_HEIGHT;
        }

        return result;
    }*/
}
