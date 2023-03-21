using Unity.Jobs;
using Unity.Entities;
using Unity.Collections;
using Unity.Transforms;
using ZG;

public abstract partial class GameEffectSystem<TEffect, THandler, TFactory> : SystemBase
    where TEffect : unmanaged, IGameEffect<TEffect>
    where THandler : struct, IGameEffectHandler<TEffect>
    where TFactory : struct, IGameEffectFactory<TEffect, THandler>
{
    private EntityQuery __group;

    protected NativeArray<GameEffectInternalSurface> _surfaces;
    protected NativeArray<GameEffectInternalCondition> _conditions;
    protected NativeArray<GameEffectInternalHeight> _heights;
    protected NativeArray<GameEffectInternalValue> _effects;
    protected NativeArray<TEffect> _values;

    public bool isEmpty => __group.IsEmptyIgnoreFilter;

    public int entityCount => __group.CalculateEntityCount();
    
    protected override void OnCreate()
    {
        base.OnCreate();

        __group = GetEntityQuery(
            ComponentType.ReadOnly<Translation>(),
            ComponentType.ReadOnly<GameEffectData<TEffect>>(),
            ComponentType.ReadWrite<GameEffectResult<TEffect>>(),
            ComponentType.ReadWrite<GameEffectArea>());
    }

    protected override void OnUpdate()
    {
        if (!_surfaces.IsCreated)
            return;

        var jobHandle = Dependency;

        GameEffectApply<TEffect, THandler, TFactory> apply;
        apply.areasOverride = GetComponentLookup<GameEffectAreaOverride>(true);
        apply.surfaces = _surfaces;
        apply.conditions = _conditions;
        apply.heights = _heights;
        apply.effects = _effects;
        apply.values = _values;
        apply.revicerType = GetBufferTypeHandle<PhysicsShapeTriggerEventRevicer>(true);
        apply.translationType = GetComponentTypeHandle<Translation>(true);
        apply.instanceType = GetComponentTypeHandle<GameEffectData<TEffect>>(true);
        apply.resultType = GetComponentTypeHandle<GameEffectResult<TEffect>>();
        apply.areaType = GetComponentTypeHandle<GameEffectArea>();
        apply.factory = _Get(ref jobHandle);

        Dependency = apply.ScheduleParallel(__group, jobHandle);
    }

    protected abstract TFactory _Get(ref JobHandle inputDeps);
}