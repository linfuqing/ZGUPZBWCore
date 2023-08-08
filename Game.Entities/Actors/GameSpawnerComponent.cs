using System;
using Unity.Jobs;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Collections;
using ZG;

public struct GameSpawnerAsset
{
    public int capacity;
    public float deadline;
}

public struct GameSpawnedInstanceData : IComponentData
{
    public int assetIndex;
}

public struct GameSpawnedInstanceInfo : IComponentData
{
    public double time;
}

public struct GameSpawnerAssetCounter : IBufferElementData
{
    public int assetIndex;
    public int value;
}

public struct GameSpawnData
{
    public int assetIndex;
    //public double time;
    public Entity entity;
    public RigidTransform transform;
}

/*public abstract class GameSpawnCommander : IEntityCommander<GameSpawnData>
{
    private struct Initializer : IEntityDataInitializer
    {
        private int __assetIndex;

        //private double __time;

        public Initializer(int assetIndex)
        {
            __assetIndex = assetIndex;

            //__time = time;
        }

        public void Invoke<T>(ref T gameObjectEntity) where T : IGameObjectEntity
        {
            GameSpawnedInstanceData instance;
            instance.assetIndex = __assetIndex;
            //instance.time = __time;

            gameObjectEntity.AddComponentData(instance);
        }
    }

    public abstract void Create<T>(int assetIndex, in RigidTransform transform, in Entity ownerEntity, in T initializer) where T : IEntityDataInitializer;

    public void Execute(
        EntityCommandPool<GameSpawnData>.Context context, 
        EntityCommandSystem system, 
        ref NativeParallelHashMap<ComponentType, JobHandle> dependency,
        in JobHandle inputDeps)
    {
        while (context.TryDequeue(out var command))
        {
            dependency.CompleteAll(inputDeps);

            Create(command.assetIndex, command.transform, command.entity, new Initializer(command.assetIndex));
        }
    }

    void IDisposable.Dispose()
    {

    }
}*/

[EntityComponent(typeof(GameSpawnerAssetCounter))]
public class GameSpawnerComponent : EntityProxyComponent
{
}