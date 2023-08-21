using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Entities;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Jobs;
using ZG;

[BurstCompile, UpdateInGroup(typeof(GameNodeInitSystemGroup), OrderFirst = true)]
public partial struct GameNodeShapeInitSystem : ISystem
{
    private struct Init
    {
        [ReadOnly]
        public NativeArray<PhysicsShapeCompoundCollider> colliders;

        public NativeArray<GameNodeShpaeDefault> shapes;

        public NativeArray<PhysicsMass> masses;

        public void Execute(int index)
        {
            var collider = colliders[index].value;
            if (!collider.IsCreated)
                return;

            GameNodeShpaeDefault shape;
            shape.mass = PhysicsMass.CreateKinematic(collider.Value.MassProperties);
            shape.mass.InverseInertia = float3.zero;
            shape.collider.Value = collider;
            shapes[index] = shape;

            if (index < masses.Length)
                masses[index] = shape.mass;
        }
    }

    [BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Deterministic)]
    private struct InitEx : IJobChunk
    {
        [ReadOnly]
        public ComponentTypeHandle<PhysicsShapeCompoundCollider> colliderType;

        public ComponentTypeHandle<GameNodeShpaeDefault> shapeType;

        public ComponentTypeHandle<PhysicsMass> massType;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Init init;
            init.colliders = chunk.GetNativeArray(ref colliderType);
            init.shapes = chunk.GetNativeArray(ref shapeType);
            init.masses = chunk.GetNativeArray(ref massType);

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                init.Execute(i);
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
                    ComponentType.ReadOnly<PhysicsShapeCompoundCollider>(), 
                    ComponentType.ReadWrite<GameNodeShpaeDefault>()
                }, 
                Options = EntityQueryOptions.IncludeDisabledEntities
            });

        __group.SetChangedVersionFilter(typeof(PhysicsShapeCompoundCollider));
    }

    public void OnDestroy(ref SystemState state)
    {

    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        InitEx init;
        init.colliderType = state.GetComponentTypeHandle<PhysicsShapeCompoundCollider>(true);
        init.shapeType = state.GetComponentTypeHandle<GameNodeShpaeDefault>();
        init.massType = state.GetComponentTypeHandle<PhysicsMass>();
        state.Dependency = init.ScheduleParallel(__group, state.Dependency);
    }
}

[BurstCompile, UpdateInGroup(typeof(GameNodeInitSystemGroup))]
public partial struct GameNodeShapeSystem : ISystem
{
    private struct Change
    {
        [ReadOnly]
        public BufferAccessor<GameNodeShpae> shapes;

        [ReadOnly]
        public NativeArray<Entity> entityArray;

        [ReadOnly]
        public NativeArray<GameNodeShpaeDefault> defaultShapes;

        [ReadOnly]
        public NativeArray<GameNodeStatus> states;

        [ReadOnly]
        public NativeArray<PhysicsCollider> physicsColliders;

        public NativeArray<PhysicsMass> physicsMasses;

        public NativeArray<PhysicsGravityFactor> physicsGravityFactors;

        [NativeDisableContainerSafetyRestriction]
        public ComponentLookup<PhysicsCollider> physicsColliderMap;

#if GAME_DEBUG_COMPARSION
        public uint frameIndex;

        [ReadOnly]
        public NativeArray<GameEntityIndex> entityIndices;
#endif

        public void Execute(int index)
        {
#if GAME_DEBUG_COMPARSION
            //UnityEngine.Debug.Log($"Node Shape {entityIndices[index].value} : {entityArray[index].Index} : {states[index].value} : {frameIndex}");
#endif

            var shapes = this.shapes[index];
            GameNodeShpae shape;
            PhysicsGravityFactor physicsGravityFactor;
            int status = states[index].value, length = shapes.Length, i;
            for(i = 0; i < length; ++i)
            {
                shape = shapes[i];
                if (shape.status == status)
                {
                    if (shape.collider.Value != physicsColliders[index].Value)
                        physicsColliderMap[entityArray[index]] = shape.collider;

                    physicsMasses[index] = shape.mass;

                    if (index < physicsGravityFactors.Length)
                    {
                        physicsGravityFactor.Value = shape.mass.InverseMass > math.FLT_MIN_NORMAL ? 1.0f : 0.0f;
                        physicsGravityFactors[index] = physicsGravityFactor;
                    }

                    break;
                }
            }
            
            if (i == length)
            {
                var defaultShape = defaultShapes[index];

                if (defaultShape.collider.Value != physicsColliders[index].Value)
                    physicsColliderMap[entityArray[index]] = defaultShape.collider;

                physicsMasses[index] = defaultShape.mass;

                if (index < physicsGravityFactors.Length)
                {
                    physicsGravityFactor.Value = defaultShape.mass.InverseMass > math.FLT_MIN_NORMAL ? 1.0f : 0.0f;
                    physicsGravityFactors[index] = physicsGravityFactor;
                }
            }
        }
    }

    [BurstCompile]
    private struct ChangeEx : IJobChunk
    {
        [ReadOnly]
        public BufferTypeHandle<GameNodeShpae> shapeType;

        [ReadOnly]
        public EntityTypeHandle entityType;

        [ReadOnly]
        public ComponentTypeHandle<GameNodeShpaeDefault> defaultShapeType;

        [ReadOnly]
        public ComponentTypeHandle<GameNodeStatus> statusType;

        [ReadOnly]
        public ComponentTypeHandle<PhysicsCollider> physicsColliderType;

        public ComponentTypeHandle<PhysicsMass> physicsMassType;

        public ComponentTypeHandle<PhysicsGravityFactor> physicsGravityFactorType;

        [NativeDisableContainerSafetyRestriction]
        public ComponentLookup<PhysicsCollider> physicsColliderMap;

#if GAME_DEBUG_COMPARSION
        public uint frameIndex;

        [ReadOnly]
        public ComponentTypeHandle<GameEntityIndex> entityIndexType;
#endif

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Change change;
            change.shapes = chunk.GetBufferAccessor(ref shapeType);
            change.entityArray = chunk.GetNativeArray(entityType);
            change.defaultShapes = chunk.GetNativeArray(ref defaultShapeType);
            change.states = chunk.GetNativeArray(ref statusType);
            change.physicsColliders = chunk.GetNativeArray(ref physicsColliderType);
            change.physicsMasses = chunk.GetNativeArray(ref physicsMassType);
            change.physicsGravityFactors = chunk.GetNativeArray(ref physicsGravityFactorType);
            change.physicsColliderMap = physicsColliderMap;

#if GAME_DEBUG_COMPARSION
            change.frameIndex = frameIndex;
            change.entityIndices = chunk.GetNativeArray(ref entityIndexType);
#endif

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                change.Execute(i);
        }
    }

    private EntityQuery __group;

#if GAME_DEBUG_COMPARSION
    private GameRollbackFrame __frame;
#endif

    public void OnCreate(ref SystemState state)
    {
        __group = state.GetEntityQuery(
            ComponentType.ReadOnly<GameNodeShpae>(),
            ComponentType.ReadOnly<GameNodeShpaeDefault>(),
            ComponentType.ReadOnly<GameNodeStatus>(),
            ComponentType.ReadOnly<PhysicsCollider>(),
            ComponentType.ReadWrite<PhysicsMass>(),
            ComponentType.ReadWrite<PhysicsGravityFactor>(), 
            ComponentType.Exclude<Disabled>());

        __group.SetChangedVersionFilter(new ComponentType[]
            {
                typeof(GameNodeStatus),
                typeof(PhysicsCollider)
            });

#if GAME_DEBUG_COMPARSION
        __frame = new GameRollbackFrame(ref state);
#endif
    }

    public void OnDestroy(ref SystemState state)
    {

    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        ChangeEx change;
        change.entityType = state.GetEntityTypeHandle();
        change.shapeType = state.GetBufferTypeHandle<GameNodeShpae>(true);
        change.defaultShapeType = state.GetComponentTypeHandle<GameNodeShpaeDefault>(true);
        change.statusType = state.GetComponentTypeHandle<GameNodeStatus>(true);
        change.physicsColliderType = state.GetComponentTypeHandle<PhysicsCollider>(true);
        change.physicsMassType = state.GetComponentTypeHandle<PhysicsMass>();
        change.physicsGravityFactorType = state.GetComponentTypeHandle<PhysicsGravityFactor>();
        change.physicsColliderMap = state.GetComponentLookup<PhysicsCollider>();

#if GAME_DEBUG_COMPARSION
        change.frameIndex = __frame.index;
        change.entityIndexType = state.GetComponentTypeHandle<GameEntityIndex>(true);
#endif

        state.Dependency = change.ScheduleParallel(__group, state.Dependency);
    }
}
