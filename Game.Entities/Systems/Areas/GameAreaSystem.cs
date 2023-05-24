using System;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;
using Unity.Physics;
using ZG;

[assembly: RegisterGenericJobType(typeof(TimeManager<GameAreaInternalInstance>.Clear))]
[assembly: RegisterGenericJobType(typeof(TimeManager<GameAreaInternalInstance>.UpdateEvents))]

[assembly: RegisterGenericJobType(typeof(GameAreaInit<GameAreaNeighborEnumerable>))]
[assembly: RegisterGenericJobType(typeof(GameAreaInvokeCommands<GameAreaPrefabSystem.Validator>))]

[assembly: RegisterGenericJobType(typeof(EntityComponentContainerMoveComponentJob<GameAreaPrefab>))]

#if DEBUG
[assembly: RegisterEntityCommandProducerJob(typeof(GameAreaInvokeCommands<GameAreaPrefabSystem.Validator>))]
#endif

public struct GameAreaDefinition
{
    public struct Type
    {
        public float maxDistance;
        public BlobPtr<Collider> collider;
    }

    public BlobArray<Type> types;
    public BlobArray<int> typeIndices;

    public BlobArray<byte> colliders;
}

public struct GameAreaData : IComponentData
{
    public BlobAssetReference<GameAreaDefinition> definition;
}

public struct GameAreaNeighborEnumerable : IGameAreaNeighborEnumerable, IComponentData
{
    public int3 segments;
    public float3 size;

    public GameAreaNeighborEnumerable(in int3 segments, in float3 size)
    {
        this.segments = segments;
        this.size = size;
    }

    public int3 GetPosition(int index)
    {
        int size = segments.x * segments.y, temp = index % size;
        return math.int3(temp % segments.x, temp / segments.x, index / size);
    }

    public bool GetPosition(in float3 position, out int3 result)
    {
        result = (int3)math.floor((position * math.float3(1.0f / size.x, 1.0f / size.y, 1.0f / size.z) + math.float3(0.5f, 0.0f, 0.5f)) * segments);

        return result.x >= 0 && result.x < segments.x && result.y >= 0 && result.y < segments.y && result.z >= 0 && result.z < segments.z;
    }

    public static int GetIndex(in int2 segments, in int3 position)
    {
        return position.x + position.y * segments.x + position.z * segments.x * segments.y;
    }

    public int GetIndex(in float3 position)
    {
        int3 result;
        if (GetPosition(position, out result))
            return GetIndex(segments.xy, result);

        return GetIndex(segments.xy, math.int3(
            math.clamp(result.x, 0, segments.x - 1),
            math.clamp(result.y, 0, segments.y - 1),
            math.clamp(result.z, 0, segments.z - 1)));
    }

    public void Execute<T>(int areaIndex, ref T enumerator) where T : IGameAreaNeighborEnumerator
    {
        int i, j, k;
        int3 position = GetPosition(areaIndex), min = math.max(position - 1, int3.zero), max = math.min(position + 1, segments - 1);
        for (i = min.x; i <= max.x; ++i)
        {
            for (j = min.y; j <= max.y; ++j)
            {
                for (k = min.z; k <= max.z; ++k)
                {
                    if (i == position.x && j == position.y && k == position.z)
                        continue;

                    enumerator.Execute(GetIndex(segments.xy, math.int3(i, j, k)));
                }
            }
        }
    }
}

[BurstCompile, UpdateInGroup(typeof(GameUpdateSystemGroup)), UpdateAfter(typeof(GameNodeCharacterSystemGroup))]
public partial struct GameAreaSystem : ISystem
{
    private struct UpdateAreaIndices
    {
        public GameAreaNeighborEnumerable enumerable;
        [ReadOnly]
        public NativeArray<Translation> translations;
        public NativeArray<GameAreaNode> nodes;

        public void Execute(int index)
        {
            GameAreaNode node;
            node.areaIndex = enumerable.GetIndex(translations[index].Value);
            nodes[index] = node;
        }
    }

    [BurstCompile]
    private struct UpdateAreaIndicesEx : IJobChunk
    {
        public GameAreaNeighborEnumerable enumerable;
        [ReadOnly]
        public ComponentTypeHandle<Translation> translationType;
        public ComponentTypeHandle<GameAreaNode> nodeType;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            UpdateAreaIndices updateAreaIndices;
            updateAreaIndices.enumerable = enumerable;
            updateAreaIndices.translations = chunk.GetNativeArray(ref translationType);
            updateAreaIndices.nodes = chunk.GetNativeArray(ref nodeType);

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                updateAreaIndices.Execute(i);
        }
    }

    private EntityQuery __enumerableGroup;
    private EntityQuery __group;

    public void OnCreate(ref SystemState state)
    {
        __enumerableGroup = state.GetEntityQuery(ComponentType.ReadOnly<GameAreaNeighborEnumerable>());

        __group = state.GetEntityQuery(
            ComponentType.ReadOnly<Translation>(),
            ComponentType.ReadWrite<GameAreaNode>());

        __group.SetChangedVersionFilter(typeof(Translation));

        state.RequireForUpdate(__group);
    }

    public void OnDestroy(ref SystemState state)
    {

    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        UpdateAreaIndicesEx updateAreaIndices;
        updateAreaIndices.enumerable = __enumerableGroup.GetSingleton<GameAreaNeighborEnumerable>();
        updateAreaIndices.translationType = state.GetComponentTypeHandle<Translation>(true);
        updateAreaIndices.nodeType = state.GetComponentTypeHandle<GameAreaNode>();

        state.Dependency = updateAreaIndices.ScheduleParallel(__group, state.Dependency);
    }
}

[BurstCompile, UpdateInGroup(typeof(GameRollbackSystemGroup), OrderLast = true)]
public partial struct GameAreaStructChangeSystem : ISystem
{
    public static readonly int InnerloopBatchCount = 1;

    private EntityCommandPool<EntityData<GameAreaPrefab>>.Context __addComponentCommander;
    private EntityCommandPool<Entity>.Context __removeComponentCommander;

    public EntityCommandPool<EntityData<GameAreaPrefab>> addComponentCommander => __addComponentCommander.pool;

    public EntityCommandPool<Entity> removeComponentCommander => __removeComponentCommander.pool;

    public void OnCreate(ref SystemState state)
    {
        __addComponentCommander = new EntityCommandPool<EntityData<GameAreaPrefab>>.Context(Allocator.Persistent);
        __removeComponentCommander = new EntityCommandPool<Entity>.Context(Allocator.Persistent);
    }

    public void OnDestroy(ref SystemState state)
    {
        __addComponentCommander.Dispose();
        __removeComponentCommander.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if(!__removeComponentCommander.isEmpty)
        {
            using (var container = new EntityCommandEntityContainer(Allocator.Temp))
            {
                __removeComponentCommander.MoveTo(container);

                container.RemoveComponent<GameAreaInstance>(ref state);
            }
        }

        if (!__addComponentCommander.isEmpty)
        {
            var container = new EntityCommandComponentContainer<GameAreaPrefab>(Allocator.TempJob);

            __addComponentCommander.MoveTo(container);

            container.AddComponentData(ref state, InnerloopBatchCount);

            state.Dependency = container.Dispose(state.Dependency);
        }
    }
}

[BurstCompile, 
    CreateAfter(typeof(GameAreaStructChangeSystem)),
    CreateAfter(typeof(GamePhysicsWorldBuildSystem)),
    UpdateInGroup(typeof(GameUpdateSystemGroup)), 
    UpdateAfter(typeof(GameAreaSystem))]
public partial struct GameAreaPrefabSystem : ISystem, IGameAreaHandler<GameAreaNeighborEnumerable, GameAreaPrefabSystem.Validator>
{
    private struct Version
    {
        public Hash128 guid;
        public int value;
        public int index;
    }

    public struct Validator : IGameAreaValidator
    {
        public BlobAssetReference<GameAreaDefinition> definition;
        [ReadOnly]
        public CollisionWorldContainer collisionWorld;
        [ReadOnly]
        public NativeParallelHashMap<int, bool> areaIndices;

        /*public bool Check(int areaIndex)
        {
            return areaIndexMap.ContainsKey(areaIndex);
        }*/

        public unsafe bool Check(int index, ref GameAreaCreateNodeCommand command)
        {
            if (!areaIndices.ContainsKey(command.areaIndex))
                return false;

            ref var definition = ref this.definition.Value;

            command.typeIndex = definition.typeIndices[command.typeIndex];

            ref var type = ref definition.types[command.typeIndex];
            if (type.collider.IsValid)
            {
                ColliderDistanceInput colliderDistanceInput = default;
                colliderDistanceInput.MaxDistance = type.maxDistance;
                colliderDistanceInput.Transform = command.transform;
                colliderDistanceInput.Collider = (Collider*)type.collider.GetUnsafePtr();
                if (colliderDistanceInput.Collider->Type == ColliderType.Compound)
                {
                    var children = ((CompoundCollider*)colliderDistanceInput.Collider)->Children;

                    ref CompoundCollider.Child child = ref children[0];
                    colliderDistanceInput.Collider = child.Collider;
                    colliderDistanceInput.Transform = math.mul(colliderDistanceInput.Transform, child.CompoundFromChild);
                }

                CollisionWorld collisionWorld = this.collisionWorld;
                if (collisionWorld.CalculateDistance(colliderDistanceInput))
                    return false;
            }

            return true;
        }
    }

    private struct BuildAreaIndices
    {
        public struct NeighborEnumerator : IGameAreaNeighborEnumerator
        {
            public NativeParallelHashMap<int, bool>.ParallelWriter areaIndices;

            public void Execute(int areaIndex)
            {
                areaIndices.TryAdd(areaIndex, true);
            }
        }

        public GameAreaNeighborEnumerable neighborEnumerable;

        [ReadOnly]
        public NativeArray<GameAreaNodePresentation> presentations;

        public NativeParallelHashMap<int, bool>.ParallelWriter areaIndices;

        public void Execute(int index)
        {
            int areaIndex = presentations[index].areaIndex;
            if (areaIndex == -1)
                return;

            NeighborEnumerator neighborEnumerator;
            neighborEnumerator.areaIndices = areaIndices;
            neighborEnumerator.Execute(areaIndex);

            neighborEnumerable.Execute(areaIndex, ref neighborEnumerator);
        }
    }

    [BurstCompile]
    private struct BuildAreaIndicesEx : IJobChunk
    {
        public GameAreaNeighborEnumerable neighborEnumerable;

        [ReadOnly]
        public ComponentTypeHandle<GameAreaNodePresentation> presentationType;

        public NativeParallelHashMap<int, bool>.ParallelWriter areaIndices;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            BuildAreaIndices buildAreaIndices;
            buildAreaIndices.neighborEnumerable = neighborEnumerable;
            buildAreaIndices.presentations = chunk.GetNativeArray(ref presentationType);
            buildAreaIndices.areaIndices = areaIndices;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                buildAreaIndices.Execute(i);
        }
    }

    [BurstCompile]
    private struct BuildPrefabIndices : IJobParallelFor
    {
        public GameAreaNeighborEnumerable neighborEnumerable;
        public BlobAssetReference<GameAreaPrefabDefinition> definition;
        public NativeParallelMultiHashMap<int, int>.ParallelWriter prefabIndices;

        public void Execute(int index)
        {
            prefabIndices.Add(neighborEnumerable.GetIndex(definition.Value.prefabs[index].transform.pos), index);
        }
    }

    [BurstCompile]
    private struct BuildPrefabVersions : IJobParallelFor
    {
        public BlobAssetReference<GameAreaPrefabDefinition> definition;

        [ReadOnly]
        public SharedHashMap<Hash128, int>.Reader inputs;

        public NativeList<Version>.ParallelWriter outputs;

        public void Execute(int index)
        {
            ref var prefab = ref definition.Value.prefabs[index];

            Version version;
            if (!inputs.TryGetValue(prefab.guid, out version.value) || version.value < prefab.version)
                return;

            version.index = index;
            version.guid = prefab.guid;
            outputs.AddNoResize(version);
        }
    }

    [BurstCompile]
    private struct ApplyPrefabVersions : IJob
    {
        [ReadOnly]
        public NativeList<Version> inputs;

        public SharedHashMap<Hash128, int>.Writer outputs;

        public void Execute()
        {
            outputs.Clear();

            foreach (var version in inputs)
                outputs.Add(version.guid, version.index);
        }
    }

    [BurstCompile]
    private struct ApplyAreaIndices : IJobParallelForDefer
    {
        [ReadOnly]
        public NativeArray<Version> versions;

        public NativeParallelHashMap<int, int>.ParallelWriter areaIndices;

        public void Execute(int index)
        {
            areaIndices.TryAdd(versions[index].value, -1);
        }
    }

    [BurstCompile]
    public struct ClearAreaIndices : IJob
    {
        [ReadOnly, DeallocateOnJobCompletion]
        public NativeArray<int> entityCount;
        public NativeParallelHashMap<int, bool> areaIndices;

        public void Execute()
        {
            int capacity = entityCount[0] * 27;
            if (areaIndices.Capacity < capacity)
                areaIndices.Capacity = capacity;

            areaIndices.Clear();
        }
    }

    private JobHandle __inputDeps;

    private GameAreaNeighborEnumerable __neighborEnumerable;

    private BlobAssetReference<GameAreaPrefabDefinition> __definition;

    private EntityQuery __enumerableGroup;
    private EntityQuery __definitionGroup;
    private EntityQuery __group;

    private ComponentTypeHandle<GameAreaNodePresentation> __presentationType;

    private GameAreaPrefabSystemCore __core;

    private NativeList<Version> __versions;

    private NativeParallelMultiHashMap<int, int> __prefabIndices;
    private NativeParallelHashMap<int, bool> __areaIndices;

    private EntityCommandPool<EntityData<GameAreaPrefab>> __addComponentCommanderPool;
    private EntityCommandPool<Entity> __removeComponentCommanderPool;
    private EntityCommandPool<GameAreaCreateNodeCommand> __createEntityCommanderPool;

    //private EndTimeSystemGroupEntityCommandSystem __endFrameBarrier;

    private SharedPhysicsWorld __physicsWorld;

    public const int MAX_NEIGHBOR_COUNT = 3 * 3 * 3 - 1;

    public static readonly int InnerloopBatchCount = 4;

    //public override double now => __syncSystemGourp.now;

    public SharedHashMap<Hash128, int> versions
    {
        get;

        private set;
    }

    public void Create<U>(World world, U commander) where U : GameAreaCreateEntityCommander
    {
        var endFrameBarrier = world.GetOrCreateSystemManaged<EndTimeSystemGroupEntityCommandSystem>();
        //__addComponentCommanderPool = endFrameBarrier.CreateAddComponentDataCommander<GameAreaPrefab>();
        //__destroyEntityCommanderPool = endFrameBarrier.CreateDestroyEntityCommander();
        __createEntityCommanderPool = endFrameBarrier.Create<GameAreaCreateNodeCommand, U>(EntityCommandManager.QUEUE_PRESENT, commander);
    }

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __enumerableGroup = builder
                    .WithAll<GameAreaNeighborEnumerable>()
                    .Build(ref state);

        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __definitionGroup = builder
                    .WithAll<GameAreaData>()
                    .Build(ref state);

        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __group = builder
                    .WithAll<GameAreaNodePresentation>()
                    .Build(ref state);

        state.RequireForUpdate(__group);

        __presentationType = state.GetComponentTypeHandle<GameAreaNodePresentation>(true);

        __core = new GameAreaPrefabSystemCore(ref state);

        __versions = new NativeList<Version>(Allocator.Persistent);

        __prefabIndices = new NativeParallelMultiHashMap<int, int>(1, Allocator.Persistent);
        __areaIndices = new NativeParallelHashMap<int, bool>(1, Allocator.Persistent);

        versions = new SharedHashMap<Hash128, int>(Allocator.Persistent);

        var world = state.WorldUnmanaged;
        ref var endFrameBarrier = ref world.GetExistingSystemUnmanaged<GameAreaStructChangeSystem>();
        __addComponentCommanderPool = endFrameBarrier.addComponentCommander;
        __removeComponentCommanderPool = endFrameBarrier.removeComponentCommander;

        __physicsWorld = world.GetExistingSystemUnmanaged<GamePhysicsWorldBuildSystem>().physicsWorld;
        //__syncSystemGourp = world.GetOrCreateSystem<GameSyncSystemGroup>();
    }

    //[BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        __core.Dispose();

        __prefabIndices.Dispose();

        __areaIndices.Dispose();

        versions.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        __inputDeps = state.Dependency;

        __neighborEnumerable = __enumerableGroup.GetSingleton<GameAreaNeighborEnumerable>();

        __core.Update<GameAreaNeighborEnumerable, Validator, GameAreaPrefabSystem>(
            MAX_NEIGHBOR_COUNT, 
            InnerloopBatchCount, 
            __addComponentCommanderPool,
            __removeComponentCommanderPool, 
            __createEntityCommanderPool, 
            ref this, 
            ref state);

        __physicsWorld.lookupJobManager.AddReadOnlyDependency(state.Dependency);
    }

    public void GetNeighborEnumerableAndPrefabIndices(
        in BlobAssetReference<GameAreaPrefabDefinition> definition,
        ref NativeParallelHashMap<int, int> areaIndices, 
        ref SystemState systemState, 
        out GameAreaNeighborEnumerable neighborEnumerable,
        out NativeParallelMultiHashMap<int, int> prefabIndices)
    {
        var jobHandle = systemState.Dependency;

        neighborEnumerable = __neighborEnumerable;
        prefabIndices = __prefabIndices;
        if (definition != __definition)
        {
            __definition = definition;

            ref var prefabs = ref definition.Value.prefabs;
            int numPrefabs = prefabs.Length;

            prefabIndices.Capacity = math.max(prefabIndices.Capacity, numPrefabs);
            prefabIndices.Clear();

            BuildPrefabIndices buildPrefabIndices;
            buildPrefabIndices.neighborEnumerable = neighborEnumerable;
            buildPrefabIndices.definition = definition;
            buildPrefabIndices.prefabIndices = prefabIndices.AsParallelWriter();
            var inputDeps = buildPrefabIndices.ScheduleByRef(numPrefabs, InnerloopBatchCount, __inputDeps);

            ref var versionJobManager = ref this.versions.lookupJobManager;
            versionJobManager.CompleteReadOnlyDependency();

            var writer = this.versions.writer;

            __versions.Clear();
            __versions.Capacity = math.max(__versions.Capacity, writer.Count());

            BuildPrefabVersions buildPrefabVersions;
            buildPrefabVersions.definition = definition;
            buildPrefabVersions.inputs = this.versions.reader;
            buildPrefabVersions.outputs = __versions.AsParallelWriter();
            jobHandle = buildPrefabVersions.ScheduleByRef(numPrefabs, InnerloopBatchCount, __inputDeps);

            ApplyPrefabVersions applyPrefabVersions;
            applyPrefabVersions.inputs = __versions;
            applyPrefabVersions.outputs = writer;
            jobHandle = applyPrefabVersions.ScheduleByRef(jobHandle);

            jobHandle = JobHandle.CombineDependencies(systemState.Dependency, inputDeps, jobHandle);
        }

        ApplyAreaIndices applyAreaIndices;
        applyAreaIndices.versions = __versions.AsDeferredJobArray();
        applyAreaIndices.areaIndices = areaIndices.AsParallelWriter();

        jobHandle = applyAreaIndices.ScheduleByRef(__versions, InnerloopBatchCount, jobHandle);

        systemState.Dependency = jobHandle;
    }

    public void GetValidatorAndVersions(
        ref SystemState systemState, 
        out Validator validator, 
        out SharedHashMap<Hash128, int> versions)
    {
        var entityCount = new NativeArray<int>(1, Allocator.TempJob, NativeArrayOptions.ClearMemory);

        var inputDeps = __group.CalculateEntityCountAsync(entityCount, __inputDeps);

        ClearAreaIndices clearAreaIndices;
        clearAreaIndices.entityCount = entityCount;
        clearAreaIndices.areaIndices = __areaIndices;
        inputDeps = clearAreaIndices.ScheduleByRef(inputDeps);

        BuildAreaIndicesEx buildAreaIndices;
        buildAreaIndices.neighborEnumerable = __neighborEnumerable;
        buildAreaIndices.presentationType = __presentationType.UpdateAsRef(ref systemState);
        buildAreaIndices.areaIndices = __areaIndices.AsParallelWriter();

        inputDeps = buildAreaIndices.ScheduleParallelByRef(__group, inputDeps);

        systemState.Dependency = JobHandle.CombineDependencies(systemState.Dependency, inputDeps, __physicsWorld.lookupJobManager.readOnlyJobHandle);

        validator.definition = __definitionGroup.GetSingleton<GameAreaData>().definition;
        validator.collisionWorld = __physicsWorld.collisionWorld;
        validator.areaIndices = __areaIndices;

        versions = this.versions;
    }
}