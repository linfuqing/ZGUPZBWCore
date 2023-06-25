using System;
using System.Collections.Generic;
using Unity.Jobs;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Transforms;
using Unity.Physics;
using Unity.Physics.Systems;
using ZG;
using Math = ZG.Mathematics.Math;

public struct GameEntityCommandActionCreate
{
    public GameActionData value;
    public GameActionDataEx valueEx;
}

[BurstCompile, UpdateInGroup(typeof(GameUpdateSystemGroup)),
    UpdateBefore(typeof(GameNodeCharacterSystemGroup))
    /*UpdateBefore(typeof(EndFramePhysicsSystem)), 
    UpdateAfter(typeof(ExportPhysicsWorld))*/]
public partial struct GameEntityActionSystemGroup : ISystem
{
    private SystemGroup __systemGroup;

    public void OnCreate(ref SystemState state)
    {
        __systemGroup = state.World.GetOrCreateSystemGroup(typeof(GameEntityActionSystemGroup));
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

[BurstCompile, 
    UpdateInGroup(typeof(GameRollbackSystemGroup)),
    UpdateBefore(typeof(GameNodeSystem)),
    UpdateBefore(typeof(GameEntityActionBeginEntityCommandSystemGroup)),
    UpdateAfter(typeof(GameNodeInitSystemGroup))]
public partial struct GameEntityActorSystemGroup : ISystem
{
    private SystemGroup __systemGroup;

    public void OnCreate(ref SystemState state)
    {
        __systemGroup = state.World.GetOrCreateSystemGroup(typeof(GameEntityActorSystemGroup));
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

[BurstCompile, UpdateInGroup(typeof(GameRollbackSystemGroup)), UpdateBefore(typeof(GameUpdateSystemGroup)), UpdateAfter(typeof(GameNodeSystem))]
public partial struct GameEntityActionBeginEntityCommandSystemGroup : ISystem
{
    private SystemGroup __systemGroup;

    public void OnCreate(ref SystemState state)
    {
        __systemGroup = state.World.GetOrCreateSystemGroup(typeof(GameEntityActionBeginEntityCommandSystemGroup));
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

//TODO: Not Used
//[UpdateInGroup(typeof(GameUpdateSystemGroup)), /*UpdateBefore(typeof(GameNodeIndirectSystem)), */UpdateBefore(typeof(GameNodeCharacterSystemGroup)), UpdateAfter(typeof(GameEntityActionSystemGroup))]
/*public partial class GameEntityActionEndEntityCommandSystemGroup : ComponentSystemGroup
{
}*/

public partial class GameEntityTimeEventSystem : SystemBase
{
    private struct Container : IEntityCommandContainer
    {
        [BurstCompile]
        private struct Cannel : IJob
        {
            [ReadOnly]
            public EntityCommandContainerReadOnly container;

            public NativeRBTree<TimeEvent<CallbackHandle>> timeEvents;

            public NativeList<CallbackHandle> callbackHandles;

            public void Execute()
            {
                CallbackHandle callbackHandle;
                var enumerator = container.GetEnumerator();
                while (enumerator.MoveNext())
                {
                    callbackHandle = enumerator.As<TimeEventHandle>().Cannel(ref timeEvents);
                    if (!callbackHandle.Equals(CallbackHandle.Null))
                        callbackHandles.Add(callbackHandle);
                }
            }
        }

        public NativeRBTree<TimeEvent<CallbackHandle>> timeEvents;
        public NativeList<CallbackHandle> callbackHandles;

        public JobHandle CopyFrom(in EntityCommandContainerReadOnly values, in JobHandle jobHandle)
        {
            Cannel cannel;
            cannel.container = values;
            cannel.timeEvents = timeEvents;
            cannel.callbackHandles = callbackHandles;
            return cannel.Schedule(jobHandle);
        }

        public void CopyFrom(in EntityCommandContainerReadOnly values)
        {
            Cannel cannel;
            cannel.container = values;
            cannel.timeEvents = timeEvents;
            cannel.callbackHandles = callbackHandles;
            cannel.Execute();
        }
    }

    private TimeEventSystem __timeEventSystem;

    private NativeList<CallbackHandle> __callbackHandles;

    private EntityCommandPool<TimeEventHandle>.Context __commander;

    public EntityCommandPool<TimeEventHandle> pool => __commander.pool;

    protected override void OnCreate()
    {
        base.OnCreate();

        __timeEventSystem = World.GetOrCreateSystemManaged<TimeEventSystem>();

        __callbackHandles = new NativeList<CallbackHandle>(Allocator.Persistent);

        __commander = new EntityCommandPool<TimeEventHandle>.Context(Allocator.Persistent);
    }

    protected override void OnDestroy()
    {
        __callbackHandles.Dispose();

        __commander.Dispose();

        base.OnDestroy();
    }

    protected override void OnUpdate()
    {
        bool result;
        int length = __callbackHandles.Length;
        for (int i = 0; i < length; ++i)
        {
            result = __callbackHandles[i].Unregister();

            UnityEngine.Assertions.Assert.IsTrue(result);
        }

        __callbackHandles.Clear();

        if (!__commander.isEmpty)
        {
            Container container;
            container.timeEvents = __timeEventSystem.events;
            container.callbackHandles = __callbackHandles;
            var jobHandle = __commander.MoveTo(container, JobHandle.CombineDependencies(__timeEventSystem.jobHandle, Dependency));

            __timeEventSystem.AddDependency(jobHandle);

            Dependency = jobHandle;
        }
    }
}

[BurstCompile, UpdateInGroup(typeof(GameEntityActionBeginEntityCommandSystemGroup))]
public partial struct GameEntityActionBeginStructChangeSystem : ISystem
{
    public EntityCommandStructChangeManager manager
    {
        get;

        private set;
    }

    public void OnCreate(ref SystemState state)
    {
        manager = new EntityCommandStructChangeManager(Allocator.Persistent);
    }

    public void OnDestroy(ref SystemState state)
    {
        manager.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        manager.Playback(ref state);
    }
}

/*[BurstCompile, UpdateInGroup(typeof(GameEntityActionEndEntityCommandSystemGroup))]
public partial struct GameEntityActionEndStructChangeSystem : ISystem
{
    private struct Assigner : EntityCommandStructChangeManager.IAssigner
    {
        public EntityQuery group;

        public EntityComponentAssigner assigner;

        public void Playback(ref SystemState systemState)
        {
            assigner.Playback(ref systemState, group);
        }
    }

    private EntityQuery __group;

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

    public void OnCreate(ref SystemState state)
    {
        __group = state.GetEntityQuery(ComponentType.ReadOnly<GameEntityActorData>());

        assigner = new EntityComponentAssigner(Allocator.Persistent);

        manager = new EntityCommandStructChangeManager(Allocator.Persistent);
    }

    public void OnDestroy(ref SystemState state)
    {
        assigner.Dispose();

        manager.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        Assigner assigner;
        assigner.group = __group;
        assigner.assigner = this.assigner;

        manager.Playback(ref state, ref assigner);
    }
}*/

[BurstCompile, UpdateInGroup(typeof(GameEntityActionBeginEntityCommandSystemGroup))]
public partial struct GameEntityActionBeginFactorySystem : ISystem
{
    [BurstCompile]
    private struct SetValues : IJob
    {
        public int numKeys;

        [ReadOnly, DeallocateOnJobCompletion]
        public NativeArray<EntityArchetype> keys;

        [ReadOnly, DeallocateOnJobCompletion]
        public NativeArray<Entity> entityArray;

        public UnsafeParallelMultiHashMap<EntityArchetype, GameEntityCommandActionCreate> commands;

        public ComponentLookup<Translation> translations;

        public ComponentLookup<Rotation> rotations;

        public ComponentLookup<GameActionData> instances;

        public ComponentLookup<GameActionDataEx> instancesEx;

        public BufferLookup<GameEntityAction> actions;

        public void Execute()
        {
            int index = 0;
            EntityArchetype key;
            Entity entity;
            Translation translation;
            Rotation rotation;
            GameEntityCommandActionCreate command;
            NativeParallelMultiHashMapIterator<EntityArchetype> iterator;
            for (int i = 0; i < numKeys; ++i)
            {
                key = keys[i];
                if (commands.TryGetFirstValue(key, out command, out iterator))
                {
                    do
                    {
                        entity = entityArray[index++];

                        translation.Value = command.valueEx.origin.pos;
                        translations[entity] = translation;

                        rotation.Value = command.valueEx.origin.rot;
                        rotations[entity] = rotation;

                        instances[entity] = command.value;
                        instancesEx[entity] = command.valueEx;

                        if (actions.HasBuffer(command.value.entity))
                            actions[command.value.entity].Add(entity);

                    } while (commands.TryGetNextValue(out command, ref iterator));
                }
            }

            //commands.Dispose();
        }
    }

    private EntityCommandPool<GameEntityCommandActionCreate>.Context __commander;

    public EntityCommandPool<GameEntityCommandActionCreate> pool => __commander.pool;

    public void OnCreate(ref SystemState state)
    {
        BurstUtility.InitializeJob<SetValues>();

        __commander = new EntityCommandPool<GameEntityCommandActionCreate>.Context(Allocator.Persistent);
    }

    public void OnDestroy(ref SystemState state)
    {
        __commander.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (__commander.isEmpty)
            return;

        var commands = new UnsafeParallelMultiHashMap<EntityArchetype, GameEntityCommandActionCreate>(1, state.WorldUpdateAllocator);

        while (__commander.TryDequeue(out var command))
            commands.Add(command.valueEx.entityArchetype, command);

        int count = commands.Count();
        if (count > 0)
        {
            var entityArray = new NativeArray<Entity>(count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            var keys = commands.GetKeyArray(Allocator.TempJob);
            {
                //state.CompleteDependency();

                int numKeys = keys.ConvertToUniqueArray(), offset = 0, length;
                EntityArchetype key;
                var entityManager = state.EntityManager;
                for (int i = 0; i < numKeys; ++i)
                {
                    key = keys[i];

                    length = commands.CountValuesForKey(key);
                    entityManager.CreateEntity(key, entityArray.GetSubArray(offset, length));
                    offset += length;
                }

                SetValues setValues;
                setValues.numKeys = numKeys;
                setValues.keys = keys;
                setValues.entityArray = entityArray;
                setValues.commands = commands;
                setValues.translations = state.GetComponentLookup<Translation>();
                setValues.rotations = state.GetComponentLookup<Rotation>();
                setValues.instances = state.GetComponentLookup<GameActionData>();
                setValues.instancesEx = state.GetComponentLookup<GameActionDataEx>();
                setValues.actions = state.GetBufferLookup<GameEntityAction>();
                state.Dependency = setValues.ScheduleByRef(state.Dependency);
                //TODO
                //setValues.Execute();
            }
            //TODO
            //keys.Dispose();
            //entityArray.Dispose();
        }
    }
}

[BurstCompile, UpdateInGroup(typeof(GameEntityActorSystemGroup), OrderLast = true)]
public partial struct GameEntityEventSystem : ISystem
{
    public struct Trigger
    {
        public bool isDisabled;

        [ReadOnly]
        public NativeArray<Entity> entityArray;

        [ReadOnly]
        public NativeArray<GameNodeStatus> states;

        [ReadOnly]
        public NativeArray<GameEntityEventCommand> commands;

        public NativeArray<GameEntityCommandVersion> commandVersions;

        public NativeArray<GameEntityEventInfo> eventInfos;

        public NativeArray<GameEntityActorInfo> actorInfos;

        public NativeArray<GameEntityActorTime> actorTimes;

        public NativeArray<GameNodeDelay> delay;

        public NativeArray<GameNodeVelocity> velocities;

        public BufferAccessor<GameNodePosition> positions;

        [WriteOnly, NativeDisableContainerSafetyRestriction]
        public ComponentLookup<GameNodeStatus> statusMap;

#if GAME_DEBUG_COMPARSION
        public uint frameIndex;
        [ReadOnly]
        public NativeArray<GameEntityIndex> entityIndices;
#endif

        public void Execute(int index)
        {
            GameEntityEventCommand command = commands[index];
            GameEntityCommandVersion commandVersion = commandVersions[index];
            if (command.version != commandVersion.value)
                return;

            ++commandVersion.value;
            commandVersions[index] = commandVersion;

            if (isDisabled)
                return;

#if GAME_DEBUG_COMPARSION
            //UnityEngine.Debug.Log($"Do: {entityArray[index]} : {command.time} : {entityIndices[index]} : {frameIndex}");
#endif

            var status = states[index];
            int value = status.value & GameNodeStatus.MASK;
            if (status.value != value)
            {
                status.value = value;
                statusMap[entityArray[index]] = status;
            }

            if (index < delay.Length)
            {
                GameNodeDelay delay;
                delay.time = command.time;
                delay.startTime = half.zero;
                delay.endTime = (half)command.coolDownTime;
                this.delay[index] = delay;
            }

            if (index < actorTimes.Length)
            {
                GameEntityActorTime actorTime;
                actorTime.value = command.time + command.performTime;
                actorTimes[index] = actorTime;
            }

            if (index < actorInfos.Length)
            {
                GameEntityActorInfo actorInfo = actorInfos[index];
                int version = ++actorInfo.version;

                actorInfo.alertTime = command.time;
                actorInfos[index] = actorInfo;

                if (index < eventInfos.Length)
                {
                    GameEntityEventInfo eventInfo;
                    eventInfo.version = version;
                    eventInfo.timeEventHandle = command.handle;
                    eventInfos[index] = eventInfo;
                }
            }

            if (index < velocities.Length)
                velocities[index] = default;

            if (index < positions.Length)
                positions[index].Clear();
        }
    }

    [BurstCompile]
    public struct TriggerEx : IJobChunk
    {
        [ReadOnly]
        public EntityTypeHandle entityArrayType;

        [ReadOnly]
        public ComponentTypeHandle<Disabled> disabledType;

        [ReadOnly]
        public ComponentTypeHandle<GameNodeStatus> statusType;

        public ComponentTypeHandle<GameEntityEventCommand> commandType;

        public ComponentTypeHandle<GameEntityCommandVersion> commandVersionType;

        public ComponentTypeHandle<GameEntityEventInfo> eventInfoType;

        public ComponentTypeHandle<GameEntityActorInfo> actorInfoType;

        public ComponentTypeHandle<GameEntityActorTime> actorTimeType;

        public ComponentTypeHandle<GameNodeDelay> delayType;

        public ComponentTypeHandle<GameNodeVelocity> velocityType;

        public BufferTypeHandle<GameNodePosition> positionType;

        [WriteOnly, NativeDisableContainerSafetyRestriction]
        public ComponentLookup<GameNodeStatus> states;

#if GAME_DEBUG_COMPARSION
        public uint frameIndex;
        [ReadOnly]
        public ComponentTypeHandle<GameEntityIndex> entityIndexType;
#endif

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Trigger trigger;
            trigger.isDisabled = chunk.Has(ref disabledType);
            trigger.entityArray = chunk.GetNativeArray(entityArrayType);
            trigger.states = chunk.GetNativeArray(ref statusType);
            trigger.commands = chunk.GetNativeArray(ref commandType);
            trigger.commandVersions = chunk.GetNativeArray(ref commandVersionType);
            trigger.eventInfos = chunk.GetNativeArray(ref eventInfoType);
            trigger.actorInfos = chunk.GetNativeArray(ref actorInfoType);
            trigger.actorTimes = chunk.GetNativeArray(ref actorTimeType);
            trigger.delay = chunk.GetNativeArray(ref delayType);
            trigger.velocities = chunk.GetNativeArray(ref velocityType);
            trigger.positions = chunk.GetBufferAccessor(ref positionType);
            trigger.statusMap = states;

#if GAME_DEBUG_COMPARSION
            trigger.frameIndex = frameIndex;
            trigger.entityIndices = chunk.GetNativeArray(ref entityIndexType);
#endif

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
            {
                trigger.Execute(i);

                chunk.SetComponentEnabled(ref commandType, i, false);
            }
        }
    }


#if GAME_DEBUG_COMPARSION
    private GameRollbackFrame __frame;
#endif

    private EntityQuery __group;

    private EntityTypeHandle __entityArrayType;

    private ComponentTypeHandle<Disabled> __disabledType;

    private ComponentTypeHandle<GameNodeStatus> __statusType;

    private ComponentTypeHandle<GameEntityEventCommand> __commandType;

    private ComponentTypeHandle<GameEntityCommandVersion> __commandVersionType;

    private ComponentTypeHandle<GameEntityEventInfo> __eventInfoType;

    private ComponentTypeHandle<GameEntityActorInfo> __actorInfoType;

    private ComponentTypeHandle<GameEntityActorTime> __actorTimeType;

    private ComponentTypeHandle<GameNodeDelay> __delayType;

    private ComponentTypeHandle<GameNodeVelocity> __velocityType;

    private BufferTypeHandle<GameNodePosition> __positionType;

    private ComponentLookup<GameNodeStatus> __states;

    public void OnCreate(ref SystemState state)
    {
#if GAME_DEBUG_COMPARSION
        __frame = new GameRollbackFrame(ref state);
#endif

        __group = state.GetEntityQuery(
            new EntityQueryDesc()
            {
                All = new ComponentType[]
                {
                    ComponentType.ReadWrite<GameEntityEventCommand>(),
                    ComponentType.ReadWrite<GameEntityCommandVersion>(),
                    ComponentType.ReadWrite<GameEntityEventInfo>(),
                    ComponentType.ReadWrite<GameNodeDelay>()
                },
                Options = EntityQueryOptions.IncludeDisabledEntities
            });

        __entityArrayType = state.GetEntityTypeHandle();
        __disabledType = state.GetComponentTypeHandle<Disabled>(true);
        __statusType = state.GetComponentTypeHandle<GameNodeStatus>(true);
        __commandType = state.GetComponentTypeHandle<GameEntityEventCommand>();
        __commandVersionType = state.GetComponentTypeHandle<GameEntityCommandVersion>();
        __eventInfoType = state.GetComponentTypeHandle<GameEntityEventInfo>();
        __actorInfoType = state.GetComponentTypeHandle<GameEntityActorInfo>();
        __actorTimeType = state.GetComponentTypeHandle<GameEntityActorTime>();
        __delayType = state.GetComponentTypeHandle<GameNodeDelay>();
        __velocityType = state.GetComponentTypeHandle<GameNodeVelocity>();
        __positionType = state.GetBufferTypeHandle<GameNodePosition>();
        __states = state.GetComponentLookup<GameNodeStatus>();
        //__group.SetChangedVersionFilter(typeof(GameEntityEventCommand));
    }

    public void OnDestroy(ref SystemState state)
    {

    }

#if !GAME_DEBUG_COMPARSION
    [BurstCompile]
#endif
    public void OnUpdate(ref SystemState state)
    {
        TriggerEx trigger;
        trigger.entityArrayType = __entityArrayType.UpdateAsRef(ref state);
        trigger.disabledType = __disabledType.UpdateAsRef(ref state);
        trigger.statusType = __statusType.UpdateAsRef(ref state);
        trigger.commandType = __commandType.UpdateAsRef(ref state);
        trigger.commandVersionType = __commandVersionType.UpdateAsRef(ref state);
        trigger.eventInfoType = __eventInfoType.UpdateAsRef(ref state);
        trigger.actorInfoType = __actorInfoType.UpdateAsRef(ref state);
        trigger.actorTimeType = __actorTimeType.UpdateAsRef(ref state);
        trigger.delayType = __delayType.UpdateAsRef(ref state);
        trigger.velocityType = __velocityType.UpdateAsRef(ref state);
        trigger.positionType = __positionType.UpdateAsRef(ref state);
        trigger.states = __states.UpdateAsRef(ref state);

#if GAME_DEBUG_COMPARSION
        uint frameIndex = __frame.index;

        trigger.frameIndex = frameIndex;
        trigger.entityIndexType = state.GetComponentTypeHandle<GameEntityIndex>(true);
#endif

        state.Dependency = trigger.ScheduleParallelByRef(__group, state.Dependency);
    }
}

[BurstCompile, UpdateInGroup(typeof(GameEntityActorSystemGroup), OrderFirst = true)]
public partial struct GameEntityActorInitSystem : ISystem
{
    private struct MaskDirty
    {
        [ReadOnly]
        public NativeArray<Entity> entityArray;

        [ReadOnly]
        public NativeArray<GameNodeParent> parents;

        [ReadOnly]
        public NativeArray<GameEntityCommandVersion> versions;

        [ReadOnly]
        public NativeArray<GameEntityActionCommand> commands;

        public NativeArray<GameEntityActionCommander> commanders;

        [NativeDisableContainerSafetyRestriction]
        public ComponentLookup<GameEntityActionCommander> commanderMap;

        public bool Execute(int index)
        {
            if (versions[index].value != commands[index].version)
                return false;

            GameEntityActionCommander commander;

            if (index < parents.Length)
            {
                var parent = parents[index];
                if (parent.authority < 1)
                    return false;

                if (commanderMap.HasComponent(parent.entity))
                {
                    commander.entity = entityArray[index];
                    commanderMap[parent.entity] = commander;
                    commanderMap.SetComponentEnabled(parent.entity, true);

                    return false;
                }
            }

            commander.entity = Entity.Null;
            commanders[index] = commander;

            return true;
        }
    }

    [BurstCompile]
    private struct MaskDirtyEx : IJobChunk
    {
        [ReadOnly]
        public EntityTypeHandle entityType;

        [ReadOnly]
        public ComponentTypeHandle<GameNodeParent> parentType;

        [ReadOnly]
        public ComponentTypeHandle<GameEntityCommandVersion> versionType;

        public ComponentTypeHandle<GameEntityActionCommand> commandType;

        public ComponentTypeHandle<GameEntityActionCommander> commanderType;

        [NativeDisableContainerSafetyRestriction]
        public ComponentLookup<GameEntityActionCommander> commanders;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            if (!chunk.Has(ref commanderType))
                return;

            MaskDirty maskDirty;
            maskDirty.entityArray = chunk.GetNativeArray(entityType);
            maskDirty.parents = chunk.GetNativeArray(ref parentType);
            maskDirty.versions = chunk.GetNativeArray(ref versionType);
            maskDirty.commands = chunk.GetNativeArray(ref commandType);
            maskDirty.commanders = chunk.GetNativeArray(ref commanderType);
            maskDirty.commanderMap = commanders;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
            {
                if (maskDirty.Execute(i))
                    chunk.SetComponentEnabled(ref commanderType, i, true);

                chunk.SetComponentEnabled(ref commandType, i, false);
            }
        }
    }

    private EntityQuery __group;
    private EntityTypeHandle __entityType;
    private ComponentTypeHandle<GameNodeParent> __parentType;
    private ComponentTypeHandle<GameEntityCommandVersion> __versionType;
    private ComponentTypeHandle<GameEntityActionCommand> __commandType;
    private ComponentTypeHandle<GameEntityActionCommander> __commanderType;
    private ComponentLookup<GameEntityActionCommander> __commanders;


    public void OnCreate(ref SystemState state)
    {
        __group = state.GetEntityQuery(
            new EntityQueryDesc()
            {
                All = new ComponentType[]
                {
                    ComponentType.ReadOnly<GameEntityCommandVersion>(),
                    ComponentType.ReadWrite<GameEntityActionCommand>(),
                },
                Options = EntityQueryOptions.IncludeDisabledEntities
            });

        __group.SetChangedVersionFilter(typeof(GameEntityActionCommand));

        __entityType = state.GetEntityTypeHandle();
        __parentType = state.GetComponentTypeHandle<GameNodeParent>(true);
        __versionType = state.GetComponentTypeHandle<GameEntityCommandVersion>(true);
        __commandType = state.GetComponentTypeHandle<GameEntityActionCommand>();
        __commanderType = state.GetComponentTypeHandle<GameEntityActionCommander>();
        __commanders = state.GetComponentLookup<GameEntityActionCommander>();
    }

    public void OnDestroy(ref SystemState state)
    {

    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        MaskDirtyEx maskDirty;
        maskDirty.entityType = __entityType.UpdateAsRef(ref state);
        maskDirty.parentType = __parentType.UpdateAsRef(ref state);
        maskDirty.versionType = __versionType.UpdateAsRef(ref state);
        maskDirty.commandType = __commandType.UpdateAsRef(ref state);
        maskDirty.commanderType = __commanderType.UpdateAsRef(ref state);
        maskDirty.commanders = __commanders.UpdateAsRef(ref state);

        state.Dependency = maskDirty.ScheduleParallelByRef(__group, state.Dependency);
    }
}

[BurstCompile, UpdateInGroup(typeof(GameEntityActorSystemGroup))]
public partial struct GameEntityActorSystem : ISystem
{
    private struct Act
    {
        public static readonly float3 forward = math.normalize(new float3(0.0f, 1.0f, 1.0f));

        public bool isDisabled;

        public float3 gravity;

        [ReadOnly]
        public BlobAssetReference<GameActionSetDefinition> actions;

        [ReadOnly]
        public BlobAssetReference<GameActionItemSetDefinition> items;

        [ReadOnly]
        public SingletonAssetContainer<BlobAssetReference<Collider>>.Reader actionColliders;

        [ReadOnly]
        public ComponentLookup<Disabled> disabled;

        [ReadOnly]
        public ComponentLookup<Translation> translationMap;

        [ReadOnly, NativeDisableContainerSafetyRestriction]
        public ComponentLookup<Rotation> rotationMap;

        [ReadOnly]
        public ComponentLookup<PhysicsMass> physicsMasses;

        [ReadOnly]
        public ComponentLookup<PhysicsCollider> physicsColliders;

        [ReadOnly, NativeDisableContainerSafetyRestriction]
        public ComponentLookup<GameNodeVelocity> velocityMap;

        [ReadOnly]
        public ComponentLookup<GameEntityActionCommand> commandMap;

        [ReadOnly]
        public NativeArray<Entity> entityArray;

        [ReadOnly]
        public NativeArray<Translation> translations;

        [ReadOnly]
        public NativeArray<GameNodeStatus> states;

        [ReadOnly]
        public NativeArray<GameNodeSurface> surfaces;

        [ReadOnly]
        public NativeArray<GameNodeCharacterData> characters;

        [ReadOnly]
        public NativeArray<GameEntityCamp> camps;

        [ReadOnly]
        public NativeArray<GameEntityActorData> actors;

        [ReadOnly]
        public NativeArray<GameEntityArchetype> archetypes;

        [ReadOnly]
        public NativeArray<GameEntityActionCommand> commands;

        [ReadOnly]
        public NativeArray<GameEntityActionCommander> commanders;

        [ReadOnly]
        public BufferAccessor<GameEntityAction> entityActions;

        [ReadOnly]
        public BufferAccessor<GameEntityItem> entityItems;

        [ReadOnly]
        public BufferAccessor<GameEntityActorActionData> actorActions;

        public BufferAccessor<GameEntityActorActionInfo> actorActionInfos;

        public BufferAccessor<GameNodeVelocityComponent> velocityComponents;

        public BufferAccessor<GameNodePosition> positions;

        public NativeArray<Rotation> rotations;

        public NativeArray<GameNodeDelay> delay;

        public NativeArray<GameNodeVelocity> velocities;

        public NativeArray<GameNodeAngle> angles;

        public NativeArray<GameNodeCharacterAngle> characterAngles;

        public NativeArray<GameNodeCharacterVelocity> characterVelocities;

        public NativeArray<PhysicsVelocity> physicsVelocities;

        public NativeArray<GameNodeActorStatus> actorStates;

        public NativeArray<GameEntityActorTime> actorTimes;

        public NativeArray<GameEntityActorInfo> actorInfos;

        public NativeArray<GameEntityCommandVersion> commandVersions;

        [NativeDisableContainerSafetyRestriction]
        public ComponentLookup<GameEntityCommandVersion> commandVersionMap;

        [WriteOnly, NativeDisableParallelForRestriction]
        public ComponentLookup<GameEntityActionInfo> actionInfos;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<GameActionStatus> actionStates;

        [WriteOnly, NativeDisableContainerSafetyRestriction]
        public ComponentLookup<GameNodeStatus> nodeStatusMap;

        public EntityCommandQueue<GameEntityCommandActionCreate>.ParallelWriter entityManager;

#if GAME_DEBUG_COMPARSION
        public uint frameIndex;
        public FixedString32Bytes angleName;
        public FixedString32Bytes delayTimeName;
        public FixedString32Bytes commandTimeName;
        public FixedString32Bytes entityName;
        public FixedString32Bytes forwardName;
        public FixedString32Bytes offsetName;
        public FixedString32Bytes rotationName;
        public FixedString32Bytes positionName;
        public FixedString32Bytes distanceName;

        public ComparisonStream<uint> stream;
        [ReadOnly]
        public NativeArray<GameEntityIndex> entityIndices;
#endif

        /*[BurstDiscard]
        public void Create(GameActionData result, Action action)
        {
            Entity resultEntity = entityManager.CreateEntity(jobIndex, result.entityArchetype);

            entityManager.SetComponent(jobIndex, resultEntity, result);

            PhysicsCollider physicsCollider;
            physicsCollider.Value = action.collider;
            entityManager.SetComponent(jobIndex, resultEntity, physicsCollider);

            entityManager.SetComponent(
                jobIndex,
                resultEntity,
                action.mass > math.FLT_MIN_NORMAL ? PhysicsMass.CreateDynamic(physicsCollider.MassProperties, action.mass) : PhysicsMass.CreateKinematic(physicsCollider.MassProperties));

            Translation translation;
            switch (action.instance.rangeType)
            {
                case GameActionRangeType.Destination:
                    translation.Value = result.destinationPosition;
                    break;
                case GameActionRangeType.All:
                    translation.Value = (result.sourcePosition + result.destinationPosition) * 0.5f;
                    break;
                default:
                    translation.Value = result.sourcePosition;
                    break;
            }

            entityManager.SetComponent(jobIndex, resultEntity, translation);
            entityManager.SetComponent(jobIndex, resultEntity, new Rotation() { Value = quaternion.LookRotationSafe(result.direction, math.up()) });
        }*/

        public void Execute(int index)
        {
            GameEntityActionCommand command;
            Entity commander = index < commanders.Length ? commanders[index].entity : Entity.Null;
            if (commandMap.HasComponent(commander))
            {
                command = commandMap[commander];

                var commandVersion = commandVersionMap[commander];
                if (commandVersion.value != command.version)
                    return;

                ++commandVersion.value;
                commandVersionMap[commander] = commandVersion;
            }
            else
            {
                command = commands[index];
                var commandVersion = commandVersions[index];
                if (commandVersion.value != command.version)
                    return;

                ++commandVersion.value;
                commandVersions[index] = commandVersion;
            }

            if (isDisabled)
            {
#if GAME_DEBUG_COMPARSION
                if(entityIndices.Length > 0)
                    UnityEngine.Debug.Log($"Do Fail {entityArray[index]} : {entityIndices[index].value} Disabled!");
#endif

                return;
            }

            var nodeStatus = states[index];
            if ((nodeStatus.value & (GameNodeStatus.STOP | GameNodeStatus.OVER)) != 0)
            {
#if GAME_DEBUG_COMPARSION
                UnityEngine.Debug.Log($"Do Fail {entityArray[index].Index} : {entityIndices[index].value} : {nodeStatus.value} Is Stopped!");
#endif

                return;
            }

            var actorTime = actorTimes[index];
#if GAME_DEBUG_COMPARSION
            stream.Begin(entityIndices[index].value);
            stream.Assert(delayTimeName, actorTime.value > command.time ? (float)(actorTime.value - command.time) : 0.0f);
            stream.Assert(commandTimeName, (double)command.time);
#endif

            //UnityEngine.Debug.Log($"Do: {frameIndex} : {entityIndices[index].value} : {entityArray[index].Index} : {(double)actorTime.value} : {(double)command.time} : {(double)this.actorActionInfos[index][command.index].coolDownTime} : {translations[index].Value}");
            if (actorTime.value < command.time)
            {
                //double x = (double)actorTime.value, y = (double)command.time;
                //UnityEngine.Debug.Log($"Do: {entityArray[index].Index} : {command.index} : {x} : {y}");

                var actorActionInfos = this.actorActionInfos[index];
                if (command.index >= 0 && command.index < actorActionInfos.Length)
                {
                    var actorActionInfo = actorActionInfos[command.index];
                    if (actorActionInfo.coolDownTime < command.time)
                    {
                        if (index < entityActions.Length)
                            GameEntityAction.Break(entityActions[index], ref actionStates);

                        int actionIndex = actorActions[index][command.index].actionIndex;
                        ref var actions = ref this.actions.Value;
                        var action = actions.values[actionIndex];
                        if (index < this.entityItems.Length)
                        {
                            DynamicBuffer<GameEntityItem> entityItems = this.entityItems[index];
                            ref var items = ref this.items.Value;
                            int numItems = entityItems.Length, length = items.values.Length;
                            GameEntityItem entityItem;
                            for (int i = 0; i < numItems; ++i)
                            {
                                entityItem = entityItems[i];

                                if (entityItem.index >= 0 && entityItem.index < length)
                                    action.info += items.values[entityItem.index];
                            }
                        }

                        Entity entity = entityArray[index];
                        var actor = actors[index];
                        if (actor.rangeScale > math.FLT_MIN_NORMAL)
                        {
                            action.info.scale = (action.info.scale > math.FLT_MIN_NORMAL ? action.info.scale : 1.0f) * actor.rangeScale;

                            action.info.radius *= actor.rangeScale;
                        }

                        if (actor.distanceScale > math.FLT_MIN_NORMAL)
                            action.info.distance *= actor.distanceScale;

                        action.instance.offset = math.select(action.instance.offset, action.instance.offset * actor.offsetScale, actor.offsetScale > math.FLT_MIN_NORMAL);

                        if (command.entity != Entity.Null && (command.entity == entity || disabled.HasComponent(command.entity)))
                            command.entity = Entity.Null;

#if GAME_DEBUG_COMPARSION
                    stream.Assert(entityName, command.entity == Entity.Null);
#endif

                        quaternion surfaceRotation = index < surfaces.Length ? surfaces[index].rotation : quaternion.identity;

                        bool isTowardTarget = (action.instance.flag & GameActionFlag.ActorTowardTarget) == GameActionFlag.ActorTowardTarget;
                        float3 up = math.mul(surfaceRotation, math.up()), source = translations[index].Value,
                            offset,
                            forward,
                            distance,
                            position;
                        quaternion rotation;
                        var actionCollider = action.colliderIndex == -1 ? BlobAssetReference<Collider>.Null : actionColliders[new SingletonAssetContainerHandle(actions.instanceID, action.colliderIndex)];
                        if (math.lengthsq(command.distance) > math.FLT_MIN_NORMAL)
                        {
                            forward = command.forward;
                            rotation = quaternion.LookRotationSafe(forward, up);

                            offset = math.mul(rotation, action.instance.offset); //command.offset;//math.mul(rotation, action.instance.offset);
                            position = source + offset;

                            distance = command.distance;// - offset;
                        }
                        else
                        {
                            if ((isTowardTarget || (action.instance.flag & GameActionFlag.ActorTowardForce) == GameActionFlag.ActorTowardForce) &&
                                math.lengthsq(command.forward) > math.FLT_MIN_NORMAL)
                            {
                                forward = command.forward;

                                //这里会不同步
                                //rotation = quaternion.LookRotationSafe(forward, up);
                            }
                            else
                            {
                                if (index < angles.Length)
                                {
                                    rotation = quaternion.RotateY(angles[index].value);

                                    rotation = math.mul(surfaceRotation, rotation);
                                }
                                else
                                    rotation = rotations[index].Value;

                                forward = math.forward(rotation);
                            }

                            if (action.instance.trackType == GameActionRangeType.Source)
                            {
                                forward -= Math.ProjectSafe(forward, gravity);

                                forward = math.normalizesafe(forward);
                            }

                            //为了同步,故意提取出来
                            //rotation = quaternion.LookRotationSafe(forward, up);

                            /*offset = math.mul(rotation, action.instance.offset);
                            position = source + offset;*/

                            if (command.entity != Entity.Null && translationMap.HasComponent(command.entity))
                            {
                                float3 destination = translationMap[command.entity].Value;
                                RigidTransform transform = math.RigidTransform(rotationMap[command.entity].Value, destination);
                                if (physicsMasses.HasComponent(command.entity))
                                    destination = math.transform(transform, physicsMasses[command.entity].CenterOfMass);

                                if (actionCollider.IsCreated)
                                {
                                    var collider = physicsColliders.HasComponent(command.entity) ? physicsColliders[command.entity].Value : BlobAssetReference<Collider>.Null;
                                    if (collider.IsCreated)
                                    {
                                        PointDistanceInput pointDistanceInput = default;
                                        pointDistanceInput.MaxDistance = math.distance(source, destination);
                                        pointDistanceInput.Position = math.transform(math.inverse(transform), source);
                                        pointDistanceInput.Filter = actionCollider.Value.Filter;
                                        pointDistanceInput.Filter.CollidesWith = action.instance.damageMask;
                                        if (collider.Value.CalculateDistance(pointDistanceInput, out DistanceHit closestHit))
                                        {
                                            destination = math.transform(transform, closestHit.Position);

                                            /*distance = destination - source;

                                            length = closestHit.Distance;*/

                                            //UnityEngine.Debug.Log($"Distance : {distance} : {length}");
                                        }
                                    }
                                }

                                if (isTowardTarget)
                                {
                                    forward = math.normalizesafe(transform.pos - source, forward);

                                    if (action.instance.trackType == GameActionRangeType.Source)
                                    {
                                        forward -= Math.ProjectSafe(forward, gravity);

                                        forward = math.normalizesafe(forward);
                                    }

                                    //rotation = quaternion.LookRotationSafe(forward, up);
                                }

                                rotation = quaternion.LookRotationSafe(forward, up);

                                offset = math.mul(rotation, action.instance.offset);
                                position = source + offset;

                                float3 targetPosition;
                                bool isTrack = command.entity != Entity.Null &&
                                        velocityMap.HasComponent(command.entity);
                                if (isTrack)
                                {
                                    float targetVelocity = velocityMap[command.entity].value;
                                    float3 targetDirection = math.forward(transform.rot/*rotationMap[command.entity].Value*/);

                                    targetPosition = destination;// + targetDirection * (targetVelocity * action.info.damageTime);

                                    /*offset = math.mul(rotation, action.instance.offset);
                                    position = source + offset;*/

                                    if ((action.instance.flag & GameActionFlag.UseGravity) == GameActionFlag.UseGravity)
                                    {
                                        if (action.info.actionMoveSpeed > math.FLT_MIN_NORMAL &&
                                               actor.accuracy > math.FLT_MIN_NORMAL &&
                                               Math.CalculateParabolaTrajectory(
                                                   (action.instance.flag & GameActionFlag.MoveWithActor) != GameActionFlag.MoveWithActor,
                                                   actor.accuracy,
                                                   math.length(gravity),
                                                   action.info.actionMoveSpeed,
                                                   targetVelocity,
                                                   targetDirection,
                                                   targetPosition,
                                                   position,
                                                   out var targetDistance))
                                            targetPosition = position + targetDistance;
                                        else
                                        {
                                            float3 targetForward = forward;

                                            float2 angleAndTime = Math.CalculateParabolaAngleAndTime(
                                                (action.instance.flag & GameActionFlag.MoveWithActor) != GameActionFlag.MoveWithActor,
                                                action.info.actionMoveSpeed,
                                                math.length(gravity),
                                                targetPosition - position,
                                                ref targetForward);

                                            if (angleAndTime.y > math.FLT_MIN_NORMAL)
                                                targetPosition = position + targetForward * (action.info.actionMoveSpeed * angleAndTime.y);
                                            else if (action.info.distance > math.FLT_MIN_NORMAL)
                                            {
                                                //LookRotationSafe防止direction==up
                                                targetForward = math.mul(quaternion.LookRotationSafe(targetForward, up), Act.forward);

                                                float offsetDistanceSq = math.lengthsq(Math.ProjectSafe(offset, targetForward)),
                                                forwardLength = math.sqrt(action.info.distance * action.info.distance - offsetDistanceSq) + math.sqrt(offsetDistanceSq);

                                                targetPosition = source + forwardLength * targetForward;
                                            }

                                            isTrack = false;
                                        }
                                    }
                                    else
                                    {
                                        if (action.info.actionMoveSpeed > math.FLT_MIN_NORMAL &&
                                            Math.CalculateLinearTrajectory(
                                                action.info.actionMoveSpeed,
                                                position,
                                                targetPosition,
                                                targetDirection,
                                                targetVelocity,
                                                out var hitPoint))
                                            targetPosition = hitPoint;
                                        else
                                            isTrack = false;
                                    }
                                }
                                else
                                    targetPosition = destination;

                                /*if (isTowardTarget)
                                {
                                    forward = math.normalizesafe(targetPosition - source, forward);

                                    if (action.instance.trackType == GameActionRangeType.Source)
                                    {
                                        forward -= Math.ProjectSafe(forward, gravity);

                                        forward = math.normalizesafe(forward);
                                    }

                                    rotation = quaternion.LookRotationSafe(forward, up);
                                }*/

                                //这样会打不准
                                /*offset = math.mul(rotation, action.instance.offset);
                                position = source + offset;*/

                                if (action.instance.direction.Equals(float3.zero))
                                {
                                    if (action.instance.trackType == GameActionRangeType.Source)
                                        distance = forward * action.info.distance;
                                    else
                                    {
                                        distance = targetPosition - position;

                                        float length = math.length(distance);
                                        if (action.info.distance > math.FLT_MIN_NORMAL && action.info.distance < length)
                                            distance = action.info.distance / length * distance;
                                    }
                                }
                                else
                                    distance = math.mul(rotation, action.instance.direction) * action.info.distance;
                            }
                            else
                            {
                                rotation = quaternion.LookRotationSafe(forward, up);

                                offset = math.mul(rotation, action.instance.offset);
                                position = source + offset;

                                if (action.instance.direction.Equals(float3.zero))
                                {
                                    distance = forward * action.info.distance;
                                    if (action.instance.trackType != GameActionRangeType.Source)
                                    {
                                        /*float distanceSq = action.info.distance * action.info.distance,
                                            offsetDistanceSq = math.lengthsq(Math.ProjectSafe(offset, forward)),
                                            forwardLength = math.sqrt(distanceSq - offsetDistanceSq) + math.sqrt(offsetDistanceSq);
                                        distance = forwardLength * forward - offset;*/

                                        //distance -= offset;

                                        if ((action.instance.flag & GameActionFlag.UseGravity) == GameActionFlag.UseGravity)
                                        {
                                            float3 targetForward = math.normalizesafe(distance, forward);
                                            float2 angleAndTime = Math.CalculateParabolaAngleAndTime(
                                                (action.instance.flag & GameActionFlag.MoveWithActor) != GameActionFlag.MoveWithActor,
                                                action.info.actionMoveSpeed,
                                                math.length(gravity),
                                                distance,
                                                ref targetForward);

                                            if (angleAndTime.y > math.FLT_MIN_NORMAL)
                                                distance = targetForward * (action.info.actionMoveSpeed * angleAndTime.y);
                                            else
                                            {
                                                //LookRotationSafe防止direction==up
                                                targetForward = math.mul(quaternion.LookRotationSafe(forward, up), Act.forward);

                                                distance = action.info.distance/*forwardLength*/ * targetForward;// - offset;
                                            }
                                        }
                                    }
                                }
                                else
                                    distance = math.mul(rotation, action.instance.direction) * action.info.distance;
                            }

                            if (math.lengthsq(distance) <= math.FLT_MIN_NORMAL)
                            {
                                //UnityEngine.Debug.LogWarning($"Reset Distance!");

                                distance = forward;
                            }
                        }

#if GAME_DEBUG_COMPARSION
                    //UnityEngine.Debug.Log($"Do: {frameIndex} : {entityIndices[index].value} : {entityArray[index].Index} : {command.index} : {position} : {translations[index].Value} : {action.instance.offset}");

                    stream.Assert(forwardName, forward);
                    stream.Assert(offsetName, offset);
                    stream.Assert(rotationName, rotation);
                    stream.Assert(positionName, position);
                    stream.Assert(distanceName, distance);
#endif

                        quaternion inverseSurfaceRotation = math.inverse(surfaceRotation);
                        float3 surfaceForward = math.mul(inverseSurfaceRotation, forward);
                        float surfaceForwardLength = math.lengthsq(surfaceForward.xz), surfaceAngle;
                        if (surfaceForwardLength > math.FLT_MIN_NORMAL)
                        {
                            surfaceAngle = math.atan2(surfaceForward.x, action.info.distance < 0.0f ? -surfaceForward.z : surfaceForward.z);

#if GAME_DEBUG_COMPARSION
                        stream.Assert(angleName, surfaceAngle);
#endif

                            if (index < angles.Length)
                            {
                                GameNodeAngle angle;
                                angle.value = (half)surfaceAngle;

                                angles[index] = angle;
                            }

                            if (index < characterAngles.Length)
                            {
                                GameNodeCharacterAngle angle;
                                angle.value = (half)surfaceAngle;

                                characterAngles[index] = angle;
                            }

                            if (index < rotations.Length)
                            {
                                Rotation result;
                                //result.Value = quaternion.RotateY(surfaceAngle);
                                result.Value = index < characters.Length && (characters[index].flag & GameNodeCharacterData.Flag.SurfaceUp) == GameNodeCharacterData.Flag.SurfaceUp ?
                                    math.mul(surfaceRotation, quaternion.RotateY(surfaceAngle)) :
                                    quaternion.RotateY(surfaceAngle);
                                rotations[index] = result;
                            }
                        }
                        else
                            return;

                        float3 direction = math.normalizesafe(distance, forward);
                        if (math.any(math.abs(action.info.angleLimit) > math.FLT_MIN_NORMAL))
                        {
                            float3 surfaceDirection = math.mul(inverseSurfaceRotation, direction);
                            float surfaceDirectionLength = math.lengthsq(surfaceDirection.xz);
                            if (surfaceDirectionLength > math.FLT_MIN_NORMAL)
                            {
                                if (math.any(math.abs(action.info.angleLimit.xy) > math.FLT_MIN_NORMAL))
                                {
                                    /*quaternion horizontalRotation = Math.FromToRotation(surfaceForward.xz * math.rsqrt(surfaceForwardLength), surfaceDirection.xz * math.rsqrt(surfaceDirectionLength));
                                    horizontalRotation = Math.RotateTowards(quaternion.identity, horizontalRotation, action.info.angleLimit.x);
                                    surfaceDirection = math.mul(horizontalRotation, surfaceDirection);*/
                                    float horizontalAngle = math.atan2(surfaceDirection.x, surfaceDirection.z);
                                    quaternion horizontalRotation = quaternion.RotateY(math.clamp(horizontalAngle - surfaceAngle, action.info.angleLimit.x, action.info.angleLimit.y) + surfaceAngle);
                                    surfaceDirection = math.forward(horizontalRotation);
                                }

                                if (math.any(math.abs(action.info.angleLimit.zw) > math.FLT_MIN_NORMAL))
                                {
                                    surfaceDirectionLength = math.sqrt(surfaceDirectionLength);
                                    float verticalAngle = math.atan2(surfaceDirection.y, surfaceDirectionLength);
                                    verticalAngle = math.clamp(verticalAngle, action.info.angleLimit.z, action.info.angleLimit.w);
                                    surfaceDirection.y = math.tan(verticalAngle) * surfaceDirectionLength;
                                    surfaceDirection = math.normalize(surfaceDirection);
                                }
                            }
                            else if (math.any(math.abs(action.info.angleLimit.zw) > math.FLT_MIN_NORMAL))
                            {
                                float verticalAngle = math.clamp(math.PI * 0.5f * math.sign(surfaceDirection.y), action.info.angleLimit.z, action.info.angleLimit.w);
                                surfaceDirection.y = math.tan(verticalAngle) * math.sqrt(surfaceForwardLength);
                                surfaceDirection.xz = surfaceForward.xz;
                                surfaceDirection = math.normalize(surfaceDirection);
                            }

                            direction = math.mul(surfaceRotation, surfaceDirection);
                        }

                        //因为Dreamer会导致卡死
                        int nodeStatusValue = 0;// nodeStatus.value & (GameNodeStatus.DELAY | GameNodeStatus.OVER);

                        GameNodeVelocityComponent velocityComponent;
                        //velocityComponent.value = float3.zero;
                        if (index < velocityComponents.Length)
                        {
                            var velocityComponents = this.velocityComponents[index];
                            velocityComponents.Clear();

                            float3 moveDirection = (action.instance.flag & GameActionFlag.ActorInAir) == GameActionFlag.ActorInAir ?
                                forward : Math.ProjectOnPlane(forward, up);//math.normalizesafe(math.float3(forward.x, 0.0f, forward.z));
                            if (math.abs(action.info.actorMoveSpeed) > math.FLT_MIN_NORMAL)
                            {
                                velocityComponent.mode = GameNodeVelocityComponent.Mode.Direct;

                                velocityComponent.value = moveDirection * action.info.actorMoveSpeed;

                                /*if ((action.instance.flag & GameActionFlag.MoveOnSurface) == GameActionFlag.MoveOnSurface)
                                    velocityComponent.value = math.mul(surfaceRotation, velocityComponent.value);*/

                                velocityComponent.time = command.time;
                                velocityComponent.time += action.info.actorMoveStartTime;
                                --velocityComponent.time.count;

                                velocityComponent.duration = action.info.actorMoveDuration;
                                velocityComponents.Add(velocityComponent);
                            }

                            if (math.abs(action.info.actorJumpSpeed) > math.FLT_MIN_NORMAL)
                            {
                                velocityComponent.mode = GameNodeVelocityComponent.Mode.Indirect;

                                float3 velocity = float3.zero;
                                if (action.info.actorMomentum > math.FLT_MIN_NORMAL)
                                {
                                    var characterVelocity = characterVelocities[index];
                                    characterVelocity.value -= Math.ProjectSafe(characterVelocity.value, gravity);
                                    velocity += Math.Project(characterVelocity.value * action.info.actorMomentum, forward);

                                    characterVelocity.value = float3.zero;
                                    characterVelocities[index] = characterVelocity;

                                    physicsVelocities[index] = default;
                                }

                                if (action.info.actorJumpSpeed > math.FLT_MIN_NORMAL)
                                {
                                    nodeStatusValue |= GameNodeActorStatus.NODE_STATUS_ACT;

                                    if (index < actorStates.Length)
                                    {
                                        GameNodeActorStatus status;
                                        status.value = GameNodeActorStatus.Status.Jump;
                                        status.time = command.time;
                                        actorStates[index] = status;
                                    }
                                }

                                velocity += math.normalizesafe(gravity) * -action.info.actorJumpSpeed;
                                if ((action.instance.flag & GameActionFlag.ActorOnSurface) == GameActionFlag.ActorOnSurface)
                                    velocity = math.mul(surfaceRotation, velocity);

                                //UnityEngine.Debug.Log($"{velocity}");

                                velocityComponent.value = velocity;
                                velocityComponent.time = command.time;
                                velocityComponent.time += action.info.actorJumpStartTime;
                                --velocityComponent.time.count;

                                velocityComponent.duration = 0.0f;
                                velocityComponents.Add(velocityComponent);
                            }

                            if (math.abs(action.info.actorMoveSpeedIndirect) > math.FLT_MIN_NORMAL)
                            {
                                velocityComponent.mode = GameNodeVelocityComponent.Mode.Indirect;

                                float3 velocity = moveDirection * action.info.actorMoveSpeedIndirect;
                                if ((action.instance.flag & GameActionFlag.ActorOnSurface) == GameActionFlag.ActorOnSurface)
                                    velocity = math.mul(surfaceRotation, velocity);

                                //UnityEngine.Debug.Log($"{velocity}");

                                velocityComponent.value = velocity;

                                velocityComponent.time = command.time;
                                velocityComponent.time += action.info.actorMoveStartTimeIndirect;
                                --velocityComponent.time.count;

                                velocityComponent.duration = action.info.actorMoveDurationIndirect;
                                velocityComponents.Add(velocityComponent);
                            }

                            if ((action.instance.flag & GameActionFlag.MoveWithActor) == GameActionFlag.MoveWithActor)
                            {
                                velocityComponent.mode = GameNodeVelocityComponent.Mode.Indirect;

                                velocityComponent.value = direction * action.info.actionMoveSpeed;

                                velocityComponent.time = command.time;
                                velocityComponent.time += action.info.damageTime;

                                velocityComponent.duration = 0.0f;// action.info.actionMoveTime;
                                velocityComponents.Add(velocityComponent);
                            }
                        }

                        if (index < velocities.Length)
                        {
                            GameNodeVelocity velocity;
                            velocity.value = action.info.actorMoveStartTime + action.info.actorMoveDuration < action.info.artTime ?
                                0.0f : action.info.actorMoveSpeed;

                            if (action.info.actorMomentum > math.FLT_MIN_NORMAL)
                                velocity.value = math.max(velocity.value, velocities[index].value);

                            velocities[index] = velocity;
                        }

                        if (index < positions.Length)
                            positions[index].Clear();

                        if (nodeStatusValue != nodeStatus.value)
                        {
                            nodeStatus.value = nodeStatusValue;
                            nodeStatusMap[entity] = nodeStatus;
                        }

                        var artTime = command.time;
                        artTime += action.info.artTime;
                        if (index < this.delay.Length)
                        {
                            //var delay = this.delay[index];
                            GameNodeDelay delay;
                            delay.time = command.time;
                            if (action.info.delayDuration > math.FLT_MIN_NORMAL)
                            {
                                delay.startTime = (half)action.info.delayStartTime;
                                delay.endTime = (half)action.info.delayDuration;
                            }
                            else
                            {
                                delay.startTime = half.zero;
                                delay.endTime = (half)action.info.artTime;
                            }

                            this.delay[index] = delay;
                        }

                        actorTime.value = command.time + action.info.performTime;
                        actorTimes[index] = actorTime;

                        var actorInfo = actorInfos[index];
                        //distance += offset;
                        int version = ++actorInfo.version;

                        actorInfo.alertTime = command.time;

                        actorInfos[index] = actorInfo;

                        GameEntityActionInfo actionInfo;
                        actionInfo.commandVersion = command.version;
                        actionInfo.version = version;
                        actionInfo.index = command.index;
                        actionInfo.hit = action.info.hitSource;
                        actionInfo.time = artTime;
                        actionInfo.forward = forward;
                        actionInfo.distance = distance;// command.distance;// distance + offset;
                                                       //actionInfo.offset = offset;
                        actionInfo.entity = command.entity;

                        actionInfos[entity] = actionInfo;

                        actorActionInfo.coolDownTime = command.time + action.info.coolDownTime;
                        actorActionInfos[command.index] = actorActionInfo;

                        //UnityEngine.Debug.Log($"{position} : {distance}");
                        //if (action.collider.IsCreated)
                        {
                            //UnityEngine.Debug.LogError($"Actor {entity.Index} : {command.version}");

                            GameEntityCommandActionCreate result;

                            result.value.version = version;
                            result.value.index = command.index;
                            result.value.actionIndex = actionIndex;
                            result.value.time = command.time;
                            result.value.entity = entity;
                            result.valueEx.camp = camps[index].value;
                            result.valueEx.direction = direction;
                            result.valueEx.offset = offset;
                            result.valueEx.position = position;
                            result.valueEx.targetPosition = position + distance;
                            result.valueEx.target = command.entity;
                            result.valueEx.info = action.info;
                            result.valueEx.value = action.instance;
                            result.valueEx.entityArchetype = archetypes[index].value;
                            result.valueEx.collider = actionCollider;
                            result.valueEx.origin.rot = quaternion.LookRotationSafe(
                                (action.instance.flag & GameActionFlag.MoveInAir) == GameActionFlag.MoveInAir ? Math.ProjectOnPlaneSafe(direction, up) : direction,
                                up);

                            switch (action.instance.rangeType)
                            {
                                case GameActionRangeType.Destination:
                                    result.valueEx.origin.pos = result.valueEx.targetPosition;
                                    break;
                                case GameActionRangeType.All:
                                    result.valueEx.origin.pos = (result.valueEx.position + result.valueEx.targetPosition) * 0.5f;
                                    break;
                                default:
                                    result.valueEx.origin.pos = result.valueEx.position;
                                    break;
                            }

                            entityManager.Enqueue(result);
                            //Create(result, action);
                        }
                    }
#if GAME_DEBUG_COMPARSION
                else
                    UnityEngine.Debug.Log($"Do Fail {frameIndex} : {entityIndices[index].value} : {entityArray[index].Index} : {actorActionInfo.coolDownTime}");
#endif
                }
            }
#if GAME_DEBUG_COMPARSION
            else
                UnityEngine.Debug.Log($"Do Fail {entityArray[index].Index} : {entityIndices[index].value} : {(double)actorTime.value} : {(double)command.time}");

            stream.End();
#endif
        }
    }

    [BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Deterministic)]
    private struct ActEx : IJobChunk, IEntityCommandProducerJob
    {
        public float3 gravity;

        [ReadOnly]
        public BlobAssetReference<GameActionSetDefinition> actions;

        [ReadOnly]
        public BlobAssetReference<GameActionItemSetDefinition> items;

        [ReadOnly]
        public SingletonAssetContainer<BlobAssetReference<Collider>>.Reader actionColliders;

        [ReadOnly]
        public ComponentLookup<Disabled> disabled;

        [ReadOnly]
        public ComponentLookup<Translation> translations;

        [ReadOnly, NativeDisableContainerSafetyRestriction]
        public ComponentLookup<Rotation> rotations;

        [ReadOnly]
        public ComponentLookup<PhysicsMass> physicsMasses;

        [ReadOnly]
        public ComponentLookup<PhysicsCollider> physicsColliders;

        [ReadOnly, NativeDisableContainerSafetyRestriction]
        public ComponentLookup<GameNodeVelocity> velocities;

        [ReadOnly]
        public ComponentLookup<GameEntityActionCommand> commands;

        [ReadOnly]
        public EntityTypeHandle entityArrayType;

        [ReadOnly]
        public ComponentTypeHandle<Disabled> disabledType;

        [ReadOnly]
        public ComponentTypeHandle<Translation> translationType;

        [ReadOnly]
        public ComponentTypeHandle<GameNodeStatus> statusType;

        [ReadOnly]
        public ComponentTypeHandle<GameNodeSurface> surfaceType;

        [ReadOnly]
        public ComponentTypeHandle<GameNodeCharacterData> characterType;

        [ReadOnly]
        public ComponentTypeHandle<GameEntityCamp> campType;

        [ReadOnly]
        public ComponentTypeHandle<GameEntityActorData> actorType;

        [ReadOnly]
        public ComponentTypeHandle<GameEntityArchetype> archetypeType;

        [ReadOnly]
        public ComponentTypeHandle<GameEntityActionCommand> commandType;

        [ReadOnly]
        public BufferTypeHandle<GameEntityAction> entityActionType;

        [ReadOnly]
        public BufferTypeHandle<GameEntityItem> entityItemType;

        [ReadOnly]
        public BufferTypeHandle<GameEntityActorActionData> actorActionType;

        public BufferTypeHandle<GameEntityActorActionInfo> actorActionInfoType;

        public BufferTypeHandle<GameNodeVelocityComponent> velocityComponentType;

        public BufferTypeHandle<GameNodePosition> positionType;

        public ComponentTypeHandle<Rotation> rotationType;

        public ComponentTypeHandle<GameNodeDelay> delayType;

        public ComponentTypeHandle<GameNodeVelocity> velocityType;

        public ComponentTypeHandle<GameNodeAngle> angleType;

        public ComponentTypeHandle<GameNodeCharacterAngle> characterAngleType;

        public ComponentTypeHandle<GameNodeCharacterVelocity> characterVelocityType;

        public ComponentTypeHandle<PhysicsVelocity> physicsVelocityType;

        public ComponentTypeHandle<GameNodeActorStatus> actorStatusType;

        public ComponentTypeHandle<GameEntityActorTime> actorTimeType;

        public ComponentTypeHandle<GameEntityActorInfo> actorInfoType;

        public ComponentTypeHandle<GameEntityActionCommander> commanderType;

        public ComponentTypeHandle<GameEntityCommandVersion> commandVersionType;

        [NativeDisableContainerSafetyRestriction]
        public ComponentLookup<GameEntityCommandVersion> commandVersions;

        [WriteOnly, NativeDisableParallelForRestriction]
        public ComponentLookup<GameEntityActionInfo> actionInfos;

        [WriteOnly, NativeDisableContainerSafetyRestriction]
        public ComponentLookup<GameNodeStatus> nodeStates;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<GameActionStatus> actionStates;

        public EntityCommandQueue<GameEntityCommandActionCreate>.ParallelWriter entityManager;

#if GAME_DEBUG_COMPARSION
        public uint frameIndex;
        public FixedString32Bytes angleName;
        public FixedString32Bytes delayTimeName;
        public FixedString32Bytes entityName;
        public FixedString32Bytes commandTimeName;
        public FixedString32Bytes forwardName;
        public FixedString32Bytes offsetName;
        public FixedString32Bytes rotationName;
        public FixedString32Bytes positionName;
        public FixedString32Bytes distanceName;

        public ComparisonStream<uint> stream;
        [ReadOnly]
        public ComponentTypeHandle<GameEntityIndex> entityIndexType;
#endif

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            if (!chunk.Has(ref commandType))
                return;

            Act act;
            act.isDisabled = chunk.Has(ref disabledType);
            act.gravity = gravity;
            act.actions = actions;
            act.items = items;
            act.actionColliders = actionColliders;
            act.disabled = disabled;
            act.translationMap = translations;
            act.rotationMap = rotations;
            act.physicsMasses = physicsMasses;
            act.physicsColliders = physicsColliders;
            act.velocityMap = velocities;
            act.commandMap = commands;
            act.entityArray = chunk.GetNativeArray(entityArrayType);
            act.translations = chunk.GetNativeArray(ref translationType);
            act.states = chunk.GetNativeArray(ref statusType);
            act.surfaces = chunk.GetNativeArray(ref surfaceType);
            act.characters = chunk.GetNativeArray(ref characterType);
            act.camps = chunk.GetNativeArray(ref campType);
            act.actors = chunk.GetNativeArray(ref actorType);
            act.archetypes = chunk.GetNativeArray(ref archetypeType);
            act.commands = chunk.GetNativeArray(ref commandType);
            act.commanders = chunk.GetNativeArray(ref commanderType);
            act.entityActions = chunk.GetBufferAccessor(ref entityActionType);
            act.entityItems = chunk.GetBufferAccessor(ref entityItemType);
            act.actorActions = chunk.GetBufferAccessor(ref actorActionType);
            act.actorActionInfos = chunk.GetBufferAccessor(ref actorActionInfoType);
            act.velocityComponents = chunk.GetBufferAccessor(ref velocityComponentType);
            act.positions = chunk.GetBufferAccessor(ref positionType);
            act.rotations = chunk.GetNativeArray(ref rotationType);
            act.delay = chunk.GetNativeArray(ref delayType);
            act.velocities = chunk.GetNativeArray(ref velocityType);
            act.angles = chunk.GetNativeArray(ref angleType);
            act.characterAngles = chunk.GetNativeArray(ref characterAngleType);
            act.characterVelocities = chunk.GetNativeArray(ref characterVelocityType);
            act.physicsVelocities = chunk.GetNativeArray(ref physicsVelocityType);
            //act.characterFlags = chunk.GetNativeArray(characterFlagType);
            act.actorStates = chunk.GetNativeArray(ref actorStatusType);
            act.actorTimes = chunk.GetNativeArray(ref actorTimeType);
            act.actorInfos = chunk.GetNativeArray(ref actorInfoType);
            act.commandVersions = chunk.GetNativeArray(ref commandVersionType);
            act.commandVersionMap = commandVersions;
            act.actionInfos = actionInfos;
            act.actionStates = actionStates;
            act.nodeStatusMap = nodeStates;
            act.entityManager = entityManager;

#if GAME_DEBUG_COMPARSION
            act.frameIndex = frameIndex;
            act.angleName = angleName;
            act.delayTimeName = delayTimeName;
            act.entityName = entityName;
            act.commandTimeName = commandTimeName;
            act.forwardName = forwardName;
            act.offsetName = offsetName;
            act.rotationName = rotationName;
            act.positionName = positionName;
            act.distanceName = distanceName;
            act.stream = stream;
            act.entityIndices = chunk.GetNativeArray(ref entityIndexType);
#endif

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
            {
                act.Execute(i);

                chunk.SetComponentEnabled(ref commanderType, i, false);
            }
        }
    }

    private EntityQuery __group;
    private EntityQuery __physicsStepGroup;

    private EntityQuery __actionSetGroup;
    private EntityQuery __actionInfoSetGroup;

    private ComponentLookup<Disabled> __disabled;

    private ComponentLookup<Translation> __translations;

    private ComponentLookup<Rotation> __rotations;

    private ComponentLookup<PhysicsMass> __physicsMasses;

    private ComponentLookup<PhysicsCollider> __physicsColliders;

    private ComponentLookup<GameNodeVelocity> __velocities;

    private ComponentLookup<GameEntityActionCommand> __commands;

    private EntityTypeHandle __entityArrayType;

    private ComponentTypeHandle<Disabled> __disabledType;

    private ComponentTypeHandle<Translation> __translationType;

    private ComponentTypeHandle<GameNodeStatus> __statusType;

    private ComponentTypeHandle<GameNodeSurface> __surfaceType;

    private ComponentTypeHandle<GameNodeCharacterData> __characterType;

    private ComponentTypeHandle<GameEntityCamp> __campType;

    private ComponentTypeHandle<GameEntityActorData> __actorType;

    private ComponentTypeHandle<GameEntityArchetype> __archetypeType;

    private ComponentTypeHandle<GameEntityActionCommand> __commandType;

    private BufferTypeHandle<GameEntityAction> __entityActionType;

    private BufferTypeHandle<GameEntityItem> __entityItemType;

    private BufferTypeHandle<GameEntityActorActionData> __actorActionType;

    private BufferTypeHandle<GameEntityActorActionInfo> __actorActionInfoType;

    private BufferTypeHandle<GameNodeVelocityComponent> __velocityComponentType;

    private BufferTypeHandle<GameNodePosition> __positionType;

    private ComponentTypeHandle<Rotation> __rotationType;

    private ComponentTypeHandle<GameNodeDelay> __delayType;

    private ComponentTypeHandle<GameNodeVelocity> __velocityType;

    private ComponentTypeHandle<GameNodeAngle> __angleType;

    private ComponentTypeHandle<GameNodeCharacterAngle> __characterAngleType;

    private ComponentTypeHandle<GameNodeCharacterVelocity> __characterVelocityType;

    private ComponentTypeHandle<PhysicsVelocity> __physicsVelocityType;

    private ComponentTypeHandle<GameNodeActorStatus> __actorStatusType;

    private ComponentTypeHandle<GameEntityActorTime> __actorTimeType;

    private ComponentTypeHandle<GameEntityActorInfo> __actorInfoType;

    private ComponentTypeHandle<GameEntityActionCommander> __commanderType;

    private ComponentTypeHandle<GameEntityCommandVersion> __commandVersionType;

    private ComponentLookup<GameEntityCommandVersion> __commandVersions;

    private ComponentLookup<GameEntityActionInfo> __actionInfos;

    private ComponentLookup<GameNodeStatus> __nodeStates;

    private ComponentLookup<GameActionStatus> __actionStates;

#if GAME_DEBUG_COMPARSION
    private GameRollbackFrame __frame;
#endif

    private EntityCommandPool<GameEntityCommandActionCreate> __endFrameBarrier;
    private SingletonAssetContainer<BlobAssetReference<Collider>> __actionColliders;

    public void OnCreate(ref SystemState state)
    {
        __group = state.GetEntityQuery(
            new EntityQueryDesc()
            {
                All = new ComponentType[]
                {
                    ComponentType.ReadOnly<GameNodeStatus>(),
                    ComponentType.ReadOnly<GameEntityCamp>(),
                    ComponentType.ReadOnly<GameEntityActorData>(),
                    ComponentType.ReadOnly<GameEntityArchetype>(),
                    ComponentType.ReadOnly<GameEntityActorActionData>(),
                    ComponentType.ReadWrite<GameEntityActionCommander>(),
                    ComponentType.ReadWrite<GameEntityCommandVersion>(),
                    ComponentType.ReadWrite<GameEntityActorActionInfo>(),
                    ComponentType.ReadWrite<GameEntityActorInfo>(),
                    ComponentType.ReadWrite<GameEntityActorTime>(),
                },
                None = new ComponentType[]
                {
                    typeof(GameNodeParent)
                },

                Options = EntityQueryOptions.IncludeDisabledEntities
            });

        __group.SetChangedVersionFilter(typeof(GameEntityActionCommander));

        __physicsStepGroup = state.GetEntityQuery(typeof(Unity.Physics.PhysicsStep));

#if GAME_DEBUG_COMPARSION
        __frame = new GameRollbackFrame(ref state);
#endif

        __actionSetGroup = state.GetEntityQuery(ComponentType.ReadOnly<GameActionSetData>());

        __actionInfoSetGroup = state.GetEntityQuery(ComponentType.ReadOnly<GameActionItemSetData>());

        __disabled = state.GetComponentLookup<Disabled>(true);
        __translations = state.GetComponentLookup<Translation>(true);
        __rotations = state.GetComponentLookup<Rotation>(true);
        __physicsMasses = state.GetComponentLookup<PhysicsMass>(true);
        __physicsColliders = state.GetComponentLookup<PhysicsCollider>(true);
        __velocities = state.GetComponentLookup<GameNodeVelocity>(true);
        __commands = state.GetComponentLookup<GameEntityActionCommand>(true);
        __entityArrayType = state.GetEntityTypeHandle();
        __disabledType = state.GetComponentTypeHandle<Disabled>(true);
        __translationType = state.GetComponentTypeHandle<Translation>(true);
        __statusType = state.GetComponentTypeHandle<GameNodeStatus>(true);
        __surfaceType = state.GetComponentTypeHandle<GameNodeSurface>(true);
        __characterType = state.GetComponentTypeHandle<GameNodeCharacterData>(true);
        __campType = state.GetComponentTypeHandle<GameEntityCamp>(true);
        __actorType = state.GetComponentTypeHandle<GameEntityActorData>(true);
        __archetypeType = state.GetComponentTypeHandle<GameEntityArchetype>(true);
        __commandType = state.GetComponentTypeHandle<GameEntityActionCommand>(true);
        __entityActionType = state.GetBufferTypeHandle<GameEntityAction>(true);
        __entityItemType = state.GetBufferTypeHandle<GameEntityItem>(true);
        __actorActionType = state.GetBufferTypeHandle<GameEntityActorActionData>(true);
        __actorActionInfoType = state.GetBufferTypeHandle<GameEntityActorActionInfo>();
        __velocityComponentType = state.GetBufferTypeHandle<GameNodeVelocityComponent>();
        __positionType = state.GetBufferTypeHandle<GameNodePosition>();
        __rotationType = state.GetComponentTypeHandle<Rotation>();
        __delayType = state.GetComponentTypeHandle<GameNodeDelay>();
        __velocityType = state.GetComponentTypeHandle<GameNodeVelocity>();
        __angleType = state.GetComponentTypeHandle<GameNodeAngle>();
        __characterAngleType = state.GetComponentTypeHandle<GameNodeCharacterAngle>();
        __characterVelocityType = state.GetComponentTypeHandle<GameNodeCharacterVelocity>();
        __physicsVelocityType = state.GetComponentTypeHandle<PhysicsVelocity>();
        __actorStatusType = state.GetComponentTypeHandle<GameNodeActorStatus>();
        __actorTimeType = state.GetComponentTypeHandle<GameEntityActorTime>();
        __actorInfoType = state.GetComponentTypeHandle<GameEntityActorInfo>();
        __commanderType = state.GetComponentTypeHandle<GameEntityActionCommander>();
        __commandVersionType = state.GetComponentTypeHandle<GameEntityCommandVersion>();
        __commandVersions = state.GetComponentLookup<GameEntityCommandVersion>();
        __actionInfos = state.GetComponentLookup<GameEntityActionInfo>();
        __actionStates = state.GetComponentLookup<GameActionStatus>();
        __nodeStates = state.GetComponentLookup<GameNodeStatus>();

        __endFrameBarrier = state.World.GetOrCreateSystemUnmanaged<GameEntityActionBeginFactorySystem>().pool;

        __actionColliders = SingletonAssetContainer<BlobAssetReference<Collider>>.instance;

        //state.RequireForUpdate(__group);
    }

    public void OnDestroy(ref SystemState state)
    {
        /*if (actions.IsCreated)
            actions.Dispose();

        if (__items.IsCreated)
            __items.Dispose();

        base.OnDestroy();*/
    }

#if !GAME_DEBUG_COMPARSION
    [BurstCompile]
#endif
    public void OnUpdate(ref SystemState state)
    {
        if (__group.IsEmptyIgnoreFilter || __actionSetGroup.IsEmptyIgnoreFilter || __actionInfoSetGroup.IsEmptyIgnoreFilter)
            return;

        var entityMananger = __endFrameBarrier.Create();

        ActEx act;
        act.gravity = __physicsStepGroup.IsEmpty ? Unity.Physics.PhysicsStep.Default.Gravity : __physicsStepGroup.GetSingleton<Unity.Physics.PhysicsStep>().Gravity;
        act.actions = __actionSetGroup.GetSingleton<GameActionSetData>().definition;
        act.items = __actionInfoSetGroup.GetSingleton<GameActionItemSetData>().definition;
        act.actionColliders = __actionColliders.reader;
        act.disabled = __disabled.UpdateAsRef(ref state).UpdateAsRef(ref state);
        act.translations = __translations.UpdateAsRef(ref state);
        act.rotations = __rotations.UpdateAsRef(ref state);
        act.physicsMasses = __physicsMasses.UpdateAsRef(ref state);
        act.physicsColliders = __physicsColliders.UpdateAsRef(ref state);
        act.velocities = __velocities.UpdateAsRef(ref state);
        act.commands = __commands.UpdateAsRef(ref state);
        act.entityArrayType = __entityArrayType.UpdateAsRef(ref state);
        act.disabledType = __disabledType.UpdateAsRef(ref state);
        act.translationType = __translationType.UpdateAsRef(ref state);
        act.statusType = __statusType.UpdateAsRef(ref state);
        act.surfaceType = __surfaceType.UpdateAsRef(ref state);
        act.characterType = __characterType.UpdateAsRef(ref state);
        act.campType = __campType.UpdateAsRef(ref state);
        act.actorType = __actorType.UpdateAsRef(ref state);
        act.archetypeType = __archetypeType.UpdateAsRef(ref state);
        act.commandType = __commandType.UpdateAsRef(ref state);
        act.entityActionType = __entityActionType.UpdateAsRef(ref state);
        act.entityItemType = __entityItemType.UpdateAsRef(ref state);
        act.actorActionType = __actorActionType.UpdateAsRef(ref state);
        act.actorActionInfoType = __actorActionInfoType.UpdateAsRef(ref state);
        act.velocityComponentType = __velocityComponentType.UpdateAsRef(ref state);
        act.positionType = __positionType.UpdateAsRef(ref state);
        act.rotationType = __rotationType.UpdateAsRef(ref state);
        act.delayType = __delayType.UpdateAsRef(ref state);
        act.velocityType = __velocityType.UpdateAsRef(ref state);
        act.angleType = __angleType.UpdateAsRef(ref state);
        act.characterAngleType = __characterAngleType.UpdateAsRef(ref state);
        act.characterVelocityType = __characterVelocityType.UpdateAsRef(ref state);
        act.physicsVelocityType = __physicsVelocityType.UpdateAsRef(ref state);
        act.actorStatusType = __actorStatusType.UpdateAsRef(ref state);
        act.actorTimeType = __actorTimeType.UpdateAsRef(ref state);
        act.actorInfoType = __actorInfoType.UpdateAsRef(ref state);
        act.commanderType = __commanderType.UpdateAsRef(ref state);
        act.commandVersionType = __commandVersionType.UpdateAsRef(ref state);
        act.commandVersions = __commandVersions.UpdateAsRef(ref state);
        act.actionInfos = __actionInfos.UpdateAsRef(ref state);
        act.actionStates = __actionStates.UpdateAsRef(ref state);
        act.nodeStates = __nodeStates.UpdateAsRef(ref state);
        act.entityManager = entityMananger.parallelWriter;

#if GAME_DEBUG_COMPARSION
        uint frameIndex = __frame.index;

        act.frameIndex = frameIndex;
        act.angleName = "angle";
        act.delayTimeName = "delayTime";
        act.entityName = "entity";
        act.commandTimeName = "commandTime";
        act.forwardName = "forward";
        act.offsetName = "offset";
        act.rotationName = "rotation";
        act.positionName = "position";
        act.distanceName = "distance";

        var streamScheduler = GameComparsionSystem.instance.Create(false, frameIndex, typeof(GameEntityActorSystem).Name, state.World.Name);
        act.stream = streamScheduler.Begin(__group.CalculateEntityCount());
        act.entityIndexType = state.GetComponentTypeHandle<GameEntityIndex>(true);
#endif

        var jobHandle = act.ScheduleParallelByRef(__group, state.Dependency);

#if GAME_DEBUG_COMPARSION
        streamScheduler.End(jobHandle);
#endif

        entityMananger.AddJobHandleForProducer<ActEx>(jobHandle);

        __actionColliders.AddDependency(state.GetSystemID(), jobHandle);

        state.Dependency = jobHandle;
    }
}

[BurstCompile, UpdateInGroup(typeof(GameEntityActionSystemGroup), OrderLast = true)/*, UpdateBefore(typeof(GameEntityActionEndEntityCommandSystemGroup)), UpdateAfter(typeof(GameEntityActionSystemGroup))*/]
public partial struct GameEntityHitSystem : ISystem
{
    private struct ComputeHits
    {
        [ReadOnly]
        public NativeArray<Entity> entityArray;
        [ReadOnly]
        public NativeArray<GameEntityHit> inputs;
        [ReadOnly]
        public NativeArray<GameEntityActorHit> actorHits;
        [ReadOnly]
        public NativeArray<GameEntityActorData> instances;
        [ReadOnly]
        public NativeArray<GameEntityActorInfo> actorInfos;
        [ReadOnly]
        public NativeArray<GameEntityActionInfo> actionInfos;
        [ReadOnly]
        public NativeArray<GameEntityCommandVersion> commandVersions;

        public NativeArray<GameEntityBreakCommand> commands;

        [NativeDisableContainerSafetyRestriction]
        public ComponentLookup<GameEntityHit> outputs;

        //public EntityCommandQueue<EntityCommandStructChange>.ParallelWriter entityManager;
        //public EntityComponentAssigner.ComponentDataParallelWriter<GameEntityBreakCommand> results;

        public bool Execute(int index)
        {
            var hit = inputs[index];
            if (hit.value > math.FLT_MIN_NORMAL && actorHits[index].destinationHit > math.FLT_MIN_NORMAL)
            {
                var actorInfo = actorInfos[index];

                //UnityEngine.Debug.Log("Hit: " + entityArray[index].ToString() + ":" + hit.value + ":" + actionInfo.hit + ":" + hit.time + ":" + actionInfo.time + (actorInfo.version != actionInfo.version));
                if (actorInfo.alertTime < hit.time)
                {
                    //判定当前技能的霸体
                    var actionInfo = actionInfos[index];
                    if (actionInfo.version != actorInfo.version || actionInfo.time < hit.time || actionInfo.hit < hit.value)
                    {
                        //UnityEngine.Debug.LogError($"{entity} : {hit.value} : {actionInfo.hit} : {hit.time}");

                        var instance = instances[index];

                        //commands.SetComponentEnabled(entity, true);

                        GameEntityBreakCommand command;
                        command.version = commandVersions[index].value;
                        command.hit = math.max(hit.value - actionInfo.hit, 0.0f);
                        command.alertTime = instance.alertTime;
                        command.delayTime = instance.delayTime;
                        command.time = hit.time;
                        command.normal = hit.normal;

                        commands[index] = command;

                        return true;
                    }
                    else
                    {
                        hit.value = 0.0f;
                        hit.normal = float3.zero;
                        outputs[entityArray[index]] = hit;
                    }
                }
            }

            return false;
        }
    }

    [BurstCompile]
    private struct ComputeHitsEx : IJobChunk
    {
        [ReadOnly]
        public EntityTypeHandle entityType;
        [ReadOnly]
        public ComponentTypeHandle<GameEntityHit> hitType;
        [ReadOnly]
        public ComponentTypeHandle<GameEntityActorHit> actorHitType;
        [ReadOnly]
        public ComponentTypeHandle<GameEntityActorData> instanceType;
        [ReadOnly]
        public ComponentTypeHandle<GameEntityActorInfo> actorInfoType;
        [ReadOnly]
        public ComponentTypeHandle<GameEntityActionInfo> actionInfoType;
        [ReadOnly]
        public ComponentTypeHandle<GameEntityCommandVersion> commandVersionType;

        public ComponentTypeHandle<GameEntityBreakCommand> commandType;

        [NativeDisableContainerSafetyRestriction]
        public ComponentLookup<GameEntityHit> hits;

        //public EntityCommandQueue<EntityCommandStructChange>.ParallelWriter entityManager;
        //public EntityComponentAssigner.ComponentDataParallelWriter<GameEntityBreakCommand> results;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            ComputeHits computeHits;
            computeHits.entityArray = chunk.GetNativeArray(entityType);
            computeHits.inputs = chunk.GetNativeArray(ref hitType);
            computeHits.actorHits = chunk.GetNativeArray(ref actorHitType);
            computeHits.instances = chunk.GetNativeArray(ref instanceType);
            computeHits.actorInfos = chunk.GetNativeArray(ref actorInfoType);
            computeHits.actionInfos = chunk.GetNativeArray(ref actionInfoType);
            computeHits.commandVersions = chunk.GetNativeArray(ref commandVersionType);
            computeHits.commands = chunk.GetNativeArray(ref commandType);
            computeHits.outputs = hits;
            //computeHits.entityManager = entityManager;
            //computeHits.results = results;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
            {
                if (computeHits.Execute(i))
                    chunk.SetComponentEnabled(ref commandType, i, true);
            }
        }
    }

    private EntityQuery __group;

    private EntityTypeHandle __entityType;

    private ComponentTypeHandle<GameEntityHit> __hitType;
    private ComponentTypeHandle<GameEntityActorHit> __actorHitType;
    private ComponentTypeHandle<GameEntityActorData> __instanceType;
    private ComponentTypeHandle<GameEntityActorInfo> __actorInfoType;
    private ComponentTypeHandle<GameEntityActionInfo> __actionInfoType;
    private ComponentTypeHandle<GameEntityCommandVersion> __commandVersionType;

    private ComponentTypeHandle<GameEntityBreakCommand> __commandType;

    private ComponentLookup<GameEntityHit> __hits;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __group = builder
                    .WithAll<GameNodeDelay, GameEntityHit, GameEntityActorHit, GameEntityActorData, GameEntityActorInfo, GameEntityActionInfo, GameEntityCommandVersion>()
                    .Build(ref state);

        __group.SetChangedVersionFilter(ComponentType.ReadOnly<GameEntityHit>());

        __entityType = state.GetEntityTypeHandle();
        __hitType = state.GetComponentTypeHandle<GameEntityHit>(true);
        __actorHitType = state.GetComponentTypeHandle<GameEntityActorHit>(true);
        __instanceType = state.GetComponentTypeHandle<GameEntityActorData>(true);
        __actorInfoType = state.GetComponentTypeHandle<GameEntityActorInfo>(true);
        __actionInfoType = state.GetComponentTypeHandle<GameEntityActionInfo>(true);
        __commandVersionType = state.GetComponentTypeHandle<GameEntityCommandVersion>(true);
        __commandType = state.GetComponentTypeHandle<GameEntityBreakCommand>();
        __hits = state.GetComponentLookup<GameEntityHit>();
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {

    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        ComputeHitsEx computeHits;
        computeHits.entityType = __entityType.UpdateAsRef(ref state);
        computeHits.hitType = __hitType.UpdateAsRef(ref state);
        computeHits.actorHitType = __actorHitType.UpdateAsRef(ref state);
        computeHits.instanceType = __instanceType.UpdateAsRef(ref state);
        computeHits.actorInfoType = __actorInfoType.UpdateAsRef(ref state);
        computeHits.actionInfoType = __actionInfoType.UpdateAsRef(ref state);
        computeHits.commandVersionType = __commandVersionType.UpdateAsRef(ref state);
        computeHits.commandType = __commandType.UpdateAsRef(ref state);
        computeHits.hits = __hits.UpdateAsRef(ref state);
        state.Dependency = computeHits.ScheduleParallelByRef(__group, state.Dependency);
    }
}

[BurstCompile, UpdateInGroup(typeof(GameUpdateSystemGroup)), UpdateAfter(typeof(GameEntityActionSystemGroup))/*UpdateAfter(typeof(GameEntityActionEndEntityCommandSystemGroup))*/]
public partial struct GameEntityBreakSystem : ISystem
{
    private struct Interrupt
    {
        public bool isDisabled;

        //[ReadOnly]
        //public ComponentLookup<GameActionDisabled> actionDisabled;

        [ReadOnly]
        public NativeArray<Entity> entityArray;

        [ReadOnly]
        public NativeArray<GameEntityBreakCommand> commands;

        [ReadOnly]
        public NativeArray<GameEntityEventInfo> eventInfos;

        [ReadOnly]
        public NativeArray<GameNodeStatus> nodeStates;

        [ReadOnly]
        public NativeArray<GameNodeSurface> surfaces;

        [ReadOnly]
        public BufferAccessor<GameEntityActorDelay> actorDelay;

        [ReadOnly]
        public BufferAccessor<GameEntityAction> entityActions;

        public BufferAccessor<GameNodeVelocityComponent> velocityComponents;

        public BufferAccessor<GameNodePosition> positions;

        public NativeArray<GameEntityCommandVersion> commandVersions;

        public NativeArray<GameEntityActorInfo> actorInfos;

        public NativeArray<GameEntityActorTime> actorTimes;

        public NativeArray<GameEntityHit> hits;

        public NativeArray<GameNodeDelay> delay;

        public NativeArray<GameNodeVelocity> velocities;

        public NativeArray<GameNodeAngle> angles;

        [WriteOnly, NativeDisableParallelForRestriction]
        public ComponentLookup<GameEntityBreakInfo> breakInfos;

        [NativeDisableContainerSafetyRestriction]
        public ComponentLookup<GameNodeStatus> nodeStatusMap;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<GameActionStatus> actionStates;

#if GAME_DEBUG_COMPARSION
        public uint frameIndex;
        public FixedString32Bytes alertTimeName;
        public FixedString32Bytes commandTimeName;
        public FixedString32Bytes commandDelayTimeName;
        public FixedString32Bytes commandAlertTimeName;

        public ComparisonStream<uint> stream;

        [ReadOnly]
        public NativeArray<GameEntityIndex> entityIndices;
#endif

        public void Execute(int index)
        {
            GameEntityBreakCommand command = commands[index];
            GameEntityCommandVersion commandVersion = commandVersions[index];
            if (command.version != commandVersion.value)
                return;

            ++commandVersion.value;
            commandVersions[index] = commandVersion;

            if (isDisabled)
                return;

            GameNodeStatus nodeStatus = nodeStates[index];
            if ((nodeStatus.value & GameNodeStatus.STOP) == GameNodeStatus.STOP)
                return;

            bool isAlert = command.delayTime > math.FLT_MIN_NORMAL;
            var actorInfo = actorInfos[index];

#if GAME_DEBUG_COMPARSION
            stream.Begin(entityIndices[index].value);
            stream.Assert(alertTimeName, (double)actorInfo.alertTime);
            stream.Assert(commandTimeName, (double)command.time);
            stream.Assert(commandDelayTimeName, (double)command.delayTime);
            stream.Assert(commandAlertTimeName, (double)command.alertTime);
#endif

            if (!isAlert || actorInfo.alertTime < command.time)//isAlert ? actorInfo.alertTime < command.time : actorInfo.castingTime > command.time)
            {
                if (index < entityActions.Length)
                    GameEntityAction.Break(entityActions[index], ref actionStates);

                ++actorInfo.version;

                if (index < this.velocityComponents.Length)
                {
                    var velocityComponents = this.velocityComponents[index];
                    int numVelocityComponents = velocityComponents.Length;
                    for (int i = 0; i < numVelocityComponents; ++i)
                    {
                        ref var velocityComponent = ref velocityComponents.ElementAt(i);
                        if (velocityComponent.mode == GameNodeVelocityComponent.Mode.Direct)
                        {
                            velocityComponents.RemoveAt(i--);

                            --numVelocityComponents;
                        }
                    }
                }

                int delayIndex = -1;
                if (isAlert)
                {
                    var hit = index < hits.Length ? hits[index] : default;
                    hit.value = 0.0f;
                    hit.normal = float3.zero;

                    if (index < this.actorDelay.Length)
                    {
                        var actorDelay = this.actorDelay[index];
                        GameEntityActorDelay targetDeley;
                        delayIndex = actorDelay.Length;
                        for (int i = 0; i < delayIndex; ++i)
                        {
                            targetDeley = actorDelay[i];
                            if (targetDeley.minHit < command.hit)
                            {
                                var normal = math.normalizesafe(command.normal);
                                if ((targetDeley.flag & GameEntityActorDelay.Flag.ForceToTurn) == GameEntityActorDelay.Flag.ForceToTurn)
                                {
                                    var forward = -math.rotate(math.inverse(surfaces[index].rotation), normal);

                                    GameNodeAngle angle;
                                    angle.value = (half)math.atan2(forward.x, forward.z);
                                    this.angles[index] = angle;
                                }

                                hit.value = targetDeley.hitOverride;

                                command.alertTime = targetDeley.alertTime;
                                command.delayTime = targetDeley.delayTime;

                                if (targetDeley.speed > math.FLT_MIN_NORMAL)
                                {
                                    GameNodeVelocityComponent velocityComponent;
                                    velocityComponent.mode = GameNodeVelocityComponent.Mode.Direct;
                                    velocityComponent.duration = targetDeley.duration;
                                    velocityComponent.time = command.time + targetDeley.startTime;
                                    velocityComponent.value = normal * targetDeley.speed;

                                    velocityComponents[index].Add(velocityComponent);
                                }

                                delayIndex = i;

                                break;
                            }
                        }
                    }
                    else
                        delayIndex = 0;

                    if(index < hits.Length)
                        hits[index] = hit;
                }

                actorInfo.alertTime = command.time + (isAlert ? command.alertTime : 0.0f);
                //actorInfo.castingTime = command.time;
                actorInfos[index] = actorInfo;

                if (index < actorTimes.Length)
                {
                    GameEntityActorTime actorTime;
                    actorTime.value = command.time;
                    actorTime.value += command.delayTime;
                    actorTimes[index] = actorTime;
                }

                if (index < this.delay.Length)
                {
                    GameNodeDelay delay;
                    delay.time = command.time;
                    delay.startTime = half.zero;
                    delay.endTime = (half)command.delayTime;
                    this.delay[index] = delay;

#if GAME_DEBUG_COMPARSION
                    //UnityEngine.Debug.Log($"Break: {entityArray[index].Index} : {(double)delay.time} : {(double)command.time} : {entityIndices[index].value} : {frameIndex}");
#endif
                }

                if (isAlert)
                {
                    if (index < velocities.Length)
                        velocities[index] = default;
                }
                else
                {
                    if (index < positions.Length)
                        positions[index].Clear();
                }

                Entity entity = entityArray[index];
                GameEntityBreakInfo breakInfo;
                breakInfo.version = actorInfo.version;
                breakInfo.delayIndex = delayIndex;
                breakInfo.commandTime = command.time;
                breakInfo.timeEventHandle = TimeEventHandle.Null;
                if (index < eventInfos.Length)
                {
                    GameEntityEventInfo eventInfo = eventInfos[index];
                    if (eventInfo.version == actorInfo.version)
                        breakInfo.timeEventHandle = eventInfo.timeEventHandle;
                }

                breakInfos[entity] = breakInfo;

                if (nodeStatus.value != 0)
                {
                    nodeStatus.value = 0;

                    nodeStatusMap[entity] = nodeStatus;
                }

                //UnityEngine.Debug.Log(entity.ToString() + this.delay[index].time);
            }

#if GAME_DEBUG_COMPARSION
            stream.End();
#endif
        }
    }

    [BurstCompile]
    private struct InterruptEx : IJobChunk
    {
        //[ReadOnly]
        //public ComponentLookup<GameActionDisabled> actionDisabled;

        [ReadOnly]
        public EntityTypeHandle entityArrayType;

        [ReadOnly]
        public ComponentTypeHandle<Disabled> disabledType;

        [ReadOnly]
        public ComponentTypeHandle<GameNodeStatus> nodeStatusType;

        [ReadOnly]
        public ComponentTypeHandle<GameNodeSurface> surfaceType;

        [ReadOnly]
        public ComponentTypeHandle<GameEntityEventInfo> eventInfoType;

        [ReadOnly]
        public BufferTypeHandle<GameEntityAction> entityActionType;

        [ReadOnly]
        public BufferTypeHandle<GameEntityActorDelay> actorDelayType;

        public BufferTypeHandle<GameNodeVelocityComponent> velocityComponentType;

        public BufferTypeHandle<GameNodePosition> positionType;

        public ComponentTypeHandle<GameEntityBreakCommand> commandType;

        public ComponentTypeHandle<GameEntityCommandVersion> commandVersionType;

        public ComponentTypeHandle<GameEntityActorInfo> actorInfoType;

        public ComponentTypeHandle<GameEntityActorTime> actorTimeType;

        public ComponentTypeHandle<GameEntityHit> hitType;

        public ComponentTypeHandle<GameNodeVelocity> velocityType;

        public ComponentTypeHandle<GameNodeDelay> delayType;

        public ComponentTypeHandle<GameNodeAngle> angleType;

        [WriteOnly, NativeDisableParallelForRestriction]
        public ComponentLookup<GameEntityBreakInfo> breakInfos;

        [NativeDisableContainerSafetyRestriction]
        public ComponentLookup<GameNodeStatus> entityStates;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<GameActionStatus> actionStates;

#if GAME_DEBUG_COMPARSION
        public uint frameIndex;
        public FixedString32Bytes alertTimeName;
        public FixedString32Bytes commandTimeName;
        public FixedString32Bytes commandDelayTimeName;
        public FixedString32Bytes commandAlertTimeName;

        public ComparisonStream<uint> stream;

        [ReadOnly]
        public ComponentTypeHandle<GameEntityIndex> entityIndexType;
#endif

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Interrupt interrupt;
            interrupt.isDisabled = chunk.Has(ref disabledType);
            //interrupt.actionDisabled = actionDisabled;
            interrupt.actionStates = actionStates;
            interrupt.entityArray = chunk.GetNativeArray(entityArrayType);
            interrupt.commands = chunk.GetNativeArray(ref commandType);
            interrupt.nodeStates = chunk.GetNativeArray(ref nodeStatusType);
            interrupt.surfaces = chunk.GetNativeArray(ref surfaceType);
            interrupt.eventInfos = chunk.GetNativeArray(ref eventInfoType);
            interrupt.entityActions = chunk.GetBufferAccessor(ref entityActionType);
            interrupt.velocityComponents = chunk.GetBufferAccessor(ref velocityComponentType);
            interrupt.positions = chunk.GetBufferAccessor(ref positionType);
            interrupt.commandVersions = chunk.GetNativeArray(ref commandVersionType);
            interrupt.actorInfos = chunk.GetNativeArray(ref actorInfoType);
            interrupt.actorTimes = chunk.GetNativeArray(ref actorTimeType);
            interrupt.hits = chunk.GetNativeArray(ref hitType);
            interrupt.velocities = chunk.GetNativeArray(ref velocityType);
            interrupt.actorDelay = chunk.GetBufferAccessor(ref actorDelayType);
            interrupt.angles = chunk.GetNativeArray(ref angleType);
            interrupt.delay = chunk.GetNativeArray(ref delayType);
            interrupt.breakInfos = breakInfos;
            interrupt.nodeStatusMap = entityStates;

#if GAME_DEBUG_COMPARSION
            interrupt.frameIndex = frameIndex;
            interrupt.alertTimeName = alertTimeName;
            interrupt.commandTimeName = commandTimeName;
            interrupt.commandDelayTimeName = commandDelayTimeName;
            interrupt.commandAlertTimeName = commandAlertTimeName;

            interrupt.stream = stream;

            interrupt.entityIndices = chunk.GetNativeArray(ref entityIndexType);
#endif

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
            {
                interrupt.Execute(i);

                chunk.SetComponentEnabled(ref commandType, i, false);
            }
        }
    }

    private EntityQuery __group;

    private EntityTypeHandle __entityArrayType;

    private ComponentTypeHandle<Disabled> __disabledType;

    private ComponentTypeHandle<GameNodeStatus> __nodeStatusType;

    private ComponentTypeHandle<GameNodeSurface> __surfaceType;

    private ComponentTypeHandle<GameEntityEventInfo> __eventInfoType;

    private BufferTypeHandle<GameEntityAction> __entityActionType;

    private BufferTypeHandle<GameEntityActorDelay> __actorDelayType;

    private BufferTypeHandle<GameNodeVelocityComponent> __velocityComponentType;

    private BufferTypeHandle<GameNodePosition> __positionType;

    private ComponentTypeHandle<GameEntityBreakCommand> __commandType;

    private ComponentTypeHandle<GameEntityCommandVersion> __commandVersionType;

    private ComponentTypeHandle<GameEntityActorInfo> __actorInfoType;

    private ComponentTypeHandle<GameEntityActorTime> __actorTimeType;

    private ComponentTypeHandle<GameEntityHit> __hitType;

    private ComponentTypeHandle<GameNodeVelocity> __velocityType;

    private ComponentTypeHandle<GameNodeDelay> __delayType;

    private ComponentTypeHandle<GameNodeAngle> __angleType;

    private ComponentLookup<GameEntityBreakInfo> __breakInfos;

    private ComponentLookup<GameNodeStatus> __entityStates;

    private ComponentLookup<GameActionStatus> __actionStates;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __group = builder
                .WithAll<GameEntityEventInfo, GameEntityActorInfo, GameNodeStatus>()
                .WithAllRW<GameEntityBreakCommand, GameEntityCommandVersion>()
                .WithOptions(EntityQueryOptions.IncludeDisabledEntities)
                .Build(ref state);

        __group.SetChangedVersionFilter(ComponentType.ReadWrite<GameEntityBreakCommand>());

        __entityArrayType = state.GetEntityTypeHandle();
        __disabledType = state.GetComponentTypeHandle<Disabled>(true);
        __nodeStatusType = state.GetComponentTypeHandle<GameNodeStatus>(true);
        __surfaceType = state.GetComponentTypeHandle<GameNodeSurface>(true);
        __eventInfoType = state.GetComponentTypeHandle<GameEntityEventInfo>(true);
        __entityActionType = state.GetBufferTypeHandle<GameEntityAction>(true);
        __actorDelayType = state.GetBufferTypeHandle<GameEntityActorDelay>(true);
        __velocityComponentType = state.GetBufferTypeHandle<GameNodeVelocityComponent>();
        __positionType = state.GetBufferTypeHandle<GameNodePosition>();
        __commandType = state.GetComponentTypeHandle<GameEntityBreakCommand>();
        __commandVersionType = state.GetComponentTypeHandle<GameEntityCommandVersion>();
        __actorInfoType = state.GetComponentTypeHandle<GameEntityActorInfo>();
        __actorTimeType = state.GetComponentTypeHandle<GameEntityActorTime>();
        __hitType = state.GetComponentTypeHandle<GameEntityHit>();
        __delayType = state.GetComponentTypeHandle<GameNodeDelay>();
        __angleType = state.GetComponentTypeHandle<GameNodeAngle>();
        __velocityType = state.GetComponentTypeHandle<GameNodeVelocity>();
        __breakInfos = state.GetComponentLookup<GameEntityBreakInfo>();
        __entityStates = state.GetComponentLookup<GameNodeStatus>();
        __actionStates = state.GetComponentLookup<GameActionStatus>();
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
        InterruptEx interrupt;
        interrupt.entityArrayType = __entityArrayType.UpdateAsRef(ref state);
        interrupt.disabledType = __disabledType.UpdateAsRef(ref state); 
        interrupt.nodeStatusType = __nodeStatusType.UpdateAsRef(ref state);
        interrupt.surfaceType = __surfaceType.UpdateAsRef(ref state);
        interrupt.eventInfoType = __eventInfoType.UpdateAsRef(ref state);
        interrupt.entityActionType = __entityActionType.UpdateAsRef(ref state);
        interrupt.actorDelayType = __actorDelayType.UpdateAsRef(ref state);
        interrupt.velocityComponentType = __velocityComponentType.UpdateAsRef(ref state);
        interrupt.positionType = __positionType.UpdateAsRef(ref state);
        interrupt.commandType = __commandType.UpdateAsRef(ref state);
        interrupt.commandVersionType = __commandVersionType.UpdateAsRef(ref state);
        interrupt.actorInfoType = __actorInfoType.UpdateAsRef(ref state);
        interrupt.actorTimeType = __actorTimeType.UpdateAsRef(ref state);
        interrupt.hitType = __hitType.UpdateAsRef(ref state);
        interrupt.delayType = __delayType.UpdateAsRef(ref state);
        interrupt.angleType = __angleType.UpdateAsRef(ref state);
        interrupt.velocityType = __velocityType.UpdateAsRef(ref state);
        interrupt.breakInfos = __breakInfos.UpdateAsRef(ref state);
        interrupt.entityStates = __entityStates.UpdateAsRef(ref state);
        interrupt.actionStates = __actionStates.UpdateAsRef(ref state);

#if GAME_DEBUG_COMPARSION
        interrupt.frameIndex = SystemAPI.GetSingleton<GameSyncManager>().SyncTime.frameIndex;
        interrupt.alertTimeName = "alertTime";
        interrupt.commandTimeName = "commandTime";
        interrupt.commandDelayTimeName = "commandDelayTime";
        interrupt.commandAlertTimeName = "commandAlertTime";

        var streamScheduler = GameComparsionSystem.instance.Create(false, interrupt.frameIndex, typeof(GameEntityBreakSystem).Name, state.World.Name);
        interrupt.stream = streamScheduler.Begin(__group.CalculateEntityCount());
        interrupt.entityIndexType = state.GetComponentTypeHandle<GameEntityIndex>(true);
#endif

        state.Dependency = interrupt.ScheduleParallelByRef(__group, state.Dependency);

#if GAME_DEBUG_COMPARSION
        streamScheduler.End(state.Dependency);
#endif
    }
}

[BurstCompile, UpdateInGroup(typeof(GameStatusSystemGroup))]
public partial struct GameEntityStatusSystem : ISystem
{
    private struct UpdateCommandVersions
    {
        [ReadOnly]
        public NativeArray<Entity> entityArray;
        [ReadOnly]
        public NativeArray<GameNodeStatus> states;
        [ReadOnly]
        public NativeArray<GameNodeOldStatus> oldStates;

        [ReadOnly]
        public NativeArray<GameEntityEventInfo> eventInfos;

        public NativeArray<GameEntityActorTime> actorTimes;

        public NativeArray<GameEntityActorInfo> actorInfos;

        public BufferAccessor<GameEntityActorActionInfo> actorActionInfos;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<GameEntityEventCommand> eventCommands;
        [NativeDisableParallelForRestriction]
        public ComponentLookup<GameEntityActionCommand> actionCommands;
        [NativeDisableParallelForRestriction]
        public ComponentLookup<GameEntityBreakCommand> breakCommands;

        public EntityCommandQueue<TimeEventHandle>.ParallelWriter timeEventHandles;

        public void Execute(int index)
        {
            int value = states[index].value & GameNodeStatus.OVER, oldValue = oldStates[index].value & GameNodeStatus.OVER;
            if (value == oldValue || value != GameNodeStatus.OVER)
                return;

            actorTimes[index] = default;

            actorInfos[index] = default;

            var actorActionInfos = this.actorActionInfos[index];
            int numActorActionInfos = actorActionInfos.Length;
            for (int i = 0; i < numActorActionInfos; ++i)
                actorActionInfos[i] = default;

            var entity = entityArray[index];

            timeEventHandles.Enqueue(eventInfos[index].timeEventHandle);
            timeEventHandles.Enqueue(eventCommands[entity].handle);

            eventCommands.SetComponentEnabled(entity, false);
            actionCommands.SetComponentEnabled(entity, false);
            breakCommands.SetComponentEnabled(entity, false);
        }
    }

    [BurstCompile]
    private struct UpdateCommandVersionsEx : IJobChunk, IEntityCommandProducerJob
    {
        [ReadOnly]
        public EntityTypeHandle entityType;
        [ReadOnly]
        public ComponentTypeHandle<GameNodeStatus> statusType;
        [ReadOnly]
        public ComponentTypeHandle<GameNodeOldStatus> oldStatusType;
        [ReadOnly]
        public ComponentTypeHandle<GameEntityEventInfo> eventInfoType;

        public ComponentTypeHandle<GameEntityActorTime> actorTimeType;

        public ComponentTypeHandle<GameEntityActorInfo> actorInfoType;

        public BufferTypeHandle<GameEntityActorActionInfo> actorActionInfoType;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<GameEntityEventCommand> eventCommands;
        [NativeDisableParallelForRestriction]
        public ComponentLookup<GameEntityActionCommand> actionCommands;
        [NativeDisableParallelForRestriction]
        public ComponentLookup<GameEntityBreakCommand> breakCommands;

        public EntityCommandQueue<TimeEventHandle>.ParallelWriter timeEventHandles;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            UpdateCommandVersions updateCommandVersions;
            updateCommandVersions.entityArray = chunk.GetNativeArray(entityType);
            updateCommandVersions.states = chunk.GetNativeArray(ref statusType);
            updateCommandVersions.oldStates = chunk.GetNativeArray(ref oldStatusType);
            updateCommandVersions.eventInfos = chunk.GetNativeArray(ref eventInfoType);
            updateCommandVersions.actorTimes = chunk.GetNativeArray(ref actorTimeType);
            updateCommandVersions.actorInfos = chunk.GetNativeArray(ref actorInfoType);
            updateCommandVersions.actorActionInfos = chunk.GetBufferAccessor(ref actorActionInfoType);
            updateCommandVersions.eventCommands = eventCommands;
            updateCommandVersions.actionCommands = actionCommands;
            updateCommandVersions.breakCommands = breakCommands;
            updateCommandVersions.timeEventHandles = timeEventHandles;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                updateCommandVersions.Execute(i);
        }
    }

    private EntityQuery __group;
    private EntityCommandPool<TimeEventHandle> __timeEventHandles;

    public void OnCreate(ref SystemState state)
    {
        __group = state.GetEntityQuery(new EntityQueryDesc[]
            {
                new EntityQueryDesc()
                {
                    All = new ComponentType[]
                    {
                        ComponentType.ReadOnly<GameNodeStatus>(),
                        ComponentType.ReadOnly<GameNodeOldStatus>(),
                        ComponentType.ReadWrite<GameEntityCommandVersion>()
                    },

                    Options = EntityQueryOptions.IncludeDisabledEntities
                }
            });
        __group.SetChangedVersionFilter(
             new ComponentType[]
             {
                typeof(GameNodeStatus),
                typeof(GameNodeOldStatus)
             });

        __timeEventHandles = state.World.GetOrCreateSystemManaged<GameEntityTimeEventSystem>().pool;
    }

    public void OnDestroy(ref SystemState state)
    {
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var timeEventHandles = __timeEventHandles.Create();

        UpdateCommandVersionsEx updateCommandVersions;
        updateCommandVersions.entityType = state.GetEntityTypeHandle();
        updateCommandVersions.statusType = state.GetComponentTypeHandle<GameNodeStatus>(true);
        updateCommandVersions.oldStatusType = state.GetComponentTypeHandle<GameNodeOldStatus>(true);
        updateCommandVersions.eventInfoType = state.GetComponentTypeHandle<GameEntityEventInfo>(true);
        updateCommandVersions.actorTimeType = state.GetComponentTypeHandle<GameEntityActorTime>();
        updateCommandVersions.actorInfoType = state.GetComponentTypeHandle<GameEntityActorInfo>();
        updateCommandVersions.actorActionInfoType = state.GetBufferTypeHandle<GameEntityActorActionInfo>();
        updateCommandVersions.eventCommands = state.GetComponentLookup<GameEntityEventCommand>();
        updateCommandVersions.actionCommands = state.GetComponentLookup<GameEntityActionCommand>();
        updateCommandVersions.breakCommands = state.GetComponentLookup<GameEntityBreakCommand>();
        updateCommandVersions.timeEventHandles = timeEventHandles.parallelWriter;

        var jobHandle = updateCommandVersions.ScheduleParallel(__group, state.Dependency);

        timeEventHandles.AddJobHandleForProducer<UpdateCommandVersionsEx>(jobHandle);

        state.Dependency = jobHandle;
    }
}

[BurstCompile, UpdateInGroup(typeof(TimeSystemGroup)), UpdateAfter(typeof(GameSyncSystemGroup))]
public partial struct GameEntityCannelEventsSystem : ISystem
{
    private struct CannelEvents
    {
        [ReadOnly]
        public NativeArray<GameEntityActorInfo> actorInfos;
        [ReadOnly]
        public NativeArray<GameEntityBreakInfo> breakInfos;

        public EntityCommandQueue<TimeEventHandle>.ParallelWriter timeEventHandles;

        public void Execute(int index)
        {
            GameEntityBreakInfo breakInfo = breakInfos[index];
            if (breakInfo.version != actorInfos[index].version)
                return;

            timeEventHandles.Enqueue(breakInfo.timeEventHandle);
        }
    }

    [BurstCompile]
    private struct CannelEventsEx : IJobChunk, IEntityCommandProducerJob
    {
        [ReadOnly]
        public ComponentTypeHandle<GameEntityActorInfo> actorInfoType;
        [ReadOnly]
        public ComponentTypeHandle<GameEntityBreakInfo> breakInfoType;

        public EntityCommandQueue<TimeEventHandle>.ParallelWriter timeEventHandles;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            CannelEvents cannelEvents;
            cannelEvents.actorInfos = chunk.GetNativeArray(ref actorInfoType);
            cannelEvents.breakInfos = chunk.GetNativeArray(ref breakInfoType);
            cannelEvents.timeEventHandles = timeEventHandles;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                cannelEvents.Execute(i);
        }
    }

    private EntityQuery __group;
    private EntityCommandPool<TimeEventHandle> __timeEventHandles;

    public void OnCreate(ref SystemState state)
    {
        __group = state.GetEntityQuery(
            ComponentType.ReadOnly<GameEntityActorInfo>(),
            ComponentType.ReadOnly<GameEntityBreakInfo>());

        __group.SetChangedVersionFilter(typeof(GameEntityBreakInfo));

        __timeEventHandles = state.World.GetOrCreateSystemManaged<GameEntityTimeEventSystem>().pool;
    }

    public void OnDestroy(ref SystemState state)
    {
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var timeEventHandles = __timeEventHandles.Create();

        CannelEventsEx cannelEvents;
        cannelEvents.actorInfoType = state.GetComponentTypeHandle<GameEntityActorInfo>(true);
        cannelEvents.breakInfoType = state.GetComponentTypeHandle<GameEntityBreakInfo>(true);
        cannelEvents.timeEventHandles = timeEventHandles.parallelWriter;

        var jobHandle = cannelEvents.ScheduleParallel(__group, state.Dependency);

        timeEventHandles.AddJobHandleForProducer<CannelEventsEx>(jobHandle);

        state.Dependency = jobHandle;
    }
}

[BurstCompile, UpdateInGroup(typeof(GameRollbackSystemGroup)), UpdateAfter(typeof(GameEntityActorSystemGroup)), UpdateBefore(typeof(GameEntityActionBeginEntityCommandSystemGroup))]
public partial struct GameEntityClearActionSystem : ISystem
{
    private struct ClearActions
    {
        [ReadOnly]
        public NativeArray<Entity> entityArray;
        [ReadOnly]
        public NativeArray<GameActionStatus> states;
        [ReadOnly]
        public NativeArray<GameActionData> instances;
        public BufferLookup<GameEntityAction> entityActions;
        public EntityCommandQueue<Entity>.Writer entityManager;

        public void Execute(int index)
        {
            var status = states[index].value;
            if ((status & GameActionStatus.Status.Destroy) != GameActionStatus.Status.Destroy ||
                (status & GameActionStatus.Status.Managed) == GameActionStatus.Status.Managed)
                return;

            Entity entity = entityArray[index];

            //int temp = (int)status;
            //UnityEngine.Debug.LogError($"Destroy {entity.Index} : {entity.Version} : {temp}");

            var instance = instances[index];
            if (this.entityActions.HasBuffer(instance.entity))
            {
                var entityActions = this.entityActions[instance.entity];
                int numEntityActions = entityActions.Length;
                for (int i = 0; i < numEntityActions; ++i)
                {
                    if (entityActions[i] == entity)
                    {
                        entityActions.RemoveAt(i);

                        break;
                    }
                }
            }

            entityManager.Enqueue(entity);
        }
    }

    [BurstCompile]
    private struct ClearActionsEx : IJobChunk, IEntityCommandProducerJob
    {
        public uint lastSystemVersion;
        [ReadOnly]
        public EntityTypeHandle entityType;
        [ReadOnly]
        public ComponentTypeHandle<GameActionStatus> statusType;
        [ReadOnly]
        public ComponentTypeHandle<GameActionData> instanceType;
        public BufferLookup<GameEntityAction> entityActions;
        public EntityCommandQueue<Entity>.Writer entityManager;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            if (!chunk.DidChange(ref statusType, lastSystemVersion))
                return;

            ClearActions clearActions;
            clearActions.entityArray = chunk.GetNativeArray(entityType);
            clearActions.states = chunk.GetNativeArray(ref statusType);
            clearActions.instances = chunk.GetNativeArray(ref instanceType);
            clearActions.entityActions = entityActions;
            clearActions.entityManager = entityManager;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                clearActions.Execute(i);
        }
    }

    private struct ClearEntities
    {
        [ReadOnly]
        public NativeArray<Entity> entityArray;
        [ReadOnly]
        public BufferAccessor<GameEntityAction> entityActions;
        public EntityCommandQueue<EntityCommandStructChange>.ParallelWriter entityManager;

        public void Execute(int index)
        {
            if (entityActions[index].Length < 1)
            {
                EntityCommandStructChange command;
                command.entity = entityArray[index];

                command.componentType = ComponentType.ReadWrite<GameEntityAction>();
                entityManager.Enqueue(command);

                command.componentType = ComponentType.ReadWrite<GameEntityActionInfo>();
                entityManager.Enqueue(command);
            }
        }
    }

    [BurstCompile]
    private struct ClearEntitiesEx : IJobChunk, IEntityCommandProducerJob
    {
        [ReadOnly]
        public EntityTypeHandle entityType;
        [ReadOnly]
        public BufferTypeHandle<GameEntityAction> entityActionType;
        public EntityCommandQueue<EntityCommandStructChange>.ParallelWriter entityManager;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            ClearEntities clearEntities;
            clearEntities.entityArray = chunk.GetNativeArray(entityType);
            clearEntities.entityActions = chunk.GetBufferAccessor(ref entityActionType);
            clearEntities.entityManager = entityManager;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                clearEntities.Execute(i);
        }
    }

    private uint __lastSystemVersion;
    private EntityQuery __actionGroup;
    private EntityQuery __entityGroup;
    /*private EntityQuery __syncDataGroup;
    private EntityQuery __updateDataGroup;*/
    private GameUpdateTime __time;
    private EntityCommandPool<Entity> __destroyEntityCommander;
    private EntityCommandPool<EntityCommandStructChange> __removeComponentCommander;

    public void OnCreate(ref SystemState state)
    {
        __actionGroup = state.GetEntityQuery(
            ComponentType.ReadOnly<GameActionStatus>(),
            ComponentType.ReadOnly<GameActionData>()/*, 
            ComponentType.ReadOnly<GameActionDisabled>()*/);

        //__actionGroup.SetChangedVersionFilter(typeof(GameActionStatus));

        __entityGroup = state.GetEntityQuery(
            ComponentType.ReadOnly<GameEntityAction>(),
            ComponentType.Exclude<GameEntityActorData>());

        /*__syncDataGroup = state.GetEntityQuery(ComponentType.ReadOnly<GameSyncData>());
        __updateDataGroup = state.GetEntityQuery(ComponentType.ReadOnly<GameUpdateData>());*/

        __time = new GameUpdateTime(ref state);

        var manager = state.World.GetOrCreateSystemUnmanaged<GameEntityActionBeginStructChangeSystem>().manager;
        __destroyEntityCommander = manager.destoyEntityPool;
        __removeComponentCommander = manager.removeComponentPool;
    }

    public void OnDestroy(ref SystemState state)
    {

    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        /*var syncData = __syncDataGroup.GetSingleton<GameSyncData>();
        var updateData = __updateDataGroup.GetSingleton<GameUpdateData>();
        if (!updateData.IsUpdate(syncData.frameIndex))
            return;*/
        if (!__time.IsVail())
            return;

        var jobHandle = state.Dependency;
        var statusType = state.GetComponentTypeHandle<GameActionStatus>(true);
        uint globalSystemVersion = statusType.GlobalSystemVersion;
        if (ChangeVersionUtility.DidChange(globalSystemVersion, __lastSystemVersion))
        {
            var destroyEntityCommander = __destroyEntityCommander.Create();

            ClearActionsEx clearActions;
            clearActions.lastSystemVersion = __lastSystemVersion;
            clearActions.entityType = state.GetEntityTypeHandle();
            clearActions.statusType = state.GetComponentTypeHandle<GameActionStatus>(true);
            clearActions.instanceType = state.GetComponentTypeHandle<GameActionData>(true);
            clearActions.entityActions = state.GetBufferLookup<GameEntityAction>();
            clearActions.entityManager = destroyEntityCommander.writer;
            jobHandle = clearActions.Schedule(__actionGroup, jobHandle);

            destroyEntityCommander.AddJobHandleForProducer<ClearActionsEx>(jobHandle);

            __lastSystemVersion = globalSystemVersion;
        }

        if (!__entityGroup.IsEmptyIgnoreFilter)
        {
            var removeComponentCommander = __removeComponentCommander.Create();

            ClearEntitiesEx clearEntities;
            clearEntities.entityType = state.GetEntityTypeHandle();
            clearEntities.entityActionType = state.GetBufferTypeHandle<GameEntityAction>(true);
            clearEntities.entityManager = removeComponentCommander.parallelWriter;

            jobHandle = clearEntities.ScheduleParallel(__entityGroup, jobHandle);

            removeComponentCommander.AddJobHandleForProducer<ClearEntitiesEx>(jobHandle);
        }

        state.Dependency = jobHandle;
    }
}

[BurstCompile, UpdateInGroup(typeof(TimeSystemGroup)), UpdateBefore(typeof(GameSyncSystemGroup))]
public partial struct GameEntityActionHitClearSystem : ISystem
{
    /*private struct ClearActorHits
    {
        public NativeArray<GameEntityActorHit> instances;

        public void Execute(int index)
        {
            GameEntityActorHit instance;
            instance.sourceTimes = 0;
            instance.destinationTimes = 0;
            instance.sourceHit = 0.0f;
            instance.destinationHit = 0.0f;

            instances[index] = instance;
        }
    }*/

    [BurstCompile]
    private struct ClearActorHitsEx : IJobChunk
    {
        public ComponentTypeHandle<GameEntityActorHit> instanceType;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            /*ClearActorHits clearActorHits;
            clearActorHits.*/
            var instances = chunk.GetNativeArray(ref instanceType);

            if (useEnabledMask)
            {
                var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (iterator.NextEntityIndex(out int i))
                    //clearActorHits.Execute(i);
                    instances[i] = default;
            }
            else
                instances.MemClear();
        }
    }

    private EntityQuery __group;

    private ComponentTypeHandle<GameEntityActorHit> __instanceType;

    public void OnCreate(ref SystemState state)
    {
        __group = state.GetEntityQuery(ComponentType.ReadWrite<GameEntityActorHit>());

        __instanceType = state.GetComponentTypeHandle<GameEntityActorHit>();
    }

    public void OnDestroy(ref SystemState state)
    {

    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        ClearActorHitsEx clearActorHits;
        clearActorHits.instanceType = __instanceType.UpdateAsRef(ref state);
        state.Dependency = clearActorHits.ScheduleParallelByRef(__group, state.Dependency);
    }
}

#if DEBUG && !UNITY_STANDALONE_LINUX
//[UpdateInGroup(typeof(FixedStepSimulationSystemGroup)), UpdateAfter(typeof(EndFramePhysicsSystem))]
public partial class GameEntityActionDebugSystem : SystemBase
{
    protected override void OnUpdate()
    {
        Entities.ForEach((
            Entity entity,
            in GameActionDataEx instance,
            in Translation translation,
            in Rotation rotation) =>
        {
            RigidBody rigidbody;
            rigidbody.CustomTags = 0;
            rigidbody.Entity = entity;
            rigidbody.Collider = instance.collider;
            rigidbody.WorldFromBody = math.RigidTransform(rotation.Value, translation.Value);
            PhysicsColliderDrawer.instance.Draw(false, UnityEngine.Color.red, rigidbody);
        })
            .WithoutBurst()
            .Run();
    }

    private unsafe void __Draw(
        Entity entity,
        ref GameActionDataEx instance,
        ref Translation translation,
        ref Rotation rotation)
    {
        RigidBody rigidbody;
        rigidbody.CustomTags = 0;
        rigidbody.Entity = entity;
        rigidbody.Collider = instance.collider;
        rigidbody.WorldFromBody = math.RigidTransform(rotation.Value, translation.Value);
        PhysicsColliderDrawer.instance.Draw(false, UnityEngine.Color.red, rigidbody);
    }
}
#endif