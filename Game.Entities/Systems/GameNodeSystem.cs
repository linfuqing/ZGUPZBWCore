using Unity.Jobs;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Entities;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using Unity.Transforms;
using ZG;
using Math = ZG.Mathematics.Math;

[BurstCompile, UpdateInGroup(typeof(GameRollbackSystemGroup)), UpdateBefore(typeof(GameNodeSystem))]
public partial struct GameStatusSystemGroup : ISystem
{
    private SystemGroup __systemGroup;

    public void OnCreate(ref SystemState state)
    {
        __systemGroup = SystemGroupUtility.GetOrCreateSystemGroup(state.World, typeof(GameStatusSystemGroup));
    }

    public void OnDestroy(ref SystemState state)
    {

    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var world = state.WorldUnmanaged;
        __systemGroup.Update(ref world);
    }
}

[BurstCompile, UpdateInGroup(typeof(GameRollbackSystemGroup))]
public partial struct GameNodeInitSystemGroup : ISystem
{
    private SystemGroup __systemGroup;

    public void OnCreate(ref SystemState state)
    {
        __systemGroup = SystemGroupUtility.GetOrCreateSystemGroup(state.World, typeof(GameNodeInitSystemGroup));
    }

    public void OnDestroy(ref SystemState state)
    {

    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var world = state.WorldUnmanaged;
        __systemGroup.Update(ref world);
    }
}

[BurstCompile, UpdateInGroup(typeof(GameNodeInitSystemGroup))]
public partial struct GameNodeInitSystem : ISystem
{
    private struct UpdateParents
    {
        [ReadOnly]
        public NativeArray<Entity> entityArray;

        [ReadOnly]
        public NativeArray<GameNodeParent> parents;

        [ReadOnly]
        public NativeArray<GameNodeDirection> directions;

        [ReadOnly]
        public NativeArray<GameNodeVersion> versions;

        public BufferAccessor<GameNodePosition> positions;

        [NativeDisableContainerSafetyRestriction]
        public BufferLookup<GameNodePosition> positionMap;

        [NativeDisableContainerSafetyRestriction]
        public ComponentLookup<GameNodeDirection> directionMap;

        public void Execute(int index)
        {
            var parent = parents[index];
            if (parent.authority < 1)
                return;

            var version = versions[index];
            if (directionMap.HasComponent(parent.entity))
            {
                bool isSet = false;
                if ((version.type & GameNodeVersion.Type.Direction) == GameNodeVersion.Type.Direction &&
                    index < directions.Length)
                {
                    var direction = directions[index];
                    if (direction.version == version.value)
                    {
                        directionMap[parent.entity] = direction;

                        isSet = true;
                    }
                }

                if (!isSet)
                    directionMap[parent.entity] = default;
            }

            if (positionMap.HasBuffer(parent.entity))
            {
                var destinations = positionMap[parent.entity];
                destinations.Clear();

                if ((version.type & GameNodeVersion.Type.Position) == GameNodeVersion.Type.Position &&
                    index < positions.Length)
                {
                    var sources = positions[index];
                    foreach (var position in sources)
                    {
                        if (position.version != version.value)
                            continue;

                        destinations.Add(position);
                    }
                }
            }
        }
    }

    [BurstCompile]
    private struct UpdateParentsEx : IJobChunk
    {
        [ReadOnly]
        public EntityTypeHandle entityType;

        [ReadOnly]
        public ComponentTypeHandle<GameNodeParent> parentType;

        public ComponentTypeHandle<GameNodeVersion> versionType;

        public ComponentTypeHandle<GameNodeDirection> directionType;

        public BufferTypeHandle<GameNodePosition> positionType;

        [NativeDisableContainerSafetyRestriction]
        public BufferLookup<GameNodePosition> positions;

        [NativeDisableContainerSafetyRestriction]
        public ComponentLookup<GameNodeDirection> directions;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            UpdateParents updateParents;
            updateParents.entityArray = chunk.GetNativeArray(entityType);
            updateParents.parents = chunk.GetNativeArray(ref parentType);
            updateParents.versions = chunk.GetNativeArray(ref versionType);
            updateParents.directions = chunk.GetNativeArray(ref directionType);
            updateParents.positions = chunk.GetBufferAccessor(ref positionType);
            updateParents.positionMap = positions;
            updateParents.directionMap = directions;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
            {
                updateParents.Execute(i);

                chunk.SetComponentEnabled(ref versionType, i, false);
            }
        }
    }

    private EntityQuery __group;

    private EntityTypeHandle __entityType;

    private ComponentTypeHandle<GameNodeParent> __parentType;

    private ComponentTypeHandle<GameNodeVersion> __versionType;

    private ComponentTypeHandle<GameNodeDirection> __directionType;

    private BufferTypeHandle<GameNodePosition> __positionType;

    private BufferLookup<GameNodePosition> __positions;

    private ComponentLookup<GameNodeDirection> __directions;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __group = builder
                .WithAll<GameNodeParent>()
                .WithAllRW<GameNodeVersion>()
                .WithAnyRW<GameNodeDirection, GameNodePosition>()
                .Build(ref state);

        __entityType = state.GetEntityTypeHandle();
        __parentType = state.GetComponentTypeHandle<GameNodeParent>(true);
        __versionType = state.GetComponentTypeHandle<GameNodeVersion>();
        __directionType = state.GetComponentTypeHandle<GameNodeDirection>();
        __positionType = state.GetBufferTypeHandle<GameNodePosition>();
        __positions = state.GetBufferLookup<GameNodePosition>();
        __directions = state.GetComponentLookup<GameNodeDirection>();
    }

    public void OnDestroy(ref SystemState state)
    {

    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        UpdateParentsEx updateParents;
        updateParents.entityType = __entityType.UpdateAsRef(ref state);
        updateParents.parentType = __parentType.UpdateAsRef(ref state);
        updateParents.versionType = __versionType.UpdateAsRef(ref state);
        updateParents.directionType = __directionType.UpdateAsRef(ref state);
        updateParents.positionType = __positionType.UpdateAsRef(ref state);
        updateParents.positions = __positions.UpdateAsRef(ref state);
        updateParents.directions = __directions.UpdateAsRef(ref state);

        state.Dependency = updateParents.ScheduleParallelByRef(__group, state.Dependency);
    }
}

[BurstCompile, UpdateInGroup(typeof(GameRollbackSystemGroup)), UpdateBefore(typeof(GameUpdateSystemGroup))]
public partial struct GameNodeSystem : ISystem
{
    /*private struct LogInfo
    {
        public uint frameIndex;
        public float value;
        public Entity entity;

        public LogInfo(uint frameIndex, float value, Entity entity)
        {
            this.frameIndex = frameIndex;
            this.value = value;
            this.entity = entity;
        }

        public override string ToString()
        {
            return entity.ToString() + value + ":" + frameIndex;
        }
    }

    private struct Log : IJob
    {
        public NativeQueue<LogInfo> logInfos;

        public void Execute()
        {
            while (logInfos.TryDequeue(out var logInfo))
                UnityEngine.Debug.Log(logInfo);
        }
    }*/

    private struct UpdateTransforms
    {
        public bool isUpdate;
        public float deltaTime;
        public GameDeadline time;

        [ReadOnly]
        public BufferAccessor<GameNodeSpeedSection> speedSections;
        [ReadOnly]
        public NativeArray<Translation> translations;
        [ReadOnly]
        public NativeArray<GameNodeStaticThreshold> staticThresholds;
        [ReadOnly]
        public NativeArray<GameNodeStoppingDistance> stoppingDistances;
        [ReadOnly]
        public NativeArray<GameNodeStatus> states;
        [ReadOnly]
        public NativeArray<GameNodeDelay> delay;
        [ReadOnly]
        public NativeArray<GameNodeSurface> surfaces;
        [ReadOnly]
        public NativeArray<GameNodeSpeed> speeds;
        [ReadOnly]
        public NativeArray<GameNodeSpeedScale> speedScales;
        [ReadOnly]
        public NativeArray<GameNodeDirection> directions;

        public NativeArray<GameNodeDesiredStatus> desiredStates;

        public NativeArray<GameNodeDesiredVelocity> desiredVelocities;

        public NativeArray<GameNodeAngle> angles;

        public NativeArray<GameNodeDirect> directs;

        public NativeArray<GameNodeVelocity> velocities;

        public BufferAccessor<GameNodePosition> positions;

#if GAME_DEBUG_COMPARSION
        //public NativeQueue<LogInfo>.ParallelWriter logInfos;

        public uint frameIndex;

        public FixedString32Bytes statusName;
        public FixedString32Bytes dataName;
        public FixedString32Bytes oldAngleName;
        public FixedString32Bytes oldVelocityName;
        public FixedString32Bytes deltaTimeName;
        public FixedString32Bytes speedScaleName;
        public FixedString32Bytes distanceName;
        public FixedString32Bytes translationName;
        public FixedString32Bytes rotationName;
        public FixedString32Bytes positionName;
        public FixedString32Bytes positionCountName;
        public FixedString32Bytes nextVelocityName;
        public FixedString32Bytes newVelocityName;
        public FixedString32Bytes newAngleName;

        [ReadOnly]
        public NativeArray<Entity> entityArray;

        public ComparisonStream<uint> stream;
        [ReadOnly]
        public NativeArray<GameEntityIndex> entityIndices;
#endif
        public void Execute(int index)
        {
            GameNodeStatus status = states.Length > index ? states[index] : default;
            double now = this.time;
            float deltaTime = this.deltaTime, elapsedTime;
            int i, length;
            bool isStatusDelay = (status.value & GameNodeStatus.DELAY) == GameNodeStatus.DELAY;
            if (isStatusDelay)
                deltaTime = 0.0f;
            else
            {
                GameNodeDelay delay = this.delay.Length > index ? this.delay[index] : default;
                //UnityEngine.Debug.Log($"{entityArray[index]} : {delay.time} : {frameIndex}");
                //double time = delay.time;
                deltaTime = delay.Clamp(now, deltaTime);
            }

            //float speed = speeds[index].value;
            
            //GameNodeData instance = instances[index];
            //float speedScale = index < speedScales.Length ? speedScales[index].value : GameNodeSpeedScale.Normal;

            //instance.angularSpeed = math.abs(instance.angularSpeed * speedScale);

#if GAME_DEBUG_COMPARSION
            //UnityEngine.Debug.Log($"Node {entityIndices[index].value} : {frameIndex} : {entityArray[index].Index} : {status.value} : {speedScales[index].value.value} : {this.translations[index].Value}");

            stream.Begin(entityIndices[index].value);
            stream.Assert(statusName, status.value);
            stream.Assert(oldAngleName, angles[index].value);
            stream.Assert(deltaTimeName, deltaTime);
            stream.Assert(speedScaleName, index < speedScales.Length ? speedScales[index].value : GameNodeSpeedScale.Normal);
            stream.Assert(dataName, velocities[index].value);
            stream.Assert(rotationName, surfaces[index].rotation);
            stream.Assert(translationName, translations[index].Value);
#endif

            if (deltaTime > math.FLT_MIN_NORMAL)
            {
                bool isMove, isOnPath = false;
                float angleValue = angles[index].value,
                    speedScale = index < speedScales.Length ? speedScales[index].value : GameNodeSpeedScale.Normal,
                    maxSpeed = speeds[index].value, 
                    speed = math.max(math.abs(maxSpeed * speedScale), math.FLT_MIN_NORMAL);
                float3 up = math.up(),
                    forward = math.forward(quaternion.RotateY(angleValue)),
                    source = float3.zero,
                    destination = source;
                GameNodePosition position;
                var direction = index < directions.Length ? directions[index] : default;
                var direct = isUpdate ? default : directs[index];
                GameNodeDesiredVelocity desiredVelocity;
                DynamicBuffer<GameNodePosition> positions = default;

#if GAME_DEBUG_COMPARSION
                stream.Assert(distanceName, direct.value);
#endif

                //instance.speed = math.max(math.abs(instance.speed * speedScale), math.FLT_MIN_NORMAL);

                float sign = 1.0f/*math.sign(math.dot(nextVelocity, forward))*/, lengthSq = math.lengthsq(direction.value);
                if (lengthSq > math.FLT_MIN_NORMAL)
                {
                    /*UnityEngine.Debug.Log(
                        isUpdate.ToString() +
                        frameIndex.ToString() + ":" + 
                        speedScale.value + ":" + 
                        velocityDirect.value + 
                        entityArray[index].ToString() +
                        translations[index].Value +
                        info.distance + ":" +
                        velocity.value + deltaTime);*/

                    isMove = true;

                    if (direction.mode == GameNodeDirection.Mode.Backward)
                        sign = -1.0f;

                    desiredVelocity.value = math.mul(math.inverse(index < surfaces.Length ? surfaces[index].rotation : quaternion.identity), direction.value);

#if GAME_DEBUG_COMPARSION
                    stream.Assert(nextVelocityName, desiredVelocity.value);
#endif

                    desiredVelocity.value *= speed;

                    float scale = math.sqrt(lengthSq);

                    speed *= scale;

                    //instance.angularSpeed *= scale;
                }
                else
                {
                    if (index < this.positions.Length)
                    {
                        positions = this.positions[index];
                        length = positions.Length;
                    }
                    else
                        length = 0;

                    if (length > 0)
                    {
                        //bool isBackward;
                        int count = 0;
                        float time, stoppingDistance = stoppingDistances[index].value;
                        float3 distance;
                        quaternion rotation = index < surfaces.Length ? surfaces[index].rotation : quaternion.identity;
                        RigidTransform transform = math.inverse(math.RigidTransform(rotation, translations[index].Value + math.mul(rotation, direct.value)));

                        isMove = false;
                        elapsedTime = deltaTime;
                        desiredVelocity.value = float3.zero;
                        for (i = 0; i < length; ++i)
                        {
                            position = positions[i];

#if GAME_DEBUG_COMPARSION
                            //UnityEngine.Debug.Log($"Pos {entityArray[index].Index} : {entityIndices[index].value} : {frameIndex} : {position.value}");
                            stream.Assert(positionName, position.value);
#endif

                            /*UnityEngine.Debug.Log(
                                isUpdate.ToString() + 
                                frameIndex.ToString() + 
                                length.ToString() + 
                                entityArray[index].ToString() + 
                                position + 
                                transform + 
                                info.distance + ":" +
                                velocity.value + deltaTime);*/

                            destination = math.transform(transform, position.value);
                            distance = destination - source;

                            /*switch(position.mode)
                            {
                                case GameNodePosition.Mode.Limit:
                                    if(math.dot(forward, distance) < 0.0f)
                                    {
                                        ++count;

                                        continue;
                                    }
                                    break;
                            }*/

                            time = math.length(distance.xz);
                            /*isBackward = math.abs(Math.SignedAngle(forward, distance / time, up)) * 2.0f > math.PI;

                            if (position.mode == GameNodePosition.Mode.Normal)
                            {
                                if (!isBackward)
                                    position.mode = GameNodePosition.Mode.Limit;
                            }
                            else if (isBackward)
                            {
                                ++count;

                                continue;
                            }*/

                            {
                                time /= speed;
                                if (time < elapsedTime)
                                {
                                    ++count;

                                    if (time > math.FLT_MIN_NORMAL)
                                    {
                                        isMove = true;

                                        elapsedTime -= time;

                                        desiredVelocity.value += distance;
                                    }

                                    source = destination;
                                }
                                else
                                {
                                    isMove = true;

                                    desiredVelocity.value += distance * (elapsedTime / time);

                                    if ((time - elapsedTime) * speed < stoppingDistance)
                                        ++count;
                                    else
                                        positions[i] = position;

                                    isOnPath = true;

                                    break;
                                }
                            }
                            /*else
                                ++count;*/
                        }

                        if (count > 0)
                            positions.RemoveRange(0, isOnPath ? count : length);

                        if (isMove)
                            desiredVelocity.value /= deltaTime;
                    }
                    else
                    {
                        isMove = false;

                        speed = 0.0f;

                        desiredVelocity.value = float3.zero;
                    }

#if GAME_DEBUG_COMPARSION
                    stream.Assert(positionCountName, positions.Length);
#endif
                }

                //var speedSelection = GameNodeSpeedSection.Get(speed, maxSpeed, speedSections[index]);

                float value = velocities[index].value;

                var speedSelection = GameNodeSpeedSection.Get(value/*math.min(value, speed)*/, maxSpeed, speedSections[index]);

                //if (deltaTime < this.deltaTime)
                //value = math.lerp(value, 0.0f, math.min(instance.acceleration * (this.deltaTime - deltaTime), 1.0f));

                //value = math.lerp(value, math.select(0.0f, instance.speed * sign, isMove), math.min(instance.acceleration * deltaTime, 1.0f));

#if GAME_DEBUG_COMPARSION
                stream.Assert(oldVelocityName, value);
#endif

                GameNodeDesiredStatus.Status desiredStatus;
                float3 actualVelocity;
                if (isMove)
                {
                    UnityEngine.Assertions.Assert.AreEqual(0, forward.y);

                    forward *= sign;

                    float radians = -Math.SignedAngle(
                            forward.xz,
                            math.normalizesafe(desiredVelocity.value.xz)),
                            absRadians = math.abs(radians);

                    if (sign > 0.0f && speedSelection.pivotAngularSpeed > speedSelection.angularSpeed && absRadians > speedSelection.pivotAngularSpeed * this.deltaTime)
                    {
                        /*actualVelocity = math.lerp(forward * value, desiredVelocity.value, math.min(speedSelection.pivotSpeed * deltaTime, 1.0f));

                        float dot = math.dot(actualVelocity, desiredVelocity.value);
                        desiredStatus = math.dot(actualVelocity, desiredVelocity.value) > 0.0f ? GameNodeDesiredStatus.Status.Pivoting : GameNodeDesiredStatus.Status.StoppingToPivot;

                        if (desiredStatus == GameNodeDesiredStatus.Status.Pivoting)
                        {
                            float lengthsq = math.lengthsq(desiredVelocity.value);

                            value = dot * math.rsqrt(lengthsq);

                            GameNodeAngle angle;
                            angle.value = (half)Math.DeltaAngle(angleValue + radians);
                            angles[index] = angle;
                        }
                        else
                            value = math.dot(actualVelocity, forward);

                        desiredVelocity.value = actualVelocity;*/

                        //UnityEngine.Debug.Log($"Pivoting {radians} : {speedSelection.pivotAngularSpeed * deltaTime}");

                        desiredStatus = GameNodeDesiredStatus.Status.Pivoting;
                        float3 newForwad = math.normalize(desiredVelocity.value);
                        value = math.dot(forward * value, newForwad);// * speedSelection.pivotSpeedScale;

                        value = math.lerp(value, speed * sign, math.min(speedSelection.acceleration * deltaTime, 1.0f));

                        forward = newForwad;

                        desiredVelocity.value *= value / speed;

                        actualVelocity = desiredVelocity.value;

                        GameNodeAngle angle;
                        angle.value = (half)Math.DeltaAngle(angleValue + radians);
                        angles[index] = angle;
                    }
                    else
                    {
                        speed *= sign;

                        value = math.lerp(value, speed, math.min(speedSelection.acceleration * deltaTime, 1.0f));

                        desiredVelocity.value *= value / speed;

                        /*value = math.lerp(value, speed * sign, math.min(speedSelection.acceleration * deltaTime, 1.0f));

                        float absolute = math.abs(value), scale = absolute / speed;

                        desiredVelocity.value *= scale;*/

                        float magnitude = math.lengthsq(desiredVelocity.value.xz);
                        if (magnitude > staticThresholds[index].value)
                        {
                            //magnitude = math.rsqrt(magnitude);
                            float angularSpeed = speedSelection.angularSpeed/* * scale*/, maxRadiansDelta = math.abs(angularSpeed * deltaTime);

                            UnityEngine.Assertions.Assert.IsFalse(math.isnan(radians));
                            if (maxRadiansDelta < absRadians)
                            {
                                desiredStatus = GameNodeDesiredStatus.Status.Turning;

                                float side = math.sign(radians);

                                if (isOnPath && positions.Length > 0)
                                {
                                    const float HALF_PI = math.PI * 0.5f;

                                    position = positions[0];
                                    switch (position.mode)
                                    {
                                        case GameNodePosition.Mode.Circle:
                                            if (absRadians < HALF_PI)
                                            {
                                                position.mode = GameNodePosition.Mode.Limit;
                                                positions[0] = position;
                                            }
                                            break;
                                        case GameNodePosition.Mode.Limit:
                                            if (absRadians > HALF_PI)
                                                positions.RemoveAt(0);
                                            break;
                                        default:
                                            float radius = math.abs(value/*absolute*/ / angularSpeed);

                                            bool isInCircle = math.distancesq(
                                                source.xz + math.normalize(math.cross(
                                                up,
                                                forward).xz) * (radius * side),
                                                destination.xz) < radius * radius;

                                            if (isInCircle)
                                            {
                                                side = -side;

                                                desiredVelocity.value = -desiredVelocity.value;
                                            }
                                            else
                                            {
                                                position.mode = GameNodePosition.Mode.Circle;
                                                positions[0] = position;
                                            }
                                            break;
                                    }
                                }

                                radians = maxRadiansDelta * side;

                                actualVelocity = math.mul(quaternion.RotateY(radians), forward) * math.sqrt(magnitude); ;// / magnitude;
                                actualVelocity.y = desiredVelocity.value.y;
                            }
                            else
                            {
                                desiredStatus = GameNodeDesiredStatus.Status.Moving;

                                actualVelocity = desiredVelocity.value;
                            }

                            GameNodeAngle angle;
                            angle.value = (half)Math.DeltaAngle(angleValue + radians);// (half)math.atan2(sign * nextVelocity.x, sign * nextVelocity.z);
#if GAME_DEBUG_COMPARSION
                            stream.Assert(newAngleName, angle.value);
#endif
                            angles[index] = angle;
                        }
                        else
                        {
                            desiredStatus = GameNodeDesiredStatus.Status.Moving;

                            actualVelocity = desiredVelocity.value;
                        }
                    }
                }
                else
                {
                    value *= 1.0f - math.min(speedSelection.deceleration * deltaTime, 1.0f);
                    if (value * value > staticThresholds[index].value)
                    {
                        desiredStatus = GameNodeDesiredStatus.Status.Stopping;

                        desiredVelocity.value = forward * value;
                    }
                    else
                    {
                        desiredStatus = GameNodeDesiredStatus.Status.Normal;

                        value = 0.0f;

                        desiredVelocity.value = float3.zero;
                    }

                    actualVelocity = desiredVelocity.value;
                }

                GameNodeVelocity velocity;
                velocity.value = value;

#if GAME_DEBUG_COMPARSION
                stream.Assert(newVelocityName, velocity.value);
#endif
                velocities[index] = velocity;

                if(index < desiredStates.Length && desiredStates[index].value != desiredStatus)
                {
                    GameNodeDesiredStatus temp;
                    temp.value = desiredStatus;
                    temp.time = time;
                    desiredStates[index] = temp;
                }

                if (index < desiredVelocities.Length)
                {
                    desiredVelocity.time = deltaTime;
                    //TODO:
                    desiredVelocity.sign = sign;
                    desiredVelocity.value = math.mul(math.inverse(quaternion.LookRotation(forward, up)), desiredVelocity.value);

                    desiredVelocities[index] = desiredVelocity;
                }

                direct.value += actualVelocity * deltaTime;

                directs[index] = direct;
            }
            else
            {
                if (isStatusDelay)
                    velocities[index] = default;

                if (index < desiredStates.Length && desiredStates[index].value != GameNodeDesiredStatus.Status.Normal)
                {
                    GameNodeDesiredStatus temp;
                    temp.value = GameNodeDesiredStatus.Status.Normal;
                    temp.time = time;
                    desiredStates[index] = temp;
                }

                if (index < desiredVelocities.Length)
                    desiredVelocities[index] = default;

                if (isUpdate)
                    directs[index] = default;
            }
            
#if GAME_DEBUG_COMPARSION
            stream.End();
#endif
        }
    }

    [BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Deterministic)]
    private struct UpdateTransformsEx : IJobChunk
    {
        public bool isUpdate;
        public GameTime time;

        [ReadOnly]
        public ComponentLookup<GameNodeDirection> directions;

        [ReadOnly]
        public BufferTypeHandle<GameNodeSpeedSection> speedSectionType;
        [ReadOnly]
        public ComponentTypeHandle<Translation> translationType;
        [ReadOnly]
        public ComponentTypeHandle<GameNodeStaticThreshold> staticThresholdType;
        [ReadOnly]
        public ComponentTypeHandle<GameNodeStoppingDistance> stoppingDistanceType;
        [ReadOnly]
        public ComponentTypeHandle<GameNodeStatus> statusType;
        [ReadOnly]
        public ComponentTypeHandle<GameNodeDelay> delayType;
        [ReadOnly]
        public ComponentTypeHandle<GameNodeSurface> surfaceType;
        [ReadOnly]
        public ComponentTypeHandle<GameNodeSpeed> speedType;
        [ReadOnly]
        public ComponentTypeHandle<GameNodeSpeedScale> speedScaleType;
        [ReadOnly]
        public ComponentTypeHandle<GameNodeDirection> directionType;

        public ComponentTypeHandle<GameNodeDesiredStatus> desiredStatusType;

        public ComponentTypeHandle<GameNodeDesiredVelocity> desiredVelocityType;

        public ComponentTypeHandle<GameNodeAngle> angleType;

        public ComponentTypeHandle<GameNodeDirect> directType;

        public ComponentTypeHandle<GameNodeVelocity> velocityType;

        //[NativeDisableParallelForRestriction]
        public BufferTypeHandle<GameNodePosition> positionType;

        [NativeDisableContainerSafetyRestriction]
        public BufferLookup<GameNodePosition> positions;

#if GAME_DEBUG_COMPARSION
        //public NativeQueue<LogInfo>.ParallelWriter logInfos;

        public uint frameIndex;
        public FixedString32Bytes statusName;
        public FixedString32Bytes dataName;
        public FixedString32Bytes oldAngleName;
        public FixedString32Bytes oldVelocityName;
        public FixedString32Bytes deltaTimeName;
        public FixedString32Bytes speedScaleName;
        public FixedString32Bytes distanceName;
        public FixedString32Bytes translationName;
        public FixedString32Bytes rotationName;
        public FixedString32Bytes positionName;
        public FixedString32Bytes positionCountName;
        public FixedString32Bytes nextVelocityName;
        public FixedString32Bytes newVelocityName;
        public FixedString32Bytes newAngleName;

        [ReadOnly]
        public EntityTypeHandle entityArrayType;

        public ComparisonStream<uint> stream;
        [ReadOnly]
        public ComponentTypeHandle<GameEntityIndex> entityIndexType;
#endif
        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            UpdateTransforms updateTransforms;
            updateTransforms.isUpdate = isUpdate;
            updateTransforms.deltaTime = time.delta;
            updateTransforms.time = time;
            updateTransforms.speedSections = chunk.GetBufferAccessor(ref speedSectionType);
            updateTransforms.translations = chunk.GetNativeArray(ref translationType);
            updateTransforms.staticThresholds = chunk.GetNativeArray(ref staticThresholdType);
            updateTransforms.stoppingDistances = chunk.GetNativeArray(ref stoppingDistanceType);
            updateTransforms.states = chunk.GetNativeArray(ref statusType);
            updateTransforms.delay = chunk.GetNativeArray(ref delayType);
            updateTransforms.surfaces = chunk.GetNativeArray(ref surfaceType);
            updateTransforms.speeds = chunk.GetNativeArray(ref speedType);
            updateTransforms.speedScales = chunk.GetNativeArray(ref speedScaleType);
            updateTransforms.desiredStates = chunk.GetNativeArray(ref desiredStatusType);
            updateTransforms.desiredVelocities = chunk.GetNativeArray(ref desiredVelocityType);
            updateTransforms.directions = chunk.GetNativeArray(ref directionType);
            updateTransforms.angles = chunk.GetNativeArray(ref angleType);
            updateTransforms.directs = chunk.GetNativeArray(ref directType);
            updateTransforms.velocities = chunk.GetNativeArray(ref velocityType);
            updateTransforms.positions = chunk.GetBufferAccessor(ref positionType);

#if GAME_DEBUG_COMPARSION
            updateTransforms.frameIndex = frameIndex;
            updateTransforms.statusName = statusName;
            updateTransforms.dataName = dataName;
            updateTransforms.oldAngleName = oldAngleName;
            updateTransforms.oldVelocityName = oldVelocityName;
            updateTransforms.deltaTimeName = deltaTimeName;
            updateTransforms.speedScaleName = speedScaleName;
            updateTransforms.distanceName = distanceName;
            updateTransforms.translationName = translationName;
            updateTransforms.rotationName = rotationName;
            updateTransforms.positionName = positionName;
            updateTransforms.positionCountName = positionCountName;
            updateTransforms.nextVelocityName = nextVelocityName;
            updateTransforms.newVelocityName = newVelocityName;
            updateTransforms.newAngleName = newAngleName;

            updateTransforms.entityArray = chunk.GetNativeArray(entityArrayType);

            updateTransforms.stream = stream;
            updateTransforms.entityIndices = chunk.GetNativeArray(ref entityIndexType);
#endif

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                updateTransforms.Execute(i);
        }
    }

    private EntityQuery __group;

    /*private EntityQuery __syncDataGroup;
    private EntityQuery __updateDataGroup;*/
    private GameUpdateTime __time;

    private ComponentLookup<GameNodeDirection> __directions;

    private BufferTypeHandle<GameNodeSpeedSection> __speedSectionType;
    private ComponentTypeHandle<Translation> __translationType;
    private ComponentTypeHandle<GameNodeStaticThreshold> __staticThresholdType;
    private ComponentTypeHandle<GameNodeStoppingDistance> __stoppingDistanceType;
    private ComponentTypeHandle<GameNodeStatus> __statusType;
    private ComponentTypeHandle<GameNodeDelay> __delayType;
    private ComponentTypeHandle<GameNodeSurface> __surfaceType;
    private ComponentTypeHandle<GameNodeSpeed> __speedType;
    private ComponentTypeHandle<GameNodeSpeedScale> __speedScaleType;
    private ComponentTypeHandle<GameNodeDirection> __directionType;
    private ComponentTypeHandle<GameNodeDesiredStatus> __desiredStatusType;
    private ComponentTypeHandle<GameNodeDesiredVelocity> __desiredVelocityType;
    private ComponentTypeHandle<GameNodeAngle> __angleType;
    private ComponentTypeHandle<GameNodeDirect> __directType;
    private ComponentTypeHandle<GameNodeVelocity> __velocityType;

    private BufferTypeHandle<GameNodePosition> __positionType;
    private BufferLookup<GameNodePosition> __positions;

    //private NativeQueue<LogInfo> __logInfos;
    public void OnCreate(ref SystemState state)
    {
        __group = state.GetEntityQuery(
            ComponentType.ReadOnly<GameNodeSpeedSection>(),
            ComponentType.ReadOnly<Translation>(),
            ComponentType.ReadOnly<GameNodeStaticThreshold>(),
            ComponentType.ReadOnly<GameNodeStoppingDistance>(),
            ComponentType.ReadOnly<GameNodeStatus>(),
            ComponentType.ReadOnly<GameNodeSpeed>(),
            ComponentType.ReadWrite<GameNodeVelocity>(),
            ComponentType.ReadWrite<GameNodeAngle>(),
            ComponentType.ReadWrite<GameNodeDirect>(),
            ComponentType.Exclude<GameNodeParent>(),
            ComponentType.Exclude<Disabled>());

        /*__syncDataGroup = state.GetEntityQuery(ComponentType.ReadOnly<GameSyncData>());
        __updateDataGroup = state.GetEntityQuery(ComponentType.ReadOnly<GameUpdateData>());*/

        __time = new GameUpdateTime(ref state);

        __directions = state.GetComponentLookup<GameNodeDirection>(true);
        __speedSectionType = state.GetBufferTypeHandle<GameNodeSpeedSection>(true);
        __translationType = state.GetComponentTypeHandle<Translation>(true);
        __staticThresholdType = state.GetComponentTypeHandle<GameNodeStaticThreshold>(true);
        __stoppingDistanceType = state.GetComponentTypeHandle<GameNodeStoppingDistance>(true);
        __statusType = state.GetComponentTypeHandle<GameNodeStatus>(true);
        __delayType = state.GetComponentTypeHandle<GameNodeDelay>(true);
        __surfaceType = state.GetComponentTypeHandle<GameNodeSurface>(true);
        __speedType = state.GetComponentTypeHandle<GameNodeSpeed>(true);
        __speedScaleType = state.GetComponentTypeHandle<GameNodeSpeedScale>(true);
        __directionType = state.GetComponentTypeHandle<GameNodeDirection>(true);
        __desiredStatusType = state.GetComponentTypeHandle<GameNodeDesiredStatus>();
        __desiredVelocityType = state.GetComponentTypeHandle<GameNodeDesiredVelocity>();
        __angleType = state.GetComponentTypeHandle<GameNodeAngle>();
        __directType = state.GetComponentTypeHandle<GameNodeDirect>();
        __velocityType = state.GetComponentTypeHandle<GameNodeVelocity>();
        __positionType = state.GetBufferTypeHandle<GameNodePosition>();
        __positions = state.GetBufferLookup<GameNodePosition>();
    }

    public void OnDestroy(ref SystemState state)
    {
        //__logInfos.Dispose();
    }

#if !GAME_DEBUG_COMPARSION
    [BurstCompile]
#endif
    public void OnUpdate(ref SystemState state)
    {
        /*var syncData = __syncDataGroup.GetSingleton<GameSyncData>();
        var updateData = __updateDataGroup.GetSingleton<GameUpdateData>();*/

        var jobHandle = state.Dependency;

        UpdateTransformsEx updateTransforms;
        updateTransforms.isUpdate = __time.IsVail(-1);// updateData.IsUpdate(syncData.frameIndex, -1);
        updateTransforms.time = __time.RollbackTime.now;// syncData.now;
        updateTransforms.directions = __directions.UpdateAsRef(ref state);
        updateTransforms.speedSectionType = __speedSectionType.UpdateAsRef(ref state);
        updateTransforms.translationType = __translationType.UpdateAsRef(ref state);
        updateTransforms.staticThresholdType = __staticThresholdType.UpdateAsRef(ref state);
        updateTransforms.stoppingDistanceType = __stoppingDistanceType.UpdateAsRef(ref state);
        updateTransforms.statusType = __statusType.UpdateAsRef(ref state);
        updateTransforms.delayType = __delayType.UpdateAsRef(ref state);
        updateTransforms.surfaceType = __surfaceType.UpdateAsRef(ref state);
        updateTransforms.speedType = __speedType.UpdateAsRef(ref state);
        updateTransforms.speedScaleType = __speedScaleType.UpdateAsRef(ref state);
        updateTransforms.directionType = __directionType.UpdateAsRef(ref state);
        updateTransforms.desiredStatusType = __desiredStatusType.UpdateAsRef(ref state);
        updateTransforms.desiredVelocityType = __desiredVelocityType.UpdateAsRef(ref state);
        updateTransforms.angleType = __angleType.UpdateAsRef(ref state);
        updateTransforms.directType = __directType.UpdateAsRef(ref state);
        updateTransforms.velocityType = __velocityType.UpdateAsRef(ref state);
        updateTransforms.positionType = __positionType.UpdateAsRef(ref state);
        updateTransforms.positions = __positions.UpdateAsRef(ref state);

#if GAME_DEBUG_COMPARSION
        uint frameIndex = __time.RollbackTime.frameIndex;

        updateTransforms.frameIndex = frameIndex;
        //updateTransform.logInfos = __logInfos.AsParallelWriter();
        updateTransforms.statusName = "status";
        updateTransforms.dataName = "data";
        updateTransforms.oldAngleName = "oldAngle";
        updateTransforms.oldVelocityName = "oldVelocity";
        updateTransforms.deltaTimeName = "deltaTime";
        updateTransforms.speedScaleName = "speedScale";
        updateTransforms.distanceName = "distance";
        updateTransforms.translationName = "translation";
        updateTransforms.rotationName = "rotation";
        updateTransforms.positionName = "position";
        updateTransforms.positionCountName = "positionCount";
        updateTransforms.nextVelocityName = "nextVelocity";
        updateTransforms.newVelocityName = "newVelocity";
        updateTransforms.newAngleName = "newAngle";

        updateTransforms.entityArrayType = state.GetEntityTypeHandle();

        var streamScheduler = GameComparsionSystem.instance.Create(SystemAPI.GetSingleton<FrameSyncFlag>().isClear, frameIndex, typeof(GameNodeSystem).Name, state.World.Name);
        updateTransforms.stream = streamScheduler.Begin(__group.CalculateEntityCount());
        updateTransforms.entityIndexType = state.GetComponentTypeHandle<GameEntityIndex>(true);
#endif

        state.Dependency = updateTransforms.ScheduleParallelByRef(__group, jobHandle);

#if GAME_DEBUG_COMPARSION
        streamScheduler.End(state.Dependency);

        /*Log log;
        log.logInfos = __logInfos;
        inputDeps = log.Schedule(Dependency);*/
#endif
    }
}

[BurstCompile, UpdateInGroup(typeof(GameNodeInitSystemGroup))/*, UpdateBefore(typeof(GameNodeSystem))*/]
public partial struct GameNodeSpeedScaleSystem : ISystem
{
    private struct UpdateSpeedScale
    {
#if GAME_DEBUG_COMPARSION
        public uint frameIndex;
#endif

        [ReadOnly]
        public NativeArray<Entity> entityArray;
        [ReadOnly]
        public NativeArray<GameNodeSpeedScale> inputs;
        [ReadOnly]
        public BufferAccessor<GameNodeSpeedScaleComponent> components;

        [NativeDisableContainerSafetyRestriction]
        public ComponentLookup<GameNodeSpeedScale> outputs;

        public void Execute(int index)
        {
            var value = inputs[index];
            var origin = value;
            if (value.Apply(components[index]))
            {
                outputs[entityArray[index]] = value;

                //UnityEngine.Debug.LogError($"Change Speed {origin.value.value} : {value.value.value} : {entityArray[index]} : {frameIndex}");
            }
        }
    }

    [BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Deterministic)]
    private struct UpdateSpeedScaleEx : IJobChunk
    {
#if GAME_DEBUG_COMPARSION
        public uint frameIndex;
#endif

        [ReadOnly]
        public EntityTypeHandle entityType;
        [ReadOnly]
        public ComponentTypeHandle<GameNodeSpeedScale> instanceType;
        [ReadOnly]
        public BufferTypeHandle<GameNodeSpeedScaleComponent> componentType;

        [NativeDisableContainerSafetyRestriction]
        public ComponentLookup<GameNodeSpeedScale> instances;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            UpdateSpeedScale updateSpeedScale;
#if GAME_DEBUG_COMPARSION
            updateSpeedScale.frameIndex = frameIndex;
#endif

            updateSpeedScale.entityArray = chunk.GetNativeArray(entityType);
            updateSpeedScale.inputs = chunk.GetNativeArray(ref instanceType);
            updateSpeedScale.components = chunk.GetBufferAccessor(ref componentType);
            updateSpeedScale.outputs = instances;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                updateSpeedScale.Execute(i);
        }
    }

    private EntityQuery __group;

#if GAME_DEBUG_COMPARSION
    private GameRollbackTime __time;
#endif

    public void OnCreate(ref SystemState state)
    {
        __group = state.GetEntityQuery(
            ComponentType.ReadWrite<GameNodeSpeedScale>(),
            ComponentType.ReadOnly<GameNodeSpeedScaleComponent>(),
            ComponentType.Exclude<Disabled>());

        __group.SetChangedVersionFilter(typeof(GameNodeSpeedScaleComponent));

#if GAME_DEBUG_COMPARSION
        __time = new GameRollbackTime(ref state);
#endif
    }

    public void OnDestroy(ref SystemState state)
    {

    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        UpdateSpeedScaleEx updateSpeedScale;
#if GAME_DEBUG_COMPARSION
        updateSpeedScale.frameIndex = __time.frameIndex;
#endif

        updateSpeedScale.entityType = state.GetEntityTypeHandle();
        updateSpeedScale.instanceType = state.GetComponentTypeHandle<GameNodeSpeedScale>(true);
        updateSpeedScale.componentType = state.GetBufferTypeHandle<GameNodeSpeedScaleComponent>(true);
        updateSpeedScale.instances = state.GetComponentLookup<GameNodeSpeedScale>();

        state.Dependency = updateSpeedScale.ScheduleParallel(__group, state.Dependency);
    }
}

[BurstCompile, UpdateInGroup(typeof(GameUpdateSystemGroup), OrderLast = true)]
public partial struct GameNodeChildrenSystem : ISystem
{
    private struct UpdateChildren
    {
        [NativeDisableContainerSafetyRestriction, ReadOnly]
        public ComponentLookup<Translation> translationMap;
        [NativeDisableContainerSafetyRestriction, ReadOnly]
        public ComponentLookup<Rotation> rotationMap;

        [ReadOnly]
        public NativeArray<GameNodeParent> parents;
        public NativeArray<Translation> translations;
        public NativeArray<Rotation> rotations;

        public void Execute(int index)
        {
            GameNodeParent parent = parents[index];
            if (!translationMap.HasComponent(parent.entity))
                return;

            RigidTransform transform = math.mul(
                math.RigidTransform(
                    rotationMap[parent.entity].Value,
                    translationMap[parent.entity].Value),
                parent.transform);

            Translation translation;
            translation.Value = transform.pos;
            translations[index] = translation;

            Rotation rotation;
            rotation.Value = transform.rot;
            rotations[index] = rotation;
        }
    }

    [BurstCompile]
    private struct UpdateChildrenEx : IJobChunk
    {
        [NativeDisableContainerSafetyRestriction, ReadOnly]
        public ComponentLookup<Translation> translations;
        [NativeDisableContainerSafetyRestriction, ReadOnly]
        public ComponentLookup<Rotation> rotations;

        [ReadOnly]
        public ComponentTypeHandle<GameNodeParent> parentType;
        public ComponentTypeHandle<Translation> translationType;
        public ComponentTypeHandle<Rotation> rotationType;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            UpdateChildren updateChildren;
            updateChildren.translationMap = translations;
            updateChildren.rotationMap = rotations;
            updateChildren.parents = chunk.GetNativeArray(ref parentType);
            updateChildren.translations = chunk.GetNativeArray(ref translationType);
            updateChildren.rotations = chunk.GetNativeArray(ref rotationType);

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                updateChildren.Execute(i);
        }
    }

    private EntityQuery __group;

    public void OnCreate(ref SystemState state)
    {
        __group = state.GetEntityQuery(ComponentType.ReadOnly<GameNodeParent>(), ComponentType.Exclude<Disabled>());
    }

    public void OnDestroy(ref SystemState state)
    {

    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        UpdateChildrenEx updateChildren;
        updateChildren.translations = state.GetComponentLookup<Translation>(true);
        updateChildren.rotations = state.GetComponentLookup<Rotation>(true);
        updateChildren.parentType = state.GetComponentTypeHandle<GameNodeParent>(true);
        updateChildren.translationType = state.GetComponentTypeHandle<Translation>();
        updateChildren.rotationType = state.GetComponentTypeHandle<Rotation>();
        state.Dependency = updateChildren.ScheduleParallel(__group, state.Dependency);
    }
}

[BurstCompile, UpdateInGroup(typeof(GameUpdateSystemGroup), OrderFirst = true)]
public partial struct GameNodeVelocityComponentSystem : ISystem
{
    private struct Calculate
    {
        public GameTime time;

        public NativeArray<GameNodeDirect> directs;

        public NativeArray<GameNodeIndirect> indirects;

        public NativeArray<GameNodeDrag> drags;

        public BufferAccessor<GameNodeVelocityComponent> velocityComponents;

#if GAME_DEBUG_COMPARSION
        [ReadOnly]
        public NativeArray<GameEntityIndex> entityIndices;
#endif
        public void Execute(int index)
        {
            var direct = directs[index];

            GameNodeIndirect indirect = default;

            GameNodeDrag drag = default;

            var velocityComponents = this.velocityComponents[index];
            int length = velocityComponents.Length;
            if (length > 0)
            {
                float elapsedTime;
                double now = this.time, time;
                GameNodeVelocityComponent velocityComponent;
                //UnityEngine.Debug.Log($"Indirect {entityIndices[index].value} : {length} : {now}");
                for (int i = 0; i < length; ++i)
                {
                    velocityComponent = velocityComponents[i];
                    time = velocityComponent.time;
                    if (velocityComponent.duration > math.FLT_MIN_NORMAL)
                    {
                        if (time + velocityComponent.duration > now)
                        {
                            if (now > time)
                            {
                                elapsedTime = (float)(now - time);

                                velocityComponent.duration -= elapsedTime;
                                velocityComponent.time = this.time;

                                velocityComponents[i] = velocityComponent;
                            }
                            else
                                elapsedTime = 0.0f;
                        }
                        else
                        {
                            if (velocityComponent.mode == GameNodeVelocityComponent.Mode.Direct)
                                drag.value += velocityComponent.value * velocityComponent.duration;

                            elapsedTime = velocityComponent.duration;

                            velocityComponents.RemoveAt(i);

                            --i;

                            --length;
                        }
                    }
                    else if (time > now)
                        elapsedTime = 0.0f;
                    else
                    {
                        velocityComponents.RemoveAt(i);

                        --i;

                        --length;

                        switch(velocityComponent.mode)
                        {
                            case GameNodeVelocityComponent.Mode.Direct:
                                drag.velocity += velocityComponent.value;
                                break;
                            case GameNodeVelocityComponent.Mode.Indirect:
                                indirect.velocity += velocityComponent.value;
                                break;
                        }

                        continue;
                        //y = 0.0f;
                    }

                    if (elapsedTime > math.FLT_MIN_NORMAL)
                    {
                        switch(velocityComponent.mode)
                        {
                            case GameNodeVelocityComponent.Mode.Direct:
                                direct.value += velocityComponent.value * elapsedTime;
                                break;
                            case GameNodeVelocityComponent.Mode.Indirect:
                                indirect.value += velocityComponent.value * elapsedTime;
                                break;
                        }
                    }
                }
            }

            if(index < directs.Length)
                directs[index] = direct;

            if(index < indirects.Length)
                indirects[index] = indirect;

            if(index < drags.Length)
                drags[index] = drag;
        }
    }

    [BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Deterministic)]
    private struct CalculateEx : IJobChunk
    {
        public GameTime time;

        public ComponentTypeHandle<GameNodeDirect> directType;

        public ComponentTypeHandle<GameNodeIndirect> indirectType;

        public ComponentTypeHandle<GameNodeDrag> dragType;

        public BufferTypeHandle<GameNodeVelocityComponent> velocityComponentsType;

#if GAME_DEBUG_COMPARSION
        [ReadOnly]
        public ComponentTypeHandle<GameEntityIndex> entityIndexType;
#endif

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Calculate calculate;
            calculate.time = time;
            calculate.directs = chunk.GetNativeArray(ref directType);
            calculate.indirects = chunk.GetNativeArray(ref indirectType);
            calculate.drags = chunk.GetNativeArray(ref dragType);
            calculate.velocityComponents = chunk.GetBufferAccessor(ref velocityComponentsType);

#if GAME_DEBUG_COMPARSION
            calculate.entityIndices = chunk.GetNativeArray(ref entityIndexType);
#endif

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                calculate.Execute(i);
        }
    }

    private EntityQuery __group;
    //private EntityQuery __syncDataGroup;
    private GameRollbackTime __time;

    public void OnCreate(ref SystemState state)
    {
        __group = state.GetEntityQuery(new EntityQueryDesc()
        {
            All = new ComponentType[]
            {
                ComponentType.ReadOnly<GameNodeVelocityComponent>()
            }, 
            Any = new ComponentType[]
            {
                ComponentType.ReadOnly<GameNodeDirect>(),
                ComponentType.ReadOnly<GameNodeIndirect>(), 
                ComponentType.ReadOnly<GameNodeDrag>()
            }
        });
        //__syncDataGroup = state.GetEntityQuery(ComponentType.ReadOnly<GameSyncData>());
        __time = new GameRollbackTime(ref state);
    }

    public void OnDestroy(ref SystemState state)
    {

    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        CalculateEx calculate;
        calculate.time = __time.now;// __syncDataGroup.GetSingleton<GameSyncData>().now;
        calculate.directType = state.GetComponentTypeHandle<GameNodeDirect>();
        calculate.indirectType = state.GetComponentTypeHandle<GameNodeIndirect>();
        calculate.dragType = state.GetComponentTypeHandle<GameNodeDrag>();
        calculate.velocityComponentsType = state.GetBufferTypeHandle<GameNodeVelocityComponent>();

#if GAME_DEBUG_COMPARSION
        calculate.entityIndexType = state.GetComponentTypeHandle<GameEntityIndex>(true);
#endif

        state.Dependency = calculate.ScheduleParallel(__group, state.Dependency);
    }
}

[BurstCompile, UpdateInGroup(typeof(GameRollbackSystemGroup)), UpdateBefore(typeof(GameStatusSystemGroup))]
public partial struct GameNodeAngleSystem : ISystem
{
    private struct Look
    {
        [ReadOnly]
        public ComponentLookup<Translation> translationMap;
        [ReadOnly]
        public NativeArray<Translation> translations;
        [ReadOnly]
        public NativeArray<GameNodeLookTarget> targets;
        public NativeArray<GameNodeAngle> angles;

        public void Execute(int index)
        {
            var target = targets[index];
            var distance = translationMap[target.entity].Value - translations[index].Value;
            GameNodeAngle angle;
            angle.value = (half)math.atan2(distance.x, distance.z);
            angles[index] = angle;
        }
    }

    [BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Deterministic)]
    private struct LookEx : IJobChunk
    {
        [ReadOnly]
        public ComponentLookup<Translation> translations;
        [ReadOnly]
        public ComponentTypeHandle<Translation> translationType;
        [ReadOnly]
        public ComponentTypeHandle<GameNodeLookTarget> targetType;
        public ComponentTypeHandle<GameNodeAngle> angleType;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Look look;
            look.translationMap = translations;
            look.translations = chunk.GetNativeArray(ref translationType);
            look.targets = chunk.GetNativeArray(ref targetType);
            look.angles = chunk.GetNativeArray(ref angleType);

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                look.Execute(i);
        }
    }

    private EntityQuery __group;

    public void OnCreate(ref SystemState state)
    {
        __group = state.GetEntityQuery(
            new EntityQueryDesc()
            {
                All = new ComponentType[]
                {
                    ComponentType.ReadOnly<Translation>(),
                    ComponentType.ReadOnly<GameNodeLookTarget>(), 
                    ComponentType.ReadWrite<GameNodeAngle>()
                }, 
                Options = EntityQueryOptions.IncludeDisabledEntities
            });
    }

    public void OnDestroy(ref SystemState state)
    {
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        LookEx look;
        look.translations = state.GetComponentLookup<Translation>(true);
        look.translationType = state.GetComponentTypeHandle<Translation>(true);
        look.targetType = state.GetComponentTypeHandle<GameNodeLookTarget>(true);
        look.angleType = state.GetComponentTypeHandle<GameNodeAngle>();
        state.Dependency = look.ScheduleParallel(__group, state.Dependency);
    }
}

[BurstCompile, UpdateInGroup(typeof(GameRollbackSystemGroup), OrderLast = true)]
public partial struct GameNodeClearSystem : ISystem
{
    private EntityQuery __group;

    public void OnCreate(ref SystemState state)
    {
        __group = state.GetEntityQuery(
            new EntityQueryDesc()
            {
                All = new ComponentType[]
                {
                    ComponentType.ReadOnly<GameNodeLookTarget>()
                },
                Options = EntityQueryOptions.IncludeDisabledEntities
            });
    }

    public void OnDestroy(ref SystemState state)
    {
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        state.EntityManager.RemoveComponent<GameNodeLookTarget>(__group);
    }
}

[BurstCompile, UpdateInGroup(typeof(GameStatusSystemGroup), OrderLast = true)]
public partial struct GameNodeStatusSystem : ISystem
{
    private struct UpdateStates
    {
        public GameDeadline time;

        [ReadOnly]
        public NativeArray<GameNodeStatus> states;
        public NativeArray<GameNodeOldStatus> oldStates;

        public NativeArray<GameNodeDelay> delay;

        public NativeArray<GameNodeVelocity> velocities;

        public NativeArray<GameNodeDirect> directs;

        public NativeArray<GameNodeDirection> directions;

        public BufferAccessor<GameNodePosition> positions;

        public BufferAccessor<GameNodeVelocityComponent> velocityComponents;


#if GAME_DEBUG_COMPARSION
        public uint frameIndex;

        public FixedString32Bytes oldStatusName;
        public FixedString32Bytes newStatusName;

        public ComparisonStream<uint> stream;
        [ReadOnly]
        public NativeArray<GameEntityIndex> entityIndices;
#endif

        public void Execute(int index)
        {
            int value = states[index].value, oldValue = oldStates[index].value;
            if (value == oldValue)
                return;

#if GAME_DEBUG_COMPARSION
            //UnityEngine.Debug.Log($"Status: {oldValue} To {value} : {entityIndices[index].value} : {frameIndex}");

            stream.Begin(entityIndices[index].value);
            stream.Assert(oldStatusName, oldValue);
            stream.Assert(newStatusName, value);
            stream.End();
#endif
            GameNodeOldStatus oldStatus;
            oldStatus.value = value;
            oldStates[index] = oldStatus;

            int diff = value ^ oldValue;

            if ((diff & value & GameNodeStatus.STOP) == GameNodeStatus.STOP)
            {
                if (index < velocities.Length)
                    velocities[index] = default;

                if (index < delay.Length)
                    delay[index] = default;

                if (index < directions.Length)
                {
                    var direction = directions[index];
                    direction.version = 0;
                    directions[index] = direction;
                }

                if (index < velocityComponents.Length)
                    velocityComponents[index].Clear();
            }

            if ((diff & value & GameNodeStatus.OVER) == GameNodeStatus.OVER)
            {
                if (index < directions.Length)
                {
                    var direction = directions[index];
                    direction.version = 0;
                    directions[index] = direction;
                }

                if (index < velocityComponents.Length)
                {
                    //UnityEngine.Debug.Log("Clear Indirects");
                    velocityComponents[index].Clear();
                }

                if (index < positions.Length)
                    positions[index].Clear();

                if (index < directs.Length)
                    directs[index] = default;
            }
        }
    }

    [BurstCompile]
    private struct UpdateStatesEx : IJobChunk
    {
        [ReadOnly]
        public ComponentTypeHandle<GameNodeStatus> statusType;
        public ComponentTypeHandle<GameNodeOldStatus> oldStatusType;

        public ComponentTypeHandle<GameNodeDelay> delayType;

        public ComponentTypeHandle<GameNodeVelocity> velocityType;
        public ComponentTypeHandle<GameNodeDirect> directType;

        public ComponentTypeHandle<GameNodeDirection> directionType;

        public BufferTypeHandle<GameNodePosition> positionType;
        public BufferTypeHandle<GameNodeVelocityComponent> velocityComponentType;

#if GAME_DEBUG_COMPARSION
        public uint frameIndex;

        public FixedString32Bytes oldStatusName;
        public FixedString32Bytes newStatusName;

        public ComparisonStream<uint> stream;
        [ReadOnly]
        public ComponentTypeHandle<GameEntityIndex> entityIndexType;
#endif
        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            UpdateStates updateStates;
            updateStates.states = chunk.GetNativeArray(ref statusType);
            updateStates.oldStates = chunk.GetNativeArray(ref oldStatusType);
            updateStates.delay = chunk.GetNativeArray(ref delayType);
            updateStates.velocities = chunk.GetNativeArray(ref velocityType);
            updateStates.directs = chunk.GetNativeArray(ref directType);
            updateStates.directions = chunk.GetNativeArray(ref directionType);
            updateStates.positions = chunk.GetBufferAccessor(ref positionType);
            updateStates.velocityComponents = chunk.GetBufferAccessor(ref velocityComponentType);

#if GAME_DEBUG_COMPARSION
            updateStates.frameIndex = frameIndex;
            updateStates.oldStatusName = oldStatusName;
            updateStates.newStatusName = newStatusName;
            updateStates.stream = stream;
            updateStates.entityIndices = chunk.GetNativeArray(ref entityIndexType);
#endif
            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                updateStates.Execute(i);
        }
    }

    private EntityQuery __group;

    public void OnCreate(ref SystemState state)
    {
        __group = state.GetEntityQuery(
            new EntityQueryDesc()
            {
                All = new ComponentType[]
                {
                    ComponentType.ReadOnly<GameNodeStatus>(),
                    ComponentType.ReadWrite<GameNodeOldStatus>()
                },

                Options = EntityQueryOptions.IncludeDisabledEntities
            });
        __group.SetChangedVersionFilter(
            new ComponentType[]
            {
                typeof(GameNodeStatus),
                typeof(GameNodeOldStatus)
            });
    }

    public void OnDestroy(ref SystemState state)
    {

    }

#if !GAME_DEBUG_COMPARSION
    [BurstCompile]
#endif
    public void OnUpdate(ref SystemState state)
    {
        UpdateStatesEx updateStates;
        updateStates.statusType = state.GetComponentTypeHandle<GameNodeStatus>(true);
        updateStates.oldStatusType = state.GetComponentTypeHandle<GameNodeOldStatus>();
        updateStates.delayType = state.GetComponentTypeHandle<GameNodeDelay>();
        updateStates.velocityType = state.GetComponentTypeHandle<GameNodeVelocity>();
        updateStates.directType = state.GetComponentTypeHandle<GameNodeDirect>();
        updateStates.directionType = state.GetComponentTypeHandle<GameNodeDirection>();
        updateStates.positionType = state.GetBufferTypeHandle<GameNodePosition>();
        updateStates.velocityComponentType = state.GetBufferTypeHandle<GameNodeVelocityComponent>();

#if GAME_DEBUG_COMPARSION
        uint frameIndex = SystemAPI.GetSingleton<GameSyncManager>().SyncTime.frameIndex;
        var streamScheduler = GameComparsionSystem.instance.Create(false, frameIndex, typeof(GameNodeStatusSystem).Name, state.World.Name);

        updateStates.frameIndex = frameIndex;
        updateStates.oldStatusName = "oldStatus";
        updateStates.newStatusName = "newStatus";
        updateStates.stream = streamScheduler.Begin(__group.CalculateEntityCount());
        updateStates.entityIndexType = state.GetComponentTypeHandle<GameEntityIndex>(true);
#endif
        state.Dependency = updateStates.ScheduleParallel(__group, state.Dependency);

#if GAME_DEBUG_COMPARSION
        streamScheduler.End(state.Dependency);
#endif
    }
}