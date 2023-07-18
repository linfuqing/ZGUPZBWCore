using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using ZG;

#region GameOwner
[assembly: RegisterGenericJobType(typeof(EntityDataComponentSerialize<GameDataEntityComponentDataSerializationSystemCore<GameOwner>.Serializer, GameDataEntityComponentDataSerializationSystemCore<GameOwner>.SerializerFactory>))]
[assembly: RegisterGenericJobType(typeof(EntityDataComponentDeserialize<GameDataEntityCompoentDataDeserializationSystem<GameOwner>.Deserializer, GameDataEntityCompoentDataDeserializationSystem<GameOwner>.DeserializerFactory>))]
//[assembly: EntityDataSerialize(typeof(GameOwner), typeof(GameDataEntityCompoentDataSerializationSystem<GameOwner>))]
[assembly: EntityDataDeserialize(typeof(GameOwner), typeof(GameDataEntityCompoentDataDeserializationSystem<GameOwner>), (int)GameDataConstans.Version)]
#endregion

#region GameActorMaster
[assembly: RegisterGenericJobType(typeof(EntityDataComponentSerialize<GameDataEntityComponentDataSerializationSystemCore<GameActorMaster>.Serializer, GameDataEntityComponentDataSerializationSystemCore<GameActorMaster>.SerializerFactory>))]
[assembly: RegisterGenericJobType(typeof(EntityDataComponentDeserialize<GameDataEntityCompoentDataDeserializationSystem<GameActorMaster>.Deserializer, GameDataEntityCompoentDataDeserializationSystem<GameActorMaster>.DeserializerFactory>))]
//[assembly: EntityDataSerialize(typeof(GameActorMaster), typeof(GameDataEntityCompoentDataSerializationSystem<GameActorMaster>))]
[assembly: EntityDataDeserialize(typeof(GameActorMaster), typeof(GameDataEntityCompoentDataDeserializationSystem<GameActorMaster>), (int)GameDataConstans.Version)]
#endregion

#region GamePlayerLocator
[assembly: RegisterGenericJobType(typeof(EntityDataComponentSerialize<GameDataEntityComponentDataSerializationSystemCore<GamePlayerLocator>.Serializer, GameDataEntityComponentDataSerializationSystemCore<GamePlayerLocator>.SerializerFactory>))]
[assembly: RegisterGenericJobType(typeof(EntityDataComponentDeserialize<GameDataEntityCompoentDataDeserializationSystem<GamePlayerLocator>.Deserializer, GameDataEntityCompoentDataDeserializationSystem<GamePlayerLocator>.DeserializerFactory>))]
//[assembly: EntityDataSerialize(typeof(GamePlayerLocator), typeof(GameDataEntityCompoentDataSerializationSystem<GamePlayerLocator>))]
[assembly: EntityDataDeserialize(typeof(GamePlayerLocator), typeof(GameDataEntityCompoentDataDeserializationSystem<GamePlayerLocator>), (int)GameDataConstans.Version)]
#endregion

#region GamePlayerSpawn
[assembly: RegisterGenericJobType(typeof(EntityDataComponentSerialize<GameDataEntityComponentDataSerializationSystemCore<GamePlayerSpawn>.Serializer, GameDataEntityComponentDataSerializationSystemCore<GamePlayerSpawn>.SerializerFactory>))]
[assembly: RegisterGenericJobType(typeof(EntityDataComponentDeserialize<GameDataEntityCompoentDataDeserializationSystem<GamePlayerSpawn>.Deserializer, GameDataEntityCompoentDataDeserializationSystem<GamePlayerSpawn>.DeserializerFactory>))]
//[assembly: EntityDataSerialize(typeof(GamePlayerSpawn), typeof(GameDataEntityCompoentDataSerializationSystem<GamePlayerSpawn>))]
[assembly: EntityDataDeserialize(typeof(GamePlayerSpawn), typeof(GameDataEntityCompoentDataDeserializationSystem<GamePlayerSpawn>), (int)GameDataConstans.Version)]
#endregion

public struct GamePlayerLocator : IGameDataEntityCompoent, IComponentData
{
    public Entity entity;

    public void Serialize(int entityIndex, ref EntityDataWriter writer)
    {
        writer.Write(entityIndex);
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

    Entity IGameDataEntityCompoent.entity
    {
        get => entity;

        set => entity = value;
    }
}

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

    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        __core.Update(ref state);
    }
}