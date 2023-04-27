using System;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using ZG;

public struct GameNodeParent : IComponentData
{
    //权限大于0即可以操作父级
    public int authority;
    public Entity entity;
    public RigidTransform transform;
}

/*public struct GameNodeCommander : IComponentData
{
    //public int authority;
    public Entity entity;
}*/

public struct GameNodeLookTarget : IComponentData
{
    public Entity entity;
}

public struct GameNodeDelay : IComponentData
{
    public GameDeadline time;
    public half startTime;
    public half endTime;

    public bool Check(double time)
    {
        return this.time + startTime <= time && time < this.time + endTime;
    }

    public float Clamp(double time, float deltaTime)
    {
        double startTime = this.time + this.startTime;
        if (time < startTime)
            return math.min(deltaTime, (float)(startTime - time));

        double deadline = time + deltaTime, endTime = this.time + this.endTime;
        return deadline > endTime ? math.min(deltaTime, (float)(deadline - endTime)) : 0.0f;
    }

    public void Clear(in GameDeadline time)
    {
        this.time = time;
        startTime = half.zero;
        endTime = half.zero;
    }

    public override string ToString()
    {
        return $"GameNodeDelay({(double)(time + startTime)}-{(double)(time + endTime)})";
    }
}

public struct GameNodeAngle : IComponentData
{
    public half value;
}

public struct GameNodeSurface : IComponentData
{
    public quaternion rotation;
}

public struct GameNodeDrag : IComponentData
{
    public float3 value;
    public float3 velocity;
}

public struct GameNodeDirect : IComponentData
{
    public float3 value;
}

public struct GameNodeIndirect : IComponentData
{
    public float3 value;
    public float3 velocity;

    public bool isZero => value.Equals(float3.zero) && velocity.Equals(float3.zero);
}

public struct GameNodeSpeed : IComponentData
{
    public float value;
}

[Serializable]
public struct GameNodeSpeedSection : IBufferElementData
{
    public float minSpeed;
    public float angularSpeed;
    public float pivotAngularSpeed;
    //public float pivotSpeedScale;
    public float acceleration;
    public float deceleration;

    public static GameNodeSpeedSection Scele(in GameNodeSpeedSection x, float speed, float maxSpeed)
    {
        float scale = speed / (x.minSpeed > math.FLT_MIN_NORMAL ? x.minSpeed : maxSpeed);
        GameNodeSpeedSection result = x;
        result.minSpeed *= scale;
        result.angularSpeed *= scale;
        result.pivotAngularSpeed *= scale;
        //result.pivotSpeedScale *= scale;

        return result;
    }

    public static GameNodeSpeedSection Lerp(in GameNodeSpeedSection x, in GameNodeSpeedSection y, float t)
    {
        GameNodeSpeedSection result;
        result.minSpeed = math.lerp(x.minSpeed, y.minSpeed, t);
        result.angularSpeed = math.lerp(x.angularSpeed, y.angularSpeed, t);
        result.pivotAngularSpeed = x.pivotAngularSpeed > math.FLT_MIN_NORMAL && y.pivotAngularSpeed > math.FLT_MIN_NORMAL ? 
            math.lerp(x.pivotAngularSpeed, y.pivotAngularSpeed, t) : 0.0f;// x.pivotAngularSpeed > math.FLT_MIN_NORMAL ? math.max(x.pivotAngularSpeed, y.pivotAngularSpeed) : 0.0f;
        //result.pivotSpeedScale = math.lerp(x.pivotSpeedScale, y.pivotSpeedScale, t);
        result.acceleration = math.lerp(x.acceleration, y.acceleration, t);
        result.deceleration = math.lerp(x.deceleration, y.deceleration, t);

        return result;
    }

    public static GameNodeSpeedSection Get(
        float speed, 
        float maxSpeed, 
        in DynamicBuffer<GameNodeSpeedSection> sections)
    {
        GameNodeSpeedSection section, preSection;
        int numSelections = sections.Length;
        for (int i = 1; i < numSelections; ++i)
        {
            section = sections[i];
            if (section.minSpeed > speed)
            {
                preSection = sections[i - 1];

                return Lerp(preSection, section, math.smoothstep(preSection.minSpeed, section.minSpeed, speed));
            }
        }

        return Scele(sections[numSelections - 1], speed, maxSpeed);
    }
}

public struct GameNodeSpeedScale : IComponentData, IEquatable<GameNodeSpeedScale>
{
    [SerializeField]
    internal int _version;
    [SerializeField]
    internal half _value;

    public int version
    {
        get => _version;
    }

    public half value
    {
        get => _value == half.zero ? Normal : _value;
    }

    public static readonly half Normal = new half(1.0f);

    public static void Set(half destination, half source, ref DynamicBuffer<GameNodeSpeedScaleComponent> components)
    {
        if (destination == half.zero || destination == source)
            return;

        int length = components.Length;
        for (int i = 0; i < length; ++i)
        {
            if (components[i].value == source)
            {
                components.RemoveAt(i);

                break;
            }
        }

        if (destination != Normal)
        {
            GameNodeSpeedScaleComponent component;
            component.value = destination;
            components.Add(component);
        }
    }

    public bool Apply(in DynamicBuffer<GameNodeSpeedScaleComponent> components)
    {
        half result;

        int length = components.Length;
        if (length == 1)
            result = components[0].value;
        else
        {
            float value = Normal;
            for (int i = 0; i < length; ++i)
                value *= components[i].value;

            result = (half)value;
        }

        if (result == value)
            return false;

        _value = result;

        ++_version;

        return true;
    }

    public bool Equals(GameNodeSpeedScale other)
    {
        return _version == other._version && _value == other._value;
    }

    public override int GetHashCode()
    {
        return (int)math.hash(_value);
    }
}

public struct GameNodeSpeedScaleComponent : IBufferElementData
{
    public half value;
}

public struct GameNodeVelocity : IComponentData
{
    public float value;
}

[InternalBufferCapacity(1)]
public struct GameNodeVelocityComponent : IBufferElementData
{
    public enum Mode
    {
        Direct,
        Indirect
    }

    public Mode mode;

    public float duration;

    public GameDeadline time;

    public float3 value;
}

public struct GameNodeDesiredStatus : IComponentData
{
    public enum Status
    {
        Normal, 
        Stopping,
        //StoppingToPivot, 
        Moving,
        Turning,
        Pivoting
    }

    public Status value;
    public GameDeadline time;
}

public struct GameNodeDesiredVelocity : IComponentData
{
    public float time;
    public float sign;
    public float3 value;
}

public struct GameNodeDirection : IComponentData
{
    public enum Mode
    {
        None,
        Forward,
        Backward
    }

    public Mode mode;
    public int version;
    public float3 value;

    public override string ToString()
    {
        return $"{version} : {value}";
    }
}

[InternalBufferCapacity(1)]
public struct GameNodePosition : IBufferElementData
{
    public enum Mode
    {
        Normal, 
        Circle, 
        Limit
    }

    public Mode mode;
    public int version;
    //public float distance;
    public float3 value;

    public override string ToString()
    {
        return "GameNodePosition(mode: " + mode + ", value: " + value + ")";
    }
}

public struct GameNodeStaticThreshold : IComponentData
{
    public float value;
}

public struct GameNodeStoppingDistance : IComponentData
{
    public float value;
}

public struct GameNodeVersionCommand : IComponentData
{
    public int value;
}

/*public struct GameNodeCommander : IComponentData, IEnableableComponent
{
    public Entity entity;
}*/

public struct GameNodeVersion : IComponentData, IEnableableComponent
{
    [Flags]
    public enum Type
    {
        Direction = 0x01,
        Position = 0x02
    }

    public Type type;
    public int value;
}

[RequireComponent(typeof(GameNodeStatusComponent))]
//[EntityComponent(typeof(Translation))]
//
[EntityComponent(typeof(GameNodeDelay))]
[EntityComponent(typeof(GameNodeAngle))]
[EntityComponent(typeof(GameNodeSurface))]
[EntityComponent(typeof(GameNodeDirect))]
[EntityComponent(typeof(GameNodeIndirect))]
[EntityComponent(typeof(GameNodeDrag))]
[EntityComponent(typeof(GameNodeSpeed))]
[EntityComponent(typeof(GameNodeSpeedSection))]
[EntityComponent(typeof(GameNodeSpeedScale))]
[EntityComponent(typeof(GameNodeSpeedScaleComponent))]
[EntityComponent(typeof(GameNodeVelocity))]
[EntityComponent(typeof(GameNodeVelocityComponent))]
[EntityComponent(typeof(GameNodeDirection))]
[EntityComponent(typeof(GameNodePosition))]
[EntityComponent(typeof(GameNodeStaticThreshold))]
[EntityComponent(typeof(GameNodeStoppingDistance))]
[EntityComponent(typeof(GameNodeVersionCommand))]
//[EntityComponent(typeof(GameNodeCommander))]
[EntityComponent(typeof(GameNodeVersion))]
public class GameNodeComponent : EntityProxyComponent, IEntityComponent
{
    private half __normalizedSpeed = (half)1.0f;

    [SerializeField]
    internal float _staticThreshold = 0.01f;
    [SerializeField]
    internal float _stoppingDistance = 0.2f;
    [SerializeField]
    internal float _speed = 0.2f;
    [SerializeField]
    internal GameNodeSpeedSection[] _speedSections;

    private static List<GameNodeSpeedScaleComponent> __speedScaleComponents;

    public GameNodeDelay delay
    {
        get
        {
            return this.GetComponentData<GameNodeDelay>();
        }

        set
        {
            this.SetComponentData(value);
        }
    }

    public half normalizedSpeed
    {
        get
        {
            return __normalizedSpeed;
        }

        set
        {
            if (value.value != 0u && value != __normalizedSpeed)
            {
                if (__speedScaleComponents == null)
                    __speedScaleComponents = new List<GameNodeSpeedScaleComponent>();
                else
                    __speedScaleComponents.Clear();

                WriteOnlyListWrapper<GameNodeSpeedScaleComponent, List<GameNodeSpeedScaleComponent>> wrapper;
                if(this.TryGetBuffer<GameNodeSpeedScaleComponent, List<GameNodeSpeedScaleComponent>, WriteOnlyListWrapper<GameNodeSpeedScaleComponent, List<GameNodeSpeedScaleComponent>>>(
                    ref __speedScaleComponents, 
                    ref wrapper))
                {
                    int count = __speedScaleComponents.Count;
                    for(int i = 0; i < count; ++i)
                    {
                        if(__speedScaleComponents[i].value.value == __normalizedSpeed.value)
                        {
                            __speedScaleComponents.RemoveAt(i);

                            break;
                        }
                    }
                }

                GameNodeSpeedScaleComponent speedScaleComponent;
                speedScaleComponent.value = value;
                __speedScaleComponents.Add(speedScaleComponent);

                this.SetBuffer<GameNodeSpeedScaleComponent, List<GameNodeSpeedScaleComponent>>(__speedScaleComponents);

                __normalizedSpeed = value;
            }
        }
    }

    public half overrideNormalizedSpeed
    {
        get
        {
            if(gameObjectEntity.isCreated)
                return this.GetComponentData<GameNodeSpeedScale>().value;

            return __normalizedSpeed;
        }

        set
        {
            if (value.value != 0u)
            {
                if (__speedScaleComponents == null)
                    __speedScaleComponents = new List<GameNodeSpeedScaleComponent>();
                else
                    __speedScaleComponents.Clear();

                GameNodeSpeedScaleComponent speedScaleComponent;
                speedScaleComponent.value = value;
                __speedScaleComponents.Add(speedScaleComponent);

                this.SetBuffer<GameNodeSpeedScaleComponent, List<GameNodeSpeedScaleComponent>>(__speedScaleComponents);

                __normalizedSpeed = value;
            }
        }
    }
    
    public half angle
    {
        get
        {
            return this.GetComponentData<GameNodeAngle>().value;
        }

        set
        {
            GameNodeAngle angle;
            angle.value = value;
            this.SetComponentData(angle);
        }
    }

    public quaternion surfaceRotation
    {
        get
        {
            return this.GetComponentData<GameNodeSurface>().rotation;
        }

        set
        {
            GameNodeSurface surface;
            surface.rotation = value;

            this.SetComponentData(surface);
        }
    }

    public float speed => _speed;

    public float velocity
    {
        get
        {
            return this.GetComponentData<GameNodeVelocity>().value;
        }

        set
        {
            GameNodeVelocity velocity;
            velocity.value = value;
            this.SetComponentData(velocity);
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

    public GameNodeDirection direction
    {
        get
        {
            return this.GetComponentData<GameNodeDirection>();
        }
    }

    public GameNodePosition[] positions
    {
        get
        {
            return this.GetBuffer<GameNodePosition>();
        }

        set
        {
            this.SetBuffer(value);
        }
    }

    public GameNodeVelocityComponent[] velocityComponents
    {
        get
        {
            return this.GetBuffer<GameNodeVelocityComponent>();
        }

        set
        {
            //Debug.Log($"{name} Set Velocities");

            this.SetBuffer(value);
        }
    }

    public float3 direct
    {
        get
        {
            return this.GetComponentData<GameNodeDirect>().value;
        }

        set
        {
            GameNodeDirect direct;
            direct.value = value;
            this.SetComponentData(direct);
        }
    }

    public bool IsDelay(double time)
    {
        return this.GetComponentData<GameNodeDelay>().Check(time);
    }

    public bool TryGetPosition(int index, out GameNodePosition value)
    {
        return this.TryGetBuffer(index, out value);
    }

    public void SetDelay(EntityCommander commander, in GameNodeDelay value)
    {
        commander.SetComponentData(entity, value);
    }

    public void SetSurfaceRotation(EntityCommander commander, in quaternion value)
    {
        GameNodeSurface surface;
        surface.rotation = value;

        commander.SetComponentData(entity, surface);
    }

    public void SetOverrideNormalizedSpeed(EntityCommander commander, in half value)
    {
        if (__speedScaleComponents == null)
            __speedScaleComponents = new List<GameNodeSpeedScaleComponent>();
        else
            __speedScaleComponents.Clear();

        GameNodeSpeedScaleComponent speedScaleComponent;
        speedScaleComponent.value = value;
        __speedScaleComponents.Add(speedScaleComponent);

        commander.SetBuffer<GameNodeSpeedScaleComponent, List<GameNodeSpeedScaleComponent>>(entity, __speedScaleComponents);
    }

    public void SetVelocityComponents(EntityCommander commander, GameNodeVelocityComponent[] values)
    {
        commander.SetBuffer(entity, values);
    }

    public void SetVelocity(EntityCommander commander, float value)
    {
        GameNodeVelocity velocity;
        velocity.value = value;

        commander.SetComponentData(entity, velocity);
    }

    public void SetDirect(
        EntityCommander commander,
        float3 value)
    {
        GameNodeDirect direct;
        direct.value = value;
        commander.SetComponentData(entity, direct);
    }

    public int SetDirection(
        EntityCommander commander, 
        float3 value, 
        int version = -1, 
        GameNodeDirection.Mode mode = GameNodeDirection.Mode.Forward)
    {
        /*float length = math.length(direction);
        if (length > 1.0f)
            direction /= length;*/

        version = version < 0 ? UpdateVersion(commander, GameNodeVersion.Type.Direction) : version;

        /*if(__syncSystemGroup == null)
            __syncSystemGroup = world.GetExistingSystem<GameSyncSystemGroup>();

        Debug.Log($"{world.Name} : {transform.root.name} : {direction} : {__syncSystemGroup.syncFrameIndex}");*/

        Entity entity = base.entity;

        GameNodeDirection direction;
        direction.mode = mode;
        direction.version = version;
        direction.value = value;
        commander.SetComponentData(entity, direction);

        commander.SetBuffer<GameNodePosition>(entity);

        return version;
    }

    public int SetDirection(in float3 value, int version = -1, GameNodeDirection.Mode mode = GameNodeDirection.Mode.Forward)
    {
        /*float length = math.length(direction);
        if (length > 1.0f)
            direction /= length;*/

        version = version < 0 ? UpdateVersion(GameNodeVersion.Type.Direction) : version;

        /*if(__syncSystemGroup == null)
            __syncSystemGroup = world.GetExistingSystem<GameSyncSystemGroup>();

        Debug.Log($"{world.Name} : {transform.root.name} : {direction} : {__syncSystemGroup.syncFrameIndex}");*/

        GameNodeDirection direction;
        direction.mode = mode;
        direction.version = version;
        direction.value = value;
        this.SetComponentData(direction);

        this.SetBuffer<GameNodePosition>();

        return version;
    }
    
    public int SetPosition(in float3 value, GameNodePosition.Mode mode = GameNodePosition.Mode.Normal, bool isClear = true)
    {
        //Debug.Log($"{world.Name} : {transform.root.name} : {value}");

        int version = UpdateVersion(GameNodeVersion.Type.Position);

        GameNodeDirection direction;
        direction.mode = GameNodeDirection.Mode.None;
        direction.version = version;
        direction.value = float3.zero;
        this.SetComponentData(direction);
        
        GameNodePosition position;
        position.mode = mode;
        position.version = version;
        //position.distance = distance;
        position.value = value;

        if (isClear)
            this.SetBuffer(position);
        else
            this.AppendBuffer(position);
        /*if (system != null)
            system.MoveTo(entity, position, type == MoveType.Force);*/

        return version;
    }

    public int SetPosition(
        EntityCommander commander, 
        in float3 value, 
        GameNodePosition.Mode mode = GameNodePosition.Mode.Normal, 
        bool isClear = true)
    {
        //Debug.Log($"{world.Name} : {transform.root.name} : {value}");

        int version = UpdateVersion(commander, GameNodeVersion.Type.Position);

        Entity entity = base.entity;

        GameNodeDirection direction;
        direction.mode = GameNodeDirection.Mode.None;
        direction.version = version;
        direction.value = float3.zero;
        commander.SetComponentData(entity, direction);

        GameNodePosition position;
        position.mode = mode;
        position.version = version;
        //position.distance = distance;
        position.value = value;

        if (isClear)
            commander.SetBuffer(entity, position);
        else
            commander.AppendBuffer(entity, position);
        /*if (system != null)
            system.MoveTo(entity, position, type == MoveType.Force);*/

        return version;
    }

    public void ClearPositions(EntityCommander commander)
    {
        commander.SetBuffer<GameNodePosition>(entity);
    }

    public void ClearPositions()
    {
        this.SetBuffer<GameNodePosition>();
    }

    public void Clear(EntityCommander commander)
    {
        Entity entity = base.entity;

        GameNodeVelocity velocity;
        velocity.value = 0.0f;
        commander.SetComponentData(entity, velocity);

        GameNodeSurface surface;
        surface.rotation = quaternion.identity;
        commander.SetComponentData(entity, surface);

        GameNodeDirect direct;
        direct.value = float3.zero;
        commander.SetComponentData(entity, direct);

        SetDirection(commander, 0.0f);

        commander.SetComponentData<GameNodeDelay>(entity, default);

        commander.SetBuffer<GameNodeVelocityComponent>(entity);
    }

    public int UpdateVersion(GameNodeVersion.Type type)
    {
        var gameObjectEntity = base.gameObjectEntity;
        var versionCommand = gameObjectEntity.GetComponentData<GameNodeVersionCommand>();
        var version = gameObjectEntity.GetComponentData<GameNodeVersion>();
        version.type = type;
        version.value = math.max(version.value, versionCommand.value) + 1;
        gameObjectEntity.SetComponentData(version);

        gameObjectEntity.SetComponentEnabled<GameNodeVersion>(true);

        return version.value;
    }

    public int UpdateVersion(EntityCommander commander, GameNodeVersion.Type type)
    {
        var gameObjectEntity = base.gameObjectEntity;
        var version = gameObjectEntity.GetComponentData<GameNodeVersion>();
        var versionCommand = gameObjectEntity.GetComponentData<GameNodeVersionCommand>();
        versionCommand.value = math.max(versionCommand.value, version.value) + 1;
        gameObjectEntity.SetComponentData(versionCommand);

        version.type = type;
        version.value = versionCommand.value;
        commander.SetComponentData(entity, version);

        commander.SetComponentEnabled<GameNodeVersion>(entity, true);

        return versionCommand.value;
    }

    void IEntityComponent.Init(in Entity entity, EntityComponentAssigner assigner)
    {
        GameNodeSurface surface;
        surface.rotation = quaternion.identity;
        assigner.SetComponentData(entity, surface);

        GameNodeStaticThreshold staticThreshold;
        staticThreshold.value = _staticThreshold;
        assigner.SetComponentData(entity, staticThreshold);

        GameNodeStoppingDistance stoppingDistance;
        stoppingDistance.value = _stoppingDistance;
        assigner.SetComponentData(entity, stoppingDistance);

        GameNodeSpeed speed;
        speed.value = _speed;
        assigner.SetComponentData(entity, speed);

        /*_speedSections[0].pivotAngularSpeed = _speedSections[1].pivotAngularSpeed;

        var list = new List<GameNodeSpeedSection>(_speedSections);

        list.Insert(0, new GameNodeSpeedSection()
        {
            minSpeed = -1.0f,
            angularSpeed = value.angularSpeed,
            pivotAngularSpeed = 0.0f,
            acceleration = 0.5f,
            deceleration = 2.0f
        });

        var f = list[list.Count - 1];

        f.minSpeed = 5.0f;
        f.pivotAngularSpeed = 0.0f;
        list.Add(f);

        _speedSections = list.ToArray();*/

        assigner.SetBuffer(true, entity, _speedSections);

        if (__normalizedSpeed != 1.0f)
        {
            if (__speedScaleComponents == null)
                __speedScaleComponents = new List<GameNodeSpeedScaleComponent>();
            else
                __speedScaleComponents.Clear();

            GameNodeSpeedScaleComponent speedScaleComponent;
            speedScaleComponent.value = __normalizedSpeed;
            __speedScaleComponents.Add(speedScaleComponent);

            assigner.SetBuffer<GameNodeSpeedScaleComponent, List<GameNodeSpeedScaleComponent>>(true, entity, __speedScaleComponents);
        }
    }

    /*protected new void OnValidate()
    {
        if (_speedSections == null || _speedSections.Length < 2)
        {
            _speed = value.speed;

            _speedSections = new GameNodeSpeedSection[1];

            _speedSections[0] = new GameNodeSpeedSection()
            {
                minSpeed = 0.0f,
                angularSpeed = value.angularSpeed,
                pivotAngularSpeed = 0.0f,
                //pivotSpeed = value.acceleration,
                acceleration = value.acceleration,
                deceleration = value.acceleration
            };
        }

        base.OnValidate();
    }*/
}