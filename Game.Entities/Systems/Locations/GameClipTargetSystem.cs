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
using UnityEngine.Rendering;
using ZG.Entities.Physics;

public struct GameLocationClipTargetFactory : IComponentData
{
    public EntityCommandPool<Entity> visibleCommander;
    public EntityCommandPool<Entity> invisibleCommander;
}

[BurstCompile, CreateAfter(typeof(GameContainerChildSystem)), 
    CreateAfter(typeof(MeshInstanceRendererSystem)), 
    UpdateInGroup(typeof(UpdatePresentationSystemGroup))]
public partial struct GameLocationClipTargetWeightSystem : ISystem, IEntityCommandProducerJob
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
        public float deltaTime;

        public float3 forward;

        public GameContainerHierarchyEnumerator enumerator;
        
        [ReadOnly]
        public SharedHashMap<Entity, MeshInstanceRendererBuilder>.Reader rendererBuilders;

        [ReadOnly]
        public ComponentLookup<GameClipTargetData> instances;

        [ReadOnly]
        public ComponentLookup<Translation> translationMap;

        [ReadOnly]
        public NativeArray<Translation> translations;

        [ReadOnly]
        public BufferAccessor<GameNodeCharacterDistanceHit> distanceHits;

        [ReadOnly]
        public BufferLookup<MeshInstanceNode> renderers;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<PhysicsRaycastColliderToIgnore> physicsRaycastCollidersToIgnore;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<GameClipTargetWeight> sources;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<ClipTargetWeight> destinations;

        public EntityCommandQueue<Entity>.ParallelWriter entityManager;

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
                sources.SetComponentEnabled(entity, true);

                if(!physicsRaycastCollidersToIgnore.IsComponentEnabled(entity))
                    physicsRaycastCollidersToIgnore.SetComponentEnabled(entity, true);

                float changedWeightValue = instance.weightSpeed * deltaTime;

                var weight = sources[entity];
                weight.value = math.clamp(weight.value + changedWeightValue, 0.0f, 1.0f) + changedWeightValue;
                sources[entity] = weight;

                if (renderers.HasBuffer(entity) && !rendererBuilders.ContainsKey(entity))
                {
                    ClipTargetWeight targetWeight;
                    targetWeight.value = math.min(weight.value, 1.0f);
                    foreach (var renderer in renderers[entity])
                    {
                        if (destinations.HasComponent(renderer.entity))
                            destinations[renderer.entity] = targetWeight;
                        else
                            entityManager.Enqueue(renderer.entity);
                    }
                }
            }
        }

        public void Execute(int index)
        {
            var distanceHits = this.distanceHits[index];
            Entity entity;
            float3 position = translations[index].Value;
            int numDistanceHits = distanceHits.Length;
            for(int i = 0; i < numDistanceHits; ++i)
            {
                entity = distanceHits[i].value.Entity;
                Execute(position, entity);

                enumerator.Init(entity);
                while (enumerator.MoveNext())
                    Execute(position, enumerator.Current);
            }
        }
    }

    [BurstCompile]
    private struct VisibleEx : IJobChunk, IEntityCommandProducerJob
    {
        public float deltaTime;

        public float3 forward;

        [ReadOnly]
        public SharedHashMap<Entity, MeshInstanceRendererBuilder>.Reader rendererBuilders;

        [ReadOnly]
        public SharedMultiHashMap<Entity, EntityData<int>>.Reader childIndices;

        [ReadOnly]
        public SharedMultiHashMap<Entity, EntityData<int>>.Reader parentIndices;

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
        public BufferLookup<MeshInstanceNode> renderers;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<PhysicsRaycastColliderToIgnore> physicsRaycastCollidersToIgnore;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<GameClipTargetWeight> sources;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<ClipTargetWeight> destinations;

        public EntityCommandQueue<Entity>.ParallelWriter entityManager;

        public NativeParallelHashSet<Entity>.ParallelWriter entities;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            using (var enumerator = new GameContainerHierarchyEnumerator(Allocator.Temp, parentIndices, childIndices))
            {
                Visible visible;
                visible.deltaTime = deltaTime;
                visible.forward = forward;
                visible.enumerator = enumerator;
                visible.rendererBuilders = rendererBuilders;
                visible.instances = instances;
                visible.translationMap = translations;
                visible.translations = chunk.GetNativeArray(ref translationType);
                visible.distanceHits = chunk.GetBufferAccessor(ref distanceHitType);
                visible.renderers = renderers;
                visible.physicsRaycastCollidersToIgnore = physicsRaycastCollidersToIgnore;
                visible.sources = sources;
                visible.destinations = destinations;
                visible.entityManager = entityManager;
                visible.entities = entities;

                var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (iterator.NextEntityIndex(out int i))
                    visible.Execute(i);
            }
        }
    }

    private struct Invisible
    {
        public float deltaTime;

        [ReadOnly]
        public SharedHashMap<Entity, MeshInstanceRendererBuilder>.Reader rendererBuilders;

        [ReadOnly]
        public NativeArray<Entity> entityArray;

        [ReadOnly]
        public NativeArray<GameClipTargetData> instances;

        public NativeArray<GameClipTargetWeight> sources;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<ClipTargetWeight> destinations;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<PhysicsRaycastColliderToIgnore> physicsRaycastCollidersToIgnore;

        public BufferAccessor<MeshInstanceNode> renderers;

        public EntityCommandQueue<Entity>.ParallelWriter entityManager;

        public bool Execute(int index)
        {
            Entity entity = entityArray[index];
            
            var instance = instances[index];
            var weight = sources[index];

            weight.value -= instance.weightSpeed * deltaTime;
            if(weight.value < 0.0f && physicsRaycastCollidersToIgnore.IsComponentEnabled(entity))
                physicsRaycastCollidersToIgnore.SetComponentEnabled(entity, false);
            
            bool result = weight.value < instance.weightMin;//0.0f;
            if (result)
                weight.value = 0.0f;

            if(index < renderers.Length && !rendererBuilders.ContainsKey(entity))
            {
                ClipTargetWeight targetWeight;
                targetWeight.value = math.max(weight.value, 0.0f);
                foreach (var renderer in renderers[index])
                {
                    if (destinations.HasComponent(renderer.entity))
                    {
                        if(result)
                            entityManager.Enqueue(renderer.entity);
                        else
                            destinations[renderer.entity] = targetWeight;
                    }
                }
            }

            sources[index] = weight;

            return result;
        }
    }

    [BurstCompile]
    private struct InvisibleEx : IJobChunk, IEntityCommandProducerJob
    {
        public float deltaTime;

        [ReadOnly]
        public SharedHashMap<Entity, MeshInstanceRendererBuilder>.Reader rendererBuilders;

        [ReadOnly]
        public EntityTypeHandle entityType;

        [ReadOnly]
        public ComponentTypeHandle<GameClipTargetData> instanceType;

        public ComponentTypeHandle<GameClipTargetWeight> sourceType;

        public BufferTypeHandle<MeshInstanceNode> rendererType;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<ClipTargetWeight> destinations;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<PhysicsRaycastColliderToIgnore> physicsRaycastCollidersToIgnore;

        public EntityCommandQueue<Entity>.ParallelWriter entityManager;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Invisible invisible;
            invisible.deltaTime = deltaTime;
            invisible.rendererBuilders = rendererBuilders;
            invisible.entityArray = chunk.GetNativeArray(entityType);
            invisible.instances = chunk.GetNativeArray(ref instanceType);
            invisible.sources = chunk.GetNativeArray(ref sourceType);
            invisible.renderers = chunk.GetBufferAccessor(ref rendererType);
            invisible.destinations = destinations;
            invisible.physicsRaycastCollidersToIgnore = physicsRaycastCollidersToIgnore;
            invisible.entityManager = entityManager;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
            {
                if (invisible.Execute(i))
                    chunk.SetComponentEnabled(ref sourceType, i, false);
            }
        }
    }

    private EntityQuery __visibleGroup;
    private EntityQuery __invisibleGroup;
    private EntityQuery __cameraForwardGroup;

    private EntityTypeHandle __entityType;
    private ComponentTypeHandle<PhysicsRaycastColliderToIgnore> __physicsRaycastCollidersToIgnoreType;
    private ComponentTypeHandle<GameClipTargetWeight> __weightType;
    private ComponentTypeHandle<GameClipTargetData> __instanceType;

    private BufferTypeHandle<GameNodeCharacterDistanceHit> __distanceHitType;

    private BufferTypeHandle<MeshInstanceNode> __rendererType;

    private BufferLookup<MeshInstanceNode> __renderers;

    private ComponentLookup<GameClipTargetData> __instances;

    private ComponentLookup<Translation> __translations;

    private ComponentLookup<Rotation> __rotations;

    private ComponentTypeHandle<Translation> __translationType;

    private ComponentLookup<GameClipTargetWeight> __sources;

    private ComponentLookup<ClipTargetWeight> __destinations;

    private ComponentLookup<PhysicsRaycastColliderToIgnore> __physicsRaycastCollidersToIgnore;

    private SharedHashMap<Entity, MeshInstanceRendererBuilder> __rendererBuilders;

    private SharedMultiHashMap<Entity, EntityData<int>> __parentIndices;
    private SharedMultiHashMap<Entity, EntityData<int>> __childIndices;
    private NativeParallelHashSet<Entity> __entities;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __visibleGroup = builder
                .WithAll<GameLocator, GameNodeCharacterDistanceHit>()
                .Build(ref state);

        __invisibleGroup = state.GetEntityQuery(ComponentType.ReadWrite<GameClipTargetWeight>());

        __cameraForwardGroup = state.GetEntityQuery(ComponentType.ReadOnly<MainCameraForward>());

        __entityType = state.GetEntityTypeHandle();
        __physicsRaycastCollidersToIgnoreType = state.GetComponentTypeHandle<PhysicsRaycastColliderToIgnore>();
        __weightType = state.GetComponentTypeHandle<GameClipTargetWeight>();
        __instanceType = state.GetComponentTypeHandle<GameClipTargetData>(true);

        __distanceHitType = state.GetBufferTypeHandle<GameNodeCharacterDistanceHit>(true);

        __rendererType = state.GetBufferTypeHandle<MeshInstanceNode>(true);
        __renderers = state.GetBufferLookup<MeshInstanceNode>(true);

        __instances = state.GetComponentLookup<GameClipTargetData>(true);
        __translations = state.GetComponentLookup<Translation>(true);
        __rotations = state.GetComponentLookup<Rotation>(true);
        __translationType = state.GetComponentTypeHandle<Translation>(true);
        __sources = state.GetComponentLookup<GameClipTargetWeight>();
        __destinations = state.GetComponentLookup<ClipTargetWeight>();

        __physicsRaycastCollidersToIgnore = state.GetComponentLookup<PhysicsRaycastColliderToIgnore>();

        __entities = new NativeParallelHashSet<Entity>(1, Allocator.Persistent);

        var world = state.WorldUnmanaged;

        __rendererBuilders = world.GetExistingSystemUnmanaged<MeshInstanceRendererSystem>().builders;

        ref var containerChildSystem = ref world.GetExistingSystemUnmanaged<GameContainerChildSystem>();
        __parentIndices = containerChildSystem.parentIndices;
        __childIndices = containerChildSystem.childIndices;
    }

    //[BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        __entities.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!__cameraForwardGroup.HasSingleton<MainCameraForward>())
            return;

        var factory = SystemAPI.GetSingleton<GameLocationClipTargetFactory>();

        var rendererBuilders = __rendererBuilders.reader;
        ref var rendererBuilderJobManager = ref __rendererBuilders.lookupJobManager;
        var destinations = __destinations.UpdateAsRef(ref state);
        var physicsRaycastCollidersToIgnore = __physicsRaycastCollidersToIgnore.UpdateAsRef(ref state);
        JobHandle? result = null;
        var inputDeps = state.Dependency;
        float deltaTime = state.WorldUnmanaged.Time.DeltaTime;
        if (!__visibleGroup.IsEmptyIgnoreFilter)
        {
            var distanceHitType = __distanceHitType.UpdateAsRef(ref state);
            var counter = new NativeArray<int>(1, Allocator.TempJob, NativeArrayOptions.ClearMemory);

            Count count;
            count.counter = counter;
            count.distanceHitType = distanceHitType;
            var jobHandle = count.ScheduleParallelByRef(__visibleGroup, inputDeps);

            ref var childIndicesJobManager = ref __childIndices.lookupJobManager;
            ref var parentIndicesJobManager = ref __parentIndices.lookupJobManager;

            var childIndices = __childIndices.reader;
            var parentIndices = __parentIndices.reader;

            Clear clear;
            clear.counter = counter;
            clear.childIndices = childIndices;
            clear.parentIndices = parentIndices;
            clear.entities = __entities;
            jobHandle = clear.ScheduleByRef(JobHandle.CombineDependencies(
                childIndicesJobManager.readOnlyJobHandle,
                parentIndicesJobManager.readOnlyJobHandle,
                jobHandle));

            var entityManager = factory.visibleCommander.Create();

            __cameraForwardGroup.CompleteDependency();

            VisibleEx visible;
            visible.deltaTime = deltaTime;
            visible.forward = __cameraForwardGroup.GetSingleton<MainCameraForward>().value;
            visible.rendererBuilders = rendererBuilders;
            visible.instances = __instances.UpdateAsRef(ref state);
            visible.translations = __translations.UpdateAsRef(ref state);
            visible.rotations = __rotations.UpdateAsRef(ref state);
            visible.translationType = __translationType.UpdateAsRef(ref state);
            visible.distanceHitType = distanceHitType;
            visible.childIndices = childIndices;
            visible.parentIndices = parentIndices;
            visible.renderers = __renderers.UpdateAsRef(ref state);
            visible.physicsRaycastCollidersToIgnore = physicsRaycastCollidersToIgnore;
            visible.sources = __sources.UpdateAsRef(ref state);
            visible.destinations = destinations;
            visible.entityManager = entityManager.parallelWriter;
            visible.entities = __entities.AsParallelWriter();
            jobHandle = visible.ScheduleParallelByRef(__visibleGroup, JobHandle.CombineDependencies(jobHandle, rendererBuilderJobManager.readOnlyJobHandle));

            childIndicesJobManager.AddReadOnlyDependency(jobHandle);
            parentIndicesJobManager.AddReadOnlyDependency(jobHandle);
            entityManager.AddJobHandleForProducer<VisibleEx>(jobHandle);

            result = jobHandle;
        }

        if (!__invisibleGroup.IsEmptyIgnoreFilter)
        {
            var jobHandle = result == null ? JobHandle.CombineDependencies(rendererBuilderJobManager.readOnlyJobHandle, inputDeps) : result.Value;

            var entityManager = factory.invisibleCommander.Create();

            InvisibleEx invisible;
            invisible.deltaTime = deltaTime;
            invisible.rendererBuilders = rendererBuilders;
            invisible.entityType = __entityType.UpdateAsRef(ref state);
            invisible.instanceType = __instanceType.UpdateAsRef(ref state);
            invisible.sourceType = __weightType.UpdateAsRef(ref state);
            invisible.physicsRaycastCollidersToIgnore = physicsRaycastCollidersToIgnore;
            invisible.rendererType = __rendererType.UpdateAsRef(ref state);
            invisible.destinations = __destinations;
            invisible.entityManager = entityManager.parallelWriter;
            jobHandle = invisible.ScheduleParallelByRef(__invisibleGroup, jobHandle);

            entityManager.AddJobHandleForProducer<InvisibleEx>(jobHandle);

            result = jobHandle;
        }

        if (result != null)
        {
            var jobHandle = result.Value;

            rendererBuilderJobManager.AddReadOnlyDependency(jobHandle);

            state.Dependency = jobHandle;
        }
    }
}

[CreateAfter(typeof(EntitiesGraphicsSystem)), 
    UpdateInGroup(typeof(PresentationSystemGroup), OrderLast = true)]
public partial class GameClipTargetRenderSystem : SystemBase
{
    private struct MaterialRef
    {
        public int count;
        public BatchMaterialID value;
    }

    [BurstCompile]
    private struct Release : IJobParallelFor
    {
        [ReadOnly]
        public NativeArray<Entity> entities;

        public NativeHashMap<Entity, BatchMaterialID> entityMaterials;

        public NativeHashMap<BatchMaterialID, MaterialRef> materialRefs;

        public ComponentLookup<MaterialMeshInfo> materialMeshInfos;

        public NativeList<BatchMaterialID> materialsToInvisible;

        public void Execute(int index)
        {
            Entity entity = entities[index];
            if (!entityMaterials.TryGetValue(entity, out var material))
                return;

            entityMaterials.Remove(entity);

            var materialRef = materialRefs[material];
            if (--materialRef.count < 1)
            {
                materialsToInvisible.Add(materialRef.value);

                materialRefs.Remove(material);
            }
            else
                materialRefs[material] = materialRef;

            if (materialMeshInfos.HasComponent(entity))
            {
                var materialMeshInfo = materialMeshInfos[entity];
                if (materialMeshInfo.MaterialID == materialRef.value)
                {
                    materialMeshInfo.MaterialID = material;

                    materialMeshInfos[entity] = materialMeshInfo;
                }
            }
        }
    }

    [BurstCompile]
    private struct Retain : IJobParallelFor
    {
        [ReadOnly]
        public NativeArray<Entity> entities;

        public NativeHashMap<Entity, BatchMaterialID> entityMaterials;

        public ComponentLookup<MaterialMeshInfo> materialMeshInfos;

        public NativeHashMap<BatchMaterialID, MaterialRef> materialRefs;

        public NativeParallelMultiHashMap<BatchMaterialID, Entity> materialToVisible;

        public unsafe void Execute(int index)
        {
            Entity entity = entities[index];
            if (entityMaterials.ContainsKey(entity) || !materialMeshInfos.HasComponent(entity))
                return;

            var materialMeshInfo = materialMeshInfos[entity];

            var material = materialMeshInfo.MaterialID;

            entityMaterials[entity] = material;

            if (materialRefs.TryGetValue(material, out var materialRef))
            {
                ++materialRef.count;

                materialMeshInfo.MaterialID = materialRef.value;

                materialMeshInfos[entity] = materialMeshInfo;

                materialRefs[material] = materialRef;
            }
            else
                materialToVisible.Add(material, entity);
        }
    }

    [BurstCompile]
    private struct Apply : IJob
    {
        [ReadOnly]
        public NativeHashMap<BatchMaterialID, MaterialRef> materialRefs;

        public NativeParallelMultiHashMap<BatchMaterialID, Entity> materialsToVisible;

        public ComponentLookup<MaterialMeshInfo> materialMeshInfos;

        public void Execute()
        {
            MaterialMeshInfo materialMeshInfo;
            Entity entity;
            foreach(var materialToVisible in materialsToVisible)
            {
                entity = materialToVisible.Value;
                materialMeshInfo = materialMeshInfos[entity];
                materialMeshInfo.MaterialID = materialRefs[materialMeshInfo.MaterialID].value;
                materialMeshInfos[entity] = materialMeshInfo;
            }

            materialsToVisible.Clear();
        }
    }

    //public int materialPropertyWeight = Shader.PropertyToID("_ClipTargetWeight");
    public const string MATERIAL_KEYWORD = "CLIP_TARGET";

    private EntityQuery __group;
    private ComponentLookup<MaterialMeshInfo> __materialMeshInfos;

    private EntityCommandPool<Entity>.Context __visibleCommander;
    private EntityCommandPool<Entity>.Context __invisibleCommander;

    private NativeHashMap<Entity, BatchMaterialID> __entityMaterials;
    private NativeHashMap<BatchMaterialID, MaterialRef> __materialRefs;

    private NativeParallelMultiHashMap<BatchMaterialID, Entity> __materialsToVisible;
    
    private EntitiesGraphicsSystem __entitiesGraphicsSystem;

    protected override void OnCreate()
    {
        base.OnCreate();

        using(var builder = new EntityQueryBuilder(Allocator.Temp))
            __group = builder
                .WithAll<ClipTargetWeight>()
                .WithNone<MaterialMeshInfo>()
                .WithOptions(EntityQueryOptions.IncludeDisabledEntities)
                .Build(this);

        __materialMeshInfos = GetComponentLookup<MaterialMeshInfo>();

        __entityMaterials = new NativeHashMap<Entity, BatchMaterialID>(1, Allocator.Persistent);
        __materialRefs = new NativeHashMap<BatchMaterialID, MaterialRef>(1, Allocator.Persistent);

        __visibleCommander = new EntityCommandPool<Entity>.Context(Allocator.Persistent);
        __invisibleCommander = new EntityCommandPool<Entity>.Context(Allocator.Persistent);

        __materialsToVisible = new NativeParallelMultiHashMap<BatchMaterialID, Entity>(1, Allocator.Persistent);

        GameLocationClipTargetFactory factory;
        factory.visibleCommander = __visibleCommander.pool;
        factory.invisibleCommander = __invisibleCommander.pool;
        EntityManager.AddComponentData(SystemHandle, factory);

        __entitiesGraphicsSystem = World.GetExistingSystemManaged<EntitiesGraphicsSystem>();
    }

    protected override void OnDestroy()
    {
        if (__entitiesGraphicsSystem != null)
        {
            foreach (var materialRef in __materialRefs)
                __Release(materialRef.Value.value);
        }

        __materialRefs.Dispose();

        __entityMaterials.Dispose();
        __visibleCommander.Dispose();
        __invisibleCommander.Dispose();

        __materialsToVisible.Dispose();

        base.OnDestroy();
    }

    protected override void OnUpdate()
    {
        var entityManager = EntityManager;
        ref var state = ref this.GetState();
        bool isInvisible = !__invisibleCommander.isEmpty, isDestroied = !__group.IsEmpty;
        if (isInvisible || isDestroied)
        {
            using (var materialsToInvisible = new NativeList<BatchMaterialID>(Allocator.TempJob))
            {
                using (var entities = new NativeList<Entity>(Allocator.TempJob))
                {
                    if(isInvisible)
                        __invisibleCommander.MoveTo(new EntityCommandEntityContainer(entities));

                    if (isDestroied)
                    {
                        using (var entityArray = __group.ToEntityArray(Allocator.Temp))
                            entities.AddRange(entityArray);
                    }

                    if (entities.Length > 0)
                    {
                        var entityArray = entities.AsArray();

                        entityManager.RemoveComponent<ClipTargetWeight>(entityArray);

                        Release release;
                        release.entities = entityArray;
                        release.entityMaterials = __entityMaterials;
                        release.materialRefs = __materialRefs;
                        release.materialMeshInfos = __materialMeshInfos.UpdateAsRef(ref state);
                        release.materialsToInvisible = materialsToInvisible;
                        release.RunByRef(entities.Length);
                    }
                }

                foreach(var materialToInvisible in materialsToInvisible)
                    __Release(materialToInvisible);
            }
        }

        if (!__visibleCommander.isEmpty)
        {
            using (var entities = new NativeList<Entity>(Allocator.TempJob))
            {
                __visibleCommander.MoveTo(new EntityCommandEntityContainer(entities));

                if (entities.Length > 0)
                {
                    var entityArray = entities.AsArray();

                    entityManager.AddComponent<ClipTargetWeight>(entityArray);

                    Retain retain;
                    retain.entities = entityArray;
                    retain.entityMaterials = __entityMaterials;
                    retain.materialRefs = __materialRefs;
                    retain.materialMeshInfos = __materialMeshInfos.UpdateAsRef(ref state);
                    retain.materialToVisible = __materialsToVisible;
                    retain.RunByRef(entities.Length);
                }
            }

            using(var materials = __materialsToVisible.GetKeyArray(Allocator.Temp))
            {
                MaterialRef materialRef;
                BatchMaterialID material;
                int numMaterials = materials.ConvertToUniqueArray();
                for(int i = 0; i < numMaterials; ++i)
                {
                    material = materials[i];
                    materialRef.count = __materialsToVisible.CountValuesForKey(material);
                    materialRef.value = __Retain(material);

                    __materialRefs.Add(material, materialRef);
                }
            }

            Apply apply;
            apply.materialRefs = __materialRefs;
            apply.materialsToVisible = __materialsToVisible;
            apply.materialMeshInfos = __materialMeshInfos.UpdateAsRef(ref state);

            Dependency = apply.ScheduleByRef(Dependency);
        }
    }

    private BatchMaterialID __Retain(in BatchMaterialID batchMaterialID)
    {
        var material = __entitiesGraphicsSystem.GetMaterial(batchMaterialID);
        material = UnityEngine.Object.Instantiate(material);
        material.EnableKeyword(MATERIAL_KEYWORD);
        material.EnableKeyword("_ALPHATEST_ON");
        material.renderQueue = (int)RenderQueue.AlphaTest;
        return __entitiesGraphicsSystem.RegisterMaterial(material);
    }

    private void __Release(in BatchMaterialID batchMaterialID)
    {
        var material = __entitiesGraphicsSystem.GetMaterial(batchMaterialID);

        __entitiesGraphicsSystem.UnregisterMaterial(batchMaterialID);

        UnityEngine.Object.Destroy(material);
    }
}