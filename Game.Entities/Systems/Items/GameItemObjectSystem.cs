using Unity.Burst;
using Unity.Entities;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using ZG;

[assembly: Unity.Jobs.RegisterGenericJobType(typeof(GameItemComponentDataInit<GameItemObjectData, GameItemObjectInitSystem.Initializer, GameItemObjectInitSystem.Factory>))]

[assembly: Unity.Jobs.RegisterGenericJobType(typeof(GameItemComponentInit<GameItemName, GameItemNameInitSystem.Initializer>))]
[assembly: Unity.Jobs.RegisterGenericJobType(typeof(GameItemComponentDataSyncInit<GameNickname, GameItemName, GameItemNameSyncInitSystem.Converter, GameItemNameSyncInitSystem.Factory>))]
[assembly: Unity.Jobs.RegisterGenericJobType(typeof(GameItemComponentDataSyncApply<GameNickname, GameItemName, GameItemNameSyncApplySystem.Converter, GameItemNameSyncApplySystem.Factory>))]

[assembly: Unity.Jobs.RegisterGenericJobType(typeof(GameItemComponentDataInit<GameItemVariant, GameItemVariantInitSystem.Initializer, GameItemVariantInitSystem.Factory>))]
[assembly: Unity.Jobs.RegisterGenericJobType(typeof(GameItemComponentDataSyncInit<GameVariant, GameItemVariant, GameItemVariantSyncInitSystem.Converter, GameItemVariantSyncInitSystem.Factory>))]
[assembly: Unity.Jobs.RegisterGenericJobType(typeof(GameItemComponentDataSyncApply<GameVariant, GameItemVariant, GameItemVariantSyncApplySystem.Converter, GameItemVariantSyncApplySystem.Factory>))]

[assembly: Unity.Jobs.RegisterGenericJobType(typeof(GameItemComponentDataInit<GameItemOwner, GameItemOwnerInitSystem.Initializer, GameItemOwnerInitSystem.Factory>))]
[assembly: Unity.Jobs.RegisterGenericJobType(typeof(GameItemComponentDataSyncInit<GameOwner, GameItemOwner, GameItemOwnerSyncInitSystem.Converter, GameItemOwnerSyncInitSystem.Factory>))]
[assembly: Unity.Jobs.RegisterGenericJobType(typeof(GameItemComponentDataSyncApply<GameOwner, GameItemOwner, GameItemOwnerSyncApplySystem.Converter, GameItemOwnerSyncApplySystem.Factory>))]

#if DEBUG
[assembly: RegisterEntityCommandProducerJob(typeof(GameItemComponentDataInit<GameItemObjectData, GameItemObjectInitSystem.Initializer, GameItemObjectInitSystem.Factory>))]
[assembly: RegisterEntityCommandProducerJob(typeof(GameItemComponentInit<GameItemName, GameItemNameInitSystem.Initializer>))]
[assembly: RegisterEntityCommandProducerJob(typeof(GameItemComponentDataInit<GameItemVariant, GameItemVariantInitSystem.Initializer, GameItemVariantInitSystem.Factory>))]
[assembly: RegisterEntityCommandProducerJob(typeof(GameItemComponentDataInit<GameItemOwner, GameItemOwnerInitSystem.Initializer, GameItemOwnerInitSystem.Factory>))]
#endif

public struct GameItemObjectData : ICleanupComponentData
{
    public int type;
}

public struct GameItemName : ICleanupComponentData
{
    public FixedString32Bytes value;
}

public struct GameItemVariant : ICleanupComponentData
{
    public int value;
}

public struct GameItemOwner : ICleanupComponentData
{
    public Entity entity;
}

[BurstCompile, UpdateInGroup(typeof(GameItemComponentInitSystemGroup), OrderFirst = true)]
public partial struct GameItemObjectInitSystem : ISystem
{
    public struct Initializer : IGameItemComponentInitializer<GameItemObjectData>
    {
        [ReadOnly]
        public UnsafeParallelHashMap<int, int> values;

        public bool IsVail(int type) => values.ContainsKey(type);

        public bool TryGetValue(in GameItemHandle handle, int type, out GameItemObjectData value)
        {
            if (values.TryGetValue(type, out int objectType))
            {
                value.type = objectType;

                return true;
            }

            value = default;

            return false;
        }
    }

    public struct Factory : IGameItemComponentFactory<Initializer>
    {
        public Initializer initializer;

        public Initializer Create(in GameItemManager.ReadOnlyInfos infos, in ArchetypeChunk chunk, int unfilteredChunkIndex)
        {
            return initializer;
        }
    }

    private GameItemComponentDataInitSystemCore<GameItemObjectData> __core;

    private UnsafeParallelHashMap<int, int> __values;

    public Initializer initializer
    {
        get
        {
            Initializer initializer;
            initializer.values = __values;
            return initializer;
        }
    }

    public void Create(System.Collections.Generic.KeyValuePair<int, int>[] values)
    {
        foreach (var value in values)
            __values.Add(value.Key, value.Value);
    }

    public void OnCreate(ref SystemState state)
    {
        __core = new GameItemComponentDataInitSystemCore<GameItemObjectData>(ref state);

        __values = new UnsafeParallelHashMap<int, int>(1, Allocator.Persistent);
    }

    public void OnDestroy(ref SystemState state)
    {
        __values.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        Factory factory;
        factory.initializer = initializer;
        __core.Update<Initializer, Factory>(factory, ref state);
    }
}

[BurstCompile, UpdateInGroup(typeof(GameItemComponentInitSystemGroup), OrderFirst = true)]
public partial struct GameItemNameInitSystem : IGameItemInitializationSystem<GameItemName, GameItemNameInitSystem.Initializer>
{
    public struct Initializer : IGameItemInitializer<GameItemName>
    {
        public GameItemObjectInitSystem.Initializer instance;

        public bool IsVail(int type) => instance.IsVail(type);

        public GameItemName GetValue(int type, int count)
        {
            GameItemName value;
            value.value = default;

            return value;
        }
    }

    private GameItemObjectInitSystem.Initializer __initializer;
    private GameItemComponentInitSystemCore<GameItemName> __core;

    public Initializer initializer
    {
        get
        {
            Initializer initializer;
            initializer.instance = __initializer;
            return initializer;
        }
    }

    public void OnCreate(ref SystemState state)
    {
        __initializer = state.World.GetOrCreateSystemUnmanaged<GameItemObjectInitSystem>().initializer;

        __core = new GameItemComponentInitSystemCore<GameItemName>(ref state);
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

[BurstCompile, UpdateInGroup(typeof(GameItemComponentInitSystemGroup), OrderFirst = true)]
public partial struct GameItemNameSyncInitSystem : ISystem
{
    public struct Converter : IGameItemComponentConverter<GameItemName, GameNickname>
    {
        public bool IsVail(int index)
        {
            return true;
        }

        public GameNickname Convert(in GameItemName value)
        {
            GameNickname result;
            result.value = value.value;
            return result;
        }
    }

    public struct Factory : IGameItemComponentConvertFactory<Converter>
    {
        public Converter Create(in ArchetypeChunk chunk, int unfilteredChunkIndex)
        {
            return default;
        }
    }

    private GameItemSyncInitSystemCore<GameNickname> __core;

    public void OnCreate(ref SystemState state)
    {
        __core = new GameItemSyncInitSystemCore<GameNickname>(ref state);
    }

    public void OnDestroy(ref SystemState state)
    {
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        Factory factory;
        __core.UpdateComponentData<GameNickname, GameItemName, Converter, Factory>(ref state, factory);
    }
}

[BurstCompile, UpdateInGroup(typeof(GameItemInitSystemGroup), OrderLast = true)]
public partial struct GameItemNameSyncApplySystem : ISystem
{
    public struct Converter : IGameItemComponentConverter<GameNickname, GameItemName>
    {
        public bool IsVail(int index)
        {
            return true;
        }

        public GameItemName Convert(in GameNickname value)
        {
            GameItemName result;
            result.value = new FixedString32Bytes(value.value);
            return result;
        }
    }

    public struct Factory : IGameItemComponentConvertFactory<Converter>
    {
        public Converter Create(in ArchetypeChunk chunk, int unfilteredChunkIndex)
        {
            return default;
        }
    }

    private GameItemSyncApplySystemCore<GameNickname> __core;

    public void OnCreate(ref SystemState state)
    {
        __core = new GameItemSyncApplySystemCore<GameNickname>(ref state);
    }

    public void OnDestroy(ref SystemState state)
    {
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        Factory factory;
        __core.UpdateComponentData<GameNickname, GameItemName, Converter, Factory>(ref state, factory);
    }
}

[BurstCompile, UpdateInGroup(typeof(GameItemComponentInitSystemGroup), OrderFirst = true)]
public partial struct GameItemVariantInitSystem : IGameItemInitializationSystem<GameItemVariant, GameItemVariantInitSystem.Initializer>
{
    public struct Initializer : IGameItemInitializer<GameItemVariant>, IGameItemComponentInitializer<GameItemVariant>
    {
        public Random random;
        public GameItemObjectInitSystem.Initializer instance;

        public bool IsVail(int type) => instance.IsVail(type);

        public GameItemVariant GetValue(int type, int count)
        {
            GameItemVariant value;
            value.value = random.NextInt();

            return value;
        }

        public bool TryGetValue(in GameItemHandle handle, int type, out GameItemVariant variant)
        {
            if (instance.IsVail(type))
            {
                variant.value = random.NextInt();

                return true;
            }

            variant = default;

            return false;
        }
    }

    public struct Factory : IGameItemComponentFactory<Initializer>
    {
        public double time;
        public GameItemObjectInitSystem.Initializer instance;

        public Initializer Create(in GameItemManager.ReadOnlyInfos infos, in ArchetypeChunk chunk, int unfilteredChunkIndex)
        {
            Initializer initializer;

            initializer.random = CreateRandom(time, unfilteredChunkIndex);
            initializer.instance = instance;

            return initializer;
        }
    }

    private double __time;
    private GameItemComponentDataInitSystemCore<GameItemVariant> __core;
    private GameItemObjectInitSystem.Initializer __initializer;

    public Initializer initializer
    {
        get
        {
            Initializer initializer;
            initializer.random = CreateRandom(__time, 0);
            initializer.instance = __initializer;
            return initializer;
        }
    }

    public void OnCreate(ref SystemState state)
    {
        __core = new GameItemComponentDataInitSystemCore<GameItemVariant>(ref state);

        __initializer = state.World.GetOrCreateSystemUnmanaged<GameItemObjectInitSystem>().initializer;
    }

    public void OnDestroy(ref SystemState state)
    {
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        __time = state.WorldUnmanaged.Time.ElapsedTime;

        Factory factory;
        factory.time = __time;
        factory.instance = __initializer;

        __core.Update<Initializer, Factory>(factory, ref state);
    }

    public static Random CreateRandom(double time, int index)
    {
        long hash = math.aslong(time);
        return new Random(math.max(1, (uint)hash ^ (uint)(hash >> 32) ^ (uint)index));
    }
}

[BurstCompile, UpdateInGroup(typeof(GameItemComponentInitSystemGroup), OrderFirst = true)]
public partial struct GameItemVariantSyncInitSystem : ISystem
{
    public struct Converter : IGameItemComponentConverter<GameItemVariant, GameVariant>
    {
        public bool IsVail(int index)
        {
            return true;
        }

        public GameVariant Convert(in GameItemVariant value)
        {
            GameVariant result;
            result.value = value.value;
            return result;
        }
    }

    public struct Factory : IGameItemComponentConvertFactory<Converter>
    {
        public Converter Create(in ArchetypeChunk chunk, int unfilteredChunkIndex)
        {
            return default;
        }
    }

    private GameItemSyncInitSystemCore<GameVariant> __core;

    public void OnCreate(ref SystemState state)
    {
        __core = new GameItemSyncInitSystemCore<GameVariant>(ref state);
    }

    public void OnDestroy(ref SystemState state)
    {
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        Factory factory;
        __core.UpdateComponentData<GameVariant, GameItemVariant, Converter, Factory>(ref state, factory);
    }
}

[BurstCompile, UpdateInGroup(typeof(GameItemInitSystemGroup), OrderLast = true)]
public partial struct GameItemVariantSyncApplySystem : ISystem
{
    public struct Converter : IGameItemComponentConverter<GameVariant, GameItemVariant>
    {
        public bool IsVail(int index)
        {
            return true;
        }

        public GameItemVariant Convert(in GameVariant value)
        {
            GameItemVariant result;
            result.value = value.value;
            return result;
        }
    }

    public struct Factory : IGameItemComponentConvertFactory<Converter>
    {
        public Converter Create(in ArchetypeChunk chunk, int unfilteredChunkIndex)
        {
            return default;
        }
    }

    private GameItemSyncApplySystemCore<GameVariant> __core;

    public void OnCreate(ref SystemState state)
    {
        __core = new GameItemSyncApplySystemCore<GameVariant>(ref state);
    }

    public void OnDestroy(ref SystemState state)
    {
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        Factory factory;
        __core.UpdateComponentData<GameVariant, GameItemVariant, Converter, Factory>(ref state, factory);
    }
}

[BurstCompile, UpdateInGroup(typeof(GameItemComponentInitSystemGroup), OrderFirst = true)]
public partial struct GameItemOwnerInitSystem : ISystem
{
    public struct Initializer : IGameItemComponentInitializer<GameItemOwner>
    {
        public GameItemObjectInitSystem.Initializer instance;

        public GameItemManager.ReadOnlyInfos infos;

        [ReadOnly]
        public SharedHashMap<GameItemHandle, Entity>.Reader rootEntities;

        public bool TryGetValue(in GameItemHandle handle, int type, out GameItemOwner value)
        {
            if (!instance.IsVail(type))
            {
                value = default;

                return false;
            }

            var root = infos.GetRoot(handle);

            rootEntities.TryGetValue(root, out value.entity);

            return true;
        }
    }

    public struct Factory : IGameItemComponentFactory<Initializer>
    {
        public GameItemObjectInitSystem.Initializer instance;

        [ReadOnly]
        public SharedHashMap<GameItemHandle, Entity>.Reader rootEntities;

        public Initializer Create(in GameItemManager.ReadOnlyInfos infos, in ArchetypeChunk chunk, int unfilteredChunkIndex)
        {
            Initializer initializer;
            initializer.instance = instance;
            initializer.infos = infos;
            initializer.rootEntities = rootEntities;
            return initializer;
        }
    }

    private GameItemComponentDataInitSystemCore<GameItemOwner> __core;
    private GameItemObjectInitSystem.Initializer __initializer;
    private SharedHashMap<GameItemHandle, Entity> __rootEntities;

    public void OnCreate(ref SystemState state)
    {
        __core = new GameItemComponentDataInitSystemCore<GameItemOwner>(ref state);

        __initializer = state.World.GetOrCreateSystemUnmanaged<GameItemObjectInitSystem>().initializer;

        __rootEntities = state.World.GetOrCreateSystemManaged<GameItemRootEntitySystem>().entities;
    }

    public void OnDestroy(ref SystemState state)
    {
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        ref var lookupJobManager = ref __rootEntities.lookupJobManager;

        state.Dependency = Unity.Jobs.JobHandle.CombineDependencies(state.Dependency, lookupJobManager.readOnlyJobHandle);

        Factory factory;
        factory.instance = __initializer;
        factory.rootEntities = __rootEntities.reader;

        __core.Update<Initializer, Factory>(factory, ref state);

        lookupJobManager.AddReadOnlyDependency(state.Dependency);
    }
}

[BurstCompile, UpdateInGroup(typeof(GameItemComponentInitSystemGroup), OrderFirst = true)]
public partial struct GameItemOwnerSyncInitSystem : ISystem
{
    public struct Converter : IGameItemComponentConverter<GameItemOwner, GameOwner>
    {
        public bool IsVail(int index)
        {
            return true;
        }

        public GameOwner Convert(in GameItemOwner value)
        {
            GameOwner result;
            result.entity = value.entity;
            return result;
        }
    }

    public struct Factory : IGameItemComponentConvertFactory<Converter>
    {
        public Converter Create(in ArchetypeChunk chunk, int unfilteredChunkIndex)
        {
            return default;
        }
    }

    private GameItemSyncInitSystemCore<GameOwner> __core;

    public void OnCreate(ref SystemState state)
    {
        __core = new GameItemSyncInitSystemCore<GameOwner>(ref state);
    }

    public void OnDestroy(ref SystemState state)
    {
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        Factory factory;
        __core.UpdateComponentData<GameOwner, GameItemOwner, Converter, Factory>(ref state, factory);
    }
}

[BurstCompile, UpdateInGroup(typeof(GameItemInitSystemGroup), OrderLast = true)]
public partial struct GameItemOwnerSyncApplySystem : ISystem
{
    public struct Converter : IGameItemComponentConverter<GameOwner, GameItemOwner>
    {
        [ReadOnly]
        public NativeArray<GameNodeStatus> states;

        public bool IsVail(int index)
        {
            return (states[index].value & GameNodeStatus.OVER) != GameNodeStatus.OVER;
        }

        public GameItemOwner Convert(in GameOwner value)
        {
            GameItemOwner result;
            result.entity = value.entity;
            return result;
        }
    }

    public struct Factory : IGameItemComponentConvertFactory<Converter>
    {
        [ReadOnly]
        public ComponentTypeHandle<GameNodeStatus> statusType;

        public Converter Create(in ArchetypeChunk chunk, int unfilteredChunkIndex)
        {
            Converter converter;
            converter.states = chunk.GetNativeArray(ref statusType);
            return converter;
        }
    }

    private GameItemSyncApplySystemCore<GameOwner> __core;

    public void OnCreate(ref SystemState state)
    {
        __core = new GameItemSyncApplySystemCore<GameOwner>(ref state);
    }

    public void OnDestroy(ref SystemState state)
    {
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        Factory factory;
        factory.statusType = state.GetComponentTypeHandle<GameNodeStatus>(true);
        __core.UpdateComponentData<GameOwner, GameItemOwner, Converter, Factory>(ref state, factory);
    }
}

[BurstCompile, UpdateInGroup(typeof(GameItemComponentInitSystemGroup))]
public partial struct GameItemObjectStructChangeSystem : ISystem
{
    private EntityQuery __instanceGroup;
    private EntityQuery __nameGroup;
    private EntityQuery __variantGroup;
    private EntityQuery __ownerGroup;

    public void OnCreate(ref SystemState state)
    {
        __instanceGroup = state.GetEntityQuery(
            ComponentType.ReadOnly<GameItemObjectData>(),
            ComponentType.Exclude<GameItemData>());

        __nameGroup = state.GetEntityQuery(
            ComponentType.ReadOnly<GameItemName>(),
            ComponentType.Exclude<GameItemData>());

        __variantGroup = state.GetEntityQuery(
            ComponentType.ReadOnly<GameItemVariant>(),
            ComponentType.Exclude<GameItemData>());

        __ownerGroup = state.GetEntityQuery(
            ComponentType.ReadOnly<GameItemOwner>(),
            ComponentType.Exclude<GameItemData>());
    }

    public void OnDestroy(ref SystemState state)
    {

    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var entityManager = state.EntityManager;
        entityManager.RemoveComponent<GameItemObjectData>(__instanceGroup);
        entityManager.RemoveComponent<GameItemName>(__nameGroup);
        entityManager.RemoveComponent<GameItemVariant>(__variantGroup);
        entityManager.RemoveComponent<GameItemOwner>(__ownerGroup);
    }
}
