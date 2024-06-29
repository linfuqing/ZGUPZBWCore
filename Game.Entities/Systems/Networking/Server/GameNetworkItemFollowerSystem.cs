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
    public int deleteChannel;
    public int setChannel;
    public uint addHandle;
    public uint removeHandle;
    public uint deleteHandle;
    public uint setHandle;
}

public struct GameServerItemFollower : IBufferElementData
{
    public Entity entity;
    public GameItemHandle handle;
    public uint id;
}

[AutoCreateIn("Server"), 
 BurstCompile, 
 CreateAfter(typeof(GameItemOwnSystem)), 
 //CreateAfter(typeof(GameItemResultSystem)), 
 UpdateBefore(typeof(NetworkRPCSystem))]
public partial struct GameItemServerFollowerSystem : ISystem
{
    private struct Result
    {
        public Entity entity;
        public GameItemHandle handle;
        public uint id;
    }

    private struct Append
    {
        [ReadOnly]
        public SharedList<GameItemOwnSystem.Command>.Reader origins;
        [ReadOnly] 
        public NativeArray<Entity> entityArray;
        [ReadOnly] 
        public BufferAccessor<GameItemFollower> itemFollowers;
        [ReadOnly] 
        public BufferAccessor<GameServerItemFollower> targetItemFollowers;

        public NativeQueue<GameItemOwnSystem.Command>.ParallelWriter commands;

        public NativeQueue<Result>.ParallelWriter results;

        public void Execute(int index)
        {
            GameItemOwnSystem.Command command;
            command.type = GameItemOwnSystem.CommandType.Add;
            command.source = entityArray[index];

            bool isContains;
            var itemFollowers = this.itemFollowers[index];
            foreach (var itemFollower in itemFollowers)
            {
                isContains = false;
                foreach (var origin in origins.AsArray())
                {
                    if (origin.destination == itemFollower.entity && origin.source == command.source)
                    {
                        isContains = true;
                        break;
                    }
                }
                
                if(isContains)
                    continue;

                command.destination = itemFollower.entity;
                command.handle = itemFollower.handle;
                
                commands.Enqueue(command);
            }

            Result result;
            result.entity = command.source;
            
            var targetItemFollowers = this.targetItemFollowers[index];
            foreach (var targetItemFollower in targetItemFollowers)
            {
                result.handle = targetItemFollower.handle;
                result.id = targetItemFollower.id;
                
                results.Enqueue(result);
            }
        }
    }

    [BurstCompile]
    private struct AppendEx : IJobChunk
    {
        [ReadOnly]
        public SharedList<GameItemOwnSystem.Command>.Reader origins;
        [ReadOnly] 
        public EntityTypeHandle entityType;
        [ReadOnly] 
        public BufferTypeHandle<GameItemFollower> itemFollowerType;
        [ReadOnly]
        public BufferTypeHandle<GameServerItemFollower> targetItemFollowerType;

        public ComponentTypeHandle<GameServerItemFollowerData> instanceType;

        public NativeQueue<GameItemOwnSystem.Command>.ParallelWriter commands;

        public NativeQueue<Result>.ParallelWriter results;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask,
            in v128 chunkEnabledMask)
        {
            Append append;
            append.origins = origins;
            append.entityArray = chunk.GetNativeArray(entityType);
            append.itemFollowers = chunk.GetBufferAccessor(ref itemFollowerType);
            append.targetItemFollowers = chunk.GetBufferAccessor(ref targetItemFollowerType);
            append.commands = commands;
            append.results = results;
            
            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
            {
                append.Execute(i);
                
                chunk.SetComponentEnabled(ref instanceType, i, false);
            }
        }
    }
    
    private struct Collect
    {
        [ReadOnly] 
        public ComponentLookup<NetworkIdentity> identities;
        [ReadOnly] 
        public ComponentLookup<GameItemRoot> itemRoots;
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
            GameItemRoot root;
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
                
                if(!itemRoots.TryGetComponent(targetItemFollower.entity, out root))
                    continue;

                for (j = 0; j < numItemFollowers; ++j)
                {
                    itemFollower = itemFollowers[j];
                    if (itemFollower.handle.Equals(root.handle))
                        break;
                }
                
                if(j == numItemFollowers)
                    continue;

                for (j = 0; j < numTargetItemFollowers; ++j)
                {
                    if (targetItemFollowers[j].entity == targetItemFollower.entity)
                        break;
                }

                if (j < numTargetItemFollowers)
                    continue;

                result.handle = root.handle;
                result.id = identity.id;
                results.Enqueue(result);

                targetItemFollower.handle = root.handle;
                targetItemFollower.id = identity.id;

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

                targetItemFollowers.RemoveAtSwapBack(i--);

                --numTargetItemFollowers;
            }
        }
    }

    [BurstCompile]
    private struct CollectEx : IJobChunk
    {
        //[ReadOnly]
        //public SharedList<GameItemOwnSystem.Command>.Reader origins;

        [ReadOnly] 
        public ComponentLookup<NetworkIdentity> identities;
        [ReadOnly] 
        public ComponentLookup<GameItemRoot> itemRoots;
        [ReadOnly] 
        public EntityTypeHandle entityType;
        [ReadOnly] 
        public BufferTypeHandle<GameFollower> followerType;
        [ReadOnly] 
        public BufferTypeHandle<GameItemFollower> itemFollowerType;

        public BufferTypeHandle<GameServerItemFollower> targetItemFollowerType;

        //public ComponentTypeHandle<GameServerItemFollowerData> instanceType;
        
        //public NativeQueue<GameItemOwnSystem.Command>.ParallelWriter commands;

        public NativeQueue<Result>.ParallelWriter results;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask,
            in v128 chunkEnabledMask)
        {
            Collect collect;
            collect.identities = identities;
            collect.itemRoots = itemRoots;
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
        public SharedList<GameItemOwnSystem.Command>.Reader origins;

        [ReadOnly] 
        public ComponentLookup<NetworkIdentity> identities;

        [ReadOnly] 
        public ComponentLookup<GameItemType> itemTypes;

        [ReadOnly] 
        public ComponentLookup<GameItemObjectData> itemObjects;

        [ReadOnly] 
        public ComponentLookup<GameItemName> itemNames;

        [ReadOnly] 
        public ComponentLookup<GameServerItemFollowerData> instances;

        //public SharedList<GameSpawnData>.Writer spawnCommands;
        
        //public SharedList<GameValhallaCommand>.Writer valhallaCommands;

        public NativeQueue<GameItemOwnSystem.Command> commands;

        public NativeQueue<Result> results;

        public NetworkDriver driver;
        public NetworkRPCCommander rpcCommander;

        public void Execute()
        {
            foreach (var command in origins.AsArray())
                commands.Enqueue(command);
            
            int value;
            DataStreamWriter stream;
            NetworkIdentity identity;
            GameItemType itemType;
            GameItemObjectData itemObject;
            GameItemName itemName;
            GameServerItemFollowerData instance;
            while(commands.TryDequeue(out var command))
            {
                if (!instances.TryGetComponent(command.source, out instance) ||
                    !identities.TryGetComponent(command.source, out identity))
                    continue;

                switch (command.type)
                {
                    case GameItemOwnSystem.CommandType.Add:
                        if(!itemObjects.TryGetComponent(command.destination, out itemObject))
                            continue;

                        if(!itemTypes.TryGetComponent(command.destination, out itemType))
                            continue;
                    
                        if (!rpcCommander.BeginCommand(identity.id, channels[instance.addChannel].pipeline,
                                driver, out stream))
                            continue;

                        stream.WritePackedUInt(instance.addHandle, model);
                        stream.WritePackedUInt((uint)command.handle.index, model);
                        stream.WritePackedUInt((uint)itemType.value, model);
                        stream.WritePackedUInt((uint)itemObject.type, model);
                        break;
                    case GameItemOwnSystem.CommandType.Remove:
                        if (!rpcCommander.BeginCommand(identity.id, channels[instance.removeChannel].pipeline,
                                driver, out stream))
                            continue;

                        stream.WritePackedUInt(instance.removeHandle, model);
                        stream.WritePackedUInt((uint)command.handle.index, model);
                        break;
                    case GameItemOwnSystem.CommandType.Delete:
                        if(!itemObjects.TryGetComponent(command.destination, out itemObject))
                            continue;

                        if (!rpcCommander.BeginCommand(identity.id, channels[instance.deleteChannel].pipeline,
                                driver, out stream))
                            continue;

                        itemNames.TryGetComponent(command.destination, out itemName);
                        
                        stream.WritePackedUInt(instance.deleteHandle, model);
                        stream.WritePackedUInt((uint)itemObject.type, model);
                        stream.WriteFixedString32(itemName.value);

                        break;
                    default:
                        continue;
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
                stream.WritePackedUInt((uint)result.handle.index, model);
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

    private EntityQuery __groupToAppend;
    private EntityQuery __groupToCollect;
    private EntityQuery __managerGroup;
    private EntityQuery __controllerGroup;

    private ComponentLookup<GameServerItemFollowerData> __instances;

    private ComponentLookup<GameItemType> __itemTypes;

    private ComponentLookup<GameItemObjectData> __itemObjects;

    private ComponentLookup<GameItemName> __itemNames;

    private ComponentLookup<GameItemRoot> __itemRoots;

    private ComponentLookup<NetworkIdentity> __identities;
    private EntityTypeHandle __entityType;
    private BufferTypeHandle<GameFollower> __followerType;

    private BufferTypeHandle<GameItemFollower> __itemFollowerType;

    private BufferTypeHandle<GameServerItemFollower> __targetItemFollowerType;

    private ComponentTypeHandle<GameServerItemFollowerData> __instanceType;
    
    private SharedList<GameItemOwnSystem.Command> __origins;

    private NativeQueue<GameItemOwnSystem.Command> __commands;
    private NativeQueue<Result> __results;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __groupToAppend = builder
                .WithAll<GameServerItemFollower, GameItemFollower, NetworkIdentity>()
                .WithAllRW<GameServerItemFollowerData>()
                .WithOptions(EntityQueryOptions.IncludeDisabledEntities)
                .Build(ref state);

        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __groupToCollect = builder
                .WithAll<GameServerItemFollowerData, GameItemFollower, GameFollower, NetworkIdentity>()
                .WithAllRW<GameServerItemFollower>()
                .WithOptions(EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IgnoreComponentEnabledState)
                .Build(ref state);
        
        __groupToCollect.AddChangedVersionFilter(ComponentType.ReadOnly<GameItemFollower>());
        __groupToCollect.AddChangedVersionFilter(ComponentType.ReadOnly<GameFollower>());
        
        __managerGroup = NetworkServerManager.GetEntityQuery(ref state);
        __controllerGroup = NetworkRPCController.GetEntityQuery(ref state);

        __instances = state.GetComponentLookup<GameServerItemFollowerData>(true);
        __itemTypes = state.GetComponentLookup<GameItemType>(true);
        __itemObjects = state.GetComponentLookup<GameItemObjectData>(true);
        __itemNames = state.GetComponentLookup<GameItemName>(true);
        __itemRoots = state.GetComponentLookup<GameItemRoot>(true);
        __identities= state.GetComponentLookup<NetworkIdentity>(true);
        __entityType = state.GetEntityTypeHandle();
        __followerType = state.GetBufferTypeHandle<GameFollower>(true);
        __itemFollowerType = state.GetBufferTypeHandle<GameItemFollower>(true);
        __targetItemFollowerType = state.GetBufferTypeHandle<GameServerItemFollower>();
        __instanceType = state.GetComponentTypeHandle<GameServerItemFollowerData>();

        var world = state.WorldUnmanaged;
        __origins = world.GetExistingSystemUnmanaged<GameItemOwnSystem>().commands;

        __commands = new NativeQueue<GameItemOwnSystem.Command>(Allocator.Persistent);

        __results = new NativeQueue<Result>(Allocator.Persistent);
    }

    //[BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        __commands.Dispose();
        
        __results.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var origins = __origins.reader;
        var entityType = __entityType.UpdateAsRef(ref state);
        var itemFollowerType = __itemFollowerType.UpdateAsRef(ref state);
        var targetItemFollowerType = __targetItemFollowerType.UpdateAsRef(ref state);
        
        AppendEx append;
        append.origins = origins;
        append.entityType = entityType;
        append.itemFollowerType = itemFollowerType;
        append.targetItemFollowerType = targetItemFollowerType;
        append.instanceType = __instanceType.UpdateAsRef(ref state);
        append.commands = __commands.AsParallelWriter();
        append.results = __results.AsParallelWriter();

        ref var originsJobManager = ref __origins.lookupJobManager;
        var jobHandle = append.ScheduleParallelByRef(__groupToAppend, 
            JobHandle.CombineDependencies(originsJobManager.readOnlyJobHandle, state.Dependency));

        var identities = __identities.UpdateAsRef(ref state);
        
        CollectEx collect;
        collect.identities = identities;
        collect.entityType = entityType;
        collect.itemRoots = __itemRoots.UpdateAsRef(ref state);
        collect.followerType = __followerType.UpdateAsRef(ref state);
        collect.itemFollowerType = itemFollowerType;
        collect.targetItemFollowerType = targetItemFollowerType;
        collect.results = __results.AsParallelWriter();

        jobHandle = collect.ScheduleParallelByRef(__groupToCollect, jobHandle);

        if (SystemAPI.HasSingleton<NetworkServerEntityChannel>())
        {
            var channels = SystemAPI.GetSingletonBuffer<NetworkServerEntityChannel>(true).AsNativeArray();
            if (channels.Length > 0)
            {
                var manager = __managerGroup.GetSingleton<NetworkServerManager>();
                var controller = __controllerGroup.GetSingleton<NetworkRPCController>();

                Apply apply;
                apply.model = StreamCompressionModel.Default;
                apply.channels = channels;
                apply.origins = origins;
                apply.identities = identities;
                apply.itemTypes = __itemTypes.UpdateAsRef(ref state);
                apply.itemObjects = __itemObjects.UpdateAsRef(ref state);
                apply.itemNames = __itemNames.UpdateAsRef(ref state);
                apply.instances = __instances.UpdateAsRef(ref state);
                apply.commands = __commands;
                apply.results = __results;
                apply.driver = manager.server.driver;
                apply.rpcCommander = controller.commander;

                ref var managerJobManager = ref manager.lookupJobManager;
                ref var controllerJobManager = ref controller.lookupJobManager;

                jobHandle = JobHandle.CombineDependencies(jobHandle,
                    managerJobManager.readWriteJobHandle,
                    controllerJobManager.readWriteJobHandle);
                jobHandle = apply.ScheduleByRef(jobHandle);

                managerJobManager.readWriteJobHandle = jobHandle;
                controllerJobManager.readWriteJobHandle = jobHandle;
            }
        }

        originsJobManager.AddReadOnlyDependency(jobHandle);
        
        state.Dependency = jobHandle;
    }
}
