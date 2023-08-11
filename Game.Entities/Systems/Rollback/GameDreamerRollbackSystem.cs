using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Jobs;
using Unity.Entities;
using ZG;

[assembly: RegisterGenericJobType(typeof(RollbackRestore<GameDreamerRollbackSystem.Restore>))]
[assembly: RegisterGenericJobType(typeof(RollbackSave<GameDreamerRollbackSystem.Save>))]
[assembly: RegisterGenericJobType(typeof(RollbackClear<GameDreamerRollbackSystem.Clear>))]

[assembly: RegisterGenericJobType(typeof(RollbackResize<GameDreamer>))]
[assembly: RegisterGenericJobType(typeof(RollbackResize<GameDreamerInfo>))]
[assembly: RegisterGenericJobType(typeof(RollbackResize<GameDreamerVersion>))]

[assembly: RegisterGenericJobType(typeof(RollbackSaveBufferCount<GameDreamerEvent>))]
[assembly: RegisterGenericJobType(typeof(RollbackSaveBufferResizeValues<GameDreamerEvent>))]

/*[AutoCreateIn("Client")]
public partial class GameDreamerRollbackSystem : RollbackSystemEx
{
    public struct Restore : IRollbackRestore
    {
        public ComponentRestoreFunction<GameDreamer> dreamers;
        public ComponentRestoreFunction<GameDreamerVersion> versions;
        public BufferRestoreFunction<GameDreamerEvent> events;

        public EntityCommandQueue<EntityData<GameDreamer>>.ParallelWriter entityManager;

        public void Execute(int index, int entityIndex, in Entity entity)
        {
            if (!versions.IsExists(entity))
                return;

            if (dreamers.IsExists(entity))
                dreamers.Invoke(entityIndex, entity);
            else
            {
                EntityData<GameDreamer> command;
                command.entity = entity;
                command.value = dreamers[entityIndex];
                entityManager.Enqueue(command);
            }

            versions.Invoke(entityIndex, entity);
            events.Invoke(entityIndex, entity);
        }
    }

    public struct Save : IRollbackSave
    {
        public ComponentSaveFunction<GameDreamer> dreamers;
        public ComponentSaveFunction<GameDreamerVersion> versions;
        public BufferSaveFunction<GameDreamerEvent> events;

        public void Execute(in ArchetypeChunk chunk, int firstEntityIndex)
        {
            dreamers.Invoke(chunk, firstEntityIndex);
            versions.Invoke(chunk, firstEntityIndex);
            events.Invoke(chunk, firstEntityIndex);
        }
    }

    public struct Clear : IRollbackClear
    {
        public ComponentClearFunction<GameDreamer> dreamers;
        public ComponentClearFunction<GameDreamerVersion> versions;
        public BufferClearFunction<GameDreamerEvent> events;

        public void Remove(int fromIndex, int count)
        {
            events.Remove(fromIndex, count);
        }
        
        public void Move(int fromIndex, int toIndex, int count)
        {
            dreamers.Move(fromIndex, toIndex, count);
            versions.Move(fromIndex, toIndex, count);
            events.Move(fromIndex, toIndex, count);
        }

        public void Resize(int count)
        {
            dreamers.Resize(count);
            versions.Resize(count);
            events.Resize(count);
        }
    }

    private EntityQuery __group;
    private EntityCommandPool<EntityCommandStructChange> __removeComponentCommander;
    private EntityCommandPool<EntityData<GameDreamer>> __addComponentCommander;

    private Component<GameDreamer> __dreamers;
    private Component<GameDreamerVersion> __versions;
    private Buffer<GameDreamerEvent> __events;

    protected override void OnCreate()
    {
        base.OnCreate();

        __group = GetEntityQuery(
            ComponentType.ReadOnly<GameRollbackObject>(),
            ComponentType.ReadWrite<GameDreamer>());

        __removeComponentCommander = World.GetOrCreateSystem<EndRollbackSystemGroupStructChangeSystem>().Struct.manager.removeComponentPool;
        __addComponentCommander = endFrameBarrier.CreateAddComponentDataCommander<GameDreamer>();

        __dreamers = _GetComponent<GameDreamer>();
        __versions = _GetComponent<GameDreamerVersion>();
        __events = _GetBuffer<GameDreamerEvent>();

    }
    
    protected override JobHandle _Restore(uint frameIndex, JobHandle inputDeps)
    {
        var entityManager = __addComponentCommander.Create();

        Restore restore;
        restore.dreamers = DelegateRestore(__dreamers);
        restore.versions = DelegateRestore(__versions);
        restore.events = DelegateRestore(__events);
        restore.entityManager = entityManager.parallelWriter;

        JobHandle jobHandle = _Schedule(restore, frameIndex, inputDeps);

        entityManager.AddJobHandleForProducer(jobHandle);

        inputDeps = _RemoveComponentIfNotSaved<GameDreamer>(frameIndex, __group, __removeComponentCommander, inputDeps);

        return JobHandle.CombineDependencies(jobHandle, inputDeps);
    }

    protected override JobHandle _Save(uint frameIndex, EntityTypeHandle entityType, JobHandle inputDeps)
    {
        int entityCount = __group.CalculateEntityCount();

        Save save;
        save.dreamers = DelegateSave(entityCount, __dreamers, inputDeps);
        save.versions = DelegateSave(entityCount, __versions, inputDeps);
        save.events = DelegateSave(entityCount, __events, __group, inputDeps);

        return _Schedule(
            save, 
            entityCount, 
            frameIndex, 
            entityType, 
            __group,
            inputDeps);
    }

    protected override JobHandle _Clear(uint maxFrameIndex, uint frameIndex, uint frameCount, JobHandle inputDeps)
    {
        Clear clear;
        clear.dreamers = DelegateClear(__dreamers);
        clear.versions = DelegateClear(__versions);
        clear.events = DelegateClear(__events);

        return _Schedule(clear, maxFrameIndex, frameIndex, frameCount, inputDeps);
    }
}*/
[BurstCompile,
    CreateAfter(typeof(EndRollbackSystemGroupStructChangeSystem)),
    CreateAfter(typeof(RollbackSystemGroup)),
    UpdateInGroup(typeof(RollbackSystemGroup)),
    AutoCreateIn("Client")]
public partial struct GameDreamerRollbackSystem : ISystem, IRollbackCore
{
    public struct Restore : IRollbackRestore, IEntityCommandProducerJob
    {
        public RollbackComponentRestoreFunction<GameDreamer> dreamers;
        public RollbackComponentRestoreFunction<GameDreamerInfo> dreamerInfos;
        public RollbackComponentRestoreFunction<GameDreamerVersion> versions;
        public RollbackBufferRestoreFunction<GameDreamerEvent> events;

        public EntityAddDataQueue.ParallelWriter entityManager;

        public void Execute(int index, int entityIndex, in Entity entity)
        {
            if (!versions.IsExists(entity))
                return;

            if (dreamers.IsExists(entity))
                dreamers.Invoke(entityIndex, entity);
            else
                entityManager.AddComponentData(entity, dreamers[entityIndex]);

            dreamerInfos.Invoke(entityIndex, entity);
            versions.Invoke(entityIndex, entity);
            events.Invoke(entityIndex, entity);
        }
    }

    public struct Save : IRollbackSave
    {
        public RollbackComponentSaveFunction<GameDreamer> dreamers;
        public RollbackComponentSaveFunction<GameDreamerInfo> dreamerInfos;
        public RollbackComponentSaveFunction<GameDreamerVersion> versions;
        public RollbackBufferSaveFunction<GameDreamerEvent> events;

        public void Execute(in ArchetypeChunk chunk, int firstEntityIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            dreamers.Invoke(chunk, firstEntityIndex, useEnabledMask, chunkEnabledMask);
            dreamerInfos.Invoke(chunk, firstEntityIndex, useEnabledMask, chunkEnabledMask);
            versions.Invoke(chunk, firstEntityIndex, useEnabledMask, chunkEnabledMask);
            events.Invoke(chunk, firstEntityIndex, useEnabledMask, chunkEnabledMask);
        }
    }

    public struct Clear : IRollbackClear
    {
        public RollbackComponentClearFunction<GameDreamer> dreamers;
        public RollbackComponentClearFunction<GameDreamerInfo> dreamerInfos;
        public RollbackComponentClearFunction<GameDreamerVersion> versions;
        public RollbackBufferClearFunction<GameDreamerEvent> events;

        public void Remove(int fromIndex, int count)
        {
            events.Remove(fromIndex, count);
        }

        public void Move(int fromIndex, int toIndex, int count)
        {
            dreamers.Move(fromIndex, toIndex, count);
            dreamerInfos.Move(fromIndex, toIndex, count);
            versions.Move(fromIndex, toIndex, count);
            events.Move(fromIndex, toIndex, count);
        }

        public void Resize(int count)
        {
            dreamers.Resize(count);
            dreamerInfos.Resize(count);
            versions.Resize(count);
            events.Resize(count);
        }
    }

    private EntityQuery __group;
    private EntityTypeHandle __entityType;
    private EntityCommandPool<EntityCommandStructChange> __removeComponentCommander;
    private EntityAddDataPool __addComponentCommander;

    private RollbackManager<Restore, Save, Clear> __manager;

    private RollbackComponent<GameDreamer> __dreamers;
    private RollbackComponent<GameDreamerInfo> __dreamerInfos;
    private RollbackComponent<GameDreamerVersion> __versions;
    private RollbackBuffer<GameDreamerEvent> __events;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        using (var builder = new EntityQueryBuilder(Unity.Collections.Allocator.Temp))
            __group = builder
                    .WithAll<RollbackObject>()
                    .WithAllRW<GameDreamer>()
                    .Build(ref state);

        __entityType = state.GetEntityTypeHandle();

        var world = state.WorldUnmanaged;
        ref var endFrameBarrier = ref world.GetExistingSystemUnmanaged<EndRollbackSystemGroupStructChangeSystem>();
        __removeComponentCommander = endFrameBarrier.manager.removeComponentPool;
        __addComponentCommander = endFrameBarrier.addDataCommander;

        var containerManager = world.GetExistingSystemUnmanaged<RollbackSystemGroup>().containerManager;

        __manager = containerManager.CreateManager<Restore, Save, Clear>(ref state);

        __dreamers = containerManager.CreateComponent<GameDreamer>(ref state);
        __dreamerInfos = containerManager.CreateComponent<GameDreamerInfo>(ref state);
        __versions = containerManager.CreateComponent<GameDreamerVersion>(ref state);
        __events = containerManager.CreateBuffer<GameDreamerEvent>(ref state);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {

    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        __manager.Update(ref state, ref this);
    }

    public void ScheduleRestore(uint frameIndex, ref SystemState state)
    {
        var inputDeps = __manager.GetChunk(frameIndex, state.Dependency);

        var jobHandle = __manager.RemoveComponentIfNotSaved<GameDreamer>(
            frameIndex, 
            __group, 
            __entityType.UpdateAsRef(ref state), 
            __removeComponentCommander, 
            inputDeps);

        var entityManager = __addComponentCommander.Create();

        Restore restore;
        restore.dreamers = __manager.DelegateRestore(__dreamers, ref state);
        restore.dreamerInfos = __manager.DelegateRestore(__dreamerInfos, ref state);
        restore.versions = __manager.DelegateRestore(__versions, ref state);
        restore.events = __manager.DelegateRestore(__events, ref state);
        restore.entityManager = entityManager.AsComponentParallelWriter<GameDreamer>(__manager.countAndStartIndex, ref inputDeps);

        jobHandle = __manager.ScheduleParallel(restore, frameIndex, JobHandle.CombineDependencies(jobHandle, inputDeps));

        entityManager.AddJobHandleForProducer<Restore>(jobHandle);

        state.Dependency = jobHandle;
    }

    public void ScheduleSave(uint frameIndex, in EntityTypeHandle entityType, ref SystemState state)
    {
        var data = new RollbackSaveData(__group, ref state);

        Save save;
        save.dreamers = __manager.DelegateSave(__dreamers, ref data, ref state);
        save.dreamerInfos = __manager.DelegateSave(__dreamerInfos, ref data, ref state);
        save.versions = __manager.DelegateSave(__versions, ref data, ref state);
        save.events = __manager.DelegateSave(__events, __group, ref data, ref state);

        state.Dependency = __manager.ScheduleParallel(
            save,
            frameIndex,
            entityType,
            __group,
            data);
    }

    public void ScheduleClear(uint maxFrameIndex, uint frameIndex, uint frameCount, ref SystemState state)
    {
        Clear clear;
        clear.dreamers = __manager.DelegateClear(__dreamers);
        clear.dreamerInfos = __manager.DelegateClear(__dreamerInfos);
        clear.versions = __manager.DelegateClear(__versions);
        clear.events = __manager.DelegateClear(__events);

        state.Dependency = __manager.Schedule(clear, maxFrameIndex, frameIndex, frameCount, state.Dependency);
    }
}