using System;
using Unity.Jobs;
using Unity.Burst;
using Unity.Entities;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using ZG;
using Handle = GameItemHandle;

public struct GameItemIdentityType : IComponentData
{
    public int value;
}

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
            UnityEngine.Assertions.Assert.AreNotEqual(Handle.Empty, items[index].Key);

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

[BurstCompile, UpdateInGroup(typeof(EntityObjectSystemGroup))]
public partial struct GameItemStructChangeSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.EntityManager.AddComponentData(state.SystemHandle, new GameItemStructChangeManager(Allocator.Persistent));
    }

    //[BurstCompile]
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

[UpdateInGroup(typeof(InitializationSystemGroup)), UpdateAfter(typeof(EntityDataSystem)), UpdateBefore(typeof(EntityObjectSystemGroup))]
public partial class GameItemSystemGroup : ComponentSystemGroup
{

}

[BurstCompile, UpdateInGroup(typeof(InitializationSystemGroup)), UpdateAfter(typeof(EntityObjectSystemGroup/*BeginFrameEntityCommandSystem*/))]
public partial struct GameItemInitSystemGroup : ISystem
{
    private SystemGroup __group;

    public void OnCreate(ref SystemState state)
    {
        __group = SystemGroupUtility.GetOrCreateSystemGroup(state.World, typeof(GameItemInitSystemGroup));
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {

    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var world = state.WorldUnmanaged;
        __group.Update(ref world);
    }
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

[/*AlwaysUpdateSystem, */BurstCompile, CreateAfter(typeof(EntityDataSystem)), UpdateInGroup(typeof(GameItemSystemGroup))]
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
    private struct Recapacity : IJob
    {
        [ReadOnly]
        public NativeList<GameItemCommand> commands;

        public NativeArray<int> typeCountAndBufferSize;

        public SharedHashMap<Hash128, Entity>.Writer guidEntities;

        public SharedHashMap<Handle, EntityArchetype>.Writer createCommander;

        public void Execute()
        {
            int commandCount = commands.Length;

            typeCountAndBufferSize[0] = 2 * commandCount;
            typeCountAndBufferSize[1] = (UnsafeUtility.SizeOf<EntityDataIdentity>() + UnsafeUtility.SizeOf<GameItemData>()) * commandCount;

            guidEntities.capacity = math.max(guidEntities.capacity, guidEntities.Count() + commandCount);
            createCommander.capacity = math.max(createCommander.capacity, createCommander.Count() + commandCount);
        }
    }

    [BurstCompile]
    private struct Command : IJobParallelForDefer, IEntityCommandProducerJob
    {
        public int identityType;
        public EntityArchetype entityArchetype;
        public long hash;

        /*[ReadOnly]
        public ComponentTypeHandle<EntityDataIdentity> identityComponentType;

        [ReadOnly]
        public NativeList<ArchetypeChunk> identityComponentChunks;*/

        [ReadOnly]
        public SharedHashMap<Entity, Entity>.Reader handleEntities;

        [ReadOnly]
        public NativeArray<GameItemCommand> commands;

        public SharedHashMap<Hash128, Entity>.ParallelWriter guidEntities;

        public EntityComponentAssigner.ParallelWriter assigner;

        public SharedHashMap<Handle, EntityArchetype>.ParallelWriter createCommander;
        public EntityCommandQueue<Entity>.ParallelWriter destroyCommander;

        public Hash128 CreateGUID(int index, Entity entity)
        {
            uint hash = RandomUtility.Hash(this.hash);
            var random = new Unity.Mathematics.Random(hash ^ (uint)index);

            Hash128 result;
            do
            {
                result.Value = random.NextUInt4();
            } while (!guidEntities.TryAdd(result, entity));

            /*NativeArray<EntityDataIdentity> identities;
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
            } while (i < numidentityComponentChunks);*/

            return result;
        }

        public void Execute(int index)
        {
            Entity handle;
            int numCommands = commands.Length;
            GameItemCommand command = commands[index], temp;
            switch (command.commandType)
            {
                case GameItemCommandType.Create:
                    UnityEngine.Assertions.Assert.AreNotEqual(Handle.Empty, command.destinationHandle);

                    handle = GameItemStructChangeFactory.Convert(command.destinationHandle);

                    if (handleEntities.ContainsKey(handle))
                        return;

                    for (int i = index + 1; i < numCommands; ++i)
                    {
                        temp = commands[i];
                        if (temp.commandType == GameItemCommandType.Destroy && temp.sourceHandle.Equals(command.destinationHandle))
                            return;
                    }

                    createCommander.TryAdd(command.destinationHandle, entityArchetype);

                    /*identity.guid = CreateGUID(index, handle);// random.NextUInt4();

                    assigner.SetComponentData(handle, identity);

                    GameItemData instance;
                    instance.handle = command.destinationHandle;
                    assigner.SetComponentData(handle, instance);*/
                    Init((uint)index ^ RandomUtility.Hash(hash), 
                        identityType, 
                        command.destinationHandle, 
                        handle, 
                        ref guidEntities, 
                        ref assigner);
                    break;
                case GameItemCommandType.Destroy:
                    UnityEngine.Assertions.Assert.AreNotEqual(Handle.Empty, command.sourceHandle);

                    handle = GameItemStructChangeFactory.Convert(command.sourceHandle);

                    if (!handleEntities.TryGetValue(handle, out Entity entity))
                        return;

                    for (int i = index + 1; i < numCommands; ++i)
                    {
                        temp = commands[i];
                        if (temp.commandType == GameItemCommandType.Create && temp.destinationHandle.Equals(command.sourceHandle))
                            return;
                    }

                    destroyCommander.Enqueue(entity);

                    //entities.Remove(command.sourceHandle);
                    break;
            }
        }
    }

    private long __seed;
    private EntityQuery __structChangeManagerGroup;
    private EntityQuery __identityTypeGroup;

    private SharedHashMap<Hash128, Entity> __guidEntities;

    private NativeList<GameItemCommand> __commandSources;
    private NativeList<GameItemCommand> __commandDestinations;

    public static readonly int InnerloopBatchCount = 4;

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

    public static Hash128 CreateGUID(
        uint hash,
        in Entity entity,
        ref SharedHashMap<Hash128, Entity>.ParallelWriter guidEntities)
    {
        var random = new Unity.Mathematics.Random(hash);

        Hash128 result;
        do
        {
            result.Value = random.NextUInt4();
        } while (!guidEntities.TryAdd(result, entity));

        return result;
    }

    public static Hash128 CreateGUID(
        in Entity entity,
        ref Unity.Mathematics.Random random, 
        ref SharedHashMap<Hash128, Entity>.Writer guidEntities)
    {
        Hash128 result;
        do
        {
            result.Value = random.NextUInt4();
        } while (!guidEntities.TryAdd(result, entity));

        return result;
    }

    public static void Init(
        uint hash,
        int identityType,
        in Handle handle,
        in Entity entity,
        ref SharedHashMap<Hash128, Entity>.ParallelWriter guidEntities,
        ref EntityComponentAssigner.ParallelWriter assigner)
    {
        EntityDataIdentity identity;
        identity.type = identityType;
        identity.guid = CreateGUID(hash, entity, ref guidEntities);

        assigner.SetComponentData(entity, identity);

        GameItemData instance;
        instance.handle = handle;
        assigner.SetComponentData(entity, instance);
    }

    public static void Init(
        int identityType,
        in Handle handle,
        in Entity entity,
        ref Unity.Mathematics.Random random,
        ref SharedHashMap<Hash128, Entity>.Writer guidEntities,
        ref EntityComponentAssigner.Writer assigner)
    {
        EntityDataIdentity identity;
        identity.type = identityType;
        identity.guid = CreateGUID(entity, ref random, ref guidEntities);

        assigner.SetComponentData(entity, identity);

        GameItemData instance;
        instance.handle = handle;
        assigner.SetComponentData(entity, instance);
    }

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        //state.SetAlwaysUpdateSystem(true);

        __structChangeManagerGroup = GameItemStructChangeManager.GetEntityQuery(ref state);

        __identityTypeGroup = state.GetEntityQuery(ComponentType.ReadOnly<GameItemIdentityType>());

        /*__group = state.GetEntityQuery(ComponentType.ReadOnly<GameItemData>(), ComponentType.ReadOnly<GameItemType>());
        __group.SetChangedVersionFilter(typeof(GameItemType));*/

        __guidEntities = state.WorldUnmanaged.GetExistingSystemUnmanaged<EntityDataSystem>().guidEntities;

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

        ref var managerJobManager = ref manager.lookupJobManager;
        ref var guidEntitiesJobManager = ref __guidEntities.lookupJobManager;

        var jobHandle = JobHandle.CombineDependencies(managerJobManager.readOnlyJobHandle, guidEntitiesJobManager.readWriteJobHandle, state.Dependency);

        var typeCountAndBufferSize = CollectionHelper.CreateNativeArray<int>(2, state.WorldUpdateAllocator, NativeArrayOptions.UninitializedMemory);

        var structChangeManager = __structChangeManagerGroup.GetSingleton<GameItemStructChangeManager>();

        var createCommander = structChangeManager.createEntityCommander;

        Recapacity recapacity;
        recapacity.commands = __commandSources;
        recapacity.typeCountAndBufferSize = typeCountAndBufferSize;
        recapacity.guidEntities = __guidEntities.writer;
        recapacity.createCommander = createCommander.writer;
        jobHandle = recapacity.ScheduleByRef(jobHandle);

        var handleEntities = structChangeManager.handleEntities;
        var assigner = structChangeManager.assigner;
        var destroyCommander = structChangeManager.destroyEntityCommander.Create();

        Command command;
        command.hash = __seed ^ math.aslong(state.WorldUnmanaged.Time.ElapsedTime);
        command.identityType = __identityTypeGroup.GetSingleton<GameItemIdentityType>().value;
        command.entityArchetype = entityArchetype;
        command.handleEntities = handleEntities.reader;
        command.commands = __commandSources.AsDeferredJobArray();
        command.guidEntities = __guidEntities.parallelWriter;
        command.assigner = assigner.AsParallelWriter(typeCountAndBufferSize, ref jobHandle);
        command.createCommander = createCommander.parallelWriter;
        command.destroyCommander = destroyCommander.parallelWriter;

        ref var handleEntitiesJobManager = ref handleEntities.lookupJobManager;
        ref var commanderJobManager = ref createCommander.lookupJobManager;

        jobHandle = JobHandle.CombineDependencies(jobHandle, handleEntitiesJobManager.readOnlyJobHandle, commanderJobManager.readWriteJobHandle);

        jobHandle = manager.ScheduleParallelCommands(ref command, InnerloopBatchCount, JobHandle.CombineDependencies(assigner.jobHandle, jobHandle));

        assigner.jobHandle = jobHandle;

        commanderJobManager.readWriteJobHandle = jobHandle;

        guidEntitiesJobManager.readWriteJobHandle = jobHandle;

        handleEntitiesJobManager.AddReadOnlyDependency(jobHandle);

        destroyCommander.AddJobHandleForProducer<Command>(jobHandle);

        jobHandle = manager.ScheduleFlush(true, JobHandle.CombineDependencies(jobHandle, managerJobManager.readWriteJobHandle));

        managerJobManager.readWriteJobHandle = jobHandle;

        state.Dependency = jobHandle;
    }
}