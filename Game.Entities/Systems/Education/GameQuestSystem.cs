using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using ZG;
using ZG.Unsafe;
using Unity.Collections.LowLevel.Unsafe;
using System.Diagnostics;

[EntityDataTypeName("GameMissionManager")]
[NativeContainer]
public struct GameQuestManager
{
    /*public enum ConditionType
    {
        Make, 
        Get, 
        Use, 
        Kill, 
        Tame
    }

    public enum RewardType
    {
        Quest,
        Formula, 
        Item
    }

    [Serializable]
    public struct Condition : IEquatable<Condition>
    {
        public ConditionType type;
        public int index;

        public bool Equals(Condition other)
        {
            return type == other.type && index == other.index;
        }
    }

    [Serializable]
    public struct Reward
    {
        public RewardType type;
        public int index;
        public int count;
    }

    [Serializable]
    public struct Data
    {
        public int money;

        public Condition[] conditions;

        public Reward[] rewards;
    }*/

    public struct Item
    {
        public int type;
        public int count;
        public GameItemHandle handle;
    }

    private struct Info
    {
        public int money;

        public int conditionStartIndex;
        public int conditionCount;

        public int rewardStartIndex;
        public int rewardCount;
    }

    private struct Data
    {
        private UnsafeList<Info> __infos;
        private UnsafeList<GameQuestConditionData> __conditions;
        private UnsafeList<GameQuestRewardData> __rewards;

        public readonly AllocatorManager.AllocatorHandle allocator => __infos.Allocator;

        public Data(in AllocatorManager.AllocatorHandle allocator)
        {
            __infos = new UnsafeList<Info>(0, allocator, NativeArrayOptions.UninitializedMemory);
            __conditions = new UnsafeList<GameQuestConditionData>(0, allocator, NativeArrayOptions.UninitializedMemory);
            __rewards = new UnsafeList<GameQuestRewardData>(0, allocator, NativeArrayOptions.UninitializedMemory);
        }

        public void Reset(GameQuestData[] datas)
        {
            int numInfos = datas.Length, numConditions = 0, numRewards = 0, i;
            __infos.Resize(numInfos, NativeArrayOptions.UninitializedMemory);
            for (i = 0; i < numInfos; ++i)
            {
                ref var data = ref datas[i];
                ref var info = ref __infos.ElementAt(i);

                info.money = data.money;
                info.conditionStartIndex = numConditions;
                info.conditionCount = data.conditions == null ? 0 : data.conditions.Length;
                info.rewardStartIndex = numRewards;
                info.rewardCount = data.rewards == null ? 0 : data.rewards.Length;

                numConditions += info.conditionCount;
                numRewards += info.rewardCount;
            }

            int length;

            __conditions.Resize(numConditions, NativeArrayOptions.UninitializedMemory);
            var conditions = __conditions.AsArray();
            __rewards.Resize(numRewards, NativeArrayOptions.UninitializedMemory);
            var rewards = __rewards.AsArray();

            numConditions = numRewards = 0;
            for (i = 0; i < numInfos; ++i)
            {
                ref var data = ref datas[i];

                length = data.conditions == null ? 0 : data.conditions.Length;
                if (length > 0)
                {
                    NativeArray<GameQuestConditionData>.Copy(data.conditions, 0, conditions, numConditions, length);

                    numConditions += length;
                }

                length = data.rewards == null ? 0 : data.rewards.Length;
                if (length > 0)
                {
                    NativeArray<GameQuestRewardData>.Copy(data.rewards, 0, rewards, numRewards, length);

                    numRewards += length;
                }
            }
        }

        public void Dispose()
        {
            __infos.Dispose();
            __conditions.Dispose();
            __rewards.Dispose();
        }

        public readonly bool IsClear(in GameQuest quest)
        {
            if (quest.status != GameQuestStatus.Normal)
                return true;

            var info = __infos[quest.index];
            if (quest.conditionBits == 0)
                return info.conditionCount == 0;

            int bitOffset = 0, bitCount, bitMask, count;
            for (int i = 0; i < info.conditionCount; ++i)
            {
                count = __conditions[info.conditionStartIndex + i].count;

                bitCount = 32 - math.lzcnt(count);
                bitMask = (1 << bitCount) - 1;
                if (((quest.conditionBits >> bitOffset) & bitMask) < count)
                    return false;

                bitOffset += bitCount;
            }

            return true;
        }

        public readonly bool Update(
            in DynamicBuffer<GameQuestCommandCondition> conditions,
            int index,
            ref int conditionBits)
        {
            bool result = false;
            int bitOffset = 0, bitCount;
            GameQuestConditionData condition;
            var info = __infos[index];
            for (int i = 0; i < info.conditionCount; ++i)
            {
                condition = __conditions[info.conditionStartIndex + i];
                bitCount = 32 - math.lzcnt(condition.count);

                result = GameQuestManager.Update(
                    conditions,
                    condition.type,
                    condition.index,
                    bitCount,
                    bitOffset,
                    condition.count,
                    ref conditionBits) || result;

                bitOffset += bitCount;

                UnityEngine.Assertions.Assert.IsFalse(bitOffset > 12);
            }

            return result;
        }

        public readonly bool Finish(
            int questIndex,
            ref DynamicBuffer<GameQuest> quests)
        {
            int index = IndexOf(questIndex, quests);
            if (index == -1)
                return false;

            var quest = quests[index];
            if (quest.status != GameQuestStatus.Normal)
                return false;

            if (!IsClear(quest))
                return false;

            quest.status = GameQuestStatus.Finish;
            quests[index] = quest;

            return true;
        }

        public readonly bool Complete(
            int questIndex,
            in GameItemHandle itemHandle,
            ref DynamicBuffer<GameQuest> quests,
            ref DynamicBuffer<GameFormulaCommand> formulaCommands,
            ref NativeQueue<Item>.ParallelWriter items,
            out int money)
        {
            money = 0;

            int index = IndexOf(questIndex, quests);
            if (index == -1)
                return false;

            var quest = quests[index];
            if (quest.status == GameQuestStatus.Complete)
                return false;

            if (!IsClear(quest))
                return false;

            quest.status = GameQuestStatus.Complete;
            quests[index] = quest;

            int i, j;
            var info = __infos[quest.index];
            GameQuestRewardData reward;
            for (i = 0; i < info.rewardCount; ++i)
            {
                reward = __rewards[info.rewardStartIndex + i];
                switch (reward.type)
                {
                    case GameQuestRewardType.Quest:
                        if (IndexOf(reward.index, quests) == -1)
                        {
                            GameQuest temp;
                            temp.index = reward.index;
                            temp.conditionBits = 0;
                            temp.status = GameQuestStatus.Normal;

                            for (j = 0; j < reward.count; ++j)
                                quests.Add(temp);
                        }
                        break;
                    case GameQuestRewardType.Formula:
                        GameFormulaCommand formulaCommand;
                        formulaCommand.index = reward.index;
                        formulaCommand.count = reward.count;

                        formulaCommands.Add(formulaCommand);
                        break;
                    case GameQuestRewardType.Item:
                        Item item;
                        item.type = reward.index;
                        item.count = reward.count;
                        item.handle = itemHandle;

                        items.Enqueue(item);
                        break;
                }
            }

            money = info.money;

            return true;
        }
    }

    [NativeDisableUnsafePtrRestriction]
    private unsafe Data* __data;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
    internal AtomicSafetyHandle m_Safety;

    internal static readonly SharedStatic<int> StaticSafetyID = SharedStatic<int>.GetOrCreate<GameQuestManager>();
#endif

    public unsafe bool isCreated => __data != null;

    public unsafe GameQuestManager(in AllocatorManager.AllocatorHandle allocator)
    {
        __data = AllocatorManager.Allocate<Data>(allocator);
        *__data = new Data(allocator);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        m_Safety = CollectionHelper.CreateSafetyHandle(allocator);

        CollectionHelper.SetStaticSafetyId<GameQuestManager>(ref m_Safety, ref StaticSafetyID.Data);
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

    public unsafe void Reset(GameQuestData[] datas)
    {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif

        __data->Reset(datas);
    }

    public static int IndexOf(
        int questIndex,
        in DynamicBuffer<GameQuest> quests)
    {
        int numQuests = quests.Length;
        for (int i = 0; i < numQuests; ++i)
        {
            if (quests[i].index == questIndex)
                return i;
        }

        return -1;
    }

    public static bool Update(
        in DynamicBuffer<GameQuestCommandCondition> conditions,
        GameQuestConditionType type,
        int index,
        int bitCount,
        int bitOffset,
        int maxCount,
        ref int conditionBits)
    {
        bool result = false;
        int length = conditions.Length, originCount, count, bitMask;
        GameQuestCommandCondition condition;
        for (int i = 0; i < length; ++i)
        {
            condition = conditions[i];
            if (condition.type == type && condition.index == index)
            {
                count = condition.count;

#if !GAME_QUEST
                count = math.max(count, 0);
#endif

                if (count != 0)
                {
                    bitMask = (1 << bitCount) - 1;
                    originCount = (conditionBits >> bitOffset) & bitMask;
                    count = math.clamp(originCount + count, 0, maxCount);
                    if (count != originCount)
                    {
                        conditionBits &= ~(bitMask << bitOffset);
                        conditionBits |= count << bitOffset;

                        result = true;
                    }
                }
            }
        }

        return result;
    }

    public unsafe bool IsClear(in GameQuest quest)
    {
        __CheckRead();

        return __data->IsClear(quest);
    }

    public unsafe bool Update(
        in DynamicBuffer<GameQuestCommandCondition> conditions,
        int index,
        ref int conditionBits)
    {
        __CheckRead();

        return __data->Update(conditions, index, ref conditionBits);
    }

    public unsafe bool Finish(
        int questIndex, 
        ref DynamicBuffer<GameQuest> quests)
    {
        __CheckRead();

        return __data->Finish(questIndex, ref quests);
    }

    public unsafe bool Complete(
        int questIndex,
        in GameItemHandle itemHandle,
        ref DynamicBuffer<GameQuest> quests,
        ref DynamicBuffer<GameFormulaCommand> formulaCommands,
        ref NativeQueue<Item>.ParallelWriter items,
        out int money)
    {
        __CheckRead();

        return __data->Complete(
            questIndex, 
            itemHandle, 
            ref quests, 
            ref formulaCommands, 
            ref items, 
            out money);
    }

    [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
    void __CheckRead()
    {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif
    }
}

[BurstCompile, CreateAfter(typeof(GameItemSystem)), UpdateInGroup(typeof(GameItemSystemGroup), OrderFirst = true), UpdateBefore(typeof(GameFormulaSystem))/*, UpdateBefore(typeof(GameItemSystem))*/]
public partial struct GameQuestSystem : ISystem
{
    public struct Command
    {
        [ReadOnly]
        public GameQuestManager manager;

        [ReadOnly]
        public NativeArray<GameItemRoot> itemRoots;

        public BufferAccessor<GameQuest> quests;

        public BufferAccessor<GameQuestCommand> commands;

        public BufferAccessor<GameQuestCommandCondition> commandConditions;

        public BufferAccessor<GameFormulaCommand> formulaCommands;

        public NativeQueue<GameQuestManager.Item>.ParallelWriter items;

        public void Execute(int index, out int money, out bool hasFormulaCommand)
        {
            var quests = this.quests[index];

            var formulaCommands = this.formulaCommands[index];
            int numFormulaCommands = formulaCommands.Length;

            money = 0;

            var commands = this.commands[index];

            bool needToCommandAgain = false;
            foreach (var command in commands)
            {
                if (__Submit(index, command, ref formulaCommands, ref quests, out int moneyTemp))
                    money += moneyTemp;
                else
                    needToCommandAgain = true;
            }

            bool isAnyQuestUpdate = false;

            var commandConditions = this.commandConditions[index];
            if (!commandConditions.IsEmpty)
            {
                GameQuest quest;
                int numQuests = quests.Length, i;

                for (i = 0; i < numQuests; ++i)
                {
                    quest = quests[i];
                    if (quest.status != GameQuestStatus.Normal)
                        continue;

                    if (manager.Update(commandConditions, quest.index, ref quest.conditionBits))
                    {
                        quests[i] = quest;

                        isAnyQuestUpdate = true;
                    }
                }

                commandConditions.Clear();
            }

            if (needToCommandAgain && isAnyQuestUpdate)
            {
                foreach (var command in commands)
                {
                    __Submit(index, command, ref formulaCommands, ref quests, out int moneyTemp);

                    money += moneyTemp;
                }
            }

            commands.Clear();

            hasFormulaCommand = numFormulaCommands != formulaCommands.Length;
        }

        private bool __Submit(
            int index, 
            in GameQuestCommand command, 
            ref DynamicBuffer<GameFormulaCommand> formulaCommands, 
            ref DynamicBuffer<GameQuest> quests, 
            out int money)
        {
            money = 0;
            switch (command.status)
            {
                case GameQuestStatus.Finish:
                    return manager.Finish(command.index, ref quests);
                case GameQuestStatus.Complete:
                    if (manager.Complete(
                        command.index,
                        itemRoots[index].handle,
                        ref quests,
                        ref formulaCommands,
                        ref items,
                        out money))
                        return true;

                    break;
            }

            return false;
        }
    }

    [BurstCompile]
    public struct CommandEx : IJobChunk
    {
        [ReadOnly]
        public GameQuestManager manager;
        [ReadOnly]
        public ComponentTypeHandle<GameItemRoot> itemRootType;

        public ComponentTypeHandle<GameMoney> monyType;

        public BufferTypeHandle<GameQuestCommand> commandType;

        public BufferTypeHandle<GameQuest> questType;
        public BufferTypeHandle<GameQuestCommandCondition> commandConditionType;

        public BufferTypeHandle<GameFormulaCommand> formulaCommandType;

        public NativeQueue<GameQuestManager.Item>.ParallelWriter items;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Command command;
            command.manager = manager;
            command.itemRoots = chunk.GetNativeArray(ref itemRootType);
            command.quests = chunk.GetBufferAccessor(ref questType);
            command.commands = chunk.GetBufferAccessor(ref commandType);
            command.commandConditions = chunk.GetBufferAccessor(ref commandConditionType);
            command.formulaCommands = chunk.GetBufferAccessor(ref formulaCommandType);
            command.items = items;

            bool hasFormulaCommand;
            int money;
            NativeArray<int> moenies = default;
            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
            {
                command.Execute(i, out money, out hasFormulaCommand);

                if(money != 0)
                {
                    if (!moenies.IsCreated)
                        moenies = chunk.GetNativeArray(ref monyType).Reinterpret<int>();

                    moenies[i] += money;
                }

                if (hasFormulaCommand)
                    chunk.SetComponentEnabled(ref formulaCommandType, i, true);

                chunk.SetComponentEnabled(ref commandType, i, false);
                chunk.SetComponentEnabled(ref commandConditionType, i, false);
            }
        }
    }

    [BurstCompile]
    private struct ApplyItems : IJob
    {
        public GameItemManager itemManager;
        public NativeQueue<GameQuestManager.Item> items;

        public void Execute()
        {
            int count, parentChildIndex;
            GameItemHandle parentHandle;
            while (items.TryDequeue(out var item))
            {
                if (!itemManager.Find(item.handle, item.type, item.count, out parentChildIndex, out parentHandle) &&
                    itemManager.Find(item.handle, item.type, 1, out parentChildIndex, out parentHandle))
                    continue;

                count = item.count;
                itemManager.Add(parentHandle, parentChildIndex, item.type, ref count);
            }
        }
    }

    private EntityQuery __group;
    private GameItemManagerShared __itemManager;
    private NativeQueue<GameQuestManager.Item> __items;

    private ComponentTypeHandle<GameItemRoot> __itemRootType;

    private ComponentTypeHandle<GameMoney> __monyType;

    private BufferTypeHandle<GameQuest> __questType;

    private BufferTypeHandle<GameQuestCommand> __commandType;

    private BufferTypeHandle<GameQuestCommandCondition> __commandConditionType;

    private BufferTypeHandle<GameFormulaCommand> __formulaCommandType;

    public GameQuestManager manager
    {
        get;

        private set;
    }

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __group = builder
                    .WithAllRW<GameQuest>()
                    .WithAnyRW<GameQuestCommand, GameQuestCommandCondition>()
                    .WithOptions(EntityQueryOptions.IncludeDisabledEntities)
                    .Build(ref state);

        //__group.SetChangedVersionFilter(ComponentType.ReadWrite<GameQuestCommand>());

        //__group.AddChangedVersionFilter(ComponentType.ReadWrite<GameQuestCommandCondition>());

        __itemRootType = state.GetComponentTypeHandle<GameItemRoot>(true);
        __monyType = state.GetComponentTypeHandle<GameMoney>();
        __questType = state.GetBufferTypeHandle<GameQuest>();
        __commandType = state.GetBufferTypeHandle<GameQuestCommand>();
        __commandConditionType = state.GetBufferTypeHandle<GameQuestCommandCondition>();
        __formulaCommandType = state.GetBufferTypeHandle<GameFormulaCommand>();

        __itemManager = state.WorldUnmanaged.GetExistingSystemUnmanaged<GameItemSystem>().manager;

        __items = new NativeQueue<GameQuestManager.Item>(Allocator.Persistent);

        manager = new GameQuestManager(Allocator.Persistent);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        manager.Dispose();

        __items.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!manager.isCreated)
            return;

        CommandEx command;
        command.manager = manager;
        command.itemRootType = __itemRootType.UpdateAsRef(ref state);
        command.monyType = __monyType.UpdateAsRef(ref state);
        command.commandType = __commandType.UpdateAsRef(ref state);
        command.questType = __questType.UpdateAsRef(ref state);
        command.commandConditionType = __commandConditionType.UpdateAsRef(ref state);
        command.formulaCommandType = __formulaCommandType.UpdateAsRef(ref state);
        command.items = __items.AsParallelWriter();

        var jobHandle = command.ScheduleParallelByRef(__group, state.Dependency);

        ApplyItems applyItems;
        applyItems.itemManager = __itemManager.value;
        applyItems.items = __items;

        ref var lookupJobManager = ref __itemManager.lookupJobManager;

        jobHandle = applyItems.Schedule(JobHandle.CombineDependencies(jobHandle, lookupJobManager.readWriteJobHandle));

        lookupJobManager.readWriteJobHandle = jobHandle;

        state.Dependency = jobHandle;
    }
}

