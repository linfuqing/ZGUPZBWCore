using System;
using Unity.Entities;
using ZG;

[Serializable]
public struct GameWatcherData : IComponentData
{
    [Mask]
    public GameActionTargetType type;

    public UnityEngine.LayerMask raycastMask;

    public float minTime;
    [UnityEngine.Serialization.FormerlySerializedAs("time")]
    public float maxTime;
    public float contactTolerance;

    public Unity.Mathematics.float3 eye;
    
    //public EntityArchetype actionEntityArchetype;
    public BlobAssetReference<Unity.Physics.Collider> collider;
}

public struct GameWatcherInfo : IComponentData
{
    public enum Type
    {
        Main,
        Camp
    }

    public double time;

    public Type type;
    public Entity target;
}

//[EntityComponent(typeof(Unity.Physics.CollisionWorldProxy))]
[EntityComponent(typeof(GameWatcherInfo))]
public class GameWatcherComponent : ZG.ComponentDataProxy<GameWatcherData>
{
    [UnityEngine.SerializeField]
    internal PhysicsShapeComponent _shape = null;

    [UnityEngine.SerializeField]
    internal int _shapeIndex = 2;

#if UNITY_EDITOR
    public PhysicsShapeComponent shape
    {
        get => _shape;

        set => _shape = value;
    }

    public int shapeIndex
    {
        get => _shapeIndex;

        set => _shapeIndex = value;
    }
#endif

    public Entity target
    {
        get
        {
            return this.GetComponentData<GameWatcherInfo>().target;
        }

        /*set
        {
            GameObjectEntity gameObjectEntity = this.gameObjectEntity;
            EntityManager entityManager = gameObjectEntity?.EntityManager;
            if (entityManager != null)
            {
                Entity entity = gameObjectEntity.Entity;
                if (entityManager.Exists(entity))
                {
                    GameWatcherInfo watcherInfo;
                    watcherInfo.target = value;

                    entityManager.AddOrSetComponentData(entity, watcherInfo);
                }
            }
        }*/
    }

    public override void Init(in Entity entity, EntityComponentAssigner assigner)
    {
        if (_shape != null)
            _value.collider = _shape.colliders.value;
        else 
        if(_shapeIndex != -1)
            _value.collider = GetComponentInChildren<PhysicsHierarchyComponent>().database.GetOrCreateCollider(_shapeIndex);

        base.Init(entity, assigner);

        GameWatcherInfo watcherInfo;
        watcherInfo.time = 0.0;
        watcherInfo.type = GameWatcherInfo.Type.Main;
        watcherInfo.target = Entity.Null;

        assigner.SetComponentData(entity, watcherInfo);
    }
}