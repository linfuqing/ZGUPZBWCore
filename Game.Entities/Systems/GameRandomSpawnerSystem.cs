﻿using Unity.Jobs;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Transforms;
using ZG;
using Unity.Physics;

[assembly: RegisterGenericJobType(typeof(TimeManager<GameSpawnData>.UpdateEvents))]

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
    private float3 __velocity;
    private GameItemHandle __itemHandle;

    //private double __time;

    public GameSpawnInitializer(
        int assetIndex,
        in float3 velocity, 
        in GameItemHandle itemHandle)
    {
        __assetIndex = assetIndex;

        __velocity = velocity;

        __itemHandle = itemHandle;

        //__time = time;
    }

    public void Invoke<T>(ref T gameObjectEntity) where T : IGameObjectEntity
    {
        PhysicsVelocity physicsVelocity;
        physicsVelocity.Angular = float3.zero;
        physicsVelocity.Linear = __velocity;
        gameObjectEntity.SetComponentData(physicsVelocity);

        GameSpawnedInstanceData instance;
        instance.assetIndex = __assetIndex;
        //instance.time = __time;

        gameObjectEntity.AddComponentData(instance);

        gameObjectEntity.AddComponent<GameSpawnedInstanceDeadline>();

        gameObjectEntity.SetComponentEnabled<GameSpawnedInstanceData>(true);

        if (!__itemHandle.Equals(GameItemHandle.Empty))
        {
            GameItemRoot itemRoot;
            itemRoot.handle = __itemHandle;
            gameObjectEntity.SetComponentData(itemRoot);
            gameObjectEntity.SetComponentEnabled<GameItemRoot>(true);
        }
    }
}

[BurstCompile,
    CreateAfter(typeof(GameItemSystem)),
    CreateAfter(typeof(GameItemObjectInitSystem)),
    UpdateInGroup(typeof(TimeSystemGroup)), 
    UpdateAfter(typeof(GameSyncSystemGroup))]
public partial struct GameRandomSpawnerSystem : ISystem
{
    /*private struct Count
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
    }*/

    private struct Spawn
    {
        private struct AssetHandler : IRandomItemHandler
        {
            //public float vertical;

            //public float horizontal;

            //public double time;

            public int itemIdentityType;

            public double time;

            public RigidTransform transform;

            public Random random;

            public Entity entity;

            public EntityArchetype itemEntityArchetype;

            public EntityArchetype ownerEntityArchetype;

            public EntityArchetype levelEntityArchetype;

            public BlobAssetReference<GameRandomSpawnerDefinition> definition;

            public GameItemObjectInitSystem.Initializer initializer;

            public GameItemManager itemManager;

            [ReadOnly]
            public DynamicBuffer<GameRandomSpawnerAsset> assets;

            public TimeManager<GameSpawnData>.Writer results;

            public EntityComponentAssigner.Writer itemAssigner;

            public SharedHashMap<GameItemHandle, EntityArchetype>.Writer itemCreateEntityCommander;

            public SharedHashMap<Hash128, Entity>.Writer guidEntities;

            public RandomResult Set(int startIndex, int count)
            {
                float halfHorizontal, halfVertical;
                float2 distance;
                GameSpawnData result;
                //spawnData.time = time;
                result.entity = entity;
                GameRandomSpawnerAsset asset;
                for (int i = 0; i < count; ++i)
                {
                    asset = assets[startIndex + i];
                    result.itemHandle = Create(asset.index, entity);
                    result.assetIndex = asset.index;

                    distance = random.NextFloat2Direction() * random.NextFloat(-asset.velocityRadius, asset.velocityRadius);
                    result.velocity = asset.velocityOffset + math.float3(distance.x, 0.0f, distance.y);
                    result.velocity = math.mul(quaternion.LookRotationSafe(asset.velocityOffset, math.up()), result.velocity) + asset.velocityOffset;

                    halfHorizontal = asset.horizontal * 0.5f;
                    distance = random.NextFloat2Direction() * random.NextFloat(-halfHorizontal, halfHorizontal);

                    halfVertical = asset.vertical * 0.5f;
                    result.transform.pos =  math.float3(distance.x, random.NextFloat(-halfVertical, halfVertical), distance.y);
                    result.velocity -= result.transform.pos;
                    result.velocity = math.normalizesafe(result.velocity) * random.NextFloat(asset.minVelocity, asset.maxVelocity);
                    //result.velocity = math.mul(asset.offset.rot, result.velocity);

                    result.transform.pos += asset.offset.pos;
                    result.transform.rot = asset.offset.rot;// quaternion.LookRotationSafe(-math.normalize(spawnData.transform.pos), math.up());
                    if(asset.space == GameRandomSpawnerAsset.Space.Local)
                        result.transform = math.mul(transform, result.transform);

                    results.Invoke(random.NextFloat(asset.minTime, asset.maxTime) + time, result);
                }

                return RandomResult.Pass;
            }

            public GameItemHandle Create(int assetIndex, in Entity entity)
            {
                ref var asset = ref definition.Value.assets[assetIndex];
                int numItemTypes = asset.itemTypes.Length;
                if (numItemTypes < 1)
                    return GameItemHandle.Empty;

                GameItemHandle result = itemManager.Add(asset.itemTypes[numItemTypes - 1], 1), handle;
                for (int i = numItemTypes - 2; i >= 0; --i)
                {
                    handle = itemManager.Add(asset.itemTypes[i]);

                    itemManager.AttachSibling(handle, result);

                    result = handle;
                }

                if (initializer.IsVail(asset.itemTypes[0]))
                {
                    itemCreateEntityCommander.Add(result, asset.levelHandle == 0 ? ownerEntityArchetype : levelEntityArchetype);

                    var handleEntity = GameItemStructChangeFactory.Convert(result);

                    GameItemSystem.Init(
                        itemIdentityType,
                        result,
                        entity,
                        ref random,
                        ref guidEntities,
                        ref itemAssigner);

                    GameItemOwner owner;
                    owner.entity = entity;
                    itemAssigner.SetComponentData(handleEntity, owner);

                    if (asset.levelHandle != 0)
                    {
                        GameItemLevel level;
                        level.handle = asset.levelHandle;
                        itemAssigner.SetComponentData(handleEntity, level);
                    }
                }
                else
                    itemCreateEntityCommander.Add(result, itemEntityArchetype);

                return result;
            }
        }

        public int itemIdentityType;

        public double time;

        public Random random;

        public EntityArchetype itemEntityArchetype;

        public EntityArchetype ownerEntityArchetype;

        public EntityArchetype levelEntityArchetype;

        public BlobAssetReference<GameRandomSpawnerDefinition> definition;

        public GameItemObjectInitSystem.Initializer initializer;

        public GameItemManager itemManager;

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

        public TimeManager<GameSpawnData>.Writer results;

        public EntityComponentAssigner.Writer itemAssigner;

        public SharedHashMap<GameItemHandle, EntityArchetype>.Writer itemCreateEntityCommander;

        public SharedHashMap<Hash128, Entity>.Writer guidEntities;

        public void Execute(int index)
        {
            var nodes = this.nodes[index];
            int length = nodes.Length;
            if (length < 1)
                return;

            Entity entity = entityArray[index];

            AssetHandler assetHandler;
            assetHandler.itemIdentityType = itemIdentityType;
            assetHandler.time = time;
            assetHandler.transform = math.RigidTransform(rotations[index].Value, translations[index].Value);
            assetHandler.random = random;
            assetHandler.entity = entity;
            assetHandler.itemEntityArchetype = itemEntityArchetype;
            assetHandler.ownerEntityArchetype = ownerEntityArchetype;
            assetHandler.levelEntityArchetype = levelEntityArchetype;
            assetHandler.definition = definition;
            assetHandler.initializer = initializer;
            assetHandler.itemManager = itemManager;
            assetHandler.assets = assets[index];
            assetHandler.results = results;
            assetHandler.itemAssigner = itemAssigner;
            assetHandler.itemCreateEntityCommander = itemCreateEntityCommander;
            assetHandler.guidEntities = guidEntities;

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
        public int itemIdentityType;

        public double time;

        public EntityArchetype itemEntityArchetype;

        public EntityArchetype ownerEntityArchetype;

        public EntityArchetype levelEntityArchetype;

        public BlobAssetReference<GameRandomSpawnerDefinition> definition;

        public GameItemObjectInitSystem.Initializer initializer;

        public GameItemManager itemManager;

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

        public TimeManager<GameSpawnData>.Writer results;

        public EntityComponentAssigner.Writer itemAssigner;

        public SharedHashMap<GameItemHandle, EntityArchetype>.Writer itemCreateEntityCommander;

        public SharedHashMap<Hash128, Entity>.Writer guidEntities;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Spawn spawn;
            spawn.itemIdentityType = itemIdentityType;
            spawn.time = time;
            uint hash = RandomUtility.Hash(time);
            spawn.random = new Random(hash ^ (uint)unfilteredChunkIndex);
            spawn.itemEntityArchetype = itemEntityArchetype;
            spawn.ownerEntityArchetype = ownerEntityArchetype;
            spawn.levelEntityArchetype = levelEntityArchetype;
            spawn.definition = definition;
            spawn.initializer = initializer;
            spawn.itemManager = itemManager;
            spawn.entityArray = chunk.GetNativeArray(entityType);
            spawn.rotations = chunk.GetNativeArray(ref rotationType);
            spawn.translations = chunk.GetNativeArray(ref translationType);
            spawn.slices = chunk.GetBufferAccessor(ref sliceType);
            spawn.groups = chunk.GetBufferAccessor(ref groupType);
            spawn.assets = chunk.GetBufferAccessor(ref assetType);
            spawn.nodes = chunk.GetBufferAccessor(ref nodeType);
            spawn.results = results;
            spawn.itemAssigner = itemAssigner;
            spawn.itemCreateEntityCommander = itemCreateEntityCommander;
            spawn.guidEntities = guidEntities;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
            {
                spawn.Execute(i);

                chunk.SetComponentEnabled(ref nodeType, i, false);
            }
        }
    }

    /*[BurstCompile]
    private struct CreateItems : IJob
    {
        public double time;

        public int itemIdentityType;

        public EntityArchetype itemEntityArchetype;

        public EntityArchetype ownerEntityArchetype;

        public EntityArchetype levelEntityArchetype;

        public BlobAssetReference<GameRandomSpawnerDefinition> definition;

        public GameItemManager itemManager;

        public GameItemObjectInitSystem.Initializer initializer;

        public EntityComponentAssigner.Writer itemAssigner;

        public SharedHashMap<Hash128, Entity>.Writer guidEntities;

        public SharedHashMap<GameItemHandle, EntityArchetype>.Writer itemCreateEntityCommander;

        public NativeList<GameSpawnData> results;

        public TimeManager<GameSpawnData>.Writer timeManager;

        public void Execute()
        {
            ref var assets = ref definition.Value.assets;
            GameItemOwner owner;
            GameItemLevel level;
            GameItemHandle handle;
            Entity entity;
            var random = RandomUtility.Create(time);
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

                    result.itemHandle = itemManager.Add(asset.itemTypes[numItemTypes - 1], 1);
                    for (j = numItemTypes - 2; j >= 0; --j)
                    {
                        handle = itemManager.Add(asset.itemTypes[j]);

                        itemManager.AttachSibling(handle, result.itemHandle);

                        result.itemHandle = handle;
                    }

                    if (initializer.IsVail(asset.itemTypes[0]))
                    {
                        itemCreateEntityCommander.Add(result.itemHandle, asset.levelHandle == 0 ? ownerEntityArchetype : levelEntityArchetype);

                        entity = GameItemStructChangeFactory.Convert(result.itemHandle);

                        GameItemSystem.Init(
                            itemIdentityType,
                            result.itemHandle,
                            entity,
                            ref random,
                            ref guidEntities,
                            ref itemAssigner);

                        owner.entity = result.entity;
                        itemAssigner.SetComponentData(entity, owner);

                        if (asset.levelHandle != 0)
                        {
                            level.handle = asset.levelHandle;
                            itemAssigner.SetComponentData(entity, level);
                        }
                    }
                    else
                        itemCreateEntityCommander.Add(result.itemHandle, itemEntityArchetype);

                    timeManager.Invoke(result);
                }
            }
        }
    }*/

    private EntityQuery __group;
    //private EntityQuery __factoryGroup;
    private GameSyncTime __time;

    private EntityArchetype __itemEntityArchetype;
    private EntityArchetype __ownerEntityArchetype;
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

    private GameItemObjectInitSystem.Initializer __initializer;

    private SharedHashMap<Hash128, Entity> __guidEntities;

    public SharedList<GameSpawnData> commands
    {
        get;

        private set;
    }
    
    public SharedTimeManager<GameSpawnData> timeManager
    {
        readonly get;

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

        state.RequireForUpdate(__group);
        state.RequireForUpdate<GameItemStructChangeManager>();
        state.RequireForUpdate<GameRandomSpawnerData>();
        state.RequireForUpdate<GameItemIdentityType>();
        
        /*using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __factoryGroup = builder
                .WithAll<GameRandomSpawnerFactory>()
                .Build(ref state);*/

        __time = new GameSyncTime(ref state);

        __entityType = state.GetEntityTypeHandle();
        __rotationType = state.GetComponentTypeHandle<Rotation>(true);
        __translationType = state.GetComponentTypeHandle<Translation>(true);
        __sliceType = state.GetBufferTypeHandle<GameRandomSpawnerSlice>(true);
        __groupType = state.GetBufferTypeHandle<GameRandomSpawnerGroup>(true);
        __assetType = state.GetBufferTypeHandle<GameRandomSpawnerAsset>(true);
        __nodeType = state.GetBufferTypeHandle<GameRandomSpawnerNode>();
        __commands = state.GetComponentLookup<GameEntityActionCommand>();

        var world = state.WorldUnmanaged;
        ref var itemSystem = ref world.GetExistingSystemUnmanaged<GameItemSystem>();

        __itemEntityArchetype = itemSystem.entityArchetype;

        var entityManager = state.EntityManager;
        using (var componentTypes = __itemEntityArchetype.GetComponentTypes(Allocator.Temp))
        {
            var componentTypeList = new NativeList<ComponentType>(Allocator.Temp);
            componentTypeList.AddRange(componentTypes);
            componentTypeList.Add(ComponentType.ReadWrite<GameItemOwner>());

            __ownerEntityArchetype = entityManager.CreateArchetype(componentTypeList.AsArray());

            componentTypeList.Add(ComponentType.ReadWrite<GameItemLevel>());

            __levelEntityArchetype = entityManager.CreateArchetype(componentTypeList.AsArray());

            componentTypeList.Dispose();
        }

        timeManager = new SharedTimeManager<GameSpawnData>(Allocator.Persistent);

        __itemManager = itemSystem.manager;

        __initializer = world.GetExistingSystemUnmanaged<GameItemObjectInitSystem>().initializer;

        __guidEntities = world.GetExistingSystemUnmanaged<EntityDataSystem>().guidEntities;

        commands = new SharedList<GameSpawnData>(Allocator.Persistent);

        GameRandomSpawnerFactory factory;
        factory.commands = commands;
        state.EntityManager.AddComponentData(state.SystemHandle, factory);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        timeManager.Dispose();

        commands.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        /*var sliceType = __sliceType.UpdateAsRef(ref state);
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
        jobHandle = recapcity.ScheduleByRef(JobHandle.CombineDependencies(jobHandle, commandsJobManager.readWriteJobHandle));*/

        double time = __time.nextTime;

        var itemStructChangeManager = SystemAPI.GetSingleton<GameItemStructChangeManager>();
        var itemAssigner = itemStructChangeManager.assigner;
        var createEntityCommander = itemStructChangeManager.createEntityCommander;

        SpawnEx spawn;
        spawn.time = time;
        spawn.itemIdentityType = SystemAPI.GetSingleton<GameItemIdentityType>().value;
        spawn.itemEntityArchetype = __itemEntityArchetype;
        spawn.ownerEntityArchetype = __ownerEntityArchetype;
        spawn.levelEntityArchetype = __levelEntityArchetype;
        spawn.definition = SystemAPI.GetSingleton<GameRandomSpawnerData>().definition;
        spawn.initializer = __initializer;
        spawn.itemManager = __itemManager.value;
        spawn.entityType = __entityType.UpdateAsRef(ref state);
        spawn.rotationType = __rotationType.UpdateAsRef(ref state);
        spawn.translationType = __translationType.UpdateAsRef(ref state);
        spawn.assetType = __assetType.UpdateAsRef(ref state);
        spawn.sliceType = __sliceType.UpdateAsRef(ref state);// sliceType;
        spawn.groupType = __groupType.UpdateAsRef(ref state);// groupType;
        spawn.nodeType = __nodeType.UpdateAsRef(ref state);// nodeType;
        spawn.commands = __commands.UpdateAsRef(ref state);
        spawn.itemAssigner = itemAssigner.writer;
        spawn.guidEntities = __guidEntities.writer;
        spawn.itemCreateEntityCommander = createEntityCommander.writer;
        spawn.results = timeManager.value.writer;

        ref var createEntityCommanderJobManager = ref createEntityCommander.lookupJobManager;
        ref var guidEntitiesJobManager = ref __guidEntities.lookupJobManager;
        ref var itemManagerJobManager = ref __itemManager.lookupJobManager;
        var jobHandle = JobHandle.CombineDependencies(
            guidEntitiesJobManager.readWriteJobHandle,
            createEntityCommanderJobManager.readWriteJobHandle,
            itemManagerJobManager.readWriteJobHandle);

        jobHandle = JobHandle.CombineDependencies(jobHandle, itemAssigner.jobHandle, state.Dependency);
        jobHandle = spawn.ScheduleByRef(__group, jobHandle);

        itemManagerJobManager.readWriteJobHandle = jobHandle;
        guidEntitiesJobManager.readWriteJobHandle = jobHandle;
        createEntityCommanderJobManager.readWriteJobHandle = jobHandle;

        itemAssigner.jobHandle = jobHandle;

        var commands = this.commands;
        NativeList<GameSpawnData> commandsWriter = commands.writer;
        ref var commandsJobManager = ref commands.lookupJobManager;

        jobHandle = timeManager.Update(time, ref commandsWriter, JobHandle.CombineDependencies(jobHandle, commandsJobManager.readWriteJobHandle));

        commandsJobManager.readWriteJobHandle = jobHandle;

        state.Dependency = jobHandle;
    }
}
