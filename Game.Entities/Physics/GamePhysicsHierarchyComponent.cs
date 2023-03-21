using System;
using UnityEngine;
using ZG;

[EntityComponent(typeof(GamePhysicsHierarchyData))]
[EntityComponent(typeof(GamePhysicsHierarchyBitField))]
public class GamePhysicsHierarchyComponent : MonoBehaviour, IEntityComponent
{
    [SerializeField]
    internal GamePhysicsHierarchyDatabase _database;

    void IEntityComponent.Init(in Unity.Entities.Entity entity, EntityComponentAssigner assigner)
    {
        GamePhysicsHierarchyData instance;
        instance.definition = _database.definition;
        assigner.SetComponentData(entity, instance);
    }
}
