using System;
using System.Collections.Generic;
using Unity.Entities;
using ZG;

public struct GameFormula : IBufferElementData
{
    public int index;
    public int level;
    public int count;
}

public struct GameFormulaCommand : IBufferElementData, IEnableableComponent
{
    public int index;
    public int count;
}

public struct GameFormulaEvent : IBufferElementData, IEnableableComponent
{
    public int index;
    public int count;
}

[EntityComponent(typeof(GameFormula))]
[EntityComponent(typeof(GameFormulaCommand))]
[EntityComponent(typeof(GameFormulaEvent))]
public class GameFormulaComponent : EntityProxyComponent
{
    public void Append(in GameFormulaCommand value)
    {
        this.AppendBuffer(value);

        this.SetComponentEnabled<GameFormulaCommand>(true);
    }

    public void Append<T>(T values) where T : IReadOnlyCollection<GameFormulaCommand>
    {
        this.AppendBuffer<GameFormulaCommand, T>(values);

        this.SetComponentEnabled<GameFormulaCommand>(true);
    }
}
