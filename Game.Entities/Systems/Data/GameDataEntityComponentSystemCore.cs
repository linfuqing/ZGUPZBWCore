using Unity;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using ZG;

public interface IGameDataEntityCompoent
{
    Entity entity { get; set; }

    void Serialize(int entityIndex, ref EntityDataWriter writer);

    int Deserialize(ref EntityDataReader reader);
}

public interface IGameDataEntityCompoentSerializer<T>
{
    Entity Get(in T value);

    void Serialize(in T value, int entityIndex, ref EntityDataWriter writer);
}

public interface IGameDataEntityCompoentDeserializer<T>
{
    bool Fallback(in Entity entity, in T value);

    int Deserialize(in Entity entity, ref T value, ref EntityDataReader reader);
}

public interface IGameDataEntityCompoentBuilder<T>
{
    void Set(ref T value, in Entity entity, in Entity instance);
}

public interface IGameDataEntityCompoentWrapper<T> : 
    IGameDataEntityCompoentSerializer<T>, 
    IGameDataEntityCompoentDeserializer<T>, 
    IGameDataEntityCompoentBuilder<T>
{

}

[BurstCompile]
public struct GameDataEntityComponentDataDeserializationRecapacity : IJob
{
    [ReadOnly]
    public EntityDataDeserializationStatusQuery.Container status;

    [ReadOnly, DeallocateOnJobCompletion]
    public NativeArray<int> entityCount;

    public NativeParallelHashMap<Entity, int> identityIndices;

    public void Execute()
    {
        if (status.value == EntityDataDeserializationStatus.Value.Created)
            identityIndices.Clear();

        identityIndices.Capacity = math.max(identityIndices.Capacity, identityIndices.Count() + entityCount[0]);
    }
}

[BurstCompile]
public struct GameDataEntityBufferDeserializationRecapacity : IJob
{
    public struct Child
    {
        public int index;

        public int identityIndex;
    }

    [ReadOnly]
    public EntityDataDeserializationStatusQuery.Container status;

    [ReadOnly, DeallocateOnJobCompletion]
    public NativeArray<int> count;

    public NativeParallelMultiHashMap<Entity, Child> children;

    public void Execute()
    {
        if (status.value == EntityDataDeserializationStatus.Value.Created)
            children.Clear();

        children.Capacity = math.max(children.Capacity, children.Count() + count[0]);
    }
}

public struct GameDataEntityComponentDataDeserializer<TValue, TDeserializer> : IEntityDataDeserializer
    where TValue : unmanaged, IComponentData
    where TDeserializer : IGameDataEntityCompoentDeserializer<TValue>
{
    [ReadOnly]
    public NativeArray<Entity> entityArray;

    public NativeArray<TValue> instances;

    public NativeParallelHashMap<Entity, int>.ParallelWriter identityIndices;

    public TDeserializer deserializer;

    public bool Fallback(int index)
    {
        return deserializer.Fallback(entityArray[index], instances[index]);// false;
    }

    public void Deserialize(int index, ref EntityDataReader reader)
    {
        var instance = instances[index];
        
        Entity entity = entityArray[index];

        int entityIndex = deserializer.Deserialize(entity, ref instance, ref reader);

        instances[index] = instance;

        identityIndices.TryAdd(entity, entityIndex);
    }
}

public struct GameDataEntityComponentDataDeserializerFactory<TValue, TDeserializer> : IEntityDataFactory<GameDataEntityComponentDataDeserializer<TValue, TDeserializer>>
    where TValue : unmanaged, IComponentData
    where TDeserializer : IGameDataEntityCompoentDeserializer<TValue>
{
    [ReadOnly]
    public EntityTypeHandle entityType;

    public DynamicComponentTypeHandle instanceType;

    public NativeParallelHashMap<Entity, int>.ParallelWriter identityIndices;

    public TDeserializer deserializer;

    public GameDataEntityComponentDataDeserializer<TValue, TDeserializer> Create(in ArchetypeChunk chunk, int unfilteredChunkIndex)
    {
        GameDataEntityComponentDataDeserializer<TValue, TDeserializer> deserializer;
        deserializer.entityArray = chunk.GetNativeArray(entityType);
        deserializer.instances = chunk.GetDynamicComponentDataArrayReinterpret<TValue>(ref instanceType, UnsafeUtility.SizeOf<TValue>());
        deserializer.identityIndices = identityIndices;
        deserializer.deserializer = this.deserializer;

        return deserializer;
    }
}

[BurstCompile]
public struct GameDataEntityComponentDataBuild<TValue, TBuilder> : IJob
    where TValue : unmanaged, IComponentData
    where TBuilder : IGameDataEntityCompoentBuilder<TValue>
{
    [ReadOnly]
    public EntityDataDeserializationStatusQuery.Container status;

    [ReadOnly]
    public SharedHashMap<int, Entity>.Reader identityEntities;

    [ReadOnly]
    public NativeParallelHashMap<Entity, int> identityIndices;

    public EntityStorageInfoLookup entityStorageInfoLookup;

    public DynamicComponentTypeHandle instanceType;

    public TBuilder builder;

    public void Execute()
    {
        if (status.value != EntityDataDeserializationStatus.Value.Complete)
            return;

        using (var keyValueArrays = identityIndices.GetKeyValueArrays(Allocator.Temp))
        {
            NativeArray<TValue> values;
            TValue value;
            EntityStorageInfo entityStorageInfo;
            Entity entity;
            int identityIndex, length = keyValueArrays.Keys.Length;
            for (int i = 0; i < length; ++i)
            {
                identityIndex = keyValueArrays.Values[i];

                entity = keyValueArrays.Keys[i];
                entityStorageInfo = entityStorageInfoLookup[entity];

                values = entityStorageInfo.Chunk.GetDynamicComponentDataArrayReinterpret<TValue>(ref instanceType, UnsafeUtility.SizeOf<TValue>());

                value = values[entityStorageInfo.IndexInChunk];

                builder.Set(ref value, identityIndex == -1 ? Entity.Null : identityEntities[identityIndex], entity);
                values[entityStorageInfo.IndexInChunk] = value;
            }
        }
    }
}

public struct GameDataEntityComponentDataSerializer<TValue, TSerializer> : IEntityDataSerializer
    where TValue : unmanaged, IComponentData
    where TSerializer : IGameDataEntityCompoentSerializer<TValue>
{
    [ReadOnly]
    public NativeArray<TValue> instances;
    [ReadOnly]
    public ComponentLookup<EntityDataIdentity> identities;

    public TSerializer serializer;

    public void Serialize(int index, in SharedHashMap<Hash128, int>.Reader entityIndices, ref EntityDataWriter writer)
    {
        var instance = instances[index];
        Entity entity = serializer.Get(instance);

        serializer.Serialize(
            instance,
            identities.HasComponent(entity) && entityIndices.TryGetValue(identities[entity].guid, out int entityIndex) ? entityIndex : -1,
            ref writer);
    }
}

public struct GameDataEntityComponentDataSerializerFactory<TValue, TSerializer> : IEntityDataFactory<GameDataEntityComponentDataSerializer<TValue, TSerializer>>
    where TValue : unmanaged, IComponentData
    where TSerializer : IGameDataEntityCompoentSerializer<TValue>
{
    [ReadOnly]
    public DynamicComponentTypeHandle instanceType;
    [ReadOnly]
    public ComponentLookup<EntityDataIdentity> identities;

    public TSerializer serializer;

    public GameDataEntityComponentDataSerializer<TValue, TSerializer> Create(in ArchetypeChunk chunk, int unfilteredChunkIndex)
    {
        GameDataEntityComponentDataSerializer<TValue, TSerializer> serializer;
        serializer.instances = chunk.GetDynamicComponentDataArrayReinterpret<TValue>(ref instanceType, UnsafeUtility.SizeOf<TValue>());
        serializer.identities = identities;
        serializer.serializer = this.serializer;

        return serializer;
    }
}

public struct GameDataEntityComponentDataSerializationSystemCore
{
    private ComponentLookup<EntityDataIdentity> __identities;
    private DynamicComponentTypeHandle __instanceType;
    private EntityDataSerializationSystemCore __core;

    public GameDataEntityComponentDataSerializationSystemCore(in TypeIndex typeIndex, ref SystemState state)
    {
        ComponentType componentType;
        componentType.TypeIndex = typeIndex;
        componentType.AccessModeType = ComponentType.AccessMode.ReadOnly;
        __identities = state.GetComponentLookup<EntityDataIdentity>(true);
        __instanceType = state.GetDynamicComponentTypeHandle(componentType);
        __core = new EntityDataSerializationSystemCore(componentType, ref state);
    }

    public void Dispose()
    {
        __core.Dispose();
    }

    [BurstCompile]
    public void Update<TValue, TSerializer>(ref TSerializer serializer, ref SystemState state)
        where TValue : unmanaged, IComponentData
        where TSerializer : IGameDataEntityCompoentSerializer<TValue>
    {
        GameDataEntityComponentDataSerializerFactory<TValue, TSerializer> serializerFactory;
        serializerFactory.identities = __identities.UpdateAsRef(ref state);
        serializerFactory.instanceType = __instanceType.UpdateAsRef(ref state);
        serializerFactory.serializer = serializer;

        __core.Update<GameDataEntityComponentDataSerializer<TValue, TSerializer>, GameDataEntityComponentDataSerializerFactory<TValue, TSerializer>>(ref serializerFactory, ref state);
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
        __core = EntityDataSerializationSystemCore.Create<T>(ref state);
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

public struct GameDataEntityComponentDataDeserializationSystemCore
{
    private EntityTypeHandle __entityType;
    private DynamicComponentTypeHandle __instanceType;
    private EntityStorageInfoLookup __entityStorageInfoLookup;

    private SharedHashMap<int, Entity> __identityEntities;
    private NativeParallelHashMap<Entity, int> __identityIndices;
    private EntityDataDeserializationSystemCore __core;
    private EntityDataDeserializationStatusQuery __statusQuery;

    public EntityQuery group => __core.group;

    public GameDataEntityComponentDataDeserializationSystemCore(in TypeIndex typeIndex, ref SystemState state)
    {
        ComponentType componentType;
        componentType.TypeIndex = typeIndex;
        componentType.AccessModeType = ComponentType.AccessMode.ReadWrite;

        __entityType = state.GetEntityTypeHandle();
        __instanceType = state.GetDynamicComponentTypeHandle(componentType);
        __entityStorageInfoLookup = state.GetEntityStorageInfoLookup();
        __identityEntities = state.WorldUnmanaged.GetExistingSystemUnmanaged<EntityDataDeserializationPresentationSystem>().identityEntities;
        __identityIndices = new NativeParallelHashMap<Entity, int>(1, Allocator.Persistent);
        __core = new EntityDataDeserializationSystemCore(componentType, ref state);
        __statusQuery = new EntityDataDeserializationStatusQuery(ref state, true);
    }

    public void Dispose()
    {
        __core.Dispose();

        __identityIndices.Dispose();
    }

    public void Update<TValue, TDeserializer, TBuilder>(
        ref TDeserializer deserializer, 
        ref TBuilder builder, 
        ref SystemState state, 
        out JobHandle deserializeJobHandle)
        where TValue : unmanaged, IComponentData
        where TDeserializer : IGameDataEntityCompoentDeserializer<TValue>
        where TBuilder : IGameDataEntityCompoentBuilder<TValue>
    {
        var status = __statusQuery.AsContainer(ref state);

        var entityCount = new NativeArray<int>(1, Allocator.TempJob, NativeArrayOptions.ClearMemory);
        var jobHandle = __core.group.CalculateEntityCountAsync(entityCount, state.Dependency);

        GameDataEntityComponentDataDeserializationRecapacity recapacity;
        recapacity.entityCount = entityCount;
        recapacity.status = status;
        recapacity.identityIndices = __identityIndices;
        state.Dependency = recapacity.ScheduleByRef(jobHandle);

        var instanceType = __instanceType.UpdateAsRef(ref state);

        GameDataEntityComponentDataDeserializerFactory<TValue, TDeserializer> factory;
        factory.entityType = __entityType.UpdateAsRef(ref state);
        factory.instanceType = instanceType;
        factory.identityIndices = __identityIndices.AsParallelWriter();
        factory.deserializer = deserializer;

        __core.Update<GameDataEntityComponentDataDeserializer<TValue, TDeserializer>, GameDataEntityComponentDataDeserializerFactory<TValue, TDeserializer>>(ref factory, ref state, true);

        deserializeJobHandle = state.Dependency;

        GameDataEntityComponentDataBuild<TValue, TBuilder> build;
        build.status = status;
        build.identityEntities = __identityEntities.reader;
        build.identityIndices = __identityIndices;
        build.entityStorageInfoLookup = __entityStorageInfoLookup.UpdateAsRef(ref state);
        build.instanceType = instanceType;
        build.builder = builder;

        ref var identityEntitiesJobManager = ref __identityEntities.lookupJobManager;

        jobHandle = JobHandle.CombineDependencies(deserializeJobHandle, identityEntitiesJobManager.readOnlyJobHandle);

        jobHandle = build.ScheduleByRef(jobHandle);

        identityEntitiesJobManager.AddReadOnlyDependency(jobHandle);

        state.Dependency = jobHandle;
    }
}

public struct GameDataEntityBufferDeserializationSystemCore<T> where T : unmanaged, IBufferElementData, IGameDataEntityCompoent
{
    public struct Deserializer : IEntityDataDeserializer
    {
        [ReadOnly]
        public NativeArray<Entity> entityArray;

        public Unity.Entities.LowLevel.Unsafe.UnsafeUntypedBufferAccessor instances;

        public NativeParallelMultiHashMap<Entity, GameDataEntityBufferDeserializationRecapacity.Child>.ParallelWriter children;

        public bool Fallback(int index)
        {
            return false;
        }

        public unsafe void Deserialize(int index, ref EntityDataReader reader)
        {
            //var instances = this.instances[index];
            GameDataEntityBufferDeserializationRecapacity.Child child;
            Entity entity = entityArray[index];
            int length = reader.Read<int>();

            instances.ResizeUninitialized(index, length);
            void* ptr = instances.GetUnsafePtr(index);
            for (int i = 0; i < length; ++i)
            {
                //child.index = reader.Read<int>();
                //child.entityIndex = reader.Read<int>();

                //children.Add(entity, child);
                child.index = i;
                child.identityIndex = UnsafeUtility.ArrayElementAsRef<T>(ptr, i).Deserialize(ref reader);

                children.Add(entity, child);
            }
        }
    }

    public struct DeserializerFactory : IEntityDataFactory<Deserializer>
    {
        [ReadOnly]
        public EntityTypeHandle entityType;

        public DynamicComponentTypeHandle instanceType;

        public NativeParallelMultiHashMap<Entity, GameDataEntityBufferDeserializationRecapacity.Child>.ParallelWriter children;

        public Deserializer Create(in ArchetypeChunk chunk, int unfilteredChunkIndex)
        {
            Deserializer deserializer;
            deserializer.entityArray = chunk.GetNativeArray(entityType);
            deserializer.instances = chunk.GetUntypedBufferAccessor(ref instanceType);
            deserializer.children = children;

            return deserializer;
        }
    }

    [BurstCompile]
    public struct Build : IJob
    {
        [ReadOnly]
        public EntityDataDeserializationStatusQuery.Container status;

        [ReadOnly]
        public SharedHashMap<int, Entity>.Reader identityEntities;

        [ReadOnly]
        public NativeParallelMultiHashMap<Entity, GameDataEntityBufferDeserializationRecapacity.Child> children;

        public BufferLookup<T> instances;

#if DEBUG
        [ReadOnly]
        public NativeList<Entity> entityArray;
#endif

        public void Execute()
        {
            if (status.value != EntityDataDeserializationStatus.Value.Complete)
                return;

            using (var keys = children.GetKeyArray(Allocator.Temp))
            {
                int length = keys.ConvertToUniqueArray();
                Entity entity;
                //T destination, temp;
                DynamicBuffer<T> instances;
                for (int i = 0; i < length; ++i)
                {
                    entity = keys[i];
                    instances = this.instances[entity];
                    foreach(var child in children.GetValuesForKey(entity))
                        instances.ElementAt(child.index).entity = identityEntities[child.identityIndex];
                    /*if (!this.results.HasBuffer(entity))
                        continue;

                    destination.index = source.index;
                    destination.entity = entity;

                    results = this.results[keyValueArrays.Keys[i]];
                    count = results.Length;
                    for (j = 0; j < count; ++j)
                    {
                        temp = results[j];
                        if (temp.index == destination.index || temp.entity == destination.entity)
                        {
                            UnityEngine.Debug.LogError($"The Same Index {source.index} In Child {destination.entity}");

                            break;
                        }
                    }

                    if (j == count)
                        results.Add(destination);*/
                }
            }

#if DEBUG
            foreach (var entity in entityArray)
            {
                foreach (var instance in instances[entity])
                {
                    if (instance.entity == Entity.Null)
                        Debug.LogError($"Error Container {entity}!");
                }
            }
#endif
        }
    }

    private EntityTypeHandle __entityType;
    private DynamicComponentTypeHandle __instanceType;
    private BufferLookup<T> __instances;
    private SharedHashMap<int, Entity> __identityEntities;
    private NativeParallelMultiHashMap<Entity, GameDataEntityBufferDeserializationRecapacity.Child> __children;
    private EntityDataDeserializationSystemCore __core;
    private EntityDataDeserializationStatusQuery __statusQuery;

    public GameDataEntityBufferDeserializationSystemCore(ref SystemState state)
    {
        __entityType = state.GetEntityTypeHandle();
        __instanceType = state.GetDynamicComponentTypeHandle(ComponentType.ReadWrite<T>());
        __instances = state.GetBufferLookup<T>();
        __identityEntities = state.WorldUnmanaged.GetExistingSystemUnmanaged<EntityDataDeserializationPresentationSystem>().identityEntities;
        __children = new NativeParallelMultiHashMap<Entity, GameDataEntityBufferDeserializationRecapacity.Child>(1, Allocator.Persistent);
        __core = EntityDataDeserializationSystemCore.Create<T>(ref state);
        __statusQuery = new EntityDataDeserializationStatusQuery(ref state, true);
    }

    public void Dispose()
    {
        __core.Dispose();

        __children.Dispose();
    }

    public void Update(ref SystemState state)
    {
        var count = new NativeArray<int>(1, Allocator.TempJob, NativeArrayOptions.ClearMemory);

        __core.Count(ref count, ref state);

        var status = __statusQuery.AsContainer(ref state);

        GameDataEntityBufferDeserializationRecapacity recapacity;
        recapacity.status = status;
        recapacity.count = count;
        recapacity.children = __children;

        state.Dependency = recapacity.ScheduleByRef(state.Dependency);

        DeserializerFactory factory;
        factory.entityType = __entityType.UpdateAsRef(ref state);
        factory.instanceType = __instanceType.UpdateAsRef(ref state);
        factory.children = __children.AsParallelWriter();

        __core.Update<Deserializer, DeserializerFactory>(ref factory, ref state, true);

        Build build;
        build.status = status;
        build.identityEntities = __identityEntities.reader;
        build.children = __children;
        build.instances = __instances.UpdateAsRef(ref state);
        ref var identityEntitiesJobManager = ref __identityEntities.lookupJobManager;

        var jobHandle = JobHandle.CombineDependencies(identityEntitiesJobManager.readOnlyJobHandle, state.Dependency);

#if DEBUG
        build.entityArray = __core.group.ToEntityListAsync(state.WorldUpdateAllocator, out var entityJobHandle);

        jobHandle = JobHandle.CombineDependencies(jobHandle, entityJobHandle);
#endif

        jobHandle = build.ScheduleByRef(jobHandle);

        identityEntitiesJobManager.AddReadOnlyDependency(jobHandle);

        state.Dependency = jobHandle;
    }
}