using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using ZG;

[EntityDataTypeName("GameMissionManager")]
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

    private NativeArray<Info> __infos;
    private NativeArray<GameQuestConditionData> __conditions;
    private NativeArray<GameQuestRewardData> __rewards;

    public bool isCreated => __infos.IsCreated;

    public GameQuestManager(GameQuestData[] datas, Allocator allocator)
    {
        int numInfos = datas.Length, numConditions = 0, numRewards = 0, i;
        GameQuestData data;
        Info info;

        __infos = new NativeArray<Info>(numInfos, allocator, NativeArrayOptions.UninitializedMemory);
        for (i = 0; i < numInfos; ++i)
        {
            data = datas[i];

            info.money = data.money;
            info.conditionStartIndex = numConditions;
            info.conditionCount = data.conditions == null ? 0 : data.conditions.Length;
            info.rewardStartIndex = numRewards;
            info.rewardCount = data.rewards == null ? 0 : data.rewards.Length;

            numConditions += info.conditionCount;
            numRewards += info.rewardCount;

            __infos[i] = info;
        }

        int length;

        __conditions = new NativeArray<GameQuestConditionData>(numConditions, allocator, NativeArrayOptions.UninitializedMemory);
        __rewards = new NativeArray<GameQuestRewardData>(numRewards, allocator, NativeArrayOptions.UninitializedMemory);

        numConditions = numRewards = 0;
        for (i = 0; i < numInfos; ++i)
        {
            data = datas[i];

            length = data.conditions == null ? 0 : data.conditions.Length;
            if (length > 0)
            {
                NativeArray<GameQuestConditionData>.Copy(data.conditions, 0, __conditions, numConditions, length);

                numConditions += length;
            }

            length = data.rewards == null ? 0 : data.rewards.Length;
            if (length > 0)
            {
                NativeArray<GameQuestRewardData>.Copy(data.rewards, 0, __rewards, numRewards, length);

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

    public bool IsClear(in GameQuest quest)
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

    public static bool Update(
        in DynamicBuffer<GameQuestCommandCondition> conditions,
        GameQuestConditionType type,
        int index,
        int bitCount,
        int bitOffset,
        int bitMax, 
        ref int conditionBits)
    {
        bool result = false;
        int length = conditions.Length, count, bitMask;
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
                    count = math.clamp(((conditionBits >> bitOffset) & bitMask) + count, 0, bitMax);

                    conditionBits &= ~(bitMask << bitOffset);
                    conditionBits |= count << bitOffset;

                    result = true;
                }
            }
        }

        return result;
    }

    public bool Update(
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

            result = Update(conditions,
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

    public bool Finish(
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

    public bool Complete(
        int questIndex,
        in GameItemHandle itemHandle,
        ref DynamicBuffer<GameQuest> quests,
        ref DynamicBuffer<GameFormulaCommandValue> formulaCommandValues,
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
                    GameFormulaCommandValue formulaCommandValue;
                    formulaCommandValue.index = reward.index;
                    formulaCommandValue.count = reward.count;

                    formulaCommandValues.Add(formulaCommandValue);
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

[UpdateInGroup(typeof(GameItemSystemGroup), OrderFirst = true), UpdateBefore(typeof(GameFormulaSystem))/*, UpdateBefore(typeof(GameItemSystem))*/]
public partial class GameQuestSystem : SystemBase
{
    public struct Command
    {
        [ReadOnly]
        public GameQuestManager manager;

        [ReadOnly]
        public NativeArray<Entity> entityArray;

        [ReadOnly]
        public NativeArray<GameItemRoot> itemRoots;

        [ReadOnly]
        public NativeArray<GameFormulaVersion> formulaVersions;

        [ReadOnly]
        public NativeArray<GameQuestCommand> commands;

        [ReadOnly]
        public NativeArray<GameQuestCommandValue> commandValues;

        public NativeArray<GameQuestVersion> versions;

        public BufferAccessor<GameQuest> quests;

        public BufferAccessor<GameQuestCommandCondition> commandConditions;

        public BufferAccessor<GameFormulaCommandValue> formulaCommandValues;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<GameFormulaCommand> formulaCommands;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<GameMoney> monies;

        public NativeQueue<GameQuestManager.Item>.ParallelWriter items;

        public void Execute(int index)
        {
            var version = versions[index];
            if (version.value != commands[index].version)
                return;

            var commandValue = commandValues[index];
            var quests = this.quests[index];

            __Submit(index, version.value, commandValue, ref quests);

            var commandConditions = this.commandConditions[index];
            if (commandConditions.Length > 0)
            {
                GameQuest quest;
                int numQuests = quests.Length, i;
                for (i = 0; i < numQuests; ++i)
                {
                    quest = quests[i];
                    if (quest.status != GameQuestStatus.Normal)
                        continue;

                    if (manager.Update(commandConditions, quest.index, ref quest.conditionBits))
                        quests[i] = quest;
                }

                commandConditions.Clear();
            }

            __Submit(index, version.value, commandValue, ref quests);

            ++version.value;
            versions[index] = version;
        }

        private bool __Submit(int index, int version, in GameQuestCommandValue commandValue, ref DynamicBuffer<GameQuest> quests)
        {
            if (version == commandValue.version)
            {
                switch (commandValue.status)
                {
                    case GameQuestStatus.Finish:
                        return manager.Finish(commandValue.index, ref quests);
                    case GameQuestStatus.Complete:
                        var formulaCommandValues = this.formulaCommandValues[index];
                        int numFormulaCommandValues = formulaCommandValues.Length;
                        if (manager.Complete(
                            commandValue.index,
                            itemRoots[index].handle,
                            ref quests,
                            ref formulaCommandValues,
                            ref items,
                            out int money))
                        {
                            Entity entity = entityArray[index];
                            if (numFormulaCommandValues != formulaCommandValues.Length)
                            {
                                GameFormulaCommand formulaCommand;
                                formulaCommand.version = formulaVersions[index].value;
                                formulaCommands[entity] = formulaCommand;
                            }

                            if (money != 0)
                            {
                                var result = monies[entity];
                                result.value += money;
                                monies[entity] = result;
                            }

                            return true;
                        }
                        break;
                }
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
        public EntityTypeHandle entityType;
        [ReadOnly]
        public ComponentTypeHandle<GameItemRoot> itemRootType;
        [ReadOnly]
        public ComponentTypeHandle<GameFormulaVersion> formulaVersionType;
        [ReadOnly]
        public ComponentTypeHandle<GameQuestCommand> commandType;
        [ReadOnly]
        public ComponentTypeHandle<GameQuestCommandValue> commandValueType;

        public ComponentTypeHandle<GameQuestVersion> versionType;

        public BufferTypeHandle<GameQuest> questType;
        public BufferTypeHandle<GameQuestCommandCondition> commandConditionType;

        public BufferTypeHandle<GameFormulaCommandValue> formulaCommandValueType;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<GameFormulaCommand> formulaCommands;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<GameMoney> monies;

        public NativeQueue<GameQuestManager.Item>.ParallelWriter items;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Command command;
            command.manager = manager;
            command.entityArray = chunk.GetNativeArray(entityType);
            command.itemRoots = chunk.GetNativeArray(ref itemRootType);
            command.formulaVersions = chunk.GetNativeArray(ref formulaVersionType);
            command.commands = chunk.GetNativeArray(ref commandType);
            command.commandValues = chunk.GetNativeArray(ref commandValueType);
            command.versions = chunk.GetNativeArray(ref versionType);
            command.quests = chunk.GetBufferAccessor(ref questType);
            command.commandConditions = chunk.GetBufferAccessor(ref commandConditionType);
            command.formulaCommandValues = chunk.GetBufferAccessor(ref formulaCommandValueType);
            command.formulaCommands = formulaCommands;
            command.monies = monies;
            command.items = items;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                command.Execute(i);
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

    public GameQuestManager manager
    {
        get;

        private set;
    }

    public void Create(GameQuestData[] datas)
    {
        manager = new GameQuestManager(datas, Allocator.Persistent);
    }

    protected override void OnCreate()
    {
        base.OnCreate();

        __group = GetEntityQuery(
            new EntityQueryDesc()
            {
                All = new ComponentType[]
                {
                    ComponentType.ReadOnly<GameQuestCommand>(),
                    ComponentType.ReadWrite<GameQuestVersion>(),
                    ComponentType.ReadWrite<GameQuest>()
                },
                Any = new ComponentType[]
                {
                    ComponentType.ReadOnly<GameQuestCommandValue>(),
                    ComponentType.ReadWrite<GameQuestCommandCondition>()
                }, 
                Options = EntityQueryOptions.IncludeDisabledEntities
            });
        __group.SetChangedVersionFilter(typeof(GameQuestCommand));

        __itemManager = World.GetOrCreateSystemUnmanaged<GameItemSystem>().manager;

        __items = new NativeQueue<GameQuestManager.Item>(Allocator.Persistent);
    }

    protected override void OnDestroy()
    {
        var manager = this.manager;
        if (manager.isCreated)
            manager.Dispose();

        __items.Dispose();

        base.OnDestroy();
    }

    protected override void OnUpdate()
    {
        if (!manager.isCreated)
            return;

        CommandEx command;
        command.manager = manager;
        command.entityType = GetEntityTypeHandle();
        command.itemRootType = GetComponentTypeHandle<GameItemRoot>(true);
        command.formulaVersionType = GetComponentTypeHandle<GameFormulaVersion>(true);
        command.commandType = GetComponentTypeHandle<GameQuestCommand>(true);
        command.commandValueType = GetComponentTypeHandle<GameQuestCommandValue>(true);
        command.versionType = GetComponentTypeHandle<GameQuestVersion>();
        command.questType = GetBufferTypeHandle<GameQuest>();
        command.commandConditionType = GetBufferTypeHandle<GameQuestCommandCondition>();
        command.formulaCommandValueType = GetBufferTypeHandle<GameFormulaCommandValue>();
        command.formulaCommands = GetComponentLookup<GameFormulaCommand>();
        command.monies = GetComponentLookup<GameMoney>();
        command.items = __items.AsParallelWriter();

        var jobHandle = command.ScheduleParallel(__group, Dependency);

        ApplyItems applyItems;
        applyItems.itemManager = __itemManager.value;
        applyItems.items = __items;

        ref var lookupJobManager = ref __itemManager.lookupJobManager;

        jobHandle = applyItems.Schedule(JobHandle.CombineDependencies(jobHandle, lookupJobManager.readWriteJobHandle));

        lookupJobManager.readWriteJobHandle = jobHandle;

        Dependency = jobHandle;
    }
}

