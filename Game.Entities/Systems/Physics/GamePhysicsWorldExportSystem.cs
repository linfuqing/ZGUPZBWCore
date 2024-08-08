using System.Reflection;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Entities;
using Unity.Transforms;
using Unity.Physics;
using Unity.Physics.Systems;
using Unity.Mathematics;

[UpdateInGroup(typeof(FixedStepSimulationSystemGroup)), UpdateBefore(typeof(EndFramePhysicsSystem)), UpdateAfter(typeof(ExportPhysicsWorld))]
public partial class GamePhysicsWorldExportSystem : SystemBase
{
    [BurstCompile]
    private struct ExportDynamicBodies : IJobParallelFor
    {
        [ReadOnly]
        public NativeArray<RigidBody> dynaimicBodies;
        [ReadOnly]
        public NativeArray<MotionVelocity> motionVelocities;
        [ReadOnly]
        public NativeArray<MotionData> motionDatas;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<Translation> positions;
        [NativeDisableParallelForRestriction]
        public ComponentLookup<Rotation> rotations;
        [NativeDisableParallelForRestriction]
        public ComponentLookup<PhysicsVelocity> velocities;

        public void Execute(int index)
        {
            var mv = motionVelocities[index];
            if (mv.InverseMass > math.FLT_MIN_NORMAL)
            {
                Entity entity = dynaimicBodies[index].Entity;

                var md = motionDatas[index];
                var worldFromBody = math.mul(md.WorldFromMotion, math.inverse(md.BodyFromMotion));
                if (positions.HasComponent(entity))
                {
                    Translation translation;
                    translation.Value = worldFromBody.pos;

                    positions[entity] = translation;
                }

                if (rotations.HasComponent(entity))
                {
                    Rotation rotation;
                    rotation.Value = worldFromBody.rot;
                    rotations[entity] = rotation;
                }

                if (velocities.HasComponent(entity))
                {
                    PhysicsVelocity physicsVelocity;
                    physicsVelocity.Linear = mv.LinearVelocity;
                    physicsVelocity.Angular = mv.LinearVelocity;
                    velocities[entity] = physicsVelocity;
                }
            }
        }
    }

    //private static readonly FieldInfo __ExportPhysicsWorldInputDependency = typeof(ExportPhysicsWorld).GetField("m_InputDependency", BindingFlags.Instance | BindingFlags.NonPublic);
    //private static readonly FieldInfo __ExportPhysicsWorldOutputDependency = typeof(ExportPhysicsWorld).GetField("m_OutputDependency", BindingFlags.Instance | BindingFlags.NonPublic);

    private BuildPhysicsWorld __buildPhysicsWorld;
    private StepPhysicsWorld __stepPhysicsWorld;
    private ExportPhysicsWorld __exportPhysicsWorld;
    private EndFramePhysicsSystem __endFramePhysicsSystem;

    protected override void OnCreate()
    {
        base.OnCreate();

        World world = World;
        __buildPhysicsWorld = world.GetOrCreateSystemManaged<BuildPhysicsWorld>();
        __stepPhysicsWorld = world.GetOrCreateSystemManaged<StepPhysicsWorld>();
        __exportPhysicsWorld = world.GetOrCreateSystemManaged<ExportPhysicsWorld>();
        __endFramePhysicsSystem = world.GetOrCreateSystemManaged<EndFramePhysicsSystem>();
    }

    protected override void OnStartRunning()
    {
        base.OnStartRunning();

        __exportPhysicsWorld.Enabled = false;
    }

    protected override void OnUpdate()
    {
        ref var world = ref __buildPhysicsWorld.PhysicsWorld;

        ExportDynamicBodies exportDynamicBodies;
        exportDynamicBodies.dynaimicBodies = world.DynamicBodies;
        exportDynamicBodies.motionVelocities = world.MotionVelocities;
        exportDynamicBodies.motionDatas = world.MotionDatas;
        exportDynamicBodies.positions = GetComponentLookup<Translation>();
        exportDynamicBodies.rotations = GetComponentLookup<Rotation>();
        exportDynamicBodies.velocities = GetComponentLookup<PhysicsVelocity>();

        var jobHandle = exportDynamicBodies.Schedule(world.NumDynamicBodies, 1, JobHandle.CombineDependencies(__stepPhysicsWorld.FinalSimulationJobHandle, Dependency));

        __endFramePhysicsSystem.AddInputDependency(jobHandle);

        Dependency = jobHandle;

        __exportPhysicsWorld.m_OutputDependency = jobHandle;
        __exportPhysicsWorld.m_InputDependency = default;
        //__ExportPhysicsWorldOutputDependency.SetValue(__exportPhysicsWorld, jobHandle);
        //__ExportPhysicsWorldInputDependency.SetValue(__exportPhysicsWorld, default(JobHandle));
    }
}
