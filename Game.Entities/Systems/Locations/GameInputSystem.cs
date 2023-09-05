using System;
using Unity.Jobs;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Physics;
using ZG;
using ZG.Mathematics;
using Unity.Transforms;

public enum GameInputButton
{
    Down,
    Hold,
    Up
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

        public int group;

        public int actorStatusMask;

        public uint actorMask;

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
            float delta,
            double time,
            in GameEntityActorTime actorTime)
        {
            if (layerMask != 0 && this.layerMask != 0 && (layerMask & this.layerMask) == 0 ||
                targetType != 0 && this.targetType != 0 && (targetType & this.targetType) == 0)
                return false;

            if (this.button == button &&
                this.group == group &&
                actorTime.Did(actorMask, time) &&
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

    public bool Did(
        GameInputButton button,
        int group,
        int actorStatus,
        float actorVelocity,
        float dot,
        float delta,
        double time,
        in GameEntityActorTime actorTime,
        in DynamicBuffer<GameEntityActorActionInfo> actorActionInfos,
        in DynamicBuffer<GameEntityActorActionData> actorActions,
        ref int actorActionIndex,
        ref int layerMask,
        ref GameActionTargetType targetType,
        out float distance)
    {
        /*layerMask = 0;
        targetType = 0;*/
        distance = 0.0f;

        GameEntityActorActionData actorAction;
        int numActions = actions.Length;
        if (actorActionIndex != -1)
        {
            int preActionIndex = actorActions[actorActionIndex].actionIndex;
            for (int i = actorActionIndex + 1; i < numActions; ++i)
            {
                actorAction = actorActions[i];
                if (actorAction.activeCount > 0)
                {
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
                        delta,
                        time,
                        actorTime))
                    {
                        if (actorActionInfos[i].coolDownTime < time)
                        {
                            actorActionIndex = i;
                            layerMask = action.layerMask;
                            targetType = action.targetType;
                            distance = action.distance;

                            return true;
                        }

                        break;
                    }
                }
            }

            for (int i = 0; i <= actorActionIndex; ++i)
            {
                actorAction = actorActions[i];
                if (actorAction.activeCount > 0)
                {
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
                        delta,
                        time,
                        actorTime))
                    {
                        if (actorActionInfos[i].coolDownTime < time)
                        {
                            actorActionIndex = i;
                            layerMask = action.layerMask;
                            targetType = action.targetType;
                            distance = action.distance;

                            return true;
                        }

                        break;
                    }
                }
            }
        }

        for (int i = 0; i < numActions; ++i)
        {
            actorAction = actorActions[i];
            if (actorAction.activeCount > 0)
            {
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
                        delta,
                        time,
                        actorTime))
                {
                    actorActionIndex = i;
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
    public GameActionTargetType targetType;

    public int sourceActorActionIndex;
    public int destinationActorActionIndex;

    public int layerMask;

    public float distance;

    public double minActionTime;
    public double maxActionTime;

    public Entity target;

    public static bool Check(GameActionTargetType type, int sourceCamp, int destinationCamp)
    {
        if ((type & GameActionTargetType.Ally) != 0 && sourceCamp == destinationCamp)
            return true;

        if ((type & GameActionTargetType.Enemy) != 0 && sourceCamp != destinationCamp)
            return true;

        return false;
    }

    public bool Did(
        GameInputButton button,
        int actorActionIndex,
        int group,
        int camp,
        int actorStatus,
        float actorVelocity,
        float dot, 
        float maxDistance,
        //float responseTime,
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
        in DynamicBuffer<GameEntityItem> items,
        ref GameInputActionDefinition definition,
        ref GameActionSetDefinition actionSet, 
        ref GameActionItemSetDefinition actionItemSet, 
        out bool isTimeout)
    {
        if (!states.HasComponent(target) || 
            (((GameEntityStatus)states[target].value & GameEntityStatus.Mask) == GameEntityStatus.Dead) || 
            math.distancesq(translations[target].Value, position) > this.distance * this.distance)
        {
            layerMask = 0;
            targetType = 0;
            this.distance = 0.0f;
            target = Entity.Null;
        }

        isTimeout = time > maxActionTime || actorActionIndex != -1 && actorActionIndex != sourceActorActionIndex;

        if (isTimeout)
        {
            //Debug.LogError($"Do Timeout {index} : {origin.sourceActionIndex} : {time} : {origin.maxActionTime}");

            sourceActorActionIndex = actorActionIndex;
            destinationActorActionIndex = -1;
        }

        bool result = definition.Did(
            button,
            group,
            actorStatus, 
            actorVelocity,
            dot, //math.normalizesafe(direction, forward),
            (float)(time - minActionTime),
            time,
            actorTime, 
            actorActionInfos,
            actorActions, 
            ref destinationActorActionIndex,
            ref layerMask,
            ref targetType,
            out float distance);

        if (result)
        {
            if (target == Entity.Null && targetType != 0)
            {
                if (__Predicate(camp, selection, states, camps, colliders))
                    target = selection;

                this.distance = math.max(distance, maxDistance);

                if (target == Entity.Null)
                {
                    GameInputTarget target;
                    int numTargets = targets.length;
                    for(int i = 0; i < numTargets; ++i)
                    {
                        target = targets[i];
                        if (__Predicate(camp, target.entity, states, camps, colliders) &&
                            target.distance < distance)
                        {
                            this.target = target.entity;

                            break;
                        }
                    }
                }
            }

            int actionIndex = actorActions[destinationActorActionIndex].actionIndex;
            ref var action = ref actionSet.values[actionIndex];
            float performTime = action.info.performTime, artTime = action.info.artTime;
            if (items.IsCreated)
            {
                int numItems = items.Length, length = actionItemSet.values.Length;
                GameEntityItem item;
                for (int i = 0; i < numItems; ++i)
                {
                    item = items[i];

                    if (item.index >= 0 && item.index < length)
                    {
                        ref var actionItem = ref actionItemSet.values[item.index];

                        performTime += actionItem.performTime;
                        artTime += actionItem.artTime;
                    }
                }
            }

            minActionTime = time + performTime;
            maxActionTime = time + artTime;// (artTime + responseTime);

            return true;
        }

        return false;
    }

    private bool __Predicate(
        int camp, 
        in Entity entity, 
        in ComponentLookup<GameNodeStatus> states, 
        in ComponentLookup<GameEntityCamp> camps, 
        in ComponentLookup<PhysicsShapeCompoundCollider> colliders)
    {
        return states.HasComponent(entity) &&
           (((GameEntityStatus)states[entity].value & GameEntityStatus.Mask) == GameEntityStatus.Dead) &&
           (layerMask == 0 || (colliders[entity].value.Value.Filter.BelongsTo & layerMask) != 0) &&
           Check(targetType == 0 ? GameActionTargetType.Enemy : targetType, camp, camps[entity].value);
    }
}

public struct GameInputActionEvent : IComponentData
{
    public int actorActionIndex;
    public Entity target;
}

public struct GameInputActionCommand : IComponentData, IEnableableComponent
{
    public GameInputButton button;
    public int actorActionIndex;
    public int group;
}

public struct GameInputSelection : IComponentData
{
    public Entity entity;
}

public struct GameInputStatus : IComponentData
{
    public enum Value
    {
        Normal,
        Hand,
        DoDown,
        DoHold,
        DoUp,
        DoUpAndDown,
        DoDownAndUp,
        DoHoldAndUp,
        DoUpAndDownAndUp
    }

    public Value value;
}

public struct GameInputRaycast : IComponentData
{
    public int raycasterMask;
    public int obstacleMask;

    public float3 raycastCenter;
}

/*[AutoCreateIn("Client"), CreateAfter(typeof(GamePhysicsWorldBuildSystem)), UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
public partial struct GameInputSystem : ISystem
{
    [BurstCompile]
    private struct OverlapHits : IJob
    {
        public uint raycasterMask;

        public Aabb aabb;
        [ReadOnly]
        public CollisionWorldContainer collisionWorld;

        public NativeList<int> hits;

        public void Execute()
        {
            hits.Clear();

            CollisionFilter filter = default;
            filter.GroupIndex = 0;
            filter.CollidesWith = ~0u;
            filter.BelongsTo = raycasterMask;

            OverlapAabbInput overlapAabbInput;
            overlapAabbInput.Aabb = aabb;
            overlapAabbInput.Filter = filter;

            ((CollisionWorld)collisionWorld).OverlapAabb(overlapAabbInput, ref hits);
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
        public NativeArray<Translation> translations;
        public SharedHashMap<Entity, float>.ParallelWriter targetDistances;

        public void Execute(int index)
        {
            CollisionFilter filter = default;
            filter.GroupIndex = 0;
            filter.CollidesWith = ~0u;
            filter.BelongsTo = raycasterMask;

            RaycastInput raycastInput = default;
            raycastInput.Filter = filter;
            raycastInput.Filter.BelongsTo = ~0u;
            raycastInput.Filter.CollidesWith = obstacleMask;

            float3 position = translations[index].Value;
            PointDistanceInput pointDistanceInput = default;
            pointDistanceInput.MaxDistance = float.MaxValue;
            pointDistanceInput.Position = position;
            pointDistanceInput.Filter = filter;

            int count = hits.Length;
            Aabb aabb;
            Box box;
            RigidBody rigidbody;
            DistanceHit distanceHit;
            var rigidbodies = collisionWorld.Bodies;

            for (int i = 0; i < count; ++i)
            {
                rigidbody = rigidbodies[hits[i]];

                aabb = rigidbody.Collider.Value.CalculateAabb();
                box = new Box(aabb.Center + rigidbody.WorldFromBody.pos, aabb.Extents, rigidbody.WorldFromBody.rot);
                if (frustumPlanes.Intersect(box.center, box.worldExtents) == FrustumPlanes.IntersectResult.Out)
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

    private struct SortTargets
    {

    }


    [BurstCompile]
    private struct Hand : IJob
    {
        public Entity localPlayer;

        public BlobAssetReference<GameActionSetDefinition> actions;

        [ReadOnly]
        public NativeArray<Target> targets;

        [ReadOnly]
        public ComponentLookup<PhysicsCollider> colliders;

        [ReadOnly]
        public ComponentLookup<GameSelectable> selectables;

        [ReadOnly]
        public ComponentLookup<GameCreature> creatures;

        [ReadOnly]
        public ComponentLookup<GamePickable> pickables;

        [ReadOnly]
        public ComponentLookup<GameFactory> factories;

        [ReadOnly]
        public ComponentLookup<GameNodeStatus> states;

        [ReadOnly]
        public ComponentLookup<GameEntityCamp> camps;

        [ReadOnly]
        public BufferLookup<GameEntityActorActionData> actorActions;

        public NativeReference<Entity> selection;

        public void Execute()
        {
            ref var actions = ref this.actions.Value;
            var actorActions = this.actorActions[localPlayer];
            GameEntityActorActionData actorAction;
            Target target;
            int numTargets = targets.Length, numActorActions = actorActions.Length, localPlayerCamp = camps[localPlayer].value, status, i, j;
            uint belongsTo;
            for (i = 0; i < numTargets; ++i)
            {
                target = targets[i];
                if (target.entity == localPlayer)
                    continue;

                if (states.HasComponent(target.entity))
                {
                    status = states[target.entity].value;
                    if ((int)GameEntityStatus.Dead == status)
                        continue;
                    else if ((int)GameEntityStatus.KnockedOut == status)
                        break;
                }

                if (pickables.HasComponent(target.entity))
                    break;

                if (camps.HasComponent(target.entity) && camps[target.entity].value == localPlayerCamp)
                {
                    if (selectables.HasComponent(target.entity))
                        break;
                }
                else if (factories.HasComponent(target.entity))
                {
                    if (factories[target.entity].status == GameFactoryStatus.Complete)
                        break;
                }
                else if (!creatures.HasComponent(target.entity) && colliders.HasComponent(target.entity))
                {
                    belongsTo = colliders[target.entity].Value.Value.Filter.BelongsTo;
                    for (j = 0; j < numActorActions; ++j)
                    {
                        actorAction = actorActions[j];
                        if (actorAction.activeCount > 0)
                        {
                            ref var action = ref actions.values[actorAction.actionIndex];
                            if ((action.instance.damageMask & belongsTo) != 0)
                                break;
                        }
                    }

                    if (j < numActorActions)
                        break;
                }
            }

            selection.Value = i < numTargets ? targets[i].entity : Entity.Null;
        }
    }

    [BurstCompile]
    private struct QuestGuide : IJob
    {
        public Entity localPlayer;

        public BlobAssetReference<GameActionSetDefinition> actions;

        public GameQuestGuideManager.ReadOnly manager;

        [ReadOnly]
        public NativeArray<Target> targets;

        [ReadOnly]
        public ComponentLookup<NetworkIdentity> identities;

        [ReadOnly]
        public ComponentLookup<PhysicsCollider> colliders;

        [ReadOnly]
        public ComponentLookup<GamePickable> pickables;

        [ReadOnly]
        public ComponentLookup<GameFactory> factories;

        [ReadOnly]
        public ComponentLookup<GameNodeStatus> states;

        [ReadOnly]
        public ComponentLookup<GameEntityCamp> camps;

        [ReadOnly]
        public BufferLookup<GameEntityActorActionData> actorActions;

        public NativeReference<Entity> selection;

        public void Execute()
        {
            ref var actions = ref this.actions.Value;
            var actorActions = this.actorActions[localPlayer];
            GameEntityActorActionData actorAction;
            Target target;
            int numTargets = targets.Length, numActorActions = actorActions.Length, localPlayerCamp = camps[localPlayer].value, i, j;
            uint belongsTo;
            for (i = 0; i < numTargets; ++i)
            {
                target = targets[i];
                if (target.entity == localPlayer)
                    continue;

                if (states.HasComponent(target.entity) && states[target.entity].value == (int)GameEntityStatus.Dead)
                    continue;

                if (!identities.HasComponent(target.entity) || !manager.IsPublished(GameQuestGuideVariantType.Entity, identities[target.entity].type))
                    continue;

                if (pickables.HasComponent(target.entity))
                    break;

                if (factories.HasComponent(target.entity) && factories[target.entity].status == GameFactoryStatus.Complete)
                    break;

                if (camps.HasComponent(target.entity) && camps[target.entity].value == localPlayerCamp)
                    break;

                if (colliders.HasComponent(target.entity))
                {
                    belongsTo = colliders[target.entity].Value.Value.Filter.BelongsTo;
                    for (j = 0; j < numActorActions; ++j)
                    {
                        actorAction = actorActions[j];
                        if (actorAction.activeCount > 0)
                        {
                            ref var action = ref actions.values[actorAction.actionIndex];
                            if ((action.instance.damageMask & belongsTo) != 0)
                                break;
                        }
                    }

                    if (j < numActorActions)
                        break;
                }
            }

            selection.Value = i < numTargets ? targets[i].entity : Entity.Null;
        }
    }

    private SharedPhysicsWorld __physicsWorld;

    public SharedList<GameInputTarget> targets
    {
        get;

        private set;
    }

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __physicsWorld = state.WorldUnmanaged.GetExistingSystemUnmanaged<GamePhysicsWorldBuildSystem>().physicsWorld;

        targets = new SharedList<GameInputTarget>(Allocator.Persistent);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        targets.Dispose();
    }

    protected override void OnUpdate()
    {
        if (!SystemAPI.HasSingleton<MainCameraFrustum>())
            return;

        FindTargets findTargets;
        findTargets.raycasterMask = (uint)raycasterMask;
        findTargets.obstacleMask = (uint)obstacleMask;
        findTargets.position = (float3)vehicle.transform.position + raycastCenter;
        findTargets.aabb = aabb;
        findTargets.collisionWorld = __physicsWorld.collisionWorld;
        findTargets.planes = __planes;
        findTargets.outputs = __targets;

        ref var physicsWorldJobManager = ref __physicsWorld.lookupJobManager;

        __targetJobHandle = findTargets.ScheduleByRef(JobHandle.CombineDependencies(physicsWorldJobManager.readOnlyJobHandle, Dependency));

        physicsWorldJobManager.AddReadOnlyDependency(__targetJobHandle);

        Entity localPlayerEntity = localPlayer.instance.instance.entity;
        var actions = SystemAPI.GetSingleton<GameActionSetData>().definition;
        var targets = __targets.AsDeferredJobArray();
        var colliders = GetComponentLookup<PhysicsCollider>(true);
        var pickables = GetComponentLookup<GamePickable>(true);
        var factories = GetComponentLookup<GameFactory>(true);
        var states = GetComponentLookup<GameNodeStatus>(true);
        var camps = GetComponentLookup<GameEntityCamp>(true);
        var actorActions = GetBufferLookup<GameEntityActorActionData>(true);

        Hand hand;
        hand.localPlayer = localPlayerEntity;
        hand.targets = targets;
        hand.actions = actions;
        hand.colliders = colliders;
        hand.selectables = GetComponentLookup<GameSelectable>(true);
        hand.creatures = GetComponentLookup<GameCreature>(true);
        hand.pickables = pickables;
        hand.factories = factories;
        hand.states = states;
        hand.camps = camps;
        hand.actorActions = actorActions;
        hand.selection = __hand;

        __handJobHandle = hand.ScheduleByRef(__targetJobHandle);

        var questGuideManager = SystemAPI.GetSingleton<GameQuestGuideManagerShared>();

        QuestGuide questGuide;
        questGuide.localPlayer = localPlayerEntity;
        questGuide.actions = actions;
        questGuide.manager = questGuideManager.value.readOnly;
        questGuide.targets = targets;
        questGuide.identities = GetComponentLookup<NetworkIdentity>(true);
        questGuide.colliders = colliders;
        questGuide.pickables = pickables;
        questGuide.factories = factories;
        questGuide.states = states;
        questGuide.camps = camps;
        questGuide.actorActions = actorActions;
        questGuide.selection = __questGuide;

        ref var questGuideJobManager = ref questGuideManager.lookupJobManager;

        __questGuideJobHandle = questGuide.ScheduleByRef(JobHandle.CombineDependencies(questGuideJobManager.readOnlyJobHandle, __targetJobHandle));

        questGuideJobManager.AddReadOnlyDependency(__questGuideJobHandle);

        Dependency = JobHandle.CombineDependencies(__handJobHandle, __questGuideJobHandle);
    }
}*/

//[UpdateInGroup(typeof(PresentationSystemGroup), OrderFirst = true)]
public partial struct GameInputActionSystem : ISystem
{
    public struct Apply
    {
        public float maxDistance;
        public double time;

        public Entity selection;

        public BlobAssetReference<GameInputActionDefinition> definition;
        public BlobAssetReference<GameActionSetDefinition> actionSet;
        public BlobAssetReference<GameActionItemSetDefinition> actionItemSet;

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
        public ComponentLookup<GameEntityCamp> camps;

        [ReadOnly]
        public BufferLookup<GameEntityItem> items;

        [ReadOnly]
        public BufferLookup<GameEntityActorActionInfo> actorActionInfos;

        [ReadOnly]
        public BufferLookup<GameEntityActorActionData> actorActions;

        [ReadOnly]
        public NativeArray<Entity> entityArray;

        [ReadOnly]
        public NativeArray<GameNodeDirection> directions;

        public NativeArray<GameInputActionEvent> actionEvents;

        public NativeArray<GameInputAction> actions;

        public NativeArray<GameInputStatus> states;

        public void Do(
            GameInputButton button,
            int actorActionIndex,
            int group,
            int index)
        {
            GameInputActionEvent actionEvent;
            var action = actions[index];
            if(__Did(
                ref action,
                button,
                actorActionIndex,
                group,
                index,
                out _, 
                out _))
            {
                actionEvent.actorActionIndex = action.destinationActorActionIndex;
                actionEvent.target = action.target;
            }
            else
            {
                actionEvent.actorActionIndex = -1;
                actionEvent.target = Entity.Null;
            }

            actionEvents[index] = actionEvent;

            actions[index] = action;
        }

        public void Execute(int index)
        {
            bool doResult, isTimeout;
            var action = actions[index];
            var status = states[index];
            var value = status.value;
            switch (status.value)
            {
                case GameInputStatus.Value.DoDown:
                case GameInputStatus.Value.DoHold:
                case GameInputStatus.Value.DoUp:
                    doResult = __Did(
                        ref action, 
                        (GameInputButton)(status.value - GameInputStatus.Value.DoDown),
                        -1,
                        0,
                        index,
                        out isTimeout, 
                        out _);
                    if (status.value == GameInputStatus.Value.DoUp)
                    {
                        if (doResult || isTimeout)
                            value = GameInputStatus.Value.Normal;
                    }
                    else if (doResult)
                        value = GameInputStatus.Value.DoHold;
                    break;
                case GameInputStatus.Value.DoUpAndDown:
                    doResult = __Did(
                        ref action,
                        GameInputButton.Up,
                        -1,
                        0, 
                        index,
                        out isTimeout,
                        out double actorTime);
                    if (doResult)
                        value = GameInputStatus.Value.DoDown;
                    else if (isTimeout || actorTime < time)
                        value = __Did(
                            ref action,
                            GameInputButton.Down,
                            -1,
                            0,
                            index,
                            out _,
                            out _) ? GameInputStatus.Value.DoHold : GameInputStatus.Value.DoDown;
                    break;
                case GameInputStatus.Value.DoDownAndUp:
                    doResult = __Did(
                        ref action,
                        GameInputButton.Down,
                        -1,
                        0,
                        index,
                        out _,
                        out _);
                    value = doResult ? GameInputStatus.Value.DoHoldAndUp : GameInputStatus.Value.DoUp;
                    break;
                case GameInputStatus.Value.DoHoldAndUp:
                    doResult = __Did(
                        ref action,
                        GameInputButton.Hold,
                        -1,
                        0,
                        index,
                        out _,
                        out _);
                    value = GameInputStatus.Value.DoUp;
                    break;
                case GameInputStatus.Value.DoUpAndDownAndUp:
                    doResult = __Did(
                        ref action,
                        GameInputButton.Up,
                        -1,
                        0,
                        index,
                        out _,
                        out _);
                    value = GameInputStatus.Value.DoDownAndUp;
                    break;
                default:
                    doResult = __Did(
                        ref action,
                        GameInputButton.Down,
                        -1,
                        0,
                        index, 
                        out _, 
                        out _);
                    break;
            }

            GameInputActionEvent actionEvent;
            if(doResult)
            {
                actionEvent.actorActionIndex = action.destinationActorActionIndex;
                actionEvent.target = action.target;
            }
            else
            {
                actionEvent.actorActionIndex = -1;
                actionEvent.target = Entity.Null;
            }

            actionEvents[index] = actionEvent;

            actions[index] = action;

            if(value != status.value)
            {
                status.value = value;

                states[index] = status;
            }
        }

        public bool __Did(
            ref GameInputAction action,
            GameInputButton button,
            int actorActionIndex,
            int group,
            int index,
            out bool isTimeout, 
            out double actorTimeValue)
        {
            var entity = GameNodeParent.GetRootMain(entityArray[index], parents);
            var actorTime = actorTimes[entity];
            actorTimeValue = actorTime.value;
            float3 forward = math.forward(rotations[entity].Value);
            return action.Did(
                button,
                actorActionIndex,
                group,
                camps[entity].value,
                (int)actorStates[entity].value,
                velocities[entity].value,
                math.dot(math.normalizesafe(directions[index].value, forward), forward),
                maxDistance,
                time,
                translations[entity].Value,
                selection,
                actorTime,
                targets,
                translations,
                colliders,
                nodeStates,
                camps,
                actorActionInfos[entity],
                actorActions[entity],
                items[entity],
                ref definition.Value,
                ref actionSet.Value,
                ref actionItemSet.Value,
                out isTimeout);
        }
    }

    [BurstCompile]
    public struct ApplyEx : IJobChunk
    {
        public float maxDistance;
        public double time;

        public Entity selection;

        public BlobAssetReference<GameInputActionDefinition> definition;
        public BlobAssetReference<GameActionSetDefinition> actionSet;
        public BlobAssetReference<GameActionItemSetDefinition> actionItemSet;

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
        public ComponentLookup<GameEntityCamp> camps;

        [ReadOnly]
        public BufferLookup<GameEntityItem> items;

        [ReadOnly]
        public BufferLookup<GameEntityActorActionInfo> actorActionInfos;

        [ReadOnly]
        public BufferLookup<GameEntityActorActionData> actorActions;

        [ReadOnly]
        public ComponentTypeHandle<GameNodeDirection> directionType;

        public ComponentTypeHandle<GameInputActionEvent> actionEventType;

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
            apply.actionSet = actionSet;
            apply.actionItemSet = actionItemSet;
            apply.targets = targets;
            apply.entityArray = chunk.GetNativeArray(entityType);
            apply.translations = translations;
            apply.rotations = rotations;
            apply.colliders = colliders;
            apply.parents = parents;
            apply.nodeStates = nodeStates;
            apply.velocities = velocities;
            apply.actorStates = actorStates;
            apply.actorTimes = actorTimes;
            apply.camps = camps;
            apply.items = items;
            apply.actorActionInfos = actorActionInfos;
            apply.actorActions = actorActions;
            apply.directions = chunk.GetNativeArray(ref directionType);
            apply.actionEvents = chunk.GetNativeArray(ref actionEventType);
            apply.actions = chunk.GetNativeArray(ref actionType);
            apply.states = chunk.GetNativeArray(ref stateType);

            var actionCommands = chunk.GetNativeArray(ref actionCommandType);
            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while(iterator.NextEntityIndex(out int i))
            {
                if (actionCommands.Length > i && chunk.IsComponentEnabled(ref actionCommandType, i))
                {
                    var actionCommand = actionCommands[i];

                    apply.Do(actionCommand.button, actionCommand.actorActionIndex, actionCommand.group, i);

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

    private ComponentLookup<GameEntityCamp> __camps;

    private BufferLookup<GameEntityItem> __items;

    private BufferLookup<GameEntityActorActionInfo> __actorActionInfos;

    private BufferLookup<GameEntityActorActionData> __actorActions;

    private ComponentTypeHandle<GameNodeDirection> __directionType;

    private ComponentTypeHandle<GameInputActionEvent> __actionEventType;

    private ComponentTypeHandle<GameInputAction> __actionType;

    private ComponentTypeHandle<GameInputStatus> __stateType;

    private ComponentTypeHandle<GameInputActionCommand> __actionCommandType;

    private SharedList<GameInputTarget> __targets;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __group = builder
                .WithAll<GameNodeDirection>()
                .WithAllRW<GameInputActionEvent, GameInputAction>()
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
        __camps = state.GetComponentLookup<GameEntityCamp>(true);
        __items = state.GetBufferLookup<GameEntityItem>(true);
        __actorActionInfos = state.GetBufferLookup<GameEntityActorActionInfo>(true);
        __actorActions = state.GetBufferLookup<GameEntityActorActionData>(true);
        __directionType = state.GetComponentTypeHandle<GameNodeDirection>(true);
        __actionEventType = state.GetComponentTypeHandle<GameInputActionEvent>();
        __actionType = state.GetComponentTypeHandle<GameInputAction>();
        __stateType = state.GetComponentTypeHandle<GameInputStatus>();
        __actionCommandType = state.GetComponentTypeHandle<GameInputActionCommand>();
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {

    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.HasSingleton<GameInputActionData>() ||
            !SystemAPI.HasSingleton<GameActionSetData>() ||
            !SystemAPI.HasSingleton<GameActionItemSetData>())
            return;

        var instance = SystemAPI.GetSingleton<GameInputActionData>();
        ApplyEx apply;
        apply.maxDistance = instance.maxDistance;
        apply.time = __time.nextTime;
        apply.selection = SystemAPI.HasSingleton<GameInputSelection>() ? SystemAPI.GetSingleton<GameInputSelection>().entity : Entity.Null;
        apply.definition = instance.definition;
        apply.actionSet = SystemAPI.GetSingleton<GameActionSetData>().definition;
        apply.actionItemSet = SystemAPI.GetSingleton<GameActionItemSetData>().definition;
        apply.targets = __targets.reader;
        apply.entityType = __entityType.UpdateAsRef(ref state);
        apply.translations = __translations.UpdateAsRef(ref state);
        apply.rotations = __rotations.UpdateAsRef(ref state);
        apply.colliders = __colliders.UpdateAsRef(ref state);
        apply.parents = __parents.UpdateAsRef(ref state);
        apply.nodeStates = __nodeStates.UpdateAsRef(ref state);
        apply.velocities = __velocities.UpdateAsRef(ref state);
        apply.actorStates = __actorStates.UpdateAsRef(ref state);
        apply.actorTimes = __actorTimes.UpdateAsRef(ref state);
        apply.camps = __camps.UpdateAsRef(ref state);
        apply.items = __items.UpdateAsRef(ref state);
        apply.actorActionInfos = __actorActionInfos.UpdateAsRef(ref state);
        apply.actorActions = __actorActions.UpdateAsRef(ref state);
        apply.directionType = __directionType.UpdateAsRef(ref state);
        apply.actionEventType = __actionEventType.UpdateAsRef(ref state);
        apply.actionType = __actionType.UpdateAsRef(ref state);
        apply.stateType = __stateType.UpdateAsRef(ref state);
        apply.actionCommandType = __actionCommandType.UpdateAsRef(ref state);

        ref var targetsJobManager = ref __targets.lookupJobManager;
        var jobHandle = apply.ScheduleParallelByRef(__group, JobHandle.CombineDependencies(targetsJobManager.readOnlyJobHandle, state.Dependency));

        targetsJobManager.AddReadOnlyDependency(jobHandle);

        state.Dependency = jobHandle;
    }
}
