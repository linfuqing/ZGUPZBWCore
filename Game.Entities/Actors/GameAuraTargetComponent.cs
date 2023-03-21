using Unity.Entities;
using ZG;

[System.Serializable]
public struct GameAuraOrigin : IBufferElementData
{
    public int itemIndex;
    public int flag;
    public double time;
    public Entity entity;
}

[EntityComponent(typeof(GameAuraOrigin))]
public class GameAuraTargetComponent : EntityProxyComponent
{
}
