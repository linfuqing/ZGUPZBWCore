using System;
using Unity.Jobs;
using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;
using ZG;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Burst;
using System.Diagnostics;

#region GamePurchaseManager
[assembly: RegisterGenericJobType(typeof(EntityDataContainerSerialize<GamePurchaseManagerShared.Serializer>))]
[assembly: RegisterGenericJobType(typeof(EntityDataContainerDeserialize<GamePurchaseManagerShared.Deserializer>))]
//[assembly: EntityDataSerialize(typeof(GamePurchaseManager), typeof(GameDataPurchaseSerializationSystem))]
//[assembly: EntityDataDeserialize(typeof(GamePurchaseManager), typeof(GameDataPurchaseDeserializationSystem), (int)GameDataConstans.Version)]
#endregion

public struct GamePurchaseManager
{
    private struct Purchase
    {
        public int userID;

        public int count;
    }

    private UnsafeHashMap<int, Purchase> __values;
    private UnsafeParallelMultiHashMap<int, int> __commands;

    public GamePurchaseManager(in AllocatorManager.AllocatorHandle allocator)
    {
        __values = new UnsafeHashMap<int, Purchase>(1, allocator);

        __commands = new UnsafeParallelMultiHashMap<int, int>(1, allocator);
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

    public void Serialize(ref NativeBuffer.Writer writer)
    {
        writer.Serialize(__values);
        writer.Serialize(__commands);
    }

    public void Deserialize(in UnsafeBlock block)
    {
        var reader = block.reader;

        reader.Deserialize(ref __values);
        reader.Deserialize(ref __commands);
    }
}

public struct GamePurchaseManagerShared
{
    private struct Data
    {
        public GamePurchaseManager value;

        public LookupJobManager lookupJobManager;
    }

    [NativeContainer]
    [NativeContainerIsReadOnly]
    public unsafe struct Serializer : IEntityDataContainerSerializer
    {
        [NativeDisableUnsafePtrRestriction]
        private GamePurchaseManager* __manager;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        internal AtomicSafetyHandle m_Safety;

        internal static readonly SharedStatic<int> StaticSafetyID = SharedStatic<int>.GetOrCreate<Serializer>();
#endif

        public Serializer(ref GamePurchaseManagerShared manager)
        {
            __manager = (GamePurchaseManager*)UnsafeUtility.AddressOf(ref manager.__data->value);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            m_Safety = manager.m_Safety;

            CollectionHelper.SetStaticSafetyId<Serializer>(ref m_Safety, ref StaticSafetyID.Data);
#endif
        }

        public void Serialize(ref NativeBuffer.Writer writer)
        {
            __CheckRead();

            __manager->Serialize(ref writer);
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private void __CheckRead()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif
        }
    }

    [NativeContainer]
    public unsafe struct Deserializer : IEntityDataContainerDeserializer
    {
        [NativeDisableUnsafePtrRestriction]
        private GamePurchaseManager* __manager;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        internal AtomicSafetyHandle m_Safety;

        internal static readonly SharedStatic<int> StaticSafetyID = SharedStatic<int>.GetOrCreate<Deserializer>();
#endif

        public Deserializer(ref GamePurchaseManagerShared manager)
        {
            __manager = (GamePurchaseManager*)UnsafeUtility.AddressOf(ref manager.__data->value);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            m_Safety = manager.m_Safety;

            CollectionHelper.SetStaticSafetyId<Deserializer>(ref m_Safety, ref StaticSafetyID.Data);
#endif
        }

        public void Deserialize(in UnsafeBlock block)
        {
            __CheckWrite();

            __manager->Deserialize(block);
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private void __CheckWrite()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndBumpSecondaryVersion(m_Safety);
#endif
        }
    }

    public readonly AllocatorManager.AllocatorHandle Allocator;

    private unsafe Data* __data;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
    internal AtomicSafetyHandle m_Safety;
#endif

    public unsafe ref LookupJobManager lookupJobManager => ref __data->lookupJobManager;

    public Serializer serializer => new Serializer(ref this);

    public Deserializer deserializer => new Deserializer(ref this);

    public unsafe GamePurchaseManagerShared(in AllocatorManager.AllocatorHandle allocator)
    {
        Allocator = allocator;

        __data = AllocatorManager.Allocate<Data>(allocator);

        __data->value = new GamePurchaseManager(allocator);
        __data->lookupJobManager = default;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        m_Safety = CollectionHelper.CreateSafetyHandle(allocator);
#endif
    }

    public unsafe void Dispose()
    {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        CollectionHelper.DisposeSafetyHandle(ref m_Safety);
#endif

        __data->value.Dispose();

        AllocatorManager.Free(Allocator, __data);

        __data = null;
    }

    public void CompleteReadOnlyDependency()
    {
        lookupJobManager.CompleteReadOnlyDependency();

        __CheckRead();
    }

    public void CompleteReadWriteDependency()
    {
        lookupJobManager.CompleteReadWriteDependency();

        __CheckWrite();
    }

    public unsafe int CountOf(int id)
    {
        CompleteReadOnlyDependency();

        return __data->value.CountOf(id);
    }

    public unsafe int Next(int userID)
    {
        CompleteReadOnlyDependency();

        return __data->value.Next(userID);
    }

    public unsafe int Command(int userID, int id)
    {
        CompleteReadWriteDependency();

        return __data->value.Command(userID, id);
    }

    public unsafe int Apply(int id, int count)
    {
        CompleteReadWriteDependency();

        return __data->value.Apply(id, count);
    }

    [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
    private void __CheckRead()
    {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif
    }

    [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
    private void __CheckWrite()
    {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        AtomicSafetyHandle.CheckWriteAndBumpSecondaryVersion(m_Safety);
#endif
    }

}

[AutoCreateIn("Server"), BurstCompile]
public partial struct GamePurchaseSystem : ISystem
{
    public GamePurchaseManagerShared manager
    {
        get;

        private set;
    }

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        manager = new GamePurchaseManagerShared(Allocator.Persistent);

        state.Enabled = false;
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        manager.Dispose();
    }

    public void OnUpdate(ref SystemState state)
    {
        throw new NotImplementedException();
    }
}

[BurstCompile,
    EntityDataSerializationSystem(typeof(GamePurchaseManager)),
    CreateAfter(typeof(GamePurchaseSystem)),
    CreateAfter(typeof(EntityDataSerializationInitializationSystem)),
    UpdateInGroup(typeof(EntityDataSerializationSystemGroup)), AutoCreateIn("Server")]
public partial struct GameDataPurchaseSerializationContainerSystem : ISystem
{
    private EntityDataSerializationTypeHandle __typeHandle;
    private GamePurchaseManagerShared __manager;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __typeHandle = new EntityDataSerializationTypeHandle(ref state);

        var world = state.WorldUnmanaged;

        __manager = world.GetExistingSystemUnmanaged<GamePurchaseSystem>().manager;
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        ref var managerJobManager = ref __manager.lookupJobManager;
        state.Dependency = JobHandle.CombineDependencies(state.Dependency, managerJobManager.readOnlyJobHandle);

        var serializer = __manager.serializer;

        __typeHandle.Update(ref serializer, ref state);

        var jobHandle = state.Dependency;

        managerJobManager.AddReadOnlyDependency(jobHandle);
    }
}

[BurstCompile,
    EntityDataDeserializationSystem(typeof(GamePurchaseManager), (int)GameDataConstans.Version),
    CreateAfter(typeof(EntityDataDeserializationContainerSystem)),
    CreateAfter(typeof(GamePurchaseSystem)),
    UpdateInGroup(typeof(EntityDataDeserializationSystemGroup)), AutoCreateIn("Server")]
public partial struct GameDataPurchaseDeserializationContainerSystem : ISystem
{
    private GamePurchaseManagerShared __manager;

    private EntityDataDeserializationContainerSystemCore __core;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __manager = state.WorldUnmanaged.GetExistingSystemUnmanaged<GamePurchaseSystem>().manager;

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
        ref var managerJobManager = ref __manager.lookupJobManager;

        state.Dependency = JobHandle.CombineDependencies(managerJobManager.readWriteJobHandle, state.Dependency);

        var deserializer = __manager.deserializer;

        __core.Update(ref deserializer, ref state);

        managerJobManager.readWriteJobHandle = state.Dependency;
    }
}
