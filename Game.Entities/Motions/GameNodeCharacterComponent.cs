using System;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Physics;
using UnityEngine;
using ZG;

[Serializable]
public struct GameNodeCharacterData : IComponentData
{
    [Flags]
    public enum Flag
    {
        [Tooltip("始终Y轴向上")]
        SurfaceUp = 0x01,
        [Tooltip("根据地形旋转")]
        RotationAll = 0x02,
        [Tooltip("可以游泳")]
        CanSwim = 0x04,
        [Tooltip("可以飞行")]
        CanFly = 0x08,
        //AirControl = 0x010, 
        [Tooltip("只能飞行")]
        FlyOnly = 0x18
    }

    [Mask]
    public Flag flag;

    [Tooltip("可攀爬区域")]
    public LayerMask climbMask;

    [Tooltip("最大碰撞检测迭代次数")]
    public int maxIterations;
    [Tooltip("最大移动速度")]
    public float maxMovementSpeed;
    [Tooltip("碰撞检测距离")]
    public float contactTolerance;
    [Tooltip("皮肤厚度")]
    public float skinWidth;
    [Tooltip("头部离身体的长度，影响跨坡跳跃")]
    public float headLength;
    [Tooltip("足部离地面的高度，影响跳还是坠落的判定，高于此高度则为坠落")]
    public float footHeight;
    [Tooltip("静态斜率，一个COS值，坡度大于该值则会滑落")]
    public float staticSlope;
    [Tooltip("脚步斜率，一个COS值，坡度大于该值则会不能行走，一般这个值要大于静态斜率")]
    public float stepSlope;
    [Tooltip("脚步摩檫力，如果站立的地方没产生相应的摩檫力，则无法行走")]
    public float stepFraction;
    [Tooltip("支撑摩檫力，如果站立的地方没产生相应的摩檫力，则滑落，摩檫力要先能支撑，才可行走")]
    public float supportFraction;
    [Tooltip("大于0则开启跨坡跳跃，该值影响最大的坡高度，坡的高度大于这个值就不会跳跃")]
    [UnityEngine.Serialization.FormerlySerializedAs("stepDeep")]
    public float raycastLength;

    [Tooltip("水面浮起的最小高度")]
    [UnityEngine.Serialization.FormerlySerializedAs("stepHeight")]
    public float waterMinHeight;

    [Tooltip("水面浮起的最大高度")]
    [UnityEngine.Serialization.FormerlySerializedAs("waterHeight")]
    public float waterMaxHeight;

    [Tooltip("水面摩擦力，对速度的直接缩放")]
    public float waterDamping;

    [Tooltip("攀爬摩擦力，对速度的直接缩放")]
    public float climbDamping;

    [Tooltip("空气摩擦力，飞行时对速度的直接缩放")]
    public float airDamping;

    [Tooltip("飞行速度")]
    public float airSpeed;

    [Tooltip("转角速度")]
    public float angluarSpeed;

    [Tooltip("重力加速度比率，比率越大受到重力影响越大")]
    public float gravityFactor;

    [Tooltip("浮力")]
    public float buoyancy;
    [Tooltip("抓地力，大于0开启攀爬")]
    public float suction;
}

[Serializable]
public struct GameNodeCharacterFlag : IComponentData
{
    [Flags]
    public enum Flag
    {
        //Dirty = 0x01,
        Unstoppable = 0x01
    }

    public Flag value;
}

[Serializable]
public struct GameNodeCharacterStatus : IComponentData
{
    public enum Status
    {
        None,
        Unsupported, 
        Supported,
        //有触达,但斜率不可走
        Sliding, 
        Contacting,
        Firming
    }

    public enum Area
    {
        Normal, 
        Water, 
        Air, 
        Fix,
        Climb
    }

    public const int NODE_STATIC_HOLD = 0x10;

    public Status value;
    public Area area;

    public bool IsStatic(bool canSwim, bool isFlyOnly)
    {
        bool isStatic;
        switch (area)
        {
            case Area.Normal:
                isStatic = value == Status.Firming;
                break;
            case Area.Water:
                isStatic = canSwim || value == Status.Firming;
                break;
            case Area.Air:
                isStatic = isFlyOnly;
                break;
            case Area.Fix:
                switch (value)
                {
                    case Status.Contacting:
                    case Status.Firming:
                        isStatic = true;
                        break;
                    default:
                        isStatic = false;
                        break;
                }
                break;
            case Area.Climb:
                isStatic = value == Status.Firming;
                break;
            default:
                isStatic = false;
                break;
        }

        return isStatic;
    }

    public bool IsStatic(GameNodeCharacterData.Flag flag)
    {
        bool canSwim = (flag & GameNodeCharacterData.Flag.CanSwim) == GameNodeCharacterData.Flag.CanSwim,
            isFlyOnly = (flag & GameNodeCharacterData.Flag.FlyOnly) == GameNodeCharacterData.Flag.FlyOnly;

        return IsStatic(canSwim, isFlyOnly);
    }

    public override string ToString()
    {
        return "GameNodeCharacterStatus(Value: " + value + ", Area: " + area + ")";
    }
    
    public static implicit operator GameNodeCharacterStatus(byte value)
    {
        GameNodeCharacterStatus result;
        result.value = (Status)(value & 7);
        result.area = (Area)(value >> 3);
        return result;
    }

    public static explicit operator byte(GameNodeCharacterStatus value)
    {
        return (byte)((int)value.value | ((int)value.area << 3));
    }
}

[Serializable]
public struct GameNodeCharacterSurface : IComponentData
{
    public uint layerMask;
    public float fraction;
    public float3 normal;
    public float3 velocity;
    public quaternion rotation;
}

[Serializable]
public struct GameNodeCharacterAngle : IComponentData
{
    public half value;
}

[Serializable]
public struct GameNodeCharacterVelocity : IComponentData
{
    public float3 value;
}

[Serializable]
public struct GameNodeCharacterDesiredVelocity : IComponentData
{
    public float3 linear;
    public float3 angular;
}

[Serializable]
public struct GameNodeCharacterCollider : IComponentData
{
    public BlobAssetReference<Unity.Physics.Collider> value;
}

[Serializable]
public struct GameNodeCharacterCenterOfMass : IComponentData
{
    public float3 value;
}

[Serializable, InternalBufferCapacity(16)]
public struct GameNodeCharacterDistanceHit : IBufferElementData
{
    public DistanceHit value;
}

public struct GameNodeCharacterRotationDirty : IComponentData
{

}

//[EntityComponent(typeof(CollisionWorldProxy))]
[EntityComponent(typeof(PhysicsVelocity))]
[EntityComponent(typeof(PhysicsGravityFactor))]
[EntityComponent(typeof(GameNodeCharacterFlag))]
[EntityComponent(typeof(GameNodeCharacterStatus))]
[EntityComponent(typeof(GameNodeCharacterSurface))]
[EntityComponent(typeof(GameNodeCharacterAngle))]
[EntityComponent(typeof(GameNodeCharacterVelocity))]
//[EntityComponent(typeof(GameNodeCharacterDesiredVelocity))]
//[EntityComponent(typeof(GameNodeCharacterCollider))]
[EntityComponent(typeof(GameNodeCharacterDistanceHit))]
public class GameNodeCharacterComponent : ComponentDataProxy<GameNodeCharacterData>
{
    [SerializeField]
    internal PhysicsShapeComponent _shape = null;
    internal bool _isKinematic = true;
    
    public half angle
    {
        get
        {
            return this.GetComponentData<GameNodeCharacterAngle>().value;
        }

        set
        {
            GameNodeAngle angle;
            angle.value = value;
            this.SetComponentData(angle);
                
            GameNodeCharacterAngle characterAngle = this.GetComponentData<GameNodeCharacterAngle>();
            characterAngle.value = value;
            this.SetComponentData(characterAngle);
        }
    }

    public float3 position
    {
        get
        {
            return this.GetComponentData<Translation>().Value;
        }

        set
        {
            Translation translation;
            translation.Value = value;
            
            this.SetComponentData(translation);
        }
    }

    public PhysicsVelocity velocity
    {
        get
        {
            return this.GetComponentData<PhysicsVelocity>();
        }

        set
        {
            this.SetComponentData(value);

            if (_isKinematic)
            {
                GameNodeCharacterVelocity velocity;
                velocity.value = value.Linear;
                this.SetComponentData(velocity);
            }
        }
    }
    
    public GameNodeCharacterStatus status
    {
        get
        {
            return this.GetComponentData<GameNodeCharacterStatus>();
        }

        set
        {
            this.SetComponentData(value);
        }
    }

    public GameNodeCharacterSurface surface
    {
        get
        {
            return this.GetComponentData<GameNodeCharacterSurface>();
        }
    }

    public half AssertAngleAreEqual()
    {
        if(this.HasComponent<GameNodeParent>())
            return SyncAngle();

        half angle = this.GetComponentData<GameNodeCharacterAngle>().value;
        if (angle != this.GetComponentData<GameNodeAngle>().value)
            Debug.LogError(transform.root.name + this.HasComponent<Disabled>());

        UnityEngine.Assertions.Assert.AreEqual(angle, this.GetComponentData<GameNodeAngle>().value, transform.root.name);
        return angle;
    }

    public half SyncAngle()
    {
        GameNodeCharacterAngle angle;
        angle.value = this.GetComponentData<GameNodeAngle>().value;
        this.SetComponentData(angle);
        return angle.value;
        /*var gameObjectEntity = this.gameObjectEntity;
        EntityManager entityManager = gameObjectEntity.entityManager;
        if (entityManager != null && entityManager.IsCreated)
        {
            Entity entity = gameObjectEntity.entity;
            if (entityManager.HasComponent<GameNodeAngle>(entity) && entityManager.HasComponent<GameNodeCharacterAngle>(entity) && entityManager.HasComponent<GameNodeCharacterStatus>(entity))
            {
                switch(entityManager.GetComponentData<GameNodeCharacterStatus>(entity).value)
                {
                    case GameNodeCharacterStatus.Status.Touch:
                    case GameNodeCharacterStatus.Status.Slide:
                    case GameNodeCharacterStatus.Status.Step:
                        {
                            GameNodeCharacterAngle angle;
                            angle.value = entityManager.GetComponentData<GameNodeAngle>(entity).value;
                            this.SetComponentData(angle);

                            return angle.value;
                        }
                    default:
                        {
                            GameNodeAngle angle;
                            angle.value = entityManager.GetComponentData<GameNodeCharacterAngle>(entity).value;
                            this.SetComponentData(angle);

                            return angle.value;
                        }
                }
            }
        }
        
        throw new InvalidOperationException();*/
    }

    public void SetAngle(in half value, in half characterValue)
    {
        GameNodeAngle angle;
        angle.value = value;
        this.SetComponentData(angle);

        GameNodeCharacterAngle characterAngle;
        characterAngle.value = characterValue;
        this.SetComponentData(characterAngle);
    }

    public void SetAngle(EntityCommander commander, in half value)
    {
        Entity entity = base.entity;

        GameNodeCharacterAngle characterAngle;
        characterAngle.value = value;
        commander.SetComponentData(entity, characterAngle);
    }

    public void SetAngle(EntityCommander commander, in half value, in half characterValue)
    {
        Entity entity = base.entity;

        GameNodeAngle angle;
        angle.value = value;
        commander.SetComponentData(entity, angle);

        GameNodeCharacterAngle characterAngle;
        characterAngle.value = characterValue;
        commander.SetComponentData(entity, characterAngle);
    }

    public void SetStatus(EntityCommander commander, in GameNodeCharacterStatus value)
    {
        commander.SetComponentData(entity, value);
    }

    public void SetVelocity(EntityCommander commander, in PhysicsVelocity value)
    {
        Entity entity = base.entity;

        commander.SetComponentData(entity, value);

        if (_isKinematic)
        {
            GameNodeCharacterVelocity velocity;
            velocity.value = value.Linear;
            commander.SetComponentData(entity, velocity);
        }
    }

    public void SetPosition(EntityCommander commander, in float3 value)
    {
        Translation translation;
        translation.Value = value;
        commander.SetComponentData(entity, translation);
    }

    public void Clear(EntityCommander commander)
    {
        Entity entity = base.entity;

        commander.SetComponentData(entity, (GameNodeCharacterStatus)0);

        if (_isKinematic)
            commander.SetComponentData(entity, default(GameNodeCharacterVelocity));
        else
            commander.SetComponentData(entity, default(PhysicsVelocity));
    }

    [EntityComponents]
    public List<Type> entityComponentTypesEx
    {
        get
        {
            var types = new List<Type>();
            if(_isKinematic)
                types.Add(typeof(GameNodeCharacterDesiredVelocity));

            if (_shape != null)
            {
                types.Add(typeof(GameNodeCharacterCollider));
                types.Add(typeof(GameNodeCharacterCenterOfMass));
            }

            return types;
        }
    }

    public override void Init(in Entity entity, EntityComponentAssigner assigner)
    {
        base.Init(entity, assigner);

        GameNodeCharacterSurface surface;
        surface.layerMask = 0;
        surface.fraction = 1.0f;
        surface.normal = math.up();
        surface.velocity = float3.zero;
        surface.rotation = quaternion.identity;
        assigner.SetComponentData(entity, surface);

        if (_shape != null)
        {
            GameNodeCharacterCollider collider;
            collider.value = _shape.colliders.value;
            assigner.SetComponentData(entity, collider);
        }
    }
}
