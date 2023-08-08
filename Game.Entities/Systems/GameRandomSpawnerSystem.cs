using Unity.Jobs;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Transforms;
using ZG;

public struct GameRandomSpawnerFactory : IComponentData
{
    public SharedList<GameSpawnData> commands;
}

public struct GameSpawnInitializer : IEntityDataInitializer
{
    private int __assetIndex;

    //private double __time;

    public GameSpawnInitializer(int assetIndex)
    {
        __assetIndex = assetIndex;

        //__time = time;
    }

    public void Invoke<T>(ref T gameObjectEntity) where T : IGameObjectEntity
    {
        GameSpawnedInstanceData instance;
        instance.assetIndex = __assetIndex;
        //instance.time = __time;

        gameObjectEntity.AddComponentData(instance);
    }
}

[BurstCompile, UpdateInGroup(typeof(TimeSystemGroup)), UpdateAfter(typeof(GameSyncSystemGroup))]
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

            var groups = this.groups[index].Reinterpret<RandomGroup>().AsNativeArray();
            var slices = this.slices[index];
            GameRandomSpawnerSlice slice;
            for (int i = 0; i < length; ++i)
            {
                slice = slices[nodes[i].sliceIndex];
                counter.Add(0, RandomUtility.CountOf(groups.Slice(slice.groupStartIndex, slice.groupCount)));
            }
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

    private EntityQuery __group;
    //private EntityQuery __factoryGroup;
    private GameSyncTime __time;

    private EntityTypeHandle __entityType;

    private ComponentTypeHandle<Rotation> __rotationType;

    private ComponentTypeHandle<Translation> __translationType;

    private BufferTypeHandle<GameRandomSpawnerSlice> __sliceType;

    private BufferTypeHandle<GameRandomSpawnerGroup> __groupType;

    private BufferTypeHandle<GameRandomSpawnerAsset> __assetType;

    private BufferTypeHandle<GameRandomSpawnerNode> __nodeType;

    private ComponentLookup<GameEntityActionCommand> __commands;

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

        commandsJobManager.readWriteJobHandle = jobHandle;

        //entityManager.AddJobHandleForProducer<SpawnEx>(jobHandle);

        state.Dependency = jobHandle;
    }
}
