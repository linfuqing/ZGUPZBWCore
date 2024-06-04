using System;
using System.Collections.Generic;
using Unity.Jobs;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Physics;
using Unity.Transforms;
using ZG;
using PhysicsRaycastColliderToIgnore = ZG.Entities.Physics.PhysicsRaycastColliderToIgnore;

public enum GameInputButton
{
    Down,
    Hold,
    Up
}

public interface IGameInputActionFilter
{
    bool Check(int actionIndex, double time);
}

public struct GameInputTarget : IComparable<GameInputTarget>
{
    public float distance;
    public Entity entity;

    public int CompareTo(GameInputTarget other)
    {
        return distance.CompareTo(other.distance);
    }
}

public struct GameInputActionDefinition
{
    private struct Comparer : IComparer<int>
    {
        public NativeArray<Action> actions;
        public DynamicBuffer<GameEntityActorActionData> actorActions;
        public DynamicBuffer<GameInputActionInstance> actionInstances;

        public int Compare(int x, int y)
        {
            return actions[actorActions[actionInstances[x].actorActionIndex].actionIndex].priority.CompareTo(actions[actorActions[actionInstances[y].actorActionIndex].actionIndex].priority);
        }
    }

    public struct PreAction
    {
        public int actionIndex;

        public float time;

        public static int FindIndex(int actionIndex, float time, ref BlobArray<PreAction> preActions)
        {
            int numPreActions = preActions.Length;
            for (int i = 0; i < numPreActions; ++i)
            {
                ref var preAction = ref preActions[i];
                if (preAction.actionIndex == actionIndex && preAction.time <= time)
                    return i;
            }

            return -1;
        }
    }

    public struct Action
    {
        public GameInputButton button;

        public GameActionTargetType targetType;

        public int layerMask;

        public int actorStatusMask;

        public int group;

        public int priority;

        //public uint actorMask;

        public float maxSpeed;
        public float minDot;
        public float maxDot;

        public float distance;

        public BlobArray<PreAction> preActions;

        public bool Did(
            GameInputButton button,
            GameActionTargetType targetType,
            int layerMask,
            int group,
            int preActionIndex,
            int actorStatus,
            float actorVelocity,
            float dot,
            float delta/*,
            double time,
            in GameEntityActorTime actorTime*/)
        {
            if (layerMask != 0 && this.layerMask != 0 && (layerMask & this.layerMask) == 0 ||
                targetType != 0 && this.targetType != 0 && (targetType & this.targetType) == 0)
                return false;

            if (this.button == button &&
                this.group == group &&
                //actorTime.Did(actorMask, time) &&
                (((actorStatusMask == 0 ? 1 : actorStatusMask) & (1 << actorStatus)) != 0) &&
                    !(maxSpeed > math.FLT_MIN_NORMAL && maxSpeed < actorVelocity) &&
                    (maxDot > minDot ? maxDot >= dot && minDot <= dot : true) &&
                    (preActionIndex == -1 ? preActions.Length < 1 :
                    preActions.Length > 0 &&
                    PreAction.FindIndex(preActionIndex, delta, ref preActions) != -1))
                return true;

            return false;
        }
    }

    public BlobArray<Action> actions;

    public bool Did<T>(
        GameInputButton button,
        int group,
        int actorStatus,
        float actorVelocity,
        float dot,
        float delta,
        double time,
        //in GameEntityActorTime actorTime,
        in DynamicBuffer<GameEntityActorActionInfo> actorActionInfos,
        in DynamicBuffer<GameEntityActorActionData> actorActions,
        in DynamicBuffer<GameInputActionInstance> actionInstances,
        ref int actorActionIndex,
        ref int layerMask,
        ref GameActionTargetType targetType,
        ref T filter,
        out float distance) where T : IGameInputActionFilter
    {
        /*layerMask = 0;
        targetType = 0;*/
        distance = 0.0f;

        /*if (actorActionIndex == 20 && button == GameInputButton.Down && group == 2)
            UnityEngine.Debug.Log("-");*/

        int numActionInstances = actionInstances.Length;
        var indices = new FixedList512Bytes<int>();
        for (int i = 0; i < numActionInstances; ++i)
            indices.Add(i);

        Comparer comparer;
        comparer.actions = actions.AsArray();
        comparer.actorActions = actorActions;
        comparer.actionInstances = actionInstances;

        indices.Sort(comparer);

        GameEntityActorActionData actorAction;
        GameInputActionInstance actionInstance;
        if (actorActionIndex != -1)
        {
            int preActionIndex = actorActions[actorActionIndex].actionIndex;
            for (int i = 0; i < numActionInstances; ++i)
            {
                actionInstance = actionInstances[indices[i]];
                if (actionInstance.activeCount > 0)
                {
                    /*if (actorActionIndex == 132)
                    {
                        if(actionInstance.actorActionIndex == 133)
                            UnityEngine.Debug.LogError(delta);
                    }*/
                    
                    actorAction = actorActions[actionInstance.actorActionIndex];

                    //if (actorAction.actionIndex == 19 && actorActionIndex == 3)
                    /*if (actorAction.actionIndex == 2 && preActionIndex == 19)
                    {
                        UnityEngine.Debug.Log("?");
                    }*/

                    ref var action = ref actions[actorAction.actionIndex];
                    if (action.Did(
                        button,
                        targetType,
                        layerMask,
                        group,
                        preActionIndex,
                        actorStatus,
                        actorVelocity,
                        dot,
                        delta/*,
                        time,
                        actorTime*/) &&
                        filter.Check(actorAction.actionIndex, time) &&
                        (actorActionInfos.Length <= actionInstance.actorActionIndex || actorActionInfos[actionInstance.actorActionIndex].coolDownTime < time))
                    {
                        /*if (actionInstance.actorActionIndex == 20 && actorActionIndex == 16)
                        {
                            UnityEngine.Debug.LogError('-');
                        }*/
                        //if (actorActionInfos[actionInstance.actorActionIndex].coolDownTime < time)
                        {
                            actorActionIndex = actionInstance.actorActionIndex;
                            layerMask = action.layerMask;
                            targetType = action.targetType;
                            distance = action.distance;

                            return true;
                        }

                        //break;
                    }
                }
            }
        }

        for (int i = 0; i < numActionInstances; ++i)
        {
            actionInstance = actionInstances[indices[i]];
            if (actionInstance.activeCount > 0)
            {
                actorAction = actorActions[actionInstance.actorActionIndex];

                /*if (actorActionIndex == 20 && actionInstance.actorActionIndex == 3 && button == GameInputButton.Down && group == 2)
                    UnityEngine.Debug.Log("1");*/

                ref var action = ref actions[actorAction.actionIndex];
                if (action.Did(
                        button,
                        targetType,
                        layerMask,
                        group,
                        -1,
                        actorStatus,
                        actorVelocity,
                        dot,
                        delta/*,
                        time,
                        actorTime*/) &&
                        filter.Check(actorAction.actionIndex, time) &&
                        (actorActionInfos.Length <= actionInstance.actorActionIndex || actorActionInfos[actionInstance.actorActionIndex].coolDownTime < time))
                {
                    /*if (actionInstance.actorActionIndex == 20)
                    {
                        UnityEngine.Debug.LogError('-');
                    }*/
                    
                    actorActionIndex = actionInstance.actorActionIndex;
                    layerMask = action.layerMask;
                    targetType = action.targetType;
                    distance = action.distance;

                    return true;
                }
            }
        }

        return false;
    }
}

public struct GameInputActionData : IComponentData
{
    public float maxDistance;
    public BlobAssetReference<GameInputActionDefinition> definition;
}

public struct GameInputAction : IComponentData
{
    public struct Filter : IGameInputActionFilter
    {
        public readonly float Rage;
        public readonly GameEntityActorTime ActorTime;
        public readonly BlobAssetReference<GameActionSetDefinition> ActionSetDefinition;

        public Filter(
            float rage, 
            in GameEntityActorTime actorTime, 
            in BlobAssetReference<GameActionSetDefinition> actionSetDefinition)
        {
            Rage = rage;
            ActorTime = actorTime;
            ActionSetDefinition = actionSetDefinition;
        }

        public bool Check(int actionIndex, double time)
        {
            ref var action = ref ActionSetDefinition.Value.values[actionIndex];
            return  Rage >= action.info.rageCost && ActorTime.Did(action.instance.actorMask, time);
        }
    }

    public GameActionTargetType targetType;

    public int actorActionIndex;

    public int layerMask;

    public float distance;

    public double minActionTime;
    public double maxActionTime;

    //public double targetTime;
    public Entity target;

    public static bool Check(GameActionTargetType type, int sourceCamp, int destinationCamp)
    {
        if ((type & GameActionTargetType.Ally) != 0 && sourceCamp == destinationCamp)
            return true;

        if ((type & GameActionTargetType.Enemy) != 0 && sourceCamp != destinationCamp)
            return true;

        return false;
    }

    public bool Predicate(
        int camp,
        in Entity entity,
        in ComponentLookup<GameNodeStatus> states,
        in ComponentLookup<GameEntityCamp> camps,
        in ComponentLookup<PhysicsShapeCompoundCollider> colliders)
    {
        if(!states.HasComponent(entity) || ((GameEntityStatus)states[entity].value & GameEntityStatus.Mask) == GameEntityStatus.Dead)
            return false;

        if(layerMask != 0)
        {
            if (!colliders.HasComponent(entity))
                return false;

            var collider = colliders[entity].value;
            if (!collider.IsCreated)
                return false;

            if ((collider.Value.Filter.BelongsTo & layerMask) == 0)
                return false;
        }

        return /*states.HasComponent(entity) &&
           (((GameEntityStatus)states[entity].value & GameEntityStatus.Mask) != GameEntityStatus.Dead) &&
           (layerMask == 0 || colliders.HasComponent(entity) && (colliders[entity].value.Value.Filter.BelongsTo & layerMask) != 0) &&*/
           Check(targetType == 0 ? GameActionTargetType.Enemy : targetType, camp, camps[entity].value);
    }

    public bool Did(
        GameInputButton button,
        int actorActionIndex,
        int group,
        int camp,
        int actorStatus,
        float actorVelocity,
        float dot,
        float rage,
        float maxDistance,
        //float targetTime,
        //float responseTime,
        double oldTime, 
        double time,
        in float3 position,
        //in float3 forward,
        //in Vector3 direction,
        in Entity selection,
        in GameEntityActorTime actorTime,
        in SharedList<GameInputTarget>.Reader targets,
        in ComponentLookup<Translation> translations,
        in ComponentLookup<PhysicsShapeCompoundCollider> colliders,
        in ComponentLookup<GameNodeStatus> states,
        in ComponentLookup<GameEntityCamp> camps,
        in DynamicBuffer<GameEntityActorActionInfo> actorActionInfos,
        in DynamicBuffer<GameEntityActorActionData> actorActions,
        in DynamicBuffer<GameInputActionInstance> actionInstances,
        in DynamicBuffer<GameEntityItem> items,
        in BlobAssetReference<GameInputActionDefinition> definition,
        in BlobAssetReference<GameActionSetDefinition> actionSetDefinition,
        in BlobAssetReference<GameActionItemSetDefinition> actionItemSetDefinition,
        out bool isTimeout)
    {
        isTimeout = math.clamp(maxActionTime, oldTime, time) > maxActionTime;
        if (isTimeout || 
            !states.HasComponent(target) ||
            (((GameEntityStatus)states[target].value & GameEntityStatus.Mask) == GameEntityStatus.Dead) ||
            math.distancesq(translations[target].Value, position) > this.distance * this.distance)
        {
            layerMask = 0;
            targetType = 0;
            this.distance = 0.0f;
            target = Entity.Null;
        }

        float artTime = 0.0f;
        if (items.IsCreated)
        {
            ref var actionItems = ref actionItemSetDefinition.Value.values;
            int numItems = items.Length, length = actionItems.Length;
            GameEntityItem item;
            for (int i = 0; i < numItems; ++i)
            {
                item = items[i];

                if (item.index >= 0 && item.index < length)
                {
                    ref var actionItem = ref actionItems[item.index];

                    rage -= actionItem.rageCost;
                    artTime += actionItem.artTime;
                }
            }
        }

        bool result = false;
        float delta = (float)(time - minActionTime), distance = 0.0f;
        var filter = new Filter(rage, actorTime, actionSetDefinition);
        if (actorActionIndex != -1 && actorActionIndex != this.actorActionIndex)
        {
            distance = 0.0f;

            var actorAction = actorActions[actorActionIndex];

            ref var action = ref definition.Value.actions[actorAction.actionIndex];
            result = action.Did(
                button,
                targetType,
                layerMask,
                group,
                isTimeout || this.actorActionIndex == -1 || action.preActions.Length < 1 ? -1 : actorActions[this.actorActionIndex].actionIndex,
                actorStatus,
                actorVelocity,
                dot,
                delta) &&
                filter.Check(actorAction.actionIndex, time);

            if (result)
            {
                result = actorActionInfos.Length <= actorActionIndex || actorActionInfos[actorActionIndex].coolDownTime < time;
                if (result)
                    distance = action.distance;
            }
        }

        if(!result)
        {
            actorActionIndex = isTimeout ? -1 : this.actorActionIndex;

            result = definition.Value.Did(
                button,
                group,
                actorStatus,
                actorVelocity,
                dot, //math.normalizesafe(direction, forward),
                delta,
                time,
                //actorTime, 
                actorActionInfos,
                actorActions,
                actionInstances,
                ref actorActionIndex,
                ref layerMask,
                ref targetType,
                ref filter,
                out distance);
        }

        if (result)
        {
            if (target == Entity.Null && targetType != 0)
            {
                if (Predicate(camp, selection, states, camps, colliders))
                    target = selection;

                this.distance = math.max(distance, maxDistance);

                if (target == Entity.Null)
                {
                    GameInputTarget target;
                    int numTargets = targets.length;
                    for (int i = 0; i < numTargets; ++i)
                    {
                        target = targets[i];
                        if (Predicate(camp, target.entity, states, camps, colliders) &&
                            target.distance < this.distance)
                        {
                            this.target = target.entity;

                            break;
                        }
                    }
                }
            }

            this.actorActionIndex = actorActionIndex;

            minActionTime = time - math.DBL_MIN_NORMAL;// + performTime;
            maxActionTime = time + artTime + actionSetDefinition.Value.values[actorActions[actorActionIndex].actionIndex].info.artTime;// (artTime + responseTime);

            return true;
        }

        return false;
    }
}

public struct GameInputActionTarget : IComponentData
{
    public GameInputStatus.Value status;
    public int actorActionIndex;
    public float3 direction;
    public Entity entity;

    public bool isDo => GameInputStatus.IsDo(status);
}

public struct GameInputActionCommand : IComponentData, IEnableableComponent
{
    public GameInputButton button;
    public int actorActionIndex;
    public int group;
    public float3 direction;
}

public struct GameInputActionInstance : IBufferElementData
{
    public int actorActionIndex;
    public int activeCount;
}

public struct GameInputSelection : IComponentData
{
    public Entity entity;
}

public struct GameInputSelectionDisabled : IComponentData
{

}

public struct GameInputStatus : IComponentData
{
    public enum Value
    {
        Normal,
        Select,
        KeyDown,
        KeyHold,
        KeyUp,
        KeyUpAndDown,
        KeyDownAndUp,
        KeyHoldAndUp,
        KeyUpAndDownAndUp
    }

    public double time;
    public Value value;

    public bool isDo => IsDo(value);

    public static bool IsDo(Value value) => value > Value.Select;

    public static Value As(GameInputButton value) => (Value)((int)value + (int)Value.KeyDown);

    public static GameInputButton As(Value value) => (GameInputButton)((int)value - (int)Value.KeyDown);
}

public struct GameInputRaycast : IComponentData
{
    public uint raycasterMask;
    public uint obstacleMask;

    //public float3 raycastCenter;
}

public struct GameInputKey : IBufferElementData
{
    public enum Status
    {
        Down,
        Up,
        Click
    }

    public enum Value
    {
        Select,
        Do
    }

    public Status status;
    public Value value;
}

public struct GameInputSelectable : IComponentData
{

}

public struct GameInputPickable : IComponentData
{

}

public struct GameInputEntity : IComponentData
{

}

public struct GameInputSelectionTarget : IComponentData
{
    public Entity entity;
}

public struct GameInputGuideTarget : IComponentData
{
    public Entity entity;
}

[AutoCreateIn("Client"), BurstCompile, CreateAfter(typeof(GamePhysicsWorldBuildSystem)), UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
public partial struct GameInputSystem : ISystem
{
    [BurstCompile]
    private struct OverlapHits : IJob
    {
        public uint raycasterMask;

        public Aabb aabb;

        [ReadOnly]
        public CollisionWorldContainer collisionWorld;

        //[ReadOnly]
        //public ComponentLookup<GameInputPickable> pickables;

        public NativeList<int> hits;

        public NativeParallelHashMap<Entity, float> targetDistances;

        public void Execute()
        {
            hits.Clear();
            targetDistances.Clear();

            CollisionFilter filter = default;
            filter.GroupIndex = 0;
            filter.CollidesWith = ~0u;
            filter.BelongsTo = raycasterMask;

            OverlapAabbInput overlapAabbInput;
            overlapAabbInput.Aabb = aabb;
            overlapAabbInput.Filter = filter;

            if (((CollisionWorld)collisionWorld).OverlapAabb(overlapAabbInput, ref hits))
                targetDistances.Capacity = math.max(targetDistances.Capacity, hits.Length);
        }
    }

    private struct FindTargets
    {
        public uint raycasterMask;
        public uint obstacleMask;

        public FrustumPlanes frustumPlanes;
        [ReadOnly]
        public CollisionWorld collisionWorld;
        [ReadOnly]
        public NativeArray<int> hits;
        [ReadOnly]
        public NativeArray<Entity> entityArray;
        public NativeParallelHashMap<Entity, float>.ParallelWriter targetDistances;

        public void Execute(int index)
        {
            Entity entity = entityArray[index];
            int rigidbodyIndex = collisionWorld.GetRigidBodyIndex(entity);
            if (rigidbodyIndex == -1)
                return;

            CollisionFilter filter = default;
            filter.GroupIndex = 0;
            filter.CollidesWith = ~0u;
            filter.BelongsTo = raycasterMask;

            RaycastInput raycastInput = default;
            raycastInput.Filter = filter;
            raycastInput.Filter.BelongsTo = ~0u;
            raycastInput.Filter.CollidesWith = obstacleMask;

            var rigidbodies = collisionWorld.Bodies;
            var rigidbody = rigidbodies[rigidbodyIndex];
            float3 position = math.transform(rigidbody.WorldFromBody, rigidbody.Collider.Value.MassProperties.MassDistribution.Transform.pos);

            PointDistanceInput pointDistanceInput = default;
            pointDistanceInput.MaxDistance = float.MaxValue;
            pointDistanceInput.Position = position;
            pointDistanceInput.Filter = filter;

            int count = hits.Length;
            Aabb aabb;
            //Box box;
            DistanceHit distanceHit;
            for (int i = 0; i < count; ++i)
            {
                rigidbody = rigidbodies[hits[i]];
                if (rigidbody.Entity == entity)
                    continue;

                aabb = rigidbody.Collider.Value.CalculateAabb(rigidbody.WorldFromBody);
                //box = new Box(aabb.Center + rigidbody.WorldFromBody.pos, aabb.Extents, rigidbody.WorldFromBody.rot);
                if (frustumPlanes.Intersect(aabb.Center, aabb.Extents) == FrustumPlanes.IntersectResult.Out)
                    continue;

                if (!rigidbody.CalculateDistance(pointDistanceInput, out distanceHit))
                    continue;

                raycastInput.Start = distanceHit.Position;
                raycastInput.End = position;
                if (collisionWorld.CastRay(raycastInput))
                    continue;

                targetDistances.TryAdd(rigidbody.Entity, distanceHit.Distance);
            }
        }
    }

    [BurstCompile]
    private struct FindTargetsEx : IJobChunk
    {
        public uint raycasterMask;
        public uint obstacleMask;

        public FrustumPlanes frustumPlanes;
        [ReadOnly]
        public CollisionWorldContainer collisionWorld;
        [ReadOnly]
        public NativeArray<int> hits;
        [ReadOnly]
        public EntityTypeHandle entityType;
        public NativeParallelHashMap<Entity, float>.ParallelWriter targetDistances;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            FindTargets findTargets;
            findTargets.raycasterMask = raycasterMask;
            findTargets.obstacleMask = obstacleMask;
            findTargets.frustumPlanes = frustumPlanes;
            findTargets.collisionWorld = collisionWorld;
            findTargets.hits = hits;
            findTargets.entityArray = chunk.GetNativeArray(entityType);
            findTargets.targetDistances = targetDistances;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                findTargets.Execute(i);
        }
    }

    [BurstCompile]
    private struct SortTargets : IJob
    {
        public NativeParallelHashMap<Entity, float> targetDistances;

        public NativeList<GameInputTarget> targets;

        public void Execute()
        {
            targets.Clear();

            GameInputTarget target;
            foreach (var targetDistance in targetDistances)
            {
                target.distance = targetDistance.Value;
                target.entity = targetDistance.Key;

                targets.Add(target);
            }

            targets.Sort();
        }
    }

    private struct Select
    {
        //public int builtInCamps;
        public float maxDistance;

        public Entity selection;

        public BlobAssetReference<GameActionSetDefinition> actionSetDefinition;

        [ReadOnly]
        public NativeArray<GameInputTarget> targets;

        [ReadOnly]
        public ComponentLookup<PhysicsCollider> colliders;

        [ReadOnly]
        public ComponentLookup<PhysicsRaycastColliderToIgnore> physicsRaycastCollidersToIgnore;

        [ReadOnly]
        public ComponentLookup<GameInputSelectionDisabled> selectionDisabled;

        [ReadOnly]
        public ComponentLookup<GameInputSelectable> selectables;

        [ReadOnly]
        public ComponentLookup<GameInputEntity> entities;

        [ReadOnly]
        public ComponentLookup<GameInputPickable> pickables;

        [ReadOnly]
        public ComponentLookup<GameFactory> factories;

        [ReadOnly]
        public ComponentLookup<GameNodeStatus> states;

        [ReadOnly]
        public ComponentLookup<GameEntityCamp> campMap;

        [ReadOnly]
        public BufferAccessor<GameEntityActorActionData> actorActions;

        [ReadOnly]
        public BufferAccessor<GameInputActionInstance> actionInstances;

        [ReadOnly]
        public NativeArray<GameEntityCamp> camps;

        public NativeArray<GameInputSelectionTarget> results;
        
        public bool IsSelectable(
            in Entity entity,
            int camp,
            in DynamicBuffer<GameEntityActorActionData> actorActions,
            in DynamicBuffer<GameInputActionInstance> actionInstances)
        {
            if (physicsRaycastCollidersToIgnore.HasComponent(entity) && physicsRaycastCollidersToIgnore.IsComponentEnabled(entity))
                return false;
            
            if (selectionDisabled.HasComponent(entity))
                return false;
            
            if (states.HasComponent(entity))
            {
                var status = states[entity].value & (int)GameEntityStatus.Mask;
                if ((int)GameEntityStatus.Dead == status)
                    return false;
                else if ((int)GameEntityStatus.KnockedOut == status)
                    return true;
            }

            if (pickables.HasComponent(entity))
                return true;

            if (factories.HasComponent(entity))
            {
                if (factories[entity].status == GameFactoryStatus.Complete)
                    return true;
            }

            if (campMap.HasComponent(entity))
            {
                int targetCamp = campMap[entity].value;
                if (targetCamp == camp)
                {
                    if (selectables.HasComponent(entity))
                        return true;
                }
                else if (!entities.HasComponent(entity) && colliders.HasComponent(entity))
                {
                    uint belongsTo = colliders[entity].Value.Value.Filter.BelongsTo;
                    foreach (var actionInstance in actionInstances)
                    {
                        if (actionInstance.activeCount > 0)
                        {
                            ref var action = ref actionSetDefinition.Value.values[actorActions[actionInstance.actorActionIndex].actionIndex];
                            if ((action.instance.damageMask & belongsTo) != 0)
                                return true;
                        }
                    }
                }
            }

            return false;
        }

        public void Execute(int index)
        {
            GameInputSelectionTarget result;

            var actionInstances = this.actionInstances[index];
            var actorActions = this.actorActions[index];
            int camp = camps[index].value;
            if (IsSelectable(selection, camp, actorActions, actionInstances))
                result.entity = selection;
            else
            {
                result.entity = Entity.Null;

                //ref var actionSetDefinition = ref this.actionSetDefinition.Value;

                foreach (var target in targets)
                {
                    if (target.distance > maxDistance)
                        continue;

                    if (IsSelectable(target.entity, camp, actorActions, actionInstances))
                    {
                        result.entity = target.entity;

                        break;
                    }
                }
            }

            results[index] = result;
        }
    }

    [BurstCompile]
    private struct SelectEx : IJobChunk
    {
        //public int builtInCamps;

        public float maxDistance;

        public Entity selection;

        public BlobAssetReference<GameActionSetDefinition> actionSetDefinition;

        [ReadOnly]
        public SharedList<GameInputTarget>.Reader targets;

        [ReadOnly]
        public ComponentLookup<PhysicsCollider> colliders;

        [ReadOnly]
        public ComponentLookup<PhysicsRaycastColliderToIgnore> physicsRaycastCollidersToIgnore;

        [ReadOnly]
        public ComponentLookup<GameInputSelectionDisabled> selectionDisabled;

        [ReadOnly]
        public ComponentLookup<GameInputSelectable> selectables;

        [ReadOnly]
        public ComponentLookup<GameInputEntity> entities;

        [ReadOnly]
        public ComponentLookup<GameInputPickable> pickables;

        [ReadOnly]
        public ComponentLookup<GameFactory> factories;

        [ReadOnly]
        public ComponentLookup<GameNodeStatus> states;

        [ReadOnly]
        public ComponentLookup<GameEntityCamp> camps;

        [ReadOnly]
        public ComponentTypeHandle<GameEntityCamp> campType;

        [ReadOnly]
        public BufferTypeHandle<GameEntityActorActionData> actorActionType;

        [ReadOnly]
        public BufferTypeHandle<GameInputActionInstance> actionInstanceType;

        public ComponentTypeHandle<GameInputSelectionTarget> resultType;

        public bool IsSelectable(in Entity entity)
        {
            if (states.HasComponent(entity))
            {
                var status = states[entity].value & (int)GameEntityStatus.Mask;
                if ((int)GameEntityStatus.Dead == status)
                    return false;
                
                if ((int)GameEntityStatus.KnockedOut == status)
                    return true;
            }

            if (pickables.HasComponent(entity))
                return true;

            if (factories.HasComponent(entity))
            {
                if (factories[entity].status == GameFactoryStatus.Complete)
                    return true;
            }

            return false;
        }

        /*public Entity Select(in NativeArray<GameInputTarget> targets)
        {
            foreach (var target in targets)
            {
                if (IsSelectable(target.entity))
                    return target.entity;
            }

            return Entity.Null;
        }*/

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            var targets = this.targets.AsArray();
            var selection = this.selection == Entity.Null || !IsSelectable(this.selection) ? Entity.Null/*Select(targets)*/ : this.selection;
            if (selection == Entity.Null)
            {
                Select select;
                //select.builtInCamps = builtInCamps;
                select.maxDistance = maxDistance;
                select.selection = this.selection;
                select.actionSetDefinition = actionSetDefinition;
                select.targets = targets;
                select.colliders = colliders;
                select.physicsRaycastCollidersToIgnore = physicsRaycastCollidersToIgnore;
                select.selectionDisabled = selectionDisabled;
                select.selectables = selectables;
                select.entities = entities;
                select.pickables = pickables;
                select.factories = factories;
                select.states = states;
                select.campMap = camps;
                select.camps = chunk.GetNativeArray(ref campType);
                select.actorActions = chunk.GetBufferAccessor(ref actorActionType);
                select.actionInstances = chunk.GetBufferAccessor(ref actionInstanceType);
                select.results = chunk.GetNativeArray(ref resultType);

                var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (iterator.NextEntityIndex(out int i))
                    select.Execute(i);
            }
            else
            {
                GameInputSelectionTarget result;

                var results = chunk.GetNativeArray(ref resultType);

                var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (iterator.NextEntityIndex(out int i))
                {
                    result.entity = selection;
                    results[i] = result;
                }
            }
        }
    }

    [BurstCompile]
    private struct GuideStart : IJob
    {
        public int counter;
        
        public GameQuestGuideManager manager;

        public NativeList<int> guideResults;

        public void Execute()
        {
            foreach (var guideResult in guideResults)
                manager.Remove(GameQuestGuideVariantType.Entity, guideResult, 1);

            guideResults.Clear();
            guideResults.Capacity = math.max(guideResults.Capacity, counter);
        }
    }

    [BurstCompile]
    private struct GuideEnd : IJob
    {
        public GameQuestGuideManager manager;

        public NativeList<int> guideResults;

        public void Execute()
        {
            foreach (var guideResult in guideResults)
                manager.Add(GameQuestGuideVariantType.Entity, guideResult, 1);
        }
    }

    private struct Guide
    {
        public BlobAssetReference<GameActionSetDefinition> actionSetDefinition;

        public GameQuestGuideManager.ReadOnly manager;

        [ReadOnly]
        public NativeArray<GameInputTarget> targets;

        [ReadOnly]
        public ComponentLookup<PhysicsCollider> colliders;

        [ReadOnly]
        public ComponentLookup<NetworkIdentityType> identityTypes;

        [ReadOnly]
        public ComponentLookup<GameInputPickable> pickables;

        [ReadOnly]
        public ComponentLookup<GameFactory> factories;

        [ReadOnly]
        public ComponentLookup<GameNodeStatus> states;

        [ReadOnly]
        public ComponentLookup<GameEntityCamp> campMap;

        [ReadOnly]
        public NativeArray<GameEntityCamp> camps;

        [ReadOnly]
        public BufferAccessor<GameEntityActorActionData> actorActions;

        [ReadOnly]
        public BufferAccessor<GameInputActionInstance> actionInstances;

        public NativeArray<GameInputGuideTarget> guideTargets;

        public NativeList<int>.ParallelWriter guideResults;

        public void Execute(int index)
        {
            GameInputGuideTarget guideTarget;
            guideTarget.entity = Entity.Null;

            ref var actionSetDefinition = ref this.actionSetDefinition.Value;

            var actionInstances = this.actionInstances[index];
            var actorActions = this.actorActions[index];
            int camp = camps[index].value;
            uint belongsTo;
            bool isContains;
            foreach (var target in targets)
            {
                if (states.HasComponent(target.entity) && (((GameEntityStatus)states[target.entity].value & GameEntityStatus.Mask) == GameEntityStatus.Dead))
                    continue;

                if (!identityTypes.HasComponent(target.entity) || !manager.IsPublished(GameQuestGuideVariantType.Entity, identityTypes[target.entity].value))
                    continue;

                if (!pickables.HasComponent(target.entity))
                {
                    isContains = campMap.HasComponent(target.entity) && campMap[target.entity].value == camp;
                    if (factories.HasComponent(target.entity))
                    {
                        if (factories[target.entity].status != GameFactoryStatus.Complete &&
                            !isContains)
                            continue;
                    }
                    else
                    {
                        if (!isContains)
                        {
                            if (colliders.HasComponent(target.entity))
                            {
                                belongsTo = colliders[target.entity].Value.Value.Filter.BelongsTo;
                                foreach (var actionInstance in actionInstances)
                                {
                                    if (actionInstance.activeCount > 0)
                                    {
                                        ref var action =
                                            ref actionSetDefinition.values[
                                                actorActions[actionInstance.actorActionIndex].actionIndex];
                                        if ((action.instance.damageMask & belongsTo) != 0)
                                        {
                                            isContains = true;

                                            break;
                                        }
                                    }
                                }
                            }
                        }
                        
                        if(!isContains)
                            continue;
                    }
                }
                
                guideTarget.entity = target.entity;

                break;
            }

            guideTargets[index] = guideTarget;
            
            if(guideTarget.entity != Entity.Null)
                guideResults.AddNoResize(identityTypes[guideTarget.entity].value);
        }
    }

    [BurstCompile]
    private struct GuideEx : IJobChunk
    {
        public BlobAssetReference<GameActionSetDefinition> actionSetDefinition;

        public GameQuestGuideManager.ReadOnly manager;

        [ReadOnly]
        public SharedList<GameInputTarget>.Reader targets;

        [ReadOnly]
        public ComponentLookup<PhysicsCollider> colliders;

        [ReadOnly]
        public ComponentLookup<NetworkIdentityType> identityTypes;

        [ReadOnly]
        public ComponentLookup<GameInputPickable> pickables;

        [ReadOnly]
        public ComponentLookup<GameFactory> factories;

        [ReadOnly]
        public ComponentLookup<GameNodeStatus> states;

        [ReadOnly]
        public ComponentLookup<GameEntityCamp> camps;

        [ReadOnly]
        public ComponentTypeHandle<GameEntityCamp> campType;

        [ReadOnly]
        public BufferTypeHandle<GameEntityActorActionData> actorActionType;

        [ReadOnly]
        public BufferTypeHandle<GameInputActionInstance> actionInstanceType;

        public ComponentTypeHandle<GameInputGuideTarget> guideTargetType;
        
        public NativeList<int>.ParallelWriter guideResults;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Guide guide;
            guide.actionSetDefinition = actionSetDefinition;
            guide.manager = manager;
            guide.targets = targets.AsArray();
            guide.colliders = colliders;
            guide.identityTypes = identityTypes;
            guide.pickables = pickables;
            guide.factories = factories;
            guide.states = states;
            guide.campMap = camps;
            guide.camps = chunk.GetNativeArray(ref campType);
            guide.actorActions = chunk.GetBufferAccessor(ref actorActionType);
            guide.actionInstances = chunk.GetBufferAccessor(ref actionInstanceType);
            guide.guideTargets = chunk.GetNativeArray(ref guideTargetType);
            guide.guideResults = guideResults;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                guide.Execute(i);
        }
    }

    private EntityQuery __group;

    private EntityTypeHandle __entityType;

    private ComponentLookup<PhysicsCollider> __colliders;

    private ComponentLookup<NetworkIdentityType> __identityTypes;

    private ComponentLookup<PhysicsRaycastColliderToIgnore> __physicsRaycastCollidersToIgnore;

    private ComponentLookup<GameInputSelectionDisabled> __selectionDisabled;

    private ComponentLookup<GameInputSelectable> __selectables;

    private ComponentLookup<GameInputEntity> __entities;

    private ComponentLookup<GameInputPickable> __pickables;

    private ComponentLookup<GameFactory> __factories;

    private ComponentLookup<GameNodeStatus> __states;

    private ComponentLookup<GameEntityCamp> __camps;

    private ComponentTypeHandle<GameEntityCamp> __campType;

    private BufferTypeHandle<GameEntityActorActionData> __actorActionType;

    private BufferTypeHandle<GameInputActionInstance> __actionInstanceType;

    private ComponentTypeHandle<GameInputSelectionTarget> __selectionTargetType;

    private ComponentTypeHandle<GameInputGuideTarget> __guideTargetType;

    private SharedPhysicsWorld __physicsWorld;

    private NativeParallelHashMap<Entity, float> __targetDistances;

    private NativeList<int> __hits;

    private NativeList<int> __guideResults;

    public static readonly float MaxDistance = 4.0f;

    public SharedList<GameInputTarget> targets
    {
        get;

        private set;
    }

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __group = builder
                .WithAll<GameInputStatus, GameEntityCamp, GameEntityActorActionData>()
                .Build(ref state);

        __entityType = state.GetEntityTypeHandle();
        __colliders = state.GetComponentLookup<PhysicsCollider>(true);
        __identityTypes = state.GetComponentLookup<NetworkIdentityType>(true);
        __physicsRaycastCollidersToIgnore = state.GetComponentLookup<PhysicsRaycastColliderToIgnore>(true);
        __selectionDisabled = state.GetComponentLookup<GameInputSelectionDisabled>(true);
        __selectables = state.GetComponentLookup<GameInputSelectable>(true);
        __entities = state.GetComponentLookup<GameInputEntity>(true);
        __pickables = state.GetComponentLookup<GameInputPickable>(true);
        __factories = state.GetComponentLookup<GameFactory>(true);
        __states = state.GetComponentLookup<GameNodeStatus>(true);
        __camps = state.GetComponentLookup<GameEntityCamp>(true);
        __campType = state.GetComponentTypeHandle<GameEntityCamp>(true);
        __actorActionType = state.GetBufferTypeHandle<GameEntityActorActionData>(true);
        __actionInstanceType = state.GetBufferTypeHandle<GameInputActionInstance>(true);
        __selectionTargetType = state.GetComponentTypeHandle<GameInputSelectionTarget>();
        __guideTargetType = state.GetComponentTypeHandle<GameInputGuideTarget>();

        __physicsWorld = state.WorldUnmanaged.GetExistingSystemUnmanaged<GamePhysicsWorldBuildSystem>().physicsWorld;

        __targetDistances = new NativeParallelHashMap<Entity, float>(1, Allocator.Persistent);

        __hits = new NativeList<int>(Allocator.Persistent);

        __guideResults = new NativeList<int>(Allocator.Persistent);

        targets = new SharedList<GameInputTarget>(Allocator.Persistent);
    }

    //[BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        __targetDistances.Dispose();

        __hits.Dispose();

        __guideResults.Dispose();

        targets.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.HasSingleton<MainCameraFrustum>() ||
            !SystemAPI.HasSingleton<GameInputRaycast>() ||
            !SystemAPI.HasSingleton<GameActionSetData>())
            return;

        var mainCameraFrustum = SystemAPI.GetSingleton<MainCameraFrustum>();
        var raycast = SystemAPI.GetSingleton<GameInputRaycast>();

        var collisionWorld = __physicsWorld.collisionWorld;

        OverlapHits overlapHits;
        overlapHits.raycasterMask = raycast.raycasterMask;
        overlapHits.aabb.Min = mainCameraFrustum.center - mainCameraFrustum.extents;
        overlapHits.aabb.Max = mainCameraFrustum.center + mainCameraFrustum.extents;
        overlapHits.collisionWorld = collisionWorld;
        overlapHits.hits = __hits;
        overlapHits.targetDistances = __targetDistances;

        //overlapHits.pickables = __pickables.UpdateAsRef(ref state);

        ref var physicsWorldJobManager = ref __physicsWorld.lookupJobManager;

        var jobHandle = overlapHits.ScheduleByRef(JobHandle.CombineDependencies(physicsWorldJobManager.readOnlyJobHandle, state.Dependency));

        FindTargetsEx findTargets;
        findTargets.raycasterMask = raycast.raycasterMask;
        findTargets.obstacleMask = raycast.obstacleMask;
        findTargets.frustumPlanes = mainCameraFrustum.frustumPlanes;
        findTargets.collisionWorld = collisionWorld;
        findTargets.hits = __hits.AsDeferredJobArray();
        findTargets.entityType = __entityType.UpdateAsRef(ref state);
        findTargets.targetDistances = __targetDistances.AsParallelWriter();
        jobHandle = findTargets.ScheduleParallelByRef(__group, jobHandle);

        physicsWorldJobManager.AddReadOnlyDependency(jobHandle);

        var targets = this.targets;
        ref var targetsJobManager = ref targets.lookupJobManager;

        SortTargets sortTargets;
        sortTargets.targetDistances = __targetDistances;
        sortTargets.targets = targets.writer;
        jobHandle = sortTargets.ScheduleByRef(JobHandle.CombineDependencies(targetsJobManager.readWriteJobHandle, jobHandle));

        var actionSetDefinition = SystemAPI.GetSingleton<GameActionSetData>().definition;

        var colliders = __colliders.UpdateAsRef(ref state);
        var pickables = __pickables.UpdateAsRef(ref state);
        var factories = __factories.UpdateAsRef(ref state);
        var states = __states.UpdateAsRef(ref state);
        var camps = __camps.UpdateAsRef(ref state);
        var campType = __campType.UpdateAsRef(ref state);
        var actorActionType = __actorActionType.UpdateAsRef(ref state);
        var actionInstanceType = __actionInstanceType.UpdateAsRef(ref state);

        var targetsReader = targets.reader;

        SelectEx select;
        select.maxDistance = MaxDistance;
        //select.builtInCamps = (int)GameDataConstans.BuiltInCamps;
        select.selection = SystemAPI.HasSingleton<GameInputSelection>() ? SystemAPI.GetSingleton<GameInputSelection>().entity : Entity.Null;
        select.actionSetDefinition = actionSetDefinition;
        select.targets = targetsReader;
        select.colliders = colliders;
        select.physicsRaycastCollidersToIgnore = __physicsRaycastCollidersToIgnore.UpdateAsRef(ref state);
        select.selectionDisabled = __selectionDisabled.UpdateAsRef(ref state);
        select.selectables = __selectables.UpdateAsRef(ref state);
        select.entities = __entities.UpdateAsRef(ref state);
        select.pickables = pickables;
        select.factories = factories;
        select.states = states;
        select.camps = camps;
        select.campType = campType;
        select.actorActionType = actorActionType;
        select.actionInstanceType = actionInstanceType;
        select.resultType = __selectionTargetType.UpdateAsRef(ref state);

        var submitJobHandle = select.ScheduleParallelByRef(__group, jobHandle);

        var questGuideManagerShared = SystemAPI.GetSingleton<GameQuestGuideManagerShared>();
        var questGuideManager = questGuideManagerShared.value;

        GuideStart guideStart;
        guideStart.counter = __group.CalculateEntityCount(); 
        guideStart.manager = questGuideManager;
        guideStart.guideResults = __guideResults;
        
        ref var questGuideJobManager = ref questGuideManagerShared.lookupJobManager;

        var questGuideJobHandle =
            guideStart.ScheduleByRef(JobHandle.CombineDependencies(questGuideJobManager.readWriteJobHandle, jobHandle));

        GuideEx guide;
        guide.actionSetDefinition = actionSetDefinition;
        guide.manager = questGuideManager.readOnly;
        guide.targets = targetsReader;
        guide.identityTypes = __identityTypes.UpdateAsRef(ref state);
        guide.colliders = colliders;
        guide.pickables = pickables;
        guide.factories = factories;
        guide.states = states;
        guide.camps = camps;
        guide.campType = campType;
        guide.actorActionType = actorActionType;
        guide.actionInstanceType = actionInstanceType;
        guide.guideTargetType = __guideTargetType.UpdateAsRef(ref state);
        guide.guideResults = __guideResults.AsParallelWriter();

        questGuideJobHandle = guide.ScheduleParallelByRef(__group, questGuideJobHandle);

        GuideEnd guideEnd;
        guideEnd.manager = questGuideManager;
        guideEnd.guideResults = __guideResults;
        questGuideJobHandle = guideEnd.ScheduleByRef(questGuideJobHandle);

        questGuideJobManager.AddReadOnlyDependency(questGuideJobHandle);

        state.Dependency = JobHandle.CombineDependencies(submitJobHandle, questGuideJobHandle);
    }
}

//[UpdateInGroup(typeof(PresentationSystemGroup), OrderFirst = true)]
[AutoCreateIn("Client"), 
 BurstCompile, 
 CreateAfter(typeof(GameInputSystem))]//, UpdateInGroup(typeof(GameRollbackSystemGroup), OrderFirst = true)]
public partial struct GameInputActionSystem : ISystem
{
    public struct Apply
    {
        public float maxDistance;
        public double time;

        public Entity selection;

        public BlobAssetReference<GameInputActionDefinition> definition;
        public BlobAssetReference<GameActionSetDefinition> actionSetDefinition;
        public BlobAssetReference<GameActionItemSetDefinition> actionItemSetDefinition;

        [ReadOnly]
        public SharedList<GameInputTarget>.Reader targets;

        [ReadOnly]
        public ComponentLookup<Translation> translations;

        [ReadOnly]
        public ComponentLookup<Rotation> rotations;

        [ReadOnly]
        public ComponentLookup<PhysicsShapeCompoundCollider> colliders;

        [ReadOnly]
        public ComponentLookup<GameNodeParent> parents;

        [ReadOnly]
        public ComponentLookup<GameNodeStatus> nodeStates;

        [ReadOnly]
        public ComponentLookup<GameNodeVelocity> velocities;

        [ReadOnly]
        public ComponentLookup<GameNodeActorStatus> actorStates;

        [ReadOnly]
        public ComponentLookup<GameEntityActorTime> actorTimes;

        [ReadOnly]
        public ComponentLookup<GameEntityRage> rages;

        [ReadOnly]
        public ComponentLookup<GameEntityCamp> camps;

        [ReadOnly]
        public BufferLookup<GameEntityItem> items;

        [ReadOnly]
        public BufferLookup<GameEntityActorActionData> actorActions;

        [ReadOnly]
        public BufferLookup<GameEntityActorActionInfo> actorActionInfos;

        [ReadOnly]
        public BufferLookup<GameInputActionInstance> actionInstances;

        [ReadOnly]
        public NativeArray<GameInputKey> keys;

        [ReadOnly]
        public NativeArray<Entity> entityArray;

        [ReadOnly]
        public NativeArray<GameNodeDirection> directions;

        public NativeArray<GameInputActionTarget> actionTargets;

        public NativeArray<GameInputAction> actions;

        public NativeArray<GameInputStatus> states;

        public static GameInputStatus.Value CollectKeys(GameInputStatus.Value value, in NativeArray<GameInputKey> keys)
        {
            foreach (var key in keys)
            {
                switch (key.value)
                {
                    case GameInputKey.Value.Select:
                        if (key.status == GameInputKey.Status.Down)
                            value = GameInputStatus.Value.Select;
                        else if (value == GameInputStatus.Value.Select)
                            value = GameInputStatus.Value.Normal;
                        break;
                    case GameInputKey.Value.Do:
                        switch (value)
                        {
                            case GameInputStatus.Value.KeyDown:
                                switch (key.status)
                                {
                                    case GameInputKey.Status.Up:
                                        value = GameInputStatus.Value.Normal;
                                        break;
                                    case GameInputKey.Status.Click:
                                        value = GameInputStatus.Value.KeyDownAndUp;
                                        break;
                                }
                                break;
                            case GameInputStatus.Value.KeyHold:
                                switch (key.status)
                                {
                                    case GameInputKey.Status.Up:
                                        value = GameInputStatus.Value.KeyHoldAndUp;
                                        break;
                                    case GameInputKey.Status.Click:
                                        value = GameInputStatus.Value.KeyDownAndUp;
                                        break;
                                }
                                break;
                            case GameInputStatus.Value.KeyUp:
                                switch (key.status)
                                {
                                    case GameInputKey.Status.Down:
                                        value = GameInputStatus.Value.KeyUpAndDown;
                                        break;
                                    case GameInputKey.Status.Click:
                                        value = GameInputStatus.Value.KeyUpAndDownAndUp;
                                        break;
                                }
                                if (key.status == GameInputKey.Status.Down)
                                    value = GameInputStatus.Value.KeyUpAndDown;
                                break;
                            case GameInputStatus.Value.KeyUpAndDown:
                                switch (key.status)
                                {
                                    case GameInputKey.Status.Up:
                                        value = GameInputStatus.Value.KeyUp;
                                        break;
                                    case GameInputKey.Status.Click:
                                        value = GameInputStatus.Value.KeyDownAndUp;
                                        break;
                                }
                                break;
                            case GameInputStatus.Value.KeyDownAndUp:
                            case GameInputStatus.Value.KeyHoldAndUp:
                            case GameInputStatus.Value.KeyUpAndDownAndUp:
                                break;
                            default:
                                switch (key.status)
                                {
                                    case GameInputKey.Status.Down:
                                        value = GameInputStatus.Value.KeyDown;
                                        break;
                                    case GameInputKey.Status.Click:
                                        value = GameInputStatus.Value.KeyDownAndUp;
                                        break;
                                }
                                break;
                        }
                        break;
                }
            }

            return value;
        }

        public void Do(
            GameInputButton button,
            int actorActionIndex,
            int group,
            int index,
            double oldTime, 
            in float3 direction)
        {
            //UnityEngine.Debug.LogError($"Do {actorActionIndex}");

            GameInputActionTarget actionTarget;
            actionTarget.status = GameInputStatus.As(button);

            var action = actions[index];
            bool result = __Did(
                ref action,
                button,
                actorActionIndex,
                group,
                index,
                oldTime, 
                direction,
                out _, 
                out int camp);

            __Apply(result, GameInputStatus.As(button), index, camp, direction, ref action);
        }

        public void Execute(
            GameInputButton button,
            int actorActionIndex,
            int group,
            int index,
            in float3 direction)
        {
            var status = states[index];
            Do(button, actorActionIndex, group, index, status.time, direction);

            var value = CollectKeys(status.value, keys);

            if (value != status.value || math.abs(time - status.time) > math.DBL_MIN_NORMAL)
            {
                status.value = value;
                status.time = time;
                states[index] = status;
            }
        }

        public void Execute(int index)
        {
            bool result, isTimeout;
            float3 direction = directions[index].value;
            var action = actions[index];
            var status = states[index];
            var value = CollectKeys(status.value, keys);
            var actionTargetStatus = value;
            int camp;
            switch (value)
            {
                case GameInputStatus.Value.KeyDown:
                case GameInputStatus.Value.KeyHold:
                case GameInputStatus.Value.KeyUp:
                    result = __Did(
                        ref action,
                        GameInputStatus.As(value),
                        -1,
                        0,
                        index,
                        status.time, 
                        direction,
                        out isTimeout, 
                        out camp);
                    if (value == GameInputStatus.Value.KeyUp)
                    {
                        if (result || isTimeout)
                            value = GameInputStatus.Value.Normal;
                    }
                    else if (result)
                        value = GameInputStatus.Value.KeyHold;
                    break;
                case GameInputStatus.Value.KeyUpAndDown:
                    result = __Did(
                        ref action,
                        GameInputButton.Up,
                        -1,
                        0,
                        index,
                        status.time, 
                        direction,
                        out _,
                        out camp);
                    if (result)
                        value = GameInputStatus.Value.KeyDown;
                    else //if (isTimeout || actorTime < time)
                    {
                        result = __Did(
                            ref action,
                            GameInputButton.Down,
                            -1,
                            0,
                            index,
                            status.time, 
                            direction,
                            out _,
                            out camp);
                        
                        value = result ? GameInputStatus.Value.KeyHold : GameInputStatus.Value.KeyDown;
                    }
                    break;
                case GameInputStatus.Value.KeyDownAndUp:
                    result = __Did(
                        ref action,
                        GameInputButton.Down,
                        -1,
                        0,
                        index,
                        status.time, 
                        direction,
                        out _,
                        out camp);
                    value = result ? GameInputStatus.Value.KeyHoldAndUp : GameInputStatus.Value.KeyUp;
                    break;
                case GameInputStatus.Value.KeyHoldAndUp:
                    result = __Did(
                        ref action,
                        GameInputButton.Hold,
                        -1,
                        0,
                        index,
                        status.time, 
                        direction,
                        out _,
                        out camp);
                    value = GameInputStatus.Value.KeyUp;
                    break;
                case GameInputStatus.Value.KeyUpAndDownAndUp:
                    result = __Did(
                        ref action,
                        GameInputButton.Up,
                        -1,
                        0,
                        index,
                        status.time, 
                        direction,
                        out _,
                        out camp);
                    value = GameInputStatus.Value.KeyDownAndUp;
                    break;
                default:
                    result = __Did(
                        ref action,
                        GameInputButton.Down,
                        -1,
                        0,
                        index,
                        status.time, 
                        direction,
                        out _,
                        out camp);
                    break;
            }

            __Apply(result, actionTargetStatus, index, camp, direction, ref action);

            if (value != status.value || math.abs(time - status.time) > math.DBL_MIN_NORMAL)
            {
                status.value = value;
                status.time = time;
                states[index] = status;
            }
        }

        private bool __Did(
            ref GameInputAction action,
            GameInputButton button,
            int actorActionIndex,
            int group,
            int index,
            double oldTime, 
            in float3 direction,
            out bool isTimeout, 
            out int camp/*,
            out double actorTimeValue*/)
        {
            var entity = GameNodeParent.GetRootMain(entityArray[index], parents);
            //var actorTime = actorTimes[entity];
            //actorTimeValue = actorTime.value;
            float3 forward = math.forward(rotations[entity].Value);
            camp = camps[entity].value;
            return action.Did(
                button,
                actorActionIndex,
                group,
                camp,
                (int)actorStates[entity].value,
                velocities[entity].value,
                math.dot(math.normalizesafe(direction, forward), forward),
                rages.HasComponent(entity) ? rages[entity].value : 0.0f,
                maxDistance,
                oldTime, 
                time,
                translations[entity].Value,
                selection,
                actorTimes[entity],
                targets,
                translations,
                colliders,
                nodeStates,
                camps,
                actorActionInfos[entity],
                actorActions[entity],
                actionInstances[entity],
                items[entity],
                definition,
                actionSetDefinition,
                actionItemSetDefinition,
                out isTimeout);
        }

        private void __Apply(
            bool result,
            GameInputStatus.Value status,
            int index,
            int camp, 
            in float3 direction,
            ref GameInputAction action)
        {
            if (result)
            {
                GameInputActionTarget actionTarget;
                actionTarget.status = status;
                actionTarget.actorActionIndex = action.actorActionIndex;
                actionTarget.entity = action.target == Entity.Null ? __GetActionTarget(index, camp, action) : action.target;
                actionTarget.direction = direction;

                if (actionTarget.isDo)
                    actions[index] = action;

                actionTargets[index] = actionTarget;
            }
            else// if (GameInputStatus.IsDo(status))
            {
                GameInputActionTarget actionTarget;
                actionTarget.status = status;
                actionTarget.actorActionIndex = -1;
                actionTarget.entity = __GetActionTarget(index, camp, action);
                actionTarget.direction = direction;

                actionTargets[index] = actionTarget;
            }
        }

        private Entity __GetActionTarget(int index, int camp, in GameInputAction action)
        {
            Entity entity = actionTargets[index].entity;
            return action.Predicate(camp, entity, nodeStates, camps, colliders) ? entity : Entity.Null;
            /*if (nodeStates.HasComponent(entity) && ((GameEntityStatus)nodeStates[entity].value & GameEntityStatus.Mask) != GameEntityStatus.Dead)
                return entity;

            return Entity.Null;*/
        }
    }

    [BurstCompile]
    public struct ApplyEx : IJobChunk
    {
        public float maxDistance;
        public double time;

        public Entity selection;

        public BlobAssetReference<GameInputActionDefinition> definition;
        public BlobAssetReference<GameActionSetDefinition> actionSetDefinition;
        public BlobAssetReference<GameActionItemSetDefinition> actionItemSetDefinition;

        [ReadOnly]
        public NativeArray<GameInputKey> keys;

        [ReadOnly]
        public SharedList<GameInputTarget>.Reader targets;

        [ReadOnly]
        public EntityTypeHandle entityType;

        [ReadOnly]
        public ComponentLookup<Translation> translations;

        [ReadOnly]
        public ComponentLookup<Rotation> rotations;

        [ReadOnly]
        public ComponentLookup<PhysicsShapeCompoundCollider> colliders;

        [ReadOnly]
        public ComponentLookup<GameNodeParent> parents;

        [ReadOnly]
        public ComponentLookup<GameNodeStatus> nodeStates;

        [ReadOnly]
        public ComponentLookup<GameNodeVelocity> velocities;

        [ReadOnly]
        public ComponentLookup<GameNodeActorStatus> actorStates;

        [ReadOnly]
        public ComponentLookup<GameEntityActorTime> actorTimes;

        [ReadOnly]
        public ComponentLookup<GameEntityRage> rages;

        [ReadOnly]
        public ComponentLookup<GameEntityCamp> camps;

        [ReadOnly]
        public BufferLookup<GameEntityItem> items;

        [ReadOnly]
        public BufferLookup<GameEntityActorActionData> actorActions;

        [ReadOnly]
        public BufferLookup<GameEntityActorActionInfo> actorActionInfos;

        [ReadOnly]
        public BufferLookup<GameInputActionInstance> actionInstances;

        [ReadOnly]
        public ComponentTypeHandle<GameNodeDirection> directionType;

        public ComponentTypeHandle<GameInputActionTarget> actionTargetType;

        public ComponentTypeHandle<GameInputAction> actionType;

        public ComponentTypeHandle<GameInputStatus> stateType;

        public ComponentTypeHandle<GameInputActionCommand> actionCommandType;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Apply apply;
            apply.maxDistance = maxDistance;
            apply.time = time;
            apply.selection = selection;
            apply.definition = definition;
            apply.actionSetDefinition = actionSetDefinition;
            apply.actionItemSetDefinition = actionItemSetDefinition;
            apply.targets = targets;
            apply.translations = translations;
            apply.rotations = rotations;
            apply.colliders = colliders;
            apply.parents = parents;
            apply.nodeStates = nodeStates;
            apply.velocities = velocities;
            apply.actorStates = actorStates;
            apply.actorTimes = actorTimes;
            apply.rages = rages;
            apply.camps = camps;
            apply.items = items;
            apply.actorActions = actorActions;
            apply.actorActionInfos = actorActionInfos;
            apply.actionInstances = actionInstances;
            apply.keys = keys;
            apply.entityArray = chunk.GetNativeArray(entityType);
            apply.directions = chunk.GetNativeArray(ref directionType);
            apply.actionTargets = chunk.GetNativeArray(ref actionTargetType);
            apply.actions = chunk.GetNativeArray(ref actionType);
            apply.states = chunk.GetNativeArray(ref stateType);

            var actionCommands = chunk.GetNativeArray(ref actionCommandType);
            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
            {
                if (actionCommands.Length > i && chunk.IsComponentEnabled(ref actionCommandType, i))
                {
                    var actionCommand = actionCommands[i];

                    apply.Execute(actionCommand.button, actionCommand.actorActionIndex, actionCommand.group, i, actionCommand.direction);

                    chunk.SetComponentEnabled(ref actionCommandType, i, false);
                }
                else
                    apply.Execute(i);
            }
        }
    }

    private EntityQuery __group;
    private GameSyncTime __time;

    private EntityTypeHandle __entityType;

    private ComponentLookup<Translation> __translations;

    private ComponentLookup<Rotation> __rotations;

    private ComponentLookup<PhysicsShapeCompoundCollider> __colliders;

    private ComponentLookup<GameNodeParent> __parents;

    private ComponentLookup<GameNodeStatus> __nodeStates;

    private ComponentLookup<GameNodeVelocity> __velocities;

    private ComponentLookup<GameNodeActorStatus> __actorStates;

    private ComponentLookup<GameEntityActorTime> __actorTimes;

    private ComponentLookup<GameEntityRage> __rages;

    private ComponentLookup<GameEntityCamp> __camps;

    private BufferLookup<GameEntityItem> __items;

    private BufferLookup<GameEntityActorActionData> __actorActions;

    private BufferLookup<GameEntityActorActionInfo> __actorActionInfos;

    private BufferLookup<GameInputActionInstance> __actionInstances;

    private ComponentTypeHandle<GameNodeDirection> __directionType;

    private ComponentTypeHandle<GameInputActionTarget> __actionTargetType;

    private ComponentTypeHandle<GameInputAction> __actionType;

    private ComponentTypeHandle<GameInputStatus> __stateType;

    private ComponentTypeHandle<GameInputActionCommand> __actionCommandType;

    private SharedList<GameInputTarget> __targets;
    private NativeList<GameInputKey> __keys;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __group = builder
                .WithAllRW<GameInputActionTarget, GameInputAction>()
                .WithAllRW<GameInputStatus>()
                .Build(ref state);

        __time = new GameSyncTime(ref state);

        __entityType = state.GetEntityTypeHandle();
        __translations = state.GetComponentLookup<Translation>(true);
        __rotations = state.GetComponentLookup<Rotation>(true);
        __colliders = state.GetComponentLookup<PhysicsShapeCompoundCollider>(true);
        __parents = state.GetComponentLookup<GameNodeParent>(true);
        __nodeStates = state.GetComponentLookup<GameNodeStatus>(true);
        __velocities = state.GetComponentLookup<GameNodeVelocity>(true);
        __actorStates = state.GetComponentLookup<GameNodeActorStatus>(true);
        __actorTimes = state.GetComponentLookup<GameEntityActorTime>(true);
        __rages = state.GetComponentLookup<GameEntityRage>(true);
        __camps = state.GetComponentLookup<GameEntityCamp>(true);
        __items = state.GetBufferLookup<GameEntityItem>(true);
        __actorActions = state.GetBufferLookup<GameEntityActorActionData>(true);
        __actorActionInfos = state.GetBufferLookup<GameEntityActorActionInfo>(true);
        __actionInstances = state.GetBufferLookup<GameInputActionInstance>(true);
        __directionType = state.GetComponentTypeHandle<GameNodeDirection>(true);
        __actionTargetType = state.GetComponentTypeHandle<GameInputActionTarget>();
        __actionType = state.GetComponentTypeHandle<GameInputAction>();
        __stateType = state.GetComponentTypeHandle<GameInputStatus>();
        __actionCommandType = state.GetComponentTypeHandle<GameInputActionCommand>();

        __targets = state.WorldUnmanaged.GetExistingSystemUnmanaged<GameInputSystem>().targets;

        __keys = new NativeList<GameInputKey>(Allocator.Persistent);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        __keys.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.HasSingleton<GameInputKey>() ||
            !SystemAPI.HasSingleton<GameInputActionData>() ||
            !SystemAPI.HasSingleton<GameActionSetData>() ||
            !SystemAPI.HasSingleton<GameActionItemSetData>())
            return;

        var keys = SystemAPI.GetSingletonBuffer<GameInputKey>();
        __keys.Clear();
        __keys.AddRange(keys.AsNativeArray());
        keys.Clear();

        var instance = SystemAPI.GetSingleton<GameInputActionData>();
        ApplyEx apply;
        apply.maxDistance = instance.maxDistance;
        apply.time = __time.nextTime;
        apply.selection = SystemAPI.HasSingleton<GameInputSelection>() ? SystemAPI.GetSingleton<GameInputSelection>().entity : Entity.Null;
        apply.definition = instance.definition;
        apply.actionSetDefinition = SystemAPI.GetSingleton<GameActionSetData>().definition;
        apply.actionItemSetDefinition = SystemAPI.GetSingleton<GameActionItemSetData>().definition;
        apply.targets = __targets.reader;
        apply.keys = __keys.AsArray();
        apply.entityType = __entityType.UpdateAsRef(ref state);
        apply.translations = __translations.UpdateAsRef(ref state);
        apply.rotations = __rotations.UpdateAsRef(ref state);
        apply.colliders = __colliders.UpdateAsRef(ref state);
        apply.parents = __parents.UpdateAsRef(ref state);
        apply.nodeStates = __nodeStates.UpdateAsRef(ref state);
        apply.velocities = __velocities.UpdateAsRef(ref state);
        apply.actorStates = __actorStates.UpdateAsRef(ref state);
        apply.actorTimes = __actorTimes.UpdateAsRef(ref state);
        apply.rages = __rages.UpdateAsRef(ref state);
        apply.camps = __camps.UpdateAsRef(ref state);
        apply.items = __items.UpdateAsRef(ref state);
        apply.actorActions = __actorActions.UpdateAsRef(ref state);
        apply.actorActionInfos = __actorActionInfos.UpdateAsRef(ref state);
        apply.actionInstances = __actionInstances.UpdateAsRef(ref state);
        apply.directionType = __directionType.UpdateAsRef(ref state);
        apply.actionTargetType = __actionTargetType.UpdateAsRef(ref state);
        apply.actionType = __actionType.UpdateAsRef(ref state);
        apply.stateType = __stateType.UpdateAsRef(ref state);
        apply.actionCommandType = __actionCommandType.UpdateAsRef(ref state);

        ref var targetsJobManager = ref __targets.lookupJobManager;
        var jobHandle = apply.ScheduleParallelByRef(__group, JobHandle.CombineDependencies(targetsJobManager.readOnlyJobHandle, state.Dependency));

        targetsJobManager.AddReadOnlyDependency(jobHandle);

        state.Dependency = jobHandle;
    }
}
