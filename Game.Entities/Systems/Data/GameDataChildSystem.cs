using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using ZG;

#region GameContainerChild
[assembly: RegisterGenericJobType(typeof(EntityDataComponentSerialize<GameDataEntityBufferSerializationSystemCore<GameContainerChild>.Serializer, GameDataEntityBufferSerializationSystemCore<GameContainerChild>.SerializerFactory>))]
[assembly: RegisterGenericJobType(typeof(EntityDataComponentDeserialize<GameDataContainerChildDeserializationSystem.Deserializer, GameDataContainerChildDeserializationSystem.DeserializerFactory>))]
//[assembly: EntityDataSerialize(typeof(GameContainerChild), typeof(GameDataContainerChildSerializationSystem))]
[assembly: EntityDataDeserialize(typeof(GameContainerChild), typeof(GameDataContainerChildDeserializationSystem), (int)GameDataConstans.Version)]
#endregion


/*[Serializable]
public struct GameChild : IBufferElementData
{
    public int index;
    public Hash128 guid;
}*/

[BurstCompile,
    EntityDataSerializationSystem(typeof(GameContainerChild)), 
    CreateAfter(typeof(EntityDataSerializationInitializationSystem)), 
    UpdateInGroup(typeof(EntityDataSerializationSystemGroup)), AutoCreateIn("Server")]
public partial struct GameDataContainerChildSerializationSystem : ISystem
{
    private GameDataEntityBufferSerializationSystemCore<GameContainerChild> __core;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __core = new GameDataEntityBufferSerializationSystemCore<GameContainerChild>(ref state);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {

    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        __core.Update(ref state);
    }
}

[DisableAutoCreation, AlwaysUpdateSystem]
public partial class GameDataContainerChildDeserializationSystem : EntityDataDeserializationComponentSystem<
    GameContainerChild,
    GameDataContainerChildDeserializationSystem.Deserializer,
    GameDataContainerChildDeserializationSystem.DeserializerFactory>
{
    public struct Child
    {
        public int index;
        public int entityIndex;
    }

    public struct Deserializer : IEntityDataDeserializer
    {
        [ReadOnly]
        public NativeArray<Entity> entityArray;

        public NativeParallelMultiHashMap<Entity, Child> children;

        public void Deserialize(int index, ref EntityDataReader reader)
        {
            Child child;
            Entity entity = entityArray[index];
            int length = reader.Read<int>();
            for (int i = 0; i < length; ++i)
            {
                child.index = reader.Read<int>();
                child.entityIndex = reader.Read<int>();

                children.Add(entity, child);
            }
        }
    }

    public struct DeserializerFactory : IEntityDataFactory<Deserializer>
    {
        [ReadOnly]
        public EntityTypeHandle entityType;

        public NativeParallelMultiHashMap<Entity, Child> children;

        public Deserializer Create(in ArchetypeChunk chunk, int firstEntityIndex)
        {
            Deserializer deserializer;
            deserializer.entityArray = chunk.GetNativeArray(entityType);
            deserializer.children = children;

            return deserializer;
        }
    }

    private struct Build : IJob
    {
        public int entityCount;
        [ReadOnly]
        public NativeParallelHashMap<int, Entity> entities;

        [ReadOnly]
        public NativeParallelMultiHashMap<Entity, Child> children;

        public BufferLookup<GameContainerChild> results;

        public void Execute()
        {
            if (entityCount > entities.Count())
                return;

            using (var keyValueArrays = children.GetKeyValueArrays(Allocator.Temp))
            { 
                int length = keyValueArrays.Keys.Length, count, i, j;
                Entity entity;
                Child source;
                GameContainerChild destination, temp;
                DynamicBuffer<GameContainerChild> results;
                for (i = 0; i < length; ++i)
                {
                    source = keyValueArrays.Values[i];

                    entity = entities[source.entityIndex];
                    if (!this.results.HasBuffer(entity))
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
                        results.Add(destination);
                }
            }
        }
    }

    private NativeParallelMultiHashMap<Entity, Child> __children;

    public override bool isSingle => true;

    protected override void OnCreate()
    {
        base.OnCreate();

        __children = new NativeParallelMultiHashMap<Entity, Child>(1, Allocator.Persistent);
    }

    protected override void OnDestroy()
    {
        __children.Dispose();

        base.OnDestroy();
    }

    protected override void OnUpdate()
    {
        base.OnUpdate();

        var systemGroup = base.systemGroup;
        var presentationSystem = systemGroup.presentationSystem;

        Build build;
        build.entityCount = systemGroup.initializationSystem.guids.Length;
        build.children = __children;
        build.entities = presentationSystem.entities;
        build.results = GetBufferLookup<GameContainerChild>();

        var jobHandle = build.Schedule(JobHandle.CombineDependencies(presentationSystem.readOnlyJobHandle, Dependency));

        presentationSystem.AddReadOnlyDependency(jobHandle);

        Dependency = jobHandle;
    }

    protected override DeserializerFactory _Get(ref JobHandle jobHandle)
    {
        DeserializerFactory factory;
        factory.entityType = GetEntityTypeHandle();
        factory.children = __children;

        return factory;
    }
}