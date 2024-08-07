﻿using System;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using ZG;

[EntityDataTypeName("GameOwer")]
public struct GameOwner : IComponentData
{
    public Entity entity;

    /*public void Serialize(int entityIndex, ref EntityDataWriter writer)
    {
        writer.Write(entityIndex);
    }

    public int Deserialize(ref EntityDataReader reader)
    {
        return reader.Read<int>();
    }

    Entity IGameDataEntityCompoent.entity
    {
        get => entity;

        set => entity = value;
    }*/
}

public struct GameFollower : IBufferElementData, IEnableableComponent
{
    public Entity entity;
}

[BurstCompile, AutoCreateIn("Server"), UpdateInGroup(typeof(FrameSyncSystemGroup), OrderFirst = true)]
public partial struct GameOwnerSystem : ISystem
{
    private struct UpdateStates
    {
        [ReadOnly]
        public NativeArray<GameNodeStatus> states;

        public BufferAccessor<GameFollower> followers;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<GameOwner> owners;

        public void Execute(int index)
        {
            if ((states[index].value & GameNodeStatus.OVER) != GameNodeStatus.OVER)
                return;

            GameOwner owner;
            owner.entity = Entity.Null;

            var followers = this.followers[index];
            foreach (var follower in followers)
            {
                if (!owners.HasComponent(follower.entity))
                    continue;

                owners[follower.entity] = owner;
            }
            followers.Clear();
        }
    }

    [BurstCompile]
    private struct UpdateStateEx : IJobChunk
    {
        [ReadOnly]
        public ComponentTypeHandle<GameNodeStatus> statusType;

        //[ReadOnly]
        public BufferTypeHandle<GameFollower> followerType;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<GameOwner> owners;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            UpdateStates updateStates;
            updateStates.states = chunk.GetNativeArray(ref statusType);
            updateStates.followers = chunk.GetBufferAccessor(ref followerType);
            updateStates.owners = owners;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
            {
                updateStates.Execute(i);

                chunk.SetComponentEnabled(ref followerType, i, false);
            }
        }
    }

    private struct DidChange
    {
        [ReadOnly]
        public SharedHashMap<Entity, Entity>.Reader origins;
        [ReadOnly]
        public BufferLookup<GameFollower> followers;
        [ReadOnly]
        public NativeArray<Entity> entityArray;
        [ReadOnly]
        public NativeArray<GameNodeStatus> states;
        [ReadOnly]
        public NativeArray<GameOwner> owners;

        public NativeList<Entity>.ParallelWriter results;

        public void Execute(int index)
        {
            Entity entity = entityArray[index], owner = owners[index].entity;
            if (!followers.HasBuffer(owner) || ((GameEntityStatus)states[index].value & GameEntityStatus.Mask) == GameEntityStatus.Dead)
                owner = Entity.Null;

            if (origins.TryGetValue(entity, out Entity origin) ? origin == owner : owner == Entity.Null)
                return;

            results.AddNoResize(entity);
        }
    }

    [BurstCompile]
    private struct DidChangeEx : IJobChunk
    {
        [ReadOnly]
        public SharedHashMap<Entity, Entity>.Reader origins;

        [ReadOnly]
        public BufferLookup<GameFollower> followers;

        [ReadOnly]
        public EntityTypeHandle entityType;

        [ReadOnly]
        public ComponentTypeHandle<GameOwner> ownerType;

        [ReadOnly]
        public ComponentTypeHandle<GameNodeStatus> statusType;

        public NativeList<Entity>.ParallelWriter results;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            DidChange didChange;
            didChange.origins = origins;
            didChange.followers = followers;
            didChange.entityArray = chunk.GetNativeArray(entityType);
            didChange.owners = chunk.GetNativeArray(ref ownerType);
            didChange.states = chunk.GetNativeArray(ref statusType);
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
        public ComponentLookup<GameEntityCampDefault> camps;

        public ComponentLookup<GameEntityCamp> entityCamps;

        public ComponentLookup<GameNodeStatus> states;

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

            if (followers.HasBuffer(entity))
            {
                var followers = this.followers[entity];
                int numFollowers = followers.Length;
                for (int i = 0; i < numFollowers; ++i)
                    UpdateCamp(followers[i].entity, value);
            }
        }

        public void UpdateStatus(in Entity entity, in GameNodeStatus value)
        {
            if (followers.HasBuffer(entity))
            {
                var followers = this.followers[entity];
                Entity follower;
                int numFollowers = followers.Length;
                for (int i = 0; i < numFollowers; ++i)
                {
                    follower = followers[i].entity;
                    if (states.HasComponent(follower))
                        states[follower] = value;
                    
                    UpdateStatus(follower, value);
                }
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

                            if(--numFollowers < 1)
                                this.followers.SetBufferEnabled(origin, false);

                            break;
                        }
                    }
                }
            }

            var status = states[entity];
            bool isDead = states.HasComponent(entity) &&
                          ((GameEntityStatus)status.value & GameEntityStatus.Mask) == GameEntityStatus.Dead;
            Entity owner = owners[entity].entity;
            if (owner != Entity.Null && isDead)
            {
                owner = Entity.Null;

                UpdateStatus(entity, status);
            }
            
            GameEntityCamp camp;
            if (followers.HasBuffer(owner))
            {
                //UnityEngine.Debug.LogError($"{owner} Own {entity}");

                this.followers.SetBufferEnabled(owner, true);

                var followers = this.followers[owner];
                GameFollower follower;
                follower.entity = entity;
                followers.Add(follower);

                origins[entity] = owner;

                if (!entityCamps.HasComponent(owner))
                    return;

                camp.value = entityCamps[owner].value;
            }
            else
            {
                //UnityEngine.Debug.LogError($"{entity} Own Self");

                origins.Remove(entity);

                camp.value = camps.HasComponent(entity) ? camps[entity].value : 0;
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

    private struct UpdateCamps
    {
        [ReadOnly]
        public NativeArray<Entity> entityArray;

        [ReadOnly]
        public NativeArray<GameEntityCamp> camps;

        [ReadOnly]
        public BufferLookup<GameFollower> followers;

        public ComponentLookup<GameEntityCamp> entityCamps;

        public void Execute(in Entity entity, int value)
        {
            if (entityCamps.HasComponent(entity) && entityCamps[entity].value != value)
            {
                GameEntityCamp camp;
                camp.value = value;
                entityCamps[entity] = camp;
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

            var followers = this.followers[entity];
            int numFollowers = followers.Length, camp = camps[index].value;
            for (int i = 0; i < numFollowers; ++i)
                Execute(followers[i].entity, camp);
        }
    }

    [BurstCompile]
    private struct UpdateCampEx : IJobChunk
    {
        public uint lastSystemVersion;
        
        [ReadOnly]
        public EntityTypeHandle entityType;

        [ReadOnly]
        public ComponentTypeHandle<GameEntityCamp> entityCampType;

        [ReadOnly]
        public ComponentTypeHandle<GameEntityCampDefault> campType;

        [ReadOnly]
        public BufferTypeHandle<GameFollower> followerType;

        [ReadOnly, NativeDisableContainerSafetyRestriction]
        public BufferLookup<GameFollower> followers;

        [NativeDisableContainerSafetyRestriction]
        public ComponentLookup<GameEntityCamp> entityCamps;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            UpdateCamps updateCamps;
            if (chunk.DidChange(ref entityCampType, lastSystemVersion))
                updateCamps.camps = chunk.GetNativeArray(ref entityCampType);
            else if(chunk.DidChange(ref campType, lastSystemVersion))
                updateCamps.camps = chunk.GetNativeArray(ref campType).Reinterpret<GameEntityCamp>();
            else if (chunk.DidChange(ref followerType, lastSystemVersion))
                updateCamps.camps = chunk.GetNativeArray(ref entityCampType);
            else
                return;
            
            updateCamps.entityArray = chunk.GetNativeArray(entityType);
            updateCamps.followers = followers;
            updateCamps.entityCamps = entityCamps;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                updateCamps.Execute(i);
        }
    }

    private EntityQuery __groupToUpdate;

    private EntityQuery __groupToChange;

    private EntityQuery __groupToApply;

    private EntityTypeHandle __entityType;

    private ComponentTypeHandle<GameEntityCamp> __entityCampType;

    private ComponentTypeHandle<GameEntityCampDefault> __campType;

    private ComponentTypeHandle<GameNodeStatus> __statusType;
    //private ComponentTypeHandle<GameNodeOldStatus> __oldStatusType;

    private ComponentTypeHandle<GameOwner> __ownerType;

    private ComponentLookup<GameOwner> __owners;

    private ComponentLookup<GameNodeStatus> __states;

    private ComponentLookup<GameEntityCampDefault> __camps;

    private ComponentLookup<GameEntityCamp> __entityCamps;

    private BufferLookup<GameFollower> __followers;

    private BufferTypeHandle<GameFollower> __followerType;

    private NativeList<Entity> __entities;

    public SharedHashMap<Entity, Entity> origins
    {
        get;

        private set;
    }

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __groupToUpdate = builder
                    .WithAllRW<GameFollower>()
                    .WithNone<GameSoul>()
                    .BuildStatusSystemGroup(ref state);

        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __groupToChange = builder
                    .WithAll<GameOwner, GameNodeStatus>()
                    .WithOptions(EntityQueryOptions.IncludeDisabledEntities)
                    .Build(ref state);
        __groupToChange.AddChangedVersionFilter(ComponentType.ReadOnly<GameOwner>());
        __groupToChange.AddChangedVersionFilter(ComponentType.ReadOnly<GameNodeStatus>());
        //__groupToChange.AddChangedVersionFilter(ComponentType.ReadOnly<GameNodeOldStatus>());

        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __groupToApply = builder
                    .WithAll<GameFollower>()
                    .WithAllRW<GameEntityCampDefault>()
                    .WithOptions(EntityQueryOptions.IncludeDisabledEntities)
                    .Build(ref state);
        //__groupToApply.AddChangedVersionFilter(ComponentType.ReadOnly<GameEntityCampDefault>());
        //__groupToApply.AddChangedVersionFilter(ComponentType.ReadOnly<GameFollower>());

        __entityType = state.GetEntityTypeHandle();
        __entityCampType = state.GetComponentTypeHandle<GameEntityCamp>(true);
        __campType = state.GetComponentTypeHandle<GameEntityCampDefault>(true);
        __statusType = state.GetComponentTypeHandle<GameNodeStatus>(true);
        //__oldStatusType = state.GetComponentTypeHandle<GameNodeOldStatus>(true);
        __ownerType = state.GetComponentTypeHandle<GameOwner>(true);
        __owners = state.GetComponentLookup<GameOwner>();
        __camps = state.GetComponentLookup<GameEntityCampDefault>();
        __entityCamps = state.GetComponentLookup<GameEntityCamp>();
        __states = state.GetComponentLookup<GameNodeStatus>();
        __followers = state.GetBufferLookup<GameFollower>();
        __followerType = state.GetBufferTypeHandle<GameFollower>();

        __entities = new NativeList<Entity>(Allocator.Persistent);

        origins = new SharedHashMap<Entity, Entity>(Allocator.Persistent);
    }

    //[BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        __entities.Dispose();
        origins.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var statusType = __statusType.UpdateAsRef(ref state);
        var owners = __owners.UpdateAsRef(ref state);
        var followerType = __followerType.UpdateAsRef(ref state);

        UpdateStateEx updateState;
        updateState.statusType = statusType;
        updateState.followerType = followerType;
        updateState.owners = owners;
        var jobHandle = updateState.ScheduleParallelByRef(__groupToUpdate, state.Dependency);

        __entities.Clear();
        __entities.Capacity = math.max(__entities.Capacity, __groupToChange.CalculateEntityCountWithoutFiltering());

        var origins = this.origins;

        ref var lookupJobManager = ref origins.lookupJobManager;

        lookupJobManager.CompleteReadWriteDependency();

        var entityType = __entityType.UpdateAsRef(ref state);
        var followers = __followers.UpdateAsRef(ref state);

        DidChangeEx didChange;
        didChange.followers = followers;
        didChange.entityType = entityType;
        didChange.ownerType = __ownerType.UpdateAsRef(ref state);
        didChange.statusType = statusType;
        didChange.origins = origins.reader;
        didChange.results = __entities.AsParallelWriter();
        jobHandle = didChange.ScheduleParallelByRef(__groupToChange, jobHandle);

        var entityCamps = __entityCamps.UpdateAsRef(ref state);
        var camps = __camps.UpdateAsRef(ref state);

        Own own;
        own.entityArray = __entities.AsDeferredJobArray();
        own.owners = owners;
        own.camps = camps;
        own.entityCamps = entityCamps;
        own.states = __states.UpdateAsRef(ref state);
        own.followers = followers;
        own.origins = origins.writer;
        jobHandle = own.ScheduleByRef(jobHandle);

        lookupJobManager.readWriteJobHandle = jobHandle;

        UpdateCampEx updateCamp;
        updateCamp.lastSystemVersion = state.LastSystemVersion;
        updateCamp.entityType = entityType;
        updateCamp.entityCampType = __entityCampType.UpdateAsRef(ref state);
        updateCamp.campType = __campType.UpdateAsRef(ref state);
        updateCamp.followerType = followerType;
        updateCamp.followers = followers;
        updateCamp.entityCamps = entityCamps;

        state.Dependency = updateCamp.ScheduleByRef(__groupToApply, jobHandle);
    }
}