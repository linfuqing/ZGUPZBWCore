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

/*public abstract class GameItemSpawnCommander : IEntityCommander<GameItemSpawnData>
{
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
                new Initializer(command.itemHandle, command.owner));
        }
    }

    void IDisposable.Dispose()
    {

    }
}*/

public enum GameItemSpawnType
{
    Drop, 
    Set
}

public struct GameItemSpawnInitializer : IEntityDataInitializer
{
    private GameItemHandle __itemHandle;

    public Entity owner
    {
        get;
    }

    public GameItemSpawnInitializer(in GameItemHandle itemHandle, in Entity owner)
    {
        __itemHandle = itemHandle;

        this.owner = owner;
    }

    public void Invoke<T>(ref T gameObjectEntity) where T : IGameObjectEntity
    {
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

        var entity = this.owner;

        GameOwner owner;
        owner.entity = entity;
        gameObjectEntity.SetComponentData(owner);

        GameActorMaster master;
        master.entity = entity;
        gameObjectEntity.SetComponentData(master);

        /*GameVariant variant;
        variant.value = __soul.variant;
        gameObjectEntity.SetComponentData(variant);*/
    }
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

/*public struct GameItemSpawnCommandVersion : IComponentData
{
    public int value;
}*/

public struct GameItemSpawnCommand : IBufferElementData, IEnableableComponent
{
    public GameItemSpawnType spawnType;
    public int itemType;
    public int itemCount;
    //public int version;
    public Entity owner;
    public RigidTransform transform;
}

public struct GameItemSpawnHandleCommand : IBufferElementData, IEnableableComponent
{
    public GameItemSpawnType spawnType;
    public GameItemHandle handle;
    public Entity owner;
    public RigidTransform transform;
}

public struct GameItemSpawnData
{
    public int identityType;
    public Entity owner;
    public RigidTransform transform;
    public GameItemHandle itemHandle;
}

[BurstCompile, CreateAfter(typeof(GameItemSystem))]
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

        //[ReadOnly]
        //public NativeArray<Translation> translations;

        //[ReadOnly]
        //public NativeArray<Rotation> rotations;

        [ReadOnly]
        public NativeArray<GameItemSpawnOffset> offsets;

        public BufferAccessor<GameItemSpawnHandleCommand> commands;

        public SharedList<GameItemSpawnData>.Writer results;

        public void Execute(int index)
        {
            var commands = this.commands[index];
            int numCommands = commands.Length;
            if (numCommands < 1)
                return;

            GameItemSpawnData result;
            //result.entity = entityArray[index];
            //result.transform = math.RigidTransform(rotations[index].Value, translations[index].Value + offsets[index].GetValue(ref random));

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
                result.transform = command.transform;

                switch (value.spawnType)
                {
                    case EntitySpwanType.ItemRoot:
                        itemManager.DetachParent(command.handle);

                        result.itemHandle = command.handle;

                        results.Add(result);
                        break;
                    default:
                        itemManager.Remove(command.handle, 0);

                        result.itemHandle = GameItemHandle.Empty;
                        for (j = 0; j < item.count; ++j)
                            results.Add(result);
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

        //[ReadOnly]
        //public NativeArray<Translation> translations;

        //[ReadOnly]
        //public NativeArray<Rotation> rotations;

        [ReadOnly]
        public NativeArray<GameItemSpawnOffset> offsets;

        public BufferAccessor<GameItemSpawnCommand> commands;

        public SharedList<GameItemSpawnData>.Writer results;

        public void Execute(int index)
        {
            var commands = this.commands[index];
            int numCommands = commands.Length;
            if (numCommands < 1)
                return;

            GameItemSpawnData result;
            //result.transform = math.RigidTransform(rotations[index].Value, translations[index].Value + offsets[index].GetValue(ref random));

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
                result.transform = command.transform;

                switch (value.spawnType)
                {
                    case EntitySpwanType.ItemRoot:
                        count = command.itemCount;
                        result.itemHandle = itemManager.Add(command.itemType, ref count);

                        results.Add(result);
                        break;
                    default:
                        result.itemHandle = GameItemHandle.Empty;
                        for (j = 0; j < command.itemCount; ++j)
                            results.Add(result);
                        break;
                }
            }

            commands.Clear();
        }
    }

    [BurstCompile]
    public struct SpawnEx : IJobChunk//, IEntityCommandProducerJob
    {
        public uint hash;

        public GameItemManager itemManager;

        [ReadOnly]
        public NativeHashMap<Key, Value> values;

        //[ReadOnly]
        //public EntityTypeHandle entityType;

        //[ReadOnly]
        //public ComponentTypeHandle<Translation> translationType;

        //[ReadOnly]
        //public ComponentTypeHandle<Rotation> rotationType;

        [ReadOnly]
        public ComponentTypeHandle<GameItemSpawnOffset> offsetType;

        public BufferTypeHandle<GameItemSpawnCommand> commandType;

        public BufferTypeHandle<GameItemSpawnHandleCommand> handleCommandType;

        public SharedList<GameItemSpawnData>.Writer results;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            var random = new Random(hash ^ (uint)unfilteredChunkIndex);
            //var translations = chunk.GetNativeArray(ref translationType);
            //var rotations = chunk.GetNativeArray(ref rotationType);
            var offsets = chunk.GetNativeArray(ref offsetType);
            if (chunk.Has(ref commandType))
            {
                Spawn spawn;
                spawn.random = random;
                spawn.itemManager = itemManager;
                spawn.values = values;
                //spawn.translations = translations;
                //spawn.rotations = rotations;
                spawn.offsets = offsets;
                spawn.commands = chunk.GetBufferAccessor(ref commandType);
                spawn.results = results;

                var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (iterator.NextEntityIndex(out int i))
                {
                    if (!chunk.IsComponentEnabled(ref commandType, i))
                        continue;

                    spawn.Execute(i);

                    chunk.SetComponentEnabled(ref commandType, i, false);
                }
            }

            if (chunk.Has(ref handleCommandType))
            {
                SpawnHandles spawn;
                spawn.random = random;
                spawn.itemManager = itemManager;
                spawn.values = values;
                //spawn.translations = translations;
                //spawn.rotations = rotations;
                spawn.offsets = offsets;
                spawn.commands = chunk.GetBufferAccessor(ref handleCommandType);
                spawn.results = results;

                var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (iterator.NextEntityIndex(out int i))
                {
                    if (!chunk.IsComponentEnabled(ref handleCommandType, i))
                        continue;

                    spawn.Execute(i);

                    chunk.SetComponentEnabled(ref handleCommandType, i, false);
                }
            }
        }
    }

    private EntityQuery __group;

    //private ComponentTypeHandle<Translation> __translationType;

    //private ComponentTypeHandle<Rotation> __rotationType;

    private ComponentTypeHandle<GameItemSpawnOffset> __offsetType;

    private BufferTypeHandle<GameItemSpawnCommand> __commandType;

    private BufferTypeHandle<GameItemSpawnHandleCommand> __handleCommandType;

    private NativeHashMap<Key, Value> __values;
    private GameItemManagerShared __itemManager;

    public SharedList<GameItemSpawnData> commands
    {
        get;

        private set;
    }

    public void Create(System.Collections.Generic.KeyValuePair<Key, Value>[] values)
    {
        foreach (var value in values)
            __values.Add(value.Key, value.Value);

        //__entityManager = world.GetOrCreateSystemManaged<EndFrameEntityCommandSystem>().Create<GameItemSpawnData, GameItemSpawnCommander>(EntityCommandManager.QUEUE_PRESENT, commander);
    }

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __group = builder
                    .WithAll<Translation, Rotation>()
                    .WithAnyRW<GameItemSpawnCommand, GameItemSpawnHandleCommand>()
                    .WithOptions(EntityQueryOptions.IncludeDisabledEntities)
                    .Build(ref state);

        //__group.AddChangedVersionFilter(ComponentType.ReadWrite<GameItemSpawnCommand>());
        //__group.AddChangedVersionFilter(ComponentType.ReadWrite<GameItemSpawnHandleCommand>());

        //__translationType = state.GetComponentTypeHandle<Translation>(true);
        //__rotationType = state.GetComponentTypeHandle<Rotation>(true);
        __offsetType = state.GetComponentTypeHandle<GameItemSpawnOffset>(true);
        __commandType = state.GetBufferTypeHandle<GameItemSpawnCommand>();
        __handleCommandType = state.GetBufferTypeHandle<GameItemSpawnHandleCommand>();

        __itemManager = state.WorldUnmanaged.GetExistingSystemUnmanaged<GameItemSystem>().manager;

        __values = new NativeHashMap<Key, Value>(1, Allocator.Persistent);

        commands = new SharedList<GameItemSpawnData>(Allocator.Persistent);
    }

    //[BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        __values.Dispose();

        commands.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (__values.IsEmpty)
            return;

        var commands = this.commands;

        SpawnEx spawn;
        spawn.hash = RandomUtility.Hash(state.WorldUnmanaged.Time.ElapsedTime);
        spawn.itemManager = __itemManager.value;
        spawn.values = __values;
        //spawn.translationType = __translationType.UpdateAsRef(ref state);
        //spawn.rotationType = __rotationType.UpdateAsRef(ref state);
        spawn.offsetType = __offsetType.UpdateAsRef(ref state);
        spawn.commandType = __commandType.UpdateAsRef(ref state);
        spawn.handleCommandType = __handleCommandType.UpdateAsRef(ref state);
        spawn.results = commands.writer;

        ref var commandsJobManager = ref commands.lookupJobManager;

        ref var itemJobManager = ref __itemManager.lookupJobManager;

        var jobHandle = spawn.ScheduleByRef(__group, JobHandle.CombineDependencies(commandsJobManager.readWriteJobHandle, itemJobManager.readWriteJobHandle, state.Dependency));

        itemJobManager.readWriteJobHandle = jobHandle;

        commandsJobManager.readWriteJobHandle = jobHandle;

        state.Dependency = jobHandle;
    }
}
