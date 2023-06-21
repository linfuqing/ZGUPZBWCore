using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
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
            in GameItemManager.ReadOnly itemManager,
            in Entity source,
            in Entity destination,
            in ComponentLookup<GameMoney> moneies,
            in ComponentLookup<GameItemRoot> itemRoots)
        {
            if (cost > 0 && cost > moneies[source].value)
                return false;

            int numChildren = children.Length;
            if (numChildren > 0)
            {
                var parentItemTypes = this.parentItemTypes.AsArray();
                int itemCount;
                bool isContains = true;
                for (int i = 0; i < numChildren; ++i)
                {
                    ref var child = ref children[i];

                    itemCount = child.itemCount;
                    if (!itemManager.Contains(handle, child.itemType, ref itemCount, parentItemTypes))
                    {
                        isContains = false;

                        break;
                    }
                }

                if (!isContains)
                {
                    //�����
                    if (source == destination)
                        return false;

                    if (!itemManager.TryGetValue(handle, out var rootItem) || parentItemTypes.IndexOf(rootItem.type) == -1)
                        return false;

                    handle = itemRoots[source].handle;
                    for (int i = 0; i < numChildren; ++i)
                    {
                        ref var child = ref children[i];

                        itemCount = child.itemCount;
                        if (!itemManager.Contains(handle, child.itemType, ref itemCount))
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

public struct GameFormulaFactorySharedData : IComponentData
{
    public BlobAssetReference<GameFormulaFactoryDefinition> definition;
}

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

    public Mode value;
}

public struct GameFormulaFactoryItemTimeScale : IComponentData
{
    public float value;
}

public struct GameFormulaFactoryTimeScale : IComponentData
{
    public float value;
}

public struct GameFormulaFactoryStorage : IComponentData
{
    public enum Status
    {
        Invactive, 
        Active
    }

    public Status status;
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

    public Entity entity;
}

public struct GameFormulaFactoryTime : IComponentData
{
    public float value;
}

public struct GameFormulaFactoryCommand : IComponentData, IEnableableComponent
{
    public Entity entity;

    public int formulaIndex;
}

[BurstCompile, UpdateInGroup(typeof(EndFrameEntityCommandSystemGroup))]
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
}

[BurstCompile, CreateAfter(typeof(GameItemSystem)), CreateAfter(typeof(GameFormulaFactoryStructChangeSystem))]
public partial struct GameFormulaFactorySystem : ISystem
{
    public struct Result
    {
        public int formulaIndex;
        public GameItemHandle handle;
        public Entity entity;
        public Entity factory;
    }

    private struct CompletedResult
    {
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

        //public GameItemHandle handle;

        public Entity entity;
        public Entity factory;
        public Entity owner;
    }

    [BurstCompile]
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
    }

    private struct Run
    {
        public float deltaTime;

        public Random random;

        [ReadOnly]
        public GameItemManager.ReadOnly itemManager;

        [ReadOnly]
        public NativeArray<Entity> entityArray;

        [ReadOnly]
        public NativeArray<GameItemRoot> itemRoots;

        [ReadOnly]
        public NativeArray<GameFormulaFactoryStorage> storages;

        [ReadOnly]
        public NativeArray<GameFormulaFactoryData> instances;

        [ReadOnly]
        public NativeArray<GameFormulaFactoryMode> modes;

        [ReadOnly]
        public NativeArray<GameFormulaFactoryStatus> states;

        [ReadOnly]
        public NativeArray<GameFormulaFactoryTimeScale> timeScales;

        public NativeArray<GameFormulaFactoryItemTimeScale> itemTimeScales;

        public NativeArray<GameFormulaFactoryTime> times;

        public NativeArray<GameFormulaFactoryCommand> commands;

        [NativeDisableContainerSafetyRestriction]
        public ComponentLookup<GameFormulaFactoryTimeScale> timeScaleMap;

        [NativeDisableContainerSafetyRestriction]
        public ComponentLookup<GameFormulaFactoryStatus> statusMap;

        public EntityCommandQueue<Entity>.ParallelWriter entityManager;

        public NativeList<CompletedResult>.ParallelWriter results;

        public bool Execute(int index)
        {
            float timeScale = 0.0f;// timeScales[index].value;
            var handle = itemRoots[index].handle;
            var hierarchy = itemManager.hierarchy;
            ref var definition = ref instances[index].definition.Value;
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

            Entity entity = entityArray[index];

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

                timeScaleMap[entity] = temp;
            }

            bool result = false;
            var time = times[index];
            time.value -= deltaTime * timeScale;
            if (time.value > 0.0f)
                times[index] = time;
            else
            {
                var status = states[index];

                if (status.value == GameFormulaFactoryStatus.Status.Running)
                {
                    status.value = GameFormulaFactoryStatus.Status.Normal;

                    var mode = modes[index].value;
                    switch (mode)
                    {
                        case GameFormulaFactoryMode.Mode.Normal:
                        case GameFormulaFactoryMode.Mode.Auto:
                            if (storages[index].status == GameFormulaFactoryStorage.Status.Active)
                            {
                                if (!Complete(false, status.formulaIndex, status.level, entity, status.entity, handle, ref definition.values[status.formulaIndex]))
                                    return false;

                                if (mode == GameFormulaFactoryMode.Mode.Auto)
                                {
                                    Command(index, status.formulaIndex, entity);

                                    result = true;
                                }
                            }
                            else
                                status.value = GameFormulaFactoryStatus.Status.Completed;
                            break;
                        case GameFormulaFactoryMode.Mode.Once:
                            if (!Complete(true, status.formulaIndex, status.level, entity, status.entity, handle, ref definition.values[status.formulaIndex]))
                                return false;

                            //factoryStatus = GameFactoryStatus.Complete;
                            break;
                        case GameFormulaFactoryMode.Mode.Repeat:
                            if (!Complete(false, status.formulaIndex, status.level, entity, status.entity, handle, ref definition.values[status.formulaIndex]))
                                return false;

                            Command(index, status.formulaIndex, entity);

                            result = true;
                            break;
                        case GameFormulaFactoryMode.Mode.Force:
                            ref var formula = ref definition.values[status.formulaIndex];
                            if (!Complete(true, status.formulaIndex, status.level, status.entity, entity, handle, ref formula))
                                return false;

                            if (formula.Test(
                                ref handle,
                                itemManager,
                                entity,
                                entity,
                                default,
                                default))
                                return false;

                            Command(index, status.formulaIndex, entity);

                            result = true;
                            break;
                        default:
                            return false;// throw new InvalidOperationException();
                    }

                    statusMap[entity] = status;
                }

                entityManager.Enqueue(entity);
            }

            return result;
        }

        public bool Complete(
            bool isForce, 
            int formulaIndex, 
            int level, 
            in Entity entity, 
            in Entity owner, 
            in GameItemHandle handle, 
            ref GameFormulaFactoryDefinition.Formula formula)
        {
            CompletedResult result;
            result.resultIndex = formula.GetResultIndex(level, random.NextFloat());
            if (result.resultIndex == -1)
                return false;

            ref var formulaResult = ref formula.results[result.resultIndex];

            result.handle = handle;
            if (!itemManager.Find(
                result.handle,
                formulaResult.itemType,
                formulaResult.itemCount,
                out result.parentChildIndex,
                out result.parentHandle))
            {
                if(!isForce)
                    return false;
            }

            result.formulaIndex = formulaIndex;

            result.entity = entity;
            result.factory = entity;
            result.owner = owner;

            results.AddNoResize(result);

            return true;
        }

        public void Command(int index, int formulaIndex, in Entity entity)
        {
            GameFormulaFactoryCommand command;
            command.entity = entity;
            command.formulaIndex = formulaIndex;
            commands[index] = command;
        }
    }

    [BurstCompile]
    private struct RunEx : IJobChunk, IEntityCommandProducerJob
    {
        public float deltaTime;
        public double time;

        public GameItemManager.ReadOnly itemManager;

        [ReadOnly]
        public EntityTypeHandle entityType;

        [ReadOnly]
        public ComponentTypeHandle<GameItemRoot> itemRootType;

        [ReadOnly]
        public ComponentTypeHandle<GameFormulaFactoryStorage> storageType;

        [ReadOnly]
        public ComponentTypeHandle<GameFormulaFactoryData> instanceType;

        [ReadOnly]
        public ComponentTypeHandle<GameFormulaFactoryMode> modeType;

        [ReadOnly]
        public ComponentTypeHandle<GameFormulaFactoryStatus> statusType;

        [ReadOnly]
        public ComponentTypeHandle<GameFormulaFactoryTimeScale> timeScaleType;

        public ComponentTypeHandle<GameFormulaFactoryItemTimeScale> itemTimeScaleType;

        public ComponentTypeHandle<GameFormulaFactoryTime> timeType;

        public ComponentTypeHandle<GameFormulaFactoryCommand> commandType;

        [NativeDisableContainerSafetyRestriction]
        public ComponentLookup<GameFormulaFactoryTimeScale> timeScales;

        [NativeDisableContainerSafetyRestriction]
        public ComponentLookup<GameFormulaFactoryStatus> states;

        public EntityCommandQueue<Entity>.ParallelWriter entityManager;

        public NativeList<CompletedResult>.ParallelWriter results;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            long hash = math.aslong(time);

            Run run;
            run.deltaTime = deltaTime;
            run.random = new Random((uint)hash ^ (uint)(hash >> 32));
            run.itemManager = itemManager;
            run.entityArray = chunk.GetNativeArray(entityType);
            run.itemRoots = chunk.GetNativeArray(ref itemRootType);
            run.storages = chunk.GetNativeArray(ref storageType);
            run.instances = chunk.GetNativeArray(ref instanceType);
            run.modes = chunk.GetNativeArray(ref modeType);
            run.states = chunk.GetNativeArray(ref statusType);
            run.timeScales = chunk.GetNativeArray(ref timeScaleType);
            run.itemTimeScales = chunk.GetNativeArray(ref itemTimeScaleType);
            run.times = chunk.GetNativeArray(ref timeType);
            run.commands = chunk.GetNativeArray(ref commandType);
            run.timeScaleMap = timeScales;
            run.statusMap = states;
            run.entityManager = entityManager;
            run.results = results;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
            {
                if (run.Execute(i))
                    chunk.SetComponentEnabled(ref commandType, i, true);
            }
        }
    }

    private struct Complete
    {
        public Random random;

        [ReadOnly]
        public GameItemManager.ReadOnly itemManager;

        [ReadOnly]
        public BufferLookup<GameFormula> formulas;

        [ReadOnly]
        public ComponentLookup<GameMoney> moneies;

        [ReadOnly]
        public ComponentLookup<GameItemRoot> itemRootMap;

        [ReadOnly]
        public NativeArray<Entity> entityArray;

        [ReadOnly]
        public NativeArray<GameItemRoot> itemRoots;

        [ReadOnly]
        public NativeArray<GameFormulaFactoryData> instances;

        [ReadOnly]
        public NativeArray<GameFormulaFactoryMode> modes;

        [ReadOnly]
        public NativeArray<GameFormulaFactoryStatus> states;

        [ReadOnly]
        public NativeArray<GameFormulaFactoryCommand> commands;

        [NativeDisableContainerSafetyRestriction]
        public ComponentLookup<GameFormulaFactoryStatus> statusMap;

        public EntityCommandQueue<EntityData<GameFormulaFactoryTime>>.ParallelWriter entityManager;

        public NativeList<RunningResult>.ParallelWriter runningResults;

        public NativeList<CompletedResult>.ParallelWriter completeResults;

        public void Execute(int index)
        {
            var command = commands[index];
            if (command.entity == Entity.Null)
                return;

            Entity entity;
            GameFormula temp;
            GameFormulaFactoryData instance;
            var status = states[index];
            if (command.formulaIndex == -1)
            {
                if (status.value != GameFormulaFactoryStatus.Status.Completed)
                    return;

                if (GameFormulaManager.IndexOf(status.formulaIndex, formulas[command.entity], out temp) == -1)
                    temp = default;

                entity = entityArray[index];

                instance = instances[index];

                ref var formula = ref instance.definition.Value.values[status.formulaIndex];

                CompletedResult result;
                result.resultIndex = formula.GetResultIndex(temp.level, random.NextFloat());
                if (result.resultIndex != -1)
                {
                    result.factory = entity;
                    result.entity = command.entity;
                    result.owner = command.entity;
                    result.handle = itemRootMap[command.entity].handle;

                    ref var formulaResult = ref formula.results[result.resultIndex];

                    itemManager.Find(result.handle, formulaResult.itemType, formulaResult.itemCount, out result.parentChildIndex, out result.parentHandle);

                    result.formulaIndex = status.formulaIndex;

                    completeResults.AddNoResize(result);
                }

                if (modes[index].value != GameFormulaFactoryMode.Mode.Auto)
                {
                    status.value = GameFormulaFactoryStatus.Status.Normal;
                    status.level = temp.level;
                    status.entity = command.entity;
                    statusMap[entity] = status;

                    return;
                }

                command.formulaIndex = status.formulaIndex;
            }
            else if (status.value == GameFormulaFactoryStatus.Status.Normal)
            {
                if (GameFormulaManager.IndexOf(command.formulaIndex, this.formulas[command.entity], out temp) == -1)
                    temp = default;

                instance = instances[index];

                entity = entityArray[index];
            }
            else
                return;

            {
                var handle = itemRoots[index].handle;
                ref var formula = ref instance.definition.Value.values[command.formulaIndex];
                if (!formula.Test(
                    ref handle,
                    itemManager,
                    command.entity,
                    entity,
                    moneies,
                    itemRootMap))
                    return;

                EntityData<GameFormulaFactoryTime> time;
                time.entity = entity;
                time.value.value = formula.time;
                entityManager.Enqueue(time);

                RunningResult result;
                result.entity = command.entity;
                result.factory = entity;
                result.owner = command.entity;
                result.formulaIndex = command.formulaIndex;
                runningResults.AddNoResize(result);

                status.value = GameFormulaFactoryStatus.Status.Running;
                status.formulaIndex = command.formulaIndex;
                status.level = temp.level;
                status.entity = command.entity;
                statusMap[entity] = status;
            }
        }
    }

    [BurstCompile]
    private struct CompleteEx : IJobChunk, IEntityCommandProducerJob
    {
        public double time;

        [ReadOnly]
        public GameItemManager.ReadOnly itemManager;

        [ReadOnly]
        public EntityTypeHandle entityType;

        [ReadOnly]
        public BufferLookup<GameFormula> formulas;

        [ReadOnly]
        public ComponentLookup<GameMoney> moneies;

        [ReadOnly]
        public ComponentLookup<GameItemRoot> itemRoots;

        [ReadOnly]
        public ComponentTypeHandle<GameItemRoot> itemRootType;

        [ReadOnly]
        public ComponentTypeHandle<GameFormulaFactoryData> instanceType;

        [ReadOnly]
        public ComponentTypeHandle<GameFormulaFactoryMode> modeType;

        [ReadOnly]
        public ComponentTypeHandle<GameFormulaFactoryStatus> statusType;

        public ComponentTypeHandle<GameFormulaFactoryCommand> commandType;

        [NativeDisableContainerSafetyRestriction]
        public ComponentLookup<GameFormulaFactoryStatus> states;

        public EntityCommandQueue<EntityData<GameFormulaFactoryTime>>.ParallelWriter entityManager;

        public NativeList<RunningResult>.ParallelWriter runningResults;

        public NativeList<CompletedResult>.ParallelWriter completeResults;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            long hash = math.aslong(time);

            Complete complete;
            complete.random = new Random((uint)hash ^ (uint)(hash >> 32));
            complete.itemManager = itemManager;
            complete.entityArray = chunk.GetNativeArray(entityType);
            complete.formulas = this.formulas;
            complete.moneies = moneies;
            complete.itemRootMap = itemRoots;
            complete.itemRoots = chunk.GetNativeArray(ref itemRootType);
            complete.instances = chunk.GetNativeArray(ref instanceType);
            complete.modes = chunk.GetNativeArray(ref modeType);
            complete.states = chunk.GetNativeArray(ref statusType);
            complete.commands = chunk.GetNativeArray(ref commandType);
            complete.statusMap = states;
            complete.entityManager = entityManager;
            complete.runningResults = runningResults;
            complete.completeResults = completeResults;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
            {
                complete.Execute(i);

                chunk.SetComponentEnabled(ref commandType, i, false);
            }
        }
    }

    [BurstCompile]
    private struct ApplyToRun : IJob
    {
        public GameItemManager itemManager;

        public NativeList<RunningResult> results;

        public BufferLookup<GameItemSibling> siblings;

        public BufferLookup<GameItemSpawnHandleCommand> commands;

        public ComponentLookup<GameItemSpawnCommandVersion> versions;

        public ComponentLookup<GameMoney> moneies;

        [ReadOnly]
        public ComponentLookup<GameItemRoot> roots;

        [ReadOnly]
        public ComponentLookup<GameFormulaFactoryData> instances;

        public void Execute()
        {
            var itemSiblingHandles = new NativeList<GameItemHandle>(Allocator.Temp);
            var itemChildHandles = new NativeList<GameItemHandle>(Allocator.Temp);

            GameMoney money;
            GameItemInfo itemChild;
            GameItemHandle parentItemHandle;
            int numResults = results.Length, numChildren, numItemSiblingHandles, numItemChildHandles, parentItemChildIndex, i, j;
            for (i = 0; i < numResults; ++i)
            {
                ref var result = ref results.ElementAt(i);
                ref var formula = ref instances[result.factory].definition.Value.values[result.formulaIndex];
                numChildren = formula.children.Length;
                if (numChildren > 0)
                {
                    int length;
                    GameItemHandle factoryRoot = roots[result.factory].handle, 
                        entityRoot = roots.HasComponent(result.entity) ? roots[result.entity].handle : GameItemHandle.Empty;
                    for (j = 0; j < numChildren; ++j)
                    {
                        ref var child = ref formula.children[j];

                        length = itemManager.Remove(factoryRoot, child.itemType, child.itemCount, itemSiblingHandles, itemChildHandles);
                        if(length < child.itemCount)
                            length += itemManager.Remove(entityRoot, child.itemType, child.itemCount - length, itemSiblingHandles, itemChildHandles);
                        
                        UnityEngine.Assertions.Assert.IsTrue(length == child.itemCount);
                    }

                    if (siblings.HasBuffer(result.factory))
                        siblings[result.factory].Reinterpret<GameItemHandle>().AddRange(itemSiblingHandles.AsArray());
                    else
                    {
                        numItemSiblingHandles = itemSiblingHandles.Length;
                        for (j = 0; j < numItemSiblingHandles; ++j)
                            itemManager.Remove(itemSiblingHandles[i], 0);
                    }
                    itemSiblingHandles.Clear();

                    numItemChildHandles = itemChildHandles.Length;
                    for (j = 0; j < numItemChildHandles; ++j)
                    {
                        ref readonly var itemChildHandle = ref itemChildHandles.ElementAt(j);

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
                            if (commands.HasBuffer(result.entity))
                            {
                                var version = versions[result.entity];

                                GameItemSpawnHandleCommand command;
                                command.spawnType = GameItemSpawnType.Drop;
                                command.handle = itemChildHandle;
                                command.version = ++version.value;
                                command.owner = result.owner;
                                commands[result.entity].Add(command);

                                versions[result.entity] = version;
                            }
                        }
                    }
                    itemChildHandles.Clear();
                }

                if (formula.cost > 0)
                {
                    money = moneies[result.entity];
                    money.value -= formula.cost;
                    moneies[result.entity] = money;
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
        public GameItemManager itemManager;

        public BufferLookup<GameItemSibling> siblings;

        public BufferLookup<GameItemSpawnCommand> commands;

        public ComponentLookup<GameItemSpawnCommandVersion> versions;

        public NativeList<CompletedResult> inputs;

        public SharedList<Result>.Writer outputs;

        [ReadOnly]
        public ComponentLookup<GameFormulaFactoryData> instances;

        public void Execute()
        {
            outputs.Clear();

            DynamicBuffer<GameItemSibling> siblings;
            Result output;
            GameItemInfo item;
            GameItemHandle sourceHandle, destinationHandle;
            int i, j, destinationItemCount, sourceItemCount, numSiblings, numResults = inputs.Length;
            for (i = 0; i < numResults; ++i)
            {
                ref var input = ref inputs.ElementAt(i);

                output.formulaIndex = input.formulaIndex;
                output.entity = input.entity;
                output.factory = input.factory;

                ref var result = ref instances[input.factory].definition.Value.values[input.formulaIndex].results[input.resultIndex];
                sourceItemCount = result.itemCount;

                do
                {
                    if (input.parentHandle.Equals(GameItemHandle.Empty))
                    {
                        output.handle = GameItemHandle.Empty;
                        outputs.Add(output);

                        if (commands.HasBuffer(input.entity))
                        {
                            var version = versions[input.entity];

                            GameItemSpawnCommand command;
                            command.spawnType = GameItemSpawnType.Drop;
                            command.itemType = result.itemType;
                            command.itemCount = sourceItemCount;
                            command.version = ++version.value;
                            command.owner = input.owner;
                            commands[input.entity].Add(command);

                            versions[input.entity] = version;
                        }

                        sourceItemCount = 0;
                    }
                    else
                    {
                        destinationItemCount = sourceItemCount;

                        output.handle = itemManager.Add(input.parentHandle, input.parentChildIndex, result.itemType, ref destinationItemCount);
                        if (itemManager.TryGetValue(output.handle, out item))
                        {
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
                                for (j = 0; j < numSiblings; ++j)
                                {
                                    destinationHandle = siblings[j].handle;
                                    itemManager.AttachSibling(sourceHandle, destinationHandle);
                                    while (itemManager.TryGetValue(destinationHandle, out item))
                                    {
                                        sourceHandle = destinationHandle;

                                        destinationHandle = item.siblingHandle;
                                    }
                                }

                                siblings.Clear();
                            }

                            numSiblings = result.itemSiblingTypes.Length;
                            for (j = 0; j < numSiblings; ++j)
                            {
                                destinationHandle = itemManager.Add(result.itemSiblingTypes[j]);
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

            inputs.Clear();
        }
    }

    private EntityQuery __groupToRun;
    private EntityQuery __groupToComplete;

    private EntityTypeHandle __entityType;
    private ComponentTypeHandle<GameItemRoot> __itemRootType;
    private ComponentTypeHandle<GameFormulaFactoryData> __instanceType;
    private ComponentTypeHandle<GameFormulaFactoryMode> __modeType;
    private ComponentTypeHandle<GameFormulaFactoryStatus> __statusType;
    private ComponentLookup<GameFormulaFactoryStatus> __states;

    private ComponentLookup<GameFormulaFactoryData> __instances;
    private ComponentLookup<GameItemSpawnCommandVersion> __versions;
    private BufferLookup<GameItemSibling> __siblings;

    private ComponentTypeHandle<GameFormulaFactoryStorage> __storageType;
    private ComponentTypeHandle<GameFormulaFactoryTimeScale> __timeScaleType;
    private ComponentTypeHandle<GameFormulaFactoryItemTimeScale> __itemTimeScaleType;
    private ComponentTypeHandle<GameFormulaFactoryTime> __timeType;

    private ComponentTypeHandle<GameFormulaFactoryCommand> __commandType;

    private ComponentLookup<GameFormulaFactoryTimeScale> __timeScales;

    private BufferLookup<GameFormula> __formulas;
    private ComponentLookup<GameItemRoot> __itemRoots;

    private ComponentLookup<GameMoney> __moneies;

    private BufferLookup<GameItemSpawnHandleCommand> __itemSpawnHandleCommands;
    private BufferLookup<GameItemSpawnCommand> __itemSpawnCommands;

    private NativeList<RunningResult> __runningResults;
    private NativeList<CompletedResult> __completedResults;

    private GameItemManagerShared __itemManager;

    private EntityCommandPool<Entity> __removeTimeComponentPool;
    private EntityCommandPool<EntityData<GameFormulaFactoryTime>> __addTimeComponentPool;

    public SharedList<Result> results
    {
        get;

        private set;
    }

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        BurstUtility.InitializeJob<Resize>();
        BurstUtility.InitializeJob<ApplyToComplete>();
        BurstUtility.InitializeJob<ApplyToRun>();

        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __groupToRun = builder
                    .WithAll<GameItemRoot, GameFormulaFactoryData, GameFormulaFactoryMode>()
                    .WithAllRW<GameFormulaFactoryTimeScale>()
                    .WithAllRW<GameFormulaFactoryTime>()
                    .WithAllRW<GameFormulaFactoryStatus>()
                    .WithOptions(EntityQueryOptions.IncludeDisabledEntities)
                    .Build(ref state);

        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __groupToComplete = builder
                    .WithAll<GameItemRoot, GameFormulaFactoryData, GameFormulaFactoryMode>()
                    .WithAllRW<GameFormulaFactoryCommand, GameFormulaFactoryStatus>()
                    .Build(ref state);
        __groupToComplete.SetChangedVersionFilter(ComponentType.ReadWrite<GameFormulaFactoryCommand>());

        __entityType = state.GetEntityTypeHandle();
        __itemRootType = state.GetComponentTypeHandle<GameItemRoot>(true);
        __instanceType = state.GetComponentTypeHandle<GameFormulaFactoryData>(true);
        __modeType = state.GetComponentTypeHandle<GameFormulaFactoryMode>(true);
        __statusType = state.GetComponentTypeHandle<GameFormulaFactoryStatus>(true);
        __states = state.GetComponentLookup<GameFormulaFactoryStatus>();

        __versions = state.GetComponentLookup<GameItemSpawnCommandVersion>();

        __instances = state.GetComponentLookup<GameFormulaFactoryData>(true);
        __siblings = state.GetBufferLookup<GameItemSibling>();

        __storageType = state.GetComponentTypeHandle<GameFormulaFactoryStorage>(true);
        __timeScaleType = state.GetComponentTypeHandle<GameFormulaFactoryTimeScale>(true);
        __itemTimeScaleType = state.GetComponentTypeHandle<GameFormulaFactoryItemTimeScale>();
        __timeType = state.GetComponentTypeHandle<GameFormulaFactoryTime>();

        __commandType = state.GetComponentTypeHandle<GameFormulaFactoryCommand>();

        __timeScales = state.GetComponentLookup<GameFormulaFactoryTimeScale>();

        __formulas = state.GetBufferLookup<GameFormula>(true);
        __itemRoots = state.GetComponentLookup<GameItemRoot>(true);

        __moneies = state.GetComponentLookup<GameMoney>();

        __itemSpawnHandleCommands = state.GetBufferLookup<GameItemSpawnHandleCommand>();
        __itemSpawnCommands = state.GetBufferLookup<GameItemSpawnCommand>();

        __runningResults = new NativeList<RunningResult>(Allocator.Persistent);
        __completedResults = new NativeList<CompletedResult>(Allocator.Persistent);

        var world = state.WorldUnmanaged;

        __itemManager = world.GetExistingSystemUnmanaged<GameItemSystem>().manager;

        ref var endFrameBarrier = ref world.GetExistingSystemUnmanaged<GameFormulaFactoryStructChangeSystem>();
        __removeTimeComponentPool = endFrameBarrier.removeTimeComponentPool;
        __addTimeComponentPool = endFrameBarrier.addTimeComponentPool;

        results = new SharedList<Result>(Allocator.Persistent);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        __runningResults.Dispose();
        __completedResults.Dispose();

        results.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        ref readonly var time = ref state.WorldUnmanaged.Time;

        var itemManager = __itemManager.value;
        var itemManagerReadOnly = itemManager.readOnly;
        ref var itemJobManager = ref __itemManager.lookupJobManager;
        JobHandle? itemJobHandle = null;
        JobHandle inputDeps = state.Dependency, jobHandle = inputDeps;

        var entityType = __entityType.UpdateAsRef(ref state);
        var itemRootType = __itemRootType.UpdateAsRef(ref state);
        var instanceType = __instanceType.UpdateAsRef(ref state);
        var modeType = __modeType.UpdateAsRef(ref state);
        var commandType = __commandType.UpdateAsRef(ref state);
        var statusType = __statusType.UpdateAsRef(ref state);
        var states = __states.UpdateAsRef(ref state);

        if (!__groupToRun.IsEmptyIgnoreFilter)
        {
            __completedResults.Capacity = math.max(__completedResults.Capacity, __completedResults.Length + __groupToRun.CalculateChunkCount());

            var entityManager = __removeTimeComponentPool.Create();

            RunEx run;
            run.deltaTime = time.DeltaTime;
            run.time = time.ElapsedTime;
            run.itemManager = itemManagerReadOnly;
            run.entityType = entityType;
            run.itemRootType = itemRootType;
            run.storageType = __storageType.UpdateAsRef(ref state);
            run.instanceType = instanceType;
            run.modeType = modeType;
            run.statusType = statusType;
            run.timeScaleType = __timeScaleType.UpdateAsRef(ref state);
            run.itemTimeScaleType = __itemTimeScaleType.UpdateAsRef(ref state);
            run.timeType = __timeType.UpdateAsRef(ref state);
            run.commandType = commandType;
            run.timeScales = __timeScales.UpdateAsRef(ref state);
            run.states = states;
            run.entityManager = entityManager.parallelWriter;
            run.results = __completedResults.AsParallelWriter();

            itemJobHandle = itemJobManager.readWriteJobHandle;

            jobHandle = run.ScheduleParallelByRef(__groupToRun, JobHandle.CombineDependencies(jobHandle, itemJobHandle.Value));

            //itemJobManager.AddReadOnlyDependency(jobHandle);

            entityManager.AddJobHandleForProducer<RunEx>(jobHandle);
        }

        var instances = __instances.UpdateAsRef(ref state);
        var versions = __versions.UpdateAsRef(ref state);
        var siblings = __siblings.UpdateAsRef(ref state);
        if (!__groupToComplete.IsEmptyIgnoreFilter)
        {
            var counter = new NativeArray<int>(1, Allocator.TempJob, NativeArrayOptions.ClearMemory);

            inputDeps = __groupToComplete.CalculateEntityCountAsync(counter, inputDeps);

            if (itemJobHandle == null)
                jobHandle = inputDeps;
            else
                jobHandle = JobHandle.CombineDependencies(jobHandle, inputDeps);

            Resize resize;
            resize.count = counter;
            resize.completedResults = __completedResults;
            resize.runningResults = __runningResults;
            jobHandle = resize.Schedule(jobHandle);

            var monies = __moneies.UpdateAsRef(ref state); 
            var itemRoots = __itemRoots.UpdateAsRef(ref state);

            var entityManager = __addTimeComponentPool.Create();

            CompleteEx complete;
            complete.time = time.ElapsedTime;
            complete.itemManager = itemManagerReadOnly;
            complete.entityType = entityType;
            complete.formulas = __formulas.UpdateAsRef(ref state);
            complete.moneies = monies;
            complete.itemRoots = itemRoots;
            complete.itemRootType = itemRootType;
            complete.instanceType = instanceType;
            complete.modeType = modeType;
            complete.statusType = statusType;
            complete.commandType = commandType;
            complete.states = states;
            complete.entityManager = entityManager.parallelWriter;
            complete.completeResults = __completedResults.AsParallelWriter();
            complete.runningResults = __runningResults.AsParallelWriter();

            if (itemJobHandle == null)
            {
                itemJobHandle = itemJobManager.readWriteJobHandle;

                jobHandle = JobHandle.CombineDependencies(jobHandle, itemJobHandle.Value);
            }

            jobHandle = complete.ScheduleParallelByRef(__groupToComplete, jobHandle);

            //itemJobManager.AddReadOnlyDependency(jobHandle);

            entityManager.AddJobHandleForProducer<CompleteEx>(jobHandle);

            ApplyToRun applyToRun;
            applyToRun.itemManager = itemManager;
            applyToRun.roots = itemRoots;
            applyToRun.siblings = siblings;
            applyToRun.commands = __itemSpawnHandleCommands.UpdateAsRef(ref state);
            applyToRun.versions = versions;
            applyToRun.moneies = monies;
            applyToRun.instances = instances;
            applyToRun.results = __runningResults;

            jobHandle = applyToRun.ScheduleByRef(jobHandle);

            //itemJobManager.readWriteJobHandle = jobHandle;
        }

        var results = this.results;

        ref var resultJobManager = ref results.lookupJobManager;

        resultJobManager.CompleteReadWriteDependency();

        ApplyToComplete applyToComplete;
        applyToComplete.itemManager = itemManager;
        applyToComplete.siblings = siblings;
        applyToComplete.commands = __itemSpawnCommands.UpdateAsRef(ref state);
        applyToComplete.versions = versions;
        applyToComplete.inputs = __completedResults;
        applyToComplete.outputs = results.writer;
        applyToComplete.instances = instances;

        if (itemJobHandle == null)
        {
            itemJobHandle = itemJobManager.readWriteJobHandle;

            jobHandle = JobHandle.CombineDependencies(jobHandle, itemJobHandle.Value);
        }

        jobHandle = applyToComplete.ScheduleByRef(jobHandle);

        resultJobManager.AddReadOnlyDependency(jobHandle);

        itemJobManager.readWriteJobHandle = jobHandle;

        state.Dependency = jobHandle;
    }
}
