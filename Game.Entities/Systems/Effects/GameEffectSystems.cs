using Unity.Jobs;
using Unity.Entities;
using Unity.Collections;
using Unity.Transforms;
using Unity.Physics;
using ZG;

public struct GameEffectSystemCore<TEffect> where TEffect : unmanaged, IGameEffect<TEffect>
{
    private EntityQuery __defintionGroup;

    private BufferLookup<GameEffectAreaOverrideBuffer> __areasOverrideBuffers;
    private ComponentLookup<GameEffectAreaOverride> __areasOverride;
    private ComponentLookup<PhysicsCollider> __physicsColliders;
    private ComponentLookup<PhysicsShapeParent> __physicsShapeParents;
    private BufferTypeHandle<PhysicsTriggerEvent> __physicsTriggerEventType;
    private ComponentTypeHandle<Translation> __translationType;
    private ComponentTypeHandle<GameEffectData<TEffect>> __instanceType;

    private ComponentTypeHandle<GameEffectResult<TEffect>> __resultType;

    private ComponentTypeHandle<GameEffectArea> __areaType;

    /*protected NativeArray<GameEffectInternalSurface> _surfaces;
    protected NativeArray<GameEffectInternalCondition> _conditions;
    protected NativeArray<GameEffectInternalHeight> _heights;
    protected NativeArray<GameEffectInternalValue> _effects;
    protected NativeArray<TEffect> _values;*/

    public readonly EntityQuery Group;

    public GameEffectSystemCore(ref SystemState state)
    {
        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            Group = builder
                    .WithAll<Translation, GameEffectData<TEffect>>()
                    .WithAllRW<GameEffectResult<TEffect>, GameEffectArea>()
                    .Build(ref state);

        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __defintionGroup = builder
                .WithAll<GameEffectLandscapeData>()
                .Build(ref state);

        __areasOverrideBuffers = state.GetBufferLookup<GameEffectAreaOverrideBuffer>(true);
        __areasOverride = state.GetComponentLookup<GameEffectAreaOverride>(true);
        __physicsColliders = state.GetComponentLookup<PhysicsCollider>(true);
        __physicsShapeParents = state.GetComponentLookup<PhysicsShapeParent>(true);
        __physicsTriggerEventType = state.GetBufferTypeHandle<PhysicsTriggerEvent>(true);
        __translationType = state.GetComponentTypeHandle<Translation>(true);
        __instanceType = state.GetComponentTypeHandle<GameEffectData<TEffect>>(true);
        __resultType = state.GetComponentTypeHandle<GameEffectResult<TEffect>>();
        __areaType = state.GetComponentTypeHandle<GameEffectArea>();
    }

    public void Update<THandler, TFactory>(
        in NativeArray<TEffect>.ReadOnly values, 
        ref TFactory factory, 
        ref SystemState state)
        where THandler : struct, IGameEffectHandler<TEffect>
        where TFactory : struct, IGameEffectFactory<TEffect, THandler>
    {
        if (!__defintionGroup.HasSingleton<GameEffectLandscapeData>())
            return;

        GameEffectApply<TEffect, THandler, TFactory> apply;
        apply.definition = __defintionGroup.GetSingleton<GameEffectLandscapeData>().definition;
        apply.values = values;
        apply.areasOverrideBuffers = __areasOverrideBuffers.UpdateAsRef(ref state);
        apply.areasOverride = __areasOverride.UpdateAsRef(ref state);
        apply.physicsColliders = __physicsColliders.UpdateAsRef(ref state);
        apply.physicsShapeParents = __physicsShapeParents.UpdateAsRef(ref state);
        apply.physicsTriggerEventType = __physicsTriggerEventType.UpdateAsRef(ref state);
        apply.translationType = __translationType.UpdateAsRef(ref state);
        apply.instanceType = __instanceType.UpdateAsRef(ref state);
        apply.resultType = __resultType.UpdateAsRef(ref state);
        apply.areaType = __areaType.UpdateAsRef(ref state);
        apply.factory = factory;

        state.Dependency = apply.ScheduleParallelByRef(Group, state.Dependency);
    }
}