using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;
using Unity.Physics;
using Unity.Burst;
using ZG;
using PhysicsStep = Unity.Physics.PhysicsStep;

[BurstCompile, UpdateInGroup(typeof(GameUpdateSystemGroup), OrderFirst = true)]
public partial struct GamePhysicsWorldBuildSystem : ISystem
{
    public static readonly int InnerloopBatchCount = 4;

    private int __timeFrameCount;
    
    private EntityQuery __timeFrameGroup;
    private EntityQuery __physicsStepGroup;
    private EntityQuery __staticEntityGroup;
    private EntityQuery __dynamicEntityGroup;

    private EntityTypeHandle __entityType;
    private ComponentTypeHandle<Parent> __parentType;
    private ComponentTypeHandle<LocalToWorld> __localToWorldType;
    private ComponentTypeHandle<Translation> __positionType;
    private ComponentTypeHandle<Rotation> __rotationType;
    private ComponentTypeHandle<PhysicsCollider> __physicsColliderType;
    private ComponentTypeHandle<PhysicsCustomTags> __physicsCustomTagsType;
    private ComponentTypeHandle<PhysicsVelocity> __physicsVelocityType;
    private ComponentTypeHandle<PhysicsMass> __physicsMassType;
    private ComponentTypeHandle<PhysicsMassOverride> __physicsMassOverrideType;
    private ComponentTypeHandle<PhysicsGravityFactor> __physicsGravityFactorType;
    private ComponentTypeHandle<PhysicsDamping> __physicsDampingType;

    public bool isDirty => __timeFrameCount != __timeFrameGroup.GetSingleton<TimeFrame>().count;

    public SharedPhysicsWorld physicsWorld
    {
        get;

        private set;
    }

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.SetAlwaysUpdateSystem(true);
        
        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __timeFrameGroup = builder
                .WithAll<TimeFrame>()
                .WithOptions(EntityQueryOptions.IncludeSystems)
                .Build(ref state);

        __physicsStepGroup = state.GetEntityQuery(
            ComponentType.ReadOnly<PhysicsStep>());

        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __dynamicEntityGroup = builder
                .WithAll<PhysicsVelocity, Translation, Rotation>()
                .WithNone<PhysicsExclude>()
                .Build(ref state);

        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __staticEntityGroup = builder
                .WithAll<PhysicsCollider>()
                .WithAny<LocalToWorld, Translation, Rotation>()
                .WithNone<PhysicsExclude, PhysicsVelocity>()
                .Build(ref state);

        __entityType = state.GetEntityTypeHandle();
        __parentType = state.GetComponentTypeHandle<Parent>(true);
        __localToWorldType = state.GetComponentTypeHandle<LocalToWorld>(true);
        __positionType = state.GetComponentTypeHandle<Translation>(true);
        __rotationType = state.GetComponentTypeHandle<Rotation>(true);
        __physicsColliderType = state.GetComponentTypeHandle<PhysicsCollider>(true);
        __physicsCustomTagsType = state.GetComponentTypeHandle<PhysicsCustomTags>(true);
        __physicsVelocityType = state.GetComponentTypeHandle<PhysicsVelocity>(true);
        __physicsMassType = state.GetComponentTypeHandle<PhysicsMass>(true);
        __physicsMassOverrideType = state.GetComponentTypeHandle<PhysicsMassOverride>(true);
        __physicsGravityFactorType = state.GetComponentTypeHandle<PhysicsGravityFactor>(true);
        __physicsDampingType = state.GetComponentTypeHandle<PhysicsDamping>(true);
        
        physicsWorld = new SharedPhysicsWorld(0, 0, Allocator.Persistent);
    }

    //[BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        physicsWorld.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        __timeFrameCount = __timeFrameGroup.GetSingleton<TimeFrame>().count;
        
        PhysicsStep physicsStep;
        if (__physicsStepGroup.IsEmptyIgnoreFilter)
            physicsStep = PhysicsStep.Default;
        else
            physicsStep = __physicsStepGroup.GetSingleton<PhysicsStep>();

        physicsWorld.ScheduleBuildJob(
            InnerloopBatchCount, 
            physicsStep.Gravity, 
            __dynamicEntityGroup, 
            __staticEntityGroup,
            __entityType.UpdateAsRef(ref state),
            __parentType.UpdateAsRef(ref state),
            __localToWorldType.UpdateAsRef(ref state),
            __positionType.UpdateAsRef(ref state),
            __rotationType.UpdateAsRef(ref state),
            __physicsColliderType.UpdateAsRef(ref state),
            __physicsCustomTagsType.UpdateAsRef(ref state),
            __physicsVelocityType.UpdateAsRef(ref state),
            __physicsMassType.UpdateAsRef(ref state),
            __physicsMassOverrideType.UpdateAsRef(ref state),
            __physicsGravityFactorType.UpdateAsRef(ref state),
            __physicsDampingType.UpdateAsRef(ref state),
            ref state);
    }
}