using System.Reflection;
using Unity.Entities;
using Unity.Physics.Systems;
using ZG;

[CreateAfter(typeof(GamePhysicsWorldBuildSystem)), 
 CreateAfter(typeof(EndFramePhysicsSystem)), 
 CreateAfter(typeof(StepPhysicsWorld)), 
 CreateAfter(typeof(BuildPhysicsWorld)), 
 CreateAfter(typeof(FixedStepSimulationSystemGroup)), 
 UpdateInGroup(typeof(FixedStepSimulationSystemGroup)), 
 UpdateBefore(typeof(BuildPhysicsWorld))]
public partial class GamePhysicsWorldApplySystem : SystemBase
{
    private static readonly FieldInfo __BuildPhysicsWorldInputDependencyToComplete = typeof(BuildPhysicsWorld).GetField("m_InputDependencyToComplete", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo __BuildPhysicsWorldOutputDependency = typeof(BuildPhysicsWorld).GetField("m_OutputDependency", BindingFlags.Instance | BindingFlags.NonPublic);

    public int innerloopBatchCount = 1;

    private SystemHandle __systemHandle;
    private SharedPhysicsWorld __physicsWorld;
    private BuildPhysicsWorld __buildPhysicsWorld;
    private StepPhysicsWorld __stepPhysicsWorld;
    private EndFramePhysicsSystem __endFramePhysicsSystem;

    protected override void OnCreate()
    {
        base.OnCreate();

        World world = World;
        __systemHandle = world.GetExistingSystem<GamePhysicsWorldBuildSystem>();
        __physicsWorld = world.Unmanaged.GetUnsafeSystemRef<GamePhysicsWorldBuildSystem>(__systemHandle).physicsWorld;

        __endFramePhysicsSystem = world.GetExistingSystemManaged<EndFramePhysicsSystem>();
        __stepPhysicsWorld = world.GetExistingSystemManaged<StepPhysicsWorld>();
        __buildPhysicsWorld = world.GetExistingSystemManaged<BuildPhysicsWorld>();

        world.GetExistingSystemManaged<FixedStepSimulationSystemGroup>().Timestep = 0.1f;
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

        var world = World.Unmanaged;
        ref var system = ref world.GetUnsafeSystemRef<GamePhysicsWorldBuildSystem>(__systemHandle);
        if (system.isDirty)
            __systemHandle.Update(world);
        
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
