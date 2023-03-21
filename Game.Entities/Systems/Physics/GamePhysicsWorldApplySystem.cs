using System.Reflection;
using Unity.Entities;
using Unity.Physics.Systems;
using ZG;

[UpdateInGroup(typeof(FixedStepSimulationSystemGroup)), UpdateBefore(typeof(BuildPhysicsWorld))]
public partial class GamePhysicsWorldApplySystem : SystemBase
{
    private static readonly FieldInfo __BuildPhysicsWorldInputDependencyToComplete = typeof(BuildPhysicsWorld).GetField("m_InputDependencyToComplete", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo __BuildPhysicsWorldOutputDependency = typeof(BuildPhysicsWorld).GetField("m_OutputDependency", BindingFlags.Instance | BindingFlags.NonPublic);

    public int innerloopBatchCount = 1;

    private SharedPhysicsWorld __physicsWorld;
    private BuildPhysicsWorld __buildPhysicsWorld;
    private StepPhysicsWorld __stepPhysicsWorld;
    private EndFramePhysicsSystem __endFramePhysicsSystem;

    protected override void OnCreate()
    {
        base.OnCreate();

        World world = World;
        __physicsWorld = world.GetOrCreateSystemUnmanaged<GamePhysicsWorldBuildSystem>().physicsWorld;

        __endFramePhysicsSystem = world.GetOrCreateSystemManaged<EndFramePhysicsSystem>();
        __stepPhysicsWorld = world.GetOrCreateSystemManaged<StepPhysicsWorld>();
        __buildPhysicsWorld = world.GetOrCreateSystemManaged<BuildPhysicsWorld>();

        world.GetOrCreateSystemManaged<FixedStepSimulationSystemGroup>().Timestep = 0.1f;
    }

    protected override void OnStartRunning()
    {
        base.OnStartRunning();

        __buildPhysicsWorld.Enabled = false;
    }

    protected override void OnUpdate()
    {
        __endFramePhysicsSystem.GetOutputDependency().Complete();

        __buildPhysicsWorld.CollisionWorldProxyGroup.CompleteDependency();

        __physicsWorld.CopyTo(
            innerloopBatchCount, 
            __buildPhysicsWorld.JointEntityGroup, 
            ref __buildPhysicsWorld.PhysicsWorld, 
            ref this.GetState());

        var jobHandle = Dependency;

        __stepPhysicsWorld.AddInputDependency(jobHandle);

        __BuildPhysicsWorldOutputDependency.SetValue(__buildPhysicsWorld, jobHandle);
        __BuildPhysicsWorldInputDependencyToComplete.SetValue(__buildPhysicsWorld, default(Unity.Jobs.JobHandle));
    }
}
