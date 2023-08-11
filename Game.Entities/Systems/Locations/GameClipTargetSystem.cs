using System;
using System.Collections.Generic;
using Unity.Jobs;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Transforms;
using Unity.Rendering;
using UnityEngine;
using ZG;
using ZG.Unsafe;

public partial class GameLocationClipTargetWeightSystem : SystemBase, IEntityCommandProducerJob
{
    [BurstCompile]
    private struct Count : IJobChunk
    {
        [NativeDisableContainerSafetyRestriction]
        public NativeArray<int> counter;

        [ReadOnly]
        public BufferTypeHandle<GameNodeCharacterDistanceHit> distanceHitType;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            var distanceHits = chunk.GetBufferAccessor(ref distanceHitType);

            int count = 0;
            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                count += distanceHits[i].Length;

            counter.Add(0, count);
        }
    }

    [BurstCompile]
    private struct Clear : IJob
    {
        [ReadOnly, DeallocateOnJobCompletion]
        public NativeArray<int> counter;

        [ReadOnly]
        public SharedMultiHashMap<Entity, EntityData<int>>.Reader childIndices;

        [ReadOnly]
        public SharedMultiHashMap<Entity, EntityData<int>>.Reader parentIndices;

        public NativeParallelHashSet<Entity> entities;

        public void Execute()
        {
            entities.Clear();

            entities.Capacity = math.max(entities.Capacity, counter[0] + childIndices.Count() + parentIndices.Count());
        }
    }

    private struct Visible
    {
        public float changedWeightValue;

        public float3 forward;

        [ReadOnly]
        public ComponentLookup<GameClipTargetData> instances;

        [ReadOnly]
        public ComponentLookup<Translation> translationMap;

        [ReadOnly]
        public NativeArray<Translation> translations;

        [ReadOnly]
        public BufferAccessor<GameNodeCharacterDistanceHit> distanceHits;

        [ReadOnly]
        public SharedMultiHashMap<Entity, EntityData<int>>.Reader childIndices;

        [ReadOnly]
        public SharedMultiHashMap<Entity, EntityData<int>>.Reader parentIndices;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<GameClipTargetWeight> weights;

        public EntityCommandQueue<Entity>.ParallelWriter entityManager;

        public EntityCommandQueue<CallbackHandle>.ParallelWriter callbackHandles;

        public NativeParallelHashSet<Entity>.ParallelWriter entities;

        public void Execute(in float3 targetPosition, in Entity entity)
        {
            if (!instances.HasComponent(entity) || !entities.Add(entity))
                return;

            var instance = instances[entity];
            var position = translationMap[entity].Value;

            if (position.y + instance.height > targetPosition.y && 
                math.dot(targetPosition - position, forward) > 0.0f)
            {
                if (weights.HasComponent(entity))
                {
                    var weight = weights[entity];
                    weight.value = math.min(weight.value + changedWeightValue, 1.0f) + changedWeightValue;
                    weights[entity] = weight;
                }
                else
                {
                    entityManager.Enqueue(entity);

                    callbackHandles.Enqueue(instance.visibleCallback);
                }
            }
        }

        public void Execute(int index)
        {
            var distanceHits = this.distanceHits[index];
            GameContainerEnumerator enumerator;
            Entity entity;
            float3 position = translations[index].Value;
            int numDistanceHits = distanceHits.Length;
            for(int i = 0; i < numDistanceHits; ++i)
            {
                entity = distanceHits[i].value.Entity;
                Execute(position, entity);

                enumerator = new GameContainerEnumerator(entity, parentIndices, childIndices);
                while (enumerator.MoveNext())
                    Execute(position, enumerator.Current);
            }
        }
    }

    [BurstCompile]
    private struct VisibleEx : IJobChunk, IEntityCommandProducerJob
    {
        public float changedWeightValue;

        public float3 forward;

        [ReadOnly]
        public ComponentLookup<GameClipTargetData> instances;

        [ReadOnly]
        public ComponentLookup<Translation> translations;

        [ReadOnly]
        public ComponentLookup<Rotation> rotations;

        [ReadOnly]
        public ComponentTypeHandle<Translation> translationType;

        [ReadOnly]
        public BufferTypeHandle<GameNodeCharacterDistanceHit> distanceHitType;

        [ReadOnly]
        public SharedMultiHashMap<Entity, EntityData<int>>.Reader childIndices;

        [ReadOnly]
        public SharedMultiHashMap<Entity, EntityData<int>>.Reader parentIndices;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<GameClipTargetWeight> weights;

        public EntityCommandQueue<Entity>.ParallelWriter entityManager;

        public EntityCommandQueue<CallbackHandle>.ParallelWriter callbackHandles;

        public NativeParallelHashSet<Entity>.ParallelWriter entities;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Visible visible;
            visible.changedWeightValue = changedWeightValue;
            visible.forward = forward;
            visible.instances = instances;
            visible.translationMap = translations;
            visible.translations = chunk.GetNativeArray(ref translationType);
            visible.distanceHits = chunk.GetBufferAccessor(ref distanceHitType);
            visible.childIndices = childIndices;
            visible.parentIndices = parentIndices;
            visible.weights = weights;
            visible.entityManager = entityManager;
            visible.callbackHandles = callbackHandles;
            visible.entities = entities;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                visible.Execute(i);
        }
    }

    private struct Invisible
    {
        public float changedWeightValue;

        [ReadOnly]
        public NativeArray<Entity> entityArray;

        [ReadOnly]
        public NativeArray<GameClipTargetData> instances;

        public NativeArray<GameClipTargetWeight> weights;

        public EntityCommandQueue<Entity>.ParallelWriter entityManager;

        public EntityCommandQueue<CallbackHandle>.ParallelWriter callbackHandles;

        public void Execute(int index)
        {
            var weight = weights[index];

            weight.value -= changedWeightValue;
            if(weight.value < 0.0f)
            {
                weight.value = 0.0f;

                entityManager.Enqueue(entityArray[index]);

                callbackHandles.Enqueue(instances[index].invisibleCallback);
            }

            weights[index] = weight;
        }
    }

    [BurstCompile]
    private struct InvisibleEx : IJobChunk, IEntityCommandProducerJob
    {
        public float changedWeightValue;

        [ReadOnly]
        public EntityTypeHandle entityType;

        [ReadOnly]
        public ComponentTypeHandle<GameClipTargetData> instanceType;

        public ComponentTypeHandle<GameClipTargetWeight> weightType;

        public EntityCommandQueue<Entity>.ParallelWriter entityManager;

        public EntityCommandQueue<CallbackHandle>.ParallelWriter callbackHandles;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Invisible invisible;
            invisible.changedWeightValue = changedWeightValue;
            invisible.entityArray = chunk.GetNativeArray(entityType);
            invisible.instances = chunk.GetNativeArray(ref instanceType);
            invisible.weights = chunk.GetNativeArray(ref weightType);
            invisible.entityManager = entityManager;
            invisible.callbackHandles = callbackHandles;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                invisible.Execute(i);
        }
    }

    public float weightSpeed = 1f;
    private Camera __camera;
    private SharedMultiHashMap<Entity, EntityData<int>> __parentIndices;
    private SharedMultiHashMap<Entity, EntityData<int>> __childIndices;
    private NativeParallelHashSet<Entity> __entities;
    private EntityQuery __visibleGroup;
    private EntityQuery __invisibleGroup;
    private EntityCommandPool<Entity> __visibleCommander;
    private EntityCommandPool<Entity> __invisibleCommander;
    private EntityCommandPool<CallbackHandle> __callbackCommander;

    protected override void OnCreate()
    {
        base.OnCreate();

        World world = World;
        __entities = new NativeParallelHashSet<Entity>(1, Allocator.Persistent);
        ref var containerChildSystem = ref world.GetOrCreateSystemUnmanaged<GameContainerChildSystem>();
        __parentIndices = containerChildSystem.parentIndices;
        __childIndices = containerChildSystem.childIndices;

        __visibleGroup = GetEntityQuery(ComponentType.ReadOnly<GameLocator>(), ComponentType.ReadOnly<GameNodeCharacterDistanceHit>());
        __invisibleGroup = GetEntityQuery(ComponentType.ReadWrite<GameClipTargetWeight>());
        __visibleCommander = world.GetOrCreateSystemManaged<GameClipTargetRenderSystem>().visibleCommander;
        __invisibleCommander = world.GetOrCreateSystemManaged<GameClipTargetRenderSystem>().invisibleCommander;
        __callbackCommander = world.GetOrCreateSystemManaged<GameClipTargetRenderSystem>().callbackCommander;
    }

    protected override void OnDestroy()
    {
        __entities.Dispose();

        base.OnDestroy();
    }

    protected override void OnUpdate()
    {
        if (__camera == null)
        {
            __camera = Camera.main;
            if (__camera == null)
                return;
        }

        float changedWeightValue = weightSpeed * World.Time.DeltaTime;
        var jobHandle = Dependency;
        var callbackCommander = __callbackCommander.Create();
        var callbackCommanderWriter = callbackCommander.parallelWriter;
        if (!__visibleGroup.IsEmptyIgnoreFilter)
        {
            var distanceHitType = GetBufferTypeHandle<GameNodeCharacterDistanceHit>(true);
            var counter = new NativeArray<int>(1, Allocator.TempJob, NativeArrayOptions.ClearMemory);

            Count count;
            count.counter = counter;
            count.distanceHitType = distanceHitType;
            jobHandle = count.ScheduleParallel(__visibleGroup, jobHandle);

            ref var childIndicesJobManager = ref __childIndices.lookupJobManager;
            ref var parentIndicesJobManager = ref __parentIndices.lookupJobManager;

            var childIndices = __childIndices.reader;
            var parentIndices = __parentIndices.reader;

            Clear clear;
            clear.counter = counter;
            clear.childIndices = childIndices;
            clear.parentIndices = parentIndices;
            clear.entities = __entities;
            jobHandle = clear.Schedule(JobHandle.CombineDependencies(
                childIndicesJobManager.readOnlyJobHandle,
                parentIndicesJobManager.readOnlyJobHandle, 
                jobHandle));

            var entityManager = __visibleCommander.Create();

            VisibleEx visible;
            visible.changedWeightValue = changedWeightValue;
            visible.forward = __camera.transform.forward;
            visible.instances = GetComponentLookup<GameClipTargetData>(true);
            visible.translations = GetComponentLookup<Translation>(true);
            visible.rotations = GetComponentLookup<Rotation>(true);
            visible.translationType = GetComponentTypeHandle<Translation>(true);
            visible.distanceHitType = distanceHitType;
            visible.childIndices = childIndices;
            visible.parentIndices = parentIndices;
            visible.weights = GetComponentLookup<GameClipTargetWeight>();
            visible.entityManager = entityManager.parallelWriter;
            visible.callbackHandles = callbackCommanderWriter;
            visible.entities = __entities.AsParallelWriter();
            jobHandle = visible.ScheduleParallel(__visibleGroup, jobHandle);

            childIndicesJobManager.AddReadOnlyDependency(jobHandle);
            parentIndicesJobManager.AddReadOnlyDependency(jobHandle);
            entityManager.AddJobHandleForProducer<VisibleEx>(jobHandle);
        }

        if (!__invisibleGroup.IsEmptyIgnoreFilter)
        {
            var entityManager = __invisibleCommander.Create();

            InvisibleEx invisible;
            invisible.changedWeightValue = changedWeightValue;
            invisible.entityType = GetEntityTypeHandle();
            invisible.instanceType = GetComponentTypeHandle<GameClipTargetData>(true);
            invisible.weightType = GetComponentTypeHandle<GameClipTargetWeight>();
            invisible.entityManager = entityManager.parallelWriter;
            invisible.callbackHandles = callbackCommanderWriter;
            jobHandle = invisible.ScheduleParallel(__invisibleGroup, jobHandle);

            entityManager.AddJobHandleForProducer<InvisibleEx>(jobHandle);
        }

        callbackCommander.AddJobHandleForProducer<GameLocationClipTargetWeightSystem>(jobHandle);

        Dependency = jobHandle;
    }
}

[AlwaysUpdateSystem, UpdateInGroup(typeof(MeshInstanceSystemGroup), OrderFirst = true), UpdateAfter(typeof(MeshInstanceFactorySystem))]
public partial class GameClipTargetRenderSystem : SystemBase
{
    public struct Result
    {
        public bool isFree;

        public float weight;

        public Entity entity;
    }

    private struct Free
    {
        [ReadOnly]
        public BufferAccessor<MeshInstanceNode> nodes;

        [NativeDisableUnsafePtrRestriction]
        public unsafe UnsafeList<Result>* results;

        public unsafe void Execute(int index)
        {
            Result result;
            result.isFree = true;
            result.weight = 0.0f;

            var nodes = this.nodes[index];
            int numNodes = nodes.Length;
            for (int i = 0; i < numNodes; ++i)
            {
                result.entity = nodes[i].entity;

                results->Add(result);
            }
        }
    }

    [BurstCompile]
    private struct FreeEx : IJobChunk
    {
        [ReadOnly]
        public BufferTypeHandle<MeshInstanceNode> nodeType;

        [NativeDisableUnsafePtrRestriction]
        public unsafe UnsafeList<Result>* results;

        public unsafe void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            if (!chunk.Has(ref nodeType))
                return;

            Free free;
            free.nodes = chunk.GetBufferAccessor(ref nodeType);
            free.results = results;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                free.Execute(i);
        }
    }

    private struct Collect
    {
        [ReadOnly]
        public NativeArray<GameClipTargetWeight> weights;
        [ReadOnly]
        public BufferAccessor<MeshInstanceNode> nodes;

        public NativeList<Result> results;

        public void Execute(int index)
        {
            Result result;
            result.isFree = false;
            result.weight = weights[index].value;

            var nodes = this.nodes[index];
            int numNodes = nodes.Length;
            for (int i = 0; i < numNodes; ++i)
            {
                result.entity = nodes[i].entity;

                results.Add(result);
            }
        }
    }

    [BurstCompile]
    private struct CollectEx : IJobChunk
    {
        [ReadOnly]
        public ComponentTypeHandle<GameClipTargetWeight> weightType;

        [ReadOnly]
        public BufferTypeHandle<MeshInstanceNode> nodeType;

        public NativeList<Result> results;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Collect collect;
            collect.weights = chunk.GetNativeArray(ref weightType);
            collect.nodes = chunk.GetBufferAccessor(ref nodeType);
            collect.results = results;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                collect.Execute(i);
        }
    }

    [BurstCompile]
    private static class BurstUtility
    {
        public struct Input
        {
            public unsafe UnsafeList<Result>* results;
            public EntityCommandPool<Entity>.Context visibleCommander;
            public EntityCommandPool<Entity>.Context invisibleCommander;
            public EntityQuery group;
        }

        public unsafe delegate void ApplyDelegate(
            Input* input,
            ref SystemState systemState);

        public static readonly unsafe ApplyDelegate ApplyFunction = BurstCompiler.CompileFunctionPointer<ApplyDelegate>(Apply).Invoke;

        [BurstCompile]
        [AOT.MonoPInvokeCallback(typeof(ApplyDelegate))]
        public static unsafe void Apply(
            Input* input,
            ref SystemState systemState)
        {
            var entityManager = systemState.EntityManager;

            Result result;
            result.isFree = true;
            result.weight = 0.0f;

            //using (var entityArray = input.group.ToEntityArray(Allocator.Temp))
            {
                /*int length = entityArray.Length;
                for (int i = 0; i < length; ++i)
                {
                    result.entity = entityArray[i];

                    input.results->Add(result);
                }*/

                systemState.CompleteDependency();

                FreeEx free;
                free.nodeType = systemState.GetBufferTypeHandle<MeshInstanceNode>(true);
                free.results = input->results;
                free.Run(input->group);

                entityManager.RemoveComponent<GameClipTargetWeight>(input->group);
            }

            using (var entities = new NativeList<Entity>(Allocator.Temp))
            {
                Entity entity;

                while (input->visibleCommander.TryDequeue(out entity))
                    entities.Add(entity);

                int visibleLength = entities.Length;
                if (visibleLength > 0)
                    entityManager.AddComponentBurstCompatible<GameClipTargetWeight>(entities.AsArray());

                while (input->invisibleCommander.TryDequeue(out entity))
                    entities.Add(entity);

                int invisibleLength = entities.Length;
                if (invisibleLength > visibleLength)
                    entityManager.RemoveComponent<GameClipTargetWeight>(entities.AsArray().GetSubArray(visibleLength, invisibleLength - visibleLength));

                //result.isFree = false;

                var nodes = systemState.GetBufferLookup<MeshInstanceNode>(true);
                DynamicBuffer<MeshInstanceNode> nodesTemp;
                int i, j, numNodes;
                /*for(i = 0; i < visibleLength; ++i)
                {
                    entity = entities[i];
                    if (!nodes.HasComponent(entity))
                        continue;

                    nodesTemp = nodes[entities[i]];
                    numNodes = nodesTemp.Length;
                    for (j = 0; j < numNodes; ++j)
                    {
                        result.entity = nodesTemp[j].value;
                        results->Add(result);
                    }
                }*/

                for (i = visibleLength; i < invisibleLength; ++i)
                {
                    entity = entities[i];
                    if (!nodes.HasBuffer(entity))
                        continue;

                    nodesTemp = nodes[entities[i]];
                    numNodes = nodesTemp.Length;
                    for (j = 0; j < numNodes; ++j)
                    {
                        result.entity = nodesTemp[j].entity;
                        input->results->Add(result);
                    }
                }
            }
        }

        public static unsafe void Apply(
            NativeList<Result> results,
            EntityCommandPool<Entity>.Context visibleCommander,
            EntityCommandPool<Entity>.Context invisibleCommander,
            in EntityQuery group,
            ref SystemState systemState)
        {
            Input input;
            input.results = results.GetUnsafeList();
            input.visibleCommander = visibleCommander;
            input.invisibleCommander = invisibleCommander;
            input.group = group;

            ApplyFunction((Input*)UnsafeUtility.AddressOf(ref input), ref systemState);
        }

        public static void ApplyAndCollect(
            NativeList<Result> results,
            EntityCommandPool<Entity>.Context visibleCommander,
            EntityCommandPool<Entity>.Context invisibleCommander,
            in EntityQuery invisibleGroup,
            in EntityQuery visibleGroup,
            ref SystemState systemState)
        {
            Apply(results, visibleCommander, invisibleCommander, invisibleGroup, ref systemState);

            //results.Capacity = math.max(results.Capacity, results.Length + visibleGroup.CalculateEntityCount());

            CollectEx collect;
            collect.weightType = systemState.GetComponentTypeHandle<GameClipTargetWeight>(true);
            collect.nodeType = systemState.GetBufferTypeHandle<MeshInstanceNode>(true);
            collect.results = results;
            collect.Run(visibleGroup);
        }
    }

    private struct MaterialRef
    {
        public int count;
        public Material source;
        public Material destination;

        public bool Release()
        {
            if (--count < 1)
            {
                UnityEngine.Object.Destroy(destination);

                destination = null;

                return true;
            }

            return false;
        }
    }

    public int materialPropertyWeight = Shader.PropertyToID("_ClipTargetWeight");
    public string materialKeyword = "CLIP_TARGET";

    private EntityQuery __visibleGroup;
    private EntityQuery __invisibleGroup;
    private EntityQuery __destroyGroup;

    private EntityCommandPool<Entity>.Context __visibleCommander;
    private EntityCommandPool<Entity>.Context __invisibleCommander;
    private EntityCommandPool<CallbackHandle>.Context __callbackCommander;

    private NativeList<Result> __results;
    private NativeParallelHashMap<Entity, int> __entityMaterials;
    private Dictionary<int, MaterialRef> __materialRefs;

    public EntityCommandPool<Entity> visibleCommander => __visibleCommander.pool;
    public EntityCommandPool<Entity> invisibleCommander => __invisibleCommander.pool;
    public EntityCommandPool<CallbackHandle> callbackCommander => __callbackCommander.pool;

    public GameClipTargetRenderSystem()
    {
        __visibleCommander = new EntityCommandPool<Entity>.Context(Allocator.Persistent);
        __invisibleCommander = new EntityCommandPool<Entity>.Context(Allocator.Persistent);
        __callbackCommander = new EntityCommandPool<CallbackHandle>.Context(Allocator.Persistent);
    }

    protected override void OnCreate()
    {
        base.OnCreate();

        __visibleGroup = GetEntityQuery(ComponentType.ReadOnly<GameClipTargetWeight>(), ComponentType.ReadOnly<MeshInstanceNode>(), ComponentType.ReadOnly<EntityObjects>());
        __invisibleGroup = GetEntityQuery(
            new EntityQueryDesc()
            {
                All = new ComponentType[]
                {
                    ComponentType.ReadOnly<GameClipTargetWeight>()
                },
                None = new ComponentType[]
                {
                    typeof(EntityObjects)
                },
                Options = EntityQueryOptions.IncludeDisabledEntities
            }, 
            new EntityQueryDesc()
            {
                All = new ComponentType[]
                {
                    ComponentType.ReadOnly<GameClipTargetWeight>(),
                    ComponentType.ReadOnly<Disabled>()
                },
                Options = EntityQueryOptions.IncludeDisabledEntities
            });
        __destroyGroup = GetEntityQuery(ComponentType.ReadOnly<GameClipTargetData>(), ComponentType.Exclude<EntityObjects>());

        __results = new NativeList<Result>(Allocator.Persistent);

        __entityMaterials = new NativeParallelHashMap<Entity, int>(1, Allocator.Persistent);
        __materialRefs = new Dictionary<int, MaterialRef>();
    }

    protected override void OnDestroy()
    {
        foreach(var material in __materialRefs.Values)
            UnityEngine.Object.Destroy(material.destination);

        __materialRefs = null;

        __entityMaterials.Dispose();
        __visibleCommander.Dispose();
        __invisibleCommander.Dispose();
        __callbackCommander.Dispose();
        __results.Dispose();

        base.OnDestroy();
    }

    protected override void OnUpdate()
    {
        __results.Clear();

        BurstUtility.ApplyAndCollect(
            __results, 
            __visibleCommander, 
            __invisibleCommander, 
            __invisibleGroup, 
            __visibleGroup, 
            ref this.GetState());

        var entityManager = EntityManager;

        bool isContains, isInit;
        int numResults = __results.Length, sourceInstanceID, destinationInstanceID;
        Result result;
        MaterialRef materialRef = default;
        RenderMesh renderMesh;
        for(int i = 0; i < numResults; ++i)
        {
            result = __results[i];

            isContains = __entityMaterials.TryGetValue(result.entity, out sourceInstanceID);
            if (result.isFree)
            {
                if (isContains)
                {
                    __Release(sourceInstanceID, out materialRef.source);

                    __entityMaterials.Remove(result.entity);

                    if (entityManager.HasComponent<RenderMesh>(result.entity))
                    {
                        renderMesh = entityManager.GetSharedComponentManaged<RenderMesh>(result.entity);
                        renderMesh.material = materialRef.source;
                        entityManager.SetSharedComponentManaged(result.entity, renderMesh);
                    }
                }
            }
            else if (entityManager.HasComponent<RenderMesh>(result.entity))
            {
                renderMesh = entityManager.GetSharedComponentManaged<RenderMesh>(result.entity);
                if (isContains)
                {
                    materialRef = __materialRefs[sourceInstanceID];
                    isInit = renderMesh.material != materialRef.destination;
                }
                else
                    isInit = true;

                if (isInit)
                {
                    if (isContains)
                        __Release(sourceInstanceID, out _);

                    destinationInstanceID = renderMesh.material.GetInstanceID();
                    if (!__materialRefs.TryGetValue(destinationInstanceID, out materialRef))
                    {
                        materialRef.count = 1;
                        materialRef.source = renderMesh.material;
                        materialRef.destination = UnityEngine.Object.Instantiate(renderMesh.material);
                        materialRef.destination.EnableKeyword(materialKeyword);
                    }
                    else
                        ++materialRef.count;

                    __materialRefs[destinationInstanceID] = materialRef;
                    __entityMaterials[result.entity] = destinationInstanceID;

                    renderMesh.material = materialRef.destination;
                    entityManager.SetSharedComponentManaged(result.entity, renderMesh);
                }

                materialRef.destination.SetFloat(materialPropertyWeight, result.weight);
            }
        }

        while(__callbackCommander.TryDequeue(out var callbackHandle))
        {
            try
            {
                callbackHandle.Invoke();
            }
            catch(Exception e)
            {
                Debug.LogException(e.InnerException ?? e);
            }
        }

        if(!__destroyGroup.IsEmptyIgnoreFilter)
        {
            //TODO:
            __destroyGroup.CompleteDependency();

            using (var instances = __destroyGroup.ToComponentDataArray<GameClipTargetData>(Allocator.Temp))
            {
                foreach(var instance in instances)
                {
                    instance.visibleCallback.Unregister();
                    instance.invisibleCallback.Unregister();
                }
            }

            entityManager.RemoveComponent<GameClipTargetData>(__destroyGroup);
        }
    }

    private void __Release(int instanceID, out Material material)
    {
        var materialRef = __materialRefs[instanceID];

        material = materialRef.source;

        if (materialRef.Release())
        {
            material.DisableKeyword(materialKeyword);

            __materialRefs.Remove(instanceID);
        }
        else
            __materialRefs[instanceID] = materialRef;
    }
}