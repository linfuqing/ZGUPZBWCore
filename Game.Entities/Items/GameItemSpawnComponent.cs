using UnityEngine;
using ZG;

[EntityComponent(typeof(GameItemSpawnRange))]
//[EntityComponent(typeof(GameItemSpawnCommandVersion))]
[EntityComponent(typeof(GameItemSpawnCommand))]
[EntityComponent(typeof(GameItemSpawnHandleCommand))]
public class GameItemSpawnComponent : MonoBehaviour, IEntityComponent
{
    public float radius;
    public Unity.Mathematics.RigidTransform center;

    void IEntityComponent.Init(in Unity.Entities.Entity entity, EntityComponentAssigner assigner)
    {
        GameItemSpawnRange range;
        range.radius = radius;
        range.center = Unity.Mathematics.math.RigidTransform(center.rot.Equals(default) ? Unity.Mathematics.quaternion.identity : center.rot, center.pos);

        assigner.SetComponentData(entity, range);
        assigner.SetComponentEnabled<GameItemSpawnCommand>(entity, false);
        assigner.SetComponentEnabled<GameItemSpawnHandleCommand>(entity, false);
    }
}
