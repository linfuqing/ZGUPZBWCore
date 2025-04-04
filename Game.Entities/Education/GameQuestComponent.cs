﻿using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using ZG;

public enum GameQuestConditionType
{
    Make,
    Get,
    Use,
    Kill,
    Own,
    Select, 
    Upgrade
}

public enum GameQuestRewardType
{
    Quest,
    Formula,
    Item, 
    Money
}

public enum GameQuestStatus
{
    Normal,
    Complete,
    Finish
}

[Serializable]
public struct GameQuestConditionData
{
    public GameQuestConditionType type;
    public int index;
    public int count;
}

[Serializable]
public struct GameQuestRewardResult
{
    public GameQuestRewardType type;
    public int index;
    public int count;

    public GameQuestRewardResult(GameQuestRewardType type, int index, int count)
    {
        this.type = type;
        this.index = index;
        this.count = count;
    }
}

[Serializable]
public struct GameQuestRewardData
{
    public GameQuestRewardType type;
    public int index;
    public int count;
    public float chance;
}

[Serializable]
public struct GameQuestData
{
    public string label;

    public GameQuestConditionData[] conditions;

    public GameQuestRewardData[] rewards;
}

[Serializable]
public struct GameQuestOption
{
    public GameQuestRewardData[] rewards;
}

[EntityDataTypeName("GameMission")]
public struct GameQuest : IBufferElementData
{
    public int index;

    public int conditionBits;

    public GameQuestStatus status;
}

public struct GameQuestCommandCondition : IBufferElementData, IEnableableComponent
{
    public GameQuestConditionType type;
    public int index;
    public int count;
    public FixedString32Bytes label;
}

public struct GameQuestCommand : IBufferElementData, IEnableableComponent
{
    public GameQuestStatus status;
    public int index;
}

[EntityComponent(typeof(GameQuestCommand))]
[EntityComponent(typeof(GameQuestCommandCondition))]
[EntityComponent(typeof(GameQuest))]
public class GameQuestComponent : EntityProxyComponent
{
    public void Submit(int index, GameQuestStatus status)
    {
        GameQuestCommand command;
        command.status = status;
        command.index = index;

        this.AppendBuffer(command);

        this.SetComponentEnabled<GameQuestCommand>(true);
    }

    public void Append(in GameQuestCommandCondition value)
    {
        this.AppendBuffer(value);

        this.SetComponentEnabled<GameQuestCommandCondition>(true);
    }

    public void Append<T>(T values) where T : IReadOnlyCollection<GameQuestCommandCondition>
    {
        this.AppendBuffer<GameQuestCommandCondition, T>(values);

        this.SetComponentEnabled<GameQuestCommandCondition>(true);
    }
}
