using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using ZG;

[Flags]
public enum GameQuestGuideFlag
{
    Key = 0x01,
    Hide = 0x02
}

public enum GameQuestGuideVariantType
{
    Money,
    Entity,
    EntityToKill,
    EntityToOwn,
    Item,
    ItemToUse,
    ItemToEquip,
    Formula,
    FormulaToUse,
}

public interface IGameQuestGuideVariant
{
    GameQuestGuideVariantType type { get; }

    int id { get; }

    int count { get; }
}

public interface IGameQuestGuide<T> where T : IGameQuestGuideVariant
{
    string name { get; }

    GameQuestGuideFlag flag { get; }

    int priority { get; }

    T[] variants { get; }

    int[] childIndices { get; }
}

public struct GameQuestGuideManager
{
    [Flags]
    public enum CallbackType
    {
        Public = 0x01, 
        Global = 0x02
    }

    public struct Variant : IEquatable<Variant>
    {
        public GameQuestGuideVariantType type;

        public int id;

        public Variant(GameQuestGuideVariantType type, int id)
        {
            this.type = type;
            this.id = id;
        }

        public bool Equals(Variant other)
        {
            return type == other.type && id == other.id;
        }

        public override int GetHashCode()
        {
            return id ^ (int)type;
        }
    }

    public struct VariantSet
    {
        public Variant value;
        public int count;

        public int siblingIndex;
    }

    public struct Guide
    {
        public FixedString128Bytes name;

        public GameQuestGuideFlag flag;

        public int priority;

        public int variantSetIndex;

        public int siblingIndex;
        public int childIndex;
    }

    private struct Callback
    {
        public CallbackType type;
        public GameQuestGuideVariantType variantType;
        public int id;
        public CallbackHandle<int> handle;

        public bool isGlobal => (type & CallbackType.Global) == CallbackType.Global;

        public bool isPublished => (type & CallbackType.Public) == CallbackType.Public;
    }

    private struct CallbackResult
    {
        public int index;

        public CallbackHandle<int> handle;

        public void Invoke()
        {
            handle.Invoke(index);
        }
    }

    public struct ReadOnly
    {
        [ReadOnly]
        private NativePool<Guide> __guides;

        [ReadOnly]
        private NativeHashMap<int, int> __guideIndices;

        [ReadOnly]
        private NativeHashMap<Variant, int> __variants;

        [ReadOnly]
        private NativePool<VariantSet> __variantSets;

        public ReadOnly(in GameQuestGuideManager manager)
        {
            __guides = manager.__guides;
            __guideIndices = manager.__guideIndices;
            __variants = manager.__variants;
            __variantSets = manager.__variantSets;
        }

        public bool IsCompleted(int guideIndex)
        {
            return __guides.TryGetValue(guideIndex, out var guide) && __IsCompleted(guide.variantSetIndex);
        }

        public bool IsPublished(int guideIndex, GameQuestGuideVariantType variantType)
        {
            int priority = 0;
            return __guides.TryGetValue(guideIndex, out var guide) && __IsPublished(guide, variantType, ref priority);
        }

        public bool IsPublished(int guideIndex, in Variant variant)
        {
            int priority = 0;
            return __guides.TryGetValue(guideIndex, out var guide) && __IsPublished(guide, variant, ref priority);
        }

        public bool IsPublished(in Variant variant)
        {
            foreach (var guideIndex in __guideIndices)
            {
                if (IsPublished(guideIndex.Value, variant))
                    return true;
            }

            return false;
        }

        public bool IsPublished(GameQuestGuideVariantType variantType)
        {
            foreach (var guideIndex in __guideIndices)
            {
                if (IsPublished(guideIndex.Value, variantType))
                    return true;
            }

            return false;
        }

        public bool IsPublished(GameQuestGuideVariantType variantType, int id)
        {
            Variant variant;
            variant.type = variantType;
            variant.id = id;

            return IsPublished(variant);
        }

        private bool __Contains(int variantSetIndex, in GameQuestGuideVariantType variantType)
        {
            if (!__variantSets.TryGetValue(variantSetIndex, out var variantSet))
                return false;

            return variantSet.value.type == variantType || __Contains(variantSet.siblingIndex, variantType);
        }

        private bool __Contains(int variantSetIndex, in Variant variant)
        {
            if (!__variantSets.TryGetValue(variantSetIndex, out var variantSet))
                return false;

            return variantSet.value.Equals(variant) || __Contains(variantSet.siblingIndex, variant);
        }

        private bool __IsCompleted(int variantSetIndex)
        {
            if (!__variantSets.TryGetValue(variantSetIndex, out var variantSet))
                return false;

            if (__variants.TryGetValue(variantSet.value, out int temp) && temp >= variantSet.count)
                return true;

            return __IsCompleted(variantSet.siblingIndex);
        }

        private bool __IsChildCompleted(int guideIndex, ref int priority, out int siblingIndex, out Guide guide)
        {
            guide = __guides[guideIndex];
            siblingIndex = guide.siblingIndex;

            if (!__IsCompleted(guide.variantSetIndex))
                return false;

            if ((guide.flag & GameQuestGuideFlag.Key) == GameQuestGuideFlag.Key)
            {
                priority = Math.Max(priority, guide.priority);

                return true;
            }

            int childIndex = guide.childIndex;
            while (childIndex != -1)
            {
                if (!__IsChildCompleted(childIndex, ref priority, out childIndex, out _))
                    return false;
            }

            priority = Math.Max(priority, guide.priority);

            return true;
        }

        private bool __IsPublished(in Guide guide, in GameQuestGuideVariantType variantType, ref int priority)
        {
            bool isCompleted = __IsCompleted(guide.variantSetIndex);
            int childIndex = guide.childIndex;
            if (isCompleted)
            {
                if ((guide.flag & GameQuestGuideFlag.Key) == GameQuestGuideFlag.Key)
                    return false;

                if (priority < guide.priority)
                    priority = guide.priority;

                bool isChildCompleted = true;
                while (childIndex != -1)
                {
                    if (!__IsChildCompleted(childIndex, ref priority, out childIndex, out _))
                        isChildCompleted = false;
                }

                if (isChildCompleted)
                    return false;

                childIndex = guide.childIndex;
            }

            Guide child;
            if (!isCompleted && 
                __Contains(guide.variantSetIndex, variantType))
            {
                if ((guide.flag & GameQuestGuideFlag.Hide) == GameQuestGuideFlag.Hide)
                    return false;

                bool temp = true;
                while (childIndex != -1)
                {
                    if (!__IsChildCompleted(childIndex, ref priority, out childIndex, out child))
                    {
                        temp = false;
                        break;
                    }

                    if ((child.flag & GameQuestGuideFlag.Key) != GameQuestGuideFlag.Key &&
                        !__IsCompleted(child.variantSetIndex))
                    {
                        temp = false;
                        break;
                    }
                }

                if(temp)
                    return true;
                
                childIndex = guide.childIndex;
            }

            bool result = false;
            int maxPriority = priority, targetPriority;
            while (childIndex != -1)
            {
                child = __guides[childIndex];

                targetPriority = priority;
                if (__IsPublished(child, variantType, ref targetPriority))
                {
                    if (targetPriority >= maxPriority)
                    {
                        maxPriority = targetPriority;

                        result = true;
                    }
                }
                else if (targetPriority > maxPriority)
                {
                    maxPriority = targetPriority;

                    result = false;
                }

                childIndex = child.siblingIndex;
            }

            priority = maxPriority;

            return result;
        }

        private bool __IsPublished(in Guide guide, in Variant variant, ref int priority)
        {
            bool isCompleted = __IsCompleted(guide.variantSetIndex);
            int childIndex = guide.childIndex;
            if (isCompleted)
            {
                if ((guide.flag & GameQuestGuideFlag.Key) == GameQuestGuideFlag.Key)
                    return false;

                if (priority < guide.priority)
                    priority = guide.priority;

                bool isChildCompleted = true;
                while (childIndex != -1)
                {
                    if (!__IsChildCompleted(childIndex, ref priority, out childIndex, out _))
                        isChildCompleted = false;
                }

                if (isChildCompleted)
                    return false;

                childIndex = guide.childIndex;
            }

            Guide child;
            if (!isCompleted && __Contains(guide.variantSetIndex, variant))
            {
                if ((guide.flag & GameQuestGuideFlag.Hide) == GameQuestGuideFlag.Hide)
                    return false; 
                
                bool temp = true;
                int priorityTemp = priority;
                while (childIndex != -1)
                {
                    if (!__IsChildCompleted(childIndex, ref priorityTemp, out childIndex, out child))
                    {
                        temp = false;
                        break;
                    }

                    if ((child.flag & GameQuestGuideFlag.Key) != GameQuestGuideFlag.Key &&
                        !__IsCompleted(child.variantSetIndex))
                    {
                        temp = false;
                        break;
                    }
                }

                if (temp)
                {
                    priority = priorityTemp;
                    
                    return true;
                }

                childIndex = guide.childIndex;
            }

            bool result = false;
            int maxPriority = priority, targetPriority;
            while (childIndex != -1)
            {
                child = __guides[childIndex];

                targetPriority = priority;
                if (__IsPublished(child, variant, ref targetPriority))
                {
                    if (targetPriority >= maxPriority)
                    {
                        maxPriority = targetPriority;

                        result = true;
                    }
                }
                else if (targetPriority > maxPriority)
                {
                    maxPriority = targetPriority;

                    result = false;
                }

                childIndex = child.siblingIndex;
            }

            priority = maxPriority;

            return result;
        }

    }

    public struct ReadWrite
    {
        private ReadOnly __readOnly;

        [ReadOnly]
        private NativePool<Callback> __callbacks;

        private NativeArray<bool> __callbackStates;

        private NativeList<CallbackResult>.ParallelWriter __callbackResults;

        public ReadWrite(ref GameQuestGuideManager manager)
        {
            __readOnly = manager.readOnly;
            __callbacks = manager.__callbacks;

            //manager.__callbackStates.ResizeUninitialized(__callbacks.capacity);

            __callbackStates = manager.__callbackStates.AsArray();

            manager.__callbackResults.Capacity = math.max(manager.__callbackResults.Capacity, manager.__callbackResults.Length + __callbacks.length);
            __callbackResults = manager.__callbackResults.AsParallelWriter();
        }

        /*public void DispatchEvents()
        {
            Callback callback;
            CallbackResult callbackResult;
            foreach (var pair in __callbacks)
            {
                callback = pair.Value;
                if ((callback.isGlobal ? __readOnly.IsPublished(callback.variantType) : __readOnly.IsPublished(new Variant(callback.variantType, callback.id))) == callback.isPublished)
                {
                    callbackResult.index = pair.Key;
                    callbackResult.handle = callback.handle;

                    __callbackResults.AddNoResize(callbackResult);
                }
            }
        }*/

        public bool DispatchEvents(int index)
        {
            if (!__callbacks.TryGetValue(index, out var callback))
                return false;

            bool isPublished = callback.isGlobal
                ? __readOnly.IsPublished(callback.variantType)
                : __readOnly.IsPublished(new Variant(callback.variantType, callback.id));
            if (isPublished == callback.isPublished)
            {
                if (isPublished != __callbackStates[index])
                {
                    CallbackResult callbackResult;
                    callbackResult.index = index;
                    callbackResult.handle = callback.handle;

                    __callbackResults.AddNoResize(callbackResult);
                }
            }
            /*else
                __callbackStates[index] = true;*/

            __callbackStates[index] = isPublished;

            return true;
        }
    }

    [BurstCompile]
    private struct DispatchEventsJob : IJobParallelForDefer
    {
        public ReadWrite readWrite;

        public void Execute(int index)
        {
            readWrite.DispatchEvents(index);
        }
    }

    private NativePool<Guide> __guides;

    private NativeHashMap<int, int> __guideIndices;

    private NativeHashMap<Variant, int> __variants;

    private NativePool<VariantSet> __variantSets;

    private NativePool<Callback> __callbacks;

    private NativeList<bool> __callbackStates;

    private NativeList<CallbackResult> __callbackResults;

    public ReadOnly readOnly => new ReadOnly(this);

    public ReadWrite readWrite => new ReadWrite(ref this);

    public GameQuestGuideManager(in AllocatorManager.AllocatorHandle allocator)
    {
        __guides = new NativePool<Guide>(allocator);
        __guideIndices = new NativeHashMap<int, int>(1, allocator);
        __variants = new NativeHashMap<Variant, int>(1, allocator);
        __variantSets = new NativePool<VariantSet>(allocator);
        __callbacks = new NativePool<Callback>(allocator);
        __callbackStates = new NativeList<bool>(allocator);
        __callbackResults = new NativeList<CallbackResult>(allocator);
    }

    public void Dispose()
    {
        __guides.Dispose();
        __guideIndices.Dispose();
        __variants.Dispose();
        __variantSets.Dispose();
        __callbacks.Dispose();
        __callbackStates.Dispose();
        __callbackResults.Dispose();
    }

    public void InvokeAllResults()
    {
        foreach(var callbackResult in __callbackResults)
            callbackResult.Invoke();

        __callbackResults.Clear();
    }

    public void DispatchEvents()
    {
        var readOnly = this.readOnly;
        Callback callback;
        CallbackResult callbackResult;
        foreach (var pair in __callbacks)
        {
            callback = pair.Value;
            if ((callback.isGlobal ? readOnly.IsPublished(callback.variantType) : readOnly.IsPublished(new Variant(callback.variantType, callback.id))) == callback.isPublished)
            {
                callbackResult.index = pair.Key;
                callbackResult.handle = callback.handle;

                __callbackResults.Add(callbackResult);
            }
        }
    }

    public JobHandle DispatchEvents(
        int innerloopBatchCount,
        in JobHandle inputDeps)
    {
        DispatchEventsJob dispatchEventsJob;
        dispatchEventsJob.readWrite = readWrite;
        return __callbacks.ScheduleParallelForDefer(ref dispatchEventsJob, innerloopBatchCount, inputDeps);
    }

    public int Register(Action<int> value, int? id, GameQuestGuideVariantType variantType, bool isPublished)
    {
        Callback callback;
        callback.type = 0;
        if (isPublished)
            callback.type |= CallbackType.Public;

        if (id == null)
        {
            callback.type |= CallbackType.Global;

            callback.id = 0;
        }
        else
            callback.id = id.Value;

        callback.variantType = variantType;
        callback.handle = value.Register();

        int index = __callbacks.Add(callback);
        if (index >= __callbackStates.Length)
            __callbackStates.ResizeUninitialized(index + 1);

        __callbackStates[index] = !isPublished;

        return index;
    }

    public int Register(Action<int> value, GameQuestGuideVariantType variantType, bool isPublished)
    {
        return Register(value, null, variantType, isPublished);
    }

    public bool Unregister(int index)
    {
        if (__callbacks.TryGetValue(index, out var callback))
            callback.handle.Unregister();

        return __callbacks.RemoveAt(index);
    }

    public bool IsCompleted(int guideIndex)
    {
        return readOnly.IsCompleted(guideIndex);
    }

    public bool IsPublished(int guideIndex, GameQuestGuideVariantType variantType)
    {
        return readOnly.IsPublished(guideIndex, variantType);
    }

    public bool IsPublished(int guideIndex, in Variant variant)
    {
        return readOnly.IsPublished(guideIndex, variant);
    }

    public bool IsPublished(in Variant variant)
    {
        return readOnly.IsPublished(variant);
    }

    public bool IsPublished(GameQuestGuideVariantType variantType)
    {
        return readOnly.IsPublished(variantType);
    }

    public bool IsPublished(GameQuestGuideVariantType variantType, int id)
    {
        return readOnly.IsPublished(variantType, id);
    }

    public void Clear()
    {
        __guides.Clear();

        __guideIndices.Clear();

        __variants.Clear();

        __variantSets.Clear();

        __callbacks.Clear();

        __callbackResults.Clear();
    }

    public bool Clear(int questID)
    {
        if (__guideIndices.TryGetValue(questID, out int guideIndex) && __Delete(guideIndex, out _))
        {
            __guideIndices.Remove(questID);

            __MaskDirty();

            return true;
        }

        return false;
    }

    public int Publish<TVariant, TGuide>(in TGuide[] guides, int index, int siblingIndex)
        where TVariant : IGameQuestGuideVariant
        where TGuide : IGameQuestGuide<TVariant>
    {
        ref readonly var source = ref guides[index];

        VariantSet variantSet;
        variantSet.siblingIndex = -1;
        foreach (var variant in source.variants)
        {
            variantSet.value.type = variant.type;
            variantSet.value.id = variant.id;
            variantSet.count = variant.count;

            variantSet.siblingIndex = __variantSets.Add(variantSet);
        }

        Guide destination;
        destination.name = source.name;
        destination.flag = source.flag;
        destination.priority = source.priority;
        destination.variantSetIndex = variantSet.siblingIndex;
        destination.siblingIndex = siblingIndex;
        destination.childIndex = -1;

        if (source.childIndices != null)
        {
            foreach (var childIndex in source.childIndices)
                destination.childIndex = Publish<TVariant, TGuide>(guides, childIndex, destination.childIndex);
        }

        return __guides.Add(destination);
    }

    public void Publish<TVariant, TGuide>(int questID, int guideIndex, in TGuide[] guides)
        where TVariant : IGameQuestGuideVariant
        where TGuide : IGameQuestGuide<TVariant>
    {
        if (__guideIndices.ContainsKey(questID))
            return;

        guideIndex = Publish<TVariant, TGuide>(guides, guideIndex, -1);

        __guideIndices.Add(questID, guideIndex);
    }

    public void Add(in Variant variant, int count)
    {
        if (__variants.TryGetValue(variant, out int temp))
            count += temp;

        __variants[variant] = count;

        __MaskDirty();
    }

    public void Add(in GameQuestGuideVariantType variantType, int id, int count)
    {
        Variant variant;
        variant.type = variantType;
        variant.id = id;
        Add(variant, count);
    }

    public bool Remove(in Variant variant, int count)
    {
        if (__variants.TryGetValue(variant, out int temp))
        {
            if (temp > count)
            {
                __variants[variant] = temp - count;

                __MaskDirty();

                return true;
            }

            __variants.Remove(variant);

            __MaskDirty();

            return temp == count;
        }

        return false;
    }

    public void Remove(in GameQuestGuideVariantType variantType, int id, int count)
    {
        Variant variant;
        variant.type = variantType;
        variant.id = id;
        Remove(variant, count);
    }

    private bool __Delete(int guideIndex, out Guide guide)
    {
        if (!__guides.TryGetValue(guideIndex, out guide))
            return false;

        int variantSetIndex = guide.variantSetIndex;
        while (__variantSets.TryGetValue(variantSetIndex, out var variantSet))
        {
            __variantSets.RemoveAt(variantSetIndex);

            switch (variantSet.value.type)
            {
                case GameQuestGuideVariantType.EntityToKill:
                case GameQuestGuideVariantType.EntityToOwn:
                case GameQuestGuideVariantType.ItemToUse:
                case GameQuestGuideVariantType.FormulaToUse:
                    __variants.Remove(variantSet.value);
                    break;
            }

            variantSetIndex = variantSet.siblingIndex;
        }

        __guides.RemoveAt(guideIndex);

        Guide child;
        int childIndex = guide.childIndex;
        while (childIndex != -1)
            childIndex = __Delete(childIndex, out child) ? child.siblingIndex : -1;

        return true;
    }

    private void __MaskDirty()
    {
        //DispatchEvents();
    }
}

public struct GameQuestGuideManagerShared : IComponentData
{
    public readonly GameQuestGuideManager value;

    private UnsafeList<LookupJobManager> __lookupJobManager;

    public ref LookupJobManager lookupJobManager => ref __lookupJobManager.ElementAt(0);

    public bool isCreated => __lookupJobManager.IsCreated;

    public GameQuestGuideManagerShared(AllocatorManager.AllocatorHandle allocator)
    {
        value = new GameQuestGuideManager(allocator);

        __lookupJobManager = new UnsafeList<LookupJobManager>(1, allocator, NativeArrayOptions.UninitializedMemory);
        __lookupJobManager.Resize(1, NativeArrayOptions.ClearMemory);
    }

    public void Dispsoe()
    {
        lookupJobManager.CompleteReadWriteDependency();

        __lookupJobManager.Dispose();

        value.Dispose();
    }
}

[BurstCompile, UpdateInGroup(typeof(PresentationSystemGroup))]
public partial struct GameQuestGuideSystem : ISystem
{
    public static readonly int InnerloopBatchCount = 1;

    public GameQuestGuideManagerShared manager
    {
        get;

        private set;
    }

    public void OnCreate(ref SystemState state)
    {
        manager = new GameQuestGuideManagerShared(Allocator.Persistent);

        state.EntityManager.AddComponentData(state.SystemHandle, manager);
    }

    public void OnDestroy(ref SystemState state)
    {
        manager.Dispsoe();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        ref var lookupJobManager = ref manager.lookupJobManager;

        var jobHandle = manager.value.DispatchEvents(
            InnerloopBatchCount, 
            JobHandle.CombineDependencies(lookupJobManager.readWriteJobHandle, state.Dependency));

        lookupJobManager.readWriteJobHandle = jobHandle;

        state.Dependency = jobHandle;
    }
}

[UpdateInGroup(typeof(CallbackSystemGroup)), 
    WorldSystemFilter(WorldSystemFilterFlags.Presentation)]
public partial class GameQuestGuideCallbackSystem : SystemBase
{
    protected override void OnUpdate()
    {
        var manager = SystemAPI.GetSingleton<GameQuestGuideManagerShared>();
        manager.lookupJobManager.CompleteReadWriteDependency();
        manager.value.InvokeAllResults();
    }
}