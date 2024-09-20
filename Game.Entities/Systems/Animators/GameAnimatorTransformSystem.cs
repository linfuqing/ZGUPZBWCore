using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using ZG;

[assembly: RegisterGenericJobType(typeof(GameTransformFactoryCopy<GameAnimatorTransform, GameAnimatorVelocity, GameAnimatorTransformFactorySystem.Handler>))]
//[assembly: RegisterGenericJobType(typeof(GameTransformUpdateDestinations<GameAnimatorTransform, GameAnimatorTransformUpdateSystem.Handler, GameAnimatorTransformUpdateSystem.Factory>))]
[assembly: RegisterGenericJobType(typeof(GameTransformUpdateAll<GameAnimatorTransform, GameAnimatorTransformUpdateSystem.Handler, GameAnimatorTransformUpdateSystem.Factory>))]
[assembly: RegisterGenericJobType(typeof(GameTransformSmoothDamp<GameAnimatorTransform, GameAnimatorVelocity, GameAnimatorTransformSystem.TransformJob>))]

[BurstCompile, 
    UpdateInGroup(typeof(GameRollbackSystemGroup)), 
    UpdateAfter(typeof(GameNodeSystem)), 
    UpdateBefore(typeof(GameUpdateSystemGroup))]
public partial struct GameAnimatorVelocitySystem : ISystem
{
    private struct AddVelocity
    {
        public bool isUpdate;
        //[ReadOnly]
        //public NativeArray<GameNodeAngle> angles;
        [ReadOnly]
        public NativeArray<GameNodeDesiredVelocity> nodeVelocities;
        public NativeArray<GameAnimatorDesiredVelocity> animatorVelocities;

        public void Execute(int index)
        {
            var nodeVelocity = nodeVelocities[index];
            var animatorVelocity = isUpdate ? default : animatorVelocities[index];
            animatorVelocity.deltaTime += nodeVelocity.time;
            animatorVelocity.sign += nodeVelocity.sign;
            animatorVelocity.distance += nodeVelocity.value * nodeVelocity.time;// math.mul(math.inverse(quaternion.RotateY(angles[index].value)), nodeVelocity.value * nodeVelocity.time);
            animatorVelocities[index] = animatorVelocity;
        }
    }

    [BurstCompile]
    private struct AddVelocityEx : IJobChunk
    {
        public bool isUpdate;
        //[ReadOnly]
        //public ComponentTypeHandle<GameNodeAngle> angleType;
        [ReadOnly]
        public ComponentTypeHandle<GameNodeDesiredVelocity> nodeVelocityType;
        public ComponentTypeHandle<GameAnimatorDesiredVelocity> animatorVelocityType;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            AddVelocity addVelocity;
            addVelocity.isUpdate = isUpdate;
            //addVelocity.angles = chunk.GetNativeArray(angleType);
            addVelocity.nodeVelocities = chunk.GetNativeArray(ref nodeVelocityType);
            addVelocity.animatorVelocities = chunk.GetNativeArray(ref animatorVelocityType);

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                addVelocity.Execute(i);
        }
    }

    private EntityQuery __group;
    private GameUpdateTime __time;
    //private EntityQuery __syncDataGroup;
    //private EntityQuery __updateDataGroup;

    public void OnCreate(ref SystemState state)
    {
        __group = state.GetEntityQuery(
            //ComponentType.ReadOnly<GameNodeAngle>(),
            ComponentType.ReadOnly<GameNodeDesiredVelocity>(),
            ComponentType.ReadWrite<GameAnimatorDesiredVelocity>());

        __time = new GameUpdateTime(ref state);

        //__syncDataGroup = state.GetEntityQuery(ComponentType.ReadOnly<GameSyncData>());
        //__updateDataGroup = state.GetEntityQuery(ComponentType.ReadOnly<GameUpdateData>());
    }

    public void OnDestroy(ref SystemState state)
    {

    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        //var syncData = __syncDataGroup.GetSingleton<GameSyncData>();
        //var updateData = __updateDataGroup.GetSingleton<GameUpdateData>();

        AddVelocityEx addVelocityEx;
        addVelocityEx.isUpdate = __time.IsVail(-1);// updateData.IsUpdate(syncData.frameIndex, -1);
        //addVelocityEx.angleType = state.GetComponentTypeHandle<GameNodeAngle>(true);
        addVelocityEx.nodeVelocityType = state.GetComponentTypeHandle<GameNodeDesiredVelocity>(true);
        addVelocityEx.animatorVelocityType = state.GetComponentTypeHandle<GameAnimatorDesiredVelocity>();

        state.Dependency = addVelocityEx.ScheduleParallel(__group, state.Dependency);
    }
}

[BurstCompile, UpdateInGroup(typeof(GameTransformFactroySystemGroup))]
public partial struct GameAnimatorTransformFactorySystem : ISystem
{
    public struct Handler : IGameTransformEntityHandler<GameAnimatorTransform>
    {
        public float deltaTime;
        [ReadOnly]
        public ComponentLookup<GameNodeStaticThreshold> staticThresholds;
        [ReadOnly]
        public ComponentLookup<GameNodeCharacterData> characters;
        [ReadOnly]
        public ComponentLookup<GameNodeCharacterStatus> states;
        [ReadOnly]
        public ComponentLookup<GameNodeCharacterSurface> surfaces;
        [ReadOnly]
        public ComponentLookup<GameAnimatorDesiredVelocity> velocities;

        public GameAnimatorTransform Get(int index, in Entity entity)
        {
            float fraction;
            var status = states[entity];
            if (status.area == GameNodeCharacterStatus.Area.Normal)
            {
                var stepFraction = characters[entity].stepFraction;
                fraction = (math.max(surfaces[entity].fraction, stepFraction) - stepFraction) / (1.0f - stepFraction);//surfaces[index].fraction;
            }
            else
                fraction = 0.0f;

            var velocity = velocities[entity];
            var value = velocity.distance / deltaTime;
            GameAnimatorTransform transform;
            transform.fraction = fraction;

            float lengthsq = math.lengthsq(value);
            transform.forwardAmount = math.sqrt(lengthsq) * math.sign(velocity.sign);
            transform.turnAmount = lengthsq > staticThresholds[entity].value && velocity.deltaTime > math.FLT_MIN_NORMAL ?
                math.atan2(value.x, value.z)
                                    ///Math.SignedAngle似乎有问题
                                    /*Math.SignedAngle(
                                        math.float3(0.0f, 0.0f, 1.0f), 
                                        value / transform.forwardAmount,
                                        math.up())*/ / deltaTime :
                0.0f;

            return transform;
        }
    }


    public struct Factory : GameTransformFactory<GameAnimatorTransform, GameAnimatorVelocity, Handler>.IFactory<Handler>
    {
        public float delta;

        public Handler Get(ref SystemState systemState)
        {
            Handler handler;
            handler.deltaTime = delta;
            handler.staticThresholds = systemState.GetComponentLookup<GameNodeStaticThreshold>(true);
            handler.characters = systemState.GetComponentLookup<GameNodeCharacterData>(true);
            handler.states = systemState.GetComponentLookup<GameNodeCharacterStatus>(true);
            handler.surfaces = systemState.GetComponentLookup<GameNodeCharacterSurface>(true);
            handler.velocities = systemState.GetComponentLookup<GameAnimatorDesiredVelocity>(true);
            return handler;
        }
    }


    public static readonly int InnerloopBatchCount = 1;

    //private EntityQuery __syncGroup;

    //private EntityQuery __updateGroup;

    private GameUpdateTime __time;

    private GameTransformFactory<GameAnimatorTransform, GameAnimatorVelocity, Handler> __instance;

    public void OnCreate(ref SystemState state)
    {
        //__syncGroup = state.GetEntityQuery(ComponentType.ReadOnly<GameSyncData>());
        //__updateGroup = state.GetEntityQuery(ComponentType.ReadOnly<GameUpdateData>());

        __time = new GameUpdateTime(ref state);

        __instance = new GameTransformFactory<GameAnimatorTransform, GameAnimatorVelocity, Handler>(
            new ComponentType[]
            {
                ComponentType.ReadOnly<GameNodeCharacterData>(),
                ComponentType.ReadOnly<GameNodeCharacterStatus>(),
                ComponentType.ReadOnly<GameNodeCharacterSurface>(),
                ComponentType.ReadOnly<GameAnimatorDesiredVelocity>()
            }, ref state);
    }

    public void OnDestroy(ref SystemState state)
    {

    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        //var syncData = __syncGroup.GetSingleton<GameSyncData>();
        //var updateData = __updateGroup.GetSingleton<GameUpdateData>();

        Factory factory;
        factory.delta = __time.RollbackTime.frameIndex % __time.frameCount * __time.RollbackTime.frameDelta;
        __instance.Update(InnerloopBatchCount, __time.RollbackTime.now, ref state, factory);
    }
}

[BurstCompile, UpdateInGroup(typeof(GameTransformSimulationSystemGroup))]
public partial struct GameAnimatorTransformUpdateSystem : ISystem
{
    public struct Handler : IGameTransformHandler<GameAnimatorTransform>
    {
        public float deltaTime;
        [ReadOnly]
        public NativeArray<GameNodeStaticThreshold> staticThresholds;
        [ReadOnly]
        public NativeArray<GameNodeCharacterData> characters;
        [ReadOnly]
        public NativeArray<GameNodeCharacterStatus> states;
        [ReadOnly]
        public NativeArray<GameNodeCharacterSurface> surfaces;
        [ReadOnly]
        public NativeArray<GameAnimatorDesiredVelocity> velocities;

        public GameAnimatorTransform Get(int index)
        {
            float fraction;
            var status = states[index];
            if (status.area == GameNodeCharacterStatus.Area.Normal)
            {
                var stepFraction = characters[index].stepFraction;
                fraction = (math.max(surfaces[index].fraction, stepFraction) - stepFraction) / (1.0f - stepFraction);//surfaces[index].fraction;
            }
            else
                fraction = 0.0f;

            var velocity = velocities[index];
            var value = velocity.distance / deltaTime;
            GameAnimatorTransform transform;
            transform.fraction = fraction;
            float lengthsq = math.lengthsq(value);
            transform.forwardAmount = math.sqrt(lengthsq) * math.sign(velocity.sign);
            transform.turnAmount = lengthsq > staticThresholds[index].value && velocity.deltaTime > math.FLT_MIN_NORMAL ?
                math.atan2(value.x, value.z) / deltaTime :
                0.0f;

            return transform;
        }
    }

    public struct Factory : IGameTransformFactory<GameAnimatorTransform, Handler>
    {
        public float deltaTime;
        [ReadOnly]
        public ComponentTypeHandle<GameNodeStaticThreshold> staticThresholdType;
        [ReadOnly]
        public ComponentTypeHandle<GameNodeCharacterData> characterType;
        [ReadOnly]
        public ComponentTypeHandle<GameNodeCharacterStatus> statusType;
        [ReadOnly]
        public ComponentTypeHandle<GameNodeCharacterSurface> surfaceType;
        [ReadOnly]
        public ComponentTypeHandle<GameAnimatorDesiredVelocity> velocityType;

        public Handler Get(in ArchetypeChunk chunk)
        {
            Handler handler;
            handler.deltaTime = deltaTime;
            handler.staticThresholds = chunk.GetNativeArray(ref staticThresholdType);
            handler.characters = chunk.GetNativeArray(ref characterType);
            handler.states = chunk.GetNativeArray(ref statusType);
            handler.surfaces = chunk.GetNativeArray(ref surfaceType);
            handler.velocities = chunk.GetNativeArray(ref velocityType);
            return handler;
        }
    }

    /*private EntityQuery __syncGroup;
    private EntityQuery __updateGroup;*/
    private GameUpdateTime __time;
    private GameTransformSimulator<GameAnimatorTransform> __instance;

    public void OnCreate(ref SystemState state)
    {
        //__syncGroup = state.GetEntityQuery(ComponentType.ReadOnly<GameSyncData>());
        //__updateGroup = state.GetEntityQuery(ComponentType.ReadOnly<GameUpdateData>());
        __time = new GameUpdateTime(ref state);

        __instance = new GameTransformSimulator<GameAnimatorTransform>(
            new ComponentType[]
            {
                ComponentType.ReadOnly<GameNodeStaticThreshold>(),
                ComponentType.ReadOnly<GameNodeCharacterData>(),
                ComponentType.ReadOnly<GameNodeCharacterStatus>(),
                ComponentType.ReadOnly<GameNodeCharacterSurface>(),
                ComponentType.ReadOnly<GameNodeCharacterDesiredVelocity>()
            }, ref state);
    }

    public void OnDestroy(ref SystemState state)
    {

    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        //var syncData = __syncGroup.GetSingleton<GameSyncData>();
        //var updateData = __updateGroup.GetSingleton<GameUpdateData>();

        __instance.Update<Handler, Factory>(
            /*GameTransformUtility.GetUpdateType(
                syncData.realFrameIndex,
                syncData.frameIndex,
                updateData.frameCount),*/
            __time.RollbackTime.now,
            ref state,
            __Get(__time.delta, ref state));
    }

    private Factory __Get(float updateDelta, ref SystemState state)
    {
        Factory factory;
        factory.deltaTime = updateDelta;
        factory.staticThresholdType = state.GetComponentTypeHandle<GameNodeStaticThreshold>(true);
        factory.characterType = state.GetComponentTypeHandle<GameNodeCharacterData>(true);
        factory.statusType = state.GetComponentTypeHandle<GameNodeCharacterStatus>(true);
        factory.surfaceType = state.GetComponentTypeHandle<GameNodeCharacterSurface>(true);
        factory.velocityType = state.GetComponentTypeHandle<GameAnimatorDesiredVelocity>(true);
        return factory;
    }
}

[BurstCompile, UpdateBefore(typeof(GameAnimatorSystem)), UpdateAfter(typeof(GameTransformSystem))]
public partial struct GameAnimatorTransformSystem : ISystem
{
    private struct CalculateTransform
    {
        public float deltaTime;

        [ReadOnly]
        public BufferAccessor<GameAnimatorForwardKeyframe> forwardKeyframes;

        [ReadOnly]
        public BufferAccessor<GameAnimatorTurnKeyframe> turnKeyframes;

        [ReadOnly]
        public NativeArray<GameAnimatorTransformData> instances;

        public NativeArray<GameAnimatorTransformInfo> infos;

        public GameTransformCalculator<GameAnimatorTransform, GameAnimatorVelocity> calculator;

        public void Execute(int index)
        {
            var info = infos[index];
            info.dirtyFlag = 0;

            var value = calculator.Execute(index, info.value);

            if (info.heightAmount != value.fraction)
            {
                info.dirtyFlag |= GameAnimatorTransformInfo.DirtyFlag.Height;

                info.heightAmount = value.fraction;
            }

            /*if (value.fraction > math.FLT_MIN_NORMAL)
            {
                if (info.value.fraction < value.fraction)
                {
                    info.maxFraction = math.max(info.maxFraction, value.fraction);
                    info.heightAmount = 1.0f;
                }
                else
                {
                    UnityEngine.Assertions.Assert.IsTrue(info.maxFraction > math.FLT_MIN_NORMAL);

                    info.heightAmount = value.fraction / info.maxFraction;
                }
            }
            else
            {
                info.maxFraction = 0.0f;
                info.heightAmount = 0.0f;
            }*/

            var forwardKeyframes = this.forwardKeyframes[index].Reinterpret<float>().AsNativeArray().Slice();
            int maxLevel = forwardKeyframes.Length - 1, level = CollectionUtilityEx.BinarySearch(forwardKeyframes, value.forwardAmount);
            float minValue;
            if (level < 0)
                minValue = forwardKeyframes[++level];
            else
            {
                minValue = forwardKeyframes[level];
                if (minValue == value.forwardAmount || level == maxLevel)
                    --level;
            }

            int nextLevel = level + 1;
            while (nextLevel < maxLevel && forwardKeyframes[nextLevel] == forwardKeyframes[nextLevel + 1])
                level = nextLevel++;

            float forwardAmount = math.smoothstep(minValue, forwardKeyframes[level + 1], value.forwardAmount);
            forwardAmount += level - CollectionUtilityEx.BinarySearch(forwardKeyframes, 0.0f);
            if(info.forwardAmount != forwardAmount)
            {
                info.dirtyFlag |= GameAnimatorTransformInfo.DirtyFlag.Forward;

                info.forwardAmount = forwardAmount;
            }

            value.turnAmount = math.lerp(info.value.turnAmount, value.turnAmount, instances[index].turnInterpolationSpeed * deltaTime);

            var turnKeyframes = this.turnKeyframes[index].Reinterpret<float>().AsNativeArray().Slice();
            level = CollectionUtilityEx.BinarySearch(turnKeyframes, value.turnAmount);
            if (level < 0)
                minValue = turnKeyframes[++level];
            else
            {
                minValue = turnKeyframes[level];
                if (minValue == value.turnAmount || level == turnKeyframes.Length - 1)
                    --level;
            }

            float turnAmount = math.smoothstep(minValue, turnKeyframes[level + 1], value.turnAmount);
            turnAmount += level - CollectionUtilityEx.BinarySearch(turnKeyframes, 0.0f);
            if (info.turnAmount != turnAmount)
            {
                info.dirtyFlag |= GameAnimatorTransformInfo.DirtyFlag.Turn;

                info.turnAmount = turnAmount;
            }

            info.value = value;

            infos[index] = info;
        }
    }

    public struct TransformJob : IGameTransformJob<GameAnimatorTransform, GameTransformCalculator<GameAnimatorTransform, GameAnimatorVelocity>>
    {
        public float deltaTime;

        [ReadOnly]
        public BufferTypeHandle<GameAnimatorForwardKeyframe> forwardKeyframeType;

        [ReadOnly]
        public BufferTypeHandle<GameAnimatorTurnKeyframe> turnKeyframeType;

        [ReadOnly]
        public ComponentTypeHandle<GameAnimatorTransformData> instanceType;

        public ComponentTypeHandle<GameAnimatorTransformInfo> infoType;

        public void Execute(
            in GameTransformCalculator<GameAnimatorTransform, GameAnimatorVelocity> calculator, 
            in ArchetypeChunk chunk, 
            int unfilteredChunkIndex, 
            bool useEnabledMask, 
            in v128 chunkEnabledMask)
        {
            CalculateTransform calculateTransform;
            calculateTransform.deltaTime = deltaTime;
            calculateTransform.forwardKeyframes = chunk.GetBufferAccessor(ref forwardKeyframeType);
            calculateTransform.turnKeyframes = chunk.GetBufferAccessor(ref turnKeyframeType);
            calculateTransform.instances = chunk.GetNativeArray(ref instanceType);
            calculateTransform.infos = chunk.GetNativeArray(ref infoType);
            calculateTransform.calculator = calculator;

            int count = chunk.Count;
            for (int i = 0; i < count; ++i)
                calculateTransform.Execute(i);
        }
    }

    private GameTransformApplication<GameAnimatorTransform, GameAnimatorVelocity> __instance;

    public void OnCreate(ref SystemState state)
    {
        __instance = new GameTransformApplication<GameAnimatorTransform, GameAnimatorVelocity>(
            new ComponentType[]
            {
                ComponentType.ReadOnly<GameAnimatorForwardKeyframe>(),
                ComponentType.ReadOnly<GameAnimatorTurnKeyframe>(),
                ComponentType.ReadOnly<GameAnimatorTransformData>(),
                ComponentType.ReadWrite<GameAnimatorTransformInfo>()
            }, ref state);
    }

    public void OnDestroy(ref SystemState state)
    {

    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        __instance.Update(ref state, __Get(ref state));
    }

    private TransformJob __Get(ref SystemState state)
    {
        TransformJob transformJob;
        transformJob.deltaTime = state.WorldUnmanaged.Time.DeltaTime;
        transformJob.forwardKeyframeType = state.GetBufferTypeHandle<GameAnimatorForwardKeyframe>(true);
        transformJob.turnKeyframeType = state.GetBufferTypeHandle<GameAnimatorTurnKeyframe>(true);
        transformJob.instanceType = state.GetComponentTypeHandle<GameAnimatorTransformData>(true);
        transformJob.infoType = state.GetComponentTypeHandle<GameAnimatorTransformInfo>();
        return transformJob;
    }
}
