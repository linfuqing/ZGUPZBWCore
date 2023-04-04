using Unity.Jobs;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Entities;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Physics;
using Unity.Physics.Systems;
using ZG;

[BurstCompile, 
    UpdateInGroup(typeof(GameNodeCharacterSystemGroup))//, 
    //UpdateBefore(typeof(EndFramePhysicsSystem)), 
    /*UpdateAfter(typeof(GameNodeCharacterSystem))*/]
public partial struct GameNodeActorSystem : ISystem
{
    private struct UpdateStates
    {
        public GameTime time;

        [ReadOnly]
        public NativeArray<Entity> entityArray;

        //[ReadOnly]
        //public NativeArray<Rotation> rotations;

        [ReadOnly]
        public NativeArray<PhysicsVelocity> physicsVelocities;

        [ReadOnly]
        public NativeArray<GameNodeStatus> nodeStates;

        [ReadOnly]
        public NativeArray<GameNodeActorData> instances;

        [ReadOnly]
        public NativeArray<GameNodeCharacterStatus> characterStates;

        [ReadOnly]
        public NativeArray<GameNodeCharacterAngle> characterAangles;

        [ReadOnly]
        public NativeArray<GameNodeCharacterVelocity> characterVelocities;
        
        public BufferAccessor<GameNodeVelocityComponent> velocityComponents;

        public NativeArray<GameNodeVelocity> velocities;

        public NativeArray<GameNodeDelay> delay;

        public NativeArray<GameNodeActorStatus> actorStates;

        [NativeDisableContainerSafetyRestriction]
        public ComponentLookup<GameNodeStatus> states;

/*#if GAME_DEBUG_COMPARSION
        //public NativeQueue<LogInfo>.ParallelWriter logInfos;

        public uint frameIndex;

        public FixedString32Bytes delayName;
        public FixedString32Bytes statusName;
        public FixedString32Bytes characterStatusName;
        public FixedString32Bytes characterAreaName;
        public FixedString32Bytes oldActorStatusName;
        public FixedString32Bytes newActorStatusName;

        public ComparisonStream<int> stream;
        [ReadOnly]
        public NativeArray<GameEntityIndex> entityIndices;
#endif*/
        public unsafe void Execute(int index)
        {
            GameNodeStatus nodeStatus = nodeStates[index];
            if ((nodeStatus.value & (GameNodeStatus.STOP | GameNodeStatus.OVER)) != 0)
                return;

            GameNodeCharacterStatus characterStatus = characterStates[index];
            GameNodeDelay delay = this.delay[index];
            GameNodeActorData instance = instances[index];
            //instance.fallDelayTime = 5.05f;
            int delayStatus = GameNodeStatus.DELAY | GameNodeActorStatus.NODE_STATUS_ACT;
            if ((nodeStatus.value & delayStatus) == delayStatus)
            {
                if (delay.Check(time)/* + instance.fallMoveTime*/)
                    return;

                //UnityEngine.Debug.Log($"ACT {entityArray[index].Index} : {nodeStatus.value} : {(double)time}");

                nodeStatus.value = GameNodeActorStatus.NODE_STATUS_ACT;
                states[entityArray[index]] = nodeStatus;
            }

            float3 inveseGravityDirection = math.up();
            GameDeadline valueTime = time, temp;
            GameNodeActorStatus actorStatus = actorStates[index];
            GameNodeActorStatus.Status value;
            switch (characterStatus.area)
            {
                case GameNodeCharacterStatus.Area.Water:
                    value = GameNodeActorStatus.Status.Swim;

                    if (actorStatus.value != GameNodeActorStatus.Status.Swim)
                    {
                        float3 characterVelocity = index < characterVelocities.Length ? characterVelocities[index].value : physicsVelocities[index].Linear;

                        GameNodeVelocity velocity;
                        velocity.value = math.dot(characterVelocity, math.forward(quaternion.RotateY(characterAangles[index].value)));
                        velocities[index] = velocity;
                    }
                    break;
                case GameNodeCharacterStatus.Area.Air:
                    if(characterStatus.value == GameNodeCharacterStatus.Status.Contacting)
                    {
                        if (actorStatus.value == GameNodeActorStatus.Status.Fall)
                            value = GameNodeActorStatus.Status.Fall;
                        else
                        {
                            if (delay.Check(valueTime))
                                value = GameNodeActorStatus.Status.Jump;
                            else
                            {
                                valueTime = delay.time + delay.endTime;

                                value = GameNodeActorStatus.Status.Normal;
                            }
                        }
                    }
                    else
                        value = GameNodeActorStatus.Status.Jump;

                    /*switch (characterStatus.value)
                    {
                        case GameNodeCharacterStatus.Status.Unsupported:
                        case GameNodeCharacterStatus.Status.Supported:
                            if (actorStatus.value == GameNodeActorStatus.Status.Fall)
                                value = GameNodeActorStatus.Status.Fall;
                            else
                            {
                                if (delay.time > time)
                                    value = GameNodeActorStatus.Status.Jump;
                                else
                                {
                                    valueTime = delay.time;

                                    value = GameNodeActorStatus.Status.Fall;
                                }
                            }
                            break;
                        case GameNodeCharacterStatus.Status.Contacting:
                            if (actorStatus.value == GameNodeActorStatus.Status.Fall)
                                value = GameNodeActorStatus.Status.Fall;
                            else
                            {
                                if (delay.time > time)
                                    value = GameNodeActorStatus.Status.Jump;
                                else
                                {
                                    valueTime = delay.time;

                                    value = GameNodeActorStatus.Status.Normal;
                                }
                            }
                            break;
                        case GameNodeCharacterStatus.Status.Sliding:
                        case GameNodeCharacterStatus.Status.Firming:
                            value = GameNodeActorStatus.Status.Jump;// delay.time > time ? GameNodeActorStatus.Status.Jump : GameNodeActorStatus.Status.Normal;
                            break;
                        default:
                            if (actorStatus.value == GameNodeActorStatus.Status.Jump)
                            {
                                temp = delay.time;
                                temp -= instance.jumpTime + instance.fallTime;
                                if (temp > time)
                                    value = GameNodeActorStatus.Status.Jump;
                                else
                                {
                                    valueTime = temp;

                                    value = GameNodeActorStatus.Status.Fall;
                                }
                            }
                            else
                                value = actorStatus.value;
                            break;
                    }*/
                    break;
                case GameNodeCharacterStatus.Area.Fix:
                    value = GameNodeActorStatus.Status.Normal;
                    switch (actorStatus.value)
                    {
                        case GameNodeActorStatus.Status.Normal:
                        case GameNodeActorStatus.Status.Jump:
                            if (actorStatus.value == GameNodeActorStatus.Status.Jump ? 
                                (nodeStatus.value & (GameNodeStatus.MASK | GameNodeActorStatus.NODE_STATUS_ACT)) == GameNodeActorStatus.NODE_STATUS_ACT :
                                (nodeStatus.value & GameNodeStatus.MASK) == 0 && !delay.Check(valueTime))
                            {
                                if (instance.stepToFallDelayTime > math.FLT_MIN_NORMAL)
                                {
                                    //instance.stepToFallDelayTime += 2.0F;

                                    delay.time = valueTime;
                                    delay.startTime = half.zero;
                                    delay.endTime = (half)instance.stepToFallDelayTime;
                                    this.delay[index] = delay;

                                    nodeStatus.value = delayStatus;

                                    states[entityArray[index]] = nodeStatus;

                                    if (instance.stepToFallSpeed > math.FLT_MIN_NORMAL && index < velocityComponents.Length)
                                    {
                                        GameNodeVelocityComponent velocityComponent;
                                        velocityComponent.mode = GameNodeVelocityComponent.Mode.Indirect;
                                        velocityComponent.time = valueTime;
                                        velocityComponent.time += instance.stepToFallDelayTime;
                                        velocityComponent.duration = instance.fallMoveTime;
                                        velocityComponent.value = math.forward(quaternion.RotateY(characterAangles[index].value)) * instance.stepToFallSpeed;

                                        velocityComponents[index].Add(velocityComponent);
                                    }
                                }

                                value = GameNodeActorStatus.Status.Fall;
                            }
                            break;
                        case GameNodeActorStatus.Status.Fall:
                            value = GameNodeActorStatus.Status.Fall;
                            /*switch(characterStatus.value)
                            {
                                case GameNodeCharacterStatus.Status.Contacting:
                                case GameNodeCharacterStatus.Status.Firming:
                                    break;
                                default:
                                    value = GameNodeActorStatus.Status.Fall;
                                    break;
                            }*/
                            break;
                    }

                    //这有啥用?
                    /*if(value == GameNodeActorStatus.Status.Normal && 
                        actorStatus.value != GameNodeActorStatus.Status.Normal && 
                        delay.Check(valueTime))
                    {
                        delay.Clear(valueTime);
                        this.delay[index] = delay;
                    }*/
                    break;
                case GameNodeCharacterStatus.Area.Climb:
                    value = GameNodeActorStatus.Status.Climb;
                    break;
                default:
                    value = GameNodeActorStatus.Status.Normal;

                    switch (actorStatus.value)
                    {
                        case GameNodeActorStatus.Status.Fall:
                            switch (characterStatus.value)
                            {
                                case GameNodeCharacterStatus.Status.Sliding:
                                /*if (valueTime > delay.time && delay.time.count > time.count - 2)
                                    valueTime = delay.time;

                                break;*/
                                case GameNodeCharacterStatus.Status.Contacting:
                                case GameNodeCharacterStatus.Status.Firming:
                                    var endTime = delay.time + delay.endTime;
                                    if (valueTime > endTime && endTime.count > time.count - 2)
                                        valueTime = endTime;

                                    if ((nodeStatus.value & GameNodeActorStatus.NODE_STATUS_ACT) == GameNodeActorStatus.NODE_STATUS_ACT)
                                    { 
                                        //if (valueTime - actorStatus.time > instance.fallDelayTime)
                                        {
                                            if (instance.fallToStepDelayTime > math.FLT_MIN_NORMAL || delay.Check(valueTime))
                                            {
                                                delay.time = valueTime;
                                                delay.startTime = half.zero;
                                                delay.endTime = (half)instance.fallToStepDelayTime;

                                                //UnityEngine.Debug.Log($"{entityArray[index]} + {delay.time} + {time.count}");
                                                this.delay[index] = delay;
                                            }

                                            if (instance.fallToStepSpeed > math.FLT_MIN_NORMAL && index < velocityComponents.Length)
                                            {
                                                GameNodeVelocityComponent velocityComponent;
                                                velocityComponent.mode = GameNodeVelocityComponent.Mode.Direct;
                                                velocityComponent.time = time;
                                                velocityComponent.duration = instance.fallToStepTime;
                                                velocityComponent.value = math.forward(quaternion.RotateY(characterAangles[index].value)) * instance.fallToStepSpeed;

                                                velocityComponents[index].Add(velocityComponent);
                                            }
                                            else if (velocities.Length > index)
                                                velocities[index] = default;

                                            /*nodeStatus.value &= ~GameNodeActorStatus.NODE_STATUS_ACT;
                                            states[entityArray[index]] = nodeStatus;*/
                                        }
                                        /*else if (delay.Check(valueTime))
                                        {
                                            delay.Clear(valueTime);

                                            //UnityEngine.Debug.Log($"{entityArray[index]} + {delay.time} + {time.count}");
                                            this.delay[index] = delay;
                                        }*/
                                    }
                                    break;
                                default:
                                    value = GameNodeActorStatus.Status.Fall;
                                    break;
                            }

                            break;
                        case GameNodeActorStatus.Status.Jump:
                            if (index < velocityComponents.Length)
                            {
                                var velocityComponents = this.velocityComponents[index];
                                GameNodeVelocityComponent velocityComponent;
                                int length = velocityComponents.Length;
                                for(int i = 0; i < length; ++i)
                                {
                                    velocityComponent = velocityComponents[i];
                                    if (velocityComponent.mode == GameNodeVelocityComponent.Mode.Direct || velocityComponent.duration > math.FLT_MIN_NORMAL)
                                        continue;

                                    if(math.dot(velocityComponent.value, inveseGravityDirection) > math.FLT_MIN_NORMAL)
                                    {
                                        value = GameNodeActorStatus.Status.Jump;

                                        break;
                                    }
                                }
                            }

                            if(value == GameNodeActorStatus.Status.Normal)
                            {
                                switch (characterStatus.value)
                                {
                                    case GameNodeCharacterStatus.Status.Unsupported:
                                        double now = valueTime;
                                        if (delay.Check(now))
                                        {
                                            temp = delay.time + delay.endTime;
                                            temp -= instance.jumpTime;

                                            var fallTime = temp;
                                            fallTime -= instance.fallTime;

                                            if (now > fallTime &&
                                                now < temp &&
                                                math.dot(index < characterVelocities.Length ? characterVelocities[index].value : physicsVelocities[index].Linear, inveseGravityDirection) < 0.0f)
                                            {
                                                valueTime = fallTime;

                                                value = GameNodeActorStatus.Status.Fall;
                                            }
                                            else
                                                value = GameNodeActorStatus.Status.Jump;
                                        }
                                        else
                                        {
                                            valueTime = delay.time + delay.endTime;

                                            value = GameNodeActorStatus.Status.Fall;
                                        }
                                        break;
                                    case GameNodeCharacterStatus.Status.Supported:
                                        if (delay.Check(valueTime))
                                            value = GameNodeActorStatus.Status.Jump;
                                        else
                                        {
                                            valueTime = delay.time + delay.endTime;

                                            value = GameNodeActorStatus.Status.Fall;
                                        }
                                        break;
                                    case GameNodeCharacterStatus.Status.Sliding:
                                        if(delay.Check(valueTime))
                                            value = GameNodeActorStatus.Status.Jump;
                                        else
                                            valueTime = delay.time + delay.endTime;
                                        break;
                                    ///存疑:主要处理落于可滑动物体(斜面或非地面)时情况
                                    /*case GameNodeCharacterStatus.Status.Slide:
                                        if (delay.time > time)
                                            value = GameNodeActorStatus.Status.Jump;
                                        break;*/
                                    case GameNodeCharacterStatus.Status.Contacting:
                                    case GameNodeCharacterStatus.Status.Firming:
                                        if (delay.Check(valueTime))
                                        {
                                            delay.Clear(valueTime);

                                            this.delay[index] = delay;
                                        }
                                        else
                                        {
                                            var endTime = delay.time + delay.endTime;
                                            if (valueTime > endTime && endTime.count > time.count - 2)
                                                valueTime = endTime;
                                        }
                                        break;
                                    default:
                                        temp = delay.time + delay.endTime;
                                        temp -= instance.jumpTime + instance.fallTime;
                                        if (temp > time)
                                            value = GameNodeActorStatus.Status.Jump;
                                        else
                                        {
                                            valueTime = temp;

                                            value = GameNodeActorStatus.Status.Fall;
                                        }
                                        break;
                                }

                            }

                            if (value == GameNodeActorStatus.Status.Normal &&
                                (nodeStatus.value & GameNodeActorStatus.NODE_STATUS_ACT) == GameNodeActorStatus.NODE_STATUS_ACT)
                            {
                                if (instance.jumpToStepDelayTime > math.FLT_MIN_NORMAL && delay.Check(valueTime + instance.jumpTime + instance.fallTime))
                                {
                                    //valueTime = time;

                                    delay.time = valueTime;
                                    delay.startTime = half.zero;
                                    delay.endTime = (half)instance.jumpToStepDelayTime;
                                    this.delay[index] = delay;

                                    if (velocities.Length > index)
                                    {
                                        var velocity = velocities[index];
                                        velocity.value *= instance.jumpToStepSpeed;
                                        velocities[index] = velocity;
                                    }
                                    /*if (velocities.Length > index)
                                    {
                                        if (index < indirectVelocities.Length)
                                        {
                                            GameNodeVelocityIndirect indirectVelocity;
                                            indirectVelocity.time = time;
                                            indirectVelocity.duration = 0.0f;
                                            indirectVelocity.value = velocities[index].instance;

                                            indirectVelocities[index].Add(indirectVelocity);
                                        }

                                        velocities[index] = default;
                                    }*/
                                }
                                else if (delay.Check(valueTime))
                                {
                                    delay.Clear(valueTime);

                                    this.delay[index] = delay;
                                }

                                /*nodeStatus.value &= ~GameNodeActorStatus.NODE_STATUS_ACT;

                                states[entityArray[index]] = nodeStatus;*/
                            }
                            break;
                        /*case GameNodeActorStatus.Status.Climb:
                            if (velocities.Length > index)
                            {
                                if (index < indirectVelocities.Length)
                                {
                                    GameNodeVelocityIndirect indirectVelocity;
                                    indirectVelocity.time = time;
                                    indirectVelocity.duration = 0.0f;
                                    indirectVelocity.value = math.forward(rotations[index].Value) * velocities[index].value;

                                    indirectVelocities[index].Add(indirectVelocity);
                                }
                                
                                velocities[index] = default;
                            }
                            break;*/
                        default:
                            switch (characterStatus.value)
                            {
                                case GameNodeCharacterStatus.Status.Supported:
                                case GameNodeCharacterStatus.Status.Sliding:
                                case GameNodeCharacterStatus.Status.Contacting:
                                case GameNodeCharacterStatus.Status.Firming:
                                    break;
                                default:
                                    value = GameNodeActorStatus.Status.Fall;
                                    
                                    break;
                            }

                            break;
                    }

                    break;
            }

            /*if ((nodeStatus.value & GameNodeActorStatus.NODE_STATUS_ACT) == GameNodeActorStatus.NODE_STATUS_ACT)
            {
                switch(value)
                {
                    case GameNodeActorStatus.Status.Fall:
                    case GameNodeActorStatus.Status.Jump:
                        break;
                    default:
                        nodeStatus.value &= ~GameNodeActorStatus.NODE_STATUS_ACT;

                        states[entityArray[index]] = nodeStatus;
                        break;
                }
            }
            else if(value == GameNodeActorStatus.Status.Fall && actorStatus.value == GameNodeActorStatus.Status.Fall)
            {
                temp = valueTime;
                if(!delay.Check(temp) && temp - actorStatus.time > instance.fallDelayTime)
                {
                    nodeStatus.value |= GameNodeActorStatus.NODE_STATUS_ACT;

                    states[entityArray[index]] = nodeStatus;
                }
            }*/

            if (actorStatus.value != value)
            {
                switch (value)
                {
                    case GameNodeActorStatus.Status.Fall:
                        if (actorStatus.value != GameNodeActorStatus.Status.Jump && !delay.Check(valueTime))
                        {
                            nodeStatus.value |= GameNodeActorStatus.NODE_STATUS_ACT;

                            states[entityArray[index]] = nodeStatus;
                        }
                        break;
                    default:
                        if ((nodeStatus.value & GameNodeActorStatus.NODE_STATUS_ACT) == GameNodeActorStatus.NODE_STATUS_ACT)
                        {
                            nodeStatus.value &= ~GameNodeActorStatus.NODE_STATUS_ACT;

                            states[entityArray[index]] = nodeStatus;
                        }
                        break;
                }

                //UnityEngine.Debug.LogError($"{entityArray[index]} :  {value} : {characterStatus} : {nodeStatus.value} : {delay.time + delay.startTime} : {delay.time + delay.endTime} : {valueTime} : {actorStatus.time} : {time}");

                actorStatus.value = value;
                actorStatus.time = valueTime;

                actorStates[index] = actorStatus;
            }
        }
    }
    
    [BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Deterministic)]
    private struct UpdateStatesEx : IJobChunk
    {
        public GameTime time;

        [ReadOnly]
        public EntityTypeHandle entityType;

        [ReadOnly]
        public ComponentTypeHandle<PhysicsVelocity> physicsVelocityType;

        [ReadOnly]
        public ComponentTypeHandle<GameNodeStatus> nodeStatusType;

        [ReadOnly]
        public ComponentTypeHandle<GameNodeActorData> instanceType;

        [ReadOnly]
        public ComponentTypeHandle<GameNodeCharacterStatus> characterStatusType;

        [ReadOnly]
        public ComponentTypeHandle<GameNodeCharacterAngle> characterAngleType;

        [ReadOnly]
        public ComponentTypeHandle<GameNodeCharacterVelocity> characterVelocityType;

        public BufferTypeHandle<GameNodeVelocityComponent> velocityComponentType;

        public ComponentTypeHandle<GameNodeVelocity> velocityType;

        public ComponentTypeHandle<GameNodeDelay> delayType;
        
        public ComponentTypeHandle<GameNodeActorStatus> actorStatusType;
        
        [NativeDisableContainerSafetyRestriction]
        public ComponentLookup<GameNodeStatus> states;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            UpdateStates updateStates;
            updateStates.time = time;
            updateStates.entityArray = chunk.GetNativeArray(entityType);
            updateStates.physicsVelocities = chunk.GetNativeArray(ref physicsVelocityType);
            updateStates.nodeStates = chunk.GetNativeArray(ref nodeStatusType);
            updateStates.instances = chunk.GetNativeArray(ref instanceType);

            updateStates.characterStates = chunk.GetNativeArray(ref characterStatusType);

            updateStates.characterAangles = chunk.GetNativeArray(ref characterAngleType);

            updateStates.characterVelocities = chunk.GetNativeArray(ref characterVelocityType);

            updateStates.velocityComponents = chunk.GetBufferAccessor(ref velocityComponentType);

            updateStates.velocities = chunk.GetNativeArray(ref velocityType);

            updateStates.delay = chunk.GetNativeArray(ref delayType);
            updateStates.actorStates = chunk.GetNativeArray(ref actorStatusType);
            
            updateStates.states = states;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                updateStates.Execute(i);
        }
    }
    
    /*public uint climbBits;

    public uint terrainMask;
    public uint waterMask;

    public float depthOfWater;*/

    private EntityQuery __group;
    //private EntityQuery __syncDataGroup;
    private GameRollbackTime __time;

    public void OnCreate(ref SystemState state)
    {
        __group = state.GetEntityQuery(
            ComponentType.ReadOnly<GameNodeStatus>(),
            ComponentType.ReadOnly<GameNodeAngle>(),
            ComponentType.ReadOnly<GameNodeActorData>(),

            ComponentType.ReadOnly<GameNodeCharacterStatus>(),

            ComponentType.ReadWrite<GameNodeDelay>(),
            ComponentType.ReadWrite<GameNodeActorStatus>(),

            ComponentType.Exclude<GameNodeParent>(),
            ComponentType.Exclude<Disabled>());

        //__syncDataGroup = state.GetEntityQuery(ComponentType.ReadOnly<GameSyncData>());
        __time = new GameRollbackTime(ref state);
    }

    public void OnDestroy(ref SystemState state)
    {

    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        //var syncData = __syncDataGroup.GetSingleton<GameSyncData>();

        UpdateStatesEx updateStates;
        updateStates.time = __time.now;
        updateStates.entityType = state.GetEntityTypeHandle();
        updateStates.physicsVelocityType = state.GetComponentTypeHandle<PhysicsVelocity>(true);
        updateStates.nodeStatusType = state.GetComponentTypeHandle<GameNodeStatus>(true);
        updateStates.instanceType = state.GetComponentTypeHandle<GameNodeActorData>(true);
        updateStates.characterStatusType = state.GetComponentTypeHandle<GameNodeCharacterStatus>(true);
        updateStates.characterAngleType = state.GetComponentTypeHandle<GameNodeCharacterAngle>(true);
        updateStates.characterVelocityType = state.GetComponentTypeHandle<GameNodeCharacterVelocity>(true);
        updateStates.velocityComponentType = state.GetBufferTypeHandle<GameNodeVelocityComponent>();
        updateStates.velocityType = state.GetComponentTypeHandle<GameNodeVelocity>();
        updateStates.delayType = state.GetComponentTypeHandle<GameNodeDelay>();
        updateStates.actorStatusType = state.GetComponentTypeHandle<GameNodeActorStatus>();
        updateStates.states = state.GetComponentLookup<GameNodeStatus>();

        state.Dependency = updateStates.ScheduleParallel(__group, state.Dependency);
    }
}