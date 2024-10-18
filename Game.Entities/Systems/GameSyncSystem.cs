using System;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Physics.Systems;
using Unity.Physics.GraphicsIntegration;
//using Unity.Physics.Authoring;
using ZG;

/*[assembly: ForceUpdateInGroup(typeof(BuildPhysicsWorld), typeof(GameUpdateSystemGroup))]
[assembly: ForceUpdateInGroup(typeof(StepPhysicsWorld), typeof(GameUpdateSystemGroup))]
[assembly: ForceUpdateInGroup(typeof(ExportPhysicsWorld), typeof(GameUpdateSystemGroup))]
[assembly: ForceUpdateInGroup(typeof(EndFramePhysicsSystem), typeof(GameUpdateSystemGroup))]

[assembly: ForceUpdateInGroup(typeof(BufferInterpolatedRigidBodiesMotion), typeof(GameUpdateSystemGroup))]
[assembly: ForceUpdateInGroup(typeof(CopyPhysicsVelocityToSmoothing), typeof(GameUpdateSystemGroup))]
[assembly: ForceUpdateInGroup(typeof(RecordMostRecentFixedTime), typeof(GameUpdateSystemGroup))]*/

//[assembly: ForceUpdateInGroup(typeof(PhysicsTriggerEventSystem), typeof(GameUpdateSystemGroup))]

//[assembly: ForceUpdateInGroup(typeof(PhysicsShapeDynamicSystem), typeof(GameUpdateSystemGroup))]
//[assembly: ForceUpdateInGroup(typeof(PhysicsShapeDestroyColliderSystem), typeof(GameUpdateSystemGroup))]
//[assembly: ForceUpdateInGroup(typeof(PhysicsShapeColliderSystem), typeof(GameUpdateSystemGroup))]
//[assembly: ForceUpdateInGroup(typeof(PhysicsShapeTriggerEventRevicerSystem), typeof(GameUpdateSystemGroup))]

public struct GameTime : IComparable<GameTime>
{
    public uint count;
    public float delta;
    
    public GameTime(uint count, float delta)
    {
        this.count = count;
        this.delta = delta;

        UnityEngine.Assertions.Assert.IsTrue(delta > math.FLT_MIN_NORMAL);
    }
    
    public static implicit operator float(in GameTime value)
    {
        return value.count * value.delta;
    }

    public static implicit operator double(in GameTime value)
    {
        return value.count * value.delta;
    }

    public int CompareTo(GameTime other)
    {
        return count.CompareTo(other.count);
    }

    public override string ToString()
    {
        return ((double)this).ToString();
    }
}

public struct GameDeadline : IEquatable<GameDeadline>, IComparable<GameDeadline>
{
    public uint count;
    public half remainder;
    public readonly float delta;
    
    public static implicit operator double(in GameDeadline value)
    {
        return value.count * value.delta + value.remainder;
    }

    public static implicit operator GameDeadline(in GameTime value)
    {
        return new GameDeadline(value.count, half.zero, value.delta);
    }

    public static bool operator ==(in GameDeadline x, in GameDeadline y)
    {
        return x.Equals(y);
    }

    public static bool operator !=(in GameDeadline x, in GameDeadline y)
    {
        return !x.Equals(y);
    }

    public static GameDeadline operator +(GameDeadline src, float dst)
    {
        if (dst < 0.0f)
            return src - (-dst);

        src.remainder += (half)dst;
        uint count = src.delta > math.FLT_MIN_NORMAL ? (uint)(src.remainder / src.delta) : 0;
        src.count += count;
        src.remainder -= (half)(src.delta * count);

        return src;
    }

    public static GameDeadline operator -(GameDeadline src, float dst)
    {
        if (dst < 0.0f)
            return src + (-dst);

        uint count = src.delta > math.FLT_MIN_NORMAL ? (uint)(dst / src.delta) : 0;
        UnityEngine.Assertions.Assert.IsTrue(src.count >= count);
        src.count -= count;
        dst -= count * src.delta;
        if (dst > src.remainder)
        {
#if DEBUG
            UnityEngine.Assertions.Assert.IsTrue(src.count > 0);
#else
            if(src.count < 1)
            {
                src.count = 0;
                src.remainder.value = 0;
                return src;
            }
#endif

            --src.count;
            src.remainder = (half)(src.delta + src.remainder - dst);
        }
        else
            src.remainder -= (half)dst;

        return src;
    }
    
    public static GameDeadline Max(GameDeadline x, GameDeadline y)
    {
        return x.count < y.count || x.count == y.count && x.remainder < y.remainder ? y : x;
    }

    public static GameDeadline Min(GameDeadline x, GameDeadline y)
    {
        return x.count < y.count || x.count == y.count && x.remainder < y.remainder ? x : y;
    }

    public GameDeadline(uint count, half remainder, float delta)
    {
        UnityEngine.Assertions.Assert.IsTrue(delta > math.FLT_MIN_NORMAL);

        this.count = count;
        this.remainder = remainder;
        this.delta = delta;
    }
    
    public bool Equals(GameDeadline other)
    {
        return count == other.count && remainder == other.remainder && delta == other.delta;
    }

    public override bool Equals(object obj)
    {
        return Equals((GameDeadline)obj);
    }

    public int CompareTo(GameDeadline other)
    {
        int result = count.CompareTo(other.count);
        if (result == 0)
            return remainder.value.CompareTo(other.remainder.value);

        return result;
    }

    public override int GetHashCode()
    {
        return (int)count ^ (remainder.value | remainder.value << 16) ^ math.asint(delta);
    }

    public override string ToString()
    {
        return ((double)this).ToString();
    }
}

/*public struct GameSyncUTCTime : IComponentData
{
    public double value;
}*/

public struct GameSyncUTCTimeOffset : IComponentData
{
    public double value;
}
public struct GameSyncUTCFrame : IComponentData
{
    public uint index;
}

public struct GameSyncVersion : IComponentData
{
    public int value;
}

public struct GameAnimationElapsedTime : IComponentData
{
    public double value;

    public static EntityQuery GetEntityQuery(ref SystemState state)
    {
        using (var builder = new EntityQueryBuilder(Unity.Collections.Allocator.Temp))
            return builder
                .WithAll<GameAnimationElapsedTime>()
                .WithOptions(EntityQueryOptions.IncludeSystems)
                .Build(ref state);
    }
}

/*#if !GAME_RESOURCE_EDIT
public class GameBootStrap : ICustomBootstrap
{
    public bool Initialize(string defaultWorldName)
    {
        var world = new World(defaultWorldName);
        
        World.DefaultGameObjectInjectionWorld = world;
        
        return true;
    }
}
#endif*/

public struct GameSyncTime
{
    public readonly EntityQuery group;

    public readonly int frameOffset
    {
        get => group.GetSingleton<GameRollbackFrameOffset>().value;

        set
        {
            GameRollbackFrameOffset offset;
            offset.value = value;
            group.SetSingleton(offset);
        }
    }

    public readonly uint frameIndex
    {
        get
        {
            return (uint)(frameOffset + group.GetSingleton<FrameSyncReal>().index);
        }
    }

    public readonly float frameDelta => group.GetSingleton<GameRollbackFrameDelta>().value;

    public readonly GameTime time => new GameTime(frameIndex, frameDelta);//__timeSystemGroup.now;

    public readonly GameTime nextTime => new GameTime(frameIndex + 1, frameDelta);

    public GameSyncTime(ref SystemState systemState)
    {
        using (var builder = new EntityQueryBuilder(Unity.Collections.Allocator.Temp))
            group = builder
                    .WithAll<FrameSyncReal, GameRollbackFrameDelta>()
                    .WithAllRW<GameRollbackFrameOffset>()
                    .WithOptions(EntityQueryOptions.IncludeSystems)
                    .Build(ref systemState);
    }
}

public struct GameSyncManager : IComponentData
{
    public readonly GameSyncTime SyncTime;

    public readonly EntityQuery Group;

    public readonly SystemHandle SystemHandle;

    private SystemGroup __systemGroup;

    public bool isVail => version != 0;

    public int version
    {
        get => Group.GetSingleton<GameSyncVersion>().value;

        set
        {
            GameSyncVersion result;
            result.value = value;
            Group.SetSingleton(result);
        }
    }

    public double utcTimeOffset
    {
        get => Group.GetSingleton<GameSyncUTCTimeOffset>().value;

        set
        {
            GameSyncUTCTimeOffset result;
            result.value = value;
            Group.SetSingleton(result);
        }
    }

    /*public double utcTime
    {
        get => Group.GetSingleton<GameSyncUTCTime>().value;

        private set
        {
            GameSyncUTCTime result;
            result.value = value;
            Group.SetSingleton(result);
        }
    }*/

    //public double utcElapsedTime => utcTime - utcTimeOffset;

    //public uint utcFrameIndex => (uint)GetFrameIndex(utcTime);

    public static ComponentType[] ComponentTypes = new ComponentType[]
    {
        ComponentType.ReadWrite<GameSyncVersion>(),
        ComponentType.ReadWrite<GameSyncUTCFrame>(),
        ComponentType.ReadWrite<GameSyncUTCTimeOffset>(),
        //ComponentType.ReadWrite<GameSyncUTCTime>(),
        ComponentType.ReadOnly<GameSyncManager>()
    };

    public static EntityQuery GetEntityQuery(ref SystemState systemState)
    {
        return systemState.GetEntityQuery(new EntityQueryDesc()
        {
            All = new ComponentType[]
            {
                ComponentType.ReadOnly<GameSyncManager>()
            },
            Options = EntityQueryOptions.IncludeSystems
        });
    }

    public GameSyncManager(ref SystemState systemState)
    {
        SyncTime = new GameSyncTime(ref systemState);

        Group = systemState.GetEntityQuery(
            new EntityQueryDesc()
            {
                All = ComponentTypes,
                Options = EntityQueryOptions.IncludeSystems
            });

        __systemGroup = SystemGroupUtility.GetOrCreateSystemGroup(systemState.World, typeof(GameSyncSystemGroup));

        SystemHandle = systemState.SystemHandle;
        var entityManager = systemState.EntityManager;
        entityManager.AddComponent(SystemHandle, new ComponentTypeSet(ComponentTypes));
        entityManager.SetComponentData(SystemHandle, this);
    }

    public long GetFrameIndex(double time)
    {
        return (long)Math.Round((time - utcTimeOffset) / SyncTime.frameDelta);
    }

    public void Clear()
    {
        SyncTime.frameOffset = 0;

        utcTimeOffset = 0.0;

        version = 0;
    }

    public void Reset(int frameOffset, double utcTime)
    {
        UnityEngine.Assertions.Assert.IsFalse(isVail);

        SyncTime.frameOffset += frameOffset;

        __ResetUTCTime(utcTime);

        ++version;
    }

    public void WaitFor(uint frames)
    {
        if (!isVail)
            return;

        //long frameCount = (long)realFrameIndex - utcFrameIndex + frames;

        //frameCount = frames - (frameCount - math.min(frameCount, maxFrameCountForWating));

        utcTimeOffset += frames * (double)SyncTime.frameDelta;
    }

    public void CatchUp(uint frames)
    {
        if (!isVail)
            return;

        /*long utcFrameIndex = this.utcFrameIndex,
            frameCount = utcFrameIndex - realFrameIndex + frames;

        frameCount = frames - (frameCount - math.min(frameCount, maxFrameCountForUpdating));*/

        utcTimeOffset -= frames * (double)SyncTime.frameDelta;
    }

    /*public void UpdateToUTCFrameIndex(double utcTime, ref WorldUnmanaged world)
    {
        //this.utcTime = utcTime;

        if (isVail)
        {
            uint utcFrameIndex = (uint)GetFrameIndex(utcTime);
            for (uint i = SyncTime.frameIndex; i < utcFrameIndex; ++i)
                __systemGroup.Update(ref world);
        }
    }*/

    public void Update(uint upperFrameCount, uint lowerFrameCount, uint maxFrameCountToUpdate, double utcTime, ref WorldUnmanaged world)
    {
        //this.utcTime = utcTime;

        bool isUpdate = true;

        if (isVail)
        {
            //double utcElapsedTime = utcTime - utcTimeOffset;

            /*GameUTCData utcData;
            utcData.elapsedTime = utcElapsedTime;
            EntityManager.SetComponentData(__entity, utcData);*/

            uint utcFrameIndex = (uint)GetFrameIndex(utcTime)/*(uint)GetFrameIndex(utcElapsedTime)*/, realFrameIndex = SyncTime.frameIndex;
            GameSyncUTCFrame utcFrame;
            utcFrame.index = utcFrameIndex;
            world.EntityManager.SetComponentData(SystemHandle, utcFrame);

            if (utcFrameIndex > realFrameIndex + upperFrameCount)
            {
                uint frameCountToUpdate = math.min(utcFrameIndex - (realFrameIndex + (upperFrameCount >> 1)) + 1, maxFrameCountToUpdate);

                //for (uint i = realFrameIndex + (upperFrameCount >> 1); i <= utcFrameIndex; ++i)
                for (uint i = 0; i < frameCountToUpdate; ++i)
                    __systemGroup.Update(ref world);

                //UnityEngine.Debug.Log($"Update Real {realFrameIndex} To UTC {utcFrameIndex}");

                isUpdate = false;
            }
            else if (utcFrameIndex + lowerFrameCount <= realFrameIndex)
                isUpdate = false;
        }

        if (isUpdate)
            __systemGroup.Update(ref world);
    }

    private void __ResetUTCTime(double utcTime)
    {
        utcTimeOffset = utcTime - (double)SyncTime.frameDelta * SyncTime.frameIndex;
    }
}

[CreateAfter(typeof(GameRollbackSystemGroup)), 
    //CreateAfter(typeof(RollbackSystemGroup)),
    UpdateInGroup(typeof(TimeSystemGroup))/*, UpdateAfter(typeof(StateMachineExecutorGroup))*/]
public partial class GameSyncSystemGroup : SystemBase
{
    private GameSyncManager __manager;

    public float animationMaxDelta = 0.05f;

    public uint maxFrameCountPerUpdate = 64;//UnityEngine.Time.maximumDeltaTime;

    //public uint maxFrameCountForWating = 8;
    //public uint maxFrameCountForUpdating = 8;

    public uint upperFrameCount = 0;
    public uint lowerFrameCount = 0;

    //public uint rollbackFrameCount = 3;

    //public uint updateFrameCount = 1;

    public int version => __manager.version;

    //public new uint clearRestoreFrameIndex => base.clearRestoreFrameIndex > 0 ? (uint)(base.clearRestoreFrameIndex + __frameOffset) : realFrameIndex;

    public uint utcFrameIndex => math.max((uint)__manager.GetFrameIndex(GameSyncUtility.utcTime), __manager.SyncTime.frameIndex);

    /*public double utcTime =>
#if UNITY_EDITOR
        World.Time.ElapsedTime;
#else
        DateTime.UtcNow.Ticks / (double)TimeSpan.TicksPerSecond;
#endif*/

    public double animationElapsedTime
    {
        get => SystemAPI.GetSingleton<GameAnimationElapsedTime>().value;

        set
        {
            GameAnimationElapsedTime elapsedTime;
            elapsedTime.value = value;
            SystemAPI.SetSingleton(elapsedTime);
        }
    }

    public GameRollbackManager rollbackManager
    {
        get;

        private set;
    }

    /*public RollbackContainerManager containerManager
    {
        get;

        private set;
    }*/

    public int GetFrameIndex(double time)
    {
        return (int)Math.Round(time / rollbackManager.delta);
    }

    public void Clear()
    {
        rollbackManager.InvokeAll();

        __manager.Clear();

        rollbackManager.Clear();

        //containerManager.Clear();
    }

    public void Reset(int frameOffset)
    {
        animationElapsedTime = -animationMaxDelta * 0.5f;

        __manager.Reset(frameOffset, GameSyncUtility.utcTime);
    }

    public void Wait(uint frames)
    {
        __manager.WaitFor(frames);
    }

    public void Update(uint frames)
    {
        __manager.CatchUp(frames);
    }

    /*public void UpdateToUTCFrameIndex()
    {
        var world = World.Unmanaged;
        __manager.UpdateToUTCFrameIndex(GameSyncUtility.utcTime, ref world);
    }*/

    protected override void OnCreate()
    {
        base.OnCreate();

        __manager = new GameSyncManager(ref this.GetState());

        EntityManager.AddComponent<GameAnimationElapsedTime>(SystemHandle);

        var world = World;
        rollbackManager = world.GetExistingSystemUnmanaged<GameRollbackSystemGroup>().manager;
        //containerManager = world.GetExistingSystemUnmanaged<RollbackSystemGroup>().containerManager;
    }

    protected override void OnUpdate()
    {
        var world = World.Unmanaged;

        //__manager.Update(upperFrameCount, lowerFrameCount, maxFrameCountPerUpdate, utcTime, ref world);
        GameSyncUtility.UpdateFunction(upperFrameCount, lowerFrameCount, maxFrameCountPerUpdate, GameSyncUtility.utcTime, ref world, ref __manager);

        double time = rollbackManager.time, animationElapsedTime = this.animationElapsedTime;
        //if (animationElapsedTime < time)
        {
            animationElapsedTime += UnityEngine.Time.smoothDeltaTime;// World.Time.DeltaTime;

            float delta = animationMaxDelta;// this.delta * updateFrameCount;
            double lastTime = time - delta * 2.0f;
            animationElapsedTime = math.clamp(animationElapsedTime, lastTime, time);

            this.animationElapsedTime = animationElapsedTime;
        }
        /*else
            UnityEngine.Debug.Log($"animationElapsedTime {animationElapsedTime} great than time {time}");*/
    }
}

[BurstCompile]
public static class GameSyncUtility
{
    public delegate void UpdateDelegate(
        uint upperFrameCount,
        uint lowerFrameCount,
        uint maxFrameCountToUpdate,
        double utcTime,
        ref WorldUnmanaged world,
        ref GameSyncManager manager);

    public readonly static UpdateDelegate UpdateFunction = BurstCompiler.CompileFunctionPointer<UpdateDelegate>(Update).Invoke;

    [BurstCompile]
    [MonoPInvokeCallback(typeof(UpdateDelegate))]
    public static void Update(
        uint upperFrameCount, 
        uint lowerFrameCount, 
        uint maxFrameCountToUpdate, 
        double utcTime, 
        ref WorldUnmanaged world, 
        ref GameSyncManager manager)
    {
        manager.Update(upperFrameCount, lowerFrameCount, maxFrameCountToUpdate, utcTime, ref world);
    }

    public static double utcTime =>
#if UNITY_EDITOR
        UnityEngine.Time.timeAsDouble;
#else
        DateTime.UtcNow.Ticks / (double)TimeSpan.TicksPerSecond;
#endif
}

#if GAME_DEBUG_COMPARSION
public struct GameEntityIndex : IComponentData
{
    public uint value;
}

public struct MassProperties : IEquatable<MassProperties>
{
    public Unity.Physics.CollisionFilter filter;
    public Unity.Physics.MassProperties value;

    public MassProperties(ref Unity.Physics.Collider collider)
    {
        this.filter = collider.Filter;
        this.value = collider.MassProperties;
    }

    public bool Equals(MassProperties other)
    {
        return  filter.Equals(other.filter) && 
            value.MassDistribution.Transform.Equals(other.value.MassDistribution.Transform) &&
            value.MassDistribution.InertiaTensor.Equals(other.value.MassDistribution.InertiaTensor) &&
            value.Volume == other.value.Volume &&
            value.AngularExpansionFactor == other.value.AngularExpansionFactor;
    }
}

[DisableAutoCreation]
public partial class GameComparsionSystem : ComparisionSystem<uint>
{
    private static GameComparsionSystem __instance;

    public static GameComparsionSystem instance
    {
        get
        {
            if (__instance == null)
                __instance = World.DefaultGameObjectInjectionWorld.CreateSystemManaged<GameComparsionSystem>();

            return __instance;
        }
    }
}

#endif