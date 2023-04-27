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
    public EntityCommandPool<GameSpawnData> pool;
}

[BurstCompile, UpdateInGroup(typeof(TimeSystemGroup)), UpdateAfter(typeof(GameSyncSystemGroup))]
public partial struct GameRandomSpawnerSystem : ISystem
{
    private struct Spawn
    {
        private struct AssetHandler : IRandomItemHandler
        {
            public float vertical;

            public float horizontal;

            //public double time;

            public RigidTransform transform;

            public Entity entity;

            public Random random;

            [ReadOnly]
            public DynamicBuffer<GameRandomSpawnerAsset> assets;

            public EntityCommandQueue<GameSpawnData>.ParallelWriter entityManager;

            public RandomResult Set(int startIndex, int count)
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
                    spawnData.transform.rot = transform.rot;// quaternion.LookRotationSafe(-math.normalize(spawnData.transform.pos), math.up());
                    spawnData.transform.pos += transform.pos;

                    entityManager.Enqueue(spawnData);
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
        public BufferAccessor<GameRandomSpawnerSlice> slices;

        [ReadOnly]
        public BufferAccessor<GameRandomSpawnerGroup> groups;

        [ReadOnly]
        public BufferAccessor<GameRandomSpawnerAsset> assets;
        
        public BufferAccessor<GameRandomSpawnerNode> nodes;
        
        public EntityCommandQueue<GameSpawnData>.ParallelWriter entityManager;

        public void Execute(int index)
        {
            var nodes = this.nodes[index];
            int length = nodes.Length;
            if (length < 1)
                return;

            Entity entity = entityArray[index];

            AssetHandler assetHandler;
            //assetHandler.time = time;
            assetHandler.transform = math.RigidTransform(rotations[index].Value, translations[index].Value);
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

            nodes.Clear();
        }
    }

    [BurstCompile]
    private struct SpawnEx : IJobChunk, IEntityCommandProducerJob
    {
        public double time;

        [ReadOnly]
        public EntityTypeHandle entityType;

        [ReadOnly]
        public ComponentTypeHandle<Rotation> rotationType;

        [ReadOnly]
        public ComponentTypeHandle<Translation> translationType;
        
        [ReadOnly]
        public BufferTypeHandle<GameRandomSpawnerSlice> sliceType;

        [ReadOnly]
        public BufferTypeHandle<GameRandomSpawnerGroup> groupType;

        [ReadOnly]
        public BufferTypeHandle<GameRandomSpawnerAsset> assetType;
        
        public BufferTypeHandle<GameRandomSpawnerNode> nodeType;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<GameEntityActionCommand> commands;

        public EntityCommandQueue<GameSpawnData>.ParallelWriter entityManager;

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
            spawn.entityManager = entityManager;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
            {
                spawn.Execute(i);

                chunk.SetComponentEnabled(ref nodeType, i, false);
            }
        }
    }

    private EntityQuery __group;
    private EntityQuery __factoryGroup;
    private GameSyncTime __time;

    private EntityTypeHandle __entityType;

    private ComponentTypeHandle<Rotation> __rotationType;

    private ComponentTypeHandle<Translation> __translationType;

    private BufferTypeHandle<GameRandomSpawnerSlice> __sliceType;

    private BufferTypeHandle<GameRandomSpawnerGroup> __groupType;

    private BufferTypeHandle<GameRandomSpawnerAsset> __assetType;

    private BufferTypeHandle<GameRandomSpawnerNode> __nodeType;

    private ComponentLookup<GameEntityActionCommand> __commands;

    private EntityCommandPool<GameSpawnData> __entityManager;

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

        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __factoryGroup = builder
                .WithAll<GameRandomSpawnerFactory>()
                .Build(ref state);

        __time = new GameSyncTime(ref state);

        __entityType = state.GetEntityTypeHandle();
        __rotationType = state.GetComponentTypeHandle<Rotation>(true);
        __translationType = state.GetComponentTypeHandle<Translation>(true);
        __sliceType = state.GetBufferTypeHandle<GameRandomSpawnerSlice>(true);
        __groupType = state.GetBufferTypeHandle<GameRandomSpawnerGroup>(true);
        __assetType = state.GetBufferTypeHandle<GameRandomSpawnerAsset>(true);
        __nodeType = state.GetBufferTypeHandle<GameRandomSpawnerNode>();
        __commands = state.GetComponentLookup<GameEntityActionCommand>();
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (__factoryGroup.IsEmpty)
            return;

        var entityManager = __factoryGroup.GetSingleton<GameRandomSpawnerFactory>().pool.Create();

        SpawnEx spawn;
        spawn.time = __time.nextTime;
        spawn.entityType = __entityType.UpdateAsRef(ref state);
        spawn.rotationType = __rotationType.UpdateAsRef(ref state);
        spawn.translationType = __translationType.UpdateAsRef(ref state);
        spawn.sliceType = __sliceType.UpdateAsRef(ref state);
        spawn.groupType = __groupType.UpdateAsRef(ref state);
        spawn.assetType = __assetType.UpdateAsRef(ref state);
        spawn.nodeType = __nodeType.UpdateAsRef(ref state);
        spawn.commands = __commands.UpdateAsRef(ref state);
        spawn.entityManager = entityManager.parallelWriter;

        var jobHandle = spawn.ScheduleParallelByRef(__group, state.Dependency);

        entityManager.AddJobHandleForProducer<SpawnEx>(jobHandle);

        state.Dependency = jobHandle;
    }
}
