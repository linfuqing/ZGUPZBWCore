using System;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using ZG;
using Random = Unity.Mathematics.Random;

[assembly: RegisterGenericJobType(typeof(CopyNativeArrayToComponentData<GameFormulaFactoryTime>))]

public struct GameFormulaFactoryDefinition
{
    public struct Formula
    {
        public struct Result
        {
            public int level;

            public int itemCount;

            public int itemType;

            public float chance;

            public BlobArray<int> childSiblingIndices;
            public BlobArray<int> itemSiblingTypes;
        }

        public struct Child
        {
            public int itemType;

            public int itemCount;
        }

        public int capacity;
        public int cost;
        public float time;
        public BlobArray<Result> results;
        public BlobArray<Child> children;
        public BlobArray<int> parentItemTypes;

        public int GetResultIndex(int level, float chance)
        {
            int numResults = results.Length;
            if (numResults > 0)
            {
                int i;
                for (i = 0; i < numResults; ++i)
                {
                    ref var result = ref results[i];
                    if (result.level != 0 && result.level != level)
                        continue;

                    if (result.chance > chance)
                        return i;
                    else
                        chance -= result.chance;
                }
            }

            return -1;
        }

        public bool Test(
            ref GameItemHandle handle,
            int count, 
            in Entity source,
            in Entity destination,
            in ComponentLookup<GameMoney> moneys,
            in ComponentLookup<GameItemRoot> itemRoots, 
            in GameItemManager.ReadOnly itemManager)
        {
            if (cost > 0 && cost > moneys[source].value)
                return false;

            int numChildren = children.Length;
            if (numChildren > 0)
            {
                var parentItemTypes = this.parentItemTypes.AsArray();
                var entityHandle = GameItemHandle.Empty;
                int itemCount;
                bool isContains = true;
                for (int i = 0; i < numChildren; ++i)
                {
                    ref var child = ref children[i];

                    itemCount = child.itemCount * count;
                    if (!itemManager.Contains(handle, child.itemType, ref itemCount, parentItemTypes))
                    {
                        if (entityHandle.Equals(GameItemHandle.Empty) && source != destination)
                            entityHandle = itemRoots[source].handle;

                        if (!itemManager.Contains(entityHandle, child.itemType, ref itemCount))
                            return false;
                    }
                }
            }

            return true;
        }
    }

    public struct Item
    {
        public struct Formula
        {
            public int index;
            public int level;
        }

        public float timeScale;

        public BlobArray<Formula> formulas;
    }

    public BlobArray<Formula> values;
    public BlobArray<Item> items;
}

public struct GameFormulaFactoryData : IComponentData
{
    public BlobAssetReference<GameFormulaFactoryDefinition> definition;
}

/*public struct GameFormulaFactorySharedData : IComponentData
{
    public BlobAssetReference<GameFormulaFactoryDefinition> definition;
}*/

public struct GameFormulaFactoryMode : IComponentData
{
    public enum Mode
    {
        Normal,
        Auto,
        Once,
        Repeat,
        Force
    }

    public enum OwnerType
    {
        User, 
        Factory
    }

    public Mode value;
    public OwnerType ownerType;
}

public struct GameFormulaFactoryItemTimeScale : IComponentData
{
    public float value;
}

public struct GameFormulaFactoryTimeScale : IComponentData
{
    public float value;
}

public struct GameFormulaFactoryEntity : IBufferElementData
{
    public Entity value;
}

[EntityDataTypeName("GameFactory")]
public struct GameFormulaFactoryStatus : IComponentData
{
    public enum Status
    {
        Normal,
        Running,
        Completed
    }

    public Status value;

    public int formulaIndex;

    public int level;

    public int count;

    public int usedCount;

    public Entity entity;
}

public struct GameFormulaFactoryTime : IComponentData, IEnableableComponent
{
    public float value;
}

public struct GameFormulaFactoryInstance : IBufferElementData, IEnableableComponent
{
    public int formulaIndex;
    public int level;
    public int count;

    public Entity entity;

    public static int IndexOf(int formulaIndex, int startIndex, in DynamicBuffer<GameFormulaFactoryInstance> instances)
    {
        int numInstances = instances.Length;
        for(int i = startIndex; i < numInstances; ++i)
        {
            if (instances[i].formulaIndex == formulaIndex)
                return i;
        }

        return -1;
    }
}

public struct GameFormulaFactoryCommand : IBufferElementData, IEnableableComponent
{
    public Entity entity;

    public int formulaIndex;
    public int count;
}

/*[BurstCompile, UpdateInGroup(typeof(EndFrameEntityCommandSystemGroup))]
public partial struct GameFormulaFactoryStructChangeSystem : ISystem
{
    [BurstCompile]
    private struct Init : IJobParallelFor
    {
        public BlobAssetReference<GameFormulaFactoryDefinition> definition;

        [ReadOnly, DeallocateOnJobCompletion]
        public NativeArray<Entity> entityArray;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<GameFormulaFactoryData> instances;

        public void Execute(int index)
        {
            GameFormulaFactoryData instance;
            instance.definition = definition;
            instances[entityArray[index]] = instance;
        }
    }

    public static readonly int InnerloopBatchCount = 1;

    private EntityQuery __definitionGroup;
    private EntityQuery __instanceGroup;

    private EntityCommandPool<Entity>.Context __removeTimePool;
    private EntityAddComponentPool<GameFormulaFactoryTime> __addTimePool;

    public EntityCommandPool<Entity> removeTimeComponentPool => __removeTimePool.pool;

    public EntityCommandPool<EntityData<GameFormulaFactoryTime>> addTimeComponentPool => __addTimePool.value;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        BurstUtility.InitializeJobParallelFor<Init>();

        using(var builder = new EntityQueryBuilder(Allocator.Temp))
            __definitionGroup = builder
                .WithAll<GameFormulaFactorySharedData>()
                .Build(ref state);


        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __instanceGroup = builder
                .WithAll<GameFormulaFactoryStatus>()
                .WithNone<GameFormulaFactoryData>()
                .WithOptions(EntityQueryOptions.IncludeDisabledEntities)
                .Build(ref state);

        __removeTimePool = new EntityCommandPool<Entity>.Context(Allocator.Persistent);

        __addTimePool = new EntityAddComponentPool<GameFormulaFactoryTime>(Allocator.Persistent);
    }

    //[BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        __removeTimePool.Dispose();

        __addTimePool.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (__definitionGroup.IsEmpty)
            return;

        NativeArray<Entity> instanceEntities;
        if (__instanceGroup.IsEmptyIgnoreFilter)
            instanceEntities = default;
        else
        {
            instanceEntities = __instanceGroup.ToEntityArray(Allocator.TempJob);

            state.EntityManager.AddComponent<GameFormulaFactoryData>(__instanceGroup);
        }

        if (!__removeTimePool.isEmpty)
        {
            using (var container = new EntityCommandEntityContainer(Allocator.Temp))
            {
                __removeTimePool.MoveTo(container);

                container.RemoveComponent<GameFormulaFactoryTime>(ref state);
            }
        }

        if(!__addTimePool.isEmpty)
            __addTimePool.Playback(InnerloopBatchCount, ref state);

        if (instanceEntities.IsCreated)
        {
            Init init;
            init.definition = __definitionGroup.GetSingleton<GameFormulaFactorySharedData>().definition;
            init.entityArray = instanceEntities;
            init.instances = state.GetComponentLookup<GameFormulaFactoryData>();

            state.Dependency = init.Schedule(instanceEntities.Length, InnerloopBatchCount, state.Dependency);
        }
    }
}*/

[BurstCompile, CreateAfter(typeof(GameItemSystem))/*, CreateAfter(typeof(GameFormulaFactoryStructChangeSystem))*/]
public partial struct GameFormulaFactorySystem : ISystem
{
    public struct Result
    {
        public int formulaIndex;
        public GameItemHandle handle;
        public Entity entity;
        public Entity factory;
    }

    [Flags]
    private enum RunningStatus
    {
        Command = 0x01, 
        Stop = 0x02
    }

    private struct CompletedResult
    {
        public int count;

        public int formulaIndex;

        public int resultIndex;

        public int parentChildIndex;

        public GameItemHandle parentHandle;

        public GameItemHandle handle;

        public Entity entity;
        public Entity factory;
        public Entity owner;
    }

    private struct RunningResult
    {
        public int formulaIndex;

        public int count;

        //public GameItemHandle handle;

        public Entity entity;
        public Entity factory;
        public Entity owner;
    }

    /*[BurstCompile]
    private struct Resize : IJob
    {
        [ReadOnly, DeallocateOnJobCompletion]
        public NativeArray<int> count;

        public NativeList<CompletedResult> completedResults;

        public NativeList<RunningResult> runningResults;

        public void Execute()
        {
            int count = this.count[0];
            completedResults.Capacity = math.max(completedResults.Capacity, completedResults.Length + count);
            runningResults.Capacity = math.max(runningResults.Capacity, runningResults.Length + count);
        }
    }*/

    private struct Run
    {
        public float deltaTime;

        public Random random;

        public BlobAssetReference<GameFormulaFactoryDefinition> definition;

        [ReadOnly]
        public GameItemManager.ReadOnly itemManager;

        [ReadOnly]
        public NativeArray<Entity> entityArray;

        [ReadOnly]
        public BufferAccessor<GameFormulaFactoryEntity> factoryEntities;

        [ReadOnly]
        public ComponentLookup<GameItemRoot> itemRootMap;

        [ReadOnly]
        public NativeArray<GameItemRoot> itemRoots;

        //[ReadOnly]
        //public NativeArray<GameFormulaFactoryData> instances;

        [ReadOnly]
        public NativeArray<GameFormulaFactoryMode> modes;

        [ReadOnly]
        public NativeArray<GameFormulaFactoryStatus> states;

        [ReadOnly]
        public NativeArray<GameFormulaFactoryTimeScale> timeScales;

        public NativeArray<GameFormulaFactoryItemTimeScale> itemTimeScales;

        public NativeArray<GameFormulaFactoryTime> times;

        public BufferAccessor<GameFormulaFactoryCommand> commands;

        public BufferAccessor<GameFormulaFactoryInstance> instances;

        [NativeDisableContainerSafetyRestriction]
        public ComponentLookup<GameFormulaFactoryTimeScale> timeScaleMap;

        [NativeDisableContainerSafetyRestriction]
        public ComponentLookup<GameFormulaFactoryStatus> statusMap;

        //public EntityCommandQueue<Entity>.ParallelWriter entityManager;

        public NativeQueue<CompletedResult>.ParallelWriter results;

        public RunningStatus Execute(int index)
        {
            float timeScale = 0.0f;// timeScales[index].value;
            var handle = itemRoots[index].handle;
            var hierarchy = itemManager.hierarchy;
            ref var definition = ref this.definition.Value;
            if (hierarchy.TryGetValue(handle, out var item))
            {
                var siblingHandle = item.siblingHandle;
                while (hierarchy.GetChildren(siblingHandle, out var enumerator, out item))
                {
                    siblingHandle = item.siblingHandle;
                    while (enumerator.MoveNext())
                    {
                        if(hierarchy.TryGetValue(enumerator.Current.handle, out item))
                            timeScale += definition.items[item.type].timeScale;
                    }
                }
            }

            Entity factory = entityArray[index];

            var itemTimeScale = itemTimeScales[index];
            if (itemTimeScale.value == timeScale)
                timeScale = timeScales[index].value;
            else
            {
                timeScale -= itemTimeScale.value;

                itemTimeScale.value += timeScale;

                itemTimeScales[index] = itemTimeScale;

                var temp = timeScales[index];
                temp.value += timeScale;

                timeScale = temp.value;

                timeScaleMap[factory] = temp;
            }

            RunningStatus result = 0;
            var time = times[index];
            time.value -= deltaTime * timeScale;
            times[index] = time;
            if (time.value < math.FLT_MIN_NORMAL)
            {
                var status = states[index];
                if (status.formulaIndex == -1)
                    result |= RunningStatus.Stop;
                else
                {
                    ref var formula = ref definition.values[status.formulaIndex];

                    if (status.value == GameFormulaFactoryStatus.Status.Running)
                    {
                        Entity entity = factory, owner;
                        var mode = modes[index];
                        switch (mode.ownerType)
                        {
                            case GameFormulaFactoryMode.OwnerType.User:
                                owner = status.entity;
                                break;
                            case GameFormulaFactoryMode.OwnerType.Factory:
                                owner = factory;
                                break;
                            default:
                                owner = Entity.Null;
                                break;
                        }

                        switch (mode.value)
                        {
                            case GameFormulaFactoryMode.Mode.Normal:
                            case GameFormulaFactoryMode.Mode.Auto:
                                var factoryEntities = this.factoryEntities[index];
                                if (factoryEntities.Length > 0)
                                {
                                    if (mode.value == GameFormulaFactoryMode.Mode.Normal)
                                    {
                                        foreach (var factoryEntity in factoryEntities)
                                        {
                                            if (factoryEntity.value == status.entity)
                                            {
                                                entity = status.entity;

                                                handle = itemRootMap[status.entity].handle;

                                                break;
                                            }
                                        }
                                    }

                                    if (!Complete(
                                            false, 
                                            status.formulaIndex, 
                                            status.level, 
                                            status.count - status.usedCount, 
                                            entity, 
                                            factory, 
                                            owner, 
                                            handle, 
                                            ref formula))
                                        return 0;

                                    if (mode.value == GameFormulaFactoryMode.Mode.Auto)
                                    {
                                        Command(index, status.formulaIndex, status.count, factory);

                                        result |= RunningStatus.Command;
                                    }
                                }
                                else
                                    status.value = GameFormulaFactoryStatus.Status.Completed;
                                break;
                            case GameFormulaFactoryMode.Mode.Once:
                                if (!Complete(
                                        true, 
                                        status.formulaIndex, 
                                        status.level, 
                                        status.count - status.usedCount, 
                                        factory, 
                                        factory, 
                                        owner, 
                                        handle, 
                                        ref formula))
                                    return 0;

                                //factoryStatus = GameFactoryStatus.Complete;
                                break;
                            case GameFormulaFactoryMode.Mode.Repeat:
                                if (!Complete(
                                        false, 
                                        status.formulaIndex, 
                                        status.level, 
                                        status.count - status.usedCount, 
                                        factory, 
                                        factory, 
                                        owner, 
                                        handle, 
                                        ref formula))
                                    return 0;

                                Command(
                                    index, 
                                    status.formulaIndex, 
                                    status.count, 
                                    factory);

                                result |= RunningStatus.Command;
                                break;
                            case GameFormulaFactoryMode.Mode.Force:
                                int count = status.count - status.usedCount;
                                if (!Complete(
                                        true, 
                                        status.formulaIndex, 
                                        status.level, 
                                        count, 
                                        factory, 
                                        factory,
                                        owner, 
                                        handle, 
                                        ref formula))
                                    return 0;

                                if (formula.Test(
                                    ref handle,
                                    count,
                                    factory,
                                    factory,
                                    default,
                                    default, 
                                    itemManager))
                                    return 0;

                                Command(index, status.formulaIndex, status.count, factory);

                                result |= RunningStatus.Command;
                                break;
                            default:
                                return 0;// throw new InvalidOperationException();
                        }
                    }

                    var instances = this.instances[index];
                    if (instances.Length > 0)
                    {
                        ref var instance = ref instances.ElementAt(0);

                        if (status.value == GameFormulaFactoryStatus.Status.Completed)
                        {
                            /*if (instance.formulaIndex == status.formulaIndex &&
                                instance.level == status.level &&
                                instance.entity == status.entity &&
                                status.count < formula.capacity)
                            {
                                ++status.count;

                                instances.RemoveAt(0);

                                time.value = formula.time;
                                times[index] = time;

                                status.value = GameFormulaFactoryStatus.Status.Running;
                            }
                            else*/
                                result |= RunningStatus.Stop;
                        }
                        else
                        {
                            status.formulaIndex = instance.formulaIndex;
                            status.level = instance.level;
                            status.count = instance.count;
                            status.entity = instance.entity;

                            instances.RemoveAt(0);

                            time.value = definition.values[status.formulaIndex].time;
                            times[index] = time;
                        }
                    }
                    else
                    {
                        if (status.value == GameFormulaFactoryStatus.Status.Running)
                        {
                            status.value = GameFormulaFactoryStatus.Status.Normal;

                            status.usedCount = status.count;
                        }

                        result |= RunningStatus.Stop;
                    }

                    statusMap[factory] = status;

                    //entityManager.Enqueue(entity);
                }
            }

            return result;
        }

        public bool Complete(
            bool isForce, 
            int formulaIndex, 
            int level, 
            int count, 
            in Entity entity, 
            in Entity factory, 
            in Entity owner, 
            in GameItemHandle handle, 
            ref GameFormulaFactoryDefinition.Formula formula)
        {
            CompletedResult result;
            result.resultIndex = formula.GetResultIndex(level, random.NextFloat());
            if (result.resultIndex == -1)
                return false;

            //++count;

            ref var formulaResult = ref formula.results[result.resultIndex];

            result.handle = handle;
            if (!itemManager.Find(
                result.handle,
                formulaResult.itemType,
                formulaResult.itemCount * count,
                out result.parentChildIndex,
                out result.parentHandle))
            {
                if(!isForce)
                    return false;
            }

            result.count = count;
            result.formulaIndex = formulaIndex;

            result.entity = entity;
            result.factory = factory;
            result.owner = owner;

            results.Enqueue(result);

            return true;
        }

        public void Command(int index, int formulaIndex, int count, in Entity entity)
        {
            GameFormulaFactoryCommand command;
            command.entity = entity;
            command.formulaIndex = formulaIndex;
            command.count = count;
            commands[index].Add(command);
        }
    }

    [BurstCompile]
    private struct RunEx : IJobChunk, IEntityCommandProducerJob
    {
        public float deltaTime;
        public uint hash;

        public BlobAssetReference<GameFormulaFactoryDefinition> definition;

        public GameItemManager.ReadOnly itemManager;

        [ReadOnly]
        public EntityTypeHandle entityType;

        [ReadOnly]
        public BufferTypeHandle<GameFormulaFactoryEntity> factoryEntityType;

        [ReadOnly]
        public ComponentLookup<GameItemRoot> itemRoots;

        [ReadOnly]
        public ComponentTypeHandle<GameItemRoot> itemRootType;

        [ReadOnly]
        public ComponentTypeHandle<GameFormulaFactoryMode> modeType;

        [ReadOnly]
        public ComponentTypeHandle<GameFormulaFactoryStatus> statusType;

        [ReadOnly]
        public ComponentTypeHandle<GameFormulaFactoryTimeScale> timeScaleType;

        public ComponentTypeHandle<GameFormulaFactoryItemTimeScale> itemTimeScaleType;

        public ComponentTypeHandle<GameFormulaFactoryTime> timeType;

        public BufferTypeHandle<GameFormulaFactoryCommand> commandType;

        public BufferTypeHandle<GameFormulaFactoryInstance> instanceType;

        [NativeDisableContainerSafetyRestriction]
        public ComponentLookup<GameFormulaFactoryTimeScale> timeScales;

        [NativeDisableContainerSafetyRestriction]
        public ComponentLookup<GameFormulaFactoryStatus> states;

        //public EntityCommandQueue<Entity>.ParallelWriter entityManager;

        public NativeQueue<CompletedResult>.ParallelWriter results;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Run run;
            run.deltaTime = deltaTime;
            run.random = new Random(hash ^ (uint)(unfilteredChunkIndex));
            run.definition = definition;
            run.itemManager = itemManager;
            run.entityArray = chunk.GetNativeArray(entityType);
            run.factoryEntities = chunk.GetBufferAccessor(ref factoryEntityType);
            run.itemRootMap = itemRoots;
            run.itemRoots = chunk.GetNativeArray(ref itemRootType);
            run.modes = chunk.GetNativeArray(ref modeType);
            run.states = chunk.GetNativeArray(ref statusType);
            run.timeScales = chunk.GetNativeArray(ref timeScaleType);
            run.itemTimeScales = chunk.GetNativeArray(ref itemTimeScaleType);
            run.times = chunk.GetNativeArray(ref timeType);
            run.commands = chunk.GetBufferAccessor(ref commandType);
            run.instances = chunk.GetBufferAccessor(ref instanceType);
            run.timeScaleMap = timeScales;
            run.statusMap = states;
            //run.entityManager = entityManager;
            run.results = results;

            RunningStatus runningStatus;
            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
            {
                runningStatus = run.Execute(i);
                if ((runningStatus & RunningStatus.Command) == RunningStatus.Command)
                    chunk.SetComponentEnabled(ref commandType, i, true);

                if ((runningStatus & RunningStatus.Stop) == RunningStatus.Stop)
                    chunk.SetComponentEnabled(ref timeType, i, false);
            }
        }
    }

    private struct Complete
    {
        public Random random;

        public BlobAssetReference<GameFormulaFactoryDefinition> definition;

        [ReadOnly]
        public GameItemManager.ReadOnly itemManager;

        [ReadOnly]
        public BufferLookup<GameFormula> formulas;

        [ReadOnly]
        public ComponentLookup<GameMoney> moneys;

        [ReadOnly]
        public ComponentLookup<GameItemRoot> itemRootMap;

        [ReadOnly]
        public NativeArray<Entity> entityArray;

        [ReadOnly]
        public NativeArray<GameItemRoot> itemRoots;

        [ReadOnly]
        public NativeArray<GameFormulaFactoryMode> modes;

        [ReadOnly]
        public NativeArray<GameFormulaFactoryStatus> states;

        public NativeArray<GameFormulaFactoryTime> times;

        public BufferAccessor<GameFormulaFactoryCommand> commands;

        public BufferAccessor<GameFormulaFactoryInstance> instances;

        [NativeDisableContainerSafetyRestriction]
        public ComponentLookup<GameFormulaFactoryStatus> statusMap;

        //public EntityCommandQueue<EntityData<GameFormulaFactoryTime>>.ParallelWriter entityManager;

        public NativeQueue<RunningResult>.ParallelWriter runningResults;

        public NativeQueue<CompletedResult>.ParallelWriter completeResults;

        public bool Execute(int index)
        {
            ref var definition = ref this.definition.Value;

            int formulaIndex, instanceIndex, count, i;
            float timeValue;
            Entity entity = entityArray[index];
            var handle = itemRoots[index].handle;
            GameFormula temp;
            CompletedResult completedResult;
            RunningResult runningResult;
            GameFormulaFactoryInstance instance;
            var mode = modes[index];
            var status = states[index];
            var time = times[index];
            var instances = this.instances[index];
            var commands = this.commands[index];
            foreach (var command in commands)
            {
                if (command.entity == Entity.Null)
                    continue;

                if (command.formulaIndex == -1)
                {
                    if (status.formulaIndex == -1)
                        continue;

                    switch (status.value)
                    {
                        case GameFormulaFactoryStatus.Status.Running:
                        case GameFormulaFactoryStatus.Status.Completed:
                            count = status.count - status.usedCount;
                            
                            UnityEngine.Assertions.Assert.AreNotEqual(0, count);
                            if (status.value == GameFormulaFactoryStatus.Status.Running)
                            {
                                count -= (int)math.ceil(time.value / definition.values[status.formulaIndex].time);
                            
                                UnityEngine.Assertions.Assert.IsTrue(count < status.count);
                            }
                            break;
                        default:
                            count = 0;
                            break;
                    }

                    if (count < 1)
                        continue;

                    ref var formula = ref definition.values[status.formulaIndex];

                    completedResult.resultIndex = formula.GetResultIndex(status.level, random.NextFloat());
                    if (completedResult.resultIndex != -1)
                    {
                        completedResult.factory = entity;
                        completedResult.entity = command.entity;
                        completedResult.owner = command.entity;
                        completedResult.handle = itemRootMap[command.entity].handle;

                        ref var formulaResult = ref formula.results[completedResult.resultIndex];

                        itemManager.Find(
                            completedResult.handle, 
                            formulaResult.itemType, 
                            formulaResult.itemCount * count, 
                            out completedResult.parentChildIndex, 
                            out completedResult.parentHandle);

                        completedResult.count = count;
                        completedResult.formulaIndex = status.formulaIndex;

                        completeResults.Enqueue(completedResult);
                    }

                    status.usedCount += count;

                    if (status.value == GameFormulaFactoryStatus.Status.Completed)
                    {
                        UnityEngine.Assertions.Assert.AreEqual(status.usedCount, status.count);
                        if (mode.value == GameFormulaFactoryMode.Mode.Auto)
                        {
                            count = status.count;

                            status.value = GameFormulaFactoryStatus.Status.Normal;
                        }
                        else
                        {
                            if (instances.Length > 0)
                            {
                                instance = instances[0];

                                instances.RemoveAt(0);
                                
                                timeValue = __CommandToRun(
                                    ref definition, 
                                    handle, 
                                    instance.formulaIndex, 
                                    instance.count, 
                                    instance.entity, 
                                    entity, 
                                    mode.ownerType);

                                if (timeValue > 0.0f)
                                {
                                    time.value = timeValue;
                                    
                                    status.value = GameFormulaFactoryStatus.Status.Running;
                                    status.formulaIndex = instance.formulaIndex;
                                    status.level = instance.level;
                                    status.count = instance.count;
                                    status.usedCount = 0;
                                    status.entity = instance.entity;
                                }
                            }
                            else
                            {
                                status.value = GameFormulaFactoryStatus.Status.Normal;

                                time.value = 0.0f;
                            }

                            statusMap[entity] = status;

                            times[index] = time;
                            
                            continue;
                            //return time;
                        }
                    }
                    else
                    {
                        statusMap[entity] = status;

                        continue;
                        //return 0.0f;
                    }

                    formulaIndex = status.formulaIndex;
                }
                else// if (status.value == GameFormulaFactoryStatus.Status.Normal)
                {
                    count = command.count;

                    formulaIndex = command.formulaIndex;
                    //entity = entityArray[index];
                }
                /*else
                    return 0.0f;*/

                if (count > 0)
                {
                    if (GameFormulaManager.IndexOf(formulaIndex, formulas[command.entity], out temp) == -1)
                        temp = default;

                    if (instances.Length > 0)
                    {
                        instance.formulaIndex = formulaIndex;
                        instance.level = temp.level;
                        instance.count = count;
                        instance.entity = command.entity;
                        instances.Add(instance);
                        
                        instance = instances[0];
                        instances.RemoveAt(0);
                    }
                    else
                    {
                        instance.formulaIndex = formulaIndex;
                        instance.level = temp.level;
                        instance.count = count;
                        instance.entity = command.entity;
                    }

                    if (instance.formulaIndex != status.formulaIndex ||
                        instance.level != status.level ||
                        instance.entity != status.entity)
                    {
                        if (status.usedCount < status.count)
                        {
                            instances.Insert(0, instance);

                            continue;
                        }
                        else
                        {
                            status.formulaIndex = instance.formulaIndex;
                            status.level = instance.level;
                            status.entity = instance.entity;

                            status.count = 0;
                            status.usedCount = 0;
                        }
                    }

                    timeValue = __CommandToRun(
                        ref definition, 
                        handle, 
                        instance.formulaIndex, 
                        instance.count, 
                        instance.entity, 
                        entity, 
                        mode.ownerType);

                    if (timeValue > 0.0f)
                    {
                        status.count += instance.count;
                        //if (status.value != GameFormulaFactoryStatus.Status.Running)
                        status.value = GameFormulaFactoryStatus.Status.Running;

                        statusMap[entity] = status;

                        time.value = math.max(0.0f, time.value) + timeValue;

                        times[index] = time;
                    }
                }
                else
                {
                    count = -count;
                    instanceIndex = 0;
                    for (i = 0; i < count; ++i)
                    {
                        instanceIndex = GameFormulaFactoryInstance.IndexOf(formulaIndex, instanceIndex, instances);
                        if (instanceIndex == -1)
                            break;

                        instances.RemoveAt(instanceIndex);
                    }
                }
            }

            commands.Clear();

            return time.value > 0.0f;
        }

        private float __CommandToRun(
            ref GameFormulaFactoryDefinition definition, 
            in GameItemHandle handle, 
            int formulaIndex, 
            int count, 
            in Entity entity, 
            in Entity factory, 
            GameFormulaFactoryMode.OwnerType ownerType)
        {
            ref var formula = ref definition.values[formulaIndex];

            var temp = handle;
            if (!formula.Test(
                    ref temp,
                    count,
                    entity,
                    factory,
                    moneys,
                    itemRootMap,
                    itemManager))
                return 0.0f;

            RunningResult runningResult;
            runningResult.entity = entity;
            runningResult.factory = factory;

            switch (ownerType)
            {
                case GameFormulaFactoryMode.OwnerType.User:
                    runningResult.owner = entity;
                    break;
                case GameFormulaFactoryMode.OwnerType.Factory:
                    runningResult.owner = factory;
                    break;
                default:
                    runningResult.owner = Entity.Null;
                    break;
            }

            runningResult.formulaIndex = formulaIndex;
            runningResult.count = count;
            runningResults.Enqueue(runningResult);

            return math.max(formula.time * count, math.FLT_MIN_NORMAL);
        }
    }

    [BurstCompile]
    private struct CompleteEx : IJobChunk, IEntityCommandProducerJob
    {
        public uint hash;

        public BlobAssetReference<GameFormulaFactoryDefinition> definition;

        [ReadOnly]
        public GameItemManager.ReadOnly itemManager;

        [ReadOnly]
        public EntityTypeHandle entityType;

        [ReadOnly]
        public BufferLookup<GameFormula> formulas;

        [ReadOnly]
        public ComponentLookup<GameMoney> moneys;

        [ReadOnly]
        public ComponentLookup<GameItemRoot> itemRoots;

        [ReadOnly]
        public ComponentTypeHandle<GameItemRoot> itemRootType;

        [ReadOnly]
        public ComponentTypeHandle<GameFormulaFactoryMode> modeType;

        [ReadOnly]
        public ComponentTypeHandle<GameFormulaFactoryStatus> statusType;

        public ComponentTypeHandle<GameFormulaFactoryTime> timeType;

        public BufferTypeHandle<GameFormulaFactoryCommand> commandType;

        public BufferTypeHandle<GameFormulaFactoryInstance> instanceType;

        [NativeDisableContainerSafetyRestriction]
        public ComponentLookup<GameFormulaFactoryStatus> states;

        //public EntityCommandQueue<EntityData<GameFormulaFactoryTime>>.ParallelWriter entityManager;

        public NativeQueue<RunningResult>.ParallelWriter runningResults;

        public NativeQueue<CompletedResult>.ParallelWriter completeResults;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Complete complete;
            complete.random = new Random(hash ^ (uint)unfilteredChunkIndex);
            complete.definition = definition;
            complete.itemManager = itemManager;
            complete.entityArray = chunk.GetNativeArray(entityType);
            complete.formulas = this.formulas;
            complete.moneys = moneys;
            complete.itemRootMap = itemRoots;
            complete.itemRoots = chunk.GetNativeArray(ref itemRootType);
            complete.modes = chunk.GetNativeArray(ref modeType);
            complete.states = chunk.GetNativeArray(ref statusType);
            complete.times = chunk.GetNativeArray(ref timeType);
            complete.commands = chunk.GetBufferAccessor(ref commandType);
            complete.instances = chunk.GetBufferAccessor(ref instanceType);
            complete.statusMap = states;
            //complete.entityManager = entityManager;
            complete.runningResults = runningResults;
            complete.completeResults = completeResults;

            var times = chunk.GetNativeArray(ref timeType);
            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
            {
                if(complete.Execute(i))
                    chunk.SetComponentEnabled(ref timeType, i, true);

                chunk.SetComponentEnabled(ref commandType, i, false);
            }
        }
    }

    [BurstCompile]
    private struct ApplyToRun : IJob
    {
        public BlobAssetReference<GameFormulaFactoryDefinition> definition;

        public GameItemManager itemManager;

        public NativeQueue<RunningResult> results;

        public BufferLookup<GameItemSibling> siblings;

        public BufferLookup<GameItemSpawnHandleCommand> itemSpawnHandleCommands;

        public BufferLookup<GameQuestCommandCondition> questCommandConditions;

        public ComponentLookup<GameMoney> moneys;

        [ReadOnly]
        public ComponentLookup<GameItemRoot> roots;

        [ReadOnly]
        public ComponentLookup<Rotation> rotations;

        [ReadOnly]
        public ComponentLookup<Translation> translations;

        public void Execute()
        {
            GameQuestCommandCondition questCommandCondition;
            questCommandCondition.type = GameQuestConditionType.Make;

            var itemSiblingHandles = new NativeList<GameItemHandle>(Allocator.Temp);
            var itemChildHandles = new NativeList<GameItemHandle>(Allocator.Temp);
            
            ref var definition = ref this.definition.Value;

            GameMoney money;
            GameItemInfo itemChild;
            GameItemHandle parentItemHandle, factoryRoot, entityRoot;
            int numChildren, numItemSiblingHandles, numItemChildHandles, parentItemChildIndex, count, length, i;
            while(results.TryDequeue(out var result))
            {
                if (questCommandConditions.HasBuffer(result.entity))
                {
                    questCommandCondition.index = result.formulaIndex;
                    questCommandCondition.count = result.count;
                    questCommandCondition.label = default;
                    questCommandConditions[result.entity].Add(questCommandCondition);
                    
                    questCommandConditions.SetBufferEnabled(result.entity, true);
                }

                ref var formula = ref definition.values[result.formulaIndex];
                numChildren = formula.children.Length;
                if (numChildren > 0)
                {
                    factoryRoot = roots[result.factory].handle;
                    entityRoot = roots.HasComponent(result.entity) ? roots[result.entity].handle : GameItemHandle.Empty;
                    for (i = 0; i < numChildren; ++i)
                    {
                        ref var child = ref formula.children[i];

                        count = child.itemCount * result.count;
                        length = itemManager.Remove(factoryRoot, child.itemType, count, itemSiblingHandles, itemChildHandles);
                        if(length < count)
                            length += itemManager.Remove(entityRoot, child.itemType, count - length, itemSiblingHandles, itemChildHandles);
                        
                        UnityEngine.Assertions.Assert.IsTrue(length == count);
                    }

                    if (siblings.HasBuffer(result.factory))
                    {
                        siblings[result.factory].Reinterpret<GameItemHandle>().AddRange(itemSiblingHandles.AsArray());
                        
                        siblings.SetComponentEnabled(result.factory, true);
                    }
                    else
                    {
                        numItemSiblingHandles = itemSiblingHandles.Length;
                        for (i = 0; i < numItemSiblingHandles; ++i)
                            itemManager.Remove(itemSiblingHandles[i], 0);
                    }
                    itemSiblingHandles.Clear();

                    numItemChildHandles = itemChildHandles.Length;
                    for (i = 0; i < numItemChildHandles; ++i)
                    {
                        ref readonly var itemChildHandle = ref itemChildHandles.ElementAt(i);

                        if (itemManager.TryGetValue(itemChildHandle, out itemChild) &&
                            itemManager.Find(
                                factoryRoot,
                                itemChild.type,
                                itemChild.count,
                                out parentItemChildIndex,
                                out parentItemHandle))
                        {
                            var moveType = itemManager.Move(itemChildHandle, parentItemHandle, parentItemChildIndex);
                            UnityEngine.Assertions.Assert.AreEqual(GameItemMoveType.All, moveType);
                        }
                        else
                        {
                            if (itemSpawnHandleCommands.HasBuffer(result.entity))
                            {
                                GameItemSpawnHandleCommand command;
                                command.spawnType = GameItemSpawnType.Drop;
                                command.handle = itemChildHandle;
                                //command.version = ++version.value;
                                command.owner = result.owner;
                                command.transform = math.RigidTransform(rotations[result.entity].Value, translations[result.entity].Value);
                                itemSpawnHandleCommands[result.entity].Add(command);
                                itemSpawnHandleCommands.SetBufferEnabled(result.entity, true);

                                //versions[result.entity] = version;
                            }
                        }
                    }
                    itemChildHandles.Clear();
                }

                if (formula.cost > 0)
                {
                    money = moneys[result.entity];
                    money.value -= formula.cost;
                    moneys[result.entity] = money;
                }
            }

            results.Clear();

            itemSiblingHandles.Dispose();
            itemChildHandles.Dispose();
        }
    }

    [BurstCompile]
    private struct ApplyToComplete : IJob
    {
        public BlobAssetReference<GameFormulaFactoryDefinition> definition;

        public GameItemManager itemManager;

        [ReadOnly]
        public ComponentLookup<Rotation> rotations;

        [ReadOnly]
        public ComponentLookup<Translation> translations;

        public BufferLookup<GameItemSibling> siblings;

        public BufferLookup<GameItemSpawnCommand> commands;

        public NativeQueue<CompletedResult> inputs;

        public SharedList<Result>.Writer outputs;

        public void Execute()
        {
            //outputs.Clear();

            ref var definition = ref this.definition.Value;

            DynamicBuffer<GameItemSibling> siblings;
            Result output;
            GameItemInfo item;
            GameItemHandle sourceHandle, destinationHandle;
            int i, destinationItemCount, sourceItemCount, numSiblings;
            while(inputs.TryDequeue(out var input))
            {
                output.formulaIndex = input.formulaIndex;
                output.entity = input.entity;
                output.factory = input.factory;

                ref var result = ref definition.values[input.formulaIndex].results[input.resultIndex];
                sourceItemCount = result.itemCount * input.count;

                do
                {
                    if (input.parentHandle.Equals(GameItemHandle.Empty))
                    {
                        output.handle = GameItemHandle.Empty;
                        outputs.Add(output);

                        if (commands.HasBuffer(input.entity))
                        {
                            GameItemSpawnCommand command;
                            command.spawnType = GameItemSpawnType.Drop;
                            command.itemType = result.itemType;
                            command.itemCount = sourceItemCount;
                            command.owner = input.owner;
                            command.transform = math.RigidTransform(rotations[input.entity].Value, translations[input.entity].Value);
                            commands[input.entity].Add(command);

                            commands.SetBufferEnabled(input.entity, true);
                        }

                        sourceItemCount = 0;
                    }
                    else
                    {
                        destinationItemCount = sourceItemCount;

                        output.handle = itemManager.Add(input.parentHandle, input.parentChildIndex, result.itemType, ref destinationItemCount);
                        if (itemManager.TryGetValue(output.handle, out item))
                        {
                            //output.handle = sourceHandle;
                            outputs.Add(output);

                            sourceHandle = output.handle;
                            destinationHandle = item.siblingHandle;
                            while(itemManager.TryGetValue(destinationHandle, out item))
                            {
                                sourceHandle = destinationHandle;

                                destinationHandle = item.siblingHandle;
                            }

                            if (this.siblings.HasBuffer(input.factory))
                            {
                                siblings = this.siblings[input.factory];
                                numSiblings = siblings.Length;
                                for (i = 0; i < numSiblings; ++i)
                                {
                                    destinationHandle = siblings[i].handle;
                                    itemManager.AttachSibling(sourceHandle, destinationHandle);
                                    while (itemManager.TryGetValue(destinationHandle, out item))
                                    {
                                        sourceHandle = destinationHandle;

                                        destinationHandle = item.siblingHandle;
                                    }
                                }

                                siblings.Clear();
                                
                                this.siblings.SetComponentEnabled(input.factory, false);
                            }

                            numSiblings = result.itemSiblingTypes.Length;
                            for (i = 0; i < numSiblings; ++i)
                            {
                                destinationHandle = itemManager.Add(result.itemSiblingTypes[i]);
                                itemManager.AttachSibling(sourceHandle, destinationHandle);
                                while (itemManager.TryGetValue(destinationHandle, out item))
                                {
                                    sourceHandle = destinationHandle;

                                    destinationHandle = item.siblingHandle;
                                }
                            }
                        }

                        if (destinationItemCount > 0)
                            itemManager.Find(
                                input.handle,
                                result.itemType,
                                destinationItemCount,
                                out input.parentChildIndex,
                                out input.parentHandle);

                        sourceItemCount = destinationItemCount;
                    }
                } while (sourceItemCount > 0);
            }
        }
    }

    private EntityQuery __groupToRun;
    private EntityQuery __groupToComplete;

    private EntityTypeHandle __entityType;
    private ComponentTypeHandle<GameItemRoot> __itemRootType;
    private ComponentTypeHandle<GameFormulaFactoryMode> __modeType;
    private ComponentTypeHandle<GameFormulaFactoryStatus> __statusType;
    private ComponentLookup<GameFormulaFactoryStatus> __states;

    private BufferLookup<GameItemSibling> __siblings;

    private BufferTypeHandle<GameFormulaFactoryEntity> __factoryEntityType;
    private ComponentTypeHandle<GameFormulaFactoryTimeScale> __timeScaleType;
    private ComponentTypeHandle<GameFormulaFactoryItemTimeScale> __itemTimeScaleType;
    private ComponentTypeHandle<GameFormulaFactoryTime> __timeType;

    private BufferTypeHandle<GameFormulaFactoryCommand> __commandType;

    private BufferTypeHandle<GameFormulaFactoryInstance> __instanceType;

    private ComponentLookup<GameFormulaFactoryTimeScale> __timeScales;

    private BufferLookup<GameFormula> __formulas;
    private ComponentLookup<GameItemRoot> __itemRoots;

    private ComponentLookup<Rotation> __rotations;

    private ComponentLookup<Translation> __translations;

    private ComponentLookup<GameMoney> __moneys;

    private BufferLookup<GameItemSpawnHandleCommand> __itemSpawnHandleCommands;
    private BufferLookup<GameItemSpawnCommand> __itemSpawnCommands;
    public BufferLookup<GameQuestCommandCondition> __questCommandConditions;

    private NativeQueue<RunningResult> __runningResults;
    private NativeQueue<CompletedResult> __completedResults;

    private GameItemManagerShared __itemManager;

    /*private EntityCommandPool<Entity> __removeTimeComponentPool;
    private EntityCommandPool<EntityData<GameFormulaFactoryTime>> __addTimeComponentPool;*/

    public SharedList<Result> results
    {
        get;

        private set;
    }

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __groupToRun = builder
                    .WithAll<GameItemRoot, GameFormulaFactoryMode>()
                    .WithAllRW<GameFormulaFactoryTimeScale>()
                    .WithAllRW<GameFormulaFactoryTime>()
                    .WithAllRW<GameFormulaFactoryStatus>()
                    .WithOptions(EntityQueryOptions.IncludeDisabledEntities)
                    .Build(ref state);

        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __groupToComplete = builder
                    .WithAll<GameItemRoot, GameFormulaFactoryMode>()
                    .WithAllRW<GameFormulaFactoryCommand, GameFormulaFactoryStatus>()
                    .Build(ref state);
        __groupToComplete.SetChangedVersionFilter(ComponentType.ReadWrite<GameFormulaFactoryCommand>());

        __entityType = state.GetEntityTypeHandle();
        __itemRootType = state.GetComponentTypeHandle<GameItemRoot>(true);
        __modeType = state.GetComponentTypeHandle<GameFormulaFactoryMode>(true);
        __statusType = state.GetComponentTypeHandle<GameFormulaFactoryStatus>(true);
        __states = state.GetComponentLookup<GameFormulaFactoryStatus>();

        __siblings = state.GetBufferLookup<GameItemSibling>();

        __factoryEntityType = state.GetBufferTypeHandle<GameFormulaFactoryEntity>(true);
        __timeScaleType = state.GetComponentTypeHandle<GameFormulaFactoryTimeScale>(true);
        __itemTimeScaleType = state.GetComponentTypeHandle<GameFormulaFactoryItemTimeScale>();
        __timeType = state.GetComponentTypeHandle<GameFormulaFactoryTime>();

        __commandType = state.GetBufferTypeHandle<GameFormulaFactoryCommand>();

        __instanceType = state.GetBufferTypeHandle<GameFormulaFactoryInstance>();

        __timeScales = state.GetComponentLookup<GameFormulaFactoryTimeScale>();

        __formulas = state.GetBufferLookup<GameFormula>(true);
        __itemRoots = state.GetComponentLookup<GameItemRoot>(true);

        __rotations = state.GetComponentLookup<Rotation>(true);

        __translations = state.GetComponentLookup<Translation>(true);

        __moneys = state.GetComponentLookup<GameMoney>();

        __itemSpawnHandleCommands = state.GetBufferLookup<GameItemSpawnHandleCommand>();
        __itemSpawnCommands = state.GetBufferLookup<GameItemSpawnCommand>();
        __questCommandConditions = state.GetBufferLookup<GameQuestCommandCondition>();

        __runningResults = new NativeQueue<RunningResult>(Allocator.Persistent);
        __completedResults = new NativeQueue<CompletedResult>(Allocator.Persistent);

        var world = state.WorldUnmanaged;

        __itemManager = world.GetExistingSystemUnmanaged<GameItemSystem>().manager;

        /*ref var endFrameBarrier = ref world.GetExistingSystemUnmanaged<GameFormulaFactoryStructChangeSystem>();
        __removeTimeComponentPool = endFrameBarrier.removeTimeComponentPool;
        __addTimeComponentPool = endFrameBarrier.addTimeComponentPool;*/

        results = new SharedList<Result>(Allocator.Persistent);
    }

    //[BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        __runningResults.Dispose();
        __completedResults.Dispose();

        results.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.HasSingleton<GameFormulaFactoryData>())
            return;

        ref readonly var time = ref state.WorldUnmanaged.Time;

        var itemManager = __itemManager.value;
        var itemManagerReadOnly = itemManager.readOnly;
        ref var itemJobManager = ref __itemManager.lookupJobManager;
        JobHandle? itemJobHandle = null;
        JobHandle inputDeps = state.Dependency, jobHandle = inputDeps;

        var entityType = __entityType.UpdateAsRef(ref state);
        var itemRootType = __itemRootType.UpdateAsRef(ref state);
        var modeType = __modeType.UpdateAsRef(ref state);
        var instanceType = __instanceType.UpdateAsRef(ref state);
        var commandType = __commandType.UpdateAsRef(ref state);
        var timeType = __timeType.UpdateAsRef(ref state);
        var statusType = __statusType.UpdateAsRef(ref state);
        var states = __states.UpdateAsRef(ref state);
        var itemRoots = __itemRoots.UpdateAsRef(ref state);

        var definition = SystemAPI.GetSingleton<GameFormulaFactoryData>().definition;
        if (!__groupToRun.IsEmptyIgnoreFilter)
        {
            //__completedResults.Capacity = math.max(__completedResults.Capacity, __completedResults.Length + __groupToRun.CalculateChunkCount());

            //var entityManager = __removeTimeComponentPool.Create();

            RunEx run;
            run.deltaTime = time.DeltaTime;
            run.hash = RandomUtility.Hash(time.ElapsedTime);
            run.definition = SystemAPI.GetSingleton<GameFormulaFactoryData>().definition;
            run.itemManager = itemManagerReadOnly;
            run.entityType = entityType;
            run.factoryEntityType = __factoryEntityType.UpdateAsRef(ref state);
            run.itemRoots = __itemRoots;
            run.itemRootType = itemRootType;
            run.modeType = modeType;
            run.statusType = statusType;
            run.timeScaleType = __timeScaleType.UpdateAsRef(ref state);
            run.itemTimeScaleType = __itemTimeScaleType.UpdateAsRef(ref state);
            run.timeType = timeType;
            run.commandType = commandType;
            run.instanceType = instanceType;
            run.timeScales = __timeScales.UpdateAsRef(ref state);
            run.states = states;
            //run.entityManager = entityManager.parallelWriter;
            run.results = __completedResults.AsParallelWriter();

            itemJobHandle = itemJobManager.readWriteJobHandle;

            jobHandle = run.ScheduleParallelByRef(__groupToRun, JobHandle.CombineDependencies(jobHandle, itemJobHandle.Value));

            //itemJobManager.AddReadOnlyDependency(jobHandle);

            //entityManager.AddJobHandleForProducer<RunEx>(jobHandle);
        }

        var siblings = __siblings.UpdateAsRef(ref state);
        if (!__groupToComplete.IsEmptyIgnoreFilter)
        {
            /*var counter = new NativeArray<int>(1, Allocator.TempJob, NativeArrayOptions.ClearMemory);

            jobHandle = __groupToComplete.CalculateEntityCountAsync(counter, jobHandle);

            Resize resize;
            resize.count = counter;
            resize.completedResults = __completedResults;
            resize.runningResults = __runningResults;
            jobHandle = resize.ScheduleByRef(jobHandle);*/

            var moneys = __moneys.UpdateAsRef(ref state); 

            //var entityManager = __addTimeComponentPool.Create();

            CompleteEx complete;
            //complete.time = time.ElapsedTime;
            complete.hash = RandomUtility.Hash(time.ElapsedTime);
            complete.definition = definition;
            complete.itemManager = itemManagerReadOnly;
            complete.entityType = entityType;
            complete.formulas = __formulas.UpdateAsRef(ref state);
            complete.moneys = moneys;
            complete.itemRoots = itemRoots;
            complete.itemRootType = itemRootType;
            complete.modeType = modeType;
            complete.statusType = statusType;
            complete.timeType = timeType;
            complete.commandType = commandType;
            complete.instanceType = instanceType;
            complete.states = states;
            //complete.entityManager = entityManager.parallelWriter;
            complete.completeResults = __completedResults.AsParallelWriter();
            complete.runningResults = __runningResults.AsParallelWriter();

            if (itemJobHandle == null)
            {
                itemJobHandle = itemJobManager.readWriteJobHandle;

                jobHandle = JobHandle.CombineDependencies(jobHandle, itemJobHandle.Value);
            }

            jobHandle = complete.ScheduleParallelByRef(__groupToComplete, jobHandle);

            //itemJobManager.AddReadOnlyDependency(jobHandle);

            //entityManager.AddJobHandleForProducer<CompleteEx>(jobHandle);

            ApplyToRun applyToRun;
            applyToRun.definition = definition;
            applyToRun.itemManager = itemManager;
            applyToRun.roots = itemRoots;
            applyToRun.siblings = siblings;
            applyToRun.itemSpawnHandleCommands = __itemSpawnHandleCommands.UpdateAsRef(ref state);
            applyToRun.questCommandConditions = __questCommandConditions.UpdateAsRef(ref state);
            applyToRun.moneys = moneys;
            applyToRun.translations = __translations.UpdateAsRef(ref state);
            applyToRun.rotations = __rotations.UpdateAsRef(ref state);
            applyToRun.results = __runningResults;

            jobHandle = applyToRun.ScheduleByRef(jobHandle);

            //itemJobManager.readWriteJobHandle = jobHandle;
        }

        var results = this.results;

        ref var resultJobManager = ref results.lookupJobManager;

        resultJobManager.CompleteReadWriteDependency();

        ApplyToComplete applyToComplete;
        applyToComplete.definition = definition;
        applyToComplete.itemManager = itemManager;
        applyToComplete.translations = __translations.UpdateAsRef(ref state);
        applyToComplete.rotations = __rotations.UpdateAsRef(ref state);
        applyToComplete.siblings = siblings;
        applyToComplete.commands = __itemSpawnCommands.UpdateAsRef(ref state);
        applyToComplete.inputs = __completedResults;
        applyToComplete.outputs = results.writer;

        if (itemJobHandle == null)
        {
            itemJobHandle = itemJobManager.readWriteJobHandle;

            jobHandle = JobHandle.CombineDependencies(jobHandle, itemJobHandle.Value);
        }

        jobHandle = applyToComplete.ScheduleByRef(jobHandle);

        //resultJobManager.AddReadOnlyDependency(jobHandle);

        itemJobManager.readWriteJobHandle = jobHandle;

        state.Dependency = jobHandle;
    }
}
