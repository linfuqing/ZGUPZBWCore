using UnityEngine;
using ZG;

[EntityComponent(typeof(GameFootstepData))]
[EntityComponent(typeof(GameFootstep))]
public class GameFootstepComponent : MonoBehaviour, IEntityComponent
{
    [SerializeField]
    internal GameFootstepDatabase _database;

    void IEntityComponent.Init(in Unity.Entities.Entity entity, EntityComponentAssigner assigner)
    {
        GameFootstepData instance;
        instance.definition = _database.definition;
        assigner.SetComponentData(entity, instance);
    }
}
