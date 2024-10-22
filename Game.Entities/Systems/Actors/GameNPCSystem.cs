using System;
using System.Diagnostics;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using ZG;
using ZG.Mathematics;
using ZG.Unsafe;
using Math = ZG.Mathematics.Math;

public struct GameNPCBounds : IComponentData
{
    public uint layerMask;

    public float3 position;
    
    public float3 min;
    public float3 max;
}

public struct GameNPCWorld
{
    private struct NPC
    {
        public int stageIndex;
        //public int layer;

        public float3 position;
        public quaternion rotation;
        public NativeQuadTreeItem<int> quadTreeItem;
    }

    /*private struct NPCIndex : IEquatable<NPCIndex>
    {
        public int value;
        public uint layerMask;

        public NPCIndex(KVPair<int, uint> keyValue)
        {
            value = keyValue.Key;
            layerMask = keyValue.Value;
        }

        public bool Equals(NPCIndex other)
        {
            return value == other.value && (layerMask & other.layerMask) != 0;
        }

        public override int GetHashCode()
        {
            return value;
        }
    }*/

    private struct Filter : INativeQuadTreeFilter<int>
    {
        public NativeList<int> npcIndices;

        public bool Predicate(
            in float3 min,
            in float3 max,
            in int value)
        {
            npcIndices.Add(value);
            
            return false;
        }
    }
    
    private struct Wrapper : ILandscapeWrapper<int>, IDisposable
    {
        public readonly int Layer;
        private UnsafePool<NPC> __npcs;
        private NativeArray<GameNPCBounds> __bounds;
        private NativeArray<int>.Enumerator __npcIndices;

        public int Current => __npcIndices.Current;

        public Wrapper(
            int layer, 
            in UnsafePool<NPC> npcs, 
            in NativeArray<GameNPCBounds> bounds, 
            in NativeArray<int> npcIndices)
        {
            Layer = layer;
            __npcs = npcs;
            __bounds = bounds;
            __npcIndices = npcIndices.GetEnumerator();
        }

        public void Dispose()
        {
            __npcIndices.Dispose();
        }

        public bool MoveNext()
        {
            while (__npcIndices.MoveNext())
            {
                if (__npcs[__npcIndices.Current].quadTreeItem.layer == Layer)
                    return true;
            }

            return false;
        }

        public float DistanceTo(in int npcIndex)
        {
            float minLengthSQ = float.MaxValue;
            foreach (var bound in __bounds)
            {
                if((bound.layerMask & (1 << __npcs[npcIndex].quadTreeItem.layer)) == 0)
                    continue;
                
                minLengthSQ = math.min(minLengthSQ, math.distancesq(bound.position, __npcs[npcIndex].position));
            }

            return minLengthSQ;
        }
    }

    private UnsafePool<NPC> __npcs;
    private UnsafeList<int> __activeIndices;
    private UnsafeQuadTree<int> __quadTree;
    private LandscapeWorld<int> __world;

    public bool isVail => __quadTree.isCreated;

    public readonly AllocatorManager.AllocatorHandle allocator => __activeIndices.Allocator;

    public int countToLoad => __world.countToLoad;

    public int countToUnload => __world.countToUnload;

    public NativeArray<int> activeIndices => __activeIndices.AsArray();

    public GameNPCWorld(int layers, in float3 min, in float3 max, in AllocatorManager.AllocatorHandle allocator)
    {
        __activeIndices = new UnsafeList<int>(1, allocator);
        
        __npcs = new UnsafePool<NPC>(1, allocator);

        __quadTree = new UnsafeQuadTree<int>(layers, min, max, allocator);

        __world = new LandscapeWorld<int>(allocator);
    }

    public GameNPCWorld(in AllocatorManager.AllocatorHandle allocator)
    {
        __activeIndices = new UnsafeList<int>(1, allocator);
        
        __npcs = new UnsafePool<NPC>(1, allocator);

        __quadTree = default;

        __world = default;
    }
    
    public void Dispose()
    {
        __activeIndices.Dispose();
        __npcs.Dispose();
        
        if(__quadTree.isCreated)
            __quadTree.Dispose();
        
        if(__world.isCreated)
            __world.Dispose();
    }

    public void Reset(int layers, in float3 min, in float3 max)
    {
        __activeIndices.Clear();
        __npcs.Clear();
        
        var allocator = this.allocator;
        
        if(__quadTree.isCreated)
            __quadTree.Dispose();
        
        __quadTree = new UnsafeQuadTree<int>(layers, min, max, allocator);

        if (__world.isCreated)
            __world.Reset(layers);
        else
            __world = new LandscapeWorld<int>(allocator, layers);
    }

    public bool Contains(int index) => __npcs.ContainsKey(index);

    public int GetIndexByStage(int index)
    {
        foreach (var npc in __npcs)
        {
            if (npc.Value.stageIndex == index)
                return npc.Key;
        }

        return -1;
    }

    public int GetStageIndex(int index) => __npcs.TryGetValue(index, out var npc) ? npc.stageIndex : -1;

    public float3 GetPosition(int index) => __npcs[index].position;

    public bool GetPositionAndRotation(int index, out float3 position, out quaternion rotation)
    {
        if (!__npcs.TryGetValue(index, out var npc))
        {
            position = float3.zero;
            rotation = quaternion.identity;

            return false;
        }

        position = npc.position;
        rotation = npc.rotation;

        return true;
    }

    public void Set(
        int index, 
        int stageIndex, 
        int layer, 
        in quaternion rotation, 
        in float3 position, 
        in float3 min, in float3 max)
    {
        if (__npcs.TryGetValue(index, out var npc))
            __quadTree.Remove(npc.quadTreeItem);
        
        var center = position + (max + min) * 0.5f;
        var box = new Box(center, (max - min) * 0.5f, rotation);
        var worldExtends = box.worldExtents;
        
        npc.stageIndex = stageIndex;
        npc.rotation = rotation;
        npc.position = position;
        npc.quadTreeItem = __quadTree.Add(layer, center - worldExtends, center + worldExtends, index);

        __npcs.Insert(index, npc);
    }

    public bool Move(
        int index, 
        int stageIndex, 
        in quaternion rotation, 
        in float3 position, 
        out int originStageIndex)
    {
        originStageIndex = -1;
        if (__npcs.TryGetValue(index, out var npc))
        {
            npc.quadTreeItem.Get(out _, out float3 min, out float3 max, out int layer);

            if (__quadTree.Remove(npc.quadTreeItem))
            {
                originStageIndex = npc.stageIndex;
                
                npc.stageIndex = stageIndex;
                
                float3 offset = position - npc.position;
                min += offset;
                max += offset;
                var center = (min + max) * 0.5f;
                var box = new Box(center, (max - min) * 0.5f, math.mul(rotation, math.inverse(npc.rotation)));
                var worldExtends = box.worldExtents;

                npc.position = position;
                npc.rotation = rotation;

                npc.quadTreeItem = __quadTree.Add(layer, center - worldExtends, center + worldExtends, index);

                __npcs[index] = npc;

                return true;
            }
        }

        return false;
    }

    public bool Inactive(int index)
    {
        int temp = __activeIndices.IndexOf(index);
        if (temp == -1)
            return false;
        
        __activeIndices.RemoveAt(temp);

        return true;
    }

    public bool Active(int index)
    {
        if (__activeIndices.Contains(index))
            return false;
        
        __activeIndices.Add(index);

        return true;
    }

    public void Apply(in NativeArray<GameNPCBounds> bounds)
    {
        var npcIndices = new NativeList<int>(Allocator.Temp);
        
        npcIndices.AddRange(__activeIndices.AsArray());

        Filter filter;
        filter.npcIndices = npcIndices;

        foreach (var bound in bounds)
            __quadTree.SearchAll(ref filter, bound.min, bound.max, bound.layerMask);

        uint layerMask = 0;
        foreach (var npcIndex in npcIndices)
            layerMask |= 1u << __npcs[npcIndex].quadTreeItem.layer;
        
        if (layerMask == 0)
            return;

        Wrapper wrapper;
        int min = Math.GetLowerstBit(layerMask), max = Math.GetHighestBit(layerMask);
        for (int i = min - 1; i < max; ++i)
        {
            wrapper = new Wrapper(i, __npcs, bounds, npcIndices.AsArray());
            __world.Apply(i, ref wrapper);
            wrapper.Dispose();
        }

        npcIndices.Dispose();
    }

    public int GetCountToLoad(int layer, float minDistance = float.MinValue)
    {
        return __world.GetCountToLoad(layer, minDistance);
    }

    public int GetCountToUnload(int layer, float maxDistance = float.MaxValue)
    {
        return __world.GetCountToUnload(layer, maxDistance);
    }

    public float GetMaxDistanceToUnload(out int layer, out int npcIndex, float maxDistance = float.MinValue)
    {
        return __world.GetMaxDistanceToUnload(out layer, out npcIndex, maxDistance);
    }

    public float GetMinDistanceToLoad(out int layer, out int npcIndex, float minDistance = int.MaxValue)
    {
        return __world.GetMinDistanceToLoad(out layer, out npcIndex, minDistance);
    }

    public bool Load(int layer, out int npcIndex, float minDistance = float.MaxValue)
    {
        return __world.Load(layer, out npcIndex, minDistance);
    }
    
    public bool Load(out int layer, out int npcIndex, float minDistance = float.MaxValue)
    {
        return __world.Load(out layer, out npcIndex, minDistance);
    }
    
    public bool Unload(int layer, out int npcIndex, float minDistance = float.MaxValue)
    {
        return __world.Unload(layer, out npcIndex, minDistance);
    }
    
    public bool Unload(out int layer, out int npcIndex, float minDistance = float.MaxValue)
    {
        return __world.Unload(out layer, out npcIndex, minDistance);
    }
    
    public LandscapeLoaderCompleteType Complete(bool isLoading, int layer, in int npcIndex)
    {
        return __world.Complete(isLoading, layer, npcIndex);
    }
}

public struct GameNPCWorldShared : ILandscapeWorld<int>
{
    private struct Data
    {
        public GameNPCWorld instance;

        public LookupJobManager lookupJobManager;
    }

    [NativeContainer]
    public struct Writer
    {
        [NativeDisableUnsafePtrRestriction]
        private unsafe GameNPCWorld* __world;
        
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        internal AtomicSafetyHandle m_Safety;
#endif

        public unsafe Writer(ref GameNPCWorldShared instance)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            m_Safety = instance.m_Safety;
#endif

            __world = &instance.__data->instance;
        }

        public unsafe void Apply(in NativeArray<GameNPCBounds> bounds)
        {
            __CheckWrite();
            __world->Apply(bounds);
        }
        
        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        void __CheckWrite()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif
        }
    }

    private unsafe Data* __data;
    
#if ENABLE_UNITY_COLLECTIONS_CHECKS
    internal AtomicSafetyHandle m_Safety;
#endif

    public unsafe bool isCreated => __data != null;

    public unsafe bool isVail => __data->instance.isVail;

    public unsafe int countToLoad
    {
        get
        {
            lookupJobManager.CompleteReadOnlyDependency();

            __CheckRead();

            return __data->instance.countToLoad;
        }
    }

    public unsafe int countToUnload
    {
        get
        {
            lookupJobManager.CompleteReadOnlyDependency();

            __CheckRead();

            return __data->instance.countToUnload;
        }
    }

    public unsafe NativeArray<int> activeIndices
    {
        get
        {
            lookupJobManager.CompleteReadOnlyDependency();

            __CheckRead();

            var activeIndices = __data->instance.activeIndices;
            
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref activeIndices, m_Safety);
#endif

            return activeIndices;
        }
    }

    public unsafe ref LookupJobManager lookupJobManager => ref __data->lookupJobManager;

    public Writer writer => new Writer(ref this);

    public unsafe GameNPCWorldShared(in AllocatorManager.AllocatorHandle allocator)
    {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        m_Safety = CollectionHelper.CreateSafetyHandle(allocator);
#endif

        __data = AllocatorManager.Allocate<Data>(allocator);
        __data->instance = new GameNPCWorld(allocator);

        __data->lookupJobManager = default;
    }

    public unsafe void Dispose()
    {
        lookupJobManager.CompleteReadWriteDependency();
        
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        CollectionHelper.DisposeSafetyHandle(ref m_Safety);
#endif

        var allocator = __data->instance.allocator;
        __data->instance.Dispose();
        
        AllocatorManager.Free(allocator, __data);

        __data = null;
    }

    public unsafe void Reset(int layers, in float3 min, in float3 max)
    {
        lookupJobManager.CompleteReadWriteDependency();

        __CheckWrite();

        __data->instance.Reset(layers, min, max);
    }

    public unsafe bool Contains(int index)
    {
        lookupJobManager.CompleteReadOnlyDependency();

        __CheckRead();

        return __data->instance.Contains(index);
    }

    public unsafe int GetIndexByStage(int index)
    {
        lookupJobManager.CompleteReadOnlyDependency();

        __CheckRead();

        return __data->instance.GetIndexByStage(index);
    }

    public unsafe int GetStageIndex(int index)
    {
        lookupJobManager.CompleteReadOnlyDependency();

        __CheckRead();

        return __data->instance.GetStageIndex(index);
    }

    public unsafe float3 GetPosition(int index)
    {
        lookupJobManager.CompleteReadOnlyDependency();

        __CheckRead();

        return __data->instance.GetPosition(index);
    }

    public unsafe bool GetPositionAndRotation(int index, out float3 position, out quaternion rotation)
    {
        lookupJobManager.CompleteReadOnlyDependency();

        __CheckRead();

        return __data->instance.GetPositionAndRotation(index, out position, out rotation);
    }

    public unsafe void Set(
        int index, 
        int stageIndex, 
        int layer, 
        in quaternion rotation, 
        in float3 position, 
        in float3 min, 
        in float3 max)
    {
        lookupJobManager.CompleteReadWriteDependency();

        __CheckWrite();
        
        __data->instance.Set(
            index, 
            stageIndex, 
            layer, 
            rotation, 
            position, 
            min, 
            max);
    }

    public unsafe bool Move(
        int index, 
        int stageIndex, 
        in quaternion rotation, 
        in float3 position, 
        out int originStageIndex)
    {
        lookupJobManager.CompleteReadWriteDependency();

        __CheckWrite();
        
        return __data->instance.Move(
            index, 
            stageIndex, 
            rotation, 
            position, 
            out originStageIndex);
    }

    public unsafe bool Inactive(int index)
    {
        lookupJobManager.CompleteReadWriteDependency();

        __CheckWrite();

        return __data->instance.Inactive(index);
    }

    public unsafe bool Active(int index)
    {
        lookupJobManager.CompleteReadWriteDependency();

        __CheckWrite();
        
        return __data->instance.Active(index);
    }

    public unsafe int GetCountToLoad(int layer, float minDistance = float.MinValue)
    {
        lookupJobManager.CompleteReadOnlyDependency();

        __CheckRead();

        return __data->instance.GetCountToLoad(layer, minDistance);
    }

    public unsafe int GetCountToUnload(int layer, float maxDistance = float.MaxValue)
    {
        lookupJobManager.CompleteReadOnlyDependency();

        __CheckRead();

        return __data->instance.GetCountToUnload(layer, maxDistance);
    }

    public unsafe float GetMaxDistanceToUnload(out int layer, out int npcIndex, float maxDistance = float.MinValue)
    {
        lookupJobManager.CompleteReadOnlyDependency();

        __CheckRead();

        return __data->instance.GetMaxDistanceToUnload(out layer, out npcIndex, maxDistance);
    }

    public unsafe float GetMinDistanceToLoad(out int layer, out int npcIndex, float minDistance = int.MaxValue)
    {
        lookupJobManager.CompleteReadOnlyDependency();

        __CheckRead();

        return __data->instance.GetMinDistanceToLoad(out layer, out npcIndex, minDistance);
    }

    public unsafe bool Load(int layer, out int npcIndex, float minDistance = float.MaxValue)
    {
        lookupJobManager.CompleteReadWriteDependency();

        __CheckWrite();

        return __data->instance.Load(layer, out npcIndex, minDistance);
    }
    
    public unsafe bool Load(out int layer, out int npcIndex, float minDistance = float.MaxValue)
    {
        lookupJobManager.CompleteReadWriteDependency();

        __CheckWrite();

        return __data->instance.Load(out layer, out npcIndex, minDistance);
    }
    
    public unsafe bool Unload(int layer, out int npcIndex, float minDistance = float.MaxValue)
    {
        lookupJobManager.CompleteReadWriteDependency();

        __CheckWrite();

        return __data->instance.Unload(layer, out npcIndex, minDistance);
    }
    
    public unsafe bool Unload(out int layer, out int npcIndex, float minDistance = float.MaxValue)
    {
        lookupJobManager.CompleteReadWriteDependency();

        __CheckWrite();

        return __data->instance.Unload(out layer, out npcIndex, minDistance);
    }

    public unsafe LandscapeLoaderCompleteType Complete(bool isLoading, int layer, in int npcIndex)
    {
        
        lookupJobManager.CompleteReadWriteDependency();

        __CheckWrite();

        return __data->instance.Complete(isLoading, layer, npcIndex);
    }

    [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
    void __CheckRead()
    {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif
    }
    
    [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
    void __CheckWrite()
    {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif
    }
}

[BurstCompile]
public partial struct GameNPCWorldSystem : ISystem
{
    [BurstCompile]
    private struct Reset : IJob
    {
        [ReadOnly]
        public NativeArray<int> baseEntityIndexArray;
        public NativeList<GameNPCBounds> bounds;

        public void Execute()
        {
            bounds.Resize(baseEntityIndexArray[baseEntityIndexArray.Length - 1] + 1, NativeArrayOptions.UninitializedMemory);
        }
    }
    
    private struct Collect
    {
        [ReadOnly]
        public NativeArray<GameNPCBounds> bounds;

        [ReadOnly]
        public NativeArray<LocalToWorld> localToWorlds;

        public GameNPCBounds Execute(int index)
        {
            var bounds = this.bounds[index];
            var localToWorld = this.localToWorlds[index];
            var box = new Box(
                    (bounds.max + bounds.min) * 0.5f,
                    (bounds.max - bounds.min) * 0.5f,
                    localToWorld.Value);

            float3 center = box.center, extends = box.worldExtents;

            bounds.min = center - extends;
            bounds.max = center + extends;
            bounds.position = math.transform(localToWorld.Value, bounds.position);
            
            return bounds;
        }
    }

    [BurstCompile]
    private struct CollectEx : IJobChunk
    {
        [ReadOnly]
        public ComponentTypeHandle<GameNPCBounds> boundsType;

        [ReadOnly]
        public ComponentTypeHandle<LocalToWorld> localToWorldType;

        [ReadOnly] 
        public NativeArray<int> baseEntityIndexArray;

        [NativeDisableParallelForRestriction]
        public NativeArray<GameNPCBounds> results;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in  v128 chunkEnabledMask)
        {
            int entityIndex = baseEntityIndexArray[unfilteredChunkIndex];
            
            Collect collect;
            collect.bounds = chunk.GetNativeArray(ref boundsType);
            collect.localToWorlds = chunk.GetNativeArray(ref localToWorldType);

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                results[entityIndex++] = collect.Execute(i);
        }
    }
    
    [BurstCompile]
    private struct Apply : IJob
    {
        [ReadOnly]
        public NativeArray<GameNPCBounds> bounds;
        
        public GameNPCWorldShared.Writer world;

        public void Execute()
        {
            world.Apply(bounds);
        }
    }

    private EntityQuery __group;

    private ComponentTypeHandle<GameNPCBounds> __boundsType;

    private ComponentTypeHandle<LocalToWorld> __localToWorldType;

    private NativeList<GameNPCBounds> __bounds;

    public GameNPCWorldShared world
    {
        get;

        private set;
    }

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __group = builder
                .WithAll<GameNPCBounds>()
                .Build(ref state);
        
        state.RequireForUpdate(__group);

        __boundsType = state.GetComponentTypeHandle<GameNPCBounds>(true);
        
        __localToWorldType = state.GetComponentTypeHandle<LocalToWorld>(true);

        __bounds = new NativeList<GameNPCBounds>(Allocator.Persistent);
        world = new GameNPCWorldShared(Allocator.Persistent);
    }

    //[BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        __bounds.Dispose();
        world.Dispose();
    }
    
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var world = this.world;
        if (!world.isVail)
            return;

        var worldUpdateAllocator = state.WorldUpdateAllocator;

        var baseEntityIndexArray = __group.CalculateBaseEntityIndexArrayAsync(worldUpdateAllocator, state.Dependency, out var jobHandle);

        Reset reset;
        reset.baseEntityIndexArray = baseEntityIndexArray;
        reset.bounds = __bounds;
        jobHandle = reset.ScheduleByRef(jobHandle);

        var bounds = __bounds.AsDeferredJobArray();

        CollectEx collect;
        collect.boundsType = __boundsType.UpdateAsRef(ref state);
        collect.localToWorldType = __localToWorldType.UpdateAsRef(ref state);
        collect.baseEntityIndexArray = baseEntityIndexArray;
        collect.results = bounds;
        jobHandle = collect.ScheduleParallelByRef(__group, jobHandle);
        
        Apply apply;
        apply.world = world.writer;
        apply.bounds = bounds;

        ref var worldJobManager = ref world.lookupJobManager;

        jobHandle = apply.ScheduleByRef(JobHandle.CombineDependencies(worldJobManager.readWriteJobHandle, jobHandle));

        worldJobManager.readWriteJobHandle = jobHandle;

        state.Dependency = jobHandle;
    }
}
