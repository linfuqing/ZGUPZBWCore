using System;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Jobs;
using Unity.Entities;
using Unity.Collections;
using ZG;

#region GameCampManager
[assembly: RegisterGenericJobType(typeof(EntityDataContainerSerialize<GameCampManager.Serializer>))]
[assembly: RegisterGenericJobType(typeof(EntityDataContainerDeserialize<GameCampManager.Deserializer>))]
[assembly: EntityDataSerialize(typeof(GameCampManager), typeof(GameDataCampSerializationSystem))]
[assembly: EntityDataDeserialize(typeof(GameCampManager), typeof(GameDataCampDeserializationSystem), (int)GameDataConstans.Version)]
#endregion

#region GameEntityCamp
[assembly: RegisterGenericJobType(typeof(EntityDataComponentSerialize<ComponentDataSerializationSystem<GameEntityCamp>.Serializer, ComponentDataSerializationSystem<GameEntityCamp>.SerializerFactory>))]
[assembly: RegisterGenericJobType(typeof(EntityDataComponentDeserialize<ComponentDataDeserializationSystem<GameEntityCamp>.Deserializer, ComponentDataDeserializationSystem<GameEntityCamp>.DeserializerFactory>))]
[assembly: EntityDataSerialize(typeof(GameEntityCamp))]
[assembly: EntityDataDeserialize(typeof(GameEntityCamp), (int)GameDataConstans.Version)]
#endregion

public struct GameCampManager
{
    [Serializable]
    private struct Group
    {
        public int id;
        public int count;
    }

    public struct Serializer : IEntityDataContainerSerializer
    {
        [ReadOnly]
        private NativePool<Group> __groups;
        [ReadOnly]
        private NativeParallelHashMap<int, int> __groupCamps;

        public Serializer(in GameCampManager manager)
        {
            __groups = manager.__groups;
            __groupCamps = manager.__groupCamps;
        }

        public void Serialize(ref NativeBuffer.Writer writer)
        {
            writer.Serialize(__groups);
            writer.Serialize(__groupCamps);
        }
    }

    public struct Deserializer : IEntityDataContainerDeserializer
    {
        private NativePool<Group> __groups;
        private NativeParallelHashMap<int, int> __groupCamps;

        public Deserializer(ref GameCampManager manager)
        {
            __groups = manager.__groups;
            __groupCamps = manager.__groupCamps;
        }

        public void Deserialize(in UnsafeBlock block)
        {
            var reader = block.reader;
            reader.Deserialize(ref __groups);
            reader.Deserialize(ref __groupCamps);

            Group group, temp;
            int camp, length = __groups.length;
            for(int i = 0; i < length; ++i)
            {
                if (!__groups.TryGetValue(i, out group) || group.id == 0)
                    continue;

                if(!__groupCamps.TryGetValue(group.id, out camp))
                {
                    UnityEngine.Debug.LogError($"WTF Group ID: {group.id}");

                    continue;
                }

                if (camp == i)
                    continue;

                UnityEngine.Debug.LogError($"WTF Group ID: {group.id}, Source Camp: {i}, Destination Camp {camp}");

                if (__groups.TryGetValue(camp, out temp))
                    temp.count += group.count;
                else
                    temp.count = group.count;

                temp.id = group.id;

                __groups[camp] = temp;

                __groups.RemoveAt(i);
            }
        }
    }

    public readonly int builtInCamps;

    private NativePool<Group> __groups;
    private NativeParallelHashMap<int, int> __groupCamps;

    public Serializer serializer => new Serializer(this);

    public Deserializer deserializer => new Deserializer(ref this);

    public GameCampManager(Allocator allocator, int builtInCamps = 32)
    {
        this.builtInCamps = builtInCamps;

        __groups = new NativePool<Group>(allocator);
        __groupCamps = new NativeParallelHashMap<int, int>(1, allocator);
    }

    public void Dispose()
    {
        __groups.Dispose();
        __groupCamps.Dispose();
    }

    public int GetCamp(int groupId, int originCamp)
    {
        int source = originCamp - builtInCamps;
        if (groupId == 0)
        {
            __Free(source);

            return Alloc();
        }

        if (!__groups.TryGetValue(source, out var group))
        {
            group.id = 0;
            group.count = 1;
        }

        if (!__groupCamps.TryGetValue(groupId, out int destination))
        {
            destination = source < 0 || group.count > 1 ? __groups.nextIndex : source;

            __groupCamps[groupId] = destination;
        }

        if (destination != source)
        {
            __Free(source);

            if (__groups.TryGetValue(destination, out group))
                ++group.count;
            else
                group.count = 1;
        }

        group.id = groupId;
        __groups.Insert(destination, group);

        return destination + builtInCamps;
    }

    public int Alloc(int groupId)
    {
        Group group;
        group.id = groupId;
        group.count = 1;

        int nextIndex = __groups.nextIndex;
        __groups.Insert(nextIndex, group);

        return nextIndex + builtInCamps;
    }

    public int Alloc()
    {
        return Alloc(0);
    }

    public bool Free(int camp)
    {
        return __Free(camp - builtInCamps);
    }

    private bool __Free(int camp)
    {
        Group group;
        if (__groups.TryGetValue(camp, out group))
        {
            if (--group.count > 0)
                __groups[camp] = group;
            else
                __groups.RemoveAt(camp);

            return true;
        }

        return false;
    }
}

[AutoCreateIn("Server"), UpdateInGroup(typeof(GameDataSystemGroup))]
public partial class GameCampSystem : ReadOnlyLookupSystem
{
    private struct Serialize
    {
        public int minSerializableCamp;

        [ReadOnly]
        public NativeArray<Entity> entityArray;

        [ReadOnly]
        public NativeArray<GameEntityCamp> camps;

        public EntityCommandQueue<Entity>.ParallelWriter entityManager;

        public void Execute(int index)
        {
            if (camps[index].value < minSerializableCamp)
                return;

            entityManager.Enqueue(entityArray[index]);
        }
    }

    [BurstCompile]
    private struct SerializeEx : IJobChunk, IEntityCommandProducerJob
    {
        public int minSerializableCamp;

        [ReadOnly]
        public EntityTypeHandle entityType;

        [ReadOnly]
        public ComponentTypeHandle<GameEntityCamp> campType;

        public EntityCommandQueue<Entity>.ParallelWriter entityManager;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Serialize serialize;
            serialize.minSerializableCamp = minSerializableCamp;
            serialize.entityArray = chunk.GetNativeArray(entityType);
            serialize.camps = chunk.GetNativeArray(ref campType);
            serialize.entityManager = entityManager;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                serialize.Execute(i);
        }
    }

    private EntityQuery __group;

    private EntityCommandPool<Entity> __entityManager;

    public GameCampManager manager
    {
        get;

        private set;
    }

    public GameCampManager GetManagerReadOnly()
    {
        _lookupJobManager.CompleteReadOnlyDependency();

        return manager;
    }

    protected override void OnCreate()
    {
        base.OnCreate();

        __group = GetEntityQuery(
            new EntityQueryDesc()
            {
                All = new ComponentType[]
                {
                    ComponentType.ReadOnly<GameEntityCamp>()
                },
                None = new ComponentType[]
                {
                    typeof(EntityDataSerializable)
                }, 
                Options = EntityQueryOptions.IncludeDisabledEntities
            });
        __group.SetChangedVersionFilter(typeof(GameEntityCamp));

        manager = new GameCampManager(Allocator.Persistent, (int)GameDataConstans.BuiltInCamps);

        __entityManager = World.GetOrCreateSystemManaged<EndFrameEntityCommandSystem>().CreateAddComponentCommander<EntityDataSerializable>();
    }

    protected override void OnDestroy()
    {
        manager.Dispose();

        base.OnDestroy();
    }

    protected override void OnUpdate()
    {
        var entityManager = __entityManager.Create();

        SerializeEx serialize;
        serialize.minSerializableCamp = manager.builtInCamps;
        serialize.entityType = GetEntityTypeHandle();
        serialize.campType = GetComponentTypeHandle<GameEntityCamp>(true);
        serialize.entityManager = entityManager.parallelWriter;

        var jobHandle = serialize.ScheduleParallel(__group, Dependency);

        entityManager.AddJobHandleForProducer<SerializeEx>(jobHandle);

        Dependency = jobHandle;
    }

    protected override void _Update()
    {
        throw new NotImplementedException();
    }
}

[DisableAutoCreation]
public partial class GameDataCampSerializationSystem : EntityDataSerializationContainerSystem<GameCampManager.Serializer>
{
    private GameCampSystem __campSystem;

    protected override void OnCreate()
    {
        base.OnCreate();

        __campSystem = World.GetOrCreateSystemManaged<GameCampSystem>();
    }

    protected override void OnUpdate()
    {
        Dependency = JobHandle.CombineDependencies(Dependency, __campSystem.readOnlyJobHandle);

        base.OnUpdate();

        __campSystem.AddReadOnlyDependency(Dependency);
    }

    protected override GameCampManager.Serializer _Get() => __campSystem.manager.serializer;
}

[DisableAutoCreation]
public partial class GameDataCampDeserializationSystem : EntityDataDeserializationContainerSystem<GameCampManager.Deserializer>
{
    private GameCampSystem __campSystem;

    protected override void OnCreate()
    {
        base.OnCreate();

        __campSystem = World.GetOrCreateSystemManaged<GameCampSystem>();
    }

    protected override void OnUpdate()
    {
        Dependency = JobHandle.CombineDependencies(Dependency, __campSystem.readOnlyJobHandle);

        base.OnUpdate();

        __campSystem.AddReadOnlyDependency(Dependency);
    }

    protected override GameCampManager.Deserializer _Create(ref JobHandle jobHandle) => __campSystem.manager.deserializer;
}
