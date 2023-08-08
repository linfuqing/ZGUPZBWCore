using Unity.Jobs;
using Unity.Burst;
using Unity.Entities;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using ZG;

[assembly: RegisterGenericJobType(typeof(GameItemComponentDataInit<GameItemLevel, GameItemLevelInitSystem.Initializer, GameItemLevelInitSystem.Factory>))]
[assembly: RegisterGenericJobType(typeof(GameItemComponentInit<GameItemExp, GameItemExpInitSystem.Initializer>))]
[assembly: RegisterGenericJobType(typeof(GameItemComponentInit<GameItemPower, GameItemPowerInitSystem.Initializer>))]

#if DEBUG
[assembly: RegisterEntityCommandProducerJob(typeof(GameItemComponentDataInit<GameItemLevel, GameItemLevelInitSystem.Initializer, GameItemLevelInitSystem.Factory>))]
[assembly: RegisterEntityCommandProducerJob(typeof(GameItemComponentInit<GameItemExp, GameItemExpInitSystem.Initializer>))]
[assembly: RegisterEntityCommandProducerJob(typeof(GameItemComponentInit<GameItemPower, GameItemPowerInitSystem.Initializer>))]
#endif

public struct GameItemLevel : ICleanupComponentData
{
    public int handle;
}

public struct GameItemExp : ICleanupComponentData
{
    public float value;
}

public struct GameItemPower : ICleanupComponentData
{
    public float value;
}

[BurstCompile, 
    CreateAfter(typeof(GameItemSystem)), 
    CreateAfter(typeof(GameItemComponentStructChangeSystem)), 
    UpdateInGroup(typeof(GameItemComponentInitSystemGroup), OrderFirst = true)]
public partial struct GameItemLevelInitSystem : IGameItemInitializationSystem<GameItemLevel, GameItemLevelInitSystem.Initializer>
{
    public struct Initializer : IGameItemInitializer<GameItemLevel>, IGameItemComponentInitializer<GameItemLevel>
    {
        public Random random;

        [ReadOnly]
        public NativeParallelMultiHashMap<int, int> values;

        public bool IsVail(int type) => values.ContainsKey(type);

        public GameItemLevel GetValue(int type, int count)
        {
            GameItemLevel result;
            result.handle = 0;

            int valueCount = values.CountValuesForKey(type), valueIndex = random.NextInt(0, valueCount);
            var enumerator = values.GetValuesForKey(type);
            while (enumerator.MoveNext())
            {
                result.handle = enumerator.Current;

                if (--valueIndex < 0)
                    break;
            }

            return result;
        }

        public bool TryGetValue(in GameItemHandle handle, int type, out GameItemLevel result)
        {
            result = default;

            int valueCount = values.CountValuesForKey(type);
            if (valueCount > 0)
            {
                int valueIndex = random.NextInt(0, valueCount);

                var enumerator = values.GetValuesForKey(type);
                while(enumerator.MoveNext())
                {
                    result.handle = enumerator.Current;

                    if (--valueIndex < 0)
                        break;
                }

                return true;
            }

            return false;
        }
    }

    public struct Factory : IGameItemComponentFactory<Initializer>
    {
        public double time;

        [ReadOnly]
        public NativeParallelMultiHashMap<int, int> values;

        public Initializer Create(in GameItemManager.ReadOnlyInfos infos, in ArchetypeChunk chunk, int unfilteredChunkIndex)
        {
            Initializer initializer;
            initializer.random = CreateRandom(time, unfilteredChunkIndex);
            initializer.values = values;
            return initializer;
        }
    }

    private double __time;
    private NativeParallelMultiHashMap<int, int> __values;
    private GameItemComponentDataInitSystemCore<GameItemLevel> __core;

    public Initializer initializer
    {
        get
        {
            Initializer initializer;
            initializer.random = CreateRandom(__time, 0);
            initializer.values = __values;
            return initializer;
        }
    }

    public void Create(System.Collections.Generic.KeyValuePair<int, int>[] values)
    {
        foreach (var value in values)
        {
            __values.Add(value.Key, value.Value);
        }
    }

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __values = new NativeParallelMultiHashMap<int, int>(1, Allocator.Persistent);

        __core = new GameItemComponentDataInitSystemCore<GameItemLevel>(ref state);
    }

    //[BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        __values.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        __time = state.WorldUnmanaged.Time.ElapsedTime;

        Factory factory;
        factory.time = __time;
        factory.values = __values;
        __core.Update<Initializer, Factory>(factory, ref state);
    }

    public static Random CreateRandom(double time, int index)
    {
        long hash = math.aslong(time);
        return new Random(math.max(1, (uint)hash ^ (uint)(hash >> 32) ^ (uint)index));
    }
}

[BurstCompile, 
    CreateAfter(typeof(GameItemSystem)), 
    CreateAfter(typeof(GameItemComponentStructChangeSystem)),
    CreateAfter(typeof(GameItemLevelInitSystem)),
    UpdateInGroup(typeof(GameItemComponentInitSystemGroup), OrderFirst = true)]
public struct GameItemExpInitSystem : IGameItemInitializationSystem<GameItemExp, GameItemExpInitSystem.Initializer>
{
    public struct Initializer : IGameItemInitializer<GameItemExp>
    {
        public GameItemLevelInitSystem.Initializer instance;

        public bool IsVail(int type) => instance.IsVail(type);

        public GameItemExp GetValue(int type, int count)
        {
            GameItemExp value;
            value.value = 0;

            return value;
        }
    }

    private GameItemLevelInitSystem.Initializer __initializer;
    private GameItemComponentInitSystemCore<GameItemExp> __core;

    public Initializer initializer
    {
        get
        {
            Initializer initializer;
            initializer.instance = __initializer;
            return initializer;
        }
    }

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __initializer = state.WorldUnmanaged.GetExistingSystemUnmanaged<GameItemLevelInitSystem>().initializer;

        __core = new GameItemComponentInitSystemCore<GameItemExp>(ref state);
    }

    public void OnDestroy(ref SystemState state)
    {
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        __core.Update(initializer, ref state);
    }
}

[BurstCompile, 
    CreateAfter(typeof(GameItemSystem)), 
    CreateAfter(typeof(GameItemComponentStructChangeSystem)), 
    CreateAfter(typeof(GameItemLevelInitSystem)), 
    UpdateInGroup(typeof(GameItemComponentInitSystemGroup), OrderFirst = true)]
public struct GameItemPowerInitSystem : IGameItemInitializationSystem<GameItemPower, GameItemPowerInitSystem.Initializer>
{
    public struct Initializer : IGameItemInitializer<GameItemPower>
    {
        public GameItemLevelInitSystem.Initializer instance;

        public bool IsVail(int type) => instance.IsVail(type);

        public GameItemPower GetValue(int type, int count)
        {
            GameItemPower value;
            value.value = 0;

            return value;
        }
    }

    private GameItemLevelInitSystem.Initializer __initializer;
    private GameItemComponentInitSystemCore<GameItemPower> __core;

    public Initializer initializer
    {
        get
        {
            Initializer initializer;
            initializer.instance = __initializer;
            return initializer;
        }
    }

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __initializer = state.WorldUnmanaged.GetExistingSystemUnmanaged<GameItemLevelInitSystem>().initializer;

        __core = new GameItemComponentInitSystemCore<GameItemPower>(ref state);
    }

    public void OnDestroy(ref SystemState state)
    {
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        __core.Update(initializer, ref state);
    }
}

[BurstCompile, UpdateInGroup(typeof(GameItemComponentInitSystemGroup))]
public partial struct GameItemLevelStructChangeSystem : ISystem
{
    private EntityQuery __instanceGroup;
    private EntityQuery __expGroup;
    private EntityQuery __powerGroup;

    public void OnCreate(ref SystemState state)
    {
        __instanceGroup = state.GetEntityQuery(
            ComponentType.ReadOnly<GameItemLevel>(),
            ComponentType.Exclude<GameItemData>());

        __expGroup = state.GetEntityQuery(
            ComponentType.ReadOnly<GameItemExp>(),
            ComponentType.Exclude<GameItemData>());

        __powerGroup = state.GetEntityQuery(
            ComponentType.ReadOnly<GameItemPower>(),
            ComponentType.Exclude<GameItemData>());
    }

    public void OnDestroy(ref SystemState state)
    {

    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var entityManager = state.EntityManager;
        entityManager.RemoveComponent<GameItemLevel>(__instanceGroup);
        entityManager.RemoveComponent<GameItemExp>(__expGroup);
        entityManager.RemoveComponent<GameItemPower>(__powerGroup);
    }
}