using System;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Entities;
using ZG;
using Random = Unity.Mathematics.Random;

public enum GameAreaFlag
{
    CreateOnStart = 0x01
}

public interface IGameAreaNeighborEnumerator
{
    void Execute(int areaIndex);
}

public interface IGameAreaNeighborEnumerable
{
    void Execute<T>(int areaIndex, ref T enumerator) where T : IGameAreaNeighborEnumerator;
}

public interface IGameAreaValidator
{
    //bool Check(int areaIndex);

    bool Check(int index, ref GameAreaCreateNodeCommand command);
}

public interface IGameAreaHandler<TNeighborEnumerable, TValidator>
    where TNeighborEnumerable : struct, IGameAreaNeighborEnumerable
    where TValidator : struct, IGameAreaValidator
{
    void GetsNeighborEnumerableAndPrefabIndices(
        in BlobAssetReference<GameAreaPrefabDefinition> definition,
        ref SystemState systemState,
        ref JobHandle jobHandle,
        out TNeighborEnumerable neighborEnumerable, 
        out NativeParallelMultiHashMap<int, int> prefabIndices);

    TValidator GetValidator(ref SystemState systemState, ref JobHandle inputDeps);
}

[Serializable]
public struct GameAreaInternalInstance
{
    [Flags]
    public enum Flag
    {
        NeedTime = 0x01,
        Random = 0x02
    }

    public Flag flag;
    public int areaIndex;
    public int prefabIndex;
}

public struct GameAreaPrefabDefinition
{
    public struct Asset
    {
        public GameAreaFlag flag;
        public int groupStartIndex;
        public int groupCount;
        public float time;
    }

    public struct Prefab
    {
        public int assetIndex;
        public RigidTransform transform;
    }

    public BlobArray<RandomGroup> randomGroups;
    public BlobArray<Asset> assets;
    public BlobArray<Prefab> prefabs;
}

public struct GameAreaPrefabData : IComponentData
{
    public BlobAssetReference<GameAreaPrefabDefinition> definition;
}

public struct GameAreaNeighborEnumerator : IGameAreaNeighborEnumerator
{
    public double time;
    [ReadOnly]
    public NativeParallelMultiHashMap<int, int> prefabIndices;
    public NativeParallelHashMap<int, int>.ParallelWriter areaIndices;
    public NativeParallelHashMap<int, double>.ParallelWriter areaCreatedTimes;
    public NativeFactory<GameAreaInternalInstance>.ParallelWriter instances;

    public void Execute(int areaIndex)
    {
        if (!areaCreatedTimes.TryAdd(areaIndex, time))
            return;

        if (prefabIndices.TryGetFirstValue(areaIndex, out int prefabIndex, out var iterator))
        {
            GameAreaInternalInstance instance;
            do
            {
                if (!areaIndices.TryAdd(prefabIndex, areaIndex))
                    continue;

                instance.flag = 0;
                instance.areaIndex = areaIndex;
                instance.prefabIndex = prefabIndex;
                instances.Create().value = instance;

            } while (prefabIndices.TryGetNextValue(out prefabIndex, ref iterator));
        }
    }
}

[BurstCompile]
public struct GameAreaResizeCreatedTimes : IJob
{
    public int maxNeighborCount;
    [ReadOnly, DeallocateOnJobCompletion]
    public NativeArray<int> entityCount;
    public NativeParallelHashMap<int, double> areaCreatedTimes;

    public void Execute()
    {
        int capacity = areaCreatedTimes.Count() + entityCount[0] * (maxNeighborCount + 1);
        if (areaCreatedTimes.Capacity < capacity)
            areaCreatedTimes.Capacity = capacity;
    }
}

[BurstCompile]
public struct GameAreaCollectInstanceAreaIndices : IJobChunk, IEntityCommandProducerJob
{
    private struct Executor
    {
        [ReadOnly]
        public NativeArray<Entity> entityArray;
        [ReadOnly]
        public NativeArray<GameAreaNode> nodes;
        [ReadOnly]
        public NativeArray<GameAreaInstance> instances;
        public NativeParallelHashMap<int, int>.ParallelWriter areaIndices;
        public EntityCommandQueue<EntityData<GameAreaPrefab>>.ParallelWriter entityManager;

        public void Execute(int index)
        {
            int areaIndex = nodes[index].areaIndex;
            bool result = areaIndices.TryAdd(instances[index].prefabIndex, areaIndex);

            UnityEngine.Assertions.Assert.IsTrue(result);

            EntityData<GameAreaPrefab> prefab;
            prefab.entity = entityArray[index];
            prefab.value.areaIndex = areaIndex;
            entityManager.Enqueue(prefab);
        }
    }

    [ReadOnly]
    public EntityTypeHandle entityType;
    [ReadOnly]
    public ComponentTypeHandle<GameAreaNode> nodeType;
    [ReadOnly]
    public ComponentTypeHandle<GameAreaInstance> instanceType;
    public NativeParallelHashMap<int, int>.ParallelWriter areaIndices;
    public EntityCommandQueue<EntityData<GameAreaPrefab>>.ParallelWriter entityManager;

    public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
    {
        Executor executor;
        executor.entityArray = chunk.GetNativeArray(entityType);
        executor.nodes = chunk.GetNativeArray(ref nodeType);
        executor.instances = chunk.GetNativeArray(ref instanceType);
        executor.areaIndices = areaIndices;
        executor.entityManager = entityManager;

        var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
        while (iterator.NextEntityIndex(out int i))
            executor.Execute(i);
    }
}

[BurstCompile]
public struct GameAreaInit<T> : IJobChunk where T : IGameAreaNeighborEnumerable
{
    public struct Executor
    {
        [ReadOnly]
        public NativeArray<GameAreaNode> nodes;

        public GameAreaNeighborEnumerator neighborEnumerator;
        public T neighborEnumerable;

        public void Execute(int index)
        {
            int areaIndex = nodes[index].areaIndex;

            neighborEnumerator.Execute(areaIndex);

            neighborEnumerable.Execute(areaIndex, ref neighborEnumerator);
        }
    }

    public double time;
    [ReadOnly]
    public ComponentTypeHandle<GameAreaNode> nodeType;
    [ReadOnly]
    public NativeParallelMultiHashMap<int, int> prefabIndices;
    public NativeParallelHashMap<int, int>.ParallelWriter areaIndices;
    public NativeParallelHashMap<int, double>.ParallelWriter areaCreatedTimes;
    public NativeFactory<GameAreaInternalInstance>.ParallelWriter instances;

    public T neighborEnumerable;

    public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
    {
        Executor executor;
        executor.nodes = chunk.GetNativeArray(ref nodeType);
        executor.neighborEnumerator.time = time;
        executor.neighborEnumerator.prefabIndices = prefabIndices;
        executor.neighborEnumerator.areaIndices = areaIndices;
        executor.neighborEnumerator.areaCreatedTimes = areaCreatedTimes;
        executor.neighborEnumerator.instances = instances;
        executor.neighborEnumerable = neighborEnumerable;

        var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
        while (iterator.NextEntityIndex(out int i))
            executor.Execute(i);
    }
}

[BurstCompile]
public struct GameAreaRecreateNodes : IJobChunk, IEntityCommandProducerJob
{
    private struct Executor
    {
        [ReadOnly]
        public NativeArray<Entity> entityArray;
        [ReadOnly]
        public NativeArray<GameAreaInstance> inputs;
        [ReadOnly]
        public NativeParallelHashMap<int, int> areaIndices;
        public NativeFactory<GameAreaInternalInstance>.ParallelWriter outputs;
        public EntityCommandQueue<Entity>.ParallelWriter entityManager;

        public void Execute(int index)
        {
            var input = inputs[index];

            GameAreaInternalInstance ouput;
            ouput.flag = GameAreaInternalInstance.Flag.NeedTime | GameAreaInternalInstance.Flag.Random;
            ouput.areaIndex = areaIndices[input.prefabIndex];
            ouput.prefabIndex = input.prefabIndex;
            outputs.Create().value = ouput;

            entityManager.Enqueue(entityArray[index]);
        }
    }

    [ReadOnly]
    public EntityTypeHandle entityType;
    [ReadOnly]
    public ComponentTypeHandle<GameAreaInstance> instanceType;
    [ReadOnly]
    public NativeParallelHashMap<int, int> areaIndices;
    public NativeFactory<GameAreaInternalInstance>.ParallelWriter instances;
    public EntityCommandQueue<Entity>.ParallelWriter entityManager;

    public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
    {
        Executor executor;
        executor.entityArray = chunk.GetNativeArray(entityType);
        executor.inputs = chunk.GetNativeArray(ref instanceType);
        executor.areaIndices = areaIndices;
        executor.outputs = instances;
        executor.entityManager = entityManager;

        var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
        while (iterator.NextEntityIndex(out int i))
            executor.Execute(i);
    }
}

[BurstCompile]
public struct GameAreaTriggerCreateNodeEvents : IJob
{
    public double time;
    public BlobAssetReference<GameAreaPrefabDefinition> definition;
    public NativeFactory<GameAreaInternalInstance> instances;
    public TimeManager<GameAreaInternalInstance>.Writer timeManager;
    public EntityCommandQueue<GameAreaCreateNodeCommand>.Writer entityManager;

    public void Execute()
    {
        ref var definition = ref this.definition.Value;

        GameAreaInternalInstance instance;
        var enumerator = instances.GetEnumerator();
        while (enumerator.MoveNext())
        {
            instance = enumerator.Current;
            timeManager.Invoke(
                (instance.flag & GameAreaInternalInstance.Flag.NeedTime) == GameAreaInternalInstance.Flag.NeedTime ? time + definition.assets[definition.prefabs[instance.prefabIndex].assetIndex].time : time,
                instance);
        }

        instances.Clear();
    }
}

[BurstCompile]
public struct GameAreaInvokeCommands<T> : IJobParalledForDeferBurstSchedulable, IEntityCommandProducerJob where T : IGameAreaValidator
{
    public struct RandomItemHandler : IRandomItemHandler
    {
        public int index;
        public int prefabIndex;
        public int areaIndex;
        public RigidTransform transform;

        public EntityCommandQueue<GameAreaCreateNodeCommand>.ParallelWriter entityManager;

        public T validator;

        public RandomResult Set(int startIndex, int count)
        {
            GameAreaCreateNodeCommand command;
            command.prefabIndex = prefabIndex;
            command.areaIndex = areaIndex;
            command.transform = transform;
            for (int i = 0; i < count; ++i)
            {
                command.typeIndex = startIndex + i;
                if (validator.Check(index, ref command))
                {
                    entityManager.Enqueue(command);

                    return RandomResult.Success;
                }
            }

            return RandomResult.Pass;
        }
    }

    public Random random;
    public BlobAssetReference<GameAreaPrefabDefinition> definition;
    [ReadOnly]
    public NativeArray<GameAreaInternalInstance> commands;

    public NativeFactory<GameAreaInternalInstance>.ParallelWriter instances;

    public EntityCommandQueue<GameAreaCreateNodeCommand>.ParallelWriter entityManager;

    public T validator;

    public unsafe void Execute(int index)
    {
        ref var definition = ref this.definition.Value;

        var command = commands[index];
        /*if (!validator.Check(command.areaIndex))
            return;*/

        RandomItemHandler randomItemHandler;
        randomItemHandler.index = index;
        randomItemHandler.areaIndex = command.areaIndex;
        randomItemHandler.prefabIndex = command.prefabIndex;

        var prefab = definition.prefabs[command.prefabIndex];
        randomItemHandler.transform = prefab.transform;

        randomItemHandler.entityManager = entityManager;

        randomItemHandler.validator = validator;

        var asset = definition.assets[prefab.assetIndex];
        /*if (!random.Next(
                ref randomItemHandler,
                randomGroups.Slice(asset.groupStartIndex, asset.groupCount),
                (command.flag & GameAreaInternalInstance.Flag.Random) != GameAreaInternalInstance.Flag.Random))
        {
            GameAreaInternalInstance instance;
            instance.flag = GameAreaInternalInstance.Flag.NeedTime;
            instance.areaIndex = command.areaIndex;
            instance.prefabIndex = command.prefabIndex;

            instances.Enqueue(instance);
        }*/

        GameAreaInternalInstance instance;
        var randomGroups = definition.randomGroups.AsArray();
        if ((command.flag & GameAreaInternalInstance.Flag.Random) == GameAreaInternalInstance.Flag.Random)
        {
            switch (random.Next(
                    ref randomItemHandler,
                    randomGroups.Slice(asset.groupStartIndex, asset.groupCount)))
            {
                case RandomResult.Success:
                    return;
                case RandomResult.Pass:
                    instance.flag = command.flag & ~(/*GameAreaInternalInstance.Flag.NeedTime | */GameAreaInternalInstance.Flag.Random);
                    break;
                default:
                    instance.flag = command.flag;
                    break;
            }
        }
        else
        {
            int i;
            float sum = 0.0f;
            RandomGroup randomGroup;
            for (i = 0; i < asset.groupCount; ++i)
            {
                randomGroup = randomGroups[i + asset.groupStartIndex];

                sum += randomGroup.chance;
            }

            if (sum > 0.0f)
            {
                float chance = 0.0f, randomValue = random.NextFloat();
                for (i = 0; i < asset.groupCount; ++i)
                {
                    randomGroup = randomGroups[i + asset.groupStartIndex];
                    chance += randomGroup.chance / sum;
                    if (chance > randomValue && randomItemHandler.Set(randomGroup.startIndex, randomGroup.count) == RandomResult.Success)
                        return;
                }
            }
            else
            {
                for (i = 0; i < asset.groupCount; ++i)
                {
                    randomGroup = randomGroups[i + asset.groupStartIndex];
                    if (randomItemHandler.Set(randomGroup.startIndex, randomGroup.count) == RandomResult.Success)
                        return;
                }
            }

            instance.flag = command.flag |/*&*/ GameAreaInternalInstance.Flag.NeedTime;
        }

        instance.areaIndex = command.areaIndex;
        instance.prefabIndex = command.prefabIndex;

        instances.Create().value = instance;
    }
}
