using System;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Transforms;
using ZG;

public enum GameItemStatus
{
    Lose = 0x1F, 
    Picked = 0x3F
}

/*[UpdateInGroup(typeof(InitializationSystemGroup)), UpdateAfter(typeof(GameItemRootEntitySystem))]
public partial class GameItemRootSystem : SystemBase
{
    [BurstCompile]
    private struct Change : IJob
    {
        [ReadOnly]
        public NativeHashMap<GameItemHandle, Entity> rootEntities;

        [ReadOnly]
        public NativeArray<GameItemManager.Command> commands;

        public ComponentLookup<GameNodeStatus> states;

        public void Execute()
        {
            int length = commands.Length;
            Entity entity;
            GameItemManager.Command command;
            for (int i = 0; i < length; ++i)
            {
                command = commands[i];
                if (!command.sourceParentHandle.Equals(GameItemHandle.Empty))
                    continue;

                if (!rootEntities.TryGetValue(command.sourceHandle, out entity))
                    continue;

                GameNodeStatus status;
                switch (command.commandType)
                {
                    case GameItemManager.CommandType.Destroy:
                        int j;
                        GameItemManager.Command tempCommand;
                        for (j = i + 1; j < length; ++j)
                        {
                            tempCommand = commands[j];
                            if (tempCommand.commandType == GameItemManager.CommandType.Create && tempCommand.destinationHandle.Equals(command.sourceHandle))
                                break;
                        }

                        if (j < length)
                            continue;

                        break;
                    case GameItemManager.CommandType.Move:
                        break;
                    default:
                        continue;
                }

                status.value = (int)GameEntityStatus.Dead;
                states[entity] = status;
            }
        }
    }

    private GameItemSystem __itemSystem;
    private GameItemRootEntitySystem __itemRootEntitySystem;

    protected override void OnCreate()
    {
        base.OnCreate();

        World world = World;
        __itemSystem = world.GetOrCreateSystem<GameItemSystem>();
        __itemRootEntitySystem = world.GetOrCreateSystem<GameItemRootEntitySystem>();
    }

    protected override void OnUpdate()
    {
        if (!__itemSystem.isCreated)
            return;

        Change change;
        change.rootEntities = __itemRootEntitySystem.entities;
        change.commands = __itemSystem.oldCommands;
        change.states = GetComponentLookup<GameNodeStatus>();

        var jobHandle = JobHandle.CombineDependencies(__itemSystem.readOnlyJobHandle, __itemRootEntitySystem.readOnlyJobHandle);
        jobHandle = change.Schedule(jobHandle);

        __itemSystem.AddReadOnlyDependency(jobHandle);
        __itemRootEntitySystem.AddReadOnlyDependency(jobHandle);

        Dependency = jobHandle;
    }
}*/

[BurstCompile]//, CreateAfter(typeof(GameItemSystem)), UpdateInGroup(typeof(GameItemSystemGroup), OrderFirst = true)/*, UpdateBefore(typeof(GameItemSystem))*/]
public partial struct GameItemRootStatusSystem : ISystem, IEntityCommandProducerJob
{
    private struct CollectRoots
    {
        public GameItemManager.Hierarchy hierarchy;

        [ReadOnly] 
        public NativeArray<Entity> entityArray;

        [ReadOnly] 
        public NativeArray<Translation> translations;

        [ReadOnly] 
        public NativeArray<Rotation> rotations;
        
        [ReadOnly]
        public NativeArray<GameNodeStatus> states;

        public NativeArray<GameItemRoot> roots;

        public BufferAccessor<GameItemSpawnHandleCommand> spawnHandleCommands;

        public NativeQueue<GameItemHandle>.ParallelWriter handlesToDetach;
        public NativeQueue<GameItemHandle>.ParallelWriter handlesToRemove;
        
        public bool Execute(int index)
        {
            var status = (GameEntityStatus)states[index].value;
            if ((status & GameEntityStatus.Mask) != GameEntityStatus.Dead)
                return false;
            
            var handle = roots[index].handle;
            if (!hierarchy.GetChildren(handle, out var enumerator, out var item) || !item.parentHandle.Equals(GameItemHandle.Empty))
                return false;

            if (status == GameEntityStatus.Dead && index < this.spawnHandleCommands.Length)
            {
                var spawnHandleCommands = this.spawnHandleCommands[index];
                
                GameItemSpawnHandleCommand spawnHandleCommand;
                spawnHandleCommand.spawnType = GameItemSpawnType.Drop;
                spawnHandleCommand.transform = math.RigidTransform(rotations[index].Value, translations[index].Value);
                spawnHandleCommand.owner = entityArray[index];

                GameItemHandle siblingHandle;
                do
                {
                    while (enumerator.MoveNext())
                    {
                        spawnHandleCommand.handle = enumerator.Current.handle;
                        spawnHandleCommands.Add(spawnHandleCommand);

                        handlesToDetach.Enqueue(spawnHandleCommand.handle);
                    }

                    siblingHandle = item.siblingHandle;
                } while (hierarchy.GetChildren(siblingHandle, out enumerator, out item));
            }

            GameItemRoot root;
            root.handle = GameItemHandle.Empty;
            roots[index] = root;

            handlesToRemove.Enqueue(handle);

            return true;
        }
    }

    [BurstCompile]
    private struct CollectRootsEx : IJobChunk
    {
        public GameItemManager.Hierarchy hierarchy;

        [ReadOnly] 
        public EntityTypeHandle entityType;

        [ReadOnly] 
        public ComponentTypeHandle<Translation> translationType;

        [ReadOnly] 
        public ComponentTypeHandle<Rotation> rotationType;

        [ReadOnly]
        public ComponentTypeHandle<GameNodeStatus> statusType;

        public ComponentTypeHandle<GameItemRoot> rootType;

        public BufferTypeHandle<GameItemSpawnHandleCommand> spawnHandleCommandType;

        public NativeQueue<GameItemHandle>.ParallelWriter handlesToDetach;
        public NativeQueue<GameItemHandle>.ParallelWriter handlesToRemove;

        //public EntityCommandQueue<EntityCommandStructChange>.ParallelWriter entityManager;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            //var entityArray = chunk.Has(ref statusType) ? default : chunk.GetNativeArray(entityType);
            
            CollectRoots collectRoots;
            collectRoots.hierarchy = hierarchy;
            collectRoots.entityArray = chunk.GetNativeArray(entityType);
            collectRoots.translations = chunk.GetNativeArray(ref translationType);
            collectRoots.rotations = chunk.GetNativeArray(ref rotationType);
            collectRoots.states = chunk.GetNativeArray(ref statusType);
            collectRoots.roots = chunk.GetNativeArray(ref rootType);
            collectRoots.spawnHandleCommands = chunk.GetBufferAccessor(ref spawnHandleCommandType);
            collectRoots.handlesToDetach = handlesToDetach;
            collectRoots.handlesToRemove = handlesToRemove;

            //EntityCommandStructChange command;
            //command.componentType = ComponentType.ReadOnly<GameItemRoot>();
            
            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
            {
                if(collectRoots.Execute(i))

                /*if (entityArray.IsCreated)
                {
                    command.entity = entityArray[i];
                    
                    entityManager.Enqueue(command);
                }
                else*/
                    chunk.SetComponentEnabled(ref rootType, i, false);
            }
        }
    }

    private struct CollectSiblings
    {
        public BufferAccessor<GameItemSibling> siblings;

        public NativeQueue<GameItemHandle>.ParallelWriter handles;

        public void Execute(int index)
        {
            var siblings = this.siblings[index];
            foreach (var sibling in siblings)
                handles.Enqueue(sibling.handle);
            
            siblings.Clear();
        }
    }

    [BurstCompile]
    private struct CollectSiblingsEx : IJobChunk
    {
        //[ReadOnly]
        //public EntityTypeHandle entityType;

        //[ReadOnly]
        //public ComponentTypeHandle<GameNodeStatus> statusType;

        public BufferTypeHandle<GameItemSibling> siblingType;

        public NativeQueue<GameItemHandle>.ParallelWriter handles;

        //public EntityCommandQueue<EntityCommandStructChange>.ParallelWriter entityManager;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            //var entityArray = chunk.Has(ref statusType) ? default : chunk.GetNativeArray(entityType);

            //EntityCommandStructChange command;
            //command.componentType = ComponentType.ReadOnly<GameItemRoot>();

            CollectSiblings collectSiblings;
            collectSiblings.siblings = chunk.GetBufferAccessor(ref siblingType);
            collectSiblings.handles = handles;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
            {
                collectSiblings.Execute(i);
                
                /*if (entityArray.IsCreated)
                {
                    command.entity = entityArray[i];
                    
                    entityManager.Enqueue(command);
                }
                else*/
                    chunk.SetComponentEnabled(ref siblingType, i, false);
            }
        }
    }

    [BurstCompile]
    private struct RemoveHandles : IJob
    {
        public GameItemManager manager;
        public NativeQueue<GameItemHandle> handlesToDetach;
        public NativeQueue<GameItemHandle> handlesToRemove;

        public void Execute()
        {
            while (handlesToDetach.TryDequeue(out var handle))
                manager.DetachParent(handle);
            
            while (handlesToRemove.TryDequeue(out var handle))
                manager.Remove(handle, 0);

            handlesToRemove.Clear();
        }
    }

    public static readonly int InnerloopBatchCount = 1;

    private EntityQuery __rootGroup;
    private EntityQuery __siblingGroup;

    private EntityTypeHandle __entityType;

    private ComponentTypeHandle<Translation> __translationType;

    private ComponentTypeHandle<Rotation> __rotationType;

    private ComponentTypeHandle<GameNodeStatus> __statusType;
    private ComponentTypeHandle<GameItemRoot> __rootType;
    private BufferTypeHandle<GameItemSibling> __siblingType;
    private BufferTypeHandle<GameItemSpawnHandleCommand> __spawnHandleCommandType;

    private GameItemManagerShared __itemManager;
    //private EntityCommandPool<EntityCommandStructChange> __endFrameBarrier;
    private NativeQueue<GameItemHandle> __handlesToDetach;
    private NativeQueue<GameItemHandle> __handlesToRemove;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __rootGroup = builder
                    .WithAll<GameItemRoot>()
                    .WithNone<GameNodeOldStatus, GameItemDontDestroyOnDead>()
                    .WithOptions(EntityQueryOptions.IncludeDisabledEntities)
                    .Build(ref state);

        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __siblingGroup = builder
                .WithAll<GameItemSibling>()
                .WithNone<GameNodeOldStatus, GameItemDontDestroyOnDead>()
                .WithOptions(EntityQueryOptions.IncludeDisabledEntities)
                .Build(ref state);

        __entityType = state.GetEntityTypeHandle();
        
        __translationType = state.GetComponentTypeHandle<Translation>(true);

        __rotationType = state.GetComponentTypeHandle<Rotation>(true);

        __statusType = state.GetComponentTypeHandle<GameNodeStatus>(true);
        
        __rootType = state.GetComponentTypeHandle<GameItemRoot>();

        __siblingType = state.GetBufferTypeHandle<GameItemSibling>();
        
        __spawnHandleCommandType = state.GetBufferTypeHandle<GameItemSpawnHandleCommand>();

        var world = state.WorldUnmanaged;
        __itemManager = world.GetExistingSystemUnmanaged<GameItemSystem>().manager;

        //__endFrameBarrier = world.GetExistingSystemUnmanaged<BeginFrameStructChangeSystem>().manager.addComponentPool;

        __handlesToDetach = new NativeQueue<GameItemHandle>(Allocator.Persistent);
        __handlesToRemove = new NativeQueue<GameItemHandle>(Allocator.Persistent);
    }

    //[BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        __handlesToDetach.Dispose();
        __handlesToRemove.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        //var entityType = __entityType.UpdateAsRef(ref state);
        //var statusType = __statusType.UpdateAsRef(ref state);
        //var endFrameBarrier = __endFrameBarrier.Create();
        
        //var entityManager = endFrameBarrier.parallelWriter;
        ref var itemManagerJobManager = ref __itemManager.lookupJobManager;
        
        var inputDeps = JobHandle.CombineDependencies(itemManagerJobManager.readOnlyJobHandle, state.Dependency);
        //JobHandle? result = null;
        var handlesToDetach = __handlesToDetach.AsParallelWriter();
        var handlesToRemove = __handlesToRemove.AsParallelWriter();
        if (!__rootGroup.IsEmptyIgnoreFilter)
        {
            //state.CompleteDependency();

            //lookupJobManager.CompleteReadOnlyDependency();

            CollectRootsEx collect;
            collect.hierarchy = __itemManager.hierarchy;
            collect.entityType = __entityType.UpdateAsRef(ref state);
            collect.translationType = __translationType.UpdateAsRef(ref state);
            collect.rotationType = __rotationType.UpdateAsRef(ref state);
            collect.statusType = __statusType.UpdateAsRef(ref state);
            collect.rootType = __rootType.UpdateAsRef(ref state);
            collect.spawnHandleCommandType = __spawnHandleCommandType.UpdateAsRef(ref state);
            collect.handlesToDetach = handlesToDetach;
            collect.handlesToRemove = handlesToRemove;
            //collect.entityManager = entityManager;
            var jobHandle = collect.ScheduleParallelByRef(__rootGroup, inputDeps);
            inputDeps = jobHandle;
            /*if (result == null)
                result = jobHandle;
            else
                result = JobHandle.CombineDependencies(result.Value, jobHandle);*/

            //state.EntityManager.RemoveComponent<GameItemRoot>(__rootGroup);
        }

        if (!__siblingGroup.IsEmptyIgnoreFilter)
        {
            //state.CompleteDependency();

            CollectSiblingsEx collect;
            //collect.entityType = entityType;
            //collect.statusType = statusType;
            collect.siblingType = __siblingType.UpdateAsRef(ref state);
            collect.handles = handlesToRemove;
            //collect.entityManager = entityManager;
            var jobHandle = collect.ScheduleParallelByRef(__siblingGroup, inputDeps);
            inputDeps = jobHandle;
            /*if (result == null)
                result = jobHandle;
            else
                result = JobHandle.CombineDependencies(result.Value, jobHandle);*/

            //state.EntityManager.RemoveComponent<GameItemSibling>(__siblingGroup);
        }

        //if (result != null)
        {
            //var jobHandle = result.Value;
            //endFrameBarrier.AddJobHandleForProducer<GameItemRootStatusSystem>(jobHandle);
            var jobHandle = inputDeps;
            
            RemoveHandles removeHandles;
            removeHandles.manager = __itemManager.value;
            removeHandles.handlesToDetach = __handlesToDetach;
            removeHandles.handlesToRemove = __handlesToRemove;

            jobHandle =
                removeHandles.ScheduleByRef(JobHandle.CombineDependencies(itemManagerJobManager.readWriteJobHandle,
                    jobHandle));

            itemManagerJobManager.readWriteJobHandle = jobHandle;
            
            state.Dependency = jobHandle;
        }
    }
}

[BurstCompile, CreateAfter(typeof(GameItemSystem)), UpdateInGroup(typeof(GameItemInitSystemGroup), OrderFirst = true)/*, UpdateAfter(typeof(EntityObjectSystemGroup))*/]
public partial struct GameItemRootEntitySystem : ISystem
{
    [Flags]
    private enum Flag
    {
        Root = 0x01, 
        New = 0x02
    }
    
    private struct Result
    {
        public Flag flag;
        public GameItemHandle handle;
        public Entity entity;
        
        public bool isRoot
        {
            get => (flag & Flag.Root) == Flag.Root;

            set
            {
                flag |= Flag.Root;
            }
        }
        
        public bool isNew
        {
            get => (flag & Flag.New) == Flag.New;

            set
            {
                flag |= Flag.New;
            }
        }
    }

    [BurstCompile]
    private struct Change : IJob
    {
        public GameItemManager.ReadOnlyInfos infos;

        [ReadOnly]
        public NativeArray<GameItemCommand> commands;

        public NativeHashMap<Entity, GameItemHandle> handles;

        public SharedHashMap<GameItemHandle, Entity>.Writer entities;

        public ComponentLookup<GameItemRoot> roots;

        public ComponentLookup<GameNodeStatus> states;

        public void Execute()
        {
            GameItemCommand command;
            int i, j, numCommands = commands.Length;
            for (i = 0; i < numCommands; ++i)
            {
                command = commands[i];

                /*if (command.destinationHandle.index == 18)
                    UnityEngine.Debug.Log($"{infos.GetRoot(command.destinationHandle).index}");*/

                switch (command.commandType)
                {
                    case GameItemCommandType.Connect:
                        {
                            if (entities.TryGetValue(command.sourceSiblingHandle, out Entity source))
                            {
                                if (entities.TryGetValue(command.destinationSiblingHandle, out Entity destination) && destination == source)
                                {
                                    if (handles.TryGetValue(destination, out var handle) && handle.Equals(command.destinationSiblingHandle))
                                        handles[destination] = command.destinationHandle;
                                }
                                else
                                {
                                    var handle = command.sourceSiblingHandle;
                                    while (infos.TryGetValue(handle, out var item))
                                    {
                                        __Remove(handle);

                                        handle = item.siblingHandle;
                                    }
                                }
                            }
                            else if (entities.TryGetValue(command.destinationHandle, out Entity entity))
                            {
                                bool isContains = handles.TryGetValue(entity, out var handle);
                                if (isContains && handle.Equals(command.destinationSiblingHandle))
                                    handles[entity] = command.destinationHandle;
                                else
                                {
                                    __Remove(command.destinationSiblingHandle);

                                    if (!isContains)
                                        handles[entity] = command.destinationHandle;
                                }

                                handle = command.destinationSiblingHandle;
                                while (infos.TryGetValue(handle, out var item))
                                {
                                    entities[handle] = entity;

                                    handle = item.siblingHandle;
                                }
                            }
                        }
                        break;
                    case GameItemCommandType.Move:
                        {
                            __Remove(command.sourceHandle);

                            if (infos.TryGetValue(command.sourceHandle, out var item))
                            {
                                GameItemHandle handle;
                                do
                                {
                                    handle = item.siblingHandle;

                                    __Remove(handle);

                                } while (infos.TryGetValue(handle, out item));
                            }
                        }
                        break;
                    case GameItemCommandType.Destroy:
                        {
                            GameItemCommand tempCommand;
                            for (j = i + 1; j < numCommands; ++j)
                            {
                                tempCommand = commands[j];
                                if (tempCommand.commandType == GameItemCommandType.Create && /*tempCommand.destinationHandle.Equals(command.sourceHandle)*/
                                    tempCommand.sourceHandle.Equals(command.sourceHandle) &&
                                    tempCommand.destinationHandle.Equals(command.destinationHandle) &&
                                    tempCommand.destinationParentHandle.Equals(GameItemHandle.Empty))
                                    break;
                            }

                            if (j < numCommands &&
                                entities.TryGetValue(command.sourceHandle, out Entity entity) &&
                                roots.HasComponent(entity) && 
                                entities.TryAdd(command.destinationHandle, entity))
                            {
                                entities.Remove(command.sourceHandle);

                                //if (handles.TryGetValue(entity, out var temp) && temp.Equals(command.sourceHandle))
                                handles[entity] = command.destinationHandle;

                                GameItemRoot root;
                                root.handle = command.destinationHandle;
                                roots[entity] = root;

                                continue;
                            }

                            __Remove(command.sourceHandle);
                        }
                        break;
                    default:
                        break;
                }
            }
        }

        private void __Remove(in GameItemHandle handle)
        {
            if (entities.TryGetValue(handle, out Entity entity))
            {
                entities.Remove(handle);

                __Remove(entity, handle);
            }
        }

        private void __Remove(in Entity entity, in GameItemHandle handle)
        {
            if (handles.TryGetValue(entity, out var temp) && temp.Equals(handle))
            {
                handles.Remove(entity);

                if (roots.HasComponent(entity))
                {
                    var root = roots[entity];
                    if (root.handle.Equals(handle))
                    {
                        root.handle = GameItemHandle.Empty;
                        roots[entity] = root;
                        roots.SetComponentEnabled(entity, false);

                        if (states.HasComponent(entity))
                        {
                            //UnityEngine.Debug.LogError($"Item {entity.Index} : {handle.index}");

                            GameNodeStatus status;
                            status.value = (int)(infos.TryGetValue(handle, out var item) && !item.parentHandle.Equals(GameItemHandle.Empty) ? GameItemStatus.Picked : GameItemStatus.Lose);

                            states[entity] = status;
                        }
                    }
                }
            }
        }
    }

    private struct Refresh
    {
        public GameItemManager.ReadOnlyInfos infos;

        [ReadOnly]
        public NativeArray<Entity> entityArray;

        [ReadOnly]
        public NativeArray<GameItemRoot> roots;

        [ReadOnly]
        public NativeHashMap<Entity, GameItemHandle> handles;

        public NativeQueue<Result>.ParallelWriter results;

        public void Execute(int index)
        {
            Entity entity = entityArray[index];
            var root = roots[index].handle;
            if (handles.TryGetValue(entity, out var handle) && handle.Equals(root))
                return;

            if (infos.TryGetValue(handle, out var item))
            {
                Result result;
                result.entity = entity;
                result.handle = handle;
                result.flag = Flag.Root;
                results.Enqueue(result);

                result.flag = 0;

                handle = item.siblingHandle;
                while (infos.TryGetValue(handle, out item))
                {
                    result.handle = handle;
                    results.Enqueue(result);

                    handle = item.siblingHandle;
                }
            }

            if (infos.TryGetValue(root, out item))
            {
                Result result;
                result.entity = entity;
                result.handle = root;
                result.flag =  Flag.Root | Flag.New;
                results.Enqueue(result);

                result.flag = Flag.New;

                handle = item.siblingHandle;
                while (infos.TryGetValue(handle, out item))
                {
                    result.handle = handle;
                    results.Enqueue(result);

                    handle = item.siblingHandle;
                }
            }
        }
    }

    [BurstCompile]
    private struct RefreshEx : IJobChunk
    {
        public GameItemManager.ReadOnlyInfos infos;

        [ReadOnly]
        public EntityTypeHandle entityType;

        [ReadOnly]
        public ComponentTypeHandle<GameItemRoot> rootType;

        [ReadOnly]
        public NativeHashMap<Entity, GameItemHandle> handles;

        public NativeQueue<Result>.ParallelWriter results;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Refresh refresh;
            refresh.infos = infos;
            refresh.entityArray = chunk.GetNativeArray(entityType);
            refresh.roots = chunk.GetNativeArray(ref rootType);
            refresh.handles = handles;
            refresh.results = results;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                refresh.Execute(i);
        }
    }

    [BurstCompile]
    private struct Apply : IJob
    {
        public NativeQueue<Result> results;
        public NativeHashMap<Entity, GameItemHandle> handles;
        public SharedHashMap<GameItemHandle, Entity>.Writer entities;

        public void Execute()
        {
            Entity entity;
            while (results.TryDequeue(out var result))
            {
                if (result.isNew)
                {
                    if (result.isRoot)
                        handles[result.entity] = result.handle;

                    entities[result.handle] = result.entity;
                }
                else if (entities.TryGetValue(result.handle, out entity) && entity == result.entity)
                {
                    if (result.isRoot)
                        handles.Remove(entity);
                    
                    entities.Remove(result.handle);
                }
            }
        }
    }

    private EntityQuery __group;

    public EntityTypeHandle __entityType;

    public ComponentTypeHandle<GameItemRoot> __rootType;

    private ComponentLookup<GameItemRoot> __roots;

    private ComponentLookup<GameNodeStatus> __states;

    private GameItemManagerShared __itemManager;
    private NativeQueue<Result> __results;

    private NativeHashMap<Entity, GameItemHandle> __handles;

    //根物品对应的实体
    public SharedHashMap<GameItemHandle, Entity> entities
    {
        get;

        private set;
    }

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        entities = new SharedHashMap<GameItemHandle, Entity>(Allocator.Persistent);

        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __group = builder
                    .WithAll<GameItemRoot, GameNodeStatus>()
                    .WithOptions(EntityQueryOptions.IncludeDisabledEntities)
                    .Build(ref state);

        __group.SetChangedVersionFilter(ComponentType.ReadOnly<GameItemRoot>());

        __entityType = state.GetEntityTypeHandle();
        __rootType = state.GetComponentTypeHandle<GameItemRoot>();

        __roots = state.GetComponentLookup<GameItemRoot>();
        __states = state.GetComponentLookup<GameNodeStatus>();

        __itemManager = state.WorldUnmanaged.GetExistingSystemUnmanaged<GameItemSystem>().manager;

        __results = new NativeQueue<Result>(Allocator.Persistent);
        __handles = new NativeHashMap<Entity, GameItemHandle>(1, Allocator.Persistent);
    }

    //[BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        __results.Dispose();
        __handles.Dispose();
        entities.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!__itemManager.isCreated)
            return;

        var entities = this.entities;
        ref var entitiesJobManager = ref entities.lookupJobManager;
        entitiesJobManager.CompleteReadWriteDependency();

        var infos = __itemManager.value.readOnlyInfos;
        var writer = entities.writer;

        Change change;
        change.infos = infos;
        change.commands = __itemManager.oldCommands;
        change.handles = __handles;
        change.entities = writer;
        change.roots = __roots.UpdateAsRef(ref state);
        change.states = __states.UpdateAsRef(ref state);

        ref var itemManagerJobManager = ref __itemManager.lookupJobManager;

        JobHandle jobHandle = change.ScheduleByRef(JobHandle.CombineDependencies(itemManagerJobManager.readOnlyJobHandle, state.Dependency)), result = jobHandle;
        if (!__group.IsEmptyIgnoreFilter)
        {
            RefreshEx refresh;
            refresh.infos = infos;
            refresh.entityType = __entityType.UpdateAsRef(ref state);
            refresh.rootType = __rootType.UpdateAsRef(ref state);
            refresh.handles = __handles;
            refresh.results = __results.AsParallelWriter();
            jobHandle = refresh.ScheduleParallelByRef(__group, jobHandle);

            Apply apply;
            apply.results = __results;
            apply.handles = __handles;
            apply.entities = writer;
            result = apply.ScheduleByRef(jobHandle);
        }

        itemManagerJobManager.AddReadOnlyDependency(jobHandle);

        entitiesJobManager.readWriteJobHandle = result;

        state.Dependency = result;
    }
}

/*
[UpdateInGroup(typeof(InitializationSystemGroup)), UpdateAfter(typeof(EntityObjectSystemGroup))]
public partial class GameItemRootEntitySystem : ReadOnlyLookupSystem
{
    [BurstCompile]
    private struct DidChange : IJobChunk
    {
        public uint lastSystemVersion;
        public NativeArray<int> results;
        [ReadOnly]
        public ComponentTypeHandle<GameItemRoot> rootType;

        public void Execute(ArchetypeChunk chunk, int chunkIndex, int firstEntityIndex)
        {
            results[chunkIndex] = chunk.DidChange(rootType, lastSystemVersion) ? 1 : 0;
        }
    }

    [BurstCompile]
    private struct ApplyChange : IJob
    {
        [ReadOnly, DeallocateOnJobCompletion]
        public NativeArray<int> input;

        public NativeArray<int> output;

        public void Execute()
        {
            bool isChanged = false;
            int length = input.Length;
            for (int i = 0; i < length; ++i)
            {
                if (input[i] != 0)
                {
                    isChanged = true;

                    break;
                }
            }

            output[0] = isChanged ? 1 : 0;
        }
    }

    private struct CountOf
    {
        public NativeCounter.Concurrent handleCount;
        [ReadOnly]
        public GameItemManager.Infos infos;
        [ReadOnly]
        public NativeArray<GameItemRoot> roots;

        public void Execute(int index)
        {
            int count = 1;
            if (infos.TryGetValue(roots[index].handle, out var item))
            {
                GameItemHandle handle;
                do
                {
                    ++count;

                    handle = item.siblingHandle;
                } while (infos.TryGetValue(handle, out item));
            }

            handleCount.Add(count);
        }
    }

    [BurstCompile]
    private struct CountOfEx : IJobChunk
    {
        public NativeCounter.Concurrent handleCount;
        [ReadOnly]
        public GameItemManager.Infos infos;
        [ReadOnly]
        public NativeArray<int> result;
        [ReadOnly]
        public ComponentTypeHandle<GameItemRoot> rootType;

        public void Execute(ArchetypeChunk chunk, int chunkIndex, int firstEntityIndex)
        {
            if (result[0] == 0)
                return;

            CountOf countOf;
            countOf.handleCount = handleCount;
            countOf.infos = infos;
            countOf.roots = chunk.GetNativeArray(rootType);

            int count = chunk.Count;
            for (int i = 0; i < count; ++i)
                countOf.Execute(i);
        }
    }

    [BurstCompile]
    private struct Clear : IJob
    {
        [ReadOnly]
        public NativeArray<int> result;

        public NativeCounter entityCount;

        public NativeHashMap<GameItemHandle, Entity> entities;

        public void Execute()
        {
            if (result[0] == 0)
                return;

            entities.Clear();
            entities.Capacity = math.max(entities.Capacity, entityCount.count);

            entityCount.count = 0;
        }
    }

    private struct Build
    {
        [ReadOnly]
        public GameItemManager.Infos infos;
        [ReadOnly]
        public NativeArray<Entity> entityArray;
        [ReadOnly]
        public NativeArray<GameItemRoot> roots;
        public NativeHashMap<GameItemHandle, Entity>.ParallelWriter entities;

        public void Execute(int index)
        {
            Entity entity = entityArray[index];
            var handle = roots[index].handle;
            entities.TryAdd(handle, entity);
            //UnityEngine.Assertions.Assert.IsTrue(result);

            if (infos.TryGetValue(handle, out var item))
            {
                handle = item.siblingHandle;
                while (infos.TryGetValue(handle, out item))
                {
                    entities.TryAdd(handle, entity);

                    handle = item.siblingHandle;
                }
            }
        }
    }

    [BurstCompile]
    private struct BuildEx : IJobChunk
    {
        [ReadOnly]
        public GameItemManager.Infos infos;
        [ReadOnly, DeallocateOnJobCompletion]
        public NativeArray<int> result;
        [ReadOnly]
        public EntityTypeHandle entityType;
        [ReadOnly]
        public ComponentTypeHandle<GameItemRoot> rootType;
        public NativeHashMap<GameItemHandle, Entity>.ParallelWriter entities;

        public void Execute(ArchetypeChunk chunk, int chunkIndex, int firstEntityIndex)
        {
            if (result[0] == 0)
                return;

            Build build;
            build.infos = infos;
            build.entityArray = chunk.GetNativeArray(entityType);
            build.roots = chunk.GetNativeArray(rootType);
            build.entities = entities;

            int count = chunk.Count;
            for (int i = 0; i < count; ++i)
                build.Execute(i);
        }
    }

    private EntityQuery __group;
    private GameItemSystem __itemSystem;
    private NativeCounter __counter;

    public NativeHashMap<GameItemHandle, Entity> entities
    {
        get;

        private set;
    }

    protected override void OnCreate()
    {
        base.OnCreate();

        __group = GetEntityQuery(
            new EntityQueryDesc()
            {
                All = new ComponentType[]
                {
                    ComponentType.ReadOnly<GameItemRoot>(), 
                    ComponentType.ReadOnly<GameNodeStatus>()
                },
                Options = EntityQueryOptions.IncludeDisabled
            });

        __itemSystem = World.GetOrCreateSystem<GameItemSystem>();

        __counter = new NativeCounter(Allocator.Persistent);

        entities = new NativeHashMap<GameItemHandle, Entity>(1, Allocator.Persistent);
    }

    protected override void OnDestroy()
    {
        __counter.Dispose();

        entities.Dispose();

        base.OnDestroy();
    }

    protected override void _Update()
    {
        __counter.count = 0;

        JobHandle jobHandle;
        var rootType = GetComponentTypeHandle<GameItemRoot>(true);
        NativeArray<int> output = new NativeArray<int>(1, Allocator.TempJob);
        var entities = this.entities;
        if (__group.CalculateEntityCount() == entities.Count())
        {
            NativeArray<int> input = new NativeArray<int>(__group.CalculateChunkCount(), Allocator.TempJob);

            DidChange didChange;
            didChange.lastSystemVersion = LastSystemVersion;
            didChange.results = input;
            didChange.rootType = rootType;
            jobHandle = didChange.ScheduleParallel(__group, Dependency);

            ApplyChange applyChange;
            applyChange.input = input;
            applyChange.output = output;
            jobHandle = applyChange.Schedule(jobHandle);
        }
        else
        {
            output[0] = 1;

            jobHandle = Dependency;
        }

        var infos = __itemSystem.manager.infos;
        CountOfEx countOf;
        countOf.handleCount = __counter;
        countOf.infos = infos;
        countOf.result = output;
        countOf.rootType = rootType;
        jobHandle = countOf.ScheduleParallel(__group, JobHandle.CombineDependencies(__itemSystem.readOnlyJobHandle, jobHandle));

        Clear clear;
        clear.entityCount = __counter;
        clear.result = output;
        clear.entities = entities;
        jobHandle = clear.Schedule(jobHandle);

        BuildEx build;
        build.infos = infos;
        build.result = output;
        build.entityType = GetEntityTypeHandle();
        build.rootType = rootType;
        build.entities = entities.AsParallelWriter();

        jobHandle = build.ScheduleParallel(__group, jobHandle);

        __itemSystem.AddReadOnlyDependency(jobHandle);

        Dependency = jobHandle;
    }
}*/
