using Unity.Jobs;
using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;
using ZG;
using Unity.Burst;
using Unity.Burst.Intrinsics;

#region GameDataTime
[assembly: RegisterGenericJobType(typeof(EntityDataComponentSerialize<GameDataTimeSerializationSystemCore<GameDataTime, GameDataTimeStatus, GameDataTimeMask>.Serializer, GameDataTimeSerializationSystemCore<GameDataTime, GameDataTimeStatus, GameDataTimeMask>.SerializerFactory>))]
[assembly: RegisterGenericJobType(typeof(EntityDataComponentDeserialize<ComponentDataDeserializationSystem<GameDataTime>.Deserializer, ComponentDataDeserializationSystem<GameDataTime>.DeserializerFactory>))]

//[assembly: EntityDataSerialize(typeof(GameDataTime), typeof(GameDataTimeSerializationSystem))]
[assembly: EntityDataDeserialize(typeof(GameDataTime), (int)GameDataConstans.Version)]
#endregion

#region GameDataDeadline
[assembly: RegisterGenericJobType(typeof(EntityDataComponentSerialize<GameDataTimeSerializationSystemCore<GameDataDeadline, GameDataDeadlineStatus, GameDataDeadlineMask>.Serializer, GameDataTimeSerializationSystemCore<GameDataDeadline, GameDataDeadlineStatus, GameDataDeadlineMask>.SerializerFactory>))]
[assembly: RegisterGenericJobType(typeof(EntityDataComponentDeserialize<ComponentDataDeserializationSystem<GameDataDeadline>.Deserializer, ComponentDataDeserializationSystem<GameDataDeadline>.DeserializerFactory>))]

//[assembly: EntityDataSerialize(typeof(GameDataDeadline), typeof(GameDataDeadlineSerializationSystem))]
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

public struct GameDataDeadlineTrigger : IComponentData
{

}

public struct GameDataTimeSerializationSystemCore<TTime, TStatus, TMask>
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
        public void Serialize(int index, in SharedHashMap<Hash128, int>.Reader entityIndices, ref EntityDataWriter writer)
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

    private ComponentTypeHandle<TTime> __timeType;
    private ComponentTypeHandle<TStatus> __statusType;
    private ComponentTypeHandle<TMask> __maskType;
    private EntityDataSerializationSystemCore __core;

    public GameDataTimeSerializationSystemCore(ref SystemState state)
    {
        __timeType = state.GetComponentTypeHandle<TTime>(true);
        __statusType = state.GetComponentTypeHandle<TStatus>(true);
        __maskType = state.GetComponentTypeHandle<TMask>(true);

        __core = EntityDataSerializationSystemCore.Create<TTime>(ref state);
    }

    public void Dispose()
    {
        __core.Dispose();
    }

    public void Update(ref SystemState state)
    {
        SerializerFactory factory;
        factory.time = state.WorldUnmanaged.Time.ElapsedTime;
        factory.timeType = __timeType.UpdateAsRef(ref state);
        factory.statusType = __statusType.UpdateAsRef(ref state);
        factory.maskType = __maskType.UpdateAsRef(ref state);

        __core.Update<Serializer, SerializerFactory>(ref factory, ref state);
    }
}

[BurstCompile,
    EntityDataSerializationSystem(typeof(GameDataTime)),
    CreateAfter(typeof(EntityDataSerializationInitializationSystem)),
    UpdateInGroup(typeof(EntityDataSerializationSystemGroup)), AutoCreateIn("Server")]
public partial struct GameDataTimeSerializationSystem : ISystem
{
    private GameDataTimeSerializationSystemCore<GameDataTime, GameDataTimeStatus, GameDataTimeMask> __core;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __core = new GameDataTimeSerializationSystemCore<GameDataTime, GameDataTimeStatus, GameDataTimeMask>(ref state);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        __core.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        __core.Update(ref state);
    }
}

[BurstCompile,
    EntityDataSerializationSystem(typeof(GameDataDeadline)),
    CreateAfter(typeof(EntityDataSerializationInitializationSystem)),
    UpdateInGroup(typeof(EntityDataSerializationSystemGroup)), AutoCreateIn("Server")]
public partial struct GameDataDeadlineSerializationSystem : ISystem
{
    private GameDataTimeSerializationSystemCore<GameDataDeadline, GameDataDeadlineStatus, GameDataDeadlineMask> __core;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __core = new GameDataTimeSerializationSystemCore<GameDataDeadline, GameDataDeadlineStatus, GameDataDeadlineMask>(ref state);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        __core.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        __core.Update(ref state);
    }
}

[BurstCompile, AutoCreateIn("Server"), UpdateAfter(typeof(NetworkRPCSystem))]
public partial struct GameDataDeadlineSystem : ISystem
{
    private struct TriggerEntry : System.IEquatable<TriggerEntry>
    {
        public int node;
        public int camp;

        public bool Equals(TriggerEntry other)
        {
            return node == other.node && camp == other.camp;
        }

        public override int GetHashCode()
        {
            return node ^ camp;
        }
    }

    [BurstCompile]
    private struct Clear : IJob
    {
        [ReadOnly]
        public NetworkRPCManager<int> networkManager;

        public NativeParallelHashSet<TriggerEntry> triggerEntries;

        public void Execute()
        {
            triggerEntries.Clear();
            triggerEntries.Capacity = math.max(triggerEntries.Capacity, networkManager.CountOfIDNodes());
        }
    }

    private struct Trigger
    {
        [ReadOnly]
        public NetworkRPCManager<int> networkManager;

        [ReadOnly]
        public NativeArray<NetworkIdentity> identities;

        [ReadOnly]
        public NativeArray<GameEntityCamp> camps;

        public NativeParallelHashSet<TriggerEntry>.ParallelWriter triggerEntries;

        public void Execute(int index)
        {
            TriggerEntry triggerEntry;
            triggerEntry.camp = camps[index].value;

            foreach (var node in networkManager.GetIDNodes(identities[index].id))
            {
                triggerEntry.node = node;
                triggerEntries.Add(triggerEntry);
            }

            /*if (!networkManager.GetIDNodes(identities[index].id, out triggerEntry.node))
                return;

            triggerEntries.Add(triggerEntry);*/
        }
    }

    [BurstCompile]
    private struct TriggerEx : IJobChunk
    {
        [ReadOnly]
        public NetworkRPCManager<int> networkManager;

        [ReadOnly]
        public ComponentTypeHandle<NetworkIdentity> identityType;

        [ReadOnly]
        public ComponentTypeHandle<GameEntityCamp> campType;

        public NativeParallelHashSet<TriggerEntry>.ParallelWriter triggerEntries;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Trigger trigger;
            trigger.networkManager = networkManager;
            trigger.identities = chunk.GetNativeArray(ref identityType);
            trigger.camps = chunk.GetNativeArray(ref campType);
            trigger.triggerEntries = triggerEntries;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while(iterator.NextEntityIndex(out int i))
                trigger.Execute(i);
        }
    }

    private struct Refresh
    {
        public double elpasedTime;

        public Random random;

        [ReadOnly]
        public NetworkRPCManager<int> networkManager;

        [ReadOnly]
        public NativeParallelHashSet<TriggerEntry> triggerEntries;

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
            TriggerEntry triggerEntry;
            if (!networkManager.TryGetNode(identities[index].id, out triggerEntry.node))
                return true;

            triggerEntry.camp = camps[index].value;

            if (triggerEntries.Contains(triggerEntry))
            {
                GameDataDeadlineMask deadlineMask;
                deadlineMask.time = elpasedTime;
                deadlineMasks[index] = deadlineMask;

                GameDataDeadline deadline;
                deadline.value = deadlineRanges[index].GetValue(ref random);
                deadlines[index] = deadline;

                return true;
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
        public NetworkRPCManager<int> networkManager;

        [ReadOnly]
        public NativeParallelHashSet<TriggerEntry> triggerEntries;

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
            refresh.triggerEntries = triggerEntries;
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

    private EntityQuery __triggerGroup;
    private EntityQuery __deadlineGroup;
    private NetworkRPCController __networkRPCController;

    private NativeParallelHashSet<TriggerEntry> __triggerEntries;

    private ComponentTypeHandle<NetworkIdentity> __identityType;
    private ComponentTypeHandle<GameEntityCamp> __campType;
    private ComponentTypeHandle<GameDataDeadlineRange> __deadlineRangeType;
    private ComponentTypeHandle<GameDataDeadlineStatus> __deadlineStatusType;
    private ComponentTypeHandle<GameDataDeadlineMask> __deadlineMaskType;
    private ComponentTypeHandle<GameDataDeadline> __deadlineType;
    private ComponentTypeHandle<GameNodeStatus> __statusType;

    public void OnCreate(ref SystemState state)
    {
        __triggerGroup = state.GetEntityQuery(
            ComponentType.ReadOnly<NetworkIdentity>(),
            ComponentType.ReadOnly<GameEntityCamp>(),
            ComponentType.ReadOnly<GameDataDeadlineTrigger>());

        __deadlineGroup = state.GetEntityQuery(
            ComponentType.ReadOnly<NetworkIdentity>(),
            ComponentType.ReadOnly<GameEntityCamp>(),
            ComponentType.ReadOnly<GameDataDeadlineRange>(),
            ComponentType.ReadWrite<GameDataDeadlineMask>(),
            ComponentType.ReadWrite<GameDataDeadline>(),
            ComponentType.ReadWrite<GameNodeStatus>());

        __networkRPCController = state.World.GetOrCreateSystemUnmanaged<NetworkRPCFactorySystem>().controller;

        __triggerEntries = new NativeParallelHashSet<TriggerEntry>(1, Allocator.Persistent);

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
        __triggerEntries.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var networkManager = __networkRPCController.manager;
        ref var networkRPCJobManager = ref __networkRPCController.lookupJobManager;

        var jobHandle = JobHandle.CombineDependencies(networkRPCJobManager.readOnlyJobHandle, state.Dependency);

        Clear clear;
        clear.networkManager = networkManager;
        clear.triggerEntries = __triggerEntries;
        jobHandle = clear.ScheduleByRef(jobHandle);

        var identityType = __identityType.UpdateAsRef(ref state);
        var campType = __campType.UpdateAsRef(ref state);

        TriggerEx trigger;
        trigger.networkManager = networkManager;
        trigger.identityType = identityType;
        trigger.campType = campType;
        trigger.triggerEntries = __triggerEntries.AsParallelWriter();

        jobHandle = trigger.ScheduleParallelByRef(__triggerGroup, jobHandle);

        RefreshEx refresh;
        refresh.elpasedTime = state.WorldUnmanaged.Time.ElapsedTime;
        refresh.networkManager = networkManager;
        refresh.triggerEntries = __triggerEntries;
        refresh.identityType = identityType;
        refresh.campType = campType;
        refresh.deadlineRangeType = __deadlineRangeType.UpdateAsRef(ref state);
        refresh.deadlineStatusType = __deadlineStatusType.UpdateAsRef(ref state);
        refresh.deadlineMaskType = __deadlineMaskType.UpdateAsRef(ref state);
        refresh.deadlineType = __deadlineType.UpdateAsRef(ref state);
        refresh.statusType = __statusType.UpdateAsRef(ref state);

        jobHandle = refresh.ScheduleParallelByRef(__deadlineGroup, jobHandle);

        networkRPCJobManager.AddReadOnlyDependency(jobHandle);

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