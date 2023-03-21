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

[UpdateInGroup(typeof(FixedStepSimulationSystemGroup)), UpdateBefore(typeof(EndFramePhysicsSystem)), UpdateAfter(typeof(ExportPhysicsWorld))]
public partial class GameRidigbodySystemGroup : ComponentSystemGroup
{

}

[BurstCompile, UpdateInGroup(typeof(GameRidigbodySystemGroup), OrderFirst = true)]
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

    public void OnCreate(ref SystemState state)
    {
        __groupToCreateMasses = state.GetEntityQuery(
            ComponentType.ReadOnly<GameRidigbodyMass>(),
            ComponentType.Exclude<PhysicsMass>());

        __groupToCreateOrigins = state.GetEntityQuery(
            ComponentType.ReadOnly<GameRidigbodyData>(), 
            ComponentType.Exclude<GameRidigbodyOrigin>());

        __groupToDestroy = state.GetEntityQuery(
            new EntityQueryDesc()
            {
                All = new ComponentType[]
                {
                    ComponentType.ReadOnly<Translation>(), 
                    ComponentType.ReadOnly<Rotation>(), 
                    ComponentType.ReadOnly<GameRidigbodyOrigin>()
                }, 
                None = new ComponentType[]
                {
                    typeof(GameRidigbodyData)
                }
            }, 
            new EntityQueryDesc()
            {
                All = new ComponentType[]
                {
                    ComponentType.ReadOnly<GameRidigbodyOrigin>(), 
                    ComponentType.ReadOnly<Disabled>()
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
        var entityManager = state.EntityManager;

        int count = __groupToCreateMasses.CalculateEntityCount();
        if (count > 0)
        {
            using (var physicsMasses = new NativeArray<PhysicsMass>(count, Allocator.TempJob))
            {
                state.CompleteDependency();

                InitMasses init;
                init.colliderType = state.GetComponentTypeHandle<PhysicsShapeCompoundCollider>(true);
                init.masseType = state.GetComponentTypeHandle<GameRidigbodyMass>(true);
                init.baseEntityIndexArray = __groupToCreateMasses.CalculateBaseEntityIndexArray(Allocator.TempJob);
                init.physicsMasses = physicsMasses;
                init.Run(__groupToCreateMasses);

                entityManager.AddComponentDataBurstCompatible(__groupToCreateMasses, physicsMasses);
            }
        }

        count = __groupToCreateOrigins.CalculateEntityCount();
        if (count > 0)
        {
            using (var origins = new NativeArray<GameRidigbodyOrigin>(count, Allocator.TempJob))
            {
                state.CompleteDependency();

                InitOrigins init;
                init.translationType = state.GetComponentTypeHandle<Translation>(true);
                init.rotationType = state.GetComponentTypeHandle<Rotation>(true);
                init.baseEntityIndexArray = __groupToCreateOrigins.CalculateBaseEntityIndexArray(Allocator.TempJob);
                init.origins = origins;
                init.Run(__groupToCreateOrigins);

                entityManager.AddComponentDataBurstCompatible(__groupToCreateOrigins, origins);
            }
        }

        entityManager.RemoveComponent<GameRidigbodyOrigin>(__groupToDestroy);
    }
}

[BurstCompile, UpdateInGroup(typeof(GameRidigbodySystemGroup))]
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
    private SharedPhysicsWorld __physicsWorld;

    public void OnCreate(ref SystemState state)
    {
        __group = state.GetEntityQuery(
            ComponentType.ReadOnly<GameRidigbodyData>(),
            ComponentType.ReadOnly<GameRidigbodyOrigin>(),
            ComponentType.ReadWrite<Rotation>(), 
            ComponentType.ReadWrite<Translation>());

        __physicsWorld = state.World.GetOrCreateSystemUnmanaged<GamePhysicsWorldBuildSystem>().physicsWorld;
    }

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
        reset.entityType = state.GetEntityTypeHandle();
        reset.instanceType = state.GetComponentTypeHandle<GameRidigbodyData>(true);
        reset.originType = state.GetComponentTypeHandle<GameRidigbodyOrigin>(true);
        reset.rotationType = state.GetComponentTypeHandle<Rotation>();
        reset.translationType = state.GetComponentTypeHandle<Translation>();

        ref var lookupJobManager = ref __physicsWorld.lookupJobManager;

        var jobHandle = reset.ScheduleParallel(__group, JobHandle.CombineDependencies(lookupJobManager.readOnlyJobHandle, state.Dependency));

        lookupJobManager.AddReadOnlyDependency(jobHandle);

        state.Dependency = jobHandle;
    }
}