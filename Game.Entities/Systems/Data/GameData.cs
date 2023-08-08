using System;
using Unity.Burst;
using Unity.Entities;
using Unity.Transforms;
using ZG;

#region Translation
//[assembly: Unity.Jobs.RegisterGenericJobType(typeof(EntityDataComponentSerialize<ComponentDataSerializationSystem<Translation>.Serializer, ComponentDataSerializationSystem<Translation>.SerializerFactory>))]
//[assembly: Unity.Jobs.RegisterGenericJobType(typeof(EntityDataComponentDeserialize<ComponentDataDeserializationSystem<Translation>.Deserializer, ComponentDataDeserializationSystem<Translation>.DeserializerFactory>))]
//[assembly: EntityDataSerialize(typeof(Translation))]
//[assembly: EntityDataDeserialize(typeof(Translation), (int)GameDataConstans.Version)]
#endregion

#region Rotation
//[assembly: Unity.Jobs.RegisterGenericJobType(typeof(EntityDataComponentSerialize<ComponentDataSerializationSystem<Rotation>.Serializer, ComponentDataSerializationSystem<Rotation>.SerializerFactory>))]
//[assembly: Unity.Jobs.RegisterGenericJobType(typeof(EntityDataComponentDeserialize<ComponentDataDeserializationSystem<Rotation>.Deserializer, ComponentDataDeserializationSystem<Rotation>.DeserializerFactory>))]
//[assembly: EntityDataSerialize(typeof(Rotation))]
//[assembly: EntityDataDeserialize(typeof(Rotation), (int)GameDataConstans.Version)]
#endregion

#region GameEntityHealth
//[assembly: Unity.Jobs.RegisterGenericJobType(typeof(EntityDataComponentSerialize<ComponentDataSerializationSystem<GameEntityHealth>.Serializer, ComponentDataSerializationSystem<GameEntityHealth>.SerializerFactory>))]
//[assembly: Unity.Jobs.RegisterGenericJobType(typeof(EntityDataComponentDeserialize<ComponentDataDeserializationSystem<GameEntityHealth>.Deserializer, ComponentDataDeserializationSystem<GameEntityHealth>.DeserializerFactory>))]
//[assembly: EntityDataSerialize(typeof(GameEntityHealth))]
//[assembly: EntityDataDeserialize(typeof(GameEntityHealth), (int)GameDataConstans.Version)]
#endregion

#region GameEntityTorpidity
//[assembly: Unity.Jobs.RegisterGenericJobType(typeof(EntityDataComponentSerialize<ComponentDataSerializationSystem<GameEntityTorpidity>.Serializer, ComponentDataSerializationSystem<GameEntityTorpidity>.SerializerFactory>))]
//[assembly: Unity.Jobs.RegisterGenericJobType(typeof(EntityDataComponentDeserialize<ComponentDataDeserializationSystem<GameEntityTorpidity>.Deserializer, ComponentDataDeserializationSystem<GameEntityTorpidity>.DeserializerFactory>))]
//[assembly: EntityDataSerialize(typeof(GameEntityTorpidity))]
//[assembly: EntityDataDeserialize(typeof(GameEntityTorpidity), (int)GameDataConstans.Version)]
#endregion

#region GameCreatureFood
//[assembly: Unity.Jobs.RegisterGenericJobType(typeof(EntityDataComponentSerialize<ComponentDataSerializationSystem<GameCreatureFood>.Serializer, ComponentDataSerializationSystem<GameCreatureFood>.SerializerFactory>))]
//[assembly: Unity.Jobs.RegisterGenericJobType(typeof(EntityDataComponentDeserialize<ComponentDataDeserializationSystem<GameCreatureFood>.Deserializer, ComponentDataDeserializationSystem<GameCreatureFood>.DeserializerFactory>))]
//[assembly: EntityDataSerialize(typeof(GameCreatureFood))]
//[assembly: EntityDataDeserialize(typeof(GameCreatureFood), (int)GameDataConstans.Version)]
#endregion

#region GameCreatureWater
//[assembly: Unity.Jobs.RegisterGenericJobType(typeof(EntityDataComponentSerialize<ComponentDataSerializationSystem<GameCreatureWater>.Serializer, ComponentDataSerializationSystem<GameCreatureWater>.SerializerFactory>))]
//[assembly: Unity.Jobs.RegisterGenericJobType(typeof(EntityDataComponentDeserialize<ComponentDataDeserializationSystem<GameCreatureWater>.Deserializer, ComponentDataDeserializationSystem<GameCreatureWater>.DeserializerFactory>))]
//[assembly: EntityDataSerialize(typeof(GameCreatureWater))]
//[assembly: EntityDataDeserialize(typeof(GameCreatureWater), (int)GameDataConstans.Version)]
#endregion

#region GameAnimalInfo
//[assembly: Unity.Jobs.RegisterGenericJobType(typeof(EntityDataComponentSerialize<ComponentDataSerializationSystem<GameAnimalInfo>.Serializer, ComponentDataSerializationSystem<GameAnimalInfo>.SerializerFactory>))]
//[assembly: Unity.Jobs.RegisterGenericJobType(typeof(EntityDataComponentDeserialize<ComponentDataDeserializationSystem<GameAnimalInfo>.Deserializer, ComponentDataDeserializationSystem<GameAnimalInfo>.DeserializerFactory>))]
//[assembly: EntityDataSerialize(typeof(GameAnimalInfo))]
//[assembly: EntityDataDeserialize(typeof(GameAnimalInfo), (int)GameDataConstans.Version)]
#endregion

#region GameNickname
//[assembly: Unity.Jobs.RegisterGenericJobType(typeof(EntityDataComponentSerialize<ComponentDataSerializationSystem<GameNickname>.Serializer, ComponentDataSerializationSystem<GameNickname>.SerializerFactory>))]
//[assembly: Unity.Jobs.RegisterGenericJobType(typeof(EntityDataComponentDeserialize<ComponentDataDeserializationSystem<GameNickname>.Deserializer, ComponentDataDeserializationSystem<GameNickname>.DeserializerFactory>))]
//[assembly: EntityDataSerialize(typeof(GameNickname))]
//[assembly: EntityDataDeserialize(typeof(GameNickname), (int)GameDataConstans.Version)]
#endregion

#region GameVariant
//[assembly: Unity.Jobs.RegisterGenericJobType(typeof(EntityDataComponentSerialize<ComponentDataSerializationSystem<GameVariant>.Serializer, ComponentDataSerializationSystem<GameVariant>.SerializerFactory>))]
//[assembly: Unity.Jobs.RegisterGenericJobType(typeof(EntityDataComponentDeserialize<ComponentDataDeserializationSystem<GameVariant>.Deserializer, ComponentDataDeserializationSystem<GameVariant>.DeserializerFactory>))]
//[assembly: EntityDataSerialize(typeof(GameVariant))]
//[assembly: EntityDataDeserialize(typeof(GameVariant), (int)GameDataConstans.Version)]
#endregion

#region GameAIActionHandle
//[assembly: Unity.Jobs.RegisterGenericJobType(typeof(EntityDataComponentSerialize<ComponentDataSerializationSystem<GameAIActionHandle>.Serializer, ComponentDataSerializationSystem<GameAIActionHandle>.SerializerFactory>))]
//[assembly: Unity.Jobs.RegisterGenericJobType(typeof(EntityDataComponentDeserialize<ComponentDataDeserializationSystem<GameAIActionHandle>.Deserializer, ComponentDataDeserializationSystem<GameAIActionHandle>.DeserializerFactory>))]
//[assembly: EntityDataSerialize(typeof(GameAIActionHandle))]
//[assembly: EntityDataDeserialize(typeof(GameAIActionHandle), (int)GameDataConstans.Version)]
#endregion

#region GameMoney
//[assembly: Unity.Jobs.RegisterGenericJobType(typeof(EntityDataComponentSerialize<ComponentDataSerializationSystem<GameMoney>.Serializer, ComponentDataSerializationSystem<GameMoney>.SerializerFactory>))]
//[assembly: Unity.Jobs.RegisterGenericJobType(typeof(EntityDataComponentDeserialize<ComponentDataDeserializationSystem<GameMoney>.Deserializer, ComponentDataDeserializationSystem<GameMoney>.DeserializerFactory>))]
//[assembly: EntityDataSerialize(typeof(GameMoney))]
//[assembly: EntityDataDeserialize(typeof(GameMoney), (int)GameDataConstans.Version)]
#endregion

#region GamePlayerCreatedDate
//[assembly: Unity.Jobs.RegisterGenericJobType(typeof(EntityDataComponentSerialize<ComponentDataSerializationSystem<GamePlayerCreatedDate>.Serializer, ComponentDataSerializationSystem<GamePlayerCreatedDate>.SerializerFactory>))]
//[assembly: Unity.Jobs.RegisterGenericJobType(typeof(EntityDataComponentDeserialize<ComponentDataDeserializationSystem<GamePlayerCreatedDate>.Deserializer, ComponentDataDeserializationSystem<GamePlayerCreatedDate>.DeserializerFactory>))]
//[assembly: EntityDataSerialize(typeof(GamePlayerCreatedDate))]
//[assembly: EntityDataDeserialize(typeof(GamePlayerCreatedDate), (int)GameDataConstans.Version)]
#endregion

#region GamePlayerPlayTime
//[assembly: Unity.Jobs.RegisterGenericJobType(typeof(EntityDataComponentSerialize<ComponentDataSerializationSystem<GamePlayerPlayTime>.Serializer, ComponentDataSerializationSystem<GamePlayerPlayTime>.SerializerFactory>))]
//[assembly: Unity.Jobs.RegisterGenericJobType(typeof(EntityDataComponentDeserialize<ComponentDataDeserializationSystem<GamePlayerPlayTime>.Deserializer, ComponentDataDeserializationSystem<GamePlayerPlayTime>.DeserializerFactory>))]
//[assembly: EntityDataSerialize(typeof(GamePlayerPlayTime))]
//[assembly: EntityDataDeserialize(typeof(GamePlayerPlayTime), (int)GameDataConstans.Version)]
#endregion

#region GameValhallaExp
//[assembly: Unity.Jobs.RegisterGenericJobType(typeof(EntityDataComponentSerialize<ComponentDataSerializationSystem<GameValhallaExp>.Serializer, ComponentDataSerializationSystem<GameValhallaExp>.SerializerFactory>))]
//[assembly: Unity.Jobs.RegisterGenericJobType(typeof(EntityDataComponentDeserialize<ComponentDataDeserializationSystem<GameValhallaExp>.Deserializer, ComponentDataDeserializationSystem<GameValhallaExp>.DeserializerFactory>))]
//[assembly: EntityDataSerialize(typeof(GameValhallaExp))]
//[assembly: EntityDataDeserialize(typeof(GameValhallaExp), (int)GameDataConstans.Version)]
#endregion

public struct GameAIActionHandle : IComponentData
{
    public int value;
}

public struct GameMoney : IComponentData
{
    public int value;
}

public struct GamePlayerCreatedDate : IComponentData
{
    public long value;
}

public struct GamePlayerPlayTime : IComponentData
{
    public uint value;
}

[BurstCompile,
    EntityDataSerializationSystem(typeof(Translation)),
    CreateAfter(typeof(EntityDataSerializationInitializationSystem)),
    UpdateInGroup(typeof(EntityDataSerializationSystemGroup)), AutoCreateIn("Server")]
public partial struct GameDataTranslationSerializationSystem : ISystem
{
    private EntityDataSerializationSystemCoreEx __core;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __core = EntityDataSerializationSystemCoreEx.Create<Translation>(ref state);
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
    EntityDataSerializationSystem(typeof(Rotation)),
    CreateAfter(typeof(EntityDataSerializationInitializationSystem)),
    UpdateInGroup(typeof(EntityDataSerializationSystemGroup)), AutoCreateIn("Server")]
public partial struct GameDataRotationSerializationSystem : ISystem
{
    private EntityDataSerializationSystemCoreEx __core;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __core = EntityDataSerializationSystemCoreEx.Create<Rotation>(ref state);
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
    EntityDataSerializationSystem(typeof(GameEntityHealth)),
    CreateAfter(typeof(EntityDataSerializationInitializationSystem)),
    UpdateInGroup(typeof(EntityDataSerializationSystemGroup)), AutoCreateIn("Server")]
public partial struct GameDataEntityHealthSerializationSystem : ISystem
{
    private EntityDataSerializationSystemCoreEx __core;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __core = EntityDataSerializationSystemCoreEx.Create<GameEntityHealth>(ref state);
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
    EntityDataSerializationSystem(typeof(GameEntityTorpidity)),
    CreateAfter(typeof(EntityDataSerializationInitializationSystem)),
    UpdateInGroup(typeof(EntityDataSerializationSystemGroup)), AutoCreateIn("Server")]
public partial struct GameDataEntityTorpiditySerializationSystem : ISystem
{
    private EntityDataSerializationSystemCoreEx __core;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __core = EntityDataSerializationSystemCoreEx.Create<GameEntityTorpidity>(ref state);
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
    EntityDataSerializationSystem(typeof(GameCreatureFood)),
    CreateAfter(typeof(EntityDataSerializationInitializationSystem)),
    UpdateInGroup(typeof(EntityDataSerializationSystemGroup)), AutoCreateIn("Server")]
public partial struct GameDataCreatureFoodSerializationSystem : ISystem
{
    private EntityDataSerializationSystemCoreEx __core;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __core = EntityDataSerializationSystemCoreEx.Create<GameCreatureFood>(ref state);
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
    EntityDataSerializationSystem(typeof(GameCreatureWater)),
    CreateAfter(typeof(EntityDataSerializationInitializationSystem)),
    UpdateInGroup(typeof(EntityDataSerializationSystemGroup)), AutoCreateIn("Server")]
public partial struct GameDataCreatureWaterSerializationSystem : ISystem
{
    private EntityDataSerializationSystemCoreEx __core;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __core = EntityDataSerializationSystemCoreEx.Create<GameCreatureWater>(ref state);
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
    EntityDataSerializationSystem(typeof(GameAnimalInfo)),
    CreateAfter(typeof(EntityDataSerializationInitializationSystem)),
    UpdateInGroup(typeof(EntityDataSerializationSystemGroup)), AutoCreateIn("Server")]
public partial struct GameDataAnimalInfoSerializationSystem : ISystem
{
    private EntityDataSerializationSystemCoreEx __core;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __core = EntityDataSerializationSystemCoreEx.Create<GameAnimalInfo>(ref state);
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
    EntityDataSerializationSystem(typeof(GameNickname)),
    CreateAfter(typeof(EntityDataSerializationInitializationSystem)),
    UpdateInGroup(typeof(EntityDataSerializationSystemGroup)), AutoCreateIn("Server")]
public partial struct GameDataNicknameSerializationSystem : ISystem
{
    private EntityDataSerializationSystemCoreEx __core;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __core = EntityDataSerializationSystemCoreEx.Create<GameNickname>(ref state);
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
    EntityDataSerializationSystem(typeof(GameVariant)),
    CreateAfter(typeof(EntityDataSerializationInitializationSystem)),
    UpdateInGroup(typeof(EntityDataSerializationSystemGroup)), AutoCreateIn("Server")]
public partial struct GameDataVariantSerializationSystem : ISystem
{
    private EntityDataSerializationSystemCoreEx __core;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __core = EntityDataSerializationSystemCoreEx.Create<GameVariant>(ref state);
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
    EntityDataSerializationSystem(typeof(GameAIActionHandle)),
    CreateAfter(typeof(EntityDataSerializationInitializationSystem)),
    UpdateInGroup(typeof(EntityDataSerializationSystemGroup)), AutoCreateIn("Server")]
public partial struct GameDataAIActionHandleSerializationSystem : ISystem
{
    private EntityDataSerializationSystemCoreEx __core;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __core = EntityDataSerializationSystemCoreEx.Create<GameAIActionHandle>(ref state);
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
    EntityDataSerializationSystem(typeof(GameMoney)),
    CreateAfter(typeof(EntityDataSerializationInitializationSystem)),
    UpdateInGroup(typeof(EntityDataSerializationSystemGroup)), AutoCreateIn("Server")]
public partial struct GameDataMoneySerializationSystem : ISystem
{
    private EntityDataSerializationSystemCoreEx __core;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __core = EntityDataSerializationSystemCoreEx.Create<GameMoney>(ref state);
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
    EntityDataSerializationSystem(typeof(GamePlayerCreatedDate)),
    CreateAfter(typeof(EntityDataSerializationInitializationSystem)),
    UpdateInGroup(typeof(EntityDataSerializationSystemGroup)), AutoCreateIn("Server")]
public partial struct GameDataPlayerCreatedDateSerializationSystem : ISystem
{
    private EntityDataSerializationSystemCoreEx __core;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __core = EntityDataSerializationSystemCoreEx.Create<GamePlayerCreatedDate>(ref state);
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
    EntityDataSerializationSystem(typeof(GamePlayerPlayTime)),
    CreateAfter(typeof(EntityDataSerializationInitializationSystem)),
    UpdateInGroup(typeof(EntityDataSerializationSystemGroup)), AutoCreateIn("Server")]
public partial struct GameDataPlayerPlayTimeSerializationSystem : ISystem
{
    private EntityDataSerializationSystemCoreEx __core;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __core = EntityDataSerializationSystemCoreEx.Create<GamePlayerPlayTime>(ref state);
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
    EntityDataSerializationSystem(typeof(GameValhallaExp)),
    CreateAfter(typeof(EntityDataSerializationInitializationSystem)),
    UpdateInGroup(typeof(EntityDataSerializationSystemGroup)), AutoCreateIn("Server")]
public partial struct GameDataValhallaExpSerializationSystem : ISystem
{
    private EntityDataSerializationSystemCoreEx __core;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __core = EntityDataSerializationSystemCoreEx.Create<GameValhallaExp>(ref state);
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
    EntityDataDeserializationSystem(typeof(Translation), (int)GameDataConstans.Version),
    CreateAfter(typeof(EntityDataDeserializationComponentSystem)),
    UpdateInGroup(typeof(EntityDataDeserializationSystemGroup)), AutoCreateIn("Server")]
public partial struct GameDataTranslationDeserializationSystem : ISystem
{
    private EntityDataDeserializationSystemCoreEx __core;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __core = EntityDataDeserializationSystemCoreEx.Create<Translation>(ref state);
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
    EntityDataDeserializationSystem(typeof(Rotation), (int)GameDataConstans.Version),
    CreateAfter(typeof(EntityDataDeserializationComponentSystem)),
    UpdateInGroup(typeof(EntityDataDeserializationSystemGroup)), AutoCreateIn("Server")]
public partial struct GameDataRotationDeserializationSystem : ISystem
{
    private EntityDataDeserializationSystemCoreEx __core;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __core = EntityDataDeserializationSystemCoreEx.Create<Rotation>(ref state);
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
    EntityDataDeserializationSystem(typeof(GameEntityHealth), (int)GameDataConstans.Version),
    CreateAfter(typeof(EntityDataDeserializationComponentSystem)),
    UpdateInGroup(typeof(EntityDataDeserializationSystemGroup)), AutoCreateIn("Server")]
public partial struct GameDataEntityHealthDeserializationSystem : ISystem
{
    private EntityDataDeserializationSystemCoreEx __core;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __core = EntityDataDeserializationSystemCoreEx.Create<GameEntityHealth>(ref state);
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
    EntityDataDeserializationSystem(typeof(GameEntityTorpidity), (int)GameDataConstans.Version),
    CreateAfter(typeof(EntityDataDeserializationComponentSystem)),
    UpdateInGroup(typeof(EntityDataDeserializationSystemGroup)), AutoCreateIn("Server")]
public partial struct GameDataEntityTorpidityDeserializationSystem : ISystem
{
    private EntityDataDeserializationSystemCoreEx __core;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __core = EntityDataDeserializationSystemCoreEx.Create<GameEntityTorpidity>(ref state);
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
    EntityDataDeserializationSystem(typeof(GameCreatureFood), (int)GameDataConstans.Version),
    CreateAfter(typeof(EntityDataDeserializationComponentSystem)),
    UpdateInGroup(typeof(EntityDataDeserializationSystemGroup)), AutoCreateIn("Server")]
public partial struct GameDataCreatureFoodDeserializationSystem : ISystem
{
    private EntityDataDeserializationSystemCoreEx __core;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __core = EntityDataDeserializationSystemCoreEx.Create<GameCreatureFood>(ref state);
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
    EntityDataDeserializationSystem(typeof(GameCreatureWater), (int)GameDataConstans.Version),
    CreateAfter(typeof(EntityDataDeserializationComponentSystem)),
    UpdateInGroup(typeof(EntityDataDeserializationSystemGroup)), AutoCreateIn("Server")]
public partial struct GameDataCreatureWaterDeserializationSystem : ISystem
{
    private EntityDataDeserializationSystemCoreEx __core;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __core = EntityDataDeserializationSystemCoreEx.Create<GameCreatureWater>(ref state);
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
    EntityDataDeserializationSystem(typeof(GameAnimalInfo), (int)GameDataConstans.Version),
    CreateAfter(typeof(EntityDataDeserializationComponentSystem)),
    UpdateInGroup(typeof(EntityDataDeserializationSystemGroup)), AutoCreateIn("Server")]
public partial struct GameDataAnimalInfoDeserializationSystem : ISystem
{
    private EntityDataDeserializationSystemCoreEx __core;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __core = EntityDataDeserializationSystemCoreEx.Create<GameAnimalInfo>(ref state);
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
    EntityDataDeserializationSystem(typeof(GameNickname), (int)GameDataConstans.Version),
    CreateAfter(typeof(EntityDataDeserializationComponentSystem)),
    UpdateInGroup(typeof(EntityDataDeserializationSystemGroup)), AutoCreateIn("Server")]
public partial struct GameDataNicknameDeserializationSystem : ISystem
{
    private EntityDataDeserializationSystemCoreEx __core;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __core = EntityDataDeserializationSystemCoreEx.Create<GameNickname>(ref state);
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
    EntityDataDeserializationSystem(typeof(GameVariant), (int)GameDataConstans.Version),
    CreateAfter(typeof(EntityDataDeserializationComponentSystem)),
    UpdateInGroup(typeof(EntityDataDeserializationSystemGroup)), AutoCreateIn("Server")]
public partial struct GameDataVariantDeserializationSystem : ISystem
{
    private EntityDataDeserializationSystemCoreEx __core;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __core = EntityDataDeserializationSystemCoreEx.Create<GameVariant>(ref state);
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
    EntityDataDeserializationSystem(typeof(GameAIActionHandle), (int)GameDataConstans.Version),
    CreateAfter(typeof(EntityDataDeserializationComponentSystem)),
    UpdateInGroup(typeof(EntityDataDeserializationSystemGroup)), AutoCreateIn("Server")]
public partial struct GameDataAIActionHandleDeserializationSystem : ISystem
{
    private EntityDataDeserializationSystemCoreEx __core;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __core = EntityDataDeserializationSystemCoreEx.Create<GameAIActionHandle>(ref state);
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
    EntityDataDeserializationSystem(typeof(GameMoney), (int)GameDataConstans.Version),
    CreateAfter(typeof(EntityDataDeserializationComponentSystem)),
    UpdateInGroup(typeof(EntityDataDeserializationSystemGroup)), AutoCreateIn("Server")]
public partial struct GameDataMoneyDeserializationSystem : ISystem
{
    private EntityDataDeserializationSystemCoreEx __core;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __core = EntityDataDeserializationSystemCoreEx.Create<GameMoney>(ref state);
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
    EntityDataDeserializationSystem(typeof(GamePlayerCreatedDate), (int)GameDataConstans.Version),
    CreateAfter(typeof(EntityDataDeserializationComponentSystem)),
    UpdateInGroup(typeof(EntityDataDeserializationSystemGroup)), AutoCreateIn("Server")]
public partial struct GameDataPlayerCreatedDateDeserializationSystem : ISystem
{
    private EntityDataDeserializationSystemCoreEx __core;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __core = EntityDataDeserializationSystemCoreEx.Create<GamePlayerCreatedDate>(ref state);
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
    EntityDataDeserializationSystem(typeof(GamePlayerPlayTime), (int)GameDataConstans.Version),
    CreateAfter(typeof(EntityDataDeserializationComponentSystem)),
    UpdateInGroup(typeof(EntityDataDeserializationSystemGroup)), AutoCreateIn("Server")]
public partial struct GameDataPlayerPlayTimeDeserializationSystem : ISystem
{
    private EntityDataDeserializationSystemCoreEx __core;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __core = EntityDataDeserializationSystemCoreEx.Create<GamePlayerPlayTime>(ref state);
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
    EntityDataDeserializationSystem(typeof(GameValhallaExp), (int)GameDataConstans.Version),
    CreateAfter(typeof(EntityDataDeserializationComponentSystem)),
    UpdateInGroup(typeof(EntityDataDeserializationSystemGroup)), AutoCreateIn("Server")]
public partial struct GameDataValhallaExpDeserializationSystem : ISystem
{
    private EntityDataDeserializationSystemCoreEx __core;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        __core = EntityDataDeserializationSystemCoreEx.Create<GameValhallaExp>(ref state);
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