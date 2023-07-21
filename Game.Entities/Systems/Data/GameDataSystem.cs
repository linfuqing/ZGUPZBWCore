﻿using System;
using System.IO;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;
using ZG;

public enum GameDataConstans
{
    Version = 5, 
    BuiltInCamps = 32,
}

public struct GameDataCommon : IComponentData
{
    public FixedString4096Bytes path;
}

[AutoCreateIn("Server"), UpdateInGroup(typeof(InitializationSystemGroup)), UpdateBefore(typeof(GameItemInitSystemGroup))]
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

    public void OnUpdate(ref SystemState state)
    {
        var world = state.WorldUnmanaged;
        __systemGroup.Update(ref world);
    }
}

//BeginFrameEntityCommandSystem 为了在GameItemEntitySystem之前
[AutoCreateIn("Server"), 
    UpdateInGroup(typeof(InitializationSystemGroup)), 
    //UpdateBefore(typeof(GameItemInitSystemGroup)), 
    UpdateBefore(typeof(GameDataSystemGroup)),
    UpdateAfter(typeof(BeginFrameEntityCommandSystem))]
public partial class GameDataDeserializationSystemGroup : EntityDataDeserializationSystemGroup
{
    private string __filePath;
    private EntityDataDeserializationCommander __commander;

    public bool isDone => path != null && !Enabled;

    public string path
    {
        get;

        private set;
    }

    public override NativeArray<Hash128>.ReadOnly types => SystemAPI.GetSingleton<EntityDataCommon>().typesGUIDs;

    public bool Activate(string path, EntityDataDeserializationCommander commander/*, Hash128[] types, ref Entity entity*/)
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

        __commander = commander;

        Enabled = true;

        return true;
    }

    public override EntityDataDeserializationCommander CreateCommander() => __commander;

    protected override void OnCreate()
    {
        base.OnCreate();

        Enabled = false;
    }

    protected override byte[] _GetBytes()
    {
        return File.ReadAllBytes(__filePath);
    }
}

[AutoCreateIn("Server"), UpdateInGroup(typeof(InitializationSystemGroup)),  UpdateAfter(typeof(GameDataDeserializationSystemGroup)),  UpdateAfter(typeof(GameItemInitSystemGroup))]
public partial class GameDataSerializationSystemGroup : EntityDataSerializationManagedSystem
{
    public double time = 600.0f;

    public int maxCount = 1024;

    private int __times;

    private SystemHandle __systemHandle;

    private List<string> __guids;

    private GameDataDeserializationSystemGroup __deserializationSystemGroup;

    public override int version => (int)GameDataConstans.Version;

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
        __Save();

        base.OnStopRunning();
    }

    protected override void OnUpdate()
    {
        int times = (int)math.floor(World.Time.ElapsedTime / time);
        if (times > __times)
        {
            __times = times;

            __Save();
        }
    }

    private void __Save()
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

                if(File.Exists(pathToDelete))
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
}