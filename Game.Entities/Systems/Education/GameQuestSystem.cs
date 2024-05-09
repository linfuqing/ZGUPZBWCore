using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using ZG;
using ZG.Unsafe;
using Unity.Collections.LowLevel.Unsafe;
using FixedString = Unity.Collections.FixedString32Bytes;

public struct GameQuestItem
{
    public int type;
    public int count;
    public GameItemHandle handle;
}

internal struct GameQuestManagerData
{
    private struct Info
    {
        public int conditionStartIndex;
        public int conditionCount;

        public int rewardStartIndex;
        public int rewardCount;

        public FixedString label;
    }

    private struct Option
    {
        public int rewardStartIndex;
        public int rewardCount;
    }

    private UnsafeList<Info> __infos;
    private UnsafeList<Option> __options;
    private UnsafeList<GameQuestConditionData> __conditions;
    private UnsafeList<GameQuestRewardData> __rewards;

    public readonly AllocatorManager.AllocatorHandle allocator => __infos.Allocator;

    public GameQuestManagerData(in AllocatorManager.AllocatorHandle allocator)
    {
        __infos = new UnsafeList<Info>(0, allocator, NativeArrayOptions.UninitializedMemory);
        __options = new UnsafeList<Option>(0, allocator, NativeArrayOptions.UninitializedMemory);
        __conditions = new UnsafeList<GameQuestConditionData>(0, allocator, NativeArrayOptions.UninitializedMemory);
        __rewards = new UnsafeList<GameQuestRewardData>(0, allocator, NativeArrayOptions.UninitializedMemory);
    }

    public void Reset(GameQuestData[] datas, GameQuestOption[] options)
    {
        int numInfos = datas.Length, numConditions = 0, numRewards = 0, i;
        __infos.Resize(numInfos, NativeArrayOptions.UninitializedMemory);
        for (i = 0; i < numInfos; ++i)
        {
            ref var data = ref datas[i];
            ref var info = ref __infos.ElementAt(i);

            //info.money = data.money;
            info.label = data.label;
            info.conditionStartIndex = numConditions;
            info.conditionCount = data.conditions == null ? 0 : data.conditions.Length;
            info.rewardStartIndex = numRewards;
            info.rewardCount = data.rewards == null ? 0 : data.rewards.Length;

            numConditions += info.conditionCount;
            numRewards += info.rewardCount;
        }

        int numOptions = options.Length;
        __options.Resize(numOptions, NativeArrayOptions.UninitializedMemory);
        for (i = 0; i < numOptions; ++i)
        {
            ref var source = ref options[i];
            ref var destination = ref __options.ElementAt(i);

            destination.rewardStartIndex = numRewards;
            destination.rewardCount = source.rewards == null ? 0 : source.rewards.Length;

            numRewards += destination.rewardCount;
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

        for (i = 0; i < numOptions; ++i)
        {
            ref var option = ref options[i];
            
            length = option.rewards == null ? 0 : option.rewards.Length;
            if (length > 0)
            {
                NativeArray<GameQuestRewardData>.Copy(option.rewards, 0, rewards, numRewards, length);

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

    public readonly NativeSlice<GameQuestConditionData> GetConditions(int index)
    {
        var info = __infos[index];

        return __conditions.AsArray().Slice(info.conditionStartIndex, info.conditionCount);
    }

    public readonly bool IsClear(in GameQuest quest)
    {
        var info = __infos[quest.index];
        return GameQuestUtility.IsClear(quest,
            __conditions.AsArray().Slice(info.conditionStartIndex, info.conditionCount));
    }

    public readonly bool Update(
        in NativeArray<GameQuestCommandCondition> commands,
        int index,
        ref int conditionBits)
    {
        var info = __infos[index];

        return GameQuestUtility.Update(
            __conditions.AsArray().Slice(info.conditionStartIndex, info.conditionCount),
            commands,
            info.label, 
            ref conditionBits,
            false);
    }

    public readonly bool Update(
        in GameItemManager.Hierarchy itemManager, 
        in GameItemHandle itemHandle, 
        int index, 
        ref int conditionBits)
    {
        GameQuestCommandCondition command;
        command.type = GameQuestConditionType.Get;
        
        NativeList<GameQuestCommandCondition> commands = default;
        
        var info = __infos[index];

        var conditions = __conditions.AsArray().Slice(info.conditionStartIndex, info.conditionCount);

        foreach (var condition in conditions)
        {
            if(condition.type != GameQuestConditionType.Get)
                continue;

            command.count = itemManager.CountOf(itemHandle, condition.index);
            if (command.count < 1)
                continue;

            command.index = condition.index;
            command.label = info.label;

            if (!commands.IsCreated)
                commands = new NativeList<GameQuestCommandCondition>(Allocator.Temp);
            
            commands.Add(command);
        }

        bool result;
        if (commands.IsCreated)
        {
            result = GameQuestUtility.Update(
                conditions,
                commands.AsArray(),
                info.label, 
                ref conditionBits,
                true);

            commands.Dispose();
        }
        else
            result = false;

        return result;
    }
    
    public readonly bool Finish(
        int questIndex,
        ref DynamicBuffer<GameQuest> quests)
    {
        int index = GameQuestUtility.IndexOf(questIndex, quests);
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
        ref NativeQueue<GameQuestItem>.ParallelWriter items,
        out int money)
    {
        money = 0;

        int index = GameQuestUtility.IndexOf(questIndex, quests);
        if (index == -1)
            return false;

        var quest = quests[index];
        if (quest.status == GameQuestStatus.Complete)
            return false;

        if (!IsClear(quest))
            return false;

        quest.status = GameQuestStatus.Complete;
        quests[index] = quest;

        var info = __infos[quest.index];
        __Reward(
            info.rewardStartIndex, 
            info.rewardCount, 
            itemHandle, 
            ref quests, 
            ref formulaCommands, 
            ref items,
            out money);
        
        return true;
    }

    public readonly bool Select(
        in GameItemHandle itemHandle,
        in NativeArray<GameQuestCommandCondition> commands, 
        ref DynamicBuffer<GameQuest> quests,
        ref DynamicBuffer<GameFormulaCommand> formulaCommands,
        ref NativeQueue<GameQuestItem>.ParallelWriter items,
        out int money)
    {
        bool result = false;
        int numQuests = quests.Length;
        for(int i = 0; i < numQuests; ++i)
        {
            ref var quest = ref quests.ElementAt(i);
            if(quest.status != GameQuestStatus.Normal)
                continue;

            result = Update(commands, quest.index, ref quest.conditionBits) || result;
        }

        money = 0;
        
        int temp;
        foreach (var command in commands)
        {
            if(command.type != GameQuestConditionType.Select)
                continue;

            ref var option = ref __options.ElementAt(command.index);
            __Reward(
                option.rewardStartIndex,
                option.rewardCount, 
                itemHandle,
                ref quests, 
                ref formulaCommands,
                ref items, 
                out temp);

            money += temp;
        }

        return result;
    }

    private readonly void __Reward(
        int rewardStartIndex, 
        int rewardCount, 
        in GameItemHandle itemHandle, 
        ref DynamicBuffer<GameQuest> quests,
        ref DynamicBuffer<GameFormulaCommand> formulaCommands,
        ref NativeQueue<GameQuestItem>.ParallelWriter items,
        out int money)
    {
        money = 0;

        int i, j;
        GameQuestRewardData reward;
        for (i = 0; i < rewardCount; ++i)
        {
            reward = __rewards[rewardStartIndex + i];
            switch (reward.type)
            {
                case GameQuestRewardType.Quest:
                    if (GameQuestUtility.IndexOf(reward.index, quests) == -1)
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
                    GameQuestItem item;
                    item.type = reward.index;
                    item.count = reward.count;
                    item.handle = itemHandle;

                    items.Enqueue(item);
                    break;
                case GameQuestRewardType.Money:
                    money += reward.count;
                    break;
            }
        }
    }
}

[EntityDataTypeName("GameMissionManager")]
[NativeContainer]
public struct GameQuestManager
{
    [NativeDisableUnsafePtrRestriction]
    internal unsafe GameQuestManagerData* _data;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
    internal AtomicSafetyHandle m_Safety;

    internal static readonly SharedStatic<int> StaticSafetyID = SharedStatic<int>.GetOrCreate<GameQuestManager>();
#endif

    public unsafe bool isCreated => _data != null;

    public unsafe GameQuestManager(in AllocatorManager.AllocatorHandle allocator)
    {
        _data = AllocatorManager.Allocate<GameQuestManagerData>(allocator);
        *_data = new GameQuestManagerData(allocator);

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

        var allocator = _data->allocator;
        _data->Dispose();
        AllocatorManager.Free(allocator, _data);
        _data = null;
    }

    public unsafe void Reset(GameQuestData[] datas, GameQuestOption[] options)
    {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif

        _data->Reset(datas, options);
    }

    public readonly unsafe bool IsClear(in GameQuest quest)
    {
        __CheckRead();

        return _data->IsClear(quest);
    }

    public readonly unsafe NativeSlice<GameQuestConditionData> GetConditions(int index)
    {
        __CheckRead();

        var conditions = _data->GetConditions(index);
        
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        NativeSliceUnsafeUtility.SetAtomicSafetyHandle(ref conditions, m_Safety);
#endif

        return conditions;
    }

    public readonly unsafe bool Update(
        in NativeArray<GameQuestCommandCondition> conditions,
        int index,
        ref int conditionBits)
    {
        __CheckRead();

        return _data->Update(conditions, index, ref conditionBits);
    }

    public readonly unsafe bool Update(
        in GameItemManager.Hierarchy itemManager,
        in GameItemHandle itemHandle,
        int index,
        ref int conditionBits)
    {
        __CheckRead();

        return _data->Update(itemManager, itemHandle, index, ref conditionBits);
    }

    public readonly unsafe bool Finish(
        int questIndex, 
        ref DynamicBuffer<GameQuest> quests)
    {
        __CheckRead();

        return _data->Finish(questIndex, ref quests);
    }

    public readonly unsafe bool Complete(
        int questIndex,
        in GameItemHandle itemHandle,
        ref DynamicBuffer<GameQuest> quests,
        ref DynamicBuffer<GameFormulaCommand> formulaCommands,
        ref NativeQueue<GameQuestItem>.ParallelWriter items,
        out int money)
    {
        __CheckRead();

        return _data->Complete(
            questIndex, 
            itemHandle, 
            ref quests, 
            ref formulaCommands, 
            ref items, 
            out money);
    }

    public readonly unsafe bool Select(
        in GameItemHandle itemHandle,
        in NativeArray<GameQuestCommandCondition> commands,
        ref DynamicBuffer<GameQuest> quests,
        ref DynamicBuffer<GameFormulaCommand> formulaCommands,
        ref NativeQueue<GameQuestItem>.ParallelWriter items,
        out int money)
    {
        __CheckRead();
        
        return _data->Select(
            itemHandle, 
            commands, 
            ref quests, 
            ref formulaCommands, 
            ref items, 
            out money);
    }

    [System.Diagnostics.Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
    readonly void __CheckRead()
    {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif
    }
}

/*public struct GameQuestManagerShared
{
    private struct Data
    {
        public GameQuestManagerData value;

        public LookupJobManager lookupJobManager;
    }
    
    [NativeDisableUnsafePtrRestriction]
    private unsafe Data* __data;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
    internal AtomicSafetyHandle m_Safety;
#endif

    public unsafe ref LookupJobManager lookupJobManager => ref __data->lookupJobManager;

    public unsafe GameQuestManager value
    {
        get
        {
            GameQuestManager result;
            result._data = (GameQuestManagerData*)UnsafeUtility.AddressOf(ref __data->value);
            
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            result.m_Safety = m_Safety;
#endif

            return result;
        }
    }
    
    
    public unsafe bool isCreated => __data != null;

    public unsafe GameQuestManagerShared(in AllocatorManager.AllocatorHandle allocator)
    {
        __data = AllocatorManager.Allocate<Data>(allocator);
        __data->value = new GameQuestManagerData(allocator);
        __data->lookupJobManager = new LookupJobManager();

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        m_Safety = CollectionHelper.CreateSafetyHandle(allocator);

        CollectionHelper.SetStaticSafetyId<GameQuestManager>(ref m_Safety, ref GameQuestManager.StaticSafetyID.Data);
#endif
    }

    public unsafe void Dispose()
    {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        CollectionHelper.DisposeSafetyHandle(ref m_Safety);
#endif

        var allocator = __data->value.allocator;
        __data->value.Dispose();
        AllocatorManager.Free(allocator, __data);
        __data = null;
    }

    public unsafe void Reset(GameQuestData[] datas)
    {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif

        __data->value.Reset(datas);
    }

    public GameQuestManager AsReadWrite()
    {
        lookupJobManager.CompleteReadWriteDependency();

        return value;
    }
}*/

[BurstCompile, CreateAfter(typeof(GameItemSystem)), 
 UpdateInGroup(typeof(GameItemSystemGroup), OrderFirst = true), 
 UpdateBefore(typeof(GameFormulaSystem))]
public partial struct GameQuestSystem : ISystem
{
    private struct Command
    {
        [ReadOnly] 
        public GameItemManager.Hierarchy itemManager;
        
        [ReadOnly]
        public GameQuestManager manager;

        [ReadOnly]
        public NativeArray<GameItemRoot> itemRoots;

        public BufferAccessor<GameQuest> quests;

        public BufferAccessor<GameQuestCommand> commands;

        public BufferAccessor<GameQuestCommandCondition> commandConditions;

        public BufferAccessor<GameFormulaCommand> formulaCommands;

        public NativeQueue<GameQuestItem>.ParallelWriter items;

        public void Execute(int index, out int money, out bool hasFormulaCommand)
        {
            var quests = this.quests[index];

            var formulaCommands = this.formulaCommands[index];
            int numFormulaCommands = formulaCommands.Length;

            money = 0;

            bool needToCommandAgain = false;
            var itemHandle = itemRoots[index].handle;
            var commands = this.commands[index];
            if (commands.Length > 0)
            {
                int numQuests = quests.Length;
                for (int i = 0; i < numQuests; ++i)
                {
                    ref var quest = ref quests.ElementAt(i);
                    if(quest.status == GameQuestStatus.Normal)
                        manager.Update(itemManager, itemHandle, quest.index, ref quest.conditionBits);
                }

                int moneyTemp;
                foreach (var command in commands)
                {
                    if (__Submit(itemHandle, command, ref formulaCommands, ref quests, out moneyTemp))
                        money += moneyTemp;
                    else
                        needToCommandAgain = true;
                }
            }

            bool isAnyQuestUpdate = false;

            var commandConditions = this.commandConditions[index];
            if (!commandConditions.IsEmpty)
            {
                /*GameQuest quest;
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
                }*/

                isAnyQuestUpdate = manager.Select(
                    itemHandle,
                    commandConditions.AsNativeArray(),
                    ref quests,
                    ref formulaCommands,
                    ref items,
                    out int moneyTemp);

                money += moneyTemp;

                commandConditions.Clear();
            }

            if (needToCommandAgain && isAnyQuestUpdate)
            {
                foreach (var command in commands)
                {
                    __Submit(itemHandle, command, ref formulaCommands, ref quests, out int moneyTemp);

                    money += moneyTemp;
                }
            }

            commands.Clear();

            hasFormulaCommand = numFormulaCommands != formulaCommands.Length;
        }

        private bool __Submit(
            //int index, 
            in GameItemHandle itemHandle, 
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
                        itemHandle,
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
    private struct CommandEx : IJobChunk
    {
        [ReadOnly] 
        public GameItemManager.Hierarchy itemManager;
        [ReadOnly]
        public GameQuestManager manager;
        [ReadOnly]
        public ComponentTypeHandle<GameItemRoot> itemRootType;

        public ComponentTypeHandle<GameMoney> monyType;

        public BufferTypeHandle<GameQuestCommand> commandType;

        public BufferTypeHandle<GameQuest> questType;
        public BufferTypeHandle<GameQuestCommandCondition> commandConditionType;

        public BufferTypeHandle<GameFormulaCommand> formulaCommandType;

        public NativeQueue<GameQuestItem>.ParallelWriter items;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Command command;
            command.itemManager = itemManager;
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
        public NativeQueue<GameQuestItem> items;

        public void Execute()
        {
            int count, parentChildIndex;
            GameItemHandle parentHandle;
            while (items.TryDequeue(out var item))
            {
                if (item.count < 0)
                    itemManager.Remove(item.handle, item.type, -item.count);
                else
                {
                    if (!itemManager.Find(item.handle, item.type, item.count, out parentChildIndex, out parentHandle) &&
                        itemManager.Find(item.handle, item.type, 1, out parentChildIndex, out parentHandle))
                        continue;

                    count = item.count;
                    itemManager.Add(parentHandle, parentChildIndex, item.type, ref count);
                }
            }
        }
    }

    private EntityQuery __group;
    private GameItemManagerShared __itemManager;
    private NativeQueue<GameQuestItem> __items;

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

        __items = new NativeQueue<GameQuestItem>(Allocator.Persistent);

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
        CommandEx command;
        command.itemManager = __itemManager.hierarchy;
        command.manager = manager;
        command.itemRootType = __itemRootType.UpdateAsRef(ref state);
        command.monyType = __monyType.UpdateAsRef(ref state);
        command.commandType = __commandType.UpdateAsRef(ref state);
        command.questType = __questType.UpdateAsRef(ref state);
        command.commandConditionType = __commandConditionType.UpdateAsRef(ref state);
        command.formulaCommandType = __formulaCommandType.UpdateAsRef(ref state);
        command.items = __items.AsParallelWriter();

        ref var itemManagerJobManager = ref __itemManager.lookupJobManager;

        var jobHandle = command.ScheduleParallelByRef(__group,  JobHandle.CombineDependencies(itemManagerJobManager.readWriteJobHandle, state.Dependency));

        ApplyItems applyItems;
        applyItems.itemManager = __itemManager.value;
        applyItems.items = __items;

        jobHandle = applyItems.ScheduleByRef(jobHandle);

        itemManagerJobManager.readWriteJobHandle = jobHandle;

        state.Dependency = jobHandle;
    }
}

public static class GameQuestUtility
{
    public static bool IsClear(
        in this GameQuest quest, 
        in NativeSlice<GameQuestConditionData> conditions)
    {
        if (quest.status != GameQuestStatus.Normal)
            return true;

        int numConditions = conditions.Length;
        if (quest.conditionBits == 0)
            return numConditions == 0;

        int bitOffset = 0, bitCount, bitMask, count;
        for (int i = 0; i < numConditions; ++i)
        {
            count = conditions[i].count;

            bitCount = 32 - math.lzcnt(count);
            bitMask = (1 << bitCount) - 1;
            if (((quest.conditionBits >> bitOffset) & bitMask) < count)
                return false;

            bitOffset += bitCount;
        }

        return true;
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
        in NativeArray<GameQuestCommandCondition> conditions,
        in FixedString label, 
        GameQuestConditionType type,
        int index,
        int bitCount,
        int bitOffset,
        int maxCount,
        ref int conditionBits,
        bool isOverride)
    {
        bool result = false;
        int length = conditions.Length, originCount, count, bitMask;
        GameQuestCommandCondition condition;
        for (int i = 0; i < length; ++i)
        {
            condition = conditions[i];
            if (condition.type == type && condition.index == index && 
                (condition.label.IsEmpty || condition.label == label))
            {
                count = condition.count;

#if !GAME_QUEST
                count = math.max(count, 0);
#endif

                if (count != 0)
                {
                    bitMask = (1 << bitCount) - 1;
                    originCount = (conditionBits >> bitOffset) & bitMask;
                    
                    if (!isOverride)
                        count += originCount;
                    
                    count = math.clamp(count, 0, maxCount);
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

    public static bool Update(
        in NativeSlice<GameQuestConditionData> conditions, 
        in NativeArray<GameQuestCommandCondition> commands,
        in FixedString label, 
        ref int conditionBits, 
        bool isOverride)
    {
        bool result = false;
        int numConditions = conditions.Length, bitOffset = 0, bitCount;
        GameQuestConditionData condition;
        for (int i = 0; i < numConditions; ++i)
        {
            condition = conditions[i];
            
            bitCount = 32 - math.lzcnt(condition.count);

            result = Update(
                commands,
                label, 
                condition.type,
                condition.index,
                bitCount,
                bitOffset,
                condition.count,
                ref conditionBits, 
                isOverride) || result;

            bitOffset += bitCount;

            UnityEngine.Assertions.Assert.IsFalse(bitOffset > 12);
        }

        return result;
    }
}