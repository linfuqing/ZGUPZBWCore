using System;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine.Experimental.AI;
using ZG;

public struct GameNavMeshAgentQuery : ICleanupComponentData
{
    public NativeFactoryObject<NavMeshQuery> value;
}

public struct GameNavMeshAgentQueryData : IComponentData
{
    public int pathNodePoolSize;
}

[Serializable]
public struct GameNavMeshAgentData : IComponentData
{
    public int iteractorCount;

    public int agentTypeID;

    //public float3 extends;
}

public struct GameNavMeshAgentTarget : IComponentData, IEnableableComponent
{
    public int sourceAreaMask;
    public int destinationAreaMask;
    public float3 position;
}

public struct GameNavMeshAgentPathStatus : IComponentData
{
    public PathQueryStatus pathResult;
    public PathQueryStatus wayResult;
    public int wayPointIndex;
    public uint frameIndex;
    public int areaMask;
    public float3 target;
    public float3 translation;
    public NavMeshLocation destinationLocation;
    public NavMeshLocation sourceLocation;

    public override string ToString()
    {
        return $"({pathResult} : {wayResult})";
    }
}

public struct GameNavMeshAgentExtends : IBufferElementData
{
    public float3 value;

    public static implicit operator GameNavMeshAgentExtends(float3 value)
    {
        GameNavMeshAgentExtends extends;
        extends.value = value;
        return extends;
    }
}

public struct GameNavMeshAgentWayPoint : IBufferElementData
{
    public NavMeshWayPoint value;
}

[EntityComponent(typeof(GameNavMeshAgentTarget))]
[EntityComponent(typeof(GameNavMeshAgentQueryData))]
[EntityComponent(typeof(GameNavMeshAgentPathStatus))]
[EntityComponent(typeof(GameNavMeshAgentExtends))]
[EntityComponent(typeof(GameNavMeshAgentWayPoint))]
public class GameNavMeshAgentComponent : ZG.ComponentDataProxy<GameNavMeshAgentData>
{
    [UnityEngine.SerializeField]
    internal int _pathNodePoolSize = 1024;

    internal GameNavMeshAgentExtends[] _extends = new GameNavMeshAgentExtends[]
    {
        new float3(1.0f, 0.5f, 1.0f), 
        new float3(3.0f, 0.5f, 3.0f), 
        new float3(5.0f, 1.0f, 5.0f),
        new float3(10.0f, 3.0f, 10.0f),
        new float3(30.0f, 10.0f, 30.0f)
    };

    public void ResetTarget()
    {
        this.SetComponentEnabled<GameNavMeshAgentTarget>(false);
    }

    public override void Init(in Entity entity, EntityComponentAssigner assigner)
    {
        base.Init(entity, assigner);

        assigner.SetComponentEnabled<GameNavMeshAgentTarget>(entity, false);

        GameNavMeshAgentQueryData instance;
        instance.pathNodePoolSize = _pathNodePoolSize;
        assigner.SetComponentData(entity, instance);

        assigner.SetBuffer(EntityComponentAssigner.BufferOption.Override, entity, _extends);
    }

    private void OnDrawGizmos()
    {

#if UNITY_EDITOR
        if (!UnityEditor.EditorApplication.isPlaying)
            return;
#endif

        var wayPoints = this.GetBuffer<GameNavMeshAgentWayPoint>();
        int numWayPoints = wayPoints == null ? 0 : wayPoints.Length;
        if (numWayPoints > 0)
        {
            var color = UnityEngine.Gizmos.color;
            UnityEngine.Vector3 extends = _extends[0].value, source = wayPoints[0].value.location.position, destination;
            float radius = math.max(math.max(extends.x, extends.y), extends.z);
            int wayPointIndex = this.GetComponentData<GameNavMeshAgentPathStatus>().wayPointIndex;
            UnityEngine.Gizmos.DrawSphere(source, radius);
            for (int i = 1; i < numWayPoints; ++i)
            {
                destination = wayPoints[i].value.location.position;
                UnityEngine.Gizmos.color = i == wayPointIndex ? UnityEngine.Color.green : color;
                UnityEngine.Gizmos.DrawSphere(destination, radius);
                UnityEngine.Gizmos.DrawLine(source, destination);
                UnityEngine.Gizmos.color = color;
                source = destination;
            }
        }
    }
}
