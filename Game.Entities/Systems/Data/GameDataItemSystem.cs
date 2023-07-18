using System;
using Unity.Jobs;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using ZG;

[assembly: RegisterGenericJobType(typeof(ClearHashMap<int, int>))]

#region GameItemManager
//[assembly: RegisterGenericJobType(typeof(EntityDataContainerSerialize<GameDataItemContainerSerializationSystem.Serializer>))]
[assembly: RegisterGenericJobType(typeof(EntityDataIndexComponentInit<GameItemData, GameItemDataSerializationWrapper>))]
[assembly: RegisterGenericJobType(typeof(EntityDataContainerDeserialize<GameDataItemContainerDeserializationSystem.Deserializer>))]

//[assembly: EntityDataSerialize(typeof(GameItemManager), typeof(GameDataItemContainerSerializationSystem))]
[assembly: EntityDataDeserialize(typeof(GameItemManager), typeof(GameDataItemContainerDeserializationSystem), (int)GameDataConstans.Version)]
#endregion

#region GameItemRoot
[assembly: RegisterGenericJobType(typeof(EntityDataComponentSerialize<GameDataItemRootSerializationSystem.Serializer, GameDataItemRootSerializationSystem.SerializerFactory>))]
[assembly: RegisterGenericJobType(typeof(EntityDataComponentDeserialize<GameDataItemRootDeserializationSystem.Deserializer, GameDataItemRootDeserializationSystem.DeserializerFactory>))]
//[assembly: EntityDataSerialize(typeof(GameItemRoot), typeof(GameDataItemRootSerializationSystem))]
[assembly: EntityDataDeserialize(typeof(GameItemRoot), typeof(GameDataItemRootDeserializationSystem), (int)GameDataConstans.Version)]
#endregion

#region GameItemSibling
[assembly: RegisterGenericJobType(typeof(EntityDataComponentSerialize<GameDataItemSiblingSerializationSystem.Serializer, GameDataItemSiblingSerializationSystem.SerializerFactory>))]
[assembly: RegisterGenericJobType(typeof(EntityDataComponentDeserialize<GameDataItemSiblingDeserializationSystem.Deserializer, GameDataItemSiblingDeserializationSystem.DeserializerFactory>))]
//[assembly: EntityDataSerialize(typeof(GameItemSibling), typeof(GameDataItemSiblingSerializationSystem))]
[assembly: EntityDataDeserialize(typeof(GameItemSibling), typeof(GameDataItemSiblingDeserializationSystem), (int)GameDataConstans.Version)]
#endregion

#region GameItemData
[assembly: RegisterGenericJobType(typeof(EntityDataComponentSerialize<EntityDataSerializationIndexComponentDataSystemCore<GameItemData, GameItemDataSerializationWrapper>.Serializer, EntityDataSerializationIndexComponentDataSystemCore<GameItemData, GameItemDataSerializationWrapper>.SerializerFactory>))]
[assembly: RegisterGenericJobType(typeof(EntityDataComponentDeserialize<GameDataItemDeserializationSystem.Deserializer, GameDataItemDeserializationSystem.DeserializerFactory>))]
//[assembly: EntityDataSerialize(typeof(GameItemData), typeof(GameDataItemSerializationSystem))]
[assembly: EntityDataDeserialize(typeof(GameItemData), typeof(GameDataItemDeserializationSystem), (int)GameDataConstans.Version)]
#endregion

public struct GameDataItem
{
    public int entityIndex;

    public int parentChildIndex;
    public int parentEntityIndex;

    public int siblingEntityIndex;
}

public struct GameDataItemContainer : IComponentData
{
    public NativeArray<Hash128>.ReadOnly typeGUIDs;
}

public struct GameItemDataSerializationWrapper : IEntityDataIndexSerializationWrapper<GameItemData>
{
    [ReadOnly]
    public GameItemManager.ReadOnlyInfos infos;

    public bool TryGet(in GameItemData data, out int index)
    {
        if (infos.TryGetValue(data.handle, out var item))
        {
            index = item.type;

            return true;
        }

        index = -1;

        return false;
    }

    public void Serialize(ref EntityDataWriter writer, in GameItemData data, int guidIndex)
    {
        if (!infos.TryGetValue(data.handle, out var item))
        {
            UnityEngine.Debug.LogError($"Item Handle {data.handle} Serialize Fail.");

            writer.Write(-1);

            return;
        }

        writer.Write(guidIndex);
        writer.Write(item.count);
    }
}

public struct GameItemDataDeserializationWrapper : IEntityDataIndexDeserializationWrapper<GameItemData>
{
    public GameItemManager manager;

    public GameItemData Deserialize(ref EntityDataReader reader, in NativeArray<int>.ReadOnly indices)
    {
        int typeIndex = reader.Read<int>();
        int count = reader.Read<int>();
        count = math.max(count, 1);

        int type = typeIndex == -1 ? -1 : indices[typeIndex];

        GameItemData instance;
        instance.handle = type == -1 ? GameItemHandle.Empty : manager.Add(type, ref count);

        return instance;
    }
}

/*public partial class GameItemContainerSystem : SystemBase
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
}*/

[BurstCompile,
    AutoCreateIn("Server"),
    CreateAfter(typeof(GameItemSystem)),
    CreateAfter(typeof(GameItemComponentStructChangeSystem)),
    //AlwaysUpdateSystem, 
    UpdateInGroup(typeof(GameItemInitSystemGroup)),
    UpdateBefore(typeof(GameItemComponentInitSystemGroup))/*, 
    UpdateAfter(typeof(GameItemEntitySystem)),
    UpdateAfter(typeof(GameItemRootEntitySystem))*/]
public partial struct GameDataItemSystem : ISystem
{
    private enum Flag
    {
        Removed = 0x01,
        Changed = 0x02
    }

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
            if (chunk.DidChange(ref serializableType, lastSystemVersion) ||
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
            if (index < roots.Length)
                hierarchyCount.Add(hierarchy.CountOf(roots[index].handle));

            if (index < this.siblings.Length)
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
        public SharedHashMap<Entity, Entity>.Reader handleEntities;

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

            Entity entity = handleEntities[GameItemStructChangeFactory.Convert(handle)];

            bool result = serializableEntities.TryAdd(entity, root);
            if (!result)
                UnityEngine.Debug.LogError($"Fail To Serializable Item {entity} : {handle} To Root {root} : {hierarchy.GetRoot(handle)}");

            //UnityEngine.Assertions.Assert.IsTrue(result);

            if (result && !serializables.HasComponent(entity))
            {
                EntityCommandStructChange command;
                command.componentType = ComponentType.ReadOnly<EntityDataSerializable>();
                command.entity = entity;
                entityManager.Enqueue(command);
            }
        }

        public void Execute(int index)
        {
            Entity entity = entityArray[index];

            if (index < roots.Length)
                Execute(roots[index].handle, entity);

            if (index < this.siblings.Length)
            {
                var siblings = this.siblings[index];
                int numSiblings = siblings.Length;
                for (int i = 0; i < numSiblings; ++i)
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
        public SharedHashMap<Entity, Entity>.Reader handleEntities;

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
            maskSerializable.handleEntities = handleEntities;
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
    private struct Filter : IJobParallelForDefer
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
        [ReadOnly, DeallocateOnJobCompletion]
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

        public NativeHashMap<Entity, bool> entitiesToRemove;

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
            if (handle.Equals(GameItemHandle.Empty))
                return;

            bool isRemoved = rootEntity == Entity.Null;

            //bool isChanged = isRemoved == serializables.HasComponent(entity);
            if (entities.TryGetValue(GameItemStructChangeFactory.Convert(handle), out Entity entity) &&
                isRemoved ? serializableEntities.Remove(entity) : serializableEntities.TryAdd(entity, rootEntity))
            {
                if (entitiesToRemove.TryGetValue(entity, out bool handleToRemove) && handleToRemove == isRemoved)
                {
                    if (!handle.Equals(GameItemHandle.Empty))
                        UnityEngine.Debug.LogError($"Wrong Handle {handle}");

                    return;
                }

                entitiesToRemove[entity] = isRemoved;
            }
            else
                return;

            if (!hierarchy.GetChildren(handle, out var enumerator, out var item))
                return;

            while (enumerator.MoveNext())
                Execute(enumerator.Current.handle, rootEntity);

            Execute(item.siblingHandle, rootEntity);

            /*bool isRemove = rootEntity == Entity.Null;
            if (entities.TryGetValue(GameItemStructChangeFactory.Convert(handle), out Entity entity) &&
                isRemove == serializables.HasComponent(entity) &&
                (isRemove ? serializableEntities.Remove(entity) : serializableEntities.TryAdd(entity, rootEntity)))
            {
                EntityCommandStructChange command;
                command.componentType = ComponentType.ReadOnly<EntityDataSerializable>();
                command.entity = entity;

                if (isRemove)
                    removeComponentCommander.Enqueue(command);
                else
                    addComponentCommander.Enqueue(command);
            }*/
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

                    if (rootEntity != Entity.Null)
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

            entitiesToRemove.Clear();

            int numCommands = commands.Length;
            for (int i = 0; i < numCommands; ++i)
                Execute(i);

            bool isRemoved;
            EntityCommandStructChange command;
            command.componentType = ComponentType.ReadOnly<EntityDataSerializable>();
            foreach (var entityToRemove in entitiesToRemove)
            {
                isRemoved = entityToRemove.Value;
                command.entity = entityToRemove.Key;
                if (serializables.HasComponent(command.entity) == isRemoved)
                {
                    if (isRemoved)
                        removeComponentCommander.Enqueue(command);
                    else
                        addComponentCommander.Enqueue(command);
                }
            }
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

    private EntityTypeHandle __entityType;
    private BufferTypeHandle<GameItemSibling> __siblingType;
    private ComponentTypeHandle<GameItemRoot> __rootType;
    private ComponentTypeHandle<EntityDataSerializable> __serializableType;
    private ComponentLookup<EntityDataSerializable> __serializables;

    private GameItemManagerShared __itemManager;
    private EntityCommandPool<EntityCommandStructChange> __addComponentCommander;
    private EntityCommandPool<EntityCommandStructChange> __removeComponentCommander;

    private NativeCounter __hierarchyCount;
    private NativeList<Entity> __oldEntities;
    private NativeHashMap<Entity, bool> __entitiesToRemove;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.SetAlwaysUpdateSystem(true);

        __structChangeManagerGroup = GameItemStructChangeManager.GetEntityQuery(ref state);

        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __group = builder
                .WithAll<EntityDataSerializable>()
                .WithAny<GameItemRoot, GameItemSibling>()
                .WithOptions(EntityQueryOptions.IncludeDisabledEntities)
                .Build(ref state);

        __entityType = state.GetEntityTypeHandle();
        __siblingType = state.GetBufferTypeHandle<GameItemSibling>(true);
        __rootType = state.GetComponentTypeHandle<GameItemRoot>(true);
        //__serializableType = state.GetComponentTypeHandle<EntityDataSerializable>(true);
        __serializables = state.GetComponentLookup<EntityDataSerializable>(true);

        var world = state.WorldUnmanaged;
        __itemManager = world.GetExistingSystemUnmanaged<GameItemSystem>().manager;

        var manager = world.GetExistingSystemUnmanaged<GameItemComponentStructChangeSystem>().manager;

        __removeComponentCommander = manager.removeComponentPool;
        __addComponentCommander = manager.addComponentPool;

        __hierarchyCount = new NativeCounter(Allocator.Persistent);
        __oldEntities = new NativeList<Entity>(Allocator.Persistent);

        __entitiesToRemove = new NativeHashMap<Entity, bool>(1, Allocator.Persistent);

        serializableEntities = new SharedHashMap<Entity, Entity>(Allocator.Persistent);
    }

    //[BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        __hierarchyCount.Dispose();
        __oldEntities.Dispose();
        __entitiesToRemove.Dispose();

        serializableEntities.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!__itemManager.isCreated)
            return;

        var rootType = __rootType.UpdateAsRef(ref state);
        var siblingType = __siblingType.UpdateAsRef(ref state);

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
            didChange.serializableType = __serializableType.UpdateAsRef(ref state);
            jobHandle = didChange.ScheduleParallelByRef(__group, state.Dependency);
        }
        else
        {
            __entityCount = entityCount;

            result[0] = 1;

            jobHandle = state.Dependency;
        }

        ref var itemJobManager = ref __itemManager.lookupJobManager;

        var hierarchy = __itemManager.hierarchy;

        CountOfEx countOf;
        countOf.result = result;
        countOf.hierarchyCount = __hierarchyCount;
        countOf.hierarchy = hierarchy;
        countOf.rootType = rootType;
        countOf.siblingType = siblingType;
        jobHandle = countOf.ScheduleParallelByRef(__group, JobHandle.CombineDependencies(itemJobManager.readOnlyJobHandle, jobHandle));

        //removeComponentCommander.AddJobHandleForProducer(jobHandle);

        var serializableEntities = this.serializableEntities;
        ref var serializableEntityJobManager = ref serializableEntities.lookupJobManager;
        serializableEntityJobManager.CompleteReadWriteDependency();

        Clear clear;
        clear.result = result;
        clear.hierarchyCount = __hierarchyCount;
        clear.oldEntities = __oldEntities;
        clear.serializableEntities = serializableEntities.writer;
        jobHandle = clear.ScheduleByRef(jobHandle);

        var serializables = __serializables.UpdateAsRef(ref state);

        var addComponentCommander = __addComponentCommander.Create();
        var addComponentParallelWriter = addComponentCommander.parallelWriter;
        var addComponentWriter = addComponentCommander.writer;

        var handleEntities = __structChangeManagerGroup.GetSingleton<GameItemStructChangeManager>().handleEntities;

        var handleEntitiesReader = handleEntities.reader;

        MaskSerializableEx maskSerializable;
        maskSerializable.result = result;
        maskSerializable.hierarchy = hierarchy;
        maskSerializable.handleEntities = handleEntitiesReader;
        maskSerializable.entityType = __entityType.UpdateAsRef(ref state);
        maskSerializable.rootType = rootType;
        maskSerializable.siblingType = siblingType;
        maskSerializable.serializables = serializables;
        maskSerializable.serializableEntities = serializableEntities.parallelWriter;
        maskSerializable.entityManager = addComponentParallelWriter;

        ref var entityJobManager = ref handleEntities.lookupJobManager;

        jobHandle = maskSerializable.ScheduleParallelByRef(__group, JobHandle.CombineDependencies(jobHandle, entityJobManager.readOnlyJobHandle));

        var removeComponentCommander = __removeComponentCommander.Create();
        var removeComponentParallelWriter = removeComponentCommander.parallelWriter;
        var removeComponentWriter = removeComponentCommander.writer;

        Filter filter;
        filter.entityArray = __oldEntities.AsDeferredJobArray();
        filter.serializableEntities = serializableEntities.reader;
        filter.serializables = serializables;
        filter.entityManager = removeComponentParallelWriter;
        jobHandle = filter.ScheduleByRef(__oldEntities, InnerloopBatchCount, jobHandle);

        Change change;
        change.result = result;
        change.hierarchy = hierarchy;
        change.entities = handleEntitiesReader;
        //change.rootEntities = __rootEntities.reader;
        change.serializables = serializables;
        change.entitiesToRemove = __entitiesToRemove;
        change.serializableEntities = serializableEntities.writer;
        change.addComponentCommander = addComponentWriter;
        change.removeComponentCommander = removeComponentWriter;

        change.commands = __itemManager.oldCommands;

        /*ref var rootEntityJobManager = ref __rootEntities.lookupJobManager;

        jobHandle = JobHandle.CombineDependencies(rootEntityJobManager.readOnlyJobHandle, jobHandle);*/
        jobHandle = change.ScheduleByRef(jobHandle);

        //这些指令可能Entity未被创建，所以不使用
        //change.commands = __itemManager.commands;
        //jobHandle = change.Schedule(jobHandle);

        itemJobManager.AddReadOnlyDependency(jobHandle);
        entityJobManager.AddReadOnlyDependency(jobHandle);
        serializableEntityJobManager.AddReadOnlyDependency(jobHandle);

        addComponentCommander.AddJobHandleForProducer<Change>(jobHandle);
        removeComponentCommander.AddJobHandleForProducer<Change>(jobHandle);

        state.Dependency = jobHandle;// result.Dispose(jobHandle);
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
    public SharedHashMap<Entity, Entity>.Reader entities;

    [ReadOnly]
    public ComponentLookup<EntityDataIdentity> identities;

    [ReadOnly]
    public ComponentLookup<EntityDataSerializable> serializable;

    public int GetEntityIndex(in GameItemHandle handle, in SharedHashMap<Hash128, int>.Reader entityIndices)
    {
        if (entities.TryGetValue(GameItemStructChangeFactory.Convert(handle), out Entity entity))
        {
            if (entityIndices.TryGetValue(identities[entity].guid, out int entityIndex))
                return entityIndex;

            UnityEngine.Debug.LogError($"Get entity index of {identities[entity].guid} fail: {serializable.HasComponent(entity)}.");
        }

        return -1;
    }

    public void Serialize(in GameItemHandle handle, SharedHashMap<Hash128, int>.Reader entityIndices, ref NativeArray<GameDataItem> items, ref int itemIndex)
    {
        if (!hierarchy.GetChildren(handle, out var enumerator, out var source))
            return;

        GameDataItem destination;
        destination.entityIndex = GetEntityIndex(handle, entityIndices);

        UnityEngine.Assertions.Assert.AreNotEqual(-1, destination.entityIndex);

        destination.parentChildIndex = source.parentChildIndex;
        destination.parentEntityIndex = GetEntityIndex(source.parentHandle, entityIndices);
        destination.siblingEntityIndex = GetEntityIndex(source.siblingHandle, entityIndices);

        items[itemIndex++] = destination;

        while (enumerator.MoveNext())
            Serialize(enumerator.Current.handle, entityIndices, ref items, ref itemIndex);

        Serialize(source.siblingHandle, entityIndices, ref items, ref itemIndex);
    }

    public void Serialize(ref EntityDataWriter writer, in GameItemHandle handle, in SharedHashMap<Hash128, int>.Reader entityIndices)
    {
        int count = hierarchy.CountOf(handle), itemIndex = 0;
        var items = new NativeArray<GameDataItem>(count, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
        Serialize(handle, entityIndices, ref items, ref itemIndex);

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

                return GameItemHandle.Empty;
            }

            return instances[entity].handle;
        }

        return GameItemHandle.Empty;
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

[BurstCompile,
    EntityDataSerializationSystem(typeof(GameItemManager)),
    CreateAfter(typeof(GameItemSystem)),
    CreateAfter(typeof(EntityDataSerializationInitializationSystem)),
    UpdateInGroup(typeof(EntityDataSerializationSystemGroup)), AutoCreateIn("Server")]
public partial struct GameDataItemContainerSerializationSystem : ISystem, IEntityDataSerializationIndexContainerSystem
{
    /*private struct Init
    {
        public GameItemManager.ReadOnlyInfos infos;

        [ReadOnly]
        public NativeArray<Hash128>.ReadOnly inputs;

        [ReadOnly]
        public NativeArray<GameItemData> instances;

        public SharedHashMap<int, int>.Writer typeIndices;

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

        public SharedHashMap<int, int>.Writer typeIndices;

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
    }*/

    //private EntityQuery __group;
    private GameItemManagerShared __itemManager;

    private EntityDataSerializationIndexContainerComponentDataSystemCore<GameItemData> __core;

    public SharedHashMap<int, int> guidIndices => __core.guidIndices;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __itemManager = state.WorldUnmanaged.GetExistingSystemUnmanaged<GameItemSystem>().manager;

        __core = new EntityDataSerializationIndexContainerComponentDataSystemCore<GameItemData>(ref state);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        __core.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!__itemManager.isCreated)
            return;

        GameItemDataSerializationWrapper wrapper;
        wrapper.infos = __itemManager.value.readOnlyInfos;
        
        ref var lookupJobManager = ref __itemManager.lookupJobManager;

        state.Dependency = JobHandle.CombineDependencies(state.Dependency, lookupJobManager.readOnlyJobHandle);

        __core.Update(SystemAPI.GetSingleton<GameDataItemContainer>().typeGUIDs, ref wrapper, ref state);

        lookupJobManager.AddReadOnlyDependency(state.Dependency);
    }
}

[BurstCompile,
    EntityDataSerializationSystem(typeof(GameItemRoot)),
    CreateAfter(typeof(GameItemSystem)),
    CreateAfter(typeof(EntityDataSerializationInitializationSystem)),
    UpdateInGroup(typeof(EntityDataSerializationSystemGroup)), AutoCreateIn("Server")]
public partial struct GameDataItemRootSerializationSystem : ISystem
{
    public struct Serializer : IEntityDataSerializer
    {
        public GameDataItemSerializer instance;

        [ReadOnly]
        public NativeArray<GameItemRoot> roots;

        public void Serialize(int index, in SharedHashMap<Hash128, int>.Reader entityIndices, ref EntityDataWriter writer)
        {
            instance.Serialize(ref writer, roots[index].handle, entityIndices);
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
    private ComponentLookup<EntityDataIdentity> __identities;
    private ComponentLookup<EntityDataSerializable> __serializable;
    private ComponentTypeHandle<GameItemRoot> __rootType;

    private GameItemManagerShared __itemManager;
    private EntityDataSerializationSystemCore __core;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __structChangeManagerGroup = GameItemStructChangeManager.GetEntityQuery(ref state);

        __identities = state.GetComponentLookup<EntityDataIdentity>(true);
        __serializable = state.GetComponentLookup<EntityDataSerializable>(true);
        __rootType = state.GetComponentTypeHandle<GameItemRoot>(true);

        __itemManager = state.WorldUnmanaged.GetExistingSystemUnmanaged<GameItemSystem>().manager;

        __core = EntityDataSerializationSystemCore.Create<GameItemRoot>(ref state);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        __core.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var handleEntities = __structChangeManagerGroup.GetSingleton<GameItemStructChangeManager>().handleEntities;

        state.Dependency = JobHandle.CombineDependencies(state.Dependency, __itemManager.lookupJobManager.readOnlyJobHandle, handleEntities.lookupJobManager.readOnlyJobHandle);

        SerializerFactory serializerFactory;
        serializerFactory.instance.hierarchy = __itemManager.value.hierarchy;
        serializerFactory.instance.entities = handleEntities.reader;
        serializerFactory.instance.identities = __identities.UpdateAsRef(ref state);
        serializerFactory.instance.serializable = __serializable.UpdateAsRef(ref state);
        serializerFactory.rootType = __rootType.UpdateAsRef(ref state);

        __core.Update<Serializer, SerializerFactory>(ref serializerFactory, ref state);

        var jobHandle = state.Dependency;
        __itemManager.lookupJobManager.AddReadOnlyDependency(jobHandle);
        handleEntities.lookupJobManager.AddReadOnlyDependency(jobHandle);
    }
}

[BurstCompile,
    EntityDataSerializationSystem(typeof(GameItemSibling)),
    CreateAfter(typeof(GameItemSystem)),
    CreateAfter(typeof(EntityDataSerializationInitializationSystem)),
    UpdateInGroup(typeof(EntityDataSerializationSystemGroup)), AutoCreateIn("Server")]
public partial struct GameDataItemSiblingSerializationSystem : ISystem
{
    public struct Serializer : IEntityDataSerializer
    {
        public GameDataItemSerializer instance;

        [ReadOnly]
        public BufferAccessor<GameItemSibling> siblings;

        public void Serialize(int index, in SharedHashMap<Hash128, int>.Reader entityIndices, ref EntityDataWriter writer)
        {
            var siblings = this.siblings[index];

            int numSiblings = siblings.Length;
            writer.Write(numSiblings);
            for (int i = 0; i < numSiblings; ++i)
                instance.Serialize(ref writer, siblings[i].handle, entityIndices);
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
    private ComponentLookup<EntityDataIdentity> __identities;
    private ComponentLookup<EntityDataSerializable> __serializable;
    private BufferTypeHandle<GameItemSibling> __siblingType;

    private GameItemManagerShared __itemManager;
    private EntityDataSerializationSystemCore __core;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __structChangeManagerGroup = GameItemStructChangeManager.GetEntityQuery(ref state);

        __identities = state.GetComponentLookup<EntityDataIdentity>(true);
        __serializable = state.GetComponentLookup<EntityDataSerializable>(true);
        __siblingType = state.GetBufferTypeHandle<GameItemSibling>(true);

        __itemManager = state.WorldUnmanaged.GetExistingSystemUnmanaged<GameItemSystem>().manager;

        __core = EntityDataSerializationSystemCore.Create<GameItemSibling>(ref state);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        __core.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var handleEntities = __structChangeManagerGroup.GetSingleton<GameItemStructChangeManager>().handleEntities;

        state.Dependency = JobHandle.CombineDependencies(state.Dependency, __itemManager.lookupJobManager.readOnlyJobHandle, handleEntities.lookupJobManager.readOnlyJobHandle);

        SerializerFactory serializerFactory;
        serializerFactory.instance.hierarchy = __itemManager.value.hierarchy;
        serializerFactory.instance.entities = handleEntities.reader;
        serializerFactory.instance.identities = __identities.UpdateAsRef(ref state);
        serializerFactory.instance.serializable = __serializable.UpdateAsRef(ref state);
        serializerFactory.siblingType = __siblingType.UpdateAsRef(ref state);

        __core.Update<Serializer, SerializerFactory>(ref serializerFactory, ref state);

        var jobHandle = state.Dependency;
        __itemManager.lookupJobManager.AddReadOnlyDependency(jobHandle);
        handleEntities.lookupJobManager.AddReadOnlyDependency(jobHandle);
    }
}

[BurstCompile,
    EntityDataSerializationSystem(typeof(GameItemData)),
    CreateAfter(typeof(GameItemSystem)),
    CreateAfter(typeof(GameDataItemContainerSerializationSystem)),
    UpdateInGroup(typeof(EntityDataSerializationSystemGroup)), AutoCreateIn("Server"),
    UpdateAfter(typeof(GameDataItemContainerSerializationSystem))]
public partial struct GameDataItemSerializationSystem : ISystem//EntityDataSerializationComponentSystem<GameItemData, GameDataItemSerializationSystem.Serializer, GameDataItemSerializationSystem.SerializerFactory>
{
    /*public struct Serializer : IEntityDataSerializer
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
    }*/

    private GameItemManagerShared __itemManager;

    private EntityDataSerializationIndexComponentDataSystemCore<GameItemData, GameItemDataSerializationWrapper> __core;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __itemManager = state.WorldUnmanaged.GetExistingSystemUnmanaged<GameItemSystem>().manager;
        __core = EntityDataSerializationIndexComponentDataSystemCore<GameItemData, GameItemDataSerializationWrapper>.Create<GameDataItemContainerSerializationSystem>(ref state);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        __core.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!__itemManager.isCreated)
            return;

        GameItemDataSerializationWrapper wrapper;
        wrapper.infos = __itemManager.value.readOnlyInfos;

        state.Dependency = JobHandle.CombineDependencies(state.Dependency, __itemManager.lookupJobManager.readOnlyJobHandle);

        __core.Update(ref wrapper, ref state);

        __itemManager.lookupJobManager.AddReadOnlyDependency(state.Dependency);
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

    private NativeList<int> __types;

    public NativeArray<int> types => __types.AsDeferredJobArray();

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
        deserializer.typeGuids = SystemAPI.GetSingleton<GameDataItemContainer>().typeGUIDs;
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
            if (handleEntities.TryGetValue(handle, out Entity temp))
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