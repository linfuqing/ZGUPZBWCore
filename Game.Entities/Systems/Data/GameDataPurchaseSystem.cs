using System;
using Unity.Jobs;
using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;
using ZG;

#region GamePurchaseManager
[assembly: RegisterGenericJobType(typeof(EntityDataContainerSerialize<GamePurchaseManager.Serializer>))]
[assembly: RegisterGenericJobType(typeof(EntityDataContainerDeserialize<GamePurchaseManager.Deserializer>))]
[assembly: EntityDataSerialize(typeof(GamePurchaseManager), typeof(GameDataPurchaseSerializationSystem))]
[assembly: EntityDataDeserialize(typeof(GamePurchaseManager), typeof(GameDataPurchaseDeserializationSystem), (int)GameDataConstans.Version)]
#endregion

public struct GamePurchaseManager
{
    private struct Purchase
    {
        public int userID;

        public int count;
    }

    public struct Serializer : IEntityDataContainerSerializer
    {
        [ReadOnly]
        private NativeParallelHashMap<int, Purchase> __values;
        [ReadOnly]
        private NativeParallelMultiHashMap<int, int> __commands;

        public Serializer(in GamePurchaseManager manager)
        {
            __values = manager.__values;
            __commands = manager.__commands;
        }

        public void Serialize(ref NativeBuffer.Writer writer)
        {
            writer.Serialize(__values);
            writer.Serialize(__commands);
        }
    }

    public struct Deserializer : IEntityDataContainerDeserializer
    {
        private NativeParallelHashMap<int, Purchase> __values;
        private NativeParallelMultiHashMap<int, int> __commands;

        public Deserializer(ref GamePurchaseManager manager)
        {
            __values = manager.__values;
            __commands = manager.__commands;
        }

        public void Deserialize(in UnsafeBlock block)
        {
            var reader = block.reader;

            reader.Deserialize(ref __values);
            reader.Deserialize(ref __commands);
        }
    }

    private NativeParallelHashMap<int, Purchase> __values;
    private NativeParallelMultiHashMap<int, int> __commands;

    public GamePurchaseManager(Allocator allocator)
    {
        __values = new NativeParallelHashMap<int, Purchase>(1, allocator);

        __commands = new NativeParallelMultiHashMap<int, int>(1, allocator);
    }

    public void Dispose()
    {
        __values.Dispose();

        __commands.Dispose();
    }

    public int CountOf(int id)
    {
        return __values.TryGetValue(id, out var value) ? value.count : 0;
    }

    public int Next(int userID)
    {
        if (__commands.TryGetFirstValue(userID, out int commandID, out _))
            return commandID;

        return 0;
    }

    public int Command(int userID, int id)
    {
        if (__commands.TryGetFirstValue(userID, out int commandID, out var iterator))
        {
            do
            {
                if (commandID == id)
                    return -1;
            } while (__commands.TryGetNextValue(out commandID, ref iterator));
        }

        __commands.Add(userID, id);

        if (__values.TryGetValue(id, out var value))
        {
            if (value.userID != userID)
                return -1;
        }
        else
        {
            value.count = 0;
            value.userID = userID;

            __values[id] = value;
        }
        
        return value.count;
    }

    public int Apply(int id, int count)
    {
        if (__values.TryGetValue(id, out var value) && 
            __commands.TryGetFirstValue(value.userID, out int commandID, out var iterator))
        {
            do
            {
                if (commandID == id)
                {
                    /*if (count == 0)
                        __values.Remove(id);
                    else
                    {
                        value.count = count;

                        __values[id] = value;
                    }*/

                    __commands.Remove(iterator);

                    if(value.count < count)
                    {
                        int result = count - value.count;

                        value.count = count;

                        __values[id] = value;

                        return result;
                    }

                    break;
                }
            } while (__commands.TryGetNextValue(out commandID, ref iterator));
        }

        return 0;
    }

    public Serializer AsSerializer()
    {
        return new Serializer(this);
    }

    public Deserializer AsDeserializer()
    {
        return new Deserializer(ref this);
    }
}

[AutoCreateIn("Server")]
public partial class GamePurchaseSystem : LookupSystem
{
    public GamePurchaseManager manager
    {
        get;

        private set;
    }

    public GamePurchaseManager GetManagerReadOnly()
    {
        _lookupJobManager.CompleteReadOnlyDependency();

        return manager;
    }

    public GamePurchaseManager GetManagerReadWrite()
    {
        _lookupJobManager.CompleteReadWriteDependency();

        return manager;
    }

    protected override void OnCreate()
    {
        base.OnCreate();

        manager = new GamePurchaseManager(Allocator.Persistent);

        Enabled = false;
    }

    protected override void OnDestroy()
    {
        manager.Dispose();

        base.OnDestroy();
    }

    protected override void _Update()
    {
        throw new NotImplementedException();
    }
}


[DisableAutoCreation]
public partial class GameDataPurchaseSerializationSystem : EntityDataSerializationContainerSystem<GamePurchaseManager.Serializer>
{
    private GamePurchaseSystem __purchaseSystem;

    protected override void OnCreate()
    {
        base.OnCreate();

        __purchaseSystem = World.GetOrCreateSystemManaged<GamePurchaseSystem>();
    }

    protected override void OnUpdate()
    {
        Dependency = JobHandle.CombineDependencies(Dependency, __purchaseSystem.readOnlyJobHandle);

        base.OnUpdate();

        __purchaseSystem.AddReadOnlyDependency(Dependency);
    }

    protected override GamePurchaseManager.Serializer _Get() => __purchaseSystem.manager.AsSerializer();
}

[DisableAutoCreation]
public partial class GameDataPurchaseDeserializationSystem : EntityDataDeserializationContainerSystem<GamePurchaseManager.Deserializer>
{
    private GamePurchaseSystem __purchaseSystem;

    protected override void OnCreate()
    {
        base.OnCreate();

        __purchaseSystem = World.GetOrCreateSystemManaged<GamePurchaseSystem>();
    }

    protected override void OnUpdate()
    {
        Dependency = JobHandle.CombineDependencies(Dependency, __purchaseSystem.readWriteJobHandle);

        base.OnUpdate();

        __purchaseSystem.readWriteJobHandle = Dependency;
    }

    protected override GamePurchaseManager.Deserializer _Create(ref JobHandle jobHandle) => __purchaseSystem.manager.AsDeserializer();
}
