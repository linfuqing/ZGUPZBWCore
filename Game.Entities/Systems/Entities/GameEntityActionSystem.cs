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

[BurstCompile, UpdateInGroup(typeof(GameNodeCharacterSystemGroup))]
public partial struct GameEntityActionLocationSystem : ISystem
{
    [BurstCompile]
    private struct ApplyTranslations : IJob
    {
        public SharedHashMap<Entity, float3>.Writer sources;

        public ComponentLookup<Translation> destinations;

        public void Execute()
        {
            Entity entity;
            Translation translation;
            foreach (var pair in sources)
            {
                entity = pair.Key;
                if (!destinations.HasComponent(entity))
                    return;

                translation.Value = pair.Value;

                //UnityEngine.Debug.Log($"Locate {entity} : {translation.Value}");

                destinations[entity] = translation;
            }

            sources.Clear();
        }
    }

    private ComponentLookup<Translation> __translations;

    public SharedHashMap<Entity, float3> locations
    {
        get;

        private set;
    }

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        locations = new SharedHashMap<Entity, float3>(Allocator.Persistent);

        __translations = state.GetComponentLookup<Translation>();
    }

    //[BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        locations.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        ApplyTranslations applyTranslations;
        applyTranslations.sources = locations.writer;
        applyTranslations.destinations = __translations.UpdateAsRef(ref state);

        ref var lookupJobManager = ref locations.lookupJobManager;
        var jobHandle = applyTranslations.ScheduleByRef(JobHandle.CombineDependencies(state.Dependency, lookupJobManager.readOnlyJobHandle));
        lookupJobManager.AddReadOnlyDependency(jobHandle);

        state.Dependency = jobHandle;
    }
}

[BurstCompile,
    CreateAfter(typeof(GameEntityActionLocationSystem)),
    CreateAfter(typeof(GamePhysicsWorldBuildSystem)),
    UpdateInGroup(typeof(GameEntityActionSystemGroup))]
public partial struct GameEntityActionSystem : ISystem
{
    [BurstCompile]
    private struct Reset : IJob
    {
        [ReadOnly, DeallocateOnJobCompletion]
        public NativeArray<int> count;

        public SharedHashMap<Entity, float3>.Writer locations;

        public void Execute()
        {
            locations.capacity = math.max(locations.capacity, count[0]);
        }
    }

    private struct DistanceCollector : ICollector<DistanceHit>// where T : IGameEntityActionHandler
    {
        //public float hitValue;

        //private int __index;
        private int __camp;
        private GameActionTargetType __type;
        private float __interval;
        private float __value;
        private float __impactForce;
        private float __impactTime;
        private float __impactMaxSpeed;
        private float __elapsedTime;
        //private double __time;
        private float3 __up;
        private float3 __position;
        private float3 __direction;
        private RigidTransform __transform;
        private Entity __entity;
        private GameActionData __instance;
        private NativeSlice<RigidBody> __rigidBodies;
        private DynamicBuffer<GameActionEntity> __actionEntities;
        private ComponentLookup<GameEntityCamp> __camps;
        private ComponentLookup<GameEntityActorMass> __masses;
        private NativeFactory<EntityData<GameNodeVelocityComponent>>.ParallelWriter __impacts;
        private NativeFactory<GameEntityActionDamager>.ParallelWriter __damagers;
        //private T __handler;

#if GAME_DEBUG_COMPARSION
        private uint __frameIndex;

        private ComponentLookup<GameEntityIndex> __entityIndices;
#endif
        public bool EarlyOutOnFirstHit => false;

        public int NumHits { get; private set; }

        public float MaxFraction { get; }

        public DistanceCollector(
            //int index,
            int camp,
            GameActionTargetType type,
            float interval,
            float value,
            float impactForce,
            float impactTime,
            float impactMaxSpeed,
            float distance,
            float elapsedTime,
            //double time,
            in float3 up,
            in float3 position,
            in float3 direction,
            in RigidTransform transform, 
            in Entity entity,
            in GameActionData instance,
            in NativeSlice<RigidBody> rigidBodies,
            in ComponentLookup<GameEntityCamp> camps,
            in ComponentLookup<GameEntityActorMass> masses,
            ref DynamicBuffer<GameActionEntity> actionEntities,
            ref NativeFactory<EntityData<GameNodeVelocityComponent>>.ParallelWriter impacts, 
            ref NativeFactory<GameEntityActionDamager>.ParallelWriter damagers
#if GAME_DEBUG_COMPARSION    
            , uint frameIndex, 
            ComponentLookup<GameEntityIndex> entityIndices
#endif
            )
        {
            //hitValue = 0.0f;

            //__index = index;
            __camp = camp;
            __type = type;
            __interval = interval;
            __value = value;
            __impactForce = impactForce;
            __impactTime = impactTime;
            __impactMaxSpeed = impactMaxSpeed;
            __elapsedTime = elapsedTime;
            //__time = time;
            __up = up;
            __position = position;
            __direction = direction;
            __transform = transform;
            __entity = entity;
            __instance = instance;
            __rigidBodies = rigidBodies;
            __camps = camps;
            __masses = masses;
            __actionEntities = actionEntities;
            __impacts = impacts;
            __damagers = damagers;

            NumHits = 0;
            MaxFraction = distance;
            
#if GAME_DEBUG_COMPARSION
            __frameIndex = frameIndex;
            __entityIndices = entityIndices;
#endif
        }

        public bool AddHit(DistanceHit hit)
        {
            var rigidbody = __rigidBodies[hit.RigidBodyIndex];
            if (!__camps.HasComponent(rigidbody.Entity))
                return false;

            GameEntityNode source, destination;
            source.camp = __camp;
            source.entity = __instance.entity;
            destination.camp = __camps[rigidbody.Entity].value;
            destination.entity = rigidbody.Entity;
            if (!source.Predicate(__type, destination))
                return false;

            float3 normal = math.normalizesafe(hit.Position - __position, __direction);
            int count = __actionEntities.Hit(rigidbody.Entity, normal, __elapsedTime, __interval, __value);
            if (count < 1)
                return false;

            GameEntityActionDamager damager;
            damager.count = count;
            damager.elapsedTime = __elapsedTime;
            damager.entity = __entity;
            damager.target = rigidbody.Entity;
            damager.position = hit.Position;
            damager.normal = -normal;
            damager.transform = __transform;
            __damagers.Create().value = damager;

            /*__handlers.Damage(
                __index,
                count,
                __elpasedTime,
                __time,
                __entity,
                rigidbody.Entity,
                hit.Position,
                -normal,
                __instance);*/

            if (__impactForce > math.FLT_MIN_NORMAL && __masses.HasComponent(rigidbody.Entity))
            {
                EntityData<GameNodeVelocityComponent> impact;
                impact.value.mode = GameNodeVelocityComponent.Mode.Indirect;
                impact.value.duration = __impactTime;
                impact.value.time = __instance.time + __elapsedTime;
                impact.value.value = normal * math.min(__impactForce * __masses[rigidbody.Entity].inverseValue, __impactMaxSpeed > math.FLT_MIN_NORMAL ? __impactMaxSpeed : float.MaxValue);
                impact.value.value = Math.ProjectOnPlaneSafe(impact.value.value, __up);
                impact.entity = rigidbody.Entity;
                
                //UnityEngine.Debug.LogError($"{__entityIndices[rigidbody.Entity]} : {__frameIndex} : {impact.value}");

                __impacts.Create().value = impact;
            }

            ++NumHits;

            return true;
        }
    }

    private struct CastCollector<TQueryResult, TQueryResultWrapper> : ICollector<TQueryResult>
        where TQueryResult : struct, IQueryResult
        where TQueryResultWrapper : struct, IQueryResultWrapper<TQueryResult>
        //where THandler : IGameEntityActionHandler
    {
        
        private bool __isClosestHitOnly;
        //private int __index;
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
        //private double __time;
        private float3 __up;
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
        private NativeFactory<EntityData<GameNodeVelocityComponent>>.ParallelWriter __impacts;
        private NativeFactory<GameEntityActionHiter>.ParallelWriter __hiters;
        private NativeFactory<GameEntityActionDamager>.ParallelWriter __damagers;
        private TQueryResultWrapper __wrapper;

#if GAME_DEBUG_COMPARSION
        private uint __frameIndex;

        private ComponentLookup<GameEntityIndex> __entityIndices;
#endif

        //public string log;

        public bool EarlyOutOnFirstHit => false;

        public int NumHits { get; private set; }

        public float MaxFraction { get; private set; }

        public TQueryResult closestHit { get; private set; }

        public CastCollector(
            bool isClosestHitOnly,
            //int index,
            int camp,
            GameActionTargetType hitType,
            GameActionTargetType damageType,
            float interval,
            float value,
            float impactForce,
            float impactTime,
            float impactMaxSpeed,
            float dot,
            float distance,
            float maxFraction,
            //double time,
            in float3 up,
            in float3 direction,
            in Entity entity,
            in GameEntityTransform start,
            in GameEntityTransform end,
            in CollisionFilter collisionFilter,
            in CollisionWorld collisionWorld,
            in GameActionData instance,
            in ComponentLookup<GameEntityCamp> camps,
            in ComponentLookup<GameEntityActorMass> masses,
            ref DynamicBuffer<GameActionEntity> actionEntities,
            ref NativeFactory<EntityData<GameNodeVelocityComponent>>.ParallelWriter impacts,
            ref NativeFactory<GameEntityActionHiter>.ParallelWriter hiters, 
            ref NativeFactory<GameEntityActionDamager>.ParallelWriter damagers, 
            ref TQueryResultWrapper wrapper
#if GAME_DEBUG_COMPARSION
            , uint frameIndex, 
            in ComponentLookup<GameEntityIndex> entityIndices
#endif
            )
        {
            //log = "";
            //hitValue = 0.0f;

            __isClosestHitOnly = isClosestHitOnly;
            //__index = index;
            __camp = camp;
            __hitType = hitType;
            __damageType = damageType;
            __interval = interval;
            __value = value;
            __impactForce = impactForce;
            __impactTime = impactTime;
            __impactMaxSpeed = impactMaxSpeed;
            __dot = dot;
            __distance = distance;
            //__time = time;
            __up = up;
            __direction = direction;
            __entity = entity;
            __start = start;
            __end = end;
            __collisionFilter = collisionFilter;
            __collisionWorld = collisionWorld;
            __instance = instance;
            __camps = camps;
            __masses = masses;
            __actionEntities = actionEntities;
            __impacts = impacts;
            __hiters = hiters;
            __damagers = damagers;
            __wrapper = wrapper;

            NumHits = 0;

            MaxFraction = maxFraction;

            closestHit = default;
            
#if GAME_DEBUG_COMPARSION
            __frameIndex = frameIndex;
            __entityIndices = entityIndices;
#endif
        }

        public bool AddHit(TQueryResult hit)
        {
            //log += hit.ToString();
            var transform = __start.LerpTo(__end, hit.Fraction);

            float3 position = __wrapper.GetPosition(hit);
            float fraction = closestHit.Fraction + math.FLT_MIN_NORMAL;
            if (fraction == MaxFraction && NumHits > 0)
            {
                float3 distance = position - transform.value.pos, 
                    originDistance = __wrapper.GetPosition(closestHit) - transform.value.pos;
                float lengthSQ = math.lengthsq(distance), originLengthSQ = math.lengthsq(originDistance);
                if (lengthSQ > originLengthSQ)
                    return false;

                if (lengthSQ == originLengthSQ)
                {
                    float3 surfaceNormal = __wrapper.GetSurfaceNormal(hit), originSurfaceNormal = __wrapper.GetPosition(closestHit);
                    if (math.dot(distance, surfaceNormal) < math.dot(originDistance, originSurfaceNormal))
                        return false;
                }
            }

            if (!__IsHit(in position, in transform.value))
                return false;

            var rigidbodies = __collisionWorld.Bodies;
            RigidBody rigidbody = rigidbodies[hit.RigidBodyIndex];
            float3 normal = __direction;// math.normalizesafe(position - transform.value.pos, __direction);// __wrapper.GetSurfaceNormal(hit);
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
                    //加此判断会不同步
                    /*if (__isClosestHitOnly)
                        MaxFraction = fraction;*/

                    return false;
                }

                if (!__isClosestHitOnly &&
                    source.Predicate(__damageType, destination) &&
                    CollisionFilter.IsCollisionEnabled(__collisionFilter, rigidbody.Collider.Value.GetLeafFilter(hit.ColliderKey)))
                    count = __actionEntities.Hit(rigidbody.Entity, normal, transform.elapsedTime, __interval, __value);
            }

            closestHit = hit;

            if (__isClosestHitOnly)
            {
                MaxFraction = fraction;

                NumHits = 1;

                return true;
            }

            __Apply(count, normal, position, transform, rigidbody.Entity, rigidbodies);

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

            if (!__IsHit(in position, in transform.value))
                return false;

            float3 normal = __direction;// math.normalizesafe(position - transform.value.pos, __direction);// __wrapper.GetSurfaceNormal(hit);
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
                    count = __actionEntities.Hit(rigidbody.Entity, normal, transform.elapsedTime, __interval, __value);
            }

            __Apply(count, normal, position, transform, rigidbody.Entity, rigidbodies);

            return true;
        }

        private void __Apply(
            int count,
            in float3 normal,
            in float3 position,
            in GameEntityTransform transform,
            in Entity entity,
            in NativeSlice<RigidBody> rigidbodies)
        {
            GameEntityActionHiter hiter;
            hiter.elapsedTime = transform.elapsedTime;
            hiter.entity = __entity;
            hiter.target = entity;
            hiter.transform = transform.value;

            __hiters.Create().value = hiter;

            /*__handlers.Hit(
                __index,
                transform.elapsedTime,
                __time,
                __entity,
                entity,
                transform.value,
                __instance);*/

            if (count > 0)
            {
                GameEntityActionDamager damager;
                damager.count = count;
                damager.elapsedTime = transform.elapsedTime;
                damager.entity = __entity;
                damager.target = entity;
                damager.position = position;
                damager.normal = -normal;
                damager.transform = transform.value;

                __damagers.Create().value = damager;

                /*__handlers.Damage(
                    __index,
                    count,
                    transform.elapsedTime,
                    __time,
                    __entity,
                    entity,
                    position,
                    -normal,
                    __instance);*/

                //UnityEngine.Debug.LogError($"Damage {__entity} : {entity}");

                if (__impactForce > math.FLT_MIN_NORMAL && __masses.HasComponent(entity))
                {
                    EntityData<GameNodeVelocityComponent> impact;
                    impact.value.mode = GameNodeVelocityComponent.Mode.Indirect;
                    impact.value.duration = __impactTime;
                    impact.value.time = __instance.time + transform.elapsedTime;
                    impact.value.value = normal * math.min(__impactForce * __masses[entity].inverseValue, __impactMaxSpeed > math.FLT_MIN_NORMAL ? __impactMaxSpeed : float.MaxValue);
                    impact.value.value = Math.ProjectOnPlaneSafe(impact.value.value, __up);
                    impact.entity = entity;

                    __impacts.Create().value = impact;

                    //UnityEngine.Debug.LogError($"{__entityIndices[entity]} : {__frameIndex} : {impact.value}");

                    //UnityEngine.Debug.Log($"{normal} : {position - transform.value.pos} : {__direction}");

                    //UnityEngine.Debug.Log($"Impact {entity.Index} : {(double)impact.value.time} : {__time} : {__start.value.pos} : {__start.value.rot.value} : {__end.value.pos} : {__end.value.rot.value}");
                }

                ++NumHits;
            }

            if (__distance > math.FLT_MIN_NORMAL)
            {
                PointDistanceInput pointDistanceInput = default;
                pointDistanceInput.MaxDistance = __distance;
                pointDistanceInput.Position = position;
                pointDistanceInput.Filter = __collisionFilter;

                var collector = new DistanceCollector(
                    //__index,
                    __camp,
                    __damageType,
                    __interval,
                    __value,
                    __impactForce,
                    __impactTime,
                    __impactMaxSpeed,
                    __distance,
                    transform.elapsedTime,
                    //__time,
                    __up,
                    position,
                    __direction,
                    transform.value, 
                    __entity,
                    __instance,
                    rigidbodies,
                    __camps,
                    __masses,
                    ref __actionEntities,
                    ref __impacts, 
                    ref __damagers
#if GAME_DEBUG_COMPARSION    
                    , __frameIndex, __entityIndices
#endif
                    );

                __collisionWorld.CalculateDistance(pointDistanceInput, ref collector);

                NumHits += collector.NumHits;
            }
        }

        private bool __IsHit(in float3 position, in RigidTransform transform)
        {
            return !(__dot > math.FLT_MIN_NORMAL && __dot < math.dot(math.normalizesafe(position - transform.pos), math.forward(transform.rot)));
        }
    }

    private struct Perform/*<THandler, TFactory>
        where THandler : struct, IGameEntityActionHandler
        where TFactory : struct, IGameEntityActionFactory<THandler>*/
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
        public ComponentLookup<GameNodeCharacterCollider> colliders;

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

        public SharedHashMap<Entity, float3>.ParallelWriter locations;

        public NativeFactory<Entity>.ParallelWriter unstoppableEntities;

        public NativeFactory<EntityData<GameNodeVelocityComponent>>.ParallelWriter impacts;

        public NativeFactory<EntityData<GameEntityBreakCommand>>.ParallelWriter breakCommands;
        //public EntityCommandQueue<EntityData<GameActionDisabled>>.ParallelWriter entityManager;

        //public THandler handler;

        public GameEntityActionManager.ParallelWriter actionManager;

#if GAME_DEBUG_COMPARSION
        public uint frameIndex;

        [ReadOnly]
        public ComponentLookup<GameEntityIndex> entityIndices;
#endif
        
        public unsafe void Execute(int index)
        {
            var status = states[index];
            var value = status.value;
            if ((value & GameActionStatus.Status.Destroy) == GameActionStatus.Status.Destroy)
            {
                actionEntities[index].Clear();

                return;
            }

            var instance = instances[index];
            var instanceEx = instancesEx[index];

            bool isSourceTransform = false;
            double time = now,
                actorMoveStartTime = instance.time + instanceEx.info.actorMoveStartTime,
                actorMoveTime = actorMoveStartTime + instanceEx.info.actorMoveDuration,
                oldTime = time - deltaTime;
            RigidTransform sourceTransform = RigidTransform.identity;
            var rigidbodies = collisionWorld.Bodies;
            bool isActorMove = time >= actorMoveStartTime && oldTime < actorMoveTime;
            if(isActorMove)
            {
                double max = math.min(time, actorMoveTime), min = math.max(oldTime, actorMoveStartTime);

                isActorMove &= max > min;
            }

            if ((isActorMove || ((value & GameActionStatus.Status.Created) != GameActionStatus.Status.Created)) &&
                infos.HasComponent(instance.entity) && infos[instance.entity].version == instance.version)
            {
                if (isActorMove && (instanceEx.value.flag & GameActionFlag.ActorUnstoppable) == GameActionFlag.ActorUnstoppable)
                    unstoppableEntities.Create().value = instance.entity;

                if (instanceEx.info.actorLocationDistance > math.FLT_MIN_NORMAL)
                {
                    int sourceRigidbodyIndex = collisionWorld.GetRigidBodyIndex(instance.entity);
                    if (sourceRigidbodyIndex != -1)
                    {
                        var sourceRigidbody = rigidbodies[sourceRigidbodyIndex];

                        float distance;
                        float3 source = math.transform(sourceRigidbody.WorldFromBody, instanceEx.value.actorOffset), destination;

                        if ((instanceEx.value.flag & GameActionFlag.ActorLocation) == GameActionFlag.ActorLocation)
                        {
                            destination = instanceEx.targetPosition;

                            distance = math.distance(destination, source);
                        }
                        else
                        {
                            distance = instanceEx.info.distance;

                            destination = source + math.forward(sourceRigidbody.WorldFromBody.rot) * distance;
                        }

                        if (distance > math.FLT_MIN_NORMAL)
                        {
                            ColliderCastInput colliderCastInput = default;
                            colliderCastInput.Collider = (Collider*)(colliders.HasComponent(instance.entity) ? colliders[instance.entity].value.GetUnsafePtr() : sourceRigidbody.Collider.GetUnsafePtr());
                            colliderCastInput.Orientation = sourceRigidbody.WorldFromBody.rot;
                            colliderCastInput.Start = source;
                            colliderCastInput.End = destination;
                            var collector = new ClosestHitCollectorExclude<ColliderCastHit>(sourceRigidbodyIndex, 1.0f);
                            /*if (collisionWorld.CastCollider(colliderCastInput, ref collector))
                                destination = math.lerp(sourceRigidbody.WorldFromBody.pos, destination, collector.closestHit.Fraction);*/

                            float fraction;
                            if (collisionWorld.CastCollider(colliderCastInput, ref collector))
                            {
                                var closestHit = collector.closestHit;
                                if (closestHit.Entity == instanceEx.target)
                                    fraction = closestHit.Fraction;
                                else
                                    fraction = math.min(instanceEx.info.actorLocationDistance, distance * closestHit.Fraction) / distance;
                            }
                            else
                                fraction = math.min(instanceEx.info.actorLocationDistance, distance) / distance;

                            sourceTransform.pos = fraction > math.FLT_MIN_NORMAL ? math.lerp(source, destination, fraction) : sourceRigidbody.WorldFromBody.pos;
                        }
                        else
                            sourceTransform.pos = sourceRigidbody.WorldFromBody.pos;

                        sourceTransform.rot = sourceRigidbody.WorldFromBody.rot;

                        isSourceTransform = true;
                    }
                }
                else if ((instanceEx.value.flag & GameActionFlag.ActorLocation) == GameActionFlag.ActorLocation)
                {
                    sourceTransform.pos = instanceEx.targetPosition;
                    int sourceRigidbodyIndex = collisionWorld.GetRigidBodyIndex(instance.entity);
                    if (sourceRigidbodyIndex != -1)
                    {
                        var sourceRigidbody = rigidbodies[sourceRigidbodyIndex];
                        float3 source = math.transform(sourceRigidbody.WorldFromBody, instanceEx.value.actorOffset),
                            destination = math.transform(sourceTransform, instanceEx.value.actorOffset);
                        float distance = collisionWorld.CollisionTolerance;
                        var collider = (Collider*)(colliders.HasComponent(instance.entity) ? colliders[instance.entity].value.GetUnsafePtr() : sourceRigidbody.Collider.GetUnsafePtr());

                        ColliderDistanceInput colliderDistanceInput = default;
                        colliderDistanceInput.Collider = collider;
                        colliderDistanceInput.Transform = math.RigidTransform(sourceRigidbody.WorldFromBody.rot, destination);
                        colliderDistanceInput.MaxDistance = distance;
                        var distanceCollector = new StaticBodyCollector<DistanceHit>(collisionWorld.NumDynamicBodies, distance);
                        if (collisionWorld.CalculateDistance(colliderDistanceInput, ref distanceCollector))
                        {
                            ColliderCastInput colliderCastInput = default;
                            colliderCastInput.Collider = (Collider*)(colliders.HasComponent(instance.entity) ? colliders[instance.entity].value.GetUnsafePtr() : sourceRigidbody.Collider.GetUnsafePtr());
                            colliderCastInput.Orientation = sourceRigidbody.WorldFromBody.rot;
                            colliderCastInput.Start = source;
                            colliderCastInput.End = destination;

                            var collector = new StaticBodyCollector<ColliderCastHit>(collisionWorld.NumDynamicBodies, 1.0f);
                            if (collisionWorld.CastCollider(colliderCastInput, ref collector))
                            {
                                float fraction = collector.closestHit.Fraction;
                                sourceTransform.pos = fraction > math.FLT_MIN_NORMAL ? math.lerp(source, destination, fraction) : sourceRigidbody.WorldFromBody.pos;
                            }
                            else
                                sourceTransform.pos = destination;
                        }
                        else
                            sourceTransform.pos = destination;

                        sourceTransform.rot = rigidbodies[sourceRigidbodyIndex].WorldFromBody.rot;
                    }

                    isSourceTransform = true;
                }

                if (isSourceTransform)
                {
                    if (isActorMove)
                        locations.TryAdd(instance.entity, sourceTransform.pos);
                }

                //actorMoveDistance = (instanceEx.info.actorMoveSpeed + instanceEx.info.actorMoveSpeedIndirect) * (float)(max - min);

                /*EntityData<float> directVelocity;
                directVelocity.value = actorMoveDistance;
                directVelocity.entity = instance.entity;
                directVelocities.Enqueue(directVelocity);*/
            }

            Entity entity = entityArray[index];
            int rigidbodyIndex = collisionWorld.GetRigidBodyIndex(entity);
            var rigidbody = rigidbodies[rigidbodyIndex];
            RigidTransform origin = rigidbody.WorldFromBody, transform = origin;// math.RigidTransform(rotations[index].Value, translations[index].Value);

            double damageTime = instance.time + instanceEx.info.damageTime,
                maxDamageTime = damageTime + instanceEx.info.duration,
                maxTime = math.max(actorMoveTime, maxDamageTime);
            if ((value & GameActionStatus.Status.Created) != GameActionStatus.Status.Created)
            {
                GameEntityActionCreator creator;
                creator.targetPosition = isSourceTransform ? sourceTransform.pos : instanceEx.targetPosition;
                creator.entity = entity;
                actionManager.creators.AddNoResize(creator);

                if((value & GameActionStatus.Status.Damage) == GameActionStatus.Status.Damage)
                {
                    GameEntityActionInitializer initializer;
                    initializer.elapsedTime = (float)(maxTime > oldTime ? oldTime - instance.time : maxTime - instance.time);
                    initializer.entity = entity;
                    initializer.transform = transform;
                    actionManager.initializers.AddNoResize(initializer);
                }

                /*if (handlers.Create(
                        index,
                        time,
                        isSourceTransform ? sourceTransform.pos : instanceEx.targetPosition,
                        entity,
                        instanceEx.transform,
                        instance) ||
                        ((value & GameActionStatus.Status.Damage) == GameActionStatus.Status.Damage) &&
                        handlers.Init(
                        index,
                        (float)(maxTime > oldTime ? oldTime - instance.time : maxTime - instance.time),
                        time,
                        entity,
                        transform,
                        instance))
                    value |= GameActionStatus.Status.Managed;
                else*/
                    value |= GameActionStatus.Status.Created;
            }

            float elapsedTime;
            if (maxTime > time)
            {
                elapsedTime = (float)(time - instance.time);

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
                }
            }
            else
            {
                elapsedTime = (float)(maxTime - instance.time);

                value |= GameActionStatus.Status.Destroied;
            }

            if (instance.time + (instanceEx.info.actionPerformTime > math.FLT_MIN_NORMAL ? instanceEx.info.actionPerformTime : math.min(instanceEx.info.performTime, instanceEx.info.damageTime)) <= time)
            {
                /*if ((value & GameActionStatus.Status.Perform) == GameActionStatus.Status.Perform)
                    value |= GameActionStatus.Status.Performed;
                else*/
                value |= GameActionStatus.Status.Perform;
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

                    if (instanceEx.value.trackType == GameActionRangeType.None &&
                        instanceEx.value.rangeType == GameActionRangeType.Source)
                    {
                        if (!isSourceTransform)
                        {
                            int sourceRigidbodyIndex = collisionWorld.GetRigidBodyIndex(instance.entity);
                            if (sourceRigidbodyIndex != -1)
                            {
                                var sourceRigidbody = rigidbodies[sourceRigidbodyIndex];
                                sourceTransform = sourceRigidbody.WorldFromBody;

                                isSourceTransform = true;
                            }
                        }

                        if (isSourceTransform)
                            transform.pos = math.transform(sourceTransform, instanceEx.value.offset) + __CalculateOffset(instance.entity);
                    }

                    GameEntityActionInitializer initializer;
                    initializer.elapsedTime = instanceEx.info.damageTime;
                    initializer.entity = entity;
                    initializer.transform = transform;
                    actionManager.initializers.AddNoResize(initializer);

                    /*if (handlers.Init(
                        index,
                        instanceEx.info.damageTime,
                        time,
                        entity,
                        transform,
                        instance))
                        value |= GameActionStatus.Status.Managed;*/

                    float3 velocity = instanceEx.direction * instanceEx.info.actionMoveSpeed;

                    physicsVelocity = default;
                    if (isMove)
                    {
                        physicsVelocity.Linear += velocity;

                        isVelocityDirty = instanceEx.info.actionMoveSpeed > math.FLT_MIN_NORMAL;

                        if (isGravity)
                        {
                            /*if (index < physicsGravityFactors.Length)
                            {
                                PhysicsGravityFactor physicsGravityFactor;
                                physicsGravityFactor.Value = 1.0f;
                                physicsGravityFactors[index] = physicsGravityFactor;
                            }*/

                            physicsVelocity.Linear += gravity * math.max(moveElapsedTime - deltaTime * 0.5f, 0.0f);

                            isVelocityDirty = true;
                        }
                    }

                    if (moveElapsedTime > math.FLT_MIN_NORMAL)
                    {
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

                var up = -gravity;
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

                                        result.pos = destinationRigidbody.WorldFromBody.pos + __CalculateOffset(instanceEx.target);

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
                                        (instanceEx.value.flag & GameActionFlag.MoveInAir) == GameActionFlag.MoveInAir ? Math.ProjectOnPlaneSafe(distance, up) : distance,
                                        up);

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
                                if (!isSourceTransform)
                                {
                                    int sourceRigidbodyIndex = collisionWorld.GetRigidBodyIndex(instance.entity);
                                    if (sourceRigidbodyIndex != -1)
                                    {
                                        sourceTransform = rigidbodies[sourceRigidbodyIndex].WorldFromBody;
                                        //result.pos = math.transform(sourceRigidbody.WorldFromBody, instanceEx.value.actorOffset);

                                        isSourceTransform = true;
                                    }
                                }

                                if (isSourceTransform)
                                {
                                    result.rot = sourceTransform.rot;
                                    result.pos = math.transform(sourceTransform, instanceEx.value.offset) + __CalculateOffset(instance.entity);

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
                                else
                                {
                                    result = transform;

                                    isDirty = false;
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
                                //UnityEngine.Debug.Log($"4 {entityArray[index]} : {entityIndices[instance.entity].value} : {frameIndex} : {instance.index} : {transform} : {physicsVelocity}");
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
                            //1.5=��ǰ֡ʵ��ʱ��0.5+��һ֡ʱ��1.0
                            velocity += gravity * (deltaTime * 1.5f);

                        float speed = math.lengthsq(velocity);
                        if (speed > math.FLT_MIN_NORMAL)
                        {
                            transform.rot = quaternion.LookRotationSafe(velocity, math.up());

                            //UnityEngine.Debug.Log($"3 {entityArray[index]} : {entityIndices[instance.entity].value} : {frameIndex} : {instance.index} : {transform} : {physicsVelocity}");
                            //UnityEngine.Debug.LogError(((UnityEngine.Quaternion)transform.rot).eulerAngles.ToString() + velocity);

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
                    //UnityEngine.Debug.Log($"2 {entityArray[index]} : {entityIndices[instance.entity].value} : {frameIndex} : {instance.index} : {transform} : {physicsVelocity} : {physicsVelocities[index]}");
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
                        GameEntityTransform start;
                        double minTime = time - deltaTime;
                        start.elapsedTime = minTime < damageTime ? instanceEx.info.damageTime : math.min((float)(minTime - instance.time), duration);
                        start.value = origin;

                        GameEntityTransform end;
                        end.elapsedTime = duration;
                        end.value = transform;

                        ColliderCastInput colliderCastInput = default;
                        colliderCastInput.Start = start.value.pos;
                        colliderCastInput.End = end.value.pos;
                        colliderCastInput.Orientation = start.value.rot;
                        colliderCastInput.Collider = collider;

                        ColliderCastHitWrapper wrapper;
                        var castCollector = new CastCollector<ColliderCastHit, ColliderCastHitWrapper>(
                            (instanceEx.value.flag & GameActionFlag.DestroyOnHit) == GameActionFlag.DestroyOnHit,
                            //index,
                            instanceEx.camp,
                            instanceEx.value.hitType,
                            instanceEx.value.damageType,
                            instanceEx.info.interval,
                            instanceEx.info.hitDestination,
                            instanceEx.info.impactForce,
                            instanceEx.info.impactTime,
                            instanceEx.info.impactMaxSpeed,
                            instanceEx.info.dot,
                            instanceEx.info.radius,
                            1.0f,
                            //time,
                            (instanceEx.value.flag & GameActionFlag.TargetInAir) == GameActionFlag.TargetInAir ? up : float3.zero,
                            math.normalizesafe(end.value.pos - start.value.pos, instanceEx.direction),
                            entity,
                            start,
                            end,
                            collisionFilter,
                            collisionWorld,
                            instance,
                            camps,
                            masses,
                            ref actionEntities,
                            ref impacts,
                            ref actionManager.hiters, 
                            ref actionManager.damagers, 
                            ref wrapper
#if GAME_DEBUG_COMPARSION
                            , frameIndex, entityIndices
#endif
                            );
                        collisionWorld.CastCollider(colliderCastInput, ref castCollector);
                        isDestroy = castCollector.Apply(out result);

                        /*foreach (var body in collisionWorld.DynamicBodies)
                        {
                            castCollector.log += $"{body}";
                                if(body.Collider.IsCreated)
                                    castCollector.log += $"{body.Collider.Value.Type} : {body.Collider.Value.Filter} : {body.Collider.Value.MassProperties.MassDistribution.Transform}";
                        }
                        
                        UnityEngine.Debug.Log($"1 {entityArray[index]} : {entityIndices[instance.entity].value} : {frameIndex} : {instance.index} : {origin} : {transform} : {physicsVelocity} : {castCollector.log}");*/
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

                        DistanceHitWrapper wrapper;
                        var castCollector = new CastCollector<DistanceHit, DistanceHitWrapper>(
                            (instanceEx.value.flag & GameActionFlag.DestroyOnHit) == GameActionFlag.DestroyOnHit,
                            //index,
                            instanceEx.camp,
                            instanceEx.value.hitType,
                            instanceEx.value.damageType,
                            instanceEx.info.interval,
                            instanceEx.info.hitDestination,
                            instanceEx.info.impactForce,
                            instanceEx.info.impactTime,
                            instanceEx.info.impactMaxSpeed,
                            instanceEx.info.dot,
                            instanceEx.info.radius,
                            distance,
                            //time,
                            (instanceEx.value.flag & GameActionFlag.TargetInAir) == GameActionFlag.TargetInAir ? up : float3.zero,
                            instanceEx.direction,
                            entity,
                            result,
                            result,
                            collisionFilter,
                            collisionWorld,
                            instance,
                            camps,
                            masses,
                            ref actionEntities,
                            ref impacts,
                            ref actionManager.hiters,
                            ref actionManager.damagers,
                            ref wrapper
#if GAME_DEBUG_COMPARSION    
                            , frameIndex, entityIndices
#endif
                            );
                        collisionWorld.CalculateDistance(colliderDistanceInput, ref castCollector);
                        isDestroy = castCollector.Apply(out result);
                        
                        /*foreach (var body in collisionWorld.DynamicBodies)
                        {
                            castCollector.log += $"{body}";
                            if(body.Collider.IsCreated)
                                castCollector.log += $"{body.Collider.Value.Type} : {body.Collider.Value.Filter} : {body.Collider.Value.MassProperties.MassDistribution.Transform}";
                        }
                        
                        UnityEngine.Debug.Log($"1 {entityArray[index]} : {entityIndices[instance.entity].value} : {frameIndex} : {instance.index} : {origin} : {transform} : {physicsVelocity} : {castCollector.log}");*/
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
                    //UnityEngine.Debug.Log($"Destroy {entityArray[index]} : {entityIndices[instance.entity].value} : {frameIndex} : {instance.index} : {transform}");

                    physicsVelocities[index] = default;

                    /*if (index < physicsGravityFactors.Length)
                        physicsGravityFactors[index] = default;*/
                }
                else if (isVelocityDirty)
                    physicsVelocities[index] = physicsVelocity;

                if (isTranslationDirty)
                {
                    //UnityEngine.Debug.Log($"Update {entityArray[index]} : {entityIndices[instance.entity].value} : {frameIndex} : {instance.index} : {translations[index].Value} : {transform.pos}");

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
                    breakCommand.value.hit = 0;
                    breakCommand.value.delayTime = 0.0f;
                    breakCommand.value.alertTime = 0.0f;
                    //breakCommand.value.time = now;
                    breakCommand.value.normal = float3.zero;
                    breakCommands.Create().value = breakCommand;
                }
            }

            /*if ((value & GameActionStatus.Status.Destroy) == GameActionStatus.Status.Destroy)
                handler.Destroy(
                    index,
                    elapsedTime,
                    time,
                    entity,
                    math.RigidTransform(rotations[index].Value, translations[index].Value),
                    instance);*/

            if (status.value != value)
            {
                //int temp = (int)value;
                //UnityEngine.Debug.LogError($"Change {entity.Index} : {entity.Version} : {temp}");

                //UnityEngine.Debug.Log($"Change {entityArray[index]} : {value} : {entityIndices[instance.entity].value} : {frameIndex} : {instance.index} : {translations[index].Value} : {transform.pos}");

                status.value = value;
                status.time = instance.time + elapsedTime;
                results[entity] = status;
            }
        }

        private float3 __CalculateOffset(in Entity entity)
        {
            float3 result = float3.zero;

            var surfaceRotation = surfaces.HasComponent(entity) ? surfaces[entity].rotation : quaternion.identity;
            if (directs.HasComponent(entity))
                result += math.mul(surfaceRotation, directs[entity].value);

            if (indirects.HasComponent(entity))
            {
                var indirect = indirects[entity];

                result += indirect.value + indirect.velocity * deltaTime;
            }

            return result;
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

    [BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Deterministic)]
    public struct PerformEx : IJobChunk
        /*where THandler : struct, IGameEntityActionHandler
        where TFactory : struct, IGameEntityActionFactory<THandler>*/
    {
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
        public ComponentLookup<GameNodeCharacterCollider> colliders;

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

        public SharedHashMap<Entity, float3>.ParallelWriter locations;

        public NativeFactory<Entity>.ParallelWriter unstoppableEntities;

        public NativeFactory<EntityData<GameNodeVelocityComponent>>.ParallelWriter impacts;

        public NativeFactory<EntityData<GameEntityBreakCommand>>.ParallelWriter breakCommands;
        //public EntityCommandQueue<EntityData<GameActionDisabled>>.ParallelWriter entityManager;

        //public TFactory factory;

        public GameEntityActionManager.ParallelWriter actionManager;

#if GAME_DEBUG_COMPARSION
        public uint frameIndex;

        [ReadOnly]
        public ComponentLookup<GameEntityIndex> entityIndices;
#endif

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Perform/*<THandler, TFactory>*/ executor;
            executor.deltaTime = (float)deltaTime;
            executor.now = time;
            executor.gravity = gravity;
            executor.collisionWorld = collisionWorld;
            executor.disabled = disabled;
            executor.surfaces = surfaces;
            executor.directs = directs;
            executor.indirects = indirects;
            executor.actorStates = actorStates;
            executor.colliders = colliders;
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
            executor.locations = locations;
            executor.unstoppableEntities = unstoppableEntities;
            //perform.directVelocities = directVelocities;
            executor.impacts = impacts;
            executor.breakCommands = breakCommands;
            executor.actionManager = actionManager;
            //perform.entityManager = entityManager;
            //executor.handler = factory.Create(chunk);

#if GAME_DEBUG_COMPARSION
            executor.frameIndex = frameIndex;

            executor.entityIndices = entityIndices;
#endif

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                executor.Execute(i);
        }
    }

    private struct ComputeHits
    {
#if GAME_DEBUG_COMPARSION
        public uint frameIndex;

        [ReadOnly]
        public ComponentLookup<GameEntityIndex> entityIndices;
#endif
        
        [ReadOnly]
        public NativeArray<GameActionData> instances;

        [ReadOnly]
        public NativeArray<GameActionDataEx> instancesEx;

        [ReadOnly]
        public NativeArray<GameActionStatus> states;

        [ReadOnly]
        public BufferAccessor<GameActionEntity> entities;

        public ComponentLookup<GameEntityRage> rages;

        public ComponentLookup<GameEntityHit> hits;

        public ComponentLookup<GameEntityActorHit> actorHits;

        public BufferLookup<GameEntityActorHitTarget> actorHitTargets;

        public void Execute(int index)
        {
            var instance = instances[index];

            float hitResult = 0.0f;

            GameEntityHit hit;
            GameEntityActorHit actorHit;
            GameEntityActorHitTarget actorHitTarget;
            GameActionEntity entity;
            GameDeadline time;
            var instanceEx = this.instancesEx[index];
            var entities = this.entities[index];
            int length = entities.Length;
            //string log = instance.entity.ToString();
            for (int i = 0; i < length; ++i)
            {
                entity = entities[i];
                if (entity.delta > math.FLT_MIN_NORMAL)
                {
                    hitResult += entity.delta;

                    //log += entity.ToString();
                    if (actorHits.HasComponent(entity.target))
                    {
                        actorHit = actorHits[entity.target];
                        actorHit.destinationMask |= instanceEx.value.breakMask;
                        
                        ++actorHit.destinationTimes;
                        actorHit.destinationHit += entity.delta;

                        actorHits[entity.target] = actorHit;
                    }

                    if (actorHitTargets.HasBuffer(entity.target))
                    {
                        actorHitTarget.entity = instance.entity;
                        actorHitTargets[entity.target].Add(actorHitTarget);
                    }

                    if (hits.HasComponent(entity.target))
                    {
                        hit = hits[entity.target];
                        hit.delta += entity.delta;
                        hit.value += entity.delta;

                        time = instance.time;
                        time += entity.elaspedTime;
                        hit.time = GameDeadline.Max(hit.time, time);
                        hit.normal += entity.normal;
                        hits[entity.target] = hit;

                        //log += "-hit: " + hit.value + ", time: " + hit.time + ", elapsedTime: " + (instance.time + entity.elaspedTime) + $": {instance.index} : {entityIndices[instance.entity].value} : {entityIndices[entity.target].value} : {frameIndex}";
                    }
                }
            }

            if (rages.HasComponent(instance.entity))
            {
                var rage = rages[instance.entity];
                rage.value += hitResult * instanceEx.info.rageScale;
                rages[instance.entity] = rage;
            }

            if (actorHits.HasComponent(instance.entity))
            {
                actorHit = actorHits[instance.entity];
                actorHit.sourceMask |= instanceEx.value.breakMask;
                actorHit.sourceHit += hitResult;

                var status = states[index].value;
                if ((status & GameActionStatus.Status.Damaged) == GameActionStatus.Status.Damage)
                    ++actorHit.sourceTimes;

                actorHits[instance.entity] = actorHit;
            }
            /*if (log.Length > 1)
                UnityEngine.Debug.Log(log);*/
        }
    }

    [BurstCompile]
    private struct ComputeHitsEx : IJobChunk
    {
#if GAME_DEBUG_COMPARSION
        public uint frameIndex;

        [ReadOnly]
        public ComponentLookup<GameEntityIndex> entityIndices;
#endif

        [ReadOnly]
        public ComponentTypeHandle<GameActionData> instanceType;

        [ReadOnly]
        public ComponentTypeHandle<GameActionDataEx> instanceExType;

        [ReadOnly]
        public ComponentTypeHandle<GameActionStatus> statusType;

        [ReadOnly]
        public BufferTypeHandle<GameActionEntity> entityType;

        public ComponentLookup<GameEntityRage> rages;

        public ComponentLookup<GameEntityHit> hits;

        public ComponentLookup<GameEntityActorHit> actorHits;

        public BufferLookup<GameEntityActorHitTarget> actorHitTargets;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            ComputeHits executor;
            
#if GAME_DEBUG_COMPARSION
            executor.frameIndex = frameIndex;
            executor.entityIndices = entityIndices;
#endif
            executor.instances = chunk.GetNativeArray(ref instanceType);
            executor.instancesEx = chunk.GetNativeArray(ref instanceExType);
            executor.states = chunk.GetNativeArray(ref statusType);
            executor.entities = chunk.GetBufferAccessor(ref entityType);
            executor.rages = rages;
            executor.hits = hits;
            executor.actorHits = actorHits;
            executor.actorHitTargets = actorHitTargets;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                executor.Execute(i);
        }
    }

    [BurstCompile]
    private struct ApplyUnstoppableEntities : IJob
    {
        public NativeFactory<Entity> entities;

        public ComponentLookup<GameNodeCharacterFlag> flags;

        public void Execute()
        {
            Entity entity;
            GameNodeCharacterFlag flag;
            var enumerator = entities.GetEnumerator();
            while (enumerator.MoveNext())
            {
                entity = enumerator.Current;
                if (!flags.HasComponent(entity))
                    continue;

                flag = flags[entity];
                flag.value |= GameNodeCharacterFlag.Flag.Unstoppable;
                flags[entity] = flag;
            }

            entities.Clear();
        }
    }

    [BurstCompile]
    public struct ApplyImpacts : IJob
    {
        public NativeFactory<EntityData<GameNodeVelocityComponent>> sources;

        public BufferLookup<GameNodeVelocityComponent> destinations;

        public void Execute()
        {
            EntityData<GameNodeVelocityComponent> value;
            var enumerator = sources.GetEnumerator();
            while (enumerator.MoveNext())
            {
                value = enumerator.Current;
                if (!destinations.HasBuffer(value.entity))
                    continue;

                destinations[value.entity].Add(value.value);
            }

            sources.Clear();
        }
    }

    [BurstCompile]
    public struct ApplyBreakCommands : IJob
    {
        public NativeFactory<EntityData<GameEntityBreakCommand>> sources;

        public ComponentLookup<GameEntityBreakCommand> destinations;

        public void Execute()
        {
            EntityData<GameEntityBreakCommand> value;
            var enumerator = sources.GetEnumerator();
            while (enumerator.MoveNext())
            {
                value = enumerator.Current;
                if (!destinations.HasComponent(value.entity))
                    continue;

                destinations[value.entity] = value.value;

                destinations.SetComponentEnabled(value.entity, true);
            }

            sources.Clear();
        }
    }

    public EntityQuery group
    {
        get;

        private set;
    }

    //public GameEntityActionEndEntityCommandSystem endFrameBarrier { get; private set; }

    private EntityQuery __physicsStepGroup;

    private GameUpdateTime __time;

    private EntityTypeHandle __entityType;
    private ComponentTypeHandle<GameActionData> __instanceType;
    private ComponentTypeHandle<GameActionStatus> __statusType;

    private ComponentLookup<Disabled> __disabled;
    private ComponentLookup<GameNodeSurface> __surfaces;
    private ComponentLookup<GameNodeDirect> __directs;
    private ComponentLookup<GameNodeIndirect> __indirects;
    private ComponentLookup<GameNodeActorStatus> __actorStates;
    private ComponentLookup<GameNodeCharacterCollider> __colliders;

    private ComponentLookup<GameEntityCamp> __camps;
    private ComponentLookup<GameEntityActorMass> __masses;
    private ComponentLookup<GameEntityActorInfo> __infos;
    private ComponentLookup<GameEntityCommandVersion> __commandVersions;

    private ComponentTypeHandle<GameActionDataEx> __instanceExType;
    private ComponentTypeHandle<Translation> __translationType;
    private ComponentTypeHandle<Rotation> __rotationType;
    private ComponentTypeHandle<PhysicsVelocity> __physicsVelocityType;
    //perform.physicsGravityFactorType = GetComponentTypeHandle<PhysicsGravityFactor>();
    private BufferTypeHandle<GameActionEntity> __actionEntityType;
    private ComponentLookup<GameActionStatus> __results;
    //private ComponentLookup<Translation> __translations;

    private ComponentLookup<GameNodeCharacterFlag> __characterflags;

    private ComponentLookup<GameEntityBreakCommand> __breakCommands;

    private ComponentLookup<GameEntityRage> __rages;
    private ComponentLookup<GameEntityHit> __hits;
    private ComponentLookup<GameEntityActorHit> __actorHits;

    private BufferLookup<GameEntityActorHitTarget> __actorHitTargets;

    private BufferLookup<GameNodeVelocityComponent> __velocities;

    //private EntityCommandPool<EntityData<GameActionDisabled>> __entityManager;

    private SharedPhysicsWorld __physicsWorld;

    private SharedHashMap<Entity, float3> __locations;

    private NativeFactory<Entity> __unstoppableEntities;
    //private NativeQueue<EntityData<float>> __directVelocities;
    private NativeFactory<EntityData<GameNodeVelocityComponent>> __impacts;
    private NativeFactory<EntityData<GameEntityBreakCommand>> __commands;

    public GameEntityActionManager actionManager
    {
        readonly get;

        private set;
    }


    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        using(var builder = new EntityQueryBuilder(Allocator.Temp))
            group = builder
                .WithAll<GameActionData, GameActionDataEx, GameActionEntity>()
                .Build(ref state);// systemState.GetEntityQuery(new List<EntityQueryDesc>(destination).ToArray());

        __physicsStepGroup = state.GetEntityQuery(ComponentType.ReadOnly<Unity.Physics.PhysicsStep>());

        __time = new GameUpdateTime(ref state);

        __entityType = state.GetEntityTypeHandle();
        __instanceType = state.GetComponentTypeHandle<GameActionData>(true);
        __statusType = state.GetComponentTypeHandle<GameActionStatus>(true);

        __disabled = state.GetComponentLookup<Disabled>(true);
        __surfaces = state.GetComponentLookup<GameNodeSurface>(true);
        __directs = state.GetComponentLookup<GameNodeDirect>(true);
        __indirects = state.GetComponentLookup<GameNodeIndirect>(true);
        __actorStates = state.GetComponentLookup<GameNodeActorStatus>(true);
        __colliders = state.GetComponentLookup<GameNodeCharacterCollider>(true);
        __camps = state.GetComponentLookup<GameEntityCamp>(true);
        __masses = state.GetComponentLookup<GameEntityActorMass>(true);
        __infos = state.GetComponentLookup<GameEntityActorInfo>(true);
        __commandVersions = state.GetComponentLookup<GameEntityCommandVersion>(true);

        __instanceExType = state.GetComponentTypeHandle<GameActionDataEx>(true);

        __translationType = state.GetComponentTypeHandle<Translation>();
        __rotationType = state.GetComponentTypeHandle<Rotation>();
        __physicsVelocityType = state.GetComponentTypeHandle<PhysicsVelocity>();
        //perform.physicsGravityFactorType = GetComponentTypeHandle<PhysicsGravityFactor>();
        __actionEntityType = state.GetBufferTypeHandle<GameActionEntity>();
        __results = state.GetComponentLookup<GameActionStatus>();

        //__translations = state.GetComponentLookup<Translation>();

        __characterflags = state.GetComponentLookup<GameNodeCharacterFlag>();

        __breakCommands = state.GetComponentLookup<GameEntityBreakCommand>();

        __rages = state.GetComponentLookup<GameEntityRage>();
        __hits = state.GetComponentLookup<GameEntityHit>();
        __actorHits = state.GetComponentLookup<GameEntityActorHit>();
        __actorHitTargets = state.GetBufferLookup<GameEntityActorHitTarget>();

        __velocities = state.GetBufferLookup<GameNodeVelocityComponent>();

        var world = state.WorldUnmanaged;
        __physicsWorld = world.GetExistingSystemUnmanaged<GamePhysicsWorldBuildSystem>().physicsWorld;

        __locations = world.GetExistingSystemUnmanaged<GameEntityActionLocationSystem>().locations;

        __unstoppableEntities = new NativeFactory<Entity>(Allocator.Persistent, true);
        //__directVelocities = new NativeQueue<EntityData<float>>(Allocator.Persistent);
        __impacts = new NativeFactory<EntityData<GameNodeVelocityComponent>>(Allocator.Persistent, true);
        __commands = new NativeFactory<EntityData<GameEntityBreakCommand>>(Allocator.Persistent, true);

        actionManager = new GameEntityActionManager(Allocator.Persistent);
    }

    //[BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        actionManager.Dispose();

        __unstoppableEntities.Dispose();
        __impacts.Dispose();
        __commands.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        /*var group = this.group;
        if (group.IsEmptyIgnoreFilter)
            return false;*/

        var inputDeps = state.Dependency;

        ref var locationJobManager = ref __locations.lookupJobManager;
        var counter = new NativeArray<int>(1, Allocator.TempJob, NativeArrayOptions.ClearMemory);
        var jobHandle = group.CalculateEntityCountAsync(counter, inputDeps);

        jobHandle = JobHandle.CombineDependencies(jobHandle, locationJobManager.readWriteJobHandle);

        Reset reset;
        reset.count = counter;
        reset.locations = __locations.writer;
        jobHandle = reset.ScheduleByRef(jobHandle);

        var actionManager = this.actionManager;

        var entityType = __entityType.UpdateAsRef(ref state);
        var instanceType = __instanceType.UpdateAsRef(ref state);
        var instanceExType = __instanceExType.UpdateAsRef(ref state);
        var statusType = __statusType.UpdateAsRef(ref state);
        var actionEntityType = __actionEntityType.UpdateAsRef(ref state);

        PerformEx perform;
        perform.deltaTime = __time.delta;
        perform.time = __time.RollbackTime.now;
        perform.gravity = __physicsStepGroup.IsEmptyIgnoreFilter ? Unity.Physics.PhysicsStep.Default.Gravity : __physicsStepGroup.GetSingleton<Unity.Physics.PhysicsStep>().Gravity;
        perform.collisionWorld = __physicsWorld.collisionWorld;
        perform.disabled = __disabled.UpdateAsRef(ref state);
        perform.surfaces = __surfaces.UpdateAsRef(ref state);
        perform.directs = __directs.UpdateAsRef(ref state);
        perform.indirects = __indirects.UpdateAsRef(ref state);
        perform.actorStates = __actorStates.UpdateAsRef(ref state);
        perform.colliders = __colliders.UpdateAsRef(ref state);
        perform.camps = __camps.UpdateAsRef(ref state);
        perform.masses = __masses.UpdateAsRef(ref state);
        perform.infos = __infos.UpdateAsRef(ref state);
        perform.commandVersions = __commandVersions.UpdateAsRef(ref state);
        perform.entityType = entityType;
        perform.instanceType = instanceType;
        perform.instanceExType = instanceExType;
        perform.statusType = statusType;
        perform.translationType = __translationType.UpdateAsRef(ref state);
        perform.rotationType = __rotationType.UpdateAsRef(ref state);
        perform.physicsVelocityType = __physicsVelocityType.UpdateAsRef(ref state);
        //perform.physicsGravityFactorType = GetComponentTypeHandle<PhysicsGravityFactor>();
        perform.actionEntityType = actionEntityType;
        perform.results = __results.UpdateAsRef(ref state);
        perform.locations = __locations.parallelWriter;
        perform.unstoppableEntities = __unstoppableEntities.parallelWriter;
        //perform.directVelocities = __directVelocities.AsParallelWriter();
        perform.impacts = __impacts.parallelWriter;
        perform.breakCommands = __commands.parallelWriter;
        perform.actionManager = actionManager.parallelWriter;

#if GAME_DEBUG_COMPARSION
        perform.frameIndex = __time.RollbackTime.frameIndex;
        perform.entityIndices = state.GetComponentLookup<GameEntityIndex>(true);
#endif

        ref var physicsWorldJobManager = ref __physicsWorld.lookupJobManager;

        jobHandle = JobHandle.CombineDependencies(jobHandle, physicsWorldJobManager.readOnlyJobHandle, actionManager.Resize(group, inputDeps));

        jobHandle = perform.ScheduleParallelByRef(group, jobHandle);

        actionManager.jobHandle = jobHandle;

        physicsWorldJobManager.AddReadOnlyDependency(jobHandle);

        locationJobManager.readWriteJobHandle = jobHandle;

        //var translations = __translations.UpdateAsRef(ref state);

        ComputeHitsEx computeHits;
        
#if GAME_DEBUG_COMPARSION
        computeHits.frameIndex = __time.RollbackTime.frameIndex;
        computeHits.entityIndices = state.GetComponentLookup<GameEntityIndex>(true);
#endif
        
        computeHits.instanceType = instanceType;
        computeHits.instanceExType = instanceExType;
        computeHits.statusType = statusType;
        computeHits.entityType = actionEntityType;
        computeHits.rages = __rages.UpdateAsRef(ref state);
        computeHits.hits = __hits.UpdateAsRef(ref state);
        computeHits.actorHits = __actorHits.UpdateAsRef(ref state);
        computeHits.actorHitTargets = __actorHitTargets.UpdateAsRef(ref state);

        var hitJob = computeHits.ScheduleByRef(group, jobHandle);

        ApplyUnstoppableEntities applyUnstoppableEntities;
        applyUnstoppableEntities.entities = __unstoppableEntities;
        applyUnstoppableEntities.flags = __characterflags.UpdateAsRef(ref state);
        var flagJob = applyUnstoppableEntities.ScheduleByRef(jobHandle);

        ApplyImpacts applyImpacts;
        applyImpacts.sources = __impacts;
        applyImpacts.destinations = __velocities.UpdateAsRef(ref state);
        var impactJob = applyImpacts.ScheduleByRef(jobHandle);

        ApplyBreakCommands applyBreakCommands;
        applyBreakCommands.sources = __commands;
        applyBreakCommands.destinations = __breakCommands.UpdateAsRef(ref state);
        var breakCommandJob = applyBreakCommands.ScheduleByRef(jobHandle);

        jobHandle = JobHandle.CombineDependencies(
            //directVelocityJob, 
            flagJob,
            impactJob,
            breakCommandJob);

        state.Dependency = JobHandle.CombineDependencies(
                jobHandle, 
                hitJob);
        //return true;
    }
}
