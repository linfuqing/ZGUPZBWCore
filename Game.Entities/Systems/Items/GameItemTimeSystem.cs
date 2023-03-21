using System;
using Unity.Jobs;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using ZG;

[assembly: RegisterGenericJobType(typeof(GameItemComponentInit<GameItemTime, GameItemTimeInitSystem.Initializer>))]
[assembly: RegisterGenericJobType(typeof(GameItemComponentChange<GameItemTime, GameItemTimeInitSystem.Initializer>))]
[assembly: RegisterGenericJobType(typeof(GameItemComponentApply<GameItemTime>))]

[Serializable]
public struct GameItemTime : IGameItemComponentData<GameItemTime>
{
    public float value;

    public void Mul(float value)
    {
        this.value *= value;
    }

    public void Add(in GameItemTime value)
    {
        this.value += value.value;
    }

    public GameItemTime Diff(in GameItemTime value)
    {
        GameItemTime result;
        result.value = math.max(this.value, value.value) - math.min(this.value, value.value);
        return result;
    }
}

public partial class GameItemTimeSystem : SystemBase
{
    private struct UpdateTime
    {
        public float deltaTime;

        [ReadOnly]
        public GameItemManager.ReadOnlyInfos infos;

        [ReadOnly]
        public UnsafeParallelHashMap<int, int> timeoutTypes;

        [ReadOnly]
        public SharedHashMap<GameItemHandle, Entity>.Reader rootEntities;

        [ReadOnly]
        public ComponentLookup<GameItemTimeScale> timeScales;

        [ReadOnly]
        public NativeArray<GameItemData> instances;

        public NativeArray<GameItemTime> times;

        public NativeArray<GameItemType> types;

        public NativeQueue<GameItemHandle>.ParallelWriter handlesToRemove;

        public void Execute(int index)
        {
            var handle = instances[index].handle;
            var root = infos.GetRoot(handle);
            if (!rootEntities.TryGetValue(root, out Entity entity))
                return;

            if (!timeScales.HasComponent(entity))
                return;

            float deltaTime = this.deltaTime * timeScales[entity].value;

            var time = times[index];
            if (time.value > deltaTime)
            {
                time.value -= deltaTime;

                times[index] = time;
            }
            else
            {
                GameItemType type;
                if (timeoutTypes.TryGetValue(types[index].value, out type.value))
                    types[index] = type;
                else
                    handlesToRemove.Enqueue(handle);
            }
        }
    }

    /*private struct UpdateTime
    {
        public float deltaTime;

        public GameItemManager.Hierarchy hierarchy;

        [ReadOnly]
        public UnsafeHashMap<int, int> timeoutTypes;

        [ReadOnly]
        public SharedHashMap<Entity, Entity>.Reader entities;

        [ReadOnly]
        public NativeArray<GameItemRoot> roots;

        [ReadOnly]
        public NativeArray<GameItemTimeScale> timeScales;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<GameItemTime> times;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<GameItemType> types;

        public NativeQueue<GameItemHandle>.ParallelWriter handlesToRemove;

        public void Execute(float deltaTime, in GameItemHandle handle)
        {
            if(!hierarchy.GetChildren(handle, out var enumerator, out var item))
                return;

            if (entities.TryGetValue(GameItemStructChangeFactory.Convert(handle), out Entity entity) && times.HasComponent(entity))
            {
                var time = times[entity];
                if (time.value > deltaTime)
                {
                    time.value -= deltaTime;

                    times[entity] = time;
                }
                else
                {
                    GameItemType type;
                    if (timeoutTypes.TryGetValue(types[entity].value, out type.value))
                        types[entity] = type;
                    else
                        handlesToRemove.Enqueue(handle);
                }
            }

            while (enumerator.MoveNext())
                Execute(deltaTime, enumerator.Current.handle);

            Execute(deltaTime, item.siblingHandle);
        }

        public void Execute(int index)
        {
            float timeScale = index < timeScales.Length ? timeScales[index].value : 1.0f;
            Execute(timeScale * deltaTime, roots[index].handle);
        }
    }*/

    [BurstCompile]
    private struct UpdateTimeEx : IJobChunk
    {
        public float deltaTime;

        [ReadOnly]
        public GameItemManager.ReadOnlyInfos infos;

        [ReadOnly]
        public UnsafeParallelHashMap<int, int> timeoutTypes;

        [ReadOnly]
        public SharedHashMap<GameItemHandle, Entity>.Reader rootEntities;

        [ReadOnly]
        public ComponentLookup<GameItemTimeScale> timeScales;

        [ReadOnly]
        public ComponentTypeHandle<GameItemData> instanceType;

        public ComponentTypeHandle<GameItemTime> timeType;

        public ComponentTypeHandle<GameItemType> typeType;

        public NativeQueue<GameItemHandle>.ParallelWriter handlesToRemove;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            UpdateTime updateTime;
            updateTime.deltaTime = deltaTime;
            updateTime.timeoutTypes = timeoutTypes;
            updateTime.infos = infos;
            updateTime.rootEntities = rootEntities;
            updateTime.timeScales = timeScales;
            updateTime.instances = chunk.GetNativeArray(ref instanceType);
            updateTime.times = chunk.GetNativeArray(ref timeType);
            updateTime.types = chunk.GetNativeArray(ref typeType);
            updateTime.handlesToRemove = handlesToRemove;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                updateTime.Execute(i);
        }
    }

    [BurstCompile]
    private struct RemoveHandles : IJob
    {
        public GameItemManager manager;
        public NativeQueue<GameItemHandle> handles;

        public void Execute()
        {
            while (handles.TryDequeue(out var handle))
                manager.Remove(handle, 0);
        }
    }

    private EntityQuery __group;
    private GameItemManagerShared __itemManager;
    private SharedHashMap<GameItemHandle, Entity> __rootEntities;
    private UnsafeParallelHashMap<int, int> __timeoutTypes;
    private NativeQueue<GameItemHandle> __handles;

    public void Create(Tuple<int, int>[] timeoutTypes)
    {
        __timeoutTypes = new UnsafeParallelHashMap<int, int>(timeoutTypes.Length, Allocator.Persistent);
        foreach (var timeoutType in timeoutTypes)
            __timeoutTypes.Add(timeoutType.Item1, timeoutType.Item2);
    }

    public void OnCreate(ref SystemState state)
    {
        /*__group = state.GetEntityQuery(
            new EntityQueryDesc()
            {
                All = new ComponentType[]
                {
                    ComponentType.ReadOnly<GameItemRoot>()
                },
                None = new ComponentType[]
                {
                   typeof(GameItemDontDestroyOnDead)
                }
            },
            new EntityQueryDesc()
            {
                All = new ComponentType[]
                {
                    ComponentType.ReadOnly<GameItemRoot>(),
                    ComponentType.ReadOnly<GameItemDontDestroyOnDead>()
                }, 
                Options = EntityQueryOptions.IncludeDisabled
            });*/
        __group = state.GetEntityQuery(
            ComponentType.ReadOnly<GameItemData>(),
            ComponentType.ReadWrite<GameItemType>(),
            ComponentType.ReadWrite<GameItemTime>());

        World world = state.World;
        __itemManager = world.GetOrCreateSystemUnmanaged<GameItemSystem>().manager;
        __rootEntities = world.GetOrCreateSystemManaged<GameItemRootEntitySystem>().entities;// world.GetOrCreateSystemUnmanaged<GameItemStructChangeSystem>().handleEntities;

        __handles = new NativeQueue<GameItemHandle>(Allocator.Persistent);
    }

    public void OnDestroy(ref SystemState state)
    {
        if (__timeoutTypes.IsCreated)
            __timeoutTypes.Dispose();

        __handles.Dispose();
    }

    public void OnUpdate(ref SystemState state)
    {
        if (!__timeoutTypes.IsCreated)
            return;

        UpdateTimeEx updateTime;
        updateTime.deltaTime = state.WorldUnmanaged.Time.DeltaTime;
        updateTime.timeoutTypes = __timeoutTypes;
        updateTime.infos = __itemManager.readOnlyInfos;
        updateTime.rootEntities = __rootEntities.reader;
        updateTime.timeScales = state.GetComponentLookup<GameItemTimeScale>(true);
        updateTime.instanceType = state.GetComponentTypeHandle<GameItemData>(true);
        updateTime.timeType = state.GetComponentTypeHandle<GameItemTime>();
        updateTime.typeType = state.GetComponentTypeHandle<GameItemType>();
        updateTime.handlesToRemove = __handles.AsParallelWriter();

        ref var itemJobManager = ref __itemManager.lookupJobManager;
        ref var entityJobManager = ref __rootEntities.lookupJobManager;

        var jobHandle = JobHandle.CombineDependencies(itemJobManager.readOnlyJobHandle, entityJobManager.readOnlyJobHandle, state.Dependency);
        jobHandle = updateTime.ScheduleParallel(__group, jobHandle);

        entityJobManager.AddReadOnlyDependency(jobHandle);

        RemoveHandles removeHandles;
        removeHandles.manager = __itemManager.value;
        removeHandles.handles = __handles;
        jobHandle = removeHandles.Schedule(JobHandle.CombineDependencies(jobHandle, itemJobManager.readWriteJobHandle));

        itemJobManager.readWriteJobHandle = jobHandle;

        state.Dependency = jobHandle;
    }

    protected override void OnCreate()
    {
        base.OnCreate();

        OnCreate(ref this.GetState());
    }

    protected override void OnDestroy()
    {
        OnDestroy(ref this.GetState());

        base.OnDestroy();
    }

    protected override void OnUpdate()
    {
        OnUpdate(ref this.GetState());
    }
}

[BurstCompile, UpdateInGroup(typeof(GameItemComponentInitSystemGroup), OrderFirst = true)]
public struct GameItemTimeInitSystem : IGameItemInitializationSystem<GameItemTime, GameItemTimeInitSystem.Initializer>
{
    public struct Initializer : IGameItemInitializer<GameItemTime>
    {
        [ReadOnly]
        public UnsafeParallelHashMap<int, float> values;

        public bool IsVail(int type) => values.ContainsKey(type);

        public GameItemTime GetValue(int type, int count)
        {
            GameItemTime value;
            value.value = values[type];
            value.value *= count;

            return value;
        }
    }

    private GameItemComponentInitSystemCore<GameItemTime> __core;
    private UnsafeParallelHashMap<int, float> __values;

    public Initializer initializer
    {
        get
        {
            Initializer initializer;
            initializer.values = __values;
            return initializer;
        }
    }

    public void Create(Tuple<int, float>[] values)
    {
        foreach(var value in values)
        {
            __values.Add(value.Item1, value.Item2);
        }
    }

    public void OnCreate(ref SystemState state)
    {
        __core = new GameItemComponentInitSystemCore<GameItemTime>(ref state);
        __values = new UnsafeParallelHashMap<int, float>(1, Allocator.Persistent);

#if DEBUG
        EntityCommandUtility.RegisterProducerJobType<GameItemComponentInit<GameItemTime, Initializer>>();
#endif
    }

    public void OnDestroy(ref SystemState state)
    {
        __values.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        __core.Update(initializer, ref state);
    }
}

[BurstCompile, UpdateInGroup(typeof(GameItemSystemGroup), OrderLast = true)]
public partial struct GameItemTimeChangeSystem : ISystem
{
    private GameItemComponentDataChangeSystemCore<GameItemTime, GameItemTimeInitSystem.Initializer> __core;
    private GameItemTimeInitSystem.Initializer __initializer;

    public SharedList<GameItemChangeResult<GameItemTime>> resutls => __core.results;

    public void OnCreate(ref SystemState state)
    {
        __core = new GameItemComponentDataChangeSystemCore<GameItemTime, GameItemTimeInitSystem.Initializer>(ref state);

        __initializer = state.World.GetOrCreateSystemUnmanaged<GameItemTimeInitSystem>().initializer;
    }

    public void OnDestroy(ref SystemState state)
    {
        __core.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        __core.Update(__initializer, ref state);
    }
}

[BurstCompile, UpdateInGroup(typeof(GameItemInitSystemGroup), OrderLast = true)]
public partial struct GameItemTimeApplySystem : ISystem
{
    private GameItemComponentDataApplySystemCore<GameItemTime> __core;
    private SharedList<GameItemChangeResult<GameItemTime>> __resutls;

    public void OnCreate(ref SystemState state)
    {
        __core = new GameItemComponentDataApplySystemCore<GameItemTime>(ref state);

        __resutls = state.World.GetOrCreateSystemUnmanaged<GameItemTimeChangeSystem>().resutls;
    }

    public void OnDestroy(ref SystemState state)
    {

    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        __core.Update(ref __resutls, ref state);
    }
}