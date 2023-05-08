using System;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Entities;
using Unity.Physics;
using ZG;
using Random = Unity.Mathematics.Random;

public struct GameAreaCreateEntityInitializer : IEntityDataInitializer
{
    private int __areaIndex;
    private int __prefabIndex;

    public World world
    {
        get;

        private set;
    }
    
    public GameAreaCreateEntityInitializer(int areaIndex, int prefabIndex, World world)
    {
        __areaIndex = areaIndex;
        __prefabIndex = prefabIndex;

        this.world = world;
    }

    public GameObjectEntityWrapper Invoke(Entity entity)
    {
        var gameObjectEntity = new GameObjectEntityWrapper(entity, world);

        GameAreaNode node;
        node.areaIndex = __areaIndex;
        gameObjectEntity.AddComponentData(node);
        
        if (__prefabIndex != -1)
        {
            GameAreaInstance instance;
            instance.prefabIndex = __prefabIndex;
            gameObjectEntity.AddComponentData(instance);
        }

        return gameObjectEntity;
    }
}

public struct GameAreaViewer : IComponentData
{

}

public struct GameAreaNode : IComponentData
{
    public int areaIndex;
}

public struct GameAreaPrefab : IComponentData
{
    public int areaIndex;
}

public struct GameAreaInstance : ICleanupComponentData
{
    public int prefabIndex;
}

public struct GameAreaCreateNodeCommand
{
    public int typeIndex;
    public int prefabIndex;
    public int areaIndex;
    public RigidTransform transform;
}

public abstract class GameAreaCreateEntityCommander : IEntityCommander<GameAreaCreateNodeCommand>
{
    private struct Initializer : IEntityDataInitializer
    {
        private int __areaIndex;
        private int __prefabIndex;

        public World world
        {
            get;

            private set;
        }
        
        public Initializer(int areaIndex, int prefabIndex, World world)
        {
            __areaIndex = areaIndex;
            __prefabIndex = prefabIndex;
            this.world = world;
        }

        public GameObjectEntityWrapper Invoke(Entity entity)
        {
            var gameObjectEntity = new GameObjectEntityWrapper(entity, world);

            GameAreaNode node;
            node.areaIndex = __areaIndex;
            gameObjectEntity.AddComponentData(node);

            GameAreaPrefab prefab;
            prefab.areaIndex = __areaIndex;
            gameObjectEntity.AddComponentData(prefab);
            
            GameAreaInstance instance;
            instance.prefabIndex = __prefabIndex;
            gameObjectEntity.AddComponentData(instance);

            return gameObjectEntity;
        }
    }

    private bool __isComplete;

    public virtual int createCountPerTime => 0;

    public abstract void Create<T>(int typeIndex, int prefabIndex, in RigidTransform transform, in T initializer) where T : IEntityDataInitializer;

    public abstract void Complete();
    
    public void Execute(
        EntityCommandPool<GameAreaCreateNodeCommand>.Context context, 
        EntityCommandSystem system,
        ref NativeParallelHashMap<ComponentType, JobHandle> dependency,
        in JobHandle inputDeps)
    {
        int count = createCountPerTime;
        if (count < 0)
            return;

        bool isCreating = false;
        World world = system.World;
        while (context.TryDequeue(out var command))
        {
            isCreating = true;

            dependency.CompleteAll(inputDeps);

            Create(command.typeIndex, command.prefabIndex, command.transform, new Initializer(command.areaIndex, command.prefabIndex, world));

            if(count > 0 && --count < 1)
            {
                Complete();

                __isComplete = true;

                return;
            }
        }

        if (isCreating)
            __isComplete = false;
        else if(!__isComplete)
        {
            Complete();

            __isComplete = true;
        }
    }

    void IDisposable.Dispose()
    {

    }
}

public struct GameAreaPrefabSystemCore
{
    private EntityQuery __definitionGroup;
    private EntityQuery __instanceGroup;
    private EntityQuery __viewerGroup;
    private EntityQuery __destroiedActorGroup;

    private TimeManager<GameAreaInternalInstance> __timeManager;

    private NativeParallelHashMap<int, int> __areaIndices;
    private NativeParallelHashMap<int, double> __areaCreatedTimes;
    private NativeFactory<GameAreaInternalInstance> __instances;

    public GameAreaPrefabSystemCore(ref SystemState systemState)
    {
        BurstUtility.InitializeJob<GameAreaResizeCreatedTimes>();
        BurstUtility.InitializeJob<GameAreaTriggerCreateNodeEvents>();

        __definitionGroup = systemState.GetEntityQuery(ComponentType.ReadOnly<GameAreaPrefabData>());

        __instanceGroup = systemState.GetEntityQuery(
            new EntityQueryDesc()
            {
                All = new ComponentType[]
                {
                    ComponentType.ReadOnly<GameAreaNode>(),
                    ComponentType.ReadOnly<GameAreaInstance>()
                },

                None = new ComponentType[]
                {
                    typeof(GameAreaPrefab)
                }, 

                Options = EntityQueryOptions.IncludeDisabledEntities
            });
        
        __viewerGroup = systemState.GetEntityQuery(
            ComponentType.ReadOnly<GameAreaNode>(),
            ComponentType.ReadOnly<GameAreaViewer>());

        __destroiedActorGroup = systemState.GetEntityQuery(
            ComponentType.ReadOnly<GameAreaInstance>(),
            ComponentType.Exclude<GameAreaNode>());

        __timeManager = new TimeManager<GameAreaInternalInstance>(Allocator.Persistent);

        __areaIndices = new NativeParallelHashMap<int, int>(1, Allocator.Persistent);

        __areaCreatedTimes = new NativeParallelHashMap<int, double>(1, Allocator.Persistent);

        __instances = new NativeFactory<GameAreaInternalInstance>(Allocator.Persistent, true);
    }

    public void Dispose()
    {
        __timeManager.Dispose();

        __areaIndices.Dispose();

        __areaCreatedTimes.Dispose();

        __instances.Dispose();
    }

    public void Update<TNeighborEnumerable, TValidator, THandler>(
        int maxNeighborCount, 
        int innerloopBatchCount, 
        EntityCommandPool<EntityData<GameAreaPrefab>> addComponentCommanderPool,
        EntityCommandPool<Entity> removeComponentCommanderPool,
        EntityCommandPool<GameAreaCreateNodeCommand> createEntityCommanderPool,
        ref THandler handler, 
        ref SystemState systemState)
        where TNeighborEnumerable : struct, IGameAreaNeighborEnumerable
        where TValidator : struct, IGameAreaValidator
        where THandler : struct, IGameAreaHandler<TNeighborEnumerable, TValidator>
    {
        /*if(!__addComponentCommander.isCreated)
            __addComponentCommander = _endFrameBarrier.CreateAddComponentDataCommander<GameAreaPrefab>();*/
        if (__definitionGroup.IsEmpty)
            return;

        var definition = __definitionGroup.GetSingleton<GameAreaPrefabData>().definition;

        var nodeType = systemState.GetComponentTypeHandle<GameAreaNode>(true);
        var instanceType = systemState.GetComponentTypeHandle<GameAreaInstance>(true);
        NativeFactory<GameAreaInternalInstance> instances = __instances;
        var instancesParallelWriter = instances.parallelWriter;

        NativeParallelHashMap<int, int> areaIndices = __areaIndices;
        areaIndices.Capacity = math.max(areaIndices.Capacity, definition.Value.prefabs.Length);
        var areaIndicesParallelWriter = areaIndices.AsParallelWriter();

        NativeParallelHashMap<int, double> areaCreatedTimes = __areaCreatedTimes;
        var areaCreatedTimesParallelWriter = areaCreatedTimes.AsParallelWriter();

        var inputDeps = systemState.Dependency;

        var entityCount = new NativeArray<int>(1, Allocator.TempJob, NativeArrayOptions.ClearMemory);

        var resizeCreatedTimesJob = __viewerGroup.CalculateEntityCountAsync(entityCount, inputDeps);

        GameAreaResizeCreatedTimes resizeCreatedTimes;
        resizeCreatedTimes.maxNeighborCount = maxNeighborCount;
        resizeCreatedTimes.entityCount = entityCount;
        resizeCreatedTimes.areaCreatedTimes = areaCreatedTimes;

        resizeCreatedTimesJob = resizeCreatedTimes.Schedule(resizeCreatedTimesJob);

        var addComponentCommander = addComponentCommanderPool.Create();

        GameAreaCollectInstanceAreaIndices collectInstanceAreaIndices;
        collectInstanceAreaIndices.entityType = systemState.GetEntityTypeHandle();
        collectInstanceAreaIndices.nodeType = nodeType;
        collectInstanceAreaIndices.instanceType = instanceType;
        collectInstanceAreaIndices.areaIndices = areaIndicesParallelWriter;
        collectInstanceAreaIndices.entityManager = addComponentCommander.parallelWriter;
        JobHandle jobHandle = collectInstanceAreaIndices.ScheduleParallel(__instanceGroup, inputDeps);

        addComponentCommander.AddJobHandleForProducer<GameAreaCollectInstanceAreaIndices>(jobHandle);

        //int count = __viewerGroup.CalculateEntityCount() * (maxNeighborCount + 1);

        jobHandle = JobHandle.CombineDependencies(jobHandle, resizeCreatedTimesJob);//areaCreatedTimes.Resize(count, inputDeps));

        double time = systemState.WorldUnmanaged.Time.ElapsedTime;// this.now;

        GameAreaInit<TNeighborEnumerable> createAreas;

        handler.GetNeighborEnumerableAndPrefabIndices(definition, ref systemState, ref jobHandle, out createAreas.neighborEnumerable, out createAreas.prefabIndices);

        createAreas.time = time;
        createAreas.nodeType = nodeType;
        createAreas.areaIndices = areaIndicesParallelWriter;
        createAreas.areaCreatedTimes = areaCreatedTimesParallelWriter;
        createAreas.instances = instancesParallelWriter;
        inputDeps = createAreas.ScheduleParallel(__viewerGroup, jobHandle);

        var removeComponentCommander = removeComponentCommanderPool.Create();

        GameAreaRecreateNodes recreateNodes;
        recreateNodes.entityType = systemState.GetEntityTypeHandle();
        recreateNodes.instanceType = instanceType;
        recreateNodes.areaIndices = areaIndices;
        recreateNodes.instances = instancesParallelWriter;
        recreateNodes.entityManager = removeComponentCommander.parallelWriter;
        inputDeps = recreateNodes.ScheduleParallel(__destroiedActorGroup, inputDeps);

        removeComponentCommander.AddJobHandleForProducer<GameAreaRecreateNodes>(inputDeps);

        var createEntityCommander = createEntityCommanderPool.Create();
        var entityManager = createEntityCommander.parallelWriter;

        GameAreaTriggerCreateNodeEvents triggerCreateNodeEvents;
        triggerCreateNodeEvents.time = time;
        triggerCreateNodeEvents.definition = definition;
        triggerCreateNodeEvents.instances = instances;
        triggerCreateNodeEvents.timeManager = __timeManager.writer;
        triggerCreateNodeEvents.entityManager = createEntityCommander.writer;
        JobHandle entityManagerJob = triggerCreateNodeEvents.Schedule(inputDeps);

        __timeManager.Flush();

        inputDeps = __timeManager.Schedule(time, entityManagerJob);

        long hash = math.aslong(time);

        GameAreaInvokeCommands<TValidator> invokeCommands;
        invokeCommands.random = new Random((uint)hash ^ (uint)(hash >> 32));
        invokeCommands.definition = definition;
        invokeCommands.commands = __timeManager.values;
        invokeCommands.instances = instancesParallelWriter;
        invokeCommands.entityManager = entityManager;
        invokeCommands.validator = handler.GetValidator(ref systemState, ref inputDeps);
        entityManagerJob = __timeManager.ScheduleParallel(ref invokeCommands, innerloopBatchCount, inputDeps);

        inputDeps = entityManagerJob;// _values.Clear(entityManagerJob);

        createEntityCommander.AddJobHandleForProducer<GameAreaInvokeCommands<TValidator>>(entityManagerJob);

        systemState.Dependency = JobHandle.CombineDependencies(jobHandle, inputDeps);
    }
}