using System;
using Unity.Jobs;
using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;
using ZG;
using Unity.Burst;

#region GameAreaManager
[assembly: RegisterGenericJobType(typeof(EntityDataContainerSerialize<GameAreaManager.Serializer>))]
[assembly: RegisterGenericJobType(typeof(EntityDataContainerDeserialize<GameAreaManager.Deserializer>))]
//[assembly: EntityDataSerialize(typeof(GameAreaManager), typeof(GameDataAreaSerializationSystem))]
//[assembly: EntityDataDeserialize(typeof(GameAreaManager), typeof(GameDataAreaDeserializationSystem), (int)GameDataConstans.Version)]
#endregion

public struct GameAreaManager
{
    public struct Serializer : IEntityDataContainerSerializer
    {
        [ReadOnly]
        private SharedHashMap<Hash128, int>.Reader __versions;

        public Serializer(in SharedHashMap<Hash128, int>.Reader versions)
        {
            __versions = versions;
        }

        public void Serialize(ref NativeBuffer.Writer writer)
        {
            using (var versions = __versions.GetKeyValueArrays(Allocator.Temp))
            {
                writer.Write(versions.Length);
                writer.Write(versions.Keys);
                writer.Write(versions.Values);
            }
        }
    }

    public struct Deserializer : IEntityDataContainerDeserializer
    {
        private SharedHashMap<Hash128, int>.Writer __versions;

        public Deserializer(SharedHashMap<Hash128, int>.Writer versions)
        {
            __versions = versions;
        }

        public void Deserialize(in UnsafeBlock block)
        {
            var reader = block.reader;
            int length = reader.Read<int>();
            __versions.capacity = math.max(__versions.capacity, length);

            var keys = reader.ReadArray<Hash128>(length);
            var values = reader.ReadArray<int>(length);

            for (int i = 0; i < length; ++i)
                __versions.Add(keys[i], values[i]);
        }
    }
}

[BurstCompile,
    EntityDataSerializationSystem(typeof(GameAreaManager)),
    CreateAfter(typeof(GameAreaPrefabSystem)),
    CreateAfter(typeof(EntityDataSerializationInitializationSystem)),
    UpdateInGroup(typeof(EntityDataSerializationSystemGroup)), AutoCreateIn("Server")]
public partial struct GameDataAreaSerializationContainerSystem : ISystem
{
    private SharedHashMap<Hash128, int> __versions;

    private EntityDataSerializationTypeHandle __typeHandle;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __typeHandle = new EntityDataSerializationTypeHandle(ref state);

        __versions = state.WorldUnmanaged.GetExistingSystemUnmanaged<GameAreaPrefabSystem>().versions;
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var serializer = new GameAreaManager.Serializer(__versions.reader);

        ref var lookupJobManager = ref __versions.lookupJobManager;
        state.Dependency = JobHandle.CombineDependencies(state.Dependency, lookupJobManager.readOnlyJobHandle);

        __typeHandle.Update(ref serializer, ref state);

        lookupJobManager.AddReadOnlyDependency(state.Dependency);
    }
}

[BurstCompile,
    EntityDataDeserializationSystem(typeof(GameAreaManager), (int)GameDataConstans.Version),
    CreateAfter(typeof(EntityDataDeserializationContainerSystem)),
    CreateAfter(typeof(GameAreaPrefabSystem)), 
    UpdateInGroup(typeof(EntityDataDeserializationSystemGroup)), AutoCreateIn("Server")]
public partial struct GameDataAreaDeserializationContainerSystem : ISystem
{
    private SharedHashMap<Hash128, int> __versions;

    private EntityDataDeserializationContainerSystemCore __core;
    
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __versions = state.WorldUnmanaged.GetExistingSystemUnmanaged<GameAreaPrefabSystem>().versions;

        __core = new EntityDataDeserializationContainerSystemCore(ref state);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        __core.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        ref var versionsJobManager = ref __versions.lookupJobManager;

        state.Dependency = JobHandle.CombineDependencies(versionsJobManager.readWriteJobHandle, state.Dependency);

        var deserializer = new GameAreaManager.Deserializer(__versions.writer);

        __core.Update(ref deserializer, ref state);

        versionsJobManager.readWriteJobHandle = state.Dependency;
    }
}
