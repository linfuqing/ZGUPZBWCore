using Unity.Jobs;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Entities;
using Unity.Collections;
using UnityEngine;
using ZG;

[BurstCompile, UpdateInGroup(typeof(GameRollbackSystemGroup)), UpdateBefore(typeof(GameNodeSystem))]
public partial struct GameStatusSystemGroup : ISystem
{
    private SystemGroup __systemGroup;

    public void OnCreate(ref SystemState state)
    {
        __systemGroup = SystemGroupUtility.GetOrCreateSystemGroup(state.World, typeof(GameStatusSystemGroup));
    }

    public void OnDestroy(ref SystemState state)
    {

    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var world = state.WorldUnmanaged;
        __systemGroup.Update(ref world);
    }
}

[BurstCompile, UpdateInGroup(typeof(GameStatusSystemGroup), OrderLast = true)]
public partial struct GameNodeStatusSystem : ISystem
{
    private struct UpdateStates
    {
        //public GameDeadline time;

        [ReadOnly]
        public NativeArray<GameNodeStatus> states;
        public NativeArray<GameNodeOldStatus> oldStates;

        public NativeArray<GameNodeDelay> delay;

        public NativeArray<GameNodeVelocity> velocities;

        public NativeArray<GameNodeDirect> directs;

        public NativeArray<GameNodeDirection> directions;

        public BufferAccessor<GameNodePosition> positions;

        public BufferAccessor<GameNodeVelocityComponent> velocityComponents;


#if GAME_DEBUG_COMPARSION
        public uint frameIndex;

        public FixedString32Bytes oldStatusName;
        public FixedString32Bytes newStatusName;

        public ComparisonStream<uint> stream;
        [ReadOnly]
        public NativeArray<GameEntityIndex> entityIndices;
        [ReadOnly]
        public NativeArray<Entity> entityArray;
#endif

        public int Execute(int index)
        {
            int value = states[index].value, oldValue = oldStates[index].value;
            if (value == oldValue)
                return value;

#if GAME_DEBUG_COMPARSION
            //UnityEngine.Debug.Log($"Status: {oldValue} To {value} : {entityIndices[index].value} : {frameIndex} : {entityArray[index]}");

            stream.Begin(entityIndices[index].value);
            stream.Assert(oldStatusName, oldValue);
            stream.Assert(newStatusName, value);
            stream.End();
#endif
            GameNodeOldStatus oldStatus;
            oldStatus.value = value;
            oldStates[index] = oldStatus;

            int diff = value ^ oldValue;

            if ((diff & value & GameNodeStatus.STOP) == GameNodeStatus.STOP)
            {
                if (index < velocities.Length)
                    velocities[index] = default;

                if (index < delay.Length)
                    delay[index] = default;

                if (index < directions.Length)
                {
                    var direction = directions[index];
                    direction.version = 0;
                    directions[index] = direction;
                }

                if (index < velocityComponents.Length)
                    velocityComponents[index].Clear();
            }

            if ((diff & value & GameNodeStatus.OVER) == GameNodeStatus.OVER)
            {
                if (index < directions.Length)
                {
                    var direction = directions[index];
                    direction.version = 0;
                    directions[index] = direction;
                }

                if (index < velocityComponents.Length)
                {
                    //UnityEngine.Debug.Log("Clear Indirects");
                    velocityComponents[index].Clear();
                }

                if (index < positions.Length)
                    positions[index].Clear();

                if (index < directs.Length)
                    directs[index] = default;
            }

            return value;
        }
    }

    [BurstCompile]
    private struct UpdateStatesEx : IJobChunk
    {
        [ReadOnly]
        public ComponentTypeHandle<GameNodeStatus> statusType;
        public ComponentTypeHandle<GameNodeOldStatus> oldStatusType;

        public ComponentTypeHandle<GameNodeDelay> delayType;

        public ComponentTypeHandle<GameNodeVelocity> velocityType;
        public ComponentTypeHandle<GameNodeDirect> directType;

        public ComponentTypeHandle<GameNodeDirection> directionType;

        public BufferTypeHandle<GameNodePosition> positionType;
        public BufferTypeHandle<GameNodeVelocityComponent> velocityComponentType;

#if GAME_DEBUG_COMPARSION
        public uint frameIndex;

        public FixedString32Bytes oldStatusName;
        public FixedString32Bytes newStatusName;

        public ComparisonStream<uint> stream;
        [ReadOnly]
        public ComponentTypeHandle<GameEntityIndex> entityIndexType;

        [ReadOnly] 
        public EntityTypeHandle entityType;
#endif
        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            UpdateStates updateStates;
            updateStates.states = chunk.GetNativeArray(ref statusType);
            updateStates.oldStates = chunk.GetNativeArray(ref oldStatusType);
            updateStates.delay = chunk.GetNativeArray(ref delayType);
            updateStates.velocities = chunk.GetNativeArray(ref velocityType);
            updateStates.directs = chunk.GetNativeArray(ref directType);
            updateStates.directions = chunk.GetNativeArray(ref directionType);
            updateStates.positions = chunk.GetBufferAccessor(ref positionType);
            updateStates.velocityComponents = chunk.GetBufferAccessor(ref velocityComponentType);

#if GAME_DEBUG_COMPARSION
            updateStates.frameIndex = frameIndex;
            updateStates.oldStatusName = oldStatusName;
            updateStates.newStatusName = newStatusName;
            updateStates.stream = stream;
            updateStates.entityIndices = chunk.GetNativeArray(ref entityIndexType);
            updateStates.entityArray = chunk.GetNativeArray(entityType);
#endif
            bool isDisabled;
            int count = chunk.Count;
            //var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            //while (iterator.NextEntityIndex(out int i))
            for(int i = 0; i < count; ++i)
            {
                var value = updateStates.Execute(i);
                isDisabled = (value & GameNodeStatus.OVER) == GameNodeStatus.OVER;
                if(isDisabled == chunk.IsComponentEnabled(ref oldStatusType, i))
                    chunk.SetComponentEnabled(ref oldStatusType, i, !isDisabled);
            }
        }
    }

    private EntityQuery __group;

#if GAME_DEBUG_COMPARSION
    private GameRollbackFrame __frame;
#endif

    private ComponentTypeHandle<GameNodeStatus> __statusType;
    private ComponentTypeHandle<GameNodeOldStatus> __oldStatusType;

    private ComponentTypeHandle<GameNodeDelay> __delayType;

    private ComponentTypeHandle<GameNodeVelocity> __velocityType;
    private ComponentTypeHandle<GameNodeDirect> __directType;

    private ComponentTypeHandle<GameNodeDirection> __directionType;

    private BufferTypeHandle<GameNodePosition> __positionType;
    private BufferTypeHandle<GameNodeVelocityComponent> __velocityComponentType;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __group = builder
                .WithAll<GameNodeStatus>()
                .WithAllRW<GameNodeOldStatus>()
                .WithOptions(EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IgnoreComponentEnabledState)
                .Build(ref state);

        __group.AddChangedVersionFilter(ComponentType.ReadOnly<GameNodeStatus>());
        __group.AddChangedVersionFilter(ComponentType.ReadWrite<GameNodeOldStatus>());

#if GAME_DEBUG_COMPARSION
        __frame = new GameRollbackFrame(ref state);
#endif
        
        __statusType = state.GetComponentTypeHandle<GameNodeStatus>(true);
        __oldStatusType = state.GetComponentTypeHandle<GameNodeOldStatus>();
        __delayType = state.GetComponentTypeHandle<GameNodeDelay>();
        __velocityType = state.GetComponentTypeHandle<GameNodeVelocity>();
        __directType = state.GetComponentTypeHandle<GameNodeDirect>();
        __directionType = state.GetComponentTypeHandle<GameNodeDirection>();
        __positionType = state.GetBufferTypeHandle<GameNodePosition>();
        __velocityComponentType = state.GetBufferTypeHandle<GameNodeVelocityComponent>();
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {

    }

#if !GAME_DEBUG_COMPARSION
    [BurstCompile]
#endif
    public void OnUpdate(ref SystemState state)
    {
        UpdateStatesEx updateStates;
        updateStates.statusType = __statusType.UpdateAsRef(ref state);
        updateStates.oldStatusType = __oldStatusType.UpdateAsRef(ref state);
        updateStates.delayType = __delayType.UpdateAsRef(ref state);
        updateStates.velocityType = __velocityType.UpdateAsRef(ref state);
        updateStates.directType = __directType.UpdateAsRef(ref state);
        updateStates.directionType = __directionType.UpdateAsRef(ref state);
        updateStates.positionType = __positionType.UpdateAsRef(ref state);
        updateStates.velocityComponentType = __velocityComponentType.UpdateAsRef(ref state);

#if GAME_DEBUG_COMPARSION
        uint frameIndex =  __frame.index;
        var streamScheduler = GameComparsionSystem.instance.Create(false, frameIndex, typeof(GameNodeStatusSystem).Name, state.World.Name);

        updateStates.frameIndex = frameIndex;
        updateStates.oldStatusName = "oldStatus";
        updateStates.newStatusName = "newStatus";
        updateStates.stream = streamScheduler.Begin(__group.CalculateEntityCount());
        updateStates.entityIndexType = state.GetComponentTypeHandle<GameEntityIndex>(true);
        updateStates.entityType = state.GetEntityTypeHandle();
#endif
        state.Dependency = updateStates.ScheduleParallelByRef(__group, state.Dependency);

#if GAME_DEBUG_COMPARSION
        streamScheduler.End(state.Dependency);
#endif
    }
}

public static class GameNodeStatusUtility
{
    public static EntityQuery BuildStatusSystemGroup(in this EntityQueryBuilder builder, ref SystemState state)
    {
        var result = builder
            .WithAll<GameNodeStatus, GameNodeOldStatus>()
            .WithOptions(EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IgnoreComponentEnabledState)
            .Build(ref state);

        result.AddChangedVersionFilter(ComponentType.ReadOnly<GameNodeStatus>());
        result.AddChangedVersionFilter(ComponentType.ReadOnly<GameNodeOldStatus>());

        return result;
    }
}