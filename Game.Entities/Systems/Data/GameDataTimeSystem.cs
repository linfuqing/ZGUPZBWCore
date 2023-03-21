using Unity.Jobs;
using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;
using ZG;
using Unity.Burst;
using Unity.Burst.Intrinsics;

#region GameDataTime
[assembly: RegisterGenericJobType(typeof(EntityDataComponentSerialize<GameDataTimeSerializationSystem.Serializer, GameDataTimeSerializationSystem.SerializerFactory>))]
[assembly: RegisterGenericJobType(typeof(EntityDataComponentDeserialize<ComponentDataDeserializationSystem<GameDataTime>.Deserializer, ComponentDataDeserializationSystem<GameDataTime>.DeserializerFactory>))]

[assembly: EntityDataSerialize(typeof(GameDataTime), typeof(GameDataTimeSerializationSystem))]
[assembly: EntityDataDeserialize(typeof(GameDataTime), (int)GameDataConstans.Version)]
#endregion

#region GameDataDeadline
[assembly: RegisterGenericJobType(typeof(EntityDataComponentSerialize<GameDataDeadlineSerializationSystem.Serializer, GameDataDeadlineSerializationSystem.SerializerFactory>))]
[assembly: RegisterGenericJobType(typeof(EntityDataComponentDeserialize<ComponentDataDeserializationSystem<GameDataDeadline>.Deserializer, ComponentDataDeserializationSystem<GameDataDeadline>.DeserializerFactory>))]

[assembly: EntityDataSerialize(typeof(GameDataDeadline), typeof(GameDataDeadlineSerializationSystem))]
[assembly: EntityDataDeserialize(typeof(GameDataDeadline), (int)GameDataConstans.Version)]
#endregion

public interface IGameDataTime : IComponentData
{
    float value { get; set; }
}

public interface IGameDataTimeStatus : IComponentData
{
    int value { get; set; }
}

public interface IGameDataTimeMask : IComponentData
{
    double time { get; set; }
}

public struct GameDataTime : IGameDataTime
{
    public float value;

    float IGameDataTime.value
    {
        get => value;

        set => this.value = value;
    }
}

public struct GameDataDeadline : IGameDataTime
{
    public float value;

    float IGameDataTime.value
    {
        get => value;

        set => this.value = value;
    }
}

public struct GameDataTimeStatus : IGameDataTimeStatus
{
    public int value;

    int IGameDataTimeStatus.value
    {
        get => value;

        set => this.value = value;
    }
}

public struct GameDataDeadlineStatus : IGameDataTimeStatus
{
    public int value;

    int IGameDataTimeStatus.value
    {
        get => value;

        set => this.value = value;
    }
}

public struct GameDataTimeMask : IGameDataTimeMask
{
    public double time;

    double IGameDataTimeMask.time
    {
        get => time;

        set => time = value;
    }
}

public struct GameDataDeadlineMask : IGameDataTimeMask
{
    public double time;
    double IGameDataTimeMask.time
    {
        get => time;

        set => time = value;
    }
}

public struct GameDataDeadlineRange : IComponentData
{
    public float min;
    public float max;

    public float GetValue(ref Random random)
    {
        return random.NextFloat(min, max);
    }
}

public partial class GameDataTimeSerializationSystem<TTime, TStatus, TMask> : EntityDataSerializationComponentSystem<
    TTime,
    GameDataTimeSerializationSystem<TTime, TStatus, TMask>.Serializer,
    GameDataTimeSerializationSystem<TTime, TStatus, TMask>.SerializerFactory> 
    where TTime : unmanaged, IGameDataTime
    where TStatus : unmanaged, IGameDataTimeStatus
    where TMask : unmanaged, IGameDataTimeMask
{
    public struct Serializer : IEntityDataSerializer
    {
        public double time;
        [ReadOnly]
        public NativeArray<TTime> times;
        [ReadOnly]
        public NativeArray<TStatus> states;
        [ReadOnly]
        public NativeArray<TMask> masks;
        public void Serialize(int index, in NativeParallelHashMap<Hash128, int> entityIndices, ref EntityDataWriter writer)
        {
            var time = times[index];
            time.value = GameDataTimeUtility.CalculateTime(
                index < states.Length ? states[index].value : 1,
                time.value,
                masks[index].time,
                this.time);

            writer.Write(time);
        }
    }

    public struct SerializerFactory : IEntityDataFactory<Serializer>
    {
        public double time;
        [ReadOnly]
        public ComponentTypeHandle<TTime> timeType;
        [ReadOnly]
        public ComponentTypeHandle<TStatus> statusType;
        [ReadOnly]
        public ComponentTypeHandle<TMask> maskType;

        public Serializer Create(in ArchetypeChunk chunk, int firstEntityIndex)
        {
            Serializer serializer;
            serializer.time = time;
            serializer.times = chunk.GetNativeArray(ref timeType);
            serializer.masks = chunk.GetNativeArray(ref maskType);
            serializer.states = chunk.GetNativeArray(ref statusType);

            return serializer;
        }
    }

    protected override SerializerFactory _Get(ref JobHandle jobHandle)
    {
        SerializerFactory factory;
        factory.time = World.Time.ElapsedTime;
        factory.timeType = GetComponentTypeHandle<TTime>(true);
        factory.statusType = GetComponentTypeHandle<TStatus>(true);
        factory.maskType = GetComponentTypeHandle<TMask>(true);

        return factory;
    }
}

[DisableAutoCreation]
public partial class GameDataTimeSerializationSystem : GameDataTimeSerializationSystem<GameDataTime, GameDataTimeStatus, GameDataTimeMask>
{
}

[DisableAutoCreation]
public partial class GameDataDeadlineSerializationSystem : GameDataTimeSerializationSystem<GameDataDeadline, GameDataDeadlineStatus, GameDataDeadlineMask>
{

}

[BurstCompile, AutoCreateIn("Server"), UpdateAfter(typeof(NetworkRPCSystem))]
public partial struct GameDataDeadlineSystem : ISystem
{
    private struct Refresh
    {
        public double elpasedTime;

        public Random random;

        [ReadOnly]
        public NetworkRPCManager<int>.ReadOnly networkManager;

        [ReadOnly]
        public SharedHashMap<uint, Entity>.Reader idEntities;

        [ReadOnly]
        public ComponentLookup<GameEntityCamp> campMap;

        [ReadOnly]
        public ComponentLookup<NetworkIdentity> identityMap;

        [ReadOnly]
        public NativeArray<NetworkIdentity> identities;

        [ReadOnly]
        public NativeArray<GameEntityCamp> camps;

        [ReadOnly]
        public NativeArray<GameDataDeadlineRange> deadlineRanges;

        [ReadOnly]
        public NativeArray<GameDataDeadlineStatus> deadlineStates;

        public NativeArray<GameDataDeadlineMask> deadlineMasks;

        public NativeArray<GameDataDeadline> deadlines;

        public bool Execute(int index)
        {
            uint id;
            Entity entity;
            var camp = camps[index].value;
            var enumerator = networkManager.GetNodeIDs(identities[index].id);
            while (enumerator.MoveNext())
            {
                id = enumerator.Current;
                if (idEntities.TryGetValue(id, out entity) &&
                    identityMap.HasComponent(entity) &&
                    identityMap[entity].isLocalPlayer &&
                    campMap.HasComponent(entity) &&
                    campMap[entity].value == camp)
                {
                    GameDataDeadlineMask deadlineMask;
                    deadlineMask.time = elpasedTime;
                    deadlineMasks[index] = deadlineMask;

                    GameDataDeadline deadline;
                    deadline.value = deadlineRanges[index].GetValue(ref random);
                    deadlines[index] = deadline;

                    return true;
                }
            }

            return GameDataTimeUtility.CalculateTime(
                index < deadlineStates.Length ? deadlineStates[0].value : 1,
                deadlines[index].value,
                deadlineMasks[index].time,
                elpasedTime) > math.FLT_MIN_NORMAL;
        }
    }

    [BurstCompile]
    private struct RefreshEx : IJobChunk
    {
        public double elpasedTime;

        [ReadOnly]
        public NetworkRPCManager<int>.ReadOnly networkManager;

        [ReadOnly]
        public SharedHashMap<uint, Entity>.Reader idEntities;

        [ReadOnly]
        public ComponentLookup<GameEntityCamp> camps;

        [ReadOnly]
        public ComponentLookup<NetworkIdentity> identities;

        [ReadOnly]
        public ComponentTypeHandle<NetworkIdentity> identityType;

        [ReadOnly]
        public ComponentTypeHandle<GameEntityCamp> campType;

        [ReadOnly]
        public ComponentTypeHandle<GameDataDeadlineRange> deadlineRangeType;

        [ReadOnly]
        public ComponentTypeHandle<GameDataDeadlineStatus> deadlineStatusType;

        public ComponentTypeHandle<GameDataDeadlineMask> deadlineMaskType;

        public ComponentTypeHandle<GameDataDeadline> deadlineType;

        public ComponentTypeHandle<GameNodeStatus> statusType;

        public void Execute(
            in ArchetypeChunk chunk, 
            int unfilteredChunkIndex, 
            bool useEnabledMask, 
            in v128 chunkEnabledMask)
        {
            Refresh refresh;
            refresh.elpasedTime = elpasedTime;
            var seed64 = math.aslong(elpasedTime);
            refresh.random = new Random((uint)seed64 ^ (uint)(seed64 >> 32));
            refresh.networkManager = networkManager;
            refresh.idEntities = idEntities;
            refresh.campMap = camps;
            refresh.identityMap = identities;
            refresh.identities = chunk.GetNativeArray(ref identityType);
            refresh.camps = chunk.GetNativeArray(ref campType);
            refresh.deadlineRanges = chunk.GetNativeArray(ref deadlineRangeType);
            refresh.deadlineStates = chunk.GetNativeArray(ref deadlineStatusType);
            refresh.deadlineMasks = chunk.GetNativeArray(ref deadlineMaskType);
            refresh.deadlines = chunk.GetNativeArray(ref deadlineType);

            GameNodeStatus status;
            NativeArray<GameNodeStatus> states = default;
            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while(iterator.NextEntityIndex(out int i))
            {
                if(!refresh.Execute(i))
                {
                    if (!states.IsCreated)
                        states = chunk.GetNativeArray(ref statusType);

                    status.value = (int)GameEntityStatus.Dead;
                    states[i] = status;
                }
            }
        }
    }

    private EntityQuery __group;
    private NetworkRPCController __networkRPCController;
    private SharedHashMap<uint, Entity> __idEntities;

    private ComponentLookup<GameEntityCamp> __camps;
    private ComponentLookup<NetworkIdentity> __identities;
    private ComponentTypeHandle<NetworkIdentity> __identityType;
    private ComponentTypeHandle<GameEntityCamp> __campType;
    private ComponentTypeHandle<GameDataDeadlineRange> __deadlineRangeType;
    private ComponentTypeHandle<GameDataDeadlineStatus> __deadlineStatusType;
    private ComponentTypeHandle<GameDataDeadlineMask> __deadlineMaskType;
    private ComponentTypeHandle<GameDataDeadline> __deadlineType;
    private ComponentTypeHandle<GameNodeStatus> __statusType;

    public void OnCreate(ref SystemState state)
    {
        __group = state.GetEntityQuery(
            ComponentType.ReadOnly<NetworkIdentity>(),
            ComponentType.ReadOnly<GameEntityCamp>(),
            ComponentType.ReadOnly<GameDataDeadlineRange>(),
            ComponentType.ReadWrite<GameDataDeadlineMask>(),
            ComponentType.ReadWrite<GameDataDeadline>(),
            ComponentType.ReadWrite<GameNodeStatus>());

        __networkRPCController = state.World.GetOrCreateSystemUnmanaged<NetworkRPCFactorySystem>().controller;

        __idEntities = state.World.GetOrCreateSystemUnmanaged<NetworkEntitySystem>().entities;

        __camps = state.GetComponentLookup<GameEntityCamp>(true);
        __identities = state.GetComponentLookup<NetworkIdentity>(true);
        __identityType = state.GetComponentTypeHandle<NetworkIdentity>(true);
        __campType = state.GetComponentTypeHandle<GameEntityCamp>(true);
        __deadlineRangeType = state.GetComponentTypeHandle<GameDataDeadlineRange>(true);
        __deadlineStatusType = state.GetComponentTypeHandle<GameDataDeadlineStatus>(true);
        __deadlineMaskType = state.GetComponentTypeHandle<GameDataDeadlineMask>();
        __deadlineType = state.GetComponentTypeHandle<GameDataDeadline>();
        __statusType = state.GetComponentTypeHandle<GameNodeStatus>();
    }

    public void OnDestroy(ref SystemState state)
    {

    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        RefreshEx refresh;
        refresh.elpasedTime = state.WorldUnmanaged.Time.ElapsedTime;
        refresh.networkManager = __networkRPCController.manager.AsReadOnly();
        refresh.idEntities = __idEntities.reader;
        refresh.camps = __camps.UpdateAsRef(ref state);
        refresh.identities = __identities.UpdateAsRef(ref state);
        refresh.identityType = __identityType.UpdateAsRef(ref state);
        refresh.campType = __campType.UpdateAsRef(ref state);
        refresh.deadlineRangeType = __deadlineRangeType.UpdateAsRef(ref state);
        refresh.deadlineStatusType = __deadlineStatusType.UpdateAsRef(ref state);
        refresh.deadlineMaskType = __deadlineMaskType.UpdateAsRef(ref state);
        refresh.deadlineType = __deadlineType.UpdateAsRef(ref state);
        refresh.statusType = __statusType.UpdateAsRef(ref state);

        ref var networkRPCJobManager = ref __networkRPCController.lookupJobManager;
        ref var idEntitiesJobManager = ref __idEntities.lookupJobManager;

        var jobHandle = refresh.ScheduleParallelByRef(
            __group,
            JobHandle.CombineDependencies(networkRPCJobManager.readOnlyJobHandle, idEntitiesJobManager.readOnlyJobHandle, state.Dependency));

        networkRPCJobManager.AddReadOnlyDependency(jobHandle);
        idEntitiesJobManager.AddReadOnlyDependency(jobHandle);

        state.Dependency = jobHandle;
    }
}

public static class GameDataTimeUtility
{
    public static float CalculateTime(int status, float time, double maskTime, double elpasedTime)
    {
        if (status != 0)
            time = time - math.min(time, (elpasedTime > maskTime ? (float)(elpasedTime - maskTime) : 0.0f));

        return time;
    }

    public static float GetTime<TTime, TStatus, TMask>(this IGameObjectEntity instance) 
        where TTime : unmanaged, IGameDataTime
        where TStatus : unmanaged, IGameDataTimeStatus
        where TMask : unmanaged, IGameDataTimeMask
    {
        return CalculateTime(
            instance.HasComponent<TStatus>() ? instance.GetComponentData<TStatus>().value : 1,
            instance.GetComponentData<TTime>().value,
            instance.GetComponentData<TMask>().time,
            instance.GetTimeData().ElapsedTime);
    }

    public static float GetTime(this IGameObjectEntity instance) => GetTime<GameDataTime, GameDataTimeStatus, GameDataTimeMask>(instance);

    public static float GetDeadline(this IGameObjectEntity instance) => GetTime<GameDataDeadline, GameDataDeadlineStatus, GameDataDeadlineMask>(instance);

    public static void SetTime<TTime, TMask>(this IGameObjectEntity instance, in float value)
        where TTime : struct, IGameDataTime
        where TMask : struct, IGameDataTimeMask
    {
        TMask mask = default;
        mask.time = instance.GetTimeData().ElapsedTime;
        instance.SetComponentData(mask);

        TTime time = default;
        time.value = value;
        instance.SetComponentData(time);
    }

    public static void SetTime(this IGameObjectEntity instance, in float value) => SetTime<GameDataTime, GameDataTimeMask>(instance, value);

    public static void SetDeadline(this IGameObjectEntity instance, in float value) => SetTime<GameDataDeadline, GameDataDeadlineMask>(instance, value);

    public static void SetTimeStatus<TTime, TStatus, TMask>(this IGameObjectEntity instance, int value)
        where TTime : unmanaged, IGameDataTime
        where TStatus : unmanaged, IGameDataTimeStatus
        where TMask : unmanaged, IGameDataTimeMask
    {
        var status = instance.GetComponentData<TStatus>();
        if (status.value == value)
            return;

        var mask = instance.GetComponentData<TMask>();

        double elapsedTime = instance.GetTimeData().ElapsedTime;

        TTime time = instance.GetComponentData<TTime>();
        time.value = CalculateTime(
            status.value,
            time.value,
            mask.time,
            elapsedTime);
        instance.SetComponentData(time);

        mask.time = elapsedTime; 
        instance.SetComponentData(mask);

        status.value = value;
        instance.SetComponentData(status);
    }

    public static void SetTimeStatus(this IGameObjectEntity instance, int value) => SetTimeStatus<GameDataTime, GameDataTimeStatus, GameDataTimeMask>(instance, value);

    public static void SetDeadlineStatus(this IGameObjectEntity instance, int value) => SetTimeStatus<GameDataDeadline, GameDataDeadlineStatus, GameDataDeadlineMask>(instance, value);
}