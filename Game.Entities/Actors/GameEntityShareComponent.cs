using System;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using ZG;

[Flags]
public enum GameSharedActionType
{
    None = 0x00,
    Local = 0x01,
    Normal = 0x02
}

[Flags]
public enum GameEntitySharedActionObjectFlag
{
    Source = 0x01,
    Destination = 0x02, 
    Buff = 0x04, 
    Create = 0x08, 
    Init = 0x10,
    Hit = 0x20, 
    Damage = 0x40
}

[Flags]
public enum GameActionSharedObjectFlag
{
    EnableWhenBreak = 0x01
}

public struct GameActionSharedObjectAsset
{
    public GameActionSharedObjectFlag flag;
    public float destroyTime;
    public GameObject gameObject;
}

/*public struct GameEntitySharedData : IComponentData
{
    public int cacheVersionCount;
}*/

public struct GameEntitySharedActionType : IComponentData
{
    public GameSharedActionType value;
}

public struct GameEntitySharedActionMask : IComponentData
{
    public uint value;
}

public struct GameEntitySharedHit : IBufferElementData
{
    public int version;
    public int actionIndex;
    public double time;
    public Entity entity;
}

/*public struct GameEntitySharedAction : ICleanupBufferElementData
{
    public int index;
    public int version;
    public float elapsedTime;

    public override string ToString()
    {
        return "GameEntitySharedAction(index: " + index + ", version: " + version + ", elapsed time: " + elapsedTime + ")";
    }
}*/

public struct GameEntitySharedActionData : IComponentData
{
}

public struct GameEntitySharedActionChild : ICleanupBufferElementData
{
    public Entity entity;
}

public struct GameActionSharedObject : ICleanupComponentData
{
    public int index;
    public int version;
    public GameActionSharedObjectFlag flag;
    public GameActionStatus.Status destroyStatus;
    public float destroyTime;
    public Entity actionEntity;
}

public struct GameActionSharedObjectParent : ICleanupComponentData
{
    //被放到相应的节点底下
    public Entity value;
}

public struct GameActionSharedObjectData : IComponentData
{
    public int index;
    //actionEntity
    public GameDeadline time;
    public RigidTransform transform;
    //技能父级，注意跟GameActionSharedObjectParent的区别
    public Entity parentEntity;
}

//[EntityComponent(typeof(GameEntitySharedData))]
[EntityComponent(typeof(GameEntitySharedActionType))]
[EntityComponent(typeof(GameEntitySharedActionMask))]
[EntityComponent(typeof(GameEntitySharedHit))]
//[EntityComponent(typeof(GameEntitySharedAction))]
public class GameEntityShareComponent : GameEntityComponentEx, IEntityComponent
{
    [UnityEngine.SerializeField]
    [UnityEngine.Tooltip("跟客户端技能效果对应，最后三层不可用")]
    internal UnityEngine.LayerMask _actionMask;

    //[UnityEngine.SerializeField]
    //internal int _cacheVersionCount = 4;
    
    //private EntityArchetype __actionEntityArchetype;

    /*public override EntityArchetype actionEntityArchetype
    {
        get
        {
            if (!__actionEntityArchetype.Valid)
            {
                List<ComponentType> componentTypes = new List<ComponentType>(GameEntityActorComponent.actionComponentTypes);
                componentTypes.Add(ComponentType.ReadWrite<GameTransformVelocity<GameTransform, GameTransformVelocity>>());
                componentTypes.Add(ComponentType.ReadOnly<EntityObjects>());
                componentTypes.Add(ComponentType.ReadOnly<GameEntitySharedActionData>());
                __actionEntityArchetype = entityManager.CreateArchetype(componentTypes.ToArray());
            }

            return __actionEntityArchetype;

        }
    }*/

    public override IReadOnlyCollection<TypeIndex> actionEntityArchetype
    {
        get
        {
            return new List<TypeIndex>(GameEntityActorComponent.ActionComponentTypes)
            {
                TypeManager.GetTypeIndex<EntityObjects>(),
                TypeManager.GetTypeIndex<GameTransformVelocity<GameTransform, GameTransformVelocity>>(),
                TypeManager.GetTypeIndex<GameEntitySharedActionData>()
            };
        }
    }

    public GameSharedActionType actionType
    {
        get
        {
            return this.GetComponentData<GameEntitySharedActionType>().value;
        }

        set
        {
            GameEntitySharedActionType actionType;
            actionType.value = value;
            this.SetComponentData(actionType);
        }
    }

    void IEntityComponent.Init(in Entity entity, EntityComponentAssigner assigner)
    {
        /*GameEntitySharedData instance;
        instance.cacheVersionCount = _cacheVersionCount;
        assigner.SetComponentData(entity, instance);*/

        GameEntitySharedActionMask actionMask;
        actionMask.value = (uint)_actionMask.value;
        assigner.SetComponentData(entity, actionMask);
    }
}
