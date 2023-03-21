using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using ZG;

#region GameSoul_2
[assembly: RegisterGenericJobType(typeof(EntityDataComponentDeserialize<GameDataSoulDeserializationSystem_2.Deserializer, GameDataSoulDeserializationSystem_2.DeserializerFactory>))]

[assembly: EntityDataDeserialize(typeof(GameSoul), typeof(GameDataSoulDeserializationSystem_2), 2)]
#endregion

public struct GameSoulData_3
{
    public int type;
    public int variant;
    public int levelIndex;
    public float power;
    public float exp;
    public FixedString128Bytes nickname;
}

public struct GameSoul_2 : IBufferElementData
{
    public int index;
    public long ticks;
    public GameSoulData_3 data;

    public static implicit operator GameSoul(in GameSoul_2 value)
    {
        GameSoul result;
        result.index = value.index;
        result.ticks = value.ticks;
        result.data.type = value.data.type;
        result.data.variant = value.data.variant;
        result.data.levelIndex = value.data.levelIndex;
        result.data.power = value.data.power;
        result.data.exp = value.data.exp;

        var nickname = value.data.nickname;
        if (nickname.Length > FixedString32Bytes.UTF8MaxLengthInBytes && !nickname.TryResize(FixedString32Bytes.UTF8MaxLengthInBytes, NativeArrayOptions.UninitializedMemory))
            result.data.nickname = default;
        else
            result.data.nickname = new FixedString32Bytes(nickname);

        return result;
    }
}

[DisableAutoCreation, /*UpdateAfter(typeof(GameDataSoulTypeContainerDeserializationSystem)), */UpdateAfter(typeof(GameDataLevelContainerDeserializationSystem))]
public partial class GameDataSoulDeserializationSystem_2 : EntityDataDeserializationComponentSystem<
    GameSoul,
    GameDataSoulDeserializationSystem_2.Deserializer,
    GameDataSoulDeserializationSystem_2.DeserializerFactory>
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
            int numInstances = reader.Read<int>();

            var instances = this.instances[index];

            instances.ResizeUninitialized(numInstances);

            var origins = reader.ReadArray<GameSoul_2>(numInstances);

            int type;
            GameSoul instance;
            for (int i = 0; i < numInstances; ++i)
            {
                instance = origins[i];

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

        public Deserializer Create(in ArchetypeChunk chunk, int firstEntityIndex)
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