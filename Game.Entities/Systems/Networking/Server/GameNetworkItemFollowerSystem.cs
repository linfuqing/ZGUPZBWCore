using System;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Entities;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Jobs;
using Unity.Networking.Transport;
using ZG;

public struct GameServerItemFollowerData : IComponentData, IEnableableComponent
{
    public int addChannel;
    public int removeChannel;
    public int setChannel;
    public uint addHandle;
    public uint removeHandle;
    public uint setHandle;
}

public struct GameServerItemFollower : IBufferElementData
{
    public Entity entity;
    public int handle;
}

[AutoCreateIn("Server"), 
 BurstCompile, 
 CreateAfter(typeof(GameItemOwnSystem)), 
 UpdateBefore(typeof(NetworkRPCSystem))]
public partial struct GameItemServerFollowerSystem : ISystem
{
    public struct Result
    {
        public Entity entity;
        public int handle;
        public uint id;
    }

    private struct Append
    {
        
        [ReadOnly] 
        public ComponentLookup<NetworkIdentity> identities;
        [ReadOnly] 
        public NativeArray<Entity> entityArray;
        [ReadOnly] 
        public BufferAccessor<GameFollower> followers;
        [ReadOnly] 
        public BufferAccessor<GameItemFollower> itemFollowers;
        [ReadOnly] 
        public BufferAccessor<GameServerItemFollower> targetItemFollowers;

        public NativeQueue<GameItemOwnSystem.Command>.ParallelWriter results;

    }
    
    private struct Collect
    {
        [ReadOnly] 
        public ComponentLookup<NetworkIdentity> identities;
        [ReadOnly] 
        public NativeArray<Entity> entityArray;
        [ReadOnly] 
        public BufferAccessor<GameFollower> followers;
        [ReadOnly] 
        public BufferAccessor<GameItemFollower> itemFollowers;
        
        public BufferAccessor<GameServerItemFollower> targetItemFollowers;

        public NativeQueue<Result>.ParallelWriter results;

        public void Execute(int index)
        {
            Result result;
            result.entity = entityArray[index];

            var followers = index < this.followers.Length ? this.followers[index] : default;
            var itemFollowers = this.itemFollowers[index];
            var targetItemFollowers = this.targetItemFollowers[index];
            NetworkIdentity identity;
            GameItemHandle handle;
            GameItemFollower itemFollower;
            GameServerItemFollower targetItemFollower;
            int numTargetItemFollowers = targetItemFollowers.Length, 
                numItemFollowers = itemFollowers.Length, 
                numFollowers = followers.IsCreated ? followers.Length : 0, 
                i, j;
            for (i = 0; i < numFollowers; ++i)
            {
                targetItemFollower.entity = followers[i].entity;

                if(!identities.TryGetComponent(targetItemFollower.entity, out identity))
                    continue;

                handle = GameItemHandle.Empty;
                for (j = 0; j < numItemFollowers; ++j)
                {
                    itemFollower = itemFollowers[j];
                    if (itemFollower.entity == targetItemFollower.entity)
                    {
                        handle = itemFollower.handle;
                        
                        break;
                    }
                }
                
                if(j == numItemFollowers)
                    continue;

                for (j = 0; j < numTargetItemFollowers; ++j)
                {
                    if (targetItemFollowers[j].entity == targetItemFollower.entity)
                        break;
                }

                if (j < numItemFollowers)
                    continue;

                result.handle = handle.index;
                result.id = identity.id;
                results.Enqueue(result);

                targetItemFollower.handle = handle.index;

                targetItemFollowers.Add(targetItemFollower);
            }

            for (i = 0; i < numTargetItemFollowers; ++i)
            {
                targetItemFollower = targetItemFollowers[i];
                for (j = 0; j < numFollowers; ++j)
                {
                    if (followers[j].entity == targetItemFollower.entity)
                        break;
                }

                if (j < numFollowers)
                    continue;
                
                result.handle = targetItemFollower.handle;
                result.id = 0;

                results.Enqueue(result);

                itemFollowers.RemoveAtSwapBack(i--);

                --numItemFollowers;
            }
        }
    }

    [BurstCompile]
    private struct CollectEx : IJobChunk
    {
        [ReadOnly] 
        public ComponentLookup<NetworkIdentity> identities;
        [ReadOnly] 
        public EntityTypeHandle entityType;
        [ReadOnly] 
        public BufferTypeHandle<GameFollower> followerType;
        [ReadOnly] 
        public BufferTypeHandle<GameItemFollower> itemFollowerType;

        public BufferTypeHandle<GameServerItemFollower> targetItemFollowerType;

        public NativeQueue<Result>.ParallelWriter results;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask,
            in v128 chunkEnabledMask)
        {
            Collect collect;
            collect.identities = identities;
            collect.entityArray = chunk.GetNativeArray(entityType);
            collect.followers = chunk.GetBufferAccessor(ref followerType);
            collect.itemFollowers = chunk.GetBufferAccessor(ref itemFollowerType);
            collect.targetItemFollowers = chunk.GetBufferAccessor(ref targetItemFollowerType);
            collect.results = results;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                collect.Execute(i);
        }
    }

    [BurstCompile]
    private struct Apply : IJob
    {
        public StreamCompressionModel model;
        
        [ReadOnly] 
        public NativeArray<NetworkServerEntityChannel> channels;

        [ReadOnly]
        public SharedList<GameItemOwnSystem.Command>.Reader commands;

        [ReadOnly] 
        public ComponentLookup<NetworkIdentity> identities;

        [ReadOnly] 
        public ComponentLookup<GameItemObjectData> itemObjects;

        [ReadOnly] 
        public ComponentLookup<GameServerItemFollowerData> instances;

        public NativeQueue<Result> results;

        public NetworkDriver driver;
        public NetworkRPCCommander rpcCommander;

        public void Execute()
        {
            int value;
            DataStreamWriter stream;
            NetworkIdentity identity;
            GameItemObjectData itemObject;
            GameServerItemFollowerData instance;
            foreach (var command in commands.AsArray())
            {
                if (!instances.TryGetComponent(command.source, out instance) ||
                    !identities.TryGetComponent(command.source, out identity))
                    continue;

                if (command.isAddOrRemove)
                {
                    if(!itemObjects.TryGetComponent(command.destination, out itemObject))
                        continue;
                    
                    if (!rpcCommander.BeginCommand(identity.id, channels[instance.addChannel].pipeline,
                            driver, out stream))
                        continue;

                    stream.WritePackedUInt(instance.addHandle, model);
                    stream.WritePackedUInt((uint)command.handle.index, model);
                    stream.WritePackedUInt((uint)itemObject.type, model);
                }
                else
                {
                    if (!rpcCommander.BeginCommand(identity.id, channels[instance.removeChannel].pipeline,
                            driver, out stream))
                        continue;

                    stream.WritePackedUInt(instance.removeHandle, model);
                    stream.WritePackedUInt((uint)command.handle.index, model);
                }

                value = rpcCommander.EndCommandRPC(
                    (int)NetworkRPCType.SendSelfOnly,
                    stream,
                    default,
                    default);
                if (value < 0)
                    UnityEngine.Debug.LogError($"[EndRPC]{(Unity.Networking.Transport.Error.StatusCode)value}");
            }

            while (results.TryDequeue(out var result))
            {
                if (!instances.TryGetComponent(result.entity, out instance) ||
                    !identities.TryGetComponent(result.entity, out identity))
                    continue;

                if (!rpcCommander.BeginCommand(identity.id, channels[instance.setChannel].pipeline,
                        driver, out stream))
                    continue;
                
                stream.WritePackedUInt(instance.setHandle, model);
                stream.WritePackedUInt((uint)result.handle, model);
                stream.WritePackedUInt(result.id, model);
                
                value = rpcCommander.EndCommandRPC(
                    (int)NetworkRPCType.SendSelfOnly,
                    stream,
                    default,
                    default);
                if (value < 0)
                    UnityEngine.Debug.LogError($"[EndRPC]{(Unity.Networking.Transport.Error.StatusCode)value}");
            }
        }
    }

    private EntityQuery __group;
    private EntityQuery __managerGroup;
    private EntityQuery __controllerGroup;

    private ComponentLookup<GameServerItemFollowerData> __instances;

    private ComponentLookup<GameItemObjectData> __itemObjects;
    
    private ComponentLookup<NetworkIdentity> __identities;
    private EntityTypeHandle __entityType;
    private BufferTypeHandle<GameFollower> __followerType;

    private BufferTypeHandle<GameItemFollower> __itemFollowerType;

    private BufferTypeHandle<GameServerItemFollower> __targetItemFollowerType;

    private SharedList<GameItemOwnSystem.Command> __commands;
    private NativeQueue<Result> __results;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __group = builder
                .WithAll<GameServerItemFollowerData, GameItemFollower, GameFollower, NetworkIdentity>()
                .WithAllRW<GameServerItemFollower>()
                .WithOptions(EntityQueryOptions.IncludeDisabledEntities)
                .Build(ref state);
        
        __group.SetChangedVersionFilter(ComponentType.ReadOnly<GameFollower>());
        
        __managerGroup = NetworkServerManager.GetEntityQuery(ref state);
        __controllerGroup = NetworkRPCController.GetEntityQuery(ref state);

        __instances = state.GetComponentLookup<GameServerItemFollowerData>(true);
        __itemObjects = state.GetComponentLookup<GameItemObjectData>(true);
        __identities= state.GetComponentLookup<NetworkIdentity>(true);
        __entityType = state.GetEntityTypeHandle();
        __followerType = state.GetBufferTypeHandle<GameFollower>(true);
        __itemFollowerType = state.GetBufferTypeHandle<GameItemFollower>(true);
        __targetItemFollowerType = state.GetBufferTypeHandle<GameServerItemFollower>();

        __commands = state.WorldUnmanaged.GetExistingSystemUnmanaged<GameItemOwnSystem>().commands;

        __results = new NativeQueue<Result>(Allocator.Persistent);
    }

    //[BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        __results.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var identities = __identities.UpdateAsRef(ref state);
        
        CollectEx collect;
        collect.identities = identities;
        collect.entityType = __entityType.UpdateAsRef(ref state);
        collect.followerType = __followerType.UpdateAsRef(ref state);
        collect.itemFollowerType = __itemFollowerType.UpdateAsRef(ref state);
        collect.targetItemFollowerType = __targetItemFollowerType.UpdateAsRef(ref state);
        collect.results = __results.AsParallelWriter();

        var jobHandle = collect.ScheduleParallelByRef(__group, state.Dependency);
        
        var manager = __managerGroup.GetSingleton<NetworkServerManager>();
        var controller = __controllerGroup.GetSingleton<NetworkRPCController>();

        Apply apply;
        apply.model = StreamCompressionModel.Default;
        apply.channels = SystemAPI.GetSingletonBuffer<NetworkServerEntityChannel>(true).AsNativeArray();
        apply.commands = __commands.reader;
        apply.identities = identities;
        apply.itemObjects = __itemObjects.UpdateAsRef(ref state);
        apply.instances = __instances.UpdateAsRef(ref state);
        apply.results = __results;
        apply.driver = manager.server.driver;
        apply.rpcCommander = controller.commander;

        ref var commandsJobManager = ref __commands.lookupJobManager;
        ref var managerJobManager = ref manager.lookupJobManager;
        ref var controllerJobManager = ref controller.lookupJobManager;

        jobHandle = JobHandle.CombineDependencies(jobHandle, 
            commandsJobManager.readOnlyJobHandle);
        jobHandle = JobHandle.CombineDependencies(jobHandle, 
            managerJobManager.readWriteJobHandle,
            controllerJobManager.readWriteJobHandle);
        jobHandle = apply.ScheduleByRef(jobHandle);

        commandsJobManager.AddReadOnlyDependency(jobHandle);
        managerJobManager.readWriteJobHandle = jobHandle;
        controllerJobManager.readWriteJobHandle = jobHandle;

        state.Dependency = jobHandle;
    }
}
