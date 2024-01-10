using System;
using System.Collections.Generic;
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
    Formula
}

public enum GameQuestRewardType
{
    Quest,
    Formula,
    Item
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
public struct GameQuestRewardData
{
    public GameQuestRewardType type;
    public int index;
    public int count;
}

[Serializable]
public struct GameQuestData
{
    public int money;

    public GameQuestConditionData[] conditions;

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
