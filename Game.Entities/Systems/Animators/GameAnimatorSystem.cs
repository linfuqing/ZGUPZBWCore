using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Jobs;
using Unity.Entities;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;
using ZG;

[BurstCompile, UpdateInGroup(typeof(GameRollbackSystemGroup), OrderLast = true)/*, UpdateAfter(typeof(GameUpdateSystemGroup))*/]
public partial struct GameAnimatorTimeSystem : ISystem
{
    private struct Delay
    {
        [ReadOnly]
        public NativeArray<GameNodeDelay> sources;
        [ReadOnly]
        public NativeArray<GameAnimatorDelay> destinations;
        public BufferAccessor<GameAnimatorDelayInfo> delayInfos;

        public void Execute(int index)
        {
            var source = sources[index];
            var delayInfos = this.delayInfos[index];
            double startTime = source.time + source.startTime;
            int length = delayInfos.Length;
            while (length > 0)
            {
                if (delayInfos[length - 1].startTime < startTime)
                    break;

                --length;
            }

            delayInfos.ResizeUninitialized(length);

            bool isContains;
            if (length < 1)
                isContains = destinations[index].Equals(source);
            else
            {
                var delayInfo = delayInfos[--length];
                delayInfo.endTime = math.min(delayInfo.endTime, source.time);
                delayInfos[length] = delayInfo;

                isContains = false;
            }

            if (!isContains)
            {
                GameAnimatorDelayInfo delayInfo;
                delayInfo.startTime = startTime;
                delayInfo.endTime = source.time + source.endTime;
                delayInfos.Add(delayInfo);
            }
        }
    }

    [BurstCompile]
    private struct DelayEx : IJobChunk
    {
        [ReadOnly]
        public ComponentTypeHandle<GameNodeDelay> sourceType;
        [ReadOnly]
        public ComponentTypeHandle<GameAnimatorDelay> destinationType;
        public BufferTypeHandle<GameAnimatorDelayInfo> delayInfoType;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Delay delay;
            delay.sources = chunk.GetNativeArray(ref sourceType);
            delay.destinations = chunk.GetNativeArray(ref destinationType);
            delay.delayInfos = chunk.GetBufferAccessor(ref delayInfoType);

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                delay.Execute(i);
        }
    }

    private struct DesiredChange
    {
        [ReadOnly]
        public NativeArray<GameNodeDesiredStatus> sources;
        [ReadOnly]
        public NativeArray<GameAnimatorDesiredStatus> destinations;
        public BufferAccessor<GameAnimatorDesiredStatusInfo> statusInfos;

        public BufferAccessor<GameTransformKeyframe<GameTransform>> transformKeyframes;
        public BufferAccessor<GameTransformKeyframe<GameAnimatorTransform>> animationKeyframes;

        public void Execute(int index)
        {
            var status = sources[index];
            var statusInfos = this.statusInfos[index];
            double time = status.time;
            int length = statusInfos.Length;
            while (length > 0)
            {
                if (statusInfos[length - 1].time < time)
                    break;

                --length;
            }

            statusInfos.ResizeUninitialized(length);

            int value = (int)status.value;
            if (value != (length < 1 ? destinations[index].value : statusInfos[length - 1].value))
            {
                GameAnimatorDesiredStatusInfo statusInfo;
                statusInfo.value = value;
                statusInfo.time = time;
                statusInfos.Add(statusInfo);

                if (status.value == GameNodeDesiredStatus.Status.Pivoting)
                {
                    if (index < transformKeyframes.Length)
                    {
                        var transformKeyframes = this.transformKeyframes[index];
                        int transformKeyframeIndex = GameTransformKeyframe<GameTransform>.BinarySearch(ref transformKeyframes, status.time);
                        if (transformKeyframeIndex >= 0)
                        {
                            if (transformKeyframes.ElementAt(transformKeyframeIndex).time == status.time)
                                --transformKeyframeIndex;

                            if (transformKeyframeIndex >= 0)
                            {
                                var transformKeyframe = transformKeyframes[transformKeyframeIndex];
                                transformKeyframe.time = status.time;

                                if (++transformKeyframeIndex < transformKeyframes.Length)
                                    transformKeyframe.value.value.pos = transformKeyframes[transformKeyframeIndex].value.value.pos;

                                transformKeyframes.Insert(transformKeyframeIndex, transformKeyframe);
                            }
                        }
                    }

                    if (index < animationKeyframes.Length)
                    {
                        var animationKeyframes = this.animationKeyframes[index];
                        int animationKeyframeIndex = GameTransformKeyframe<GameAnimatorTransform>.BinarySearch(ref animationKeyframes, status.time);
                        if (animationKeyframeIndex >= 0)
                        {
                            if (animationKeyframes.ElementAt(animationKeyframeIndex).time == status.time)
                                --animationKeyframeIndex;

                            if (animationKeyframeIndex >= 0)
                            {
                                var transformKeyframe = animationKeyframes[animationKeyframeIndex];
                                transformKeyframe.time = status.time;

                                if (++animationKeyframeIndex < animationKeyframes.Length)
                                {
                                    ref var temp = ref animationKeyframes.ElementAt(animationKeyframeIndex);

                                    transformKeyframe.value.fraction = temp.value.fraction;
                                    transformKeyframe.value.forwardAmount = temp.value.forwardAmount;
                                }

                                animationKeyframes.Insert(animationKeyframeIndex, transformKeyframe);
                            }
                        }
                    }
                }
            }
        }
    }

    [BurstCompile]
    private struct DesiredChangeEx : IJobChunk
    {
        [ReadOnly]
        public ComponentTypeHandle<GameNodeDesiredStatus> sourceType;
        [ReadOnly]
        public ComponentTypeHandle<GameAnimatorDesiredStatus> destinationType;
        public BufferTypeHandle<GameAnimatorDesiredStatusInfo> statusInfoType;

        public BufferTypeHandle<GameTransformKeyframe<GameTransform>> transformKeyframeType;
        public BufferTypeHandle<GameTransformKeyframe<GameAnimatorTransform>> animationKeyframeType;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            DesiredChange change;
            change.sources = chunk.GetNativeArray(ref sourceType);
            change.destinations = chunk.GetNativeArray(ref destinationType);
            change.statusInfos = chunk.GetBufferAccessor(ref statusInfoType);
            change.transformKeyframes = chunk.GetBufferAccessor(ref transformKeyframeType);
            change.animationKeyframes = chunk.GetBufferAccessor(ref animationKeyframeType);

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                change.Execute(i);
        }
    }

    private struct ActorChange
    {
        [ReadOnly]
        public NativeArray<GameNodeActorStatus> sources;
        [ReadOnly]
        public NativeArray<GameAnimatorActorStatus> destinations;
        public BufferAccessor<GameAnimatorActorStatusInfo> statusInfos;

        public void Execute(int index)
        {
            var status = sources[index];
            var statusInfos = this.statusInfos[index];
            double time = status.time;
            int length = statusInfos.Length;
            while (length > 0)
            {
                if (statusInfos[length - 1].time < time)
                    break;

                --length;
            }

            statusInfos.ResizeUninitialized(length);

            int value = (int)status.value;
            if (value != (length < 1 ? destinations[index].value : statusInfos[length - 1].value))
            {
                GameAnimatorActorStatusInfo statusInfo;
                statusInfo.value = (int)status.value;
                statusInfo.time = time;
                statusInfos.Add(statusInfo);
            }
        }
    }

    [BurstCompile]
    private struct ActorChangeEx : IJobChunk
    {
        [ReadOnly]
        public ComponentTypeHandle<GameNodeActorStatus> sourceType;
        [ReadOnly]
        public ComponentTypeHandle<GameAnimatorActorStatus> destinationType;
        public BufferTypeHandle<GameAnimatorActorStatusInfo> statusInfoType;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            ActorChange change;
            change.sources = chunk.GetNativeArray(ref sourceType);
            change.destinations = chunk.GetNativeArray(ref destinationType);
            change.statusInfos = chunk.GetBufferAccessor(ref statusInfoType);

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                change.Execute(i);
        }
    }

    private struct Break
    {
        public double time;

        [ReadOnly]
        public NativeArray<GameAnimatorActorTimeToLive> timeToLives;
        [ReadOnly]
        public NativeArray<GameEntityBreakInfo> breakInfos;
        [ReadOnly]
        public NativeArray<GameEntityActorInfo> actorInfos;
        public BufferAccessor<GameAnimatorBreakInfo> infos;

        public void Execute(int index)
        {
            var breakInfo = breakInfos[index];
            var actorInfo = actorInfos[index];
            if (actorInfo.version != breakInfo.version)
                return;

            float timeToLive = timeToLives[index].value;
            double time = this.time - timeToLive, commandTime = breakInfo.commandTime;// math.max(this.time, breakInfo.commandTime);
            /*if (commandTime < time)
                return;*/

            var infos = this.infos[index];
            int length = infos.Length;
            for (int i = 0; i < length; ++i)
            {
                ref var info = ref infos.ElementAt(i);
                if (info.version >= breakInfo.version)
                {
                    if (info.time > this.time || info.time < commandTime && (commandTime - info.time) > timeToLive)
                    {
                        infos.RemoveRange(i, length - i);

                        break;
                    }
                    else
                        return;
                }
                else if (info.time < time)
                {
                    infos.RemoveAt(i--);

                    --length;
                }
            }

            GameAnimatorBreakInfo result;
            result.version = breakInfo.version;
            //result.flag = 0;

            /*if (actorTimes[index].value > commandTime)
                result.flag |= GameAnimatorBreakInfo.Flag.Delay;*/

            result.delayIndex = breakInfo.delayIndex;

            result.time = commandTime;
            infos.Add(result);
        }
    }

    [BurstCompile]
    private struct BreakEx : IJobChunk
    {
        public double time;

        [ReadOnly]
        public ComponentTypeHandle<GameAnimatorActorTimeToLive> timeToLiveType;
        [ReadOnly]
        public ComponentTypeHandle<GameEntityBreakInfo> breakInfoType;
        [ReadOnly]
        public ComponentTypeHandle<GameEntityActorInfo> actorInfoType;
        //[ReadOnly]
        //public ComponentTypeHandle<GameEntityActorTime> actorTimeType;
        public BufferTypeHandle<GameAnimatorBreakInfo> infoType;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Break @break;
            @break.time = time;
            @break.timeToLives = chunk.GetNativeArray(ref timeToLiveType);
            @break.breakInfos = chunk.GetNativeArray(ref breakInfoType);
            @break.actorInfos = chunk.GetNativeArray(ref actorInfoType);
            //@break.actorTimes = chunk.GetNativeArray(ref actorTimeType);
            @break.infos = chunk.GetBufferAccessor(ref infoType);

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                @break.Execute(i);
        }
    }

    private struct Do
    {
        public double time;

        [ReadOnly]
        public NativeArray<GameAnimatorActorTimeToLive> timeToLives;
        [ReadOnly]
        public NativeArray<GameEntityActorInfo> actorInfos;
        [ReadOnly]
        public NativeArray<GameEntityActionInfo> actionInfos;
        public BufferAccessor<GameAnimatorDoInfo> infos;

        public void Execute(int index)
        {
            var actionInfo = actionInfos[index];
            var actorInfo = actorInfos[index];
            if (actorInfo.version != actionInfo.version)
                return;

            float timeToLive = timeToLives[index].value;
            double time = this.time - timeToLive, commandTime = actorInfo.alertTime;// math.max(this.time, actorInfo.alertTime);
            /*if (commandTime < time)
                return;*/

            var infos = this.infos[index];
            int length = infos.Length;
            for (int i = 0; i < length; ++i)
            {
                ref var info = ref infos.ElementAt(i);

                if (info.version >= actionInfo.version)
                {
                    if (info.time > this.time || info.time < commandTime && (commandTime - info.time) > timeToLive)
                    {
                        infos.RemoveRange(i, length - i);
                        /*if (info.time > this.time || math.abs(commandTime - info.time) > timeToLive)
                            infos.RemoveRange(i, length - i);
                        else
                        {
                            //Debug.Log("Missing Do");

                            return;
                        }*/

                        break;
                    }
                    else
                        return;
                }
                else if (info.time < time)
                {
                    infos.RemoveAt(i--);

                    --length;
                }
            }

            GameAnimatorDoInfo result;
            result.version = actionInfo.version;
            result.index = actionInfo.index;
            result.entity = actionInfo.entity;
            result.time = commandTime;
            infos.Add(result);
        }
    }

    [BurstCompile]
    private struct DoEx : IJobChunk
    {
        public double time;

        [ReadOnly]
        public ComponentTypeHandle<GameAnimatorActorTimeToLive> timeToLiveType;
        [ReadOnly]
        public ComponentTypeHandle<GameEntityActionInfo> actionInfoType;
        [ReadOnly]
        public ComponentTypeHandle<GameEntityActorInfo> actorInfoType;
        public BufferTypeHandle<GameAnimatorDoInfo> infoType;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Do @do;
            @do.time = time;
            @do.timeToLives = chunk.GetNativeArray(ref timeToLiveType);
            @do.actionInfos = chunk.GetNativeArray(ref actionInfoType);
            @do.actorInfos = chunk.GetNativeArray(ref actorInfoType);
            @do.infos = chunk.GetBufferAccessor(ref infoType);

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                @do.Execute(i);
        }
    }

    private EntityQuery __animationElapsedTimeGroup;

    private EntityQuery __groupToDelay;
    private EntityQuery __groupToDesiredChange;
    private EntityQuery __groupToActorChange;
    private EntityQuery __groupToBreak;
    private EntityQuery __groupToDo;

    private ComponentTypeHandle<GameNodeDelay> __nodeDelayType;
    private ComponentTypeHandle<GameAnimatorDelay> __animatorDelayType;
    private BufferTypeHandle<GameAnimatorDelayInfo> __delayInfoType;

    private ComponentTypeHandle<GameNodeDesiredStatus> __nodeDesiredStatusType;
    private ComponentTypeHandle<GameAnimatorDesiredStatus> __animatorDesiredStatusType;
    private BufferTypeHandle<GameAnimatorDesiredStatusInfo> __animatorDesiredStatusInfoType;
    private BufferTypeHandle<GameTransformKeyframe<GameTransform>> __transformKeyframeType;
    private BufferTypeHandle<GameTransformKeyframe<GameAnimatorTransform>> __animationKeyframeType;

    private ComponentTypeHandle<GameNodeActorStatus> __nodeActorStatusType;
    private ComponentTypeHandle<GameAnimatorActorStatus> __animatorActorStatusType;
    private BufferTypeHandle<GameAnimatorActorStatusInfo> __animatorActorStatusInfoType;

    private ComponentTypeHandle<GameAnimatorActorTimeToLive> __animatorActorTimeToLiveType;
    private ComponentTypeHandle<GameEntityBreakInfo> __breakInfoType;
    private ComponentTypeHandle<GameEntityActorInfo> __actorInfoType;
    //private ComponentTypeHandle<GameEntityActorTime> __actorTimeType;
    private BufferTypeHandle<GameAnimatorBreakInfo> __animatorBreakInfoType;

    private ComponentTypeHandle<GameEntityActionInfo> __actionInfoType;
    private BufferTypeHandle<GameAnimatorDoInfo> __animatorInfoType;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __animationElapsedTimeGroup = GameAnimationElapsedTime.GetEntityQuery(ref state);

        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __groupToDelay = builder
                    .WithAll<GameNodeDelay, GameAnimatorDelay>()
                    .WithAllRW<GameAnimatorDelayInfo>()
                    .WithNone<GameNodeParent>()
                    .Build(ref state);

        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __groupToDesiredChange = builder
                .WithAll<GameNodeDesiredStatus, GameAnimatorDesiredStatus>()
                .WithAllRW<GameAnimatorDesiredStatusInfo>()
                .WithNone<GameNodeParent>()
                .Build(ref state);

        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __groupToActorChange = builder
                .WithAll<GameNodeActorStatus, GameAnimatorActorStatus>()
                .WithAllRW<GameAnimatorActorStatusInfo>()
                .WithNone<GameNodeParent>()
                .Build(ref state);

        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __groupToBreak = builder
                .WithAll<GameAnimatorActorTimeToLive, GameNodeDelay, GameEntityBreakInfo, GameEntityActorInfo>()
                .WithAllRW<GameAnimatorBreakInfo>()
                .WithNone<GameNodeParent>()
                .Build(ref state);
        __groupToBreak.SetChangedVersionFilter(ComponentType.ReadOnly<GameEntityBreakInfo>());

        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __groupToDo = builder
                .WithAll<GameAnimatorActorTimeToLive, GameEntityActionInfo, GameEntityActorInfo>()
                .WithAllRW<GameAnimatorDoInfo>()
                .WithNone<GameNodeParent>()
                .Build(ref state);
        __groupToDo.SetChangedVersionFilter(ComponentType.ReadOnly<GameEntityActionInfo>());

        __nodeDelayType = state.GetComponentTypeHandle<GameNodeDelay>(true);
        __animatorDelayType = state.GetComponentTypeHandle<GameAnimatorDelay>(true);
        __delayInfoType = state.GetBufferTypeHandle<GameAnimatorDelayInfo>();

        __nodeDesiredStatusType = state.GetComponentTypeHandle<GameNodeDesiredStatus>(true);
        __animatorDesiredStatusType = state.GetComponentTypeHandle<GameAnimatorDesiredStatus>(true);
        __animatorDesiredStatusInfoType = state.GetBufferTypeHandle<GameAnimatorDesiredStatusInfo>();
        __transformKeyframeType = state.GetBufferTypeHandle<GameTransformKeyframe<GameTransform>>();
        __animationKeyframeType = state.GetBufferTypeHandle<GameTransformKeyframe<GameAnimatorTransform>>();

        __nodeActorStatusType = state.GetComponentTypeHandle<GameNodeActorStatus>(true);
        __animatorActorStatusType = state.GetComponentTypeHandle<GameAnimatorActorStatus>(true);
        __animatorActorStatusInfoType = state.GetBufferTypeHandle<GameAnimatorActorStatusInfo>();

        __animatorActorTimeToLiveType = state.GetComponentTypeHandle<GameAnimatorActorTimeToLive>(true);
        __breakInfoType = state.GetComponentTypeHandle<GameEntityBreakInfo>(true);
        __actorInfoType = state.GetComponentTypeHandle<GameEntityActorInfo>(true);
        //__actorTimeType = state.GetComponentTypeHandle<GameEntityActorTime>(true);
        __animatorBreakInfoType = state.GetBufferTypeHandle<GameAnimatorBreakInfo>();

        __actionInfoType = state.GetComponentTypeHandle<GameEntityActionInfo>(true);
        __animatorInfoType = state.GetBufferTypeHandle<GameAnimatorDoInfo>();
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {

    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        JobHandle inputDeps = state.Dependency;
        JobHandle? result = null;

        if (!__groupToDelay.IsEmptyIgnoreFilter)
        {
            DelayEx delay;
            delay.sourceType = __nodeDelayType.UpdateAsRef(ref state);
            delay.destinationType = __animatorDelayType.UpdateAsRef(ref state);
            delay.delayInfoType = __delayInfoType.UpdateAsRef(ref state);

            JobHandle jobHandle = delay.ScheduleParallelByRef(__groupToDelay, inputDeps);

            result = result == null ? jobHandle : JobHandle.CombineDependencies(result.Value, jobHandle);
        }

        if (!__groupToDesiredChange.IsEmptyIgnoreFilter)
        {
            DesiredChangeEx desiredChange;
            desiredChange.sourceType = __nodeDesiredStatusType.UpdateAsRef(ref state);
            desiredChange.destinationType = __animatorDesiredStatusType.UpdateAsRef(ref state);
            desiredChange.statusInfoType = __animatorDesiredStatusInfoType.UpdateAsRef(ref state);
            desiredChange.transformKeyframeType = __transformKeyframeType.UpdateAsRef(ref state);
            desiredChange.animationKeyframeType = __animationKeyframeType.UpdateAsRef(ref state);

            var jobHandle = desiredChange.ScheduleParallelByRef(__groupToDesiredChange, inputDeps);

            result = result == null ? jobHandle : JobHandle.CombineDependencies(result.Value, jobHandle);
        }

        if (!__groupToActorChange.IsEmptyIgnoreFilter)
        {
            ActorChangeEx actorChange;
            actorChange.sourceType = __nodeActorStatusType.UpdateAsRef(ref state);
            actorChange.destinationType = __animatorActorStatusType.UpdateAsRef(ref state);
            actorChange.statusInfoType = __animatorActorStatusInfoType.UpdateAsRef(ref state);

            var jobHandle = actorChange.ScheduleParallelByRef(__groupToActorChange, inputDeps);

            result = result == null ? jobHandle : JobHandle.CombineDependencies(result.Value, jobHandle);
        }

        double time = __animationElapsedTimeGroup.GetSingleton<GameAnimationElapsedTime>().value;
        var animatorActorTimeToLiveType = __animatorActorTimeToLiveType.UpdateAsRef(ref state);
        var actorInfoType = __actorInfoType.UpdateAsRef(ref state);
        if (!__groupToBreak.IsEmptyIgnoreFilter)
        {
            BreakEx @break;
            @break.time = time;
            @break.timeToLiveType = animatorActorTimeToLiveType;
            @break.breakInfoType = __breakInfoType.UpdateAsRef(ref state);
            @break.actorInfoType = actorInfoType;
            //@break.actorTimeType = __actorTimeType.UpdateAsRef(ref state);
            @break.infoType = __animatorBreakInfoType.UpdateAsRef(ref state);

            var jobHandle = @break.ScheduleParallelByRef(__groupToBreak, inputDeps);

            result = result == null ? jobHandle : JobHandle.CombineDependencies(result.Value, jobHandle);
        }

        if (!__groupToDo.IsEmptyIgnoreFilter)
        {
            DoEx @do;
            @do.time = time;
            @do.timeToLiveType = animatorActorTimeToLiveType;
            @do.actionInfoType = __actionInfoType.UpdateAsRef(ref state);
            @do.actorInfoType = actorInfoType;
            @do.infoType = __animatorInfoType.UpdateAsRef(ref state);

            var jobHandle = @do.ScheduleParallelByRef(__groupToDo, inputDeps);

            result = result == null ? jobHandle : JobHandle.CombineDependencies(result.Value, jobHandle);
        }

        if (result != null)
            state.Dependency = result.Value;
    }
}

/*[UpdateAfter(typeof(GameTransformSystem))]
public partial class GameAnimatorSimulationSystem : JobComponentSystem
{
    [BurstCompile]
    private struct Copy : IJobParallelForTransform
    {
        public NativeArray<RigidTransform> transforms;

        public void Execute(int index, TransformAccess transform)
        {
            transforms[index] = math.RigidTransform(transform.rotation, transform.position);
        }
    }
    
    private struct Move
    {
        public float deltaTime;
        
        [ReadOnly]
        public NativeSlice<RigidTransform> transforms;

        [ReadOnly]
        public NativeArray<GameNodeCharacterSurface> surfaces;

        [ReadOnly]
        public NativeArray<GameNodeCharacterDesiredVelocity> characterVelocities;

        [ReadOnly]
        public NativeArray<GameTransformVelocity<GameTransform, GameTransformVelocity>> rigidbodyVelocities;

        [ReadOnly]
        public NativeArray<GameAnimatorMoveData> instances;

        [ReadOnly]
        public BufferAccessor<GameAnimatorForwardKeyframe> forwardKeyframes;

        [ReadOnly]
        public BufferAccessor<GameAnimatorTurnKeyframe> turnKeyframes;
        
        public NativeArray<GameAnimatorMoveInfo> moveInfos;

        public float Max(float x, float y)
        {
            if (math.abs(x) < math.abs(y))
                return y;

            return x;
        }

        public void Execute(int index)
        {
            GameAnimatorMoveData instance = instances[index];
            GameAnimatorMoveInfo moveInfo = moveInfos[index];

            var transform = transforms[index];

            //float3 forward = math.forward(transform.rot);
            float3 normal = surfaces[index].normal, forward = math.normalizesafe(math.cross(math.mul(transform.rot, math.float3(1.0f, 0.0f, 0.0f)), normal));
            var characterVelocity = characterVelocities[index];
            //var rigidbodyVelocity = rigidbodyVelocities[index];
            float forwardAmout = math.dot(characterVelocity.linear, forward);// Max(math.dot(characterVelocity.linear, forward), math.dot(rigidbodyVelocity.linear, forward));
            var forwardKeyframes = this.forwardKeyframes[index].Reinterpret<float>().AsNativeArray().Slice();
            int level = forwardKeyframes.BinarySearch(forwardAmout);
            float minValue;
            if (level < 0)
                minValue = forwardKeyframes[++level];
            else
            {
                minValue = forwardKeyframes[level];
                if (minValue == forwardAmout || level == forwardKeyframes.Length - 1)
                    --level;
            }

            forwardAmout = math.smoothstep(minValue, forwardKeyframes[level + 1], forwardAmout);
            forwardAmout += level - forwardKeyframes.BinarySearch(0.0f);

            moveInfo.forwardAmount = math.lerp(
                moveInfo.forwardAmount,
                forwardAmout, 
                math.min((forwardAmout > 0.0f ? instance.forwardInterpolationSpeed : instance.backwardInterpolationSpeed) * deltaTime, 1.0f));

            //var node = nodes[index];
            float turnAmount = math.rotate(math.inverse(transform.rot), characterVelocity.angular).y;
            var turnKeyframes = this.turnKeyframes[index].Reinterpret<float>().AsNativeArray().Slice();
            level = turnKeyframes.BinarySearch(turnAmount);
            if (level < 0)
                minValue = turnKeyframes[++level];
            else
            {
                minValue = turnKeyframes[level];
                if (minValue == turnAmount || level == turnKeyframes.Length - 1)
                    --level;
            }

            turnAmount = math.smoothstep(minValue, turnKeyframes[level + 1], turnAmount);
            turnAmount += level - turnKeyframes.BinarySearch(0.0f);
            moveInfo.turnAmount = math.lerp(moveInfo.turnAmount, turnAmount, math.min(instance.turnInterpolationSpeed * deltaTime, 1.0f));

            moveInfos[index] = moveInfo;
        }
    }

    [BurstCompile]
    private struct MoveEx : IJobChunk
    {
        public float deltaTime;

        [ReadOnly, DeallocateOnJobCompletion]
        public NativeArray<RigidTransform> transforms;

        [ReadOnly]
        public ArchetypeChunkComponentType<GameNodeCharacterSurface> surfaceType;

        [ReadOnly]
        public ArchetypeChunkComponentType<GameNodeCharacterDesiredVelocity> characterVelocityType;

        [ReadOnly]
        public ArchetypeChunkComponentType<GameTransformVelocity<GameTransform, GameTransformVelocity>> rigidbodyVelocityType;

        [ReadOnly]
        public ArchetypeChunkComponentType<GameAnimatorMoveData> instanceType;

        [ReadOnly]
        public ArchetypeChunkBufferType<GameAnimatorForwardKeyframe> forwardKeyframeType;

        [ReadOnly]
        public ArchetypeChunkBufferType<GameAnimatorTurnKeyframe> turnKeyframeType;
        
        public ArchetypeChunkComponentType<GameAnimatorMoveInfo> moveInfoType;

        public void Execute(ArchetypeChunk chunk, int chunkIndex, int firstEntityIndex)
        {
            int count = chunk.Count;

            Move move;
            move.deltaTime = deltaTime;
            move.transforms = transforms.Slice(firstEntityIndex, count);
            move.surfaces = chunk.GetNativeArray(surfaceType);
            move.characterVelocities = chunk.GetNativeArray(characterVelocityType);
            move.rigidbodyVelocities = chunk.GetNativeArray(rigidbodyVelocityType);
            move.instances = chunk.GetNativeArray(instanceType);
            move.forwardKeyframes = chunk.GetBufferAccessor(forwardKeyframeType);
            move.turnKeyframes = chunk.GetBufferAccessor(turnKeyframeType);
            move.moveInfos = chunk.GetNativeArray(moveInfoType);
            
            for (int i = 0; i < count; ++i)
                move.Execute(i);
        }
    }

    private struct Drop
    {
        public float smoothTime;
        public float deltaTime;

        [ReadOnly]
        public NativeArray<GameNodeCharacterData> characters;
        [ReadOnly]
        public NativeArray<GameNodeCharacterStatus> states;
        [ReadOnly]
        public NativeArray<GameNodeCharacterSurface> surfaces;

        public NativeArray<GameAnimatorHeightInfo> heightInfos;

        public void Execute(int index)
        {
            float amount;
            var heightInfo = heightInfos[index];
            var status = states[index];
            if (status.area == GameNodeCharacterStatus.Area.Normal)
            {
                var character = characters[index];
                amount = (math.max(surfaces[index].fraction, character.stepFraction) - character.stepFraction) / (1.0f - character.stepFraction);//surfaces[index].fraction;

                heightInfo.max = math.max(heightInfo.max, amount);

                amount /= heightInfo.max;
            }
            else
            {
                heightInfo.max = 0.0f;

                amount = 0.0f;
            }

            heightInfo.amount = ZG.Mathematics.Math.SmoothDamp(
                heightInfo.amount,
                amount, 
                ref heightInfo.velocity,
                smoothTime, 
                float.MaxValue,
                deltaTime);

            //Debug.Log($"{heightInfo.amount}");
            heightInfos[index] = heightInfo;
        }
    }

    [BurstCompile]
    private struct DropEx : IJobChunk
    {
        public float smoothTime;
        public float deltaTime;

        [ReadOnly]
        public ArchetypeChunkComponentType<GameNodeCharacterData> characterType;
        [ReadOnly]
        public ArchetypeChunkComponentType<GameNodeCharacterStatus> statusType;
        [ReadOnly]
        public ArchetypeChunkComponentType<GameNodeCharacterSurface> surfaceType;

        public ArchetypeChunkComponentType<GameAnimatorHeightInfo> heightInfoType;

        public void Execute(ArchetypeChunk chunk, int chunkIndex, int firstEntityIndex)
        {
            Drop drop;
            drop.smoothTime = smoothTime;
            drop.deltaTime = deltaTime;
            drop.characters = chunk.GetNativeArray(characterType);
            drop.states = chunk.GetNativeArray(statusType);
            drop.surfaces = chunk.GetNativeArray(surfaceType);
            drop.heightInfos = chunk.GetNativeArray(heightInfoType);

            int count = chunk.Count;
            for (int i = 0; i < count; ++i)
                drop.Execute(i);
        }
    }

    private EntityQuery __groupToDrop;
    private EntityQuery __groupToMove;
    private TransformAccessArrayEx __transformAccessArray;
    private GameUpdateSystemGroup __updateSystemGroup;
    
    protected override void OnCreate()
    {
        base.OnCreate();

        __groupToDrop = GetEntityQuery(
            ComponentType.ReadOnly<GameNodeCharacterData>(),
            ComponentType.ReadOnly<GameNodeCharacterStatus>(),
            ComponentType.ReadOnly<GameNodeCharacterSurface>(),
            ComponentType.ReadWrite<GameAnimatorHeightInfo>(),
            ComponentType.Exclude<GameNodeParent>());

        __groupToMove = GetEntityQuery(
            ComponentType.ReadOnly<GameNodeCharacterSurface>(), 
            ComponentType.ReadOnly<GameNodeCharacterDesiredVelocity>(),
            ComponentType.ReadOnly<GameTransformVelocity<GameTransform, GameTransformVelocity>>(),
            ComponentType.ReadOnly<GameAnimatorMoveData>(),
            ComponentType.ReadOnly<GameAnimatorForwardKeyframe>(),
            ComponentType.ReadOnly<GameAnimatorTurnKeyframe>(),
            ComponentType.ReadWrite<GameAnimatorMoveInfo>(),
            TransformAccessArrayEx.componentType,
            ComponentType.Exclude<GameNodeParent>());
        
        __transformAccessArray = new TransformAccessArrayEx(__groupToMove);

        __updateSystemGroup = World.GetOrCreateSystem<GameUpdateSystemGroup>();
    }

    protected override void OnDestroy()
    {
        __transformAccessArray.Dispose();

        base.OnDestroy();
    }

    protected override JobHandle OnUpdate(JobHandle inputDeps)
    {
        TransformAccessArray transformAccessArray = __transformAccessArray.Convert(this);

        var transforms = new NativeArray<RigidTransform>(transformAccessArray.length, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

        Copy copy;
        copy.transforms = transforms;
        inputDeps = copy.Schedule(transformAccessArray, inputDeps);
        
        float deltaTime = Time.DeltaTime;

        MoveEx move;
        move.deltaTime = deltaTime;
        move.transforms = transforms;
        move.surfaceType = GetArchetypeChunkComponentType<GameNodeCharacterSurface>(true);
        move.characterVelocityType = GetArchetypeChunkComponentType<GameNodeCharacterDesiredVelocity>(true);
        move.rigidbodyVelocityType = GetArchetypeChunkComponentType<GameTransformVelocity<GameTransform, GameTransformVelocity>>(true);
        move.instanceType = GetArchetypeChunkComponentType<GameAnimatorMoveData>(true);
        move.forwardKeyframeType = GetArchetypeChunkBufferType<GameAnimatorForwardKeyframe>(true);
        move.turnKeyframeType = GetArchetypeChunkBufferType<GameAnimatorTurnKeyframe>(true);
        move.moveInfoType = GetArchetypeChunkComponentType<GameAnimatorMoveInfo>();

        JobHandle jobHandle = move.Schedule(__groupToMove, inputDeps);

        float delta = __updateSystemGroup.updateDelta;

        DropEx drop;
        drop.smoothTime = delta;
        drop.deltaTime = deltaTime;
        drop.characterType = GetArchetypeChunkComponentType<GameNodeCharacterData>(true);
        drop.statusType = GetArchetypeChunkComponentType<GameNodeCharacterStatus>(true);
        drop.surfaceType = GetArchetypeChunkComponentType<GameNodeCharacterSurface>(true);
        drop.heightInfoType = GetArchetypeChunkComponentType<GameAnimatorHeightInfo>();

        return JobHandle.CombineDependencies(jobHandle, drop.Schedule(__groupToDrop, inputDeps));
    }
}*/

//[UpdateInGroup(typeof(TimeSystemGroup)), UpdateAfter/*UpdateBefore*/(typeof(GameSyncSystemGroup))]
//[UpdateAfter(typeof(GameAnimatorTransformSystem))]
[BurstCompile, UpdateBefore(typeof(AnimatorControllerSystem))]
public partial struct GameAnimatorSystem : ISystem
{
    private struct ApplyTime
    {
        public double time;

        [ReadOnly]
        public NativeArray<GameAnimatorParameterData> parameters;

        public NativeArray<GameAnimatorDelay> delay;
        public NativeArray<GameAnimatorDesiredStatus> desiredStates;
        public NativeArray<GameAnimatorActorStatus> actorStates;

        //public NativeArray<GameTransformVelocity<GameTransform, GameTransformVelocity>> velocities;

        public BufferAccessor<GameAnimatorDelayInfo> delayInfos;
        public BufferAccessor<GameAnimatorDesiredStatusInfo> desiredStatusInfos;
        public BufferAccessor<GameAnimatorActorStatusInfo> actorStatusInfos;

        public BufferAccessor<MeshInstanceAnimatorParameterCommand> paramterCommands;

        [ReadOnly]
        public NativeArray<EntityObject<Animator>> animators;

        [NativeDisableContainerSafetyRestriction]
        public EntityCommandQueue<GameAnimatorApplySystem.Result>.ParallelWriter results;

        public bool Execute(int index)
        {
            DynamicBuffer<MeshInstanceAnimatorParameterCommand> paramterCommands = default;

            //Entity entity = entityArray[index];
            var parameter = parameters[index];
            var delayInfos = this.delayInfos[index];
            int length = delayInfos.Length;
            if (length > 0)
            {
                bool isDelay = false, isChanged = false;
                GameAnimatorDelayInfo delayInfo = delayInfos[0], oldDelayInfo = default;
                while (delayInfo.startTime <= time)
                {
                    if (delayInfo.endTime > time)
                    {
                        isDelay = true;

                        break;
                    }
                    else
                    {
                        isChanged = true;

                        oldDelayInfo = delayInfo;

                        delayInfos.RemoveAt(0);

                        if (--length > 0)
                            delayInfo = delayInfos[0];
                        else
                            break;
                    }
                }

                if (parameter.busyID != 0)
                {
                    if (index < this.delay.Length)
                    {
                        //Debug.LogError($"{isDelay} : {delayInfo.startTime} : {delayInfo.endTime} : {time}");

                        var delay = this.delay[index];

                        if (isChanged)
                        {
                            delay.startTime = oldDelayInfo.startTime;
                            delay.endTime = oldDelayInfo.endTime;
                        }

                        isChanged = (delay.status == GameAnimatorDelay.Status.Busy) != isDelay;
                        delay.status = isDelay ? GameAnimatorDelay.Status.Busy : GameAnimatorDelay.Status.Normal;

                        this.delay[index] = delay;
                    }
                    else
                        isChanged |= isDelay;

                    if (isChanged)
                    {
                        //Debug.LogError($"{isDelay} : {delayInfo.startTime} : {delayInfo.endTime} : {time}");
                        /*if (animator.GetBool(GameAnimatorFlag.triggerHashBusy) != isBusy)
                            Debug.Log(animator.transform.root.name + " : " + isBusy + time);*/

                        if (index < this.paramterCommands.Length)
                        {
                            if (!paramterCommands.IsCreated)
                                paramterCommands = this.paramterCommands[index];

                            MeshInstanceAnimatorComponent.SetBool(parameter.busyID, isDelay, ref paramterCommands);
                        }
                        else if (index < animators.Length)
                        {
                            GameAnimatorApplySystem.Result result;
                            result.animator = animators[index];
                            result.type = GameAnimatorApplySystem.ResultType.Busy;
                            result.id = parameter.busyID;
                            result.value = isDelay ? 1.0f : 0.0f;
                            results.Enqueue(result);
                        }
                    }
                }
            }

            var desiredStatusInfos = this.desiredStatusInfos[index];
            if (desiredStatusInfos.Length > 0)
            {
                var statusInfo = desiredStatusInfos[0];
                if (statusInfo.time <= time)
                {
                    desiredStatusInfos.RemoveAt(0);

                    if (parameter.desiredStatusID != 0)
                    {
                        /*if (animator.GetInteger(GameAnimatorFlag.triggerHashMoveStatus) != statusInfo.value)
                            Debug.Log(animator.transform.root.name + " : " + (GameNodeActorStatus.Status)statusInfo.value + " : " + animator.GetBool(GameAnimatorFlag.triggerHashBusy) + time);*/

                        if (statusInfo.value == (int)GameNodeDesiredStatus.Status.Pivoting)
                        {
                            /*if(index < velocities.Length)
                            {
                                var velocity = velocities[index];
                                velocity.value.angular = float.MaxValue;
                                velocities[index] = velocity;
                            }*/

                            /*if (index < sourceTransforms.Length)
                            {
                                var sourceTransform = sourceTransforms[index];
                                sourceTransform.oldValue = sourceTransform.value;
                                sourceTransform.value = destinationTransforms[index].value;
                                sourceTransforms[index] = sourceTransform;
                            }*/
                        }

                        if (index < this.paramterCommands.Length)
                        {
                            if (!paramterCommands.IsCreated)
                                paramterCommands = this.paramterCommands[index];

                            MeshInstanceAnimatorComponent.SetInteger(parameter.desiredStatusID, statusInfo.value, ref paramterCommands);
                        }
                        else if (index < animators.Length)
                        {
                            GameAnimatorApplySystem.Result result;
                            result.animator = animators[index];
                            result.type = GameAnimatorApplySystem.ResultType.DesiredStatus;
                            result.id = parameter.desiredStatusID;
                            result.value = statusInfo.value;
                            results.Enqueue(result);
                        }

                        //animator.SetInteger(GameAnimatorFlag.triggerHashMoveStatus, statusInfo.value);

                        if (index < desiredStates.Length)
                        {
                            //Debug.LogError($"{(GameNodeActorStatus.Status)statusInfo.value} : {time}");

                            GameAnimatorDesiredStatus status;
                            status.value = statusInfo.value;
                            desiredStates[index] = status;
                        }
                    }
                }
            }

            var actorStatusInfos = this.actorStatusInfos[index];
            if (actorStatusInfos.Length > 0)
            {
                var statusInfo = actorStatusInfos[0];
                if (statusInfo.time <= time)
                {
                    actorStatusInfos.RemoveAt(0);

                    if (parameter.moveStatusID != 0)
                    {
                        /*if (animator.GetInteger(GameAnimatorFlag.triggerHashMoveStatus) != statusInfo.value)
                            Debug.Log(animator.transform.root.name + " : " + (GameNodeActorStatus.Status)statusInfo.value + " : " + animator.GetBool(GameAnimatorFlag.triggerHashBusy) + time);*/

                        if (index < this.paramterCommands.Length)
                        {
                            if (!paramterCommands.IsCreated)
                                paramterCommands = this.paramterCommands[index];

                            MeshInstanceAnimatorComponent.SetInteger(parameter.moveStatusID, statusInfo.value, ref paramterCommands);
                        }
                        else if (index < animators.Length)
                        {
                            GameAnimatorApplySystem.Result result;
                            result.animator = animators[index];
                            result.type = GameAnimatorApplySystem.ResultType.ActorStatus;
                            result.id = parameter.moveStatusID;
                            result.value = statusInfo.value;
                            results.Enqueue(result);
                        }

                        //animator.SetInteger(GameAnimatorFlag.triggerHashMoveStatus, statusInfo.value);

                        if (index < actorStates.Length)
                        {
                            //Debug.LogError($"{(GameNodeActorStatus.Status)statusInfo.value} : {time}");

                            GameAnimatorActorStatus status;
                            status.value = statusInfo.value;
                            actorStates[index] = status;
                        }
                    }
                }
            }

            return paramterCommands.IsCreated;
        }
    }

    [BurstCompile]
    private struct ApplyTimeEx : IJobChunk, IEntityCommandProducerJob
    {
        public double time;
        [ReadOnly]
        public ComponentTypeHandle<GameAnimatorParameterData> parameterType;

        public ComponentTypeHandle<GameAnimatorDelay> delayType;
        public ComponentTypeHandle<GameAnimatorDesiredStatus> desiredStatusType;
        public ComponentTypeHandle<GameAnimatorActorStatus> actorStatusType;

        public ComponentTypeHandle<GameTransformVelocity<GameTransform, GameTransformVelocity>> velocityType;

        public BufferTypeHandle<GameAnimatorDelayInfo> delayInfoType;
        public BufferTypeHandle<GameAnimatorDesiredStatusInfo> desiredStatusInfoType;
        public BufferTypeHandle<GameAnimatorActorStatusInfo> actorStatusInfoType;

        //public BufferTypeHandle<GameTransformKeyframe<GameTransform>> transformKeyframeType;

        public BufferTypeHandle<MeshInstanceAnimatorParameterCommand> paramterCommandType;

        [ReadOnly]
        public ComponentTypeHandle<EntityObject<Animator>> animatorType;

        [NativeDisableContainerSafetyRestriction]
        public EntityCommandQueue<GameAnimatorApplySystem.Result>.ParallelWriter results;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            ApplyTime applyTime;
            applyTime.time = time;
            applyTime.parameters = chunk.GetNativeArray(ref parameterType);
            applyTime.delay = chunk.GetNativeArray(ref delayType);
            applyTime.desiredStates = chunk.GetNativeArray(ref desiredStatusType);
            applyTime.actorStates = chunk.GetNativeArray(ref actorStatusType);
            //applyTime.velocities = chunk.GetNativeArray(velocityType);
            applyTime.delayInfos = chunk.GetBufferAccessor(ref delayInfoType);
            applyTime.desiredStatusInfos = chunk.GetBufferAccessor(ref desiredStatusInfoType);
            applyTime.actorStatusInfos = chunk.GetBufferAccessor(ref actorStatusInfoType);
            //applyTime.transformKeyframes = chunk.GetBufferAccessor(transformKeyframeType);
            applyTime.paramterCommands = chunk.GetBufferAccessor(ref paramterCommandType);
            applyTime.animators = chunk.GetNativeArray(ref animatorType);
            applyTime.results = results;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
            {
                if (applyTime.Execute(i))
                    chunk.SetComponentEnabled(ref paramterCommandType, i, true);
            }
        }
    }

    private struct ApplySimulation
    {
        [ReadOnly]
        public NativeArray<GameAnimatorParameterData> parameters;

        [ReadOnly]
        public NativeArray<GameAnimatorTransformInfo> transformInfos;

        [ReadOnly]
        public NativeArray<EntityObject<Animator>> animators;

        public BufferAccessor<MeshInstanceAnimatorParameterCommand> paramterCommands;

        public EntityCommandQueue<GameAnimatorApplySystem.Result>.ParallelWriter results;

        public bool Execute(int index)
        {
            DynamicBuffer<MeshInstanceAnimatorParameterCommand> paramterCommands = default;

            var parameter = parameters[index];
            var transformInfo = transformInfos[index];

            if (parameter.heightID != 0 &&
                (transformInfo.dirtyFlag & GameAnimatorTransformInfo.DirtyFlag.Height) == GameAnimatorTransformInfo.DirtyFlag.Height)
            {
                if (index < this.paramterCommands.Length)
                {
                    if (!paramterCommands.IsCreated)
                        paramterCommands = this.paramterCommands[index];

                    MeshInstanceAnimatorComponent.SetFloat(parameter.heightID, transformInfo.heightAmount, ref paramterCommands);
                }
                else if (index < animators.Length)
                {
                    GameAnimatorApplySystem.Result result;
                    result.animator = animators[index];
                    result.type = GameAnimatorApplySystem.ResultType.Height;
                    result.id = parameter.heightID;
                    result.value = transformInfo.heightAmount;
                    results.Enqueue(result);
                }
            }

            if (parameter.moveID != 0 &&
                (transformInfo.dirtyFlag & GameAnimatorTransformInfo.DirtyFlag.Forward) == GameAnimatorTransformInfo.DirtyFlag.Forward)
            {
                if (index < this.paramterCommands.Length)
                {
                    if (!paramterCommands.IsCreated)
                        paramterCommands = this.paramterCommands[index];

                    MeshInstanceAnimatorComponent.SetFloat(parameter.moveID, transformInfo.forwardAmount, ref paramterCommands);
                }
                else if (index < animators.Length)
                {
                    GameAnimatorApplySystem.Result result;
                    result.animator = animators[index];
                    result.type = GameAnimatorApplySystem.ResultType.Forward;
                    result.id = parameter.moveID;
                    result.value = transformInfo.forwardAmount;
                    results.Enqueue(result);

                    //animator.SetFloat(GameAnimatorFlag.triggerHashMove, transformInfo.forwardAmount);
                }
            }

            if (parameter.turnID != 0 &&
                (transformInfo.dirtyFlag & GameAnimatorTransformInfo.DirtyFlag.Turn) == GameAnimatorTransformInfo.DirtyFlag.Turn)
            {
                if (index < this.paramterCommands.Length)
                {
                    if (!paramterCommands.IsCreated)
                        paramterCommands = this.paramterCommands[index];

                    MeshInstanceAnimatorComponent.SetFloat(parameter.turnID, transformInfo.turnAmount, ref paramterCommands);
                }
                else if (index < animators.Length)
                {
                    GameAnimatorApplySystem.Result result;
                    result.animator = animators[index];
                    result.type = GameAnimatorApplySystem.ResultType.Turn;
                    result.id = parameter.turnID;
                    result.value = transformInfo.turnAmount;
                    results.Enqueue(result);
                    //animator.SetFloat(GameAnimatorFlag.triggerHashTurn, transformInfo.turnAmount);
                }
            }

            return paramterCommands.IsCreated;
        }
    }

    [BurstCompile]
    private struct ApplySimulationEx : IJobChunk
    {
        [ReadOnly]
        public ComponentTypeHandle<GameAnimatorParameterData> parameterType;
        [ReadOnly]
        public ComponentTypeHandle<GameAnimatorTransformInfo> transformInfoType;

        [ReadOnly]
        public ComponentTypeHandle<EntityObject<Animator>> animatorType;

        public BufferTypeHandle<MeshInstanceAnimatorParameterCommand> paramterCommandType;

        public EntityCommandQueue<GameAnimatorApplySystem.Result>.ParallelWriter results;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            ApplySimulation applySimulation;
            applySimulation.parameters = chunk.GetNativeArray(ref parameterType);
            applySimulation.transformInfos = chunk.GetNativeArray(ref transformInfoType);
            applySimulation.paramterCommands = chunk.GetBufferAccessor(ref paramterCommandType);
            applySimulation.animators = chunk.GetNativeArray(ref animatorType);
            applySimulation.results = results;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
            {
                if (applySimulation.Execute(i))
                    chunk.SetComponentEnabled(ref paramterCommandType, i, true);
            }
        }
    }

    private EntityQuery __animationElapsedTimeGroup;
    private EntityQuery __simulationGroup;
    private EntityQuery __timeGroup;

    private BufferTypeHandle<MeshInstanceAnimatorParameterCommand> __paramterCommandType;

    private ComponentTypeHandle<GameAnimatorParameterData> __parameterType;

    private ComponentTypeHandle<GameAnimatorTransformInfo> __transformInfoType;

    private ComponentTypeHandle<GameAnimatorDelay> __delayType;
    private ComponentTypeHandle<GameAnimatorDesiredStatus> __desiredStatusType;
    private ComponentTypeHandle<GameAnimatorActorStatus> __actorStatusType;

    private ComponentTypeHandle<GameTransformVelocity<GameTransform, GameTransformVelocity>> __velocityType;

    private BufferTypeHandle<GameAnimatorDelayInfo> __delayInfoType;
    private BufferTypeHandle<GameAnimatorDesiredStatusInfo> __desiredStatusInfoType;
    private BufferTypeHandle<GameAnimatorActorStatusInfo> __actorStatusInfoType;

    private ComponentTypeHandle<EntityObject<Animator>> __animatorType;

    private EntityCommandPool<GameAnimatorApplySystem.Result> __results;

    public void OnCreate(ref SystemState state)
    {
        __animationElapsedTimeGroup = GameAnimationElapsedTime.GetEntityQuery(ref state);

        __simulationGroup = state.GetEntityQuery(
            ComponentType.ReadOnly<GameAnimatorParameterData>(),
            ComponentType.ReadOnly<GameAnimatorTransformInfo>(),
            ComponentType.Exclude<GameNodeParent>());

        __timeGroup = state.GetEntityQuery(
            ComponentType.ReadOnly<GameAnimatorParameterData>(),
            ComponentType.ReadWrite<GameAnimatorDelayInfo>(),
            ComponentType.Exclude<GameNodeParent>());

        __paramterCommandType = state.GetBufferTypeHandle<MeshInstanceAnimatorParameterCommand>();
        __parameterType = state.GetComponentTypeHandle<GameAnimatorParameterData>(true);
        __transformInfoType = state.GetComponentTypeHandle<GameAnimatorTransformInfo>(true);
        __delayType = state.GetComponentTypeHandle<GameAnimatorDelay>();
        __desiredStatusType = state.GetComponentTypeHandle<GameAnimatorDesiredStatus>();
        __actorStatusType = state.GetComponentTypeHandle<GameAnimatorActorStatus>();

        __velocityType = state.GetComponentTypeHandle<GameTransformVelocity<GameTransform, GameTransformVelocity>>();

        __delayInfoType = state.GetBufferTypeHandle<GameAnimatorDelayInfo>();
        __desiredStatusInfoType = state.GetBufferTypeHandle<GameAnimatorDesiredStatusInfo>();
        __actorStatusInfoType = state.GetBufferTypeHandle<GameAnimatorActorStatusInfo>();

        __animatorType = state.GetComponentTypeHandle<EntityObject<Animator>>(true);

        __results = state.World.GetOrCreateSystemManaged<GameAnimatorApplySystem>().pool;
    }

    public void OnDestroy(ref SystemState state)
    {
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var inputDeps = state.Dependency;

        var paramterCommandType = __paramterCommandType.UpdateAsRef(ref state);
        var parameterType = __parameterType.UpdateAsRef(ref state);
        var animatorType = __animatorType.UpdateAsRef(ref state);

        var results = __results.Create();

        var resultParallelWriter = results.parallelWriter;

        ApplySimulationEx applySimulation;
        applySimulation.parameterType = parameterType;
        applySimulation.transformInfoType = __transformInfoType.UpdateAsRef(ref state);
        applySimulation.paramterCommandType = paramterCommandType;
        applySimulation.animatorType = animatorType;
        applySimulation.results = resultParallelWriter;

        var jobHandle = applySimulation.ScheduleParallelByRef(__simulationGroup, inputDeps);

        ApplyTimeEx applyTime;
        applyTime.time = __animationElapsedTimeGroup.GetSingleton<GameAnimationElapsedTime>().value;// + Time.DeltaTime;
        applyTime.parameterType = parameterType;
        applyTime.delayType = __delayType.UpdateAsRef(ref state);
        applyTime.desiredStatusType = __desiredStatusType.UpdateAsRef(ref state);
        applyTime.actorStatusType = __actorStatusType.UpdateAsRef(ref state);
        applyTime.velocityType = __velocityType.UpdateAsRef(ref state);
        applyTime.delayInfoType = __delayInfoType.UpdateAsRef(ref state);
        applyTime.desiredStatusInfoType = __desiredStatusInfoType.UpdateAsRef(ref state);
        applyTime.actorStatusInfoType = __actorStatusInfoType.UpdateAsRef(ref state);
        //applyTime.transformKeyframeType = GetBufferTypeHandle<GameTransformKeyframe<GameTransform>>();
        applyTime.paramterCommandType = paramterCommandType;
        applyTime.animatorType = animatorType;
        applyTime.results = resultParallelWriter;

        jobHandle = applyTime.ScheduleParallelByRef(__timeGroup, jobHandle);

        results.AddJobHandleForProducer<ApplyTimeEx>(jobHandle);

        state.Dependency = jobHandle;
    }
}

[UpdateBefore(typeof(GameAnimatorSystem))]
public partial class GameAnimatorApplySystem : SystemBase
{
    public enum ResultType
    {
        Busy,
        DesiredStatus,
        ActorStatus,
        Height,
        Forward,
        Turn, 
        Other
    }

    public struct Result
    {
        public EntityObject<Animator> animator;

        public ResultType type;
        public int id;
        public float value;

        public void Apply()
        {
            var instance = animator.value;
            if (instance == null)
                return;

            switch (type)
            {
                case ResultType.Busy:
                    instance.SetBool(id, Mathf.Abs(value) > 0.0f);
                    break;
                case ResultType.DesiredStatus:
                case ResultType.ActorStatus:
                    instance.SetInteger(id, Mathf.RoundToInt(value));
                    break;
                case ResultType.Height:
                case ResultType.Forward:
                case ResultType.Turn:
                    instance.SetFloat(id, value);
                    break;
            }
        }
    }

    private EntityCommandPool<Result>.Context __context;

    public EntityCommandPool<Result> pool => __context.pool;

    protected override void OnCreate()
    {
        base.OnCreate();

        __context = new EntityCommandPool<Result>.Context(Allocator.Persistent);
    }

    protected override void OnDestroy()
    {
        __context.Dispose();

        base.OnDestroy();
    }

    protected override void OnUpdate()
    {
        while (__context.TryDequeue(out var result))
            result.Apply();
    }
}
