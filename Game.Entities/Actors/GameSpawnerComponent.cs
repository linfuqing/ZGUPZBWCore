using System;
using Unity.Jobs;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Collections;
using ZG;

[Serializable]
public struct GameSpawnerAsset
{
    public int capacity;
    public float deadline;
}

[Serializable]
public struct GameSpawnedInstanceData : IComponentData
{
    public int assetIndex;
}

[Serializable]
public struct GameSpawnedInstanceInfo : IComponentData
{
    public double time;
}

[Serializable]
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

public abstract class GameSpawnCommander : IEntityCommander<GameSpawnData>
{
    public interface IInitializer : IEntityDataInitializer
    {
        Entity entity { get; }
    }

    private struct Initializer : IInitializer
    {
        private int __assetIndex;

        //private double __time;

        public Entity entity { get; }

        public World world { get; }

        public Initializer(int assetIndex, /*double time, */Entity entity, World world)
        {
            __assetIndex = assetIndex;

            //__time = time;

            this.entity = entity;

            this.world = world;
        }

        public GameObjectEntityWrapper Invoke(Entity entity)
        {
            GameSpawnedInstanceData instance;
            instance.assetIndex = __assetIndex;
            //instance.time = __time;

            var gameObjectEntity = new GameObjectEntityWrapper(entity, world);
            gameObjectEntity.SetComponentData(instance);
            return gameObjectEntity;
        }
    }

    public abstract void Create<T>(int assetIndex, in RigidTransform transform, in T initializer) where T : IInitializer;

    public void Execute(
        EntityCommandPool<GameSpawnData>.Context context, 
        EntityCommandSystem system, 
        ref NativeParallelHashMap<ComponentType, JobHandle> dependency,
        in JobHandle inputDeps)
    {
        World world = system.World;
        while (context.TryDequeue(out var command))
        {
            dependency.CompleteAll(inputDeps);

            Create(command.assetIndex, command.transform, new Initializer(command.assetIndex, /*command.time, */command.entity, world));
        }
    }

    void IDisposable.Dispose()
    {

    }
}

[EntityComponent(typeof(GameSpawnerAssetCounter))]
public class GameSpawnerComponent : EntityProxyComponent
{
}