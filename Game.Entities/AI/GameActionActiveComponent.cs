using System;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using ZG;

public enum GameActionConditionResult
{
    OK,
    OutOfGroup,
    //OutOfActorTime,
    OutOfActorStatus, 
    OutOfSpeed, 
    CoolDown,
    NotTheNext
}

[Serializable]
public struct GameActionConditionAction : IBufferElementData
{
    public int index;

    public static implicit operator GameActionConditionAction(int index)
    {
        GameActionConditionAction action;
        action.index = index;
        return action;
    }
}

[Serializable]
public struct GameActionCondition : IBufferElementData
{
    public int actionIndex;
    public int groupMask;
    public int preActionStartIndex;
    public int preActionCount;

    public int actorStatusMask;

    public float maxSpeed;

    public GameActionConditionResult Did(
        //in GameEntityActorTime actorTime,
        double time,
        float actorVelocity,
        //uint actorMask,
        int actorStatus,
        int preActionIndex,
        ref int groupMask,
        in DynamicBuffer<GameEntityActorActionInfo> actionInfos,
        in DynamicBuffer<GameActionConditionAction> actions)
    {
        if (groupMask != 0 && (groupMask & this.groupMask) == 0)
            return GameActionConditionResult.OutOfGroup;

        /*if (!actorTime.Did(actorMask, time))
            return GameActionConditionResult.OutOfActorTime;*/

        if (((actorStatusMask == 0 ? 1 : actorStatusMask) & (1 << actorStatus)) == 0)
            return GameActionConditionResult.OutOfActorStatus;

        if (maxSpeed > math.FLT_MIN_NORMAL && maxSpeed < actorVelocity)
            return GameActionConditionResult.OutOfSpeed;

        if (actionInfos[actionIndex].coolDownTime > time)
            return GameActionConditionResult.CoolDown;

        if (preActionIndex != -1 && actions.IsCreated)
        {
            for (int i = 0; i < preActionCount; ++i)
            {
                if (actions[i + preActionStartIndex].index == preActionIndex)
                {
                    if (groupMask == 0)
                        groupMask |= this.groupMask;

                    return GameActionConditionResult.OK;
                }
            }
        }
        else if(preActionCount < 1)
        {
            if (groupMask == 0)
                groupMask |= this.groupMask;

            return GameActionConditionResult.OK;
        }

        return GameActionConditionResult.NotTheNext;
    }

    public static GameActionConditionResult Did(
        //in GameEntityActorTime actorTime,
        double time, 
        float actorVelocity,
        //uint actorMask,
        int actorStatus, 
        ref int groupMask,
        ref int conditionIndex, 
        in DynamicBuffer<GameActionCondition> conditions, 
        in DynamicBuffer<GameEntityActorActionInfo> actionInfos, 
        in DynamicBuffer<GameActionConditionAction> actions)
    {
        GameActionConditionResult result = GameActionConditionResult.OK, temp;
        int groupMaskResult = groupMask, groupMaskTemp, numConditions = conditions.Length;
        if (conditionIndex == -1)
        {
            for (int i = 0; i < numConditions; ++i)
            {
                groupMaskTemp = groupMask;
                temp = conditions[i].Did(
                        //actorTime,
                        time,
                        actorVelocity,
                        //actorMask, 
                        actorStatus,
                        -1,
                        ref groupMaskTemp,
                        actionInfos,
                        default);
                if (temp == GameActionConditionResult.OK)
                {
                    if (groupMask == 0)
                        groupMaskResult |= groupMaskTemp;
                    else
                    {
                        conditionIndex = i;

                        //Debug.Log($"Condi {conditionIndex}");

                        return GameActionConditionResult.OK;
                    }
                }
                else if (temp > result)
                    result = temp;
            }
        }
        else
        {
            int preActionIndex = conditions[conditionIndex].actionIndex;
            for (int i = conditionIndex + 1; i < numConditions; ++i)
            {
                groupMaskTemp = groupMask;
                temp = conditions[i].Did(
                        //actorTime,
                        time,
                        actorVelocity,
                        //actorMask,
                        actorStatus,
                        preActionIndex,
                        ref groupMaskTemp,
                        actionInfos,
                        actions);
                if (temp == GameActionConditionResult.OK)
                {
                    if (groupMask == 0)
                        groupMaskResult |= groupMaskTemp;
                    else
                    {
                        conditionIndex = i;

                        return GameActionConditionResult.OK;
                    }
                }
                else if (temp > result)
                    result = temp;
            }

            for (int i = 0; i < conditionIndex; ++i)
            {
                groupMaskTemp = groupMask;
                temp = conditions[i].Did(
                        //actorTime,
                        time,
                        actorVelocity,
                        //actorMask,
                        actorStatus,
                        preActionIndex,
                        ref groupMaskTemp,
                        actionInfos,
                        actions);
                if (temp == GameActionConditionResult.OK)
                {
                    if (groupMask == 0)
                        groupMaskResult |= groupMaskTemp;
                    else
                    {
                        conditionIndex = i;

                        return GameActionConditionResult.OK;
                    }
                }
                else if (temp > result)
                    result = temp;
            }
        }

        if (groupMask == 0 && groupMaskResult != 0)
        {
            groupMask = groupMaskResult;

            return GameActionConditionResult.OK;
        }

        return result;
    }
}

[Serializable]
public struct GameActionGroup : IBufferElementData
{
    public int mask;
    public float chance;

    [Tooltip("Cos(最小角度)")]
    public float minDot;
    [Tooltip("Cos(最大角度)")]
    public float maxDot;

    public float minDistance;
    public float maxDistance;

    [Range(0.0f, 1.0f)]
    public float minHealth;
    [Range(0.0f, 1.0f)]
    public float maxHealth;

    [Range(0.0f, 1.0f)]
    public float minTorpidity;
    [Range(0.0f, 1.0f)]
    public float maxTorpidity;

    public int Did(
        float health,
        float torpidity,
        float dot)
    {
        if (health < minHealth ||
            health > maxHealth ||
            torpidity < minTorpidity ||
            torpidity > maxTorpidity)
            return 0;

        if (minDot < maxDot && (dot < minDot || dot > maxDot))
            return 0;

        return mask;
    }

    public int Did(
        int mask, 
        float health,
        float torpidity,
        float dot,
        float distance, 
        ref float chance)
    {
        if ((mask & this.mask) == 0)
            return 0;

        if (health < minHealth ||
            health > maxHealth ||
            torpidity < minTorpidity ||
            torpidity > maxTorpidity)
            return 0;

        if (minDot < maxDot && (dot < minDot || dot > maxDot))
            return 0;

        if (minDistance < maxDistance && (distance < minDistance || distance > maxDistance))
            return 0;

        if (this.chance < chance)
        {
            chance -= this.chance;

            return 0;
        }

        return this.mask;
    }

    public static int Did(
        int mask,
        float health,
        float torpidity,
        float dot, 
        float distance, 
        float chance, 
        in DynamicBuffer<GameActionGroup> groups)
    {
        int numGroups = groups.Length, maskTemp;
        for(int i = 0; i < numGroups; ++i)
        {
            maskTemp = groups[i].Did(mask, health, torpidity, dot, distance, ref chance);
            if (maskTemp != 0)
                return maskTemp;
        }

        return 0;
    }
}

[EntityComponent(typeof(GameActionConditionAction))]
[EntityComponent(typeof(GameActionCondition))]
[EntityComponent(typeof(GameActionGroup))]
public class GameActionActiveComponent : EntityProxyComponent, IEntityComponent
{
    [Serializable]
    public struct Condition
    {
#if UNITY_EDITOR
        public string name;
#endif

        public int actionIndex;
        public int groupMask;

        [Mask(type = typeof(GameNodeActorStatus.Status))]
        public int actorStatusMask;

        public float maxSpeed;

        public int[] preActionIndices;
    }

    public Condition[] conditions;
    public GameActionGroup[] groups;

    void IEntityComponent.Init(in Entity entity, EntityComponentAssigner assigner)
    {
        int numConditions = this.conditions.Length;
        Condition source;
        GameActionCondition destination;
        GameActionCondition[] conditions = new GameActionCondition[numConditions];
        List<GameActionConditionAction> actions = null;
        for(int i = 0; i < numConditions; ++i)
        {
            source = this.conditions[i];
            destination.actionIndex = source.actionIndex;
            destination.groupMask = source.groupMask;
            destination.actorStatusMask = source.actorStatusMask;
            destination.maxSpeed = source.maxSpeed;
            destination.preActionStartIndex = actions == null ? 0 : actions.Count;
            destination.preActionCount = source.preActionIndices == null ? 0 : source.preActionIndices.Length;

            if(destination.preActionCount > 0)
            {
                if (actions == null)
                    actions = new List<GameActionConditionAction>();

                foreach(var preActionIndex in source.preActionIndices)
                    actions.Add(preActionIndex);
            }

            conditions[i] = destination;
        }

        if (actions != null)
            assigner.SetBuffer(true, entity, actions.ToArray());

        assigner.SetBuffer(true, entity, conditions);

        assigner.SetBuffer(true, entity, groups);
    }
}
