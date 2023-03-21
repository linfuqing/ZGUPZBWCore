using Unity.Jobs;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Jobs;
using Cinemachine;
using ZG;

[assembly: RegisterGenericJobType(typeof(PhysicsCameraApply<GamePhysicsCameraHandler>))]

public struct GamePhysicsCameraHandler : IPhysicsCameraHandler
{
    [ReadOnly]
    public ComponentLookup<LocalToWorld> localToWorlds;

    public bool TryGetTargetPosition(
        in Entity entity,
        in Unity.Physics.CollisionWorld collisionWorld,
        out float3 position)
    {
        position = default;

        int rigidbodyIndex = collisionWorld.GetRigidBodyIndex(entity);
        if (rigidbodyIndex == -1)
            return false;

        var rigidbody = collisionWorld.Bodies[rigidbodyIndex];
        if (!rigidbody.Collider.IsCreated)
            return false;

        position = localToWorlds.HasComponent(entity) ? localToWorlds[entity].Position : rigidbody.WorldFromBody.pos;

        position.y += rigidbody.Collider.Value.CalculateAabb().Center.y;

        return true;
    }
}

[RequireComponent(typeof(PhysicsCameraComponent))]
[EntityComponent(typeof(Transform))]
[EntityComponent(typeof(Translation))]
public class GamePhysicsCinemachine : CinemachineExtension
{
    [SerializeField]
    internal GameObjectEntity _target;

    private PhysicsCameraComponent __physicsCameraComponent;

    private GamePhysicsCinemachineSystemGroup __systemGroup;

    public GameObjectEntity target
    {
        set
        {
            __physicsCameraComponent.target = value == null ? Entity.Null : value.entity;

            _target = value;
        }
    }

    public void UpdateCamera()
    {
        if (__physicsCameraComponent == null)
            return;

        __physicsCameraComponent.camera = __physicsCameraComponent.camera;
    }

    protected override void Awake()
    {
        base.Awake();

        __physicsCameraComponent = GetComponent<PhysicsCameraComponent>();

        __systemGroup = __physicsCameraComponent.world.GetExistingSystemManaged<GamePhysicsCinemachineSystemGroup>();

        if (_target == null)
        {
            var lookAt = VirtualCamera.LookAt;

            _target = lookAt == null ? null : lookAt.GetComponent<GameObjectEntity>();
        }
    }

    protected void Start()
    {
        if (_target != null)
            __physicsCameraComponent.target = _target.entity;
    }

    protected override void PostPipelineStageCallback(
        CinemachineVirtualCameraBase vcam, 
        CinemachineCore.Stage stage, 
        ref CameraState state, 
        float deltaTime)
    {
#if UNITY_EDITOR
        if (!Application.isPlaying)
            return;
#endif

        if (stage == CinemachineCore.Stage.Body)
        {
            if (__systemGroup != null)
                __systemGroup.Update();

            state.RawPosition = __physicsCameraComponent.position;
            state.PositionCorrection = __physicsCameraComponent.displacement;
        }
    }
}

[UpdateInGroup(typeof(PresentationSystemGroup))]
public partial class GamePhysicsCinemachineSystemGroup : ComponentSystemGroup
{
    private int __frameCount;

    protected override void OnUpdate()
    {
        int frameCount = UnityEngine.Time.frameCount;
        if (frameCount == __frameCount)
            return;

        base.OnUpdate();

        __frameCount = frameCount;
    }
}

[UpdateInGroup(typeof(GamePhysicsCinemachineSystemGroup))]
public partial class GamePhysicsCinemachineTransformSystem : SystemBase
{
    [BurstCompile]
    private struct CopyFrom : IJobParallelForTransform
    {
        public NativeArray<float3> positions;

        public void Execute(int index, TransformAccess transform)
        {
            positions[index] = transform.isValid ? (float3)transform.position : float3.zero;
        }
    }

    [BurstCompile]
    private struct CopyTo : IJobChunk
    {
        [ReadOnly, DeallocateOnJobCompletion]
        public NativeArray<int> baseEntityIndexArray;

        [ReadOnly, DeallocateOnJobCompletion]
        public NativeArray<float3> positions;

        public ComponentTypeHandle<Translation> translationType;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            var translations = chunk.GetNativeArray(ref translationType);
            Translation translation;
            int index = baseEntityIndexArray[unfilteredChunkIndex];
            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
            {
                translation.Value = positions[index++];
                translations[i] = translation;
            }
        }
    }

    private EntityQuery __group;
    private TransformAccessArrayEx __transformAccessArray;

    protected override void OnCreate()
    {
        base.OnCreate();

        __group = GetEntityQuery(
            ComponentType.ReadOnly<PhysicsCameraDisplacement>(), 
            ComponentType.ReadWrite<Translation>(),
            TransformAccessArrayEx.componentType);

        __transformAccessArray = new TransformAccessArrayEx(__group);
    }

    protected override void OnDestroy()
    {
        __transformAccessArray.Dispose();

        base.OnDestroy();
    }

    protected override void OnUpdate()
    {
        var transformAccessArray = __transformAccessArray.Convert(this);

        var positions = new NativeArray<float3>(transformAccessArray.length, Allocator.TempJob);

        var inputDeps = Dependency;

        CopyFrom copyFrom;
        copyFrom.positions = positions;
        var jobHandle = copyFrom.ScheduleReadOnly(transformAccessArray, 1, inputDeps);

        CopyTo copyTo;
        copyTo.baseEntityIndexArray = __group.CalculateBaseEntityIndexArrayAsync(Allocator.TempJob, inputDeps, out var baseEntityIndexJobHandle);
        copyTo.positions = positions;
        copyTo.translationType = GetComponentTypeHandle<Translation>();

        Dependency = copyTo.ScheduleParallel(__group, JobHandle.CombineDependencies(baseEntityIndexJobHandle, jobHandle));
    }
}

[BurstCompile, UpdateInGroup(typeof(GamePhysicsCinemachineSystemGroup)), UpdateAfter(typeof(GamePhysicsCinemachineTransformSystem))]
public partial struct GamePhysicsCinemachineSystem : ISystem
{
    private PhysicsCameraSystemCore __core;
    private SharedPhysicsWorld __sharedPhysicsWorld;

    public void OnCreate(ref SystemState state)
    {
        __core = new PhysicsCameraSystemCore(ref state);

        __sharedPhysicsWorld = state.World.GetOrCreateSystemUnmanaged<GamePhysicsWorldBuildSystem>().physicsWorld;
    }

    public void OnDestroy(ref SystemState state)
    {

    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        GamePhysicsCameraHandler handler;
        handler.localToWorlds = state.GetComponentLookup<LocalToWorld>(true);

        ref var lookupJobManager = ref __sharedPhysicsWorld.lookupJobManager;

        state.Dependency = JobHandle.CombineDependencies(state.Dependency, lookupJobManager.readOnlyJobHandle);
        __core.Update(handler, __sharedPhysicsWorld.collisionWorld, ref state);

        lookupJobManager.AddReadOnlyDependency(state.Dependency);
    }
}
