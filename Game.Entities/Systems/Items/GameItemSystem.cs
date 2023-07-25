using System;
using Unity.Jobs;
using Unity.Burst;
using Unity.Entities;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using ZG;
using Handle = GameItemHandle;

[Serializable]
public struct GameItemIdentityType : IComponentData
{
    public int value;
}

[Serializable]
public struct GameItemData : IComponentData
{
    public Handle handle;
}

/*public abstract class GameItemCommander : IEntityCommander<Handle>
{
    private struct Value
    {
        public Handle handle;
        public EntityDataIdentity identity;
    }

    private struct Init : IJobParallelFor
    {
        [ReadOnly, DeallocateOnJobCompletion]
        public NativeArray<Entity> entityArray;
        public NativeArray<Value> values;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<EntityDataIdentity> identities;
        [NativeDisableParallelForRestriction]
        public ComponentLookup<GameItemData> instances;

        public void Execute(int index)
        {
            Entity entity = entityArray[index];
            var value = values[index];
            identities[entity] = value.identity;
            GameItemData instance;
            instance.handle = value.handle;
            instances[entity] = instance;
        }
    }

    public const int innerloopBatchCount = 32;

    internal EntityArchetype _type;

    internal GameItemManagerShared _itemManager;

    public abstract EntityDataIdentity GetIdentity(
        EntityCommandSystem system,
        in GameItemManager manager,
        in Handle handle);

    public void Execute(
        EntityCommandPool<Handle>.Context context, 
        EntityCommandSystem system,
        ref NativeHashMap<ComponentType, JobHandle> dependency,
        in JobHandle inputDeps)
    {
        var values = new NativeList<Value>(Allocator.TempJob);
        {
            _itemManager.lookupJobManager.CompleteReadOnlyDependency();

            var manager = _itemManager.value;
            Value value;
            while (context.TryDequeue(out value.handle))
            {
                value.identity = GetIdentity(system, manager, value.handle);

                values.Add(value);
            }

            int length = values.Length;
            if (length > 0)
            {
                dependency.CompleteAll(inputDeps);

                Init init;
                init.entityArray = system.EntityManager.CreateEntity(_type, length, Allocator.TempJob);
                init.values = values;
                init.identities = system.GetComponentLookup<EntityDataIdentity>();
                init.instances = system.GetComponentLookup<GameItemData>();

                var jobHandle = init.Schedule(length, innerloopBatchCount, inputDeps);

                jobHandle = values.Dispose(jobHandle);

                dependency[typeof(EntityDataIdentity)] = jobHandle;
                dependency[typeof(GameItemData)] = jobHandle;

                return;
            }
        }
        values.Dispose();
    }

    void IDisposable.Dispose()
    {

    }
}*/

public struct GameItemStructChangeFactory
{
    private struct Comparer : System.Collections.Generic.IComparer<KeyValue<Handle, EntityArchetype>>
    {
        public int Compare(KeyValue<Handle, EntityArchetype> x, KeyValue<Handle, EntityArchetype> y)
        {
            return x.Value.GetHashCode() - y.Value.GetHashCode();
        }
    }

    [BurstCompile]
    private struct Collect : IJobParallelFor
    {
        [ReadOnly, DeallocateOnJobCompletion]
        public NativeArray<KeyValue<Handle, EntityArchetype>> items;

        [ReadOnly, DeallocateOnJobCompletion]
        public NativeArray<Entity> entityArray;

        public SharedHashMap<Entity, Entity>.Writer entityHandles;

        public SharedHashMap<Entity, Entity>.Writer handleEntities;

        public void Execute(int index)
        {
            Entity source = Convert(items[index].Key), destination = entityArray[index];

            entityHandles[destination] = source;

            handleEntities[source] = destination;
        }
    }

    public SharedHashMap<Entity, Entity> entityHandles
    {
        get;
    }

    public SharedHashMap<Entity, Entity> handleEntities
    {
        get;
    }

    public SharedHashMap<Handle, EntityArchetype> createEntityCommander
    {
        get;
    }

    public static Entity Convert(in Handle handle)
    {
        Entity entity;
        entity.Index = -(handle.index + 1);
        entity.Version = handle.version;
        return entity;
    }

    public static Handle Convert(in Entity entity)
    {
        Handle handle;
        handle.index = -entity.Index - 1;
        handle.version = entity.Version;
        return handle;
    }

    public GameItemStructChangeFactory(
        SharedHashMap<Entity, Entity> entityHandles,
        SharedHashMap<Entity, Entity> handleEntities,
        SharedHashMap<Handle, EntityArchetype> createEntityCommander)
    {
        this.entityHandles = entityHandles;
        this.handleEntities = handleEntities;
        this.createEntityCommander = createEntityCommander;
    }

    public int Playback(ref SystemState state)
    {
        var entityManager = state.EntityManager;

        var handleEntities = this.handleEntities;
        var entityHandles = this.entityHandles;

        var createEntityCommander = this.createEntityCommander;
        createEntityCommander.lookupJobManager.CompleteReadWriteDependency();

        var writer = createEntityCommander.writer;
        int entityCountToCreated = writer.Count();
        if (entityCountToCreated > 0)
        {
            int index = 0;
            var items = new NativeArray<KeyValue<Handle, EntityArchetype>>(entityCountToCreated, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            var enumerator = createEntityCommander.GetEnumerator();
            while (enumerator.MoveNext())
                items[index++] = enumerator.Current;

            writer.Clear();

            items.Sort(new Comparer());

            int offset = 0, length = 1;
            EntityArchetype oldEntityArchetype = items[0].Value, entityArchetype;
            KeyValue<Handle, EntityArchetype> item;
            var entityArray = new NativeArray<Entity>(entityCountToCreated, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            for (int i = 1; i < entityCountToCreated; ++i)
            {
                item = items[i];

                entityArchetype = item.Value;
                if (entityArchetype == oldEntityArchetype)
                    ++length;
                else
                {
                    entityManager.CreateEntity(oldEntityArchetype, entityArray.GetSubArray(offset, length));

                    oldEntityArchetype = entityArchetype;

                    offset += length;

                    length = 1;
                }
            }

            entityManager.CreateEntity(oldEntityArchetype, entityArray.GetSubArray(offset, length));

            UnityEngine.Assertions.Assert.AreEqual(entityArray.Length, offset + length);

            entityHandles.lookupJobManager.CompleteReadWriteDependency();

            handleEntities.lookupJobManager.CompleteReadWriteDependency();

            Collect collect;
            collect.items = items;
            collect.entityArray = entityArray;
            collect.entityHandles = entityHandles.writer;
            collect.handleEntities = handleEntities.writer;
            //systemState.Dependency = collect.Schedule(systemState.Dependency);
            collect.Run(entityCountToCreated);
        }

        return entityCountToCreated;
    }

    public void Assign(ref SystemState state, EntityComponentAssigner assigner)
    {
        assigner.Playback(ref state, handleEntities.reader, entityHandles);
    }
}

public struct GameItemStructChangeManager : IComponentData
{
    [BurstCompile]
    private struct Clear : IJob
    {
        public NativeList<Entity> entities;

        public SharedHashMap<Entity, Entity>.Writer entityHandles;

        public SharedHashMap<Entity, Entity>.Writer handleEntities;

        public void Execute()
        {
            Entity entity;
            int numEntities = entities.Length;
            for (int i = 0; i < numEntities; ++i)
            {
                entity = entities[i];
                handleEntities.Remove(entityHandles[entity]);
                entityHandles.Remove(entity);
            }
        }
    }

    //private EntityQuery __group;
    private EntityCommandPool<Entity>.Context __destroyEntityCommander;

    public EntityCommandPool<Entity> destroyEntityCommander
    {
        get => __destroyEntityCommander.pool;
    }

    public SharedHashMap<Handle, EntityArchetype> createEntityCommander
    {
        get;
    }

    public EntityComponentAssigner assigner
    {
        get;
    }

    public SharedHashMap<Entity, Entity> entityHandles
    {
        get;
    }

    public SharedHashMap<Entity, Entity> handleEntities
    {
        get;
    }

    public GameItemStructChangeFactory factory => new GameItemStructChangeFactory(entityHandles, handleEntities, createEntityCommander);

    public static EntityQuery GetEntityQuery(ref SystemState state)
    {
        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            return builder
                .WithAllRW<GameItemStructChangeManager>()
                .WithOptions(EntityQueryOptions.IncludeSystems)
                .Build(ref state);
    }

    public GameItemStructChangeManager(Allocator allocator)
    {
        __destroyEntityCommander = new EntityCommandPool<Entity>.Context(allocator);
        createEntityCommander = new SharedHashMap<Handle, EntityArchetype>(allocator);
        assigner = new EntityComponentAssigner(allocator);
        entityHandles = new SharedHashMap<Entity, Entity>(allocator);
        handleEntities = new SharedHashMap<Entity, Entity>(allocator);
    }

    public void Playback(ref SystemState state)
    {
        var factory = this.factory;
        int entityCountToCreated = factory.Playback(ref state);

        NativeList<Entity> entitiesToDestroy;
        if (__destroyEntityCommander.isEmpty)
            entitiesToDestroy = default;
        else
        {
            entitiesToDestroy = new NativeList<Entity>(Allocator.TempJob);

            __destroyEntityCommander.MoveTo(new EntityCommandEntityContainer(entitiesToDestroy));

            if (entitiesToDestroy.IsEmpty)
                entitiesToDestroy.Dispose();
            else
                state.EntityManager.DestroyEntity(entitiesToDestroy.AsArray());
        }

        if (entityCountToCreated > 0)
            factory.Assign(ref state, assigner);

        if (entitiesToDestroy.IsCreated)
        {
            var handleEntities = this.handleEntities;
            var entityHandles = this.entityHandles;

            Clear clear;
            clear.entities = entitiesToDestroy;
            clear.entityHandles = entityHandles.writer;
            clear.handleEntities = handleEntities.writer;

            ref var entityHandleJobManager = ref entityHandles.lookupJobManager;
            ref var handleEntityJobManager = ref handleEntities.lookupJobManager;

            JobHandle jobHandle;
            if (entityCountToCreated > 0)
                jobHandle = JobHandle.CombineDependencies(entityHandleJobManager.readWriteJobHandle, handleEntityJobManager.readWriteJobHandle, state.Dependency);
            else
            {
                entityHandleJobManager.CompleteReadWriteDependency();
                handleEntityJobManager.CompleteReadWriteDependency();

                jobHandle = state.Dependency;
            }

            jobHandle = clear.ScheduleByRef(jobHandle);

            entityHandleJobManager.readWriteJobHandle = jobHandle;
            handleEntityJobManager.readWriteJobHandle = jobHandle;

            state.Dependency = entitiesToDestroy.Dispose(jobHandle);
        }
    }

    public void Dispose()
    {
        __destroyEntityCommander.Dispose();
        createEntityCommander.Dispose();
        assigner.Dispose();
        entityHandles.Dispose();
        handleEntities.Dispose();
    }
}

[BurstCompile, UpdateInGroup(typeof(EndFrameEntityCommandSystemGroup))]
public partial struct GameItemStructChangeSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.EntityManager.AddComponentData(state.SystemHandle, new GameItemStructChangeManager(Allocator.Persistent));
    }

    public void OnDestroy(ref SystemState state)
    {
        state.EntityManager.GetComponentData<GameItemStructChangeManager>(state.SystemHandle).Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        state.EntityManager.GetComponentData<GameItemStructChangeManager>(state.SystemHandle).Playback(ref state);
    }
}

[UpdateInGroup(typeof(PresentationSystemGroup))/*, UpdateBefore(typeof(EndFrameEntityCommandSystemGroup))*/, UpdateAfter(typeof(CallbackSystem))]
public partial class GameItemSystemGroup : ComponentSystemGroup
{

}

[UpdateInGroup(typeof(InitializationSystemGroup)), UpdateAfter(typeof(EntityObjectSystemGroup/*BeginFrameEntityCommandSystem*/))]
public partial class GameItemInitSystemGroup : ComponentSystemGroup
{

}

/*[BurstCompile, UpdateInGroup(typeof(GameItemInitSystemGroup), OrderFirst = true)]
public partial struct GameItemEntitySystem : ISystem
{
    [BurstCompile]
    private struct Init : IJob
    {
        public int entityCount;
        [ReadOnly]
        public NativeArray<GameItemCommand> commands;
        public SharedHashMap<Handle, Entity>.Writer entities;

        public void Execute()
        {
            int length = commands.Length;
            GameItemCommand command;
            for (int i  = 0; i < length; ++i)
            {
                command = commands[i];
                if (command.commandType == GameItemCommandType.Destroy)
                    entities.Remove(command.sourceHandle);
            }

            entities.capacity = math.max(entities.capacity, entities.Count() + entityCount);
        }
    }

    private struct Rebuild
    {
        [ReadOnly]
        public NativeArray<Entity> entityArray;
        [ReadOnly]
        public NativeArray<GameItemData> instances;

        public SharedHashMap<Handle, Entity>.ParallelWriter entities;

        public void Execute(int index)
        {
            var handle = instances[index].handle;
            if (handle.Equals(Handle.empty))
            {
                UnityEngine.Debug.LogError($"GameItemSystem.Rebuild: {entityArray[index]}");

                return;
            }

            bool result = entities.TryAdd(handle, entityArray[index]);
            UnityEngine.Assertions.Assert.IsTrue(result);
        }
    }

    [BurstCompile]
    private struct RebuildEx : IJobChunk
    {
        [ReadOnly]
        public EntityTypeHandle entityType;
        [ReadOnly]
        public ComponentTypeHandle<GameItemData> instanceType;

        public SharedHashMap<Handle, Entity>.ParallelWriter entities;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Rebuild rebuild;
            rebuild.entityArray = batchInChunk.GetNativeArray(entityType);
            rebuild.instances = batchInChunk.GetNativeArray(instanceType);
            rebuild.entities = entities;

            int count = batchInChunk.Count;
            for (int i = 0; i < count; ++i)
                rebuild.Execute(i);
        }
    }

    private EntityQuery __group;
    private GameItemManagerShared __itemManager;

    public SharedHashMap<Handle, Entity> entities
    {
        get;

        private set;
    }

    public void OnCreate(ref SystemState state)
    {
        BurstUtility.InitializeJob<Init>();

        state.SetAlwaysUpdateSystem(true);

        __group = state.GetEntityQuery(
            ComponentType.ReadOnly<GameItemData>(),
            ComponentType.Exclude<GameItemType>());

        ref var system = ref state.World.GetOrCreateSystemUnmanaged<GameItemSystem>();

        __itemManager = system.manager;

        entities = system.entities;
    }

    public void OnDestroy(ref SystemState state)
    {
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var entities = this.entities;
        ref var lookupJobManager = ref entities.lookupJobManager;
        lookupJobManager.CompleteReadWriteDependency();

        ref var itemJobManager = ref __itemManager.lookupJobManager;

        Init init;
        init.entityCount = __group.CalculateEntityCount();
        init.commands = __itemManager.oldCommands;
        init.entities = entities.writer;

        var jobHandle = init.Schedule(JobHandle.CombineDependencies(itemJobManager.readOnlyJobHandle, state.Dependency));

        itemJobManager.AddReadOnlyDependency(jobHandle);

        RebuildEx rebuild;
        rebuild.entityType = state.GetEntityTypeHandle();
        rebuild.instanceType = state.GetComponentTypeHandle<GameItemData>(true);
        rebuild.entities = entities.parallelWriter;

        jobHandle = rebuild.ScheduleParallel(__group, 1, jobHandle);

        lookupJobManager.readWriteJobHandle = jobHandle;

        state.Dependency = jobHandle;
    }
}*/

[/*AlwaysUpdateSystem, */BurstCompile, UpdateInGroup(typeof(GameItemSystemGroup))]
public partial struct GameItemSystem : ISystem
{
    /*private struct ChangeType
    {
        public GameItemManager manager;

        [ReadOnly]
        public NativeArray<GameItemData> instances;
        [ReadOnly]
        public NativeArray<GameItemType> inputs;

        public void Execute(int index)
        {
            Handle sourceHandle = instances[index].handle, destinationHandle = sourceHandle;
            int destinationType = inputs[index].value, sourceType = destinationType;
            /if (outputs.CompareExchange(ref destinationHandle, ref source, out var item))
            {
                GameItemManager.Command command;
                command.count = item.count;

                command.commandType = GameItemManager.CommandType.Destroy;
                command.type = source;
                command.sourceCount = item.count;
                command.sourceParentChildIndex = item.parentChildIndex;
                command.sourceParentHandle = item.parentHandle;
                command.sourceSiblingHandle = item.siblingHandle;
                command.sourceHandle = sourceHandle;
                command.destinationCount = 0;
                command.destinationParentChildIndex = -1;
                command.destinationParentHandle = Handle.empty;
                command.destinationSiblingHandle = Handle.empty;
                command.destinationHandle = Handle.empty;
                commands.Add(command);

                command.commandType = GameItemManager.CommandType.Create;
                command.type = destination;
                command.sourceCount = 0;
                command.sourceParentChildIndex = -1;
                command.sourceParentHandle = Handle.empty;
                command.sourceSiblingHandle = Handle.empty;
                command.sourceHandle = Handle.empty;
                command.destinationCount = item.count;
                command.destinationParentChildIndex = item.parentChildIndex;
                command.destinationParentHandle = item.parentHandle;
                command.destinationSiblingHandle = item.siblingHandle;
                command.destinationHandle = destinationHandle;

                commands.Add(command);
            }/
            manager.CompareExchange(ref destinationHandle, ref sourceType, out _);
        }
    }

    [BurstCompile]
    private struct ChangeTypeEx : IJobChunk
    {
        public GameItemManager manager;

        [ReadOnly]
        public ComponentTypeHandle<GameItemData> instanceType;
        [ReadOnly]
        public ComponentTypeHandle<GameItemType> typeType;

        //public NativeList<GameItemManager.Command> commands;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            ChangeType changeType;
            changeType.manager = manager;
            changeType.instances = batchInChunk.GetNativeArray(instanceType);
            changeType.inputs = batchInChunk.GetNativeArray(typeType);
            //changeType.commands = commands;

            int count = batchInChunk.Count;
            for (int i = 0; i < count; ++i)
                changeType.Execute(i);
        }
    }*/

    [BurstCompile]
    private struct Command : IJob, IEntityCommandProducerJob
    {
        public int identityType;
        public long hash;
        public EntityArchetype entityArchetype;

        [ReadOnly]
        public ComponentTypeHandle<EntityDataIdentity> identityComponentType;

        [ReadOnly]
        public NativeList<ArchetypeChunk> identityComponentChunks;

        [ReadOnly]
        public SharedHashMap<Entity, Entity>.Reader entities;

        public NativeList<GameItemCommand> sources;

        public NativeList<GameItemCommand> destinations;

        public EntityComponentAssigner.Writer assigner;

        public SharedHashMap<Handle, EntityArchetype>.Writer createCommander;
        public EntityCommandQueue<Entity>.Writer destroyCommander;

        public Hash128 CreateGUID(ref Unity.Mathematics.Random random)
        {
            NativeArray<EntityDataIdentity> identities;
            Hash128 result;
            int i, j, numEntities, numidentityComponentChunks = identityComponentChunks.Length;

            do
            {
                result.Value = random.NextUInt4();
                for (i = 0; i < numidentityComponentChunks; ++i)
                {
                    identities = identityComponentChunks[i].GetNativeArray(ref identityComponentType);
                    numEntities = identities.Length;
                    for (j = 0; j < numEntities; ++j)
                    {
                        if (identities[j].guid == result)
                            break;
                    }

                    if (j < numEntities)
                        break;
                }
            } while (i < numidentityComponentChunks);

            return result;
        }

        public void Execute()
        {
            EntityDataIdentity identity;
            identity.type = identityType;

            GameItemData instance;

            //long hash = math.aslong(time);
            var random = new Unity.Mathematics.Random((uint)hash ^ (uint)(hash >> 32));
            GameItemCommand command, temp;
            Entity entity;
            int numCommands = sources.Length, i, j;
            for (i = 0; i < numCommands; ++i)
            {
                command = sources[i];
                switch (command.commandType)
                {
                    case GameItemCommandType.Create:
                        bool isContains = false;
                        for (j = i + 1; j < numCommands; ++j)
                        {
                            temp = sources[j];
                            if (temp.commandType == GameItemCommandType.Destroy && temp.sourceHandle.Equals(command.destinationHandle))
                            {
                                sources.RemoveAt(j);

                                --numCommands;

                                isContains = true;

                                break;
                            }
                        }

                        if (isContains)
                        {
                            sources.RemoveAt(i--);

                            --numCommands;
                        }
                        else
                        {
                            entity = GameItemStructChangeFactory.Convert(command.destinationHandle);

                            isContains = entities.ContainsKey(entity);
                            if (isContains)
                            {
                                for (j = i - 1; j >= 0; --j)
                                {
                                    temp = sources[j];
                                    if (temp.commandType == GameItemCommandType.Destroy && temp.sourceHandle.Equals(command.destinationHandle))
                                    {
                                        isContains = false;

                                        break;
                                    }
                                }
                            }

                            if (!isContains)
                            {
                                createCommander.TryAdd(command.destinationHandle, entityArchetype);

                                identity.guid = CreateGUID(ref random);// random.NextUInt4();
                                /*if (identity.guid.ToString() == "4b959ef3998812f82e760687c088b17e")
                                    UnityEngine.Debug.Log($"dfgg {identity.type}");*/

                                assigner.SetComponentData(entity, identity);

                                instance.handle = command.destinationHandle;
                                assigner.SetComponentData(entity, instance);
                            }
                        }
                        break;
                    case GameItemCommandType.Destroy:
                        if (!createCommander.Remove(command.sourceHandle))
                            destroyCommander.Enqueue(entities[GameItemStructChangeFactory.Convert(command.sourceHandle)]);

                        //entities.Remove(command.sourceHandle);
                        break;
                }
            }

            destinations.Clear();
            destinations.AddRange(sources.AsArray());

            sources.Clear();
        }
    }

    private long __seed;
    private EntityQuery __structChangeManagerGroup;
    private EntityQuery __identityTypeGroup;
    private EntityQuery __identityGroup;

    private ComponentTypeHandle<EntityDataIdentity> __identityType;

    private NativeList<GameItemCommand> __commandSources;
    private NativeList<GameItemCommand> __commandDestinations;

    public EntityArchetype entityArchetype
    {
        get;

        private set;
    }

    public GameItemManagerShared manager
    {
        get;

        private set;
    }

    public GameItemManager GetManagerReadOnly()
    {
        var manager = this.manager;

        manager.lookupJobManager.CompleteReadOnlyDependency();

        return manager.value;
    }

    public GameItemManager GetManagerReadWrite()
    {
        var manager = this.manager;

        manager.lookupJobManager.CompleteReadWriteDependency();

        return manager.value;
    }

    public void Create(GameItemDataDefinition[] datas)
    {
        var manager = this.manager;
        manager.lookupJobManager.CompleteReadWriteDependency();
        manager.Rebuild(datas);

        __seed = DateTime.UtcNow.Ticks;
    }

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        BurstUtility.InitializeJob<Command>();

        //state.SetAlwaysUpdateSystem(true);

        __structChangeManagerGroup = GameItemStructChangeManager.GetEntityQuery(ref state);

        __identityTypeGroup = state.GetEntityQuery(ComponentType.ReadOnly<GameItemIdentityType>());
        __identityGroup = state.GetEntityQuery(ComponentType.ReadOnly<EntityDataIdentity>());

        __identityType = state.GetComponentTypeHandle<EntityDataIdentity>(true);

        /*__group = state.GetEntityQuery(ComponentType.ReadOnly<GameItemData>(), ComponentType.ReadOnly<GameItemType>());
        __group.SetChangedVersionFilter(typeof(GameItemType));*/

        __commandSources = new NativeList<GameItemCommand>(Allocator.Persistent);
        __commandDestinations = new NativeList<GameItemCommand>(Allocator.Persistent);

        manager = new GameItemManagerShared(ref __commandSources, ref __commandDestinations, Allocator.Persistent);

        var componentTypes = new NativeArray<ComponentType>(2, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
        componentTypes[0] = ComponentType.ReadOnly<EntityDataIdentity>();
        componentTypes[1] = ComponentType.ReadOnly<GameItemData>();
        entityArchetype = state.EntityManager.CreateArchetype(componentTypes);
        componentTypes.Dispose();
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        //entities.Dispose();
        manager.Dispose();
        __commandSources.Dispose();
        __commandDestinations.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!__identityTypeGroup.HasSingleton<GameItemIdentityType>())
            return;

        var manager = this.manager;

        ref var lookupJobManager = ref manager.lookupJobManager;

        var jobHandle = state.Dependency;

        var structChangeManager = __structChangeManagerGroup.GetSingleton<GameItemStructChangeManager>();

        var handleEntities = structChangeManager.handleEntities;
        var assigner = structChangeManager.assigner;
        var createCommander = structChangeManager.createEntityCommander;
        var destroyCommander = structChangeManager.destroyEntityCommander.Create();

        Command command;
        command.hash = __seed + math.aslong(state.WorldUnmanaged.Time.ElapsedTime);
        command.identityType = __identityTypeGroup.GetSingleton<GameItemIdentityType>().value;
        command.entityArchetype = entityArchetype;
        command.identityComponentChunks = __identityGroup.ToArchetypeChunkListAsync(state.WorldUpdateAllocator, out var identityJobHandle);
        command.identityComponentType = __identityType.UpdateAsRef(ref state);
        command.sources = __commandSources;
        command.destinations = __commandDestinations;
        command.entities = handleEntities.reader;
        command.assigner = assigner.writer;
        command.createCommander = createCommander.writer;
        command.destroyCommander = destroyCommander.writer;

        ref var entityJobManager = ref handleEntities.lookupJobManager;

        jobHandle = JobHandle.CombineDependencies(jobHandle, entityJobManager.readOnlyJobHandle, assigner.jobHandle);

        ref var commanderJobManager = ref createCommander.lookupJobManager;

        jobHandle = JobHandle.CombineDependencies(jobHandle, commanderJobManager.readWriteJobHandle, identityJobHandle);

        jobHandle = command.Schedule(JobHandle.CombineDependencies(jobHandle, lookupJobManager.readWriteJobHandle));

        entityJobManager.AddReadOnlyDependency(jobHandle);

        assigner.jobHandle = jobHandle;

        commanderJobManager.readWriteJobHandle = jobHandle;
        destroyCommander.AddJobHandleForProducer<Command>(jobHandle);

        lookupJobManager.readWriteJobHandle = jobHandle;

        state.Dependency = jobHandle;
    }
}