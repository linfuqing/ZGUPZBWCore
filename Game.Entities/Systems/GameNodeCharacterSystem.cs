using System;
using Unity.Jobs;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Physics;
using Unity.Physics.Systems;
using ZG;
using Math = ZG.Mathematics.Math;
using SurfaceConstraintInfo = ZG.SurfaceConstraintInfo;
using static ZG.CharacterControllerUtilities;

[System.Serializable]
public struct GameNodeCharacterCommon : IComponentData
{
    public static readonly GameNodeCharacterCommon Default = new GameNodeCharacterCommon(0, (1u << 30) | (1u << 31), 1u << 4, 50.0f);

    public uint dynamicMask;

    public uint terrainMask;

    public uint waterMask;

    public float depthOfWater;

    public GameNodeCharacterCommon(uint dynamicMask, uint terrainMask, uint waterMask, float depthOfWater)
    {
        this.dynamicMask = dynamicMask;
        this.terrainMask = terrainMask;
        this.waterMask = waterMask;
        this.depthOfWater = depthOfWater;
    }
}

[BurstCompile, UpdateInGroup(typeof(GameUpdateSystemGroup))]
public partial struct GameNodeCharacterSystemGroup : ISystem
{
    private SystemGroup __systemGroup;

    public void OnCreate(ref SystemState state)
    {
        __systemGroup = state.World.GetOrCreateSystemGroup(typeof(GameNodeCharacterSystemGroup));
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

[BurstCompile, UpdateInGroup(typeof(GameNodeCharacterSystemGroup), OrderFirst = true)]
public partial struct GameNodeCharacterSystem : ISystem
{
    /*internal struct CheckStaticBodyChangesJob : IJobChunk
    {
        [ReadOnly]
        public NativeSlice<RigidBody> rigidbodies;
        [ReadOnly] public ArchetypeChunkComponentType<Translation> PositionType;
        [ReadOnly] public ArchetypeChunkComponentType<Rotation> RotationType;
        [ReadOnly] public ArchetypeChunkComponentType<PhysicsCollider> PhysicsColliderType;
        
        public uint m_LastSystemVersion;

        public void Execute(ArchetypeChunk chunk, int chunkIndex, int firstEntityIndex)
        {
            if (!chunk.DidChange(PositionType, m_LastSystemVersion) && !chunk.DidChange(RotationType, m_LastSystemVersion))
            {
                var positions = chunk.GetNativeArray(PositionType);
                var rotations = chunk.GetNativeArray(RotationType);
                for (int i = 0; i < chunk.Count; ++i)
                {
                    if (!rigidbodies[firstEntityIndex + i].WorldFromBody.Equals(math.RigidTransform(rotations[i].Value, positions[i].Value)))
                        throw new System.Exception("nima le b");
                }
            }

            if (!chunk.DidChange(PhysicsColliderType, m_LastSystemVersion))
            {
                var physicsColliders = chunk.GetNativeArray(PhysicsColliderType);
                for (int i = 0; i < chunk.Count; ++i)
                {
                    if (!rigidbodies[firstEntityIndex + i].Collider.Equals(physicsColliders[i].Value))
                        throw new System.Exception("nima le b");
                }
            }
        }
    }*/

    public struct ClosestHitCollectorMask<T> : ICollector<T> where T : struct, IQueryResult
    {
        private NativeSlice<RigidBody> __rigidbodies;
        private uint __maskFilter;
        private int __maskRigidbodyIndex;

        public bool EarlyOutOnFirstHit => false;

        public int NumHits { get; private set; }

        public float MaxFraction { get; private set; }

        public float fraction { get; private set; }

        public T closestHit { get; private set; }

        public ClosestHitCollectorMask(int maskRigidbodyIndex, uint maskFilter, float maxFraction, NativeSlice<RigidBody> rigidbodies)
        {
            __rigidbodies = rigidbodies;
            __maskFilter = maskFilter;
            __maskRigidbodyIndex = maskRigidbodyIndex;

            NumHits = 0;
            MaxFraction = maxFraction;

            fraction = maxFraction;

            closestHit = default;
        }

        public void Reset(float maxFraction)
        {
            NumHits = 0;
            MaxFraction = maxFraction;

            fraction = maxFraction;

            closestHit = default;
        }

        #region ICollector

        public unsafe bool AddHit(T hit)
        {
            if (hit.RigidBodyIndex == __maskRigidbodyIndex)
                return false;

            fraction = math.min(fraction, hit.Fraction);

            ref var collider = ref __rigidbodies[hit.RigidBodyIndex].Collider.Value;
            uint belongsTo = collider.GetLeaf(hit.ColliderKey, out var leaf) ? leaf.Collider->Filter.BelongsTo : collider.Filter.BelongsTo;
            if ((belongsTo & __maskFilter) != 0)
                return false;

            MaxFraction = hit.Fraction;
            closestHit = hit;

            NumHits = 1;

            return true;
        }

        #endregion
    }

    /*[BurstCompile]
    private struct ClearDistanceHits : IJobForEach_B<GameNodeCharacterDistanceHit>
    {
        public void Execute(DynamicBuffer<GameNodeCharacterDistanceHit> distanceHits)
        {
            distanceHits.Clear();
        }
    }*/

    [BurstCompile]
    private struct ClearDistanceHits : IJobChunk
    {
        public BufferTypeHandle<GameNodeCharacterDistanceHit> distanceHitType;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            var distanceHist = chunk.GetBufferAccessor(ref distanceHitType);

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                distanceHist[i].Clear();
        }
    }

    private struct ApplyTransforms
    {
        public uint dynamicMask;

        public uint terrainMask;

        public uint waterMask;
        public float depthOfWater;
        
        public float deltaTime;
        public float3 gravity;
        
        [ReadOnly]
        public PhysicsWorldContainer physicsWorld;

        [ReadOnly]
        public ComponentLookup<GameWayData> ways;

        [ReadOnly]
        public BufferLookup<GameWayPoint> wayPoints;

        [ReadOnly]
        public NativeArray<Entity> entityArray;
        
        [ReadOnly]
        public NativeArray<GameNodeDirect> directs;

        [ReadOnly]
        public NativeArray<GameNodeIndirect> indirects;

        [ReadOnly]
        public NativeArray<GameNodeDirection> directions;

        [ReadOnly]
        public NativeArray<GameNodeDrag> drags;

        [ReadOnly]
        public NativeArray<GameNodeCharacterData> instances;

        [ReadOnly]
        public NativeArray<GameNodeCharacterCollider> colliders;

        [ReadOnly]
        public NativeArray<GameNodeCharacterCenterOfMass> centerOfMasses;

        public NativeArray<GameNodeCharacterFlag> flags;

        public NativeArray<GameNodeCharacterStatus> characterStates;

        public NativeArray<GameNodeCharacterVelocity> characterVelocities;

        public NativeArray<GameNodeCharacterDesiredVelocity> characterDesiredVelocities;

        public NativeArray<GameNodeCharacterAngle> characterAngles;

        public NativeArray<GameNodeCharacterSurface> characterSurfaces;

        public NativeArray<GameNodeSurface> surfaces;

        public NativeArray<GameNodeAngle> angles;
        
        public NativeArray<Translation> translations;

        public NativeArray<Rotation> rotations;
        
        public NativeArray<PhysicsVelocity> physicsVelocities;
        
        public BufferAccessor<GameNodeCharacterDistanceHit> distanceHits;

        public static unsafe uint CalculateLayerMask(
            in DynamicBuffer<DistanceHit> distanceHits, 
            in NativeSlice<RigidBody> rigidbodies, 
            out bool isUnstopping)
        {
            isUnstopping = false;

            int numDistanceHits = distanceHits.Length;
            uint layerMask = 0;
            DistanceHit distanceHit;
            RigidBody tempRigidbody;
            ChildCollider leaf;
            for (int i = 0; i < numDistanceHits; ++i)
            {
                distanceHit = distanceHits[i];
                tempRigidbody = rigidbodies[distanceHit.RigidBodyIndex];
                ref var collider = ref tempRigidbody.Collider.Value;
                if (collider.GetLeaf(distanceHit.ColliderKey, out leaf))
                    collider = ref *leaf.Collider;

                //if(distanceHit.Distance < 0.0f)
                    //isUnstopping = true;

                layerMask |= collider.Filter.BelongsTo;
            }

            return layerMask;
        }

        public static float3 RotatePosition(in float3 centerOfMass, in quaternion target, in RigidTransform origin)
        {
            return math.transform(origin, centerOfMass) - math.mul(target, centerOfMass);
        }
        
        public static float CalculateFraction(float fraction, float height, float deep, out float distance)
        {
            distance = fraction * (height + deep) - height;
            float result = math.max(distance, 0.0f);
            result /= deep;

            return result;
        }

        /*public static quaternion CalculateRotation(
            float3 right,
            float3 up,
            float3 surfaceNormal)
        {
            float3 tangent = math.cross(-right, surfaceNormal);
            tangent = math.normalize(tangent);

            float3 binorm = math.cross(tangent, surfaceNormal);
            binorm = math.normalize(binorm);

            return new quaternion(new float3x3(binorm, tangent, surfaceNormal));
        }*/

        public static quaternion CalculateSurfaceRotation(
            in float3 forward, 
            in float3 up, 
            in float3 surfaceNormal, 
            out float3 right, 
            out float3 tangent, 
            out float3 binorm)
        {
            UnityEngine.Assertions.Assert.IsFalse(surfaceNormal.Equals(float3.zero));
            /*float3 originUp = math.up();
            return math.dot(originUp, surfaceNormal) < 0.0f ?
                math.mul(Math.FromToRotation(-originUp, surfaceNormal), inverseRotation) :
                Math.FromToRotation(originUp, surfaceNormal);*/

            right = math.cross(up, forward);
            //UnityEngine.Assertions.Assert.IsTrue(Math.IsNormalized(right));
            
            tangent = math.cross(right, surfaceNormal);
            tangent = math.normalize(tangent);
            
            binorm = math.cross(surfaceNormal, tangent);
            //UnityEngine.Assertions.Assert.IsTrue(Math.IsNormalized(binorm));
            //binorm = math.normalize(binorm);
            
            return new quaternion(new float3x3(binorm, surfaceNormal, tangent));
        }

        public static float3 CalculateMovement(
            in float3 right, 
            in float3 forward,
            //in float3 up,
            in float3 binorm,
            in float3 tangent,
            //in float3 inveseGravityDirection,
            in float3 currentVelocity,
            in float3 desiredVelocity,
            in float3 surfaceNormal,
            in float3 surfaceVelocity)
        {
            quaternion surfaceRotation = new quaternion(new float3x3(binorm, -tangent, surfaceNormal));
            /*float3 right = math.cross(up, forward);
            quaternion surfaceRotation;// = CalculateRotation(right, up, surfaceNormal);
            {
                float3 tangent = math.cross(-right, surfaceNormal);
                tangent = math.normalize(tangent);

                float3 binorm = math.cross(tangent, surfaceNormal);
                binorm = math.normalize(binorm);

                surfaceRotation = new quaternion(new float3x3(binorm, tangent, surfaceNormal));
            }*/

            float3 relative = currentVelocity - surfaceVelocity;
            relative = math.rotate(math.inverse(surfaceRotation), relative);

            float3 diff;
            {
                float len = math.length(desiredVelocity);
                //float fwd = math.dot(desiredVelocity, forward);
                float side = math.dot(desiredVelocity, right);
                //float sign = math.dot(desiredVelocity, forward) * math.dot(up, math.up()) > 0.0f ? -1.0f : 1.0f;
                float3 desiredVelocitySF = new float3(side, -math.dot(desiredVelocity, forward)/*sign * (len - math.abs(side))*/, 0.0f);
                desiredVelocitySF = math.normalizesafe(desiredVelocitySF, float3.zero);
                desiredVelocitySF *= len;

                diff = desiredVelocitySF - relative;
            }

            relative += diff;

            return math.rotate(surfaceRotation, relative) + surfaceVelocity;/* +
                math.dot(desiredVelocity, inveseGravityDirection) * inveseGravityDirection;*/
        }

        public static bool IsUnsupported(float length, in float3 point, in float3 position, in float3 up)
        {
            float pointLength = math.dot(point, up), positionLength = math.dot(position, up);
            return pointLength + length < positionLength || pointLength - length > positionLength;
        }

        /*private static RigidTransform source;
        private static RigidTransform destination;
        private static float3 desiredVelocity;
        private static float3 velocity;
        private static float3 normal;*/

        public static float CalculateBuoyancy(float waterDistance, float waterMaxHeight, float deltaTime, float value)
        {
            float factor = waterDistance < waterMaxHeight ? waterDistance / waterMaxHeight : 1.0f;
            waterDistance = math.max(waterDistance - value * factor * deltaTime, 0.0f);
            factor += waterDistance < waterMaxHeight ? waterDistance / waterMaxHeight : 1.0f;
            factor *= 0.5f;
            value *= factor;
            return value;
        }

        public static unsafe float CalculateWaterDistance(
            in CollisionWorld collisionWorld,
            in CollisionFilter collisionFilter,
            in float3 position, 
            in float3 up,
            float stepHeight,
            float depthOfWater, 
            uint waterMask, 
            out uint layerMask, 
            out float3 normal)
        {
            layerMask = 0;
            if (stepHeight > math.FLT_MIN_NORMAL)
            {
                RaycastInput raycastInput = default;
                raycastInput.Start = position + up * stepHeight;
                raycastInput.End = position + up * depthOfWater;
                raycastInput.Filter = collisionFilter;

                if (collisionWorld.CastRay(raycastInput, out var closestHit))
                {
                    ref var collider = ref collisionWorld.Bodies[closestHit.RigidBodyIndex].Collider.Value;
                    if (collider.GetLeaf(closestHit.ColliderKey, out var leaf))
                        collider = ref *leaf.Collider;

                    layerMask = collider.Filter.BelongsTo;
                    if ((layerMask & waterMask) != 0)
                    {
                        normal = -closestHit.SurfaceNormal;

                        return closestHit.Fraction * depthOfWater;
                    }
                }
            }

            normal = float3.zero;

            return 0.0f;
        }

#if GAME_DEBUG_COMPARSION
        public uint frameIndex;

        public FixedString32Bytes hitsName;
        public FixedString32Bytes angleName;
        public FixedString32Bytes characterAngleName;
        public FixedString32Bytes directName;
        public FixedString32Bytes dragName;
        public FixedString32Bytes distanceName;
        public FixedString32Bytes surfaceRotationName;
        public FixedString32Bytes velocityName;
        public FixedString32Bytes oldVelocityName;
        public FixedString32Bytes oldStatusName;
        public FixedString32Bytes oldNormalName;
        public FixedString32Bytes oldPositionName;
        public FixedString32Bytes oldRotationName;
        public FixedString32Bytes newRotationName;
        public FixedString32Bytes newPositionName;
        public FixedString32Bytes newNormalName;
        public FixedString32Bytes newVelocityName;

        public ComparisonStream<uint> stream;
        [ReadOnly]
        public NativeArray<GameEntityIndex> entityIndices;
        [ReadOnly]
        public ComponentLookup<GameEntityIndex> entityIndexMap;
#endif
        public unsafe void Execute(int index)
        {
            var flag = flags[index];
            var instance = instances[index];

            bool isKinematic = index < characterVelocities.Length,
                isCanSwim = (instance.flag & GameNodeCharacterData.Flag.CanSwim) == GameNodeCharacterData.Flag.CanSwim,
                isFlyOnly = (instance.flag & GameNodeCharacterData.Flag.FlyOnly) == GameNodeCharacterData.Flag.FlyOnly;
            var angle = angles[index];
            var characterAngle = characterAngles[index];
            ///必须,否则将导致ACT之后不同步
            bool isAngleChanged = angle.value != characterAngle.value;

            var oldStatus = characterStates[index];

            var drag = drags[index];

            float3 direct = directs[index].value + drag.velocity * deltaTime;
            var indirect = indirects[index];
            bool isDirectZero = direct.Equals(float3.zero),
                isIndirectZero = indirect.isZero;

            var collisionWorld = physicsWorld.collisionWorld;
            var rigidbodies = collisionWorld.Bodies;
            int rigidbodyIndex = collisionWorld.GetRigidBodyIndex(entityArray[index]);
            if(rigidbodyIndex == -1)
            {
                UnityEngine.Debug.LogError($"Error Rigidbody Index of {entityArray[index]}!");

                return;
            }

            var rigidbody = rigidbodies[rigidbodyIndex];

#if GAME_DEBUG_COMPARSION
            //UnityEngine.Debug.Log($"{rigidbody.Entity} {oldStatus} In {frameIndex}");
            UnityEngine.Assertions.Assert.AreEqual(entityIndexMap[rigidbody.Entity].value, entityIndices[index].value);

            stream.Begin(entityIndices[index].value);
            stream.Assert(oldStatusName, oldStatus);
            stream.Assert(angleName, angle.value);
            stream.Assert(characterAngleName, characterAngle.value);
            stream.Assert(directName, directs[index].value);
            stream.Assert(dragName, drag.velocity);
            stream.Assert(distanceName, indirect.value);
            stream.Assert(velocityName, indirect.velocity);
#endif

            if (!isAngleChanged && isDirectZero && isIndirectZero/* && (flag.value & GameNodeCharacterFlag.Flag.Dirty) != GameNodeCharacterFlag.Flag.Dirty*/)
            {
                bool isStatic = oldStatus.IsStatic(isCanSwim, isFlyOnly);
                /*switch (oldStatus.area)
                {
                    case GameNodeCharacterStatus.Area.Normal:
                        isStatic = oldStatus.value == GameNodeCharacterStatus.Status.Firming;
                        break;
                    case GameNodeCharacterStatus.Area.Water:
                        isStatic = isCanSwim || oldStatus.value == GameNodeCharacterStatus.Status.Firming;
                        break;
                    case GameNodeCharacterStatus.Area.Air:
                        isStatic = isFlyOnly;
                        break;
                    case GameNodeCharacterStatus.Area.Fix:
                        switch (oldStatus.value)
                        {
                            case GameNodeCharacterStatus.Status.Contacting:
                            case GameNodeCharacterStatus.Status.Firming:
                                isStatic = true;
                                break;
                            default:
                                isStatic = false;
                                break;
                        }
                        break;
                    case GameNodeCharacterStatus.Area.Climb:
                        isStatic = oldStatus.value == GameNodeCharacterStatus.Status.Firming;
                        break;
                    default:
                        isStatic = false;
                        break;
                }*/

                if (isStatic)
                {
                    if (index < characterDesiredVelocities.Length)
                        characterDesiredVelocities[index] = default;

                    if (isKinematic)
                    {
                        //UnityEngine.Debug.Log(rigidbody.Entity.ToString() + rigidbody.WorldFromBody.pos + frameIndex);

                        Translation translation;
                        translation.Value = rigidbody.WorldFromBody.pos;
                        translations[index] = translation;

                        Rotation rotation;
                        rotation.Value = rigidbody.WorldFromBody.rot;
                        rotations[index] = rotation;

                        characterVelocities[index] = default;
                    }

                    physicsVelocities[index] = default;

                    if (flag.value != 0)
                    {
                        flag.value = 0;

                        flags[index] = flag;
                    }

                    //distanceHits[index].Clear();

#if GAME_DEBUG_COMPARSION
                    stream.Assert(surfaceRotationName, surfaces[index].rotation);
                    stream.Assert(oldPositionName, rigidbody.WorldFromBody.pos);
                    stream.Assert(oldRotationName, rigidbody.WorldFromBody.rot);
                    //stream.Assert(oldRotationName, math.mul(surfaces[index].rotation, quaternion.RotateY(characterAngle.value)));

                    //UnityEngine.Assertions.Assert.AreEqual(math.mul(surfaces[index].rotation, quaternion.RotateY(characterAngle.value)), rotations[index].Value, entityIndices[index].value.ToString());
                    stream.End();
#endif
                    return;
                }
            }

            bool isHasCollider = index < colliders.Length, isRotateAll = (instance.flag & GameNodeCharacterData.Flag.RotationAll) == GameNodeCharacterData.Flag.RotationAll;
            RigidTransform transform = isKinematic ? rigidbody.WorldFromBody : math.RigidTransform(rotations[index].Value, translations[index].Value);//, source = transform;
            quaternion originRotation = quaternion.RotateY(characterAngle.value);

            var hits = distanceHits[index].Reinterpret<DistanceHit>();
            DynamicBufferWriteOnlyWrapper<DistanceHit> wrapper;
            var distanceHitsCollector = new ListCollectorExclude<DistanceHit, DynamicBuffer<DistanceHit>, DynamicBufferWriteOnlyWrapper<DistanceHit>>(
                rigidbodyIndex, 
                instance.contactTolerance, 
                //rigidbodies,
                ref hits, 
                ref wrapper);

            ColliderDistanceInput input = default;
            {
                input.MaxDistance = instance.contactTolerance;
                input.Transform = isRotateAll ? transform : math.RigidTransform(originRotation, transform.pos);
                input.Collider = (Collider*)(isHasCollider ? colliders[index].value.GetUnsafePtr() : rigidbody.Collider.GetUnsafePtr());
            }

            collisionWorld.CalculateDistance(input, ref distanceHitsCollector);

            bool isSurfaceUp = (instance.flag & GameNodeCharacterData.Flag.SurfaceUp) == GameNodeCharacterData.Flag.SurfaceUp;
            float3 up = math.up(), inveseGravityDirection = up,
                gravity = this.gravity * (instance.gravityFactor * deltaTime),
                velocity = isKinematic ? characterVelocities[index].value : physicsVelocities[index].Linear;
            GameNodeSurface surface = surfaces[index];


#if GAME_DEBUG_COMPARSION
            stream.Assert(hitsName, distanceHitsCollector.NumHits);
            stream.Assert(surfaceRotationName, surface.rotation);
            stream.Assert(oldPositionName, transform.pos);
            //stream.Assert(oldRotationName, math.mul(surface.rotation, originRotation));
            stream.Assert(oldRotationName, transform.rot);
            //UnityEngine.Assertions.Assert.AreEqual(math.mul(surface.rotation, originRotation), transform.rot, entityIndices[index].value.ToString());
            stream.Assert(oldVelocityName, velocity);

            /*string log = $"{entityIndices[index].value} : {frameIndex} : {rigidbody.Entity.Index} : {physicsWorld.NumBodies}";
            for(int i = 0; i < distanceHitsCollector.NumHits; ++i)
                log += $"-{distanceHitsCollector.hits[i].Entity.Index} : {rigidbodies[distanceHitsCollector.hits[i].RigidBodyIndex].WorldFromBody}";

            log += $" {instance.contactTolerance} ";
            int num = rigidbodies.Length;
            for (int i = 0; i < num; ++i)
            {
                if (entityIndexMap.Exists(rigidbodies[i].Entity) && entityIndexMap[rigidbodies[i].Entity].value == 681)
                {
                    log += rigidbodies[i].Entity.Index;
                    var d = rigidbodies[i].CalculateAabb();
                    log += $" Destination {d.Min} : {d.Max}";

                    d = input.Collider->CalculateAabb(rigidbody.WorldFromBody);
                    log += $" Source {d.Min} : {d.Max}";
                    
                    ColliderDistanceInput t = input;
                    t.Transform = math.mul(math.inverse(rigidbodies[i].WorldFromBody), t.Transform);
                    log += rigidbodies[i].CalculateDistance(t);
                    break;
                }
            }

            UnityEngine.Debug.Log(log);*/

            /*if (distanceHitsCollector.NumHits < 1)
            {
                int num = rigidbodies.Length;
                for (int i = 0; i < num; ++i)
                {
                    if (entityIndexMap.Exists(rigidbodies[i].Entity) && entityIndexMap[rigidbodies[i].Entity].value == 681)
                    {
                        ColliderDistanceInput t = input;
                        t.Transform = math.mul(math.inverse(rigidbodies[i].WorldFromBody), t.Transform);
                        if (rigidbodies[i].CalculateDistance(t))
                        {
                            physicsWorld.CalculateDistance(input, ref distanceHitsCollector);
                        }
                    }
                }
            }*/
#endif

            CharacterControllerStepInput stepInput;
            {
                //stepInput.distanceHits = distanceHitsCollector.hits.AsNativeArray();
                stepInput.physicsWorld = physicsWorld;
                stepInput.up = isSurfaceUp ? math.mul(surface.rotation, up) : up;
                stepInput.deltaTime = deltaTime;
                stepInput.maxMovementSpeed = instance.maxMovementSpeed > math.FLT_MIN_NORMAL ? instance.maxMovementSpeed : float.MaxValue;
                stepInput.skinWidth = instance.skinWidth;
                stepInput.contactTolerance = instance.contactTolerance;
                stepInput.maxSlope = math.max(instance.stepSlope, 0.0f);
                stepInput.rigidbodyIndex = rigidbodyIndex;
            }

            var motionDatas = physicsWorld.motionDatas;

            //stepInput.distanceHits.Sort(new DistanceHitComparer());

            //var constraints = this.constraints[index].Reinterpret<SurfaceConstraintInfo>();

            bool isUnstopping = false,
                isNeedToSortHits = true,
                isContacting = oldStatus.area != GameNodeCharacterStatus.Area.Air &&
                oldStatus.area != GameNodeCharacterStatus.Area.Fix &&
                (oldStatus.value == GameNodeCharacterStatus.Status.Contacting ||
                oldStatus.value == GameNodeCharacterStatus.Status.Firming),
                isAir = oldStatus.area == GameNodeCharacterStatus.Area.Air && math.dot(velocity + gravity * 0.5f, inveseGravityDirection) > math.FLT_MIN_NORMAL,
                isJump = math.dot(indirect.velocity, stepInput.up) > math.FLT_MIN_NORMAL,
                isClimb = false;
            int numDynamicBodies = collisionWorld.NumDynamicBodies, numDistanceHits = distanceHitsCollector.hits.Length;
            CharacterSupportState supportStatus = CharacterSupportState.Unsupported;
            GameNodeCharacterStatus.Status value;
            GameNodeCharacterStatus.Area area = GameNodeCharacterStatus.Area.Normal;
            half climbAngle = angle.value;
            float3 centerOfMass = isHasCollider ? centerOfMasses[index].value : motionDatas[rigidbodyIndex].BodyFromMotion.pos,
                position = transform.pos,
                //forward = math.forward(originRotation), 
                forward = math.forward(originRotation);//forward;
            quaternion climbRotation = surface.rotation;
            Aabb aabb = input.Collider->CalculateAabb();//rigidbody.Collider.Value.CalculateAabb();
            GameNodeCharacterSurface characterSurface;
            characterSurface.layerMask = 0;
            if (isFlyOnly)
            {
                value = GameNodeCharacterStatus.Status.None;

                characterSurface.layerMask = CalculateLayerMask(
                    distanceHitsCollector.hits,
                    rigidbodies,
                    //terrainMask, 
                    out isUnstopping);
                characterSurface.fraction = 1.0f;
                characterSurface.normal = up;
                characterSurface.velocity = float3.zero;

            }
            else if (isJump)
            {
                area = GameNodeCharacterStatus.Area.Air;

                value = GameNodeCharacterStatus.Status.Firming;

                characterSurface.layerMask = CalculateLayerMask(
                    distanceHitsCollector.hits,
                    rigidbodies,
                    //terrainMask, 
                    out isUnstopping);
                characterSurface.fraction = 0.0f;
                characterSurface.normal = stepInput.up;
                characterSurface.velocity = float3.zero;
            }
            else
            {
                float3 desiredVelocity = velocity - gravity * 0.5f;
                //if (!isAir && !isJump)
                {
                    float minDistance = float.MaxValue, suction = 0.0f;
                    float3 climbPosition = transform.pos;
                    DistanceHit distanceHit;
                    {
                        int wayPointIndex, pointIndex;
                        uint belongsTo;
                        float distance, fraction;
                        float3 point, wayPoint, wayForward;
                        RigidTransform inverseTransform = math.inverse(transform);
                        RigidBody tempRigidbody;
                        GameWayPoint start, end;
                        GameWayData way;
                        DynamicBuffer<GameWayPoint> wayPoints;
                        for (int i = 0; i < numDistanceHits; ++i)
                        {
                            distanceHit = distanceHitsCollector.hits[i];
                            tempRigidbody = rigidbodies[distanceHit.RigidBodyIndex];
                            belongsTo = tempRigidbody.Collider.Value.GetLeafFilter(distanceHit.ColliderKey).BelongsTo;

                            characterSurface.layerMask |= belongsTo;

                            if (distanceHit.Distance < 0.0f)
                                isUnstopping = true;

                            if ((belongsTo & instance.climbMask) != 0 && !isAir && !isJump)
                            {
                                if (this.wayPoints.HasBuffer(tempRigidbody.Entity))
                                {
                                    wayPoints = this.wayPoints[tempRigidbody.Entity];
                                    way = ways[tempRigidbody.Entity];

                                    point = math.transform(math.inverse(tempRigidbody.WorldFromBody), transform.pos);
                                    if (GameWayPoint.FindClosestPoint(
                                        wayPoints,
                                        point,
                                        ref way.maxDistanceSq,
                                        out wayPoint,
                                        out fraction,
                                        out wayPointIndex))
                                    {
                                        start = wayPoints[wayPointIndex];
                                        end = wayPoints[wayPointIndex + 1];

                                        wayForward = end.value - start.value;

                                        pointIndex = wayPointIndex;
                                        wayPoint = GameWayPoint.Move(
                                            isDirectZero ? 0 : (int)math.sign(math.dot(math.mul(tempRigidbody.WorldFromBody.rot, wayForward), math.mul(surface.rotation, direct))),
                                            wayPoint,
                                            wayPoints,
                                            ref pointIndex,
                                            ref fraction,
                                            ref start,
                                            ref end);

                                        distance = math.distancesq(point, wayPoint);
                                        if (distance < minDistance)
                                        {
                                            isClimb = true;

                                            minDistance = distance;

                                            suction = math.lerp(start.suction, end.suction, fraction);

                                            forward = wayForward;// pointIndex == wayPointIndex ? wayForward : end.value - start.value;

                                            climbPosition = math.transform(tempRigidbody.WorldFromBody, wayPoint);

                                            climbRotation = tempRigidbody.WorldFromBody.rot;
                                        }
                                    }
                                }
                                else
                                    isClimb = true;

                                continue;
                            }

                            if ((belongsTo & terrainMask) != 0)
                            {
                                if (instance.footHeight > math.FLT_MIN_NORMAL ? math.transform(inverseTransform, distanceHit.Position).y < instance.footHeight : true)
                                    continue;
                            }

                            distanceHitsCollector.hits[i--] = distanceHitsCollector.hits[--numDistanceHits];
                            distanceHitsCollector.hits[numDistanceHits] = distanceHit;

                        }
                    }

                    if (isClimb)
                    {
                        if (minDistance < float.MaxValue)
                        {
                            forward = math.normalizesafe(forward);

                            float3 wayRight = math.normalizesafe(math.cross(math.float3(0.0f, 0.0f, 1.0f), forward)), wayUp;
                            if (wayRight.Equals(float3.zero))
                            {
                                wayRight = math.float3(math.sign(forward.z), 0.0f, 0.0f);

                                wayUp = up;

                                stepInput.up = math.mul(climbRotation, wayUp);
                            }
                            else
                            {
                                wayUp = math.cross(forward, wayRight);

                                stepInput.up = math.mul(climbRotation, wayUp);

                                float3 climbUp = isSurfaceUp ? math.mul(math.inverse(surface.rotation), stepInput.up) : stepInput.up;
                                climbAngle = (half)math.atan2(-climbUp.x, -climbUp.z);
                            }

                            if (minDistance > instance.contactTolerance * instance.contactTolerance)
                            {
                                quaternion target = quaternion.RotateY(climbAngle),
                                    wayRotation = math.mul(climbRotation, math.quaternion(math.float3x3(wayRight, wayUp, forward))),
                                    surfaceTarget = isSurfaceUp ? math.mul(wayRotation, target) : target,
                                    rotation = isRotateAll ? surfaceTarget : target;
                                position = RotatePosition(centerOfMass,
                                    rotation,
                                    math.RigidTransform(isRotateAll ? transform.rot : originRotation, climbPosition));
                                float3 climbDistance = position - transform.pos,
                                    climbDistanceProject = Math.ProjectSafe(climbDistance, math.forward(wayRotation)/*desiredVelocity*/),
                                    climbCorner = transform.pos + climbDistanceProject;
                                var collector = new ClosestHitCollectorMask<ColliderCastHit>(rigidbodyIndex, (uint)instance.climbMask.value, 1.0f, rigidbodies);

                                ColliderCastInput colliderCastInput = default;
                                colliderCastInput.Collider = input.Collider;
                                colliderCastInput.Orientation = transform.rot;
                                colliderCastInput.Start = transform.pos;
                                colliderCastInput.End = climbCorner;

                                bool isResetTransform = false, isHit = collisionWorld.CastCollider(colliderCastInput, ref collector) && collector.NumHits > 0;
                                float fraction = collector.fraction;
                                if (!isHit)
                                {
                                    colliderCastInput.Orientation = rotation;
                                    colliderCastInput.Start = climbCorner;
                                    colliderCastInput.End = position;

                                    collector.Reset(1.0f);

                                    if (collisionWorld.CastCollider(colliderCastInput, ref collector) && collector.NumHits > 0)
                                    {
                                        isResetTransform = fraction == 1.0f;
                                        if (isResetTransform)
                                        {
                                            transform.pos = climbCorner + (climbDistance - climbDistanceProject) * collector.fraction;
                                            transform.rot = surfaceTarget;
                                        }
                                    }
                                    else
                                    {
                                        isResetTransform = true;

                                        transform.pos = position;
                                        transform.rot = surfaceTarget;
                                    }
                                }

                                if (!isResetTransform)
                                {
                                    isResetTransform = fraction > math.FLT_MIN_NORMAL;
                                    if (isResetTransform)
                                        transform.pos += climbDistanceProject * fraction;
                                }

                                if (isResetTransform)
                                {
                                    /*transform.pos = position;
                                    transform.rot = surfaceTarget;*/
                                    input.Transform = transform;

                                    distanceHitsCollector.hits.Clear();
                                    collisionWorld.CalculateDistance(input, ref distanceHitsCollector);
                                    //stepInput.distanceHits = distanceHitsCollector.hits.AsNativeArray();
                                    //stepInput.distanceHits.Sort(new DistanceHitComparer());

                                    characterSurface.layerMask = 0;
                                    numDistanceHits = distanceHitsCollector.hits.Length;

                                    isUnstopping = false;

                                    uint belongsTo;
                                    var inverseTransform = math.inverse(transform);
                                    for (int i = 0; i < numDistanceHits; ++i)
                                    {
                                        distanceHit = distanceHitsCollector.hits[i];
                                        belongsTo = rigidbodies[distanceHit.RigidBodyIndex].Collider.Value.GetLeafFilter(distanceHit.ColliderKey).BelongsTo;

                                        characterSurface.layerMask |= belongsTo;

                                        /*if (distanceHit.Distance < 0.0f)
                                            isUnstopping = true;*/

                                        if ((belongsTo & instance.climbMask) != 0)
                                            continue;

                                        if ((belongsTo & terrainMask) != 0)
                                        {
                                            if (instance.footHeight > math.FLT_MIN_NORMAL ? math.transform(inverseTransform, distanceHit.Position).y < instance.footHeight : true)
                                                continue;
                                        }

                                        distanceHitsCollector.hits[i--] = distanceHitsCollector.hits[--numDistanceHits];
                                        distanceHitsCollector.hits[numDistanceHits] = distanceHit;
                                    }
                                }
                            }

                            instance.suction += suction;
                        }

                        stepInput.maxSlope = 0.0f;
                    }
                }

                if (numDistanceHits < distanceHitsCollector.hits.Length)
                    stepInput.distanceHits = distanceHitsCollector.hits.AsNativeArray().Slice(0, numDistanceHits);
                else
                {
                    stepInput.distanceHits = distanceHitsCollector.hits.AsNativeArray().Slice();

                    isNeedToSortHits = false;
                }

                stepInput.distanceHits.Sort(new DistanceHitComparer());

                float3 surfaceVelocity = instance.contactTolerance / deltaTime * math.normalize(velocity) - instance.suction * deltaTime * stepInput.up;// velocity - (instance.suction * deltaTime + instance.contactTolerance / deltaTime) * stepInput.Up;

                supportStatus = CheckSupport(
                           stepInput,
                           transform.pos, //(instance.flag & GameNodeCharacterData.Flag.RotationAll) == GameNodeCharacterData.Flag.RotationAll ? transform : math.RigidTransform(originRotation, transform.pos),
                                          //ref constraints,
                           surfaceVelocity,
                           out characterSurface.normal,
                           out characterSurface.velocity);

#if GAME_DEBUG_COMPARSION
                stream.Assert(oldNormalName, characterSurface.normal);
#endif

                if (supportStatus != CharacterSupportState.Supported && isClimb)
                {
                    supportStatus = CharacterSupportState.Supported;

                    characterSurface.normal = stepInput.up;
                }

                switch (supportStatus)
                {
                    case CharacterSupportState.Supported:
                        characterSurface.fraction = 0.0f;

                        value = GameNodeCharacterStatus.Status.Firming;
                        break;
                    case CharacterSupportState.Sliding:
                        characterSurface.fraction = 0.0f;

                        if (characterSurface.normal.Equals(float3.zero))
                            characterSurface.normal = stepInput.up;

                        value = GameNodeCharacterStatus.Status.Sliding;
                        break;
                    default:
                        if (isAir)
                        {
                            characterSurface.fraction = 1.0f;
                            characterSurface.normal = inveseGravityDirection;

                            value = GameNodeCharacterStatus.Status.None;
                        }
                        else
                        {
                            //float3 point = math.transform(transform, math.float3(aabb.Center.x, aabb.Max.y, 0.0f/*aabb.Center.z*/));
                            float3 origin = transform.pos + stepInput.up * aabb.Max.y;
                            float displacement = -instance.raycastLength - aabb.Max.y;

                            RaycastInput raycastInput = default;
                            //raycastInput.Filter = rigidbody.Collider.Value.Filter;
                            //raycastInput.Filter.CollidesWith = terrainMask;
                            raycastInput.Filter = input.Collider->Filter;
                            raycastInput.Start = origin;// transform.pos + stepInput.up * aabb.Max.y;
                            raycastInput.End = origin + stepInput.up * displacement;// transform.pos + stepInput.up * -instance.raycastLength;
                            //raycastInput.Start = point;
                            //raycastInput.End = point - stepInput.Up * (aabb.Max.y + instance.raycastLength);

                            var collector = new StaticBodyCollector<RaycastHit>(numDynamicBodies, 1.0f);
                            if (collisionWorld.CastRay(raycastInput, ref collector) && collector.NumHits > 0)
                            {
                                var closestHit = collector.closestHit;
                                characterSurface.fraction = CalculateFraction(closestHit.Fraction, aabb.Max.y, instance.raycastLength, out float distance);

                                //position: fixed Sync:
                                position = origin + stepInput.up * (displacement * closestHit.Fraction);// closestHit.Position;

/*#if GAME_DEBUG_COMPARSION
                                if (entityIndices[index].value == 30 || entityIndices[index].value == 6 || entityIndices[index].value == 12998)
                                {
                                    int entityIndex = entityIndexMap.HasComponent(closestHit.Entity) ? entityIndexMap[closestHit.Entity].value : -1;
                                    UnityEngine.Debug.Log($"P {entityIndex}:{closestHit}:{raycastInput}:{entityIndices[index].value}:{frameIndex}");
                                }
#endif*/
                                if (characterSurface.fraction > instance.supportFraction)
                                {
                                    characterSurface.normal = inveseGravityDirection;

                                    value = GameNodeCharacterStatus.Status.Unsupported;
                                }
                                else if (characterSurface.fraction > instance.stepFraction)
                                {
                                    characterSurface.normal = inveseGravityDirection;

                                    value = GameNodeCharacterStatus.Status.Supported;
                                }
                                else
                                {
                                    //UnityEngine.Assertions.Assert.IsTrue(distance > instance.contactTolerance);

                                    if (math.dot(closestHit.SurfaceNormal, stepInput.up) < stepInput.maxSlope)
                                    {
                                        characterSurface.normal = stepInput.up;

                                        value = GameNodeCharacterStatus.Status.Sliding;
                                    }
                                    else
                                    {
                                        characterSurface.fraction = 0.0f;
                                        characterSurface.normal = closestHit.SurfaceNormal;

#if GAME_DEBUG_COMPARSION
                                        stream.Assert(newNormalName, characterSurface.normal);
#endif

                                        value = distance > instance.contactTolerance ? GameNodeCharacterStatus.Status.Contacting : GameNodeCharacterStatus.Status.Firming;
                                    }
                                }
                            }
                            else
                            {
                                characterSurface.fraction = 1.0f;
                                characterSurface.normal = inveseGravityDirection;

                                value = GameNodeCharacterStatus.Status.None;
                            }
                        }
                        break;
                }

                //UnityEngine.Debug.Log($"{characterSurface.normal} : {characterSurface.velocity} : {numDistanceHits} : {distanceHitsCollector.hits.Length}");
                if (oldStatus.area == GameNodeCharacterStatus.Area.Fix && !isIndirectZero)
                    area = GameNodeCharacterStatus.Area.Fix;
                else if (instance.headLength > math.FLT_MIN_NORMAL && !isAir && /*oldStatus.area != GameNodeCharacterStatus.Area.Fix && */math.dot(forward, desiredVelocity) > 0.0f)
                {
                    switch (value)
                    {
                        case GameNodeCharacterStatus.Status.Unsupported:
                        case GameNodeCharacterStatus.Status.Supported:
                        case GameNodeCharacterStatus.Status.Contacting:
                        case GameNodeCharacterStatus.Status.Firming:
                            float bodyLength = aabb.Max.z - instance.headLength, length = bodyLength * instance.stepSlope;
                            float3 point = math.float3(0.0f, 0.0f, length);
                            length = math.sqrt(bodyLength * bodyLength - length * length);

                            float3 totalDistance = (velocity + gravity * 0.5f) * deltaTime;
                            float upComponent = math.dot(totalDistance, stepInput.up);
                            point = math.transform(transform, point) + totalDistance + stepInput.up * -upComponent;

                            float3 origin = point + stepInput.up * (length + aabb.Max.y);
                            float displacement = -length * 2.0f - instance.raycastLength - aabb.Max.y;

                            RaycastInput raycastInput = default;
                            raycastInput.Start = origin;// point + stepInput.up * (length + aabb.Max.y);
                            raycastInput.End = origin + stepInput.up * displacement;// point - stepInput.up * (length + instance.raycastLength);
                            raycastInput.Filter = input.Collider->Filter;
                            //raycastInput.Filter = rigidbody.Collider.Value.Filter;
                            //raycastInput.Filter.CollidesWith = terrainMask;
                            var collector = new StaticBodyCollector<RaycastHit>(numDynamicBodies, 1.0f);
                            if (collisionWorld.CastRay(raycastInput, ref collector) && collector.NumHits > 0)
                            {
                                var closestHit = collector.closestHit;
                                characterSurface.fraction = CalculateFraction(closestHit.Fraction, aabb.Max.y + length + length, instance.raycastLength, out float distance);

                                //distance += upComponent;
                                /*if (distance > 0.0f)
                                    distance = math.min(distance, distance + math.dot(gravity, stepInput.Up) * (deltaTime * 0.5f));*/

                                //ClosestHitPosition: fixed Sync:
                                float3 closestHitPosition = origin + stepInput.up * (displacement * closestHit.Fraction)/*closestHit.Position*/, 
                                    hitDistance = closestHitPosition - position,
                                    normal = math.normalizesafe(math.cross(
                                            math.normalizesafe(hitDistance),
                                            math.normalize(math.cross(stepInput.up, math.forward(transform.rot)))));

                                if (math.dot(normal, stepInput.up) < instance.stepSlope)
                                {
                                    switch (value)
                                    {
                                        case GameNodeCharacterStatus.Status.Contacting:
                                        case GameNodeCharacterStatus.Status.Firming:
                                            if (isContacting && math.dot(hitDistance, stepInput.up) < 0.0f)
                                                area = GameNodeCharacterStatus.Area.Fix;
                                            break;
                                    }
                                }
                                else if (value != GameNodeCharacterStatus.Status.Firming && distance + upComponent < 0.0f)
                                {
                                    if (supportStatus != CharacterSupportState.Supported)
                                    {
                                        characterSurface.normal = normal;

#if GAME_DEBUG_COMPARSION
                                        stream.Assert(newNormalName, characterSurface.normal);

                                        /*if (entityIndices[index].value == 30 || entityIndices[index].value == 6 || entityIndices[index].value == 12998)
                                        {
                                            int entityIndex = entityIndexMap.HasComponent(closestHit.Entity) ? entityIndexMap[closestHit.Entity].value : -1;
                                            UnityEngine.Debug.Log($"{entityIndex}:{closestHit}:{transform.rot}:{stepInput.up}:{position}:{normal}:{entityIndices[index].value}:{frameIndex}");
                                        }*/
#endif
                                    }

                                    //value == GameNodeCharacterStatus.Status.Contacting为了防止卡住
                                    value = isContacting || value == GameNodeCharacterStatus.Status.Contacting ? GameNodeCharacterStatus.Status.Contacting : GameNodeCharacterStatus.Status.Sliding;
                                }

                                /*switch (value)
                                {
                                    case GameNodeCharacterStatus.Status.Supported:
                                    case GameNodeCharacterStatus.Status.Contacting:
                                        var normal = math.normalizesafe(math.cross(
                                            math.normalizesafe(closestHit.Position - position),
                                            math.normalize(math.cross(stepInput.Up, math.forward(transform.rot)))));

                                        if (math.dot(normal, stepInput.Up) > instance.stepSlope)
                                        {
                                            characterSurface.normal = normal;

                                            if (distance > 0.0f)
                                            {
                                                if (isContacting && value == GameNodeCharacterStatus.Status.Contacting)
                                                    area = GameNodeCharacterStatus.Area.Fix;
                                            }
                                            else
                                                value = isContacting ? GameNodeCharacterStatus.Status.Contacting : GameNodeCharacterStatus.Status.Sliding;
                                        }
                                        else
                                            area = GameNodeCharacterStatus.Area.Fix;
                                        break;
                                    case GameNodeCharacterStatus.Status.Sliding:
                                        break;
                                    case GameNodeCharacterStatus.Status.Firming:
                                        if (isContacting && distance > 0.0f)
                                            area = GameNodeCharacterStatus.Area.Fix;
                                        break;
                                }*/
                            }
                            else
                            {
                                switch (value)
                                {
                                    case GameNodeCharacterStatus.Status.Sliding:
                                        break;
                                    case GameNodeCharacterStatus.Status.Contacting:
                                    case GameNodeCharacterStatus.Status.Firming:
                                        if (isContacting)
                                            area = GameNodeCharacterStatus.Area.Fix;
                                        break;
                                    default:
                                        characterSurface.fraction = 1.0f;
                                        break;
                                }
                            }
                            break;
                    }
                }
            }

            quaternion targetRotation = originRotation;
            bool isCanFly = (instance.flag & GameNodeCharacterData.Flag.CanFly) == GameNodeCharacterData.Flag.CanFly, isDrop = false,
                isStep;
            switch (value)
            {
                case GameNodeCharacterStatus.Status.Sliding:
                case GameNodeCharacterStatus.Status.Contacting:
                case GameNodeCharacterStatus.Status.Firming:
                    if (oldStatus.area != GameNodeCharacterStatus.Area.Fix &&
                        (isContacting || !isAir && value != GameNodeCharacterStatus.Status.Contacting))
                    {
                        float buoyancy = 0.0f;
                        if (isClimb)
                        {
                            if (isSurfaceUp)
                                surface.rotation = climbRotation;
                            else
                                forward = math.mul(climbRotation, forward);

                            if (climbAngle != angle.value)
                            {
                                //isFallOrAir = false;

                                angle.value = climbAngle;

                                angles[index] = angle;
                            }

                            if (climbAngle != characterAngle.value)
                            {
                                targetRotation = quaternion.RotateY(climbAngle);

                                characterAngle.value = climbAngle;
                                characterAngles[index] = characterAngle;
                            }

                            direct = Math.Project(direct, math.forward(targetRotation)) * instance.climbDamping;

                            //transform.pos = climbPosition;

                            /*climbForward = isSurfaceUp ? math.mul(climbRotation, climbForward) : forward;

                            RigidTransform climbTransform = math.RigidTransform(
                                math.mul(math.quaternion(math.float3x3(math.cross(stepInput.Up, climbForward), stepInput.Up, climbForward)), math.inverse(targetRotation)),
                                transform.pos);
                            climbTransform = math.inverse(climbTransform);
                            info.distance += math.transform(climbTransform, climbPosition);*/

                            area = GameNodeCharacterStatus.Area.Climb;

                            isAngleChanged = false;

                            isStep = true;
                        }
                        else
                        {
                            isStep = value != GameNodeCharacterStatus.Status.Sliding &&
                                //area != GameNodeCharacterStatus.Area.Fix && 
                                math.dot(characterSurface.normal, inveseGravityDirection) > instance.staticSlope;

                            //forward = math.forward(targetRotation);

                            var filter = input.Collider->Filter;
                            filter.CollidesWith = terrainMask | waterMask;
                            float waterDistance = CalculateWaterDistance(
                                collisionWorld,
                                filter,
                                transform.pos,
                                inveseGravityDirection,
                                instance.waterMinHeight,
                                depthOfWater,
                                waterMask,
                                out uint layerMask, 
                                out float3 normal);
                            if (waterDistance > math.FLT_MIN_NORMAL)
                            {
                                area = GameNodeCharacterStatus.Area.Water;

                                characterSurface.layerMask |= layerMask;
                                characterSurface.normal = normal;// inveseGravityDirection;

                                isStep |= (instance.flag & GameNodeCharacterData.Flag.CanSwim) == GameNodeCharacterData.Flag.CanSwim;
                                if (isStep)
                                    direct *= instance.waterDamping;

                                if (instance.buoyancy > math.FLT_MIN_NORMAL)
                                {
                                    if (value == GameNodeCharacterStatus.Status.Firming)
                                        value = GameNodeCharacterStatus.Status.Contacting;

                                    buoyancy = CalculateBuoyancy(waterDistance, instance.waterMaxHeight, deltaTime, instance.buoyancy);
                                }
                            }
                        }

                        if (!isCanFly || area != GameNodeCharacterStatus.Area.Air)
                        {
                            bool isMovement = isContacting || isJump;
                            if (!isMovement)
                            {
                                switch (area)
                                {
                                    case GameNodeCharacterStatus.Area.Water:
                                    case GameNodeCharacterStatus.Area.Fix:
                                    case GameNodeCharacterStatus.Area.Climb:
                                        isMovement = true;
                                        break;
                                }
                            }

                            if (isMovement)
                            {
                                if (isAngleChanged)
                                {
                                    characterAngle.value = angle.value;
                                    characterAngles[index] = characterAngle;

                                    targetRotation = quaternion.RotateY(angle.value);

                                    forward = math.forward(targetRotation);
                                }

                                if (isSurfaceUp)
                                    forward = math.mul(surface.rotation, forward);

                                if (isStep)
                                {
                                    characterSurface.rotation = math.mul(
                                        CalculateSurfaceRotation(forward, stepInput.up, characterSurface.normal, out float3 right, out float3 tangent, out float3 binorm),
                                        math.inverse(targetRotation));

                                    velocity = CalculateMovement(
                                        right,
                                        forward,
                                        binorm,
                                        tangent,
                                        velocity,
                                        math.mul(characterSurface.rotation, direct / deltaTime),
                                        characterSurface.normal,
                                        characterSurface.velocity);

                                    /*if (isClimb)
                                    {
                                        climbForward = (climbPosition - transform.pos) / deltaTime;

                                        float dot = math.dot(climbForward, velocity);
                                        if (dot > 0.0f)
                                        {
                                            float lengthSq = math.lengthsq(velocity);
                                            climbForward -= lengthSq > math.lengthsq(climbForward) ? dot / lengthSq * velocity : velocity;
                                        }
                                        //climbForward -= Math.Project(climbForward, tangent);
                                        velocity += climbForward;
                                    }*/
                                }
                                else
                                    characterSurface.rotation = surface.rotation;
                            }
                            else
                            {
                                if (isAngleChanged)
                                {
                                    angle.value = characterAngle.value;
                                    angles[index] = angle;
                                }

                                if (isSurfaceUp)
                                {
                                    forward = math.mul(surface.rotation, forward);

                                    characterSurface.rotation = isStep ? math.mul(
                                        CalculateSurfaceRotation(
                                            forward,
                                            stepInput.up,
                                            characterSurface.normal,
                                            out _,
                                            out _,
                                            out _),
                                        math.inverse(targetRotation)) : quaternion.identity;// surface.rotation;
                                }
                                else
                                    characterSurface.rotation = surface.rotation;

                                if (value == GameNodeCharacterStatus.Status.Firming/* && area == GameNodeCharacterStatus.Area.Normal*/)
                                    velocity = characterSurface.velocity;
                            }
                        }
                        else
                        {
                            isStep = isFlyOnly;
                            if (!isStep && isAngleChanged)
                            {
                                angle.value = characterAngle.value;
                                angles[index] = angle;
                            }

                            characterSurface.rotation = quaternion.identity;// surface.rotation;
                        }

                        if (isJump)
                            value = GameNodeCharacterStatus.Status.Contacting;
                        else if (isAir)
                        {
                            value = GameNodeCharacterStatus.Status.Sliding;

                            isDrop = !isFlyOnly;
                        }
                        else
                        {
                            if (value == GameNodeCharacterStatus.Status.Firming && (!isStep || supportStatus != CharacterSupportState.Supported))
                                value = GameNodeCharacterStatus.Status.Contacting;

                            if (value == GameNodeCharacterStatus.Status.Firming)
                            {
                                if (!isDirectZero || !isIndirectZero || (characterSurface.layerMask & dynamicMask) != 0)
                                    value = GameNodeCharacterStatus.Status.Contacting;
                            }
                            else
                            {
                                if (isContacting)
                                    value = GameNodeCharacterStatus.Status.Contacting;

                                if (instance.suction > math.FLT_MIN_NORMAL)
                                    velocity -= instance.suction * deltaTime * characterSurface.normal;
                                else
                                    isDrop = true;
                            }
                        }

                        if (buoyancy > math.FLT_MIN_NORMAL)
                            velocity += buoyancy * deltaTime * characterSurface.normal;// inveseGravityDirection;
                    }
                    else
                    {
                        characterSurface.rotation = quaternion.identity;

                        value = GameNodeCharacterStatus.Status.Supported;

                        if (area == GameNodeCharacterStatus.Area.Fix)
                        {
                            isStep = false;

                            if(isAngleChanged)
                            {
                                angle.value = characterAngle.value;
                                angles[index] = angle;
                            }
                        }
                        else
                        {
                            var filter = input.Collider->Filter;
                            filter.CollidesWith = terrainMask | waterMask;
                            float waterDistance = CalculateWaterDistance(
                                    collisionWorld,
                                    filter,
                                    transform.pos,
                                    inveseGravityDirection,
                                    instance.waterMinHeight,
                                    depthOfWater,
                                    waterMask,
                                    out uint layerMask,
                                    out float3 normal);
                            if (waterDistance > math.FLT_MIN_NORMAL)
                            {
                                isStep = (instance.flag & GameNodeCharacterData.Flag.CanSwim) == GameNodeCharacterData.Flag.CanSwim;

                                area = GameNodeCharacterStatus.Area.Water;

                                characterSurface.layerMask |= layerMask;
                                characterSurface.normal = normal;

                                if (isStep)
                                {
                                    if (isAngleChanged)
                                    {
                                        characterAngle.value = angle.value;
                                        characterAngles[index] = characterAngle;

                                        targetRotation = quaternion.RotateY(angle.value);

                                        //forward = math.forward(targetRotation);
                                    }

                                    //velocity = direct * (instance.waterDamping / deltaTime);

                                    characterSurface.rotation = math.mul(
                                        CalculateSurfaceRotation(forward, stepInput.up, characterSurface.normal, out float3 right, out float3 tangent, out float3 binorm),
                                        math.inverse(targetRotation));

                                    velocity = CalculateMovement(
                                        right,
                                        forward,
                                        binorm,
                                        tangent,
                                        velocity,
                                        math.mul(characterSurface.rotation, direct * (instance.waterDamping / deltaTime)),
                                        characterSurface.normal,
                                        characterSurface.velocity);
                                }

                                if (instance.buoyancy > math.FLT_MIN_NORMAL)
                                {
                                    float buoyancy = CalculateBuoyancy(waterDistance, instance.waterMaxHeight, deltaTime, instance.buoyancy);

                                    velocity += buoyancy * deltaTime * normal;// inveseGravityDirection;
                                }
                            }
                            else if (isAir || isFlyOnly)
                            {
                                isStep = isFlyOnly;

                                area = GameNodeCharacterStatus.Area.Air;
                            }
                            else
                                isStep = false;

                            if (!isStep && isAngleChanged)
                            {
                                angle.value = characterAngle.value;
                                angles[index] = angle;
                            }

                            isDrop = true;
                        }
                    }
                    break;
                default:
                    isStep = false;

                    characterSurface.rotation = quaternion.identity;

                    if (area != GameNodeCharacterStatus.Area.Fix)
                    {
                        if (isAir)
                            area = GameNodeCharacterStatus.Area.Air;
                        else
                        {
                            var filter = input.Collider->Filter;
                            filter.CollidesWith = terrainMask | waterMask;
                            float waterDistance = CalculateWaterDistance(
                                collisionWorld,
                                filter,
                                transform.pos,
                                inveseGravityDirection,
                                instance.waterMinHeight,
                                depthOfWater,
                                waterMask,
                                out uint layerMask, 
                                out float3 normal);
                            if (waterDistance > math.FLT_MIN_NORMAL)
                            {
                                area = GameNodeCharacterStatus.Area.Water;

                                characterSurface.layerMask |= layerMask;
                                characterSurface.normal = normal;

                                if ((instance.flag & GameNodeCharacterData.Flag.CanSwim) == GameNodeCharacterData.Flag.CanSwim)
                                {
                                    if (isAngleChanged)
                                    {
                                        characterAngle.value = angle.value;
                                        characterAngles[index] = characterAngle;

                                        targetRotation = quaternion.RotateY(angle.value);

                                        //forward = math.forward(targetRotation);
                                    }

                                    //velocity = direct * (instance.waterDamping / deltaTime);

                                    characterSurface.rotation = math.mul(
                                        CalculateSurfaceRotation(forward, stepInput.up, characterSurface.normal, out float3 right, out float3 tangent, out float3 binorm),
                                        math.inverse(targetRotation));

                                    velocity = CalculateMovement(
                                        right,
                                        forward,
                                        binorm,
                                        tangent,
                                        velocity,
                                        math.mul(characterSurface.rotation, direct * (instance.waterDamping / deltaTime)),
                                        characterSurface.normal,
                                        characterSurface.velocity);

                                    isStep = true;
                                }

                                if (instance.buoyancy > math.FLT_MIN_NORMAL)
                                {
                                    float buoyancy = CalculateBuoyancy(waterDistance, instance.waterMaxHeight, deltaTime, instance.buoyancy);

                                    velocity += buoyancy * deltaTime * normal;// inveseGravityDirection;
                                }
                            }
                        }

                        if (isFlyOnly && area == GameNodeCharacterStatus.Area.Normal)
                            area = GameNodeCharacterStatus.Area.Air;

                        if (area == GameNodeCharacterStatus.Area.Air)
                            isStep = isFlyOnly;
                    }

                    /*switch (area)
                    {
                        case GameNodeCharacterStatus.Area.Water:
                            if ((instance.flag & GameNodeCharacterData.Flag.CanSwim) == GameNodeCharacterData.Flag.CanSwim)
                            {
                                if (isAngleChanged)
                                {
                                    characterAngle.value = angle.value;
                                    characterAngles[index] = characterAngle;

                                    targetRotation = quaternion.RotateY(angle.value);

                                    forward = math.forward(targetRotation);
                                }
                                
                                velocity = info.distance * (instance.waterDamping / deltaTime) + (oldStatus.area == GameNodeCharacterStatus.Area.Water ? float3.zero : velocity);
                                    
                                isStep = true;
                            }
                            velocity += instance.buoyancy * deltaTime * inveseGravityDirection;
                            break;
                        case GameNodeCharacterStatus.Area.Air:
                            break;
                        default:
                            if(oldStatus.area == GameNodeCharacterStatus.Area.Air || isFlyOnly)
                                area = GameNodeCharacterStatus.Area.Air;
                            break;
                    }*/

                    isDrop = !isStep;

                    /*if(isCanFly && area == GameNodeCharacterStatus.Area.Air)
                        isStep = true;
                    else */
                    if (isDrop && isAngleChanged)
                    {
                        angle.value = characterAngle.value;
                        angles[index] = angle;
                    }
                    break;
            }

            if (area == GameNodeCharacterStatus.Area.Air)
            {
                if (isCanFly)
                {
                    if (isFlyOnly)
                    {
                        if (isAngleChanged)
                        {
                            characterAngle.value = angle.value;
                            characterAngles[index] = characterAngle;

                            targetRotation = quaternion.RotateY(angle.value);

                            //forward = math.forward(targetRotation);
                        }

                        if (oldStatus.area == GameNodeCharacterStatus.Area.Air)
                            velocity = float3.zero;
                    }

                    velocity += direct * (instance.airDamping / deltaTime);
                }

                var direction = directions[index].value;
                float speed = math.dot(velocity, direction);
                if(speed < instance.airSpeed)
                    velocity += (instance.airSpeed - speed) * direction;
            }

            float3 directVelocity = isStep ? velocity : float3.zero;
            if (!isIndirectZero)
            {
                float3 indirectValue = indirect.value / deltaTime;
                if (!isStep)
                    indirectValue -= Math.ProjectSafe(indirectValue, velocity);

                velocity += indirectValue + indirect.velocity;
            }

            quaternion surfaceRotation, orientation;
            if (isSurfaceUp/* && area == GameNodeCharacterStatus.Area.Normal*/)
            {
                switch(area)
                {
                    case GameNodeCharacterStatus.Area.Normal:
                        surfaceRotation = characterSurface.rotation;
                        break;
                    /*case GameNodeCharacterStatus.Area.Fix:
                        surfaceRotation = surface.rotation;
                        break;*/
                    default:
                        surfaceRotation = quaternion.identity;
                        break;
                }

                orientation = Math.RotateTowards(
                    surface.rotation,
                    surfaceRotation, //characterSurface.rotation,
                    instance.angluarSpeed * deltaTime);

                surfaceRotation = math.mul(orientation, math.inverse(surface.rotation));

                surface.rotation = orientation;

                orientation = math.mul(surface.rotation, targetRotation);

                surfaces[index] = surface;
            }
            else
            {
                surfaceRotation = quaternion.identity;

                orientation = targetRotation;
            }

            float3 angular = Math.ToAngular(math.mul(orientation, math.inverse(transform.rot))) / deltaTime;
            if (index < characterDesiredVelocities.Length)
            {
                GameNodeCharacterDesiredVelocity desiredVelocity;
                desiredVelocity.linear = directVelocity;
                desiredVelocity.angular = angular;
                characterDesiredVelocities[index] = desiredVelocity;
            }

            if (isKinematic)
            {
                gravity *= 0.5f;

                if (isDrop)
                    velocity += gravity;

                //float3 inveseGravityComponent = Math.Project(velocity, inveseGravityDirection);

                //var temp = transform.pos;

                //float3 orginVelocity = velocity;

                quaternion originOrientation = isRotateAll ? transform.rot : originRotation;
                float3 originDistance = math.mul(isRotateAll ? math.mul(surfaceRotation, transform.rot) : originRotation, centerOfMass), 
                    rotationDistance = math.mul(isRotateAll ? orientation : targetRotation, centerOfMass),
                    rotationVelocity = (originDistance - rotationDistance) / deltaTime;
                velocity += rotationVelocity;

                //UnityEngine.Debug.Log($"{rotationVelocity} : {characterSurface.normal} : {characterSurface.velocity} : {numDistanceHits} : {distanceHitsCollector.hits.Length}");

                stepInput.distanceHits = distanceHitsCollector.hits.AsNativeArray().Slice();
                if (isNeedToSortHits)
                    stepInput.distanceHits.Sort(new DistanceHitComparer());

                /*if (!isIndirectZero)
                    UnityEngine.Debug.Log($"Integrate {entityArray[index]} : {velocity}");*/

                isUnstopping |= (flag.value & GameNodeCharacterFlag.Flag.Unstoppable) == GameNodeCharacterFlag.Flag.Unstoppable;
                NativeQueue<DeferredCharacterControllerImpulse>.ParallelWriter deferredImpulseWriter = default;
                CollideAndIntegrate(
                    false,
                    isUnstopping,
                    //isUnstopping ? terrainMask : ~0u, 
                    instance.maxIterations,
                    input.Collider, 
                    stepInput,
                    originOrientation, 
                    ref transform.pos,
                    ref velocity,
                    //ref constraints,
                    ref deferredImpulseWriter);

                if (isJump || isDrop)
                {
                    // velocity = orginVelocity;
                }
                else
                {
                    float lengthsq = math.lengthsq(rotationVelocity);
                    if (lengthsq > math.FLT_MIN_NORMAL)
                    {
                        float invLength = math.rsqrt(lengthsq), dot = math.dot(velocity, rotationVelocity) * invLength;
                        velocity -= rotationVelocity * (math.clamp(dot, 0.0f, 1.0f / invLength) * invLength);
                    }
                }

                {
                    float lengthsq = math.lengthsq(drag.value);
                    if (lengthsq > math.FLT_MIN_NORMAL)
                    {
                        float invLength = math.rsqrt(lengthsq), dot = math.dot(velocity, drag.value) * invLength;
                        velocity -= drag.value * (math.clamp(dot, 0.0f, 1.0f / invLength / deltaTime) * invLength);
                    }
                }

                if (isStep)
                {
                    //velocity -= Math.ProjectSafe(directVelocity, velocity);

                    if (!isJump)
                        velocity -= Math.ProjectSafe(velocity, gravity);
                }

                /*if (math.dot(velocity, inveseGravityComponent) < 0.0f && (isDrop || math.dot(inveseGravityComponent, stepInput.Up) < 0.0f))
                {
                    inveseGravityComponent -= Math.ProjectSafe(velocity, inveseGravityComponent);
                    velocity += inveseGravityComponent;
                }*/

                velocity += gravity;

                /*{
                    if (math.lengthsq(transform.pos - temp) > 10.0f)
                    {
                        UnityEngine.Debug.Log("fuck!!!!!!!!!!!");

                        transform = rigidbody.WorldFromBody;
                    }
                    else
                    {
                        var motionData = physicsWorld.MotionDatas[rigidbodyIndex];
                        raycastInput.Start = motionData.WorldFromMotion.pos;
                        raycastInput.End = math.mul(transform, motionData.BodyFromMotion).pos;
                        raycastInput.Filter.CollidesWith = terrainMask;
                        if (physicsWorld.CastRay(raycastInput))
                        {
                            UnityEngine.Debug.Log("fuck:" + distanceHitsCollector.NumHits);

                            //transform = rigidbody.WorldFromBody;

                            //transform.pos -= velocity * deltaTime;
                        }
                    }
                }*/

                /*var centerOfMass = input.Collider->MassProperties.MassDistribution.Transform.pos;
                var worldFromMotion = math.transform(math.RigidTransform(origin, transform.pos), centerOfMass);
                var target = (instance.flag & GameNodeCharacterData.Flag.RotationAll) == GameNodeCharacterData.Flag.RotationAll ? orientation : targetRotation;
                transform.pos = worldFromMotion - math.mul(target, centerOfMass);*/

                transform.pos = math.transform(math.RigidTransform(originOrientation, transform.pos), centerOfMass) - rotationDistance;/*RotatePosition(centerOfMass,
                    isRotateAll ? orientation : targetRotation,
                    math.RigidTransform(originOrientation, transform.pos));*/
                transform.rot = orientation;

#if GAME_DEBUG_COMPARSION
                stream.Assert(newPositionName, transform.pos);
                stream.Assert(newRotationName, transform.rot);
                stream.Assert(newVelocityName, velocity);
                stream.End();
                //UnityEngine.Debug.Log(rigidbody.Entity.ToString() + transform.pos + frameIndex);
#endif
                //if (rigidbody.Collider.Value.Type == ColliderType.Box)
                /*if (entityIndices[index].value == 30 || entityIndices[index].value == 6 || entityIndices[index].value == 12998)
                {
                    string log = rigidbody.Entity.ToString() + isAngleChanged + oldStatus + distanceHitsCollector.NumHits + ":";
                    for(int i = 0; i < rigidbodies.Length; ++i)
                    {
                        var target = rigidbodies[i];
                        if (target.CustomTags == 255 && entityIndexMap.HasComponent(target.Entity))
                        {
                            log += $"---{target.Entity} : {entityIndexMap[target.Entity].value} : {target.WorldFromBody}---";
                        }
                    }

                    for (int i = 0; i < distanceHitsCollector.NumHits; ++i)
                    {
                        var target = collisionWorld.Bodies[distanceHitsCollector.hits[i].RigidBodyIndex].Entity;
                        if (entityIndexMap.HasComponent(target))
                            log += entityIndexMap[target].value + "---";

                        log += target;
                        log += distanceHitsCollector.hits[i].Distance;
                        log += distanceHitsCollector.hits[i].Position;
                        log += distanceHitsCollector.hits[i].SurfaceNormal;
                        log += collisionWorld.Bodies[distanceHitsCollector.hits[i].RigidBodyIndex].WorldFromBody;
                        //log += (physicsWorld.Bodies[distanceHitsCollector.hits[i].RigidBodyIndex].Collider->Filter.BelongsTo & terrainMask) != 0;
                    }

                    //log += source;
                    log += physicsWorld.motionDatas[rigidbodyIndex].BodyFromMotion;
                    //log += rigidbody.Collider->Type;
                    log += input.Collider->Type;
                    log += input.Collider->CalculateAabb().Max;
                    log += input.Collider->CalculateAabb().Min;
                    log += transform;
                    //log += info.distance;
                    log += velocity;
                    log += characterSurface.normal;
                    log += centerOfMass;
                    //log += eee;
                    //log += temp;
                    /*bool sourceE = ApplyTransforms.source.Equals(source),
                        destinationE = ApplyTransforms.destination.Equals(transform),
                        distanceE = ApplyTransforms.desiredVelocity.Equals(info.distance);

                    log += sourceE;
                    log += destinationE;
                    log += distanceE;
                    log += ApplyTransforms.velocity.Equals(velocity);
                    log += ApplyTransforms.normal.Equals(characterSurface.normal);
                    log += ApplyTransforms.destination.pos.Equals(transform.pos);
                    log += ApplyTransforms.destination.rot.Equals(transform.rot);
                    if (sourceE && distanceE && !destinationE)
                        UnityEngine.Debug.LogError(log);
                    else
                        UnityEngine.Debug.Log(log);


                    ApplyTransforms.source = source;
                    ApplyTransforms.destination = transform;
                    ApplyTransforms.desiredVelocity = info.distance;
                    ApplyTransforms.velocity = velocity;
                    ApplyTransforms.normal = characterSurface.normal;/
                    UnityEngine.Debug.Log(log + ":" + entityIndices[index].value + ":" + frameIndex);
                }*/

                Translation translation;
                translation.Value = transform.pos;
                translations[index] = translation;

                Rotation rotation;
                rotation.Value = transform.rot;
                rotations[index] = rotation;

                GameNodeCharacterVelocity characterVelocity;
                characterVelocity.value = velocity;
                characterVelocities[index] = characterVelocity;
            }
            else if (isDrop)
                velocity += gravity;

            PhysicsVelocity physicsVelocity;
            physicsVelocity.Linear = velocity;
            physicsVelocity.Angular = math.rotate(math.inverse(motionDatas[rigidbodyIndex].WorldFromMotion.rot), angular);
            physicsVelocities[index] = physicsVelocity;

            characterSurfaces[index] = characterSurface;

            if (value != oldStatus.value || area != oldStatus.area)
            {
                GameNodeCharacterStatus status;
                status.value = value;
                status.area = area;
                characterStates[index] = status;

                //UnityEngine.Debug.Log($"{entityArray[index]} : Status {status} : Old Status {oldStatus}");
            }

            if (flag.value != 0)
            {
                flag.value = 0;
                flags[index] = flag;
            }
        }
    }

    [BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Deterministic)]
    private struct ApplyTransformsEx : IJobChunk
    {
        //public int queryHitShilft;

        public uint dynamicMask;

        public uint terrainMask;

        public uint waterMask;

        public float depthOfWater;
        
        public GameTime deltaTime;

        public float3 gravity;

        [ReadOnly]
        public PhysicsWorldContainer physicsWorld;

        [ReadOnly]
        public ComponentLookup<GameWayData> ways;

        [ReadOnly]
        public BufferLookup<GameWayPoint> wayPoints;

        [ReadOnly]
        public EntityTypeHandle entityType;

        [ReadOnly]
        public ComponentTypeHandle<GameNodeDirect> directType;

        [ReadOnly]
        public ComponentTypeHandle<GameNodeIndirect> indirectType;

        [ReadOnly]
        public ComponentTypeHandle<GameNodeDrag> dragType;

        [ReadOnly]
        public ComponentTypeHandle<GameNodeDirection> directionType;

        [ReadOnly]
        public ComponentTypeHandle<GameNodeCharacterData> instanceType;

        [ReadOnly]
        public ComponentTypeHandle<GameNodeCharacterCollider> colliderType;

        [ReadOnly]
        public ComponentTypeHandle<GameNodeCharacterCenterOfMass> centerOfMassType;

        public ComponentTypeHandle<GameNodeCharacterFlag> flagType;

        public ComponentTypeHandle<GameNodeCharacterStatus> characterStatusType;

        public ComponentTypeHandle<GameNodeCharacterVelocity> characterVelocityType;

        public ComponentTypeHandle<GameNodeCharacterDesiredVelocity> characterDesiredVelocityType;

        public ComponentTypeHandle<GameNodeCharacterAngle> characterAngleType;

        public ComponentTypeHandle<GameNodeCharacterSurface> characterSurfaceType;

        public ComponentTypeHandle<GameNodeSurface> surfaceType;

        public ComponentTypeHandle<GameNodeAngle> angleType;
        
        public ComponentTypeHandle<Rotation> rotationType;

        public ComponentTypeHandle<Translation> translationType;

        public ComponentTypeHandle<PhysicsVelocity> physicsVelocityType;

        public BufferTypeHandle<GameNodeCharacterDistanceHit> distanceHitType;

        /*[DeallocateOnJobCompletion]
        public NativeArray<DistanceHit> distanceHits;*/

        /*[DeallocateOnJobCompletion]
        public NativeArray<ColliderCastHit> castHits;

        [DeallocateOnJobCompletion]
        public NativeArray<SurfaceConstraintInfo> constraints;*/

#if GAME_DEBUG_COMPARSION
        public uint frameIndex;

        public FixedString32Bytes hitsName;
        public FixedString32Bytes angleName;
        public FixedString32Bytes characterAngleName;
        public FixedString32Bytes directName;
        public FixedString32Bytes dragName;
        public FixedString32Bytes distanceName;
        public FixedString32Bytes surfaceRotationName;
        public FixedString32Bytes velocityName;
        public FixedString32Bytes oldVelocityName;
        public FixedString32Bytes oldStatusName;
        public FixedString32Bytes oldNormalName;
        public FixedString32Bytes oldPositionName;
        public FixedString32Bytes oldRotationName;
        public FixedString32Bytes newRotationName;
        public FixedString32Bytes newPositionName;
        public FixedString32Bytes newNormalName;
        public FixedString32Bytes newVelocityName;

        public ComparisonStream<uint> stream;
        [ReadOnly]
        public ComponentTypeHandle<GameEntityIndex> entityIndexType;
        [ReadOnly]
        public ComponentLookup<GameEntityIndex> entityIndices;
#endif
        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            ApplyTransforms applyTransforms;
            applyTransforms.dynamicMask = dynamicMask;
            applyTransforms.terrainMask = terrainMask;
            applyTransforms.waterMask = waterMask;
            applyTransforms.depthOfWater = depthOfWater;
            applyTransforms.deltaTime = deltaTime;
            applyTransforms.gravity = gravity;
            applyTransforms.physicsWorld = physicsWorld;
            applyTransforms.ways = ways;
            applyTransforms.wayPoints = wayPoints;
            applyTransforms.entityArray = chunk.GetNativeArray(entityType);
            applyTransforms.directs = chunk.GetNativeArray(ref directType);
            applyTransforms.indirects = chunk.GetNativeArray(ref indirectType);
            applyTransforms.directions = chunk.GetNativeArray(ref directionType);
            applyTransforms.drags = chunk.GetNativeArray(ref dragType);
            applyTransforms.instances = chunk.GetNativeArray(ref  instanceType);
            applyTransforms.colliders = chunk.GetNativeArray(ref colliderType);
            applyTransforms.centerOfMasses = chunk.GetNativeArray(ref centerOfMassType);
            applyTransforms.flags = chunk.GetNativeArray(ref flagType);
            applyTransforms.characterStates = chunk.GetNativeArray(ref characterStatusType);
            applyTransforms.characterVelocities = chunk.GetNativeArray(ref characterVelocityType);
            applyTransforms.characterDesiredVelocities = chunk.GetNativeArray(ref characterDesiredVelocityType);
            applyTransforms.characterAngles = chunk.GetNativeArray(ref characterAngleType);
            applyTransforms.characterSurfaces = chunk.GetNativeArray(ref characterSurfaceType);
            applyTransforms.surfaces = chunk.GetNativeArray(ref surfaceType);
            applyTransforms.angles = chunk.GetNativeArray(ref angleType);
            applyTransforms.translations = chunk.GetNativeArray(ref translationType);
            applyTransforms.rotations = chunk.GetNativeArray(ref rotationType);
            applyTransforms.physicsVelocities = chunk.GetNativeArray(ref physicsVelocityType);

            applyTransforms.distanceHits = chunk.GetBufferAccessor(ref distanceHitType);

            //int startIndex = chunkIndex << queryHitShilft, length = 1 << queryHitShilft;
            //applyTransforms.castHits = castHits.Slice(startIndex, length);
            //applyTransforms.constraints = constraints.Slice(startIndex << 2, length << 2);

#if GAME_DEBUG_COMPARSION
            applyTransforms.frameIndex = frameIndex;
            applyTransforms.hitsName = hitsName;
            applyTransforms.angleName = angleName;
            applyTransforms.characterAngleName = characterAngleName;
            applyTransforms.directName = directName;
            applyTransforms.dragName = dragName;
            applyTransforms.distanceName = distanceName;
            applyTransforms.surfaceRotationName = surfaceRotationName;
            applyTransforms.velocityName = velocityName;
            applyTransforms.oldVelocityName = oldVelocityName;
            applyTransforms.oldStatusName = oldStatusName;
            applyTransforms.oldNormalName = oldNormalName;
            applyTransforms.oldPositionName = oldPositionName;
            applyTransforms.oldRotationName = oldRotationName;
            applyTransforms.newRotationName = newRotationName;
            applyTransforms.newPositionName = newPositionName;
            applyTransforms.newNormalName = newNormalName;
            applyTransforms.newVelocityName = newVelocityName;

            applyTransforms.stream = stream;
            applyTransforms.entityIndices = chunk.GetNativeArray(ref entityIndexType);
            applyTransforms.entityIndexMap = entityIndices;
#endif

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                applyTransforms.Execute(i);
        }
    }

    /*private EntityQuery __syncGroup;
    private EntityQuery __updateGroup;*/
    private EntityQuery __commonGroup;
    private EntityQuery __physicsStepGroup;
    private EntityQuery __distanceHitGroup;
    private EntityQuery __transformGroup;

    private BufferTypeHandle<GameNodeCharacterDistanceHit> __distanceHitType;
    private ComponentLookup<GameWayData> __ways;
    private BufferLookup<GameWayPoint> __wayPoints;
    private EntityTypeHandle __entityType;
    private ComponentTypeHandle<GameNodeDirect> __directType;
    private ComponentTypeHandle<GameNodeIndirect> __indirectType;
    private ComponentTypeHandle<GameNodeDirection> __directionType;
    private ComponentTypeHandle<GameNodeDrag> __dragType;
    private ComponentTypeHandle<GameNodeCharacterData> __instanceType;
    private ComponentTypeHandle<GameNodeCharacterCollider> __colliderType;
    private ComponentTypeHandle<GameNodeCharacterCenterOfMass> __centerOfMassType;
    private ComponentTypeHandle<GameNodeCharacterFlag> __flagType;
    private ComponentTypeHandle<GameNodeCharacterStatus> __characterStatusType;
    private ComponentTypeHandle<GameNodeCharacterVelocity> __characterVelocityType;
    private ComponentTypeHandle<GameNodeCharacterDesiredVelocity> __characterDesiredVelocityType;
    private ComponentTypeHandle<GameNodeCharacterAngle> __characterAngleType;
    private ComponentTypeHandle<GameNodeCharacterSurface> __characterSurfaceType;
    private ComponentTypeHandle<GameNodeSurface> __surfaceType;
    private ComponentTypeHandle<GameNodeAngle> __angleType;
    private ComponentTypeHandle<Translation> __translationType;
    private ComponentTypeHandle<Rotation> __rotationType;
    private ComponentTypeHandle<PhysicsVelocity> __physicsVelocityType;

    private GameUpdateTime __time;

    private SharedPhysicsWorld __physicsWorld;

    public void OnCreate(ref SystemState state)
    {
        /*__syncGroup = state.GetEntityQuery(ComponentType.ReadOnly<GameSyncData>());
        __updateGroup = state.GetEntityQuery(ComponentType.ReadOnly<GameUpdateData>());*/
        __commonGroup = state.GetEntityQuery(ComponentType.ReadOnly<GameNodeCharacterCommon>());
        __physicsStepGroup = state.GetEntityQuery(ComponentType.ReadOnly<Unity.Physics.PhysicsStep>());
        __distanceHitGroup = state.GetEntityQuery( 
            new EntityQueryDesc()
            {
                All = new ComponentType[]
                {
                    ComponentType.ReadOnly<GameNodeCharacterDistanceHit>()
                },
                Options = EntityQueryOptions.IncludeDisabledEntities
            });

        __transformGroup = state.GetEntityQuery(
            ComponentType.ReadOnly<GameNodeDirect>(),
            ComponentType.ReadOnly<GameNodeIndirect>(),
            ComponentType.ReadOnly<GameNodeCharacterData>(),
            ComponentType.ReadWrite<GameNodeCharacterStatus>(),
            ComponentType.ReadWrite<GameNodeCharacterAngle>(),
            ComponentType.ReadWrite<GameNodeCharacterSurface>(),
            ComponentType.ReadWrite<GameNodeAngle>(),
            ComponentType.ReadWrite<GameNodeCharacterDistanceHit>(), 
            ComponentType.Exclude<GameNodeParent>(),
            ComponentType.Exclude<Disabled>());

        __time = new GameUpdateTime(ref state);

        __physicsWorld = state.World.GetOrCreateSystemUnmanaged<GamePhysicsWorldBuildSystem>().physicsWorld;

        __distanceHitType = state.GetBufferTypeHandle<GameNodeCharacterDistanceHit>();
        __ways = state.GetComponentLookup<GameWayData>(true);
        __wayPoints = state.GetBufferLookup<GameWayPoint>(true);
        __entityType = state.GetEntityTypeHandle();
        __directType = state.GetComponentTypeHandle<GameNodeDirect>(true);
        __indirectType = state.GetComponentTypeHandle<GameNodeIndirect>(true);
        __directionType = state.GetComponentTypeHandle<GameNodeDirection>(true);
        __dragType = state.GetComponentTypeHandle<GameNodeDrag>(true);
        __instanceType = state.GetComponentTypeHandle<GameNodeCharacterData>(true);
        __colliderType = state.GetComponentTypeHandle<GameNodeCharacterCollider>(true);
        __centerOfMassType = state.GetComponentTypeHandle<GameNodeCharacterCenterOfMass>(true);
        __flagType = state.GetComponentTypeHandle<GameNodeCharacterFlag>();
        __characterStatusType = state.GetComponentTypeHandle<GameNodeCharacterStatus>();
        __characterVelocityType = state.GetComponentTypeHandle<GameNodeCharacterVelocity>();
        __characterDesiredVelocityType = state.GetComponentTypeHandle<GameNodeCharacterDesiredVelocity>();
        __characterAngleType = state.GetComponentTypeHandle<GameNodeCharacterAngle>();
        __characterSurfaceType = state.GetComponentTypeHandle<GameNodeCharacterSurface>();
        __surfaceType = state.GetComponentTypeHandle<GameNodeSurface>();
        __angleType = state.GetComponentTypeHandle<GameNodeAngle>();
        __translationType = state.GetComponentTypeHandle<Translation>();
        __rotationType = state.GetComponentTypeHandle<Rotation>();
        __physicsVelocityType = state.GetComponentTypeHandle<PhysicsVelocity>();
    }

    public void OnDestroy(ref SystemState state)
    {

    }

#if !GAME_DEBUG_COMPARSION
    [BurstCompile]
#endif
    public void OnUpdate(ref SystemState state)
    {
        var distanceHitType = __distanceHitType.UpdateAsRef(ref state);

        var jobHandle = state.Dependency;
        JobHandle? result = null;

        if (!__distanceHitGroup.IsEmptyIgnoreFilter)
        {
            ClearDistanceHits clearDistanceHits;
            clearDistanceHits.distanceHitType = distanceHitType;
            jobHandle = clearDistanceHits.ScheduleParallel(__distanceHitGroup, jobHandle);

            result = jobHandle;
        }

        if (!__transformGroup.IsEmptyIgnoreFilter)
        {
            var common = __commonGroup.IsEmptyIgnoreFilter ? GameNodeCharacterCommon.Default : __commonGroup.GetSingleton<GameNodeCharacterCommon>();
            /*var syncData = __syncGroup.GetSingleton<GameSyncData>();
            var updateData = __updateGroup.GetSingleton<GameUpdateData>();*/

            ApplyTransformsEx applyTransforms;
            //applyTransforms.queryHitShilft = queryHitShilft;
            applyTransforms.dynamicMask = common.dynamicMask;
            applyTransforms.terrainMask = common.terrainMask;
            applyTransforms.waterMask = common.waterMask;
            applyTransforms.depthOfWater = common.depthOfWater;
            applyTransforms.deltaTime = __time.delta;// updateData.GetDelta(syncData.delta);
            applyTransforms.gravity = __physicsStepGroup.IsEmptyIgnoreFilter ? Unity.Physics.PhysicsStep.Default.Gravity : __physicsStepGroup.GetSingleton<Unity.Physics.PhysicsStep>().Gravity;
            applyTransforms.physicsWorld = __physicsWorld.container;
            applyTransforms.ways = __ways.UpdateAsRef(ref state);
            applyTransforms.wayPoints = __wayPoints.UpdateAsRef(ref state);
            applyTransforms.entityType = __entityType.UpdateAsRef(ref state);
            applyTransforms.directType = __directType.UpdateAsRef(ref state);
            applyTransforms.indirectType = __indirectType.UpdateAsRef(ref state);
            applyTransforms.directionType = __directionType.UpdateAsRef(ref state);
            applyTransforms.dragType = __dragType.UpdateAsRef(ref state);
            applyTransforms.instanceType = __instanceType.UpdateAsRef(ref state);
            applyTransforms.colliderType = __colliderType.UpdateAsRef(ref state);
            applyTransforms.centerOfMassType = __centerOfMassType.UpdateAsRef(ref state);
            applyTransforms.flagType = __flagType.UpdateAsRef(ref state);
            applyTransforms.characterStatusType = __characterStatusType.UpdateAsRef(ref state);
            applyTransforms.characterVelocityType = __characterVelocityType.UpdateAsRef(ref state);
            applyTransforms.characterDesiredVelocityType = __characterDesiredVelocityType.UpdateAsRef(ref state);
            applyTransforms.characterAngleType = __characterAngleType.UpdateAsRef(ref state);
            applyTransforms.characterSurfaceType = __characterSurfaceType.UpdateAsRef(ref state);
            applyTransforms.surfaceType = __surfaceType.UpdateAsRef(ref state);
            applyTransforms.angleType = __angleType.UpdateAsRef(ref state);
            applyTransforms.translationType = __translationType.UpdateAsRef(ref state);
            applyTransforms.rotationType = __rotationType.UpdateAsRef(ref state);
            applyTransforms.physicsVelocityType = __physicsVelocityType.UpdateAsRef(ref state);

            applyTransforms.distanceHitType = distanceHitType;

            /*int count = __groupToApply.CalculateChunkCount();
            //applyTransforms.distanceHits = new NativeArray<DistanceHit>(count << queryHitShilft, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            applyTransforms.castHits = new NativeArray<ColliderCastHit>(count << queryHitShilft, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            applyTransforms.constraints = new NativeArray<SurfaceConstraintInfo>(count << (queryHitShilft + 2), Allocator.TempJob, NativeArrayOptions.UninitializedMemory);*/

#if GAME_DEBUG_COMPARSION
            uint frameIndex = __time.RollbackTime.frameIndex;
            applyTransforms.frameIndex = frameIndex;
            applyTransforms.hitsName = "hits";
            applyTransforms.angleName = "angle";
            applyTransforms.characterAngleName = "characterAngle";
            applyTransforms.directName = "direct";
            applyTransforms.dragName = "drag";
            applyTransforms.distanceName = "distance";
            applyTransforms.surfaceRotationName = "surfaceRotation";
            applyTransforms.velocityName = "velocity";
            applyTransforms.oldVelocityName = "oldVelocity";
            applyTransforms.oldStatusName = "oldStatus";
            applyTransforms.oldNormalName = "oldNormal";
            applyTransforms.oldPositionName = "oldPosition";
            applyTransforms.oldRotationName = "oldRotation";
            applyTransforms.newRotationName = "newRotation";
            applyTransforms.newPositionName = "newPosition";
            applyTransforms.newNormalName = "newNormal";
            applyTransforms.newVelocityName = "newVelocity";

            var streamScheduler = GameComparsionSystem.instance.Create(SystemAPI.GetSingleton<FrameSyncFlag>().isClear, frameIndex, typeof(GameNodeCharacterSystem).Name, state.World.Name);
            applyTransforms.stream = streamScheduler.Begin(__transformGroup.CalculateEntityCount());
            applyTransforms.entityIndexType = state.GetComponentTypeHandle<GameEntityIndex>(true);
            applyTransforms.entityIndices = state.GetComponentLookup<GameEntityIndex>(true);
#endif

            ref var lookupJobManager = ref __physicsWorld.lookupJobManager;

            jobHandle = applyTransforms.ScheduleParallel(__transformGroup, JobHandle.CombineDependencies(jobHandle, lookupJobManager.readOnlyJobHandle));

#if GAME_DEBUG_COMPARSION
        streamScheduler.End(jobHandle);
#endif

            lookupJobManager.AddReadOnlyDependency(jobHandle);

            result = jobHandle;
        }

        if(result != null)
            state.Dependency = result.Value;
    }
}

/*[UpdateInGroup(typeof(GameNodeCharacterSystemGroup))]
public partial class GameNodeCharacterSystem : SystemBase
{
    [BurstCompile]
    private unsafe static class BurstUtility
    {
        public delegate void UpdateDelegate(GameNodeCharacterSystemCore* core, ref SystemState state);

#if GAME_DEBUG_COMPARSION
        public static readonly UpdateDelegate UpdateFunction = Update;
#else
        public static readonly UpdateDelegate UpdateFunction = BurstCompiler.CompileFunctionPointer<UpdateDelegate>(Update).Invoke;

        [BurstCompile]
#endif
        public static void Update(GameNodeCharacterSystemCore* core, ref SystemState state)
        {
            core->OnUpdate(ref state);
        }
    }

    private GameNodeCharacterSystemCore __core;

    protected override void OnCreate()
    {
        base.OnCreate();

        __core.OnCreate(ref this.GetState());
    }

    protected override void OnDestroy()
    {
        __core.OnDestroy(ref this.GetState());

        base.OnDestroy();
    }

    protected override unsafe void OnUpdate()
    {
        BurstUtility.UpdateFunction(
            (GameNodeCharacterSystemCore*)Unity.Collections.LowLevel.Unsafe.UnsafeUtility.AddressOf(ref __core),
            ref this.GetState());
    }
}*/

[BurstCompile, UpdateInGroup(typeof(GameUpdateSystemGroup)), UpdateBefore(typeof(GameNodeCharacterSystemGroup))]
public partial struct GameNodeCharacterColliderSystem : ISystem
{
    private struct Calculate
    {
        [ReadOnly]
        public NativeArray<GameNodeCharacterCollider> colliders;

        public NativeArray<GameNodeCharacterCenterOfMass> centerOfMasses;

        public void Execute(int index)
        {
            var collider = colliders[index].value;
            GameNodeCharacterCenterOfMass centerOfMass;
            centerOfMass.value = collider.IsCreated ? collider.Value.MassProperties.MassDistribution.Transform.pos : float3.zero;
            centerOfMasses[index] = centerOfMass;
        }
    }

    [BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Deterministic)]
    private struct CalculateEx : IJobChunk
    {
        [ReadOnly]
        public ComponentTypeHandle<GameNodeCharacterCollider> colliderType;

        public ComponentTypeHandle<GameNodeCharacterCenterOfMass> centerOfMassType;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Calculate calculate;
            calculate.colliders = chunk.GetNativeArray(ref colliderType);
            calculate.centerOfMasses = chunk.GetNativeArray(ref centerOfMassType);

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                calculate.Execute(i);
        }
    }

    private EntityQuery __group;
    private ComponentTypeHandle<GameNodeCharacterCollider> __colliderType;
    private ComponentTypeHandle<GameNodeCharacterCenterOfMass> __centerOfMassType;

    public void OnCreate(ref SystemState state)
    {
        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __group = builder
                .WithAll<GameNodeCharacterCollider>()
                .WithAllRW<GameNodeCharacterCenterOfMass>()
                .WithOptions(EntityQueryOptions.IncludeDisabledEntities)
                .Build(ref state);
        __group.SetChangedVersionFilter(ComponentType.ReadOnly<GameNodeCharacterCollider>());
        
        __colliderType = state.GetComponentTypeHandle<GameNodeCharacterCollider>(true);
        __centerOfMassType = state.GetComponentTypeHandle<GameNodeCharacterCenterOfMass>();
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {

    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        CalculateEx calculate;
        calculate.colliderType = __colliderType.UpdateAsRef(ref state);
        calculate.centerOfMassType = __centerOfMassType.UpdateAsRef(ref state);
        state.Dependency = calculate.ScheduleParallelByRef(__group, state.Dependency);
    }
}

[BurstCompile, 
    UpdateInGroup(typeof(GameStatusSystemGroup), OrderFirst = true)/*, 
    UpdateBefore(typeof(GameNodeStatusSystem)), 
    UpdateAfter(typeof(GameNodeAngleSystem))*/]
public partial struct GameNodeCharacterRotationSystem : ISystem
{
    private struct BuildRotations
    {
        [ReadOnly]
        public NativeArray<GameNodeCharacterData> instances;
        [ReadOnly]
        public NativeArray<GameNodeCharacterAngle> angles;
        [ReadOnly]
        public NativeArray<GameNodeSurface> surfaces;
        [ReadOnly]
        public NativeArray<Translation> translations;

        public NativeArray<Rotation> rotations;

        public NativeArray<LocalToWorld> localToWorlds;

        public void Execute(int index)
        {
            quaternion rotationY = quaternion.RotateY(angles[index].value);

            Rotation rotation;
            rotation.Value = (instances[index].flag & GameNodeCharacterData.Flag.SurfaceUp) == GameNodeCharacterData.Flag.SurfaceUp ? 
                math.mul(surfaces[index].rotation, rotationY) :
                rotationY;
            
            rotations[index] = rotation;

            if (index < localToWorlds.Length)
            {
                LocalToWorld localToWorld;
                localToWorld.Value = float4x4.TRS(translations[index].Value, rotation.Value, 1.0f);
                localToWorlds[index] = localToWorld;
            }
        }
    }

    [BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Deterministic)]
    private struct BuildRotationsEx : IJobChunk
    {
        [ReadOnly]
        public ComponentTypeHandle<GameNodeCharacterData> instanceType;
        [ReadOnly]
        public ComponentTypeHandle<GameNodeCharacterAngle> angleType;
        [ReadOnly]
        public ComponentTypeHandle<GameNodeSurface> surfaceType;
        [ReadOnly]
        public ComponentTypeHandle<Translation> translationType;

        public ComponentTypeHandle<Rotation> rotationType;

        public ComponentTypeHandle<LocalToWorld> localToWorldType;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            BuildRotations buildRotations;
            buildRotations.instances = chunk.GetNativeArray(ref instanceType);
            buildRotations.angles = chunk.GetNativeArray(ref angleType);
            buildRotations.surfaces = chunk.GetNativeArray(ref surfaceType);
            buildRotations.translations = chunk.GetNativeArray(ref translationType);
            buildRotations.rotations = chunk.GetNativeArray(ref rotationType);
            buildRotations.localToWorlds = chunk.GetNativeArray(ref localToWorldType);

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                buildRotations.Execute(i);
        }
    }

    private EntityQuery __group;

    private ComponentTypeHandle<GameNodeCharacterData> __instanceType;
    private ComponentTypeHandle<GameNodeCharacterAngle> __angleType;
    private ComponentTypeHandle<GameNodeSurface> __surfaceType;
    private ComponentTypeHandle<Translation> __translationType;

    private ComponentTypeHandle<Rotation> __rotationType;

    private ComponentTypeHandle<LocalToWorld> __localToWorldType;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __group = builder
                    .WithAll<GameNodeCharacterRotationDirty, GameNodeCharacterData, GameNodeCharacterAngle, GameNodeSurface>()
                    .WithAllRW<Rotation>()
                    .WithOptions(EntityQueryOptions.IncludeDisabledEntities)
                    .Build(ref state);

        __instanceType = state.GetComponentTypeHandle<GameNodeCharacterData>(true);
        __angleType = state.GetComponentTypeHandle<GameNodeCharacterAngle>(true);
        __surfaceType = state.GetComponentTypeHandle<GameNodeSurface>(true);
        __translationType = state.GetComponentTypeHandle<Translation>(true);
        __rotationType = state.GetComponentTypeHandle<Rotation>();
        __localToWorldType = state.GetComponentTypeHandle<LocalToWorld>();
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        BuildRotationsEx buildRotations;
        buildRotations.instanceType = __instanceType.UpdateAsRef(ref state);
        buildRotations.angleType = __angleType.UpdateAsRef(ref state);
        buildRotations.surfaceType = __surfaceType.UpdateAsRef(ref state);
        buildRotations.translationType = __translationType.UpdateAsRef(ref state);
        buildRotations.rotationType = __rotationType.UpdateAsRef(ref state);
        buildRotations.localToWorldType = __localToWorldType.UpdateAsRef(ref state);

        state.Dependency = buildRotations.ScheduleParallel(__group, state.Dependency);
    }
}


[BurstCompile, UpdateInGroup(typeof(GameRollbackSystemGroup), OrderLast = true)]
public partial struct GameNodeCharacterClearSystem : ISystem
{
    private EntityQuery __group;

    public void OnCreate(ref SystemState state)
    {
        __group = state.GetEntityQuery(
            new EntityQueryDesc()
            {
                All = new ComponentType[]
                {
                    ComponentType.ReadOnly<GameNodeCharacterRotationDirty>()
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
        state.EntityManager.RemoveComponent<GameNodeCharacterRotationDirty>(__group);
    }
}

/*#if GAME_DEBUG_COMPARSION
[UpdateInGroup(typeof(GameSyncSystemGroup)), UpdateAfter(typeof(GameNodeCharacterRotationSystem)), UpdateBefore(typeof(GameNodeStatusSystem))]
public partial class GameNodeCharacterRotationComparsionSystem : JobComponentSystem
{
    private struct Comparsion
    {
        public FixedString32Bytes resultName;

        public ComparisonStream<int> stream;
        [ReadOnly]
        public NativeArray<GameEntityIndex> entityIndices;
        [ReadOnly]
        public NativeArray<Rotation> rotations;

        public void Execute(int index)
        {
            stream.Begin(entityIndices[index].value);
            stream.Assert(resultName, rotations[index].Value);
            stream.End();
        }
    }

    [BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Deterministic)]
    private struct ComparsionEx : IJobChunk
    {
        public FixedString32Bytes resultName;

        public ComparisonStream<int> stream;
        [ReadOnly]
        public ArchetypeChunkComponentType<GameEntityIndex> entityIndexType;
        [ReadOnly]
        public ArchetypeChunkComponentType<Rotation> rotationType;

        public void Execute(ArchetypeChunk chunk, int chunkIndex, int firstEntityIndex)
        {
            Comparsion comparsion;
            comparsion.resultName = resultName;
            comparsion.stream = stream;
            comparsion.entityIndices = chunk.GetNativeArray(entityIndexType);
            comparsion.rotations = chunk.GetNativeArray(rotationType);

            int count = chunk.Count;
            for (int i = 0; i < count; ++i)
                comparsion.Execute(i);
        }
    }

    private EntityQuery __group;
    private GameSyncSystemGroup __syncSystemGroup;

    protected override void OnCreate()
    {
        base.OnCreate();

        __group = GetEntityQuery(
            ComponentType.ReadOnly<GameEntityIndex>(),
            ComponentType.ReadOnly<Rotation>());

        __syncSystemGroup = World.GetOrCreateSystem<GameSyncSystemGroup>();
    }

    protected override JobHandle OnUpdate(JobHandle inputDeps)
    {
        ComparsionEx comparsion;
        comparsion.resultName = "result";

        var streamScheduler = GameComparsionSystem.instance.Create(false, __syncSystemGroup.frameIndex, this);
        comparsion.stream = streamScheduler.Begin(__group.CalculateEntityCount());
        comparsion.entityIndexType = GetArchetypeChunkComponentType<GameEntityIndex>(true);
        comparsion.rotationType = GetArchetypeChunkComponentType<Rotation>(true);
        inputDeps = comparsion.Schedule(__group, inputDeps);

        streamScheduler.End(inputDeps);
        
        return inputDeps;
    }
}

[UpdateInGroup(typeof(GameUpdateSystemGroup)), UpdateAfter(typeof(ExportPhysicsWorld)), UpdateBefore(typeof(GameNodeCharacterSystem))]
public class GameNodeCharacterRotationComparsionSystem2 : JobComponentSystem
{
    private struct Comparsion
    {
        public FixedString32Bytes resultName;

        public ComparisonStream<int> stream;
        [ReadOnly]
        public NativeArray<GameEntityIndex> entityIndices;
        [ReadOnly]
        public NativeArray<Rotation> rotations;

        public void Execute(int index)
        {
            stream.Begin(entityIndices[index].value);
            stream.Assert(resultName, rotations[index].Value);
            stream.End();
        }
    }

    [BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Deterministic)]
    private struct ComparsionEx : IJobChunk
    {
        public FixedString32Bytes resultName;

        public ComparisonStream<int> stream;
        [ReadOnly]
        public ArchetypeChunkComponentType<GameEntityIndex> entityIndexType;
        [ReadOnly]
        public ArchetypeChunkComponentType<Rotation> rotationType;

        public void Execute(ArchetypeChunk chunk, int chunkIndex, int firstEntityIndex)
        {
            Comparsion comparsion;
            comparsion.resultName = resultName;
            comparsion.stream = stream;
            comparsion.entityIndices = chunk.GetNativeArray(entityIndexType);
            comparsion.rotations = chunk.GetNativeArray(rotationType);

            int count = chunk.Count;
            for (int i = 0; i < count; ++i)
                comparsion.Execute(i);
        }
    }

    private EntityQuery __group;
    private GameSyncSystemGroup __syncSystemGroup;
    private ExportPhysicsWorld __exportPhysicsWorld;

    protected override void OnCreate()
    {
        base.OnCreate();

        __group = GetEntityQuery(
            ComponentType.ReadOnly<GameEntityIndex>(),
            ComponentType.ReadOnly<Rotation>());

        __syncSystemGroup = World.GetOrCreateSystem<GameSyncSystemGroup>();
        __exportPhysicsWorld = World.GetOrCreateSystem<ExportPhysicsWorld>();
    }

    protected override JobHandle OnUpdate(JobHandle inputDeps)
    {
        ComparsionEx comparsion;
        comparsion.resultName = "result";

        var streamScheduler = GameComparsionSystem.instance.Create(false, __syncSystemGroup.frameIndex, this);
        comparsion.stream = streamScheduler.Begin(__group.CalculateEntityCount());
        comparsion.entityIndexType = GetArchetypeChunkComponentType<GameEntityIndex>(true);
        comparsion.rotationType = GetArchetypeChunkComponentType<Rotation>(true);
        inputDeps = comparsion.Schedule(__group, JobHandle.CombineDependencies(inputDeps, __exportPhysicsWorld.GetOutputDependency()));

        streamScheduler.End(inputDeps);

        return inputDeps;
    }
}
#endif*/