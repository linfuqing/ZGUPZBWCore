using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using ZG;

[assembly: RegisterGenericJobType(typeof(EntityDataIndexBufferInit<GameSoul, GameSoulTypeWrapper>))]

#region GameSoul
[assembly: RegisterGenericJobType(typeof(EntityDataComponentSerialize<GameDataSoulSerializationSystem.Serializer, GameDataSoulSerializationSystem.SerializerFactory>))]
[assembly: RegisterGenericJobType(typeof(EntityDataComponentDeserialize<GameDataSoulDeserializationSystem.Deserializer, GameDataSoulDeserializationSystem.DeserializerFactory>))]

[assembly: EntityDataSerialize(typeof(GameSoul), typeof(GameDataSoulSerializationSystem))]
[assembly: EntityDataDeserialize(typeof(GameSoul), typeof(GameDataSoulDeserializationSystem), (int)GameDataConstans.Version)]
#endregion

#region GameSoulIndex
[assembly: RegisterGenericJobType(typeof(EntityDataComponentSerialize<ComponentDataSerializationSystem<GameSoulIndex>.Serializer, ComponentDataSerializationSystem<GameSoulIndex>.SerializerFactory>))]
[assembly: RegisterGenericJobType(typeof(EntityDataComponentDeserialize<ComponentDataDeserializationSystem<GameSoulIndex>.Deserializer, ComponentDataDeserializationSystem<GameSoulIndex>.DeserializerFactory>))]
[assembly: EntityDataSerialize(typeof(GameSoulIndex))]
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
}

/*[DisableAutoCreation]
public partial class GameDataSoulTypeContainerSerializationSystem : EntityDataIndexContainerSerializationSystemBase
{
    private EntityQuery __group;

    public override NativeHashMap<int, int> indices => systemGroup.initializationSystem.typeIndices;

    protected override void OnCreate()
    {
        base.OnCreate();

        __group = GetEntityQuery(
            new EntityQueryDesc()
            {
                All = new ComponentType[]
                {
                    ComponentType.ReadOnly<GameSoul>(),
                    ComponentType.ReadOnly<EntityDataIdentity>(),
                    ComponentType.ReadOnly<EntityDataSerializable>()
                },
                Options = EntityQueryOptions.IncludeDisabled
            });
    }

    protected override JobHandle _Update(in JobHandle inputDeps)
    {
        GameSoulTypeWrapper wrapper;
        var initializationSystem = systemGroup.initializationSystem;
        var jobHandle = _ScheduleBuffer<GameSoul, GameSoulTypeWrapper>(
            __group, 
            systemGroup.types, 
            ref wrapper, 
            JobHandle.CombineDependencies(initializationSystem.readWriteJobHandle, inputDeps));
        initializationSystem.readWriteJobHandle = jobHandle;

        return jobHandle;
    }

    protected override NativeList<Hash128> _GetResultGuids()
    {
        return systemGroup.initializationSystem.typeGuids;
    }
}*/

[DisableAutoCreation, /*UpdateAfter(typeof(GameDataSoulTypeContainerSerializationSystem)), */UpdateAfter(typeof(GameDataLevelContainerSerializationSystem))]
public partial class GameDataSoulSerializationSystem : EntityDataSerializationComponentSystem<
    GameSoul,
    GameDataSoulSerializationSystem.Serializer,
    GameDataSoulSerializationSystem.SerializerFactory>
{
    public struct Serializer : IEntityDataSerializer
    {
        [ReadOnly]
        public NativeParallelHashMap<int, int> typeIndices;

        [ReadOnly]
        public NativeParallelHashMap<int, int> levelIndices;

        [ReadOnly]
        public BufferAccessor<GameSoul> instances;

        public void Serialize(int index, in NativeParallelHashMap<Hash128, int> entityIndices, ref EntityDataWriter writer)
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

                instance.data.type = typeIndices[instance.data.type];
                instance.data.levelIndex = levelIndices[instance.data.levelIndex];

                writer.Write(instance);
            }
        }
    }

    public struct SerializerFactory : IEntityDataFactory<Serializer>
    {
        [ReadOnly]
        public NativeParallelHashMap<int, int> typeIndices;

        [ReadOnly]
        public NativeParallelHashMap<int, int> levelIndices;

        [ReadOnly]
        public BufferTypeHandle<GameSoul> instanceType;

        public Serializer Create(in ArchetypeChunk chunk, int firstEntityIndex)
        {
            Serializer serializer;
            serializer.typeIndices = typeIndices;
            serializer.levelIndices = levelIndices;
            serializer.instances = chunk.GetBufferAccessor(ref instanceType);

            return serializer;
        }
    }

    private JobHandle __jobHandle;

    //private GameDataSoulTypeContainerSerializationSystem __typeSystem;
    private GameDataLevelContainerSerializationSystem __levelSystem;

    protected override void OnCreate()
    {
        base.OnCreate();

        var world = World;
        //__typeSystem = world.GetOrCreateSystem<GameDataSoulTypeContainerSerializationSystem>();
        __levelSystem = world.GetOrCreateSystemManaged<GameDataLevelContainerSerializationSystem>();

        var initializationSystem = world.GetOrCreateSystemManaged<EntityDataSerializationInitializationSystem>();
        initializationSystem.onUpdateTypes += __UpdateTypes;
    }

    private JobHandle __UpdateTypes(ref NativeList<Hash128> guids, ref NativeParallelHashMap<int, int> indices, JobHandle inputDeps)
    {
        GameSoulTypeWrapper wrapper;
        __jobHandle = EntityDataIndexBufferUtility<GameSoul, GameSoulTypeWrapper>.Schedule(
            group,
            systemGroup.types,
            GetBufferTypeHandle<GameSoul>(true),
            ref indices,
            ref guids,
            ref wrapper,
            JobHandle.CombineDependencies(group.GetDependency(), inputDeps));

        return __jobHandle;
    }

    protected override void OnUpdate()
    {
        Dependency = JobHandle.CombineDependencies(Dependency, __jobHandle);

        base.OnUpdate();

        var jobHandle = Dependency;

        systemGroup.initializationSystem.AddReadOnlyDependency(jobHandle);

        //__typeSystem.AddReadOnlyDependency(jobHandle);

        __levelSystem.AddReadOnlyDependency(jobHandle);
    }

    protected override SerializerFactory _Get(ref JobHandle jobHandle)
    {
        var initializationSystem = systemGroup.initializationSystem;
        jobHandle = JobHandle.CombineDependencies(jobHandle, initializationSystem.readOnlyJobHandle, __levelSystem.readOnlyJobHandle);

        SerializerFactory serializerFactory;
        serializerFactory.typeIndices = initializationSystem.typeIndices;
        serializerFactory.levelIndices = __levelSystem.indices;
        serializerFactory.instanceType = GetBufferTypeHandle<GameSoul>(true);

        return serializerFactory;
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
        public NativeArray<Hash128> typeInputs;

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
        public NativeArray<Hash128> typeInputs;

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