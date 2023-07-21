using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using ZG;

[assembly: RegisterGenericJobType(typeof(EntityDataIndexBufferInit<GameSoul, GameSoulTypeWrapper>))]

#region GameSoul
[assembly: RegisterGenericJobType(typeof(EntityDataComponentSerialize<GameDataSoulSerializationSystem.Serializer, GameDataSoulSerializationSystem.SerializerFactory>))]
[assembly: RegisterGenericJobType(typeof(EntityDataComponentDeserialize<GameDataSoulDeserializationSystem.Deserializer, GameDataSoulDeserializationSystem.DeserializerFactory>))]

//[assembly: EntityDataSerialize(typeof(GameSoul), typeof(GameDataSoulSerializationSystem))]
[assembly: EntityDataDeserialize(typeof(GameSoul), typeof(GameDataSoulDeserializationSystem), (int)GameDataConstans.Version)]
#endregion

#region GameSoulIndex
//[assembly: RegisterGenericJobType(typeof(EntityDataComponentSerialize<ComponentDataSerializationSystem<GameSoulIndex>.Serializer, ComponentDataSerializationSystem<GameSoulIndex>.SerializerFactory>))]
[assembly: RegisterGenericJobType(typeof(EntityDataComponentDeserialize<ComponentDataDeserializationSystem<GameSoulIndex>.Deserializer, ComponentDataDeserializationSystem<GameSoulIndex>.DeserializerFactory>))]
//[assembly: EntityDataSerialize(typeof(GameSoulIndex))]
[assembly: EntityDataDeserialize(typeof(GameSoulIndex), (int)GameDataConstans.Version)]
#endregion

public struct GameSoulTypeWrapper : IEntityDataIndexReadWriteWrapper<GameSoul>
{
    public bool TryGet(in GameSoul data, out int index)
    {
        index = data.data.type;

        return data.data.type != -1;
    }

    public void Invail(ref GameSoul data)
    {
        data.data.type = -1;
    }

    public void Set(ref GameSoul data, int index)
    {
        data.data.type = index;
    }

    public void Serialize(ref EntityDataWriter writer, in GameSoul data, in SharedHashMap<int, int>.Reader guidIndices)
    {
        EntityDataIndexReadWriteWrapperUtility.Serialize(ref this, ref writer, data, guidIndices);
    }

    public GameSoul Deserialize(ref EntityDataReader reader, in NativeArray<int>.ReadOnly indices)
    {
        return EntityDataIndexReadWriteWrapperUtility.Deserialize<GameSoul, GameSoulTypeWrapper>(ref this, ref reader, indices);
    }
}

[BurstCompile,
    CreateAfter(typeof(EntityDataSerializationInitializationSystem)),
    UpdateInGroup(typeof(EntityDataSerializationSystemGroup), OrderFirst = true),
    UpdateBefore(typeof(EntityDataSerializationTypeGUIDSystem)), 
    UpdateAfter(typeof(EntityDataSerializationInitializationSystem))]
public partial struct GameDataSoulSerializationInitSystem : ISystem
{
    private EntityQuery __group;
    private EntityQuery __commonGroup;
    private BufferTypeHandle<GameSoul> __instanceType;

    private SharedList<Hash128> __typeGUIDs;
    private SharedHashMap<int, int> __typeGUIDIndices;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __group = builder
                    .WithAll<GameSoul, EntityDataIdentity, EntityDataSerializable>()
                    .WithOptions(EntityQueryOptions.IncludeDisabledEntities)
                    .Build(ref state);

        __commonGroup = EntityDataCommon.GetEntityQuery(ref state);

        __instanceType = state.GetBufferTypeHandle<GameSoul>(true);

        var world = state.WorldUnmanaged;

        ref var system = ref world.GetExistingSystemUnmanaged<EntityDataSerializationInitializationSystem>();

        __typeGUIDs = system.typeGUIDs;
        __typeGUIDIndices = system.typeGUIDIndices;
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {

    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var typeGUIDIndicesWriter = __typeGUIDIndices.writer;
        var typeGUIDsWriter = __typeGUIDs.writer;
        ref var typeGUIDIndicesJobManager = ref __typeGUIDIndices.lookupJobManager;
        ref var typeGUIDsJobManager = ref __typeGUIDs.lookupJobManager;

        GameSoulTypeWrapper wrapper;
        var jobHandle = EntityDataIndexBufferUtility<GameSoul, GameSoulTypeWrapper>.Schedule(
            __group,
            __commonGroup.GetSingleton<EntityDataCommon>().typesGUIDs,
            __instanceType.UpdateAsRef(ref state),
            ref typeGUIDIndicesWriter,
            ref typeGUIDsWriter,
            ref wrapper,
            JobHandle.CombineDependencies(state.Dependency, typeGUIDIndicesJobManager.readWriteJobHandle, typeGUIDsJobManager.readWriteJobHandle));

        typeGUIDIndicesJobManager.readWriteJobHandle = jobHandle;
        typeGUIDsJobManager.readWriteJobHandle = jobHandle;

        state.Dependency = jobHandle;
    }

}

//[DisableAutoCreation, /*UpdateAfter(typeof(GameDataSoulTypeContainerSerializationSystem)), */UpdateAfter(typeof(GameDataLevelContainerSerializationSystem))]

[BurstCompile,
    EntityDataSerializationSystem(typeof(GameSoul)),
    CreateAfter(typeof(GameDataLevelContainerSerializationSystem)),
    CreateAfter(typeof(GameDataSoulSerializationInitSystem)),
    UpdateInGroup(typeof(EntityDataSerializationSystemGroup)), AutoCreateIn("Server"),
    UpdateAfter(typeof(GameDataLevelContainerSerializationSystem))]
public partial struct GameDataSoulSerializationSystem : ISystem
{
    public struct Serializer : IEntityDataSerializer
    {
        [ReadOnly]
        public SharedHashMap<int, int>.Reader typeGUIDIndices;

        [ReadOnly]
        public SharedHashMap<int, int>.Reader levelGUIDIndices;

        [ReadOnly]
        public BufferAccessor<GameSoul> instances;

        public void Serialize(int index, in SharedHashMap<Hash128, int>.Reader entityIndices, ref EntityDataWriter writer)
        {
            var instances = this.instances[index];
            GameSoul instance;
            int numInstances = instances.Length, activeCount = 0;
            for(int i = 0; i < numInstances; ++i)
            {
                if (instances[i].data.type == -1)
                    continue;

                ++activeCount;
            }

            writer.Write(activeCount);
            for (int i = 0; i < numInstances; ++i)
            {
                instance = instances[i];
                if (instance.data.type == -1)
                    continue;

                instance.data.type = typeGUIDIndices[instance.data.type];
                instance.data.levelIndex = levelGUIDIndices[instance.data.levelIndex];

                writer.Write(instance);
            }
        }
    }

    public struct SerializerFactory : IEntityDataFactory<Serializer>
    {
        [ReadOnly]
        public SharedHashMap<int, int>.Reader typeGUIDIndices;

        [ReadOnly]
        public SharedHashMap<int, int>.Reader levelGUIDIndices;

        [ReadOnly]
        public BufferTypeHandle<GameSoul> instanceType;

        public Serializer Create(in ArchetypeChunk chunk, int unfilteredChunkIndex)
        {
            Serializer serializer;
            serializer.typeGUIDIndices = typeGUIDIndices;
            serializer.levelGUIDIndices = levelGUIDIndices;
            serializer.instances = chunk.GetBufferAccessor(ref instanceType);

            return serializer;
        }
    }

    private BufferTypeHandle<GameSoul> __instanceType;
    private SharedHashMap<int, int> __typeGUIDIndices;
    private SharedHashMap<int, int> __levelGUIDIndices;
    private EntityDataSerializationSystemCore __core;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __instanceType = state.GetBufferTypeHandle<GameSoul>(true);

        var world = state.WorldUnmanaged;
        __typeGUIDIndices = world.GetExistingSystemUnmanaged<EntityDataSerializationInitializationSystem>().typeGUIDIndices;
        __levelGUIDIndices = world.GetExistingSystemUnmanaged<GameDataLevelContainerSerializationSystem>().guidIndices;

        __core = EntityDataSerializationSystemCore.Create<GameSoul>(ref state);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        __core.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        ref var typeGUIDIndicesJobManager = ref __typeGUIDIndices.lookupJobManager;
        ref var levelGUIDIndicesJobManager = ref __levelGUIDIndices.lookupJobManager;

        SerializerFactory serializerFactory;
        serializerFactory.typeGUIDIndices = __typeGUIDIndices.reader;
        serializerFactory.levelGUIDIndices = __levelGUIDIndices.reader;
        serializerFactory.instanceType = __instanceType.UpdateAsRef(ref state);

        state.Dependency = JobHandle.CombineDependencies(state.Dependency, typeGUIDIndicesJobManager.readOnlyJobHandle, levelGUIDIndicesJobManager.readOnlyJobHandle);

        __core.Update<Serializer, SerializerFactory>(ref serializerFactory, ref state);

        var jobHandle = state.Dependency;

        typeGUIDIndicesJobManager.AddReadOnlyDependency(jobHandle);

        levelGUIDIndicesJobManager.AddReadOnlyDependency(jobHandle);
    }
}

[BurstCompile,
    EntityDataSerializationSystem(typeof(GameSoulIndex)),
    CreateAfter(typeof(EntityDataSerializationInitializationSystem)),
    UpdateInGroup(typeof(EntityDataSerializationSystemGroup)), AutoCreateIn("Server")]
public partial struct GameDataSoulIndexSerializationSystem : ISystem
{
    private EntityDataSerializationSystemCoreEx __core;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __core = EntityDataSerializationSystemCoreEx.Create<GameSoulIndex>(ref state);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        __core.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        __core.Update(ref state);
    }
}

/*[DisableAutoCreation]
public partial class GameDataSoulTypeContainerDeserializationSystem : EntityDataIndexContainerDeserializationSystem
{
    protected override NativeArray<Hash128> _GetGuids()
    {
        return systemGroup.types;
    }
}*/

[DisableAutoCreation, /*UpdateAfter(typeof(GameDataSoulTypeContainerDeserializationSystem)), */UpdateAfter(typeof(GameDataLevelContainerDeserializationSystem))]
public partial class GameDataSoulDeserializationSystem : EntityDataDeserializationComponentSystem<
    GameSoul,
    GameDataSoulDeserializationSystem.Deserializer,
    GameDataSoulDeserializationSystem.DeserializerFactory>
{
    public struct Deserializer : IEntityDataDeserializer
    {
        [ReadOnly]
        public NativeArray<int> levelIndices;

        [ReadOnly]
        public NativeArray<Hash128>.ReadOnly typeInputs;

        [ReadOnly]
        public NativeArray<Hash128> typeOuputs;

        [ReadOnly]
        public NativeParallelHashMap<int, int> typeIndices;

        public BufferAccessor<GameSoul> instances;

        public void Deserialize(int index, ref EntityDataReader reader)
        {
            var instances = this.instances[index];

            int numInstances = reader.Read<int>();
            instances.CopyFrom(reader.ReadArray<GameSoul>(numInstances));

            int type;
            GameSoul instance;
            for (int i = 0; i < numInstances; ++i)
            {
                instance = instances[i];
                if (typeIndices.TryGetValue(instance.data.type, out type))
                    instance.data.type = type;
                else
                    instance.data.type = typeInputs.IndexOf<Hash128, Hash128>(typeOuputs[instance.data.type]);

                instance.data.levelIndex = levelIndices[instance.data.levelIndex];
                instances[i] = instance;
            }
        }
    }

    public struct DeserializerFactory : IEntityDataFactory<Deserializer>
    {
        [ReadOnly]
        public NativeArray<int> levelIndices;

        [ReadOnly]
        public NativeArray<Hash128>.ReadOnly typeInputs;

        [ReadOnly]
        public NativeArray<Hash128> typeOuputs;

        [ReadOnly]
        public NativeParallelHashMap<int, int> typeIndices;

        public BufferTypeHandle<GameSoul> instanceType;

        public Deserializer Create(in ArchetypeChunk chunk, int unfilteredChunkIndex)
        {
            Deserializer deserializer;
            deserializer.levelIndices = levelIndices;
            deserializer.typeInputs = typeInputs;
            deserializer.typeOuputs = typeOuputs;
            deserializer.typeIndices = typeIndices;
            deserializer.instances = chunk.GetBufferAccessor(ref instanceType);

            return deserializer;
        }
    }

    //private GameDataSoulTypeContainerDeserializationSystem __typeSystem;
    private GameDataLevelContainerDeserializationSystem __levelSystem;

    protected override void OnCreate()
    {
        base.OnCreate();

        var world = World;
        //__typeSystem = world.GetOrCreateSystem<GameDataSoulTypeContainerDeserializationSystem>();
        __levelSystem = world.GetOrCreateSystemManaged<GameDataLevelContainerDeserializationSystem>();
    }

    protected override void OnUpdate()
    {
        base.OnUpdate();

        //__typeSystem.AddReadOnlyDependency(jobHandle);
        __levelSystem.AddReadOnlyDependency(Dependency);

    }
    protected override DeserializerFactory _Get(ref JobHandle jobHandle)
    {
        jobHandle = JobHandle.CombineDependencies(jobHandle, /*__typeSystem.readOnlyJobHandle, */__levelSystem.readOnlyJobHandle);

        var initializationSystem = systemGroup.initializationSystem;

        DeserializerFactory deserializerFactory;
        deserializerFactory.levelIndices = __levelSystem.indices;
        deserializerFactory.typeInputs = systemGroup.types;
        deserializerFactory.typeOuputs = initializationSystem.types;
        deserializerFactory.typeIndices = initializationSystem.typeIndices;//__typeSystem.indices;
        deserializerFactory.instanceType = GetBufferTypeHandle<GameSoul>();
        return deserializerFactory;
    }
}