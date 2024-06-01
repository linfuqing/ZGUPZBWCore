using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
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

[Flags]
public enum GameValhallaFlag
{
    SoulAsItem = 0x01
}

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
        public int type;
        
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
        gameObjectEntity.SetComponentEnabled<GameItemRoot>(true);

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
    CreateAfter(typeof(GameItemRootEntitySystem)), 
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
        public GameValhallaFlag flag;
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

    private struct CollectHandles
    {
        public Random random;

        public BlobAssetReference<GameValhallaDefinition> definition;

        [ReadOnly]
        public GameItemManager.Hierarchy hierarchy;

        [ReadOnly]
        public SharedHashMap<GameItemHandle, Entity>.Reader rootEntities;

        [ReadOnly]
        public BufferAccessor<GameValhallaCollectCommand> commands;

        [ReadOnly]
        public NativeArray<Entity> entityArray;

        [ReadOnly]
        public NativeArray<GameValhallaExp> exps;

        [NativeDisableContainerSafetyRestriction]
        public ComponentLookup<GameValhallaExp> expMap;

        public NativeQueue<GameItemHandle>.ParallelWriter handles;

        public bool Execute(int index)
        {
            bool result = false;
            var commands = this.commands[index];
            if (commands.Length > 0)
            {
                ref var itemTypeToExps = ref definition.Value.itemTypeToExps;
                GameItemInfo item;
                float value;
                var exp = exps[index];
                foreach (var command in commands)
                {
                    if (hierarchy.TryGetValue(command.handle, out item))
                    {
                        ref var itemTypeToExp = ref itemTypeToExps[item.type];

                        value = random.NextFloat(itemTypeToExp.min * item.count, itemTypeToExp.max * item.count);
                        if (value > math.FLT_MIN_NORMAL)
                        {
                            exp.value += value;

                            handles.Enqueue(command.handle);

                            result = true;
                        }
                    }
                }

                if (result)
                    expMap[entityArray[index]] = exp;
            }

            return result;
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
        public SharedHashMap<GameItemHandle, Entity>.Reader rootEntities;

        [ReadOnly]
        public EntityTypeHandle entityType;

        [ReadOnly]
        public ComponentTypeHandle<GameValhallaExp> expType;

        [ReadOnly]
        public ComponentTypeHandle<GameItemRoot> itemRootType;

        public BufferTypeHandle<GameValhallaCollectCommand> commandType;

        [NativeDisableContainerSafetyRestriction]
        public ComponentLookup<GameValhallaExp> exps;

        public NativeQueue<GameItemHandle>.ParallelWriter handles;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            var random = new Random(hash ^ (uint)unfilteredChunkIndex);
            var entityArray = chunk.GetNativeArray(entityType);
            var exps = chunk.GetNativeArray(ref expType);

            Collect collect;
            collect.random = random;
            collect.definition = definition;
            collect.hierarchy = hierarchy;
            collect.entityArray = entityArray;
            collect.exps = exps;
            collect.itemRoots = chunk.GetNativeArray(ref itemRootType);
            collect.expMap = this.exps;
            collect.handles = handles;

            CollectHandles collectHandles;
            collectHandles.random = random;
            collectHandles.definition = definition;
            collectHandles.hierarchy = hierarchy;
            collectHandles.rootEntities = rootEntities;
            collectHandles.entityArray = entityArray;
            collectHandles.commands = chunk.GetBufferAccessor(ref commandType);
            collectHandles.exps = exps;
            collectHandles.expMap = this.exps;
            collectHandles.handles = handles;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
            {
                if(!collectHandles.Execute(i))
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

    private struct Destroy
    {
        public BlobAssetReference<GameValhallaDefinition> definition;

        [ReadOnly]
        public BufferLookup<GameSoul> souls;
        public BufferAccessor<GameValhallaDestroyCommand> commands;
        public NativeArray<GameValhallaExp> exps;

        public NativeQueue<DestroyResult>.ParallelWriter results;

        public void Execute(int index)
        {
            int soulIndex;
            DestroyResult result;
            GameValhallaExp exp;
            GameSoul soul;
            DynamicBuffer<GameSoul> souls;
            var commands = this.commands[index];
            foreach (var command in commands)
            {
                if (command.soulIndex < 0)
                    continue;

                if (!this.souls.HasBuffer(command.entity))
                    continue;

                souls = this.souls[command.entity];
                soulIndex = GameSoul.IndexOf(command.soulIndex, souls);
                if (soulIndex == -1)
                    continue;

                soul = souls[soulIndex];
                ref var destroyLevelExp = ref definition.Value.levels[soul.data.levelIndex].expToDestroy;

                exp = exps[index];
                exp.value += destroyLevelExp.value + destroyLevelExp.factor * soul.data.exp;
                exps[index] = exp;

                result.soulIndex = command.soulIndex;
                result.entity = command.entity;
                results.Enqueue(result);
            }

            commands.Clear();
        }
    }

    [BurstCompile]
    private struct DestroyEx : IJobChunk
    {
        public BlobAssetReference<GameValhallaDefinition> definition;

        [ReadOnly]
        public BufferLookup<GameSoul> souls;
        public BufferTypeHandle<GameValhallaDestroyCommand> commandType;
        public ComponentTypeHandle<GameValhallaExp> expType;

        public NativeQueue<DestroyResult>.ParallelWriter results;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Destroy destroy;
            destroy.souls = souls;
            destroy.definition = definition;
            destroy.commands = chunk.GetBufferAccessor(ref commandType);
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

        public BufferLookup<GameSoulDislike> soulsDislike;

        public void Execute()
        {
            DynamicBuffer<GameSoulDislike> soulsDislike;
            DynamicBuffer<GameSoul> souls;
            while (results.TryDequeue(out var result))
            {
                souls = this.souls[result.entity];

                souls.RemoveAt(GameSoul.IndexOf(result.soulIndex, souls));

                if(this.soulsDislike.HasBuffer(result.entity))
                {
                    soulsDislike = this.soulsDislike[result.entity];

                    soulsDislike.RemoveAt(GameSoulDislike.IndexOf(result.soulIndex, soulsDislike));
                }
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
            var exp = exps[index];
            float value = math.min(soul.data.exp + (command.exp > math.FLT_MIN_NORMAL ? command.exp : exp.value), definition.Value.levels[soul.data.levelIndex].maxExp) - soul.data.exp;

            /*if (exp.value < value)
                return;*/

            exp.value = command.exp > math.FLT_MIN_NORMAL ? exp.value - math.min(exp.value, value) : 0.0f;
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
    private struct ClearSoulIndices : IJob
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
            bool hasExp = index < exps.Length;
            var exp = hasExp ? exps[index] : default;
            if (exp.value < expCost)
                return;

            if (!soulIndicesToRemove.TryAdd(command.entity, command.soulIndex))
                return;

            if (hasExp)
            {
                exp.value -= expCost;
                exps[index] = exp;
            }

            TimeEvent<Command> result;
            result.time = time + command.time;
            result.value.flag = command.flag;
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

        [ReadOnly]
        public ComponentLookup<GameItemRoot> itemRoots;

        public GameItemManager itemManager;

        //public EntityCommandQueue<GameValhallaCommand>.Writer entityManager;

        public NativeList<GameValhallaCommand> results;

        //public EntityCommandQueue<GameValhallaCommand>.ParallelWriter entityManager;
        public SharedHashMap<GameItemHandle, EntityArchetype>.Writer createItemCommander;

        public EntityComponentAssigner.Writer assigner;

        public void Execute(int index)
        {
            var command = commands[index];

            ref var definition = ref this.definition.Value;
            int type = definition.levels[command.value.levelIndex].type, itemType = definition.soulToItemTypes[type];
            if (itemType == -1)
                return;

            int count = 1;
            var handle = itemManager.Add(itemType, ref count);

            if ((command.flag & GameValhallaFlag.SoulAsItem) != GameValhallaFlag.SoulAsItem || 
                itemRoots.HasComponent(command.entity) || 
                itemManager.Find(
                    itemRoots[command.entity].handle, 
                    itemType, 
                    1, 
                    out int parentChildIndex, 
                    out var parentHandle) || 
                itemManager.Move(handle, parentHandle, parentChildIndex) == GameItemMoveType.Error)
            {
                GameValhallaCommand result;
                result.variant = command.value.variant;
                result.type = type;
                result.entity = command.entity;
                result.transform = command.transform;
                result.nickname = command.value.nickname;
                result.itemHandle = handle;

                results.Add(result);
            }
            
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
            instance.type = type;
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

    private BufferTypeHandle<GameValhallaCollectCommand> __collectCommandType;
    private BufferTypeHandle<GameValhallaDestroyCommand> __destroyCommandType;

    private ComponentTypeHandle<GameValhallaUpgradeCommand> __upgradeCommandType;

    private ComponentTypeHandle<GameValhallaRenameCommand> __renameCommandType;

    private ComponentTypeHandle<GameValhallaEvoluteCommand> __evoluteCommandType;

    private ComponentTypeHandle<GameValhallaRespawnCommand> __respawnCommandType;

    private ComponentLookup<GameValhallaExp> __exps;

    private BufferLookup<GameSoulDislike> __soulsDislike;

    private BufferLookup<GameSoul> __souls;

    private BufferLookup<GameSoul> __soulsRO;

    private BufferLookup<GameValhallaSacrificer> __sacrificers;

    private ComponentLookup<GameItemRoot> __itemRoots;

    private GameItemManagerShared __itemManager;

    private GameSoulManager __soulManager;

    private SharedHashMap<GameItemHandle, Entity> __rootEntities;

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
            __destroyGroup = builder
                .WithAllRW<GameValhallaDestroyCommand>()
                .Build(ref state);
        __destroyGroup.SetChangedVersionFilter(ComponentType.ReadWrite<GameValhallaDestroyCommand>());

        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __renameGroup = builder
                .WithAllRW<GameValhallaRenameCommand>()
                .Build(ref state);

        __renameGroup.SetChangedVersionFilter(ComponentType.ReadWrite<GameValhallaRenameCommand>());

        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __upgradeGroup = builder
                .WithAllRW<GameValhallaUpgradeCommand, GameValhallaExp>()
                .Build(ref state);

        __upgradeGroup.SetChangedVersionFilter(ComponentType.ReadWrite<GameValhallaUpgradeCommand>());

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
        __collectCommandType = state.GetBufferTypeHandle<GameValhallaCollectCommand>();
        __destroyCommandType = state.GetBufferTypeHandle<GameValhallaDestroyCommand>();
        __upgradeCommandType = state.GetComponentTypeHandle<GameValhallaUpgradeCommand>();
        __renameCommandType = state.GetComponentTypeHandle<GameValhallaRenameCommand>();
        __evoluteCommandType = state.GetComponentTypeHandle<GameValhallaEvoluteCommand>();
        __respawnCommandType = state.GetComponentTypeHandle<GameValhallaRespawnCommand>();
        __exps = state.GetComponentLookup<GameValhallaExp>();
        __soulsDislike = state.GetBufferLookup<GameSoulDislike>();
        __souls = state.GetBufferLookup<GameSoul>();
        __soulsRO = state.GetBufferLookup<GameSoul>(true);
        __sacrificers = state.GetBufferLookup<GameValhallaSacrificer>(true);
        __itemRoots = state.GetComponentLookup<GameItemRoot>(true);

        var world = state.WorldUnmanaged;

        ref var itemSystem = ref state.WorldUnmanaged.GetExistingSystemUnmanaged<GameItemSystem>();

        __itemManager = itemSystem.manager;

        __soulManager = world.GetExistingSystemUnmanaged<GameSoulSystem>().manager;

        __rootEntities = world.GetExistingSystemUnmanaged<GameItemRootEntitySystem>().entities;

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
        collect.rootEntities = __rootEntities.reader;
        collect.entityType = entityType;
        collect.expType = expType;
        collect.itemRootType = __itemRootType.UpdateAsRef(ref state);
        collect.commandType = __collectCommandType.UpdateAsRef(ref state);
        collect.exps = __exps.UpdateAsRef(ref state);
        collect.handles = __handles.AsParallelWriter();

        ref var itemJobManager = ref __itemManager.lookupJobManager;
        ref var rootEntitiesJobManager = ref __rootEntities.lookupJobManager;

        var jobHandle = JobHandle.CombineDependencies(itemJobManager.readWriteJobHandle, rootEntitiesJobManager.readOnlyJobHandle, inputDeps);

        jobHandle = collect.ScheduleParallelByRef(__collectGroup, jobHandle);

        rootEntitiesJobManager.AddReadOnlyDependency(jobHandle);

        DeleteHandles deleteHandles;
        deleteHandles.manager = manager;
        deleteHandles.handles = __handles;
        var finalCollectJobHandle = deleteHandles.ScheduleByRef(jobHandle);

        //lookupJobManager.readWriteJobHandle = finalCollectJobHandle;

        ref var soulsRO = ref __soulsRO.UpdateAsRef(ref state);

        DestroyEx destroy;
        destroy.definition = definition;
        destroy.souls = soulsRO;
        destroy.commandType = __destroyCommandType.UpdateAsRef(ref state);
        destroy.expType = expType;
        destroy.results = __destroyResults.AsParallelWriter();
        var finalDestroyJobHandle = destroy.ScheduleParallelByRef(__destroyGroup, jobHandle);

        RenameEx rename;
        rename.souls = soulsRO;
        rename.commandType = __renameCommandType.UpdateAsRef(ref state);
        rename.results = __renameResults.AsParallelWriter();
        var finalRenameJobHandle = rename.ScheduleParallelByRef(__renameGroup, inputDeps);

        UpgradeEx upgrade;
        upgrade.definition = definition;
        upgrade.souls = soulsRO;
        upgrade.commandType = __upgradeCommandType.UpdateAsRef(ref state);
        upgrade.expType = expType;
        upgrade.results = __upgradeResults.AsParallelWriter();
        var finalUpgradeJobHandle = upgrade.ScheduleParallelByRef(__upgradeGroup, finalDestroyJobHandle);

        EvoluteEx evolute;
        evolute.hash = hash;
        evolute.definition = definition;
        evolute.manager = __soulManager;
        evolute.souls = soulsRO;
        evolute.sacrificerType = __sacrificerType.UpdateAsRef(ref state);
        evolute.entityType = entityType;
        evolute.commandType = __evoluteCommandType.UpdateAsRef(ref state);
        evolute.results = __evoluteResults.AsParallelWriter();

        var souls = __souls.UpdateAsRef(ref state);

        var finalEvoluteJobHandle = evolute.ScheduleParallelByRef(__evoluteGroup, inputDeps);

        var finalJobHandle = JobHandle.CombineDependencies(finalRenameJobHandle, finalEvoluteJobHandle);
        if (__identityTypeGroup.HasSingleton<GameItemIdentityType>())
        {
            var entityCount = new NativeArray<int>(1, Allocator.TempJob, NativeArrayOptions.ClearMemory);

            inputDeps = __respawnGroup.CalculateEntityCountAsync(entityCount, inputDeps);

            ClearSoulIndices clearSoulIndices;
            clearSoulIndices.capacity = entityCount;
            clearSoulIndices.soulIndicess = __soulIndicess;
            var finalRespawnJobHandle = clearSoulIndices.ScheduleByRef(inputDeps);

            RespawnEx respawn;
            respawn.time = time;
            respawn.definition = definition;
            respawn.souls = soulsRO;
            respawn.commandType = __respawnCommandType.UpdateAsRef(ref state);
            respawn.expType = expType;
            respawn.soulIndicesToRemove = __soulIndicess.AsParallelWriter();
            respawn.results = __timeEvents.AsParallelWriter();
            finalRespawnJobHandle = respawn.ScheduleParallelByRef(__respawnGroup, JobHandle.CombineDependencies(finalRespawnJobHandle, finalUpgradeJobHandle));

            Add add;
            add.inputs = __timeEvents;
            add.outputs = __timeManager.writer;
            finalRespawnJobHandle = add.ScheduleByRef(finalRespawnJobHandle);

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
            applyRespawn.itemRoots = __itemRoots.UpdateAsRef(ref state);
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
            finalJobHandle = remove.ScheduleByRef(JobHandle.CombineDependencies(finalJobHandle, finalRespawnJobHandle));
        }
        else
            finalJobHandle = JobHandle.CombineDependencies(finalJobHandle, finalCollectJobHandle, finalUpgradeJobHandle);

        ApplyDestroy applyDestroy;
        applyDestroy.results = __destroyResults;
        applyDestroy.souls = souls;
        applyDestroy.soulsDislike = __soulsDislike.UpdateAsRef(ref state);
        finalJobHandle = applyDestroy.ScheduleByRef(finalJobHandle);

        ApplyRename applyRename;
        applyRename.results = __renameResults;
        applyRename.souls = souls;
        finalJobHandle = applyRename.ScheduleByRef(finalJobHandle);

        ApplyUpgrade applyUpgrade;
        applyUpgrade.results = __upgradeResults;
        applyUpgrade.souls = souls;
        finalJobHandle = applyUpgrade.ScheduleByRef(finalJobHandle);

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