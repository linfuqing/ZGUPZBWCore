using Unity.Transforms;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Entities;
using Unity.Physics;
using Unity.Burst;
using Unity.Jobs;
using ZG;

[assembly:RegisterGenericJobType(typeof(RollbackEntryTest<GameRollbackEntryTester>))]

/*public struct GameRollbackObject : IComponentData
{
}*/

public struct GameRollbackBVH
{
    private BoundingVolumeHierarchyLiteEx __value;
    //private NativeHashMapLite<Entity, int> __rigidbodyIndices;

    public NativeArray<RigidBody> rigidbodies => __value.rigidbodies;

    public GameRollbackBVH(Allocator allocator, in CollisionWorldContainer collisionWorld)
    {
        __value = collisionWorld.CreateBoundingVolumeHierarchyDynamic(allocator);
        //__rigidbodyIndices = new NativeHashMapLite<Entity, int>(1, allocator);

        //__value.CreateRigidbodyIndices(__rigidbodyIndices);
    }

    public void Dispose()
    {
        __value.Dispose();
        //__rigidbodyIndices.Dispose();
    }

    public int GetRigidbodyIndex(in Entity entity)
    {
        //始终用初始化数据，否则有可能不同步。这里需要再深入思考
        /*if (__rigidbodyIndices.TryGetValue(entity, out int rigidbodyIndex))
            return rigidbodyIndex;*/

        return -1;
    }

    public unsafe bool CalculateDistance(in ColliderDistanceInput input)
    {
        if(input.Collider->CollisionType == CollisionType.Composite)
        {
            ColliderDistanceInput childInput = default;
            childInput.MaxDistance = input.MaxDistance;

            var compoundCollider = (CompoundCollider*)input.Collider;
            var children = compoundCollider->Children;
            int numChildren = compoundCollider->NumChildren;
            for(int i = 0; i < numChildren; ++i)
            {
                ref var child = ref children[i];

                childInput.Transform = math.mul(input.Transform, child.CompoundFromChild);
                childInput.Collider = child.Collider;
                if (CalculateDistance(childInput))
                    return true;
            }

            return false;
        }

        return __value.CalculateDistance(input);
    }

    public bool CalculateDistance<T>(in ColliderDistanceInput input, ref T collector) where T : struct, ICollector<DistanceHit>
    {
        return __value.CalculateDistance(input, ref collector);
    }
}

public struct GameRollbackEntryTester : IRollbackEntryTester
{
    public float collisionTolerance;

    [ReadOnly]
    public SharedHashMap<uint, GameRollbackBVH>.Reader bvhs;

    [ReadOnly]
    public ComponentLookup<Translation> translations;

    [ReadOnly]
    public ComponentLookup<Rotation> rotations;

    [ReadOnly]
    public ComponentLookup<PhysicsCollider> colliders;

    public unsafe bool Test(uint frameIndex, in Entity entity)
    {
        if (!bvhs.TryGetValue(frameIndex, out var bvh))
            return false;

        int rigidbodyIndex = bvh.GetRigidbodyIndex(entity);
        if (rigidbodyIndex == -1)
        {
            if (!colliders.HasComponent(entity))
                return false;

            var collider = colliders[entity].Value;
            if (!collider.IsCreated)
                return false;

            ColliderDistanceInput colliderDistanceInput = default;
            colliderDistanceInput.Collider = (Collider*)collider.GetUnsafePtr();
            colliderDistanceInput.Transform = math.RigidTransform(rotations[entity].Value, translations[entity].Value);
            colliderDistanceInput.MaxDistance = collisionTolerance;

            return bvh.CalculateDistance(colliderDistanceInput);
        }
        else
        {
            var rigidbody = bvh.rigidbodies[rigidbodyIndex];
            if (!rigidbody.Collider.IsCreated)
                return false;

            var collector = new ClosestHitCollectorExclude<DistanceHit>(rigidbodyIndex, collisionTolerance);

            ColliderDistanceInput colliderDistanceInput = default;
            colliderDistanceInput.Collider = (Collider*)rigidbody.Collider.GetUnsafePtr();
            colliderDistanceInput.Transform = rigidbody.WorldFromBody;
            colliderDistanceInput.MaxDistance = collisionTolerance;

            return bvh.CalculateDistance(colliderDistanceInput, ref collector);
        }
    }
}

[AutoCreateIn("Client"), 
    BurstCompile, 
    CreateAfter(typeof(RollbackCommandSystem)),
    //CreateAfter(typeof(GameBVHRollbackSystem)),
    UpdateInGroup(typeof(TimeSystemGroup)), /*UpdateAfter(typeof(GameRollbackCommandSystemHybrid)), */UpdateBefore(typeof(GameSyncSystemGroup))]
/*#if !USING_NETCODE
[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
#endif*/
public partial struct GameRollbackCommandSystem : ISystem
{
    public static readonly int InnerloopBatchCount = 1;
    public static readonly uint MaxRestoreFrameCount = 8;
    public static readonly float CollisionTolerance = 0.5f;

/*#if GAME_DEBUG_COMPARSION
#else
    private EntityQuery __frameSyncFlagGroup;
#endif*/
    private EntityQuery __frameGroup;

    private ComponentLookup<Translation> __translations;

    private ComponentLookup<Rotation> __rotations;

    private ComponentLookup<PhysicsCollider> __colliders;

    private RollbackCommanderManaged __commander;

    private SharedHashMap<uint, GameRollbackBVH> __bvhs;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        //BurstUtility.InitializeJobParalledForDefer<RollbackEntryTest<GameRollbackEntryTester>>();

        /*#if GAME_DEBUG_COMPARSION
        #else
                __frameSyncFlagGroup = state.GetEntityQuery(ComponentType.ReadOnly<FrameSyncFlag>());
        #endif*/

        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __frameGroup = builder
                    .WithAll<RollbackFrame, RollbackFrameClear>()
                    .WithOptions(EntityQueryOptions.IncludeSystems)
                    .Build(ref state);

        //__frameClearGroup = state.GetEntityQuery(ComponentType.ReadOnly<RollbackFrameClear>());

        __translations = state.GetComponentLookup<Translation>(true);
        __rotations = state.GetComponentLookup<Rotation>(true);
        __colliders = state.GetComponentLookup<PhysicsCollider>(true);

        var world = state.WorldUnmanaged;
        __commander = world.GetExistingSystemUnmanaged<RollbackCommandSystem>().commander;

        var systemHandle = world.GetExistingUnmanagedSystem<GameBVHRollbackSystem>();
        __bvhs = systemHandle == SystemHandle.Null ? default : world.GetExistingSystemUnmanaged<GameBVHRollbackSystem>().bvhs;
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {

    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var jobHandle = state.Dependency;
        jobHandle = __commander.Update(__frameGroup.GetSingleton<RollbackFrameClear>().maxIndex, jobHandle);

        if (__bvhs.isCreated)
        {
            GameRollbackEntryTester tester;
            tester.collisionTolerance = CollisionTolerance;
            tester.bvhs = __bvhs.reader;
            tester.translations = __translations.UpdateAsRef(ref state);
            tester.rotations = __rotations.UpdateAsRef(ref state);
            tester.colliders = __colliders.UpdateAsRef(ref state);

            ref var lookupJobManager = ref __bvhs.lookupJobManager;

            uint frameIndex = __frameGroup.GetSingleton<RollbackFrame>().index;
            jobHandle = __commander.Test(
                tester,
#if GAME_DEBUG_COMPARSION
            0, 
#else
                frameIndex > MaxRestoreFrameCount/* && __frameSyncFlagGroup.GetSingleton<FrameSyncFlag>().isClear*/ ? frameIndex - MaxRestoreFrameCount : 0,
#endif
                InnerloopBatchCount,
                JobHandle.CombineDependencies(lookupJobManager.readOnlyJobHandle, jobHandle));

            lookupJobManager.AddReadOnlyDependency(jobHandle);
        }

        state.Dependency = jobHandle;
    }
}

[BurstCompile,
    CreateBefore(typeof(GameRollbackCommandSystem)),
    CreateAfter(typeof(GamePhysicsWorldBuildSystem)),
    CreateAfter(typeof(RollbackSystemGroup)),
    UpdateInGroup(typeof(RollbackSystemGroup)), AutoCreateIn("Client")]
public partial struct GameBVHRollbackSystem : ISystem, IRollbackCore
{
    [BurstCompile]
    private struct Save : IJob
    {
        public uint frameIndex;

        [ReadOnly]
        public CollisionWorldContainer collisionWorld;

        public SharedHashMap<uint, GameRollbackBVH>.Writer bvhs;

        public void Execute()
        {
            bvhs.Add(frameIndex, new GameRollbackBVH(Allocator.Persistent, collisionWorld));
        }
    }

    [BurstCompile]
    private struct Clear : IJob
    {
        public uint frameIndex;
        public uint frameCount;

        public SharedHashMap<uint, GameRollbackBVH>.Writer bvhs;

        public void Execute()
        {
            uint index;
            GameRollbackBVH bvh;
            for (uint i = 0; i < frameCount; ++i)
            {
                index = frameIndex + i;
                if(bvhs.TryGetValue(index, out bvh))
                {
                    bvh.Dispose();

                    bvhs.Remove(index);
                }
            }
        }
    }

    //public readonly float CollisionTolerance = 0.5f;

    private bool __isCompleted;
    private SharedPhysicsWorld __physicsWorld;
    //private RollbackCommanderManaged __commander;
    private RollbackManager __manager;

    public SharedHashMap<uint, GameRollbackBVH> bvhs
    {
        get;

        private set;
    }

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        BurstUtility.InitializeJob<Save>();
        BurstUtility.InitializeJob<Clear>();

        var world = state.WorldUnmanaged;

        __physicsWorld = world.GetExistingSystemUnmanaged<GamePhysicsWorldBuildSystem>().physicsWorld;

        var containerManager = world.GetExistingSystemUnmanaged<RollbackSystemGroup>().containerManager;

        __manager = containerManager.CreateManager(ref state);

        var bvhs = new SharedHashMap<uint, GameRollbackBVH>(Allocator.Persistent);

        containerManager.Manage(new GameBVHRollbackUtility.Container(bvhs));

        this.bvhs = bvhs;
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {

    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        __isCompleted = false;

        __manager.Update(ref state, ref this);
    }

    public void ScheduleRestore(uint frameIndex, ref SystemState systemState)
    {
    }

    public void ScheduleSave(uint frameIndex, in EntityTypeHandle entityType, ref SystemState systemState)
    {
        Save save;
        save.frameIndex = frameIndex;
        save.bvhs = bvhs.writer;
        save.collisionWorld = __physicsWorld.collisionWorld;

        ref var boundingVolumeHierarchyJobManager = ref __GetBoundingVolumeHierarchyJobManager();

        ref var physicsWorldJobManager = ref __physicsWorld.lookupJobManager;

        var jobHandle = save.Schedule(JobHandle.CombineDependencies(systemState.Dependency, physicsWorldJobManager.readOnlyJobHandle));

        physicsWorldJobManager.AddReadOnlyDependency(jobHandle);

        boundingVolumeHierarchyJobManager.readWriteJobHandle = jobHandle;

        systemState.Dependency = jobHandle;
    }

    public void ScheduleClear(uint maxFrameIndex, uint frameIndex, uint frameCount, ref SystemState systemState)
    {
        Clear clear;
        clear.frameIndex = frameIndex;
        clear.frameCount = frameCount;
        clear.bvhs = bvhs.writer;

        ref var boundingVolumeHierarchyJobManager = ref __GetBoundingVolumeHierarchyJobManager();

        var jobHandle = clear.Schedule(systemState.Dependency);

        boundingVolumeHierarchyJobManager.readWriteJobHandle = jobHandle;

        systemState.Dependency = jobHandle;
    }

    private ref LookupJobManager __GetBoundingVolumeHierarchyJobManager()
    {
        ref var lookupJobManager = ref bvhs.lookupJobManager;

        if (!__isCompleted)
        {
            __isCompleted = true;

            lookupJobManager.CompleteReadWriteDependency();
        }

        return ref lookupJobManager;
    }
}

[BurstCompile]
internal static class GameBVHRollbackUtility
{
    private class ContainerClear
    {

    }

    private class ContainerDispose
    {

    }

    public struct Container : IRollbackContainer
    {
        private SharedHashMap<uint, GameRollbackBVH> __bvhs;

        public Container(SharedHashMap<uint, GameRollbackBVH> bvhs)
        {
            __bvhs = bvhs;
        }

        public void Clear()
        {
            __bvhs.lookupJobManager.CompleteReadWriteDependency();

            foreach (var bvh in __bvhs)
                bvh.Value.Dispose();

            __bvhs.writer.Clear();
        }

        public void Dispose()
        {
            __bvhs.lookupJobManager.CompleteReadWriteDependency();

            __bvhs.Dispose();
        }
    }


    [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.AfterAssembliesLoaded)]
    public static void Init()
    {
        RollbackUtility.GetClearFunction<Container>() = FunctionWrapperUtility.CompileManagedFunctionPointer<RollbackContainerDelegate>(__ClearContainer);
        RollbackUtility.GetDisposeFunction<Container>() = FunctionWrapperUtility.CompileManagedFunctionPointer<RollbackContainerDelegate>(__DisposeContainer);
    }

    //[BurstCompile]
    [AOT.MonoPInvokeCallback(typeof(RollbackContainerDelegate))]
    private static void __ClearContainer(in UnsafeBlock value)
    {
        value.As<Container>().Clear();
    }

    //[BurstCompile]
    [AOT.MonoPInvokeCallback(typeof(RollbackContainerDelegate))]
    private static void __DisposeContainer(in UnsafeBlock value)
    {
        value.As<Container>().Dispose();
    }
}

/*[UpdateInGroup(typeof(TimeSystemGroup)), UpdateBefore(typeof(GameSyncSystemGroup))]
public class GameRollbackCommandSystemHybrid : EntityCommandSystemHybrid
{
}*/
