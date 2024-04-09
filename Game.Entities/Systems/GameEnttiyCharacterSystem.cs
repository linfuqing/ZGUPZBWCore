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
        public NativeArray<Entity> entityArray;
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
            int numInstance = instances.Length, numDistanceHits = distanceHits.Length, i, j;
            for (i = 0; i < numDistanceHits; ++i)
            {
                distanceHit = distanceHits[i];
                rigidbody = rigidbodies[distanceHit.value.RigidBodyIndex];
                if (!healthDamages.HasBuffer(rigidbody.Entity))
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
        public EntityTypeHandle entityType;
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
            calculateHits.entityArray = chunk.GetNativeArray(entityType);
            calculateHits.instances = chunk.GetBufferAccessor(ref instanceType);
            calculateHits.distanceHits = chunk.GetBufferAccessor(ref distanceHitType);
            calculateHits.healthDamages = healthDamages;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                calculateHits.Execute(i);
        }
    }

    private EntityQuery __group;
    private SharedPhysicsWorld __physicsWorld;

    public void OnCreate(ref SystemState state)
    {
        __group = state.GetEntityQuery(
            ComponentType.ReadOnly<GameEntityCharacterHit>(),
            ComponentType.ReadOnly<GameNodeCharacterDistanceHit>());

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
        calculateHits.entityType = state.GetEntityTypeHandle();
        calculateHits.instanceType = state.GetBufferTypeHandle<GameEntityCharacterHit>(true);
        calculateHits.distanceHitType = state.GetBufferTypeHandle<GameNodeCharacterDistanceHit>(true);
        calculateHits.healthDamages = state.GetBufferLookup<GameEntityHealthDamage>();

        ref var lookupJobManager = ref __physicsWorld.lookupJobManager;

        var jobHandle = calculateHits.Schedule(__group, JobHandle.CombineDependencies(lookupJobManager.readOnlyJobHandle, state.Dependency));

        lookupJobManager.AddReadOnlyDependency(jobHandle);

        state.Dependency = jobHandle;
    }
}
