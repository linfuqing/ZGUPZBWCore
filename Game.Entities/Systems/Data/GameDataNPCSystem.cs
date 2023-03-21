using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using ZG;

#region GameLevelManager
[assembly: RegisterGenericJobType(typeof(EntityDataIndexBufferInit<GameNPC, GameNPCWrapper>))]

[assembly: RegisterGenericJobType(typeof(EntityDataContainerSerialize<GameDataNPCContainerSerializationSystem.Serializer>))]
[assembly: RegisterGenericJobType(typeof(EntityDataContainerDeserialize<GameDataNPCContainerDeserializationSystem.Deserializer>))]

[assembly: EntityDataSerialize(typeof(GameNPCManager), typeof(GameDataNPCContainerSerializationSystem))]
[assembly: EntityDataDeserialize(typeof(GameNPCManager), typeof(GameDataNPCContainerDeserializationSystem), (int)GameDataConstans.Version)]
#endregion

#region GameNPCStageManager
[assembly: RegisterGenericJobType(typeof(EntityDataIndexBufferInit<GameNPC, GameNPCStageWrapper>))]

[assembly: RegisterGenericJobType(typeof(EntityDataContainerSerialize<GameDataNPCStageContainerSerializationSystem.Serializer>))]
[assembly: RegisterGenericJobType(typeof(EntityDataContainerDeserialize<GameDataNPCStageContainerDeserializationSystem.Deserializer>))]

[assembly: EntityDataSerialize(typeof(GameNPCStageManager), typeof(GameDataNPCStageContainerSerializationSystem))]
[assembly: EntityDataDeserialize(typeof(GameNPCStageManager), typeof(GameDataNPCStageContainerDeserializationSystem), (int)GameDataConstans.Version)]
#endregion

#region GameNPC
[assembly: RegisterGenericJobType(typeof(EntityDataComponentSerialize<GameDataNPCSerializationSystem.Serializer, GameDataNPCSerializationSystem.SerializerFactory>))]
[assembly: RegisterGenericJobType(typeof(EntityDataComponentDeserialize<GameDataNPCDeserializationSystem.Deserializer, GameDataNPCDeserializationSystem.DeserializerFactory>))]
[assembly: EntityDataSerialize(typeof(GameNPC), typeof(GameDataNPCSerializationSystem))]
[assembly: EntityDataDeserialize(typeof(GameNPC), typeof(GameDataNPCDeserializationSystem), (int)GameDataConstans.Version)]
#endregion


public struct GameNPCManager
{

}

public struct GameNPCStageManager
{

}

[Serializable]
public struct GameNPC : IBufferElementData
{
    public int index;
    public int stage;
}

public struct GameNPCWrapper : IEntityDataIndexReadOnlyWrapper<GameNPC>
{
    public bool TryGet(in GameNPC data, out int index)
    {
        index = data.index;

        return data.index != -1;
    }
}

public struct GameNPCStageWrapper : IEntityDataIndexReadOnlyWrapper<GameNPC>
{
    public bool TryGet(in GameNPC data, out int index)
    {
        index = data.stage;

        return data.stage != -1;
    }
}

[DisableAutoCreation]
public partial class GameDataNPCSystem : SystemBase
{
    public NativeArray<Hash128> npcs
    {
        get;

        private set;
    }

    public NativeArray<Hash128> stages
    {
        get;

        private set;
    }

    public void Create(Hash128[] npcs, Hash128[] stages)
    {
        this.npcs = new NativeArray<Hash128>(npcs, Allocator.Persistent);
        this.stages = new NativeArray<Hash128>(stages, Allocator.Persistent);
    }

    protected override void OnDestroy()
    {
        if (npcs.IsCreated)
            npcs.Dispose();

        if (stages.IsCreated)
            stages.Dispose();

        base.OnDestroy();
    }

    protected override void OnUpdate()
    {
        throw new NotImplementedException();
    }
}

[DisableAutoCreation]
public partial class GameDataNPCContainerSerializationSystem : EntityDataIndexBufferContainerSerializationSystem<GameNPC, GameNPCWrapper>
{
    private GameDataNPCSystem __npcSystem;
    private GameNPCWrapper __wrapper;

    protected override void OnCreate()
    {
        base.OnCreate();

        __npcSystem = World.GetOrCreateSystemManaged<GameDataNPCSystem>();
    }

    protected override NativeArray<Hash128> _GetGuids() => __npcSystem.npcs;

    protected override ref GameNPCWrapper _GetWrapper() => ref __wrapper;
}

[DisableAutoCreation]
public partial class GameDataNPCStageContainerSerializationSystem : EntityDataIndexBufferContainerSerializationSystem<GameNPC, GameNPCStageWrapper>
{
    private GameDataNPCSystem __npcSystem;
    private GameNPCStageWrapper __wrapper;

    protected override void OnCreate()
    {
        base.OnCreate();

        __npcSystem = World.GetOrCreateSystemManaged<GameDataNPCSystem>();
    }

    protected override NativeArray<Hash128> _GetGuids() => __npcSystem.stages;

    protected override ref GameNPCStageWrapper _GetWrapper() => ref __wrapper;
}


[DisableAutoCreation, UpdateAfter(typeof(GameDataNPCContainerSerializationSystem)), UpdateAfter(typeof(GameDataNPCStageContainerSerializationSystem))]
public partial class GameDataNPCSerializationSystem : EntityDataSerializationComponentSystem<
        GameNPC,
        GameDataNPCSerializationSystem.Serializer,
        GameDataNPCSerializationSystem.SerializerFactory>
{
    public struct Serializer : IEntityDataSerializer
    {
        [ReadOnly]
        public NativeParallelHashMap<int, int> indices;
        [ReadOnly]
        public NativeParallelHashMap<int, int> stages;
        [ReadOnly]
        public BufferAccessor<GameNPC> instances;

        public void Serialize(int index, in NativeParallelHashMap<Hash128, int> entityIndices, ref EntityDataWriter writer)
        {
            var instances = this.instances[index].ToNativeArray(Allocator.Temp);

            GameNPC instance;
            int length = instances.Length;
            for (int i = 0; i < length; ++i)
            {
                instance = instances[i];

                instance.index = indices[instance.index];
                instance.stage = stages[instance.stage];

                instances[i] = instance;
            }

            writer.Write(length);
            writer.Write(instances);

            instances.Dispose();
        }
    }

    public struct SerializerFactory : IEntityDataFactory<Serializer>
    {
        [ReadOnly]
        public NativeParallelHashMap<int, int> indices;
        [ReadOnly]
        public NativeParallelHashMap<int, int> stages;
        [ReadOnly]
        public BufferTypeHandle<GameNPC> instanceType;

        public Serializer Create(in ArchetypeChunk chunk, int firstEntityIndex)
        {
            Serializer serializer;
            serializer.indices = indices;
            serializer.stages = stages;
            serializer.instances = chunk.GetBufferAccessor(ref instanceType);

            return serializer;
        }
    }

    private GameDataNPCContainerSerializationSystem __containerSystem;
    private GameDataNPCStageContainerSerializationSystem __stageContainerSystem;

    protected override void OnCreate()
    {
        base.OnCreate();

        World world = World;
        __containerSystem = world.GetOrCreateSystemManaged<GameDataNPCContainerSerializationSystem>();
        __stageContainerSystem = world.GetOrCreateSystemManaged<GameDataNPCStageContainerSerializationSystem>();
    }

    protected override SerializerFactory _Get(ref JobHandle jobHandle)
    {
        jobHandle = JobHandle.CombineDependencies(jobHandle, __containerSystem.readOnlyJobHandle, __stageContainerSystem.readOnlyJobHandle);

        SerializerFactory serializerFactory;
        serializerFactory.indices = __containerSystem.indices;
        serializerFactory.stages = __stageContainerSystem.indices;
        serializerFactory.instanceType = GetBufferTypeHandle<GameNPC>(true);

        return serializerFactory;
    }
}

[DisableAutoCreation]
public partial class GameDataNPCContainerDeserializationSystem : EntityDataIndexContainerDeserializationSystem
{
    private GameDataNPCSystem __npcSystem;

    protected override void OnCreate()
    {
        base.OnCreate();

        __npcSystem = World.GetOrCreateSystemManaged<GameDataNPCSystem>();
    }

    protected override NativeArray<Hash128> _GetGuids()
    {
        return __npcSystem.npcs;
    }
}

[DisableAutoCreation]
public partial class GameDataNPCStageContainerDeserializationSystem : EntityDataIndexContainerDeserializationSystem
{
    private GameDataNPCSystem __npcSystem;

    protected override void OnCreate()
    {
        base.OnCreate();

        __npcSystem = World.GetOrCreateSystemManaged<GameDataNPCSystem>();
    }

    protected override NativeArray<Hash128> _GetGuids()
    {
        return __npcSystem.stages;
    }
}

[DisableAutoCreation, UpdateAfter(typeof(GameDataNPCContainerDeserializationSystem)), UpdateAfter(typeof(GameDataNPCStageContainerDeserializationSystem))]
public partial class GameDataNPCDeserializationSystem : EntityDataDeserializationComponentSystem<
        GameNPC,
        GameDataNPCDeserializationSystem.Deserializer,
        GameDataNPCDeserializationSystem.DeserializerFactory>
{
    public struct Deserializer : IEntityDataDeserializer
    {
        [ReadOnly]
        public NativeArray<int> indices;
        [ReadOnly]
        public NativeArray<int> stages;

        public BufferAccessor<GameNPC> instances;

        public void Deserialize(int index, ref EntityDataReader reader)
        {
            int length = reader.Read<int>();
            var sources = reader.ReadArray<GameNPC>(length);
            var destinations = instances[index];
            destinations.CopyFrom(sources);

            int count = 0, numIndices = indices.Length;
            GameNPC instance;
            for (int i = 0; i < length; ++i)
            {
                instance = destinations[i];
                if(instance.index < 0 || instance.index >= numIndices)
                {
                    UnityEngine.Debug.LogError($"NPC Index {instance.index} Invail.");

                    continue;
                }

                instance.index = indices[instance.index];
                instance.stage = stages[instance.stage];

                destinations[count++] = instance;
            }

            destinations.ResizeUninitialized(count);
        }
    }

    public struct DeserializerFactory : IEntityDataFactory<Deserializer>
    {
        [ReadOnly]
        public NativeArray<int> indices;

        [ReadOnly]
        public NativeArray<int> stages;

        public BufferTypeHandle<GameNPC> instanceType;

        public Deserializer Create(in ArchetypeChunk chunk, int firstEntityIndex)
        {
            Deserializer deserializer;
            deserializer.indices = indices;
            deserializer.stages = stages;
            deserializer.instances = chunk.GetBufferAccessor(ref instanceType);

            return deserializer;
        }
    }

    private GameDataNPCContainerDeserializationSystem __containerSystem;
    private GameDataNPCStageContainerDeserializationSystem __stageContainerSystem;

    protected override void OnCreate()
    {
        base.OnCreate();

        World world = World;
        __containerSystem = world.GetOrCreateSystemManaged<GameDataNPCContainerDeserializationSystem>();
        __stageContainerSystem = world.GetOrCreateSystemManaged<GameDataNPCStageContainerDeserializationSystem>();
    }

    protected override DeserializerFactory _Get(ref JobHandle jobHandle)
    {
        jobHandle = JobHandle.CombineDependencies(jobHandle, __containerSystem.readOnlyJobHandle, __stageContainerSystem.readOnlyJobHandle);

        DeserializerFactory deserializerFactory;
        deserializerFactory.indices = __containerSystem.indices;
        deserializerFactory.stages = __stageContainerSystem.indices;
        deserializerFactory.instanceType = GetBufferTypeHandle<GameNPC>();

        return deserializerFactory;
    }
}