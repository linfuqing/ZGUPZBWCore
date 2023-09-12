using System;
using Unity.Mathematics;
using UnityEngine;

public enum GameActionRangeType
{
    None = 0x00,
    Source = 0x01,
    Destination = 0x02,
    All = 0x03
}

[Flags]
public enum GameActionTargetType
{
    Self = 0x01,
    Ally = 0x02,
    Enemy = 0x04
}

[Flags]
public enum GameActionFlag
{
    [Tooltip("仅当击中时产生伤害")]
    DestroyOnHit = 0x0001,

    [Tooltip("不消耗耐久")]
    IgnoreDamage = 0x0002,

    [Tooltip("朝向目标方向")]
    ActorTowardTarget = 0x0004,

    [Tooltip("朝向攻击方向")]
    ActorTowardForce = 0x0008,

    [Tooltip("受重力影响")]
    UseGravity = 0x0010,

    [Tooltip("施放者贴地移动")]
    ActorOnSurface = 0x0020,

    [Tooltip("施放者移动的时候忽略Y轴")]
    ActorInAir = 0x0040,

    [Tooltip("施放者是否不可阻挡")]
    ActorUnstoppable = 0x0080,

    [Tooltip("传送到特定位置")]
    ActorLocation = 0x0100,

    [Tooltip("技能移动的时候忽略Y轴")]
    MoveInAir = 0x0200,

    [Tooltip("技能移动被应用到施放者身上")]
    MoveWithActor = 0x0400,

    [Tooltip("目标被击退时忽略Y轴")]
    TargetInAir = 0x0800,

    //[Tooltip("释放者传送到目标点")]
    //TargetActorLocation = 0x1100
}

[Serializable]
public struct GameActionInfo
{
    [Tooltip("霸体")]
    public float hitSource;

    [Tooltip("削韧")]
    public float hitDestination;

    [Tooltip("使用技能消耗的怒气值")]
    public float rage;

    [Tooltip("使用技能消耗的怒气值")]
    public float rageCost;
    
    [Tooltip("冷却时间")]
    public float coolDownTime;

    [Tooltip("技能动作时间")]
    public float artTime;

    [Tooltip("持有时长，释放者在这段时间内不可被自身打断")]
    public float performTime;

    [Tooltip("技能结算时间")]
    public float damageTime;

    [Tooltip("每次攻击结算时间")]
    public float interval;

    [Tooltip("攻击持续时间")]
    public float duration;

    [Tooltip("攻击定身开始时间")]
    public float delayStartTime;

    [Tooltip("攻击定身持续时间")]
    public float delayDuration;

    [Tooltip("释放者传送到目标点的最大距离，大于零时传送生效")]
    public float actorLocationDistance;

    [Tooltip("动量，代表惯性转化成速度的比率，跳跃时生效")]
    public float actorMomentum;

    //[Tooltip("惯性持续时间")]
    //[UnityEngine.Serialization.FormerlySerializedAs("actorMoveTime")]
    //public float actorMomentumDuration;

    [Tooltip("跳跃速度")]
    public float actorJumpSpeed;

    [Tooltip("跳跃开始时间")]
    public float actorJumpStartTime;

    [Tooltip("冲撞速度：该速度为贴地移动（如牛的冲撞），该速度一般与actorMoveSpeedIndirect互斥")]
    public float actorMoveSpeed;

    [Tooltip("冲撞/瞬移开始时间")]
    public float actorMoveStartTime;

    [Tooltip("冲撞时间")]
    [UnityEngine.Serialization.FormerlySerializedAs("actorMoveTime")]
    public float actorMoveDuration;

    [Tooltip("突进速度（该速度为忽视地形移动，且不可打断）")]
    public float actorMoveSpeedIndirect;

    [Tooltip("突进开始时间")]
    public float actorMoveStartTimeIndirect;

    [Tooltip("突进持续时长")]
    [UnityEngine.Serialization.FormerlySerializedAs("actorMoveTimeIndirect")]
    public float actorMoveDurationIndirect;

    [Tooltip("远程攻击速度")]
    public float actionMoveSpeed;

    [Tooltip("技能移动时间，该时间从结算开始计算")]
    public float actionMoveTime;

    [Tooltip("技能可被打断时间，技能在该时间后不可被打断")]
    public float actionPerformTime;

    [Tooltip("技能的最大距离，大于零时生效，否则使用攻击距离")]
    public float actionDistance;

    [Tooltip("攻击碰撞体缩放")]
    public float scale;

    [Tooltip("攻击范围")]
    public float radius;
    
    [Tooltip("攻击距离")]
    public float distance;

    [Tooltip("击退速度")]
    public float impactForce;

    [Tooltip("击退时间")]
    public float impactTime;

    [Tooltip("最大击退速度")]
    public float impactMaxSpeed;

    [Tooltip("扇形命中区域，填大于0的值生效，COS值")]
    public float dot;

    [Tooltip("角度限制")]
    public float4 angleLimit;

    public static GameActionInfo operator +(GameActionInfo x, in GameActionInfo y)
    {
        x.hitSource += y.hitSource;
        x.hitDestination += y.hitDestination;
        x.rage += y.rage;
        x.coolDownTime += y.coolDownTime;
        x.artTime += y.artTime;
        //x.castingTime += y.castingTime;
        x.performTime += y.performTime;
        x.damageTime += y.damageTime;
        x.interval += y.interval;
        x.duration += y.duration;
        x.actorLocationDistance += y.actorLocationDistance;
        x.actorMomentum += y.actorMomentum;
        x.actorJumpSpeed += y.actorJumpSpeed;
        x.actorJumpStartTime += y.actorJumpStartTime;
        x.actorMoveSpeed += y.actorMoveSpeed;
        x.actorMoveStartTime += y.actorMoveStartTime;
        x.actorMoveDuration += y.actorMoveDuration;
        x.actorMoveSpeedIndirect += y.actorMoveSpeedIndirect;
        x.actorMoveStartTimeIndirect += y.actorMoveStartTimeIndirect;
        x.actorMoveDurationIndirect += y.actorMoveDurationIndirect;
        x.actionMoveSpeed += y.actionMoveSpeed;
        x.actionMoveTime += y.actionMoveTime;
        x.actionPerformTime += y.actionPerformTime;
        x.actionDistance += y.actionDistance;
        x.radius += y.radius;
        x.distance += y.distance;
        x.impactForce += y.impactForce;
        x.impactTime += y.impactTime;
        x.impactMaxSpeed += y.impactMaxSpeed;
        x.dot += y.dot;
        x.angleLimit += y.angleLimit;

        return x;
    }

    public static GameActionInfo operator -(GameActionInfo x, in GameActionInfo y)
    {
        x.hitSource -= y.hitSource;
        x.hitDestination -= y.hitDestination;
        x.rage -= y.rage;
        x.coolDownTime -= y.coolDownTime;
        x.artTime -= y.artTime;
        //x.castingTime -= y.castingTime;
        x.performTime -= y.performTime;
        x.damageTime -= y.damageTime;
        x.interval -= y.interval;
        x.duration -= y.duration;
        x.actorLocationDistance -= y.actorLocationDistance;
        x.actorMomentum -= y.actorMomentum;
        x.actorJumpSpeed -= y.actorJumpSpeed;
        x.actorJumpStartTime -= y.actorJumpStartTime;
        x.actorMoveSpeed -= y.actorMoveSpeed;
        x.actorMoveStartTime -= y.actorMoveStartTime;
        x.actorMoveDuration -= y.actorMoveDuration;
        x.actorMoveSpeedIndirect -= y.actorMoveSpeedIndirect;
        x.actorMoveStartTimeIndirect -= y.actorMoveStartTimeIndirect;
        x.actionMoveSpeed -= y.actionMoveSpeed;
        x.actionMoveTime -= y.actionMoveTime;
        x.actionPerformTime -= y.actionPerformTime;
        x.actionDistance -= y.actionDistance;
        x.radius -= y.radius;
        x.distance -= y.distance;
        x.impactForce -= y.impactForce;
        x.impactTime -= y.impactTime;
        x.impactMaxSpeed -= y.impactMaxSpeed;
        x.dot -= y.dot;
        x.angleLimit -= y.angleLimit;

        return x;
    }
}

[Serializable]
public struct GameAction
{
    public GameActionFlag flag;

    public GameActionRangeType rangeType;

    public GameActionRangeType trackType;

    public GameActionTargetType hitType;

    public GameActionTargetType damageType;

    public uint damageMask;
    
    public uint actionMask;

    public uint actorMask;

    public uint actorStatusMask;

    public float3 offset;

    public float3 direction;

    //public float3 actorLocation;

    public float3 actorOffset;
}
