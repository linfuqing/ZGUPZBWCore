using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using ZG;

public enum GameQuestConditionType
{
    Make,
    Get,
    Use,
    Kill,
    Tame,
    Select
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

[Serializable, EntityDataTypeName("GameMission")]
public struct GameQuest : IBufferElementData
{
    public int index;

    public int conditionBits;

    public GameQuestStatus status;
}

[Serializable]
public struct GameQuestCommandCondition : IBufferElementData
{
    public GameQuestConditionType type;
    public int index;
    public int count;
}

[Serializable]
public struct GameQuestCommandValue : IComponentData
{
    public GameQuestStatus status;
    public int index;
    public int version;
}

[Serializable]
public struct GameQuestCommand : IComponentData
{
    public int version;
}

[Serializable]
public struct GameQuestVersion : IComponentData
{
    public int value;
}

[EntityComponent(typeof(GameQuestVersion))]
[EntityComponent(typeof(GameQuestCommand))]
[EntityComponent(typeof(GameQuestCommandValue))]
[EntityComponent(typeof(GameQuestCommandCondition))]
[EntityComponent(typeof(GameQuest))]
public class GameQuestComponent : EntityProxyComponent, IEntityComponent
{
    public int Submit()
    {
        GameQuestCommand command;
        command.version = this.GetComponentData<GameQuestVersion>().value;
        this.SetComponentData(command);

        return command.version;
    }

    public void Submit(int index, GameQuestStatus status)
    {
        GameQuestCommandValue commandValue;
        commandValue.status = status;
        commandValue.index = index;
        commandValue.version = Submit();

        this.SetComponentData(commandValue);
    }

    public void Append(in GameQuestCommandCondition value)
    {
        this.AppendBuffer(value);

        Submit();
    }

    public void Append<T>(T values) where T : IReadOnlyCollection<GameQuestCommandCondition>
    {
        this.AppendBuffer<GameQuestCommandCondition, T>(values);

        Submit();
    }

    void IEntityComponent.Init(in Entity entity, EntityComponentAssigner assigner)
    {
        GameQuestVersion version;
        version.value = 1;
        assigner.SetComponentData(entity, version);
    }
}
