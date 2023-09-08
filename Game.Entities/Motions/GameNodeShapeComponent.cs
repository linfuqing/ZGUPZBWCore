using System;
using Unity.Mathematics;
using Unity.Entities;
using Unity.Physics;
using UnityEngine;
using ZG;
using Unity.Transforms;

public struct GameNodeShpaeDefault : IComponentData
{
    public PhysicsMass mass;

    public PhysicsCollider collider;
}

public struct GameNodeShpae : IBufferElementData
{
    public int status;

    public PhysicsMass mass;

    public PhysicsCollider collider;
}

//[EntityComponent(typeof(PhysicsVelocity))]
[EntityComponent(typeof(PhysicsMass))]
//[EntityComponent(typeof(PhysicsGravityFactor))]
[EntityComponent(typeof(GameNodeShpaeDefault))]
[EntityComponent(typeof(GameNodeShpae))]
public class GameNodeShapeComponent : EntityProxyComponent, IEntityComponent
{
    [Serializable]
    public class Shape : ISerializationCallbackReceiver
    {
        [SerializeField]
        internal int _shapeIndex = -1;

        [SerializeField]
        internal int _status = 0;

        //[SerializeField]
        internal float _mass = 0.0f;

        [SerializeField]
        internal PhysicsShapeComponent _instance = null;

        [SerializeField, HideInInspector]
        private PhysicsColliders __colliders;

        private BlobAssetReference<Unity.Physics.Collider> __collider;

#if UNITY_EDITOR
        public PhysicsShapeComponent shape
        {
            get => _instance;

            set => _instance = value;
        }

        public int shapeIndex
        {
            get => _shapeIndex;

            set => _shapeIndex = value;
        }
#endif

        public BlobAssetReference<Unity.Physics.Collider> colliders => __colliders == null  ? __collider : __colliders.value;

        internal GameNodeShpae _Init(GameObject gameObject)
        {
            BlobAssetReference<Unity.Physics.Collider> collider;
            if (__colliders == null)
            {
                __collider = gameObject.GetComponent<PhysicsHierarchyComponent>().database.GetOrCreateCollider(_shapeIndex);

                collider = __collider;
            }
            else
                collider = __colliders.value;
            
            GameNodeShpae shape;
            shape.status = _status;
            shape.mass = _mass > math.FLT_MIN_NORMAL ? PhysicsMass.CreateDynamic(collider.Value.MassProperties, _mass) : PhysicsMass.CreateKinematic(collider.Value.MassProperties);
            shape.collider.Value = collider;

            return shape;
        }

        void ISerializationCallbackReceiver.OnAfterDeserialize()
        {

        }

        void ISerializationCallbackReceiver.OnBeforeSerialize()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
                return;
#endif
            if (__colliders == null && _instance != null)
            {
                var colliders = _instance.colliders;
                var triggers = _instance.triggers;
                var colliderBlobInstances = new Unity.Collections.NativeList<CompoundCollider.ColliderBlobInstance>(Unity.Collections.Allocator.Temp);
                int length = colliders.length, temp = 0, numTriggerIndices = triggers == null ? 0 : triggers.Count, triggerIndex = numTriggerIndices < 1 ? -1 : triggers[0].index;
                for (int i = 0; i < length; ++i)
                {
                    if (i == triggerIndex)
                    {
                        ++temp;

                        triggerIndex = temp < numTriggerIndices ? triggers[temp].index : -1;

                        continue;
                    }

                    colliderBlobInstances.Add(colliders[i]);
                }

                __colliders = PhysicsColliders.Create(colliderBlobInstances.AsArray(), false);

                colliderBlobInstances.Dispose();
            }
        }
    }

    [SerializeField, UnityEngine.Serialization.FormerlySerializedAs("_shape")]
    internal Shape[] _shapes = null;

    //[SerializeField]
    //internal float _mass = 0.0f;

#if UNITY_EDITOR
    public Shape[] shape
    {
        get => _shapes;

        set => _shapes = value;
    }
#endif

    public int shapeCount => _shapes.Length;

    public static Entity GetParent(IGameObjectEntity gameObjectEntity, Entity entity, int authorityMask = -1)
    {
        if (!gameObjectEntity.HasComponent<GameNodeParent>(entity))
            return Entity.Null;
        
        GameNodeParent parent = gameObjectEntity.GetComponentData<GameNodeParent>(entity);
        if ((parent.authority & authorityMask) != 0)
            return parent.entity;

        return GetParent(gameObjectEntity, parent.entity, authorityMask);
    }

    public Entity GetParentEntity(int authorityMask = -1) => GetParent(this, entity, authorityMask);

    public void ResetParent(EntityCommander commander)
    {
        Entity entity = this.entity;

        commander.RemoveComponent<PhysicsExclude>(entity);
        commander.RemoveComponent<GameNodeParent>(entity);
    }

    public void ResetParent()
    {
        /*if (__defaultShape != null)
        {
            __defaultShape.enabled = true;

            __defaultShape.Refresh();
        }*/

        //this.AddComponentDataIfNotExists<PhysicsVelocity>(default);

        this.RemoveComponent<PhysicsExclude>();
        this.RemoveComponent<GameNodeParent>();
    }

    public void SetParent(IPhysicsComponent value, int authority = 0)
    {
        this.AddComponent<PhysicsExclude>();

        //this.RemoveComponentIfExists<PhysicsVelocity>();

        /*PhysicsGravityFactor physicsGravityFactor;
        physicsGravityFactor.Value = 0.0f;
        this.SetComponentData(physicsGravityFactor);*/

        GameNodeParent parent;
        parent.authority = authority;
        parent.entity = value.entity;
        parent.transform = math.mul(math.inverse(value.GetTransform()), math.RigidTransform(this.GetComponentData<Rotation>().Value, this.GetComponentData<Translation>().Value));
        this.AddComponentData(parent);
    }

    public void SetParent(EntityCommander commander, IPhysicsComponent value, int authority = 0)
    {
        Entity entity = this.entity;
        commander.AddComponent<PhysicsExclude>(entity);

        GameNodeParent parent;
        parent.authority = authority;
        parent.entity = value.entity;
        parent.transform = math.mul(math.inverse(value.GetTransform()), math.RigidTransform(this.GetComponentData<Rotation>().Value, this.GetComponentData<Translation>().Value));
        commander.AddComponentData(entity, parent);
    }
    
    public BlobAssetReference<Unity.Physics.Collider> GetCollider(int index)
    {
        return _shapes[index].colliders;
    }

    void IEntityComponent.Init(in Entity entity, EntityComponentAssigner assigner)
    {
        int length = _shapes.Length;
        var shapes = new GameNodeShpae[length];
        for (int i = length - 1; i >= 0; --i)
            shapes[i] = _shapes[i]._Init(gameObject);

        assigner.SetBuffer(true, entity, shapes);
    }

    /*private void __OnChanged(BlobAssetReference<Unity.Physics.Collider> collider)
    {
        if (!collider.IsCreated)
            return;
        
        GameNodeShpaeDefault shape;
        shape.mass = _mass > math.FLT_MIN_NORMAL ? PhysicsMass.CreateDynamic(collider.Value.MassProperties, _mass) : PhysicsMass.CreateKinematic(collider.Value.MassProperties);
        shape.mass.InverseInertia = float3.zero;
        shape.collider.Value = collider;
        this.SetComponentData(shape);
        this.SetComponentData(shape.mass);
    }*/

    /*private void __OnChanged(Entity target)
    {
        var gameObjectEntity = base.gameObjectEntity;
        EntityManager entityManager = gameObjectEntity.entityManager;
        if (entityManager == null || !entityManager.IsCreated)
            return;

        Entity entity = gameObjectEntity.entity;
        if (!entityManager.HasComponent<GameNodeParent>(entity))
            return;

        GameNodeParent parent = entityManager.GetComponentData<GameNodeParent>(entity);
        parent.entity = target;
        this.SetComponentData(parent);
    }*/
}
