using Unity.Jobs;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Entities;
using Unity.Collections;
using Unity.Transforms;
using Unity.Physics;
using ZG;

[assembly: RegisterGenericJobType(typeof(RollbackRestore<GameEntityRollbackSystem.Restore>))]
[assembly: RegisterGenericJobType(typeof(RollbackSave<GameEntityRollbackSystem.Save>))]
[assembly: RegisterGenericJobType(typeof(RollbackClear<GameEntityRollbackSystem.Clear>))]

[assembly: RegisterGenericJobType(typeof(RollbackRestore<GameEntityRageRollbackSystem.Restore>))]
[assembly: RegisterGenericJobType(typeof(RollbackSave<GameEntityRageRollbackSystem.Save>))]
[assembly: RegisterGenericJobType(typeof(RollbackClear<GameEntityRageRollbackSystem.Clear>))]

[assembly: RegisterGenericJobType(typeof(RollbackRestoreSingle<GameEntityActorRollbackSystem.Restore>))]
[assembly: RegisterGenericJobType(typeof(RollbackSave<GameEntityActorRollbackSystem.Save>))]
[assembly: RegisterGenericJobType(typeof(RollbackClear<GameEntityActorRollbackSystem.Clear>))]

[assembly: RegisterGenericJobType(typeof(RollbackRestore<GameActionRollbackSystem.Restore>))]
[assembly: RegisterGenericJobType(typeof(RollbackSave<GameActionRollbackSystem.Save>))]
[assembly: RegisterGenericJobType(typeof(RollbackClear<GameActionRollbackSystem.Clear>))]

[assembly: RegisterGenericJobType(typeof(RollbackResize<GameEntityRage>))]

[assembly: RegisterGenericJobType(typeof(RollbackResize<GameEntityCamp>))]
[assembly: RegisterGenericJobType(typeof(RollbackResize<GameEntityBreakInfo>))]
[assembly: RegisterGenericJobType(typeof(RollbackResize<GameEntityEventInfo>))]
[assembly: RegisterGenericJobType(typeof(RollbackResize<GameEntityActionInfo>))]
[assembly: RegisterGenericJobType(typeof(RollbackResize<GameEntityActorInfo>))]
[assembly: RegisterGenericJobType(typeof(RollbackResize<GameEntityActorTime>))]
[assembly: RegisterGenericJobType(typeof(RollbackResize<GameEntityActorHit>))]
[assembly: RegisterGenericJobType(typeof(RollbackResize<GameEntityHit>))]

[assembly: RegisterGenericJobType(typeof(RollbackResize<Translation>))]
[assembly: RegisterGenericJobType(typeof(RollbackResize<Rotation>))]
[assembly: RegisterGenericJobType(typeof(RollbackResize<PhysicsVelocity>))]
[assembly: RegisterGenericJobType(typeof(RollbackResize<PhysicsGravityFactor>))]
[assembly: RegisterGenericJobType(typeof(RollbackResize<GameActionData>))]
[assembly: RegisterGenericJobType(typeof(RollbackResize<GameActionDataEx>))]
[assembly: RegisterGenericJobType(typeof(RollbackResize<GameActionStatus>))]

#region GameEntityItem
[assembly: RegisterGenericJobType(typeof(RollbackSaveBufferCount<GameEntityItem>))]
[assembly: RegisterGenericJobType(typeof(RollbackSaveBufferResizeValues<GameEntityItem>))]
#endregion

#region GameEntityActorActionInfo
[assembly: RegisterGenericJobType(typeof(RollbackSaveBufferCount<GameEntityActorActionInfo>))]
[assembly: RegisterGenericJobType(typeof(RollbackSaveBufferResizeValues<GameEntityActorActionInfo>))]
#endregion

#region GameActionEntity
[assembly: RegisterGenericJobType(typeof(RollbackSaveBufferCount<GameActionEntity>))]
[assembly: RegisterGenericJobType(typeof(RollbackSaveBufferResizeValues<GameActionEntity>))]
#endregion

public struct GameActionRollbackCreateCommand
{
    public int entityIndex;

    public Translation translation;
    public Rotation rotation;
    public PhysicsVelocity physicsVelocity;
    public PhysicsGravityFactor physicsGravityFactor;
    public GameActionData instance;
    public GameActionDataEx instanceEx;
    public GameActionStatus status;
}

[BurstCompile,
    CreateAfter(typeof(RollbackSystemGroup)),
    UpdateInGroup(typeof(EndRollbackSystemGroupEntityCommandSystemGroup)),
    AutoCreateIn("Client")]
public partial struct GameActionRollbackFactroySystem : ISystem
{
    public RollbackBuffer<GameActionEntity> entities
    {
        get;

        private set;
    }

    public SharedMultiHashMap<EntityArchetype, GameActionRollbackCreateCommand> commands
    {
        get;

        private set;
    }

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        commands = new SharedMultiHashMap<EntityArchetype, GameActionRollbackCreateCommand>(Allocator.Persistent);

        entities = state.WorldUnmanaged.GetExistingSystemUnmanaged<RollbackSystemGroup>().containerManager.CreateBuffer<GameActionEntity>(ref state);
    }

    //[BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        commands.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var entityManager = state.EntityManager;

        var commands = this.commands;
        commands.lookupJobManager.CompleteReadWriteDependency();

        var writer = commands.writer;
        if (writer.isEmpty)
            return;

        using (var keys = writer.GetKeyArray(Allocator.Temp))
        {
            int numKeys = keys.ConvertToUniqueArray(), index;
            GameEntityAction action;
            EntityArchetype key;
            NativeParallelMultiHashMapIterator<EntityArchetype> iterator;
            NativeArray<Entity> entities;
            for (int i = 0; i < numKeys; ++i)
            {
                key = keys[i];
                if (writer.TryGetFirstValue(key, out var command, out iterator))
                {
                    using (entities = new NativeArray<Entity>(writer.CountValuesForKey(key), Allocator.Temp, NativeArrayOptions.UninitializedMemory))
                    {
                        index = 0;

                        entityManager.CreateEntity(key, entities);

                        do
                        {
                            action.entity = entities[index++];

                            if (entityManager.HasComponent<GameEntityAction>(command.instance.entity))
                            {
                                entityManager.GetBuffer<GameEntityAction>(command.instance.entity).Add(action);

                                //UnityEngine.Debug.Log("Fuck:" + entityManager.GetBuffer<GameEntityAction>(command.instance.entity).Length);
                            }

                            entityManager.SetComponentData(action.entity, command.translation);
                            entityManager.SetComponentData(action.entity, command.rotation);

                            entityManager.SetComponentData(action.entity, command.physicsVelocity);

                            entityManager.SetComponentData(action.entity, command.physicsGravityFactor);

                            entityManager.SetComponentData(action.entity, command.instance);

                            entityManager.SetComponentData(action.entity, command.instanceEx);

                            entityManager.SetComponentData(action.entity, command.status);

                            this.entities.CopyTo(command.entityIndex, entityManager.GetBuffer<GameActionEntity>(action.entity));

                            //UnityEngine.Debug.Log("Create: " + command.instance.entity + command.instance.index + ":" + entityManager.GetBuffer<GameActionEntity>(entity).Length);
                        } while (writer.TryGetNextValue(out command, ref iterator));
                    }
                }
            }
        }

        writer.Clear();
    }
}

/*[AutoCreateIn("Client")]
public partial class GameEntityRollbackSystem : RollbackSystemEx
{
    public struct Restore : IRollbackRestore
    {
        public ComponentRestoreFunction<GameEntityCamp> camps;

        public void Execute(int index, int entityIndex, in Entity entity)
        {
            if(camps.IsExists(entity))
                camps.Invoke(entityIndex, entity);
        }
    }

    public struct Save : IRollbackSave
    {
        public ComponentSaveFunction<GameEntityCamp> camps;

        public void Execute(in ArchetypeChunk chunk, int firstEntityIndex)
        {
            camps.Invoke(chunk, firstEntityIndex);
        }
    }

    public struct Clear : IRollbackClear
    {
        public ComponentClearFunction<GameEntityCamp> camps;

        public void Remove(int fromIndex, int count)
        {
        }

        public void Move(int fromIndex, int toIndex, int count)
        {
            camps.Move(fromIndex, toIndex, count);
        }

        public void Resize(int count)
        {
            camps.Resize(count);
        }
    }

    private EntityQuery __group;

    private Component<GameEntityCamp> __camps;

    protected override void OnCreate()
    {
        base.OnCreate();

        __group = GetEntityQuery(
            ComponentType.ReadOnly<GameRollbackObject>(),
            ComponentType.ReadOnly<GameEntityCamp>());
        
        __camps = _GetComponent<GameEntityCamp>();
    }
    
    protected override JobHandle _Restore(uint frameIndex, JobHandle inputDeps)
    {
        Restore restore;
        restore.camps = DelegateRestore(__camps);
        
        return _ScheduleSingle(restore, frameIndex, inputDeps);
    }

    protected override JobHandle _Save(uint frameIndex, EntityTypeHandle entityType, JobHandle inputDeps)
    {
        int entityCount = __group.CalculateEntityCount();

        Save save;
        save.camps = DelegateSave(entityCount, __camps, inputDeps);

        return _Schedule(save, entityCount, frameIndex, entityType, __group, inputDeps);
    }

    protected override JobHandle _Clear(uint maxFrameIndex, uint frameIndex, uint frameCount, JobHandle inputDeps)
    {
        Clear clear;
        clear.camps = DelegateClear(__camps);

        return _Schedule(clear, maxFrameIndex, frameIndex, frameCount, inputDeps);
    }
}

[AutoCreateIn("Client")]
public partial class GameEntityActorRollbackSystem : RollbackSystemEx
{
    public struct Restore : IRollbackRestore
    {
        //public uint frameIndex;

        [ReadOnly]
        public ComponentLookup<GameActionData> actions;

        public BufferLookup<GameEntityAction> entityActions;

        public ComponentRestoreFunction<GameEntityBreakInfo> breakInfos;
        public ComponentRestoreFunction<GameEntityEventInfo> eventInfos;
        public ComponentRestoreFunction<GameEntityActionInfo> actionInfos;

        public ComponentRestoreFunction<GameEntityActorInfo> actorInfos;

        public ComponentRestoreFunction<GameEntityActorTime> actorTimes;

        public ComponentRestoreFunction<GameEntityActorHit> actorHits;
        public ComponentRestoreFunction<GameEntityHit> hits;

        public BufferRestoreFunction<GameEntityItem> items;
        public BufferRestoreFunction<GameEntityActorActionInfo> actorActionInfos;

        public EntityCommandQueue<Entity>.Writer entityManager;

        public void Execute(int index, int entityIndex, in Entity entity)
        {
            if (!actorInfos.IsExists(entity))
                return;
            
            breakInfos.InvokeDiff(entityIndex, entity);
            eventInfos.InvokeDiff(entityIndex, entity);
            actionInfos.InvokeDiff(entityIndex, entity, out var actionInfo);

            actorInfos.Invoke(entityIndex, entity);
            actorTimes.Invoke(entityIndex, entity);
            actorHits.Invoke(entityIndex, entity);

            //UnityEngine.Debug.Log("Restore: " + entity.ToString() + " : " + actorTimes[index].value + ":" + frameIndex);

            hits.InvokeDiff(entityIndex, entity);

            if(items.IsExists(entity))
                items.Invoke(entityIndex, entity);

            actorActionInfos.Invoke(entityIndex, entity);

            if (this.entityActions.HasComponent(entity))
            {
                var entityActions = this.entityActions[entity];
                Entity entityAction;
                int length = entityActions.Length;
                for(int i = 0; i < length; ++i)
                {
                    entityAction = entityActions[i];
                    if (actions[entityAction].version > actionInfo.version)
                    {
                        //UnityEngine.Debug.Log("Rollback Remove: " + entity.ToString() + " : " + actions[entityAction].index + ":" + actions[entityAction].time);

                        entityActions.RemoveAt(i--);

                        --length;

                        entityManager.Enqueue(entityAction);
                    }
                }
            }
        }
    }

    public struct Save : IRollbackSave
    {
        public ComponentSaveFunction<GameEntityBreakInfo> breakInfos;
        public ComponentSaveFunction<GameEntityEventInfo> eventInfos;
        public ComponentSaveFunction<GameEntityActionInfo> actionInfos;

        public ComponentSaveFunction<GameEntityActorInfo> actorInfos;

        public ComponentSaveFunction<GameEntityActorTime> actorTimes;

        public ComponentSaveFunction<GameEntityActorHit> actorHits;
        public ComponentSaveFunction<GameEntityHit> hits;

        public BufferSaveFunction<GameEntityItem> items;
        public BufferSaveFunction<GameEntityActorActionInfo> actorActionInfos;

        public void Execute(in ArchetypeChunk chunk, int firstEntityIndex)
        {
            breakInfos.Invoke(chunk, firstEntityIndex);
            eventInfos.Invoke(chunk, firstEntityIndex);
            actionInfos.Invoke(chunk, firstEntityIndex);

            actorInfos.Invoke(chunk, firstEntityIndex);
            actorTimes.Invoke(chunk, firstEntityIndex);

            actorHits.Invoke(chunk, firstEntityIndex);
            hits.Invoke(chunk, firstEntityIndex);

            items.Invoke(chunk, firstEntityIndex);
            actorActionInfos.Invoke(chunk, firstEntityIndex);
        }
    }

    public struct Clear : IRollbackClear
    {
        public ComponentClearFunction<GameEntityBreakInfo> breakInfos;
        public ComponentClearFunction<GameEntityEventInfo> eventInfos;
        public ComponentClearFunction<GameEntityActionInfo> actionInfos;

        public ComponentClearFunction<GameEntityActorInfo> actorInfos;

        public ComponentClearFunction<GameEntityActorTime> actorTimes;

        public ComponentClearFunction<GameEntityActorHit> actorHits;
        public ComponentClearFunction<GameEntityHit> hits;

        public BufferClearFunction<GameEntityItem> items;
        public BufferClearFunction<GameEntityActorActionInfo> actorActionInfos;
        
        public void Remove(int fromIndex, int count)
        {
            items.Remove(fromIndex, count);
            actorActionInfos.Remove(fromIndex, count);
        }
        
        public void Move(int fromIndex, int toIndex, int count)
        {
            breakInfos.Move(fromIndex, toIndex, count);
            eventInfos.Move(fromIndex, toIndex, count);
            actionInfos.Move(fromIndex, toIndex, count);
            actorInfos.Move(fromIndex, toIndex, count);
            actorTimes.Move(fromIndex, toIndex, count);
            actorHits.Move(fromIndex, toIndex, count);
            hits.Move(fromIndex, toIndex, count);
            
            items.Move(fromIndex, toIndex, count);
            actorActionInfos.Move(fromIndex, toIndex, count);
        }

        public void Resize(int count)
        {
            breakInfos.Resize(count);
            eventInfos.Resize(count);
            actionInfos.Resize(count);
            actorInfos.Resize(count);
            actorTimes.Resize(count);
            actorHits.Resize(count);
            hits.Resize(count);

            items.Resize(count);
            actorActionInfos.Resize(count);
        }
    }
    
    private EntityQuery __group;
    private EntityCommandPool<Entity> __endFrameBarrier;

    private Component<GameEntityBreakInfo> __breakInfos;
    private Component<GameEntityEventInfo> __eventInfos;
    private Component<GameEntityActionInfo> __actionInfos;

    private Component<GameEntityActorInfo> __actorInfos;
    private Component<GameEntityActorTime> __actorTimes;
    private Component<GameEntityActorHit> __actorHits;
    private Component<GameEntityHit> __hits;

    private Buffer<GameEntityItem> __items;
    private Buffer<GameEntityActorActionInfo> __actorActionInfos;

    protected override void OnCreate()
    {
        base.OnCreate();

        __group = GetEntityQuery(
            ComponentType.ReadOnly<GameRollbackObject>(),
            ComponentType.ReadOnly<GameEntityBreakInfo>(),
            ComponentType.ReadOnly<GameEntityEventInfo>(),
            ComponentType.ReadOnly<GameEntityActionInfo>(),
            ComponentType.ReadOnly<GameEntityActorInfo>(),
            ComponentType.ReadOnly<GameEntityActorHit>(),
            ComponentType.ReadOnly<GameEntityActorTime>(),
            ComponentType.ReadOnly<GameEntityHit>(),
            ComponentType.ReadOnly<GameEntityActorActionInfo>());

        __endFrameBarrier = World.GetOrCreateSystem<EndRollbackSystemGroupStructChangeSystem>().Struct.manager.destoyEntityPool;

        __breakInfos = _GetComponent<GameEntityBreakInfo>();
        __eventInfos = _GetComponent<GameEntityEventInfo>();
        __actionInfos = _GetComponent<GameEntityActionInfo>();
        __actorInfos = _GetComponent<GameEntityActorInfo>();
        __actorTimes = _GetComponent<GameEntityActorTime>();
        __actorHits = _GetComponent<GameEntityActorHit>();
        __hits = _GetComponent<GameEntityHit>();
        __items = _GetBuffer<GameEntityItem>();
        __actorActionInfos = _GetBuffer<GameEntityActorActionInfo>();
    }

    protected override JobHandle _Restore(uint frameIndex, JobHandle inputDeps)
    {
        var entityManager = __endFrameBarrier.Create();

        Restore restore;
        //restore.frameIndex = frameIndex;
        restore.actions = GetComponentLookup<GameActionData>(true);
        restore.entityActions = GetBufferLookup<GameEntityAction>();
        restore.breakInfos = DelegateRestore(__breakInfos);
        restore.eventInfos = DelegateRestore(__eventInfos);
        restore.actionInfos = DelegateRestore(__actionInfos);
        restore.actorInfos = DelegateRestore(__actorInfos);
        restore.actorTimes = DelegateRestore(__actorTimes);
        restore.actorHits = DelegateRestore(__actorHits);
        restore.hits = DelegateRestore(__hits);

        restore.items = DelegateRestore(__items);
        restore.actorActionInfos = DelegateRestore(__actorActionInfos);
        restore.entityManager = entityManager.writer;

        inputDeps = _ScheduleSingle(restore, frameIndex, inputDeps);

        entityManager.AddJobHandleForProducer(inputDeps);

        return inputDeps;
    }

    protected override JobHandle _Save(uint frameIndex, EntityTypeHandle entityType, JobHandle inputDeps)
    {
        int entityCount = __group.CalculateEntityCount();
        if (entityCount < 1)
            return inputDeps;

        Save save;
        save.breakInfos = DelegateSave(entityCount, __breakInfos, inputDeps);
        save.eventInfos = DelegateSave(entityCount, __eventInfos, inputDeps);
        save.actionInfos = DelegateSave(entityCount, __actionInfos, inputDeps);
        save.actorInfos = DelegateSave(entityCount, __actorInfos, inputDeps);
        save.actorTimes = DelegateSave(entityCount, __actorTimes, inputDeps);
        save.actorHits = DelegateSave(entityCount, __actorHits, inputDeps);
        save.hits = DelegateSave(entityCount, __hits, inputDeps);
        save.items = DelegateSave(entityCount, __items, __group, inputDeps);
        save.actorActionInfos = DelegateSave(entityCount, __actorActionInfos, __group, inputDeps);

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
        clear.breakInfos = DelegateClear(__breakInfos);
        clear.eventInfos = DelegateClear(__eventInfos);
        clear.actionInfos = DelegateClear(__actionInfos);
        clear.actorInfos = DelegateClear(__actorInfos);
        clear.actorTimes = DelegateClear(__actorTimes);
        clear.actorHits = DelegateClear(__actorHits);
        clear.hits = DelegateClear(__hits);

        clear.items = DelegateClear(__items);
        clear.actorActionInfos = DelegateClear(__actorActionInfos);

        return _Schedule(clear, maxFrameIndex, frameIndex, frameCount, inputDeps);
    }
}

[AutoCreateIn("Client"), UpdateAfter(typeof(GameEntityActorRollbackSystem))]
public partial class GameActionRollbackSystem : RollbackSystemEx
{
    public struct Command
    {
        public int entityIndex;
        
        public Translation translation;
        public Rotation rotation;
        public PhysicsVelocity physicsVelocity;
        public PhysicsGravityFactor physicsGravityFactor;
        public GameActionData instance;
        public GameActionDataEx instanceEx;
        public GameActionStatus status;
    }

    private class CreateAction : IEntityCommander<Command>
    {
        public Buffer<GameActionEntity> entities;

        private NativeParallelMultiHashMap<EntityArchetype, Command> __commands;

        public void Execute(
            EntityCommandPool<Command>.Context context, 
            EntityCommandSystem system,
            ref NativeHashMap<ComponentType, JobHandle> dependency,
            in JobHandle inputDeps)
        {
            if (__commands.IsCreated)
                __commands.Clear();
            else
                __commands = new NativeParallelMultiHashMap<EntityArchetype, Command>(1, Allocator.Persistent);

            {
                Command command;
                while (context.TryDequeue(out command))
                {
                    __commands.Add(command.instanceEx.entityArchetype, command);
                }

                var entityManager = system.EntityManager;

                using (var keys = __commands.GetKeyArray(Allocator.Temp))
                {
                    int numKeys = keys.ConvertToUniqueArray(), index;
                    Entity entity;
                    EntityArchetype key;
                    NativeParallelMultiHashMapIterator<EntityArchetype> iterator;
                    PhysicsRigidbodyIndex physicsRigidbodyIndex;
                    physicsRigidbodyIndex.value = -1;
                    NativeArray<Entity> entities;
                    for (int i = 0; i < numKeys; ++i)
                    {
                        key = keys[i];
                        if (__commands.TryGetFirstValue(key, out command, out iterator))
                        {
                            using (entities = new NativeArray<Entity>(__commands.CountValuesForKey(key), Allocator.Temp, NativeArrayOptions.UninitializedMemory))
                            {
                                index = 0;

                                entityManager.CreateEntity(key, entities);

                                do
                                {
                                    entity = entities[index++];

                                    if (entityManager.HasComponent<GameEntityAction>(command.instance.entity))
                                    {
                                        entityManager.GetBuffer<GameEntityAction>(command.instance.entity).Add(entity);

                                        //UnityEngine.Debug.Log("Fuck:" + entityManager.GetBuffer<GameEntityAction>(command.instance.entity).Length);
                                    }

                                    entityManager.SetComponentData(entity, command.translation);
                                    entityManager.SetComponentData(entity, command.rotation);

                                    entityManager.SetComponentData(entity, command.physicsVelocity);

                                    entityManager.SetComponentData(entity, command.physicsGravityFactor);

                                    entityManager.SetComponentData(entity, physicsRigidbodyIndex);

                                    entityManager.SetComponentData(entity, command.instance);

                                    entityManager.SetComponentData(entity, command.instanceEx);

                                    entityManager.SetComponentData(entity, command.status);

                                    this.entities.CopyTo(command.entityIndex, entityManager.GetBuffer<GameActionEntity>(entity));

                                    //UnityEngine.Debug.Log("Create: " + command.instance.entity + command.instance.index + ":" + entityManager.GetBuffer<GameActionEntity>(entity).Length);
                                } while (__commands.TryGetNextValue(out command, ref iterator));
                            }
                        }
                    }
                }
            }
        }

        public void Dispose()
        {
            if (__commands.IsCreated)
                __commands.Dispose();
        }
    }
    
    public struct Restore : IRollbackRestore
    {
        [ReadOnly]
        public NativeArray<GameEntityActorSystem.Action>.ReadOnly actions;

        [ReadOnly]
        public BufferLookup<GameEntityAction> entityActions;

        [ReadOnly]
        public ComponentLookup<GameEntityActionInfo> actionInfos;

        //[ReadOnly]
        //public ComponentLookup<GameActionDisabled> disabled;

        public ComponentRestoreFunction<Translation> translations;
        public ComponentRestoreFunction<Rotation> rotations;
        public ComponentRestoreFunction<PhysicsVelocity> physicsVelocities;
        public ComponentRestoreFunction<PhysicsGravityFactor> physicsGravityFactors;
        public ComponentRestoreFunction<GameActionData> instances;
        public ComponentRestoreFunction<GameActionDataEx> instancesEx;
        public ComponentRestoreFunction<GameActionStatus> states;
        public BufferRestoreFunction<GameActionEntity> entities;
        
        public EntityCommandQueue<Command>.ParallelWriter entityManager;

        public void Execute(int index, int entityIndex, in Entity entity)
        {
            bool isExists = instances.IsExists(entity);//, isEntity = isExists;
            Entity target = entity;
            var instance = instances[entityIndex];
            if (!isExists)
            {
                if (this.entityActions.HasComponent(instance.entity))
                {
                    var entityActions = this.entityActions[instance.entity];
                    GameActionData temp;
                    Entity entityAction;
                    int length = entityActions.Length;
                    for (int i = 0; i < length; ++i)
                    {
                        entityAction = entityActions[i];
                        temp = instances[entityAction];
                        if (temp.version == instance.version && temp.actionIndex == instance.actionIndex)
                        {
                            target = entityAction;

                            isExists = true;

                            //UnityEngine.Debug.Log("Unbiliable!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!");

                            break;
                        }
                    }
                }
            }

            if (isExists)
            {
                //UnityEngine.Debug.LogError("Reset Action: " + instance.entity.ToString() + instance.index + ":" + instance.time);

                //if (disabled.HasComponent(target))
                //    removeComponentCommander.Enqueue(target);

                translations.Invoke(entityIndex, target);
                rotations.Invoke(entityIndex, target);
                physicsVelocities.Invoke(entityIndex, target);
                physicsGravityFactors.Invoke(entityIndex, target);
                states.Invoke(entityIndex, target);

                entities.Invoke(entityIndex, target);
            }
            else if (actionInfos.HasComponent(instance.entity) && actionInfos[instance.entity].version >= instance.version)
            {
                //UnityEngine.Debug.LogError("Create Action: " + instance.entity.ToString() + instance.index + ":" + instance.time);

                Command command;
                command.entityIndex = entityIndex;
                command.translation = translations[entityIndex];
                command.rotation = rotations[entityIndex];
                command.physicsVelocity = physicsVelocities[entityIndex];
                command.physicsGravityFactor = physicsGravityFactors[entityIndex];
                command.instance = instance;
                command.instanceEx = instancesEx[entityIndex];
                command.status = states[entityIndex];

                if ((command.status.value & GameActionStatus.Status.Managed) == GameActionStatus.Status.Managed)
                {
                    command.status.value &= ~GameActionStatus.Status.Managed;
                    //command.status.value |= GameActionStatus.Status.Destroied;
                }

                entityManager.Enqueue(command);
            }
            //else
            //   UnityEngine.Debug.LogError("Create Action Fail: " + instance.entity.ToString() + instance.index + ":" + instance.time);
        }
    }

    public struct Save : IRollbackSave
    {
        public ComponentSaveFunction<Translation> translations;
        public ComponentSaveFunction<Rotation> rotations;
        public ComponentSaveFunction<PhysicsVelocity> physicsVelocities;
        public ComponentSaveFunction<PhysicsGravityFactor> physicsGravityFactors;
        public ComponentSaveFunction<GameActionData> instances;
        public ComponentSaveFunction<GameActionDataEx> instancesEx;
        public ComponentSaveFunction<GameActionStatus> states;
        public BufferSaveFunction<GameActionEntity> entities;

        public void Execute(in ArchetypeChunk chunk, int firstEntityIndex)
        {
            translations.Invoke(chunk, firstEntityIndex);
            rotations.Invoke(chunk, firstEntityIndex);
            physicsVelocities.Invoke(chunk, firstEntityIndex);
            physicsGravityFactors.Invoke(chunk, firstEntityIndex);
            instances.Invoke(chunk, firstEntityIndex);
            instancesEx.Invoke(chunk, firstEntityIndex);
            states.Invoke(chunk, firstEntityIndex);
            
            entities.Invoke(chunk, firstEntityIndex);
        }
    }

    public struct Clear : IRollbackClear
    {
        public ComponentClearFunction<Translation> translations;
        public ComponentClearFunction<Rotation> rotations;
        public ComponentClearFunction<PhysicsVelocity> physicsVelocities;
        public ComponentClearFunction<PhysicsGravityFactor> physicsGravityFactors;
        public ComponentClearFunction<GameActionData> instances;
        public ComponentClearFunction<GameActionDataEx> instancesEx;
        public ComponentClearFunction<GameActionStatus> states;
        public BufferClearFunction<GameActionEntity> entities;
        
        public void Remove(int fromIndex, int count)
        {
            entities.Remove(fromIndex, count);
        }
        
        public void Move(int fromIndex, int toIndex, int count)
        {
            translations.Move(fromIndex, toIndex, count);
            rotations.Move(fromIndex, toIndex, count);
            physicsVelocities.Move(fromIndex, toIndex, count);
            physicsGravityFactors.Move(fromIndex, toIndex, count);
            instances.Move(fromIndex, toIndex, count);
            instancesEx.Move(fromIndex, toIndex, count);
            states.Move(fromIndex, toIndex, count);
            
            entities.Move(fromIndex, toIndex, count);
        }

        public void Resize(int count)
        {
            translations.Resize(count);
            rotations.Resize(count);
            physicsVelocities.Resize(count);
            physicsGravityFactors.Resize(count);
            instances.Resize(count);
            instancesEx.Resize(count);
            states.Resize(count);
            
            entities.Resize(count);
        }
    }

    private EntityQuery __group;
    
    private Component<Translation> __translations;
    private Component<Rotation> __rotations;
    private Component<PhysicsVelocity> __physicsVelocities;
    private Component<PhysicsGravityFactor> __physicsGravityFactors;
    private Component<GameActionData> __instances;
    private Component<GameActionDataEx> __instancesEx;
    private Component<GameActionStatus> __states;
    private Buffer<GameActionEntity> __entities;

    private EntityCommandPool<Command> __entityManager;
    //private EntityCommandPool<Entity> __removeComponentCommander;

    protected override void OnCreate()
    {
        base.OnCreate();

        __group = GetEntityQuery(
                ComponentType.ReadOnly<Translation>(),
                ComponentType.ReadOnly<Rotation>(), 
                ComponentType.ReadOnly<PhysicsVelocity>(),
                ComponentType.ReadOnly<GameActionData>(),
                ComponentType.ReadOnly<GameActionDataEx>(),
                ComponentType.ReadOnly<GameActionStatus>(),
                ComponentType.ReadOnly<GameActionEntity>());

        var endFrameBarrier = World.GetOrCreateSystem<EndRollbackSystemGroupEntityCommandSystem>();
        
        __translations = _GetComponent<Translation>();
        __rotations = _GetComponent<Rotation>();
        __physicsVelocities = _GetComponent<PhysicsVelocity>();
        __physicsGravityFactors = _GetComponent<PhysicsGravityFactor>();
        __instances = _GetComponent<GameActionData>();
        __instancesEx = _GetComponent<GameActionDataEx>();
        __states = _GetComponent<GameActionStatus>();
        __entities = _GetBuffer<GameActionEntity>();

        CreateAction createAction = new CreateAction();
        createAction.entities = __entities;
        __entityManager = endFrameBarrier.Create<Command, CreateAction>(EntityCommandManager.QUEUE_CREATE, createAction);
        //__removeComponentCommander = endFrameBarrier.CreateRemoveComponentCommander<GameActionDisabled>();
    }

    protected override JobHandle _Restore(uint frameIndex, JobHandle inputDeps)
    {
        var entityManager = __entityManager.Create();
        //var removeComponentCommander = __removeComponentCommander.Create();

        Restore restore;
        restore.actions = World.GetExistingSystem<GameEntityActorSystem>().Struct.actions;
        restore.entityActions = GetBufferLookup<GameEntityAction>(true);
        restore.actionInfos = GetComponentLookup<GameEntityActionInfo>(true);
        //restore.disabled = GetComponentLookup<GameActionDisabled>(true);
        restore.translations = DelegateRestore(__translations);
        restore.rotations = DelegateRestore(__rotations);
        restore.physicsVelocities = DelegateRestore(__physicsVelocities);
        restore.physicsGravityFactors = DelegateRestore(__physicsGravityFactors);
        restore.instances = DelegateRestore(__instances);
        restore.instancesEx = DelegateRestore(__instancesEx);
        restore.states = DelegateRestore(__states);
        restore.entities = DelegateRestore(__entities);
        restore.entityManager = entityManager.parallelWriter;
        //restore.removeComponentCommander = removeComponentCommander.parallelWriter;

        inputDeps = _Schedule(restore, frameIndex, inputDeps);

        entityManager.AddJobHandleForProducer(inputDeps);
        //removeComponentCommander.AddJobHandleForProducer(inputDeps);

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
        save.physicsVelocities = DelegateSave(entityCount, __physicsVelocities, inputDeps);
        save.physicsGravityFactors = DelegateSave(entityCount, __physicsGravityFactors, inputDeps);
        save.instances = DelegateSave(entityCount, __instances, inputDeps);
        save.instancesEx = DelegateSave(entityCount, __instancesEx, inputDeps);
        save.states = DelegateSave(entityCount, __states, inputDeps);
        save.entities = DelegateSave(entityCount, __entities, __group, inputDeps);

        return _Schedule(save, entityCount, frameIndex, entityType, __group, inputDeps);
    }

    protected override JobHandle _Clear(uint maxFrameIndex, uint frameIndex, uint frameCount, JobHandle inputDeps)
    {
        Clear clear;
        clear.translations = DelegateClear(__translations);
        clear.rotations = DelegateClear(__rotations);
        clear.physicsVelocities = DelegateClear(__physicsVelocities);
        clear.physicsGravityFactors = DelegateClear(__physicsGravityFactors);
        clear.instances = DelegateClear(__instances);
        clear.instancesEx = DelegateClear(__instancesEx);
        clear.states = DelegateClear(__states);
        
        clear.entities = DelegateClear(__entities);

        return _Schedule(clear, maxFrameIndex, frameIndex, frameCount, inputDeps);
    }
}*/

[BurstCompile,
    CreateAfter(typeof(RollbackSystemGroup)),
    UpdateInGroup(typeof(RollbackSystemGroup)),
    AutoCreateIn("Client")]
public partial struct GameEntityRollbackSystem : ISystem, IRollbackCore
{
    public struct Restore : IRollbackRestore
    {
        public RollbackComponentRestoreFunction<GameEntityCamp> camps;

        public void Execute(int index, int entityIndex, in Entity entity)
        {
            if (camps.IsExists(entity))
                camps.Invoke(entityIndex, entity);
        }
    }

    public struct Save : IRollbackSave
    {
        public RollbackComponentSaveFunction<GameEntityCamp> camps;

        public void Execute(in ArchetypeChunk chunk, int firstEntityIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            camps.Invoke(chunk, firstEntityIndex, useEnabledMask, chunkEnabledMask);
        }
    }

    public struct Clear : IRollbackClear
    {
        public RollbackComponentClearFunction<GameEntityCamp> camps;

        public void Remove(int fromIndex, int count)
        {
        }

        public void Move(int fromIndex, int toIndex, int count)
        {
            camps.Move(fromIndex, toIndex, count);
        }

        public void Resize(int count)
        {
            camps.Resize(count);
        }
    }

    private EntityQuery __group;

    private RollbackManager<Restore, Save, Clear> __manager;

    private RollbackComponent<GameEntityCamp> __camps;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        using(var builder = new EntityQueryBuilder(Allocator.Temp))
        __group = builder
                .WithAll<RollbackObject, GameEntityCamp>()
                .Build(ref state);

        var containerManager = state.WorldUnmanaged.GetExistingSystemUnmanaged<RollbackSystemGroup>().containerManager;

        __manager = containerManager.CreateManager<Restore, Save, Clear>(ref state);

        __camps = containerManager.CreateComponent<GameEntityCamp>(ref state);
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
        restore.camps = __manager.DelegateRestore(__camps, ref state);

        state.Dependency = __manager.ScheduleParallel(restore, frameIndex, state.Dependency, true);
    }

    public void ScheduleSave(uint frameIndex, in EntityTypeHandle entityType, ref SystemState state)
    {
        var data = new RollbackSaveData(__group, ref state);

        Save save;
        save.camps = __manager.DelegateSave(__camps, ref data, ref state);

        state.Dependency = __manager.ScheduleParallel(save, frameIndex, entityType, __group, data);
    }

    public void ScheduleClear(uint maxFrameIndex, uint frameIndex, uint frameCount, ref SystemState state)
    {
        Clear clear;
        clear.camps = __manager.DelegateClear(__camps);

        state.Dependency = __manager.Schedule(clear, maxFrameIndex, frameIndex, frameCount, state.Dependency);
    }
}

[BurstCompile,
    CreateAfter(typeof(RollbackSystemGroup)),
    UpdateInGroup(typeof(RollbackSystemGroup)),
    AutoCreateIn("Client")]
public partial struct GameEntityRageRollbackSystem : ISystem, IRollbackCore
{
    public struct Restore : IRollbackRestore
    {
        public RollbackComponentRestoreFunction<GameEntityRage> rages;

        public void Execute(int index, int entityIndex, in Entity entity)
        {
            if (rages.IsExists(entity))
                rages.InvokeDiff(entityIndex, entity);
        }
    }

    public struct Save : IRollbackSave
    {
        public RollbackComponentSaveFunction<GameEntityRage> rages;

        public void Execute(in ArchetypeChunk chunk, int firstEntityIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            rages.Invoke(chunk, firstEntityIndex, useEnabledMask, chunkEnabledMask);
        }
    }

    public struct Clear : IRollbackClear
    {
        public RollbackComponentClearFunction<GameEntityRage> rages;

        public void Remove(int fromIndex, int count)
        {
        }

        public void Move(int fromIndex, int toIndex, int count)
        {
            rages.Move(fromIndex, toIndex, count);
        }

        public void Resize(int count)
        {
            rages.Resize(count);
        }
    }

    private EntityQuery __group;

    private RollbackManager<Restore, Save, Clear> __manager;

    private RollbackComponent<GameEntityRage> __rages;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __group = builder
                    .WithAll<RollbackObject, GameEntityRage>()
                    .Build(ref state);

        var containerManager = state.WorldUnmanaged.GetExistingSystemUnmanaged<RollbackSystemGroup>().containerManager;

        __manager = containerManager.CreateManager<Restore, Save, Clear>(ref state);

        __rages = containerManager.CreateComponent<GameEntityRage>(ref state);
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
        restore.rages = __manager.DelegateRestore(__rages, ref state);

        state.Dependency = __manager.ScheduleParallel(restore, frameIndex, state.Dependency, true);
    }

    public void ScheduleSave(uint frameIndex, in EntityTypeHandle entityType, ref SystemState state)
    {
        var data = new RollbackSaveData(__group, ref state);

        Save save;
        save.rages = __manager.DelegateSave(__rages, ref data, ref state);

        state.Dependency = __manager.ScheduleParallel(save, frameIndex, entityType, __group, data);
    }

    public void ScheduleClear(uint maxFrameIndex, uint frameIndex, uint frameCount, ref SystemState state)
    {
        Clear clear;
        clear.rages = __manager.DelegateClear(__rages);

        state.Dependency = __manager.Schedule(clear, maxFrameIndex, frameIndex, frameCount, state.Dependency);
    }
}

[BurstCompile,
    CreateAfter(typeof(EndRollbackSystemGroupStructChangeSystem)),
    CreateAfter(typeof(RollbackSystemGroup)),
    UpdateInGroup(typeof(RollbackSystemGroup)),
    AutoCreateIn("Client")]
public partial struct GameEntityActorRollbackSystem : ISystem, IRollbackCore
{
    public struct Restore : IRollbackRestore, IEntityCommandProducerJob
    {
        //public uint frameIndex;

        [ReadOnly]
        public ComponentLookup<GameActionData> actions;

        public BufferLookup<GameEntityAction> entityActions;

        public RollbackComponentRestoreFunction<GameEntityBreakInfo> breakInfos;
        public RollbackComponentRestoreFunction<GameEntityEventInfo> eventInfos;
        public RollbackComponentRestoreFunction<GameEntityActionInfo> actionInfos;

        public RollbackComponentRestoreFunction<GameEntityActorInfo> actorInfos;

        public RollbackComponentRestoreFunction<GameEntityActorTime> actorTimes;

        public RollbackComponentRestoreFunction<GameEntityActorHit> actorHits;
        public RollbackComponentRestoreFunction<GameEntityHit> hits;

        public RollbackBufferRestoreFunction<GameEntityItem> items;
        public RollbackBufferRestoreFunction<GameEntityActorActionInfo> actorActionInfos;

        public EntityCommandQueue<Entity>.Writer entityManager;

        public void Execute(int index, int entityIndex, in Entity entity)
        {
            if (!actorInfos.IsExists(entity))
                return;

            breakInfos.InvokeDiff(entityIndex, entity);
            eventInfos.InvokeDiff(entityIndex, entity);
            actionInfos.InvokeDiff(entityIndex, entity, out var actionInfo);

            actorInfos.Invoke(entityIndex, entity);
            actorTimes.Invoke(entityIndex, entity);
            actorHits.Invoke(entityIndex, entity);

            //UnityEngine.Debug.Log("Restore: " + entity.ToString() + " : " + actorTimes[index].value + ":" + frameIndex);

            hits.InvokeDiff(entityIndex, entity);

            if (items.IsExists(entity))
                items.Invoke(entityIndex, entity);

            actorActionInfos.Invoke(entityIndex, entity);

            if (this.entityActions.HasBuffer(entity))
            {
                var entityActions = this.entityActions[entity];
                Entity entityAction;
                int length = entityActions.Length;
                for (int i = 0; i < length; ++i)
                {
                    entityAction = entityActions[i].entity;
                    if (actions[entityAction].version > actionInfo.version)
                    {
                        //UnityEngine.Debug.LogError($"Rollback Remove: {entityAction.Index} : {entityAction.Version} : {actions[entityAction].index} :{(double)actions[entityAction].time}");

                        entityActions.RemoveAt(i--);

                        --length;

                        entityManager.Enqueue(entityAction);
                    }
                }
            }
        }
    }

    public struct Save : IRollbackSave
    {
        public RollbackComponentSaveFunction<GameEntityBreakInfo> breakInfos;
        public RollbackComponentSaveFunction<GameEntityEventInfo> eventInfos;
        public RollbackComponentSaveFunction<GameEntityActionInfo> actionInfos;

        public RollbackComponentSaveFunction<GameEntityActorInfo> actorInfos;

        public RollbackComponentSaveFunction<GameEntityActorTime> actorTimes;

        public RollbackComponentSaveFunction<GameEntityActorHit> actorHits;
        public RollbackComponentSaveFunction<GameEntityHit> hits;

        public RollbackBufferSaveFunction<GameEntityItem> items;
        public RollbackBufferSaveFunction<GameEntityActorActionInfo> actorActionInfos;

        public void Execute(in ArchetypeChunk chunk, int firstEntityIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            breakInfos.Invoke(chunk, firstEntityIndex, useEnabledMask, chunkEnabledMask);
            eventInfos.Invoke(chunk, firstEntityIndex, useEnabledMask, chunkEnabledMask);
            actionInfos.Invoke(chunk, firstEntityIndex, useEnabledMask, chunkEnabledMask);

            actorInfos.Invoke(chunk, firstEntityIndex, useEnabledMask, chunkEnabledMask);
            actorTimes.Invoke(chunk, firstEntityIndex, useEnabledMask, chunkEnabledMask);

            actorHits.Invoke(chunk, firstEntityIndex, useEnabledMask, chunkEnabledMask);
            hits.Invoke(chunk, firstEntityIndex, useEnabledMask, chunkEnabledMask);

            items.Invoke(chunk, firstEntityIndex, useEnabledMask, chunkEnabledMask);
            actorActionInfos.Invoke(chunk, firstEntityIndex, useEnabledMask, chunkEnabledMask);
        }
    }

    public struct Clear : IRollbackClear
    {
        public RollbackComponentClearFunction<GameEntityBreakInfo> breakInfos;
        public RollbackComponentClearFunction<GameEntityEventInfo> eventInfos;
        public RollbackComponentClearFunction<GameEntityActionInfo> actionInfos;

        public RollbackComponentClearFunction<GameEntityActorInfo> actorInfos;

        public RollbackComponentClearFunction<GameEntityActorTime> actorTimes;

        public RollbackComponentClearFunction<GameEntityActorHit> actorHits;
        public RollbackComponentClearFunction<GameEntityHit> hits;

        public RollbackBufferClearFunction<GameEntityItem> items;
        public RollbackBufferClearFunction<GameEntityActorActionInfo> actorActionInfos;

        public void Remove(int fromIndex, int count)
        {
            items.Remove(fromIndex, count);
            actorActionInfos.Remove(fromIndex, count);
        }

        public void Move(int fromIndex, int toIndex, int count)
        {
            breakInfos.Move(fromIndex, toIndex, count);
            eventInfos.Move(fromIndex, toIndex, count);
            actionInfos.Move(fromIndex, toIndex, count);
            actorInfos.Move(fromIndex, toIndex, count);
            actorTimes.Move(fromIndex, toIndex, count);
            actorHits.Move(fromIndex, toIndex, count);
            hits.Move(fromIndex, toIndex, count);

            items.Move(fromIndex, toIndex, count);
            actorActionInfos.Move(fromIndex, toIndex, count);
        }

        public void Resize(int count)
        {
            breakInfos.Resize(count);
            eventInfos.Resize(count);
            actionInfos.Resize(count);
            actorInfos.Resize(count);
            actorTimes.Resize(count);
            actorHits.Resize(count);
            hits.Resize(count);

            items.Resize(count);
            actorActionInfos.Resize(count);
        }
    }

    private EntityQuery __group;
    private EntityCommandPool<Entity> __endFrameBarrier;
    private RollbackManager<Restore, Save, Clear> __manager;

    private RollbackComponent<GameEntityBreakInfo> __breakInfos;
    private RollbackComponent<GameEntityEventInfo> __eventInfos;
    private RollbackComponent<GameEntityActionInfo> __actionInfos;

    private RollbackComponent<GameEntityActorInfo> __actorInfos;
    private RollbackComponent<GameEntityActorTime> __actorTimes;
    private RollbackComponent<GameEntityActorHit> __actorHits;
    private RollbackComponent<GameEntityHit> __hits;

    private RollbackBuffer<GameEntityItem> __items;
    private RollbackBuffer<GameEntityActorActionInfo> __actorActionInfos;

    private ComponentLookup<GameActionData> __actions;
    private BufferLookup<GameEntityAction> __entityActions;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __group = builder
                    .WithAll<RollbackObject, GameEntityBreakInfo, GameEntityEventInfo, GameEntityActionInfo, GameEntityActorInfo, GameEntityActorHit>()
                    .WithAll<GameEntityActorTime, GameEntityHit, GameEntityActorActionInfo>()
                    .Build(ref state);

        var world = state.WorldUnmanaged;
        __endFrameBarrier = world.GetExistingSystemUnmanaged<EndRollbackSystemGroupStructChangeSystem>().manager.destoyEntityPool;

        var containerManager = world.GetExistingSystemUnmanaged<RollbackSystemGroup>().containerManager;

        __manager = containerManager.CreateManager<Restore, Save, Clear>(ref state);

        __breakInfos = containerManager.CreateComponent<GameEntityBreakInfo>(ref state);
        __eventInfos = containerManager.CreateComponent<GameEntityEventInfo>(ref state);
        __actionInfos = containerManager.CreateComponent<GameEntityActionInfo>(ref state);
        __actorInfos = containerManager.CreateComponent<GameEntityActorInfo>(ref state);
        __actorTimes = containerManager.CreateComponent<GameEntityActorTime>(ref state);
        __actorHits = containerManager.CreateComponent<GameEntityActorHit>(ref state);
        __hits = containerManager.CreateComponent<GameEntityHit>(ref state);
        __items = containerManager.CreateBuffer<GameEntityItem>(ref state);
        __actorActionInfos = containerManager.CreateBuffer<GameEntityActorActionInfo>(ref state);

        __actions = state.GetComponentLookup<GameActionData>(true);
        __entityActions = state.GetBufferLookup<GameEntityAction>();
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
        var entityManager = __endFrameBarrier.Create();

        Restore restore;
        //restore.frameIndex = frameIndex;
        restore.actions = __actions.UpdateAsRef(ref state);
        restore.entityActions = __entityActions.UpdateAsRef(ref state);
        restore.breakInfos = __manager.DelegateRestore(__breakInfos, ref state);
        restore.eventInfos = __manager.DelegateRestore(__eventInfos, ref state);
        restore.actionInfos = __manager.DelegateRestore(__actionInfos, ref state);
        restore.actorInfos = __manager.DelegateRestore(__actorInfos, ref state);
        restore.actorTimes = __manager.DelegateRestore(__actorTimes, ref state);
        restore.actorHits = __manager.DelegateRestore(__actorHits, ref state);
        restore.hits = __manager.DelegateRestore(__hits, ref state);

        restore.items = __manager.DelegateRestore(__items, ref state);
        restore.actorActionInfos = __manager.DelegateRestore(__actorActionInfos, ref state);
        restore.entityManager = entityManager.writer;

        var jobHandle = __manager.Schedule(restore, frameIndex, state.Dependency);

        entityManager.AddJobHandleForProducer<Restore>(jobHandle);

        state.Dependency = jobHandle;
    }

    public void ScheduleSave(uint frameIndex, in EntityTypeHandle entityType, ref SystemState state)
    {
        var data = new RollbackSaveData(__group, ref state);

        Save save;
        save.breakInfos = __manager.DelegateSave(__breakInfos, ref data, ref state);
        save.eventInfos = __manager.DelegateSave(__eventInfos, ref data, ref state);
        save.actionInfos = __manager.DelegateSave(__actionInfos, ref data, ref state);
        save.actorInfos = __manager.DelegateSave(__actorInfos, ref data, ref state);
        save.actorTimes = __manager.DelegateSave(__actorTimes, ref data, ref state);
        save.actorHits = __manager.DelegateSave(__actorHits, ref data, ref state);
        save.hits = __manager.DelegateSave(__hits, ref data, ref state);
        save.items = __manager.DelegateSave(__items, __group, ref data, ref state);
        save.actorActionInfos = __manager.DelegateSave(__actorActionInfos, __group, ref data, ref state);

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
        clear.breakInfos = __manager.DelegateClear(__breakInfos);
        clear.eventInfos = __manager.DelegateClear(__eventInfos);
        clear.actionInfos = __manager.DelegateClear(__actionInfos);
        clear.actorInfos = __manager.DelegateClear(__actorInfos);
        clear.actorTimes = __manager.DelegateClear(__actorTimes);
        clear.actorHits = __manager.DelegateClear(__actorHits);
        clear.hits = __manager.DelegateClear(__hits);

        clear.items = __manager.DelegateClear(__items);
        clear.actorActionInfos = __manager.DelegateClear(__actorActionInfos);

        state.Dependency = __manager.Schedule(clear, maxFrameIndex, frameIndex, frameCount, state.Dependency);
    }
}

/*[AutoCreateIn("Client"), AlwaysUpdateSystem, UpdateInGroup(typeof(RollbackSystemGroup))]
public partial class GameEntityActorRollbackSystem : SystemBase
{
    [BurstCompile]
    private static class BurstUtility
    {
        public unsafe delegate void UpdateDelegate(GameEntityActorRollbackSystemCore* core, ref SystemState state);

        public readonly unsafe static UpdateDelegate UpdateFunction = BurstCompiler.CompileFunctionPointer<UpdateDelegate>(Update).Invoke;

        [BurstCompile]
        [MonoPInvokeCallback(typeof(UpdateDelegate))]
        public static unsafe void Update(GameEntityActorRollbackSystemCore* core, ref SystemState state)
        {
            core->OnUpdate(ref state);
        }
    }

    private GameEntityActorRollbackSystemCore __core;

    protected override void OnCreate()
    {
        base.OnCreate();

        __core.OnCreate(ref this.GetState());
    }

    protected override void OnDestroy()
    {
        __core.OnDestroy(ref this.GetState());

        base.OnDestroy();
    }

    protected override unsafe void OnUpdate()
    {
        BurstUtility.UpdateFunction((GameEntityActorRollbackSystemCore*)Unity.Collections.LowLevel.Unsafe.UnsafeUtility.AddressOf(ref __core), ref this.GetState());
    }
}*/

[BurstCompile,
    CreateAfter(typeof(GameActionRollbackFactroySystem)),
    CreateAfter(typeof(RollbackSystemGroup)),
    UpdateInGroup(typeof(RollbackSystemGroup)), 
    UpdateAfter(typeof(GameEntityActorRollbackSystem)), 
    AutoCreateIn("Client")]
public partial struct GameActionRollbackSystem : ISystem, IRollbackCore
{
    [BurstCompile]
    private struct Recapcity : IJob
    {
        [ReadOnly]
        public NativeArray<int> count;

        public SharedMultiHashMap<EntityArchetype, GameActionRollbackCreateCommand>.Writer entityManager;

        public void Execute()
        {
            int entityCount = entityManager.Count() + count[0];

            if (entityManager.capacity < entityCount)
                entityManager.capacity = entityCount;
        }
    }

    public struct Restore : IRollbackRestore
    {
        //[ReadOnly]
        //public BlobAssetReference<GameActionSetDefinition> actions;

        [ReadOnly]
        public BufferLookup<GameEntityAction> entityActions;

        [ReadOnly]
        public ComponentLookup<GameEntityActionInfo> actionInfos;

        //[ReadOnly]
        //public ComponentLookup<GameActionDisabled> disabled;

        public RollbackComponentRestoreFunction<Translation> translations;
        public RollbackComponentRestoreFunction<Rotation> rotations;
        public RollbackComponentRestoreFunction<PhysicsVelocity> physicsVelocities;
        public RollbackComponentRestoreFunction<PhysicsGravityFactor> physicsGravityFactors;
        public RollbackComponentRestoreFunction<GameActionData> instances;
        public RollbackComponentRestoreFunction<GameActionDataEx> instancesEx;
        public RollbackComponentRestoreFunction<GameActionStatus> states;
        public RollbackBufferRestoreFunction<GameActionEntity> entities;

        public SharedMultiHashMap<EntityArchetype, GameActionRollbackCreateCommand>.ParallelWriter entityManager;

        public void Execute(int index, int entityIndex, in Entity entity)
        {
            bool isExists = instances.IsExists(entity);//, isEntity = isExists;
            Entity target = entity;
            var instance = instances[entityIndex];
            if (!isExists)
            {
                if (this.entityActions.HasBuffer(instance.entity))
                {
                    var entityActions = this.entityActions[instance.entity];
                    GameActionData temp;
                    Entity entityAction;
                    int length = entityActions.Length;
                    for (int i = 0; i < length; ++i)
                    {
                        entityAction = entityActions[i].entity;
                        temp = instances[entityAction];
                        if (temp.version == instance.version && temp.actionIndex == instance.actionIndex)
                        {
                            target = entityAction;

                            isExists = true;

                            //UnityEngine.Debug.Log("Unbiliable!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!");

                            break;
                        }
                    }
                }
            }

            if (isExists)
            {
                //UnityEngine.Debug.LogError("Reset Action: " + instance.entity.ToString() + instance.index + ":" + instance.time);

                //if (disabled.HasComponent(target))
                //    removeComponentCommander.Enqueue(target);

                translations.Invoke(entityIndex, target);
                rotations.Invoke(entityIndex, target);
                physicsVelocities.Invoke(entityIndex, target);
                physicsGravityFactors.Invoke(entityIndex, target);

                GameActionStatus destination = states[entityIndex], source = states[target];
                destination.value = destination.value & ~GameActionStatus.Status.Managed | (source.value & GameActionStatus.Status.Managed);
                if (destination.value != source.value)
                    states[target] = destination;

                entities.Invoke(entityIndex, target);
            }
            else if (actionInfos.HasComponent(instance.entity) && actionInfos[instance.entity].version >= instance.version)
            {
                //UnityEngine.Debug.LogError("Create Action: " + instance.entity.ToString() + instance.index + ":" + instance.time);

                GameActionRollbackCreateCommand command;
                command.entityIndex = entityIndex;
                command.translation = translations[entityIndex];
                command.rotation = rotations[entityIndex];
                command.physicsVelocity = physicsVelocities[entityIndex];
                command.physicsGravityFactor = physicsGravityFactors[entityIndex];
                command.instance = instance;
                command.instanceEx = instancesEx[entityIndex];
                command.status = states[entityIndex];

                if ((command.status.value & GameActionStatus.Status.Managed) == GameActionStatus.Status.Managed)
                {
                    command.status.value &= ~GameActionStatus.Status.Managed;
                    //command.status.value |= GameActionStatus.Status.Destroied;
                }

                entityManager.Add(command.instanceEx.entityArchetype, command);
            }
            /*else
                UnityEngine.Debug.LogError("Create Action Fail: " + instance.entity.ToString() + instance.index + ":" + instance.time);*/
        }
    }

    public struct Save : IRollbackSave
    {
        public RollbackComponentSaveFunction<Translation> translations;
        public RollbackComponentSaveFunction<Rotation> rotations;
        public RollbackComponentSaveFunction<PhysicsVelocity> physicsVelocities;
        public RollbackComponentSaveFunction<PhysicsGravityFactor> physicsGravityFactors;
        public RollbackComponentSaveFunction<GameActionData> instances;
        public RollbackComponentSaveFunction<GameActionDataEx> instancesEx;
        public RollbackComponentSaveFunction<GameActionStatus> states;
        public RollbackBufferSaveFunction<GameActionEntity> entities;

        public void Execute(in ArchetypeChunk chunk, int firstEntityIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            translations.Invoke(chunk, firstEntityIndex, useEnabledMask, chunkEnabledMask);
            rotations.Invoke(chunk, firstEntityIndex, useEnabledMask, chunkEnabledMask);
            physicsVelocities.Invoke(chunk, firstEntityIndex, useEnabledMask, chunkEnabledMask);
            physicsGravityFactors.Invoke(chunk, firstEntityIndex, useEnabledMask, chunkEnabledMask);
            instances.Invoke(chunk, firstEntityIndex, useEnabledMask, chunkEnabledMask);
            instancesEx.Invoke(chunk, firstEntityIndex, useEnabledMask, chunkEnabledMask);
            states.Invoke(chunk, firstEntityIndex, useEnabledMask, chunkEnabledMask);

            entities.Invoke(chunk, firstEntityIndex, useEnabledMask, chunkEnabledMask);
        }
    }

    public struct Clear : IRollbackClear
    {
        public RollbackComponentClearFunction<Translation> translations;
        public RollbackComponentClearFunction<Rotation> rotations;
        public RollbackComponentClearFunction<PhysicsVelocity> physicsVelocities;
        public RollbackComponentClearFunction<PhysicsGravityFactor> physicsGravityFactors;
        public RollbackComponentClearFunction<GameActionData> instances;
        public RollbackComponentClearFunction<GameActionDataEx> instancesEx;
        public RollbackComponentClearFunction<GameActionStatus> states;
        public RollbackBufferClearFunction<GameActionEntity> entities;

        public void Remove(int fromIndex, int count)
        {
            entities.Remove(fromIndex, count);
        }

        public void Move(int fromIndex, int toIndex, int count)
        {
            translations.Move(fromIndex, toIndex, count);
            rotations.Move(fromIndex, toIndex, count);
            physicsVelocities.Move(fromIndex, toIndex, count);
            physicsGravityFactors.Move(fromIndex, toIndex, count);
            instances.Move(fromIndex, toIndex, count);
            instancesEx.Move(fromIndex, toIndex, count);
            states.Move(fromIndex, toIndex, count);

            entities.Move(fromIndex, toIndex, count);
        }

        public void Resize(int count)
        {
            translations.Resize(count);
            rotations.Resize(count);
            physicsVelocities.Resize(count);
            physicsGravityFactors.Resize(count);
            instances.Resize(count);
            instancesEx.Resize(count);
            states.Resize(count);

            entities.Resize(count);
        }
    }

    private EntityQuery __group;
    //private EntityQuery __actionSetGroup;

    private BufferLookup<GameEntityAction> __entityActions;

    private ComponentLookup<GameEntityActionInfo> __actionInfos;

    private SharedMultiHashMap<EntityArchetype, GameActionRollbackCreateCommand> __entityManager;

    private RollbackManager<Restore, Save, Clear> __manager;

    private RollbackComponent<Translation> __translations;
    private RollbackComponent<Rotation> __rotations;
    private RollbackComponent<PhysicsVelocity> __physicsVelocities;
    private RollbackComponent<PhysicsGravityFactor> __physicsGravityFactors;
    private RollbackComponent<GameActionData> __instances;
    private RollbackComponent<GameActionDataEx> __instancesEx;
    private RollbackComponent<GameActionStatus> __states;
    private RollbackBuffer<GameActionEntity> __entities;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        using(var builder = new EntityQueryBuilder(Allocator.Temp))
            __group = builder
                    .WithAll<Translation, Rotation, PhysicsVelocity, GameActionData, GameActionDataEx, GameActionStatus, GameActionEntity>()
                    .Build(ref state);

        //__actionSetGroup = state.GetEntityQuery(ComponentType.ReadOnly<GameActionSetData>());

        __entityActions = state.GetBufferLookup<GameEntityAction>(true);
        __actionInfos = state.GetComponentLookup<GameEntityActionInfo>(true);

        var world = state.WorldUnmanaged;
        ref var endFrameBarrier = ref world.GetExistingSystemUnmanaged<GameActionRollbackFactroySystem>();
        __entityManager = endFrameBarrier.commands;

        var containerManager = world.GetExistingSystemUnmanaged<RollbackSystemGroup>().containerManager;

        __manager = containerManager.CreateManager<Restore, Save, Clear>(ref state);

        __translations = containerManager.CreateComponent<Translation>(ref state);
        __rotations = containerManager.CreateComponent<Rotation>(ref state);
        __physicsVelocities = containerManager.CreateComponent<PhysicsVelocity>(ref state);
        __physicsGravityFactors = containerManager.CreateComponent<PhysicsGravityFactor>(ref state);
        __instances = containerManager.CreateComponent<GameActionData>(ref state);
        __instancesEx = containerManager.CreateComponent<GameActionDataEx>(ref state);
        __states = containerManager.CreateComponent<GameActionStatus>(ref state);
        __entities = endFrameBarrier.entities;
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
        Recapcity recapcity;
        recapcity.count = __manager.countAndStartIndex;
        recapcity.entityManager = __entityManager.writer;

        ref var entityManagerJobManager = ref __entityManager.lookupJobManager;
        var jobHandle = recapcity.ScheduleByRef(JobHandle.CombineDependencies(
            entityManagerJobManager.readWriteJobHandle, 
            __manager.GetChunk(frameIndex, state.Dependency)));

        Restore restore;
        //restore.actions = __actions;
        restore.entityActions = __entityActions.UpdateAsRef(ref state);
        restore.actionInfos = __actionInfos.UpdateAsRef(ref state);
        //restore.disabled = GetComponentLookup<GameActionDisabled>(true);
        restore.translations = __manager.DelegateRestore(__translations, ref state);
        restore.rotations = __manager.DelegateRestore(__rotations, ref state);
        restore.physicsVelocities = __manager.DelegateRestore(__physicsVelocities, ref state);
        restore.physicsGravityFactors = __manager.DelegateRestore(__physicsGravityFactors, ref state);
        restore.instances = __manager.DelegateRestore(__instances, ref state);
        restore.instancesEx = __manager.DelegateRestore(__instancesEx, ref state);
        restore.states = __manager.DelegateRestore(__states, ref state);
        restore.entities = __manager.DelegateRestore(__entities, ref state);
        restore.entityManager = __entityManager.parallelWriter;

        jobHandle = __manager.ScheduleParallel(restore, frameIndex, jobHandle, false);

        __entityManager.lookupJobManager.readWriteJobHandle = jobHandle;

        state.Dependency = jobHandle;
    }

    public void ScheduleSave(uint frameIndex, in EntityTypeHandle entityType, ref SystemState state)
    {
        var data = new RollbackSaveData(__group, ref state);

        Save save;
        save.translations = __manager.DelegateSave(__translations, ref data, ref state);
        save.rotations = __manager.DelegateSave(__rotations, ref data, ref state);
        save.physicsVelocities = __manager.DelegateSave(__physicsVelocities, ref data, ref state);
        save.physicsGravityFactors = __manager.DelegateSave(__physicsGravityFactors, ref data, ref state);
        save.instances = __manager.DelegateSave(__instances, ref data, ref state);
        save.instancesEx = __manager.DelegateSave(__instancesEx, ref data, ref state);
        save.states = __manager.DelegateSave(__states, ref data, ref state);
        save.entities = __manager.DelegateSave(__entities, __group, ref data, ref state);

        state.Dependency = __manager.ScheduleParallel(save, frameIndex, entityType, __group, data);
    }

    public void ScheduleClear(uint maxFrameIndex, uint frameIndex, uint frameCount, ref SystemState state)
    {
        Clear clear;
        clear.translations = __manager.DelegateClear(__translations);
        clear.rotations = __manager.DelegateClear(__rotations);
        clear.physicsVelocities = __manager.DelegateClear(__physicsVelocities);
        clear.physicsGravityFactors = __manager.DelegateClear(__physicsGravityFactors);
        clear.instances = __manager.DelegateClear(__instances);
        clear.instancesEx = __manager.DelegateClear(__instancesEx);
        clear.states = __manager.DelegateClear(__states);

        clear.entities = __manager.DelegateClear(__entities);

        state.Dependency = __manager.Schedule(clear, maxFrameIndex, frameIndex, frameCount, state.Dependency);
    }
}