using System;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using ZG;

public enum GameEntityStatus
{
    KnockedOut = GameNodeStatus.DELAY | GameNodeStatus.STOP,
    Dead = GameNodeStatus.DELAY | GameNodeStatus.STOP | GameNodeStatus.OVER,

    Mask = GameNodeStatus.DELAY | GameNodeStatus.STOP | GameNodeStatus.OVER
}

public struct GameEntityNode
{
    public int camp;
    public Entity entity;

    /*public struct Collector<T> : ICollector<T> where T : struct, IQueryResult
    {
        public GameActionTargetType type;
        public GameEntityNode node;

        [ReadOnly]
        public ComponentLookup<GameEntityCamp> camps;

        [ReadOnly]
        public NativeSlice<RigidBody> rigidBodies;
        
        private float __fraction;
        
        public bool EarlyOutOnFirstHit => false;

        public int NumHits => 0;

        public float MaxFraction { get; private set; }
        
        public GameEntityNode result { get; private set; }

        public Collector(
            GameActionTargetType type,
            float maxFraction,
            GameEntityNode node, 
            ComponentLookup<GameEntityCamp> camps,
            NativeSlice<RigidBody> rigidBodies)
        {
            this.type = type;
            this.node = node;
            this.camps = camps;
            this.rigidBodies = rigidBodies;
            
            __fraction = maxFraction;
            MaxFraction = maxFraction;

            result = default;
        }

        public bool AddHit(T hit)
        {
            MaxFraction = hit.Fraction;
            
            return true;
        }

        public void TransformNewHits(int oldNumHits, float oldFraction, Unity.Physics.Math.MTransform transform, uint numSubKeyBits, uint subKey)
        {
        }

        public unsafe void TransformNewHits(int oldNumHits, float oldFraction, Unity.Physics.Math.MTransform transform, int rigidBodyIndex)
        {
            RigidBody rigidBody = rigidBodies[rigidBodyIndex];
            bool isContains = rigidBody.Collider != null && camps.HasComponent(rigidBody.Entity);
            if (isContains)
            {
                GameEntityNode node;
                node.camp = camps[rigidBody.Entity].value;
                node.entity = rigidBody.Entity;
                isContains = this.node.Predicate(type, node);
                if(isContains)
                {
                    __fraction = MaxFraction;

                    result = node;
                }
            }

            if (!isContains)
                MaxFraction = __fraction;
        }
    }*/

    public bool Predicate(GameActionTargetType type, GameEntityNode node)
    {
        if ((type & GameActionTargetType.Self) == GameActionTargetType.Self)
        {
            if (node.entity == entity)
                return true;
        }

        if ((type & GameActionTargetType.Ally) == GameActionTargetType.Ally)
        {
            if (node.camp == camp && node.entity != entity)
                return true;
        }

        if ((type & GameActionTargetType.Enemy) == GameActionTargetType.Enemy)
        {
            if (node.camp != camp)
                return true;
        }

        return false;
    }
}

/*public struct GameActionDisabled : IComponentData
{
    public double time;
}*/

public struct GameActionData : IComponentData
{
    public int version;
    public int index;
    public int actionIndex;
    public GameDeadline time;
    public Entity entity;

    public override string ToString()
    {
        return "GameActionData(Version: " + version + ", Index: " + index + ")";
    }
}

public struct GameActionDataEx : IComponentData
{
    public int camp;
    public float3 forward;
    public float3 direction;
    public float3 offset;
    public float3 position;
    public float3 targetPosition;
    public RigidTransform origin;
    public Entity target;
    public GameActionInfo info;
    public GameAction value;
    public EntityArchetype entityArchetype;
    public BlobAssetReference<Unity.Physics.Collider> collider;
}

public struct GameEntityCampDefault : IComponentData
{
    public int value;
}

public struct GameEntityCamp : IComponentData
{
    public int value;

    public override string ToString()
    {
        return value.ToString();
    }
}

[InternalBufferCapacity(4)]
public struct GameEntityItem : IBufferElementData
{
    public int index;

    public static implicit operator GameEntityItem(int x)
    {
        GameEntityItem item;
        item.index = x;
        return item;
    }
}

[EntityComponent(typeof(GameEntityCampDefault))]
[EntityComponent(typeof(GameEntityCamp))]
[EntityComponent(typeof(GameEntityItem))]
public class GameEntityComponent : EntityProxyComponent, IEntityComponent
{
#if UNITY_EDITOR
    [CSVField(CSVFieldFlag.OverrideNearestPrefab)]
    public int 阵营
    {
        set
        {
            _camp = value;
        }
    }
#endif

    [SerializeField]
    internal int _camp;

    [SerializeField]
    internal int[] _itemIndices;

    public int camp
    {
        get
        {
            return this.GetComponentData<GameEntityCamp>().value;
        }

        set
        {
            GameEntityCamp camp;
            camp.value = value;
            this.SetComponentData(camp);
        }
    }

    public int itemCount
    {
        get
        {
            return _itemIndices == null ? 0 : _itemIndices.Length;
        }

        set
        {
            int length = _itemIndices == null ? 0 : _itemIndices.Length;
            Array.Resize(ref _itemIndices, value);
            for (int i = length; i < value; ++i)
                _itemIndices[i] = -1;
        }
    }

    public GameEntityItem[] items
    {
        get
        {
            return __GetItemIndices(_itemIndices);
        }
    }

    public void Set(int index, int itemIndex)
    {
        int length = _itemIndices == null ? 0 : _itemIndices.Length;
        if (length < index + 1)
        {
            Array.Resize(ref _itemIndices, index + 1);

            for (int i = length; i < index; ++i)
                _itemIndices[i] = -1;

            _itemIndices[index] = itemIndex;

            if (gameObjectEntity.isCreated)
                this.SetBuffer(__GetItemIndices(_itemIndices));

            return;
        }

        _itemIndices[index] = itemIndex;

        if (gameObjectEntity.isCreated)
            this.SetBuffer(__GetItemIndices(_itemIndices));
    }

    public void Set(EntityCommander commander, int index, int itemIndex)
    {
        int length = _itemIndices == null ? 0 : _itemIndices.Length;
        if (length < index + 1)
        {
            Array.Resize(ref _itemIndices, index + 1);

            for (int i = length; i < index; ++i)
                _itemIndices[i] = -1;

            _itemIndices[index] = itemIndex;

            if (gameObjectEntity.isCreated)
                this.SetBuffer(__GetItemIndices(_itemIndices));

            return;
        }

        _itemIndices[index] = itemIndex;

        commander.SetBuffer(entity, __GetItemIndices(_itemIndices));
    }

    void IEntityComponent.Init(in Entity entity, EntityComponentAssigner assigner)
    {
        GameEntityCampDefault campDefault;
        campDefault.value = _camp;
        assigner.SetComponentData(entity, campDefault);

        GameEntityCamp camp;
        camp.value = _camp;
        assigner.SetComponentData(entity, camp);

        assigner.SetBuffer(true, entity, __GetItemIndices(_itemIndices));
    }

    private static GameEntityItem[] __GetItemIndices(int[] values)
    {
        int numValues = values == null ? 0 : values.Length;

        GameEntityItem[] items = new GameEntityItem[numValues];
        for (int i = 0; i < numValues; ++i)
            items[i] = values[i];

        return items;
    }
}

public abstract class GameEntityComponentEx : EntityProxyComponent
{
    public abstract EntityArchetype actionEntityArchetype { get; }
}