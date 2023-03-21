using System;
using System.Collections.Generic;
using Unity.Entities;
using ZG;

[Serializable]
public struct GameFormula : IBufferElementData
{
    public int index;
    public int level;
    public int count;
}

[Serializable]
public struct GameFormulaVersion : IComponentData
{
    public int value;
}

[Serializable]
public struct GameFormulaCommand : IComponentData
{
    public int version;
}

[Serializable]
public struct GameFormulaCommandValue : IBufferElementData
{
    public int index;
    public int count;
}

[Serializable]
public struct GameFormulaEvent : IBufferElementData
{
    public int index;
    public int count;
}

[EntityComponent(typeof(GameFormula))]
[EntityComponent(typeof(GameFormulaVersion))]
[EntityComponent(typeof(GameFormulaCommand))]
[EntityComponent(typeof(GameFormulaCommandValue))]
[EntityComponent(typeof(GameFormulaEvent))]
public class GameFormulaComponent : EntityProxyComponent, IEntityComponent
{
    public void Submit()
    {
        GameFormulaCommand command;
        command.version = this.GetComponentData<GameFormulaVersion>().value;
        this.SetComponentData(command);
    }

    public void Append(in GameFormulaCommandValue value)
    {
        this.AppendBuffer(value);

        Submit();
    }

    public void Append<T>(T values) where T : IReadOnlyCollection<GameFormulaCommandValue>
    {
        this.AppendBuffer<GameFormulaCommandValue, T>(values);

        Submit();
    }

    void IEntityComponent.Init(in Entity entity, EntityComponentAssigner assigner)
    {
        GameFormulaVersion version;
        version.value = 1;
        assigner.SetComponentData(entity, version);
    }
}
