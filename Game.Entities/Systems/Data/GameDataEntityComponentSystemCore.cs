using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using ZG;

public interface IGameDataEntityCompoent
{
    Entity entity { get; set; }

    void Serialize(int entityIndex, ref EntityDataWriter writer);
}

public struct GameDataEntityComponentDataSerializationSystemCore<T> where T : unmanaged, IComponentData, IGameDataEntityCompoent
{
    public struct Serializer : IEntityDataSerializer
    {
        [ReadOnly]
        public NativeArray<T> instances;
        [ReadOnly]
        public ComponentLookup<EntityDataIdentity> identities;

        public void Serialize(int index, in SharedHashMap<Hash128, int>.Reader entityIndices, ref EntityDataWriter writer)
        {
            var instance = instances[index];
            Entity entity = instance.entity;

            instance.Serialize(identities.HasComponent(entity) && entityIndices.TryGetValue(identities[entity].guid, out int entityIndex) ? entityIndex : -1, ref writer);
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

    private ComponentLookup<EntityDataIdentity> __identities;
    private ComponentTypeHandle<T> __instanceType;
    private EntityDataSerializationSystemCore __core;

    public GameDataEntityComponentDataSerializationSystemCore(ref SystemState state)
    {
        __identities = state.GetComponentLookup<EntityDataIdentity>(true);
        __instanceType = state.GetComponentTypeHandle<T>();
        __core = EntityDataSerializationSystemCore.Create<GameContainerChild>(ref state);
    }

    public void Dispose()
    {
        __core.Dispose();
    }

    [BurstCompile]
    public void Update(ref SystemState state)
    {
        SerializerFactory serializerFactory;
        serializerFactory.identities = __identities.UpdateAsRef(ref state);
        serializerFactory.instanceType = __instanceType.UpdateAsRef(ref state);

        __core.Update<Serializer, SerializerFactory>(ref serializerFactory, ref state);
    }
}

public struct GameDataEntityBufferSerializationSystemCore<T> where T : unmanaged, IBufferElementData, IGameDataEntityCompoent
{
    public struct Serializer : IEntityDataSerializer
    {
        [ReadOnly]
        public ComponentLookup<EntityDataIdentity> identities;
        public BufferAccessor<T> instances;

        public void Serialize(int index, in SharedHashMap<Hash128, int>.Reader entityIndices, ref EntityDataWriter writer)
        {
            var instances = this.instances[index];
            T instance;
            Entity entity;
            int length = instances.Length;
            for (int i = 0; i < length; ++i)
            {
                instance = instances[i];
                entity = instance.entity;
                if (!identities.HasComponent(entity) || !entityIndices.ContainsKey(identities[entity].guid))
                {
                    UnityEngine.Debug.LogError($"Child {entity} Missing.");

                    instances.RemoveAt(i--);

                    --length;
                }
            }

            writer.Write(length);
            for (int i = 0; i < length; ++i)
            {
                instance = instances[i];
                instance.Serialize(entityIndices[identities[instance.entity].guid], ref writer);
            }
        }
    }

    public struct SerializerFactory : IEntityDataFactory<Serializer>
    {
        [ReadOnly]
        public ComponentLookup<EntityDataIdentity> identities;
        public BufferTypeHandle<T> instanceType;

        public Serializer Create(in ArchetypeChunk chunk, int firstEntityIndex)
        {
            Serializer serializer;
            serializer.identities = identities;
            serializer.instances = chunk.GetBufferAccessor(ref instanceType);

            return serializer;
        }
    }

    private ComponentLookup<EntityDataIdentity> __identities;
    private BufferTypeHandle<T> __instanceType;
    private EntityDataSerializationSystemCore __core;

    public GameDataEntityBufferSerializationSystemCore(ref SystemState state)
    {
        __identities = state.GetComponentLookup<EntityDataIdentity>(true);
        __instanceType = state.GetBufferTypeHandle<T>();
        __core = EntityDataSerializationSystemCore.Create<GameContainerChild>(ref state);
    }

    public void Dispose()
    {
        __core.Dispose();
    }

    public void Update(ref SystemState state)
    {
        SerializerFactory serializerFactory;
        serializerFactory.identities = __identities.UpdateAsRef(ref state);
        serializerFactory.instanceType = __instanceType.UpdateAsRef(ref state);

        __core.Update<Serializer, SerializerFactory>(ref serializerFactory, ref state);
    }
}

[AlwaysUpdateSystem]
public partial class GameDataEntityCompoentDataDeserializationSystem<T> : EntityDataDeserializationComponentSystem<
    T,
    GameDataEntityCompoentDataDeserializationSystem<T>.Deserializer,
    GameDataEntityCompoentDataDeserializationSystem<T>.DeserializerFactory> where T : unmanaged, IComponentData, IGameDataEntityCompoent
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