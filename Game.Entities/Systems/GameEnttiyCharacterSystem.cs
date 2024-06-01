using Unity.Jobs;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Entities;
using Unity.Collections;
using Unity.Physics;
using ZG;

[BurstCompile, UpdateInGroup(typeof(GameNodeCharacterSystemGroup))/*, UpdateAfter(typeof(GameNodeCharacterSystemGroup))*/]
public partial struct GameEntityCharacterSystem : ISystem
{
    private struct CalculateHits
    {
        public float deltaTime;

        public double time;

        [ReadOnly]
        public CollisionWorld collisionWorld;
        [ReadOnly] 
        public ComponentLookup<GameEntityCamp> campMap;
        [ReadOnly]
        public NativeArray<Entity> entityArray;
        [ReadOnly] 
        public NativeArray<GameEntityCamp> camps;
        [ReadOnly]
        public BufferAccessor<GameEntityCharacterHit> instances;
        [ReadOnly]
        public BufferAccessor<GameNodeCharacterDistanceHit> distanceHits;

        public BufferLookup<GameEntityHealthDamage> healthDamages;

        public unsafe void Execute(int index)
        {
            var rigidbodies = collisionWorld.Bodies;
            var instances = this.instances[index];
            var distanceHits = this.distanceHits[index];
            GameEntityCharacterHit instance;
            GameNodeCharacterDistanceHit distanceHit;
            GameEntityHealthDamage healthDamage;
            RigidBody rigidbody;
            ChildCollider leaf;
            Entity entity = entityArray[index];
            float hit;
            uint belongsTo;
            int i, j, numInstance = instances.Length, numDistanceHits = distanceHits.Length, camp = camps[index].value;
            for (i = 0; i < numDistanceHits; ++i)
            {
                distanceHit = distanceHits[i];
                rigidbody = rigidbodies[distanceHit.value.RigidBodyIndex];
                if (!healthDamages.HasBuffer(rigidbody.Entity) || 
                    !campMap.HasComponent(rigidbody.Entity) || 
                    campMap[rigidbody.Entity].value == camp)
                    continue;

                ref var collider = ref rigidbody.Collider.Value;
                belongsTo = collider.GetLeaf(distanceHit.value.ColliderKey, out leaf) ? leaf.Collider->Filter.BelongsTo : collider.Filter.BelongsTo;
                hit = 0.0f;
                for (j = 0; j < numInstance; ++j)
                {
                    instance = instances[j];
                    if ((((uint)instance.layerMask.value) & belongsTo) != 0)
                        hit += instance.value;
                }

                if (hit != 0.0f)
                {
                    healthDamage.value = hit * deltaTime;
                    healthDamage.time = time;
                    healthDamage.entity = entity;

                    healthDamages[rigidbody.Entity].Add(healthDamage);
                }
            }
        }
    }

    [BurstCompile]
    private struct CalculateHitsEx : IJobChunk
    {
        public float deltaTime;

        public double time;

        [ReadOnly]
        public CollisionWorldContainer collisionWorld;
        [ReadOnly]
        public ComponentLookup<GameEntityCamp> camps;
        [ReadOnly]
        public EntityTypeHandle entityType;
        [ReadOnly]
        public ComponentTypeHandle<GameEntityCamp> campType;
        [ReadOnly]
        public BufferTypeHandle<GameEntityCharacterHit> instanceType;
        [ReadOnly]
        public BufferTypeHandle<GameNodeCharacterDistanceHit> distanceHitType;

        public BufferLookup<GameEntityHealthDamage> healthDamages;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            CalculateHits calculateHits;
            calculateHits.deltaTime = deltaTime;
            calculateHits.time = time;
            calculateHits.collisionWorld = collisionWorld;
            calculateHits.campMap = camps;
            calculateHits.entityArray = chunk.GetNativeArray(entityType);
            calculateHits.camps = chunk.GetNativeArray(ref campType);
            calculateHits.instances = chunk.GetBufferAccessor(ref instanceType);
            calculateHits.distanceHits = chunk.GetBufferAccessor(ref distanceHitType);
            calculateHits.healthDamages = healthDamages;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                calculateHits.Execute(i);
        }
    }

    private EntityQuery __group;

    private ComponentLookup<GameEntityCamp> __camps;
    
    private EntityTypeHandle __entityType;
    private ComponentTypeHandle<GameEntityCamp> __campType;
    private BufferTypeHandle<GameEntityCharacterHit> __instanceType;
    private BufferTypeHandle<GameNodeCharacterDistanceHit> __distanceHitType;

    private BufferLookup<GameEntityHealthDamage> __healthDamages;

    private SharedPhysicsWorld __physicsWorld;
    
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __group = builder
                .WithAll<GameEntityCharacterHit, GameNodeCharacterDistanceHit>()
                .Build(ref state);

        __camps = state.GetComponentLookup<GameEntityCamp>(true);
        
        __entityType = state.GetEntityTypeHandle();
        __campType = state.GetComponentTypeHandle<GameEntityCamp>(true);
        __instanceType = state.GetBufferTypeHandle<GameEntityCharacterHit>(true);
        __distanceHitType = state.GetBufferTypeHandle<GameNodeCharacterDistanceHit>(true);
        __healthDamages = state.GetBufferLookup<GameEntityHealthDamage>();

        __physicsWorld = state.World.GetOrCreateSystemUnmanaged<GamePhysicsWorldBuildSystem>().physicsWorld;
    }

    public void OnDestroy(ref SystemState state)
    {

    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        ref readonly var time = ref state.WorldUnmanaged.Time;

        CalculateHitsEx calculateHits;
        calculateHits.deltaTime = time.DeltaTime;
        calculateHits.time = time.ElapsedTime;
        calculateHits.collisionWorld = __physicsWorld.collisionWorld;
        calculateHits.camps = __camps.UpdateAsRef(ref state);
        calculateHits.entityType = __entityType.UpdateAsRef(ref state);
        calculateHits.campType = __campType.UpdateAsRef(ref state);
        calculateHits.instanceType = __instanceType.UpdateAsRef(ref state);
        calculateHits.distanceHitType = __distanceHitType.UpdateAsRef(ref state);
        calculateHits.healthDamages = __healthDamages.UpdateAsRef(ref state);

        ref var lookupJobManager = ref __physicsWorld.lookupJobManager;

        var jobHandle = calculateHits.ScheduleByRef(__group, JobHandle.CombineDependencies(lookupJobManager.readOnlyJobHandle, state.Dependency));

        lookupJobManager.AddReadOnlyDependency(jobHandle);

        state.Dependency = jobHandle;
    }
}
