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

    public GameAreaCreateEntityInitializer(int areaIndex, int prefabIndex)
    {
        __areaIndex = areaIndex;
        __prefabIndex = prefabIndex;
    }

    public void Invoke<T>(ref T gameObjectEntity) where T : IGameObjectEntity
    {
        GameAreaNode node;
        node.areaIndex = __areaIndex;
        gameObjectEntity.AddComponentData(node);
        
        if (__prefabIndex != -1)
        {
            GameAreaInstance instance;
            instance.prefabIndex = __prefabIndex;
            gameObjectEntity.AddComponentData(instance);
        }
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

        public Initializer(int areaIndex, int prefabIndex)
        {
            __areaIndex = areaIndex;
            __prefabIndex = prefabIndex;
        }

        public void Invoke<T>(ref T gameObjectEntity) where T : IGameObjectEntity
        {
            GameAreaNode node;
            node.areaIndex = __areaIndex;
            gameObjectEntity.AddComponentData(node);

            GameAreaPrefab prefab;
            prefab.areaIndex = __areaIndex;
            gameObjectEntity.AddComponentData(prefab);
            
            GameAreaInstance instance;
            instance.prefabIndex = __prefabIndex;
            gameObjectEntity.AddComponentData(instance);
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

            Create(command.typeIndex, command.prefabIndex, command.transform, new Initializer(command.areaIndex, command.prefabIndex));

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

    private EntityTypeHandle __entityType;
    private ComponentTypeHandle<GameAreaNode> __nodeType;
    private ComponentTypeHandle<GameAreaInstance> __instanceType;

    private TimeManager<GameAreaInternalInstance> __timeManager;

    private NativeList<GameAreaInternalInstance> __commands;

    private NativeParallelHashMap<int, int> __areaIndices;
    private NativeParallelHashMap<int, double> __areaCreatedTimes;
    private NativeFactory<GameAreaInternalInstance> __instances;

    public GameAreaPrefabSystemCore(ref SystemState systemState)
    {
        using(var builder = new EntityQueryBuilder(Allocator.Temp))
            __definitionGroup = builder
                    .WithAll<GameAreaPrefabData>()
                    .Build(ref systemState);

        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __instanceGroup = builder
                    .WithAll<GameAreaNode, GameAreaInstance>()
                    .WithNone<GameAreaPrefab>()
                    .WithOptions(EntityQueryOptions.IncludeDisabledEntities)
                    .Build(ref systemState);

        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __viewerGroup = builder
                    .WithAll<GameAreaNode, GameAreaViewer>()
                    .Build(ref systemState);

        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __destroiedActorGroup = builder
                    .WithAll<GameAreaInstance>()
                    .WithNone<GameAreaNode>()
                    .Build(ref systemState);

        __entityType = systemState.GetEntityTypeHandle();
        __nodeType = systemState.GetComponentTypeHandle<GameAreaNode>(true);
        __instanceType = systemState.GetComponentTypeHandle<GameAreaInstance>(true);

        __timeManager = new TimeManager<GameAreaInternalInstance>(Allocator.Persistent);

        __commands = new NativeList<GameAreaInternalInstance>(Allocator.Persistent);

        __areaIndices = new NativeParallelHashMap<int, int>(1, Allocator.Persistent);

        __areaCreatedTimes = new NativeParallelHashMap<int, double>(1, Allocator.Persistent);

        __instances = new NativeFactory<GameAreaInternalInstance>(Allocator.Persistent, true);
    }

    public void Dispose()
    {
        __timeManager.Dispose();

        __commands.Dispose();

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

        var entityType = __entityType.UpdateAsRef(ref systemState);
        var nodeType = __nodeType.UpdateAsRef(ref systemState);
        var instanceType = __instanceType.UpdateAsRef(ref systemState);
        var instancesParallelWriter = __instances.parallelWriter;

        JobHandle inputDeps = systemState.Dependency, jobHandle = inputDeps;

        double time = systemState.WorldUnmanaged.Time.ElapsedTime;// this.now;

        __areaIndices.Capacity = math.max(__areaIndices.Capacity, definition.Value.prefabs.Length);
        var areaIndicesParallelWriter = __areaIndices.AsParallelWriter();

        if (!__instanceGroup.IsEmpty)
        {
            var addComponentCommander = addComponentCommanderPool.Create();

            GameAreaCollectInstanceAreaIndices collectInstanceAreaIndices;
            collectInstanceAreaIndices.entityType = entityType;
            collectInstanceAreaIndices.nodeType = nodeType;
            collectInstanceAreaIndices.instanceType = instanceType;
            collectInstanceAreaIndices.areaIndices = areaIndicesParallelWriter;
            collectInstanceAreaIndices.entityManager = addComponentCommander.parallelWriter;
            jobHandle = collectInstanceAreaIndices.ScheduleParallelByRef(__instanceGroup, inputDeps);

            addComponentCommander.AddJobHandleForProducer<GameAreaCollectInstanceAreaIndices>(jobHandle);
        }

        if (!__viewerGroup.IsEmpty)
        {
            var areaCreatedTimesParallelWriter = __areaCreatedTimes.AsParallelWriter();

            var entityCount = new NativeArray<int>(1, Allocator.TempJob, NativeArrayOptions.ClearMemory);

            var resizeCreatedTimesJob = __viewerGroup.CalculateEntityCountAsync(entityCount, inputDeps);

            GameAreaResizeCreatedTimes resizeCreatedTimes;
            resizeCreatedTimes.maxNeighborCount = maxNeighborCount;
            resizeCreatedTimes.entityCount = entityCount;
            resizeCreatedTimes.areaCreatedTimes = __areaCreatedTimes;

            resizeCreatedTimesJob = resizeCreatedTimes.Schedule(resizeCreatedTimesJob);

            //int count = __viewerGroup.CalculateEntityCount() * (maxNeighborCount + 1);

            jobHandle = JobHandle.CombineDependencies(jobHandle, resizeCreatedTimesJob);//areaCreatedTimes.Resize(count, inputDeps));

            systemState.Dependency = jobHandle;

            GameAreaInit<TNeighborEnumerable> createAreas;

            handler.GetNeighborEnumerableAndPrefabIndices(
                definition, 
                ref __areaIndices, 
                ref systemState, 
                out createAreas.neighborEnumerable, 
                out createAreas.prefabIndices);

            createAreas.time = time;
            createAreas.nodeType = nodeType;
            createAreas.areaIndices = areaIndicesParallelWriter;
            createAreas.areaCreatedTimes = areaCreatedTimesParallelWriter;
            createAreas.instances = instancesParallelWriter;
            jobHandle = createAreas.ScheduleParallelByRef(__viewerGroup, systemState.Dependency);
        }

        if (!__destroiedActorGroup.IsEmpty)
        {
            var removeComponentCommander = removeComponentCommanderPool.Create();

            GameAreaRecreateNodes recreateNodes;
            recreateNodes.entityType = entityType;
            recreateNodes.instanceType = instanceType;
            recreateNodes.areaIndices = __areaIndices;
            recreateNodes.instances = instancesParallelWriter;
            recreateNodes.entityManager = removeComponentCommander.parallelWriter;
            jobHandle = recreateNodes.ScheduleParallelByRef(__destroiedActorGroup, jobHandle);

            removeComponentCommander.AddJobHandleForProducer<GameAreaRecreateNodes>(jobHandle);
        }

        var createEntityCommander = createEntityCommanderPool.Create();
        var entityManager = createEntityCommander.parallelWriter;

        GameAreaTriggerCreateNodeEvents triggerCreateNodeEvents;
        triggerCreateNodeEvents.time = time;
        triggerCreateNodeEvents.definition = definition;
        triggerCreateNodeEvents.instances = __instances;
        triggerCreateNodeEvents.timeManager = __timeManager.writer;
        triggerCreateNodeEvents.entityManager = createEntityCommander.writer;
        inputDeps = triggerCreateNodeEvents.ScheduleByRef(jobHandle);

        __commands.Clear();

        inputDeps = __timeManager.Schedule(time, ref __commands, inputDeps);

        long hash = math.aslong(time);

        systemState.Dependency = jobHandle;

        GameAreaInvokeCommands<TValidator> invokeCommands;

        handler.GetValidatorAndVersions(ref systemState, out invokeCommands.validator, out var versions);

        ref var versionJobManager = ref versions.lookupJobManager;

        invokeCommands.random = new Random((uint)hash ^ (uint)(hash >> 32));
        invokeCommands.definition = definition;
        invokeCommands.commands = __commands.AsDeferredJobArray();
        invokeCommands.instances = instancesParallelWriter;
        invokeCommands.entityManager = entityManager;
        invokeCommands.versions = versions.parallelWriter;
        jobHandle = invokeCommands.ScheduleByRef(
            __commands, 
            innerloopBatchCount, 
            JobHandle.CombineDependencies(inputDeps, systemState.Dependency, versionJobManager.readWriteJobHandle));

        createEntityCommander.AddJobHandleForProducer<GameAreaInvokeCommands<TValidator>>(jobHandle);

        versionJobManager.readWriteJobHandle = jobHandle;

        systemState.Dependency = jobHandle;
    }
}