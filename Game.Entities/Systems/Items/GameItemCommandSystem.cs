using System;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Jobs;
using ZG;
using System.Collections.Generic;

[UpdateInGroup(typeof(GameItemSystemGroup), OrderFirst = true)/*, UpdateBefore(typeof(GameItemSystem))*/]
public partial class GameItemCommandSystem : LookupSystem
{
    public enum CommandType
    {
        Move,
        Split
    }

    public struct Command
    {
        public CommandType type;
        public int parentChildIndex;
        public GameItemHandle parentHandle;
        public GameItemHandle handle;
    }

    public struct Result
    {
        public GameItemHandle handle;
        public float durability;
    }

    public struct Adapter
    {
        public int type;
        public float durability;
    }

    public struct AdapterData
    {
        public int type;

        public Adapter[] values;
    }

    [BurstCompile]
    private struct Apply : IJob
    {
        public GameItemManager manager;

        [ReadOnly]
        public ComponentLookup<GameItemDurability> durabilities;

        [ReadOnly]
        public NativeHashMap<int, float> maxDurabilities;

        [ReadOnly]
        public SharedHashMap<Entity, Entity>.Reader entities;

        [ReadOnly]
        public NativeParallelMultiHashMap<int, Adapter> adapters;

        [ReadOnly, DeallocateOnJobCompletion]
        public NativeArray<Command> commands;

        public NativeList<Result> results;

        public void Execute(int index)
        {
            GameItemInfo item;
            var command = commands[index];
            switch (command.type)
            {
                case CommandType.Move:
                    if (command.parentChildIndex == -1)
                    {
                        if (manager.GetChildren(command.handle, out var enumerator, out item))
                        {
                            bool isSibling = false;
                            GameItemHandle siblingHandle;
                            do
                            {
                                siblingHandle = item.siblingHandle;
                                if (siblingHandle.Equals(command.parentHandle))
                                {
                                    isSibling = true;

                                    break;
                                }
                            } while (manager.TryGetValue(siblingHandle, out item));

                            if (!isSibling)
                            {
                                if (manager.TryGetValue(command.parentHandle, out item))
                                {
                                    do
                                    {
                                        siblingHandle = item.siblingHandle;
                                        if (siblingHandle.Equals(command.handle))
                                        {
                                            isSibling = true;

                                            break;
                                        }
                                    } while (manager.TryGetValue(siblingHandle, out item));
                                }
                                else
                                    break;
                            }

                            var children = new NativeList<GameItemHandle>(Allocator.Temp);
                            while (enumerator.MoveNext())
                                children.Add(enumerator.Current.handle);

                            GameItemFindFlag flag = isSibling ? GameItemFindFlag.Self : GameItemFindFlag.Self | GameItemFindFlag.Siblings | GameItemFindFlag.Children;
                            int numChildren = children.Length, parentChildIndex;
                            GameItemHandle parentHandle, handle;
                            GameItemMoveType moveType;
                            for (int i = 0; i < numChildren; ++i)
                            {
                                handle = children[i];
                                if (manager.GetChildren(handle, out enumerator, out item))
                                {
                                    if (manager.Find(
                                        command.parentHandle,
                                        item.type,
                                        item.count,
                                        out parentChildIndex,
                                        out parentHandle,
                                        flag))
                                    {
                                        moveType = manager.Move(handle, parentHandle, parentChildIndex);

                                        UnityEngine.Assertions.Assert.AreNotEqual(GameItemMoveType.Error, moveType);
                                    }
                                    else
                                    {
                                        while (enumerator.MoveNext())
                                        {
                                            handle = enumerator.Current.handle;
                                            if (manager.TryGetValue(handle, out item) && manager.Find(
                                                 command.parentHandle,
                                                 item.type,
                                                 item.count,
                                                 out parentChildIndex,
                                                 out parentHandle,
                                                 flag))
                                            {
                                                moveType = manager.Move(handle, parentHandle, parentChildIndex);

                                                UnityEngine.Assertions.Assert.AreNotEqual(GameItemMoveType.Error, moveType);
                                            }
                                        }
                                    }
                                }
                            }

                            children.Dispose();
                        }
                    }
                    else
                    {
                        var handle = manager.GetChild(command.parentHandle, command.parentChildIndex);

                        if (manager.TryGetValue(handle, out var target))
                        {
                            if (adapters.TryGetFirstValue(target.type, out var adapter, out var iterator) &&
                                manager.TryGetValue(command.handle, out item))
                            {
                                bool isAdapted = false;
                                Result result;
                                do
                                {
                                    if (adapter.type != item.type)
                                        continue;

                                    if (!maxDurabilities.TryGetValue(target.type, out var destination))
                                        continue;

                                    if (!entities.TryGetValue(GameItemStructChangeFactory.Convert(handle), out var entity))
                                        continue;

                                    if (!durabilities.HasComponent(entity))
                                        continue;

                                    var source = durabilities[entity].value;
                                    destination -= source;

                                    int count = (int)math.ceil(destination / adapter.durability);
                                    if (count > 0)
                                    {
                                        result.durability = math.min(destination, adapter.durability * manager.Remove(command.handle, count));
                                        result.durability += source;
                                        result.handle = handle;
                                        results.Add(result);
                                    }

                                    isAdapted = true;

                                    break;
                                } while (adapters.TryGetNextValue(out adapter, ref iterator));

                                if (isAdapted)
                                    return;
                            }
                        }

                        var moveType = manager.Move(command.handle, command.parentHandle, command.parentChildIndex);

                        UnityEngine.Assertions.Assert.AreNotEqual(GameItemMoveType.Error, moveType);
                    }
                    break;
                case CommandType.Split:
                    if (manager.TryGetValue(command.handle, out item))
                    {
                        int count = item.count >> 1;
                        if (count < 1)
                            break;

                        var handle = manager.Move(
                            command.handle,
                            command.parentHandle.Equals(GameItemHandle.Empty) ? item.parentHandle : command.parentHandle,
                            command.parentChildIndex, ref count);

                        UnityEngine.Assertions.Assert.AreNotEqual(GameItemHandle.Empty, handle);
                    }
                    break;
            }
        }

        public void Execute()
        {
            //results.Clear();

            int numCommands = commands.Length;/*, count;
            float source, destination;
            Entity entity;
            Command command;
            GameItemHandle handle;
            GameItemInfo item;*/
            for (int i = 0; i < numCommands; ++i)
            {
                Execute(i);
                /*command = commands[i];
                if (!manager.TryGetValue(command.handle, out item))
                    continue;

                switch (command.type)
                {
                    case CommandType.Move:
                        if (command.parentChildIndex == -1)
                        {
                            if(manager.GetChildren(command.handle, out var enumerator, out var temp))
                            {
                                while(enumerator.MoveNext())
                                {

                                }
                            }
                        }
                        else
                        {
                            handle = manager.GetChild(command.parentHandle, command.parentChildIndex);

                            if (manager.TryGetValue(handle, out var target))
                            {
                                if (adapters.TryGetFirstValue(target.type, out var adapter, out var iterator))
                                {
                                    bool isAdapted = false;
                                    Result result;
                                    do
                                    {
                                        if (adapter.type != item.type)
                                            continue;

                                        if (!maxDurabilities.TryGetValue(target.type, out destination))
                                            continue;

                                        if (!entities.TryGetValue(GameItemStructChangeFactory.Convert(handle), out entity))
                                            continue;

                                        if (!durabilities.HasComponent(entity))
                                            continue;

                                        source = durabilities[entity].value;
                                        destination -= source;

                                        count = (int)math.ceil(destination / adapter.durability);
                                        if (count > 0)
                                        {
                                            result.durability = math.min(destination, adapter.durability * manager.Remove(command.handle, count));
                                            result.durability += source;
                                            result.handle = handle;
                                            results.Add(result);
                                        }

                                        isAdapted = true;

                                        break;
                                    } while (adapters.TryGetNextValue(out adapter, ref iterator));

                                    if (isAdapted)
                                        continue;
                                }
                            }

                            var type = manager.Move(command.handle, command.parentHandle, command.parentChildIndex);

                            UnityEngine.Assertions.Assert.AreNotEqual(GameItemMoveType.Error, type);
                        }
                        break;
                    case CommandType.Split:
                        count = item.count >> 1;
                        if (count < 1)
                            continue;

                        handle = manager.Move(
                            command.handle,
                            command.parentHandle.Equals(GameItemHandle.Empty) ? item.parentHandle : command.parentHandle,
                            command.parentChildIndex, ref count);

                        UnityEngine.Assertions.Assert.AreNotEqual(GameItemHandle.Empty, handle);
                        break;
                }*/
            }
        }
    }

    private EntityQuery __structChangeManagerGroup;
    private GameItemManagerShared __itemManager;
    private NativeHashMap<int, float> __maxDurabilities;
    private NativeParallelMultiHashMap<int, Adapter> __adapters;

    public NativeList<Command> commands
    {
        get;

        private set;
    }

    public NativeList<Result> results
    {
        get;

        private set;
    }

    public void Create(AdapterData[] datas, ICollection<KeyValuePair<int, float>> maxDurabilities)
    {
        __adapters = new NativeParallelMultiHashMap<int, Adapter>(1, Allocator.Persistent);
        foreach (var data in datas)
        {
            foreach (var value in data.values)
                __adapters.Add(data.type, value);
        }

        __maxDurabilities = new NativeHashMap<int, float>(maxDurabilities.Count, Allocator.Persistent);
        foreach (var pair in maxDurabilities)
            __maxDurabilities.Add(pair.Key, pair.Value);
    }

    protected override void OnCreate()
    {
        base.OnCreate();

        __structChangeManagerGroup = GameItemStructChangeManager.GetEntityQuery(ref this.GetState());

        World world = World;
        __itemManager = world.GetOrCreateSystemUnmanaged<GameItemSystem>().manager;

        commands = new NativeList<Command>(Allocator.Persistent);

        results = new NativeList<Result>(Allocator.Persistent);
    }

    protected override void OnDestroy()
    {
        commands.Dispose();

        results.Dispose();

        if (__adapters.IsCreated)
        {
            __adapters.Dispose();

            __maxDurabilities.Dispose();
        }

        base.OnDestroy();
    }

    protected override void _Update()
    {
        if (!__adapters.IsCreated)
            return;

        if (commands.Length < 1)
            return;

        var handleEntities = __structChangeManagerGroup.GetSingleton<GameItemStructChangeManager>().handleEntities;

        Apply apply;
        apply.manager = __itemManager.value;
        apply.durabilities = GetComponentLookup<GameItemDurability>(true);
        apply.maxDurabilities = __maxDurabilities;
        apply.entities = handleEntities.reader;
        apply.adapters = __adapters;
        apply.commands = commands.ToArray(Allocator.TempJob);
        apply.results = results;

        commands.Clear();

        ref var itemJobManager = ref __itemManager.lookupJobManager;
        ref var entityHandleJobManager = ref handleEntities.lookupJobManager;

        var jobHandle = apply.Schedule(JobHandle.CombineDependencies(itemJobManager.readWriteJobHandle, entityHandleJobManager.readOnlyJobHandle, Dependency));

        itemJobManager.readWriteJobHandle = jobHandle;

        entityHandleJobManager.AddReadOnlyDependency(jobHandle);

        Dependency = jobHandle;
    }
}

[AutoCreateIn("Server"),
    AlwaysUpdateSystem, 
    UpdateInGroup(typeof(GameItemInitSystemGroup)), 
    UpdateAfter(typeof(GameItemComponentInitSystemGroup))
    //UpdateBefore(typeof(GameItemTimeApplySystem)),
    //UpdateBefore(typeof(GameItemDurabilityApplySystem)),
    //UpdateAfter(typeof(GameItemBeginSystem)),
    /*UpdateAfter(typeof(GameDataItemSystem)),
    UpdateAfter(typeof(GameDataSystemGroup))*/]
public partial class GameItemResultSystem : LookupSystem
{
    public interface IComponentMap<T> where T : struct, IComponentData
    {
        T this[in Entity entity]
        {
            get;
        }
    }

    public enum ComponentValueType
    {
        Add,
        Remove, 
        Override
    }

    public enum ResultType
    {
        Add,
        Remove
    }

    public struct Result
    {
        public ResultType resultType;

        public int sourceParentChildIndex;
        public int sourceParentHandle;

        public int destinationParentChildIndex;
        public int destinationParentHandle;

        public int sourceSiblingHandle;
        public int destinationSiblingHandle;

        public int handle;

        public int type;
        public int count;

        public float time;
        public float durability;
    }

    public struct Version
    {
        public int value;

        public int parentChildIndex;

        public int parentHandle;

        public int siblingHandle;

        public int type;

        public int count;

        //public float durability;

        //public float time;

        public Entity entity;
    }

    public struct ItemChild
    {
        public int index;
        public int handle;
    }

    public struct ItemMask
    {
        public int commandStartIndex;
        public int commandCount;
        public GameItemHandle handle;
        //public int resultOffset;

        public bool Check(int commandIndex)
        {
            if (commandIndex < commandStartIndex || commandIndex >= commandStartIndex + commandCount)
                return false;

            return this.handle.Equals(handle);
        }

        public override string ToString()
        {
            return handle.ToString();
        }
    }

    private struct ComponentDataMap<T> : IComponentMap<T> where T : unmanaged, IComponentData
    {
        private ComponentLookup<T> __instance;

        public T this[in Entity entity] => __instance.HasComponent(entity) ? __instance[entity] : default;

        public ComponentDataMap(ComponentLookup<T> instance)
        {
            __instance = instance;
        }
    }

    private struct ComponentManager<T> : IComponentMap<T> where T : unmanaged, IComponentData
    {
        private EntityManager __instance;

        public T this[in Entity entity] => __instance.HasComponent<T>(entity) ? __instance.GetComponentData<T>(entity) : default;

        public ComponentManager(EntityManager instance)
        {
            __instance = instance;
        }
    }

    [BurstCompile]
    private struct Change : IJob
    {
        public GameItemManager.Hierarchy hierarchy;

        public NativeArray<int> commandCount;

        [ReadOnly]
        public NativeArray<GameItemCommand> oldCommands;

        [ReadOnly]
        public NativeArray<GameItemCommand> commands;

        [ReadOnly]
        public SharedList<GameItemChangeResult<GameItemDurability>>.Reader durabilityResults;

        [ReadOnly]
        public SharedList<GameItemChangeResult<GameItemTime>>.Reader timeResults;

        [ReadOnly]
        public SharedHashMap<Entity, Entity>.Reader entities;

        [ReadOnly]
        public SharedHashMap<GameItemHandle, Entity>.Reader rootEntities;

        [ReadOnly]
        public ComponentLookup<GameItemDurability> durabilities;

        [ReadOnly]
        public ComponentLookup<GameItemTime> times;

        [ReadOnly]
        public ComponentLookup<EntityDataSerializable> serializables;

        public NativeList<ItemMask> itemMasks;

        public NativeList<EntityData<Result>> results;

        public NativeParallelHashMap<int, Version> versions;

        public NativeParallelMultiHashMap<int, ItemChild> children;

        public bool GetRoot(in GameItemHandle handle, out Entity entity)
        {
            if (handle.Equals(GameItemHandle.Empty))
            {
                entity = Entity.Null;

                return false;
            }

            if (rootEntities.TryGetValue(handle, out entity) && serializables.HasComponent(entity))
                return true;

            if(hierarchy.TryGetValue(handle, out var item) && !item.parentHandle.Equals(GameItemHandle.Empty))
            {
                if (versions.TryGetValue(item.parentHandle.index, out var version))
                {
                    entity = version.entity;

                    return true;
                }

                if (GetRoot(item.parentHandle, out entity))
                    return true;
            }

            return false;
        }

        public float GetDurability(ComponentValueType type, in GameItemHandle handle, int commandIndex, ref int durabilityIndex)
        {
            return GetComponentValue(
                type, 
                commandIndex, 
                ref durabilityIndex, 
                handle, 
                durabilityResults, 
                entities, 
                new ComponentDataMap<GameItemDurability>(durabilities)).value;
            /*GameItemChangeResult<GameItemDurability> result;
            int numDurabilityResults = durabilityResults.length;
            while (durabilityIndex < numDurabilityResults)
            {
                result = durabilityResults[durabilityIndex];
                if (result.index > commandIndex)
                    break;

                ++durabilityIndex;

                if (result.index == commandIndex)
                {
                    UnityEngine.Assertions.Assert.AreEqual(handle, result.handle);

                    return isOrigin ? result.orgin.value : result.value.value;
                }
            }

            /*if (durabilityIndex < durabilityResults.Length)
            {
                result = durabilityResults[durabilityIndex];
                while (result.index < commandIndex)
                    result = durabilityResults[++durabilityIndex];

                if (result.index == commandIndex)
                    ++durabilityIndex;

                if (result.handle.Equals(handle))
                    return isOrigin ? result.orgin.value : result.value.value;
            }*

            for (int i = durabilityIndex - 1; i >= 0; --i)
            {
                result = durabilityResults[i];
                if (result.handle.Equals(handle))
                    return isOrigin ? result.orgin.value : result.value.value;
            }

            //当有time而没有durability的时候
            if (!entities.TryGetValue(GameItemStructChangeFactory.Convert(handle), out Entity entity))
                return 0.0f;

            return durabilities.HasComponent(entity) ? durabilities[entity].value : 0.0f;*/
        }

        public float GetTime(ComponentValueType type, in GameItemHandle handle, int commandIndex, ref int timeIndex)
        {
            return GetComponentValue(
                type, 
                commandIndex, 
                ref timeIndex, 
                handle, 
                timeResults, 
                entities, 
                new ComponentDataMap<GameItemTime>(times)).value;
        }

        public EntityData<Result> RemoveResult(
            bool isDiff, 
            ref int durabilityIndex,
            ref int timeIndex,
            int commandIndex,
            int sourceParentChildIndex,
            int sourceParentHandle,
            int sourceSiblingHandle,
            int destinationParentChildIndex,
            int destinationParentHandle,
            int destinationSiblingHandle,
            int type,
            int count,
            in Entity entity,
            in GameItemHandle handle)
        {
            EntityData<Result> result;
            result.value.resultType = ResultType.Remove;
            result.value.type = type;
            result.value.count = count;
            result.value.sourceParentChildIndex = sourceParentChildIndex;
            result.value.sourceParentHandle = sourceParentHandle;
            result.value.sourceSiblingHandle = sourceSiblingHandle;
            result.value.destinationParentChildIndex = destinationParentChildIndex;
            result.value.destinationParentHandle = destinationParentHandle;
            result.value.destinationSiblingHandle = destinationSiblingHandle;
            result.value.handle = handle.index;

            var componentValueType = isDiff ? ComponentValueType.Remove : ComponentValueType.Override;
            result.value.durability = GetDurability(componentValueType, handle, commandIndex, ref durabilityIndex);
            result.value.time = GetTime(componentValueType, handle, commandIndex, ref timeIndex);

            result.entity = entity;

            return result;
            //results.Add(result);
        }

        public EntityData<Result> AddResult(
            bool isDiff, 
            ref int durabilityIndex,
            ref int timeIndex,
            int commandIndex,
            int sourceParentChildIndex,
            int sourceParentHandle,
            int sourceSiblingHandle, 
            int destinationParentChildIndex,
            int destinationParentHandle,
            int destinationSiblingHandle,
            int type, 
            int count,
            in Entity entity,
            in GameItemHandle handle)
        {
            EntityData<Result> result;
            result.value.resultType = ResultType.Add;
            result.value.type = type;
            result.value.count = count;
            result.value.sourceParentChildIndex = sourceParentChildIndex;
            result.value.sourceParentHandle = sourceParentHandle;
            result.value.sourceSiblingHandle = sourceSiblingHandle;
            result.value.destinationParentChildIndex = destinationParentChildIndex;
            result.value.destinationParentHandle = destinationParentHandle;
            result.value.destinationSiblingHandle = destinationSiblingHandle;
            result.value.handle = handle.index;

            var componentValueType = isDiff ? ComponentValueType.Add : ComponentValueType.Override;
            result.value.durability = GetDurability(componentValueType, handle, commandIndex, ref durabilityIndex);
            result.value.time = GetTime(componentValueType, handle, commandIndex, ref timeIndex);

            result.entity = entity;

            return result;
        }

        public bool Remove(
            ref int durabilityIndex,
            ref int timeIndex, 
            int commandIndex,
            in GameItemHandle handle, 
            out Version version, 
            ref UnsafeList<EntityData<Result>> results)
        {
            if (hierarchy.GetChildren(handle, out var enumerator, out var item))
            {
                while (enumerator.MoveNext())
                {
                    Remove(
                        ref durabilityIndex, 
                        ref timeIndex, 
                        commandIndex, 
                        enumerator.Current.handle, 
                        out _,
                        ref results);
                }

                children.Remove(handle.index);

                Remove(
                    ref durabilityIndex,
                    ref timeIndex,
                    commandIndex,
                    item.siblingHandle, 
                    out _, 
                    ref results);

                if (versions.TryGetValue(handle.index, out version))
                {
                    var result = RemoveResult(
                        false, 
                        ref durabilityIndex,
                        ref timeIndex,
                        commandIndex,
                        version.parentChildIndex,
                        version.parentHandle,
                        version.siblingHandle,
                        -1,
                        -1,
                        -1,
                        version.type,
                        version.count,
                        version.entity,
                        handle);

                    if (!results.IsCreated)
                        results = new UnsafeList<EntityData<Result>>(1, Allocator.Temp, NativeArrayOptions.UninitializedMemory);

                    results.Add(result);

                    versions.Remove(handle.index);

                    return true;
                }
            }

            version = default;

            return false;
        }

        public bool Remove(
            ref int durabilityIndex,
            ref int timeIndex,
            int commandIndex,
            int handle, 
            int parentChildIndex, 
            int parentHandle, 
            int siblingHandle, 
            bool isReadOnly, 
            out Version version)
        {
            if(__Remove(
                ref durabilityIndex,
                ref timeIndex,
                commandIndex, 
                handle, 
                parentChildIndex, 
                parentHandle, 
                siblingHandle, 
                isReadOnly, 
                out version))
            {
                if (!isReadOnly && children.TryGetFirstValue(version.parentHandle, out var child, out var iterator))
                {
                    do
                    {
                        if (child.handle == handle)
                        {
                            //UnityEngine.Assertions.Assert.AreEqual(child.index, version.parentChildIndex);

                            children.Remove(iterator);

                            break;
                        }
                    } while (children.TryGetNextValue(out child, ref iterator));
                }

                return true;
            }

            return false;
        }

        public bool Destroy(
            ref int durabilityIndex,
            ref int timeIndex,
            int commandIndex, 
            in GameItemCommand command)
        {
            int parentHandle = command.destinationParentHandle.Equals(GameItemHandle.Empty) ? -1 : command.destinationParentHandle.index;
            if (Remove(
                ref durabilityIndex, 
                ref timeIndex, 
                commandIndex, 
                command.sourceHandle.index, 
                command.destinationParentChildIndex,
                parentHandle,
                command.destinationSiblingHandle.Equals(GameItemHandle.Empty) ? -1 : command.destinationSiblingHandle.index,
                command.commandType != GameItemCommandType.Destroy && parentHandle != -1 && versions.ContainsKey(parentHandle), 
                out var version))
            {
                UnityEngine.Assertions.Assert.AreEqual(version.value, command.sourceHandle.version);
                UnityEngine.Assertions.Assert.AreEqual(
                    version.parentHandle,
                    command.sourceParentHandle.Equals(GameItemHandle.Empty) ? -1 : command.sourceParentHandle.index);
                UnityEngine.Assertions.Assert.AreEqual(
                    version.siblingHandle,
                    command.sourceSiblingHandle.Equals(GameItemHandle.Empty) ? -1 : command.sourceSiblingHandle.index);

                return true;
            }

            return false;
        }

        public bool Update(
            ref int durabilityIndex,
            ref int timeIndex,
            int commandIndex,
            int handle, 
            int parentChildIndex,
            int parentHandle,
            int siblingHandle,
            in Entity entity)
        {
            if (versions.TryGetValue(handle, out var version) && version.entity != entity)
            {
                Update(
                    ref durabilityIndex, 
                    ref timeIndex, 
                    commandIndex, 
                    version.siblingHandle,
                    -1,
                    -1,
                    -1,
                    entity);

                version.entity = entity;
                versions[handle] = version;

                GameItemHandle itemHandle;
                itemHandle.index = handle;
                itemHandle.version = version.value;

                var result = AddResult(
                    false,
                    ref durabilityIndex,
                    ref timeIndex,
                    commandIndex,
                    parentChildIndex,
                    parentHandle,
                    siblingHandle,
                    version.parentChildIndex,
                    version.parentHandle,
                    version.siblingHandle,
                    version.type,
                    version.count,
                    version.entity,
                    itemHandle);

                results.Add(result);

                if (children.TryGetFirstValue(handle, out var child, out var iterator))
                {
                    do
                    {
                        Update(
                            ref durabilityIndex,
                            ref timeIndex,
                            commandIndex,
                            child.handle,
                            -1,
                            -1,
                            -1,
                            entity);
                    } while (children.TryGetNextValue(out child, ref iterator));
                }

                return true;
            }

            return false;
        }

        /*public bool AppendSiblings(
            ref int durabilityIndex, 
            ref int timeIndex, 
            int commandIndex, 
            in Entity entity, 
            in GameItemHandle handle,
            ref NativeList<ItemMask> masks)
        {
            if (!versions.ContainsKey(handle.index) && hierarchy.GetChildren(handle, out var enumerator, out var item))
            {
                int siblingHandle;
                if (AppendSiblings(
                    ref durabilityIndex,
                    ref timeIndex,
                    commandIndex,
                    entity,
                    item.siblingHandle, 
                    ref masks))
                    siblingHandle = item.siblingHandle.index;
                else
                    siblingHandle = -1;

                AddResult(
                    ref durabilityIndex,
                    ref timeIndex,
                    commandIndex,
                    -1,
                    -1,
                    -1,
                    -1,
                    siblingHandle,
                    item.type,
                    item.count, 
                    entity, 
                    handle);

                ref readonly var result = ref results.ElementAt(results.Length - 1);

                Version version;
                version.value = handle.version;
                version.parentHandle = -1;
                version.siblingHandle = siblingHandle;
                version.type = item.type;
                version.count = item.count;
                version.durability = result.value.durability;
                version.time = result.value.time;
                version.entity = entity;
                versions.Add(handle.index, version);

                GameItemChild child;
                while (enumerator.MoveNext())
                {
                    child = enumerator.Current;

                    Append(
                        ref durabilityIndex,
                        ref timeIndex,
                        commandIndex,
                        -1,
                        -1,
                        child.index,
                        handle.index,
                        entity,
                        child.handle);
                }

                return true;
            }

            return false;
        }*/

        public bool Append(
            ref int durabilityIndex,
            ref int timeIndex,
            int commandIndex,
            int commandStartIndex,
            int commandCount,
            int sourceParentChildIndex, 
            int sourceParentHandle,
            int sourceSiblingHandle,
            in Entity entity,
            in GameItemHandle handle, 
            ref UnsafeList<EntityData<Result>> results)
        {
            if(Remove(
                ref durabilityIndex, 
                ref timeIndex, 
                commandIndex - 1, 
                handle, 
                out var version, 
                ref results))
            {
                if (children.TryGetFirstValue(version.parentHandle, out var child, out var iterator))
                {
                    do
                    {
                        if (child.handle == handle.index)
                        {
                            UnityEngine.Assertions.Assert.AreEqual(child.index, version.parentChildIndex);

                            children.Remove(iterator);

                            break;
                        }
                    } while (children.TryGetNextValue(out child, ref iterator));
                }
            }

            return __Append(
                ref durabilityIndex,
                ref timeIndex,
                commandIndex,
                commandStartIndex, 
                commandCount, 
                sourceParentChildIndex,
                sourceParentHandle,
                sourceSiblingHandle,
                entity,
                handle,
                ref results);
        }

        public bool Apply(
            in GameItemCommand command, 
            int commandIndex,
            int commandStartIndex,
            int commandCount,
            ref int durabilityIndex,
            ref int timeIndex,
            ref UnsafeList<EntityData<Result>> results)
        {
            int numItemMasks = itemMasks.IsCreated ? itemMasks.Length : 0, commandIndexToMask = commandIndex + commandStartIndex;
            for (int i = 0; i < numItemMasks; ++i)
            {
                ref readonly var itemMask = ref itemMasks.ElementAt(i);
                if (itemMask.Check(commandIndexToMask) && itemMask.handle.Equals(command.destinationParentHandle) || itemMask.handle.Equals(command.destinationHandle))
                    return true;
            }

            if (!command.destinationParentHandle.Equals(GameItemHandle.Empty) && 
                versions.TryGetValue(command.destinationParentHandle.index, out var parentVersion) && 
                versions.TryGetValue(command.destinationHandle.index, out var version))
            {
                UnityEngine.Assertions.Assert.AreEqual(parentVersion.value, command.destinationParentHandle.version);
                UnityEngine.Assertions.Assert.AreEqual(version.value, command.destinationHandle.version);

                int sourceParentHandle = command.sourceParentHandle.Equals(GameItemHandle.Empty) ? -1 : command.sourceParentHandle.index,
                    sourceSiblingHandle = command.sourceSiblingHandle.Equals(GameItemHandle.Empty) ? -1 : command.sourceSiblingHandle.index, 
                    destinationSiblingHandle = command.destinationSiblingHandle.Equals(GameItemHandle.Empty) ? -1 : command.destinationSiblingHandle.index;

                if (command.destinationParentHandle.index != version.parentHandle)
                {
                    if (children.TryGetFirstValue(version.parentHandle, out var child, out var iterator))
                    {
                        do
                        {
                            if (child.handle == command.destinationHandle.index)
                            {
                                children.Remove(iterator);

                                break;
                            }
                        } while (children.TryGetNextValue(out child, ref iterator));
                    }

                    child.handle = command.destinationHandle.index;
                    child.index = command.destinationParentChildIndex;
                    children.Add(command.destinationParentHandle.index, child);
                }

                var result = AddResult(
                    command.commandType != GameItemCommandType.Move, 
                    ref durabilityIndex,
                    ref timeIndex,
                    commandIndex,
                    command.sourceParentChildIndex,
                    sourceParentHandle,
                    sourceSiblingHandle, 
                    command.destinationParentChildIndex,
                    command.destinationParentHandle.index,
                    destinationSiblingHandle,
                    command.type,
                    command.commandType == GameItemCommandType.Move ? command.destinationCount : command.count,
                    parentVersion.entity,
                    command.destinationHandle);

                version.parentChildIndex = command.destinationParentChildIndex;
                version.parentHandle = command.destinationParentHandle.index;
                version.siblingHandle = command.destinationSiblingHandle.Equals(GameItemHandle.Empty) ? -1 : command.destinationSiblingHandle.index;

                version.type = command.type;
                version.count += command.count;

                //version.entity = entity;

                versions[command.destinationHandle.index] = version;

                if (version.entity == parentVersion.entity)
                    this.results.Add(result);
                else
                    Update(
                        ref durabilityIndex,
                        ref timeIndex,
                        commandIndex,
                        command.destinationHandle.index,
                        command.sourceParentChildIndex,
                        sourceParentHandle,
                        sourceSiblingHandle,
                        parentVersion.entity);

                return true;
            }
            else if (GetRoot(command.destinationHandle, out Entity entity))
            {
                Append(
                    ref durabilityIndex,
                    ref timeIndex,
                    commandIndex,
                    commandStartIndex, 
                    commandCount, 
                    command.sourceParentChildIndex,
                    command.sourceParentHandle.Equals(GameItemHandle.Empty) ? -1 : command.sourceParentHandle.index,
                    command.sourceSiblingHandle.Equals(GameItemHandle.Empty) ? -1 : command.sourceSiblingHandle.index,
                    entity,
                    command.destinationHandle,
                    ref results);

                return true;
            }

            return false;
        }

        public bool Execute(
            ref int durabilityIndex,
            ref int timeIndex,
            ref int commandIndex,
            int commandStartIndex,
            int commandCount,
            in GameItemCommand command,
            in GameItemHandle moveOrginHandle, 
            ref UnsafeList<EntityData<Result>> results)
        {
            bool moveResult = true;
            int numItemMasks = itemMasks.IsCreated ? itemMasks.Length : 0, commandIndexToMask, i;
            Version version;
            switch (command.commandType)
            {
                case GameItemCommandType.Create:
                case GameItemCommandType.Add:
                    Apply(
                        command, 
                        commandIndex,
                        commandStartIndex,
                        commandCount, 
                        ref durabilityIndex,
                        ref timeIndex, 
                        ref results);
                    break;
                case GameItemCommandType.Connect:
                    commandIndexToMask = commandIndex + commandStartIndex;
                    for (i = 0; i < numItemMasks; ++i)
                    {
                        ref var itemMask = ref itemMasks.ElementAt(i);
                        if (itemMask.Check(commandIndexToMask) && itemMask.handle.Equals(command.destinationHandle))
                            break;
                    }

                    if (i == numItemMasks && versions.TryGetValue(command.destinationHandle.index, out version))
                    {
                        UnityEngine.Assertions.Assert.AreEqual(version.value, command.destinationHandle.version);
                        UnityEngine.Assertions.Assert.AreEqual(
                            version.siblingHandle,
                            command.sourceSiblingHandle.Equals(GameItemHandle.Empty) ? -1 : command.sourceSiblingHandle.index);

                        if (version.siblingHandle != -1)
                            Remove(
                                ref durabilityIndex,
                                ref timeIndex,
                                commandIndex, 
                                version.siblingHandle,
                                -1,
                                -1,
                                -1,
                                false,
                                out _);

                        if (command.destinationSiblingHandle.Equals(GameItemHandle.Empty))
                            version.siblingHandle = -1;
                        else
                        {
                            Append(
                                ref durabilityIndex,
                                ref timeIndex,
                                commandIndex,
                                commandStartIndex,
                                commandCount,
                                -1,
                                -1,
                                command.sourceSiblingHandle.Equals(GameItemHandle.Empty) ? -1 : command.sourceSiblingHandle.index,
                                version.entity, 
                                command.destinationSiblingHandle, 
                                ref results);

                            version.siblingHandle = command.destinationSiblingHandle.index;
                        }

                        versions[command.destinationHandle.index] = version;
                    }
                    break;
                case GameItemCommandType.Move:
                    if(command.sourceHandle.Equals(moveOrginHandle))
                        moveResult = false;
                    //else if (GetRoot(command.sourceParentHandle, out entity))
                    else if (!command.sourceParentHandle.Equals(GameItemHandle.Empty) && 
                        versions.TryGetValue(command.sourceParentHandle.index, out version))
                    {
                        UnityEngine.Assertions.Assert.AreEqual(version.value, command.sourceParentHandle.version);

                        commandIndexToMask = commandIndex + commandStartIndex;
                        for (i = 0; i < numItemMasks; ++i)
                        {
                            ref var itemMask = ref itemMasks.ElementAt(i);
                            if (itemMask.Check(commandIndexToMask) && itemMask.handle.Equals(command.sourceParentHandle))
                                break;
                        }

                        if (i == numItemMasks)
                            Destroy(
                                ref durabilityIndex,
                                ref timeIndex,
                                commandIndex, 
                                command);
                        /*{
                            var result = RemoveResult(
                                ref durabilityIndex,
                                ref timeIndex,
                                commandIndex,
                                command.sourceParentChildIndex,
                                command.sourceParentHandle.Equals(GameItemHandle.Empty) ? -1 : command.sourceParentHandle.index,
                                command.destinationParentChildIndex,
                                command.destinationParentHandle.Equals(GameItemHandle.Empty) ? -1 : command.destinationParentHandle.index,
                                -1,
                                command.type,
                                command.sourceCount,
                                version.entity,
                                command.sourceHandle);

                            this.results.Add(result);
                        }*/
                    }

                    int nextCommandIndex = commandIndex + 1;
                    if (nextCommandIndex < oldCommands.Length)
                    {
                        var temp = oldCommands[nextCommandIndex];
                        if (temp.commandType == GameItemCommandType.Move &&
                            temp.sourceParentChildIndex == command.destinationParentChildIndex &&
                            temp.sourceParentHandle.Equals(command.destinationParentHandle))
                        {
                            bool nextMoveResult = Execute(
                                ref durabilityIndex,
                                ref timeIndex,
                                ref nextCommandIndex,
                                commandStartIndex, 
                                commandCount, 
                                temp, 
                                command.sourceHandle, 
                                ref results);

                            commandIndex = nextCommandIndex;

                            if (!nextMoveResult)
                                break;
                        }
                    }

                    Apply(
                        command, 
                        commandIndex, 
                        commandStartIndex, 
                        commandCount, 
                        ref durabilityIndex, 
                        ref timeIndex, 
                        ref results);

                    break;
                case GameItemCommandType.Remove:
                    if (!versions.TryGetValue(command.sourceHandle.index, out version))
                        break;

                    UnityEngine.Assertions.Assert.AreEqual(version.value, command.sourceHandle.version);
                    UnityEngine.Assertions.Assert.AreEqual(
                        version.parentHandle,
                        command.sourceParentHandle.Equals(GameItemHandle.Empty) ? -1 : command.sourceParentHandle.index);
                    UnityEngine.Assertions.Assert.AreEqual(
                        version.siblingHandle,
                        command.sourceSiblingHandle.Equals(GameItemHandle.Empty) ? -1 : command.sourceSiblingHandle.index);

                    commandIndexToMask = commandIndex + commandStartIndex;
                    for (i = 0; i < numItemMasks; ++i)
                    {
                        ref readonly var itemMask = ref itemMasks.ElementAt(i);
                        if (itemMask.Check(commandIndexToMask) && itemMask.handle.Equals(command.sourceParentHandle))
                            break;
                    }

                    if (i == numItemMasks)
                    {
                        var result = RemoveResult(
                            true, 
                            ref durabilityIndex,
                            ref timeIndex,
                            commandIndex,
                            command.sourceParentChildIndex,
                            command.sourceParentHandle.Equals(GameItemHandle.Empty) ? -1 : command.sourceParentHandle.index,
                            command.sourceSiblingHandle.Equals(GameItemHandle.Empty) ? -1 : command.sourceSiblingHandle.index,
                            command.destinationParentChildIndex,
                            command.destinationParentHandle.Equals(GameItemHandle.Empty) ? -1 : command.destinationParentHandle.index,
                            command.destinationSiblingHandle.Equals(GameItemHandle.Empty) ? -1 : command.destinationSiblingHandle.index,
                            command.type,
                            command.count,
                            version.entity,
                            command.sourceHandle);

                        this.results.Add(result);

                        version.count -= command.count;
                        //version.durability -= result.value.durability;
                        //version.time -= result.value.time;

                        versions[command.sourceHandle.index] = version;
                    }
                    break;
                case GameItemCommandType.Destroy:
                    Destroy(
                        ref durabilityIndex, 
                        ref timeIndex, 
                        commandIndex, 
                        command);
                    break;
            }

            return moveResult;
        }

        public void Execute()
        {
            UnsafeList<EntityData<Result>> results = default;
            int startCommandIndex = this.commandCount[0],
                oldCommandCount = oldCommands.Length,
                commandCount = oldCommandCount + commands.Length, 
                timeIndex = 0,
                durabilityIndex = 0;
            for (int i = 0; i < oldCommandCount; ++i)
                Execute(
                    ref durabilityIndex, 
                    ref timeIndex, 
                    ref i,
                    startCommandIndex, 
                    commandCount, 
                    oldCommands[i], 
                    GameItemHandle.Empty, 
                    ref results);

            if (results.IsCreated)
            {
                this.results.AddRange(ZG.Unsafe.CollectionUtility.AsArray(ref results));

                results.Dispose();
            }

            this.commandCount[0] = startCommandIndex + oldCommandCount;
        }

        public bool __Remove(
            ref int durabilityIndex,
            ref int timeIndex,
            int commandIndex,
            int handle,
            int parentChildIndex,
            int parentHandle,
            int siblingHandle,
            bool isReadOnly,
            out Version version)
        {
            if (versions.TryGetValue(handle, out version))
            {
                if (children.TryGetFirstValue(handle, out var child, out var iterator))
                {
                    do
                    {
                        __Remove(
                            ref durabilityIndex, 
                            ref timeIndex, 
                            commandIndex, 
                            child.handle,
                            -1,
                            -1,
                            -1,
                            isReadOnly,
                            out _);
                    } while (children.TryGetNextValue(out child, ref iterator));

                    if (!isReadOnly)
                        children.Remove(handle);
                }

                __Remove(
                    ref durabilityIndex,
                    ref timeIndex,
                    commandIndex,
                    version.siblingHandle,
                    -1,
                    -1,
                    -1,
                    isReadOnly,
                    out _);

                GameItemHandle itemHandle;
                itemHandle.index = handle;
                itemHandle.version = version.value;

                var result = RemoveResult(
                    true, 
                    ref durabilityIndex, 
                    ref timeIndex, 
                    commandIndex,
                    version.parentChildIndex,
                    version.parentHandle,
                    version.siblingHandle,
                    parentChildIndex,
                    parentHandle,
                    siblingHandle, 
                    version.type, 
                    version.count, 
                    version.entity,
                    itemHandle);

                results.Add(result);

                if (!isReadOnly)
                    versions.Remove(handle);

                return true;
            }

            return false;
        }

        public bool __Append(
            ref int durabilityIndex,
            ref int timeIndex,
            int commandIndex,
            int commandStartIndex, 
            int commandCount, 
            int sourceParentChildIndex,
            int sourceParentHandle,
            int sourceSiblingHandle,
            in Entity entity,
            in GameItemHandle handle,
            ref UnsafeList<EntityData<Result>> results)
        {
            if (!versions.ContainsKey(handle.index) &&
                hierarchy.GetChildren(handle, out var enumerator, out var item))
            {
                int siblingHandle = __Append(
                    ref durabilityIndex,
                    ref timeIndex,
                    commandIndex,
                    commandStartIndex,
                    commandCount, 
                    -1,
                    -1,
                    -1,
                    entity,
                    item.siblingHandle,
                    ref results) || !item.siblingHandle.Equals(GameItemHandle.Empty) ? item.siblingHandle.index : -1;

                int parentHandle;
                if (item.parentHandle.Equals(GameItemHandle.Empty))
                    parentHandle = -1;
                else
                    parentHandle = item.parentHandle.index;

                ItemMask itemMask;
                itemMask.commandStartIndex = commandStartIndex + commandIndex;
                itemMask.commandCount = commandCount - commandIndex;
                itemMask.handle = handle;

                itemMasks.Add(itemMask);

                var result = AddResult(
                    false, 
                    ref durabilityIndex,
                    ref timeIndex,
                    commandIndex,
                    sourceParentChildIndex,
                    sourceParentHandle,
                    sourceSiblingHandle,
                    item.parentChildIndex,
                    parentHandle,
                    siblingHandle,
                    item.type,
                    item.count,
                    entity,
                    handle);

                if (!results.IsCreated)
                    results = new UnsafeList<EntityData<Result>>(1, Allocator.Temp, NativeArrayOptions.UninitializedMemory);

                results.Add(result);

                Version version;
                version.value = handle.version;
                version.type = item.type;
                version.count = item.count;
                version.parentChildIndex = item.parentChildIndex;
                version.parentHandle = parentHandle;
                version.siblingHandle = siblingHandle;
                //version.durability = result.value.durability;
                //version.time = result.value.time;
                version.entity = entity;
                versions.Add(handle.index, version);

                GameItemChild source;
                while (enumerator.MoveNext())
                {
                    source = enumerator.Current;

                    __Append(
                        ref durabilityIndex,
                        ref timeIndex,
                        commandIndex, 
                        commandStartIndex,
                        commandCount,
                        -1,
                        -1,
                        -1,
                        entity,
                        source.handle,
                        ref results);
                }

                if (parentHandle != -1)
                {
                    ItemChild destination;
                    destination.index = item.parentChildIndex;
                    destination.handle = handle.index;
                    children.Add(parentHandle, destination);
                }

                return true;
            }

            return false;
        }
    }

    [BurstCompile]
    private struct Clear : IJob
    {
        [ReadOnly, DeallocateOnJobCompletion]
        public NativeArray<int> count;

        public NativeList<Entity> entities;

        public void Execute()
        {
            entities.Clear();
            entities.Capacity = math.max(entities.Capacity, count[0]);
        }
    }

    private struct Reset
    {
        [ReadOnly]
        public GameItemManager.Hierarchy hierarchy;

        [ReadOnly]
        public NativeArray<Entity> entityArray;

        [ReadOnly]
        public NativeArray<GameItemRoot> roots;

        [ReadOnly]
        public SharedHashMap<Entity, Entity>.Reader itemEntities;

        [ReadOnly]
        public NativeParallelHashMap<int, Version> versions;

        public NativeList<Entity>.ParallelWriter entities;

        public NativeCounter.Concurrent counter;

        public void Execute(int index)
        {
            var root = roots[index].handle;
            if (root.Equals(GameItemHandle.Empty))
                return;

            if (!itemEntities.ContainsKey(GameItemStructChangeFactory.Convert(root)))
                return;

            if (versions.TryGetValue(root.index, out var version) && version.value == root.version)
                return;

            entities.AddNoResize(entityArray[index]);

            counter.Add(hierarchy.CountOf(root));
        }
    }

    [BurstCompile]
    private struct ResetEx : IJobChunk
    {
        [ReadOnly]
        public GameItemManager.Hierarchy hierarchy;

        [ReadOnly]
        public SharedHashMap<Entity, Entity>.Reader itemEntities;

        [ReadOnly]
        public NativeParallelHashMap<int, Version> versions;

        [ReadOnly]
        public EntityTypeHandle entityType;

        [ReadOnly]
        public ComponentTypeHandle<GameItemRoot> rootType;

        public NativeList<Entity>.ParallelWriter entities;

        public NativeCounter.Concurrent counter;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Reset reset;
            reset.hierarchy = hierarchy;
            reset.itemEntities = itemEntities;
            reset.versions = versions;
            reset.entityArray = chunk.GetNativeArray(entityType);
            reset.roots = chunk.GetNativeArray(ref rootType);
            reset.entities = entities;
            reset.counter = counter;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                reset.Execute(i);
        }
    }

    [BurstCompile]
    private struct Resize : IJob
    {
        [ReadOnly]
        public GameItemManager.Hierarchy hierarchy;

        [ReadOnly]
        public SharedHashMap<Entity, Entity>.Reader entities;

        [ReadOnly]
        public ComponentLookup<GameItemDurability> durabilities;

        [ReadOnly]
        public ComponentLookup<GameItemTime> times;

        [ReadOnly]
        public ComponentLookup<GameItemRoot> roots;

        [ReadOnly]
        public NativeArray<Entity> entityArray;

        [ReadOnly]
        public NativeArray<int> commandCount;

        public NativeCounter counter;

        public NativeList<ItemMask> itemMasks;

        public NativeList<EntityData<Result>> results;

        public NativeParallelHashMap<int, Version> versions;

        public NativeParallelMultiHashMap<int, ItemChild> children;

        public bool GetValue(in GameItemHandle handle, out float durability, out float time)
        {
            if (!entities.TryGetValue(GameItemStructChangeFactory.Convert(handle), out Entity entity))
            {
                durability = 0.0f;
                time = 0.0f;

                return false;
            }

            durability = durabilities.HasComponent(entity) ? durabilities[entity].value : default;
            time = times.HasComponent(entity) ? times[entity].value : default;

            return true;
        }

        public bool Remove(in GameItemHandle handle)
        {
            if (hierarchy.GetChildren(handle, out var enumerator, out var item))
            {
                while(enumerator.MoveNext())
                {
                    Remove(enumerator.Current.handle);
                }

                children.Remove(handle.index);

                Remove(item.siblingHandle);

                if (versions.TryGetValue(handle.index, out var version))
                {
                    EntityData<Result> result;
                    result.value.resultType = ResultType.Remove;
                    result.value.sourceParentChildIndex = version.parentChildIndex;
                    result.value.sourceParentHandle = version.parentHandle;
                    result.value.sourceSiblingHandle = version.siblingHandle;
                    result.value.destinationParentChildIndex = -1;
                    result.value.destinationParentHandle = -1;
                    result.value.destinationSiblingHandle = -1;
                    result.value.handle = handle.index;
                    result.value.type = version.type;
                    result.value.count = version.count;

                    GetValue(handle, out result.value.durability, out result.value.time);

                    result.entity = version.entity;
                    results.Add(result);

                    versions.Remove(handle.index);

                    return true;
                }
            }

            return false;
        }

        public void Execute()
        {
            int numEntities = entityArray.Length;
            for(int i = 0; i < numEntities; ++i)
                Remove(roots[entityArray[i]].handle);

            int count = counter.count, commandCount = this.commandCount[0], numItemMasks = itemMasks.Length;
            for(int i = 0; i < numItemMasks; ++i)
            {
                ref readonly var itemMask = ref itemMasks.ElementAt(i);
                if(itemMask.commandStartIndex + itemMask.commandCount <= commandCount)
                {
                    itemMasks.RemoveAtSwapBack(i--);

                    --numItemMasks;
                }
            }

            itemMasks.Capacity = math.max(itemMasks.Capacity, itemMasks.Length + count);

            results.Capacity = math.max(results.Capacity, results.Length + count);

            versions.Capacity = math.max(versions.Capacity, versions.Count() + count);
            children.Capacity = math.max(children.Capacity, children.Count() + count);

            counter.count = 0;
        }
    }

    [BurstCompile]
    private struct Apply : IJobParallelForDefer
    {
        [ReadOnly]
        public GameItemManager.Hierarchy hierarchy;

        [ReadOnly]
        public NativeArray<Entity> entityArray;

        [ReadOnly]
        public NativeArray<int> commandCount;

        [ReadOnly]
        public NativeArray<GameItemCommand> commands;

        [ReadOnly]
        public ComponentLookup<GameItemRoot> roots;

        [ReadOnly]
        public ComponentLookup<GameItemDurability> durabilities;

        [ReadOnly]
        public ComponentLookup<GameItemTime> times;

        [ReadOnly]
        public SharedHashMap<Entity, Entity>.Reader entities;

        public NativeList<ItemMask>.ParallelWriter itemMasks;

        public NativeList<EntityData<Result>>.ParallelWriter results;

        public NativeParallelHashMap<int, Version>.ParallelWriter versions;

        public NativeParallelMultiHashMap<int, ItemChild>.ParallelWriter children;

        public bool GetValue(in GameItemHandle handle, out float durability, out float time)
        {
            if (!entities.TryGetValue(GameItemStructChangeFactory.Convert(handle), out Entity entity))
            {
                durability = 0.0f;
                time = 0.0f;

                return false;
            }

            durability = durabilities.HasComponent(entity) ? durabilities[entity].value : default;
            time = times.HasComponent(entity) ? times[entity].value : default;

            return true;
        }

        public bool Execute(
            int commandStartIndex, 
            int commandCount, 
            in Entity entity, 
            in GameItemHandle handle)
        {
            if (hierarchy.GetChildren(handle, out var enumerator, out var item))
            {
                Execute(
                    commandStartIndex, 
                    commandCount, 
                    entity, 
                    item.siblingHandle);

                GetValue(handle, out float durability, out float time);

                Version version;
                version.value = handle.version;
                version.type = item.type;
                version.count = item.count;
                version.parentChildIndex = item.parentChildIndex;
                version.parentHandle = item.parentHandle.Equals(GameItemHandle.Empty) ? -1 : item.parentHandle.index;
                version.siblingHandle = item.siblingHandle.Equals(GameItemHandle.Empty) ? -1 : item.siblingHandle.index;
                //version.durability = durability;
                //version.time = time;

                version.entity = entity;
                if (versions.TryAdd(handle.index, version))
                {
                    ItemMask itemMask;
                    itemMask.commandStartIndex = commandStartIndex;
                    itemMask.commandCount = commands.Length;
                    itemMask.handle = handle;
                    itemMasks.AddNoResize(itemMask);

                    EntityData<Result> result;
                    result.value.resultType = ResultType.Add;

                    result.value.sourceParentChildIndex = -1;
                    result.value.sourceParentHandle = -1;
                    result.value.sourceSiblingHandle = -1;

                    result.value.destinationParentChildIndex = item.parentChildIndex;
                    result.value.destinationParentHandle = item.parentHandle.Equals(GameItemHandle.Empty) ? -1 : item.parentHandle.index;
                    result.value.destinationSiblingHandle = item.siblingHandle.Equals(GameItemHandle.Empty) ? -1 : item.siblingHandle.index;

                    result.value.handle = handle.index;

                    result.value.type = item.type;
                    result.value.count = item.count;

                    result.value.durability = durability;
                    result.value.time = time;

                    result.entity = entity;

                    results.AddNoResize(result);

                    GameItemChild source;
                    ItemChild destination;
                    while (enumerator.MoveNext())
                    {
                        source = enumerator.Current;

                        if (Execute(
                            commandStartIndex, 
                            commandCount, 
                            entity, 
                            source.handle))
                        {
                            destination.index = source.index;
                            destination.handle = source.handle.index;
                            children.Add(handle.index, destination);
                        }
                    }

                    return true;
                }
            }

            return false;
        }

        public void Execute(int index)
        {
            Entity entity = entityArray[index];

            Execute(
                commandCount[0], 
                commands.Length, 
                entity, 
                roots[entity].handle);
        }
    }

    private EntityQuery __structChangeManagerGroup;
    private EntityQuery __group;

    private GameItemManagerShared __itemManager;
    private SharedHashMap<GameItemHandle, Entity> __itemRootEntities;
    private SharedList<GameItemChangeResult<GameItemTime>> __timeResults;
    private SharedList<GameItemChangeResult<GameItemDurability>> __durabilityResults;

    private NativeCounter __counter;
    private NativeArray<int> __commandCount;
    private NativeList<ItemMask> __itemMasks;
    private NativeList<Entity> __entities;
    private NativeParallelHashMap<int, Version> __versions;
    private NativeParallelMultiHashMap<int, ItemChild> __children;

    private GameItemCommandSystem __itemCommandSystem;

    public NativeList<EntityData<Result>> results
    {
        get;

        private set;
    }

    public static TValue GetComponentValue<TValue, TMap>(
        ComponentValueType type,
        int commandIndex,
        ref int valueIndex,
        in GameItemHandle handle, 
        in SharedList<GameItemChangeResult<TValue>>.Reader results,
        in SharedHashMap<Entity, Entity>.Reader entities, 
        in TMap values) 
        where TValue : unmanaged, IGameItemComponentData<TValue> 
        where TMap : struct, IComponentMap<TValue>
    {
        GameItemChangeResult<TValue> result;
        int originValueIndex = valueIndex, numResults = results.length;
        while (valueIndex < numResults)
        {
            result = results[valueIndex];
            if (result.index > commandIndex)
                break;

            ++valueIndex;

            if (result.index == commandIndex)
            {
                //commandIndex - 1
                //UnityEngine.Assertions.Assert.AreEqual(handle, result.handle);

                return type == ComponentValueType.Override ? result.value : result.diff;
            }
        }

        for (int i = originValueIndex - 1; i >= 0; --i)
        {
            result = results[i];
            if (result.handle.Equals(handle))
                return type == ComponentValueType.Override ? result.value : result.diff;
        }

        if (type == ComponentValueType.Add)
            return default;

        for(int i = valueIndex; i < numResults; ++i)
        {
            result = results[i];
            if (result.handle.Equals(handle))
                return result.orgin;
        }

        //当有time而没有durability的时候
        if (!entities.TryGetValue(GameItemStructChangeFactory.Convert(handle), out Entity entity))
            return default;

        return values[entity];
    }

    public bool TryGetVersion(int handle, out int version)
    {
        CompleteReadOnlyDependency();

        if (__versions.TryGetValue(handle, out var temp))
        {
            version = temp.value;

            return true;
        }

        version = 0;

        return false;
    }

    public bool TryGetRoot(int handle, out Entity entity)
    {
        /*if (!TryGetVersion(handle, out int version))
        {
            entity = Entity.Null;
            return false;
        }

        GameItemHandle key;
        key.index = handle;
        key.version = version;

        __itemManager.lookupJobManager.CompleteReadOnlyDependency();

        key = __itemManager.value.GetRoot(key);

        __itemRootEntities.lookupJobManager.CompleteReadOnlyDependency();

        return __itemRootEntities.reader.TryGetValue(key, out entity);*/
        CompleteReadOnlyDependency();

        if (__versions.TryGetValue(handle, out var temp))
        {
            entity = temp.entity;

            return true;
        }

        entity = Entity.Null;

        return false;
    }

    public bool TryGetEntity(int handle, out Entity entity, out int version)
    {
        if (!TryGetVersion(handle, out version))
        {
            entity = Entity.Null;
            return false;
        }

        GameItemHandle key;
        key.index = handle;
        key.version = version;

        var itemEntities = __structChangeManagerGroup.GetSingleton<GameItemStructChangeManager>().handleEntities;
        itemEntities.lookupJobManager.CompleteReadOnlyDependency();

        return itemEntities.reader.TryGetValue(GameItemStructChangeFactory.Convert(key), out entity);
    }

    public bool TryGetValue(int handle, out int version, out int type, out float durability, out float time)
    {
        type = -1;
        durability = 0.0f;
        time = 0.0f;
        if (!TryGetEntity(handle, out Entity entity, out version))
            return false;

        var entityManager = EntityManager;
        type = entityManager.GetComponentData<GameItemType>(entity).value;

        if (entityManager.HasComponent<GameItemDurability>(entity))
            durability = entityManager.GetComponentData<GameItemDurability>(entity).value;

        if (entityManager.HasComponent<GameItemTime>(entity))
            time = entityManager.GetComponentData<GameItemTime>(entity).value;

        return true;
    }

    public bool TryGetDurability(int handle, out float value, out int version)
    {
        value = 0.0f;
        if (!TryGetEntity(handle, out Entity entity, out version))
            return false;

        var entityManager = EntityManager;
        if (!entityManager.HasComponent<GameItemDurability>(entity))
            return false;

        value = entityManager.GetComponentData<GameItemDurability>(entity).value;

        return true;
    }

    public bool SetTime(int handle, float value, out int version)
    {
        if (!TryGetEntity(handle, out Entity entity, out version))
            return false;

        var entityManager = EntityManager;
        if (!entityManager.HasComponent<GameItemTime>(entity))
            return false;

        GameItemTime time;
        time.value = value;
        entityManager.SetComponentData(entity, time);

        return true;
    }

    public bool SetDurability(int handle, float value, out int version)
    {
        if (!TryGetEntity(handle, out Entity entity, out version))
            return false;

        var entityManager = EntityManager;
        if (!entityManager.HasComponent<GameItemDurability>(entity))
            return false;

        GameItemDurability durability;
        durability.value = value;
        entityManager.SetComponentData(entity, durability);

        return true;
    }

    public bool SetDurability(int handle, ref float destination, out int version)
    {
        if (!TryGetEntity(handle, out Entity entity, out version))
            return false;

        var entityManager = EntityManager;
        if (!entityManager.HasComponent<GameItemDurability>(entity))
            return false;

        float source = entityManager.GetComponentData<GameItemDurability>(entity).value;

        GameItemDurability durability;
        durability.value = destination;
        entityManager.SetComponentData(entity, durability);

        destination = source;

        return true;
    }

    public bool Move(int handle, int parentHandle, int parentChildIndex)
    {
        CompleteReadOnlyDependency();

        GameItemCommandSystem.Command command;

        if (!__versions.TryGetValue(handle, out var version))
            return false;

        command.handle.version = version.value;

        if (!__versions.TryGetValue(parentHandle, out version))
            return false;

        command.parentHandle.version = version.value;

        command.type = GameItemCommandSystem.CommandType.Move;
        command.handle.index = handle;
        command.parentHandle.index = parentHandle;
        command.parentChildIndex = parentChildIndex;

        __itemCommandSystem.commands.Add(command);

        return true;
    }

    public bool Split(int handle, int parentHandle, int parentChildIndex)
    {
        CompleteReadOnlyDependency();

        GameItemCommandSystem.Command command;

        if (!__versions.TryGetValue(handle, out var version))
            return false;

        command.handle.version = version.value;

        if (parentHandle == -1)
            command.parentHandle = GameItemHandle.Empty;
        else
        {
            if (!__versions.TryGetValue(parentHandle, out version))
                return false;

            command.parentHandle.version = version.value;
            command.parentHandle.index = parentHandle;
        }

        command.parentChildIndex = parentChildIndex;
        command.handle.index = handle;
        command.type = GameItemCommandSystem.CommandType.Split;

        __itemCommandSystem.commands.Add(command);

        return true;
    }

    public bool Convert(int handle, Action<int, GameItem> handler)
    {
        CompleteReadOnlyDependency();

        __durabilityResults.lookupJobManager.CompleteReadOnlyDependency();
        __timeResults.lookupJobManager.CompleteReadOnlyDependency();

        var itemEntities = __structChangeManagerGroup.GetSingleton<GameItemStructChangeManager>().handleEntities;

        itemEntities.lookupJobManager.CompleteReadOnlyDependency();

        int durabilityIndex = 0, timeIndex = 0;
        var entityManager = EntityManager;
        return __Convert(
            handle, 
            ref durabilityIndex, 
            ref timeIndex, 
            new ComponentManager<GameItemDurability>(entityManager),
            new ComponentManager<GameItemTime>(entityManager),
            itemEntities.reader, 
            handler);
    }

    protected override void OnCreate()
    {
        base.OnCreate();

        __structChangeManagerGroup = GameItemStructChangeManager.GetEntityQuery(ref this.GetState());

        __group = GetEntityQuery(
            new EntityQueryDesc()
            {
                All = new ComponentType[]
                {
                    ComponentType.ReadOnly<GameItemRoot>(),
                    ComponentType.ReadOnly<EntityDataSerializable>()
                },
                Options = EntityQueryOptions.IncludeDisabledEntities
            });
        __group.SetChangedVersionFilter(new ComponentType[] { typeof(GameItemRoot), typeof(EntityDataSerializable) });

        World world = World;
        __itemManager = world.GetOrCreateSystemUnmanaged<GameItemSystem>().manager;
        __itemRootEntities = world.GetOrCreateSystemManaged<GameItemRootEntitySystem>().entities;
        __timeResults = world.GetOrCreateSystemUnmanaged<GameItemTimeChangeSystem>().resutls;
        __durabilityResults = world.GetOrCreateSystemUnmanaged<GameItemDurabilityChangeSystem>().results;

        __itemCommandSystem = world.GetOrCreateSystemManaged<GameItemCommandSystem>();

        __counter = new NativeCounter(Allocator.Persistent);
        __commandCount = new NativeArray<int>(1, Allocator.Persistent, NativeArrayOptions.ClearMemory);
        __itemMasks = new NativeList<ItemMask>(Allocator.Persistent);
        __entities = new NativeList<Entity>(Allocator.Persistent);
        __versions = new NativeParallelHashMap<int, Version>(1, Allocator.Persistent);
        __children = new NativeParallelMultiHashMap<int, ItemChild>(1, Allocator.Persistent);

        results = new NativeList<EntityData<Result>>(Allocator.Persistent);
    }

    protected override void OnDestroy()
    {
        __counter.Dispose();
        __commandCount.Dispose();
        __itemMasks.Dispose();
        __entities.Dispose();
        __versions.Dispose();
        __children.Dispose();

        results.Dispose();

        base.OnDestroy();
    }

    protected override void _Update()
    {
        if (!__itemManager.isCreated)
            return;

        var inputDeps = Dependency;

        ref var itemJobManager = ref __itemManager.lookupJobManager;

        var itemEntities = __structChangeManagerGroup.GetSingleton<GameItemStructChangeManager>().handleEntities;
        ref var entityJobManager = ref itemEntities.lookupJobManager;

        var itemEntitiesReader = itemEntities.reader;

        var durabilities = GetComponentLookup<GameItemDurability>(true);
        var times = GetComponentLookup<GameItemTime>(true);

        Change change;
        change.hierarchy = __itemManager.value.hierarchy;
        change.commandCount = __commandCount;
        change.oldCommands = __itemManager.oldCommands;
        change.commands = __itemManager.commands;
        change.entities = itemEntitiesReader;
        change.rootEntities = __itemRootEntities.reader;
        change.durabilityResults = __durabilityResults.reader;
        change.timeResults = __timeResults.reader;
        change.durabilities = durabilities;
        change.times = times;
        change.serializables = GetComponentLookup<EntityDataSerializable>(true);
        change.itemMasks = __itemMasks;
        change.results = results;
        change.versions = __versions;
        change.children = __children;

        ref var rootEntityJobManager = ref __itemRootEntities.lookupJobManager;
        ref var timeResultJobManager = ref __timeResults.lookupJobManager;
        ref var durabilityJobManager = ref __durabilityResults.lookupJobManager;

        var jobHandle = JobHandle.CombineDependencies(itemJobManager.readOnlyJobHandle, entityJobManager.readOnlyJobHandle, rootEntityJobManager.readOnlyJobHandle);
        jobHandle = JobHandle.CombineDependencies(jobHandle, timeResultJobManager.readOnlyJobHandle, durabilityJobManager.readOnlyJobHandle);
        jobHandle = change.Schedule(JobHandle.CombineDependencies(jobHandle, inputDeps));

        rootEntityJobManager.AddReadOnlyDependency(jobHandle);
        timeResultJobManager.AddReadOnlyDependency(jobHandle);
        durabilityJobManager.AddReadOnlyDependency(jobHandle);

        var count = new NativeArray<int>(1, Allocator.TempJob, NativeArrayOptions.ClearMemory);
        var temp = __group.CalculateEntityCountAsync(count, inputDeps);

        Clear clear;
        clear.count = count;
        clear.entities = __entities;
        temp = clear.Schedule(temp);

        var hierarchy = __itemManager.hierarchy;

        ResetEx reset;
        reset.hierarchy = hierarchy;
        reset.itemEntities = itemEntitiesReader;
        reset.versions = __versions;
        reset.entityType = GetEntityTypeHandle();
        reset.rootType = GetComponentTypeHandle<GameItemRoot>(true);
        reset.entities = __entities.AsParallelWriter();
        reset.counter = __counter;
        jobHandle = reset.ScheduleParallel(__group, JobHandle.CombineDependencies(temp, jobHandle));

        var roots = GetComponentLookup<GameItemRoot>(true);
        var entities = __entities.AsDeferredJobArray();

        Resize resize;
        resize.hierarchy = hierarchy;
        resize.entities = itemEntitiesReader;
        resize.durabilities = durabilities;
        resize.times = times;
        resize.roots = roots;
        resize.entityArray = entities;
        resize.commandCount = __commandCount;
        resize.counter = __counter;
        resize.itemMasks = __itemMasks;
        resize.results = results;
        resize.versions = __versions;
        resize.children = __children;
        jobHandle = resize.Schedule(jobHandle);

        Apply apply;
        apply.hierarchy = hierarchy;
        apply.entityArray = entities;
        apply.commandCount = __commandCount;
        apply.commands = __itemManager.commands;
        apply.roots = GetComponentLookup<GameItemRoot>(true);
        apply.durabilities = durabilities;
        apply.times = times;
        apply.entities = itemEntitiesReader;
        apply.results = results.AsParallelWriter();
        apply.itemMasks = __itemMasks.AsParallelWriter();
        apply.versions = __versions.AsParallelWriter();
        apply.children = __children.AsParallelWriter();
        jobHandle = apply.Schedule(__entities, 1, jobHandle);

        itemJobManager.AddReadOnlyDependency(jobHandle);
        entityJobManager.AddReadOnlyDependency(jobHandle);

        Dependency = jobHandle;
    }

    private bool __Convert(
        int handle, 
        ref int durabilityIndex, 
        ref int timeIndex, 
        in ComponentManager<GameItemDurability> durabilities,
        in ComponentManager<GameItemTime> times,
        in SharedHashMap<Entity, Entity>.Reader itemEntities, 
        Action<int, GameItem> handler)
    {
        if (!__versions.TryGetValue(handle, out var version))
            return false;

        __Convert(
            version.siblingHandle,
            ref durabilityIndex,
            ref timeIndex,
            durabilities,
            times,
            itemEntities, 
            handler);

        GameItemHandle itemHandle;
        itemHandle.index = handle;
        itemHandle.version = version.value;

        GameItem destination;
        destination.parentHandle = version.parentHandle;
        destination.parentChildIndex = (byte)version.parentChildIndex;
        destination.count = (byte)version.count;
        destination.itemIndex = (short)version.type;
        destination.durability = (short)math.round(GetComponentValue(ComponentValueType.Override, -1, ref durabilityIndex, itemHandle, __durabilityResults.reader, itemEntities, durabilities).value);// (short)math.round(version.durability);
        destination.time = GetComponentValue(ComponentValueType.Override, -1, ref timeIndex, itemHandle, __timeResults.reader, itemEntities, times).value;

        handler(handle, destination);

        if (__children.TryGetFirstValue(handle, out var child, out var iterator))
        {
            do
            {
                __Convert(
                    child.handle,
                    ref durabilityIndex,
                    ref timeIndex,
                    durabilities,
                    times,
                    itemEntities, 
                    handler);

            } while (__children.TryGetNextValue(out child, ref iterator));
        }

        /*int capacity = __itemManager.value.GetType(version.type).capacity;
        if (capacity > 0)
        {
            destination.childHandles = new int[capacity];
            for (int i = 0; i < capacity; ++i)
                destination.childHandles[i] = -1;

            if(__children.TryGetFirstValue(handle, out var child, out var iterator))
            {
                do
                {

                    if (Convert(child.handle, handler))
                        destination.childHandles[child.index] = child.handle;
                } while (__children.TryGetNextValue(out child, ref iterator));
            }
        }
        else
            destination.childHandles = null;*/

        return true;
    }

}