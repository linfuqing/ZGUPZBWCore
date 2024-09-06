using Unity.Collections;
using Unity.Entities;
using Unity.Burst;
using Unity.Jobs;

/*[assembly: RegisterGenericJobType(typeof(GameItemComponentDataSyncInit<GameLevel, GameItemLevel, GameItemLevelSyncInitSystem.Converter, GameItemLevelSyncInitSystem.Factory>))]
[assembly: RegisterGenericJobType(typeof(GameItemComponentDataSyncApply<GameLevel, GameItemLevel, GameItemLevelSyncApplySystem.Converter, GameItemLevelSyncApplySystem.Factory>))]

[assembly: RegisterGenericJobType(typeof(GameItemComponentDataSyncInit<GameExp, GameItemExp, GameItemExpSyncInitSystem.Converter, GameItemExpSyncInitSystem.Factory>))]
[assembly: RegisterGenericJobType(typeof(GameItemComponentDataSyncApply<GameExp, GameItemExp, GameItemExpSyncApplySystem.Converter, GameItemExpSyncApplySystem.Factory>))]

[assembly: RegisterGenericJobType(typeof(GameItemComponentDataSyncInit<GamePower, GameItemPower, GameItemPowerSyncInitSystem.Converter, GameItemPowerSyncInitSystem.Factory>))]
[assembly: RegisterGenericJobType(typeof(GameItemComponentDataSyncApply<GamePower, GameItemPower, GameItemPowerSyncApplySystem.Converter, GameItemPowerSyncApplySystem.Factory>))]*/

public struct GameVariant : IComponentData
{
    public int value;
}

public struct GameNickname : IComponentData
{
    public FixedString128Bytes value;
}

/*public struct GameLevel : IComponentData
{
    public int handle;
}

public struct GameExp : IComponentData
{
    public float value;
}

public struct GamePower : IComponentData
{
    public float value;
}

[BurstCompile, UpdateInGroup(typeof(GameItemComponentInitSystemGroup), OrderFirst = true)]
public partial struct GameItemLevelSyncInitSystem : ISystem
{
    public struct Converter : IGameItemComponentConverter<GameItemLevel, GameLevel>
    {
        public bool IsVail(int index)
        {
            return true;
        }

        public GameLevel Convert(in GameItemLevel value)
        {
            GameLevel result;
            result.handle = value.handle;
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

    private GameItemSyncInitSystemCore<GameLevel> __core;

    public void OnCreate(ref SystemState state)
    {
        __core = new GameItemSyncInitSystemCore<GameLevel>(ref state);
    }

    public void OnDestroy(ref SystemState state)
    {
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        Factory factory;
        __core.UpdateComponentData<GameLevel, GameItemLevel, Converter, Factory>(ref state, factory);
    }
}

[BurstCompile, UpdateInGroup(typeof(GameItemInitSystemGroup), OrderLast = true)]
public partial struct GameItemLevelSyncApplySystem : ISystem
{
    public struct Converter : IGameItemComponentConverter<GameLevel, GameItemLevel>
    {
        public bool IsVail(int index)
        {
            return true;
        }

        public GameItemLevel Convert(in GameLevel value)
        {
            GameItemLevel result;
            result.handle = value.handle;
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

    private GameItemSyncApplySystemCore<GameLevel> __core;

    public void OnCreate(ref SystemState state)
    {
        __core = new GameItemSyncApplySystemCore<GameLevel>(ref state);
    }

    public void OnDestroy(ref SystemState state)
    {
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        Factory factory;
        __core.UpdateComponentData<GameLevel, GameItemLevel, Converter, Factory>(ref state, factory);
    }
}

[BurstCompile, UpdateInGroup(typeof(GameItemComponentInitSystemGroup), OrderFirst = true)]
public partial struct GameItemExpSyncInitSystem : ISystem
{
    public struct Converter : IGameItemComponentConverter<GameItemExp, GameExp>
    {
        public bool IsVail(int index)
        {
            return true;
        }

        public GameExp Convert(in GameItemExp value)
        {
            GameExp result;
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

    private GameItemSyncInitSystemCore<GameExp> __core;

    public void OnCreate(ref SystemState state)
    {
        __core = new GameItemSyncInitSystemCore<GameExp>(ref state);
    }

    public void OnDestroy(ref SystemState state)
    {
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        Factory factory;
        __core.UpdateComponentData<GameExp, GameItemExp, Converter, Factory>(ref state, factory);
    }
}

[BurstCompile, UpdateInGroup(typeof(GameItemInitSystemGroup), OrderLast = true)]
public partial struct GameItemExpSyncApplySystem : ISystem
{
    public struct Converter : IGameItemComponentConverter<GameExp, GameItemExp>
    {
        public bool IsVail(int index)
        {
            return true;
        }

        public GameItemExp Convert(in GameExp value)
        {
            GameItemExp result;
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

    private GameItemSyncApplySystemCore<GameExp> __core;

    public void OnCreate(ref SystemState state)
    {
        __core = new GameItemSyncApplySystemCore<GameExp>(ref state);
    }

    public void OnDestroy(ref SystemState state)
    {
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        Factory factory;
        __core.UpdateComponentData<GameExp, GameItemExp, Converter, Factory>(ref state, factory);
    }
}

[BurstCompile, UpdateInGroup(typeof(GameItemComponentInitSystemGroup), OrderFirst = true)]
public partial struct GameItemPowerSyncInitSystem : ISystem
{
    public struct Converter : IGameItemComponentConverter<GameItemPower, GamePower>
    {
        public bool IsVail(int index)
        {
            return true;
        }

        public GamePower Convert(in GameItemPower value)
        {
            GamePower result;
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

    private GameItemSyncInitSystemCore<GamePower> __core;

    public void OnCreate(ref SystemState state)
    {
        __core = new GameItemSyncInitSystemCore<GamePower>(ref state);
    }

    public void OnDestroy(ref SystemState state)
    {
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        Factory factory;
        __core.UpdateComponentData<GamePower, GameItemPower, Converter, Factory>(ref state, factory);
    }
}

[BurstCompile, UpdateInGroup(typeof(GameItemInitSystemGroup), OrderLast = true)]
public partial struct GameItemPowerSyncApplySystem : ISystem
{
    public struct Converter : IGameItemComponentConverter<GamePower, GameItemPower>
    {
        public bool IsVail(int index)
        {
            return true;
        }

        public GameItemPower Convert(in GamePower value)
        {
            GameItemPower result;
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

    private GameItemSyncApplySystemCore<GamePower> __core;

    public void OnCreate(ref SystemState state)
    {
        __core = new GameItemSyncApplySystemCore<GamePower>(ref state);
    }

    public void OnDestroy(ref SystemState state)
    {
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        Factory factory;
        __core.UpdateComponentData<GamePower, GameItemPower, Converter, Factory>(ref state, factory);
    }
}*/