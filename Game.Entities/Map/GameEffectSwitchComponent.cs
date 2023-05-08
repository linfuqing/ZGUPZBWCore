using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;
using System;
using UnityEngine;
using UnityEngine.Events;
using ZG;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Jobs;

//[assembly: RegisterEntityObject(typeof(GameEffectSwitchComponent))]

[Serializable]
public struct GameEffectSwitchData : ICleanupComponentData
{
    public GameEffect effect;

    public CallbackHandle<bool> callbackHandle;
}

[Serializable]
public struct GameEffectSwitchResult : IComponentData
{
    public int value;
}

[EntityComponent(typeof(GameEffectSwitchData))]
[EntityComponent(typeof(GameEffectSwitchResult))]
public class GameEffectSwitchComponent : EntityProxyComponent, IEntityComponent, IEntitySystemStateComponent
{
    public UnityEvent onEnable;
    public UnityEvent onDisable;

    public GameEffect effect;

    [SerializeField]
    internal bool _isActive = true;

    private bool __isAwake;

    public bool isActive
    {
        get
        {
            return _isActive;
        }
    }

    protected void Awake()
    {
        if (_isActive)
        {
            if (onEnable != null)
                onEnable.Invoke();
        }
        else
        {
            if (onDisable != null)
                onDisable.Invoke();
        }

        __isAwake = true;
    }

    void IEntityComponent.Init(in Entity entity, EntityComponentAssigner assigner)
    {
        GameEffectSwitchResult result;
        result.value = _isActive ? 1 : 0;
        assigner.SetComponentData(entity, result);
    }

    void IEntitySystemStateComponent.Init(in Entity entity, EntityComponentAssigner assigner)
    {
        GameEffectSwitchData instance;
        instance.effect = effect;
        instance.callbackHandle = new Action<bool>(__Set).Register();
        assigner.SetComponentData(entity, instance);
    }

    private void __Set(bool value)
    {
        if (_isActive == value)
            return;

        if (__isAwake)
        {
            if (value)
            {
                if (onEnable != null)
                    onEnable.Invoke();
            }
            else
            {
                if (onDisable != null)
                    onDisable.Invoke();
            }
        }

        _isActive = value;
    }
}

public partial class GameEffectSwitchSystem : SystemBase
{
    [Serializable]
    public struct Callback
    {
        public bool result;
        public CallbackHandle<bool> handle;
    }

    private struct Refresh
    {
        [ReadOnly]
        public NativeArray<GameEffectResult<GameEffect>> effects;
        [ReadOnly]
        public NativeArray<GameEffectSwitchData> instances;

        public NativeArray<GameEffectSwitchResult> results;

        public NativeList<Callback>.ParallelWriter callbacks;

        public static bool IsActive(GameEffect x, GameEffect y)
        {
            if (math.abs(x.force) > 0 && x.force > y.force)
                return false;

            if (math.abs(x.power) > 0 && x.power > y.power)
                return false;

            if (math.abs(x.temperature) > math.FLT_MIN_NORMAL && x.temperature > y.temperature)
                return false;

            if (math.abs(x.health) > math.FLT_MIN_NORMAL && x.health > y.health)
                return false;

            if (math.abs(x.food) > math.FLT_MIN_NORMAL && x.food > y.food)
                return false;

            if (math.abs(x.water) > math.FLT_MIN_NORMAL && x.water > y.water)
                return false;

            if (math.abs(x.itemTimeScale) > math.FLT_MIN_NORMAL && x.itemTimeScale > y.itemTimeScale)
                return false;

            if (math.abs(x.layTimeScale) > math.FLT_MIN_NORMAL && x.layTimeScale > y.layTimeScale)
                return false;

            return true;
        }

        public void Execute(int index)
        {
            var instance = instances[index];
            bool value = IsActive(instance.effect, effects[index].value), oldValue = results[index].value  == 0 ? false : true;
            if (value == oldValue)
                return;

            GameEffectSwitchResult result;
            result.value = value ? 1 : 0;
            results[index] = result;

            Callback callback;
            callback.result = value;
            callback.handle = instance.callbackHandle;
            callbacks.AddNoResize(callback);
        }
    }

    [BurstCompile]
    private struct RefreshEx : IJobChunk
    {
        [ReadOnly]
        public ComponentTypeHandle<GameEffectResult<GameEffect>> effectType;
        [ReadOnly]
        public ComponentTypeHandle<GameEffectSwitchData> instanceType;

        public ComponentTypeHandle<GameEffectSwitchResult> resultType;

        public NativeList<Callback>.ParallelWriter callbacks;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Refresh refresh;
            refresh.effects = chunk.GetNativeArray(ref effectType);
            refresh.instances = chunk.GetNativeArray(ref instanceType);
            refresh.results = chunk.GetNativeArray(ref resultType);
            refresh.callbacks = callbacks;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                refresh.Execute(i);
        }
    }

    private JobHandle __jobHandle;
    private EntityQuery __group;
    private NativeList<Callback> __callbacks;

    public NativeArray<Callback> callbacks
    {
        get
        {
            __jobHandle.Complete();
            __jobHandle = default;

            return __callbacks.AsArray();
        }
    }

    protected override void OnCreate()
    {
        base.OnCreate();

        __group = GetEntityQuery(
            ComponentType.ReadOnly<GameEffectResult<GameEffect>>(), 
            ComponentType.ReadOnly<GameEffectSwitchData>(), 
            ComponentType.ReadWrite<GameEffectSwitchResult>());

        __callbacks = new NativeList<Callback>(Allocator.Persistent);
    }

    protected override void OnDestroy()
    {
        __callbacks.Dispose();

        base.OnDestroy();
    }

    protected override void OnUpdate()
    {
        __callbacks.Capacity = math.max(__callbacks.Capacity, __group.CalculateEntityCount());
        __callbacks.Clear();

        RefreshEx refresh;
        refresh.effectType = GetComponentTypeHandle<GameEffectResult<GameEffect>>(true);
        refresh.instanceType = GetComponentTypeHandle<GameEffectSwitchData>(true);
        refresh.resultType = GetComponentTypeHandle<GameEffectSwitchResult>();
        refresh.callbacks = __callbacks.AsParallelWriter();

        __jobHandle = refresh.ScheduleParallel(__group, Dependency);

        Dependency = __jobHandle;
    }
}

[UpdateInGroup(typeof(PresentationSystemGroup))/*, UpdateBefore(typeof(EndFrameEntityCommandSystemGroup))*/]
public partial class GameEffectSwitchCallbackSystem : SystemBase
{
    private GameEffectSwitchSystem __system;

    protected override void OnCreate()
    {
        base.OnCreate();

        __system = World.GetOrCreateSystemManaged<GameEffectSwitchSystem>();
    }

    protected override void OnUpdate()
    {
        var callbacks = __system.callbacks;
        foreach (var callback in callbacks)
            callback.handle.Invoke(callback.result);
    }
}

[UpdateInGroup(typeof(InitializationSystemGroup)), UpdateAfter(typeof(EntityObjectSystemGroup))]
public partial class GameEffectSwitchDestroySystem : SystemBase
{
    private EntityQuery __group;

    protected override void OnCreate()
    {
        base.OnCreate();

        __group = GetEntityQuery(
            ComponentType.ReadOnly<GameEffectSwitchData>(),
            ComponentType.Exclude<GameEffectSwitchResult>());
    }

    protected override void OnUpdate()
    {
        //TODO
        CompleteDependency();

        using (var instances = __group.ToComponentDataArray<GameEffectSwitchData>(Allocator.Temp))
        {
            foreach (var instance in instances)
                instance.callbackHandle.Unregister();
        }

        EntityManager.RemoveComponent<GameEffectSwitchData>(__group);
    }
}