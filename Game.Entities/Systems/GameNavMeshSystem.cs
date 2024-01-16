using Unity.Collections;
using Unity.Mathematics;
using Unity.Entities;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Transforms;
using UnityEngine.Experimental.AI;
using ZG;

[BurstCompile, UpdateInGroup(typeof(InitializationSystemGroup)), UpdateAfter(typeof(EntityObjectSystemGroup))]
public partial struct GameNavMeshFactorySystem : ISystem
{
    private EntityQuery __groupToCreate;
    private EntityQuery __groupToDestroy;
    private NativeFactory<NavMeshQuery> __navMeshQueries;
/*#if ENABLE_UNITY_COLLECTIONS_CHECKS
    private System.Collections.Generic.Dictionary<Entity, NavMeshQuery> __queries;
#endif*/

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __groupToCreate = builder
                    .WithAll<GameNavMeshAgentQueryData>()
                    .WithNone<GameNavMeshAgentQuery>()
                    .Build(ref state);

        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __groupToDestroy = builder
                .WithAll <GameNavMeshAgentQuery> ()
                .WithNone<GameNavMeshAgentQueryData>()
                .Build(ref state);

        __navMeshQueries = new NativeFactory<NavMeshQuery>(Allocator.Persistent);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        foreach (var navMeshQuery in __navMeshQueries)
            navMeshQuery.Dispose();

        __navMeshQueries.Dispose();
        
/*#if ENABLE_UNITY_COLLECTIONS_CHECKS
        if(__queries != null)
        {
            foreach(var pair in __queries)
                pair.Value.Dispose();

            __queries = null;
        }
#endif*/

        //base.OnDestroy();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        //TODO:
        state.CompleteDependency();

        var entityManager = state.EntityManager;
        using (var entities = __groupToCreate.ToEntityArray(Allocator.Temp))
        using (var instances = __groupToCreate.ToComponentDataArray<GameNavMeshAgentQueryData>(Allocator.Temp))
        {
            entityManager.AddComponent<GameNavMeshAgentQuery>(__groupToCreate);

            int numEntities = entities.Length;
            GameNavMeshAgentQuery query;
            NavMeshQuery navMeshQuery;
            var navMeshWorld = NavMeshWorld.GetDefaultWorld();
            for (int i = 0; i < numEntities; ++i)
            {
                navMeshQuery = new NavMeshQuery(navMeshWorld, Allocator.Persistent, instances[i].pathNodePoolSize);

/*#if ENABLE_UNITY_COLLECTIONS_CHECKS
                if (__queries == null)
                    __queries = new System.Collections.Generic.Dictionary<Entity, NavMeshQuery>();

                __queries.Add(entities[i], navMeshQuery);
#endif*/
                query.value = __navMeshQueries.Create();
                query.value.value = navMeshQuery;
                entityManager.SetComponentData(entities[i], query);
            }
        }

        using (var instances = __groupToDestroy.ToComponentDataArray<GameNavMeshAgentQuery>(Allocator.Temp))
/*#if ENABLE_UNITY_COLLECTIONS_CHECKS
        using (var entities = __groupToDestroy.ToEntityArray(Allocator.Temp))
#endif*/
        {
            int numInstances = instances.Length;
            GameNavMeshAgentQuery instance;
            for (int i = 0; i < numInstances; ++i)
            {
                instance = instances[i];

/*#if ENABLE_UNITY_COLLECTIONS_CHECKS
                Entity entity = entities[i];
                __queries[entity].Dispose();
                __queries.Remove(entity);
#else*/
                instance.value.value.Dispose();
//#endif
                instance.value.Dispose();
            }

            entityManager.RemoveComponent<GameNavMeshAgentQuery>(__groupToDestroy);
        }
    }
}

/*[BurstCompile, AutoCreateIn("Server"), UpdateInGroup(typeof(GameNavMeshSystemGroup), OrderLast = true)]
public partial struct GameNavMeshStructChangeSystem : ISystem
{
    public readonly static int InnerloopBatchCount = 1;

    private EntityCommandPool<Entity>.Context __removeComponentCommander;
    private EntityAddComponentPool<GameNavMeshAgentTarget> __addComponentCommander;

    public EntityCommandPool<Entity> removeComponentPool => __removeComponentCommander.pool;

    public EntityCommandPool<EntityData<GameNavMeshAgentTarget>> addComponentPool => __addComponentCommander.value;

    public void OnCreate(ref SystemState state)
    {
        __removeComponentCommander = new EntityCommandPool<Entity>.Context(Allocator.Persistent);
        __addComponentCommander = new EntityAddComponentPool<GameNavMeshAgentTarget>(Allocator.Persistent);
    }

    public void OnDestroy(ref SystemState state)
    {
        __removeComponentCommander.Dispose();
        __addComponentCommander.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!__removeComponentCommander.isEmpty)
        {
            using (var container = new EntityCommandEntityContainer(Allocator.Temp))
            {
                __removeComponentCommander.MoveTo(container);

                container.RemoveComponent<GameNavMeshAgentTarget>(ref state);
            }
        }

        if(!__addComponentCommander.isEmpty)
            __addComponentCommander.Playback(InnerloopBatchCount, ref state);
    }
}*/

[BurstCompile, UpdateInGroup(typeof(TimeSystemGroup)), UpdateBefore(typeof(GameSyncSystemGroup)), UpdateAfter(typeof(StateMachineGroup))]
public partial struct GameNavMeshSystem : ISystem
{
    private struct Move
    {
        [ReadOnly]
        public NativeArray<Entity> entityArray;
        [ReadOnly]
        public NativeArray<GameNavMeshAgentTarget> targets;

        public NativeArray<GameNodeDirection> directions;

        public BufferAccessor<GameNodePosition> positions;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<GameNodeVersion> versions;

        public void Execute(int index)
        {
            Entity entity = entityArray[index];

            var positions = this.positions[index];
            SetPosition(index, entity, targets[index].position, ref positions, ref directions, ref versions);
        }
    }

    private struct FindPath
    {
        //public uint frameIndex;
        public float deltaTimeSq;
        [ReadOnly]
        public BufferAccessor<GameNavMeshAgentExtends> extends;
        [ReadOnly]
        public NativeArray<Entity> entityArray;
        [ReadOnly]
        public NativeArray<Translation> translations;
        [ReadOnly]
        public NativeArray<Rotation> rotations;
        [ReadOnly]
        public NativeArray<GameNavMeshAgentData> instances;
        [ReadOnly]
        public NativeArray<GameNavMeshAgentQuery> queries;
        [ReadOnly]
        public NativeArray<GameNavMeshAgentTarget> targets;
        [ReadOnly]
        public NativeArray<GameNodeStaticThreshold> staticThresholds;

        public NativeArray<GameNodeDirection> directions;

        public NativeArray<GameNavMeshAgentPathStatus> states;

        public BufferAccessor<GameNavMeshAgentWayPoint> wayPoints;

        public BufferAccessor<GameNodePosition> positions;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<GameNodeVersion> versions;

        public static bool Locate(
            int agentTypeID, 
            int areaMask, 
            in float3 position, 
            in NavMeshQuery navMeshQuery, 
            in DynamicBuffer<GameNavMeshAgentExtends> extends, 
            out NavMeshLocation location)
        {
            int length = extends.Length;
            for (int i = 0; i < length; ++i)
            {
                location = navMeshQuery.MapLocation(position, extends[i].value, agentTypeID, areaMask);
                if (navMeshQuery.IsValid(location))
                    return true;
            }
            
            location = default;
            return false;
        }

        public static bool FindWayPointIndex(
            int numWayPoints,
            int areaMask,
            in float3 direction, 
            in float3 translation, 
            in NavMeshLocation location, 
            in NavMeshQuery navMeshQuery, 
            in DynamicBuffer<GameNavMeshAgentWayPoint> wayPoints,
            ref float minDistance,
            ref int wayPointIndex)
        {
            bool result = false;
            int endWayPointIndex = numWayPoints - 1;
            float distance, dot;
            float3 source = wayPoints[wayPointIndex].value.location.position, destination, normal, position;
            UnityEngine.AI.NavMeshHit hit;
            for (int i = wayPointIndex + 1; i < numWayPoints; ++i)
            {
                destination = wayPoints[i].value.location.position;

                position = translation - source;
                normal = destination - source;

                dot = math.lengthsq(normal);
                if (dot > math.FLT_MIN_NORMAL)
                {
                    dot = math.dot(position, normal) / dot;
                    /*if (dot > 1.0f && i < endWayPointIndex)
                        continue;*/

                    //normal *= dot;
                }
                else
                {
                    if (i == endWayPointIndex)
                        dot = 1.0f;

                    //normal = float3.zero;
                }

                position = dot < 0.0f ? source : destination;

                if (navMeshQuery.Raycast(out hit, location, position, areaMask) == PathQueryStatus.Success && !hit.hit)
                {
                    normal = position - translation;
                    distance = math.dot(normal, direction);
                    if (distance < 0.0f)
                        distance = math.length(normal);

                    if (distance < minDistance)
                    {
                        minDistance = distance;

                        if (i == endWayPointIndex && dot > 1.0f)
                            wayPointIndex = numWayPoints;
                        else
                            wayPointIndex = dot > math.FLT_MIN_NORMAL ? i : i - 1;

                        result = true;
                    }
                }

                source = destination;
            }

            return result;
        }

        public bool Execute(int index)
        {
            var extends = this.extends[index];
            var positions = this.positions[index];
            var wayPoints = this.wayPoints[index];
            int numWayPoints = wayPoints.Length;

            bool isRepath, isVailTarget, result = false;
            Entity entity = entityArray[index];
            var position = translations[index].Value;
            var instance = instances[index];
            var status = states[index];
            var target = targets[index];
            ref var navMeshQuery = ref queries[index].value.value;
            NavMeshLocation targetLocation;
            if (target.destinationAreaMask == status.areaMask && 
                target.position.Equals(status.target) && 
                navMeshQuery.IsValid(status.destinationLocation) && 
                navMeshQuery.GetAgentTypeIdForPolygon(status.destinationLocation.polygon) == instance.agentTypeID)
            {
                isRepath = false;

                isVailTarget = true;

                targetLocation = status.destinationLocation;
            }
            else if (Locate(instance.agentTypeID, target.destinationAreaMask, target.position, navMeshQuery, extends, out targetLocation))
            {
                isRepath = targetLocation.polygon != status.destinationLocation.polygon;

                isVailTarget = true;

                status.areaMask = target.destinationAreaMask;
                status.target = target.position;
                status.destinationLocation = targetLocation;
            }
            else
            {
                if (numWayPoints > status.wayPointIndex && math.distancesq(wayPoints[numWayPoints - 1].value.location.position, target.position) < math.distancesq(position, target.position))
                {
                    isRepath = false;

                    isVailTarget = false;
                }
                else
                {
                    status.pathResult = PathQueryStatus.Failure;

                    if (positions.Length < 1 || !positions[0].value.Equals(target.position))
                    {
                        SetPosition(index, entity, target.position, ref positions, ref directions, ref versions);

                        status.pathResult |= PathQueryStatus.PartialResult;
                    }
                    else
                        result = true;

                    //status.frameIndex = frameIndex;
                    status.position = position;
                    states[index] = status;

                    //UnityEngine.Debug.Log($"{instance.agentTypeID} Target Fail.");

                    return result;
                }
            }

            NavMeshLocation location;
            float3 forward = math.forward(rotations[index].Value);
            if (isRepath)
                location = default;
            else
            {
                location = status.Move(navMeshQuery, extends[0].value, position, instance.agentTypeID, target.sourceAreaMask);
                if (!navMeshQuery.IsValid(location))
                    isRepath = true;
                else if (numWayPoints > status.wayPointIndex)
                {
                    var wayPointLocation = wayPoints[status.wayPointIndex].value.location;
                    float3 wayPointPosition = wayPointLocation.position, wayPointDistance = wayPointPosition - position;
                    float wayPointLength = math.length(wayPointDistance), minDistance = wayPointLength;
                    int areaMask = target.sourceAreaMask | target.destinationAreaMask, wayPointIndex = status.wayPointIndex;

                    if (FindWayPointIndex(
                        numWayPoints,
                        areaMask,
                        forward,
                        position,
                        location,
                        navMeshQuery,
                        wayPoints,
                        ref minDistance,
                        ref wayPointIndex) &&
                        wayPointIndex > status.wayPointIndex)
                        status.wayPointIndex = wayPointIndex;
                    else
                    {
                        wayPointIndex = 0;
                        if (!FindWayPointIndex(
                           status.wayPointIndex + 1,
                           areaMask,
                           forward,
                           position,
                           location,
                           navMeshQuery,
                           wayPoints,
                           ref minDistance,
                           ref wayPointIndex))
                            wayPointIndex = status.wayPointIndex;

                        bool isMove = math.distancesq(status.position, position) / deltaTimeSq > staticThresholds[index].value;
                        if (wayPointIndex == status.wayPointIndex)
                        {
                            /*if (positions.Length < 1)
                            {
                                if (wayPointLocation.polygon == location.polygon ||
                                    wayPointLength <= stoppingDistance ||
                                    status.wayPointIndex < numWayPoints - 1 && wayPoints[status.wayPointIndex + 1].value.location.polygon == location.polygon)
                                    ++status.wayPointIndex;
                                else
                                    status.wayPointIndex = 0;
                            }
                            else if (isMove)
                            {
                                status.frameIndex = frameIndex;
                                states[index] = status;

                                return;
                            }
                            else
                                ++status.wayPointIndex;*/

                            if (isMove && positions.Length > 0)
                            {
                                status.position = position;
                                status.sourceLocation = location;
                                states[index] = status;

                                return result;
                            }

                            ++status.wayPointIndex;
                        }
                        else if (wayPointIndex < status.wayPointIndex - 1)
                        {
                            if (!isMove && !status.position.Equals(position))
                                status.wayPointIndex = wayPointIndex;
                            else if (wayPointLength > math.FLT_MIN_NORMAL)
                            {
                                if (math.dot(forward, math.normalizesafe((float3)wayPoints[wayPointIndex].value.location.position - position)) > math.dot(forward, wayPointDistance / wayPointLength))
                                    status.wayPointIndex = wayPointIndex;
                            }
                        }
                    }

                    if (status.wayPointIndex > 0)
                    {
                        status.sourceLocation = location;

                        if (status.wayPointIndex >= numWayPoints - 1)
                        {
                            //UnityEngine.Debug.Log("Finish Path");

                            /*SetPosition(index, target.position, positions);

                            status.nextPosition = target.position;*/

                            if (positions.Length < 1 || !positions[0].value.Equals(target.position))
                                SetPosition(index, entity, target.position, ref positions, ref directions, ref versions);

                            result = true;

                            status.position = position;
                            states[index] = status;

                            return result;
                        }

                        if (isVailTarget)
                        {
                            int endWayPointIndex = numWayPoints - 1;
                            var wayPoint = wayPoints[endWayPointIndex];
                            wayPoint.value.location = targetLocation;
                            wayPoints[endWayPointIndex] = wayPoint;
                        }
                    }
                    else
                        isRepath = true;
                }
                else
                    isRepath = (status.pathResult & PathQueryStatus.InProgress) != PathQueryStatus.InProgress;
            }

            status.position = position;

            if (isRepath)
            {
                wayPoints.Clear();

                numWayPoints = 0;

                status.wayPointIndex = 0;

                bool isNeedToLocate = true;
                if (isVailTarget)
                {
                    if (navMeshQuery.IsValid(location))
                    {
                        status.sourceLocation = location;
                        isNeedToLocate = false;
                    }
                    else
                        isNeedToLocate = !Locate(instance.agentTypeID, target.sourceAreaMask, position, navMeshQuery, extends, out status.sourceLocation);
                }

                if (isNeedToLocate)
                {
                    if (positions.Length < 1 || !positions[0].value.Equals(target.position))
                    {
                        SetPosition(index, entity, target.position, ref positions, ref directions, ref versions);

                        status.pathResult = PathQueryStatus.PartialResult;
                    }
                    else
                    {
                        result = true;

                        status.pathResult = PathQueryStatus.Failure;
                    }

                    //status.frameIndex = frameIndex;
                    states[index] = status;

                    return result;
                }

                status.pathResult = navMeshQuery.BeginFindPath(
                            status.sourceLocation,
                            targetLocation,
                            target.sourceAreaMask | target.destinationAreaMask);

                //UnityEngine.Debug.Log($"FindPath {status} Source {navMeshQuery.IsValid(location)} Destination {navMeshQuery.IsValid(targetLocation)}");
            }

            float3 locationPosition;
            if (status.wayPointIndex >= numWayPoints)
            {
                if ((status.pathResult & PathQueryStatus.InProgress) == PathQueryStatus.InProgress)
                {
                    var pathResult = navMeshQuery.UpdateFindPath(
                            instance.iteractorCount,
                            out int iterationsPerformed);

                    if((pathResult & PathQueryStatus.InProgress) == PathQueryStatus.InProgress)
                    {
                        if(isRepath || status.pathResult != pathResult)
                        {
                            status.pathResult = pathResult;

                            states[index] = status;
                        }

                        return result;
                    }

                    status.pathResult = pathResult;
                }

                if ((status.pathResult & PathQueryStatus.Success) != PathQueryStatus.Success)
                {
                    //UnityEngine.Debug.Log($"FindPath {status} Source {location.position} Destination {targetLocation.position}");

                    status.sourceLocation = navMeshQuery.MoveLocation(status.sourceLocation, target.position, target.sourceAreaMask | target.destinationAreaMask);
                    locationPosition = status.sourceLocation.position;
                    if (positions.Length < 1 || !positions[0].value.Equals(locationPosition))
                    {
                        SetPosition(index, entity, locationPosition, ref positions, ref directions, ref versions);

                        status.pathResult |= PathQueryStatus.PartialResult;
                    }
                    else
                        result = true;

                    states[index] = status;
                    
                    return result;
                }
                /*else
                {
                    int temp = (int)(status.pathResult & ~PathQueryStatus.Success);
                    if (temp != 0)
                        UnityEngine.Debug.Log($"FindPath {temp}");
                }*/

                status.pathResult = navMeshQuery.EndFindPath(out int pathLength);

                DynamicBuffer<NavMeshWayPoint> navMeshWayPoints;
                using (var polygons = new NativeArray<PolygonId>(pathLength, Allocator.Temp))
                {
                    pathLength = navMeshQuery.GetPathResult(polygons);

                    navMeshWayPoints = wayPoints.Reinterpret<NavMeshWayPoint>();
                    DynamicBuffer<float> vertexSides = default;
                    status.wayResult = navMeshQuery.FindStraightPath(
                                position,
                                target.position,
                                polygons,
                                ref navMeshWayPoints,
                                ref vertexSides);
                }

                if ((status.wayResult & PathQueryStatus.Success) != PathQueryStatus.Success)
                {
                    //UnityEngine.Debug.Log($"FindStraightPath {(int)status.wayResult}");

                    status.sourceLocation = navMeshQuery.MoveLocation(status.sourceLocation, target.position, target.sourceAreaMask | target.destinationAreaMask);
                    locationPosition = status.sourceLocation.position;
                    if (positions.Length < 1 || !positions[0].value.Equals(locationPosition))
                    {
                        SetPosition(index, entity, locationPosition, ref positions, ref directions, ref versions);

                        status.pathResult |= PathQueryStatus.PartialResult;
                    }
                    else
                        result = true;

                    states[index] = status;

                    return result;
                }

                numWayPoints = navMeshWayPoints.Length;
                if (numWayPoints < 3)
                {
                    status.wayPointIndex = numWayPoints - 1;
                    if (numWayPoints < 1)
                    {
                        states[index] = status;

                        return true;
                    }

                    result = true;
                }
                else
                {
                    float wayPointDistance = math.distance(position, navMeshWayPoints[0].location.position);

                    if (FindWayPointIndex(
                        numWayPoints,
                        target.sourceAreaMask | target.destinationAreaMask,
                        forward,
                        position,
                        status.sourceLocation,
                        navMeshQuery,
                        wayPoints,
                        ref wayPointDistance,
                        ref status.wayPointIndex))
                    {
                        if (status.wayPointIndex == numWayPoints)
                        {
                            states[index] = status;

                            return true;
                        }
                    }
                    else
                        status.wayPointIndex = 0;

                }
            }

            locationPosition = wayPoints[status.wayPointIndex].value.location.position;
            if (positions.Length < 1 || !positions[0].value.Equals(locationPosition))
                SetPosition(index, entity, locationPosition, ref positions, ref directions, ref versions);
            
            states[index] = status;

            return result;
        }
    }
    
    [BurstCompile]
    private struct FindPathEx : IJobChunk, IEntityCommandProducerJob
    {
        public float deltaTimeSq;
        [ReadOnly]
        public EntityTypeHandle entityType;
        [ReadOnly]
        public BufferTypeHandle<GameNavMeshAgentExtends> extendType;
        [ReadOnly]
        public ComponentTypeHandle<Translation> translationType;
        [ReadOnly]
        public ComponentTypeHandle<Rotation> rotationType;
        [ReadOnly]
        public ComponentTypeHandle<GameNavMeshAgentData> instanceType;
        [ReadOnly]
        public ComponentTypeHandle<GameNavMeshAgentQuery> queryType;
        [ReadOnly]
        public ComponentTypeHandle<GameNodeStaticThreshold> staticThresholdType;

        public ComponentTypeHandle<GameNavMeshAgentTarget> targetType;

        public ComponentTypeHandle<GameNodeDirection> directionType;

        public ComponentTypeHandle<GameNavMeshAgentPathStatus> statusType;

        public BufferTypeHandle<GameNavMeshAgentWayPoint> wayPointType;

        public BufferTypeHandle<GameNodePosition> positionType;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<GameNodeVersion> versions;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            var entityArray = chunk.GetNativeArray(entityType);
            if (chunk.Has(ref positionType))
            {
                if (chunk.Has(ref instanceType))
                {
                    if (chunk.Has(ref queryType))
                    {
                        FindPath findPath;
                        findPath.deltaTimeSq = deltaTimeSq;
                        findPath.extends = chunk.GetBufferAccessor(ref extendType);
                        findPath.entityArray = entityArray;
                        findPath.translations = chunk.GetNativeArray(ref translationType);
                        findPath.rotations = chunk.GetNativeArray(ref rotationType);
                        findPath.instances = chunk.GetNativeArray(ref instanceType);
                        findPath.staticThresholds = chunk.GetNativeArray(ref staticThresholdType);
                        findPath.queries = chunk.GetNativeArray(ref queryType);
                        findPath.targets = chunk.GetNativeArray(ref targetType);
                        findPath.directions = chunk.GetNativeArray(ref directionType);
                        findPath.states = chunk.GetNativeArray(ref statusType);
                        findPath.wayPoints = chunk.GetBufferAccessor(ref wayPointType);
                        findPath.positions = chunk.GetBufferAccessor(ref positionType);
                        findPath.versions = versions;

                        var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                        while (iterator.NextEntityIndex(out int i))
                        {
                            if(findPath.Execute(i))
                                chunk.SetComponentEnabled(ref targetType, i, false);
                        }
                    }
                }
                else
                {
                    Move move;
                    move.entityArray = entityArray;
                    move.targets = chunk.GetNativeArray(ref targetType);
                    move.directions = chunk.GetNativeArray(ref directionType);
                    move.positions = chunk.GetBufferAccessor(ref positionType);
                    move.versions = versions;

                    var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                    while (iterator.NextEntityIndex(out int i))
                    {
                        move.Execute(i);

                        chunk.SetComponentEnabled(ref targetType, i, false);
                    }
                }
            }
            else
            {
                var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (iterator.NextEntityIndex(out int i))
                    chunk.SetComponentEnabled(ref targetType, i, false);
            }
        }
    }

    public static void SetPosition(
        int index,
        Entity entity,
        float3 point,
        ref DynamicBuffer<GameNodePosition> positions,
        ref NativeArray<GameNodeDirection> directions,
        ref ComponentLookup<GameNodeVersion> versions)
    {
        var version = versions[entity];
        version.type = GameNodeVersion.Type.Position;
        ++version.value;

        if (index < directions.Length && !directions[index].value.Equals(float3.zero))
        {
            GameNodeDirection direction;
            direction.mode = GameNodeDirection.Mode.None;
            direction.version = version.value;
            direction.value = float3.zero;
            directions[index] = direction;

            version.type |= GameNodeVersion.Type.Direction;
        }

        positions.Clear();

        GameNodePosition position;
        position.mode = GameNodePosition.Mode.Normal;
        position.version = version.value;
        position.value = point;
        positions.Add(position);

        versions[entity] = version;
        versions.SetComponentEnabled(entity, true);
    }

    private EntityQuery __group;
    private EntityTypeHandle __entityType;
    private BufferTypeHandle<GameNavMeshAgentExtends> __extendType;
    private ComponentTypeHandle<Translation> __translationType;
    private ComponentTypeHandle<Rotation> __rotationType;
    private ComponentTypeHandle<GameNavMeshAgentData> __instanceType;
    private ComponentTypeHandle<GameNavMeshAgentQuery> __queryType;
    private ComponentTypeHandle<GameNodeStaticThreshold> __staticThresholdType;
    private ComponentTypeHandle<GameNavMeshAgentTarget> __targetType;
    private ComponentTypeHandle<GameNodeDirection> __directionType;
    private ComponentTypeHandle<GameNavMeshAgentPathStatus> __statusType;
    private BufferTypeHandle<GameNavMeshAgentWayPoint> __wayPointType;
    private BufferTypeHandle<GameNodePosition> __positionType;

    private ComponentLookup<GameNodeVersion> __versions;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        using(var builder = new EntityQueryBuilder(Allocator.Temp))
            __group = builder
                    .WithAllRW<GameNavMeshAgentTarget>()
                    .Build(ref state);

        __entityType = state.GetEntityTypeHandle();
        __extendType = state.GetBufferTypeHandle<GameNavMeshAgentExtends>(true);
        __translationType = state.GetComponentTypeHandle<Translation>(true);
        __rotationType = state.GetComponentTypeHandle<Rotation>(true);
        __instanceType = state.GetComponentTypeHandle<GameNavMeshAgentData>(true);
        __queryType = state.GetComponentTypeHandle<GameNavMeshAgentQuery>(true);
        __targetType = state.GetComponentTypeHandle<GameNavMeshAgentTarget>();
        __staticThresholdType = state.GetComponentTypeHandle<GameNodeStaticThreshold>(true);
        __directionType = state.GetComponentTypeHandle<GameNodeDirection>();
        __statusType = state.GetComponentTypeHandle<GameNavMeshAgentPathStatus>();
        __wayPointType = state.GetBufferTypeHandle<GameNavMeshAgentWayPoint>();
        __positionType = state.GetBufferTypeHandle<GameNodePosition>();
        __versions = state.GetComponentLookup<GameNodeVersion>();

        //__time = new GameUpdateTime(ref state);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {

    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        FindPathEx findPath;
        //findPath.frameIndex = __time.RollbackTime.frameIndex / __time.frameCount;// updateData.GetFrameIndex(syncData.realFrameIndex);
        findPath.deltaTimeSq = state.WorldUnmanaged.Time.DeltaTime;//__time.delta;// updateData.GetDelta(syncData.now.delta);
        findPath.deltaTimeSq *= findPath.deltaTimeSq;
        findPath.entityType = __entityType.UpdateAsRef(ref state);
        findPath.extendType = __extendType.UpdateAsRef(ref state);
        findPath.translationType = __translationType.UpdateAsRef(ref state);
        findPath.rotationType = __rotationType.UpdateAsRef(ref state);
        findPath.instanceType = __instanceType.UpdateAsRef(ref state);
        findPath.queryType = __queryType.UpdateAsRef(ref state);
        findPath.targetType = __targetType.UpdateAsRef(ref state);
        findPath.staticThresholdType = __staticThresholdType.UpdateAsRef(ref state);
        findPath.directionType = __directionType.UpdateAsRef(ref state);
        findPath.statusType = __statusType.UpdateAsRef(ref state);
        findPath.wayPointType = __wayPointType.UpdateAsRef(ref state);
        findPath.positionType = __positionType.UpdateAsRef(ref state);
        findPath.versions = __versions.UpdateAsRef(ref state);

        state.Dependency = findPath.ScheduleParallelByRef(__group, state.Dependency);
    }
}
