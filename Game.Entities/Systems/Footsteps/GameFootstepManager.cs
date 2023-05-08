using Unity.Jobs;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Animation;
using UnityEngine.ParticleSystemJobs;
using ZG;
using ZG.Mathematics;

public struct GameFootstepTag
{
    public float3 position;
    public quaternion rotation;
    public float scale;
}

public interface IGameFootstepTagManager
{
    bool ScheduleParallel(in NativeArray<GameFootstepTag> tags, int offset, int count, int index, ref JobHandle jobHandle);
}

public struct GameFootstepDefinition
{
    public struct Tag
    {
        //Event state or particle index
        public int state;

        public uint layerMask;

        public float scale;

        public float scalePerSpeed;
        public float countPerSpeed;

        public float minSpeed;
        public float maxSpeed;

        public StringHash speedParamter;

        public StringHash eventType;

        public bool Check(
            uint layerMask, 
            float speed, 
            in DynamicBuffer<AnimatorControllerParameter> parameterValues, 
            ref BlobArray<AnimatorControllerDefinition.Parameter> parameterKeys)
        {
            if ((this.layerMask & layerMask) == 0)
                return false;

            float targetSpeed = speed;
            if (!StringHash.IsNullOrEmpty(speedParamter))
            {
                int paramterIndex = AnimatorControllerDefinition.Parameter.IndexOf(speedParamter, ref parameterKeys);
                if (paramterIndex != -1)
                    targetSpeed = parameterKeys[paramterIndex].GetFloat(paramterIndex, parameterValues);
            }

            return minSpeed <= targetSpeed && (minSpeed >= maxSpeed || maxSpeed > targetSpeed);
        }
    }

    public struct Foot
    {
        public int boneIndex;

        //public float minSpeed;

        public float minPlaneHeight;
        public float maxPlaneHeight;

        public BlobArray<Tag> tags;

        public bool Check(in float3 normal, in float3 localPosition)
        {
            return Math.PlaneDistanceToPoint(math.float4(normal, -math.min(minPlaneHeight, maxPlaneHeight)), localPosition) < 0.0f;
        }
    }

    public struct Rig
    {
        public int index;
        public BlobArray<Foot> foots;
    }

    public BlobArray<Rig> rigs;
}

public struct GameFootstepData : IComponentData
{
    public BlobAssetReference<GameFootstepDefinition> definition;
}

public struct GameFootstep : IBufferElementData
{
    public enum Type
    {
        Triggered,
        Normal
    }

    public Type type;
    public float3 localPosition;
}

public struct GameFootstepManager
{
    private enum JobHandleType
    {
        Count, 
        Apply, 
        Dependency, 
        All
    }

    [BurstCompile]
    private struct InitTags : IJob
    {
        [ReadOnly]
        public NativeArray<int> counts;

        public NativeArray<int> offsets;

        public NativeList<GameFootstepTag> results;

        public void Execute()
        {
            offsets[0] = 0;

            int count = counts.Length, preCount = counts[0];
            for (int i = 1; i < count; ++i)
            {
                offsets[i] = preCount;

                preCount += counts[i];
            }

            results.ResizeUninitialized(preCount);
        }
    }

    private NativeArray<JobHandle> __jobHandles;

    private NativeList<int> __tagCounts;

    private NativeList<GameFootstepTag> __tagResults;

    public bool isVail => __tagCounts.IsCreated && __tagCounts.Length > 0;

    public JobHandle countJobHandle
    {
        get => __jobHandles[(int)JobHandleType.Count];

        set => __jobHandles[(int)JobHandleType.Count] = value;
    }

    public JobHandle applyJobHandle
    {
        get => __jobHandles[(int)JobHandleType.Apply];

        set => __jobHandles[(int)JobHandleType.Apply] = value;
    }

    public JobHandle dependency
    {
        get => __jobHandles[(int)JobHandleType.Dependency];

        set => __jobHandles[(int)JobHandleType.Dependency] = value;
    }

    public NativeArray<int> tagCounts => __tagCounts.AsArray();

    public NativeArray<GameFootstepTag> tagResults => __tagResults.AsDeferredJobArray();

    public GameFootstepManager(Allocator allocator)
    {
        BurstUtility.InitializeJob<InitTags>();

        __jobHandles = new NativeArrayLite<JobHandle>((int)JobHandleType.All, allocator);
        __tagCounts = new NativeList<int>(allocator);
        __tagResults = new NativeList<GameFootstepTag>(allocator);
    }

    public void Dispose()
    {
        __jobHandles.Dispose();
        __tagCounts.Dispose();
        __tagResults.Dispose();
    }

    public NativeArray<int> GetTagCounts()
    {
        countJobHandle.Complete();
        countJobHandle = default;

        return __tagCounts.AsArray();
    }

    public NativeArray<GameFootstepTag> GetTagResults()
    {
        applyJobHandle.Complete();
        applyJobHandle = default;

        return __tagResults.AsArray();
    }

    public void Reset(int tagCount)
    {
        countJobHandle.Complete();
        countJobHandle = default;

        __tagCounts.Resize(tagCount, NativeArrayOptions.ClearMemory);
    }

    public JobHandle Init(NativeArray<int> offsets, in JobHandle inputDeps)
    {
        InitTags initTags;
        initTags.counts = __tagCounts.AsArray();
        initTags.offsets = offsets;
        initTags.results = __tagResults;

        var jobHandle = initTags.Schedule(inputDeps);

        countJobHandle = jobHandle;

        return jobHandle;
    }

    public bool Apply<T>(T manager, out JobHandle jobHandle) where T : IGameFootstepTagManager
    {
        countJobHandle.Complete();
        countJobHandle = default;

        dependency.Complete();
        dependency = default;

        int numTagCounts = __tagCounts.Length;
        {
            int count, offset = 0;
            JobHandle temp;
            NativeList<JobHandle> jobHandles = default;
            var tags = __tagResults.AsDeferredJobArray();
            for (int i = 0; i < numTagCounts; ++i)
            {
                count = __tagCounts[i];
                if (count > 0)
                {
                    temp = applyJobHandle;
                    if (manager.ScheduleParallel(tags, offset, count, i, ref temp))
                    {
                        if (!jobHandles.IsCreated)
                            jobHandles = new NativeList<JobHandle>(numTagCounts, Allocator.Temp);

                        jobHandles.Add(temp);
                    }

                    offset += count;

                    __tagCounts[i] = 0;
                }
            }

            if (jobHandles.IsCreated)
            {
                jobHandle = JobHandle.CombineDependencies(jobHandles.AsArray());

                jobHandles.Dispose();

                dependency = jobHandle;

                return true;
            }
        }

        jobHandle = default;

        return false;
    }
}

[BurstCompile, UpdateAfter(typeof(AnimationSystemGroup))]
public partial struct GameFootstepSystem : ISystem
{
    private struct CountTags
    {
        public float deltaTime;

        [ReadOnly]
        public ComponentLookup<LocalToWorld> localToWorlds;

        [ReadOnly]
        public BufferLookup<AnimatedLocalToWorld> animatedLocalToWorlds;

        [ReadOnly]
        public BufferLookup<AnimatorControllerParameter> animatorControllerParameters;

        [ReadOnly]
        public ComponentLookup<AnimatorControllerData> animatorControllers;

        [ReadOnly]
        public NativeArray<GameFootstepData> instances;

        [ReadOnly]
        public NativeArray<GameNodeCharacterSurface> surfaces;

        [ReadOnly]
        public BufferAccessor<MeshInstanceRig> rigs;

        [ReadOnly]
        public BufferAccessor<GameFootstep> footsteps;

        [NativeDisableParallelForRestriction]
        public BufferLookup<AnimatorControllerEvent> results;

        [NativeDisableParallelForRestriction]
        public NativeArray<int> tagCounts;

        public void Execute(int index)
        {
            if (!GetSurface(index, surfaces, out uint layerMask, out var normal, out _))
                return;

            GameFootstep footstep;
            var footsteps = this.footsteps[index];
            var rigs = this.rigs[index];
            DynamicBuffer<AnimatedLocalToWorld> animatedLocalToWorlds;
            DynamicBuffer<AnimatorControllerEvent> results;
            DynamicBuffer<AnimatorControllerParameter> animatorControllerParameters;
            AnimatorControllerEvent result;
            ref var definition = ref instances[index].definition.Value;
            Entity entity;
            float4x4 worldToRoot, matrix;
            float3 worldPosition, localPosition, up;
            float speed;
            int i, j, k, tagCount, numTags, numFoots, numRigs = definition.rigs.Length, numFootsteps = footsteps.Length, footstepIndex = 0;
            for (i = 0; i < numRigs; ++i)
            {
                ref var rig = ref definition.rigs[i];

                entity = rigs[rig.index].entity;

                worldToRoot = math.inverse(localToWorlds[entity].Value);

                ref var animatorControllerDefinition = ref animatorControllers[entity].definition.Value;

                animatedLocalToWorlds = default;
                results = default;
                animatorControllerParameters = default;

                numFoots = rig.foots.Length;
                for (j = 0; j < numFoots; ++j)
                {
                    ref var foot = ref rig.foots[j];

                    if (footstepIndex < numFootsteps)
                    {
                        footstep = footsteps[footstepIndex];
                        if (footstep.type == GameFootstep.Type.Normal)
                        {
                            if (!animatedLocalToWorlds.IsCreated)
                                animatedLocalToWorlds = this.animatedLocalToWorlds[entity];

                            matrix = animatedLocalToWorlds[foot.boneIndex].Value;
                            worldPosition = matrix.c3.xyz;
                            localPosition = math.transform(worldToRoot, worldPosition);

                            up = footstep.localPosition - localPosition;
                            speed = math.dot(up, normal);
                            if (speed > math.FLT_MIN_NORMAL &&
                                foot.Check(normal, localPosition))
                            {
                                speed /= deltaTime;
                                numTags = foot.tags.Length;
                                for (k = 0; k < numTags; ++k)
                                {
                                    ref var tag = ref foot.tags[k];

                                    if (!animatorControllerParameters.IsCreated)
                                        animatorControllerParameters = this.animatorControllerParameters[entity];

                                    if (tag.Check(layerMask, speed, animatorControllerParameters, ref animatorControllerDefinition.parameters))
                                    {
                                        if (StringHash.IsNullOrEmpty(tag.eventType))
                                        {
                                            tagCount = math.max(1, (int)math.round(tag.countPerSpeed * speed));
                                            if (tagCount > 1)
                                                tagCounts.Add(tag.state, tagCount);
                                            else
                                                tagCounts.Increment(tag.state);
                                        }
                                        else
                                        {
                                            if (!results.IsCreated)
                                                results = this.results[entity];

                                            result.state = tag.state;
                                            result.type = tag.eventType;
                                            result.weight = math.smoothstep(tag.minSpeed, tag.maxSpeed, speed);

                                            results.Add(result);
                                        }
                                    }
                                }
                            }
                        }
                    }
                    else
                        return;

                    ++footstepIndex;
                }
            }
        }
    }

    [BurstCompile]
    private struct CountTagsEx : IJobChunk
    {
        public float deltaTime;

        [ReadOnly]
        public ComponentLookup<LocalToWorld> localToWorlds;

        [ReadOnly]
        public BufferLookup<AnimatedLocalToWorld> animatedLocalToWorlds;

        [ReadOnly]
        public BufferLookup<AnimatorControllerParameter> animatorControllerParameters;

        [ReadOnly]
        public ComponentLookup<AnimatorControllerData> animatorControllers;

        [ReadOnly]
        public ComponentTypeHandle<GameFootstepData> instanceType;

        [ReadOnly]
        public ComponentTypeHandle<GameNodeCharacterSurface> surfaceType;

        [ReadOnly]
        public BufferTypeHandle<MeshInstanceRig> rigType;

        [ReadOnly]
        public BufferTypeHandle<GameFootstep> footstepType;

        [NativeDisableParallelForRestriction]
        public BufferLookup<AnimatorControllerEvent> events;

        [NativeDisableParallelForRestriction]
        public NativeArray<int> tagCounts;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            CountTags countTags;
            countTags.deltaTime = deltaTime;
            countTags.localToWorlds = localToWorlds;
            countTags.animatedLocalToWorlds = animatedLocalToWorlds;
            countTags.animatorControllerParameters = animatorControllerParameters;
            countTags.animatorControllers = animatorControllers;
            countTags.instances = chunk.GetNativeArray(ref instanceType);
            countTags.surfaces = chunk.GetNativeArray(ref surfaceType);
            countTags.rigs = chunk.GetBufferAccessor(ref rigType);
            countTags.footsteps = chunk.GetBufferAccessor(ref footstepType);
            countTags.results = events;
            countTags.tagCounts = tagCounts;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                countTags.Execute(i);
        }
    }

    private struct ApplyTags
    {
        public float deltaTime;

        [ReadOnly]
        public ComponentLookup<LocalToWorld> localToWorlds;

        [ReadOnly]
        public BufferLookup<AnimatedLocalToWorld> animatedLocalToWorlds;

        [ReadOnly]
        public BufferLookup<AnimatorControllerParameter> animatorControllerParameters;

        [ReadOnly]
        public ComponentLookup<AnimatorControllerData> animatorControllers;

        [ReadOnly]
        public NativeArray<GameFootstepData> instances;

        [ReadOnly]
        public NativeArray<GameNodeCharacterSurface> surfaces;

        [ReadOnly]
        public BufferAccessor<MeshInstanceRig> rigs;

        public BufferAccessor<GameFootstep> footsteps;

        [NativeDisableParallelForRestriction, DeallocateOnJobCompletion]
        public NativeArray<int> tagOffsets;

        [NativeDisableParallelForRestriction]
        public NativeArray<GameFootstepTag> tagResults;

        public void Execute(int index)
        {
            GameFootstepTag tagResult;
            if (!GetSurface(index, surfaces, out uint layerMask, out var normal, out tagResult.rotation))
                return;

            ref var definition = ref instances[index].definition.Value;
            int i, numRigs = definition.rigs.Length, footstepCount = 0;
            for (i = 0; i < numRigs; ++i)
            {
                ref var rig = ref definition.rigs[i];

                footstepCount += rig.foots.Length;
            }

            GameFootstep footstep;
            var footsteps = this.footsteps[index];
            var rigs = this.rigs[index];
            DynamicBuffer<AnimatedLocalToWorld> animatedLocalToWorlds;
            DynamicBuffer<AnimatorControllerParameter> animatorControllerParameters;
            Entity entity;
            float4x4 worldToRoot, matrix;
            float3 worldPosition, localPosition, up;
            float speed;
            int j, k, l, tagOffset, tagCount, numTags, numFoots, numFootsteps = footsteps.Length, footstepIndex = 0;
            for (i = 0; i < numRigs; ++i)
            {
                ref var rig = ref definition.rigs[i];

                entity = rigs[rig.index].entity;

                worldToRoot = math.inverse(localToWorlds[entity].Value);

                ref var animatorControllerDefinition = ref animatorControllers[entity].definition.Value;

                animatedLocalToWorlds = default;
                animatorControllerParameters = default;

                numFoots = rig.foots.Length;
                for (j = 0; j < numFoots; ++j)
                {
                    ref var foot = ref rig.foots[j];

                    if (!animatedLocalToWorlds.IsCreated)
                        animatedLocalToWorlds = this.animatedLocalToWorlds[entity];

                    matrix = animatedLocalToWorlds[foot.boneIndex].Value;
                    worldPosition = matrix.c3.xyz;

                    localPosition = math.transform(worldToRoot, worldPosition);

                    if (footstepIndex < numFootsteps)
                    {
                        footstep = footsteps[footstepIndex];

                        if (foot.Check(normal, localPosition))
                        {
                            if (footstep.type == GameFootstep.Type.Normal)
                            {
                                up = footstep.localPosition - localPosition;
                                speed = math.dot(up, normal);
                                if (speed > math.FLT_MIN_NORMAL)
                                {
                                    speed /= deltaTime;

                                    numTags = foot.tags.Length;
                                    for (k = 0; k < numTags; ++k)
                                    {
                                        ref var tag = ref foot.tags[k];

                                        if (StringHash.IsNullOrEmpty(tag.eventType))
                                        {
                                            if (!animatorControllerParameters.IsCreated)
                                                animatorControllerParameters = this.animatorControllerParameters[entity];

                                            if (tag.Check(layerMask, speed, animatorControllerParameters, ref animatorControllerDefinition.parameters))
                                            {
                                                tagResult.scale = 1.0f + tag.scale + (int)math.round(tag.scalePerSpeed * speed);

                                                tagCount = math.max(1, (int)math.round(tag.countPerSpeed * speed));

                                                if (tagCount > 1)
                                                    tagOffset = tagOffsets.Add(tag.state, tagCount) - tagCount;
                                                else
                                                    tagOffset = tagOffsets.Increment(tag.state) - 1;

                                                tagResult.position = worldPosition;

                                                if (tagResults.Length < tagOffset + tagCount)
                                                    Debug.LogError("Tag Index Out Of Range!");

                                                for (l = 0; l < tagCount; ++l)
                                                    tagResults[tagOffset + l] = tagResult;
                                            }
                                        }
                                    }

                                    footstep.type = GameFootstep.Type.Triggered;
                                }
                            }
                        }
                        else if (footstep.type == GameFootstep.Type.Triggered && 
                            Math.PlaneDistanceToPoint(
                                math.float4(normal, -foot.minPlaneHeight), 
                                localPosition) >= 0.0f)
                            footstep.type = GameFootstep.Type.Normal;
                    }
                    else
                    {
                        footsteps.ResizeUninitialized(footstepCount);

                        footstep.type = GameFootstep.Type.Triggered;
                    }

                    footstep.localPosition = localPosition;
                    footsteps[footstepIndex] = footstep;

                    ++footstepIndex;
                }
            }
        }
    }

    [BurstCompile]
    private struct ApplyTagsEx : IJobChunk
    {
        public float deltaTime;

        [ReadOnly]
        public ComponentLookup<LocalToWorld> localToWorlds;

        [ReadOnly]
        public BufferLookup<AnimatedLocalToWorld> animatedLocalToWorlds;

        [ReadOnly]
        public BufferLookup<AnimatorControllerParameter> animatorControllerParameters;

        [ReadOnly]
        public ComponentLookup<AnimatorControllerData> animatorControllers;

        [ReadOnly]
        public ComponentTypeHandle<GameFootstepData> instanceType;

        [ReadOnly]
        public ComponentTypeHandle<GameNodeCharacterSurface> surfaceType;

        [ReadOnly]
        public BufferTypeHandle<MeshInstanceRig> rigType;

        public BufferTypeHandle<GameFootstep> footstepType;

        [NativeDisableParallelForRestriction, DeallocateOnJobCompletion]
        public NativeArray<int> tagOffsets;

        [NativeDisableParallelForRestriction]
        public NativeArray<GameFootstepTag> tagResults;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            ApplyTags applyTags;
            applyTags.deltaTime = deltaTime;
            applyTags.localToWorlds = localToWorlds;
            applyTags.animatedLocalToWorlds = animatedLocalToWorlds;
            applyTags.animatorControllerParameters = animatorControllerParameters;
            applyTags.animatorControllers = animatorControllers;
            applyTags.instances = chunk.GetNativeArray(ref instanceType);
            applyTags.surfaces = chunk.GetNativeArray(ref surfaceType);
            applyTags.rigs = chunk.GetBufferAccessor(ref rigType);
            applyTags.footsteps = chunk.GetBufferAccessor(ref footstepType);
            applyTags.tagOffsets = tagOffsets;
            applyTags.tagResults = tagResults;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                applyTags.Execute(i);
        }
    }

    private EntityQuery __group;
    private ComponentLookup<LocalToWorld> __localToWorlds;
    private BufferLookup<AnimatedLocalToWorld> __animatedLocalToWorlds;
    private BufferLookup<AnimatorControllerParameter> __animatorControllerParameters;
    private ComponentLookup<AnimatorControllerData> __animatorControllers;
    private ComponentTypeHandle<GameFootstepData> __instanceType;
    private ComponentTypeHandle<GameNodeCharacterSurface> __surfaceType;
    private BufferTypeHandle<MeshInstanceRig> __rigType;
    private BufferTypeHandle<GameFootstep> __footstepType;
    private BufferLookup<AnimatorControllerEvent> __events;

    public GameFootstepManager manager
    {
        get;

        private set;
    }

    public static bool GetSurface(
        int index, 
        in NativeArray<GameNodeCharacterSurface> surfaces, 
        out uint layerMask, 
        out float3 normal, 
        out quaternion rotation)
    {
        if (index < surfaces.Length)
        {
            var surface = surfaces[index];
            if (surface.layerMask == 0 || surface.fraction > math.FLT_MIN_NORMAL)
            {
                layerMask = 0;
                normal = float3.zero;
                rotation = quaternion.identity;

                return false;
            }

            layerMask = surface.layerMask;
            normal = math.mul(math.inverse(surface.rotation), surface.normal);
            rotation = surface.rotation;
        }
        else
        {
            layerMask = ~0u;
            normal = math.up();
            rotation = quaternion.identity;
        }

        return true;
    }

    public void OnCreate(ref SystemState systemState)
    {
        __group = systemState.GetEntityQuery(
            ComponentType.ReadOnly<MeshInstanceRig>(),
            //ComponentType.ReadOnly<GameNodeCharacterSurface>(),
            ComponentType.ReadOnly<GameFootstepData>(),
            ComponentType.ReadWrite<GameFootstep>());

        __localToWorlds = systemState.GetComponentLookup<LocalToWorld>(true);
        __animatedLocalToWorlds = systemState.GetBufferLookup<AnimatedLocalToWorld>(true);
        __animatorControllerParameters = systemState.GetBufferLookup<AnimatorControllerParameter>(true);
        __animatorControllers = systemState.GetComponentLookup<AnimatorControllerData>(true);
        __instanceType = systemState.GetComponentTypeHandle<GameFootstepData>(true);
        __surfaceType = systemState.GetComponentTypeHandle<GameNodeCharacterSurface>(true);
        __rigType = systemState.GetBufferTypeHandle<MeshInstanceRig>(true);
        __footstepType = systemState.GetBufferTypeHandle<GameFootstep>();
        __events = systemState.GetBufferLookup<AnimatorControllerEvent>();
        //__tagCounts = new NativeArrayLite<int>(tagCount, Allocator.Persistent, NativeArrayOptions.ClearMemory);
        manager = new GameFootstepManager(Allocator.Persistent);
    }

    public void OnDestroy(ref SystemState systemState)
    {
        manager.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState systemState)
    {
        var manager = this.manager;
        if (!manager.isVail)
            return;

        manager.dependency.Complete();
        manager.dependency = default;

        float deltaTime = systemState.WorldUnmanaged.Time.DeltaTime;

        var tagCounts = manager.tagCounts;
        var tagOffsets = new NativeArray<int>(tagCounts.Length, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

        var localToWorlds = __localToWorlds.UpdateAsRef(ref systemState);
        var animatedLocalToWorlds = __animatedLocalToWorlds.UpdateAsRef(ref systemState);
        var animatorControllerParameters = __animatorControllerParameters.UpdateAsRef(ref systemState);
        var animatorControllers = __animatorControllers.UpdateAsRef(ref systemState);
        var instanceType = __instanceType.UpdateAsRef(ref systemState);
        var surfaceType = __surfaceType.UpdateAsRef(ref systemState);
        var rigType = __rigType.UpdateAsRef(ref systemState);
        var footstepType = __footstepType.UpdateAsRef(ref systemState);

        CountTagsEx countTags;
        countTags.deltaTime = deltaTime;
        countTags.localToWorlds = localToWorlds;
        countTags.animatedLocalToWorlds = animatedLocalToWorlds;
        countTags.animatorControllerParameters = animatorControllerParameters;
        countTags.animatorControllers = animatorControllers;
        countTags.instanceType = instanceType;
        countTags.surfaceType = surfaceType;
        countTags.rigType = rigType;
        countTags.footstepType = footstepType;
        countTags.events = __events.UpdateAsRef(ref systemState);
        countTags.tagCounts = tagCounts;

        var jobHandle = countTags.ScheduleParallel(__group, systemState.Dependency);

        jobHandle = manager.Init(tagOffsets, jobHandle);

        ApplyTagsEx applyTags;
        applyTags.deltaTime = deltaTime;
        applyTags.localToWorlds = __localToWorlds.UpdateAsRef(ref systemState);
        applyTags.localToWorlds = localToWorlds;
        applyTags.animatedLocalToWorlds = animatedLocalToWorlds;
        applyTags.animatorControllerParameters = animatorControllerParameters;
        applyTags.animatorControllers = animatorControllers;
        applyTags.instanceType = instanceType;
        applyTags.surfaceType = surfaceType;
        applyTags.rigType = rigType;
        applyTags.footstepType = footstepType;
        applyTags.tagOffsets = tagOffsets;
        applyTags.tagResults = manager.tagResults;

        jobHandle = applyTags.ScheduleParallel(__group, jobHandle);

        manager.applyJobHandle = jobHandle;

        systemState.Dependency = jobHandle;
    }
}