using System.Collections.Generic;
using Unity.Jobs;
using Unity.Burst;
using Unity.Entities;
using Unity.Collections;
using ZG;

public enum GameTransformUpdateType
{
    None,
    Destination,
    All
}

[/*AlwaysSynchronizeSystem, */UpdateInGroup(typeof(InitializationSystemGroup)), UpdateAfter(typeof(EntityObjectSystemGroup))]
public partial class GameTransformFactroySystemGroup : ComponentSystemGroup
{

}

[BurstCompile, UpdateInGroup(typeof(GameUpdateSystemGroup)), UpdateAfter(typeof(GameNodeCharacterSystemGroup))]
public partial struct GameTransformSimulationSystemGroup : ISystem
{
    private SystemGroup __sysetmGroup;

    public void OnCreate(ref SystemState state)
    {
        __sysetmGroup = state.World.GetOrCreateSystemGroup(typeof(GameTransformSimulationSystemGroup));
    }

    public void OnDestroy(ref SystemState state)
    {
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var world = state.WorldUnmanaged;
        __sysetmGroup.Update(ref world);
    }

}

public struct GameTransformFactory<TTransform, TVelocity, THandler>
    where TTransform : unmanaged, IGameTransform<TTransform>
    where TVelocity : struct, IGameTransformVelocity<TTransform>
    where THandler : struct, IGameTransformEntityHandler<TTransform>
{
    public interface IFactory<T> where T : struct, IGameTransformEntityHandler<TTransform>
    {
        public T Get(ref SystemState state); 
    }

    private EntityQuery __groupToCreate;
    private EntityQuery __groupToDestroy;

    public GameTransformFactory(ComponentType[] componentTypes, ref SystemState systemState)
    {
        BurstUtility.InitializeJobParallelFor<GameTransformFactoryCopy<TTransform, TVelocity, THandler>>();

        var results = new List<ComponentType>(4);
        results.Add(ComponentType.ReadOnly<GameTransformVelocity<TTransform, TVelocity>>());
        results.AddRange(componentTypes);
        //componentTypes.Add(ComponentType.Exclude<GameTransformSource<TTransform>>());
        results.Add(ComponentType.Exclude<GameTransformKeyframe<TTransform>>());

        __groupToCreate = systemState.GetEntityQuery(results.ToArray());

        __groupToDestroy = systemState.GetEntityQuery(
            new EntityQueryDesc()
            {
                All = new ComponentType[]
                {
                    ComponentType.ReadOnly<GameTransformKeyframe<TTransform>>(),
                },
                None = new ComponentType[]
                {
                    typeof(GameTransformVelocity<TTransform, TVelocity>)
                }
            },
            new EntityQueryDesc()
            {
                All = new ComponentType[]
                {
                    ComponentType.ReadOnly<GameTransformKeyframe<TTransform>>(),
                    ComponentType.ReadOnly<Disabled>(),
                },

                Options = EntityQueryOptions.IncludeDisabledEntities
            });
    }

    public void Update<TFactory>(int innerloopBatchCount, in GameTime time, ref SystemState systemState, in TFactory factory)
        //where THandler : struct, IGameTransformEntityHandler<TTransform>
        where TFactory : struct, IFactory<THandler>
    {
        EntityManager entityManager = systemState.EntityManager;

        if (!__groupToDestroy.IsEmptyIgnoreFilter)
        {
            entityManager.RemoveComponent(__groupToDestroy, new ComponentTypeSet(
                ComponentType.ReadWrite<GameTransformKeyframe<TTransform>>()));
        }

        if (!__groupToCreate.IsEmptyIgnoreFilter)
        {
            systemState.CompleteDependency();

            GameTransformFactoryCopy<TTransform, TVelocity, THandler> copy;
            copy.time = time;
            copy.entityArray = __groupToCreate.ToEntityArrayBurstCompatible(systemState.GetEntityTypeHandle(), Allocator.TempJob);

            entityManager.AddComponent<GameTransformKeyframe<TTransform>>(__groupToCreate);

            copy.keyframes = systemState.GetBufferLookup<GameTransformKeyframe<TTransform>>();

            copy.handler = factory.Get(ref systemState);

            systemState.Dependency = copy.Schedule(copy.entityArray.Length, innerloopBatchCount, systemState.Dependency);
        }
    }
}

public struct GameTransformSimulator<T> where T : unmanaged, IGameTransform<T> 
{
    private EntityQuery __group;

    public GameTransformSimulator(ComponentType[] componentTypes, ref SystemState systemState)
    {
        List<ComponentType> results = new List<ComponentType>(4);
        results.AddRange(componentTypes);
        results.Add(ComponentType.ReadWrite<GameTransformKeyframe<T>>());

        __group = systemState.GetEntityQuery(
            new EntityQueryDesc()
            {
                All = results.ToArray(),
                Options = EntityQueryOptions.FilterWriteGroup
            });
    }

    public void Update<THandler, TFactory>(/*GameTransformUpdateType updateType, */GameTime time, ref SystemState state, in TFactory factory)
        where THandler : struct, IGameTransformHandler<T>
        where TFactory : struct, IGameTransformFactory<T, THandler>
    {
        GameTransformUpdateAll<T, THandler, TFactory> updateAll;
        updateAll.time = time;
        updateAll.keyframeType = state.GetBufferTypeHandle<GameTransformKeyframe<T>>();
        updateAll.factory = factory;

        state.Dependency = updateAll.ScheduleParallel(__group, state.Dependency);

        /*switch (updateType)
        {
            case GameTransformUpdateType.Destination:
                /*GameTransformUpdateDestinations<T, THandler, TFactory> updateDestinations;
                updateDestinations.time = time;
                updateDestinations.destinationType = state.GetComponentTypeHandle<GameTransformDestination<T>>();
                updateDestinations.factory = factory;

                state.Dependency = updateDestinations.ScheduleParallel(__group, state.Dependency);
                break;/
            case GameTransformUpdateType.All:
                GameTransformUpdateAll<T, THandler, TFactory> updateAll;
                updateAll.time = time;
                updateAll.keyframeType = state.GetBufferTypeHandle<GameTransformKeyframe<T>>();
                updateAll.factory = factory;

                state.Dependency = updateAll.ScheduleParallel(__group, state.Dependency);
                break;
        }*/
    }
}

//[UpdateInGroup(typeof(TimeSystemGroup)), UpdateAfter(typeof(GameSyncSystemGroup))]
//[UpdateInGroup(typeof(PresentationSystemGroup))]
public struct GameTransformApplication<TTransform, TVelocity>
    where TTransform : unmanaged, IGameTransform<TTransform>
    where TVelocity : unmanaged, IGameTransformVelocity<TTransform>
{
    //private EntityQuery __utcGroup;
    /*private EntityQuery __syncGroup;
    private EntityQuery __updateGroup;*/
    private GameUpdateTime __time;

    private EntityQuery __animationElpasedTimeGroup;

    public EntityQuery group
    {
        get;
    }

    public GameTransformApplication(ComponentType[] componentTypes, ref SystemState systemState)
    {
        //__utcGroup = systemState.GetEntityQuery(ComponentType.ReadOnly<GameUTCData>());
        /*__syncGroup = systemState.GetEntityQuery(ComponentType.ReadOnly<GameSyncData>());
        __updateGroup = systemState.GetEntityQuery(ComponentType.ReadOnly<GameUpdateData>());*/

        __time = new GameUpdateTime(ref systemState);

        __animationElpasedTimeGroup = GameAnimationElapsedTime.GetEntityQuery(ref systemState);

        var results = new List<ComponentType>();
        results.Add(ComponentType.ReadWrite<GameTransformKeyframe<TTransform>>());
        results.Add(ComponentType.ReadWrite<GameTransformVelocity<TTransform, TVelocity>>());
        results.AddRange(componentTypes);

        group = systemState.GetEntityQuery(results.ToArray());
    }
    
    public void Update<T>(ref SystemState systemState, in T job) where T : IGameTransformJob<TTransform, GameTransformCalculator<TTransform, TVelocity>>
    {
        //var utcData = __utcGroup.GetSingleton<GameUTCData>();
        /*var syncData = __syncGroup.GetSingleton<GameSyncData>();
        var updateData = __updateGroup.GetSingleton<GameUpdateData>();*/

        GameTransformSmoothDamp<TTransform, TVelocity, T> smoothDamp;
        smoothDamp.smoothTime = __time.delta;// updateData.GetDelta(syncData.delta);
        smoothDamp.deltaTime = systemState.WorldUnmanaged.Time.DeltaTime;
        smoothDamp.time = __animationElpasedTimeGroup.GetSingleton<GameAnimationElapsedTime>().value;// syncData.animationElapsedTime;
        smoothDamp.keyframeType = systemState.GetBufferTypeHandle<GameTransformKeyframe<TTransform>>();
        smoothDamp.velocityType = systemState.GetComponentTypeHandle<GameTransformVelocity<TTransform, TVelocity>>();
        smoothDamp.job = job;

        systemState.Dependency = smoothDamp.ScheduleParallel(group, systemState.Dependency);
    }
}

public struct GameTransformSimulatorEx<T> where T : unmanaged, IGameTransform<T>
{
    /*private EntityQuery __syncGroup;
    private EntityQuery __updateGroup;*/
    private GameRollbackTime __time;
    private GameTransformSimulator<T> __instance;

    public GameTransformSimulatorEx(ComponentType[] componentTypes, ref SystemState systemState)
    {
        /*__syncGroup = systemState.GetEntityQuery(ComponentType.ReadOnly<GameSyncData>());
        __updateGroup = systemState.GetEntityQuery(ComponentType.ReadOnly<GameUpdateData>());*/

        __time = new GameRollbackTime(ref systemState);

        __instance = new GameTransformSimulator<T>(componentTypes, ref systemState);
    }

    public void Update<THandler, TFactory>(ref SystemState state, in TFactory factory)
        where THandler : struct, IGameTransformHandler<T>
        where TFactory : struct, IGameTransformFactory<T, THandler>
    {
        //var syncData = __syncGroup.GetSingleton<GameSyncData>();

        __instance.Update<THandler, TFactory>(
            /*GameTransformUtility.GetUpdateType(
                syncData.realFrameIndex,
                syncData.frameIndex,
                __updateGroup.GetSingleton<GameUpdateData>().frameCount),*/
            //syncData.now,
            __time.now, 
            ref state,
            factory);
    }
}

public static class GameTransformUtility
{
    public static GameTransformUpdateType GetUpdateType(uint realFrameIndex, uint frameIndex, uint frameCount)
    {
        uint offsetFrameIndex = frameIndex + frameCount;
        if (offsetFrameIndex < realFrameIndex)
            return GameTransformUpdateType.None;

        return offsetFrameIndex == realFrameIndex ? GameTransformUpdateType.Destination : GameTransformUpdateType.All;
    }
}
