using System;
using System.Collections.Generic;
using ZG;

//[EntityComponent(typeof(PhysicsTriggerEvent))]
[EntityComponent(typeof(GameFollower))]
[EntityComponent(typeof(GameRangeSpawnerNode))]
[EntityComponent(typeof(GameRangeSpawnerEntity))]
[EntityComponent(typeof(GameRangeSpawnerStatus))]
[EntityComponent(typeof(GameRangeSpawnerCoolDownTime))]
public class GameRangeSpawnerComponent : EntityProxyComponent, IEntityComponent
{
    [Serializable]
    public struct Node
    {
#if UNITY_EDITOR
        public string name;
#endif
        
        public int sliceIndex;
        public float inTime;
        public float outTime;
    }

    public bool isOverrideOrigin;
    public UnityEngine.Vector3 origin;

    [EntityComponents]
    public Type[] types
    {
        get
        {
            return isOverrideOrigin ? new[] { typeof(GameRangeSpawnerOrigin) } : null;
        }
    }

    public Node[] nodes;
    
    void IEntityComponent.Init(in Unity.Entities.Entity entity, EntityComponentAssigner assigner)
    {
        GameRangeSpawnerOrigin origin;
        origin.value = this.origin;
        assigner.SetComponentData(entity, origin);
        
        int numNodes = this.nodes == null ? 0 : this.nodes.Length;
        var nodes = new GameRangeSpawnerNode[numNodes];
        for (int i = 0; i < numNodes; ++i)
        {
            ref var source = ref this.nodes[i];
            ref var destination = ref nodes[i];
            
            destination.sliceIndex = source.sliceIndex;
            destination.inTime = source.inTime;
            destination.outTime = source.outTime;
        }

        assigner.SetBuffer(EntityComponentAssigner.BufferOption.Override, entity, nodes);
    }
}
