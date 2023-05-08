using System;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using ZG;
using Random = Unity.Mathematics.Random;

public abstract class GameItemSpawnCommander : IEntityCommander<GameItemSpawnData>
{
    public interface IInitializer : IEntityDataInitializer
    {
        public Entity owner { get; }
    }

    private struct Initializer : IInitializer
    {
        private GameItemHandle __itemHandle;

        public Entity owner
        {
            get;
        }

        public World world
        {
            get;
        }

        public Initializer(in GameItemHandle itemHandle, in Entity owner, World world)
        {
            __itemHandle = itemHandle;

            this.owner = owner;
            this.world = world;
        }

        public GameObjectEntityWrapper Invoke(Entity entity)
        {
            var gameObjectEntity = new GameObjectEntityWrapper(entity, world);

            /*GameLevel level;
            level.handle = __soul.levelIndex + 1;
            gameObjectEntity.SetComponentData(level);

            GamePower power;
            power.value = __soul.power;
            gameObjectEntity.SetComponentData(power);

            GameExp exp;
            exp.value = __soul.exp;
            gameObjectEntity.SetComponentData(exp);*/

            GameItemRoot itemRoot;
            itemRoot.handle = __itemHandle;
            gameObjectEntity.SetComponentData(itemRoot);

            /*GameOwner owner;
            owner.entity = this.owner;
            gameObjectEntity.SetComponentData(owner);*/

            /*GameVariant variant;
            variant.value = __soul.variant;
            gameObjectEntity.SetComponentData(variant);*/

            return gameObjectEntity;
        }
    }

    public abstract void Create<T>(int type, in RigidTransform transform, in T initializer) where T : IInitializer;

    public void Execute(
        EntityCommandPool<GameItemSpawnData>.Context context,
        EntityCommandSystem system,
        ref NativeParallelHashMap<ComponentType, JobHandle> dependency,
        in JobHandle inputDeps)
    {
        World world = system.World;
        while (context.TryDequeue(out var command))
        {
            dependency.CompleteAll(inputDeps);

            Create(
                command.identityType,
                command.transform,
                new Initializer(command.itemHandle, command.owner, world));
        }
    }

    void IDisposable.Dispose()
    {

    }
}

public enum GameItemSpawnType
{
    Drop, 
    Set
}

public struct GameItemSpawnOffset : IComponentData
{
    public float3 min;
    public float3 max;

    public float3 GetValue(ref Random random)
    {
        return random.NextFloat3(min, max);
    }
}

public struct GameItemSpawnCommandVersion : IComponentData
{
    public int value;
}

public struct GameItemSpawnCommand : IBufferElementData
{
    public GameItemSpawnType spawnType;
    public int itemType;
    public int itemCount;
    public int version;
    public Entity owner;
}

public struct GameItemSpawnHandleCommand : IBufferElementData
{
    public GameItemSpawnType spawnType;
    public int version;
    public GameItemHandle handle;
    public Entity owner;
}

public struct GameItemSpawnData
{
    public int identityType;
    public Entity owner;
    public RigidTransform transform;
    public GameItemHandle itemHandle;
}

[BurstCompile]
public partial struct GameItemSpawnSystem : ISystem
{
    public enum EntitySpwanType
    {
        Normal, 
        ItemRoot
    }

    public struct Key : IEquatable<Key>
    {
        public GameItemSpawnType spawnType;
        public int itemType;

        public bool Equals(Key other)
        {
            return spawnType == other.spawnType && itemType == other.itemType;
        }

        public override int GetHashCode()
        {
            return (int)spawnType ^ itemType;
        }
    }

    public struct Value
    {
        public EntitySpwanType spawnType;
        public int identityType;
    }

    public struct SpawnHandles
    {
        public Random random;

        public GameItemManager itemManager;

        [ReadOnly]
        public NativeHashMap<Key, Value> values;

        //[ReadOnly]
        //public NativeArray<Entity> entityArray;

        [ReadOnly]
        public NativeArray<Translation> translations;

        [ReadOnly]
        public NativeArray<Rotation> rotations;

        [ReadOnly]
        public NativeArray<GameItemSpawnOffset> offsets;

        public BufferAccessor<GameItemSpawnHandleCommand> commands;

        public EntityCommandQueue<GameItemSpawnData>.Writer results;

        public void Execute(int index)
        {
            var commands = this.commands[index];
            int numCommands = commands.Length;
            if (numCommands < 1)
                return;

            GameItemSpawnData result;
            //result.entity = entityArray[index];
            result.transform = math.RigidTransform(rotations[index].Value, translations[index].Value + offsets[index].GetValue(ref random));

            int i, j;
            GameItemInfo item;
            Key key;
            Value value;
            for (i = 0; i < numCommands; ++i)
            {
                ref readonly var command = ref commands.ElementAt(i);
                if (!itemManager.TryGetValue(command.handle, out item))
                    continue;

                key.spawnType = command.spawnType;
                key.itemType = item.type;
                if (!values.TryGetValue(key, out value))
                {
                    itemManager.Remove(command.handle, 0);

                    continue;
                }

                result.identityType = value.identityType;
                result.owner = command.owner;

                switch (value.spawnType)
                {
                    case EntitySpwanType.ItemRoot:
                        result.itemHandle = command.handle;

                        results.Enqueue(result);
                        break;
                    default:
                        itemManager.Remove(command.handle, 0);

                        result.itemHandle = GameItemHandle.empty;
                        for (j = 0; j < item.count; ++j)
                            results.Enqueue(result);
                        break;
                }
            }

            commands.Clear();
        }
    }

    public struct Spawn
    {
        public Random random;

        public GameItemManager itemManager;

        [ReadOnly]
        public NativeHashMap<Key, Value> values;

        [ReadOnly]
        public NativeArray<Translation> translations;

        [ReadOnly]
        public NativeArray<Rotation> rotations;

        [ReadOnly]
        public NativeArray<GameItemSpawnOffset> offsets;

        public BufferAccessor<GameItemSpawnCommand> commands;

        public EntityCommandQueue<GameItemSpawnData>.Writer results;

        public void Execute(int index)
        {
            var commands = this.commands[index];
            int numCommands = commands.Length;
            if (numCommands < 1)
                return;

            GameItemSpawnData result;
            result.transform = math.RigidTransform(rotations[index].Value, translations[index].Value + offsets[index].GetValue(ref random));

            int i, j, count;
            Key key;
            Value value;
            for (i = 0; i < numCommands; ++i)
            {
                ref readonly var command = ref commands.ElementAt(i);

                key.spawnType = command.spawnType;
                key.itemType = command.itemType;
                if (!values.TryGetValue(key, out value))
                    continue;

                result.identityType = value.identityType;
                result.owner = command.owner;

                switch (value.spawnType)
                {
                    case EntitySpwanType.ItemRoot:
                        count = command.itemCount;
                        result.itemHandle = itemManager.Add(command.itemType, ref count);

                        results.Enqueue(result);
                        break;
                    default:
                        result.itemHandle = GameItemHandle.empty;
                        for (j = 0; j < command.itemCount; ++j)
                            results.Enqueue(result);
                        break;
                }
            }

            commands.Clear();
        }
    }

    [BurstCompile]
    public struct SpawnEx : IJobChunk, IEntityCommandProducerJob
    {
        public uint hash;

        public GameItemManager itemManager;

        [ReadOnly]
        public NativeHashMap<Key, Value> values;

        //[ReadOnly]
        //public EntityTypeHandle entityType;

        [ReadOnly]
        public ComponentTypeHandle<Translation> translationType;

        [ReadOnly]
        public ComponentTypeHandle<Rotation> rotationType;

        [ReadOnly]
        public ComponentTypeHandle<GameItemSpawnOffset> offsetType;

        public BufferTypeHandle<GameItemSpawnCommand> commandType;

        public BufferTypeHandle<GameItemSpawnHandleCommand> handleCommandType;

        public EntityCommandQueue<GameItemSpawnData>.Writer results;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            var random = new Random(hash ^ (uint)unfilteredChunkIndex);
            var translations = chunk.GetNativeArray(ref translationType);
            var rotations = chunk.GetNativeArray(ref rotationType);
            var offsets = chunk.GetNativeArray(ref offsetType);
            if (chunk.Has(ref commandType))
            {
                Spawn spawn;
                spawn.random = random;
                spawn.itemManager = itemManager;
                spawn.values = values;
                spawn.translations = translations;
                spawn.rotations = rotations;
                spawn.offsets = offsets;
                spawn.commands = chunk.GetBufferAccessor(ref commandType);
                spawn.results = results;

                var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (iterator.NextEntityIndex(out int i))
                    spawn.Execute(i);
            }

            if (chunk.Has(ref handleCommandType))
            {
                SpawnHandles spawn;
                spawn.random = random;
                spawn.itemManager = itemManager;
                spawn.values = values;
                spawn.translations = translations;
                spawn.rotations = rotations;
                spawn.offsets = offsets;
                spawn.commands = chunk.GetBufferAccessor(ref handleCommandType);
                spawn.results = results;

                var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (iterator.NextEntityIndex(out int i))
                    spawn.Execute(i);
            }
        }
    }

    private EntityQuery __group;
    private NativeHashMap<Key, Value> __values;
    private EntityCommandPool<GameItemSpawnData> __entityManager;
    private GameItemManagerShared __itemManager;

    public void Create(System.Collections.Generic.KeyValuePair<Key, Value>[] values, GameItemSpawnCommander commander, World world)
    {
        __values.Capacity = values.Length;
        foreach (var value in values)
            __values.Add(value.Key, value.Value);

        __entityManager = world.GetOrCreateSystemManaged<EndFrameEntityCommandSystem>().Create<GameItemSpawnData, GameItemSpawnCommander>(EntityCommandManager.QUEUE_PRESENT, commander);
    }

    public void OnCreate(ref SystemState state)
    {
        __group = state.GetEntityQuery(
            new EntityQueryDesc()
            {
                All = new ComponentType[]
                {
                    ComponentType.ReadOnly<Translation>(),
                    ComponentType.ReadOnly<Rotation>(),
                    ComponentType.ReadOnly<GameItemSpawnCommandVersion>(),
                    ComponentType.ReadWrite<GameItemSpawnCommand>()
                }, 
                Options = EntityQueryOptions.IncludeDisabledEntities
            });

        __group.SetChangedVersionFilter(typeof(GameItemSpawnCommandVersion));

        __values = new NativeHashMap<Key, Value>(1, Allocator.Persistent);

        __itemManager = state.World.GetOrCreateSystemUnmanaged<GameItemSystem>().manager;
    }

    public void OnDestroy(ref SystemState state)
    {
        __values.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (__values.IsEmpty)
            return;

        double time = state.WorldUnmanaged.Time.ElapsedTime;
        long hash = math.aslong(time);

        var entityManager = __entityManager.Create();

        SpawnEx spawn;
        spawn.hash = (uint)hash ^ (uint)(hash >> 32);
        spawn.itemManager = __itemManager.value;
        spawn.values = __values;
        spawn.translationType = state.GetComponentTypeHandle<Translation>(true);
        spawn.rotationType = state.GetComponentTypeHandle<Rotation>(true);
        spawn.offsetType = state.GetComponentTypeHandle<GameItemSpawnOffset>(true);
        spawn.commandType = state.GetBufferTypeHandle<GameItemSpawnCommand>();
        spawn.handleCommandType = state.GetBufferTypeHandle<GameItemSpawnHandleCommand>();
        spawn.results = entityManager.writer;

        ref var itemJobManager = ref __itemManager.lookupJobManager;

        var jobHandle = spawn.Schedule(__group, JobHandle.CombineDependencies(itemJobManager.readWriteJobHandle, state.Dependency));

        itemJobManager.readWriteJobHandle = jobHandle;

        entityManager.AddJobHandleForProducer<SpawnEx>(jobHandle);

        state.Dependency = jobHandle;
    }
}
