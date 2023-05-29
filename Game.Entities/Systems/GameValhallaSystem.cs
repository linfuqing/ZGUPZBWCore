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

[assembly: RegisterGenericJobType(typeof(TimeManager<GameValhallaRespawnSystem.Command>.Clear))]
[assembly: RegisterGenericJobType(typeof(TimeManager<GameValhallaRespawnSystem.Command>.UpdateEvents))]

public abstract class GameValhallaCommander : IEntityCommander<GameValhallaCommand>
{
    private struct Initializer : IEntityDataInitializer
    {
        //private GameSoulData __soul;
        private GameItemHandle __itemHandle;
        private FixedString32Bytes __nickname;

        public Initializer(in GameItemHandle itemHandle, in FixedString32Bytes nickname)
        {
            __itemHandle = itemHandle;
            __nickname = nickname;
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
        }
    }

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
                new Initializer(command.itemHandle, command.nickname));
        }
    }

    void IDisposable.Dispose()
    {

    }
}

public partial class GameValhallaCollectSystem : SystemBase
{
    public struct Exp
    {
        public float min;
        public float max;
    }

    private struct Collect
    {
        public Random random;

        [ReadOnly]
        public GameItemManager.Hierarchy hierarchy;

        [ReadOnly]
        public NativeParallelHashMap<int, Exp> typeToExps;

        [ReadOnly]
        public NativeArray<Entity> entityArray;

        [ReadOnly]
        public NativeArray<GameValhallaExp> exps;

        [ReadOnly]
        public NativeArray<GameItemRoot> itemRoots;

        [ReadOnly]
        public NativeArray<GameValhallaCollectCommand> commands;

        public NativeArray<GameValhallaVersion> versions;

        public NativeQueue<GameItemHandle>.ParallelWriter handles;

        [NativeDisableContainerSafetyRestriction]
        public ComponentLookup<GameValhallaExp> expMap;

        public void Execute(int index)
        {
            var command = commands[index];
            var version = versions[index];
            if (command.version != version.value)
                return;

            ++version.value;
            versions[index] = version;

            var root = itemRoots[index].handle;

            bool result = false;
            var exp = exps[index];
            if (hierarchy.GetChildren(root, out var enumerator, out var _))
            {
                Exp typeToExp;
                GameItemHandle handle;
                GameItemInfo item;
                while (enumerator.MoveNext())
                {
                    handle = enumerator.Current.handle;
                    if (hierarchy.TryGetValue(handle, out item) &&
                        typeToExps.TryGetValue(item.type, out typeToExp))
                    {
                        exp.value += random.NextFloat(typeToExp.min * item.count, typeToExp.max * item.count);

                        handles.Enqueue(handle);

                        result = true;
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
        public long hash;

        [ReadOnly]
        public GameItemManager.Hierarchy hierarchy;

        [ReadOnly]
        public NativeParallelHashMap<int, Exp> typeToExps;

        [ReadOnly]
        public EntityTypeHandle entityType;

        [ReadOnly]
        public ComponentTypeHandle<GameValhallaExp> expType;

        [ReadOnly]
        public ComponentTypeHandle<GameItemRoot> itemRootType;

        [ReadOnly]
        public ComponentTypeHandle<GameValhallaCollectCommand> commandType;

        public ComponentTypeHandle<GameValhallaVersion> versionType;

        public NativeQueue<GameItemHandle>.ParallelWriter handles;

        [NativeDisableContainerSafetyRestriction]
        public ComponentLookup<GameValhallaExp> expMap;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Collect collect;
            collect.random = new Random((uint)hash ^ (uint)(hash >> 32) ^ (uint)unfilteredChunkIndex);
            collect.hierarchy = hierarchy;
            collect.typeToExps = typeToExps;
            collect.entityArray = chunk.GetNativeArray(entityType);
            collect.exps = chunk.GetNativeArray(ref expType);
            collect.itemRoots = chunk.GetNativeArray(ref itemRootType);
            collect.commands = chunk.GetNativeArray(ref commandType);
            collect.versions = chunk.GetNativeArray(ref versionType);
            collect.handles = handles;
            collect.expMap = expMap;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                collect.Execute(i);
        }
    }

    [BurstCompile]
    private struct Delete : IJob
    {
        public GameItemManager manager;

        public NativeQueue<GameItemHandle> handles;

        public void Execute()
        {
            while (handles.TryDequeue(out var handle))
                manager.Remove(handle, 0);
        }
    }

    private EntityQuery __group;
    private GameItemManagerShared __itemManager;

    private NativeParallelHashMap<int, Exp> __typeToExps;
    private NativeQueue<GameItemHandle> __handles;

    public void Create(IReadOnlyCollection<KeyValuePair<int, Exp>> typeToExps)
    {
        __typeToExps = new NativeParallelHashMap<int, Exp>(typeToExps.Count, Allocator.Persistent);

        foreach (var typeToExp in typeToExps)
            __typeToExps.Add(typeToExp.Key, typeToExp.Value);
    }

    protected override void OnCreate()
    {
        base.OnCreate();

        __group = GetEntityQuery(
            ComponentType.ReadOnly<GameValhallaExp>(),
            ComponentType.ReadOnly<GameItemRoot>(),
            ComponentType.ReadOnly<GameValhallaCollectCommand>(),
            ComponentType.ReadWrite<GameValhallaVersion>());
        __group.SetChangedVersionFilter(typeof(GameValhallaCollectCommand));

        __itemManager = World.GetOrCreateSystemUnmanaged<GameItemSystem>().manager;

        __handles = new NativeQueue<GameItemHandle>(Allocator.Persistent);
    }

    protected override void OnDestroy()
    {
        if (__typeToExps.IsCreated)
            __typeToExps.Dispose();

        __handles.Dispose();

        base.OnDestroy();
    }

    protected override void OnUpdate()
    {
        if (!__typeToExps.IsCreated)
            return;

        var manager = __itemManager.value;

        CollectEx collect;
        collect.hash = math.aslong(World.Time.ElapsedTime);
        collect.hierarchy = manager.hierarchy;
        collect.typeToExps = __typeToExps;
        collect.entityType = GetEntityTypeHandle();
        collect.expType = GetComponentTypeHandle<GameValhallaExp>(true);
        collect.itemRootType = GetComponentTypeHandle<GameItemRoot>(true);
        collect.commandType = GetComponentTypeHandle<GameValhallaCollectCommand>(true);
        collect.versionType = GetComponentTypeHandle<GameValhallaVersion>();
        collect.handles = __handles.AsParallelWriter();
        collect.expMap = GetComponentLookup<GameValhallaExp>();

        ref var lookupJobManager = ref __itemManager.lookupJobManager;

        var jobHandle = collect.ScheduleParallel(__group, JobHandle.CombineDependencies(lookupJobManager.readWriteJobHandle,  Dependency));

        Delete delete;
        delete.manager = manager;
        delete.handles = __handles;
        jobHandle = delete.Schedule(jobHandle);

        lookupJobManager.readWriteJobHandle = jobHandle;

        Dependency = jobHandle;
    }
}

public partial class GameValhallaUpgradeSystem : SystemBase
{
    private struct Result
    {
        public int soulIndex;
        public float exp;
        public Entity entity;
    }

    private struct Upgrade
    {
        [ReadOnly]
        public BufferLookup<GameSoul> souls;
        [ReadOnly]
        public NativeArray<float> maxExps;
        [ReadOnly]
        public NativeArray<GameValhallaUpgradeCommand> commands;
        public NativeArray<GameValhallaVersion> versions;
        public NativeArray<GameValhallaExp> exps;

        public NativeQueue<Result>.ParallelWriter results;

        public void Execute(int index)
        {
            var command = commands[index];
            if (command.soulIndex < 0)
                return;

            var version = versions[index];
            if (command.version != version.value)
                return;

            ++version.value;
            versions[index] = version;

            if (!this.souls.HasBuffer(command.entity))
                return;

            var souls = this.souls[command.entity];
            int soulIndex = GameSoul.IndexOf(command.soulIndex, souls);
            if (soulIndex == -1)
                return;

            var soul = souls[soulIndex];
            float value = math.min(soul.data.exp + command.exp, maxExps[soul.data.levelIndex]) - soul.data.exp;
            var exp = exps[index];
            /*if (exp.value < value)
                return;*/

            exp.value -= math.min(exp.value, value);
            exps[index] = exp;

            Result result;
            result.soulIndex = soulIndex;
            result.exp = value;
            result.entity = command.entity;
            results.Enqueue(result);
        }
    }

    [BurstCompile]
    private struct UpgradeEx : IJobChunk
    {
        [ReadOnly]
        public BufferLookup<GameSoul> souls;
        [ReadOnly]
        public NativeArray<float> maxExps;
        [ReadOnly]
        public ComponentTypeHandle<GameValhallaUpgradeCommand> commandType;
        public ComponentTypeHandle<GameValhallaVersion> versionType;
        public ComponentTypeHandle<GameValhallaExp> expType;

        public NativeQueue<Result>.ParallelWriter results;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Upgrade upgrade;
            upgrade.souls = souls;
            upgrade.maxExps = maxExps;
            upgrade.commands = chunk.GetNativeArray(ref commandType);
            upgrade.versions = chunk.GetNativeArray(ref versionType);
            upgrade.exps = chunk.GetNativeArray(ref expType);
            upgrade.results = results;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                upgrade.Execute(i);
        }
    }

    [BurstCompile]
    private struct Apply : IJob
    {
        public NativeQueue<Result> results;

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

    private EntityQuery __group;
    private NativeQueue<Result> __results;

    public NativeArray<float> maxExps
    {
        get;

        private set;
    }

    public void Create(float[] maxExps)
    {
        this.maxExps = new NativeArray<float>(maxExps, Allocator.Persistent);
    }

    protected override void OnCreate()
    {
        base.OnCreate();

        __group = GetEntityQuery(
            ComponentType.ReadOnly<GameValhallaUpgradeCommand>(),
            ComponentType.ReadWrite<GameValhallaVersion>(),
            ComponentType.ReadWrite<GameValhallaExp>());

        __group.SetChangedVersionFilter(typeof(GameValhallaUpgradeCommand));

        __results = new NativeQueue<Result>(Allocator.Persistent);
    }

    protected override void OnDestroy()
    {
        if (maxExps.IsCreated)
            maxExps.Dispose();

        __results.Dispose();

        base.OnDestroy();
    }

    protected override void OnUpdate()
    {
        var maxExps = this.maxExps;
        if (!maxExps.IsCreated)
            return;

        var souls = GetBufferLookup<GameSoul>();

        UpgradeEx upgrade;
        upgrade.souls = souls;
        upgrade.maxExps = maxExps;
        upgrade.commandType = GetComponentTypeHandle<GameValhallaUpgradeCommand>(true);
        upgrade.versionType = GetComponentTypeHandle<GameValhallaVersion>();
        upgrade.expType = GetComponentTypeHandle<GameValhallaExp>();
        upgrade.results = __results.AsParallelWriter();
        var jobHandle = upgrade.ScheduleParallel(__group, Dependency);

        Apply apply;
        apply.results = __results;
        apply.souls = souls;
        Dependency = apply.Schedule(jobHandle);
    }
}

public partial class GameValhallaRenameSystem : SystemBase
{
    [Serializable]
    public struct Result
    {
        public int soulIndex;
        public Entity entity;
        public FixedString32Bytes name;
    }

    private struct Rename
    {
        [ReadOnly]
        public BufferLookup<GameSoul> souls;
        [ReadOnly]
        public NativeArray<GameValhallaRenameCommand> commands;
        public NativeArray<GameValhallaVersion> versions;

        public NativeQueue<Result>.ParallelWriter results;

        public void Execute(int index)
        {
            var command = commands[index];
            if (command.soulIndex < 0)
                return;

            var version = versions[index];
            if (command.version != version.value)
                return;

            ++version.value;
            versions[index] = version;

            if (!this.souls.HasBuffer(command.entity))
                return;

            var souls = this.souls[command.entity];
            int soulIndex = GameSoul.IndexOf(command.soulIndex, souls);
            if (soulIndex == -1)
                return;

            Result result;
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
        [ReadOnly]
        public ComponentTypeHandle<GameValhallaRenameCommand> commandType;
        public ComponentTypeHandle<GameValhallaVersion> versionType;

        public NativeQueue<Result>.ParallelWriter results;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Rename rename;
            rename.souls = souls;
            rename.commands = chunk.GetNativeArray(ref commandType);
            rename.versions = chunk.GetNativeArray(ref versionType);
            rename.results = results;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                rename.Execute(i);
        }
    }

    [BurstCompile]
    private struct Apply : IJob
    {
        public NativeQueue<Result> results;

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

    private EntityQuery __group;
    private NativeQueue<Result> __results;

    protected override void OnCreate()
    {
        base.OnCreate();

        __group = GetEntityQuery(
            ComponentType.ReadOnly<GameValhallaRenameCommand>(),
            ComponentType.ReadWrite<GameValhallaVersion>());

        __group.SetChangedVersionFilter(typeof(GameValhallaRenameCommand));

        __results = new NativeQueue<Result>(Allocator.Persistent);
    }

    protected override void OnDestroy()
    {
        __results.Dispose();

        base.OnDestroy();
    }

    protected override void OnUpdate()
    {
        var souls = GetBufferLookup<GameSoul>();

        RenameEx rename;
        rename.souls = souls;
        rename.commandType = GetComponentTypeHandle<GameValhallaRenameCommand>(true);
        rename.versionType = GetComponentTypeHandle<GameValhallaVersion>();
        rename.results = __results.AsParallelWriter();
        var jobHandle = rename.ScheduleParallel(__group, Dependency);

        Apply apply;
        apply.results = __results;
        apply.souls = souls;
        Dependency = apply.Schedule(jobHandle);
    }
}

public partial class GameValhallaDestroySystem : SystemBase
{
    [Serializable]
    public struct DestroyLevelExp
    {
        public float value;
        public float factor;
    }

    private struct Result
    {
        public int soulIndex;
        public Entity entity;
    }

    private struct Destroy
    {
        [ReadOnly]
        public BufferLookup<GameSoul> souls;
        [ReadOnly]
        public NativeArray<DestroyLevelExp> destroyLevelExps;
        [ReadOnly]
        public NativeArray<GameValhallaDestroyCommand> commands;
        public NativeArray<GameValhallaVersion> versions;
        public NativeArray<GameValhallaExp> exps;

        public NativeQueue<Result>.ParallelWriter results;

        public void Execute(int index)
        {
            var command = commands[index];
            if (command.soulIndex < 0)
                return;

            var version = versions[index];
            if (command.version != version.value)
                return;

            ++version.value;
            versions[index] = version;

            if (!this.souls.HasBuffer(command.entity))
                return;

            var souls = this.souls[command.entity];
            int soulIndex = GameSoul.IndexOf(command.soulIndex, souls);
            if (soulIndex == -1)
                return;

            var soul = souls[soulIndex];
            var destroyLevelExp = destroyLevelExps[soul.data.levelIndex];

            var exp = exps[index];
            exp.value += destroyLevelExp.value + destroyLevelExp.factor * soul.data.exp;
            exps[index] = exp;

            Result result;
            result.soulIndex = command.soulIndex;
            result.entity = command.entity;
            results.Enqueue(result);
        }
    }

    [BurstCompile]
    private struct DestroyEx : IJobChunk
    {
        [ReadOnly]
        public BufferLookup<GameSoul> souls;
        [ReadOnly]
        public NativeArray<DestroyLevelExp> destroyLevelExps;
        [ReadOnly]
        public ComponentTypeHandle<GameValhallaDestroyCommand> commandType;
        public ComponentTypeHandle<GameValhallaVersion> versionType;
        public ComponentTypeHandle<GameValhallaExp> expType;

        public NativeQueue<Result>.ParallelWriter results;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Destroy destroy;
            destroy.souls = souls;
            destroy.destroyLevelExps = destroyLevelExps;
            destroy.commands = chunk.GetNativeArray(ref commandType);
            destroy.versions = chunk.GetNativeArray(ref versionType);
            destroy.exps = chunk.GetNativeArray(ref expType);
            destroy.results = results;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                destroy.Execute(i);
        }
    }

    [BurstCompile]
    private struct Apply : IJob
    {
        public NativeQueue<Result> results;

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

    private EntityQuery __group;
    private NativeArray<DestroyLevelExp> __destroyLevelExps;
    private NativeQueue<Result> __results;

    public void Create(DestroyLevelExp[] destroyLevelExps)
    {
        __destroyLevelExps = new NativeArray<DestroyLevelExp>(destroyLevelExps, Allocator.Persistent);
    }

    protected override void OnCreate()
    {
        base.OnCreate();

        __group = GetEntityQuery(
            ComponentType.ReadOnly<GameValhallaDestroyCommand>(),
            ComponentType.ReadWrite<GameValhallaVersion>(),
            ComponentType.ReadWrite<GameValhallaExp>());

        __group.SetChangedVersionFilter(typeof(GameValhallaDestroyCommand));

        __results = new NativeQueue<Result>(Allocator.Persistent);
    }

    protected override void OnDestroy()
    {
        if (__destroyLevelExps.IsCreated)
            __destroyLevelExps.Dispose();

        __results.Dispose();

        base.OnDestroy();
    }

    protected override void OnUpdate()
    {
        if (!__destroyLevelExps.IsCreated)
            return;

        var souls = GetBufferLookup<GameSoul>();

        DestroyEx destroy;
        destroy.souls = souls;
        destroy.destroyLevelExps = __destroyLevelExps;
        destroy.commandType = GetComponentTypeHandle<GameValhallaDestroyCommand>(true);
        destroy.versionType = GetComponentTypeHandle<GameValhallaVersion>();
        destroy.expType = GetComponentTypeHandle<GameValhallaExp>();
        destroy.results = __results.AsParallelWriter();
        var jobHandle = destroy.ScheduleParallel(__group, Dependency);

        Apply apply;
        apply.results = __results;
        apply.souls = souls;
        Dependency = apply.Schedule(jobHandle);
    }
}

public partial class GameValhallaEvolutionSystem : SystemBase
{
    [Serializable]
    public struct Result
    {
        public int soulIndex;
        public int nextIndex;
        public Entity valhalla;
        public Entity master;
    }

    private struct Evolute
    {
        public Random random;

        [ReadOnly]
        public GameSoulManager manager;

        [ReadOnly]
        public BufferLookup<GameSoul> souls;

        [ReadOnly]
        public BufferAccessor<GameValhallaSacrificer> sacrificers;

        [ReadOnly]
        public NativeArray<float> maxExps;

        [ReadOnly]
        public NativeArray<Entity> entityArray;

        [ReadOnly]
        public NativeArray<GameValhallaEvoluteCommand> commands;
        public NativeArray<GameValhallaVersion> versions;

        public NativeQueue<Result>.ParallelWriter results;

        public void Execute(int index)
        {
            var command = commands[index];
            var version = versions[index];
            if (version.value != command.version)
                return;

            ++version.value;
            versions[index] = version;

            if (!this.souls.HasBuffer(command.entity))
                return;

            var souls = this.souls[command.entity];
            int soulIndex = GameSoul.IndexOf(command.soulIndex, souls);
            if (soulIndex == -1)
                return;

            var soul = souls[soulIndex];
            if ((int)math.round(soul.data.exp) < (int)math.floor(maxExps[soul.data.levelIndex]))
                return;

            int nextIndex = manager.FindNextIndex(
                soulIndex,
                sacrificers[index].Reinterpret<int>().AsNativeArray(),
                souls, 
                ref random);

            if (nextIndex == -1)
                return;

            Result result;
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
        public double time;

        [ReadOnly]
        public GameSoulManager manager;

        [ReadOnly]
        public NativeArray<float> maxExps;

        [ReadOnly]
        public BufferLookup<GameSoul> souls;

        [ReadOnly]
        public BufferTypeHandle<GameValhallaSacrificer> sacrificerType;

        [ReadOnly]
        public EntityTypeHandle entityType;

        [ReadOnly]
        public ComponentTypeHandle<GameValhallaEvoluteCommand> commandType;
        public ComponentTypeHandle<GameValhallaVersion> versionType;

        public NativeQueue<Result>.ParallelWriter results;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            long hash = math.aslong(time);

            Evolute evolute;
            evolute.random = new Random((uint)hash ^ (uint)(hash >> 32) ^ (uint)unfilteredChunkIndex);
            evolute.manager = manager;
            evolute.maxExps = maxExps;
            evolute.souls = souls;
            evolute.sacrificers = chunk.GetBufferAccessor(ref sacrificerType);
            evolute.entityArray = chunk.GetNativeArray(entityType);
            evolute.commands = chunk.GetNativeArray(ref commandType);
            evolute.versions = chunk.GetNativeArray(ref versionType);
            evolute.results = results;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                evolute.Execute(i);
        }
    }

    [BurstCompile]
    private struct Apply : IJob
    {
        [ReadOnly]
        public GameSoulManager manager;

        [ReadOnly]
        public BufferLookup<GameValhallaSacrificer> sacrificers;

        public BufferLookup<GameSoul> souls;

        public NativeQueue<Result> inputs;

        public NativeList<Result> outputs;

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

    private JobHandle __jobHandle;
    private EntityQuery __group;
    private GameSoulSystem __soulSystem;
    private GameValhallaUpgradeSystem __upgradeSystem;
    private NativeQueue<Result> __inputs;
    private NativeList<Result> __outputs;

    public NativeArray<Result> results
    {
        get
        {
            __jobHandle.Complete();
            __jobHandle = default;

            return __outputs.AsArray();
        }
    }

    protected override void OnCreate()
    {
        base.OnCreate();

        __group = GetEntityQuery(
            ComponentType.ReadOnly<GameValhallaSacrificer>(), 
            ComponentType.ReadOnly<GameValhallaEvoluteCommand>(),
            ComponentType.ReadWrite<GameValhallaVersion>());

        __group.SetChangedVersionFilter(typeof(GameValhallaEvoluteCommand));

        var world = World;
        __soulSystem = world.GetOrCreateSystemManaged<GameSoulSystem>();
        __upgradeSystem = world.GetOrCreateSystemManaged<GameValhallaUpgradeSystem>();

        __inputs = new NativeQueue<Result>(Allocator.Persistent);
        __outputs = new NativeList<Result>(Allocator.Persistent);
    }

    protected override void OnDestroy()
    {
        __inputs.Dispose();

        __outputs.Dispose();

        base.OnDestroy();
    }

    protected override void OnUpdate()
    {
        var maxExps = __upgradeSystem.maxExps;
        if (!maxExps.IsCreated)
            return;

        var manager = __soulSystem.manager;
        if (!manager.isCreated)
            return;

        var souls = GetBufferLookup<GameSoul>();

        EvoluteEx evolute;
        evolute.time = World.Time.ElapsedTime;
        evolute.manager = manager;
        evolute.maxExps = maxExps;
        evolute.souls = souls;
        evolute.sacrificerType = GetBufferTypeHandle<GameValhallaSacrificer>(true);
        evolute.entityType = GetEntityTypeHandle();
        evolute.commandType = GetComponentTypeHandle<GameValhallaEvoluteCommand>(true);
        evolute.versionType = GetComponentTypeHandle<GameValhallaVersion>();
        evolute.results = __inputs.AsParallelWriter();

        __jobHandle = evolute.ScheduleParallel(__group, Dependency);

        Apply apply;
        apply.manager = manager;
        apply.sacrificers = GetBufferLookup<GameValhallaSacrificer>(true);
        apply.inputs = __inputs;
        apply.souls = souls;
        apply.outputs = __outputs;

        __jobHandle = apply.Schedule(__jobHandle);

        Dependency = __jobHandle;
    }
}

[AlwaysUpdateSystem]
public partial class GameValhallaRespawnSystem : SystemBase
{
    public struct Command
    {
        public Entity entity;
        public RigidTransform transform;
        public GameSoulData value;
    }

    [Serializable]
    public struct RespawnLevelExp
    {
        public float value;
        public float factor;
    }

    [BurstCompile]
    public struct ClearSoulIndicess : IJob
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

        [ReadOnly]
        public BufferLookup<GameSoul> souls;
        [ReadOnly]
        public NativeArray<RespawnLevelExp> respawnLevelExps;
        [ReadOnly]
        public NativeArray<Translation> translations;
        [ReadOnly]
        public NativeArray<Rotation> rotations;
        [ReadOnly]
        public NativeArray<GameValhallaData> instances;
        [ReadOnly]
        public NativeArray<GameValhallaRespawnCommand> commands;
        public NativeArray<GameValhallaVersion> versions;
        public NativeArray<GameValhallaExp> exps;

        public NativeParallelHashMap<Entity, int>.ParallelWriter soulIndicesToRemove;

        public NativeQueue<TimeEvent<Command>>.ParallelWriter results;
        //public EntityCommandQueue<GameValhallaCommand>.ParallelWriter entityManager;

        public void Execute(int index)
        {
            var command = commands[index];
            if (command.soulIndex < 0)
                return;

            var version = versions[index];
            if (command.version != version.value)
                return;

            ++version.value;
            versions[index] = version;

            if (!this.souls.HasBuffer(command.entity))
                return;

            var souls = this.souls[command.entity];
            int soulIndex = GameSoul.IndexOf(command.soulIndex, souls);
            if (soulIndex == -1)
                return;

            var soul = souls[soulIndex];
            var respawnLevelExp = respawnLevelExps[soul.data.levelIndex];

            float expCost = respawnLevelExp.value + respawnLevelExp.factor * soul.data.exp;
            var exp = exps[index];
            if (exp.value < expCost)
                return;

            if (!soulIndicesToRemove.TryAdd(command.entity, command.soulIndex))
                return;

            exp.value -= expCost;
            exps[index] = exp;

            var instance = instances[index];

            TimeEvent<Command> result;
            result.time = time + instance.respawnTime;
            result.value.entity = command.entity;
            result.value.transform = math.RigidTransform(rotations[index].Value, translations[index].Value);
            result.value.transform.pos = math.transform(result.value.transform, instance.respawnOffset);
            result.value.value = soul.data;
            results.Enqueue(result);
        }
    }

    [BurstCompile]
    private struct RespawnEx : IJobChunk
    {
        public double time;
        [ReadOnly]
        public BufferLookup<GameSoul> souls;
        [ReadOnly]
        public NativeArray<RespawnLevelExp> respawnLevelExps;
        [ReadOnly]
        public ComponentTypeHandle<Translation> translationType;
        [ReadOnly]
        public ComponentTypeHandle<Rotation> rotationType;
        [ReadOnly]
        public ComponentTypeHandle<GameValhallaData> instanceType;
        [ReadOnly]
        public ComponentTypeHandle<GameValhallaRespawnCommand> commandType;
        public ComponentTypeHandle<GameValhallaVersion> versionType;
        public ComponentTypeHandle<GameValhallaExp> expType;

        public NativeParallelHashMap<Entity, int>.ParallelWriter soulIndicesToRemove;
        public NativeQueue<TimeEvent<Command>>.ParallelWriter results;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Respawn respawn;
            respawn.time = time;
            respawn.souls = souls;
            respawn.respawnLevelExps = respawnLevelExps;
            respawn.translations = chunk.GetNativeArray(ref translationType);
            respawn.rotations = chunk.GetNativeArray(ref rotationType);
            respawn.instances = chunk.GetNativeArray(ref instanceType);
            respawn.commands = chunk.GetNativeArray(ref commandType);
            respawn.versions = chunk.GetNativeArray(ref versionType);
            respawn.exps = chunk.GetNativeArray(ref expType);
            respawn.soulIndicesToRemove = soulIndicesToRemove;
            respawn.results = results;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                respawn.Execute(i);
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
    private struct Apply : IJob, IEntityCommandProducerJob
    {
        public int itemIdentityType;
        public EntityArchetype itemEntityArchetype;
        public Random random;

        [ReadOnly]
        public NativeArray<Command> commands;

        [ReadOnly]
        public NativeParallelHashMap<int, int> itemTypes;

        public GameItemManager itemManager;

        public EntityCommandQueue<GameValhallaCommand>.Writer entityManager;

        //public EntityCommandQueue<GameValhallaCommand>.ParallelWriter entityManager;
        public SharedHashMap<GameItemHandle, EntityArchetype>.Writer createItemCommander;

        public EntityComponentAssigner.Writer assigner;

        public void Execute(int index)
        {
            var command = commands[index];

            int count = 1;
            var handle = itemManager.Add(itemTypes[command.value.type], ref count);

            GameValhallaCommand result;
            result.variant = command.value.variant;
            result.type = command.value.type;
            result.entity = command.entity;
            result.transform = command.transform;
            result.nickname = new FixedString32Bytes(command.value.nickname);
            result.itemHandle = handle;

            entityManager.Enqueue(result);

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

    private EntityQuery __structChangeManagerGroup;
    private EntityQuery __identityTypeGroup;
    private EntityQuery __group;
    private NativeArray<RespawnLevelExp> __respawnLevelExps;
    private NativeParallelHashMap<int, int> __itemTypes;
    private NativeParallelHashMap<Entity, int> __soulIndicess;
    private NativeQueue<TimeEvent<Command>> __commands;
    private TimeManager<Command> __timeManager;
    private EntityCommandPool<GameValhallaCommand> __entityManager;
    private GameItemManagerShared __itemManager;

    public EntityArchetype itemEntityArchetype
    {
        get;

        private set;
    }

    public void Create(RespawnLevelExp[] respawnLevelExps, KeyValuePair<int, int>[] itemTypes, GameValhallaCommander commander)
    {
        __respawnLevelExps = new NativeArray<RespawnLevelExp>(respawnLevelExps, Allocator.Persistent);

        __itemTypes = new NativeParallelHashMap<int, int>(itemTypes.Length, Allocator.Persistent);
        foreach (var pair in itemTypes)
            __itemTypes.Add(pair.Key, pair.Value);

        __entityManager = World.GetOrCreateSystemManaged<EndFrameEntityCommandSystem>().Create<GameValhallaCommand, GameValhallaCommander>(EntityCommandManager.QUEUE_PRESENT, commander);
    }

    protected override void OnCreate()
    {
        base.OnCreate();

        __structChangeManagerGroup = GameItemStructChangeManager.GetEntityQuery(ref this.GetState());

        __identityTypeGroup = GetEntityQuery(ComponentType.ReadOnly<GameItemIdentityType>());

        __group = GetEntityQuery(
            ComponentType.ReadOnly<GameValhallaRespawnCommand>(),
            ComponentType.ReadWrite<GameValhallaVersion>(),
            ComponentType.ReadWrite<GameValhallaExp>());

        __group.SetChangedVersionFilter(typeof(GameValhallaRespawnCommand));

        __soulIndicess = new NativeParallelHashMap<Entity, int>(1, Allocator.Persistent);

        __commands = new NativeQueue<TimeEvent<Command>>(Allocator.Persistent);

        __timeManager = new TimeManager<Command>(Allocator.Persistent);

        ref var itemSystem = ref World.GetOrCreateSystemUnmanaged<GameItemSystem>();

        __itemManager = itemSystem.manager;

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

            itemEntityArchetype = EntityManager.CreateArchetype(results.AsArray().ToArray());

            results.Dispose();
        }
    }

    protected override void OnDestroy()
    {
        if (__respawnLevelExps.IsCreated)
            __respawnLevelExps.Dispose();

        if (__itemTypes.IsCreated)
            __itemTypes.Dispose();

        __soulIndicess.Dispose();

        __timeManager.Dispose();

        __commands.Dispose();

        base.OnDestroy();
    }

    protected override void OnUpdate()
    {
        if (!__respawnLevelExps.IsCreated || !__identityTypeGroup.HasSingleton<GameItemIdentityType>())
            return;

        var entityCount = new NativeArray<int>(1, Allocator.TempJob, NativeArrayOptions.ClearMemory);

        var jobHandle = __group.CalculateEntityCountAsync(entityCount, Dependency);

        ClearSoulIndicess clearSoulIndicess;
        clearSoulIndicess.capacity = entityCount;
        clearSoulIndicess.soulIndicess = __soulIndicess;
        jobHandle = clearSoulIndicess.Schedule(jobHandle);

        double time = World.Time.ElapsedTime;

        RespawnEx respawn;
        respawn.time = time;
        respawn.souls = GetBufferLookup<GameSoul>(true);
        respawn.respawnLevelExps = __respawnLevelExps;
        respawn.translationType = GetComponentTypeHandle<Translation>(true);
        respawn.rotationType = GetComponentTypeHandle<Rotation>(true);
        respawn.instanceType = GetComponentTypeHandle<GameValhallaData>(true);
        respawn.commandType = GetComponentTypeHandle<GameValhallaRespawnCommand>(true);
        respawn.versionType = GetComponentTypeHandle<GameValhallaVersion>();
        respawn.expType = GetComponentTypeHandle<GameValhallaExp>();
        respawn.soulIndicesToRemove = __soulIndicess.AsParallelWriter();
        respawn.results = __commands.AsParallelWriter();
        jobHandle = respawn.ScheduleParallel(__group, jobHandle);

        Add add;
        add.inputs = __commands;
        add.outputs = __timeManager.writer;
        var timeJobHandle = add.Schedule(jobHandle);

        __timeManager.Flush();

        timeJobHandle = __timeManager.Schedule(time, timeJobHandle);

        var itemStructChangeManager = __structChangeManagerGroup.GetSingleton<GameItemStructChangeManager>();
        var createItemCommander = itemStructChangeManager.createEntityCommander;
        var itemAssigner = itemStructChangeManager.assigner;

        var entityManager = __entityManager.Create();

        long hash = math.aslong(time);

        Apply apply;
        apply.itemIdentityType = __identityTypeGroup.GetSingleton<GameItemIdentityType>().value;
        apply.itemEntityArchetype = itemEntityArchetype;
        apply.random = new Random(math.max(1, (uint)hash & (uint)(hash >> 32)));
        apply.commands = __timeManager.values;
        apply.itemTypes = __itemTypes;
        apply.itemManager = __itemManager.value;
        apply.entityManager = entityManager.writer;
        apply.createItemCommander = createItemCommander.writer;
        apply.assigner = itemAssigner.writer;

        ref var itemJobManager = ref __itemManager.lookupJobManager;
        ref var commanderJobManager = ref createItemCommander.lookupJobManager;

        timeJobHandle = JobHandle.CombineDependencies(timeJobHandle, itemJobManager.readWriteJobHandle, commanderJobManager.readWriteJobHandle);

        timeJobHandle = apply.Schedule(JobHandle.CombineDependencies(timeJobHandle, itemAssigner.jobHandle));

        itemAssigner.jobHandle = timeJobHandle;

        itemJobManager.readWriteJobHandle = timeJobHandle;

        commanderJobManager.readWriteJobHandle = timeJobHandle;

        entityManager.AddJobHandleForProducer<Apply>(timeJobHandle);

        //timeJobHandle = _values.Clear(timeJobHandle);

        Remove remove;
        remove.soulIndices = __soulIndicess;
        remove.souls = GetBufferLookup<GameSoul>();
        jobHandle = remove.Schedule(jobHandle);

        Dependency = JobHandle.CombineDependencies(jobHandle, timeJobHandle);
    }
}