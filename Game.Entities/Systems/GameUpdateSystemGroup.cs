using Unity.Burst;
using Unity.Entities;
using ZG;

public struct GameUpdateFrameCount : IComponentData
{
    public uint value;
}

public struct GameUpdateTime
{
    public readonly EntityQuery Group;

    public readonly GameRollbackTime RollbackTime;

    public uint frameCount => Group.GetSingleton<GameUpdateFrameCount>().value;

    public GameTime delta => new GameTime(frameCount, RollbackTime.frameDelta);

    //public uint frameIndex => rollbackTime.frame.index % frameCount;

    public GameUpdateTime(ref SystemState systemState)
    {
        Group = systemState.GetEntityQuery(
            new EntityQueryDesc()
            { 
                All = new ComponentType[]
                {
                    ComponentType.ReadOnly<GameUpdateFrameCount>()
                },
                Options = EntityQueryOptions.IncludeSystems
            });

        RollbackTime = new GameRollbackTime(ref systemState);
    }

    public bool IsVail(int offset = 0)
    {
        return (RollbackTime.frameIndex + offset) % frameCount == 0;
    }
}

[BurstCompile, UpdateInGroup(typeof(GameRollbackSystemGroup))]
public partial struct GameUpdateSystemGroup : ISystem
{
    private WorldTimeWrapper __worldTime;
    private GameUpdateTime __time;
    private SystemGroup __systemGroup;

    public const uint UPDATE_FRAME_COUNT = 1;

    public bool isUpdate => __time.IsVail(0);

    public void OnCreate(ref SystemState state)
    {
        GameUpdateFrameCount frameCount;
        frameCount.value = UPDATE_FRAME_COUNT;
        var entityManager = state.EntityManager;
        entityManager.AddComponentData(state.SystemHandle, frameCount);

        __worldTime = WorldTimeWrapper.GetOrCreate(ref state);

        __time = new GameUpdateTime(ref state);

        __systemGroup = SystemGroupUtility.GetOrCreateSystemGroup(state.World, typeof(GameUpdateSystemGroup));

        //__syncSystemGroup.onRollback += __OnRollback;
    }

    public void OnDestroy(ref SystemState state)
    {
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!isUpdate)
            return;

        /*#if UNITY_DOTSPLAYER
                var source = Time;
                var destination = new Unity.Core.TimeData(now, updateDelta);
                var world = World;
                world.SetTime(destination);
#else
    float fixedDeltaTime = UnityEngine.Time.fixedDeltaTime;
                UnityEngine.Time.fixedDeltaTime = updateDelta;
        #endif*/

        __worldTime.PushTime(new Unity.Core.TimeData(__time.RollbackTime.now, __time.delta));

        var world = state.WorldUnmanaged;
        __systemGroup.Update(ref world);

        __worldTime.PopTime();

        /*#if UNITY_DOTSPLAYER
                world.SetTime(source);
        #else
                UnityEngine.Time.fixedDeltaTime = fixedDeltaTime;
        #endif*/
    }
}