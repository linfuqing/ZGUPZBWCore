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
//[assembly: RegisterGenericJobType(typeof(EntityDataContainerDeserialize<GameDataNPCContainerDeserializationSystem.Deserializer>))]

//[assembly: EntityDataSerialize(typeof(GameNPCManager), typeof(GameDataNPCContainerSerializationSystem))]
//[assembly: EntityDataDeserialize(typeof(GameNPCManager), typeof(GameDataNPCContainerDeserializationSystem), (int)GameDataConstans.Version)]
#endregion

#region GameNPCStageManager
[assembly: RegisterGenericJobType(typeof(EntityDataIndexBufferInit<GameNPC, GameNPCStageWrapper>))]

//[assembly: RegisterGenericJobType(typeof(EntityDataContainerSerialize<GameDataNPCStageContainerSerializationSystem.Serializer>))]
//[assembly: RegisterGenericJobType(typeof(EntityDataContainerDeserialize<GameDataNPCStageContainerDeserializationSystem.Deserializer>))]

//[assembly: EntityDataSerialize(typeof(GameNPCStageManager), typeof(GameDataNPCStageContainerSerializationSystem))]
//[assembly: EntityDataDeserialize(typeof(GameNPCStageManager), typeof(GameDataNPCStageContainerDeserializationSystem), (int)GameDataConstans.Version)]
#endregion

#region GameNPC
[assembly: RegisterGenericJobType(typeof(EntityDataComponentSerialize<GameDataNPCSerializationSystem.Serializer, GameDataNPCSerializationSystem.SerializerFactory>))]
[assembly: RegisterGenericJobType(typeof(EntityDataComponentDeserialize<GameDataNPCDeserializationSystem.Deserializer, GameDataNPCDeserializationSystem.DeserializerFactory>))]
//[assembly: EntityDataSerialize(typeof(GameNPC), typeof(GameDataNPCSerializationSystem))]
//[assembly: EntityDataDeserialize(typeof(GameNPC), typeof(GameDataNPCDeserializationSystem), (int)GameDataConstans.Version)]
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

[BurstCompile,
    EntityDataDeserializationSystem(typeof(GameNPCManager), (int)GameDataConstans.Version),
    CreateAfter(typeof(EntityDataDeserializationContainerSystem)),
    UpdateInGroup(typeof(EntityDataDeserializationSystemGroup)), AutoCreateIn("Server")]
public partial struct GameDataNPCContainerDeserializationSystem : ISystem, IEntityDataDeserializationIndexContainerSystem
{
    private EntityDataDeserializationIndexContainerSystemCore __core;

    public SharedList<int> guidIndices => __core.guidIndices;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __core = new EntityDataDeserializationIndexContainerSystemCore(ref state);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        __core.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        __core.Update(SystemAPI.GetSingleton<GameDataNPCContainer>().guids, ref state);
    }
}

[BurstCompile,
    EntityDataDeserializationSystem(typeof(GameNPCStageManager), (int)GameDataConstans.Version),
    CreateAfter(typeof(EntityDataDeserializationContainerSystem)),
    UpdateInGroup(typeof(EntityDataDeserializationSystemGroup)), AutoCreateIn("Server")]
public partial struct GameDataNPCStageContainerDeserializationSystem : ISystem, IEntityDataDeserializationIndexContainerSystem
{
    private EntityDataDeserializationIndexContainerSystemCore __core;

    public SharedList<int> guidIndices => __core.guidIndices;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __core = new EntityDataDeserializationIndexContainerSystemCore(ref state);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        __core.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        __core.Update(SystemAPI.GetSingleton<GameDataNPCStageContainer>().guids, ref state);
    }
}

[BurstCompile,
    EntityDataDeserializationSystem(typeof(GameNPC), (int)GameDataConstans.Version),
    CreateAfter(typeof(EntityDataDeserializationComponentSystem)),
    CreateAfter(typeof(GameDataNPCContainerDeserializationSystem)),
    CreateAfter(typeof(GameDataNPCStageContainerDeserializationSystem)),
    UpdateInGroup(typeof(EntityDataDeserializationSystemGroup)),
    UpdateAfter(typeof(GameDataNPCContainerDeserializationSystem)),
    UpdateAfter(typeof(GameDataNPCStageContainerDeserializationSystem)), AutoCreateIn("Server")]
public partial struct GameDataNPCDeserializationSystem : ISystem
{
    public struct Deserializer : IEntityDataDeserializer
    {
        [ReadOnly]
        public SharedList<int>.Reader guidIndices;
        [ReadOnly]
        public SharedList<int>.Reader stageGUIDIndices;

        public BufferAccessor<GameNPC> instances;

        public bool Fallback(int index)
        {
            return false;
        }

        public void Deserialize(int index, ref EntityDataReader reader)
        {
            int length = reader.Read<int>();
            var sources = reader.ReadArray<GameNPC>(length);
            var destinations = instances[index];
            destinations.CopyFrom(sources);

            int count = 0, numGUIDIndices = guidIndices.length;
            GameNPC instance;
            for (int i = 0; i < length; ++i)
            {
                instance = destinations[i];
                if(instance.index < 0 || instance.index >= numGUIDIndices)
                {
                    UnityEngine.Debug.LogError($"NPC Index {instance.index} Invail.");

                    continue;
                }

                instance.index = guidIndices[instance.index];
                instance.stage = stageGUIDIndices[instance.stage];

                destinations[count++] = instance;
            }

            destinations.ResizeUninitialized(count);
        }
    }

    public struct DeserializerFactory : IEntityDataFactory<Deserializer>
    {
        [ReadOnly]
        public SharedList<int>.Reader guidIndices;
        [ReadOnly]
        public SharedList<int>.Reader stageGUIDIndices;

        public BufferTypeHandle<GameNPC> instanceType;

        public Deserializer Create(in ArchetypeChunk chunk, int firstEntityIndex)
        {
            Deserializer deserializer;
            deserializer.guidIndices = guidIndices;
            deserializer.stageGUIDIndices = stageGUIDIndices;
            deserializer.instances = chunk.GetBufferAccessor(ref instanceType);

            return deserializer;
        }
    }

    private BufferTypeHandle<GameNPC> __instanceType;

    private SharedList<int> __guidIndices;
    private SharedList<int> __stageGUIDIndices;

    private EntityDataDeserializationSystemCore __core;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __instanceType = state.GetBufferTypeHandle<GameNPC>();

        var world = state.WorldUnmanaged;
        __guidIndices = world.GetExistingSystemUnmanaged<GameDataNPCContainerDeserializationSystem>().guidIndices;
        __stageGUIDIndices = world.GetExistingSystemUnmanaged<GameDataNPCStageContainerDeserializationSystem>().guidIndices;

        __core = EntityDataDeserializationSystemCore.Create<GameNPC>(ref state);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        __core.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        ref var guidIndicesJobManager = ref __guidIndices.lookupJobManager;
        ref var stageGUIDIndicesJobManager = ref __stageGUIDIndices.lookupJobManager;

        state.Dependency = JobHandle.CombineDependencies(guidIndicesJobManager.readOnlyJobHandle, stageGUIDIndicesJobManager.readOnlyJobHandle, state.Dependency);

        DeserializerFactory deserializerFactory;
        deserializerFactory.guidIndices = __guidIndices.reader;
        deserializerFactory.stageGUIDIndices = __stageGUIDIndices.reader;
        deserializerFactory.instanceType = __instanceType.UpdateAsRef(ref state);

        __core.Update<Deserializer, DeserializerFactory>(ref deserializerFactory, ref state, true);

        var jobHandle = state.Dependency;

        guidIndicesJobManager.AddReadOnlyDependency(jobHandle);
        stageGUIDIndicesJobManager.AddReadOnlyDependency(jobHandle);
    }
}