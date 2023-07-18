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
[assembly: EntityDataDeserialize(typeof(GameAreaManager), typeof(GameDataAreaDeserializationSystem), (int)GameDataConstans.Version)]
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
        __typeHandle = EntityDataSerializationUtility.GetTypeHandle(ref state);

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

        EntityDataSerializationUtility.Update(__typeHandle, ref serializer, ref state);

        lookupJobManager.AddReadOnlyDependency(state.Dependency);
    }
}

[DisableAutoCreation]
public partial class GameDataAreaDeserializationSystem : EntityDataDeserializationContainerSystem<GameAreaManager.Deserializer>
{
    private SharedHashMap<Hash128, int> __versions;

    protected override void OnCreate()
    {
        base.OnCreate();

        __versions = World.GetOrCreateSystemUnmanaged<GameAreaPrefabSystem>().versions;
    }

    protected override void OnUpdate()
    {
        ref var lookupJobManager = ref __versions.lookupJobManager;

        Dependency = JobHandle.CombineDependencies(Dependency, lookupJobManager.readWriteJobHandle);

        base.OnUpdate();

        lookupJobManager.readWriteJobHandle = Dependency;
    }

    protected override GameAreaManager.Deserializer _Create(ref JobHandle jobHandle) => new GameAreaManager.Deserializer(__versions.writer);
}