using System;
using Unity.Entities;

[Serializable]
public struct GameEffectorData : IComponentData
{
    [UnityEngine.Serialization.FormerlySerializedAs("effect")]
    public GameEffect value;
}

public class GameEffectorComponent : ZG.ComponentDataProxy<GameEffectorData>
{
}