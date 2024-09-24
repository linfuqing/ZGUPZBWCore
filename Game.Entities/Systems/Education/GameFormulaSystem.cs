using System;
using System.Diagnostics;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using ZG;
using ZG.Unsafe;

public interface IGameFormulaManager
{
    int GetRemainingCount(int type, int index, in DynamicBuffer<GameFormula> formulas);
}

[Serializable]
public struct GameFormulaLevel
{
    public int cost;
    public int count;

    public GameFormulaLevel(int cost, int count)
    {
        this.cost = cost;
        this.count = count;
    }
}

[Serializable]
public struct GameFormulaData
{
    public int[] types;

    public int[] parentIndices;

    public GameFormulaLevel[] levels;
}

[NativeContainer]
public struct GameFormulaManager : IDisposable
{
    public struct Info
    {
        public int typeStartIndex;
        public int typeCount;

        public int parentStartIndex;
        public int parentCount;

        public int levelStartIndex;
        public int levelCount;
    }

    private struct Data
    {
        private UnsafeList<Info> __infos;
        private UnsafeList<int> __types;
        private UnsafeList<int> __parentIndices;
        private UnsafeList<GameFormulaLevel> __levels;

        public readonly AllocatorManager.AllocatorHandle allocator => __infos.Allocator;

        public Data(in AllocatorManager.AllocatorHandle allocator)
        {
            __infos = new UnsafeList<Info>(0, allocator);
            __types = new UnsafeList<int>(0, allocator);
            __parentIndices = new UnsafeList<int>(0, allocator);
            __levels = new UnsafeList<GameFormulaLevel>(0, allocator);
        }

        public void Dispose()
        {
            __infos.Dispose();
            __types.Dispose();
            __parentIndices.Dispose();
            __levels.Dispose();
        }

        public void Reset(GameFormulaData[] datas)
        {
            int numInfos = datas.Length, numTypes = 0, numParentIndices = 0, numLevels = 0, i;

            __infos.Resize(numInfos, NativeArrayOptions.UninitializedMemory);
            for (i = 0; i < numInfos; ++i)
            {
                ref var data = ref datas[i];
                ref var info = ref __infos.ElementAt(i);

                info.typeStartIndex = numTypes;
                info.typeCount = data.types == null ? 0 : data.types.Length;
                info.parentStartIndex = numParentIndices;
                info.parentCount = data.parentIndices == null ? 0 : data.parentIndices.Length;
                info.levelStartIndex = numLevels;
                info.levelCount = data.levels == null ? 0 : data.levels.Length;

                numTypes += info.typeCount;
                numParentIndices += info.parentCount;
                numLevels += info.levelCount;
            }

            int length;

            __types.Resize(numTypes, NativeArrayOptions.UninitializedMemory);
            var types = __types.AsArray();
            __parentIndices.Resize(numParentIndices, NativeArrayOptions.UninitializedMemory);
            var parentIndices = __parentIndices.AsArray();
            __levels.Resize(numLevels, NativeArrayOptions.UninitializedMemory);
            var levels = __levels.AsArray();

            numTypes = numParentIndices = numLevels = 0;
            for (i = 0; i < numInfos; ++i)
            {
                ref var data = ref datas[i];

                length = data.types == null ? 0 : data.types.Length;
                if (length > 0)
                {
                    NativeArray<int>.Copy(data.types, 0, types, numTypes, length);

                    numTypes += length;
                }

                length = data.parentIndices == null ? 0 : data.parentIndices.Length;
                if (length > 0)
                {
                    NativeArray<int>.Copy(data.parentIndices, 0, parentIndices, numParentIndices, length);

                    numParentIndices += length;
                }

                length = data.levels == null ? 0 : data.levels.Length;
                if (length > 0)
                {
                    NativeArray<GameFormulaLevel>.Copy(data.levels, 0, levels, numLevels, length);

                    numLevels += length;
                }
            }
        }

        public readonly bool Set(
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

            int formulaIndex = IndexOf(index, formulas.AsNativeArray(), out var formula);

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

        public readonly bool Upgrade(
            int index,
            ref int money,
            ref DynamicBuffer<GameFormula> formulas)
        {
            var formulaArray = formulas.AsNativeArray();
            int formulaIndex = IndexOf(index, formulaArray, out var formula);

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
                    if (IndexOf(__parentIndices[info.parentStartIndex + i], formulaArray, out temp) == -1 || temp.level < 1)
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

    [NativeDisableUnsafePtrRestriction]
    private unsafe Data* __data;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
    internal AtomicSafetyHandle m_Safety;

    internal static readonly SharedStatic<int> StaticSafetyID = SharedStatic<int>.GetOrCreate<GameFormulaManager>();
#endif

    public static int IndexOf(int index, in NativeArray<GameFormula> formulas, out GameFormula formula)
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

    public unsafe bool isCreated => __data != null;

    public unsafe GameFormulaManager(Allocator allocator)
    {
        __data = AllocatorManager.Allocate<Data>(allocator);
        *__data = new Data(allocator);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        m_Safety = CollectionHelper.CreateSafetyHandle(allocator);

        CollectionHelper.SetStaticSafetyId<GameFormulaManager>(ref m_Safety, ref StaticSafetyID.Data);
#endif
    }

    public unsafe void Dispose()
    {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        CollectionHelper.DisposeSafetyHandle(ref m_Safety);
#endif

        var allocator = __data->allocator;
        __data->Dispose();
        AllocatorManager.Free(allocator, __data);
        __data = null;
    }

    public unsafe void Reset(GameFormulaData[] datas)
    {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif

        __data->Reset(datas);
    }

    public unsafe bool Set(
            int index,
            int type,
            ref int count,
            ref DynamicBuffer<GameFormula> formulas)
    {
        __CheckRead();

        return __data->Set(index, type, ref count, ref formulas);
    }

    public unsafe bool Upgrade(
            int index,
            ref int money,
            ref DynamicBuffer<GameFormula> formulas)
    {

        __CheckRead();

        return __data->Upgrade(index, ref money, ref formulas);
    }

    [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
    void __CheckRead()
    {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif
    }
}

//[UpdateInGroup(typeof(PresentationSystemGroup)), UpdateBefore(typeof(EndFrameEntityCommandSystemGroup))]
[BurstCompile, UpdateInGroup(typeof(GameItemSystemGroup), OrderFirst = true)]
public partial struct GameFormulaSystem : ISystem
{
    private struct Command
    {
        [ReadOnly]
        public GameFormulaManager manager;

        //[ReadOnly]
        //public NativeArray<Entity> entityArray;

        [ReadOnly]
        public NativeArray<EntityDataIdentity> identities;

        public NativeArray<GameMoney> moneys;

        public BufferAccessor<GameFormulaCommand> commands;

        public BufferAccessor<GameFormula> instances;

        public BufferAccessor<GameFormulaEvent> events;

        public void Execute(int index)
        {
            int type = identities[index].type, sourceMoney = moneys[index].value, destinationMoney = sourceMoney, count;
            //Entity entity = entityArray[index];
            var instances = this.instances[index];
            var commands = this.commands[index];
            var events = this.events[index];
            foreach (var command in commands)
            {
                if (command.count > 0)
                {
                    count = command.count;
                    if (manager.Set(command.index, type, ref count, ref instances))
                    {
                        GameFormulaEvent result;
                        result.index = command.index;
                        result.count = command.count - count;

                        events.Add(result);
                    }
                }
                else
                    manager.Upgrade(command.index, ref destinationMoney, ref instances);
            }

            if (destinationMoney != sourceMoney)
            {
                GameMoney money;
                money.value = destinationMoney;
                moneys[index] = money;
            }

            commands.Clear();
        }
    }

    [BurstCompile]
    private struct CommandEx : IJobChunk
    {
        [ReadOnly]
        public GameFormulaManager manager;

        //[ReadOnly]
        //public EntityTypeHandle entityType;

        [ReadOnly]
        public ComponentTypeHandle<EntityDataIdentity> identityType;

        public ComponentTypeHandle<GameMoney> moneyType;

        public BufferTypeHandle<GameFormulaCommand> commandType;

        public BufferTypeHandle<GameFormula> instanceType;

        public BufferTypeHandle<GameFormulaEvent> eventType;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Command command;
            command.manager = manager;
            //command.entityArray = chunk.GetNativeArray(entityType);
            command.identities = chunk.GetNativeArray(ref identityType);
            command.moneys = chunk.GetNativeArray(ref moneyType);
            command.commands = chunk.GetBufferAccessor(ref commandType);
            command.instances = chunk.GetBufferAccessor(ref instanceType);
            command.events = chunk.GetBufferAccessor(ref eventType);

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
            {
                command.Execute(i);

                chunk.SetComponentEnabled(ref commandType, i, false);
            }
        }
    }

    private EntityQuery __group;

    //private EntityTypeHandle __entityType;

    private ComponentTypeHandle<EntityDataIdentity> __identityType;
    private ComponentTypeHandle<GameMoney> __moneyType;
    private BufferTypeHandle<GameFormulaCommand> __commandType;
    private BufferTypeHandle<GameFormula> __instanceType;
    private BufferTypeHandle<GameFormulaEvent> __eventType;

    public GameFormulaManager manager
    {
        get;

        private set;
    }

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __group = builder
                    .WithAll<EntityDataIdentity>()
                    .WithAllRW<GameFormula, GameFormulaCommand>()
                    .WithOptions(EntityQueryOptions.IncludeDisabledEntities)
                    .Build(ref state);
        __group.SetChangedVersionFilter(ComponentType.ReadWrite<GameFormulaCommand>());

        //__entityType = state.GetEntityTypeHandle();
        __identityType = state.GetComponentTypeHandle<EntityDataIdentity>(true);
        __moneyType = state.GetComponentTypeHandle<GameMoney>();
        __instanceType = state.GetBufferTypeHandle<GameFormula>();
        __commandType = state.GetBufferTypeHandle<GameFormulaCommand>();
        __eventType = state.GetBufferTypeHandle<GameFormulaEvent>();

        manager = new GameFormulaManager(Allocator.Persistent);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        manager.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        CommandEx command;
        command.manager = manager;
        //command.entityType = __entityType.UpdateAsRef(ref state);
        command.identityType = __identityType.UpdateAsRef(ref state);
        command.moneyType = __moneyType.UpdateAsRef(ref state);
        command.instanceType = __instanceType.UpdateAsRef(ref state);
        command.commandType = __commandType.UpdateAsRef(ref state);
        command.eventType = __eventType.UpdateAsRef(ref state);
        state.Dependency = command.ScheduleParallelByRef(__group, state.Dependency);
    }
}

