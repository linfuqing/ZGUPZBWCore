using Unity.Jobs;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Entities;
using Unity.Collections;
using Unity.Transforms;
using Unity.Physics;
using ZG;

[assembly: RegisterGenericJobType(typeof(RollbackRestore<GameNodeTransformRollbackSystem.Restore>))]
[assembly: RegisterGenericJobType(typeof(RollbackSave<GameNodeTransformRollbackSystem.Save>))]
[assembly: RegisterGenericJobType(typeof(RollbackClear<GameNodeTransformRollbackSystem.Clear>))]

[assembly: RegisterGenericJobType(typeof(RollbackRestore<GameNodeActorRollbackSystem.Restore>))]
[assembly: RegisterGenericJobType(typeof(RollbackSave<GameNodeActorRollbackSystem.Save>))]
[assembly: RegisterGenericJobType(typeof(RollbackClear<GameNodeActorRollbackSystem.Clear>))]

[assembly: RegisterGenericJobType(typeof(RollbackRestore<GameNodeRootRollbackSystem.Restore>))]
[assembly: RegisterGenericJobType(typeof(RollbackSave<GameNodeRootRollbackSystem.Save>))]
[assembly: RegisterGenericJobType(typeof(RollbackClear<GameNodeRootRollbackSystem.Clear>))]

[assembly: RegisterGenericJobType(typeof(RollbackRestore<GameNodeChildRollbackSystem.Restore>))]
[assembly: RegisterGenericJobType(typeof(RollbackSave<GameNodeChildRollbackSystem.Save>))]
[assembly: RegisterGenericJobType(typeof(RollbackClear<GameNodeChildRollbackSystem.Clear>))]

[assembly: RegisterGenericJobType(typeof(RollbackRestore<GameNodeCharacterRollbackSystem.Restore>))]
[assembly: RegisterGenericJobType(typeof(RollbackSave<GameNodeCharacterRollbackSystem.Save>))]
[assembly: RegisterGenericJobType(typeof(RollbackClear<GameNodeCharacterRollbackSystem.Clear>))]

[assembly: RegisterGenericJobType(typeof(RollbackRestore<GameNodePhysicsRollbackSystem.Restore>))]
[assembly: RegisterGenericJobType(typeof(RollbackSave<GameNodePhysicsRollbackSystem.Save>))]
[assembly: RegisterGenericJobType(typeof(RollbackClear<GameNodePhysicsRollbackSystem.Clear>))]

//[assembly: RegisterGenericJobType(typeof(RollbackResize<Translation>))]
//[assembly: RegisterGenericJobType(typeof(RollbackResize<Rotation>))]

[assembly: RegisterGenericJobType(typeof(RollbackResize<GameNodeStatus>))]
[assembly: RegisterGenericJobType(typeof(RollbackResize<GameNodeOldStatus>))]
[assembly: RegisterGenericJobType(typeof(RollbackResize<GameNodeDelay>))]

[assembly: RegisterGenericJobType(typeof(RollbackResize<GameNodeActorStatus>))]
[assembly: RegisterGenericJobType(typeof(RollbackResize<GameNodeCharacterStatus>))]
[assembly: RegisterGenericJobType(typeof(RollbackResize<GameNodeCharacterAngle>))]
[assembly: RegisterGenericJobType(typeof(RollbackResize<GameNodeDirect>))]
[assembly: RegisterGenericJobType(typeof(RollbackResize<GameNodeAngle>))]
[assembly: RegisterGenericJobType(typeof(RollbackResize<GameNodeSurface>))]
[assembly: RegisterGenericJobType(typeof(RollbackResize<GameNodeVelocity>))]
[assembly: RegisterGenericJobType(typeof(RollbackResize<GameNodeDirection>))]

[assembly: RegisterGenericJobType(typeof(RollbackResize<GameNodeParent>))]

[assembly: RegisterGenericJobType(typeof(RollbackResize<GameNodeCharacterVelocity>))]

//[assembly: RegisterGenericJobType(typeof(RollbackResize<PhysicsVelocity>))]

#region GameNodePosition
[assembly: RegisterGenericJobType(typeof(RollbackSaveBufferCount<GameNodePosition>))]
[assembly: RegisterGenericJobType(typeof(RollbackSaveBufferResizeValues<GameNodePosition>))]
#endregion

#region GameNodeVelocityComponent
[assembly: RegisterGenericJobType(typeof(RollbackSaveBufferCount<GameNodeVelocityComponent>))]
[assembly: RegisterGenericJobType(typeof(RollbackSaveBufferResizeValues<GameNodeVelocityComponent>))]
#endregion

#region GameNodeSpeedScaleComponent
[assembly: RegisterGenericJobType(typeof(RollbackSaveBufferCount<GameNodeSpeedScaleComponent>))]
[assembly: RegisterGenericJobType(typeof(RollbackSaveBufferResizeValues<GameNodeSpeedScaleComponent>))]
#endregion

/*[AutoCreateIn("Client")]
public partial class GameNodeTransformRollbackSystem : RollbackSystemEx
{
    public struct Restore : IRollbackRestore
    {
        public ComponentRestoreFunction<Translation> translations;
        public ComponentRestoreFunction<Rotation> rotations;
        public ComponentRestoreFunction<GameNodeStatus> states;
        public ComponentRestoreFunction<GameNodeOldStatus> oldStates;
        public ComponentRestoreFunction<GameNodeDelay> delay;
        
        public void Execute(int index, int entityIndex, in Entity entity)
        {
            if (!states.IsExists(entity))
                return;
            
            translations.Invoke(entityIndex, entity);
            rotations.Invoke(entityIndex, entity);

            states.InvokeDiff(entityIndex, entity);
            oldStates.InvokeDiff(entityIndex, entity);

            delay.Invoke(entityIndex, entity);
        }
    }

    public struct Save : IRollbackSave
    {
        public ComponentSaveFunction<Translation> translations;
        public ComponentSaveFunction<Rotation> rotations;
        public ComponentSaveFunction<GameNodeStatus> states;
        public ComponentSaveFunction<GameNodeOldStatus> oldStates;
        public ComponentSaveFunction<GameNodeDelay> delay;

        public void Execute(in ArchetypeChunk chunk, int firstEntityIndex)
        {
            translations.Invoke(chunk, firstEntityIndex);
            rotations.Invoke(chunk, firstEntityIndex);
            states.Invoke(chunk, firstEntityIndex);
            oldStates.Invoke(chunk, firstEntityIndex);
            delay.Invoke(chunk, firstEntityIndex);
        }
    }

    public struct Clear : IRollbackClear
    {
        public ComponentClearFunction<Translation> translations;
        public ComponentClearFunction<Rotation> rotations;
        public ComponentClearFunction<GameNodeStatus> states;
        public ComponentClearFunction<GameNodeOldStatus> oldStates;
        public ComponentClearFunction<GameNodeDelay> delay;
        
        public void Remove(int fromIndex, int count)
        {
        }
        
        public void Move(int fromIndex, int toIndex, int count)
        {
            translations.Move(fromIndex, toIndex, count);
            rotations.Move(fromIndex, toIndex, count);
            states.Move(fromIndex, toIndex, count);
            oldStates.Move(fromIndex, toIndex, count);
            delay.Move(fromIndex, toIndex, count);
        }

        public void Resize(int count)
        {
            translations.Resize(count);
            rotations.Resize(count);
            states.Resize(count);
            oldStates.Resize(count);
            delay.Resize(count);
        }
    }

    private JobHandle __jobHandle;

    private EntityQuery __group;
    
    private Component<Translation> __translations;
    private Component<Rotation> __rotations;
    private Component<GameNodeStatus> __states;
    private Component<GameNodeOldStatus> __oldStates;
    private Component<GameNodeDelay> __delay;

    public int IndexOf(uint frameIndex, Entity entity)
    {
        UnityEngine.Profiling.Profiler.BeginSample("GameNodeSyncSystem.IndexOf");

        __jobHandle.Complete();

        int index =  _IndexOf(frameIndex, entity);

        UnityEngine.Profiling.Profiler.EndSample();

        return index;
    }

    public Translation GetTranslation(int index)
    {
        return __translations[index];
    }
    
    protected override void OnCreate()
    {
        base.OnCreate();
        
        __group = GetEntityQuery(
            ComponentType.ReadOnly<GameRollbackObject>(), 
            ComponentType.ReadOnly<Translation>(),
            ComponentType.ReadOnly<Rotation>(),
            ComponentType.ReadOnly<GameNodeStatus>(),
            ComponentType.ReadOnly<GameNodeOldStatus>(),
            ComponentType.ReadOnly<GameNodeDelay>());
        
        __translations = _GetComponent<Translation>();
        __rotations = _GetComponent<Rotation>();
        __states = _GetComponent<GameNodeStatus>();
        __oldStates = _GetComponent<GameNodeOldStatus>();
        __delay = _GetComponent<GameNodeDelay>();
    }
    
    protected override JobHandle OnUpdate(JobHandle inputDeps)
    {
        __jobHandle = base.OnUpdate(inputDeps);

        return __jobHandle;
    }

    protected override JobHandle _Restore(uint frameIndex, JobHandle inputDeps)
    {
        Restore restore;
        restore.translations = DelegateRestore(__translations);
        restore.rotations = DelegateRestore(__rotations);
        restore.states = DelegateRestore(__states);
        restore.oldStates = DelegateRestore(__oldStates);
        restore.delay = DelegateRestore(__delay);

        inputDeps = _Schedule(restore, frameIndex, inputDeps);
        
        return inputDeps;
    }

    protected override JobHandle _Save(uint frameIndex, EntityTypeHandle entityType, JobHandle inputDeps)
    {
        int entityCount = __group.CalculateEntityCount();
        if (entityCount < 1)
            return inputDeps;

        Save save;
        save.translations = DelegateSave(entityCount, __translations, inputDeps);
        save.rotations = DelegateSave(entityCount, __rotations, inputDeps);
        save.states = DelegateSave(entityCount, __states, inputDeps);
        save.oldStates = DelegateSave(entityCount, __oldStates, inputDeps);
        save.delay = DelegateSave(entityCount, __delay, inputDeps);

        return _Schedule(save, entityCount, frameIndex, entityType, __group, inputDeps);
    }

    protected override JobHandle _Clear(uint maxFrameIndex, uint frameIndex, uint frameCount, JobHandle inputDeps)
    {
        Clear clear;
        clear.translations = DelegateClear(__translations);
        clear.rotations = DelegateClear(__rotations);
        clear.states = DelegateClear(__states);
        clear.oldStates = DelegateClear(__oldStates);
        clear.delay = DelegateClear(__delay);

        return _Schedule(clear, maxFrameIndex, frameIndex, frameCount, inputDeps);
    }
}

[AutoCreateIn("Client"), UpdateBefore(typeof(GameNodeTransformRollbackSystem))]
public partial class GameNodeRootRollbackSystem : RollbackSystemEx
{
    public struct Restore : IRollbackRestore
    {
        [ReadOnly]
        public ComponentLookup<GameNodeData> instances;
        [ReadOnly]
        public ComponentLookup<GameNodeParent> parents;
        
        public ComponentRestoreFunction<GameNodeActorStatus> actorStates;
        public ComponentRestoreFunction<GameNodeCharacterStatus> characterStates;
        public ComponentRestoreFunction<GameNodeCharacterAngle> characterAngles;
        public ComponentRestoreFunction<GameNodeDirect> directs;
        public ComponentRestoreFunction<GameNodeAngle> angles;
        public ComponentRestoreFunction<GameNodeSurface> surfaces;
        public ComponentRestoreFunction<GameNodeVelocity> velocities;
        public ComponentRestoreFunction<GameNodeDirection> directions;
        public BufferRestoreFunction<GameNodePosition> positions;
        public BufferRestoreFunction<GameNodeVelocityComponent> velocityComponents;
        public BufferRestoreFunction<GameNodeSpeedScaleComponent> speedScaleComponents;

        public EntityCommandQueue<EntityCommandStructChange>.ParallelWriter removeComponentCommander;

        public void Execute(int index, int entityIndex, in Entity entity)
        {
            if (parents.HasComponent(entity))
            {
                EntityCommandStructChange command;
                command.componentType = ComponentType.ReadWrite<GameNodeParent>();
                command.entity = entity;
                removeComponentCommander.Enqueue(command);
            }

            if (instances.HasComponent(entity))
            {
                actorStates.Invoke(entityIndex, entity);
                characterStates.Invoke(entityIndex, entity);
                characterAngles.Invoke(entityIndex, entity);
                directs.Invoke(entityIndex, entity);
                angles.Invoke(entityIndex, entity);

                surfaces.Invoke(entityIndex, entity);

                velocities.Invoke(entityIndex, entity);

                velocityComponents.Invoke(entityIndex, entity);

                directions.Invoke(entityIndex, entity);

                positions.Invoke(entityIndex, entity);

                speedScaleComponents.Invoke(entityIndex, entity);
            }
        }
    }
    
    public struct Save : IRollbackSave
    {
        public ComponentSaveFunction<GameNodeActorStatus> actorStates;
        public ComponentSaveFunction<GameNodeCharacterStatus> characterStates;
        public ComponentSaveFunction<GameNodeCharacterAngle> characterAngles;
        public ComponentSaveFunction<GameNodeDirect> directs;
        public ComponentSaveFunction<GameNodeAngle> angles;
        public ComponentSaveFunction<GameNodeSurface> surfaces;
        public ComponentSaveFunction<GameNodeVelocity> velocities;
        public ComponentSaveFunction<GameNodeDirection> directions;
        public BufferSaveFunction<GameNodePosition> positions;
        public BufferSaveFunction<GameNodeVelocityComponent> velocityComponents;
        public BufferSaveFunction<GameNodeSpeedScaleComponent> speedScaleComponents;

        public void Execute(in ArchetypeChunk chunk, int firstEntityIndex)
        {
            actorStates.Invoke(chunk, firstEntityIndex);
            characterStates.Invoke(chunk, firstEntityIndex);
            characterAngles.Invoke(chunk, firstEntityIndex);
            directs.Invoke(chunk, firstEntityIndex);
            angles.Invoke(chunk, firstEntityIndex);
            surfaces.Invoke(chunk, firstEntityIndex);
            velocities.Invoke(chunk, firstEntityIndex);
            directions.Invoke(chunk, firstEntityIndex);
            positions.Invoke(chunk, firstEntityIndex);
            velocityComponents.Invoke(chunk, firstEntityIndex);
            speedScaleComponents.Invoke(chunk, firstEntityIndex);
        }
    }

    public struct Clear : IRollbackClear
    {
        public ComponentClearFunction<GameNodeActorStatus> actorStates;
        public ComponentClearFunction<GameNodeCharacterStatus> characterStates;
        public ComponentClearFunction<GameNodeCharacterAngle> characterAngles;
        public ComponentClearFunction<GameNodeDirect> directs;
        public ComponentClearFunction<GameNodeAngle> angles;
        public ComponentClearFunction<GameNodeSurface> surfaces;
        public ComponentClearFunction<GameNodeVelocity> velocities;
        public ComponentClearFunction<GameNodeDirection> directions;
        public BufferClearFunction<GameNodePosition> positions;
        public BufferClearFunction<GameNodeVelocityComponent> velocityComponents;
        public BufferClearFunction<GameNodeSpeedScaleComponent> speedScaleComponents;

        public void Remove(int fromIndex, int count)
        {
            positions.Remove(fromIndex, count);
            velocityComponents.Remove(fromIndex, count);
            speedScaleComponents.Remove(fromIndex, count);
        }
        
        public void Move(int fromIndex, int toIndex, int count)
        {
            actorStates.Move(fromIndex, toIndex, count);
            characterStates.Move(fromIndex, toIndex, count);
            characterAngles.Move(fromIndex, toIndex, count);
            directs.Move(fromIndex, toIndex, count);
            angles.Move(fromIndex, toIndex, count);
            surfaces.Move(fromIndex, toIndex, count);

            velocities.Move(fromIndex, toIndex, count);

            directions.Move(fromIndex, toIndex, count);
            positions.Move(fromIndex, toIndex, count);

            velocityComponents.Move(fromIndex, toIndex, count);
            speedScaleComponents.Move(fromIndex, toIndex, count);
        }

        public void Resize(int count)
        {
            actorStates.Resize(count);
            characterStates.Resize(count);
            characterAngles.Resize(count);
            directs.Resize(count);
            angles.Resize(count);
            surfaces.Resize(count);

            velocities.Resize(count);

            directions.Resize(count);
            positions.Resize(count);

            velocityComponents.Resize(count);
            speedScaleComponents.Resize(count);
        }
    }

    private EntityQuery __group;

    private Component<GameNodeActorStatus> __actorStates;
    private Component<GameNodeCharacterStatus> __characterStates;
    private Component<GameNodeCharacterAngle> __characterAngles;
    private Component<GameNodeDirect> __directs;
    private Component<GameNodeAngle> __angles;
    private Component<GameNodeSurface> __surfaces;
    private Component<GameNodeVelocity> __velocities;
    private Component<GameNodeDirection> __directions;
    private Buffer<GameNodePosition> __positions;
    private Buffer<GameNodeVelocityComponent> __velocityComponents;
    private Buffer<GameNodeSpeedScaleComponent> __speedScaleComponents;

    private EntityCommandPool<EntityCommandStructChange> __removeComponentCommander;

    protected override void OnCreate()
    {
        base.OnCreate();

        __group = GetEntityQuery(
            ComponentType.ReadOnly<GameRollbackObject>(), 
            ComponentType.ReadOnly<GameNodeData>(),
            ComponentType.Exclude<GameNodeParent>());

        __actorStates = _GetComponent<GameNodeActorStatus>();
        __characterStates = _GetComponent<GameNodeCharacterStatus>();
        __characterAngles = _GetComponent<GameNodeCharacterAngle>();
        __directs = _GetComponent<GameNodeDirect>();
        __angles = _GetComponent<GameNodeAngle>();
        __surfaces = _GetComponent<GameNodeSurface>();
        __velocities = _GetComponent<GameNodeVelocity>();
        __directions = _GetComponent<GameNodeDirection>();
        __positions = _GetBuffer<GameNodePosition>();
        __velocityComponents = _GetBuffer<GameNodeVelocityComponent>();
        __speedScaleComponents = _GetBuffer<GameNodeSpeedScaleComponent>();

        __removeComponentCommander = World.GetOrCreateSystem<EndRollbackSystemGroupStructChangeSystem>().Struct.manager.removeComponentPool;
    }

    protected override JobHandle _Restore(uint frameIndex, JobHandle inputDeps)
    {
        var removeComponentCommander = __removeComponentCommander.Create();

        Restore restore;
        restore.instances = GetComponentLookup<GameNodeData>(true);
        restore.parents = GetComponentLookup<GameNodeParent>(true);
        restore.actorStates = DelegateRestore(__actorStates);
        restore.characterStates = DelegateRestore(__characterStates);
        restore.characterAngles = DelegateRestore(__characterAngles);
        restore.directs = DelegateRestore(__directs);
        restore.angles = DelegateRestore(__angles);
        restore.surfaces = DelegateRestore(__surfaces);
        restore.velocities = DelegateRestore(__velocities);
        restore.directions = DelegateRestore(__directions);
        restore.positions = DelegateRestore(__positions);
        restore.velocityComponents = DelegateRestore(__velocityComponents);
        restore.speedScaleComponents = DelegateRestore(__speedScaleComponents);
        restore.removeComponentCommander = removeComponentCommander.parallelWriter;

        inputDeps = _Schedule(restore, frameIndex, inputDeps);
        
        removeComponentCommander.AddJobHandleForProducer(inputDeps);

        return inputDeps;
    }

    protected override JobHandle _Save(uint frameIndex, EntityTypeHandle entityType, JobHandle inputDeps)
    {
        int entityCount = __group.CalculateEntityCount();
        if (entityCount < 1)
            return inputDeps;

        Save save;
        save.actorStates = DelegateSave(entityCount, __actorStates, inputDeps);
        save.characterStates = DelegateSave(entityCount, __characterStates, inputDeps);
        save.characterAngles = DelegateSave(entityCount, __characterAngles, inputDeps);
        save.directs = DelegateSave(entityCount, __directs, inputDeps);
        save.angles = DelegateSave(entityCount, __angles, inputDeps);
        save.surfaces = DelegateSave(entityCount, __surfaces, inputDeps);
        save.velocities = DelegateSave(entityCount, __velocities, inputDeps);
        save.directions = DelegateSave(entityCount, __directions, inputDeps);
        save.positions = DelegateSave(entityCount, __positions, __group, inputDeps);
        save.velocityComponents = DelegateSave(entityCount, __velocityComponents, __group, inputDeps);
        save.speedScaleComponents = DelegateSave(entityCount, __speedScaleComponents, __group, inputDeps);

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
        clear.actorStates = DelegateClear(__actorStates);
        clear.characterStates = DelegateClear(__characterStates);
        clear.characterAngles = DelegateClear(__characterAngles);
        clear.directs = DelegateClear(__directs);
        clear.angles = DelegateClear(__angles);
        clear.surfaces = DelegateClear(__surfaces);
        clear.velocities = DelegateClear(__velocities);
        clear.directions = DelegateClear(__directions);
        clear.positions = DelegateClear(__positions);
        clear.velocityComponents = DelegateClear(__velocityComponents);
        clear.speedScaleComponents = DelegateClear(__speedScaleComponents);

        return _Schedule(clear, maxFrameIndex, frameIndex, frameCount, inputDeps);
    }
}

[AutoCreateIn("Client")]
public partial class GameNodeChildRollbackSystem : RollbackSystemEx
{
    public struct Restore : IRollbackRestore
    {
        [ReadOnly]
        public ComponentLookup<GameNodeData> instances;

        public ComponentRestoreFunction<GameNodeParent> parents;

        public EntityCommandQueue<EntityData<GameNodeParent>>.ParallelWriter addComponentDataCommander;

        public void Execute(int index, int entityIndex, in Entity entity)
        {
            if (!instances.HasComponent(entity))
                return;

            if (parents.IsExists(entity))
                parents.Invoke(entityIndex, entity);
            else
            {
                EntityData<GameNodeParent> command;
                command.entity = entity;
                command.value = parents[entityIndex];
                addComponentDataCommander.Enqueue(command);
            }
        }
    }
    
    public struct Save : IRollbackSave
    {
        public ComponentSaveFunction<GameNodeParent> parents;

        public void Execute(in ArchetypeChunk chunk, int firstEntityIndex)
        {
            parents.Invoke(chunk, firstEntityIndex);
        }
    }
    
    public struct Clear : IRollbackClear
    {
        public ComponentClearFunction<GameNodeParent> parents;
        
        public void Remove(int fromIndex, int count)
        {
        }
        
        public void Move(int fromIndex, int toIndex, int count)
        {
            parents.Move(fromIndex, toIndex, count);
        }

        public void Resize(int count)
        {
            parents.Resize(count);
        }
    }

    private Component<GameNodeParent> __parents;
    private EntityQuery __group;
    private EntityCommandPool<EntityData<GameNodeParent>> __addComponentDataCommander;

    protected override void OnCreate()
    {
        base.OnCreate();
        
        __parents = _GetComponent<GameNodeParent>();

        __group = GetEntityQuery(
            ComponentType.ReadOnly<GameRollbackObject>(),
            ComponentType.ReadOnly<GameNodeData>(),
            ComponentType.ReadOnly<GameNodeParent>());

        __addComponentDataCommander = World.GetOrCreateSystem<EndRollbackSystemGroupEntityCommandSystem>().CreateAddComponentDataCommander<GameNodeParent>();
    }
    
    protected override JobHandle _Restore(uint frameIndex, JobHandle inputDeps)
    {
        var addComponentDataCommander = __addComponentDataCommander.Create();

        Restore restore;
        restore.instances = GetComponentLookup<GameNodeData>(true);
        restore.parents = DelegateRestore(__parents);
        restore.addComponentDataCommander = addComponentDataCommander.parallelWriter;

        inputDeps = _Schedule(restore, frameIndex, inputDeps);

        addComponentDataCommander.AddJobHandleForProducer(inputDeps);

        return inputDeps;
    }

    protected override JobHandle _Save(uint frameIndex, EntityTypeHandle entityType, JobHandle inputDeps)
    {
        int entityCount = __group.CalculateEntityCount();

        Save save;
        save.parents = DelegateSave(entityCount, __parents, inputDeps);

        return _Schedule(save, entityCount, frameIndex, entityType, __group, inputDeps);
    }

    protected override JobHandle _Clear(uint maxFrameIndex, uint frameIndex, uint frameCount, JobHandle inputDeps)
    {
        Clear clear;
        clear.parents = DelegateClear(__parents);

        return _Schedule(clear, maxFrameIndex, frameIndex, frameCount, inputDeps);
    }
}

[AutoCreateIn("Client")]
public partial class GameNodeCharacterRollbackSystem : RollbackSystemEx
{
    public struct Restore : IRollbackRestore
    {
        public ComponentRestoreFunction<GameNodeCharacterVelocity> characterVelocities;

        public void Execute(int index, int entityIndex, in Entity entity)
        {
            if (characterVelocities.IsExists(entity))
                characterVelocities.Invoke(entityIndex, entity);
        }
    }

    public struct Save : IRollbackSave
    {
        public ComponentSaveFunction<GameNodeCharacterVelocity> characterVelocities;

        public void Execute(in ArchetypeChunk chunk, int firstEntityIndex)
        {
            characterVelocities.Invoke(chunk, firstEntityIndex);
        }
    }

    public struct Clear : IRollbackClear
    {
        public ComponentClearFunction<GameNodeCharacterVelocity> characterVelocities;
        
        public void Remove(int fromIndex, int count)
        {
        }
        
        public void Move(int fromIndex, int toIndex, int count)
        {
            characterVelocities.Move(fromIndex, toIndex, count);
        }

        public void Resize(int count)
        {
            characterVelocities.Resize(count);
        }
    }

    private Component<GameNodeCharacterVelocity> __characterVelocities;

    private EntityQuery __group;

    protected override void OnCreate()
    {
        base.OnCreate();

        __characterVelocities = _GetComponent<GameNodeCharacterVelocity>();

        __group = GetEntityQuery(
            ComponentType.ReadOnly<GameRollbackObject>(), 
            ComponentType.ReadOnly<GameNodeCharacterVelocity>());
    }
    
    protected override JobHandle _Restore(uint frameIndex, JobHandle inputDeps)
    {
        Restore restore;
        restore.characterVelocities = DelegateRestore(__characterVelocities);

        inputDeps = _Schedule(restore, frameIndex, inputDeps);

        return inputDeps;
    }

    protected override JobHandle _Save(uint frameIndex, EntityTypeHandle entityType, JobHandle inputDeps)
    {
        int entityCount = __group.CalculateEntityCount();

        Save save;
        save.characterVelocities = DelegateSave(entityCount, __characterVelocities, inputDeps);

        return _Schedule(save, entityCount, frameIndex, entityType, __group, inputDeps);
    }

    protected override JobHandle _Clear(uint maxFrameIndex, uint frameIndex, uint frameCount, JobHandle inputDeps)
    {
        Clear clear;
        clear.characterVelocities = DelegateClear(__characterVelocities);

        return _Schedule(clear, maxFrameIndex, frameIndex, frameCount, inputDeps);
    }
}

[AutoCreateIn("Client")]
public partial class GameNodePhysicsRollbackSystem : RollbackSystemEx
{
    public struct Restore : IRollbackRestore
    {
        public ComponentRestoreFunction<PhysicsVelocity> physicsVelocities;

        public void Execute(int index, int entityIndex, in Entity entity)
        {
            if (physicsVelocities.IsExists(entity))
                physicsVelocities.Invoke(entityIndex, entity);
        }
    }

    public struct Save : IRollbackSave
    {
        public ComponentSaveFunction<PhysicsVelocity> physicsVelocities;

        public void Execute(in ArchetypeChunk chunk, int firstEntityIndex)
        {
            physicsVelocities.Invoke(chunk, firstEntityIndex);
        }
    }

    public struct Clear : IRollbackClear
    {
        public ComponentClearFunction<PhysicsVelocity> physicsVelocities;

        public void Remove(int fromIndex, int count)
        {
        }

        public void Move(int fromIndex, int toIndex, int count)
        {
            physicsVelocities.Move(fromIndex, toIndex, count);
        }

        public void Resize(int count)
        {
            physicsVelocities.Resize(count);
        }
    }

    private Component<PhysicsVelocity> __physicsVelocities;

    private EntityQuery __group;

    protected override void OnCreate()
    {
        base.OnCreate();

        __physicsVelocities = _GetComponent<PhysicsVelocity>();

        __group = GetEntityQuery(
            ComponentType.ReadOnly<GameRollbackObject>(),
            ComponentType.ReadOnly<PhysicsVelocity>());
    }

    protected override JobHandle _Restore(uint frameIndex, JobHandle inputDeps)
    {
        Restore restore;
        restore.physicsVelocities = DelegateRestore(__physicsVelocities);

        inputDeps = _Schedule(restore, frameIndex, inputDeps);

        return inputDeps;
    }

    protected override JobHandle _Save(uint frameIndex, EntityTypeHandle entityType, JobHandle inputDeps)
    {
        int entityCount = __group.CalculateEntityCount();

        Save save;
        save.physicsVelocities = DelegateSave(entityCount, __physicsVelocities, inputDeps);

        return _Schedule(save, entityCount, frameIndex, entityType, __group, inputDeps);
    }

    protected override JobHandle _Clear(uint maxFrameIndex, uint frameIndex, uint frameCount, JobHandle inputDeps)
    {
        Clear clear;
        clear.physicsVelocities = DelegateClear(__physicsVelocities);

        return _Schedule(clear, maxFrameIndex, frameIndex, frameCount, inputDeps);
    }
}*/

[BurstCompile,
    CreateAfter(typeof(RollbackSystemGroup)),
    UpdateInGroup(typeof(RollbackSystemGroup)),
    AutoCreateIn("Client")]
public partial struct GameNodeTransformRollbackSystem : ISystem, IRollbackCore
{
    public struct Restore : IRollbackRestore
    {
        public RollbackComponentRestoreFunction<Translation> translations;
        public RollbackComponentRestoreFunction<Rotation> rotations;
        public RollbackComponentRestoreFunction<GameNodeStatus> states;
        public RollbackComponentRestoreFunction<GameNodeOldStatus> oldStates;
        public RollbackComponentRestoreFunction<GameNodeDelay> delay;

        public void Execute(int index, int entityIndex, in Entity entity)
        {
            if (!states.IsExists(entity))
                return;

            translations.Invoke(entityIndex, entity);
            rotations.Invoke(entityIndex, entity);

            states.InvokeDiff(entityIndex, entity);
            oldStates.InvokeDiff(entityIndex, entity);

            delay.Invoke(entityIndex, entity);
        }
    }

    public struct Save : IRollbackSave
    {
        public RollbackComponentSaveFunction<Translation> translations;
        public RollbackComponentSaveFunction<Rotation> rotations;
        public RollbackComponentSaveFunction<GameNodeStatus> states;
        public RollbackComponentSaveFunction<GameNodeOldStatus> oldStates;
        public RollbackComponentSaveFunction<GameNodeDelay> delay;

        public void Execute(in ArchetypeChunk chunk, int firstEntityIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            translations.Invoke(chunk, firstEntityIndex, useEnabledMask, chunkEnabledMask);
            rotations.Invoke(chunk, firstEntityIndex, useEnabledMask, chunkEnabledMask);
            states.Invoke(chunk, firstEntityIndex, useEnabledMask, chunkEnabledMask);
            oldStates.Invoke(chunk, firstEntityIndex, useEnabledMask, chunkEnabledMask);
            delay.Invoke(chunk, firstEntityIndex, useEnabledMask, chunkEnabledMask);
        }
    }

    public struct Clear : IRollbackClear
    {
        public RollbackComponentClearFunction<Translation> translations;
        public RollbackComponentClearFunction<Rotation> rotations;
        public RollbackComponentClearFunction<GameNodeStatus> states;
        public RollbackComponentClearFunction<GameNodeOldStatus> oldStates;
        public RollbackComponentClearFunction<GameNodeDelay> delay;

        public void Remove(int fromIndex, int count)
        {
        }

        public void Move(int fromIndex, int toIndex, int count)
        {
            translations.Move(fromIndex, toIndex, count);
            rotations.Move(fromIndex, toIndex, count);
            states.Move(fromIndex, toIndex, count);
            oldStates.Move(fromIndex, toIndex, count);
            delay.Move(fromIndex, toIndex, count);
        }

        public void Resize(int count)
        {
            translations.Resize(count);
            rotations.Resize(count);
            states.Resize(count);
            oldStates.Resize(count);
            delay.Resize(count);
        }
    }

    private JobHandle __jobHandle;

    private EntityQuery __group;

    private RollbackManager<Restore, Save, Clear> __manager;

    private RollbackComponent<Translation> __translations;
    private RollbackComponent<Rotation> __rotations;
    private RollbackComponent<GameNodeStatus> __states;
    private RollbackComponent<GameNodeOldStatus> __oldStates;
    private RollbackComponent<GameNodeDelay> __delay;

    public int IndexOf(uint frameIndex, Entity entity)
    {
        UnityEngine.Profiling.Profiler.BeginSample("GameNodeSyncSystem.IndexOf");

        __jobHandle.Complete();

        int index = __manager.IndexOf(frameIndex, entity);

        UnityEngine.Profiling.Profiler.EndSample();

        return index;
    }

    public Translation GetTranslation(int index)
    {
        return __translations[index];
    }

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        using(var componentTypes = new NativeList<ComponentType>(Allocator.Temp)
        {
            ComponentType.ReadOnly<RollbackObject>(),
            ComponentType.ReadOnly<Translation>(),
            ComponentType.ReadOnly<Rotation>(),
            ComponentType.ReadOnly<GameNodeStatus>(),
            ComponentType.ReadOnly<GameNodeOldStatus>(),
            ComponentType.ReadOnly<GameNodeDelay>()
        })
            __group = GameRollbackObjectIncludeDisabled.GetEntityQuery(componentTypes.AsArray(), ref state);

        var containerManager = state.WorldUnmanaged.GetExistingSystemUnmanaged<RollbackSystemGroup>().containerManager;

        __manager = containerManager.CreateManager<Restore, Save, Clear>(ref state);

        __translations = containerManager.CreateComponent<Translation>(ref state);
        __rotations = containerManager.CreateComponent<Rotation>(ref state);
        __states = containerManager.CreateComponent<GameNodeStatus>(ref state);
        __oldStates = containerManager.CreateComponent<GameNodeOldStatus>(ref state);
        __delay = containerManager.CreateComponent<GameNodeDelay>(ref state);
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
        restore.translations = __manager.DelegateRestore(__translations, ref state);
        restore.rotations = __manager.DelegateRestore(__rotations, ref state);
        restore.states = __manager.DelegateRestore(__states, ref state);
        restore.oldStates = __manager.DelegateRestore(__oldStates, ref state);
        restore.delay = __manager.DelegateRestore(__delay, ref state);

        state.Dependency = __manager.ScheduleParallel(restore, frameIndex, state.Dependency);
    }

    public void ScheduleSave(uint frameIndex, in EntityTypeHandle entityType, ref SystemState state)
    {
        var data = new RollbackSaveData(__group, ref state);

        Save save;
        save.translations = __manager.DelegateSave(__translations, ref data, ref state);
        save.rotations = __manager.DelegateSave(__rotations, ref data, ref state);
        save.states = __manager.DelegateSave(__states, ref data, ref state);
        save.oldStates = __manager.DelegateSave(__oldStates, ref data, ref state);
        save.delay = __manager.DelegateSave(__delay, ref data, ref state);

        state.Dependency = __manager.ScheduleParallel(save, frameIndex, entityType, __group, data);
    }

    public void ScheduleClear(uint maxFrameIndex, uint frameIndex, uint frameCount, ref SystemState state)
    {
        Clear clear;
        clear.translations = __manager.DelegateClear(__translations);
        clear.rotations = __manager.DelegateClear(__rotations);
        clear.states = __manager.DelegateClear(__states);
        clear.oldStates = __manager.DelegateClear(__oldStates);
        clear.delay = __manager.DelegateClear(__delay);

        state.Dependency = __manager.Schedule(clear, maxFrameIndex, frameIndex, frameCount, state.Dependency);
    }
}

//TODO: Why Before GameNodeTransformRollbackSystem?
//[AutoCreateIn("Client"), UpdateBefore(typeof(GameNodeTransformRollbackSystem))]
[BurstCompile,
    CreateAfter(typeof(RollbackSystemGroup)),
    UpdateInGroup(typeof(RollbackSystemGroup)),
    AutoCreateIn("Client")]
public partial struct GameNodeActorRollbackSystem : ISystem, IRollbackCore
{
    public struct Restore : IRollbackRestore
    {
        [ReadOnly]
        public ComponentLookup<GameNodeActorData> instances;

        public RollbackComponentRestoreFunction<GameNodeActorStatus> actorStates;
        public RollbackComponentRestoreFunction<GameNodeCharacterStatus> characterStates;
        public RollbackComponentRestoreFunction<GameNodeCharacterAngle> characterAngles;
        public RollbackComponentRestoreFunction<GameNodeDirect> directs;
        public RollbackComponentRestoreFunction<GameNodeAngle> angles;
        public RollbackComponentRestoreFunction<GameNodeSurface> surfaces;
        public RollbackComponentRestoreFunction<GameNodeVelocity> velocities;
        public RollbackComponentRestoreFunction<GameNodeDirection> directions;
        public RollbackBufferRestoreFunction<GameNodePosition> positions;
        public RollbackBufferRestoreFunction<GameNodeVelocityComponent> velocityComponents;
        public RollbackBufferRestoreFunction<GameNodeSpeedScaleComponent> speedScaleComponents;

        public void Execute(int index, int entityIndex, in Entity entity)
        {
            if (!instances.HasComponent(entity))
                return;

            actorStates.Invoke(entityIndex, entity);
            characterStates.Invoke(entityIndex, entity);
            characterAngles.Invoke(entityIndex, entity);
            directs.Invoke(entityIndex, entity);
            angles.Invoke(entityIndex, entity);

            surfaces.Invoke(entityIndex, entity);

            velocities.Invoke(entityIndex, entity);

            velocityComponents.Invoke(entityIndex, entity);

            directions.Invoke(entityIndex, entity);

            positions.Invoke(entityIndex, entity);

            speedScaleComponents.Invoke(entityIndex, entity);
        }
    }

    public struct Save : IRollbackSave
    {
        public RollbackComponentSaveFunction<GameNodeActorStatus> actorStates;
        public RollbackComponentSaveFunction<GameNodeCharacterStatus> characterStates;
        public RollbackComponentSaveFunction<GameNodeCharacterAngle> characterAngles;
        public RollbackComponentSaveFunction<GameNodeDirect> directs;
        public RollbackComponentSaveFunction<GameNodeAngle> angles;
        public RollbackComponentSaveFunction<GameNodeSurface> surfaces;
        public RollbackComponentSaveFunction<GameNodeVelocity> velocities;
        public RollbackComponentSaveFunction<GameNodeDirection> directions;
        public RollbackBufferSaveFunction<GameNodePosition> positions;
        public RollbackBufferSaveFunction<GameNodeVelocityComponent> velocityComponents;
        public RollbackBufferSaveFunction<GameNodeSpeedScaleComponent> speedScaleComponents;

        public void Execute(in ArchetypeChunk chunk, int firstEntityIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            actorStates.Invoke(chunk, firstEntityIndex, useEnabledMask, chunkEnabledMask);
            characterStates.Invoke(chunk, firstEntityIndex, useEnabledMask, chunkEnabledMask);
            characterAngles.Invoke(chunk, firstEntityIndex, useEnabledMask, chunkEnabledMask);
            directs.Invoke(chunk, firstEntityIndex, useEnabledMask, chunkEnabledMask);
            angles.Invoke(chunk, firstEntityIndex, useEnabledMask, chunkEnabledMask);
            surfaces.Invoke(chunk, firstEntityIndex, useEnabledMask, chunkEnabledMask);
            velocities.Invoke(chunk, firstEntityIndex, useEnabledMask, chunkEnabledMask);
            directions.Invoke(chunk, firstEntityIndex, useEnabledMask, chunkEnabledMask);
            positions.Invoke(chunk, firstEntityIndex, useEnabledMask, chunkEnabledMask);
            velocityComponents.Invoke(chunk, firstEntityIndex, useEnabledMask, chunkEnabledMask);
            speedScaleComponents.Invoke(chunk, firstEntityIndex, useEnabledMask, chunkEnabledMask);
        }
    }

    public struct Clear : IRollbackClear
    {
        public RollbackComponentClearFunction<GameNodeActorStatus> actorStates;
        public RollbackComponentClearFunction<GameNodeCharacterStatus> characterStates;
        public RollbackComponentClearFunction<GameNodeCharacterAngle> characterAngles;
        public RollbackComponentClearFunction<GameNodeDirect> directs;
        public RollbackComponentClearFunction<GameNodeAngle> angles;
        public RollbackComponentClearFunction<GameNodeSurface> surfaces;
        public RollbackComponentClearFunction<GameNodeVelocity> velocities;
        public RollbackComponentClearFunction<GameNodeDirection> directions;
        public RollbackBufferClearFunction<GameNodePosition> positions;
        public RollbackBufferClearFunction<GameNodeVelocityComponent> velocityComponents;
        public RollbackBufferClearFunction<GameNodeSpeedScaleComponent> speedScaleComponents;

        public void Remove(int fromIndex, int count)
        {
            positions.Remove(fromIndex, count);
            velocityComponents.Remove(fromIndex, count);
            speedScaleComponents.Remove(fromIndex, count);
        }

        public void Move(int fromIndex, int toIndex, int count)
        {
            actorStates.Move(fromIndex, toIndex, count);
            characterStates.Move(fromIndex, toIndex, count);
            characterAngles.Move(fromIndex, toIndex, count);
            directs.Move(fromIndex, toIndex, count);
            angles.Move(fromIndex, toIndex, count);
            surfaces.Move(fromIndex, toIndex, count);

            velocities.Move(fromIndex, toIndex, count);

            directions.Move(fromIndex, toIndex, count);
            positions.Move(fromIndex, toIndex, count);

            velocityComponents.Move(fromIndex, toIndex, count);
            speedScaleComponents.Move(fromIndex, toIndex, count);
        }

        public void Resize(int count)
        {
            actorStates.Resize(count);
            characterStates.Resize(count);
            characterAngles.Resize(count);
            directs.Resize(count);
            angles.Resize(count);
            surfaces.Resize(count);

            velocities.Resize(count);

            directions.Resize(count);
            positions.Resize(count);

            velocityComponents.Resize(count);
            speedScaleComponents.Resize(count);
        }
    }

    private EntityQuery __group;
    private RollbackManager<Restore, Save, Clear> __manager;

    private RollbackComponent<GameNodeActorStatus> __actorStates;
    private RollbackComponent<GameNodeCharacterStatus> __characterStates;
    private RollbackComponent<GameNodeCharacterAngle> __characterAngles;
    private RollbackComponent<GameNodeDirect> __directs;
    private RollbackComponent<GameNodeAngle> __angles;
    private RollbackComponent<GameNodeSurface> __surfaces;
    private RollbackComponent<GameNodeVelocity> __velocities;
    private RollbackComponent<GameNodeDirection> __directions;
    private RollbackBuffer<GameNodePosition> __positions;
    private RollbackBuffer<GameNodeVelocityComponent> __velocityComponents;
    private RollbackBuffer<GameNodeSpeedScaleComponent> __speedScaleComponents;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        using (var componentTypes = new NativeList<ComponentType>(Allocator.Temp)
        {
            ComponentType.ReadOnly<RollbackObject>(),
            ComponentType.ReadOnly<GameNodeActorData>()/*,
            ComponentType.Exclude<GameNodeParent>()*/
        })
            __group = GameRollbackObjectIncludeDisabled.GetEntityQuery(componentTypes.AsArray(), ref state);

        var containerManager = state.WorldUnmanaged.GetExistingSystemUnmanaged<RollbackSystemGroup>().containerManager;

        __manager = containerManager.CreateManager<Restore, Save, Clear>(ref state);

        __actorStates = containerManager.CreateComponent<GameNodeActorStatus>(ref state);
        __characterStates = containerManager.CreateComponent<GameNodeCharacterStatus>(ref state);
        __characterAngles = containerManager.CreateComponent<GameNodeCharacterAngle>(ref state);
        __directs = containerManager.CreateComponent<GameNodeDirect>(ref state);
        __angles = containerManager.CreateComponent<GameNodeAngle>(ref state);
        __surfaces = containerManager.CreateComponent<GameNodeSurface>(ref state);
        __velocities = containerManager.CreateComponent<GameNodeVelocity>(ref state);
        __directions = containerManager.CreateComponent<GameNodeDirection>(ref state);
        __positions = containerManager.CreateBuffer<GameNodePosition>(ref state);
        __velocityComponents = containerManager.CreateBuffer<GameNodeVelocityComponent>(ref state);
        __speedScaleComponents = containerManager.CreateBuffer<GameNodeSpeedScaleComponent>(ref state);
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
        restore.instances = state.GetComponentLookup<GameNodeActorData>(true);
        restore.actorStates = __manager.DelegateRestore(__actorStates, ref state);
        restore.characterStates = __manager.DelegateRestore(__characterStates, ref state);
        restore.characterAngles = __manager.DelegateRestore(__characterAngles, ref state);
        restore.directs = __manager.DelegateRestore(__directs, ref state);
        restore.angles = __manager.DelegateRestore(__angles, ref state);
        restore.surfaces = __manager.DelegateRestore(__surfaces, ref state);
        restore.velocities = __manager.DelegateRestore(__velocities, ref state);
        restore.directions = __manager.DelegateRestore(__directions, ref state);
        restore.positions = __manager.DelegateRestore(__positions, ref state);
        restore.velocityComponents = __manager.DelegateRestore(__velocityComponents, ref state);
        restore.speedScaleComponents = __manager.DelegateRestore(__speedScaleComponents, ref state);

        state.Dependency = __manager.ScheduleParallel(restore, frameIndex, state.Dependency);
    }

    public void ScheduleSave(uint frameIndex, in EntityTypeHandle entityType, ref SystemState state)
    {
        var data = new RollbackSaveData(__group, ref state);

        Save save;
        save.actorStates = __manager.DelegateSave(__actorStates, ref data, ref state);
        save.characterStates = __manager.DelegateSave(__characterStates, ref data, ref state);
        save.characterAngles = __manager.DelegateSave(__characterAngles, ref data, ref state);
        save.directs = __manager.DelegateSave(__directs, ref data, ref state);
        save.angles = __manager.DelegateSave(__angles, ref data, ref state);
        save.surfaces = __manager.DelegateSave(__surfaces, ref data, ref state);
        save.velocities = __manager.DelegateSave(__velocities, ref data, ref state);
        save.directions = __manager.DelegateSave(__directions, ref data, ref state);
        save.positions = __manager.DelegateSave(__positions, __group, ref data, ref state);
        save.velocityComponents = __manager.DelegateSave(__velocityComponents, __group, ref data, ref state);
        save.speedScaleComponents = __manager.DelegateSave(__speedScaleComponents, __group, ref data, ref state);

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
        clear.actorStates = __manager.DelegateClear(__actorStates);
        clear.characterStates = __manager.DelegateClear(__characterStates);
        clear.characterAngles = __manager.DelegateClear(__characterAngles);
        clear.directs = __manager.DelegateClear(__directs);
        clear.angles = __manager.DelegateClear(__angles);
        clear.surfaces = __manager.DelegateClear(__surfaces);
        clear.velocities = __manager.DelegateClear(__velocities);
        clear.directions = __manager.DelegateClear(__directions);
        clear.positions = __manager.DelegateClear(__positions);
        clear.velocityComponents = __manager.DelegateClear(__velocityComponents);
        clear.speedScaleComponents = __manager.DelegateClear(__speedScaleComponents);

        state.Dependency = __manager.Schedule(clear, maxFrameIndex, frameIndex, frameCount, state.Dependency);
    }
}

[BurstCompile,
    CreateAfter(typeof(EndRollbackSystemGroupStructChangeSystem)),
    CreateAfter(typeof(RollbackSystemGroup)),
    UpdateInGroup(typeof(RollbackSystemGroup)),
    AutoCreateIn("Client")]
public partial struct GameNodeRootRollbackSystem : ISystem, IRollbackCore
{
    public struct Restore : IRollbackRestore, IEntityCommandProducerJob
    {
        [ReadOnly]
        public ComponentLookup<GameNodeSpeed> instances;
        [ReadOnly]
        public ComponentLookup<GameNodeParent> parents;

        public EntityCommandQueue<EntityCommandStructChange>.ParallelWriter removeComponentCommander;

        public void Execute(int index, int entityIndex, in Entity entity)
        {
            if (parents.HasComponent(entity))
            {
                EntityCommandStructChange command;
                command.componentType = ComponentType.ReadWrite<GameNodeParent>();
                command.entity = entity;
                removeComponentCommander.Enqueue(command);
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

    private EntityCommandPool<EntityCommandStructChange> __removeComponentCommander;

    private RollbackManager<Restore, Save, Clear> __manager;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __group = builder
                    .WithAll<RollbackObject, GameNodeSpeed>()
                    .WithNone<GameNodeParent>()
                    .Build(ref state);

        var world = state.WorldUnmanaged;

        __removeComponentCommander = world.GetExistingSystemUnmanaged<EndRollbackSystemGroupStructChangeSystem>().manager.removeComponentPool;

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
        var removeComponentCommander = __removeComponentCommander.Create();

        Restore restore;
        restore.instances = state.GetComponentLookup<GameNodeSpeed>(true);
        restore.parents = state.GetComponentLookup<GameNodeParent>(true);
        restore.removeComponentCommander = removeComponentCommander.parallelWriter;

        var jobHandle = __manager.ScheduleParallel(restore, frameIndex, state.Dependency);

        removeComponentCommander.AddJobHandleForProducer<Restore>(jobHandle);

        state.Dependency = jobHandle;
    }

    public void ScheduleSave(uint frameIndex, in EntityTypeHandle entityType, ref SystemState state)
    {
        var data = new RollbackSaveData(__group, ref state);

        Save save;
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
        state.Dependency = __manager.Schedule(clear, maxFrameIndex, frameIndex, frameCount, state.Dependency);
    }
}

[BurstCompile,
    CreateAfter(typeof(EndRollbackSystemGroupStructChangeSystem)),
    CreateAfter(typeof(RollbackSystemGroup)),
    UpdateInGroup(typeof(RollbackSystemGroup)),
    AutoCreateIn("Client")]
public partial struct GameNodeChildRollbackSystem : ISystem, IRollbackCore
{
    public struct Restore : IRollbackRestore, IEntityCommandProducerJob
    {
        [ReadOnly]
        public ComponentLookup<GameNodeSpeed> instances;

        public RollbackComponentRestoreFunction<GameNodeParent> parents;

        public EntityAddDataQueue.ParallelWriter entityManager;

        public void Execute(int index, int entityIndex, in Entity entity)
        {
            if (!instances.HasComponent(entity))
                return;

            if (parents.IsExists(entity))
                parents.Invoke(entityIndex, entity);
            else
                entityManager.AddComponentData(entity, parents[entityIndex]);
        }
    }

    public struct Save : IRollbackSave
    {
        public RollbackComponentSaveFunction<GameNodeParent> parents;

        public void Execute(in ArchetypeChunk chunk, int firstEntityIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            parents.Invoke(chunk, firstEntityIndex, useEnabledMask, chunkEnabledMask);
        }
    }

    public struct Clear : IRollbackClear
    {
        public RollbackComponentClearFunction<GameNodeParent> parents;

        public void Remove(int fromIndex, int count)
        {
        }

        public void Move(int fromIndex, int toIndex, int count)
        {
            parents.Move(fromIndex, toIndex, count);
        }

        public void Resize(int count)
        {
            parents.Resize(count);
        }
    }

    private EntityQuery __group;
    private EntityAddDataPool __entityManager;
    private RollbackComponent<GameNodeParent> __parents;
    private RollbackManager<Restore, Save, Clear> __manager;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __group = builder
                .WithAll<RollbackObject, GameNodeSpeed, GameNodeParent>()
                .Build(ref state);

        var world = state.WorldUnmanaged;
        __entityManager = world.GetExistingSystemUnmanaged<EndRollbackSystemGroupStructChangeSystem>().addDataCommander;

        var containerManager = world.GetExistingSystemUnmanaged<RollbackSystemGroup>().containerManager;

        __manager = containerManager.CreateManager<Restore, Save, Clear>(ref state);

        __parents = containerManager.CreateComponent<GameNodeParent>(ref state);
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
        var entityManager = __entityManager.Create();

        var jobHandle = state.Dependency;

        jobHandle = __manager.GetChunk(frameIndex, jobHandle);

        Restore restore;
        restore.instances = state.GetComponentLookup<GameNodeSpeed>(true);
        restore.parents = __manager.DelegateRestore(__parents, ref state);
        restore.entityManager = entityManager.AsComponentParallelWriter<GameNodeParent>(__manager.countAndStartIndex, ref jobHandle);

        jobHandle = __manager.ScheduleParallel(restore, frameIndex, jobHandle);

        entityManager.AddJobHandleForProducer<Restore>(jobHandle);

        state.Dependency = jobHandle;
    }

    public void ScheduleSave(uint frameIndex, in EntityTypeHandle entityType, ref SystemState state)
    {
        var data = new RollbackSaveData(__group, ref state);

        Save save;
        save.parents = __manager.DelegateSave(__parents, ref data, ref state);

        state.Dependency = __manager.ScheduleParallel(save, frameIndex, entityType, __group, data);
    }

    public void ScheduleClear(uint maxFrameIndex, uint frameIndex, uint frameCount, ref SystemState state)
    {
        Clear clear;
        clear.parents = __manager.DelegateClear(__parents);

        state.Dependency = __manager.Schedule(clear, maxFrameIndex, frameIndex, frameCount, state.Dependency);
    }
}

[BurstCompile,
    CreateAfter(typeof(RollbackSystemGroup)),
    UpdateInGroup(typeof(RollbackSystemGroup)),
    AutoCreateIn("Client")]
public partial struct GameNodeCharacterRollbackSystem : ISystem, IRollbackCore
{
    public struct Restore : IRollbackRestore
    {
        public RollbackComponentRestoreFunction<GameNodeCharacterVelocity> characterVelocities;

        public void Execute(int index, int entityIndex, in Entity entity)
        {
            if (characterVelocities.IsExists(entity))
                characterVelocities.Invoke(entityIndex, entity);
        }
    }

    public struct Save : IRollbackSave
    {
        public RollbackComponentSaveFunction<GameNodeCharacterVelocity> characterVelocities;

        public void Execute(in ArchetypeChunk chunk, int firstEntityIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            characterVelocities.Invoke(chunk, firstEntityIndex, useEnabledMask, chunkEnabledMask);
        }
    }

    public struct Clear : IRollbackClear
    {
        public RollbackComponentClearFunction<GameNodeCharacterVelocity> characterVelocities;

        public void Remove(int fromIndex, int count)
        {
        }

        public void Move(int fromIndex, int toIndex, int count)
        {
            characterVelocities.Move(fromIndex, toIndex, count);
        }

        public void Resize(int count)
        {
            characterVelocities.Resize(count);
        }
    }

    private EntityQuery __group;

    private RollbackComponent<GameNodeCharacterVelocity> __characterVelocities;

    private RollbackManager<Restore, Save, Clear> __manager;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        using(var builder = new EntityQueryBuilder(Allocator.Temp))
            __group = builder
                    .WithAll<RollbackObject, GameNodeCharacterVelocity>()
                    .Build(ref state);

        var containerManager = state.WorldUnmanaged.GetExistingSystemUnmanaged<RollbackSystemGroup>().containerManager;

        __manager = containerManager.CreateManager<Restore, Save, Clear>(ref state);

        __characterVelocities = containerManager.CreateComponent<GameNodeCharacterVelocity>(ref state);
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
        restore.characterVelocities = __manager.DelegateRestore(__characterVelocities, ref state);

        state.Dependency = __manager.ScheduleParallel(restore, frameIndex, state.Dependency);
    }

    public void ScheduleSave(uint frameIndex, in EntityTypeHandle entityType, ref SystemState state)
    {
        var data = new RollbackSaveData(__group, ref state);

        Save save;
        save.characterVelocities = __manager.DelegateSave(__characterVelocities, ref data, ref state);

        state.Dependency = __manager.ScheduleParallel(save, frameIndex, entityType, __group, data);
    }

    public void ScheduleClear(uint maxFrameIndex, uint frameIndex, uint frameCount, ref SystemState state)
    {
        Clear clear;
        clear.characterVelocities = __manager.DelegateClear(__characterVelocities);

        state.Dependency = __manager.Schedule(clear, maxFrameIndex, frameIndex, frameCount, state.Dependency);
    }
}

[BurstCompile,
    CreateAfter(typeof(RollbackSystemGroup)),
    UpdateInGroup(typeof(RollbackSystemGroup)),
    AutoCreateIn("Client")]
public partial struct GameNodePhysicsRollbackSystem : ISystem, IRollbackCore
{
    public struct Restore : IRollbackRestore
    {
        public RollbackComponentRestoreFunction<PhysicsVelocity> physicsVelocities;

        public void Execute(int index, int entityIndex, in Entity entity)
        {
            if (physicsVelocities.IsExists(entity))
                physicsVelocities.Invoke(entityIndex, entity);
        }
    }

    public struct Save : IRollbackSave
    {
        public RollbackComponentSaveFunction<PhysicsVelocity> physicsVelocities;

        public void Execute(in ArchetypeChunk chunk, int firstEntityIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            physicsVelocities.Invoke(chunk, firstEntityIndex, useEnabledMask, chunkEnabledMask);
        }
    }

    public struct Clear : IRollbackClear
    {
        public RollbackComponentClearFunction<PhysicsVelocity> physicsVelocities;

        public void Remove(int fromIndex, int count)
        {
        }

        public void Move(int fromIndex, int toIndex, int count)
        {
            physicsVelocities.Move(fromIndex, toIndex, count);
        }

        public void Resize(int count)
        {
            physicsVelocities.Resize(count);
        }
    }

    private EntityQuery __group;

    private RollbackManager<Restore, Save, Clear> __manager;

    private RollbackComponent<PhysicsVelocity> __physicsVelocities;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __group = builder
                    .WithAll<RollbackObject, PhysicsVelocity>()
                    .Build(ref state);

        var containerManager = state.WorldUnmanaged.GetExistingSystemUnmanaged<RollbackSystemGroup>().containerManager;

        __manager = containerManager.CreateManager<Restore, Save, Clear>(ref state);

        __physicsVelocities = containerManager.CreateComponent<PhysicsVelocity>(ref state);
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
        restore.physicsVelocities = __manager.DelegateRestore(__physicsVelocities, ref state);

        state.Dependency = __manager.ScheduleParallel(restore, frameIndex, state.Dependency);
    }

    public void ScheduleSave(uint frameIndex, in EntityTypeHandle entityType, ref SystemState state)
    {
        var data = new RollbackSaveData(__group, ref state);

        Save save;
        save.physicsVelocities = __manager.DelegateSave(__physicsVelocities, ref data, ref state);

        state.Dependency = __manager.ScheduleParallel(save, frameIndex, entityType, __group, data);
    }

    public void ScheduleClear(uint maxFrameIndex, uint frameIndex, uint frameCount, ref SystemState state)
    {
        Clear clear;
        clear.physicsVelocities = __manager.DelegateClear(__physicsVelocities);

        state.Dependency = __manager.Schedule(clear, maxFrameIndex, frameIndex, frameCount, state.Dependency);
    }
}