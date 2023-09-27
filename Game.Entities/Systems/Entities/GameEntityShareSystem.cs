using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Transforms;
using UnityEngine;
using ZG;
using ZG.Unsafe;
using Unity.Jobs;

[assembly: RegisterGenericJobType(typeof(GameEntityActionSharedFactorySytem.Apply<GameActionSharedObjectData>))]
[assembly: RegisterGenericJobType(typeof(GameEntityActionSharedFactorySytem.Apply<GameActionSharedObjectParent>))]
[assembly: RegisterGenericJobType(typeof(GameEntityActionSystemCore.PerformEx<GameEntityActionSharedSystem.Handler, GameEntityActionSharedSystem.Factory>))]

/*[UpdateBefore(typeof(GameTransformSystem))]
public class GameEntityActionSharedCommandSytem : EntityCommandSystemHybrid
{

}*/

public struct GameEntityActionSharedDefinition
{
    public struct Item
    {
        public uint negativeActionMask;
        public uint positiveActionMask;
    }

    public struct ActionObject
    {
        public GameEntitySharedActionObjectFlag flag;

        public GameSharedActionType sourceType;
        public GameSharedActionType destinationType;

        public uint mask;
    }

    public struct Action
    {
        public BlobArray<int> objectIndices;
    }

    public BlobArray<Item> items;
    public BlobArray<Action> actions;
    public BlobArray<ActionObject> actionObjects;
}

public struct GameEntityActionSharedData : IComponentData
{
    public BlobAssetReference<GameEntityActionSharedDefinition> definition;
}

[BurstCompile, UpdateInGroup(typeof(PresentationSystemGroup))]//UpdateInGroup(typeof(EndTimeSystemGroupEntityCommandSystemGroup))]
public partial struct GameEntityActionSharedFactorySytem : ISystem
{
    public struct Command
    {
        public Entity entity;
        public GameActionSharedObjectData instance;
        public GameActionSharedObjectParent parent;
    }

    [BurstCompile]
    public struct Apply<T> : IJobParallelFor where T : unmanaged, IComponentData
    {
        [ReadOnly, DeallocateOnJobCompletion]
        public NativeArray<Entity> entityArray;

        [ReadOnly, DeallocateOnJobCompletion]
        public NativeArray<T> sources;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<T> destinations;

        public void Execute(int index)
        {
            destinations[entityArray[index]] = sources[index];
        }
    }

    [BurstCompile]
    public struct Apply : IJob
    {
        [ReadOnly, DeallocateOnJobCompletion]
        public NativeArray<Entity> parentEntities;
        [ReadOnly, DeallocateOnJobCompletion]
        public NativeArray<Entity> childEntities;

        public BufferLookup<GameEntitySharedActionChild> children;

        public void Execute()
        {
            GameEntitySharedActionChild child;
            int length = parentEntities.Length;
            for (int i = 0; i < length; ++i)
            {
                child.entity = childEntities[i];
                children[parentEntities[i]].Add(child);
            }
        }
    }

    public static readonly int InnerloopBatchCount = 1;

    private EntityCommandPool<Command>.Context __context;

    public EntityCommandPool<Command> pool => __context.pool;

    public void OnCreate(ref SystemState state)
    {
        BurstUtility.InitializeJobParallelFor<Apply<GameActionSharedObjectData>>();
        BurstUtility.InitializeJobParallelFor<Apply<GameActionSharedObjectParent>>();
        BurstUtility.InitializeJob<Apply>();

        __context = new EntityCommandPool<Command>.Context(Allocator.Persistent);
    }

    public void OnDestroy(ref SystemState state)
    {
        __context.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (__context.isEmpty)
            return;

        UnsafeList<Entity> entities = default, entitiesWithParent = default, entitiesWithChild = default;
        UnsafeParallelHashMap<Entity, GameActionSharedObjectData> instances = default;
        UnsafeParallelHashMap<Entity, GameActionSharedObjectParent> parents = default;
        UnsafeParallelMultiHashMap<Entity, Entity> childEntities = default;
        Entity entity;
        var entityManager = state.EntityManager;
        while (__context.TryDequeue(out var command))
        {
            if (command.entity == Entity.Null)
            {
                entity = entityManager.CreateEntity();

                if (!entities.IsCreated)
                    entities = new UnsafeList<Entity>(1, state.WorldUpdateAllocator);

                entities.Add(entity);
            }
            else
            {
                entity = command.entity;

                if (entityManager.HasComponent<GameActionSharedObjectData>(entity))
                    command.instance.time = GameDeadline.Min(command.instance.time, entityManager.GetComponentData<GameActionSharedObjectData>(entity).time);
                else if (entityManager.Exists(entity))
                {
                    if (!entities.IsCreated)
                        entities = new UnsafeList<Entity>(1, state.WorldUpdateAllocator);

                    if (!entities.Contains(entity))
                        entities.Add(entity);
                }
                else
                    continue;
            }

            if (command.instance.parentEntity != Entity.Null && entityManager.Exists(command.instance.parentEntity))
            {
                bool isHasChildComponent;
                if (childEntities.IsCreated)
                    isHasChildComponent = childEntities.ContainsKey(command.instance.parentEntity);
                else
                {
                    isHasChildComponent = false;

                    childEntities = new UnsafeParallelMultiHashMap<Entity, Entity>(1, state.WorldUpdateAllocator);
                }

                if (!isHasChildComponent && !entityManager.HasComponent<GameEntitySharedActionChild>(command.instance.parentEntity))
                {
                    if (!entitiesWithChild.IsCreated)
                        entitiesWithChild = new UnsafeList<Entity>(1, state.WorldUpdateAllocator);

                    entitiesWithChild.Add(command.instance.parentEntity);
                }

                childEntities.Add(command.instance.parentEntity, entity);
            }

            if (!instances.IsCreated)
                instances = new UnsafeParallelHashMap<Entity, GameActionSharedObjectData>(1, state.WorldUpdateAllocator);

            instances[entity] = command.instance;

            if (command.parent.value != Entity.Null)
            {
                if (!entityManager.HasComponent<GameActionSharedObjectParent>(entity))
                {
                    if (!entitiesWithParent.IsCreated)
                        entitiesWithParent = new UnsafeList<Entity>(1, state.WorldUpdateAllocator);

                    if (!entitiesWithParent.Contains(entity))
                        entitiesWithParent.Add(entity);
                }

                if (!parents.IsCreated)
                    parents = new UnsafeParallelHashMap<Entity, GameActionSharedObjectParent>(1, state.WorldUpdateAllocator);

                parents[entity] = command.parent;
            }
        }

        if (!entities.IsEmpty)
        {
            entityManager.AddComponentBurstCompatible<GameActionSharedObjectData>(entities.AsArray());

            //entities.Dispose();
        }

        if (!entitiesWithParent.IsEmpty)
        {
            entityManager.AddComponentBurstCompatible<GameActionSharedObjectParent>(entitiesWithParent.AsArray());

            //entitiesWithParent.Dispose();
        }

        if (!entitiesWithChild.IsEmpty)
        {
            entityManager.AddComponentBurstCompatible<GameEntitySharedActionChild>(entitiesWithChild.AsArray());

            //entitiesWithChild.Dispose();
        }

        var inputDeps = state.Dependency;
        JobHandle? result = null;

        if (!instances.IsEmpty)
        {
            var keyValueArrays = instances.GetKeyValueArrays(Allocator.TempJob);

            Apply<GameActionSharedObjectData> apply;
            apply.entityArray = keyValueArrays.Keys;
            apply.sources = keyValueArrays.Values;
            apply.destinations = state.GetComponentLookup<GameActionSharedObjectData>();
            var jobHandle = apply.ScheduleByRef(keyValueArrays.Length, InnerloopBatchCount, inputDeps);

            result = result == null ? jobHandle : JobHandle.CombineDependencies(jobHandle, result.Value);

            //instances.Dispose();
        }

        if (!parents.IsEmpty)
        {
            var keyValueArrays = parents.GetKeyValueArrays(Allocator.TempJob);

            Apply<GameActionSharedObjectParent> apply;
            apply.entityArray = keyValueArrays.Keys;
            apply.sources = keyValueArrays.Values;
            apply.destinations = state.GetComponentLookup<GameActionSharedObjectParent>();
            var jobHandle = apply.ScheduleByRef(keyValueArrays.Length, InnerloopBatchCount, inputDeps);

            result = result == null ? jobHandle : JobHandle.CombineDependencies(jobHandle, result.Value);

            //parents.Dispose();
        }

        if (childEntities.IsCreated && !childEntities.IsEmpty)
        {
            var keyValueArrays = childEntities.GetKeyValueArrays(Allocator.TempJob);

            Apply apply;
            apply.parentEntities = keyValueArrays.Keys;
            apply.childEntities = keyValueArrays.Values;
            apply.children = state.GetBufferLookup<GameEntitySharedActionChild>();

            var jobHandle = apply.ScheduleByRef(inputDeps);

            result = result == null ? jobHandle : JobHandle.CombineDependencies(jobHandle, result.Value);

            //childEntities.Dispose();
        }

        if (result != null)
            state.Dependency = result.Value;
    }
}

[BurstCompile, UpdateInGroup(typeof(GameTransformSimulationSystemGroup), OrderLast = true)]
public partial struct GameEntitySharedActionTransformSystem : ISystem
{
    private struct OverrideWriteTime
    {
        [ReadOnly]
        public NativeArray<GameActionStatus> states;
        public BufferAccessor<GameTransformKeyframe<GameTransform>> transformKeyframes;

        public void Execute(int index)
        {
            var status = states[index];
            if ((status.value & GameActionStatus.Status.Destroy) != GameActionStatus.Status.Destroy)
                return;

            var transformKeyframes = this.transformKeyframes[index];
            int numTransformKeyframes = transformKeyframes.Length;
            if (numTransformKeyframes < 1)
                return;

            var transformKeyframe = transformKeyframes[numTransformKeyframes - 1];

            transformKeyframe.time = status.time;

            GameTransformKeyframe<GameTransform>.Insert(ref transformKeyframes, transformKeyframe);
        }
    }

    [BurstCompile]
    private struct OverrideWriteTimeEx : IJobChunk
    {
        [ReadOnly]
        public ComponentTypeHandle<GameActionStatus> statusType;
        public BufferTypeHandle<GameTransformKeyframe<GameTransform>> transformKeyframeType;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            OverrideWriteTime overrideWriteTime;
            overrideWriteTime.states = chunk.GetNativeArray(ref statusType);
            overrideWriteTime.transformKeyframes = chunk.GetBufferAccessor(ref transformKeyframeType);

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                overrideWriteTime.Execute(i);
        }
    }

    private EntityQuery __group;

    public void OnCreate(ref SystemState state)
    {
        __group = state.GetEntityQuery(
            ComponentType.ReadOnly<GameActionStatus>(),
            ComponentType.ReadWrite<GameTransformKeyframe<GameTransform>>());
    }

    public void OnDestroy(ref SystemState state)
    {

    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        OverrideWriteTimeEx overrideWriteTime;
        overrideWriteTime.statusType = state.GetComponentTypeHandle<GameActionStatus>(true);
        overrideWriteTime.transformKeyframeType = state.GetBufferTypeHandle<GameTransformKeyframe<GameTransform>>();

        state.Dependency = overrideWriteTime.ScheduleParallel(__group, state.Dependency);
    }
}

//[UpdateBefore(typeof(GameTransformSystem))]//[UpdateBefore(typeof(GameEntityActionSharedCommandSytem))]
[UpdateInGroup(typeof(PresentationSystemGroup)), UpdateAfter(typeof(GameEntityActionSharedFactorySytem))]
public partial class GameEntityActionSharedObjectFactorySystem : SystemBase
{
    private double __time;
    private GameDeadline __now;
    private EntityQuery __groupToCreate;
    private EntityQuery __groupToDestroy;
    private EntityQuery __groupToDestroyImmediate;
    private GameRollbackTime __rollbackTime;
    //private GameUpdateSystemGroup __updateSystemGroup;
    private EntityCommander __endFrameBarrier;

    private GameActionSharedObjectAsset[] __assets;
    private Pool<GameObject> __instances;

    public void Create(IEnumerable<GameActionSharedObjectAsset> assets)
    {
        __assets = new List<GameActionSharedObjectAsset>(assets).ToArray();
    }

    protected override void OnCreate()
    {
        base.OnCreate();

        __groupToCreate = GetEntityQuery(ComponentType.ReadOnly<GameActionSharedObjectData>(), ComponentType.Exclude<GameActionSharedObject>());
        __groupToDestroy = GetEntityQuery(
            new EntityQueryDesc()
            {
                All = new ComponentType[]
                {
                    ComponentType.ReadOnly<GameActionSharedObject>()
                },
                None = new ComponentType[]
                {
                    typeof(GameActionSharedObjectData)
                }
            },
            new EntityQueryDesc()
            {
                All = new ComponentType[]
                {
                    ComponentType.ReadOnly<GameActionSharedObject>(),
                    ComponentType.ReadOnly<GameActionStatus>()
                }
            });

        __groupToDestroyImmediate = GetEntityQuery(
            ComponentType.ReadOnly<GameActionSharedObjectParent>(),
            ComponentType.Exclude<GameActionSharedObjectData>(),
            ComponentType.Exclude<GameActionSharedObject>(),
            ComponentType.Exclude<GameActionData>());

        __rollbackTime = new GameRollbackTime(ref this.GetState());

        //__updateSystemGroup = world.GetOrCreateSystem<GameUpdateSystemGroup>();

        __endFrameBarrier = new EntityCommander(Allocator.Persistent);
    }

    protected override void OnDestroy()
    {
        __endFrameBarrier.Dispose();

        base.OnDestroy();
    }

    protected override void OnUpdate()
    {
        __time = SystemAPI.GetSingleton<GameAnimationElapsedTime>().value;

        if (!__groupToCreate.IsEmpty)
        {
            __now = __rollbackTime.now;

            //TODO: 
            __groupToCreate.CompleteDependency();

            using (var entityEntity = __groupToCreate.ToEntityArray(Allocator.Temp))
            using (var instances = __groupToCreate.ToComponentDataArray<GameActionSharedObjectData>(Allocator.Temp))
            {
                int length = entityEntity.Length;
                for (int i = 0; i < length; ++i)
                    __Create(entityEntity[i], instances[i]);
            }
        }

        if (!__groupToDestroy.IsEmpty)
        {
            //TODO: 
            __groupToDestroy.CompleteDependency();

            using (var entityEntity = __groupToDestroy.ToEntityArray(Allocator.Temp))
            using (var instances = __groupToDestroy.ToComponentDataArray<GameActionSharedObject>(Allocator.Temp))
            {
                int length = entityEntity.Length;
                for (int i = 0; i < length; ++i)
                    __Destroy(entityEntity[i], instances[i]);
            }
        }

        //Entities.With(__groupToCreate).ForEach<GameActionSharedObjectData>(__Create);

        //Entities.With(__groupToDestroy).ForEach<GameActionSharedObject>(__Destroy);

        EntityManager.RemoveComponent<GameActionSharedObjectParent>(__groupToDestroyImmediate);

        __endFrameBarrier.Playback(ref this.GetState());
    }

    private void __Create(in Entity entity, in GameActionSharedObjectData instance)
    {
        if (__time < instance.time)
            return;

        var entityManager = EntityManager;

        UnityEngine.Assertions.Assert.IsFalse(instance.index < 0 || instance.index >= __assets.Length);

        bool isAction = entityManager.HasComponent<GameActionData>(entity);
        if (!isAction && instance.parentEntity != Entity.Null)
        {
            var children = entityManager.HasComponent<GameEntitySharedActionChild>(instance.parentEntity) ?
                entityManager.GetBuffer<GameEntitySharedActionChild>(instance.parentEntity) : default;
            int childIndex = children.IsCreated ? children.Reinterpret<Entity>().AsNativeArray().IndexOf(entity) : -1;
            if (childIndex == -1)
            {
                __endFrameBarrier.DestroyEntity(entity);

                return;
            }
        }

        GameActionStatus.Status destroyStatus = 0;
        Entity actionEntity;
        var asset = __assets[instance.index];
        if (asset.gameObject == null)
        {
            if (isAction)
                __endFrameBarrier.RemoveComponent<GameActionSharedObjectData>(entity);
            else
                __endFrameBarrier.DestroyEntity(entity);

            return;
        }

        //float destroyTime;
        var gameObject = UnityEngine.Object.Instantiate(
            asset.gameObject,
            instance.transform.pos,
            instance.transform.rot);
        Transform transform = gameObject.transform, parent = __GetParent(entity, !isAction);
        transform.SetParent(parent, false);
        //Debug.LogError($"Create {gameObject.name} : {asset.destroyTime}", gameObject);
        if (isAction)
        {
            actionEntity = entity;

            //__endFrameBarrier.AddComponent<EntityObjects>(entity);
            __endFrameBarrier.AddComponentObject(entity, new EntityObject<Transform>(transform));
            //__endFrameBarrier.AddComponent<GameTransformVelocity<GameTransform, GameTransformVelocity>>(entity);

            GameTransformKeyframe<GameTransform> source, destination;
            source.time = instance.time;
            source.value.value = instance.transform;

            destination.time =
                entityManager.TryGetComponentData<GameActionStatus>(entity, out var status) && (status.value & GameActionStatus.Status.Destroy) == GameActionStatus.Status.Destroy ?
                status.time :
                __now;
            destination.value.value = math.RigidTransform(
                entityManager.GetComponentData<Rotation>(entity).Value,
                entityManager.GetComponentData<Translation>(entity).Value);

            __endFrameBarrier.AddBuffer(entity, source, destination);
        }
        else
        {
            float elpasedTime = (float)(__time - instance.time),
                destroyTime = asset.destroyTime - elpasedTime;
            if (gameObject != null)
            {
                var particleSystem = gameObject.GetComponentInChildren<ParticleSystem>();
                if (particleSystem != null)
                {
                    //particleSystem.Play(true);

                    particleSystem.Simulate(elpasedTime, true);
                    particleSystem.Play(true);
                }

                GameObject.Destroy(gameObject, destroyTime);
            }

            if (instance.parentEntity == Entity.Null || destroyTime < math.FLT_MIN_NORMAL)
            {
                __endFrameBarrier.DestroyEntity(entity);

                return;
            }

            UnityEngine.Assertions.Assert.IsTrue(
                (entityManager.GetComponentData<GameActionStatus>(instance.parentEntity).value & GameActionStatus.Status.Managed) == GameActionStatus.Status.Managed);

            //Debug.LogError($"Create Shared {entity} : {instance.parentEntity} : {entityManager.GetComponentData<GameActionStatus>(instance.parentEntity).value}");

            actionEntity = instance.parentEntity;

            //asset.destroyTime = 0.0f;

            destroyStatus = /*GameActionStatus.Status.Damage | */GameActionStatus.Status.Break;
        }

        GameActionSharedObject target;
        if (__instances == null)
            __instances = new Pool<GameObject>();

        target.index = __instances.Add(gameObject);
        target.flag = asset.flag;
        target.destroyStatus = destroyStatus;
        target.destroyTime = asset.destroyTime;
        target.actionEntity = actionEntity;

        __endFrameBarrier.AddComponentData(entity, target);
    }

    private void __Destroy(in Entity entity, in GameActionSharedObject target)
    {
        var entityManager = EntityManager;
        bool isDisabled = entityManager.TryGetComponentData<GameActionStatus>(target.actionEntity, out var status);
        if (isDisabled &&
            ((status.value & GameActionStatus.Status.Destroy) != GameActionStatus.Status.Destroy ||
            status.time/* + Time.DeltaTime*/ > __time))
            return;

        Transform parent = __GetParent(entity, true);
        if ((target.destroyStatus & status.value) == 0)
        {
            //isDisabled can be false after delete the action in rollback.
            //UnityEngine.Assertions.Assert.IsTrue(isDisabled, $"Destory Shared Action {entity} : {target.actionEntity} : {target.destroyStatus}");

            var gameObject = __instances[target.index];
            if (gameObject != null)
            {
                //UnityEngine.Debug.LogError($"Destroy {gameObject.name}", gameObject);

                /*if (!isDisabled)
                {
                    UnityEngine.Debug.LogError(gameObject);
                    //UnityEngine.Debug.Break();
                }*/

                /*if (isDisabled && target.destroyStatus == 0)
                {
                    gameObject.transform.SetParent(parent);
                }*/
                if (isDisabled)
                {
                    if(target.destroyStatus == 0)
                        gameObject.transform.SetParent(parent);
                    else if((target.flag & GameActionSharedObjectFlag.EnableWhenBreak) == 0)
                        gameObject.SetActive(false);
                }
                else
                    gameObject.SetActive(false);

                GameObject.Destroy(gameObject, target.destroyTime);
            }
        }

        __instances.RemoveAt(target.index);

        __endFrameBarrier.RemoveComponent<GameActionSharedObject>(entity);

        if (entityManager.HasComponent<GameActionSharedObjectData>(entity))
            __endFrameBarrier.RemoveComponent<GameActionSharedObjectData>(entity);

        //Debug.LogError($"Clear Managed {target.actionEntity} : {isDisabled} : {entityManager.HasComponent<GameEntitySharedActionChild>(target.actionEntity)}");
        if (isDisabled && !entityManager.HasComponent<GameEntitySharedActionChild>(target.actionEntity))
        {
            //Debug.LogError($"Clear Managed {target.actionEntity.Index} : {status.value}");

            var value = status.value & ~GameActionStatus.Status.Managed;
            if (value != status.value)
            {
                status.value = value;

                __endFrameBarrier.SetComponentData(target.actionEntity, status);
            }
        }

        /*if (isDisabled)
        {
            var temp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            temp.transform.position = this.GetComponentData<Translation>(entity).Value;
            Debug.Log(temp.transform.position.ToString() + ":" + this.GetComponentData<GameRigidbodyTransformDestination>(entity).value.time + ":" + __updateSystemGroup.animationElapsedTime);
        }*/
    }

    private Transform __GetParent(Entity entity, bool isRemove)
    {
        var entityManager = EntityManager;
        if (!entityManager.TryGetComponentData<GameActionSharedObjectParent>(entity, out var parent))
            return null;

        var result = entityManager.TryGetComponentData<EntityObject<Transform>>(parent.value, out var transform) ? transform.value : null;

        if (isRemove)
            __endFrameBarrier.RemoveComponent<GameActionSharedObjectParent>(entity);

        return result;
    }
}

[BurstCompile, RequireMatchingQueriesForUpdate, UpdateInGroup(typeof(PresentationSystemGroup)), UpdateBefore(typeof(GameEntityActionSharedObjectFactorySystem))]
public partial struct GameEntityActionSharedObjectBreakSystem : ISystem
{
    private struct CollectFromNonexistentActions
    {
        [ReadOnly]
        public NativeArray<Entity> entityArray;

        [ReadOnly]
        public BufferAccessor<GameEntitySharedActionChild> children;

        public NativeList<Entity> childEntities;

        public NativeList<Entity> entities;

        public void Execute(int index)
        {
            childEntities.AddRange(children[index].Reinterpret<Entity>().AsNativeArray());

            entities.Add(entityArray[index]);
        }
    }

    private struct CollectFromDestroiedActions
    {
        [ReadOnly]
        public ComponentLookup<GameActionSharedObject> targets;

        [ReadOnly]
        public NativeArray<Entity> entityArray;

        [ReadOnly]
        public NativeArray<GameActionStatus> states;

        [ReadOnly]
        public BufferAccessor<GameEntitySharedActionChild> children;

        public NativeList<Entity> childEntities;

        public NativeList<Entity> entities;

        public void Execute(int index)
        {
            if ((states[index].value & GameActionStatus.Status.Destroy) != GameActionStatus.Status.Destroy)
                return;

            var children = this.children[index];
            Entity child;
            int numChildren = children.Length;
            for (int i = 0; i < numChildren; ++i)
            {
                child = children[i].entity;
                if (targets.HasComponent(child))
                {
                    childEntities.Add(child);

                    children.RemoveAtSwapBack(i--);

                    --numChildren;
                }
            }

            if (numChildren < 1)
                entities.Add(entityArray[index]);
        }
    }

    [BurstCompile]
    private struct CollectFromNonexistentActionsEx : IJobChunk
    {
        [ReadOnly]
        public EntityTypeHandle entityType;

        public BufferTypeHandle<GameEntitySharedActionChild> childrenType;

        public NativeList<Entity> childEntities;

        public NativeList<Entity> entities;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            CollectFromNonexistentActions collect;
            collect.entityArray = chunk.GetNativeArray(entityType);
            collect.children = chunk.GetBufferAccessor(ref childrenType);
            collect.childEntities = childEntities;
            collect.entities = entities;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                collect.Execute(i);
        }
    }

    [BurstCompile]
    private struct CollectFromDestroiedActionsEx : IJobChunk
    {
        [ReadOnly]
        public ComponentLookup<GameActionSharedObject> targets;

        [ReadOnly]
        public EntityTypeHandle entityType;

        [ReadOnly]
        public ComponentTypeHandle<GameActionStatus> statusType;

        public BufferTypeHandle<GameEntitySharedActionChild> childrenType;

        public NativeList<Entity> childEntities;

        public NativeList<Entity> entities;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            CollectFromDestroiedActions collect;
            collect.targets = targets;
            collect.entityArray = chunk.GetNativeArray(entityType);
            collect.states = chunk.GetNativeArray(ref statusType);
            collect.children = chunk.GetBufferAccessor(ref childrenType);
            collect.childEntities = childEntities;
            collect.entities = entities;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                collect.Execute(i);
        }
    }

    private EntityQuery __groupFromNonexistentActions;
    private EntityQuery __groupFromDestroiedActions;

    public void OnCreate(ref SystemState state)
    {
        __groupFromNonexistentActions = state.GetEntityQuery(
            new EntityQueryDesc()
            {
                All = new ComponentType[]
                {
                    ComponentType.ReadWrite<GameEntitySharedActionChild>()
                },
                None = new ComponentType[]
                {
                    typeof(GameActionStatus)
                }
            });

        __groupFromDestroiedActions = state.GetEntityQuery(
            new EntityQueryDesc()
            {
                All = new ComponentType[]
                {
                    ComponentType.ReadOnly<GameActionStatus>(),
                    ComponentType.ReadWrite<GameEntitySharedActionChild>()
                }
            });
        //TODO:
        //__groupFromDestroiedActions.SetChangedVersionFilter(typeof(GameActionStatus));
    }

    public void OnDestroy(ref SystemState state)
    {

    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var entityManager = state.EntityManager;
        using (var entities = new NativeList<Entity>(Allocator.TempJob))
        using (var childEntities = new NativeList<Entity>(Allocator.TempJob))
        {
            state.CompleteDependency();

            var entityType = state.GetEntityTypeHandle();
            var childType = state.GetBufferTypeHandle<GameEntitySharedActionChild>();

            CollectFromNonexistentActionsEx collectFromNonexistentActions;
            collectFromNonexistentActions.entityType = entityType;
            collectFromNonexistentActions.childrenType = childType;
            collectFromNonexistentActions.childEntities = childEntities;
            collectFromNonexistentActions.entities = entities;
            collectFromNonexistentActions.Run(__groupFromNonexistentActions);

            CollectFromDestroiedActionsEx collectFromDestroiedActions;
            collectFromDestroiedActions.targets = state.GetComponentLookup<GameActionSharedObject>(true);
            collectFromDestroiedActions.entityType = entityType;
            collectFromDestroiedActions.statusType = state.GetComponentTypeHandle<GameActionStatus>(true);
            collectFromDestroiedActions.childrenType = childType;
            collectFromDestroiedActions.childEntities = childEntities;
            collectFromDestroiedActions.entities = entities;
            collectFromDestroiedActions.Run(__groupFromDestroiedActions);

            entityManager.DestroyEntity(childEntities.AsArray());

            entityManager.RemoveComponent<GameEntitySharedActionChild>(entities.AsArray());
        }
    }
}

/*[UpdateInGroup(typeof(GameSyncSystemGroup)), UpdateBefore(typeof(GameEntityActorSystem))]
public partial class GameEntityActionSharedStatusSystem : JobComponentSystem
{
    private struct UpdateStates
    {
        public float staticThreshold;

        [ReadOnly]
        public NativeArray<Entity> entityArray;

        [ReadOnly]
        public NativeArray<GameRidigbodySyncVelocity> velocities;
        
        public NativeArray<GameActionStatus> states;
        
        public void Execute(int index)
        {
            Entity entity = entityArray[index];
            GameRidigbodySyncVelocity velocity = velocities[index];
            GameActionStatus status = states[index];
            switch (status.value & GameActionStatus.Status.Managed)
            {
                case GameActionStatus.Status.Destroied:
                    if (math.lengthsq(velocity.linear) >= staticThreshold)
                    {
                        status.value |= GameActionStatus.Status.Managed;
                        states[index] = status;
                    }
                    break;
                case GameActionStatus.Status.Managed:
                    if (math.lengthsq(velocity.linear) < staticThreshold)
                    {
                        status.value &= ~GameActionStatus.Status.Managed;
                        status.value |= GameActionStatus.Status.Destroied;
                        states[index] = status;
                    }
                    break;
            }
        }
    }

    [BurstCompile]
    private struct UpdateStatesEx : IJobChunk
    {
        public float staticThreshold;

        [ReadOnly]
        public ArchetypeChunkEntityType entityType;
        [ReadOnly]
        public ArchetypeChunkComponentType<GameRidigbodySyncVelocity> velocityType;
        
        public ArchetypeChunkComponentType<GameActionStatus> statusType;
        
        public void Execute(ArchetypeChunk chunk, int chunkIndex, int firstEntityIndex)
        {
            UpdateStates updateStates;
            updateStates.staticThreshold = staticThreshold;
            updateStates.entityArray = chunk.GetNativeArray(entityType);
            updateStates.velocities = chunk.GetNativeArray(velocityType);
            updateStates.states = chunk.GetNativeArray(statusType);
            
            int count = chunk.Count;
            for (int i = 0; i < count; ++i)
                updateStates.Execute(i);
        }
    }
    
    public float staticThreshold = 0.01f;
    
    private EntityQuery __group;
    
    protected override void OnCreate()
    {
        base.OnCreate();

        __group = GetEntityQuery(
            ComponentType.ReadOnly<GameActionStatus>(),
            ComponentType.ReadOnly<GameRidigbodySyncVelocity>());
    }

    protected override JobHandle OnUpdate(JobHandle inputDeps)
    {
        UpdateStatesEx updateStates;
        updateStates.staticThreshold = staticThreshold;
        updateStates.entityType = GetArchetypeChunkEntityType();
        updateStates.velocityType = GetArchetypeChunkComponentType<GameRidigbodySyncVelocity>(true);
        updateStates.statusType = GetArchetypeChunkComponentType<GameActionStatus>();
        
        return updateStates.Schedule(__group, inputDeps);
    }
}*/

/*[BurstCompile, UpdateInGroup(typeof(GameUpdateSystemGroup)), UpdateAfter(typeof(GameEntityActionSystemGroup))]
public partial struct GameEntityActionSharedUpdateSystem : ISystem
{
    private struct UpdateActions
    {
        public double time;

        [ReadOnly]
        public ComponentLookup<GameActionData> actions;

        [ReadOnly]
        public NativeArray<GameEntitySharedData> instances;

        [ReadOnly]
        public NativeArray<GameEntityActionInfo> actionInfos;

        [ReadOnly]
        public BufferAccessor<GameEntityAction> entityActions;

        public BufferAccessor<GameEntitySharedAction> sharedActions;

        public void Execute(int index)
        {
            var sharedActions = this.sharedActions[index];
            var instance = instances[index];
            GameEntitySharedAction sharedAction;
            int numSharedActions = sharedActions.Length, version = actionInfos[index].version, i;
            for (i = 0; i < numSharedActions; ++i)
            {
                sharedAction = sharedActions[i];
                if (sharedAction.version + instance.cacheVersionCount < version)
                {
                    //Debug.Log($"Remove (New Version {version}, Index: {sharedAction.index}, Version: {sharedAction.version}) From {sharedAction.elapsedTime}");

                    sharedActions.RemoveAtSwapBack(i--);

                    --numSharedActions;
                }
            }

            var entityActions = this.entityActions[index];
            GameActionData action;
            int numEntityActions = entityActions.Length, j;
            for (i = 0; i < numEntityActions; ++i)
            {
                action = actions[entityActions[i].entity];
                for (j = 0; j < numSharedActions; ++j)
                {
                    sharedAction = sharedActions[j];
                    if (sharedAction.index == action.index && sharedAction.version == action.version)
                    {
                        sharedAction.elapsedTime = math.max(sharedAction.elapsedTime, (float)(time - action.time));

                        sharedActions[j] = sharedAction;

                        //Debug.Log($"Set (Entity {entityActions[i].entity.Index}, Index: {action.index}, Version: {action.version}) To {sharedAction.elapsedTime}");

                        break;
                    }
                }

                if (j == numSharedActions)
                {
                    sharedAction.index = action.index;
                    sharedAction.version = action.version;
                    sharedAction.elapsedTime = (float)(time - action.time);

                    //Debug.Log($"Set (Entity {entityActions[i].entity.Index}, Index: {action.index}, Version: {action.version}) To {sharedAction.elapsedTime}");

                    sharedActions.Add(sharedAction);
                }
            }
        }
    }

    [BurstCompile]
    private struct UpdateActionsEx : IJobChunk
    {
        public double time;

        [ReadOnly]
        public ComponentLookup<GameActionData> actions;

        [ReadOnly]
        public ComponentTypeHandle<GameEntitySharedData> instanceType;

        [ReadOnly]
        public ComponentTypeHandle<GameEntityActionInfo> actionInfoType;

        [ReadOnly]
        public BufferTypeHandle<GameEntityAction> entityActionType;

        public BufferTypeHandle<GameEntitySharedAction> sharedActionType;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            UpdateActions updateActions;
            updateActions.time = time;
            updateActions.actions = actions;
            updateActions.instances = chunk.GetNativeArray(ref instanceType);
            updateActions.actionInfos = chunk.GetNativeArray(ref actionInfoType);
            updateActions.entityActions = chunk.GetBufferAccessor(ref entityActionType);
            updateActions.sharedActions = chunk.GetBufferAccessor(ref sharedActionType);

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                updateActions.Execute(i);
        }
    }

    private EntityQuery __groupToDestroy;
    private EntityQuery __groupToUpdate;
    private GameRollbackTime __time;

    public void OnCreate(ref SystemState state)
    {
        __groupToDestroy = state.GetEntityQuery(
            ComponentType.ReadOnly<GameEntitySharedAction>(),
            ComponentType.Exclude<GameEntityAction>());

        __groupToUpdate = state.GetEntityQuery(
            ComponentType.ReadOnly<GameEntitySharedData>(),
            ComponentType.ReadOnly<GameEntityActionInfo>(),
            ComponentType.ReadOnly<GameEntityAction>(),
            ComponentType.ReadWrite<GameEntitySharedAction>());

        __time = new GameRollbackTime(ref state);
    }

    public void OnDestroy(ref SystemState state)
    {

    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        state.EntityManager.RemoveComponent<GameEntitySharedAction>(__groupToDestroy);

        if (!__groupToUpdate.IsEmpty)
        {
            UpdateActionsEx updateActions;
            updateActions.time = __time.now;// __syncGroup.GetSingleton<GameSyncData>().now;
            updateActions.actions = state.GetComponentLookup<GameActionData>(true);
            updateActions.instanceType = state.GetComponentTypeHandle<GameEntitySharedData>(true);
            updateActions.actionInfoType = state.GetComponentTypeHandle<GameEntityActionInfo>(true);
            updateActions.entityActionType = state.GetBufferTypeHandle<GameEntityAction>(true);
            updateActions.sharedActionType = state.GetBufferTypeHandle<GameEntitySharedAction>();

            state.Dependency = updateActions.ScheduleParallel(__groupToUpdate, state.Dependency);
        }
    }
}*/

[BurstCompile,
    CreateAfter(typeof(GameEntityActionLocationSystem)),
    CreateAfter(typeof(GamePhysicsWorldBuildSystem)),
    CreateAfter(typeof(GameEntityActionSharedFactorySytem)),
    UpdateInGroup(typeof(GameEntityActionSystemGroup))]
[WorldSystemFilter(WorldSystemFilterFlags.Presentation)]
public partial struct GameEntityActionSharedSystem : ISystem
{
    /*public struct Item
    {
        public uint negativeActionMask;
        public uint positiveActionMask;
    }

    public struct ActionObject
    {
        [Mask]
        public GameEntitySharedActionObjectFlag flag;

        public GameSharedActionType sourceType;
        public GameSharedActionType destinationType;

        public uint mask;
    }

    public struct ActionObjectRange
    {
        public int startIndex;
        public int count;
    }*/

    public struct Handler : IGameEntityActionHandler
    {
        /*[ReadOnly]
        public NativeArray<ActionObject> actionObjects;

        [ReadOnly]
        public NativeArray<ActionObjectRange> actionObjectRanges;

        [ReadOnly]
        public NativeArray<Item> items;*/

        public BlobAssetReference<GameEntityActionSharedDefinition> definition;

        [ReadOnly]
        public BufferLookup<GameEntityItem> entityItems;

        [ReadOnly, NativeDisableContainerSafetyRestriction]
        public ComponentLookup<Translation> translations;

        [ReadOnly, NativeDisableContainerSafetyRestriction]
        public ComponentLookup<Rotation> rotations;

        [ReadOnly]
        public ComponentLookup<GameEntitySharedActionType> actionTypes;

        [ReadOnly]
        public ComponentLookup<GameEntitySharedActionMask> actionMasks;

        /*[ReadOnly]
        public BufferLookup<GameEntitySharedAction> actions;*/

        public NativeFactory<EntityData<GameEntitySharedHit>>.ParallelWriter hits;

        public EntityCommandQueue<GameEntityActionSharedFactorySytem.Command>.ParallelWriter entityManager;

        /// <summary>
        /// 以前用来检测Rollback时候删除的技能被创建后特效不重复创建，现在没有意义了，因为原技能被删除后子特效全部被清除
        /// </summary>
        /// <param name="index"></param>
        /// <param name="version"></param>
        /// <param name="elapsedTime"></param>
        /// <param name="entity"></param>
        /// <returns></returns>
        public bool Check(int index, int version, float elapsedTime, Entity entity)
        {
            /*if (!this.actions.HasBuffer(entity))
                return false;

            var actions = this.actions[entity];
            GameEntitySharedAction action;
            int numActions = actions.Length;
            //string log = "";
            for (int i = 0; i < numActions; ++i)
            {
                action = actions[i];

                //log += action;

                if (action.index == index && action.version == version)
                {
                    if (action.elapsedTime < elapsedTime)
                    {
                        //Debug.LogError($"Fail: {action.index} : {action.elapsedTime} : {elapsedTime}");
                        break;
                    }

                    //Debug.Log("Ok:" + log + "(Index: " + index + ", Version" + version + ", Time: " + elapsedTime + ")");

                    return false;
                }
            }*/

            //Debug.Log(log + "(Index: " + index + ", Version" + version + ", Time: " + elapsedTime + ")");
            return true;
        }

        public uint ComputeActionMask(in Entity target, ref BlobArray<GameEntityActionSharedDefinition.Item> items)
        {
            uint actionMask = actionMasks.HasComponent(target) ? actionMasks[target].value : 0u,
                negativeActionMask = 0,
                positiveActionMask = 0;
            if (entityItems.HasBuffer(target))
            {
                var entityItems = this.entityItems[target];
                int numEntityItems = entityItems.Length, numItems = items.Length, itemIndex;
                for (int i = 0; i < numEntityItems; ++i)
                {
                    itemIndex = entityItems[i].index;
                    if (itemIndex < 0 || itemIndex >= numItems)
                        continue;

                    ref var item = ref items[itemIndex];

                    negativeActionMask |= item.negativeActionMask;
                    positiveActionMask |= item.positiveActionMask;
                }

                actionMask |= negativeActionMask;
                actionMask &= ~positiveActionMask;
            }

            return actionMask;
        }

        public bool Create(
            int index,
            double time,
            in float3 targetPosition,
            in Entity entity,
            in RigidTransform transform,
            in GameActionData data)
        {
            if (!Check(data.index, data.version, (float)(time - data.time), data.entity))
                return false;

            ref var definition = ref this.definition.Value;
            ref var action = ref definition.actions[data.actionIndex];

            GameEntityActionSharedFactorySytem.Command command;
            command.entity = Entity.Null;
            command.instance.time = data.time;
            /*command.instance.transform = math.RigidTransform(
                rotations.HasComponent(data.entity) ? rotations[data.entity].Value : quaternion.identity, 
                translations.HasComponent(data.entity) ? translations[data.entity].Value : float3.zero);*/
            command.instance.parentEntity = entity;

            bool result = false, isSourceTransformed = false, isDestinationTransformed = false;
            var actionType = actionTypes.HasComponent(data.entity) ? actionTypes[data.entity].value : 0;
            int actionObjectIndex, numActionObjects = action.objectIndices.Length;
            RigidTransform sourceTransform = RigidTransform.identity, destaintionTransform = RigidTransform.identity;
            for (int i = 0; i < numActionObjects; ++i)
            {
                actionObjectIndex = action.objectIndices[i];
                ref var actionObject = ref definition.actionObjects[actionObjectIndex];
                if ((actionObject.flag & GameEntitySharedActionObjectFlag.Create) == GameEntitySharedActionObjectFlag.Create &&
                    ((actionObject.sourceType & actionType) == actionType))
                {
                    //Debug.LogError($"Create Action {entity.Index} : {data.index} : {data.version} : {(float)(time - data.time)}");

                    command.instance.index = actionObjectIndex;

                    if ((actionObject.flag & GameEntitySharedActionObjectFlag.Source) == GameEntitySharedActionObjectFlag.Source)
                    {
                        command.instance.transform = RigidTransform.identity;
                        command.parent.value = data.entity;
                    }
                    else if ((actionObject.flag & GameEntitySharedActionObjectFlag.Destination) == GameEntitySharedActionObjectFlag.Destination)
                    {
                        if (!isDestinationTransformed)
                        {
                            if (!isSourceTransformed)
                            {
                                isSourceTransformed = __GetTransform(data.entity, out sourceTransform);
                                if (!isSourceTransformed)
                                {
                                    isSourceTransformed = true;

                                    sourceTransform = transform;
                                }
                            }

                            destaintionTransform.rot = sourceTransform.rot;
                            destaintionTransform.pos = targetPosition;

                            isDestinationTransformed = true;
                            /*isDestinationTransformed = __GetTransform(target, out destaintionTransform);
                            if (!isDestinationTransformed)
                            {
                                isDestinationTransformed = true;

                                destaintionTransform = transform;
                            }*/
                        }

                        command.instance.transform = destaintionTransform;
                        command.parent.value = Entity.Null;
                    }
                    else
                    {
                        if (!isSourceTransformed)
                        {
                            isSourceTransformed = __GetTransform(data.entity, out sourceTransform);
                            if (!isSourceTransformed)
                            {
                                isSourceTransformed = true;

                                sourceTransform = transform;
                            }
                        }

                        command.instance.transform = sourceTransform;
                        command.parent.value = Entity.Null;
                    }

                    entityManager.Enqueue(command);

                    result = true;
                }
            }

            return result;
        }

        public bool Init(
            int index,
            float elapsedTime,
            double time,
            in Entity entity,
            in RigidTransform transform,
            in GameActionData data)
        {
            if (!Check(data.index, data.version, (float)(time - data.time), data.entity))
                return false;

            ref var definition = ref this.definition.Value;
            ref var action = ref definition.actions[data.actionIndex];

            GameEntityActionSharedFactorySytem.Command command;
            command.entity = entity;
            command.instance.time = data.time + elapsedTime;
            command.instance.parentEntity = Entity.Null;

            bool result = false, isSourceTransformed = false;
            var actionType = actionTypes.HasComponent(data.entity) ? actionTypes[data.entity].value : 0;
            int actionObjectIndex, numActionObjects = action.objectIndices.Length;
            RigidTransform sourceTransform = RigidTransform.identity;
            for (int i = 0; i < numActionObjects; ++i)
            {
                actionObjectIndex = action.objectIndices[i];
                ref var actionObject = ref definition.actionObjects[actionObjectIndex];
                if ((actionObject.flag & GameEntitySharedActionObjectFlag.Init) == GameEntitySharedActionObjectFlag.Init &&
                    ((actionObject.sourceType & actionType) == actionType))
                {
                    //Debug.LogError($"Init Action {entity.Index} : {entity.Version} : {data.entity.Index}");

                    command.instance.index = actionObjectIndex;

                    if ((actionObject.flag & GameEntitySharedActionObjectFlag.Source) == GameEntitySharedActionObjectFlag.Source)
                    {
                        if ((actionObject.flag & GameEntitySharedActionObjectFlag.Buff) == GameEntitySharedActionObjectFlag.Buff)
                            command.instance.transform = RigidTransform.identity;
                        else
                        {
                            if (!isSourceTransformed)
                            {
                                isSourceTransformed = __GetTransform(data.entity, out sourceTransform);

                                sourceTransform = isSourceTransformed ? math.mul(math.inverse(sourceTransform), transform) : RigidTransform.identity;

                                isSourceTransformed = true;
                            }

                            command.instance.transform = sourceTransform;
                        }

                        command.parent.value = data.entity;
                    }
                    else
                    {
                        command.instance.transform = transform;
                        command.parent.value = Entity.Null;
                    }

                    entityManager.Enqueue(command);

                    result = true;
                }
            }

            return result;
        }

        public unsafe void Hit(
            int index,
            float elapsedTime,
            double time,
            in Entity entity,
            in Entity target,
            in RigidTransform transform,
            in GameActionData data)
        {
            if (!Check(data.index, data.version, (float)(time - data.time), data.entity))
                return;

            ref var definition = ref this.definition.Value;
            ref var action = ref definition.actions[data.actionIndex];

            GameEntityActionSharedFactorySytem.Command command;
            command.entity = Entity.Null;
            command.instance.time = data.time + elapsedTime;
            command.instance.parentEntity = Entity.Null;// (actionObject.flag & GameEntitySharedActionObjectFlag.Init) == GameEntitySharedActionObjectFlag.Init ? entity : Entity.Null;

            bool isSourceTransformed = false, isDestinationTransformed = false;
            int actionObjectIndex, numActionObjects = action.objectIndices.Length;
            uint actionMask = ComputeActionMask(target, ref definition.items);
            GameSharedActionType sourceActionType = actionTypes.HasComponent(data.entity) ? actionTypes[data.entity].value : 0,
                destinationActionType = actionTypes.HasComponent(target) ? actionTypes[target].value : 0;
            RigidTransform sourceTransform = RigidTransform.identity, destinationTranform = RigidTransform.identity;
            for (int i = 0; i < numActionObjects; ++i)
            {
                actionObjectIndex = action.objectIndices[i];

                ref var actionObject = ref definition.actionObjects[actionObjectIndex];
                if ((actionObject.flag & GameEntitySharedActionObjectFlag.Hit) == GameEntitySharedActionObjectFlag.Hit &&
                    (actionObject.sourceType & sourceActionType) == sourceActionType &&
                    (actionObject.destinationType & destinationActionType) == destinationActionType &&
                    (actionObject.mask == actionMask || (actionObject.mask & actionMask) != 0))
                {
                    //Debug.LogError($"Action Hit {entity.Index} : {data.index} : {data.version} : {(float)(time - data.time)} : {actionObjectIndex}");

                    //此处会加多次,不可行
                    /*if ((actionObject.flag & ActionObjectFlag.Init) == ActionObjectFlag.Init)
                        addComponentDataCommander.Enqueue(parent);
                    else*/
                    {
                        command.instance.index = actionObjectIndex;

                        if ((actionObject.flag & GameEntitySharedActionObjectFlag.Destination) == GameEntitySharedActionObjectFlag.Destination)
                        {
                            if ((actionObject.flag & GameEntitySharedActionObjectFlag.Buff) == GameEntitySharedActionObjectFlag.Buff)
                                command.instance.transform = RigidTransform.identity;
                            else
                            {
                                if (!isDestinationTransformed)
                                {
                                    isDestinationTransformed = __GetTransform(target, out destinationTranform);

                                    destinationTranform = isDestinationTransformed ? math.mul(math.inverse(destinationTranform), transform) : RigidTransform.identity;

                                    isDestinationTransformed = true;
                                }

                                command.instance.transform = destinationTranform;
                            }

                            command.parent.value = target;
                        }
                        else if ((actionObject.flag & GameEntitySharedActionObjectFlag.Source) == GameEntitySharedActionObjectFlag.Source)
                        {
                            if ((actionObject.flag & GameEntitySharedActionObjectFlag.Buff) == GameEntitySharedActionObjectFlag.Buff)
                                command.instance.transform = RigidTransform.identity;
                            else
                            {
                                if (!isSourceTransformed)
                                {
                                    isSourceTransformed = __GetTransform(data.entity, out sourceTransform);

                                    sourceTransform = isSourceTransformed ? math.mul(math.inverse(sourceTransform), transform) : RigidTransform.identity;

                                    isSourceTransformed = true;
                                }

                                command.instance.transform = sourceTransform;
                            }

                            command.parent.value = data.entity;
                        }
                        else
                        {
                            command.instance.transform = transform;
                            command.parent.value = Entity.Null;
                        }

                        entityManager.Enqueue(command);
                    }
                }
            }
        }

        public void Damage(
            int index,
            int count,
            float elapsedTime,
            double time,
            in Entity entity,
            in Entity target,
            in float3 position,
            in float3 normal,
            in GameActionData data)
        {
            if (!Check(data.index, data.version, (float)(time - data.time), data.entity))
                return;

            ref var definition = ref this.definition.Value;
            ref var action = ref definition.actions[data.actionIndex];

            EntityData<GameEntitySharedHit> hit;
            hit.value.version = data.version;
            hit.value.actionIndex = data.actionIndex;
            hit.value.time = data.time + elapsedTime;
            hit.value.entity = data.entity;
            hit.entity = target;
            hits.Create().value = hit;

            GameEntityActionSharedFactorySytem.Command command;
            command.entity = Entity.Null;
            command.instance.time = data.time + elapsedTime;
            command.instance.parentEntity = Entity.Null;

            bool isTransformed = false, isSourceTransformed = false, isDestinationTransformed = false;
            int actionObjectIndex, numActionObjects = action.objectIndices.Length;
            uint actionMask = ComputeActionMask(target, ref definition.items);
            GameSharedActionType sourceActionType = actionTypes.HasComponent(data.entity) ? actionTypes[data.entity].value : 0,
                destinationActionType = actionTypes.HasComponent(target) ? actionTypes[target].value : 0;
            float3 up = math.up();
            RigidTransform transform = RigidTransform.identity, sourceTransform = RigidTransform.identity, destinationTranform = RigidTransform.identity;
            for (int i = 0; i < numActionObjects; ++i)
            {
                actionObjectIndex = action.objectIndices[i];

                ref var actionObject = ref definition.actionObjects[actionObjectIndex];
                if (((actionObject.flag & GameEntitySharedActionObjectFlag.Damage) == GameEntitySharedActionObjectFlag.Damage/* || 
                    actionObject.flag == 0*/) &&
                    (actionObject.sourceType & sourceActionType) == sourceActionType &&
                    (actionObject.destinationType & destinationActionType) == destinationActionType &&
                    (actionObject.mask == actionMask || (actionObject.mask & actionMask) != 0))
                {
                    //Debug.LogError($"Action Damage {entity.Index} : {data.index} : {data.version} : {(float)(time - data.time)} : {actionObjectIndex}");

                    command.instance.index = actionObjectIndex;

                    if ((actionObject.flag & GameEntitySharedActionObjectFlag.Destination) == GameEntitySharedActionObjectFlag.Destination)
                    {
                        if ((actionObject.flag & GameEntitySharedActionObjectFlag.Buff) == GameEntitySharedActionObjectFlag.Buff)
                            command.instance.transform = RigidTransform.identity;
                        else
                        {
                            if (!isDestinationTransformed)
                            {
                                if (!isTransformed)
                                {
                                    isTransformed = true;

                                    transform = math.RigidTransform(quaternion.LookRotationSafe(-normal, up), position);
                                }

                                isDestinationTransformed = __GetTransform(target, out destinationTranform);

                                destinationTranform = isDestinationTransformed ? math.mul(math.inverse(destinationTranform), transform) : RigidTransform.identity;

                                isDestinationTransformed = true;
                            }

                            command.instance.transform = destinationTranform;
                        }

                        command.parent.value = target;
                    }
                    else if ((actionObject.flag & GameEntitySharedActionObjectFlag.Source) == GameEntitySharedActionObjectFlag.Source)
                    {
                        if ((actionObject.flag & GameEntitySharedActionObjectFlag.Buff) == GameEntitySharedActionObjectFlag.Buff)
                            command.instance.transform = RigidTransform.identity;
                        else
                        {
                            if (!isSourceTransformed)
                            {
                                if (!isTransformed)
                                {
                                    isTransformed = true;

                                    transform = math.RigidTransform(quaternion.LookRotationSafe(-normal, up), position);
                                }

                                isSourceTransformed = __GetTransform(data.entity, out sourceTransform);

                                sourceTransform = isSourceTransformed ? math.mul(math.inverse(sourceTransform), transform) : RigidTransform.identity;

                                isSourceTransformed = true;

                            }

                            command.instance.transform = sourceTransform;
                        }

                        command.parent.value = data.entity;
                    }
                    else
                    {
                        if (!isTransformed)
                        {
                            isTransformed = true;

                            transform = math.RigidTransform(quaternion.LookRotationSafe(-normal, up), position);
                        }

                        command.instance.transform = transform;
                        command.parent.value = Entity.Null;
                    }

                    entityManager.Enqueue(command);
                }
            }
        }

        public void Destroy(
            int index,
            float elapsedTime,
            double time,
            in Entity entity,
            in RigidTransform transform,
            in GameActionData data)
        {
        }

        private bool __GetTransform(in Entity entity, out RigidTransform transform)
        {
            if (!translations.HasComponent(entity))
            {
                transform = RigidTransform.identity;

                return false;
            }

            transform = math.RigidTransform(
                    rotations.HasComponent(entity) ? rotations[entity].Value : quaternion.identity,
                    translations[entity].Value);

            return true;
        }
    }

    public struct Factory : IGameEntityActionFactory<Handler>, IEntityCommandProducerJob
    {
        /*[ReadOnly]
        public NativeArray<ActionObject> actionObjects;

        [ReadOnly]
        public NativeArray<ActionObjectRange> actionObjectRanges;

        [ReadOnly]
        public NativeArray<Item> items;*/

        public BlobAssetReference<GameEntityActionSharedDefinition> definition;

        [ReadOnly]
        public BufferLookup<GameEntityItem> entityItems;

        [ReadOnly, NativeDisableContainerSafetyRestriction]
        public ComponentLookup<Translation> translations;

        [ReadOnly, NativeDisableContainerSafetyRestriction]
        public ComponentLookup<Rotation> rotations;

        [ReadOnly]
        public ComponentLookup<GameEntitySharedActionType> actionTypes;

        [ReadOnly]
        public ComponentLookup<GameEntitySharedActionMask> actionMasks;

        /*[ReadOnly]
        public BufferLookup<GameEntitySharedAction> actions;*/

        public NativeFactory<EntityData<GameEntitySharedHit>>.ParallelWriter hits;

        public EntityCommandQueue<GameEntityActionSharedFactorySytem.Command>.ParallelWriter entityManager;

        public Handler Create(in ArchetypeChunk chunk)
        {
            Handler handler;
            /*handler.actionObjects = actionObjects;
            handler.actionObjectRanges = actionObjectRanges;
            handler.items = items;*/
            handler.definition = definition;
            handler.entityItems = entityItems;
            handler.translations = translations;
            handler.rotations = rotations;
            handler.actionTypes = actionTypes;
            handler.actionMasks = actionMasks;
            //handler.actions = actions;
            handler.hits = hits;
            handler.entityManager = entityManager;

            return handler;
        }
    }

    [BurstCompile]
    public struct ApplyHits : IJob
    {
        public NativeFactory<EntityData<GameEntitySharedHit>> sources;

        public BufferLookup<GameEntitySharedHit> destinations;

        public void Execute()
        {
            EntityData<GameEntitySharedHit> value;
            var enumerator = sources.GetEnumerator();
            while (enumerator.MoveNext())
            {
                value = enumerator.Current;
                if (!destinations.HasBuffer(value.entity))
                    continue;

                destinations[value.entity].Add(value.value);
            }

            sources.Clear();
        }
    }

    [BurstCompile]
    private struct ClearHits : IJobChunk
    {
        public BufferTypeHandle<GameEntitySharedHit> hitType;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            var hits = chunk.GetBufferAccessor(ref hitType);

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                hits[i].Clear();
        }
    }

    private EntityQuery __hitGroup;

    /*private NativeArray<ActionObject> __actionObjects;
    private NativeArray<ActionObjectRange> __actionObjectRanges;
    private NativeArray<Item> __items;*/
    private NativeFactory<EntityData<GameEntitySharedHit>> __hits;
    //private EntityCommandQueue<GameEntityActionSharedFactorySytem.Command> __entityManager;
    private EntityCommandPool<GameEntityActionSharedFactorySytem.Command> __endFrameBarrier;
    private GameEntityActionSystemCore __core;

    private BufferLookup<GameEntityItem> __entityItems;
    private ComponentLookup<Translation> __translations;
    private ComponentLookup<Rotation> __rotations;
    private ComponentLookup<GameEntitySharedActionType> __actionTypes;
    private ComponentLookup<GameEntitySharedActionMask> __actionMasks;
    //private BufferLookup<GameEntitySharedAction> __actions;

    private BufferTypeHandle<GameEntitySharedHit> __hitType;
    private BufferLookup<GameEntitySharedHit> __hitResults;

    /*public void Create(
        NativeArray<Item> items,
        NativeArray<ActionObject> actionObjects,
        NativeArray<ActionObjectRange> actionObjectRanges)
    {
        if (__items.IsCreated)
            __items.Dispose();

        __items = new NativeArray<Item>(items.Length, Allocator.Persistent);

        NativeArray<Item>.Copy(items, __items);

        if (__actionObjects.IsCreated)
            __actionObjects.Dispose();

        __actionObjects = new NativeArray<ActionObject>(actionObjects.Length, Allocator.Persistent);

        NativeArray<ActionObject>.Copy(actionObjects, __actionObjects);

        if (__actionObjectRanges.IsCreated)
            __actionObjectRanges.Dispose();

        __actionObjectRanges = new NativeArray<ActionObjectRange>(actionObjectRanges.Length, Allocator.Persistent);

        NativeArray<ActionObjectRange>.Copy(actionObjectRanges, __actionObjectRanges);
    }*/

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __hitGroup = builder
                .WithAllRW<GameEntitySharedHit>()
                .Build(ref state);

        __hits = new NativeFactory<EntityData<GameEntitySharedHit>>(Allocator.Persistent, true);

        __endFrameBarrier = state.WorldUnmanaged.GetExistingSystemUnmanaged<GameEntityActionSharedFactorySytem>().pool;

        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __core = new GameEntityActionSystemCore(builder
                .WithAll<GameEntitySharedActionData>(),
                ref state);

        __entityItems = state.GetBufferLookup<GameEntityItem>(true);
        __translations = state.GetComponentLookup<Translation>(true);
        __rotations = state.GetComponentLookup<Rotation>(true);
        __actionTypes = state.GetComponentLookup<GameEntitySharedActionType>(true);
        __actionMasks = state.GetComponentLookup<GameEntitySharedActionMask>(true);
        //__actions = state.GetBufferLookup<GameEntitySharedAction>(true);

        __hitType = state.GetBufferTypeHandle<GameEntitySharedHit>();

        __hitResults = state.GetBufferLookup<GameEntitySharedHit>();
    }

    public void OnDestroy(ref SystemState state)
    {
        /*if (__items.IsCreated)
            __items.Dispose();

        if (__actionObjects.IsCreated)
            __actionObjects.Dispose();

        if (__actionObjectRanges.IsCreated)
            __actionObjectRanges.Dispose();*/

        __hits.Dispose();

        __core.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        /*if (!__actionObjects.IsCreated)
            return;*/
        if (!SystemAPI.HasSingleton<GameEntityActionSharedData>())
            return;

        ClearHits clearHits;
        clearHits.hitType = __hitType.UpdateAsRef(ref state);
        var jobHandle = clearHits.ScheduleParallel(__hitGroup, state.Dependency);

        var entityManager = __endFrameBarrier.Create();

        Factory factory;
        /*factory.actionObjects = __actionObjects;
        factory.actionObjectRanges = __actionObjectRanges;
        factory.items = __items;*/
        factory.definition = SystemAPI.GetSingleton<GameEntityActionSharedData>().definition;
        factory.entityItems = __entityItems.UpdateAsRef(ref state);
        factory.translations = __translations.UpdateAsRef(ref state);
        factory.rotations = __rotations.UpdateAsRef(ref state);
        factory.actionTypes = __actionTypes.UpdateAsRef(ref state);
        factory.actionMasks = __actionMasks.UpdateAsRef(ref state);
        //factory.actions = __actions.UpdateAsRef(ref state);
        factory.hits = __hits.parallelWriter;
        factory.entityManager = entityManager.parallelWriter;

        if (__core.Update<Handler, Factory>(factory, ref state))
        {
            var performJob = __core.performJob;

            entityManager.AddJobHandleForProducer<Factory>(performJob);

            ApplyHits applyHits;
            applyHits.sources = __hits;
            applyHits.destinations = __hitResults.UpdateAsRef(ref state);
            jobHandle = applyHits.ScheduleByRef(JobHandle.CombineDependencies(jobHandle, performJob));

            state.Dependency = JobHandle.CombineDependencies(jobHandle, state.Dependency);
        }
        else
            state.Dependency = jobHandle;
    }
}
