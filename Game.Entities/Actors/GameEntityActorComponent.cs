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
        Perform = 0x02,
        //Performed = 0x04 | Perform,
        Damage = 0x04,
        Damaged = 0x08 | Damage,
        Break = 0x10,
        Destroy = 0x20 | Perform,
        Destroied = Break | Destroy,
        Managed = 0x40 | Created
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
    public float3 normal;
    public Entity target;

    public override string ToString()
    {
        return "GameActionEntity(hit: " + hit + ", delta: " + delta + ", elapsedTime " + elaspedTime + ", target: " + target + ")";
    }
}

public struct GameEntityHit : IComponentData, IEquatable<GameEntityHit>
{
    public float delta;
    public float value;
    public GameDeadline time;
    public float3 normal;

    public bool Equals(GameEntityHit other)
    {
        return value == other.value && time == other.time && normal.Equals(other.normal);
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

    public static void Break(
        in GameDeadline time,
        in DynamicBuffer<GameEntityAction> entityActions,
        ref ComponentLookup<GameActionStatus> states)
    {
        int length = entityActions.Length;
        Entity entity;
        GameActionStatus status;
        for (int i = 0; i < length; ++i)
        {
            entity = entityActions[i].entity;
            if (!states.HasComponent(entity))
                continue;

            status = states[entity];
            if ((status.value & GameActionStatus.Status.Perform) == 0 || status.time < time)
                continue;

            status.time = time;
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

    public GameDeadline time;

    public float3 forward;

    public float3 distance;

    //public float3 offset;

    public Entity entity;

    public Entity commander;

    public bool Equals(GameEntityActionInfo other)
    {
        //Do not need commandVersion
        return version == other.version &&
            index == other.index &&
            hit == other.hit &&
            time == other.time &&
            forward.Equals(other.forward) &&
            distance.Equals(other.distance) &&
            //offset.Equals(other.offset) &&
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

    public double time;

    //public TimeEventHandle timeEventHandle;

    public bool Equals(GameEntityEventInfo other)
    {
        return version == other.version;// && timeEventHandle.Equals(other.timeEventHandle);
    }

    public override int GetHashCode()
    {
        return version;
    }
}

public struct GameEntityBreakInfo : IComponentData, IEquatable<GameEntityBreakInfo>
{
    public int version;

    public int delayIndex;

    public double commandTime;

    //public TimeEventHandle timeEventHandle;

    public bool Equals(GameEntityBreakInfo other)
    {
        return version == other.version;// && timeEventHandle.Equals(other.timeEventHandle);
    }

    public override int GetHashCode()
    {
        return version;
    }
}

public struct GameEntityActorInfo : IComponentData
{
    public int version;

    public GameDeadline alertTime;
}

public struct GameEntityActorTime : IComponentData
{
    public uint actionMask;
    public GameDeadline value;

    public bool Did(uint actorMask, double time)
    {
        return (actorMask & actionMask) != 0 || time > value;
    }
}

public struct GameEntityActorHit : IComponentData
{
    public int sourceTimes;
    public int destinationTimes;
    public float sourceHit;
    public float destinationHit;
}

public struct GameEntityActorHitTarget : IBufferElementData
{
    public Entity entity;
}

[InternalBufferCapacity(2)]
public struct GameEntityActorActionData : IBufferElementData
{
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
    [Tooltip("精准度")]
    public float accuracy;

    [Tooltip("打断时间")]
    public float delayTime;

    [Tooltip("被打断之后过多久才能被再次打断")]
    public float alertTime;

    [Tooltip("攻击碰撞体和范围的缩放")]
    [UnityEngine.Serialization.FormerlySerializedAs("radiusScale")]
    public float rangeScale;

    [Tooltip("攻击距离的缩放")]
    public float distanceScale;

    [Tooltip("攻击偏移的缩放")]
    public float3 offsetScale;
}

[Serializable]
public struct GameEntityActorDelay : IBufferElementData
{
    [Flags]
    public enum Flag
    {
        [Tooltip("打断后是否强制转向")]
        ForceToTurn = 0x01,
    }

    [Mask]
    public Flag flag;
    [Tooltip("要触发该状态受到消韧的最小值")]
    public int minHit;
    [Tooltip("触发该状态后“被削韧值”覆写为该值，可把该值填成负数使其成为霸体")]
    public int hitOverride;
    [Tooltip("打断时间")]
    public float delayTime;
    [Tooltip("被打断之后过多久才能被再次打断")]
    public float alertTime;
    [Tooltip("固定击退（失衡）速度")]
    public float speed;
    [Tooltip("失衡时间")]
    public float duration;
    [Tooltip("失衡开始时间")]
    public float startTime;
}

public struct GameEntityActorMass : IComponentData
{
    public float inverseValue;
}

public struct GameEntityCommandVersion : IComponentData
{
    public int value;
}

public struct GameEntityCallbackHandle : IComponentData
{
    public int version;
    public CallbackHandle value;

    public static void Command(
        int index, 
        ref NativeArray<GameEntityCallbackHandle> callbackHandles, 
        ref SharedList<CallbackHandle>.ParallelWriter results)
    {
        var callbackHandle = callbackHandles[index];
        if (!callbackHandle.value.Equals(CallbackHandle.Null))
        {
            results.AddNoResize(callbackHandle.value);

            callbackHandle.value = CallbackHandle.Null;
            callbackHandles[index] = callbackHandle;
        }
    }
}

public struct GameEntityEventCommand : IComponentData, IEnableableComponent
{
    public int version;
    public float performTime;
    public float coolDownTime;
    public GameDeadline time;
}

public struct GameEntityActionCommand : IComponentData, IEnableableComponent
{
    public int version;
    public int index;
    public GameDeadline time;
    public Entity entity;
    public float3 forward;
    public float3 distance;
    //public float3 offset;
}

public struct GameEntityBreakCommand : IComponentData, IEnableableComponent
{
    public int version;
    public float hit;
    public float alertTime;
    public float delayTime;
    public GameDeadline time;
    public float3 normal;
}

public struct GameEntityActionCommander : IComponentData, IEnableableComponent
{
    public Entity entity;
}

public struct GameEntityActionComponentType : IBufferElementData
{
    public TypeIndex index;
}

[EntityComponent(typeof(GameNodeDelay))]
[EntityComponent(typeof(GameEntityAction))]
[EntityComponent(typeof(GameEntityHit))]
[EntityComponent(typeof(GameEntityActorHit))]
[EntityComponent(typeof(GameEntityActorDelay))]
[EntityComponent(typeof(GameEntityActorTime))]
[EntityComponent(typeof(GameEntityActionInfo))]
[EntityComponent(typeof(GameEntityEventInfo))]
[EntityComponent(typeof(GameEntityBreakInfo))]
[EntityComponent(typeof(GameEntityActorInfo))]
[EntityComponent(typeof(GameEntityCommandVersion))]
[EntityComponent(typeof(GameEntityActorActionData))]
[EntityComponent(typeof(GameEntityActorActionInfo))]
[EntityComponent(typeof(GameEntityCallbackHandle))]
//[EntityComponent(typeof(GameEntityActorMass))]
[EntityComponent(typeof(GameEntityActionComponentType))]
[EntityComponent(typeof(GameEntityEventCommand))]
[EntityComponent(typeof(GameEntityActionCommand))]
[EntityComponent(typeof(GameEntityBreakCommand))]
[EntityComponent(typeof(GameEntityActionCommander))]
public class GameEntityActorComponent : ComponentDataProxy<GameEntityActorData>, IEntityComponent
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
    internal float _rage;

    [SerializeField]
    internal float _rageMax;

    [SerializeField]
    internal float _rageHitScale;

    [SerializeField]
    internal int[] _actionIndices;

    [SerializeField]
    internal GameEntityActorDelay[] _delay;

    private GameEntityComponentEx __entityComponent;

    private SharedTimeManager<CallbackHandle> __timeManager;

    public readonly static TypeIndex[] ActionComponentTypes = new TypeIndex[]
    {
        //ComponentType.ReadOnly<CollisionWorldProxy>(),
        TypeManager.GetTypeIndex<GameActionData>(),
        TypeManager.GetTypeIndex<GameActionDataEx>(),
        TypeManager.GetTypeIndex<GameActionStatus>(),
        TypeManager.GetTypeIndex<GameActionEntity>(),
        TypeManager.GetTypeIndex<PhysicsGravityFactor>(),
        TypeManager.GetTypeIndex<PhysicsVelocity>(),
        TypeManager.GetTypeIndex<Translation>(),
        TypeManager.GetTypeIndex<Rotation>()
    };

    public int commandVersion
    {
        get
        {
            return this.GetComponentData<GameEntityCommandVersion>().value;
        }
    }

    public GameEntityActorInfo actorInfo
    {
        get => this.GetComponentData<GameEntityActorInfo>();

        set => this.SetComponentData(value);
    }

    public GameEntityActorTime actorTime
    {
        get
        {
            return this.GetComponentData<GameEntityActorTime>();
        }

        set
        {
            this.SetComponentData(value);
        }
    }

    public GameEntityHit hit
    {
        get => this.GetComponentData<GameEntityHit>();

        set => this.SetComponentData(value);
    }

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
            if (Application.isPlaying)
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
        return this.TryGetBuffer(index, out GameEntityActorActionInfo value) ? value.coolDownTime : 0.0f;
    }

    public bool IsDelay(double time)
    {
        return this.GetComponentData<GameNodeDelay>().Check(time);
    }

    public int RegisterCallback(Action action)
    {
        var callbackHandle = this.GetComponentData<GameEntityCallbackHandle>();
        callbackHandle.value.Unregister();
        callbackHandle.version = commandVersion;
        callbackHandle.value = action.Register();

        this.SetComponentData(callbackHandle);

        return callbackHandle.version;
    }

    public int RegisterOrCombineCallback(Action action)
    {
        var callbackHandle = this.GetComponentData<GameEntityCallbackHandle>();
        if (action.Combine(callbackHandle.value))
            return callbackHandle.version;

        callbackHandle.version = commandVersion;
        callbackHandle.value = action.Register();

        this.SetComponentData(callbackHandle);

        return callbackHandle.version;
    }

    public int Do(
        in GameDeadline time,
        int index,
        in Entity target,
        in float3 forward,
        in float3 distance = default/*,
        in float3 offset = default*/)
    {
        GameEntityActionCommand command;
        command.version = commandVersion;
        command.index = index;
        command.time = time;
        command.entity = target;
        command.forward = forward;
        command.distance = distance;
        //command.offset = offset;

        this.SetComponentData(command);
        this.SetComponentEnabled<GameEntityActionCommand>(true);

        return command.version;
    }

    public int Do(
        EntityCommander commander,
        in GameDeadline time,
        int index,
        in Entity target,
        in float3 forward,
        in float3 distance = default/*, 
        in float3 offset = default*/)
    {
        GameEntityActionCommand command;
        command.version = this.commandVersion;
        command.index = index;
        command.time = time;
        command.entity = target;
        command.forward = forward;
        command.distance = distance;
        //command.offset = offset;

        commander.SetComponentData(entity, command);
        commander.SetComponentEnabled<GameEntityActionCommand>(entity, true);

        //commander.SetComponentData<GameEntityEventCommand>(entity, default);
        commander.SetComponentEnabled<GameEntityEventCommand>(entity, false);

        //commander.SetComponentData<GameEntityBreakCommand>(entity, default);
        commander.SetComponentEnabled<GameEntityBreakCommand>(entity, false);

        GameEntityCommandVersion commandVersion;
        commandVersion.value = command.version;
        commander.SetComponentData(entity, commandVersion);

        return command.version;
    }

    public int Do(EntityCommander commander, in GameDeadline time, float performTime, float coolDownTime, int version)
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

        command.version = version == 0 ? this.commandVersion : version;
        command.performTime = performTime;
        command.coolDownTime = coolDownTime;
        command.time = time;

        commander.SetComponentData(entity, command);

        commander.SetComponentEnabled<GameEntityEventCommand>(entity, true);
        commander.SetComponentEnabled<GameEntityActionCommand>(entity, false);
        commander.SetComponentEnabled<GameEntityBreakCommand>(entity, false);

        GameEntityCommandVersion commandVersion;
        commandVersion.value = command.version;
        commander.SetComponentData(entity, commandVersion);

        return command.version;
    }

    public int Do(in GameDeadline time, float performTime, float coolDownTime, int version)
    {
        GameEntityEventCommand command;
        /*if (this.TryGetComponentData(out command) && !command.handle.Equals(timeEventHandle) &&
            command.handle.isVail)
        {
            Debug.LogError("Force Command!");

            if (!__timeManager.isCreated)
                __timeManager = world.GetExistingSystemUnmanaged<TimeEventSystem>().manager;

            __timeManager.Cannel(command.handle);
        }*/

        command.version = version == 0 ? commandVersion : version;
        command.performTime = performTime;
        command.coolDownTime = coolDownTime;
        command.time = time;
        //command.handle = timeEventHandle;

        this.SetComponentData(command);
        this.SetComponentEnabled<GameEntityEventCommand>(true);

        return command.version;
    }

    public void Break(in GameDeadline time, float alertTime, float delayTime)
    {
        GameEntityBreakCommand command;
        command.version = commandVersion;
        command.hit = 0;
        command.alertTime = alertTime;
        command.delayTime = delayTime;
        command.time = time;
        command.normal = float3.zero;

        this.SetComponentData(command);
        this.SetComponentEnabled<GameEntityBreakCommand>(true);
    }

    public int Break(EntityCommander commander, in GameDeadline time, float alertTime, float delayTime)
    {
        GameEntityBreakCommand command;
        command.version = this.commandVersion;
        command.hit = 0;
        command.alertTime = alertTime;
        command.delayTime = delayTime;
        command.time = time;
        command.normal = float3.zero;

        //commander.SetComponentData<GameEntityActionCommand>(entity, default);
        //commander.SetComponentData<GameEntityEventCommand>(entity, default);
        commander.SetComponentData(entity, command);
        commander.SetComponentEnabled<GameEntityBreakCommand>(entity, true);
        commander.SetComponentEnabled<GameEntityActionCommand>(entity, false);
        commander.SetComponentEnabled<GameEntityEventCommand>(entity, false);

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

        /*GameEntityCommandVersion version;
        version.value = 1;
        assigner.SetComponentData(entity, version);*/

        if (__entityComponent == null)
            __entityComponent = transform.GetComponentInParent<GameEntityComponentEx>(true);

        assigner.SetBuffer<TypeIndex, IReadOnlyCollection<TypeIndex>>(
            EntityComponentAssigner.BufferOption.AppendUnique, 
            TypeManager.GetTypeIndex<GameEntityActionComponentType>(), 
            entity, 
            __entityComponent.actionEntityArchetype);

        /*GameEntityEventInfo eventInfo;
        eventInfo.version = 0;
        eventInfo.timeEventHandle = TimeEventHandle.Null;
        assigner.SetComponentData(entity, eventInfo);*/

        /*GameEntityBreakInfo breakInfo;
        breakInfo.version = 0;
        breakInfo.delayIndex = -1;
        breakInfo.commandTime = 0.0;
        //breakInfo.timeEventHandle = TimeEventHandle.Null;
        assigner.SetComponentData(entity, breakInfo);*/

        if (_mass > math.FLT_MIN_NORMAL)
        {
            GameEntityActorMass mass;
            mass.inverseValue = 1.0f / _mass;
            assigner.SetComponentData(entity, mass);
        }
        /*else
            this.RemoveComponent<GameEntityActorMass>();*/

        var actionIndices = __GetActionIndices(_actionIndices);
        assigner.SetBuffer(EntityComponentAssigner.BufferOption.Override, entity, actionIndices);
        //assigner.SetBuffer(EntityComponentAssigner.BufferOption.Override, entity, new GameEntityActorActionInfo[actionIndices.Length]);

        if (_delay != null)
            assigner.SetBuffer(EntityComponentAssigner.BufferOption.Override, entity, _delay);
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

    /*void IEntityComponent.Init(in Entity entity, EntityComponentAssigner assigner)
    {
        GameEntityActionInfo actionInfo;
        actionInfo.commandVersion = 0;
        actionInfo.version = 0;
        actionInfo.index = -1;
        actionInfo.hit = 0.0f;
        actionInfo.time = default;
        actionInfo.forward = float3.zero;
        actionInfo.distance = float3.zero;
        //actionInfo.offset = float3.zero;
        actionInfo.entity = Entity.Null;
        actionInfo.commander = Entity.Null;
        assigner.SetComponentData(entity, actionInfo);

        if (_delay != null)
            assigner.SetBuffer(EntityComponentAssigner.BufferOption.Override, entity, _delay);
    }*/
}