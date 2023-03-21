using System;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using ZG;

public interface IGameFormulaManager
{
    int GetRemainingCount(int type, int index, in DynamicBuffer<GameFormula> formulas);
}

public struct GameFormulaManager : IDisposable
{
    [Serializable]
    public struct Level
    {
        public int cost;
        public int count;

        public Level(int cost, int count)
        {
            this.cost = cost;
            this.count = count;
        }
    }

    public struct Data
    {
        public int[] types;

        public int[] parentIndices;

        public Level[] levels;
    }

    public struct Info
    {
        public int typeStartIndex;
        public int typeCount;

        public int parentStartIndex;
        public int parentCount;

        public int levelStartIndex;
        public int levelCount;
    }

    private NativeArray<Info> __infos;
    private NativeArray<int> __types;
    private NativeArray<int> __parentIndices;
    private NativeArray<Level> __levels;

    public static int IndexOf(int index, in DynamicBuffer<GameFormula> formulas, out GameFormula formula)
    {
        int numFormulas = formulas.Length, i;
        for (i = 0; i < numFormulas; ++i)
        {
            if (formulas[i].index == index)
                break;
        }

        if (i < numFormulas)
        {
            formula = formulas[i];

            return i;
        }

        formula.index = index;
        formula.level = 0;
        formula.count = 0;

        return -1;
    }

    public bool isCreated => __infos.IsCreated;

    public GameFormulaManager(Data[] datas, Allocator allocator)
    {
        int numInfos = datas.Length, numTypes = 0, numParentIndices = 0, numLevels = 0, i;
        Data data;
        Info info;

        __infos = new NativeArray<Info>(numInfos, allocator, NativeArrayOptions.UninitializedMemory);
        for (i = 0; i < numInfos; ++i)
        {
            data = datas[i];

            info.typeStartIndex = numTypes;
            info.typeCount = data.types == null ? 0 : data.types.Length;
            info.parentStartIndex = numParentIndices;
            info.parentCount = data.parentIndices == null ? 0 : data.parentIndices.Length;
            info.levelStartIndex = numLevels;
            info.levelCount = data.levels == null ? 0 : data.levels.Length;

            numTypes += info.typeCount;
            numParentIndices += info.parentCount;
            numLevels += info.levelCount;

            __infos[i] = info;
        }

        int length;

        __types = new NativeArray<int>(numTypes, allocator, NativeArrayOptions.UninitializedMemory);
        __parentIndices = new NativeArray<int>(numParentIndices, allocator, NativeArrayOptions.UninitializedMemory);
        __levels = new NativeArray<Level>(numLevels, allocator, NativeArrayOptions.UninitializedMemory);

        numTypes = numParentIndices = numLevels = 0;
        for (i = 0; i < numInfos; ++i)
        {
            data = datas[i];

            length = data.types == null ? 0 : data.types.Length;
            if (length > 0)
            {
                NativeArray<int>.Copy(data.types, 0, __types, numTypes, length);

                numTypes += length;
            }

            length = data.parentIndices == null ? 0 : data.parentIndices.Length;
            if (length > 0)
            {
                NativeArray<int>.Copy(data.parentIndices, 0, __parentIndices, numParentIndices, length);

                numParentIndices += length;
            }

            length = data.levels == null ? 0 : data.levels.Length;
            if (length > 0)
            {
                NativeArray<Level>.Copy(data.levels, 0, __levels, numLevels, length);

                numLevels += length;
            }
        }
    }

    public void Dispose()
    {
        __infos.Dispose();
        __types.Dispose();
        __parentIndices.Dispose();
        __levels.Dispose();
    }

    public bool Set(
        int index,
        int type,
        ref int count,
        ref DynamicBuffer<GameFormula> formulas)
    {
        int i;
        var info = __infos[index];
        if (info.typeCount > 0)
        {
            for (i = 0; i < info.typeCount; ++i)
            {
                if (__types[info.typeStartIndex + i] == type)
                    break;
            }

            if (i == info.typeCount)
                return false;
        }

        int formulaIndex = IndexOf(index, formulas, out var formula);

        int length = 0, temp = formula.count + count;
        for (i = formula.level; i < info.levelCount; ++i)
        {
            length += __levels[info.levelStartIndex + i].count;
            if (length >= temp)
                break;
        }

        length = math.min(length, temp);
        if (length == formula.count)
            return false;

        count -= length - formula.count;

        formula.count = length;

        if (formulaIndex == -1)
            formulas.Add(formula);
        else
            formulas[formulaIndex] = formula;

        return true;
    }

    public bool Upgrade(
        int index,
        ref int money,
        ref DynamicBuffer<GameFormula> formulas)
    {
        int formulaIndex = IndexOf(index, formulas, out var formula);

        if (formula.level < 0)
            return false;

        var info = __infos[index];

        var level = __levels[info.levelStartIndex + formula.level];
        if (level.cost > money || level.count > formula.count)
            return false;

        if (formula.level == 0)
        {
            GameFormula temp;
            for (int i = 0; i < info.parentCount; ++i)
            {
                if (IndexOf(__parentIndices[info.parentStartIndex + i], formulas, out temp) == -1 || temp.level < 1)
                    return false;
            }
        }

        money -= level.cost;

        formula.count -= level.count;

        ++formula.level;

        if (formulaIndex == -1)
            formulas.Add(formula);
        else
            formulas[formulaIndex] = formula;

        return true;
    }
}

//[UpdateInGroup(typeof(PresentationSystemGroup)), UpdateBefore(typeof(EndFrameEntityCommandSystemGroup))]
[UpdateInGroup(typeof(GameItemSystemGroup), OrderFirst = true)]
public partial class GameFormulaSystem : SystemBase
{
    private struct Command
    {
        [ReadOnly]
        public GameFormulaManager manager;

        [ReadOnly]
        public NativeArray<Entity> entityArray;

        [ReadOnly]
        public NativeArray<EntityDataIdentity> identities;

        [ReadOnly]
        public NativeArray<GameFormulaCommand> commands;

        public NativeArray<GameFormulaVersion> versions;

        public BufferAccessor<GameFormula> instances;

        public BufferAccessor<GameFormulaCommandValue> commandValues;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<GameMoney> moneys;

        [NativeDisableParallelForRestriction]
        public BufferLookup<GameFormulaEvent> events;

        public void Execute(int index)
        {
            var version = versions[index];
            if (version.value != commands[index].version)
                return;

            ++version.value;
            versions[index] = version;

            var instances = this.instances[index];
            var commandValues = this.commandValues[index];
            GameFormulaCommandValue commandValue;
            Entity entity = entityArray[index];
            int numCommandValues = commandValues.Length, type = identities[index].type, count, sourceMoney, destinationMoney;
            for (int i = 0; i < numCommandValues; ++i)
            {
                commandValue = commandValues[i];
                if (commandValue.count > 0)
                {
                    count = commandValue.count;
                    if (manager.Set(commandValue.index, type, ref count, ref instances))
                    {
                        GameFormulaEvent result;
                        result.index = commandValue.index;
                        result.count = commandValue.count - count;

                        events[entity].Add(result);
                    }
                }
                else
                {
                    sourceMoney = moneys[entity].value;
                    destinationMoney = sourceMoney;
                    if (manager.Upgrade(commandValue.index, ref destinationMoney, ref instances) && sourceMoney != destinationMoney)
                    {
                        GameMoney money;
                        money.value = destinationMoney;
                        moneys[entity] = money;
                    }
                }
            }

            commandValues.Clear();
        }
    }

    [BurstCompile]
    private struct CommandEx : IJobChunk
    {
        [ReadOnly]
        public GameFormulaManager manager;

        [ReadOnly]
        public EntityTypeHandle entityType;

        [ReadOnly]
        public ComponentTypeHandle<EntityDataIdentity> identityType;

        [ReadOnly]
        public ComponentTypeHandle<GameFormulaCommand> commandType;

        public ComponentTypeHandle<GameFormulaVersion> versionType;

        public BufferTypeHandle<GameFormula> instanceType;

        public BufferTypeHandle<GameFormulaCommandValue> commandValueType;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<GameMoney> moneys;

        [NativeDisableParallelForRestriction]
        public BufferLookup<GameFormulaEvent> events;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Command command;
            command.manager = manager;
            command.entityArray = chunk.GetNativeArray(entityType);
            command.identities = chunk.GetNativeArray(ref identityType);
            command.commands = chunk.GetNativeArray(ref commandType);
            command.versions = chunk.GetNativeArray(ref versionType);
            command.instances = chunk.GetBufferAccessor(ref instanceType);
            command.commandValues = chunk.GetBufferAccessor(ref commandValueType);
            command.moneys = moneys;
            command.events = events;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                command.Execute(i);
        }
    }

    private EntityQuery __group;

    public GameFormulaManager manager
    {
        get;

        private set;
    }

    public void Create(GameFormulaManager.Data[] datas)
    {
        manager = new GameFormulaManager(datas, Allocator.Persistent);
    }

    protected override void OnCreate()
    {
        base.OnCreate();

        __group = GetEntityQuery(
            ComponentType.ReadOnly<EntityDataIdentity>(),
            ComponentType.ReadOnly<GameFormulaCommand>(),
            ComponentType.ReadWrite<GameFormulaVersion>(),
            ComponentType.ReadWrite<GameFormula>(),
            ComponentType.ReadWrite<GameFormulaCommandValue>());
        __group.SetChangedVersionFilter(typeof(GameFormulaCommand));
    }

    protected override void OnDestroy()
    {
        var manager = this.manager;
        if (manager.isCreated)
            manager.Dispose();

        base.OnDestroy();
    }

    protected override void OnUpdate()
    {
        if (!manager.isCreated)
            return;

        CommandEx command;
        command.manager = manager;
        command.entityType = GetEntityTypeHandle();
        command.identityType = GetComponentTypeHandle<EntityDataIdentity>(true);
        command.commandType = GetComponentTypeHandle<GameFormulaCommand>(true);
        command.versionType = GetComponentTypeHandle<GameFormulaVersion>();
        command.instanceType = GetBufferTypeHandle<GameFormula>();
        command.commandValueType = GetBufferTypeHandle<GameFormulaCommandValue>();
        command.moneys = GetComponentLookup<GameMoney>();
        command.events = GetBufferLookup<GameFormulaEvent>();
        Dependency = command.ScheduleParallel(__group, Dependency);
    }
}

