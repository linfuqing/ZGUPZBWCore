using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using ZG;

#region GameOwner
[assembly: RegisterGenericJobType(typeof(EntityDataComponentSerialize<GameDataEntityComponentDataSerializationSystemCore<GameOwner>.Serializer, GameDataEntityComponentDataSerializationSystemCore<GameOwner>.SerializerFactory>))]
[assembly: RegisterGenericJobType(typeof(EntityDataComponentDeserialize<GameDataEntityComponentDataDeserializationSystemCore<GameOwner>.Deserializer, GameDataEntityComponentDataDeserializationSystemCore<GameOwner>.DeserializerFactory>))]
[assembly: RegisterGenericJobType(typeof(GameDataEntityComponentDataDeserializationSystemCore<GameOwner>.Build))]
//[assembly: EntityDataSerialize(typeof(GameOwner), typeof(GameDataEntityCompoentDataSerializationSystem<GameOwner>))]
//[assembly: EntityDataDeserialize(typeof(GameOwner), typeof(GameDataEntityCompoentDataDeserializationSystem<GameOwner>), (int)GameDataConstans.Version)]
#endregion

#region GameActorMaster
[assembly: RegisterGenericJobType(typeof(EntityDataComponentSerialize<GameDataEntityComponentDataSerializationSystemCore<GameActorMaster>.Serializer, GameDataEntityComponentDataSerializationSystemCore<GameActorMaster>.SerializerFactory>))]
[assembly: RegisterGenericJobType(typeof(EntityDataComponentDeserialize<GameDataEntityComponentDataDeserializationSystemCore<GameActorMaster>.Deserializer, GameDataEntityComponentDataDeserializationSystemCore<GameActorMaster>.DeserializerFactory>))]
[assembly: RegisterGenericJobType(typeof(GameDataEntityComponentDataDeserializationSystemCore<GameOwner>.Build))]
//[assembly: EntityDataSerialize(typeof(GameActorMaster), typeof(GameDataEntityCompoentDataSerializationSystem<GameActorMaster>))]
//[assembly: EntityDataDeserialize(typeof(GameActorMaster), typeof(GameDataEntityCompoentDataDeserializationSystem<GameActorMaster>), (int)GameDataConstans.Version)]
#endregion

#region GamePlayerLocator
[assembly: RegisterGenericJobType(typeof(EntityDataComponentSerialize<GameDataEntityComponentDataSerializationSystemCore<GamePlayerLocator>.Serializer, GameDataEntityComponentDataSerializationSystemCore<GamePlayerLocator>.SerializerFactory>))]
[assembly: RegisterGenericJobType(typeof(EntityDataComponentDeserialize<GameDataEntityComponentDataDeserializationSystemCore<GamePlayerLocator>.Deserializer, GameDataEntityComponentDataDeserializationSystemCore<GamePlayerLocator>.DeserializerFactory>))]
[assembly: RegisterGenericJobType(typeof(GameDataEntityComponentDataDeserializationSystemCore<GamePlayerLocator>.Build))]
//[assembly: EntityDataSerialize(typeof(GamePlayerLocator), typeof(GameDataEntityCompoentDataSerializationSystem<GamePlayerLocator>))]
//[assembly: EntityDataDeserialize(typeof(GamePlayerLocator), typeof(GameDataEntityCompoentDataDeserializationSystem<GamePlayerLocator>), (int)GameDataConstans.Version)]
#endregion

#region GamePlayerSpawn
[assembly: RegisterGenericJobType(typeof(EntityDataComponentSerialize<GameDataEntityComponentDataSerializationSystemCore<GamePlayerSpawn>.Serializer, GameDataEntityComponentDataSerializationSystemCore<GamePlayerSpawn>.SerializerFactory>))]
[assembly: RegisterGenericJobType(typeof(EntityDataComponentDeserialize<GameDataEntityComponentDataDeserializationSystemCore<GamePlayerSpawn>.Deserializer, GameDataEntityComponentDataDeserializationSystemCore<GamePlayerSpawn>.DeserializerFactory>))]
[assembly: RegisterGenericJobType(typeof(GameDataEntityComponentDataDeserializationSystemCore<GamePlayerSpawn>.Build))]
//[assembly: EntityDataSerialize(typeof(GamePlayerSpawn), typeof(GameDataEntityCompoentDataSerializationSystem<GamePlayerSpawn>))]
//[assembly: EntityDataDeserialize(typeof(GamePlayerSpawn), typeof(GameDataEntityCompoentDataDeserializationSystem<GamePlayerSpawn>), (int)GameDataConstans.Version)]
#endregion

public struct GamePlayerLocator : IGameDataEntityCompoent, IComponentData
{
    public Entity entity;

    public void Serialize(int entityIndex, ref EntityDataWriter writer)
    {
        writer.Write(entityIndex);
    }

    public int Deserialize(ref EntityDataReader reader)
    {
        return reader.Read<int>();
    }

    Entity IGameDataEntityCompoent.entity
    {
        get => entity;

        set => entity = value;
    }
}

[EntityDataTypeName("GameSpawn")]
public struct GamePlayerSpawn : IGameDataEntityCompoent, IComponentData
{
    public Entity entity;

    public void Serialize(int entityIndex, ref EntityDataWriter writer)
    {
        writer.Write(entityIndex);
    }

    public int Deserialize(ref EntityDataReader reader)
    {
        return reader.Read<int>();
    }

    Entity IGameDataEntityCompoent.entity
    {
        get => entity;

        set => entity = value;
    }
}

#region Serialization
[BurstCompile,
    EntityDataSerializationSystem(typeof(GameOwner)),
    CreateAfter(typeof(EntityDataSerializationInitializationSystem)),
    UpdateInGroup(typeof(EntityDataSerializationSystemGroup)), AutoCreateIn("Server")]
public partial struct GameDataOwnerSerializationSystem : ISystem
{
    private GameDataEntityComponentDataSerializationSystemCore<GameOwner> __core;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __core = new GameDataEntityComponentDataSerializationSystemCore<GameOwner>(ref state);
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
    EntityDataSerializationSystem(typeof(GameActorMaster)),
    CreateAfter(typeof(EntityDataSerializationInitializationSystem)),
    UpdateInGroup(typeof(EntityDataSerializationSystemGroup)), AutoCreateIn("Server")]
public partial struct GameDataActorMasterSerializationSystem : ISystem
{
    private GameDataEntityComponentDataSerializationSystemCore<GameActorMaster> __core;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __core = new GameDataEntityComponentDataSerializationSystemCore<GameActorMaster>(ref state);
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
    EntityDataSerializationSystem(typeof(GamePlayerLocator)),
    CreateAfter(typeof(EntityDataSerializationInitializationSystem)),
    UpdateInGroup(typeof(EntityDataSerializationSystemGroup)), AutoCreateIn("Server")]
public partial struct GameDataPlayerLocatorSerializationSystem : ISystem
{
    private GameDataEntityComponentDataSerializationSystemCore<GamePlayerLocator> __core;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __core = new GameDataEntityComponentDataSerializationSystemCore<GamePlayerLocator>(ref state);
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
    EntityDataSerializationSystem(typeof(GamePlayerSpawn)),
    CreateAfter(typeof(EntityDataSerializationInitializationSystem)),
    UpdateInGroup(typeof(EntityDataSerializationSystemGroup)), AutoCreateIn("Server")]
public partial struct GameDataPlayerSpawnSerializationSystem : ISystem
{
    private GameDataEntityComponentDataSerializationSystemCore<GamePlayerSpawn> __core;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __core = new GameDataEntityComponentDataSerializationSystemCore<GamePlayerSpawn>(ref state);
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
#endregion

#region Deserialization
[BurstCompile,
    EntityDataDeserializationSystem(typeof(GameOwner), (int)GameDataConstans.Version),
    CreateAfter(typeof(EntityDataDeserializationPresentationSystem)),
    CreateAfter(typeof(EntityDataDeserializationComponentSystem)),
    UpdateInGroup(typeof(EntityDataDeserializationSystemGroup)), AutoCreateIn("Server")]
public partial struct GameDataOwnerDeserializationSystem : ISystem
{
    private GameDataEntityComponentDataDeserializationSystemCore<GameOwner> __core;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __core = new GameDataEntityComponentDataDeserializationSystemCore<GameOwner>(ref state);
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
    EntityDataDeserializationSystem(typeof(GameActorMaster), (int)GameDataConstans.Version),
    CreateAfter(typeof(EntityDataDeserializationPresentationSystem)),
    CreateAfter(typeof(EntityDataDeserializationComponentSystem)),
    UpdateInGroup(typeof(EntityDataDeserializationSystemGroup)), AutoCreateIn("Server")]
public partial struct GameDataActorMasterDeserializationSystem : ISystem
{
    private GameDataEntityComponentDataDeserializationSystemCore<GameActorMaster> __core;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __core = new GameDataEntityComponentDataDeserializationSystemCore<GameActorMaster>(ref state);
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
    EntityDataDeserializationSystem(typeof(GamePlayerLocator), (int)GameDataConstans.Version),
    CreateAfter(typeof(EntityDataDeserializationPresentationSystem)),
    CreateAfter(typeof(EntityDataDeserializationComponentSystem)),
    UpdateInGroup(typeof(EntityDataDeserializationSystemGroup)), AutoCreateIn("Server")]
public partial struct GameDataPlayerLocatorDeserializationSystem : ISystem
{
    private GameDataEntityComponentDataDeserializationSystemCore<GamePlayerLocator> __core;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __core = new GameDataEntityComponentDataDeserializationSystemCore<GamePlayerLocator>(ref state);
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
    EntityDataDeserializationSystem(typeof(GamePlayerSpawn), (int)GameDataConstans.Version),
    CreateAfter(typeof(EntityDataDeserializationPresentationSystem)),
    CreateAfter(typeof(EntityDataDeserializationComponentSystem)),
    UpdateInGroup(typeof(EntityDataDeserializationSystemGroup)), AutoCreateIn("Server")]
public partial struct GameDataPlayerSpawnDeserializationSystem : ISystem
{
    private GameDataEntityComponentDataDeserializationSystemCore<GamePlayerSpawn> __core;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __core = new GameDataEntityComponentDataDeserializationSystemCore<GamePlayerSpawn>(ref state);
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
#endregion