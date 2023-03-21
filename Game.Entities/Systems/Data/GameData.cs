using System;
using Unity.Entities;
using Unity.Transforms;
using ZG;

#region Translation
[assembly: Unity.Jobs.RegisterGenericJobType(typeof(EntityDataComponentSerialize<ComponentDataSerializationSystem<Translation>.Serializer, ComponentDataSerializationSystem<Translation>.SerializerFactory>))]
[assembly: Unity.Jobs.RegisterGenericJobType(typeof(EntityDataComponentDeserialize<ComponentDataDeserializationSystem<Translation>.Deserializer, ComponentDataDeserializationSystem<Translation>.DeserializerFactory>))]
[assembly: EntityDataSerialize(typeof(Translation))]
[assembly: EntityDataDeserialize(typeof(Translation), (int)GameDataConstans.Version)]
#endregion

#region Rotation
[assembly: Unity.Jobs.RegisterGenericJobType(typeof(EntityDataComponentSerialize<ComponentDataSerializationSystem<Rotation>.Serializer, ComponentDataSerializationSystem<Rotation>.SerializerFactory>))]
[assembly: Unity.Jobs.RegisterGenericJobType(typeof(EntityDataComponentDeserialize<ComponentDataDeserializationSystem<Rotation>.Deserializer, ComponentDataDeserializationSystem<Rotation>.DeserializerFactory>))]
[assembly: EntityDataSerialize(typeof(Rotation))]
[assembly: EntityDataDeserialize(typeof(Rotation), (int)GameDataConstans.Version)]
#endregion

#region GameEntityHealth
[assembly: Unity.Jobs.RegisterGenericJobType(typeof(EntityDataComponentSerialize<ComponentDataSerializationSystem<GameEntityHealth>.Serializer, ComponentDataSerializationSystem<GameEntityHealth>.SerializerFactory>))]
[assembly: Unity.Jobs.RegisterGenericJobType(typeof(EntityDataComponentDeserialize<ComponentDataDeserializationSystem<GameEntityHealth>.Deserializer, ComponentDataDeserializationSystem<GameEntityHealth>.DeserializerFactory>))]
[assembly: EntityDataSerialize(typeof(GameEntityHealth))]
[assembly: EntityDataDeserialize(typeof(GameEntityHealth), (int)GameDataConstans.Version)]
#endregion

#region GameEntityTorpidity
[assembly: Unity.Jobs.RegisterGenericJobType(typeof(EntityDataComponentSerialize<ComponentDataSerializationSystem<GameEntityTorpidity>.Serializer, ComponentDataSerializationSystem<GameEntityTorpidity>.SerializerFactory>))]
[assembly: Unity.Jobs.RegisterGenericJobType(typeof(EntityDataComponentDeserialize<ComponentDataDeserializationSystem<GameEntityTorpidity>.Deserializer, ComponentDataDeserializationSystem<GameEntityTorpidity>.DeserializerFactory>))]
[assembly: EntityDataSerialize(typeof(GameEntityTorpidity))]
[assembly: EntityDataDeserialize(typeof(GameEntityTorpidity), (int)GameDataConstans.Version)]
#endregion

#region GameCreatureFood
[assembly: Unity.Jobs.RegisterGenericJobType(typeof(EntityDataComponentSerialize<ComponentDataSerializationSystem<GameCreatureFood>.Serializer, ComponentDataSerializationSystem<GameCreatureFood>.SerializerFactory>))]
[assembly: Unity.Jobs.RegisterGenericJobType(typeof(EntityDataComponentDeserialize<ComponentDataDeserializationSystem<GameCreatureFood>.Deserializer, ComponentDataDeserializationSystem<GameCreatureFood>.DeserializerFactory>))]
[assembly: EntityDataSerialize(typeof(GameCreatureFood))]
[assembly: EntityDataDeserialize(typeof(GameCreatureFood), (int)GameDataConstans.Version)]
#endregion

#region GameCreatureWater
[assembly: Unity.Jobs.RegisterGenericJobType(typeof(EntityDataComponentSerialize<ComponentDataSerializationSystem<GameCreatureWater>.Serializer, ComponentDataSerializationSystem<GameCreatureWater>.SerializerFactory>))]
[assembly: Unity.Jobs.RegisterGenericJobType(typeof(EntityDataComponentDeserialize<ComponentDataDeserializationSystem<GameCreatureWater>.Deserializer, ComponentDataDeserializationSystem<GameCreatureWater>.DeserializerFactory>))]
[assembly: EntityDataSerialize(typeof(GameCreatureWater))]
[assembly: EntityDataDeserialize(typeof(GameCreatureWater), (int)GameDataConstans.Version)]
#endregion

#region GameAnimalInfo
[assembly: Unity.Jobs.RegisterGenericJobType(typeof(EntityDataComponentSerialize<ComponentDataSerializationSystem<GameAnimalInfo>.Serializer, ComponentDataSerializationSystem<GameAnimalInfo>.SerializerFactory>))]
[assembly: Unity.Jobs.RegisterGenericJobType(typeof(EntityDataComponentDeserialize<ComponentDataDeserializationSystem<GameAnimalInfo>.Deserializer, ComponentDataDeserializationSystem<GameAnimalInfo>.DeserializerFactory>))]
[assembly: EntityDataSerialize(typeof(GameAnimalInfo))]
[assembly: EntityDataDeserialize(typeof(GameAnimalInfo), (int)GameDataConstans.Version)]
#endregion

#region GameNickname
[assembly: Unity.Jobs.RegisterGenericJobType(typeof(EntityDataComponentSerialize<ComponentDataSerializationSystem<GameNickname>.Serializer, ComponentDataSerializationSystem<GameNickname>.SerializerFactory>))]
[assembly: Unity.Jobs.RegisterGenericJobType(typeof(EntityDataComponentDeserialize<ComponentDataDeserializationSystem<GameNickname>.Deserializer, ComponentDataDeserializationSystem<GameNickname>.DeserializerFactory>))]
[assembly: EntityDataSerialize(typeof(GameNickname))]
[assembly: EntityDataDeserialize(typeof(GameNickname), (int)GameDataConstans.Version)]
#endregion

#region GameVariant
[assembly: Unity.Jobs.RegisterGenericJobType(typeof(EntityDataComponentSerialize<ComponentDataSerializationSystem<GameVariant>.Serializer, ComponentDataSerializationSystem<GameVariant>.SerializerFactory>))]
[assembly: Unity.Jobs.RegisterGenericJobType(typeof(EntityDataComponentDeserialize<ComponentDataDeserializationSystem<GameVariant>.Deserializer, ComponentDataDeserializationSystem<GameVariant>.DeserializerFactory>))]
[assembly: EntityDataSerialize(typeof(GameVariant))]
[assembly: EntityDataDeserialize(typeof(GameVariant), (int)GameDataConstans.Version)]
#endregion

#region GameAIActionHandle
[assembly: Unity.Jobs.RegisterGenericJobType(typeof(EntityDataComponentSerialize<ComponentDataSerializationSystem<GameAIActionHandle>.Serializer, ComponentDataSerializationSystem<GameAIActionHandle>.SerializerFactory>))]
[assembly: Unity.Jobs.RegisterGenericJobType(typeof(EntityDataComponentDeserialize<ComponentDataDeserializationSystem<GameAIActionHandle>.Deserializer, ComponentDataDeserializationSystem<GameAIActionHandle>.DeserializerFactory>))]
[assembly: EntityDataSerialize(typeof(GameAIActionHandle))]
[assembly: EntityDataDeserialize(typeof(GameAIActionHandle), (int)GameDataConstans.Version)]
#endregion

#region GameMoney
[assembly: Unity.Jobs.RegisterGenericJobType(typeof(EntityDataComponentSerialize<ComponentDataSerializationSystem<GameMoney>.Serializer, ComponentDataSerializationSystem<GameMoney>.SerializerFactory>))]
[assembly: Unity.Jobs.RegisterGenericJobType(typeof(EntityDataComponentDeserialize<ComponentDataDeserializationSystem<GameMoney>.Deserializer, ComponentDataDeserializationSystem<GameMoney>.DeserializerFactory>))]
[assembly: EntityDataSerialize(typeof(GameMoney))]
[assembly: EntityDataDeserialize(typeof(GameMoney), (int)GameDataConstans.Version)]
#endregion

#region GamePlayerCreatedDate
[assembly: Unity.Jobs.RegisterGenericJobType(typeof(EntityDataComponentSerialize<ComponentDataSerializationSystem<GamePlayerCreatedDate>.Serializer, ComponentDataSerializationSystem<GamePlayerCreatedDate>.SerializerFactory>))]
[assembly: Unity.Jobs.RegisterGenericJobType(typeof(EntityDataComponentDeserialize<ComponentDataDeserializationSystem<GamePlayerCreatedDate>.Deserializer, ComponentDataDeserializationSystem<GamePlayerCreatedDate>.DeserializerFactory>))]
[assembly: EntityDataSerialize(typeof(GamePlayerCreatedDate))]
[assembly: EntityDataDeserialize(typeof(GamePlayerCreatedDate), (int)GameDataConstans.Version)]
#endregion

#region GamePlayerPlayTime
[assembly: Unity.Jobs.RegisterGenericJobType(typeof(EntityDataComponentSerialize<ComponentDataSerializationSystem<GamePlayerPlayTime>.Serializer, ComponentDataSerializationSystem<GamePlayerPlayTime>.SerializerFactory>))]
[assembly: Unity.Jobs.RegisterGenericJobType(typeof(EntityDataComponentDeserialize<ComponentDataDeserializationSystem<GamePlayerPlayTime>.Deserializer, ComponentDataDeserializationSystem<GamePlayerPlayTime>.DeserializerFactory>))]
[assembly: EntityDataSerialize(typeof(GamePlayerPlayTime))]
[assembly: EntityDataDeserialize(typeof(GamePlayerPlayTime), (int)GameDataConstans.Version)]
#endregion

#region GameValhallaExp
[assembly: Unity.Jobs.RegisterGenericJobType(typeof(EntityDataComponentSerialize<ComponentDataSerializationSystem<GameValhallaExp>.Serializer, ComponentDataSerializationSystem<GameValhallaExp>.SerializerFactory>))]
[assembly: Unity.Jobs.RegisterGenericJobType(typeof(EntityDataComponentDeserialize<ComponentDataDeserializationSystem<GameValhallaExp>.Deserializer, ComponentDataDeserializationSystem<GameValhallaExp>.DeserializerFactory>))]
[assembly: EntityDataSerialize(typeof(GameValhallaExp))]
[assembly: EntityDataDeserialize(typeof(GameValhallaExp), (int)GameDataConstans.Version)]
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