using Unity.Jobs;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine.Jobs;
using ZG;

[assembly: RegisterGenericJobType(typeof(GameTransformFactoryCopy<GameTransform, GameTransformVelocity, GameTransformFactorySystem.Handler>))]
//[assembly: RegisterGenericJobType(typeof(GameTransformUpdateDestinations<GameTransform, GameTransformUpdateSystem.Handler, GameTransformUpdateSystem.Factory>))]
[assembly: RegisterGenericJobType(typeof(GameTransformUpdateAll<GameTransform, GameTransformUpdateSystem.Handler, GameTransformUpdateSystem.Factory>))]
[assembly: RegisterGenericJobType(typeof(GameTransformSmoothDamp<GameTransform, GameTransformVelocity, GameTransformSystem.TransformJob>))]

[BurstCompile, UpdateInGroup(typeof(GameTransformFactroySystemGroup))]
public partial struct GameTransformFactorySystem : ISystem
{
    public struct Handler : IGameTransformEntityHandler<GameTransform>
    {
        [ReadOnly]
        public ComponentLookup<Translation> translations;
        [ReadOnly]
        public ComponentLookup<Rotation> rotations;

        public GameTransform Get(int index, in Entity entity)
        {
            GameTransform transform;
            transform.value = math.RigidTransform(rotations[entity].Value, translations[entity].Value);
            return transform;
        }
    }

    public struct Factory : GameTransformFactory<GameTransform, GameTransformVelocity, Handler>.IFactory<Handler>
    {
        public Handler Get(ref SystemState systemState)
        {
            Handler handler;
            handler.translations = systemState.GetComponentLookup<Translation>(true);
            handler.rotations = systemState.GetComponentLookup<Rotation>(true);
            return handler;
        }
    }

    public static readonly int InnerloopBatchCount = 1;

    //private EntityQuery __group;
    private GameRollbackTime __time;
    private GameTransformFactory<GameTransform, GameTransformVelocity, Handler> __instance;

    public void OnCreate(ref SystemState state)
    {
        __instance = new GameTransformFactory<GameTransform, GameTransformVelocity, Handler>(
            new ComponentType[]
            {
                ComponentType.ReadOnly<Translation>(),
                ComponentType.ReadOnly<Rotation>()
            }, ref state);

        //__group = state.GetEntityQuery(ComponentType.ReadOnly<GameSyncData>());
        __time = new GameRollbackTime(ref state);
    }

    public void OnDestroy(ref SystemState state)
    {

    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        Factory factory;

        __instance.Update(
            InnerloopBatchCount,
            __time.now, //__group.GetSingleton<GameSyncData>().time, 
            ref state,
            factory);
    }
}

[BurstCompile, UpdateInGroup(typeof(GameTransformSimulationSystemGroup))]
public partial struct GameTransformUpdateSystem : ISystem
{
    public struct Handler : IGameTransformHandler<GameTransform>
    {
        [ReadOnly]
        public NativeArray<Translation> translations;
        [ReadOnly]
        public NativeArray<Rotation> rotations;

        public GameTransform Get(int index)
        {
            GameTransform transform;
            transform.value = math.RigidTransform(rotations[index].Value, translations[index].Value);
            return transform;
        }
    }

    public struct Factory : IGameTransformFactory<GameTransform, Handler>
    {
        [ReadOnly]
        public ComponentTypeHandle<Translation> translationType;
        [ReadOnly]
        public ComponentTypeHandle<Rotation> rotationType;

        public Handler Get(in ArchetypeChunk chunk)
        {
            Handler handler;
            handler.translations = chunk.GetNativeArray(ref translationType);
            handler.rotations = chunk.GetNativeArray(ref rotationType);
            return handler;
        }
    }

    private GameTransformSimulatorEx<GameTransform> __instance;

    public void OnCreate(ref SystemState state)
    {
        __instance = new GameTransformSimulatorEx<GameTransform>(new ComponentType[]
        {
            ComponentType.ReadOnly<Translation>(),
            ComponentType.ReadOnly<Rotation>()
        }, ref state);
    }

    public void OnDestroy(ref SystemState state)
    {

    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        __instance.Update<Handler, Factory>(ref state, __Get(ref state));
    }

    private Factory __Get(ref SystemState state)
    {
        Factory factory;
        factory.translationType = state.GetComponentTypeHandle<Translation>(true);
        factory.rotationType = state.GetComponentTypeHandle<Rotation>(true);

        return factory;
    }
}

//[UpdateInGroup(typeof(TimeSystemGroup)), UpdateAfter(typeof(GameSyncSystemGroup))]
//[UpdateInGroup(typeof(PresentationSystemGroup))]
public partial class GameTransformSystem : SystemBase
{
    [BurstCompile]
    private struct CopyFromTransforms : IJobParallelForTransform
    {
        public NativeArray<GameTransform> transforms;

        public void Execute(int index, TransformAccess transformAccess)
        {
            if (!transformAccess.isValid)
                return;

            GameTransform transform;
            transform.value = math.RigidTransform(transformAccess.rotation, transformAccess.position);
            transforms[index] = transform;
        }
    }

    [BurstCompile]
    private struct CopyToTransforms : IJobParallelForTransform
    {
        [ReadOnly, DeallocateOnJobCompletion]
        public NativeArray<GameTransform> transforms;

        public void Execute(int index, TransformAccess transformAccess)
        {
            if (!transformAccess.isValid)
                return;

            var transform = transforms[index];
            transformAccess.position = transform.value.pos;
            transformAccess.rotation = transform.value.rot;
        }
    }

    /*[BurstCompile]
    private struct CopyToLocalToWorld : IJobEntityBatchWithIndex
    {
        [ReadOnly, DeallocateOnJobCompletion]
        public NativeArray<GameTransform> transforms;

        public ComponentTypeHandle<LocalToWorld> localToWorldType;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask, int indexOfFirstEntityInQuery)
        {
            var localToWorlds = batchInChunk.GetNativeArray(localToWorldType);
            LocalToWorld localToWorld;
            int count = batchInChunk.Count;
            for(int i = 0; i < count; ++i)
            {
                localToWorld.Value = math.float4x4(transforms[indexOfFirstEntityInQuery + i].value);

                localToWorlds[i] = localToWorld;
            }
        }
    }*/

    [BurstCompile]
    private struct ApplyTrasnforms : IJobChunk
    {
        [ReadOnly, DeallocateOnJobCompletion]
        public NativeArray<int> baseEntityIndexArray;

        [ReadOnly, DeallocateOnJobCompletion]
        public NativeArray<GameTransform> transforms;

        [Unity.Collections.LowLevel.Unsafe.NativeDisableContainerSafetyRestriction]
        public ComponentTypeHandle<LocalToWorld> localToWorldType;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            var localToWorlds = chunk.GetNativeArray(ref localToWorldType);
            LocalToWorld localToWorld;
            GameTransform transform;
            int index = baseEntityIndexArray[unfilteredChunkIndex];
            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
            {
                transform = transforms[index++];
                localToWorld.Value = math.float4x4(transform.value);

                localToWorlds[i] = localToWorld;
            }
        }
    }

    public struct TransformJob : IGameTransformJob<GameTransform, GameTransformCalculator<GameTransform, GameTransformVelocity>>
    {
        [ReadOnly, DeallocateOnJobCompletion]
        public NativeArray<int> baseEntityIndexArray;

        [NativeDisableParallelForRestriction]
        public NativeArray<GameTransform> transforms;

        public ComponentTypeHandle<LocalToWorld> localToWorldType;

        public void Execute(
            in GameTransformCalculator<GameTransform, GameTransformVelocity> calculator, 
            in ArchetypeChunk chunk, 
            int unfilteredChunkIndex, 
            bool useEnabledMask, 
            in v128 chunkEnabledMask)
        {
            int index = baseEntityIndexArray[unfilteredChunkIndex];
            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            if (chunk.Has(ref localToWorldType))
            {
                GameTransform transform;
                LocalToWorld localToWorld;
                var localToWorlds = chunk.GetNativeArray(ref localToWorldType);
                while (iterator.NextEntityIndex(out int i))
                {
                    localToWorld = localToWorlds[i];
                    transform.value = localToWorld.Value.Equals(float4x4.zero) ? transforms[index].value : math.RigidTransform(localToWorld.Value);
                    transform = calculator.Execute(i, transform);

                    transforms[index] = transform;

                    localToWorld.Value = math.float4x4(transform.value);
                    localToWorlds[i] = localToWorld;

                    ++index;
                }
            }
            else
            {
                while (iterator.NextEntityIndex(out int i))
                {
                    transforms[index] = calculator.Execute(i, transforms[index]);

                    ++index;
                }
            }
        }
    }

    public int batchSize = 1;

    private EntityQuery __childGroup;
    private GameTransformApplication<GameTransform, GameTransformVelocity> __instance;
    //private TransformAccessArrayEx __sourceTransformAccessArray;
    private TransformAccessArrayEx __destinationTransformAccessArray;
    private TransformAccessArrayEx __childTransformAccessArray;

    protected override void OnCreate()
    {
        base.OnCreate();

        __childGroup = GetEntityQuery(ComponentType.ReadWrite<LocalToWorld>(), ComponentType.ReadOnly<GameNodeParent>(), TransformAccessArrayEx.componentType);

        __instance = new GameTransformApplication<GameTransform, GameTransformVelocity>(
            new ComponentType[] {
                TransformAccessArrayEx.componentType,
                ComponentType.Exclude<GameNodeParent>(), 
            }, 
            ref this.GetState());

        /*__group = GetEntityQuery(
            ComponentType.ReadOnly<GameTransformData>(),
            ComponentType.ReadWrite<LocalToWorld>(),
            TransformAccessArrayEx.componentType);

        __sourceTransformAccessArray = new TransformAccessArrayEx(__group);*/
        __destinationTransformAccessArray = new TransformAccessArrayEx(__instance.group);
        __childTransformAccessArray = new TransformAccessArrayEx(__childGroup);
    }

    protected override void OnDestroy()
    {
        __childTransformAccessArray.Dispose();
        //__sourceTransformAccessArray.Dispose();
        __destinationTransformAccessArray.Dispose();

        base.OnDestroy();
    }

    protected override void OnUpdate()
    {
        JobHandle inputDeps = Dependency;
        JobHandle? result = null;
        var localToWorldType = GetComponentTypeHandle<LocalToWorld>();
        if (!__instance.group.IsEmptyIgnoreFilter)
        {
            var transformAccessArray = __destinationTransformAccessArray.Convert(this);
            var transforms = new NativeArray<GameTransform>(transformAccessArray.length, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            CopyFromTransforms copyFromTransforms;
            copyFromTransforms.transforms = transforms;
            var jobHandle = copyFromTransforms.ScheduleReadOnly(transformAccessArray, batchSize, inputDeps);

            TransformJob job;
            job.baseEntityIndexArray = __instance.group.CalculateBaseEntityIndexArrayAsync(Allocator.TempJob, inputDeps, out var temp);
            job.transforms = transforms;
            job.localToWorldType = localToWorldType;

            Dependency = JobHandle.CombineDependencies(jobHandle, temp);

            __instance.Update(ref this.GetState(), job);

            CopyToTransforms copyToTransforms;
            copyToTransforms.transforms = transforms;

            jobHandle = copyToTransforms.Schedule(transformAccessArray, Dependency);
            result = result == null ? jobHandle : JobHandle.CombineDependencies(jobHandle, result.Value);
        }

        if(!__childGroup.IsEmptyIgnoreFilter)
        {
            var transformAccessArray = __childTransformAccessArray.Convert(this);
            var transforms = new NativeArray<GameTransform>(transformAccessArray.length, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            CopyFromTransforms copyFromTransforms;
            copyFromTransforms.transforms = transforms;
            var jobHandle = copyFromTransforms.ScheduleReadOnly(transformAccessArray, batchSize, inputDeps);

            ApplyTrasnforms applyTrasnforms;
            applyTrasnforms.baseEntityIndexArray = __childGroup.CalculateBaseEntityIndexArrayAsync(Allocator.TempJob, inputDeps, out var temp);
            applyTrasnforms.transforms = transforms;
            applyTrasnforms.localToWorldType = localToWorldType;
            jobHandle = applyTrasnforms.ScheduleParallel(__childGroup, JobHandle.CombineDependencies(jobHandle, temp));

            result = result == null ? jobHandle : JobHandle.CombineDependencies(jobHandle, result.Value);
        }

        /*if (!__group.IsEmptyIgnoreFilter)
        {
            var transformAccessArray = __sourceTransformAccessArray.Convert(this);
            var transforms = new NativeArray<GameTransform>(transformAccessArray.length, Allocator.TempJob);
            CopyFromTransforms copyFromTransforms;
            copyFromTransforms.transforms = transforms;
            var jobHandle = copyFromTransforms.ScheduleReadOnly(transformAccessArray, batchSize, Dependency);

            CopyToLocalToWorld copyToLocalToWorld;
            copyToLocalToWorld.transforms = transforms;
            copyToLocalToWorld.localToWorldType = GetComponentTypeHandle<LocalToWorld>();
            Dependency = copyToLocalToWorld.ScheduleParallel(__group, 1, jobHandle);
        }*/

        if (result != null)
            Dependency = result.Value;
    }
}
