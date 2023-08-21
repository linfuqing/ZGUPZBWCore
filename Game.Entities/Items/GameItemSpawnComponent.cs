using UnityEngine;
using ZG;

[EntityComponent(typeof(GameItemSpawnOffset))]
//[EntityComponent(typeof(GameItemSpawnCommandVersion))]
[EntityComponent(typeof(GameItemSpawnCommand))]
[EntityComponent(typeof(GameItemSpawnHandleCommand))]
public class GameItemSpawnComponent : MonoBehaviour, IEntityComponent
{
    public Vector3 offsetMin;
    public Vector3 offsetMax;

    void IEntityComponent.Init(in Unity.Entities.Entity entity, EntityComponentAssigner assigner)
    {
        GameItemSpawnOffset offset;
        offset.min = offsetMin;
        offset.max = offsetMax;

        assigner.SetComponentData(entity, offset);
        assigner.SetComponentEnabled<GameItemSpawnCommand>(entity, false);
        assigner.SetComponentEnabled<GameItemSpawnHandleCommand>(entity, false);
    }
}
