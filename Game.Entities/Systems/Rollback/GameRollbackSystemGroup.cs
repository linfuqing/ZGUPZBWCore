using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Jobs;
using Unity.Burst;
using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;
using ZG;

public struct GameRollbackFrameDelta : IComponentData
{
    public float value;
}

public struct GameRollbackFrameOffset : IComponentData
{
    public int value;
}

public struct GameRollbackFrame
{
    public readonly EntityQuery group;

    public int offset
    {
        get => group.GetSingleton<GameRollbackFrameOffset>().value;
    }

    public uint index
    {
        get
        {
            return (uint)(offset + group.GetSingleton<RollbackFrame>().index);
        }
    }

    public GameRollbackFrame(ref SystemState systemState)
    {
        group = systemState.GetEntityQuery(
            new EntityQueryDesc()
            {
                All = new ComponentType[]
                {
                    ComponentType.ReadOnly<RollbackFrame>(), 
                    ComponentType.ReadOnly<GameRollbackFrameOffset>()
                },
                Options = EntityQueryOptions.IncludeSystems
            });
    }
}

public struct GameRollbackTime
{
    public readonly EntityQuery Group;

    public int frameOffset
    {
        get => Group.GetSingleton<GameRollbackFrameOffset>().value;


        set
        {
            GameRollbackFrameOffset offset;
            offset.value = value;
            Group.SetSingleton(offset);
        }
    }

    public uint frameIndex
    {
        get
        {
            return (uint)(frameOffset + Group.GetSingleton<RollbackFrame>().index);
        }
    }

    public float frameDelta
    {
        get
        {
            return Group.GetSingleton<GameRollbackFrameDelta>().value;
        }
    }

    public GameTime now => new GameTime(frameIndex, frameDelta);

    public GameRollbackTime(ref SystemState systemState)
    {
        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            Group = builder
                .WithAll<RollbackFrame, GameRollbackFrameOffset, GameRollbackFrameDelta>()
                .WithOptions(EntityQueryOptions.IncludeSystems)
                .Build(ref systemState);
    }
}

public struct GameRollbackManager
{
    private FrameSyncSystemGroup __frameSyncSystemGroup;

    private GameRollbackTime __time;

    public bool isClear => __frameSyncSystemGroup.isClear;

    public FrameSyncFlag.Value flag
    {
        get => __frameSyncSystemGroup.flag;

        set
        {
            __frameSyncSystemGroup.flag = value;
        }
    }

    public uint maxFrameCount
    {
        get => __frameSyncSystemGroup.maxFrameCount;

        set => __frameSyncSystemGroup.maxFrameCount = value;
    }

    public int frameOffset
    {
        get => __time.frameOffset;
    }

    public uint frameIndex => (uint)(__frameSyncSystemGroup.frameIndex + frameOffset);

    public uint realFrameIndex => (uint)(__frameSyncSystemGroup.realFrameIndex + frameOffset);

    public uint syncFrameIndex => (uint)(__frameSyncSystemGroup.syncFrameIndex + frameOffset);

    public uint clearFrameIndex => (uint)(__frameSyncSystemGroup.clearFrameIndex + frameOffset);

    public float delta => __time.frameDelta;

    public GameTime now => new GameTime(frameIndex, delta);//__timeSystemGroup.now - __timeSystemGroup.delta * (realFrameIndex - frameIndex);

    public GameTime time => new GameTime(realFrameIndex, delta);//__timeSystemGroup.now;

    public void Clear()
    {
        //containerManager.Clear();

        __frameSyncSystemGroup.Clear();
    }

#if ZG_LEGACY_ROLLBACK
    public new void Move(long type, uint frameIndex, uint syncFrameIndex)
    {
        int frameOffset = this.frameOffset;

        base.Move(type, (uint)(frameIndex - frameOffset), (uint)(syncFrameIndex - frameOffset));
    }
#else
    public void Move(long type, uint frameIndex, uint syncFrameIndex)
    {
        int frameOffset = this.frameOffset;

        __frameSyncSystemGroup.commander.Move(type, (uint)(frameIndex - frameOffset), (uint)(syncFrameIndex - frameOffset));
    }
#endif

#if ZG_LEGACY_ROLLBACK
    public new void Invoke(uint frameIndex, long type, Action value, Action clear)
    {
        base.Invoke((uint)(frameIndex - frameOffset), type, value, clear);
    }
#else
    public void InvokeAll()
    {
        __frameSyncSystemGroup.commander.InvokeAll();
    }

    public EntityCommander Invoke(uint frameIndex, in RollbackEntry entry, Action clear)
    {
        return __frameSyncSystemGroup.commander.Invoke((uint)(frameIndex - frameOffset), entry, clear);
    }
#endif

    public static readonly ComponentType[] ComponentTypes = new ComponentType[]
    {
        ComponentType.ReadOnly<GameRollbackFrameDelta>(),
        ComponentType.ReadOnly<GameRollbackFrameOffset>()//,
        //ComponentType.ReadOnly<GameRollbackManager>()
    };

    public GameRollbackManager(ref SystemState systemState)
    {
        __frameSyncSystemGroup = new FrameSyncSystemGroup(ref systemState);

        var entityManager = systemState.EntityManager;
        var systemHandle = systemState.SystemHandle;
        entityManager.AddComponent(systemHandle, new ComponentTypeSet(ComponentTypes));

        GameRollbackFrameDelta delta;
        delta.value = UnityEngine.Time.fixedDeltaTime;
        entityManager.SetComponentData(systemHandle, delta);

        __time = new GameRollbackTime(ref systemState);

        //entityManager.SetComponentData(systemHandle, this);
    }

    public void Update(ref WorldUnmanaged world, int rollbackFrameCount = 3)
    {
        var flag = __frameSyncSystemGroup.flag;
        if ((flag & FrameSyncFlag.Value.Clear) == FrameSyncFlag.Value.Clear && (realFrameIndex % rollbackFrameCount) == 0)
            __frameSyncSystemGroup.Update(ref world);
        else
        {
            __frameSyncSystemGroup.flag &= ~FrameSyncFlag.Value.Rollback;
            __frameSyncSystemGroup.Update(ref world);
            __frameSyncSystemGroup.flag = flag;
        }
    }
}

[BurstCompile, UpdateInGroup(typeof(GameSyncSystemGroup)), SystemGroupInherit(typeof(FrameSyncSystemGroup))]
public partial struct GameRollbackSystemGroup : ISystem
{
    public GameRollbackManager manager
    {
        get;

        private set;
    }

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        manager = new GameRollbackManager(ref state);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var world = state.WorldUnmanaged;
        manager.Update(ref world);
    }
}
