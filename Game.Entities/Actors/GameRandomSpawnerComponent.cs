using System;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using ZG;

public struct GameRandomSpawnerSlice : IBufferElementData
{
    public int groupStartIndex;
    public int groupCount;
}

public struct GameRandomSpawnerGroup : IBufferElementData
{
    public RandomGroup value;
}

public struct GameRandomSpawnerAsset : IBufferElementData
{
    public enum Space
    {
        Local, 
        World
    }

    public Space space;

    public int index;

    public float vertical;
    public float horizontal;

    public float minTime;
    public float maxTime;

    public float minVelocity;
    public float maxVelocity;
    public float velocityRadius;
    public float3 velocityOffset;

    public RigidTransform offset;
}

public struct GameRandomSpawnerNode : IBufferElementData, IEnableableComponent
{
    public int sliceIndex;
}

[EntityComponent(typeof(GameRandomSpawnerSlice))]
[EntityComponent(typeof(GameRandomSpawnerGroup))]
[EntityComponent(typeof(GameRandomSpawnerAsset))]
[EntityComponent(typeof(GameRandomSpawnerNode))]
public class GameRandomSpawnerComponent : EntityProxyComponent, IEntityComponent
{
#if UNITY_EDITOR
    public GameActorDatabase database;
#endif

    [Serializable]
    public struct Slice
    {
#if UNITY_EDITOR
        public string name;
#endif

        /*[Tooltip("纵向掉落范围")]
        public float vertical;
        [Tooltip("横向掉落范围")]
        public float horizontal;

        public Vector3 position;
        public Quaternion rotation;*/

        public RandomGroup[] groups;
    }

    [Serializable]
    public struct Asset
    {
#if UNITY_EDITOR
        public string name;
#endif

        public GameRandomSpawnerAsset.Space space;

        [Index("database.assets", pathLevel = -1)]
        public int index;

        [Tooltip("纵向掉落范围")]
        public float vertical;
        [Tooltip("横向掉落范围")]
        public float horizontal;

        [Tooltip("随机时间最小值")]
        public float minTime;
        [Tooltip("随机时间最大值")]
        public float maxTime;

        [Tooltip("随机发射速度")]
        public float maxVelocity;
        [Tooltip("随机发射速度")]
        public float minVelocity;
        [Tooltip("发射速度的圆锥半径")]
        public float velocityRadius;
        [Tooltip("发射速度的圆锥偏移")]
        public Vector3 velocityOffset;

        public Vector3 position;
        public Quaternion rotation;
    }

    [SerializeField]
    internal Slice[] _slices = null;

    [SerializeField]
    internal Asset[] _assets = null;

    [SerializeField]
    [Index("database.assets", pathLevel = 1)]
    internal int[] _assetIndices = null;
    
    private static List<GameRandomSpawnerGroup> __groups = null;

    void IEntityComponent.Init(in Entity entity, EntityComponentAssigner assigner)
    {
        int length = _assetIndices == null ? 0 : _assetIndices.Length;
        if (length > 0)
        {
            _assets = new Asset[length];
            for (int i = 0; i < length; ++i)
                _assets[i].index = _assetIndices[i];
        }

        length = _assets == null ? 0 : _assets.Length;
        var assets = new GameRandomSpawnerAsset[length];
        for (int i = 0; i < length; ++i)
        {
            ref var source = ref _assets[i];
            ref var destination = ref assets[i];
            destination.space = source.space;
            destination.index = source.index;
            destination.vertical = source.vertical;
            destination.horizontal = source.horizontal;
            destination.minTime = source.minTime;
            destination.maxTime = source.maxTime;
            destination.minVelocity = source.minVelocity;
            destination.maxVelocity = source.maxVelocity;
            destination.velocityRadius = source.velocityRadius;
            destination.velocityOffset = source.velocityOffset;
            destination.offset = math.RigidTransform(source.rotation.normalized, source.position);
        }

        assigner.SetBuffer(EntityComponentAssigner.BufferOption.Override, entity, assets);

        length = _slices == null ? 0 : _slices.Length;
        if (length > 0)
        {
            if (__groups == null)
                __groups = new List<GameRandomSpawnerGroup>(length);
            else
                __groups.Clear();

            GameRandomSpawnerGroup destinationGroup;
            foreach (Slice slice in _slices)
            {
                foreach (var sourceGroup in slice.groups)
                {
                    destinationGroup.value = sourceGroup;
                    __groups.Add(destinationGroup);
                }
            }

            if(__groups.Count > 0)
                assigner.SetBuffer(EntityComponentAssigner.BufferOption.Override, entity, __groups.ToArray());

            var slices = new GameRandomSpawnerSlice[length];

            int groupCount = 0;
            for(int i = 0; i < length; ++i)
            {
                ref var source = ref _slices[i];
                ref var destination = ref slices[i];
                destination.groupStartIndex = groupCount;
                destination.groupCount = source.groups.Length;

                groupCount += destination.groupCount;
            }

            assigner.SetBuffer(EntityComponentAssigner.BufferOption.Override, entity, slices);
        }
    }
}
