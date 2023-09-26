using System;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using ZG;

[Serializable]
public struct GameTimeActionFactor : IComponentData
{
    public float value;
}

[Serializable, InternalBufferCapacity(2)]
public struct GameTimeAction : IBufferElementData
{
    public int index;
    public int assetIndex;
    public float time;
    public float chance;
    public float3 spawnOffset;
}

[Serializable, InternalBufferCapacity(2)]
public struct GameTimeActionElapsedTime : IBufferElementData
{
    public float value;
}

[EntityComponent(typeof(GameTimeActionFactor))]
[EntityComponent(typeof(GameTimeAction))]
[EntityComponent(typeof(GameTimeActionElapsedTime))]
public class GameTimeActorComponent : EntityProxyComponent, IEntityComponent
{
#if UNITY_EDITOR
    public GameActorDatabase database;
#endif

    [Serializable]
    public struct Action
    {
        public int index;
        [Index("database.assets", pathLevel = 2)]
        public int assetIndex;
        public float chance;
        public float time;
        public float3 spawnOffset;

        public static implicit operator GameTimeAction(Action x)
        {
            GameTimeAction result;
            result.index = x.index;
            result.assetIndex = x.assetIndex;
            result.time = x.time;
            result.chance = x.chance;
            result.spawnOffset = x.spawnOffset;

            return result;
        }
    }
    
    internal float _factor = 1.0f;
    
    public Action[] _actions = null;
    
    public float factor
    {
        get
        {
            return _factor;
        }

        set
        {

            if (Mathf.Approximately(_factor, value))
                return;

            GameTimeActionFactor factor;
            factor.value = value;
            this.SetComponentData(factor);

            _factor = value;
        }
    }
    
    void IEntityComponent.Init(in Entity entity, EntityComponentAssigner assigner)
    {
        GameTimeActionFactor factor;
        factor.value = _factor;

        assigner.SetComponentData(entity, factor);

        int numActions = _actions == null ? 0 : _actions.Length;
        if (numActions > 0)
        {
            GameTimeAction[] actions = new GameTimeAction[numActions];
            for (int i = 0; i < numActions; ++i)
                actions[i] = _actions[i];

            assigner.SetBuffer(EntityComponentAssigner.BufferOption.Override, entity, actions);
            assigner.SetBuffer(EntityComponentAssigner.BufferOption.Override, entity, new GameTimeActionElapsedTime[numActions]);
        }
    }
}