using System.Collections.Generic;
using Unity.Jobs;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Entities;
using Unity.Collections;
using Unity.Physics;
using ZG;

[assembly: RegisterGenericJobType(typeof(RollbackRestore<GameShapeRollbackSystem.Restore>))]
[assembly: RegisterGenericJobType(typeof(RollbackSave<GameShapeRollbackSystem.Save>))]
[assembly: RegisterGenericJobType(typeof(RollbackClear<GameShapeRollbackSystem.Clear>))]

[assembly: RegisterGenericJobType(typeof(RollbackRestore<GameColliderRollbackSystem.Restore>))]
[assembly: RegisterGenericJobType(typeof(RollbackSave<GameColliderRollbackSystem.Save>))]
[assembly: RegisterGenericJobType(typeof(RollbackClear<GameColliderRollbackSystem.Clear>))]

[assembly: RegisterGenericJobType(typeof(RollbackRestore<GameRollbackObjectSystem.Restore>))]
[assembly: RegisterGenericJobType(typeof(RollbackSave<GameRollbackObjectSystem.Save>))]
[assembly: RegisterGenericJobType(typeof(RollbackClear<GameRollbackObjectSystem.Clear>))]

[assembly: RegisterGenericJobType(typeof(RollbackSaveBufferCount<PhysicsHierarchyInactiveColliders>))]
[assembly: RegisterGenericJobType(typeof(RollbackSaveBufferResizeValues<PhysicsHierarchyInactiveColliders>))]

/*public struct GameRollbackObject : IComponentData
{

}*/

public struct GameRollbackCollider : IComponentData
{

}

public struct GameRollbackObjectIncludeDisabled : IComponentData
{
    public unsafe static EntityQuery GetEntityQuery(NativeArray<ComponentType> componentTypes, ref SystemState state)
    {
        var builder = new EntityQueryBuilder(Allocator.Temp);
        {
            NativeList<ComponentType> all = new NativeList<ComponentType>(Allocator.Temp), none = default;
            ComponentType excludeType;
            foreach (var componentType in componentTypes)
            {
                if (componentType.AccessModeType == ComponentType.AccessMode.Exclude)
                {
                    excludeType.TypeIndex = componentType.TypeIndex;
                    excludeType.AccessModeType = ComponentType.AccessMode.ReadWrite;

                    if (!none.IsCreated)
                        none = new NativeList<ComponentType>(Allocator.Temp);

                    none.Add(excludeType);
                }
                else
                    all.Add(componentType);
            }

            builder = builder.WithAll(ref all);

            if (none.IsCreated)
                builder = builder.WithNone(ref none);

            all.Add(ComponentType.ReadOnly<GameRollbackObjectIncludeDisabled>());

            builder = builder
                .AddAdditionalQuery()
                .WithAll(ref all);

            all.Dispose();

            if (none.IsCreated)
            {
                builder = builder.WithNone(ref none);

                none.Dispose();
            }

            var result = builder
                .WithOptions(EntityQueryOptions.IncludeDisabledEntities)
                .Build(ref state);

            builder.Dispose();

            return result;
        }
    }
}

/*[AutoCreateIn("Client")]
public partial class GameColliderRollbackSystem : RollbackSystemEx
{
    public struct Restore : IRollbackRestore
    {
        [ReadOnly]
        public BufferLookup<PhysicsShapeChild> children;

        [ReadOnly]
        public ComponentLookup<PhysicsExclude> physicsExcludes;
        
        public ComponentRestoreFunction<PhysicsCollider> physicsColliders;
        public EntityCommandQueue<EntityCommandStructChange>.ParallelWriter entityManager;
        
        public void Execute(int index, int entityIndex, in Entity entity)
        {
            if (!children.HasComponent(entity))
                return;

            physicsColliders.Invoke(entityIndex, entity);

            if (physicsExcludes.HasComponent(entity))
            {
                EntityCommandStructChange command;
                command.componentType = ComponentType.ReadWrite<PhysicsExclude>();
                command.entity = entity;
                entityManager.Enqueue(command);
            }
        }
    }

    public struct Save : IRollbackSave
    {
        public ComponentSaveFunction<PhysicsCollider> physicsColliders;

        public void Execute(in ArchetypeChunk chunk, int firstEntityIndex)
        {
            physicsColliders.Invoke(chunk, firstEntityIndex);
        }
    }

    public struct Clear : IRollbackClear
    {
        public ComponentClearFunction<PhysicsCollider> physicsColliders;

        public void Remove(int fromIndex, int count)
        {
        }

        public void Move(int fromIndex, int toIndex, int count)
        {
            physicsColliders.Move(fromIndex, toIndex, count);
        }

        public void Resize(int count)
        {
            physicsColliders.Resize(count);
        }
    }

    private EntityQuery __group;
    
    private EntityCommandPool<EntityCommandStructChange> __addComponentCommander;
    private EntityCommandPool<EntityCommandStructChange> __removeComponentCommander;

    private Component<PhysicsCollider> __physicsColliders;

    protected override void OnCreate()
    {
        base.OnCreate();

        __group = GetEntityQuery(new EntityQueryDesc()
        {
            All = new ComponentType[]
            {
                ComponentType.ReadOnly<GameRollbackObject>(),
                //ComponentType.ReadOnly<PhysicsShapeChild>(),
                ComponentType.ReadOnly<GameNodeShpaeDefault>(),
                ComponentType.ReadOnly<PhysicsCollider>()
            },

            None = new ComponentType[]
            {
                typeof(Disabled), 
                typeof(PhysicsExclude)
            }
        },
        new EntityQueryDesc()
        {
            All = new ComponentType[]
            {
                ComponentType.ReadOnly<GameRollbackObject>(),
                //ComponentType.ReadOnly<PhysicsShapeChild>(),
                ComponentType.ReadOnly<PhysicsCollider>()
            },

            None = new ComponentType[]
            {
                typeof(Disabled),
                typeof(PhysicsExclude),
                typeof(PhysicsVelocity),
                typeof(GameNodeShpaeDefault)
            }
        });

        __physicsColliders = _GetComponent<PhysicsCollider>();

        var manager = World.GetOrCreateSystem<EndRollbackSystemGroupStructChangeSystem>().Struct.manager;
        __addComponentCommander = manager.addComponentPool;
        __removeComponentCommander = manager.removeComponentPool;
    }
    
    protected override JobHandle _Restore(uint frameIndex, JobHandle inputDeps)
    {
        var entityManager = __removeComponentCommander.Create();

        Restore restore;
        restore.children = GetBufferLookup<PhysicsShapeChild>(true);
        restore.physicsExcludes = GetComponentLookup<PhysicsExclude>(true);
        restore.physicsColliders = DelegateRestore(__physicsColliders);
        restore.entityManager = entityManager.parallelWriter;

        JobHandle jobHandle = _Schedule(restore, frameIndex, inputDeps);

        entityManager.AddJobHandleForProducer(jobHandle);

        inputDeps = _AddComponentIfNotSaved<PhysicsExclude>(frameIndex, __group, __addComponentCommander, inputDeps);
        
        return JobHandle.CombineDependencies(jobHandle, inputDeps);
    }

    protected override JobHandle _Save(uint frameIndex, EntityTypeHandle entityType, JobHandle inputDeps)
    {
        int entityCount = __group.CalculateEntityCount();

        Save save;
        save.physicsColliders = DelegateSave(entityCount, __physicsColliders, inputDeps);

        return _Schedule(save, entityCount, frameIndex, entityType, __group, inputDeps);
    }

    protected override JobHandle _Clear(uint maxFrameIndex, uint frameIndex, uint frameCount, JobHandle inputDeps)
    {
        Clear clear;
        clear.physicsColliders = DelegateClear(__physicsColliders);

        return _Schedule(clear, maxFrameIndex, frameIndex, frameCount, inputDeps);
    }
}

[AutoCreateIn("Client")]
public partial class GameRollbackObjectSystem : RollbackSystemEx
{
    public struct Restore : IRollbackRestore
    {
        [ReadOnly]
        public ComponentLookup<GameRollbackObject> rollbackObjects;

        [ReadOnly]
        public ComponentLookup<Disabled> disabled;
        
        public EntityCommandQueue<EntityCommandStructChange>.ParallelWriter entityManager;

        public void Execute(int index, int entityIndex, in Entity entity)
        {
            if (!rollbackObjects.HasComponent(entity))
                return;

            if (disabled.HasComponent(entity))
            {
                EntityCommandStructChange command;
                command.componentType = ComponentType.ReadWrite<Disabled>();
                command.entity = entity;
                entityManager.Enqueue(command);
            }
        }
    }

    public struct Save : IRollbackSave
    {
        public void Execute(in ArchetypeChunk chunk, int firstEntityIndex)
        {
        }
    }

    public struct Clear : IRollbackClear
    {
        public void Remove(int fromIndex, int count)
        {
        }

        public void Move(int fromIndex, int toIndex, int count)
        {
        }

        public void Resize(int count)
        {
        }
    }

    private EntityQuery __group;

    private EntityCommandPool<EntityCommandStructChange> __addComponentCommander;
    private EntityCommandPool<EntityCommandStructChange> __removeComponentCommander;

    protected override void OnCreate()
    {
        base.OnCreate();

        __group = GetEntityQuery(ComponentType.ReadOnly<GameRollbackObject>());

        var manager = World.GetOrCreateSystem<EndRollbackSystemGroupStructChangeSystem>().Struct.manager;
        __addComponentCommander = manager.addComponentPool;
        __removeComponentCommander = manager.removeComponentPool;
    }
    
    protected override JobHandle _Restore(uint frameIndex, JobHandle inputDeps)
    {
        var removeComponentCommander = __removeComponentCommander.Create();

        Restore restore;
        restore.rollbackObjects = GetComponentLookup<GameRollbackObject>(true);
        restore.disabled = GetComponentLookup<Disabled>(true);
        restore.entityManager = removeComponentCommander.parallelWriter;

        JobHandle jobHandle = _Schedule(restore, frameIndex, inputDeps);

        removeComponentCommander.AddJobHandleForProducer(jobHandle);

        inputDeps = _AddComponentIfNotSaved<Disabled>(frameIndex, __group, __addComponentCommander, inputDeps);

        return JobHandle.CombineDependencies(jobHandle, inputDeps);
    }

    protected override JobHandle _Save(uint frameIndex, EntityTypeHandle entityType, JobHandle inputDeps)
    {
        int entityCount = __group.CalculateEntityCount();

        Save save;

        return _Schedule(save, entityCount, frameIndex, entityType, __group, inputDeps);
    }

    protected override JobHandle _Clear(uint maxFrameIndex, uint frameIndex, uint frameCount, JobHandle inputDeps)
    {
        Clear clear;

        return _Schedule(clear, maxFrameIndex, frameIndex, frameCount, inputDeps);
    }
}*/
[BurstCompile,
    CreateAfter(typeof(EndRollbackSystemGroupStructChangeSystem)),
    CreateAfter(typeof(RollbackSystemGroup)),
    UpdateInGroup(typeof(RollbackSystemGroup)),
    AutoCreateIn("Client")]
public partial struct GameShapeRollbackSystem : ISystem, IRollbackCore
{
    public struct Restore : IRollbackRestore, IEntityCommandProducerJob
    {
        [ReadOnly]
        public ComponentLookup<GameNodeShpaeDefault> shapes;

        //门逻辑已经被替代
        /*[ReadOnly]
        public ComponentLookup<PhysicsVelocity> velocities;*/

        [ReadOnly]
        public ComponentLookup<PhysicsExclude> physicsExcludes;

        //public RollbackComponentRestoreFunction<PhysicsCollider> physicsColliders;

        public EntityCommandQueue<EntityCommandStructChange>.ParallelWriter entityManager;

        public void Execute(int index, int entityIndex, in Entity entity)
        {
            if (!shapes.HasComponent(entity)/* && velocities.HasComponent(entity)*/)
            {
                UnityEngine.Debug.LogError($"Restore {entity} has been failed!");

                return;
            }

            //physicsColliders.Invoke(entityIndex, entity);

            if (physicsExcludes.HasComponent(entity))
            {
                EntityCommandStructChange command;
                command.componentType = ComponentType.ReadWrite<PhysicsExclude>();
                command.entity = entity;
                entityManager.Enqueue(command);
            }
        }
    }

    public struct Save : IRollbackSave
    {
        //public RollbackComponentSaveFunction<PhysicsCollider> physicsColliders;

        public void Execute(in ArchetypeChunk chunk, int firstEntityIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            //physicsColliders.Invoke(chunk, firstEntityIndex);
        }
    }

    public struct Clear : IRollbackClear
    {
        //public RollbackComponentClearFunction<PhysicsCollider> physicsColliders;

        public void Remove(int fromIndex, int count)
        {
        }

        public void Move(int fromIndex, int toIndex, int count)
        {
            //physicsColliders.Move(fromIndex, toIndex, count);
        }

        public void Resize(int count)
        {
            //physicsColliders.Resize(count);
        }
    }

    private EntityQuery __group;

    private ComponentLookup<GameNodeShpaeDefault> __shapes;
    private ComponentLookup<PhysicsExclude> __physicsExcludes;

    private EntityCommandPool<EntityCommandStructChange> __addComponentCommander;
    private EntityCommandPool<EntityCommandStructChange> __removeComponentCommander;

    private RollbackManager<Restore, Save, Clear> __manager;

    //private RollbackComponent<PhysicsCollider> __physicsColliders;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __group = builder
                    .WithAll<RollbackObject, GameNodeShpaeDefault, PhysicsCollider>()
                    .WithNone<Disabled, PhysicsExclude>()
                    .Build(ref state);
        /*,
            new EntityQueryDesc()
            {
                All = new ComponentType[]
                {
                    ComponentType.ReadOnly<RollbackObject>(),
                    //ComponentType.ReadOnly<PhysicsShapeChild>(),
                    ComponentType.ReadOnly<PhysicsCollider>()
                },

                None = new ComponentType[]
                {
                    typeof(Disabled),
                    typeof(PhysicsExclude),
                    typeof(PhysicsVelocity),
                    typeof(GameNodeShpaeDefault)
                }
            }*/;

        __shapes = state.GetComponentLookup<GameNodeShpaeDefault>(true);
        __physicsExcludes = state.GetComponentLookup<PhysicsExclude>(true);

        var world = state.WorldUnmanaged;
        var manager = world.GetExistingSystemUnmanaged<EndRollbackSystemGroupStructChangeSystem>().manager;
        __addComponentCommander = manager.addComponentPool;
        __removeComponentCommander = manager.removeComponentPool;

        var containerManager = world.GetExistingSystemUnmanaged<RollbackSystemGroup>().containerManager;

        __manager = containerManager.CreateManager<Restore, Save, Clear>(ref state);

        //__physicsColliders = containerManager.CreateComponent<PhysicsCollider>();
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
        //var result = __manager.AddComponentIfNotSaved<PhysicsExclude>(frameIndex, __group, __addComponentCommander, inputDeps, ref state);

        var entityManager = __removeComponentCommander.Create();

        Restore restore;
        restore.shapes = __shapes.UpdateAsRef(ref state);
        restore.physicsExcludes = __physicsExcludes.UpdateAsRef(ref state);
        //restore.physicsColliders = __manager.DelegateRestore(__physicsColliders, ref state);
        restore.entityManager = entityManager.parallelWriter;

        JobHandle jobHandle = __manager.ScheduleParallel(restore, frameIndex, state.Dependency, true);

        entityManager.AddJobHandleForProducer<Restore>(jobHandle);

        state.Dependency = jobHandle;// JobHandle.CombineDependencies(jobHandle, result);
    }

    public void ScheduleSave(uint frameIndex, in EntityTypeHandle entityType, ref SystemState state)
    {
        var data = new RollbackSaveData(__group, ref state);

        Save save;
        state.Dependency = __manager.ScheduleParallel(save, frameIndex, entityType, __group, data);
    }

    public void ScheduleClear(uint maxFrameIndex, uint frameIndex, uint frameCount, ref SystemState state)
    {
        Clear clear;
        state.Dependency = __manager.Schedule(clear, maxFrameIndex, frameIndex, frameCount, state.Dependency);
    }
}

[BurstCompile,
    CreateAfter(typeof(RollbackSystemGroup)),
    UpdateInGroup(typeof(RollbackSystemGroup)),
    AutoCreateIn("Client")]
public partial struct GameColliderRollbackSystem : ISystem, IRollbackCore
{
    public struct Restore : IRollbackRestore
    {
        [ReadOnly]
        public ComponentLookup<PhysicsHierarchyData> instances;

        public RollbackBufferRestoreFunction<PhysicsHierarchyInactiveColliders> inactiveColliders;

        public void Execute(int index, int entityIndex, in Entity entity)
        {
            if (!instances.HasComponent(entity))
                return;

            inactiveColliders.Invoke(entityIndex, entity);
        }
    }

    public struct Save : IRollbackSave
    {
        public RollbackBufferSaveFunction<PhysicsHierarchyInactiveColliders> inactiveColliders;

        public void Execute(in ArchetypeChunk chunk, int firstEntityIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            inactiveColliders.Invoke(chunk, firstEntityIndex, useEnabledMask, chunkEnabledMask);
        }
    }

    public struct Clear : IRollbackClear
    {
        public RollbackBufferClearFunction<PhysicsHierarchyInactiveColliders> inactiveColliders;

        public void Remove(int fromIndex, int count)
        {
            inactiveColliders.Remove(fromIndex, count);
        }

        public void Move(int fromIndex, int toIndex, int count)
        {
            inactiveColliders.Move(fromIndex, toIndex, count);
        }

        public void Resize(int count)
        {
            inactiveColliders.Resize(count);
        }
    }

    private EntityQuery __group;

    private ComponentLookup<PhysicsHierarchyData> __instances;

    private RollbackManager<Restore, Save, Clear> __manager;

    private RollbackBuffer<PhysicsHierarchyInactiveColliders> __inactiveColliders;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __group = builder
                    .WithAll<GameRollbackCollider, PhysicsHierarchyInactiveColliders>()
                    .WithNone<PhysicsExclude>()
                    .Build(ref state);

        __instances = state.GetComponentLookup<PhysicsHierarchyData>(true);

        var containerManager = state.WorldUnmanaged.GetExistingSystemUnmanaged<RollbackSystemGroup>().containerManager;

        __manager = containerManager.CreateManager<Restore, Save, Clear>(ref state);

        __inactiveColliders = containerManager.CreateBuffer<PhysicsHierarchyInactiveColliders>(ref state);
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
        Restore restore;
        restore.instances = __instances.UpdateAsRef(ref state);
        restore.inactiveColliders = __manager.DelegateRestore(__inactiveColliders, ref state);

        state.Dependency = __manager.ScheduleParallel(restore, frameIndex, state.Dependency, true);
    }

    public void ScheduleSave(uint frameIndex, in EntityTypeHandle entityType, ref SystemState state)
    {
        var data = new RollbackSaveData(__group, ref state);

        Save save;
        save.inactiveColliders = __manager.DelegateSave(__inactiveColliders, __group, ref data, ref state);

        state.Dependency = __manager.ScheduleParallel(save, frameIndex, entityType, __group, data);
    }

    public void ScheduleClear(uint maxFrameIndex, uint frameIndex, uint frameCount, ref SystemState state)
    {
        Clear clear;
        clear.inactiveColliders = __manager.DelegateClear(__inactiveColliders);

        state.Dependency = __manager.Schedule(clear, maxFrameIndex, frameIndex, frameCount, state.Dependency);
    }
}

[BurstCompile,
    CreateAfter(typeof(EndRollbackSystemGroupStructChangeSystem)),
    CreateAfter(typeof(RollbackSystemGroup)),
    UpdateInGroup(typeof(RollbackSystemGroup)),
    AutoCreateIn("Client")]
public partial struct GameRollbackObjectSystem : ISystem, IRollbackCore
{
    public struct Restore : IRollbackRestore, IEntityCommandProducerJob
    {
        [ReadOnly]
        public ComponentLookup<RollbackObject> rollbackObjects;

        [ReadOnly]
        public ComponentLookup<Disabled> disabled;

        public EntityCommandQueue<EntityCommandStructChange>.ParallelWriter entityManager;

        public void Execute(int index, int entityIndex, in Entity entity)
        {
            if (!rollbackObjects.HasComponent(entity))
                return;

            if (disabled.HasComponent(entity))
            {
                //UnityEngine.Debug.LogError($"Enable {entity}");

                EntityCommandStructChange command;
                command.componentType = ComponentType.ReadWrite<Disabled>();
                command.entity = entity;
                entityManager.Enqueue(command);
            }
        }
    }

    public struct Save : IRollbackSave
    {
        public void Execute(in ArchetypeChunk chunk, int firstEntityIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
        }
    }

    public struct Clear : IRollbackClear
    {
        public void Remove(int fromIndex, int count)
        {
        }

        public void Move(int fromIndex, int toIndex, int count)
        {
        }

        public void Resize(int count)
        {
        }
    }

    private EntityQuery __group;

    private EntityTypeHandle __entityType;

    private ComponentLookup<RollbackObject> __rollbackObjects;

    private ComponentLookup<Disabled> __disabled;

    private EntityCommandPool<EntityCommandStructChange> __addComponentCommander;
    private EntityCommandPool<EntityCommandStructChange> __removeComponentCommander;

    private RollbackManager<Restore, Save, Clear> __manager;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __group = state.GetEntityQuery(ComponentType.ReadOnly<RollbackObject>());

        __entityType = state.GetEntityTypeHandle();

        __rollbackObjects = state.GetComponentLookup<RollbackObject>(true);
        __disabled = state.GetComponentLookup<Disabled>(true);

        var world = state.WorldUnmanaged;

        var manager = world.GetExistingSystemUnmanaged<EndRollbackSystemGroupStructChangeSystem>().manager;
        __addComponentCommander = manager.addComponentPool;
        __removeComponentCommander = manager.removeComponentPool;

        var containerManager = world.GetExistingSystemUnmanaged<RollbackSystemGroup>().containerManager;

        __manager = containerManager.CreateManager<Restore, Save, Clear>(ref state);
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
        //var inputDeps = __manager.GetChunk(frameIndex, state.Dependency);

        var jobHandle = __manager.AddComponentIfNotSaved<Disabled>(
            frameIndex, 
            __group, 
            __entityType.UpdateAsRef(ref state), 
            __addComponentCommander,
            state.Dependency, 
            true);

        var removeComponentCommander = __removeComponentCommander.Create();

        Restore restore;
        restore.rollbackObjects = __rollbackObjects.UpdateAsRef(ref state);
        restore.disabled = __disabled.UpdateAsRef(ref state);
        restore.entityManager = removeComponentCommander.parallelWriter;

        jobHandle = __manager.ScheduleParallel(restore, frameIndex, jobHandle, false);

        removeComponentCommander.AddJobHandleForProducer<Restore>(jobHandle);

        state.Dependency = jobHandle;
    }

    public void ScheduleSave(uint frameIndex, in EntityTypeHandle entityType, ref SystemState state)
    {
        var data = new RollbackSaveData(__group, ref state);

        Save save;

        state.Dependency = __manager.ScheduleParallel(save, frameIndex, entityType, __group, data);
    }

    public void ScheduleClear(uint maxFrameIndex, uint frameIndex, uint frameCount, ref SystemState state)
    {
        Clear clear;

        state.Dependency = __manager.Schedule(clear, maxFrameIndex, frameIndex, frameCount, state.Dependency);
    }
}