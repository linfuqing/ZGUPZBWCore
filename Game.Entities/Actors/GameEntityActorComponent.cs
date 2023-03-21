using System;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Transforms;
using Unity.Physics;
using UnityEngine;
using ZG;
using System.Collections.Generic;

public struct GameActionItemSetDefinition
{
    public BlobArray<GameActionInfo> values;
}

public struct GameActionSetDefinition
{
    public struct Action
    {
        public GameActionInfo info;

        public GameAction instance;

        public int colliderIndex;
    }

    public int instanceID;
    public BlobArray<Action> values;
}

public struct GameActionSetData : IComponentData
{
    public BlobAssetReference<GameActionSetDefinition> definition;
}

public struct GameActionItemSetData : IComponentData
{
    public BlobAssetReference<GameActionItemSetDefinition> definition;
}

public struct GameActionStatus : IComponentData
{
    [Flags]
    public enum Status
    {
        Created = 0x01,
        //Perform = 0x02,
        //Performed = 0x04 | Perform,
        Damage = 0x02,
        Damaged = 0x04 | Damage, 
        Break = 0x08,
        Destroy = 0x10,
        Destroied = Break | Destroy,
        Managed = 0x20//0x30
    }
    
    public Status value;
    public GameDeadline time;
}

[InternalBufferCapacity(1)]
public struct GameActionEntity : IBufferElementData
{
    public float hit;
    public float delta;
    public float elaspedTime;
    public Entity target;

    public override string ToString()
    {
        return "GameActionEntity(hit: " + hit + ", delta: " + delta + ", elapsedTime " + elaspedTime + ", target: " + target + ")";
    }
}

public struct GameEntityHit : IComponentData, IEquatable<GameEntityHit>
{
    public float value;
    public GameDeadline time;

    public bool Equals(GameEntityHit other)
    {
        return value == other.value && time == other.time;
    }

    public override int GetHashCode()
    {
        return value.GetHashCode();
    }
}

[InternalBufferCapacity(1)]
public struct GameEntityAction : ICleanupBufferElementData
{
    public Entity entity;

    public static implicit operator GameEntityAction(Entity entity)
    {
        GameEntityAction action;
        action.entity = entity;
        return action;
    }

    public static implicit operator Entity(GameEntityAction action)
    {
        return action.entity;
    }

    public static void Break(
        in DynamicBuffer<GameEntityAction> entityActions, 
        ref ComponentLookup<GameActionStatus> states)
    {
        int length = entityActions.Length;
        Entity entity;
        GameActionStatus status;
        for (int i = 0; i < length; ++i)
        {
            entity = entityActions[i];
            if (!states.HasComponent(entity))
                continue;

            status = states[entity];
            if ((status.value & GameActionStatus.Status.Damage) == GameActionStatus.Status.Damage ||
                (status.value & GameActionStatus.Status.Destroy) == GameActionStatus.Status.Destroy)
                continue;

            status.value |= GameActionStatus.Status.Destroy;

            states[entity] = status;
        }
    }
}

public struct GameEntityActionInfo : ICleanupComponentData, IEquatable<GameEntityActionInfo>
{
    public int commandVersion;

    public int version;

    public int index;

    public float hit;

    public double time;
    
    public float3 forward;

    public float3 distance;

    public Entity entity;

    public bool Equals(GameEntityActionInfo other)
    {
        //Do not need commandVersion
        return version == other.version &&
            index == other.index &&
            hit == other.hit && 
            time == other.time &&
            forward.Equals(other.forward) &&
            distance.Equals(other.distance) &&
            entity == other.entity;
    }

    public override int GetHashCode()
    {
        return entity.GetHashCode();
    }
}

public struct GameEntityEventInfo : IComponentData, IEquatable<GameEntityEventInfo>
{
    public int version;

    public TimeEventHandle timeEventHandle;

    public bool Equals(GameEntityEventInfo other)
    {
        return version == other.version && timeEventHandle.Equals(other.timeEventHandle);
    }

    public override int GetHashCode()
    {
        return version;
    }
}

public struct GameEntityBreakInfo : IComponentData, IEquatable<GameEntityBreakInfo>
{
    public int version;

    public double commandTime;
    
    public TimeEventHandle timeEventHandle;

    public bool Equals(GameEntityBreakInfo other)
    {
        return version == other.version && timeEventHandle.Equals(other.timeEventHandle);
    }

    public override int GetHashCode()
    {
        return version;
    }
}

//TODO: Sync
public struct GameEntityActorInfo : IComponentData
{
    public int version;

    public GameDeadline alertTime;
}

public struct GameEntityActorTime : IComponentData
{
    public GameDeadline value;
}

public struct GameEntityActorHit : IComponentData
{
    public int sourceTimes;
    public int destinationTimes;
    public float sourceHit;
    public float destinationHit;
}

[InternalBufferCapacity(2)]
public struct GameEntityActorActionData : IBufferElementData
{
    public int activeCount;
    public int actionIndex;
}

[InternalBufferCapacity(2)]
public struct GameEntityActorActionInfo : IBufferElementData
{
    public GameDeadline coolDownTime;
}

[Serializable]
public struct GameEntityActorData : IComponentData
{
    public float accuracy;

    public float delayTime;

    public float alertTime;

    [UnityEngine.Serialization.FormerlySerializedAs("radiusScale")]
    public float rangeScale;

    public float distanceScale;

    public float3 offsetScale;
}

public struct GameEntityActorMass : IComponentData
{
    public float inverseValue;
}

public struct GameEntityCommandVersion : IComponentData
{
    public int value;
}

public struct GameEntityEventCommand : IComponentData
{
    public int version;
    public float performTime;
    public float coolDownTime;
    public GameDeadline time;
    public TimeEventHandle handle;
}

public struct GameEntityActionCommand : IComponentData
{
    public int version;
    public int index;
    public GameDeadline time;
    public Entity entity;
    public float3 forward;
    public float3 distance;
}

public struct GameEntityBreakCommand : IComponentData
{
    public int version;
    public float alertTime;
    public float delayTime;
    public GameDeadline time;
}

public struct GameEntityArchetype : IComponentData
{
    public EntityArchetype value;
}

[EntityComponent(typeof(GameEntityArchetype))]
[EntityComponent(typeof(GameNodeDelay))]
[EntityComponent(typeof(GameEntityAction))]
[EntityComponent(typeof(GameEntityHit))]
[EntityComponent(typeof(GameEntityActorHit))]
[EntityComponent(typeof(GameEntityActorTime))]
[EntityComponent(typeof(GameEntityActionInfo))]
[EntityComponent(typeof(GameEntityEventInfo))]
[EntityComponent(typeof(GameEntityBreakInfo))]
[EntityComponent(typeof(GameEntityActorInfo))]
[EntityComponent(typeof(GameEntityCommandVersion))]
[EntityComponent(typeof(GameEntityActorActionData))]
[EntityComponent(typeof(GameEntityActorActionInfo))]
//[EntityComponent(typeof(GameEntityActorMass))]
[EntityComponent(typeof(GameEntityEventCommand))]
[EntityComponent(typeof(GameEntityActionCommand))]
[EntityComponent(typeof(GameEntityBreakCommand))]
public class GameEntityActorComponent : ComponentDataProxy<GameEntityActorData>, IEntitySystemStateComponent
{
#if UNITY_EDITOR
    [CSVField(CSVFieldFlag.OverrideNearestPrefab)]
    public string 技能
    {
        set
        {
            if (string.IsNullOrEmpty(value))
                return;

            string[] actions = value.Split('/');
            int numActiions = actions == null ? 0 : actions.Length;
            if (numActiions < 1)
                return;

            _actionIndices = new int[numActiions];
            for (int i = 0; i < numActiions; ++i)
                _actionIndices[i] = (short)database.GetActions().IndexOf(actions[i]);
        }
    }

    [CSVField(CSVFieldFlag.OverrideNearestPrefab)]
    public float 硬直时间
    {
        set
        {
            GameEntityActorData temp = base.value;
            temp.delayTime = value;
            temp.alertTime = 5.0f;
            base.value = temp;
        }
    }

    public GameActorDatabase database;
#endif
    [SerializeField]
    internal float _mass = 0.0f;

    [SerializeField]
    internal int[] _actionIndices;
    
    private GameEntityComponentEx __entityComponent;

    private TimeEventSystem __timeEventSystem;

    public static ComponentType[] actionComponentTypes
    {
        get
        {
            return new ComponentType[]
            {
                //ComponentType.ReadOnly<CollisionWorldProxy>(),
                ComponentType.ReadOnly<GameActionData>(),
                ComponentType.ReadOnly<GameActionDataEx>(),
                ComponentType.ReadWrite<GameActionStatus>(),
                ComponentType.ReadWrite<GameActionEntity>(),
                ComponentType.ReadWrite<PhysicsGravityFactor>(),
                ComponentType.ReadWrite<PhysicsVelocity>(),
                ComponentType.ReadWrite<Translation>(),
                ComponentType.ReadWrite<Rotation>()
            };
        }
    }

    public int commandVersion
    {
        get
        {
            return this.GetComponentData<GameEntityCommandVersion>().value;
        }
    }

    public GameDeadline actorTime
    {
        get
        {
            return this.GetComponentData<GameEntityActorTime>().value;
        }
        
        set
        {
            GameEntityActorTime actorTime;
            actorTime.value = value;
            this.SetComponentData(actorTime);
        }
    }

    /*public GameDeadline delayTime
    {
        get
        {
            return this.GetComponentData<GameNodeDelay>().time;
        }

        set
        {
            GameNodeDelay delay;
            delay.time = value;
            this.SetComponentData(delay);
        }
    }*/
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

    public int[] actionIndices
    {
        get
        {
            return _actionIndices;
        }

        set
        {
            _actionIndices = value;

#if UNITY_EDITOR
            if(Application.isPlaying)
#endif
                this.SetBuffer(__GetActionIndices(value));
        }
    }

    private static List<GameEntityActorActionData> __actions;

    public List<GameEntityActorActionData> actions
    {
        get
        {
            if (__actions == null)
                __actions = new List<GameEntityActorActionData>();
            else
                __actions.Clear();

            WriteOnlyListWrapper<GameEntityActorActionData, List<GameEntityActorActionData>> wrapper;
            this.TryGetBuffer<GameEntityActorActionData, List<GameEntityActorActionData>, WriteOnlyListWrapper<GameEntityActorActionData, List<GameEntityActorActionData>>>(ref __actions, ref wrapper);

            return __actions;
        }

        set
        {
            this.SetBuffer<GameEntityActorActionData, List<GameEntityActorActionData>>(value);
        }
    }

    public GameEntityActorActionInfo[] actionInfos
    {
        get
        {
            return this.GetBuffer<GameEntityActorActionInfo>();
        }

        set
        {
            this.SetBuffer(value);
        }
    }

    [EntityComponents]
    public Type[] entityComponentTypesEx
    {
        get
        {
            if (_mass > math.FLT_MIN_NORMAL)
                return new Type[] { typeof(GameEntityActorMass) };

            return null;
        }
    }

    public double GetCoolDownTime(int index)
    {
        return this.GetBuffer<GameEntityActorActionInfo>(index).coolDownTime;
    }

    public bool IsDelay(double time)
    {
        return this.GetComponentData<GameNodeDelay>().Check(time);
    }

    public int Do(in GameDeadline time, int index, Entity target, float3 forward, float3 distance = default)
    {
        GameEntityActionCommand command;
        command.version = commandVersion;
        command.index = index;
        command.time = time;
        command.entity = target;
        command.forward = forward;
        command.distance = distance;
        
        this.SetComponentData(command);

        return command.version;
    }

    public int Do(EntityCommander commander, in GameDeadline time, int index, Entity target, float3 forward, float3 distance = default)
    {
        GameEntityActionCommand command;
        command.version = this.commandVersion;
        command.index = index;
        command.time = time;
        command.entity = target;
        command.forward = forward;
        command.distance = distance;

        commander.SetComponentData(entity, command);
        commander.SetComponentData<GameEntityEventCommand>(entity, default);
        commander.SetComponentData<GameEntityBreakCommand>(entity, default);

        GameEntityCommandVersion commandVersion;
        commandVersion.value = command.version;
        commander.SetComponentData(entity, commandVersion);

        return command.version;
    }

    public int Do(EntityCommander commander, in GameDeadline time, in TimeEventHandle timeEventHandle, float performTime, float coolDownTime)
    {
        GameEntityEventCommand command;
        //TODO:
        /*if (this.TryGetComponentData(out command) && !command.handle.Equals(timeEventHandle) &&
            command.handle.isVail)
        {
            Debug.LogError("Force Command!");

            if (__timeEventSystem == null)
                __timeEventSystem = world.GetExistingSystem<TimeEventSystem>();

            __timeEventSystem.Cannel(command.handle);
        }*/

        command.version = this.commandVersion;
        command.performTime = performTime;
        command.coolDownTime = coolDownTime;
        command.time = time;
        command.handle = timeEventHandle;

        commander.SetComponentData<GameEntityActionCommand>(entity, default);
        commander.SetComponentData(entity, command);
        commander.SetComponentData<GameEntityBreakCommand>(entity, default);

        GameEntityCommandVersion commandVersion;
        commandVersion.value = command.version;
        commander.SetComponentData(entity, commandVersion);

        return command.version;
    }

    public int Do(in GameDeadline time, in TimeEventHandle timeEventHandle, float performTime, float coolDownTime)
    {
        GameEntityEventCommand command;
        if (this.TryGetComponentData(out command) && !command.handle.Equals(timeEventHandle) && 
            command.handle.isVail)
        {
            Debug.LogError("Force Command!");

            if (__timeEventSystem == null)
                __timeEventSystem = world.GetExistingSystemManaged<TimeEventSystem>();

            __timeEventSystem.Cannel(command.handle);
        }

        command.version = commandVersion;
        command.performTime = performTime;
        command.coolDownTime = coolDownTime;
        command.time = time;
        command.handle = timeEventHandle;

        this.SetComponentData(command);

        return command.version;
    }

    public void Break(in GameDeadline time, float alertTime, float delayTime)
    {
        GameEntityBreakCommand command;
        command.version = commandVersion;
        command.alertTime = alertTime;
        command.delayTime = delayTime;
        command.time = time;

        this.SetComponentData(command);
    }

    public int Break(EntityCommander commander, in GameDeadline time, float alertTime, float delayTime)
    {
        GameEntityBreakCommand command;
        command.version = this.commandVersion;
        command.alertTime = alertTime;
        command.delayTime = delayTime;
        command.time = time;

        commander.SetComponentData<GameEntityActionCommand>(entity, default);
        commander.SetComponentData<GameEntityEventCommand>(entity, default);
        commander.SetComponentData(entity, command);

        GameEntityCommandVersion commandVersion;
        commandVersion.value = command.version;
        commander.SetComponentData(entity, commandVersion);

        return command.version;
    }

    public void Break(EntityCommander commander, in GameDeadline time, float delayTime)
    {
        Break(commander, time, 0.0f, delayTime);
    }

    public void Break(in GameDeadline time, float delayTime)
    {
        Break(time, 0.0f, delayTime);
    }

    public override void Init(in Entity entity, EntityComponentAssigner assigner)
    {
        base.Init(entity, assigner);

        GameEntityCommandVersion version;
        version.value = 1;
        assigner.SetComponentData(entity, version);

        if (__entityComponent == null)
            __entityComponent = transform.GetComponentInParent<GameEntityComponentEx>(true);
        
        GameEntityArchetype entityArchetype;
        entityArchetype.value = __entityComponent.actionEntityArchetype;
        assigner.SetComponentData(entity, entityArchetype);
        
        GameEntityEventInfo eventInfo;
        eventInfo.version = 0;
        eventInfo.timeEventHandle = TimeEventHandle.Null;
        assigner.SetComponentData(entity, eventInfo);

        GameEntityBreakInfo breakInfo;
        breakInfo.version = 0;
        breakInfo.commandTime = 0.0;
        breakInfo.timeEventHandle = TimeEventHandle.Null;
        assigner.SetComponentData(entity, breakInfo);

        if (_mass > math.FLT_MIN_NORMAL)
        {
            GameEntityActorMass mass;
            mass.inverseValue = 1.0f / _mass;
            assigner.SetComponentData(entity, mass);
        }
        /*else
            this.RemoveComponent<GameEntityActorMass>();*/

        var actionIndices = __GetActionIndices(_actionIndices);
        assigner.SetBuffer(true, entity, actionIndices);
        assigner.SetBuffer(true, entity, new GameEntityActorActionInfo[actionIndices.Length]);
    }
    
    private GameEntityActorActionData[] __GetActionIndices(int[] values)
    {
        int numValues = values == null ? 0 : values.Length, actionIndex;

        GameEntityActorActionData[] actions = new GameEntityActorActionData[numValues];
        for (int i = 0; i < numValues; ++i)
        {
            actionIndex = values[i];
            if (actionIndex == -1)
                Debug.LogError($"{name} Error Action {i}", this);

            actions[i].actionIndex = actionIndex;
        }

        return actions;
    }

    void IEntitySystemStateComponent.Init(in Entity entity, EntityComponentAssigner assigner)
    {
        GameEntityActionInfo actionInfo;
        actionInfo.commandVersion = 0;
        actionInfo.version = 0;
        actionInfo.index = -1;
        actionInfo.hit = 0.0f;
        actionInfo.time = 0.0;
        actionInfo.forward = float3.zero;
        actionInfo.distance = float3.zero;
        actionInfo.entity = Entity.Null;

        assigner.SetComponentData(entity, actionInfo);
    }
}