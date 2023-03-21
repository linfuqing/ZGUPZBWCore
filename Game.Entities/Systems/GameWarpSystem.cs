using Unity.Mathematics;
using Unity.Collections;
using Unity.Entities;
using Unity.Physics;
using ZG;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Jobs;

public struct GameWarperData : IComponentData
{
    public int maxTimes;
    public uint ignoreMask;
    public uint positionMask;
}

public struct GameWarper : IComponentData
{
    public float radius;
    public float height;
    public float3 position;
    public CallbackHandle<float3> callbackHandle;
}

[BurstCompile, UpdateInGroup(typeof(GameUpdateSystemGroup)), UpdateAfter(typeof(GameAreaSystem))]
public partial struct GameWarperSystem : ISystem
{
    public struct Result
    {
        public Entity entity;
        public float3 position;
        public CallbackHandle<float3> callbackHandle;
    }

    private struct Warp
    {
        public int maxTimes;
        public uint ignoreMask;
        public uint positionMask;

        public Random random;

        [ReadOnly]
        public CollisionWorld world;

        [ReadOnly]
        public NativeArray<Entity> entityArray;

        [ReadOnly]
        public NativeArray<GameWarper> warpers;

        public EntityCommandQueue<Result>.ParallelWriter entityManager;

        public void Execute(int index)
        {
            var warper = warpers[index];

            Result result;
            result.entity = entityArray[index];

            result.position = SamplePosition(
                maxTimes,
                ignoreMask,
                positionMask,
                warper.radius, 
                warper.height, 
                warper.position,
                world,
                ref random);

            result.callbackHandle = warper.callbackHandle;

            entityManager.Enqueue(result);
        }
    }

    [BurstCompile]
    private struct WarpEx : IJobChunk, IEntityCommandProducerJob
    {
        public int maxTimes;
        public uint ignoreMask;
        public uint positionMask;

        public double time;

        [ReadOnly]
        public CollisionWorldContainer world;

        [ReadOnly]
        public EntityTypeHandle entityType;

        [ReadOnly]
        public ComponentTypeHandle<GameWarper> warperType;

        public EntityCommandQueue<Result>.ParallelWriter entityManager;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Warp warp;
            warp.maxTimes = maxTimes;
            warp.ignoreMask = ignoreMask;
            warp.positionMask = positionMask;
            long hash = math.aslong(time);
            warp.random = new Random((uint)(((int)(hash >> 32)) ^ (int)hash ^ unfilteredChunkIndex));
            warp.world = world;
            warp.entityArray = chunk.GetNativeArray(entityType);
            warp.warpers = chunk.GetNativeArray(ref warperType);
            warp.entityManager = entityManager;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                warp.Execute(i);
        }
    }

    public static unsafe float3 SamplePosition(
        int maxTimes,
        uint ignoreMask,
        uint positionMask,
        float radius, 
        float height, 
        in float3 position, 
        in CollisionWorld world, 
        ref Random random)
    {
        height = height > math.FLT_MIN_NORMAL ? height : radius;

        float2 point;
        float3 result = position;
        RaycastInput raycastInput = default;
        raycastInput.Filter = CollisionFilter.Default;
        raycastInput.Filter.CollidesWith = ~ignoreMask;
        for (int i = 0; i < maxTimes; ++i)
        {
            point = math.float2(
                    position.x + random.NextFloat(-radius, radius),
                    position.z + random.NextFloat(-radius, radius));

            raycastInput.Start = math.float3(point.x, position.y + height, point.y);
            raycastInput.End = math.float3(point.x, position.y - height, point.y);
            if (world.CastRay(raycastInput, out var raycastHit))
            {
                ref var collider = ref world.Bodies[raycastHit.RigidBodyIndex].Collider.Value;
                if (collider.GetLeaf(raycastHit.ColliderKey, out var leaf))
                    collider = ref *leaf.Collider;

                if ((collider.Filter.BelongsTo & positionMask) != 0)
                    return raycastHit.Position;
            }
        }

        UnityEngine.Debug.LogError("Sample Position Fail.");

        return position;
    }

    /*public int maxTimes;
    public uint ignoreMask;
    public uint positionMask;*/

    //private JobHandle __jobHandle;
    private EntityQuery __instanceGroup;
    private EntityQuery __group;
    private SharedPhysicsWorld __physicsWorld;
    private EntityCommandPool<Result> __entityManager;

    public float3 SamplePosition(
        float radius, 
        float height,
        double elapsedTime, 
        in float3 position)
    {
        var instance = __instanceGroup.GetSingleton<GameWarperData>();

        __physicsWorld.lookupJobManager.CompleteReadOnlyDependency();

        long hash = math.aslong(elapsedTime);
        var random = new Random((uint)(((int)(hash >> 32)) ^ (int)hash));
        return SamplePosition(
            instance.maxTimes,
            instance.ignoreMask,
            instance.positionMask,
            radius, 
            height, 
            position,
            __physicsWorld.collisionWorld,
            ref random);
    }

    public void OnCreate(ref SystemState state)
    {
        __instanceGroup = state.GetEntityQuery(ComponentType.ReadOnly<GameWarperData>());

        __group = state.GetEntityQuery(new EntityQueryDesc()
        {
            All = new ComponentType[]
            {
                ComponentType.ReadOnly<GameWarper>()
            },
            Options = EntityQueryOptions.IncludeDisabledEntities
        });

        state.RequireForUpdate(__group);

        World world = state.World;
        __physicsWorld = world.GetOrCreateSystemUnmanaged<GamePhysicsWorldBuildSystem>().physicsWorld;
        //__endFrameBarrier = world.GetOrCreateSystem<EndTimeSystemGroupEntityCommandSystem>();

        __entityManager = world.GetOrCreateSystemManaged<GameWarperCallbackSystem>().pool;
    }

    public void OnDestroy(ref SystemState state)
    {
    }
    
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var entityManager = __entityManager.Create();

        var instance = __instanceGroup.GetSingleton<GameWarperData>();

        WarpEx warp;
        warp.maxTimes = instance.maxTimes;
        warp.ignoreMask = instance.ignoreMask;
        warp.positionMask = instance.positionMask;
        warp.time = state.WorldUnmanaged.Time.ElapsedTime;
        warp.world = __physicsWorld.collisionWorld;
        warp.entityType = state.GetEntityTypeHandle();
        warp.warperType = state.GetComponentTypeHandle<GameWarper>(true);
        warp.entityManager = entityManager.parallelWriter;

        ref var lookupJobManager = ref __physicsWorld.lookupJobManager;

        var jobHandle = warp.ScheduleParallel(__group, JobHandle.CombineDependencies(lookupJobManager.readOnlyJobHandle, state.Dependency));

        lookupJobManager.AddReadOnlyDependency(jobHandle);

        entityManager.AddJobHandleForProducer<WarpEx>(jobHandle);

        state.Dependency = jobHandle;

        //Dependency = __endFrameBarrier.RemoveComponent<GameWarper>(__group, __jobHandle);
    }
}

[UpdateInGroup(typeof(TimeSystemGroup)), UpdateAfter(typeof(GameSyncSystemGroup))]
public partial class GameWarperCallbackSystem : SystemBase//EntityCommandSystemHybrid
{
    private GameUpdateTime __time;

    private EntityCommandPool<GameWarperSystem.Result>.Context __context;

    public EntityCommandPool<GameWarperSystem.Result> pool => __context.pool;

    protected override void OnCreate()
    {
        base.OnCreate();

        __context = new EntityCommandPool<GameWarperSystem.Result>.Context(Allocator.Persistent);

        __time = new GameUpdateTime(ref this.GetState());
    }

    protected override void OnDestroy()
    {
        __context.Dispose();

        base.OnDestroy();
    }

    protected override void OnUpdate()
    {
        if (!__time.IsVail())
            return;

        var entities = new NativeList<Entity>(Allocator.Temp);
        while (__context.TryDequeue(out var result))
        {
            entities.Add(result.entity);

            result.callbackHandle.InvokeAndUnregister(result.position);
        }

        EntityManager.RemoveComponent<GameWarper>(entities.AsArray());

        entities.Dispose();
    }
}