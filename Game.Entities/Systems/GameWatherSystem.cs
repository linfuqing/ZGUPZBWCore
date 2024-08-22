using Unity.Jobs;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Physics;
using ZG;
using Random = Unity.Mathematics.Random;

[BurstCompile, UpdateInGroup(typeof(GameUpdateSystemGroup)),
    //UpdateBefore(typeof(EndFramePhysicsSystem)),
    UpdateAfter(typeof(GameEntityActionSystemGroup))
    /*UpdateAfter(typeof(GameEntityActionEndEntityCommandSystemGroup))*/]
public partial struct GameWatcherSystem : ISystem
{
    public struct Collector : ICollector<DistanceHit>
    {
        public float3 position; 
        public CollisionFilter raycastFilter;
        public GameActionTargetType type;
        public GameEntityNode node;

        [ReadOnly]
        public CollisionWorld collisionWorld;

        [ReadOnly]
        public ComponentLookup<GameEntityCamp> camps;
        
        public bool EarlyOutOnFirstHit => false;

        public int NumHits { get; private set; }

        public float MaxFraction { get; private set; }

        public GameEntityNode result { get; private set; }

        public Collector(
            GameActionTargetType type,
            float maxFraction,
            in float3 position, 
            in CollisionFilter raycastFilter, 
            in GameEntityNode node,
            in CollisionWorld collisionWorld, 
            in ComponentLookup<GameEntityCamp> camps)
        {
            this.type = type;
            this.position = position;
            this.raycastFilter = raycastFilter;
            this.node = node;
            this.collisionWorld = collisionWorld;
            this.camps = camps;

            NumHits = 0;

            MaxFraction = maxFraction;

            result = default;
        }

        public bool AddHit(DistanceHit hit)
        {
            RigidBody rigidbody = collisionWorld.Bodies[hit.RigidBodyIndex];
            bool isContains = camps.HasComponent(rigidbody.Entity);
            if (isContains)
            {
                GameEntityNode node;
                node.camp = camps[rigidbody.Entity].value;
                node.entity = rigidbody.Entity;
                isContains = this.node.Predicate(type, node);
                if (isContains)
                {
                    if (!raycastFilter.IsEmpty)
                    {
                        RaycastInput raycastInput = default;
                        raycastInput.Filter = raycastFilter;
                        raycastInput.Start = position;
                        raycastInput.End = hit.Position;
                        isContains = !collisionWorld.CastRay(raycastInput);
                    }

                    if (isContains)
                        result = node;
                }
            }

            if (isContains)
            {
                MaxFraction = hit.Fraction;

                ++NumHits;

                return true;
            }

            return false;
        }
    }

    private struct Watch
    {
        public double time;

        public Random random;

        [ReadOnly]
        public CollisionWorld collisionWorld;

        [ReadOnly]
        public NativeArray<Entity> entityArray;

        [ReadOnly]
        public ComponentLookup<Disabled> disabled;

        [ReadOnly]
        public ComponentLookup<GameEntityCamp> campMap;
        
        [ReadOnly]
        public NativeArray<GameEntityCamp> camps;

        [ReadOnly]
        public NativeArray<GameWatcherData> instances;
        
        public NativeArray<GameWatcherInfo> infos;

        public unsafe void Execute(int index)
        {
            var info = infos[index];
            if (info.time/* + instance.time*/ > time/* && isExists*/)
                return;

            var instance = instances[index];
            if (!instance.collider.IsCreated)
            {
                UnityEngine.Debug.LogError("Watcher's Collider Invail!");

                return;
            }

            int rigidbodyIndex = collisionWorld.GetRigidBodyIndex(entityArray[index]);
            if (rigidbodyIndex == -1)
            {
                UnityEngine.Debug.LogError("Watcher's Rigidbody is invailed!");

                return;
            }

            var rigidbody = collisionWorld.Bodies[rigidbodyIndex];
            GameEntityNode source, destination = default;
            source.camp = camps[index].value;
            source.entity = rigidbody.Entity;

            bool isExists = !disabled.HasComponent(info.target) && campMap.HasComponent(info.target);
            if (isExists)
            {
                destination.camp = campMap[info.target].value;
                destination.entity = info.target;

                isExists = source.Predicate(instance.type, destination);
            }
            
            GameEntityNode node;
            GameActionTargetType type;
            if (isExists)
            {
                node = destination;

                type = GameActionTargetType.Self | GameActionTargetType.Ally;

                info.type = GameWatcherInfo.Type.Camp;
            }
            else
            {
                node = source;

                type = instance.type;

                info.type = GameWatcherInfo.Type.Main;
            }

            /*if (!instance.collider.IsCreated)
                return;*/
            
            ColliderDistanceInput colliderDistanceInput = default;
            colliderDistanceInput.MaxDistance = instance.contactTolerance;
            colliderDistanceInput.Transform = rigidbody.WorldFromBody;
            colliderDistanceInput.Collider = (Collider*)instance.collider.GetUnsafePtr();

            var filter = colliderDistanceInput.Collider->Filter;
            filter.CollidesWith = (uint)(int)instance.raycastMask;
            var collector = new Collector(
                type, 
                instance.contactTolerance,
                math.transform(rigidbody.WorldFromBody, instance.eye),
                filter, 
                node,
                collisionWorld,
                campMap);
            collisionWorld.CalculateDistance(colliderDistanceInput, ref collector);

            info.target = collector.result.entity;
            info.time = time + random.NextFloat(instance.minTime, instance.maxTime);
            infos[index] = info;
        }
    }

    [BurstCompile]
    private struct WatchEx : IJobChunk
    {
        public double time;

        [ReadOnly]
        public CollisionWorldContainer collisionWorld;

        [ReadOnly]
        public EntityTypeHandle entityType;

        [ReadOnly]
        public ComponentLookup<Disabled> disabled;

        [ReadOnly]
        public ComponentLookup<GameEntityCamp> camps;
        
        [ReadOnly]
        public ComponentTypeHandle<GameEntityCamp> campType;

        [ReadOnly]
        public ComponentTypeHandle<GameWatcherData> instanceType;
        
        public ComponentTypeHandle<GameWatcherInfo> infoType;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            long hash = math.aslong(time);

            Watch watch;
            watch.time = time;
            watch.random = new Random((uint)hash ^ (uint)(hash >> 32));
            watch.collisionWorld = collisionWorld;
            watch.entityArray = chunk.GetNativeArray(entityType);
            watch.disabled = disabled;
            watch.campMap = camps;
            watch.camps = chunk.GetNativeArray(ref campType);
            watch.instances = chunk.GetNativeArray(ref instanceType);
            watch.infos = chunk.GetNativeArray(ref infoType);

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                watch.Execute(i);
        }
    }

    private EntityQuery __group;

    public SharedPhysicsWorld __physicsWorld;

    public void OnCreate(ref SystemState state)
    {
        __group = state.GetEntityQuery(
            ComponentType.ReadOnly<GameWatcherData>(),
            ComponentType.ReadWrite<GameWatcherInfo>(), 
            ComponentType.Exclude<Disabled>());

        __physicsWorld = state.World.GetOrCreateSystemUnmanaged<GamePhysicsWorldBuildSystem>().physicsWorld;
    }

    public void OnDestroy(ref SystemState state)
    {

    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (__group.IsEmptyIgnoreFilter)
            return;

        WatchEx watch;
        watch.time = state.WorldUnmanaged.Time.ElapsedTime;
        watch.collisionWorld = __physicsWorld.collisionWorld;
        watch.entityType = state.GetEntityTypeHandle();
        watch.disabled = state.GetComponentLookup<Disabled>(true);
        watch.camps = state.GetComponentLookup<GameEntityCamp>(true);
        watch.campType = state.GetComponentTypeHandle<GameEntityCamp>(true);
        watch.instanceType = state.GetComponentTypeHandle<GameWatcherData>(true);
        watch.infoType = state.GetComponentTypeHandle<GameWatcherInfo>();

        ref var lookupJobManager = ref __physicsWorld.lookupJobManager;

        var jobHandle = watch.ScheduleParallel(__group, JobHandle.CombineDependencies(lookupJobManager.readOnlyJobHandle, state.Dependency));

        lookupJobManager.AddReadOnlyDependency(jobHandle);

        state.Dependency = jobHandle;
    }
}