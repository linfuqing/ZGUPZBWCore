using System;
using Unity.Jobs;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using ZG;

#region GameItemManager
[assembly: RegisterGenericJobType(typeof(EntityDataContainerSerialize<GameDataItemContainerSerializationSystem.Serializer>))]
[assembly: RegisterGenericJobType(typeof(EntityDataContainerDeserialize<GameDataItemContainerDeserializationSystem.Deserializer>))]

[assembly: EntityDataSerialize(typeof(GameItemManager), typeof(GameDataItemContainerSerializationSystem))]
[assembly: EntityDataDeserialize(typeof(GameItemManager), typeof(GameDataItemContainerDeserializationSystem), (int)GameDataConstans.Version)]
#endregion

#region GameItemRoot
[assembly: RegisterGenericJobType(typeof(EntityDataComponentSerialize<GameDataItemRootSerializationSystem.Serializer, GameDataItemRootSerializationSystem.SerializerFactory>))]
[assembly: RegisterGenericJobType(typeof(EntityDataComponentDeserialize<GameDataItemRootDeserializationSystem.Deserializer, GameDataItemRootDeserializationSystem.DeserializerFactory>))]
[assembly: EntityDataSerialize(typeof(GameItemRoot), typeof(GameDataItemRootSerializationSystem))]
[assembly: EntityDataDeserialize(typeof(GameItemRoot), typeof(GameDataItemRootDeserializationSystem), (int)GameDataConstans.Version)]
#endregion

#region GameItemRoot
[assembly: RegisterGenericJobType(typeof(EntityDataComponentSerialize<GameDataItemSerializationSystem.Serializer, GameDataItemSerializationSystem.SerializerFactory>))]
[assembly: RegisterGenericJobType(typeof(EntityDataComponentDeserialize<GameDataItemDeserializationSystem.Deserializer, GameDataItemDeserializationSystem.DeserializerFactory>))]
[assembly: EntityDataSerialize(typeof(GameItemData), typeof(GameDataItemSerializationSystem))]
[assembly: EntityDataDeserialize(typeof(GameItemData), typeof(GameDataItemDeserializationSystem), (int)GameDataConstans.Version)]
#endregion

[Serializable]
public struct GameDataItem
{
    public int entityIndex;

    public int parentChildIndex;
    public int parentEntityIndex;

    public int siblingEntityIndex;
}

public partial class GameItemContainerSystem : SystemBase
{
    private NativeArray<Hash128> __typeGuids;

    public NativeArray<Hash128>.ReadOnly typeGuids => __typeGuids.AsReadOnly();

    public void Create(Hash128[] typeGuids)
    {
        __typeGuids = new NativeArray<Hash128>(typeGuids, Allocator.Persistent);
    }

    protected override void OnCreate()
    {
        base.OnCreate();

        Enabled = false;
    }

    protected override void OnDestroy()
    {
        if (__typeGuids.IsCreated)
            __typeGuids.Dispose();

        base.OnDestroy();
    }

    protected override void OnUpdate()
    {
        throw new NotImplementedException();
    }
}

[BurstCompile, AutoCreateIn("Server"), 
    //AlwaysUpdateSystem, 
    UpdateInGroup(typeof(GameItemInitSystemGroup)), 
    UpdateBefore(typeof(GameItemComponentInitSystemGroup))/*, 
    UpdateAfter(typeof(GameItemEntitySystem)),
    UpdateAfter(typeof(GameItemRootEntitySystem))*/]
public partial struct GameDataItemSystem : ISystem
{
    /*private struct Result
    {
        public Entity entity;
        public Entity root;
    }*/

    [BurstCompile]
    private struct DidChange : IJobChunk
    {
        public uint lastSystemVersion;
        [NativeDisableParallelForRestriction]
        public NativeArray<int> result;
        [ReadOnly]
        public BufferTypeHandle<GameItemSibling> siblingType;
        [ReadOnly]
        public ComponentTypeHandle<GameItemRoot> rootType;
        [ReadOnly]
        public ComponentTypeHandle<EntityDataSerializable> serializableType;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            if(chunk.DidChange(ref serializableType, lastSystemVersion) ||
                chunk.Has(ref rootType) && chunk.DidChange(ref rootType, lastSystemVersion) ||
                chunk.Has(ref siblingType) && chunk.DidChange(ref siblingType, lastSystemVersion))
            result[0] = 1;
        }
    }

    private struct CountOf
    {
        public NativeCounter.Concurrent hierarchyCount;

        public GameItemManager.Hierarchy hierarchy;

        [ReadOnly]
        public NativeArray<GameItemRoot> roots;

        [ReadOnly]
        public BufferAccessor<GameItemSibling> siblings;

        public void Execute(int index)
        {
            if(index < roots.Length)
                hierarchyCount.Add(hierarchy.CountOf(roots[index].handle));

            if(index < this.siblings.Length)
            {
                var siblings = this.siblings[index];
                int numSiblings = siblings.Length;
                for (int i = 0; i < numSiblings; ++i)
                    hierarchyCount.Add(hierarchy.CountOf(siblings[i].handle));
            }
        }
    }

    [BurstCompile]
    private struct CountOfEx : IJobChunk
    {
        [ReadOnly]
        public NativeArray<int> result;

        public NativeCounter.Concurrent hierarchyCount;

        public GameItemManager.Hierarchy hierarchy;

        [ReadOnly]
        public ComponentTypeHandle<GameItemRoot> rootType;

        [ReadOnly]
        public BufferTypeHandle<GameItemSibling> siblingType;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            if (result[0] == 0)
                return;

            CountOf countOf;
            countOf.hierarchyCount = hierarchyCount;
            countOf.hierarchy = hierarchy;
            countOf.roots = chunk.GetNativeArray(ref rootType);
            countOf.siblings = chunk.GetBufferAccessor(ref siblingType);

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                countOf.Execute(i);
        }
    }

    [BurstCompile]
    private struct Clear : IJob
    {
        [ReadOnly]
        public NativeArray<int> result;

        public NativeCounter hierarchyCount;

        public NativeList<Entity> oldEntities;

        //[ReadOnly]
        //public ComponentLookup<EntityDataSerializable> serializables;

        public SharedHashMap<Entity, Entity>.Writer serializableEntities;

        //public EntityCommandQueue<EntityCommandStructChange>.ParallelWriter entityManager;

        public void Execute()
        {
            oldEntities.Clear();

            if (result[0] == 0)
                return;

            /*EntityCommandStructChange command;
            command.componentType = ComponentType.ReadOnly<EntityDataSerializable>();

            var enumerator = serializableEntities.GetEnumerator();
            KeyValue<Entity, Entity> keyValue;
            Entity key;
            while(enumerator.MoveNext())
            {
                keyValue = enumerator.Current;
                key = keyValue.Key;
                if (serializables.HasComponent(key) && !serializables.HasComponent(keyValue.Value))
                {
                    command.entity = key;
                    entityManager.Enqueue(command);
                }
            }*/

            using (var entities = serializableEntities.GetKeyArray(Allocator.Temp))
            {
                /*int count = entities.ConvertToUniqueArray();
                oldEntities.AddRange(entities.GetSubArray(0, count));*/

                oldEntities.AddRange(entities);
            }

            serializableEntities.capacity = math.max(serializableEntities.capacity, hierarchyCount.count);

            serializableEntities.Clear();

            hierarchyCount.count = 0;
        }
    }

    private struct MaskSerializable
    {
        public GameItemManager.Hierarchy hierarchy;

        [ReadOnly]
        public SharedHashMap<Entity, Entity>.Reader entities;

        [ReadOnly]
        public NativeArray<Entity> entityArray;

        [ReadOnly]
        public NativeArray<GameItemRoot> roots;

        [ReadOnly]
        public BufferAccessor<GameItemSibling> siblings;

        [ReadOnly]
        public ComponentLookup<EntityDataSerializable> serializables;

        public SharedHashMap<Entity, Entity>.ParallelWriter serializableEntities;

        public EntityCommandQueue<EntityCommandStructChange>.ParallelWriter entityManager;

        public void Execute(in GameItemHandle handle, in Entity root)
        {
            if (!hierarchy.GetChildren(handle, out var enumerator, out var item))
                return;

            while (enumerator.MoveNext())
                Execute(enumerator.Current.handle, root);

            Execute(item.siblingHandle, root);

            if (entities.TryGetValue(GameItemStructChangeFactory.Convert(handle), out Entity entity))
            {
                bool result = serializableEntities.TryAdd(entity, root);

                UnityEngine.Assertions.Assert.IsTrue(result);

                if (result && !serializables.HasComponent(entity))
                {
                    EntityCommandStructChange command;
                    command.componentType = ComponentType.ReadOnly<EntityDataSerializable>();
                    command.entity = entity;
                    entityManager.Enqueue(command);
                }
            }
        }

        public void Execute(int index)
        {
            Entity entity = entityArray[index];

            if(index < roots.Length)
                Execute(roots[index].handle, entity);

            if (index < this.siblings.Length)
            {
                var siblings = this.siblings[index];
                int numSiblings = siblings.Length;
                for(int i = 0; i < numSiblings; ++i)
                    Execute(siblings[i].handle, entity);
            }
        }
    }

    [BurstCompile]
    private struct MaskSerializableEx : IJobChunk
    {
        [ReadOnly]
        public NativeArray<int> result;

        public GameItemManager.Hierarchy hierarchy;

        [ReadOnly]
        public SharedHashMap<Entity, Entity>.Reader entities;

        [ReadOnly]
        public EntityTypeHandle entityType;

        [ReadOnly]
        public ComponentTypeHandle<GameItemRoot> rootType;

        [ReadOnly]
        public BufferTypeHandle<GameItemSibling> siblingType;

        [ReadOnly]
        public ComponentLookup<EntityDataSerializable> serializables;

        public SharedHashMap<Entity, Entity>.ParallelWriter serializableEntities;

        public EntityCommandQueue<EntityCommandStructChange>.ParallelWriter entityManager;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            if (result[0] == 0)
                return;

            MaskSerializable maskSerializable;
            maskSerializable.hierarchy = hierarchy;
            maskSerializable.entities = entities;
            maskSerializable.entityArray = chunk.GetNativeArray(entityType);
            maskSerializable.roots = chunk.GetNativeArray(ref rootType);
            maskSerializable.siblings = chunk.GetBufferAccessor(ref siblingType);
            maskSerializable.serializables = serializables;
            maskSerializable.serializableEntities = serializableEntities;
            maskSerializable.entityManager = entityManager;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                maskSerializable.Execute(i);
        }
    }

    [BurstCompile]
    private struct Filter : IJobParalledForDeferBurstSchedulable
    {
        [ReadOnly]
        public NativeArray<Entity> entityArray;

        [ReadOnly]
        public SharedHashMap<Entity, Entity>.Reader serializableEntities;

        [ReadOnly]
        public ComponentLookup<EntityDataSerializable> serializables;

        public EntityCommandQueue<EntityCommandStructChange>.ParallelWriter entityManager;

        public void Execute(int index)
        {
            Entity entity = entityArray[index];
            if (serializableEntities.ContainsKey(entity))
                return;

            if (!serializables.HasComponent(entity))
                return;

            EntityCommandStructChange command;
            command.componentType = ComponentType.ReadOnly<EntityDataSerializable>();
            command.entity = entity;
            entityManager.Enqueue(command);
        }
    }

    [BurstCompile]
    private struct Change : IJob, IEntityCommandProducerJob
    {
        [ReadOnly]
        public NativeArray<int> result;

        public GameItemManager.Hierarchy hierarchy;

        [ReadOnly]
        public NativeArray<GameItemCommand> commands;

        [ReadOnly]
        public SharedHashMap<Entity, Entity>.Reader entities;

        /*[ReadOnly]
        public SharedHashMap<GameItemHandle, Entity>.Reader rootEntities;*/

        [ReadOnly]
        public ComponentLookup<EntityDataSerializable> serializables;

        public SharedHashMap<Entity, Entity>.Writer serializableEntities;

        public EntityCommandQueue<EntityCommandStructChange>.Writer addComponentCommander;
        public EntityCommandQueue<EntityCommandStructChange>.Writer removeComponentCommander;

        /*public Entity GetRootEntity(in GameItemHandle handle)
        {
            if (rootEntities.TryGetValue(handle, out var rootEntity))
                return serializables.HasComponent(rootEntity) ? rootEntity : Entity.Null;

            return Entity.Null;
        }*/

        public Entity GetRootEntitySerialized(in GameItemHandle handle)
        {
            if (entities.TryGetValue(
                        GameItemStructChangeFactory.Convert(handle),
                        out Entity entity) &&
                        serializableEntities.TryGetValue(entity, out Entity rootEntity))
                return rootEntity;

            return Entity.Null;
        }

        public Entity GetRootEntity(in GameItemHandle handle)
        {
            return GetRootEntitySerialized(hierarchy.GetRoot(handle));
        }

        public void Execute(in GameItemHandle handle, in Entity rootEntity)
        {
            if (!hierarchy.GetChildren(handle, out var enumerator, out var item))
                return;

            while (enumerator.MoveNext())
                Execute(enumerator.Current.handle, rootEntity);

            Execute(item.siblingHandle, rootEntity);

            bool isRemove = rootEntity == Entity.Null;
            if (entities.TryGetValue(GameItemStructChangeFactory.Convert(handle), out Entity entity) &&
                isRemove == serializables.HasComponent(entity) &&
                (isRemove ? serializableEntities.Remove(entity) : serializableEntities.TryAdd(entity, rootEntity)))
            {
                /*if (handle.index == 18)
                    UnityEngine.Debug.Log("ddd");*/

                EntityCommandStructChange command;
                command.componentType = ComponentType.ReadOnly<EntityDataSerializable>();
                command.entity = entity;

                if (isRemove)
                    removeComponentCommander.Enqueue(command);
                else
                    addComponentCommander.Enqueue(command);
            }
        }

        public void Execute(int index)
        {
            Entity rootEntity;
            var command = commands[index];
            switch (command.commandType)
            {
                case GameItemCommandType.Create:
                    rootEntity = GetRootEntity(command.destinationHandle);
                    if (rootEntity != Entity.Null)
                        Execute(command.destinationHandle, rootEntity);
                    break;
                case GameItemCommandType.Move:
                    rootEntity = GetRootEntitySerialized(command.destinationParentHandle);
                    if (rootEntity == Entity.Null)
                        rootEntity = GetRootEntity(command.destinationHandle);

                    if (rootEntity != Entity.Null)
                        Execute(command.destinationHandle, rootEntity);
                    break;
                case GameItemCommandType.Connect:
                    rootEntity = GetRootEntity(command.destinationHandle);
                    if (rootEntity == Entity.Null)
                        rootEntity = GetRootEntity(command.destinationSiblingHandle);

                    if(rootEntity != Entity.Null)
                        Execute(command.destinationSiblingHandle, rootEntity);

                    rootEntity = GetRootEntity(command.sourceSiblingHandle);
                    if (rootEntity != Entity.Null)
                        Execute(command.sourceSiblingHandle, rootEntity);
                    break;
                default:
                    return;
            }
        }

        public void Execute()
        {
            if (result[0] == 1)
                return;

            int numCommands = commands.Length;
            for (int i = 0; i < numCommands; ++i)
                Execute(i);
        }
    }

    [BurstCompile]
    private struct DisposeAll : IJob
    {
        [ReadOnly, DeallocateOnJobCompletion]
        public NativeArray<int> result;

        public void Execute()
        {

        }
    }

    public static readonly int InnerloopBatchCount = 1;

    public SharedHashMap<Entity, Entity> serializableEntities
    {
        get;

        private set;
    }

    private int __entityCount;
    private EntityQuery __group;
    private EntityQuery __structChangeManagerGroup;
    private GameItemManagerShared __itemManager;
    private EntityCommandPool<EntityCommandStructChange> __addComponentCommander;
    private EntityCommandPool<EntityCommandStructChange> __removeComponentCommander;

    private NativeCounterLite __hierarchyCount;
    private NativeListLite<Entity> __oldEntities;

    public void OnCreate(ref SystemState state)
    {
        BurstUtility.InitializeJob<Clear>();
        BurstUtility.InitializeJobParalledForDefer<Filter>();
        BurstUtility.InitializeJob<Change>();
        BurstUtility.InitializeJob<DisposeAll>();

        state.SetAlwaysUpdateSystem(true);

        __structChangeManagerGroup = GameItemStructChangeManager.GetEntityQuery(ref state);

        __group = state.GetEntityQuery(
            new EntityQueryDesc()
            {
                All = new ComponentType[]
                {
                    ComponentType.ReadOnly<EntityDataSerializable>(),
                },
                Any = new ComponentType[]
                {
                    ComponentType.ReadOnly<GameItemRoot>(),
                    ComponentType.ReadOnly<GameItemSibling>(),
                }, 
                Options = EntityQueryOptions.IncludeDisabledEntities
            });

        World world = state.World;
        __itemManager = world.GetOrCreateSystemUnmanaged<GameItemSystem>().manager;

        var manager = world.GetOrCreateSystemUnmanaged<GameItemComponentStructChangeSystem>().manager;

        __removeComponentCommander = manager.removeComponentPool;
        __addComponentCommander = manager.addComponentPool;

        __hierarchyCount = new NativeCounterLite(Allocator.Persistent);
        __oldEntities = new NativeListLite<Entity>(Allocator.Persistent);

        serializableEntities = new SharedHashMap<Entity, Entity>(Allocator.Persistent);
    }

    public void OnDestroy(ref SystemState state)
    {
        __hierarchyCount.Dispose();
        __oldEntities.Dispose();

        serializableEntities.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!__itemManager.isCreated)
            return;

        var rootType = state.GetComponentTypeHandle<GameItemRoot>(true);
        var siblingType = state.GetBufferTypeHandle<GameItemSibling>(true);

        JobHandle jobHandle;
        var result = new NativeArray<int>(1, Allocator.TempJob, NativeArrayOptions.ClearMemory);
        int entityCount = __group.CalculateEntityCount();
        if (entityCount == __entityCount)
        {
            DidChange didChange;
            didChange.lastSystemVersion = state.LastSystemVersion;
            didChange.result = result;
            didChange.rootType = rootType;
            didChange.siblingType = siblingType;
            didChange.serializableType = state.GetComponentTypeHandle<EntityDataSerializable>(true);
            jobHandle = didChange.ScheduleParallel(__group, state.Dependency);
        }
        else
        {
            __entityCount = entityCount;

            result[0] = 1;

            jobHandle = state.Dependency;
        }

        ref var itemJobManager = ref __itemManager.lookupJobManager;

        var hierarchy = __itemManager.hierarchy;

        NativeCounter hierarchyCount = __hierarchyCount;

        CountOfEx countOf;
        countOf.result = result;
        countOf.hierarchyCount = hierarchyCount;
        countOf.hierarchy = hierarchy;
        countOf.rootType = rootType;
        countOf.siblingType = siblingType;
        jobHandle = countOf.ScheduleParallel(__group, JobHandle.CombineDependencies(itemJobManager.readOnlyJobHandle, jobHandle));

        //removeComponentCommander.AddJobHandleForProducer(jobHandle);

        var serializableEntities = this.serializableEntities;
        ref var serializableEntityJobManager = ref serializableEntities.lookupJobManager;
        serializableEntityJobManager.CompleteReadWriteDependency();

        NativeList<Entity> oldEntities = __oldEntities;

        Clear clear;
        clear.result = result;
        clear.hierarchyCount = hierarchyCount;
        clear.oldEntities = oldEntities;
        clear.serializableEntities = serializableEntities.writer;
        jobHandle = clear.Schedule(jobHandle);

        var serializables = state.GetComponentLookup<EntityDataSerializable>(true);

        var addComponentCommander = __addComponentCommander.Create();
        var addComponentParallelWriter = addComponentCommander.parallelWriter;
        var addComponentWriter = addComponentCommander.writer;

        var handleEntities = __structChangeManagerGroup.GetSingleton<GameItemStructChangeManager>().handleEntities;

        var handleEntitiesReader = handleEntities.reader;

        MaskSerializableEx maskSerializable;
        maskSerializable.result = result;
        maskSerializable.hierarchy = hierarchy;
        maskSerializable.entities = handleEntitiesReader;
        maskSerializable.entityType = state.GetEntityTypeHandle();
        maskSerializable.rootType = rootType;
        maskSerializable.siblingType = siblingType;
        maskSerializable.serializables = serializables;
        maskSerializable.serializableEntities = serializableEntities.parallelWriter;
        maskSerializable.entityManager = addComponentParallelWriter;

        ref var entityJobManager = ref handleEntities.lookupJobManager;

        jobHandle = maskSerializable.ScheduleParallel(__group, JobHandle.CombineDependencies(jobHandle, entityJobManager.readOnlyJobHandle));

        var removeComponentCommander = __removeComponentCommander.Create();
        var removeComponentParallelWriter = removeComponentCommander.parallelWriter;
        var removeComponentWriter = removeComponentCommander.writer;

        Filter filter;
        filter.entityArray = oldEntities.AsDeferredJobArrayEx();
        filter.serializableEntities = serializableEntities.reader;
        filter.serializables = serializables;
        filter.entityManager = removeComponentParallelWriter;
        jobHandle = filter.ScheduleParallel(oldEntities, InnerloopBatchCount, jobHandle);

        Change change;
        change.result = result;
        change.hierarchy = hierarchy;
        change.entities = handleEntitiesReader;
        //change.rootEntities = __rootEntities.reader;
        change.serializables = serializables;
        change.serializableEntities = serializableEntities.writer;
        change.addComponentCommander = addComponentWriter;
        change.removeComponentCommander = removeComponentWriter;

        change.commands = __itemManager.oldCommands;

        /*ref var rootEntityJobManager = ref __rootEntities.lookupJobManager;

        jobHandle = JobHandle.CombineDependencies(rootEntityJobManager.readOnlyJobHandle, jobHandle);*/
        jobHandle = change.Schedule(jobHandle);

        //这些指令可能Entity未被创建，所以不使用
        //change.commands = __itemManager.commands;
        //jobHandle = change.Schedule(jobHandle);

        itemJobManager.AddReadOnlyDependency(jobHandle);
        entityJobManager.AddReadOnlyDependency(jobHandle);
        serializableEntityJobManager.AddReadOnlyDependency(jobHandle);

        addComponentCommander.AddJobHandleForProducer<Change>(jobHandle);
        removeComponentCommander.AddJobHandleForProducer<Change>(jobHandle);

        DisposeAll disposeAll;
        disposeAll.result = result;

        state.Dependency = disposeAll.Schedule(jobHandle);
    }
}

/*[BurstCompile, AutoCreateIn("Server"), UpdateInGroup(typeof(GameDataDeserializationSystemGroup), OrderFirst = true), UpdateAfter(typeof(EntityDataDeserializationCommandSystem))]
public partial struct GameDataItemDeserializationStructChangeSystem : ISystem
{
    private EntityQuery __group;
    private GameItemStructChangeSystem.Factory __factory;

    public SharedHashMap<GameItemHandle, EntityArchetype> createEntityCommander => __factory.createEntityCommander;

    public EntityComponentAssigner assigner
    {
        get;

        private set;
    }

    public void OnCreate(ref SystemState state)
    {
        state.SetAlwaysUpdateSystem(true);

        __group = state.GetEntityQuery(ComponentType.ReadOnly<GameItemData>());

        ref var itemStructChangeSystem = ref state.World.GetOrCreateSystemUnmanaged<GameItemStructChangeSystem>();
        assigner = itemStructChangeSystem.assigner;

        __factory = itemStructChangeSystem.factory;
    }

    public void OnDestroy(ref SystemState state)
    {

    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if(__factory.Playback(ref state) > 0)
            __factory.Assign(ref state, __group, assigner);
    }
}*/

/*[AutoCreateIn("Server"), UpdateInGroup(typeof(PresentationSystemGroup)), UpdateBefore(typeof(GameItemSystem))]
public partial class GameDataItemPresentSystem : SystemBase
{
    public int innerloopBatchCount = 32;

    private GameItemSystem __itemSystem;
    private GameItemEntitySystem __entitySystem;
    private GameDataItemInitSystem __initSystem;

    private EntityCommandPool<Entity> __addComponentCommander;
    private EntityCommandPool<Entity> __removeComponentCommander;

    protected override void OnCreate()
    {
        base.OnCreate();

        World world = World;
        __itemSystem = world.GetOrCreateSystem<GameItemSystem>();
        __entitySystem = world.GetOrCreateSystem<GameItemEntitySystem>();
        __initSystem = world.GetOrCreateSystem<GameDataItemInitSystem>();

        var endFrameBarrier = world.GetOrCreateSystem<EndFrameEntityCommandSystem>();

        __addComponentCommander = endFrameBarrier.CreateAddComponentCommander<EntityDataSerializable>();
        __removeComponentCommander = endFrameBarrier.CreateRemoveComponentCommander<EntityDataSerializable>();
    }

    protected override void OnUpdate()
    {
        if (!__itemSystem.isCreated)
            return;

        var addComponentCommander = __addComponentCommander.Create();
        var removeComponentCommander = __removeComponentCommander.Create();

        JobHandle jobHandle = JobHandle.CombineDependencies(__itemSystem.commandJobHandle, __entitySystem.jobHandle);
        jobHandle = JobHandle.CombineDependencies(jobHandle, __initSystem.jobHandle, Dependency);

        var manager = __itemSystem.manager;
        Change change;
        change.commands = manager.commands;
        change.entities = __entitySystem.entities;
        change.serializableEntities = __initSystem.serializableEntities;
        change.serializables = GetComponentLookup<EntityDataSerializable>(true);
        change.addComponentCommander = addComponentCommander.parallelWriter;
        change.removeComponentCommander = removeComponentCommander.parallelWriter;
        jobHandle = manager.ScheduleCommands(ref change, innerloopBatchCount, jobHandle);

        addComponentCommander.AddJobHandleForProducer(jobHandle);
        removeComponentCommander.AddJobHandleForProducer(jobHandle);

        __itemSystem.commandJobHandle = jobHandle;
        __entitySystem.jobHandle = jobHandle;
        __initSystem.jobHandle = jobHandle;

        Dependency = jobHandle;
    }
}*/

public struct GameDataItemSerializer
{
    public GameItemManager.Hierarchy hierarchy;

    [ReadOnly]
    public NativeParallelHashMap<Hash128, int> entityIndices;

    [ReadOnly]
    public SharedHashMap<Entity, Entity>.Reader entities;

    [ReadOnly]
    public ComponentLookup<EntityDataIdentity> identities;

    public int GetEntityIndex(in GameItemHandle handle)
    {
        if(entities.TryGetValue(GameItemStructChangeFactory.Convert(handle), out Entity entity))
        {
            if (entityIndices.TryGetValue(identities[entity].guid, out int entityIndex))
                return entityIndex;

            UnityEngine.Debug.LogError($"Get entity index of {identities[entity].guid} has been fail.");
        }

        return -1;
    }

    public void Serialize(in GameItemHandle handle, ref NativeArray<GameDataItem> items, ref int itemIndex)
    {
        if (!hierarchy.GetChildren(handle, out var enumerator, out var source))
            return;

        GameDataItem destination;
        destination.entityIndex = GetEntityIndex(handle);

        UnityEngine.Assertions.Assert.AreNotEqual(-1, destination.entityIndex);

        destination.parentChildIndex = source.parentChildIndex;
        destination.parentEntityIndex = GetEntityIndex(source.parentHandle);
        destination.siblingEntityIndex = GetEntityIndex(source.siblingHandle);

        items[itemIndex++] = destination;

        while (enumerator.MoveNext())
            Serialize(enumerator.Current.handle, ref items, ref itemIndex);

        Serialize(source.siblingHandle, ref items, ref itemIndex);
    }

    public void Serialize(in GameItemHandle handle, ref EntityDataWriter writer)
    {
        int count = hierarchy.CountOf(handle), itemIndex = 0;
        var items = new NativeArray<GameDataItem>(count, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
        Serialize(handle, ref items, ref itemIndex);

        writer.Write(itemIndex);
        writer.Write(items);
        items.Dispose();
    }
}

public struct GameDataItemDeserializer
{
    public GameItemManager manager;

    [ReadOnly]
    public NativeParallelHashMap<int, Entity> entities;

    [ReadOnly]
    public ComponentLookup<GameItemData> instances;

    public GameItemHandle GetHandle(int entityIndex)
    {
        if (entities.TryGetValue(entityIndex, out var entity))
        {
            if (!instances.HasComponent(entity))
            {
                UnityEngine.Debug.LogError($"{entity}");

                return GameItemHandle.empty;
            }

            return instances[entity].handle;
        }

        return GameItemHandle.empty;
    }

    public GameItemHandle Deserialize(in GameDataItem item)
    {
        GameItemHandle handle = GetHandle(item.entityIndex);
        if (item.parentEntityIndex != -1)
            manager.Move(handle, GetHandle(item.parentEntityIndex), item.parentChildIndex);

        if (item.siblingEntityIndex != -1)
            manager.AttachSibling(handle, GetHandle(item.siblingEntityIndex));

        return handle;
    }
}

[DisableAutoCreation, AlwaysUpdateSystem]
public partial class GameDataItemContainerSerializationSystem : EntityDataSerializationContainerSystem<GameDataItemContainerSerializationSystem.Serializer>, IReadOnlyLookupJobManager
{
    private struct Init
    {
        public GameItemManager.ReadOnlyInfos infos;

        [ReadOnly]
        public NativeArray<Hash128>.ReadOnly inputs;

        [ReadOnly]
        public NativeArray<GameItemData> instances;

        public NativeParallelHashMap<int, int> typeIndices;

        public NativeList<Hash128> outputs;

        public void Execute(int index)
        {
            if (!infos.TryGetValue(instances[index].handle, out var item))
                return;

            if (typeIndices.TryAdd(item.type, outputs.Length))
                outputs.Add(inputs[item.type]);
        }
    }

    [BurstCompile]
    private struct InitEx : IJobChunk
    {
        public GameItemManager.ReadOnlyInfos infos;

        [ReadOnly]
        public NativeArray<Hash128>.ReadOnly inputs;

        [ReadOnly]
        public ComponentTypeHandle<GameItemData> instanceType;

        public NativeParallelHashMap<int, int> typeIndices;

        public NativeList<Hash128> outputs;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Init init;
            init.infos = infos;
            init.inputs = inputs;
            init.instances = chunk.GetNativeArray(ref instanceType);
            init.typeIndices = typeIndices;
            init.outputs = outputs;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                init.Execute(i);
        }
    }

    public struct Serializer : IEntityDataContainerSerializer
    {
        [ReadOnly]
        public NativeArray<Hash128> typeGuids;

        public void Serialize(ref NativeBuffer.Writer writer)
        {
            writer.Write(typeGuids);
        }
    }

    private LookupJobManager __lookupJobManager;

    private EntityQuery __group;
    private GameItemManagerShared __itemManager;
    private GameItemContainerSystem __containerSystem;

    private NativeList<Hash128> __typeGuids;

    public NativeParallelHashMap<int, int> typeIndices
    {
        get;

        private set;
    }

    #region LookupJob
    public JobHandle readOnlyJobHandle
    {
        get => __lookupJobManager.readOnlyJobHandle;
    }

    public void CompleteReadOnlyDependency() => __lookupJobManager.CompleteReadOnlyDependency();

    public void AddReadOnlyDependency(in JobHandle inputDeps) => __lookupJobManager.AddReadOnlyDependency(inputDeps);
    #endregion

    protected override void OnCreate()
    {
        base.OnCreate();

        __group = GetEntityQuery(
            new EntityQueryDesc()
            {
                All = new ComponentType[]
                {
                        ComponentType.ReadOnly<GameItemData>(),
                        ComponentType.ReadOnly<EntityDataIdentity>(),
                        ComponentType.ReadOnly<EntityDataSerializable>()
                },
                Options = EntityQueryOptions.IncludeDisabledEntities
            });

        World world = World;
        __itemManager = world.GetOrCreateSystemUnmanaged<GameItemSystem>().manager;
        __containerSystem = world.GetOrCreateSystemManaged<GameItemContainerSystem>();

        __typeGuids = new NativeList<Hash128>(Allocator.Persistent);

        typeIndices = new NativeParallelHashMap<int, int>(1, Allocator.Persistent);
    }

    protected override void OnDestroy()
    {
        __typeGuids.Dispose();

        typeIndices.Dispose();

        base.OnDestroy();
    }

    protected override void OnUpdate()
    {
        if (!__itemManager.isCreated)
            return;

        __lookupJobManager.CompleteReadWriteDependency();

        var inputDeps = Dependency;
        var jobHandle = typeIndices.Clear(__group.CalculateEntityCount(), inputDeps);
        jobHandle = JobHandle.CombineDependencies(jobHandle, __typeGuids.Clear(inputDeps));

        InitEx init;
        init.infos = __itemManager.value.readOnlyInfos;
        init.inputs = __containerSystem.typeGuids;
        init.instanceType = GetComponentTypeHandle<GameItemData>();
        init.typeIndices = typeIndices;
        init.outputs = __typeGuids;

        ref var lookupJobManager = ref __itemManager.lookupJobManager;

        jobHandle = init.Schedule(__group, JobHandle.CombineDependencies(lookupJobManager.readOnlyJobHandle, jobHandle));

        __lookupJobManager.readWriteJobHandle = jobHandle;

        lookupJobManager.AddReadOnlyDependency(jobHandle);

        Dependency = jobHandle;

        base.OnUpdate();
    }

    protected override Serializer _Get()
    {
        Serializer serializer;
        serializer.typeGuids = __typeGuids.AsDeferredJobArrayEx();
        return serializer;
    }
}

[DisableAutoCreation]
public partial class GameDataItemRootSerializationSystem : EntityDataSerializationComponentSystem<GameItemRoot, GameDataItemRootSerializationSystem.Serializer, GameDataItemRootSerializationSystem.SerializerFactory>
{
    public struct Serializer : IEntityDataSerializer
    {
        public GameDataItemSerializer instance;

        [ReadOnly]
        public NativeArray<GameItemRoot> roots;

        public void Serialize(int index, in NativeParallelHashMap<Hash128, int> entityIndices, ref EntityDataWriter writer)
        {
            instance.Serialize(roots[index].handle, ref writer);
        }
    }

    public struct SerializerFactory : IEntityDataFactory<Serializer>
    {
        public GameDataItemSerializer instance;

        [ReadOnly]
        public ComponentTypeHandle<GameItemRoot> rootType;

        public Serializer Create(in ArchetypeChunk chunk, int firstEntityIndex)
        {
            Serializer serializer;
            serializer.instance = instance;
            serializer.roots = chunk.GetNativeArray(ref rootType);

            return serializer;
        }
    }

    private EntityQuery __structChangeManagerGroup;
    private SharedHashMap<Entity, Entity> __handleEntities;
    private GameItemManagerShared __itemManager;

    protected override void OnCreate()
    {
        base.OnCreate();

        __structChangeManagerGroup = GameItemStructChangeManager.GetEntityQuery(ref this.GetState());

        World world = World;
        __itemManager = world.GetOrCreateSystemUnmanaged<GameItemSystem>().manager;
    }

    protected override void OnUpdate()
    {
        __handleEntities = __structChangeManagerGroup.GetSingleton<GameItemStructChangeManager>().handleEntities;

        base.OnUpdate();

        var jobHandle = Dependency;
        __itemManager.lookupJobManager.AddReadOnlyDependency(jobHandle);
        __handleEntities.lookupJobManager.AddReadOnlyDependency(jobHandle);
        systemGroup.initializationSystem.AddReadOnlyDependency(jobHandle);
    }

    protected override SerializerFactory _Get(ref JobHandle jobHandle)
    {
        jobHandle = JobHandle.CombineDependencies(jobHandle, __itemManager.lookupJobManager.readOnlyJobHandle, __handleEntities.lookupJobManager.readOnlyJobHandle);

        var initializationSystem = systemGroup.initializationSystem;
        jobHandle = JobHandle.CombineDependencies(jobHandle, initializationSystem.readOnlyJobHandle);

        SerializerFactory serializerFactory;
        serializerFactory.instance.hierarchy = __itemManager.value.hierarchy;
        serializerFactory.instance.entityIndices = initializationSystem.entityIndices;
        serializerFactory.instance.entities = __handleEntities.reader;
        serializerFactory.instance.identities = GetComponentLookup<EntityDataIdentity>(true);
        serializerFactory.rootType = GetComponentTypeHandle<GameItemRoot>(true);

        return serializerFactory;
    }
}

[DisableAutoCreation]
public partial class GameDataItemSiblingSerializationSystem : EntityDataSerializationComponentSystem<GameItemSibling, GameDataItemSiblingSerializationSystem.Serializer, GameDataItemSiblingSerializationSystem.SerializerFactory>
{
    public struct Serializer : IEntityDataSerializer
    {
        public GameDataItemSerializer instance;

        [ReadOnly]
        public BufferAccessor<GameItemSibling> siblings;

        public void Serialize(int index, in NativeParallelHashMap<Hash128, int> entityIndices, ref EntityDataWriter writer)
        {
            var siblings = this.siblings[index];

            int numSiblings = siblings.Length;
            writer.Write(numSiblings);
            for (int i = 0; i < numSiblings; ++i)
                instance.Serialize(siblings[i].handle, ref writer);
        }
    }

    public struct SerializerFactory : IEntityDataFactory<Serializer>
    {
        public GameDataItemSerializer instance;

        [ReadOnly]
        public BufferTypeHandle<GameItemSibling> siblingType;

        public Serializer Create(in ArchetypeChunk chunk, int firstEntityIndex)
        {
            Serializer serializer;
            serializer.instance = instance;
            serializer.siblings = chunk.GetBufferAccessor(ref siblingType);

            return serializer;
        }
    }

    private EntityQuery __structChangeManagerGroup;
    private SharedHashMap<Entity, Entity> __handleEntities;
    private GameItemManagerShared __itemManager;

    protected override void OnCreate()
    {
        base.OnCreate();

        __structChangeManagerGroup = GameItemStructChangeManager.GetEntityQuery(ref this.GetState());

        World world = World;
        __itemManager = world.GetOrCreateSystemUnmanaged<GameItemSystem>().manager;
    }

    protected override void OnUpdate()
    {
        __handleEntities = __structChangeManagerGroup.GetSingleton<GameItemStructChangeManager>().handleEntities;

        base.OnUpdate();

        var jobHandle = Dependency;

        __itemManager.lookupJobManager.AddReadOnlyDependency(jobHandle);
        __handleEntities.lookupJobManager.AddReadOnlyDependency(jobHandle);
        systemGroup.initializationSystem.AddReadOnlyDependency(jobHandle);
    }

    protected override SerializerFactory _Get(ref JobHandle jobHandle)
    {
        jobHandle = JobHandle.CombineDependencies(jobHandle, __itemManager.lookupJobManager.readOnlyJobHandle, __handleEntities.lookupJobManager.readOnlyJobHandle);

        var initializationSystem = systemGroup.initializationSystem;
        jobHandle = JobHandle.CombineDependencies(jobHandle, initializationSystem.readOnlyJobHandle);

        SerializerFactory serializerFactory;
        serializerFactory.instance.hierarchy = __itemManager.value.hierarchy;
        serializerFactory.instance.entityIndices = initializationSystem.entityIndices;
        serializerFactory.instance.entities = __handleEntities.reader;
        serializerFactory.instance.identities = GetComponentLookup<EntityDataIdentity>(true);
        serializerFactory.siblingType = GetBufferTypeHandle<GameItemSibling>(true);

        return serializerFactory;
    }
}

[DisableAutoCreation, UpdateAfter(typeof(GameDataItemContainerSerializationSystem))]
public partial class GameDataItemSerializationSystem : EntityDataSerializationComponentSystem<GameItemData, GameDataItemSerializationSystem.Serializer, GameDataItemSerializationSystem.SerializerFactory>
{
    public struct Serializer : IEntityDataSerializer
    {
        public GameItemManager.ReadOnlyInfos infos;

        [ReadOnly]
        public NativeParallelHashMap<int, int> typeIndices;

        [ReadOnly]
        public NativeArray<GameItemData> instances;

        public void Serialize(int index, in NativeParallelHashMap<Hash128, int> entityIndices, ref EntityDataWriter writer)
        {
            var handle = instances[index].handle;
            if (!infos.TryGetValue(handle, out var item))
            {
                UnityEngine.Debug.LogError($"Item Handle {handle} Serialize Fail.");

                writer.Write(-1);

                return;
            }

            writer.Write(this.typeIndices[item.type]);
            writer.Write(item.count);
        }
    }

    public struct SerializerFactory : IEntityDataFactory<Serializer>
    {
        public GameItemManager.ReadOnlyInfos infos;

        [ReadOnly]
        public NativeParallelHashMap<int, int> typeIndices;

        [ReadOnly]
        public ComponentTypeHandle<GameItemData> instanceType;

        public Serializer Create(in ArchetypeChunk chunk, int firstEntityIndex)
        {
            Serializer serializer;
            serializer.infos = infos;
            serializer.typeIndices = typeIndices;
            serializer.instances = chunk.GetNativeArray(ref instanceType);

            return serializer;
        }
    }

    private GameItemManagerShared __itemManager;
    private GameDataItemContainerSerializationSystem __containerSystem;

    protected override void OnCreate()
    {
        base.OnCreate();

        World world = World;
        __itemManager = world.GetOrCreateSystemUnmanaged<GameItemSystem>().manager;
        __containerSystem = world.GetOrCreateSystemManaged<GameDataItemContainerSerializationSystem>();
    }

    protected override void OnUpdate()
    {
        base.OnUpdate();

        var jobHandle = Dependency;

        __itemManager.lookupJobManager.AddReadOnlyDependency(jobHandle);
        __containerSystem.AddReadOnlyDependency(jobHandle);
    }

    protected override SerializerFactory _Get(ref JobHandle jobHandle)
    {
        jobHandle = JobHandle.CombineDependencies(jobHandle, __itemManager.lookupJobManager.readOnlyJobHandle, __containerSystem.readOnlyJobHandle);

        SerializerFactory serializerFactory;
        serializerFactory.infos = __itemManager.value.readOnlyInfos;
        serializerFactory.typeIndices = __containerSystem.typeIndices;
        serializerFactory.instanceType = GetComponentTypeHandle<GameItemData>(true);
        return serializerFactory;
    }
}

[DisableAutoCreation]
public partial class GameDataItemContainerDeserializationSystem : EntityDataDeserializationContainerSystem<GameDataItemContainerDeserializationSystem.Deserializer>, IReadOnlyLookupJobManager
{
    public struct Deserializer : IEntityDataContainerDeserializer
    {
        [ReadOnly]
        public NativeArray<Hash128>.ReadOnly typeGuids;
        public NativeList<int> types;

        public void Deserialize(in UnsafeBlock block)
        {
            var typeGuids = block.AsArray<Hash128>();

            int length = typeGuids.Length;
            types.ResizeUninitialized(length);
            for (int i = 0; i < length; ++i)
            {
                types[i] = this.typeGuids.IndexOf(typeGuids[i]);

#if DEBUG
                /*if (typeGuids[i].ToString() == "4b959ef3998812f82e760687c088b17e")
                    UnityEngine.Debug.Log($"{types[i]}");*/

                if (types[i] == -1)
                    UnityEngine.Debug.LogError($"Item {typeGuids[i]} Deserialize Fail.");
#endif
            }
        }
    }

    private LookupJobManager __lookupJobManager;

    private GameItemContainerSystem __containerSystem;

    private NativeList<int> __types;

    public NativeArray<int> types => __types.AsDeferredJobArrayEx();

#region LookupJob
    public JobHandle readOnlyJobHandle
    {
        get => __lookupJobManager.readOnlyJobHandle;
    }

    public void CompleteReadOnlyDependency() => __lookupJobManager.CompleteReadOnlyDependency();

    public void AddReadOnlyDependency(in JobHandle inputDeps) => __lookupJobManager.AddReadOnlyDependency(inputDeps);
#endregion

    protected override void OnCreate()
    {
        base.OnCreate();

        __containerSystem = World.GetOrCreateSystemManaged<GameItemContainerSystem>();

        __types = new NativeList<int>(Allocator.Persistent);
    }

    protected override void OnDestroy()
    {
        __types.Dispose();

        base.OnDestroy();
    }

    protected override void OnUpdate()
    {
        __lookupJobManager.CompleteReadWriteDependency();

        base.OnUpdate();

        var jobHandle = Dependency;

        __lookupJobManager.readWriteJobHandle = jobHandle;
    }

    protected override Deserializer _Create(ref JobHandle jobHandle)
    {
        Deserializer deserializer;
        deserializer.typeGuids = __containerSystem.typeGuids;
        deserializer.types = __types;
        return deserializer;
    }
}

[DisableAutoCreation, UpdateAfter(typeof(GameDataItemContainerDeserializationSystem))]
public partial class GameDataItemDeserializationSystem : EntityDataDeserializationComponentSystem<
    GameItemData, 
    GameDataItemDeserializationSystem.Deserializer, 
    GameDataItemDeserializationSystem.DeserializerFactory>, IEntityCommandProducerJob
{
    public struct Deserializer : IEntityDataDeserializer
    {
        public GameItemManager manager;

        [ReadOnly]
        public NativeArray<int> types;

        [ReadOnly]
        public NativeArray<Entity> entityArray;

        public NativeArray<GameItemData> instances;

        public SharedHashMap<Entity, Entity>.Writer entityHandles;

        public SharedHashMap<Entity, Entity>.Writer handleEntities;

        public EntityCommandQueue<Entity>.Writer entityManager;

        public void Deserialize(int index, ref EntityDataReader reader)
        {
            int typeIndex = reader.Read<int>();
            if (typeIndex == -1)
                return;

            GameItemData instance;
            int count = reader.Read<int>();
            count = math.max(count, 1);

            int type = types[typeIndex];
            if (type == -1)
            {
                UnityEngine.Debug.LogError($"Error Item Type of {entityArray[index]}");

                return;
            }

            instance.handle = manager.Add(type, ref count);
            /*if(instance.handle.index == 461)
            {
                bool result = manager.TryGetValue(instance.handle, out var temp);

                UnityEngine.Debug.LogError(result);
            }*/

            Entity handle = GameItemStructChangeFactory.Convert(instance.handle), entity = entityArray[index];
            if(handleEntities.TryGetValue(handle, out Entity temp))
            {
                entityHandles.Remove(temp);

                entityManager.Enqueue(temp);
            }

            entityHandles[entity] = handle;
            handleEntities[handle] = entity;

            instances[index] = instance;
        }
    }

    public struct DeserializerFactory : IEntityDataFactory<Deserializer>
    {
        public GameItemManager manager;

        [ReadOnly]
        public NativeArray<int> types;

        [ReadOnly]
        public EntityTypeHandle entityType;

        public ComponentTypeHandle<GameItemData> instanceType;

        public SharedHashMap<Entity, Entity>.Writer entityHandles;

        public SharedHashMap<Entity, Entity>.Writer handleEntities;

        public EntityCommandQueue<Entity>.Writer entityManager;

        public Deserializer Create(in ArchetypeChunk chunk, int unfilteredChunkIndex)
        {
            Deserializer deserializer;
            deserializer.manager = manager;
            deserializer.types = types;
            deserializer.entityArray = chunk.GetNativeArray(entityType);
            deserializer.instances = chunk.GetNativeArray(ref instanceType);
            deserializer.entityHandles = entityHandles;
            deserializer.handleEntities = handleEntities;
            deserializer.entityManager = entityManager;

            return deserializer;
        }
    }

    private EntityQuery __structChangeManagerGroup;
    private GameDataItemContainerDeserializationSystem __containerSystem;
    private GameItemManagerShared __itemManager;

    private EntityCommandPool<Entity> __endFrameBarrier;
    private EntityCommandQueue<Entity> __entityManager;

    private SharedHashMap<Entity, Entity> __entityHandles;
    private SharedHashMap<Entity, Entity> __handleEntities;

    public override bool isSingle => true;

    protected override void OnCreate()
    {
        base.OnCreate();

        __structChangeManagerGroup = GameItemStructChangeManager.GetEntityQuery(ref this.GetState());

        World world = World;
        __endFrameBarrier = world.GetOrCreateSystemUnmanaged<EndFrameStructChangeSystem>().manager.destoyEntityPool;

        __itemManager = world.GetOrCreateSystemUnmanaged<GameItemSystem>().manager;
        __containerSystem = world.GetOrCreateSystemManaged<GameDataItemContainerDeserializationSystem>();
    }

    protected override void OnUpdate()
    {
        var sructChangeManager = __structChangeManagerGroup.GetSingleton<GameItemStructChangeManager>();
        __entityHandles = sructChangeManager.entityHandles;
        __handleEntities = sructChangeManager.handleEntities;

        __entityManager = __endFrameBarrier.Create();

        base.OnUpdate();

        var jobHandle = Dependency;

        __entityHandles.lookupJobManager.readWriteJobHandle = jobHandle;
        __handleEntities.lookupJobManager.readWriteJobHandle = jobHandle;

        __itemManager.lookupJobManager.readWriteJobHandle = jobHandle;

        __entityManager.AddJobHandleForProducer<GameDataItemDeserializationSystem>(jobHandle);

        __containerSystem.AddReadOnlyDependency(jobHandle);
    }

    protected override DeserializerFactory _Get(ref JobHandle jobHandle)
    {
        jobHandle = JobHandle.CombineDependencies(jobHandle, __entityHandles.lookupJobManager.readWriteJobHandle, __handleEntities.lookupJobManager.readWriteJobHandle);
        jobHandle = JobHandle.CombineDependencies(jobHandle, __itemManager.lookupJobManager.readWriteJobHandle, __containerSystem.readOnlyJobHandle);

        DeserializerFactory deserializerFactory;
        deserializerFactory.manager = __itemManager.value;
        deserializerFactory.types = __containerSystem.types;
        deserializerFactory.entityType = GetEntityTypeHandle();
        deserializerFactory.instanceType = GetComponentTypeHandle<GameItemData>();
        deserializerFactory.entityHandles = __entityHandles.writer;
        deserializerFactory.handleEntities = __handleEntities.writer;
        deserializerFactory.entityManager = __entityManager.writer;

        return deserializerFactory;
    }
}

[DisableAutoCreation, AlwaysUpdateSystem, UpdateAfter(typeof(GameDataItemDeserializationSystem))]
public partial class GameDataItemRootDeserializationSystem : EntityDataDeserializationComponentSystem<GameItemRoot, GameDataItemRootDeserializationSystem.Deserializer, GameDataItemRootDeserializationSystem.DeserializerFactory>
{
    public struct Item
    {
        public int index;
        public GameDataItem value;
    }

    public struct Deserializer : IEntityDataDeserializer
    {
        [ReadOnly]
        public NativeArray<Entity> entityArray;

        public NativeParallelMultiHashMap<Entity, Item> items;

        public void Deserialize(int index, ref EntityDataReader reader)
        {
            int length = reader.Read<int>();
            if (length < 1)
                return;

            Entity entity = entityArray[index];
            Item item;
            var items = reader.ReadArray<GameDataItem>(length);
            for (int i = 0; i < length; ++i)
            {
                item.index = i;
                item.value = items[i];
                this.items.Add(entity, item);
            }
        }
    }

    public struct DeserializerFactory : IEntityDataFactory<Deserializer>
    {
        [ReadOnly]
        public EntityTypeHandle entityType;

        public NativeParallelMultiHashMap<Entity, Item> items;

        public Deserializer Create(in ArchetypeChunk chunk, int firstEntityIndex)
        {
            Deserializer deserializer;
            deserializer.entityArray = chunk.GetNativeArray(entityType);
            deserializer.items = items;

            return deserializer;
        }
    }

    public struct Build : IJob
    {
        public int entityCount;

        public GameDataItemDeserializer instance;

        [ReadOnly]
        public NativeParallelMultiHashMap<Entity, Item> items;

        public ComponentLookup<GameItemRoot> roots;

        public void Execute()
        {
            if (entityCount > instance.entities.Count())
                return;

            using (var keyValueArrays = items.GetKeyValueArrays(Allocator.Temp))
            {

                Item item;
                GameItemRoot root;
                GameItemHandle handle;
                int length = keyValueArrays.Length;
                for (int i = 0; i < length; ++i)
                {
                    item = keyValueArrays.Values[i];
                    handle = instance.Deserialize(item.value);
                    if (item.index == 0)
                    {
                        root.handle = handle;

                        roots[keyValueArrays.Keys[i]] = root;
                    }
                }
            }
        }
    }

    private NativeParallelMultiHashMap<Entity, Item> __items;
    private GameItemManagerShared __itemManager;

    public override bool isSingle => true;

    protected override void OnCreate()
    {
        base.OnCreate();

        __itemManager = World.GetOrCreateSystemUnmanaged<GameItemSystem>().manager;

        __items = new NativeParallelMultiHashMap<Entity, Item>(1, Allocator.Persistent);
    }

    protected override void OnDestroy()
    {
        __items.Dispose();

        base.OnDestroy();
    }

    protected override void OnUpdate()
    {
        base.OnUpdate();

        ref var lookupJobManager = ref __itemManager.lookupJobManager;

        var jobHandle = Dependency;

        jobHandle = JobHandle.CombineDependencies(jobHandle, lookupJobManager.readWriteJobHandle);

        var systemGroup = base.systemGroup;
        var presentationSystem = systemGroup.presentationSystem;
        jobHandle = JobHandle.CombineDependencies(jobHandle, presentationSystem.readOnlyJobHandle);

        Build build;
        build.entityCount = systemGroup.initializationSystem.guids.Length;
        build.instance.manager = __itemManager.value;
        build.instance.entities = presentationSystem.entities;
        build.instance.instances = GetComponentLookup<GameItemData>(true);
        build.items = __items;
        build.roots = GetComponentLookup<GameItemRoot>();
        jobHandle = build.Schedule(jobHandle);

        lookupJobManager.readWriteJobHandle = jobHandle;

        presentationSystem.AddReadOnlyDependency(jobHandle);

        Dependency = jobHandle;
    }

    protected override DeserializerFactory _Get(ref JobHandle jobHandle)
    {
        DeserializerFactory deserializerFactory;
        deserializerFactory.entityType = GetEntityTypeHandle();
        deserializerFactory.items = __items;
        return deserializerFactory;
    }
}

[DisableAutoCreation, AlwaysUpdateSystem, UpdateAfter(typeof(GameDataItemDeserializationSystem))]
public partial class GameDataItemSiblingDeserializationSystem : EntityDataDeserializationComponentSystem<GameItemSibling, GameDataItemSiblingDeserializationSystem.Deserializer, GameDataItemSiblingDeserializationSystem.DeserializerFactory>
{
    public struct Key : IEquatable<Key>
    {
        public int index;

        public Entity entity;

        public bool Equals(Key other)
        {
            return index == other.index && entity == other.entity;
        }

        public override int GetHashCode()
        {
            return index ^ entity.GetHashCode();
        }
    }

    public struct Item
    {
        public int index;
        public GameDataItem value;
    }

    public struct Deserializer : IEntityDataDeserializer
    {
        [ReadOnly]
        public NativeArray<Entity> entityArray;

        public BufferAccessor<GameItemSibling> siblings;

        public NativeParallelMultiHashMap<Key, Item> items;

        public void Deserialize(in Key key, ref EntityDataReader reader)
        {
            int length = reader.Read<int>();
            if (length < 1)
                return;

            Item item;
            var items = reader.ReadArray<GameDataItem>(length);
            for (int i = 0; i < length; ++i)
            {
                item.index = i;
                item.value = items[i];
                this.items.Add(key, item);
            }
        }

        public void Deserialize(int index, ref EntityDataReader reader)
        {
            int length = reader.Read<int>();
            if (length < 1)
                return;

            var siblings = this.siblings[index];
            siblings.ResizeUninitialized(length);

            Key key;
            key.entity = entityArray[index];
            for (int i = 0; i < length; ++i)
            {
                key.index = i;

                Deserialize(key, ref reader);
            }
        }
    }

    public struct DeserializerFactory : IEntityDataFactory<Deserializer>
    {
        [ReadOnly]
        public EntityTypeHandle entityType;

        public BufferTypeHandle<GameItemSibling> siblingType;

        public NativeParallelMultiHashMap<Key, Item> items;

        public Deserializer Create(in ArchetypeChunk chunk, int firstEntityIndex)
        {
            Deserializer deserializer;
            deserializer.entityArray = chunk.GetNativeArray(entityType);
            deserializer.siblings = chunk.GetBufferAccessor(ref siblingType);
            deserializer.items = items;

            return deserializer;
        }
    }

    public struct Build : IJob
    {
        public int entityCount;

        public GameDataItemDeserializer instance;

        [ReadOnly]
        public NativeParallelMultiHashMap<Key, Item> items;

        public BufferLookup<GameItemSibling> siblings;

        public void Execute()
        {
            if (entityCount > instance.entities.Count())
                return;

            using (var keyValueArrays = items.GetKeyValueArrays(Allocator.Temp))
            {
                DynamicBuffer<GameItemSibling> siblings;
                GameItemSibling sibling;
                GameItemHandle handle;
                Key key;
                Item item;
                int length = keyValueArrays.Length;
                for (int i = 0; i < length; ++i)
                {
                    item = keyValueArrays.Values[i];
                    handle = instance.Deserialize(item.value);
                    if (item.index == 0)
                    {
                        sibling.handle = handle;

                        key = keyValueArrays.Keys[i];

                        siblings = this.siblings[key.entity];

                        siblings[key.index] = sibling;
                    }
                }
            }
        }
    }

    private NativeParallelMultiHashMap<Key, Item> __items;
    private GameItemManagerShared __itemManager;

    public override bool isSingle => true;

    protected override void OnCreate()
    {
        base.OnCreate();

        __itemManager = World.GetOrCreateSystemUnmanaged<GameItemSystem>().manager;

        __items = new NativeParallelMultiHashMap<Key, Item>(1, Allocator.Persistent);
    }

    protected override void OnDestroy()
    {
        __items.Dispose();

        base.OnDestroy();
    }

    protected override void OnUpdate()
    {
        base.OnUpdate();

        ref var lookupJobManager = ref __itemManager.lookupJobManager;

        var jobHandle = Dependency;

        jobHandle = JobHandle.CombineDependencies(jobHandle, lookupJobManager.readWriteJobHandle);

        var systemGroup = base.systemGroup;
        var presentationSystem = systemGroup.presentationSystem;
        jobHandle = JobHandle.CombineDependencies(jobHandle, presentationSystem.readOnlyJobHandle);

        Build build;
        build.entityCount = systemGroup.initializationSystem.guids.Length;
        build.instance.manager = __itemManager.value;
        build.instance.entities = presentationSystem.entities;
        build.instance.instances = GetComponentLookup<GameItemData>(true);
        build.siblings = GetBufferLookup<GameItemSibling>();
        build.items = __items;
        jobHandle = build.Schedule(jobHandle);

        lookupJobManager.readWriteJobHandle = jobHandle;

        presentationSystem.AddReadOnlyDependency(jobHandle);

        Dependency = jobHandle;
    }

    protected override DeserializerFactory _Get(ref JobHandle jobHandle)
    {
        DeserializerFactory deserializerFactory;
        deserializerFactory.entityType = GetEntityTypeHandle();
        deserializerFactory.siblingType = GetBufferTypeHandle<GameItemSibling>();
        deserializerFactory.items = __items;
        return deserializerFactory;
    }
}