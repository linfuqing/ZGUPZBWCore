using Unity.Jobs;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Transforms;
using Unity.Physics;
using Unity.Physics.Systems;
using ZG;

[BurstCompile, UpdateInGroup(typeof(InitializationSystemGroup))]
public partial struct GameRidigbodyFactorySystem : ISystem
{
    [BurstCompile]
    private struct InitMasses : IJobChunk
    {
        [ReadOnly]
        public ComponentTypeHandle<PhysicsShapeCompoundCollider> colliderType;

        [ReadOnly]
        public ComponentTypeHandle<GameRidigbodyMass> masseType;

        [ReadOnly, DeallocateOnJobCompletion]
        public NativeArray<int> baseEntityIndexArray;

        public NativeArray<PhysicsMass> physicsMasses;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            var colliders = chunk.GetNativeArray(ref colliderType);
            var masses = chunk.GetNativeArray(ref masseType);

            int index = baseEntityIndexArray[unfilteredChunkIndex];
            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                physicsMasses[index++] = PhysicsMass.CreateDynamic(colliders[i].value.Value.MassProperties, masses[i].value);
        }
    }

    [BurstCompile]
    private struct InitOrigins : IJobChunk
    {
        [ReadOnly]
        public ComponentTypeHandle<Translation> translationType;
        [ReadOnly]
        public ComponentTypeHandle<Rotation> rotationType;

        [ReadOnly, DeallocateOnJobCompletion]
        public NativeArray<int> baseEntityIndexArray;

        public NativeArray<GameRidigbodyOrigin> origins;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            var translations = chunk.GetNativeArray(ref translationType);
            var rotations = chunk.GetNativeArray(ref rotationType);

            int index = baseEntityIndexArray[unfilteredChunkIndex];
            GameRidigbodyOrigin origin;
            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
            {
                origin.transform = math.RigidTransform(rotations[i].Value, translations[i].Value);
                origins[index++] = origin;
            }
        }
    }

    private EntityQuery __groupToCreateMasses;
    private EntityQuery __groupToCreateOrigins;
    private EntityQuery __groupToDestroy;

    private ComponentTypeHandle<PhysicsShapeCompoundCollider> __colliderType;
    private ComponentTypeHandle<GameRidigbodyMass> __masseType;

    private ComponentTypeHandle<Translation> __translationType;
    private ComponentTypeHandle<Rotation> __rotationType;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __groupToCreateMasses = builder
                    .WithAll<GameRidigbodyMass>()
                    .WithNone<PhysicsMass>()
                    .Build(ref state);

        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __groupToCreateOrigins = builder
                .WithAll<GameRidigbodyData>()
                .WithNone<GameRidigbodyOrigin>()
                .Build(ref state);

        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __groupToDestroy = builder
                .WithAll<Translation, Rotation, GameRidigbodyOrigin>()
                .WithNone<GameRidigbodyData>()
                .AddAdditionalQuery()
                .WithAll<GameRidigbodyOrigin, Disabled>()
                .WithOptions(EntityQueryOptions.IncludeDisabledEntities)
                .Build(ref state);

        __colliderType = state.GetComponentTypeHandle<PhysicsShapeCompoundCollider>(true);
        __masseType = state.GetComponentTypeHandle<GameRidigbodyMass>(true);

        __translationType = state.GetComponentTypeHandle<Translation>(true);
        __rotationType = state.GetComponentTypeHandle<Rotation>(true);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {

    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var entityManager = state.EntityManager;

        int count = __groupToCreateMasses.CalculateEntityCount();
        if (count > 0)
        {
            using (var physicsMasses = new NativeArray<PhysicsMass>(count, Allocator.TempJob))
            {
                state.CompleteDependency();

                InitMasses init;
                init.colliderType = __colliderType.UpdateAsRef(ref state);
                init.masseType = __masseType.UpdateAsRef(ref state);
                init.baseEntityIndexArray = __groupToCreateMasses.CalculateBaseEntityIndexArray(Allocator.TempJob);
                init.physicsMasses = physicsMasses;
                init.RunByRef(__groupToCreateMasses);

                entityManager.AddComponentData(__groupToCreateMasses, physicsMasses);
            }
        }

        count = __groupToCreateOrigins.CalculateEntityCount();
        if (count > 0)
        {
            using (var origins = new NativeArray<GameRidigbodyOrigin>(count, Allocator.TempJob))
            {
                state.CompleteDependency();

                InitOrigins init;
                init.translationType = __translationType.UpdateAsRef(ref state);
                init.rotationType = __rotationType.UpdateAsRef(ref state);
                init.baseEntityIndexArray = __groupToCreateOrigins.CalculateBaseEntityIndexArray(Allocator.TempJob);
                init.origins = origins;
                init.RunByRef(__groupToCreateOrigins);

                entityManager.AddComponentData(__groupToCreateOrigins, origins);
            }
        }

        entityManager.RemoveComponent<GameRidigbodyOrigin>(__groupToDestroy);
    }
}

[BurstCompile, 
    CreateAfter(typeof(GamePhysicsWorldBuildSystem)), 
    UpdateInGroup(typeof(FixedStepSimulationSystemGroup)), 
    UpdateBefore(typeof(EndFramePhysicsSystem)), 
    UpdateAfter(typeof(ExportPhysicsWorld))]
public partial struct GameRidigbodySystem : ISystem
{
    private struct Reset
    {
        public uint waterLayerMask;
        public float waterHeight;

        [ReadOnly]
        public CollisionWorld collisionWorld;

        [ReadOnly]
        public NativeArray<Entity> entityArray;
        [ReadOnly]
        public NativeArray<GameRidigbodyData> instances;
        [ReadOnly]
        public NativeArray<GameRidigbodyOrigin> origins;
        public NativeArray<Rotation> rotations;
        public NativeArray<Translation> translations;

        public unsafe void Execute(int index)
        {
            int rigidbodyIndex = collisionWorld.GetRigidBodyIndex(entityArray[index]);
            if (rigidbodyIndex == -1)
                return;

            var rigidbodies = collisionWorld.Bodies;
            var rigidbody = rigidbodies[rigidbodyIndex];
            if (!rigidbody.Collider.IsCreated)
                return;

            var origin = origins[index];
            var filter = rigidbody.Collider.Value.Filter;
            filter.CollidesWith = waterLayerMask | (uint)instances[index].layerMask.value;

            float3 position = math.float3(rigidbody.WorldFromBody.pos.x, origin.transform.pos.y, rigidbody.WorldFromBody.pos.z);
            RaycastInput raycastInput = default;
            raycastInput.Filter = filter;
            raycastInput.Start = position;
            raycastInput.End = position + math.up() * waterHeight;
            if(collisionWorld.CastRay(raycastInput, out var closestRayHit))
            {
                ref var collider = ref rigidbodies[closestRayHit.RigidBodyIndex].Collider.Value;

                uint layerMask = collider.GetLeaf(closestRayHit.ColliderKey, out var leaf) ? leaf.Collider->Filter.BelongsTo : collider.Filter.BelongsTo;
                if ((layerMask & waterLayerMask) != 0)
                {
                    Translation translation;
                    translation.Value = closestRayHit.Position;
                    translations[index] = translation;

                    return;
                }
            }

            ColliderCastInput colliderCastInput = default;
            int memorySize = rigidbody.Collider.Value.MemorySize;
            byte* bytes = stackalloc byte[memorySize];
            Unity.Collections.LowLevel.Unsafe.UnsafeUtility.MemCpy(bytes, rigidbody.Collider.GetUnsafePtr(), memorySize);
            colliderCastInput.Collider = (Collider*)bytes;
            colliderCastInput.Collider->Filter = filter;
            colliderCastInput.Orientation = rigidbody.WorldFromBody.rot;
            colliderCastInput.Start = origin.transform.pos;
            colliderCastInput.End = rigidbody.WorldFromBody.pos;
            
            if(collisionWorld.CastCollider(colliderCastInput, out var closestHit) && closestHit.Fraction < 1.0f)
            {
                position = math.lerp(origin.transform.pos, rigidbody.WorldFromBody.pos, closestHit.Fraction);
                Aabb aabb = rigidbody.CalculateAabb(math.RigidTransform(quaternion.identity, position - rigidbody.WorldFromBody.pos));
                if (!aabb.Overlaps(rigidbody.CalculateAabb()))
                {
                    Rotation rotation;
                    rotation.Value = rigidbody.WorldFromBody.rot;
                    rotations[index] = rotation;

                    Translation translation;
                    translation.Value = position;
                    translations[index] = translation;
                }
            }
        }
    }

    [BurstCompile]
    private struct ResetEx : IJobChunk
    {
        public uint waterLayerMask;
        public float waterHeight;

        [ReadOnly]
        public CollisionWorldContainer collisionWorld;

        [ReadOnly]
        public EntityTypeHandle entityType;
        [ReadOnly]
        public ComponentTypeHandle<GameRidigbodyData> instanceType;
        [ReadOnly]
        public ComponentTypeHandle<GameRidigbodyOrigin> originType;
        public ComponentTypeHandle<Rotation> rotationType;
        public ComponentTypeHandle<Translation> translationType;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Reset reset;
            reset.waterHeight = waterHeight;
            reset.waterLayerMask = waterLayerMask;
            reset.collisionWorld = collisionWorld;
            reset.entityArray = chunk.GetNativeArray(entityType);
            reset.instances = chunk.GetNativeArray(ref instanceType);
            reset.origins = chunk.GetNativeArray(ref originType);
            reset.rotations = chunk.GetNativeArray(ref rotationType);
            reset.translations = chunk.GetNativeArray(ref translationType);

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                reset.Execute(i);
        }
    }

    public static readonly uint waterLayerMask = 1 << 4;
    public static readonly float waterHeight = 50.0f;

    private EntityQuery __group;

    private EntityTypeHandle __entityType;
    private ComponentTypeHandle<GameRidigbodyData> __instanceType;
    private ComponentTypeHandle<GameRidigbodyOrigin> __originType;
    private ComponentTypeHandle<Rotation> __rotationType;
    private ComponentTypeHandle<Translation> __translationType;

    private SharedPhysicsWorld __physicsWorld;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __group = builder
                    .WithAll<GameRidigbodyData, GameRidigbodyOrigin>()
                    .WithAllRW<Rotation, Translation>()
                    .Build(ref state);

        __entityType = state.GetEntityTypeHandle();
        __instanceType = state.GetComponentTypeHandle<GameRidigbodyData>(true);
        __originType = state.GetComponentTypeHandle<GameRidigbodyOrigin>(true);
        __rotationType = state.GetComponentTypeHandle<Rotation>();
        __translationType = state.GetComponentTypeHandle<Translation>();

        __physicsWorld = state.WorldUnmanaged.GetExistingSystemUnmanaged<GamePhysicsWorldBuildSystem>().physicsWorld;
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {

    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        ResetEx reset;
        reset.waterLayerMask = waterLayerMask;
        reset.waterHeight = waterHeight;
        reset.collisionWorld = __physicsWorld.collisionWorld;
        reset.entityType = __entityType.UpdateAsRef(ref state);
        reset.instanceType = __instanceType.UpdateAsRef(ref state);
        reset.originType = __originType.UpdateAsRef(ref state);
        reset.rotationType = __rotationType.UpdateAsRef(ref state);
        reset.translationType = __translationType.UpdateAsRef(ref state);

        ref var lookupJobManager = ref __physicsWorld.lookupJobManager;

        var jobHandle = reset.ScheduleParallelByRef(__group, JobHandle.CombineDependencies(lookupJobManager.readOnlyJobHandle, state.Dependency));

        lookupJobManager.AddReadOnlyDependency(jobHandle);

        state.Dependency = jobHandle;
    }
}