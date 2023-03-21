using System;
using Unity.Entities;
using ZG;

[Serializable]
public struct GameEntityHealthActorData : IComponentData
{
    public float minSpeedToHit;
    public float maxSpeedToHit;
    public float speedToHitPower;
    public float speedToHitScale;
}

[Serializable]
public struct GameEntityHealthActorInfo : IComponentData
{
    public float oldVelocity;
    public float oldHeight;
    public double time;
}

[EntityComponent(typeof(GameEntityHealthActorInfo))]
public class GameEntityHealthActorComponent : ComponentDataProxy<GameEntityHealthActorData>
{
    public void Clear()
    {
        this.SetComponentData<GameEntityHealthActorInfo>(default);
    }
}