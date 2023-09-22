using System;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using ZG;
using Random = Unity.Mathematics.Random;

public struct GameSoulData
{
    public int type;
    public int variant;
    public int levelIndex;
    public float power;
    public float exp;
    //public Entity owner;
    public FixedString32Bytes nickname;
}

public struct GameSoulIndex : IComponentData
{
    public int value;
}

public struct GameSoulDislike : IBufferElementData
{
    public int index;

    public static int IndexOf(int soulIndex, in DynamicBuffer<GameSoulDislike> souls)
    {
        int numSouls = souls.Length;
        for (int i = 0; i < numSouls; ++i)
        {
            if (souls[i].index == soulIndex)
                return i;
        }

        return -1;
    }
}

public struct GameSoul : IBufferElementData
{
    public int index;
    public long ticks;
    public GameSoulData data;

    public static int IndexOf(int soulIndex, in DynamicBuffer<GameSoul> souls)
    {
        int numSouls = souls.Length;
        for(int i = 0; i < numSouls; ++i)
        {
            if (souls[i].index == soulIndex)
                return i;
        }

        return -1;
    }
}

/*[Serializable]
public struct GameSoulVersion :IComponentData
{
    public int value;
}

[Serializable]
public struct GameSoulUpgradeCommand : IComponentData
{
    public int version;
    public int soulIndex;
    public int nextLevelIndex;
}

[Serializable]
public struct GameSoulSacrificer : IBufferElementData
{
    public int index;
}*/

public struct GameSoulManager : IDisposable
{
    [Serializable]
    public struct Sacrificer
    {
        public int levelIndex;
    }

    [Serializable]
    public struct Next
    {
        public int levelIndex;
        public float chance;
        public float power;
    }

    [Serializable]
    public struct Level
    {
        public int type;

        //public float power;

        public float maxExp;

        public float stageExpFactor;

        public int sacrificerStartIndex;
        public int sacrificerCount;

        public int nextStartIndex;
        public int nextCount;
    }

    [Serializable]
    public struct LevelData
    {
        public int type;

        //public float power;

        public float maxExp;

        public float stageExpFactor;

        public Sacrificer[] sacrificers;

        public Next[] nexts;
    }

    private NativeList<Sacrificer> __sacrificers;
    private NativeList<Next> __nexts;
    private NativeList<Level> __levels;

    public GameSoulManager(in AllocatorManager.AllocatorHandle allocator)
    {
        __sacrificers = new NativeList<Sacrificer>(allocator);
        __nexts = new NativeList<Next>(allocator);
        __levels = new NativeList<Level>(allocator);
    }

    public void Init(LevelData[] levels)
    {
        int i, count, numSacrificers = 0, numNexts = 0, numLevels = levels.Length;
        LevelData source;
        Level destination;
        __levels.ResizeUninitialized(numLevels);
        for(i = 0; i < numLevels; ++i)
        {
            source = levels[i];

            destination.type = source.type;
            //destination.power = source.power;
            destination.maxExp = source.maxExp;
            destination.stageExpFactor = source.stageExpFactor;

            destination.sacrificerStartIndex = numSacrificers;
            destination.sacrificerCount = source.sacrificers == null ? 0 : source.sacrificers.Length;

            numSacrificers += destination.sacrificerCount;

            destination.nextStartIndex = numNexts;
            destination.nextCount = source.nexts == null ? 0 : source.nexts.Length;

            numNexts += destination.nextCount;

            __levels[i] = destination;
        }

        __nexts.ResizeUninitialized(numNexts);
        __sacrificers.ResizeUninitialized(numSacrificers);
        numNexts = 0;
        numSacrificers = 0;
        for(i = 0; i < numLevels; ++i)
        {
            source = levels[i];

            count = source.nexts == null ? 0 : source.nexts.Length;

            if(count > 0)
                NativeArray<Next>.Copy(source.nexts, 0, __nexts.AsArray(), numNexts, count);

            numNexts += count;

            count = source.sacrificers == null ? 0 : source.sacrificers.Length;

            if(count > 0)
                NativeArray<Sacrificer>.Copy(source.sacrificers, 0, __sacrificers.AsArray(), numSacrificers, count);

            numSacrificers += count;
        }
    }

    public void Dispose()
    {
        __sacrificers.Dispose();
        __nexts.Dispose();
        __levels.Dispose();
    }

    public static float GetStageExp(float stage, float stageExpFactor) => math.exp(stage) * stageExpFactor;

    public static float GetStage(float exp, float stageExpFactor) => math.log(math.max(exp / stageExpFactor, 1.0f));

    public static float GetStagePower(float power, float stage) => power > 0.0f ? math.pow(stage, power) : 0.0f;

    public static float GetStagePower(float power, float exp, float stageExpFactor) => GetStagePower(power, GetStage(exp, stageExpFactor));

    public int FindNextIndex(
        int soulIndex,
        in NativeArray<int> sacrificerIndices,
        in DynamicBuffer<GameSoul> souls,
        ref Random random)
    {
        /*soulIndex = GameSoul.IndexOf(soulIndex, souls);
        if (soulIndex == -1)
            return false;*/

        var soul = souls[soulIndex];
        var level = __levels[soul.data.levelIndex];

        int numSacrificerIndices = sacrificerIndices.Length;
        if (numSacrificerIndices != level.sacrificerCount)
            return -1;

        int i, j, sacrificeIndex;
        Sacrificer sacrificer;
        GameSoul temp;
        for (i = 0; i < level.sacrificerCount; ++i)
        {
            sacrificer = __sacrificers[level.sacrificerStartIndex + i];
            for (j = 0; j < numSacrificerIndices; ++j)
            {
                sacrificeIndex = GameSoul.IndexOf(sacrificerIndices[j], souls);
                if (sacrificeIndex == -1)
                    return -1;

                temp = souls[sacrificeIndex];
                if (temp.data.levelIndex == sacrificer.levelIndex)
                    break;
            }

            if (j == numSacrificerIndices)
                return -1;
        }

        float randomValue = random.NextFloat(), chance;
        for (i = 0; i < level.nextCount; ++i)
        {
            chance = __nexts[level.nextStartIndex + i].chance;
            if (chance > randomValue)
                return i;
            else
                randomValue -= chance;
        }

        return -1;
    }

    public void Apply(
        int nextIndex, 
        int soulIndex,
        in NativeArray<int> sacrificerIndices, 
        ref DynamicBuffer<GameSoul> souls)
    {
        var soul = souls[soulIndex];
        var level = __levels[soul.data.levelIndex];
        var next = __nexts[level.nextStartIndex + nextIndex];
        soul.data.type = __levels[next.levelIndex].type;
        soul.data.levelIndex = next.levelIndex;
        soul.data.power += next.power * soul.data.exp / level.maxExp;
        soul.data.exp = 0.0f;
        souls[soulIndex] = soul;

        for (int i = 0; i < sacrificerIndices.Length; ++i)
            souls.RemoveAt(GameSoul.IndexOf(sacrificerIndices[i], souls));
    }
}

//[UpdateInGroup(typeof(GameSyncSystemGroup)), UpdateBefore(typeof(GameStatusSystemGroup))]

[BurstCompile, UpdateInGroup(typeof(GameItemInitSystemGroup)), UpdateBefore(typeof(GameItemComponentInitSystemGroup))]
public partial struct GameSoulSystem : ISystem
{
    private struct Convert
    {
        [ReadOnly]
        public NativeArray<GameItemObjectData> instances;
        [ReadOnly]
        public NativeArray<GameItemName> names;
        [ReadOnly]
        public NativeArray<GameItemVariant> variants;
        [ReadOnly]
        public NativeArray<GameItemOwner> owners;
        [ReadOnly]
        public NativeArray<GameItemLevel> levels;
        [ReadOnly]
        public NativeArray<GameItemExp> exps;
        [ReadOnly]
        public NativeArray<GameItemPower> powers;

        [ReadOnly]
        public BufferLookup<GameSoul> souls;

        public NativeQueue<EntityData<GameSoulData>>.ParallelWriter results;

        public void Execute(int index)
        {
            int levelHandle = levels[index].handle;
            if (levelHandle == 0)
                return;

            EntityData<GameSoulData> result;
            result.entity = owners[index].entity;
            if (!souls.HasBuffer(result.entity))
                return;

            result.value.type = instances[index].type;
            result.value.nickname = names[index].value;
            result.value.variant = variants[index].value;
            result.value.levelIndex = levelHandle - 1;
            result.value.power = powers[index].value;
            result.value.exp = exps[index].value;
            results.Enqueue(result);
        }
    }

    [BurstCompile]
    private struct ConvertEx : IJobChunk
    {
        [ReadOnly]
        public ComponentTypeHandle<GameItemObjectData> instanceType;
        [ReadOnly]
        public ComponentTypeHandle<GameItemName> nameType;
        [ReadOnly]
        public ComponentTypeHandle<GameItemVariant> variantType;
        [ReadOnly]
        public ComponentTypeHandle<GameItemOwner> ownerType;
        [ReadOnly]
        public ComponentTypeHandle<GameItemLevel> levelType;
        [ReadOnly]
        public ComponentTypeHandle<GameItemExp> expType;
        [ReadOnly]
        public ComponentTypeHandle<GameItemPower> powerType;

        [ReadOnly]
        public BufferLookup<GameSoul> souls;

        public NativeQueue<EntityData<GameSoulData>>.ParallelWriter results;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Convert convert;
            convert.instances = chunk.GetNativeArray(ref instanceType);
            convert.names = chunk.GetNativeArray(ref nameType);
            convert.variants = chunk.GetNativeArray(ref variantType);
            convert.owners = chunk.GetNativeArray(ref ownerType);
            convert.levels = chunk.GetNativeArray(ref levelType);
            convert.exps = chunk.GetNativeArray(ref expType);
            convert.powers = chunk.GetNativeArray(ref powerType);
            convert.souls = souls;
            convert.results = results;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                convert.Execute(i);
        }
    }
    /*
    private struct Die
    {
        [ReadOnly]
        public NativeArray<EntityDataIdentity> identities;
        [ReadOnly]
        public NativeArray<GameVariant> variants;
        [ReadOnly]
        public NativeArray<GameLevel> levels;
        [ReadOnly]
        public NativeArray<GameExp> exps;
        [ReadOnly]
        public NativeArray<GamePower> powers;
        [ReadOnly]
        public NativeArray<GameNickname> nicknames;
        [ReadOnly]
        public NativeArray<GameOwner> owners;

        [ReadOnly]
        public NativeArray<GameNodeStatus> states;
        [ReadOnly]
        public NativeArray<GameNodeOldStatus> oldStates;

        [ReadOnly]
        public BufferLookup<GameSoul> souls;

        public NativeQueue<EntityData<GameSoulData>>.ParallelWriter results;

        public void Execute(int index)
        {
            int status = states[index].value;
            if (status != (int)GameEntityStatus.Dead || status == oldStates[index].value)
                return;

            int levelHandle = levels[index].handle;
            if (levelHandle == 0)
                return;

            EntityData<GameSoulData> result;
            result.entity = owners[index].entity;
            if (!souls.HasComponent(result.entity))
                return;

            result.value.type = identities[index].type;
            result.value.variant = variants[index].value;
            result.value.levelIndex = levelHandle - 1;
            result.value.power = powers[index].value;
            result.value.exp = exps[index].value;
            result.value.nickname = nicknames[index].value;
            results.Enqueue(result);
        }
    }

    [BurstCompile]
    private struct DieEx : IJobChunk
    {
        [ReadOnly]
        public ComponentTypeHandle<EntityDataIdentity> identityType;
        [ReadOnly]
        public ComponentTypeHandle<GameVariant> variantType;
        [ReadOnly]
        public ComponentTypeHandle<GameLevel> levelType;
        [ReadOnly]
        public ComponentTypeHandle<GameExp> expType;
        [ReadOnly]
        public ComponentTypeHandle<GamePower> powerType;
        [ReadOnly]
        public ComponentTypeHandle<GameNickname> nicknameType;
        [ReadOnly]
        public ComponentTypeHandle<GameOwner> ownerType;

        [ReadOnly]
        public ComponentTypeHandle<GameNodeStatus> statusType;
        [ReadOnly]
        public ComponentTypeHandle<GameNodeOldStatus> oldStatusType;

        [ReadOnly]
        public BufferLookup<GameSoul> souls;

        public NativeQueue<EntityData<GameSoulData>>.ParallelWriter results;

        public void Execute(ArchetypeChunk chunk, int chunkIndex, int firstEntityIndex)
        {
            Die die;
            die.identities = chunk.GetNativeArray(identityType);
            die.variants = chunk.GetNativeArray(variantType);
            die.levels = chunk.GetNativeArray(levelType);
            die.exps = chunk.GetNativeArray(expType);
            die.powers = chunk.GetNativeArray(powerType);
            die.nicknames = chunk.GetNativeArray(nicknameType);
            die.owners = chunk.GetNativeArray(ownerType);
            die.states = chunk.GetNativeArray(statusType);
            die.oldStates = chunk.GetNativeArray(oldStatusType);
            die.souls = souls;
            die.results = results;

            int count = chunk.Count;
            for (int i = 0; i < count; ++i)
                die.Execute(i);
        }
    }
    */

    [BurstCompile]
    private struct Collect : IJob
    {
        public long ticks;

        public NativeQueue<EntityData<GameSoulData>> inputs;

        public BufferLookup<GameSoul> outputs;

        public ComponentLookup<GameSoulIndex> indices;

        public void Execute()
        {
            GameSoul output;
            GameSoulIndex index;
            while (inputs.TryDequeue(out var input))
            {
                index = indices[input.entity];
                output.index = index.value++;
                output.ticks = ticks;
                output.data = input.value;

                outputs[input.entity].Add(output);
                indices[input.entity] = index;
            }
        }
    }

    private long __ticks;
    private EntityQuery __group;

    private ComponentTypeHandle<GameItemObjectData> __instanceType;
    private ComponentTypeHandle<GameItemName> __nameType;
    private ComponentTypeHandle<GameItemVariant> __variantType;
    private ComponentTypeHandle<GameItemOwner> __ownerType;
    private ComponentTypeHandle<GameItemLevel> __levelType;
    private ComponentTypeHandle<GameItemExp> __expType;
    private ComponentTypeHandle<GameItemPower> __powerType;

    private ComponentLookup<GameSoulIndex> __indices;

    private BufferLookup<GameSoul> __souls;

    private NativeQueue<EntityData<GameSoulData>> __results;

    public GameSoulManager manager
    {
        get;

        private set;
    }

    public void Create(long ticks, GameSoulManager.LevelData[] levels)
    {
        __ticks = ticks;

        manager.Init(levels);
    }

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __group = builder
                    .WithAll<GameItemObjectData>()
                    .WithAny<GameItemName, GameItemVariant, GameItemOwner, GameItemLevel, GameItemExp, GameItemPower>()
                    .WithNone<GameItemData>()
                    .Build(ref state);

        __instanceType = state.GetComponentTypeHandle<GameItemObjectData>(true);
        __nameType = state.GetComponentTypeHandle<GameItemName>(true);
        __variantType = state.GetComponentTypeHandle<GameItemVariant>(true);
        __ownerType = state.GetComponentTypeHandle<GameItemOwner>(true);
        __levelType = state.GetComponentTypeHandle<GameItemLevel>(true);
        __expType = state.GetComponentTypeHandle<GameItemExp>(true);
        __powerType = state.GetComponentTypeHandle<GameItemPower>(true);
        __indices = state.GetComponentLookup<GameSoulIndex>();
        __souls = state.GetBufferLookup<GameSoul>();

        __results = new NativeQueue<EntityData<GameSoulData>>(Allocator.Persistent);


        manager = new GameSoulManager(Allocator.Persistent);
    }

    //[BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        __results.Dispose();

        manager.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        /*DieEx die;
        die.identityType = GetComponentTypeHandle<EntityDataIdentity>(true);
        die.variantType = GetComponentTypeHandle<GameVariant>(true);
        die.levelType = GetComponentTypeHandle<GameLevel>(true);
        die.expType = GetComponentTypeHandle<GameExp>(true);
        die.powerType = GetComponentTypeHandle<GamePower>(true);
        die.nicknameType = GetComponentTypeHandle<GameNickname>(true);
        die.ownerType = GetComponentTypeHandle<GameOwner>(true);
        die.statusType = GetComponentTypeHandle<GameNodeStatus>(true);
        die.oldStatusType = GetComponentTypeHandle<GameNodeOldStatus>(true);
        die.souls = GetBufferLookup<GameSoul>(true);
        die.results = __results.AsParallelWriter();*/
        ConvertEx convert;
        convert.instanceType = __instanceType.UpdateAsRef(ref state);
        convert.nameType = __nameType.UpdateAsRef(ref state);
        convert.variantType = __variantType.UpdateAsRef(ref state);
        convert.ownerType = __ownerType.UpdateAsRef(ref state);
        convert.levelType = __levelType.UpdateAsRef(ref state);
        convert.expType = __expType.UpdateAsRef(ref state);
        convert.powerType = __powerType.UpdateAsRef(ref state);
        convert.souls = __souls.UpdateAsRef(ref state);
        convert.results = __results.AsParallelWriter();
        var jobHandle = convert.ScheduleParallelByRef(__group, state.Dependency);

        Collect collect;
        collect.ticks = __ticks + (long)math.round(TimeSpan.TicksPerSecond * state.WorldUnmanaged.Time.ElapsedTime);
        collect.inputs = __results;
        collect.outputs = __souls.UpdateAsRef(ref state);
        collect.indices = __indices.UpdateAsRef(ref state);
        state.Dependency = collect.ScheduleByRef(jobHandle);
    }
}

/*public partial class GameSoulUpgradeSystem : SystemBase
{
    private struct Upgrade
    {
        [ReadOnly]
        public GameSoulManager manager;

        [ReadOnly]
        public BufferAccessor<GameSoulSacrificer> sacrificers;

        [ReadOnly]
        public NativeArray<GameSoulUpgradeCommand> commands;
        public NativeArray<GameSoulVersion> versions;

        public BufferAccessor<GameSoul> souls;

        public void Execute(int index)
        {
            var command = commands[index];
            var version = versions[index];
            if (version.value != command.version)
                return;

            ++version.value;
            versions[index] = version;

            var souls = this.souls[index];
            manager.Upgrade(
                command.nextLevelIndex, 
                command.soulIndex, 
                sacrificers[index].Reinterpret<int>().AsNativeArray(), 
                ref souls);
        }
    }

    [BurstCompile]
    private struct UpgradeEx : IJobChunk
    {
        [ReadOnly]
        public GameSoulManager manager;

        [ReadOnly]
        public BufferTypeHandle<GameSoulSacrificer> sacrificerType;

        [ReadOnly]
        public ComponentTypeHandle<GameSoulUpgradeCommand> commandType;
        public ComponentTypeHandle<GameSoulVersion> versionType;

        public BufferTypeHandle<GameSoul> soulType;

        public void Execute(ArchetypeChunk chunk, int chunkIndex, int firstEntityIndex)
        {
            Upgrade upgrade;
            upgrade.manager = manager;
            upgrade.sacrificers = chunk.GetBufferAccessor(sacrificerType);
            upgrade.commands = chunk.GetNativeArray(commandType);
            upgrade.versions = chunk.GetNativeArray(versionType);
            upgrade.souls = chunk.GetBufferAccessor(soulType);

            int count = chunk.Count;
            for (int i = 0; i < count; ++i)
                upgrade.Execute(i);
        }
    }

    private EntityQuery __group;
    private GameSoulSystem __soulSystem;

    protected override void OnCreate()
    {
        base.OnCreate();

        __group = GetEntityQuery(
            ComponentType.ReadOnly<GameSoulUpgradeCommand>(),
            ComponentType.ReadOnly<GameSoulVersion>(),
            ComponentType.ReadWrite<GameSoul>());

        __group.SetChangedVersionFilter(typeof(GameSoulUpgradeCommand));

        __soulSystem = World.GetOrCreateSystem<GameSoulSystem>();
    }

    protected override void OnUpdate()
    {
        var manager = __soulSystem.manager;
        if (!manager.isCreated)
            return;

        UpgradeEx upgrade;
        upgrade.manager = manager;
        upgrade.sacrificerType = GetBufferTypeHandle<GameSoulSacrificer>(true);
        upgrade.commandType = GetComponentTypeHandle<GameSoulUpgradeCommand>(true);
        upgrade.versionType = GetComponentTypeHandle<GameSoulVersion>();
        upgrade.soulType = GetBufferTypeHandle<GameSoul>();

        var jobHandle = upgrade.ScheduleParallel(__group, JobHandle.CombineDependencies(__soulSystem.readOnlyJobHandle, Dependency));

        __soulSystem.AddReadOnlyDependency(jobHandle);

        Dependency = jobHandle;
    }
}*/