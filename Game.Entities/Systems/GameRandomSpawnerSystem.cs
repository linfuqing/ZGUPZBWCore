using Unity.Jobs;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Transforms;
using ZG;

[UpdateInGroup(typeof(TimeSystemGroup)), UpdateAfter(typeof(GameSyncSystemGroup))]
public partial class GameRandomSpawnerSystem : SystemBase
{
    private struct Spawn
    {
        private struct AssetHandler : IRandomItemHandler
        {
            public float vertical;

            public float horizontal;

            //public double time;

            public float3 position;

            public Entity entity;

            public Random random;

            [ReadOnly]
            public DynamicBuffer<GameRandomSpawnerAsset> assets;

            public EntityCommandQueue<GameSpawnData>.ParallelWriter entityManager;

            public RandomItemType Set(int startIndex, int count)
            {
                GameSpawnData spawnData;
                //spawnData.time = time;
                spawnData.entity = entity;
                for (int i = 0; i < count; ++i)
                {
                    spawnData.assetIndex = assets[startIndex + i].index;
                    spawnData.transform.pos.x = horizontal * random.NextFloat();
                    spawnData.transform.pos.y = vertical;
                    spawnData.transform.pos.z = horizontal * random.NextFloat();
                    spawnData.transform.rot = quaternion.LookRotationSafe(-math.normalize(spawnData.transform.pos), math.up());
                    spawnData.transform.pos += position;

                    entityManager.Enqueue(spawnData);
                }

                return RandomItemType.Pass;
            }
        }
        
        //public double time;

        public Random random;

        [ReadOnly]
        public NativeArray<Entity> entityArray;

        [ReadOnly]
        public NativeArray<Translation> translations;
        
        [ReadOnly]
        public BufferAccessor<GameRandomSpawnerSlice> slices;

        [ReadOnly]
        public BufferAccessor<GameRandomSpawnerGroup> groups;

        [ReadOnly]
        public BufferAccessor<GameRandomSpawnerAsset> assets;
        
        [ReadOnly]
        public BufferAccessor<GameRandomSpawnerNode> nodes;
        
        public EntityCommandQueue<GameSpawnData>.ParallelWriter entityManager;

        public NativeQueue<Entity>.ParallelWriter nodesToClear;

        public void Execute(int index)
        {
            var nodes = this.nodes[index];
            int length = nodes.Length;
            if (length < 1)
                return;

            Entity entity = entityArray[index];

            AssetHandler assetHandler;
            //assetHandler.time = time;
            assetHandler.position = translations[index].Value;
            assetHandler.entity = entity;
            assetHandler.random = random;
            assetHandler.assets = assets[index];
            assetHandler.entityManager = entityManager;
            
            var groups = this.groups[index];
            var slices = this.slices[index];
            GameRandomSpawnerSlice slice;
            for (int i = 0; i < length; ++i)
            {
                slice = slices[nodes[i].sliceIndex];
                assetHandler.vertical = slice.vertical;
                assetHandler.horizontal = slice.horizontal;
                random.Next(ref assetHandler, groups.Reinterpret<RandomGroup>().AsNativeArray().Slice(slice.groupStartIndex, slice.groupCount));
            }

            nodesToClear.Enqueue(entity);
        }
    }

    [BurstCompile]
    private struct SpawnEx : IJobChunk, IEntityCommandProducerJob
    {
        public double time;

        [ReadOnly]
        public EntityTypeHandle entityType;

        [ReadOnly]
        public ComponentTypeHandle<Translation> translationType;
        
        [ReadOnly]
        public BufferTypeHandle<GameRandomSpawnerSlice> sliceType;

        [ReadOnly]
        public BufferTypeHandle<GameRandomSpawnerGroup> groupType;

        [ReadOnly]
        public BufferTypeHandle<GameRandomSpawnerAsset> assetType;
        
        [ReadOnly]
        public BufferTypeHandle<GameRandomSpawnerNode> nodeType;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<GameEntityActionCommand> commands;

        public EntityCommandQueue<GameSpawnData>.ParallelWriter entityManager;

        public NativeQueue<Entity>.ParallelWriter nodesToClear;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Spawn spawn;
            //spawn.time = time;
            long hash = math.aslong(time);
            spawn.random = new Random((uint)((int)(hash >> 32) ^ ((int)hash) ^ unfilteredChunkIndex));
            spawn.entityArray = chunk.GetNativeArray(entityType);
            spawn.translations = chunk.GetNativeArray(ref translationType);
            spawn.slices = chunk.GetBufferAccessor(ref sliceType);
            spawn.groups = chunk.GetBufferAccessor(ref groupType);
            spawn.assets = chunk.GetBufferAccessor(ref assetType);
            spawn.nodes = chunk.GetBufferAccessor(ref nodeType);
            spawn.entityManager = entityManager;
            spawn.nodesToClear = nodesToClear;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                spawn.Execute(i);
        }
    }

    [BurstCompile]
    private struct Clear : IJob
    {
        public NativeQueue<Entity> entities;
        public BufferLookup<GameRandomSpawnerNode> nodes;

        public void Execute()
        {
            while (entities.TryDequeue(out Entity entity))
                nodes[entity].Clear();
        }
    }

    private EntityQuery __group;
    private GameSyncTime __time;
    private EntityCommandPool<GameSpawnData> __entityManager;
    private NativeQueue<Entity> __nodesToClear;

    public void Create<T>(T instance) where T : GameSpawnCommander
    {
        ///初始化完成之后才能进入帧
        __entityManager = World.GetExistingSystemManaged<EndTimeSystemGroupEntityCommandSystem>().Create<GameSpawnData, T>(EntityCommandManager.QUEUE_PRESENT, instance);
    }

    public void Create<T>() where T : GameSpawnCommander, new()
    {
        ///初始化完成之后才能进入帧
        __entityManager = World.GetExistingSystemManaged<EndTimeSystemGroupEntityCommandSystem>().GetOrCreate<GameSpawnData, T>(EntityCommandManager.QUEUE_PRESENT);
    }

    protected override void OnCreate()
    {
        base.OnCreate();

        __group = GetEntityQuery(
            ComponentType.ReadOnly<Translation>(),
            ComponentType.ReadOnly<GameRandomSpawnerSlice>(),
            ComponentType.ReadOnly<GameRandomSpawnerGroup>(),
            ComponentType.ReadOnly<GameRandomSpawnerAsset>(),
            ComponentType.ReadOnly<GameRandomSpawnerNode>());

        __group.SetChangedVersionFilter(typeof(GameRandomSpawnerNode));

        __time = new GameSyncTime(ref this.GetState());

        __nodesToClear = new NativeQueue<Entity>(Allocator.Persistent);
    }

    protected override void OnDestroy()
    {
        __nodesToClear.Dispose();

        base.OnDestroy();
    }

    protected override void OnUpdate()
    {
        if (!__entityManager.isCreated)
            return;

        var entityManager = __entityManager.Create();

        SpawnEx spawn;
        spawn.time = __time.nextTime;
        spawn.entityType = GetEntityTypeHandle();
        spawn.translationType = GetComponentTypeHandle<Translation>(true);
        spawn.sliceType = GetBufferTypeHandle<GameRandomSpawnerSlice>(true);
        spawn.groupType = GetBufferTypeHandle<GameRandomSpawnerGroup>(true);
        spawn.assetType = GetBufferTypeHandle<GameRandomSpawnerAsset>(true);
        spawn.nodeType = GetBufferTypeHandle<GameRandomSpawnerNode>(true);
        spawn.commands = GetComponentLookup<GameEntityActionCommand>();
        spawn.entityManager = entityManager.parallelWriter;
        spawn.nodesToClear = __nodesToClear.AsParallelWriter();

        var jobHandle = Dependency;
        jobHandle = spawn.ScheduleParallel(__group, jobHandle);

        entityManager.AddJobHandleForProducer<SpawnEx>(jobHandle);

        //__endFrameBarrier.RemoveComponent<GameRandomSpawnerNode>(__group, inputDeps);

        Clear clear;
        clear.entities = __nodesToClear;
        clear.nodes = GetBufferLookup<GameRandomSpawnerNode>();

        Dependency = clear.Schedule(jobHandle);
    }
}
