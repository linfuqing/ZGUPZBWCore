using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using ZG;

#region GameOwner
[assembly: RegisterGenericJobType(typeof(EntityDataComponentSerialize<GameDataEntityComponentDataSerializer<GameOwner, GameOwnerDataWrapper>, GameDataEntityComponentDataSerializerFactory<GameOwner, GameOwnerDataWrapper>>))]
[assembly: RegisterGenericJobType(typeof(EntityDataComponentDeserialize<GameDataEntityComponentDataDeserializer<GameOwner, GameOwnerDataWrapper>, GameDataEntityComponentDataDeserializerFactory<GameOwner, GameOwnerDataWrapper>>))]
[assembly: RegisterGenericJobType(typeof(GameDataEntityComponentDataBuild<GameOwner, GameOwnerDataWrapper>))]
//[assembly: EntityDataSerialize(typeof(GameOwner), typeof(GameDataEntityCompoentDataSerializationSystem<GameOwner>))]
//[assembly: EntityDataDeserialize(typeof(GameOwner), typeof(GameDataEntityCompoentDataDeserializationSystem<GameOwner>), (int)GameDataConstans.Version)]
#endregion

#region GameActorMaster
[assembly: RegisterGenericJobType(typeof(EntityDataComponentSerialize<GameDataEntityComponentDataSerializer<GameActorMaster, GameActorMasterDataWrapper>, GameDataEntityComponentDataSerializerFactory<GameActorMaster, GameActorMasterDataWrapper>>))]
[assembly: RegisterGenericJobType(typeof(EntityDataComponentDeserialize<GameDataEntityComponentDataDeserializer<GameActorMaster, GameActorMasterDataWrapper>, GameDataEntityComponentDataDeserializerFactory<GameActorMaster, GameActorMasterDataWrapper>>))]
[assembly: RegisterGenericJobType(typeof(GameDataEntityComponentDataBuild<GameActorMaster, GameActorMasterDataWrapper>))]
//[assembly: EntityDataSerialize(typeof(GameActorMaster), typeof(GameDataEntityCompoentDataSerializationSystem<GameActorMaster>))]
//[assembly: EntityDataDeserialize(typeof(GameActorMaster), typeof(GameDataEntityCompoentDataDeserializationSystem<GameActorMaster>), (int)GameDataConstans.Version)]
#endregion

#region GamePlayerLocator
[assembly: RegisterGenericJobType(typeof(EntityDataComponentSerialize<GameDataEntityComponentDataSerializer<GamePlayerLocator, GamePlayerLocatorDataWrapper>, GameDataEntityComponentDataSerializerFactory<GamePlayerLocator, GamePlayerLocatorDataWrapper>>))]
[assembly: RegisterGenericJobType(typeof(EntityDataComponentDeserialize<GameDataEntityComponentDataDeserializer<GamePlayerLocator, GamePlayerLocatorDataWrapper>, GameDataEntityComponentDataDeserializerFactory<GamePlayerLocator, GamePlayerLocatorDataWrapper>>))]
[assembly: RegisterGenericJobType(typeof(GameDataEntityComponentDataBuild<GamePlayerLocator, GamePlayerLocatorDataWrapper>))]
//[assembly: EntityDataSerialize(typeof(GamePlayerLocator), typeof(GameDataEntityCompoentDataSerializationSystem<GamePlayerLocator>))]
//[assembly: EntityDataDeserialize(typeof(GamePlayerLocator), typeof(GameDataEntityCompoentDataDeserializationSystem<GamePlayerLocator>), (int)GameDataConstans.Version)]
#endregion

#region GamePlayerSpawn
[assembly: RegisterGenericJobType(typeof(EntityDataComponentSerialize<GameDataEntityComponentDataSerializer<GamePlayerSpawn, GamePlayerSpawnDataWrapper>, GameDataEntityComponentDataSerializerFactory<GamePlayerSpawn, GamePlayerSpawnDataWrapper>>))]
[assembly: RegisterGenericJobType(typeof(EntityDataComponentDeserialize<GameDataEntityComponentDataDeserializer<GamePlayerSpawn, GamePlayerSpawnDataWrapper>, GameDataEntityComponentDataDeserializerFactory<GamePlayerSpawn, GamePlayerSpawnDataWrapper>>))]
[assembly: RegisterGenericJobType(typeof(GameDataEntityComponentDataBuild<GamePlayerSpawn, GamePlayerSpawnDataWrapper>))]
//[assembly: EntityDataSerialize(typeof(GamePlayerSpawn), typeof(GameDataEntityCompoentDataSerializationSystem<GamePlayerSpawn>))]
//[assembly: EntityDataDeserialize(typeof(GamePlayerSpawn), typeof(GameDataEntityCompoentDataDeserializationSystem<GamePlayerSpawn>), (int)GameDataConstans.Version)]
#endregion

public struct GameOwnerDataWrapper : IGameDataEntityCompoentWrapper<GameOwner>
{
    public Entity Get(in GameOwner value)
    {
        return value.entity;
    }

    public void Set(ref GameOwner value, in Entity entity, in Entity instance)
    {
        value.entity = entity;
    }

    public void Serialize(in GameOwner value, int entityIndex, ref EntityDataWriter writer)
    {
        writer.Write(entityIndex);
    }

    public int Deserialize(in Entity entity, ref GameOwner value, ref EntityDataReader reader)
    {
        return reader.Read<int>();
    }

    public bool Fallback(in Entity entity, in GameOwner value)
    {
        return false;
    }
}

public struct GameActorMasterDataWrapper : IGameDataEntityCompoentWrapper<GameActorMaster>
{
    public Entity Get(in GameActorMaster value)
    {
        return value.entity;
    }

    public void Set(ref GameActorMaster value, in Entity entity, in Entity instance)
    {
        value.entity = entity;
    }

    public void Serialize(in GameActorMaster value, int entityIndex, ref EntityDataWriter writer)
    {
        writer.Write(entityIndex);
    }

    public int Deserialize(in Entity entity, ref GameActorMaster value, ref EntityDataReader reader)
    {
        return reader.Read<int>();
    }

    public bool Fallback(in Entity entity, in GameActorMaster value)
    {
        return false;
    }
}

public struct GamePlayerLocator : IComponentData
{
    public Entity entity;
}

public struct GamePlayerLocatorDataWrapper : IGameDataEntityCompoentWrapper<GamePlayerLocator>
{
    public Entity Get(in GamePlayerLocator value)
    {
        return value.entity;
    }

    public void Set(ref GamePlayerLocator value, in Entity entity, in Entity instance)
    {
        value.entity = entity;
    }

    public void Serialize(in GamePlayerLocator value, int entityIndex, ref EntityDataWriter writer)
    {
        writer.Write(entityIndex);
    }

    public int Deserialize(in Entity entity, ref GamePlayerLocator value, ref EntityDataReader reader)
    {
        return reader.Read<int>();
    }

    public bool Fallback(in Entity entity, in GamePlayerLocator value)
    {
        return false;
    }
}

[EntityDataTypeName("GameSpawn")]
public struct GamePlayerSpawn : IComponentData
{
    public Entity entity;
}

public struct GamePlayerSpawnDataWrapper : IGameDataEntityCompoentWrapper<GamePlayerSpawn>
{
    public Entity Get(in GamePlayerSpawn value)
    {
        return value.entity;
    }

    public void Set(ref GamePlayerSpawn value, in Entity entity, in Entity instance)
    {
        value.entity = entity;
    }

    public void Serialize(in GamePlayerSpawn value, int entityIndex, ref EntityDataWriter writer)
    {
        writer.Write(entityIndex);
    }

    public int Deserialize(in Entity entity, ref GamePlayerSpawn value, ref EntityDataReader reader)
    {
        return reader.Read<int>();
    }

    public bool Fallback(in Entity entity, in GamePlayerSpawn value)
    {
        return false;
    }
}

#region Serialization
[BurstCompile,
    EntityDataSerializationSystem(typeof(GameOwner)),
    CreateAfter(typeof(EntityDataSerializationInitializationSystem)),
    UpdateInGroup(typeof(EntityDataSerializationSystemGroup)), AutoCreateIn("Server")]
public partial struct GameDataOwnerSerializationSystem : ISystem
{
    private GameDataEntityComponentDataSerializationSystemCore __core;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __core = new GameDataEntityComponentDataSerializationSystemCore(TypeManager.GetTypeIndex<GameOwner>(), ref state);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        __core.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        GameOwnerDataWrapper wrapper;
        __core.Update<GameOwner, GameOwnerDataWrapper>(ref wrapper, ref state);
    }
}

[BurstCompile,
    EntityDataSerializationSystem(typeof(GameActorMaster)),
    CreateAfter(typeof(EntityDataSerializationInitializationSystem)),
    UpdateInGroup(typeof(EntityDataSerializationSystemGroup)), AutoCreateIn("Server")]
public partial struct GameDataActorMasterSerializationSystem : ISystem
{
    private GameDataEntityComponentDataSerializationSystemCore __core;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __core = new GameDataEntityComponentDataSerializationSystemCore(TypeManager.GetTypeIndex<GameActorMaster>(), ref state);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        __core.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        GameActorMasterDataWrapper wrapper;
        __core.Update<GameActorMaster, GameActorMasterDataWrapper>(ref wrapper, ref state);
    }
}

[BurstCompile,
    EntityDataSerializationSystem(typeof(GamePlayerLocator)),
    CreateAfter(typeof(EntityDataSerializationInitializationSystem)),
    UpdateInGroup(typeof(EntityDataSerializationSystemGroup)), AutoCreateIn("Server")]
public partial struct GameDataPlayerLocatorSerializationSystem : ISystem
{
    private GameDataEntityComponentDataSerializationSystemCore __core;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __core = new GameDataEntityComponentDataSerializationSystemCore(TypeManager.GetTypeIndex<GamePlayerLocator>(), ref state);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        __core.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        GamePlayerLocatorDataWrapper wrapper;
        __core.Update<GamePlayerLocator, GamePlayerLocatorDataWrapper>(ref wrapper, ref state);
    }
}

[BurstCompile,
    EntityDataSerializationSystem(typeof(GamePlayerSpawn)),
    CreateAfter(typeof(EntityDataSerializationInitializationSystem)),
    UpdateInGroup(typeof(EntityDataSerializationSystemGroup)), AutoCreateIn("Server")]
public partial struct GameDataPlayerSpawnSerializationSystem : ISystem
{
    private GameDataEntityComponentDataSerializationSystemCore __core;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __core = new GameDataEntityComponentDataSerializationSystemCore(TypeManager.GetTypeIndex<GamePlayerSpawn>(), ref state);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        __core.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        GamePlayerSpawnDataWrapper wrapper;
        __core.Update<GamePlayerSpawn, GamePlayerSpawnDataWrapper>(ref wrapper, ref state);
    }
}
#endregion

#region Deserialization
[BurstCompile,
    EntityDataDeserializationSystem(typeof(GameOwner), (int)GameDataConstans.Version),
    CreateAfter(typeof(EntityDataDeserializationPresentationSystem)),
    CreateAfter(typeof(EntityDataDeserializationComponentSystem)),
    UpdateInGroup(typeof(EntityDataDeserializationSystemGroup)), AutoCreateIn("Server")]
public partial struct GameDataOwnerDeserializationSystem : ISystem
{
    private GameDataEntityComponentDataDeserializationSystemCore __core;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __core = new GameDataEntityComponentDataDeserializationSystemCore(TypeManager.GetTypeIndex<GameOwner>(), ref state);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        __core.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        GameOwnerDataWrapper wrapper;
        __core.Update<GameOwner, GameOwnerDataWrapper, GameOwnerDataWrapper>(ref wrapper, ref wrapper, ref state, out _);
    }
}

[BurstCompile,
    EntityDataDeserializationSystem(typeof(GameActorMaster), (int)GameDataConstans.Version),
    CreateAfter(typeof(EntityDataDeserializationPresentationSystem)),
    CreateAfter(typeof(EntityDataDeserializationComponentSystem)),
    UpdateInGroup(typeof(EntityDataDeserializationSystemGroup)), AutoCreateIn("Server")]
public partial struct GameDataActorMasterDeserializationSystem : ISystem
{
    private GameDataEntityComponentDataDeserializationSystemCore __core;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __core = new GameDataEntityComponentDataDeserializationSystemCore(TypeManager.GetTypeIndex<GameActorMaster>(), ref state);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        __core.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        GameActorMasterDataWrapper wrapper;
        __core.Update<GameActorMaster, GameActorMasterDataWrapper, GameActorMasterDataWrapper>(ref wrapper, ref wrapper, ref state, out _);
    }
}

[BurstCompile,
    EntityDataDeserializationSystem(typeof(GamePlayerLocator), (int)GameDataConstans.Version),
    CreateAfter(typeof(EntityDataDeserializationPresentationSystem)),
    CreateAfter(typeof(EntityDataDeserializationComponentSystem)),
    UpdateInGroup(typeof(EntityDataDeserializationSystemGroup)), AutoCreateIn("Server")]
public partial struct GameDataPlayerLocatorDeserializationSystem : ISystem
{
    private GameDataEntityComponentDataDeserializationSystemCore __core;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __core = new GameDataEntityComponentDataDeserializationSystemCore(TypeManager.GetTypeIndex<GamePlayerLocator>(), ref state);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        __core.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        GamePlayerLocatorDataWrapper wrapper;
        __core.Update<GamePlayerLocator, GamePlayerLocatorDataWrapper, GamePlayerLocatorDataWrapper>(ref wrapper, ref wrapper, ref state, out _);
    }
}

[BurstCompile,
    EntityDataDeserializationSystem(typeof(GamePlayerSpawn), (int)GameDataConstans.Version),
    CreateAfter(typeof(EntityDataDeserializationPresentationSystem)),
    CreateAfter(typeof(EntityDataDeserializationComponentSystem)),
    UpdateInGroup(typeof(EntityDataDeserializationSystemGroup)), AutoCreateIn("Server")]
public partial struct GameDataPlayerSpawnDeserializationSystem : ISystem
{
    private GameDataEntityComponentDataDeserializationSystemCore __core;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __core = new GameDataEntityComponentDataDeserializationSystemCore(TypeManager.GetTypeIndex<GamePlayerSpawn>(), ref state);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        __core.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        GamePlayerSpawnDataWrapper wrapper;
        __core.Update<GamePlayerSpawn, GamePlayerSpawnDataWrapper, GamePlayerSpawnDataWrapper>(ref wrapper, ref wrapper, ref state, out _);
    }
}
#endregion