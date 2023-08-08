using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Jobs;
using Unity.Entities;
using Unity.Collections;
using ZG;
using MoveType = GameItemMoveType;
using CommandType = GameItemCommandType;
using Command = GameItemCommand;
using Handle = GameItemHandle;
using Info = GameItemInfo;
using Child = GameItemChild;
using Type = GameItemTypeDefinition;
using Data = GameItemDataDefinition;
using Unity.Burst;

[Flags]
public enum GameItemFindFlag
{
    Self = 0x01, 
    Siblings = 0x02, 
    Children = 0x04
}

public enum GameItemMoveType
{
    Normal,
    Reverse,
    All,
    Error
}

public enum GameItemCommandType
{
    Create,
    Add,
    Connect,
    Move,
    Remove,
    Destroy
}

public struct GameItemCommand
{
    public CommandType commandType;
    public int type;
    public int count;
    public int sourceCount;
    public int destinationCount;
    public int sourceParentChildIndex;
    public int destinationParentChildIndex;
    public Handle sourceParentHandle;
    public Handle destinationParentHandle;
    public Handle sourceSiblingHandle;
    public Handle destinationSiblingHandle;
    public Handle sourceHandle;
    public Handle destinationHandle;
}

public struct GameItemHandle : IEquatable<Handle>
{
    public int index;
    public int version;

    public static readonly Handle Empty = default;

    public bool Equals(Handle other)
    {
        return index == other.index && version == other.version;
    }

    public override int GetHashCode()
    {
        return index;
    }

    public override string ToString()
    {
        return $"GameItemHandle(Index: {index}, Version: {version})";
    }
}

public struct GameItemInfo
{
    public int version;
    public int type;
    public int count;

    public int parentChildIndex;
    public Handle parentHandle;

    public Handle siblingHandle;
}

public struct GameItemChild
{
    public int index;
    public Handle handle;
}

public struct GameItemTypeDefinition
{
    public int count;
    public int capacity;
}

[Serializable]
public struct GameItemDataDefinition
{
    public int count;
    public int capacity;

    public bool isInvert;
    public int[] filters;
    public int[] fungibles;
}

public struct GameItemManager
{
    public enum FindResult
    {
        None,
        Empty, 
        Normal
    }

    public struct ReadOnlyInfos
    {
        [ReadOnly]
        private NativePool<Info>.ReadOnlySlice __values;

        public ReadOnlyInfos(NativePool<Info>.ReadOnlySlice values)
        {
            __values = values;
        }

        public Handle GetRoot(in Handle handle)
        {
            if (__values.TryGetValue(handle, out var item))
            {
                Handle result = GetRoot(item.parentHandle);
                if (result.Equals(Handle.Empty))
                    return handle;

                return result;
            }

            return Handle.Empty;
        }

        public bool TryGetValue(in Handle handle, out Info item) => __values.TryGetValue(handle, out item);
    }

    /*public struct Infos
    {
        private NativePool<Info>.Slice __values;

        public Infos(NativePool<Info>.Slice values)
        {
            __values = values;
        }

        public Handle GetRoot(in Handle handle)
        {
            if (__values.TryGetValue(handle, out var item))
            {
                Handle result = GetRoot(item.parentHandle);
                if (result.Equals(Handle.Empty))
                    return handle;

                return result;
            }

            return Handle.Empty;
        }

        public bool TryGetValue(in Handle handle, out Info item) => __values.TryGetValue(handle, out item);
    }*/

    public struct Hierarchy
    {
        [ReadOnly]
        private NativePool<Info>.ReadOnlySlice __infos;
        [ReadOnly]
        private NativeParallelMultiHashMap<int, Child> __children;

        public Hierarchy(
            in NativePool<Info>.ReadOnlySlice infos, 
            in NativeParallelMultiHashMap<int, Child> children)
        {
            __infos = infos;
            __children = children;
        }

        public bool TryGetValue(in Handle handle, out Info item) => __infos.TryGetValue(handle, out item);

        public int CountOf(in Handle handle)
        {
            if (!__infos.TryGetValue(handle, out var item))
                return 0;

            int length = 1;

            NativeParallelMultiHashMapIterator<int> iterator;
            if (__children.TryGetFirstValue(handle.index, out var child, out iterator))
            {
                do
                {
                    length += CountOf(child.handle);

                } while (__children.TryGetNextValue(out child, ref iterator));
            }

            length += CountOf(item.siblingHandle);

            return length;
        }

        public bool IsParentOf(in Handle parentHandle, in Handle handle)
        {
            if(__infos.TryGetValue(handle, out var item))
            {
                if (item.parentHandle.Equals(parentHandle))
                    return true;

                return IsParentOf(parentHandle, item.parentHandle);
            }

            return false;
        }

        public bool IsSiblingOrParentOf(in Handle parentOrSiblingHandle, in Handle handle)
        {
            if (__infos.TryGetValue(parentOrSiblingHandle, out var item))
            {
                if (item.siblingHandle.Equals(handle) || IsSiblingOrParentOf(item.siblingHandle, handle))
                    return true;

                if (__children.TryGetFirstValue(parentOrSiblingHandle.index, out var child, out var iterator))
                {
                    do
                    {
                        if (child.handle.Equals(handle) || IsSiblingOrParentOf(child.handle, handle))
                            return true;

                    } while (__children.TryGetNextValue(out child, ref iterator));
                }
            }

            return false;
        }

        public Handle GetRoot(in Handle handle)
        {
            if (__infos.TryGetValue(handle, out var item))
            {
                Handle result = GetRoot(item.parentHandle);
                if (result.Equals(Handle.Empty))
                    return handle;

                return result;
            }

            return Handle.Empty;
        }

        public bool GetChildren(in Handle handle, out NativeParallelMultiHashMap<int, Child>.Enumerator enumerator, out Info item)
        {
            if (!__infos.TryGetValue(handle, out item))
            {
                enumerator = default;

                return false;
            }

            enumerator = __children.GetValuesForKey(handle.index);

            return true;
        }

        /*private static bool __Contains(
            in Handle handle,
            int type,
            in NativePool<Info>.ReadOnlySlice items,
            in NativeParallelMultiHashMap<int, Child> children,
            ref int length)
        {
            if (!items.TryGetValue(handle, out var item))
                return false;

            if (item.type == type)
                length -= item.count;

            if (length <= 0)
                return true;

            if (children.TryGetFirstValue(handle.index, out var child, out var iterator))
            {
                do
                {
                    if (__Contains(child.handle, type, items, children, ref length))
                        return true;

                } while (children.TryGetNextValue(out child, ref iterator));
            }

            return false;
        }*/
    }

    public struct ReadOnly
    {
        [ReadOnly]
        private NativeArray<Type> __types;

        [ReadOnly]
        private NativePool<Info>.ReadOnlySlice __infos;
        [ReadOnly]
        private NativeParallelMultiHashMap<int, Child> __children;

        [ReadOnly]
        private NativeParallelMultiHashMap<int, int> __positiveFilters;
        [ReadOnly]
        private NativeParallelMultiHashMap<int, int> __negativefilters;
        [ReadOnly]
        private NativeParallelMultiHashMap<int, int> __fungibleItems;

        public Hierarchy hierarchy => new Hierarchy(__infos, __children);

        public ReadOnly(
            in NativeArray<Type> types, 
            in NativePool<Info>.ReadOnlySlice infos, 
            in NativeParallelMultiHashMap<int, Child> children, 
            in NativeParallelMultiHashMap<int, int> positiveFilters,
            in NativeParallelMultiHashMap<int, int> negativefilters, 
            in NativeParallelMultiHashMap<int, int> fungibleItems)
        {
            __types = types;
            __infos = infos;
            __children = children;
            __positiveFilters = positiveFilters;
            __negativefilters = negativefilters;
            __fungibleItems = fungibleItems;
        }

        public bool TryGetValue(int index, out Info item) => __infos.TryGetValue(index, out item);

        public bool TryGetValue(in Handle handle, out Info item) => __infos.TryGetValue(handle, out item);

        public bool Contains(
            in Handle handle,
            int type,
            ref int count)
        {
            //return __Contains(handle, type, __infos, __children, ref count);

            return Contains(handle, type, ref count, default(NativeArray<int>));
        }

        public bool Contains(in Handle handle, int type, ref int count, in NativeArray<int> parentTypes)
        {
            if (count < 1)
                return false;

            return __Contains(
                handle,
                type,
                parentTypes,
                __infos,
                __children,
                __fungibleItems,
                ref count);
        }

        public bool Find(
            in Handle handle,
            int type,
            int count,
            //bool isRecursive,
            out int parentChildIndex,
            out Handle parentHandle,
            GameItemFindFlag flag = (GameItemFindFlag)~0)
        {
            if (type < 0 || type >= __types.Length)
            {
                parentChildIndex = -1;
                parentHandle = Handle.Empty;

                return false;
            }

            return __Find(
                true, 
                flag, 
                type, 
                count, 
                handle,
                __types, 
                __infos, 
                __children,
                __positiveFilters, 
                __negativefilters, 
                out parentChildIndex, 
                out parentHandle) != FindResult.None;
        }
    }

    public struct Enumerator : IEnumerator<Info>
    {
        private NativePool<Info>.Enumerator __instance;

        public Info Current => __instance.value;

        public Enumerator(in GameItemManager manager)
        {
            __instance = manager.__infos.GetEnumerator();
        }

        public bool MoveNext() => __instance.MoveNext();

        public void Reset() => __instance.Reset();

        object IEnumerator.Current => Current;

        void IDisposable.Dispose()
        {

        }
    }

    [ReadOnly]
    private NativeList<Type> __types;

    private NativeList<Command> __commands;

    private NativePool<Info> __infos;
    private NativeParallelMultiHashMap<int, Child> __children;

    [ReadOnly]
    private NativeParallelMultiHashMap<int, int> __positiveFilters;
    [ReadOnly]
    private NativeParallelMultiHashMap<int, int> __negativefilters;
    [ReadOnly]
    private NativeParallelMultiHashMap<int, int> __fungibleItems;

    public bool isCreated => __types.IsCreated;

    public int length => __infos.length;

    public AllocatorManager.AllocatorHandle allocator => __infos.allocator;

    public NativeArray<Command> commands => __commands.AsDeferredJobArray();

    public ReadOnlyInfos readOnlyInfos => new ReadOnlyInfos(__infos);

    //public Infos infos => new Infos(__infos);

    public Hierarchy hierarchy => new Hierarchy(__infos, __children);

    public ReadOnly readOnly => new ReadOnly(
        __types.AsArray(),
        __infos,
        __children,
        __positiveFilters,
        __negativefilters, 
        __fungibleItems);

    public JobHandle ScheduleCommands<T>(ref T job, int innerloopBatchCount, JobHandle inputDeps) where T : struct, IJobParallelForDefer
    {
        return job.Schedule(__commands, innerloopBatchCount, inputDeps);
    }

    public GameItemManager(Allocator allocator, ref NativeList<Command> commands)
    {
        __commands = commands;

        __types = new NativeList<Type>(allocator);
        __infos = new NativePool<Info>(allocator);
        __children = new NativeParallelMultiHashMap<int, Child>(1, allocator);

        __positiveFilters = new NativeParallelMultiHashMap<int, int>(1, allocator);
        __negativefilters = new NativeParallelMultiHashMap<int, int>(1, allocator);

        __fungibleItems = new NativeParallelMultiHashMap<int, int>(1, allocator);
    }

    public GameItemManager(Data[] datas, in NativeList<Command> commands, Allocator allocator)
    {
        int numItems = datas != null ? datas.Length : 0;

        __types = new NativeList<Type>(numItems, allocator);
        __types.ResizeUninitialized(numItems);
        int i, j, numFilters, numPositiveFilters = 0, numNegativefilters = 0, numFungibles = 0;
        Type destination;
        Data source;
        for (i = 0; i < numItems; ++i)
        {
            source = datas[i];

            destination.count = source.count;
            destination.capacity = source.capacity;

            __types[i] = destination;

            numFilters = source.filters == null ? 0 : source.filters.Length;
            if (source.isInvert)
                numNegativefilters += numFilters;
            else
                numPositiveFilters += numFilters;

            numFungibles += source.fungibles == null ? 0 : source.fungibles.Length;
        }

        __commands = commands;
        __infos = new NativePool<Info>(allocator);
        __children = new NativeParallelMultiHashMap<int, Child>(1, allocator);

        __positiveFilters = new NativeParallelMultiHashMap<int, int>(numPositiveFilters, allocator);
        __negativefilters = new NativeParallelMultiHashMap<int, int>(numNegativefilters, allocator);
        __fungibleItems = new NativeParallelMultiHashMap<int, int>(numFungibles, allocator);
        for (i = 0; i < numItems; ++i)
        {
            source = datas[i];

            numFilters = source.filters == null ? 0 : source.filters.Length;
            if (source.isInvert)
            {
                for (j = 0; j < numFilters; ++j)
                    __negativefilters.Add(i, source.filters[j]);
            }
            else
            {
                for (j = 0; j < numFilters; ++j)
                    __positiveFilters.Add(i, source.filters[j]);
            }

            if (source.fungibles != null)
            {
                foreach(int fungible in source.fungibles)
                    __fungibleItems.Add(i, fungible);
            }
        }
    }

    internal GameItemManager(
        in NativeList<Type> types,
        NativeList<Command> commands,
        NativePool<Info> infos,
        NativeParallelMultiHashMap<int, Child> children,
        in NativeParallelMultiHashMap<int, int> positiveFilters,
        in NativeParallelMultiHashMap<int, int> negativefilters,
        in NativeParallelMultiHashMap<int, int> fungibleItems)
    {
        __types = types;
        __commands = commands;
        __infos = infos;
        __children = children;
        __positiveFilters = positiveFilters;
        __negativefilters = negativefilters;
        __fungibleItems = fungibleItems;
    }

    public void Dispose()
    {
        __types.Dispose();

        __infos.Dispose();

        __children.Dispose();

        __positiveFilters.Dispose();

        __negativefilters.Dispose();
    }

    public Enumerator GetEnumerator() => new Enumerator(in this);

    public void Reset(Data[] datas)
    {
        int numItems = datas != null ? datas.Length : 0;

        __types.Resize(numItems, NativeArrayOptions.UninitializedMemory);
        int i, j, numFilters, numPositiveFilters = 0, numNegativefilters = 0, numFungibles = 0;
        Type destination;
        Data source;
        for (i = 0; i < numItems; ++i)
        {
            source = datas[i];

            destination.count = source.count;
            destination.capacity = source.capacity;

            __types[i] = destination;

            numFilters = source.filters == null ? 0 : source.filters.Length;
            if (source.isInvert)
                numNegativefilters += numFilters;
            else
                numPositiveFilters += numFilters;

            numFungibles += source.fungibles == null ? 0 : source.fungibles.Length;
        }

        __children.Clear();

        __positiveFilters.Clear();
        __negativefilters.Clear();
        __fungibleItems.Clear();
        for (i = 0; i < numItems; ++i)
        {
            source = datas[i];

            numFilters = source.filters == null ? 0 : source.filters.Length;
            if (source.isInvert)
            {
                for (j = 0; j < numFilters; ++j)
                    __negativefilters.Add(i, source.filters[j]);
            }
            else
            {
                for (j = 0; j < numFilters; ++j)
                    __positiveFilters.Add(i, source.filters[j]);
            }

            if (source.fungibles != null)
            {
                foreach (int fungible in source.fungibles)
                    __fungibleItems.Add(i, fungible);
            }
        }
    }

    public Type GetType(int index)
    {
        return __types[index];
    }

    public bool TryGetValue(in Handle handle, out Info item) => __infos.TryGetValue(handle, out item);

    public int CountOf(in Handle handle, int type)
    {
        if (!TryGetValue(handle, out var item))
            return 0;

        int length;
        if (item.type == type)
            length = item.count;
        else
            length = 0;

        NativeParallelMultiHashMapIterator<int> iterator;
        if (__children.TryGetFirstValue(handle.index, out var child, out iterator))
        {
            do
            {
                length += CountOf(child.handle, type);

            } while (__children.TryGetNextValue(out child, ref iterator));
        }

        return length;
    }

    /*public unsafe bool Contains(in Handle handle, int type, int count, int[] parentTypes)
    {
        if (count < 1)
            return false;

        int length = count;

        if(parentTypes == null)
            return __Contains(
                handle,
                type,
                default(NativeArray<int>),
                __infos,
                __children,
                ref length);

        fixed (int* ptr = parentTypes)
            return __Contains(
                handle, 
                type, 
                ZG.Unsafe.CollectionUtility.ToNativeArray<int>(ptr, parentTypes.Length), 
                __infos, 
                __children, 
                ref length);
    }*/

    public unsafe bool Contains(in Handle handle, int type, int count, in NativeArray<int> parentTypes = default)
    {
        if (count < 1)
            return false;

        int length = count;

        return __Contains(
                handle,
                type,
                parentTypes,
                __infos,
                __children,
                __fungibleItems, 
                ref length);
    }

    public Handle GetChild(in Handle handle, int index)
    {
        if (!__Find(handle.index, index, __children, out _, out var child))
            return Handle.Empty;

        return child.handle;
    }

    public Handle GetRoot(in Handle handle)
    {
        if (TryGetValue(handle, out var item))
        {
            Handle result = GetRoot(item.parentHandle);
            if (result.Equals(Handle.Empty))
                return handle;

            return result;
        }

        return Handle.Empty;
    }

    public bool GetChildren(in Handle handle, out NativeParallelMultiHashMap<int, Child>.Enumerator enumerator, out Info item)
    {
        if (!TryGetValue(handle, out item))
        {
            enumerator = default;

            return false;
        }

        enumerator = __children.GetValuesForKey(handle.index);

        return true;
    }

    public Handle Find(in Handle handle, int type)
    {
        if (!TryGetValue(handle, out var item))
            return Handle.Empty;

        if (item.type == type)
            return handle;

        Handle result;
        if (__children.TryGetFirstValue(handle.index, out var child, out var iterator))
        {
            do
            {
                result = Find(child.handle, type);
                if (!result.Equals(Handle.Empty))
                    return result;

            } while (__children.TryGetNextValue(out child, ref iterator));
        }

        result = Find(item.siblingHandle, type);
        if (!result.Equals(Handle.Empty))
            return result;

        return Handle.Empty;
    }

    public bool Find(
        in Handle handle,
        int type,
        int count,
        //bool isRecursive,
        out int parentChildIndex,
        out Handle parentHandle, 
        GameItemFindFlag flag = (GameItemFindFlag)~0)
    {
        if (type < 0 || type >= __types.Length)
        {
            parentChildIndex = -1;
            parentHandle = Handle.Empty;

            return false;
        }

        return __Find(
            true,
            flag, 
            type, 
            count, 
            handle,
            __types.AsArray(), 
            __infos, 
            __children, 
            __positiveFilters, 
            __negativefilters, 
            out parentChildIndex, 
            out parentHandle) != FindResult.None;
    }

    public bool CompareExchange(ref Handle handle, ref int value, out Info item)
    {
        if (__infos.TryGetValue(handle, out item) && item.type != value)
        {
            Command command;
            command.count = 0;

            command.sourceCount = item.count;
            command.sourceParentChildIndex = item.parentChildIndex;
            command.sourceParentHandle = item.parentHandle;
            command.sourceSiblingHandle = item.siblingHandle;
            command.sourceHandle = handle;

            int temp = item.type;
            item.type = value;

            item.version = ++handle.version;

            __infos[handle.index] = item;

            value = temp;

            if (__Find(item.parentHandle.index, item.parentChildIndex, __children, out var iterator, out var child))
            {
                UnityEngine.Assertions.Assert.AreEqual(child.handle.index, handle.index);
                UnityEngine.Assertions.Assert.AreEqual(child.handle.version + 1, handle.version);

                child.handle = handle;

                __children.SetValue(child, iterator);
            }

            command.destinationCount = item.count;
            command.destinationParentChildIndex = item.parentChildIndex;
            command.destinationParentHandle = item.parentHandle;
            command.destinationSiblingHandle = item.siblingHandle;
            command.destinationHandle = handle;

            command.type = temp;
            command.commandType = CommandType.Destroy;

            __commands.Add(command);

            command.type = item.type;
            command.commandType = CommandType.Create;
            /*command.type = item.type;
            command.sourceCount = 0;
            command.sourceParentChildIndex = -1;
            command.sourceParentHandle = Handle.Empty;
            command.sourceSiblingHandle = Handle.Empty;
            command.sourceHandle = Handle.Empty;
            command.destinationCount = item.count;
            command.destinationParentChildIndex = item.parentChildIndex;
            command.destinationParentHandle = item.parentHandle;
            command.destinationSiblingHandle = item.siblingHandle;
            command.destinationHandle = handle;*/

            __commands.Add(command);

            return true;
        }

        return false;
    }

    public Handle Add(int type, ref int count)
    {
        int capacity = __types[type].count;

        var handle = __Create();
        /*handle.index = __infos.nextIndex;
        __infos.TryGetValue(handle.index, out item);
        handle.version = ++item.version;*/
        Info item;
        item.version = handle.version;
        item.type = type;

        if (count == 0)
            count = capacity;

        if (count > capacity)
        {
            item.count = capacity;
            count -= capacity;
        }
        else
        {
            item.count = count;
            count = 0;
        }

        item.parentChildIndex = -1;
        item.parentHandle = Handle.Empty;

        item.siblingHandle = Handle.Empty;

        __infos.Insert(handle.index, item);

        Command command;
        command.commandType = CommandType.Create;
        command.type = type;
        command.count = item.count;
        command.sourceCount = 0;
        command.destinationCount = item.count;
        command.sourceParentChildIndex = -1;
        command.destinationParentChildIndex = -1;
        command.sourceParentHandle = Handle.Empty;
        command.destinationParentHandle = Handle.Empty;
        command.sourceSiblingHandle = Handle.Empty;
        command.destinationSiblingHandle = Handle.Empty;
        command.sourceHandle = Handle.Empty;
        command.destinationHandle = handle;

        __commands.Add(command);

        return handle;
    }

    public Handle Add(int type)
    {
        int count = 0;

        return Add(type, ref count);
    }

    public Handle Add(in Handle parentHandle, int parentChildIndex, int type, ref int count)
    {
        if (count < 1)
            return Handle.Empty;

        if (!TryGetValue(parentHandle, out var item))
            return Handle.Empty;

        if (__types[item.type].capacity <= parentChildIndex)
            return Handle.Empty;

        if (!__Filter(item.type, type, __positiveFilters, __negativefilters))
            return Handle.Empty;

        Command command;

        int capacity = __types[type].count;

        bool isFind = __Find(parentHandle.index, parentChildIndex, __children, out var iterator, out var child);
        if (isFind && TryGetValue(child.handle, out var temp))
        {
            if (temp.type != type)
                return Handle.Empty;

            UnityEngine.Assertions.Assert.AreEqual(temp.parentChildIndex, parentChildIndex);
            UnityEngine.Assertions.Assert.AreEqual(temp.parentHandle, parentHandle);

            command.commandType = CommandType.Add;
            command.type = type;
            command.count = count;
            command.sourceCount = temp.count;
            command.sourceParentChildIndex = parentChildIndex;
            command.sourceParentHandle = parentHandle;
            command.sourceSiblingHandle = temp.siblingHandle;
            command.sourceHandle = child.handle;

            temp.count += count;
            if (temp.count < capacity)
                count = 0;
            else
            {
                count = temp.count - capacity;

                command.count -= count;

                temp.count = capacity;
            }

            __infos[child.handle.index] = temp;

            command.destinationCount = temp.count;
            command.destinationParentChildIndex = parentChildIndex;
            command.destinationParentHandle = parentHandle;
            command.destinationSiblingHandle = temp.siblingHandle;
            command.destinationHandle = child.handle;

            __commands.Add(command);

            return child.handle;
        }

        command.commandType = CommandType.Create;
        command.type = type;
        command.count = count;
        command.sourceCount = 0;
        command.sourceParentChildIndex = -1;
        command.sourceParentHandle = Handle.Empty;
        command.sourceSiblingHandle = Handle.Empty;
        command.sourceHandle = Handle.Empty;

        child.handle = __Create();

        /*__infos.TryGetValue(__infos.nextIndex, out temp);
        child.handle.version = ++temp.version;*/
        temp.version = child.handle.version;
        temp.type = type;

        if (count > capacity)
        {
            temp.count = capacity;
            count -= capacity;
            command.count -= count;
        }
        else
        {
            temp.count = count;
            count = 0;
        }

        temp.parentChildIndex = parentChildIndex;
        temp.parentHandle = parentHandle;

        temp.siblingHandle = Handle.Empty;

        child.index = parentChildIndex;

        __infos.Insert(child.handle.index, temp);

        if (isFind)
            __children.SetValue(child, iterator);
        else
            __children.Add(parentHandle.index, child);

        command.destinationCount = temp.count;
        command.destinationParentChildIndex = parentChildIndex;
        command.destinationParentHandle = parentHandle;
        command.destinationSiblingHandle = Handle.Empty;
        command.destinationHandle = child.handle;

        __commands.Add(command);

        return child.handle;
    }

    public bool Add(in Handle handle, ref int count)
    {
        if (count < 1)
            return false;

        if (!TryGetValue(handle, out var item))
            return false;

        int capacity = __types[item.type].count;

        Command command;
        command.commandType = CommandType.Add;
        command.type = item.type;
        command.count = count;
        command.sourceCount = item.count;
        command.sourceParentChildIndex = item.parentChildIndex;
        command.sourceParentHandle = item.parentHandle;
        command.sourceSiblingHandle = item.siblingHandle;
        command.sourceHandle = handle;

        item.count += count;
        if (item.count < capacity)
            count = 0;
        else
        {
            count = item.count - capacity;

            command.count -= count;

            item.count = capacity;
        }

        __infos[handle.index] = item;

        command.destinationCount = item.count;
        command.destinationParentChildIndex = item.parentChildIndex;
        command.destinationParentHandle = item.parentHandle;
        command.destinationSiblingHandle = item.siblingHandle;
        command.destinationHandle = handle;

        __commands.Add(command);

        return true;
    }

    public bool AttachSibling(in Handle handle, in Handle siblingHandle)
    {
        if (!TryGetValue(handle, out var item))
            return false;

        Command command;
        command.commandType = CommandType.Connect;
        command.type = item.type;
        command.count = 0;
        command.sourceCount = item.count;
        command.sourceParentChildIndex = item.parentChildIndex;
        command.sourceParentHandle = item.parentHandle;
        command.sourceSiblingHandle = item.siblingHandle;
        command.sourceHandle = handle;

        //item.parentHandle = Handle.Empty;
        //item.parentChildIndex = -1;

        //UnityEngine.Debug.LogError($"AttachSibling {item.type} : {handle.index} -> {siblingHandle.index}");

        item.siblingHandle = siblingHandle;

        __infos[handle.index] = item;

        command.destinationCount = item.count;
        command.destinationParentChildIndex = item.parentChildIndex;
        command.destinationParentHandle = item.parentHandle;
        command.destinationSiblingHandle = siblingHandle;
        command.destinationHandle = handle;

        __commands.Add(command);

        return true;
    }

    public bool DetachParent(in Handle handle)
    {
        if (!TryGetValue(handle, out var item))
            return false;

        if (!__DetachParent(handle, __children, ref __infos, ref item, out var command))
            return false;

        /*if (!__Find(item.parentHandle.index, item.parentChildIndex, __children, out var iterator, out var child))
            return false;

        Command command;
        command.commandType = CommandType.Move;
        command.type = item.type;
        command.count = 0;
        command.sourceCount = item.count;
        command.sourceParentChildIndex = item.parentChildIndex;
        command.sourceParentHandle = item.parentHandle;
        command.sourceSiblingHandle = item.siblingHandle;
        command.sourceHandle = handle;

        item.parentHandle = Handle.Empty;
        item.parentChildIndex = -1;

        __infos[handle.index] = item;
        __children.Remove(iterator);

        command.destinationCount = item.count;
        command.destinationParentChildIndex = -1;
        command.destinationParentHandle = Handle.Empty;
        command.destinationSiblingHandle = item.siblingHandle;
        command.destinationHandle = handle;*/

        __commands.Add(command);

        return true;
    }

    public Handle DetachParent(in Handle handle, ref int count)
    {
        if (count < 1)
            return Handle.Empty;

        if (!TryGetValue(handle, out var item) && item.count < count)
            return Handle.Empty;

        if(item.count == count)
        {
            if(__DetachParent(handle, __children, ref __infos, ref item, out var command))
            {
                count = 0;

                __commands.Add(command);

                return handle;
            }

            return Handle.Empty;
        }

        Command source, destination;

        int capacity = __types[item.type].count;

        destination.commandType = CommandType.Create;
        destination.type = item.type;
        destination.count = count;
        destination.sourceCount = item.count;
        destination.sourceParentChildIndex = item.parentChildIndex;
        destination.sourceParentHandle = item.parentHandle;
        destination.sourceSiblingHandle = item.siblingHandle;
        destination.sourceHandle = handle;

        //__infos.TryGetValue(__infos.nextIndex, out var temp);

        //Handle result = __Create();
        //result.version = ++temp.version;

        source.commandType = CommandType.Remove;
        source.type = item.type;
        source.count = count;
        source.sourceCount = item.count;
        source.sourceParentChildIndex = item.parentChildIndex;
        source.sourceParentHandle = item.parentHandle;
        source.sourceSiblingHandle = item.siblingHandle;
        source.sourceHandle = handle;

        item.count -= count;

        Handle result = __Create();
        Info temp;
        temp.version = result.version;
        temp.type = item.type;
        if (count > capacity)
        {
            count -= capacity;

            destination.count -= count;
            source.count -= count;

            item.count += count;

            temp.count = capacity;
        }
        else
        {
            temp.count = count;
            count = 0;
        }

        __infos[handle.index] = item;

        temp.parentChildIndex = -1;
        temp.parentHandle = Handle.Empty;

        temp.siblingHandle = Handle.Empty;

        __infos.Insert(result.index, temp);

        source.destinationCount = item.count;
        source.destinationParentChildIndex = -1;
        source.destinationParentHandle = Handle.Empty;
        source.destinationSiblingHandle = item.siblingHandle;
        source.destinationHandle = result;

        __commands.Add(source);

        destination.destinationCount = temp.count;
        destination.destinationParentChildIndex = -1;
        destination.destinationParentHandle = Handle.Empty;
        destination.destinationSiblingHandle = temp.siblingHandle;
        destination.destinationHandle = result;

        __commands.Add(destination);

        return result;
    }

    public Handle Move(in Handle handle, in Handle parentHandle, int parentChildIndex, ref int count)
    {
        if (count < 1)
            return Handle.Empty;

        if (!TryGetValue(handle, out var item) && item.count <= count)
            return Handle.Empty;

        if (!TryGetValue(parentHandle, out var parent))
            return Handle.Empty;

        if (__types[parent.type].capacity <= parentChildIndex)
            return Handle.Empty;

        if (!__Filter(parent.type, item.type, __positiveFilters, __negativefilters))
            return Handle.Empty;

        Command source, destination;

        int capacity = __types[item.type].count;

        bool isFind = __Find(parentHandle.index, parentChildIndex, __children, out var iterator, out var child);
        if (isFind && TryGetValue(child.handle, out var temp))
        {
            if (temp.type != item.type)
                return Handle.Empty;

            UnityEngine.Assertions.Assert.AreEqual(temp.parentChildIndex, parentChildIndex);
            UnityEngine.Assertions.Assert.AreEqual(temp.parentHandle, parentHandle);

            destination.commandType = CommandType.Add;
            destination.type = temp.type;
            destination.count = count;
            destination.sourceCount = item.count;
            destination.sourceParentChildIndex = item.parentChildIndex;
            destination.sourceParentHandle = item.parentHandle;
            destination.sourceSiblingHandle = item.siblingHandle;
            destination.sourceHandle = handle;

            source.commandType = CommandType.Remove;
            source.type = item.type;
            source.count = count;
            source.sourceCount = item.count;
            source.sourceParentChildIndex = item.parentChildIndex;
            source.sourceParentHandle = item.parentHandle;
            source.sourceSiblingHandle = item.siblingHandle;
            source.sourceHandle = handle;

            item.count -= count;
            temp.count += count;
            if (temp.count < capacity)
                count = 0;
            else
            {
                count = temp.count - capacity;

                destination.count -= count;
                source.count -= count;

                item.count += count;

                temp.count = capacity;
            }

            __infos[handle.index] = item;

            source.destinationCount = item.count;
            source.destinationParentChildIndex = parentChildIndex;
            source.destinationParentHandle = parentHandle;
            source.destinationSiblingHandle = item.siblingHandle;
            source.destinationHandle = child.handle;

            __commands.Add(source);

            __infos[child.handle.index] = temp;

            destination.destinationCount = temp.count;
            destination.destinationParentChildIndex = parentChildIndex;
            destination.destinationParentHandle = parentHandle;
            destination.destinationSiblingHandle = temp.siblingHandle;
            destination.destinationHandle = child.handle;

            __commands.Add(destination);

            return child.handle;
        }

        destination.commandType = CommandType.Create;
        destination.type = item.type;
        destination.count = count;
        destination.sourceCount = item.count;
        destination.sourceParentChildIndex = item.parentChildIndex;
        destination.sourceParentHandle = item.parentHandle;
        destination.sourceSiblingHandle = item.siblingHandle;
        destination.sourceHandle = handle;

        /*__infos.TryGetValue(__infos.nextIndex, out temp);
        child.handle.version = ++temp.version;*/

        source.commandType = CommandType.Remove;
        source.type = item.type;
        source.count = count;
        source.sourceCount = item.count;
        source.sourceParentChildIndex = item.parentChildIndex;
        source.sourceParentHandle = item.parentHandle;
        source.sourceSiblingHandle = item.siblingHandle;
        source.sourceHandle = handle;

        item.count -= count;

        child.handle = __Create();
        temp.version = child.handle.version;
        temp.type = item.type;
        if (count > capacity)
        {
            count -= capacity;

            destination.count -= count;
            source.count -= count;

            item.count += count;

            temp.count = capacity;
        }
        else
        {
            temp.count = count;
            count = 0;
        }

        __infos[handle.index] = item;

        temp.parentChildIndex = parentChildIndex;
        temp.parentHandle = parentHandle;

        temp.siblingHandle = Handle.Empty;

        child.index = parentChildIndex;

         __infos.Insert(child.handle.index, temp);

        if (isFind)
            __children.SetValue(child, iterator);
        else
            __children.Add(parentHandle.index, child);

        source.destinationCount = item.count;
        source.destinationParentChildIndex = parentChildIndex;
        source.destinationParentHandle = parentHandle;
        source.destinationSiblingHandle = item.siblingHandle;
        source.destinationHandle = child.handle;

        __commands.Add(source);

        destination.destinationCount = temp.count;
        destination.destinationParentChildIndex = parentChildIndex;
        destination.destinationParentHandle = parentHandle;
        destination.destinationSiblingHandle = temp.siblingHandle;
        destination.destinationHandle = child.handle;

        __commands.Add(destination);

        return child.handle;
    }

    public MoveType Move(in Handle handle, in Handle parentHandle, int parentChildIndex)
    {
        if (handle.Equals(parentHandle))
            return MoveType.Error;

        if (!TryGetValue(handle, out var item))
            return MoveType.Error;

        if (!TryGetValue(parentHandle, out var destination))
            return MoveType.Error;

        Command command;

        var type = __types[destination.type];
        if (type.capacity <= parentChildIndex)
        {
            if (destination.type != item.type || destination.count >= type.count)
                return MoveType.Error;

            MoveType result;

            command.commandType = CommandType.Add;
            command.type = destination.type;
            command.count = item.count;
            command.sourceCount = item.count;
            command.sourceParentChildIndex = item.parentChildIndex;
            command.sourceParentHandle = item.parentHandle;
            command.sourceSiblingHandle = item.siblingHandle;
            command.sourceHandle = handle;

            int count = destination.count;
            destination.count += item.count;
            if (destination.count > type.count)
            {
                count = type.count - count;

                destination.count = type.count;

                command.count = count;

                Command temp;
                temp.commandType = CommandType.Remove;
                temp.type = item.type;
                temp.count = count;
                temp.sourceCount = item.count;
                temp.sourceParentChildIndex = item.parentChildIndex;
                temp.sourceParentHandle = item.parentHandle;
                temp.sourceSiblingHandle = item.siblingHandle;
                temp.sourceHandle = handle;

                item.count -= count;

                temp.destinationCount = item.count;
                temp.destinationParentChildIndex = destination.parentChildIndex;
                temp.destinationParentHandle = destination.parentHandle;
                temp.destinationSiblingHandle = destination.siblingHandle;
                temp.destinationHandle = parentHandle;

                __commands.Add(temp);

                __infos[handle.index] = item;

                result = MoveType.Normal;
            }
            else
            {
                __Delete(handle, parentHandle, destination.siblingHandle, destination.parentHandle, destination.parentChildIndex);

                result = MoveType.Reverse;
            }

            __infos[parentHandle.index] = destination;

            command.destinationCount = destination.count;
            command.destinationParentChildIndex = destination.parentChildIndex;
            command.destinationParentHandle = destination.parentHandle;
            command.destinationSiblingHandle = destination.siblingHandle;
            command.destinationHandle = parentHandle;

            __commands.Add(command);

            return result;
        }

        if (!__Filter(destination.type, item.type, __positiveFilters, __negativefilters))
            return MoveType.Error;

        if (__Find(parentHandle.index, parentChildIndex, __children, out var destinationIterator, out var destinationChild))
        {
            if (handle.Equals(destinationChild.handle))
                return MoveType.Error;

            if (TryGetValue(destinationChild.handle, out var child))
            {
                if (child.type == item.type)
                {
                    type = __types[child.type];
                    if (child.count >= type.count)
                        return MoveType.Error;

                    MoveType result;

                    command.commandType = CommandType.Add;
                    command.type = child.type;
                    command.count = item.count;
                    command.sourceCount = item.count;
                    command.sourceParentChildIndex = item.parentChildIndex;
                    command.sourceParentHandle = item.parentHandle;
                    command.sourceSiblingHandle = item.siblingHandle;
                    command.sourceHandle = handle;

                    int count = child.count;
                    child.count += item.count;
                    if (child.count > type.count)
                    {
                        count = type.count - count;

                        child.count = type.count;

                        command.count = count;

                        Command temp;
                        temp.commandType = CommandType.Remove;
                        temp.type = item.type;
                        temp.count = count;
                        temp.sourceCount = item.count;
                        temp.sourceParentChildIndex = item.parentChildIndex;
                        temp.sourceParentHandle = item.parentHandle;
                        temp.sourceSiblingHandle = item.siblingHandle;
                        temp.sourceHandle = handle;

                        item.count -= count;

                        temp.destinationCount = item.count;
                        temp.destinationParentChildIndex = child.parentChildIndex;
                        temp.destinationParentHandle = child.parentHandle;
                        temp.destinationSiblingHandle = child.siblingHandle;
                        temp.destinationHandle = destinationChild.handle;

                        __commands.Add(temp);

                        __infos[handle.index] = item;

                        result = MoveType.Normal;
                    }
                    else
                    {
                        __Delete(handle, destinationChild.handle, child.siblingHandle, child.parentHandle, child.parentChildIndex);

                        result = MoveType.Reverse;
                    }

                    __infos[destinationChild.handle.index] = child;

                    command.destinationCount = child.count;
                    command.destinationParentChildIndex = child.parentChildIndex;
                    command.destinationParentHandle = child.parentHandle;
                    command.destinationSiblingHandle = child.siblingHandle;
                    command.destinationHandle = destinationChild.handle;

                    __commands.Add(command);

                    return result;
                }

                if (!TryGetValue(item.parentHandle, out var source) || !__Filter(source.type, child.type, __positiveFilters, __negativefilters))
                    return MoveType.Error;

                if (__Find(item.parentHandle.index, item.parentChildIndex, __children, out var sourceIterator, out var sourceChild))
                {
                    command.commandType = CommandType.Move;
                    command.type = item.type;
                    command.count = 0;
                    command.sourceCount = item.count;
                    command.sourceParentChildIndex = item.parentChildIndex;
                    command.sourceParentHandle = item.parentHandle;
                    command.sourceSiblingHandle = item.siblingHandle;
                    command.sourceHandle = handle;

                    Command temp;
                    temp.commandType = CommandType.Move;
                    temp.type = child.type;
                    temp.count = 0;
                    temp.sourceCount = child.count;
                    temp.sourceParentChildIndex = child.parentChildIndex;
                    temp.sourceParentHandle = child.parentHandle;
                    temp.sourceSiblingHandle = child.siblingHandle;
                    temp.sourceHandle = destinationChild.handle;

                    child.parentChildIndex = item.parentChildIndex;
                    child.parentHandle = item.parentHandle;

                    sourceChild.handle = destinationChild.handle;

                    __children.SetValue(sourceChild, sourceIterator);

                    __infos[destinationChild.handle.index] = child;

                    temp.destinationCount = child.count;
                    temp.destinationParentChildIndex = child.parentChildIndex;
                    temp.destinationParentHandle = child.parentHandle;
                    temp.destinationSiblingHandle = child.siblingHandle;
                    temp.destinationHandle = destinationChild.handle;

                    __commands.Add(temp);

                    item.parentChildIndex = parentChildIndex;
                    item.parentHandle = parentHandle;
                }
                else
                    return MoveType.Error;
            }
            else
            {
                command.commandType = CommandType.Move;
                command.type = item.type;
                command.count = 0;
                command.sourceCount = item.count;
                command.sourceParentChildIndex = item.parentChildIndex;
                command.sourceParentHandle = item.parentHandle;
                command.sourceSiblingHandle = item.siblingHandle;
                command.sourceHandle = handle;

                if (__Find(item.parentHandle.index, item.parentChildIndex, __children, out var sourceIterator, out _))
                    __children.Remove(sourceIterator);

                item.parentHandle = parentHandle;
                item.parentChildIndex = parentChildIndex;
            }

            destinationChild.handle = handle;

            __children.SetValue(destinationChild, destinationIterator);
        }
        else
        {
            command.commandType = CommandType.Move;
            command.type = item.type;
            command.count = 0;
            command.sourceCount = item.count;
            command.sourceParentChildIndex = item.parentChildIndex;
            command.sourceParentHandle = item.parentHandle;
            command.sourceSiblingHandle = item.siblingHandle;
            command.sourceHandle = handle;

            destinationChild.handle = handle;
            destinationChild.index = parentChildIndex;

            if (__Find(item.parentHandle.index, item.parentChildIndex, __children, out var sourceIterator, out _))
                __children.Remove(sourceIterator);

            __children.Add(parentHandle.index, destinationChild);

            item.parentHandle = parentHandle;
            item.parentChildIndex = parentChildIndex;
        }

        __infos[handle.index] = item;

        command.destinationCount = item.count;
        command.destinationParentChildIndex = item.parentChildIndex;
        command.destinationParentHandle = item.parentHandle;
        command.destinationSiblingHandle = item.siblingHandle;
        command.destinationHandle = handle;

        __commands.Add(command);

        return MoveType.All;
    }

    public int Remove(in Handle handle, int count)
    {
        //UnityEngine.Debug.LogError($"Remove {handle.index} : {count}");

        if (!TryGetValue(handle, out var item))
            return 0;

        if (count > 0 && item.count > count)
        {
            Command command;
            command.commandType = CommandType.Remove;
            command.type = item.type;
            command.count = count;
            command.sourceCount = item.count;
            command.sourceParentChildIndex = item.parentChildIndex;
            command.sourceParentHandle = item.parentHandle;
            command.sourceSiblingHandle = item.siblingHandle;
            command.sourceHandle = handle;

            item.count -= count;

            command.destinationCount = item.count;
            command.destinationParentChildIndex = item.parentChildIndex;
            command.destinationParentHandle = item.parentHandle;
            command.destinationSiblingHandle = item.siblingHandle;
            command.destinationHandle = handle;

            __commands.Add(command);

            __infos[handle.index] = item;

            return count;
        }

        __Delete(handle, Handle.Empty, Handle.Empty, Handle.Empty, -1);

        return item.count;
    }

    public int Remove(
        in Handle handle, 
        int type, 
        int count, 
        in NativeArray<int> parentTypes = default, 
        NativeList<Handle> siblings = default, 
        NativeList<Handle> children = default)
    {
        if (!TryGetValue(handle, out var item))
            return 0;

        int length = 0;
        if (item.type == type)
        {
            bool result = !parentTypes.IsCreated || parentTypes.Length < 1;
            if (!result)
                result = TryGetValue(item.parentHandle, out var parent) && parentTypes.IndexOf<int, int>(parent.type) != -1;

            if (result)
            {
                if(item.count <= count)
                {
                    if(siblings.IsCreated && TryGetValue(item.siblingHandle, out _))
                    {
                        siblings.Add(item.siblingHandle);

                        Command command;
                        command.commandType = CommandType.Connect;
                        command.count = 0;

                        command.type = item.type;
                        command.sourceCount = item.count;
                        command.sourceParentChildIndex = item.parentChildIndex;
                        command.sourceParentHandle = item.parentHandle;
                        command.sourceSiblingHandle = item.siblingHandle;
                        command.sourceHandle = handle;

                        item.siblingHandle = Handle.Empty;
                        __infos[handle.index] = item;

                        command.destinationCount = item.count;
                        command.destinationParentChildIndex = item.parentChildIndex;
                        command.destinationParentHandle = item.parentHandle;
                        command.destinationSiblingHandle = item.siblingHandle;
                        command.destinationHandle = handle;

                        __commands.Add(command);
                    }

                    if (children.IsCreated && __children.TryGetFirstValue(handle.index, out var child, out var iterator))
                    {
                        Info temp;

                        Command command;
                        command.commandType = CommandType.Move;
                        command.count = 0;

                        do
                        {
                            children.Add(child.handle);

                            temp = __infos[child.handle.index];
                            UnityEngine.Assertions.Assert.AreEqual(temp.version, child.handle.version);

                            command.type = temp.type;
                            command.sourceCount = temp.count;
                            command.sourceParentChildIndex = temp.parentChildIndex;
                            command.sourceParentHandle = temp.parentHandle;
                            command.sourceSiblingHandle = temp.siblingHandle;
                            command.sourceHandle = child.handle;

                            temp.parentHandle = Handle.Empty;
                            temp.parentChildIndex = -1;

                            __infos[child.handle.index] = temp;

                            command.destinationCount = temp.count;
                            command.destinationParentChildIndex = -1;
                            command.destinationParentHandle = Handle.Empty;
                            command.destinationSiblingHandle = temp.siblingHandle;
                            command.destinationHandle = child.handle;

                            __commands.Add(command);

                        } while (__children.TryGetNextValue(out child, ref iterator));
                    }
                }

                length += Remove(handle, count);
            }
        }
        else
        {
            if (__children.TryGetFirstValue(handle.index, out var child, out var iterator))
            {
                do
                {
                    length += Remove(child.handle, type, count - length, default(NativeArray<int>), siblings, children);
                    if (length >= count)
                        return length;

                } while (__children.TryGetNextValue(out child, ref iterator));
            }

            length += Remove(item.siblingHandle, type, count - length, default(NativeArray<int>), siblings, children);
            /*if (length >= count)
                return length;*/
        }

        return length;
    }

    public int Remove(in Handle handle, int type, int count, NativeList<Handle> siblings, NativeList<Handle> children)
    {
        return Remove(handle, type, count, default(NativeArray<int>), siblings, children);
    }

    /*public unsafe int Remove(in Handle handle, int type, int count, int[] parentTypes)
    {
        if (parentTypes == null)
            return Remove(handle, type, count, default);

        fixed (int* ptr = parentTypes)
            return Remove(handle, type, count, ZG.Unsafe.CollectionUtility.ToNativeArray<int>(ptr, parentTypes.Length), default, default);
    }*/

    private bool __Delete(Handle handle, Handle target, Handle siblingHandle, Handle parentHandle, int parentChildIndex)
    {
        if (!TryGetValue(handle, out var item))
            return false;

        if (__children.TryGetFirstValue(handle.index, out var child, out var iterator))
        {
            do
            {
                __Delete(child.handle, Handle.Empty, Handle.Empty, Handle.Empty, -1);
            } while (__children.TryGetNextValue(out child, ref iterator));
        }

        __Delete(item.siblingHandle, Handle.Empty, Handle.Empty, Handle.Empty, -1);

        Command command;
        command.commandType = CommandType.Destroy;
        command.type = item.type;
        command.count = item.count;
        command.sourceCount = item.count;
        command.destinationCount = 0;
        command.sourceParentChildIndex = item.parentChildIndex;
        command.destinationParentChildIndex = parentChildIndex;
        command.sourceParentHandle = item.parentHandle;
        command.destinationParentHandle = parentHandle;
        command.sourceSiblingHandle = item.siblingHandle;
        command.destinationSiblingHandle = siblingHandle;
        command.sourceHandle = handle;
        command.destinationHandle = target;

        __commands.Add(command);

        if (TryGetValue(item.parentHandle, out _) && __Find(item.parentHandle.index, item.parentChildIndex, __children, out iterator, out child))
        {
            UnityEngine.Assertions.Assert.AreEqual(child.handle, handle);
            UnityEngine.Assertions.Assert.AreEqual(child.index, item.parentChildIndex);

            __children.Remove(iterator);
        }

        return __infos.RemoveAt(handle.index);
    }

    private Handle __Create()
    {
        Handle handle;
        handle.index = __infos.nextIndex;
        __infos.TryGetValue(handle.index, out var item);
        handle.version = ++item.version;

        UnityEngine.Assertions.Assert.AreNotEqual(int.MaxValue, handle.index);
        UnityEngine.Assertions.Assert.AreNotEqual(0, handle.version);

        return handle;
    }

    private static bool __Filter(
        int parentType, 
        int childType,
        in NativeParallelMultiHashMap<int, int> positiveFilters, 
        in NativeParallelMultiHashMap<int, int> negativefilters)
    {
        int filter;
        NativeParallelMultiHashMapIterator<int> filterIterator;
        if (negativefilters.TryGetFirstValue(parentType, out filter, out filterIterator))
        {
            do
            {
                if (childType == filter)
                    return false;

            } while (negativefilters.TryGetNextValue(out filter, ref filterIterator));
        }

        bool isFind;
        if (positiveFilters.TryGetFirstValue(parentType, out filter, out filterIterator))
        {
            isFind = false;
            do
            {
                if (childType == filter)
                {
                    isFind = true;

                    break;
                }

            } while (positiveFilters.TryGetNextValue(out filter, ref filterIterator));

            if (!isFind)
                return false;
        }

        return true;
    }

    private static FindResult __Find(
        bool isRoot,
        GameItemFindFlag flag,
        int type,
        int count,
        in Handle handle,
        in NativeArray<Type> types, 
        in NativePool<Info>.ReadOnlySlice items,
        in NativeParallelMultiHashMap<int, Child> children, 
        in NativeParallelMultiHashMap<int, int> positiveFilters,
        in NativeParallelMultiHashMap<int, int> negativefilters, 
        out int parentChildIndex,
        out Handle parentHandle)
    {
        parentChildIndex = -1;
        parentHandle = Handle.Empty;

        var result = FindResult.None;

        if (!isRoot)
        {
            var resultTemp = __Find(
                flag,
                1,
                type,
                count,
                handle,
                types,
                items,
                children,
                positiveFilters,
                negativefilters,
                out parentChildIndex,
                out parentHandle);

            if (resultTemp == FindResult.Normal)
                return FindResult.Normal;

            result = resultTemp;
        }

        if ((flag & GameItemFindFlag.Siblings) == GameItemFindFlag.Siblings &&
            items.TryGetValue(handle, out var item))
        {
            var resultTemp = __Find(
                false,
                flag | GameItemFindFlag.Self | GameItemFindFlag.Children,
                type,
                count,
                item.siblingHandle,
                types,
                items,
                children,
                positiveFilters,
                negativefilters,
                out int parentChildIndexTemp,
                out Handle parentHandleTemp);

            switch(resultTemp)
            {
                case FindResult.Empty:
                    if (result == FindResult.None)
                    {
                        parentChildIndex = parentChildIndexTemp;
                        parentHandle = parentHandleTemp;

                        result = FindResult.Empty;
                    }
                    break;
                case FindResult.Normal:
                    parentChildIndex = parentChildIndexTemp;
                    parentHandle = parentHandleTemp;

                    return FindResult.Normal;
                default:
                    break;
            }
        }

        if (isRoot)
        {
            var resultTemp = __Find(
                flag,
                0,
                type,
                count,
                handle,
                types,
                items,
                children,
                positiveFilters,
                negativefilters,
                out int parentChildIndexTemp,
                out Handle parentHandleTemp);

            switch (resultTemp)
            {
                case FindResult.Empty:
                    if (result == FindResult.None)
                    {
                        parentChildIndex = parentChildIndexTemp;
                        parentHandle = parentHandleTemp;

                        result = FindResult.Empty;
                    }
                    break;
                case FindResult.Normal:
                    parentChildIndex = parentChildIndexTemp;
                    parentHandle = parentHandleTemp;

                    return FindResult.Normal;
                default:
                    break;
            }
        }

        return result;
    }

    private static FindResult __Find(
        GameItemFindFlag flag,
        int depth,
        int type,
        int count,
        in Handle handle, 
        in NativeArray<Type> types, 
        in NativePool<Info>.ReadOnlySlice items,
        in NativeParallelMultiHashMap<int, Child> children,
        in NativeParallelMultiHashMap<int, int> positiveFilters,
        in NativeParallelMultiHashMap<int, int> negativefilters,
        out int parentChildIndex,
        out Handle parentHandle)
    {
        parentChildIndex = -1;
        parentHandle = Handle.Empty;

        if (!items.TryGetValue(handle, out var item))
            return FindResult.None;

        if ((flag & GameItemFindFlag.Children) == GameItemFindFlag.Children && children.TryGetFirstValue(handle.index, out var child, out var iterator))
        {
            FindResult result;
            Info temp;
            do
            {
                if (items.TryGetValue(child.handle, out temp) && temp.type == type && temp.count + count <= types[type].count)
                {
                    parentHandle = handle;
                    parentChildIndex = child.index;

                    return FindResult.Normal;
                }

                if (depth > 0)
                {
                    result = __Find(
                        flag | GameItemFindFlag.Self,
                        depth - 1,
                        type,
                        count,
                        child.handle,
                        types,
                        items,
                        children,
                        positiveFilters,
                        negativefilters,
                        out parentChildIndex,
                        out parentHandle);

                    if(result != FindResult.None)
                        return result;
                }
            } while (children.TryGetNextValue(out child, ref iterator));
        }

        if ((flag & GameItemFindFlag.Self) == GameItemFindFlag.Self && 
            __Filter(
            item.type, 
            type,
            positiveFilters,
            negativefilters))
        {
            int capacity = types[item.type].capacity;
            for (int i = 0; i < capacity; ++i)
            {
                if (!__Find(handle.index, i, children, out _, out _))
                {
                    parentHandle = handle;
                    parentChildIndex = i;

                    return FindResult.Empty;
                }
            }
        }

        return FindResult.None;
    }

    private static bool __Find(
        int index,
        int childIndex,
        in NativeParallelMultiHashMap<int, Child> children,
        out NativeParallelMultiHashMapIterator<int> iterator,
        out Child child)
    {
        if (children.TryGetFirstValue(index, out child, out iterator))
        {
            do
            {
                if (child.index == childIndex)
                    return true;

            } while (children.TryGetNextValue(out child, ref iterator));
        }

        return false;
    }

    private static bool __Contains(
        in Handle handle,
        int type,
        in NativeArray<int> parentTypes,
        in NativePool<Info>.ReadOnlySlice items,
        in NativeParallelMultiHashMap<int, Child> children,
        ref int length)
    {
        if (!items.TryGetValue(handle, out var item))
            return false;

        if (item.type == type)
        {
            bool result = !parentTypes.IsCreated || parentTypes.Length < 1;
            if (!result)
                result = items.TryGetValue(item.parentHandle, out var parent) && parentTypes.IndexOf<int, int>(parent.type) != -1;

            if (result)
                length -= item.count;
        }

        if (length <= 0)
            return true;

        if (children.TryGetFirstValue(handle.index, out var child, out var iterator))
        {
            do
            {
                if (__Contains(child.handle, type, default, items, children, ref length))
                    return true;

            } while (children.TryGetNextValue(out child, ref iterator));
        }

        if (__Contains(item.siblingHandle, type, default, items, children, ref length))
            return true;

        return false;
    }

    private static bool __Contains(
        in Handle handle,
        int type,
        in NativeArray<int> parentTypes,
        in NativePool<Info>.ReadOnlySlice items,
        in NativeParallelMultiHashMap<int, Child> children,
        in NativeParallelMultiHashMap<int, int> fungibleItems,
        ref int length)
    {
        if (__Contains(
            handle,
            type,
            parentTypes,
            items,
            children,
            ref length))
            return true;

        if(fungibleItems.IsCreated && fungibleItems.TryGetFirstValue(type, out int fungibleType, out var iterator))
        {
            do
            {
                if (__Contains(
                    handle,
                    fungibleType,
                    parentTypes,
                    items,
                    children,
                    ref length))
                    return true;

            } while (fungibleItems.TryGetNextValue(out fungibleType, ref iterator));
        }

        return false;
    }

    public static bool __DetachParent(
        in Handle handle, 
        in NativeParallelMultiHashMap<int, Child> children,
        ref NativePool<Info> items,
        ref Info item, 
        out Command command)
    {
        if (!__Find(item.parentHandle.index, item.parentChildIndex, children, out var iterator, out _))
        {
            command = default;

            return false;
        }

        command.commandType = CommandType.Move;
        command.type = item.type;
        command.count = 0;
        command.sourceCount = item.count;
        command.sourceParentChildIndex = item.parentChildIndex;
        command.sourceParentHandle = item.parentHandle;
        command.sourceSiblingHandle = item.siblingHandle;
        command.sourceHandle = handle;

        item.parentHandle = Handle.Empty;
        item.parentChildIndex = -1;

        items[handle.index] = item;
        children.Remove(iterator);

        command.destinationCount = item.count;
        command.destinationParentChildIndex = -1;
        command.destinationParentHandle = Handle.Empty;
        command.destinationSiblingHandle = item.siblingHandle;
        command.destinationHandle = handle;

        return true;
    }

}

/*public struct GameItemManagerLite : IDisposable
{
    private NativeListLite<Type> __types;

    private NativeListLite<Command> __commands;

    private NativePoolLite<Info> __infos;
    private NativeMultiHashMapLite<int, Child> __children;

    private NativeMultiHashMapLite<int, int> __positiveFilters;
    private NativeMultiHashMapLite<int, int> __negativefilters;
    private NativeMultiHashMapLite<int, int> __fungibleItems;

    public AllocatorManager.AllocatorHandle allocator => __types.allocatar;

    public bool isCreated => __types.isCreated;

    public NativeArray<Command> commands => __commands.AsDeferredJobArray();

    public GameItemManager.ReadOnlyInfos readOnlyInfos => new GameItemManager.ReadOnlyInfos((NativePool<Info>)__infos);

    //public GameItemManager.Infos infos => new GameItemManager.Infos((NativePool<Info>)__infos);

    public GameItemManager.Hierarchy hierarchy => new GameItemManager.Hierarchy((NativePool<Info>)__infos, __children);

    public GameItemManagerLite(Allocator allocator, ref NativeListLite<Command> commands)
    {
        __commands = commands;

        __types = new NativeListLite<Type>(allocator);
        __infos = new NativePoolLite<Info>(1, allocator);
        __children = new NativeMultiHashMapLite<int, Child>(1, allocator);

        __positiveFilters = new NativeMultiHashMapLite<int, int>(1, allocator);
        __negativefilters = new NativeMultiHashMapLite<int, int>(1, allocator);

        __fungibleItems = new NativeMultiHashMapLite<int, int>(1, allocator);
    }

    public void Reset(Data[] datas)
    {
        int numItems = datas != null ? datas.Length : 0;

        __types.Resize(numItems, NativeArrayOptions.UninitializedMemory);
        int i, j, numFilters, numPositiveFilters = 0, numNegativefilters = 0, numFungibles = 0;
        Type destination;
        Data source;
        for (i = 0; i < numItems; ++i)
        {
            source = datas[i];

            destination.count = source.count;
            destination.capacity = source.capacity;

            __types[i] = destination;

            numFilters = source.filters == null ? 0 : source.filters.Length;
            if (source.isInvert)
                numNegativefilters += numFilters;
            else
                numPositiveFilters += numFilters;

            numFungibles += source.fungibles == null ? 0 : source.fungibles.Length;
        }

        __children.Clear();

        __positiveFilters.Clear();
        __negativefilters.Clear();
        __fungibleItems.Clear();
        for (i = 0; i < numItems; ++i)
        {
            source = datas[i];

            numFilters = source.filters == null ? 0 : source.filters.Length;
            if (source.isInvert)
            {
                for (j = 0; j < numFilters; ++j)
                    __negativefilters.Add(i, source.filters[j]);
            }
            else
            {
                for (j = 0; j < numFilters; ++j)
                    __positiveFilters.Add(i, source.filters[j]);
            }

            if(source.fungibles != null)
            {
                foreach (int fungible in source.fungibles)
                    __fungibleItems.Add(i, fungible);
            }
        }
    }

    public void Dispose()
    {
        __types.Dispose();

        __infos.Dispose();

        __children.Dispose();

        __positiveFilters.Dispose();

        __negativefilters.Dispose();

        __fungibleItems.Dispose();
    }

    public static implicit operator GameItemManager(GameItemManagerLite lite)
    {
        return new GameItemManager(
            lite.__types,
            lite.__commands,
            lite.__infos,
            lite.__children,
            lite.__positiveFilters,
            lite.__negativefilters,
            lite.__fungibleItems);
    }
}*/

public struct GameItemManagerShared : IDisposable
{
    [BurstCompile]
    private struct Flush : IJob
    {
        public bool isClear;

        public NativeList<Command> sources;

        public NativeList<Command> destinations;

        public void Execute()
        {
            if (isClear)
                destinations.Clear();
            else
                UnityEngine.Assertions.Assert.IsTrue(destinations.IsEmpty);

            destinations.AddRange(sources.AsArray());

            sources.Clear();
        }
    }

    private struct SharedData
    {
        public GameItemManager value;
        public LookupJobManager lookupJobManager;
    }

    private NativeList<Command> __commands;
    private NativeList<Command> __oldCommands;
    private unsafe SharedData* __data;

    public unsafe bool isCreated => __data != null && __data->value.isCreated;

    public unsafe ref LookupJobManager lookupJobManager => ref __data->lookupJobManager;

    public NativeArray<Command> oldCommands => __oldCommands.AsDeferredJobArray();

    public NativeArray<Command> commands => __commands.AsDeferredJobArray();

    public unsafe GameItemManager.ReadOnlyInfos readOnlyInfos => __data->value.readOnlyInfos;

    //public unsafe GameItemManager.Infos infos => __data->value.infos;

    public unsafe GameItemManager.Hierarchy hierarchy => __data->value.hierarchy;

    public unsafe GameItemManager value => __data->value;

    public unsafe GameItemManagerShared(
        ref NativeList<Command> commands,
        ref NativeList<Command> oldCommands,
        Allocator allocator)
    {
        __commands = commands;
        __oldCommands = oldCommands;
        __data = AllocatorManager.Allocate<SharedData>(allocator);
        __data->value = new GameItemManager(allocator, ref commands);
        __data->lookupJobManager = default;
    }

    public unsafe void Rebuild(Data[] datas)
    {
        lookupJobManager.CompleteReadWriteDependency();

        __data->value.Reset(datas);
    }

    public unsafe void Dispose()
    {
        lookupJobManager.CompleteReadWriteDependency();

        var allocator = __data->value.allocator;
        if (__data->value.isCreated)
            __data->value.Dispose();

        AllocatorManager.Free(allocator, __data);

        __data = null;
    }

    public GameItemManager AsReadOnly()
    {
        lookupJobManager.CompleteReadOnlyDependency();

        return value;
    }

    public GameItemManager AsReadWrite()
    {
        lookupJobManager.CompleteReadWriteDependency();

        return value;
    }

    public JobHandle ScheduleFlush(bool isClear, in JobHandle jobHandle)
    {
        Flush flush;
        flush.isClear = isClear;
        flush.sources = __commands;
        flush.destinations = __oldCommands;
        return flush.ScheduleByRef(jobHandle);
    }

    public JobHandle ScheduleParallelCommands<T>(ref T job, int innerloopBatchCount, in JobHandle inputDeps) where T : struct, IJobParallelForDefer
    {
        return job.ScheduleByRef(__commands, innerloopBatchCount, inputDeps);
    }

    public JobHandle ScheduleParallelOldCommands<T>(ref T job, int innerloopBatchCount, in JobHandle inputDeps) where T : struct, IJobParallelForDefer
    {
        return job.ScheduleByRef(__oldCommands, innerloopBatchCount, inputDeps);
    }
}

public static class GameItemUtility
{
    public static bool TryGetValue(this in NativePool<Info> items, in Handle handle, out Info item)
    {
        return items.TryGetValue(handle.index, out item) && handle.version == item.version;
    }

    public static bool TryGetValue(this in NativePool<Info>.Slice items, in Handle handle, out Info item)
    {
        return items.TryGetValue(handle.index, out item) && handle.version == item.version;
    }

    public static bool TryGetValue(this in NativePool<Info>.ReadOnlySlice items, in Handle handle, out Info item)
    {
        return items.TryGetValue(handle.index, out item) && handle.version == item.version;
    }
}
