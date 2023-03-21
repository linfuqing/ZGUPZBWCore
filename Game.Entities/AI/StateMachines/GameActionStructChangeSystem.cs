using Unity.Collections;
using Unity.Mathematics;
using Unity.Entities;
using Unity.Burst;
using Unity.Transforms;
using ZG;

[BurstCompile, AutoCreateIn("Server"), UpdateInGroup(typeof(TimeSystemGroup)), UpdateBefore(typeof(GameNavMeshSystemGroup)), UpdateAfter(typeof(StateMachineExecutorGroup))]
public partial struct GameActionStructChangeSystem : ISystem
{
    //private EntityQuery __group;

    public EntityComponentAssigner assigner
    {
        get;

        private set;
    }

    public EntityCommandStructChangeManager manager
    {
        get;

        private set;
    }

    public EntityAddDataPool addDataCommander => new EntityAddDataPool(manager.addComponentPool, assigner);

    public void OnCreate(ref SystemState state)
    {
        /*state.SetAlwaysUpdateSystem(true);

        __group = state.GetEntityQuery(new EntityQueryDesc()
        {
            All = new ComponentType[]
            {
                    ComponentType.ReadOnly<StateMachineInfo>(),
                    ComponentType.ReadOnly<StateMachineStatus>()
            },
            Options = EntityQueryOptions.IncludeDisabled
        });*/

        assigner = new EntityComponentAssigner(Allocator.Persistent);

        manager = new EntityCommandStructChangeManager(Allocator.Persistent);
    }

    public void OnDestroy(ref SystemState state)
    {
        manager.Dispose();

        assigner.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        manager.Playback(ref state);

        assigner.Playback(ref state);
    }
}
