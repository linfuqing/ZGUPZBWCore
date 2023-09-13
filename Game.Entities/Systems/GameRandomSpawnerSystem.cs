using Unity.Jobs;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Transforms;
using ZG;

public struct GameRandomSpawnerDefinition
{
    public struct Asset
    {
        public BlobArray<int> itemTypes;

        public int levelHandle;
    }

    public BlobArray<Asset> assets;
}

public struct GameRandomSpawnerData : IComponentData
{
    public BlobAssetReference<GameRandomSpawnerDefinition> definition;
}

public struct GameRandomSpawnerFactory : IComponentData
{
    public SharedList<GameSpawnData> commands;
}

public struct GameSpawnInitializer : IEntityDataInitializer
{
    private int __assetIndex;
    private GameItemHandle __itemHandle;

    //private double __time;

    public GameSpawnInitializer(int assetIndex, in GameItemHandle itemHandle)
    {
        __assetIndex = assetIndex;

        __itemHandle = itemHandle;

        //__time = time;
    }

    public void Invoke<T>(ref T gameObjectEntity) where T : IGameObjectEntity
    {
        GameSpawnedInstanceData instance;
        instance.assetIndex = __assetIndex;
        //instance.time = __time;

        gameObjectEntity.AddComponentData(instance);

        if (!__itemHandle.Equals(GameItemHandle.Empty))
        {
            GameItemRoot itemRoot;
            itemRoot.handle = __itemHandle;
            gameObjectEntity.SetComponentData(itemRoot);
        }
    }
}

[BurstCompile,
    CreateAfter(typeof(GameItemSystem)),
    UpdateInGroup(typeof(TimeSystemGroup)), 
    UpdateAfter(typeof(GameSyncSystemGroup))]
public partial struct GameRandomSpawnerSystem : ISystem
{
    private struct Count
    {
        [NativeDisableParallelForRestriction]
        public NativeArray<int> counter;

        [ReadOnly]
        public BufferAccessor<GameRandomSpawnerSlice> slices;

        [ReadOnly]
        public BufferAccessor<GameRandomSpawnerGroup> groups;

        [ReadOnly]
        public BufferAccessor<GameRandomSpawnerNode> nodes;

        public void Execute(int index)
        {
            var nodes = this.nodes[index];
            int length = nodes.Length;
            if (length < 1)
                return;

            int count = 0;
            var groups = this.groups[index].Reinterpret<RandomGroup>().AsNativeArray();
            var slices = this.slices[index];
            GameRandomSpawnerSlice slice;
            for (int i = 0; i < length; ++i)
            {
                slice = slices[nodes[i].sliceIndex];
                count += RandomUtility.CountOf(groups.Slice(slice.groupStartIndex, slice.groupCount));
            }

            counter.Add(0, count);
        }
    }

    [BurstCompile]
    private struct CountEx : IJobChunk
    {
        [NativeDisableParallelForRestriction]
        public NativeArray<int> counter;

        [ReadOnly]
        public BufferTypeHandle<GameRandomSpawnerSlice> sliceType;

        [ReadOnly]
        public BufferTypeHandle<GameRandomSpawnerGroup> groupType;

        [ReadOnly]
        public BufferTypeHandle<GameRandomSpawnerNode> nodeType;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Count count;
            count.counter = counter;
            count.slices = chunk.GetBufferAccessor(ref sliceType);
            count.groups = chunk.GetBufferAccessor(ref groupType);
            count.nodes = chunk.GetBufferAccessor(ref nodeType);

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                count.Execute(i);
        }
    }

    [BurstCompile]
    public struct Recapcity : IJob
    {
        [ReadOnly, DeallocateOnJobCompletion]
        public NativeArray<int> counter;

        public NativeList<GameSpawnData> results;

        public void Execute()
        {
            results.Capacity = math.max(results.Capacity, results.Length + counter[0]);
        }
    }

    private struct Spawn
    {
        private struct AssetHandler : IRandomItemHandler
        {
            //public float vertical;

            //public float horizontal;

            //public double time;

            public RigidTransform transform;

            public Entity entity;

            public Random random;

            [ReadOnly]
            public DynamicBuffer<GameRandomSpawnerAsset> assets;

            public SharedList<GameSpawnData>.ParallelWriter results;

            public RandomResult Set(int startIndex, int count)
            {
                GameSpawnData spawnData;
                //spawnData.time = time;
                spawnData.entity = entity;
                GameRandomSpawnerAsset asset;
                for (int i = 0; i < count; ++i)
                {
                    asset = assets[startIndex + i];
                    spawnData.assetIndex = asset.index;
                    spawnData.transform.pos.x = asset.horizontal * random.NextFloat();
                    spawnData.transform.pos.y = asset.vertical;
                    spawnData.transform.pos.z = asset.horizontal * random.NextFloat();
                    spawnData.transform.pos += asset.offset.pos;
                    spawnData.transform.rot = asset.offset.rot;// quaternion.LookRotationSafe(-math.normalize(spawnData.transform.pos), math.up());
                    spawnData.transform = math.mul(transform, spawnData.transform);
                    spawnData.itemHandle = GameItemHandle.Empty;

                    results.AddNoResize(spawnData);
                }

                return RandomResult.Pass;
            }
        }
        
        //public double time;

        public Random random;

        [ReadOnly]
        public NativeArray<Entity> entityArray;

        [ReadOnly]
        public NativeArray<Translation> translations;

        [ReadOnly]
        public NativeArray<Rotation> rotations;

        [ReadOnly]
        public BufferAccessor<GameRandomSpawnerAsset> assets;

        [ReadOnly]
        public BufferAccessor<GameRandomSpawnerSlice> slices;

        [ReadOnly]
        public BufferAccessor<GameRandomSpawnerGroup> groups;

        public BufferAccessor<GameRandomSpawnerNode> nodes;
        
        public SharedList<GameSpawnData>.ParallelWriter results;

        public void Execute(int index)
        {
            var nodes = this.nodes[index];
            int length = nodes.Length;
            if (length < 1)
                return;

            Entity entity = entityArray[index];

            AssetHandler assetHandler;
            //assetHandler.time = time;
            assetHandler.entity = entity;
            assetHandler.random = random;
            assetHandler.transform = math.RigidTransform(rotations[index].Value, translations[index].Value);
            assetHandler.assets = assets[index];
            assetHandler.results = results;

            var groups = this.groups[index].Reinterpret<RandomGroup>().AsNativeArray();
            var slices = this.slices[index];
            GameRandomSpawnerSlice slice;
            for (int i = 0; i < length; ++i)
            {
                slice = slices[nodes[i].sliceIndex];
                /*assetHandler.vertical = slice.vertical;
                assetHandler.horizontal = slice.horizontal;
                assetHandler.transform = math.mul(transform, slice.offset);*/
                random.Next(ref assetHandler, groups.Slice(slice.groupStartIndex, slice.groupCount));
            }

            nodes.Clear();
        }
    }

    [BurstCompile]
    private struct SpawnEx : IJobChunk//, IEntityCommandProducerJob
    {
        public double time;

        [ReadOnly]
        public EntityTypeHandle entityType;

        [ReadOnly]
        public ComponentTypeHandle<Rotation> rotationType;

        [ReadOnly]
        public ComponentTypeHandle<Translation> translationType;

        [ReadOnly]
        public BufferTypeHandle<GameRandomSpawnerAsset> assetType;

        [ReadOnly]
        public BufferTypeHandle<GameRandomSpawnerSlice> sliceType;

        [ReadOnly]
        public BufferTypeHandle<GameRandomSpawnerGroup> groupType;

        public BufferTypeHandle<GameRandomSpawnerNode> nodeType;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<GameEntityActionCommand> commands;

        public SharedList<GameSpawnData>.ParallelWriter results;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Spawn spawn;
            //spawn.time = time;
            long hash = math.aslong(time);
            spawn.random = new Random((uint)((int)(hash >> 32) ^ ((int)hash) ^ unfilteredChunkIndex));
            spawn.entityArray = chunk.GetNativeArray(entityType);
            spawn.rotations = chunk.GetNativeArray(ref rotationType);
            spawn.translations = chunk.GetNativeArray(ref translationType);
            spawn.slices = chunk.GetBufferAccessor(ref sliceType);
            spawn.groups = chunk.GetBufferAccessor(ref groupType);
            spawn.assets = chunk.GetBufferAccessor(ref assetType);
            spawn.nodes = chunk.GetBufferAccessor(ref nodeType);
            spawn.results = results;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
            {
                spawn.Execute(i);

                chunk.SetComponentEnabled(ref nodeType, i, false);
            }
        }
    }

    [BurstCompile]
    private struct CreateItems : IJob
    {
        public EntityArchetype itemEntityArchetype;

        public EntityArchetype levelEntityArchetype;

        public BlobAssetReference<GameRandomSpawnerDefinition> definition;

        public GameItemManager itemManager;

        public EntityComponentAssigner.Writer itemAssigner;

        public SharedHashMap<GameItemHandle, EntityArchetype>.Writer itemCreateEntityCommander;

        public NativeList<GameSpawnData> results;

        public void Execute()
        {
            ref var assets = ref definition.Value.assets;
            Entity entity;
            GameItemOwner owner;
            GameItemLevel level;
            GameItemHandle handle;
            int i, j, numItemTypes, numResults = results.Length;
            for(i = 0; i < numResults; ++i)
            {
                ref var result = ref results.ElementAt(i);
                if (result.itemHandle.Equals(GameItemHandle.Empty))
                {
                    ref var asset = ref assets[result.assetIndex];
                    numItemTypes = asset.itemTypes.Length;
                    if (numItemTypes < 1)
                        continue;

                    result.itemHandle = itemManager.Add(asset.itemTypes[numItemTypes - 1]);
                    for (j = numItemTypes - 2; j >= 0; --j)
                    {
                        handle = itemManager.Add(asset.itemTypes[j]);

                        itemManager.AttachSibling(handle, result.itemHandle);

                        result.itemHandle = handle;
                    }

                    if (asset.levelHandle == 0)
                        itemCreateEntityCommander.Add(result.itemHandle, itemEntityArchetype);
                    else
                    {
                        itemCreateEntityCommander.Add(result.itemHandle, levelEntityArchetype);

                        entity = GameItemStructChangeFactory.Convert(result.itemHandle);

                        level.handle = asset.levelHandle;
                        itemAssigner.SetComponentData(entity, level);

                        owner.entity = result.entity;
                        itemAssigner.SetComponentData(entity, owner);
                    }
                }
            }
        }
    }

    private EntityQuery __group;
    //private EntityQuery __factoryGroup;
    private GameSyncTime __time;

    private EntityArchetype __itemEntityArchetype;
    private EntityArchetype __levelEntityArchetype;

    private EntityTypeHandle __entityType;

    private ComponentTypeHandle<Rotation> __rotationType;

    private ComponentTypeHandle<Translation> __translationType;

    private BufferTypeHandle<GameRandomSpawnerSlice> __sliceType;

    private BufferTypeHandle<GameRandomSpawnerGroup> __groupType;

    private BufferTypeHandle<GameRandomSpawnerAsset> __assetType;

    private BufferTypeHandle<GameRandomSpawnerNode> __nodeType;

    private ComponentLookup<GameEntityActionCommand> __commands;

    private GameItemManagerShared __itemManager;

    public SharedList<GameSpawnData> commands
    {
        get;

        private set;
    }

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        using(var builder = new EntityQueryBuilder(Allocator.Temp))
            __group = builder
                    .WithAll<Translation, GameRandomSpawnerSlice, GameRandomSpawnerGroup, GameRandomSpawnerAsset>()
                    .WithAllRW<GameRandomSpawnerNode>()
                    .WithOptions(EntityQueryOptions.IncludeDisabledEntities)
                    .Build(ref state);

        __group.SetChangedVersionFilter(ComponentType.ReadWrite<GameRandomSpawnerNode>());

        /*using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __factoryGroup = builder
                .WithAll<GameRandomSpawnerFactory>()
                .Build(ref state);*/

        __entityType = state.GetEntityTypeHandle();
        __rotationType = state.GetComponentTypeHandle<Rotation>(true);
        __translationType = state.GetComponentTypeHandle<Translation>(true);
        __sliceType = state.GetBufferTypeHandle<GameRandomSpawnerSlice>(true);
        __groupType = state.GetBufferTypeHandle<GameRandomSpawnerGroup>(true);
        __assetType = state.GetBufferTypeHandle<GameRandomSpawnerAsset>(true);
        __nodeType = state.GetBufferTypeHandle<GameRandomSpawnerNode>();
        __commands = state.GetComponentLookup<GameEntityActionCommand>();

        __time = new GameSyncTime(ref state);

        ref var itemSystem = ref state.WorldUnmanaged.GetExistingSystemUnmanaged<GameItemSystem>();

        __itemEntityArchetype = itemSystem.entityArchetype;

        using (var componentTypes = __itemEntityArchetype.GetComponentTypes(Allocator.Temp))
        {
            var componentTypeList = new NativeList<ComponentType>(Allocator.Temp);
            componentTypeList.AddRange(componentTypes);
            componentTypeList.Add(ComponentType.ReadWrite<GameItemLevel>());
            componentTypeList.Add(ComponentType.ReadWrite<GameItemOwner>());

            __levelEntityArchetype = state.EntityManager.CreateArchetype(componentTypeList.AsArray());

            componentTypeList.Dispose();
        }

        __itemManager = itemSystem.manager;

        commands = new SharedList<GameSpawnData>(Allocator.Persistent);

        GameRandomSpawnerFactory factory;
        factory.commands = commands;
        state.EntityManager.AddComponentData(state.SystemHandle, factory);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        commands.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var sliceType = __sliceType.UpdateAsRef(ref state);
        var groupType = __groupType.UpdateAsRef(ref state);
        var nodeType = __nodeType.UpdateAsRef(ref state);

        var counter = new NativeArray<int>(1, Allocator.TempJob, NativeArrayOptions.ClearMemory);

        CountEx count;
        count.counter = counter;
        count.sliceType = sliceType;
        count.groupType = groupType;
        count.nodeType = nodeType;
        var jobHandle = count.ScheduleParallelByRef(__group, state.Dependency);

        var commands = this.commands;
        ref var commandsJobManager = ref commands.lookupJobManager;

        Recapcity recapcity;
        recapcity.counter = counter;
        recapcity.results = commands.writer;
        jobHandle = recapcity.ScheduleByRef(JobHandle.CombineDependencies(jobHandle, commandsJobManager.readWriteJobHandle));

        SpawnEx spawn;
        spawn.time = __time.nextTime;
        spawn.entityType = __entityType.UpdateAsRef(ref state);
        spawn.rotationType = __rotationType.UpdateAsRef(ref state);
        spawn.translationType = __translationType.UpdateAsRef(ref state);
        spawn.assetType = __assetType.UpdateAsRef(ref state);
        spawn.sliceType = sliceType;
        spawn.groupType = groupType;
        spawn.nodeType = nodeType;
        spawn.commands = __commands.UpdateAsRef(ref state);
        spawn.results = commands.parallelWriter;

        jobHandle = spawn.ScheduleParallelByRef(__group, jobHandle);

        //entityManager.AddJobHandleForProducer<SpawnEx>(jobHandle);

        if (SystemAPI.HasSingleton<GameRandomSpawnerData>())
        {
            var itemStructChangeManager = SystemAPI.GetSingleton<GameItemStructChangeManager>();
            var itemAssigner = itemStructChangeManager.assigner;
            var createEntityCommander = itemStructChangeManager.createEntityCommander;

            CreateItems createItems;
            createItems.itemEntityArchetype = __itemEntityArchetype;
            createItems.levelEntityArchetype = __levelEntityArchetype;
            createItems.definition = SystemAPI.GetSingleton<GameRandomSpawnerData>().definition;
            createItems.itemManager = __itemManager.value;
            createItems.itemAssigner = itemAssigner.writer;
            createItems.itemCreateEntityCommander = createEntityCommander.writer;
            createItems.results = commands.writer;

            ref var createEntityCommanderJobManager = ref createEntityCommander.lookupJobManager;
            ref var itemManagerJobManager = ref __itemManager.lookupJobManager;
            var temp = JobHandle.CombineDependencies(itemAssigner.jobHandle, createEntityCommanderJobManager.readWriteJobHandle, itemManagerJobManager.readWriteJobHandle);
            jobHandle = createItems.ScheduleByRef(JobHandle.CombineDependencies(temp, jobHandle));
            itemManagerJobManager.readWriteJobHandle = jobHandle;
            createEntityCommanderJobManager.readWriteJobHandle = jobHandle;

            itemAssigner.jobHandle = jobHandle;
        }

        commandsJobManager.readWriteJobHandle = jobHandle;

        state.Dependency = jobHandle;
    }
}
