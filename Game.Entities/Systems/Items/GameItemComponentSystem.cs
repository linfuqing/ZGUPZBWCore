using System;
using Unity.Jobs;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;
using ZG;
using Handle = GameItemHandle;

public interface IGameItemComponentData<T> : IComponentData where T : IGameItemComponentData<T>
{
    void Mul(float value);

    void Add(in T value);

    T Diff(in T value);
}

public interface IGameItemInitializer<out T>
{
    bool IsVail(int type);

    T GetValue(int type, int count);
}

public interface IGameItemComponentInitializer<T>
{
    bool TryGetValue(in Handle handle, int type, out T value);
}

public interface IGameItemComponentFactory<T>
{
    T Create(in GameItemManager.ReadOnlyInfos infos, in ArchetypeChunk chunk, int unfilteredChunkIndex);
}

public interface IGameItemInitializationSystem<TValue, TInitializer> : ISystem
    where TValue : struct
    where TInitializer : struct, IGameItemInitializer<TValue>
{
    TInitializer initializer { get; }
}

public struct GameItemChangeResult<T> where T : struct, IGameItemComponentData<T>
{
    public int index;
    public Handle handle;
    public T orgin;
    public T value;

    public T diff => orgin.Diff(value);
}

[BurstCompile]
public struct GameItemComponentInit<TValue, TInitializer> : IJobChunk, IEntityCommandProducerJob
    where TValue : struct
    where TInitializer : struct, IGameItemInitializer<TValue>
{
    private struct Executor
    {
        public TInitializer initializer;

        public GameItemManager.ReadOnlyInfos infos;

        [ReadOnly]
        public NativeArray<Entity> entityArray;

        [ReadOnly]
        public NativeArray<GameItemData> instances;

        public EntityCommandQueue<EntityCommandStructChange>.ParallelWriter entityManager;

        public void Execute(int index)
        {
            if (!infos.TryGetValue(instances[index].handle, out var item))
                return;

            if (!initializer.IsVail(item.type))
                return;

            EntityCommandStructChange command;
            command.componentType = ComponentType.ReadOnly<TValue>();
            command.entity = entityArray[index];

            entityManager.Enqueue(command);
        }
    }

    public TInitializer initializer;

    public GameItemManager.ReadOnlyInfos infos;

    [ReadOnly]
    public EntityTypeHandle entityType;

    [ReadOnly]
    public ComponentTypeHandle<GameItemData> instanceType;

    public EntityCommandQueue<EntityCommandStructChange>.ParallelWriter entityManager;

    public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
    {
        Executor executor;
        executor.initializer = initializer;
        executor.infos = infos;
        executor.entityArray = chunk.GetNativeArray(entityType);
        executor.instances = chunk.GetNativeArray(ref instanceType);
        executor.entityManager = entityManager;

        var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
        while(iterator.NextEntityIndex(out int i))
            executor.Execute(i);
    }
}

[BurstCompile]
public struct GameItemComponentDataInit<TValue, TInitializer, TFactory> : IJobChunk, IEntityCommandProducerJob
    where TValue : struct, IComponentData
    where TInitializer : struct, IGameItemComponentInitializer<TValue>
    where TFactory : struct, IGameItemComponentFactory<TInitializer>
{
    private struct Executor
    {
        public TInitializer initializer;

        public GameItemManager.ReadOnlyInfos infos;

        [ReadOnly]
        public NativeArray<Entity> entityArray;

        [ReadOnly]
        public NativeArray<GameItemData> instances;

        public EntityAddDataQueue.ParallelWriter entityManager;

        public void Execute(int index)
        {
            var handle = instances[index].handle;
            if (!infos.TryGetValue(handle, out var item))
                return;

            if (!initializer.TryGetValue(handle, item.type, out var value))
                return;

            entityManager.AddComponentData(entityArray[index], value);
        }
    }

    public TFactory factory;

    public GameItemManager.ReadOnlyInfos infos;

    [ReadOnly]
    public EntityTypeHandle entityType;

    [ReadOnly]
    public ComponentTypeHandle<GameItemData> instanceType;

    public EntityAddDataQueue.ParallelWriter entityManager;

    public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
    {
        Executor executor;
        executor.initializer = factory.Create(infos, chunk, unfilteredChunkIndex);
        executor.infos = infos;
        executor.entityArray = chunk.GetNativeArray(entityType);
        executor.instances = chunk.GetNativeArray(ref instanceType);
        executor.entityManager = entityManager;

        var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
        while (iterator.NextEntityIndex(out int i))
            executor.Execute(i);
    }
}

[BurstCompile]
public struct GameItemComponentChange<TValue, TInitializer> : IJob
    where TValue : unmanaged, IGameItemComponentData<TValue>
    where TInitializer : struct, IGameItemInitializer<TValue>
{
    public TInitializer initializer;

    [ReadOnly]
    public SharedHashMap<Entity, Entity>.Reader entities;

    [ReadOnly]
    public NativeArray<GameItemCommand> commands;

    public ComponentLookup<TValue> values;

    public SharedList<GameItemChangeResult<TValue>>.Writer results;

    public bool TryGetValue(in Handle handle, bool isOrigin, out TValue value)
    {
        int length = results.length;
        if (length > 0)
        {
            GameItemChangeResult<TValue> result;
            for (int i = length - 1; i >= 0; --i)
            {
                result = results[i];
                if (result.handle.Equals(handle))
                {
                    value = isOrigin ? result.orgin : result.value;

                    return true;
                }
            }
        }

        value = default;

        return false;
    }

    public TValue Get(in Handle handle, bool isOrigin)
    {
        if (TryGetValue(handle, isOrigin, out var value))
            return value;

        return values[entities[GameItemStructChangeFactory.Convert(handle)]];
    }

    public void Execute()
    {
        results.Clear();

        int length = commands.Length;
        if (length < 1)
            return;

        GameItemChangeResult<TValue> result;
        GameItemCommand command;
        for (int i = 0; i < length; ++i)
        {
            command = commands[i];
            switch (command.commandType)
            {
                case GameItemCommandType.Create:
                case GameItemCommandType.Add:
                    if (!initializer.IsVail(command.type))
                        continue;

                    result.index = i;
                    result.handle = command.destinationHandle;
                    if (command.count == 0 || command.sourceHandle.Equals(Handle.Empty))
                    {
                        if (entities.TryGetValue(GameItemStructChangeFactory.Convert(command.destinationHandle), out Entity entity) && 
                            values.HasComponent(entity) && 
                            !TryGetValue(command.destinationHandle, true, out _))
                            result.value = values[entity]; //Deserialize
                        else
                            result.value = initializer.GetValue(command.type, command.destinationCount);
                    }
                    else
                    {
                        result.value = Get(command.sourceHandle, true);

                        result.value.Mul(command.count * 1.0f / command.sourceCount);
                    }

                    if (command.commandType == GameItemCommandType.Create)
                        result.orgin = result.value;
                    else
                    {
                        result.orgin = Get(command.destinationHandle, false);
                        result.value.Add(result.orgin);
                    }

                    results.Add(result);
                    break;
                case GameItemCommandType.Remove:
                case GameItemCommandType.Destroy:
                    if (!initializer.IsVail(command.type))
                        continue;

                    result.index = i;
                    result.handle = command.sourceHandle;
                    result.orgin = Get(command.sourceHandle, false);
                    result.value = result.orgin;
                    result.value.Mul(1.0f - command.count * 1.0f / command.sourceCount);
                    results.Add(result);

                    break;
            }
        }
    }
}

[BurstCompile]
public struct GameItemComponentApply<T> : IJob where T : unmanaged, IGameItemComponentData<T>
{
    [ReadOnly]
    public SharedList<GameItemChangeResult<T>>.Reader results;

    [ReadOnly]
    public SharedHashMap<Entity, Entity>.Reader entities;

    public ComponentLookup<T> values;

    public void Execute()
    {
        GameItemChangeResult<T> result;
        Entity entity;
        int length = results.length;
        for (int i = 0; i < length; ++i)
        {
            result = results[i];
            if (!entities.TryGetValue(GameItemStructChangeFactory.Convert(result.handle), out entity))
                continue;

            values[entity] = result.value;
        }
    }
}

/*[BurstCompile]
public struct GameItemDiposeAll : IJob
{
    [DeallocateOnJobCompletion]
    public NativeArray<int> entityCount;

    public void Execute()
    {

    }
}*/

[/*AlwaysUpdateSystem, */BurstCompile, UpdateInGroup(typeof(GameItemComponentInitSystemGroup))]
public partial struct GameItemComponentStructChangeSystem : ISystem
{
    //private EntityQuery __group;

    public EntityCommandStructChangeManager manager
    {
        get;

        private set;
    }

    public EntityComponentAssigner assigner
    {
        get;

        private set;
    }

    public EntityAddDataPool addDataPool => new EntityAddDataPool(manager.addComponentPool, assigner);

    public void OnCreate(ref SystemState state)
    {
        //__group = state.GetEntityQuery(ComponentType.ReadOnly<GameItemData>());

        manager = new EntityCommandStructChangeManager(Allocator.Persistent);
        assigner = new EntityComponentAssigner(Allocator.Persistent);
    }

    public void OnDestroy(ref SystemState state)
    {
        manager.Dispose();
        assigner.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var assigner = this.assigner;
        manager.Playback(ref state, ref assigner);
    }
}

[UpdateInGroup(typeof(GameItemComponentInitSystemGroup), OrderFirst = true)/*, UpdateAfter(typeof(BeginFrameEntityCommandSystem))*/]
public struct GameItemComponentInitSystemCore<T> where T : struct
{
    private EntityQuery __group;
    private GameItemManagerShared __itemManager;
    private EntityCommandPool<EntityCommandStructChange> __entityManager;

    public GameItemComponentInitSystemCore(ref SystemState state)
    {
        __group = state.GetEntityQuery(
            ComponentType.ReadOnly<GameItemData>(),
            ComponentType.Exclude<GameItemType>(),
            ComponentType.Exclude<T>());

        World world = state.World;

        __itemManager = world.GetOrCreateSystemUnmanaged<GameItemSystem>().manager;

        __entityManager = world.GetOrCreateSystemUnmanaged<GameItemComponentStructChangeSystem>().manager.addComponentPool;
    }

    public void Update<TInitializer>(in TInitializer initializer, ref SystemState state)
        where TInitializer : struct, IGameItemInitializer<T>
    {
        if (!__itemManager.isCreated)
            return;

        var entityManager = __entityManager.Create();

        GameItemComponentInit<T, TInitializer> init;
        init.initializer = initializer;
        init.infos = __itemManager.value.readOnlyInfos;
        init.entityType = state.GetEntityTypeHandle();
        init.instanceType = state.GetComponentTypeHandle<GameItemData>(true);
        init.entityManager = entityManager.parallelWriter;

        ref var lookupJobManager = ref __itemManager.lookupJobManager;

        var jobHandle = init.ScheduleParallel(__group, JobHandle.CombineDependencies(lookupJobManager.readOnlyJobHandle, state.Dependency));

        lookupJobManager.AddReadOnlyDependency(jobHandle);

        entityManager.AddJobHandleForProducer<GameItemComponentInit<T, TInitializer>>(jobHandle);

        state.Dependency = jobHandle;
    }
}

[UpdateInGroup(typeof(GameItemSystemGroup), OrderLast = true)/*, UpdateBefore(typeof(EndFrameEntityCommandSystemGroup)), UpdateAfter(typeof(GameItemSystem))*/]
public struct GameItemComponentDataChangeSystemCore<TValue, TInitializer> : IDisposable
    where TValue : unmanaged, IGameItemComponentData<TValue>
    where TInitializer : struct, IGameItemInitializer<TValue>
{
    private EntityQuery __structChangeManagerGroup;
    private GameItemManagerShared __itemManager;

    public SharedList<GameItemChangeResult<TValue>> results
    {
        get;

        private set;
    }

    public GameItemComponentDataChangeSystemCore(ref SystemState state)
    {
        BurstUtility.InitializeJob<GameItemComponentChange<TValue, TInitializer>>();

        __structChangeManagerGroup = GameItemStructChangeManager.GetEntityQuery(ref state);

        World world = state.World;
        __itemManager = world.GetOrCreateSystemUnmanaged<GameItemSystem>().manager;

        results = new SharedList<GameItemChangeResult<TValue>>(Allocator.Persistent);
    }

    public void Dispose()
    {
        results.Dispose();
    }

    public void Update(in TInitializer initializer, ref SystemState state)
    {
        ref var lookupJobManager = ref results.lookupJobManager;
        lookupJobManager.CompleteReadWriteDependency();

        var handleEntities = __structChangeManagerGroup.GetSingleton<GameItemStructChangeManager>().handleEntities;

        GameItemComponentChange<TValue, TInitializer> change;
        change.initializer = initializer;
        change.entities = handleEntities.reader;
        change.commands = __itemManager.oldCommands;
        change.values = state.GetComponentLookup<TValue>();
        change.results = results.writer;

        ref var itemJobManager = ref __itemManager.lookupJobManager;
        ref var entityJobManager = ref handleEntities.lookupJobManager;

        var jobHandle = JobHandle.CombineDependencies(itemJobManager.readOnlyJobHandle, entityJobManager.readOnlyJobHandle, state.Dependency);

        jobHandle = change.Schedule(jobHandle);

        itemJobManager.AddReadOnlyDependency(jobHandle);

        entityJobManager.AddReadOnlyDependency(jobHandle);

        lookupJobManager.readWriteJobHandle = jobHandle;

        state.Dependency = jobHandle;
    }
}

[UpdateInGroup(typeof(GameItemInitSystemGroup), OrderLast = true)/*, UpdateAfter(typeof(GameItemBeginSystem))*/]
public struct GameItemComponentDataApplySystemCore<T> where T : unmanaged, IGameItemComponentData<T>
{
    private EntityQuery __structChangeManagerGroup;

    public GameItemComponentDataApplySystemCore(ref SystemState state)
    {
        BurstUtility.InitializeJob<GameItemComponentApply<T>>();

        __structChangeManagerGroup = GameItemStructChangeManager.GetEntityQuery(ref state);
    }

    public void Update(ref SharedList<GameItemChangeResult<T>> results, ref SystemState state)
    {
        var handleEntities = __structChangeManagerGroup.GetSingleton<GameItemStructChangeManager>().handleEntities;

        GameItemComponentApply<T> apply;
        apply.results = results.reader;
        apply.entities = handleEntities.reader;
        apply.values = state.GetComponentLookup<T>();

        ref var entityJobManager = ref handleEntities.lookupJobManager;
        ref var resultJobManager = ref results.lookupJobManager;

        JobHandle jobHandle = JobHandle.CombineDependencies(entityJobManager.readOnlyJobHandle, resultJobManager.readOnlyJobHandle, state.Dependency);
        jobHandle = apply.Schedule(jobHandle);

        entityJobManager.AddReadOnlyDependency(jobHandle);
        resultJobManager.AddReadOnlyDependency(jobHandle);
        state.Dependency = jobHandle;
    }
}

[UpdateInGroup(typeof(GameItemComponentInitSystemGroup), OrderFirst = true)/*, UpdateAfter(typeof(BeginFrameEntityCommandSystem))*/]
public struct GameItemComponentDataInitSystemCore<T> where T : struct, IComponentData
{
    private EntityQuery __group;
    private GameItemManagerShared __itemManager;
    private EntityAddDataPool __entityManager;

    public GameItemComponentDataInitSystemCore(ref SystemState state)
    {
        //BurstUtility.InitializeJob<GameItemDiposeAll>();

        __group = state.GetEntityQuery(
            ComponentType.ReadOnly<GameItemData>(),
            ComponentType.Exclude<GameItemType>(),
            ComponentType.Exclude<T>());

        World world = state.World;

        __itemManager = world.GetOrCreateSystemUnmanaged<GameItemSystem>().manager;

        __entityManager = world.GetOrCreateSystemUnmanaged<GameItemComponentStructChangeSystem>().addDataPool;
    }

    public void Update<TInitializer, TFactory>(in TFactory factory, ref SystemState state)
        where TInitializer : unmanaged, IGameItemComponentInitializer<T>
        where TFactory : struct, IGameItemComponentFactory<TInitializer>
    {
        if (!__itemManager.isCreated)
            return;

        //var entityCount = new NativeArray<int>(1, Allocator.TempJob, NativeArrayOptions.ClearMemory);
        //var jobHandle = __group.CalculateEntityCountAsync(entityCount, state.Dependency);
        var jobHandle = state.Dependency;
        int entityCount = __group.CalculateEntityCount();

        var entityManager = __entityManager.Create();

        GameItemComponentDataInit<T, TInitializer, TFactory> init;
        init.factory = factory;
        init.infos = __itemManager.value.readOnlyInfos;
        init.entityType = state.GetEntityTypeHandle();
        init.instanceType = state.GetComponentTypeHandle<GameItemData>(true);
        init.entityManager = entityManager.AsComponentParallelWriter<T>(entityCount/*, ref jobHandle*/);

        ref var lookupJobManager = ref __itemManager.lookupJobManager;

        jobHandle = init.ScheduleParallel(__group, JobHandle.CombineDependencies(lookupJobManager.readOnlyJobHandle, jobHandle));

        lookupJobManager.AddReadOnlyDependency(jobHandle);

        entityManager.AddJobHandleForProducer<GameItemComponentDataInit<T, TInitializer, TFactory>>(jobHandle);

        /*GameItemDiposeAll diposeAll;
        diposeAll.entityCount = entityCount;*/
        state.Dependency = jobHandle;// diposeAll.Schedule(jobHandle);
    }
}

[UpdateInGroup(typeof(GameItemInitSystemGroup))]
public partial class GameItemComponentInitSystemGroup : ComponentSystemGroup
{

}