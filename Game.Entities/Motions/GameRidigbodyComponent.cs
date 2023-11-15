using System;
using Unity.Mathematics;
using Unity.Entities;
using Unity.Physics;
using ZG;

[Serializable]
public struct GameRidigbodyData : IComponentData
{
    public UnityEngine.LayerMask layerMask;
}

public struct GameRidigbodyMass : IComponentData
{
    public float value;
}

public struct GameRidigbodyOrigin : ICleanupComponentData
{
    public RigidTransform transform;
}

[EntityComponent(typeof(PhysicsVelocity))]
//[EntityComponent(typeof(PhysicsMass))]
[EntityComponent(typeof(PhysicsGravityFactor))]
[EntityComponent(typeof(GameRidigbodyMass))]
public class GameRidigbodyComponent : ComponentDataProxy<GameRidigbodyData>
{
    [UnityEngine.SerializeField]
    public float _mass;

    public override void Init(in Entity entity, EntityComponentAssigner assigner)
    {
        base.Init(entity, assigner);

        /*var physicsShapeComponent = GetComponentInChildren<PhysicsShapeComponent>();
        
        var collider = physicsShapeComponent == null ? this.GetComponentData<PhysicsCollider>().Value : physicsShapeComponent.colliders.value;

        assigner.SetComponentData(entity, PhysicsMass.CreateDynamic(collider.Value.MassProperties, _mass));*/

        GameRidigbodyMass mass;
        mass.value = _mass;
        assigner.SetComponentData(entity, mass);

        PhysicsGravityFactor physicsGravityFactor;
        physicsGravityFactor.Value = 1.0f;
        assigner.SetComponentData(entity, physicsGravityFactor);
    }
}