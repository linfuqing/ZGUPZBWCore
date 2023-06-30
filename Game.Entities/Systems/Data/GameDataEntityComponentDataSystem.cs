using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using ZG;

#region GameOwner
[assembly: RegisterGenericJobType(typeof(EntityDataComponentSerialize<GameDataEntityCompoentDataSerializationSystem<GameOwner>.Serializer, GameDataEntityCompoentDataSerializationSystem<GameOwner>.SerializerFactory>))]
[assembly: RegisterGenericJobType(typeof(EntityDataComponentDeserialize<GameDataEntityCompoentDataDeserializationSystem<GameOwner>.Deserializer, GameDataEntityCompoentDataDeserializationSystem<GameOwner>.DeserializerFactory>))]
[assembly: EntityDataSerialize(typeof(GameOwner), typeof(GameDataEntityCompoentDataSerializationSystem<GameOwner>))]
[assembly: EntityDataDeserialize(typeof(GameOwner), typeof(GameDataEntityCompoentDataDeserializationSystem<GameOwner>), (int)GameDataConstans.Version)]
#endregion

#region GameActorMaster
[assembly: RegisterGenericJobType(typeof(EntityDataComponentSerialize<GameDataEntityCompoentDataSerializationSystem<GameActorMaster>.Serializer, GameDataEntityCompoentDataSerializationSystem<GameActorMaster>.SerializerFactory>))]
[assembly: RegisterGenericJobType(typeof(EntityDataComponentDeserialize<GameDataEntityCompoentDataDeserializationSystem<GameActorMaster>.Deserializer, GameDataEntityCompoentDataDeserializationSystem<GameActorMaster>.DeserializerFactory>))]
[assembly: EntityDataSerialize(typeof(GameActorMaster), typeof(GameDataEntityCompoentDataSerializationSystem<GameActorMaster>))]
[assembly: EntityDataDeserialize(typeof(GameActorMaster), typeof(GameDataEntityCompoentDataDeserializationSystem<GameActorMaster>), (int)GameDataConstans.Version)]
#endregion

#region GamePlayerLocator
[assembly: RegisterGenericJobType(typeof(EntityDataComponentSerialize<GameDataEntityCompoentDataSerializationSystem<GamePlayerLocator>.Serializer, GameDataEntityCompoentDataSerializationSystem<GamePlayerLocator>.SerializerFactory>))]
[assembly: RegisterGenericJobType(typeof(EntityDataComponentDeserialize<GameDataEntityCompoentDataDeserializationSystem<GamePlayerLocator>.Deserializer, GameDataEntityCompoentDataDeserializationSystem<GamePlayerLocator>.DeserializerFactory>))]
[assembly: EntityDataSerialize(typeof(GamePlayerLocator), typeof(GameDataEntityCompoentDataSerializationSystem<GamePlayerLocator>))]
[assembly: EntityDataDeserialize(typeof(GamePlayerLocator), typeof(GameDataEntityCompoentDataDeserializationSystem<GamePlayerLocator>), (int)GameDataConstans.Version)]
#endregion

#region GamePlayerSpawn
[assembly: RegisterGenericJobType(typeof(EntityDataComponentSerialize<GameDataEntityCompoentDataSerializationSystem<GamePlayerSpawn>.Serializer, GameDataEntityCompoentDataSerializationSystem<GamePlayerSpawn>.SerializerFactory>))]
[assembly: RegisterGenericJobType(typeof(EntityDataComponentDeserialize<GameDataEntityCompoentDataDeserializationSystem<GamePlayerSpawn>.Deserializer, GameDataEntityCompoentDataDeserializationSystem<GamePlayerSpawn>.DeserializerFactory>))]
[assembly: EntityDataSerialize(typeof(GamePlayerSpawn), typeof(GameDataEntityCompoentDataSerializationSystem<GamePlayerSpawn>))]
[assembly: EntityDataDeserialize(typeof(GamePlayerSpawn), typeof(GameDataEntityCompoentDataDeserializationSystem<GamePlayerSpawn>), (int)GameDataConstans.Version)]
#endregion

public struct GamePlayerLocator : IGameDataEntityCompoentData
{
    public Entity entity;

    Entity IGameDataEntityCompoentData.entity
    {
        get => entity;

        set => entity = value;
    }
}

[EntityDataTypeName("GameSpawn")]
public struct GamePlayerSpawn : IGameDataEntityCompoentData
{
    public Entity entity;

    Entity IGameDataEntityCompoentData.entity
    {
        get => entity;

        set => entity = value;
    }
}

public interface IGameDataEntityCompoentData : IComponentData
{
    Entity entity { get; set; }
}

public partial class GameDataEntityCompoentDataSerializationSystem<T> : EntityDataSerializationComponentSystem<
    T,
    GameDataEntityCompoentDataSerializationSystem<T>.Serializer,
    GameDataEntityCompoentDataSerializationSystem<T>.SerializerFactory> where T : unmanaged, IGameDataEntityCompoentData
{
    public struct Serializer : IEntityDataSerializer
    {
        [ReadOnly]
        public NativeArray<T> instances;
        [ReadOnly]
        public ComponentLookup<EntityDataIdentity> identities;

        public void Serialize(int index, in NativeParallelHashMap<Hash128, int> entityIndices, ref EntityDataWriter writer)
        {
            Entity entity = instances[index].entity;

            writer.Write(identities.HasComponent(entity) && entityIndices.TryGetValue(identities[entity].guid, out int entityIndex) ? entityIndex : -1);
        }
    }

    public struct SerializerFactory : IEntityDataFactory<Serializer>
    {
        [ReadOnly]
        public ComponentTypeHandle<T> instanceType;
        [ReadOnly]
        public ComponentLookup<EntityDataIdentity> identities;

        public Serializer Create(in ArchetypeChunk chunk, int unfilteredChunkIndex)
        {
            Serializer serializer;
            serializer.instances = chunk.GetNativeArray(ref instanceType);
            serializer.identities = identities;

            return serializer;
        }
    }

    protected override SerializerFactory _Get(ref JobHandle jobHandle)
    {
        SerializerFactory serializerFactory;
        serializerFactory.instanceType = GetComponentTypeHandle<T>(true);
        serializerFactory.identities = GetComponentLookup<EntityDataIdentity>(true);

        return serializerFactory;
    }
}

[AlwaysUpdateSystem]
public partial class GameDataEntityCompoentDataDeserializationSystem<T> : EntityDataDeserializationComponentSystem<
    T,
    GameDataEntityCompoentDataDeserializationSystem<T>.Deserializer,
    GameDataEntityCompoentDataDeserializationSystem<T>.DeserializerFactory> where T : unmanaged, IGameDataEntityCompoentData
{
    public struct Deserializer : IEntityDataDeserializer
    {
        [ReadOnly]
        public NativeArray<Entity> entityArray;

        public NativeParallelHashMap<Entity, int>.ParallelWriter entityIndices;

        public void Deserialize(int index, ref EntityDataReader reader)
        {
            entityIndices.TryAdd(entityArray[index], reader.Read<int>());
        }
    }

    public struct DeserializerFactory : IEntityDataFactory<Deserializer>
    {
        [ReadOnly]
        public EntityTypeHandle entityType;

        public NativeParallelHashMap<Entity, int>.ParallelWriter entityIndices;

        public Deserializer Create(in ArchetypeChunk chunk, int unfilteredChunkIndex)
        {
            Deserializer deserializer;
            deserializer.entityArray = chunk.GetNativeArray(entityType);
            deserializer.entityIndices = entityIndices;

            return deserializer;
        }
    }

    [BurstCompile]
    private struct Build : IJob
    {
        public int entityCount;
        [ReadOnly]
        public NativeParallelHashMap<int, Entity> entities;

        [ReadOnly]
        public NativeParallelHashMap<Entity, int> entityIndices;

        public ComponentLookup<T> values;

        public void Execute()
        {
            if (entityCount > entities.Count())
                return;

            using (var keyValueArrays = entityIndices.GetKeyValueArrays(Allocator.Temp))
            {
                T value = default;
                int entityIndex, length = keyValueArrays.Keys.Length;
                for (int i = 0; i < length; ++i)
                {
                    entityIndex = keyValueArrays.Values[i];

                    value.entity = entityIndex == -1 ? Entity.Null : entities[entityIndex];
                    values[keyValueArrays.Keys[i]] = value;
                }
            }
        }
    }

    private NativeParallelHashMap<Entity, int> __entityIndices;

    protected override void OnCreate()
    {
        base.OnCreate();

        __entityIndices = new NativeParallelHashMap<Entity, int>(1, Allocator.Persistent);
    }

    protected override void OnDestroy()
    {
        __entityIndices.Dispose();

        base.OnStopRunning();
    }

    protected override void OnUpdate()
    {
        base.OnUpdate();

        var systemGroup = base.systemGroup;
        var presentationSystem = systemGroup.presentationSystem;
        var jobHandle = JobHandle.CombineDependencies(Dependency, presentationSystem.readOnlyJobHandle);

        Build build;
        build.entityCount = systemGroup.initializationSystem.guids.Length;
        build.entities = presentationSystem.entities;
        build.entityIndices = __entityIndices;
        build.values = GetComponentLookup<T>();
        jobHandle = build.Schedule(jobHandle);

        presentationSystem.AddReadOnlyDependency(jobHandle);

        Dependency = jobHandle;
    }

    protected override DeserializerFactory _Get(ref JobHandle jobHandle)
    {
        __entityIndices.Capacity = math.max(__entityIndices.Capacity, __entityIndices.Count() + group.CalculateEntityCount());

        var presentationSystem = systemGroup.presentationSystem;
        jobHandle = JobHandle.CombineDependencies(jobHandle, presentationSystem.readOnlyJobHandle);

        DeserializerFactory factory;
        factory.entityType = GetEntityTypeHandle();
        factory.entityIndices = __entityIndices.AsParallelWriter();

        return factory;
    }
}
