using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Transforms;
using ZG;
using Random = Unity.Mathematics.Random;

[assembly: RegisterGenericJobType(typeof(TimeManager<GameValhallaSystem.Command>.UpdateEvents))]

/*public abstract class GameValhallaCommander : IEntityCommander<GameValhallaCommand>
{
    public abstract void Create<T>(int type, int variant, in RigidTransform transform, in Entity ownerEntity, in T initializer) where T : IEntityDataInitializer;

    public void Execute(
        EntityCommandPool<GameValhallaCommand>.Context context, 
        EntityCommandSystem system,
        ref NativeParallelHashMap<ComponentType, JobHandle> dependency,
        in JobHandle inputDeps)
    {
        while (context.TryDequeue(out var command))
        {
            dependency.CompleteAll(inputDeps);

            Create(
                command.type, 
                command.variant, 
                command.transform,
                command.entity, 
                new Initializer(command.itemHandle, command.nickname, command.entity));
        }
    }

    void IDisposable.Dispose()
    {

    }
}*/

public struct GameValhallaDefinition
{
    public struct Exp
    {
        public float min;
        public float max;
    }

    public struct LevelExp
    {
        public float value;
        public float factor;
    }

    public struct Level
    {
        public float maxExp;

        public LevelExp expToDestroy;
        public LevelExp expToRespawn;
    }

    public BlobArray<int> soulToItemTypes;

    public BlobArray<Exp> itemTypeToExps;

    public BlobArray<Level> levels;
}

public struct GameValhallaData : IComponentData
{
    public BlobAssetReference<GameValhallaDefinition> definition;
}

public struct GameValhallaInitializer : IEntityDataInitializer
{
    //private GameSoulData __soul;
    private GameItemHandle __itemHandle;
    private FixedString32Bytes __nickname;
    private Entity __owner;

    public GameValhallaInitializer(in GameItemHandle itemHandle, in FixedString32Bytes nickname, Entity owner)
    {
        __itemHandle = itemHandle;
        __nickname = nickname;
        __owner = owner;
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

        GameNickname nickname;
        nickname.value = __nickname;// __soul.nickname;
        gameObjectEntity.SetComponentData(nickname);

        /*GameVariant variant;
        variant.value = __soul.variant;
        gameObjectEntity.SetComponentData(variant);*/

        GameOwner owner;
        owner.entity = __owner;
        gameObjectEntity.SetComponentData(owner);

        GameActorMaster master;
        master.entity = __owner;
        gameObjectEntity.SetComponentData(master);
    }
}

[BurstCompile, 
    CreateAfter(typeof(GameItemSystem)),
    CreateAfter(typeof(GameSoulSystem)),
    UpdateInGroup(typeof(InitializationSystemGroup))]
public partial struct GameValhallaSystem : ISystem
{
    private struct UpgradeResult
    {
        public int soulIndex;
        public float exp;
        public Entity entity;
    }

    private struct RenameResult
    {
        public int soulIndex;
        public Entity entity;
        public FixedString32Bytes name;
    }

    private struct DestroyResult
    {
        public int soulIndex;
        public Entity entity;
    }

    public struct EvoluteResult
    {
        public int soulIndex;
        public int nextIndex;
        public Entity valhalla;
        public Entity master;
    }

    public struct Command
    {
        public Entity entity;
        public RigidTransform transform;
        public GameSoulData value;
    }

    private struct Collect
    {
        public Random random;

        public BlobAssetReference<GameValhallaDefinition> definition;

        [ReadOnly]
        public GameItemManager.Hierarchy hierarchy;

        [ReadOnly]
        public NativeArray<Entity> entityArray;

        [ReadOnly]
        public NativeArray<GameValhallaExp> exps;

        [ReadOnly]
        public NativeArray<GameItemRoot> itemRoots;

        public NativeQueue<GameItemHandle>.ParallelWriter handles;

        [NativeDisableContainerSafetyRestriction]
        public ComponentLookup<GameValhallaExp> expMap;

        public void Execute(int index)
        {
            var root = itemRoots[index].handle;

            bool result = false;
            var exp = exps[index];
            if (hierarchy.GetChildren(root, out var enumerator, out var _))
            {
                ref var itemTypeToExps = ref definition.Value.itemTypeToExps;
                GameItemHandle handle;
                GameItemInfo item;
                float value;
                while (enumerator.MoveNext())
                {
                    handle = enumerator.Current.handle;
                    if (hierarchy.TryGetValue(handle, out item))
                    {
                        ref var itemTypeToExp = ref itemTypeToExps[item.type];

                        value = random.NextFloat(itemTypeToExp.min * item.count, itemTypeToExp.max * item.count);
                        if (value > math.FLT_MIN_NORMAL)
                        {
                            exp.value += value;

                            handles.Enqueue(handle);

                            result = true;
                        }
                    }
                }
            }

            if(result)
                expMap[entityArray[index]] = exp;
        }
    }

    [BurstCompile]
    private struct CollectEx : IJobChunk
    {
        public uint hash;

        public BlobAssetReference<GameValhallaDefinition> definition;

        [ReadOnly]
        public GameItemManager.Hierarchy hierarchy;

        [ReadOnly]
        public EntityTypeHandle entityType;

        [ReadOnly]
        public ComponentTypeHandle<GameValhallaExp> expType;

        [ReadOnly]
        public ComponentTypeHandle<GameItemRoot> itemRootType;

        public ComponentTypeHandle<GameValhallaCollectCommand> commandType;

        [NativeDisableContainerSafetyRestriction]
        public ComponentLookup<GameValhallaExp> exps;

        public NativeQueue<GameItemHandle>.ParallelWriter handles;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Collect collect;
            collect.random = new Random(hash ^ (uint)unfilteredChunkIndex);
            collect.definition = definition;
            collect.hierarchy = hierarchy;
            collect.entityArray = chunk.GetNativeArray(entityType);
            collect.exps = chunk.GetNativeArray(ref expType);
            collect.itemRoots = chunk.GetNativeArray(ref itemRootType);
            collect.expMap = exps;
            collect.handles = handles;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
            {
                collect.Execute(i);

                chunk.SetComponentEnabled(ref commandType, i, false);
            }
        }
    }

    [BurstCompile]
    private struct DeleteHandles : IJob
    {
        public GameItemManager manager;

        public NativeQueue<GameItemHandle> handles;

        public void Execute()
        {
            while (handles.TryDequeue(out var handle))
                manager.Remove(handle, 0);
        }
    }

    private struct Upgrade
    {
        public BlobAssetReference<GameValhallaDefinition> definition;

        [ReadOnly]
        public BufferLookup<GameSoul> souls;
        [ReadOnly]
        public NativeArray<GameValhallaUpgradeCommand> commands;
        public NativeArray<GameValhallaExp> exps;

        public NativeQueue<UpgradeResult>.ParallelWriter results;

        public void Execute(int index)
        {
            var command = commands[index];
            if (command.soulIndex < 0)
                return;

            if (!this.souls.HasBuffer(command.entity))
                return;

            var souls = this.souls[command.entity];
            int soulIndex = GameSoul.IndexOf(command.soulIndex, souls);
            if (soulIndex == -1)
                return;

            var soul = souls[soulIndex];
            float value = math.min(soul.data.exp + command.exp, definition.Value.levels[soul.data.levelIndex].maxExp) - soul.data.exp;
            var exp = exps[index];
            /*if (exp.value < value)
                return;*/

            exp.value -= math.min(exp.value, value);
            exps[index] = exp;

            UpgradeResult result;
            result.soulIndex = soulIndex;
            result.exp = value;
            result.entity = command.entity;
            results.Enqueue(result);
        }
    }

    [BurstCompile]
    private struct UpgradeEx : IJobChunk
    {
        public BlobAssetReference<GameValhallaDefinition> definition;

        [ReadOnly]
        public BufferLookup<GameSoul> souls;

        public ComponentTypeHandle<GameValhallaUpgradeCommand> commandType;
        public ComponentTypeHandle<GameValhallaExp> expType;

        public NativeQueue<UpgradeResult>.ParallelWriter results;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Upgrade upgrade;
            upgrade.definition = definition;
            upgrade.souls = souls;
            upgrade.commands = chunk.GetNativeArray(ref commandType);
            upgrade.exps = chunk.GetNativeArray(ref expType);
            upgrade.results = results;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
            {
                upgrade.Execute(i);

                chunk.SetComponentEnabled(ref commandType, i, false);
            }
        }
    }

    [BurstCompile]
    private struct ApplyUpgrade : IJob
    {
        public NativeQueue<UpgradeResult> results;

        public BufferLookup<GameSoul> souls;

        public void Execute()
        {
            GameSoul soul;
            DynamicBuffer<GameSoul> souls;
            while (results.TryDequeue(out var result))
            {
                souls = this.souls[result.entity];
                soul = souls[result.soulIndex];
                soul.data.exp += result.exp;
                souls[result.soulIndex] = soul;
            }
        }
    }

    private struct Rename
    {
        [ReadOnly]
        public BufferLookup<GameSoul> souls;
        [ReadOnly]
        public NativeArray<GameValhallaRenameCommand> commands;

        public NativeQueue<RenameResult>.ParallelWriter results;

        public void Execute(int index)
        {
            var command = commands[index];
            if (command.soulIndex < 0)
                return;

            if (!this.souls.HasBuffer(command.entity))
                return;

            var souls = this.souls[command.entity];
            int soulIndex = GameSoul.IndexOf(command.soulIndex, souls);
            if (soulIndex == -1)
                return;

            RenameResult result;
            result.soulIndex = command.soulIndex;
            result.entity = command.entity;
            result.name = command.name;

            results.Enqueue(result);
        }
    }

    [BurstCompile]
    private struct RenameEx : IJobChunk
    {
        [ReadOnly]
        public BufferLookup<GameSoul> souls;

        public ComponentTypeHandle<GameValhallaRenameCommand> commandType;

        public NativeQueue<RenameResult>.ParallelWriter results;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Rename rename;
            rename.souls = souls;
            rename.commands = chunk.GetNativeArray(ref commandType);
            rename.results = results;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
            {
                rename.Execute(i);

                chunk.SetComponentEnabled(ref commandType, i, false);
            }
        }
    }

    [BurstCompile]
    private struct ApplyRename : IJob
    {
        public NativeQueue<RenameResult> results;

        public BufferLookup<GameSoul> souls;

        public void Execute()
        {
            int soulIndex;
            GameSoul soul;
            DynamicBuffer<GameSoul> souls;
            while (results.TryDequeue(out var result))
            {
                souls = this.souls[result.entity];

                soulIndex = GameSoul.IndexOf(result.soulIndex, souls);

                soul = souls[soulIndex];
                soul.data.nickname = result.name;
                souls[soulIndex] = soul;
            }
        }
    }

    private struct Destroy
    {
        public BlobAssetReference<GameValhallaDefinition> definition;

        [ReadOnly]
        public BufferLookup<GameSoul> souls;
        [ReadOnly]
        public NativeArray<GameValhallaDestroyCommand> commands;
        public NativeArray<GameValhallaExp> exps;

        public NativeQueue<DestroyResult>.ParallelWriter results;

        public void Execute(int index)
        {
            var command = commands[index];
            if (command.soulIndex < 0)
                return;

            if (!this.souls.HasBuffer(command.entity))
                return;

            var souls = this.souls[command.entity];
            int soulIndex = GameSoul.IndexOf(command.soulIndex, souls);
            if (soulIndex == -1)
                return;

            var soul = souls[soulIndex];
            ref var destroyLevelExp = ref definition.Value.levels[soul.data.levelIndex].expToDestroy;

            var exp = exps[index];
            exp.value += destroyLevelExp.value + destroyLevelExp.factor * soul.data.exp;
            exps[index] = exp;

            DestroyResult result;
            result.soulIndex = command.soulIndex;
            result.entity = command.entity;
            results.Enqueue(result);
        }
    }

    [BurstCompile]
    private struct DestroyEx : IJobChunk
    {
        public BlobAssetReference<GameValhallaDefinition> definition;

        [ReadOnly]
        public BufferLookup<GameSoul> souls;
        public ComponentTypeHandle<GameValhallaDestroyCommand> commandType;
        public ComponentTypeHandle<GameValhallaExp> expType;

        public NativeQueue<DestroyResult>.ParallelWriter results;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Destroy destroy;
            destroy.souls = souls;
            destroy.definition = definition;
            destroy.commands = chunk.GetNativeArray(ref commandType);
            destroy.exps = chunk.GetNativeArray(ref expType);
            destroy.results = results;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
            {
                destroy.Execute(i);

                chunk.SetComponentEnabled(ref commandType, i, false);
            }
        }
    }

    [BurstCompile]
    private struct ApplyDestroy : IJob
    {
        public NativeQueue<DestroyResult> results;

        public BufferLookup<GameSoul> souls;

        public void Execute()
        {
            DynamicBuffer<GameSoul> souls;
            while (results.TryDequeue(out var result))
            {
                souls = this.souls[result.entity];

                souls.RemoveAt(GameSoul.IndexOf(result.soulIndex, souls));
            }
        }
    }

    private struct Evolute
    {
        public Random random;

        public BlobAssetReference<GameValhallaDefinition> definition;

        [ReadOnly]
        public GameSoulManager manager;

        [ReadOnly]
        public BufferLookup<GameSoul> souls;

        [ReadOnly]
        public BufferAccessor<GameValhallaSacrificer> sacrificers;

        [ReadOnly]
        public NativeArray<Entity> entityArray;

        [ReadOnly]
        public NativeArray<GameValhallaEvoluteCommand> commands;

        public NativeQueue<EvoluteResult>.ParallelWriter results;

        public void Execute(int index)
        {
            var command = commands[index];

            if (!this.souls.HasBuffer(command.entity))
                return;

            var souls = this.souls[command.entity];
            int soulIndex = GameSoul.IndexOf(command.soulIndex, souls);
            if (soulIndex == -1)
                return;

            var soul = souls[soulIndex];
            if ((int)math.round(soul.data.exp) < (int)math.floor(definition.Value.levels[soul.data.levelIndex].maxExp))
                return;

            int nextIndex = manager.FindNextIndex(
                soulIndex,
                sacrificers[index].Reinterpret<int>().AsNativeArray(),
                souls,
                ref random);

            if (nextIndex == -1)
                return;

            EvoluteResult result;
            result.soulIndex = command.soulIndex;
            result.nextIndex = nextIndex;
            result.valhalla = entityArray[index];
            result.master = command.entity;
            results.Enqueue(result);
        }
    }

    [BurstCompile]
    private struct EvoluteEx : IJobChunk
    {
        public uint hash;

        public BlobAssetReference<GameValhallaDefinition> definition;

        [ReadOnly]
        public GameSoulManager manager;

        [ReadOnly]
        public BufferLookup<GameSoul> souls;

        [ReadOnly]
        public BufferTypeHandle<GameValhallaSacrificer> sacrificerType;

        [ReadOnly]
        public EntityTypeHandle entityType;

        public ComponentTypeHandle<GameValhallaEvoluteCommand> commandType;

        public NativeQueue<EvoluteResult>.ParallelWriter results;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Evolute evolute;
            evolute.random = new Random(hash ^ (uint)unfilteredChunkIndex);
            evolute.definition = definition;
            evolute.manager = manager;
            evolute.souls = souls;
            evolute.sacrificers = chunk.GetBufferAccessor(ref sacrificerType);
            evolute.entityArray = chunk.GetNativeArray(entityType);
            evolute.commands = chunk.GetNativeArray(ref commandType);
            evolute.results = results;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
            {
                evolute.Execute(i);

                chunk.SetComponentEnabled(ref commandType, i, false);
            }
        }
    }

    [BurstCompile]
    private struct ApplyEvolute : IJob
    {
        [ReadOnly]
        public GameSoulManager manager;

        [ReadOnly]
        public BufferLookup<GameValhallaSacrificer> sacrificers;

        public BufferLookup<GameSoul> souls;

        public NativeQueue<EvoluteResult> inputs;

        public NativeList<EvoluteResult> outputs;

        public void Execute()
        {
            outputs.Clear();

            DynamicBuffer<GameSoul> souls;
            while (inputs.TryDequeue(out var result))
            {
                souls = this.souls[result.master];
                manager.Apply(
                    result.nextIndex,
                    GameSoul.IndexOf(result.soulIndex, souls),
                    sacrificers[result.valhalla].Reinterpret<int>().AsNativeArray(),
                    ref souls);

                outputs.Add(result);
            }
        }
    }

    [BurstCompile]
    private struct ClearSoulIndicess : IJob
    {
        [ReadOnly, DeallocateOnJobCompletion]
        public NativeArray<int> capacity;
        public NativeParallelHashMap<Entity, int> soulIndicess;

        public void Execute()
        {
            int capacity = this.capacity[0];
            if (soulIndicess.Capacity < capacity)
                soulIndicess.Capacity = capacity;

            soulIndicess.Clear();
        }
    }

    private struct Respawn
    {
        public double time;

        public BlobAssetReference<GameValhallaDefinition> definition;

        [ReadOnly]
        public BufferLookup<GameSoul> souls;
        [ReadOnly]
        public NativeArray<GameValhallaRespawnCommand> commands;

        public NativeArray<GameValhallaExp> exps;

        public NativeParallelHashMap<Entity, int>.ParallelWriter soulIndicesToRemove;

        public NativeQueue<TimeEvent<Command>>.ParallelWriter results;
        //public EntityCommandQueue<GameValhallaCommand>.ParallelWriter entityManager;

        public void Execute(int index)
        {
            var command = commands[index];
            if (command.soulIndex < 0)
                return;

            if (!this.souls.HasBuffer(command.entity))
                return;

            var souls = this.souls[command.entity];
            int soulIndex = GameSoul.IndexOf(command.soulIndex, souls);
            if (soulIndex == -1)
                return;

            var soul = souls[soulIndex];
            ref var respawnLevelExp = ref definition.Value.levels[soul.data.levelIndex].expToRespawn;

            float expCost = respawnLevelExp.value + respawnLevelExp.factor * soul.data.exp;
            var exp = exps[index];
            if (exp.value < expCost)
                return;

            if (!soulIndicesToRemove.TryAdd(command.entity, command.soulIndex))
                return;

            exp.value -= expCost;
            exps[index] = exp;

            TimeEvent<Command> result;
            result.time = time + command.time;
            result.value.entity = command.entity;
            result.value.transform = command.transform;
            result.value.value = soul.data;
            results.Enqueue(result);
        }
    }

    [BurstCompile]
    private struct RespawnEx : IJobChunk
    {
        public double time;

        public BlobAssetReference<GameValhallaDefinition> definition;

        [ReadOnly]
        public BufferLookup<GameSoul> souls;

        public ComponentTypeHandle<GameValhallaRespawnCommand> commandType;
        public ComponentTypeHandle<GameValhallaExp> expType;

        public NativeParallelHashMap<Entity, int>.ParallelWriter soulIndicesToRemove;
        public NativeQueue<TimeEvent<Command>>.ParallelWriter results;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Respawn respawn;
            respawn.time = time;
            respawn.definition = definition;
            respawn.souls = souls;
            respawn.commands = chunk.GetNativeArray(ref commandType);
            respawn.exps = chunk.GetNativeArray(ref expType);
            respawn.soulIndicesToRemove = soulIndicesToRemove;
            respawn.results = results;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
            {
                respawn.Execute(i);

                chunk.SetComponentEnabled(ref commandType, i, false);
            }
        }
    }

    [BurstCompile]
    public struct Add : IJob
    {
        public NativeQueue<TimeEvent<Command>> inputs;
        public TimeManager<Command>.Writer outputs;

        public void Execute()
        {
            while (inputs.TryDequeue(out var command))
                outputs.Invoke(command.time, command.value);
        }
    }

    [BurstCompile]
    private struct Remove : IJob
    {
        public NativeParallelHashMap<Entity, int> soulIndices;

        public BufferLookup<GameSoul> souls;

        public void Execute()
        {
            DynamicBuffer<GameSoul> souls;
            using (var keyValueArrays = soulIndices.GetKeyValueArrays(Allocator.Temp))
            {
                int length = keyValueArrays.Keys.Length;
                for (int i = 0; i < length; ++i)
                {
                    souls = this.souls[keyValueArrays.Keys[i]];
                    souls.RemoveAt(GameSoul.IndexOf(keyValueArrays.Values[i], souls));
                }
            }
        }
    }

    [BurstCompile]
    private struct ApplyRespawn : IJob//, IEntityCommandProducerJob
    {
        public int itemIdentityType;
        public EntityArchetype itemEntityArchetype;
        public Random random;

        public BlobAssetReference<GameValhallaDefinition> definition;

        [ReadOnly]
        public NativeArray<Command> commands;

        public GameItemManager itemManager;

        //public EntityCommandQueue<GameValhallaCommand>.Writer entityManager;

        public NativeList<GameValhallaCommand> results;

        //public EntityCommandQueue<GameValhallaCommand>.ParallelWriter entityManager;
        public SharedHashMap<GameItemHandle, EntityArchetype>.Writer createItemCommander;

        public EntityComponentAssigner.Writer assigner;

        public void Execute(int index)
        {
            var command = commands[index];

            int itemType = definition.Value.soulToItemTypes[command.value.type];
            if (itemType == -1)
                return;

            int count = 1;
            var handle = itemManager.Add(itemType, ref count);

            GameValhallaCommand result;
            result.variant = command.value.variant;
            result.type = command.value.type;
            result.entity = command.entity;
            result.transform = command.transform;
            result.nickname = command.value.nickname;
            result.itemHandle = handle;

            results.Add(result);

            createItemCommander.Add(handle, itemEntityArchetype);

            Entity entity = GameItemStructChangeFactory.Convert(handle);

            EntityDataIdentity identity;
            identity.type = itemIdentityType;
            identity.guid.Value = random.NextUInt4();
            assigner.SetComponentData(entity, identity);

            GameItemData item;
            item.handle = handle;
            assigner.SetComponentData(entity, item);

            GameItemObjectData instance;
            instance.type = command.value.type;
            assigner.SetComponentData(entity, instance);

            GameItemName name;
            name.value = new FixedString32Bytes(command.value.nickname);
            assigner.SetComponentData(entity, name);

            GameItemVariant variant;
            variant.value = command.value.variant;
            assigner.SetComponentData(entity, variant);

            GameItemOwner owner;
            owner.entity = command.entity;
            assigner.SetComponentData(entity, owner);

            GameItemLevel level;
            level.handle = command.value.levelIndex + 1;
            assigner.SetComponentData(entity, level);

            GameItemExp exp;
            exp.value = command.value.exp;
            assigner.SetComponentData(entity, exp);

            GameItemPower power;
            power.value = command.value.power;
            assigner.SetComponentData(entity, power);

            //entityManager.Enqueue(commands[index]);
        }

        public void Execute()
        {
            int numCommands = commands.Length;
            for (int i = 0; i < numCommands; ++i)
                Execute(i);
        }
    }

    private EntityQuery __collectGroup;
    private EntityQuery __upgradeGroup;
    private EntityQuery __renameGroup;
    private EntityQuery __destroyGroup;
    private EntityQuery __evoluteGroup;
    private EntityQuery __respawnGroup;
    private EntityQuery __structChangeManagerGroup;
    private EntityQuery __identityTypeGroup;

    private EntityTypeHandle __entityType;

    private BufferTypeHandle<GameValhallaSacrificer> __sacrificerType;

    private ComponentTypeHandle<GameValhallaExp> __expType;

    private ComponentTypeHandle<GameItemRoot> __itemRootType;

    private ComponentTypeHandle<GameValhallaCollectCommand> __collectCommandType;

    private ComponentTypeHandle<GameValhallaUpgradeCommand> __upgradeCommandType;

    private ComponentTypeHandle<GameValhallaRenameCommand> __renameCommandType;

    private ComponentTypeHandle<GameValhallaDestroyCommand> __destroyCommandType;

    private ComponentTypeHandle<GameValhallaEvoluteCommand> __evoluteCommandType;

    private ComponentTypeHandle<GameValhallaRespawnCommand> __respawnCommandType;

    private ComponentLookup<GameValhallaExp> __exps;

    private BufferLookup<GameSoul> __souls;

    private BufferLookup<GameValhallaSacrificer> __sacrificers;

    private GameItemManagerShared __itemManager;

    private GameSoulManager __soulManager;

    private NativeParallelHashMap<Entity, int> __soulIndicess;

    private NativeQueue<TimeEvent<Command>> __timeEvents;
    private NativeList<Command> __commands;
    private TimeManager<Command> __timeManager;

    private NativeQueue<GameItemHandle> __handles;

    private NativeQueue<UpgradeResult> __upgradeResults;
    private NativeQueue<RenameResult> __renameResults;
    private NativeQueue<DestroyResult> __destroyResults;

    private NativeQueue<EvoluteResult> __evoluteResults;

    public SharedList<EvoluteResult> evoluteResults
    {
        get;

        private set;
    }

    public SharedList<GameValhallaCommand> commands
    {
        get;

        private set;
    }

    public EntityArchetype itemEntityArchetype
    {
        get;

        private set;
    }

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __collectGroup = builder
                    .WithAll<GameValhallaExp, GameItemRoot>()
                    .WithAllRW<GameValhallaCollectCommand>()
                    .Build(ref state);
        __collectGroup.SetChangedVersionFilter(ComponentType.ReadWrite<GameValhallaCollectCommand>());

        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __upgradeGroup = builder
                .WithAllRW<GameValhallaUpgradeCommand, GameValhallaExp>()
                .Build(ref state);

        __upgradeGroup.SetChangedVersionFilter(ComponentType.ReadWrite<GameValhallaUpgradeCommand>());

        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __renameGroup = builder
                .WithAllRW<GameValhallaRenameCommand>()
                .Build(ref state);

        __renameGroup.SetChangedVersionFilter(ComponentType.ReadWrite<GameValhallaRenameCommand>());

        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __destroyGroup = builder
                .WithAllRW<GameValhallaDestroyCommand>()
                .Build(ref state);
        __destroyGroup.SetChangedVersionFilter(ComponentType.ReadWrite<GameValhallaDestroyCommand>());

        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __evoluteGroup = builder
                .WithAll<GameValhallaSacrificer>()
                .WithAllRW<GameValhallaEvoluteCommand>()
                .Build(ref state);

        __evoluteGroup.SetChangedVersionFilter(ComponentType.ReadWrite<GameValhallaEvoluteCommand>());

        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __respawnGroup = builder
                .WithAllRW<GameValhallaRespawnCommand, GameValhallaExp>()
                .Build(ref state);

        __respawnGroup.SetChangedVersionFilter(ComponentType.ReadWrite<GameValhallaRespawnCommand>());

        __structChangeManagerGroup = GameItemStructChangeManager.GetEntityQuery(ref state);

        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __identityTypeGroup = builder
                    .WithAll<GameItemIdentityType>()
                    .Build(ref state);

        __entityType = state.GetEntityTypeHandle();
        __sacrificerType = state.GetBufferTypeHandle<GameValhallaSacrificer>(true);
        __expType = state.GetComponentTypeHandle<GameValhallaExp>();
        __itemRootType = state.GetComponentTypeHandle<GameItemRoot>(true);
        __collectCommandType = state.GetComponentTypeHandle<GameValhallaCollectCommand>();
        __upgradeCommandType = state.GetComponentTypeHandle<GameValhallaUpgradeCommand>();
        __renameCommandType = state.GetComponentTypeHandle<GameValhallaRenameCommand>();
        __destroyCommandType = state.GetComponentTypeHandle<GameValhallaDestroyCommand>();
        __evoluteCommandType = state.GetComponentTypeHandle<GameValhallaEvoluteCommand>();
        __respawnCommandType = state.GetComponentTypeHandle<GameValhallaRespawnCommand>();
        __exps = state.GetComponentLookup<GameValhallaExp>();
        __souls = state.GetBufferLookup<GameSoul>();
        __sacrificers = state.GetBufferLookup<GameValhallaSacrificer>(true);

        var world = state.WorldUnmanaged;

        ref var itemSystem = ref state.WorldUnmanaged.GetExistingSystemUnmanaged<GameItemSystem>();

        __itemManager = itemSystem.manager;

        __soulManager = world.GetExistingSystemUnmanaged<GameSoulSystem>().manager;

        __handles = new NativeQueue<GameItemHandle>(Allocator.Persistent);

        __upgradeResults = new NativeQueue<UpgradeResult>(Allocator.Persistent);

        __renameResults = new NativeQueue<RenameResult>(Allocator.Persistent);

        __destroyResults = new NativeQueue<DestroyResult>(Allocator.Persistent);

        __evoluteResults = new NativeQueue<EvoluteResult>(Allocator.Persistent);

        evoluteResults = new SharedList<EvoluteResult>(Allocator.Persistent);

        __soulIndicess = new NativeParallelHashMap<Entity, int>(1, Allocator.Persistent);

        __timeEvents = new NativeQueue<TimeEvent<Command>>(Allocator.Persistent);

        __commands = new NativeList<Command>(Allocator.Persistent);

        __timeManager = new TimeManager<Command>(Allocator.Persistent);

        commands = new SharedList<GameValhallaCommand>(Allocator.Persistent);

        using (var componentTypes = itemSystem.entityArchetype.GetComponentTypes(Allocator.Temp))
        {
            var results = new NativeList<ComponentType>(Allocator.Temp);

            results.AddRange(componentTypes);
            results.Add(ComponentType.ReadOnly<GameItemObjectData>());
            results.Add(ComponentType.ReadOnly<GameItemName>());
            results.Add(ComponentType.ReadOnly<GameItemVariant>());
            results.Add(ComponentType.ReadWrite<GameItemOwner>());
            results.Add(ComponentType.ReadWrite<GameItemLevel>());
            results.Add(ComponentType.ReadWrite<GameItemExp>());
            results.Add(ComponentType.ReadWrite<GameItemPower>());

            itemEntityArchetype = state.EntityManager.CreateArchetype(results.AsArray());

            results.Dispose();
        }
    }

    //[BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        __handles.Dispose();

        __upgradeResults.Dispose();

        __renameResults.Dispose();

        __destroyResults.Dispose();

        __evoluteResults.Dispose();

        evoluteResults.Dispose();

        __soulIndicess.Dispose();

        __timeEvents.Dispose();

        __commands.Dispose();

        __timeManager.Dispose();

        commands.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.HasSingleton<GameValhallaData>())
            return;

        double time = state.WorldUnmanaged.Time.ElapsedTime;

        uint hash = RandomUtility.Hash(time);

        var definition = SystemAPI.GetSingleton<GameValhallaData>().definition;

        var manager = __itemManager.value;

        var inputDeps = state.Dependency;
        var entityType = __entityType.UpdateAsRef(ref state);
        var expType = __expType.UpdateAsRef(ref state);

        CollectEx collect;
        collect.hash = hash;
        collect.definition = definition;
        collect.hierarchy = manager.hierarchy;
        collect.entityType = entityType;
        collect.expType = expType;
        collect.itemRootType = __itemRootType.UpdateAsRef(ref state);
        collect.commandType = __collectCommandType.UpdateAsRef(ref state);
        collect.exps = __exps.UpdateAsRef(ref state);
        collect.handles = __handles.AsParallelWriter();

        ref var itemJobManager = ref __itemManager.lookupJobManager;

        var jobHandle = collect.ScheduleParallelByRef(__collectGroup, JobHandle.CombineDependencies(itemJobManager.readWriteJobHandle, inputDeps));

        DeleteHandles deleteHandles;
        deleteHandles.manager = manager;
        deleteHandles.handles = __handles;
        var finalCollectJobHandle = deleteHandles.ScheduleByRef(jobHandle);

        //lookupJobManager.readWriteJobHandle = finalCollectJobHandle;

        ref var souls = ref __souls.UpdateAsRef(ref state);

        UpgradeEx upgrade;
        upgrade.definition = definition;
        upgrade.souls = souls;
        upgrade.commandType = __upgradeCommandType.UpdateAsRef(ref state);
        upgrade.expType = expType;
        upgrade.results = __upgradeResults.AsParallelWriter();
        var finalUpgradeJobHandle = upgrade.ScheduleParallelByRef(__upgradeGroup, jobHandle);

        RenameEx rename;
        rename.souls = souls;
        rename.commandType = __renameCommandType.UpdateAsRef(ref state);
        rename.results = __renameResults.AsParallelWriter();
        var finalRenameJobHandle = rename.ScheduleParallelByRef(__renameGroup, inputDeps);

        DestroyEx destroy;
        destroy.definition = definition;
        destroy.souls = souls;
        destroy.commandType = __destroyCommandType.UpdateAsRef(ref state);
        destroy.expType = expType;
        destroy.results = __destroyResults.AsParallelWriter();
        var finalDestroyJobHandle = destroy.ScheduleParallelByRef(__destroyGroup, finalUpgradeJobHandle);

        EvoluteEx evolute;
        evolute.hash = hash;
        evolute.definition = definition;
        evolute.manager = __soulManager;
        evolute.souls = souls;
        evolute.sacrificerType = __sacrificerType.UpdateAsRef(ref state);
        evolute.entityType = entityType;
        evolute.commandType = __evoluteCommandType.UpdateAsRef(ref state);
        evolute.results = __evoluteResults.AsParallelWriter();

        var finalEvoluteJobHandle = evolute.ScheduleParallelByRef(__evoluteGroup, inputDeps);

        var finalJobHandle = JobHandle.CombineDependencies(finalRenameJobHandle, finalEvoluteJobHandle);
        if (__identityTypeGroup.HasSingleton<GameItemIdentityType>())
        {
            var entityCount = new NativeArray<int>(1, Allocator.TempJob, NativeArrayOptions.ClearMemory);

            inputDeps = __respawnGroup.CalculateEntityCountAsync(entityCount, inputDeps);

            ClearSoulIndicess clearSoulIndicess;
            clearSoulIndicess.capacity = entityCount;
            clearSoulIndicess.soulIndicess = __soulIndicess;
            jobHandle = clearSoulIndicess.ScheduleByRef(inputDeps);

            RespawnEx respawn;
            respawn.time = time;
            respawn.definition = definition;
            respawn.souls = souls;
            respawn.commandType = __respawnCommandType.UpdateAsRef(ref state);
            respawn.expType = expType;
            respawn.soulIndicesToRemove = __soulIndicess.AsParallelWriter();
            respawn.results = __timeEvents.AsParallelWriter();
            jobHandle = respawn.ScheduleParallelByRef(__respawnGroup, JobHandle.CombineDependencies(jobHandle, finalDestroyJobHandle));

            Add add;
            add.inputs = __timeEvents;
            add.outputs = __timeManager.writer;
            var finalRespawnJobHandle = add.ScheduleByRef(jobHandle);

            __commands.Clear();

            finalRespawnJobHandle = __timeManager.Schedule(time, ref __commands, finalRespawnJobHandle);

            var itemStructChangeManager = __structChangeManagerGroup.GetSingleton<GameItemStructChangeManager>();
            var createItemCommander = itemStructChangeManager.createEntityCommander;
            var itemAssigner = itemStructChangeManager.assigner;

            //var entityManager = __entityManager.Create();
            var commands = this.commands;

            ApplyRespawn applyRespawn;
            applyRespawn.itemIdentityType = __identityTypeGroup.GetSingleton<GameItemIdentityType>().value;
            applyRespawn.itemEntityArchetype = itemEntityArchetype;
            applyRespawn.random = new Random(math.max(1, hash));
            applyRespawn.definition = definition;
            applyRespawn.commands = __commands.AsDeferredJobArray();
            applyRespawn.itemManager = __itemManager.value;
            applyRespawn.results = commands.writer;
            applyRespawn.createItemCommander = createItemCommander.writer;
            applyRespawn.assigner = itemAssigner.writer;

            ref var commandsJobManager = ref commands.lookupJobManager;
            ref var commanderJobManager = ref createItemCommander.lookupJobManager;

            finalRespawnJobHandle = JobHandle.CombineDependencies(finalRespawnJobHandle, finalCollectJobHandle, commandsJobManager.readWriteJobHandle);

            finalRespawnJobHandle = applyRespawn.ScheduleByRef(JobHandle.CombineDependencies(finalRespawnJobHandle, commanderJobManager.readWriteJobHandle, itemAssigner.jobHandle));

            itemAssigner.jobHandle = finalRespawnJobHandle;

            commanderJobManager.readWriteJobHandle = finalRespawnJobHandle;

            commandsJobManager.readWriteJobHandle = finalRespawnJobHandle;

            itemJobManager.readWriteJobHandle = finalRespawnJobHandle;

            //timeJobHandle = _values.Clear(timeJobHandle);

            Remove remove;
            remove.soulIndices = __soulIndicess;
            remove.souls = souls;
            jobHandle = remove.ScheduleByRef(jobHandle);

            finalJobHandle = JobHandle.CombineDependencies(finalJobHandle, finalRespawnJobHandle, jobHandle);
        }
        else
            finalJobHandle = JobHandle.CombineDependencies(finalJobHandle, finalCollectJobHandle, finalDestroyJobHandle);

        ApplyDestroy applyDestroy;
        applyDestroy.results = __destroyResults;
        applyDestroy.souls = souls;
        finalJobHandle = applyDestroy.ScheduleByRef(finalJobHandle);

        ApplyUpgrade applyUpgrade;
        applyUpgrade.results = __upgradeResults;
        applyUpgrade.souls = souls;
        finalJobHandle = applyUpgrade.ScheduleByRef(finalJobHandle);

        ApplyRename applyRename;
        applyRename.results = __renameResults;
        applyRename.souls = souls;
        finalJobHandle = applyRename.ScheduleByRef(finalJobHandle);

        var evoluteResults = this.evoluteResults;
        ref var evoluteResultsJobManager = ref evoluteResults.lookupJobManager;

        ApplyEvolute applyEvolute;
        applyEvolute.manager = __soulManager;
        applyEvolute.sacrificers = __sacrificers.UpdateAsRef(ref state);
        applyEvolute.inputs = __evoluteResults;
        applyEvolute.souls = souls;
        applyEvolute.outputs = evoluteResults.writer;

        finalJobHandle = applyEvolute.ScheduleByRef(JobHandle.CombineDependencies(finalJobHandle, evoluteResultsJobManager.readWriteJobHandle));

        evoluteResultsJobManager.readWriteJobHandle = finalJobHandle;

        state.Dependency = finalJobHandle;
    }
}