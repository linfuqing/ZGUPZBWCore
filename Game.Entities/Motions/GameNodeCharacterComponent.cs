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
        SurfaceUp = 0x01,
        RotationAll = 0x02, 
        CanSwim = 0x04, 
        CanFly = 0x08, 
        //AirControl = 0x010, 
        FlyOnly = 0x18
    }

    [Mask]
    public Flag flag;

    public LayerMask climbMask;

    public int maxIterations;
    public float maxMovementSpeed;
    public float contactTolerance;
    public float skinWidth;
    public float headLength;
    public float footHeight;
    public float staticSlope;
    public float stepSlope;
    public float stepFraction;
    public float supportFraction;
    [UnityEngine.Serialization.FormerlySerializedAs("stepDeep")]
    public float raycastLength;

    [UnityEngine.Serialization.FormerlySerializedAs("stepHeight")]
    public float waterMinHeight;

    [UnityEngine.Serialization.FormerlySerializedAs("waterHeight")]
    public float waterMaxHeight;
    
    public float waterDamping;

    public float climbDamping;

    public float airDamping;

    public float airSpeed;

    public float angluarSpeed;

    public float gravityFactor;

    public float buoyancy;
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
