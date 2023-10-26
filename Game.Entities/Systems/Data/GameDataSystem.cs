using System;
using System.IO;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;
using ZG;
using Unity.Burst;

public enum GameDataConstans
{
    Version = 6, 
    BuiltInCamps = 32,
}

public struct GameDataCommon : IComponentData
{
    public FixedString4096Bytes path;
}

[BurstCompile, 
    UpdateInGroup(typeof(InitializationSystemGroup)), 
    UpdateBefore(typeof(GameItemInitSystemGroup)), 
    UpdateAfter(typeof(GameItemSystemGroup)), AutoCreateIn("Server")]
public partial struct GameDataSystemGroup : ISystem
{
    private SystemGroup __systemGroup;
    private NativeArray<Hash128> __types;

    public static void Activate(World world, string path, Hash128[] types)
    {
        var systemHandle = world.GetOrCreateSystem<GameDataSystemGroup>();

        ref var systemGroup = ref world.Unmanaged.GetUnsafeSystemRef<GameDataSystemGroup>(systemHandle);

        systemGroup.__types = new NativeArray<Hash128>(types, Allocator.Persistent);

        var entityManager = world.EntityManager;

        EntityDataCommon entityDatacommon;
        entityDatacommon.typesGUIDs = systemGroup.__types.AsReadOnly();
        entityManager.AddComponentData(systemHandle, entityDatacommon);

        GameDataCommon gameDataCommon;
        gameDataCommon.path = Path.Combine(UnityEngine.Application.persistentDataPath, path);
        entityManager.AddComponentData(systemHandle, gameDataCommon);
    }

    public void OnCreate(ref SystemState state)
    {
        __systemGroup = SystemGroupUtility.GetOrCreateSystemGroup(state.World, typeof(GameDataSystemGroup));
    }

    public void OnDestroy(ref SystemState state)
    {
        //__systemGroup.Dispose();
        if (__types.IsCreated)
            __types.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var world = state.WorldUnmanaged;
        __systemGroup.Update(ref world);
    }
}

[BurstCompile, 
    UpdateInGroup(typeof(GameDataSystemGroup), OrderLast = true), AutoCreateIn("Server")]
public partial struct GameDataStructChangeSystem : ISystem
{
    /*private struct Assigner : EntityCommandStructChangeManager.IAssigner
    {
        public EntityComponentAssigner instance;

        public void Playback(ref SystemState systemState)
        {
            instance.Playback(ref systemState);
        }
    }*/

    //private EntityQuery __group;

    public EntityCommandStructChangeManager manager
    {
        get;

        private set;
    }

    public EntityComponentAssigner assigner
    {
        get;

        private set;
    }

    public EntityAddDataPool addDataPool => new EntityAddDataPool(manager.addComponentPool, assigner);

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        manager = new EntityCommandStructChangeManager(Allocator.Persistent);

        assigner = new EntityComponentAssigner(Allocator.Persistent);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        manager.Dispose();

        assigner.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        /*Assigner assigner;
        assigner.instance = this.assigner;*/
        manager.Playback(ref state/*, ref assigner*/);

        assigner.Playback(ref state);
    }
}

//BeginFrameEntityCommandSystem 为了在GameItemEntitySystem之前
[AutoCreateIn("Server"), 
    UpdateInGroup(typeof(InitializationSystemGroup)), 
    //UpdateBefore(typeof(GameItemInitSystemGroup)), 
    UpdateBefore(typeof(GameDataSystemGroup))/*,
    UpdateAfter(typeof(BeginFrameEntityCommandSystem))*/]
public partial class GameDataDeserializationSystemGroup : EntityDataDeserializationManagedSystem
{
    private string __filePath;
    private SystemHandle __system;
    private EntityDataDeserializationBuilder __builder;
    //private EntityDataDeserializationCommander __commander;

    public bool isDone => path != null && !Enabled;

    public string path
    {
        get;

        private set;
    }

    //public override NativeArray<Hash128>.ReadOnly types => SystemAPI.GetSingleton<EntityDataCommon>().typesGUIDs;

    public bool Activate(string path/*, EntityDataDeserializationCommander commander, Hash128[] types, ref Entity entity*/)
    {
        path = Path.Combine(UnityEngine.Application.persistentDataPath, path);
        this.path = path;
        if (!File.Exists(path))
            return false;

        string[] lines = File.ReadAllLines(path);
        string folder = Path.GetDirectoryName(path);
        int numLines = lines.Length, i;
        for (i = numLines - 1; i >= 0; --i)
        {
            __filePath = Path.Combine(folder, lines[i]);
            if (File.Exists(__filePath))
                break;
        }

        if (i < 0)
            return false;

        //__commander = commander;

        return Build(out _, out _);
    }

    //public override EntityDataDeserializationCommander CreateCommander() => __commander;

    protected override void OnCreate()
    {
        base.OnCreate();

        __system = World.GetOrCreateSystem<EntityDataDeserializationSystemGroup>();
    }

    protected override void OnUpdate()
    {
        switch(EntityManager.GetComponentData<EntityDataDeserializationStatus>(__system).value)
        {
            case EntityDataDeserializationStatus.Value.None:
                if (SystemAPI.TryGetSingleton(out GameDataCommon common))
                {
                    bool result = Activate(common.path.ToString());

                    if(result)
                        __system.Update(World.Unmanaged);

                    Enabled = result;
                }

                break;
            case EntityDataDeserializationStatus.Value.Complete:
                Enabled = false;
                break;
            default:
                __system.Update(World.Unmanaged);
                break;
        }
    }

    public override byte[] GetBytes()
    {
        return File.ReadAllBytes(__filePath);
    }
}

[AutoCreateIn("Server"), 
    UpdateInGroup(typeof(InitializationSystemGroup)),  
    UpdateAfter(typeof(GameDataDeserializationSystemGroup)),  
    UpdateAfter(typeof(GameItemInitSystemGroup))]
public partial class GameDataSerializationSystemGroup : EntityDataSerializationManagedSystem
{
    public double time = 600.0f;

    public int maxCount = 1024;

    private int __times;

    private SystemHandle __systemHandle;

    private List<string> __guids;

    private GameDataDeserializationSystemGroup __deserializationSystemGroup;

    public override int version => (int)GameDataConstans.Version;

    public void Save()
    {
        if (!__deserializationSystemGroup.isDone)
            return;

        string path = __deserializationSystemGroup.path;
        string folder = Path.GetDirectoryName(path);
        if (!Directory.Exists(folder))
        {
            var info = Directory.CreateDirectory(folder);
            if (info == null || !info.Exists)
            {
                UnityEngine.Debug.LogError($"Save Fail: {folder}");

                return;
            }
        }

        //base.OnUpdate();
        var world = World.Unmanaged;
        __systemHandle.Update(world);

        IEnumerable<string> lines = File.Exists(path) ? File.ReadLines(path) : Array.Empty<string>();
        if (__guids == null)
            __guids = new List<string>(lines);
        else
        {
            __guids.Clear();
            __guids.AddRange(lines);
        }

        int count = __guids.Count + 1;
        if (count > maxCount)
        {
            string pathToDelete;
            for (int i = maxCount; i < count; ++i)
            {
                pathToDelete = Path.Combine(folder, __guids[i - maxCount]);

                if (File.Exists(pathToDelete))
                    File.Delete(pathToDelete);
            }

            __guids.RemoveRange(0, count - maxCount);
        }

        string guid;
        do
        {
            guid = Guid.NewGuid().ToString();
        } while (__guids.IndexOf(guid) != -1);

        File.WriteAllBytes(Path.Combine(folder, guid), world.GetUnsafeSystemRef<EntityDataSerializationSystemGroup>(__systemHandle).ToBytes());

        if (count > maxCount)
        {
            __guids.Add(guid);

            File.WriteAllLines(path, __guids);
        }
        else
        {
            __guids.Clear();
            __guids.Add(guid);

            File.AppendAllLines(path, __guids);
        }
    }

    protected override void OnCreate()
    {
        base.OnCreate();

        var world = World;

        __deserializationSystemGroup = world.GetOrCreateSystemManaged<GameDataDeserializationSystemGroup>();
    }

    protected override void OnStartRunning()
    {
        base.OnStartRunning();

        __systemHandle = World.GetExistingSystem<EntityDataSerializationSystemGroup>();
    }

    protected override void OnStopRunning()
    {
        Save();

        base.OnStopRunning();
    }

    protected override void OnUpdate()
    {
        int times = (int)math.floor(World.Time.ElapsedTime / time);
        if (times > __times)
        {
            __times = times;

            Save();
        }
    }
}