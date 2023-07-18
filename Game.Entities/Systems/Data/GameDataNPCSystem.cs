using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using ZG;

#region GameNPCManager
[assembly: RegisterGenericJobType(typeof(EntityDataIndexBufferInit<GameNPC, GameNPCWrapper>))]

//[assembly: RegisterGenericJobType(typeof(EntityDataContainerSerialize<GameDataNPCContainerSerializationSystem.Serializer>))]
[assembly: RegisterGenericJobType(typeof(EntityDataContainerDeserialize<GameDataNPCContainerDeserializationSystem.Deserializer>))]

//[assembly: EntityDataSerialize(typeof(GameNPCManager), typeof(GameDataNPCContainerSerializationSystem))]
[assembly: EntityDataDeserialize(typeof(GameNPCManager), typeof(GameDataNPCContainerDeserializationSystem), (int)GameDataConstans.Version)]
#endregion

#region GameNPCStageManager
[assembly: RegisterGenericJobType(typeof(EntityDataIndexBufferInit<GameNPC, GameNPCStageWrapper>))]

//[assembly: RegisterGenericJobType(typeof(EntityDataContainerSerialize<GameDataNPCStageContainerSerializationSystem.Serializer>))]
[assembly: RegisterGenericJobType(typeof(EntityDataContainerDeserialize<GameDataNPCStageContainerDeserializationSystem.Deserializer>))]

//[assembly: EntityDataSerialize(typeof(GameNPCStageManager), typeof(GameDataNPCStageContainerSerializationSystem))]
[assembly: EntityDataDeserialize(typeof(GameNPCStageManager), typeof(GameDataNPCStageContainerDeserializationSystem), (int)GameDataConstans.Version)]
#endregion

#region GameNPC
[assembly: RegisterGenericJobType(typeof(EntityDataComponentSerialize<GameDataNPCSerializationSystem.Serializer, GameDataNPCSerializationSystem.SerializerFactory>))]
[assembly: RegisterGenericJobType(typeof(EntityDataComponentDeserialize<GameDataNPCDeserializationSystem.Deserializer, GameDataNPCDeserializationSystem.DeserializerFactory>))]
//[assembly: EntityDataSerialize(typeof(GameNPC), typeof(GameDataNPCSerializationSystem))]
[assembly: EntityDataDeserialize(typeof(GameNPC), typeof(GameDataNPCDeserializationSystem), (int)GameDataConstans.Version)]
#endregion


public struct GameNPCManager
{

}

public struct GameNPCStageManager
{

}

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

public struct GameDataNPCContainer : IComponentData
{
    public NativeArray<Hash128>.ReadOnly guids;
}

public struct GameDataNPCStageContainer : IComponentData
{
    public NativeArray<Hash128>.ReadOnly guids;
}

[BurstCompile,
    EntityDataSerializationSystem(typeof(GameNPCManager)),
    CreateAfter(typeof(EntityDataSerializationInitializationSystem)),
    UpdateInGroup(typeof(EntityDataSerializationSystemGroup)), AutoCreateIn("Server")]
public partial struct GameDataNPCContainerSerializationSystem : ISystem, IEntityDataSerializationIndexContainerSystem
{
    private EntityDataSerializationIndexContainerBufferSystemCore<GameNPC> __core;

    public SharedHashMap<int, int> guidIndices => __core.guidIndices;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __core = new EntityDataSerializationIndexContainerBufferSystemCore<GameNPC>(ref state);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        __core.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        GameNPCWrapper wrapper;
        var guids = SystemAPI.GetSingleton<GameDataNPCContainer>().guids;

        __core.Update(guids, ref wrapper, ref state);
    }
}

[BurstCompile,
    EntityDataSerializationSystem(typeof(GameNPCStageManager)),
    CreateAfter(typeof(EntityDataSerializationInitializationSystem)),
    UpdateInGroup(typeof(EntityDataSerializationSystemGroup)), AutoCreateIn("Server")]
public partial struct GameDataNPCStageContainerSerializationSystem : ISystem, IEntityDataSerializationIndexContainerSystem
{
    private EntityDataSerializationIndexContainerBufferSystemCore<GameNPC> __core;

    public SharedHashMap<int, int> guidIndices => __core.guidIndices;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __core = new EntityDataSerializationIndexContainerBufferSystemCore<GameNPC>(ref state);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        __core.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        GameNPCStageWrapper wrapper;
        var guids = SystemAPI.GetSingleton<GameDataNPCStageContainer>().guids;

        __core.Update(guids, ref wrapper, ref state);
    }
}


[BurstCompile,
    EntityDataSerializationSystem(typeof(GameNPC)),
    CreateAfter(typeof(GameDataNPCContainerSerializationSystem)),
    CreateAfter(typeof(GameDataNPCStageContainerSerializationSystem)),
    UpdateInGroup(typeof(EntityDataSerializationSystemGroup)), AutoCreateIn("Server"),
    UpdateAfter(typeof(GameDataNPCContainerSerializationSystem)), 
    UpdateAfter(typeof(GameDataNPCStageContainerSerializationSystem))]
public partial struct GameDataNPCSerializationSystem : ISystem
{
    public struct Serializer : IEntityDataSerializer
    {
        [ReadOnly]
        public SharedHashMap<int, int>.Reader guidIndices;
        [ReadOnly]
        public SharedHashMap<int, int>.Reader stageGUIDIndices;
        [ReadOnly]
        public BufferAccessor<GameNPC> instances;

        public void Serialize(int index, in SharedHashMap<Hash128, int>.Reader entityIndices, ref EntityDataWriter writer)
        {
            var instances = this.instances[index].ToNativeArray(Allocator.Temp);

            GameNPC instance;
            int length = instances.Length;
            for (int i = 0; i < length; ++i)
            {
                instance = instances[i];

                instance.index = guidIndices[instance.index];
                instance.stage = stageGUIDIndices[instance.stage];

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
        public SharedHashMap<int, int>.Reader guidIndices;
        [ReadOnly]
        public SharedHashMap<int, int>.Reader stageGUIDIndices;
        [ReadOnly]
        public BufferTypeHandle<GameNPC> instanceType;

        public Serializer Create(in ArchetypeChunk chunk, int firstEntityIndex)
        {
            Serializer serializer;
            serializer.guidIndices = guidIndices;
            serializer.stageGUIDIndices = stageGUIDIndices;
            serializer.instances = chunk.GetBufferAccessor(ref instanceType);

            return serializer;
        }
    }

    private BufferTypeHandle<GameNPC> __instanceType;
    private SharedHashMap<int, int> __guidIndices;
    private SharedHashMap<int, int> __stageGUIDIndices;
    private EntityDataSerializationSystemCore __core;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __instanceType = state.GetBufferTypeHandle<GameNPC>(true);
        var world = state.WorldUnmanaged;
        __guidIndices = world.GetExistingSystemUnmanaged<GameDataNPCContainerSerializationSystem>().guidIndices;
        __stageGUIDIndices = world.GetExistingSystemUnmanaged<GameDataNPCStageContainerSerializationSystem>().guidIndices;
        __core = EntityDataSerializationSystemCore.Create<GameNPC>(ref state);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        __core.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        SerializerFactory serializerFactory;
        serializerFactory.guidIndices = __guidIndices.reader;
        serializerFactory.stageGUIDIndices = __stageGUIDIndices.reader;
        serializerFactory.instanceType = __instanceType.UpdateAsRef(ref state);

        state.Dependency = JobHandle.CombineDependencies(state.Dependency, __guidIndices.lookupJobManager.readOnlyJobHandle, __stageGUIDIndices.lookupJobManager.readOnlyJobHandle);

        __core.Update<Serializer, SerializerFactory>(ref serializerFactory, ref state);

        var jobHandle = state.Dependency;

        __guidIndices.lookupJobManager.AddReadOnlyDependency(jobHandle);
        __stageGUIDIndices.lookupJobManager.AddReadOnlyDependency(jobHandle);
    }
}

[DisableAutoCreation]
public partial class GameDataNPCContainerDeserializationSystem : EntityDataIndexContainerDeserializationSystem
{
    protected override NativeArray<Hash128>.ReadOnly _GetGuids()
    {
        return SystemAPI.GetSingleton<GameDataNPCContainer>().guids;
    }
}

[DisableAutoCreation]
public partial class GameDataNPCStageContainerDeserializationSystem : EntityDataIndexContainerDeserializationSystem
{
    protected override NativeArray<Hash128>.ReadOnly _GetGuids()
    {
        return SystemAPI.GetSingleton<GameDataNPCStageContainer>().guids;
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