using Unity.Collections;
using Unity.Mathematics;
using Unity.Entities;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Jobs;
using ZG;

public struct GameItemFollower : IBufferElementData
{
    public GameItemHandle handle;
    public Entity entity;
}

[BurstCompile, UpdateInGroup(typeof(InitializationSystemGroup), OrderLast = true)]
public partial struct GameItemOwnSystem : ISystem
{
    public struct Command
    {
        public bool isAddOrRemove;
        public GameItemHandle handle;
        public Entity source;
        public Entity destination;
    }

    [BurstCompile]
    private struct Resize : IJob
    {
        [ReadOnly, DeallocateOnJobCompletion]
        public NativeArray<int> counter;

        public NativeList<Command> commands;

        public void Execute()
        {
            commands.Clear();
            commands.Capacity = math.max(commands.Capacity, counter[0]);
        }
    }
    
    private struct DidChange
    {
        [ReadOnly]
        public NativeArray<Entity> entityArray;

        [ReadOnly]
        public NativeArray<GameItemData> instances;

        [ReadOnly]
        public NativeArray<GameItemOwner> owners;

        [ReadOnly] 
        public BufferLookup<GameItemFollower> followers;

        public SharedList<Command>.ParallelWriter commands;
        
        public void Execute(int index)
        {
            Entity owner = owners[index].entity;

            if (!this.followers.HasBuffer(owner))
                return;

            Entity entity = entityArray[index];

            var handle = GameItemHandle.Empty;
            var followers = this.followers[owner];
            GameItemFollower follower;
            int numFollowers = followers.Length;
            for (int i = 0; i < numFollowers; ++i)
            {
                follower = followers[i];
                if (follower.entity == entity)
                {
                    handle = follower.handle;

                    break;
                }
            }

            bool isContains = handle.Equals(handle);
            if (isContains != (index < instances.Length))
            {
                Command command;
                command.isAddOrRemove = isContains;
                command.handle = isContains ? handle : instances[index].handle;
                command.source = owner;
                command.destination = entity;
                
                commands.AddNoResize(command);
            }
        }
    }

    [BurstCompile]
    private struct DidChangeEx : IJobChunk
    {
        [ReadOnly]
        public EntityTypeHandle entityType;

        [ReadOnly]
        public ComponentTypeHandle<GameItemData> instanceType;

        [ReadOnly]
        public ComponentTypeHandle<GameItemOwner> ownerType;

        [ReadOnly] 
        public BufferLookup<GameItemFollower> followers;

        public SharedList<Command>.ParallelWriter commands;
        
        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            DidChange didChange;
            didChange.entityArray = chunk.GetNativeArray(entityType);
            didChange.instances = chunk.GetNativeArray(ref instanceType);
            didChange.owners = chunk.GetNativeArray(ref ownerType);
            didChange.followers = followers;
            didChange.commands = commands;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                didChange.Execute(i);
        }
    }

    [BurstCompile]
    private struct Apply : IJob
    {
        [ReadOnly] 
        public SharedList<Command>.Reader commands;

        public BufferLookup<GameItemFollower> followers;

        public void Execute()
        {
            int i, numFollowers;
            GameItemFollower follower;
            DynamicBuffer<GameItemFollower> followers;
            foreach (var command in commands.AsArray())
            {
                followers = this.followers[command.source];
                if (command.isAddOrRemove)
                {
                    follower.handle = command.handle;
                    follower.entity = command.destination;
                    followers.Add(follower);
                }
                else
                {
                    numFollowers = followers.Length;
                    for (i = 0; i < numFollowers; ++i)
                    {
                        if (followers.ElementAt(i).entity == command.destination)
                        {
                            followers.RemoveAtSwapBack(i);
                            
                            break;
                        }
                    }
                }
            }
        }
    }
    
    private EntityQuery __group;

    private EntityTypeHandle __entityType;

    private ComponentTypeHandle<GameItemData> __instanceType;

    private ComponentTypeHandle<GameItemOwner> __ownerType;

    private BufferLookup<GameItemFollower> __followers;

    public SharedList<Command> commands
    {
        get;

        private set;
    }

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __group = builder
                .WithAll<GameItemOwner>()
                .Build(ref state);
        
        __group.AddChangedVersionFilter(ComponentType.ReadOnly<GameItemOwner>());
        __group.AddOrderVersionFilter();

        __entityType = state.GetEntityTypeHandle();
        __instanceType = state.GetComponentTypeHandle<GameItemData>(true);
        __ownerType = state.GetComponentTypeHandle<GameItemOwner>(true);
        __followers = state.GetBufferLookup<GameItemFollower>();

        commands = new SharedList<Command>(Allocator.Persistent);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        commands.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var counter = new NativeArray<int>(1, Allocator.TempJob, NativeArrayOptions.ClearMemory);
        var jobHandle = __group.CalculateEntityCountAsync(counter, state.Dependency);
        Resize resize;
        resize.counter = counter;
        resize.commands = commands.writer;

        ref var commandsJobManager = ref commands.lookupJobManager;
        
        jobHandle = resize.ScheduleByRef(JobHandle.CombineDependencies(jobHandle, commandsJobManager.readWriteJobHandle));

        var followers = __followers.UpdateAsRef(ref state);

        DidChangeEx didChange;
        didChange.entityType = __entityType.UpdateAsRef(ref state);
        didChange.instanceType = __instanceType.UpdateAsRef(ref state);
        didChange.ownerType = __ownerType.UpdateAsRef(ref state);
        didChange.followers = __followers;
        didChange.commands = commands.parallelWriter;
        jobHandle = didChange.ScheduleParallelByRef(__group, jobHandle);

        Apply apply;
        apply.followers = followers;
        apply.commands = commands.reader;
        jobHandle = apply.ScheduleByRef(jobHandle);

        commandsJobManager.readWriteJobHandle = jobHandle;

        state.Dependency = jobHandle;
    }
}
