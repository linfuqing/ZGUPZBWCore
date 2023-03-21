using System;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using ZG;

[Serializable]
public struct GameCampDefault : IComponentData
{
    public int value;
}

[Serializable]
[EntityDataTypeName("GameOwer")]
public struct GameOwner : IGameDataEntityCompoentData
{
    public Entity entity;

    Entity IGameDataEntityCompoentData.entity
    {
        get => entity;

        set => entity = value;
    }
}

[Serializable]
public struct GameFollower : IBufferElementData
{
    public Entity entity;
}

[BurstCompile, AutoCreateIn("Server"), UpdateInGroup(typeof(GameStatusSystemGroup), OrderFirst = true)]
public partial struct GameCampInitSystem : ISystem
{
    [BurstCompile]
    private struct MoveTo : IJobParallelFor
    {
        [ReadOnly, DeallocateOnJobCompletion]
        public NativeArray<Entity> entityArray;

        [ReadOnly, DeallocateOnJobCompletion]
        public NativeArray<GameCampDefault> inputs;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<GameCampDefault> outputs;

        public void Execute(int index)
        {
            Entity entity = entityArray[index];
            if (!outputs.HasComponent(entity))
                return;

            outputs[entity] = inputs[index];
        }
    }

    public static readonly int InnerloopBatchCount = 1;

    private EntityQuery __group;

    public void OnCreate(ref SystemState state)
    {
        BurstUtility.InitializeJobParallelFor<MoveTo>();

        __group = state.GetEntityQuery(
            new EntityQueryDesc()
            {
                All = new ComponentType[]
                {
                    ComponentType.ReadOnly<GameEntityCamp>()
                }, 
                None = new ComponentType[]
                {
                    typeof(GameCampDefault)
                },

                Options = EntityQueryOptions.IncludeDisabledEntities
            });
    }

    public void OnDestroy(ref SystemState state)
    {

    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        //TODO: 
        state.CompleteDependency();

        var entityArray = __group.ToEntityArrayBurstCompatible(state.GetEntityTypeHandle(), Allocator.TempJob);
        var camps = __group.ToComponentDataArrayBurstCompatible<GameEntityCamp>(state.GetDynamicComponentTypeHandle(ComponentType.ReadOnly<GameEntityCamp>()), Allocator.TempJob);

        state.EntityManager.AddComponent<GameCampDefault>(__group);

        MoveTo moveTo;
        moveTo.entityArray = entityArray;
        moveTo.inputs = camps.Reinterpret<GameCampDefault>();
        moveTo.outputs = state.GetComponentLookup<GameCampDefault>();

        state.Dependency = moveTo.Schedule(entityArray.Length, InnerloopBatchCount, state.Dependency);
    }
}

[BurstCompile, AutoCreateIn("Server"), UpdateInGroup(typeof(GameStatusSystemGroup)), UpdateBefore(typeof(GameOwnerSystem))/*, UpdateAfter(typeof(GameCampInitSystem))*/]
public partial struct GameOwnerStatusSystem : ISystem
{
    private struct UpdateStates
    {
        [ReadOnly]
        public NativeArray<Entity> entityArray;
        [ReadOnly]
        public NativeArray<GameNodeStatus> states;
        [ReadOnly]
        public NativeArray<GameNodeOldStatus> oldStates;
        [ReadOnly]
        public NativeArray<GameOwner> owners;

        [NativeDisableContainerSafetyRestriction]
        public ComponentLookup<GameOwner> ownerMap;

        public void Execute(int index)
        {
            int status = states[index].value;
            if ((status & (int)GameEntityStatus.Mask) != (int)GameEntityStatus.Dead)
                return;

            int oldStatus = oldStates[index].value;
            if (status == oldStatus)
                return;

            if (owners[index].entity == Entity.Null)
                return;

            GameOwner owner;
            owner.entity = Entity.Null;
            ownerMap[entityArray[index]] = owner;
        }
    }

    [BurstCompile]
    private struct UpdateStateEx : IJobChunk
    {
        [ReadOnly]
        public EntityTypeHandle entityType;
        [ReadOnly]
        public ComponentTypeHandle<GameNodeStatus> statusType;
        [ReadOnly]
        public ComponentTypeHandle<GameNodeOldStatus> oldStatusType;
        [ReadOnly]
        public ComponentTypeHandle<GameOwner> ownerType;

        [NativeDisableContainerSafetyRestriction]
        public ComponentLookup<GameOwner> owners;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            UpdateStates updateStates;
            updateStates.entityArray = chunk.GetNativeArray(entityType);
            updateStates.states = chunk.GetNativeArray(ref statusType);
            updateStates.oldStates = chunk.GetNativeArray(ref oldStatusType);
            updateStates.owners = chunk.GetNativeArray(ref ownerType);
            updateStates.ownerMap = owners;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                updateStates.Execute(i);
        }
    }

    private EntityQuery __group;

    public void OnCreate(ref SystemState state)
    {
        __group = state.GetEntityQuery(
            new EntityQueryDesc()
            {
                All = new ComponentType[]
                {
                    ComponentType.ReadOnly<GameNodeStatus>(),
                    ComponentType.ReadOnly<GameNodeOldStatus>(),
                    ComponentType.ReadOnly<GameOwner>(),
                },
                Options = EntityQueryOptions.IncludeDisabledEntities
            });
        __group.SetChangedVersionFilter(new ComponentType[] { typeof(GameNodeStatus), typeof(GameNodeOldStatus) });
    }

    public void OnDestroy(ref SystemState state)
    {

    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        UpdateStateEx updateState;
        updateState.entityType = state.GetEntityTypeHandle();
        updateState.statusType = state.GetComponentTypeHandle<GameNodeStatus>(true);
        updateState.oldStatusType = state.GetComponentTypeHandle<GameNodeOldStatus>(true);
        updateState.ownerType = state.GetComponentTypeHandle<GameOwner>(true);
        updateState.owners = state.GetComponentLookup<GameOwner>();
        state.Dependency = updateState.ScheduleParallel(__group, state.Dependency);
    }
}

[BurstCompile, AutoCreateIn("Server"), UpdateInGroup(typeof(GameStatusSystemGroup))]
public partial struct GameOwnerSystem : ISystem
{
    private struct DidChange
    {
        [ReadOnly]
        public NativeArray<Entity> entityArray;
        [ReadOnly]
        public NativeArray<GameOwner> owners;
        [ReadOnly]
        public SharedHashMap<Entity, Entity>.Reader origins;

        public NativeList<Entity>.ParallelWriter results;

        public void Execute(int index)
        {
            Entity entity = entityArray[index], owner = owners[index].entity;
            if (origins.TryGetValue(entity, out Entity origin) ? origin == owner : entity == Entity.Null)
                return;

            results.AddNoResizeEx(entity);
        }
    }

    [BurstCompile]
    private struct DidChangeEx : IJobChunk
    {
        [ReadOnly]
        public EntityTypeHandle entityType;

        [ReadOnly]
        public ComponentTypeHandle<GameOwner> ownerType;
        [ReadOnly]
        public SharedHashMap<Entity, Entity>.Reader origins;

        public NativeList<Entity>.ParallelWriter results;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            DidChange didChange;
            didChange.entityArray = chunk.GetNativeArray(entityType);
            didChange.owners = chunk.GetNativeArray(ref ownerType);
            didChange.origins = origins;
            didChange.results = results;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                didChange.Execute(i);
        }
    }

    [BurstCompile]
    private struct Own : IJob
    {
        [ReadOnly]
        public NativeArray<Entity> entityArray;

        [ReadOnly]
        public ComponentLookup<GameOwner> owners;

        [ReadOnly]
        public ComponentLookup<GameCampDefault> camps;

        public ComponentLookup<GameEntityCamp> entityCamps;

        public BufferLookup<GameFollower> followers;

        public SharedHashMap<Entity, Entity>.Writer origins;

        public void UpdateCamp(in Entity entity, in GameEntityCamp value)
        {
            if (entityCamps.HasComponent(entity))
            {
                if (entityCamps[entity].value == value.value)
                    return;

                entityCamps[entity] = value;
            }

            if(followers.HasBuffer(entity))
            {
                var followers = this.followers[entity];
                int numFollowers = followers.Length;
                for(int i = 0; i < numFollowers; ++i)
                    UpdateCamp(followers[i].entity, value);
            }
        }

        public void Execute(int index)
        {
            Entity entity = entityArray[index];
            if (origins.TryGetValue(entity, out Entity origin))
            {
                if (this.followers.HasBuffer(origin))
                {
                    var followers = this.followers[origin];
                    GameFollower follower;
                    int numFollowers = followers.Length;
                    for (int i = 0; i < numFollowers; ++i)
                    {
                        follower = followers[i];
                        if (follower.entity == entity)
                        {
                            followers.RemoveAt(i);

                            break;
                        }
                    }
                }
            }

            GameEntityCamp camp;
            var owner = owners[entity].entity;
            if (owner == Entity.Null)
            {
                origins.Remove(owner);

                camp.value = camps.HasComponent(entity) ? camps[entity].value : 0;
            }
            else
            {
                if (this.followers.HasBuffer(owner))
                {
                    var followers = this.followers[owner];
                    GameFollower follower;
                    follower.entity = entity;
                    followers.Add(follower);
                }

                origins[entity] = owner;

                if (!entityCamps.HasComponent(owner))
                    return;

                camp.value = entityCamps[owner].value;
            }

            UpdateCamp(entity, camp);
        }

        public void Execute()
        {
            int length = entityArray.Length;
            for (int i = 0; i < length; ++i)
                Execute(i);
        }
    }

    private EntityQuery __group;
    private NativeListLite<Entity> __entities;

    public SharedHashMap<Entity, Entity> origins
    {
        get;

        private set;
    }

    public void OnCreate(ref SystemState state)
    {
        BurstUtility.InitializeJob<Own>();

        __group = state.GetEntityQuery(
            new EntityQueryDesc()
            {
                All = new ComponentType[]
                {
                    ComponentType.ReadOnly<GameOwner>()
                },
                Options = EntityQueryOptions.IncludeDisabledEntities
            });
        __group.SetChangedVersionFilter(typeof(GameOwner));

        __entities = new NativeListLite<Entity>(Allocator.Persistent);

        origins = new SharedHashMap<Entity, Entity>(Allocator.Persistent);
    }

    public void OnDestroy(ref SystemState state)
    {
        __entities.Dispose();
        origins.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        NativeList<Entity> entities = __entities;

        entities.Clear();
        entities.Capacity = math.max(__entities.Capacity, __group.CalculateEntityCountWithoutFiltering());

        var origins = this.origins;

        ref var lookupJobManager = ref origins.lookupJobManager;

        lookupJobManager.CompleteReadWriteDependency();

        DidChangeEx didChange;
        didChange.entityType = state.GetEntityTypeHandle();
        didChange.ownerType = state.GetComponentTypeHandle<GameOwner>(true);
        didChange.origins = origins.reader;
        didChange.results = entities.AsParallelWriter();
        var jobHandle = didChange.ScheduleParallel(__group, state.Dependency);

        Own own;
        own.entityArray = entities.AsDeferredJobArrayEx();
        own.owners = state.GetComponentLookup<GameOwner>(true);
        own.camps = state.GetComponentLookup<GameCampDefault>(true);
        own.entityCamps = state.GetComponentLookup<GameEntityCamp>();
        own.followers = state.GetBufferLookup<GameFollower>();
        own.origins = origins.writer;
        jobHandle = own.Schedule(jobHandle);

        lookupJobManager.readWriteJobHandle = jobHandle;

        state.Dependency = jobHandle;
    }
}

[BurstCompile, AutoCreateIn("Server"), UpdateInGroup(typeof(GameStatusSystemGroup)), /*UpdateBefore(typeof(GameNodeStatusSystem)), */UpdateAfter(typeof(GameOwnerSystem))]
public partial struct GameOwnerCampSystem : ISystem
{
    private struct UpdateCamps
    {
        [ReadOnly]
        public NativeArray<Entity> entityArray;

        [ReadOnly]
        public BufferLookup<GameFollower> followers;

        public ComponentLookup<GameEntityCamp> camps;

        public void Execute(in Entity entity, in GameEntityCamp value)
        {
            if (camps.HasComponent(entity))
            {
                if (camps[entity].value == value.value)
                    return;

                camps[entity] = value;
            }

            if (this.followers.HasBuffer(entity))
            {
                var followers = this.followers[entity];
                int numFollowers = followers.Length;
                for (int i = 0; i < numFollowers; ++i)
                    Execute(followers[i].entity, value);
            }
        }

        public void Execute(int index)
        {
            Entity entity = entityArray[index];
            if (!this.followers.HasBuffer(entity))
                return;

            var camp = camps[entity];

            var followers = this.followers[entity];
            int numFollowers = followers.Length;
            for (int i = 0; i < numFollowers; ++i)
                Execute(followers[i].entity, camp);
        }

        public void Execute()
        {
            int length = entityArray.Length;
            for (int i = 0; i < length; ++i)
                Execute(i);
        }
    }

    [BurstCompile]
    private struct UpdateCampEx : IJobChunk
    {
        [ReadOnly]
        public EntityTypeHandle entityType;

        [ReadOnly]
        public BufferLookup<GameFollower> followers;

        public ComponentLookup<GameEntityCamp> camps;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            UpdateCamps updateCamps;
            updateCamps.entityArray = chunk.GetNativeArray(entityType);
            updateCamps.followers = followers;
            updateCamps.camps = camps;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                updateCamps.Execute(i);
        }
    }

    private EntityQuery __group;

    public void OnCreate(ref SystemState state)
    {
        __group = state.GetEntityQuery(
            new EntityQueryDesc()
            {
                All = new ComponentType[]
                {
                    ComponentType.ReadOnly<GameEntityCamp>()
                },
                Options = EntityQueryOptions.IncludeDisabledEntities
            });
        __group.SetChangedVersionFilter(typeof(GameEntityCamp));
    }

    public void OnDestroy(ref SystemState state)
    {

    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        /*UpdateCamps updateCamps;
        updateCamps.entityArray = __group.ToEntityArrayAsync(Allocator.TempJob, out JobHandle jobHandle);
        updateCamps.followers = GetBufferLookup<GameFollower>(true);
        updateCamps.camps = GetComponentLookup<GameEntityCamp>();
        Dependency = updateCamps.Schedule(JobHandle.CombineDependencies(jobHandle, Dependency));*/

        UpdateCampEx updateCamp;
        updateCamp.entityType = state.GetEntityTypeHandle();
        updateCamp.followers = state.GetBufferLookup<GameFollower>(true);
        updateCamp.camps = state.GetComponentLookup<GameEntityCamp>();

        state.Dependency = updateCamp.Schedule(__group, state.Dependency);
    }
}