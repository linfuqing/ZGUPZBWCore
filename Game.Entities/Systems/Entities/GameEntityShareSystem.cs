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
//[assembly: RegisterGenericJobType(typeof(GameEntityActionSystemCore.PerformEx<GameEntityActionSharedSystem.Handler, GameEntityActionSharedSystem.Factory>))]

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
    private struct Instance
    {
        public int version;
        public GameObject gameObject;
    }
    
    private double __time;
    private GameDeadline __now;
    private EntityQuery __groupToCreate;
    private EntityQuery __groupToDestroy;
    private EntityQuery __groupToDestroyImmediate;
    private GameRollbackTime __rollbackTime;
    //private GameUpdateSystemGroup __updateSystemGroup;
    private EntityCommander __endFrameBarrier;

    private GameActionSharedObjectAsset[] __assets;
    private Pool<Instance> __instances;

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
                //Debug.LogError($"Create {instance.index} : {entity} : {gameObject.name} : {destroyTime}", gameObject);

                var playerDirector = gameObject.GetComponentInChildren<UnityEngine.Playables.PlayableDirector>();
                if (playerDirector != null)
                {
                    playerDirector.time = elpasedTime;
                    //playerDirector.Play()
                }
                else
                {
                    var particleSystem = gameObject.GetComponentInChildren<ParticleSystem>();
                    if (particleSystem != null)
                    {
                        //particleSystem.Play(true);

                        particleSystem.Simulate(elpasedTime, true);
                        particleSystem.Play(true);
                    }
                }

                if(destroyTime > 0.0f)
                    GameObject.Destroy(gameObject, destroyTime);
                else
                    GameObject.Destroy(gameObject);
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
            __instances = new Pool<Instance>();

        target.index = __instances.nextIndex;
        
        __instances.TryGetValue(target.index, out var temp);
        target.version = ++temp.version;
        
        temp.gameObject = gameObject;
        __instances.Insert(target.index, temp);

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
        if (__instances.TryGetValue(target.index, out var instance) && 
            instance.version == target.version)
        {
            if ((target.destroyStatus & status.value) == 0)
            {
                //isDisabled can be false after delete the action in rollback.
                //UnityEngine.Assertions.Assert.IsTrue(isDisabled, $"Destory Shared Action {entity} : {target.actionEntity} : {target.destroyStatus}");

                if (instance.gameObject != null)
                {
                    //UnityEngine.Debug.LogError($"Destroy {target.index} : {entity} : {gameObject.name} : {isDisabled}", gameObject);

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
                        if (target.destroyStatus == 0)
                            instance.gameObject.transform.SetParent(parent);
                        else if ((target.flag & GameActionSharedObjectFlag.EnableWhenBreak) == 0)
                            instance.gameObject.SetActive(false);
                    }
                    else
                        instance.gameObject.SetActive(false);

                    GameObject.Destroy(instance.gameObject, target.destroyTime);
                }
            }

            __instances.RemoveAt(target.index);
        }
        
        //__endFrameBarrier.RemoveComponent<GameActionSharedObject>(entity);

        if (entityManager.HasComponent<GameActionSharedObjectData>(entity))
            __endFrameBarrier.RemoveComponent<GameActionSharedObjectData>(entity);

        if (isDisabled)
        {
            //Debug.LogError($"Clear Managed {target.actionEntity} : {isDisabled} : {entityManager.HasComponent<GameEntitySharedActionChild>(target.actionEntity)}");
            if (!entityManager.HasComponent<GameEntitySharedActionChild>(target.actionEntity))
            {
                //Debug.LogError($"Clear Managed {target.actionEntity.Index} : {status.value}");

                var value = status.value & ~GameActionStatus.Status.Managed;
                if (value != status.value)
                {
                    status.value = value;

                    __endFrameBarrier.SetComponentData(target.actionEntity, status);
                }

                __endFrameBarrier.RemoveComponent<GameActionSharedObject>(entity);
            }
        }
        else
            __endFrameBarrier.RemoveComponent<GameActionSharedObject>(entity);
        
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
    /*CreateAfter(typeof(GameEntityActionLocationSystem)),
    CreateAfter(typeof(GamePhysicsWorldBuildSystem)),*/
    CreateAfter(typeof(GameEntityActionSharedFactorySytem)),
    CreateAfter(typeof(GameEntityActionSystem)), 
    UpdateInGroup(typeof(GameEntityActionSystemGroup), OrderLast = true)]
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

    private struct TransformLookup
    {
        [ReadOnly]
        public ComponentLookup<Translation> translations;

        [ReadOnly]
        public ComponentLookup<Rotation> rotations;

        public TransformLookup(ref SystemState state)
        {
            translations = state.GetComponentLookup<Translation>(true);
            rotations = state.GetComponentLookup<Rotation>(true);
        }

        public TransformLookup UpdateAsRef(ref SystemState state)
        {
            TransformLookup result;
            result.translations = translations.UpdateAsRef(ref state);
            result.rotations = rotations.UpdateAsRef(ref state);

            return result;
        }

        public bool Get(in Entity entity, out RigidTransform transform)
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

    private struct ActionMaskLookup
    {
        [ReadOnly]
        public BufferLookup<GameEntityItem> entityItems;

        [ReadOnly]
        public ComponentLookup<GameEntitySharedActionMask> actionMasks;

        public ActionMaskLookup(ref SystemState state)
        {
            entityItems = state.GetBufferLookup<GameEntityItem>(true);
            actionMasks = state.GetComponentLookup<GameEntitySharedActionMask>(true);
        }

        public ActionMaskLookup UpdateAsRef(ref SystemState state)
        {
            ActionMaskLookup result;
            result.entityItems = entityItems.UpdateAsRef(ref state);
            result.actionMasks = actionMasks.UpdateAsRef(ref state);

            return result;
        }

        public uint Compute(
            in Entity target,
            ref BlobArray<GameEntityActionSharedDefinition.Item> items)
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

    }

    [BurstCompile]
    private struct ApplyCreateors : IJobParallelForDefer, IEntityCommandProducerJob
    {
        public BlobAssetReference<GameEntityActionSharedDefinition> definition;

        public TransformLookup transformLookup;

        [ReadOnly]
        public SharedList<GameEntityActionCreator>.Reader creators;

        [ReadOnly]
        public ComponentLookup<GameActionData> instances;

        [ReadOnly]
        public ComponentLookup<GameActionDataEx> instancesEx;

        [ReadOnly]
        public ComponentLookup<GameEntitySharedActionType> actionTypes;

        [NativeDisableContainerSafetyRestriction]
        public ComponentLookup<GameActionStatus> states;

        public EntityCommandQueue<GameEntityActionSharedFactorySytem.Command>.ParallelWriter entityManager;

        public void Execute(int index)
        {
            /*if (!Check(data.index, data.version, (float)(time - data.time), data.entity))
                return false;*/

            var creator = creators[index];

            var instance = instances[creator.entity];

            ref var definition = ref this.definition.Value;
            ref var action = ref definition.actions[instance.actionIndex];

            GameEntityActionSharedFactorySytem.Command command;
            command.entity = Entity.Null;
            command.instance.time = instance.time;
            /*command.instance.transform = math.RigidTransform(
                rotations.HasComponent(data.entity) ? rotations[data.entity].Value : quaternion.identity, 
                translations.HasComponent(data.entity) ? translations[data.entity].Value : float3.zero);*/
            command.instance.parentEntity = creator.entity;

            bool result = false, isSourceTransformed = false, isDestinationTransformed = false;
            var actionType = actionTypes.HasComponent(instance.entity) ? actionTypes[instance.entity].value : 0;
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
                        command.parent.value = instance.entity;
                    }
                    else if ((actionObject.flag & GameEntitySharedActionObjectFlag.Destination) == GameEntitySharedActionObjectFlag.Destination)
                    {
                        if (!isDestinationTransformed)
                        {
                            if (!isSourceTransformed)
                            {
                                isSourceTransformed = transformLookup.Get(instance.entity, out sourceTransform);
                                if (!isSourceTransformed)
                                {
                                    isSourceTransformed = true;

                                    sourceTransform = instancesEx[creator.entity].transform;
                                }
                            }

                            destaintionTransform.rot = sourceTransform.rot;
                            destaintionTransform.pos = creator.targetPosition;

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
                        /*if (!isSourceTransformed)
                        {
                            isSourceTransformed = __GetTransform(data.entity, out sourceTransform);
                            if (!isSourceTransformed)
                            {
                                isSourceTransformed = true;

                                sourceTransform = transform;
                            }
                        }*/

                        command.instance.transform = instancesEx[creator.entity].transform;// sourceTransform;
                        command.parent.value = Entity.Null;
                    }

                    entityManager.Enqueue(command);

                    result = true;
                }
            }

            if (result)
            {
                var status = states[creator.entity];
                status.value |= GameActionStatus.Status.Managed;
                states[creator.entity] = status;
            }
        }
    }

    [BurstCompile]
    private struct ApplyInitializers : IJobParallelForDefer, IEntityCommandProducerJob
    {
        public BlobAssetReference<GameEntityActionSharedDefinition> definition;

        public TransformLookup transformLookup;

        [ReadOnly]
        public SharedList<GameEntityActionInitializer>.Reader initializers;

        [ReadOnly]
        public ComponentLookup<GameActionData> instances;

        [ReadOnly]
        public ComponentLookup<GameEntitySharedActionType> actionTypes;

        [NativeDisableContainerSafetyRestriction]
        public ComponentLookup<GameActionStatus> states;

        public EntityCommandQueue<GameEntityActionSharedFactorySytem.Command>.ParallelWriter entityManager;

        public void Execute(int index)
        {
            /*if (!Check(data.index, data.version, (float)(time - data.time), data.entity))
                return false;*/

            var initializer = initializers[index];

            var instance = instances[initializer.entity];

            ref var definition = ref this.definition.Value;
            ref var action = ref definition.actions[instance.actionIndex];

            GameEntityActionSharedFactorySytem.Command command;
            command.entity = initializer.entity;
            command.instance.time = instance.time + initializer.elapsedTime;
            command.instance.parentEntity = Entity.Null;

            bool result = false, isSourceTransformed = false;
            var actionType = actionTypes.HasComponent(instance.entity) ? actionTypes[instance.entity].value : 0;
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
                                isSourceTransformed = transformLookup.Get(instance.entity, out sourceTransform);

                                sourceTransform = isSourceTransformed ? math.mul(math.inverse(sourceTransform), initializer.transform) : RigidTransform.identity;

                                isSourceTransformed = true;
                            }

                            command.instance.transform = sourceTransform;
                        }

                        command.parent.value = instance.entity;
                    }
                    else
                    {
                        command.instance.transform = initializer.transform;
                        command.parent.value = Entity.Null;
                    }

                    entityManager.Enqueue(command);

                    result = true;
                }
            }

            if (result)
            {
                var status = states[initializer.entity];
                status.value |= GameActionStatus.Status.Managed;
                states[initializer.entity] = status;
            }
        }
    }

    [BurstCompile]
    private struct ApplyHiters : IJob, IEntityCommandProducerJob
    {
        public BlobAssetReference<GameEntityActionSharedDefinition> definition;

        public TransformLookup transformLookup;
        public ActionMaskLookup actionMaskLookup;

        [ReadOnly]
        public NativeFactory<GameEntityActionHiter> hiters;

        [ReadOnly]
        public ComponentLookup<GameActionData> instances;

        [ReadOnly]
        public ComponentLookup<GameEntitySharedActionType> actionTypes;

        public EntityCommandQueue<GameEntityActionSharedFactorySytem.Command>.ParallelWriter entityManager;

        public void Execute(in GameEntityActionHiter hiter)
        {
            /*if (!Check(data.index, data.version, (float)(time - data.time), data.entity))
                return false;*/

            var instance = instances[hiter.entity];

            ref var definition = ref this.definition.Value;
            ref var action = ref definition.actions[instance.actionIndex];

            GameEntityActionSharedFactorySytem.Command command;
            command.entity = Entity.Null;
            command.instance.time = instance.time + hiter.elapsedTime;
            command.instance.parentEntity = Entity.Null;// (actionObject.flag & GameEntitySharedActionObjectFlag.Init) == GameEntitySharedActionObjectFlag.Init ? entity : Entity.Null;

            bool isSourceTransformed = false, isDestinationTransformed = false;
            int actionObjectIndex, numActionObjects = action.objectIndices.Length;
            uint actionMask = actionMaskLookup.Compute(hiter.target, ref definition.items);
            GameSharedActionType sourceActionType = actionTypes.HasComponent(instance.entity) ? actionTypes[instance.entity].value : 0,
                destinationActionType = actionTypes.HasComponent(hiter.target) ? actionTypes[hiter.target].value : 0;
            RigidTransform sourceTransform = RigidTransform.identity, destinationTranform = RigidTransform.identity;
            for (int i = 0; i < numActionObjects; ++i)
            {
                actionObjectIndex = action.objectIndices[i];

                ref var actionObject = ref definition.actionObjects[actionObjectIndex];
                if ((actionObject.flag & GameEntitySharedActionObjectFlag.Hit) == GameEntitySharedActionObjectFlag.Hit &&
                    (actionObject.sourceType & sourceActionType) == sourceActionType &&
                    (actionObject.destinationType & destinationActionType) == destinationActionType &&
                    (actionObject.mask == actionMask || (actionObject.mask & actionMask) != 0 || actionObject.mask == uint.MaxValue))
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
                                    isDestinationTransformed = transformLookup.Get(hiter.target, out destinationTranform);

                                    destinationTranform = isDestinationTransformed ? math.mul(math.inverse(destinationTranform), hiter.transform) : RigidTransform.identity;

                                    isDestinationTransformed = true;
                                }

                                command.instance.transform = destinationTranform;
                            }

                            command.parent.value = hiter.target;
                        }
                        else if ((actionObject.flag & GameEntitySharedActionObjectFlag.Source) == GameEntitySharedActionObjectFlag.Source)
                        {
                            if ((actionObject.flag & GameEntitySharedActionObjectFlag.Buff) == GameEntitySharedActionObjectFlag.Buff)
                                command.instance.transform = RigidTransform.identity;
                            else
                            {
                                if (!isSourceTransformed)
                                {
                                    isSourceTransformed = transformLookup.Get(instance.entity, out sourceTransform);

                                    sourceTransform = isSourceTransformed ? math.mul(math.inverse(sourceTransform), hiter.transform) : RigidTransform.identity;

                                    isSourceTransformed = true;
                                }

                                command.instance.transform = sourceTransform;
                            }

                            command.parent.value = instance.entity;
                        }
                        else
                        {
                            command.instance.transform = hiter.transform;
                            command.parent.value = Entity.Null;
                        }

                        entityManager.Enqueue(command);
                    }
                }
            }
        }

        public void Execute()
        {
            foreach (var hit in hiters)
                Execute(hit);
        }
    }

    [BurstCompile]
    private struct ApplyDamagers : IJob, IEntityCommandProducerJob
    {
        public BlobAssetReference<GameEntityActionSharedDefinition> definition;

        public TransformLookup transformLookup;
        public ActionMaskLookup actionMaskLookup;

        [ReadOnly]
        public NativeFactory<GameEntityActionDamager> damagers;

        [ReadOnly]
        public ComponentLookup<GameActionData> instances;

        [ReadOnly]
        public ComponentLookup<GameEntitySharedActionType> actionTypes;

        public NativeFactory<EntityData<GameEntitySharedHit>>.ParallelWriter hits;

        public EntityCommandQueue<GameEntityActionSharedFactorySytem.Command>.ParallelWriter entityManager;

        public void Execute(in GameEntityActionDamager damager)
        {
            /*if (!Check(data.index, data.version, (float)(time - data.time), data.entity))
                return false;*/

            var instance = instances[damager.entity];

            ref var definition = ref this.definition.Value;
            ref var action = ref definition.actions[instance.actionIndex];

            EntityData<GameEntitySharedHit> hit;
            hit.value.version = instance.version;
            hit.value.actionIndex = instance.actionIndex;
            hit.value.time = instance.time + damager.elapsedTime;
            hit.value.entity = instance.entity;
            hit.entity = damager.target;
            hits.Create().value = hit;

            GameEntityActionSharedFactorySytem.Command command;
            command.entity = Entity.Null;
            command.instance.time = instance.time + damager.elapsedTime;
            command.instance.parentEntity = Entity.Null;

            bool isTransformed = false, isSourceTransformed = false, isDestinationTransformed = false;
            int actionObjectIndex, numActionObjects = action.objectIndices.Length;
            uint actionMask = actionMaskLookup.Compute(damager.target, ref definition.items);
            GameSharedActionType sourceActionType = actionTypes.HasComponent(instance.entity) ? actionTypes[instance.entity].value : 0,
                destinationActionType = actionTypes.HasComponent(damager.target) ? actionTypes[damager.target].value : 0;
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
                    (actionObject.mask == actionMask || (actionObject.mask & actionMask) != 0 || actionObject.mask == uint.MaxValue))
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

                                    transform = math.RigidTransform(quaternion.LookRotationSafe(-damager.normal, up), damager.position);
                                }

                                isDestinationTransformed = transformLookup.Get(damager.target, out destinationTranform);

                                destinationTranform = isDestinationTransformed ? math.mul(math.inverse(destinationTranform), transform) : RigidTransform.identity;

                                isDestinationTransformed = true;
                            }

                            command.instance.transform = destinationTranform;
                        }

                        command.parent.value = damager.target;
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

                                    transform = math.RigidTransform(quaternion.LookRotationSafe(-damager.normal, up), damager.position);
                                }

                                isSourceTransformed = transformLookup.Get(instance.entity, out sourceTransform);

                                sourceTransform = isSourceTransformed ? math.mul(math.inverse(sourceTransform), transform) : RigidTransform.identity;

                                isSourceTransformed = true;

                            }

                            command.instance.transform = sourceTransform;
                        }

                        command.parent.value = instance.entity;
                    }
                    else
                    {
                        if (!isTransformed)
                        {
                            isTransformed = true;

                            transform = math.RigidTransform(quaternion.LookRotationSafe(-damager.normal, up), damager.position);
                        }

                        command.instance.transform = transform;
                        command.parent.value = Entity.Null;
                    }

                    entityManager.Enqueue(command);
                }
            }
        }

        public void Execute()
        {
            foreach (var damager in damagers)
                Execute(damager);
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

    private EntityQuery __definitionGroup;

    private EntityQuery __hitGroup;

    private TransformLookup __transformLookup;

    private ActionMaskLookup __actionMaskLookup;

    private ComponentLookup<GameActionData> __instances;

    private ComponentLookup<GameActionDataEx> __instancesEx;

    private ComponentLookup<GameActionStatus> __states;

    private ComponentLookup<GameEntitySharedActionType> __actionTypes;

    private BufferTypeHandle<GameEntitySharedHit> __hitType;
    private BufferLookup<GameEntitySharedHit> __hitResults;

    private EntityCommandPool<GameEntityActionSharedFactorySytem.Command> __endFrameBarrier;

    private GameEntityActionManager __actionManager;

    private NativeFactory<EntityData<GameEntitySharedHit>> __hits;

    public readonly static int InnerloopBatchCount = 4;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __definitionGroup = builder
                .WithAll<GameEntityActionSharedData>()
                //.WithOptions(EntityQueryOptions.IncludeSystems)
                .Build(ref state);

        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __hitGroup = builder
                .WithAllRW<GameEntitySharedHit>()
                .Build(ref state);

        /*using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __core = new GameEntityActionSystemCore(builder
                .WithAll<GameEntitySharedActionData>(),
                ref state);*/

        __transformLookup = new TransformLookup(ref state);
        __actionMaskLookup = new ActionMaskLookup(ref state);

        __instances = state.GetComponentLookup<GameActionData>(true);
        __instancesEx = state.GetComponentLookup<GameActionDataEx>(true);

        __states = state.GetComponentLookup<GameActionStatus>(true);

        __actionTypes = state.GetComponentLookup<GameEntitySharedActionType>(true);
        //__actions = state.GetBufferLookup<GameEntitySharedAction>(true);

        __hitType = state.GetBufferTypeHandle<GameEntitySharedHit>();

        __hitResults = state.GetBufferLookup<GameEntitySharedHit>();


        var world = state.WorldUnmanaged;

        __endFrameBarrier = world.GetExistingSystemUnmanaged<GameEntityActionSharedFactorySytem>().pool;

        __actionManager = world.GetExistingSystemUnmanaged<GameEntityActionSystem>().actionManager;

        __hits = new NativeFactory<EntityData<GameEntitySharedHit>>(Allocator.Persistent, true);
    }

    public void OnDestroy(ref SystemState state)
    {
        __hits.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!__definitionGroup.HasSingleton<GameEntityActionSharedData>())
            return;

        var inputDeps = state.Dependency;

        var definition = __definitionGroup.GetSingleton<GameEntityActionSharedData>().definition;
        var transformLookup = __transformLookup.UpdateAsRef(ref state);
        var actionMaskLookup = __actionMaskLookup.UpdateAsRef(ref state);
        var instances = __instances.UpdateAsRef(ref state);
        var instancesEx = __instancesEx.UpdateAsRef(ref state);
        var states = __states.UpdateAsRef(ref state);
        var actionTypes = __actionTypes.UpdateAsRef(ref state);

        var commandCreators = __endFrameBarrier.Create();
        var creators = __actionManager.creators;

        ApplyCreateors applyCreateors;
        applyCreateors.definition = definition;
        applyCreateors.transformLookup = transformLookup;
        applyCreateors.creators = creators.reader;
        applyCreateors.instances = instances;
        applyCreateors.instancesEx = instancesEx;
        applyCreateors.states = states;
        applyCreateors.actionTypes = actionTypes;
        //applyCreateors.hits = __hits.parallelWriter;
        applyCreateors.entityManager = commandCreators.parallelWriter;

        ref var creatersJobManager = ref creators.lookupJobManager;
        var applyCreateorsJobHandle = JobHandle.CombineDependencies(creatersJobManager.readOnlyJobHandle, inputDeps);
        applyCreateorsJobHandle = applyCreateors.ScheduleByRef(creators.AsList(), InnerloopBatchCount, applyCreateorsJobHandle);

        creatersJobManager.AddReadOnlyDependency(applyCreateorsJobHandle);
        commandCreators.AddJobHandleForProducer<ApplyCreateors>(applyCreateorsJobHandle);

        var commandInitializers = __endFrameBarrier.Create();
        var initializers = __actionManager.initializers;

        ApplyInitializers applyInitializers;
        applyInitializers.definition = definition;
        applyInitializers.transformLookup = transformLookup;
        applyInitializers.initializers = initializers.reader;
        applyInitializers.instances = instances;
        applyInitializers.states = states;
        applyInitializers.actionTypes = actionTypes;
        applyInitializers.entityManager = commandInitializers.parallelWriter;

        ref var initializersJobManager = ref initializers.lookupJobManager;
        var applyInitializersJobHandle = JobHandle.CombineDependencies(initializersJobManager.readOnlyJobHandle, inputDeps);
        applyInitializersJobHandle = applyInitializers.ScheduleByRef(initializers.AsList(), InnerloopBatchCount, applyInitializersJobHandle);

        initializersJobManager.AddReadOnlyDependency(applyInitializersJobHandle);
        commandInitializers.AddJobHandleForProducer<ApplyInitializers>(applyInitializersJobHandle);

        var commandHiters = __endFrameBarrier.Create();
        var hiters = __actionManager.hiters;

        ApplyHiters applyHiters;
        applyHiters.definition = definition;
        applyHiters.transformLookup = transformLookup;
        applyHiters.actionMaskLookup = actionMaskLookup;
        applyHiters.hiters = hiters.value;
        applyHiters.instances = instances;
        applyHiters.actionTypes = actionTypes;
        applyHiters.entityManager = commandHiters.parallelWriter;

        ref var hitersJobManager = ref hiters.lookupJobManager;
        var applyHitersJobHandle = JobHandle.CombineDependencies(hitersJobManager.readOnlyJobHandle, inputDeps);
        applyHitersJobHandle = applyHiters.ScheduleByRef(applyHitersJobHandle);

        hitersJobManager.AddReadOnlyDependency(applyHitersJobHandle);
        commandHiters.AddJobHandleForProducer<ApplyHiters>(applyHitersJobHandle);

        var commandDamagers = __endFrameBarrier.Create();
        var damagers = __actionManager.damagers;

        ApplyDamagers applyDamagers;
        applyDamagers.definition = definition;
        applyDamagers.transformLookup = transformLookup;
        applyDamagers.actionMaskLookup = actionMaskLookup;
        applyDamagers.damagers = damagers.value;
        applyDamagers.instances = instances;
        applyDamagers.actionTypes = actionTypes;
        applyDamagers.hits = __hits.parallelWriter;
        applyDamagers.entityManager = commandDamagers.parallelWriter;

        ref var damagersJobManager = ref damagers.lookupJobManager;
        var applyDamagersJobHandle = JobHandle.CombineDependencies(damagersJobManager.readOnlyJobHandle, inputDeps);
        applyDamagersJobHandle = applyDamagers.ScheduleByRef(applyDamagersJobHandle);

        damagersJobManager.AddReadOnlyDependency(applyDamagersJobHandle);
        commandDamagers.AddJobHandleForProducer<ApplyDamagers>(applyDamagersJobHandle);

        ClearHits clearHits;
        clearHits.hitType = __hitType.UpdateAsRef(ref state);
        var jobHandle = clearHits.ScheduleParallelByRef(__hitGroup, inputDeps);

        ApplyHits applyHits;
        applyHits.sources = __hits;
        applyHits.destinations = __hitResults.UpdateAsRef(ref state);
        applyDamagersJobHandle = applyHits.ScheduleByRef(JobHandle.CombineDependencies(jobHandle, applyDamagersJobHandle));

        state.Dependency = JobHandle.CombineDependencies(
            JobHandle.CombineDependencies(
                applyCreateorsJobHandle, 
                applyInitializersJobHandle, 
                applyHitersJobHandle), 
            applyDamagersJobHandle);
    }
}
