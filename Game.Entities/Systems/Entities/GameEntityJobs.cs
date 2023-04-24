using Unity.Jobs;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Transforms;
using Unity.Physics;
using Unity.Physics.Extensions;
using ZG;
using Math = ZG.Mathematics.Math;

public interface IGameEntityActionHandler
{
    bool Create(
        int index,
        double time, 
        in Entity entity,
        in GameActionData instance);

    bool Init(
        int index,
        float elapsedTime,
        double time,
        in Entity entity,
        in RigidTransform transform,
        in GameActionData instance);

    void Hit(
        int index,
        float elapsedTime,
        double time,
        in Entity entity,
        in Entity target,
        in RigidTransform transform,
        in GameActionData instance);

    void Damage(
        int index,
        int count,
        float elapsedTime,
        double time,
        in Entity entity,
        in Entity target,
        in float3 position,
        in float3 normal,
        in GameActionData instance);

    void Destroy(
        int index,
        float elapsedTime,
        double time,
        in Entity entity,
        in RigidTransform transform,
        in GameActionData instance);
}

public interface IGameEntityActionFactory<T> where T : struct, IGameEntityActionHandler
{
    T Create(in ArchetypeChunk chunk);
}

public struct GameEntityTransform
{
    public float elapsedTime;
    public RigidTransform value;

    public GameEntityTransform LerpTo(in GameEntityTransform transform, float fraction)
    {
        GameEntityTransform result;
        result.elapsedTime = math.lerp(elapsedTime, transform.elapsedTime, fraction);
        result.value = Math.Lerp(value, transform.value, fraction);

        return result;
    }
}

public struct GameEntityDistanceCollector<T> : ICollector<DistanceHit> where T : IGameEntityActionHandler
{
    //public float hitValue;

    private int __index;
    private int __camp;
    private GameActionTargetType __type;
    private float __interval;
    private float __value;
    private float __impactForce;
    private float __impactTime;
    private float __impactMaxSpeed;
    private float __elpasedTime;
    private double __time;
    private float3 __position;
    private float3 __direction;
    private Entity __entity;
    private GameActionData __instance;
    private NativeSlice<RigidBody> __rigidBodies;
    private DynamicBuffer<GameActionEntity> __actionEntities;
    private ComponentLookup<GameEntityCamp> __camps;
    private ComponentLookup<GameEntityActorMass> __masses;
    private NativeQueue<EntityData<GameNodeVelocityComponent>>.ParallelWriter __impacts;
    private T __handler;

    public bool EarlyOutOnFirstHit => false;

    public int NumHits { get; private set; }

    public float MaxFraction { get; }

    public GameEntityDistanceCollector(
        int index,
        int camp,
        GameActionTargetType type,
        float interval,
        float value,
        float impactForce,
        float impactTime,
        float impactMaxSpeed,
        float distance,
        float elpasedTime,
        double time,
        float3 position,
        float3 direction,
        Entity entity,
        GameActionData instance,
        NativeSlice<RigidBody> rigidBodies,
        DynamicBuffer<GameActionEntity> actionEntities,
        ComponentLookup<GameEntityCamp> camps,
        ComponentLookup<GameEntityActorMass> masses,
        NativeQueue<EntityData<GameNodeVelocityComponent>>.ParallelWriter impacts,
        T handler)
    {
        //hitValue = 0.0f;

        __index = index;
        __camp = camp;
        __type = type;
        __interval = interval;
        __value = value;
        __impactForce = impactForce;
        __impactTime = impactTime;
        __impactMaxSpeed = impactMaxSpeed;
        __elpasedTime = elpasedTime;
        __time = time;
        __position = position;
        __direction = direction;
        __entity = entity;
        __instance = instance;
        __rigidBodies = rigidBodies;
        __actionEntities = actionEntities;
        __camps = camps;
        __masses = masses;
        __impacts = impacts;
        __handler = handler;

        NumHits = 0;
        MaxFraction = distance;
    }

    public bool AddHit(DistanceHit hit)
    {
        RigidBody rigidbody = __rigidBodies[hit.RigidBodyIndex];
        if (!__camps.HasComponent(rigidbody.Entity))
            return false;

        GameEntityNode source, destination;
        source.camp = __camp;
        source.entity = __instance.entity;
        destination.camp = __camps[rigidbody.Entity].value;
        destination.entity = rigidbody.Entity;
        if (!source.Predicate(__type, destination))
            return false;

        int count = __actionEntities.Hit(rigidbody.Entity, __elpasedTime, __interval, __value);
        if (count < 1)
            return false;

        float3 normal = math.normalizesafe(hit.Position - __position, __direction);
        __handler.Damage(
            __index,
            count,
            __elpasedTime,
            __time,
            __entity,
            rigidbody.Entity,
            hit.Position,
            -normal,
            __instance);

        if (__impactForce > math.FLT_MIN_NORMAL && __masses.HasComponent(rigidbody.Entity))
        {
            EntityData<GameNodeVelocityComponent> impact;
            impact.value.mode = GameNodeVelocityComponent.Mode.Indirect;
            impact.value.duration = __impactTime;
            impact.value.time = __instance.time + __elpasedTime;
            impact.value.value = normal * math.min(__impactForce * __masses[rigidbody.Entity].inverseValue, __impactMaxSpeed > math.FLT_MIN_NORMAL ? __impactMaxSpeed : float.MaxValue);
            impact.entity = rigidbody.Entity;

            __impacts.Enqueue(impact);
        }

        ++NumHits;

        return true;
    }
}

public struct GameEntityCastCollector<TQueryResult, TQueryResultWrapper, THandler> : ICollector<TQueryResult>
    where TQueryResult : struct, IQueryResult
    where TQueryResultWrapper : struct, IQueryResultWrapper<TQueryResult> 
    where THandler : IGameEntityActionHandler
{
    private bool __isClosestHitOnly;
    private int __index;
    private int __camp;
    private GameActionTargetType __hitType;
    private GameActionTargetType __damageType;
    private float __dot;
    private float __distance;
    private float __interval;
    private float __value;
    private float __impactForce;
    private float __impactTime;
    private float __impactMaxSpeed;
    private double __time;
    private float3 __direction;
    private Entity __entity;
    private GameEntityTransform __start;
    private GameEntityTransform __end;
    private CollisionFilter __collisionFilter;
    private CollisionWorld __collisionWorld;
    private GameActionData __instance;
    private DynamicBuffer<GameActionEntity> __actionEntities;
    private ComponentLookup<GameEntityCamp> __camps;
    private ComponentLookup<GameEntityActorMass> __masses;
    private NativeQueue<EntityData<GameNodeVelocityComponent>>.ParallelWriter __impacts;
    private TQueryResultWrapper __wrapper;
    private THandler __handler;

    public bool EarlyOutOnFirstHit => false;

    public int NumHits { get; private set; }

    public float MaxFraction { get; private set; }

    public TQueryResult closestHit { get; private set; }

    public GameEntityCastCollector(
        bool isClosestHitOnly,
        int index,
        int camp,
        GameActionTargetType hitType,
        GameActionTargetType damageType,
        float dot,
        float interval,
        float value,
        float impactForce,
        float impactTime,
        float impactMaxSpeed,
        float distance,
        float maxFraction,
        double time,
        float3 direction,
        Entity entity,
        GameEntityTransform start,
        GameEntityTransform end,
        CollisionFilter collisionFilter,
        CollisionWorld collisionWorld,
        GameActionData instance,
        DynamicBuffer<GameActionEntity> actionEntities,
        ComponentLookup<GameEntityCamp> camps,
        ComponentLookup<GameEntityActorMass> masses,
        NativeQueue<EntityData<GameNodeVelocityComponent>>.ParallelWriter impacts,
        TQueryResultWrapper wrapper,
        THandler handler)
    {
        //hitValue = 0.0f;

        __isClosestHitOnly = isClosestHitOnly;
        __index = index;
        __camp = camp;
        __hitType = hitType;
        __damageType = damageType;
        __dot = dot;
        __interval = interval;
        __value = value;
        __impactForce = impactForce;
        __impactTime = impactTime;
        __impactMaxSpeed = impactMaxSpeed;
        __distance = distance;
        __time = time;
        __direction = direction;
        __entity = entity;
        __start = start;
        __end = end;
        __collisionFilter = collisionFilter;
        __collisionWorld = collisionWorld;
        __instance = instance;
        __actionEntities = actionEntities;
        __camps = camps;
        __masses = masses;
        __impacts = impacts;
        __wrapper = wrapper;
        __handler = handler;

        NumHits = 0;

        MaxFraction = maxFraction;

        closestHit = default;
    }

    public bool AddHit(TQueryResult hit)
    {
        var transform = __start.LerpTo(__end, hit.Fraction);

        float3 position = __wrapper.GetPosition(hit);
        if (!__IsHit(position, transform.value))
            return false;

        var rigidbodies = __collisionWorld.Bodies;
        RigidBody rigidbody = rigidbodies[hit.RigidBodyIndex];
        int count = 0;
        if (__camps.HasComponent(rigidbody.Entity))
        {
            GameEntityNode source, destination;
            source.camp = __camp;
            source.entity = __instance.entity;
            destination.camp = __camps[rigidbody.Entity].value;
            destination.entity = rigidbody.Entity;
            if (!source.Predicate(__hitType, destination))
            {
                if (__isClosestHitOnly)
                    MaxFraction = closestHit.Fraction;

                return false;
            }

            if (!__isClosestHitOnly &&
                source.Predicate(__damageType, destination) &&
                CollisionFilter.IsCollisionEnabled(__collisionFilter, rigidbody.Collider.Value.GetLeafFilter(hit.ColliderKey)))
                count = __actionEntities.Hit(rigidbody.Entity, transform.elapsedTime, __interval, __value);
        }

        closestHit = hit;

        if (__isClosestHitOnly)
        {
            MaxFraction = hit.Fraction;

            NumHits = 1;

            return true;
        }

        __Apply(count, position, transform, rigidbody.Entity, rigidbodies);

        return true;
    }

    public bool Apply(out GameEntityTransform transform)
    {
        if (!__isClosestHitOnly || NumHits < 1)
        {
            transform = __end;

            return false;
        }

        var hit = closestHit;

        transform = __start.LerpTo(__end, hit.Fraction);

        float3 position = __wrapper.GetPosition(hit);
        if (!__IsHit(position, transform.value))
            return false;

        var rigidbodies = __collisionWorld.Bodies;
        RigidBody rigidbody = rigidbodies[hit.RigidBodyIndex];
        int count = 0;
        if (CollisionFilter.IsCollisionEnabled(
            __collisionFilter,
            rigidbody.Collider.Value.GetLeafFilter(hit.ColliderKey)) &&
            __camps.HasComponent(rigidbody.Entity))
        {
            GameEntityNode source, destination;
            source.camp = __camp;
            source.entity = __instance.entity;
            destination.camp = __camps[rigidbody.Entity].value;
            destination.entity = rigidbody.Entity;
            if (source.Predicate(__damageType, destination))
                count = __actionEntities.Hit(rigidbody.Entity, transform.elapsedTime, __interval, __value);
        }

        __Apply(count, position, transform, rigidbody.Entity, rigidbodies);

        return true;
    }

    private void __Apply(int count, in float3 position, in GameEntityTransform transform, in Entity entity, in NativeSlice<RigidBody> rigidbodies)
    {
        __handler.Hit(
            __index,
            transform.elapsedTime,
            __time,
            __entity,
            entity,
            transform.value,
            __instance);

        if (count > 0)
        {
            float3 normal = __direction;// math.normalizesafe(position - transform.value.pos, __direction);// __wrapper.GetSurfaceNormal(hit);
            __handler.Damage(
                __index,
                count,
                transform.elapsedTime,
                __time,
                __entity,
                entity,
                position,
                -normal,
                __instance);

            if (__impactForce > math.FLT_MIN_NORMAL && __masses.HasComponent(entity))
            {
                EntityData<GameNodeVelocityComponent> impact;
                impact.value.mode = GameNodeVelocityComponent.Mode.Indirect;
                impact.value.duration = __impactTime;
                impact.value.time = __instance.time + transform.elapsedTime;
                impact.value.value = normal * math.min(__impactForce * __masses[entity].inverseValue, __impactMaxSpeed > math.FLT_MIN_NORMAL ? __impactMaxSpeed : float.MaxValue);
                impact.entity = entity;

                __impacts.Enqueue(impact);

                //UnityEngine.Debug.Log($"{normal} : {position - transform.value.pos} : {__direction}");

                //UnityEngine.Debug.Log($"Impact {entity.Index} : {(double)impact.value.time} : {__time} : {surfaceNormal} : {__start.value.pos} : {__start.value.rot.value} : {__end.value.pos} : {__end.value.rot.value}");
            }

            ++NumHits;
        }

        if (__distance > math.FLT_MIN_NORMAL)
        {
            PointDistanceInput pointDistanceInput = default;
            pointDistanceInput.MaxDistance = __distance;
            pointDistanceInput.Position = position;
            pointDistanceInput.Filter = __collisionFilter;

            var collector = new GameEntityDistanceCollector<THandler>(
                __index,
                __camp,
                __damageType,
                __interval,
                __value,
                __impactForce,
                __impactTime,
                __impactMaxSpeed,
                __distance,
                transform.elapsedTime,
                __time,
                position,
                __direction,
                __entity,
                __instance,
                rigidbodies,
                __actionEntities,
                __camps,
                __masses,
                __impacts,
                __handler);

            __collisionWorld.CalculateDistance(pointDistanceInput, ref collector);

            NumHits += collector.NumHits;
        }
    }

    private bool __IsHit(in float3 position, in RigidTransform transform)
    {
        return !(__dot > math.FLT_MIN_NORMAL && __dot < math.dot(math.normalizesafe(position - transform.pos), math.forward(transform.rot)));
    }
}

[BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Deterministic)]
public struct GameEntityPerform<THandler, TFactory> : IJobChunk 
    where THandler : struct, IGameEntityActionHandler
    where TFactory : struct, IGameEntityActionFactory<THandler>
{
    public struct Executor
    {
        public static readonly PhysicsMass DefaultPhysicsMask = new PhysicsMass()
        {
            Transform = RigidTransform.identity, 
            InverseMass = 0.0f, 
            InverseInertia = float3.zero, 
            AngularExpansionFactor = 1.0f
        };// PhysicsMass.CreateKinematic(MassProperties.UnitSphere);

        public float deltaTime;
        public GameDeadline now;

        public float3 gravity;

        [ReadOnly]
        public CollisionWorld collisionWorld;

        [ReadOnly]
        public ComponentLookup<Disabled> disabled;

        [ReadOnly]
        public ComponentLookup<GameNodeSurface> surfaces;

        [ReadOnly]
        public ComponentLookup<GameNodeDirect> directs;

        [ReadOnly]
        public ComponentLookup<GameNodeIndirect> indirects;

        [ReadOnly]
        public ComponentLookup<GameNodeActorStatus> actorStates;

        [ReadOnly]
        public ComponentLookup<GameEntityCamp> camps;

        [ReadOnly]
        public ComponentLookup<GameEntityActorMass> masses;

        [ReadOnly]
        public ComponentLookup<GameEntityActorInfo> infos;

        [ReadOnly]
        public ComponentLookup<GameEntityCommandVersion> commandVersions;

        [ReadOnly]
        public NativeArray<Entity> entityArray;

        [ReadOnly]
        public NativeArray<GameActionData> instances;

        [ReadOnly]
        public NativeArray<GameActionDataEx> instancesEx;

        [ReadOnly]
        public NativeArray<GameActionStatus> states;

        public NativeArray<Translation> translations;

        public NativeArray<Rotation> rotations;

        public NativeArray<PhysicsVelocity> physicsVelocities;

        //public NativeArray<PhysicsGravityFactor> physicsGravityFactors;

        public BufferAccessor<GameActionEntity> actionEntities;

        [NativeDisableContainerSafetyRestriction]
        public ComponentLookup<GameActionStatus> results;

        public NativeQueue<Entity>.ParallelWriter unstoppableEntities;

        public NativeQueue<EntityData<Translation>>.ParallelWriter locations;

        public NativeQueue<EntityData<GameNodeVelocityComponent>>.ParallelWriter impacts;

        public NativeQueue<EntityData<GameEntityBreakCommand>>.ParallelWriter breakCommands;
        //public EntityCommandQueue<EntityData<GameActionDisabled>>.ParallelWriter entityManager;

        public THandler handler;

        public unsafe void Execute(int index)
        {
            GameActionStatus status = states[index];
            GameActionStatus.Status value = status.value;
            if ((value & GameActionStatus.Status.Destroy) == GameActionStatus.Status.Destroy)
            {
                actionEntities[index].Clear();

                return;
            }

            double time = now;
            Entity entity = entityArray[index];
            GameActionData instance = instances[index];
            if((value & GameActionStatus.Status.Created) != GameActionStatus.Status.Created &&
                handler.Create(
                        index,
                        time,
                        entity,
                        instance))
                value |= GameActionStatus.Status.Created | GameActionStatus.Status.Managed;

            GameActionDataEx instanceEx = instancesEx[index];
            //float actorMoveDistance = 0.0f;
            double oldTime = time - deltaTime,
                actorMoveStartTime = instance.time + instanceEx.info.actorMoveStartTime,
                actorMoveTime = actorMoveStartTime + instanceEx.info.actorMoveDuration;
            if (actorMoveStartTime <= time && oldTime < actorMoveTime)
            {
                double max = math.min(time, actorMoveTime), min = math.max(oldTime, actorMoveStartTime);
                if (max > min && infos.HasComponent(instance.entity) && infos[instance.entity].version == instance.version)
                {
                    if ((instanceEx.value.flag & GameActionFlag.ActorUnstoppable) == GameActionFlag.ActorUnstoppable)
                        unstoppableEntities.Enqueue(instance.entity);

                    if ((instanceEx.value.flag & GameActionFlag.ActorLocation) == GameActionFlag.ActorLocation)
                    {
                        EntityData<Translation> location;
                        location.entity = instance.entity;
                        location.value.Value = instanceEx.value.location;

                        locations.Enqueue(location);
                    }

                    //actorMoveDistance = (instanceEx.info.actorMoveSpeed + instanceEx.info.actorMoveSpeedIndirect) * (float)(max - min);

                    /*EntityData<float> directVelocity;
                    directVelocity.value = actorMoveDistance;
                    directVelocity.entity = instance.entity;
                    directVelocities.Enqueue(directVelocity);*/
                }
            }

            /*if (instance.time + instanceEx.info.performTime <= time)
            {
                if ((value & GameActionStatus.Status.Perform) == GameActionStatus.Status.Perform)
                    value |= GameActionStatus.Status.Performed;
                else
                    value |= GameActionStatus.Status.Perform;
            }*/

            double damageTime = instance.time + instanceEx.info.damageTime, maxDamageTime = damageTime + instanceEx.info.duration;
            double maxTime = math.max(actorMoveTime, maxDamageTime);
            float elapsedTime;
            if (maxTime > time)
                elapsedTime = (float)(time - instance.time);
            else
            {
                elapsedTime = (float)(maxTime - instance.time);

                value |= GameActionStatus.Status.Destroied;
            }

            if (damageTime <= time && oldTime < maxDamageTime)
            {
                PhysicsVelocity physicsVelocity;
                float duration = math.min(elapsedTime, instanceEx.info.damageTime + instanceEx.info.duration), damageElapsedTime = duration - instanceEx.info.damageTime, moveElapsedTime;
                bool isMove;
                if (instanceEx.info.actionMoveTime > math.FLT_MIN_NORMAL && instanceEx.info.actionMoveTime < damageElapsedTime)
                {
                    moveElapsedTime = instanceEx.info.actionMoveTime;

                    isMove = false;
                }
                else if (instanceEx.info.actionMoveSpeed > math.FLT_MIN_NORMAL)
                {
                    moveElapsedTime = damageElapsedTime;

                    isMove = true;
                }
                else
                {
                    moveElapsedTime = 0.0f;

                    isMove = instanceEx.value.trackType != GameActionRangeType.None;
                }

                bool isDamaged = (value & GameActionStatus.Status.Damage) == GameActionStatus.Status.Damage, 
                    isTranslationDirty = false,
                    isRotationDirty = false,
                    isVelocityDirty = false,
                    isGravity = (instanceEx.value.flag & GameActionFlag.UseGravity) == GameActionFlag.UseGravity &&
                            instanceEx.value.trackType == GameActionRangeType.None;
                RigidTransform transform = math.RigidTransform(rotations[index].Value, translations[index].Value);
                var rigidbodies = collisionWorld.Bodies;
                if (isDamaged)
                {
                    value |= GameActionStatus.Status.Damaged;

                    physicsVelocity = physicsVelocities[index];
                }
                else
                {
                    if ((instanceEx.value.flag & GameActionFlag.IgnoreDamage) == GameActionFlag.IgnoreDamage)
                        value |= GameActionStatus.Status.Damaged;
                    else
                        value |= GameActionStatus.Status.Damage;

                    /*if (__CalculateActorOffset(instanceEx, instance.entity, duration, out float3 offset))
                    {
                        transform.pos = instanceEx.origin.pos + offset;

                        isTranslationDirty = true;
                    }*/

                    if (handler.Init(
                        index,
                        instanceEx.info.damageTime,
                        time,
                        entity,
                        transform,
                        instance))
                        value |= GameActionStatus.Status.Managed;

                    physicsVelocity = default;
                    if (isMove)
                    {
                        float3 velocity = instanceEx.direction * instanceEx.info.actionMoveSpeed;
                        physicsVelocity.Linear += velocity;

                        isVelocityDirty = instanceEx.info.actionMoveSpeed > math.FLT_MIN_NORMAL;

                        if (moveElapsedTime > math.FLT_MIN_NORMAL)
                        {
                            if (isGravity)
                            {
                                physicsVelocity.Linear += gravity * moveElapsedTime;// math.max(moveElapsedTime - deltaTime * 0.5f, 0.0f);

                                isVelocityDirty = true;
                            }

                            if (instanceEx.info.actionMoveSpeed > math.FLT_MIN_NORMAL)
                            {
                                transform.pos += velocity * moveElapsedTime;

                                isTranslationDirty = true;
                            }

                            if ((instanceEx.value.flag & GameActionFlag.UseGravity) == GameActionFlag.UseGravity)
                            {
                                transform.pos += gravity * (moveElapsedTime * moveElapsedTime * 0.5f);

                                isTranslationDirty = true;
                            }
                        }
                    }
                }

                bool hasMove = isMove;
                if (hasMove)
                {
                    if (instanceEx.value.trackType != GameActionRangeType.None)
                    {
                        bool isDirty = false;
                        RigidTransform result;
                        if (instanceEx.value.trackType == GameActionRangeType.Destination)
                        {
                            if (instanceEx.info.distance > math.FLT_MIN_NORMAL && disabled.HasComponent(instance.entity))
                                result = transform;
                            else
                            {
                                if (instanceEx.target == Entity.Null || disabled.HasComponent(instanceEx.target))
                                {
                                    result = transform;

                                    result.pos += physicsVelocity.Linear * deltaTime;
                                }
                                else
                                {
                                    var destinationRigidbodyIndex = collisionWorld.GetRigidBodyIndex(instanceEx.target);
                                    if (destinationRigidbodyIndex == -1)
                                    {
                                        result = transform;

                                        result.pos += physicsVelocity.Linear * deltaTime;
                                    }
                                    else
                                    {
                                        var destinationRigidbody = rigidbodies[destinationRigidbodyIndex];

                                        result.pos = destinationRigidbody.WorldFromBody.pos;

                                        var surfaceRotation = surfaces.HasComponent(instanceEx.target) ? surfaces[instanceEx.target].rotation : quaternion.identity;
                                        if (directs.HasComponent(instanceEx.target))
                                            result.pos += math.mul(surfaceRotation, directs[instanceEx.target].value);

                                        if (indirects.HasComponent(instanceEx.target))
                                        {
                                            var indirect = indirects[instanceEx.target];
                                            result.pos += indirect.value + indirect.velocity * deltaTime;
                                        }

                                        if (destinationRigidbody.Collider.IsCreated)
                                        {
                                            PointDistanceInput pointDistanceInput = default;
                                            pointDistanceInput.MaxDistance = math.length(result.pos - transform.pos);
                                            pointDistanceInput.Position = math.transform(math.inverse(destinationRigidbody.WorldFromBody), transform.pos);
                                            pointDistanceInput.Filter = destinationRigidbody.Collider.Value.Filter;
                                            pointDistanceInput.Filter.CollidesWith = instanceEx.value.damageMask;
                                            if (destinationRigidbody.Collider.Value.CalculateDistance(pointDistanceInput, out DistanceHit closestHit))
                                                result.pos = math.transform(destinationRigidbody.WorldFromBody, closestHit.Position);
                                        }

                                        result.rot = transform.rot;

                                        isDirty = true;
                                    }
                                }

                                if (instanceEx.info.distance > math.FLT_MIN_NORMAL)
                                {
                                    float3 distance = result.pos - instanceEx.position;
                                    float length = math.length(distance);
                                    if (length > instanceEx.info.distance)
                                    {
                                        result.pos = instanceEx.position + distance * (instanceEx.info.distance / length);

                                        isDirty = true;
                                    }

                                    result.rot = quaternion.LookRotationSafe(
                                        instanceEx.value.flag == GameActionFlag.MoveInAir ? distance : math.float3(distance.x, 0.0f, distance.z),
                                        math.up());

                                    isRotationDirty = true;
                                }
                            }
                        }
                        else
                        {
                            isDirty = !disabled.HasComponent(instance.entity) &&
                                (instanceEx.value.trackType != GameActionRangeType.All ||
                                instanceEx.target != Entity.Null && !disabled.HasComponent(instanceEx.target));
                            if (isDirty)
                            {
                                int sourceRigidbodyIndex = collisionWorld.GetRigidBodyIndex(instance.entity);
                                if (sourceRigidbodyIndex == -1)
                                {
                                    result = transform;

                                    isDirty = false;
                                }
                                else
                                {
                                    var sourceRigidbody = rigidbodies[sourceRigidbodyIndex];

                                    result.rot = sourceRigidbody.WorldFromBody.rot;
                                    result.pos = math.transform(sourceRigidbody.WorldFromBody, instanceEx.value.offset);

                                    var surfaceRotation = surfaces.HasComponent(instance.entity) ? surfaces[instance.entity].rotation : quaternion.identity;
                                    if (directs.HasComponent(instance.entity))
                                        result.pos += math.mul(surfaceRotation, directs[instance.entity].value);

                                    if (indirects.HasComponent(instance.entity))
                                    {
                                        var indirect = indirects[instance.entity];

                                        result.pos += indirect.value + indirect.velocity * deltaTime;
                                    }

                                    //result.pos += actorMoveDistance * math.forward(result.rot);

                                    //UnityEngine.Debug.LogError($"{result.pos}");
                                    /*if (__CalculateActorOffset(instanceEx, instance.entity, duration, out float3 offset))
                                        result.pos += offset;*/

                                    if (instanceEx.value.trackType == GameActionRangeType.All)
                                    {
                                        var destinationRigidbodyIndex = collisionWorld.GetRigidBodyIndex(instanceEx.target);
                                        if (destinationRigidbodyIndex != -1)
                                        {
                                            var destinationRigidbody = rigidbodies[destinationRigidbodyIndex];
                                            if (destinationRigidbody.Collider.IsCreated)
                                            {
                                                PointDistanceInput pointDistanceInput = default;
                                                pointDistanceInput.MaxDistance = math.length(destinationRigidbody.WorldFromBody.pos - result.pos);
                                                pointDistanceInput.Position = math.transform(math.inverse(transform), result.pos);
                                                pointDistanceInput.Filter = destinationRigidbody.Collider.Value.Filter;
                                                pointDistanceInput.Filter.CollidesWith = instanceEx.value.damageMask;
                                                if (destinationRigidbody.Collider.Value.CalculateDistance(pointDistanceInput, out var closestHit))
                                                    destinationRigidbody.WorldFromBody.pos = math.transform(transform, closestHit.Position);
                                            }

                                            if (instanceEx.info.distance > math.FLT_MIN_NORMAL)
                                            {
                                                float3 distance = destinationRigidbody.WorldFromBody.pos - result.pos;
                                                float length = math.length(distance);
                                                if (length > instanceEx.info.distance)
                                                    destinationRigidbody.WorldFromBody.pos = result.pos + distance * (instanceEx.info.distance / length);

                                                //result.rot = quaternion.LookRotationSafe(distance, math.up());
                                            }

                                            result = Math.Lerp(result, destinationRigidbody.WorldFromBody, 0.5f);

                                            isRotationDirty = true;
                                        }
                                    }
                                }
                            }
                            else
                                result = transform;
                        }

                        if (isDirty)
                        {
                            if (instanceEx.info.actionMoveSpeed > math.FLT_MIN_NORMAL)
                            {
                                float3 distance = result.pos - transform.pos;
                                float length = math.lengthsq(distance);
                                if (length > math.FLT_MIN_NORMAL)
                                {
                                    float moveSpeed = instanceEx.info.actionMoveSpeed * math.rsqrt(length);

                                    physicsVelocity.Linear = math.min(moveSpeed * deltaTime, 1.0f) / deltaTime * distance;
                                }
                                else
                                    physicsVelocity.Linear = float3.zero;

                                isVelocityDirty = true;
                                isRotationDirty = false;
                            }
                            else
                            {
                                transform = result;

                                isTranslationDirty = true;

                                hasMove = isDamaged;
                            }
                        }
                    }

                    if (instanceEx.value.trackType == GameActionRangeType.Source)
                    {
                        isVelocityDirty = !physicsVelocity.Angular.Equals(float3.zero);
                        if (isVelocityDirty)
                            physicsVelocity.Angular = float3.zero;
                    }
                    else
                    {
                        float3 velocity = physicsVelocity.Linear;
                        if (isGravity)
                            //1.5=当前帧实际时间0.5+下一帧时间1.0
                            velocity += gravity * (deltaTime * 1.5f);

                        float speed = math.lengthsq(velocity);
                        if (speed > math.FLT_MIN_NORMAL)
                        {
                            transform.rot = quaternion.LookRotationSafe(velocity, math.up());

                            isRotationDirty = true;

                            /*velocity = math.mul(math.inverse(transform.rot), velocity * math.rsqrt(speed));

                            physicsVelocity.Angular = Math.FromToRotationAxis(math.float3(0.0f, 0.0f, 1.0f), velocity) / deltaTime;
                            if(instanceEx.value.flag != GameActionFlag.MoveInAir)
                                physicsVelocity.Angular.xz = float2.zero;

                            isVelocityDirty = true;*/
                        }
                    }

                    if (isGravity)
                    {
                        physicsVelocity.Linear += gravity * deltaTime;

                        isVelocityDirty = true;
                    }
                }
                else
                {
                    if (instanceEx.info.actionMoveSpeed > math.FLT_MIN_NORMAL)
                        isVelocityDirty = true;

                    if ((instanceEx.value.flag & GameActionFlag.UseGravity) == GameActionFlag.UseGravity &&
                        instanceEx.value.trackType == GameActionRangeType.None)
                    {
                        /*PhysicsGravityFactor physicsGravityFactor;
                        physicsGravityFactor.Value = 0.0f;
                        physicsGravityFactors[index] = physicsGravityFactor;*/

                        isVelocityDirty = true;
                    }

                    if (isVelocityDirty)
                    {
                        hasMove = true;

                        physicsVelocity = default;
                    }
                }

                if (isMove)
                {
                    physicsVelocity.Integrate(DefaultPhysicsMask, deltaTime, ref transform.pos, ref transform.rot);

                    isTranslationDirty = true;

                    isRotationDirty = true;
                }

                if (instanceEx.collider.IsCreated)
                {
                    var actionEntities = this.actionEntities[index];
                    GameActionEntity actionEntity;
                    int numActionEntites = actionEntities.Length;
                    for (int i = 0; i < numActionEntites; ++i)
                    {
                        actionEntity = actionEntities[i];
                        if (math.abs(actionEntity.delta) > math.FLT_MIN_NORMAL)
                        {
                            actionEntity.delta = 0.0f;
                            actionEntities[i] = actionEntity;
                        }
                    }

                    var collider = (Collider*)instanceEx.collider.GetUnsafePtr();
                    if (instanceEx.info.scale > math.FLT_MIN_NORMAL)
                    {
                        int size = instanceEx.collider.Value.MemorySize;
                        var bytes = stackalloc byte[size];
                        UnsafeUtility.MemCpy(bytes, collider, size);
                        collider = (Collider*)bytes;
                        (*collider).Scale(instanceEx.info.scale);
                    }

                    var collisionFilter = instanceEx.collider.Value.Filter;
                    collisionFilter.CollidesWith = instanceEx.value.damageMask;

                    GameEntityTransform result;
                    bool isDestroy;
                    if (hasMove)
                    {
                        int rigidbodyIndex = collisionWorld.GetRigidBodyIndex(entity);

                        GameEntityTransform start;
                        double minTime = time - deltaTime;
                        start.elapsedTime = minTime < damageTime ? instanceEx.info.damageTime : math.min((float)(minTime - instance.time), duration);
                        start.value = rigidbodyIndex == -1 ? transform : rigidbodies[rigidbodyIndex].WorldFromBody;

                        GameEntityTransform end;
                        end.elapsedTime = duration;
                        end.value = transform;

                        ColliderCastInput colliderCastInput = default;
                        colliderCastInput.Start = start.value.pos;
                        colliderCastInput.End = end.value.pos;
                        colliderCastInput.Orientation = start.value.rot;
                        colliderCastInput.Collider = collider;

                        //UnityEngine.Debug.LogError($"dd {index} : {entity.Index} : {instance.actionIndex} : {time}");

                        var castCollector = new GameEntityCastCollector<ColliderCastHit, ColliderCastHitWrapper, THandler>(
                            (instanceEx.value.flag & GameActionFlag.DestroyOnHit) == GameActionFlag.DestroyOnHit,
                            index,
                            instanceEx.camp,
                            instanceEx.value.hitType,
                            instanceEx.value.damageType,
                            instanceEx.info.dot,
                            instanceEx.info.interval,
                            instanceEx.info.hitDestination,
                            instanceEx.info.impactForce,
                            instanceEx.info.impactTime,
                            instanceEx.info.impactMaxSpeed,
                            instanceEx.info.radius,
                            1.0f,
                            time,
                            math.normalizesafe(end.value.pos - start.value.pos, instanceEx.direction),
                            entity,
                            start,
                            end,
                            collisionFilter,
                            collisionWorld,
                            instance,
                            actionEntities,
                            camps,
                            masses,
                            impacts,
                            default,
                            handler);
                        collisionWorld.CastCollider(colliderCastInput, ref castCollector);
                        isDestroy = castCollector.Apply(out result);
                    }
                    else
                    {
                        result.elapsedTime = duration;
                        result.value = transform;

                        float distance = collisionWorld.CollisionTolerance;

                        ColliderDistanceInput colliderDistanceInput = default;
                        colliderDistanceInput.MaxDistance = distance;
                        colliderDistanceInput.Transform = transform;
                        colliderDistanceInput.Collider = collider;

                        var castCollector = new GameEntityCastCollector<DistanceHit, DistanceHitWrapper, THandler>(
                            (instanceEx.value.flag & GameActionFlag.DestroyOnHit) == GameActionFlag.DestroyOnHit,
                            index,
                            instanceEx.camp,
                            instanceEx.value.hitType,
                            instanceEx.value.damageType,
                            instanceEx.info.dot,
                            instanceEx.info.interval,
                            instanceEx.info.hitDestination,
                            instanceEx.info.impactForce,
                            instanceEx.info.impactTime,
                            instanceEx.info.impactMaxSpeed,
                            instanceEx.info.radius,
                            distance,
                            time,
                            instanceEx.direction,
                            entity,
                            result,
                            result,
                            collisionFilter,
                            collisionWorld,
                            instance,
                            actionEntities,
                            camps,
                            masses,
                            impacts,
                            default,
                            handler);
                        collisionWorld.CalculateDistance(colliderDistanceInput, ref castCollector);
                        isDestroy = castCollector.Apply(out result);
                    }

                    if (isDestroy)
                    {
                        isTranslationDirty = true;

                        value |= GameActionStatus.Status.Destroy;

                        elapsedTime = result.elapsedTime;

                        transform = result.value;
                    }
                }

                if ((value & GameActionStatus.Status.Destroy) == GameActionStatus.Status.Destroy)
                {
                    physicsVelocities[index] = default;

                    /*if (index < physicsGravityFactors.Length)
                        physicsGravityFactors[index] = default;*/
                }
                else if (isVelocityDirty)
                    physicsVelocities[index] = physicsVelocity;

                if (isTranslationDirty)
                {
                    Translation translation;
                    translation.Value = transform.pos;
                    translations[index] = translation;
                }

                if (isRotationDirty)
                {
                    Rotation rotation;
                    rotation.Value = transform.rot;
                    rotations[index] = rotation;
                }
            }

            if ((value & GameActionStatus.Status.Destroy) != GameActionStatus.Status.Destroy &&
                instanceEx.value.actorStatusMask != 0 &&
                (!actorStates.HasComponent(instance.entity) ||
                (instanceEx.value.actorStatusMask & (1 << (int)actorStates[instance.entity].value)) == 0))
            {
                value |= GameActionStatus.Status.Destroy;

                if (commandVersions.HasComponent(instance.entity))
                {
                    EntityData<GameEntityBreakCommand> breakCommand;
                    breakCommand.entity = instance.entity;
                    breakCommand.value.version = commandVersions[instance.entity].value;
                    breakCommand.value.delayTime = 0.0f;
                    breakCommand.value.alertTime = 0.0f;
                    breakCommand.value.time = now;
                    breakCommands.Enqueue(breakCommand);
                }
            }

            if ((value & GameActionStatus.Status.Destroy) == GameActionStatus.Status.Destroy)
                handler.Destroy(
                    index,
                    elapsedTime,
                    time,
                    entity,
                    math.RigidTransform(rotations[index].Value, translations[index].Value),
                    instance);

            if (status.value != value)
            {
                //int temp = (int)value;
                //UnityEngine.Debug.LogError($"Change {entity.Index} : {entity.Version} : {temp}");

                status.value = value;
                status.time = instance.time + elapsedTime;
                results[entity] = status;
            }
        }

        /*private bool __CalculateActorOffset(
            in GameActionDataEx instanceEx,
            in Entity entity,
            float duration,
            out float3 offset)
        {
            if ((instanceEx.info.actorMoveSpeed > math.FLT_MIN_NORMAL || instanceEx.info.actorMoveSpeedIndirect > math.FLT_MIN_NORMAL) &&
                    instanceEx.info.damageTime > instanceEx.info.castingTime)
            {
                switch (instanceEx.value.rangeType)
                {
                    case GameActionRangeType.Source:
                    case GameActionRangeType.All:
                        float fixedElapsedTime = duration - deltaTime - instanceEx.info.castingTime,
                            fixedDamageTime = instanceEx.info.damageTime - instanceEx.info.castingTime,
                            moveTime = math.max(instanceEx.info.actorMoveTime, instanceEx.info.actorMoveTimeIndirect);
                        if (moveTime > math.FLT_MIN_NORMAL)
                        {
                            fixedElapsedTime = math.min(fixedElapsedTime, moveTime);
                            fixedDamageTime = math.min(fixedDamageTime, moveTime);
                        }

                        if (fixedElapsedTime > 0.0f)
                        {
                            if (!disabled.HasComponent(entity))
                            {
                                int rigidbodyIndex = collisionWorld.GetRigidBodyIndex(entity);
                                if (rigidbodyIndex == -1)
                                    offset = float3.zero;
                                else
                                {
                                    float3 position = collisionWorld.Bodies[rigidbodyIndex].WorldFromBody.pos + instanceEx.offset;
                                    offset = (position - instanceEx.position) * (fixedDamageTime / fixedElapsedTime);
                                    //UnityEngine.Debug.Log($"{math.distance(position, instanceEx.position)} : {fixedDamageTime / fixedElapsedTime} : {transform.pos + instanceEx.origin.pos}");
                                }
                            }
                            else
                                offset = float3.zero;
                        }
                        else
                            offset = fixedDamageTime * (instanceEx.info.actorMoveSpeed + instanceEx.info.actorMoveSpeedIndirect) * instanceEx.direction;

                        return true;
                }
            }

            offset = float3.zero;

            return false;
        }*/
    }

    public GameDeadline deltaTime;
    public GameTime time;

    public float3 gravity;

    [ReadOnly]
    public CollisionWorldContainer collisionWorld;

    [ReadOnly]
    public ComponentLookup<Disabled> disabled;

    [ReadOnly]
    public ComponentLookup<GameNodeSurface> surfaces;

    [ReadOnly]
    public ComponentLookup<GameNodeDirect> directs;

    [ReadOnly]
    public ComponentLookup<GameNodeIndirect> indirects;

    [ReadOnly]
    public ComponentLookup<GameNodeActorStatus> actorStates;

    [ReadOnly]
    public ComponentLookup<GameEntityCamp> camps;

    [ReadOnly]
    public ComponentLookup<GameEntityActorMass> masses;

    [ReadOnly]
    public ComponentLookup<GameEntityActorInfo> infos;

    [ReadOnly]
    public ComponentLookup<GameEntityCommandVersion> commandVersions;

    [ReadOnly]
    public EntityTypeHandle entityType;

    [ReadOnly]
    public ComponentTypeHandle<GameActionData> instanceType;

    [ReadOnly]
    public ComponentTypeHandle<GameActionDataEx> instanceExType;

    [ReadOnly]
    public ComponentTypeHandle<GameActionStatus> statusType;

    public ComponentTypeHandle<Translation> translationType;

    public ComponentTypeHandle<Rotation> rotationType;

    public ComponentTypeHandle<PhysicsVelocity> physicsVelocityType;

    //public ComponentTypeHandle<PhysicsGravityFactor> physicsGravityFactorType;

    public BufferTypeHandle<GameActionEntity> actionEntityType;

    [NativeDisableContainerSafetyRestriction]
    public ComponentLookup<GameActionStatus> results;

    public NativeQueue<Entity>.ParallelWriter unstoppableEntities;

    //public NativeQueue<EntityData<float>>.ParallelWriter directVelocities;

    public NativeQueue<EntityData<Translation>>.ParallelWriter locations;

    public NativeQueue<EntityData<GameNodeVelocityComponent>>.ParallelWriter impacts;

    public NativeQueue<EntityData<GameEntityBreakCommand>>.ParallelWriter breakCommands;
    //public EntityCommandQueue<EntityData<GameActionDisabled>>.ParallelWriter entityManager;

    public TFactory factory;

    public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
    {
        Executor executor;
        executor.deltaTime = (float)deltaTime;
        executor.now = time;
        executor.gravity = gravity;
        executor.collisionWorld = collisionWorld;
        executor.disabled = disabled;
        executor.surfaces = surfaces;
        executor.directs = directs;
        executor.indirects = indirects;
        executor.actorStates = actorStates;
        executor.camps = camps;
        executor.masses = masses;
        executor.infos = infos;
        executor.commandVersions = commandVersions;
        executor.entityArray = chunk.GetNativeArray(entityType);
        executor.instances = chunk.GetNativeArray(ref instanceType);
        executor.instancesEx = chunk.GetNativeArray(ref instanceExType);
        executor.states = chunk.GetNativeArray(ref statusType);
        executor.translations = chunk.GetNativeArray(ref translationType);
        executor.rotations = chunk.GetNativeArray(ref rotationType);
        executor.physicsVelocities = chunk.GetNativeArray(ref physicsVelocityType);
        //perform.physicsGravityFactors = chunk.GetNativeArray(physicsGravityFactorType);
        executor.actionEntities = chunk.GetBufferAccessor(ref actionEntityType);
        executor.results = results;
        executor.unstoppableEntities = unstoppableEntities;
        executor.locations = locations;
        //perform.directVelocities = directVelocities;
        executor.impacts = impacts;
        executor.breakCommands = breakCommands;
        //perform.entityManager = entityManager;
        executor.handler = factory.Create(chunk);

        var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
        while (iterator.NextEntityIndex(out int i))
            executor.Execute(i);
    }
}

[BurstCompile]
public struct GameEntityComputeHits : IJobChunk
{
    private struct Executor
    {
        [ReadOnly]
        public NativeArray<Entity> entityArray;

        [ReadOnly]
        public NativeArray<GameActionData> instances;

        [ReadOnly]
        public NativeArray<GameActionStatus> states;

        [ReadOnly]
        public BufferAccessor<GameActionEntity> entities;

        [ReadOnly]
        public ComponentLookup<Translation> translations;

        public ComponentLookup<GameEntityActorHit> actorHits;

        public ComponentLookup<GameEntityHit> hits;

        public void Execute(int index)
        {
            var instance = instances[index];
            bool isExists = actorHits.HasComponent(instance.entity);
            GameEntityActorHit result = isExists ? actorHits[instance.entity] : default;
            if ((states[index].value & GameActionStatus.Status.Damaged) == GameActionStatus.Status.Damage)
                ++result.sourceTimes;

            GameEntityHit hit;
            GameEntityActorHit actorHit;
            GameActionEntity entity;
            GameDeadline time;
            var entities = this.entities[index];
            int length = entities.Length;
            //string log = "";
            for (int i = 0; i < length; ++i)
            {
                entity = entities[i];
                if (entity.delta > math.FLT_MIN_NORMAL)
                {
                    //log += entity.ToString();
                    result.sourceHit += entity.delta;

                    if (actorHits.HasComponent(entity.target))
                    {
                        actorHit = actorHits[entity.target];
                        ++actorHit.destinationTimes;
                        actorHit.destinationHit += entity.delta;

                        actorHits[entity.target] = actorHit;
                    }

                    if (hits.HasComponent(entity.target))
                    {
                        hit = hits[entity.target];
                        hit.value += entity.delta;

                        time = instance.time;
                        time += entity.elaspedTime;
                        hit.time = GameDeadline.Max(hit.time, time);
                        hits[entity.target] = hit;

                        //log += "-hit: " + hit.value + ", time: " + hit.time + ", elapsedTime: " + (instance.time + entity.elaspedTime);
                    }
                }
            }

            if (isExists)
                actorHits[instance.entity] = result;
            /*if (log.Length > 1)
                UnityEngine.Debug.Log(log);*/
        }
    }

    [ReadOnly]
    public EntityTypeHandle entityArrayType;

    [ReadOnly]
    public ComponentTypeHandle<GameActionData> instanceType;

    [ReadOnly]
    public ComponentTypeHandle<GameActionStatus> statusType;

    [ReadOnly]
    public BufferTypeHandle<GameActionEntity> entityType;

    [ReadOnly]
    public ComponentLookup<Translation> translations;

    public ComponentLookup<GameEntityActorHit> actorHits;

    public ComponentLookup<GameEntityHit> hits;

    public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
    {
        Executor executor;
        executor.entityArray = chunk.GetNativeArray(entityArrayType);
        executor.instances = chunk.GetNativeArray(ref instanceType);
        executor.states = chunk.GetNativeArray(ref statusType);
        executor.entities = chunk.GetBufferAccessor(ref entityType);
        executor.translations = translations;
        executor.actorHits = actorHits;
        executor.hits = hits;

        var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
        while (iterator.NextEntityIndex(out int i))
            executor.Execute(i);
    }
}

/*[BurstCompile]
private struct ApplyDirectVelocities : IJob
{
    public NativeQueue<EntityData<float>> inputs;

    public ComponentLookup<GameNodeDirect> outputs;

    [ReadOnly]
    public ComponentLookup<Rotation> rotations;

    public void Execute()
    {
        GameNodeDirect output;
        while (inputs.TryDequeue(out var input))
        {
            if (!outputs.HasComponent(input.entity))
                continue;

            output = outputs[input.entity];
            output.value += math.forward(rotations[input.entity].Value) * input.value;

            outputs[input.entity] = output;
        }
    }
}*/

[BurstCompile]
public struct GameEntityApplyUnstoppableEntities : IJob
{
    public NativeQueue<Entity> entities;

    public ComponentLookup<GameNodeCharacterFlag> flags;

    public void Execute()
    {
        GameNodeCharacterFlag flag;
        while (entities.TryDequeue(out var entity))
        {
            if (!flags.HasComponent(entity))
                continue;

            flag = flags[entity];
            flag.value |= GameNodeCharacterFlag.Flag.Unstoppable;
            flags[entity] = flag;
        }
    }
}

public static class GameEntityUtility
{
    public static int Hit(
        this ref DynamicBuffer<GameActionEntity> actionEntities,
        in Entity entity,
        float elaspedTime,
        float interval,
        float value)
    {
        int i, numActionEntities = actionEntities.Length;
        GameActionEntity actionEntity = default;
        for (i = 0; i < numActionEntities; ++i)
        {
            actionEntity = actionEntities[i];
            if (actionEntity.target == entity)
                break;
        }

        if (i < numActionEntities)
        {
            if (interval > math.FLT_MIN_NORMAL)
            {
                if (actionEntity.elaspedTime >= elaspedTime)
                    return 0;

                int count = 0;
                float nextTime = actionEntity.elaspedTime + interval;
                do
                {
                    ++count;

                    actionEntity.elaspedTime = nextTime;

                    nextTime += interval;
                } while (nextTime <= elaspedTime);

                actionEntity.delta = value * count;
                actionEntity.hit += actionEntity.delta;
                actionEntities[i] = actionEntity;

                return count;
            }
            else
                return 0;
        }

        actionEntity.hit = value;
        actionEntity.delta = value;
        actionEntity.elaspedTime = elaspedTime;
        actionEntity.target = entity;

        actionEntities.Add(actionEntity);

        return 1;
    }

}
