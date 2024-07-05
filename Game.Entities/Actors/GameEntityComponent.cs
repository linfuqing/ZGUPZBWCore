using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using ZG;

[Flags]
public enum GameEntityStatus
{
    KnockedOut = GameNodeStatus.DELAY | GameNodeStatus.STOP,
    Dead = GameNodeStatus.DELAY | GameNodeStatus.STOP | GameNodeStatus.OVER,

    Mask = GameNodeStatus.DELAY | GameNodeStatus.STOP | GameNodeStatus.OVER
}

public struct GameEntityNode
{
    public int camp;
    public Entity entity;

    public bool Predicate(GameActionTargetType type, GameEntityNode node)
    {
        if ((type & GameActionTargetType.Self) == GameActionTargetType.Self)
        {
            if (node.entity == entity)
                return true;
        }

        if ((type & GameActionTargetType.Ally) == GameActionTargetType.Ally)
        {
            if (node.camp == camp && node.entity != entity)
                return true;
        }

        if ((type & GameActionTargetType.Enemy) == GameActionTargetType.Enemy)
        {
            if (node.camp != camp)
                return true;
        }

        return false;
    }
}

public struct GameActionEntityArchetype : IEquatable<GameActionEntityArchetype>
{
    public FixedList64Bytes<TypeIndex> typeIndices;

    public int componentTypeCount => typeIndices.Length;

    public void Add(in TypeIndex typeIndex)
    {
        if (typeIndices.Contains(typeIndex))
            return;

        typeIndices.Add(typeIndex);
    }

    public void ToComponentTypes(ref NativeList<ComponentType> componentTypes)
    {
        ComponentType componentType;
        componentType.AccessModeType = ComponentType.AccessMode.ReadWrite;
        foreach (var typeIndex in typeIndices)
        {
            componentType.TypeIndex = typeIndex;
            componentTypes.Add(componentType);
        }
    }

    public bool Equals(GameActionEntityArchetype other)
    {
        return typeIndices.Equals(other.typeIndices);
    }

    public override int GetHashCode()
    {
        return typeIndices.GetHashCode();
    }
}

/*public struct GameActionDisabled : IComponentData
{
    public double time;
}*/

public struct GameActionData : IComponentData
{
    public int version;
    public int index;
    public int actionIndex;
    public GameDeadline time;
    public Entity entity;

    public override string ToString()
    {
        return "GameActionData(Version: " + version + ", Index: " + index + ")";
    }
}

public struct GameActionDataEx : IComponentData
{
    public int camp;
    public float3 direction;
    //public float3 offset;
    public float3 position;
    public float3 targetPosition;
    public RigidTransform transform;
    public Entity target;
    public GameActionInfo info;
    public GameAction value;
    public GameActionEntityArchetype entityArchetype;
    public BlobAssetReference<Unity.Physics.Collider> collider;
}

public struct GameEntityCampDefault : IComponentData
{
    public int value;
}

public struct GameEntityCamp : IComponentData
{
    public int value;

    public override string ToString()
    {
        return value.ToString();
    }
}

[InternalBufferCapacity(4)]
public struct GameEntityItem : IBufferElementData
{
    public int index;

    public static implicit operator GameEntityItem(int x)
    {
        GameEntityItem item;
        item.index = x;
        return item;
    }
}

[EntityComponent(typeof(GameEntityCampDefault))]
[EntityComponent(typeof(GameEntityCamp))]
[EntityComponent(typeof(GameEntityItem))]
public class GameEntityComponent : EntityProxyComponent, IEntityComponent
{
#if UNITY_EDITOR
    [CSVField(CSVFieldFlag.OverrideNearestPrefab)]
    public int 阵营
    {
        set
        {
            _camp = value;
        }
    }
#endif

    [SerializeField]
    internal int _camp;

    [SerializeField]
    internal int[] _itemIndices;

    public int campDefault => _camp;

    public int camp
    {
        get
        {
            return this.GetComponentData<GameEntityCamp>().value;
        }

        set
        {
            GameEntityCamp camp;
            camp.value = value;
            this.SetComponentData(camp);
        }
    }

    public int itemCount
    {
        get
        {
            return _itemIndices == null ? 0 : _itemIndices.Length;
        }

        set
        {
            int length = _itemIndices == null ? 0 : _itemIndices.Length;
            Array.Resize(ref _itemIndices, value);
            for (int i = length; i < value; ++i)
                _itemIndices[i] = -1;
        }
    }

    public GameEntityItem[] items
    {
        get
        {
            return __GetItemIndices(_itemIndices);
        }
    }

    public void Set(int index, int itemIndex)
    {
        int length = _itemIndices == null ? 0 : _itemIndices.Length;
        if (length < index + 1)
        {
            Array.Resize(ref _itemIndices, index + 1);

            for (int i = length; i < index; ++i)
                _itemIndices[i] = -1;
        }

        _itemIndices[index] = itemIndex;

        this.SetBuffer(__GetItemIndices(_itemIndices));
    }

    public void Set(EntityCommander commander, int index, int itemIndex)
    {
        int length = _itemIndices == null ? 0 : _itemIndices.Length;
        if (length < index + 1)
        {
            Array.Resize(ref _itemIndices, index + 1);

            for (int i = length; i < index; ++i)
                _itemIndices[i] = -1;
        }

        _itemIndices[index] = itemIndex;

        commander.SetBuffer(entity, __GetItemIndices(_itemIndices));
    }

    void IEntityComponent.Init(in Entity entity, EntityComponentAssigner assigner)
    {
        if (this.GetFactory().GetEntity(entity, true) != Entity.Null)
            return;

        GameEntityCampDefault campDefault;
        campDefault.value = _camp;
        assigner.SetComponentData(entity, campDefault);

        GameEntityCamp camp;
        camp.value = _camp;
        assigner.SetComponentData(entity, camp);

        //assigner.SetBuffer(EntityComponentAssigner.BufferOption.Override, entity, __GetItemIndices(_itemIndices));
    }

    private static GameEntityItem[] __GetItemIndices(int[] values)
    {
        int numValues = values == null ? 0 : values.Length;

        var items = new GameEntityItem[numValues];
        for (int i = 0; i < numValues; ++i)
            items[i] = values[i];

        return items;
    }
}

public abstract class GameEntityComponentEx : EntityProxyComponent
{
    public abstract IReadOnlyCollection<TypeIndex> actionEntityArchetype { get; }
}