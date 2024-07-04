using Unity.Entities;

public enum GameFactoryStatus
{
    Normal,
    Building,
    Complete
}

public struct GameFactory : IComponentData
{
    public GameFactoryStatus status;

    public int formulaIndex;

    //public int level;

    public int count;

    public uint id;

    //public float time;
}

public struct GameFactoryTimeScale : IComponentData
{
    public float value;
}
